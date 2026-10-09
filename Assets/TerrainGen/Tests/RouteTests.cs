// Assets/TerrainGen/Tests/RouteTests.cs
//
// Surface rebuild B8 (plan rule 9; owner Q3 (a), Q4 (a), Q5 (a)): routes between
// the easy areas (Routes.cs). Acceptance over seeds 42 / 7 / 2026 / 31337:
//   graph        every route ends in a (final) easy area; the easy-area graph is
//                connected and every area has 2+ links
//   danger       on 80%+ of route length the corridor's danger is >= 1 tier
//                below the land 100-250 m to either side (median of the land
//                samples, both sides together). Reported two ways: STRICT, and
//                FLOORED (where that land is already danger 0 a danger-0
//                corridor counts: it cannot go lower). The gate is FLOORED --
//                see the report's note; STRICT is printed beside it.
//   bands        Walk + Scramble on 85%+ of route length, 0 m of Sheer, from the
//                VOXEL ground (ShapeField.Sample, the LOD-0 top) every 0.5 m
//                along the centre line, slope = central differences over 1 m in
//                x and z. Fords (water over the column) are their own class and
//                count against Walk + Scramble.
//   detour       centre-line length <= 1.6 x the straight line between its ends
//   in sight     the next waypoint (a pass, a signature feature within 300 m of
//                the route, the destination's end) is in line of sight (2 m eye
//                over the map surface) from 65%+ of route length (was 70%, owner Q33 (a)), both ways
//   passes       lowered <= 20 m; second crossings of Climb/Sheer bands within
//                500 m REPORTED only
//   build        the map (with the record twice and the routes) <= 32 s
// Writes Logs/TerrainGen/routes/b8_routes.txt; REPORTS=1 adds a route PNG per
// seed. The 20-seed sweep (graph only, map only) is opt-in:
//   ROUTESWEEP=1 tools/run-editmode-filter.sh RouteTests
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace TerrainGen.Tests
{
    public class RouteTests
    {
        private static readonly uint[] Seeds = { 42u, 7u, 2026u, 31337u };
        // MIN_IN_SIGHT was 0.70f. owner Q33 (a) 2026-10-09: canyon tablelands lengthen routes; seed 42 = 66.3%
        public const float MIN_SAFER = 0.80f, MIN_EASY_BANDS = 0.85f, MAX_DETOUR = 1.6f, MIN_IN_SIGHT = 0.65f;
        public const double MAX_BUILD_MS = 32000.0;

        // ------------------------------------------------------------ fast tests

        [Test]
        public void Passes_AreCrestsOverBothSides_AndSmoothKeepsTheEnds()
        {
            // a 15 m crest between two lows, a 5 m bump that is not a pass
            var prof = new float[] { 0, 50, 100, 150, 100, 60, 80, 110, 80, 20, 0 };
            CollectionAssert.AreEqual(new[] { 3 }, Routes.Passes(prof));
            var path = new[] { 0, 1, 2, 1024 + 3, 2048 + 4, 2048 + 5, 2048 + 6 };
            var line = Routes.Smooth(path, 1024, 80f);
            Assert.AreEqual(new float2(40f, 40f), line[0]);
            Assert.AreEqual(new float2(6.5f * 80f, 2.5f * 80f), line[line.Length - 1]);
        }

        // ------------------------------------------------------------ the gate

        public sealed class Result
        {
            public List<string> fails = new List<string>();
            public string numbers = "", detail = "";
        }

        /// Every acceptance number of one v4 map; `full` adds the voxel bands,
        /// the danger margin, the sight lines and the crossings (the sweep only
        /// checks the graph).
        public static Result Check(WorldMap map, bool full, double buildMs)
        {
            var res = new Result();
            var rec = map.Record;
            var spec = map.Spec;
            int n = spec.mapCells; float cv = spec.mapCellVoxels;
            var routes = rec.RouteList;
            var areas = rec.EasyAreaList;
            var sb = new StringBuilder();
            if (routes == null || routes.Length == 0) { res.fails.Add("no routes"); return res; }

            // ---- graph, on the FINAL easy areas ----
            var owner = new int[n * n];
            for (int a = 0; a < areas.Length; a++) foreach (int i in areas[a].cells) owner[i] = a + 1;
            int AreaAt(int2 p)
            {
                int cx = (int)(p.x / cv), cz = (int)(p.y / cv), best = 0, bd = int.MaxValue;
                for (int z = math.max(0, cz - 5); z <= math.min(n - 1, cz + 5); z++)
                for (int x = math.max(0, cx - 5); x <= math.min(n - 1, cx + 5); x++)
                {
                    int o = owner[z * n + x], d = (x - cx) * (x - cx) + (z - cz) * (z - cz);
                    if (o > 0 && d < bd) { bd = d; best = o; }
                }
                return best - 1;
            }
            var deg = new int[areas.Length];
            var comp = Enumerable.Range(0, areas.Length).ToArray();
            int Find(int a) { while (comp[a] != a) a = comp[a] = comp[comp[a]]; return a; }
            var pairs = new HashSet<int>();
            foreach (var rt in routes)
            {
                int a = AreaAt(rt.endA), b = AreaAt(rt.endB);
                if (rt.mapVersion != spec.mapVersion) res.fails.Add($"route mapVersion {rt.mapVersion}");
                if (a < 0 || b < 0 || a == b) { res.fails.Add($"route {rt.areaA}-{rt.areaB}: its ends are not in two easy areas ({a}, {b})"); continue; }
                if (!pairs.Add(math.min(a, b) * 64 + math.max(a, b))) res.fails.Add($"areas {a} and {b} linked twice");
                deg[a]++; deg[b]++;
                comp[Find(a)] = Find(b);
            }
            int roots = 0; for (int a = 0; a < areas.Length; a++) if (Find(a) == a) roots++;
            if (roots != 1) res.fails.Add($"easy-area graph has {roots} parts");
            for (int a = 0; a < areas.Length; a++) if (deg[a] < 2) res.fails.Add($"area {a} has {deg[a]} link(s)");
            // corridor flag == the routes' corridor lists
            var inCorr = new bool[n * n];
            foreach (var rt in routes) foreach (int i in rt.corridor) inCorr[i] = true;
            int flagBad = 0;
            for (int i = 0; i < n * n; i++) if (rec.Data[i].IsRoute != inCorr[i]) flagBad++;
            if (flagBad > 0) res.fails.Add($"{flagBad} cells where FLAG_ROUTE and the corridor lists disagree");
            float totalKm = routes.Sum(r => r.lengthM) / 1000f;
            res.numbers = $"routes {routes.Length} ({routes.Count(r => r.tree)} tree + {routes.Count(r => !r.tree)} extra), areas {areas.Length}, links/area {string.Join(" ", deg)}, {totalKm:F1} km";
            if (!full) return res;

            // ---- per route ----
            var view = map.View; var o = Offsets.For(in spec);
            var feats = rec.FeatureList;
            var fpList = new List<int>[feats.Length];
            for (int f = 0; f < feats.Length; f++) fpList[f] = new List<int>();
            for (int i = 0; i < n * n; i++) if (rec.Data[i].IsFeature) fpList[rec.Data[i].site - 1].Add(i);
            var footprints = fpList.Select(l => l.ToArray()).ToArray();
            var sheerAt = new List<string>(); var sheerSeen = new HashSet<int>();
            double sumL = 0, safeStrict = 0, safeFloor = 0, sideLess = 0;
            var bandM = new double[5];   // Walk, Scramble, Climb, Sheer, ford
            double sightW = 0, sightAny = 0, sightL = 0;
            var sightVar = new double[SightResult.VARIANTS]; var sightTypeL = new double[3]; var sightTypeV = new double[3];
            float maxCut = 0f, worstDetour = 0f;
            int crossings = 0, withSecond = 0;
            var crossText = new List<string>();
            foreach (var rt in routes)
            {
                int L = rt.line.Length;
                var seg = new float[L];
                for (int k = 1; k < L; k++) seg[k] = math.distance(rt.line[k], rt.line[k - 1]);
                // danger margin
                double rs = 0, rf = 0, rl = 0, rn = 0;
                for (int k = 0; k < L; k++)
                {
                    float w = 0.5f * ((k > 0 ? seg[k] : 0f) + (k < L - 1 ? seg[k + 1] : 0f));
                    float2 p = rt.line[k];
                    float2 dir = math.normalizesafe(rt.line[math.min(L - 1, k + 2)] - rt.line[math.max(0, k - 2)]);
                    float2 side = new float2(-dir.y, dir.x);
                    var ds = new List<int>();
                    for (float s = 1000f; s <= 2500f; s += 250f)
                    for (int sg = -1; sg <= 1; sg += 2)
                    {
                        int ci = Routes.CellOf(p + sg * s * side, n, cv);
                        if (map.Height[ci] <= 0f) continue;
                        ds.Add(rec.Data[ci].Danger);
                    }
                    rl += w;
                    if (ds.Count == 0) { rn += w; continue; }
                    ds.Sort();
                    int sideD = ds[ds.Count / 2];
                    int cd = rec.Data[Routes.CellOf(p, n, cv)].Danger;
                    if (cd <= sideD - 1) { rs += w; rf += w; }
                    else if (sideD == 0 && cd == 0) rf += w;
                }
                sumL += rl - rn; safeStrict += rs; safeFloor += rf; sideLess += rn;
                // voxel bands every 0.5 m
                var rb = new double[5];
                for (int k = 1; k < L; k++)
                {
                    int steps = math.max(1, (int)math.round(seg[k] / 5f));
                    for (int s = 0; s < steps; s++)
                    {
                        float2 p = math.lerp(rt.line[k - 1], rt.line[k], (s + 0.5f) / steps);
                        var c0 = ShapeField.Sample(in spec, in view, in o, p.x, p.y, 1);
                        int cls;
                        if (c0.water != ColumnSample.NO_WATER_Y && c0.water > c0.height) cls = 4;
                        else
                        {
                            float hx1 = ShapeField.Sample(in spec, in view, in o, p.x + 5f, p.y, 1).height;
                            float hx0 = ShapeField.Sample(in spec, in view, in o, p.x - 5f, p.y, 1).height;
                            float hz1 = ShapeField.Sample(in spec, in view, in o, p.x, p.y + 5f, 1).height;
                            float hz0 = ShapeField.Sample(in spec, in view, in o, p.x, p.y - 5f, 1).height;
                            float gx = (hx1 - hx0) / 10f, gz = (hz1 - hz0) / 10f;
                            cls = Danger.BandOfTan2(gx * gx + gz * gz);
                        }
                        rb[cls] += seg[k] / steps / 10f;
                        if (cls == Danger.Sheer && sheerAt.Count < 16 && !sheerSeen.Contains(rt.areaA * 100000 + k))
                        {
                            sheerSeen.Add(rt.areaA * 100000 + k);
                            int c9 = Routes.CellOf(p, n, cv), x9 = c9 & (n - 1), z9 = c9 / n;
                            var nb = new StringBuilder(" 3x3 m [");
                            for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++)
                            {
                                int j = math.clamp(z9 + dz, 0, n - 1) * n + math.clamp(x9 + dx, 0, n - 1);
                                var rj = rec.Data[j];
                                nb.Append($"{map.Height[j] / 10f:F1}{(rj.IsEasy ? "e" : "")}{(rj.IsFeature ? "f" : "")}{(map.WaterClass[j] != 0 ? "w" : "")}{(rj.IsRoute ? "" : "x")}");
                                if (Routes.DiagH0 != null && Routes.DiagH0.Length == n * n)
                                    nb.Append($"(h0 {Routes.DiagH0[j] / 10f:F1} lo {Routes.DiagLo[j] / 10f:F1} hi {Routes.DiagHi[j] / 10f:F1}{(Routes.DiagSkip[j] ? " SKIP" : "")})");
                                nb.Append(" ");
                            }
                            nb.Append($"] vox c {c0.height} ledge {c0.open3Hi > c0.open3Lo} open1 {c0.open1Hi > c0.open1Lo}");
                            nb.Append($" x+ {ShapeField.Sample(in spec, in view, in o, p.x + 5f, p.y, 1).height} x- {ShapeField.Sample(in spec, in view, in o, p.x - 5f, p.y, 1).height} z+ {ShapeField.Sample(in spec, in view, in o, p.x, p.y + 5f, 1).height} z- {ShapeField.Sample(in spec, in view, in o, p.x, p.y - 5f, 1).height}");
                            sheerAt.Add(nb.ToString());
                            var rc0 = rec.Data[Routes.CellOf(p, n, cv)];
                            sheerAt.Add($"{rt.areaA}-{rt.areaB}@{k} ({p.x / 10f:F0},{p.y / 10f:F0}) cut {rt.cut[k] / 10f:F1} fill {rt.fill[k] / 10f:F1} before {rt.bandBefore[k]} now {rc0.MoveBand}{(rc0.IsFeature ? " feature" : "")}{(rc0.IsEasy ? " easy" : "")} wc {map.WaterClass[Routes.CellOf(p, n, cv)]} rough {map.Rough[Routes.CellOf(p, n, cv)]:F2} mapdeg {math.degrees(math.atan(math.sqrt(Danger.SlopeTan2(map.Height, n, (int)cv, Routes.CellOf(p, n, cv))))):F0} wetnbr {WetNeighbours(map, Routes.CellOf(p, n, cv))} stamp {c0.stamp3D}");
                        }
                    }
                }
                for (int c = 0; c < 5; c++) bandM[c] += rb[c];
                // in sight, both ways
                int aA = AreaAt(rt.endA), aB = AreaAt(rt.endB);
                var sr = new SightResult();
                Sight(map, rt, aA >= 0 ? areas[aA].cells : null, aB >= 0 ? areas[aB].cells : null, feats, footprints, sr);
                double sl = sr.len, sw = sr.vis[0], sa = sr.any;
                for (int v = 0; v < sightVar.Length; v++) sightVar[v] += sr.vis[v];
                for (int t = 0; t < 3; t++) { sightTypeL[t] += sr.typeLen[t]; sightTypeV[t] += sr.typeVis[t]; }
                sightL += sl; sightW += sw; sightAny += sa;
                // cuts, detour
                float rc = rt.cut.Max();
                maxCut = math.max(maxCut, rc);
                float detour = rt.lengthM / math.max(1f, rt.straightM);
                worstDetour = math.max(worstDetour, detour);
                if (detour > MAX_DETOUR) res.fails.Add($"route {rt.areaA}-{rt.areaB} detour {detour:F2}x > {MAX_DETOUR}x");
                // second crossings near Climb/Sheer runs (the bands before easing)
                for (int k = 0; k < L; )
                {
                    if (rt.bandBefore[k] < Danger.Climb) { k++; continue; }
                    int k1 = k; while (k1 + 1 < L && rt.bandBefore[k1 + 1] >= Danger.Climb) k1++;
                    crossings++;
                    bool second = SecondCrossing(map, rt, math.max(0, k - 2), math.min(L - 1, k1 + 2), inCorr);
                    if (second) withSecond++;
                    float runM = 0f; for (int j = k + 1; j <= k1; j++) runM += seg[j] / 10f;
                    crossText.Add($"{rt.areaA}-{rt.areaB}@{k}: {math.max(8f, runM):F0} m {(rt.bandBefore.Skip(k).Take(k1 - k + 1).Max() == Danger.Sheer ? "Sheer" : "Climb")}, second within 500 m: {(second ? "yes" : "NO")}");
                    k = k1 + 1;
                }
                var cutsAtPasses = rt.passes.Select(k => (rt.cut[k] / 10f).ToString("F1"));
                // the pose for a picture: the middle of the line, looking ~150 m along it
                int km = L / 2, ka = km; float ahead = 0f;
                while (ka < L - 1 && ahead < 1500f) { ahead += seg[ka + 1]; ka++; }
                // and the deepest cut, seen from ~80 m back along the line
                int kc = 0; for (int k = 1; k < L; k++) if (rt.cut[k] > rt.cut[kc]) kc = k;
                int kb = kc; float back = 0f;
                while (kb > 0 && back < 800f) { back += seg[kb]; kb--; }
                sb.AppendLine($"    route {rt.areaA}-{rt.areaB}{(rt.tree ? "" : " (extra)")}: {rt.lengthM:F0} m (straight {rt.straightM:F0} m, {detour:F2}x), width {rt.widthM:F0} m, overlap {rt.overlap * 100f:F0}%, " +
                              $"W/S/C/Sh/ford {Pct(rb[0], rb)}/{Pct(rb[1], rb)}/{Pct(rb[2], rb)}/{Pct(rb[3], rb)}/{Pct(rb[4], rb)}, Sheer {rb[3]:F1} m, " +
                              $"safer strict {100 * rs / math.max(1e-6, rl - rn):F0}% floored {100 * rf / math.max(1e-6, rl - rn):F0}%, in sight {100 * sw / math.max(1e-6, sl):F0}% (any landmark {100 * sa / math.max(1e-6, sl):F0}%), " +
                              $"passes {rt.passes.Length} cut m [{string.Join(" ", cutsAtPasses)}], deepest cut {rc / 10f:F1} m, " +
                              $"mid {rt.line[km].x / 10f:F1},{rt.line[km].y / 10f:F1} look {rt.line[ka].x / 10f:F1},{rt.line[ka].y / 10f:F1}, " +
                              $"deepest cut at {rt.line[kc].x / 10f:F1},{rt.line[kc].y / 10f:F1} (from {rt.line[kb].x / 10f:F1},{rt.line[kb].y / 10f:F1})");
            }
            double bl = bandM.Sum();
            float easyBands = (float)((bandM[0] + bandM[1]) / bl);
            float strict = (float)(safeStrict / sumL), floored = (float)(safeFloor / sumL), sight = (float)(sightW / sightL);
            res.numbers += $"; Walk+Scramble {easyBands * 100f:F1}% (W {Pct(bandM[0], bandM)} S {Pct(bandM[1], bandM)} C {Pct(bandM[2], bandM)} Sh {Pct(bandM[3], bandM)} ford {Pct(bandM[4], bandM)}), Sheer {bandM[3]:F1} m; " +
                           $"safer strict {strict * 100f:F1}% floored {floored * 100f:F1}% (no land beside {sideLess / 10.0:F0} m); detour max {worstDetour:F2}x; " +
                           $"in sight {sight * 100f:F1}% (any landmark {sightAny / sightL * 100.0:F1}%); deepest cut {maxCut / 10f:F1} m; " +
                           $"Climb/Sheer crossings {crossings}, with a second within 500 m {withSecond}; build {buildMs / 1000.0:F1} s";
            sb.Append("    in sight by variant:");
            for (int v = 0; v < sightVar.Length; v++) sb.Append($" {SightResult.Names[v]} {100.0 * sightVar[v] / sightL:F1}%;");
            sb.Append("\n    by next waypoint (variant 0): ");
            string[] tn = { "pass", "feature", "end" };
            for (int t = 0; t < 3; t++) sb.Append($"{tn[t]} {sightTypeL[t] / 1000.0:F2} km, {100.0 * sightTypeV[t] / math.max(1e-6, sightTypeL[t]):F0}% seen; ");
            sb.AppendLine();
            if (sheerAt.Count > 0) sb.AppendLine("    Sheer samples (first 16): " + string.Join("; ", sheerAt));
            res.detail = sb + "    crossings: " + (crossText.Count == 0 ? "none" : string.Join("; ", crossText)) + "\n";
            if (easyBands < MIN_EASY_BANDS) res.fails.Add($"Walk+Scramble {easyBands * 100f:F1}% < {MIN_EASY_BANDS * 100f}%");
            if (bandM[3] > 0) res.fails.Add($"{bandM[3]:F1} m of Sheer on the voxel ground");
            if (floored < MIN_SAFER) res.fails.Add($"corridor >= 1 tier safer on {floored * 100f:F1}% < {MIN_SAFER * 100f}%");
            if (sight < MIN_IN_SIGHT) res.fails.Add($"next waypoint in sight {sight * 100f:F1}% < {MIN_IN_SIGHT * 100f}%");
            if (maxCut > Routes.CUT_MAX + 0.01f) res.fails.Add($"a pass lowered {maxCut / 10f:F1} m > 20 m");
            if (buildMs > MAX_BUILD_MS) res.fails.Add($"map build {buildMs / 1000.0:F1} s > {MAX_BUILD_MS / 1000.0} s");
            return res;
        }

        private static int WetNeighbours(WorldMap map, int i)
        {
            int n = map.Spec.mapCells, x = i & (n - 1), z = i / n, c = 0;
            for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++)
            {
                int xx = math.clamp(x + dx, 0, n - 1), zz = math.clamp(z + dz, 0, n - 1);
                if (map.WaterClass[zz * n + xx] != Hydrology.DRY) c++;
            }
            return c;
        }

        private static string Pct(double v, double[] all) => $"{100.0 * v / all.Sum():F1}";

        private static float Surf(WorldMap map, float2 p) => Features.Surface(map, Routes.CellOf(p, map.Spec.mapCells, map.Spec.mapCellVoxels));

        public sealed class SightResult
        {
            public const int VARIANTS = 4;
            public static readonly string[] Names =
            {
                "[0 GATE] pass +2 m, feature = any footprint cell +2 m, end = the end point or any destination-area cell +2 m",
                "[1] as 0, the end = the end point only",
                "[2] as 0, targets +5 m (a cairn)",
                "[3] as 0, eye 1.6 m",
            };
            public double len, any;
            public double[] vis = new double[VARIANTS];
            public double[] typeLen = new double[3], typeVis = new double[3];
        }

        /// Both ways along the route: from each line point (2 m eye over the map
        /// surface), is the next waypoint ahead in line of sight? Waypoints: the
        /// passes, a signature feature within 300 m of the route (at its nearest
        /// line point; seen at its centre +10 m), the far end.
        public static void Sight(WorldMap map, Route rt, int[] cellsA, int[] cellsB, Feature[] feats, int[][] footprints, SightResult r)
        {
            int n = map.Spec.mapCells; float cv = map.Spec.mapCellVoxels;
            int L = rt.line.Length;
            float2 CellPos(int i) => new float2(((i & (n - 1)) + 0.5f) * cv, ((i / n) + 0.5f) * cv);
            int[] Spread(int[] cells)
            {
                if (cells == null || cells.Length == 0) return new int[0];
                int step = math.max(1, cells.Length / 48);
                var l = new List<int>(); for (int j = 0; j < cells.Length; j += step) l.Add(cells[j]);
                return l.ToArray();
            }
            for (int dirn = 0; dirn < 2; dirn++)
            {
                float2[] line = dirn == 0 ? rt.line : rt.line.Reverse().ToArray();
                int[] dest = Spread(dirn == 0 ? cellsB : cellsA);
                var wps = new List<(int k, int type, float2 t, float ht, int[] cells)>();
                foreach (int pk in rt.passes) { int k = dirn == 0 ? pk : L - 1 - pk; wps.Add((k, 0, line[k], Surf(map, line[k]), null)); }
                for (int fi = 0; fi < feats.Length; fi++)
                {
                    // the feature's footprint cell nearest the route, within 300 m
                    int bk = -1; float bd = float.MaxValue;
                    for (int ci = 0; ci < footprints[fi].Length; ci += 4)
                    {
                        float2 q = CellPos(footprints[fi][ci]);
                        for (int k = 0; k < L; k += 2) { float d = math.distancesq(line[k], q); if (d < bd) { bd = d; bk = k; } }
                    }
                    if (bk >= 0 && bd <= 3000f * 3000f) wps.Add((bk, 1, (float2)feats[fi].centre, Surf(map, (float2)feats[fi].centre), Spread(footprints[fi])));
                }
                wps.Add((L - 1, 2, line[L - 1], Surf(map, line[L - 1]), dest));
                wps.Sort((a, b) => a.k != b.k ? a.k.CompareTo(b.k) : a.type.CompareTo(b.type));
                for (int k = 0; k < L - 1; k++)
                {
                    float w = math.distance(line[k], line[k + 1]) / 10f;
                    float2 e = line[k]; float ground = Surf(map, e);
                    int wi = 0; while (wi < wps.Count && wps[wi].k <= k) wi++;
                    var wp = wps[math.min(wi, wps.Count - 1)];
                    bool near = math.distance(e, wp.t) < 160f;
                    bool Los(float eye, float lift, bool cellsToo)
                    {
                        if (near) return true;
                        if (wp.type != 1 && Features.LineOfSight(map, e, ground + eye, wp.t, wp.ht + lift)) return true;
                        if (cellsToo && wp.cells != null)
                            foreach (int c in wp.cells)
                                if (math.distance(e, CellPos(c)) < 160f || Features.LineOfSight(map, e, ground + eye, CellPos(c), Features.Surface(map, c) + lift)) return true;
                        return false;
                    }
                    bool v0 = Los(20f, 20f, true);
                    bool[] v = { v0, wp.type == 2 ? Los(20f, 20f, false) : v0, Los(20f, 50f, true), Los(16f, 20f, true) };
                    r.len += w;
                    for (int j = 0; j < v.Length; j++) if (v[j]) r.vis[j] += w;
                    r.typeLen[wp.type] += w; if (v0) r.typeVis[wp.type] += w;
                    bool any = v0;
                    if (!any)
                        for (int fi = 0; fi < feats.Length && !any; fi++)
                            foreach (int c in Spread(footprints[fi]))
                                if (math.distance(e, CellPos(c)) < 30000f && Features.LineOfSight(map, e, ground + 20f, CellPos(c), Features.Surface(map, c) + 20f)) { any = true; break; }
                    if (any) r.any += w;
                }
            }
        }

        /// Is there another Walk/Scramble way (record bands) across from the cell
        /// before a Climb/Sheer run to the cell after it, inside 500 m of the
        /// crossing and outside every corridor?
        private static bool SecondCrossing(WorldMap map, Route rt, int k0, int k1, bool[] inCorr)
        {
            int n = map.Spec.mapCells; float cv = map.Spec.mapCellVoxels;
            float2 mid = 0.5f * (rt.line[k0] + rt.line[k1]);
            int r = (int)(5000f / cv), cx = (int)(mid.x / cv), cz = (int)(mid.y / cv);
            int s = Routes.CellOf(rt.line[k0], n, cv), t = Routes.CellOf(rt.line[k1], n, cv);
            int tx = t & (n - 1), tz = t / n;
            bool Ok(int x, int z)
            {
                if ((x - cx) * (x - cx) + (z - cz) * (z - cz) > r * r) return false;
                int i = z * n + x;
                var c = map.Record.Data[i];
                return map.Height[i] > 0f && c.MoveBand <= Danger.Scramble && map.WaterClass[i] != Hydrology.LAKE && !inCorr[i];
            }
            // start from the Walk/Scramble cells just outside the corridor near each end
            var seen = new HashSet<int>(); var q = new Queue<int>();
            int sx = s & (n - 1), sz = s / n;
            for (int z = sz - 8; z <= sz + 8; z++) for (int x = sx - 8; x <= sx + 8; x++)
                if (x >= 0 && z >= 0 && x < n && z < n && Ok(x, z) && seen.Add(z * n + x)) q.Enqueue(z * n + x);
            while (q.Count > 0)
            {
                int i = q.Dequeue(), x = i & (n - 1), z = i / n;
                if (math.abs(x - tx) <= 8 && math.abs(z - tz) <= 8) return true;
                for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++)
                {
                    int xx = x + dx, zz = z + dz;
                    if (xx < 0 || zz < 0 || xx >= n || zz >= n || !Ok(xx, zz)) continue;
                    if (seen.Add(zz * n + xx)) q.Enqueue(zz * n + xx);
                }
            }
            return false;
        }

        [Test]
        [Category("Slow")]   // four shared v4 maps
        public void V4_FourSeeds_RoutesJoinTheEasyAreas()
        {
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "TerrainGen", "routes");
            Directory.CreateDirectory(dir);
            bool pics = Environment.GetEnvironmentVariable("REPORTS") == "1";
            var report = new StringBuilder("B8 routes between the easy areas (map v4, Routes.cs). Gate on 'safer' is FLOORED (a danger-0 corridor beside danger-0 land counts); STRICT beside it.\n\n");
            var fails = new StringBuilder();
            uint[] seeds = Seeds;
            string only = Environment.GetEnvironmentVariable("ROUTE_SEEDS");   // iteration aid: a subset
            if (!string.IsNullOrEmpty(only)) seeds = only.Split(',').Select(x => uint.Parse(x.Trim())).ToArray();
            Routes.Diagnose = Environment.GetEnvironmentVariable("ROUTE_DIAG") == "1";   // iteration aid
            foreach (uint seed in seeds)
            {
                var spec = TerrainSpec.SizeClass2(seed, 4);
                var entry = SharedMaps.GetEntry(spec);
                var map = entry.Map;
                var r = Check(map, true, entry.BuildMs);
                report.AppendLine($"  seed {seed,-6} {(r.fails.Count == 0 ? "PASS" : "FAIL")}  {r.numbers}");
                report.AppendLine($"      record {map.Record.Hash():X16}  map {DangerTests.MapHash(map):X16}");
                report.AppendLine($"      build: {map.RouteBuild}");
                report.Append(r.detail);
                foreach (var f in r.fails) fails.AppendLine($"seed {seed}: {f}");
                if (pics) WritePng(Path.Combine(dir, $"routes_seed{seed}.png"), map);
            }
            Routes.Diagnose = false; Routes.DiagH0 = Routes.DiagLo = Routes.DiagHi = null; Routes.DiagSkip = null;
            File.WriteAllText(Path.Combine(dir, "b8_routes.txt"), report.ToString());
            Debug.Log("[B8 routes]\n" + report);
            Assert.AreEqual("", fails.ToString(), fails.ToString());
        }

        [Test]
        [Timeout(3600000)]
        public void Sweep20_Routes_GraphConnected()
        {
            if (Environment.GetEnvironmentVariable("ROUTESWEEP") != "1")
                Assert.Ignore("opt-in, ~9 min: ROUTESWEEP=1 tools/run-editmode-filter.sh RouteTests");
            var sb = new StringBuilder("=== B8 20-seed map-only sweep (v4): easy-area graph connected, every area 2+ links\n");
            var failed = new List<string>();
            foreach (uint seed in EasyAreaTests.SweepSeeds)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                using var map = WorldMap.Build(TerrainSpec.SizeClass2(seed, 4));
                double ms = sw.Elapsed.TotalMilliseconds;
                var r = Check(map, false, ms);
                if (ms > MAX_BUILD_MS) r.fails.Add($"build {ms / 1000.0:F1} s > 32 s");
                // B4: the final record keeps the first record's easy-area LIST
                // (WorldRecord.Build's easyFrom), so the list-level checks (count,
                // size, share) read what the map had before B8 -- reported, not
                // B8's gate; the per-cell checks are (the easing must not spoil
                // an area: Routes.Easable's collar)
                var easy = EasyAreaTests.Check(map, out string easyNums);
                var listLevel = new List<string>();
                foreach (var f in easy)
                    if (f.Contains("easy areas, want") || f.StartsWith("an area of") || f.StartsWith("easy share")) listLevel.Add(f);
                    else r.fails.Add("B4: " + f);
                if (listLevel.Count > 0) easyNums += "  [B4 list, before B8: " + string.Join("; ", listLevel) + "]";
                sb.AppendLine($"  seed {seed,-6} {(r.fails.Count == 0 ? "PASS" : "FAIL")}  {r.numbers}; B4 {easyNums}; build {ms / 1000.0:F1} s" +
                              (r.fails.Count > 0 ? "  -- " + string.Join("; ", r.fails) : ""));
                sb.AppendLine($"      {Routes.LastBuild}");
                if (r.fails.Count > 0) failed.Add($"{seed}: {string.Join("; ", r.fails)}");
            }
            sb.AppendLine($"  {EasyAreaTests.SweepSeeds.Length - failed.Count} of {EasyAreaTests.SweepSeeds.Length} pass");
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "TerrainGen", "routes");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "routesweep.txt"), sb.ToString());
            Debug.Log(sb.ToString());
            Assert.IsEmpty(failed, $"{failed.Count} seeds fail:\n" + string.Join("\n", failed));
        }

        // ------------------------------------------------------------- picture

        /// 1024 px = 8 m per pixel. Base: danger 0-7 (DangerTests' ramp), faded;
        /// sea dark blue, lakes/rivers light blue; easy areas green; signature
        /// features magenta; corridors cyan; centre lines black; passes red
        /// squares; route ends white squares.
        private static void WritePng(string path, WorldMap map)
        {
            var rec = map.Record;
            int n = map.Spec.mapCells; float cv = map.Spec.mapCellVoxels;
            var px = new Color32[n * n];
            for (int i = 0; i < n * n; i++)
            {
                var c = rec.Data[i];
                Color32 col = map.Height[i] <= 0f ? new Color32(26, 46, 89, 255)
                            : map.WaterClass[i] != Hydrology.DRY ? new Color32(89, 153, 230, 255)
                            : c.IsEasy ? new Color32(70, 170, 80, 255)
                            : c.IsFeature ? new Color32(200, 90, 200, 255)
                            : Color32.Lerp(DangerTests.DangerCol[c.Danger], new Color32(255, 255, 255, 255), 0.35f);
                if (c.IsRoute && map.Height[i] > 0f) col = Color32.Lerp(col, new Color32(0, 200, 230, 255), 0.6f);
                px[(n - 1 - i / n) * n + (i & (n - 1))] = col;   // north (+z) up
            }
            void Dot(float2 p, int r, Color32 col)
            {
                int x0 = (int)(p.x / cv), z0 = (int)(p.y / cv);
                for (int dz = -r; dz <= r; dz++) for (int dx = -r; dx <= r; dx++)
                {
                    int x = x0 + dx, z = z0 + dz;
                    if (x >= 0 && z >= 0 && x < n && z < n) px[(n - 1 - z) * n + x] = col;
                }
            }
            foreach (var rt in rec.RouteList)
            {
                for (int k = 1; k < rt.line.Length; k++)
                    for (int s = 0; s < 8; s++) Dot(math.lerp(rt.line[k - 1], rt.line[k], s / 8f), 0, new Color32(0, 0, 0, 255));
                foreach (int k in rt.passes) Dot(rt.line[k], 3, new Color32(230, 20, 20, 255));
                Dot(rt.line[0], 3, new Color32(255, 255, 255, 255));
                Dot(rt.line[rt.line.Length - 1], 3, new Color32(255, 255, 255, 255));
            }
            var tex = new Texture2D(n, n, TextureFormat.RGB24, false);
            tex.SetPixels32(px); tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
        }
    }
}
