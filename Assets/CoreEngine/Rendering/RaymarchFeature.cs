// ==========================================
// Assets/CoreEngine/Rendering/RaymarchFeature.cs
//
// PHASE 4 REVISION. Additive only -- no traversal behaviour changes. New
// bindings for the sliding window and the chunk-major clipmap layout:
//   _WindowDimsChunksPacked   window size in chunks (chunk-major indexing)
//   _WindowOriginBricksPacked window minimum corner in bricks (bounds guard)
//   _ContentCeilingVoxelY     replaces the hardcoded 128 early-exit
//   _CascadeTierInfo1/2       coarse-brick geometry per cascade tier
// See PATCH_phase4_shaders.md for the matching shader edits.
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;
using VoxelEngine.Mirror;

public partial class RaymarchFeature : ScriptableRendererFeature
{
    public enum DebugMode
    {
        Beauty = 0,
        StepHeat = 1,
        UniformDense = 2,
        Normals = 3,
        NonExitHeat = 4,
        VoxelGrain = 5,
        LODTier = 6,
        // §10.3 item 6, Phase 7: "sun-visibility view + shadow-ray step-count
        // heat". Appended, never inserted -- RaymarchDebugControls, the capture
        // rigs and several report files all key off these integers.
        SunVis = 7,
        ShadowHeat = 8,
        // Far band (FarField.cs), appended per the rule above.
        FarBand = 9,
        FarHeat = 10,
        VoxelStop = 11,
        // Task 93b3 (diagnostic, CSMainFarShade only): where the far march starts, shell march heat.
        FarStart = 12,
        ShellHeat = 13,
        // Task 129 (diagnostic, CSMainFarShade only): hit tier + distance codes for -lodref.
        HitInfo = 14,
    }

    public enum GateResMode { UseInspector, Native, Forced960x540, ForcedCustom }
    public static GateResMode GateModeOverride = GateResMode.UseInspector;
    public static Vector2Int CustomGateResolution = new Vector2Int(960, 540);

    [Tooltip("Runtime debug view. Beauty is the shipped output.")]
    public DebugMode debugMode = DebugMode.Beauty;

    [Header("Gate measurement resolution")]
    [SerializeField] private bool _forceGateResolution = true;
    [SerializeField] private int _gateWidth = 960;
    [SerializeField] private int _gateHeight = 540;

    public static bool AirMipEnabled = true;

    // CERTIFIED DEFAULT: mode 4 (DenseSkip). Amendment 8.9 recorded it as the
    // fastest measured mode and the one with a CPU oracle fuzz proof.
    public static int TraversalMode = 4;

    // Phase 3 PATCH_iteration_cap: 400 -> 1024. The .compute clamp that pinned
    // the effective cap at 400 was removed in the same patch.
    public static int MaxOuterIterations = 1024;

    public static bool UseStrippedKernel = false;
    public static bool UseMemoryProbeKernel = false;
    public static int ProbeIterations = 14;

    public static bool UsePackedMips = true;
    public static float MaxRayDistance = 1280f;
    public static bool UseLODCascade = true;

    /// Amendment 8.14 step 11b (task 80): draw the coarse shells when a
    /// ShellStreamer is Active (`-shells 1`). While they draw, cascade tiers 1-2
    /// do not (row 2: shells replace them) and tier 0 marches to MaxRayDistance.
    /// False = the shells stay resident but the frame is the pre-11b one (the
    /// capture's OFF arm). ShellTint (-shellsdebug) tints each pixel by its shell.
    public static bool DrawShells = true;
    public static bool ShellTint = false;
    public static int ShellIterCap = 384;
    public static bool ShellsDrawnLastFrame { get; private set; }

    /// Tier boundaries: 8.9's fixed LODConfig.TIER_OUTER_RANGE_M (default) or
    /// the pixel-footprint form, recomputed per frame from the dispatch height
    /// and camera FOV (-tierbounds pixel; measured 2026-09-25, not adopted --
    /// see LODConfig). Last match wins.
    public static bool LegacyTierBounds = LastArg("-tierbounds") != "pixel";
    public static float[] LastTierBoundsM = new float[LODConfig.TIER_COUNT];
    private static string LastArg(string name)
    {
        string[] a = System.Environment.GetCommandLineArgs();
        string found = null;
        for (int i = 0; i + 1 < a.Length; i++) if (a[i] == name) found = a[i + 1];
        return found;
    }
    public static bool UseDevColors = true;

    // ---- PRESENTATION TOGGLES (shading step only) ----
    // Each gates a purely cosmetic term applied after traversal has resolved
    // the hit. Turning all three off reproduces the pre-palette look exactly.
    // They exist as toggles precisely so Phase 7's real lighting (§6.5) can
    // disable any of them without unpicking code.
    public static bool ShadeAmbientOcclusion = true;
    public static bool ShadeHemisphere = true;
    public static bool ShadeDistanceFog = true;
    /// Also the fog target and the hemisphere "up" tint. A pale, slightly cool
    /// daylight haze -- deliberately not a saturated blue, so distant terrain
    /// desaturates into it instead of turning blue.
    public static Color SkyColor = new Color(0.62f, 0.70f, 0.78f, 1f);
    public static float FogStartMetres = 45f;
    public static float FogEndMetres = 260f;

    // ---- PHASE 7 LIGHTING (§6.5, §3.8, C.9/C.10/C.11) ----
    //
    // Statics, matching every other render toggle in this file, so a rig can
    // set them before the first frame without a scene reference. The VALUES
    // come from LightingConfig (live-reloadable JSON) and SunCycle every frame;
    // these are the seams a rig or the Playground drives.

    /// The A.6 light buffer this feature binds. Owned by whoever builds the
    /// world (the bootstrapper / Playground), not by the renderer -- the
    /// renderer only reads it, which keeps §0.1 invariant 1's direction of
    /// travel obvious: lights are a consequence of terrain edits.
    public static VoxelEngine.Rendering.LightSourceBuffer Lights;

    /// §6.5's fallback ladder. Defaults to EngineConfig's shipped rung; a rig
    /// overrides it per process launch so Stage 6 can measure every rung from
    /// ONE binary in ONE session.
    public static int ShadowQualityLevel = EngineConfig.SHADOW_QUALITY_LEVEL;

    /// Runaway guard for the shadow DDA, in OUTER iterations -- the same unit
    /// as MaxOuterIterations, and deliberately the same value. A shadow ray
    /// crosses the same clipmap with the same leap primitives, so there is no
    /// basis for a different number, and two numbers would invite one to drift.
    public static int MaxShadowSteps = 1024;

    /// C.9 attaches `max(0, N.L)` to the SUN term only and leaves `pointLights`
    /// bare. This was shipped at the literal reading (false) and FLAGGED rather
    /// than decided -- PHASE_7_COMPLETION.md §10 flag 3.
    ///
    /// DEFAULT FLIPPED TO TRUE, 2026-09-13, ON EVIDENCE RATHER THAN TASTE.
    /// The evidence is §8's own screenshot finding: in
    /// 02_cave_lit_by_torch.png every face of the chamber is lit identically
    /// regardless of which way it points, because with no N.L the only thing
    /// modulating a point light is distance. A cube lit from one side that is
    /// equally bright on all six faces does not read as lit at all -- it reads
    /// as tinted. §6.1's aesthetic contract is explicit that the look is carried
    /// by "albedo, the lighting of §6.5, and voxel ambient occlusion", and a
    /// lighting term that cannot distinguish a face from its opposite is not
    /// carrying anything.
    ///
    /// WHY THIS IS A DEFAULT FLIP AND NOT A SPEC DEVIATION TO ARGUE ABOUT: the
    /// toggle is unchanged and still reachable, false still reproduces C.9's
    /// literal grouping exactly, and both states are screenshotted side by side
    /// so the owner can reverse it in one edit. It is reversible, evidence-
    /// backed, and pinned by a test so it cannot drift back silently.
    public static bool PointLightNdotL = true;

    /// FLAG: modulate ambient by a straight-up sky-visibility probe so deep
    /// caves are genuinely dark (§6.5). It IS a second ray; measured as its own
    /// arm rather than assumed free.
    public static bool UseSkyProbeAmbient = true;

    /// TASK 138 (owner Q89 (a); `-skytilt 1`): the sky probe leans out of the face -- side faces
    /// 45 deg up and out, down faces horizontally toward the viewer, up faces straight up. Off = the
    /// vertical probe for every face; 2 = side faces only. CPU twin: ShadowRayReference.SkyProbeDir.
    public static int SkyProbeTilt = ReadSkyTiltArg();

    private static int ReadSkyTiltArg()
    {
        string[] a = System.Environment.GetCommandLineArgs();
        int v = 0;
        for (int i = 0; i + 1 < a.Length; i++)
            if (a[i] == "-skytilt" && int.TryParse(a[i + 1], out int k)) v = k;
        return v;
    }

    /// Highest world voxel Y that generation can produce content for, +1.
    /// Set by the bootstrapper from StreamManager.MAX_GENERATED_CHUNK_Y.
    /// Kept tighter than the window's Y extent on purpose -- see the shader.
    public static int ContentCeilingVoxelY = 128;
    /// Lowest voxel Y the voxel band holds (V2). 0 for a flat world, which is
    /// the shader's old hard floor; a vertically streamed world sets both
    /// bounds to its loaded band every frame, and rays leaving the band hand
    /// over to the far band.
    public static int ContentFloorVoxelY = 0;
    /// One light colour per voxel: faces differ in brightness only (owner,
    /// 2026-09-26). -facehue 0 restores the per-face colour for A/B.
    public static bool UniformFaceHue = ReadIntArg("-facehue", 1) != 0;

    public static DebugMode DebugViewOverride = DebugMode.Beauty;
    public static bool UseDebugViewOverride = false;

    public static Vector2Int LastDispatchResolution = new Vector2Int(0, 0);
    /// Primary raymarch dispatches executed so far (task 47's smoke checks
    /// that every path really traces a new image every frame).
    public static int PrimaryDispatchCount;

    /// Stage F's branch-engagement counters. Off by default: the per-pixel
    /// InterlockedAdd that fills them is expensive and would contaminate any
    /// timing arm. Enabled only by the -shadowstats diagnostic.
    /// Forces a tonemap curve regardless of the live config, so a capture pass
    /// can shoot the same preset with and without. -1 = use the config.
    public static int TonemapOverride = -1;

    /// Rig overrides for Stage F's two levers. -1 = use the live config.
    public static float ShadowMaxDistanceOverrideM = -1f;
    public static float Rung1DistanceOverrideM = -1f;

    /// Shadow LOD coarsening by ray TRAVEL as well as camera distance.
    /// &lt; 0 defers to LightingConfig; 0 disables (the shipped 8.2 behaviour, so
    /// every historical measurement stays reproducible); 1 is the
    /// penumbra-derived threshold. See Raymarch.compute's note on
    /// _ShadowTravelLOD for why max() of the two rules cannot reintroduce 8.1's
    /// detached shadows.
    public static float ShadowTravelLodOverride = -1f;

    /// Within-material colour variation (§M.4 item 1). &lt; 0 defers to
    /// LightingConfig; 0 off, 1 on.
    public static int MaterialVariationOverride = -1;
    /// >= 0 forces LightingConfig.look without touching the file (the
    /// Playground's U key and the -phase7demo A/B shots). -1 = use the config.
    public static int LookOverride = -1;
    /// >= 0 forces LightingConfig.emissiveBleed without touching the file. -1 = use the config.
    public static int EmissiveBleedOverride = -1;
    /// Task 66b: with the eye inside water, oil, sludge, acid, honey or quicksand the
    /// primary ray passes through that fluid and fogs by the distance (Raymarch.compute
    /// `passMat`). False = the old view (black). Play sets it from
    /// PlaySettings.underwaterFog; rigs keep this default and are never under a fluid.
    public static bool UnderwaterView = true;
    /// Task 75: water, honey, oil, acid and sludge seen from above or the side are
    /// see-through, each by its own clarity (Raymarch.compute `_SeeThroughLiquids`,
    /// FluidExtinction; the far band too). OFF by default so rigs and capture runs
    /// keep today's look byte for byte; play sets it from PlaySettings.seeThroughLiquids
    /// (on), a capture run only with `-seethrough 1`.
    public static bool SeeThroughLiquids = false;
    /// DIAGNOSTIC (task 75b's cost split arms see_noblend / see_nofar / see_nomarch):
    /// bits that skip one part of the see-through work -- 1 the end blend behind a
    /// voxel surface, 2 the far band's blend, 4 the march through the fluid (the
    /// blend then runs on the surface itself). Sent as _SeeThroughLiquids bits 1-3.
    /// Never set outside the cost split.
    public static int DebugSeeSkip = 0;
    /// DIAGNOSTIC (task 93b4's cost split arms shellshadow_off / shellmarch_off; the
    /// far-shading kernel only): bit 0 skips the shell-hit sun-shadow march, bit 1 the
    /// primary shell march and the far march after it, bit 2 turns ShellMarch's read caches off
    /// (shellcache_off: same image, the pre-93b4 reads), bit 3 ShellMarch's air-brick mask
    /// (shellmask_off, task 93b5: same image, the pre-93b5 iterations), bit 4 the exit-plane shell leap
    /// without the mask (shellleap_new = bits 3 + 4: what that leap costs; the mask turns it on).
    /// Task 130 (PERF_SHELL_MARCH_PLAN E1/E2): bit 5 (with bit 1) hands each pixel's shell-march input to
    /// CSShellProbe, dispatched after the primary pass, which marches it alone (shellsplit = 2 + 32), bit 6
    /// twice (shellsplit2 = 2 + 32 + 64); the image is wrong by design (misses draw sky): timing only.
    /// Never set outside the cost split.
    public static int DebugShellSkip = 0;
    /// Task 76 (R2 + R5, replaces task 73's prototype): generated rivers drawn as one
    /// moving sheet -- one-voxel risers between water drawn as surface, ripples moving
    /// along the map's flow directions and foam where the profile is steep, on near
    /// voxels, coarse tiers and the far band alike (Raymarch.compute `_RiverLook`,
    /// RiverSurface; the flow map is FarField.RiverFlow). Look 1 path. OFF by default so
    /// rigs and capture runs keep their look; play sets it from PlaySettings.riverLook (on).
    public static bool RiverLook = false;
    /// Task 85b (BUGS #9, owner Q48 (a)): FAR SHADING -- a hemisphere sky term that
    /// follows the time of day, the ground slope instead of the cube face where a
    /// 0.1 m voxel is <= 2 px, and aerial-perspective haze that light (torches,
    /// lava) mostly escapes. Look 1 path; voxels, tiers, shells and the far band
    /// (Raymarch.compute kernel CSMainFarShade, VE_FAR_SHADE). OFF by default so rigs and capture runs keep
    /// their look byte for byte; play sets it from PlaySettings.farShade (on); any
    /// other player (capture runs, rigs) only with `-farshade 1` (last match wins).
    public static bool FarShade = ReadFarShadeArg();
    /// Task 93b5: ShellMarch uses the shells' air-brick mask: an empty brick group in ONE leap,
    /// exact with the exit-plane leap it turns on (task 93b5 (b)). OFF by default: warm, vs the
    /// mask-off march p50 -2.1 ms walk_near but +0.8 ms walk_open, and the exit-plane leap moves
    /// ~0.02% of pixels by 1-7 levels (Logs/costsplit_2026-10-17_041222.txt; KNOWN_ISSUES "SHELLS
    /// COST"). `-shellmask 1` turns it on. The mask itself is always built and uploaded.
    public static bool ShellAirMask = ReadShellMaskArg();
    /// Task 99 (owner Q61 (a)): the SKY-RAY EXIT. A rising shell ray above the highest
    /// non-air brick any shell store holds (ShellStreamer.SolidTopY) -- and, for a primary
    /// ray with the far band on, 2 voxels above the far band's max height -- ends there
    /// as a no-hit: exact (Raymarch.compute ShellSkyY, ShellSkyExitTests; no-upscaler stills 0 px).
    /// ON by default: warm, ABBA, p50 -4.7 ms walk_near / -2.6 ms walk_open (noise 0.9 / 0.6;
    /// Logs/costsplit_2026-10-17_102453.txt). `-shellskyexit 0` = the march before task 99.
    public static bool ShellSkyExit = ReadShellSkyExitArg();
    /// Task 116 (owner Q66 (a), PERF_30FPS_PLAN row 1): the COLUMN SKY LEAP. A rising shell ray in
    /// an air brick at or above its group column's top (ShellStore COLUMN TOPS: 4x4 bricks of a
    /// paged chunk) crosses the rest of that column -- or of the chunk, when every column is that
    /// low -- in ONE leap (Raymarch.compute ShellMarch, ShellColumnLeapTests). Exact (no-upscaler stills
    /// 0 px at walk_near / walk_open). OFF by default (task 116's rule: < 1 ms at both poses): warm, shell
    /// iterations per sky ray 82.2 -> 29.5 (walk_near), per pixel 30.3 -> 19.0 (walk_open), but p50 ON vs OFF
    /// +1.9 / -0.2 ms (Logs/costsplit_2026-10-22_202941.txt) and MODE=walk ABBA 41.71 vs 41.70 ms.
    /// `-shellcolleap 1` turns it on. The column tops are always built and uploaded.
    public static bool ShellColumnLeap = ReadShellColumnLeapArg();
    /// Task 137 (owner, 2026-10-24: the noon canyon shortcut, with a generality rule): the HOLE LEAP. Inside shell 1's
    /// hole and outside tier 0's layers every shell-march iteration is the same tier-0-chunk leap; Raymarch.compute
    /// ShellHoleBox derives that box from constants the shader already has (shell 1's published box and hole, the band,
    /// the sky exit, shell 1's outer bound) and ShellMarch repeats the leap there without its per-iteration tests. Same
    /// leaps bit for bit and the same iteration count, so the same image (ShellHoleLeapTests). No new data or memory.
    /// OFF by default (task 137's rule: not faster beyond noise): warm ABBA ON - OFF p50 +0.3 / -0.1 ms at noon walk_near /
    /// walk_open, -0.3 / +0.65 at sun 20 (Logs/costsplit_2026-10-24_21{3718,4843}.txt); raw stills 0 px. The hole is ~1 of the
    /// noon canyon's 8 near-ray shell iterations; the rest climb to the shells' global top. `-holeleap 1` turns it on.
    public static bool ShellHoleLeap = ReadOffFlag(System.Environment.GetCommandLineArgs(), "-holeleap");
    /// Task 132 (R1, PERF_SHELL_MARCH_PLAN §4, owner Q85 (a)): the SHELL PRE-PASS. The far-shading kernel runs
    /// twice around a small kernel (CSShellPrepass) that does the primary shell march alone: pass 1 draws every
    /// 8x8 group with no shell ray and hands the others' shell rays over, pass 2 draws those groups with the
    /// pre-pass's result (Raymarch.compute ShellPre). Exact (same rays, same march). Needs the far-shading
    /// kernel, shells on and debug view 0; +20 B/px GPU scratch (10.4 MB at 960x540), allocated only while on.
    /// ON by default (task 132's rule: faster at both poses, image byte-identical): warm ABBA p50 73.8 -> 69.6 ms
    /// walk_near, 48.8 -> 41.8 ms walk_open (Logs/costsplit_2026-10-23_175808.txt). `-shellprepass 0` = the one-pass path.
    public static bool ShellPrepass = ReadShellPrepassArg();
    /// Task 133 (owner Q83 (a)): NEAR SUN SHADOWS THROUGH THE SHELLS. A tier-0 sun-shadow ray that leaves the 0.1 m
    /// window or the band ceiling unblocked goes on through the shells (Raymarch.compute _NearShadowShells), so ground
    /// in the shadow of terrain outside the window is no longer drawn lit (task 131: a lit square round the player
    /// under a low sun). The tier-0 part is then bounded by the window and ceiling, not the primary's 128 m range.
    /// Far-shading kernel with shells drawn; both the pre-pass and the one-pass path. ON by default (task 133's rule:
    /// the lit square is gone, no-upscaler stills 0 px elsewhere, warm ABBA p50 +1.1 to +1.9 ms at sun 20 deg; noon
    /// walk_near +6.1 ms, flagged: every lit ray climbs out of the band ceiling; Logs/costsplit_2026-10-24_1{11411,12128,14818}.txt).
    /// `-nearshadowshells 0` = the window-bounded shadow before task 133.
    public static bool NearShadowShells = ParseFlagOn(System.Environment.GetCommandLineArgs(), "-nearshadowshells");
    /// Task 134 (R6, PERF_SHELL_MARCH_PLAN §4, owner Q86 (a) / Q72 (b)): the KERNEL DIET. ON: the far-shading kernels are
    /// the lean play kernels CSMainFarShade / CSShellPrepass (Raymarch.compute VE_SHELL_LEAN: no counters, no
    /// diagnostic fields or shell debug views, no _DebugShellSkip arms, no air-brick mask / plane leap / column sky
    /// leap; ShellByDistance asked per distance band). Exact. Their *Diag twins (the code before task 134) run
    /// instead while ShellDiagKernels holds; `-shelldiet 0` forces them (the cost split's diet_off arm). ON by default
    /// (task 134's rule: faster at both poses, raw stills 0 px): warm ABBA p50 69.0 -> 62.1 ms walk_near, 41.8 -> 36.3 ms
    /// walk_open (Logs/costsplit_2026-10-24_141618.txt).
    public static bool ShellKernelDiet = ParseFlagOn(System.Environment.GetCommandLineArgs(), "-shelldiet");
    /// Task 134: does this frame need the far-shading *Diag kernels? A counter window, a debug view, a
    /// _DebugShellSkip arm, the OFF-by-default experiments (-shellmask 1, -shellcolleap 1), or the diet off.
    public static bool ShellDiagKernels(int debugMode) =>
        !ShellKernelDiet || CollectShadowStats || debugMode != 0 || DebugShellSkip != 0 || ShellAirMask || ShellColumnLeap;
    /// Task 134: the far-shading kernels last frame were the *Diag twins (log / capture only).
    public static bool ShellDiagDrawnLastFrame { get; private set; }
    /// Task 132: the pre-pass ran last frame (shells on, far-shading kernel, debug view 0, flag on).
    public static bool ShellPrepassDrawnLastFrame { get; private set; }
    /// Task 132: bytes of the pre-pass scratch buffer now allocated (0 while the flag has never been on).
    public static long ShellPrepassBytes => _shellPre == null ? 0L : (long)_shellPre.count * SHELL_PRE_STRIDE;
    /// Task 119 (VISUAL_REVIEW_114 #3, owner 2026-10-22): FAR ROCK BY SLOPE. In the far-shading kernel
    /// (FarShade on) a far-band step side is drawn as its biome's rock where the ground slope
    /// (FarSlopeNormal) is steeper than ~40 deg, instead of wherever the side is deeper than 1.5 m below
    /// its top (FAR_WALL_LIP) -- that rule striped gentle land with grey dashes that crawl when moving.
    /// ON by default; `-farrockslope 0` restores the lip rule. Without far shading the lip rule is unchanged.
    public static bool FarRockSlope = ParseFarRockSlope(System.Environment.GetCommandLineArgs());
    /// Task 119 (VISUAL_REVIEW_114 #3, the real cause of the crawling far dashes): in the far-shading kernel
    /// the far band's sun-shadow ray starts one texel (8 << level voxels) above the hit, so the band's own
    /// staircase stops shadowing the foot of every step (grey 1-px dashes over gentle far land; `-farshadow 0`
    /// removed them, the rock rule above did not). Hills and cliffs taller than a texel still cast shadows.
    /// ON by default; `-farshadowlift 0` = the old shadow start. Without far shading nothing changes.
    public static bool FarShadowLift = ParseFlagOn(System.Environment.GetCommandLineArgs(), "-farshadowlift");
    /// Task 120 (VISUAL_REVIEW_114 #1 + #2 + #9, owner Q4 (a), 2026-10-22): CUBE FACE LIGHTING in the far-shading
    /// kernel (FarShade on), so near and far terrain read as the same small-cube material. Three parts, ON by default:
    /// (1) `-cubebounce 0` drops the warm ground bounce on side faces (sun colour x 0.25 x sun height x (1 - n.y));
    /// (2) `-cubefilter 0` restores task 85b's switch from cube-face to slope lighting at the live render rows'
    /// 1-2 px point (a pale band that moved with FSR 3's dynamic rows) -- ON lights far ground as the mean of its
    /// staircase's cube faces, blended in over a fixed 20-50 m; (3) `-cellhash 0` hashes the colour noise per 0.1 m
    /// voxel again instead of once per drawn cell. `-cubelook 0` turns all three off (task 85b's look).
    public static bool CubeLook = ParseFlagOn(System.Environment.GetCommandLineArgs(), "-cubelook");
    public static bool CubeBounce = CubeLook && ParseFlagOn(System.Environment.GetCommandLineArgs(), "-cubebounce");
    public static bool CubeFilter = CubeLook && ParseFlagOn(System.Environment.GetCommandLineArgs(), "-cubefilter");
    public static bool CellHash = CubeLook && ParseFlagOn(System.Environment.GetCommandLineArgs(), "-cellhash");
    public static float CubeBounceStrength = 0.25f;
    public static float CubeFilterStartM = 20f, CubeFilterFullM = 50f;
    /// Its constants (task 85b's picks): sky strength (x the sky colour the face
    /// sees), haze range and scale height (metres), and the share of the haze that
    /// light takes (0.25: a torch at haze 0.9 keeps ~78% of its light).
    public static float FarShadeSkyStrength = 1.1f;
    public static float FarShadeHazeRangeM = 3500f;
    /// Task 121 (VISUAL_REVIEW_114 #4 + #6, owner 2026-10-22): THE SKY LOOK, ON by default. `-skylook 0` = the
    /// old sky AND haze (all of task 121 off). With it on: the sky's horizon colour concentrated near the
    /// horizon, a time-of-day rim tint (SunCycle.LookSkyRimAt) and a sun disc (Raymarch.compute LookAtmosphere /
    /// LookSkyBackground). `-skyhaze 0` keeps the old haze only: range FarShadeHazeRangeM (3500 m) instead of
    /// SkyHazeRangeM, no desaturation with distance. The haze colour is the sky behind the pixel either way.
    public static bool SkyLook = ParseFlagOn(System.Environment.GetCommandLineArgs(), "-skylook");
    public static bool SkyHaze = SkyLook && ParseFlagOn(System.Environment.GetCommandLineArgs(), "-skyhaze");
    public static float SkyHazeRangeM = 1800f;
    /// Task 126 (owner Q77, 2026-10-23: dusk "apocalyptic red", sunset "a little too red"): how much of the
    /// sky rim's red is kept near and after sunset (SunCycle.LookSkyRimAt / LookSkyRimShapeAt). `-skyrimscale 1`
    /// = task 121's rim; the default calms dusk to a red-brown / violet glow and sunset a little. Golden hour
    /// and noon are the same at any value. Clamped to 0..1.
    public const float SkyRimScaleDefault = 0.5f;
    public static float SkyRimScale = Mathf.Clamp01(ReadFloatArg("-skyrimscale", SkyRimScaleDefault));
    /// Task 125 (VISUAL_REVIEW_114, the hard sea/sky line): the drawn world's EDGE FADE, ON by default (with
    /// -skyhaze). The line sits on the far band's square (~5.6 km), not the world's edge: task 121 faded the haze
    /// to full there, but the far water's sky reflection (SeeReflection) and the grade come AFTER the haze, so
    /// the sea at the edge stayed darker and bluer than the sky. With it on, the edge fade moves out of the haze
    /// and is the pixel's LAST step, to exactly the sky a miss shows there (Raymarch.compute DrawnEdgeFade).
    /// `-edgefade 0` = task 121's fade inside the haze.
    public static bool EdgeFade = SkyHaze && ParseFlagOn(System.Environment.GetCommandLineArgs(), "-edgefade");
    /// Task 122 (VISUAL_REVIEW_114 #5): per-biome surface tint (Raymarch.compute BiomeTint), ON by default.
    /// `-biometint 0` = off. The generator's border blend is TerrainSpec.biomeBlendOff (`-biomeblend 0`).
    public static bool BiomeTint = ParseFlagOn(System.Environment.GetCommandLineArgs(), "-biometint");
    public static float FarShadeHazeScaleHeightM = 1500f;
    public static float FarShadeLightHaze = 0.25f;
    /// >= 0 forces LightingConfig.torchShadowRays (-torchshadows N; 0 = no
    /// occlusion rays, the pre-task-46 leak). -1 = use the config.
    public static int TorchShadowsOverride = -1;

    /// SHADOW ROOF SKIP (Raymarch.compute's HeightMip block): exact by
    /// construction, so it changes cost, not pixels. OFF until measured;
    /// `-roofskip 1` on any player's command line turns it on (last match wins,
    /// like PlaygroundCapture's args).
    public static bool ShadowRoofSkip = ReadRoofSkipArg();
    /// DIAGNOSTIC (task 59's cost split arm roof_build): build the HeightMip
    /// pyramid every frame but leave the shadow march as with the skip OFF, so
    /// the build's cost can be told from the march's. Never set outside it.
    public static bool DebugRoofBuildOnly = false;

    /// HALF-RES SUN SHADOWS (task 61, Raymarch.compute's _ShadowHalfRes): one
    /// sun ray per 2x2 pixels at all distances, edge-aware. Play sets it from
    /// PlaySettings.shadowRes; anywhere else `-shadowres half` turns it on (last
    /// match wins). OFF by default: rigs and capture runs are unchanged.
    public static bool ShadowHalfRes = ReadShadowResArg();

    /// Build rays from URP's per-frame projection (which carries TAA/STP
    /// sub-pixel JITTER) instead of Camera.projectionMatrix. Without jitter a
    /// temporal upscaler accumulates identical samples and cannot add detail.
    /// With TAA/STP off URP's jitter matrix is identity, so this is inert; it
    /// is still OFF by default so the shipped path is byte-identical.
    public static bool UseCameraDataProjection = false;
    public static GraphicsBuffer HeightMipBuffer;
    private static int _heightMipWindowX = -1, _heightMipWindowZ = -1;
    private static int[] _heightOffsets = new int[8];

    private static bool ReadShadowResArg()
    {
        string[] a = System.Environment.GetCommandLineArgs();
        bool half = false;
        for (int i = 0; i + 1 < a.Length; i++)
            if (a[i] == "-shadowres") half = a[i + 1] == "half";
        return half;
    }

    private static bool ReadRoofSkipArg()
    {
        string[] a = System.Environment.GetCommandLineArgs();
        bool on = false;
        for (int i = 0; i + 1 < a.Length; i++)
            if (a[i] == "-roofskip") on = a[i + 1] == "1";
        return on;
    }

    private static bool ReadShellMaskArg()
    {
        string[] a = System.Environment.GetCommandLineArgs();
        bool on = false;
        for (int i = 0; i + 1 < a.Length; i++)
            if (a[i] == "-shellmask") on = a[i + 1] == "1";
        return on;
    }

    private static bool ReadShellPrepassArg() => ParseFlagOn(System.Environment.GetCommandLineArgs(), "-shellprepass");

    /// `<flag> 0|1`, last match wins; OFF unless the last value is "1".
    private static bool ReadOffFlag(string[] a, string flag)
    {
        bool on = false;
        for (int i = 0; i + 1 < a.Length; i++)
            if (a[i] == flag) on = a[i + 1] == "1";
        return on;
    }

    private static bool ReadShellColumnLeapArg()
    {
        string[] a = System.Environment.GetCommandLineArgs();
        bool on = false;
        for (int i = 0; i + 1 < a.Length; i++)
            if (a[i] == "-shellcolleap") on = a[i + 1] == "1";
        return on;
    }

    /// `-farrockslope 0|1` (last match wins); ON unless the last value is "0".
    public static bool ParseFarRockSlope(string[] a) => ParseFlagOn(a, "-farrockslope");

    /// `<flag> 0|1`, last match wins; ON unless the last value is "0".
    public static bool ParseFlagOn(string[] a, string flag)
    {
        bool on = true;
        for (int i = 0; i + 1 < a.Length; i++)
            if (a[i] == flag) on = a[i + 1] != "0";
        return on;
    }

    private static bool ReadShellSkyExitArg()
    {
        string[] a = System.Environment.GetCommandLineArgs();
        bool on = true;
        for (int i = 0; i + 1 < a.Length; i++)
            if (a[i] == "-shellskyexit") on = a[i + 1] != "0";
        return on;
    }

    private static bool ReadFarShadeArg()
    {
        string[] a = System.Environment.GetCommandLineArgs();
        bool on = false;
        for (int i = 0; i + 1 < a.Length; i++)
            if (a[i] == "-farshade") on = a[i + 1] == "1";
        return on;
    }

    /// Sizes the pyramid for the window: levels 0..5 at (X>>L)*(Z>>L) each,
    /// then a level-0-sized raw scratch. ~2.4 MB for a 512x512-brick window.
    private static void EnsureHeightMip(int nx, int nz)
    {
        if (HeightMipBuffer != null && nx == _heightMipWindowX && nz == _heightMipWindowZ) return;
        HeightMipBuffer?.Release();
        int off = 0;
        for (int L = 0; L < 6; L++) { _heightOffsets[L] = off; off += (nx >> L) * (nz >> L); }
        _heightOffsets[6] = off; off += nx * nz;
        HeightMipBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, off, sizeof(uint));
        _heightMipWindowX = nx; _heightMipWindowZ = nz;
    }
    private static Vector4 V4(Unity.Mathematics.float3 v) => new Vector4(v.x, v.y, v.z, 0f);

    /// TEMPORAL SHADOW REUSE (Raymarch.compute's header note). Off unless
    /// `-tshadow 1` (last match wins) until measured.
    public static bool TemporalShadows = ReadIntArg("-tshadow", 0) != 0;
    /// Task 124: with TemporalShadows, a tile traces its sun ray one frame in
    /// ShadowReusePeriod (1, 2, 4, 8 or 16; anything else -> 4 = the pre-124
    /// rotation) and reuses the history on the others. `-shadowreuse N`.
    public static int ShadowReusePeriod = ValidReusePeriod(ReadIntArg("-shadowreuse", 4));
    public static int ValidReusePeriod(int n) => n == 1 || n == 2 || n == 4 || n == 8 || n == 16 ? n : 4;
    private static GraphicsBuffer _tsA, _tsB, _tsDummyR, _tsDummyW;
    private static int _tsW, _tsH;
    private static bool _tsValid;
    private static uint _tsFrame;
    private static Matrix4x4 _tsPrevVP;

    /// TEMPORAL ANTI-ALIASING (Raymarch.compute's TAA block; overnight task 06).
    /// OFF unless `-taa 1` (last match wins) or Playground's F4. Beauty view
    /// only, main shader only; any other view, a resize, a light jump or a
    /// teleport drops the history. Costs W*H*56 bytes of buffers while on
    /// (29 MB at the 960x540 gate) plus one scratch colour target.
    /// CHECKERBOARD (overnight task 08): with TAA on, trace only half the
    /// primary rays each frame (alternating checker) and fill the other half
    /// from TAA's history. OFF unless `-checker 1`, which also turns TAA on.
    public static bool TaaChecker = ReadIntArg("-checker", 0) != 0;
    public static bool Taa = ReadIntArg("-taa", 0) != 0 || TaaChecker;
    public static float TaaAlphaStill = 0.1f;     // weight of the new frame with the camera still
    public static float TaaAlphaMoving = 0.4f;    // ... at TaaMotionPx of reprojected motion and beyond
    public static float TaaMotionPx = 8f;
    /// cos(0.25 deg): a sun step larger than this in one frame resets the history.
    private const float TaaSunResetDot = 0.99999048f;
    public static float TaaSharpenAmount = ReadIntArg("-taasharpen", 25) / 100f;
    public const int TaaJitterCycle = 8;
    public static Vector2 LastTaaJitter;
    private static GraphicsBuffer _taaColA, _taaColB, _taaMetaA, _taaMetaB, _taaCur, _taaDummy4, _taaDummy2;
    private static int _taaW, _taaH;
    private static bool _taaValid;
    private static uint _taaFrame;
    private static Matrix4x4 _taaPrevVP;
    private static Vector3 _taaPrevCamVox, _taaPrevSun;
    private static int _taaPrevLightSig;
    private static readonly Vector4[] _taaDirty = new Vector4[TerrainClipmap.UploadedChunksCap];

    private static float Halton(int i, int b)
    {
        float f = 1f, r = 0f;
        while (i > 0) { f /= b; r += f * (i % b); i /= b; }
        return r;
    }

    private static float ReadFloatArg(string flag, float fallback)
    {
        string[] a = System.Environment.GetCommandLineArgs();
        float v = fallback;
        for (int i = 0; i + 1 < a.Length; i++)
            if (a[i] == flag && float.TryParse(a[i + 1], System.Globalization.NumberStyles.Float,
                                               System.Globalization.CultureInfo.InvariantCulture, out float p)) v = p;
        return v;
    }

    private static int ReadIntArg(string flag, int fallback)
    {
        string[] a = System.Environment.GetCommandLineArgs();
        int v = fallback;
        for (int i = 0; i + 1 < a.Length; i++)
            if (a[i] == flag && int.TryParse(a[i + 1], out int p)) v = p;
        return v;
    }

    public static bool CollectShadowStats = false;
    public static GraphicsBuffer ShadowStatsBuffer { get; private set; }
    // [0..4] shadow counters; [5] far rays, [6] far steps (primary + far shadow), [7] far hits.
    public const int STATS_LEN = 82;   // 0-7 shadow / far; 8 + 4k .. 11 + 4k: shell k's px metric (task 80); 40-47 far start / steps (task 93b3, far-shading kernel); 48-59 shell march iterations by ray kind / step kind / reads (task 93b4, far-shading kernel); 60-61 air-brick mask groups / bricks crossed (task 93b5); 62-64 sky rays / their shell iterations / ended by the sky exit (task 99, far-shading kernel); 65-66 primary column sky leaps / of them whole-chunk (task 116); 67-76 the shell probe's per-SIMD counters (task 130, CSShellProbe, shellsplit arms only); 77-79 near sun rays handed on to the shells / their shell iterations / of them blocked (task 133); 80-81 their tier-0 chunk leaps / of them inside the hole box (task 137)
    private static readonly uint[] _statsZero = new uint[STATS_LEN];
    private static GraphicsBuffer _farDummy;
    /// Task 130 (DIAGNOSTIC): CSShellProbe's per-pixel buffer (shellsplit arms only, allocated on first
    /// use at the dispatch size, 4 B/px) and the 1-element dummy bound to CSMainFarShade otherwise.
    private static GraphicsBuffer _shellProbeIO, _shellProbeDummy;
    /// Task 132: the shell pre-pass's per-pixel entries (Raymarch.compute ShellPreEntry, 5 uints) and the
    /// 1-element dummy bound to CSMainFarShade while the pre-pass is off.
    private static GraphicsBuffer _shellPre, _shellPreDummy;
    private const int SHELL_PRE_STRIDE = 20;
    /// Bound as LightBuffer when no LightingDriver exists (the Phase 4
    /// acceptance scene, the fluid rigs). An UNSET buffer does not fail loudly
    /// on Metal: Unity logs "Property (LightBuffer) ... is not set" once and
    /// SKIPS THE DISPATCH, so every frame came out black and the acceptance
    /// rig's frame times measured an empty frame (found 2026-09-25).
    private static GraphicsBuffer _lightDummy;

    /// Zeroes the counters, so a read measures ONE frame rather than an
    /// unbounded accumulation.
    public static void ResetShadowStats()
    {
        if (ShadowStatsBuffer == null) return;
        ShadowStatsBuffer.SetData(_statsZero);
    }

    /// hit / beyondLod1 / traced / reused, for the last frame since a reset.
    public static uint[] ReadShadowStats()
    {
        var d = new uint[STATS_LEN];
        ShadowStatsBuffer?.GetData(d);
        return d;
    }

    public static GraphicsBuffer DebugBuffer { get; private set; }
    public static Vector2Int DebugPixel = new Vector2Int(700, 400);
    private const int DEBUG_BUFFER_FLOATS = 128;

    class RaymarchPass : ScriptableRenderPass
    {
        private ComputeShader _compute;
        private ComputeShader _computeStripped;
        private ComputeShader _computeMemoryProbe;
        private int _debugMode;
        private bool _forceRes;
        private int _gateW, _gateH;

        class ComputePassData
        {
            public int temporalShadows, tsValid, tsReuseMask;
            public uint tsFrame;
            public GraphicsBuffer tsRead, tsWrite;
            public Matrix4x4 tsPrevVP;
            public int taa, taaHistValid, taaDirtyCount, taaChecker, taaCheckerParity;
            public Vector2 taaJitter;
            public Matrix4x4 taaPrevVP;
            public Vector4 taaPrevCamVox, taaParams;
            public Vector4[] taaDirty;
            public GraphicsBuffer taaCur, taaMetaR, taaMetaW, taaColR, taaColW;
            public TextureHandle taaScratch;
            public int kTaaResolve, kTaaSharpen;
            // Task 47: taa != 0 with taaResolve == 0 = TAA's jitter and history
            // buffers only, for an external upscaler (RaymarchFeature.Upscalers.cs).
            public int taaResolve;
            public UpsComputeData ups;
            public int kPrimary;   // CSMain (0), or CSMainChecker with -checker, or CSMainDetail (task 24)
            public bool primaryChecker;   // kPrimary is CSMainChecker: dispatch half the width
            public ComputeShader compute;
            public Camera cam;
            public Matrix4x4 invProjection;
            public TextureHandle target;
            public int dispatchWidth;
            public int dispatchHeight;
            public GraphicsBuffer clipmapBuffer;
            public GraphicsBuffer brickDataBuffer;
            public GraphicsBuffer debugBuffer;
            public GraphicsBuffer shadowStats;
            public int collectShadowStats;
            public Vector3Int windowDimsBricks;
            public Vector3Int windowDimsChunks;
            public Vector3Int windowOriginBricks;
            public int contentCeilingVoxelY;
            public int contentFloorVoxelY;
            public int uniformFaceHue;
            public Vector2Int debugPixel;
            public int debugMode;
            public int traversalMode;
            public int useDevColors;
            public int shadeAO, shadeHemi, shadeFog;
            public Color skyColor;
            public float fogStart, fogEnd;
            public int maxOuterIterations;

            // ---- Phase 7 lighting ----
            public GraphicsBuffer lightBuffer;
            public int activeLightCount;
            public Vector4 sunDir;
            public Vector4 sunColor;
            public Vector4 ambientColor;
            public float kSoft;
            public int shadowQualityLevel;
            public int maxShadowSteps;
            public int pointLightNdotL;
            public int useSkyProbeAmbient;
            public int skyProbeTilt;
            public float undergroundAmbientFloor;
            public float shadowMaxDistVoxels;
            public float rung1DistVoxels;
            public float shadowTravelLod;
            public int materialVariation;
            public float materialVariationAmount;
            public int tonemap;
            public float exposure;
            public float tonemapWhite;
            public int look;
            public int roofSkip;
            public float shadowHalfRes;
            public GraphicsBuffer heightMip;
            public int[] heightOffsetsA, heightOffsetsB;
            public int kHeightRaw, kHeightDilate, kHeightLevel;
            public Vector4 lookSkyZenith, lookSkyHorizon, lookSkyHalo, lookSun, lookAmbient, lookMoon;
            public Vector4 lookSkyRim;   // task 121
            public Vector4 lookSkyRimShape;   // task 126
            public float lookShadowDarkness, lookHazeDensity;
            public Vector4 lookGrade;
            public int emissiveBleed;
            public int underwaterView;
            public int seeThroughLiquids;
            public int riverLook;
            public Vector4 farShadeParams;
            public GraphicsBuffer riverFlow;   // task 76: FarField.RiverFlow, or the far dummy
            public Vector4 riverMap;           // xy gen offset, z cell voxels, w cells (0 = none)
            public float riverTime;
            public int torchShadowRays;

            // ---- far band (FarField.cs) ----
            public int farEnabled, farShadow;
            public GraphicsBuffer farTexels, farMips, farStaging, farResident;
            public Vector4[] farOrigins;
            public Vector4 farParams;
            public float farPixelAngle;
            public float detailRadiusVoxels;   // task 24: 0 = hand-over at the residency edge (today)

            // ---- 8.14 shells (task 80); shellCount 0 = off, dummies bound ----
            public int shellCount, shellIterCap, shellTint, shellMaskBase, shellTopY, shellColTopBase;
            public float shellPixelAngle;
            public GraphicsBuffer shellDir, shellPages, shellBricks;
            public bool primaryFarShade;            // task 130: kPrimary is CSMainFarShade (ShellProbeIO bound)
            public int kShellProbe;                 // task 130: CSShellProbe, or -1 (not dispatched)
            public GraphicsBuffer shellProbeIO;
            public int kShellPrepass;               // task 132: CSShellPrepass, or -1 (the pre-pass is off this frame)
            public GraphicsBuffer shellPre;
            public Vector4[] shellRing, shellLo, shellHi, shellCam, shellBounds;
            public VoxelEngine.Rendering.FarField.Scatter[] farScatters;
            public int farMipMask;
            public int kFarScatter = -1, kFarMip = -1;

            public bool useStripped;
            public bool useMemoryProbe;
            public int probeIterations;

            public GraphicsBuffer airMip1;
            public GraphicsBuffer airMip2;
            public GraphicsBuffer airMip3;
            public GraphicsBuffer airMip4;
            public Vector3Int airMipDims0;
            public Vector3Int airMipDims1;
            public Vector3Int airMipDims2;
            public Vector3Int airMipDims3;
            public int airMipLevelCount;

            public GraphicsBuffer airMipPacked;
            public Vector4[] mipInfo;
            public int usePackedMips;
            public float maxRayDistance;

            public GraphicsBuffer clipmapTier1;
            public GraphicsBuffer brickDataTier1;
            public Vector3Int windowDimsCoarseTier1;
            public Vector4 cascadeTierInfo1;
            public GraphicsBuffer clipmapTier2;
            public GraphicsBuffer brickDataTier2;
            public Vector3Int windowDimsCoarseTier2;
            public Vector4 cascadeTierInfo2;
            public Vector4 tierOuterRangeVoxels;
            public int useLODCascade;
        }

        class BlitPassData { public TextureHandle source; }

        public RaymarchPass(ComputeShader compute, ComputeShader computeStripped, ComputeShader computeMemoryProbe)
        {
            _compute = compute;
            _computeStripped = computeStripped;
            _computeMemoryProbe = computeMemoryProbe;
            renderPassEvent = RenderPassEvent.AfterRenderingSkybox;
        }

        public void SetDebugMode(int mode) => _debugMode = mode;
        public void SetGateResolution(bool force, int w, int h) { _forceRes = force; _gateW = w; _gateH = h; }

        /// log2 for the small power-of-two edge sizes the cascade uses.
        private static int Log2Int(int v) { int n = 0; while ((1 << n) < v) n++; return n; }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            bool useMemoryProbe = RaymarchFeature.UseMemoryProbeKernel && _computeMemoryProbe != null;
            bool useStripped = !useMemoryProbe && RaymarchFeature.UseStrippedKernel && _computeStripped != null;
            ComputeShader activeCompute = useMemoryProbe ? _computeMemoryProbe : (useStripped ? _computeStripped : _compute);
            if (activeCompute == null || TerrainClipmap.Active == null) return;

            if (DebugBuffer == null)
                DebugBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, DEBUG_BUFFER_FLOATS, sizeof(float));
            if (ShadowStatsBuffer == null)
                ShadowStatsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, STATS_LEN, sizeof(uint));
            if (_farDummy == null)
                _farDummy = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, sizeof(uint));
            if (_shellProbeDummy == null)
                _shellProbeDummy = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, sizeof(float));
            if (_shellPreDummy == null)
                _shellPreDummy = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, SHELL_PRE_STRIDE);
            if (_lightDummy == null)
                _lightDummy = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1,
                    System.Runtime.InteropServices.Marshal.SizeOf<VoxelEngine.Memory.LightSource>());

            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            TextureHandle activeColor = resourceData.activeColorTexture;

            RenderTextureDescriptor desc = cameraData.cameraTargetDescriptor;

            int dispatchW = _forceRes ? _gateW : desc.width;
            int dispatchH = _forceRes ? _gateH : desc.height;
            desc.width = dispatchW;
            desc.height = dispatchH;
            desc.enableRandomWrite = true;
            desc.depthBufferBits = 0;
            desc.msaaSamples = 1;
            desc.useMipMap = false;

            LastDispatchResolution = new Vector2Int(dispatchW, dispatchH);

            TextureHandle target = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, "_RaymarchTarget", false);
            var taaCam = cameraData.camera;
            // Task 47: STP / FSR 3 take TAA's jitter, depth and reprojection but
            // replace its resolve. Off (the default) leaves taaOn exactly as before.
            bool upsIn = UpscaleInputs != UpscalerInputMode.Off && !useStripped && !useMemoryProbe && _debugMode == 0
                      && taaCam.cameraType == CameraType.Game;
            bool taaOn = (Taa || upsIn) && !useStripped && !useMemoryProbe && _debugMode == 0
                      && taaCam.cameraType == CameraType.Game;
            bool taaResolve = taaOn && !upsIn;
            TextureHandle taaScratch = taaResolve
                ? UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, "_RaymarchTaaCur", false)
                : TextureHandle.nullHandle;

            using (var builder = renderGraph.AddComputePass<ComputePassData>("VoxelRaymarch_Compute", out var passData))
            {
                var clip = TerrainClipmap.Active;

                passData.compute = activeCompute;
                passData.useStripped = useStripped;
                passData.useMemoryProbe = useMemoryProbe;
                passData.probeIterations = RaymarchFeature.ProbeIterations;
                passData.cam = cameraData.camera;
                passData.invProjection = (UseCameraDataProjection ? cameraData.GetProjectionMatrix(0)
                                                                  : cameraData.camera.projectionMatrix).inverse;
                passData.target = target;
                passData.dispatchWidth = dispatchW;
                passData.dispatchHeight = dispatchH;

                // Temporal shadow reuse: ping-pong history, previous camera.
                if (_tsDummyR == null)
                {
                    _tsDummyR = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, 8);
                    _tsDummyW = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, 8);
                }
                passData.temporalShadows = TemporalShadows ? 1 : 0;
                if (TemporalShadows)
                {
                    if (_tsA == null || _tsW != dispatchW || _tsH != dispatchH)
                    {
                        _tsA?.Release(); _tsB?.Release();
                        _tsA = new GraphicsBuffer(GraphicsBuffer.Target.Structured, dispatchW * dispatchH, 8);
                        _tsB = new GraphicsBuffer(GraphicsBuffer.Target.Structured, dispatchW * dispatchH, 8);
                        // (sunVis, origin key) per pixel. A slot never written (the
                        // checkerboard's other half) must never match a key: 0xFFFFFFFF.
                        var fill = new uint[dispatchW * dispatchH * 2];
                        for (int i = 1; i < fill.Length; i += 2) fill[i] = 0xFFFFFFFFu;
                        _tsA.SetData(fill); _tsB.SetData(fill);
                        _tsW = dispatchW; _tsH = dispatchH; _tsValid = false;
                    }
                    passData.tsRead = _tsA; passData.tsWrite = _tsB;
                    passData.tsValid = _tsValid ? 1 : 0;
                    passData.tsFrame = _tsFrame++;
                    passData.tsReuseMask = ValidReusePeriod(ShadowReusePeriod) - 1;
                    passData.tsPrevVP = _tsPrevVP;
                    var tc = cameraData.camera;
                    _tsPrevVP = tc.projectionMatrix * tc.worldToCameraMatrix;
                    (_tsA, _tsB) = (_tsB, _tsA);
                    _tsValid = true;
                }
                else { passData.tsRead = _tsDummyR; passData.tsWrite = _tsDummyW; }

                // TAA: ping-pong colour + "same surface?" history, previous
                // unjittered camera, this frame's jitter.
                if (_taaDummy4 == null)
                {
                    _taaDummy4 = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, 16);
                    _taaDummy2 = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, 8);
                }
                TerrainClipmap.RecordUploadedChunks = taaOn;
                passData.taa = taaOn ? 1 : 0;
                passData.taaResolve = taaResolve ? 1 : 0;
                passData.ups = null;
                passData.taaCur = _taaDummy4; passData.taaMetaW = _taaDummy2;
                passData.kPrimary = 0;
                passData.primaryChecker = false;
                if (taaOn)
                {
                    if (_taaCur == null || _taaW != dispatchW || _taaH != dispatchH)
                    {
                        _taaColA?.Release(); _taaColB?.Release(); _taaMetaA?.Release(); _taaMetaB?.Release(); _taaCur?.Release();
                        int n = dispatchW * dispatchH;
                        _taaColA = new GraphicsBuffer(GraphicsBuffer.Target.Structured, n, 16);
                        _taaColB = new GraphicsBuffer(GraphicsBuffer.Target.Structured, n, 16);
                        _taaMetaA = new GraphicsBuffer(GraphicsBuffer.Target.Structured, n, 8);
                        _taaMetaB = new GraphicsBuffer(GraphicsBuffer.Target.Structured, n, 8);
                        _taaCur = new GraphicsBuffer(GraphicsBuffer.Target.Structured, n, 16);
                        _taaW = dispatchW; _taaH = dispatchH; _taaValid = false;
                    }
                    // An upscaler's motion vectors are measured between UNJITTERED cameras.
                    Matrix4x4 proj = UseCameraDataProjection && !upsIn ? cameraData.GetProjectionMatrix(0) : taaCam.projectionMatrix;
                    Matrix4x4 vp = proj * taaCam.worldToCameraMatrix;
                    Vector3 camVox = taaCam.transform.position * 10f;
                    Unity.Mathematics.float3 sdT = SunCycle.Direction;
                    Vector3 sun = new Vector3(sdT.x, sdT.y, sdT.z).normalized;
                    var cfgT = LightingConfig.Active;
                    int bleedSig = EmissiveBleedOverride >= 0 ? EmissiveBleedOverride : cfgT.emissiveBleed;
                    int torchSig = TorchShadowsOverride >= 0 ? TorchShadowsOverride : cfgT.torchShadowRays;
                    int lightSig = ShadowQualityLevel * 31 + (LookOverride >= 0 ? LookOverride : cfgT.look) * 7
                                 + (TonemapOverride >= 0 ? TonemapOverride : cfgT.tonemap) + bleedSig * 13
                                 + torchSig * 97;
                    // A LARGE LIGHT JUMP (the sun moving > 0.25 deg in a frame, a
                    // look / shadow-rung / tonemap switch) or a teleport (> 20 m)
                    // invalidates every history colour at once. The sun bound was
                    // ~1 deg until task 07: at 0.5-0.9 deg/frame (the held time
                    // scrubber, 3 h/s, is ~0.73 deg/frame at 60 fps) the history
                    // lagged the light -- ON +2.6..+4 luma brighter than OFF while
                    // the sun set, edges 0.32-0.38x (Logs/taarisk_tg2_20261002_165730.txt).
                    // The shipped 120 s day is ~0.1 deg/frame and still accumulates.
                    bool reset = !_taaValid
                              || Vector3.Dot(sun, _taaPrevSun) < TaaSunResetDot
                              || lightSig != _taaPrevLightSig
                              || (camVox - _taaPrevCamVox).sqrMagnitude > 200f * 200f;
                    passData.taaHistValid = reset ? 0 : 1;
                    passData.taaPrevVP = _taaPrevVP;
                    passData.taaPrevCamVox = _taaPrevCamVox;
                    // Checkerboard: each jitter is held for two frames, so both
                    // halves of the checker see the whole 8-frame cycle (with one
                    // jitter per frame a pixel traced on alternate frames would see
                    // only the odd or only the even Halton indices -- for base 2,
                    // all >= 0.5 or all < 0.5: a half-pixel bias between the halves).
                    passData.taaChecker = TaaChecker && !upsIn ? 1 : 0;
                    if (TaaChecker && !upsIn) { passData.kPrimary = activeCompute.FindKernel("CSMainChecker"); passData.primaryChecker = true; }
                    passData.taaCheckerParity = (int)(_taaFrame & 1u);
                    int hi = (int)((TaaChecker ? (_taaFrame >> 1) : _taaFrame) % TaaJitterCycle) + 1;
                    LastTaaJitter = upsIn ? UpsJitter(_taaFrame, dispatchW, dispatchH, cameraData)
                                          : new Vector2(Halton(hi, 2) - 0.5f, Halton(hi, 3) - 0.5f);
                    _taaFrame++;
                    passData.taaJitter = LastTaaJitter;
                    passData.taaParams = new Vector4(TaaAlphaStill, TaaAlphaMoving, TaaMotionPx, TaaSharpenAmount);
                    passData.taaCur = _taaCur;
                    passData.taaMetaR = _taaMetaA; passData.taaMetaW = _taaMetaB;
                    passData.taaColR = _taaColA; passData.taaColW = _taaColB;
                    (_taaMetaA, _taaMetaB) = (_taaMetaB, _taaMetaA);
                    (_taaColA, _taaColB) = (_taaColB, _taaColA);
                    _taaPrevVP = vp; _taaPrevCamVox = camVox; _taaPrevSun = sun; _taaPrevLightSig = lightSig;
                    _taaValid = true;

                    var up = TerrainClipmap.Active.UploadedChunks;
                    int nd = Mathf.Min(up.Count, _taaDirty.Length);
                    for (int i = 0; i < nd; i++) _taaDirty[i] = new Vector4(up[i].x, up[i].y, up[i].z, 0f);
                    up.Clear();
                    passData.taaDirty = _taaDirty;
                    passData.taaDirtyCount = nd;
                    passData.taaScratch = taaScratch;
                    passData.kTaaResolve = activeCompute.FindKernel("TaaResolve");
                    passData.kTaaSharpen = activeCompute.FindKernel("TaaSharpen");
                    if (upsIn) passData.ups = UpsPrepare(renderGraph, frameData, dispatchW, dispatchH, reset);
                }
                else
                {
                    _taaValid = false;
                    TerrainClipmap.Active.UploadedChunks.Clear();
                }
                passData.clipmapBuffer = clip.ClipmapBuffer;
                passData.brickDataBuffer = clip.BrickDataBuffer;
                passData.debugBuffer = DebugBuffer;
                passData.shadowStats = ShadowStatsBuffer;
                passData.collectShadowStats = CollectShadowStats ? 1 : 0;
                passData.debugPixel = DebugPixel;
                passData.debugMode = _debugMode;
                passData.traversalMode = TraversalMode;
                passData.useDevColors = UseDevColors ? 1 : 0;
                passData.shadeAO = ShadeAmbientOcclusion ? 1 : 0;
                passData.shadeHemi = ShadeHemisphere ? 1 : 0;
                passData.shadeFog = ShadeDistanceFog ? 1 : 0;
                passData.skyColor = SkyColor;
                passData.fogStart = FogStartMetres;
                passData.fogEnd = FogEndMetres;
                passData.maxOuterIterations = MaxOuterIterations;
                passData.usePackedMips = UsePackedMips ? 1 : 0;
                passData.contentCeilingVoxelY = ContentCeilingVoxelY;
                passData.contentFloorVoxelY = ContentFloorVoxelY;
                passData.uniformFaceHue = UniformFaceHue ? 1 : 0;

                // ---- Phase 7 lighting ----
                // LightSourceBuffer.Upload() is the CPU's job and happens in the
                // world's own update, not here: §3.9's contract is CPU -> GPU
                // with "none (dispatch after write)" staleness, and the render
                // graph's record phase is not where a CPU-side selection pass
                // belongs. This only READS what was already selected.
                var cfg = LightingConfig.Active;
                var lights = Lights;
                passData.lightBuffer = lights != null ? lights.Buffer : null;
                passData.activeLightCount = lights != null ? lights.ActiveCount : 0;

                Unity.Mathematics.float3 sd = SunCycle.Direction;
                Unity.Mathematics.float3 sc = SunCycle.Color;
                Unity.Mathematics.float3 amb = SunCycle.Ambient;
                passData.sunDir = new Vector4(sd.x, sd.y, sd.z, 0f);
                passData.sunColor = new Vector4(sc.x, sc.y, sc.z, 0f);
                passData.ambientColor = new Vector4(amb.x, amb.y, amb.z, 0f);

                passData.kSoft = cfg.kSoft;
                passData.shadowQualityLevel = ShadowQualityLevel;
                passData.maxShadowSteps = MaxShadowSteps;
                passData.pointLightNdotL = PointLightNdotL ? 1 : 0;
                passData.useSkyProbeAmbient = UseSkyProbeAmbient ? 1 : 0;
                passData.skyProbeTilt = SkyProbeTilt;
                passData.undergroundAmbientFloor = cfg.undergroundAmbientFloor;
                float shadowClampM = ShadowMaxDistanceOverrideM >= 0f
                    ? ShadowMaxDistanceOverrideM : cfg.shadowMaxDistanceM;
                passData.shadowMaxDistVoxels = shadowClampM * 10f;
                float rung1M = Rung1DistanceOverrideM >= 0f
                    ? Rung1DistanceOverrideM : cfg.rung1DistanceM;
                passData.rung1DistVoxels = rung1M * 10f;
                passData.shadowTravelLod = ShadowTravelLodOverride >= 0f
                    ? ShadowTravelLodOverride : cfg.shadowTravelLod;
                passData.materialVariation = MaterialVariationOverride >= 0
                    ? MaterialVariationOverride : cfg.materialVariation;
                passData.materialVariationAmount = cfg.materialVariationAmount;
                passData.tonemap = TonemapOverride >= 0 ? TonemapOverride : cfg.tonemap;
                passData.exposure = cfg.exposure;
                passData.tonemapWhite = cfg.tonemapWhite;

                // LightingConfig.look -- see the LOOK 1 block in Raymarch.compute.
                passData.look = LookOverride >= 0 ? LookOverride : cfg.look;
                float hrs = SunCycle.Hours;
                passData.lookSkyZenith = V4(SunCycle.LookSkyZenithAt(hrs));
                passData.lookSkyHorizon = V4(SunCycle.LookSkyHorizonAt(hrs));
                passData.lookSkyHalo = V4(SunCycle.LookSunHaloAt(hrs));
                { var rim = SunCycle.LookSkyRimAt(hrs, SkyRimScale); passData.lookSkyRim = new Vector4(rim.x, rim.y, rim.z, rim.w); }
                { var sh = SunCycle.LookSkyRimShapeAt(hrs, SkyRimScale); passData.lookSkyRimShape = new Vector4(sh.x, sh.y, sh.z, sh.w); }
                passData.lookSun = V4(SunCycle.LookSunAt(hrs));
                passData.lookAmbient = V4(SunCycle.LookAmbientAt(hrs));
                passData.lookMoon = V4(SunCycle.LookMoonAt(hrs));
                passData.lookShadowDarkness = cfg.lookShadowDarkness;
                passData.lookHazeDensity = cfg.hazeDensity;
                Unity.Mathematics.float4 g = SunCycle.LookGradeAt(hrs);
                passData.lookGrade = new Vector4(g.x, g.y, g.z, g.w);
                passData.emissiveBleed = EmissiveBleedOverride >= 0 ? EmissiveBleedOverride : cfg.emissiveBleed;
                passData.underwaterView = UnderwaterView ? 1 : 0;
                passData.seeThroughLiquids = SeeThroughLiquids ? 1 | ((DebugSeeSkip & 7) << 1) : 0;   // bits 1-3: task 75b diagnostic
                passData.riverLook = RiverLook ? 1 : 0;
                passData.farShadeParams = new Vector4(FarShadeSkyStrength, (SkyHaze ? SkyHazeRangeM : FarShadeHazeRangeM) * 10f,
                                                      FarShadeHazeScaleHeightM * 10f, FarShadeLightHaze);
                {
                    var farR = VoxelEngine.Rendering.FarField.Active;
                    bool hasFlow = farR != null && farR.RiverFlow != null;
                    passData.riverFlow = hasFlow ? farR.RiverFlow : _farDummy;
                    passData.riverMap = hasFlow ? farR.RiverMapParams : Vector4.zero;
                    passData.riverTime = Time.time;
                }
                passData.torchShadowRays = Mathf.Clamp(TorchShadowsOverride >= 0 ? TorchShadowsOverride : cfg.torchShadowRays,
                                                       0, LightingConfig.MaxTorchShadowRays);

                // Shadow roof skip. The buffer is bound on EVERY frame, on or
                // off: an HLSL-declared buffer left unbound is undefined on Metal.
                EnsureHeightMip(clip.WindowDimsBricks.x, clip.WindowDimsBricks.z);
                passData.roofSkip = ShadowRoofSkip ? 1 : DebugRoofBuildOnly ? 2 : 0;   // 2 = build only
                passData.shadowHalfRes = ShadowHalfRes ? LODConfig.PixelAngleRad(cameraData.camera.fieldOfView, dispatchH) : 0f;
                passData.heightMip = HeightMipBuffer;
                passData.heightOffsetsA = new[] { _heightOffsets[0], _heightOffsets[1], _heightOffsets[2], _heightOffsets[3] };
                passData.heightOffsetsB = new[] { _heightOffsets[4], _heightOffsets[5], _heightOffsets[6], 0 };
                if (!useStripped && !useMemoryProbe)
                {
                    passData.kHeightRaw = activeCompute.FindKernel("BuildHeightRaw");
                    passData.kHeightDilate = activeCompute.FindKernel("BuildHeightDilate");
                    passData.kHeightLevel = activeCompute.FindKernel("BuildHeightLevel");
                }

                // Far band. Buffers are bound EVERY frame, on or off (dummies
                // when off): a declared buffer left unbound is undefined on Metal.
                var far = VoxelEngine.Rendering.FarField.Active;
                bool farOn = far != null && far.Ready && VoxelEngine.Rendering.FarField.RenderEnabled && !useStripped && !useMemoryProbe;
                passData.farEnabled = farOn ? (far.FarOnly ? 2 : 1) : 0;
                // Task 24: speed-dependent voxel-detail radius (opt-in, -detailspeed 1).
                if (cameraData.camera.cameraType == CameraType.Game)
                    VoxelEngine.Streaming.DetailRadius.Tick(Time.frameCount, Time.realtimeSinceStartup, cameraData.camera.transform.position);
                passData.detailRadiusVoxels = farOn ? VoxelEngine.Streaming.DetailRadius.ShaderRadiusVoxels : 0f;
                // Task 85 (8.14 row 5, step 12): with shells drawn the hand-over is by
                // distance, so -detailspeed is not used on that path (shellsOn below).
                if (DrawShells && ShellStreamer.Active != null && !useStripped && !useMemoryProbe)
                    passData.detailRadiusVoxels = 0f;
                // Only the CSMainDetail kernel reads it (VE_DETAIL_SPEED); CSMain and
                // CSMainChecker are the pre-task code, so OFF and a still camera are
                // byte-identical. Not combined with the checkerboard (CSMainChecker wins).
                if (passData.detailRadiusVoxels > 0f && passData.kPrimary == 0)
                    passData.kPrimary = activeCompute.FindKernel("CSMainDetail");
                // Task 85b: far shading is its own kernel (VE_FAR_SHADE), so OFF runs the
                // pre-task CSMain byte for byte. The checkerboard and the -detailspeed
                // square keep their kernels (no far shading while they hold the frame).
                // Task 134: the lean play kernel, or its *Diag twin (ShellDiagKernels).
                bool shellDiag = ShellDiagKernels(_debugMode) && activeCompute.HasKernel("CSMainFarShadeDiag");
                string farShadeKernel = shellDiag ? "CSMainFarShadeDiag" : "CSMainFarShade";
                if (FarShade && passData.kPrimary == 0 && activeCompute.HasKernel(farShadeKernel))
                    passData.kPrimary = activeCompute.FindKernel(farShadeKernel);
                passData.farShadow = farOn && far.ShadowsOn ? 1 : 0;
                passData.farTexels = farOn ? far.Texels : _farDummy;
                passData.farMips = farOn ? far.Mips : _farDummy;
                passData.farStaging = farOn ? far.Staging : _farDummy;
                passData.farResident = farOn ? far.Resident : _farDummy;
                passData.farOrigins = new Vector4[VoxelEngine.Rendering.FarField.LEVELS];
                passData.farScatters = System.Array.Empty<VoxelEngine.Rendering.FarField.Scatter>();
                passData.farMipMask = 0;
                if (farOn)
                {
                    for (int L = 0; L < VoxelEngine.Rendering.FarField.LEVELS; L++)
                    {
                        var o = far.Origin(L);
                        passData.farOrigins[L] = new Vector4(o.x, o.y, 1f, 0f);
                    }
                    passData.farParams = new Vector4(far.MaxHeightVoxels, far.HazeRangeMetres * 10f, far.PrimaryStepCap, far.ShadowStepCap);
                    passData.farPixelAngle = VoxelEngine.Rendering.FarField.FootprintLevels
                        ? LODConfig.PixelAngleRad(cameraData.camera.fieldOfView, dispatchH) : 0f;
                    passData.farScatters = far.PendingScatters.ToArray();
                    far.PendingScatters.Clear();
                    passData.farMipMask = far.PendingMipMask;
                    far.PendingMipMask = 0;
                    passData.kFarScatter = activeCompute.FindKernel("FarScatter");
                    passData.kFarMip = activeCompute.FindKernel("FarMipBuild");
                }

                Unity.Mathematics.int3 dims = clip.WindowDimsBricks;
                passData.windowDimsBricks = new Vector3Int(dims.x, dims.y, dims.z);

                Unity.Mathematics.int3 dimsC = clip.WindowDimsChunks;
                passData.windowDimsChunks = new Vector3Int(dimsC.x, dimsC.y, dimsC.z);

                // The window's minimum corner, in bricks. Every frame: this is
                // what turns the Phase 3 bounds guard from "assumes origin
                // (0,0,0)" into a correct test under a moving window.
                Unity.Mathematics.int3 originB = clip.WindowOriginBricks;
                passData.windowOriginBricks = new Vector3Int(originB.x, originB.y, originB.z);

                int levelCount = AirMipEnabled ? clip.AirMipLevelCount : 0;
                passData.airMipLevelCount = levelCount;

                GraphicsBuffer b1 = clip.AirMipBuffer(1);
                int available = clip.AirMipLevelCount;
                passData.airMip1 = b1;
                passData.airMip2 = available >= 2 ? clip.AirMipBuffer(2) : b1;
                passData.airMip3 = available >= 3 ? clip.AirMipBuffer(3) : b1;
                passData.airMip4 = available >= 4 ? clip.AirMipBuffer(4) : b1;

                Unity.Mathematics.int3 g0 = clip.Mips.DimsOfLevel(1);
                Unity.Mathematics.int3 g1 = available >= 2 ? clip.Mips.DimsOfLevel(2) : g0;
                Unity.Mathematics.int3 g2 = available >= 3 ? clip.Mips.DimsOfLevel(3) : g0;
                Unity.Mathematics.int3 g3 = available >= 4 ? clip.Mips.DimsOfLevel(4) : g0;
                passData.airMipDims0 = new Vector3Int(g0.x, g0.y, g0.z);
                passData.airMipDims1 = new Vector3Int(g1.x, g1.y, g1.z);
                passData.airMipDims2 = new Vector3Int(g2.x, g2.y, g2.z);
                passData.airMipDims3 = new Vector3Int(g3.x, g3.y, g3.z);

                passData.airMipPacked = clip.AirMipPackedBuffer;
                var packed = clip.Packed;
                var info = new Vector4[4];
                for (int k = 0; k < 4; k++)
                {
                    if (packed != null && k < packed.NumLevels)
                    {
                        Unity.Mathematics.int3 d = packed.LevelDims[k];
                        info[k] = new Vector4(d.x, d.y, d.z, packed.WordOffsets[k]);
                    }
                    else info[k] = new Vector4(1, 1, 1, 0);
                }
                passData.mipInfo = info;

                var shells = ShellStreamer.Active;
                bool shellsOn = DrawShells && shells != null && !useStripped && !useMemoryProbe;
                ShellsDrawnLastFrame = shellsOn;
                passData.shellCount = shellsOn ? shells.Layout.ShellCount : 0;
                passData.shellIterCap = Mathf.Max(16, ShellIterCap);
                passData.shellTint = shellsOn && ShellTint ? 1 : 0;
                passData.shellPixelAngle = LODConfig.PixelAngleRad(cameraData.camera.fieldOfView, dispatchH);
                passData.shellRing = new Vector4[ShellLayout.MAX_SHELLS]; passData.shellLo = new Vector4[ShellLayout.MAX_SHELLS];
                passData.shellHi = new Vector4[ShellLayout.MAX_SHELLS]; passData.shellCam = new Vector4[ShellLayout.MAX_SHELLS];
                passData.shellBounds = new Vector4[ShellLayout.MAX_SHELLS];
                if (shellsOn)
                {
                    shells.ShaderParams(passData.shellRing, passData.shellLo, passData.shellHi, passData.shellCam, passData.shellBounds);
                    passData.shellDir = shells.Gpu.Dir; passData.shellPages = shells.Gpu.Pages; passData.shellBricks = shells.Gpu.Bricks;
                    passData.shellMaskBase = shells.Gpu.MaskBase;   // task 93b5
                    passData.shellTopY = shells.SolidTopY;          // task 99
                    passData.shellColTopBase = shells.Gpu.ColTopBase; // task 116
                }
                else { passData.shellDir = _farDummy; passData.shellPages = _farDummy; passData.shellBricks = _farDummy; passData.shellMaskBase = 0; passData.shellTopY = int.MinValue; passData.shellColTopBase = 0; }

                // Task 130 (DIAGNOSTIC): the shellsplit arms' probe (see DebugShellSkip).
                passData.primaryFarShade = activeCompute.HasKernel(farShadeKernel) && passData.kPrimary == activeCompute.FindKernel(farShadeKernel);
                ShellDiagDrawnLastFrame = passData.primaryFarShade && shellDiag;
                passData.kShellProbe = -1;
                passData.shellProbeIO = _shellProbeDummy;
                if (passData.primaryFarShade && (DebugShellSkip & 32) != 0 && passData.shellCount > 1 && activeCompute.HasKernel("CSShellProbe"))
                {
                    int n = passData.dispatchWidth * passData.dispatchHeight;
                    if (_shellProbeIO == null || _shellProbeIO.count < n)
                    {
                        _shellProbeIO?.Release();
                        _shellProbeIO = new GraphicsBuffer(GraphicsBuffer.Target.Structured, n, sizeof(float));
                    }
                    passData.shellProbeIO = _shellProbeIO;
                    passData.kShellProbe = activeCompute.FindKernel("CSShellProbe");
                }
                // Task 132 (R1): the shell pre-pass (see ShellPrepass). Not with a debug view, task 130's split arms
                // or 93b4's shellmarch_off arm (DebugShellSkip bit 1: no primary shell march to move).
                passData.kShellPrepass = -1;
                passData.shellPre = _shellPreDummy;
                if (ShellPrepass && passData.primaryFarShade && passData.kShellProbe < 0 && (DebugShellSkip & 2) == 0 && passData.shellCount > 1
                    && _debugMode == 0 && activeCompute.HasKernel(shellDiag ? "CSShellPrepassDiag" : "CSShellPrepass"))
                {
                    int n = passData.dispatchWidth * passData.dispatchHeight;
                    if (_shellPre == null || _shellPre.count < n)
                    {
                        _shellPre?.Release();
                        _shellPre = new GraphicsBuffer(GraphicsBuffer.Target.Structured, n, SHELL_PRE_STRIDE);
                        Debug.Log($"[shellprepass] scratch {passData.dispatchWidth}x{passData.dispatchHeight} = {n} px x {SHELL_PRE_STRIDE} B = {n * (long)SHELL_PRE_STRIDE / (1024.0 * 1024.0):F1} MB");
                    }
                    passData.shellPre = _shellPre;
                    passData.kShellPrepass = activeCompute.FindKernel(shellDiag ? "CSShellPrepassDiag" : "CSShellPrepass");
                }
                ShellPrepassDrawnLastFrame = passData.kShellPrepass >= 0;
                var cascadeManager = LODCascadeManager.Active;
                bool cascadeAvailable = UseLODCascade && cascadeManager != null && !shellsOn;
                passData.useLODCascade = cascadeAvailable ? 1 : 0;

                if (cascadeAvailable)
                {
                    var tier1 = cascadeManager.TierPool(1);
                    var tier2 = cascadeManager.TierPool(2);

                    passData.clipmapTier1 = tier1.ClipmapBuffer;
                    passData.brickDataTier1 = tier1.BrickDataBuffer;
                    Unity.Mathematics.int3 d1 = tier1.WindowDimsCoarseBricks;
                    passData.windowDimsCoarseTier1 = new Vector3Int(d1.x, d1.y, d1.z);
                    passData.cascadeTierInfo1 = new Vector4(
                        tier1.CoarseBricksPerChunkEdge,
                        Log2Int(tier1.CoarseBricksPerChunkEdge),
                        tier1.EntriesPerChunk, 0f);

                    passData.clipmapTier2 = tier2.ClipmapBuffer;
                    passData.brickDataTier2 = tier2.BrickDataBuffer;
                    Unity.Mathematics.int3 d2 = tier2.WindowDimsCoarseBricks;
                    passData.windowDimsCoarseTier2 = new Vector3Int(d2.x, d2.y, d2.z);
                    passData.cascadeTierInfo2 = new Vector4(
                        tier2.CoarseBricksPerChunkEdge,
                        Log2Int(tier2.CoarseBricksPerChunkEdge),
                        tier2.EntriesPerChunk, 0f);

                    if (LegacyTierBounds)
                        System.Array.Copy(LODConfig.TIER_OUTER_RANGE_M, LastTierBoundsM, LODConfig.TIER_COUNT);
                    else
                        LODConfig.FrameBoundariesM(cameraData.camera.fieldOfView, dispatchH, LastTierBoundsM);
                    float tier0Outer = LastTierBoundsM[0] * 10f;
                    float tier1Outer = LastTierBoundsM[1] * 10f;
                    float tier2Outer = LastTierBoundsM[2] * 10f;
                    passData.tierOuterRangeVoxels = new Vector4(tier0Outer, tier1Outer, tier2Outer, 0f);
                    passData.maxRayDistance = tier2Outer;
                }
                else
                {
                    passData.clipmapTier1 = clip.ClipmapBuffer;
                    passData.brickDataTier1 = clip.BrickDataBuffer;
                    passData.windowDimsCoarseTier1 = passData.windowDimsBricks;
                    passData.cascadeTierInfo1 = new Vector4(8, 3, 512, 0);
                    passData.clipmapTier2 = clip.ClipmapBuffer;
                    passData.brickDataTier2 = clip.BrickDataBuffer;
                    passData.windowDimsCoarseTier2 = passData.windowDimsBricks;
                    passData.cascadeTierInfo2 = new Vector4(4, 2, 64, 0);
                    passData.tierOuterRangeVoxels = Vector4.zero;
                    passData.maxRayDistance = MaxRayDistance;
                }

                builder.UseTexture(target, AccessFlags.Write);
                if (passData.taaResolve != 0) builder.UseTexture(passData.taaScratch, AccessFlags.ReadWrite);
                if (passData.ups != null) { builder.UseTexture(passData.ups.motion, AccessFlags.Write); builder.UseTexture(passData.ups.depth, AccessFlags.Write); }
                builder.AllowPassCulling(false);

                builder.SetRenderFunc((ComputePassData data, ComputeGraphContext context) =>
                {
                    var cmd = context.cmd;

                    cmd.SetComputeMatrixParam(data.compute, "_CameraInverseProjection", data.invProjection);
                    cmd.SetComputeMatrixParam(data.compute, "_CameraInverseView", data.cam.cameraToWorldMatrix);
                    cmd.SetComputeVectorParam(data.compute, "_CameraPos", data.cam.transform.position);

                    cmd.SetComputeBufferParam(data.compute, data.kPrimary, "ClipmapBuffer", data.clipmapBuffer);
                    cmd.SetComputeBufferParam(data.compute, data.kPrimary, "BrickDataBuffer", data.brickDataBuffer);

                    cmd.SetComputeBufferParam(data.compute, data.kPrimary, "AirMip1", data.airMip1);
                    cmd.SetComputeBufferParam(data.compute, data.kPrimary, "AirMip2", data.airMip2);
                    cmd.SetComputeBufferParam(data.compute, data.kPrimary, "AirMip3", data.airMip3);
                    cmd.SetComputeBufferParam(data.compute, data.kPrimary, "AirMip4", data.airMip4);
                    cmd.SetComputeIntParams(data.compute, "_AirMipDims0",
                        new int[] { data.airMipDims0.x, data.airMipDims0.y, data.airMipDims0.z, 0 });
                    cmd.SetComputeIntParams(data.compute, "_AirMipDims1",
                        new int[] { data.airMipDims1.x, data.airMipDims1.y, data.airMipDims1.z, 0 });
                    cmd.SetComputeIntParams(data.compute, "_AirMipDims2",
                        new int[] { data.airMipDims2.x, data.airMipDims2.y, data.airMipDims2.z, 0 });
                    cmd.SetComputeIntParams(data.compute, "_AirMipDims3",
                        new int[] { data.airMipDims3.x, data.airMipDims3.y, data.airMipDims3.z, 0 });
                    cmd.SetComputeIntParam(data.compute, "_AirMipLevelCount", data.airMipLevelCount);

                    if (data.airMipPacked != null)
                        cmd.SetComputeBufferParam(data.compute, data.kPrimary, "AirMipPacked", data.airMipPacked);
                    cmd.SetComputeVectorArrayParam(data.compute, "_MipInfo", data.mipInfo);
                    cmd.SetComputeIntParam(data.compute, "_UsePackedMips", data.usePackedMips);
                    cmd.SetComputeFloatParam(data.compute, "_MaxRayDistance", data.maxRayDistance);

                    cmd.SetComputeIntParams(data.compute, "_WindowDimsBricksPacked",
                        new int[] { data.windowDimsBricks.x, data.windowDimsBricks.y, data.windowDimsBricks.z, 0 });

                    // --- PHASE 4 window bindings. Set for EVERY kernel,
                    // including stripped/probe, because all three now index the
                    // clipmap chunk-major and would read garbage without them.
                    cmd.SetComputeIntParams(data.compute, "_WindowDimsChunksPacked",
                        new int[] { data.windowDimsChunks.x, data.windowDimsChunks.y, data.windowDimsChunks.z, 0 });
                    cmd.SetComputeIntParams(data.compute, "_WindowOriginBricksPacked",
                        new int[] { data.windowOriginBricks.x, data.windowOriginBricks.y, data.windowOriginBricks.z, 0 });
                    cmd.SetComputeIntParam(data.compute, "_ContentCeilingVoxelY", data.contentCeilingVoxelY);
                    cmd.SetComputeIntParam(data.compute, "_ContentFloorVoxelY", data.contentFloorVoxelY);
                    cmd.SetComputeIntParam(data.compute, "_UniformFaceHue", data.uniformFaceHue);

                    cmd.SetComputeBufferParam(data.compute, data.kPrimary, "ClipmapBufferTier1", data.clipmapTier1);
                    cmd.SetComputeBufferParam(data.compute, data.kPrimary, "BrickDataBufferTier1", data.brickDataTier1);
                    cmd.SetComputeBufferParam(data.compute, data.kPrimary, "ClipmapBufferTier2", data.clipmapTier2);
                    cmd.SetComputeBufferParam(data.compute, data.kPrimary, "BrickDataBufferTier2", data.brickDataTier2);
                    cmd.SetComputeIntParams(data.compute, "_WindowDimsCoarseBricksTier1",
                        new int[] { data.windowDimsCoarseTier1.x, data.windowDimsCoarseTier1.y, data.windowDimsCoarseTier1.z, 0 });
                    cmd.SetComputeIntParams(data.compute, "_WindowDimsCoarseBricksTier2",
                        new int[] { data.windowDimsCoarseTier2.x, data.windowDimsCoarseTier2.y, data.windowDimsCoarseTier2.z, 0 });
                    cmd.SetComputeIntParams(data.compute, "_CascadeTierInfo1",
                        new int[] { (int)data.cascadeTierInfo1.x, (int)data.cascadeTierInfo1.y, (int)data.cascadeTierInfo1.z, 0 });
                    cmd.SetComputeIntParams(data.compute, "_CascadeTierInfo2",
                        new int[] { (int)data.cascadeTierInfo2.x, (int)data.cascadeTierInfo2.y, (int)data.cascadeTierInfo2.z, 0 });
                    cmd.SetComputeVectorParam(data.compute, "_TierOuterRangeVoxels", data.tierOuterRangeVoxels);
                    cmd.SetComputeIntParam(data.compute, "_UseLODCascade", data.useLODCascade);

                    if (!data.useStripped && !data.useMemoryProbe)
                    {
                        cmd.SetComputeBufferParam(data.compute, data.kPrimary, "DebugOut", data.debugBuffer);
                        cmd.SetComputeIntParams(data.compute, "_DebugPixel",
                            new int[] { data.debugPixel.x, data.debugPixel.y });
                        cmd.SetComputeIntParams(data.compute, "_DebugModePacked",
                            new int[] { data.debugMode, 0, 0, 0 });
                        cmd.SetComputeIntParam(data.compute, "_TraversalMode", data.traversalMode);
                        cmd.SetComputeIntParam(data.compute, "_MaxOuterIterations", data.maxOuterIterations);
                        cmd.SetComputeIntParam(data.compute, "_UseDevColors", data.useDevColors);
                        cmd.SetComputeIntParam(data.compute, "_ShadeAO", data.shadeAO);
                        cmd.SetComputeIntParam(data.compute, "_ShadeHemi", data.shadeHemi);
                        cmd.SetComputeIntParam(data.compute, "_ShadeFog", data.shadeFog);
                        cmd.SetComputeVectorParam(data.compute, "_SkyColor",
                            new Vector4(data.skyColor.r, data.skyColor.g, data.skyColor.b, 1f));
                        cmd.SetComputeFloatParam(data.compute, "_FogStartM", data.fogStart);
                        cmd.SetComputeFloatParam(data.compute, "_FogEndM", data.fogEnd);

                        // ---- Phase 7 lighting. FULL KERNEL ONLY ----
                        // The stripped and memory-probe kernels do not declare
                        // any of this and never shade; binding to them would be
                        // a silent no-op at best. Keeping them out of this block
                        // is also what preserves them as clean isolation arms --
                        // Amendment 8.10 §5.3 measured the diagnostic kernels
                        // earning their keep and this must not change what they
                        // measure.
                        // ALWAYS bound: a dummy (with a light count of 0) when no
                        // driver provides one -- see _lightDummy.
                        cmd.SetComputeBufferParam(data.compute, data.kPrimary, "LightBuffer", data.lightBuffer ?? _lightDummy);
                        cmd.SetComputeIntParam(data.compute, "_ActiveLightCount",
                            data.lightBuffer != null ? data.activeLightCount : 0);
                        cmd.SetComputeVectorParam(data.compute, "_SunDir", data.sunDir);
                        cmd.SetComputeVectorParam(data.compute, "_SunColor", data.sunColor);
                        cmd.SetComputeVectorParam(data.compute, "_AmbientColor", data.ambientColor);
                        cmd.SetComputeFloatParam(data.compute, "_KSoft", data.kSoft);
                        cmd.SetComputeIntParam(data.compute, "_ShadowQualityLevel", data.shadowQualityLevel);
                        cmd.SetComputeIntParam(data.compute, "_TemporalShadows", data.temporalShadows);
                        cmd.SetComputeIntParam(data.compute, "_ShadowHistValid", data.tsValid);
                        cmd.SetComputeIntParam(data.compute, "_ShadowFrame", (int)data.tsFrame);
                        cmd.SetComputeIntParam(data.compute, "_ShadowReuseMask", data.tsReuseMask);
                        cmd.SetComputeMatrixParam(data.compute, "_PrevViewProj", data.tsPrevVP);
                        cmd.SetComputeBufferParam(data.compute, data.kPrimary, "ShadowHistR", data.tsRead);
                        cmd.SetComputeBufferParam(data.compute, data.kPrimary, "ShadowHistW", data.tsWrite);
                        cmd.SetComputeIntParam(data.compute, "_MaxShadowSteps", data.maxShadowSteps);
                        cmd.SetComputeIntParam(data.compute, "_PointLightNdotL", data.pointLightNdotL);
                        cmd.SetComputeIntParam(data.compute, "_UseSkyProbeAmbient", data.useSkyProbeAmbient);
                        cmd.SetComputeIntParam(data.compute, "_SkyProbeTilt", data.skyProbeTilt);
                        cmd.SetComputeFloatParam(data.compute, "_UndergroundAmbientFloor",
                            data.undergroundAmbientFloor);

                        // Stage F's counters. Bound unconditionally (an unbound
                        // RWStructuredBuffer is undefined even when unread) but
                        // only WRITTEN when the flag is set.
                        if (data.shadowStats != null)
                            cmd.SetComputeBufferParam(data.compute, data.kPrimary, "ShadowStats", data.shadowStats);
                        cmd.SetComputeIntParam(data.compute, "_CollectShadowStats", data.collectShadowStats);
                        cmd.SetComputeFloatParam(data.compute, "_ShadowMaxDistVoxels", data.shadowMaxDistVoxels);
                        cmd.SetComputeFloatParam(data.compute, "_Rung1DistVoxels", data.rung1DistVoxels);
                        cmd.SetComputeFloatParam(data.compute, "_ShadowTravelLOD", data.shadowTravelLod);
                        cmd.SetComputeIntParam(data.compute, "_MaterialVariation", data.materialVariation);
                        cmd.SetComputeFloatParam(data.compute, "_MaterialVariationAmount", data.materialVariationAmount);
                        cmd.SetComputeIntParam(data.compute, "_Tonemap", data.tonemap);
                        cmd.SetComputeFloatParam(data.compute, "_Exposure", data.exposure);
                        cmd.SetComputeFloatParam(data.compute, "_TonemapWhite", data.tonemapWhite);
                        cmd.SetComputeIntParam(data.compute, "_Look", data.look);
                        cmd.SetComputeVectorParam(data.compute, "_LookSkyZenith", data.lookSkyZenith);
                        cmd.SetComputeVectorParam(data.compute, "_LookSkyHorizon", data.lookSkyHorizon);
                        cmd.SetComputeVectorParam(data.compute, "_LookSkyHalo", data.lookSkyHalo);
                        cmd.SetComputeVectorParam(data.compute, "_LookSkyRim", data.lookSkyRim);   // task 121
                        cmd.SetComputeVectorParam(data.compute, "_LookSkyRimShape", data.lookSkyRimShape);   // task 126
                        cmd.SetComputeIntParam(data.compute, "_SkyLook", (SkyLook ? 1 : 0) | (SkyHaze ? 2 : 0) | (EdgeFade ? 4 : 0));
                        cmd.SetComputeIntParam(data.compute, "_BiomeTint", BiomeTint ? 1 : 0);   // task 122
                        cmd.SetComputeVectorParam(data.compute, "_LookSun", data.lookSun);
                        cmd.SetComputeVectorParam(data.compute, "_LookAmbient", data.lookAmbient);
                        cmd.SetComputeVectorParam(data.compute, "_LookMoon", data.lookMoon);
                        cmd.SetComputeFloatParam(data.compute, "_LookShadowDarkness", data.lookShadowDarkness);
                        cmd.SetComputeFloatParam(data.compute, "_LookHazeDensity", data.lookHazeDensity);
                        cmd.SetComputeVectorParam(data.compute, "_LookGrade", data.lookGrade);
                        cmd.SetComputeIntParam(data.compute, "_EmissiveBleed", data.emissiveBleed);
                        cmd.SetComputeIntParam(data.compute, "_UnderwaterView", data.underwaterView);
                        cmd.SetComputeIntParam(data.compute, "_SeeThroughLiquids", data.seeThroughLiquids);
                        cmd.SetComputeIntParam(data.compute, "_RiverLook", data.riverLook);
                        cmd.SetComputeVectorParam(data.compute, "_FarShadeParams", data.farShadeParams);
                        cmd.SetComputeVectorParam(data.compute, "_RiverMap", data.riverMap);
                        cmd.SetComputeFloatParam(data.compute, "_RiverTime", data.riverTime);
                        cmd.SetComputeBufferParam(data.compute, data.kPrimary, "RiverFlow", data.riverFlow);
                        cmd.SetComputeIntParam(data.compute, "_TorchShadowRays", data.torchShadowRays);

                        cmd.SetComputeIntParam(data.compute, "_ShadowRoofSkip", data.roofSkip == 1 ? 1 : 0);
                        cmd.SetComputeFloatParam(data.compute, "_ShadowHalfRes", data.shadowHalfRes);
                        cmd.SetComputeIntParams(data.compute, "_HeightOffsetsA", data.heightOffsetsA);
                        cmd.SetComputeIntParams(data.compute, "_HeightOffsetsB", data.heightOffsetsB);
                        cmd.SetComputeBufferParam(data.compute, data.kPrimary, "HeightMip", data.heightMip);
                        if (data.roofSkip != 0)
                        {
                            int gx = (data.windowDimsBricks.x + 7) / 8, gz = (data.windowDimsBricks.z + 7) / 8;
                            cmd.BeginSample("VE.Raymarch.HeightMip");
                            cmd.SetComputeBufferParam(data.compute, data.kHeightRaw, "ClipmapBuffer", data.clipmapBuffer);
                            cmd.SetComputeBufferParam(data.compute, data.kHeightRaw, "HeightMip", data.heightMip);
                            cmd.DispatchCompute(data.compute, data.kHeightRaw, gx, gz, 1);
                            cmd.SetComputeBufferParam(data.compute, data.kHeightDilate, "HeightMip", data.heightMip);
                            cmd.DispatchCompute(data.compute, data.kHeightDilate, gx, gz, 1);
                            cmd.SetComputeBufferParam(data.compute, data.kHeightLevel, "HeightMip", data.heightMip);
                            for (int L = 1; L < 6; L++)
                            {
                                cmd.SetComputeIntParam(data.compute, "_HeightLevel", L);
                                int lx = Mathf.Max(1, ((data.windowDimsBricks.x >> L) + 7) / 8);
                                int lz = Mathf.Max(1, ((data.windowDimsBricks.z >> L) + 7) / 8);
                                cmd.DispatchCompute(data.compute, data.kHeightLevel, lx, lz, 1);
                            }
                            cmd.EndSample("VE.Raymarch.HeightMip");
                        }

                        // ---- far band: bindings, then upkeep BEFORE the primary ray ----
                        cmd.SetComputeIntParam(data.compute, "_FarEnabled", data.farEnabled);
                        cmd.SetComputeIntParam(data.compute, "_FarShadow", data.farShadow);
                        cmd.SetComputeIntParam(data.compute, "_FarRockSlope", FarRockSlope ? 1 : 0);   // task 119
                        cmd.SetComputeIntParam(data.compute, "_FarShadowLift", FarShadowLift ? 1 : 0); // task 119
                        cmd.SetComputeFloatParam(data.compute, "_CubeBounce", CubeBounce ? CubeBounceStrength : 0f);   // task 120
                        cmd.SetComputeIntParam(data.compute, "_CubeFilter", CubeFilter ? 1 : 0);
                        cmd.SetComputeIntParam(data.compute, "_CellHash", CellHash ? 1 : 0);
                        cmd.SetComputeVectorParam(data.compute, "_CubeFilterRange", new Vector4(CubeFilterStartM * 10f, CubeFilterFullM * 10f, 0f, 0f));
                        cmd.SetComputeVectorArrayParam(data.compute, "_FarOrigin", data.farOrigins);
                        cmd.SetComputeVectorParam(data.compute, "_FarParams", data.farParams);
                        cmd.SetComputeFloatParam(data.compute, "_FarPixelAngle", data.farPixelAngle);
                        cmd.SetComputeFloatParam(data.compute, "_DetailRadiusVoxels", data.detailRadiusVoxels);
                        cmd.SetComputeBufferParam(data.compute, data.kPrimary, "FarTexels", data.farTexels);
                        cmd.SetComputeBufferParam(data.compute, data.kPrimary, "FarMips", data.farMips);
                        cmd.SetComputeBufferParam(data.compute, data.kPrimary, "FarResident", data.farResident);
                        // 8.14 shells (task 80). Always bound (dummies when off).
                        cmd.SetComputeIntParam(data.compute, "_ShellCount", data.shellCount);
                        cmd.SetComputeIntParam(data.compute, "_ShellIterCap", data.shellIterCap);
                        cmd.SetComputeIntParam(data.compute, "_NearShadowShells", NearShadowShells && data.shellCount > 1 && data.primaryFarShade ? 1 : 0);   // task 133
                        cmd.SetComputeIntParam(data.compute, "_ShellMaskBase", data.shellMaskBase);   // task 93b5
                        cmd.SetComputeIntParam(data.compute, "_ShellAirMask", ShellAirMask ? 1 : 0);
                        cmd.SetComputeIntParam(data.compute, "_ShellTopY", data.shellTopY);             // task 99
                        cmd.SetComputeIntParam(data.compute, "_ShellSkyExit", ShellSkyExit ? 1 : 0);
                        cmd.SetComputeIntParam(data.compute, "_ShellColTopBase", data.shellColTopBase); // task 116
                        cmd.SetComputeIntParam(data.compute, "_ShellColLeap", ShellColumnLeap ? 1 : 0);
                        cmd.SetComputeIntParam(data.compute, "_ShellHoleLeap", ShellHoleLeap ? 1 : 0);   // task 137
                        cmd.SetComputeIntParam(data.compute, "_ShellTint", data.shellTint);
                        cmd.SetComputeIntParam(data.compute, "_DebugShellSkip", DebugShellSkip);   // task 93b4 (diagnostic)
                        cmd.SetComputeFloatParam(data.compute, "_ShellPixelAngle", data.shellPixelAngle);
                        cmd.SetComputeVectorArrayParam(data.compute, "_ShellRing", data.shellRing);
                        cmd.SetComputeVectorArrayParam(data.compute, "_ShellLo", data.shellLo);
                        cmd.SetComputeVectorArrayParam(data.compute, "_ShellHi", data.shellHi);
                        cmd.SetComputeVectorArrayParam(data.compute, "_ShellCam", data.shellCam);
                        cmd.SetComputeVectorArrayParam(data.compute, "_ShellBounds", data.shellBounds);
                        cmd.SetComputeBufferParam(data.compute, data.kPrimary, "ShellDir", data.shellDir);
                        cmd.SetComputeBufferParam(data.compute, data.kPrimary, "ShellPages", data.shellPages);
                        cmd.SetComputeBufferParam(data.compute, data.kPrimary, "ShellBricks", data.shellBricks);
                        if (data.primaryFarShade)   // task 130 (diagnostic): always bound to the far-shading kernel
                        {
                            cmd.SetComputeBufferParam(data.compute, data.kPrimary, "ShellProbeIO", data.shellProbeIO);
                            cmd.SetComputeVectorParam(data.compute, "_ShellProbeParams",
                                data.kShellProbe >= 0 || data.kShellPrepass >= 0 ? new Vector4(data.dispatchWidth, data.dispatchHeight, 0f, 0f) : Vector4.zero);
                            cmd.SetComputeBufferParam(data.compute, data.kPrimary, "ShellPre", data.shellPre);   // task 132
                            cmd.SetComputeIntParam(data.compute, "_ShellPrepass", data.kShellPrepass >= 0 ? 1 : 0);
                        }
                        if (data.farEnabled != 0 && (data.farScatters.Length > 0 || data.farMipMask != 0))
                        {
                            cmd.BeginSample("VE.Raymarch.FarUpkeep");
                            foreach (var sc in data.farScatters)
                            {
                                cmd.SetComputeIntParams(data.compute, "_FarScatter", new int[] { sc.x0, sc.z0, sc.w, sc.h });
                                cmd.SetComputeIntParam(data.compute, "_FarScatterLevel", sc.level);
                                cmd.SetComputeBufferParam(data.compute, data.kFarScatter, "FarStaging", data.farStaging);
                                cmd.SetComputeBufferParam(data.compute, data.kFarScatter, "FarTexelsRW", data.farTexels);
                                cmd.DispatchCompute(data.compute, data.kFarScatter, (sc.w * sc.h + 63) / 64, 1, 1);
                            }
                            for (int L = 0; L < VoxelEngine.Rendering.FarField.LEVELS; L++)
                            {
                                if ((data.farMipMask & (1 << L)) == 0) continue;
                                cmd.SetComputeIntParam(data.compute, "_FarMipLevel", L);
                                cmd.SetComputeBufferParam(data.compute, data.kFarMip, "FarTexelsRW", data.farTexels);
                                cmd.SetComputeBufferParam(data.compute, data.kFarMip, "FarMipsRW", data.farMips);
                                for (int k = 1; k <= VoxelEngine.Rendering.FarField.MIP_COUNT; k++)
                                {
                                    cmd.SetComputeIntParam(data.compute, "_FarMipK", k);
                                    int n = VoxelEngine.Rendering.FarField.EDGE >> k;
                                    cmd.DispatchCompute(data.compute, data.kFarMip, (n + 7) / 8, (n + 7) / 8, 1);
                                }
                            }
                            cmd.EndSample("VE.Raymarch.FarUpkeep");
                        }
                    }

                    if (!data.useMemoryProbe)
                        cmd.SetComputeBufferParam(data.compute, data.kPrimary, "BrickDataBuffer", data.brickDataBuffer);

                    if (data.useMemoryProbe)
                        cmd.SetComputeIntParam(data.compute, "_ProbeIterations", data.probeIterations);

                    if (!data.useStripped && !data.useMemoryProbe)
                    {
                        cmd.SetComputeIntParam(data.compute, "_TaaEnabled", data.taa);
                        cmd.SetComputeIntParam(data.compute, "_TaaChecker", data.taa != 0 ? data.taaChecker : 0);
                        cmd.SetComputeBufferParam(data.compute, data.kPrimary, "TaaCur", data.taaCur);
                        cmd.SetComputeBufferParam(data.compute, data.kPrimary, "TaaMetaW", data.taaMetaW);
                    }
                    if (data.taa != 0)
                    {
                        cmd.SetComputeVectorParam(data.compute, "_TaaJitter", data.taaJitter);
                        cmd.SetComputeIntParam(data.compute, "_TaaHistValid", data.taaHistValid);
                        cmd.SetComputeMatrixParam(data.compute, "_TaaPrevViewProj", data.taaPrevVP);
                        cmd.SetComputeVectorParam(data.compute, "_TaaPrevCamVox", data.taaPrevCamVox);
                        cmd.SetComputeVectorParam(data.compute, "_TaaParams", data.taaParams);
                        cmd.SetComputeVectorArrayParam(data.compute, "_TaaDirtyChunks", data.taaDirty);
                        cmd.SetComputeIntParam(data.compute, "_TaaDirtyCount", data.taaDirtyCount);
                        cmd.SetComputeIntParam(data.compute, "_TaaCheckerParity", data.taaCheckerParity);
                    }
                    // With TAA on, the primary ray writes the scratch target and
                    // TaaSharpen writes the real one.
                    cmd.SetComputeTextureParam(data.compute, data.kPrimary, "ResultTexture", data.taaResolve != 0 ? data.taaScratch : data.target);

                    int threadGroupsX = Mathf.CeilToInt(data.dispatchWidth / 8f);
                    int threadGroupsY = Mathf.CeilToInt(data.dispatchHeight / 8f);
                    // Checkerboard: the primary pass covers half the width.
                    int primaryGroupsX = data.primaryChecker
                        ? ((data.dispatchWidth + 1) / 2 + 7) / 8 : threadGroupsX;
                    // NAMED for GPU capture. CommandBuffer.BeginSample emits a
                    // Metal debug group, so this shows up in an Instruments
                    // "Metal System Trace" as a named encoder rather than an
                    // anonymous dispatch. See tools/capture-gpu-trace.sh.
                    // The name must match the one parsed by
                    // tools/parse-gpu-trace.py.
                    cmd.BeginSample("VE.Raymarch.Primary");
                    cmd.DispatchCompute(data.compute, data.kPrimary, primaryGroupsX, threadGroupsY, 1);
                    cmd.EndSample("VE.Raymarch.Primary");
                    PrimaryDispatchCount++;
                    if (data.kShellPrepass >= 0)
                    {
                        // Task 132 (R1): the primary shell march alone, for the rays pass 1 handed over, then the
                        // far-shading kernel's pass 2 draws the groups pass 1 left (Raymarch.compute ShellPre).
                        cmd.BeginSample("VE.Raymarch.ShellPrepass");
                        cmd.SetComputeBufferParam(data.compute, data.kShellPrepass, "ShellDir", data.shellDir);
                        cmd.SetComputeBufferParam(data.compute, data.kShellPrepass, "ShellPages", data.shellPages);
                        cmd.SetComputeBufferParam(data.compute, data.kShellPrepass, "ShellBricks", data.shellBricks);
                        cmd.SetComputeBufferParam(data.compute, data.kShellPrepass, "FarResident", data.farResident);
                        cmd.SetComputeBufferParam(data.compute, data.kShellPrepass, "ShellPre", data.shellPre);
                        cmd.SetComputeBufferParam(data.compute, data.kShellPrepass, "ShadowStats", data.shadowStats ?? _farDummy);
                        cmd.DispatchCompute(data.compute, data.kShellPrepass, (data.dispatchWidth + 7) / 8, (data.dispatchHeight + 7) / 8, 1);
                        cmd.EndSample("VE.Raymarch.ShellPrepass");
                        cmd.BeginSample("VE.Raymarch.PrimaryPass2");
                        cmd.SetComputeIntParam(data.compute, "_ShellPrepass", 2);
                        cmd.DispatchCompute(data.compute, data.kPrimary, primaryGroupsX, threadGroupsY, 1);
                        cmd.SetComputeIntParam(data.compute, "_ShellPrepass", 0);
                        cmd.EndSample("VE.Raymarch.PrimaryPass2");
                    }
                    if (data.kShellProbe >= 0)
                    {
                        // Task 130 (DIAGNOSTIC, shellsplit arms): the shell march alone, for the pixels the
                        // primary pass flagged. Reads what ShellMarch reads; ShadowStats for its counters.
                        cmd.BeginSample("VE.Raymarch.ShellProbe");
                        cmd.SetComputeBufferParam(data.compute, data.kShellProbe, "ShellDir", data.shellDir);
                        cmd.SetComputeBufferParam(data.compute, data.kShellProbe, "ShellPages", data.shellPages);
                        cmd.SetComputeBufferParam(data.compute, data.kShellProbe, "ShellBricks", data.shellBricks);
                        cmd.SetComputeBufferParam(data.compute, data.kShellProbe, "FarResident", data.farResident);
                        cmd.SetComputeBufferParam(data.compute, data.kShellProbe, "ShellProbeIO", data.shellProbeIO);
                        cmd.SetComputeBufferParam(data.compute, data.kShellProbe, "ShadowStats", data.shadowStats ?? _farDummy);
                        cmd.DispatchCompute(data.compute, data.kShellProbe, (data.dispatchWidth + 7) / 8, (data.dispatchHeight + 7) / 8, 1);
                        cmd.EndSample("VE.Raymarch.ShellProbe");
                    }

                    if (data.ups != null) UpsDispatch(cmd, data.ups, data.taaCur, data.taaMetaW);
                    if (data.taaResolve != 0)
                    {
                        cmd.BeginSample("VE.Raymarch.Taa");
                        cmd.SetComputeTextureParam(data.compute, data.kTaaResolve, "TaaCurColor", data.taaScratch);
                        cmd.SetComputeBufferParam(data.compute, data.kTaaResolve, "TaaCur", data.taaCur);
                        cmd.SetComputeBufferParam(data.compute, data.kTaaResolve, "TaaMetaR", data.taaMetaR);
                        cmd.SetComputeBufferParam(data.compute, data.kTaaResolve, "TaaColR", data.taaColR);
                        cmd.SetComputeBufferParam(data.compute, data.kTaaResolve, "TaaColW", data.taaColW);
                        cmd.SetComputeBufferParam(data.compute, data.kTaaResolve, "TaaMetaW", data.taaMetaW);
                        cmd.DispatchCompute(data.compute, data.kTaaResolve, threadGroupsX, threadGroupsY, 1);
                        cmd.SetComputeBufferParam(data.compute, data.kTaaSharpen, "TaaColS", data.taaColW);
                        cmd.SetComputeTextureParam(data.compute, data.kTaaSharpen, "ResultTexture", data.target);
                        cmd.DispatchCompute(data.compute, data.kTaaSharpen, threadGroupsX, threadGroupsY, 1);
                        cmd.EndSample("VE.Raymarch.Taa");
                    }
                });
            }

            // FSR 3 upscales the gate image to the camera target's size; the blit
            // below then copies 1:1.
            TextureHandle blitSource = target;
            if (upsIn && UpscaleInputs == UpscalerInputMode.Fsr3)
                blitSource = UpsRecordFsr3(renderGraph, frameData, target, cameraData);
            using (var builder = renderGraph.AddRasterRenderPass<BlitPassData>("VoxelRaymarch_Blit", out var blitData))
            {
                blitData.source = blitSource;
                builder.UseTexture(blitSource, AccessFlags.Read);
                builder.SetRenderAttachment(activeColor, 0, AccessFlags.Write);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc((BlitPassData data, RasterGraphContext context) =>
                {
                    // BILINEAR (2026-10-01, owner: moire): 960 -> 1080 is a
                    // 1.125x scale, and point sampling doubles every 8th row
                    // and column -- a fixed grid that beats against the
                    // terrain's own patterns (moire). Bilinear spreads it out.
                    Blitter.BlitTexture(context.cmd, data.source, new Vector4(1, 1, 0, 0), 0, true);
                });
            }
        }
    }

    public ComputeShader raymarchShader;
    public ComputeShader raymarchShaderStripped;
    public ComputeShader raymarchShaderMemoryProbe;

    private RaymarchPass _pass;

    public override void Create()
    {
        _pass = new RaymarchPass(raymarchShader, raymarchShaderStripped, raymarchShaderMemoryProbe);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (raymarchShader != null)
        {
            int mode = UseDebugViewOverride ? (int)DebugViewOverride : (int)debugMode;
            _pass.SetDebugMode(mode);

            bool forceRes; int gw, gh;
            switch (GateModeOverride)
            {
                case GateResMode.Native: forceRes = false; gw = 0; gh = 0; break;
                case GateResMode.Forced960x540: forceRes = true; gw = 960; gh = 540; break;
                case GateResMode.ForcedCustom: forceRes = true; gw = CustomGateResolution.x; gh = CustomGateResolution.y; break;
                default: forceRes = _forceGateResolution; gw = _gateWidth; gh = _gateHeight; break;
            }
            _pass.SetGateResolution(forceRes, gw, gh);

            renderer.EnqueuePass(_pass);
            if (UpscaleInputs == UpscalerInputMode.Stp)
            {
                if (_stpInputsPass == null) _stpInputsPass = new StpInputsPass();
                renderer.EnqueuePass(_stpInputsPass);
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        DebugBuffer?.Release();
        DebugBuffer = null;
        ShadowStatsBuffer?.Release();
        ShadowStatsBuffer = null;
        _farDummy?.Release();
        _shellProbeIO?.Release(); _shellProbeIO = null;
        _shellProbeDummy?.Release(); _shellProbeDummy = null;
        _shellPre?.Release(); _shellPre = null;
        _shellPreDummy?.Release(); _shellPreDummy = null;
        _lightDummy?.Release(); _lightDummy = null;
        _farDummy = null;
        _taaColA?.Release(); _taaColB?.Release(); _taaMetaA?.Release(); _taaMetaB?.Release(); _taaCur?.Release();
        _taaDummy4?.Release(); _taaDummy2?.Release();
        _taaColA = _taaColB = _taaMetaA = _taaMetaB = _taaCur = _taaDummy4 = _taaDummy2 = null;
        _taaValid = false;
        UpsDispose();
        base.Dispose(disposing);
    }
}