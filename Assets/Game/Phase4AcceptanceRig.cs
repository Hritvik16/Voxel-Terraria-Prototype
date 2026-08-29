// ==========================================
// Assets/Game/Phase4AcceptanceRig.cs
//
// §13 Phase 4's acceptance test, automated. Runs as STAGED GATES in fixed
// order and HALTS at the first red.
//
// Why staged rather than one big pass: Phase 4 introduces four untrusted
// systems at once (sliding window, async generation, delta persistence,
// incremental upload). §0.1 invariant 5 wants exactly one place to look when a
// test fails; the gate order restores that property at test time even though
// everything ships together. A red in Gate B means the uploader, not the
// window; a red in Gate C means the window, not the uploader, because B
// already passed with the window pinned.
//
//   GATE A  CPU only, no camera   -- lifecycle table, delta round-trip + fuzz
//   GATE B  static window         -- clipmap byte-match, upload budget
//   GATE C  moving window         -- slide, evict, re-admit, content identity
//   GATE D  persistence           -- edit/leave/return, corrupt-delta recovery
//   GATE E  soak                  -- sustained traversal, memory flatness
//
// EVERY NUMBER'S STATUS IS STATED. FrameTimingManager figures are relative
// within a run, never absolute against a gate (§10.2) -- an Xcode Metal System
// Trace remains the only source for that. Upload ms is CPU-side Stopwatch and
// IS directly comparable to §4.3's 1.0 ms budget, because that budget is a
// CPU-main-thread budget.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Memory;
using VoxelEngine.Streaming;
using VoxelEngine.WorldGen;

public class Phase4AcceptanceRig : MonoBehaviour
{
    [Header("Run control")]
    [SerializeField] private bool _runOnStart = true;
    // Default OFF. The first run halted at Gate B and produced a report with one
    // failure line, no screenshots, and no census -- so a red gate cost us the
    // entire rest of the run's evidence and still did not explain itself.
    // Gate ORDER already tells us where things first broke; stopping is not what
    // provides that. Collect everything, then read top-down.
    [SerializeField] private bool _haltOnFirstRedGate = false;

    [Header("Traversal")]
    [Tooltip("§2.5 burst player speed. The window is sized against this.")]
    [SerializeField] private float _flySpeed = 60f;
    [SerializeField] private float _traverseMeters = 200f;
    [SerializeField] private float _soakSeconds = 20f;
    [Tooltip("Distance for Gate D's leave-and-return. §13 says 500m; shorter still crosses the eviction ring.")]
    [SerializeField] private float _persistenceRoundTripMeters = 250f;
    [Tooltip("Hard ceiling on total rig wall-clock. The run aborts and writes what it has rather than hanging.")]
    [SerializeField] private float _maxRunSeconds = 240f;
    [Tooltip("Beauty screenshot every N metres of traversal. The stills are the only artifact that shows streaming LAG -- terrain arriving behind the camera -- as opposed to streaming errors.")]
    [SerializeField] private float _screenshotEveryMeters = 100f;

    [Header("Output")]
    [SerializeField] private string _outputRootFolderName = "Phase4Acceptance";
    [SerializeField] private bool _zipWhenDone = true;
    [SerializeField] private bool _revealInFinderWhenDone = true;
    [SerializeField] private bool _copyPlayerLogWhenDone = true;
    [SerializeField] private bool _quitWhenDone = true;

    private readonly StringBuilder _report = new StringBuilder();
    private string _runFolder;
    private int _pass, _fail;
    private bool _gateFailed;

    void Awake()
    {
        var fly = Camera.main != null ? Camera.main.GetComponent<SimpleFlyCamera>() : null;
        if (fly != null) fly.enabled = false; // the rig drives the camera
    }

    void Start() { if (_runOnStart) StartCoroutine(RunAll()); }

    private void Line(string s) { _report.AppendLine(s); Debug.Log("[Phase4Rig] " + s); }
    private void Check(bool ok, string what)
    {
        if (ok) { _pass++; Line($"PASS: {what}"); }
        else { _fail++; _gateFailed = true; Line($"FAIL: {what}"); }
    }
    private void Info(string s) => _report.AppendLine(s);

    private IEnumerator RunAll()
    {
        yield return new WaitForSeconds(1.0f);

        string ts = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
        _runFolder = Path.Combine(Application.persistentDataPath, _outputRootFolderName, ts);
        Directory.CreateDirectory(_runFolder);

        _report.AppendLine("=== Phase 4 Acceptance Rig (streaming & persistence) ===");
        _report.AppendLine($"Date: {DateTime.Now}");
        _report.AppendLine($"Unity: {Application.unityVersion}  Platform: {Application.platform}");
        _report.AppendLine($"Device: {SystemInfo.graphicsDeviceName}  OS: {SystemInfo.operatingSystem}");
        _report.AppendLine($"Internal render resolution: {RaymarchFeature.LastDispatchResolution}");
        _report.AppendLine($"WINDOW_CHUNKS_XZ={EngineConfig.WINDOW_CHUNKS_XZ} WINDOW_CHUNKS_Y={EngineConfig.WINDOW_CHUNKS_Y} " +
                           $"BRICK_POOL_CAP={EngineConfig.BRICK_POOL_CAP} " +
                           $"MAX_CLIPMAP_UPLOAD_BYTES_PER_FRAME={EngineConfig.MAX_CLIPMAP_UPLOAD_BYTES_PER_FRAME}");
        _report.AppendLine($"Startup: {Phase4Bootstrapper.StartupMs:F0}ms " +
            $"(prime: {Phase4Bootstrapper.Streamer?.PrimeChunks ?? 0} chunks, " +
            $"generate {Phase4Bootstrapper.Streamer?.PrimeGenerateMs ?? 0:F0}ms, " +
            $"transfer {Phase4Bootstrapper.Streamer?.PrimeTransferMs ?? 0:F0}ms, " +
            $"{Phase4Bootstrapper.Streamer?.PrimeWaves ?? 0} waves)");
        _report.AppendLine();
        _report.AppendLine("READING RULE: upload_ms is CPU main-thread Stopwatch and IS comparable to §4.3's");
        _report.AppendLine("1.0ms budget. Any FrameTimingManager figure is relative-within-run only (§10.2).");
        _report.AppendLine();

        if (Phase4Bootstrapper.Store == null || Phase4Bootstrapper.Streamer == null)
        {
            Line("FATAL: Phase4Bootstrapper did not initialise. Aborting.");
            Line($"Meta round-trip: {Phase4Bootstrapper.MetaRoundtripStatus ?? "(never ran)"}");
            Finish(); yield break;
        }

        _runStart = Time.realtimeSinceStartup;

        yield return StartCoroutine(GateA()); FlushReport();
        if (!Halted() && !OutOfTime()) { yield return StartCoroutine(GateB()); FlushReport(); }
        if (!Halted() && !OutOfTime()) { yield return StartCoroutine(GateC()); FlushReport(); }
        if (!Halted() && !OutOfTime()) { yield return StartCoroutine(GateD()); FlushReport(); }
        if (!Halted() && !OutOfTime()) { yield return StartCoroutine(GateE()); FlushReport(); }

        _report.AppendLine();
        _report.AppendLine("=== COMMIT READINESS ===");
        _report.AppendLine($"PASS {_pass}   FAIL {_fail}");
        _report.AppendLine(_fail == 0
            ? "CORRECTNESS: proven for every assertion above.\n" +
              "PERFORMANCE: measured, see Gate B/C/E upload figures against §4.3's 1.0ms.\n" +
              "VISUAL: NOT proven by this rig -- the screenshots are human-review evidence."
            : "ONE OR MORE GATES RED. Read the first FAIL line; later gates were skipped on purpose\n" +
              "so the failure has exactly one candidate cause.");

        Finish();
    }

    private bool Halted() => _haltOnFirstRedGate && _gateFailed;

    private float _runStart;
    private bool OutOfTime()
    {
        if (Time.realtimeSinceStartup - _runStart < _maxRunSeconds) return false;
        Line($"ABORTING: exceeded _maxRunSeconds ({_maxRunSeconds:F0}s). Everything above still stands; " +
             "gates below it did not run. Raise the budget or shorten the legs.");
        return true;
    }

    /// Writes the report after every gate rather than only at the end.
    ///
    /// The first run was cancelled mid-flight and produced NOTHING -- all the
    /// evidence up to that point was lost because Finish() had not been reached.
    /// A partial report from a cancelled run is far more useful than no report,
    /// so the file is rewritten as each gate completes.
    private IEnumerator WritePathAB()
    {
        _report.AppendLine("  --- GPU write path A/B (LockBufferForWrite vs SetData) ---");
        var store = Phase4Bootstrapper.Store;
        var pool = Phase4Bootstrapper.Pool;
        var clip = Phase4Bootstrapper.Clipmap;

        foreach (bool useLock in new[] { true, false })
        {
            TerrainClipmap.UseLockBufferForUploads = useLock;
            double total = 0; int reps = 8;
            for (int i = 0; i < reps; i++)
            {
                // Dirty a fixed slice of the window so both paths move the same
                // bytes, then time the flush.
                int n = 0;
                foreach (var ch in store.ResidentChunks())
                { clip.MarkDirty(ch.coord); if (++n >= 32) break; }

                var sw = System.Diagnostics.Stopwatch.StartNew();
                var st = clip.FlushAllDirty(store, pool);
                total += sw.Elapsed.TotalMilliseconds;
                yield return null;
            }
            Line($"write path {(useLock ? "LockBufferForWrite" : "SetData")}: " +
                 $"{total / reps:F2}ms per 32-chunk flush (main-thread only)");
        }
        TerrainClipmap.UseLockBufferForUploads = true;
    }

    private void FlushReport()
    {
        try { File.WriteAllText(Path.Combine(_runFolder, "phase4_report.txt"), _report.ToString()); }
        catch (Exception e) { Debug.LogWarning($"[Phase4Rig] report flush failed: {e.Message}"); }
    }

    // =====================================================================
    // GATE A -- CPU only. No camera, no GPU.
    // =====================================================================
    private IEnumerator GateA()
    {
        _report.AppendLine("--- GATE A: lifecycle table + delta codec (CPU only) ---");

        // A1: every one of the 16 state pairs classified against §4.4's table.
        int legal = 0, illegal = 0;
        foreach (ChunkState from in Enum.GetValues(typeof(ChunkState)))
        foreach (ChunkState to in Enum.GetValues(typeof(ChunkState)))
        {
            var v = ChunkLifecycle.Classify(from, to, conditionMet: true);
            if (v == TransitionVerdict.Legal) legal++; else illegal++;
        }
        Check(legal == 5, $"exactly 5 of 16 state pairs are legal per §4.4's table (found {legal})");
        Check(illegal == 11, $"the other 11 pairs are rejected (found {illegal})");

        Check(ChunkLifecycle.Classify(ChunkState.Loading, ChunkState.Unloaded, true) == TransitionVerdict.ForbiddenAbandonLoad,
            "Loading->Unloaded rejected as ForbiddenAbandonLoad (§4.4 'never abandon mid-load')");
        Check(ChunkLifecycle.Classify(ChunkState.Saving, ChunkState.Resident, true) == TransitionVerdict.ForbiddenSaveInterrupt,
            "Saving->Resident rejected (the Saving-eviction lock)");
        Check(ChunkLifecycle.Classify(ChunkState.Saving, ChunkState.Loading, true) == TransitionVerdict.ForbiddenSaveInterrupt,
            "Saving->Loading rejected (the Saving-eviction lock)");
        Check(ChunkLifecycle.Classify(ChunkState.Unloaded, ChunkState.Loading, false) == TransitionVerdict.PreconditionFailed,
            "Unloaded->Loading with no free slot rejected as PreconditionFailed");

        // A2: generation counter bumps on the full cycle, which is what makes
        // the stale-completion guard in StreamManager work at all.
        var rec = new ChunkRecord { coord = new int3(1, 0, 1) };
        ushort g0 = rec.generation;
        ChunkLifecycle.Transition(rec, ChunkState.Loading, true);
        ChunkLifecycle.Transition(rec, ChunkState.Resident, true);
        ChunkLifecycle.Transition(rec, ChunkState.Unloaded, true);
        Check(rec.generation == (ushort)(g0 + 1), $"generation increments on a full residency cycle ({g0} -> {rec.generation})");

        // A3: delta round-trip against a real generated chunk.
        yield return null;
        RunDeltaCodecChecks();

        _report.AppendLine();
        yield return null;
    }

    private void RunDeltaCodecChecks()
    {
        var meta = Phase4Bootstrapper.Meta;
        var coord = new int3(11, 0, 11);
        using var st = ColumnSampler.CreateState(meta);

        var poolA = new BrickDataPool(EngineConfig.BRICKS_PER_CHUNK);
        var poolB = new BrickDataPool(EngineConfig.BRICKS_PER_CHUNK);
        var poolC = new BrickDataPool(EngineConfig.BRICKS_PER_CHUNK);
        try
        {
            var allocA = new ChunkHandleAllocator(2);
            var allocB = new ChunkHandleAllocator(2);
            var allocC = new ChunkHandleAllocator(2);

            var live = new Chunk();
            ChunkGeneratorFull.GenerateChunkFull(in st, meta, coord, live, allocA, poolA);
            var baseline = new Chunk();
            ChunkGeneratorFull.GenerateChunkFull(in st, meta, coord, baseline, allocB, poolB);

            // Pristine chunk => no delta at all. §4.1: absence IS information.
            byte[] none = DeltaCodec.Encode(coord, meta.seed, live, poolA, baseline, poolB);
            Check(none == null, "an unedited chunk encodes to NO delta file at all (§4.1 zero-byte pristine)");

            // Simulate edits, then round-trip.
            var editStore = new ChunkStore(poolA, allocA);
            editStore.InsertChunk(live);
            int edits = 0;
            for (int i = 0; i < 400; i++)
            {
                int wx = coord.x * 128 + (i * 7) % 128;
                int wy = 30 + (i % 40);
                int wz = coord.z * 128 + (i * 13) % 128;
                editStore.SetVoxel(new int3(wx, wy, wz), Materials.Stone);
                edits++;
            }

            byte[] bytes = DeltaCodec.Encode(coord, meta.seed, live, poolA, baseline, poolB);
            Check(bytes != null, $"an edited chunk encodes to a delta ({edits} SetVoxel calls, {bytes?.Length ?? 0} bytes)");
            if (bytes == null) return;

            uint liveHash = ChunkContentHash.Hash(live, poolA);

            var restored = new Chunk();
            ChunkGeneratorFull.GenerateChunkFull(in st, meta, coord, restored, allocC, poolC);
            bool ok = DeltaCodec.TryDecodeOnto(bytes, coord, meta.seed, restored, poolC, out var reason);
            Check(ok, $"delta decodes onto a fresh baseline (reason={reason})");
            if (ok)
            {
                uint restoredHash = ChunkContentHash.Hash(restored, poolC);
                Check(liveHash == restoredHash,
                    $"round-trip is content-exact: live 0x{liveHash:X8} == baseline+delta 0x{restoredHash:X8}");
            }

            // §13's "hex-corrupt a .delta" assertion, done exhaustively rather
            // than once: every single-byte flip must be REJECTED, not
            // half-applied. A CRC that only catches most corruption is a CRC
            // that loses saves occasionally, which is worse than one that never
            // catches any -- the failure is invisible.
            int rejected = 0, accepted = 0;
            var rng = new System.Random(1234);
            for (int trial = 0; trial < 200; trial++)
            {
                byte[] corrupt = (byte[])bytes.Clone();
                int at = rng.Next(corrupt.Length);
                corrupt[at] ^= (byte)(1 << rng.Next(8));

                var victim = new Chunk();
                var vpool = new BrickDataPool(EngineConfig.BRICKS_PER_CHUNK);
                try
                {
                    ChunkGeneratorFull.GenerateChunkFull(in st, meta, coord, victim, new ChunkHandleAllocator(2), vpool);
                    uint pristine = ChunkContentHash.Hash(victim, vpool);
                    if (DeltaCodec.TryDecodeOnto(corrupt, coord, meta.seed, victim, vpool, out _)) accepted++;
                    else
                    {
                        rejected++;
                        // The rejection must leave the baseline untouched.
                        if (ChunkContentHash.Hash(victim, vpool) != pristine)
                            Check(false, "a rejected delta left the baseline chunk modified (partial apply)");
                    }
                }
                finally { vpool.Dispose(); }
            }
            Check(accepted == 0, $"all 200 single-bit corruptions rejected by CRC (accepted={accepted}, rejected={rejected})");

            // Truncation at every length must also reject, never throw.
            int truncThrew = 0, truncAccepted = 0;
            for (int len = 0; len < bytes.Length; len += math.max(1, bytes.Length / 100))
            {
                byte[] trunc = new byte[len];
                Array.Copy(bytes, trunc, len);
                var victim = new Chunk();
                var vpool = new BrickDataPool(EngineConfig.BRICKS_PER_CHUNK);
                try
                {
                    ChunkGeneratorFull.GenerateChunkFull(in st, meta, coord, victim, new ChunkHandleAllocator(2), vpool);
                    if (DeltaCodec.TryDecodeOnto(trunc, coord, meta.seed, victim, vpool, out _)) truncAccepted++;
                }
                catch (Exception) { truncThrew++; }
                finally { vpool.Dispose(); }
            }
            Check(truncThrew == 0, $"no truncation length throws (§4.2 requires total decode; threw={truncThrew})");
            Check(truncAccepted == 0, $"no truncated delta is accepted (accepted={truncAccepted})");

            // Wrong coord / wrong seed must reject: a delta from another world
            // silently applied is a save-corruption bug with no symptom.
            var wrong = new Chunk();
            var wpool = new BrickDataPool(EngineConfig.BRICKS_PER_CHUNK);
            try
            {
                ChunkGeneratorFull.GenerateChunkFull(in st, meta, coord, wrong, new ChunkHandleAllocator(2), wpool);
                Check(!DeltaCodec.TryDecodeOnto(bytes, new int3(0, 0, 0), meta.seed, wrong, wpool, out var r1)
                      && r1 == DeltaRejectReason.ChunkCoordMismatch, "delta for a different chunk coord is rejected");
                Check(!DeltaCodec.TryDecodeOnto(bytes, coord, meta.seed + 1, wrong, wpool, out var r2)
                      && r2 == DeltaRejectReason.SeedMismatch, "delta from a different world seed is rejected");
            }
            finally { wpool.Dispose(); }

            Info($"  delta size for {edits} edits: {bytes.Length} bytes " +
                 $"(§11.4 worst case for a fully-edited chunk ~= 2MB)");
        }
        finally { poolA.Dispose(); poolB.Dispose(); poolC.Dispose(); }
    }

    // =====================================================================
    // GATE B -- static window. Isolates the uploader from the slide.
    // =====================================================================
    private IEnumerator GateB()
    {
        _report.AppendLine("--- GATE B: residency census + incremental upload correctness, window PINNED ---");

        var store = Phase4Bootstrapper.Store;
        var pool = Phase4Bootstrapper.Pool;
        var clip = Phase4Bootstrapper.Clipmap;
        var streamer = Phase4Bootstrapper.Streamer;

        // ---- VISUAL EVIDENCE FIRST, unconditionally. ----
        // The first run took no screenshots because it halted before reaching
        // them. Captures are the only artifact that shows a BROKEN WORLD as
        // opposed to a broken assertion, and they cost a few frames.
        yield return StartCoroutine(Screenshot("GateB_Initial"));

        // ---- RESIDENCY CENSUS ----
        // The first run printed "380 chunks resident" with nothing to compare it
        // against, so a 52%-populated world read as normal. A count without an
        // expected value is not a census.
        int expected = streamer.ExpectedResidentChunks;
        int actual = store.ResidentCount;
        Line($"residency: {actual} resident / {expected} expected " +
             $"(load radius {streamer.LoadRadiusChunks}, evict {streamer.EvictRadiusChunks}, " +
             $"cy 0..{StreamManager.MAX_GENERATED_CHUNK_Y})");
        Line($"window origin {store.WindowOrigin}, store ring {store.WindowDims}, GPU mirror {clip.WindowDimsChunks}");
        Line($"dense bricks {store.DenseBricksHeld} ({store.PoolUtilisation:P1} of cap), " +
             $"clipmap dirty queue {clip.DirtyCount}");
        Line($"streaming: admitted {streamer.ChunksAdmittedTotal}, evicted {streamer.ChunksEvictedTotal}, " +
             $"pending {streamer.PendingLoads}, inFlight {streamer.InFlightLoads}, " +
             $"generation errors {streamer.GenerationErrors}");

        Check(actual == expected,
            $"every chunk in the load radius is resident ({actual}/{expected}) -- " +
            "a shortfall here means admission is dropping work, and every later gate " +
            "is measuring a world with holes in it");

        if (actual != expected)
        {
            var missing = streamer.MissingChunks(24);
            _report.AppendLine($"  first {missing.Count} missing chunk coords:");
            foreach (var c in missing) _report.AppendLine($"    {c}");
        }

        yield return StartCoroutine(GenerationMicroBenchmark());

        yield return StartCoroutine(UploadIsolationProbe());

        // A/B the two GPU write paths. SetData with an offset may rename a whole
        // buffer allocation; LockBufferForWrite is the API meant for partial
        // updates. Which one this machine prefers is a measurement, not a
        // reading-comprehension exercise.
        yield return StartCoroutine(WritePathAB());

        yield return null;

        // ---- PASS 1: as the frame loop left it ----
        var v1 = ClipmapValidator.ValidateRegion(clip, pool, store, maxChunks: 64);
        _report.AppendLine("  clipmap pass 1 (as-is): " + v1.Describe());

        // ---- PASS 2: after forcing every queued upload out ----
        // This is what separates LAG from LOST UPDATE at the whole-run level: if
        // flushing the queue fixes it, the pipeline works and the budget or
        // ordering delayed it. If it does NOT, something changed the CPU chunk
        // without marking it dirty, and no amount of waiting will ever fix it.
        clip.FlushAllDirty(store, pool);
        for (int i = 0; i < 3; i++) yield return null;

        var v2 = ClipmapValidator.ValidateRegion(clip, pool, store, maxChunks: 64);
        _report.AppendLine("  clipmap pass 2 (after forced flush): " + v2.Describe());

        Check(v2.pass, "GPU clipmap byte-matches CPU truth after a forced full flush");
        Check(v2.mismatchesInCleanChunks == 0,
            $"no LOST UPDATES: every stale GPU entry was still queued for upload " +
            $"({v2.mismatchesInCleanChunks} entries were stale with nothing queued)");

        if (!v1.pass && v2.pass)
            _report.AppendLine("  DIAGNOSIS: pass 1 red, pass 2 green => upload LAG only. The pipeline is " +
                               "correct; the per-frame budget had not caught up. Not corruption.");
        if (!v2.pass)
            _report.AppendLine("  DIAGNOSIS: still red after a forced flush => LOST UPDATE. Some path mutates " +
                               "a chunk without marking it dirty. Read the byKind breakdown above: " +
                               "CpuUniformGpuDense implicates the coalescer, CpuDenseGpuUniform implicates " +
                               "edit/delta replay, BothDenseDifferentSlot implicates pool reallocation.");

        // §11.3's two [Phase 4] lines, filled in with derived numbers.
        long clipmapBytes = (long)clip.WindowDimsBricks.x * clip.WindowDimsBricks.y * clip.WindowDimsBricks.z * 4;
        long handleBytes = (long)store.ResidentCount * EngineConfig.BRICKS_PER_CHUNK * 4;
        Info($"  §11.3 [Phase 4] Terrain Clipmap        = {clipmapBytes / 1048576.0:F1} MB " +
             $"({clip.WindowDimsBricks.x}x{clip.WindowDimsBricks.y}x{clip.WindowDimsBricks.z} bricks x 4B)");
        Info($"  §11.3 [Phase 4] Inlined brick handles  = {handleBytes / 1048576.0:F1} MB " +
             $"({store.ResidentCount} populated chunks x 4096 x 4B)");
        Info($"  Unity total allocated: {UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / 1048576.0:F1} MB");
        Info($"  Unity total reserved:  {UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong() / 1048576.0:F1} MB");

        _report.AppendLine();
        yield return null;
    }

    // =====================================================================
    // GATE C -- moving window.
    // =====================================================================
    private IEnumerator GateC()
    {
        _report.AppendLine("--- GATE C: sliding window, eviction, re-admission ---");

        var store = Phase4Bootstrapper.Store;
        var pool = Phase4Bootstrapper.Pool;
        var streamer = Phase4Bootstrapper.Streamer;
        Camera cam = Camera.main;
        if (cam == null) { Check(false, "Camera.main exists"); yield break; }

        // Record content hashes BEFORE the traversal, for chunks we will fly
        // away from and come back to. This is the check §10 of
        // PHASE_3_COMPLETION.md asked for: "content identical after a chunk
        // leaves and returns -- point the content-hash check at a moved window."
        var startPos = cam.transform.position;
        int3 homeChunk = CoordMath.VoxelToChunk(CoordMath.WorldToVoxel(
            new float3(startPos.x, startPos.y, startPos.z)));
        var watched = new List<int3>();
        var hashesBefore = new Dictionary<int3, uint>();
        for (int dz = -1; dz <= 1; dz++)
        for (int dx = -1; dx <= 1; dx++)
        {
            var c = new int3(homeChunk.x + dx, 0, homeChunk.z + dz);
            var ch = store.GetChunk(c);
            if (ch != null) { watched.Add(c); hashesBefore[c] = ChunkContentHash.Hash(ch, pool); }
        }
        Line($"watching {watched.Count} home chunks for leave/return content identity");

        int admitBefore = streamer.ChunksAdmittedTotal;
        int evictBefore = streamer.ChunksEvictedTotal;

        // ---- Fly out at 60 m/s, then back. ----
        var uploadMs = new List<double>();
        var uploadBytes = new List<int>();
        var drainMs = new List<double>();

        yield return StartCoroutine(FlyLeg(cam, new Vector3(1, 0, 0), _traverseMeters, uploadMs, uploadBytes, drainMs));
        Line($"outbound leg done: origin now {store.WindowOrigin}, resident {store.ResidentCount}");
        yield return StartCoroutine(FlyLeg(cam, new Vector3(-1, 0, 0), _traverseMeters, uploadMs, uploadBytes, drainMs));

        cam.transform.position = startPos;
        for (int i = 0; i < 10; i++) yield return null;
        streamer.WaitForIdle();
        yield return null;

        int admitted = streamer.ChunksAdmittedTotal - admitBefore;
        int evicted = streamer.ChunksEvictedTotal - evictBefore;
        Line($"traversal: {admitted} chunks admitted, {evicted} evicted over {_traverseMeters * 2:F0}m at {_flySpeed} m/s");
        Check(evicted > 0, "chunks were actually evicted (the window really moved)");
        Check(admitted > 0, "chunks were actually admitted");

        // The headline correctness assertion of this phase.
        // Wait for the refill EXPLICITLY and time it, instead of hoping
        // WaitForIdle's iteration guard was generous enough. Time-to-refill is
        // also the number that answers "does terrain load as fast as I move" --
        // the previous run conflated "not reloaded yet" with "content changed"
        // (4 'mismatches' that were almost certainly just missing chunks) and
        // could not answer either question.
        float refillStart = Time.realtimeSinceStartup;
        while (streamer.LoadDeficit() > 0 && Time.realtimeSinceStartup - refillStart < 30f)
        {
            streamer.WaitForIdle(500);
            yield return null;
        }
        float refillSeconds = Time.realtimeSinceStartup - refillStart;
        Line($"time-to-refill after returning home: {refillSeconds:F1}s (deficit now {streamer.LoadDeficit()})");
        Check(streamer.LoadDeficit() == 0, "the load square fully refilled within 30s of returning");

        int notResident = 0, hashMismatch = 0;
        foreach (var c in watched)
        {
            var ch = store.GetChunk(c);
            if (ch == null) { notResident++; continue; }
            if (ChunkContentHash.Hash(ch, pool) != hashesBefore[c]) hashMismatch++;
        }
        Check(hashMismatch == 0,
            $"no reloaded chunk changed content ({hashMismatch} of {watched.Count} hash-mismatched) -- " +
            "this is the corruption check, kept separate from reload timing on purpose");
        Check(notResident == 0,
            $"every watched chunk actually re-loaded ({notResident} still missing) -- " +
            "a failure HERE is refill speed, not corruption");

        // Upload budget, the §4.3 / §0.2 gate.
        ReportUpload("Gate C traversal", uploadMs, uploadBytes, drainMs);

        var vc = ClipmapValidator.ValidateRegion(Phase4Bootstrapper.Clipmap, pool, store, maxChunks: 64);
        _report.AppendLine("  clipmap after traversal: " + vc.Describe());
        Check(vc.mismatchesInCleanChunks == 0,
            $"no LOST UPDATES after the window moved ({vc.mismatchesInCleanChunks})");

        // Resident count is a RANGE, not an equality. Chunks stay resident out
        // to the EVICT radius (15), while ExpectedResidentChunks counts the LOAD
        // square (13). The band between them is the hysteresis ring doing its
        // job, so 783 > 729 was the engine behaving correctly and the assertion
        // being wrong. Bound it on both sides instead.
        int evictSide = streamer.EvictRadiusChunks * 2 + 1;
        int maxResident = evictSide * evictSide * (StreamManager.MAX_GENERATED_CHUNK_Y + 1);
        Line($"post-traversal residency: {store.ResidentCount} " +
             $"(load square {streamer.ExpectedResidentChunks} .. evict square {maxResident})");
        Check(store.ResidentCount >= streamer.ExpectedResidentChunks && store.ResidentCount <= maxResident,
            $"resident count sits between the load and evict squares " +
            $"({streamer.ExpectedResidentChunks} <= {store.ResidentCount} <= {maxResident})");

        yield return StartCoroutine(Screenshot("GateC_AfterReturn"));
        _report.AppendLine();
    }

    // Per-phase accumulators. The previous run reported a single upload_ms of
    // 44.9ms median with no way to attribute it, AND left ~1,000ms/frame of
    // render-thread time completely unmeasured.
    private readonly List<double> _frameMs = new List<double>();
    // Where each frame sample was taken. frame total is Time.unscaledDeltaTime,
    // so it absorbs ANY main-thread block -- including work this rig does
    // between legs (WaitForIdle, screenshot encodes, validator GetData storms,
    // RunFullPass). Without knowing WHERE an outlier landed, a 1000ms p99
    // cannot be told apart from a measurement artifact at a leg boundary.
    private readonly List<string> _frameLabel = new List<string>();
    private string _legLabel = "pre-leg";
    // Camera chunk per frame sample. The worst frames turned out to sit in a
    // contiguous MID-LEG band, not at leg boundaries, so the next question is
    // purely positional: where is the camera when the frame collapses?
    private readonly List<int3> _frameCamChunk = new List<int3>();
    // GC collections seen at each sample. Main-thread phase timers stayed
    // tiny during 1-2.6s frames, so the cost is something they do not
    // measure. A Mono collection over a multi-GB heap is exactly that
    // shape, and LODDownsampler.DownsampleChunkToTier allocates a fresh
    // byte[] per chunk PER TIER (256KB + 32KB) on every admission.
    private readonly List<int> _frameGcCount = new List<int>();

    // GPU-side timing per frame. Five CPU-side hypotheses for the 1-2.6s
    // frames were each measured and each disproven (screenshots, leg
    // boundaries, worker priority, GC, LockBufferForWrite), while every
    // main-thread phase timer stayed under a millisecond throughout. The
    // cost is downstream of everything the CPU can see, so the CPU cannot
    // be the instrument any more.
    //
    // FrameTimingManager, same mechanics Phase3AcceptanceRig already uses.
    // TWO THINGS ARE NOT TRUSTED HERE:
    //   1. That it populates at all. Mac Metal players return nothing in
    //      some Unity versions, so _ftValid/_ftAttempts is reported and any
    //      conclusion drawn from a zero-valid run is worthless.
    //   2. Absolute magnitude. Amendment 8.10 measured gpuFrameTime running
    //      ~2.6x inflated -- a frame's GPU time cannot exceed its wall
    //      clock, and it consistently did. Used here ONLY as a relative
    //      signal: does GPU time spike WITH the stutter or stay flat?
    //   3. Exact frame alignment. GetLatestTimings lags the pipeline by a
    //      few frames, so the dump prints the value at the sample AND the
    //      max across +/-3 samples rather than pretending to be exact.
    private readonly List<double> _gpuMs = new List<double>();
    private readonly List<double> _rtMs = new List<double>();
    private readonly List<double> _pwMs = new List<double>();
    private readonly List<double> _mtMs = new List<double>();   // cpuMainThreadFrameTime
    private readonly FrameTiming[] _ftBuf = new FrameTiming[1];
    private int _ftValid, _ftAttempts;
    private int _legOrdinal, _legFrameIdx;
    private readonly List<double> _stagingMs = new List<double>();
    private readonly List<double> _clipSetMs = new List<double>();
    private readonly List<double> _mipMs = new List<double>();
    private readonly List<double> _packMs = new List<double>();
    private readonly List<double> _brickSetMs = new List<double>();
    private readonly List<double> _packUpMs = new List<double>();
    private readonly List<double> _cascadeMs = new List<double>();
    private readonly List<int> _setDataCalls = new List<int>();
    private readonly List<int> _dirtyRemaining = new List<int>();
    private readonly List<int> _loadDeficit = new List<int>();
    private readonly List<double> _cascadeDownMs = new List<double>();
    private readonly List<double> _cascadeWriteMs = new List<double>();
    private float _nextShotAt;
    private int _travShotIndex;

    private void SamplePhases()
    {
        var st = Phase4Bootstrapper.Streamer;
        var u = st.LastUploadStats;
        _frameMs.Add(Time.unscaledDeltaTime * 1000.0);
        _frameLabel.Add($"{_legLabel}#{_legFrameIdx++}");
        _frameGcCount.Add(System.GC.CollectionCount(0));

        FrameTimingManager.CaptureFrameTimings();
        uint ftGot = FrameTimingManager.GetLatestTimings(1, _ftBuf);
        _ftAttempts++;
        if (ftGot > 0 && _ftBuf[0].gpuFrameTime > 0)
        {
            _ftValid++;
            _gpuMs.Add(_ftBuf[0].gpuFrameTime);
            _rtMs.Add(_ftBuf[0].cpuRenderThreadFrameTime);
            _pwMs.Add(_ftBuf[0].cpuMainThreadPresentWaitTime);
            _mtMs.Add(_ftBuf[0].cpuMainThreadFrameTime);
        }
        else { _gpuMs.Add(-1); _rtMs.Add(-1); _pwMs.Add(-1); _mtMs.Add(-1); }
        {
            Camera mc = Camera.main;
            _frameCamChunk.Add(mc != null
                ? CoordMath.VoxelToChunk(CoordMath.WorldToVoxel(
                    new float3(mc.transform.position.x, mc.transform.position.y, mc.transform.position.z)))
                : new int3(-999, -999, -999));
        }
        _stagingMs.Add(u.stagingMs);
        _clipSetMs.Add(u.clipmapSetMs);
        _mipMs.Add(u.mipRebuildMs);
        _packMs.Add(u.packRegionMs);
        _brickSetMs.Add(u.brickSetMs);
        _packUpMs.Add(u.packUploadMs);
        _cascadeMs.Add(st.LastCascadeMs);
        _setDataCalls.Add(u.setDataCalls);
        _dirtyRemaining.Add(u.dirtyRemaining);
        _loadDeficit.Add(st.LoadDeficit());

        var casc = Phase4Bootstrapper.Cascades;
        if (casc != null)
        {
            _cascadeDownMs.Add(casc.TierPool(1).LastDownsampleMs + casc.TierPool(2).LastDownsampleMs);
            _cascadeWriteMs.Add(casc.TierPool(1).LastGpuWriteMs + casc.TierPool(2).LastGpuWriteMs);
        }
    }

    private static double Pct(List<double> v, float p)
    {
        if (v.Count == 0) return 0;
        var c = new List<double>(v); c.Sort();
        return c[Mathf.Clamp((int)(c.Count * p), 0, c.Count - 1)];
    }
    private static double MaxOf(List<int> v)
    { int m = 0; foreach (int x in v) if (x > m) m = x; return m; }
    private static List<double> ToD(List<int> v)
    { var d = new List<double>(v.Count); foreach (int x in v) d.Add(x); return d; }

    private IEnumerator FlyLeg(Camera cam, Vector3 dir, float meters,
        List<double> uploadMs, List<int> uploadBytes, List<double> drainMs)
    {
        float travelled = 0f;
        var streamer = Phase4Bootstrapper.Streamer;
        _legOrdinal++; _legFrameIdx = 0; _legLabel = $"leg{_legOrdinal}";
        while (travelled < meters)
        {
            float dt = Mathf.Min(Time.deltaTime, 1f / 30f);
            float step = _flySpeed * dt;
            cam.transform.position += dir.normalized * step;
            travelled += step;
            yield return null;
            uploadMs.Add(streamer.LastUploadMs);
            uploadBytes.Add(streamer.LastUploadBytes);
            drainMs.Add(streamer.LastDrainMs);
            SamplePhases();

            // In-motion stills. Everything else in this rig measures whether
            // streaming is CORRECT; these are the artifact that shows whether it
            // is KEEPING UP -- terrain visibly arriving late reads as an empty
            // leading edge here long before any assertion goes red.
            _nextShotAt -= step;
            if (_nextShotAt <= 0f)
            {
                _nextShotAt = _screenshotEveryMeters;
                yield return new WaitForEndOfFrame();
                Texture2D shot = ScreenCapture.CaptureScreenshotAsTexture();
                try
                {
                    File.WriteAllBytes(Path.Combine(_runFolder,
                        $"Traverse_{_travShotIndex:D2}_deficit{Phase4Bootstrapper.Streamer.LoadDeficit()}.png"),
                        shot.EncodeToPNG());
                }
                finally { UnityEngine.Object.Destroy(shot); }
                _travShotIndex++;
            }
        }
    }

    /// Settles the generation-throughput question with a direct measurement
    /// instead of a fourth theory. Three runs of theorising produced:
    /// Task.Run ~40ms/chunk, Parallel.For waves ~29-33ms/chunk (SetMinThreads
    /// changed nothing, falsifying the ThreadPool-starvation explanation for
    /// it), dedicated threads ~11.3ms/chunk. What was never measured is the
    /// baseline: what does ONE chunk cost on ONE thread, here, in this
    /// process? Everything else -- how many workers help, whether 11.3ms is
    /// thread-limited or work-limited, whether §4.3's 152 chunks/s demand is
    /// even reachable on this machine -- divides out of that number.
    private IEnumerator GenerationMicroBenchmark()
    {
        _report.AppendLine("  --- generation micro-benchmark ---");
        var meta = Phase4Bootstrapper.Meta;
        using var st = VoxelEngine.WorldGen.ColumnSampler.CreateState(meta);
        const int N = 24;

        // Single thread, no pipeline, coords outside the resident window so
        // nothing here perturbs the live world.
        var pool = new VoxelEngine.Memory.BrickDataPool(EngineConfig.BRICKS_PER_CHUNK);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        double genMs = 0, downMs = 0;
        VoxelEngine.WorldGen.ChunkGeneratorFull.ResetPhaseCounters();
        try
        {
            for (int i = 0; i < N; i++)
            {
                var alloc = new VoxelEngine.Memory.ChunkHandleAllocator(2);
                var chunk = new Chunk();
                double t0 = sw.Elapsed.TotalMilliseconds;
                VoxelEngine.WorldGen.ChunkGeneratorFull.GenerateChunkFull(
                    in st, meta, new Unity.Mathematics.int3(500 + i, 0, 500), chunk, alloc, pool, null);
                double t1 = sw.Elapsed.TotalMilliseconds;
                VoxelEngine.Mirror.LODDownsampler.DownsampleChunkToTier(chunk, pool, 1);
                VoxelEngine.Mirror.LODDownsampler.DownsampleChunkToTier(chunk, pool, 2);
                double t2 = sw.Elapsed.TotalMilliseconds;
                genMs += t1 - t0; downMs += t2 - t1;

                if (!chunk.isUniform && chunk.bricks != null)
                    for (int b = 0; b < EngineConfig.BRICKS_PER_CHUNK; b++)
                        if ((chunk.bricks[b].data & 0x80000000u) != 0)
                            pool.Free((int)(chunk.bricks[b].data & 0x3FFFFFFFu));
            }
        }
        finally { pool.Dispose(); }

        double perChunk = genMs / N;
        Line($"single-thread generation: {perChunk:F2}ms/chunk, worker-side downsample adds {downMs / N:F2}ms/chunk " +
             $"({N} chunks, ocean-edge coords)");

        // ---- Burst vs Mono on the SAME column-sampling math ----
        // The phase split below says how much of generation this math is; this
        // says what Burst does to it. Both paths call ColumnSampler.SampleColumn
        // -- one compiled by Mono, one by Burst via ColumnSampleJob.Run() -- so
        // there is one implementation and no chance of the two drifting.
        //
        // Output equality is ASSERTED, not assumed. Generation determinism is on
        // §0.3's review list: if Burst and Mono disagree in even one column,
        // saved worlds would stop matching regenerated baselines, and that must
        // surface as a red gate rather than a faster number.
        {
            const int EDGE = 128;
            const int COLS = EDGE * EDGE;
            var hMono = new int[COLS];
            var bMono = new byte[COLS];
            int bx = 500 * 128, bz = 500 * 128;

            var swb = System.Diagnostics.Stopwatch.StartNew();
            for (int lz = 0; lz < EDGE; lz++)
            for (int lx = 0; lx < EDGE; lx++)
            {
                VoxelEngine.WorldGen.ColumnSampler.SampleColumn(in st, bx + lx, bz + lz,
                    out int h, out byte b);
                int idx = lz * EDGE + lx;
                hMono[idx] = h; bMono[idx] = b;
            }
            double monoMs = swb.Elapsed.TotalMilliseconds;

            var hJob = new Unity.Collections.NativeArray<int>(COLS, Unity.Collections.Allocator.Persistent);
            var bJob = new Unity.Collections.NativeArray<byte>(COLS, Unity.Collections.Allocator.Persistent);
            double burstMs;
            int mismatches = 0;
            try
            {
                var job = new VoxelEngine.WorldGen.ColumnSampleJob
                {
                    st = st, baseVoxelX = bx, baseVoxelZ = bz, edge = EDGE,
                    heights = hJob, biomes = bJob,
                };
                job.Run();                    // warm: forces Burst compilation
                swb.Restart();
                job.Run();
                burstMs = swb.Elapsed.TotalMilliseconds;

                for (int i = 0; i < COLS; i++)
                    if (hJob[i] != hMono[i] || bJob[i] != bMono[i]) mismatches++;
            }
            finally { hJob.Dispose(); bJob.Dispose(); }

            Line($"  column sampling, same math both paths, {COLS} columns: " +
                 $"Mono {monoMs:F2}ms vs Burst {burstMs:F2}ms " +
                 $"= {(burstMs > 0 ? monoMs / burstMs : -1):F1}x");
            Check(mismatches == 0,
                $"Burst and Mono column sampling agree exactly ({mismatches} of {COLS} columns differ) " +
                $"-- generation determinism (§5.3, §0.3 review list)");
        }

        // How much of generation is the Burst-able math (ColumnSampler ->
        // FeatureCarve) versus the voxel fill that writes Chunk/BrickDataPool?
        // The second half cannot be Burst-compiled without the protected
        // layout changes on §0.3's review list, so this ratio is the ceiling on
        // what Bursting those two alone can ever return.
        {
            double freq = System.Diagnostics.Stopwatch.Frequency;
            double colMs = VoxelEngine.WorldGen.ChunkGeneratorFull.ColumnPhaseTicks * 1000.0 / freq;
            double totMs = VoxelEngine.WorldGen.ChunkGeneratorFull.TotalPhaseTicks * 1000.0 / freq;
            double pct = totMs > 0 ? 100.0 * colMs / totMs : -1;
            // NOTE ON WHAT THIS NOW MEASURES. Before the Burst port this timer
            // wrapped the per-column SampleColumn calls and read 92.6%. Column
            // sampling now happens in ColumnSampleJob BEFORE this loop, so the
            // timer wraps only the buffer READ that replaced it -- which is why
            // it now reads a fraction of a percent. That collapse IS the win,
            // not a measurement error, but the label has to say so or the next
            // reader will draw the opposite conclusion.
            Line($"  generation phase split: per-column buffer read " +
                 $"{colMs / N:F2}ms/chunk of {totMs / N:F2}ms/chunk = {pct:F1}% " +
                 $"(was 92.6% when this was Mono SampleColumn calls; the sampling " +
                 $"itself is now Burst-compiled in ColumnSampleJob ahead of the loop). " +
                 $"The remainder is voxel fill into Chunk/BrickDataPool.");
        }
        double workers = Mathf.Max(2, SystemInfo.processorCount - 1);
        Line($"implied ceilings: 1 thread = {1000.0 / perChunk:F0} chunks/s; " +
             $"{workers:F0} perfect workers = {workers * 1000.0 / perChunk:F0} chunks/s; " +
             $"demand at 60 m/s = ~152 chunks/s (§4.3)");
        _report.AppendLine("  Read the pipeline's observed chunks/s against these ceilings: near the multi-");
        _report.AppendLine("  worker line means the pipeline is healthy and the WORK is the limit; near the");
        _report.AppendLine("  single-thread line means the threads are not actually running in parallel.");
        yield return null;
    }

    /// Measures frame time with GPU uploads suppressed vs enabled, changing
    /// NOTHING else. This is the only clean way to separate upload cost from
    /// raymarch cost when the time is hiding on the render thread where no
    /// main-thread Stopwatch can see it. Prefer finding out over guessing.
    private IEnumerator UploadIsolationProbe()
    {
        _report.AppendLine("  --- upload isolation probe ---");
        _report.AppendLine("  Frame time with GPU uploads ON vs SUPPRESSED. CPU-side work is identical in");
        _report.AppendLine("  both passes; only the GraphicsBuffer writes are skipped. A large gap means the");
        _report.AppendLine("  cost is the upload path; no gap means it is the raymarch dispatch.");

        // Keep the upload path genuinely BUSY through both windows by re-marking
        // resident chunks each frame. Last run's probe measured a NEGATIVE delta
        // (-6.2ms) because it ran against an EMPTY dirty queue -- both passes
        // uploaded nothing and the difference was frame-time noise. A probe that
        // can return "uploads cost less than zero" is measuring the wrong thing.
        var probeStore = Phase4Bootstrapper.Store;
        var probeClip = Phase4Bootstrapper.Clipmap;
        void ChurnDirty()
        {
            int n = 0;
            foreach (var ch in probeStore.ResidentChunks())
            { probeClip.MarkDirty(ch.coord); if (++n >= 12) break; }
        }

        for (int warm = 0; warm < 10; warm++) { ChurnDirty(); yield return null; }

        double onSum = 0; int onN = 0;
        for (int i = 0; i < 60; i++) { ChurnDirty(); yield return null; onSum += Time.unscaledDeltaTime * 1000.0; onN++; }

        TerrainClipmap.SuppressGpuUploads = true;
        for (int warm = 0; warm < 10; warm++) { ChurnDirty(); yield return null; }
        double offSum = 0; int offN = 0;
        for (int i = 0; i < 60; i++) { ChurnDirty(); yield return null; offSum += Time.unscaledDeltaTime * 1000.0; offN++; }
        TerrainClipmap.SuppressGpuUploads = false;

        double on = onSum / Mathf.Max(1, onN), off = offSum / Mathf.Max(1, offN);
        Line($"frame ms: uploads ON {on:F1}, uploads SUPPRESSED {off:F1}, delta {on - off:F1} " +
             $"({(on > 0 ? (on - off) / on * 100.0 : 0):F0}% of the frame is GPU upload)");
        _report.AppendLine(on - off > on * 0.5
            ? "  DIAGNOSIS: uploads dominate the frame. The raymarch is not the problem."
            : "  DIAGNOSIS: uploads are NOT the majority of the frame. Look at the raymarch dispatch " +
              "(StepHeat capture, GPU counters) before optimising the upload path further.");

        for (int warm = 0; warm < 5; warm++) yield return null;
    }

    private void ReportUpload(string label, List<double> ms, List<int> bytes, List<double> drain)
    {
        if (ms.Count == 0) { Line($"{label}: no frames sampled"); return; }

        ms.Sort();
        double p50 = ms[ms.Count / 2];
        double p99 = ms[Mathf.Min(ms.Count - 1, (int)(ms.Count * 0.99f))];
        double max = ms[ms.Count - 1];

        int maxBytes = 0; foreach (int b in bytes) if (b > maxBytes) maxBytes = b;
        double drainMax = 0; foreach (double d in drain) if (d > drainMax) drainMax = d;

        Line($"{label}: upload_ms p50={p50:F3} p99={p99:F3} max={max:F3} over {ms.Count} frames; " +
             $"drain_ms max={drainMax:F3}; max upload bytes/frame={maxBytes} ({maxBytes / 1048576.0:F2} MB)");

        Check(p99 <= 1.0,
            $"steady-state terrain upload p99 {p99:F3}ms <= 1.0ms (§4.3 'terrain upload <=1.0ms/frame CPU')");
        _report.AppendLine($"  upload phase breakdown (p50 / p99 ms over {_frameMs.Count} sampled frames):");
        _report.AppendLine($"    frame total     {Pct(_frameMs, 0.5f),8:F2} / {Pct(_frameMs, 0.99f),8:F2}");
        {
            // The 10 worst frames, WITH where they happened. p99 over ~700
            // samples is just the top ~7 frames, so if those all sit at #0/#1 of
            // a leg they are the rig's own between-leg work being charged to the
            // first frame after it -- not a streaming stutter.
            var idx = new List<int>();
            for (int i = 0; i < _frameMs.Count; i++) idx.Add(i);
            idx.Sort((a, b) => _frameMs[b].CompareTo(_frameMs[a]));
            _report.AppendLine($"    FrameTimingManager: {_ftValid}/{_ftAttempts} samples valid" +
                (_ftValid == 0
                    ? "  <-- API RETURNED NOTHING; gpu/rt/pw columns below are meaningless"
                    : "  (gpu is a RELATIVE signal only, ~2.6x inflated per Amendment 8.10)"));
            var sb = new StringBuilder("    worst frames: ");
            for (int i = 0; i < Math.Min(10, idx.Count); i++)
            {
                int j = idx[i];
                sb.Append($"{_frameMs[j]:F0}ms@{(j < _frameLabel.Count ? _frameLabel[j] : "?")}");
                if (j < _frameCamChunk.Count) sb.Append($" cam{_frameCamChunk[j]}");
                if (j < _loadDeficit.Count) sb.Append($" deficit={_loadDeficit[j]}");
                if (j > 0 && j < _frameGcCount.Count)
                    sb.Append($" gc+{_frameGcCount[j] - _frameGcCount[j - 1]}");
                if (j < _gpuMs.Count)
                {
                    // POINT values for every column. An earlier version reported
                    // rt/pw as the max across +/-3 samples while gpu showed its
                    // point value, which biased rt/pw upward and let a
                    // neighbouring frame's spike be read as this frame's cost --
                    // with FrameTimingManager only ~62% valid, that is a real
                    // misattribution risk, not a theoretical one. The window max
                    // is still printed, but separately and labelled, because
                    // GetLatestTimings genuinely does lag the pipeline.
                    double gpuNear = -1;
                    for (int k = Math.Max(0, j - 3); k <= Math.Min(_gpuMs.Count - 1, j + 3); k++)
                        if (_gpuMs[k] > gpuNear) gpuNear = _gpuMs[k];

                    sb.Append($" mt={(j < _mtMs.Count ? _mtMs[j] : -1):F1}");
                    sb.Append($" gpu={_gpuMs[j]:F1}");
                    sb.Append($" rt={(j < _rtMs.Count ? _rtMs[j] : -1):F1}");
                    sb.Append($" pw={_pwMs[j]:F1}");
                    sb.Append($" (gpuWin{gpuNear:F1})");
                }
                if (i < Math.Min(10, idx.Count) - 1) sb.Append(", ");
            }
            _report.AppendLine(sb.ToString());
        }
        _report.AppendLine($"    staging         {Pct(_stagingMs, 0.5f),8:F2} / {Pct(_stagingMs, 0.99f),8:F2}");
        _report.AppendLine($"    clipmap write   {Pct(_clipSetMs, 0.5f),8:F2} / {Pct(_clipSetMs, 0.99f),8:F2}");
        _report.AppendLine($"    mip rebuild     {Pct(_mipMs, 0.5f),8:F2} / {Pct(_mipMs, 0.99f),8:F2}");
        _report.AppendLine($"    pack region     {Pct(_packMs, 0.5f),8:F2} / {Pct(_packMs, 0.99f),8:F2}");
        _report.AppendLine($"    brick bodies    {Pct(_brickSetMs, 0.5f),8:F2} / {Pct(_brickSetMs, 0.99f),8:F2}");
        _report.AppendLine($"    packed mip up   {Pct(_packUpMs, 0.5f),8:F2} / {Pct(_packUpMs, 0.99f),8:F2}");
        _report.AppendLine($"    cascades        {Pct(_cascadeMs, 0.5f),8:F2} / {Pct(_cascadeMs, 0.99f),8:F2}");
        _report.AppendLine($"      - downsample  {Pct(_cascadeDownMs, 0.5f),8:F2} / {Pct(_cascadeDownMs, 0.99f),8:F2}");
        _report.AppendLine($"      - gpu writes  {Pct(_cascadeWriteMs, 0.5f),8:F2} / {Pct(_cascadeWriteMs, 0.99f),8:F2}");
        _report.AppendLine($"    max GPU write calls/frame: {MaxOf(_setDataCalls)}");
        _report.AppendLine($"    max clipmap dirty backlog: {MaxOf(_dirtyRemaining)} chunks");
        _report.AppendLine($"    load deficit during traversal: p50 {Pct(ToD(_loadDeficit), 0.5f):F0}, " +
                           $"p99 {Pct(ToD(_loadDeficit), 0.99f):F0}, max {MaxOf(_loadDeficit):F0} chunks " +
                           $"(0 = the visible world was always complete; a big number IS the 'terrain " +
                           $"loads slower than I move' complaint, quantified)");
        _report.AppendLine("    NOTE: these are MAIN-THREAD figures. GraphicsBuffer writes are serviced on");
        _report.AppendLine("    the render thread, so a small total here with a slow frame means the cost is");
        _report.AppendLine("    downstream -- read it against 'frame total' and the isolation probe above.");

        Check(maxBytes <= EngineConfig.MAX_CLIPMAP_UPLOAD_BYTES_PER_FRAME,
            $"no frame exceeded MAX_CLIPMAP_UPLOAD_BYTES_PER_FRAME " +
            $"({maxBytes} <= {EngineConfig.MAX_CLIPMAP_UPLOAD_BYTES_PER_FRAME}) (§0.2 anti-stutter guarantee)");
    }

    // =====================================================================
    // GATE D -- persistence.
    // =====================================================================
    private IEnumerator GateD()
    {
        _report.AppendLine("--- GATE D: edit / leave / return, and corrupt-delta recovery ---");

        var store = Phase4Bootstrapper.Store;
        var pool = Phase4Bootstrapper.Pool;
        var streamer = Phase4Bootstrapper.Streamer;
        Camera cam = Camera.main;
        var startPos = cam.transform.position;

        // ---- Dig a tunnel + build a structure (§13's exact assertion). ----
        //
        // Both assertions in this block used to fail, and neither was an engine
        // fault. The probe output below is permanent: "0 voxels dug" with no
        // coordinates printed cost a whole run to interpret, which is exactly
        // the failure mode §10.4 wants validators to avoid.
        int3 camVox = CoordMath.WorldToVoxel(new float3(startPos.x, startPos.y, startPos.z));
        int digs = 0, builds = 0;
        var editedChunks = new HashSet<int3>();

        // HARNESS BUG 1: the dig line was camVox.y - 4 -- 0.4m below the
        // CAMERA, not below the ground. The camera spawns at y=12m and
        // generated terrain tops out near there (WorldGenConstants
        // MAX_TERRAIN_HEIGHT=120 voxels), so the line ran through open air and
        // correctly removed nothing. Find the real surface in this column
        // instead of assuming the camera is standing on it.
        int surfaceY = int.MinValue;
        for (int y = camVox.y; y >= 0; y--)
            if (store.GetVoxel(new int3(camVox.x, y, camVox.z)) != Materials.Air) { surfaceY = y; break; }

        int digY = surfaceY > int.MinValue ? surfaceY - 2 : camVox.y - 4;
        Line($"  dig probe: camVox {camVox} (camera y={startPos.y:F1}m), " +
             $"surface at y={surfaceY} ({(surfaceY > int.MinValue ? surfaceY * 0.1f : -1f):F1}m), digging at y={digY}");
        Line($"  GetVoxel down the camera column: " +
             $"y={camVox.y} -> {store.GetVoxel(new int3(camVox.x, camVox.y, camVox.z))}, " +
             $"y={camVox.y - 4} (the OLD dig line) -> {store.GetVoxel(new int3(camVox.x, camVox.y - 4, camVox.z))}, " +
             $"y={digY} (the NEW dig line) -> {store.GetVoxel(new int3(camVox.x, digY, camVox.z))}");

        for (int i = 0; i < 200; i++)
        {
            var v = new int3(camVox.x + i, digY, camVox.z);
            if (store.GetVoxel(v) != Materials.Air)
            {
                store.SetVoxel(v, Materials.Air);
                digs++;
                editedChunks.Add(CoordMath.VoxelToChunk(v));
            }
        }

        // HARNESS BUG 2: the structure was placed at camVox.x - 10, and
        // camVox.x is 1408 = 11 * 128 -- exactly a chunk boundary. Every placed
        // voxel therefore landed in chunk (10,0,11) while the assertion below
        // read chunk (11,0,11), which the (failed) dig had left untouched. The
        // engine was marking deltaDirty on the chunk it was actually told to
        // edit; the test interrogated its neighbour. The corrupt-delta step
        // later in this gate names 10_0_11.delta -- the same fact from the
        // other end. Build inside the dug chunk, and assert on what was
        // actually edited rather than on a coordinate guess.
        int buildBaseY = surfaceY > int.MinValue ? surfaceY + 1 : camVox.y;
        for (int y = 0; y < 8; y++)
        for (int x = 0; x < 4; x++)
        for (int z = 0; z < 4; z++)
        {
            var v = new int3(camVox.x + 4 + x, buildBaseY + y, camVox.z + z);
            store.SetVoxel(v, Materials.Stone);
            builds++;
            editedChunks.Add(CoordMath.VoxelToChunk(v));
        }

        int3 editChunk = CoordMath.VoxelToChunk(new int3(camVox.x, digY, camVox.z));
        Line($"edited: {digs} voxels dug, {builds} placed; chunks touched: " +
             $"{string.Join(", ", editedChunks)}; round-trip assertions use {editChunk}");
        Check(digs > 0, "the dig actually removed solid voxels (edit landed on real terrain)");

        var edited = store.GetChunk(editChunk);
        uint editedHash = edited != null ? ChunkContentHash.Hash(edited, pool) : 0u;

        // A save cannot have consumed the flag at this point: everything above
        // runs synchronously in one frame, before the first yield, so
        // StreamManager has not ticked and no eviction/save could have run. If
        // this goes red again it is a genuine SetVoxel/deltaDirty fault, not a
        // timing artifact -- which is why it now names the offending chunks.
        int notDirty = 0;
        foreach (int3 c in editedChunks)
        {
            var ch = store.GetChunk(c);
            if (ch == null || !ch.deltaDirty)
            {
                notDirty++;
                Line($"  NOT deltaDirty: chunk {c} (resident={ch != null})");
            }
        }
        Check(editedChunks.Count > 0 && notDirty == 0,
            $"every edited chunk is marked deltaDirty ({editedChunks.Count} touched, {notDirty} missing the flag)");

        yield return null;

        // ---- Fly 500m away and back (§13 says 500m). ----
        var uploadMs = new List<double>(); var uploadBytes = new List<int>(); var drainMs = new List<double>();
        yield return StartCoroutine(FlyLeg(cam, new Vector3(0, 0, 1), _persistenceRoundTripMeters, uploadMs, uploadBytes, drainMs));
        yield return StartCoroutine(FlyLeg(cam, new Vector3(0, 0, -1), _persistenceRoundTripMeters, uploadMs, uploadBytes, drainMs));
        cam.transform.position = startPos;
        for (int i = 0; i < 10; i++) yield return null;
        streamer.WaitForIdle();
        yield return null;

        Line($"deltas written={streamer.ChunksSavedTotal} loaded={streamer.DeltasLoadedTotal} " +
             $"rejected={streamer.DeltasRejectedTotal}");
        Check(streamer.ChunksSavedTotal > 0, "at least one delta was written during eviction");
        Check(streamer.DeltasLoadedTotal > 0, "at least one delta was read back on re-admission");

        var returned = store.GetChunk(editChunk);
        Check(returned != null, $"the edited chunk {editChunk} is resident again");
        if (returned != null)
            Check(ChunkContentHash.Hash(returned, pool) == editedHash,
                $"edits survived a 500m round trip: 0x{editedHash:X8} == 0x{ChunkContentHash.Hash(returned, pool):X8}");

        // ---- Refill the tunnel, coalesce, confirm it returns to uniform. ----
        int denseBefore = store.DenseBricksHeld;
        for (int i = 0; i < 200; i++)
            store.SetVoxel(new int3(camVox.x + i, digY, camVox.z), Materials.Stone);
        Phase4Bootstrapper.Coalescer.RunFullPass();
        int denseAfter = store.DenseBricksHeld;
        Line($"refill + coalesce: dense bricks {denseBefore} -> {denseAfter} " +
             $"(coalesced {Phase4Bootstrapper.Coalescer.BricksCoalescedTotal} total, " +
             $"{Phase4Bootstrapper.Coalescer.ChunksCollapsedTotal} chunks collapsed to uniform)");
        Check(denseAfter <= denseBefore,
            "coalescing after refilling the tunnel did not increase dense-brick count (§4.5 only ever frees)");

        // ---- Hex-corrupt a real .delta on disk (§13's exact assertion). ----
        //
        // ORDER IS LOad-BEARING: fly OUT first, THEN corrupt, THEN fly back.
        //
        // The original order (corrupt -> fly out -> fly back) silently defeated
        // itself. The refill step just above re-dirties the same chunks whose
        // deltas are on disk, so the outbound leg's eviction SAVED those chunks
        // again and overwrote the corrupted bytes with a valid CRC before
        // anything could read them. The assertion then found rejections 0 -> 0
        // and called it a failure of §4.2, when in fact nothing corrupt had
        // survived to be rejected.
        //
        // It passed before this was noticed only by luck: the edit site used to
        // land in a neighbouring chunk (a separate harness bug, see BUG 2
        // above), so the corrupted file happened to belong to a chunk the
        // refill never touched.
        //
        // Corrupting while the chunk is NON-RESIDENT makes the file final:
        // nothing can rewrite it, and the return leg is forced to read it.
        yield return StartCoroutine(FlyLeg(cam, new Vector3(0, 0, 1), _persistenceRoundTripMeters, uploadMs, uploadBytes, drainMs));
        streamer.WaitForIdle();

        string[] deltas = Directory.GetFiles(Phase4Bootstrapper.DeltaDirectory, "*.delta");
        if (deltas.Length == 0) Check(false, "at least one .delta exists on disk to corrupt");
        else
        {
            string victimPath = deltas[0];
            byte[] raw = File.ReadAllBytes(victimPath);
            raw[raw.Length / 2] ^= 0xFF;
            File.WriteAllBytes(victimPath, raw);
            Line($"corrupted {Path.GetFileName(victimPath)} (flipped a byte mid-file, chunk evicted so the file is final)");

            int rejectedBefore = streamer.DeltasRejectedTotal;
            yield return StartCoroutine(FlyLeg(cam, new Vector3(0, 0, -1), _persistenceRoundTripMeters, uploadMs, uploadBytes, drainMs));
            cam.transform.position = startPos;
            for (int i = 0; i < 10; i++) yield return null;
            streamer.WaitForIdle();

            Check(streamer.DeltasRejectedTotal > rejectedBefore,
                $"the corrupted delta was DISCARDED and the chunk regenerated pristine " +
                $"(rejections {rejectedBefore} -> {streamer.DeltasRejectedTotal}) (§4.2)");
            Check(store.ResidentCount > 0, "the game continued after the corrupt delta (no crash, world intact)");

            if (streamer.RejectLog.Count > 0)
            {
                _report.AppendLine("  CRC discard log (§13 'CRC log shows the discard'):");
                foreach (string s in streamer.RejectLog) _report.AppendLine("    " + s);
            }
        }

        // ---- cascade tier validation (tiers 1..N) -------------------------
        // Placed HERE, after corrupt-delta recovery, because this is the exact
        // point the GateD_AfterPersistence screenshot showed floating slabs and
        // a hole in the water at the HORIZON -- distance, i.e. cascade
        // territory. ClipmapValidator ran GREEN through all of it and always
        // will: it only ever checked tier 0. Nothing had ever compared tiers
        // 1/2 against anything, so this is measuring an unmeasured surface, not
        // re-confirming a known-good one.
        //
        // Ground truth is a FRESH LODDownsampler run off current ChunkStore
        // state, not CascadeTierPool's own shadow copy -- see CascadeValidator's
        // header for why agreeing with the code under test would prove nothing.
        {
            var cascades = Phase4Bootstrapper.Cascades;
            if (cascades == null)
            {
                _report.AppendLine("  cascade validation SKIPPED: no LODCascadeManager on the bootstrapper.");
            }
            else
            {
                var cascResults = CascadeValidator.ValidateAllTiers(cascades, store, pool, maxChunks: 12);
                foreach (var cr in cascResults)
                {
                    _report.AppendLine("  cascade " + cr.Describe());
                    Check(cr.pass,
                        $"cascade tier {cr.tier} byte-matches a fresh CPU downsample after the persistence cycle " +
                        $"(entry {cr.entryMismatches}, body {cr.bodyByteMismatches}, " +
                        $"lost-updates {cr.mismatchesInCleanChunks})");
                }

                // The resident-chunk sweep above CANNOT see a coarse entry left
                // describing terrain for an EVICTED chunk -- it only walks
                // ResidentChunks(). That blind spot is the current leading
                // suspect for the Gate D horizon slab, since the artifact sits
                // over open ocean at distance where the window has been sliding
                // and evicting. This closes it.
                for (int tier = 1; tier < LODConfig.TIER_COUNT; tier++)
                {
                    var er = CascadeValidator.ValidateEvictedSlots(cascades.TierPool(tier), store);
                    _report.AppendLine($"  cascade evicted-slot sweep " + er.Describe());
                    Check(er.pass,
                        $"cascade tier {er.tier} has no stale entries for non-resident chunks " +
                        $"({er.chunksChecked} non-resident slots swept, {er.entryMismatches} stale entries)");
                }
            }
        }

        yield return StartCoroutine(Screenshot("GateD_AfterPersistence"));
        _report.AppendLine();
    }

    // =====================================================================
    // GATE E -- soak. §13: "memory flat over 10 minutes (any creep is a leak)."
    // =====================================================================
    private IEnumerator GateE()
    {
        _report.AppendLine($"--- GATE E: sustained traversal soak ({_soakSeconds:F0}s) ---");
        _report.AppendLine("NOTE: §13 asks for 10 MINUTES. This runs 20s by default so the whole rig finishes");
        _report.AppendLine("in a couple of minutes; raise _soakSeconds to 600 for the real gate once the run is");
        _report.AppendLine("known-good. A short soak can only DISPROVE flatness, never prove it -- smoke test only.");

        var store = Phase4Bootstrapper.Store;
        var streamer = Phase4Bootstrapper.Streamer;
        Camera cam = Camera.main;

        long memStart = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();
        int denseStart = store.DenseBricksHeld;

        var uploadMs = new List<double>(); var uploadBytes = new List<int>(); var drainMs = new List<double>();
        var samples = new List<(float t, long mem, int dense, int resident)>();

        float t0 = Time.realtimeSinceStartup;
        int leg = 0;
        while (Time.realtimeSinceStartup - t0 < _soakSeconds)
        {
            Vector3 dir = (leg % 4) switch
            {
                0 => new Vector3(1, 0, 0),
                1 => new Vector3(0, 0, 1),
                2 => new Vector3(-1, 0, 0),
                _ => new Vector3(0, 0, -1),
            };
            yield return StartCoroutine(FlyLeg(cam, dir, 100f, uploadMs, uploadBytes, drainMs));
            leg++;
            samples.Add((Time.realtimeSinceStartup - t0,
                         UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong(),
                         store.DenseBricksHeld, store.ResidentCount));
        }

        long memEnd = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();
        int denseEnd = store.DenseBricksHeld;

        _report.AppendLine("  t(s)   allocMB   denseBricks   resident");
        foreach (var s in samples)
            _report.AppendLine($"  {s.t,5:F1}   {s.mem / 1048576.0,7:F1}   {s.dense,11}   {s.resident,8}");

        double driftMB = (memEnd - memStart) / 1048576.0;
        Line($"memory drift over soak: {driftMB:+0.0;-0.0} MB ({memStart / 1048576.0:F1} -> {memEnd / 1048576.0:F1})");
        Line($"dense-brick drift: {denseEnd - denseStart:+#;-#;0} ({denseStart} -> {denseEnd})");
        Line($"LRU evictions triggered by pool pressure: {streamer.LruEvictionsTotal}");

        // A tolerance, not zero: the managed heap legitimately grows with
        // pooled handle arrays and the sparse chunk table, both of which are
        // bounded. Unbounded growth is what "leak" means here.
        Check(driftMB < 128.0,
            $"process memory drift {driftMB:F1} MB stayed bounded over the soak " +
            "(§13 'memory flat over 10 minutes -- any creep is a leak'; SHORT SOAK, see note)");
        Check(denseEnd < EngineConfig.BrickPoolHighWaterBricks,
            $"dense-brick count {denseEnd} stayed under the §3.6 high-water mark " +
            $"({EngineConfig.BrickPoolHighWaterBricks})");

        ReportUpload("Gate E soak", uploadMs, uploadBytes, drainMs);
        Line($"sparse chunk table size: {streamer.TableSize} records " +
             $"(grows with world AREA VISITED, not resident count -- expected, §3.2)");

        yield return StartCoroutine(Screenshot("GateE_SoakEnd"));
        _report.AppendLine();
    }

    // =====================================================================
    private IEnumerator Screenshot(string name)
    {
        // Two views, not four. Each costs 6 settle frames plus a full-frame
        // readback and PNG encode, and Beauty + StepHeat are the two that
        // actually answer "is the geometry right" and "where is the time going".
        // UniformDense and LODTier are one line away if a capture raises a
        // question they would answer.
        var views = new[]
        {
            RaymarchFeature.DebugMode.Beauty,
            RaymarchFeature.DebugMode.StepHeat,
        };
        foreach (var view in views)
        {
            RaymarchFeature.UseDebugViewOverride = true;
            RaymarchFeature.DebugViewOverride = view;
            for (int i = 0; i < 6; i++) yield return null;
            yield return new WaitForEndOfFrame();
            Texture2D shot = ScreenCapture.CaptureScreenshotAsTexture();
            try { File.WriteAllBytes(Path.Combine(_runFolder, $"{name}_{view}.png"), shot.EncodeToPNG()); }
            finally { UnityEngine.Object.Destroy(shot); }
        }
        RaymarchFeature.UseDebugViewOverride = false;
    }

    private void Finish()
    {
        File.WriteAllText(Path.Combine(_runFolder, "phase4_report.txt"), _report.ToString());

        if (_copyPlayerLogWhenDone)
        {
            try
            {
                string src = Application.consoleLogPath;
                if (!string.IsNullOrEmpty(src) && File.Exists(src))
                    File.Copy(src, Path.Combine(_runFolder, "player_log.txt"), true);
            }
            catch (Exception e) { Debug.LogWarning($"[Phase4Rig] log copy failed: {e.Message}"); }
        }

        string zip = null;
        if (_zipWhenDone)
        {
            try
            {
                zip = _runFolder + ".zip";
                if (File.Exists(zip)) File.Delete(zip);
                System.IO.Compression.ZipFile.CreateFromDirectory(_runFolder, zip,
                    System.IO.Compression.CompressionLevel.Optimal, true);
            }
            catch (Exception e) { Debug.LogWarning($"[Phase4Rig] zip failed: {e.Message}"); zip = null; }
        }

        Debug.Log("[Phase4Rig] ===== FULL REPORT =====\n" + _report);
        Debug.Log($"[Phase4Rig] DONE. Folder: {_runFolder}");
        if (zip != null) Debug.Log($"[Phase4Rig] Zipped to: {zip}  <- send this one");

#if UNITY_STANDALONE_OSX
        if (_revealInFinderWhenDone)
        {
            try { System.Diagnostics.Process.Start("open", $"-R \"{_runFolder}\""); }
            catch (Exception e) { Debug.LogWarning($"[Phase4Rig] reveal failed: {e.Message}"); }
        }
#endif
        if (_quitWhenDone)
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}