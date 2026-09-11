// ==========================================
// Assets/Game/LateGameSiegeRig.cs
//
// THE LATE-GAME SIEGE: large-scale fluid AND all six Phase 6 systems, at
// full intensity, continuously, for 3+ minutes.
//
// Everything before this tested one axis at a time. FluidChaosRig pushed
// volume to a million placed voxels with a thin gameplay load.
// Phase6CombinedRig ran all six systems concurrently but with a modest
// 8,000-voxel pool. The QA pass profiled each system alone and in pairs.
// NOTHING has run heavy fluid AND heavy gameplay together, sustained, long
// enough for a slow problem to show itself.
//
// -------------------------------------------------------------------------
// WHAT "SUSTAINED" MEANS HERE, AND WHY THE POUR IS ADAPTIVE
// -------------------------------------------------------------------------
// A fixed pour rate cannot hold a volume. Pour too slowly and the band is
// never reached; pour too fast and you hit MAX_ACTIVE_FLUID in the first ten
// seconds and spend the rest of the run measuring the clamp -- which is what
// the chaos ladder already measured, and is a different test.
//
// So the pour is CLOSED-LOOP: it tops up only while live slots sit below the
// target band and backs off above it. The run then holds a large active
// volume for its whole duration instead of spiking once, which is the only
// way "does it degrade over three minutes" is a meaningful question.
//
// -------------------------------------------------------------------------
// THE NEW QUESTION THIS RIG EXISTS TO ASK
// -------------------------------------------------------------------------
// Does frame time DEGRADE PROGRESSIVELY under sustained combined load --
// a growing apply backlog, heap creep, tile-pool churn, free-list
// fragmentation -- or does it stay flat? Every prior test was too short or
// too one-sided to tell. Frame stats are therefore reported PER 30-SECOND
// SEGMENT, not just as one aggregate, because an aggregate hides a trend by
// construction.
//
// MEASUREMENT: wall clock only. No gpuFrameTime against a budget (Amendment
// 8.10), no Xcode/Instruments (8.9 Rule 1), no Performance State field.
// Cooldowns belong to the runner.
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

public class LateGameSiegeRig : MonoBehaviour
{
    [SerializeField] private ComputeShader _fluidCA;
    [SerializeField] private PlayerController _player;
    [SerializeField] private int _tilePoolCap = 512;
    [SerializeField] private int _slotCapacity = 500000;
    [SerializeField] private int _maxOpsPerFrame = 65536;
    [SerializeField] private string _outputRootFolderName = "LateGameSiege";

    private const float Dt = 1f / 60f;
    /// Hold the live volume in this band. Well under MAX_ACTIVE_FLUID (500K)
    /// so the clamp is NOT what this run measures.
    private const int TargetLiveLow = 150000;
    private const int TargetLiveHigh = 320000;
    private const int SegmentFrames = 1800;          // 30 s
    private const int SpreadVoxels = 260;            // pour across ~52 m, not one pool

    private static ChunkStore Store => Phase4Bootstrapper.Store;
    private static TerrainClipmap Clip => Phase4Bootstrapper.Clipmap;

    private EditService _edits;
    private FluidGpuSimulation _fluid;
    private FluidOpListReadback _readback;
    private FluidTileMap _tiles;
    private SweptCCD _ccd;
    private ProjectileTrace _proj;
    private DestructionReducer _boom;
    private Buoyancy _buoy;
    private FrameGapProbe _gap;

    private readonly StringBuilder _log = new StringBuilder();
    private int _pass, _fail, _shot;
    private string _outDir;
    private int3 _centre; private int _surfaceY;
    private long _applied, _placed, _dug, _sweeps, _shots, _dets, _boomVox, _buoyWet, _steps;
    private int _ccdMissed;
    private readonly List<double> _frameMs = new List<double>();
    private readonly List<int> _liveTrace = new List<int>();
    private readonly List<int> _backlogTrace = new List<int>();
    private readonly List<double> _heapTrace = new List<double>();

    private void L(string s) { _log.AppendLine(s); Debug.Log("[siege] " + s); }
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

    IEnumerator Start()
    {
        int seconds = ArgInt("-siegeseconds", 200);
        _tilePoolCap = ArgInt("-tilecap", _tilePoolCap);
        int shotEvery = ArgInt("-shotevery", 15) * 60;

        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;

        if (_player == null) _player = FindAnyObjectByType<PlayerController>();
        float t0 = Time.realtimeSinceStartup;
        while (Store == null && Time.realtimeSinceStartup - t0 < 240f) yield return null;

        _outDir = Path.Combine(Application.persistentDataPath, _outputRootFolderName,
                               DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(_outDir);

        L("=== LATE-GAME SIEGE: heavy fluid + all six systems, sustained ===");
        L(DateTime.Now.ToString("u", CultureInfo.InvariantCulture));
        L($"duration {seconds}s, screenshot every {shotEvery / 60}s");
        L($"tile pool cap {_tilePoolCap} (0.5 MB/tile reserved up front)");
        L($"live-volume band {TargetLiveLow:N0}-{TargetLiveHigh:N0} " +
          $"(MAX_ACTIVE_FLUID {EngineConfig.MAX_ACTIVE_FLUID:N0} -- the clamp is NOT what this measures)");
        L("Wall clock only; gpuFrameTime read nowhere against a budget (Amdt 8.10).");
        L("");

        if (Store == null) { Fail("world never booted"); yield return Report(); yield break; }
        if (_player == null) { Fail("no PlayerController"); yield return Report(); yield break; }
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
        _ccd = new SweptCCD(Store, Store);
        _proj = new ProjectileTrace(Store, Store);
        _boom = new DestructionReducer(_edits, Store);
        _buoy = new Buoyancy(Store, Store);
        _player.DebugTakeControl();
        _player.Bind(Store, Store);
        _gap = gameObject.AddComponent<FrameGapProbe>();

        BuildArena();
        yield return Shot("000s_start");

        _gap.Recording = true;
        yield return Siege(seconds * 60, shotEvery);
        _gap.Recording = false;

        yield return Settle();
        yield return Shot("999_after_settle");
        yield return Verify();
        yield return Report();
    }

    private void BuildArena()
    {
        int y = _surfaceY; int3 c = _centre;
        // A big basin to swim in, a wall for CCD, and two large masses to
        // detonate repeatedly so the destruction never runs out of material.
        _edits.SetBox(new int3(c.x - 20, y - 14, c.z - 20), new int3(c.x + 20, y + 10, c.z + 20), Materials.Air);
        _edits.SetBox(new int3(c.x - 21, y - 15, c.z - 21), new int3(c.x + 21, y - 15, c.z + 21), Materials.Stone);
        _edits.SetBox(new int3(c.x - 20, y - 14, c.z - 20), new int3(c.x + 20, y - 2, c.z + 20), Materials.Water);
        // CCD WALL AT +50, AND THE MASSES KEPT CLEAR OF IT.
        // First version put the wall at +30 and detonated radius 40 at +65,
        // which spans +25..+105 -- the blast destroyed the wall, and 7 of 40
        // sweeps then correctly reported "no wall to hit". The assertion was
        // right and the arena was wrong. Right mass moved to +80..135 so a
        // radius-40 blast at +105 spans +65..+145, and the in-water blast is
        // radius 20 so it cannot reach +50 either.
        _edits.SetBox(new int3(c.x + 50, y - 2, c.z - 12), new int3(c.x + 51, y + 20, c.z + 12), Materials.Stone);
        _edits.SetBox(new int3(c.x - 100, y - 30, c.z - 40), new int3(c.x - 40, y + 30, c.z + 40), Materials.Stone);
        _edits.SetBox(new int3(c.x + 80, y - 30, c.z - 40), new int3(c.x + 135, y + 30, c.z + 40), Materials.Stone);
        L($"arena at {c}, surface y={y}: 40x40 basin, wall, two 60x60x80 masses to detonate");
        L($"dense bricks at rest {Store.DenseBricksHeld}");
        L("");
    }

    private IEnumerator Siege(int frames, int shotEvery)
    {
        var rng = new System.Random(20260911);
        var bore = EditService.Tiers[EditService.Tiers.Length - 1];
        var budget = new EditService.ToolBudget();
        int3 c = _centre; int y = _surfaceY;
        int poursThrottled = 0;

        for (int f = 0; f < frames; f++)
        {
            int3 pv = CoordMath.WorldToVoxel(_player.Motor.PositionM);

            // ---- FLUID ----
            if ((f % 20) == 0)
            {
                _fluid.UpdatePlayerPosition(pv);
                var rst = FluidTileResidency.Refresh(Store, _tiles, pv,
                                           _fluid.ActiveRadiusVoxels, _fluid.SleepRadiusVoxels);
                RecordDemand(rst);
            }
            if (_readback.CanIssue) { _fluid.Tick(Clip); _readback.IssueReadback(0); }
            _readback.PumpAndApply();

            _fluid.ReadSlotCounters(out uint live, out uint _);

            // ---- CLOSED-LOOP POUR, WIDE AREA ----
            // Tops up only below the band. Above it the pour backs off, so the
            // run HOLDS a large volume instead of spiking into the clamp.
            if (live < TargetLiveHigh)
            {
                int boxes = live < TargetLiveLow ? 5 : 2;
                for (int k = 0; k < boxes; k++)
                {
                    byte m = k % 3 == 0 ? Materials.Water : k % 3 == 1 ? Materials.Sand : Materials.Lava;
                    int dx = rng.Next(-SpreadVoxels, SpreadVoxels), dz = rng.Next(-SpreadVoxels, SpreadVoxels);
                    int sx = c.x + dx, sz = c.z + dz;
                    int sy = SurfaceY(sx, sz);
                    if (sy < 1) continue;
                    var lo = new int3(sx, sy + 8, sz);
                    _placed += _edits.SetBox(lo, lo + new int3(5, 5, 5), m);
                }
            }
            else poursThrottled++;

            // ---- SUSTAINED MAX-TIER DIGGING, EVERY FRAME ----
            int allowed = budget.Accrue(Dt, bore.VoxelsPerSecond);
            if (allowed > 0)
            {
                var at = new int3(pv.x + rng.Next(-10, 10), pv.y - 2 + rng.Next(-3, 3),
                                  pv.z + rng.Next(-10, 10));
                _dug += _edits.SetSphere(at, bore.RadiusVoxels, Materials.Air);
            }

            // ---- REPEATED LARGE DETONATIONS, toward the 400K reference ----
            // radius 30 = 113,081 voxels; radius 40 = 267,761. §13's reference
            // event is radius 46 (407,597). Alternating sides so neither mass
            // is exhausted, and every third lands in the basin -- into fluid.
            if ((f % 1200) == 300)
            {
                int n = (int)_dets;
                bool intoWater = (n % 3 == 2);
                int r = intoWater ? 20 : ((n % 3 == 0) ? 40 : 30);
                int3 at = intoWater
                    ? new int3(c.x + rng.Next(-14, 14), y - 6, c.z + rng.Next(-14, 14))
                    : (n % 2 == 0 ? new int3(c.x - 70, y + rng.Next(-10, 10), c.z + rng.Next(-20, 20))
                                  : new int3(c.x + 105, y + rng.Next(-10, 10), c.z + rng.Next(-20, 20)));
                _boom.Detonate(at, r);
                _dets++;
                Note($"[{f / 60}s] detonation #{_dets} radius {r} at {at}" +
                     ((n % 3 == 2) ? "  (INTO THE FLUID)" : ""));
            }
            _boomVox += _boom.Step();
            while (_boom.TryTakeCompleted(out ProxyDrop _)) { }

            // ---- PLAYER: swimming through it all ----
            float ang = f * 0.012f;
            _player.DebugStep(Dt, new float2(Mathf.Cos(ang), Mathf.Sin(ang * 1.4f)), (f % 80) == 0);
            _steps++;

            var bs = _buoy.Sample(_player.Motor.PositionM, 1.8f, 8f, _readback.FramesSinceLastApplied);
            if (bs.InFluid) _buoyWet++;

            // ---- GRAPPLE-SPEED CCD every ~5 s ----
            if ((f % 300) == 150)
            {
                float3 a = new float3((c.x + 24) * 0.1f, (y + 4) * 0.1f, c.z * 0.1f);
                float3 b = new float3((c.x + 56) * 0.1f, (y + 4) * 0.1f, c.z * 0.1f);
                CCDResult r = _ccd.Sweep(a, b, 0.6f, 1.8f);
                _sweeps++;
                if (!r.HitSolid) _ccdMissed++;
            }

            // ---- PROJECTILES every ~2 s, through fluid and debris ----
            if ((f % 120) == 60)
            {
                float3 a = new float3((c.x + 24) * 0.1f, (y + 14) * 0.1f, (c.z + 14) * 0.1f);
                float3 b = new float3((c.x - 14) * 0.1f, (y - 8) * 0.1f, (c.z - 12) * 0.1f);
                _proj.Trace(a, b); _shots++;
            }

            if (shotEvery > 0 && f > 0 && (f % shotEvery) == 0)
                yield return Shot($"{f / 60:D3}s");

            yield return null;
            _frameMs.Add(Time.unscaledDeltaTime * 1000.0);
            _liveTrace.Add((int)live);
            _backlogTrace.Add(_readback.PendingOps);
            if ((f % 60) == 0) _heapTrace.Add(GC.GetTotalMemory(false) / 1048576.0);
        }
        Note($"pour throttled on {poursThrottled} frames (the band held)");
        L("");
    }

    /// PEAK TILE DEMAND -- the pool size this scenario would need in order to
    /// refuse nothing. Counted post-radius-gate, so it is the tiles that
    /// passed all three of Refresh's gates and genuinely wanted a slot;
    /// Stats.TilesWithFluid is counted BEFORE the radius test and overcounts.
    /// Without this, "raise the cap" has no target to raise it TO.
    private int _peakTileDemand;
    private void RecordDemand(FluidTileResidency.Stats st)
    {
        int demand = st.TilesAlreadyResident + st.TilesAcquired + st.TilesRefusedPoolFull;
        if (demand > _peakTileDemand) _peakTileDemand = demand;
    }

    private long _lavaObsBefore, _lavaObsAfter, _waterBefore, _waterAfter;
    private IEnumerator Settle()
    {
        L("--- SETTLE: 300 quiet frames, nothing placed, dug or detonated ---");
        CountMaterials(out _lavaObsBefore, out _waterBefore);
        for (int i = 0; i < 300; i++)
        {
            int3 pv = CoordMath.WorldToVoxel(_player.Motor.PositionM);
            if ((i % 20) == 0)
            {
                _fluid.UpdatePlayerPosition(pv);
                var rst = FluidTileResidency.Refresh(Store, _tiles, pv,
                                           _fluid.ActiveRadiusVoxels, _fluid.SleepRadiusVoxels);
                RecordDemand(rst);
            }
            if (_readback.CanIssue) { _fluid.Tick(Clip); _readback.IssueReadback(0); }
            _readback.PumpAndApply();
            yield return null;
        }
        _readback.DrainBlocking();
        CountMaterials(out _lavaObsAfter, out _waterAfter);
        L("");
    }

    /// LAVA + OBSIDIAN, not "fluid voxels". §7.3 is Water + Lava -> Air +
    /// Obsidian, so water is DELIBERATELY destroyed on reaction and a raw
    /// fluid census can never balance in a scenario that pours both. Each
    /// reacted lava becomes exactly one obsidian, so this sum survives.
    /// (Lesson from the chaos ladder, where the first gate measured the wrong
    /// quantity and failed at -10%.)
    private void CountMaterials(out long lavaObs, out long water)
    {
        long lo = 0, w = 0;
        // MUST COVER THE POUR SPREAD. A first version censused +/-130 while
        // the pour spreads +/-260, so it counted 3,577 voxels out of nearly
        // 200,000 placed -- a conservation gate over 2% of the material is
        // not a conservation gate. Bounded tightly in Y instead, where the
        // fluid actually is, to keep the full-stride scan affordable.
        for (int x = _centre.x - (SpreadVoxels + 12); x <= _centre.x + (SpreadVoxels + 12); x++)
            for (int y = _surfaceY - 26; y <= _surfaceY + 24; y++)
                for (int z = _centre.z - (SpreadVoxels + 12); z <= _centre.z + (SpreadVoxels + 12); z++)
                {
                    var v = new int3(x, y, z);
                    if (!Store.IsResident(CoordMath.VoxelToChunk(v))) continue;
                    byte m = Store.GetVoxel(v);
                    if (m == Materials.Lava || m == Materials.Obsidian) lo++;
                    else if (m == Materials.Water) w++;
                }
        lavaObs = lo; water = w;
    }

    private IEnumerator Verify()
    {
        _fluid.ReadSlotCounters(out uint hi, out uint ever);
        L("--- ACTIVITY OVER THE WHOLE SIEGE ---");
        L($"  placed {_placed:N0}   dug {_dug:N0}   voxel writes {_applied:N0}");
        L($"  detonations {_dets} ({_boomVox:N0} voxels)   CCD sweeps {_sweeps} (missed {_ccdMissed})");
        L($"  projectiles {_shots}   player steps {_steps}   buoyancy wet {_buoyWet:N0}");
        L($"  live slots peak {hi:N0} / {_fluid.SlotCapacity:N0}");
        L($"  PEAK TILE DEMAND {_peakTileDemand} (the pool size that would refuse nothing; " +
          $"cap is {_tiles.TileCapacity})");
        L($"  tiles {_tiles.ResidentTiles}/{_tiles.TileCapacity} resident, acquired " +
          $"{_tiles.TilesAcquiredTotal}, released {_tiles.TilesReleasedTotal}, exhaustions {_tiles.PoolExhaustionsTotal:N0}");
        L($"  op-list total {_readback.OpsTotal:N0}, readback errors {_readback.ReadbackErrorsTotal}, " +
          $"stale {_readback.StaleOpsDropped:N0}, non-resident {_readback.OpsDroppedNonResident}");
        L($"  GPU active set {_fluid.GpuActiveSetBytes() / 1048576.0:F1} MB");
        L("");

        // ---- THE NEW QUESTION: does it degrade over time? ----
        L("--- FRAME TIME PER 30s SEGMENT (the degradation test) ---");
        L($"  {"segment",-10}{"p50",8}{"p99",8}{"max",10}{"live avg",11}{"backlog avg",13}");
        int segs = (_frameMs.Count + SegmentFrames - 1) / SegmentFrames;
        var firstP50 = 0.0; var lastP50 = 0.0;
        for (int s = 0; s < segs; s++)
        {
            int lo = s * SegmentFrames, hi2 = Math.Min(_frameMs.Count, lo + SegmentFrames);
            if (hi2 <= lo) continue;
            var slice = _frameMs.GetRange(lo, hi2 - lo);
            var sorted = new List<double>(slice); sorted.Sort();
            double p50 = sorted[sorted.Count / 2];
            double p99 = sorted[Mathf.Clamp((int)(0.99 * (sorted.Count - 1)), 0, sorted.Count - 1)];
            double mx = sorted[sorted.Count - 1];
            long lsum = 0, bsum = 0; int n = 0;
            for (int i = lo; i < hi2 && i < _liveTrace.Count; i++) { lsum += _liveTrace[i]; bsum += _backlogTrace[i]; n++; }
            L($"  {s * 30}-{(s + 1) * 30}s{"",-3}{p50,8:F2}{p99,8:F2}{mx,10:F2}{(n > 0 ? lsum / n : 0),11:N0}{(n > 0 ? bsum / n : 0),13:N0}");
            if (s == 0) firstP50 = p50;
            lastP50 = p50;
        }
        double drift = firstP50 > 0 ? (lastP50 / firstP50 - 1) * 100 : 0;
        L($"  first segment p50 {firstP50:F2} -> last {lastP50:F2} ms  ({drift:+0.0;-0.0}%)");
        if (_heapTrace.Count > 1)
            L($"  managed heap {_heapTrace[0]:F0} MB -> {_heapTrace[_heapTrace.Count - 1]:F0} MB " +
              $"(peak {Max(_heapTrace):F0})");
        L("");

        // ONE-SIDED, DELIBERATELY. A first version used Math.Abs and so FAILED
        // a run whose frame time IMPROVED 38% -- flagging the scenario getting
        // FASTER as a degradation. Only a rising trend is a problem here; the
        // fall is the opening pour ramp draining out of segment 1.
        Check(drift < 25.0,
            $"frame time did not degrade progressively (first->last segment p50 {drift:+0.0;-0.0}%). " +
            "A growing backlog, heap creep or pool churn would show as a RISING trend; " +
            "a fall is the opening pour ramp leaving segment 1 and is not a fault");

        _frameMs.Sort();
        L($"  WHOLE RUN: frames {_frameMs.Count:N0}  p50 {P(0.5):F2}  p99 {P(0.99):F2}  max {P(1.0):F2} ms");
        L("");

        // ---- CORRECTNESS ----
        L("--- CORRECTNESS UNDER SUSTAINED COMBINED LOAD ---");
        Check(_dets > 0 && _dug > 0 && _sweeps > 0 && _shots > 0 && _buoyWet > 0 && _placed > 0,
            "every system actually ran (a silent no-op would make the whole run meaningless)");
        Check(_ccdMissed == 0, $"every CCD sweep hit the wall ({_sweeps - _ccdMissed}/{_sweeps})");
        Check(_readback.ReadbackErrorsTotal == 0, $"no op-list readback errors ({_readback.ReadbackErrorsTotal})");
        Check(_readback.OpsDroppedNonResident == 0,
            $"no ops dropped for non-residency ({_readback.OpsDroppedNonResident})");
        Check(_tiles.PeakResidentTiles <= _tiles.TileCapacity,
            $"tile pool never exceeded its cap ({_tiles.PeakResidentTiles} <= {_tiles.TileCapacity})");
        if (_tiles.PoolExhaustionsTotal > 0)
            Note($"tile pool cap reached {_tiles.PoolExhaustionsTotal:N0}x -- §7.7's clean refusal, " +
                 "expected when destruction keeps feeding new material into a wide fluid spread");

        long d = _lavaObsAfter - _lavaObsBefore;
        double pct = _lavaObsBefore > 0 ? Math.Abs(d) * 100.0 / _lavaObsBefore : 0;
        Check(pct <= 3.0,
            $"lava+obsidian conserved across the quiet settle: {_lavaObsBefore:N0} -> {_lavaObsAfter:N0} " +
            $"({d:+0;-0}, {pct:F2}%)");
        Note($"water {_waterBefore:N0} -> {_waterAfter:N0} ({_waterAfter - _waterBefore:+0;-0}) -- " +
             "destroyed by design on lava contact (§7.3)");

        int unwoken = 0, sampled = 0;
        for (int dx = -240; dx <= 240; dx += 16)
            for (int dz = -240; dz <= 240; dz += 16)
                for (int dy = -10; dy <= 12; dy += 4)
                {
                    var v = new int3(_centre.x + dx, _surfaceY + dy, _centre.z + dz);
                    if (!Store.IsResident(CoordMath.VoxelToChunk(v))) continue;
                    if (!MaterialRules.IsMobile(Store.GetVoxel(v))) continue;
                    if (!FluidActiveRegion.WithinWakeRadius(v, _fluid.PlayerVoxel, _fluid.ActiveRadiusVoxels)) continue;
                    sampled++;
                    if (_tiles.SlotForVoxel(v) == FluidTileMap.NO_TILE) unwoken++;
                }
        Check(unwoken == 0,
            $"no silent wake failure: {unwoken} of {sampled} sampled mobile voxels inside the radius lack a tile");

        // FROZEN-FLUID CENSUS -- the artifact itself, counted rather than
        // eyeballed in a screenshot.
        //
        // WHY THE GATE ABOVE DOES NOT ALREADY CATCH IT, which matters: that
        // audit steps 16 voxels and spans only surfaceY-10..+12, and it
        // reported 0 in both siege runs while the screenshots plainly showed
        // suspended cubes. It was not wrong -- it sampled a band the floaters
        // were mostly above. A "0" from a narrow sample is not evidence of
        // absence, so this walks the pour footprint on a 4-voxel stride and
        // reaches 40 voxels above the surface.
        //
        // Reported as a NOTE, never a gate: at cap 512 a nonzero count is the
        // designed §7.7 refusal being visible, not a failure.
        long frozen = 0, mobileSeen = 0;
        for (int x = _centre.x - (SpreadVoxels + 12); x <= _centre.x + (SpreadVoxels + 12); x += 4)
            for (int y = _surfaceY - 26; y <= _surfaceY + 40; y += 4)
                for (int z = _centre.z - (SpreadVoxels + 12); z <= _centre.z + (SpreadVoxels + 12); z += 4)
                {
                    var v = new int3(x, y, z);
                    if (!Store.IsResident(CoordMath.VoxelToChunk(v))) continue;
                    if (!MaterialRules.IsMobile(Store.GetVoxel(v))) continue;
                    if (!FluidActiveRegion.WithinWakeRadius(v, _fluid.PlayerVoxel, _fluid.ActiveRadiusVoxels)) continue;
                    mobileSeen++;
                    if (_tiles.SlotForVoxel(v) == FluidTileMap.NO_TILE) frozen++;
                }
        // WHY ARE OPS BEING DROPPED FOR NON-RESIDENCY? Two candidates, and
        // they call for different responses:
        //   (a) a tile OUTLIVES its chunk's residency. Refresh tests §9.4 at
        //       ACQUIRE time and never again; ReleaseBeyondSleep tests only
        //       the radius. So an evicted chunk leaves its tile resident, and
        //       that would be a latent gap a small pool merely hid.
        //   (b) fluid simply reaches the STREAMING EDGE and keeps trying to
        //       move outward into never-loaded space -- the documented §9.4
        //       guard doing its job, just far more often because a larger
        //       pool can afford tiles out at the edge that a full one never
        //       acquired.
        // This counter separates them: it is nonzero only under (a).
        int orphanTiles = 0;
        foreach (int3 tc in _tiles.ResidentTileCoords())
        {
            int3 anyVoxel = tc * ChunkFluidMask.TILE_EDGE;
            if (!Store.IsResident(CoordMath.VoxelToChunk(anyVoxel))) orphanTiles++;
        }
        Note($"TILES WHOSE CHUNK IS NO LONGER RESIDENT: {orphanTiles} of {_tiles.ResidentTiles}. " +
             "Nonzero means a tile outlived its chunk (Refresh checks §9.4 at acquire time only); " +
             "zero means the dropped ops are fluid pressing on the streaming edge instead");

        Note($"FROZEN-FLUID CENSUS (4-voxel stride, surface-26..+40): {frozen:N0} of " +
             $"{mobileSeen:N0} sampled mobile voxels inside the radius have NO TILE and " +
             $"therefore cannot move ({(mobileSeen > 0 ? frozen * 100.0 / mobileSeen : 0):F1}%). " +
             $"Tile cap {_tiles.TileCapacity}, peak demand {_peakTileDemand}");

        var sb = new StringBuilder();
        _gap.AppendReport(sb);
        _log.Append(sb);
        yield return null;
    }

    private static double Max(List<double> v) { double m = 0; foreach (double x in v) if (x > m) m = x; return m; }

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
        File.WriteAllText(Path.Combine(_outDir, "siege_report.txt"), _log.ToString());
        Debug.Log("[siege] report -> " + _outDir);
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

    private IEnumerator Shot(string name)
    {
        _readback?.DrainBlocking();
        Camera cam = Camera.main;
        if (cam != null)
        {
            cam.transform.position = new Vector3((_centre.x - 70) * 0.1f,
                                                 (_surfaceY + 40) * 0.1f, (_centre.z - 70) * 0.1f);
            var t = new Vector3(_centre.x * 0.1f, _surfaceY * 0.1f, _centre.z * 0.1f);
            cam.transform.rotation = Quaternion.LookRotation((t - cam.transform.position).normalized, Vector3.up);
        }
        yield return null;
        yield return new WaitForEndOfFrame();
        Texture2D tex = ScreenCapture.CaptureScreenshotAsTexture();
        File.WriteAllBytes(Path.Combine(_outDir, $"{_shot:D2}_{name}.png"), tex.EncodeToPNG());
        Destroy(tex);
        _shot++;
    }
}
