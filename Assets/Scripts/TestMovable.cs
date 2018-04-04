using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.AI;

//Very basic movable used to test lag compensation
[NetworkSettings(channel = Channels.DefaultUnreliable)]
public class TestMovable : NetworkBehaviour, IServerUpdate
{
    public struct MovableState
    {
        public Vector3 position;
        public Quaternion rotation;
        public uint serverFrame;
    }

    [SyncVar(hook = "OnStateSync")]
    MovableState _serverState;
    MovableState _previousState;

    protected NavMeshAgent _navmeshAgent;
    protected float _sinceLastPick = 0.0f;

    protected float _interpolationTime = 0.0f;
    protected float _interpolationDuration = 0.0f;

    [ServerCallback]
    private void OnEnable()
    {
        _navmeshAgent = GetComponent<NavMeshAgent>();
        ServerSimulation.RegisterObject(this);
    }

    [ServerCallback]
    private void OnDisable()
    {
        ServerSimulation.UnregisterObject(this);
    }

    [ServerCallback]
    private void Start()
    {
        _sinceLastPick = 0.0f;
    }

    void Update()
    {
        if (isServer)
            return;

        // maybe remvoe that check, interpolatind between empty data
        if(_interpolationDuration > 0.0f)
        {
            _interpolationTime = Mathf.Clamp(_interpolationTime + Time.deltaTime, 0, _interpolationDuration);
            float ratio = _interpolationTime / _interpolationDuration;

            transform.position = Vector3.Lerp(_previousState.position, _serverState.position, ratio);
            transform.rotation = Quaternion.Lerp(_previousState.rotation, _serverState.rotation, ratio);
        }
    }

    void OnStateSync(MovableState newState)
    {
        _previousState = _serverState;
        _serverState = newState;

        if(_previousState.serverFrame > 0)
        {
            _interpolationTime = 0.0f;
            _interpolationDuration = (_serverState.serverFrame - _previousState.serverFrame) * ServerSimulation.serverTimestep;
        }
    }

    public void Tick()
    {
        _sinceLastPick += ServerSimulation.serverTimestep;

        if(_sinceLastPick > 4.0f || _navmeshAgent.remainingDistance < 0.01f)
        {
            Vector2 direction = Random.insideUnitCircle * 5.0f;
            _navmeshAgent.SetDestination(transform.position + new Vector3(direction.x, 0, direction.y));
            _sinceLastPick = 0.0f;
        }

        _previousState.position = transform.position;
        _previousState.rotation = transform.rotation;
        _previousState.serverFrame = ServerSimulation.frameNumber;

        _serverState = _previousState;
    }
}
