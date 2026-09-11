// ==========================================
// Assets/Game/Phase6QaRig.cs
//
// PERFORMANCE + VISUAL QA ACROSS EVERY PHASE 6 SYSTEM, ONE MODE AT A TIME.
//
// WHY THIS EXISTS. Fluid has had five sessions of scrutiny. The other five
// Phase 6 systems have had none of comparable depth: they are covered for
// CORRECTNESS by their own rigs, but nobody has ever asked whether continuous
// digging, or back-to-back detonations, has its own frame-time tail. If it
// does, that is exactly as important as fluid's and has been invisible the
// whole time because nothing looked.
//
// ONE RIG, MANY MODES, because building nine separate rigs would take longer
// than running them and would drift apart. -qamode selects:
//
//   INDIVIDUAL (fluid off unless the mode needs it):
//     player        continuous walk / jump / 3-voxel steps
//     ccd           repeated grapple-speed sweeps into a wall
//     edits         continuous digging at the MAX tool tier (bore, 200 vox/s)
//     projectile    rapid-fire DDA traces
//     destruction   back-to-back large detonations
//     buoyancy      extended swimming in a deep pool
//
//   ORGANIC (the user's own observation, step 1):
//     organic       sustained BRUSH PLACEMENT -- not vents -- while walking,
//                   for 2+ minutes, fluid accumulating naturally
//
//   PAIRWISE (nothing has ever isolated pairs):
//     dig+fluid     heavy digging into live fluid
//     boom+swim     detonations while submerged
//     ccd+edits     grapple passes through terrain being actively dug
//
//   INVESTIGATION:
//     boundary      pours fluid straddling the §7.4 radius edge, then
//                   censuses every fluid voxel against tile residency to
//                   decide whether scattered voxels near the boundary are
//                   correct sleep behaviour or a released-tile defect
//
// MEASUREMENT: wall clock only. No gpuFrameTime against a budget (Amendment
// 8.10). No Xcode, no Instruments (8.9 Rule 1). No Performance State field.
// Cooldowns are the runner's job -- this machine throttles ~183% back-to-back.
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

public class Phase6QaRig : MonoBehaviour
{
    [SerializeField] private ComputeShader _fluidCA;
    [SerializeField] private PlayerController _player;
    [SerializeField] private int _tilePoolCap = EngineConfig.FLUID_TILE_POOL_CAPACITY;
    [SerializeField] private int _slotCapacity = 65536;
    [SerializeField] private int _maxOpsPerFrame = 65536;
    [SerializeField] private string _outputRootFolderName = "Phase6Qa";

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
    private FrameGapProbe _gap;

    private readonly StringBuilder _log = new StringBuilder();
    private int _pass, _fail;
    private string _outDir, _mode = "organic";
    private int3 _centre; private int _surfaceY;
    private long _applied, _dug, _placed, _sweeps, _shots, _dets, _boomVox, _buoyWet, _steps;
    private readonly List<double> _frameMs = new List<double>();

    private void L(string s) { _log.AppendLine(s); Debug.Log("[qa] " + s); }
    private void Note(string s) => L("    note  " + s);
    private void Pass(string s) { _pass++; L("    PASS  " + s); }
    private void Fail(string s) { _fail++; L("    FAIL  " + s); }
    private void Check(bool ok, string s) { if (ok) Pass(s); else Fail(s); }

    private static string Arg(string f, string d)
    {
        string[] a = Environment.GetCommandLineArgs();
        for (int i = 0; i < a.Length - 1; i++)
            if (string.Equals(a[i], f, StringComparison.OrdinalIgnoreCase)) return a[i + 1];
        return d;
    }

    private bool NeedsFluid =>
        _mode == "organic" || _mode == "buoyancy" || _mode == "dig+fluid" ||
        _mode == "boom+swim" || _mode == "boundary";

    IEnumerator Start()
    {
        _mode = Arg("-qamode", "organic");
        int seconds = int.Parse(Arg("-qaseconds", _mode == "organic" ? "130" : "65"),
                                CultureInfo.InvariantCulture);

        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;

        if (_player == null) _player = FindAnyObjectByType<PlayerController>();
        float t0 = Time.realtimeSinceStartup;
        while (Store == null && Time.realtimeSinceStartup - t0 < 240f) yield return null;

        _outDir = Path.Combine(Application.persistentDataPath, _outputRootFolderName,
                               $"{DateTime.Now:yyyyMMdd_HHmmss}_{_mode.Replace('+', '-')}");
        Directory.CreateDirectory(_outDir);

        L($"=== PHASE 6 QA -- mode '{_mode}', {seconds}s ===");
        L(DateTime.Now.ToString("u", CultureInfo.InvariantCulture));
        L("Wall clock only; gpuFrameTime read nowhere against a budget (Amdt 8.10).");
        L($"fluid: {(NeedsFluid ? "ON (this mode needs it)" : "OFF -- isolating this system")}");
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

        if (NeedsFluid)
        {
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
        }

        _ccd = new SweptCCD(Store, Store);
        _proj = new ProjectileTrace(Store, Store);
        _boom = new DestructionReducer(_edits, Store);
        _buoy = new Buoyancy(Store, Store);
        _player.DebugTakeControl();
        _player.Bind(Store, Store);
        _gap = gameObject.AddComponent<FrameGapProbe>();

        BuildArena();
        yield return Shot("00_start");

        _gap.Recording = true;
        yield return Drive(seconds * 60);
        _gap.Recording = false;

        yield return Shot("90_end");
        yield return Verify();
        yield return Report();
    }

    private void BuildArena()
    {
        int y = _surfaceY; int3 c = _centre;
        // A deep pool (swimming + buoyancy), a wall (CCD), a mass (detonation).
        _edits.SetBox(new int3(c.x - 12, y - 10, c.z - 12), new int3(c.x + 12, y + 8, c.z + 12), Materials.Air);
        _edits.SetBox(new int3(c.x - 13, y - 11, c.z - 13), new int3(c.x + 13, y - 11, c.z + 13), Materials.Stone);
        if (NeedsFluid)
            _edits.SetBox(new int3(c.x - 12, y - 10, c.z - 12), new int3(c.x + 12, y - 1, c.z + 12), Materials.Water);
        _edits.SetBox(new int3(c.x + 20, y - 2, c.z - 8), new int3(c.x + 21, y + 18, c.z + 8), Materials.Stone);
        _edits.SetBox(new int3(c.x - 34, y - 6, c.z - 10), new int3(c.x - 16, y + 12, c.z + 10), Materials.Stone);
        L($"arena at {c}, surface y={y}");
        L("");
    }

    private IEnumerator Drive(int frames)
    {
        var rng = new System.Random(20260911);
        var bore = EditService.Tiers[EditService.Tiers.Length - 1];   // max tier
        var budget = new EditService.ToolBudget();
        int3 c = _centre; int y = _surfaceY;
        int shotMid = frames / 2;

        for (int f = 0; f < frames; f++)
        {
            int3 pv = CoordMath.WorldToVoxel(_player.Motor.PositionM);

            if (NeedsFluid)
            {
                if ((f % 20) == 0)
                {
                    _fluid.UpdatePlayerPosition(pv);
                    FluidTileResidency.Refresh(Store, _tiles, pv,
                                               _fluid.ActiveRadiusVoxels, _fluid.SleepRadiusVoxels);
                }
                if (_readback.CanIssue) { _fluid.Tick(Clip); _readback.IssueReadback(0); }
                _readback.PumpAndApply();
            }

            bool wantPlayer = _mode == "player" || _mode == "organic" || _mode == "buoyancy" ||
                              _mode == "boom+swim" || _mode == "dig+fluid" || _mode == "ccd+edits";
            if (wantPlayer)
            {
                float ang = f * 0.018f;
                _player.DebugStep(Dt, new float2(Mathf.Cos(ang), Mathf.Sin(ang * 1.6f)), (f % 70) == 0);
                _steps++;
            }

            // ---- CONTINUOUS DIGGING AT MAX TIER ----
            if (_mode == "edits" || _mode == "dig+fluid" || _mode == "ccd+edits")
            {
                int allowed = budget.Accrue(Dt, bore.VoxelsPerSecond);
                if (allowed > 0)
                {
                    var at = new int3(c.x - 25 + rng.Next(-6, 6), y + rng.Next(-4, 8), c.z + rng.Next(-8, 8));
                    _dug += _edits.SetSphere(at, bore.RadiusVoxels, Materials.Air);
                }
            }

            // ---- ORGANIC: SUSTAINED BRUSH PLACEMENT, NOT VENTS ----
            // The distinction matters. Playground's vents meter ONE voxel per
            // frame by design (Emit(ref budget,...)), so a vent can never
            // produce a burst. A brush places a whole sphere at once, which is
            // what a player actually does and what the toggle sweep never did.
            if (_mode == "organic" && (f % 12) == 0)
            {
                byte m = (f / 12) % 3 == 0 ? Materials.Water
                       : (f / 12) % 3 == 1 ? Materials.Sand : Materials.Lava;
                var at = new int3(pv.x + rng.Next(-10, 10), pv.y + 6, pv.z + rng.Next(-10, 10));
                _placed += _edits.SetSphere(at, 3, m);
            }

            // ---- GRAPPLE-SPEED CCD ----
            if (_mode == "ccd" || _mode == "ccd+edits")
            {
                if ((f % 6) == 0)
                {
                    float3 a = new float3((c.x - 4) * 0.1f, (y + 2) * 0.1f, c.z * 0.1f);
                    float3 b = new float3((c.x + 26) * 0.1f, (y + 2) * 0.1f, c.z * 0.1f);
                    CCDResult r = _ccd.Sweep(a, b, 0.6f, 1.8f);
                    _sweeps++;
                    if (!r.HitSolid) Fail($"CCD sweep at frame {f} missed the wall");
                }
            }

            // ---- RAPID-FIRE PROJECTILES ----
            if (_mode == "projectile" && (f % 3) == 0)
            {
                float3 a = new float3((c.x + 16) * 0.1f, (y + 10) * 0.1f, (c.z + 10) * 0.1f);
                float3 b = new float3((c.x - 8) * 0.1f, (y - 5) * 0.1f, (c.z - 8) * 0.1f);
                _proj.Trace(a, b); _shots++;
            }

            // ---- BACK-TO-BACK LARGE DETONATIONS ----
            if (_mode == "destruction" || _mode == "boom+swim")
            {
                if ((f % 45) == 10)
                {
                    var at = new int3(c.x - 25 + (int)(_dets % 8) * 2, y + 2 + (int)(_dets % 4) * 3,
                                      c.z - 6 + (int)(_dets % 5) * 3);
                    _boom.Detonate(at, 10); _dets++;
                }
                _boomVox += _boom.Step();
                while (_boom.TryTakeCompleted(out ProxyDrop _)) { }
            }

            // ---- SWIMMING / BUOYANCY ----
            if (_mode == "buoyancy" || _mode == "boom+swim")
            {
                var st = _buoy.Sample(_player.Motor.PositionM, 1.8f, 8f,
                                      _readback != null ? _readback.FramesSinceLastApplied : 0);
                if (st.InFluid) _buoyWet++;
            }

            // ---- BOUNDARY INVESTIGATION ----
            if (_mode == "boundary" && f == 60)
            {
                // Straddle the wake radius on purpose: half inside, half out.
                int r = _fluid.ActiveRadiusVoxels;
                for (int d = -20; d <= 20; d += 4)
                {
                    var at = new int3(c.x + r + d, y + 6, c.z);
                    _placed += _edits.SetSphere(at, 3, Materials.Water);
                }
                Note($"poured a line of water straddling the wake radius ({r} voxels) at x={c.x + r}");
            }

            if (f == shotMid) yield return Shot("50_mid");
            yield return null;
            _frameMs.Add(Time.unscaledDeltaTime * 1000.0);
        }
        L("");
    }

    private IEnumerator Verify()
    {
        L("--- ACTIVITY ---");
        L($"  player steps {_steps}   dug {_dug}   placed {_placed}");
        L($"  CCD sweeps {_sweeps}   projectiles {_shots}   detonations {_dets} ({_boomVox} voxels)");
        L($"  buoyancy wet samples {_buoyWet}");
        if (NeedsFluid)
        {
            _fluid.ReadSlotCounters(out uint hi, out uint _);
            L($"  fluid: {_applied} writes, live slots peak {hi}, tiles {_tiles.ResidentTiles}/" +
              $"{_tiles.TileCapacity}, exhaustions {_tiles.PoolExhaustionsTotal}");
            L($"  op-list: readback errors {_readback.ReadbackErrorsTotal}, " +
              $"non-resident dropped {_readback.OpsDroppedNonResident}");
            Check(_readback.ReadbackErrorsTotal == 0, $"no readback errors ({_readback.ReadbackErrorsTotal})");
        }
        L("");

        // The mode must actually have exercised its system, or the profile is
        // of an idle frame loop wearing the mode's name.
        switch (_mode)
        {
            case "player": Check(_steps > 0, $"player stepped ({_steps})"); break;
            case "ccd": Check(_sweeps > 0, $"CCD swept ({_sweeps})"); break;
            case "edits": case "dig+fluid": Check(_dug > 0, $"dug ({_dug} voxels)"); break;
            case "projectile": Check(_shots > 0, $"fired ({_shots})"); break;
            case "destruction": Check(_dets > 0 && _boomVox > 0, $"detonated ({_dets}, {_boomVox} voxels)"); break;
            case "buoyancy": Check(_buoyWet > 0, $"was in water ({_buoyWet} samples)"); break;
            case "organic": Check(_placed > 0, $"placed by brush ({_placed} voxels)"); break;
            case "boom+swim": Check(_dets > 0 && _buoyWet > 0, $"detonated {_dets} while wet {_buoyWet}"); break;
            case "ccd+edits": Check(_sweeps > 0 && _dug > 0, $"swept {_sweeps} while digging {_dug}"); break;
        }

        if (_mode == "boundary") BoundaryCensus();

        _frameMs.Sort();
        L("--- FRAME TIME (wall clock) ---");
        L($"  frames {_frameMs.Count}   p50 {P(0.50):F2}   p99 {P(0.99):F2}   max {P(1.0):F2} ms");
        var sb = new StringBuilder();
        _gap.AppendReport(sb);
        _log.Append(sb);
        yield return null;
    }

    /// Is scattered water near the radius edge correct sleep behaviour, or a
    /// tile released while still holding live fluid?
    ///
    /// The two look identical on screen. They are distinguishable in state:
    /// fluid OUTSIDE the wake radius is SUPPOSED to sit as static terrain with
    /// no tile (that is §7.4 working). Fluid INSIDE the radius with no
    /// resident tile would be a genuine wake failure.
    private void BoundaryCensus()
    {
        int inRadiusNoTile = 0, inRadiusTiled = 0, outRadiusNoTile = 0, outRadiusTiled = 0;
        int r = _fluid.ActiveRadiusVoxels;
        int3 c = _centre;
        for (int dx = r - 40; dx <= r + 40; dx++)
            for (int dy = -6; dy <= 14; dy++)
                for (int dz = -8; dz <= 8; dz++)
                {
                    var v = new int3(c.x + dx, _surfaceY + dy, c.z + dz);
                    if (!Store.IsResident(CoordMath.VoxelToChunk(v))) continue;
                    if (!MaterialRules.IsMobile(Store.GetVoxel(v))) continue;
                    bool inR = FluidActiveRegion.WithinWakeRadius(v, _fluid.PlayerVoxel, r);
                    bool tiled = _tiles.SlotForVoxel(v) != FluidTileMap.NO_TILE;
                    if (inR && tiled) inRadiusTiled++;
                    else if (inR) inRadiusNoTile++;
                    else if (tiled) outRadiusTiled++;
                    else outRadiusNoTile++;
                }
        L("--- BOUNDARY CENSUS (mobile voxels near the wake radius) ---");
        L($"  inside radius, HAS tile    {inRadiusTiled}   (correct: simulating)");
        L($"  inside radius, NO tile     {inRadiusNoTile}   (would be a WAKE FAILURE)");
        L($"  outside radius, no tile    {outRadiusNoTile}   (correct: §7.4 sleep, static terrain)");
        L($"  outside radius, HAS tile   {outRadiusTiled}   (benign: tile not yet released)");
        Check(inRadiusNoTile == 0,
            $"no mobile voxel inside the wake radius lacks a tile ({inRadiusNoTile}) -- " +
            "scattered fluid OUTSIDE the radius is §7.4 working, not a defect");
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
        File.WriteAllText(Path.Combine(_outDir, "qa_report.txt"), _log.ToString());
        Debug.Log("[qa] report -> " + _outDir);
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
        _readback?.DrainBlocking();   // sync capture vs async readback; see Phase6CombinedRig
        Camera cam = Camera.main;
        if (cam != null)
        {
            cam.transform.position = new Vector3((_centre.x - 38) * 0.1f,
                                                 (_surfaceY + 20) * 0.1f, (_centre.z - 38) * 0.1f);
            var t = new Vector3(_centre.x * 0.1f, _surfaceY * 0.1f, _centre.z * 0.1f);
            cam.transform.rotation = Quaternion.LookRotation((t - cam.transform.position).normalized, Vector3.up);
        }
        yield return null;
        yield return new WaitForEndOfFrame();
        Texture2D tex = ScreenCapture.CaptureScreenshotAsTexture();
        File.WriteAllBytes(Path.Combine(_outDir, $"{name}.png"), tex.EncodeToPNG());
        Destroy(tex);
    }
}
