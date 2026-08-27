// ==========================================
// Assets/Game/Phase4Bootstrapper.cs
//
// §13 Phase 4 scene wiring: "Phase4_Stream -- island, fly capped at 60 m/s,
// HUD showing pool occupancies + upload ms + memory."
//
// Sibling of Phase3Bootstrapper. Differences, all consequences of streaming:
//   1. NO eager world generation. Phase 3 generated all 484 chunks inside
//      Start(); StreamManager now admits chunks around the camera and the
//      world is unbounded in XZ, which is what resolves PHASE_3_COMPLETION.md
//      §7 issues 1, 2 and 7.
//   2. StreamManager.Update() is driven every frame from LateUpdate, after the
//      camera has moved, so admission sees this frame's position rather than
//      last frame's.
//   3. The initial window is filled synchronously via WaitForIdle so the
//      acceptance rig starts from a known-populated state instead of racing
//      the prefetcher.
using System.Diagnostics;
using System.IO;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Memory;
using VoxelEngine.Mirror;
using VoxelEngine.Streaming;
using VoxelEngine.WorldGen;

public class Phase4Bootstrapper : MonoBehaviour
{
    public static TerrainClipmap Clipmap { get; private set; }
    public static ChunkStore Store { get; private set; }
    public static BrickDataPool Pool { get; private set; }
    public static LODCascadeManager Cascades { get; private set; }
    public static WorldMetaData Meta { get; private set; }
    public static StreamManager Streamer { get; private set; }
    public static CoalesceScheduler Coalescer { get; private set; }
    public static string DeltaDirectory { get; private set; }
    public static string MetaRoundtripStatus { get; private set; }
    public static double StartupMs { get; private set; }

    public const uint WORLD_SEED = 42;
    public const byte SIZE_CLASS = 0;

    [Header("Streaming")]
    [Tooltip("Chunks loaded around the camera. 15 x 12.8m = 192m, just inside the window half-width (204.8m) so the eviction ring still fits.")]
    [SerializeField] private int _loadRadiusChunks = 15;
    [Tooltip("Block on Start until the initial window is fully resident. Off = watch it stream in.")]
    [SerializeField] private bool _fillWindowOnStart = true;
    [Tooltip("Wipe the delta directory on boot. ON for repeatable acceptance runs; OFF to test persistence across launches.")]
    [SerializeField] private bool _clearDeltasOnStart = true;

    [Header("Camera spawn")]
    [SerializeField] private bool _overrideCameraOnStart = true;
    [SerializeField] private Vector3 _cameraSpawnPosition = new Vector3(140.8f, 12f, 140.8f);
    [SerializeField] private Vector3 _cameraSpawnEuler = new Vector3(10f, 0f, 0f);

    private BrickDataPool _pool;
    private ChunkHandleAllocator _allocator;
    private ChunkStore _store;

    void Start()
    {
        var total = Stopwatch.StartNew();
        var sw = Stopwatch.StartNew();

        // ---- Stage 1 (§5.2): plan, persist, read back, verify. Unchanged
        // from Phase 3 -- generation still consumes the FILE, not the plan.
        WorldMetaData planned = AnchorPlanner.Plan(WORLD_SEED, SIZE_CLASS);
        string worldDir = Path.Combine(Application.persistentDataPath, "Phase4World");
        string metaPath = Path.Combine(worldDir, "world.meta");
        WorldMeta.WriteAtomic(metaPath, planned);

        if (!WorldMeta.TryRead(metaPath, out WorldMetaData readBack))
        {
            MetaRoundtripStatus = $"FAIL: world.meta at {metaPath} could not be read back (CRC/format).";
            UnityEngine.Debug.LogError("[Phase4Bootstrapper] " + MetaRoundtripStatus);
            return;
        }

        byte[] a = WorldMeta.Serialize(planned);
        byte[] b = WorldMeta.Serialize(readBack);
        bool identical = a.Length == b.Length;
        if (identical)
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) { identical = false; break; }

        if (!identical)
        {
            MetaRoundtripStatus = "FAIL: read-back world.meta serializes differently from the in-memory plan.";
            UnityEngine.Debug.LogError("[Phase4Bootstrapper] " + MetaRoundtripStatus);
            return;
        }

        MetaRoundtripStatus = $"PASS: world.meta written+read-back byte-identical ({a.Length} bytes, " +
                              $"{readBack.anchors.Length} anchors, {readBack.biomeSeeds.Length} biome seeds)";
        Meta = readBack;
        UnityEngine.Debug.Log($"[Phase4Bootstrapper] Stage 1: {sw.ElapsedMilliseconds}ms. {MetaRoundtripStatus}");

        // ---- Delta directory ----
        DeltaDirectory = Path.Combine(worldDir, "deltas");
        Directory.CreateDirectory(DeltaDirectory);
        if (_clearDeltasOnStart)
        {
            foreach (string f in Directory.GetFiles(DeltaDirectory, "*.delta")) File.Delete(f);
            foreach (string f in Directory.GetFiles(DeltaDirectory, "*.delta.tmp")) File.Delete(f);
        }

        // ---- Pools / store / mirrors ----
        _pool = new BrickDataPool(EngineConfig.BRICK_POOL_CAP);
        _allocator = new ChunkHandleAllocator(1024);
        _store = new ChunkStore(_pool, _allocator);
        Store = _store; Pool = _pool;

        int3 windowChunks = new int3(EngineConfig.WINDOW_CHUNKS_XZ, EngineConfig.WINDOW_CHUNKS_Y, EngineConfig.WINDOW_CHUNKS_XZ);
        Clipmap = new TerrainClipmap(windowChunks, _pool.Capacity);
        Cascades = new LODCascadeManager(windowChunks, tier => LODCascadeManager.DefaultTierPoolCapacity(EngineConfig.BRICK_POOL_CAP));

        // The shader's content-ceiling early exit, driven from config rather
        // than the hardcoded 128 it used through Phase 3.
        RaymarchFeature.ContentCeilingVoxelY = (StreamManager.MAX_GENERATED_CHUNK_Y + 1) * EngineConfig.CHUNK_EDGE_VOXELS;

        Streamer = new StreamManager(_store, _pool, _allocator, Clipmap, Cascades,
                                     Meta, DeltaDirectory, _loadRadiusChunks);
        Coalescer = new CoalesceScheduler(_store, _pool, Clipmap, Cascades);

        if (_overrideCameraOnStart && Camera.main != null)
        {
            Camera.main.transform.position = _cameraSpawnPosition;
            Camera.main.transform.rotation = Quaternion.Euler(_cameraSpawnEuler);
        }

        // ---- Fill the initial window ----
        sw.Restart();
        Vector3 camPos = Camera.main != null ? Camera.main.transform.position : _cameraSpawnPosition;
        Streamer.Update(camPos);
        if (_fillWindowOnStart)
        {
            Streamer.WaitForIdle();
            // Uploads are budgeted per frame; startup deliberately bypasses the
            // budget so the rig does not begin measuring against a half-uploaded
            // window. This is the one place the cap is intentionally ignored.
            Clipmap.UploadDirty(_store, _pool);
            Cascades.UploadDirty(_store, _pool);
        }
        UnityEngine.Debug.Log($"[Phase4Bootstrapper] Initial window fill: {sw.ElapsedMilliseconds}ms, " +
                              $"{_store.ResidentCount} chunks resident, pool {_store.PoolUtilisation:P1}");

        StartupMs = total.Elapsed.TotalMilliseconds;
        UnityEngine.Debug.Log($"[Phase4Bootstrapper] Total startup: {StartupMs:F0}ms");
    }

    void LateUpdate()
    {
        if (Streamer == null) return;
        Vector3 camPos = Camera.main != null ? Camera.main.transform.position : transform.position;
        Streamer.Update(camPos);
        Coalescer.Update();
    }

    void OnApplicationQuit()
    {
        // Clean shutdown flushes pending edits. The rig's force-quit test
        // deliberately bypasses this path (Process.Kill) -- that is the point.
        if (Streamer != null)
        {
            int n = Streamer.FlushAllDirty();
            if (n > 0) UnityEngine.Debug.Log($"[Phase4Bootstrapper] Flushed {n} dirty chunks on quit.");
        }
    }

    void OnDestroy()
    {
        Streamer?.Dispose();
        Clipmap?.Dispose();
        Cascades?.Dispose();
        _pool?.Dispose();
        Clipmap = null; Cascades = null; Store = null; Pool = null; Meta = null;
        Streamer = null; Coalescer = null; MetaRoundtripStatus = null;
    }
}