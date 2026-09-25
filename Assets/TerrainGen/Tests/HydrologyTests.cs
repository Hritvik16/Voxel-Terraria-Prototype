// Assets/TerrainGen/Tests/HydrologyTests.cs
//
// Map stage v2 (Hydrology.cs) oracles, on the sizeClass 2 map (seed 42):
//   Drainage   every land cell's flood-parent chain reaches the ocean, no loops
//   Lakes      each lake is FLAT, CONTAINED (no neighbour below its level) and
//              FILLED TO SPILL (some neighbour sits at its level: the outlet)
//   Rivers     exist, and every river cell's parent chain ends in the sea
//   S/D        S reports the level, D puts still water exactly over (height, level]
// Report: Logs/TerrainGen/hydrology.txt.
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace TerrainGen.Tests
{
    public class HydrologyTests
    {
        private static TerrainSpec _spec;
        private static Offsets _o;
        private static WorldMap _map;
        private static int[] _parent;

        [OneTimeSetUp]
        public void BuildOnce()
        {
            if (_map != null) return;
            _spec = TerrainSpec.SizeClass2(42);
            _o = Offsets.For(42);
            _map = WorldMap.Build(_spec);
            // Re-derive the drainage parents on the FINAL heights (the build keeps
            // only the products). Same function, same determinism.
            int n = _spec.mapCells;
            _parent = new int[n * n];
            Hydrology.Flood(n, _map.Height, Hydrology.FLOOD_EPS, new float[n * n], _parent, new int[n * n]);
        }

        private static int N => _spec.mapCells;

        private static bool ReachesSea(int i, out int steps)
        {
            steps = 0;
            int limit = N * N;
            while (i >= 0 && steps <= limit)
            {
                if (_map.Height[i] <= 0f) return true;
                i = _parent[i]; steps++;
            }
            return false;
        }

        [Test]
        public void EveryLandCell_DrainsToTheSea()
        {
            var rng = new Unity.Mathematics.Random(11);
            int land = 0, maxSteps = 0;
            for (int k = 0; k < 20000; k++)
            {
                int i = rng.NextInt(0, N * N);
                if (_map.Height[i] <= 0f) continue;
                land++;
                Assert.IsTrue(ReachesSea(i, out int s), $"cell {i % N},{i / N} does not drain to the sea");
                maxSteps = math.max(maxSteps, s);
            }
            Assert.Greater(land, 1000);
        }

        [Test]
        public void Lakes_AreFlat_Contained_AndFilledToSpill()
        {
            int n = N;
            var seen = new bool[n * n];
            var stack = new Stack<int>();
            int lakes = 0;
            for (int i0 = 0; i0 < n * n; i0++)
            {
                if (seen[i0] || !Hydrology.IsLake(i0, _map.WaterClass)) continue;
                lakes++;
                float level = _map.Water[i0];
                bool outlet = false; float lowestRim = float.MaxValue; int size = 0;
                stack.Push(i0); seen[i0] = true;
                while (stack.Count > 0)
                {
                    int c = stack.Pop(); size++;
                    Assert.AreEqual(level, _map.Water[c], 1e-4f, "a lake must be flat");
                    int cx = c % n, cz = c / n;
                    for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int x = cx + dx, z = cz + dz;
                        if ((dx == 0 && dz == 0) || x < 0 || z < 0 || x >= n || z >= n) continue;
                        int nb = z * n + x;
                        if (Hydrology.IsLake(nb, _map.WaterClass) && math.abs(_map.Water[nb] - level) < 1e-4f)
                        { if (!seen[nb]) { seen[nb] = true; stack.Push(nb); } continue; }
                        // A rim cell: its GROUND holds the lake in, unless it is the
                        // outlet. (Shallower-than-LAKE_MIN_DEPTH margin cells and river
                        // cells are rims too; their ground is what matters.)
                        lowestRim = math.min(lowestRim, _map.Height[nb]);
                        if (_map.Height[nb] <= level + 1e-3f) outlet = true;
                    }
                }
                Assert.GreaterOrEqual(lowestRim, level - Hydrology.LAKE_MIN_DEPTH - 1e-3f,
                    $"lake at {i0 % n},{i0 / n} (level {level}) leaks: a rim cell sits at {lowestRim}");
                Assert.IsTrue(outlet, $"lake at {i0 % n},{i0 / n} is below its spill level (no rim cell at its level)");
            }
            Assert.Greater(lakes, 0, "map v2 produced no lakes at all");
        }

        [Test]
        public void Rivers_Exist_AndReachTheSea()
        {
            var h = _map.Hydro;
            Assert.Greater(h.riverCells, 500, "almost no river cells");
            Assert.Greater(h.riverMouths, 3, "fewer than 4 rivers reach the sea");
            int checkedN = 0;
            for (int i = 0; i < N * N; i++)
            {
                if (_map.WaterClass[i] != Hydrology.RIVER) continue;
                checkedN++;
                Assert.IsTrue(ReachesSea(i, out _), $"river cell {i % N},{i / N} does not reach the sea");
            }
            Assert.Greater(checkedN, 50);
        }

        [Test]
        public void ShapeAndDetail_PutStillWaterOverTheColumn()
        {
            var v = _map.View;
            int found = 0;
            for (int i = 0; i < N * N && found < 40; i += 131)
            {
                if (!Hydrology.IsLake(i, _map.WaterClass)) continue;
                float x = (i % N + 0.5f) * _spec.mapCellVoxels, z = (i / N + 0.5f) * _spec.mapCellVoxels;
                var c = ShapeField.Sample(in _spec, in v, in _o, x, z, 1);
                if (c.water <= c.height) continue;   // a lake cell whose column rose above its level (detail)
                found++;
                var cd = DetailField.PrepareColumn(in _o, (int)x, (int)z);
                Assert.AreEqual(GenMat.Water, DetailField.Material(in _spec, in _o, in c, in cd, 0f, (int)x, c.water, (int)z));
                Assert.AreEqual(GenMat.Air, DetailField.Material(in _spec, in _o, in c, in cd, 0f, (int)x, c.water + 1, (int)z));
                Assert.AreNotEqual(GenMat.Water, DetailField.Material(in _spec, in _o, in c, in cd, 0f, (int)x, c.height, (int)z));
            }
            Assert.Greater(found, 5, "too few lake columns found to test");
        }

        [Test]
        public void Report_Hydrology()
        {
            var h = _map.Hydro;
            var lines = new List<string>
            {
                $"map v2 hydrology (sizeClass 2, seed 42), built in {h.ms:F0} ms (managed):",
                $"  land cells {h.landCells:N0}; river cells {h.riverCells:N0} ({h.riverCells * 8 / 1000.0:F1} km of 8 m cells); river mouths {h.riverMouths}",
                $"  lakes {h.lakes} covering {h.lakeCells:N0} cells ({h.lakeCells * 64 / 1e6:F2} km^2)",
                $"  deepest erosion carve {h.maxCarve / 10f:F1} m; largest catchment {h.maxCatchmentKm2:F2} km^2",
            };
            Debug.Log("[Hydrology]\n" + string.Join("\n", lines));
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "TerrainGen");
            Directory.CreateDirectory(dir);
            File.WriteAllLines(Path.Combine(dir, "hydrology.txt"), lines);
        }
    }
}
