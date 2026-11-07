// Assets/TerrainGen/Tests/RiverShapeTests.cs
//
// Task 179 (WORLD_GEN_LOOK_PLAN C5, finding 3 "rivers: canals, not rivers"):
// the shaped river course, measured on four seeds against the pre-179 rivers
// (spec.riverShapeOff = 1, a map of its own: Profile's levels differ). Per river
// cell with a river downstream (mouths left out: they end in a lake or the sea):
//   course   the largest turn between consecutive straight pieces of the drawn
//            course round the cell (the D8 line turns 45 / 90 / 135 deg at once);
//            the largest sideways offset of the course from the D8 line, in half
//            widths (the meanders)
//   section  a cross-section through the middle of the cell's piece, normal to
//            it, sampled every voxel with ShapeField.Sample: per side of the wet
//            span, PERCHED = the dry ground 3 m past the water's edge stands
//            under the water (the river held up by a levee / an aqueduct), and
//            SHEER = the first metre of bank is steeper than 45 deg over 1 m
//            (the route test's band rule, along the section)
//   steps    the water surface every 2 voxels along the course: a jump of more
//            than one voxel between two samples (cells with Profile's FALL flag,
//            R4's waterfalls, left out)
// and the same per adversarial class, found from the map of any seed:
//   walled   both sides rise >= 6 m within 3 cells across the flow (a canyon or
//            gorge floor)
//   lake     a lake cell beside it that is not where it drains (an outlet, or a
//            river along a shore)
//   border   its biome differs from its downstream cell's
// Report: Logs/TerrainGen/river_shape179.txt.
using System.IO;
using System.Text;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace TerrainGen.Tests
{
    public class RiverShapeTests
    {
        private static readonly uint[] Seeds = { 42u, 7u, 2026u, 31337u };

        // per cell: 0 valid, 1 max turn (deg), 2 max offset / W, 3 sides measured, 4 perched sides,
        // 5 sheer sides, 6 step samples, 7 big steps, 8 centre dry, 9 class bits (1 walled, 2 lake, 4 border),
        // 10 perched >= 0.5 m sides, 11-12 section centre, 13-14 section normal, 15 first sheer edge (signed, 0 none),
        // 16 first perched edge, 17 its water level
        private const int F = 18;
        private const int WALLED = 1, LAKE = 2, BORDER = 4;

        [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High, CompileSynchronously = true)]
        private struct CellJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float> h, r, w;
            [ReadOnly] public NativeArray<byte> cls, route;
            [ReadOnly] public NativeArray<ushort> flow;
            [ReadOnly] public NativeArray<int> cells;
            public TerrainSpec spec; public Offsets o;
            [NativeDisableParallelForRestriction] public NativeArray<float> outv;

            private static int DX(int d) => d == 0 || d == 3 || d == 5 ? -1 : (d == 1 || d == 6 ? 0 : 1);
            private static int DZ(int d) => d < 3 ? -1 : (d < 5 ? 0 : 1);

            private static float Turn(float2 a, float2 b)
            {
                if (math.lengthsq(a) < 1e-6f || math.lengthsq(b) < 1e-6f) return 0f;
                return math.degrees(math.acos(math.clamp(math.dot(math.normalize(a), math.normalize(b)), -1f, 1f)));
            }

            private static float DistSeg(float2 p, float2 a, float2 b)
            {
                float2 ab = b - a; float l2 = math.dot(ab, ab);
                float t = l2 > 0f ? math.saturate(math.dot(p - a, ab) / l2) : 0f;
                return math.length(p - (a + t * ab));
            }

            public unsafe void Execute(int idx)
            {
                var map = new MapView
                {
                    height = (float*)h.GetUnsafeReadOnlyPtr(), rough = (float*)r.GetUnsafeReadOnlyPtr(),
                    water = (float*)w.GetUnsafeReadOnlyPtr(), waterClass = (byte*)cls.GetUnsafeReadOnlyPtr(),
                    flowDir = (ushort*)flow.GetUnsafeReadOnlyPtr(),
                    route = route.Length > 1 ? (byte*)route.GetUnsafeReadOnlyPtr() : null,
                    cells = spec.mapCells, cellVoxels = spec.mapCellVoxels,
                };
                int b = idx * F;
                int n = spec.mapCells, k = cells[idx], cx = k % n, cz = k / n;
                float cv = spec.mapCellVoxels;
                bool shaped = spec.RiverShape;
                int f = map.flowDir[k];
                int d = f & Rivers.FLOW_DIR_MASK, xx = cx + DX(d), xz = cz + DZ(d);
                float W = Rivers.HalfWidth(f >> Rivers.FLOW_SIZE_SHIFT);
                Rivers.Seg* sy = stackalloc Rivers.Seg[8];
                Rivers.Seg* sc = stackalloc Rivers.Seg[8];
                int my = Rivers.CellCourse(in map, spec.seed, shaped, cx, cz, sy);
                if (my == 0) return;
                outv[b + 0] = 1f;

                // ---- course: turns (from the main child's last piece) and offset ----
                int child = -1, best = -1;
                for (int q = 0; q < 8; q++)
                {
                    int qx = cx + DX(q), qz = cz + DZ(q);
                    if (qx < 0 || qz < 0 || qx >= n || qz >= n) continue;
                    int kq = qz * n + qx, fq = map.flowDir[kq];
                    if (map.waterClass[kq] != Hydrology.RIVER || (fq & Rivers.FLOW_HAS) == 0) continue;
                    if (qx + DX(fq & 7) != cx || qz + DZ(fq & 7) != cz) continue;
                    if ((fq >> Rivers.FLOW_SIZE_SHIFT) > best) { best = fq >> Rivers.FLOW_SIZE_SHIFT; child = kq; }
                }
                float turn = 0f;
                if (child >= 0)
                {
                    int mc = Rivers.CellCourse(in map, spec.seed, shaped, child % n, child / n, sc);
                    if (mc > 0) turn = math.max(turn, Turn(sc[mc - 1].b - sc[mc - 1].a, sy[0].b - sy[0].a));
                }
                for (int i = 1; i < my; i++) turn = math.max(turn, Turn(sy[i - 1].b - sy[i - 1].a, sy[i].b - sy[i].a));
                outv[b + 1] = turn;
                float2 cY = new float2((cx + 0.5f) * cv, (cz + 0.5f) * cv), cX = new float2((xx + 0.5f) * cv, (xz + 0.5f) * cv), cZ = cX;
                int kx = xz * n + xx, fx = map.flowDir[kx];
                if (map.waterClass[kx] == Hydrology.RIVER && (fx & Rivers.FLOW_HAS) != 0)
                    cZ = new float2((xx + DX(fx & 7) + 0.5f) * cv, (xz + DZ(fx & 7) + 0.5f) * cv);
                float off = 0f;
                for (int i = 0; i < my; i++)
                {
                    off = math.max(off, math.min(DistSeg(sy[i].a, cY, cX), DistSeg(sy[i].a, cX, cZ)));
                    off = math.max(off, math.min(DistSeg(sy[i].b, cY, cX), DistSeg(sy[i].b, cX, cZ)));
                }
                outv[b + 2] = off / W;

                // ---- classes (the map only: the same in both arms) ----
                int bits = 0;
                {
                    float2 dir = math.normalize(new float2(DX(d), DZ(d)));
                    float2 nrm = new float2(-dir.y, dir.x);
                    float riseL = 0f, riseR = 0f;
                    for (int s = 1; s <= 3; s++)
                    {
                        int2 a = (int2)math.round(new float2(cx, cz) + nrm * s), c = (int2)math.round(new float2(cx, cz) - nrm * s);
                        if (math.all(a >= 0) && math.all(a < n)) riseL = math.max(riseL, map.height[a.y * n + a.x] - map.height[k]);
                        if (math.all(c >= 0) && math.all(c < n)) riseR = math.max(riseR, map.height[c.y * n + c.x] - map.height[k]);
                    }
                    if (riseL >= 60f && riseR >= 60f) bits |= WALLED;
                    for (int q = 0; q < 8; q++)
                    {
                        int qx = cx + DX(q), qz = cz + DZ(q);
                        if (qx < 0 || qz < 0 || qx >= n || qz >= n || (qx == xx && qz == xz)) continue;
                        if (map.waterClass[qz * n + qx] == Hydrology.LAKE) bits |= LAKE;
                    }
                    if (ShapeField.Biome(in o, cY) != ShapeField.Biome(in o, cX)) bits |= BORDER;
                }
                outv[b + 9] = bits;

                // ---- section: through the middle of the cell's last piece ----
                Rivers.Seg mid = sy[my > 1 ? my - 2 : my - 1];   // shaped: the curve's middle subsegment
                float2 sa = mid.a, sb = mid.b;
                float2 sdir = math.normalizesafe(sb - sa, new float2(1f, 0f));
                float2 snrm = new float2(-sdir.y, sdir.x);
                float2 c0 = 0.5f * (sa + sb);
                const int S = 160;
                int* hs = stackalloc int[2 * S + 1];
                int* ws = stackalloc int[2 * S + 1];
                Rivers.Seg* segsF = stackalloc Rivers.Seg[Rivers.MAX_SEGS];
                for (int s = -S; s <= S; s++)
                {
                    float2 q = c0 + s * snrm;
                    var col = ShapeField.Sample(in spec, in map, in o, q.x, q.y, 1, false);
                    hs[s + S] = col.height;
                    // a LIP (task 76) is drawn as water: part of the wet span
                    ws[s + S] = col.water > col.height ? col.water : (col.lipLo > 0 ? col.height : (col.height < spec.seaLevelY ? spec.seaLevelY : int.MinValue));   // the sea is water too
                }
                // the wet span round the centre (within 2 voxels of it)
                int cs = int.MinValue;
                for (int s = 0; s <= 2 && cs == int.MinValue; s++)
                {
                    if (ws[S + s] != int.MinValue) cs = s;
                    else if (ws[S - s] != int.MinValue) cs = -s;
                }
                outv[b + 11] = c0.x; outv[b + 12] = c0.y; outv[b + 13] = snrm.x; outv[b + 14] = snrm.y;
                if (cs == int.MinValue) { outv[b + 8] = 1f; }
                else
                {
                    for (int side = -1; side <= 1; side += 2)
                    {
                        int s = cs;
                        while (math.abs(s + side) <= S && ws[S + s + side] != int.MinValue) s += side;
                        int edge = s + side;                        // the first dry column
                        if (math.abs(edge + side * 40) > S - 6) continue;
                        int far = edge + side * 30;                 // 3 m past the edge
                        if (ws[S + far] != int.MinValue) continue;  // more water there (a lake, another channel)
                        // the level of the channel NEAREST the far point (on a steep reach the
                        // section also runs downstream, so the edge's own water is not the yardstick)
                        float2 fq = c0 + far * snrm;
                        int mf = shaped ? Rivers.GatherShaped(in map, Rivers.SeedOffset(spec.seed), fq, 200f, segsF) : Rivers.Gather(in map, fq.x, fq.y, segsF);
                        float Lf = Rivers.LevelAt(segsF, mf, fq, 0f, out _, out _, out _, out float nearL, out _, out _, shaped);
                        if (Lf > Hydrology.NO_WATER * 0.5f || nearL <= Hydrology.NO_WATER * 0.5f) continue;
                        int wl = spec.seaLevelY + (int)math.floor(nearL);
                        outv[b + 3] += 1f;
                        if (hs[S + far] < wl)
                        {
                            outv[b + 4] += 1f;
                            if (outv[b + 16] == 0f) { outv[b + 16] = edge == 0 ? side : edge; outv[b + 17] = wl; }
                        }
                        if (hs[S + far] <= wl - 5) outv[b + 10] += 1f;
                        bool sheer = false;
                        for (int e = 0; e <= 10 && !sheer; e++)
                        {
                            int at = edge + side * e;
                            if (ws[S + at] != int.MinValue) break;
                            float g = (hs[S + at + 5] - hs[S + at - 5]) / 10f;
                            sheer = g * g > Danger.CLIMB_TAN2;
                        }
                        if (sheer) { outv[b + 5] += 1f; if (outv[b + 15] == 0f) outv[b + 15] = edge == 0 ? side : edge; }
                    }
                }

                // ---- steps along the course ----
                if ((f & Rivers.FLOW_FALL) == 0)
                {
                    int prev = int.MinValue, samples = 0, big = 0;
                    for (int i = 0; i < my; i++)
                    {
                        float len = math.length(sy[i].b - sy[i].a);
                        int ns = math.max(1, (int)(len / 2f));
                        for (int j = 0; j < ns; j++)
                        {
                            float2 q = math.lerp(sy[i].a, sy[i].b, (j + 0.5f) / ns);
                            var col = ShapeField.Sample(in spec, in map, in o, q.x, q.y, 1, false);
                            int top = col.water > col.height ? col.water : (col.lipLo > 0 ? col.height : int.MinValue);
                            if (top != int.MinValue && prev != int.MinValue)
                            {
                                samples++;
                                if (math.abs(top - prev) > 1) big++;
                            }
                            prev = top;
                        }
                    }
                    outv[b + 6] = samples; outv[b + 7] = big;
                }
            }
        }

        private struct Agg
        {
            public int cells, turn40, sides, perched, perched5, sheer, dryCentre; public long steps, big;
            public System.Collections.Generic.List<float> turns, offs;
        }

        private static Agg NewAgg() => new Agg { turns = new System.Collections.Generic.List<float>(), offs = new System.Collections.Generic.List<float>() };

        private static float Pct(System.Collections.Generic.List<float> l, float q)
        {
            if (l.Count == 0) return 0f;
            l.Sort();
            return l[math.min(l.Count - 1, (int)(q * l.Count))];
        }

        private static string Line(string name, Agg a) =>
            $"{name,-8} cells {a.cells,5}  turn p50/p90/p99/max {Pct(a.turns, 0.5f),5:F1}/{Pct(a.turns, 0.9f),5:F1}/{Pct(a.turns, 0.99f),5:F1}/{Pct(a.turns, 1f),5:F1} deg, > 40 deg {100f * a.turn40 / math.max(1, a.cells),5:F1}%" +
            $"  offset p50/p90 {Pct(a.offs, 0.5f):F2}/{Pct(a.offs, 0.9f):F2} W" +
            $"  PERCHED {100f * a.perched / math.max(1, a.sides),5:F1}% (>= 0.5 m {100f * a.perched5 / math.max(1, a.sides),5:F1}%)  bank SHEER {100f * a.sheer / math.max(1, a.sides),5:F1}% (of {a.sides} sides)" +
            $"  steps > 1 voxel {1000.0 * a.big / math.max(1, a.steps):F1} per 1000  dry centre {a.dryCentre}";

        /// Per seed and arm: all, walled, lake, border.
        private static unsafe Agg[] Measure(TerrainSpec spec, WorldMap map, out double secs, StringBuilder examples = null)
        {
            int n = spec.mapCells;
            var list = new NativeList<int>(Allocator.TempJob);
            for (int i = 0; i < n * n; i++)
            {
                if (map.WaterClass[i] != Hydrology.RIVER) continue;
                int f = map.FlowDir[i];
                if ((f & Rivers.FLOW_HAS) == 0) continue;
                int2 dd = Rivers.Dir(map.FlowDir[i]);
                int p = i + dd.y * n + dd.x;
                if (map.WaterClass[p] != Hydrology.RIVER) continue;   // a mouth
                int x = i % n, z = i / n;
                if (x < 3 || z < 3 || x >= n - 3 || z >= n - 3) continue;
                list.Add(i);
            }
            var hA = new NativeArray<float>(map.Height, Allocator.TempJob);
            var rA = new NativeArray<float>(map.Rough, Allocator.TempJob);
            var wA = new NativeArray<float>(map.Water, Allocator.TempJob);
            var cA = new NativeArray<byte>(map.WaterClass, Allocator.TempJob);
            var fA = new NativeArray<ushort>(map.FlowDir, Allocator.TempJob);
            var rtA = new NativeArray<byte>(map.RouteMask ?? new byte[1], Allocator.TempJob);
            var outv = new NativeArray<float>(list.Length * F, Allocator.TempJob);
            var aggs = new Agg[4];
            for (int a = 0; a < 4; a++) aggs[a] = NewAgg();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                new CellJob { h = hA, r = rA, w = wA, cls = cA, route = rtA, flow = fA, cells = list.AsArray(), spec = spec, o = Offsets.For(in spec), outv = outv }
                    .Schedule(list.Length, 8).Complete();
                for (int c = 0; c < list.Length; c++)
                {
                    int b = c * F;
                    if (outv[b] == 0f) continue;
                    int bits = (int)outv[b + 9];
                    for (int a = 0; a < 4; a++)
                    {
                        if (a == 1 && (bits & WALLED) == 0) continue;
                        if (a == 2 && (bits & LAKE) == 0) continue;
                        if (a == 3 && (bits & BORDER) == 0) continue;
                        var g = aggs[a];
                        g.cells++;
                        g.turns.Add(outv[b + 1]); g.offs.Add(outv[b + 2]);
                        if (outv[b + 1] > 40f) g.turn40++;
                        g.sides += (int)outv[b + 3]; g.perched += (int)outv[b + 4]; g.sheer += (int)outv[b + 5]; g.perched5 += (int)outv[b + 10];
                        g.steps += (long)outv[b + 6]; g.big += (long)outv[b + 7]; g.dryCentre += (int)outv[b + 8];
                        aggs[a] = g;
                    }
                    if (examples != null && (outv[b + 15] != 0f || outv[b + 16] != 0f) && examples.Length < 6000)
                    {
                        var o = Offsets.For(in spec); var view = map.View;
                        float2 c0 = new float2(outv[b + 11], outv[b + 12]), nr = new float2(outv[b + 13], outv[b + 14]);
                        for (int kind = 0; kind < 2; kind++)
                        {
                            float e = outv[b + 15 + kind];
                            if (e == 0f) continue;
                            int side = e > 0 ? 1 : -1;
                            examples.Append($"    {(kind == 0 ? "SHEER  " : "PERCHED")} cell {list[c]} at ({c0.x / 10f:F0},{c0.y / 10f:F0}) m class {bits}: ");
                            if (kind == 1)
                            {
                                float2 q = c0 + (e + side * 30) * nr;
                                var v0 = view; v0.flowDir = null;
                                var sc = spec; sc.cliffsOff = 1;
                                examples.Append($"[water {outv[b + 17]}; 3 m out: no-river h {ShapeField.Sample(in spec, in v0, in o, q.x, q.y, 1, false).height}, cliffs off {ShapeField.Sample(in sc, in view, in o, q.x, q.y, 1, false).height}] ");
                            }
                            for (int t = kind == 0 ? -8 : -8; t <= (kind == 0 ? 16 : 32); t += kind == 0 ? 1 : 2)
                            {
                                float2 q = c0 + (e + side * t) * nr;
                                var col = ShapeField.Sample(in spec, in view, in o, q.x, q.y, 1, false);
                                examples.Append(col.water > col.height ? $"{col.height}w{col.water} " : (col.lipLo > 0 ? $"L{col.height} " : $"{col.height} "));
                            }
                            examples.AppendLine();
                        }
                    }
                }
            }
            finally
            {
                hA.Dispose(); rA.Dispose(); wA.Dispose(); cA.Dispose(); fA.Dispose(); rtA.Dispose(); outv.Dispose(); list.Dispose();
            }
            secs = sw.Elapsed.TotalSeconds;
            return aggs;
        }

        private static readonly string[] AggName = { "all", "walled", "lake", "border" };

        /// The shaped rivers against the pre-179 ones (riverShapeOff) on four
        /// seeds, every class with cells. Gates (a regression guard set from the
        /// first full run, 2026-11-06, Logs/TerrainGen/river_shape179.txt): no D8
        /// kink survives (turns > 40 deg on <= 5 % of cells, D8 had 17-40 %); the
        /// course meanders (offset p90 >= 0.5 W); water perched >= 0.5 m over the
        /// ground 3 m out on <= 12 % of banks and <= a third of the pre-179 share
        /// above a 5 % floor
        /// (measured 5-7 % all, 9-11 % walled, vs 41-64 %); Sheer banks, surface
        /// jumps no worse than pre-179; dry section centres at most 5 points
        /// worse (a river beside a lake and under its level is buried by the
        /// lake's rim, ShapeField.LakeRim, in both arms: ~40 % of 'lake' cells;
        /// incised rivers are lower, so a few more are: 63 vs 56 of 150, seed 7).
        [Test]
        [Category("Slow")]   // four shared maps + four pre-179 maps, ~1 min of Burst sections
        public void Shaped_Rivers_FourSeeds()
        {
            var sb = new StringBuilder("Task 179 river shape: SHAPED (default) vs PRE-179 (-rivershape 0); river cells with a river downstream.\n");
            var fails = new StringBuilder();
            foreach (uint seed in Seeds)
            {
                var spec = TerrainSpec.SizeClass2(seed, 4);
                var map = SharedMaps.Get(spec);
                var offSpec = spec; offSpec.riverShapeOff = 1;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                Agg[] before, after;
                var ex = new StringBuilder();
                double s0, s1;
                using (var offMap = WorldMap.Build(offSpec))
                {
                    double build = sw.Elapsed.TotalSeconds;
                    before = Measure(offSpec, offMap, out s0);
                    after = Measure(spec, map, out s1, seed == 31337u ? ex : null);
                    sb.AppendLine($"seed {seed}  (pre-179 map {build:F1} s, sections {s0:F1} / {s1:F1} s; raised cells {offMap.Hydro.riverRaisedCells} -> {map.Hydro.riverRaisedCells} of {map.Hydro.riverCells})");
                }
                if (ex.Length > 0) sb.Append("  examples (SHEER: every voxel from 0.8 m inside the edge to 1.6 m out; PERCHED: every 0.2 m to 3.2 m out; groundY[wWaterY], L = lip):\n" + ex);
                for (int a = 0; a < 4; a++)
                {
                    sb.AppendLine("  before " + Line(AggName[a], before[a]));
                    sb.AppendLine("  after  " + Line(AggName[a], after[a]));
                    var A = after[a]; var B = before[a];
                    if (A.cells == 0) continue;
                    string tag = $"seed {seed} {AggName[a]}";
                    float p5A = (float)A.perched5 / math.max(1, A.sides), p5B = (float)B.perched5 / math.max(1, B.sides);
                    float sheerA = (float)A.sheer / math.max(1, A.sides), sheerB = (float)B.sheer / math.max(1, B.sides);
                    float stepA = (float)A.big / math.max(1, A.steps), stepB = (float)B.big / math.max(1, B.steps);
                    if (A.turn40 > 0.05f * A.cells) fails.AppendLine($"{tag}: {A.turn40} of {A.cells} cells turn > 40 deg");
                    if (a == 0 && Pct(A.offs, 0.9f) < 0.5f) fails.AppendLine($"{tag}: offset p90 {Pct(A.offs, 0.9f):F2} W < 0.5 W (no meanders)");
                    if (A.sides >= 50 && (p5A > 0.12f || (p5A > 0.05f && p5A > p5B / 3f))) fails.AppendLine($"{tag}: perched >= 0.5 m {p5A:P1} (pre-179 {p5B:P1})");
                    if (A.sides >= 50 && sheerA > sheerB + 0.02f) fails.AppendLine($"{tag}: Sheer banks {sheerA:P1} (pre-179 {sheerB:P1})");
                    if (stepA > stepB + 0.001f) fails.AppendLine($"{tag}: {A.big} surface jumps > 1 voxel in {A.steps} samples (pre-179 {B.big} in {B.steps})");
                    if (A.dryCentre > B.dryCentre + math.max(2, 0.05f * A.cells)) fails.AppendLine($"{tag}: {A.dryCentre} of {A.cells} sections dry at the centre (pre-179 {B.dryCentre})");
                }
            }
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "TerrainGen");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "river_shape179.txt"), sb + (fails.Length > 0 ? "\nFAILS:\n" + fails : "\nPASS\n"));
            UnityEngine.Debug.Log("[river shape 179]\n" + sb);
            Assert.AreEqual("", fails.ToString(), fails.ToString());
        }
    }
}
