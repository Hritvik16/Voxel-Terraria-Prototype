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

    /// Axial tilt of the sun's arc, unitless. 0 puts the sun's path in a
    /// vertical plane through the zenith, which makes every shadow sweep
    /// through exactly one azimuth and looks artificial. 0.25 tilts the arc so
    /// shadows swing as well as shorten.
    public float sunArcTilt = 0.25f;

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
