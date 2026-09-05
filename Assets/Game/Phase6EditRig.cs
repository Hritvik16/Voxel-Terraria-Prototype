// ==========================================
// Assets/Game/Phase6EditRig.cs
//
// EDIT SERVICE ACCEPTANCE RIG. §13 Phase 6 file 3's acceptance slice, driven
// mechanically. Diagnostic scene, not a demo.
//
// EditServiceTests proves the orchestration against a fake writer: what gets
// written, what gets marked dirty, what gets refused. What it cannot prove, and
// what this rig is for:
//   * the same path against the REAL ChunkStore -- real uniform-chunk
//     expansion, real brick densification, real dirty flags
//   * the tool tiers producing their §13 rates (10/40/200 vox/s) against real
//     terrain over real elapsed time
//   * §13's headline for this file: the wake-scan actually reaching the Phase-5
//     FLUID POOL, so breaching a water body makes the water move (§10.4's M-E)
//   * digging at the edge of the streaming window being REFUSED and counted
//     rather than silently doing nothing (§9.4 applied to mining)
//
// NOT COVERED HERE, AND SAID PLAINLY: §13's "persists through save/reload,
// coalesces on fill-in" for a drilled region. That is §10.4's M-G, an edit x
// persistence integration, and it belongs with the Phase 4 persistence rig
// rather than bolted onto this one. This rig makes no claim about it.
//
// NO TIMING. Voxel RATES are reported (that is what a tool tier IS), but no ms
// figures. Performance stays with run-acceptance-rig.sh.

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

public class Phase6EditRig : MonoBehaviour
{
    [SerializeField] private ComputeShader _fluidCA;
    [SerializeField] private int _slotCapacity = 65536;
    [SerializeField] private int _maxOpsPerFrame = 65536;
    [SerializeField] private string _outputRootFolderName = "Phase6Edit";

    // The fluid arena for the wake-scan step. Half a chunk on each axis.
    private const int RX = 64, RY = 32, RZ = 64;

    private readonly StringBuilder _log = new StringBuilder();
    private int _pass, _fail;
    private string _phase = "-";
    private string _outDir;
    private int _shotIndex;

    private static ChunkStore Store => Phase4Bootstrapper.Store;
    private static TerrainClipmap Clip => Phase4Bootstrapper.Clipmap;

    private EditService _edits;
    private FluidGpuSimulation _fluid;
    private FluidOpListReadback _readback;
    private int3 _regionOrigin;
    private long _applied;

    private void L(string s) { _log.AppendLine(s); Debug.Log("[6ed] " + s); }
    private void Note(string s) => L("    note  " + s);
    private void Pass(string s) { _pass++; L("    PASS  " + s); }
    private void Fail(string s) { _fail++; L($"    FAIL  {s}   [{_phase}]"); }
    private void Check(bool ok, string s) { if (ok) Pass(s); else Fail(s); }

    // =====================================================================

    IEnumerator Start()
    {
        float t0 = Time.realtimeSinceStartup;
        while (Store == null && Time.realtimeSinceStartup - t0 < 180f) yield return null;

        _outDir = Path.Combine(Application.persistentDataPath, _outputRootFolderName,
                               DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(_outDir);

        L("=== PHASE 6 FILE 3: EDIT SERVICE ACCEPTANCE ===");
        L(DateTime.Now.ToString("u", CultureInfo.InvariantCulture));
        L("");

        if (Store == null) { Fail("world never booted"); yield return Report(); yield break; }
        for (int i = 0; i < 120; i++) yield return null;

        // The region the fluid arena will cover, centred under the camera.
        Camera cam = Camera.main;
        int3 camVox = cam != null
            ? CoordMath.WorldToVoxel(new float3(cam.transform.position.x, cam.transform.position.y,
                                                cam.transform.position.z))
            : new int3(12800, 40, 12680);
        _regionOrigin = new int3(camVox.x - RX / 2, math.max(0, SurfaceY(camVox.x, camVox.z) - 8),
                                 camVox.z - RZ / 2);

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

        Check(_edits.IsWired, "EditService wired to the real ChunkStore and TerrainClipmap");
        L($"fluid arena {RX}x{RY}x{RZ} at {_regionOrigin}");
        L($"tiers: {string.Join(", ", TierNames())}");
        L("");

        yield return Step1_MineAndBuildThroughTheRealPath();
        yield return Step2_ToolTiersProduceTheirRates();
        yield return Step3_SustainedDrillAtTopTier();
        yield return Step4_DiggingOutsideTheWindowIsRefused();
        yield return Step5_BreachAWaterBodyAndTheFluidWakes();
        yield return Step6_PrefabPlacement();

        yield return Report();
    }

    private IEnumerable<string> TierNames()
    {
        foreach (var t in EditService.Tiers) yield return $"{t.Name} {t.VoxelsPerSecond} vox/s r{t.RadiusVoxels}";
    }

    private IEnumerator Report()
    {
        _log.AppendLine();
        _log.AppendLine($"PASS {_pass}  FAIL {_fail}");
        _log.AppendLine(_fail == 0 ? "RESULT: PASSED" : "RESULT: FAILED");
        File.WriteAllText(Path.Combine(_outDir, "phase6_edit_report.txt"), _log.ToString());
        Debug.Log("[6ed] report -> " + _outDir);
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

    private int CountInRegion(byte m)
    {
        int n = 0;
        for (int z = 0; z < RZ; z++)
        for (int y = 0; y < RY; y++)
        for (int x = 0; x < RX; x++)
            if (Store.GetVoxel(_regionOrigin + new int3(x, y, z)) == m) n++;
        return n;
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
    // STEP 1 -- §10.4 M-A: mine a single block, and build one, through the
    //           REAL ChunkStore rather than a fake writer
    // =====================================================================

    private IEnumerator Step1_MineAndBuildThroughTheRealPath()
    {
        _phase = "step1 mine and build";
        L("STEP 1 -- mine one block and build one, through the real ChunkStore");

        int cx = _regionOrigin.x + RX / 2, cz = _regionOrigin.z + RZ / 2;
        int sy = SurfaceY(cx, cz);
        int3 target = new int3(cx, sy, cz);

        byte before = Store.GetVoxel(target);
        Check(before != Materials.Air, $"the target is solid terrain to begin with ({before})");

        _edits.ResetCounters();
        bool mined = _edits.TrySetVoxel(target, Materials.Air);

        Check(mined, "the mine reports success");
        Check(Store.GetVoxel(target) == Materials.Air, "and the voxel really is Air in ChunkStore");
        Check(_edits.VoxelsWritten == 1, $"one voxel written ({_edits.VoxelsWritten})");

        // Mining the same cell again must be a no-op, not a second write.
        bool again = _edits.TrySetVoxel(target, Materials.Air);
        Check(!again, "mining the same cell again is a no-op");
        Check(_edits.EditsNoOp == 1, $"and is counted as one ({_edits.EditsNoOp})");

        // Build it back with a different material.
        bool built = _edits.TrySetVoxel(target, Materials.Stone);
        Check(built, "building into the hole reports success");
        Check(Store.GetVoxel(target) == Materials.Stone, "and the voxel is stone");

        L($"  writes {_edits.VoxelsWritten}, no-ops {_edits.EditsNoOp}, " +
          $"residency misses {_edits.EditsRejectedNotResident}");
        L("");
        yield return null;
    }

    // =====================================================================
    // STEP 2 -- §13: "tools at 10/40/200 vox/s". The rates must be REAL.
    // =====================================================================

    private IEnumerator Step2_ToolTiersProduceTheirRates()
    {
        _phase = "step2 tool tiers";
        L("STEP 2 -- each tier removes rock at ITS OWN RATE over one simulated second");

        const float dt = 1f / 60f;
        var removedByTier = new int[EditService.Tiers.Length];

        for (int t = 0; t < EditService.Tiers.Length; t++)
        {
            var tier = EditService.Tiers[t];

            // A fresh slab per tier, so tiers never compete for the same rock.
            int bx = _regionOrigin.x + 8 + t * 18;
            int by = _regionOrigin.y + 4;
            int bz = _regionOrigin.z + 8;
            _edits.SetBox(new int3(bx - 8, by - 3, bz - 8), new int3(bx + 8, by + 3, bz + 8),
                          Materials.Stone);

            _edits.ResetCounters();
            var budget = new EditService.ToolBudget();
            int removed = 0;
            int cursor = 0;

            for (int f = 0; f < 60; f++)                     // one simulated second
            {
                int allowance = budget.Accrue(dt, tier.VoxelsPerSecond);
                if (allowance <= 0) continue;

                // THE TOOL MOVES, and that is what makes this a RATE test.
                // Held still, every tier empties its brush within the first
                // frames and then reports the BRUSH SIZE rather than the rate:
                // the first version measured 7 / 33 / 123 -- exactly the cell
                // counts of a radius 1 / 2 / 3 sphere -- for tiers nominally at
                // 10 / 40 / 200 vox/s. The rate never bound anything, so the
                // step proved three brush sizes and claimed to prove three
                // speeds. Walking the cursor keeps fresh rock under the tool so
                // the budget is the only limit.
                int3 at = new int3(bx - 6 + (cursor / 13) % 13, by, bz - 6 + cursor % 13);
                removed += _edits.MineSphereBudgeted(at, tier.RadiusVoxels, allowance, Materials.Air);
                cursor++;
            }

            removedByTier[t] = removed;
            int expected = (int)tier.VoxelsPerSecond;
            L($"  {tier.Name,-6} {tier.VoxelsPerSecond,5} vox/s r{tier.RadiusVoxels} -> removed {removed} in 1.0 s (expected ~{expected})");

            Check(math.abs(removed - expected) <= math.max(2, expected / 10),
                $"{tier.Name} removed {removed} voxels in one second against a nominal " +
                $"{expected} -- the RATE is what bounds it, not the brush");
            Check(_edits.VoxelsWritten == removed,
                $"{tier.Name}: every removal is a real write ({_edits.VoxelsWritten} vs {removed})");

            yield return null;
        }

        Check(removedByTier[0] < removedByTier[1] && removedByTier[1] < removedByTier[2],
            $"and the tiers are strictly ordered: {removedByTier[0]} < {removedByTier[1]} " +
            $"< {removedByTier[2]}. Without this a rate bug that scaled every tier the same " +
            "way would still pass each individual check.");
        L("");
    }

    // =====================================================================
    // STEP 3 -- §13: "drill one region at 200 vox/s". Sustained, and the
    //           removals must keep pace rather than stalling.
    // =====================================================================

    private IEnumerator Step3_SustainedDrillAtTopTier()
    {
        _phase = "step3 sustained drill";
        L("STEP 3 -- sustained drilling at the top tier (200 vox/s) for 5 simulated seconds");

        var tier = EditService.Tiers[2];
        const float dt = 1f / 60f;
        const int seconds = 5;

        // A solid block big enough that 1000 voxels cannot exhaust it.
        int bx = _regionOrigin.x + RX / 2, by = _regionOrigin.y + 10, bz = _regionOrigin.z + RZ / 2;
        _edits.SetBox(new int3(bx - 12, by - 12, bz - 12), new int3(bx + 12, by + 12, bz + 12),
                      Materials.Stone);

        _edits.ResetCounters();
        var budget = new EditService.ToolBudget();
        int removed = 0;
        int cursor = 0;

        for (int f = 0; f < 60 * seconds; f++)
        {
            int allowance = budget.Accrue(dt, tier.VoxelsPerSecond);
            if (allowance <= 0) continue;

            // Walk the drill along a line so it keeps meeting fresh rock,
            // rather than re-mining an already-empty sphere and reporting a
            // stall that is really just "nothing left here".
            int3 at = new int3(bx - 10 + (cursor / 6) % 21, by, bz - 10 + (cursor % 21));
            removed += _edits.MineSphereBudgeted(at, tier.RadiusVoxels, allowance, Materials.Air);
            cursor++;
            if ((f % 60) == 59) yield return null;
        }

        int expected = (int)(tier.VoxelsPerSecond * seconds);
        L($"  removed {removed} voxels in {seconds} s at {tier.VoxelsPerSecond} vox/s (expected ~{expected})");
        L($"  writes {_edits.VoxelsWritten}, no-ops {_edits.EditsNoOp}, " +
          $"residency misses {_edits.EditsRejectedNotResident}");

        Check(removed >= expected * 0.9f,
            $"the drill kept pace with its rate ({removed} vs ~{expected}) -- a big shortfall " +
            "means the budget is being dropped rather than carried");
        Check(_edits.EditsRejectedNotResident == 0,
            "and none of it was lost to a residency miss inside the loaded window");

        yield return Shot("step3_drilled_region",
            new float3((bx - 20) * 0.1f, (by + 14) * 0.1f, (bz - 20) * 0.1f),
            new float3(bx * 0.1f, by * 0.1f, bz * 0.1f));
        L("");
    }

    // =====================================================================
    // STEP 4 -- §9.4 applied to mining: digging into an unloaded chunk is
    //           refused and COUNTED, not silently swallowed
    // =====================================================================

    private IEnumerator Step4_DiggingOutsideTheWindowIsRefused()
    {
        _phase = "step4 dig outside the window";
        L("STEP 4 -- digging outside the loaded window is refused and counted");

        // Far outside any plausible streaming window.
        int3 far = new int3(_regionOrigin.x + 400000, 40, _regionOrigin.z + 400000);
        Check(!Store.IsResident(CoordMath.VoxelToChunk(far)),
            $"the target chunk {CoordMath.VoxelToChunk(far)} really is not resident");

        _edits.ResetCounters();
        bool dug = _edits.TrySetVoxel(far, Materials.Air);

        Check(!dug, "the dig reports FAILURE rather than a silent success");
        Check(_edits.EditsRejectedNotResident == 1,
            $"and is counted as a residency miss ({_edits.EditsRejectedNotResident})");
        Check(_edits.VoxelsWritten == 0, "with no write recorded");

        Note("This is the §9.4 lesson applied to editing: ChunkStore.SetVoxel drops the " +
             "write silently, so without this check a mining tool would report breaking a " +
             "block that is still there.");
        L("");
        yield return null;
    }

    // =====================================================================
    // STEP 5 -- §13's headline for this file: the wake-scan reaching the
    //           Phase-5 fluid pool. §10.4's M-E, "breach a water body".
    // =====================================================================

    private IEnumerator Step5_BreachAWaterBodyAndTheFluidWakes()
    {
        _phase = "step5 breach a water body";
        L("STEP 5 -- breach a water body; the wake-scan must make the fluid MOVE OUT");

        // ITS OWN VOLUME, AND AN APRON TO DRAIN INTO. The first version put the
        // basin at the arena centre, which steps 2 and 3 had already filled with
        // a 25^3 stone block -- so the basin was carved INSIDE solid rock, the
        // breach opened onto more rock, and the water had nowhere to go. It
        // still registered 766 applied ops and passed, because sloshing inside a
        // sealed basin is movement. It just was not DRAINAGE, which is what the
        // step claims. The screenshot showed the outside of a stone cube and
        // gave it away.
        int bx = _regionOrigin.x + 16, bz = _regionOrigin.z + RZ / 2;
        int floor = _regionOrigin.y + 6;
        const int R = 8;                      // basin half-width
        const int APRON = 22;                 // open ground on the +X side

        // Clear everything around the basin AND the apron it will drain into.
        _edits.SetBox(new int3(bx - R - 2, floor, bz - R - 2),
                      new int3(bx + APRON, floor + 18, bz + R + 2), Materials.Air);
        _edits.SetBox(new int3(bx - R - 2, floor, bz - R - 2),
                      new int3(bx + APRON, floor, bz + R + 2), Materials.Stone);

        // Four walls, 6 tall, around the basin only -- the apron stays open.
        _edits.SetBox(new int3(bx - R, floor + 1, bz - R), new int3(bx - R, floor + 6, bz + R), Materials.Stone);
        _edits.SetBox(new int3(bx + R, floor + 1, bz - R), new int3(bx + R, floor + 6, bz + R), Materials.Stone);
        _edits.SetBox(new int3(bx - R, floor + 1, bz - R), new int3(bx + R, floor + 6, bz - R), Materials.Stone);
        _edits.SetBox(new int3(bx - R, floor + 1, bz + R), new int3(bx + R, floor + 6, bz + R), Materials.Stone);
        // Water inside.
        _edits.SetBox(new int3(bx - R + 1, floor + 1, bz - R + 1),
                      new int3(bx + R - 1, floor + 4, bz + R - 1), Materials.Water);

        for (int i = 0; i < 180; i++) { FluidTick(); yield return null; }

        int InBasin()
        {
            int n = 0;
            for (int z = bz - R; z <= bz + R; z++)
            for (int y = floor; y <= floor + 8; y++)
            for (int x = bx - R; x <= bx + R; x++)
                if (Store.GetVoxel(new int3(x, y, z)) == Materials.Water) n++;
            return n;
        }
        int InApron()
        {
            int n = 0;
            for (int z = bz - R; z <= bz + R; z++)
            for (int y = floor; y <= floor + 8; y++)
            for (int x = bx + R + 1; x <= bx + APRON; x++)
                if (Store.GetVoxel(new int3(x, y, z)) == Materials.Water) n++;
            return n;
        }

        int basinBefore = InBasin(), apronBefore = InApron();
        int totalBefore = CountInRegion(Materials.Water);
        long appliedBefore = _applied;
        long wakesBefore = _fluid.WakeRequestsQueuedTotal;

        L($"  settled: {basinBefore} water in the basin, {apronBefore} on the apron outside it");
        Check(basinBefore > 0, "the basin holds water before the breach");
        Check(apronBefore == 0, "and the apron outside it is dry");

        yield return Shot("step5_basin_before_breach",
            new float3((bx - R - 6) * 0.1f, (floor + 16) * 0.1f, (bz - R - 6) * 0.1f),
            new float3(bx * 0.1f, (floor + 2) * 0.1f, bz * 0.1f));

        // THE BREACH: one EditService call, which must both cut the hole and
        // wake the fluid beside it.
        _edits.ResetCounters();
        int breached = _edits.SetBox(new int3(bx + R, floor + 1, bz - 2),
                                     new int3(bx + R, floor + 3, bz + 2), Materials.Air);

        Check(breached > 0, $"the wall was breached ({breached} voxels removed)");
        Check(_fluid.WakeRequestsQueuedTotal > wakesBefore,
            $"the edit QUEUED WAKES into the fluid pool " +
            $"({_fluid.WakeRequestsQueuedTotal - wakesBefore} requests) -- this is §8.3's " +
            "'scan 26-neighborhood -> wake slots (§7.6)' actually firing");

        for (int i = 0; i < 400; i++) { FluidTick(); yield return null; }

        int basinAfter = InBasin(), apronAfter = InApron();
        int totalAfter = CountInRegion(Materials.Water);
        long appliedDuring = _applied - appliedBefore;

        L($"  after: {basinAfter} in the basin (was {basinBefore}), {apronAfter} on the apron");
        L($"  fluid ops applied: {appliedDuring}; total water in arena {totalBefore} -> {totalAfter}");

        Check(appliedDuring > 0, $"the CA applied ops after the breach ({appliedDuring})");
        Check(apronAfter > 0,
            $"WATER ACTUALLY DRAINED THROUGH THE BREACH: {apronAfter} voxels are now on the " +
            "apron that was dry before. Ops alone would not prove this -- water sloshing " +
            "inside a sealed basin also applies ops.");
        Check(basinAfter < basinBefore,
            $"and the basin lost water ({basinBefore} -> {basinAfter})");
        Check(totalAfter >= totalBefore - 2,
            $"with no mass lost on the way out ({totalBefore} -> {totalAfter})");

        yield return Shot("step5_basin_after_breach",
            new float3((bx - R - 6) * 0.1f, (floor + 16) * 0.1f, (bz - R - 6) * 0.1f),
            new float3((bx + R) * 0.1f, (floor + 2) * 0.1f, bz * 0.1f));
        L("");
    }

    // =====================================================================
    // STEP 6 -- §8.3's "prefab"
    // =====================================================================

    private IEnumerator Step6_PrefabPlacement()
    {
        _phase = "step6 prefab";
        L("STEP 6 -- place a prefab with a non-box silhouette");

        int bx = _regionOrigin.x + 8, by = _regionOrigin.y + 20, bz = _regionOrigin.z + 8;
        _edits.SetBox(new int3(bx - 1, by - 1, bz - 1), new int3(bx + 5, by + 5, bz + 5), Materials.Air);

        // A 3x3x1 plus-sign: corners are Air and must be skipped.
        int3 dims = new int3(3, 3, 1);
        byte A = Materials.Air, S = Materials.Sandstone;
        var mats = new byte[]
        {
            A, S, A,
            S, S, S,
            A, S, A,
        };

        _edits.ResetCounters();
        int placed = _edits.PlacePrefab(new int3(bx, by, bz), dims, mats);

        Check(placed == 5, $"five of the nine cells are written ({placed})");
        Check(Store.GetVoxel(new int3(bx + 1, by + 1, bz)) == Materials.Sandstone, "the centre is filled");
        Check(Store.GetVoxel(new int3(bx, by, bz)) == Materials.Air,
            "and a corner marked Air is left alone, so the silhouette survives");
        Check(_edits.VoxelsWritten == 5, $"exactly five writes ({_edits.VoxelsWritten})");

        yield return Shot("step6_prefab",
            new float3((bx - 3) * 0.1f, (by + 2) * 0.1f, (bz - 4) * 0.1f),
            new float3((bx + 1) * 0.1f, (by + 1) * 0.1f, bz * 0.1f));
        L("");
    }
}
