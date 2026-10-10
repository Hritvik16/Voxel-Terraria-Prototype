// Assets/TerrainGen/Tests/CaveTests.cs
//
// TerrainGen v3 T4 (design doc §13): STRUCTURED caves, no 3-D noise (owner,
// 2026-09-25: replace the noise caves). Every cave is a per-column open
// interval (DetailField.PrepareColumn); these tests sample the island on a
// 6.4 m grid and check that each structure exists, sits where it should, and
// that D's voxels agree with the intervals it was given.
//   Tunnels   surface-following passages 18-30 m down, and two deep layers
//   Caverns   halls in the cavern layer (150-400 m below sea)
//   Chamber   the underworld's open chamber, over a lava sea at rest
// Report: Logs/TerrainGen/caves.txt.
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace TerrainGen.Tests
{
    public class CaveTests
    {
        private static TerrainSpec _spec;
        private static Offsets _o;
        private static WorldMap _map;

        [OneTimeSetUp]
        public void BuildOnce()
        {
            if (_map != null) return;
            _spec = TerrainSpec.SizeClass2(42, 3);
            _o = Offsets.For(42);
            _map = SharedMaps.Get(_spec);
        }

        private static bool InPocket(int x, int y, int z)
        {
            byte p = DetailField.Pocket(in _spec, in _o, x, y, z, out bool shell);
            return p != 255 || shell;
        }

        [Test]
        public void StructuredCaves_ExistAndMatchTheirIntervals()
        {
            int cols = 0, tun0 = 0, tunDeep = 0, cav = 0, under = 0, lava = 0, checkedVoxels = 0;
            var v = _map.View;
            float c = _spec.worldSizeVoxels * 0.5f;
            for (int gz = -150; gz < 150; gz++)
            for (int gx = -150; gx < 150; gx++)
            {
                int x = (int)c + gx * 64, z = (int)c + gz * 64;
                var col = ShapeField.Sample(in _spec, in v, in _o, x + 0.5f, z + 0.5f, 1);
                if (col.height < _spec.seaLevelY + 50) continue;          // land only
                cols++;
                var cd = DetailField.PrepareColumn(in _spec, in _o, x, z);
                if (cd.tun0 < 0.5f) tun0++;
                if (cd.tun1 < 0.5f || cd.tun2 < 0.5f) tunDeep++;
                if (cd.cav0Hi > cd.cav0Lo) cav++;
                if (cd.underHi > cd.underLo) under++;
                if (cd.underHi > cd.underLo && cd.underLo <= _spec.worldBottomY + DetailField.LAVA_ABOVE_FLOOR) lava++;
                // D agrees with the intervals (away from pockets, which win).
                if (cd.cav0Hi > cd.cav0Lo + 4 && (gx + gz) % 7 == 0)
                {
                    int y = (cd.cav0Lo + cd.cav0Hi) / 2;
                    if (InPocket(x, y, z)) continue;               // pockets and their shells win over caves
                    byte m = DetailField.Material(in _spec, in _o, in col, in cd, 0f, x, y, z);
                    Assert.IsTrue(m == GenMat.Air || m == GenMat.MushroomStem || m == GenMat.MushroomCap || m < 10,
                                  $"cavern interval at ({x},{y},{z}) holds material {m}");
                    checkedVoxels++;
                }
                if (cd.tun0 < 0.3f && (gx + gz) % 5 == 0)
                {
                    int y = col.height - (int)cd.tunY0;
                    if (col.height - y > DetailField.CARVE_MIN_DEPTH && !InPocket(x, y, z) && !col.stamp3D)
                    {
                        byte m = DetailField.Material(in _spec, in _o, in col, in cd, 0f, x, y, z);
                        Assert.IsTrue(m == GenMat.Air || m < 10, $"tunnel centre at ({x},{y},{z}) holds material {m}");
                        checkedVoxels++;
                    }
                }
            }
            var lines = new List<string>
            {
                $"land columns sampled (6.4 m grid): {cols}",
                $"surface-following tunnel over the column (|line| < 0.5 width): {100.0 * tun0 / cols:F1}%",
                $"deep tunnel over the column: {100.0 * tunDeep / cols:F1}%",
                $"cavern through the column: {100.0 * cav / cols:F1}%",
                $"underworld chamber open: {100.0 * under / cols:F1}% (lava below it: {100.0 * lava / cols:F1}%)",
                $"voxels checked against their interval: {checkedVoxels}",
            };
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "TerrainGen");
            Directory.CreateDirectory(dir);
            File.WriteAllLines(Path.Combine(dir, "caves.txt"), lines);
            Debug.Log("[Caves]\n" + string.Join("\n", lines));
            // Passages are 2-4 m wide and a layer's centre lines run ~100-200 m apart,
            // so ~1% of columns on a 6.4 m grid sit within half a width of one
            // (measured 0.9% / 1.7%). 0.5% catches a layer that vanished.
            Assert.Greater(tun0, cols / 200, "almost no surface-following tunnels");
            Assert.Greater(tunDeep, cols / 200, "almost no deep tunnels");
            Assert.Greater(cav, cols / 100, "almost no caverns");
            Assert.Greater(under, cols / 4, "the underworld chamber is mostly closed");
            Assert.Greater(checkedVoxels, 20);
        }
    }
}
