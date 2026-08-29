// Assets/Game/Phase3Bootstrapper.cs
//
// Phase 3b wiring (§13 Phase 3): "the only new wiring is feeding the richer
// output through Phase 2's ChunkStore/uploader." Structurally a sibling of
// Phase2Bootstrapper — same pools, same clipmap/cascade construction, same
// validator calls — with three additions:
//
//   1. Stage 1 planning (AnchorPlanner) runs first, and world.meta is WRITTEN
//      to disk, READ BACK, and byte-verified before any chunk generates. The
//      generation loop consumes the READ-BACK meta, not the in-memory plan —
//      so the persisted D.2 file is proven to be the real source of truth on
//      every single run, not just in a unit test.
//   2. Generation calls ChunkGeneratorFull (§5.3 steps 1–4).
//   3. Static Store/Pool/Clipmap/Cascades/Meta exposed for the acceptance rig
//      and benchmark (same pattern, and for the same reasons, as
//      Phase2Bootstrapper's statics).
//
// WORLD EXTENT DECISION: GENERATED_CHUNKS_XZ stays 22, matching Phase 2.
// PHASE_2_COMPLETION.md §7 mandates re-running RaymarchAutoBenchmark on the
// new generation; keeping the extent (and the spawn pose) identical means the
// only variable between the Phase-2 and Phase-3 benchmark folders is the
// terrain content itself — which is exactly the variable being measured.
using UnityEngine;
using Unity.Mathematics;
using VoxelEngine.Memory;
using VoxelEngine.Mirror;
using VoxelEngine.WorldGen;
using System.Diagnostics;
using System.IO;

public class Phase3Bootstrapper : MonoBehaviour
{
    public static TerrainClipmap Clipmap { get; private set; }
    public static ChunkStore Store { get; private set; }
    public static BrickDataPool Pool { get; private set; }
    public static LODCascadeManager Cascades { get; private set; }
    public static WorldMetaData Meta { get; private set; }

    // Human-readable result of the write->read-back->verify cycle, for the
    // acceptance rig's report. Null until Start has run.
    public static string MetaRoundtripStatus { get; private set; }

    // WORLD EXTENT. §2.4 specifies Small = 200x200 chunks = 2,560m. That is NOT
    // reachable in Phase 3 and raising this number will not get you there:
    // 200x200 = 40,000 chunks would need ~3.3 minutes of eager generation at the
    // measured rate and ~12.5M dense bricks against a 750,000 pool cap - a 16x
    // overrun that would throw. Full world size structurally requires Phase 4
    // streaming (§2.4 is literally titled "3D Streaming"), which replaces eager
    // generation with a moving resident window.
    //
    // What IS reachable: 32 fills the clipmap window exactly
    // (EngineConfig.WINDOW_CHUNKS_XZ = 32 => 409.6m), which also makes tier 2's
    // 290m bound meaningful since that bound is derived from the window's own
    // half-diagonal. Cost is roughly 2x the startup of 22.
    //
    // Left at 22 by DEFAULT so this run stays directly comparable to every
    // prior benchmark folder. Change it in the Inspector, not in code.
    [Header("World extent")]
    [Tooltip("22 = matches all prior benchmark runs. 32 = fills the clipmap window exactly (409.6m) at ~2x startup cost. Full §2.4 size needs Phase 4 streaming.")]
    [SerializeField] private int _generatedChunksXZ = 22;

    public static int GENERATED_CHUNKS_XZ { get; private set; } = 22;
    public const uint WORLD_SEED = 42;         // §13 Phase 3: "Scene: Phase3_Island — seed 42"
    public const byte SIZE_CLASS = 0;

    [Header("Camera spawn (kept identical to Phase2Bootstrapper's default")]
    [Tooltip("If true, the camera is moved to the pose below on play — the same top-down pose Phase 2's benchmark runs recorded, so the main-sweep numbers stay comparable.")]
    [SerializeField] private bool _overrideCameraOnStart = true;
    [SerializeField] private Vector3 _cameraSpawnPosition = new Vector3(52.0f, 84.0f, 52.0f);
    [SerializeField] private Vector3 _cameraSpawnEuler = new Vector3(90.0f, 0.0f, 0.0f);

    private BrickDataPool _pool;
    private ChunkHandleAllocator _allocator;
    private ChunkStore _store;

    void Start()
    {
        GENERATED_CHUNKS_XZ = Mathf.Max(1, _generatedChunksXZ);

        // ---- Stage 1 (§5.2): plan, persist, read back, verify ----
        var sw = Stopwatch.StartNew();
        WorldMetaData planned = AnchorPlanner.Plan(WORLD_SEED, SIZE_CLASS);
        string metaPath = Path.Combine(Application.persistentDataPath, "Phase3World", "world.meta");
        WorldMeta.WriteAtomic(metaPath, planned);

        if (!WorldMeta.TryRead(metaPath, out WorldMetaData readBack))
        {
            MetaRoundtripStatus = $"FAIL: world.meta at {metaPath} could not be read back (CRC/format).";
            UnityEngine.Debug.LogError("[Phase3Bootstrapper] " + MetaRoundtripStatus);
            return; // do not generate from unverified state
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
            UnityEngine.Debug.LogError("[Phase3Bootstrapper] " + MetaRoundtripStatus);
            return;
        }

        MetaRoundtripStatus = $"PASS: world.meta written+read-back byte-identical ({a.Length} bytes, " +
                              $"{readBack.anchors.Length} anchors, {readBack.biomeSeeds.Length} biome seeds) at {metaPath}";
        UnityEngine.Debug.Log($"[Phase3Bootstrapper] Stage 1 planning + meta verify: {sw.ElapsedMilliseconds}ms. {MetaRoundtripStatus}");

        Meta = readBack; // the FILE's content drives generation from here on

        // ---- Pools / store / mirrors (same construction as Phase 2) ----
        _pool = new BrickDataPool(EngineConfig.BRICK_POOL_CAP);
        _allocator = new ChunkHandleAllocator(512);
        _store = new ChunkStore(_pool, _allocator);
        Store = _store;
        Pool = _pool;

        int3 windowChunks = new int3(EngineConfig.WINDOW_CHUNKS_XZ, EngineConfig.WINDOW_CHUNKS_Y, EngineConfig.WINDOW_CHUNKS_XZ);
        Clipmap = new TerrainClipmap(windowChunks, _pool.Capacity);
        Cascades = new LODCascadeManager(windowChunks, tier => LODCascadeManager.DefaultTierPoolCapacity(EngineConfig.BRICK_POOL_CAP));

        // ---- Stage 2 (§5.2/§5.3): per-chunk generation from the verified meta ----
        //
        // PARALLELISED (was a plain serial double loop). Measured cause of the
        // long startup stall, from the standalone build's own log:
        //   worldgen 3665ms + Cascades.UploadDirty 2945ms + Clipmap 439ms
        // all synchronous on the main thread inside Start(). In-Editor (Mono,
        // no IL2CPP optimisation) that same work is several times slower,
        // which is the minute-long beachball.
        //
        // Column sampling dominates that 3665ms (484 chunks x 16,384 column
        // samples x ~9 cnoise evaluations = ~71M noise calls) and is a PURE
        // function of (meta, x, z) — so chunks parallelise with no ordering
        // hazard. The two pieces of shared mutable state are the brick pool
        // and the handle allocator; neither is thread-safe, so both are
        // guarded by a single lock. That lock is held only for the allocation
        // itself, never across the noise work, so contention stays low.
        //
        // DETERMINISM NOTE: pool INDICES now vary between runs (allocation
        // order is no longer deterministic). Logical content does not, and
        // nothing depends on index stability — ChunkContentHash deliberately
        // hashes materials rather than indices, and the rig's live-store-vs-
        // fresh-regeneration check compares content. Verified by that check
        // continuing to pass, not assumed.
        sw.Restart();
        using var samplerState = ColumnSampler.CreateState(Meta);
        int totalChunks = GENERATED_CHUNKS_XZ * GENERATED_CHUNKS_XZ;
        var generated = new Chunk[totalChunks];
        object allocLock = new object();

        System.Threading.Tasks.Parallel.For(0, totalChunks, i =>
        {
            int cx = i % GENERATED_CHUNKS_XZ;
            int cz = i / GENERATED_CHUNKS_XZ;
            var coord = new int3(cx, 0, cz);
            var chunk = new Chunk();
            ChunkGeneratorFull.GenerateChunkFull(in samplerState, Meta, coord, chunk,
                _allocator, _pool, allocLock);
            generated[i] = chunk;
        });

        // Insert + mark dirty serially: ChunkStore and the dirty sets are not
        // thread-safe either, and this part is cheap next to the noise work.
        for (int i = 0; i < totalChunks; i++)
        {
            var chunk = generated[i];
            _store.InsertChunk(chunk);
            Clipmap.MarkDirty(chunk.coord);
            Cascades.MarkDirty(chunk.coord);
        }
        UnityEngine.Debug.Log($"[Phase3Bootstrapper] Worldgen loop ({GENERATED_CHUNKS_XZ}x{GENERATED_CHUNKS_XZ}, full §5.3 pipeline, parallel across {System.Environment.ProcessorCount} cores): {sw.ElapsedMilliseconds}ms");

        sw.Restart();
        Clipmap.UploadDirty(_store, _pool);
        UnityEngine.Debug.Log($"[Phase3Bootstrapper] Clipmap.UploadDirty: {sw.ElapsedMilliseconds}ms");

        sw.Restart();
        Cascades.UploadDirty(_store, _pool);
        UnityEngine.Debug.Log($"[Phase3Bootstrapper] Cascades.UploadDirty: {sw.ElapsedMilliseconds}ms");

        // Same runtime validators as Phase 2, unchanged: GPU state must be
        // byte-identical to CPU truth before anything is trusted on screen.
        ClipmapValidator.ValidateRegion(Clipmap, _pool, _store);
        AirMipValidator.ValidateAll(Clipmap, _store);

        if (_overrideCameraOnStart && Camera.main != null)
        {
            Camera.main.transform.position = _cameraSpawnPosition;
            Camera.main.transform.rotation = Quaternion.Euler(_cameraSpawnEuler);
        }
    }

    void OnDestroy()
    {
        Clipmap?.Dispose();
        Cascades?.Dispose();
        _pool?.Dispose();
        Clipmap = null; Cascades = null; Store = null; Pool = null; Meta = null;
        MetaRoundtripStatus = null;
    }
}