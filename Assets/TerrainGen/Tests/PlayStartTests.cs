// Assets/TerrainGen/Tests/PlayStartTests.cs
//
// Gameplay step 4 (task 93c1): the play start (PlayStart.cs) on three v4
// seeds -- the start cell is FLAG_EASY, Walk band, dry, in the chosen area,
// above the sea with no still water over its column, and the same pick comes
// back from a second call (a pure function of the map). The maps are the
// shared ones DangerTests already builds (42, 7, 2026), so no extra builds.
using NUnit.Framework;
using Unity.Mathematics;

namespace TerrainGen.Tests
{
    public class PlayStartTests
    {
        [TestCase(42u)]
        [TestCase(7u)]
        [TestCase(2026u)]
        [Category("Slow")]   // shared v4 maps (~25 s each when this test builds them first)
        public void Start_IsEasyWalkDry_Deterministic(uint seed)
        {
            var map = SharedMaps.Get(TerrainSpec.SizeClass2(seed, 4));
            var rec = map.Record;
            Assert.IsTrue(PlayStart.Find(map, out var p), $"seed {seed}: no play start");
            UnityEngine.Debug.Log($"[PlayStart] seed {seed}: {p}; areas " +
                string.Join(", ", System.Array.ConvertAll(rec.EasyAreaList, a => $"{a.hectares:F0} ha @ ({a.centre.x}, {a.centre.y})")));

            Assert.AreEqual(PlayStart.BestArea(rec.EasyAreaList, map.Spec), p.area);
            CollectionAssert.Contains(rec.EasyAreaList[p.area].cells, p.cell, "the cell is one of the area's");
            var c = rec.Data[p.cell];
            Assert.IsTrue(c.IsEasy, "FLAG_EASY");
            Assert.AreEqual(Danger.Walk, c.MoveBand, "Walk band");
            Assert.IsFalse(c.IsSea, "not sea");
            Assert.AreEqual(Hydrology.DRY, map.WaterClass[p.cell], "no lake or river cell");
            Assert.IsTrue(p.interior, "all 8 neighbours easy, Walk and dry");

            var spec = map.Spec; var view = map.View; var o = Offsets.For(in spec);
            var s = ShapeField.Sample(in spec, in view, in o, p.column.x, p.column.y, 1);
            Assert.AreEqual(s.height, p.groundY);
            Assert.Greater(s.height, spec.seaLevelY, "above the sea");
            Assert.IsTrue(s.water == ColumnSample.NO_WATER_Y || s.water <= s.height, "no still water over the column");
            int n = rec.Cells;
            Assert.AreEqual(p.cell, (p.column.y / rec.CellVoxels) * n + p.column.x / rec.CellVoxels, "column inside the cell");

            Assert.IsTrue(PlayStart.Find(map, out var q));
            Assert.AreEqual(p.ToString(), q.ToString(), "deterministic");
        }

        /// The area rule on a hand-made list: size first (within AREA_TIE), then nearest the centre.
        [Test]
        public void BestArea_LargestThenMostCentral()
        {
            var spec = TerrainSpec.SizeClass2(42u, 4);
            int mid = spec.worldSizeVoxels / 2;
            var areas = new[]
            {
                Area(1000, mid + 20000, mid),   // largest, far from the centre
                Area(950, mid + 5000, mid),     // tied on size, nearer: wins
                Area(500, mid, mid),            // at the centre but too small
            };
            Assert.AreEqual(1, PlayStart.BestArea(areas, spec));
            areas[1].cellCount = 800;           // no longer tied: the largest wins
            Assert.AreEqual(0, PlayStart.BestArea(areas, spec));
            Assert.AreEqual(-1, PlayStart.BestArea(new EasyArea[0], spec));
        }

        [Test]
        [Category("Slow")]   // the shared v3 seed-42 map
        public void V3_HasNoPlayStart()
        {
            Assert.IsFalse(PlayStart.Find(SharedMaps.Get(TerrainSpec.SizeClass2(42u, 3)), out _), "v3 has no easy areas");
        }

        private static EasyArea Area(int cells, int x, int z) =>
            new EasyArea { cellCount = cells, centre = new int2(x, z), cells = new[] { 0 } };
    }
}
