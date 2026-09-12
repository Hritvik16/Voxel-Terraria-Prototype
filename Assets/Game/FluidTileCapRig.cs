// ==========================================
// Assets/Game/FluidTileCapRig.cs
//
// THE FROZEN-CUBE REPRO, ON PURPOSE, AND THE TILE-POOL CAP LADDER.
//
// The late-game siege left one visible artifact: blocks of fluid suspended in
// mid-air that never settle. The explanation offered was the 512-tile pool
// cap -- §7.7's guarded refusal, made visible. That explanation was inferred
// from a counter (294,588 exhaustions) and some arithmetic, NOT demonstrated.
// This rig demonstrates it, or fails to.
//
// -------------------------------------------------------------------------
// WHY THE SATURATION FIELD IS THIN AND WIDE, NOT A BIG POOL
// -------------------------------------------------------------------------
// A tile is 32^3 = 32,768 cells but costs ONE pool slot however little fluid
// it holds. MAX_ACTIVE_FLUID is 750,000 live voxels; 512 tiles could hold
// 16.7 MILLION. So the two caps are reached by OPPOSITE shapes:
//
//   dense and compact -> slots run out first, tiles are nearly empty
//   thin and wide     -> TILES run out first, slots are barely touched
//
// The siege's pour spreads +/-260 voxels, which is the thin-and-wide shape.
// To isolate the TILE cap this rig therefore places a sparse LATTICE of small
// pockets on a 32-voxel pitch -- one pocket per tile, 64 voxels each -- so
// tile demand is (2*spread/32+1)^2 * layers while slot demand stays around
// 100K. If the artifact reproduces here, the slot cap cannot be the cause,
// because the slot cap is never approached.
//
// -------------------------------------------------------------------------
// THE MARKER CUBE IS THE OBSERVABLE
// -------------------------------------------------------------------------
// A census of "how much fluid froze" over the whole field is unreadable. So
// after the lattice has taken the pool, ONE obvious 12^3 cube of water is
// placed in mid-air OUTSIDE the lattice, on ground, with nothing under it.
// Then nothing else is placed and the sim runs quiet.
//
//   pool exhausted -> the cube's tiles are refused, it never moves, and its
//                     original box still holds ~100% of its voxels
//   pool available -> it falls, and its original box empties
//
// The cube sits at the FAR corner (+x, +z) deliberately: Refresh scans chunks
// from lo to hi, so the last-scanned tiles are the ones that lose the race
// for a full pool. That makes the outcome deterministic rather than a
// coin-flip on iteration order.
//
// -------------------------------------------------------------------------
// WHAT IT MEASURES FOR THE CAP LADDER
// -------------------------------------------------------------------------
//   -tilecap N   the pool size under test (512 / 1024 / 2048)
//   -slotcap N   so Step 3's slot question can use the same harness
//
// Per run: peak tile DEMAND (post-radius-gate, the number a pool would have
// to be to hold everything), refusals, cube retention, GPU bytes both for the
// active set and for everything the sim allocates, and cooled frame time over
// a fixed hold phase so caps are compared on identical work.
//
// MEASUREMENT: wall clock only. No gpuFrameTime against a budget (Amdt 8.10),
// no Xcode (8.9 Rule 1). Cooldowns belong to the runner.
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

public class FluidTileCapRig : MonoBehaviour
{
    [SerializeField] private ComputeShader _fluidCA;
    [SerializeField] private PlayerController _player;
    [SerializeField] private int _tilePoolCap = EngineConfig.FLUID_TILE_POOL_CAPACITY;
    [SerializeField] private int _slotCapacity = EngineConfig.MAX_ACTIVE_FLUID;
    [SerializeField] private int _maxOpsPerFrame = 65536;
    [SerializeField] private string _outputRootFolderName = "FluidTileCap";

    private const float Dt = 1f / 60f;
    /// Pocket pitch == tile edge, so one pocket lands in one tile.
    private const int Pitch = 32;
    private const int PocketEdge = 4;       // 4^3 = 64 voxels per tile, ~0.2% full
    private const int CubeEdge = 12;        // the marker, 1,728 voxels
    private const int CubeHeight = 34;      // voxels of clear air under it

    private static ChunkStore Store => Phase4Bootstrapper.Store;
    private static TerrainClipmap Clip => Phase4Bootstrapper.Clipmap;

    private EditService _edits;
    private FluidGpuSimulation _fluid;
    private FluidOpListReadback _readback;
    private FluidTileMap _tiles;
    private FrameGapProbe _gap;

    private readonly StringBuilder _log = new StringBuilder();
    private int _pass, _fail, _shot;
    private string _outDir;
    private int3 _centre; private int _surfaceY;
    private int3 _cubeLo, _cubeHi;
    private int _cubeGroundY;
    private long _placed, _applied;
    private readonly List<double> _frameMs = new List<double>();

    // Demand, measured post-radius-gate: every tile the scenario actually
    // asked for this refresh, whether it got one or not.
    private int _peakDemand, _lastDemand;
    private long _refusalsAtRefresh;

    private void L(string s) { _log.AppendLine(s); Debug.Log("[tilecap] " + s); }
    private void Note(string s) => L("    note  " + s);
    private void Pass(string s) { _pass++; L("    PASS  " + s); }
    private void Fail(string s) { _fail++; L("    FAIL  " + s); }
    private void Check(bool ok, string s) { if (ok) Pass(s); else Fail(s); }

    private static int ArgInt(string f, int d)
    {
        string[] a = Environment.GetCommandLineArgs();
        for (int i = 0; i < a.Length - 1; i++)
            if (string.Equals(a[i], f, StringComparison.OrdinalIgnoreCase))
                return int.Parse(a[i + 1], CultureInfo.InvariantCulture);
        return d;
    }

    private int _spread;

    IEnumerator Start()
    {
        _tilePoolCap = ArgInt("-tilecap", _tilePoolCap);
        _slotCapacity = ArgInt("-slotcap", _slotCapacity);
        _spread = ArgInt("-spread", 384);
        int holdFrames = ArgInt("-holdseconds", 60) * 60;

        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;

        if (_player == null) _player = FindAnyObjectByType<PlayerController>();
        float t0 = Time.realtimeSinceStartup;
        while (Store == null && Time.realtimeSinceStartup - t0 < 240f) yield return null;

        _outDir = Path.Combine(Application.persistentDataPath, _outputRootFolderName,
                               DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(_outDir);

        int perSide = 2 * (_spread / Pitch) + 1;
        L("=== FLUID TILE-CAP LADDER: the frozen-cube repro, deliberately ===");
        L(DateTime.Now.ToString("u", CultureInfo.InvariantCulture));
        L($"tile pool cap {_tilePoolCap}   slot cap {_slotCapacity:N0} " +
          $"(MAX_ACTIVE_FLUID {EngineConfig.MAX_ACTIVE_FLUID:N0})");
        L($"lattice: {perSide}x{perSide} pockets of {PocketEdge}^3 on a {Pitch}-voxel pitch, " +
          $"spread +/-{_spread} voxels");
        L($"  => tile demand ~{perSide * perSide} per Y layer, slot demand " +
          $"~{perSide * perSide * PocketEdge * PocketEdge * PocketEdge:N0} voxels");
        L($"  per-cell GPU cost is 16 B/cell x 32,768 cells/tile = 0.5 MB PER TILE, " +
          $"so this cap reserves {_tilePoolCap * 0.5:F0} MB before any fluid exists");
        L($"hold phase {holdFrames / 60}s. Wall clock only; no gpuFrameTime vs a budget (Amdt 8.10).");
        L("");

        if (Store == null) { Fail("world never booted"); yield return Report(); yield break; }
        for (int i = 0; i < 150; i++) yield return null;

        Camera cam = Camera.main;
        int3 camVox = CoordMath.WorldToVoxel(new float3(cam.transform.position.x,
                                                        cam.transform.position.y,
                                                        cam.transform.position.z));
        _surfaceY = SurfaceY(camVox.x, camVox.z);
        if (_surfaceY < 1) { Fail("no surface"); yield return Report(); yield break; }
        _centre = camVox;

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
            OnVoxelApplied = v => { Clip.MarkDirty(CoordMath.VoxelToChunk(v)); _applied++; },
        };
        _edits.AttachFluidSimulation(_fluid, Store);
        _gap = gameObject.AddComponent<FrameGapProbe>();

        L($"GPU AT ALLOCATION, before any fluid: active set " +
          $"{_fluid.GpuActiveSetBytes() / 1048576.0:F1} MB, everything the sim owns " +
          $"{_fluid.GpuAllocatedBytes() / 1048576.0:F1} MB");
        L("");

        yield return Saturate();
        yield return PlaceMarkerCube();
        yield return Shot("01_cube_placed");

        _gap.Recording = true;
        yield return Hold(holdFrames);
        _gap.Recording = false;

        yield return Settle();
        yield return Shot("02_after_settle");
        yield return Verify();
        yield return Report();
    }

    private void Refresh(int3 pv)
    {
        _fluid.UpdatePlayerPosition(pv);
        var st = FluidTileResidency.Refresh(Store, _tiles, pv,
                                            _fluid.ActiveRadiusVoxels, _fluid.SleepRadiusVoxels);
        // DEMAND, post-radius-gate. TilesWithFluid is counted BEFORE the
        // radius test, so it overcounts by the tiles in the chunk AABB that
        // fall outside the sphere. These three are exactly the tiles that
        // passed all three gates and therefore genuinely wanted a slot.
        int demand = st.TilesAlreadyResident + st.TilesAcquired + st.TilesRefusedPoolFull;
        _lastDemand = demand;
        if (demand > _peakDemand) _peakDemand = demand;
        _refusalsAtRefresh += st.TilesRefusedPoolFull;
    }

    private void Tick()
    {
        if (_readback.CanIssue) { _fluid.Tick(Clip); _readback.IssueReadback(0); }
        _readback.PumpAndApply();
    }

    /// The thin-and-wide lattice: one small pocket per tile, so the pool is
    /// taken by TILE COUNT rather than by fluid volume.
    private IEnumerator Saturate()
    {
        L("--- SATURATE: a sparse lattice, one pocket per tile ---");
        int3 c = _centre;
        int pockets = 0;
        for (int dz = -_spread; dz <= _spread; dz += Pitch)
        {
            for (int dx = -_spread; dx <= _spread; dx += Pitch)
            {
                int sx = c.x + dx, sz = c.z + dz;
                int sy = SurfaceY(sx, sz);
                if (sy < 1) continue;
                var lo = new int3(sx, sy + 1, sz);
                _placed += _edits.SetBox(lo, lo + (PocketEdge - 1), Materials.Water);
                pockets++;
            }
            // Drain as we go: placing ~600 pockets in one frame would stage an
            // op burst far past the §8.5 apply budget and the first hold
            // segment would be measuring the drain, not the cap.
            Refresh(_centre); Tick();
            yield return null;
        }
        for (int i = 0; i < 120; i++)
        {
            if ((i % 20) == 0) Refresh(_centre);
            Tick();
            yield return null;
        }
        _fluid.ReadSlotCounters(out uint live, out uint _);
        L($"  placed {pockets} pockets, {_placed:N0} voxels");
        L($"  live slots {live:N0} / {_slotCapacity:N0}  ({live * 100.0 / _slotCapacity:F1}% of the slot cap)");
        L($"  tiles resident {_tiles.ResidentTiles} / {_tiles.TileCapacity}, " +
          $"demand {_lastDemand}, refusals so far {_refusalsAtRefresh:N0}");
        L("");
    }

    /// One unmistakable cube, in mid-air, outside the lattice, at the far
    /// corner so it is scanned LAST when the pool is already full.
    private IEnumerator PlaceMarkerCube()
    {
        L("--- THE MARKER CUBE: 12^3 water, suspended, nothing under it ---");
        int3 c = _centre;
        int mx = c.x + _spread + 96, mz = c.z + _spread + 96;
        int groundY = SurfaceY(mx, mz);
        if (groundY < 1) { Fail("no ground under the marker cube site"); yield break; }

        // Clear the column so the only thing that can stop it is the cap.
        // Cleared WIDE, not just around the cube. A tight column left the
        // surrounding terrain standing right up against it, and in the first
        // screenshot a ridge hid the gap underneath -- the census said
        // "floating" while the picture was ambiguous. The visual half of this
        // repro has to be readable on its own.
        _edits.SetBox(new int3(mx - 10, groundY + 1, mz - 10),
                      new int3(mx + CubeEdge + 9, groundY + CubeHeight + CubeEdge + 2, mz + CubeEdge + 9),
                      Materials.Air);
        _cubeGroundY = groundY;
        _cubeLo = new int3(mx, groundY + CubeHeight, mz);
        _cubeHi = _cubeLo + (CubeEdge - 1);
        long n = _edits.SetBox(_cubeLo, _cubeHi, Materials.Water);
        _placed += n;

        L($"  cube at {_cubeLo}..{_cubeHi} ({n:N0} voxels), ground y={groundY}, " +
          $"{CubeHeight} voxels of clear air beneath");
        L($"  distance from player {math.distance((float3)_cubeLo, (float3)_fluid.PlayerVoxel):F0} voxels " +
          $"(wake radius {_fluid.ActiveRadiusVoxels}) -- inside, so residency is the ONLY thing " +
          "that can stop it");
        for (int i = 0; i < 60; i++)
        {
            if ((i % 20) == 0) Refresh(_centre);
            Tick();
            yield return null;
        }
        int3 tile = _cubeLo / ChunkFluidMask.TILE_EDGE;
        L($"  cube's tile {tile}: slot " +
          $"{(_tiles.SlotForVoxel(_cubeLo) == FluidTileMap.NO_TILE ? "REFUSED (no tile)" : "granted")}");
        L("");
    }

    /// Steady state at a fixed cost, so frame time is comparable across caps.
    private IEnumerator Hold(int frames)
    {
        L($"--- HOLD: {frames / 60}s of steady simulation, nothing new placed ---");
        for (int f = 0; f < frames; f++)
        {
            if ((f % 20) == 0) Refresh(_centre);
            Tick();
            yield return null;
            _frameMs.Add(Time.unscaledDeltaTime * 1000.0);
        }
        L("");
    }

    private IEnumerator Settle()
    {
        L("--- SETTLE: 300 quiet frames ---");
        for (int i = 0; i < 300; i++)
        {
            if ((i % 20) == 0) Refresh(_centre);
            Tick();
            yield return null;
        }
        _readback.DrainBlocking();
        L("");
    }

    private long CountWater(int3 lo, int3 hi)
    {
        long n = 0;
        for (int x = lo.x; x <= hi.x; x++)
            for (int y = lo.y; y <= hi.y; y++)
                for (int z = lo.z; z <= hi.z; z++)
                {
                    var v = new int3(x, y, z);
                    if (!Store.IsResident(CoordMath.VoxelToChunk(v))) continue;
                    if (Store.GetVoxel(v) == Materials.Water) n++;
                }
        return n;
    }

    private IEnumerator Verify()
    {
        _fluid.ReadSlotCounters(out uint hi, out uint _);
        long expected = (long)CubeEdge * CubeEdge * CubeEdge;
        long stillThere = CountWater(_cubeLo, _cubeHi);
        double retained = expected > 0 ? stillThere * 100.0 / expected : 0;
        bool frozen = retained >= 90.0;

        L("--- RESULT ---");
        L($"  tile pool cap          {_tiles.TileCapacity}");
        L($"  PEAK TILE DEMAND       {_peakDemand}   (what a pool would need to hold to refuse nothing)");
        L($"  peak resident          {_tiles.PeakResidentTiles}");
        L($"  refusals               {_tiles.PoolExhaustionsTotal:N0}");
        L($"  live slots peak        {hi:N0} / {_slotCapacity:N0}  ({hi * 100.0 / _slotCapacity:F1}%)");
        L($"  GPU active set         {_fluid.GpuActiveSetBytes() / 1048576.0:F1} MB");
        L($"  GPU total (sim)        {_fluid.GpuAllocatedBytes() / 1048576.0:F1} MB");
        L($"  managed heap           {GC.GetTotalMemory(false) / 1048576.0:F0} MB");
        L("");
        L($"  MARKER CUBE: {stillThere:N0} of {expected:N0} voxels still in its original box " +
          $"({retained:F1}%)");
        L($"  VERDICT: {(frozen ? "FROZEN -- the cube never moved" : "FELL -- the cube simulated normally")}");
        L("");

        // ---- WHICH CAP WAS THE BINDING ONE ----
        // Step 3's question, answered by the run rather than assumed: if the
        // cube froze while slots sat far below their cap, the slot cap is not
        // implicated and raising it would change nothing.
        double slotPct = hi * 100.0 / _slotCapacity;
        if (frozen && slotPct < 50.0)
            Note($"the cube froze with live slots at only {slotPct:F1}% of MAX_ACTIVE_FLUID -- " +
                 "the TILE cap is the binding constraint here, the slot cap is not implicated");
        else if (frozen)
            Note($"the cube froze with live slots at {slotPct:F1}% of the slot cap -- " +
                 "both caps are in play, so a tile-cap-only change may not resolve it");
        else
            Note("the cube fell: at this cap the scenario is fully served");

        L("--- CORRECTNESS ---");
        Check(_readback.ReadbackErrorsTotal == 0,
            $"no op-list readback errors ({_readback.ReadbackErrorsTotal})");
        Check(_readback.OpsDroppedNonResident == 0,
            $"no ops dropped for non-residency ({_readback.OpsDroppedNonResident})");
        Check(_tiles.PeakResidentTiles <= _tiles.TileCapacity,
            $"tile pool never exceeded its cap ({_tiles.PeakResidentTiles} <= {_tiles.TileCapacity})");
        // WATER IS CONSERVED EITHER WAY -- frozen or fallen. A first version
        // censused only the cube's own x/z footprint down to the CENTRE's
        // surface height, which is the wrong column and too narrow: a cube
        // that FELL spreads sideways over the cleared pad, so 1,728 voxels
        // read back as 380 and the gate failed a run that was behaving
        // correctly. Censused over the whole cleared pad and the cube's own
        // ground level instead. (No lava anywhere here, so §7.3 destroys
        // nothing and the count must balance.)
        long around = CountWater(new int3(_cubeLo.x - 36, _cubeGroundY - 3, _cubeLo.z - 36),
                                 new int3(_cubeHi.x + 36, _cubeHi.y, _cubeHi.z + 36));
        Check(around >= expected * 0.95,
            $"the cube's water is conserved whether it froze or fell: {around:N0} of " +
            $"{expected:N0} within +/-36 of the site ({stillThere:N0} still in the original box) " +
            "-- §7.7 refusals are guarded no-ops, never a lost voxel");
        L("");

        if (_frameMs.Count > 0)
        {
            _frameMs.Sort();
            L($"--- FRAME TIME OVER THE HOLD PHASE (cap {_tiles.TileCapacity}) ---");
            L($"  frames {_frameMs.Count:N0}  p50 {P(0.5):F2}  p99 {P(0.99):F2}  max {P(1.0):F2} ms");
            L("");
        }

        var sb = new StringBuilder();
        _gap.AppendReport(sb);
        _log.Append(sb);
        yield return null;
    }

    private double P(double q)
    {
        if (_frameMs.Count == 0) return 0;
        return _frameMs[Mathf.Clamp(Mathf.RoundToInt((float)(q * (_frameMs.Count - 1))), 0, _frameMs.Count - 1)];
    }

    private IEnumerator Report()
    {
        _log.AppendLine();
        _log.AppendLine($"PASS {_pass}  FAIL {_fail}");
        _log.AppendLine(_fail == 0 ? "RESULT: PASSED" : "RESULT: FAILED");
        File.WriteAllText(Path.Combine(_outDir, "tilecap_report.txt"), _log.ToString());
        Debug.Log("[tilecap] report -> " + _outDir);
        yield return null;
        Application.Quit(_fail == 0 ? 0 : 1);
    }

    private void OnDestroy() { _readback?.Dispose(); _fluid?.Dispose(); }

    private int SurfaceY(int x, int z)
    {
        for (int y = WorldGenConstants.MAX_TERRAIN_HEIGHT + 2; y >= 1; y--)
        {
            byte m = Store.GetVoxel(new int3(x, y, z));
            if (m != Materials.Air && !MaterialRules.IsFluidMaterial(m)) return y;
        }
        return -1;
    }

    /// Aimed at the MARKER CUBE, not at the arena: the whole point is to see
    /// whether that specific cube is hanging in the air.
    private IEnumerator Shot(string name)
    {
        _readback?.DrainBlocking();
        Camera cam = Camera.main;
        if (cam != null)
        {
            var target = new Vector3((_cubeLo.x + CubeEdge * 0.5f) * 0.1f,
                                     (_cubeLo.y + CubeEdge * 0.5f) * 0.1f,
                                     (_cubeLo.z + CubeEdge * 0.5f) * 0.1f);
            // BELOW the cube's centre, looking slightly UP, so the clear air
            // under it is in frame. Looking down from above shows a cube on a
            // hillside and proves nothing.
            cam.transform.position = target + new Vector3(-7.0f, -1.4f, -7.0f);
            cam.transform.rotation = Quaternion.LookRotation(
                (target - cam.transform.position).normalized, Vector3.up);
        }
        yield return null;
        yield return new WaitForEndOfFrame();
        Texture2D tex = ScreenCapture.CaptureScreenshotAsTexture();
        File.WriteAllBytes(Path.Combine(_outDir, $"{_shot:D2}_{name}.png"), tex.EncodeToPNG());
        Destroy(tex);
        _shot++;
    }
}
