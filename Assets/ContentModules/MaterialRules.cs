// ==========================================
// Assets/ContentModules/MaterialRules.cs
//
// Phase 5a. The simulation half of the material registry: the per-material
// FLAGS and TICK INTERVAL that Appendix A.7's MaterialData carries to the GPU,
// plus §7.6's reaction pair table.
//
// WHY THIS IS A SEPARATE FILE FROM Content.cs
// Content.cs is the world-GENERATION registry snapshot (§5.5) and its
// ContentVersionHash() is load-bearing for world.meta staleness. This table
// describes how a material BEHAVES once it exists, which generation never
// reads. Keeping them in separate files keeps that hash's input obvious.
//
// STILL ONE PLACE (§0.1 invariant 8): every consumer -- FluidReferenceCPU
// today, the A.7 registry upload and FluidCA.compute in 5b -- reads the
// behaviour of a material from here and nowhere else. No caller may inline
// "material == Materials.Water" to mean "this is a fluid".
//
// LAYOUT CONTRACT (Appendix B): the flag bit positions below are the ones
// MaterialData.flags ships to the GPU. They are fixed by the spec, not by
// this file's convenience -- do not reorder them.
//
// THE ONE CONTENT DECISION MADE HERE, FLAGGED EXPLICITLY:
// Sand is marked IsFallingSolid. Sand is also the Desert biome's surface
// stratum (Content.cs Biomes.Table), so once Phase 5b runs the CA over shipped
// terrain, desert surfaces become collapsible. That is the intended v1
// behaviour per §7.5, but it is a GAMEPLAY change and it is not exercised by
// anything today: Phase 5a runs on a private sandbox array, and no shipped
// code path reads these flags yet. Named here so it is a decision on the
// record rather than a surprise in 5b.

public static class MaterialRules
{
    // ---- MaterialData.flags bit positions (Appendix B, fixed by spec) ----
    public const uint IsFluid        = 1u << 0;
    public const uint IsFallingSolid = 1u << 1;
    public const uint IsFlammable    = 1u << 2;
    public const uint DamagesPlayer  = 1u << 3;
    public const uint IsEmissive     = 1u << 4;
    public const uint Dissolves      = 1u << 5;
    /// ADDITIVE (2026-09-25), past Appendix B's fixed bits 0-5: a fluid whose
    /// Intent hierarchy is mirrored UPWARD (steam). Bit 6 was unused.
    public const uint IsGas          = 1u << 6;

    /// A material that participates in the CA at all -- fluid or falling solid.
    /// §7.5: falling solids run the identical pipeline with a shorter Intent
    /// hierarchy, so "does this get a slot?" is exactly this test.
    public const uint IsMobileMask = IsFluid | IsFallingSolid;

    // 256-entry tables, matching A.7's 256-entry registry buffer exactly.
    private static readonly uint[] _flags = new uint[256];
    private static readonly uint[] _tickInterval = new uint[256];

    /// §7.6 reaction pair. `self` and `other` must be adjacent (face-adjacent
    /// in the Intent neighbour scan); the cell holding `self` becomes
    /// `selfProduct` and the cell holding `other` becomes `otherProduct`.
    /// Both entries of a pair are stored, so the lookup is order-independent.
    public struct Reaction
    {
        public byte self;
        public byte other;
        public byte selfProduct;
        public byte otherProduct;
    }

    private static readonly Reaction[] _reactions;

    static MaterialRules()
    {
        // ---- Viscosity (§7.4): "Water every tick, Lava every 6, Honey every 30" ----
        // A tick interval of 0 is meaningless; every mobile material gets >= 1.
        Define(Materials.Water, IsFluid, 1);
        Define(Materials.Lava,  IsFluid | IsEmissive | DamagesPlayer, 6);
        Define(Materials.Honey, IsFluid, 30);

        // §7.5 falling solid. Interval 1: repose angle comes from the Intent
        // hierarchy (down + down-diagonals only), not from ticking slowly.
        Define(Materials.Sand, IsFallingSolid, 1);

        // §6.6's placeable emissive. A static solid -- NOT mobile, so it gets
        // no fluid slot and never runs the CA -- carrying Appendix B's bit 4.
        // Tick interval 0 for the same reason Obsidian's is 0: it never ticks.
        Define(Materials.Torch, IsEmissive, 0);
        // Stage G's coloured torches: identical flags and hardness to the warm
        // torch by design. They differ ONLY in tint, so a measurement that
        // separates them is measuring colour and not some other property.
        Define(Materials.TorchBlue,   IsEmissive, 0);
        Define(Materials.TorchGreen,  IsEmissive, 0);
        Define(Materials.TorchPurple, IsEmissive, 0);

        // Obsidian is the Water+Lava product: an ordinary static solid, and
        // specifically NOT mobile -- that is what makes a reaction show up in
        // the conservation ledger as a permanent removal rather than a loss.
        Define(Materials.Obsidian, 0u, 0);

        // ---- §2.7 fluids (2026-09-25) ----
        // Oil: slightly viscous, flammable (fire is a later system), LIGHTER
        // than water, so it layers on top via the CA's density swap.
        Define(Materials.Oil,       IsFluid | IsFlammable, 2);
        // Acid: runny, harmful, dissolves anything flagged Dissolves (below)
        // and is consumed doing it -- one acid voxel eats one rock voxel.
        Define(Materials.Acid,      IsFluid | DamagesPlayer, 1);
        // Toxic sludge: thick and emissive. Emissive + mobile => it lights
        // through LightSourceBuffer's tile AGGREGATE, never per voxel (CLAUDE.md).
        Define(Materials.Sludge,    IsFluid | IsEmissive | DamagesPlayer, 12);
        // Quicksand: the slow viscosity tier (honey is 30). Dense and draggy.
        Define(Materials.Quicksand, IsFluid, 60);
        // Steam: a gas -- rises, spreads under ceilings, liquids fall through it.
        Define(Materials.Steam,     IsFluid | IsGas, 1);

        // What acid eats. Obsidian is deliberately acid-proof (it is the
        // container a player can make), as are torches.
        foreach (byte m in new[] { Materials.Stone, Materials.Deepstone, Materials.MossyStone,
                                   Materials.Sandstone, Materials.Grass, Materials.JungleGrass,
                                   Materials.Snow })
            _flags[m] |= Dissolves;
        _flags[Materials.Sand] |= Dissolves;   // keeps IsFallingSolid

        // ---- §7.6 reactions ----
        // Water + Lava -> Obsidian. The LAVA cell becomes the obsidian (the
        // solid is where the melt was); the WATER cell is consumed to Air
        // (it flashed off). Both fluid slots free -- the one that ran the scan
        // frees immediately, its counterpart self-frees on the next tick's
        // orphan check (§7.3), because its home byte no longer matches its
        // cached material. That asymmetry is the spec's mechanism, not a bug.
        var reactions = new System.Collections.Generic.List<Reaction>
        {
            new Reaction { self = Materials.Water, other = Materials.Lava,
                           selfProduct = Materials.Air, otherProduct = Materials.Obsidian },
            new Reaction { self = Materials.Lava,  other = Materials.Water,
                           selfProduct = Materials.Obsidian, otherProduct = Materials.Air },
        };
        // Acid + anything Dissolves -> Air + Air. Both directions, so the pair
        // resolves whichever side scans first (the rock never has a slot, so in
        // practice the acid always does).
        for (int m = 0; m < 256; m++)
        {
            if ((_flags[m] & Dissolves) == 0u) continue;
            reactions.Add(new Reaction { self = Materials.Acid, other = (byte)m,
                                         selfProduct = Materials.Air, otherProduct = Materials.Air });
            reactions.Add(new Reaction { self = (byte)m, other = Materials.Acid,
                                         selfProduct = Materials.Air, otherProduct = Materials.Air });
        }
        _reactions = reactions.ToArray();
    }

    private static void Define(byte material, uint flags, uint tickInterval)
    {
        _flags[material] = flags;
        _tickInterval[material] = tickInterval;
    }

    public static uint Flags(byte material) => _flags[material];

    // =====================================================================
    // §8.6 / Appendix C.6 -- DENSITY AND DRAG
    //
    // A.7's MaterialData declares `density` and `viscosityDrag` fields, and
    // §8.6 says buoyancy takes them "from the Registry" -- but NOTHING IN THE
    // PROJECT EVER POPULATED THEM. The struct existed; the data did not. This
    // table is that data, added additively alongside the flags and tick-interval
    // tables it sits beside, in the same Define-style shape.
    //
    // VALUES ARE CONTENT, NOT ARCHITECTURE. They are seeded from real-world
    // figures (kg/m^3) so the relative ordering is defensible on day one --
    // honey sinks in water, lava is denser than both, a player floats in water
    // and sinks in air. They are exactly the kind of number §8.1 says feel is
    // found by iterating, so treat them as a starting point to tune, not as
    // settled physics.
    //
    // Air is given a density so the buoyancy formula needs no special case:
    // (rho_fluid - rho_entity) with rho_fluid = air just produces a negligible
    // force, which is correct.
    // =====================================================================

    private static readonly float[] _densityKgM3 = BuildDensities();
    private static readonly float[] _viscosityDrag = BuildDrag();

    private static float[] BuildDensities()
    {
        var d = new float[256];
        for (int i = 0; i < 256; i++) d[i] = 2200f;      // generic rock default
        d[Materials.Air] = 1.2f;
        d[Materials.Water] = 1000f;
        d[Materials.Lava] = 3100f;
        d[Materials.Honey] = 1420f;
        d[Materials.Sand] = 1600f;
        d[Materials.Snow] = 300f;
        d[Materials.Stone] = 2600f;
        d[Materials.Deepstone] = 2900f;
        d[Materials.Obsidian] = 2650f;
        d[Materials.Sandstone] = 2300f;
        d[Materials.Grass] = 1300f;
        d[Materials.JungleGrass] = 1300f;
        d[Materials.MossyStone] = 2500f;
        d[Materials.Oil] = 850f;          // floats on water
        d[Materials.Acid] = 1100f;        // sinks under water, just
        d[Materials.Sludge] = 1300f;
        d[Materials.Quicksand] = 1700f;
        d[Materials.Steam] = 0.6f;        // lighter than air: rises
        return d;
    }

    private static float[] BuildDrag()
    {
        var v = new float[256];
        for (int i = 0; i < 256; i++) v[i] = 0f;         // solids apply no drag; they block
        v[Materials.Air] = 0.02f;
        v[Materials.Water] = 3.0f;
        v[Materials.Lava] = 12.0f;
        v[Materials.Honey] = 30.0f;                      // §7.4 already makes honey the slow one
        v[Materials.Oil] = 5.0f;
        v[Materials.Acid] = 3.5f;
        v[Materials.Sludge] = 18.0f;
        v[Materials.Quicksand] = 80.0f;                  // it holds you
        v[Materials.Steam] = 0.5f;
        return v;
    }

    // =====================================================================
    // §6.6 / A.7 -- EMISSIVE, AND THE LIGHT A MATERIAL DEFINES
    //
    // THE SAME GAP PHASE_6_COMPLETION.md §2.3 FOUND, ONE FIELD OVER. That note
    // records that A.7's `density` and `viscosityDrag` were declared by the
    // struct and never populated by anything -- "the struct existed; the
    // numbers did not" -- and adds the two tables above. `emissive` is the
    // third field of that kind and it was in exactly the same state: declared
    // in StructHeaders.cs and StructHeaders.hlsl, referenced by nothing,
    // no value ever written. §6.6 and C.9 both depend on it.
    //
    // STATED PRECISELY, BECAUSE "THE REGISTRY IS UNPOPULATED" UNDERSTATES IT:
    // there is no GraphicsBuffer<MaterialData> anywhere in this project at all.
    // FluidGpuSimulation.UploadMaterialRegistry() uploads four separate flat
    // uint arrays -- deliberately, per its own comment, "restricted to what the
    // CA reads" -- not the A.7 struct. So this table is the A.7 emissive data
    // existing for the first time, and the shader reads it the way the shader
    // already reads the palette: mirrored, with a parse-the-shader sync test.
    // MaterialPalette's own header explains why that route was chosen over
    // wiring a registry buffer through RaymarchFeature's render-graph pass, and
    // PaletteSyncTests is the guard. Building the real A.7 buffer is carried
    // forward, not done here: it is a §3.9 sync-contract addition that wants
    // its own checkpoint, and Phase 7 already has one new untrusted thing
    // (§0.1 invariant 5).
    //
    // VALUES ARE CONTENT, NOT ARCHITECTURE -- the same disclaimer the density
    // table carries, and it means it. Strengths and tints below are placeholders
    // awaiting the owner's eye, tunable live through LightingConfig.json's
    // global multipliers without touching this file.
    // =====================================================================

    private static readonly float[] _emissive = BuildEmissive();
    private static readonly float[] _emissiveTint = BuildEmissiveTints();   // 3 floats per material
    private static readonly float[] _lightRangeScale = BuildLightRangeScales();

    private static float[] BuildEmissive()
    {
        var e = new float[256];                          // 0 = not emissive
        // A torch is the §13 Phase 7 scene's placeable light. Full strength.
        e[Materials.Torch] = 1.0f;
        // Lava already carries the IsEmissive flag (set in the static ctor
        // above, Appendix B bit 4) and already reads hot through the shader's
        // self-lit shading cheat. Giving it a real emissive value is what turns
        // that cheat into an actual light source per §6.6 -- but at a lower
        // strength than a torch, because a lava lake is large and 1.0 across
        // hundreds of voxels would wash out a cave that §6.5 requires be dark.
        e[Materials.Lava] = 0.55f;
        // Stage G: same strength as the warm torch. A dimmer coloured torch
        // would confound "does the colour read" with "is it bright enough".
        e[Materials.TorchBlue]   = 1.0f;
        e[Materials.TorchGreen]  = 1.0f;
        e[Materials.TorchPurple] = 1.0f;
        // Sludge: a dim toxic glow. Lower than lava: a sludge pool is a
        // navigation cue in a dark cavern, not a light source to read by.
        e[Materials.Sludge] = 0.35f;
        return e;
    }

    private static float[] BuildEmissiveTints()
    {
        var t = new float[256 * 3];
        for (int i = 0; i < 256; i++) { t[i * 3] = 1f; t[i * 3 + 1] = 1f; t[i * 3 + 2] = 1f; }
        // Warm candle-flame, not white: a neutral torch reads as a flashlight.
        Tint(t, Materials.Torch, 1.00f, 0.72f, 0.38f);
        // Hotter and redder than the torch, matching the palette's deep hot red.
        Tint(t, Materials.Lava,  1.00f, 0.42f, 0.14f);
        // Stage G. Each keeps ONE channel near 1 and lifts the others off zero:
        // a fully saturated primary (0,0,1) lights a grey wall to pure blue and
        // loses all surface detail, because albedo * light zeroes two channels.
        // These read as coloured without flattening what they fall on.
        Tint(t, Materials.TorchBlue,   0.36f, 0.58f, 1.00f);   // cool arcane blue
        Tint(t, Materials.TorchGreen,  0.42f, 1.00f, 0.48f);   // green flame
        Tint(t, Materials.TorchPurple, 0.78f, 0.42f, 1.00f);   // violet
        Tint(t, Materials.Sludge,      0.55f, 1.00f, 0.30f);   // toxic green
        return t;
    }

    private static void Tint(float[] t, byte m, float r, float g, float b)
    { t[m * 3] = r; t[m * 3 + 1] = g; t[m * 3 + 2] = b; }

    private static float[] BuildLightRangeScales()
    {
        var s = new float[256];
        for (int i = 0; i < 256; i++) s[i] = 1f;
        // Per-material multiplier on LightingConfig's base light range, so the
        // one tunable number in the JSON still moves every light together while
        // materials keep their relative reach.
        s[Materials.Torch] = 1.0f;
        s[Materials.Lava]  = 0.6f;
        s[Materials.TorchBlue]   = 1.0f;
        s[Materials.TorchGreen]  = 1.0f;
        s[Materials.TorchPurple] = 1.0f;
        s[Materials.Sludge] = 0.7f;
        return s;
    }

    /// A.7's `emissive` field: how strongly this material emits, 0..1.
    /// 0 for everything that is not an emissive material.
    public static float Emissive(byte material) => _emissive[material];

    /// True if this material is a LIGHT SOURCE -- i.e. §6.6's "emissives
    /// additionally enqueue into the light system when placed" applies to it.
    ///
    /// Deliberately derived from the emissive STRENGTH being non-zero AND the
    /// Appendix B flag being set, so a material cannot become a light by
    /// accident of one table being edited without the other. A mismatch is a
    /// test failure (MaterialEmissiveTests), not a silent behaviour change.
    public static bool IsEmissiveMaterial(byte material) =>
        (_flags[material] & IsEmissive) != 0u && _emissive[material] > 0f;

    /// The light a material defines, for §3.8's buffer. Colour is the material's
    /// tint scaled by its emissive strength; `rangeScale` multiplies
    /// LightingConfig's base range. Returns false for anything that is not a
    /// light, so a caller cannot enqueue a dark voxel.
    public static bool TryGetLight(byte material, out float r, out float g, out float b,
                                   out float rangeScale)
    {
        if (!IsEmissiveMaterial(material))
        {
            r = g = b = 0f; rangeScale = 0f;
            return false;
        }
        float e = _emissive[material];
        r = _emissiveTint[material * 3] * e;
        g = _emissiveTint[material * 3 + 1] * e;
        b = _emissiveTint[material * 3 + 2] * e;
        rangeScale = _lightRangeScale[material];
        return true;
    }

    /// Density in kg/m^3. Appendix C.6's rho.
    public static float Density(byte material) => _densityKgM3[material];

    /// Linear drag coefficient applied to a body moving through this material,
    /// per second. 0 for solids, which stop a body rather than slowing it.
    public static float ViscosityDrag(byte material) => _viscosityDrag[material];

    /// True if `material` gets a fluid slot and runs the CA (§7.3).
    public static bool IsMobile(byte material) => (_flags[material] & IsMobileMask) != 0u;

    public static bool IsFluidMaterial(byte material) => (_flags[material] & IsFluid) != 0u;

    public static bool IsFallingSolidMaterial(byte material) => (_flags[material] & IsFallingSolid) != 0u;

    /// A fluid that rises (steam): its Intent hierarchy is mirrored upward.
    public static bool IsGasMaterial(byte material) => (_flags[material] & IsGas) != 0u;

    /// §2.7 density layering: may `heavy`, sitting directly above `light`,
    /// trade places with it? Both must be FLUIDS (a falling solid never swaps:
    /// sand has an angle of repose, it sinks through nothing), must not react
    /// with each other (the reaction wins), and `heavy` must rank denser.
    /// Ranks, not raw densities, so the CPU oracle and FluidCA.compute (which
    /// receives only the ranks) can never disagree at a margin.
    public static bool SinksThrough(byte heavy, byte light)
    {
        if (!IsFluidMaterial(heavy) || !IsFluidMaterial(light) || heavy == light) return false;
        if (TryGetReaction(heavy, light, out _, out _)) return false;
        return DensityRank(heavy) > DensityRank(light);
    }

    /// 1..n rank of a FLUID by density (0 for non-fluids); a rank step always
    /// means more than 5% denser, so near-equal fluids share a rank and never
    /// churn. Uploaded to the GPU with the registry.
    /// LAZY, deliberately: static field initializers run BEFORE the static
    /// constructor body that populates _flags, so an initializer here would
    /// rank against empty flags.
    public static uint DensityRank(byte material)
    {
        if (_densityRank == null) _densityRank = BuildDensityRank();
        return _densityRank[material];
    }
    private static uint[] _densityRank;
    private static uint[] BuildDensityRank()
    {
        var r = new uint[256];
        var fluids = new System.Collections.Generic.List<byte>();
        for (int m = 0; m < 256; m++) if (IsFluidMaterial((byte)m)) fluids.Add((byte)m);
        fluids.Sort((a, b) => _densityKgM3[a].CompareTo(_densityKgM3[b]));
        uint rank = 0; float last = -1f;
        foreach (byte m in fluids)
        {
            if (last < 0f || _densityKgM3[m] > last * 1.05f) { rank++; last = _densityKgM3[m]; }
            r[m] = rank;
        }
        return r;
    }

    /// §7.4 viscosity tick interval. 1 = acts every tick. Never returns 0 for a
    /// mobile material, so a caller can use it as a divisor/counter bound
    /// without a special case.
    public static uint TickInterval(byte material)
    {
        uint t = _tickInterval[material];
        return t == 0u ? 1u : t;
    }

    /// §7.6: does `self` react on contact with `other`? Order-independent --
    /// both directions are in the table, so whichever slot runs its neighbour
    /// scan first produces the same two products.
    public static bool TryGetReaction(byte self, byte other, out byte selfProduct, out byte otherProduct)
    {
        for (int i = 0; i < _reactions.Length; i++)
        {
            if (_reactions[i].self == self && _reactions[i].other == other)
            {
                selfProduct = _reactions[i].selfProduct;
                otherProduct = _reactions[i].otherProduct;
                return true;
            }
        }
        selfProduct = 0;
        otherProduct = 0;
        return false;
    }

    public static bool HasAnyReaction(byte material)
    {
        for (int i = 0; i < _reactions.Length; i++)
            if (_reactions[i].self == material) return true;
        return false;
    }
}

/// ==========================================================================
/// MaterialPalette — the authored look of each material.
///
/// REPLACES the shader's old `(mat - 1) % 8` dev palette, which assigned
/// colours by arithmetic accident: it guaranteed adjacent material ids never
/// shared a colour, and guaranteed nothing else. Water landed on teal, lava on
/// olive, obsidian on magenta -- flat, saturated, game-UI colours that made a
/// screenshot hard to read as terrain.
///
/// ART DIRECTION: natural and muted. Earthy greens and browns, warm neutral
/// stone, and liquids that read as materials rather than as UI accents. Fewer
/// saturated primaries; tonal variation comes from the shading step (per-voxel
/// grain, ambient occlusion, hemisphere tint) rather than from picking louder
/// hues. Values are deliberately mid-range: the shading multiplies them down,
/// so anything authored near 1.0 blows out once a face catches the sky term.
///
/// THIS TABLE IS MIRRORED IN Raymarch.compute's MaterialAlbedo(). The two must
/// stay in sync and there is an EditMode test (PaletteSyncTests) that parses the
/// shader and fails if they drift -- because a silent mismatch between the CPU
/// palette (debug overlays) and the GPU palette (what you actually look at) is
/// exactly the kind of thing nobody notices for a month.
public static class MaterialPalette
{
    public readonly struct Rgb
    {
        public readonly float R, G, B;
        public Rgb(float r, float g, float b) { R = r; G = g; B = b; }
    }

    /// Unknown/unauthored materials get a flat magenta so a missing entry is
    /// obvious on screen rather than silently plausible.
    public static readonly Rgb Missing = new Rgb(0.90f, 0.10f, 0.80f);

    private static readonly Rgb[] _table = new Rgb[256];

    static MaterialPalette()
    {
        for (int i = 0; i < 256; i++) _table[i] = Missing;

        // Air is never shaded (the raymarcher treats mat==0 as no-hit), but give
        // it something inert so a stray lookup is not magenta.
        _table[Materials.Air] = new Rgb(0.00f, 0.00f, 0.00f);

        // ---- Rock ----
        // Warm neutral grey, very slightly brown. Pure grey reads as plastic;
        // a few points of red over blue is what makes it read as rock.
        _table[Materials.Stone] = new Rgb(0.44f, 0.425f, 0.400f);
        // Deeper rock: darker and cooler, so depth reads as depth without
        // needing a lighting system to tell you.
        _table[Materials.Deepstone] = new Rgb(0.255f, 0.260f, 0.285f);
        // Stone with a green cast rather than a green ON stone -- it should sit
        // between Stone and Grass, not look like painted rock.
        _table[Materials.MossyStone] = new Rgb(0.355f, 0.410f, 0.330f);

        // ---- Ground cover ----
        // Muted olive-green. The single most important restraint in the whole
        // palette: a saturated green here is what makes voxel terrain look like
        // a toy. Kept dark and yellow-shifted.
        _table[Materials.Grass] = new Rgb(0.365f, 0.470f, 0.255f);
        // Jungle: deeper and bluer than grass, still unsaturated.
        _table[Materials.JungleGrass] = new Rgb(0.275f, 0.410f, 0.235f);
        // NOTE ON "DIRT": the v1 roster (§5.5) has no Dirt material, so the
        // warm-earth role is carried by Sandstone below. Adding a Dirt id would
        // be additive and harmless, but it would be content generation never
        // emits, so it is deliberately not added here.
        _table[Materials.Sandstone] = new Rgb(0.520f, 0.415f, 0.295f);
        // Warm pale sand, not yellow. Beach sand is closer to grey than people
        // remember; pushing saturation here is what makes deserts look neon.
        _table[Materials.Sand] = new Rgb(0.735f, 0.660f, 0.495f);
        // Snow: LOWERED 0.86 -> 0.72 after looking at the first Playground
        // captures. At 0.86 the hemisphere term pushed up-facing snow to
        // clipping, and since snow covers most of this island the whole
        // mid-ground read as a flat white sheet with the terrain shape lost in
        // it. 0.72 keeps snow clearly the brightest material while leaving the
        // sky term somewhere to go, so slopes and contours stay legible.
        // Faint cool cast retained so it still separates from stone.
        _table[Materials.Snow] = new Rgb(0.715f, 0.735f, 0.760f);

        // ---- Liquids ----
        // Water: desaturated blue-green and DARK. The old teal read as a UI
        // colour; real water is mostly a dark surface that borrows brightness
        // from the sky, which the hemisphere term supplies.
        _table[Materials.Water] = new Rgb(0.150f, 0.330f, 0.420f);
        // Lava: authored as a deep hot red rather than orange. The shading step
        // gives it a self-lit boost and skips the darkening terms, which is what
        // actually makes it read as emissive -- see MaterialAlbedo/IsEmissive in
        // the shader. Authoring it bright here instead would just look pink.
        _table[Materials.Lava] = new Rgb(0.720f, 0.215f, 0.070f);
        // Honey: amber, warm, noticeably darker than sand so the two never
        // read as the same substance.
        _table[Materials.Honey] = new Rgb(0.680f, 0.480f, 0.130f);

        // ---- Light source (Phase 7) ----
        // Authored MID-BRIGHT, not near-white. The shading step adds the
        // emissive term on top (C.9), so a torch authored at 0.9 would clip the
        // moment its own light landed on it -- the same mistake Snow made at
        // 0.86 and was lowered for. Warm and slightly orange so an unlit torch
        // still reads as a torch.
        _table[Materials.Torch] = new Rgb(0.760f, 0.545f, 0.285f);
        // Stage G's coloured torches. The ALBEDO is the unlit body of the torch
        // -- what you see when it is off screen-edge or occluded -- and it is
        // deliberately a MUTED version of the light it casts, not the same
        // colour: a torch whose body is as saturated as its flame reads as a
        // glowing cube rather than as a lit object.
        _table[Materials.TorchBlue]   = new Rgb(0.300f, 0.480f, 0.780f);
        _table[Materials.TorchGreen]  = new Rgb(0.330f, 0.720f, 0.390f);
        _table[Materials.TorchPurple] = new Rgb(0.560f, 0.340f, 0.760f);

        // ---- Reaction product ----
        // Obsidian: near-black with a violet bias, and the darkest thing in the
        // palette. It should read as dense and glassy against both the lava it
        // came from and the stone around it.
        _table[Materials.Obsidian] = new Rgb(0.095f, 0.085f, 0.125f);

        // ---- §2.7 fluids ----
        // Oil: near-black warm brown, darker than anything but obsidian.
        _table[Materials.Oil]       = new Rgb(0.110f, 0.085f, 0.060f);
        // Acid: sour yellow-green, the one deliberately loud liquid (danger).
        _table[Materials.Acid]      = new Rgb(0.520f, 0.700f, 0.160f);
        // Sludge: deeper, murkier green; the glow comes from its emissive term.
        _table[Materials.Sludge]    = new Rgb(0.300f, 0.620f, 0.180f);
        // Quicksand: wet sand, darker and browner than dry sand.
        _table[Materials.Quicksand] = new Rgb(0.560f, 0.470f, 0.320f);
        // Steam: pale cool grey, below snow so the two never read alike.
        _table[Materials.Steam]     = new Rgb(0.700f, 0.720f, 0.740f);
    }

    public static Rgb Of(byte material) => _table[material];

    // ---------------------------------------------------------------------
    // THE LOOK-1 PALETTE (LightingConfig.look = 1, the Brickmap look).
    //
    // Taken verbatim from VoxelTerraria_Brickmap's NewMaterialPalette.asset,
    // through that repo's own PaletteMap (main material -> Brickmap slot). These
    // are DISPLAY-space values -- that engine renders in Gamma -- and the shader
    // treats them as such. Deliberately more saturated than `_table` above: the
    // "natural and muted" direction above was authored for a linear pipeline
    // that brightens and desaturates everything on output, and the owner's
    // recorded preference is the Brickmap's look.
    //
    // Mirrored in Raymarch.compute's MaterialAlbedoLook(); PaletteSyncTests
    // pins the two together exactly as it does for MaterialAlbedo.
    // Deepstone and MossyStone follow PaletteMap (stone and sandstone/mud); the
    // Brickmap has no honey, so honey keeps its muted amber.
    // ---------------------------------------------------------------------
    private static readonly Rgb[] _lookTable = BuildLookTable();

    private static Rgb[] BuildLookTable()
    {
        var t = new Rgb[256];
        for (int i = 0; i < 256; i++) t[i] = Missing;
        t[Materials.Air]         = new Rgb(0.00f, 0.00f, 0.00f);
        t[Materials.Stone]       = new Rgb(0.48f, 0.48f, 0.50f);
        t[Materials.Grass]       = new Rgb(0.33f, 0.55f, 0.24f);
        t[Materials.Sand]        = new Rgb(0.83f, 0.74f, 0.51f);
        t[Materials.MossyStone]  = new Rgb(0.72f, 0.60f, 0.42f);
        t[Materials.Water]       = new Rgb(0.12f, 0.34f, 0.62f);
        t[Materials.Snow]        = new Rgb(0.93f, 0.95f, 0.97f);
        t[Materials.Sandstone]   = new Rgb(0.72f, 0.60f, 0.42f);
        t[Materials.JungleGrass] = new Rgb(0.20f, 0.46f, 0.18f);
        t[Materials.Deepstone]   = new Rgb(0.48f, 0.48f, 0.50f);
        t[Materials.Lava]        = new Rgb(0.95f, 0.35f, 0.08f);
        t[Materials.Honey]       = new Rgb(0.68f, 0.48f, 0.13f);
        t[Materials.Obsidian]    = new Rgb(0.09f, 0.07f, 0.12f);
        t[Materials.Torch]       = new Rgb(1.00f, 0.70f, 0.35f);
        t[Materials.TorchBlue]   = new Rgb(0.40f, 0.60f, 1.00f);
        t[Materials.TorchGreen]  = new Rgb(0.45f, 1.00f, 0.50f);
        t[Materials.TorchPurple] = new Rgb(0.78f, 0.45f, 1.00f);
        t[Materials.Oil]         = new Rgb(0.16f, 0.12f, 0.08f);
        t[Materials.Acid]        = new Rgb(0.62f, 0.90f, 0.18f);
        t[Materials.Sludge]      = new Rgb(0.40f, 0.85f, 0.22f);
        t[Materials.Quicksand]   = new Rgb(0.66f, 0.56f, 0.38f);
        t[Materials.Steam]       = new Rgb(0.85f, 0.87f, 0.90f);
        return t;
    }

    public static Rgb OfLook(byte material) => _lookTable[material];

    /// True for materials the shader should treat as self-lit -- they skip the
    /// ambient-occlusion and hemisphere darkening so they stay hot.
    /// NOT a lighting system: nothing here emits light onto anything else.
    /// Real emission arrives with Phase 7 (§6.5); this is a shading cheat.
    public static bool IsSelfLit(byte material) => material == Materials.Lava || material == Materials.Sludge;
}
