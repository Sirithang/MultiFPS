using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

public class ServerSimulation : NetworkBehaviour
{
    public struct RecordedData
    {
        public Collider body;

        public Vector3 position;
        public Quaternion rotation;
        public uint serverFrame;
    }

    public struct FrameData
    {
        public uint serverFrame;
        public RecordedData[] data;
    }

    public static float serverTimestep = 0.3f;
    public static uint frameNumber {  get { return _instance._frameNumber; } }

    protected static ServerSimulation _instance;

    public List<IServerUpdate> serverTicked = new List<IServerUpdate>();
    public List<Collider> trackedColliders = new List<Collider>();

    protected FixedRingBuffer<FrameData> _framesData = new FixedRingBuffer<FrameData>(60);

    protected float _sinceLastUpdate;

    //synced data
    [SyncVar]
    uint _frameNumber;


    private void Awake()
    {
        _instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        _sinceLastUpdate = 0.0f;
        _frameNumber = 0;
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
        _sinceLastUpdate += Time.deltaTime;

        while(_sinceLastUpdate > serverTimestep)
        {
            if(isServer)
                Tick();

            _frameNumber += 1;
            _sinceLastUpdate -= serverTimestep;
        }
    }

    void RewindTo(uint serverFrame)
    {
        //nt offset = 
    }

    void Tick()
    {
        for(int i = 0; i < serverTicked.Count; ++i)
        {
            serverTicked[i].Tick();
        }
    }
}

public interface IServerUpdate
{
    void Tick();
}