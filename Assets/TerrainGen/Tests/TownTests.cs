// Assets/TerrainGen/Tests/TownTests.cs
//
// Task 190 (`-towns 1`): towns on the 187 sites (Towns.cs) and the town spacing
// (Sites.TownSep2 + tier 2). The engine side (chunk / shell / point agreement,
// pools) is CoreEngine/Tests/TownChunkTests.cs.
//   Spacing_Adversarial_OneCorner   a 3.7 km island whose only Walk ground is one
//       corner holding 3 easy areas: ONE town (target 2), on Walk ground; the
//       negative control (spread off) puts two there; with a far Walk patch (not
//       easy) the second town goes there (tier 2), > the spacing away.
//   Layout_Adversarial              a flat town disc crossed by a route corridor,
//       a blocked strip and a 3 m wall the map cannot see: no building on any of
//       them, none overlapping, every box inside the disc, doors to the centre.
//   Flatten_OffPins_OutsideIdentical  seeds 42 / 7 with the plane in the view:
//       OFF == task 188a's column pins; ON: every sampled column outside every
//       disc + ring byte-identical (FNV), inside a disc at the pad, in a ring
//       between the pad and the natural ground; ON with no plane == OFF.
//   FourSeeds_TownsTable (Slow)     the counts table (towns / villages / outposts /
//       buildings, min town distance before -> after), an independent checker of
//       every building rule, the maps (sites + buildings), seed 7's before map,
//       the site plan sheets and the eye-level / 150 m poses (-wsfixed).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace TerrainGen.Tests
{
    public unsafe class TownTests
    {
        private static readonly uint[] Seeds = { 42u, 7u, 2026u, 31337u };
        private static string LogDir => Path.Combine(Directory.GetCurrentDirectory(), "Logs", "TerrainGen", "towns190");
        private static readonly CultureInfo CI = CultureInfo.InvariantCulture;

        // ------------------------------------------------------------------
        // The adversarial island: n 512 x 80 vox (4 km), radius 230 cells, all
        // Climb but a Walk corner (cells 110..190 both ways) with three easy
        // areas; optionally a far Walk patch (330..400) that is not easy.
        // ------------------------------------------------------------------
        private const int AN = 512, ACV = 80;

        private static SiteInput Corner(bool farWalk)
        {
            int n = AN, N = n * n;
            var inp = new SiteInput { n = n, cellVoxels = ACV, seaLevelY = 5120, h = new int[N], waterClass = new byte[N], rec = new RecordCell[N], blocked = new bool[N] };
            var easy = new List<int>[3]; for (int a = 0; a < 3; a++) easy[a] = new List<int>();
            for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                int i = z * n + x, dx = x - 256, dz = z - 256;
                if (dx * dx + dz * dz > 230 * 230) { inp.h[i] = -100; inp.rec[i] = new RecordCell { landform = WorldRecord.SEA }; continue; }
                bool walk = (x >= 110 && x <= 190 && z >= 110 && z <= 190) || (farWalk && x >= 330 && x <= 400 && z >= 330 && z <= 400);
                inp.h[i] = walk ? 100 : 300 + (x * 37 + z * 91) % 200;
                byte flags = 0;
                int area = x >= 115 && x <= 135 && z >= 115 && z <= 135 ? 0 : x >= 160 && x <= 180 && z >= 115 && z <= 135 ? 1 : x >= 115 && x <= 135 && z >= 160 && z <= 180 ? 2 : -1;
                if (area >= 0) { flags |= WorldRecord.FLAG_EASY; easy[area].Add(i); }
                if ((z == 150 || z == 151) && x >= 100 && x <= 400) flags |= WorldRecord.FLAG_ROUTE;
                inp.rec[i] = new RecordCell { landform = walk ? Regions.Plains : Regions.Highlands, biome = GenBiome.Forest, hazard = (byte)((walk ? 0 : 2) << 3), flags = flags, freshWater = 3 };
            }
            inp.easyCells = easy.Select(l => l.ToArray()).ToArray();
            var line = new List<float2>();
            for (int x = 100; x <= 400; x++) line.Add(new float2(x * ACV + 40, 150 * ACV + 40));
            inp.routeLines = new[] { line.ToArray() };
            return inp;
        }

        private static double Dist(int2 a, int2 b) => math.sqrt((double)(a.x - b.x) * (a.x - b.x) + (double)(a.y - b.y) * (a.y - b.y));

        private static double MinTownDistance(Site[] sites)
        {
            var t = sites.Where(s => s.kind == Site.TOWN).ToArray();
            double best = double.PositiveInfinity;
            for (int a = 0; a < t.Length; a++) for (int b = a + 1; b < t.Length; b++) best = math.min(best, Dist(t[a].centre, t[b].centre));
            return best;
        }

        [Test]
        public void Spacing_Adversarial_OneCorner_FewerTowns()
        {
            var inp = Corner(false);
            var sites = Sites.Plan(inp, out var st);
            var towns = sites.Where(s => s.kind == Site.TOWN).ToArray();
            Debug.Log($"[190] corner island: {st.Line()}");
            Assert.AreEqual(2, st.townTarget, "the island's town target");
            Assert.AreEqual(1, towns.Length, "one town: the other easy areas lie inside its fair share");
            Assert.Greater(st.sepRejects, 0, "the spacing rejected the corner's other areas");
            Assert.IsEmpty(SiteTests.CheckDiscs(inp, sites, null), "every disc rule (independent checker)");
            foreach (var t in towns)
            {
                var cells = new List<int>();
                Sites.DiscCells(inp.n, inp.cellVoxels, (t.centre.y / ACV) * AN + t.centre.x / ACV, t.radius, cells);
                foreach (int i in cells) Assert.AreEqual(0, inp.rec[i].MoveBand, "a town disc cell off Walk ground");
            }
            // negative control: no spacing = two towns in the same corner
            var before = Sites.Plan(Corner(false), out _, false).Where(s => s.kind == Site.TOWN).ToArray();
            Assert.AreEqual(2, before.Length, "without the spacing both towns sit in the corner");
            Assert.Less(MinTownDistance(before), math.sqrt((double)Sites.TownSep2(st.landCells, st.townTarget, ACV)), "and closer than the spacing");
            // a far Walk patch (not easy): the second town goes there (tier 2)
            var inp2 = Corner(true);
            var s2 = Sites.Plan(inp2, out var st2);
            var t2 = s2.Where(s => s.kind == Site.TOWN).ToArray();
            Debug.Log($"[190] corner + far patch: {st2.Line()}");
            Assert.AreEqual(2, t2.Length, "two towns with the far patch");
            Assert.AreEqual(1, st2.towns2, "one of them tier 2");
            var far = t2.First(t => t.easyArea < 0);
            Assert.IsTrue(far.centre.x >= 330 * ACV && far.centre.y >= 330 * ACV, "the tier-2 town is on the far patch");
            Assert.GreaterOrEqual(MinTownDistance(t2), math.sqrt((double)Sites.TownSep2(st2.landCells, st2.townTarget, ACV)) - 1);
            Assert.IsEmpty(SiteTests.CheckDiscs(inp2, s2, null), "every disc rule (independent checker)");
        }

        // ------------------------------------------------------------------
        // Layout on a hand-made disc
        // ------------------------------------------------------------------
        [Test]
        public void Layout_Adversarial_RouteWallBlocked()
        {
            int n = 64, cv = ACV, N = n * n, sea = 5120;
            var inp = new SiteInput { n = n, cellVoxels = cv, seaLevelY = sea, h = new int[N], waterClass = new byte[N], rec = new RecordCell[N], blocked = new bool[N] };
            for (int i = 0; i < N; i++) { inp.h[i] = 100; inp.rec[i] = new RecordCell { landform = Regions.Plains, biome = GenBiome.Desert, freshWater = 3 }; }
            for (int x = 0; x < n; x++) inp.rec[30 * n + x].flags |= WorldRecord.FLAG_ROUTE;   // a corridor along z cell 30 (through the centre's row-1)
            for (int z = 26; z <= 40; z++) inp.blocked[z * n + 40] = true;                    // a blocked strip at x cell 40
            int wallX = 27 * cv;                                                              // a 3 m wall the map cannot see at x = 27 cells
            inp.ground = (x, z) => sea + 100 + (x >= wallX && x < wallX + 20 && z > 34 * cv ? 30 : 0);
            var s = new Site { id = 16, kind = Site.TOWN, centre = new int2(32 * cv + cv / 2, 32 * cv + cv / 2), radius = Sites.TOWN_R, padLevelY = sea + 100, biome = GenBiome.Desert, route = 0, joinPoint = new int2(32 * cv, 30 * cv + 40) };
            var st = default(Towns.Stats);
            var bs = Towns.Layout(inp, s, ref st);
            Debug.Log($"[190] layout adversarial: {bs.Count} buildings, {st.slotGround} slots refused by the ground");
            Assert.Greater(bs.Count, 8, "a flat town disc takes most of its program");
            Assert.AreEqual(1, bs.Count(b => b.shape == BuildingKit.HALL), "one hall");
            Assert.Greater(st.slotGround, 0, "the route / strip / wall refused slots");
            var boxes = bs.Select(b => BuildingKit.Footprint(in b)).ToArray();
            for (int a = 0; a < bs.Count; a++)
            {
                var f = boxes[a];
                for (int z = f.min.z / cv; z <= (f.max.z - 1) / cv; z++)
                for (int x = f.min.x / cv; x <= (f.max.x - 1) / cv; x++)
                {
                    Assert.IsFalse(inp.rec[z * n + x].IsRoute, $"building {a} on the route corridor");
                    Assert.IsFalse(inp.blocked[z * n + x], $"building {a} on the blocked strip");
                }
                bool crossesWall = f.min.x < wallX && f.max.x > wallX + 20 && f.max.z > 34 * cv + 21;   // spans it: a 2 m grid sample lands on it
                Assert.IsFalse(crossesWall, $"building {a} over the wall");
                CheckBox(s, bs[a], f, boxes, a, out string why);
                Assert.IsNull(why, why);
            }
        }

        /// Independent box rules: inside R - EDGE, GAP clear of the others, the
        /// door to the centre (or a centre hall's door toward the route join),
        /// floor = pad + 1. `why` null = ok.
        private static void CheckBox(in Site s, in Building b, in BuildingFootprint f, BuildingFootprint[] all, int self, out string why)
        {
            why = null;
            double lim = s.radius - Towns.EDGE;
            foreach (var c in new[] { new int2(f.min.x, f.min.z), new int2(f.max.x - 1, f.min.z), new int2(f.min.x, f.max.z - 1), new int2(f.max.x - 1, f.max.z - 1) })
                if (Dist(c, s.centre) > lim + 1e-9) { why = $"box corner {c} {Dist(c, s.centre):F0} vox from the centre (limit {lim})"; return; }
            for (int o = 0; o < all.Length; o++)
            {
                if (o == self) continue;
                var g = all[o];
                bool apart = f.max.x + Towns.GAP <= g.min.x || g.max.x + Towns.GAP <= f.min.x || f.max.z + Towns.GAP <= g.min.z || g.max.z + Towns.GAP <= f.min.z;
                if (!apart) { why = $"box {f.min}-{f.max} within {Towns.GAP} vox of box {o} {g.min}-{g.max}"; return; }
            }
            int2 fd = BuildingKit.FrontDir(b.rot);
            double2 mid = new double2((f.min.x + f.max.x) * 0.5, (f.min.z + f.max.z) * 0.5);
            double2 toC = new double2(s.centre.x, s.centre.y) - mid;
            bool atCentre = math.lengthsq(toC) < 25.0;
            if (!atCentre && fd.x * toC.x + fd.y * toC.y <= 0) { why = $"door {fd} faces away from the centre ({toC.x:F0}, {toC.y:F0})"; return; }
            if (atCentre && s.route >= 0)
            {
                double2 tj = new double2(s.joinPoint.x - s.centre.x, s.joinPoint.y - s.centre.y);
                if (fd.x * tj.x + fd.y * tj.y < 0) { why = $"centre hall door {fd} away from the route join"; return; }
            }
            if (b.origin.y != s.padLevelY + 1) why = $"floor {b.origin.y} != pad {s.padLevelY} + 1";
        }

        // ------------------------------------------------------------------
        // The flatten
        // ------------------------------------------------------------------
        private const uint PIN_42 = 0x6f6c8910u, PIN_7 = 0xeaaad676u;   // task 188a's (MatVariantTests), -matvariant 1 arm

        private static uint PinHash(in TerrainSpec spec, WorldMap map, in MapView view)
        {
            var o = Offsets.For(in spec);
            var rng = new Unity.Mathematics.Random(0x188a);
            int n = spec.mapCells, cv = spec.mapCellVoxels, cols = 0, guard = 0;
            uint h = 2166136261u;
            void Mix(int v) { h = (h ^ (uint)v) * 16777619u; }
            while (cols < 600 && guard++ < 60000)
            {
                int cx = rng.NextInt(2, n - 2), cz = rng.NextInt(2, n - 2);
                int x = cx * cv + rng.NextInt(cv), z = cz * cv + rng.NextInt(cv);
                if (map.Height[cz * n + cx] <= 0f) continue;
                var c = ShapeField.Sample(in spec, in view, in o, x + 0.5f, z + 0.5f, 1);
                float st = ShapeField.MaxStep(in spec, in view, in o, x + 0.5f, z + 0.5f, 1, c.height);
                var cd = DetailField.PrepareColumn(in spec, in o, x, z);
                Mix(c.height); Mix(c.mrSlot); Mix(c.cover); Mix(c.mrSoil); Mix(c.mrSub); Mix(c.surfBiome);
                Mix(DetailField.TopMaterial(in spec, in c, st, in o, x, z));
                for (int d = 0; d < 48; d++) Mix(DetailField.Material(in spec, in o, in c, in cd, st, x, c.height - d, z));
                cols++;
            }
            return h;
        }

        private sealed class Plan : IDisposable
        {
            public TerrainSpec off, on; public Offsets o; public WorldMap map; public SiteInput inp; public Site[] sites;
            public int[] plane; public Building[] all; public BuildingFootprint[] prints; public Towns.Stats st; public Sites.Stats sst;
            public long ms; private GCHandle _h;
            public MapView view, bare;   // with / without the plane
            public Plan(uint seed, bool spread = true)
            {
                off = TerrainSpec.SizeClass2(seed, 4);
                on = off; on.sitesOn = 1; on.townsOn = 1;
                o = Offsets.For(in off);
                map = SharedMaps.Get(off);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                inp = Sites.FromMap(map);
                sites = Sites.Plan(inp, out sst, spread);
                plane = Towns.Build(inp, sites, out st, out all, out prints);
                ms = sw.ElapsedMilliseconds;
                _h = GCHandle.Alloc(plane, GCHandleType.Pinned);
                bare = map.View; bare.town = null;
                view = bare; view.town = (int*)_h.AddrOfPinnedObject();
            }
            public void Dispose() { if (_h.IsAllocated) _h.Free(); }
        }

        /// 0 outside every disc + ring, 1 in a ring, 2 in a disc; k = the site.
        private static int Zone(int[] plane, int x, int z, out int k)
        {
            for (k = 0; k < plane[0]; k++)
            {
                int e = plane[2] + k * Towns.SITE_INTS;
                long dx = x - plane[e], dz = z - plane[e + 1], d2 = dx * dx + dz * dz;
                long r = plane[e + 2], rb = r + plane[e + 3];
                if (d2 <= r * r) return 2;
                if (d2 <= rb * rb) return 1;
            }
            k = -1;
            return 0;
        }

        private static void ColHash(ref uint h, in ColumnSample c)
        {
            foreach (int v in new[] { c.height, c.water, c.cover, c.mrSlot, c.mrSoil, c.mrSub, c.surfBiome, c.riverFloor, c.open1Lo, c.open3Hi, c.stampMat })
                h = (h ^ (uint)v) * 16777619u;
        }

        [Test]
        public void Flatten_OffPins_OutsideIdentical()
        {
            var log = new StringBuilder("Task 190 flatten: OFF pins, ON outside identical, disc at the pad, ring between\n");
            foreach (uint seed in new[] { 42u, 7u })
            {
                using var p = new Plan(seed);
                Assert.Greater(p.sites.Length, 0);
                Assert.IsFalse(p.off.Towns, "the default stays OFF");
                var off1 = p.off; off1.matVariantOn = 1;   // the 188a pins carry the variant spray (task 190a)
                off1.landSigOff = 1;                       // ... and predate the landform signatures (task 190b)
                uint pin = PinHash(in off1, p.map, in p.view);
                Assert.AreEqual(seed == 42u ? PIN_42 : PIN_7, pin, $"seed {seed}: -towns 0 with the plane in the view == before");
                // the whole world on a 32 m grid + every site's neighbourhood on a 2 m grid
                int world = p.off.mapCells * p.off.mapCellVoxels;
                uint hOff = 2166136261u, hOn = 2166136261u;
                int outside = 0, inDisc = 0, inRing = 0, ringMoved = 0;
                var pts = new List<int2>();
                for (int z = 160; z < world; z += 320) for (int x = 160; x < world; x += 320) pts.Add(new int2(x, z));
                foreach (var s in p.sites)
                {
                    int reach = s.radius + s.radius / 2 + 160;
                    for (int dz = -reach; dz <= reach; dz += 20) for (int dx = -reach; dx <= reach; dx += 20) pts.Add(s.centre + new int2(dx, dz));
                }
                foreach (var q in pts)
                {
                    var a = ShapeField.Sample(in p.off, in p.view, in p.o, q.x + 0.5f, q.y + 0.5f, 1);
                    var b = ShapeField.Sample(in p.on, in p.view, in p.o, q.x + 0.5f, q.y + 0.5f, 1);
                    int zone = Zone(p.plane, q.x, q.y, out int k);
                    if (zone == 0) { ColHash(ref hOff, in a); ColHash(ref hOn, in b); outside++; continue; }
                    int pad = p.plane[p.plane[2] + k * Towns.SITE_INTS + 4];
                    if (zone == 2) { inDisc++; Assert.AreEqual(pad, b.height, $"seed {seed} ({q.x}, {q.y}): disc column not at the pad"); continue; }
                    inRing++;
                    if (b.height != a.height) ringMoved++;
                    Assert.IsTrue(b.height >= math.min(pad, a.height) && b.height <= math.max(pad, a.height), $"seed {seed} ({q.x}, {q.y}): ring {b.height} outside [pad {pad}, natural {a.height}]");
                }
                Assert.AreEqual(hOff, hOn, $"seed {seed}: a column outside every disc + ring changed");
                Assert.Greater(inDisc, 0); Assert.Greater(ringMoved, 0, "the ring blends");
                // ON with no plane in the view (an older view) == OFF
                foreach (var s in p.sites.Take(3))
                {
                    var a = ShapeField.Sample(in p.off, in p.bare, in p.o, s.centre.x + 0.5f, s.centre.y + 0.5f, 1);
                    var b = ShapeField.Sample(in p.on, in p.bare, in p.o, s.centre.x + 0.5f, s.centre.y + 0.5f, 1);
                    uint ha = 1, hb = 1; ColHash(ref ha, in a); ColHash(ref hb, in b);
                    Assert.AreEqual(ha, hb, "ON without a plane changes nothing");
                }
                log.AppendLine($"seed {seed}: pin 0x{pin:x8}; {outside} outside columns FNV 0x{hOn:x8} == OFF; {inDisc} disc columns at the pad; {inRing} ring columns ({ringMoved} moved), all between pad and natural");
            }
            Directory.CreateDirectory(LogDir);
            File.WriteAllText(Path.Combine(LogDir, "flatten.txt"), log.ToString());
            Debug.Log(log.ToString());
        }

        // ------------------------------------------------------------------
        // Four seeds: counts, every building rule, maps, sheets, poses
        // ------------------------------------------------------------------
        [Test]
        [Category("Slow")]   // four shared v4 maps, two plans each
        public void FourSeeds_TownsTable_Maps_Poses()
        {
            Directory.CreateDirectory(LogDir);
            var table = new StringBuilder("Task 190 towns on the sites (-towns 1), map v4, 4 seeds. Spacing = the fair-share radius sqrt(land / (pi towns)); " +
                "'before' = task 187's plan (no spacing). Buildings from Towns.Build (hall / house / hut).\n\n");
            table.AppendLine($"| seed | towns (tier 2) | frontier | villages | outposts | buildings (hall/house/hut) | min town dist before -> after m | spacing m | rings shortened | plan + layout ms |");
            table.AppendLine("|---|---|---|---|---|---|---|---|---|---|");
            var detail = new StringBuilder();
            var fails = new List<string>();
            foreach (uint seed in Seeds)
            {
                using var p = new Plan(seed);
                var before = Sites.Plan(p.inp, out var stB, false);
                int K(Site[] ss, byte k) => ss.Count(s => s.kind == k);
                var starts = new List<int2>();
                for (int mild = 0; mild < 2; mild++) if (PlayStart.Find(p.map, out var pick, mild == 1)) starts.Add(pick.column);
                foreach (var f in SiteTests.CheckDiscs(p.inp, p.sites, starts.ToArray())) fails.Add($"seed {seed}: {f}");
                if (K(p.sites, Site.TOWN) < 1) fails.Add($"seed {seed}: no town");
                // every building rule, independently
                var byId = p.sites.ToDictionary(s => s.id);
                int rules = 0;
                for (int b = 0; b < p.all.Length; b++)
                {
                    var bd = p.all[b]; var fp = p.prints[b];
                    if (!byId.TryGetValue(bd.id, out var s)) { fails.Add($"seed {seed} building {b}: no site {bd.id}"); continue; }
                    CheckBox(s, bd, fp, p.prints, b, out string why);
                    if (why != null) fails.Add($"seed {seed} building {b} ({Site.KindName(s.kind)} #{s.id}): {why}");
                    int n = p.inp.n, cv = p.inp.cellVoxels;
                    for (int z = fp.min.z / cv; z <= (fp.max.z - 1) / cv; z++)
                    for (int x = fp.min.x / cv; x <= (fp.max.x - 1) / cv; x++)
                    {
                        var c = p.inp.rec[z * n + x];
                        if (c.IsSea || p.inp.waterClass[z * n + x] != Hydrology.DRY || c.IsFeature || c.IsRoute || p.inp.blocked[z * n + x])
                        { fails.Add($"seed {seed} building {b}: on a sea / water / feature / route / blocked cell ({x}, {z})"); goto nextB; }
                    }
                    // the natural ground's steepest 2 m step under the box (degrees, double)
                    double worst = 0;
                    for (int z = fp.min.z; z + 20 < fp.max.z; z += 20)
                    for (int x = fp.min.x; x + 20 < fp.max.x; x += 20)
                    {
                        int g0 = ShapeField.Sample(in p.off, in p.bare, in p.o, x, z, 1, false).height;
                        int gx = ShapeField.Sample(in p.off, in p.bare, in p.o, x + 20, z, 1, false).height;
                        int gz = ShapeField.Sample(in p.off, in p.bare, in p.o, x, z + 20, 1, false).height;
                        worst = math.max(worst, math.max(math.abs(gx - g0), math.abs(gz - g0)));
                    }
                    if (math.degrees(math.atan(worst / 20.0)) > 30.0 + 1e-6) fails.Add($"seed {seed} building {b}: natural ground step {worst} vox / 2 m (> 30 deg)");
                    rules++;
                    nextB:;
                }
                // ON ground: flat under every building (the pad)
                foreach (var fp in p.prints.Take(40))
                {
                    int g = ShapeField.Sample(in p.on, in p.view, in p.o, (fp.min.x + fp.max.x) / 2 + 0.5f, (fp.min.z + fp.max.z) / 2 + 0.5f, 1).height;
                    if (g != fp.min.y + Towns.FOUND - 1) fails.Add($"seed {seed}: ground {g} under a building at floor {fp.min.y + Towns.FOUND}");
                }
                double dB = MinTownDistance(before), dA = MinTownDistance(p.sites);
                string D(double d) => double.IsInfinity(d) ? "-" : (d * 0.1).ToString("F0", CI);
                table.AppendLine($"| {seed} | {K(p.sites, Site.TOWN)} ({p.sst.towns2}) | {K(p.sites, Site.FRONTIER)} | {K(p.sites, Site.VILLAGE)} | {K(p.sites, Site.OUTPOST)} | " +
                                 $"{p.st.buildings} ({p.st.halls}/{p.st.houses}/{p.st.huts}) | {D(dB)} -> {D(dA)} | {p.sst.townSep / 10} | {p.st.blendShort} of {p.sites.Length} | {p.ms} |");
                detail.AppendLine($"seed {seed}: {p.sst.Line()}");
                detail.AppendLine($"  towns: {p.st.Line()}; plane {p.plane.Length * 4} B; buildings checked {rules}");
                detail.AppendLine($"  before (187): towns {K(before, Site.TOWN)} at {string.Join(" ", before.Where(s => s.kind == Site.TOWN).Select(s => $"({s.centre.x},{s.centre.y})"))}");
                foreach (var s in p.sites)
                {
                    int nb = p.all.Count(b => b.id == s.id);
                    int e = p.plane[2] + (s.id - Sites.FIRST_ID) * Towns.SITE_INTS;
                    Towns.BlendWidth(p.inp, p.sites, s.id - Sites.FIRST_ID, out int why);
                    detail.AppendLine(string.Format(CI, "  #{0} {1,-8} ({2}, {3}) r {4} m ring {5} m ({11}) pad {6} cut/fill {7:F1}/{8:F1} m: {9} buildings{10}",
                        s.id, Site.KindName(s.kind), s.centre.x, s.centre.y, s.radius / 10, p.plane[e + 3] / 10, s.padLevelY, s.cutMax * 0.1f, s.fillMax * 0.1f, nb,
                        s.kind == Site.TOWN && s.easyArea < 0 ? " (tier 2)" : "", Towns.WhyName[why]));
                }
                detail.AppendLine();
                SiteTests.WriteMap(Path.Combine(LogDir, $"map_{seed}.png"), p.map, p.inp, p.sites, p.prints);
                File.WriteAllLines(Path.Combine(LogDir, $"sites_{seed}.txt"), p.sites.Select(s => string.Format(CI, "{0} {1} {2} {3} {4}", Site.KindName(s.kind), s.id, s.centre.x, s.centre.y, s.radius)));
                if (seed == 7u)
                {
                    SiteTests.WriteMap(Path.Combine(LogDir, "map_7_before.png"), p.map, p.inp, before);
                    File.WriteAllLines(Path.Combine(LogDir, "sites_7_before.txt"), before.Select(s => string.Format(CI, "{0} {1} {2} {3} {4}", Site.KindName(s.kind), s.id, s.centre.x, s.centre.y, s.radius)));
                }
                WriteSheet(Path.Combine(LogDir, $"plans_{seed}.png"), p);
                File.WriteAllText(Path.Combine(LogDir, $"poses_{seed}.txt"), Poses(p));
            }
            string all = table + "\n" + detail + (fails.Count > 0 ? "FAILURES:\n" + string.Join("\n", fails) : "every rule holds\n");
            File.WriteAllText(Path.Combine(LogDir, "table.txt"), all);
            Debug.Log(all);
            Assert.IsEmpty(fails, string.Join("\n", fails.Take(40)));
        }

        /// The poses for the shots (-wsfixed, metres): the first town and the
        /// first village (else the frontier town) at eye level -- 1.7 m over the
        /// pad, north of the centre at ~0.8 R in a lane (no building box within
        /// 1.5 m), looking south (the noon sun is behind: it stands north) at the
        /// centre 3 m up -- and the town from 150 m north, 30 m over the ground there.
        private static string Poses(Plan p)
        {
            var list = new List<string>();
            string M(float v) => (v * 0.1f).ToString("F1", CI);
            void Eye(string name, Site s)
            {
                for (int tries = 0; tries < 40; tries++)
                {
                    int dx = (tries % 2 == 0 ? 1 : -1) * (tries / 2) * 20;
                    int2 e = s.centre + new int2(dx, s.radius * 8 / 10);
                    bool free = true;
                    foreach (var f in p.prints)
                        if (e.x > f.min.x - 15 && e.x < f.max.x + 15 && e.y > f.min.z - 15 && e.y < f.max.z + 15) { free = false; break; }
                    if (!free) continue;
                    list.Add($"{name}|{M(e.x)},{M(s.padLevelY + 17)},{M(e.y)}|{M(s.centre.x)},{M(s.padLevelY + 30)},{M(s.centre.y)}");
                    return;
                }
            }
            var town = p.sites.FirstOrDefault(s => s.kind == Site.TOWN);
            var vil = p.sites.FirstOrDefault(s => s.kind == Site.VILLAGE);
            if (vil.radius == 0) vil = p.sites.FirstOrDefault(s => s.kind == Site.FRONTIER);
            if (town.radius > 0) Eye("town_eye", town);
            if (vil.radius > 0) Eye(vil.kind == Site.VILLAGE ? "village_eye" : "frontier_eye", vil);
            if (town.radius > 0)
            {
                int ex = town.centre.x, ez = town.centre.y + 1500;
                int g = ShapeField.Sample(in p.on, in p.view, in p.o, ex + 0.5f, ez + 0.5f, 1).height;
                list.Add($"town_150m|{M(ex)},{M(math.max(g, town.padLevelY) + 300)},{M(ez)}|{M(town.centre.x)},{M(town.padLevelY + 30)},{M(town.centre.y)}");
            }
            return string.Join(";", list);
        }

        /// The site plans: every town / frontier / village of the seed (outposts
        /// too if room), 0.5 m per pixel, north up. Ground: the NATURAL height
        /// minus the pad (blue below, red above, +-4 m full), the disc outlined
        /// white, the ring grey; route corridor cells cyan, blocked cells yellow;
        /// buildings filled by shape (hall red, house orange, hut yellow), the door
        /// wall black.
        private static void WriteSheet(string path, Plan p)
        {
            var show = p.sites.OrderBy(s => s.kind == Site.TOWN ? 0 : s.kind == Site.FRONTIER ? 1 : s.kind == Site.VILLAGE ? 2 : 3).Take(8).ToArray();
            const int T = 400, COLS = 4;   // 400 px = 200 m tiles
            int rows = (show.Length + COLS - 1) / COLS;
            var px = new Color32[T * COLS * T * math.max(1, rows)];
            int W = T * COLS;
            for (int k = 0; k < show.Length; k++)
            {
                var s = show[k];
                int ox = (k % COLS) * T, oy = (k / COLS) * T;
                int e = p.plane[2] + (s.id - Sites.FIRST_ID) * Towns.SITE_INTS, B = p.plane[e + 3];
                for (int j = 0; j < T; j += 4)
                for (int i = 0; i < T; i += 4)
                {
                    int x = s.centre.x + (i - T / 2) * 5, z = s.centre.y + (j - T / 2) * 5;
                    int g = ShapeField.Sample(in p.off, in p.bare, in p.o, x + 0.5f, z + 0.5f, 1, false).height;
                    float t = math.clamp((g - s.padLevelY) / 40f, -1f, 1f);
                    var col = t >= 0 ? Color32.Lerp(new Color32(200, 200, 190, 255), new Color32(200, 60, 50, 255), t) : Color32.Lerp(new Color32(200, 200, 190, 255), new Color32(50, 90, 200, 255), -t);
                    int cx = x / p.inp.cellVoxels, cz = z / p.inp.cellVoxels;
                    if (cx >= 0 && cz >= 0 && cx < p.inp.n && cz < p.inp.n)
                    {
                        int ci = cz * p.inp.n + cx;
                        if (p.inp.rec[ci].IsRoute) col = Color32.Lerp(col, new Color32(0, 200, 230, 255), 0.6f);
                        if (p.inp.blocked[ci]) col = Color32.Lerp(col, new Color32(240, 220, 40, 255), 0.5f);
                        if (p.inp.waterClass[ci] != Hydrology.DRY || p.inp.rec[ci].IsSea) col = new Color32(60, 120, 220, 255);
                    }
                    double d = Dist(new int2(x, z), s.centre);
                    if (math.abs(d - s.radius) < 10) col = new Color32(255, 255, 255, 255);
                    else if (B > 0 && math.abs(d - s.radius - B) < 10) col = new Color32(120, 120, 120, 255);
                    for (int bj = 0; bj < 4; bj++) for (int bi = 0; bi < 4; bi++) px[(oy + j + bj) * W + ox + i + bi] = col;
                }
                foreach (var b in p.all.Where(b => b.id == s.id))
                {
                    var f = BuildingKit.Footprint(in b);
                    var fill = b.shape == BuildingKit.HALL ? new Color32(210, 40, 30, 255) : b.shape == BuildingKit.HOUSE ? new Color32(240, 140, 30, 255) : new Color32(240, 220, 60, 255);
                    int2 fd = BuildingKit.FrontDir(b.rot);
                    for (int z = f.min.z; z < f.max.z; z += 5)
                    for (int x = f.min.x; x < f.max.x; x += 5)
                    {
                        int i = (x - s.centre.x) / 5 + T / 2, j = (z - s.centre.y) / 5 + T / 2;
                        if (i < 0 || j < 0 || i >= T || j >= T) continue;
                        bool door = (fd.y < 0 && z < f.min.z + 10) || (fd.y > 0 && z >= f.max.z - 10) || (fd.x < 0 && x < f.min.x + 10) || (fd.x > 0 && x >= f.max.x - 10);
                        px[(oy + j) * W + ox + i] = door ? new Color32(0, 0, 0, 255) : fill;
                    }
                }
            }
            var tex = new Texture2D(W, T * math.max(1, rows), TextureFormat.RGB24, false);
            tex.SetPixels32(px); tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            File.WriteAllLines(path + ".txt", show.Select((s, k) => $"tile {k} (row {k / COLS}, col {k % COLS}; image row 0 is the BOTTOM): {Site.KindName(s.kind)} #{s.id} at ({s.centre.x}, {s.centre.y})"));
        }
    }
}
