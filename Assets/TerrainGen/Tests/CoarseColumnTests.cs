// Assets/TerrainGen/Tests/CoarseColumnTests.cs
//
// Task 86 (owner Q45 (a)): DetailField.PrepareColumn's cave noises on an
// absolute 2^s grid (spec.coarseColumnShift), bilinear to the column.
//   Exactness   cached == uncached; at a node the cave terms are the exact
//               ones; geology, soil and ore are exact everywhere (strata
//               boundaries do not move); shift 0 is the pre-86 reference.
//   Shift       how far each cave boundary moves, in voxels: four 16,384-column
//               vertical slices through the island (two along x, two along z),
//               every voxel whose open/closed state differs from the per-column
//               reference, and its distance to the nearest reference voxel in
//               that state (a 2-D Hausdorff distance; the 3-D one is <= it).
// Report: Logs/TerrainGen/coarse_columns.txt.
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Unity.Mathematics;

namespace TerrainGen.Tests
{
    public class CoarseColumnTests
    {
        private static TerrainSpec _spec, _exact;
        private static Offsets _o;
        private static WorldMap _map;

        [OneTimeSetUp]
        public void BuildOnce()
        {
            if (_map != null) return;
            _spec = TerrainSpec.SizeClass2(42);
            _exact = _spec; _exact.coarseColumnShift = 0;
            _o = Offsets.For(in _spec);
            _map = SharedMaps.Get(_spec);
        }

        private static bool Same(in DetailField.ColumnDetail a, in DetailField.ColumnDetail b) => a.Equals(b);

        [Test]
        public void CoarseColumns_AreTheDefault_AndCachedEqualsUncached()
        {
            Assert.AreEqual(TerrainSpec.COARSE_COLUMN_SHIFT, _spec.coarseColumnShift);
            Assert.AreEqual(3, TerrainSpec.COARSE_COLUMN_SHIFT);
            var cache = new DetailField.NodeCache();
            int n = 0;
            // A row-major block (ColumnJob's order) and a scatter.
            for (int z = 40000; z < 40064; z++)
            for (int x = 39990; x < 40054; x++)
            {
                var a = DetailField.PrepareColumn(in _spec, in _o, x, z);
                var b = DetailField.PrepareColumn(in _spec, in _o, x, z, ref cache);
                Assert.IsTrue(Same(in a, in b), $"cached differs at ({x},{z})");
                n++;
            }
            Assert.Less(cache.Misses, 9 * 9 + 9 * 2, "each node about once in a row-major sweep");
            uint h = 12345u;
            for (int i = 0; i < 2000; i++)
            {
                h = h * 1664525u + 1013904223u; int x = (int)(h % 80000u);
                h = h * 1664525u + 1013904223u; int z = (int)(h % 80000u);
                var a = DetailField.PrepareColumn(in _spec, in _o, x, z);
                var b = DetailField.PrepareColumn(in _spec, in _o, x, z, ref cache);
                Assert.IsTrue(Same(in a, in b), $"cached differs at ({x},{z})");
                n++;
            }
            TestContext.WriteLine($"{n} columns, cached == uncached; {cache.Misses} node evaluations");
        }

        [Test]
        public void CoarseColumns_GeologyExact_NodesExact_ShiftZeroIsReference()
        {
            int nodes = 0;
            uint h = 777u;
            for (int i = 0; i < 4000; i++)
            {
                h = h * 1664525u + 1013904223u; int x = (int)(h % 80000u);
                h = h * 1664525u + 1013904223u; int z = (int)(h % 80000u);
                var c = DetailField.PrepareColumn(in _spec, in _o, x, z);
                var e = DetailField.PrepareColumnExact(in _spec, in _o, x, z);
                // Strata, soil and ore: exact per column.
                Assert.AreEqual(e.deepOffset, c.deepOffset); Assert.AreEqual(e.cavernOffset, c.cavernOffset);
                Assert.AreEqual(e.underOffset, c.underOffset); Assert.AreEqual(e.soilDepth, c.soilDepth);
                Assert.AreEqual(e.oreMul, c.oreMul);
                var z0 = DetailField.PrepareColumn(in _exact, in _o, x, z);
                Assert.IsTrue(Same(in e, in z0), "shift 0 = the reference");
                // Snap to the node at or below: columns = 7 mod 8.
                int nx = ((x - 7) & ~7) + 7, nz = ((z - 7) & ~7) + 7;
                var cn = DetailField.PrepareColumn(in _spec, in _o, nx, nz);
                var en = DetailField.PrepareColumnExact(in _spec, in _o, nx, nz);
                Assert.IsTrue(Same(in cn, in en), $"node ({nx},{nz}) is not exact");
                nodes++;
            }
            TestContext.WriteLine($"4000 columns geology-exact, {nodes} nodes exact");
        }

        // ---------------- boundary shift ----------------
        // Per column, each cave group's open y intervals [lo, hi): caverns (2),
        // the underworld chamber (1), tunnels (4 layers, InTunnel's own test),
        // all clipped to CARVE_MIN_DEPTH below the top (pockets and river
        // gates are the same in both versions and are left out).
        private const int GROUPS = 3;
        private static readonly string[] GroupName = { "tunnels", "caverns", "underworld" };

        private struct Col { public int n0, n1, n2; public int4 a0, b0; public int2 a1, b1, a2; }

        private static bool InTunnel(float t, float yc, float halfH, int y)
        {
            if (t >= 1f) return false;
            float v = (y - yc) / halfH;
            return t * t + v * v < 1f;
        }

        private static int2 TunnelSpan(float t, float yc, float halfH, int clipHi)
        {
            if (t >= 1f) return int2.zero;
            int lo = int.MaxValue, hi = int.MinValue;
            for (int y = (int)math.floor(yc - halfH) - 1; y <= (int)math.ceil(yc + halfH) + 1; y++)
                if (y < clipHi && InTunnel(t, yc, halfH, y)) { lo = math.min(lo, y); hi = math.max(hi, y + 1); }
            return lo < hi ? new int2(lo, hi) : int2.zero;
        }

        private static int2 Clip(int lo, int hi, int clipHi) { hi = math.min(hi, clipHi); return hi > lo ? new int2(lo, hi) : int2.zero; }

        /// The column's open intervals per group, packed: tunnels in a0/b0
        /// (lo, hi pairs of 4 layers as (a0[k], b0[k])), caverns a1/b1, underworld a2.
        private static Col Intervals(in ColumnSample c, in DetailField.ColumnDetail cd)
        {
            int clip = c.height - DetailField.CARVE_MIN_DEPTH;   // hc - y > CARVE_MIN_DEPTH
            var col = new Col();
            int2 t0 = TunnelSpan(cd.tun0, c.height - cd.tunY0, cd.tunH0, clip);
            int2 t1 = TunnelSpan(cd.tun1, cd.tunY1, cd.tunH1, clip);
            int2 t2 = TunnelSpan(cd.tun2, cd.tunY2, cd.tunH2, clip);
            int2 t3 = TunnelSpan(cd.tun3, c.regional - cd.tunY3, cd.tunH3, clip);
            col.a0 = new int4(t0.x, t1.x, t2.x, t3.x); col.b0 = new int4(t0.y, t1.y, t2.y, t3.y);
            int2 c0 = Clip(cd.cav0Lo, cd.cav0Hi, clip), c1 = Clip(cd.cav1Lo, cd.cav1Hi, clip);
            col.a1 = new int2(c0.x, c1.x); col.b1 = new int2(c0.y, c1.y);
            col.a2 = Clip(cd.underLo, math.min(cd.underHi, _spec.UnderworldTopY), clip);
            return col;
        }

        private static bool Open(in Col c, int g, int y)
        {
            if (g == 0) return math.any((y >= c.a0) & (y < c.b0));
            if (g == 1) return math.any((y >= c.a1) & (y < c.b1));
            return y >= c.a2.x && y < c.a2.y;
        }

        /// Every interval endpoint of group g (the state is constant between two).
        private static void Ends(in Col c, int g, List<int> into)
        {
            if (g == 0) { for (int k = 0; k < 4; k++) if (c.b0[k] > c.a0[k]) { into.Add(c.a0[k]); into.Add(c.b0[k]); } }
            else if (g == 1) { for (int k = 0; k < 2; k++) if (c.b1[k] > c.a1[k]) { into.Add(c.a1[k]); into.Add(c.b1[k]); } }
            else if (c.a2.y > c.a2.x) { into.Add(c.a2.x); into.Add(c.a2.y); }
        }

        [Test, Category("Slow")]
        public void CoarseColumns_BoundaryShift_InVoxels()
        {
            const int W = 16384, R = 24;
            var v = _map.View;
            int ctr = _spec.worldSizeVoxels / 2;
            var lines = new List<string>
            {
                $"Task 86: cave boundary shift, coarse (shift {_spec.coarseColumnShift}, one node per {1 << _spec.coarseColumnShift}^2 columns) vs per column; seed 42 map v{_spec.mapVersion}",
                $"4 vertical slices x {W} columns through the island centre; distance (voxels) from each voxel whose state differs to the nearest reference voxel in that state",
            };
            var worst = new double[GROUPS]; var diff = new long[GROUPS]; var open = new long[GROUPS]; var far = new long[GROUPS];
            var hist = new long[GROUPS, 8];   // distance buckets: <=1, <=1.5, <=2, <=3, <=4, <=8, <=R, >R
            int geoDiff = 0;
            var ends = new List<int>(32);
            for (int sl = 0; sl < 4; sl++)
            {
                bool alongX = sl < 2;
                int fixedC = ctr + (sl % 2 == 0 ? -3000 : 2100) + 3;   // not on a node row
                var E = new Col[W]; var C = new Col[W];
                for (int i = 0; i < W; i++)
                {
                    int x = alongX ? ctr - W / 2 + i : fixedC, z = alongX ? fixedC : ctr - W / 2 + i;
                    var s = ShapeField.Sample(in _spec, in v, in _o, x + 0.5f, z + 0.5f, 1);
                    var cd = DetailField.PrepareColumn(in _spec, in _o, x, z);
                    var ed = DetailField.PrepareColumnExact(in _spec, in _o, x, z);
                    if (cd.deepOffset != ed.deepOffset || cd.cavernOffset != ed.cavernOffset || cd.underOffset != ed.underOffset) geoDiff++;
                    E[i] = Intervals(in s, in ed); C[i] = Intervals(in s, in cd);
                }
                for (int g = 0; g < GROUPS; g++)
                for (int i = 0; i < W; i++)
                {
                    ends.Clear(); Ends(in E[i], g, ends); Ends(in C[i], g, ends);
                    if (ends.Count == 0) continue;
                    ends.Sort();
                    for (int q = 0; q + 1 < ends.Count; q++)
                    {
                        int y0 = ends[q], y1 = ends[q + 1];
                        if (y1 <= y0) continue;
                        bool e = Open(in E[i], g, y0), c = Open(in C[i], g, y0);
                        if (e) open[g] += y1 - y0;
                        if (e == c) continue;
                    for (int y = y0; y < y1; y++)
                    {
                        diff[g]++;
                        // Nearest reference voxel in state c, ring by ring.
                        int best2 = int.MaxValue;
                        for (int r = 1; r <= R && (long)r * r < best2; r++)
                        for (int dy = -r; dy <= r; dy++)
                        for (int dx = -r; dx <= r; dx++)
                        {
                            if (math.max(math.abs(dx), math.abs(dy)) != r) continue;
                            int j = i + dx; if (j < 0 || j >= W) continue;
                            int d2 = dx * dx + dy * dy; if (d2 >= best2) continue;
                            if (Open(in E[j], g, y + dy) == c) best2 = d2;
                        }
                        double d = best2 == int.MaxValue ? R + 1 : math.sqrt(best2);
                        if (best2 == int.MaxValue) far[g]++;
                        worst[g] = math.max(worst[g], d);
                        int b = d <= 1 ? 0 : d <= 1.5 ? 1 : d <= 2 ? 2 : d <= 3 ? 3 : d <= 4 ? 4 : d <= 8 ? 5 : d <= R ? 6 : 7;
                        hist[g, b]++;
                    }
                    }
                }
            }
            lines.Add($"strata (deep / cavern / underworld boundary offsets): {geoDiff} of {4 * W} columns differ (exact by construction)");
            for (int g = 0; g < GROUPS; g++)
                lines.Add($"  {GroupName[g],-10} open voxels {open[g],10:N0}  differing {diff[g],8:N0} ({100.0 * diff[g] / math.max(1, open[g]):F3}%)  " +
                          $"largest shift {(far[g] > 0 ? $"> {R}" : $"{worst[g]:F2}")} voxels  " +
                          $"[<=1: {hist[g, 0]}, <=1.5: {hist[g, 1]}, <=2: {hist[g, 2]}, <=3: {hist[g, 3]}, <=4: {hist[g, 4]}, <=8: {hist[g, 5]}, <={R}: {hist[g, 6]}, >{R}: {hist[g, 7]}]");
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "TerrainGen");
            Directory.CreateDirectory(dir);
            File.WriteAllLines(Path.Combine(dir, "coarse_columns.txt"), lines);
            UnityEngine.Debug.Log("[task 86]\n" + string.Join("\n", lines));
            Assert.AreEqual(0, geoDiff, "strata must be exact");
            for (int g = 0; g < GROUPS; g++)
            {
                Assert.Greater(open[g], 0, $"{GroupName[g]}: the slices hold none (pick other slices)");
                Assert.AreEqual(0, far[g], $"{GroupName[g]}: a boundary moved more than {R} voxels");
                Assert.LessOrEqual(worst[g], 2.0, $"{GroupName[g]}: largest shift {worst[g]:F2} voxels (> 0.2 m)");
            }
        }
    }
}
