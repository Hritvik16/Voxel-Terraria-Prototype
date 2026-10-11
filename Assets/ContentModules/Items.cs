// Assets/ContentModules/Items.cs
//
// GAMEPLAY STEP 1 (task 64, GAMEPLAY_PHASE_PLAN.md): the item table. Content
// lives in a table (plan guardrail 8), not in Playground's code.
//
// AN ITEM IS A MATERIAL ID. The 1-byte material id is already the engine's
// registry key (§6.6); an inventory slot holds (item, count) with item = the
// material byte it places, 0 = empty. So "dug red sandstone gives red
// sandstone" (pick P3) is the identity, and the only table entries are the
// exceptions and the names:
//   * grass-type surfaces give their soil (tg2 Grass -> Loam, JungleGrass ->
//     Mud, SavannaGrass -> RedEarth), so a lawn is not a portable material;
//   * fluids give nothing (P9: no bucket until a later step);
//   * torches are counted per placement and always place ONE voxel, whatever
//     the cube size (a torch voxel is one point light, §6.6).
// Amounts are voxels (0.1 m cells, P4).
//
// tg2 ids are Materials.TG2_BASE + TerrainGen's GenMat code. This assembly does
// not reference TerrainGen, so the codes below are written out and pinned by
// ItemsTests against GenMat.

public static class Items
{
    public const byte None = 0;

    /// What digging this material puts in the inventory, or None.
    public static byte ItemFor(byte material)
    {
        if (material == Materials.Air) return None;
        if (MaterialRules.IsFluidMaterial(material)) return None;   // P9
        if (material == Tg2(GrassCode)) return Tg2(LoamCode);
        if (material == Tg2(JungleGrassCode)) return Tg2(MudCode);
        if (material == Tg2(SavannaGrassCode)) return Tg2(RedEarthCode);
        return material;
    }

    /// Torches: one voxel per placement, never a cube.
    public static bool PlacesSingleVoxel(byte item) =>
        item == Materials.Torch || item == Materials.TorchBlue ||
        item == Materials.TorchGreen || item == Materials.TorchPurple;

    /// Something a slot can hold and place (not air, not a fluid).
    public static bool IsPlaceable(byte item) =>
        item != None && !MaterialRules.IsFluidMaterial(item);

    /// The Survival starter kit (P10): 50 torches.
    public static readonly (byte item, int count)[] StarterKit = { (Materials.Torch, 50) };

    // ---- tg2 GenMat codes used above (pinned by ItemsTests) ----
    public const byte GrassCode = 10, JungleGrassCode = 11, SavannaGrassCode = 15;
    public const byte LoamCode = 20, MudCode = 23, RedEarthCode = 24;
    private static byte Tg2(byte code) => Materials.FromTg2(code);

    /// Display name; "id N" for an id nothing names.
    public static string Name(byte item)
    {
        string n = _names[item];
        return n ?? $"id {item}";
    }

    private static readonly string[] _names = BuildNames();

    private static string[] BuildNames()
    {
        var n = new string[256];
        n[Materials.Air] = "empty";
        n[Materials.Stone] = "stone"; n[Materials.Grass] = "grass"; n[Materials.Sand] = "sand";
        n[Materials.MossyStone] = "mossy stone"; n[Materials.Water] = "water"; n[Materials.Snow] = "snow";
        n[Materials.Sandstone] = "sandstone"; n[Materials.JungleGrass] = "jungle grass";
        n[Materials.Deepstone] = "deepstone"; n[Materials.Lava] = "lava"; n[Materials.Honey] = "honey";
        n[Materials.Obsidian] = "obsidian"; n[Materials.Torch] = "torch"; n[Materials.TorchBlue] = "blue torch";
        n[Materials.TorchGreen] = "green torch"; n[Materials.TorchPurple] = "violet torch";
        n[Materials.Oil] = "oil"; n[Materials.Acid] = "acid"; n[Materials.Sludge] = "toxic sludge";
        n[Materials.Quicksand] = "quicksand"; n[Materials.Steam] = "steam";

        // tg2 (GenMat code -> name), TerrainGen/GenMat.cs.
        void T(byte code, string name) => n[Materials.TG2_BASE + code] = name;
        T(10, "grass"); T(11, "jungle grass"); T(12, "sand"); T(13, "snow"); T(14, "beach sand");
        T(15, "savanna grass"); T(16, "badlands clay");
        T(20, "loam"); T(21, "packed sand"); T(22, "packed ice"); T(23, "mud"); T(24, "red earth"); T(25, "hardpan");
        T(30, "limestone"); T(31, "slate"); T(32, "ochre clay"); T(33, "granite"); T(34, "shale");
        T(35, "kopje granite"); T(36, "ironstone"); T(37, "gneiss");
        T(40, "cream sandstone"); T(41, "red sandstone"); T(42, "orange sandstone"); T(43, "maroon mudstone"); T(44, "pink siltstone");
        T(45, "red band"); T(46, "cream band"); T(47, "ochre band"); T(48, "umber mudstone"); T(49, "plum shale");
        T(50, "blue slate"); T(51, "marble"); T(52, "basalt"); T(53, "teal quartzite"); T(54, "silver schist");
        T(60, "karst limestone"); T(61, "black basalt"); T(62, "laterite"); T(63, "moss rock"); T(64, "jade serpentine");
        T(65, "copper ore"); T(66, "iron ore"); T(67, "gold ore"); T(68, "cobalt ore"); T(69, "emberstone");   // step 3 (Ores.cs)
        T(70, "deepstone"); T(71, "cavern rock"); T(72, "ash"); T(73, "obsidian"); T(74, "magma crust"); T(75, "bedrock");
        T(80, "sinter"); T(81, "blue crystal"); T(82, "amber crystal"); T(83, "rose crystal"); T(84, "green crystal");
        T(85, "bone"); T(86, "meteor ore"); T(87, "bark"); T(88, "leaves"); T(89, "mushroom stem"); T(90, "mushroom cap");
        T(91, "volcanic rock"); T(92, "standing stone");
        return n;
    }
}
