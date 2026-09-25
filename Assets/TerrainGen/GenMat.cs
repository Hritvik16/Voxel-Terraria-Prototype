// Assets/TerrainGen/GenMat.cs
//
// The generator's OWN material vocabulary. Deliberately not any project's
// material ids: each project maps GenMat -> its ids once, in its own code.
// Values are append-only (a saved world's content hash depends on them).
namespace TerrainGen
{
    public static class GenMat
    {
        public const byte Air = 0;

        // ---- fluids, generated AT REST (§2.7) ----
        public const byte Water = 1;
        public const byte Lava = 2;
        public const byte Honey = 3;
        public const byte Oil = 4;
        public const byte Acid = 5;
        public const byte Sludge = 6;
        public const byte Quicksand = 7;

        // ---- surfaces ----
        public const byte Grass = 10;
        public const byte JungleGrass = 11;
        public const byte Sand = 12;          // falling solid: only on slopes <= 45 deg
        public const byte Snow = 13;          // powder snow: falling solid, same rule
        public const byte BeachSand = 14;

        // ---- soils ----
        public const byte Loam = 20;
        public const byte PackedSand = 21;
        public const byte PackedIce = 22;
        public const byte Mud = 23;

        // ---- strata, per biome (§2.6), 5 each, top of the band cycle first ----
        public const byte Limestone = 30, Slate = 31, OchreClay = 32, Granite = 33, Shale = 34;             // forest
        public const byte CreamSandstone = 40, RedSandstone = 41, OrangeSandstone = 42, MaroonMudstone = 43, PinkSiltstone = 44; // desert
        public const byte BlueSlate = 50, Marble = 51, Basalt = 52, TealQuartzite = 53, SilverSchist = 54;  // snow
        public const byte KarstLimestone = 60, BlackBasalt = 61, Laterite = 62, MossRock = 63, JadeSerpentine = 64; // jungle

        // ---- depth layers, shared by every biome (§2.2) ----
        public const byte Deepstone = 70;
        public const byte CavernRock = 71;
        public const byte Ash = 72;
        public const byte Obsidian = 73;
        public const byte MagmaCrust = 74;
        public const byte Bedrock = 75;

        // ---- stamps (§2.5), 2026-09-25, append-only ----
        public const byte Sinter = 80;          // geyser-basin mineral terraces
        public const byte CrystalBlue = 81, CrystalAmber = 82, CrystalRose = 83, CrystalGreen = 84; // spires, by biome
        public const byte Bone = 85;            // colossal fossils
        public const byte MeteorOre = 86;       // a crater's glowing core
        public const byte Bark = 87, Leaves = 88;          // the world tree
        public const byte MushroomStem = 89, MushroomCap = 90;
        public const byte VolcanicRock = 91;    // the volcano's cone
        public const byte StandingStone = 92;   // stone rings

        public static bool IsFluid(byte m) => m >= Water && m <= Quicksand;
        public static bool IsSolid(byte m) => m >= Grass;
    }

    public static class GenBiome
    {
        public const byte Forest = 0, Desert = 1, Snow = 2, Jungle = 3;
        public const int Count = 4;
    }
}
