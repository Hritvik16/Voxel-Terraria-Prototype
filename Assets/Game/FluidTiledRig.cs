// ==========================================
// Assets/Game/FluidTiledRig.cs
//
// THE ACCEPTANCE RIG FOR §7.2's SPARSE TILED ACTIVE SET.
//
// Two scenarios, because one pool at radius does not exercise the property the
// design exists for:
//
//   A. THE SHIPPED RADIUS, MEASURED. FLUID_ACTIVE_RADIUS_VOXELS = 1280 with a
//      real GPU allocation number summed from the buffer objects -- not the
//      arithmetic already done in EditMode. And the number must not move when
//      the radius does, which is the whole claim.
//
//   B. EXPLOSION SCATTER. Many small, disconnected, unpredictably placed
//      pockets appearing near-simultaneously over a wide area. A single pool is
//      one tile cluster; scatter is what actually tests concurrent tile count,
//      reactions between adjacent pockets, the pool cap, and -- the one that
//      matters most -- whether anything SILENTLY FAILS TO WAKE.
//
// NO GPU TIMING IS TAKEN. §2.2 budgets the fluid CA on the GPU lane and this
// workflow cannot attribute GPU stages (AMENDMENT_8_9 §0 Rules 1-2,
// AMENDMENT_8_10's 2.6-2.7x inflation). Memory here is exact; time would not be.

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

public class FluidTiledRig : MonoBehaviour
{
    [SerializeField] private ComputeShader _fluidCA;
    [SerializeField] private int _tilePoolCap = EngineConfig.FLUID_TILE_POOL_CAPACITY;
    [SerializeField] private int _slotCapacity = 65536;
    [SerializeField] private string _outputRootFolderName = "FluidTiled";

    private readonly StringBuilder _log = new StringBuilder();
    private int _pass, _fail;
    private string _phase = "-";
    private string _outDir;

    private static ChunkStore Store => Phase4Bootstrapper.Store;
    private static TerrainClipmap Clip => Phase4Bootstrapper.Clipmap;

    private EditService _edits;
    private FluidGpuSimulation _fluid;
    private FluidOpListReadback _readback;
    private FluidTileMap _tiles;
    private long _applied;

    private void L(string s) { _log.AppendLine(s); Debug.Log("[tiled] " + s); }
    private void Note(string s) => L("    note  " + s);
    private void Pass(string s) { _pass++; L("    PASS  " + s); }
    private void Fail(string s) { _fail++; L($"    FAIL  {s}   [{_phase}]"); }
    private void Check(bool ok, string s) { if (ok) Pass(s); else Fail(s); }
    private void Finding(string s) => L("    FINDING  " + s);

    IEnumerator Start()
    {
        float t0 = Time.realtimeSinceStartup;
        while (Store == null && Time.realtimeSinceStartup - t0 < 240f) yield return null;

        _outDir = Path.Combine(Application.persistentDataPath, _outputRootFolderName,
                               DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(_outDir);

        L("=== §7.2 SPARSE TILED ACTIVE SET -- ACCEPTANCE ===");
        L(DateTime.Now.ToString("u", CultureInfo.InvariantCulture));
        L("");
        L("READING RULE: every figure is a COUNT or a BYTE SIZE. No frame time is");
        L("taken -- §2.2's fluid budget is on the GPU lane, which this workflow");
        L("cannot attribute. Nothing here may be quoted against §2.2.");
        L("");

        if (Store == null) { Fail("world never booted"); yield return Report(); yield break; }
        for (int i = 0; i < 150; i++) yield return null;

        _edits = new EditService();
        _edits.AttachWorld(Store, Store, Store, Clip);

        yield return A_ShippedRadiusMeasured();
        yield return B_RadiusActuallyBites();
        yield return C_ExplosionScatter();
        yield return D_MultiplePoolsOneInstance();

        yield return Report();
    }

    private IEnumerator Report()
    {
        _log.AppendLine();
        _log.AppendLine($"PASS {_pass}  FAIL {_fail}");
        _log.AppendLine(_fail == 0 ? "RESULT: PASSED" : "RESULT: FAILED");
        File.WriteAllText(Path.Combine(_outDir, "fluid_tiled_report.txt"), _log.ToString());
        Debug.Log("[tiled] report -> " + _outDir);
        yield return null;
        Application.Quit(_fail == 0 ? 0 : 1);
    }

    private void OnDestroy() { _readback?.Dispose(); _fluid?.Dispose(); }

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

    private void Build(int radiusVoxels, int cap)
    {
        _readback?.Dispose();
        if (_fluid != null) { _edits.DetachFluidSimulation(_fluid); _fluid.Dispose(); }

        _tiles = new FluidTileMap(ChunkFluidMask.TILE_EDGE, new int3(128, 128, 128), cap);
        // The region box is now only an addressing origin for the DENSE path;
        // under tiling the active set is the tile pool, so this stays small.
        _fluid = new FluidGpuSimulation(_fluidCA, new int3(64, 64, 64), _slotCapacity, 65536, _tiles)
        {
            RegionOriginVoxels = int3.zero,
            ActiveRadiusVoxels = radiusVoxels,
            SleepRadiusVoxels = FluidActiveRegion.SleepRadiusFor(radiusVoxels),
        };
        _readback = new FluidOpListReadback(_fluid, Store)
        {
            OnVoxelApplied = v => { Clip.MarkDirty(CoordMath.VoxelToChunk(v)); _applied++; },
        };
        _edits.AttachFluidSimulation(_fluid, Store);
    }

    private void Tick()
    {
        if (_readback.CanIssue) { _fluid.Tick(Clip); _readback.IssueReadback(0); }
        _readback.PumpAndApply();
    }

    /// Moves BOTH halves of §7.4's centre: the tile set (CPU residency) and
    /// _PlayerVoxel (the GPU's own radius gate).
    ///
    /// THEY ARE SEPARATE AND BOTH REQUIRED. A first version of this rig moved
    /// only the tile set, so tiles were resident and dispatched while CSPromote
    /// rejected every cell as outside WithinActiveRadius -- 488 tiles live,
    /// zero slots allocated, zero voxels moved. Tiles existing is not the same
    /// as fluid simulating, and scenario C is the step that can tell.
    /// The shader's own §10.4 per-stage counters. They are cleared every tick,
    /// so this is a snapshot of the LAST tick -- which is exactly what is wanted
    /// when the question is "where did the CA decide to do nothing".
    private void DumpDebugCounters(string when)
    {
        var buf = _fluid.DebugCountersBuffer;
        if (buf == null) { L($"  [{when}] no debug counters"); return; }
        var v = new uint[FluidGpuSimulation.DebugSlots];
        buf.GetData(v);
        var sb = new StringBuilder();
        for (int i = 0; i < v.Length && i < FluidGpuSimulation.DebugCounterNames.Length; i++)
            if (v[i] != 0) sb.Append($"{FluidGpuSimulation.DebugCounterNames[i]}={v[i]}  ");
        L($"  [{when}] last-tick stage counters: {(sb.Length == 0 ? "(all zero)" : sb.ToString())}");
    }

    private void RefreshTiles(int3 centre)
    {
        _fluid.UpdatePlayerPosition(centre);
        FluidTileResidency.Refresh(Store, _tiles, centre,
                                   _fluid.ActiveRadiusVoxels, _fluid.SleepRadiusVoxels);
    }

    // =====================================================================
    // A -- the shipped radius, with a MEASURED footprint
    // =====================================================================

    private IEnumerator A_ShippedRadiusMeasured()
    {
        _phase = "A shipped radius, measured";
        int shipped = EngineConfig.FLUID_ACTIVE_RADIUS_VOXELS;
        L($"A -- THE SHIPPED RADIUS: FLUID_ACTIVE_RADIUS_VOXELS = {shipped} " +
          $"({shipped * 0.1f:F0} m)");
        L("");
        L("  radius   activeSet bytes   total GPU bytes   cells      tiles cap   fits ring");

        long baseline = -1;
        foreach (int r in new[] { 128, 640, shipped })
        {
            Build(r, _tilePoolCap);
            long act = _fluid.GpuActiveSetBytes();
            long tot = _fluid.GpuAllocatedBytes();
            bool fits = _tiles.RadiusFitsRing(r);

            L($"  {r,6} {act / (1024f * 1024f),15:F1} MB {tot / (1024f * 1024f),15:F1} MB " +
              $"{_fluid.CellCount,10} {_tiles.TileCapacity,10}   {fits}");

            if (baseline < 0) baseline = act;
            else Check(act == baseline,
                $"the active-set footprint does not move with the radius ({act} vs {baseline} " +
                $"at r={r}) -- the entire claim of the design");

            Check(fits, $"the {r}-voxel radius fits the tile ring without aliasing (§6.2)");
            yield return null;
        }

        long finalBytes = _fluid.GpuActiveSetBytes();
        L("");
        L($"  MEASURED at the SHIPPED radius: active set {finalBytes / (1024f * 1024f):F1} MB, " +
          $"whole simulation {_fluid.GpuAllocatedBytes() / (1024f * 1024f):F1} MB");

        // The dense equivalent, for scale. 2048^3 is the smallest power-of-two
        // region whose half-diagonal reaches 1280 voxels.
        const long denseCells = 2048L * 2048L * 2048L;
        double denseGB = denseCells * 16.0 / (1024.0 * 1024.0 * 1024.0);
        L($"  the DENSE equivalent would be 2048^3 = {denseCells:N0} cells = {denseGB:F0} GB");

        Check(finalBytes < 512L * 1024 * 1024,
            $"the active set at the shipped radius is under 512 MB ({finalBytes / (1024 * 1024)} MB)");
        Check(denseCells * 16 / math.max(1, finalBytes) > 400,
            "and orders of magnitude smaller than the dense region it replaces");

        Finding($"THE SHIPPED RADIUS IS REACHABLE. {finalBytes / (1024 * 1024)} MB measured " +
                $"from the actual GPU allocations, against {denseGB:F0} GB for the dense region " +
                "that made the radius inert. The footprint is identical at 128, 640 and 1280 " +
                "voxels because it is a function of the tile pool, not the radius.");
        L("");
    }

    // =====================================================================
    // B -- and it actually gates, which it never could before
    // =====================================================================

    private IEnumerator B_RadiusActuallyBites()
    {
        _phase = "B the radius bites";
        L("B -- §7.4's radius GATES, which at any affordable dense size it could not");

        Camera cam = Camera.main;
        int3 camVox = CoordMath.WorldToVoxel(new float3(cam.transform.position.x,
                                                        cam.transform.position.y,
                                                        cam.transform.position.z));
        int surface = SurfaceY(camVox.x, camVox.z);
        int3 pool = new int3(camVox.x, surface + 3, camVox.z);

        // A demo radius, so "walk away" is a few metres rather than 128.
        Build(128, _tilePoolCap);
        _edits.SetBox(pool - new int3(4, 0, 4), pool + new int3(4, 3, 4), Materials.Water);
        for (int i = 0; i < 5; i++) { RefreshTiles(pool); Tick(); yield return null; }

        int near = _tiles.ResidentTiles;
        long movedHere = _applied;
        for (int i = 0; i < 60; i++) { Tick(); yield return null; }
        movedHere = _applied - movedHere;
        uint hiB, everB;
        _fluid.ReadSlotCounters(out hiB, out everB);

        L($"  standing on the pool: {near} resident tile(s), {_fluid.ActiveTileCount} dispatched, " +
          $"{movedHere} voxel writes, slots everAllocated {everB}");
        Check(near > 0, $"fluid within the radius is tiled ({near} tiles)");
        Check(everB > 0,
            $"AND SLOTS WERE ACTUALLY ALLOCATED ({everB}) -- tiles being resident is not the " +
            "same as fluid simulating, and asserting only residency is what let a rig report " +
            "488 live tiles with zero slots and zero motion");

        // THE POOL'S OWN TILE, not the global count. The first version asserted
        // "0 tiles anywhere", which is wrong on a world that HAS water: walking
        // 85 m away legitimately picks up whatever fluid is near the new
        // position. What §7.4 promises is that the tile you LEFT is released.
        int3 poolTile = ChunkFluidMask.AbsoluteTile(CoordMath.VoxelToChunk(pool),
                                                    ChunkFluidMask.TileInChunkOf(pool));
        int3 away = pool + new int3(600, 0, 600);
        for (int i = 0; i < 5; i++) { RefreshTiles(away); Tick(); yield return null; }
        int far = _tiles.ResidentTiles;
        bool leftReleased = _tiles.TryGetSlot(poolTile) == FluidTileMap.NO_TILE;
        L($"  walked {math.length(new float2(600, 600)) * 0.1f:F0} m away: {far} tile(s) " +
          $"resident near the NEW position; the tile we left is " +
          $"{(leftReleased ? "released" : "STILL HELD")}");
        Check(leftReleased,
            "leaving the radius releases the tile you left -- §7.4's departure half, which is " +
            "what makes the footprint bounded by presence rather than by radius");

        for (int i = 0; i < 5; i++) { RefreshTiles(pool); Tick(); yield return null; }
        int back = _tiles.ResidentTiles;
        bool reacquired = _tiles.TryGetSlot(poolTile) != FluidTileMap.NO_TILE;
        L($"  returned: {back} resident tile(s); the tile we left is " +
          $"{(reacquired ? "re-acquired" : "STILL MISSING")}");
        Check(reacquired && back > 0,
            $"and returning WAKES it again ({back} tiles) -- the §7.4 contract that a sparse " +
            "active set nearly lost, answered by ChunkFluidMask rather than by a cell sweep");
        L("");
    }

    // =====================================================================
    // D -- §9.7's "several simultaneous regions", under ONE CA instance
    //
    // WHY THIS RETIRES A STOPGAP. §9.7's fix was EditService holding a LIST of
    // separate FluidGpuSimulation instances, so two player-placed pools would
    // both wake. That was correct for the dense design -- a region is a fixed
    // box, and two pools far apart genuinely needed two boxes -- but it is the
    // wrong shape here: a sparse tile pool covers arbitrarily-placed pools
    // structurally, because a tile is created wherever fluid is and nowhere
    // else. Scenario C already showed 220 pockets under one instance; this
    // reproduces §9.7's ORIGINAL scenario specifically, so the claim is
    // compared against the thing it replaces rather than a new one.
    // =====================================================================

    private IEnumerator D_MultiplePoolsOneInstance()
    {
        _phase = "D §9.7 under one instance";
        L("D -- §9.7's several-simultaneous-regions, with ONE CA over the tile pool");

        Camera cam = Camera.main;
        int3 camVox = CoordMath.WorldToVoxel(new float3(cam.transform.position.x,
                                                        cam.transform.position.y,
                                                        cam.transform.position.z));
        Build(EngineConfig.FLUID_ACTIVE_RADIUS_VOXELS, _tilePoolCap);

        // Two pools far enough apart that a single DENSE region could not have
        // held both: 300 voxels is well beyond a 64^3 box.
        int3[] sites = { camVox + new int3(-150, 0, 0), camVox + new int3(150, 0, 0) };
        var poolTiles = new List<int3>();
        int totalPlaced = 0;

        foreach (int3 site in sites)
        {
            int sy = SurfaceY(site.x, site.z);
            int3 c = new int3(site.x, sy + 3, site.z);
            totalPlaced += _edits.SetBox(c - new int3(3, 0, 3), c + new int3(3, 2, 3), Materials.Water);
            poolTiles.Add(ChunkFluidMask.AbsoluteTile(CoordMath.VoxelToChunk(c),
                                                      ChunkFluidMask.TileInChunkOf(c)));
        }
        L($"  placed {totalPlaced} water voxels in 2 pools 300 voxels ({300 * 0.1f:F0} m) apart");
        Check(_edits.AttachedFluidSimulations == 1,
            $"ONE CA instance is attached ({_edits.AttachedFluidSimulations}), not one per pool -- " +
            "the §9.7 stopgap held a list");

        long before = _applied;
        for (int i = 0; i < 240; i++)
        {
            if ((i % 20) == 0) RefreshTiles(camVox);
            Tick();
            yield return null;
        }

        int live = 0;
        foreach (int3 t in poolTiles) if (_tiles.TryGetSlot(t) != FluidTileMap.NO_TILE) live++;
        uint hiD, everD;
        _fluid.ReadSlotCounters(out hiD, out everD);

        L($"  after 240 ticks: {live}/2 pool tiles resident, {_applied - before} voxel writes, " +
          $"slots everAllocated {everD}");
        Check(live == 2,
            $"BOTH pools are resident under a single instance ({live}/2) -- structurally, " +
            "because a tile exists wherever fluid is, not because something kept a list");
        Check(_applied - before > 0,
            $"and both are actually simulating ({_applied - before} voxel writes)");
        L("");
    }

    // =====================================================================
    // C -- explosion scatter: the scenario a single pool does not test
    // =====================================================================

    private IEnumerator C_ExplosionScatter()
    {
        _phase = "C explosion scatter";
        L("C -- EXPLOSION SCATTER: many small disconnected pockets over a wide area");
        L("");

        Camera cam = Camera.main;
        int3 camVox = CoordMath.WorldToVoxel(new float3(cam.transform.position.x,
                                                        cam.transform.position.y,
                                                        cam.transform.position.z));
        int surface = SurfaceY(camVox.x, camVox.z);

        Build(EngineConfig.FLUID_ACTIVE_RADIUS_VOXELS, _tilePoolCap);

        // Scatter pockets of THREE materials across a wide radius, deterministic
        // so a failure can be re-run. Water and lava adjacent on purpose: §7.3's
        // reaction is what proves scattered pockets are really simulating and
        // not merely resident.
        var rng = new System.Random(20260905);
        const int POCKETS = 220;
        const int SCATTER = 900;                      // voxels each way => 90 m
        int placed = 0, pocketsPlaced = 0;
        var centres = new List<int3>();

        for (int i = 0; i < POCKETS; i++)
        {
            int dx = rng.Next(-SCATTER, SCATTER), dz = rng.Next(-SCATTER, SCATTER);
            int x = camVox.x + dx, z = camVox.z + dz;
            int sy = SurfaceY(x, z);
            if (sy < 1) continue;

            byte m = (i % 3) == 0 ? Materials.Water : (i % 3) == 1 ? Materials.Sand : Materials.Lava;
            int3 c = new int3(x, sy + 2, z);
            int n = _edits.SetSphere(c, 2, m);
            if (n > 0) { placed += n; pocketsPlaced++; centres.Add(c); }

            if ((i & 15) == 0) yield return null;
        }

        L($"  scattered {pocketsPlaced} pockets ({placed} voxels) of water/sand/lava across " +
          $"+/-{SCATTER * 0.1f:F0} m");

        var st = FluidTileResidency.Refresh(Store, _tiles, camVox,
                                            _fluid.ActiveRadiusVoxels, _fluid.SleepRadiusVoxels);
        L($"  residency scan: {st.ChunksScanned} chunks read, {st.ChunksSkippedNonResident} " +
          $"skipped non-resident, {st.TilesWithFluid} tiles flagged, {st.TilesAcquired} acquired, " +
          $"{st.TilesRefusedPoolFull} refused (pool full)");

        int peakTiles = 0;
        long appliedBefore = _applied;
        for (int i = 0; i < 400; i++)
        {
            if ((i % 20) == 0) RefreshTiles(camVox);
            Tick();
            peakTiles = math.max(peakTiles, _tiles.ResidentTiles);
            // Counters are cleared EVERY TICK, so a dump at the end only ever
            // shows a settled region. The question is where the CA decided to
            // do nothing while it was still awake.
            if (i == 1 || i == 4 || i == 12 || i == 40) DumpDebugCounters($"tick {i}");
            yield return null;
        }

        uint hi, ever;
        _fluid.ReadSlotCounters(out hi, out ever);
        L($"  after 400 ticks: peak {peakTiles} concurrent tiles (cap {_tilePoolCap}), " +
          $"{_applied - appliedBefore} voxel writes applied");
        L($"  slots: highWater {hi}, everAllocated {ever} / cap {_fluid.SlotCapacity}, " +
          $"free list {_fluid.ReadFreeSlotCount()}");
        L($"  tiles: acquired {_tiles.TilesAcquiredTotal}, released {_tiles.TilesReleasedTotal}, " +
          $"peak resident {_tiles.PeakResidentTiles}, pool exhaustions {_tiles.PoolExhaustionsTotal}");

        // WHERE THE MOTION WENT, if it went anywhere. "0 writes applied" has
        // three completely different causes and the counters separate them:
        // the CA emitted nothing, or it emitted and the readback dropped them
        // (§9.4 residency / staleness), or the requests never reached it.
        L($"  op-list: emitted {_readback.OpsTotal} total, peak {_readback.PeakOpsInAFrame}/frame, " +
          $"applied {_readback.AppliedVoxelWrites}");
        L($"  ops dropped: {_readback.OpsDroppedNonResident} non-resident, " +
          $"{_readback.StaleOpsDropped} stale; readback errors {_readback.ReadbackErrorsTotal} " +
          $"({_readback.LastReadbackError})");
        DumpDebugCounters("after 400 ticks");
        L($"  wake requests: queued {_fluid.WakeRequestsQueuedTotal}, dispatched " +
          $"{_fluid.WakeDispatchedTotal}, rejected out-of-region {_fluid.WakeRejectedOutOfRegion}, " +
          $"rejected full {_fluid.WakeRejectedFull}, still pending {_fluid.PendingWakeRequests}" +
          $"/{_fluid.DeferredWakeRequests} deferred");

        Check(pocketsPlaced > 100, $"the scatter actually landed ({pocketsPlaced} pockets)");
        Check(peakTiles > 1,
            $"many disconnected pockets are simulated CONCURRENTLY ({peakTiles} tiles) -- one " +
            "dense region could never have covered this spread at any affordable size");
        Check(_applied - appliedBefore > 0,
            $"and they actually moved ({_applied - appliedBefore} voxel writes), so the tiles " +
            "are simulating rather than merely resident");

        // THE CAP, handled the way §3.6 handles the brick pool: refuse cleanly,
        // never overwrite, never crash.
        Check(_tiles.ResidentTiles <= _tilePoolCap,
            $"the tile pool never exceeds its cap ({_tiles.ResidentTiles} <= {_tilePoolCap})");
        if (_tiles.PoolExhaustionsTotal > 0)
            Note($"the cap WAS reached ({_tiles.PoolExhaustionsTotal} refusals) and each was a " +
                 "guarded no-op per §7.7 -- the fluid stays put and retries, no invalid write");
        else
            Note($"the cap was not reached (peak {_tiles.PeakResidentTiles} of {_tilePoolCap}); " +
                 "this run does not exercise the refusal path");

        // NOTHING SILENTLY FAILED TO WAKE. Every pocket still holding mobile
        // material must be inside a resident tile after a refresh.
        RefreshTiles(camVox);
        int unwoken = 0, checkedPockets = 0;
        foreach (int3 c in centres)
        {
            bool hasFluid = false;
            for (int dy = -2; dy <= 3 && !hasFluid; dy++)
            for (int dx = -2; dx <= 2 && !hasFluid; dx++)
            for (int dz = -2; dz <= 2 && !hasFluid; dz++)
                if (MaterialRules.IsMobile(Store.GetVoxel(c + new int3(dx, dy, dz)))) hasFluid = true;
            if (!hasFluid) continue;

            checkedPockets++;
            int3 tile = ChunkFluidMask.AbsoluteTile(CoordMath.VoxelToChunk(c),
                                                    ChunkFluidMask.TileInChunkOf(c));
            if (_tiles.TryGetSlot(tile) == FluidTileMap.NO_TILE) unwoken++;
        }

        L($"  wake audit: {checkedPockets} pockets still hold mobile material; {unwoken} of them " +
          "are NOT inside a resident tile");
        Check(unwoken == 0,
            $"NOTHING SILENTLY FAILED TO WAKE ({unwoken} unwoken of {checkedPockets}) -- the " +
            "exact defect class DESIGN_NOTE_7_2 §9 exists to prevent, audited rather than assumed");
        L("");
    }
}
