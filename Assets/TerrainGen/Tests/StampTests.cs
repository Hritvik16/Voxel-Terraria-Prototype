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
            _map = WorldMap.Build(_spec);
        }

        private static ColumnSample S(float x, float z) { var v = _map.View; return ShapeField.Sample(in _spec, in v, in _o, x, z, 1); }
        /// The column (x, z) exactly as the chunk generator samples it: S at the
        /// voxel CENTRE, D at the integer coordinate. Mixing floor(x) and
        /// floor(x + 0.5) compares two different columns.
        private static ColumnSample Col(int x, int z) => S(x + 0.5f, z + 0.5f);
        private static byte D(in ColumnSample c, int x, int y, int z)
        {
            var cd = DetailField.PrepareColumn(in _o, x, z);
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
                                       Stamps.CrystalSpires, Stamps.MeteorCrater, Stamps.Fossil })
            {
                int n = LandStamps(k).Count;
                lines.Add($"kind {k,2}: {n} on land");
                Assert.Greater(n, 0, $"stamp kind {k} never lands on the island");
            }
            var vs = Stamps.VolcanoSite(in _spec, in _o);
            float rim = SiteGround(vs + new float2(Stamps.CALDERA_R * 1.2f, 0f)) - _spec.seaLevelY;
            lines.Add($"volcano at {vs}: flank {rim / 10f:F0} m above sea 120 m out from the caldera");
            Assert.Greater(rim, 2000f, "the volcano's flank is not a mountain");
            var ts = Stamps.TreeSite(in _spec, in _o);
            var tc = S(ts.x + 400f, ts.y);
            lines.Add($"world tree at {ts}: canopy top {(tc.height - _spec.seaLevelY) / 10f:F0} m above sea");
            Assert.Greater(tc.height - SiteGround(ts + new float2(1500f, 0f)), 2000, "the tree is not 200+ m tall");
            var gs = Stamps.GeyserSite(in _spec, in _o);
            Assert.Greater(SiteGround(gs), _spec.seaLevelY + 20, "the geyser basin is not on land");
            File.WriteAllLines(Path.Combine(Dir(), "stamps.txt"), lines);
            Debug.Log("[Stamps]\n" + string.Join("\n", lines));
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

        [Test]
        public void Lod0_HoldsOverStampColumns()
        {
            int checkedCols = 0, skipped = 0;
            foreach (byte k in new[] { Stamps.Arch, Stamps.Hoop, Stamps.StoneRing, Stamps.Fossil, Stamps.CrystalSpires, Stamps.CaveMouth, Stamps.Fissure })
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
