// Assets/TerrainGen/Tests/LakeLevelTests.cs
//
// Task 37e (owner Q31 part 2 (a), 2026-10-09): every generated lake has ONE flat
// water level and is AT REST -- no lake water voxel has air beside it (the four
// side neighbours), directly below it, or diagonally below it (the four cells
// beside the one below; task 39's -poolprobe measure: 85-90k such cells at seed
// 42's lake shore, and touching that lake woke ~500k fluid slots).
//
// BOUNDED BY CONSTRUCTION (the first version of this test grew Unity to 22.5 GB;
// rewritten from scratch, owner 2026-10-09). One seed per test case. The MAP
// picks the work: the columns are tiled by the 2x2 "dual cells" of the map
// (ShapeField.SampleWater), lake water reaches at most ShapeField.REACH_MAX
// (< 1.5 cells) from a lake cell's centre (task 42), so only a tile whose 4x4
// cell window holds a LAKE cell and is not one lake at one level (a lake's
// shore, or two levels meeting) can hold an unsettled lake voxel. Per tile, S is sampled once on its (cv + 2)^2
// columns into stack buffers; voxels are classed from those column samples
// (air above max(ground, water, sea), solid at or below the ground) and
// DetailField.Material is called only where a voxel COULD be carved: a column
// with a stamp opening / 3-D stamp, or a voxel more than CARVE_MIN_DEPTH under
// its column top (caves). Nothing scales with lake cells x voxel height.
//
// Kinds: "lake" = the column's water is the lake water there (ShapeField.LakeWaterY);
// "geyser" = the geyser basin's sinter pools (Stamps.Column; reported, gated
// too). Rivers are not at rest by design (Hydrology.cs header) and the
// volcano's lava lake is not covered. Report: Logs/TerrainGen/lake_level_<seed>.txt.
using System.IO;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace TerrainGen.Tests
{
    public class LakeLevelTests
    {
        // Per tile and kind: 0 water columns, 1 water voxels, 2 unsettled voxels
        // (any of 3-5), 3 air beside, 4 air directly below, 5 air diagonally
        // below, 6 exact (Material) voxel checks made, 7 unsettled voxels whose
        // air is CARVED (a cave or stamp opening, not open sky over a low column).
        private const int K = 8, KINDS = 2, LAKE = 0, GEYSER = 1;

        [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High, CompileSynchronously = true)]
        private struct TileJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float> h, r, w;
            [ReadOnly] public NativeArray<byte> cls;
            [ReadOnly] public NativeArray<int2> tiles;
            public TerrainSpec spec; public Offsets o;
            [NativeDisableParallelForRestriction] public NativeArray<int> counts;   // tiles x KINDS x K

            private const int CV = 80, G = CV + 2;

            public unsafe void Execute(int t)
            {
                var map = new MapView
                {
                    height = (float*)h.GetUnsafeReadOnlyPtr(), rough = (float*)r.GetUnsafeReadOnlyPtr(),
                    water = (float*)w.GetUnsafeReadOnlyPtr(), waterClass = (byte*)cls.GetUnsafeReadOnlyPtr(),
                    cells = spec.mapCells, cellVoxels = spec.mapCellVoxels,
                };
                int n = spec.mapCells;
                int2 tile = tiles[t];
                int x0 = tile.x * CV + CV / 2 - 1, z0 = tile.y * CV + CV / 2 - 1;   // the border column
                int* hg = stackalloc int[G * G];
                int* tp = stackalloc int[G * G];     // max(ground, water, sea): every voxel above it is air
                byte* fl = stackalloc byte[G * G];   // 1 lake water, 2 geyser water, 4 may be carved below its top
                for (int k = 0; k < G; k++)
                for (int i = 0; i < G; i++)
                {
                    int x = x0 + i, z = z0 + k, gi = k * G + i;
                    var c = ShapeField.Sample(in spec, in map, in o, x + 0.5f, z + 0.5f, 1);
                    hg[gi] = c.height;
                    tp[gi] = math.max(c.height, math.max(c.water, spec.seaLevelY));
                    byte f = 0;
                    if (c.open1Hi > c.open1Lo || c.open2Hi > c.open2Lo || c.open3Hi > c.open3Lo || c.stamp3D || c.carvesTop) f |= 4;
                    if (c.water > c.height && c.waterKind == 0)
                    {
                        if (c.water == ShapeField.LakeWaterY(in spec, in map, in o, x + 0.5f, z + 0.5f)) f |= 1;
                        else if (c.stampMat == GenMat.Sinter) f |= 2;
                    }
                    fl[gi] = f;
                }

                for (int k = 1; k <= CV; k++)
                for (int i = 1; i <= CV; i++)
                {
                    int gi = k * G + i;
                    int kind = (fl[gi] & 1) != 0 ? LAKE : (fl[gi] & 2) != 0 ? GEYSER : -1;
                    if (kind < 0) continue;
                    int b = (t * KINDS + kind) * K;
                    int x = x0 + i, z = z0 + k, hc = hg[gi], wc = tp[gi], exact = 0;
                    counts[b + 0]++;
                    counts[b + 1] += wc - hc;
                    bool belowAir = (fl[gi] & 4) != 0 && IsAir(in map, x, hc, z, ref exact);
                    for (int y = hc + 1; y <= wc; y++)
                    {
                        bool beside = false, diag = false, carved = false, below = y == hc + 1 && belowAir;
                        for (int d = 0; d < 4; d++)
                        {
                            int dx = d == 0 ? 1 : d == 1 ? -1 : 0, dz = d == 2 ? 1 : d == 3 ? -1 : 0;
                            int ni = gi + dz * G + dx;
                            int a0 = beside ? 0 : Air(in map, hg, tp, fl, ni, x + dx, y, z + dz, ref exact);
                            int a1 = diag ? 0 : Air(in map, hg, tp, fl, ni, x + dx, y - 1, z + dz, ref exact);
                            beside |= a0 != 0; diag |= a1 != 0;
                            carved |= a0 == 2 || a1 == 2;
                        }
                        if (carved || below) counts[b + 7]++;
                        if (beside) counts[b + 3]++;
                        if (below) counts[b + 4]++;
                        if (diag) counts[b + 5]++;
                        if (beside || below || diag) counts[b + 2]++;
                    }
                    counts[b + 6] += exact;
                }
            }

            /// Is voxel (x, y, z) air? From the column samples where they decide
            /// it; DetailField.Material only where a cave or stamp could open it.
            /// 0 not air, 1 air over the column's top, 2 carved air.
            private unsafe int Air(in MapView map, int* hg, int* tp, byte* fl, int gi, int x, int y, int z, ref int exact)
            {
                if (y > tp[gi]) return 1;
                if (y > hg[gi]) return 0;                                     // water (lake, river, sea) or lava
                if ((fl[gi] & 4) == 0 && hg[gi] - y <= DetailField.CARVE_MIN_DEPTH) return 0;
                return IsAir(in map, x, y, z, ref exact) ? 2 : 0;
            }

            private bool IsAir(in MapView map, int x, int y, int z, ref int exact)
            {
                exact++;
                var c = ShapeField.Sample(in spec, in map, in o, x + 0.5f, z + 0.5f, 1);
                return DetailField.Material(in spec, in o, in c, 0f, x, y, z) == GenMat.Air;
            }
        }

        /// On a failure, for the report: in the worst tile, the first few lake
        /// columns with a low neighbour (open sky), a carved neighbour voxel or
        /// a carved voxel under them, with the samples and the stamp there.
        /// Managed and one tile only.
        private static unsafe string Diag(TerrainSpec spec, Offsets o, WorldMap wm, int2 tile)
        {
            var map = wm.View; int n = spec.mapCells;
            int x0 = tile.x * 80 + 40, z0 = tile.y * 80 + 40, hits = 0, carvedHits = 0, belowHits = 0; var sb = new System.Text.StringBuilder();
            for (int k = 0; k < 80; k++)
            for (int i = 0; i < 80; i++)
            {
                int x = x0 + i, z = z0 + k;
                var c = ShapeField.Sample(in spec, in map, in o, x + 0.5f, z + 0.5f, 1);
                if (c.water <= c.height || c.water != ShapeField.LakeWaterY(in spec, in map, in o, x + 0.5f, z + 0.5f)) continue;
                if ((c.carvesTop || c.stamp3D || c.open1Hi > c.open1Lo || c.open2Hi > c.open2Lo || c.open3Hi > c.open3Lo) && belowHits < 3
                    && DetailField.Material(in spec, in o, in c, 0f, x, c.height, z) == GenMat.Air)
                {
                    belowHits++;
                    sb.Append($"[BELOW col ({x},{z}) h {c.height} w {c.water} s3D {c.stamp3D} top {c.carvesTop} open {c.open1Lo}-{c.open1Hi},{c.open2Lo}-{c.open2Hi},{c.open3Lo}-{c.open3Hi}" +
                              $" stamp {(Stamps.StampAt(in o, new float2(x, z), out var st3) ? st3.kind.ToString() + "@" + st3.pos : "-")}] ");
                }
                for (int d = 0; d < 4; d++)
                {
                    int dx = d == 0 ? 1 : d == 1 ? -1 : 0, dz = d == 2 ? 1 : d == 3 ? -1 : 0;
                    var nb = ShapeField.Sample(in spec, in map, in o, x + dx + 0.5f, z + dz + 0.5f, 1);
                    if (math.max(nb.height, math.max(nb.water, spec.seaLevelY)) >= c.water)
                    {
                        bool sp = nb.open1Hi > nb.open1Lo || nb.open2Hi > nb.open2Lo || nb.open3Hi > nb.open3Lo || nb.stamp3D || nb.carvesTop;
                        for (int y = c.height; y <= c.water && carvedHits < 3; y++)
                        {
                            if (y > nb.height || (!sp && nb.height - y <= DetailField.CARVE_MIN_DEPTH)) continue;
                            if (DetailField.Material(in spec, in o, in nb, 0f, x + dx, y, z + dz) != GenMat.Air) continue;
                            carvedHits++;
                            var raw = map; raw.waterClass = null;
                            var nb0 = ShapeField.Sample(in spec, in raw, in o, x + dx + 0.5f, z + dz + 0.5f, 1);
                            sb.Append($"[CARVED col ({x},{z}) h {c.height} w {c.water} | nb ({x + dx},{y},{z + dz}) h {nb.height} (no rim {nb0.height}, rimY {ShapeField.LakeRimY(in spec, in map, x + dx + 0.5f, z + dz + 0.5f, out float nd)} nearest {nd:F0}) w {nb.water} s3D {nb.stamp3D} top {nb.carvesTop} open {nb.open1Lo}-{nb.open1Hi},{nb.open2Lo}-{nb.open2Hi},{nb.open3Lo}-{nb.open3Hi}" +
                                      $" stamp {(Stamps.StampAt(in o, new float2(x, z), out var st2) ? st2.kind.ToString() + "@" + st2.pos : "-")}] ");
                            break;
                        }
                        continue;
                    }
                    if (hits++ < 3)
                        sb.Append($"[col ({x},{z}) h {c.height} w {c.water} | nb ({x + dx},{z + dz}) h {nb.height} w {nb.water} kind {nb.waterKind} stampMat {nb.stampMat} s3D {nb.stamp3D} top {nb.carvesTop}" +
                                  $" open {nb.open1Lo}-{nb.open1Hi},{nb.open2Lo}-{nb.open2Hi},{nb.open3Lo}-{nb.open3Hi} geyserD {math.distance(new float2(x, z), Stamps.GeyserSite(in spec, in o)):F0}" +
                                  $" volcD {math.distance(new float2(x, z), Stamps.VolcanoSite(in spec, in o)):F0} stamp {(Stamps.StampAt(in o, new float2(x, z), out var st) ? st.kind.ToString() + "@" + st.pos : "-")}] ");
                }
            }
            return $"{hits} pairs: " + sb;
        }

        [Category("Slow")]   // one shared v4 map per case (~30 s to build when not cached)
        [TestCase(42u)]
        [TestCase(7u)]
        [TestCase(2026u)]
        [TestCase(31337u)]
        public void LakeWater_IsAtRest(uint seed)
        {
            var spec = TerrainSpec.SizeClass2(seed, 4);
            var o = Offsets.For(in spec);
            var map = SharedMaps.Get(spec);   // shared: never disposed or written here
            Assert.AreEqual(80, spec.mapCellVoxels, "the tile job's stack buffers assume 8 m cells");
            int n = spec.mapCells;

            // Tiles: dual cell (ix, iz) has corners (ix..ix+1, iz..iz+1). Lake
            // shores and level changes from the map; geyser pools by site.
            var list = new NativeList<int2>(Allocator.TempJob);
            int lakeTiles = 0;
            for (int iz = 0; iz < n - 1; iz++)
            for (int ix = 0; ix < n - 1; ix++)
            {
                bool lakeNear = false, uniform = true;
                float lv = map.Water[iz * n + ix];
                for (int j = -1; j <= 2; j++)
                for (int i = -1; i <= 2; i++)
                {
                    int cx = math.clamp(ix + i, 0, n - 1), cz = math.clamp(iz + j, 0, n - 1), c = cz * n + cx;
                    if (map.WaterClass[c] == Hydrology.LAKE) lakeNear = true;
                    if (map.WaterClass[c] != Hydrology.LAKE || map.Water[c] != lv) uniform = false;
                }
                if (lakeNear && !uniform) { list.Add(new int2(ix, iz)); lakeTiles++; }
            }
            int geyserTiles = 0;
            var seen = new System.Collections.Generic.HashSet<int>();
            for (int k = 0; k < list.Length; k++) seen.Add(list[k].y * n + list[k].x);
            for (int j = 0; j < 9; j++)
            {
                float2 pc = Stamps.PoolCentre(in spec, in o, j);
                int2 lo = (int2)math.floor((pc - 130f) / spec.mapCellVoxels - 0.5f), hi = (int2)math.floor((pc + 130f) / spec.mapCellVoxels - 0.5f);
                for (int iz = math.max(0, lo.y); iz <= math.min(n - 2, hi.y); iz++)
                for (int ix = math.max(0, lo.x); ix <= math.min(n - 2, hi.x); ix++)
                    if (seen.Add(iz * n + ix)) { list.Add(new int2(ix, iz)); geyserTiles++; }
            }

            var tiles = list.AsArray();
            var hA = new NativeArray<float>(map.Height, Allocator.TempJob);
            var rA = new NativeArray<float>(map.Rough, Allocator.TempJob);
            var wA = new NativeArray<float>(map.Water, Allocator.TempJob);
            var cA = new NativeArray<byte>(map.WaterClass, Allocator.TempJob);
            var counts = new NativeArray<int>(tiles.Length * KINDS * K, Allocator.TempJob);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var tot = new long[KINDS * K];
            int worstTile = -1, worst = 0; int2 worstTileXZ = default;
            try
            {
                new TileJob { h = hA, r = rA, w = wA, cls = cA, tiles = tiles, spec = spec, o = o, counts = counts }
                    .Schedule(tiles.Length, 4).Complete();
                for (int t = 0; t < tiles.Length; t++)
                {
                    for (int s = 0; s < KINDS * K; s++) tot[s] += counts[t * KINDS * K + s];
                    if (counts[t * KINDS * K + LAKE * K + 2] > worst) { worst = counts[t * KINDS * K + LAKE * K + 2]; worstTile = t; }
                }
            }
            finally
            {
                if (worstTile >= 0) worstTileXZ = tiles[worstTile];
                string at = worstTile >= 0 ? $" worst lake tile ({tiles[worstTile].x},{tiles[worstTile].y}) = {worst} voxels" : "";
                counts.Dispose(); hA.Dispose(); rA.Dispose(); wA.Dispose(); cA.Dispose(); list.Dispose();
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"seed {seed} v4  map lakes {map.Hydro.lakes}  lake cells {map.Hydro.lakeCells} ({map.Hydro.lakeCells * 64e-4:F1} ha)" +
                              $"  tiles: {lakeTiles} lake shore, {geyserTiles} geyser  ({sw.Elapsed.TotalSeconds:F1} s)");
                for (int kd = 0; kd < KINDS; kd++)
                {
                    int b = kd * K;
                    sb.AppendLine($"  {(kd == LAKE ? "lake  " : "geyser")} water columns {tot[b]} ({tot[b] * 1e-6:F2} ha) voxels {tot[b + 1]}" +
                                  $"  UNSETTLED {tot[b + 2]}: air beside {tot[b + 3]}, below {tot[b + 4]}, diagonally below {tot[b + 5]}" +
                                  $"  (of them CARVED air {tot[b + 7]}; exact checks {tot[b + 6]})");
                }
                sb.Append(at);
                if (worstTile >= 0 && worst > 0) sb.Append("\n DIAG " + Diag(spec, o, map, worstTileXZ));
                string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "TerrainGen");
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, $"lake_level_{seed}.txt"), sb.ToString());
                UnityEngine.Debug.Log(sb.ToString());
            }
            Assert.AreEqual(0, tot[LAKE * K + 2], $"seed {seed}: lake water voxels not at rest (see Logs/TerrainGen/lake_level_{seed}.txt)");
            Assert.AreEqual(0, tot[GEYSER * K + 2], $"seed {seed}: geyser pool water voxels not at rest");
        }
    }
}
