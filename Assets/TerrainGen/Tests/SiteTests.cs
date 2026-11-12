// Assets/TerrainGen/Tests/SiteTests.cs
//
// Task 187 (surface rebuild B7): the site planner (Sites.cs). An INDEPENDENT
// checker re-derives every disc rule from the map and the record (its own disc,
// brute-force Euclidean keep-outs, its own start zone read) instead of reusing
// the planner's helpers, then the per-seed counts table, the labelled map
// sources (Logs/TerrainGen/sites187/map_<seed>.png + sites_<seed>.txt, labelled
// by tools/sites187.py) and the eye-level poses (poses.txt, -wsfixed format).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace TerrainGen.Tests
{
    public class SiteTests
    {
        private static readonly uint[] Seeds = { 42u, 7u, 2026u, 31337u };
        private static string LogDir => Path.Combine(Directory.GetCurrentDirectory(), "Logs", "TerrainGen", "sites187");

        // ------------------------------------------------------------------
        // The adversarial island: n 512 (4 km), rough Climb land everywhere
        // but four small flat patches.
        // ------------------------------------------------------------------
        private const int AN = 512, ACV = 80;

        private static RecordCell Cell(byte arch, byte biome, int band, int danger, byte flags) =>
            new RecordCell { landform = arch, biome = biome, hazard = (byte)(danger | (band << 3)), flags = flags };

        private static bool InBox(int x, int z, int x0, int z0, int x1, int z1) => x >= x0 && x <= x1 && z >= z0 && z <= z1;

        /// patchBBand: the village patch's band (0 Walk; 1 = the negative control).
        /// ground: 0 = none (no ground check), 1 = the real ground equals the map
        /// cell height, 2 = as 1 plus a 3 m wall across patch B (x >= cell 309):
        /// every village disc there crosses it (the step rule's negative control).
        private static SiteInput Island(bool flatPatches, int patchBBand = 0, int ground = 0)
        {
            int n = AN, N = n * n;
            var inp = new SiteInput { n = n, cellVoxels = ACV, seaLevelY = 5120, h = new int[N], waterClass = new byte[N], rec = new RecordCell[N], blocked = new bool[N] };
            var lakes = new List<int>();
            for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                int i = z * n + x;
                int dx = x - 256, dz = z - 256;
                if (dx * dx + dz * dz > 230 * 230) { inp.h[i] = -100; inp.rec[i] = Cell(WorldRecord.SEA, 0, 0, 0, 0); continue; }
                inp.h[i] = 300 + (x * 37 + z * 91) % 200;
                bool hardBox = InBox(x, z, 380, 180, 429, 229);
                inp.rec[i] = Cell(hardBox ? Regions.Volcanic : Regions.Highlands, GenBiome.Forest, 2, 2, 0);
                bool lake = InBox(x, z, 100, 200, 104, 204) || InBox(x, z, 316, 305, 318, 307) || InBox(x, z, 215, 333, 216, 334);
                if (lake) { inp.waterClass[i] = Hydrology.LAKE; inp.h[i] = 100; lakes.Add(i); }
                if (z == 320 || z == 321) if (x >= 120 && x <= 400) inp.rec[i].flags |= WorldRecord.FLAG_ROUTE;
                if (!flatPatches) continue;
                if (InBox(x, z, 110, 190, 139, 219)) { inp.h[i] = 100; inp.rec[i] = Cell(Regions.Plains, GenBiome.Forest, 0, 0, WorldRecord.FLAG_EASY); }   // A: town
                if (InBox(x, z, 300, 300, 313, 313)) { inp.h[i] = 200; inp.rec[i] = Cell(Regions.Plains, GenBiome.Forest, patchBBand, 1, 0); }   // B: village
                if (InBox(x, z, 200, 330, 206, 336)) { inp.h[i] = 250; inp.rec[i] = Cell(Regions.Highlands, GenBiome.Forest, 0, 5, 0); }   // C: outpost (danger 5)
                if (InBox(x, z, 380, 200, 393, 213)) { inp.h[i] = 400; inp.rec[i] = Cell(Regions.Volcanic, GenBiome.Forest, 0, 4, 0); }   // D: frontier
            }
            // fresh water steps (16 m) by brute force from the lake cells
            for (int i = 0; i < N; i++)
            {
                if (inp.rec[i].IsSea) continue;
                long best = long.MaxValue;
                foreach (int l in lakes) { long dx = (i % n - l % n) * ACV, dz = (i / n - l / n) * ACV; best = math.min(best, dx * dx + dz * dz); }
                inp.rec[i].freshWater = (byte)math.min(WorldRecord.WATER_FAR, (int)(math.sqrt((double)best) / 160));
            }
            // a blocked strip (a stamp disc) over patch B's west columns
            for (int z = 300; z <= 313; z++) for (int x = 300; x <= 302; x++) inp.blocked[z * n + x] = true;
            var easy = new List<int>();
            for (int i = 0; i < N; i++) if (inp.rec[i].IsEasy) easy.Add(i);
            inp.easyCells = new[] { easy.ToArray() };
            var line = new List<float2>();
            for (int x = 120; x <= 400; x++) line.Add(new float2(x * ACV + 40, 320 * ACV + 40));
            inp.routeLines = new[] { line.ToArray() };
            if (ground > 0)
            {
                int[] hh = inp.h; int sea = inp.seaLevelY; bool wall = ground == 2;
                inp.ground = (x, z) =>
                {
                    int cx = math.clamp(x / ACV, 0, AN - 1), cz = math.clamp(z / ACV, 0, AN - 1);
                    bool w = wall && InBox(cx, cz, 300, 300, 313, 313) && x >= 309 * ACV;
                    return sea + hh[cz * AN + cx] + (w ? 30 : 0);
                };
            }
            return inp;
        }

        [Test]
        public void Adversarial_FewFlatCells_OnlyTheFlatPatches()
        {
            var inp = Island(true);
            var sites = Sites.Plan(inp, out var st);
            var sb = new StringBuilder("Adversarial island (4 km, rough Climb land, four flat patches A-D): " + st.Line() + "\n");
            foreach (var s in sites) sb.AppendLine("  " + Describe(s));
            Directory.CreateDirectory(LogDir);
            File.WriteAllText(Path.Combine(LogDir, "adversarial.txt"), sb.ToString());
            Debug.Log(sb.ToString());

            var fails = new List<string>();
            fails.AddRange(CheckDiscs(inp, sites, null));
            int K(byte k) => sites.Count(s => s.kind == k);
            if (K(Site.TOWN) != 1) fails.Add($"towns {K(Site.TOWN)}, want 1 (patch A)");
            if (K(Site.FRONTIER) != 1) fails.Add($"frontiers {K(Site.FRONTIER)}, want 1 (patch D)");
            if (K(Site.VILLAGE) != 1) fails.Add($"villages {K(Site.VILLAGE)}, want 1 (patch B)");
            if (K(Site.OUTPOST) != 1) fails.Add($"outposts {K(Site.OUTPOST)}, want 1 (patch C)");
            if (st.villages + st.outposts >= st.settleTarget) fails.Add($"settlements {st.villages + st.outposts} met the target {st.settleTarget}: the island has room for 2 only");
            foreach (var s in sites)
            {
                int cx = s.centre.x / ACV, cz = s.centre.y / ACV;
                bool inPatch = s.kind == Site.TOWN ? InBox(cx, cz, 110, 190, 139, 219) : s.kind == Site.VILLAGE ? InBox(cx, cz, 300, 300, 313, 313)
                             : s.kind == Site.OUTPOST ? InBox(cx, cz, 200, 330, 206, 336) : InBox(cx, cz, 380, 200, 393, 213);
                if (!inPatch) fails.Add($"{Describe(s)}: not in its patch");
            }
            // determinism
            if (Sites.Hash(Sites.Plan(Island(true), out _)) != Sites.Hash(sites)) fails.Add("two plans of the same island differ");
            // no flat ground: no site; the village patch on Scramble: no village (negative control)
            var none = Sites.Plan(Island(false), out var st0);
            if (none.Length != 0) fails.Add($"no flat ground but {none.Length} sites ({st0.Line()})");
            var scr = Sites.Plan(Island(true, 1), out _);
            if (scr.Any(s => s.kind == Site.VILLAGE)) fails.Add("negative control: a village on Scramble ground");
            Assert.IsEmpty(fails, string.Join("\n", fails));
        }

        /// THE STEP RULE (ground check): a 3 m wall the 8 m map does not see
        /// (the map heights stay flat) across the village patch removes the
        /// village; the same island with the ground equal to the map keeps all
        /// four sites, each with a 0 step.
        [Test]
        public void GroundCheck_StepRule_WallInDisc_NoVillage()
        {
            var fails = new List<string>();
            var flat = Island(true, 0, 1);
            var a = Sites.Plan(flat, out var sa);
            fails.AddRange(CheckDiscs(flat, a, null));
            if (a.Length != 4) fails.Add($"flat ground: {a.Length} sites, want 4 ({sa.Line()})");
            foreach (var s in a) if (s.groundStepVox != 0 || s.groundRangeVox != 0) fails.Add($"{Describe(s)}: ground step {s.groundStepVox} range {s.groundRangeVox}, want 0 / 0");
            var walled = Island(true, 0, 2);
            var b = Sites.Plan(walled, out var sb);
            fails.AddRange(CheckDiscs(walled, b, null));
            if (b.Any(s => s.kind == Site.VILLAGE)) fails.Add($"a village across the 3 m wall ({sb.Line()})");
            if (sb.stepRejects < 1) fails.Add($"no disc rejected by the step rule ({sb.Line()})");
            if (b.Count(s => s.kind != Site.VILLAGE) != 3) fails.Add($"walled: {b.Length} sites, want the other 3 kept");
            // the rule itself: 30 deg over 2 m is 1.155 m; 11 voxels pass, 12 fail
            if (Sites.StepTooSteep(11) || !Sites.StepTooSteep(12)) fails.Add("StepTooSteep: 11 / 12 voxels over 2 m");
            Assert.IsEmpty(fails, string.Join("\n", fails));
        }

        // ------------------------------------------------------------------
        // The independent checker
        // ------------------------------------------------------------------

        private static int RangeMax(byte k) => k == Site.TOWN ? Sites.TOWN_RANGE : k == Site.FRONTIER ? Sites.FRONTIER_RANGE : k == Site.VILLAGE ? Sites.VILLAGE_RANGE : Sites.OUTPOST_RANGE;
        private static int RadiusOf(byte k) => k == Site.TOWN ? Sites.TOWN_R : k == Site.FRONTIER ? Sites.FRONTIER_R : k == Site.VILLAGE ? Sites.VILLAGE_R : Sites.OUTPOST_R;

        /// Every rule, from the input arrays only. `starts` (map runs): both
        /// start rules' columns (PlayStart.Find), read again here; no disc cell's
        /// nearest point may lie within START_REACH of one.
        private static List<string> CheckDiscs(SiteInput inp, Site[] sites, int2[] starts)
        {
            var fails = new List<string>();
            int n = inp.n, cv = inp.cellVoxels;
            var townAreas = new HashSet<int>();
            for (int a = 0; a < sites.Length; a++)
            {
                var s = sites[a]; string who = Describe(s);
                if (s.id != Sites.FIRST_ID + a) fails.Add($"{who}: id {s.id}, want {Sites.FIRST_ID + a}");
                if (s.radius != RadiusOf(s.kind)) fails.Add($"{who}: radius {s.radius}");
                if ((s.centre.x - cv / 2) % cv != 0 || (s.centre.y - cv / 2) % cv != 0) fails.Add($"{who}: centre not a cell centre");
                int ccx = s.centre.x / cv, ccz = s.centre.y / cv, ci = ccz * n + ccx;
                var c0 = inp.rec[ci];
                int lo = int.MaxValue, hi = int.MinValue, disc = 0, scr = 0;
                int reach = (s.radius + math.max(Sites.SEA_GAP, Sites.FEATURE_GAP)) / cv + 2;
                double gapIn = (s.radius + Sites.SEA_GAP) * 0.92;   // 5/7 chamfer overestimates by <= 8 %
                for (int dz = -reach; dz <= reach; dz++)
                for (int dx = -reach; dx <= reach; dx++)
                {
                    int x = ccx + dx, z = ccz + dz;
                    double e = math.sqrt((double)(dx * dx + dz * dz)) * cv;
                    bool inDisc = e <= s.radius;
                    if (x < 0 || z < 0 || x >= n || z >= n) { if (inDisc) fails.Add($"{who}: disc off the map"); continue; }
                    int i = z * n + x; var c = inp.rec[i];
                    if (e < gapIn && (c.IsSea || inp.h[i] <= 0)) { fails.Add($"{who}: sea cell {e:F0} vox from the centre"); goto next; }
                    if (e < (s.radius + Sites.FEATURE_GAP) * 0.92 && c.IsFeature) { fails.Add($"{who}: feature cell {e:F0} vox from the centre"); goto next; }
                    if (!inDisc) continue;
                    disc++;
                    if (inp.waterClass[i] != Hydrology.DRY || c.freshWater < 1) { fails.Add($"{who}: wet / near-water cell in the disc"); goto next; }
                    if (inp.blocked[i]) { fails.Add($"{who}: blocked cell in the disc"); goto next; }
                    if (starts != null)
                        foreach (var p in starts)
                        {
                            long qx = math.clamp(p.x, x * cv, x * cv + cv - 1) - p.x, qz = math.clamp(p.y, z * cv, z * cv + cv - 1) - p.y;
                            if (qx * qx + qz * qz <= (long)Sites.START_REACH * Sites.START_REACH) { fails.Add($"{who}: disc cell within {Sites.START_REACH} vox of a start column"); goto next; }
                        }
                    int band = c.MoveBand;
                    if (band == 1) scr++;
                    if (band >= 2 || (band == 1 && s.kind != Site.FRONTIER)) { fails.Add($"{who}: band {band} cell in the disc"); goto next; }
                    lo = math.min(lo, inp.h[i]); hi = math.max(hi, inp.h[i]);
                }
                if (hi - lo > RangeMax(s.kind) || hi - lo != s.rangeVox) fails.Add($"{who}: range {hi - lo} (recorded {s.rangeVox}, max {RangeMax(s.kind)})");
                if (scr * 4 > disc) fails.Add($"{who}: {scr} of {disc} cells Scramble");
                if (disc != s.discCells) fails.Add($"{who}: disc {disc} cells, recorded {s.discCells}");
                if (inp.ground != null && (s.groundRangeVox < 0 || s.groundRangeVox > RangeMax(s.kind) + Sites.GROUND_SLACK)) fails.Add($"{who}: ground range {s.groundRangeVox}");
                // the step rule, re-derived: tan^2 30 = 1/3 over GROUND_STEP
                if (inp.ground != null && (s.groundStepVox < 0 || 3.0 * s.groundStepVox * s.groundStepVox > (double)Sites.GROUND_STEP * Sites.GROUND_STEP))
                    fails.Add($"{who}: ground step {s.groundStepVox} vox over {Sites.GROUND_STEP} (steeper than 30 deg)");
                if (s.padLevelY < inp.seaLevelY + lo || s.padLevelY > inp.seaLevelY + hi) fails.Add($"{who}: pad {s.padLevelY} outside the disc's heights");
                // the kind's own rule
                if (s.kind == Site.TOWN)
                {
                    if (!c0.IsEasy || s.easyArea < 0 || Array.IndexOf(inp.easyCells[s.easyArea], ci) < 0) fails.Add($"{who}: town centre not on its easy area");
                    if (!townAreas.Add(s.easyArea)) fails.Add($"{who}: second town on area {s.easyArea}");
                }
                else if (s.kind == Site.FRONTIER)
                {
                    if (!Sites.IsHard(in c0) || c0.IsEasy) fails.Add($"{who}: frontier centre not on hard ground");
                }
                else
                {
                    int maxW = s.kind == Site.VILLAGE ? Sites.VILLAGE_WATER : Sites.OUTPOST_WATER;
                    if (c0.freshWater > maxW) fails.Add($"{who}: water {c0.freshWater} steps > {maxW}");
                    if (s.kind == Site.VILLAGE && c0.Danger > Sites.VILLAGE_DANGER) fails.Add($"{who}: village danger {c0.Danger}");
                    if (s.kind == Site.OUTPOST && c0.freshWater <= Sites.VILLAGE_WATER && c0.Danger <= Sites.VILLAGE_DANGER) fails.Add($"{who}: outpost that a village could be");
                    if (NearestRouteCellVox(inp, ccx, ccz) > Sites.ROUTE_FAR * 1.08) fails.Add($"{who}: no route corridor within {Sites.ROUTE_FAR} vox");
                }
                // spacing
                for (int b = 0; b < a; b++)
                {
                    var o = sites[b];
                    double d = math.sqrt((double)(s.centre.x - o.centre.x) * (s.centre.x - o.centre.x) + (double)(s.centre.y - o.centre.y) * (s.centre.y - o.centre.y));
                    int sep = s.kind == Site.VILLAGE ? Sites.VILLAGE_SEP : s.kind == Site.OUTPOST ? Sites.OUTPOST_SEP : 0;
                    if (d < math.max(sep, s.radius + o.radius + Sites.SITE_GAP)) fails.Add($"{who}: {d:F0} vox from {Describe(o)}");
                }
                // the route join
                if (inp.routeLines != null && inp.routeLines.Length > 0)
                {
                    if (s.route < 0) fails.Add($"{who}: no route join");
                    else
                    {
                        double best = double.MaxValue;
                        foreach (var line in inp.routeLines) foreach (var p in line)
                            best = math.min(best, math.sqrt(math.square((double)math.round(p.x) - s.centre.x) + math.square((double)math.round(p.y) - s.centre.y)));
                        if (math.abs(best - s.joinDistance) > 1) fails.Add($"{who}: spur {s.joinDistance}, nearest centre-line point {best:F0}");
                    }
                }
                next:;
            }
            return fails;
        }

        private static double NearestRouteCellVox(SiteInput inp, int cx, int cz)
        {
            int n = inp.n, cv = inp.cellVoxels, r = Sites.ROUTE_FAR * 11 / 10 / cv + 1;
            double best = double.MaxValue;
            for (int dz = -r; dz <= r; dz++)
            for (int dx = -r; dx <= r; dx++)
            {
                int x = cx + dx, z = cz + dz;
                if (x < 0 || z < 0 || x >= n || z >= n || !inp.rec[z * n + x].IsRoute) continue;
                best = math.min(best, math.sqrt((double)(dx * dx + dz * dz)) * cv);
            }
            return best;
        }

        private static string Describe(in Site s) =>
            $"#{s.id} {Site.KindName(s.kind)} ({s.centre.x}, {s.centre.y})";

        // ------------------------------------------------------------------
        // Four seeds: every rule, the counts table, the map sources, poses
        // ------------------------------------------------------------------

        [Test]
        [Category("Slow")]   // four shared v4 maps
        public void FourSeeds_EveryRule_CountsTable()
        {
            Directory.CreateDirectory(LogDir);
            var ci = CultureInfo.InvariantCulture;
            var table = new StringBuilder("Task 187 site planner (Sites.cs), map v4, 4 seeds. Water = the centre's record fresh-water distance; " +
                "map range = disc max - min of the 8 m map heights; ground range / max slope = ShapeField.Sample columns on a 1 m grid over the disc " +
                "(the detail noise the 8 m map does not see; the stored flatten rule would remove it).\n\n");
            var fails = new List<string>();
            var poses = new List<string>();
            foreach (uint seed in Seeds)
            {
                var spec = TerrainSpec.SizeClass2(seed, 4);
                var map = SharedMaps.Get(spec);
                ulong recHash = map.Record.Hash();
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var inp = Sites.FromMap(map);
                var sites = Sites.Plan(inp, out var st);
                long ms = sw.ElapsedMilliseconds;
                if (Sites.Hash(Sites.Plan(Sites.FromMap(map), out _)) != Sites.Hash(sites)) fails.Add($"seed {seed}: two plans differ");
                if (map.Record.Hash() != recHash) fails.Add($"seed {seed}: planning wrote the record");
                if (map.Record.SiteList.Length != 0) fails.Add($"seed {seed}: the shared record holds sites");
                var starts = new List<int2>();
                for (int mild = 0; mild < 2; mild++) if (PlayStart.Find(map, out var pick, mild == 1)) starts.Add(pick.column);
                foreach (var f in CheckDiscs(inp, sites, starts.ToArray())) fails.Add($"seed {seed}: {f}");
                int K(byte k) => sites.Count(s => s.kind == k);
                if (K(Site.TOWN) < 1) fails.Add($"seed {seed}: no town");
                if (K(Site.FRONTIER) < 1) fails.Add($"seed {seed}: no frontier town ({st.Line()})");
                if (K(Site.VILLAGE) + K(Site.OUTPOST) < 1) fails.Add($"seed {seed}: no village or outpost");

                table.AppendLine($"seed {seed}: plan {ms} ms; {st.Line()}");
                table.AppendLine($"  {"site",-22} {"biome x landform",-22} {"water m",7} {"danger",6} {"map rng m",9} {"gnd rng m",9} {"max slope",9} {"pad y",6} {"cut/fill m",10} {"route",5} {"spur m",6}");
                var o = Offsets.For(in spec); var view = map.View;
                var col = new List<string>();
                foreach (var s in sites)
                {
                    GroundStats(in spec, in view, in o, s, out int gLo, out int gHi, out float slope);
                    string pair = GenBiomeName(s.biome) + " x " + Regions.Name(s.archetype);
                    table.AppendLine(string.Format(ci, "  {0,-22} {1,-22} {2,7} {3,6} {4,9:F1} {5,9:F1} {6,8:F0}d {7,6} {8,10} {9,5} {10,6:F0}",
                        Describe(s), pair, s.freshWaterM, s.danger, s.rangeVox * 0.1f, (gHi - gLo) * 0.1f, slope, s.padLevelY,
                        $"{s.cutMax * 0.1f:F1}/{s.fillMax * 0.1f:F1}", s.route, s.joinDistance * 0.1f));
                    col.Add(string.Format(ci, "{0} {1} {2} {3} {4}", Site.KindName(s.kind), s.id, s.centre.x, s.centre.y, s.radius));
                }
                table.AppendLine($"  counts: town {K(Site.TOWN)}, frontier {K(Site.FRONTIER)}, village {K(Site.VILLAGE)}, outpost {K(Site.OUTPOST)}; " +
                                 $"water m mean {(sites.Length > 0 ? sites.Average(s => s.freshWaterM) : 0):F0}; pairs {sites.Select(s => s.biome * 16 + s.archetype).Distinct().Count()}");
                table.AppendLine();
                File.WriteAllLines(Path.Combine(LogDir, $"sites_{seed}.txt"), col);
                WriteMap(Path.Combine(LogDir, $"map_{seed}.png"), map, inp, sites);
                if (seed == 42u)
                {
                    var town = sites.FirstOrDefault(s => s.kind == Site.TOWN);
                    var vil = sites.FirstOrDefault(s => s.kind == Site.VILLAGE);
                    if (town.radius > 0) poses.Add(Pose("site_town", in spec, in view, in o, town));
                    if (vil.radius > 0) poses.Add(Pose("site_village", in spec, in view, in o, vil));
                    var fr = sites.FirstOrDefault(s => s.kind == Site.FRONTIER);
                    if (fr.radius > 0) poses.Add(Pose("site_frontier", in spec, in view, in o, fr));
                }
            }
            if (fails.Count > 0) table.AppendLine("FAILURES:\n" + string.Join("\n", fails));
            File.WriteAllText(Path.Combine(LogDir, "table.txt"), table.ToString());
            File.WriteAllText(Path.Combine(LogDir, "poses.txt"), string.Join(";", poses));
            Debug.Log(table.ToString());
            Assert.IsEmpty(fails, string.Join("\n", fails.Take(40)));
        }

        private static string GenBiomeName(byte b) =>
            b == GenBiome.Forest ? "forest" : b == GenBiome.Desert ? "desert" : b == GenBiome.Snow ? "snow" : b == GenBiome.Jungle ? "jungle"
          : b == GenBiome.Savanna ? "savanna" : b == GenBiome.Badlands ? "badlands" : "b" + b;

        /// The real ground over the disc, 1 m grid: height range and the
        /// steepest 1 m step (degrees).
        private static void GroundStats(in TerrainSpec spec, in MapView view, in Offsets o, in Site s, out int lo, out int hi, out float slopeDeg)
        {
            int r = s.radius, step = 10, m = 2 * r / step + 1;
            var h = new int[m * m]; var ok = new bool[m * m];
            lo = int.MaxValue; hi = int.MinValue;
            for (int b = 0; b < m; b++)
            for (int a = 0; a < m; a++)
            {
                int dx = a * step - r, dz = b * step - r;
                if (dx * dx + dz * dz > r * r) continue;
                int g = ShapeField.Sample(in spec, in view, in o, s.centre.x + dx, s.centre.y + dz, 1, false).height;
                h[b * m + a] = g; ok[b * m + a] = true;
                lo = math.min(lo, g); hi = math.max(hi, g);
            }
            int worst = 0;
            for (int b = 0; b < m; b++)
            for (int a = 0; a < m; a++)
            {
                if (!ok[b * m + a]) continue;
                if (a + 1 < m && ok[b * m + a + 1]) worst = math.max(worst, math.abs(h[b * m + a + 1] - h[b * m + a]));
                if (b + 1 < m && ok[(b + 1) * m + a]) worst = math.max(worst, math.abs(h[(b + 1) * m + a] - h[b * m + a]));
            }
            slopeDeg = math.degrees(math.atan(worst / (float)step));
        }

        /// Eye-level pose (metres, -wsfixed): 1.7 m over the ground just inside
        /// the disc's west edge, looking east across the centre.
        private static string Pose(string name, in TerrainSpec spec, in MapView view, in Offsets o, in Site s)
        {
            float ex = s.centre.x - s.radius * 0.85f, ez = s.centre.y;
            float ax = s.centre.x + s.radius, az = s.centre.y;
            int ge = ShapeField.Sample(in spec, in view, in o, ex, ez, 1).height;
            int ga = ShapeField.Sample(in spec, in view, in o, ax, az, 1).height;
            string M(float v) => (v * 0.1f).ToString("F1", CultureInfo.InvariantCulture);
            return $"{name}|{M(ex)},{M(ge + 17)},{M(ez)}|{M(ax)},{M(ga + 10)},{M(az)}";
        }

        /// 1024 px, 8 m per pixel, north (+z) up. Sea dark blue, lake / river
        /// light blue; land grey by band (Walk light .. Sheer dark), hard ground
        /// (volcanic / snow) tinted red, easy areas green, features magenta,
        /// start zone yellow, route corridors cyan; site discs filled by kind
        /// (town orange, frontier red, village yellow-green, outpost white),
        /// outlined black. Labels: tools/sites187.py.
        private static void WriteMap(string path, WorldMap map, SiteInput inp, Site[] sites)
        {
            int n = inp.n, cv = inp.cellVoxels;
            var rec = inp.rec;
            var zone = Sites.StartDiscs(map);
            var px = new Color32[n * n];
            Color32[] band = { new Color32(214, 208, 190, 255), new Color32(178, 168, 148, 255), new Color32(130, 120, 105, 255), new Color32(90, 80, 72, 255) };
            for (int i = 0; i < n * n; i++)
            {
                var c = rec[i];
                Color32 col = c.IsSea || inp.h[i] <= 0 ? new Color32(26, 46, 89, 255)
                            : inp.waterClass[i] != Hydrology.DRY ? new Color32(89, 153, 230, 255)
                            : band[c.MoveBand];
                if (!c.IsSea && inp.h[i] > 0 && inp.waterClass[i] == Hydrology.DRY)
                {
                    if (Sites.IsHard(in c)) col = Color32.Lerp(col, new Color32(200, 60, 40, 255), 0.35f);
                    if (c.IsEasy) col = Color32.Lerp(col, new Color32(70, 170, 80, 255), 0.55f);
                    if (c.IsFeature) col = Color32.Lerp(col, new Color32(200, 90, 200, 255), 0.6f);
                    if (zone[i]) col = Color32.Lerp(col, new Color32(240, 220, 40, 255), 0.5f);
                    if (c.IsRoute) col = Color32.Lerp(col, new Color32(0, 200, 230, 255), 0.6f);
                }
                px[i] = col;   // Texture2D row 0 is the BOTTOM: z = 0 at the bottom, north (+z) up
            }
            foreach (var s in sites)
            {
                Color32 fill = s.kind == Site.TOWN ? new Color32(255, 140, 0, 255) : s.kind == Site.FRONTIER ? new Color32(230, 20, 20, 255)
                             : s.kind == Site.VILLAGE ? new Color32(170, 230, 40, 255) : new Color32(255, 255, 255, 255);
                int ccx = s.centre.x / cv, ccz = s.centre.y / cv;
                int rr = math.max(3, s.radius / cv) + 2;   // drawn at least 3 px so an outpost shows
                for (int dz = -rr; dz <= rr; dz++)
                for (int dx = -rr; dx <= rr; dx++)
                {
                    int x = ccx + dx, z = ccz + dz, d2 = dx * dx + dz * dz;
                    if (x < 0 || z < 0 || x >= n || z >= n || d2 > rr * rr) continue;
                    px[z * n + x] = d2 > (rr - 1) * (rr - 1) ? new Color32(0, 0, 0, 255) : fill;
                }
            }
            var tex = new Texture2D(n, n, TextureFormat.RGB24, false);
            tex.SetPixels32(px); tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
        }
    }
}
