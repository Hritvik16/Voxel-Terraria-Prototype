// Assets/TerrainGen/Tests/LandformClassTests.cs
//
// Surface rebuild B2 (plan rule 2; owner Q7 (a) and Q9, 2026-10-03): on map v4
// the regions are dealt by LANDFORM CLASS so the gentle class covers
// spec.GentleShare of the land (default 0.50, tunable), broken and steep split
// the rest 3:2, and there are no T6 inselbergs. v3 is untouched.
//   Layout    Regions.AssignByFlatness hits each share on its own estimate grid
//             (cheap: no map build) at 0.40 / 0.50 / 0.60, four seeds.
//   Map       on the built v4 map (land = height above sea, class = the cell's
//             region) the gentle share is 50 +- 5 points, all three classes
//             are present and no inselberg was raised; v3 still has them.
// Slopes are reported by PlayMetricsReport (B2 table), not gated here.
using NUnit.Framework;

namespace TerrainGen.Tests
{
    public class LandformClassTests
    {
        private static readonly uint[] Seeds = { 42u, 7u, 2026u, 31337u };

        private static float[] EstimatedShares(in TerrainSpec spec, out Regions.Layout L)
        {
            var o = Offsets.For(in spec);
            L = Regions.Build(in spec, in o);
            int[] area = Regions.LandCells(in spec, in o, L);
            var share = new float[Regions.CLASSES];
            float total = 0f;
            for (int s = 0; s < L.Count; s++) { share[Regions.ClassOf(L.kind[s])] += area[s]; total += area[s]; }
            for (int c = 0; c < Regions.CLASSES; c++) share[c] /= total;
            return share;
        }

        [Test]
        [Category("Slow")]   // 24 layouts, ~18 s in the Editor's Mono
        public void V4Layout_HitsTheGentleShare_OnTheEstimateGrid()
        {
            var lines = new System.Collections.Generic.List<string>();
            foreach (uint seed in Seeds)
            foreach (float g in new[] { 0.40f, 0.50f, 0.60f })
            {
                var spec = TerrainSpec.SizeClass2(seed, 4);
                spec.gentleShare = g;
                float[] sh = EstimatedShares(in spec, out var L);
                lines.Add($"seed {seed} target {g:P0}: gentle {sh[0]:P1} broken {sh[1]:P1} steep {sh[2]:P1} ({L.Count} regions)");
                Assert.AreEqual(g, sh[0], 0.03f, $"seed {seed}: gentle share {sh[0]:P1} against {g:P0}");
                Assert.Greater(sh[1], 0f, $"seed {seed} at {g:P0}: no broken region");
                Assert.Greater(sh[2], 0f, $"seed {seed} at {g:P0}: no steep region");
                Assert.AreEqual(Regions.Alpine, L.kind[0], "the peak's region stays alpine");
                Assert.AreEqual(Regions.Volcanic, L.kind[1], "the volcano's region stays volcanic");
            }
            UnityEngine.Debug.Log("[LandformClass] estimate grid\n" + string.Join("\n", lines));

            // v3 ignores gentleShare: its layout is the v3 deal whatever the field says.
            var v3 = TerrainSpec.SizeClass2(42u, 3);
            var o3 = Offsets.For(in v3);
            var a = Regions.Build(in v3, in o3);
            v3.gentleShare = 0.9f;
            Assert.AreEqual(0f, v3.GentleShare);
            var b = Regions.Build(in v3, in o3);
            CollectionAssert.AreEqual(a.kind, b.kind, "a v3 layout must not read gentleShare");
        }

        [Test]
        [Category("Slow")]   // four v4 map builds (shared) + the v3 seed 42 one
        public void V4Map_GentleShare_AllClasses_NoInselbergs()
        {
            var lines = new System.Collections.Generic.List<string>();
            foreach (uint seed in Seeds)
            {
                var spec = TerrainSpec.SizeClass2(seed, 4);
                var map = SharedMaps.Get(spec);   // shared: never disposed here
                var n = new int[Regions.CLASSES];
                int land = 0;
                for (int i = 0; i < map.Height.Length; i++)
                {
                    if (map.Height[i] <= 0f) continue;
                    land++;
                    if (map.Region[i] != 255) n[Regions.ClassOf(map.Region[i])]++;
                }
                float g = n[0] / (float)land, b = n[1] / (float)land, s = n[2] / (float)land;
                lines.Add($"seed {seed} v4: gentle {g:P1} broken {b:P1} steep {s:P1}, inselbergs {map.InselbergCount}");
                Assert.AreEqual(TerrainSpec.V4_GENTLE_SHARE, g, 0.05f, $"seed {seed}: gentle share of land {g:P1}");
                Assert.Greater(b, 0.01f, $"seed {seed}: broken class missing");
                Assert.Greater(s, 0.01f, $"seed {seed}: steep class missing");
                Assert.AreEqual(0, map.InselbergCount, $"seed {seed}: v4 must raise no inselbergs (owner Q7 (a))");
            }
            int v3Insel = SharedMaps.Get(TerrainSpec.SizeClass2(42u, 3)).InselbergCount;
            lines.Add($"seed 42 v3: inselbergs {v3Insel}");
            Assert.Greater(v3Insel, 0, "v3 keeps its inselbergs");
            UnityEngine.Debug.Log("[LandformClass] maps\n" + string.Join("\n", lines));
        }
    }
}
