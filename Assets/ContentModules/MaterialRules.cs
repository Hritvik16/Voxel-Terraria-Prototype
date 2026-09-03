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
