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
}
