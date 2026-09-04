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

        // Obsidian is the Water+Lava product: an ordinary static solid, and
        // specifically NOT mobile -- that is what makes a reaction show up in
        // the conservation ledger as a permanent removal rather than a loss.
        Define(Materials.Obsidian, 0u, 0);

        // ---- §7.6 reactions ----
        // Water + Lava -> Obsidian. The LAVA cell becomes the obsidian (the
        // solid is where the melt was); the WATER cell is consumed to Air
        // (it flashed off). Both fluid slots free -- the one that ran the scan
        // frees immediately, its counterpart self-frees on the next tick's
        // orphan check (§7.3), because its home byte no longer matches its
        // cached material. That asymmetry is the spec's mechanism, not a bug.
        _reactions = new[]
        {
            new Reaction { self = Materials.Water, other = Materials.Lava,
                           selfProduct = Materials.Air, otherProduct = Materials.Obsidian },
            new Reaction { self = Materials.Lava,  other = Materials.Water,
                           selfProduct = Materials.Obsidian, otherProduct = Materials.Air },
        };
    }

    private static void Define(byte material, uint flags, uint tickInterval)
    {
        _flags[material] = flags;
        _tickInterval[material] = tickInterval;
    }

    public static uint Flags(byte material) => _flags[material];

    /// True if `material` gets a fluid slot and runs the CA (§7.3).
    public static bool IsMobile(byte material) => (_flags[material] & IsMobileMask) != 0u;

    public static bool IsFluidMaterial(byte material) => (_flags[material] & IsFluid) != 0u;

    public static bool IsFallingSolidMaterial(byte material) => (_flags[material] & IsFallingSolid) != 0u;

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

        // ---- Reaction product ----
        // Obsidian: near-black with a violet bias, and the darkest thing in the
        // palette. It should read as dense and glassy against both the lava it
        // came from and the stone around it.
        _table[Materials.Obsidian] = new Rgb(0.095f, 0.085f, 0.125f);
    }

    public static Rgb Of(byte material) => _table[material];

    /// True for materials the shader should treat as self-lit -- they skip the
    /// ambient-occlusion and hemisphere darkening so they stay hot.
    /// NOT a lighting system: nothing here emits light onto anything else.
    /// Real emission arrives with Phase 7 (§6.5); this is a shading cheat.
    public static bool IsSelfLit(byte material) => material == Materials.Lava;
}
