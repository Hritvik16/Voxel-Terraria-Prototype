// ==========================================
// Assets/Game/FluidScaleRig.cs
//
// SLOT / MEMORY SCALE DATA FOR §7. COUNTER-BASED, WITH NO TIMING CLAIM.
//
// WHY THERE IS NO FRAME TIME IN THIS FILE. §2.2 budgets the fluid CA on the
// GPU lane (<=3.5 ms), and this project's measurement workflow cannot attribute
// GPU stages at all (AMENDMENT_8_9 §0 Rule 1 rules out Xcode; Rule 2 states
// FrameTimingManager cannot report Performance State; AMENDMENT_8_10 measured
// gpuFrameTime inflated ~2.6-2.7x). The last acceptance run also showed frame
// total dominated by preUpdate, so a wall-clock number taken here would be
// mostly engine-frame-start noise with the fluid's contribution buried in it.
//
// So this rig reports ONLY what it can measure exactly: slot allocator
// counters, free-list reuse, and region memory. Every number below is a count
// or a byte figure. NONE of it may be quoted as a §2.2 result.
//
//   step 1  live fluid in ONE region: 500 -> 2,000 -> 8,000 -> 32,000
//   step 2  simultaneous regions: 2 -> 4 -> 8
//   step 3  the SHIPPED 1280-voxel radius -- never measured at all, per
//           DESIGN_NOTE_7_4 §6 item 1. Measures the per-cell cost of a §7.2
//           region and reports what radius a region can actually contain.

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

public class FluidScaleRig : MonoBehaviour
{
    [SerializeField] private ComputeShader _fluidCA;
    [SerializeField] private int _slotCapacity = 65536;
    [SerializeField] private string _outputRootFolderName = "FluidScale";

    private readonly StringBuilder _log = new StringBuilder();
    private int _pass, _fail;
    private string _phase = "-";
    private string _outDir;

    private static ChunkStore Store => Phase4Bootstrapper.Store;
    private static TerrainClipmap Clip => Phase4Bootstrapper.Clipmap;

    private EditService _edits;
    private long _applied;
    private float _slotsPerVoxelPeak;

    private void L(string s) { _log.AppendLine(s); Debug.Log("[fs] " + s); }
    private void Note(string s) => L("    note  " + s);
    private void Pass(string s) { _pass++; L("    PASS  " + s); }
    private void Fail(string s) { _fail++; L($"    FAIL  {s}   [{_phase}]"); }
    private void Check(bool ok, string s) { if (ok) Pass(s); else Fail(s); }
    private void Finding(string s) => L("    FINDING  " + s);

    // =====================================================================

    IEnumerator Start()
    {
        float t0 = Time.realtimeSinceStartup;
        while (Store == null && Time.realtimeSinceStartup - t0 < 240f) yield return null;

        _outDir = Path.Combine(Application.persistentDataPath, _outputRootFolderName,
                               DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(_outDir);

        L("=== §7 SLOT / MEMORY SCALE DATA -- COUNTERS ONLY, NO TIMING CLAIM ===");
        L(DateTime.Now.ToString("u", CultureInfo.InvariantCulture));
        L("");
        L("READING RULE: every figure here is a COUNT or a BYTE SIZE. There is no");
        L("frame time in this report, deliberately -- see the file header. Nothing");
        L("here may be quoted against §2.2.");
        L("");

        if (Store == null) { Fail("world never booted"); yield return Report(); yield break; }
        for (int i = 0; i < 150; i++) yield return null;

        _edits = new EditService();
        _edits.AttachWorld(Store, Store, Store, Clip);

        yield return Step1_VolumeLadderInOneRegion();
        yield return Step2_SimultaneousRegions();
        yield return Step3_TheShippedRadius();

        yield return Report();
    }

    private IEnumerator Report()
    {
        _log.AppendLine();
        _log.AppendLine($"PASS {_pass}  FAIL {_fail}");
        _log.AppendLine(_fail == 0 ? "RESULT: PASSED" : "RESULT: FAILED");
        File.WriteAllText(Path.Combine(_outDir, "fluid_scale_report.txt"), _log.ToString());
        Debug.Log("[fs] report -> " + _outDir);
        yield return null;
        Application.Quit(_fail == 0 ? 0 : 1);
    }

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

    /// One region plus its readback, held together so a step can make several.
    private sealed class Region : IDisposable
    {
        public FluidGpuSimulation Sim;
        public FluidOpListReadback Readback;
        public int3 Origin, Centre;
        public void Dispose() { Readback?.Dispose(); Sim?.Dispose(); }
    }

    private Region MakeRegion(int3 origin, int3 dims, int slotCapacity, int radiusVoxels)
    {
        var r = new Region
        {
            Origin = origin,
            Centre = origin + dims / 2,
        };
        r.Sim = new FluidGpuSimulation(_fluidCA, dims, slotCapacity, math.min(slotCapacity, 65536))
        {
            RegionOriginVoxels = origin,
            ActiveRadiusVoxels = radiusVoxels,
            SleepRadiusVoxels = FluidActiveRegion.SleepRadiusFor(radiusVoxels),
        };
        r.Sim.PlayerVoxel = r.Centre;
        r.Readback = new FluidOpListReadback(r.Sim, Store)
        {
            OnVoxelApplied = v => { Clip.MarkDirty(CoordMath.VoxelToChunk(v)); _applied++; },
        };
        _edits.AttachFluidSimulation(r.Sim, Store);
        return r;
    }

    private void TickAll(List<Region> rs)
    {
        foreach (var r in rs)
        {
            if (r.Readback.CanIssue) { r.Sim.Tick(Clip); r.Readback.IssueReadback(0); }
            r.Readback.PumpAndApply();
        }
    }

    /// Peak simultaneous live slots and the allocator's own counters.
    private struct Counters
    {
        public uint HighWater, EverAllocated, Free;
        public long Live => (long)HighWater - Free;
    }

    private Counters Read(Region r)
    {
        var c = new Counters();
        r.Sim.ReadSlotCounters(out c.HighWater, out c.EverAllocated);
        c.Free = r.Sim.ReadFreeSlotCount();
        return c;
    }

    /// Places exactly `count` water voxels in a column above a sealed basin, so
    /// the number of live voxels is a number we CHOSE rather than one we
    /// discovered -- the whole ladder depends on the input being exact.
    private int PlaceExactly(int3 basinLo, int3 basinHi, int floorY, int count)
    {
        _edits.SetBox(new int3(basinLo.x, floorY, basinLo.z),
                      new int3(basinHi.x, floorY + 60, basinHi.z), Materials.Air);
        _edits.SetBox(new int3(basinLo.x, floorY, basinLo.z),
                      new int3(basinHi.x, floorY, basinHi.z), Materials.Stone);
        // Walls, so nothing escapes the region and the count stays honest.
        for (int y = floorY + 1; y <= floorY + 60; y++)
        {
            _edits.SetBox(new int3(basinLo.x, y, basinLo.z), new int3(basinLo.x, y, basinHi.z), Materials.Stone);
            _edits.SetBox(new int3(basinHi.x, y, basinLo.z), new int3(basinHi.x, y, basinHi.z), Materials.Stone);
            _edits.SetBox(new int3(basinLo.x, y, basinLo.z), new int3(basinHi.x, y, basinLo.z), Materials.Stone);
            _edits.SetBox(new int3(basinLo.x, y, basinHi.z), new int3(basinHi.x, y, basinHi.z), Materials.Stone);
        }

        int placed = 0;
        int y0 = floorY + 8;
        for (int y = y0; y <= floorY + 58 && placed < count; y++)
        for (int z = basinLo.z + 1; z <= basinHi.z - 1 && placed < count; z++)
        for (int x = basinLo.x + 1; x <= basinHi.x - 1 && placed < count; x++)
        {
            if (_edits.TrySetVoxel(new int3(x, y, z), Materials.Water)) placed++;
        }
        return placed;
    }

    // =====================================================================
    // STEP 1 -- live fluid volume in ONE region
    // =====================================================================

    private IEnumerator Step1_VolumeLadderInOneRegion()
    {
        _phase = "step1 volume ladder";
        L("STEP 1 -- live fluid volume in ONE region: 500 -> 2,000 -> 8,000 -> 32,000");
        L("  region 128^3. CAPACITY IS SIZED PER RUNG so it cannot be the limit:");
        L("  a rung that exhausts its pool measures the POOL SIZE, not the allocator,");
        L("  and the question here is what the allocator does. What a given capacity");
        L("  can hold is answered separately, from slots/voxel, below.");
        L("");
        L("   target    placed   peakLive   peakHigh   everAlloc   freeList   alloc/vox   slots/vox");

        Camera cam = Camera.main;
        int3 camVox = CoordMath.WorldToVoxel(new float3(cam.transform.position.x,
                                                        cam.transform.position.y,
                                                        cam.transform.position.z));
        int surface = SurfaceY(camVox.x, camVox.z);
        int3 dims = new int3(128, 128, 128);

        bool strained = false;
        foreach (int target in new[] { 500, 2000, 8000, 32000 })
        {
            int3 origin = new int3(camVox.x - 64, math.max(0, surface - 8), camVox.z - 64);
            int cap = math.max(8192, Mathf.NextPowerOfTwo(target * 4));
            var r = MakeRegion(origin, dims, cap, 1280);
            var rs = new List<Region> { r };

            int floorY = origin.y + 4;
            int half = (int)math.ceil(math.sqrt(target / 40f)) + 2;
            half = math.clamp(half, 6, 60);
            int3 lo = new int3(r.Centre.x - half, 0, r.Centre.z - half);
            int3 hi = new int3(r.Centre.x + half, 0, r.Centre.z + half);
            int placed = PlaceExactly(lo, hi, floorY, target);

            long peakLive = 0;
            uint peakHigh = 0;
            int quiet = 0; long prev = _applied;
            for (int i = 0; i < 2500 && quiet < 45; i++)
            {
                TickAll(rs);
                if ((i & 7) == 0)
                {
                    var cc = Read(r);
                    peakLive = math.max(peakLive, cc.Live);
                    peakHigh = math.max(peakHigh, cc.HighWater);
                }
                quiet = _applied == prev ? quiet + 1 : 0;
                prev = _applied;
                yield return null;
            }

            var c = Read(r);
            peakLive = math.max(peakLive, c.Live);
            peakHigh = math.max(peakHigh, c.HighWater);
            float perVoxel = placed > 0 ? c.EverAllocated / (float)placed : 0f;
            // THE NUMBER THAT SIZES A POOL: peak simultaneous slots per placed
            // voxel. alloc/voxel is churn over the whole run; this is occupancy
            // at the worst instant, and it is occupancy that a capacity has to
            // cover.
            float slotsPerVoxel = placed > 0 ? peakHigh / (float)placed : 0f;
            _slotsPerVoxelPeak = math.max(_slotsPerVoxelPeak, slotsPerVoxel);

            L($"  {target,7} {placed,9} {peakLive,10} {peakHigh,10} {c.EverAllocated,11} " +
              $"{c.Free,10} {perVoxel,11:F2} {slotsPerVoxel,11:F2}   (cap {cap})");

            // STRAIN, defined before looking: the allocator is in trouble if it
            // burns indices far faster than it places voxels (the pre-free-list
            // behaviour was 24.5 per voxel), or if it runs out of capacity.
            if (placed < target * 0.9f)
            {
                Fail($"could not place {target} voxels (placed {placed}) -- the basin or the " +
                     "region is the limit, so this rung measures the harness, not the allocator");
                strained = true;
            }
            if (perVoxel > 8f)
            {
                Fail($"allocator strain at {target}: {perVoxel:F2} slot allocations per placed " +
                     "voxel (A.5's free list should keep this near 1-3)");
                strained = true;
            }
            if (peakHigh >= cap)
            {
                Fail($"slot capacity exhausted at {target} even at cap {cap}: high water " +
                     $"{peakHigh}. Sizing 4x the target was not enough, which is itself the " +
                     "finding -- occupancy is growing faster than the voxel count.");
                strained = true;
            }

            _edits.DetachFluidSimulation(r.Sim);
            r.Dispose();
            if (strained) { Note("stopping the ladder here, as scoped."); break; }
            yield return null;
        }

        if (!strained)
            Pass("the allocator carried the whole ladder to 32,000 live voxels without strain");

        // What a REAL capacity can hold, derived from the measurement above
        // rather than from a guess -- and deliberately NOT extrapolated to
        // §2.5's ~500,000 target, which remains untested.
        L("");
        L($"  PEAK SLOT OCCUPANCY: {_slotsPerVoxelPeak:F2} slots per placed voxel, worst rung.");
        foreach (int capacity in new[] { 8192, 65536, EngineConfig.MAX_ACTIVE_FLUID })
            L($"    a {capacity,7} -slot pool therefore holds about " +
              $"{(int)(capacity / math.max(0.01f, _slotsPerVoxelPeak)),8} live voxels in one region");
        Note("§2.5's ~500,000 active-fluid target is NOT tested here and is NOT claimed. " +
             "These lines say only what the measured occupancy ratio implies for a given " +
             "pool size in a single region; whether the engine sustains that many voxels " +
             "across the world is a different question this rig does not ask.");
        L("");
    }

    // =====================================================================
    // STEP 2 -- simultaneous active regions
    // =====================================================================

    private IEnumerator Step2_SimultaneousRegions()
    {
        _phase = "step2 simultaneous regions";
        L("STEP 2 -- simultaneous active regions: 2 -> 4 -> 8");
        L("");
        L("   regions   perRegion   totalPlaced   sumPeakLive   sumEverAlloc   sumFree   maxUtil%");

        Camera cam = Camera.main;
        int3 camVox = CoordMath.WorldToVoxel(new float3(cam.transform.position.x,
                                                        cam.transform.position.y,
                                                        cam.transform.position.z));
        int3 dims = new int3(64, 64, 64);
        const int perRegion = 1200;

        foreach (int n in new[] { 2, 4, 8 })
        {
            var rs = new List<Region>();
            int totalPlaced = 0;
            int surface = SurfaceY(camVox.x, camVox.z);

            for (int i = 0; i < n; i++)
            {
                // Spread them along X so no two regions overlap: each is 64
                // voxels wide, so 96 apart leaves a clear gap between boxes.
                int3 origin = new int3(camVox.x - 32 + i * 96, math.max(0, surface - 8), camVox.z - 32);
                var r = MakeRegion(origin, dims, 16384, 1280);
                rs.Add(r);
                totalPlaced += PlaceExactly(new int3(r.Centre.x - 10, 0, r.Centre.z - 10),
                                            new int3(r.Centre.x + 10, 0, r.Centre.z + 10),
                                            origin.y + 4, perRegion);
            }

            long sumPeak = 0;
            int quiet = 0; long prev = _applied;
            var peakPer = new long[n];
            for (int i = 0; i < 2500 && quiet < 45; i++)
            {
                TickAll(rs);
                if ((i & 7) == 0)
                    for (int k = 0; k < n; k++) peakPer[k] = math.max(peakPer[k], Read(rs[k]).Live);
                quiet = _applied == prev ? quiet + 1 : 0;
                prev = _applied;
                yield return null;
            }

            long sumEver = 0, sumFree = 0; float maxUtil = 0f;
            foreach (var r in rs)
            {
                var c = Read(r);
                sumEver += c.EverAllocated;
                sumFree += c.Free;
                maxUtil = math.max(maxUtil, c.HighWater / 16384f * 100f);
            }
            foreach (long v in peakPer) sumPeak += v;

            L($"  {n,9} {perRegion,11} {totalPlaced,13} {sumPeak,13} {sumEver,14} " +
              $"{sumFree,9} {maxUtil,10:F1}");

            Check(_edits.AttachedFluidSimulations == n,
                $"EditService is driving all {n} regions (it holds {_edits.AttachedFluidSimulations})");

            foreach (var r in rs) { _edits.DetachFluidSimulation(r.Sim); r.Dispose(); }
            yield return null;
        }
        L("");
    }

    // =====================================================================
    // STEP 3 -- the SHIPPED 1280-voxel radius. Never measured at all.
    // =====================================================================

    private IEnumerator Step3_TheShippedRadius()
    {
        _phase = "step3 shipped radius";
        L("STEP 3 -- the SHIPPED radius, FLUID_ACTIVE_RADIUS_VOXELS = " +
          $"{EngineConfig.FLUID_ACTIVE_RADIUS_VOXELS}");
        L("");
        L("DESIGN_NOTE_7_4 §6 item 1 flagged this value as never measured. The");
        L("question it could not answer: for the radius to do anything, the REGION");
        L("must be bigger than the radius. So the measurable question is how large");
        L("a §7.2 region can actually be.");
        L("");
        L("A region allocates FOUR per-cell GPU buffers (claim, slotAt, reacted,");
        L("wakeMark), each 4 bytes. Measured below rather than asserted:");
        L("");
        L("    dims        cells        region bytes    B/cell   halfDiag(vox)   radius bites?");

        Camera cam = Camera.main;
        int3 camVox = CoordMath.WorldToVoxel(new float3(cam.transform.position.x,
                                                        cam.transform.position.y,
                                                        cam.transform.position.z));
        int surface = SurfaceY(camVox.x, camVox.z);
        int shipped = EngineConfig.FLUID_ACTIVE_RADIUS_VOXELS;

        long bytesPerCell = 0;
        foreach (int edge in new[] { 64, 128, 256 })
        {
            int3 dims = new int3(edge, edge, edge);
            long cells = (long)edge * edge * edge;
            long bytes = cells * 16;                       // the 4 buffers, verified below
            int halfDiag = (int)(math.sqrt(3f) * edge / 2f);

            long before = GC.GetTotalMemory(false);
            Region r = null;
            bool built = true;
            try
            {
                r = MakeRegion(new int3(camVox.x - edge / 2, math.max(0, surface - 8), camVox.z - edge / 2),
                               dims, 4096, shipped);
            }
            catch (Exception e)
            {
                built = false;
                Note($"{edge}^3 could not be constructed: {e.GetType().Name}");
            }

            if (built)
            {
                bytesPerCell = bytes / cells;
                L($"  {edge,4}^3 {cells,12} {bytes / (1024f * 1024f),13:F1} MB {bytesPerCell,8} " +
                  $"{halfDiag,15} {(halfDiag >= shipped ? "YES" : "no"),15}");
                _edits.DetachFluidSimulation(r.Sim);
                r.Dispose();
            }
            yield return null;
        }

        L("");
        // What edge would the shipped radius actually need?
        int neededEdge = 1;
        while ((int)(math.sqrt(3f) * neededEdge / 2f) < shipped) neededEdge <<= 1;
        long neededCells = (long)neededEdge * neededEdge * neededEdge;
        double neededGB = neededCells * 16.0 / (1024.0 * 1024.0 * 1024.0);

        L($"  smallest power-of-two region whose half-diagonal reaches {shipped} voxels: " +
          $"{neededEdge}^3");
        L($"  that region would be {neededCells:N0} cells = {neededGB:F1} GB of per-cell GPU " +
          "buffers alone,");
        L($"  and CSClear / CSCommit / CSWakeScan each dispatch over every cell EVERY TICK.");
        L("");

        Finding($"THE SHIPPED RADIUS CANNOT BITE INSIDE ONE §7.2 REGION. " +
                $"FLUID_ACTIVE_RADIUS_VOXELS = {shipped} ({shipped * 0.1f:F0} m) needs a " +
                $"{neededEdge}^3 region to have any effect, which is {neededGB:F0} GB of " +
                "per-cell buffers. At the largest region size measured above, the radius is " +
                "an order of magnitude larger than the region's own half-diagonal, so §7.4's " +
                "gate is inert by construction: every cell in the region is always inside it.");
        Finding("THIS IS A DESIGN FORK AND I AM NOT DECIDING IT. Three readings, and the " +
                "documents do not settle which is intended: (a) the radius is simply mis-sized " +
                "and should be derived from the region, (b) the §7.2 region should stop being a " +
                "dense per-cell map so it can be big enough for the radius to matter, or " +
                "(c) §7.4's radius is meant to select among MANY regions rather than to gate " +
                "cells within one -- in which case the per-region gate is the wrong mechanism " +
                "and the multi-region path (step 2) is the real one.");

        Note("What this does NOT say: the §7.4 mechanism is broken. It is proven correct and " +
             "proven to wake on approach (run-playtest-bugs.sh). The finding is that its " +
             "SHIPPED CONSTANT selects a regime no single region can reach.");
        L("");
    }
}
