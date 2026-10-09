// Assets/TerrainGen/Tests/FeatureTests.cs
//
// Surface rebuild B5 (plan rule 6; owner Q6 (a), Q7 (a); the brief's pool):
// signature features drawn from the pool on map v4.
//   Draw (fast)     the pool's rules on 40 seeds: Medium 5, slot 0 the way
//                   down (a canyon), caps, tiers, deterministic; v3's volcano
//                   site and offsets untouched.
//   Four seeds      42, 7, 2026, 31337 (shared v4 maps): 5 features, <= 1 rare
//                   wonder, >= 1 way down, centres >= 1.2 km apart, every
//                   footprint flagged and none on an easy cell, the record's
//                   list is the map's, B4's easy areas still pass (EasyAreaTests.
//                   Check), and >= 80% of land sees a feature within
//                   3 km from a 2 m eye (Features.VisibleShare: a 128 m eye
//                   grid, a ray per target sampled every 8 m map cell).
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.Mathematics;

namespace TerrainGen.Tests
{
    public class FeatureTests
    {
        public static readonly uint[] Seeds = { 42u, 7u, 2026u, 31337u };
        public const float VISIBLE_MIN = 0.80f;

        [Test]
        public void Draw_FollowsThePool()
        {
            var tally = new int[8];
            for (uint seed = 1; seed <= 40; seed++)
            {
                var spec = TerrainSpec.SizeClass2(seed, 4);
                byte[] k = Features.Draw(in spec);
                CollectionAssert.AreEqual(k, Features.Draw(in spec), "the draw is a function of the seed");
                Assert.AreEqual(5, k.Length, $"seed {seed}: Medium draws 5");
                Assert.AreEqual(Features.GreatCanyon, k[0], "slot 0 is the way down");
                foreach (byte b in new[] { Features.GreatPeak, Features.GreatCanyon, Features.Mesa, Features.Volcano, Features.CraterLake })
                    Assert.LessOrEqual(k.Count(x => x == b), Features.Cap(b), $"seed {seed}: cap of {Features.Name(b)}");
                Assert.AreEqual(0, k.Count(x => Features.TierOf(x) == Features.Rare), "no rare wonder exists today");
                foreach (byte b in k) tally[b]++;
            }
            UnityEngine.Debug.Log("[Features] 40 seeds: " + string.Join(", ",
                Enumerable.Range(1, 5).Select(b => $"{Features.Name((byte)b)} {tally[b]}")));
            Assert.Greater(tally[Features.Volcano], 0, "the volcano is in the pool");
            Assert.Less(tally[Features.Volcano], 40, "the volcano is not a fixture");

            // v3: the pinned site, and offsets with no v4 site
            var v3 = TerrainSpec.SizeClass2(42u);
            var o3 = Offsets.For(in v3);
            Assert.AreEqual(0, o3.v4Sites);
            Assert.AreEqual(Stamps.VolcanoSiteV3(in v3, in o3), Stamps.VolcanoSite(in v3, in o3));
            // v4: a drawn volcano sits where the offsets say; none, off the map
            for (uint seed = 1; seed <= 40; seed++)
            {
                var s4 = TerrainSpec.SizeClass2(seed, 4);
                var o4 = Offsets.For(in s4);
                bool drew = Features.Draw(in s4).Contains(Features.Volcano);
                float2 vs = Stamps.VolcanoSite(in s4, in o4);
                if (drew) Assert.Greater(WorldMap.Coast(in s4, in o4, vs), 0.3f, $"seed {seed}: the volcano stands inland");
                else Assert.AreEqual(0f, Stamps.VolcanoHeight(in s4, in o4, new float2(40000f, 40000f), out _, out _), "no volcano, no cone");
            }
        }

        public static List<string> Check(WorldMap map, out string numbers, out Features.Visibility vis)
        {
            var fail = new List<string>();
            var spec = map.Spec;
            var rec = map.Record;
            var f = map.SignatureFeatures;
            vis = default;
            numbers = "";
            if (f == null || rec == null) { fail.Add("no feature list on a v4 map"); return fail; }
            int want = Features.CountFor(in spec);
            if (f.Length != want) fail.Add($"{f.Length} features, want {want} ({Features.LastPlace})");
            int rare = f.Count(x => x.tier == Features.Rare);
            if (rare > Features.RareMaxFor(in spec)) fail.Add($"{rare} rare wonders");
            if (!f.Any(x => x.wayDown)) fail.Add("no way down");
            float minSep = float.MaxValue;
            for (int a = 0; a < f.Length; a++)
            for (int b = a + 1; b < f.Length; b++)
                minSep = math.min(minSep, math.distance((float2)f[a].centre, (float2)f[b].centre));
            if (f.Length > 1 && minSep < Features.MIN_SEP) fail.Add($"centres {minSep / 10f:F0} m apart (< 1.2 km)");
            if (!ReferenceEquals(rec.FeatureList, f)) fail.Add("record list is not the map's");
            foreach (var x in f)
            {
                if (x.mapVersion != spec.mapVersion) fail.Add($"{Features.Name(x.kind)} mapVersion {x.mapVersion}");
                if (x.cellCount <= 0) fail.Add($"{Features.Name(x.kind)} flags no cell");
            }
            int both = 0, flagged = 0;
            var perSite = new int[f.Length + 1];
            foreach (var c in rec.Data)
            {
                if (c.IsFeature) { flagged++; if (c.site >= 1 && c.site <= f.Length) perSite[c.site]++; }
                if (c.IsFeature && c.IsEasy) both++;
            }
            if (both != 0) fail.Add($"{both} cells both easy and feature");
            for (int k = 0; k < f.Length; k++)
                if (perSite[k + 1] != f[k].cellCount) fail.Add($"site {k + 1}: {perSite[k + 1]} cells, list says {f[k].cellCount}");
            vis = Features.VisibleShare(map);
            if (vis.Share < VISIBLE_MIN) fail.Add($"{vis.Share * 100f:F1}% of land sees a feature within 3 km (< 80%)");
            numbers = $"{string.Join(", ", f.Select(x => $"{Features.Name(x.kind)} ({x.centre.x / 10}, {x.centre.y / 10}) m r {x.radiusM:F0} m {x.cellCount} cells"))}; " +
                      $"min spacing {minSep / 10f:F0} m; visible {vis.Share * 100f:F1}% of {vis.eyes} eyes, hidden-near {vis.HiddenNear * 100f:F1}%";
            return fail;
        }

        [Test]
        [Category("Slow")]   // the four shared v4 maps
        public void V4_FourSeeds_FeaturesDrawnPlacedRecordedVisible()
        {
            var lines = new List<string>(); var fails = new List<string>();
            foreach (uint seed in Seeds)
            {
                var map = SharedMaps.Get(TerrainSpec.SizeClass2(seed, 4));   // shared: never disposed here
                var f = Check(map, out string nums, out _);
                // B4 must still hold beside the features
                f.AddRange(EasyAreaTests.Check(map, out string easy).Select(x => "B4: " + x));
                lines.Add($"seed {seed}: {nums}; B4 {easy}\n    place: {Features.LastPlace}\n    easy pick: {EasyAreas.LastPick}\n    easy mark: {EasyAreas.LastMark}");
                fails.AddRange(f.Select(x => $"seed {seed}: {x}"));
            }
            UnityEngine.Debug.Log("[Features]\n" + string.Join("\n", lines));
            Assert.IsEmpty(fails, string.Join("\n", fails));
            Assert.IsNull(SharedMaps.Get(TerrainSpec.SizeClass2(42u)).SignatureFeatures, "v3 has no feature list");
        }
    }
}
