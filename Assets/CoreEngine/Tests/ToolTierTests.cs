// Assets/CoreEngine/Tests/ToolTierTests.cs
//
// Gameplay STEP 10 (task 105, GAMEPLAY_PHASE_PLAN.md step 10's acceptance):
// hardness and tool tiers as gear. The rate table (Tools.Cost per pick x
// material, pinned literally), the ladder (each pick digs the next ore, a
// copper pick is REFUSED at iron, nothing digs bedrock, no rock is ever
// refused), and the Survival dig paths on a synthetic ChunkStore: the aimed
// voxel too hard -> refused, nothing written; mixed spheres leave the ore;
// work is conserved across frames; dig strokes keep too-hard cells; the held
// pick survives player.json.

using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;
using VoxelEngine.Memory;

public class ToolTierTests
{
    private static byte T(byte code) => Materials.FromTg2(code);
    private static readonly byte Loam = T(TerrainGen.GenMat.Loam);
    private static readonly byte Limestone = T(TerrainGen.GenMat.Limestone);
    private static readonly byte Granite = T(TerrainGen.GenMat.Granite);
    private static readonly byte Obsidian = T(TerrainGen.GenMat.Obsidian);
    private static readonly byte Copper = T(TerrainGen.Ores.Copper);
    private static readonly byte Iron = T(TerrainGen.Ores.Iron);
    private static readonly byte Gold = T(TerrainGen.Ores.Gold);
    private static readonly byte Cobalt = T(TerrainGen.Ores.Cobalt);
    private static readonly byte Ember = T(TerrainGen.Ores.Emberstone);
    private static readonly byte Bedrock = T(TerrainGen.GenMat.Bedrock);

    private const int StonePick = 0, CopperPick = 1, GoldPick = 2, IronPick = 3, CobaltPick = 4, EmberPick = 5;

    // =====================================================================
    // The table
    // =====================================================================

    [Test]
    public void Codes_MatchTerrainGen()
    {
        Assert.AreEqual(TerrainGen.Ores.Copper, Tools.CopperOreCode);
        Assert.AreEqual(TerrainGen.Ores.Iron, Tools.IronOreCode);
        Assert.AreEqual(TerrainGen.Ores.Gold, Tools.GoldOreCode);
        Assert.AreEqual(TerrainGen.Ores.Cobalt, Tools.CobaltOreCode);
        Assert.AreEqual(TerrainGen.Ores.Emberstone, Tools.EmberstoneCode);
        Assert.AreEqual(TerrainGen.GenMat.MeteorOre, Tools.MeteorOreCode);
        Assert.AreEqual(TerrainGen.GenMat.Bedrock, Tools.BedrockCode);
        Assert.AreEqual("iron ore", Items.Name(Iron));
    }

    /// THE RATE TABLE: work units per voxel (0 = too hard), pick x material.
    [Test]
    public void RateTable_CostPerPickAndMaterial()
    {
        byte[] mats = { Loam, Limestone, Granite, Obsidian, Copper, Gold, Iron, Cobalt, Ember, Bedrock };
        int[,] want =
        {   //  loam lime gran obsid  cu  au  fe  co  emb bed
            {   1,   2,   3,   4,     2,  0,  0,  0,  0,  0 },   // stone pick (1)
            {   1,   1,   2,   3,     1,  2,  0,  0,  0,  0 },   // copper pick (2)
            {   1,   1,   1,   2,     1,  1,  2,  0,  0,  0 },   // gold pick (3)
            {   1,   1,   1,   1,     1,  1,  1,  2,  0,  0 },   // iron pick (4)
            {   1,   1,   1,   1,     1,  1,  1,  1,  2,  0 },   // cobalt pick (5)
            {   1,   1,   1,   1,     1,  1,  1,  1,  1,  0 },   // emberstone pick (6)
        };
        var lines = new System.Text.StringBuilder("pick            vox/s r  | voxels/s on: loam limestone granite obsidian cu au fe co ember bedrock\n");
        for (int p = 0; p < Tools.Picks.Length; p++)
        {
            var pk = Tools.Picks[p];
            Assert.AreEqual(p + 1, pk.Tier, $"{pk.Name}: tiers run 1..6 in table order");
            lines.Append($"{pk.Name,-15} {pk.VoxelsPerSecond,5} {pk.RadiusVoxels}  |");
            for (int i = 0; i < mats.Length; i++)
            {
                int c = Tools.Cost(mats[i], pk.Tier);
                Assert.AreEqual(want[p, i], c, $"{pk.Name} on {Items.Name(mats[i])}");
                lines.Append(c == 0 ? "  --" : $" {pk.VoxelsPerSecond / c,4:0}");
            }
            lines.Append('\n');
            if (p > 0) Assert.Greater(pk.VoxelsPerSecond, Tools.Picks[p - 1].VoxelsPerSecond, "a better pick is faster");
        }
        UnityEngine.Debug.Log("[ToolTierTests] rate table\n" + lines);
    }

    [Test]
    public void Ladder_EachPickDigsTheNextOre_CopperRefusedAtIron()
    {
        byte[] orderOfUnlock = { Copper, Gold, Iron, Cobalt, Ember };
        for (int p = 0; p < 5; p++)
        {
            int tier = Tools.Picks[p].Tier;
            Assert.IsFalse(Tools.TooHard(orderOfUnlock[p], tier), $"{Tools.Picks[p].Name} digs {Items.Name(orderOfUnlock[p])}");
            if (p + 1 < orderOfUnlock.Length)
                Assert.IsTrue(Tools.TooHard(orderOfUnlock[p + 1], tier), $"{Tools.Picks[p].Name} refused at {Items.Name(orderOfUnlock[p + 1])}");
            Assert.AreEqual(p, Tools.WeakestPickFor(orderOfUnlock[p]));
        }
        Assert.IsTrue(Tools.TooHard(Iron, Tools.Picks[CopperPick].Tier), "THE acceptance: a copper pick cannot dig iron");
        Assert.IsFalse(Tools.TooHard(Iron, Tools.Picks[GoldPick].Tier));
        Assert.AreEqual(-1, Tools.WeakestPickFor(Bedrock), "nothing digs bedrock");
        Assert.AreEqual(CobaltPick, Tools.WeakestPickFor(T(TerrainGen.GenMat.MeteorOre)));
    }

    [Test]
    public void OnlyOresAndBedrockAreEverRefused_FluidsAreSoft()
    {
        int stoneTier = Tools.Picks[StonePick].Tier;
        for (int m = 0; m < 256; m++)
        {
            byte b = (byte)m;
            bool needsTier = (b > Copper && b <= Ember) || b == Bedrock || b == T(TerrainGen.GenMat.MeteorOre);
            Assert.AreEqual(needsTier, Tools.TooHard(b, stoneTier), $"id {m} ({Items.Name(b)}): only ores past copper and bedrock refuse the starter");
            if (MaterialRules.IsFluidMaterial(b)) Assert.AreEqual(0, Tools.Hardness(b), $"fluid {Items.Name(b)} is soft");
        }
        Assert.AreEqual(0, Tools.Hardness(Materials.Torch));
    }

    // =====================================================================
    // The dig paths on a synthetic ChunkStore
    // =====================================================================

    private BrickDataPool _pool;
    private ChunkStore _store;
    private EditService _edits;
    private readonly int[] _scratch = new int[256];
    private const int Ground = 40;                       // y < 40 limestone, above air

    [SetUp]
    public void SetUp()
    {
        _pool = new BrickDataPool(4000, rangeAware: true);
        _store = new ChunkStore(_pool, new ChunkHandleAllocator(8), new int3(4, 4, 4));
        _store.InsertChunk(new Chunk { coord = int3.zero, isUniform = true, uniformMaterial = Materials.Air });
        for (int z = 0; z < 64; z++)
        for (int y = 0; y < Ground; y++)
        for (int x = 0; x < 64; x++)
            _store.SetVoxel(new int3(x, y, z), Limestone);
        _edits = new EditService();
        _edits.AttachWorld(_store, _store, _store, null);
    }

    [TearDown]
    public void TearDown() => _pool?.Dispose();

    /// An iron vein: a 4^3 block of iron ore at lo.
    private void Vein(int3 lo, byte ore, int n = 4)
    {
        for (int z = 0; z < n; z++) for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            _store.SetVoxel(lo + new int3(x, y, z), ore);
    }

    private int Count(byte m)
    {
        int n = 0;
        for (int z = 0; z < 64; z++) for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
            if (_store.GetVoxel(new int3(x, y, z)) == m) n++;
        return n;
    }

    [Test]
    public void CopperPick_AimedAtIron_IsRefused_NothingWritten()
    {
        Vein(new int3(30, 20, 30), Iron);
        var inv = new Inventory();
        var pick = new SurvivalBuild.PickTables(CopperPick);
        int work = 500;
        long written = _edits.VoxelsWritten;
        var r = SurvivalBuild.DigWithPick(_edits, _store, inv, new int3(31, 21, 31), pick, ref work, _scratch);
        Assert.IsTrue(r.Refused);
        Assert.AreEqual(Iron, r.RefusedMaterial);
        Assert.AreEqual(0, r.Removed);
        Assert.AreEqual(0, work, "a refused dig drops its work");
        Assert.AreEqual(written, _edits.VoxelsWritten, "nothing written");
        Assert.AreEqual(64, Count(Iron));
        Assert.AreEqual(0, inv.Count(Iron));
    }

    [Test]
    public void GoldPick_DigsTheVein_AtItsCost()
    {
        Vein(new int3(30, 20, 30), Iron);
        var inv = new Inventory();
        var pick = new SurvivalBuild.PickTables(GoldPick);           // tier 3 on iron (3): 2 work per voxel
        int work = 10;
        var r = SurvivalBuild.DigWithPick(_edits, _store, inv, new int3(31, 21, 31), pick, ref work, _scratch);
        Assert.IsFalse(r.Refused);
        Assert.AreEqual(5, r.Removed, "10 work / 2 per iron voxel");
        Assert.IsTrue(r.Waiting);
        Assert.AreEqual(0, work);
        Assert.AreEqual(5, inv.Count(Iron), "credited as iron ore");
        Assert.AreEqual(59, Count(Iron));
    }

    [Test]
    public void MixedSphere_DigsTheRock_LeavesTheOre()
    {
        Vein(new int3(30, 20, 30), Iron);
        var inv = new Inventory();
        var pick = new SurvivalBuild.PickTables(CopperPick);           // radius 2, limestone cost 1
        int work = 10000;
        int3 aim = new int3(29, 21, 31);                                // limestone beside the vein
        var r = SurvivalBuild.DigWithPick(_edits, _store, inv, aim, pick, ref work, _scratch);
        Assert.IsFalse(r.Refused);
        Assert.IsFalse(r.Waiting);
        Assert.AreEqual(0, work, "unspent work is dropped when the sphere is done");
        int iron = 0, lime = 0;
        for (int z = -2; z <= 2; z++) for (int y = -2; y <= 2; y++) for (int x = -2; x <= 2; x++)
        {
            if (x * x + y * y + z * z > 4) continue;
            byte m = _store.GetVoxel(aim + new int3(x, y, z));
            if (m == Iron) iron++;
            else Assert.AreEqual(Materials.Air, m, "every limestone voxel of the sphere dug");
            lime += m == Iron ? 0 : 1;
        }
        Assert.Greater(iron, 0);
        Assert.AreEqual(iron, r.TooHard, "too-hard voxels counted");
        Assert.AreEqual(lime, r.Removed);
        Assert.AreEqual(lime, inv.Count(Limestone));
        Assert.AreEqual(64, Count(Iron), "the vein untouched");
    }

    /// The same voxels cost the same total work however it arrives (frame by
    /// frame, 1 unit at a time, vs all at once).
    [Test]
    public void Work_IsConservedAcrossFrames()
    {
        var pick = new SurvivalBuild.PickTables(StonePick);             // limestone: 2 work per voxel
        int3 aim = new int3(10, 30, 10);
        var inv = new Inventory();
        int work = 0, spent = 0, removed = 0;
        for (int f = 0; f < 21; f++)
        {
            work += 1; spent += 1;
            var r = SurvivalBuild.DigWithPick(_edits, _store, inv, aim, pick, ref work, _scratch);
            removed += r.Removed;
            Assert.IsTrue(r.Waiting, "a whole sphere is never finished at this rate");
        }
        Assert.AreEqual(10, removed, "21 work / 2 per voxel = 10, 1 left over");
        Assert.AreEqual(1, work);

        var budget = new EditService.ToolBudget();
        int accrued = 0;
        for (int f = 0; f < 4; f++) accrued += budget.Accrue(0.25f, Tools.Picks[StonePick].VoxelsPerSecond);
        Assert.AreEqual(40, accrued, "one second of the stone pick = 40 work = 20 limestone voxels");
    }

    [Test]
    public void DigStroke_KeepsTooHardCells_PreviewMatches()
    {
        Vein(new int3(16, 32, 8), Iron);
        var cubes = new List<int3>();
        SurvivalBuild.LineCubes(new int3(0, 32, 8), new int3(40, 32, 8), 4, cubes);   // straight through the vein
        var keep = new SurvivalBuild.PickTables(CopperPick).TooHard;
        var preview = SurvivalBuild.Cost(_store, cubes, 4, true, int.MaxValue, keep);
        var inv = new Inventory();
        var r = SurvivalBuild.DigStroke(_edits, inv, cubes, 4, _scratch, keep);
        Assert.AreEqual(64, r.TooHard, "the 4^3 vein left");
        Assert.AreEqual(preview.Cells, r.Removed, "the preview counts what is dug");
        Assert.AreEqual(64, Count(Iron));
        Assert.AreEqual(0, inv.Count(Iron));

        var r2 = SurvivalBuild.DigStroke(_edits, inv, cubes, 4, _scratch, new SurvivalBuild.PickTables(GoldPick).TooHard);
        Assert.AreEqual(64, r2.Removed, "the gold pick's stroke takes the vein");
        Assert.AreEqual(64, inv.Count(Iron));
    }

    // =====================================================================
    // The held pick in player.json
    // =====================================================================

    [Test]
    public void HeldPick_RoundTrips_OldFileGetsTheStarter()
    {
        var inv = Inventory.WithStarterKit();
        Assert.AreEqual(Tools.StarterPick, inv.pick);
        inv.pick = IronPick;
        var back = Inventory.FromJson(inv.ToJson(), out string err);
        Assert.IsNull(err);
        Assert.AreEqual(IronPick, back.pick);

        var old = Inventory.FromJson("{\"version\":1,\"selected\":2,\"items\":[14],\"counts\":[50]}", out _);
        Assert.AreEqual(Tools.StarterPick, old.pick, "a pre-105 file holds the starter");
        var bad = Inventory.FromJson("{\"version\":1,\"pick\":99}", out _);
        Assert.AreEqual(Tools.Picks.Length - 1, bad.pick, "out of range clamps");
    }
}
