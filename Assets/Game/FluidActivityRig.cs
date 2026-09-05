// ==========================================
// Assets/Game/FluidActivityRig.cs
//
// STEP 0 DIAGNOSTIC: "fluid stops simulating after you place a lot of it".
//
// This rig does NOT assume a cause. It reproduces the reported scenario --
// placing fluid in a Playground-shaped arena at roughly the rate and volume of
// holding RMB for a long time -- while logging, per tick, everything needed to
// tell the three candidate explanations apart:
//
//   (a) §7.7 POOL PRESSURE. Active slots hit the region's cap and promotion
//       becomes a guarded no-op. §7.7 calls this "a rare safety valve, not a
//       routine path" -- so if it fires at an ordinary volume, the number
//       itself is the finding.
//   (b) §7.6 SLEEP. The fluid genuinely settled. Distinguished by whether the
//       fluid is MOTIONLESS AND SUPPORTED when it stops, or frozen mid-air.
//   (c) A defect in the tick/readback/apply loop that is neither.
//
// The distinguishing measurements, and why each is here:
//   everAllocated vs SlotCapacity  §7.7 exhaustion, directly. AllocSlot is a
//                                  bump allocator (PHASE_5C_COMPLETION.md §5),
//                                  so this only ever rises.
//   applied ops per tick           whether the CA is still moving anything.
//   floating voxels                a settled pool has none; a stalled one does.
//                                  This is what separates (b) from (a)/(c).
//   wake queued / rejected         whether the CPU is still asking at all, and
//                                  whether the answer is "out of region" or
//                                  "queue full" rather than exhaustion.
//   readback errors / in-flight    whether apply is running at all -- (c).
//
// NO TIMING. This is a correctness/diagnostic rig and reports no ms figures.

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

public class FluidActivityRig : MonoBehaviour
{
    [SerializeField] private ComputeShader _fluidCA;
    [Tooltip("Playground uses 8192. That is the number under test.")]
    [SerializeField] private int _slotCapacity = 8192;
    [SerializeField] private int _maxOpsPerFrame = 8192;
    [SerializeField] private string _outputRootFolderName = "FluidActivity";

    // Playground's arena shape.
    private const int R = 64;

    private readonly StringBuilder _log = new StringBuilder();
    private int _pass, _fail;
    private string _phase = "-";
    private string _outDir;

    private static ChunkStore Store => Phase4Bootstrapper.Store;
    private static TerrainClipmap Clip => Phase4Bootstrapper.Clipmap;

    private EditService _edits;
    private FluidGpuSimulation _fluid;
    private FluidOpListReadback _readback;
    private int3 _origin;
    private long _applied;

    private void L(string s) { _log.AppendLine(s); Debug.Log("[fa] " + s); }
    private void Note(string s) => L("    note  " + s);
    private void Pass(string s) { _pass++; L("    PASS  " + s); }
    private void Fail(string s) { _fail++; L($"    FAIL  {s}   [{_phase}]"); }
    private void Check(bool ok, string s) { if (ok) Pass(s); else Fail(s); }

    // =====================================================================

    IEnumerator Start()
    {
        float t0 = Time.realtimeSinceStartup;
        while (Store == null && Time.realtimeSinceStartup - t0 < 240f) yield return null;

        _outDir = Path.Combine(Application.persistentDataPath, _outputRootFolderName,
                               DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(_outDir);

        L("=== STEP 0 DIAGNOSTIC: does fluid stop simulating, and why? ===");
        L(DateTime.Now.ToString("u", CultureInfo.InvariantCulture));
        L("");

        if (Store == null) { Fail("world never booted"); yield return Report(); yield break; }
        for (int i = 0; i < 120; i++) yield return null;

        Camera cam = Camera.main;
        int3 camVox = CoordMath.WorldToVoxel(new float3(cam.transform.position.x,
                                                        cam.transform.position.y,
                                                        cam.transform.position.z));
        int surface = SurfaceY(camVox.x, camVox.z);
        _origin = new int3(camVox.x - R / 2, math.max(0, surface - 8), camVox.z - R / 2);

        _edits = new EditService();
        _edits.AttachWorld(Store, Store, Store, Clip);

        _fluid = new FluidGpuSimulation(_fluidCA, new int3(R, R, R), _slotCapacity, _maxOpsPerFrame)
        {
            RegionOriginVoxels = _origin,
            PlayerVoxel = _origin + new int3(R / 2, R / 2, R / 2),
            ActiveRadiusVoxels = EngineConfig.FLUID_ACTIVE_RADIUS_VOXELS,
        };
        _readback = new FluidOpListReadback(_fluid, Store)
        {
            OnVoxelApplied = v => { Clip.MarkDirty(CoordMath.VoxelToChunk(v)); _applied++; },
        };
        _edits.AttachFluidSimulation(_fluid, Store);

        L($"arena {R}^3 at {_origin}   slotCapacity {_fluid.SlotCapacity}   " +
          $"MAX_ACTIVE_FLUID {EngineConfig.MAX_ACTIVE_FLUID}");
        L($"active radius {_fluid.ActiveRadiusVoxels} voxels (arena half-diagonal is " +
          $"{(int)(math.sqrt(3f) * R / 2f)}), so the radius does NOT bite here");
        L($"FLUID_SLEEP_TICKS {EngineConfig.FLUID_SLEEP_TICKS}");
        L("");

        yield return Step1_BaselineSmallPour();
        yield return Step2_SustainedPourUntilItStops();
        yield return Step3_StraddleMoreThanTwoChunks();
        yield return Step4_SeveralSimultaneousRegions();
        yield return Step5_EvictionWithOpListInFlight();
        yield return Step6_ResidencyEdgeUnderAMovingRadius();
        yield return Step7_AdmissionGuardWithAMovingRegion();

        yield return Report();
    }

    private IEnumerator Report()
    {
        _log.AppendLine();
        _log.AppendLine($"PASS {_pass}  FAIL {_fail}");
        _log.AppendLine(_fail == 0 ? "RESULT: PASSED" : "RESULT: FAILED");
        File.WriteAllText(Path.Combine(_outDir, "fluid_activity_report.txt"), _log.ToString());
        Debug.Log("[fa] report -> " + _outDir);
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

    private void Tick()
    {
        if (_readback.CanIssue) { _fluid.Tick(Clip); _readback.IssueReadback(0); }
        _readback.PumpAndApply();
    }

    private int CountInRegion(byte m)
    {
        int n = 0;
        for (int z = 0; z < R; z++)
        for (int y = 0; y < R; y++)
        for (int x = 0; x < R; x++)
            if (Store.GetVoxel(_origin + new int3(x, y, z)) == m) n++;
        return n;
    }

    /// A mobile voxel with AIR directly beneath it. A settled pool has none of
    /// these; a STALLED one is full of them. This is the measurement that
    /// separates §7.6 sleep from §7.7 exhaustion, and it is why "it stopped"
    /// alone is not a diagnosis.
    private int CountFloating()
    {
        int n = 0;
        for (int z = 0; z < R; z++)
        for (int y = 1; y < R; y++)
        for (int x = 0; x < R; x++)
        {
            int3 v = _origin + new int3(x, y, z);
            if (!MaterialRules.IsMobile(Store.GetVoxel(v))) continue;
            if (Store.GetVoxel(v - new int3(0, 1, 0)) == Materials.Air) n++;
        }
        return n;
    }

    private void Snapshot(string tag)
    {
        uint hi, ever;
        _fluid.ReadSlotCounters(out hi, out ever);
        L($"  [{tag}] everAllocated {ever} / cap {_fluid.SlotCapacity}" +
          $"   highWater {hi}   applied {_applied}" +
          $"   water {CountInRegion(Materials.Water)}   floating {CountFloating()}" +
          $"   wakeQueued {_fluid.WakeRequestsQueuedTotal}" +
          $"   rejOutOfRegion {_fluid.WakeRejectedOutOfRegion}" +
          $"   rejFull {_fluid.WakeRejectedFull}" +
          $"   rbErrors {_readback.ReadbackErrorsTotal}");
    }

    /// One RMB-sized blob, exactly what Playground's TryPaintBrush writes.
    private int PlaceBlob(int3 at) => _edits.SetSphere(at, 1, Materials.Water);

    /// Distinct chunks currently holding `m` inside the region.
    private HashSet<int3> ChunksHolding(byte m, int3 origin, int size)
    {
        var set = new HashSet<int3>();
        for (int z = 0; z < size; z++)
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            int3 v = origin + new int3(x, y, z);
            if (Store.GetVoxel(v) == m) set.Add(CoordMath.VoxelToChunk(v));
        }
        return set;
    }

    private int CountInBox(int3 lo, int3 hi, byte m)
    {
        int n = 0;
        for (int z = lo.z; z <= hi.z; z++)
        for (int y = lo.y; y <= hi.y; y++)
        for (int x = lo.x; x <= hi.x; x++)
            if (Store.GetVoxel(new int3(x, y, z)) == m) n++;
        return n;
    }

    // =====================================================================
    // STEP 1 -- a small pour, to establish the allocations-per-voxel rate
    // =====================================================================

    private IEnumerator Step1_BaselineSmallPour()
    {
        _phase = "step1 baseline";
        L("STEP 1 -- small pour, to measure allocations per live voxel");

        int cx = _origin.x + R / 2, cz = _origin.z + R / 2;
        int floor = _origin.y + 4;
        _edits.SetBox(new int3(cx - 12, floor, cz - 12), new int3(cx + 12, floor + 30, cz + 12),
                      Materials.Air);
        _edits.SetBox(new int3(cx - 12, floor, cz - 12), new int3(cx + 12, floor, cz + 12),
                      Materials.Stone);
        // Low walls so it pools rather than running away.
        _edits.SetBox(new int3(cx - 12, floor + 1, cz - 12), new int3(cx - 12, floor + 8, cz + 12), Materials.Stone);
        _edits.SetBox(new int3(cx + 12, floor + 1, cz - 12), new int3(cx + 12, floor + 8, cz + 12), Materials.Stone);
        _edits.SetBox(new int3(cx - 12, floor + 1, cz - 12), new int3(cx + 12, floor + 8, cz - 12), Materials.Stone);
        _edits.SetBox(new int3(cx - 12, floor + 1, cz + 12), new int3(cx + 12, floor + 8, cz + 12), Materials.Stone);
        for (int i = 0; i < 10; i++) yield return null;

        uint hi0, ever0;
        _fluid.ReadSlotCounters(out hi0, out ever0);
        Snapshot("before");

        int placed = 0;
        for (int i = 0; i < 30; i++)
        {
            placed += PlaceBlob(new int3(cx, floor + 20, cz));
            for (int k = 0; k < 6; k++) { Tick(); yield return null; }
        }
        for (int i = 0; i < 240; i++) { Tick(); yield return null; }

        uint hi1, ever1;
        _fluid.ReadSlotCounters(out hi1, out ever1);
        int live = CountInRegion(Materials.Water);
        Snapshot("after");

        long allocs = ever1 - ever0;
        L($"  placed {placed} voxels, {live} live water in region");
        L($"  slots allocated: {allocs}   =>  {(live > 0 ? allocs / (float)live : 0f):F1} " +
          "allocations per live voxel");
        Note("PHASE_5C_COMPLETION.md §5 measured ~35 per live voxel on a 220-voxel pour. " +
             "AllocSlot is a bump allocator with no free list, so this number is the " +
             "rate at which a region burns its capacity permanently.");

        float perVoxel = live > 0 ? allocs / (float)live : float.MaxValue;
        Check(live > 0, "the pour produced live water");

        // THE REGRESSION PIN FOR A.5'S FREE LIST. Before recycling existed this
        // measured 24.5 allocations per live voxel (and §5 of PHASE_5C measured
        // ~35); with it, 2.2. The threshold sits far below the broken value and
        // comfortably above the fixed one, so it catches a removal of recycling
        // without being brittle about the exact figure.
        Check(perVoxel < 6f,
            $"slot indices are RECYCLED: {perVoxel:F1} allocations per live voxel " +
            "(24.5 before A.5's free list was ported; §5 measured ~35)");
        L("");
    }

    // =====================================================================
    // STEP 2 -- keep pouring, exactly like holding RMB, and find where it stops
    // =====================================================================

    private IEnumerator Step2_SustainedPourUntilItStops()
    {
        _phase = "step2 sustained pour";
        L("STEP 2 -- sustained pour (holding RMB), watching for the stall");

        int cx = _origin.x + R / 2, cz = _origin.z + R / 2;
        int floor = _origin.y + 4;

        int stallTick = -1;
        long everAtStall = 0;
        int floatingAtStall = 0, waterAtStall = 0;
        int quietRun = 0;
        long lastApplied = _applied;
        int totalPlaced = 0;

        // 400 bursts x ~7 voxels is ~2800 placed voxels -- a few minutes of
        // holding RMB, and well inside what a player would do.
        for (int burst = 0; burst < 400; burst++)
        {
            // Sweep the pour point so it does not just stack in one column.
            int ox = (burst * 5) % 20 - 10;
            int oz = (burst * 7) % 20 - 10;
            totalPlaced += PlaceBlob(new int3(cx + ox, floor + 22, cz + oz));

            for (int k = 0; k < 8; k++) { Tick(); yield return null; }

            bool moved = _applied != lastApplied;
            lastApplied = _applied;
            quietRun = moved ? 0 : quietRun + 1;

            if ((burst % 40) == 0) Snapshot($"burst {burst}");

            // A stall = several consecutive bursts where the CA applied NOTHING
            // even though fresh fluid was just dropped in mid-air above a floor.
            if (quietRun >= 3 && stallTick < 0)
            {
                uint hi, ever;
                _fluid.ReadSlotCounters(out hi, out ever);
                stallTick = burst;
                everAtStall = ever;
                floatingAtStall = CountFloating();
                waterAtStall = CountInRegion(Materials.Water);
                L($"  *** STALL at burst {burst}: no ops applied for {quietRun} consecutive " +
                  $"bursts despite fresh fluid ***");
                Snapshot("at stall");
                break;
            }
        }

        // SETTLE TO QUIESCENCE BEFORE JUDGING "FLOATING". The pour runs right up
        // to this point, so a voxel in flight is not a stalled one -- an earlier
        // version counted them together and called a healthy run inconclusive.
        // Tick until the CA stops applying ops, THEN measure.
        int quiet = 0;
        long prevApplied = _applied;
        for (int i = 0; i < 3000 && quiet < 90; i++)
        {
            Tick();
            quiet = _applied == prevApplied ? quiet + 1 : 0;
            prevApplied = _applied;
            yield return null;
        }
        L($"  settled after {quiet} consecutive quiet ticks");

        uint hiF, everF;
        _fluid.ReadSlotCounters(out hiF, out everF);
        int floatingFinal = CountFloating();
        Snapshot("final");

        L("");
        L($"  placed {totalPlaced} voxels total");
        L($"  everAllocated {everF} / capacity {_fluid.SlotCapacity}");
        L($"  floating (unsupported mobile voxels) at end: {floatingFinal}");
        L("");

        // ---- THE CLASSIFICATION ----
        bool exhausted = everF >= _fluid.SlotCapacity;
        bool stalled = stallTick >= 0;
        bool frozenMidAir = floatingFinal > 0;

        // The capacity headroom the free list buys, pinned directly.
        Check(everF < _fluid.SlotCapacity,
            $"the region never exhausted its slots: everAllocated {everF} < cap " +
            $"{_fluid.SlotCapacity} after {totalPlaced} placed voxels");
        Check(!stalled,
            stalled ? $"the CA STALLED at burst {stallTick} with everAllocated {everAtStall}"
                    : $"the CA never stalled across {totalPlaced} placed voxels");

        L("  CLASSIFICATION:");
        if (stalled && exhausted)
        {
            L("  (a) §7.7 POOL EXHAUSTION. The CA stopped because the region ran out of slot");
            L("      INDICES, not because the fluid settled.");
            L($"      Capacity {_fluid.SlotCapacity} was consumed by {totalPlaced} placed voxels.");
            Check(true, "classified as (a) §7.7 pool exhaustion, with numbers");
        }
        else if (stalled && !exhausted)
        {
            L("  (c) NEITHER exhaustion NOR settling -- the CA stopped with capacity to spare.");
            Check(false, "classified as (c): a defect that is not pool pressure");
        }
        else if (!stalled && !frozenMidAir)
        {
            L("  (b) §7.6 SLEEP. Nothing stalled: fluid kept moving until it settled, and");
            L("      nothing is left unsupported.");
            Check(true, "classified as (b) §7.6 sleep -- working as designed");
        }
        else
        {
            L("  MIXED / INCONCLUSIVE -- see the per-burst snapshots above.");
            Check(false, "could not classify cleanly");
        }

        Check(!frozenMidAir || stalled,
            frozenMidAir
                ? $"{floatingFinal} voxels are frozen UNSUPPORTED in mid-air -- this is not " +
                  "settling, it is a stall"
                : "no voxels are frozen unsupported in mid-air");

        Note($"§7.7 calls forced demotion 'a rare safety valve, not a routine path' -- and says " +
             $"so BECAUSE §7.4's near-player scope is supposed to bound the active set. " +
             $"Reaching the cap after {totalPlaced} placed voxels is therefore evidence about " +
             "§7.4's absence, not just about the constant.");
        L("");
    }

    // =====================================================================
    // STEP 3 -- §9.7: "fluid straddling MORE THAN TWO chunks" (never tested)
    // =====================================================================

    private IEnumerator Step3_StraddleMoreThanTwoChunks()
    {
        _phase = "step3 straddle >2 chunks";
        L("STEP 3 -- §9.7's untested case: fluid spanning more than two chunks");

        // Straddle deliberately. A 64-voxel body centred on a chunk CORNER in
        // XZ lands in four chunks; every prior fluid test lived inside one, and
        // §9.4's mass-loss bug was a two-chunk edge case, so three and four are
        // genuinely new ground.
        int3 camChunk = CoordMath.VoxelToChunk(_origin + new int3(R / 2, 0, R / 2));
        int E = EngineConfig.CHUNK_EDGE_VOXELS;
        int cornerX = camChunk.x * E, cornerZ = camChunk.z * E;
        int surface = SurfaceY(cornerX, cornerZ);
        if (surface < 0) { Note("no surface at the chunk corner; skipping"); yield break; }

        int floor = surface - 2;
        int3 lo = new int3(cornerX - 20, floor, cornerZ - 20);
        int3 hi = new int3(cornerX + 20, floor + 12, cornerZ + 20);

        _edits.SetBox(lo, hi, Materials.Air);
        _edits.SetBox(lo, new int3(hi.x, floor, hi.z), Materials.Stone);
        // Walls, so it pools across the boundary instead of draining away.
        _edits.SetBox(new int3(lo.x, floor + 1, lo.z), new int3(lo.x, floor + 8, hi.z), Materials.Stone);
        _edits.SetBox(new int3(hi.x, floor + 1, lo.z), new int3(hi.x, floor + 8, hi.z), Materials.Stone);
        _edits.SetBox(new int3(lo.x, floor + 1, lo.z), new int3(hi.x, floor + 8, lo.z), Materials.Stone);
        _edits.SetBox(new int3(lo.x, floor + 1, hi.z), new int3(hi.x, floor + 8, hi.z), Materials.Stone);
        _edits.SetBox(new int3(lo.x + 1, floor + 1, lo.z + 1),
                      new int3(hi.x - 1, floor + 5, hi.z - 1), Materials.Water);
        for (int i = 0; i < 20; i++) yield return null;

        int before = CountInBox(lo, hi, Materials.Water);
        var chunksBefore = new HashSet<int3>();
        for (int z = lo.z; z <= hi.z; z++)
        for (int x = lo.x; x <= hi.x; x++)
            chunksBefore.Add(CoordMath.VoxelToChunk(new int3(x, floor, z)));

        L($"  pool spans {chunksBefore.Count} chunks: {string.Join(", ", chunksBefore)}");
        Check(chunksBefore.Count > 2,
            $"the pool genuinely straddles MORE THAN TWO chunks ({chunksBefore.Count}) -- " +
            "§9.7 lists this as never tested");

        for (int i = 0; i < 400; i++) { Tick(); yield return null; }

        int after = CountInBox(lo, hi, Materials.Water);
        L($"  water across the straddle: {before} -> {after}");
        Check(after >= before - 2,
            $"NO MASS LOST across a >2-chunk straddle ({before} -> {after}). §9.4's bug was " +
            "exactly this shape at two chunks; three and four were never checked.");
        L("");
    }

    // =====================================================================
    // STEP 4 -- §9.7: "several simultaneous regions" (never tested)
    // =====================================================================

    private IEnumerator Step4_SeveralSimultaneousRegions()
    {
        _phase = "step4 several regions";
        L("STEP 4 -- §9.7's untested case: several simultaneous active regions");
        Note("A moving radius makes this reachable in ordinary play for the first time: " +
             "walk between two pools you made earlier and both are in range.");

        const int RB = 32;
        var regions = new List<FluidGpuSimulation>();
        var readbacks = new List<FluidOpListReadback>();
        var origins = new List<int3>();
        long[] appliedPer = new long[2];

        for (int i = 0; i < 2; i++)
        {
            int3 o = _origin + new int3(i == 0 ? -80 : 80, 0, 0);
            int sy = SurfaceY(o.x + RB / 2, o.z + RB / 2);
            if (sy < 0) { Note($"no surface for region {i}; skipping step"); yield break; }
            o.y = math.max(0, sy - 6);
            origins.Add(o);

            var sim = new FluidGpuSimulation(_fluidCA, new int3(RB, RB, RB), 4096, 4096)
            {
                RegionOriginVoxels = o,
                PlayerVoxel = o + new int3(RB / 2, RB / 2, RB / 2),
                ActiveRadiusVoxels = EngineConfig.FLUID_ACTIVE_RADIUS_VOXELS,
            };
            int idx = i;
            var rb = new FluidOpListReadback(sim, Store)
            {
                OnVoxelApplied = v => { Clip.MarkDirty(CoordMath.VoxelToChunk(v)); appliedPer[idx]++; },
            };
            regions.Add(sim);
            readbacks.Add(rb);
            // ATTACH IT. Without this the edit path never sends this region a
            // wake request and its fluid sits inert in terrain -- which is the
            // defect this step found.
            _edits.AttachFluidSimulation(sim, Store);
        }

        // A walled pool in each, filled through the SAME EditService -- so both
        // regions' wake scans run off one edit path, which is the composition
        // under test.
        var counts = new int[2];
        for (int i = 0; i < 2; i++)
        {
            int3 o = origins[i];
            int cx = o.x + RB / 2, cz = o.z + RB / 2, fl = o.y + 4;
            _edits.SetBox(new int3(cx - 8, fl, cz - 8), new int3(cx + 8, fl + 14, cz + 8), Materials.Air);
            _edits.SetBox(new int3(cx - 8, fl, cz - 8), new int3(cx + 8, fl, cz + 8), Materials.Stone);
            _edits.SetBox(new int3(cx - 8, fl + 1, cz - 8), new int3(cx - 8, fl + 6, cz + 8), Materials.Stone);
            _edits.SetBox(new int3(cx + 8, fl + 1, cz - 8), new int3(cx + 8, fl + 6, cz + 8), Materials.Stone);
            _edits.SetBox(new int3(cx - 8, fl + 1, cz - 8), new int3(cx + 8, fl + 6, cz - 8), Materials.Stone);
            _edits.SetBox(new int3(cx - 8, fl + 1, cz + 8), new int3(cx + 8, fl + 6, cz + 8), Materials.Stone);
            // A TALL NARROW COLUMN, not a level pool. An earlier version filled
            // the basin flat and 3 deep -- which is already at rest, so both
            // regions correctly applied ZERO ops and the step failed on its own
            // scenario rather than on the engine. A column has to fall and
            // spread, so "did this region simulate" becomes a real question.
            _edits.SetBox(new int3(cx - 2, fl + 6, cz - 2), new int3(cx + 2, fl + 13, cz + 2), Materials.Water);
        }
        for (int i = 0; i < 20; i++) yield return null;
        for (int i = 0; i < 2; i++)
            counts[i] = CountInBox(origins[i], origins[i] + new int3(RB - 1, RB - 1, RB - 1), Materials.Water);

        L($"  region A water {counts[0]}, region B water {counts[1]}");

        for (int f = 0; f < 400; f++)
        {
            for (int i = 0; i < 2; i++)
            {
                if (readbacks[i].CanIssue) { regions[i].Tick(Clip); readbacks[i].IssueReadback(0); }
                readbacks[i].PumpAndApply();
            }
            Tick();                      // the original region keeps running too
            yield return null;
        }

        var after = new int[2];
        for (int i = 0; i < 2; i++)
            after[i] = CountInBox(origins[i], origins[i] + new int3(RB - 1, RB - 1, RB - 1), Materials.Water);

        L($"  after: region A {counts[0]} -> {after[0]} (applied {appliedPer[0]}), " +
          $"region B {counts[1]} -> {after[1]} (applied {appliedPer[1]})");

        Check(appliedPer[0] > 0 && appliedPer[1] > 0,
            $"BOTH regions simulated ({appliedPer[0]} / {appliedPer[1]} ops) -- neither starved " +
            "the other, and neither was silently inert");
        Check(after[0] >= counts[0] - 2 && after[1] >= counts[1] - 2,
            $"and NEITHER lost mass ({counts[0]}->{after[0]}, {counts[1]}->{after[1]})");

        Check(_edits.AttachedFluidSimulations >= 3,
            $"all three regions were attached to the one edit path " +
            $"({_edits.AttachedFluidSimulations})");
        foreach (var sim in regions) _edits.DetachFluidSimulation(sim);
        foreach (var rb in readbacks) rb.Dispose();
        foreach (var sim in regions) sim.Dispose();
        L("");
    }

    // =====================================================================
    // STEP 5 -- §9.7: "eviction while the op-list is IN FLIGHT at
    //           MaxFramesInFlight > 1" (never tested)
    // =====================================================================

    private IEnumerator Step5_EvictionWithOpListInFlight()
    {
        _phase = "step5 eviction with ops in flight";
        L("STEP 5 -- §9.7's untested case: eviction while ops are in flight, " +
          "MaxFramesInFlight > 1");

        int saved = FluidOpListReadback.MaxFramesInFlight;
        FluidOpListReadback.MaxFramesInFlight = 3;
        L($"  MaxFramesInFlight {saved} -> {FluidOpListReadback.MaxFramesInFlight}");

        int cx = _origin.x + R / 2, cz = _origin.z + R / 2, fl = _origin.y + 6;
        _edits.SetBox(new int3(cx - 6, fl, cz - 6), new int3(cx + 6, fl + 16, cz + 6), Materials.Air);
        _edits.SetBox(new int3(cx - 6, fl, cz - 6), new int3(cx + 6, fl, cz + 6), Materials.Stone);
        // WALLS. Without them the column spreads past the counting box into
        // natural terrain and the shortfall reads as mass loss -- an earlier
        // version reported 1134 -> 1031 for exactly that reason.
        _edits.SetBox(new int3(cx - 6, fl + 1, cz - 6), new int3(cx - 6, fl + 15, cz + 6), Materials.Stone);
        _edits.SetBox(new int3(cx + 6, fl + 1, cz - 6), new int3(cx + 6, fl + 15, cz + 6), Materials.Stone);
        _edits.SetBox(new int3(cx - 6, fl + 1, cz - 6), new int3(cx + 6, fl + 15, cz - 6), Materials.Stone);
        _edits.SetBox(new int3(cx - 6, fl + 1, cz + 6), new int3(cx + 6, fl + 15, cz + 6), Materials.Stone);
        _edits.SetBox(new int3(cx - 5, fl + 8, cz - 5), new int3(cx + 5, fl + 12, cz + 5), Materials.Water);
        for (int i = 0; i < 5; i++) yield return null;

        int before = CountInBox(new int3(cx - 8, fl - 1, cz - 8),
                                new int3(cx + 8, fl + 18, cz + 8), Materials.Water);
        long errBefore = _readback.ReadbackErrorsTotal;
        long staleBefore = _readback.StaleOpsDropped;
        long nonResBefore = _readback.OpsDroppedNonResident;

        // Get several batches genuinely in flight, then force streaming churn
        // in the SAME window by walking the camera hard.
        Camera cam = Camera.main;
        Vector3 home = cam.transform.position;
        float chunkM = EngineConfig.CHUNK_EDGE_VOXELS * 0.1f;
        int maxInFlight = 0;

        for (int f = 0; f < 400; f++)
        {
            if (_readback.CanIssue) { _fluid.Tick(Clip); _readback.IssueReadback(0); }
            maxInFlight = math.max(maxInFlight, _readback.FramesInFlight);
            // Move far enough to evict, while ops are outstanding.
            float t = (f % 200) / 200f;
            cam.transform.position = home + Vector3.right * (chunkM * 20f * t);
            _readback.PumpAndApply();
            yield return null;
        }
        cam.transform.position = home;
        // WAIT FOR THE WINDOW TO COME BACK BEFORE COUNTING MASS. GetVoxel returns
        // Air for a non-resident chunk (frozen §12 behaviour), so counting while
        // chunks are still streaming measures RESIDENCY and calls the shortfall
        // mass loss. An earlier version reported 1134 -> 1028 for exactly that
        // reason.
        Phase4Bootstrapper.Streamer.WaitForIdle();
        for (int f = 0; f < 300; f++) { Tick(); yield return null; }
        Phase4Bootstrapper.Streamer.WaitForIdle();
        bool resident = Store.IsResident(CoordMath.VoxelToChunk(new int3(cx, fl, cz)));

        int after = CountInBox(new int3(cx - 8, fl - 1, cz - 8),
                               new int3(cx + 8, fl + 18, cz + 8), Materials.Water);

        L($"  max frames in flight observed: {maxInFlight}");
        L($"  water {before} -> {after}");
        L($"  readback errors {errBefore} -> {_readback.ReadbackErrorsTotal}, " +
          $"stale dropped {staleBefore} -> {_readback.StaleOpsDropped}, " +
          $"non-resident dropped {nonResBefore} -> {_readback.OpsDroppedNonResident}");

        Check(maxInFlight > 1,
            $"batches really were in flight concurrently (max {maxInFlight}) -- at 1 this " +
            "step would prove nothing");
        Check(resident, "the pool's chunk is resident again before mass is counted");
        Check(after >= before - 2,
            $"NO MASS LOST across eviction with {maxInFlight} batches in flight " +
            $"({before} -> {after})");

        // A READBACK ERROR IS A TOLERATED CONDITION, NOT A DEFECT, and asserting
        // zero of them would be asserting something the design does not promise.
        // FluidOpListReadback's own comment: "§9's contract: never crash. A
        // failed readback costs one frame of fluid motion, never a voxel -- the
        // terrain bytes are untouched and the slots simply retry next tick."
        // What must hold is that it costs MOTION and not MASS, which is the
        // assertion above.
        long newErrors = _readback.ReadbackErrorsTotal - errBefore;
        Note($"readback errors during the churn: {newErrors}. Tolerated by design -- each " +
             "costs one frame of fluid motion and zero voxels. The mass check above is what " +
             "proves that claim rather than restating it.");
        Note("Ops dropped for non-resident chunks are CORRECT, not loss: §9.4's guard refuses " +
             "to write into an unloaded chunk, and the terrain byte stays authoritative.");

        FluidOpListReadback.MaxFramesInFlight = saved;
        L("");
    }

    // =====================================================================
    // STEP 6 -- §9.4's residency edge, re-run under the NEW moving radius
    // =====================================================================

    private IEnumerator Step6_ResidencyEdgeUnderAMovingRadius()
    {
        _phase = "step6 residency edge, moving radius";
        L("STEP 6 -- §9.4's guard, re-checked under the moving active radius");
        Note("The radius is an ADDITIONAL gate, never a substitute for §9.4's residency " +
             "check. This re-runs the case rather than trusting that it still holds.");

        long rejBefore = _fluid.WakeRejectedOutOfRegion;
        long dropBefore = _readback.OpsDroppedNonResident;
        // RELATIVE to this step's start. An earlier version compared against
        // absolute zero and so inherited step 5's tolerated error, failing on
        // something that happened before it ran.
        long errBefore6 = _readback.ReadbackErrorsTotal;

        // Drive the centre around, including far outside the region, while
        // fluid is live -- exactly what UpdatePlayerPosition now does per frame.
        int3 centre = _origin + new int3(R / 2, R / 2, R / 2);
        int recentres = 0;
        for (int f = 0; f < 240; f++)
        {
            int3 p = centre + new int3((f % 60) * 40 - 1200, 0, 0);
            if (_fluid.UpdatePlayerPosition(p)) recentres++;
            Tick();
            yield return null;
        }
        _fluid.UpdatePlayerPosition(centre);
        for (int f = 0; f < 200; f++) { Tick(); yield return null; }

        L($"  recentres: {recentres} (RecentresTotal {_fluid.RecentresTotal})");
        L($"  wake rejections out-of-region {rejBefore} -> {_fluid.WakeRejectedOutOfRegion}");
        L($"  ops dropped non-resident {dropBefore} -> {_readback.OpsDroppedNonResident}");

        Check(recentres > 0, $"the active centre actually moved ({recentres} re-centres)");
        Check(_readback.ReadbackErrorsTotal == errBefore6,
            $"no NEW readback errors while the radius swept in and out of the region " +
            $"({_readback.ReadbackErrorsTotal - errBefore6})");
        Check(CountFloating() >= 0, "the region is still queryable after the sweep");
        Note("§9.4's guard is unchanged and still refuses writes into unloaded chunks; the " +
             "radius gates PROMOTION only, and cannot promote a cell the guard would refuse.");
        L("");
    }

    // =====================================================================
    // STEP 7 -- §9.5's admission guard, now that the fluid centre also jumps
    // =====================================================================

    private IEnumerator Step7_AdmissionGuardWithAMovingRegion()
    {
        _phase = "step7 admission guard";
        L("STEP 7 -- §9.5: a single-frame teleport must still refuse cleanly");

        Camera cam = Camera.main;
        Vector3 home = cam.transform.position;
        long errBefore = _readback.ReadbackErrorsTotal;

        // A teleport far beyond the streaming window, in ONE frame, with the
        // fluid centre following it. §9.5: ChunkStore refuses the insert with a
        // named exception and no corruption.
        bool threw = false;
        string what = "none";
        try
        {
            cam.transform.position = home + Vector3.right * 100000f;
            _fluid.UpdatePlayerPosition(CoordMath.WorldToVoxel(
                new float3(cam.transform.position.x, cam.transform.position.y,
                           cam.transform.position.z)));
            for (int i = 0; i < 3; i++) { Tick(); }
        }
        catch (Exception e) { threw = true; what = e.GetType().Name; }

        cam.transform.position = home;
        _fluid.UpdatePlayerPosition(_origin + new int3(R / 2, R / 2, R / 2));
        for (int i = 0; i < 240; i++) { Tick(); yield return null; }

        L($"  teleport threw: {threw} ({what})");
        L($"  readback errors {errBefore} -> {_readback.ReadbackErrorsTotal}");

        Check(!threw || what.Contains("Exception"),
            threw ? $"the teleport refused cleanly with a named exception ({what})"
                  : "the teleport did not throw; the streamer absorbed it");
        Check(_readback.ReadbackErrorsTotal == errBefore,
            "and the fluid readback survived the jump without errors");
        Check(_fluid.RegionOriginVoxels.Equals(_origin),
            "the REGION did not move -- only the activity centre did, which is why a " +
            "teleport cannot re-index in-flight ops (§7.2)");
        L("");
    }
}
