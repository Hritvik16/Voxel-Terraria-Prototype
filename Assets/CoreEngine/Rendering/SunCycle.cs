// ==========================================
// Assets/CoreEngine/Rendering/SunCycle.cs
//
// PHASE 7, §13 file 2: "clock -> sun direction/color uniforms."
//
// THIS FILE OWNS THE CLOCK TOO, and that is not scope creep -- it is a finding.
// Stage 0 searched the whole of Assets/ for TimeOfDay, WorldClock, GameClock,
// dayLength, SunCycle, _SunDir and sunDirection and found NOTHING. There is no
// in-game clock in this project for a sun cycle to read from, so §13's arrow
// ("clock -> sun direction") has no left-hand side until this file supplies
// one. It is a float and a wrap; it does not deserve its own file.
//
// §6.5's DAY/NIGHT PARAGRAPH IS THE WHOLE SPECIFICATION:
//
//     "Day/night: sun direction + color are two animated uniforms from the
//      in-game clock. No cascades to re-render, no staleness -- the shadow is
//      exact every frame at every distance. BELOW THE HORIZON: SKIP SHADOW
//      RAYS (SUN TERM = 0), AMBIENT CARRIES THE NIGHT."
//
// "No cascades to re-render, no staleness" is worth dwelling on, because it is
// why this file is as small as it is. A shadow-map renderer would need a
// cascade update, a stability scheme and a snapping rule here. A fused shadow
// ray needs a direction and a colour, and the shadow is re-derived from live
// terrain bytes every frame by construction (§3.10). There is nothing to
// invalidate, so there is no invalidation code.
//
// EVERYTHING IS A PURE STATIC FUNCTION OF AN HOUR. DirectionAt, ColorAt,
// ElevationDegreesAt and HoursForElevation take a time and return a value --
// no Unity lifecycle, no scene, no singleton state -- so EditMode can prove the
// sunrise/sunset/midnight/golden-hour behaviour without a scene, the way
// PlayerConfig.Parse is pure for the same reason. The only mutable state is
// `Hours` itself, and Advance() is the only thing that moves it.
//
// THE ART CONSTANTS ARE NOT HERE. Day length, the colour ramp endpoints, the
// ramp width and the arc tilt all live in LightingConfig so they are editable
// while the game runs (§13's solo-dev note: "tune by eye in real time"). This
// file holds the SHAPE of the cycle; that file holds its NUMBERS.
// ==========================================

using Unity.Mathematics;

public static class SunCycle
{
    // =====================================================================
    // The clock
    // =====================================================================

    /// In-game time of day in HOURS, [0, 24). 6 is sunrise, 12 is noon, 18 is
    /// sunset, 0 is midnight -- see DirectionAt for why those four are exact
    /// rather than approximate.
    public static float Hours { get; private set; } = 12f;

    /// Whole in-game days elapsed. Surfaced only so a rig can tell "the cycle
    /// wrapped" from "the cycle stalled" without inferring it from Hours.
    public static int DaysElapsed { get; private set; }

    /// True when the sun is above the horizon and a shadow ray is worth firing.
    public static bool SunIsUp => ElevationDegreesAt(Hours) > 0f;

    /// Advance the clock by `realDeltaSeconds`, scaled by
    /// LightingConfig.dayLengthSeconds. Validate() floors that at 1 s, so the
    /// division cannot blow up on a hand-edited zero.
    public static void Advance(float realDeltaSeconds)
    {
        float dayLength = LightingConfig.Active.dayLengthSeconds;
        SetHours(Hours + 24f * (realDeltaSeconds / dayLength));
    }

    /// Jump the clock. The scrubber (§10.3, §13's "time scrubber") and the
    /// Playground's golden-hour / noon / midnight keys both come through here,
    /// so there is one place that normalises and one place that counts days.
    public static void SetHours(float hours)
    {
        if (float.IsNaN(hours) || float.IsInfinity(hours)) return;   // a scrubber cannot break the clock
        while (hours >= 24f) { hours -= 24f; DaysElapsed++; }
        while (hours < 0f) { hours += 24f; DaysElapsed--; }
        Hours = hours;
    }

    /// Test/rig seam: put the clock in a known state without a frame.
    public static void DebugReset(float hours = 12f) { Hours = 12f; DaysElapsed = 0; SetHours(hours); }

    // =====================================================================
    // Geometry of the arc
    // =====================================================================

    /// The sun's angular position along its arc, in radians, such that
    /// hours = 6 -> 0 (east horizon), 12 -> pi/2 (zenith), 18 -> pi (west
    /// horizon). sin(theta) is therefore the elevation sine and is negative for
    /// exactly the hours the sun is down, with no special case.
    private static float Theta(float hours) => (hours - 6f) / 12f * math.PI;

    /// Sun direction: points FROM a surface TOWARD the sun -- the direction a
    /// shadow ray travels, and the `L` in C.9's `max(0, N.L)`.
    ///
    /// The sun rises in +X and sets in -X. `sunArcTilt` leans the arc toward
    /// +Z so that shadows SWING as well as shorten across the day; with tilt 0
    /// the arc lies in a vertical plane through the zenith, every shadow sweeps
    /// through a single azimuth, and §13's "scrub dawn->dusk: the mountain
    /// shadow sweeps continuously" is a much less interesting thing to look at.
    public static float3 DirectionAt(float hours)
    {
        float t = Theta(hours);
        float tilt = LightingConfig.Active.sunArcTilt;
        return math.normalize(new float3(math.cos(t), math.sin(t), tilt));
    }

    public static float3 Direction => DirectionAt(Hours);

    /// Elevation above the horizon in DEGREES. Negative when the sun is down.
    ///
    /// Derived from the normalised direction rather than from sin(theta)
    /// directly, so the arc tilt is accounted for: a tilted arc never quite
    /// reaches the zenith, and quoting an elevation that ignored that would
    /// make the HUD disagree with the shadows on screen.
    public static float ElevationDegreesAt(float hours) =>
        math.degrees(math.asin(math.clamp(DirectionAt(hours).y, -1f, 1f)));

    public static float ElevationDegrees => ElevationDegreesAt(Hours);

    /// The time of day at which the sun sits at `degrees` above the horizon,
    /// on the morning or the evening side.
    ///
    /// EXISTS SO GOLDEN HOUR IS DERIVED AND NOT TYPED. §13 asks for golden hour
    /// to be "captured specifically", and a hardcoded 17.6 would silently stop
    /// meaning golden hour the moment sunArcTilt changed. Inverting the arc
    /// keeps the definition ("the sun is N degrees up") true whatever the arc
    /// does.
    public static float HoursForElevation(float degrees, bool morning)
    {
        float tilt = LightingConfig.Active.sunArcTilt;
        // With d = (cos t, sin t, tilt) / |(cos t, sin t, tilt)| and
        // |(cos t, sin t, tilt)| = sqrt(1 + tilt^2) (cos^2 + sin^2 = 1),
        // elevation sine is sin(t) / sqrt(1 + tilt^2), so:
        float s = math.sin(math.radians(degrees)) * math.sqrt(1f + tilt * tilt);
        s = math.clamp(s, -1f, 1f);
        float t = math.asin(s);                    // in [-pi/2, pi/2]
        if (!morning) t = math.PI - t;             // the evening branch of the same arc
        return 6f + t / math.PI * 12f;
    }

    /// §13's three judgement poses, derived from the arc rather than typed.
    /// GOLDEN HOUR is the evening one: §6.5 calls it "the longest grazing
    /// rays", which is the expensive case, and §13 asks for it specifically.
    public const float GoldenHourElevationDegrees = 6f;
    public static float GoldenHourHours => HoursForElevation(GoldenHourElevationDegrees, morning: false);
    public const float NoonHours = 12f;
    public const float MidnightHours = 0f;

    // =====================================================================
    // Colour
    // =====================================================================

    /// Sun colour at a given time, already scaled by sunIntensity.
    ///
    /// §6.5: below the horizon the sun term is 0. That is returned as an EXACT
    /// zero vector, not a small number -- the shader's own "is the sun up"
    /// branch and this colour must never disagree about whether it is night,
    /// or a sliver of sun would light the world from underneath while every
    /// shadow ray was being skipped.
    ///
    /// Above the horizon the ramp runs from the horizon colour to the zenith
    /// colour over `sunRampTopDegrees`, smoothstepped so dawn does not snap.
    /// The band below that top is golden hour, which is why the constant is
    /// tunable: it IS the width of the effect the owner is judging.
    public static float3 ColorAt(float hours)
    {
        float el = ElevationDegreesAt(hours);
        if (el <= 0f) return float3.zero;

        var c = LightingConfig.Active;
        float k = math.smoothstep(0f, math.max(0.5f, c.sunRampTopDegrees), el);
        var horizon = new float3(c.sunHorizonR, c.sunHorizonG, c.sunHorizonB);
        var zenith = new float3(c.sunZenithR, c.sunZenithG, c.sunZenithB);
        return math.lerp(horizon, zenith, k) * c.sunIntensity;
    }

    public static float3 Color => ColorAt(Hours);

    /// C.9's `ambientSky` colour, already scaled by intensity.
    ///
    /// §6.5 gives ambient two jobs that pull in opposite directions -- it is a
    /// "cheap hemisphere term" AND it is what "carries the night" -- so it
    /// cannot simply follow the sun to zero. It ramps from full daylight
    /// ambient down to `nightAmbientFraction` of it, and never below, because
    /// an ambient of exactly 0 at midnight is not night, it is a black screen.
    ///
    /// The ramp is driven by elevation and continues BELOW the horizon down to
    /// -12 degrees, which is what gives dusk a readable falloff instead of the
    /// world switching off the instant the sun clips the horizon.
    public static float3 AmbientAt(float hours)
    {
        var c = LightingConfig.Active;
        float el = ElevationDegreesAt(hours);
        float k = math.smoothstep(-12f, 6f, el);                  // 0 deep dusk .. 1 daylight
        float scale = math.lerp(c.nightAmbientFraction, 1f, k) * c.ambientIntensity;
        return new float3(c.ambientColorR, c.ambientColorG, c.ambientColorB) * scale;
    }

    public static float3 Ambient => AmbientAt(Hours);

    // =====================================================================
    // LOOK 1 (the Brickmap look) -- LightingConfig.look. Every value below is
    // DISPLAY-space, like the engine it is taken from; the shader converts to
    // linear once, at the end. The day factor is the Brickmap's own
    // `saturate(sunDir.y * 4)`: full day from ~14.5 degrees up, zero at the
    // horizon, so sky, sun and ambient all fade together through dusk.
    // =====================================================================

    public static float LookDayFactorAt(float hours) => math.saturate(DirectionAt(hours).y * 4f);

    private static float3 V(float r, float g, float b) => new float3(r, g, b);

    /// >= 0 forces LightingConfig.lookGrade without touching the file (the
    /// -looksweep before/after pairs). -1 = use the config.
    public static int LookGradeOverride = -1;

    /// How much of the CINEMATIC GRADE applies at this time of day: 0 at and
    /// below gradeFromDeg of sun elevation, 1 at gradeToDeg, smoothstep between.
    /// 0 whenever lookGrade is 0. Because it is 0 below gradeFromDeg, golden
    /// hour, dawn, dusk and night are identical with the grade on or off --
    /// see LightingConfig's CINEMATIC GRADE block.
    public static float LookGradeAmountAt(float hours)
    {
        var c = LightingConfig.Active;
        int grade = LookGradeOverride >= 0 ? LookGradeOverride : c.lookGrade;
        if (grade == 0) return 0f;
        return math.smoothstep(c.gradeFromDeg, c.gradeToDeg, ElevationDegreesAt(hours));
    }

    /// The DAY sky colours, blended toward the grade's deeper sky by the grade.
    private static float3 DayZenith(float hours)
    {
        var c = LightingConfig.Active;
        return math.lerp(V(c.skyZenithR, c.skyZenithG, c.skyZenithB),
                         V(c.cineZenithR, c.cineZenithG, c.cineZenithB), LookGradeAmountAt(hours));
    }

    private static float3 DayHorizon(float hours)
    {
        var c = LightingConfig.Active;
        return math.lerp(V(c.skyHorizonR, c.skyHorizonG, c.skyHorizonB),
                         V(c.cineHorizonR, c.cineHorizonG, c.cineHorizonB), LookGradeAmountAt(hours));
    }

    public static float3 LookSkyZenithAt(float hours)
    {
        var c = LightingConfig.Active;
        return math.lerp(V(c.nightZenithR, c.nightZenithG, c.nightZenithB),
                         DayZenith(hours), LookDayFactorAt(hours));
    }

    public static float3 LookSkyHorizonAt(float hours)
    {
        var c = LightingConfig.Active;
        return math.lerp(V(c.nightHorizonR, c.nightHorizonG, c.nightHorizonB),
                         DayHorizon(hours), LookDayFactorAt(hours));
    }

    /// The grade's shader parameters: x = exposure on sun + ambient, y =
    /// saturation, z = highlight-shoulder knee (1 = none), w = grade amount.
    /// All three are exactly neutral (1, 1, 1) when the amount is 0.
    public static float4 LookGradeAt(float hours)
    {
        var c = LightingConfig.Active;
        float k = LookGradeAmountAt(hours);
        return new float4(math.lerp(1f, c.gradeNoonExposure, k),
                          math.lerp(1f, c.gradeSaturation, k),
                          math.lerp(1f, c.gradeShoulderKnee, k),
                          k);
    }

    /// The sun's colour in the sky (the halo), fading to black at night and
    /// warming toward `golden` as it nears the horizon -- 0 warmth at 25
    /// degrees and above, full at 2 degrees (the comparison Playground's curve).
    public static float3 LookSunHaloAt(float hours)
    {
        var c = LightingConfig.Active;
        float el = ElevationDegreesAt(hours);
        float warmth = math.saturate(1f - (el - 2f) / 23f);
        float3 high = math.lerp(V(c.sunHaloR, c.sunHaloG, c.sunHaloB), V(c.cineSunR, c.cineSunG, c.cineSunB),
                                LookGradeAmountAt(hours));
        float3 halo = math.lerp(high, V(c.goldenR, c.goldenG, c.goldenB), warmth);
        return halo * LookDayFactorAt(hours);
    }

    /// Direct sun term: the halo times `lookSunScale` (the Brickmap's 1.5).
    public static float3 LookSunAt(float hours) => LookSunHaloAt(hours) * LightingConfig.Active.lookSunScale;

    public static float3 LookAmbientAt(float hours)
    {
        var c = LightingConfig.Active;
        float d = LookDayFactorAt(hours);
        float3 day = DayHorizon(hours) * c.lookAmbientStrength;
        float3 night = V(c.nightHorizonR, c.nightHorizonG, c.nightHorizonB) * c.lookNightAmbient;
        return math.lerp(night, day, d);
    }

    public static float3 LookMoonAt(float hours)
    {
        var c = LightingConfig.Active;
        return V(c.moonR, c.moonG, c.moonB) * (1f - LookDayFactorAt(hours));
    }

    /// A short human label for the HUD and for report lines. Derived from
    /// elevation so it cannot drift from what is on screen.
    public static string PhaseNameAt(float hours)
    {
        float el = ElevationDegreesAt(hours);
        bool morning = hours >= 0f && hours < 12f;
        if (el <= -6f) return "night";
        if (el <= 0f) return morning ? "dawn twilight" : "dusk twilight";
        if (el <= GoldenHourElevationDegrees + 2f) return morning ? "sunrise" : "golden hour";
        if (el <= LightingConfig.Active.sunRampTopDegrees) return morning ? "morning" : "afternoon";
        return "midday";
    }

    public static string PhaseName => PhaseNameAt(Hours);

    /// Formats the clock as HH:MM, for the HUD.
    public static string ClockString()
    {
        int h = (int)Hours;
        int m = (int)((Hours - h) * 60f);
        return $"{h:00}:{m:00}";
    }
}
