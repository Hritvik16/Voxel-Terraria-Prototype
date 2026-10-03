// Assets/TerrainGen/Tests/StampTests.cs
//
// §2.5's stamps (Stamps.cs) on the sizeClass 2 island, seed 42:
//   Presence    every kind occurs on land at least once; the three singletons
//               (volcano, world tree, geyser basin) are where they should be
//   Arch        an arch has an air opening UNDER a solid deck; S = the deck top
//   Cave mouth  breaks the surface (carvesTop columns exist) and descends > 20 m
//   Volcano     the caldera holds LAVA at rest: flat, contained by its wall
//   World tree  air under the canopy, bark trunk, leaves on top, root caves
//   Geyser      pools of still water below the flat sinter terrace
//   Mushrooms   at least one stands in the cavern layer
//   LOD-0       over stamp columns: S height == D's top solid (except carvesTop)
//   Needles     no map summit falls > 25 m within 40 m on every side (two seeds)
//   Crystals    cluster prisms are <= 6 m tall
// Report: Logs/TerrainGen/stamps.txt.
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace TerrainGen.Tests
{
    public class StampTests
    {
        private static TerrainSpec _spec;
        private static Offsets _o;
        private static WorldMap _map;

        [OneTimeSetUp]
        public void BuildOnce()
        {
            if (_map != null) return;
            _spec = TerrainSpec.SizeClass2(42);
            _o = Offsets.For(42);
            _map = SharedMaps.Get(_spec);
        }

        private static ColumnSample S(float x, float z) { var v = _map.View; return ShapeField.Sample(in _spec, in v, in _o, x, z, 1); }
        /// The column (x, z) exactly as the chunk generator samples it: S at the
        /// voxel CENTRE, D at the integer coordinate. Mixing floor(x) and
        /// floor(x + 0.5) compares two different columns.
        private static ColumnSample Col(int x, int z) => S(x + 0.5f, z + 0.5f);
        private static byte D(in ColumnSample c, int x, int y, int z)
        {
            var cd = DetailField.PrepareColumn(in _spec, in _o, x, z);
            return DetailField.Material(in _spec, in _o, in c, in cd, 0f, x, y, z);
        }
        private static int SiteGround(float2 p) { var v = _map.View; Stamps.SampleSiteHeight(in _spec, in v, p, out int g); return g; }

        private static List<Stamps.Stamp> LandStamps(byte kind)
        {
            var list = new List<Stamps.Stamp>();
            int cells = _spec.worldSizeVoxels >> Stamps.CELL_SHIFT;
            for (int gz = 0; gz < cells; gz++)
            for (int gx = 0; gx < cells; gx++)
            {
                if (!Stamps.CellStamp(in _o, gx, gz, out var s) || s.kind != kind) continue;
                if (SiteGround(s.pos) <= _spec.seaLevelY + 30) continue;
                list.Add(s);
            }
            return list;
        }

        [Test]
        public void EveryKind_OccursOnLand()
        {
            var lines = new List<string>();
            foreach (byte k in new[] { Stamps.CaveMouth, Stamps.Fissure, Stamps.Arch, Stamps.StoneRing, Stamps.Hoop,
                                       Stamps.CrystalCluster, Stamps.MeteorCrater, Stamps.Fossil })
            {
                int n = LandStamps(k).Count;
                lines.Add($"kind {k,2}: {n} on land");
                Assert.Greater(n, 0, $"stamp kind {k} never lands on the island");
            }
            var vs = Stamps.VolcanoSite(in _spec, in _o);
            float rim = SiteGround(vs + new float2(Stamps.CALDERA_R * 1.2f, 0f)) - _spec.seaLevelY;
            lines.Add($"volcano at {vs}: flank {rim / 10f:F0} m above sea 120 m out from the caldera");
            Assert.Greater(rim, 2000f, "the volcano's flank is not a mountain");
            if (Stamps.WorldTreeEnabled)
            {
                var ts = Stamps.TreeSite(in _spec, in _o);
                var tc = S(ts.x + 400f, ts.y);
                lines.Add($"world tree at {ts}: canopy top {(tc.height - _spec.seaLevelY) / 10f:F0} m above sea");
                Assert.Greater(tc.height - SiteGround(ts + new float2(1500f, 0f)), 2000, "the tree is not 200+ m tall");
            }
            else lines.Add("world tree: OFF (Stamps.WorldTreeEnabled; deferred to object placement)");
            var gs = Stamps.GeyserSite(in _spec, in _o);
            Assert.Greater(SiteGround(gs), _spec.seaLevelY + 20, "the geyser basin is not on land");
            File.WriteAllLines(Path.Combine(Dir(), "stamps.txt"), lines);
            Debug.Log("[Stamps]\n" + string.Join("\n", lines));
        }

        /// v3 T5b cap-rock ledges: they exist at cliff feet, each is a solid
        /// shelf over an air gap over solid ground, and the shelf is the
        /// column's top. Counted on a 16 m grid over the island.
        /// Report: Logs/TerrainGen/ledges.txt.
        [Test]
        public void Ledges_AreShelvesOverAGap_AtCliffFeet()
        {
            int land = 0, ledges = 0, checkedN = 0;
            float c0 = _spec.worldSizeVoxels * 0.5f;
            for (int gz = -200; gz < 200; gz++)
            for (int gx = -200; gx < 200; gx++)
            {
                int x = (int)c0 + gx * 160, z = (int)c0 + gz * 160;
                var c = Col(x, z);
                if (c.height < _spec.seaLevelY + 60) continue;
                land++;
                if (c.open3Hi <= c.open3Lo) continue;
                ledges++;
                if (checkedN >= 60) continue;
                Assert.Less(c.open3Hi, c.height, "the gap reaches the shelf top");
                Assert.GreaterOrEqual(c.open3Hi - c.open3Lo, ShapeField.LEDGE_MIN_GAP);
                Assert.AreEqual(GenMat.Air, D(c, x, (c.open3Lo + c.open3Hi) / 2, z), $"no air under the ledge at ({x},{z})");
                Assert.IsTrue(GenMat.IsSolid(D(c, x, c.height, z)), $"the ledge top at ({x},{z}) is not solid");
                Assert.IsTrue(GenMat.IsSolid(D(c, x, c.open3Lo - 1, z)), $"the ground under the ledge at ({x},{z}) is not solid");
                checkedN++;
            }
            var lines = new List<string> { $"ledge columns on a 16 m grid: {ledges} of {land} land ({100.0 * ledges / System.Math.Max(1, land):F2}%), {checkedN} checked voxel by voxel" };
            string dir = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Logs", "TerrainGen");
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.WriteAllLines(System.IO.Path.Combine(dir, "ledges.txt"), lines);
            Debug.Log("[Ledges] " + lines[0]);
            Assert.Greater(ledges, 20, "almost no ledges on the island");
        }

        [Test]
        public void Arch_HasAnOpening_UnderASolidDeck()
        {
            var arches = LandStamps(Stamps.Arch);
            Assert.Greater(arches.Count, 0);
            var a = arches[0];
            int x = (int)math.floor(a.pos.x), z = (int)math.floor(a.pos.y);
            var c = Col(x, z);
            Assert.Greater(c.open1Hi, c.open1Lo, "no opening at the arch's centre column");
            Assert.Less(c.open1Hi, c.height, "the opening reaches the deck top");
            Assert.AreEqual(GenMat.Air, D(c, x, (c.open1Lo + c.open1Hi) / 2, z));
            Assert.IsTrue(GenMat.IsSolid(D(c, x, c.height, z)), "the deck top must be solid");
            Assert.IsTrue(GenMat.IsSolid(D(c, x, c.open1Lo - 1, z)), "the ground under the arch must be solid");
        }

        [Test]
        public void CaveMouth_BreaksTheSurface_AndDescends()
        {
            var mouths = LandStamps(Stamps.CaveMouth);
            Assert.Greater(mouths.Count, 0);
            int tested = 0;
            foreach (var m in mouths)
            {
                int carved = 0, deepAir = 0;
                int g = SiteGround(m.pos);
                for (int dz = -120; dz <= 120; dz += 8)
                for (int dx = -120; dx <= 120; dx += 8)
                {
                    var c = S(m.pos.x + dx + 0.5f, m.pos.y + dz + 0.5f);
                    if (c.carvesTop) carved++;
                }
                // Walk down the tube's axis.
                for (int t = 100; t <= 900; t += 50)
                {
                    float3 e = new float3(m.pos.x, g + (45f + 25f * m.a) * 0.35f, m.pos.y);
                    float3 d = math.normalize(new float3(m.dir.x, -(0.6f + 0.25f * m.b), m.dir.y));
                    float3 p = e + d * t;
                    var c = S(p.x, p.z);
                    if ((int)p.y < c.height - 200 && D(c, (int)math.floor(p.x), (int)math.floor(p.y), (int)math.floor(p.z)) == GenMat.Air) deepAir++;
                }
                if (carved > 0 && deepAir > 0) tested++;
            }
            Assert.Greater(tested, 0, "no cave mouth both breaks the surface and reaches > 20 m down");
        }

        [Test]
        public void Volcano_CalderaHoldsLava_AtRest()
        {
            var vs = Stamps.VolcanoSite(in _spec, in _o);
            int vx = (int)math.floor(vs.x), vz = (int)math.floor(vs.y);
            var centre = Col(vx, vz);
            Assert.AreEqual(1, centre.waterKind, "the caldera centre has no lava");
            Assert.Greater(centre.water, centre.height);
            int level = centre.water;
            // Flat and contained: every column across the lake reports the same
            // level; the wall ring stands above it.
            for (int k = 0; k < 16; k++)
            {
                float2 u = new float2(math.cos(k * 0.3927f), math.sin(k * 0.3927f));
                var inLake = S(vs.x + u.x * Stamps.CALDERA_R * 0.5f, vs.y + u.y * Stamps.CALDERA_R * 0.5f);
                Assert.AreEqual(level, inLake.water, "the lava lake is not flat");
                var wall = S(vs.x + u.x * Stamps.CALDERA_R * 0.97f, vs.y + u.y * Stamps.CALDERA_R * 0.97f);
                Assert.Greater(wall.height, level, "the caldera wall does not contain the lava");
            }
            Assert.AreEqual(GenMat.Lava, D(centre, vx, level, vz));
            Assert.AreEqual(GenMat.Air, D(centre, vx, level + 1, vz));
        }

        [Test]
        public void WorldTree_HasTrunkCanopyAndAirBeneath()
        {
            if (!Stamps.WorldTreeEnabled)
                Assert.Ignore("the world tree is off until object placement is designed (owner, 2026-09-25)");
            var ts = Stamps.TreeSite(in _spec, in _o);
            int tx = (int)math.floor(ts.x), tz = (int)math.floor(ts.y);
            var under = Col(tx + 700, tz);
            Assert.Greater(under.open1Hi, under.open1Lo + 1000, "no void under the canopy");
            Assert.AreEqual(GenMat.Air, D(under, tx + 700, under.open1Lo + 500, tz));
            Assert.AreEqual(GenMat.Leaves, D(under, tx + 700, under.height, tz));
            var trunk = Col(tx + 150, tz + 100);
            Assert.AreEqual(GenMat.Bark, D(trunk, tx + 150, trunk.stampBodyLo + 800, tz + 100));
            // Root caves: the shaft under the trunk centre.
            var mid = Col(tx, tz);
            Assert.AreEqual(GenMat.Air, D(mid, tx, mid.stampBodyLo - 1 - 60, tz), "no root cave under the trunk");
        }

        [Test]
        public void GeyserPools_HoldStillWaterBelowTheTerrace()
        {
            var gs = Stamps.GeyserSite(in _spec, in _o);
            int gx0 = (int)math.floor(gs.x), gz0 = (int)math.floor(gs.y);
            int pools = 0;
            for (int dz = -600; dz <= 600; dz += 10)
            for (int dx = -600; dx <= 600; dx += 10)
            {
                var c = Col(gx0 + dx, gz0 + dz);
                if (c.water <= c.height || c.waterKind != 0) continue;
                pools++;
                // At rest: every non-pool neighbour column stands at or above the level.
                foreach (var (ox, oz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    var nb = Col(gx0 + dx + ox, gz0 + dz + oz);
                    if (nb.water <= nb.height) Assert.GreaterOrEqual(nb.height, c.water, $"pool at {dx},{dz} is not contained");
                }
            }
            Assert.Greater(pools, 20, "the geyser basin has no pools");
        }

        [Test]
        public void Mushrooms_StandInTheCaverns()
        {
            int found = 0;
            int c0x = _spec.worldSizeVoxels / 2 >> Stamps.MUSHROOM_CELL_SHIFT;
            for (int cz = c0x - 8; cz <= c0x + 8 && found == 0; cz++)
            for (int cx = c0x - 8; cx <= c0x + 8 && found == 0; cx++)
            for (int cy = _spec.UnderworldTopY >> Stamps.MUSHROOM_CELL_SHIFT; cy <= _spec.CavernTopY >> Stamps.MUSHROOM_CELL_SHIFT && found == 0; cy++)
            {
                // Probe the stem line of the cell's candidate.
                int bx = cx << Stamps.MUSHROOM_CELL_SHIFT, by = cy << Stamps.MUSHROOM_CELL_SHIFT, bz = cz << Stamps.MUSHROOM_CELL_SHIFT;
                for (int y = by; y < by + 1024 && found == 0; y += 16)
                for (int x = bx + 300; x < bx + 700 && found == 0; x += 16)
                for (int z = bz + 300; z < bz + 700 && found == 0; z += 16)
                    if (Stamps.Mushroom(in _spec, in _o, x, y, z) == GenMat.MushroomCap) found++;
            }
            Assert.Greater(found, 0, "no giant mushroom in 17 x 17 cavern columns around the centre");
        }

        /// Owner, 2026-09-25: "I don't like the tall cone shaped things ... too
        /// unnatural". No summit on the map may be a NEEDLE: at every local
        /// maximum of the map, the highest ground 40 m away (the ring of cells at
        /// Chebyshev distance 5) must be within NEEDLE_DROP of the summit. A
        /// dome, a mesa top, a ridge or a crater rim keeps high ground on that
        /// ring; a spire (25-45 m radius, 80-160 m tall) or a 50 m spike beacon
        /// drops by nearly its whole height. Two seeds.
        public const float NEEDLE_DROP = 250f;   // voxels (25 m)

        [Test]
        [Category("Slow")]   // > 10 s: SUITE=all only (task 11)
        public void NoNeedles_OnTheMap()
        {
            var lines = new List<string>();
            foreach (uint seed in new[] { 42u, 7u })
            {
                var spec = TerrainSpec.SizeClass2(seed);
                var map = SharedMaps.Get(spec);   // shared: never disposed here
                {
                    int n = spec.mapCells, summits = 0;
                    var worst = new List<(float drop, int x, int z)>();
                    var h = map.Height;
                    for (int z = 5; z < n - 5; z++)
                    for (int x = 5; x < n - 5; x++)
                    {
                        float c = h[z * n + x];
                        if (c <= 40f) continue;                          // land only
                        bool isMax = true;
                        for (int j = -1; j <= 1 && isMax; j++)
                        for (int i = -1; i <= 1 && isMax; i++)
                            if ((i != 0 || j != 0) && h[(z + j) * n + x + i] > c) isMax = false;
                        if (!isMax) continue;
                        summits++;
                        float ring = float.MinValue;
                        for (int k = -5; k <= 5; k++)
                        {
                            ring = math.max(ring, math.max(h[(z - 5) * n + x + k], h[(z + 5) * n + x + k]));
                            ring = math.max(ring, math.max(h[(z + k) * n + x - 5], h[(z + k) * n + x + 5]));
                        }
                        worst.Add((c - ring, x, z));
                    }
                    worst.Sort((a, b) => b.drop.CompareTo(a.drop));
                    lines.Add($"seed {seed}: {summits} summits on land; worst drops 40 m from a summit:");
                    var o = Offsets.For(seed);
                    for (int i = 0; i < math.min(8, worst.Count); i++)
                    {
                        // Which macro term built it (WorldMap.Macro's own fields).
                        float2 pp = new float2((worst[i].x + 0.5f) * spec.mapCellVoxels, (worst[i].z + 0.5f) * spec.mapCellVoxels);
                        float pf = Noise.Fbm(pp / 11500f + o.plateau, 3);
                        float range = Noise.SmoothStep(0.05f, 0.40f, Noise.Fbm(pp / 17000f + o.range, 3));
                        float peakD = math.distance(pp, map.PeakPosition) / 10f;
                        // The macro landform ALONE (before beacons and hydrology) at the
                        // summit and on its 40 m ring: a needle already here is a macro
                        // term; one that appears only in the final map was made later.
                        WorldMap.Macro(in spec, in o, map.PeakPosition, pp, out float mh, out _);
                        float mRing = float.MinValue;
                        for (int k = -5; k <= 5; k++)
                        foreach (var off in new[] { new int2(k, -5), new int2(k, 5), new int2(-5, k), new int2(5, k) })
                        {
                            float2 rp = new float2((worst[i].x + off.x + 0.5f) * spec.mapCellVoxels, (worst[i].z + off.y + 0.5f) * spec.mapCellVoxels);
                            WorldMap.Macro(in spec, in o, map.PeakPosition, rp, out float rh, out _);
                            mRing = math.max(mRing, rh);
                        }
                        float vf = math.abs(Noise.Fbm(pp / 9500f + o.valley, 3));
                        lines.Add($"   {worst[i].drop / 10f,6:F1} m at map cell ({worst[i].x},{worst[i].z}), summit {h[worst[i].z * n + worst[i].x] / 10f:F0} m" +
                                  $"   macro alone: summit {mh / 10f:F0} m, drop {(mh - mRing) / 10f:F1} m   valley field {vf:F3} (cut < 0.070)" +
                                  $"   plateau {pf:F3}  range {range:F2}  {peakD:F0} m from the peak");
                    }
                    File.WriteAllLines(Path.Combine(Dir(), $"needles_{seed}.txt"), lines);
                    Assert.Less(worst.Count > 0 ? worst[0].drop : 0f, NEEDLE_DROP,
                        $"seed {seed}: a needle -- the summit at map cell ({worst[0].x},{worst[0].z}) falls {worst[0].drop / 10f:F1} m within 40 m on every side");
                }
            }
            File.WriteAllLines(Path.Combine(Dir(), "needles.txt"), lines);
            Debug.Log("[Stamps] needle check\n" + string.Join("\n", lines));
        }

        /// The crystal clusters that replaced the spires stay squat: every
        /// crystal column is 2.5-6 m of crystal on its own ground.
        [Test]
        public void CrystalClusters_AreSquat()
        {
            int cols = 0, tallest = 0;
            foreach (var s in LandStamps(Stamps.CrystalCluster))
            {
                for (int dz = -220; dz <= 220; dz += 5)
                for (int dx = -220; dx <= 220; dx += 5)
                {
                    int x = (int)math.floor(s.pos.x) + dx, z = (int)math.floor(s.pos.y) + dz;
                    var c = Col(x, z);
                    if (c.stampMat < GenMat.CrystalBlue || c.stampMat > GenMat.CrystalGreen) continue;
                    int tall = c.height - c.stampBodyLo + 1;
                    tallest = math.max(tallest, tall);
                    Assert.LessOrEqual(tall, 60, $"crystal column at ({x},{z}) is {tall / 10f} m tall");
                    cols++;
                }
            }
            Debug.Log($"[Stamps] crystal clusters: {cols} crystal columns sampled, tallest {tallest / 10f:F1} m");
            Assert.Greater(cols, 20, "no crystal cluster columns found on land");
        }

        [Test]
        public void Lod0_HoldsOverStampColumns()
        {
            int checkedCols = 0, skipped = 0;
            foreach (byte k in new[] { Stamps.Arch, Stamps.Hoop, Stamps.StoneRing, Stamps.Fossil, Stamps.CrystalCluster, Stamps.CaveMouth, Stamps.Fissure })
            {
                foreach (var s in LandStamps(k))
                {
                    for (int i = -3; i <= 3; i++)
                    {
                        float2 p = s.pos + s.dir * (i * 45f) + new float2(-s.dir.y, s.dir.x) * (i * 7f);
                        int x = (int)math.floor(p.x), z = (int)math.floor(p.y), top = int.MinValue;
                        var c = Col(x, z);
                        if (c.carvesTop) { skipped++; continue; }
                        for (int y = c.height + 40; y > c.height - 800; y--)
                            if (GenMat.IsSolid(D(c, x, y, z))) { top = y; break; }
                        Assert.AreEqual(c.height, top, $"stamp kind {k} at ({x},{z}): S {c.height}, D's top solid {top}");
                        checkedCols++;
                    }
                    if (checkedCols > 400) break;
                }
            }
            Debug.Log($"[Stamps] LOD-0 over stamp columns: {checkedCols} checked, {skipped} carvesTop columns excluded");
            Assert.Greater(checkedCols, 100);
        }

        private static string Dir()
        {
            string d = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "TerrainGen");
            Directory.CreateDirectory(d);
            return d;
        }
    }
}
