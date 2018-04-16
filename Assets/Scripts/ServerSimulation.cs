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
        public double networkTime;
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

    protected Queue<int> _freeIdx = new Queue<int>();
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
        if(_instance._freeIdx.Count > 0)
        {
            int index = _instance._freeIdx.Dequeue();
            _instance.trackedColliders[index] = c;
        }
        else
            _instance.trackedColliders.Add(c);
    }

    public static void StopTrackingCollider(Collider c)
    {
        //we never remove from the list, we reuse slot that were freed, allow to keep a match between frame data (same collider always at the same index)

        //TODO : change that to store in the colliders their index, will avoid a costy search
        int idx = _instance.trackedColliders.FindIndex(coll => { return coll == c; });

        _instance._freeIdx.Enqueue(idx);
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

    public static void Rewind(double networkTime)
    {
        _instance.RewindTo(networkTime);
    }

    void RewindTo(double networkTime)
    {
        int frameDiff = Mathf.FloorToInt((float)(Network.time - networkTime) / serverTimestep) + 1;

        if (frameDiff == 1)
            return; //we can't really interpolate between the previous frame and the frame currently being ticked, so just exit (TODO : use last frame data instead of current frame data)

        var data = _framesData.data[_framesData.GetIndex(-frameDiff)];
        var next = _framesData.data[_framesData.GetIndex(-frameDiff + 1)];

        float ratio = (float)((networkTime - data.networkTime) / (next.networkTime - data.networkTime));

        Debug.Log($"Interpolting between frame {data.serverFrame}:{data.networkTime} and frame {next.serverFrame}:{next.networkTime} for asked time {networkTime} and an interp ratio of {ratio}");


        for (int i = 0; i < data.data.Length; ++i)
         {
            if (data.data[i].collider != null)
            {
                Vector3 originPosition = data.data[i].position;
                Quaternion originRotation = data.data[i].rotation;

                if(next.data.Length > i && next.data[i].collider == data.data[i].collider)
                {
                    originPosition = Vector3.Lerp(originPosition, next.data[i].position, ratio);
                    originRotation = Quaternion.Lerp(originRotation, next.data[i].rotation, ratio);
                }

                if (data.data[i].collider.attachedRigidbody != null)
                {
                    Debug.DrawLine(data.data[i].collider.attachedRigidbody.position + Vector3.up * 0.5f, data.data[i].collider.attachedRigidbody.position - Vector3.up * 0.5f, Color.blue);
                    Debug.DrawLine(data.data[i].collider.attachedRigidbody.position + Vector3.forward * 0.5f, data.data[i].collider.attachedRigidbody.position - Vector3.forward * 0.5f, Color.blue);

                    data.data[i].collider.attachedRigidbody.position = originPosition;
                    data.data[i].collider.attachedRigidbody.rotation = originRotation;
                }
                else
                {
                    Debug.DrawLine(data.data[i].collider.transform.position + Vector3.up * 0.5f, data.data[i].collider.transform.position - Vector3.up * 0.5f, Color.red);
                    Debug.DrawLine(data.data[i].collider.transform.position + Vector3.forward * 0.5f, data.data[i].collider.transform.position - Vector3.forward * 0.5f, Color.red);

                    data.data[i].collider.transform.position = originPosition;
                    data.data[i].collider.transform.rotation = originRotation;
                }
            }
        }

        Physics.autoSimulation = false;
        Physics.Simulate(0.00001f);
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
        data.networkTime = Network.time;
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