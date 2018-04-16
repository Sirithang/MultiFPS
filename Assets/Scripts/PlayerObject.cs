using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

[NetworkSettings(channel = Channels.DefaultUnreliable)]
public class PlayerObject : NetworkBehaviour, IServerUpdate
{
    public struct PlayerState
    {
        public Vector3 position;
        public Vector3 velocity;
        public Quaternion bodyRotation;
        public Quaternion lookRotation;

        public double networkTime;
        public uint serverFrame;
        public uint lastInputProcessed;
    }

    public struct PlayerInputState
    {
        public float horizontalValue;
        public float verticalValue;

        public bool jumpPressed;
        public bool shootingPressed;

        public float lookVerticalValue;
        public float lookHorizontalValue;

        public double networkTime;
        public uint serverFrame;
        public uint inputID;
    }

    public float speed = 3.0f;
    public float horizontalRotateSpeed = 90.0f;
    public float verticalRotateSpeed = 45.0f;

    [Header("References")]
    public Transform cameraPosition;
    public Transform thirdPersonPosition;

    public GameObject firstpersonPrefab;
    public GameObject thirdPersonPrefab;

    [Header("TEMP REF")]
    public GameObject hitPrefab;

    [SyncVar(hook = "OnSyncPlayerState")]
    protected PlayerState serverState;
    protected PlayerState _currentState;

    protected CharacterController _controller;

    //Input buffer is a ring buffer of input (i.e. when end reach the end, it go back to 0)
    protected FixedRingBuffer<PlayerInputState> _inputBuffer = new FixedRingBuffer<PlayerInputState>(300);
    protected FixedRingBuffer<Vector3> _positions = new FixedRingBuffer<Vector3>(300);

    //server use that to know if it had new input since last update
    protected uint _lastInputProcessed;
    protected Queue<PlayerInputState> _unsentInput = new Queue<PlayerInputState>(30);
    protected uint _inputIdMax = 0;

    protected PlayerInputState _localInput;
    protected Vector3 _currentVelocity = Vector3.zero;

    protected float _positionLerpTime = -1;
    protected float _positionLerpDuration = -1;

    protected double _currentLatency = 0.0f;

    protected Collider _collider;

    protected ThirdPersonPlayer _thirdPersonPlayerController;

    private void OnEnable()
    {
        _controller = GetComponent<CharacterController>();
        _collider = GetComponent<Collider>();

        ServerSimulation.StartTrackingCollider(_collider);
    }

    private void OnDisable()
    {
        ServerSimulation.StopTrackingCollider(_collider);
    }

    private void Start()
    {
        if (!isLocalPlayer)
        {
            var tp = Instantiate(thirdPersonPrefab, thirdPersonPosition);
            tp.transform.localPosition = Vector3.zero;
            tp.transform.localRotation = Quaternion.identity;
            _thirdPersonPlayerController = tp.GetComponentInChildren<ThirdPersonPlayer>();
        }
        else
        {
            var fp = Instantiate(firstpersonPrefab, cameraPosition);
            fp.transform.localPosition = Vector3.zero;
            fp.transform.localRotation = Quaternion.identity;
        }
    }

    public void Tick()
    {
        if (isLocalPlayer)
        {
            _currentState.lastInputProcessed = _localInput.inputID;
            SendUpdatedState();
            return;
        }

        if (_unsentInput.Count > 0 && _lastInputProcessed < _unsentInput.Peek().inputID)
        {
            while (_unsentInput.Count > 0 && _lastInputProcessed < _unsentInput.Peek().inputID)
            {
                var state = _unsentInput.Dequeue();

                ApplyInput(state);

                _currentState.lastInputProcessed = state.inputID;
                _lastInputProcessed = _currentState.lastInputProcessed;
            }
        }

        SendUpdatedState();
    }

    private void Update()
    {
        if (!isLocalPlayer && _positionLerpDuration > 0)
        {
            _positionLerpTime = Mathf.Clamp(_positionLerpTime + Time.deltaTime, 0, _positionLerpDuration);
            float ratio = _positionLerpTime / _positionLerpDuration;

            transform.position = Vector3.Lerp(_currentState.position, serverState.position, ratio);
            transform.rotation = Quaternion.Lerp(_currentState.bodyRotation, serverState.bodyRotation, ratio);
            _thirdPersonPlayerController.weaponTransform.localRotation = Quaternion.Lerp(_currentState.lookRotation, serverState.lookRotation, ratio);

            return;
        }
        
        if(isLocalPlayer)
        {
            _localInput.verticalValue = Input.GetAxis("Vertical");
            _localInput.horizontalValue = Input.GetAxis("Horizontal");

            _localInput.lookHorizontalValue = Input.GetAxis("Mouse X");
            _localInput.lookVerticalValue = Input.GetAxis("Mouse Y");

            _localInput.jumpPressed = Input.GetButton("Jump");

            if(Input.GetKeyDown(KeyCode.Escape))
            {
                if (Cursor.lockState == CursorLockMode.Locked)
                    Cursor.lockState = CursorLockMode.None;
                else
                    Cursor.lockState = CursorLockMode.Locked;
            }

            _localInput.shootingPressed = Input.GetButtonDown("Fire1");
        }
    }

    private void FixedUpdate()
    {
        if(isLocalPlayer)
        {
            CreateInputState(ref _localInput);

            ApplyInput(_localInput);

            _inputBuffer.AddValue(_localInput);

            if (_unsentInput.Count == 30)
                _unsentInput.Dequeue();

            _unsentInput.Enqueue(_localInput);

            _positions.AddValue(transform.position);

            CmdReceiveInput(_unsentInput.ToArray(), _unsentInput.Count, Network.time);
        }
    }

    void SendUpdatedState()
    {
        _currentState.position = transform.position;
        _currentState.bodyRotation = transform.rotation;
        _currentState.lookRotation = cameraPosition.localRotation;
        _currentState.velocity = _currentVelocity;

        _currentState.networkTime = Network.time;
        _currentState.serverFrame = ServerSimulation.frameNumber;

        serverState = _currentState;
    }

    void CreateInputState(ref PlayerInputState state)
    {
        state.inputID = _inputIdMax++;
        state.networkTime = Network.time;
        state.serverFrame = ServerSimulation.frameNumber;
    }

    void Shoot(PlayerInputState input)
    {
        Vector3 origin = cameraPosition.position;
        Vector3 direction = cameraPosition.forward;

        if (isServer && input.serverFrame != ServerSimulation.frameNumber)
            ServerSimulation.Rewind(input.networkTime - _currentLatency);

        RaycastHit hit;
        if(Physics.Raycast(origin, direction, out hit, 1000))
        {
            var instance = Instantiate(hitPrefab, hit.collider.transform, false);
            instance.transform.position = hit.point;
            instance.transform.forward = hit.normal;
        }

        Debug.DrawRay(origin, direction * 1000);
        Debug.Break();
    }

    void OnSyncPlayerState(PlayerState val)
    {
        if (isLocalPlayer)
        {
            while (_unsentInput.Count > 0 && _unsentInput.Peek().inputID <= val.lastInputProcessed)
                _unsentInput.Dequeue();

            uint diff = _inputIdMax - 1 - val.lastInputProcessed;

            Vector3 originPos = transform.position;
            Quaternion originBodyRotation = transform.rotation;
            Quaternion originLookRotation = cameraPosition.localRotation;

            //move to the position sync from server
            transform.position = val.position;
            transform.rotation = val.bodyRotation;
            cameraPosition.localRotation = val.lookRotation;

            _currentVelocity = val.velocity;

            if (diff > 0)
            {
                int startIdx = _inputBuffer.GetIndex(-(int)diff);

                int lastProcessedIdx = _inputBuffer.GetIndex(-(int)(diff + 1));

                while (startIdx != _inputBuffer.end)
                {
                    ApplyInput(_inputBuffer.data[startIdx]);
                    startIdx = (startIdx + 1) % _inputBuffer.data.Length;
                }

                //TODO : save the offset between the resulting server reconciliation and previous position to slowly add over time to current pos.
                // Could help remove the snapping in case of drift with a lerping
            }
        }

        _currentState = serverState;
        serverState = val;

        if (!isLocalPlayer && _currentState.serverFrame > 0)
        {
            //this ensure it's at least the 2nd we received, as the 1st sync, _currentState.networkTime == 0
            //_positionLerpDuration = Mathf.Max(1, (serverState.serverFrame - _currentState.serverFrame)) * ServerSimulation.serverTimestep;
            _positionLerpDuration = (float)(serverState.networkTime - _currentState.networkTime);
            _positionLerpTime = 0;
        }
    }


    void ApplyInput(PlayerInputState input)
    {
        transform.Rotate(Vector3.up, input.lookHorizontalValue * horizontalRotateSpeed * Time.fixedDeltaTime, Space.Self);

        cameraPosition.transform.Rotate(Vector3.right, input.lookVerticalValue * -verticalRotateSpeed * Time.fixedDeltaTime, Space.Self);

        float frictionFactor;
        if(_controller.isGrounded)
        {
            if (input.jumpPressed)
            {
                _currentVelocity.y = 3.0f;
            }
        }
        else
        {

        }

        _currentVelocity.y -= 5.0f * Time.fixedDeltaTime;

        _currentVelocity.x = 0;
        _currentVelocity.z = 0;
        _currentVelocity += transform.forward * input.verticalValue + transform.right * input.horizontalValue;

        float horizontalMagnitude = Mathf.Sqrt(_currentVelocity.x * _currentVelocity.x) + (_currentVelocity.z * _currentVelocity.z);
        if(horizontalMagnitude > speed)
        {
            _currentVelocity.x = _currentVelocity.x / horizontalMagnitude * speed;
            _currentVelocity.z = _currentVelocity.z / horizontalMagnitude * speed;
        }

        var result = _controller.Move(_currentVelocity * Time.fixedDeltaTime * speed);

        if((result & CollisionFlags.Below) != 0)
        {
            if (_currentVelocity.y < 0)
                _currentVelocity.y = 0.0f;
        }

        //-- shooting
        if(input.shootingPressed)
            Shoot(input);
    }

    //Call by the owning player to send their cached input to the server
    [Command(channel = Channels.DefaultUnreliable)]
    void CmdReceiveInput(PlayerInputState[] input, int count, double networkTime)
    {
        _currentLatency = (Network.time - networkTime);

        _unsentInput.Clear();
        for (int i = 0; i < count; ++i)
        {
            if (input[i].inputID <= _lastInputProcessed)
                continue; //this is  a duplicate, ignore it

            _unsentInput.Enqueue(input[i]);
        }
    }

    [Command(channel = Channels.DefaultReliable)]
    void CmdFire(PlayerInputState[] input, int count)
    {

    }

    public override void OnStartLocalPlayer()
    {
        Cursor.lockState = CursorLockMode.Locked;
    }

    public override void OnStartServer()
    {
        ServerSimulation.RegisterObject(this);
    }

    private void OnDestroy()
    {
        ServerSimulation.UnregisterObject(this);
    }

    void OnGUI()
    {
        GUILayout.Label($"RTT : {NetworkManager.singleton.client.GetRTT()}");
    }

}
