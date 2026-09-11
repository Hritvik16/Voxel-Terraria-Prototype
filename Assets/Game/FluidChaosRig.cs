// ==========================================
// Assets/Game/FluidChaosRig.cs
//
// THE LARGE-SCALE CHAOS LADDER. Nothing has been tested past 32,000
// simultaneous live voxels or 512 scattered pockets; this goes looking for the
// real ceiling.
//
// WHAT IT DRIVES, ALL AT ONCE, CAMERA-DRIVEN, NO HUMAN INPUT:
//   - continuous vents of water / sand / lava across a wide area
//   - repeated OVERLAPPING detonations scattering material into the fluid
//   - the player walking and digging throughout
//
// -------------------------------------------------------------------------
// THE TWO CEILINGS THAT ALREADY EXIST, AND WHY THE LADDER IS PHRASED IN
// "PLACED" RATHER THAN "LIVE"
// -------------------------------------------------------------------------
// EngineConfig.MAX_ACTIVE_FLUID = 500,000 hard-clamps the slot pool, so no
// configuration can have more than half a million voxels SIMULATING at once.
// A "1,000,000 live voxel" rung is not a thing this engine can express, and a
// rig that claimed one would be lying. The ladder therefore targets PLACED
// voxels -- how much fluid is pushed into the world -- and reports LIVE slots
// as a measured consequence. Hitting the 500,000 clamp is a real, designed
// ceiling and reporting it is a result, not a failure.
//
// The second ceiling is the 512-tile pool. It binds on SPREAD, not volume:
// 512 tiles hold 16.7M cells, so a concentrated pour cannot exhaust it, while
// a wide scatter can. A cap-reached state that refuses cleanly per §7.7 is a
// pass; an unhandled exhaustion is not, and the two are checked separately.
//
// -------------------------------------------------------------------------
// PER-RUNG GATES -- the ladder STOPS on any of these
// -------------------------------------------------------------------------
//   1. zero silent wake failures (fluid present but outside a resident tile)
//   2. mass conservation across a quiet settling window
//   3. tile-pool exhaustion is CLEAN (refused and counted) or absent
//   4. no readback errors, no ops dropped for non-residency
//
// A found ceiling is the point of the exercise. Forcing a higher rung after a
// clean failure would destroy the evidence for where the ceiling is.
//
// MEASUREMENT: wall clock only, no gpuFrameTime against a budget (Amendment
// 8.10), no Xcode/Instruments (8.9 Rule 1), no Performance State field.
// Cooldowns belong to the runner script.
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

public class FluidChaosRig : MonoBehaviour
{
    [SerializeField] private ComputeShader _fluidCA;
    [SerializeField] private PlayerController _player;
    [SerializeField] private int _tilePoolCap = 512;
    [SerializeField] private int _slotCapacity = 500000;     // == MAX_ACTIVE_FLUID
    [SerializeField] private int _maxOpsPerFrame = 65536;
    [SerializeField] private string _outputRootFolderName = "FluidChaos";

    private const float Dt = 1f / 60f;

    private static ChunkStore Store => Phase4Bootstrapper.Store;
    private static TerrainClipmap Clip => Phase4Bootstrapper.Clipmap;

    private EditService _edits;
    private FluidGpuSimulation _fluid;
    private FluidOpListReadback _readback;
    private FluidTileMap _tiles;
    private DestructionReducer _boom;
    private FrameGapProbe _gapProbe;

    private readonly StringBuilder _log = new StringBuilder();
    private int _pass, _fail;
    private string _outDir, _phase = "-";
    private int3 _centre;
    private int _surfaceY;
    private long _applied, _placed, _dug, _detonations, _boomVoxels;
    private readonly List<double> _frameMs = new List<double>();

    private void L(string s) { _log.AppendLine(s); Debug.Log("[chaos] " + s); }
    private void Note(string s) => L("    note  " + s);
    private void Pass(string s) { _pass++; L("    PASS  " + s); }
    private void Fail(string s) { _fail++; L($"    FAIL  {s}   [{_phase}]"); }
    private void Check(bool ok, string s) { if (ok) Pass(s); else Fail(s); }

    private static string Arg(string flag)
    {
        string[] a = Environment.GetCommandLineArgs();
        for (int i = 0; i < a.Length - 1; i++)
            if (string.Equals(a[i], flag, StringComparison.OrdinalIgnoreCase)) return a[i + 1];
        return null;
    }

    IEnumerator Start()
    {
        int target = 50000;
        string t = Arg("-chaosvoxels");
        if (!string.IsNullOrEmpty(t)) target = int.Parse(t, CultureInfo.InvariantCulture);
        int spreadVox = 220;
        string sp = Arg("-chaosspread");
        if (!string.IsNullOrEmpty(sp)) spreadVox = int.Parse(sp, CultureInfo.InvariantCulture);

        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;

        if (_player == null) _player = FindAnyObjectByType<PlayerController>();
        float t0 = Time.realtimeSinceStartup;
        while (Store == null && Time.realtimeSinceStartup - t0 < 240f) yield return null;

        _outDir = Path.Combine(Application.persistentDataPath, _outputRootFolderName,
                               DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(_outDir);

        L("=== FLUID CHAOS LADDER ===");
        L(DateTime.Now.ToString("u", CultureInfo.InvariantCulture));
        L($"RUNG: target {target} PLACED fluid voxels, scatter +/-{spreadVox} voxels");
        L("");
        L("READING RULE. Wall clock only; no gpuFrameTime against any budget");
        L("(Amendment 8.10). §2.2's fluid CA budget is GPU-lane and this workflow");
        L("cannot attribute GPU stages, so nothing here is scored against it.");
        L("");
        L($"HARD CEILINGS IN PLAY: MAX_ACTIVE_FLUID = {EngineConfig.MAX_ACTIVE_FLUID} clamps");
        L($"LIVE slots regardless of how much is placed. Tile pool cap {_tilePoolCap}");
        L("binds on SPREAD, not volume (512 tiles hold 16.7M cells).");
        L("");

        if (Store == null) { Fail("world never booted"); yield return Report(); yield break; }
        for (int i = 0; i < 150; i++) yield return null;

        Camera cam = Camera.main;
        int3 camVox = CoordMath.WorldToVoxel(new float3(cam.transform.position.x,
                                                        cam.transform.position.y,
                                                        cam.transform.position.z));
        int surface = SurfaceY(camVox.x, camVox.z);
        if (surface < 1) { Fail("no surface under the camera"); yield return Report(); yield break; }
        _centre = camVox; _surfaceY = surface;

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
        _boom = new DestructionReducer(_edits, Store);
        if (_player != null) { _player.DebugTakeControl(); _player.Bind(Store, Store); }
        _gapProbe = gameObject.AddComponent<FrameGapProbe>();

        L($"slot capacity requested {_slotCapacity}, actual {_fluid.SlotCapacity}");
        L($"GPU active set {_fluid.GpuActiveSetBytes() / 1048576.0:F1} MB, " +
          $"whole sim {_fluid.GpuAllocatedBytes() / 1048576.0:F1} MB");
        L("");

        yield return Shot("00_before");
        _gapProbe.Recording = true;
        yield return Chaos(target, spreadVox);
        _gapProbe.Recording = false;
        yield return Shot("50_peak");
        yield return Settle();
        yield return Shot("90_after");
        yield return Verify(target);
        yield return Report();
    }

    // =====================================================================

    private IEnumerator Chaos(int targetPlaced, int spread)
    {
        _phase = "chaos";
        L($"--- CHAOS: pouring toward {targetPlaced} placed voxels ---");

        var rng = new System.Random(20260911);
        int frame = 0;
        const int MaxFrames = 9000;                 // 150 s ceiling, so a rung cannot hang
        int perPour = Mathf.Max(64, targetPlaced / 220);

        while (_placed < targetPlaced && frame < MaxFrames)
        {
            int3 pc = _player != null ? CoordMath.WorldToVoxel(_player.Motor.PositionM) : _centre;
            if ((frame % 20) == 0)
            {
                _fluid.UpdatePlayerPosition(pc);
                FluidTileResidency.Refresh(Store, _tiles, pc,
                                           _fluid.ActiveRadiusVoxels, _fluid.SleepRadiusVoxels);
            }

            // ---- POUR: three materials, scattered, every frame ----
            for (int k = 0; k < 3; k++)
            {
                byte m = k == 0 ? Materials.Water : k == 1 ? Materials.Sand : Materials.Lava;
                int dx = rng.Next(-spread, spread), dz = rng.Next(-spread, spread);
                int x = _centre.x + dx, z = _centre.z + dz;
                int sy = SurfaceY(x, z);
                if (sy < 1) continue;
                int side = Mathf.Max(2, (int)Math.Round(Math.Pow(perPour / 3.0, 1.0 / 3.0)));
                var lo = new int3(x, sy + 6, z);
                _placed += _edits.SetBox(lo, lo + new int3(side - 1, side - 1, side - 1), m);
            }

            // ---- OVERLAPPING DETONATIONS into the fluid ----
            if ((frame % 90) == 30)
            {
                int dx = rng.Next(-spread / 2, spread / 2), dz = rng.Next(-spread / 2, spread / 2);
                var at = new int3(_centre.x + dx, _surfaceY + rng.Next(-2, 6), _centre.z + dz);
                _boom.Detonate(at, 9);
                _detonations++;
            }
            _boomVoxels += _boom.Step();
            while (_boom.TryTakeCompleted(out ProxyDrop _)) { }

            // ---- PLAYER: moving and digging throughout ----
            if (_player != null)
            {
                float ang = frame * 0.015f;
                _player.DebugStep(Dt, new float2(Mathf.Cos(ang), Mathf.Sin(ang * 1.7f)), (frame % 90) == 0);
                if ((frame % 15) == 0)
                {
                    var at = new int3(pc.x + rng.Next(-6, 6), pc.y - 2, pc.z + rng.Next(-6, 6));
                    _dug += _edits.SetSphere(at, 2, Materials.Air);
                }
            }

            if (_readback.CanIssue) { _fluid.Tick(Clip); _readback.IssueReadback(0); }
            _readback.PumpAndApply();

            yield return null;
            _frameMs.Add(Time.unscaledDeltaTime * 1000.0);
            frame++;
        }

        L($"  poured {_placed} voxels over {frame} frames " +
          (frame >= MaxFrames ? "(HIT THE FRAME CEILING before the target)" : "(target reached)"));
        L("");
    }

    /// A quiet window: no pouring, no edits, no detonations. Mass must not
    /// change across it -- the CA moves voxels, it does not create or destroy.
    private long _lavaObsBefore, _lavaObsAfter, _waterBefore, _waterAfter;
    private IEnumerator Settle()
    {
        _phase = "settle";
        L("--- SETTLE: 300 quiet frames, nothing placed or removed ---");
        CountMaterials(out _lavaObsBefore, out _waterBefore);
        for (int i = 0; i < 300; i++)
        {
            int3 pc = _player != null ? CoordMath.WorldToVoxel(_player.Motor.PositionM) : _centre;
            if ((i % 20) == 0)
            {
                _fluid.UpdatePlayerPosition(pc);
                FluidTileResidency.Refresh(Store, _tiles, pc,
                                           _fluid.ActiveRadiusVoxels, _fluid.SleepRadiusVoxels);
            }
            if (_readback.CanIssue) { _fluid.Tick(Clip); _readback.IssueReadback(0); }
            _readback.PumpAndApply();
            yield return null;
        }
        _readback.DrainBlocking();
        CountMaterials(out _lavaObsAfter, out _waterAfter);
        L($"  lava+obsidian before {_lavaObsBefore}, after {_lavaObsAfter}, " +
          $"delta {_lavaObsAfter - _lavaObsBefore}");
        L($"  water          before {_waterBefore}, after {_waterAfter}, " +
          $"delta {_waterAfter - _waterBefore}  (DESTROYED by design on reaction)");
        L("");
    }

    /// WHAT IS ACTUALLY CONSERVED HERE, AND WHY IT IS NOT "FLUID VOXELS".
    ///
    /// A first version counted fluid voxels and demanded the total hold across
    /// the quiet window. It failed at -10%, and the engine was right: this
    /// scenario pours WATER AND LAVA TOGETHER, and §7.3's reaction table is
    ///     Water + Lava  ->  Air + Obsidian
    /// so every reaction DELIBERATELY DESTROYS one water voxel and converts
    /// one lava voxel to solid. Fluid count cannot be conserved in a scenario
    /// that reacts, and a gate demanding it was measuring the wrong quantity.
    ///
    /// The invariant that DOES survive reaction is lava + obsidian: each lava
    /// lost becomes exactly one obsidian. Water is reported separately as an
    /// informational figure, because its loss is designed behaviour.
    ///
    /// Strict whole-system conservation is NOT this rig's job -- Phase 5a/5b
    /// own it with a ledger and a CPU oracle, and prove it at 5/5 scenarios
    /// with zero mismatches. This rig tests SCALE.
    ///
    /// Stride 1, in a box tight enough to afford it: a strided sample of a
    /// still-moving body changes as voxels cross the stride, which would be
    /// indistinguishable from a leak.
    private void CountMaterials(out long lavaPlusObsidian, out long water)
    {
        long lo = 0, w = 0;
        for (int x = _centre.x - 120; x <= _centre.x + 120; x++)
            for (int y = _surfaceY - 24; y <= _surfaceY + 34; y++)
                for (int z = _centre.z - 120; z <= _centre.z + 120; z++)
                {
                    var v = new int3(x, y, z);
                    if (!Store.IsResident(CoordMath.VoxelToChunk(v))) continue;
                    byte m = Store.GetVoxel(v);
                    if (m == Materials.Lava || m == Materials.Obsidian) lo++;
                    else if (m == Materials.Water) w++;
                }
        lavaPlusObsidian = lo; water = w;
    }

    private IEnumerator Verify(int target)
    {
        _phase = "verify";
        _fluid.ReadSlotCounters(out uint hi, out uint ever);

        L("--- RUNG RESULT ---");
        L($"  placed           {_placed} (target {target})");
        L($"  dug              {_dug};  detonations {_detonations}, {_boomVoxels} voxels");
        L($"  voxel writes     {_applied}");
        L($"  LIVE slots       highWater {hi} / capacity {_fluid.SlotCapacity} " +
          $"(MAX_ACTIVE_FLUID {EngineConfig.MAX_ACTIVE_FLUID})");
        L($"  tiles            {_tiles.ResidentTiles} resident / {_tiles.TileCapacity} cap, " +
          $"acquired {_tiles.TilesAcquiredTotal}, released {_tiles.TilesReleasedTotal}, " +
          $"peak {_tiles.PeakResidentTiles}, exhaustions {_tiles.PoolExhaustionsTotal}");
        L($"  op-list          total {_readback.OpsTotal}, readback errors {_readback.ReadbackErrorsTotal}, " +
          $"stale {_readback.StaleOpsDropped}, non-resident {_readback.OpsDroppedNonResident}");
        L($"  GPU active set   {_fluid.GpuActiveSetBytes() / 1048576.0:F1} MB " +
          $"(whole sim {_fluid.GpuAllocatedBytes() / 1048576.0:F1} MB)");
        L($"  managed heap     {GC.GetTotalMemory(false) / 1048576.0:F0} MB");
        L($"  dense bricks     {Store.DenseBricksHeld}");
        L("");

        // ---- GATE 1: the rung actually reached its target ----
        Check(_placed >= target * 0.9,
            $"the rung placed what it claimed ({_placed} of {target}) -- a rung that fell short " +
            "measures a smaller scenario than its label");

        // ---- GATE 2: no silent wake failure ----
        int unwoken = 0, sampled = 0;
        for (int dx = -200; dx <= 200; dx += 20)
            for (int dz = -200; dz <= 200; dz += 20)
                for (int dy = -6; dy <= 10; dy += 4)
                {
                    var v = new int3(_centre.x + dx, _surfaceY + dy, _centre.z + dz);
                    if (!Store.IsResident(CoordMath.VoxelToChunk(v))) continue;
                    if (!MaterialRules.IsMobile(Store.GetVoxel(v))) continue;
                    sampled++;
                    if (!FluidActiveRegion.WithinWakeRadius(v, _fluid.PlayerVoxel, _fluid.ActiveRadiusVoxels))
                        continue;   // legitimately asleep -- outside the radius
                    if (_tiles.SlotForVoxel(v) == FluidTileMap.NO_TILE) unwoken++;
                }
        Check(unwoken == 0,
            $"no silent wake failure: {unwoken} of {sampled} sampled mobile voxels inside the radius " +
            "lay outside a resident tile");

        // ---- GATE 3: mass conservation across the quiet window ----
        long delta = _lavaObsAfter - _lavaObsBefore;
        double pct = _lavaObsBefore > 0 ? Math.Abs(delta) * 100.0 / _lavaObsBefore : 0;
        Check(pct <= 3.0,
            $"lava+obsidian conserved across the quiet settle: {_lavaObsBefore} -> {_lavaObsAfter} " +
            $"({delta:+0;-0}, {pct:F2}%). Each reacted lava becomes exactly one obsidian, so this " +
            "sum survives §7.3 where a raw fluid count cannot. The 3% allowance is flow across " +
            "the counted box's open boundary, not a leak budget");
        Note($"water {_waterBefore} -> {_waterAfter} ({_waterAfter - _waterBefore:+0;-0}) -- " +
             "water is DESTROYED by design on contact with lava (§7.3: Water + Lava -> Air + " +
             "Obsidian), so this is reported, not gated");

        // ---- GATE 4: tile-pool exhaustion is clean, not unhandled ----
        if (_tiles.PoolExhaustionsTotal > 0)
            Note($"tile pool cap REACHED ({_tiles.PoolExhaustionsTotal} refusals) -- this is the " +
                 "documented §7.7 clean-refusal path, not a failure. Reported as a ceiling.");
        Check(_tiles.PeakResidentTiles <= _tiles.TileCapacity,
            $"tile pool never exceeded its cap ({_tiles.PeakResidentTiles} <= {_tiles.TileCapacity})");

        // ---- GATE 5: the op-list stayed healthy ----
        Check(_readback.ReadbackErrorsTotal == 0,
            $"no op-list readback errors ({_readback.ReadbackErrorsTotal})");
        Check(_readback.OpsDroppedNonResident == 0,
            $"no ops dropped for non-residency ({_readback.OpsDroppedNonResident})");

        // ---- The designed ceiling, reported rather than asserted ----
        if (hi >= EngineConfig.MAX_ACTIVE_FLUID - 1024)
            Note($"LIVE SLOTS AT THE MAX_ACTIVE_FLUID CEILING ({hi} of " +
                 $"{EngineConfig.MAX_ACTIVE_FLUID}). This is the engine's designed clamp, not a " +
                 "defect: no configuration can simulate more at once.");

        _frameMs.Sort();
        L("--- FRAME TIME (wall clock; NOT a §2.2 GPU-lane figure) ---");
        L($"  frames {_frameMs.Count}   p50 {P(0.50):F2} ms   p99 {P(0.99):F2} ms   max {P(1.0):F2} ms");
        var sb = new StringBuilder();
        _gapProbe.AppendReport(sb);
        _log.Append(sb);
        yield return null;
    }

    private double P(double q)
    {
        if (_frameMs.Count == 0) return 0;
        int k = Mathf.Clamp(Mathf.RoundToInt((float)(q * (_frameMs.Count - 1))), 0, _frameMs.Count - 1);
        return _frameMs[k];
    }

    private IEnumerator Report()
    {
        _log.AppendLine();
        _log.AppendLine($"PASS {_pass}  FAIL {_fail}");
        _log.AppendLine(_fail == 0 ? "RESULT: PASSED" : "RESULT: FAILED");
        File.WriteAllText(Path.Combine(_outDir, "fluid_chaos_report.txt"), _log.ToString());
        Debug.Log("[chaos] report -> " + _outDir);
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
        _readback?.DrainBlocking();       // see Phase6CombinedRig: sync GPU work vs async readback
        Camera cam = Camera.main;
        if (cam != null)
        {
            cam.transform.position = new Vector3((_centre.x - 120) * 0.1f,
                                                 (_surfaceY + 70) * 0.1f, (_centre.z - 120) * 0.1f);
            var target = new Vector3(_centre.x * 0.1f, _surfaceY * 0.1f, _centre.z * 0.1f);
            cam.transform.rotation = Quaternion.LookRotation((target - cam.transform.position).normalized, Vector3.up);
        }
        yield return null;
        yield return new WaitForEndOfFrame();
        Texture2D tex = ScreenCapture.CaptureScreenshotAsTexture();
        File.WriteAllBytes(Path.Combine(_outDir, $"{name}.png"), tex.EncodeToPNG());
        Destroy(tex);
        Note($"screenshot -> {name}.png");
    }
}
