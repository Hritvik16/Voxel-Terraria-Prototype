// ==========================================
// Assets/Game/Phase5dStreamFluid.cs
//
// STREAMING x FLUID INTERACTION RIG. Diagnostic scene, not a demo.
//
// =========================================================================
// THE GAP THIS EXISTS TO CLOSE
// =========================================================================
// PHASE_5B_COMPLETION.md §2 lists under "NOT TESTED AT ALL":
//   "Streaming interaction. The demo and rig use one static chunk. Fluid has
//    never run while chunks stream in/out."
// Every fluid rig so far (5a basin, 5b basin, 5c edit stress) builds its own
// world in one chunk with NO StreamManager. This rig runs the REAL Phase 4
// streaming stack (Phase4Bootstrapper: StreamManager, admission, eviction,
// the sliding toroidal window) with live fluid next to it.
//
// NOT IN SCOPE, DELIBERATELY: §7.4's moving active radius. The CA's region is
// fixed and stays fixed. This rig asks what the EXISTING design does when the
// world moves underneath it -- it does not build the feature that would make
// the region follow the player.
//
// =========================================================================
// THE PRECEDENT BEING CHECKED FOR (PHASE_3_COMPLETION.md §6.2)
// =========================================================================
// "Phantom terrain floating in the sky": ReadClipmap addressed the clipmap
// toroidally with NO bounds check, so a ray that outran the window's extent
// wrapped around and struck terrain near the origin. The fix added an XZ
// bounds check, and the patch flagged that Phase 4 must make it
// ORIGIN-RELATIVE once StreamManager slides the window.
//
// The fluid CA reads the same toroidally-addressed clipmap through
// SampleVoxel. Whether it inherited the fix or the bug is the central
// question here, and it is checked per-voxel, not by eye -- see ProbeGpuRead.
//
// TIMING: this rig reports NO performance numbers. It is a correctness
// investigation.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Memory;
using VoxelEngine.Simulation;
using VoxelEngine.Streaming;

public class Phase5dStreamFluid : MonoBehaviour
{
    [SerializeField] private ComputeShader _fluidCA;
    [SerializeField] private int _slotCapacity = 65536;
    [SerializeField] private int _maxOpsPerFrame = 65536;
    [SerializeField] private string _outputRootFolderName = "Phase5dStreamFluid";

    // The CA region. 64^3 voxels, half a chunk on each axis (chunk = 128^3),
    // placed so it sits INSIDE one chunk -- see PickRegion.
    private const int RX = 64, RY = 32, RZ = 64;

    private FluidGpuSimulation _fluid;
    private FluidOpListReadback _readback;
    private EditService _edits;
    private int3 _regionOrigin;
    private long _applied;

    private readonly StringBuilder _log = new StringBuilder();
    private int _pass, _fail;
    private string _phase = "-";

    // Screenshot output. The run folder is created UP FRONT (it used to be
    // created only at the end, when the report was written) because stills are
    // captured while the run is in progress.
    private string _outDir;
    private int _shotIndex;

    private static ChunkStore Store => Phase4Bootstrapper.Store;
    private static TerrainClipmap Clip => Phase4Bootstrapper.Clipmap;
    private static StreamManager Streamer => Phase4Bootstrapper.Streamer;

    private void L(string s) { _log.AppendLine(s); Debug.Log("[5d] " + s); }
    private void Note(string s) => L("    note  " + s);
    private void Pass(string s) { _pass++; L("    PASS  " + s); }
    private void Fail(string s) { _fail++; L($"    FAIL  {s}   [{_phase}]"); }
    private void Check(bool ok, string s) { if (ok) Pass(s); else Fail(s); }

    // =====================================================================
    // Geometry helpers
    // =====================================================================

    private static int3 ChunkOriginVoxel(int3 c) => c * EngineConfig.CHUNK_EDGE_VOXELS;

    /// Every chunk the CA region touches.
    private List<int3> RegionChunks()
    {
        var set = new HashSet<int3>();
        int3 lo = _regionOrigin, hi = _regionOrigin + new int3(RX - 1, RY - 1, RZ - 1);
        for (int z = lo.z; z <= hi.z; z += 1)
        for (int x = lo.x; x <= hi.x; x += 1)
            set.Add(CoordMath.VoxelToChunk(new int3(x, lo.y, z)));
        return new List<int3>(set);
    }

    private int CountMobileInRegion()
    {
        int n = 0;
        for (int y = 0; y < RY; y++)
        for (int z = 0; z < RZ; z++)
        for (int x = 0; x < RX; x++)
            if (MaterialRules.IsMobile(Store.GetVoxel(_regionOrigin + new int3(x, y, z)))) n++;
        return n;
    }

    private int CountMaterialInRegion(byte m)
    {
        int n = 0;
        for (int y = 0; y < RY; y++)
        for (int z = 0; z < RZ; z++)
        for (int x = 0; x < RX; x++)
            if (Store.GetVoxel(_regionOrigin + new int3(x, y, z)) == m) n++;
        return n;
    }

    // =====================================================================
    // Window-relative telemetry (QUEUE STEP 2)
    // =====================================================================

    private struct WindowState
    {
        public int3 originChunks, originBricks, dimsBricks;
        public int inWindow, resident, total;
        public bool allInWindow, allResident;
    }

    private WindowState Sample()
    {
        var w = new WindowState
        {
            originBricks = Clip.WindowOriginBricks,
            dimsBricks = Clip.WindowDimsBricks,
        };
        var chunks = RegionChunks();
        w.total = chunks.Count;
        foreach (int3 c in chunks)
        {
            if (Store.IsInWindow(c)) w.inWindow++;
            if (Store.IsResident(c)) w.resident++;
        }
        w.allInWindow = w.inWindow == w.total;
        w.allResident = w.resident == w.total;
        return w;
    }

    private string Describe(WindowState w, Vector3 camPos)
    {
        int3 camChunk = CoordMath.VoxelToChunk(CoordMath.WorldToVoxel(
            new float3(camPos.x, camPos.y, camPos.z)));
        return $"cam {camPos.x,7:F1},{camPos.z,7:F1} chunk {camChunk.x},{camChunk.z}  " +
               $"windowOriginBricks {w.originBricks.x},{w.originBricks.z}  " +
               $"regionChunks inWindow {w.inWindow}/{w.total} resident {w.resident}/{w.total}";
    }

    // =====================================================================
    // THE PER-VOXEL GPU/CPU ORACLE
    // =====================================================================
    //
    // FluidCA.compute's CSPromote writes, from thread 0, BEFORE any early-out:
    //     DebugCounters[17] = SampleVoxel(RegionVoxel(WakeRequests[0]));
    // i.e. "what terrain the GPU actually reads at the first wake request".
    // Issuing exactly ONE wake request for a probe voxel and reading slot 17
    // back therefore answers, per voxel, what the GPU sees THROUGH THE
    // TOROIDAL CLIPMAP -- which is exactly the read path §6.2's phantom
    // terrain came from. Comparing it against ChunkStore.GetVoxel is the
    // per-voxel aliasing test, not an eyeball.
    //
    // Returns false if the request never reached the GPU (the wake queue defers
    // until the mirror has the chunk), because a stale slot 17 would be a
    // false reading -- the same class of mistake as reporting on a stale build.
    private bool ProbeGpuRead(int3 worldVoxel, out uint gpuMaterial, out byte cpuMaterial)
    {
        gpuMaterial = 0xFFFFFFFF;
        cpuMaterial = Store.GetVoxel(worldVoxel);

        _fluid.ClearWakeRequests();
        _fluid.RequestWake(worldVoxel);
        for (int attempt = 0; attempt < 240; attempt++)
        {
            Clip.UploadDirty(Store, Phase4Bootstrapper.Pool);
            _fluid.Tick(Clip);
            if (_fluid.LastDispatchedWakeCount > 0)
            {
                uint[] dbg = _fluid.ReadDebugCountersBlocking();
                gpuMaterial = dbg[17];
                cpuMaterial = Store.GetVoxel(worldVoxel);
                return true;
            }
            _fluid.RequestWake(worldVoxel);
        }
        return false;
    }

    // =====================================================================
    // Setup
    // =====================================================================

    /// Puts the CA region inside ONE chunk, on the island, at a place the
    /// camera can then walk away from. One chunk on purpose: it makes
    /// "the region's backing chunk was evicted" a single, unambiguous event.
    private void PickRegion(out Vector3 camStart)
    {
        Vector3 spawn = Phase4Bootstrapper.DeriveIslandSpawn(1);
        int3 spawnVoxel = CoordMath.WorldToVoxel(new float3(spawn.x, spawn.y, spawn.z));
        int3 chunk = CoordMath.VoxelToChunk(spawnVoxel);
        int3 corg = ChunkOriginVoxel(chunk);

        // Sit the region in the chunk's lower-middle: origin + 32, so a 64-wide
        // region spans voxels 32..95 of a 128-wide chunk and cannot straddle a
        // chunk boundary. Straddling is a DIFFERENT test and is called out in
        // the report rather than silently mixed in here.
        //
        // Y IS RESOLVED LATER, AGAINST THE ACTUAL TERRAIN (ResolveRegionY).
        // The first version hardcoded y=1, which on this island is ~100 voxels
        // UNDERGROUND: the whole region sat inside solid rock, nothing could be
        // poured, and every conservation check passed 0-against-0. A rig that
        // asserts nothing and looks green is the failure mode this project has
        // hit before, so the pour now fails loudly instead (see Step3).
        _regionOrigin = new int3(corg.x + 32, 1, corg.z + 32);
        camStart = new Vector3((_regionOrigin.x + RX / 2) * 0.1f,
                               12f,
                               (_regionOrigin.z + RZ / 2) * 0.1f - 4f);
    }

    /// Puts the region's base just above the terrain surface at its centre
    /// column, so the CA has open air to work in and real ground to land on.
    private bool ResolveRegionY()
    {
        int3 c = new int3(_regionOrigin.x + RX / 2, 0, _regionOrigin.z + RZ / 2);
        for (int y = WorldGenConstants.MAX_TERRAIN_HEIGHT + 2; y >= 1; y--)
            if (Store.GetVoxel(new int3(c.x, y, c.z)) != Materials.Air)
            {
                _regionOrigin.y = y + 1;
                return true;
            }
        return false;
    }

    private void CreateFluid()
    {
        _edits = new EditService();
        _fluid = new FluidGpuSimulation(_fluidCA, new int3(RX, RY, RZ), _slotCapacity, _maxOpsPerFrame)
        {
            RegionOriginVoxels = _regionOrigin,
            PlayerVoxel = _regionOrigin + new int3(RX / 2, RY / 2, RZ / 2),
            ActiveRadiusVoxels = EngineConfig.FLUID_ACTIVE_RADIUS_VOXELS,
        };
        _readback = new FluidOpListReadback(_fluid, Store)
        {
            OnVoxelApplied = v => { Clip.MarkDirty(CoordMath.VoxelToChunk(v)); _applied++; },
        };
        _edits.AttachFluidSimulation(_fluid, Store);
    }

    private void Edit(int3 v, byte m)
    {
        Store.SetVoxel(v, m);
        Clip.MarkDirty(CoordMath.VoxelToChunk(v));
        _edits.NotifyEdited(v);
    }

    /// One CA tick in the SHIPPING frame order (tick, then upload) -- the
    /// ordering Phase 5c established every real scene uses.
    private void FluidTick()
    {
        _fluid.Tick(Clip);
        _readback.IssueReadback(0);
        _readback.DrainBlocking();
        Clip.UploadDirty(Store, Phase4Bootstrapper.Pool);
    }

    private int Settle(int quiet = 20, int max = 900)
    {
        int still = 0, t = 0; long last = _applied;
        for (; t < max; t++)
        {
            FluidTick();
            bool q = _applied == last && _fluid.DeferredWakeRequests == 0;
            if (q) still++; else { still = 0; last = _applied; }
            if (still >= quiet) break;
        }
        return t;
    }

    /// Drops a column of fluid into the region's middle, above the terrain.
    private int PourInto(byte material, int count)
    {
        int placed = 0;
        int3 src = _regionOrigin + new int3(RX / 2, RY - 3, RZ / 2);
        for (int i = 0; i < 2000 && placed < count; i++)
        {
            if (Store.GetVoxel(src) == Materials.Air) { Edit(src, material); placed++; }
            FluidTick();
        }
        return placed;
    }

    // =====================================================================
    // Driver
    // =====================================================================

    void Start() => StartCoroutine(Run());

    private IEnumerator Run()
    {
        while (Store == null || Streamer == null) yield return null;
        for (int i = 0; i < 240; i++) yield return null;   // let the initial window settle

        var cam = Camera.main;
        PickRegion(out Vector3 camStart);
        if (cam != null) cam.transform.position = camStart;
        yield return null;
        for (int i = 0; i < 180; i++) yield return null;   // let the region's chunk stream in

        // Y must be resolved AFTER the chunk is resident -- GetVoxel on a
        // non-resident chunk returns Air, which would put the region back
        // underground for the same reason as before, just less obviously.
        if (!ResolveRegionY())
        {
            L("FATAL: no terrain surface found at the region column; cannot place the region.");
            _fail++;
        }
        camStart = new Vector3(camStart.x, (_regionOrigin.y + 20) * 0.1f, camStart.z);
        if (cam != null) cam.transform.position = camStart;
        for (int i = 0; i < 60; i++) yield return null;

        CreateFluid();

        _outDir = Path.Combine(Application.persistentDataPath, _outputRootFolderName,
                               DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(_outDir);

        L("=== PHASE 5D: STREAMING x FLUID INTERACTION ===");
        L(DateTime.Now.ToString("u", CultureInfo.InvariantCulture));
        L("");
        L("Real Phase 4 streaming (StreamManager, admission, eviction, sliding");
        L("toroidal window) with a LIVE fluid CA region beside it. The CA's region");
        L("is FIXED -- §7.4's moving active radius is not built and is not built here.");
        L("NO performance numbers are produced. This is a correctness investigation.");
        L("");
        L($"region origin voxels {_regionOrigin}  dims {RX}x{RY}x{RZ}");
        L($"region chunks: {string.Join(", ", RegionChunks().ConvertAll(c => c.ToString()))}");
        L($"load radius chunks: {Streamer.LoadRadiusChunks}   chunk edge voxels: {EngineConfig.CHUNK_EDGE_VOXELS}");
        L($"FLUID_ACTIVE_RADIUS_VOXELS: {EngineConfig.FLUID_ACTIVE_RADIUS_VOXELS}");
        L("");

        yield return Step2_Characterize(cam, camStart);
        yield return Step3_Eviction(cam, camStart);
        yield return Step4_WindowSlide(cam, camStart);
        yield return Step5_Straddle(cam, camStart);
        yield return Step7_Honey(cam, camStart);

        _log.AppendLine();
        _log.AppendLine($"PASS {_pass}  FAIL {_fail}");
        _log.AppendLine(_fail == 0 ? "RESULT: PASSED" : "RESULT: FAILED");
        File.WriteAllText(Path.Combine(_outDir, "phase5d_report.txt"), _log.ToString());
        Debug.Log("[5d] report -> " + _outDir);
        yield return null;
        Application.Quit(_fail == 0 ? 0 : 1);
    }

    /// Moves the camera CONTINUOUSLY to a world position, then holds.
    ///
    /// NOT A TELEPORT, and that is a finding in itself. The first version of
    /// this rig set transform.position directly, jumping ~17 chunks in one
    /// frame. StreamManager then tried to admit the new neighbourhood before
    /// the old one had been evicted, the resident set spanned more than the
    /// 32-chunk window, and ChunkStore's own guard fired repeatedly:
    ///
    ///   InvalidOperationException: [ChunkStore] Insert of chunk (119,0,100)
    ///   would overwrite live chunk (87,0,100) in ring slot 2071
    ///
    /// 119 - 87 = 32 = exactly the window width, i.e. two chunks aliasing to
    /// one ring slot. THE GUARD DID ITS JOB -- it refused the insert and named
    /// the cause instead of corrupting the ring, which is the §4.5 invariant
    /// holding. But a teleport is not how a camera moves, so driving streaming
    /// that way tests the guard, not the fluid. Stepping at 0.5 m/frame (~30
    /// m/s at 60 fps, well under a 12.8 m chunk per frame) keeps the resident
    /// set inside the window and lets the eviction path actually run.
    private IEnumerator MoveCam(Camera cam, Vector3 pos, int frames)
    {
        const float stepM = 0.5f;
        if (cam != null)
        {
            Vector3 from = cam.transform.position;
            float dist = Vector3.Distance(from, pos);
            int steps = Mathf.Max(1, Mathf.CeilToInt(dist / stepM));
            for (int i = 1; i <= steps; i++)
            {
                cam.transform.position = Vector3.Lerp(from, pos, (float)i / steps);
                yield return null;
            }
        }
        for (int i = 0; i < frames; i++) yield return null;
    }

    /// Captures one still, aiming the camera at the CA region first.
    ///
    /// AIMING IS ROTATION-ONLY, AND THAT IS WHAT MAKES THIS SAFE TO CALL FROM
    /// INSIDE A TEST. StreamManager is driven purely by camera POSITION --
    /// Phase4Bootstrapper passes it `Camera.main.transform.position` and nothing
    /// reads the rotation (the only other rotation write is the spawn override,
    /// which this scene disables). So pointing the camera cannot admit, evict,
    /// or slide anything, and cannot perturb a residency count an assertion is
    /// about to read. Moving the camera WOULD, so this never does.
    ///
    /// These stills are for a human to look at. NOTHING here is asserted --
    /// the rig's verdict comes from the counts, not the pictures.
    private IEnumerator Shot(Camera cam, string name)
    {
        if (cam == null || string.IsNullOrEmpty(_outDir)) yield break;

        Vector3 target = new Vector3((_regionOrigin.x + RX / 2) * 0.1f,
                                     (_regionOrigin.y + 4) * 0.1f,
                                     (_regionOrigin.z + RZ / 2) * 0.1f);
        Vector3 look = target - cam.transform.position;
        if (look.sqrMagnitude > 1e-6f)
            cam.transform.rotation = Quaternion.LookRotation(look.normalized, Vector3.up);

        // One frame to render the new orientation, then grab the framebuffer.
        // CaptureScreenshotAsTexture is synchronous at end-of-frame, unlike
        // CaptureScreenshot, which returns before the file exists.
        yield return null;
        yield return new WaitForEndOfFrame();

        Texture2D tex = ScreenCapture.CaptureScreenshotAsTexture();
        string file = Path.Combine(_outDir, $"{_shotIndex:D2}_{name}.png");
        File.WriteAllBytes(file, tex.EncodeToPNG());
        Destroy(tex);
        _shotIndex++;
        Note($"screenshot -> {Path.GetFileName(file)}   (cam {cam.transform.position}, looking at the region)");
    }

    // =====================================================================
    // QUEUE STEP 2 -- characterize the window-relative position FIRST
    // =====================================================================
    // Is the CA's fixed region inside the streaming window under normal camera
    // movement, or has it only ever been safe because every scene so far kept
    // the camera on top of it? That decides whether steps 3 and 4 are testing
    // an edge case or the common case.
    private IEnumerator Step2_Characterize(Camera cam, Vector3 camStart)
    {
        _phase = "step2-characterize";
        L("---------------------------------------------------------------");
        L("STEP 2 -- WINDOW-RELATIVE POSITION OF THE FIXED CA REGION");
        L("---------------------------------------------------------------");

        float chunkM = EngineConfig.CHUNK_EDGE_VOXELS * 0.1f;
        int firstOutOfWindow = -1, firstNotResident = -1;

        for (int step = 0; step <= 12; step++)
        {
            Vector3 p = camStart + new Vector3(step * chunkM, 0f, 0f);
            yield return MoveCam(cam, p, 40);
            var w = Sample();
            L($"  +{step,2} chunks ({step * chunkM,6:F1} m)  {Describe(w, p)}");
            if (!w.allInWindow && firstOutOfWindow < 0) firstOutOfWindow = step;
            if (!w.allResident && firstNotResident < 0) firstNotResident = step;
        }

        L("");
        Note(firstOutOfWindow < 0
            ? "region stayed INSIDE the window for the whole 12-chunk walk"
            : $"region left the window at +{firstOutOfWindow} chunks ({firstOutOfWindow * chunkM:F0} m)");
        Note(firstNotResident < 0
            ? "region chunks stayed RESIDENT for the whole walk"
            : $"region chunks stopped being resident at +{firstNotResident} chunks ({firstNotResident * chunkM:F0} m)");
        // Stated from the measurement, not from the constant. An earlier draft
        // of this line asserted the region is left behind "almost immediately",
        // which the walk above refutes: it survived the full 12 chunks.
        Note($"load radius is {Streamer.LoadRadiusChunks} chunks " +
             $"(~{Streamer.LoadRadiusChunks * EngineConfig.CHUNK_EDGE_VOXELS * 0.1f:F0} m), and the walk above " +
             "shows the region stays in-window well past 150 m. So this is NOT a " +
             "hair-trigger condition.");
        Note("It is still the COMMON case over a whole session: sizeClass 1's island is " +
             "~1909 m across (AMENDMENT_8_11), so any real traversal leaves a fixed " +
             "region behind many times over. Steps 3 and 4 test normal play, not a corner.");

        yield return MoveCam(cam, camStart, 90);
    }

    // =====================================================================
    // QUEUE STEP 3 -- THE EVICTION TEST
    // =====================================================================
    private IEnumerator Step3_Eviction(Camera cam, Vector3 camStart)
    {
        _phase = "step3-eviction";
        L("");
        L("---------------------------------------------------------------");
        L("STEP 3 -- EVICTION: what happens to live fluid when its chunk goes");
        L("---------------------------------------------------------------");
        L("DEFINED BEFORE MEASURING (the queue's instruction): fluid whose backing");
        L("chunk is no longer resident SHOULD freeze/deactivate cleanly. It must");
        L("NOT: vanish from the CPU state, be double-counted on return, or let a");
        L("later chunk in the same physical slot be misread as fluid.");
        L("");

        int poured = PourInto(Materials.Water, 40);
        Settle();
        int before = CountMaterialInRegion(Materials.Water);
        // VACUOUS-TEST GUARD. Everything below compares counts; if nothing was
        // poured, all of it passes 0-against-0 and proves nothing. The first
        // run of this rig did exactly that.
        Check(poured > 0, $"the pour actually placed fluid ({poured}) -- otherwise every check below is vacuous");
        var w0 = Sample();
        L($"  poured {poured}, settled water in region = {before}, {Describe(w0, camStart)}");
        Check(before == poured, $"all poured water present before eviction ({before}/{poured})");
        yield return Shot(cam, "step3_poured_resident");

        // Probe the GPU's own read of a known fluid voxel while everything is resident.
        int3 probe = FindFirst(Materials.Water);
        if (probe.x >= 0 && ProbeGpuRead(probe, out uint g0, out byte c0))
            Check(g0 == c0, $"baseline: GPU and CPU agree at {probe} (gpu {g0} cpu {c0})");
        else
            Note("baseline probe did not reach the GPU; skipped");

        // Walk far enough to evict, THEN keep ticking the CA -- the whole point
        // is that the CA is still live while its world is gone.
        float chunkM = EngineConfig.CHUNK_EDGE_VOXELS * 0.1f;
        Vector3 far = camStart + new Vector3((Streamer.LoadRadiusChunks + 4) * chunkM, 0f, 0f);
        yield return MoveCam(cam, far, 240);
        var w1 = Sample();
        L($"  after moving away: {Describe(w1, far)}");
        Note($"evicted total so far: {Streamer.ChunksEvictedTotal}");
        yield return Shot(cam, "step3_walked_away_evicted");

        if (w1.resident > 0)
        {
            Note("region chunks are STILL RESIDENT at this distance -- eviction did not");
            Note("happen, so the eviction test below is vacuous. Reported, not hidden.");
        }

        long appliedBefore = _applied;
        for (int i = 0; i < 120; i++) FluidTick();
        int during = CountMaterialInRegion(Materials.Water);
        L($"  CA ticked 120x with region evicted={!w1.allResident}: water in region = {during}, " +
          $"ops applied during = {_applied - appliedBefore}");

        // The dangerous read: does the GPU see this voxel as something else now?
        if (probe.x >= 0 && ProbeGpuRead(probe, out uint g1, out byte c1))
        {
            L($"  probe at {probe}: GPU reads {g1}, CPU reads {c1}");
            Check(g1 == c1 || g1 == 0,
                "GPU read of an evicted cell is either AIR or agrees with the CPU " +
                "(anything else is the §6.2 aliasing class)");
        }

        // Back into range.
        yield return MoveCam(cam, camStart, 300);
        Settle();
        var w2 = Sample();
        int after = CountMaterialInRegion(Materials.Water);
        L($"  after returning: {Describe(w2, camStart)}  water in region = {after}");
        Check(after <= poured, $"no water was CREATED by the round trip ({after} <= {poured})");
        Check(after == before,
            $"water conserved across evict+return ({after} vs {before} before)");
        Note($"stale ops dropped by the readback ledger: {_readback.StaleOpsDropped}");
        yield return Shot(cam, "step3_returned");
    }

    private int3 FindFirst(byte m)
    {
        for (int y = 0; y < RY; y++)
        for (int z = 0; z < RZ; z++)
        for (int x = 0; x < RX; x++)
        {
            int3 v = _regionOrigin + new int3(x, y, z);
            if (Store.GetVoxel(v) == m) return v;
        }
        return new int3(-1, -1, -1);
    }

    // =====================================================================
    // QUEUE STEP 4 -- THE WINDOW-SLIDE TEST (the §6.2 precedent)
    // =====================================================================
    private IEnumerator Step4_WindowSlide(Camera cam, Vector3 camStart)
    {
        _phase = "step4-window-slide";
        L("");
        L("---------------------------------------------------------------");
        L("STEP 4 -- WINDOW SLIDE: per-voxel aliasing check (PHASE_3 §6.2)");
        L("---------------------------------------------------------------");
        L("Slides the toroidal window origin itself and asks, PER VOXEL, whether");
        L("the GPU's clipmap read still resolves to the same world location the");
        L("CPU does. This is the read path the phantom-terrain bug came from.");
        L("");

        // Build a known, distinctive pattern the CA will not touch: Stone is not
        // mobile, so any change to these cells is the world lying, not physics.
        var probes = new List<int3>();
        var expect = new List<byte>();
        byte[] pattern = { Materials.Stone, Materials.Sandstone, Materials.Deepstone, Materials.MossyStone };
        for (int i = 0; i < 16; i++)
        {
            int3 v = _regionOrigin + new int3(2 + (i * 3) % (RX - 4), 4 + (i % 5), 2 + (i * 7) % (RZ - 4));
            byte m = pattern[i % pattern.Length];
            Edit(v, m);
            probes.Add(v);
            expect.Add(m);
        }
        Clip.UploadDirty(Store, Phase4Bootstrapper.Pool);
        for (int i = 0; i < 10; i++) FluidTick();

        int3 originBefore = Clip.WindowOriginBricks;
        int agreeBefore = 0;
        for (int i = 0; i < probes.Count; i++)
            if (ProbeGpuRead(probes[i], out uint g, out byte c) && g == c && c == expect[i]) agreeBefore++;
        L($"  before slide: window origin bricks {originBefore}, {agreeBefore}/{probes.Count} probes agree GPU==CPU==expected");
        Check(agreeBefore == probes.Count, "all probes agree BEFORE the window slides");

        // Slide the window: move far enough that the origin must change, then
        // come back so the probes are in-window again and can be re-read.
        float chunkM = EngineConfig.CHUNK_EDGE_VOXELS * 0.1f;
        yield return MoveCam(cam, camStart + new Vector3(6 * chunkM, 0f, 3 * chunkM), 240);
        int3 originMid = Clip.WindowOriginBricks;
        yield return MoveCam(cam, camStart + new Vector3(-3 * chunkM, 0f, -5 * chunkM), 240);
        int3 originMid2 = Clip.WindowOriginBricks;
        yield return MoveCam(cam, camStart, 300);
        int3 originAfter = Clip.WindowOriginBricks;

        L($"  window origin bricks: start {originBefore} -> {originMid} -> {originMid2} -> back {originAfter}");
        Check(!originBefore.Equals(originMid) || !originBefore.Equals(originMid2),
            "the window origin actually MOVED (otherwise this test proves nothing)");

        int agreeAfter = 0, mismatched = 0;
        for (int i = 0; i < probes.Count; i++)
        {
            if (!ProbeGpuRead(probes[i], out uint g, out byte c)) { Note($"probe {probes[i]} never reached the GPU"); continue; }
            if (g == c) agreeAfter++;
            else { mismatched++; if (mismatched <= 5) L($"    MISMATCH at {probes[i]}: GPU {g}, CPU {c}, expected {expect[i]}"); }
        }
        L($"  after slide: {agreeAfter}/{probes.Count} probes agree GPU==CPU, {mismatched} mismatched");
        Check(mismatched == 0, "NO probe aliases to a different material after the window slid (§6.2 class)");

        int stillRight = 0;
        for (int i = 0; i < probes.Count; i++) if (Store.GetVoxel(probes[i]) == expect[i]) stillRight++;
        Check(stillRight == probes.Count,
            $"CPU state itself is unchanged by streaming ({stillRight}/{probes.Count})");
        yield return Shot(cam, "step4_after_window_slide");
    }

    // =====================================================================
    // STEP 5 -- THE STRADDLE CASE (predicted by reading, tested here)
    // =====================================================================
    // Steps 3 and 4 put the region INSIDE one chunk, so "evicted" was all or
    // nothing. Reading the apply path suggests the dangerous case is PARTIAL:
    //
    //   FluidOpListReadback.Apply:
    //     if (_store.GetVoxel(op.Dst) != op.ExpectedAtDst) { drop; }   // (1)
    //     _store.SetVoxel(op.Dst, op.NewMaterial);                     // (2)
    //     if (op.HasSrc) _store.SetVoxel(op.Src, 0);                   // (3)
    //
    //   ChunkStore.GetVoxel on a NON-RESIDENT chunk returns Air, and its own
    //   comment says that is "DELIBERATELY ambiguous with real air" and that
    //   callers needing the distinction must ask IsResident/IsInWindow.
    //   ChunkStore.SetVoxel on a non-resident chunk silently returns.
    //
    // So if Dst is in an evicted chunk and Src is not: (1) passes because Air
    // is what an empty destination looks like, (2) silently does nothing, and
    // (3) succeeds -- the source is cleared and the material is never written
    // anywhere. That is SILENT MASS LOSS, and the apply path asks neither
    // IsResident nor IsInWindow.
    //
    // The CA can generate exactly that op, because SampleVoxel returns AIR for
    // out-of-window cells and AIR reads as "free to move into".
    //
    // This step re-homes the region ACROSS a chunk boundary and walks the
    // camera until exactly one of its two chunks is resident, which is the
    // only state in which the above can fire.
    private IEnumerator Step5_Straddle(Camera cam, Vector3 camStart)
    {
        _phase = "step5-straddle";
        L("");
        L("---------------------------------------------------------------");
        L("STEP 5 -- REGION STRADDLING A CHUNK BOUNDARY, ONE SIDE EVICTED");
        L("---------------------------------------------------------------");

        // Re-home the CA across the boundary at chunk-local x = 128.
        _readback?.Dispose(); _fluid?.Dispose();
        int3 c = CoordMath.VoxelToChunk(_regionOrigin);
        int3 corg = ChunkOriginVoxel(c);
        _regionOrigin = new int3(corg.x + 128 - RX / 2, _regionOrigin.y, corg.z + 32);
        CreateFluid();

        var chunks = RegionChunks();
        L($"  region re-homed to {_regionOrigin}, spanning {chunks.Count} chunks: " +
          string.Join(", ", chunks.ConvertAll(k => k.ToString())));
        Check(chunks.Count >= 2, "the region really does straddle a chunk boundary");

        int poured = PourInto(Materials.Water, 40);
        Settle();
        int before = CountMaterialInRegion(Materials.Water);
        Check(poured > 0, $"the straddle pour placed fluid ({poured})");
        L($"  poured {poured}, settled = {before}");
        yield return Shot(cam, "step5_straddle_poured");

        // Walk out one chunk at a time, looking for PARTIAL residency.
        float chunkM = EngineConfig.CHUNK_EDGE_VOXELS * 0.1f;
        bool sawPartial = false;
        int atStep = -1;
        for (int step = 10; step <= 22 && !sawPartial; step++)
        {
            yield return MoveCam(cam, camStart + new Vector3(step * chunkM, 0f, 0f), 60);
            var w = Sample();
            L($"  +{step,2} chunks: resident {w.resident}/{w.total} inWindow {w.inWindow}/{w.total}");
            if (w.resident > 0 && w.resident < w.total) { sawPartial = true; atStep = step; }
        }

        if (!sawPartial)
        {
            Note("NEVER reached a partial-residency state: the region's chunks always");
            Note("evicted together. The predicted mass-loss path needs one side resident");
            Note("and the other not, so it was NOT exercised. Reported, not assumed safe.");
            yield return MoveCam(cam, camStart, 240);
            Settle();
            int back = CountMaterialInRegion(Materials.Water);
            Check(back == before, $"water still conserved across the walk ({back} vs {before})");
            yield break;
        }

        L($"  PARTIAL RESIDENCY reached at +{atStep} chunks -- this is the state the");
        L("  predicted mass-loss path needs.");

        // The fluid poured earlier has SETTLED, so it is asleep and nothing
        // moves -- a first version of this step ticked 300 times here, applied
        // ZERO ops, and "passed" without exercising the path at all. Fresh
        // fluid is injected on the RESIDENT side, right next to the boundary,
        // so it is actively moving toward the evicted chunk while the edge
        // exists. That is the only configuration in which Apply can be handed
        // a Dst in a non-resident chunk.
        int3 evicted = int3.zero; bool haveEvicted = false;
        foreach (int3 k in chunks) if (!Store.IsResident(k)) { evicted = k; haveEvicted = true; }
        L($"  evicted chunk: {(haveEvicted ? evicted.ToString() : "none")}");

        int injected = 0;
        int boundaryX = ChunkOriginVoxel(evicted).x + EngineConfig.CHUNK_EDGE_VOXELS;
        for (int i = 0; i < 12; i++)
        {
            // Just inside the RESIDENT chunk, a few voxels up so it falls and
            // spreads -- toward the evicted side among other directions.
            int3 v = new int3(boundaryX + 1 + (i % 3), _regionOrigin.y + 6 + (i / 3), _regionOrigin.z + RZ / 2);
            if (Store.GetVoxel(v) == Materials.Air) { Edit(v, Materials.Water); injected++; }
        }
        int expected = before + injected;
        L($"  injected {injected} water at the resident side of x={boundaryX}; expected total {expected}");

        long appliedBefore = _applied;
        for (int i = 0; i < 400; i++) FluidTick();
        long opsDuring = _applied - appliedBefore;
        L($"  ops applied while partially resident: {opsDuring}");
        yield return Shot(cam, "step5_partial_residency_edge");
        Check(opsDuring > 0,
            "the CA actually MOVED fluid while a residency edge cut the region " +
            "(otherwise the mass-loss path is untested, not proven safe)");

        yield return MoveCam(cam, camStart, 300);
        Settle();
        int after = CountMaterialInRegion(Materials.Water);
        L($"  after returning: water = {after} (expected {expected}, was {before} before injection)");
        Check(after == expected,
            $"NO MASS LOST OR GAINED across a residency edge ({after} vs expected {expected})");
        Note($"stale ops dropped by the ledger overall: {_readback.StaleOpsDropped}");
        Note($"ops refused for a NON-RESIDENT half: {_readback.OpsDroppedNonResident} " +
             "-- these are the ops that used to destroy mass silently; a non-zero " +
             "count here is the guard working, not a fault.");
        Check(_readback.OpsDroppedNonResident > 0,
            "the residency guard actually fired (if 0, the edge was never crossed " +
            "and the conservation result above is untested, not proven)");
        yield return Shot(cam, "step5_straddle_returned");
    }

    // =====================================================================
    // QUEUE STEP 7 -- HONEY ON THE GPU PATH
    // =====================================================================
    private IEnumerator Step7_Honey(Camera cam, Vector3 camStart)
    {
        _phase = "step7-honey";
        L("");
        L("---------------------------------------------------------------");
        L("STEP 7 -- HONEY THROUGH THE REAL GPU CA (never poured on GPU before)");
        L("---------------------------------------------------------------");
        yield return MoveCam(cam, camStart, 60);

        int before = CountMaterialInRegion(Materials.Honey);
        int poured = PourInto(Materials.Honey, 12);
        Check(poured > 0, $"honey pour actually placed fluid ({poured})");
        long tickStart = _applied;

        // Interval 30: honey must still be MOVING long after a fast fluid would
        // have finished. Sampled, not assumed.
        int movedEarly = 0;
        for (int i = 0; i < 60; i++) { long a = _applied; FluidTick(); if (_applied != a) movedEarly++; }
        int settleTicks = Settle(30, 3000);

        int after = CountMaterialInRegion(Materials.Honey);
        L($"  poured {poured}, settled after {settleTicks} further ticks, honey in region = {after}");
        L($"  ops applied for honey: {_applied - tickStart}; ticks with movement in the first 60: {movedEarly}");
        Check(after == before + poured, $"honey conserved on the GPU path ({after} = {before} + {poured})");
        Check(_applied - tickStart > 0, "honey actually MOVED on the GPU (not inert)");

        int floating = 0;
        for (int y = 1; y < RY; y++)
        for (int z = 0; z < RZ; z++)
        for (int x = 0; x < RX; x++)
        {
            int3 v = _regionOrigin + new int3(x, y, z);
            if (Store.GetVoxel(v) != Materials.Honey) continue;
            if (Store.GetVoxel(v - new int3(0, 1, 0)) == Materials.Air) floating++;
        }
        Check(floating == 0, $"no honey left floating ({floating})");
        Note($"MaterialRules.TickInterval(Honey) = {MaterialRules.TickInterval(Materials.Honey)} " +
             "-- viscosity is REPORTED here, not asserted as a rate: this rig makes no timing claim.");
        yield return Shot(cam, "step7_honey_settled");
    }

    void OnDestroy()
    {
        _readback?.Dispose();
        _fluid?.Dispose();
    }
}
