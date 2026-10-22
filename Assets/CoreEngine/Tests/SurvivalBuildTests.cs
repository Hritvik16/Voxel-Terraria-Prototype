// Assets/CoreEngine/Tests/SurvivalBuildTests.cs
//
// Gameplay step 1 (task 64, GAMEPLAY_PHASE_PLAN.md step 1's acceptance): the
// inventory's rules, the item table, EditService's reporting dig overload, and
// Survival's dig/place on a synthetic ChunkStore -- conservation both ways,
// all-or-nothing placement, the bitwise grid snap -- plus the game-mode
// setting and the Creative hotbar the rigs key off.

using System;
using System.Reflection;
using NUnit.Framework;
using Unity.Mathematics;
using VoxelEngine.Memory;

public class SurvivalBuildTests
{
    private static readonly byte Limestone = Materials.FromTg2(TerrainGen.GenMat.Limestone);
    private static readonly byte Tg2Grass = Materials.FromTg2(TerrainGen.GenMat.Grass);
    private static readonly byte Loam = Materials.FromTg2(TerrainGen.GenMat.Loam);
    private const int Ground = 20;                 // y < 20 limestone, y == 20 grass, above air

    // =====================================================================
    // Inventory
    // =====================================================================

    [Test]
    public void Inventory_StacksFirst_ThenEmptySlots_HotbarFirst()
    {
        var inv = new Inventory();
        Assert.AreEqual(100, inv.Add(Limestone, 100));
        Assert.AreEqual(50, inv.Add(Loam, 50));
        Assert.AreEqual(30, inv.Add(Limestone, 30));
        Assert.AreEqual(Limestone, inv.ItemAt(0)); Assert.AreEqual(130, inv.CountAt(0));
        Assert.AreEqual(Loam, inv.ItemAt(1)); Assert.AreEqual(50, inv.CountAt(1));
        Assert.IsTrue(inv.IsEmpty(2));
        Assert.AreEqual(130, inv.Count(Limestone));
        Assert.AreEqual(0, inv.Add(Items.None, 10), "air is not an item");
    }

    [Test]
    public void Inventory_StackCap_SplitsAcrossSlots()
    {
        var inv = new Inventory();
        Assert.AreEqual(25000, inv.Add(Limestone, 25000));
        Assert.AreEqual(Inventory.StackCap, inv.CountAt(0));
        Assert.AreEqual(Inventory.StackCap, inv.CountAt(1));
        Assert.AreEqual(5000, inv.CountAt(2));
        Assert.AreEqual(25000, inv.Count(Limestone));
    }

    [Test]
    public void Inventory_Full_AddsOnlyWhatFits()
    {
        var inv = new Inventory();
        for (int i = 0; i < Inventory.SlotCount; i++) Assert.AreEqual(9990, inv.Add((byte)(Materials.TG2_BASE + 20 + i), 9990));
        Assert.AreEqual(0, inv.Add(Materials.Stone, 5), "no empty slot for a new item");
        Assert.AreEqual(10, inv.Add((byte)(Materials.TG2_BASE + 20), 25), "the existing stack tops up to the cap only");
        Assert.AreEqual(Inventory.StackCap, inv.CountAt(0));
        Assert.AreEqual(0, inv.SpaceFor((byte)(Materials.TG2_BASE + 20)));
    }

    [Test]
    public void Inventory_TakeFromSlot_IsAllOrNothing_AndEmptiesTheSlot()
    {
        var inv = new Inventory();
        inv.Add(Limestone, 64);
        Assert.IsFalse(inv.TakeFromSlot(0, 65));
        Assert.AreEqual(64, inv.CountAt(0), "a refused take changes nothing");
        Assert.IsTrue(inv.TakeFromSlot(0, 64));
        Assert.IsTrue(inv.IsEmpty(0));
        Assert.AreEqual(Items.None, inv.ItemAt(0));
        Assert.IsFalse(inv.TakeFromSlot(40, 1), "out of range");
    }

    [Test]
    public void Inventory_Swap_SwapsOrMerges()
    {
        var inv = new Inventory();
        inv.Add(Limestone, 10);                 // slot 0
        inv.Add(Loam, 20);                      // slot 1
        inv.Swap(0, 20);
        Assert.IsTrue(inv.IsEmpty(0));
        Assert.AreEqual(Limestone, inv.ItemAt(20)); Assert.AreEqual(10, inv.CountAt(20));
        inv.Add(Limestone, 5);                  // tops up slot 20 (stacks first)
        Assert.AreEqual(15, inv.CountAt(20));
        inv.counts[0] = 9995; inv.items[0] = Limestone;
        inv.Swap(20, 0);                        // same item: merge into 0 up to the cap
        Assert.AreEqual(Inventory.StackCap, inv.CountAt(0));
        Assert.AreEqual(10, inv.CountAt(20));
        inv.Selected = -1; Assert.AreEqual(8, inv.Selected, "selection wraps inside the hotbar");
        inv.Selected = 9; Assert.AreEqual(0, inv.Selected);
    }

    [Test]
    public void Inventory_Json_RoundTrips_AndRepairsBadSlots()
    {
        var inv = Inventory.WithStarterKit();
        inv.Add(Limestone, 12345);
        inv.Selected = 4;
        var back = Inventory.FromJson(inv.ToJson(), out string err);
        Assert.IsNull(err);
        Assert.AreEqual(4, back.Selected);
        for (int i = 0; i < Inventory.SlotCount; i++)
        {
            Assert.AreEqual(inv.ItemAt(i), back.ItemAt(i), $"slot {i}");
            Assert.AreEqual(inv.CountAt(i), back.CountAt(i), $"slot {i}");
        }
        Assert.AreEqual(50, back.Count(Materials.Torch), "the starter kit (P10)");

        Assert.IsNull(Inventory.FromJson("{ \"items\": [", out err)); Assert.IsNotNull(err);
        Assert.IsNull(Inventory.FromJson("", out err));
        var fixedInv = Inventory.FromJson("{\"items\":[2,300,4],\"counts\":[20000,5,0]}", out err);
        Assert.AreEqual(Inventory.StackCap, fixedInv.CountAt(0), "over the cap: clamped");
        Assert.IsTrue(fixedInv.IsEmpty(1), "item id out of range: emptied");
        Assert.IsTrue(fixedInv.IsEmpty(2), "zero count: emptied");
        StringAssert.Contains("bad slot", err);
    }

    // =====================================================================
    // Items
    // =====================================================================

    [Test]
    public void Items_ExactMaterial_GrassGivesSoil_FluidsGiveNothing()
    {
        Assert.AreEqual(Limestone, Items.ItemFor(Limestone));
        Assert.AreEqual(Materials.FromTg2(TerrainGen.GenMat.RedSandstone), Items.ItemFor(Materials.FromTg2(TerrainGen.GenMat.RedSandstone)));
        Assert.AreEqual(Loam, Items.ItemFor(Tg2Grass));
        Assert.AreEqual(Materials.FromTg2(TerrainGen.GenMat.Mud), Items.ItemFor(Materials.FromTg2(TerrainGen.GenMat.JungleGrass)));
        Assert.AreEqual(Materials.FromTg2(TerrainGen.GenMat.RedEarth), Items.ItemFor(Materials.FromTg2(TerrainGen.GenMat.SavannaGrass)));
        foreach (byte f in new[] { Materials.Water, Materials.Lava, Materials.Honey, Materials.Oil, Materials.Acid,
                                   Materials.Sludge, Materials.Quicksand, Materials.Steam, Materials.Air })
            Assert.AreEqual(Items.None, Items.ItemFor(f), $"material {f}");
        Assert.AreEqual(Materials.Torch, Items.ItemFor(Materials.Torch));
        Assert.IsTrue(Items.PlacesSingleVoxel(Materials.Torch));
        Assert.IsFalse(Items.PlacesSingleVoxel(Limestone));
        Assert.AreEqual("limestone", Items.Name(Limestone));
        Assert.AreEqual("loam", Items.Name(Loam));
        // Items.cs writes the GenMat codes out (ContentModules cannot see TerrainGen): pin them.
        Assert.AreEqual(TerrainGen.GenMat.Grass, Items.GrassCode);
        Assert.AreEqual(TerrainGen.GenMat.JungleGrass, Items.JungleGrassCode);
        Assert.AreEqual(TerrainGen.GenMat.SavannaGrass, Items.SavannaGrassCode);
        Assert.AreEqual(TerrainGen.GenMat.Loam, Items.LoamCode);
        Assert.AreEqual(TerrainGen.GenMat.Mud, Items.MudCode);
        Assert.AreEqual(TerrainGen.GenMat.RedEarth, Items.RedEarthCode);
    }

    // =====================================================================
    // Dig and place on a synthetic ChunkStore
    // =====================================================================

    private BrickDataPool _pool;
    private ChunkStore _store;
    private EditService _edits;

    [SetUp]
    public void SetUp()
    {
        _pool = new BrickDataPool(4000, rangeAware: true);
        _store = new ChunkStore(_pool, new ChunkHandleAllocator(8), new int3(4, 4, 4));
        _store.InsertChunk(new Chunk { coord = int3.zero, isUniform = true, uniformMaterial = Materials.Air });
        for (int z = 0; z < 64; z++)
        for (int x = 0; x < 64; x++)
        {
            for (int y = 0; y < Ground; y++) _store.SetVoxel(new int3(x, y, z), Limestone);
            _store.SetVoxel(new int3(x, Ground, z), Tg2Grass);
        }
        _edits = new EditService();
        _edits.AttachWorld(_store, _store, _store, null);
    }

    [TearDown]
    public void TearDown() => _pool?.Dispose();

    private int CountIn(int3 lo, int edge, byte m)
    {
        int n = 0;
        for (int z = 0; z < edge; z++) for (int y = 0; y < edge; y++) for (int x = 0; x < edge; x++)
            if (_store.GetVoxel(lo + new int3(x, y, z)) == m) n++;
        return n;
    }

    [Test]
    public void EditService_ReportingOverload_CountsWhatItRemoved()
    {
        var counts = new int[256];
        int got = _edits.MineSphereBudgeted(new int3(30, Ground, 30), 3, 1000, counts);
        Assert.Greater(got, 0);
        Assert.AreEqual(got, counts[Limestone] + counts[Tg2Grass], "every removed voxel is counted once");
        Assert.Greater(counts[Tg2Grass], 0);
        Assert.Greater(counts[Limestone], 0);
        Assert.AreEqual(Materials.Air, _store.GetVoxel(new int3(30, Ground, 30)));
        // The old overload is untouched: same dig, a count only.
        Assert.Greater(_edits.MineSphereBudgeted(new int3(10, Ground - 5, 10), 2, 7), 0);
        Assert.Throws<ArgumentException>(() => _edits.MineSphereBudgeted(int3.zero, 1, 1, new int[10]));
    }

    [Test]
    public void Dig_CreditsExactlyWhatWasRemoved_GrassAsLoam()
    {
        var inv = new Inventory();
        var scratch = new int[256];
        var r = SurvivalBuild.Dig(_edits, inv, new int3(30, Ground - 4, 30), 3, 40, scratch);
        Assert.AreEqual(40, r.Removed);
        Assert.AreEqual(40, r.Credited);
        Assert.AreEqual(0, r.Lost);
        Assert.AreEqual(40, inv.Count(Limestone), "dug N voxels of limestone -> inventory +N limestone");

        var g = SurvivalBuild.Dig(_edits, inv, new int3(10, Ground, 10), 1, 1, scratch);
        Assert.AreEqual(1, g.Removed);
        Assert.AreEqual(1, inv.Count(Loam), "grass gives its soil (P3)");
        Assert.AreEqual(0, inv.Count(Tg2Grass));
    }

    [Test]
    public void Dig_Fluid_GivesNothing_AndFullInventory_LosesTheOverflow()
    {
        var inv = new Inventory();
        var scratch = new int[256];
        _store.SetVoxel(new int3(5, Ground + 1, 5), Materials.Water);
        var w = SurvivalBuild.Dig(_edits, inv, new int3(5, Ground + 1, 5), 0, 1, scratch);
        Assert.AreEqual(1, w.Removed, "the water is still removed, as today (P9)");
        Assert.AreEqual(0, w.Credited);
        Assert.AreEqual(0, w.Lost, "a fluid is not 'lost', it never had an item");

        for (int i = 0; i < Inventory.SlotCount; i++) { inv.items[i] = Materials.TG2_BASE + 20 + i; inv.counts[i] = Inventory.StackCap; }
        var full = SurvivalBuild.Dig(_edits, inv, new int3(40, Ground - 3, 40), 2, 12, scratch);
        Assert.AreEqual(12, full.Removed, "digging still works (P8)");
        Assert.AreEqual(0, full.Credited);
        Assert.AreEqual(12, full.Lost);
    }

    [Test]
    public void Place_InTheAir_CostsTheWholeCube()
    {
        var inv = new Inventory();
        inv.Add(Limestone, 500);
        int3 at = new int3(13, Ground + 6, 13);                 // in the air
        var r = SurvivalBuild.Place(_edits, _store, inv, 0, at, 4);
        Assert.AreEqual(SurvivalBuild.PlaceOutcome.Placed, r.Outcome);
        Assert.AreEqual(new int3(12, 24, 12), r.Lo, "snapped to the 4-voxel grid");
        Assert.AreEqual(64, r.Needed);
        Assert.AreEqual(64, r.Placed);
        Assert.AreEqual(64, CountIn(r.Lo, 4, Limestone), "ChunkStore +64 cells");
        Assert.AreEqual(436, inv.Count(Limestone), "inventory -64");
    }

    [Test]
    public void Place_SkipsSolidCells_AndCountsOnlyTheFilledOnes()
    {
        var inv = new Inventory();
        inv.Add(Limestone, 500);
        // A 4-cube with origin y = 20 (the grass row): its bottom 16 cells are grass and are left alone.
        var r = SurvivalBuild.Place(_edits, _store, inv, 0, new int3(9, Ground + 1, 9), 4);
        Assert.AreEqual(new int3(8, 20, 8), r.Lo);
        Assert.AreEqual(48, r.Needed, "16 grass cells are solid: not filled, not charged");
        Assert.AreEqual(48, r.Placed);
        Assert.AreEqual(16, CountIn(r.Lo, 4, Tg2Grass), "the grass row is untouched");
        Assert.AreEqual(452, inv.Count(Limestone));

        // Conservation the other way: dig the placed cells back out.
        var scratch = new int[256];
        int back = 0;
        for (int y = 21; y < 24; y++)
        for (int z = 8; z < 12; z++)
        for (int x = 8; x < 12; x++)
            back += SurvivalBuild.Dig(_edits, inv, new int3(x, y, z), 0, 1, scratch).Credited;
        Assert.AreEqual(48, back);
        Assert.AreEqual(500, inv.Count(Limestone), "placed then dug: the inventory is whole again");
        Assert.AreEqual(0, CountIn(new int3(8, 21, 8), 3, Limestone));
    }

    [Test]
    public void Place_RefusesWhenShort_EmptyOrUnplaceable_AndWritesNothing()
    {
        var inv = new Inventory();
        inv.Add(Limestone, 63);
        long before = _edits.VoxelsWritten;
        var r = SurvivalBuild.Place(_edits, _store, inv, 0, new int3(20, Ground + 9, 20), 4);
        Assert.AreEqual(SurvivalBuild.PlaceOutcome.TooFew, r.Outcome);
        Assert.AreEqual(64, r.Needed);
        Assert.AreEqual(63, inv.Count(Limestone), "all or nothing (P7)");
        Assert.AreEqual(before, _edits.VoxelsWritten, "nothing written");
        Assert.AreEqual(SurvivalBuild.PlaceOutcome.EmptySlot, SurvivalBuild.Place(_edits, _store, inv, 5, new int3(20, 30, 20), 1).Outcome);
        Assert.AreEqual(SurvivalBuild.PlaceOutcome.NothingToFill,
                        SurvivalBuild.Place(_edits, _store, inv, 0, new int3(20, 5, 20), 2).Outcome, "inside rock");
        inv.items[3] = Materials.Water; inv.counts[3] = 10;
        Assert.AreEqual(SurvivalBuild.PlaceOutcome.NotPlaceable, SurvivalBuild.Place(_edits, _store, inv, 3, new int3(20, 30, 20), 1).Outcome);
        Assert.AreEqual(before, _edits.VoxelsWritten);
    }

    [Test]
    public void Place_Torch_IsOneVoxel_WhateverTheSize_AndFillsFluid()
    {
        var inv = Inventory.WithStarterKit();
        var r = SurvivalBuild.Place(_edits, _store, inv, 0, new int3(33, Ground + 1, 33), 8);
        Assert.AreEqual(SurvivalBuild.PlaceOutcome.Placed, r.Outcome);
        Assert.AreEqual(1, r.Edge);
        Assert.AreEqual(1, r.Placed);
        Assert.AreEqual(Materials.Torch, _store.GetVoxel(new int3(33, Ground + 1, 33)));
        Assert.AreEqual(49, inv.Count(Materials.Torch));

        // A fluid cell is filled (and charged); the water gives nothing back.
        _store.SetVoxel(new int3(40, Ground + 1, 40), Materials.Water);
        inv.Add(Limestone, 1);
        int slot = 1;
        Assert.AreEqual(Limestone, inv.ItemAt(slot));
        var w = SurvivalBuild.Place(_edits, _store, inv, slot, new int3(40, Ground + 1, 40), 1);
        Assert.AreEqual(1, w.Placed);
        Assert.AreEqual(Limestone, _store.GetVoxel(new int3(40, Ground + 1, 40)));
    }

    [Test]
    public void SnapToGrid_IsBitwise_AndFloorsNegativeCoordinates()
    {
        Assert.AreEqual(new int3(4, 0, 8), SurvivalBuild.SnapToGrid(new int3(5, 3, 11), 4));
        Assert.AreEqual(new int3(-4, -8, -4), SurvivalBuild.SnapToGrid(new int3(-1, -5, -4), 4));
        Assert.AreEqual(new int3(7, -3, 0), SurvivalBuild.SnapToGrid(new int3(7, -3, 0), 1));
        foreach (int e in SurvivalBuild.CubeEdges)
        {
            Assert.AreEqual(0, e & (e - 1), $"edge {e} must be a power of two");
            for (int v = -40; v <= 40; v++)
            {
                int floorDiv = v >= 0 ? v / e : -((-v + e - 1) / e);   // integer floor division
                Assert.AreEqual(floorDiv * e, SurvivalBuild.SnapToGrid(new int3(v, v, v), e).x, $"v {v} edge {e}");
            }
        }
        Assert.IsTrue(SurvivalBuild.CubeOverlaps(new int3(0, 0, 0), 4, new int3(3, 3, 3), new int3(5, 9, 5)));
        Assert.IsFalse(SurvivalBuild.CubeOverlaps(new int3(0, 0, 0), 4, new int3(4, 0, 0), new int3(5, 9, 5)));
    }

    // =====================================================================
    // The game-mode setting, and Creative unchanged
    // =====================================================================

    [Test]
    public void PlaySettings_GameMode_DefaultsSurvival_FlagOverridesOneRun()
    {
        Assert.AreEqual("survival", new PlaySettings().gameMode, "P1");
        var old = PlaySettings.Parse("{\"upscaler\":\"fsr3\",\"rows\":\"dyn\"}", out string e);
        Assert.AreEqual("survival", old.gameMode, "a file saved before task 64");
        var over = new System.Collections.Generic.List<string>();
        var run = PlaySettings.WithArgs(old, new[] { "-gamemode", "Creative" }, over);
        Assert.AreEqual("creative", run.gameMode);
        Assert.AreEqual("survival", old.gameMode, "the saved settings are untouched");
        CollectionAssert.Contains(over, "gameMode");
        run = PlaySettings.WithArgs(old, new[] { "-gamemode", "hardcore" }, over);
        Assert.AreEqual("survival", run.gameMode, "a bad value keeps the saved one");
        Assert.AreEqual("survival", PlaySettings.Parse("{\"gameMode\":\"x\"}", out e).gameMode);
        StringAssert.Contains("gameMode", e);
    }

    [Test]
    public void Creative_IsTheDefault_AndTheBrushSlotsTheRigsUseAreUnchanged()
    {
        Type pg = Type.GetType("Playground, Assembly-CSharp");
        Assert.IsNotNull(pg, "Playground not found in Assembly-CSharp");
        var mode = pg.GetField("SurvivalMode", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.IsNotNull(mode);
        Assert.IsFalse((bool)mode.GetValue(null), "only an interactive run turns Survival on; rigs stay Creative");
        var brushes = (byte[])pg.GetField("_brushes", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        // Phase6BrushGuard's BrushWater/BrushSand/BrushLava/BrushStone = 0/1/2/3.
        CollectionAssert.AreEqual(new[] { Materials.Water, Materials.Sand, Materials.Lava, Materials.Stone },
                                  new[] { brushes[0], brushes[1], brushes[2], brushes[3] });
    }
}
