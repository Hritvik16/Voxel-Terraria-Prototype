// ==========================================
// Assets/Game/Phase7CycleRig.cs
//
// §13 PHASE 7'S ACCEPTANCE TEST, as machine-checkable gates.
//
// The five acceptance bullets, and how each is checked here:
//
//   1. "A torch lights its surroundings via falloff; deep cave genuinely dark."
//      -> GATE 1. Dig a chamber, measure rendered luminance at known distances
//         from a placed torch, assert monotone falloff; measure the same
//         chamber with no torch and assert it is dark.
//
//   2. "Scrub dawn->dusk: mountain shadow sweeps continuously and exactly; a
//      single protruding voxel casts a crisp discrete shadow; grazing-angle
//      shadows show ZERO acne, ZERO Peter-Panning."
//      -> GATE 2. Scrub and assert the shadowed FRACTION changes monotonically
//         and without discontinuity; assert a lone voxel casts a shadow at all;
//         assert flat ground under a grazing sun has no shadowed pixels (acne)
//         and that a wall's shadow starts in the cell touching it (Peter-Pan).
//
//   3. §6.5's silhouette test "as a PIXEL-LEVEL assertion, not an eyeball".
//      -> GATE 3, and this is the strongest gate here. Rather than comparing
//         two rendered images and arguing about edges, it compares the GPU's
//         own sun-visibility output against ShadowRayReference -- the CPU
//         oracle -- PER SAMPLED PIXEL. For each sample the CPU casts the same
//         primary ray with RaymarchReference, takes the hit voxel and normal,
//         fires its own shadow ray, and the two must agree on occlusion.
//         A regression to the 8.1 travelled-distance LOD rule changes which
//         tier a distant sample reads and therefore changes occlusion, so it
//         shows up here as a disagreement count, not as a judgement call.
//
//   4. "Bomb a cave roof at noon: sunlight reaches the floor THE SAME FRAME;
//      pour water off a ledge -> moving shadow."
//      -> GATE 4. Sample the floor's sun visibility, break the roof through the
//         real §8.3 edit path, and assert the floor is lit on the very next
//         frame (§3.10's authoritative byte). The fluid half is reported as a
//         screenshot pair -- it is a consequence of §3.10, not separate code.
//
//   5. §3.8's cap: "place MAX_ACTIVE_LIGHTS torches in view, then place more.
//      Graceful furthest-first degradation, not silent drops."
//      -> GATE 5.
//
// PLUS a gate §13 does not ask for but Amendment 8.10 §2's method does:
//
//   0. TIER-0 PATH UNCHANGED WHEN SHADOWS ARE OFF, proven by an EXACT
//      traversal-counter match at a fixed pose -- not by an argument from code
//      structure. Amdt 8.10 proved the cascade this way (denseMicroSteps
//      116 == 116) and the same standard applies to a change this size.
//
// WHAT IS INHERENTLY VISUAL AND IS NOT ASSERTED: whether a shadow looks
// "crisp", whether the penumbra width is pleasing, whether the sun ramp reads
// as golden hour, and whether a cave is dark in a way that is atmospheric
// rather than merely dim. Those are the owner's five minutes. The report says
// so in its own COMMIT READINESS block rather than implying the gates cover it.
//
// MEASUREMENT: this rig asserts CORRECTNESS. It reports frame times only as
// context and never against a budget -- no gpuFrameTime (Amdt 8.10), no Xcode
// (Amdt 8.9 Rule 1), no Performance State. The §13 budget gates are handled
// separately by run-phase7-lighting-ab.sh and AMENDMENT_8_12_PHASE7_GATE.md.
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
using VoxelEngine.Rendering;
using VoxelEngine.WorldGen;

public class Phase7CycleRig : MonoBehaviour
{
    [SerializeField] private string _outputRootFolderName = "Phase7Cycle";

    private static ChunkStore Store => Phase4Bootstrapper.Store;

    private readonly StringBuilder _log = new StringBuilder();
    private string _outDir;
    private int _pass, _fail, _shots;
    private EditService _edits;
    private LightingDriver _driver;
    private int3 _chamberCentre;
    private int _surfaceY;

    private void L(string s) { _log.AppendLine(s); Debug.Log("[p7] " + s); }
    private void Pass(string s) { _pass++; L("    PASS  " + s); }
    private void Fail(string s) { _fail++; L("    FAIL  " + s); }
    private void Note(string s) { L("    note  " + s); }
    private void Check(bool ok, string s) { if (ok) Pass(s); else Fail(s); }

    IEnumerator Start()
    {
        string ts = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
        _outDir = Path.Combine(Application.persistentDataPath, _outputRootFolderName, ts);
        Directory.CreateDirectory(_outDir);

        // FULLSCREEN. Not for frame time -- this rig quotes none against a
        // budget -- but because FPS_INVESTIGATION §0.1 shows a windowed surface
        // on this machine is composited and throttled unpredictably, and a
        // throttled surface means screenshots can be captured mid-resize. The
        // gates read PIXELS, so the surface has to be stable.
        Screen.SetResolution(1920, 1080, true);
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;

        L($"PHASE 7 ACCEPTANCE -- Lighting & Day/Night -- {ts}");
        L($"device {SystemInfo.graphicsDeviceName}   {Screen.width}x{Screen.height}");
        L($"build: {(Debug.isDebugBuild ? "DEVELOPMENT (numbers here are NOT comparable to a release run)" : "RELEASE")}");
        L("");
        L("This rig asserts CORRECTNESS. It quotes no frame time against any budget:");
        L("  no Xcode (Amdt 8.9 Rule 1), no Performance State (Rule 2), no gpuFrameTime");
        L("  (Amdt 8.10's CORRECTION NOTICE). §13's two budget gates are unrunnable as");
        L("  written and are handled by run-phase7-lighting-ab.sh + AMENDMENT_8_12.");
        L("");

        while (Store == null) yield return null;
        for (int i = 0; i < 240; i++) yield return null;      // let streaming settle

        _driver = FindAnyObjectByType<LightingDriver>();
        if (_driver == null) { Fail("no LightingDriver in the scene -- lights would never upload"); yield return Report(); yield break; }

        _edits = new EditService();
        _edits.AttachWorld(Store, Store, Store, Phase4Bootstrapper.Clipmap);
        _driver.Attach(_edits);
        _driver.SetRunClock(false);                           // every gate wants a fixed pose
        LightingDriver.ClockPaused = true;

        var cam = Camera.main;
        int3 camVox = CoordMath.WorldToVoxel(new float3(cam.transform.position.x,
                                                        cam.transform.position.y,
                                                        cam.transform.position.z));
        _surfaceY = SurfaceY(camVox.x, camVox.z);
        if (_surfaceY < 1) { Fail("no surface under the spawn -- world did not generate"); yield return Report(); yield break; }

        // THE CHAMBER HAS TO BE INSIDE THE WORLD, AND THE FIRST VERSION WAS NOT.
        // It was spawnSurfaceY - 45. The spawn sits near the coast where the
        // surface is y=38, so that put the chamber at y=-7 -- BELOW the world,
        // where ChunkStore has no chunks, EditService refuses every write, and
        // the camera sees nothing but sky. Every Gate 1 and Gate 5 assertion
        // failed for that one reason, and the constant luminance of 0.8446 at
        // all three falloff distances was the tell: that is the sky colour, not
        // a cave.
        //
        // So the chamber is now sited under the DEEPEST terrain nearby (a
        // mountain, which is what §13's scene asks for anyway) and its depth is
        // clamped so the sphere cannot reach below y=0.
        int3 deep = DeepestGroundNear(camVox, 220);
        int mountainY = SurfaceY(deep.x, deep.z);
        int chamberRadius = 14;
        int chamberY = Mathf.Max(chamberRadius + 2, mountainY - 30);
        _chamberCentre = new int3(deep.x, chamberY, deep.z);
        L($"spawn voxel {camVox}, spawn surface y={_surfaceY}");
        L($"deepest terrain found nearby: {deep} with surface y={mountainY}");
        L($"chamber centre {_chamberCentre} (radius {chamberRadius}, " +
          $"{mountainY - (chamberY + chamberRadius)} voxels of rock above it)");
        _surfaceY = mountainY;
        L("");

        yield return Gate0_Tier0Unchanged();
        yield return Gate1_TorchFalloffAndCaveDarkness();
        yield return Gate2_SweepAcneAndPeterPanning();
        yield return Gate3_SilhouetteAgainstTheCpuOracle();
        yield return Gate4_SameFrameSunlight();
        yield return Gate5_LightCapDegradation();

        yield return Report();
    }

    // =====================================================================
    // GATE 0 -- the tier-0 path is unchanged when shadows are off
    // =====================================================================

    /// AMENDMENT 8.10 §2's METHOD, not an argument from code structure:
    /// "Tier-0 path proven unchanged when cascade is on but not engaged:
    ///  denseMicroSteps matched EXACTLY (116 = 116) ... real evidence, not just
    ///  the 'should be identical' argument from the code's own structure."
    ///
    /// TraceShadowRay uses its own locals and touches none of the primary
    /// traversal's counters, so the primary ray's step count and dense
    /// micro-step count must be BIT-IDENTICAL between shadow rung 3 (off) and
    /// rung 0 (full penumbra) at the same pose. If they differ, the shadow ray
    /// is perturbing the traversal it was supposed to run after.
    private IEnumerator Gate0_Tier0Unchanged()
    {
        L("GATE 0 -- tier-0 traversal unchanged when shadows are off (Amdt 8.10 §2's method)");

        var cam = Camera.main;
        // THE POSE MUST PUT TERRAIN UNDER THE DEBUG PIXEL, AND THE FIRST ONE DID
        // NOT. GroundHorizon from the bootstrapper's spawn (14 m up, looking
        // level) put sky at the centre pixel, so both counters read 0 and the
        // match was 0 == 0 -- vacuously true. The rig's own anti-vacuity check
        // caught it, which is why that check exists; the fix is to aim at the
        // mountain and to assert the hit as a PRECONDITION rather than trust it.
        PoseLookingDownAt(cam, new int3(_chamberCentre.x, _surfaceY, _chamberCentre.z), 18f);
        RaymarchFeature.DebugPixel = new Vector2Int(RaymarchFeature.LastDispatchResolution.x / 2,
                                                    RaymarchFeature.LastDispatchResolution.y / 2);
        for (int i = 0; i < 40; i++) yield return null;

        RaymarchFeature.ShadowQualityLevel = EngineConfig.SHADOW_LEVEL_OFF;
        for (int i = 0; i < 20; i++) yield return null;
        float[] off = ReadDebug();

        RaymarchFeature.ShadowQualityLevel = EngineConfig.SHADOW_LEVEL_FULL_PENUMBRA;
        for (int i = 0; i < 20; i++) yield return null;
        float[] on = ReadDebug();

        int stepsOff = Mathf.RoundToInt(off[7]), stepsOn = Mathf.RoundToInt(on[7]);
        int denseOff = Mathf.RoundToInt(off[13]), denseOn = Mathf.RoundToInt(on[13]);

        Check(stepsOff == stepsOn,
            $"primary outer steps identical with shadows off vs on ({stepsOff} vs {stepsOn})");
        Check(denseOff == denseOn,
            $"primary denseMicroSteps identical with shadows off vs on ({denseOff} vs {denseOn})");
        // PRECONDITION FIRST. A counter match between two zeroes proves nothing,
        // so the hit is asserted before the match is believed.
        Check(Mathf.RoundToInt(on[45]) == 1,
            "PRECONDITION: the debug pixel resolves a primary hit (a sky pixel would make " +
            "the counter match below vacuously true)");
        Check(stepsOn > 0,
            $"PRECONDITION: the primary ray actually traversed ({stepsOn} outer steps)");
        Note($"shadow steps at this pixel: off={Mathf.RoundToInt(off[20])} on={Mathf.RoundToInt(on[20])}");
        Check(Mathf.RoundToInt(on[20]) > 0,
            "the shadow branch actually executed at rung 0 -- without this the counter match " +
            "above says only that nothing ran twice");
        L("");
    }

    // =====================================================================
    // GATE 1 -- torch falloff, and a genuinely dark cave
    // =====================================================================

    private IEnumerator Gate1_TorchFalloffAndCaveDarkness()
    {
        L("GATE 1 -- §13: \"a torch lights its surroundings via falloff; deep cave genuinely dark\"");

        // A chamber well below the surface, carved through the REAL §8.3 edit
        // path so this tests the shipped path rather than a parallel one.
        DigChamber(_chamberCentre, 14);
        for (int i = 0; i < 30; i++) yield return null;

        var cam = Camera.main;
        SunCycle.SetHours(SunCycle.NoonHours);

        // --- dark first, before any torch exists ---
        PoseInChamber(cam);
        for (int i = 0; i < 20; i++) yield return null;
        yield return Shot("01_cave_dark_no_torch");
        float darkLum = AverageLuminance();
        Note($"mean luminance in the sealed chamber with NO torch: {darkLum:F4}");
        Check(darkLum < 0.10f,
            $"deep cave is genuinely dark before any light is placed (mean luminance {darkLum:F4} < 0.10)");

        // --- place a torch through the §8.3 path, and §6.6 must enqueue it ---
        int before = _driver.Lights.RegisteredCount;
        var torchAt = _chamberCentre + new int3(0, -10, 0);
        bool wrote = _edits.TrySetVoxel(torchAt, Materials.Torch);
        for (int i = 0; i < 20; i++) yield return null;

        Check(wrote, "the torch was actually written to terrain");
        Check(_driver.Lights.RegisteredCount == before + 1,
            $"§6.6: placing an emissive through the §8.3 path enqueued it into the light system " +
            $"({before} -> {_driver.Lights.RegisteredCount})");

        yield return Shot("02_cave_lit_by_torch");
        float litLum = AverageLuminance();
        Note($"mean luminance with one torch: {litLum:F4}");
        Check(litLum > darkLum * 1.5f,
            $"the torch measurably lights its surroundings ({darkLum:F4} -> {litLum:F4})");

        // --- FALLOFF: luminance must fall as the camera backs away ---
        // Measured at three distances along one axis so the shape is checked,
        // not just the presence of light. C.11 is 1/(1 + d^2 k), so this is
        // monotone by construction and a non-monotone result means the range
        // cutoff or the attenuation is wired wrong.
        //
        // THE FIRST VERSION OF THESE POSES WAS INSIDE ROCK. The chamber is 1.4 m
        // in radius; 1.5 / 3 / 6 m back from the torch is solid stone, so all
        // three readings were 0.0000 (a black frame) and the gate measured the
        // inside of a wall, not a falloff. The fix is the POSE, not the
        // assertion: a sealed air corridor is carved along the measurement axis
        // so the camera stands in open air with the torch in view, and the
        // camera-in-air precondition below makes that a checked fact.
        const float PoseLiftM = 0.3f;
        int3 axis = CarveSightCorridor(torchAt, 6.0f, PoseLiftM);
        for (int i = 0; i < 30; i++) yield return null;
        var lums = new List<float>();
        foreach (float dist in new[] { 1.5f, 3.0f, 6.0f })
        {
            PoseLookingAlong(cam, torchAt, axis, dist, PoseLiftM);
            for (int i = 0; i < 12; i++) yield return null;
            Vector3 p = cam.transform.position;
            byte at = Store.GetVoxel(CoordMath.WorldToVoxel(new float3(p.x, p.y, p.z)));
            Check(at == Materials.Air,
                $"PRECONDITION: the {dist:F1} m falloff camera stands in air (material {at}) -- " +
                "a camera inside rock reads a black frame and makes the falloff check meaningless");
            lums.Add(AverageLuminance());
            if (dist < 2f) yield return Shot("03a_torch_falloff_1.5m");
        }
        Note($"luminance at 1.5 m / 3 m / 6 m from the torch: " +
             $"{lums[0]:F4} / {lums[1]:F4} / {lums[2]:F4}");
        Check(lums[0] > lums[1] && lums[1] > lums[2],
            "torch brightness falls off monotonically with distance (C.11)");
        yield return Shot("03_torch_falloff_6m");

        // --- the §6.5 / §13 AMBIGUITY, screenshotted rather than argued ---
        // §6.5 specifies pure attenuation with NO per-light occlusion ray, so a
        // torch behind a wall lights the far side. That is what ships, and this
        // is the picture of it.
        var wallAt = torchAt + new int3(3, 0, 0);
        for (int dy = -4; dy <= 4; dy++)
        for (int dz = -4; dz <= 4; dz++)
            _edits.TrySetVoxel(wallAt + new int3(0, dy, dz), Materials.Stone);
        for (int i = 0; i < 20; i++) yield return null;
        PoseLookingAt(cam, wallAt + new int3(3, 0, 0), 2.5f);
        for (int i = 0; i < 12; i++) yield return null;
        yield return Shot("04_torch_through_wall_FLAGGED");
        Note("04_torch_through_wall_FLAGGED.png: §6.5 and §3.8 specify pure attenuation with NO");
        Note("  per-light occlusion ray, so a torch behind a wall LIGHTS THE FAR SIDE. That is");
        Note("  leaking, not bending, and it is in tension with §13's acceptance wording. The");
        Note("  literal reading is what ships; this is the consequence; the decision is flagged");
        Note("  for the owner in PHASE_7_COMPLETION.md. NOT asserted either way.");
        L("");
    }

    // =====================================================================
    // GATE 2 -- the sweep, acne, and Peter-Panning
    // =====================================================================

    private IEnumerator Gate2_SweepAcneAndPeterPanning()
    {
        L("GATE 2 -- §13: sweep continuity, crisp discrete shadow, ZERO acne, ZERO Peter-Panning");

        var cam = Camera.main;
        RaymarchFeature.ShadowQualityLevel = EngineConfig.SHADOW_LEVEL_HARD_ONLY;
        RaymarchFeature.UseDebugViewOverride = true;
        RaymarchFeature.DebugViewOverride = RaymarchFeature.DebugMode.SunVis;

        // --- ACNE: a CARVED FLAT PLATFORM, grazing sun, SunVis view. ---
        //
        // TWO THINGS THE FIRST VERSION GOT WRONG, both of which made it measure
        // terrain rather than acne:
        //
        //   (a) it SEARCHED for flat ground and verified flatness over a
        //       6-voxel neighbourhood, then pointed a camera at it from 3 m up
        //       -- a view covering far more ground than was ever checked. At 2
        //       degrees of sun elevation ANY bump outside that neighbourhood
        //       throws a shadow tens of metres long, so the 2.01% it measured
        //       was almost certainly real shadow from real terrain.
        //   (b) it measured the WHOLE FRAME, including the horizon.
        //
        // Both are fixed by construction rather than by loosening the threshold:
        // the platform is CARVED (so it is provably flat and provably large),
        // and only the centre of the frame is sampled (so only the platform is
        // in the measurement). Acne on a plane nothing occludes is now the only
        // thing that can register.
        int3 flat = CarveFlatPlatform(_chamberCentre, 26);
        for (int deg = 2; deg <= 10; deg += 4)
        {
            SunCycle.SetHours(SunCycle.HoursForElevation(deg, morning: false));
            PoseLookingDownAt(cam, flat, 3.0f);
            for (int i = 0; i < 16; i++) yield return null;
            float shadowFrac = ShadowedFractionCentre();
            Note($"grazing sun at {deg} deg: shadowed fraction of the carved platform = {shadowFrac * 100f:F2}%");
            Check(shadowFrac < 0.01f,
                $"ZERO acne at {deg} deg elevation on a carved flat platform " +
                $"(shadowed {shadowFrac * 100f:F2}% < 1%)");
            if (deg == 2) yield return Shot("05_acne_check_grazing_sunvis");
        }

        // --- PETER-PANNING: a wall's shadow must touch its own base. ---
        // A detached shadow is the other half of the same construction, so the
        // check is that the cell IMMEDIATELY beside the caster is shadowed.
        int3 wallBase = flat + new int3(0, 1, 0);
        for (int y = 0; y < 12; y++)
        for (int dz = -6; dz <= 6; dz++)
            _edits.TrySetVoxel(wallBase + new int3(0, y, dz), Materials.Stone);
        for (int i = 0; i < 20; i++) yield return null;

        SunCycle.SetHours(SunCycle.HoursForElevation(25f, morning: false));
        float3 sun = SunCycle.Direction;

        // THE SHADOW SIDE IS DERIVED FROM THE SUN, NOT ASSUMED. The first
        // version's comment said "the sun is in +X at this hour, so the shadow
        // falls toward -X" and was simply wrong: HoursForElevation(.., evening)
        // returns a time on the FAR branch of the arc, where cos(theta) is
        // negative and the sun is in -X. It then checked the lit side and
        // reported a Peter-Panning gap that did not exist. A test that hardcodes
        // which way a shadow falls will be wrong every time the arc changes, so
        // this now reads the sun vector.
        int shadowStepX = sun.x > 0f ? -1 : 1;
        int3 touching = flat + new int3(shadowStepX, 0, 0);
        Note($"sun direction {sun}; shadow falls toward x{(shadowStepX > 0 ? "+" : "-")}, " +
             $"so the cell touching the wall base is {touching}");
        bool touchingShadowed = CpuOccluded(touching, new int3(0, 1, 0), sun, cam.transform.position);
        Check(touchingShadowed,
            "the ground cell touching the wall base is in shadow -- no Peter-Panning gap");
        PoseLookingDownAt(cam, flat + new int3(shadowStepX * 6, 0, 0), 4.0f);
        for (int i = 0; i < 16; i++) yield return null;
        yield return Shot("06_peterpanning_check_sunvis");

        // --- A SINGLE PROTRUDING VOXEL casts a shadow at all. ---
        int3 lone = flat + new int3(-25, 3, 0);
        _edits.TrySetVoxel(lone, Materials.Stone);
        for (int i = 0; i < 20; i++) yield return null;
        int3 under = new int3(lone.x, flat.y, lone.z);
        // Directly overhead so the lone voxel is unambiguously the only caster.
        SunCycle.SetHours(SunCycle.NoonHours);
        bool loneCasts = CpuOccluded(under, new int3(0, 1, 0), SunCycle.Direction, cam.transform.position);
        Check(loneCasts, "a single protruding voxel casts a discrete shadow");
        Note("\"crisp\" is a visual judgement and is NOT asserted -- see 07_lone_voxel_shadow.png");
        PoseLookingDownAt(cam, lone, 2.5f);
        for (int i = 0; i < 16; i++) yield return null;
        yield return Shot("07_lone_voxel_shadow");

        // --- SWEEP CONTINUITY: no cascade staleness. ---
        // §13: "the mountain shadow sweeps CONTINUOUSLY (no cascade staleness)".
        // There are no cascades to be stale -- the shadow is re-derived from
        // live terrain bytes every frame -- so what is actually checked is that
        // the shadowed fraction moves smoothly and never jumps, which is what a
        // stale or snapping cascade would produce.
        PoseGroundHorizon(cam);
        var fracs = new List<float>();
        for (float h = 6.5f; h <= 17.5f; h += 0.5f)
        {
            SunCycle.SetHours(h);
            for (int i = 0; i < 8; i++) yield return null;
            fracs.Add(ShadowedFraction());
        }
        float biggestJump = 0f; int jumpAt = -1;
        for (int i = 1; i < fracs.Count; i++)
        {
            float d = Mathf.Abs(fracs[i] - fracs[i - 1]);
            if (d > biggestJump) { biggestJump = d; jumpAt = i; }
        }
        Note($"dawn->dusk sweep, 23 steps: shadowed fraction {fracs[0] * 100f:F1}% .. " +
             $"{fracs[fracs.Count - 1] * 100f:F1}%, largest single-step change {biggestJump * 100f:F2}% " +
             $"(step {jumpAt})");
        Check(biggestJump < 0.25f,
            $"the shadow sweeps continuously -- no single 0.5 h step moves more than 25% of the " +
            $"frame (largest {biggestJump * 100f:F2}%)");

        SunCycle.SetHours(SunCycle.GoldenHourHours);
        for (int i = 0; i < 16; i++) yield return null;
        yield return Shot("08_golden_hour_sunvis");
        RaymarchFeature.UseDebugViewOverride = false;
        for (int i = 0; i < 8; i++) yield return null;
        yield return Shot("09_golden_hour_beauty");
        L("");
    }

    // =====================================================================
    // GATE 3 -- the silhouette test, as a pixel-level assertion
    // =====================================================================

    /// §6.5: "Phase 7 test: a player in a distant mountain's shadow sees the
    /// shadow edge align exactly with the visible silhouette." §13's scope for
    /// this phase requires it as "a PIXEL-LEVEL assertion, not an eyeball".
    ///
    /// Comparing two rendered images and locating edges would be an eyeball
    /// dressed as arithmetic. Instead this compares the GPU's own sun-visibility
    /// output against the CPU oracle, per sampled pixel: the CPU casts the SAME
    /// primary ray, takes the resulting hit voxel and face normal, fires its own
    /// shadow ray under §6.5's camera-distance LOD rule, and the two must agree.
    ///
    /// THIS IS WHAT CATCHES A REGRESSION TO THE 8.1 RULE. Travelled-distance LOD
    /// changes which tier a distant sample reads, which changes whether it is
    /// occluded, which shows up here as a disagreement count rather than as a
    /// judgement about an edge.
    ///
    /// Run at rung 2 (hard only) so both sides are binary: the shader's penumbra
    /// is a documented bounded approximation of the oracle's and would make a
    /// sunVis comparison ambiguous, while OCCLUSION is exact on both.
    private IEnumerator Gate3_SilhouetteAgainstTheCpuOracle()
    {
        L("GATE 3 -- §6.5's silhouette test as a PIXEL-LEVEL assertion (GPU vs the CPU oracle)");

        var cam = Camera.main;
        RaymarchFeature.ShadowQualityLevel = EngineConfig.SHADOW_LEVEL_HARD_ONLY;
        RaymarchFeature.UseDebugViewOverride = true;
        RaymarchFeature.DebugViewOverride = RaymarchFeature.DebugMode.SunVis;
        SunCycle.SetHours(SunCycle.GoldenHourHours);   // longest grazing rays
        PoseGroundHorizon(cam);
        for (int i = 0; i < 30; i++) yield return null;
        yield return new WaitForEndOfFrame();

        Texture2D tex = ScreenCapture.CaptureScreenshotAsTexture();
        int w = tex.width, h = tex.height;
        float3 camPos = new float3(cam.transform.position.x, cam.transform.position.y, cam.transform.position.z);
        float3 sun = SunCycle.Direction;

        int compared = 0, agree = 0, disagree = 0, skipped = 0;
        var mismatches = new List<string>();

        // A coarse grid rather than every pixel: each sample costs a full CPU
        // primary raycast plus a CPU shadow ray, and 2M of those would take
        // minutes. 40x24 = 960 samples spread over the whole frame is enough to
        // catch a systematic rule error, which is what this gate is for -- a
        // one-pixel edge difference is not.
        for (int gy = 0; gy < 24; gy++)
        for (int gx = 0; gx < 40; gx++)
        {
            int px = (int)((gx + 0.5f) * w / 40f);
            int py = (int)((gy + 0.5f) * h / 24f);

            Ray r = cam.ScreenPointToRay(new Vector3(px, py, 0f));
            var origin = new float3(r.origin.x, r.origin.y, r.origin.z);
            var dir = new float3(r.direction.x, r.direction.y, r.direction.z);

            var primary = RaymarchReference.TracerRaycast(origin, dir, Store,
                                                          LODConfig.TIER_OUTER_RANGE_M[2]);
            Color c = tex.GetPixel(px, py);

            // The SunVis view paints sky as a flat dark blue and night as
            // violet; both mean "no comparison to make here".
            bool gpuSky = (c.b > c.r + 0.05f);
            if (!primary.hit || gpuSky) { skipped++; continue; }

            var sr = ShadowRayReference.TraceHard(Store, primary.voxelCoord, primary.normal, sun, camPos);
            bool cpuOccluded = sr.occluded;
            bool gpuOccluded = c.r < 0.5f;

            compared++;
            if (cpuOccluded == gpuOccluded) agree++;
            else
            {
                disagree++;
                if (mismatches.Count < 12)
                    mismatches.Add($"px({px},{py}) voxel{primary.voxelCoord} n{primary.normal} " +
                                   $"cpu={(cpuOccluded ? "shadow" : "lit")} gpu={(gpuOccluded ? "shadow" : "lit")}");
            }
        }
        Destroy(tex);

        float rate = compared > 0 ? (float)agree / compared : 0f;
        Note($"compared {compared} sampled pixels ({skipped} skipped as sky/no-hit): " +
             $"{agree} agree, {disagree} disagree = {rate * 100f:F2}%");
        foreach (string m in mismatches) Note("  mismatch: " + m);

        // WHY NOT 100%. Two honest sources of legitimate disagreement, both
        // recorded rather than absorbed into a loose threshold:
        //   - a sample within one voxel of a shadow EDGE can land either side of
        //     it between a macro-skip traversal and a per-voxel walk;
        //   - tiers 1/2 have no CPU oracle at all (Amdt 8.10 §5.7), so a distant
        //     sample compares the shader's real downsampled cascade data
        //     against a majority vote this rig re-derives from tier 0.
        // A REGRESSION TO THE 8.1 RULE IS NOT A ONE-PIXEL EFFECT -- it moves
        // whole distant regions, which lands far below 97%.
        Check(compared > 300, $"enough sampled pixels resolved a hit to make this gate meaningful ({compared})");
        Check(rate > 0.97f,
            $"GPU sun visibility agrees with the CPU oracle on {rate * 100f:F2}% of sampled pixels (>97%)");
        yield return Shot("10_silhouette_sunvis");
        RaymarchFeature.UseDebugViewOverride = false;
        for (int i = 0; i < 8; i++) yield return null;
        yield return Shot("11_silhouette_beauty");
        L("");
    }

    // =====================================================================
    // GATE 4 -- §3.10's authoritative byte: sunlight the SAME FRAME
    // =====================================================================

    private IEnumerator Gate4_SameFrameSunlight()
    {
        L("GATE 4 -- §13: \"bomb a cave roof at noon: sunlight reaches the floor THE SAME FRAME\"");

        SunCycle.SetHours(SunCycle.NoonHours);
        RaymarchFeature.ShadowQualityLevel = EngineConfig.SHADOW_LEVEL_HARD_ONLY;
        var cam = Camera.main;
        float3 camPos;

        // A SHALLOW chamber under the same mountain, so one bomb can reach
        // daylight -- and clamped, like the main chamber, so the sphere cannot
        // reach below y=0.
        int3 roofXZ = new int3(_chamberCentre.x + 60, 0, _chamberCentre.z + 20);
        int roofSurface = SurfaceY(roofXZ.x, roofXZ.z);
        if (roofSurface < 20) { Fail($"no deep enough terrain at {roofXZ} for the roof gate (surface {roofSurface})"); yield break; }
        int3 roofCentre = new int3(roofXZ.x, Mathf.Max(10, roofSurface - 16), roofXZ.z);
        Note($"roof-gate chamber at {roofCentre}, local surface y={roofSurface}");
        DigChamber(roofCentre, 8);
        for (int i = 0; i < 30; i++) yield return null;

        int3 floorCell = roofCentre + new int3(0, -7, 0);
        int savedSurface = _surfaceY;
        _surfaceY = roofSurface;     // the shaft is carved to THIS mountain's surface
        PoseInChamberAt(cam, roofCentre);
        camPos = new float3(cam.transform.position.x, cam.transform.position.y, cam.transform.position.z);
        for (int i = 0; i < 16; i++) yield return null;
        yield return Shot("12_cave_roof_before_noon");

        bool before = CpuOccluded(floorCell, new int3(0, 1, 0), SunCycle.Direction, cam.transform.position);
        Check(before, "precondition: the cave floor is in shadow before the roof is broken");

        // Break the roof through the real §8.3 path, then check on the VERY
        // NEXT FRAME. §3.10: the terrain byte IS the current material, so a
        // shadow ray reading it needs no invalidation, no cascade refresh and
        // no propagation step -- "the same frame" is a property of the design,
        // and this is the assertion that it actually holds.
        // THE SHAFT HAS TO BE WIDER THAN THE SUN'S DRIFT, AND AT +-3 IT WAS NOT.
        // The sun at noon is not vertical -- sunArcTilt leans the arc toward +Z
        // precisely so shadows swing rather than only shorten -- so a ray
        // climbing from the floor to the surface drifts sideways by
        // (rise * tilt / up). Over the ~20 voxels from floor to surface that is
        // ~5 voxels of drift, and a +-3 shaft meant the ray left through the
        // shaft WALL and was correctly reported as still occluded. The rig was
        // wrong, not the shadow ray. The half-width is now derived from the
        // actual sun vector and the actual rise, with slack, instead of typed.
        float3 sunNoon = SunCycle.Direction;
        int rise = Mathf.Max(1, _surfaceY - floorCell.y);
        int driftX = Mathf.CeilToInt(Mathf.Abs(sunNoon.x / Mathf.Max(0.05f, sunNoon.y)) * rise);
        int driftZ = Mathf.CeilToInt(Mathf.Abs(sunNoon.z / Mathf.Max(0.05f, sunNoon.y)) * rise);
        int halfX = driftX + 3, halfZ = driftZ + 3;
        Note($"sun {sunNoon}; a ray rising {rise} voxels drifts ({driftX},{driftZ}), " +
             $"so the shaft is carved at +-({halfX},{halfZ})");

        int broken = 0;
        for (int dx = -halfX; dx <= halfX; dx++)
        for (int dz = -halfZ; dz <= halfZ; dz++)
        for (int y = roofCentre.y; y <= _surfaceY + 2; y++)
            if (_edits.TrySetVoxel(new int3(roofCentre.x + dx, y, roofCentre.z + dz), Materials.Air)) broken++;

        yield return null;   // EXACTLY one frame

        bool after = CpuOccluded(floorCell, new int3(0, 1, 0), SunCycle.Direction, cam.transform.position);
        Check(!after,
            $"sunlight reaches the cave floor the SAME FRAME the roof is broken " +
            $"({broken} voxels removed, §3.10's authoritative byte)");
        for (int i = 0; i < 16; i++) yield return null;
        yield return Shot("13_cave_roof_after_noon");
        _surfaceY = savedSurface;

        Note("The \"pour water off a ledge -> moving shadow\" half is a CONSEQUENCE of the same");
        Note("  §3.10 rule and of nothing else: fluid writes terrain bytes, and shadow rays read");
        Note("  terrain bytes. There is no fluid-aware shadow code to test. It is exercised in the");
        Note("  Playground (see PHASE_7_COMPLETION.md's FIVE-MINUTE REVIEW) and under live fluid");
        Note("  by run-phase7-lighting-ab.sh's combined arm.");
        L("");
    }

    // =====================================================================
    // GATE 5 -- §3.8's cap and its degradation
    // =====================================================================

    private IEnumerator Gate5_LightCapDegradation()
    {
        L("GATE 5 -- §3.8: over the cap, FURTHEST-FROM-PLAYER lights drop first, not silent drops");

        var cam = Camera.main;
        PoseInChamber(cam);
        for (int i = 0; i < 16; i++) yield return null;

        _driver.Lights.Clear();
        int cap = EngineConfig.MAX_ACTIVE_LIGHTS;

        // Exactly the cap, all at a known distance, placed through the §8.3 path.
        int3 near = _chamberCentre + new int3(0, -8, 0);
        int placed = 0;
        for (int i = 0; i < cap; i++)
        {
            int3 at = near + new int3((i % 8) - 4, (i / 8) - 2, 6);
            if (_edits.TrySetVoxel(at, Materials.Torch)) placed++;
        }
        for (int i = 0; i < 10; i++) yield return null;
        Check(_driver.Lights.RegisteredCount == cap,
            $"exactly MAX_ACTIVE_LIGHTS torches registered ({_driver.Lights.RegisteredCount} of {cap}, {placed} written)");
        Check(_driver.Lights.ActiveCount == cap,
            $"all {cap} are ACTIVE at the cap ({_driver.Lights.ActiveCount})");
        Check(!_driver.Lights.CapIsBinding, "the cap is not yet binding at exactly the cap");
        yield return Shot("14_cap_at_limit");

        // Now place more, FAR away. The furthest must drop, not the new ones.
        int extra = 0;
        for (int i = 0; i < 16; i++)
            if (_edits.TrySetVoxel(near + new int3(i - 8, 0, 40), Materials.Torch)) extra++;
        for (int i = 0; i < 10; i++) yield return null;

        Check(_driver.Lights.RegisteredCount == cap + extra,
            $"over-cap torches are still REGISTERED, not silently ignored " +
            $"({_driver.Lights.RegisteredCount} registered, {extra} over the cap)");
        Check(_driver.Lights.ActiveCount == cap,
            $"the ACTIVE set is still exactly the cap ({_driver.Lights.ActiveCount})");
        Check(_driver.Lights.CapIsBinding, "the rig reports the cap as binding, so the HUD can too");

        // And one placed right at the player must displace a far one.
        int3 atFeet = CoordMath.WorldToVoxel(new float3(cam.transform.position.x,
                                                        cam.transform.position.y - 0.3f,
                                                        cam.transform.position.z));
        _edits.TrySetVoxel(atFeet, Materials.Torch);
        for (int i = 0; i < 10; i++) yield return null;
        Check(_driver.Lights.IsRegistered(atFeet),
            "a torch placed at the player's feet while over the cap is registered");
        Check(_driver.Lights.ActiveCount == cap,
            "the active set stays at the cap after the displacement");
        Note("Whether the DISPLACED light was the furthest is proven exhaustively in EditMode " +
             "(LightingTests.Cap_FurthestLightsDropFirst, mutation-checked M3); this gate proves " +
             "the same rule survives the real edit path and the real upload.");
        yield return Shot("15_cap_over_limit");
        L("");
    }

    // =====================================================================
    // Report
    // =====================================================================

    private IEnumerator Report()
    {
        L("=========================================================");
        L($"PASS {_pass}  FAIL {_fail}    screenshots: {_shots}");
        L("");
        L("--- COMMIT READINESS (§13 Phase 7) ---");
        L($"  CORRECTNESS PROVEN : {(_fail == 0 ? "YES" : "NO")} -- {_pass} gate assertions, {_fail} failures.");
        L("                       Gate 3 is a per-pixel GPU-vs-CPU-oracle comparison, which is what");
        L("                       makes the §6.5 silhouette requirement an assertion rather than a look.");
        L("  PERFORMANCE        : NOT MEASURED HERE, DELIBERATELY. §13's two budget gates");
        L("                       (raymarch+shadows <=9.0 ms, combined <=13.0 ms, both \"Performance");
        L("                       State checked\") CANNOT BE RUN AS WRITTEN in this workflow: no Xcode");
        L("                       (Amdt 8.9 Rule 1), Performance State unreadable (Rule 2),");
        L("                       gpuFrameTime inflated ~2.6-2.7x (Amdt 8.10), Unity merges dispatches");
        L("                       into one Metal encoder. See run-phase7-lighting-ab.sh and");
        L("                       AMENDMENT_8_12_PHASE7_GATE.md for the substitute and what it can and");
        L("                       cannot establish. NO per-pass ms figure is quoted against §11.2.");
        L("  VISUAL             : NOT COVERED BY ANY ASSERTION HERE. Whether a shadow reads as");
        L("                       \"crisp\", whether the penumbra width is pleasing, whether golden");
        L("                       hour looks like golden hour, and whether the cave is dark in an");
        L("                       atmospheric rather than merely dim way are the owner's judgement.");
        L("                       Screenshots 01-15 are the evidence to look at, and");
        L("                       04_torch_through_wall_FLAGGED.png in particular is a DECISION, not");
        L("                       a defect.");
        L("");
        L(_fail == 0
            ? "  => Correctness gate GREEN. The remaining questions are the owner's: the shipping"
            : "  => Correctness gate RED. Do not commit; read the FAIL lines above.");
        if (_fail == 0)
            L("     quality rung, the two §6.5 ambiguities, and the art constants.");

        File.WriteAllText(Path.Combine(_outDir, "phase7_report.txt"), _log.ToString());
        Debug.Log("[p7] report -> " + _outDir);
        yield return null;
        Application.Quit(_fail == 0 ? 0 : 1);
    }

    // =====================================================================
    // helpers
    // =====================================================================

    private static float[] ReadDebug()
    {
        var d = new float[128];
        if (RaymarchFeature.DebugBuffer != null) RaymarchFeature.DebugBuffer.GetData(d);
        return d;
    }

    private bool CpuOccluded(int3 voxel, int3 normal, float3 sun, Vector3 camPos)
    {
        var c = new float3(camPos.x, camPos.y, camPos.z);
        return ShadowRayReference.TraceHard(Store, voxel, normal, sun, c).occluded;
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

    /// The DEEPEST terrain near `from` -- i.e. a mountain, which is what §13's
    /// scene asks to have in view and also the only place a chamber can be
    /// carved with rock above AND below it.
    ///
    /// EXISTS BECAUSE THE FIRST VERSION PUT THE CHAMBER BELOW THE WORLD. It
    /// used the spawn's own surface height minus a constant; the spawn is near
    /// the coast at y=38, so the chamber landed at y=-7 where no chunk exists.
    /// Siting it from a measured maximum instead of a typed offset means the
    /// depth is a property of the terrain rather than of a number that happened
    /// to work somewhere else.
    private int3 DeepestGroundNear(int3 from, int searchRadius)
    {
        int3 best = new int3(from.x, 0, from.z);
        int bestY = -1;
        for (int dz = -searchRadius; dz <= searchRadius; dz += 10)
        for (int dx = -searchRadius; dx <= searchRadius; dx += 10)
        {
            int x = from.x + dx, z = from.z + dz;
            int y = SurfaceY(x, z);
            if (y > bestY) { bestY = y; best = new int3(x, y, z); }
        }
        return best;
    }

    /// Carves a flat platform of Stone, `half` voxels either way, with clear air
    /// above it, and returns its top surface cell.
    ///
    /// THE ACNE TEST NEEDS A PLANE, NOT A FLAT-ISH PATCH. §6.5 claims acne is
    /// impossible BY CONSTRUCTION, so the assertion has to be zero -- and a zero
    /// assertion is only meaningful if every shadowed pixel in the measured
    /// region would genuinely be acne. Searching for flat terrain cannot give
    /// that: at 2 degrees of sun elevation a bump ten metres outside the
    /// verified patch still throws a shadow across it. Carving the plane makes
    /// the premise true instead of probable, and it does it through the real
    /// §8.3 edit path.
    private int3 CarveFlatPlatform(int3 near, int half)
    {
        int y = SurfaceY(near.x, near.z);
        if (y < 1) y = _surfaceY;
        int filled = 0, cleared = 0;
        for (int dz = -half; dz <= half; dz++)
        for (int dx = -half; dx <= half; dx++)
        {
            int3 c = new int3(near.x + dx, y, near.z + dz);
            // Solid at exactly y, and everything above it air for 40 voxels, so
            // nothing within reach can cast onto the plane.
            if (_edits.TrySetVoxel(c, Materials.Stone)) filled++;
            for (int up = 1; up <= 40; up++)
                if (_edits.TrySetVoxel(c + new int3(0, up, 0), Materials.Air)) cleared++;
        }
        var top = new int3(near.x, y, near.z);
        Note($"carved a {2 * half + 1}x{2 * half + 1} flat platform at y={y} " +
             $"({filled} surface voxels set, {cleared} voxels of air cleared above it)");
        return top;
    }

    /// Carves a sealed spherical chamber through the REAL §8.3 edit path.
    private void DigChamber(int3 centre, int radius)
    {
        int r2 = radius * radius;
        int n = 0;
        for (int dz = -radius; dz <= radius; dz++)
        for (int dy = -radius; dy <= radius; dy++)
        for (int dx = -radius; dx <= radius; dx++)
        {
            if (dx * dx + dy * dy + dz * dz > r2) continue;
            if (_edits.TrySetVoxel(centre + new int3(dx, dy, dz), Materials.Air)) n++;
        }
        Note($"carved a chamber of radius {radius} at {centre}: {n} voxels removed via EditService");
    }

    /// Carves Gate 1's falloff corridor: a straight air tunnel from the torch
    /// out along one horizontal axis, long enough for the farthest pose plus a
    /// margin, and 7 voxels wide x 9 tall around the camera's line of sight.
    /// Returns the axis (unit int3, pointing from the torch toward the camera).
    ///
    /// SEALED, NOT JUST CARVED. A corridor that broke out of the mountain's
    /// flank would let daylight in, and daylight at the far pose would make the
    /// falloff non-monotone for a reason that has nothing to do with C.11. So
    /// each of the four horizontal axes is checked for at least MinRoof voxels
    /// of terrain over every corridor column BEFORE anything is carved, and the
    /// first sealed one is used. No sealed axis is reported as a failed
    /// PRECONDITION rather than silently measuring a leaky tunnel.
    private int3 CarveSightCorridor(int3 torchAt, float farthestM, float liftM)
    {
        const int HalfWidth = 3, Below = 2, Above = 6, MinRoof = 6;
        int lift = Mathf.RoundToInt(liftM * 10f);
        int length = Mathf.CeilToInt(farthestM * 10f) + 8;
        int top = torchAt.y + lift + Above;
        var axes = new[] { new int3(0, 0, -1), new int3(0, 0, 1), new int3(-1, 0, 0), new int3(1, 0, 0) };
        int3 chosen = axes[0];
        int chosenRoof = int.MinValue;
        foreach (var a in axes)
        {
            int3 side = new int3(a.z, 0, a.x);   // perpendicular in XZ
            int minRoof = int.MaxValue;
            for (int s = 1; s <= length; s++)
            for (int w = -HalfWidth; w <= HalfWidth; w++)
            {
                int3 c = torchAt + a * s + side * w;
                minRoof = Mathf.Min(minRoof, SurfaceY(c.x, c.z) - top);
            }
            if (minRoof > chosenRoof) { chosenRoof = minRoof; chosen = a; }
            if (minRoof >= MinRoof) break;
        }
        Check(chosenRoof >= MinRoof,
            $"PRECONDITION: a sealed falloff corridor exists (axis {chosen}, " +
            $"thinnest roof {chosenRoof} voxels, need >= {MinRoof})");

        int3 perp = new int3(chosen.z, 0, chosen.x);
        int n = 0;
        for (int s = 1; s <= length; s++)
        for (int w = -HalfWidth; w <= HalfWidth; w++)
        for (int dy = -Below; dy <= lift + Above; dy++)
            if (_edits.TrySetVoxel(torchAt + chosen * s + perp * w + new int3(0, dy, 0), Materials.Air)) n++;
        Note($"carved a {length}-voxel sealed sight corridor along {chosen} from the torch " +
             $"({n} voxels removed; thinnest roof {chosenRoof} voxels)");
        return chosen;
    }

    /// The camera `distanceM` from `target` along `axis`, lifted `liftM`,
    /// looking back at the target.
    private static void PoseLookingAlong(Camera cam, int3 target, int3 axis, float distanceM, float liftM)
    {
        var t = new Vector3(target.x * 0.1f, target.y * 0.1f, target.z * 0.1f);
        cam.transform.position = t + new Vector3(axis.x, 0f, axis.z) * distanceM + new Vector3(0f, liftM, 0f);
        cam.transform.rotation = Quaternion.LookRotation((t - cam.transform.position).normalized, Vector3.up);
    }

    private static void PoseGroundHorizon(Camera cam)
    {
        // Amendment 8.9's "real gameplay camera": standing on the ground looking
        // at the skyline. Consistently the most expensive pose in every config,
        // which is why every gate that wants a hard case uses it.
        Vector3 p = cam.transform.position;
        cam.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
        cam.transform.position = new Vector3(p.x, p.y, p.z);
    }

    private void PoseInChamber(Camera cam) => PoseInChamberAt(cam, _chamberCentre);

    private static void PoseInChamberAt(Camera cam, int3 centre)
    {
        cam.transform.position = new Vector3(centre.x * 0.1f, centre.y * 0.1f, centre.z * 0.1f);
        cam.transform.rotation = Quaternion.Euler(15f, 0f, 0f);
    }

    private static void PoseLookingAt(Camera cam, int3 target, float distanceM)
    {
        var t = new Vector3(target.x * 0.1f, target.y * 0.1f, target.z * 0.1f);
        cam.transform.position = t + new Vector3(0f, 0.3f, -distanceM);
        cam.transform.rotation = Quaternion.LookRotation((t - cam.transform.position).normalized, Vector3.up);
    }

    private static void PoseLookingDownAt(Camera cam, int3 target, float heightM)
    {
        var t = new Vector3(target.x * 0.1f, target.y * 0.1f, target.z * 0.1f);
        cam.transform.position = t + new Vector3(0f, heightM, -heightM * 0.6f);
        cam.transform.rotation = Quaternion.LookRotation((t - cam.transform.position).normalized, Vector3.up);
    }

    /// Mean perceptual luminance of the current frame. The luminance weights are
    /// Rec. 709's, so "brighter" means brighter to an eye rather than to a sum
    /// of channels -- a torch is warm, and an unweighted sum would over-report it.
    private float AverageLuminance()
    {
        Texture2D tex = ScreenCapture.CaptureScreenshotAsTexture();
        try
        {
            Color[] px = tex.GetPixels();
            double sum = 0;
            for (int i = 0; i < px.Length; i += 7)      // every 7th pixel; this is a mean, not a census
                sum += 0.2126 * px[i].r + 0.7152 * px[i].g + 0.0722 * px[i].b;
            return (float)(sum / (px.Length / 7 + 1));
        }
        finally { Destroy(tex); }
    }

    /// Shadowed fraction of the CENTRE of the frame only.
    ///
    /// The acne gate points the camera at a carved platform, but the frame also
    /// contains the horizon, and at a 2-degree sun elevation the horizon is
    /// legitimately full of very long real shadows. Measuring the whole frame
    /// therefore measures terrain, which is what the first version of that gate
    /// did (2.01% against a 2% threshold -- a near-miss that was not acne at
    /// all). Restricting to the middle 30% keeps only the platform in view.
    private float ShadowedFractionCentre()
    {
        Texture2D tex = ScreenCapture.CaptureScreenshotAsTexture();
        try
        {
            int w = tex.width, h = tex.height;
            int x0 = (int)(w * 0.35f), x1 = (int)(w * 0.65f);
            int y0 = (int)(h * 0.35f), y1 = (int)(h * 0.65f);
            int hits = 0, dark = 0;
            for (int y = y0; y < y1; y += 2)
            for (int x = x0; x < x1; x += 2)
            {
                Color c = tex.GetPixel(x, y);
                if (c.b > c.r + 0.05f) continue;      // sky or night
                hits++;
                if (c.r < 0.5f) dark++;
            }
            return hits > 0 ? (float)dark / hits : 0f;
        }
        finally { Destroy(tex); }
    }

    /// Fraction of HIT pixels reading as shadowed in the SunVis debug view.
    /// Sky (flat dark blue) and night (violet) are excluded -- both have a blue
    /// channel above red, which greyscale sunVis never does.
    private float ShadowedFraction()
    {
        Texture2D tex = ScreenCapture.CaptureScreenshotAsTexture();
        try
        {
            Color[] px = tex.GetPixels();
            int hits = 0, dark = 0;
            for (int i = 0; i < px.Length; i += 5)
            {
                Color c = px[i];
                if (c.b > c.r + 0.05f) continue;       // sky or night
                hits++;
                if (c.r < 0.5f) dark++;
            }
            return hits > 0 ? (float)dark / hits : 0f;
        }
        finally { Destroy(tex); }
    }

    private IEnumerator Shot(string name)
    {
        for (int i = 0; i < 4; i++) yield return null;
        yield return new WaitForEndOfFrame();
        Texture2D tex = ScreenCapture.CaptureScreenshotAsTexture();
        try { File.WriteAllBytes(Path.Combine(_outDir, name + ".png"), tex.EncodeToPNG()); }
        finally { Destroy(tex); }
        _shots++;
    }
}
