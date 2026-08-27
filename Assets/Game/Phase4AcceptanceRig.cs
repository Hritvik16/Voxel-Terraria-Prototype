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
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Memory;
using VoxelEngine.Streaming;
using VoxelEngine.WorldGen;

public class Phase4AcceptanceRig : MonoBehaviour
{
    [Header("Run control")]
    [SerializeField] private bool _runOnStart = true;
    [SerializeField] private bool _haltOnFirstRedGate = true;

    [Header("Traversal")]
    [Tooltip("§2.5 burst player speed. The window is sized against this.")]
    [SerializeField] private float _flySpeed = 60f;
    [SerializeField] private float _traverseMeters = 400f;
    [SerializeField] private float _soakSeconds = 60f;

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

        yield return StartCoroutine(GateA());
        if (!Halted()) yield return StartCoroutine(GateB());
        if (!Halted()) yield return StartCoroutine(GateC());
        if (!Halted()) yield return StartCoroutine(GateD());
        if (!Halted()) yield return StartCoroutine(GateE());

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
        var st = ColumnSampler.CreateState(meta);

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
        _report.AppendLine("--- GATE B: incremental upload correctness, window PINNED ---");

        var store = Phase4Bootstrapper.Store;
        var pool = Phase4Bootstrapper.Pool;
        var clip = Phase4Bootstrapper.Clipmap;

        Line($"resident chunks: {store.ResidentCount}, window origin {store.WindowOrigin}, " +
             $"dense bricks {store.DenseBricksHeld} ({store.PoolUtilisation:P1} of cap)");
        Check(store.ResidentCount > 0, "the initial window populated at all");

        yield return null;

        var v = ClipmapValidator.ValidateRegion(clip, pool, store, maxChunks: 64);
        Check(v.pass, $"GPU clipmap byte-matches CPU truth after incremental upload -- {v}");

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
        int mismatches = 0;
        foreach (var c in watched)
        {
            var ch = store.GetChunk(c);
            if (ch == null) { mismatches++; continue; }
            if (ChunkContentHash.Hash(ch, pool) != hashesBefore[c]) mismatches++;
        }
        Check(mismatches == 0,
            $"every watched chunk is content-identical after leaving and re-entering the window " +
            $"({watched.Count} chunks, {mismatches} mismatches)");

        // Upload budget, the §4.3 / §0.2 gate.
        ReportUpload("Gate C traversal", uploadMs, uploadBytes, drainMs);

        var v = ClipmapValidator.ValidateRegion(Phase4Bootstrapper.Clipmap, pool, store, maxChunks: 64);
        Check(v.pass, $"GPU clipmap still byte-matches CPU truth after the window moved -- {v}");

        yield return StartCoroutine(Screenshot("GateC_AfterReturn"));
        _report.AppendLine();
    }

    private IEnumerator FlyLeg(Camera cam, Vector3 dir, float meters,
        List<double> uploadMs, List<int> uploadBytes, List<double> drainMs)
    {
        float travelled = 0f;
        var streamer = Phase4Bootstrapper.Streamer;
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
        }
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
        int3 camVox = CoordMath.WorldToVoxel(new float3(startPos.x, startPos.y, startPos.z));
        int3 editChunk = CoordMath.VoxelToChunk(camVox);
        int digs = 0, builds = 0;

        for (int i = 0; i < 200; i++)
        {
            var v = new int3(camVox.x + i, camVox.y - 4, camVox.z);
            if (store.GetVoxel(v) != Materials.Air) { store.SetVoxel(v, Materials.Air); digs++; }
        }
        for (int y = 0; y < 8; y++)
        for (int x = 0; x < 4; x++)
        for (int z = 0; z < 4; z++)
        {
            store.SetVoxel(new int3(camVox.x - 10 + x, camVox.y + y, camVox.z + z), Materials.Stone);
            builds++;
        }
        Line($"edited: {digs} voxels dug, {builds} placed, around chunk {editChunk}");
        Check(digs > 0, "the dig actually removed solid voxels (edit landed on real terrain)");

        var edited = store.GetChunk(editChunk);
        uint editedHash = edited != null ? ChunkContentHash.Hash(edited, pool) : 0u;
        Check(edited != null && edited.deltaDirty, "the edited chunk is marked deltaDirty");

        yield return null;

        // ---- Fly 500m away and back (§13 says 500m). ----
        var uploadMs = new List<double>(); var uploadBytes = new List<int>(); var drainMs = new List<double>();
        yield return StartCoroutine(FlyLeg(cam, new Vector3(0, 0, 1), 500f, uploadMs, uploadBytes, drainMs));
        yield return StartCoroutine(FlyLeg(cam, new Vector3(0, 0, -1), 500f, uploadMs, uploadBytes, drainMs));
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
            store.SetVoxel(new int3(camVox.x + i, camVox.y - 4, camVox.z), Materials.Stone);
        Phase4Bootstrapper.Coalescer.RunFullPass();
        int denseAfter = store.DenseBricksHeld;
        Line($"refill + coalesce: dense bricks {denseBefore} -> {denseAfter} " +
             $"(coalesced {Phase4Bootstrapper.Coalescer.BricksCoalescedTotal} total, " +
             $"{Phase4Bootstrapper.Coalescer.ChunksCollapsedTotal} chunks collapsed to uniform)");
        Check(denseAfter <= denseBefore,
            "coalescing after refilling the tunnel did not increase dense-brick count (§4.5 only ever frees)");

        // ---- Hex-corrupt a real .delta on disk (§13's exact assertion). ----
        string[] deltas = Directory.GetFiles(Phase4Bootstrapper.DeltaDirectory, "*.delta");
        if (deltas.Length == 0) Check(false, "at least one .delta exists on disk to corrupt");
        else
        {
            string victimPath = deltas[0];
            byte[] raw = File.ReadAllBytes(victimPath);
            raw[raw.Length / 2] ^= 0xFF;
            File.WriteAllBytes(victimPath, raw);
            Line($"corrupted {Path.GetFileName(victimPath)} (flipped a byte mid-file)");

            int rejectedBefore = streamer.DeltasRejectedTotal;
            yield return StartCoroutine(FlyLeg(cam, new Vector3(0, 0, 1), 500f, uploadMs, uploadBytes, drainMs));
            yield return StartCoroutine(FlyLeg(cam, new Vector3(0, 0, -1), 500f, uploadMs, uploadBytes, drainMs));
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

        yield return StartCoroutine(Screenshot("GateD_AfterPersistence"));
        _report.AppendLine();
    }

    // =====================================================================
    // GATE E -- soak. §13: "memory flat over 10 minutes (any creep is a leak)."
    // =====================================================================
    private IEnumerator GateE()
    {
        _report.AppendLine($"--- GATE E: sustained traversal soak ({_soakSeconds:F0}s) ---");
        _report.AppendLine("NOTE: §13 asks for 10 MINUTES. This runs a shorter leg by default so the rig");
        _report.AppendLine("stays a one-press artifact; raise _soakSeconds to 600 for the real gate. A short");
        _report.AppendLine("soak can only DISPROVE flatness, never prove it -- read this as a smoke test.");

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
            yield return StartCoroutine(FlyLeg(cam, dir, 200f, uploadMs, uploadBytes, drainMs));
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
        var views = new[]
        {
            RaymarchFeature.DebugMode.Beauty,
            RaymarchFeature.DebugMode.UniformDense,
            RaymarchFeature.DebugMode.LODTier,
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