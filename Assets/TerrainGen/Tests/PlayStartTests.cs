// Assets/TerrainGen/Tests/PlayStartTests.cs
//
// Gameplay step 4 (task 93c1): the play start (PlayStart.cs) on three v4
// seeds -- the start cell is FLAG_EASY, Walk band, dry, in the chosen area,
// above the sea with no still water over its column, and the same pick comes
// back from a second call (a pure function of the map). The maps are the
// shared ones DangerTests already builds (42, 7, 2026), so no extra builds.
// Task 98: the start prefers a MILD biome's easy area (Forest / Savanna) when
// one has >= MIN_MILD_HA of mild cells; seed 42 (whose largest area is snow)
// must start mild; a hand-made record with no mild area falls back to the
// largest. Each seed logs the 93c1 pick (before) beside the new one (after).
using NUnit.Framework;
using Unity.Mathematics;

namespace TerrainGen.Tests
{
    public class PlayStartTests
    {
        [TestCase(42u)]
        [TestCase(7u)]
        [TestCase(2026u)]
        [TestCase(31337u)]
        [Category("Slow")]   // shared v4 maps (~25 s each when this test builds them first)
        public void Start_IsEasyWalkDry_Deterministic(uint seed)
        {
            var map = SharedMaps.Get(TerrainSpec.SizeClass2(seed, 4));
            var rec = map.Record;
            Assert.IsTrue(PlayStart.Find(map, out var p), $"seed {seed}: no play start");
            Assert.IsTrue(PlayStart.Find(map, out var old, preferMild: false), $"seed {seed}: no 93c1 play start");
            var mildSizes = PlayStart.MildSizes(rec.EasyAreaList, rec.Data);
            string Desc(PlayStart.Pick k) =>
                $"area {k.area} biome {k.biome} ({rec.EasyAreaList[k.area].hectares:F0} ha, mild {mildSizes[k.area] * 0.0064f:F0} ha) " +
                $"ground {(k.groundY - map.Spec.seaLevelY) * 0.1f:F1} m above sea";
            UnityEngine.Debug.Log($"[PlayStart] seed {seed}: BEFORE {Desc(old)} -> AFTER {Desc(p)}; {p}; areas " +
                string.Join(", ", System.Array.ConvertAll(rec.EasyAreaList, a => $"{a.hectares:F0} ha @ ({a.centre.x}, {a.centre.y})")) +
                "; mild ha " + string.Join(", ", System.Array.ConvertAll(mildSizes, m => (m * 0.0064f).ToString("F0"))));

            int area = PlayStart.ChooseArea(rec.EasyAreaList, rec.Data, map.Spec, out bool mild);
            Assert.AreEqual(area, p.area);
            Assert.AreEqual(mild, p.mild);
            if (p.mild) Assert.IsTrue(PlayStart.IsMild(p.biome), "a mild pick stands on a mild cell");
            Assert.AreEqual(rec.Data[p.cell].Biome, p.biome);
            if (seed == 42u)
            {
                Assert.IsTrue(p.mild, "seed 42 starts in a mild biome");
                Assert.IsTrue(PlayStart.IsMild(p.biome), "seed 42: biome mild");
            }
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

        /// The mild rule on a hand-made record: mild size decides, a mild part below
        /// MIN_MILD_HA does not count, and with no mild area the largest of any biome wins.
        [Test]
        public void ChooseArea_MildFirst_FallsBackToLargest()
        {
            var spec = TerrainSpec.SizeClass2(42u, 4);
            int mid = spec.worldSizeVoxels / 2;
            int min = PlayStart.MinMildCells(spec);
            Assert.AreEqual(313, min, "2 ha of 8 m cells");
            var data = new RecordCell[4 * min];
            for (int i = 0; i < data.Length; i++) data[i].biome = GenBiome.Snow;
            var areas = new[]
            {
                AreaOf(0, 2 * min, mid, mid),             // the largest, central, all snow
                AreaOf(2 * min, min, mid + 20000, mid),   // smaller, far, desert
                AreaOf(3 * min, min, mid + 30000, mid),   // smallest, farthest, forest
            };
            for (int i = 2 * min; i < 3 * min; i++) data[i].biome = GenBiome.Desert;

            // No mild cell anywhere: the largest of any biome (93c1's rule).
            Assert.AreEqual(-1, PlayStart.BestMildArea(areas, data, spec));
            Assert.AreEqual(0, PlayStart.ChooseArea(areas, data, spec, out bool mild));
            Assert.IsFalse(mild);

            // A mild part one cell short of the minimum still does not count.
            for (int i = 3 * min; i < 4 * min - 1; i++) data[i].biome = GenBiome.Forest;
            Assert.AreEqual(0, PlayStart.ChooseArea(areas, data, spec, out mild));
            Assert.IsFalse(mild);

            // At the minimum it does: the small forest area beats the large snow one.
            data[4 * min - 1].biome = GenBiome.Savanna;
            Assert.AreEqual(2, PlayStart.ChooseArea(areas, data, spec, out mild));
            Assert.IsTrue(mild);

            // Among mild areas the 93c1 rule runs on mild sizes: a mostly-mild area that is tied
            // (>= AREA_TIE) and nearer the centre wins.
            for (int i = min / 20; i < 2 * min; i++) data[i].biome = GenBiome.Forest;   // area 0: 95% forest
            Assert.AreEqual(0, PlayStart.ChooseArea(areas, data, spec, out mild));
            Assert.IsTrue(mild);

            // Deterministic: same inputs, same answer.
            Assert.AreEqual(PlayStart.ChooseArea(areas, data, spec, out _), PlayStart.ChooseArea(areas, data, spec, out _));
            Assert.IsTrue(PlayStart.IsMild(GenBiome.Forest) && PlayStart.IsMild(GenBiome.Savanna));
            Assert.IsFalse(PlayStart.IsMild(GenBiome.Snow) || PlayStart.IsMild(GenBiome.Desert)
                        || PlayStart.IsMild(GenBiome.Jungle) || PlayStart.IsMild(GenBiome.Badlands));
        }

        [Test]
        [Category("Slow")]   // the shared v3 seed-42 map
        public void V3_HasNoPlayStart()
        {
            Assert.IsFalse(PlayStart.Find(SharedMaps.Get(TerrainSpec.SizeClass2(42u, 3)), out _), "v3 has no easy areas");
        }

        private static EasyArea AreaOf(int first, int count, int x, int z)
        {
            var cells = new int[count];
            for (int i = 0; i < count; i++) cells[i] = first + i;
            return new EasyArea { cellCount = count, centre = new int2(x, z), cells = cells };
        }

        private static EasyArea Area(int cells, int x, int z) =>
            new EasyArea { cellCount = cells, centre = new int2(x, z), cells = new[] { 0 } };
    }
}
