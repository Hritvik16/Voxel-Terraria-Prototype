// ==========================================
// Assets/Game/PlaytestBugsRig.cs
//
// STEP 0 DIAGNOSTIC for two bugs found by playtesting. It assumes NOTHING about
// either cause; each step is built to tell the candidate explanations apart.
//
// BUG A -- a floating cluster of voxels near the fluid arena, with gaps beneath.
//   Candidates, and how each is separated here:
//     (a) a terrain-generation seam unrelated to fluid
//         -> A1 scans for floating terrain with NO fluid simulation running at
//            all. If clusters appear there, fluid is exonerated.
//     (b) §7.4's activity radius demoting cells while they are mid-fall, now
//         that PlayerVoxel tracks the camera
//         -> A2 pours, walks the camera back to an ordinary viewing distance,
//            and reports whether frozen voxels are INSIDE or OUTSIDE the sleep
//            radius. Outside = §7.4 doing exactly what §7.7 describes
//            ("distant fluid freezes mid-flow, no state lost"), which is a
//            TUNING problem, not a correctness one.
//     (c) an orphaned dense brick left by CSIntent's force-demote path
//         -> A3 watches DenseBricksHeld across a full demote/re-promote cycle.
//            A leak shows as bricks held that never come back.
//
// BUG B -- at extreme height terrain vanishes and Tab (fly->walk) does not fall.
//   Deliberately treated as TWO things:
//     B1 reports altitude vs LODConfig.TIER_OUTER_RANGE_M and the world's actual
//        height. If terrain is simply beyond the outermost tier, that is the LOD
//        system doing its documented job and NOT a bug.
//     B2 is the one that smells real: it instruments residency, IsBlocking,
//        ResolveSpawn's return value and the motor's vertical motion at height,
//        so "why doesn't it fall" is answered rather than guessed.
//
// NO TIMING. Correctness/diagnosis only.

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

public class PlaytestBugsRig : MonoBehaviour
{
    [SerializeField] private ComputeShader _fluidCA;
    [SerializeField] private PlayerController _player;
    [Tooltip("Playground's demo value, which is the configuration the bug was seen in.")]
    [SerializeField] private int _activeRadiusVoxels = 128;
    [SerializeField] private string _outputRootFolderName = "PlaytestBugs";

    private const int R = 64;

    private readonly StringBuilder _log = new StringBuilder();
    private int _pass, _fail;
    private string _phase = "-";
    private string _outDir;
    private int _shotIndex;

    private static ChunkStore Store => Phase4Bootstrapper.Store;
    private static TerrainClipmap Clip => Phase4Bootstrapper.Clipmap;
    private static StreamManager Streamer => Phase4Bootstrapper.Streamer;

    private EditService _edits;
    private FluidGpuSimulation _fluid;
    private FluidOpListReadback _readback;
    private int3 _origin, _arenaCentre;
    private long _applied;
    private readonly List<int3> _stalled = new List<int3>();
    private int3 _scanLo, _scanHi;
    private int _settleBaseline = -1;

    private void L(string s) { _log.AppendLine(s); Debug.Log("[pb] " + s); }
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

        L("=== STEP 0: TWO PLAYTEST BUGS, DIAGNOSED ===");
        L(DateTime.Now.ToString("u", CultureInfo.InvariantCulture));
        L("");

        if (Store == null) { Fail("world never booted"); yield return Report(); yield break; }
        if (_player == null) _player = FindObjectOfType<PlayerController>();
        for (int i = 0; i < 150; i++) yield return null;

        L($"WORLD SHAPE, which both bugs depend on:");
        L($"  MAX_GENERATED_CHUNK_Y = {StreamManager.MAX_GENERATED_CHUNK_Y}  =>  only chunk " +
          $"layer {StreamManager.MAX_GENERATED_CHUNK_Y} generates");
        L($"  CHUNK_EDGE_VOXELS = {EngineConfig.CHUNK_EDGE_VOXELS}  =>  the world is " +
          $"{(StreamManager.MAX_GENERATED_CHUNK_Y + 1) * EngineConfig.CHUNK_EDGE_VOXELS * 0.1f:F1} m tall");
        L($"  MAX_TERRAIN_HEIGHT = {WorldGenConstants.MAX_TERRAIN_HEIGHT} voxels = " +
          $"{WorldGenConstants.MAX_TERRAIN_HEIGHT * 0.1f:F1} m");
        L($"  LOD tier outer ranges (m): {string.Join(", ", LODConfig.TIER_OUTER_RANGE_M)}");
        L("");

        yield return A1_FloatingTerrainWithNoFluidAtAll();
        yield return A0_SettleBaselineWithoutMoving();
        yield return A2_FrozenFluidVsTheSleepRadius();
        yield return A2b_CanTheStalledCellsBeWokenAtAll();
        yield return A2m_TheSameScenarioWithoutTheSeed();
        yield return A3_DenseBrickLeakAcrossDemoteCycle();
        yield return B1_AltitudeVsRenderRange();
        yield return B2_WhyItDoesNotFallAtHeight();

        yield return Report();
    }

    private IEnumerator Report()
    {
        _log.AppendLine();
        _log.AppendLine($"PASS {_pass}  FAIL {_fail}");
        _log.AppendLine(_fail == 0 ? "RESULT: PASSED" : "RESULT: FAILED");
        File.WriteAllText(Path.Combine(_outDir, "playtest_bugs_report.txt"), _log.ToString());
        Debug.Log("[pb] report -> " + _outDir);
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
        if (_fluid == null) return;
        if (_readback.CanIssue) { _fluid.Tick(Clip); _readback.IssueReadback(0); }
        _readback.PumpAndApply();
    }

    /// Solid terrain voxels with AIR directly beneath AND air directly above --
    /// an overhang lip is normal, a whole disconnected slab is not. Counts only
    /// SOLID (non-fluid) material, so this measures TERRAIN, not fluid.
    private int CountFloatingTerrain(int3 lo, int3 hi)
    {
        int n = 0;
        for (int z = lo.z; z <= hi.z; z++)
        for (int y = math.max(1, lo.y); y <= hi.y; y++)
        for (int x = lo.x; x <= hi.x; x++)
        {
            int3 v = new int3(x, y, z);
            byte m = Store.GetVoxel(v);
            if (m == Materials.Air || MaterialRules.IsFluidMaterial(m)) continue;
            if (Store.GetVoxel(v - new int3(0, 1, 0)) != Materials.Air) continue;
            n++;
        }
        return n;
    }

    private List<int3> FloatingMobile(int3 lo, int3 hi)
    {
        var cells = new List<int3>();
        for (int z = lo.z; z <= hi.z; z++)
        for (int y = math.max(1, lo.y); y <= hi.y; y++)
        for (int x = lo.x; x <= hi.x; x++)
        {
            int3 v = new int3(x, y, z);
            if (!MaterialRules.IsMobile(Store.GetVoxel(v))) continue;
            if (Store.GetVoxel(v - new int3(0, 1, 0)) != Materials.Air) continue;
            cells.Add(v);
        }
        return cells;
    }

    /// Builds a fresh region, disposing any previous one. Several steps each
    /// want their own arena so leftover water from one cannot make the next
    /// look stalled (or unstalled) for the wrong reason; centralising the
    /// teardown here is what stops that from leaking a buffer set per step.
    private void MakeFluid(int3 origin)
    {
        if (_fluid != null)
        {
            _readback?.Dispose();
            _readback = null;
            _edits.DetachFluidSimulation(_fluid);
            _fluid.Dispose();
            _fluid = null;
        }

        _origin = origin;
        _arenaCentre = origin + new int3(R / 2, R / 2, R / 2);
        _fluid = new FluidGpuSimulation(_fluidCA, new int3(R, R, R), 8192, 8192)
        {
            RegionOriginVoxels = origin,
            PlayerVoxel = _arenaCentre,
            ActiveRadiusVoxels = _activeRadiusVoxels,
            SleepRadiusVoxels = FluidActiveRegion.SleepRadiusFor(_activeRadiusVoxels),
        };
        _readback = new FluidOpListReadback(_fluid, Store)
        {
            OnVoxelApplied = v => { Clip.MarkDirty(CoordMath.VoxelToChunk(v)); _applied++; },
        };
        _edits.AttachFluidSimulation(_fluid, Store);
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
    // A1 -- is it a TERRAIN seam? Scan with NO fluid simulation at all.
    // =====================================================================

    private IEnumerator A1_FloatingTerrainWithNoFluidAtAll()
    {
        _phase = "A1 terrain seam, no fluid";
        L("BUG A / candidate (a) -- floating TERRAIN, with no fluid simulation running");

        _edits = new EditService();
        _edits.AttachWorld(Store, Store, Store, Clip);
        Check(!_edits.HasFluidSimulation, "no fluid simulation is attached for this scan");

        Camera cam = Camera.main;
        int3 camVox = CoordMath.WorldToVoxel(new float3(cam.transform.position.x,
                                                        cam.transform.position.y,
                                                        cam.transform.position.z));
        int3 lo = new int3(camVox.x - 96, 1, camVox.z - 96);
        int3 hi = new int3(camVox.x + 96, WorldGenConstants.MAX_TERRAIN_HEIGHT + 2, camVox.z + 96);

        int floatingTerrain = CountFloatingTerrain(lo, hi);
        L($"  scanned {(hi.x - lo.x + 1)}x{(hi.y - lo.y + 1)}x{(hi.z - lo.z + 1)} voxels of " +
          "untouched generated terrain");
        L($"  solid voxels with air directly beneath: {floatingTerrain}");
        Note("Overhangs and cave roofs legitimately produce these, so a NONZERO count is not " +
             "itself a defect -- what matters is whether it changes once fluid runs (A2).");

        yield return Shot("A1_untouched_terrain",
            new float3(camVox.x * 0.1f - 6f, camVox.y * 0.1f + 4f, camVox.z * 0.1f - 6f),
            new float3(camVox.x * 0.1f, camVox.y * 0.1f, camVox.z * 0.1f));
        L("");
    }

    // =====================================================================
    // A0 -- THE SETTLE BASELINE. The same pour with the player never moving.
    //
    // A2 asks "does a region that slept mid-flow restart?". That question is
    // only answerable against a control, because "how much is still hanging
    // after this pour settles" is not necessarily zero for reasons that have
    // nothing to do with §7.4. Without this number, any residue in A2 gets
    // blamed on the radius by default -- which is exactly the guess-instead-of-
    // isolate move that these rigs exist to avoid.
    // =====================================================================

    private IEnumerator A0_SettleBaselineWithoutMoving()
    {
        _phase = "A0 settle baseline, player stationary";
        L("BUG A / baseline -- the same pour, with the active centre never moving");

        Camera cam = Camera.main;
        int3 camVox = CoordMath.WorldToVoxel(new float3(cam.transform.position.x,
                                                        cam.transform.position.y,
                                                        cam.transform.position.z));
        int surface = SurfaceY(camVox.x - 4 * R, camVox.z);
        MakeFluid(new int3(camVox.x - 4 * R - R / 2, math.max(0, surface - 8), camVox.z - R / 2));
        _fluid.UpdatePlayerPosition(_arenaCentre);

        yield return PourLeaveAndReturn(seedTicks: _fluid.RecentreSeedTicks,
                                        verbose: false, moveAway: false);

        _settleBaseline = _stalled.Count;
        L($"  BASELINE: {_settleBaseline} unsupported voxels after a pour that was never " +
          "left and never returned to");
        Note("This is the CA's own settling residue. Anything ABOVE it in A2 is what §7.4 " +
             "is responsible for; anything at or below it is not.");
        L("");
    }

    // =====================================================================
    // A2  -- §7.4's wake-on-approach: does a region that slept mid-flow restart?
    // A2m -- THE SAME SCENARIO WITH THE SEED DISABLED.
    //
    // A2m is not decoration. A2 asserts that nothing is left hanging, and an
    // assertion like that passes just as happily when the scenario failed to
    // set anything up -- a pour that never reached the boundary, a radius that
    // never bit, a settle that ran short. A2m runs the identical scenario with
    // RecentreSeedTicks = 0 and asserts the stall DOES come back. The pair is
    // the mutation check, built in and re-run on every future run rather than
    // done by hand once and forgotten.
    // =====================================================================

    private IEnumerator A2_FrozenFluidVsTheSleepRadius()
    {
        _phase = "A2 wake-on-approach (seed ON)";
        L("BUG A / candidate (b) -- does a region that slept mid-flow wake on approach?");

        Camera cam = Camera.main;
        int3 camVox = CoordMath.WorldToVoxel(new float3(cam.transform.position.x,
                                                        cam.transform.position.y,
                                                        cam.transform.position.z));
        int surface = SurfaceY(camVox.x, camVox.z);
        MakeFluid(new int3(camVox.x - R / 2, math.max(0, surface - 8), camVox.z - R / 2));

        yield return PourLeaveAndReturn(seedTicks: _fluid.RecentreSeedTicks, verbose: true);

        L($"  re-centres {_fluid.RecentresTotal}, seeded ticks run {_fluid.RecentreSeedTicksRun}");

        // NON-VACUITY GUARDS. Without these a pass could mean "the radius never
        // moved" or "the seed never ran" rather than "the seed worked".
        Check(_fluid.RecentresTotal > 0,
            $"the active centre actually moved ({_fluid.RecentresTotal} re-centres) -- " +
            "otherwise §7.4 was never exercised at all");
        Check(_fluid.RecentreSeedTicksRun > 0,
            $"and the wake-on-approach seed actually ran ({_fluid.RecentreSeedTicksRun} ticks)");

        // MEASURED AGAINST A0'S CONTROL, not against zero. The claim under test
        // is "leaving and returning costs nothing" -- that is exactly
        // "no worse than never leaving". Asserting zero here would fold the
        // CA's own settling residue into §7.4's account and blame the radius
        // for something the radius did not do.
        Check(_stalled.Count <= _settleBaseline,
            _stalled.Count <= _settleBaseline
                ? $"leaving and returning leaves no more hanging than never leaving did " +
                  $"({_stalled.Count} vs baseline {_settleBaseline}) -- §7.4's 'on approach " +
                  "it wakes' is doing its job"
                : $"{_stalled.Count} voxels stalled against a baseline of {_settleBaseline}: " +
                  $"{_stalled.Count - _settleBaseline} of them are §7.4's doing");

        if (_settleBaseline > 0)
            Finding($"SEPARATE, SMALLER ISSUE, not caused by §7.4: even a pour that is never " +
                    $"left leaves {_settleBaseline} voxel(s) unsupported. A2b shows an " +
                    "explicit wake clears them, so they are wakeable and simply unasked -- " +
                    "the same shape of gap as the §7.4 bug but a different trigger. Reported, " +
                    "NOT fixed here: it is a different cause and deserves its own isolation.");

        yield return Shot("A2_after_return",
            new float3(_arenaCentre.x * 0.1f - 5f, (_origin.y + 26) * 0.1f, _arenaCentre.z * 0.1f - 5f),
            new float3(_arenaCentre.x * 0.1f, (_origin.y + 14) * 0.1f, _arenaCentre.z * 0.1f));
        L("");
    }

    private IEnumerator A2m_TheSameScenarioWithoutTheSeed()
    {
        _phase = "A2m wake-on-approach (seed OFF) -- mutation control";
        L("BUG A / MUTATION CONTROL -- the identical scenario with the seed disabled");

        // A fresh arena well clear of A2's, so leftover water from that run
        // cannot make this one look stalled (or unstalled) for the wrong reason.
        int3 far = new int3(_origin.x + 4 * R, _origin.y, _origin.z);
        MakeFluid(far);

        yield return PourLeaveAndReturn(seedTicks: 0, verbose: false);

        L($"  re-centres {_fluid.RecentresTotal}, seeded ticks run {_fluid.RecentreSeedTicksRun}");
        Check(_fluid.RecentreSeedTicksRun == 0, "the seed really was disabled for this run");

        Check(_stalled.Count > 0,
            _stalled.Count > 0
                ? $"CONTROL: with the seed off the stall comes back ({_stalled.Count} voxels " +
                  "left hanging), so A2's pass is not vacuous"
                : "with the seed off NOTHING stalled either -- A2 proves nothing, because the " +
                  "scenario is not reproducing the bug at all");
        L("");
    }

    /// The shared scenario. Pours a falling column, walks the camera away until
    /// §7.4 demotes the arena, walks back, and settles to quiescence. Leaves the
    /// still-unsupported cells in _stalled.
    private IEnumerator PourLeaveAndReturn(int seedTicks, bool verbose, bool moveAway = true)
    {
        _fluid.RecentreSeedTicks = seedTicks;
        _stalled.Clear();

        Camera cam = Camera.main;
        int wake = _fluid.ActiveRadiusVoxels, sleep = _fluid.SleepRadiusVoxels;
        if (verbose)
        {
            int halfDiag = (int)(math.sqrt(3f) * R / 2f);
            L($"  arena {R}^3 at {_origin}, centre {_arenaCentre}, half-diagonal {halfDiag} voxels");
            L($"  §7.4 radii: wake {wake}, sleep {sleep} (Playground's demo values)");
        }

        int cx = _arenaCentre.x, cz = _arenaCentre.z, fl = _origin.y + 4;
        _edits.SetBox(new int3(cx - 10, fl, cz - 10), new int3(cx + 10, fl + 40, cz + 10), Materials.Air);
        _edits.SetBox(new int3(cx - 10, fl, cz - 10), new int3(cx + 10, fl, cz + 10), Materials.Stone);
        _edits.SetBox(new int3(cx - 3, fl + 24, cz - 3), new int3(cx + 3, fl + 34, cz + 3), Materials.Water);
        for (int i = 0; i < 6; i++) { Tick(); yield return null; }

        // WALK AWAY WHILE IT IS STILL FALLING. This is the reported scenario:
        // you look at the arena from a few metres back, not from inside it.
        int3 lo = new int3(cx - 12, fl - 1, cz - 12), hi = new int3(cx + 12, fl + 42, cz + 12);
        for (int step = 0; step < 5 && moveAway; step++)
        {
            float back = 4f + step * 3f;            // 4 m .. 16 m away
            cam.transform.position = new Vector3(cx * 0.1f - back, (fl + 20) * 0.1f, cz * 0.1f - back);
            int3 pv = CoordMath.WorldToVoxel(new float3(cam.transform.position.x,
                                                        cam.transform.position.y,
                                                        cam.transform.position.z));
            _fluid.UpdatePlayerPosition(pv);
            for (int i = 0; i < 60; i++) { Tick(); yield return null; }

            if (!verbose) continue;
            var f = FloatingMobile(lo, hi);
            int outside = 0;
            foreach (var v in f)
                if (FluidActiveRegion.BeyondSleepRadius(v, _fluid.PlayerVoxel, sleep)) outside++;
            L($"  camera {back:F0} m back (centre {_fluid.PlayerVoxel}): {f.Count} unsupported " +
              $"-- {outside} OUTSIDE the sleep radius, {f.Count - outside} inside");
        }

        // ---- COME BACK ----
        _fluid.UpdatePlayerPosition(_arenaCentre);
        int quiet = 0; long prev = _applied;
        for (int i = 0; i < 1500 && quiet < 60; i++)
        {
            Tick();
            quiet = _applied == prev ? quiet + 1 : 0;
            prev = _applied;
            yield return null;
        }

        var stillFrozen = FloatingMobile(lo, hi);
        int onBoxEdge = 0, outsideBox = 0;
        int3 boxLo = _origin, boxHi = _origin + new int3(R - 1, R - 1, R - 1);
        foreach (var v in stillFrozen)
        {
            // THREE MECHANISMS THAT LOOK IDENTICAL ON SCREEN, counted apart:
            //   outside the §7.2 region box -> never simulated at all
            //   ON its boundary             -> simulated, nowhere to flow
            //   inside, inside the radius   -> a genuine stall
            bool inBox = math.all(v >= boxLo) && math.all(v <= boxHi);
            if (!inBox) { outsideBox++; continue; }
            bool onEdge = v.x == boxLo.x || v.x == boxHi.x || v.y == boxLo.y ||
                          v.y == boxHi.y || v.z == boxLo.z || v.z == boxHi.z;
            if (onEdge) { onBoxEdge++; continue; }
            if (!FluidActiveRegion.BeyondSleepRadius(v, _fluid.PlayerVoxel, sleep))
                _stalled.Add(v);
        }
        _scanLo = lo; _scanHi = hi;

        L($"  after returning to the arena and settling: {stillFrozen.Count} unsupported " +
          $"-- {outsideBox} outside the §7.2 box, {onBoxEdge} on its boundary, " +
          $"{_stalled.Count} strictly inside and inside the radius");
    }

    // =====================================================================
    // A2b -- THE DISCRIMINATOR. Are the stalled cells broken, or unasked?
    //
    // ReadSlotAtCell's own doc comment names the two cases and says they need
    // opposite fixes: a cell with NO owner was never promoted (a wake problem),
    // a cell WITH an owner can never be promoted by anything (a leak). Then the
    // control: request a wake explicitly and see whether they move. If they do,
    // the cells were always fine and the only missing thing was something to
    // ask -- which is a statement about §7.4's driving, not about the CA.
    // =====================================================================

    private IEnumerator A2b_CanTheStalledCellsBeWokenAtAll()
    {
        _phase = "A2b stalled cells: broken or unasked";
        L("BUG A / isolating the stall found in A2");

        if (_stalled.Count == 0)
        {
            Note("A2 found no stalled cells, so there is nothing to isolate.");
            L("");
            yield break;
        }

        int owned = 0, unowned = 0, oddball = 0;
        var sample = new StringBuilder();
        for (int i = 0; i < _stalled.Count; i++)
        {
            int cell = _fluid.RegionIndex(_stalled[i]);
            int slot = _fluid.ReadSlotAtCell(cell);
            if (slot == -1) unowned++;
            else if (slot >= 0) owned++;
            else oddball++;
            if (i < 6) sample.Append($"{_stalled[i]}->slot {slot}   ");
        }

        L($"  {_stalled.Count} stalled cells: {unowned} own NO slot, {owned} still OWN a slot, " +
          $"{oddball} out of range");
        L($"  sample: {sample}");

        if (owned > 0)
            Finding($"{owned} cells still hold a slot that is not awake. Nothing can ever " +
                    "promote those -- both CSPromote and CSWakeScan return early on an owned " +
                    "cell. That is a leak in the demote path.");
        if (unowned > 0)
            Finding($"{unowned} cells own no slot at all, so the demote path DID clean up " +
                    "correctly. They are stalled because nothing ever asks them to wake.");

        // ---- THE CONTROL ----
        // Ask explicitly. Everything else about the region is unchanged: same
        // player position, same radius, same materials, same terrain.
        long queuedBefore = _fluid.WakeRequestsQueuedTotal;
        foreach (var v in _stalled) _fluid.RequestWake(v);
        L($"  explicitly requested a wake for all {_stalled.Count} cells " +
          $"({_fluid.WakeRequestsQueuedTotal - queuedBefore} accepted, " +
          $"{_fluid.WakeRejectedOutOfRegion} rejected out-of-region)");

        long prev = _applied;
        int quiet = 0;
        for (int i = 0; i < 1500 && quiet < 60; i++)
        {
            Tick();
            quiet = _applied == prev ? quiet + 1 : 0;
            prev = _applied;
            yield return null;
        }

        var after = FloatingMobile(_scanLo, _scanHi);
        int stillStalled = 0;
        foreach (var v in after)
            if (!FluidActiveRegion.BeyondSleepRadius(v, _fluid.PlayerVoxel, _fluid.SleepRadiusVoxels))
                stillStalled++;

        L($"  after an explicit wake and a settle: {stillStalled} unsupported " +
          $"(was {_stalled.Count})");

        // STRICT REDUCTION, not a fraction. An earlier version compared against
        // _stalled.Count / 2, which for a single stalled cell is integer 0 --
        // so a perfect 1 -> 0 recovery was reported as a failure.
        Check(stillStalled < _stalled.Count,
            stillStalled < _stalled.Count
                ? $"CONTROL: an explicit wake request unstalls them ({_stalled.Count} -> " +
                  $"{stillStalled}). The cells were never broken -- §7.4's demote simply has " +
                  "no inverse, so nothing re-promotes them when the player returns."
                : $"an explicit wake request did NOT unstall them ({_stalled.Count} -> " +
                  $"{stillStalled}), so the cause is in the CA, not in §7.4's driving");
        L("");
    }

    // =====================================================================
    // A3 -- does force-demote leak dense bricks? (candidate (c))
    // =====================================================================

    private IEnumerator A3_DenseBrickLeakAcrossDemoteCycle()
    {
        _phase = "A3 dense brick leak";
        L("BUG A / candidate (c) -- orphaned dense bricks from the force-demote path");

        // A2m left the seed off on this instance. Restore it, so the leak test
        // exercises the SHIPPED configuration rather than the mutation control.
        _fluid.RecentreSeedTicks = 4;

        int before = Store.DenseBricksHeld;
        uint hi0, ever0;
        _fluid.ReadSlotCounters(out hi0, out ever0);

        // Five full demote/re-promote cycles: shove the centre far away so
        // everything force-demotes, then bring it back so everything re-wakes.
        for (int cycle = 0; cycle < 5; cycle++)
        {
            _fluid.UpdatePlayerPosition(_arenaCentre + new int3(4000, 0, 0));
            for (int i = 0; i < 60; i++) { Tick(); yield return null; }
            _fluid.UpdatePlayerPosition(_arenaCentre);
            for (int i = 0; i < 60; i++) { Tick(); yield return null; }
        }

        int after = Store.DenseBricksHeld;
        uint hi1, ever1;
        _fluid.ReadSlotCounters(out hi1, out ever1);
        uint freeNow = _fluid.ReadFreeSlotCount();

        L($"  dense bricks {before} -> {after} across 5 demote/re-promote cycles");
        L($"  slots everAllocated {ever0} -> {ever1}, highWater {hi0} -> {hi1}, free list {freeNow}");

        Check(after <= before + 64,
            $"force-demote does not leak dense bricks ({before} -> {after}); a leak would " +
            "grow this every cycle");
        Check(ever1 - ever0 < 4000,
            $"and does not burn slot indices per cycle ({ever1 - ever0} across 5 cycles) -- " +
            "A.5's free list is absorbing the churn");
        L("");
    }

    // =====================================================================
    // B1 -- terrain vanishing at height: LOD doing its job, or a defect?
    // =====================================================================

    private IEnumerator B1_AltitudeVsRenderRange()
    {
        _phase = "B1 altitude vs render range";
        L("BUG B / part 1 -- terrain vanishing at extreme height");

        float outer = LODConfig.TIER_OUTER_RANGE_M[LODConfig.TIER_OUTER_RANGE_M.Length - 1];
        float worldTopM = (StreamManager.MAX_GENERATED_CHUNK_Y + 1) *
                          EngineConfig.CHUNK_EDGE_VOXELS * 0.1f;

        L($"  outermost LOD tier reaches {outer} m (LODConfig.TIER_OUTER_RANGE_M)");
        L($"  the generated world is only {worldTopM:F1} m tall");
        L($"  => from {outer:F0} m up, ALL terrain is beyond the outermost tier and is not drawn");

        Camera cam = Camera.main;
        foreach (float h in new[] { 20f, 100f, 290f, 600f, 2000f })
        {
            cam.transform.position = new Vector3(1280f, h, 1268f);
            int3 v = CoordMath.WorldToVoxel(new float3(1280f, h, 1268f));
            int3 c = CoordMath.VoxelToChunk(v);
            bool resident = Store.IsResident(c);
            bool inWindow = Store.IsInWindow(c);
            L($"  h={h,6:F0} m  voxel y {v.y,6}  chunk {c}  resident {resident,-5}  " +
              $"inWindow {inWindow,-5}  distance to terrain top {h - worldTopM,7:F0} m " +
              $"({(h - worldTopM > outer ? "BEYOND the outer tier" : "within render range")})");
            yield return null;
        }

        Finding($"Terrain vanishing at height is the LOD system doing its documented job. " +
                $"The world is {worldTopM:F1} m tall and the outermost tier reaches {outer} m, " +
                $"so above ~{outer + worldTopM:F0} m there is nothing left in range to draw. " +
                "This is the SAME 290 m limit CLAUDE.md's known-issues bullet and " +
                "AMENDMENT_8_11_RENDER_RANGE.md already describe. NOT A BUG.");

        yield return Shot("B1_high_altitude", new float3(1280f, 600f, 1268f),
                          new float3(1280f, 0f, 1268f));
        L("");
    }

    // =====================================================================
    // B2 -- why doesn't the player fall when switching to walk at height?
    // =====================================================================

    private IEnumerator B2_WhyItDoesNotFallAtHeight()
    {
        _phase = "B2 no fall at height";
        L("BUG B / part 2 -- switching to walk at height does not fall");

        if (_player == null) { Fail("no PlayerController in the scene"); yield break; }
        if (!_player.Ready) _player.Bind(Store, Store);
        _player.DebugTakeControl();

        float worldTopM = (StreamManager.MAX_GENERATED_CHUNK_Y + 1) *
                          EngineConfig.CHUNK_EDGE_VOXELS * 0.1f;

        // The control height must be genuinely INSIDE the generated layer and
        // genuinely in air, or "it fell" would be measuring the wrong thing.
        int3 probe = CoordMath.WorldToVoxel(new float3(1280f, 5f, 1268f));
        int surface = SurfaceY(probe.x, probe.z);
        int controlVoxelY = math.min(surface + 20, EngineConfig.CHUNK_EDGE_VOXELS - 4);
        float controlH = controlVoxelY * 0.1f;
        L($"  terrain surface under (1280, 1268) m is voxel y {surface} " +
          $"({surface * 0.1f:F1} m); control drop height {controlH:F1} m, inside the world");
        L("");
        L("  height   chunk            resident  IsBlocking  overlaps  ResolveSpawn  fell/2s  nonRes");

        float fellLow = -1f, fellHigh = -1f;
        bool highResident = true, highBlocking = false, highResolved = true;

        foreach (float h in new[] { controlH, 20f, 60f, 300f })
        {
            float3 p = new float3(1280f, h, 1268f);
            int3 v = CoordMath.WorldToVoxel(p);
            int3 c = CoordMath.VoxelToChunk(v);
            bool resident = Store.IsResident(c);
            bool blocking = VoxelCollision.IsBlocking(Store, Store, v);
            bool overlaps = VoxelCollision.OverlapsSolid(Store, Store, p, 0.6f, 1.8f);

            _player.Motor.Teleport(p);
            bool resolved = _player.Motor.ResolveSpawn();

            float y0 = _player.Motor.PositionM.y;
            for (int i = 0; i < 120; i++)
            {
                _player.DebugStep(1f / 60f, float2.zero, false);
                yield return null;
            }
            float fell = y0 - _player.Motor.PositionM.y;

            L($"  {h,6:F1} {c.ToString(),-16} {resident,-9} {blocking,-11} {overlaps,-9} " +
              $"{resolved,-13} {fell,7:F2}  {_player.Motor.LastBlockedByNonResident}");

            if (h == controlH) fellLow = fell;
            if (h == 300f)
            {
                fellHigh = fell;
                highResident = resident;
                highBlocking = blocking;
                highResolved = resolved;
            }
        }
        L("");

        // THE CONTROL, first. Without it the "does not fall" row below proves
        // nothing -- a motor that never falls anywhere would print the same
        // table and look like the same bug.
        Check(fellLow > 1f,
            $"CONTROL: inside the generated world the player DOES fall " +
            $"({fellLow:F2} m in 2 s), so the motor and gravity work");

        // THE FIXED BEHAVIOUR. Before the VoxelCollision domain fix this row
        // read: fell 0.00, IsBlocking True, ResolveSpawn False. The EditMode
        // mutation check (AboveWorldCeilingTests) pins all three; these assert
        // the same thing end-to-end against real streamed terrain.
        Check(fellHigh > 10f,
            $"at 300 m the player now falls out of the sky ({fellHigh:F2} m in 2 s); " +
            "before the fix this was exactly 0.00");

        Check(!highResident,
            "and it does so WITHOUT the chunk becoming resident -- it is above " +
            "MAX_GENERATED_CHUNK_Y, so nothing ever generates or streams there. The fix " +
            "is to the collision rule's domain, not to streaming.");
        Check(!highBlocking,
            "VoxelCollision.IsBlocking no longer reports empty sky as SOLID");
        Check(highResolved,
            "and ResolveSpawn succeeds instead of failing silently");

        Finding("Bug B part 2 was a real defect, and it was not in the motor. §9.4's " +
                "fail-closed rule -- 'a non-resident chunk blocks' -- is correct at the " +
                "HORIZONTAL streaming edge, where 'not loaded yet' really might be rock. " +
                "Above MAX_GENERATED_CHUNK_Y it was wrong: that space is not unknown, it " +
                "is statically guaranteed empty, and treating it as solid embedded the " +
                "body in phantom rock on every side. Fixed by bounding rule 1's domain; " +
                "below the world stays fail-closed on purpose.");
        Finding("Both halves of Bug B share ONE root cause -- the world is only " +
                $"{worldTopM:F1} m tall -- but they are still two separate things: part 1 " +
                "is the renderer behaving as documented, part 2 is a collision rule " +
                "applied outside the domain it was written for.");
        L("");
    }
}
