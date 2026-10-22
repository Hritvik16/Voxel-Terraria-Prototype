// ==========================================
// Assets/CoreEngine/Rendering/LightingConfig.cs
//
// PHASE 7. §13 Phase 7's solo-dev note is the entire justification for this
// file, so it is worth quoting rather than summarising:
//
//     "the fused shadow ray is a day of code, not a week. THE REAL WORK IS
//      TUNING (penumbra constant, ambient curve, sun-color ramp) -- that's what
//      the scrubber is for. TUNE BY EYE IN REAL TIME."
//
// §8.1 already established the mechanism for exactly this problem and calls it
// "the single highest-value tool for tuning game feel": the numbers live in
// JSON, a poll reloads them while the game runs, and a typo mid-edit must not
// be able to break a live session. PlayerConfig.cs is that mechanism for
// movement. THIS FILE IS A DELIBERATE, STRUCTURAL COPY OF IT -- same persistent
// -data-path location, same timestamp-and-length poll, same pure
// Parse/Validate split so EditMode can prove the malformed-file behaviour
// without a scene, and the same "a half-written file leaves the RUNNING config
// untouched" guarantee.
//
// WHY A COPY AND NOT A SHARED GENERIC BASE. The two configs share a shape, not
// a contract: PlayerConfig's Validate clamps things that would tunnel a player
// through the floor, this one clamps things that would black out the screen.
// A shared base would have to be written to the union of both, and §0.1
// invariant 9 ("the dumbest implementation that works, wins") says two ~200
// line files that each read straight through beat one clever one. The cost of
// the duplication is that a fix to the reload mechanism has to be made twice;
// that is noted here so the second site is findable, and the reload mechanism
// has not changed since Phase 6.
//
// WRITTEN IN STAGE 2 RATHER THAN STAGE 7, DELIBERATELY. The phase plan puts
// this file in Stage 7 with the Playground wiring, but SunCycle (Stage 2) needs
// the sun-colour ramp and the day-length multiplier and LightSourceBuffer
// (Stage 2) needs the C.11 falloff constant and the base light range. Writing
// those constants in Stage 2 and again in Stage 7 would put the same number in
// two places, which §0.1 invariant 8 forbids outright. The file lands here; the
// Playground keys, the HUD readout and the guide update still land in Stage 7.
//
// WHERE THE FILE LIVES: Application.persistentDataPath/LightingConfig.json.
// On macOS that is
//   ~/Library/Application Support/DefaultCompany/<product>/LightingConfig.json
// -- the same directory PlayerConfig.json is already in, so the owner has one
// folder to open, not two. The path is printed once at startup.
//
// EVERY NUMBER BELOW IS A PLACEHOLDER AWAITING THE OWNER'S EYE. They are
// authored to be defensible on day one, not to be right. §10 of the Phase 7
// scope lists the art constants as FLAG-DO-NOT-DECIDE, and that is what this
// file is: the decision surface, shipped with sane defaults.

using System;
using System.Globalization;
using System.IO;
using UnityEngine;

[Serializable]
public sealed class LightingConfig
{
    // =====================================================================
    // C.10 -- THE PENUMBRA CONSTANT
    // =====================================================================

    /// C.10's `k_soft`. `sunVis = smoothstep(0, k_soft, m)` where `m` is the
    /// minimum clearance/travel ratio, i.e. the angular miss distance -- so
    /// k_soft is effectively the sun's angular radius in radians.
    ///
    /// 0 means HARD SHADOWS, and §6.5 requires that toggle: "hard shadows
    /// remain a toggle." It is handled as a branch, not as a limit, in both
    /// ShadowRayReference and the shader -- smoothstep with a zero-width edge
    /// is undefined, and a NaN here would black out every lit pixel.
    ///
    /// 0.03 rad is ~1.7 degrees, roughly three times the real sun's angular
    /// radius. Chosen because a physically-correct 0.0047 produces a penumbra
    /// that is invisible at 0.1 m voxels, and the point of the term is to be
    /// seen. Tune by eye; that is what it is here for.
    public float kSoft = 0.03f;

    // =====================================================================
    // C.11 -- POINT LIGHTS
    // =====================================================================

    /// C.11's `k` in `atten = 1/(1 + d^2 * k)`, with `d` in METRES. Larger k
    /// falls off faster. At k = 0.35 a light is at half strength ~1.7 m out and
    /// ~10% at 5 m, which reads as a torch rather than a floodlight.
    public float pointFalloffK = 0.35f;

    /// A.6's `range`: the cutoff radius in metres, before the per-material
    /// scale in MaterialRules.TryGetLight. Beyond it a light contributes
    /// nothing at all -- that hard cutoff is what keeps §3.8's per-pixel loop
    /// bounded, so it is not merely cosmetic.
    public float lightRangeM = 8f;

    /// Multiplies every light's colour. The one knob for "torches are too dim /
    /// too blinding" without touching the per-material tints.
    public float lightIntensity = 1.6f;

    // =====================================================================
    // LAVA LIGHT -- one aggregate point light per lava-containing tile
    // (owner decision 8, 2026-09-24; PHASE_7_COMPLETION §H.2 Option 2)
    // =====================================================================

    /// The most §3.8 slots lava may occupy. Placed torches always keep
    /// MAX_ACTIVE_LIGHTS minus this many, however much lava is near. 0 turns
    /// lava lighting off (lava still self-lights, as before decision 8).
    public int lavaLightMaxSlots = 8;

    /// Colour multiplier on a FULL tile's light, on top of lightIntensity.
    public float lavaLightIntensity = 1.0f;

    /// Lava cells in a tile at which its light reaches full strength. Below
    /// it the light scales by sqrt(count / this), so a trickle glows faintly
    /// and a pool glows fully, without a lake saturating.
    public float lavaLightFullCount = 96f;

    /// Range multiplier over lava's own per-material range: one aggregate
    /// stands in for a whole 3.2 m tile, so it has to reach further than one
    /// voxel's light would.
    public float lavaLightRangeScale = 1.6f;

    /// Height of the aggregate light above the tile's top lava surface, in
    /// metres. Above the surface so the ground around a pool faces it (N.L).
    public float lavaLightLiftM = 0.6f;

    // =====================================================================
    // AMBIENT -- §6.5's hemisphere term, and the cave-darkness requirement
    // =====================================================================

    /// `ambientSky` in C.9: a cheap hemisphere term, also the stand-in for
    /// bounce light into cave mouths. Scales the whole ambient contribution.
    public float ambientIntensity = 0.30f;

    /// The colour ambient light takes from the sky. Kept slightly cool so
    /// sunlit faces separate from shaded ones by hue as well as by value.
    public float ambientColorR = 0.52f;
    public float ambientColorG = 0.62f;
    public float ambientColorB = 0.78f;

    /// Ambient floor at night, as a fraction of ambientIntensity. §6.5: "below
    /// the horizon: skip shadow rays, sun term 0, AMBIENT CARRIES THE NIGHT."
    /// If this were 0 the world would be perfectly black at midnight, which is
    /// not "night" but "no render".
    public float nightAmbientFraction = 0.16f;

    /// THE FRACTION OF AMBIENT THAT SURVIVES WITH ZERO SKY VISIBILITY, 0..1.
    /// This is the knob for §6.5's "underground reads near-zero ambient --
    /// caves are genuinely dark and torch-lit." 0 makes a sealed cave perfectly
    /// black; 1 disables the effect entirely and gives the plain hemisphere
    /// term.
    ///
    /// THIS IS ONE HALF OF AN OPEN DESIGN QUESTION, NOT A SETTLED CONSTANT.
    /// §6.5 asks for three things at once -- a cheap hemisphere term, ambient
    /// as the bounce stand-in for cave mouths, and genuinely dark deep caves --
    /// and a plain normal-based hemisphere term cannot deliver the third,
    /// because it lights a cave ceiling exactly as brightly as open sky does.
    /// What ships is a straight-up sky-visibility probe (the SAME shadow-ray
    /// function with the direction fixed to +Y), which is the cheapest thing
    /// that satisfies all three and reuses already-proven code rather than
    /// adding a second traversal. It IS a second ray, and it is measured as its
    /// own arm rather than assumed free -- see AMENDMENT_8_12_PHASE7_GATE.md
    /// and PHASE_7_COMPLETION.md's FLAG list for the options and their costs.
    /// 0.05 keeps a sealed cave nearly black while leaving just enough to tell
    /// a wall from a hole before a torch is placed.
    public float undergroundAmbientFloor = 0.05f;

    // =====================================================================
    // THE SUN -- colour ramp across the cycle, and how fast the cycle runs
    // =====================================================================

    /// Sun colour at the horizon (elevation 0). Deep warm orange.
    public float sunHorizonR = 1.00f;
    public float sunHorizonG = 0.45f;
    public float sunHorizonB = 0.18f;

    /// Sun colour at zenith. Near-white with a trace of warmth -- a pure white
    /// sun makes every material read as a greyscale version of itself.
    public float sunZenithR = 1.00f;
    public float sunZenithG = 0.97f;
    public float sunZenithB = 0.92f;

    /// Elevation in DEGREES at which the ramp finishes reaching its zenith
    /// colour. Below this the sun is still visibly warm; that band is golden
    /// hour, and it is the band §13 asks to be captured specifically.
    public float sunRampTopDegrees = 22f;

    /// Overall sun brightness multiplier.
    public float sunIntensity = 1.25f;

    /// Real seconds per in-game day. 120 s is short enough that a full
    /// dawn-to-dusk sweep is watchable inside the owner's five minutes, and
    /// long enough that golden hour is not a single frame. This is the number
    /// to raise once the look is settled.
    public float dayLengthSeconds = 120f;

    // =====================================================================
    // TONEMAPPING -- §6.1's deferred "post", added in Phase 7
    // =====================================================================

    /// 0 = off (hard clamp, the pre-Phase-7 behaviour, kept reproducible)
    /// 1 = Reinhard with a white point   2 = ACES filmic
    ///
    /// This is an AXIS of the presets, not a global decision: every preset is
    /// screenshotted both with and without, because a curve changes the look
    /// more than any single intensity value and the owner should compare rather
    /// than inherit my choice.
    public int tonemap = 2;

    /// Linear exposure applied BEFORE the curve. With a curve in play this is
    /// the natural brightness control -- it slides the image along the curve
    /// instead of pushing values into a clamp.
    public float exposure = 1.0f;

    /// Reinhard's white point: the linear value that maps to display white.
    /// Higher keeps more highlight detail and looks flatter; lower clips
    /// sooner and looks punchier. Ignored by ACES.
    public float tonemapWhite = 2.2f;

    /// STAGE F's LEVER. How far a SUN SHADOW RAY may travel, in METRES.
    /// <= 0 inherits the primary ray's clamp (290 m), which is the behaviour
    /// every measurement before 2026-09-13 was taken under.
    ///
    /// Stage F measured 0.003% of hit pixels beyond 128 m at the ground-level
    /// gameplay camera, yet every shadow ray was allowed 290 m. Shortening this
    /// bounds the single most expensive thing in the frame (§6's isolation put
    /// the sun ray at +25.5 ms). The trade is that distant geometry stops
    /// casting onto you -- a QUALITY decision, which is why it lives here and
    /// is per-preset rather than being a hidden constant.
    public float shadowMaxDistanceM = 0f;

    /// Rung 1's engagement threshold in metres. <= 0 uses the LOD1 boundary
    /// (128 m). Exposed only so Stage F could test whether the THRESHOLD rather
    /// than the mechanism was why rung 1 never engaged.
    public float rung1DistanceM = 0f;

    /// Shadow LOD coarsening by ray travel (see Raymarch.compute's
    /// _ShadowTravelLOD note). 0 = off, which is the shipped §6.5 8.2 behaviour.
    /// DEFAULT IS 0 DELIBERATELY: this is a new approximation and turning it on
    /// by default would silently change every shadow in the game and every
    /// number measured against it. It is switched on by measurement, not by me.
    public float shadowTravelLod = 0f;

    /// Within-material colour variation (§M.4 item 1 -- the highest
    /// look-per-effort item in that assessment). OFF by default: it changes how
    /// every surface in the game looks, which is an art decision and §10 flag 5
    /// reserves those for the owner.
    public int materialVariation = 0;
    public float materialVariationAmount = 1.0f;

    /// Axial tilt of the sun's arc, unitless. 0 puts the sun's path in a
    /// vertical plane through the zenith, which makes every shadow sweep
    /// through exactly one azimuth and looks artificial. 0.25 tilts the arc so
    /// shadows swing as well as shorten.
    public float sunArcTilt = 0.25f;

    // =====================================================================
    // THE LOOK -- which shading model the final combine uses (2026-09-24)
    // =====================================================================

    /// 1 = THE BRICKMAP LOOK (default). The owner's recorded decision is that
    ///     VoxelTerraria_Brickmap is the look reference. This path reproduces
    ///     that engine's shading from its own source: its palette (mirrored in
    ///     MaterialPalette.OfLook), its three-tone grass, its face shade, its
    ///     floored sun term (a shadowed face keeps 25% of the sun instead of
    ///     going black), a zenith/horizon sky with a sun halo, squared distance
    ///     haze, and NO tone curve -- because that engine renders in GAMMA
    ///     colour space and its numbers are display values. The shader converts
    ///     the result to linear at the very end so URP's sRGB encode shows the
    ///     same value that engine shows. That conversion, not any single colour,
    ///     is most of why this project looked washed out beside it.
    /// 0 = C.9 as specified (§6.5), the look every Phase 7 document describes.
    ///     Kept byte-for-byte so every earlier screenshot stays reproducible.
    ///
    /// PRESENTATION ONLY. Traversal, shadow rays, the sky probe, the light
    /// buffer and every §13 mechanism are shared by both looks; only the
    /// colour maths after them differs. NOTE THE ONE SPEC DEVIATION: look 1
    /// floors N.L (0.8*N.L + 0.2) and lets shadowed faces keep
    /// (1 - lookShadowDarkness) of the sun, where C.9 says max(0, N.L) and
    /// full occlusion. That is the Brickmap's model and the fix for the black
    /// terrace contours; it is flagged in PHASE_7_COMPLETION.md, not hidden.
    public int look = 1;

    /// Sky gradient by day: zenith straight up, horizon at eye level. Also the
    /// haze target, and the horizon colour is the ambient light's colour.
    public float skyZenithR = 0.20f, skyZenithG = 0.40f, skyZenithB = 0.80f;
    public float skyHorizonR = 0.60f, skyHorizonG = 0.80f, skyHorizonB = 1.00f;

    /// The same two colours at night. Night is dark blue, not void.
    public float nightZenithR = 0.02f, nightZenithG = 0.02f, nightZenithB = 0.06f;
    public float nightHorizonR = 0.10f, nightHorizonG = 0.11f, nightHorizonB = 0.22f;

    /// Sun colour when high, and the colour it warms toward near the horizon.
    public float sunHaloR = 1.00f, sunHaloG = 0.90f, sunHaloB = 0.70f;
    public float goldenR = 1.00f, goldenG = 0.62f, goldenB = 0.32f;

    /// Direct sun multiplier (the Brickmap's `sun_light * 1.5`).
    public float lookSunScale = 1.5f;

    /// Ambient strength by day, as a fraction of the horizon colour (0.3 in the
    /// Brickmap), and at night as a fraction of the night horizon colour.
    public float lookAmbientStrength = 0.30f;
    public float lookNightAmbient = 0.90f;

    /// How much of the sun a SHADOWED face loses. 0.75 = a face in shadow still
    /// gets 25% of the direct term. 1.0 = C.9's full occlusion.
    public float lookShadowDarkness = 0.75f;

    /// Direct moonlight at night, from opposite the sun. No shadow ray.
    public float moonR = 0.16f, moonG = 0.19f, moonB = 0.30f;

    /// Distance haze: fog = saturate((d / renderRange)^2 * hazeDensity).
    /// 1.2 is the "restrained haze" the owner chose in the comparison repo;
    /// the Brickmap ships 2.5.
    public float hazeDensity = 1.2f;

    /// Emissive bleed: faces next to an emitting voxel (torch, lava) pick up
    /// its glow. 1 = on (default since task 46, owner Q5 (a), as in the SVO
    /// build), 0 = off (-emissivebleed 0).
    public int emissiveBleed = 1;

    /// TORCH SHADOW RAYS (task 46, owner Q5 (a); fixes "torches shine through
    /// walls"). Each lit tier-0 pixel traces an occlusion ray to its N nearest
    /// in-range point lights; the remaining in-range lights are scaled by the
    /// traced share (the fraction of the N that were visible) -- the SVO
    /// build's form. 0 = no rays, every light leaks through walls as before.
    /// -torchshadows N overrides. Capped at MaxTorchShadowRays.
    public int torchShadowRays = 4;
    public const int MaxTorchShadowRays = 8;

    // ---------------------------------------------------------------------
    // THE CINEMATIC GRADE (owner decisions 2-4, 2026-09-24) -- look 1 only.
    //
    // The Brickmap look was right at golden hour and blinding from 08:00 to
    // 16:00: full sun x 1.5 on snow is ~1.4x display white, so snow clipped flat
    // and grass went neon. The owner anchored golden hour to
    // Phase7Demo/2026-09-24_151908/1b_ground_golden_NEW.png.
    //
    // So the grade is an ELEVATION RAMP: amount 0 at and below gradeFromDeg,
    // full at gradeToDeg (smoothstep). Golden hour (6 deg), dawn, dusk and
    // night sit below the ramp and are PIXEL-IDENTICAL to lookGrade 0 -- the
    // anchor is preserved by construction, not by tuning. As the sun climbs
    // the grade lowers exposure on sun+ambient (not torches/lava), deepens the
    // day sky, takes the yellow out of the noon sun, trims saturation and
    // rolls highlights off with a soft shoulder instead of clipping.
    // 0 = the Brickmap look exactly as it was on 2026-09-24.
    // ---------------------------------------------------------------------
    public int lookGrade = 1;
    public float gradeFromDeg = 8f, gradeToDeg = 40f;
    /// Multiplier on sun + ambient at full grade (noon).
    public float gradeNoonExposure = 0.62f;
    /// Saturation at full grade (1 = unchanged).
    public float gradeSaturation = 0.86f;
    /// Display value where the highlight shoulder starts. 1 = no shoulder.
    public float gradeShoulderKnee = 0.70f;
    /// The day sky at full grade -- deeper than skyZenith/skyHorizon.
    public float cineZenithR = 0.14f, cineZenithG = 0.30f, cineZenithB = 0.66f;
    public float cineHorizonR = 0.50f, cineHorizonG = 0.66f, cineHorizonB = 0.86f;
    /// The sun's colour at full grade -- whiter than sunHalo's afternoon yellow.
    public float cineSunR = 1.00f, cineSunG = 0.95f, cineSunB = 0.86f;

    // =====================================================================
    // Validation
    // =====================================================================

    /// Clamps every field into a range the renderer can actually run with, and
    /// reports what it had to change.
    ///
    /// NOT OPTIONAL, for PlayerConfig.cs's reason transposed to lighting: this
    /// file is edited BY HAND, WHILE THE GAME IS RUNNING. A NaN in kSoft makes
    /// smoothstep return NaN and every lit pixel goes black; a negative
    /// lightRangeM makes the §3.8 cutoff reject every light so torches stop
    /// working with no error; a zero dayLengthSeconds divides by zero in the
    /// clock and freezes time at whatever it was. None of those are acceptable
    /// outcomes for a typo in a tuning file, and all three are one keystroke
    /// away.
    public string Validate()
    {
        var notes = new System.Text.StringBuilder();

        // kSoft's LOWER bound is 0 and 0 is MEANINGFUL -- it is §6.5's hard
        // shadow toggle. It must not be clamped up to a small positive number.
        kSoft            = Fix(kSoft, 0f, 1.5f, 0.03f, nameof(kSoft), notes);
        pointFalloffK    = Fix(pointFalloffK, 0.0001f, 1000f, 0.35f, nameof(pointFalloffK), notes);
        lightRangeM      = Fix(lightRangeM, 0.1f, 512f, 8f, nameof(lightRangeM), notes);
        lightIntensity   = Fix(lightIntensity, 0f, 64f, 1.6f, nameof(lightIntensity), notes);
        if (lavaLightMaxSlots < 0 || lavaLightMaxSlots > EngineConfig.MAX_ACTIVE_LIGHTS / 2)
        { notes.Append($"{nameof(lavaLightMaxSlots)} {lavaLightMaxSlots}->8; "); lavaLightMaxSlots = 8; }
        lavaLightIntensity  = Fix(lavaLightIntensity, 0f, 16f, 1.0f, nameof(lavaLightIntensity), notes);
        lavaLightFullCount  = Fix(lavaLightFullCount, 1f, 32768f, 96f, nameof(lavaLightFullCount), notes);
        lavaLightRangeScale = Fix(lavaLightRangeScale, 0.1f, 16f, 1.6f, nameof(lavaLightRangeScale), notes);
        lavaLightLiftM      = Fix(lavaLightLiftM, 0f, 4f, 0.6f, nameof(lavaLightLiftM), notes);

        ambientIntensity = Fix(ambientIntensity, 0f, 8f, 0.30f, nameof(ambientIntensity), notes);
        ambientColorR    = Fix(ambientColorR, 0f, 1f, 0.52f, nameof(ambientColorR), notes);
        ambientColorG    = Fix(ambientColorG, 0f, 1f, 0.62f, nameof(ambientColorG), notes);
        ambientColorB    = Fix(ambientColorB, 0f, 1f, 0.78f, nameof(ambientColorB), notes);
        nightAmbientFraction = Fix(nightAmbientFraction, 0f, 1f, 0.16f, nameof(nightAmbientFraction), notes);
        undergroundAmbientFloor =
            Fix(undergroundAmbientFloor, 0f, 1f, 0.05f, nameof(undergroundAmbientFloor), notes);

        sunHorizonR      = Fix(sunHorizonR, 0f, 1f, 1.00f, nameof(sunHorizonR), notes);
        sunHorizonG      = Fix(sunHorizonG, 0f, 1f, 0.45f, nameof(sunHorizonG), notes);
        sunHorizonB      = Fix(sunHorizonB, 0f, 1f, 0.18f, nameof(sunHorizonB), notes);
        sunZenithR       = Fix(sunZenithR, 0f, 1f, 1.00f, nameof(sunZenithR), notes);
        sunZenithG       = Fix(sunZenithG, 0f, 1f, 0.97f, nameof(sunZenithG), notes);
        sunZenithB       = Fix(sunZenithB, 0f, 1f, 0.92f, nameof(sunZenithB), notes);
        // Lower bound is well above 0: a zero-width ramp divides by zero when
        // normalising elevation into it.
        sunRampTopDegrees = Fix(sunRampTopDegrees, 0.5f, 90f, 22f, nameof(sunRampTopDegrees), notes);
        sunIntensity     = Fix(sunIntensity, 0f, 64f, 1.25f, nameof(sunIntensity), notes);

        // A day cannot be zero seconds long -- the clock would divide by it.
        dayLengthSeconds = Fix(dayLengthSeconds, 1f, 86400f, 120f, nameof(dayLengthSeconds), notes);
        sunArcTilt       = Fix(sunArcTilt, -1f, 1f, 0.25f, nameof(sunArcTilt), notes);
        exposure         = Fix(exposure, 0f, 32f, 1.0f, nameof(exposure), notes);
        shadowMaxDistanceM = Fix(shadowMaxDistanceM, 0f, 4000f, 0f, nameof(shadowMaxDistanceM), notes);
        rung1DistanceM     = Fix(rung1DistanceM, 0f, 4000f, 0f, nameof(rung1DistanceM), notes);
        tonemapWhite     = Fix(tonemapWhite, 0.1f, 64f, 2.2f, nameof(tonemapWhite), notes);
        if (tonemap < 0 || tonemap > 2)
        { notes.Append($"{nameof(tonemap)} {tonemap}->2; "); tonemap = 2; }

        if (look < 0 || look > 1)
        { notes.Append($"{nameof(look)} {look}->1; "); look = 1; }
        skyZenithR = Fix(skyZenithR, 0f, 1f, 0.20f, nameof(skyZenithR), notes);
        skyZenithG = Fix(skyZenithG, 0f, 1f, 0.40f, nameof(skyZenithG), notes);
        skyZenithB = Fix(skyZenithB, 0f, 1f, 0.80f, nameof(skyZenithB), notes);
        skyHorizonR = Fix(skyHorizonR, 0f, 1f, 0.60f, nameof(skyHorizonR), notes);
        skyHorizonG = Fix(skyHorizonG, 0f, 1f, 0.80f, nameof(skyHorizonG), notes);
        skyHorizonB = Fix(skyHorizonB, 0f, 1f, 1.00f, nameof(skyHorizonB), notes);
        nightZenithR = Fix(nightZenithR, 0f, 1f, 0.02f, nameof(nightZenithR), notes);
        nightZenithG = Fix(nightZenithG, 0f, 1f, 0.02f, nameof(nightZenithG), notes);
        nightZenithB = Fix(nightZenithB, 0f, 1f, 0.06f, nameof(nightZenithB), notes);
        nightHorizonR = Fix(nightHorizonR, 0f, 1f, 0.10f, nameof(nightHorizonR), notes);
        nightHorizonG = Fix(nightHorizonG, 0f, 1f, 0.11f, nameof(nightHorizonG), notes);
        nightHorizonB = Fix(nightHorizonB, 0f, 1f, 0.22f, nameof(nightHorizonB), notes);
        sunHaloR = Fix(sunHaloR, 0f, 1f, 1.00f, nameof(sunHaloR), notes);
        sunHaloG = Fix(sunHaloG, 0f, 1f, 0.90f, nameof(sunHaloG), notes);
        sunHaloB = Fix(sunHaloB, 0f, 1f, 0.70f, nameof(sunHaloB), notes);
        goldenR = Fix(goldenR, 0f, 1f, 1.00f, nameof(goldenR), notes);
        goldenG = Fix(goldenG, 0f, 1f, 0.62f, nameof(goldenG), notes);
        goldenB = Fix(goldenB, 0f, 1f, 0.32f, nameof(goldenB), notes);
        lookSunScale = Fix(lookSunScale, 0f, 8f, 1.5f, nameof(lookSunScale), notes);
        lookAmbientStrength = Fix(lookAmbientStrength, 0f, 4f, 0.30f, nameof(lookAmbientStrength), notes);
        lookNightAmbient = Fix(lookNightAmbient, 0f, 4f, 0.90f, nameof(lookNightAmbient), notes);
        lookShadowDarkness = Fix(lookShadowDarkness, 0f, 1f, 0.75f, nameof(lookShadowDarkness), notes);
        moonR = Fix(moonR, 0f, 1f, 0.16f, nameof(moonR), notes);
        moonG = Fix(moonG, 0f, 1f, 0.19f, nameof(moonG), notes);
        moonB = Fix(moonB, 0f, 1f, 0.30f, nameof(moonB), notes);
        hazeDensity = Fix(hazeDensity, 0f, 16f, 1.2f, nameof(hazeDensity), notes);
        if (lookGrade < 0 || lookGrade > 1)
        { notes.Append($"{nameof(lookGrade)} {lookGrade}->1; "); lookGrade = 1; }
        if (emissiveBleed < 0 || emissiveBleed > 1)
        { notes.Append($"{nameof(emissiveBleed)} {emissiveBleed}->1; "); emissiveBleed = 1; }
        if (torchShadowRays < 0 || torchShadowRays > MaxTorchShadowRays)
        { notes.Append($"{nameof(torchShadowRays)} {torchShadowRays}->4; "); torchShadowRays = 4; }
        gradeFromDeg = Fix(gradeFromDeg, -90f, 90f, 8f, nameof(gradeFromDeg), notes);
        gradeToDeg = Fix(gradeToDeg, -90f, 90f, 40f, nameof(gradeToDeg), notes);
        if (gradeToDeg <= gradeFromDeg)
        { notes.Append($"{nameof(gradeToDeg)} <= {nameof(gradeFromDeg)} -> 8/40; "); gradeFromDeg = 8f; gradeToDeg = 40f; }
        gradeNoonExposure = Fix(gradeNoonExposure, 0.05f, 4f, 0.62f, nameof(gradeNoonExposure), notes);
        gradeSaturation = Fix(gradeSaturation, 0f, 2f, 0.86f, nameof(gradeSaturation), notes);
        gradeShoulderKnee = Fix(gradeShoulderKnee, 0.1f, 1f, 0.70f, nameof(gradeShoulderKnee), notes);
        cineZenithR = Fix(cineZenithR, 0f, 1f, 0.14f, nameof(cineZenithR), notes);
        cineZenithG = Fix(cineZenithG, 0f, 1f, 0.30f, nameof(cineZenithG), notes);
        cineZenithB = Fix(cineZenithB, 0f, 1f, 0.66f, nameof(cineZenithB), notes);
        cineHorizonR = Fix(cineHorizonR, 0f, 1f, 0.50f, nameof(cineHorizonR), notes);
        cineHorizonG = Fix(cineHorizonG, 0f, 1f, 0.66f, nameof(cineHorizonG), notes);
        cineHorizonB = Fix(cineHorizonB, 0f, 1f, 0.86f, nameof(cineHorizonB), notes);
        cineSunR = Fix(cineSunR, 0f, 1f, 1.00f, nameof(cineSunR), notes);
        cineSunG = Fix(cineSunG, 0f, 1f, 0.95f, nameof(cineSunG), notes);
        cineSunB = Fix(cineSunB, 0f, 1f, 0.86f, nameof(cineSunB), notes);

        return notes.Length == 0 ? null : notes.ToString().TrimEnd();
    }

    private static float Fix(float v, float lo, float hi, float fallback, string name,
                             System.Text.StringBuilder notes)
    {
        if (float.IsNaN(v) || float.IsInfinity(v))
        {
            notes.Append($"{name} not finite -> {fallback.ToString(CultureInfo.InvariantCulture)}; ");
            return fallback;
        }
        if (v < lo) { notes.Append($"{name} {v.ToString(CultureInfo.InvariantCulture)}->{lo.ToString(CultureInfo.InvariantCulture)}; "); return lo; }
        if (v > hi) { notes.Append($"{name} {v.ToString(CultureInfo.InvariantCulture)}->{hi.ToString(CultureInfo.InvariantCulture)}; "); return hi; }
        return v;
    }

    public LightingConfig Clone() => (LightingConfig)MemberwiseClone();

    // =====================================================================
    // Parsing -- pure, so EditMode can prove the failure behaviour
    // =====================================================================

    /// Parses `json`, validates it, and returns it. On ANY failure returns
    /// `fallback` UNCHANGED and sets `error`.
    ///
    /// Returning the fallback rather than defaults is the behaviour that makes
    /// live editing safe: the text editor saves in two steps, so a poll can
    /// land on a truncated file. Snapping to shipped defaults at that instant
    /// would make the screen jump every time the owner hits save. The next
    /// successful read picks up the finished edit.
    public static LightingConfig Parse(string json, LightingConfig fallback, out string error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(json)) { error = "empty file"; return fallback; }

        LightingConfig parsed;
        try
        {
            parsed = JsonUtility.FromJson<LightingConfig>(json);
        }
        catch (Exception e)
        {
            error = "malformed JSON: " + e.Message;
            return fallback;
        }
        if (parsed == null) { error = "malformed JSON (null result)"; return fallback; }

        string clamped = parsed.Validate();
        if (clamped != null) error = "clamped: " + clamped;   // not fatal; the values are usable
        return parsed;
    }

    public string ToJson() => JsonUtility.ToJson(this, true);

    // =====================================================================
    // The live instance + hot reload
    // =====================================================================

    private static LightingConfig _active;
    private static string _path;
    private static DateTime _lastWriteUtc;
    private static long _lastLength = -1;

    /// The config the renderer reads. Never null after startup.
    public static LightingConfig Active => _active ?? (_active = new LightingConfig());

    /// Where the JSON lives. Empty until Initialize has run.
    public static string ActivePath => _path ?? string.Empty;

    /// Bumped on every successful reload. A rig asserts on this rather than
    /// sleeping and hoping.
    public static int ReloadCount { get; private set; }

    /// The most recent parse/clamp problem, or null. Surfaced on the HUD so a
    /// typo shows up as a message rather than as mysteriously wrong lighting.
    public static string LastError { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    public static void Initialize()
    {
        try
        {
            _path = Path.Combine(Application.persistentDataPath, "LightingConfig.json");
            if (!File.Exists(_path))
            {
                _active = new LightingConfig();
                Directory.CreateDirectory(Path.GetDirectoryName(_path));
                File.WriteAllText(_path, _active.ToJson());
                Debug.Log($"[LightingConfig] wrote defaults -> {_path}");
            }
            else
            {
                _active = new LightingConfig();
                ReloadNow();
            }
            StampFile();
            Debug.Log($"[LightingConfig] tune live by editing {_path}");
        }
        catch (Exception e)
        {
            _active = new LightingConfig();
            LastError = "init failed: " + e.Message;
            Debug.LogWarning("[LightingConfig] " + LastError);
        }
    }

    /// Cheap per-frame check: reload only when the file's timestamp or length
    /// actually moved. Polling beats FileSystemWatcher here for PlayerConfig's
    /// stated reason -- the watcher is unreliable across editors and network
    /// volumes on macOS, and two stat() calls a frame is nothing.
    public static bool ReloadIfChanged()
    {
        if (string.IsNullOrEmpty(_path)) return false;
        try
        {
            var info = new FileInfo(_path);
            if (!info.Exists) return false;
            if (info.LastWriteTimeUtc == _lastWriteUtc && info.Length == _lastLength) return false;
            StampFile();
            return ReloadNow();
        }
        catch (Exception e)
        {
            LastError = "stat failed: " + e.Message;
            return false;
        }
    }

    private static bool ReloadNow()
    {
        try
        {
            string json = File.ReadAllText(_path);
            string err;
            LightingConfig next = Parse(json, Active, out err);
            LastError = err;
            if (!ReferenceEquals(next, Active))
            {
                _active = next;
                ReloadCount++;
                Debug.Log($"[LightingConfig] reloaded (#{ReloadCount})" + (err != null ? " -- " + err : ""));
                return true;
            }
            return false;
        }
        catch (Exception e)
        {
            LastError = "read failed: " + e.Message;
            return false;
        }
    }

    private static void StampFile()
    {
        try
        {
            var info = new FileInfo(_path);
            if (!info.Exists) return;
            _lastWriteUtc = info.LastWriteTimeUtc;
            _lastLength = info.Length;
        }
        catch { /* stamping is best-effort */ }
    }

    /// Test/rig seam: install a config directly, bypassing disk.
    public static void DebugSetActive(LightingConfig c)
    {
        if (c == null) return;
        c.Validate();
        _active = c;
        ReloadCount++;
    }
}
