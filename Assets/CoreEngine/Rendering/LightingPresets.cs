// ==========================================
// Assets/CoreEngine/Rendering/LightingPresets.cs
//
// PHASE 7 CONTINUATION, STAGE C. Four named, coherent lighting directions,
// offered as OPTIONS rather than converged into one answer.
//
// WHY OPTIONS AND NOT A DECISION. Every art constant in this project has been
// the owner's call -- k_soft, the C.11 falloff, the shadow rung, the density
// and drag tables PHASE_6 §2.3 added. PHASE_7_COMPLETION §10 flag 5 says the
// same about these. One unattended night is not enough to know a single guess
// is right, and picking one would quietly convert a judgement into a fact.
// So: four directions, each internally coherent, each screenshotted at the same
// poses, with a stated read on which leans closest to the brief -- and the
// choice left open.
//
// ---------------------------------------------------------------------------
// THE PART THAT IS *NOT* A STYLE CHOICE: SNOW CLIPS TO PURE WHITE AT NOON
// ---------------------------------------------------------------------------
// The owner reported the snow biome as "blindingly bright". It is, and it is
// arithmetic rather than taste. For an up-facing snow voxel at noon the shader
// computes (Raymarch.compute's combine, and MaterialPalette's snow entry):
//
//     out = snowAlbedo * (zenithColour * sunIntensity * N.L
//                         + ambientColour * ambientIntensity) * hemiUp
//
// with snowAlbedo.b = 0.760, zenith.b = 0.92, hemiUp = 1.12 and N.L ~ 0.97 for
// a flat up-face. At the SHIPPED values (sunIntensity 1.25, ambientIntensity
// 0.30) that is:
//
//     0.760 * (0.92*1.25*0.97 + 0.78*0.30) * 1.12  =  1.15     -> CLIPPED
//
// All three channels exceed 1.0, so the surface saturates to flat white and the
// terrain shape disappears into it. That is the reported symptom exactly.
//
// AND -- THIS IS THE BIT WORTH READING -- LOWERING AMBIENT ALONE DOES NOT FIX
// IT. Dropping ambientIntensity 0.30 -> 0.10 still leaves:
//
//     0.760 * (0.92*1.25*0.97 + 0.78*0.10) * 1.12  =  1.05     -> STILL CLIPPED
//
// because the SUN term alone is already at the ceiling. §8 of
// PHASE_7_COMPLETION diagnosed golden hour as "ambient is 1.5-5x the sun" and
// the natural reading of that is "turn ambient down". For golden hour that is
// right. For snow at NOON it is insufficient, and a preset that only did that
// would have left the owner's actual complaint in place while appearing to
// address it. Stage C's brief asked for exactly this distinction to be checked
// rather than assumed, so it is derived here and every preset below is bounded
// by it.
//
// THE CONSTRAINT, solved for sunIntensity S at a given ambientIntensity A:
//
//     0.760 * (0.8924*S + 0.78*A) * 1.12 <= 1.0
//     =>  0.8924*S + 0.78*A <= 1.1747
//     =>  S <= (1.1747 - 0.78*A) / 0.8924
//
// Every preset below states its own ceiling and sits under it. The shipped
// config sits 15% OVER it, which is the defect.
//
// NOTE ON THE SNOW PALETTE ITSELF: MORNING_REPORT.md item 1 already lowered
// snow 0.86 -> 0.72 for precisely this class of problem ("the hemisphere term
// pushed up-facing snow to clipping ... the whole mid-ground read as a flat
// white sheet"). That fix was correct and is not undone here -- 0.715/0.735/
// 0.760 is left exactly as authored. What changed since is that Phase 7 added a
// real sun term on top of the hemisphere term, which re-opened the same
// headroom problem from the other side. The fix therefore belongs in the
// LIGHTING constants, not in the palette, and lowering snow again would be
// treating the symptom in the wrong file.
// ==========================================

using System;
using System.IO;
using UnityEngine;

namespace VoxelEngine.Rendering
{
    public static class LightingPresets
    {
        /// The ceiling derived above: the largest sunIntensity that keeps an
        /// up-facing snow voxel at noon inside [0,1] for a given ambient.
        /// Exposed so a test can assert each preset respects it rather than the
        /// arithmetic living only in a comment.
        public static float MaxSunIntensityForSnow(float ambientIntensity) =>
            (1.1747f - 0.78f * ambientIntensity) / 0.8924f;

        /// The brightest value an up-facing snow voxel reaches at noon under a
        /// given config. > 1.0 means it clips to flat white.
        public static float SnowNoonPeak(LightingConfig c) =>
            0.760f * (c.sunZenithB * c.sunIntensity * 0.97f
                      + c.ambientColorB * c.ambientIntensity) * 1.12f;

        public struct Preset
        {
            public string Name;
            public string Note;          // what direction this is, in one line
            public Func<LightingConfig> Build;
        }

        // =================================================================
        // 1. WARM -- sun-dominant golden hour. The direct answer to §8.
        // =================================================================
        // §8 measured ambient at 1.5-5x the sun at 6 degrees elevation, which is
        // why golden hour read cool and flat. This preset inverts that ratio:
        // ambient drops to a third of shipped and the horizon colour is pushed
        // deeper, so a low sun actually tints the world it is lighting.
        // Ceiling at A=0.11 is S <= 1.220; this sits at 1.15.
        public static LightingConfig Warm() => new LightingConfig
        {
            kSoft = 0.035f,
            pointFalloffK = 1.10f,
            lightRangeM = 7f,
            lightIntensity = 0.75f,
            ambientIntensity = 0.11f,
            ambientColorR = 0.58f, ambientColorG = 0.60f, ambientColorB = 0.68f,
            nightAmbientFraction = 0.18f,
            undergroundAmbientFloor = 0.045f,
            sunHorizonR = 1.00f, sunHorizonG = 0.42f, sunHorizonB = 0.14f,
            sunZenithR = 1.00f, sunZenithG = 0.94f, sunZenithB = 0.84f,
            sunRampTopDegrees = 26f,
            sunIntensity = 1.15f,
            dayLengthSeconds = 180f,
            sunArcTilt = 0.25f,
        };

        // =================================================================
        // 2. ATMOSPHERIC -- the closest lean to the stated brief.
        // =================================================================
        // "Soft, atmospheric, painterly, warm/cool balance." The three levers
        // that actually produce that here, as opposed to just being darker:
        //   - k_soft 0.09, three times shipped: penumbrae wide enough to SEE,
        //     which is what makes voxel edges read as painted rather than cut;
        //   - ambient kept comparatively high but pushed lilac-cool, so shadows
        //     go blue-violet while lit faces go warm -- that split IS the
        //     warm/cool balance, and it is lost if ambient is merely turned down;
        //   - a wide sun ramp (30 deg) so the warm band lasts, plus a long day,
        //     so the transition is something you watch rather than catch.
        // Torch is dimmest and shortest-range here; a soft look is spoiled by a
        // hot point light more than by anything else.
        // Ceiling at A=0.17 is S <= 1.168; this sits at 1.02.
        public static LightingConfig Atmospheric() => new LightingConfig
        {
            kSoft = 0.090f,
            pointFalloffK = 1.35f,
            lightRangeM = 6.5f,
            lightIntensity = 0.62f,
            ambientIntensity = 0.17f,
            ambientColorR = 0.50f, ambientColorG = 0.55f, ambientColorB = 0.74f,
            nightAmbientFraction = 0.22f,
            undergroundAmbientFloor = 0.05f,
            sunHorizonR = 0.98f, sunHorizonG = 0.52f, sunHorizonB = 0.30f,
            sunZenithR = 1.00f, sunZenithG = 0.95f, sunZenithB = 0.88f,
            sunRampTopDegrees = 30f,
            sunIntensity = 1.02f,
            dayLengthSeconds = 300f,
            sunArcTilt = 0.30f,
        };

        // =================================================================
        // 3. HIGH CONTRAST -- dramatic, near-hard, deep shadow.
        // =================================================================
        // The opposite direction, included deliberately so the comparison has a
        // pole rather than three variations on soft. Near-hard shadows
        // (k_soft 0.012), the lowest ambient of the four, and a near-black
        // underground floor. This is the preset that makes the shadow RAY
        // visible as a feature -- which is worth seeing at least once before
        // choosing, given §6 measured that ray as the most expensive thing in
        // the frame.
        // Ceiling at A=0.055 is S <= 1.268; this sits at 1.20.
        public static LightingConfig HighContrast() => new LightingConfig
        {
            kSoft = 0.012f,
            pointFalloffK = 0.85f,
            lightRangeM = 8.5f,
            lightIntensity = 0.90f,
            ambientIntensity = 0.055f,
            ambientColorR = 0.44f, ambientColorG = 0.52f, ambientColorB = 0.70f,
            nightAmbientFraction = 0.10f,
            undergroundAmbientFloor = 0.02f,
            sunHorizonR = 1.00f, sunHorizonG = 0.38f, sunHorizonB = 0.10f,
            sunZenithR = 1.00f, sunZenithG = 0.98f, sunZenithB = 0.95f,
            sunRampTopDegrees = 18f,
            sunIntensity = 1.20f,
            dayLengthSeconds = 120f,
            sunArcTilt = 0.22f,
        };

        // =================================================================
        // 4. MUTED -- low-saturation, even, documentary.
        // =================================================================
        // Deliberately the least dramatic: the lowest sun, a desaturated ramp,
        // and enough ambient that nothing is ever hard to read. Included as the
        // safe option -- if the other three all read as "too much" in the
        // morning, this is the floor to start from rather than going back to
        // the shipped values, which clip.
        // Ceiling at A=0.15 is S <= 1.185; this sits at 0.95.
        public static LightingConfig Muted() => new LightingConfig
        {
            kSoft = 0.050f,
            pointFalloffK = 1.00f,
            lightRangeM = 7f,
            lightIntensity = 0.70f,
            ambientIntensity = 0.15f,
            ambientColorR = 0.56f, ambientColorG = 0.60f, ambientColorB = 0.68f,
            nightAmbientFraction = 0.20f,
            undergroundAmbientFloor = 0.06f,
            sunHorizonR = 0.95f, sunHorizonG = 0.58f, sunHorizonB = 0.38f,
            sunZenithR = 0.98f, sunZenithG = 0.96f, sunZenithB = 0.93f,
            sunRampTopDegrees = 24f,
            sunIntensity = 0.95f,
            dayLengthSeconds = 240f,
            sunArcTilt = 0.25f,
        };

        /// The shipped values, kept as a preset so the owner can A/B back to
        /// exactly what they saw tonight without hunting for the numbers.
        /// IT CLIPS SNOW AT NOON -- that is the point of keeping it available.
        public static LightingConfig Shipped() => new LightingConfig();

        public static readonly Preset[] All =
        {
            new Preset { Name = "Warm",         Note = "sun-dominant golden hour; the direct answer to §8", Build = Warm },
            new Preset { Name = "Atmospheric",  Note = "soft, wide penumbra, warm/cool split -- closest to the brief", Build = Atmospheric },
            new Preset { Name = "HighContrast", Note = "near-hard shadows, deep ambient, dramatic", Build = HighContrast },
            new Preset { Name = "Muted",        Note = "low-saturation, even, the safe floor", Build = Muted },
            new Preset { Name = "Shipped",      Note = "tonight's values, for A/B -- CLIPS SNOW AT NOON", Build = Shipped },
        };

        /// Writes every preset beside LightingConfig.json so the owner can read
        /// and edit them as ordinary files. Non-destructive: an existing preset
        /// file is left alone, so hand-edits survive a restart.
        public static void WritePresetFilesIfAbsent()
        {
            foreach (Preset p in All)
            {
                try
                {
                    string path = PathFor(p.Name);
                    if (File.Exists(path)) continue;
                    File.WriteAllText(path, p.Build().ToJson());
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[LightingPresets] could not write {p.Name}: {e.Message}");
                }
            }
        }

        public static string PathFor(string name) =>
            Path.Combine(Application.persistentDataPath, $"LightingConfig_{name}.json");

        /// Applies a preset by WRITING IT OVER LightingConfig.json, which the
        /// existing §8.1 poll then picks up on the next frame.
        ///
        /// DELIBERATELY GOES THROUGH THE FILE rather than calling
        /// LightingConfig.DebugSetActive directly. Two reasons, both practical:
        /// the owner ends up with the chosen preset already sitting in the file
        /// they were told to edit, so tweaking from there needs no extra step;
        /// and it exercises the same hot-reload path the owner will use, so a
        /// preset that switches correctly is evidence the reload works.
        /// If the file cannot be written, it falls back to an in-memory swap so
        /// a read-only disk degrades to "works but does not persist" rather than
        /// to "the key does nothing".
        public static string Apply(int index)
        {
            index = Mathf.Clamp(index, 0, All.Length - 1);
            Preset p = All[index];
            LightingConfig cfg = p.Build();
            try
            {
                if (!string.IsNullOrEmpty(LightingConfig.ActivePath))
                {
                    File.WriteAllText(LightingConfig.ActivePath, cfg.ToJson());
                    LightingConfig.ReloadIfChanged();
                    return $"{p.Name} — {p.Note}";
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[LightingPresets] write failed, applying in memory: {e.Message}");
            }
            LightingConfig.DebugSetActive(cfg);
            return $"{p.Name} (in memory) — {p.Note}";
        }
    }
}
