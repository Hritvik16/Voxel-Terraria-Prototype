// ==========================================
// Assets/Game/FluidStaggerRig.cs
//
// WHY FLUID POURED ACROSS A WIDE AREA STARTS MOVING IN STAGES.
//
// DIAGNOSIS ONLY. This rig changes nothing and tunes nothing. It exists to
// answer which of two mechanisms produces the staggering, with frame numbers
// rather than an impression, so the actual decision can be made by someone
// else.
//
// -------------------------------------------------------------------------
// THE TWO CANDIDATES
// -------------------------------------------------------------------------
//   A. TILE ACQUISITION TIMING. Fluid only simulates inside a RESIDENT tile,
//      and tiles are acquired by FluidTileResidency.Refresh, which every
//      caller runs on a 20-frame cadence. Fluid placed one frame after a
//      refresh waits up to 19 frames for a tile before it can move at all.
//
//   B. APPLY-RATE THROTTLING. §8.5's frame budget applies at most
//      MaxOpsAppliedPerFrame (4096) op-list entries per frame and carries the
//      rest forward, and CanIssue refuses a new CA tick while a batch is
//      still draining. A wide pour produces far more than 4096 moves in its
//      first ticks, so motion could be rationed rather than delayed.
//
// They predict DIFFERENT signatures, which is what makes this separable:
//   A predicts first-motion clustered on multiples of the refresh cadence,
//     and the gap SHRINKING toward zero as the cadence approaches 1.
//   B predicts first-motion spread smoothly in proportion to how many ops
//     are queued, and the gap shrinking as the budget rises.
//
// DELIBERATELY SMALL. A chaos-ladder-scale pour would trigger the tile-pool
// cap and the MAX_ACTIVE_FLUID clamp as well, and three overlapping effects
// cannot be told apart. This pours one modest column per tile across a 5x5
// tile footprint -- big enough to span tiles, small enough that nothing else
// binds.
//
// KNOBS, for the two variants:
//   -refreshevery N     tile residency refresh cadence (default 20)
//   -applybudget N      FluidOpListReadback.MaxOpsAppliedPerFrame (default 4096)
// ==========================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Memory;
using VoxelEngine.Mirror;
using VoxelEngine.Simulation;
using VoxelEngine.Streaming;
using VoxelEngine.WorldGen;

public class FluidStaggerRig : MonoBehaviour
{
    [SerializeField] private ComputeShader _fluidCA;
    [SerializeField] private int _tilePoolCap = 512;
    [SerializeField] private int _slotCapacity = 65536;
    [SerializeField] private int _maxOpsPerFrame = 65536;
    [SerializeField] private string _outputRootFolderName = "FluidStagger";

    /// 5x5 tiles of footprint. Tiles are 32 voxels, so 160x160 voxels.
    private const int TilesPerSide = 5;
    private const int Frames = 600;

    private static ChunkStore Store => Phase4Bootstrapper.Store;
    private static TerrainClipmap Clip => Phase4Bootstrapper.Clipmap;

    private EditService _edits;
    private FluidGpuSimulation _fluid;
    private FluidOpListReadback _readback;
    private FluidTileMap _tiles;

    private readonly StringBuilder _log = new StringBuilder();
    private string _outDir;
    private int _frame;

    private sealed class TileRec
    {
        public int3 Coord;
        public int PlacedFrame = -1;
        public int AcquiredFrame = -1;
        public int FirstMotionFrame = -1;
        public int Writes;
    }
    private readonly Dictionary<int3, TileRec> _recs = new Dictionary<int3, TileRec>();
    private readonly List<int> _backlog = new List<int>();
    private readonly List<int> _liveSlots = new List<int>();

    private void L(string s) { _log.AppendLine(s); Debug.Log("[stagger] " + s); }

    private static int ArgInt(string flag, int dflt)
    {
        string[] a = Environment.GetCommandLineArgs();
        for (int i = 0; i < a.Length - 1; i++)
            if (string.Equals(a[i], flag, StringComparison.OrdinalIgnoreCase))
                return int.Parse(a[i + 1], CultureInfo.InvariantCulture);
        return dflt;
    }

    IEnumerator Start()
    {
        int refreshEvery = ArgInt("-refreshevery", 20);
        int applyBudget = ArgInt("-applybudget", FluidOpListReadback.MaxOpsAppliedPerFrameDefault);
        FluidOpListReadback.MaxOpsAppliedPerFrame = applyBudget;

        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;

        float t0 = Time.realtimeSinceStartup;
        while (Store == null && Time.realtimeSinceStartup - t0 < 240f) yield return null;

        _outDir = Path.Combine(Application.persistentDataPath, _outputRootFolderName,
                               DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(_outDir);

        L("=== FLUID STAGGER DIAGNOSIS (changes nothing, tunes nothing) ===");
        L(DateTime.Now.ToString("u", CultureInfo.InvariantCulture));
        L($"VARIANT: refreshEvery={refreshEvery}  applyBudget={applyBudget}");
        L("");

        if (Store == null) { L("FATAL: world never booted"); yield return Write(); yield break; }
        for (int i = 0; i < 150; i++) yield return null;

        Camera cam = Camera.main;
        int3 camVox = CoordMath.WorldToVoxel(new float3(cam.transform.position.x,
                                                        cam.transform.position.y,
                                                        cam.transform.position.z));
        int surface = SurfaceY(camVox.x, camVox.z);
        if (surface < 1) { L("FATAL: no surface"); yield return Write(); yield break; }

        _edits = new EditService();
        _edits.AttachWorld(Store, Store, Store, Clip);
        _tiles = new FluidTileMap(ChunkFluidMask.TILE_EDGE, new int3(128, 128, 128), _tilePoolCap);
        _fluid = new FluidGpuSimulation(_fluidCA, new int3(64, 64, 64), _slotCapacity,
                                        _maxOpsPerFrame, _tiles)
        {
            RegionOriginVoxels = int3.zero,
            PlayerVoxel = camVox,
            ActiveRadiusVoxels = EngineConfig.FLUID_ACTIVE_RADIUS_VOXELS,
            SleepRadiusVoxels = FluidActiveRegion.SleepRadiusFor(EngineConfig.FLUID_ACTIVE_RADIUS_VOXELS),
        };
        _readback = new FluidOpListReadback(_fluid, Store)
        {
            OnVoxelApplied = v =>
            {
                Clip.MarkDirty(CoordMath.VoxelToChunk(v));
                int3 tc = TileOf(v);
                if (_recs.TryGetValue(tc, out TileRec r))
                {
                    r.Writes++;
                    if (r.FirstMotionFrame < 0) r.FirstMotionFrame = _frame;
                }
            },
        };
        _edits.AttachFluidSimulation(_fluid, Store);

        // ---- Establish residency BEFORE placing, so acquisition timing is
        // measured from the pour and not from the world booting. ----
        // NOTE: deliberately NOT refreshing here. Pre-acquiring the tiles
        // before the pour is exactly what made the first version measure zero.
        yield return null;

        // ---- ONE POUR, ONE FRAME, ACROSS 5x5 TILES ----
        // All of it placed in the SAME frame, so every later difference is the
        // engine's, not the scenario's.
        //
        // POUR PHASE MATTERS, AND A FIRST VERSION OF THIS RIG GOT IT WRONG.
        // Pouring immediately before the loop meant frame 0's refresh (0 % 20
        // == 0) acquired every tile instantly, so the measured acquisition
        // spread was 0 and hypothesis A looked disproven -- by the scenario,
        // not by the engine. In real use a pour lands at an arbitrary point in
        // the cadence and waits 0-19 frames. -pourat sets that phase; the
        // default of 1 is the WORST case (a refresh just missed), which is the
        // one worth measuring.
        int e = ChunkFluidMask.TILE_EDGE;
        int3 baseTile = TileOf(camVox) - new int3(TilesPerSide / 2, 0, TilesPerSide / 2);
        int pourAt = ArgInt("-pourat", 1);
        int placed = 0;
        System.Action Pour = () => {
        for (int tx = 0; tx < TilesPerSide; tx++)
            for (int tz = 0; tz < TilesPerSide; tz++)
            {
                int3 tc = baseTile + new int3(tx, 0, tz);
                int cx = tc.x * e + e / 2, cz = tc.z * e + e / 2;
                int sy = SurfaceY(cx, cz);
                if (sy < 1) continue;
                var lo = new int3(cx - 3, sy + 10, cz - 3);
                int n = _edits.SetBox(lo, lo + new int3(6, 7, 6), Materials.Water);
                if (n <= 0) continue;
                placed += n;
                // Record against the tile the fluid ACTUALLY landed in.
                int3 rec = TileOf(new int3(cx, sy + 10, cz));
                if (!_recs.ContainsKey(rec))
                    _recs[rec] = new TileRec { Coord = rec, PlacedFrame = _frame };
            }
        };
        L($"pour scheduled for loop frame {pourAt} (refresh cadence {refreshEvery})");
        L("");

        // ---- OBSERVE ----
        for (_frame = 0; _frame < Frames; _frame++)
        {
            if (_frame == pourAt)
            {
                Pour();
                L($"poured {placed} water voxels across {_recs.Count} tiles on frame {_frame}");
            }
            if ((_frame % refreshEvery) == 0)
            {
                _fluid.UpdatePlayerPosition(camVox);
                FluidTileResidency.Refresh(Store, _tiles, camVox,
                                           _fluid.ActiveRadiusVoxels, _fluid.SleepRadiusVoxels);
            }
            if (_frame >= pourAt)
                foreach (var r in _recs.Values)
                    if (r.AcquiredFrame < 0 && _tiles.TryGetSlot(r.Coord) != FluidTileMap.NO_TILE)
                        r.AcquiredFrame = _frame;

            if (_readback.CanIssue) { _fluid.Tick(Clip); _readback.IssueReadback(0); }
            _readback.PumpAndApply();

            _backlog.Add(_readback.PendingOps);
            _fluid.ReadSlotCounters(out uint hi, out uint _);
            _liveSlots.Add((int)hi);
            yield return null;
        }

        Report(refreshEvery, applyBudget);
        yield return Write();
    }

    private void Report(int refreshEvery, int applyBudget)
    {
        var acq = new List<int>(); var mot = new List<int>(); var gap = new List<int>();
        L("--- PER TILE ---");
        L($"  {"tile",-18} {"placed",7} {"acquired",9} {"1st motion",11} {"acq gap",8} {"mot gap",8} {"writes",7}");
        var ordered = new List<TileRec>(_recs.Values);
        ordered.Sort((a, b) => a.FirstMotionFrame.CompareTo(b.FirstMotionFrame));
        foreach (var r in ordered)
        {
            L($"  {r.Coord.ToString(),-18} {r.PlacedFrame,7} {r.AcquiredFrame,9} {r.FirstMotionFrame,11} " +
              $"{(r.AcquiredFrame - r.PlacedFrame),8} {(r.FirstMotionFrame - r.PlacedFrame),8} {r.Writes,7}");
            if (r.AcquiredFrame >= 0) acq.Add(r.AcquiredFrame);
            if (r.FirstMotionFrame >= 0) { mot.Add(r.FirstMotionFrame); gap.Add(r.FirstMotionFrame - r.AcquiredFrame); }
        }
        L("");
        L("--- SPREAD (this is the staggering, in frames) ---");
        L($"  tiles tracked                 {_recs.Count}");
        L($"  never acquired                {_recs.Count - acq.Count}");
        L($"  never moved                   {_recs.Count - mot.Count}");
        if (acq.Count > 0)
        {
            acq.Sort();
            L($"  ACQUIRED   first {acq[0]}  last {acq[acq.Count - 1]}  SPREAD {acq[acq.Count - 1] - acq[0]} frames " +
              $"({(acq[acq.Count - 1] - acq[0]) / 60.0:F2} s)");
        }
        if (mot.Count > 0)
        {
            mot.Sort();
            L($"  1st MOTION first {mot[0]}  last {mot[mot.Count - 1]}  SPREAD {mot[mot.Count - 1] - mot[0]} frames " +
              $"({(mot[mot.Count - 1] - mot[0]) / 60.0:F2} s)   <-- THE VISIBLE EFFECT");
            gap.Sort();
            L($"  motion-after-acquire gap: min {gap[0]}  median {gap[gap.Count / 2]}  max {gap[gap.Count - 1]} frames");
        }
        L("");
        L("--- APPLY BACKLOG (carry-forward queue depth) ---");
        int maxB = 0; long sumB = 0; int framesWithBacklog = 0;
        foreach (int b in _backlog) { if (b > maxB) maxB = b; sumB += b; if (b > 0) framesWithBacklog++; }
        L($"  budget {applyBudget} ops/frame   max backlog {maxB}   mean {sumB / Math.Max(1, _backlog.Count)}   " +
          $"frames with any backlog {framesWithBacklog} of {_backlog.Count}");
        int peakLive = 0; foreach (int v in _liveSlots) if (v > peakLive) peakLive = v;
        L($"  peak live slots {peakLive} / {_fluid.SlotCapacity}");
        L($"  tiles resident {_tiles.ResidentTiles} / {_tiles.TileCapacity}, exhaustions {_tiles.PoolExhaustionsTotal}");
        L("");
        L("--- HOW TO READ THIS ---");
        L($"  refreshEvery was {refreshEvery}. If ACQUIRED spread is ~{refreshEvery} frames and");
        L("  collapses when refreshEvery=1, tile acquisition is the cause.");
        L("  If ACQUIRED spread is ~0 but 1st MOTION spread is large and shrinks");
        L("  when the apply budget rises, apply throttling is the cause.");
    }

    private static int3 TileOf(int3 v)
    {
        int e = ChunkFluidMask.TILE_EDGE;
        return new int3(
            (v.x >= 0 ? v.x : v.x - (e - 1)) / e,
            (v.y >= 0 ? v.y : v.y - (e - 1)) / e,
            (v.z >= 0 ? v.z : v.z - (e - 1)) / e);
    }

    private int SurfaceY(int x, int z)
    {
        for (int y = WorldGenConstants.MAX_TERRAIN_HEIGHT + 2; y >= 1; y--)
        {
            byte m = Store.GetVoxel(new int3(x, y, z));
            if (m != Materials.Air && !MaterialRules.IsFluidMaterial(m)) return y;
        }
        return -1;
    }

    private IEnumerator Write()
    {
        File.WriteAllText(Path.Combine(_outDir, "fluid_stagger_report.txt"), _log.ToString());
        Debug.Log("[stagger] report -> " + _outDir);
        yield return null;
        Application.Quit(0);
    }

    private void OnDestroy() { _readback?.Dispose(); _fluid?.Dispose(); }
}
