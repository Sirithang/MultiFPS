using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

public class PlayerObject : NetworkBehaviour
{
    public struct PlayerState
    {
        public Vector3 position;
        public Quaternion bodyRotation;
        public Quaternion lookRotation;

        public double networkTime;
        public uint lastInputProcessed;
    }

    public struct PlayerInputState
    {
        public float horizontalValue;
        public float verticalValue;

        public float lookVerticalValue;
        public float lookHorizontalValue;

        public double networkTime;
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

    [SyncVar(hook = "OnSyncPlayerState")]
    protected PlayerState serverState;

    protected PlayerState _currentState;

    protected CharacterController _controller;

    //Input buffer is a ring buffer of input (i.e. when end reach the end, it go back to 0)
    protected FixedRingBuffer<PlayerInputState> _inputBuffer = new FixedRingBuffer<PlayerInputState>(300);
    //server use that to know if it had new input since last update
    protected int _lastInputProcessed;
    protected PlayerInputState[] _sendBuffer = new PlayerInputState[32];
    protected uint _inputIdMax = 0;

    protected float _verticalInputValue;
    protected float _horizontalInputValue;
    protected float _horizontalRotationInputValue;
    protected float _verticalRotationInputValue;

    protected float _positionLerpTime = -1;
    protected float _positionLerpDuration = -1;

    protected ThirdPersonPlayer _thirdPersonPlayerController;

    private void OnEnable()
    {
        _controller = GetComponent<CharacterController>();
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
            _verticalInputValue = Input.GetAxis("Vertical");
            _horizontalInputValue = Input.GetAxis("Horizontal");

            _horizontalRotationInputValue = Input.GetAxis("Mouse X");
            _verticalRotationInputValue = Input.GetAxis("Mouse Y");

            if(Input.GetKeyDown(KeyCode.Escape))
            {
                if (Cursor.lockState == CursorLockMode.Locked)
                    Cursor.lockState = CursorLockMode.None;
                else
                    Cursor.lockState = CursorLockMode.Locked;
            }
        }
    }

    private void FixedUpdate()
    {
        if (!isLocalPlayer)
        {//non local player will just lerp toward server state from local state
            if (isServer)
            { //the server on physic update will just process the unprocessed input.

                if (_lastInputProcessed != _inputBuffer.end)
                {
                    while (_lastInputProcessed != _inputBuffer.end)
                    {
                        ApplyInput(_inputBuffer.data[_lastInputProcessed]);

                        _currentState.lastInputProcessed = _inputBuffer.data[_lastInputProcessed].inputID;

                        _lastInputProcessed = (_lastInputProcessed + 1) % _inputBuffer.data.Length;
                    }

                    SendUpdatedState();
                }
            } 
        }
        else
        {
            PlayerInputState newState = new PlayerInputState();
            CreateInputState(ref newState);

            ApplyInput(newState);

            _inputBuffer.AddValue(newState);
            CmdReceiveInput(newState);

            if(isServer)
            {//this is a selfhosting server, we need to send the synchronized value to other
                _currentState.lastInputProcessed = newState.inputID;
                SendUpdatedState();
            }
        }
    }

    void SendUpdatedState()
    {
        _currentState.position = transform.position;
        _currentState.bodyRotation = transform.rotation;
        _currentState.lookRotation = cameraPosition.localRotation;
        _currentState.networkTime = Network.time;

        serverState = _currentState;
    }

    bool CreateInputState(ref PlayerInputState state)
    {
        state.horizontalValue = _horizontalInputValue;
        state.verticalValue = _verticalInputValue;

        state.lookHorizontalValue = _horizontalRotationInputValue;
        state.lookVerticalValue = _verticalRotationInputValue;

        state.inputID = _inputIdMax++;
        state.networkTime = Network.time;

        return true;
    }

    void OnSyncPlayerState(PlayerState val)
    {
        if (isLocalPlayer)
        {
            uint diff = _inputIdMax - 1 - val.lastInputProcessed;

            //move to the position sync from server
            transform.position = val.position;
            transform.rotation = val.bodyRotation;
            cameraPosition.localRotation = val.lookRotation;

            if (diff > 0)
            {
                int startIdx = _inputBuffer.GetIndex(-(int)diff);
                while (startIdx != _inputBuffer.end)
                {
                    ApplyInput(_inputBuffer.data[startIdx]);
                    startIdx = (startIdx + 1) % _inputBuffer.data.Length;
                }
            }
        }

        _currentState = serverState;
        serverState = val;

        if (!isLocalPlayer && _currentState.networkTime > 0.1f)
        { //this ensure it's at least the 2nd we received, as the 1st sync, _currentState.networkTime == 0
            _positionLerpDuration = (float)(serverState.networkTime - _currentState.networkTime);
            _positionLerpTime = 0;
        }
    }


    void ApplyInput(PlayerInputState input)
    {
        transform.Rotate(Vector3.up, input.lookHorizontalValue * horizontalRotateSpeed * Time.fixedDeltaTime, Space.Self);

        cameraPosition.transform.Rotate(Vector3.right, input.lookVerticalValue * -verticalRotateSpeed * Time.fixedDeltaTime, Space.Self);

        _controller.Move( (transform.forward * input.verticalValue + transform.right * input.horizontalValue) * Time.fixedDeltaTime * speed);
    }

    //Call by the owning player to send their cached input to the server
    [Command]
    void CmdReceiveInput(PlayerInputState input)
    {
        _inputBuffer.AddValue(input);
    }

    public override void OnStartLocalPlayer()
    {
        Cursor.lockState = CursorLockMode.Locked;
    }

    //public override void OnStartClient()
    //{
    //    if(!isLocalPlayer)
    //    {
    //        var tp = Instantiate(thirdPersonPrefab, cameraPosition.transform);
    //        tp.transform.localPosition = Vector3.zero;
    //        tp.transform.localRotation = Quaternion.identity;
    //    }
    //}

}
