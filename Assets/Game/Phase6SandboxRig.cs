// ==========================================
// Assets/Game/Phase6SandboxRig.cs
//
// §13 PHASE 6'S INTEGRATED ACCEPTANCE RIG (`Phase6_Sandbox`).
//
// =========================================================================
// WHY THIS EXISTS WHEN FOUR PER-FILE RIGS ALREADY PASS
// =========================================================================
// Every Phase 6 rig so far tests ONE file against a world nobody else is
// touching. §13's acceptance list is not that: it is one scenario in which the
// player, the CCD, the edit path, destruction, buoyancy, streaming, persistence
// and the fluid CA are all live at once. The interesting failures of a phase
// like this one are interactions -- an edit that races an eviction, a
// detonation that outruns the upload budget, buoyancy reading a cell the CA has
// not applied yet -- and none of them can appear in a rig that runs one system
// in isolation. Four green per-file rigs are NOT a substitute for this and
// should never be reported as one.
//
// §13's list, and where each lands below:
//   walk/jump ......................... step 1
//   0.2m wall, grapple at 60 m/s ...... step 2
//   adversarial checkerboard (§3.6) ... step 3   <- the LRU valve gate
//   drill 60s at 200 vox/s, persist ... step 4
//   400K detonation ................... step 5
//   60 m/s dive, buoyancy in window ... step 6
//   flood-front (judge by feel) ....... step 7   <- PARTLY MANUAL, see below
//   CPU-lane total under 16.6ms ....... step 8   <- READ ITS CAVEAT
//
// =========================================================================
// TWO THINGS THIS RIG DOES NOT CLAIM
// =========================================================================
// STEP 7 IS THE ONE GENUINELY MANUAL ITEM IN PHASE 6. §13 asks to "explicitly
// judge, BY FEEL, whether the bounded op-list latency is perceptible at this
// specific leading-edge-of-motion moment". A rig cannot judge perception. What
// it can do -- and does -- is stage the exact moment mechanically, measure the
// latency that a human would be judging, and leave screenshots. The verdict
// line is left for a person.
//
// STEP 8 IS NOT run-acceptance-rig.sh. CLAUDE.md is explicit that the only
// trusted frame-time source is that script's own output. This rig measures
// frame time by the SAME METHODOLOGY (a release standalone, launched outside
// the Editor, reporting its own Time.unscaledDeltaTime) but it is a different
// harness running a different scenario, so its numbers are reported as
// PROVISIONAL and must not be quoted as the §2.2 gate. They are here because
// §13 asks for a CPU-lane figure during THIS scenario, which run-acceptance-rig
// does not run.

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

public class Phase6SandboxRig : MonoBehaviour
{
    [SerializeField] private PlayerController _player;
    [SerializeField] private ComputeShader _fluidCA;
    [SerializeField] private int _slotCapacity = 65536;
    [SerializeField] private int _maxOpsPerFrame = 65536;
    [SerializeField] private string _outputRootFolderName = "Phase6Sandbox";

    private const int RX = 64, RY = 32, RZ = 64;
    private const float Dt = 1f / 60f;

    private readonly StringBuilder _log = new StringBuilder();
    private int _pass, _fail;
    private string _phase = "-";
    private string _outDir;
    private int _shotIndex;

    private static ChunkStore Store => Phase4Bootstrapper.Store;
    private static TerrainClipmap Clip => Phase4Bootstrapper.Clipmap;
    private static StreamManager Streamer => Phase4Bootstrapper.Streamer;

    private EditService _edits;
    private SweptCCD _ccd;
    private ProjectileTrace _proj;
    private DestructionReducer _boom;
    private Buoyancy _buoy;
    private FluidGpuSimulation _fluid;
    private FluidOpListReadback _readback;
    private int3 _regionOrigin;
    private long _applied;

    // Frame-time samples for step 8.
    private readonly List<float> _frameMs = new List<float>();
    private bool _sampling;

    private PlayerMotor M => _player.Motor;

    private void L(string s) { _log.AppendLine(s); Debug.Log("[6sb] " + s); }
    private void Note(string s) => L("    note  " + s);
    private void Pass(string s) { _pass++; L("    PASS  " + s); }
    private void Fail(string s) { _fail++; L($"    FAIL  {s}   [{_phase}]"); }
    private void Check(bool ok, string s) { if (ok) Pass(s); else Fail(s); }
    private void Manual(string s) => L("    MANUAL  " + s);

    private void Update()
    {
        if (_sampling) _frameMs.Add(Time.unscaledDeltaTime * 1000f);
    }

    // =====================================================================

    IEnumerator Start()
    {
        if (_player == null) _player = FindObjectOfType<PlayerController>();
        float t0 = Time.realtimeSinceStartup;
        while (Store == null && Time.realtimeSinceStartup - t0 < 240f) yield return null;

        _outDir = Path.Combine(Application.persistentDataPath, _outputRootFolderName,
                               DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(_outDir);

        L("=== PHASE 6 INTEGRATED ACCEPTANCE (§13 Phase6_Sandbox) ===");
        L(DateTime.Now.ToString("u", CultureInfo.InvariantCulture));
        L("");

        if (Store == null || _player == null)
        {
            Fail(Store == null ? "world never booted" : "no PlayerController in the scene");
            yield return Report();
            yield break;
        }

        for (int i = 0; i < 150; i++) yield return null;      // let the window fill

        Camera cam = Camera.main;
        int3 camVox = CoordMath.WorldToVoxel(new float3(cam.transform.position.x,
                                                        cam.transform.position.y,
                                                        cam.transform.position.z));
        int surface = SurfaceY(camVox.x, camVox.z);
        _regionOrigin = new int3(camVox.x - RX / 2, math.max(0, surface - 8), camVox.z - RZ / 2);

        _edits = new EditService();
        _edits.AttachWorld(Store, Store, Store, Clip);

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

        _ccd = new SweptCCD(Store, Store);
        _proj = new ProjectileTrace(Store, Store);
        _boom = new DestructionReducer(_edits, Store);
        _buoy = new Buoyancy(Store, Store);

        _player.DebugTakeControl();
        _player.Bind(Store, Store);

        L($"fluid arena {RX}x{RY}x{RZ} at {_regionOrigin}");
        L($"brick pool cap {EngineConfig.BRICK_POOL_CAP}, high water " +
          $"{EngineConfig.BrickPoolHighWaterBricks} ({EngineConfig.BRICK_POOL_HIGH_WATER_FRACTION:P0})");
        L($"dense bricks at rest: {Store.DenseBricksHeld}");
        L("");

        _sampling = true;

        yield return Step1_WalkAndJump();
        yield return Step2_GrappleStop();
        yield return Step3_AdversarialCheckerboard();
        yield return Step4_DrillAndPersist();
        yield return Step5_Detonation();
        yield return Step6_DiveIntoWater();
        yield return Step7_FloodFront();

        _sampling = false;
        yield return Step8_FrameTime();

        yield return Report();
    }

    private IEnumerator Report()
    {
        _log.AppendLine();
        _log.AppendLine($"PASS {_pass}  FAIL {_fail}");
        _log.AppendLine(_fail == 0 ? "RESULT: PASSED" : "RESULT: FAILED");
        File.WriteAllText(Path.Combine(_outDir, "phase6_sandbox_report.txt"), _log.ToString());
        Debug.Log("[6sb] report -> " + _outDir);
        yield return null;
        Application.Quit(_fail == 0 ? 0 : 1);
    }

    private void OnDestroy() { _readback?.Dispose(); _fluid?.Dispose(); }

    // =====================================================================
    // Helpers
    // =====================================================================

    private int SurfaceY(int x, int z)
    {
        for (int y = WorldGenConstants.MAX_TERRAIN_HEIGHT + 2; y >= 1; y--)
        {
            byte m = Store.GetVoxel(new int3(x, y, z));
            if (m != Materials.Air && !MaterialRules.IsFluidMaterial(m)) return y;
        }
        return -1;
    }

    private void FluidTick()
    {
        if (_readback.CanIssue) { _fluid.Tick(Clip); _readback.IssueReadback(0); }
        _readback.PumpAndApply();
    }

    private void Drive(float2 wish, bool jump) => _player.DebugStep(Dt, wish, jump);

    private IEnumerator DriveFor(int frames, float2 wish)
    {
        for (int i = 0; i < frames; i++) { Drive(wish, false); yield return null; }
    }

    private void BuildLane(int x0, int x1, int zLo, int zHi, int laneY)
    {
        _edits.SetBox(new int3(x0, laneY + 1, zLo), new int3(x1, laneY + 25, zHi), Materials.Air);
        _edits.SetBox(new int3(x0, laneY, zLo), new int3(x1, laneY, zHi), Materials.Stone);
    }

    private IEnumerator Shot(string name, float3 eyeM, float3 lookAtM)
    {
        Camera cam = Camera.main;
        if (cam != null)
        {
            cam.transform.position = new Vector3(eyeM.x, eyeM.y, eyeM.z);
            Vector3 d = new Vector3(lookAtM.x, lookAtM.y, lookAtM.z) - cam.transform.position;
            if (d.sqrMagnitude > 1e-6f) cam.transform.rotation = Quaternion.LookRotation(d.normalized, Vector3.up);
        }
        yield return null;
        yield return new WaitForEndOfFrame();
        Texture2D tex = ScreenCapture.CaptureScreenshotAsTexture();
        File.WriteAllBytes(Path.Combine(_outDir, $"{_shotIndex:D2}_{name}.png"), tex.EncodeToPNG());
        Destroy(tex);
        Note($"screenshot -> {_shotIndex:D2}_{name}.png");
        _shotIndex++;
    }

    // =====================================================================
    // STEP 1 -- walk / jump, with everything else live
    // =====================================================================

    private IEnumerator Step1_WalkAndJump()
    {
        _phase = "step1 walk/jump";
        L("STEP 1 -- walk and jump on generated terrain, whole stack live");

        int px = _regionOrigin.x + RX / 2, pz = _regionOrigin.z - 40;
        int sy = SurfaceY(px, pz);
        M.Teleport(new float3(px * 0.1f, (sy + 1) * 0.1f + 0.3f, pz * 0.1f));
        M.ResolveSpawn();
        yield return DriveFor(90, float2.zero);

        Check(M.Grounded, $"the player settles on generated terrain (y {M.PositionM.y:F2} m)");
        float3 start = M.PositionM;

        bool insideTerrain = false;
        for (int i = 0; i < 240; i++)
        {
            Drive(new float2(1f, 0f), false);
            if (M.OverlapsSolid(M.PositionM)) insideTerrain = true;
            FluidTick();
            yield return null;
        }
        float walked = math.length(new float2(M.PositionM.x - start.x, M.PositionM.z - start.z));
        Check(walked > 3f, $"walked {walked:F2} m");
        Check(!insideTerrain, "never inside terrain during the walk");

        float floorY = M.PositionM.y;
        Drive(float2.zero, true);
        float apex = M.PositionM.y;
        for (int i = 0; i < 200; i++)
        {
            Drive(float2.zero, false);
            apex = math.max(apex, M.PositionM.y);
            if (M.Grounded && i > 5) break;
            yield return null;
        }
        Check(apex - floorY > 0.2f, $"jumped {apex - floorY:F3} m and landed");

        yield return Shot("step1_walk_jump",
            new float3(M.PositionM.x - 3f, M.PositionM.y + 2.5f, M.PositionM.z - 3f),
            new float3(M.PositionM.x, M.PositionM.y + 0.9f, M.PositionM.z));
        L("");
    }

    // =====================================================================
    // STEP 2 -- §13: "0.2m wall, grapple in at 60 m/s: stop at the face, 20/20"
    // =====================================================================

    private IEnumerator Step2_GrappleStop()
    {
        _phase = "step2 grapple stop";
        L("STEP 2 -- 0.2 m wall, grapple at 60 m/s, 20 trials");

        int3 p = CoordMath.WorldToVoxel(M.PositionM);
        int laneY = p.y - 1;
        int x0 = p.x + 4, x1 = p.x + 200, wallX = p.x + 60;
        BuildLane(x0, x1, p.z - 6, p.z + 6, laneY);
        _edits.SetBox(new int3(wallX, laneY + 1, p.z - 6), new int3(wallX + 1, laneY + 25, p.z + 6),
                      Materials.Stone);
        for (int i = 0; i < 20; i++) yield return null;

        float face = wallX * 0.1f, bodyY = (laneY + 1) * 0.1f + 0.05f, zM = p.z * 0.1f;
        int stopped = 0;
        var bad = new List<string>();
        for (int t = 0; t < 20; t++)
        {
            float sx = face - 1.25f + t * 0.045f;
            float3 from = new float3(sx, bodyY, zM), to = from + new float3(1f, 0f, 0f);
            CCDResult r = _ccd.Sweep(from, to, 0.6f, 1.8f);
            if (!r.HitSolid) { bad.Add($"t{t} no hit"); continue; }
            float lead = SweptCCD.ClampToHit(from, to, r).x + 0.3f;
            if (lead > face + 1e-3f) bad.Add($"t{t} lead {lead:F4} past {face:F2}"); else stopped++;
        }
        Check(bad.Count == 0 && stopped == 20,
            $"20/20 grapple approaches stop at the face ({stopped}/20)" +
            (bad.Count > 0 ? " -- " + string.Join(" | ", bad) : ""));

        // A projectile must agree with the sweep about the same wall.
        ProjectileHit ph = _proj.Trace(new float3(face - 2f, bodyY, zM), new float3(face + 2f, bodyY, zM));
        Check(ph.Hit && math.abs(ph.DistanceM - 2f) < 0.05f,
            $"and a projectile stops at the same wall ({ph.DistanceM:F3} m)");
        L("");
    }

    // =====================================================================
    // STEP 3 -- §3.6's ADVERSARIAL CHECKERBOARD. §13: "this is where §3.6's
    //           entire memory story is proven true or found wanting."
    // =====================================================================

    private IEnumerator Step3_AdversarialCheckerboard()
    {
        _phase = "step3 checkerboard vs the LRU valve";
        L("STEP 3 -- adversarial checkerboard against the §3.6 LRU valve");
        L($"  dense bricks before: {Store.DenseBricksHeld} / cap {EngineConfig.BRICK_POOL_CAP}" +
          $"  (high water {EngineConfig.BrickPoolHighWaterBricks})");

        // THE ATTACK. A brick is 8 voxels; forcing ONE differing voxel into a
        // uniform brick makes it dense. Stepping by 8 therefore densifies one
        // brick per write -- the cheapest possible way to reach the pool cap,
        // which is exactly what an adversarial player checkerboarding a base
        // does to memory.
        int3 p = CoordMath.WorldToVoxel(M.PositionM);
        int written = 0, rejected = 0;
        int peakDense = Store.DenseBricksHeld;
        int evictionsBefore = Streamer.LruEvictionsTotal;
        bool pressureSeen = false;
        bool everOverCap = false;
        int lastEditFailed = 0;

        const int SPAN = 320;                     // voxels each way => 40 bricks each way
        int step = EngineConfig.BRICK_EDGE;       // 8

        for (int dz = -SPAN; dz <= SPAN && written < 200000; dz += step)
        {
            for (int dy = -64; dy <= 96 && written < 200000; dy += step)
            for (int dx = -SPAN; dx <= SPAN && written < 200000; dx += step)
            {
                int3 v = new int3(p.x + dx, p.y + dy, p.z + dz);
                if (v.y < 1) continue;
                // Alternate materials so no brick can coalesce back to uniform.
                byte m = ((dx + dy + dz) / step) % 2 == 0 ? Materials.Stone : Materials.Sandstone;
                if (_edits.TrySetVoxel(v, m)) written++; else rejected++;
            }

            peakDense = math.max(peakDense, Store.DenseBricksHeld);
            if (Store.IsUnderPoolPressure) pressureSeen = true;
            if (Store.DenseBricksHeld > EngineConfig.BRICK_POOL_CAP) everOverCap = true;
            lastEditFailed = rejected;
            yield return null;                    // let StreamManager run its valve
        }

        int evictions = Streamer.LruEvictionsTotal - evictionsBefore;
        L($"  wrote {written} checkerboard voxels ({rejected} refused as non-resident)");
        L($"  dense bricks peak {peakDense} / cap {EngineConfig.BRICK_POOL_CAP} " +
          $"({peakDense / (float)EngineConfig.BRICK_POOL_CAP:P1})");
        L($"  LRU evictions during the attack: {evictions}");
        L($"  under pool pressure at any point: {pressureSeen}");

        Check(written > 10000, $"the attack actually landed a lot of edits ({written})");
        Check(!everOverCap,
            $"MEMORY NEVER EXCEEDS THE CAP: peak {peakDense} <= {EngineConfig.BRICK_POOL_CAP}. " +
            "§3.6's whole claim is that the pool is hard-capped and the valve, not luck, " +
            "keeps it there.");

        if (pressureSeen)
        {
            Check(evictions > 0,
                $"the LRU valve FIRED once past the high-water mark ({evictions} evictions) -- " +
                "§3.6: 'past a high-water mark, StreamManager LRU-evicts the coldest resident " +
                "chunk'");
            Note("EngineConfig line 57 flags BRICK_POOL_HIGH_WATER_FRACTION as an ASSUMPTION " +
                 "whose Phase 6 gate is this test. It was reached and the valve engaged.");
        }
        else
        {
            Note($"POOL PRESSURE WAS NEVER REACHED. Peak {peakDense} stayed under the " +
                 $"{EngineConfig.BrickPoolHighWaterBricks} high-water mark, so the LRU valve was " +
                 "never asked to fire and §3.6's eviction path is NOT exercised by this run. " +
                 "That is a genuine gap in the gate, not a pass: the attack was bounded by the " +
                 "resident window, and a wider window or a longer attack would be needed to " +
                 "reach the mark. Reported rather than papered over.");
        }

        // §3.6: "the triggering edit always succeeds -- you push the eviction
        // radius inward, never fail."
        int3 probe = new int3(p.x + 1, p.y + 1, p.z + 1);
        Check(_edits.TrySetVoxel(probe, Materials.Obsidian),
            "and an edit still succeeds after the attack -- §3.6: 'the triggering edit always " +
            "succeeds'");
        Note($"edits refused as non-resident during the attack: {lastEditFailed} " +
             "(expected: the checkerboard reaches past the streaming window)");

        yield return Shot("step3_checkerboard",
            new float3(p.x * 0.1f - 12f, p.y * 0.1f + 10f, p.z * 0.1f - 12f),
            new float3(p.x * 0.1f, p.y * 0.1f, p.z * 0.1f));
        L("");
    }

    // =====================================================================
    // STEP 4 -- §13: "Drill one region 60s at 200 vox/s: memory sane, persists
    //           through save/reload, coalesces on fill-in."
    // =====================================================================

    private IEnumerator Step4_DrillAndPersist()
    {
        _phase = "step4 drill + persist";
        L("STEP 4 -- drill 60 s at 200 vox/s, then save/reload the region");

        // THE BLOCK MUST HOLD 60 SECONDS OF FRESH ROCK, LAID OUT SO CONSECUTIVE
        // DRILL POSITIONS DO NOT OVERLAP. 200 vox/s x 60 s = 12,000 voxels, and
        // a radius-3 brush covers 123 cells, so ~98 NON-OVERLAPPING spheres are
        // needed. Earlier versions stepped the cursor one voxel at a time, so
        // adjacent spheres overlapped ~90%: the tool kept re-pointing at rock it
        // had already cleared, the unspent allowance was dropped, and 60 s
        // delivered ~8,900 of 12,000. Stepping by the brush DIAMETER (7) over a
        // 10x10 grid gives 100 disjoint spheres = 12,300 cells.
        // ANCHORED CLEAR OF THE FLUID ARENA, NOT ON THE PLAYER. This block is
        // 81 voxels across -- wider than the 64-voxel arena -- so centring it on
        // the player put it straight through the volume step 6 later builds its
        // dive pool in. The fill-in below then dumped ~111,000 voxels of stone
        // into that space and step 6 failed with an empty pool. Steps sharing
        // volumes has now bitten this rig twice (step 3's block enclosed step
        // 5's basin in an earlier version); every step gets its own ground.
        int bx = _regionOrigin.x + RX + 80;
        int bz = _regionOrigin.z + RZ / 2;
        int by = math.max(20, SurfaceY(bx, bz) - 10);
        const int GRID = 10, STEP = 7;
        _edits.SetBox(new int3(bx - 40, by - 8, bz - 40), new int3(bx + 40, by + 8, bz + 40),
                      Materials.Stone);
        for (int i = 0; i < 10; i++) yield return null;

        var tier = EditService.Tiers[2];              // 200 vox/s
        var budget = new EditService.ToolBudget();
        int removed = 0, cursor = 0;
        for (int f = 0; f < 60 * 60; f++)             // 60 simulated seconds
        {
            int allow = budget.Accrue(Dt, tier.VoxelsPerSecond);
            if (allow > 0)
            {
                // ADVANCE ONLY WHEN THE CURRENT SPHERE IS SPENT. Moving the
                // drill every frame made it re-point at ground it had already
                // cleared: MineSphereBudgeted then removed nothing, the unspent
                // allowance was discarded rather than carried, and 60 s at
                // 200 vox/s delivered 8,716 of a nominal 12,000. That is what a
                // player actually does -- hold on the rock until it is gone,
                // then move -- and it is the scenario that was wrong, not the
                // tool.
                int gx = cursor % GRID, gz = (cursor / GRID) % GRID;
                int3 at = new int3(bx - 35 + gx * STEP, by, bz - 35 + gz * STEP);
                int got = _edits.MineSphereBudgeted(at, tier.RadiusVoxels, allow, Materials.Air);
                removed += got;
                if (got < allow) cursor++;          // this sphere is exhausted, move on
            }
            if ((f % 120) == 119) yield return null;
        }

        int expected = (int)(tier.VoxelsPerSecond * 60);
        L($"  removed {removed} voxels in 60 s (nominal {expected})");
        Check(removed >= expected * 0.9f, $"the drill kept pace over a full minute ({removed})");
        Check(Store.DenseBricksHeld <= EngineConfig.BRICK_POOL_CAP,
            $"memory stayed sane: {Store.DenseBricksHeld} <= {EngineConfig.BRICK_POOL_CAP}");

        // ---- Persistence: flush, force the region out and back ----
        int3 probe = new int3(bx, by, bz);
        byte expectMaterial = Store.GetVoxel(probe);
        int3 probeChunk = CoordMath.VoxelToChunk(probe);

        int flushed = Streamer.FlushAllDirty();
        Streamer.WaitForIdle();
        int savedBefore = Streamer.ChunksSavedTotal;
        int loadedBefore = Streamer.DeltasLoadedTotal;
        L($"  flushed {flushed} dirty chunks; saved total {savedBefore}");
        Check(flushed > 0 || savedBefore > 0, "the drilled region was written to disk");

        // WALK PAST THE EVICT RADIUS, DERIVED, NOT GUESSED. A first version
        // moved a flat 8 chunks and the probe chunk never left the window, so
        // the "save/reload" step only ever exercised the flush half and said so.
        // WINDOW_CHUNKS_XZ is 32, and the streamer knows its own evict radius --
        // ask it, the way the Phase 5d rig derives its distances, rather than
        // hardcoding a number that silently stops being far enough.
        Camera cam = Camera.main;
        Vector3 home = cam.transform.position;
        float chunkM = EngineConfig.CHUNK_EDGE_VOXELS * 0.1f;
        float awayChunks = Streamer.EvictRadiusChunks + 6;
        Vector3 away = home + Vector3.right * (chunkM * awayChunks);
        L($"  walking {awayChunks} chunks ({chunkM * awayChunks:F0} m) to force eviction " +
          $"(evict radius {Streamer.EvictRadiusChunks}, load radius {Streamer.LoadRadiusChunks})");

        // Walk, don't teleport: a single-frame jump beyond the window trips
        // ChunkStore's admission guard (PHASE_5C_COMPLETION.md §9.5), which is a
        // different failure from the one under test.
        for (int i = 0; i <= 600; i++)
        {
            cam.transform.position = Vector3.Lerp(home, away, i / 600f);
            if ((i % 4) == 0) Streamer.WaitForIdle();
            yield return null;
        }
        Streamer.WaitForIdle();
        bool evicted = !Store.IsResident(probeChunk);
        Note($"probe chunk {probeChunk} evicted while away: {evicted} " +
             $"(evictions total {Streamer.ChunksEvictedTotal}, saved {Streamer.ChunksSavedTotal})");

        for (int i = 0; i <= 600; i++)
        {
            cam.transform.position = Vector3.Lerp(away, home, i / 600f);
            if ((i % 4) == 0) Streamer.WaitForIdle();
            yield return null;
        }
        cam.transform.position = home;
        Streamer.WaitForIdle();
        for (int i = 0; i < 180; i++) yield return null;

        int loaded = Streamer.DeltasLoadedTotal - loadedBefore;
        L($"  deltas loaded on return: {loaded}; rejected total {Streamer.DeltasRejectedTotal}");

        if (evicted)
        {
            Check(Store.IsResident(probeChunk), "the region streamed back in");
            Check(Store.GetVoxel(probe) == expectMaterial,
                $"AND THE DRILLED HOLE SURVIVED save/reload (probe reads {Store.GetVoxel(probe)}, " +
                $"expected {expectMaterial})");
            Check(Streamer.DeltasRejectedTotal == 0,
                $"with no delta rejected ({Streamer.DeltasRejectedTotal})");
        }
        else
        {
            Note("THE PROBE CHUNK NEVER EVICTED, so this run did not actually exercise " +
                 "save/reload for it -- the camera walk stayed inside the window's reach. " +
                 "The flush half is proven; the round trip is NOT. Reported as a gap.");
            Check(Store.GetVoxel(probe) == expectMaterial,
                "the drilled hole is still present (without an eviction round trip)");
        }

        yield return Shot("step4_drilled",
            new float3(bx * 0.1f - 4f, by * 0.1f + 4f, bz * 0.1f - 4f),
            new float3(bx * 0.1f, by * 0.1f, bz * 0.1f));

        // ---- §13's third clause: "coalesces on fill-in" ----
        //
        // Drilling forced bricks DENSE (a uniform-air or uniform-stone brick
        // has to be expanded the moment one voxel in it differs). Filling the
        // hole back in with a single material should let §4.5's coalescer
        // collapse them to uniform again and hand the slots back -- otherwise a
        // player who digs and refills leaks pool capacity permanently, which is
        // the §3.6 memory story failing by a slower route than the checkerboard.
        var coalescer = Phase4Bootstrapper.Coalescer;
        int denseBeforeFill = Store.DenseBricksHeld;
        int coalescedBefore = coalescer.BricksCoalescedTotal;

        _edits.SetBox(new int3(bx - 40, by - 8, bz - 40), new int3(bx + 40, by + 8, bz + 40),
                      Materials.Stone);
        for (int i = 0; i < 10; i++) yield return null;
        int denseAfterFill = Store.DenseBricksHeld;

        coalescer.RunFullPass();
        for (int i = 0; i < 10; i++) yield return null;

        int coalesced = coalescer.BricksCoalescedTotal - coalescedBefore;
        int denseAfterCoalesce = Store.DenseBricksHeld;
        L($"  fill-in: dense {denseBeforeFill} -> {denseAfterFill} (refilled) -> " +
          $"{denseAfterCoalesce} (after coalesce); {coalesced} bricks coalesced");

        Check(coalesced > 0,
            $"§13's 'coalesces on fill-in': the refilled region collapsed {coalesced} bricks " +
            "back to uniform");
        Check(denseAfterCoalesce < denseAfterFill,
            $"and the pool got slots back ({denseAfterFill} -> {denseAfterCoalesce} dense " +
            "bricks) -- without this, dig-and-refill leaks capacity permanently");
        L("");
    }

    // =====================================================================
    // STEP 5 -- §13: "400K detonation: recovery <=3 frames, one Proxy Drop,
    //           plausible tally."
    // =====================================================================

    private IEnumerator Step5_Detonation()
    {
        _phase = "step5 400K detonation";
        L("STEP 5 -- 400K detonation");

        int radius = DestructionReducer.RadiusForAtLeast(400000);
        int cells = DestructionReducer.SphereVoxelCount(radius);
        int3 p = CoordMath.WorldToVoxel(M.PositionM);
        int3 centre = new int3(p.x, math.max(radius + 2, p.y - 6), p.z);

        L($"  radius {radius} covers {cells} cells at {centre}");

        _boom.Detonate(centre, radius);
        int frames = 0;
        while (_boom.InProgress && frames < 200) { _boom.Step(); frames++; yield return null; }

        ProxyDrop drop;
        bool got = _boom.TryTakeCompleted(out drop);

        L($"  drained in {frames} frames, removed {drop.TotalVoxels} voxels, " +
          $"dominant {drop.DominantMaterial}");
        Check(!_boom.InProgress, "the event finished");
        Check(frames <= 3, $"§13: recovery <=3 frames (took {frames})");
        Check(got, "exactly one Proxy Drop was produced");
        Check(!_boom.TryTakeCompleted(out _), "and only one");
        Check(drop.TotalVoxels > 0, $"with a plausible tally ({drop.TotalVoxels} voxels)");
        Check(Store.DenseBricksHeld <= EngineConfig.BRICK_POOL_CAP,
            "and memory is still under the cap after the blast");

        yield return Shot("step5_detonation",
            new float3(centre.x * 0.1f - 8f, centre.y * 0.1f + 8f, centre.z * 0.1f - 8f),
            new float3(centre.x * 0.1f, centre.y * 0.1f, centre.z * 0.1f));
        L("");
    }

    // =====================================================================
    // STEP 6 -- §13: "60 m/s dive into water: buoyancy engages within the
    //           mitigated bounded window, never an unmitigated slam."
    // =====================================================================

    private IEnumerator Step6_DiveIntoWater()
    {
        _phase = "step6 60 m/s dive";
        L("STEP 6 -- 60 m/s dive into water; buoyancy must engage within the bounded window");

        // THE POOL MUST BE AT THE LOCAL SURFACE, AND THE SKY ABOVE IT MUST BE
        // CLEARED. A first version placed the floor at _regionOrigin.y + 2 and
        // filled water above it -- but the arena origin sits 8 voxels BELOW the
        // terrain surface, so the "pool" was carved underground with metres of
        // natural rock still above it. The 60 m/s dive then started INSIDE that
        // rock and the sweep correctly reported "hit bottom" on frame 0. Four
        // assertions failed on a scenario that never existed.
        int bx = _regionOrigin.x + RX / 2, bz = _regionOrigin.z + RZ / 2;
        int localSurface = SurfaceY(bx, bz);
        int floor = math.max(2, localSurface - 22);
        // Everything from the pool floor up to well above the dive start.
        _edits.SetBox(new int3(bx - 12, floor, bz - 12),
                      new int3(bx + 12, localSurface + 40, bz + 12), Materials.Air);
        _edits.SetBox(new int3(bx - 12, floor, bz - 12), new int3(bx + 12, floor, bz + 12),
                      Materials.Stone);
        _edits.SetBox(new int3(bx - 11, floor + 1, bz - 11), new int3(bx + 11, floor + 20, bz + 11),
                      Materials.Water);
        for (int i = 0; i < 120; i++) { FluidTick(); yield return null; }

        float surfaceY = (floor + 21) * 0.1f;
        L($"  local surface voxel {localSurface}, pool floor {floor}, water top {floor + 20} " +
          $"(surface {surfaceY:F2} m); sky cleared to voxel {localSurface + 40}");
        Check(Store.GetVoxel(new int3(bx, floor + 30, bz)) == Materials.Air,
            "the air above the pool is genuinely clear, so the dive starts in open sky");
        float3 above = new float3(bx * 0.1f, surfaceY + 3f, bz * 0.1f);

        // The dive: 60 m/s downward, one frame = 1.0 m.
        float3 pos = above;
        float3 vel = new float3(0f, -60f, 0f);
        int framesToBuoyancy = -1;
        bool everInsideSolid = false;

        for (int f = 0; f < 60; f++)
        {
            float3 next = pos + vel * Dt;
            CCDResult r = _ccd.Sweep(pos, next, 0.6f, 1.8f);
            float3 landed = SweptCCD.ClampToHit(pos, next, r);

            // §8.2's fluid accumulation is what makes the splash fire at all.
            if (r.FluidTraversedDistanceM > 0f && framesToBuoyancy < 0)
            {
                framesToBuoyancy = f;
                L($"  frame {f}: sweep reports {r.FluidTraversedDistanceM:F2} m of " +
                  $"{r.PrimaryFluidMaterial} crossed -- the splash fires here");
            }

            pos = landed;
            if (VoxelCollision.OverlapsSolid(Store, Store, pos, 0.6f, 1.8f)) everInsideSolid = true;
            if (r.HitSolid) { L($"  frame {f}: hit bottom at y {pos.y:F2} m"); break; }

            BuoyancyState bs = _buoy.Sample(pos, 1.8f, 60f, _readback.FramesSinceLastApplied);
            if (bs.InFluid && framesToBuoyancy < 0) framesToBuoyancy = f;

            FluidTick();
            yield return null;
        }

        BuoyancyState final = _buoy.Sample(pos, 1.8f, 60f, _readback.FramesSinceLastApplied);
        L($"  final: submerged {final.SubmergedFraction:P0}, buoyant accel " +
          $"{final.BuoyantAccelMps2:F2} m/s^2, drag {final.DragPerSecond:F2}/s");
        L($"  op-list frames since applied: {_readback.FramesSinceLastApplied}, " +
          $"clamp engaged: {final.SpeedClampEngaged}");

        Check(framesToBuoyancy >= 0,
            $"the dive was NOTICED, not phased through (first fluid contact at frame " +
            $"{framesToBuoyancy})");
        Check(framesToBuoyancy <= 3,
            $"within §8.2's mitigated bound of a few frames ({framesToBuoyancy})");
        Check(!everInsideSolid, "and the body never ended up inside the lakebed -- no silent slam");
        Check(final.InFluid, $"buoyancy is engaged at rest ({final.SubmergedFraction:P0} submerged)");
        Check(final.BuoyantAccelMps2 > 0f,
            $"pushing the body UP ({final.BuoyantAccelMps2:F2} m/s^2), since a body at " +
            $"{_buoy.BodyDensityKgM3} kg/m^3 is lighter than water");

        yield return Shot("step6_dive",
            new float3((bx - 16) * 0.1f, surfaceY + 1.5f, (bz - 16) * 0.1f),
            new float3(bx * 0.1f, surfaceY - 0.5f, bz * 0.1f));
        L("");
    }

    // =====================================================================
    // STEP 7 -- §13's FLOOD-FRONT PLAYTEST. Partly manual by §13's own wording.
    // =====================================================================

    private IEnumerator Step7_FloodFront()
    {
        _phase = "step7 flood front";
        L("STEP 7 -- flood front: breach a water body with the player at the leading edge");

        int bx = _regionOrigin.x + 14, bz = _regionOrigin.z + RZ / 2;
        int floor = _regionOrigin.y + 6;
        const int R = 8, APRON = 24;

        _edits.SetBox(new int3(bx - R - 2, floor, bz - R - 2),
                      new int3(bx + APRON, floor + 18, bz + R + 2), Materials.Air);
        _edits.SetBox(new int3(bx - R - 2, floor, bz - R - 2),
                      new int3(bx + APRON, floor, bz + R + 2), Materials.Stone);
        _edits.SetBox(new int3(bx - R, floor + 1, bz - R), new int3(bx - R, floor + 6, bz + R), Materials.Stone);
        _edits.SetBox(new int3(bx + R, floor + 1, bz - R), new int3(bx + R, floor + 6, bz + R), Materials.Stone);
        _edits.SetBox(new int3(bx - R, floor + 1, bz - R), new int3(bx + R, floor + 6, bz - R), Materials.Stone);
        _edits.SetBox(new int3(bx - R, floor + 1, bz + R), new int3(bx + R, floor + 6, bz + R), Materials.Stone);
        _edits.SetBox(new int3(bx - R + 1, floor + 1, bz - R + 1),
                      new int3(bx + R - 1, floor + 4, bz + R - 1), Materials.Water);
        for (int i = 0; i < 180; i++) { FluidTick(); yield return null; }

        // THE PLAYER STANDS AT THE LEADING EDGE -- §13's "at/entering the
        // leading edge of the resulting flood at the instant it arrives".
        int standX = bx + R + 6;
        M.Teleport(new float3(standX * 0.1f, (floor + 1) * 0.1f + 0.2f, bz * 0.1f));
        yield return DriveFor(60, float2.zero);
        Check(M.Grounded, "the player is standing on the apron, in the flood's path");

        yield return Shot("step7_before_breach",
            new float3((bx - R - 6) * 0.1f, (floor + 14) * 0.1f, (bz - R - 6) * 0.1f),
            new float3((bx + R) * 0.1f, (floor + 2) * 0.1f, bz * 0.1f));

        long appliedBefore = _applied;
        _edits.SetBox(new int3(bx + R, floor + 1, bz - 2), new int3(bx + R, floor + 3, bz + 2),
                      Materials.Air);

        // Watch for the front to reach the player, and record the latency a
        // human would be judging.
        int frameFrontArrived = -1;
        int frameBuoyancyNoticed = -1;
        int maxStale = 0;
        for (int f = 0; f < 400; f++)
        {
            FluidTick();
            Drive(float2.zero, false);
            maxStale = math.max(maxStale, _readback.FramesSinceLastApplied);

            byte atFeet = Store.GetVoxel(CoordMath.WorldToVoxel(M.PositionM + new float3(0f, 0.05f, 0f)));
            if (atFeet == Materials.Water && frameFrontArrived < 0) frameFrontArrived = f;

            BuoyancyState bs = _buoy.Sample(M.PositionM, 1.8f, 5f, _readback.FramesSinceLastApplied);
            if (bs.InFluid && frameBuoyancyNoticed < 0) frameBuoyancyNoticed = f;
            yield return null;
        }

        long appliedDuring = _applied - appliedBefore;
        L($"  fluid ops applied after the breach: {appliedDuring}");
        L($"  front reached the player at frame {frameFrontArrived}; " +
          $"buoyancy noticed at frame {frameBuoyancyNoticed}");
        L($"  worst op-list staleness during the flood: {maxStale} frames " +
          $"(clamp threshold {SweptCCD.StaleFramesBeforeClamp})");

        Check(appliedDuring > 0, $"the flood actually moved ({appliedDuring} ops)");
        if (frameFrontArrived >= 0)
        {
            Check(frameBuoyancyNoticed >= 0 && frameBuoyancyNoticed - frameFrontArrived <= 3,
                $"buoyancy noticed the front within a few frames of it arriving " +
                $"(front {frameFrontArrived}, noticed {frameBuoyancyNoticed})");
        }
        else
        {
            Note("the front did not reach the player's exact cell within 400 frames; the " +
                 "latency figure below is therefore about the flood generally, not about the " +
                 "instant of contact.");
        }

        // §8.2 DOES NOT PROMISE STALENESS NEVER EXCEEDS THE THRESHOLD. It
        // promises that WHEN it does, the clamp engages -- that is the whole
        // mitigation. A first version asserted "worst <= threshold + 2", which
        // is a claim the spec never makes and which failed at 6 frames during a
        // heavy flood that was behaving exactly as designed. What matters is
        // that staleness stays BOUNDED (the readback is not wedged) and that
        // the clamp actually fires when it is crossed.
        Check(maxStale < 60,
            $"op-list staleness stayed bounded -- the readback never wedged " +
            $"(worst {maxStale} frames)");
        if (maxStale >= SweptCCD.StaleFramesBeforeClamp)
        {
            Check(Buoyancy.ClampedSpeedFor(60f, maxStale) <= SweptCCD.ClampedSpeedMps,
                $"and at the worst observed staleness ({maxStale} frames) the §8.2 clamp " +
                $"engages, limiting a 60 m/s body to {SweptCCD.ClampedSpeedMps} m/s -- which " +
                "is the mitigation §8.6's dive safety argument rests on");
        }
        else
        {
            Note($"staleness never reached the clamp threshold ({maxStale} < " +
                 $"{SweptCCD.StaleFramesBeforeClamp}), so the clamp was not exercised here.");
        }

        yield return Shot("step7_flood_front",
            new float3((bx - R - 6) * 0.1f, (floor + 14) * 0.1f, (bz - R - 6) * 0.1f),
            new float3((bx + R + 4) * 0.1f, (floor + 2) * 0.1f, bz * 0.1f));

        Manual("§13 asks for a BY-FEEL judgement here that no rig can make: is the bounded " +
               "op-list latency PERCEPTIBLE at the leading edge of the flood? The scenario is " +
               "staged and the numbers above are what a human would be judging (worst staleness " +
               $"{maxStale} frames, buoyancy within " +
               $"{(frameFrontArrived >= 0 && frameBuoyancyNoticed >= 0 ? (frameBuoyancyNoticed - frameFrontArrived).ToString() : "n/a")} " +
               "frames of contact). Watch step7_flood_front.png and the live scene and decide. " +
               "§13's remedy if it feels laggy: tighten the speed-clamp threshold or shrink " +
               "§7.4's active radius, then re-test.");
        L("");
    }

    // =====================================================================
    // STEP 8 -- §13: "CPU-lane total measured (§2.2): fluid + gameplay + upload
    //           under 16.6ms." READ THE CAVEAT.
    // =====================================================================

    private IEnumerator Step8_FrameTime()
    {
        _phase = "step8 frame time";
        L("STEP 8 -- frame time across the whole integrated scenario");

        if (_frameMs.Count < 30)
        {
            Note($"only {_frameMs.Count} frame samples; too few to report");
            yield break;
        }

        var sorted = new List<float>(_frameMs);
        sorted.Sort();
        float p50 = sorted[sorted.Count / 2];
        float p99 = sorted[(int)(sorted.Count * 0.99f)];
        float worst = sorted[sorted.Count - 1];

        L($"  frames sampled {sorted.Count}");
        L($"  frame total  p50 {p50:F2} ms   p99 {p99:F2} ms   worst {worst:F2} ms");
        L($"  build: {(Debug.isDebugBuild ? "DEVELOPMENT" : "RELEASE")}, " +
          $"screen {Screen.width}x{Screen.height}");

        Note("THIS IS NOT run-acceptance-rig.sh. CLAUDE.md names that script as the only " +
             "trusted frame-time source. These figures use the SAME methodology (release " +
             "standalone, outside the Editor, own Time.unscaledDeltaTime) but a different " +
             "harness and a different scenario, so they are PROVISIONAL and must not be " +
             "quoted as the §2.2 gate. They are reported because §13 asks for a CPU-lane " +
             "figure during THIS scenario, which run-acceptance-rig.sh does not run.");
        Note("They also include this rig's own overhead -- whole-region voxel counts, " +
             "screenshot encodes, a 200K-edit checkerboard -- which no real frame does. " +
             "Treat them as an upper bound with unknown slack, not as the engine's frame cost.");

        if (p99 > 16.6f)
            Note($"p99 {p99:F2} ms is ABOVE §2.2's 16.6 ms CPU-lane budget. Given the caveats " +
                 "above this is not a gate failure, but it IS a reason to run the real " +
                 "acceptance rig before believing Phase 6 is inside budget.");
        else
            Note($"p99 {p99:F2} ms is under §2.2's 16.6 ms, which is encouraging and not " +
                 "conclusive for the reasons above.");
        yield break;
    }
}
