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
    public const byte SIZE_CLASS = 1;   // §2.4 "Small": 2560m, 200x200 chunks

    [Header("Streaming")]
    [Tooltip("Chunks loaded around the camera. 0 = derive the maximum the window allows (recommended). " +
             "Any value above the maximum is clamped with a warning rather than silently accepted.")]
    [SerializeField] private int _loadRadiusChunks = 0;
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

        // The STORE's ring and the GPU MIRROR's window are deliberately
        // different heights. WINDOW_CHUNKS_Y=16 exists for the raymarch oracle
        // tests, which build 16-chunk-tall synthetic worlds in their own
        // ChunkStore. The mirror only ever has to cover chunk layers generation
        // can produce, so it uses MIRROR_CHUNKS_Y=4 and saves 200 MB plus a 4x
        // reduction in the per-frame AirMip.Pack scan (see EngineConfig).
        //
        // Both are powers of two and both mask cy=0 to slot 0, so the two rings
        // agree for every chunk that actually exists. If generation ever grows
        // vertically, MIRROR_CHUNKS_Y must grow with MAX_GENERATED_CHUNK_Y --
        // StreamManager asserts that the streamed range fits its window.
        int3 storeWindowChunks = _store.WindowDims;
        int3 mirrorChunks = new int3(EngineConfig.WINDOW_CHUNKS_XZ, EngineConfig.MIRROR_CHUNKS_Y, EngineConfig.WINDOW_CHUNKS_XZ);
        Clipmap = new TerrainClipmap(mirrorChunks, _pool.Capacity);
        Cascades = new LODCascadeManager(mirrorChunks, tier => LODCascadeManager.DefaultTierPoolCapacity(EngineConfig.BRICK_POOL_CAP));

        UnityEngine.Debug.Log($"[Phase4Bootstrapper] store ring {storeWindowChunks}, GPU mirror {mirrorChunks} " +
                              $"(camera altitude ceiling {EngineConfig.MIRROR_CEILING_METRES:F1}m -- see EngineConfig)");

        // The shader's content-ceiling early exit, driven from config rather
        // than the hardcoded 128 it used through Phase 3.
        RaymarchFeature.ContentCeilingVoxelY = (StreamManager.MAX_GENERATED_CHUNK_Y + 1) * EngineConfig.CHUNK_EDGE_VOXELS;

        // DERIVE the load radius rather than trusting a hand-picked number.
        //
        // This is here because the first Phase 4 run died on StreamManager's own
        // assert: the inspector default was 15, sized against the window's
        // half-width, but the EVICT radius is what has to fit and it always sits
        // HYSTERESIS_RING_CHUNKS (2) further out -- 17 against a maximum of 15.
        // The comment justifying 15 did the metres arithmetic and simply never
        // added the ring.
        //
        // COVERAGE CONSEQUENCE, worth knowing before reading Gate C's captures:
        // at load radius 13 terrain exists to 166.4m axis-aligned and ~235m
        // diagonally, while LODConfig's tier-2 outer bound is 290m. Rays past
        // the loaded region hit the window bound and terminate as sky. That is
        // the correct behaviour of a 32-chunk window with a 2-chunk ring, NOT a
        // regression in the bounds guard or the cascade. Widening it means
        // either WINDOW_CHUNKS_XZ 32->64 (clipmap 268MB -> 1.07GB, no) or
        // HYSTERESIS_RING_CHUNKS 2->1 (+12.8m). Measure before doing either.
        int maxRadius = StreamManager.MaxLoadRadiusChunks(_store.WindowDims);
        int loadRadius = _loadRadiusChunks <= 0 ? maxRadius : _loadRadiusChunks;
        if (loadRadius > maxRadius)
        {
            UnityEngine.Debug.LogWarning(
                $"[Phase4Bootstrapper] load radius {loadRadius} exceeds the maximum {maxRadius} for a " +
                $"{_store.WindowDims.x}x{_store.WindowDims.z} window (the eviction ring needs " +
                $"{EngineConfig.HYSTERESIS_RING_CHUNKS} chunks beyond it). Clamping to {maxRadius}.");
            loadRadius = maxRadius;
        }
        UnityEngine.Debug.Log($"[Phase4Bootstrapper] load radius {loadRadius} chunks " +
                              $"({loadRadius * 12.8f:F1}m), evict radius " +
                              $"{loadRadius + EngineConfig.HYSTERESIS_RING_CHUNKS}, max {maxRadius}.");

        Streamer = new StreamManager(_store, _pool, _allocator, Clipmap, Cascades,
                                     Meta, DeltaDirectory, loadRadius);
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
            // PrimeWindow, not WaitForIdle. WaitForIdle drives the normal
            // streaming pipeline, which measured 399 chunks in 16,239ms on the
            // previous run -- ~40ms/chunk wall against Phase 3's 3.8ms with
            // Parallel.For over the same generator, i.e. effective parallelism
            // near 1. PrimeWindow uses the mechanism Phase 3 already measured
            // fast, for the one case that needs bulk throughput.
            Streamer.PrimeWindow(camPos);
            // Mop up anything PrimeWindow skipped (records already mid-flight
            // from the Update() above).
            Streamer.WaitForIdle();
            // Tier-0 upload deliberately bypasses the per-frame byte cap here so
            // the rig does not begin measuring against a half-uploaded window.
            // This is the one place that cap is intentionally ignored.
            Clipmap.UploadDirty(_store, _pool);

            // CASCADES ARE NOT FLUSHED HERE. They stay budgeted even at startup:
            // 729 chunks x 2 tiers of DownsampleChunkToTier is ~4.4s in a single
            // call, which is most of what made the first run look hung. Distant
            // terrain instead resolves over the first few seconds of play while
            // tier 0 -- everything inside 128m -- is already complete.
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

        // TIMED, because the whole streaming system runs HERE, in LateUpdate.
        // FrameGapProbe splits the frame at its own LateUpdate, and Unity's
        // execution order between the two components is arbitrary -- so without
        // this number there is no way to tell "the stall is in render
        // submission" from "the stall is in Streamer.Update", and the probe was
        // reporting 98% of stutter wall clock in that interval either way.
        // TransferToSharedPool in particular is covered by NO existing phase
        // timer and copies up to MAX_CHUNK_LOADS_PER_FRAME x 4096 dense bodies
        // into the 366 MB shared pool every frame.
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Streamer.Update(camPos);
        double streamMs = sw.Elapsed.TotalMilliseconds;
        Coalescer.Update();
        FrameGapProbe.LastStreamerMs = streamMs;
        FrameGapProbe.LastCoalescerMs = sw.Elapsed.TotalMilliseconds - streamMs;
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