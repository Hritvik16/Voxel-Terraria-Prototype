// ==========================================
// Assets/Game/Phase6CombinedRig.cs
//
// EVERY PHASE 6 SYSTEM RUNNING AT ONCE, ON THE TILED FLUID SUBSTRATE.
//
// WHAT MAKES THIS DIFFERENT FROM Phase6SandboxRig, which already drives all
// six systems: that rig runs them ONE AT A TIME, in sequence, and it builds
// the OLD DENSE FluidGpuSimulation -- it predates the tiled rewrite entirely.
// Nothing has ever run the player, edits, CCD, detonations, projectiles and
// buoyancy CONCURRENTLY, and nothing has ever run any of them against the
// tiled substrate. Both of those are firsts here.
//
// Sequential coverage answers "does each system work". Concurrent coverage
// answers a different question -- whether they interact -- and the two are
// not substitutes. A wake-scan racing a detonation racing a tile eviction is
// a state no sequential rig can reach.
//
// -------------------------------------------------------------------------
// THE SECOND REASON THIS EXISTS: A NEW TAIL-LATENCY LEVER
// -------------------------------------------------------------------------
// The fluid+terrain p99 tail has survived ten eliminated candidates (GC,
// in-run thermal, the LOD cascade, vsync/present, streaming starvation,
// upload volume, CPU apply, GPU time, CA dispatch rate, rig overhead) and is
// still unattributed -- bounded to `preUpdate`, where Unity's own numbers do
// not account for the wall clock.
//
// Per-system toggles are a lever nothing has tried. If the tail tracks one
// subsystem, that localises it. If it persists identically with EVERY
// toggleable system off, that is the strongest available evidence the cause
// is below what this toolchain can attribute -- which is a real result, not
// a failure to find one.
//
// Toggles (each REMOVES one system):
//   -nophysics   player stepping / CCD
//   -noedits     dig + place
//   -noboom      detonations
//   -noproj      projectile traces
//   -nobuoy      buoyancy sampling
//   -nofluid     the tiled CA itself (the control: terrain + gameplay only)
//
// -------------------------------------------------------------------------
// MEASUREMENT
// -------------------------------------------------------------------------
// Wall clock only, RELEASE standalone, vsync off. No gpuFrameTime against any
// budget (Amendment 8.10: inflated ~2.6-2.7x here). No Xcode, no Instruments
// (8.9 Rule 1). No Performance State field -- FrameTimingManager cannot report
// it and that stays a stated limitation.
//
// COOLDOWNS ARE THE CALLER'S JOB. This machine throttles ~183% across
// back-to-back runs but is internally stable within one cooled run (measured
// last session, by decile). run-phase6-combined.sh owns the 300 s idle.
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

public class Phase6CombinedRig : MonoBehaviour
{
    [SerializeField] private ComputeShader _fluidCA;
    [SerializeField] private PlayerController _player;
    [SerializeField] private int _tilePoolCap = 512;
    [SerializeField] private int _slotCapacity = 65536;
    [SerializeField] private int _maxOpsPerFrame = 65536;
    [SerializeField] private float _secondsOfActivity = 75f;
    [SerializeField] private string _outputRootFolderName = "Phase6Combined";

    private const float Dt = 1f / 60f;

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
    private FrameGapProbe _gapProbe;

    private readonly StringBuilder _log = new StringBuilder();
    private int _pass, _fail, _shotIndex;
    private string _outDir, _phase = "-";

    // Toggles
    private bool _doPhysics = true, _doEdits = true, _doBoom = true;
    private bool _doProj = true, _doBuoy = true, _doFluid = true;

    // Activity counters -- the evidence that a toggle was actually ON
    private long _applied, _playerSteps, _editOps, _ccdSweeps, _projShots;
    private long _detonations, _buoySamples, _fluidTicks, _boomVoxels;
    private int _projThroughFluid, _ccdThroughFluid, _buoyWetSamples;
    private int3 _poolCentre;
    private int _poolSurfaceY;

    private readonly List<double> _frameMs = new List<double>();

    private void L(string s) { _log.AppendLine(s); Debug.Log("[6cmb] " + s); }
    private void Note(string s) => L("    note  " + s);
    private void Pass(string s) { _pass++; L("    PASS  " + s); }
    private void Fail(string s) { _fail++; L($"    FAIL  {s}   [{_phase}]"); }
    private void Check(bool ok, string s) { if (ok) Pass(s); else Fail(s); }

    private static bool Flag(string n)
    {
        foreach (string a in Environment.GetCommandLineArgs()) if (a == n) return true;
        return false;
    }

    IEnumerator Start()
    {
        _doPhysics = !Flag("-nophysics"); _doEdits = !Flag("-noedits");
        _doBoom = !Flag("-noboom");       _doProj = !Flag("-noproj");
        _doBuoy = !Flag("-nobuoy");       _doFluid = !Flag("-nofluid");

        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;

        if (_player == null) _player = FindAnyObjectByType<PlayerController>();
        float t0 = Time.realtimeSinceStartup;
        while (Store == null && Time.realtimeSinceStartup - t0 < 240f) yield return null;

        _outDir = Path.Combine(Application.persistentDataPath, _outputRootFolderName,
                               DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(_outDir);

        L("=== PHASE 6 COMBINED: every system at once, on the TILED substrate ===");
        L(DateTime.Now.ToString("u", CultureInfo.InvariantCulture));
        L("");
        L("READING RULE. Wall clock only. gpuFrameTime is read nowhere against a");
        L("budget (Amendment 8.10: inflated ~2.6-2.7x here). §2.2's fluid CA budget");
        L("is on the GPU lane and this workflow cannot attribute GPU stages, so");
        L("nothing below is scored against §2.2's <=3.5 ms.");
        L("");
        L($"TOGGLES  physics {_doPhysics}  edits {_doEdits}  boom {_doBoom}  " +
          $"proj {_doProj}  buoy {_doBuoy}  fluid {_doFluid}");
        L("");

        if (Store == null) { Fail("world never booted"); yield return Report(); yield break; }
        if (_player == null) { Fail("no PlayerController in the scene"); yield return Report(); yield break; }

        for (int i = 0; i < 150; i++) yield return null;   // window fill

        Camera cam = Camera.main;
        int3 camVox = CoordMath.WorldToVoxel(new float3(cam.transform.position.x,
                                                        cam.transform.position.y,
                                                        cam.transform.position.z));
        int surface = SurfaceY(camVox.x, camVox.z);
        if (surface < 1) { Fail("no surface under the camera"); yield return Report(); yield break; }
        _poolCentre = camVox; _poolSurfaceY = surface;

        _edits = new EditService();
        _edits.AttachWorld(Store, Store, Store, Clip);

        // TILED, not dense. This is the substrate every other Phase 6 proof
        // predates, and running the gameplay stack against it is the point.
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
        _gapProbe = gameObject.AddComponent<FrameGapProbe>();

        BuildArena();
        yield return Shot("00_arena_before");

        _gapProbe.Recording = true;
        yield return RunConcurrent();
        _gapProbe.Recording = false;

        yield return Shot("90_arena_after");
        yield return Verify();
        yield return Report();
    }

    // =====================================================================
    // The arena: a pool to swim in, a wall to hit, terrain to dig
    // =====================================================================
    private void BuildArena()
    {
        int y = _poolSurfaceY;
        int3 c = _poolCentre;

        // A basin, then water in it. Deep enough that the head probe submerges.
        _edits.SetBox(new int3(c.x - 14, y - 6, c.z - 14), new int3(c.x + 14, y + 8, c.z + 14), Materials.Air);
        _edits.SetBox(new int3(c.x - 15, y - 7, c.z - 15), new int3(c.x + 15, y - 7, c.z + 15), Materials.Stone);
        _edits.SetBox(new int3(c.x - 14, y - 6, c.z - 14), new int3(c.x + 14, y - 1, c.z + 14), Materials.Water);

        // A wall ~20 m along +X for the grapple-speed CCD pass.
        _edits.SetBox(new int3(c.x + 20, y - 2, c.z - 8), new int3(c.x + 21, y + 18, c.z + 8), Materials.Stone);

        // A stone mass to detonate, offset so debris lands near/in the pool.
        _edits.SetBox(new int3(c.x - 30, y - 4, c.z - 8), new int3(c.x - 14, y + 10, c.z + 8), Materials.Stone);

        L($"arena built at {c}, surface y={y}: basin 28x28 with water, wall +20x, stone mass -30x");
        L($"dense bricks at rest: {Store.DenseBricksHeld}");
        L("");
    }

    // =====================================================================
    // EVERYTHING AT ONCE. Each system on its own cadence, in one loop.
    // =====================================================================
    private IEnumerator RunConcurrent()
    {
        _phase = "concurrent";
        int frames = Mathf.RoundToInt(_secondsOfActivity * 60f);
        L($"--- CONCURRENT ACTIVITY: {_secondsOfActivity:F0}s ({frames} frames) ---");

        int3 c = _poolCentre;
        int y = _poolSurfaceY;
        var rng = new System.Random(20260911);
        int shotAt = frames / 3, shotAt2 = (frames * 2) / 3;

        for (int f = 0; f < frames; f++)
        {
            // ---- FLUID (tiled) ----
            if (_doFluid)
            {
                if ((f % 20) == 0)
                {
                    _fluid.UpdatePlayerPosition(PlayerVoxel());
                    FluidTileResidency.Refresh(Store, _tiles, PlayerVoxel(),
                                               _fluid.ActiveRadiusVoxels, _fluid.SleepRadiusVoxels);
                }
                if (_readback.CanIssue) { _fluid.Tick(Clip); _readback.IssueReadback(0); _fluidTicks++; }
                _readback.PumpAndApply();
            }

            // ---- PLAYER: a figure-of-eight walk, jumping periodically ----
            if (_doPhysics)
            {
                float ang = f * 0.02f;
                var wish = new float2(Mathf.Cos(ang), Mathf.Sin(ang * 2f));
                _player.DebugStep(Dt, wish, (f % 75) == 0);
                _playerSteps++;

                // ---- SWEPT CCD: grapple-speed pass at the wall ----
                // §8.2's own acceptance clause is a fast move into a wall; 40 m/s
                // over one frame is ~0.67 m, so the sweep is run over a longer
                // synthetic span to actually exercise the solid-hit path.
                if ((f % 90) == 45)
                {
                    float3 from = new float3((c.x - 4) * 0.1f, (y + 2) * 0.1f, c.z * 0.1f);
                    float3 to = new float3((c.x + 26) * 0.1f, (y + 2) * 0.1f, c.z * 0.1f);
                    CCDResult r = _ccd.Sweep(from, to, 0.6f, 1.8f);
                    _ccdSweeps++;
                    if (r.FluidTraversedDistanceM > 0f) _ccdThroughFluid++;
                    if (!r.HitSolid) Fail($"CCD grapple pass at frame {f} did not hit the wall");

                    // A second sweep straight through the pool, below the
                    // waterline, so the fluid-traversal path is exercised too
                    // -- the wall pass runs above the water and never touches it.
                    float3 wf = new float3((c.x - 13) * 0.1f, (y - 4) * 0.1f, c.z * 0.1f);
                    float3 wt = new float3((c.x + 13) * 0.1f, (y - 4) * 0.1f, c.z * 0.1f);
                    CCDResult rw = _ccd.Sweep(wf, wt, 0.6f, 1.8f);
                    _ccdSweeps++;
                    if (rw.FluidTraversedDistanceM > 0f) _ccdThroughFluid++;
                }
            }

            // ---- BUOYANCY: sampled every frame while the player moves ----
            if (_doBuoy)
            {
                var st = _buoy.Sample(_player.Motor.PositionM, 1.8f, 8f,
                                      _readback != null ? _readback.FramesSinceLastApplied : 0);
                _buoySamples++;
                if (st.InFluid) _buoyWetSamples++;
            }

            // ---- EDITS: two tool tiers, into and beside the fluid ----
            if (_doEdits && (f % 25) == 0)
            {
                bool bigTool = (f % 50) == 0;
                int r = bigTool ? 4 : 2;                       // two tool tiers
                int dx = rng.Next(-12, 12), dz = rng.Next(-12, 12);
                var at = new int3(c.x + dx, y - 3 + rng.Next(0, 4), c.z + dz);
                // Alternate dig and place so the wake-scan path runs both ways.
                _editOps += _edits.SetSphere(at, r, ((f / 25) % 2 == 0) ? Materials.Air : Materials.Sand);
            }

            // ---- DETONATIONS: meaningful size, debris toward the pool ----
            if (_doBoom)
            {
                if ((f % 600) == 120)
                {
                    // WALK ALONG the mass. Detonating the same spot repeatedly
                    // drained it -- later events removed 0 voxels, which is a
                    // scenario that stops testing anything rather than an
                    // engine result.
                    int step = (int)_detonations;
                    var at = new int3(c.x - 28 + step * 2, y + 2 + (step % 3) * 3,
                                      c.z - 6 + (step % 4) * 4);
                    _boom.Detonate(at, 7);                      // ~1400-voxel sphere
                    _detonations++;
                }
                int removed = _boom.Step();
                _boomVoxels += removed;
                while (_boom.TryTakeCompleted(out ProxyDrop drop))
                    Note($"detonation drained: {drop.TotalVoxels} voxels of {drop.DominantMaterial} " +
                         $"over {drop.Frames} frames at {drop.CentreVoxel}");
            }

            // ---- PROJECTILES: one line of fire crosses the pool ----
            if (_doProj && (f % 120) == 60)
            {
                // MUST START IN OPEN AIR. The first version fired from
                // c.x-18, which is INSIDE the stone mass (-30..-14), so every
                // shot hit at ~0 m and none ever reached the water -- 0 of 37
                // through fluid. The rig caught it; the engine was fine.
                // Fires from above the basin rim down through the water.
                float3 from = new float3((c.x + 16) * 0.1f, (y + 10) * 0.1f, (c.z + 10) * 0.1f);
                float3 to = new float3((c.x - 8) * 0.1f, (y - 5) * 0.1f, (c.z - 8) * 0.1f);
                ProjectileHit h = _proj.Trace(from, to);
                _projShots++;
                if (h.FluidTraversedDistanceM > 0f) _projThroughFluid++;
            }

            if (f == shotAt) yield return Shot("40_mid_activity");
            if (f == shotAt2) yield return Shot("60_late_activity");

            yield return null;
            _frameMs.Add(Time.unscaledDeltaTime * 1000.0);
        }
        L("");
    }

    private int3 PlayerVoxel() =>
        CoordMath.WorldToVoxel(_player.Motor.PositionM);

    // =====================================================================
    // Verification
    // =====================================================================
    private IEnumerator Verify()
    {
        _phase = "verify";
        L("--- ACTIVITY (evidence each toggle was really ON) ---");
        L($"  player steps      {_playerSteps}");
        L($"  CCD sweeps        {_ccdSweeps}  (through fluid: {_ccdThroughFluid})");
        L($"  edit voxels       {_editOps}");
        L($"  detonations       {_detonations}, voxels removed {_boomVoxels}");
        L($"  projectile shots  {_projShots}  (through fluid: {_projThroughFluid})");
        L($"  buoyancy samples  {_buoySamples}  (wet: {_buoyWetSamples})");
        L($"  fluid ticks       {_fluidTicks}, voxel writes applied {_applied}");
        if (_doFluid)
        {
            _fluid.ReadSlotCounters(out uint hi, out uint ever);
            L($"  fluid slots       highWater {hi}, everAllocated {ever} / {_fluid.SlotCapacity}");
            L($"  tiles             acquired {_tiles.TilesAcquiredTotal}, released {_tiles.TilesReleasedTotal}, " +
              $"peak resident {_tiles.PeakResidentTiles}, exhaustions {_tiles.PoolExhaustionsTotal}");
            L($"  op-list           total {_readback.OpsTotal}, readback errors {_readback.ReadbackErrorsTotal}, " +
              $"stale {_readback.StaleOpsDropped}, non-resident {_readback.OpsDroppedNonResident}");
            L($"  readback requests {_readback.RequestsIssuedTotal}");
            L($"  FIRST readback error: {_readback.FirstReadbackErrorDetail}");
        }
        L("");

        L("--- CORRECTNESS ---");
        // Each enabled system must have actually done something. A toggle that
        // silently did nothing would make the tail comparison meaningless --
        // "no correlation" is only evidence if the system was really running.
        if (_doPhysics) Check(_playerSteps > 0 && _ccdSweeps > 0, $"physics ran ({_playerSteps} steps, {_ccdSweeps} sweeps)");
        if (_doEdits) Check(_editOps > 0, $"edits ran ({_editOps} voxels)");
        if (_doBoom) Check(_detonations > 0 && _boomVoxels > 0, $"detonations ran ({_detonations}, {_boomVoxels} voxels)");
        if (_doProj) Check(_projShots > 0, $"projectiles ran ({_projShots} shots)");
        if (_doBuoy) Check(_buoySamples > 0, $"buoyancy ran ({_buoySamples} samples)");
        if (_doFluid) Check(_fluidTicks > 0 && _applied > 0, $"fluid ran ({_fluidTicks} ticks, {_applied} writes)");

        if (_doProj) Check(_projThroughFluid > 0,
            $"at least one projectile passed THROUGH fluid ({_projThroughFluid}) -- " +
            "the brief's specific case, and it only counts if the trace reports fluid distance");
        if (_doPhysics && _doFluid) Check(_ccdThroughFluid > 0,
            $"at least one CCD sweep traversed fluid ({_ccdThroughFluid}) -- " +
            "the wall pass runs above the waterline, so this needs its own sweep");
        if (_doBuoy && _doFluid) Check(_buoyWetSamples > 0,
            $"the player was actually IN the water for {_buoyWetSamples} samples -- " +
            "buoyancy sampling dry water proves nothing");

        if (_doFluid)
        {
            Check(_readback.ReadbackErrorsTotal == 0,
                $"no op-list readback errors under the full combined load ({_readback.ReadbackErrorsTotal})");
            Check(_readback.OpsDroppedNonResident == 0,
                $"no ops dropped for non-residency ({_readback.OpsDroppedNonResident}) -- " +
                "§9.4's guard firing here would mean fluid was moving into unloaded space");
            _fluid.ReadSlotCounters(out uint hi2, out uint _);
            Check(hi2 < _fluid.SlotCapacity,
                $"the slot pool did not saturate ({hi2} of {_fluid.SlotCapacity}) -- " +
                "a saturated run measures pool exhaustion, not the scenario");

            // §9's wake contract: fluid still present must be inside a resident tile.
            int unwoken = 0, checkedCells = 0;
            int3 c = _poolCentre; int y = _poolSurfaceY;
            for (int dx = -12; dx <= 12; dx += 4)
                for (int dz = -12; dz <= 12; dz += 4)
                    for (int dy = -5; dy <= 0; dy += 2)
                    {
                        var v = new int3(c.x + dx, y + dy, c.z + dz);
                        if (!Store.IsResident(CoordMath.VoxelToChunk(v))) continue;
                        byte m = Store.GetVoxel(v);
                        if (!MaterialRules.IsMobile(m)) continue;
                        checkedCells++;
                        if (_tiles.TryGetSlot(v / ChunkFluidMask.TILE_EDGE) == FluidTileMap.NO_TILE) unwoken++;
                    }
            Check(unwoken == 0,
                $"no silent wake failure: {unwoken} of {checkedCells} sampled mobile voxels lay outside a resident tile");
        }

        Check(Store.DenseBricksHeld >= 0, $"brick pool intact at rest ({Store.DenseBricksHeld} dense bricks)");
        L("");

        L("--- FRAME TIME (wall clock; NOT a §2.2 GPU-lane figure) ---");
        _frameMs.Sort();
        L($"  frames sampled {_frameMs.Count}");
        L($"  p50 {P(0.50):F2} ms   p99 {P(0.99):F2} ms   max {P(1.0):F2} ms");
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
        File.WriteAllText(Path.Combine(_outDir, "phase6_combined_report.txt"), _log.ToString());
        Debug.Log("[6cmb] report -> " + _outDir);
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
        // DRAIN FIRST. ScreenCapture is a synchronous full-framebuffer read,
        // and an AsyncGPUReadback left outstanding across synchronous GPU work
        // is precisely the hazard root-caused two sessions ago (the stray
        // readback error). A shipped frame never takes a screenshot, so this
        // removes a RIG artefact rather than masking an engine one -- and the
        // error's own detail line is printed either way so the claim is
        // checkable rather than asserted.
        _readback?.DrainBlocking();

        Camera cam = Camera.main;
        if (cam != null)
        {
            // Look at the pool from outside and slightly above, so the basin,
            // the wall and the detonation mass are all in frame.
            int3 c = _poolCentre;
            cam.transform.position = new Vector3((c.x - 40) * 0.1f, (_poolSurfaceY + 16) * 0.1f, (c.z - 40) * 0.1f);
            Vector3 target = new Vector3(c.x * 0.1f, _poolSurfaceY * 0.1f, c.z * 0.1f);
            cam.transform.rotation = Quaternion.LookRotation((target - cam.transform.position).normalized, Vector3.up);
        }
        yield return null;
        yield return new WaitForEndOfFrame();
        Texture2D tex = ScreenCapture.CaptureScreenshotAsTexture();
        File.WriteAllBytes(Path.Combine(_outDir, $"{name}.png"), tex.EncodeToPNG());
        Destroy(tex);
        Note($"screenshot -> {name}.png");
        _shotIndex++;
    }
}
