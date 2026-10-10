// Assets/TerrainGen/Tests/LakeShoreTests.cs
//
// Task 42 (owner Q35 3 (a), 2026-10-09): a lake's shoreline follows the ground,
// not the 8 m map grid. Task 37e's at-rest fix ended every lake exactly at the
// edge of the 2x2 "dual cells" that hold its water (ShapeField.SampleWater:
// boundaries at x = 40 + 80k voxels), so seed 42's lake became a rectangle.
//
// Measure, per seed, over the shore edges (a lake water column beside a dry
// column, 4-neighbours) in every dual tile near a lake:
//   - ON GRID: the share of shore edges within GRID_BAND voxels of a dual-cell
//     boundary line. A shore that ignores the grid lands there by chance
//     (2 x GRID_BAND + 1) / 80 of the time (6.25%).
//   - STRAIGHT: the share of shore edges in an axis-aligned straight run of
//     RUN or more voxels (2.4 m) -- "no edge axis-aligned over more than a
//     few voxels".
// Lake water = column water above its ground, water kind 0, at the lake level
// of the column (ShapeField.LakeWaterY, the rule LakeRim uses). Bounded
// like LakeLevelTests: one seed per case, map-driven tiles, 81 x 81 column
// samples per tile in a stack buffer, no voxel loop. Report:
// Logs/TerrainGen/lake_shore_<seed>.txt.
using System.IO;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace TerrainGen.Tests
{
    public class LakeShoreTests
    {
        private const int K = 4;            // per tile: 0 lake columns, 1 shore edges, 2 on grid, 3 in straight runs
        public const int GRID_BAND = 2;     // voxels either side of a dual-cell boundary line
        public const int RUN = 24;          // voxels: a straight run this long counts as grid-like
        // Pass lines (see HANDOFF 42): ON GRID at most twice chance, STRAIGHT at most 5%.
        public const float MAX_ON_GRID = 2f * (2 * GRID_BAND + 1) / 80f;
        public const float MAX_STRAIGHT = 0.05f;

        [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High, CompileSynchronously = true)]
        private struct ShoreJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float> h, r, w;
            [ReadOnly] public NativeArray<byte> cls;
            [ReadOnly] public NativeArray<int2> tiles;
            public TerrainSpec spec; public Offsets o;
            [NativeDisableParallelForRestriction] public NativeArray<int> counts;   // tiles x K

            private const int CV = 80, G = CV + 1;

            public unsafe void Execute(int t)
            {
                var map = new MapView
                {
                    height = (float*)h.GetUnsafeReadOnlyPtr(), rough = (float*)r.GetUnsafeReadOnlyPtr(),
                    water = (float*)w.GetUnsafeReadOnlyPtr(), waterClass = (byte*)cls.GetUnsafeReadOnlyPtr(),
                    cells = spec.mapCells, cellVoxels = spec.mapCellVoxels,
                };
                int2 tile = tiles[t];
                int x0 = tile.x * CV + CV / 2, z0 = tile.y * CV + CV / 2;   // the tile's first column: on a boundary line
                byte* f = stackalloc byte[G * G];   // 1 lake water, 2 dry land, 0 other (river, sea, lava)
                int b = t * K;
                for (int k = 0; k < G; k++)
                for (int i = 0; i < G; i++)
                {
                    int x = x0 + i, z = z0 + k;
                    var c = ShapeField.Sample(in spec, in map, in o, x + 0.5f, z + 0.5f, 1);
                    byte v = 0;
                    if (c.water <= c.height) v = c.height >= spec.seaLevelY ? (byte)2 : (byte)0;
                    else if (c.waterKind == 0 && c.water == ShapeField.LakeWaterY(in spec, in map, in o, x + 0.5f, z + 0.5f)) v = 1;
                    f[k * G + i] = v;
                    if (v == 1 && i < CV && k < CV) counts[b + 0]++;
                }
                // x-edges: between columns i and i+1 of row k; the edge sits at
                // tile coordinate i+1, the boundary lines at 0 and 80.
                for (int k = 0; k < CV; k++)
                {
                    for (int i = 0; i < CV; i++) Edge(f[k * G + i], f[k * G + i + 1], i + 1, b);
                }
                for (int k = 0; k < CV; k++)
                for (int i = 0; i < CV; i++) Edge(f[k * G + i], f[(k + 1) * G + i], k + 1, b);
                // Straight runs: the same edge (same side wet) on consecutive rows.
                for (int i = 0; i < CV; i++)
                {
                    int run = 0, dir = 0;
                    for (int k = 0; k <= CV; k++)
                    {
                        int d = k < CV ? Dir(f[k * G + i], f[k * G + i + 1]) : 0;
                        if (d != 0 && d == dir) run++;
                        else { if (run >= RUN) counts[b + 3] += run; run = d != 0 ? 1 : 0; dir = d; }
                    }
                }
                for (int k = 0; k < CV; k++)
                {
                    int run = 0, dir = 0;
                    for (int i = 0; i <= CV; i++)
                    {
                        int d = i < CV ? Dir(f[k * G + i], f[(k + 1) * G + i]) : 0;
                        if (d != 0 && d == dir) run++;
                        else { if (run >= RUN) counts[b + 3] += run; run = d != 0 ? 1 : 0; dir = d; }
                    }
                }
            }

            private static int Dir(byte a, byte c) => a == 1 && c == 2 ? 1 : a == 2 && c == 1 ? -1 : 0;

            private void Edge(byte a, byte c, int pos, int b)
            {
                if (Dir(a, c) == 0) return;
                counts[b + 1]++;
                if (math.min(pos, CV - pos) <= GRID_BAND) counts[b + 2]++;
            }
        }

        [Category("Slow")]   // one shared v4 map per case
        [TestCase(42u)]
        [TestCase(7u)]
        [TestCase(2026u)]
        [TestCase(31337u)]
        public void LakeShore_IsNotGridAligned(uint seed)
        {
            var spec = TerrainSpec.SizeClass2(seed, 4);
            var o = Offsets.For(in spec);
            var map = SharedMaps.Get(spec);   // shared: never disposed or written here
            Assert.AreEqual(80, spec.mapCellVoxels, "the shore job's stack buffer assumes 8 m cells");
            int n = spec.mapCells;

            // Tiles: dual cell (ix, iz) with a lake cell within two cells of its
            // corners whose window is not one lake at one level.
            var list = new NativeList<int2>(Allocator.TempJob);
            for (int iz = 0; iz < n - 1; iz++)
            for (int ix = 0; ix < n - 1; ix++)
            {
                bool lake = false, uniform = true; float lv = float.NaN;
                for (int j = -2; j <= 3; j++)
                for (int i = -2; i <= 3; i++)
                {
                    int c = math.clamp(iz + j, 0, n - 1) * n + math.clamp(ix + i, 0, n - 1);
                    bool isLake = map.WaterClass[c] == Hydrology.LAKE;
                    lake |= isLake;
                    if (!isLake) uniform = false;
                    else if (float.IsNaN(lv)) lv = map.Water[c];
                    else if (map.Water[c] != lv) uniform = false;
                }
                if (lake && !uniform) list.Add(new int2(ix, iz));
            }

            var tiles = list.AsArray();
            var hA = new NativeArray<float>(map.Height, Allocator.TempJob);
            var rA = new NativeArray<float>(map.Rough, Allocator.TempJob);
            var wA = new NativeArray<float>(map.Water, Allocator.TempJob);
            var cA = new NativeArray<byte>(map.WaterClass, Allocator.TempJob);
            var counts = new NativeArray<int>(tiles.Length * K, Allocator.TempJob);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var tot = new long[K];
            int nt = tiles.Length, uniformTiles = 0;
            for (int iz = 0; iz < n - 1; iz++)
            for (int ix = 0; ix < n - 1; ix++)
            {
                // Tiles left out above that are one lake at one level: all water (reach > 0.71 cell).
                bool all = true; float lv = map.Water[iz * n + ix];
                for (int j = -2; j <= 3 && all; j++)
                for (int i = -2; i <= 3; i++)
                {
                    int c = math.clamp(iz + j, 0, n - 1) * n + math.clamp(ix + i, 0, n - 1);
                    if (map.WaterClass[c] != Hydrology.LAKE || map.Water[c] != lv) { all = false; break; }
                }
                if (all) uniformTiles++;
            }
            try
            {
                new ShoreJob { h = hA, r = rA, w = wA, cls = cA, tiles = tiles, spec = spec, o = o, counts = counts }
                    .Schedule(tiles.Length, 4).Complete();
                for (int t = 0; t < tiles.Length; t++)
                for (int s = 0; s < K; s++) tot[s] += counts[t * K + s];
            }
            finally
            {
                counts.Dispose(); hA.Dispose(); rA.Dispose(); wA.Dispose(); cA.Dispose(); list.Dispose();
            }
            float onGrid = tot[1] > 0 ? (float)tot[2] / tot[1] : 0f, straight = tot[1] > 0 ? (float)tot[3] / tot[1] : 0f;
            string rep = $"seed {seed} v4  map lakes {map.Hydro.lakes}  lake cells {map.Hydro.lakeCells} ({map.Hydro.lakeCells * 64e-4:F1} ha)  tiles {nt}  ({sw.Elapsed.TotalSeconds:F1} s)\n" +
                         $"  lake water columns {tot[0]} ({tot[0] * 1e-6:F2} ha) + {uniformTiles} all-lake tiles = {(tot[0] + uniformTiles * 6400L) * 1e-6:F2} ha in all  shore edges {tot[1]} ({tot[1] * 0.1:F0} m)\n" +
                         $"  ON GRID (within {GRID_BAND} voxels of a dual-cell line) {tot[2]} = {onGrid * 100f:F2}%  (chance {(2 * GRID_BAND + 1) / 80f * 100f:F2}%, pass <= {MAX_ON_GRID * 100f:F2}%)\n" +
                         $"  STRAIGHT (axis-aligned runs >= {RUN} voxels) {tot[3]} = {straight * 100f:F2}%  (pass <= {MAX_STRAIGHT * 100f:F2}%)";
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "TerrainGen");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, $"lake_shore_{seed}.txt"), rep);
            UnityEngine.Debug.Log(rep);
            Assert.Greater(tot[1], 0, $"seed {seed}: no lake shore found");
            Assert.LessOrEqual(onGrid, MAX_ON_GRID, $"seed {seed}: lake shore follows the map grid (Logs/TerrainGen/lake_shore_{seed}.txt)");
            Assert.LessOrEqual(straight, MAX_STRAIGHT, $"seed {seed}: lake shore has long straight edges (Logs/TerrainGen/lake_shore_{seed}.txt)");
        }
    }
}
