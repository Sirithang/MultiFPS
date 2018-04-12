using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

public class ServerSimulation : NetworkBehaviour
{
    public struct RecordedData
    {
        public Collider collider;

        public Vector3 position;
        public Quaternion rotation;
        public uint serverFrame;
    }

    public struct FrameData
    {
        public uint serverFrame;
        public RecordedData[] data;
    }

    [System.Serializable]
    public struct ServerState
    {
        public float sinceLastTick;
        public uint frameNumber;
        public double networkTime;
    }

    public static float serverTimestep = 0.3f;
    public static uint frameNumber {  get { return _instance._serverState.frameNumber; } }

    protected static ServerSimulation _instance;

    public List<IServerUpdate> serverTicked = new List<IServerUpdate>();
    public List<Collider> trackedColliders = new List<Collider>();

    protected FixedRingBuffer<FrameData> _framesData = new FixedRingBuffer<FrameData>(60);

    [SyncVar(hook = "SyncServerState")]
    ServerState _serverState;


    private void Awake()
    {
        _instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        if (isServer)
        {
            _serverState = new ServerState
            {
                frameNumber = 0,
                networkTime = Network.time,
                sinceLastTick = 0
            };
        }
    }

    public static void RegisterObject(IServerUpdate obj)
    {
        _instance.serverTicked.Add(obj);
    }

    public static void UnregisterObject(IServerUpdate obj)
    {
        _instance.serverTicked.Remove(obj);
    }

    public static void StartTrackingCollider(Collider c)
    {
        _instance.trackedColliders.Add(c);
    }

    public static void StopTrackingCollider(Collider c)
    {
        _instance.trackedColliders.Remove(c);
    }

    private void Update()
    {
        UpdateServerState();
    }

    void UpdateServerState()
    {
        _serverState.sinceLastTick += Time.deltaTime;
        if (isServer)
            _serverState.networkTime = Network.time;

        while (_serverState.sinceLastTick > serverTimestep)
        {
            if (isServer)
                Tick();

            _serverState.frameNumber += 1;
            _serverState.sinceLastTick -= serverTimestep;
        }
    }

    void SyncServerState(ServerState newState)
    {
        _serverState = newState;

        //we add the time it took the message to reach the client so it catch up ant missing frame
        _serverState.sinceLastTick += (float)(Network.time - newState.networkTime);

        UpdateServerState();
    }

    public static void Rewind(uint frame)
    {
        _instance.RewindTo(frame);
    }

    void RewindTo(uint serverFrame)
    {
        uint lastDataFrame = _framesData.data[_framesData.GetIndex(-1)].serverFrame;
        int difference = (int)(lastDataFrame - serverFrame);

        FrameData data = _framesData.data[_framesData.GetIndex(-difference-1)];

        for(int i = 0; i < data.data.Length; ++i)
        {
            if(data.data[i].collider != null)
            {
                if(data.data[i].collider.attachedRigidbody != null)
                {
                    data.data[i].collider.attachedRigidbody.position = data.data[i].position;
                    data.data[i].collider.attachedRigidbody.rotation = data.data[i].rotation;
                }
                else
                {
                    data.data[i].collider.transform.position = data.data[i].position;
                    data.data[i].collider.transform.rotation = data.data[i].rotation;
                }
            }
        }

        Physics.autoSimulation = false;
        Physics.Simulate(0);
        Physics.autoSimulation = true;
    }

    void Tick()
    {
        for(int i = 0; i < serverTicked.Count; ++i)
        {
            serverTicked[i].Tick();
        }

        FrameData data = new FrameData();
        data.serverFrame = frameNumber;
        data.data = new RecordedData[trackedColliders.Count];
        for (int i = 0; i < trackedColliders.Count; ++i)
        {
            data.data[i].collider = trackedColliders[i];
            data.data[i].position = trackedColliders[i].transform.position;
            data.data[i].rotation = trackedColliders[i].transform.rotation;

        }

        _framesData.AddValue(data);
    }
}

public interface IServerUpdate
{
    void Tick();
}