// Assets/ContentModules/Tools.cs
//
// GAMEPLAY STEP 10 (task 105, GAMEPLAY_PHASE_PLAN.md step 10): hardness and
// tool tiers as gear. Content lives in a table (plan guardrail 8).
//
//   HARDNESS  every material has one (0 soft .. 5), plus a REQUIRED TIER (0 =
//             any pick; only the ores, meteor ore and bedrock have one).
//   PICKS     the held gear: a tier (power), a rate in voxels/s on soft ground
//             and a dig radius. One pick is held at a time (Inventory.pick).
//   COST      one voxel of material m costs, in voxels of the pick's rate,
//                 Cost = 1 + max(0, hardness - tier + 1)
//             i.e. a tier two above the hardness digs at full rate, an equal
//             tier at half, each tier short of it slower again (rock is never
//             refused, only slowed); and 0 = TOO HARD when tier < required.
//
// THE LADDER (agent's pick, task 105; the owner may change it): each pick
// digs the ore of the NEXT tier, so crafting (step 11) can climb it, and the
// acceptance's refusal holds -- a COPPER pick cannot dig IRON:
//     stone pick (tier 1, the starter) -> copper ore
//     copper pick (2)                  -> gold ore     (NOT iron)
//     gold pick (3)                    -> iron ore
//     iron pick (4)                    -> cobalt ore
//     cobalt pick (5)                  -> emberstone, meteor ore
//     emberstone pick (6)              -> everything but bedrock
// Gold before iron: gold is the softer metal (Mohs ~2.5 vs ~5.5-6.5 for iron
// ores), and it lies DEEPER (Ores.cs: iron sea-50..-100 m, gold below), so a
// copper pick sends the player past the iron to the gold first.
//
// tg2 ids are Materials.TG2_BASE + TerrainGen's GenMat code. This assembly does
// not reference TerrainGen, so the codes are written out (as Items.cs does)
// and pinned by ToolTierTests against GenMat / Ores.

public static class Tools
{
    public readonly struct Pick
    {
        public readonly string Name;
        public readonly int Tier;
        public readonly float VoxelsPerSecond;   // on hardness-0 ground (Cost 1)
        public readonly int RadiusVoxels;

        public Pick(string name, int tier, float voxelsPerSecond, int radiusVoxels)
        {
            Name = name; Tier = tier; VoxelsPerSecond = voxelsPerSecond; RadiusVoxels = radiusVoxels;
        }
    }

    /// Index = Inventory.pick. The starter (index 0) matches pre-105 Survival's
    /// default dig on soft ground (the "drill": 40 vox/s, radius 2).
    public static readonly Pick[] Picks =
    {
        new Pick("stone pick",       1,  40f, 2),
        new Pick("copper pick",      2,  60f, 2),
        new Pick("gold pick",        3,  90f, 2),
        new Pick("iron pick",        4, 130f, 2),
        new Pick("cobalt pick",      5, 180f, 3),
        new Pick("emberstone pick",  6, 250f, 3),
    };

    public const int StarterPick = 0;
    public const byte Never = 255;               // RequiredTier: no pick digs it

    public static int ClampPick(int i) => i < 0 ? 0 : i >= Picks.Length ? Picks.Length - 1 : i;

    public static byte Hardness(byte material) => _hardness[material];
    public static byte RequiredTier(byte material) => _required[material];

    public static bool TooHard(byte material, int tier) => tier < _required[material];

    /// Work units one voxel costs with a pick of `tier`; 0 = too hard.
    public static int Cost(byte material, int tier)
    {
        if (TooHard(material, tier)) return 0;
        int over = _hardness[material] - tier + 1;
        return 1 + (over > 0 ? over : 0);
    }

    /// Fills `cost` (length 256) with Cost(m, tier) for every id.
    public static void FillCosts(int tier, byte[] cost)
    {
        for (int m = 0; m < 256; m++) cost[m] = (byte)Cost((byte)m, tier);
    }

    /// The weakest pick (index) that digs `material`; -1 = none does.
    public static int WeakestPickFor(byte material)
    {
        for (int i = 0; i < Picks.Length; i++) if (!TooHard(material, Picks[i].Tier)) return i;
        return -1;
    }

    // ---- tg2 GenMat codes with a required tier (pinned by ToolTierTests) ----
    public const byte CopperOreCode = 65, IronOreCode = 66, GoldOreCode = 67, CobaltOreCode = 68, EmberstoneCode = 69;
    public const byte MeteorOreCode = 86, BedrockCode = 75;

    private static readonly byte[] _hardness = new byte[256];
    private static readonly byte[] _required = new byte[256];

    static Tools()
    {
        // Anything not named below: ordinary rock.
        for (int m = 0; m < 256; m++) _hardness[m] = 1;

        void H(byte id, byte h, byte req = 0) { _hardness[id] = h; _required[id] = req; }
        void T(byte code, byte h, byte req = 0) => H(Materials.FromTg2(code), h, req);

        // ---- legacy ids ----
        H(Materials.Air, 0);
        H(Materials.Grass, 0); H(Materials.Sand, 0); H(Materials.Snow, 0); H(Materials.JungleGrass, 0);
        H(Materials.Water, 0); H(Materials.Lava, 0); H(Materials.Honey, 0); H(Materials.Oil, 0);
        H(Materials.Acid, 0); H(Materials.Sludge, 0); H(Materials.Quicksand, 0); H(Materials.Steam, 0);
        H(Materials.Torch, 0); H(Materials.TorchBlue, 0); H(Materials.TorchGreen, 0); H(Materials.TorchPurple, 0);
        H(Materials.Stone, 1); H(Materials.MossyStone, 1); H(Materials.Sandstone, 1); H(Materials.Glass, 1);
        H(Materials.Deepstone, 2); H(Materials.CooledStone, 2);
        H(Materials.Obsidian, 3);

        // ---- tg2: surfaces and soils (and the river lip, drawn as water) ----
        T(8, 0);
        T(10, 0); T(11, 0); T(12, 0); T(13, 0); T(14, 0); T(15, 0); T(16, 0); T(17, 0);   // 17 gravel (task 177)
        T(20, 0); T(21, 0); T(23, 0); T(24, 0); T(32, 0);
        T(22, 1); T(25, 1);                                    // packed ice, hardpan
        // ---- tg2: layer rock. Sedimentary 1, igneous / metamorphic 2 ----
        T(30, 1); T(31, 1); T(34, 1);                          // limestone, slate, shale
        T(33, 2); T(35, 2); T(36, 2); T(37, 2);                // granite, kopje granite, ironstone, gneiss
        for (byte c = 40; c <= 49; c++) T(c, 1);               // sandstones, mudstones, bands
        T(50, 1); T(51, 1); T(54, 1);                          // blue slate, marble, silver schist
        T(52, 2); T(53, 2);                                    // basalt, teal quartzite
        T(60, 1); T(62, 1); T(63, 1); T(64, 1);                // karst limestone, laterite, moss rock, serpentine
        T(61, 2);                                              // black basalt
        // ---- tg2: ores (step 3) -- hardness = the tier they need ----
        T(CopperOreCode, 1, 1);
        T(GoldOreCode, 2, 2);
        T(IronOreCode, 3, 3);
        T(CobaltOreCode, 4, 4);
        T(EmberstoneCode, 5, 5);
        T(MeteorOreCode, 5, 5);
        // ---- tg2: the deep ----
        T(70, 2); T(71, 2); T(72, 0); T(73, 3); T(74, 2);      // deepstone, cavern rock, ash, obsidian, magma crust
        T(BedrockCode, 5, Never);
        // ---- tg2: features ----
        T(80, 1); T(81, 2); T(82, 2); T(83, 2); T(84, 2);      // sinter, crystals
        T(85, 1); T(87, 0); T(88, 0); T(89, 0); T(90, 0);      // bone, bark, leaves, mushroom
        T(91, 2); T(92, 2);                                    // volcanic rock, standing stone
    }
}
