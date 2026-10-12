// Assets/TerrainGen/Tests/FluidAtRestTests.cs
//
// Task 76 = RIVERS_WATERFALLS_PLAN.md R6 (BUGS #7's acceptance): every fluid the
// map v4 generator places is AT REST -- no fluid voxel has air beside it (the
// four side neighbours), directly below it, or diagonally below it (the four
// cells beside the one below). LakeLevelTests' rule and machinery, for the
// other fluids:
//   river   every column within one map cell of a RIVER cell (all of them)
//   sea     coast cells (an ocean cell beside land), one in COAST_STRIDE
//   lava    the volcano's caldera lake (Stamps.VolcanoSite)
//   lake    lake columns met in those tiles (LakeLevelTests covers all lakes)
//   geyser  the sinter pools met in those tiles
// and, separately, the sealed underground pockets (DetailField.Pocket: water,
// oil, acid, sludge, honey, quicksand, lava) in a window of pocket cells.
// A tile is one map cell (80 x 80 columns plus a one-column border); S is
// sampled once per column, DetailField.Material only where a cave or stamp
// could open a voxel (LakeLevelTests' Air rule). Report:
// Logs/TerrainGen/fluid_at_rest_<seed>.txt.
using System.IO;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace TerrainGen.Tests
{
    public class FluidAtRestTests
    {
        // Per tile and kind: 0 fluid columns, 1 fluid voxels, 2 unsettled (any of
        // 3-5), 3 air beside, 4 air below, 5 air diagonally below, 6 exact checks,
        // 7 unsettled whose air is CARVED (cave / stamp opening).
        private const int K = 8, KINDS = 5, LAKE = 0, RIVER = 1, SEA = 2, LAVA = 3, GEYSER = 4;
        private static readonly string[] KindName = { "lake  ", "river ", "sea   ", "lava  ", "geyser" };
        private const int COAST_STRIDE = 6;

        [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High, CompileSynchronously = true)]
        private struct TileJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float> h, r, w;
            [ReadOnly] public NativeArray<byte> cls;
            [ReadOnly] public NativeArray<ushort> flow;
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
                    flowDir = flow.Length > 1 ? (ushort*)flow.GetUnsafeReadOnlyPtr() : null,
                    cells = spec.mapCells, cellVoxels = spec.mapCellVoxels,
                };
                int2 tile = tiles[t];
                int x0 = tile.x * CV - 1, z0 = tile.y * CV - 1;   // the border column
                int* hg = stackalloc int[G * G];
                int* tp = stackalloc int[G * G];     // max(ground, water, sea): every voxel above it is air
                byte* kd = stackalloc byte[G * G];   // kind + 1, 0 = no fluid
                byte* fl = stackalloc byte[G * G];   // 4 may be carved below its top
                for (int k = 0; k < G; k++)
                for (int i = 0; i < G; i++)
                {
                    int x = x0 + i, z = z0 + k, gi = k * G + i;
                    var c = ShapeField.Sample(in spec, in map, in o, x + 0.5f, z + 0.5f, 1);
                    hg[gi] = c.height;
                    tp[gi] = math.max(c.height, math.max(c.water, spec.seaLevelY));
                    fl[gi] = (byte)((c.open1Hi > c.open1Lo || c.open2Hi > c.open2Lo || c.open3Hi > c.open3Lo || c.stamp3D || c.carvesTop) ? 4 : 0);
                    int kind = -1;
                    if (c.water > c.height && c.water > spec.seaLevelY)
                    {
                        if (c.waterKind == 1) kind = LAVA;
                        else if (c.water == ShapeField.LakeWaterY(in spec, in map, in o, x + 0.5f, z + 0.5f)) kind = LAKE;
                        else if (c.stampMat == GenMat.Sinter) kind = GEYSER;
                        else kind = RIVER;
                    }
                    else if (c.height < spec.seaLevelY) kind = SEA;
                    kd[gi] = (byte)(kind + 1);
                }

                for (int k = 1; k <= CV; k++)
                for (int i = 1; i <= CV; i++)
                {
                    int gi = k * G + i;
                    int kind = kd[gi] - 1;
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

            /// 0 not air, 1 air over the column's top, 2 carved air (LakeLevelTests).
            private unsafe int Air(in MapView map, int* hg, int* tp, byte* fl, int gi, int x, int y, int z, ref int exact)
            {
                if (y > tp[gi]) return 1;
                if (y > hg[gi]) return 0;
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

        /// The first few unsettled river/sea/lava voxels of one tile, for the report (managed).
        private static unsafe string Diag(TerrainSpec spec, Offsets o, WorldMap wm, int2 tile)
        {
            var map = wm.View; var sb = new System.Text.StringBuilder(); int hits = 0;
            int x0 = tile.x * 80, z0 = tile.y * 80;
            for (int k = 0; k < 80 && hits < 4; k++)
            for (int i = 0; i < 80 && hits < 4; i++)
            {
                int x = x0 + i, z = z0 + k;
                var c = ShapeField.Sample(in spec, in map, in o, x + 0.5f, z + 0.5f, 1);
                int top = math.max(c.height, math.max(c.water, spec.seaLevelY));
                if (top <= c.height) continue;
                for (int d = 0; d < 4 && hits < 4; d++)
                {
                    int dx = d == 0 ? 1 : d == 1 ? -1 : 0, dz = d == 2 ? 1 : d == 3 ? -1 : 0;
                    var nb = ShapeField.Sample(in spec, in map, in o, x + dx + 0.5f, z + dz + 0.5f, 1);
                    int nt = math.max(nb.height, math.max(nb.water, spec.seaLevelY));
                    if (nt >= top) continue;
                    hits++;
                    sb.Append($"[col ({x},{z}) h {c.height} w {c.water} kind {c.waterKind} lip {c.lipLo} | nb ({x + dx},{z + dz}) h {nb.height} w {nb.water} lip {nb.lipLo}" +
                              $" rimGround {nb.rimGround} lakeY {ShapeField.LakeWaterY(in spec, in map, in o, x + 0.5f, z + 0.5f)}] ");
                }
            }
            return sb.ToString();
        }

        [Category("Slow")]   // one shared v4 map per case, then ~1-2 min of tiles
        [TestCase(42u)]
        [TestCase(7u)]
        public void RiverSeaLava_AreAtRest(uint seed)
        {
            var spec = TerrainSpec.SizeClass2(seed, 4);
            var o = Offsets.For(in spec);
            var map = SharedMaps.Get(spec);   // shared: never disposed or written here
            Assert.AreEqual(80, spec.mapCellVoxels, "the tile job's stack buffers assume 8 m cells");
            Assert.IsNotNull(map.FlowDir, "a v4 map carries the river flow map (task 76)");
            int n = spec.mapCells;

            var seen = new System.Collections.Generic.HashSet<int>();
            var list = new NativeList<int2>(Allocator.TempJob);
            int riverTiles = 0, coastTiles = 0, coastCells = 0, lavaTiles = 0;
            for (int iz = 0; iz < n; iz++)
            for (int ix = 0; ix < n; ix++)
            {
                if (map.WaterClass[iz * n + ix] != Hydrology.RIVER) continue;
                for (int j = -1; j <= 1; j++)
                for (int i = -1; i <= 1; i++)
                {
                    int cx = ix + i, cz = iz + j;
                    if (cx < 1 || cz < 1 || cx >= n - 1 || cz >= n - 1 || !seen.Add(cz * n + cx)) continue;
                    list.Add(new int2(cx, cz)); riverTiles++;
                }
            }
            for (int iz = 1; iz < n - 1; iz++)
            for (int ix = 1; ix < n - 1; ix++)
            {
                int c = iz * n + ix;
                if (map.Height[c] > 0f) continue;
                if (map.Height[c - 1] <= 0f && map.Height[c + 1] <= 0f && map.Height[c - n] <= 0f && map.Height[c + n] <= 0f) continue;
                if (coastCells++ % COAST_STRIDE != 0 || !seen.Add(c)) continue;
                list.Add(new int2(ix, iz)); coastTiles++;
            }
            float2 vs = Stamps.VolcanoSite(in spec, in o);
            int vr = (int)math.ceil(Stamps.CALDERA_R * 1.3f / spec.mapCellVoxels);
            int2 vc = (int2)math.floor(vs / spec.mapCellVoxels);
            for (int iz = vc.y - vr; iz <= vc.y + vr; iz++)
            for (int ix = vc.x - vr; ix <= vc.x + vr; ix++)
                if (ix >= 1 && iz >= 1 && ix < n - 1 && iz < n - 1 && seen.Add(iz * n + ix)) { list.Add(new int2(ix, iz)); lavaTiles++; }

            var hy = map.Hydro;
            string head = $"seed {seed} v4  river cells {hy.riverCells} (falls > {Rivers.FALL_DROP * 0.1f:F0} m/cell: {hy.riverFallCells}, raised: {hy.riverRaisedCells}), mouths {hy.riverMouths}" +
                          $"  tiles: {riverTiles} river, {coastTiles} coast (1 in {COAST_STRIDE} of {coastCells} coast cells), {lavaTiles} volcano (site {vs})";
            var tot = RunTiles(spec, o, map, list, head, $"fluid_at_rest_{seed}.txt");
            Assert.Greater(tot[RIVER * K + 0], 0, "no river columns found: the river step did not run");
            for (int kd = 0; kd < KINDS; kd++)
                Assert.AreEqual(0, tot[kd * K + 2], $"seed {seed}: {KindName[kd].Trim()} voxels not at rest (Logs/TerrainGen/fluid_at_rest_{seed}.txt)");
        }

        /// The volcano's caldera lava lake (BUGS #5 asked for it). Map v4 draws
        /// the volcano from the feature pool, so the test seeds may not have
        /// one: the first of the four test seeds whose offsets place it on the map.
        [Category("Slow")]
        [Test]
        public void VolcanoLavaLake_IsAtRest()
        {
            uint seed = 0; TerrainSpec spec = default; Offsets o = default; float2 vs = default;
            foreach (uint sd in new uint[] { 42u, 7u, 2026u, 31337u })
            {
                spec = TerrainSpec.SizeClass2(sd, 4); o = Offsets.For(in spec); vs = Stamps.VolcanoSite(in spec, in o);
                if (vs.x > 0f && vs.y > 0f && vs.x < spec.mapCells * spec.mapCellVoxels && vs.y < spec.mapCells * spec.mapCellVoxels) { seed = sd; break; }
            }
            if (seed == 0) { Assert.Ignore("none of the four test seeds draws a volcano (map v4 feature pool)"); return; }
            var map = SharedMaps.Get(spec);
            int n = spec.mapCells;
            var list = new NativeList<int2>(Allocator.TempJob);
            int vr = (int)math.ceil(Stamps.CALDERA_R * 1.3f / spec.mapCellVoxels);
            int2 vc = (int2)math.floor(vs / spec.mapCellVoxels);
            for (int iz = vc.y - vr; iz <= vc.y + vr; iz++)
            for (int ix = vc.x - vr; ix <= vc.x + vr; ix++)
                if (ix >= 1 && iz >= 1 && ix < n - 1 && iz < n - 1) list.Add(new int2(ix, iz));
            var tot = RunTiles(spec, o, map, list, $"seed {seed} v4 volcano at {vs}: {list.Length} tiles", $"volcano_at_rest_{seed}.txt");
            Assert.Greater(tot[LAVA * K + 0], 0, "no lava columns in the caldera");
            for (int kd = 0; kd < KINDS; kd++)
                Assert.AreEqual(0, tot[kd * K + 2], $"seed {seed}: {KindName[kd].Trim()} voxels not at rest near the volcano");
        }

        /// Runs TileJob on `list` (disposed here), writes the report, returns the totals.
        private static long[] RunTiles(TerrainSpec spec, Offsets o, WorldMap map, NativeList<int2> list, string head, string file)
        {
            var tiles = list.AsArray();
            var hA = new NativeArray<float>(map.Height, Allocator.TempJob);
            var rA = new NativeArray<float>(map.Rough, Allocator.TempJob);
            var wA = new NativeArray<float>(map.Water, Allocator.TempJob);
            var cA = new NativeArray<byte>(map.WaterClass, Allocator.TempJob);
            var fA = new NativeArray<ushort>(map.FlowDir, Allocator.TempJob);
            var counts = new NativeArray<int>(tiles.Length * KINDS * K, Allocator.TempJob);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var tot = new long[KINDS * K];
            var worst = new int[KINDS]; var worstTile = new int2[KINDS];
            try
            {
                new TileJob { h = hA, r = rA, w = wA, cls = cA, flow = fA, tiles = tiles, spec = spec, o = o, counts = counts }
                    .Schedule(tiles.Length, 4).Complete();
                for (int t = 0; t < tiles.Length; t++)
                for (int kd = 0; kd < KINDS; kd++)
                {
                    for (int s = 0; s < K; s++) tot[kd * K + s] += counts[(t * KINDS + kd) * K + s];
                    int u = counts[(t * KINDS + kd) * K + 2];
                    if (u > worst[kd]) { worst[kd] = u; worstTile[kd] = tiles[t]; }
                }
            }
            finally
            {
                counts.Dispose(); hA.Dispose(); rA.Dispose(); wA.Dispose(); cA.Dispose(); fA.Dispose(); list.Dispose();
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"{head}  ({sw.Elapsed.TotalSeconds:F1} s)");
                for (int kd = 0; kd < KINDS; kd++)
                {
                    int b = kd * K;
                    sb.AppendLine($"  {KindName[kd]} columns {tot[b]} voxels {tot[b + 1]}  UNSETTLED {tot[b + 2]}: air beside {tot[b + 3]}, below {tot[b + 4]}, " +
                                  $"diagonally below {tot[b + 5]}  (of them CARVED air {tot[b + 7]}; exact checks {tot[b + 6]})" +
                                  (worst[kd] > 0 ? $"  worst tile ({worstTile[kd].x},{worstTile[kd].y}) = {worst[kd]}" : ""));
                }
                for (int kd = 0; kd < KINDS; kd++)
                    if (worst[kd] > 0) sb.AppendLine($"  DIAG {KindName[kd].Trim()}: " + Diag(spec, o, map, worstTile[kd]));
                string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "TerrainGen");
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, file), sb.ToString());
                UnityEngine.Debug.Log(sb.ToString());
            }
            return tot;
        }

        [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High, CompileSynchronously = true)]
        private struct PocketJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float> h, r, w;
            [ReadOnly] public NativeArray<byte> cls;
            [ReadOnly] public NativeArray<ushort> flow;
            [ReadOnly] public NativeArray<int3> cells;
            public TerrainSpec spec; public Offsets o;
            [NativeDisableParallelForRestriction] public NativeArray<long> counts;   // per pocket: voxels, unsettled, exact

            public unsafe void Execute(int t)
            {
                var map = new MapView
                {
                    height = (float*)h.GetUnsafeReadOnlyPtr(), rough = (float*)r.GetUnsafeReadOnlyPtr(),
                    water = (float*)w.GetUnsafeReadOnlyPtr(), waterClass = (byte*)cls.GetUnsafeReadOnlyPtr(),
                    flowDir = flow.Length > 1 ? (ushort*)flow.GetUnsafeReadOnlyPtr() : null,
                    cells = spec.mapCells, cellVoxels = spec.mapCellVoxels,
                };
                int3 pc = cells[t];
                DetailField.TryGetPocket(in spec, in o, pc.x, pc.y, pc.z, out var pk);
                int3 lo = (int3)math.floor(pk.centre - pk.radii), hi = (int3)math.ceil(pk.centre + pk.radii);
                long voxels = 0, bad = 0, exact = 0;
                for (int z = lo.z; z <= hi.z; z++)
                for (int x = lo.x; x <= hi.x; x++)
                {
                    bool haveCol = false; ColumnSample col = default;
                    for (int y = lo.y; y <= hi.y; y++)
                    {
                        float3 q = (new float3(x, y, z) + 0.5f - pk.centre) / pk.radii;
                        if (math.lengthsq(q) >= 1f || y + 0.5f > pk.level) continue;
                        bool air = false, checkedSelf = false, real = true;
                        for (int d = 0; d < 9 && !air; d++)
                        {
                            // 4 beside, 1 below, 4 diagonally below
                            int dx = 0, dz = 0, dy = d < 4 ? 0 : -1;
                            int e = d < 4 ? d : d - 5;
                            if (d != 4) { dx = e == 0 ? 1 : e == 1 ? -1 : 0; dz = e == 2 ? 1 : e == 3 ? -1 : 0; }
                            int3 v = new int3(x + dx, y + dy, z + dz);
                            float3 qn = (new float3(v) + 0.5f - pk.centre) / pk.radii;
                            if (math.lengthsq(qn) < 0.98f && v.y + 0.5f <= pk.level) continue;   // inside: the same fluid
                            if (!checkedSelf)
                            {
                                // A boundary voxel: is it really this pocket's fluid (not cut off by the surface)?
                                checkedSelf = true; exact++;
                                if (!haveCol) { col = ShapeField.Sample(in spec, in map, in o, x + 0.5f, z + 0.5f, 1); haveCol = true; }
                                if (DetailField.Material(in spec, in o, in col, 0f, x, y, z) != pk.fluid) { real = false; break; }
                            }
                            exact++;
                            var cn = ShapeField.Sample(in spec, in map, in o, v.x + 0.5f, v.z + 0.5f, 1);
                            if (DetailField.Material(in spec, in o, in cn, 0f, v.x, v.y, v.z) == GenMat.Air) air = true;
                        }
                        if (!real) continue;
                        voxels++;
                        if (air) bad++;
                    }
                }
                counts[t * 3 + 0] = voxels; counts[t * 3 + 1] = bad; counts[t * 3 + 2] = exact;
            }
        }

        /// The sealed pockets: every fluid voxel of every pocket in a window of
        /// pocket cells round the island's middle (DetailField.Pocket: flat
        /// level inside a never-carved shell).
        [Category("Slow")]
        [TestCase(42u)]
        public void UndergroundPockets_AreAtRest(uint seed)
        {
            var spec = TerrainSpec.SizeClass2(seed, 4);
            var o = Offsets.For(in spec);
            var map = SharedMaps.Get(spec);
            int c0 = (spec.mapCells * spec.mapCellVoxels >> DetailField.POCKET_CELL_SHIFT) / 2;   // the island's middle
            int cyTop = (spec.seaLevelY >> DetailField.POCKET_CELL_SHIFT) + 1;
            var list = new NativeList<int3>(Allocator.TempJob);
            var fluids = new System.Collections.Generic.SortedDictionary<byte, int>();
            for (int cz = c0 - 4; cz <= c0 + 4 && list.Length < 24; cz++)
            for (int cx = c0 - 4; cx <= c0 + 4 && list.Length < 24; cx++)
            for (int cy = cyTop; cy >= 0 && list.Length < 24; cy--)
                if (DetailField.TryGetPocket(in spec, in o, cx, cy, cz, out var pk))
                {
                    list.Add(new int3(cx, cy, cz));
                    fluids.TryGetValue(pk.fluid, out int k); fluids[pk.fluid] = k + 1;
                }
            var cellsA = list.AsArray();
            var hA = new NativeArray<float>(map.Height, Allocator.TempJob);
            var rA = new NativeArray<float>(map.Rough, Allocator.TempJob);
            var wA = new NativeArray<float>(map.Water, Allocator.TempJob);
            var cA = new NativeArray<byte>(map.WaterClass, Allocator.TempJob);
            var fA = new NativeArray<ushort>(map.FlowDir, Allocator.TempJob);
            var counts = new NativeArray<long>(cellsA.Length * 3, Allocator.TempJob);
            long voxels = 0, unsettled = 0, exact = 0; int pockets = cellsA.Length;
            var sb = new System.Text.StringBuilder();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                new PocketJob { h = hA, r = rA, w = wA, cls = cA, flow = fA, cells = cellsA, spec = spec, o = o, counts = counts }
                    .Schedule(cellsA.Length, 1).Complete();
                for (int t = 0; t < pockets; t++)
                {
                    voxels += counts[t * 3]; unsettled += counts[t * 3 + 1]; exact += counts[t * 3 + 2];
                    if (counts[t * 3 + 1] > 0) sb.Append($"[pocket {cellsA[t]}: {counts[t * 3 + 1]} unsettled] ");
                }
            }
            finally { counts.Dispose(); hA.Dispose(); rA.Dispose(); wA.Dispose(); cA.Dispose(); fA.Dispose(); list.Dispose(); }
            var fl = new System.Text.StringBuilder();
            foreach (var kv in fluids) fl.Append($"{kv.Key}:{kv.Value} ");
            string rep = $"seed {seed} v4 pockets {pockets} (GenMat fluid:count {fl}) fluid voxels {voxels} UNSETTLED {unsettled} (exact checks {exact}, {sw.Elapsed.TotalSeconds:F1} s) {sb}";
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "TerrainGen");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, $"pocket_at_rest_{seed}.txt"), rep);
            UnityEngine.Debug.Log(rep);
            Assert.Greater(pockets, 0, "no pocket in the window");
            Assert.AreEqual(0, unsettled, rep);
        }

        /// Map-stage invariants of Rivers.Profile: a river never stands higher
        /// than its upstream neighbour (levels never rise downstream) and ends at
        /// its lake's level or at sea level.
        [Test]
        public void RiverProfile_NeverRisesDownstream()
        {
            var spec = TerrainSpec.SizeClass2(42u, 4);
            var map = SharedMaps.Get(spec);
            int n = spec.mapCells, rivers = 0, rises = 0, ends = 0, endsOff = 0;
            for (int i = 0; i < n * n; i++)
            {
                if (map.WaterClass[i] != Hydrology.RIVER) continue;
                rivers++;
                int2 d = Rivers.Dir(map.FlowDir[i]);
                if (d.x == 0 && d.y == 0) continue;
                int p = i + d.y * n + d.x;
                if (map.WaterClass[p] == Hydrology.RIVER) { if (map.Water[p] > map.Water[i]) rises++; }
                else
                {
                    ends++;
                    float end = map.Height[p] <= 0f ? 0f : map.Water[p];
                    if (map.Water[i] < end) endsOff++;
                }
            }
            string rep = $"seed 42 v4 river cells {rivers}, rises {rises}, ends {ends} (below their lake/sea: {endsOff}); " +
                         $"Height FNV {map.HeightHash():X16} Water FNV {map.WaterHash():X16}";
            Directory.CreateDirectory(Path.Combine(Directory.GetCurrentDirectory(), "Logs", "TerrainGen"));
            File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(), "Logs", "TerrainGen", "river_profile_42.txt"), rep);
            UnityEngine.Debug.Log(rep);
            Assert.Greater(rivers, 100);
            Assert.AreEqual(0, rises, "a river cell's downstream neighbour stands higher");
            Assert.AreEqual(0, endsOff, "a river ends below the lake or sea it runs into");
        }
    }
}
