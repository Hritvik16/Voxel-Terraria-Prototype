// Assets/TerrainGen/Tests/StampFitTests.cs
//
// Task 185 (WORLD_GEN_LOOK_PLAN C7, TerrainSpec.StampFit, the default since task 187a; `-stampfit 0` = off):
// one test per rule, each on the stamp's own footprint / column heights, on
// the four test seeds (42, 7, 2026, 31337; map v4) plus one adversarial case.
//   Cave     a kept cave's tube breaks the surface only within its mouth
//            (CAVE_MOUTH_MAX + r of the site); S and D agree on the fitted
//            axis. OFF arm = the negative control (trenches exist). Adversarial:
//            a cave forced on the steepest stamp-free site, pointing downhill.
//   Ledge    every shelf top is a rock-layer boundary, 35 voxels thick, and
//            neighbouring shelf columns differ by <= 1 voxel or a whole layer
//            (no contour rings). Adversarial: a dense window at the tallest cliff.
//   Geyser   terraces sit on the ground (-RISE <= top - map ground < STEP), the
//            pools are at rest (FluidAtRestTests' tile job). Adversarial: the
//            basin moved onto the steepest land site (offsets' peakDir).
//   Hoop     kept iff on a rise or in rugged rock. Adversarial: the flattest
//            plain on the map is rejected.
// The sea keep-out's test is FluidAtRestTests.Sea_AtRest_StampFit_Seed2026.
// Report: Logs/TerrainGen/stampfit185/*.txt (also the rough cost, ON vs OFF).
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Unity.Mathematics;

namespace TerrainGen.Tests
{
    public class StampFitTests
    {
        private static readonly uint[] Seeds = { 42u, 7u, 2026u, 31337u };

        private struct W
        {
            public TerrainSpec on, off; public Offsets o; public WorldMap map;
            public ColumnSample S(bool fit, int x, int z)
            {
                var v = map.View; var sp = fit ? on : off;
                return ShapeField.Sample(in sp, in v, in o, x + 0.5f, z + 0.5f, 1);
            }
            public byte D(bool fit, in ColumnSample c, int x, int y, int z)
            {
                var sp = fit ? on : off;
                var cd = DetailField.PrepareColumn(in sp, in o, x, z);
                return DetailField.Material(in sp, in o, in c, in cd, 0f, x, y, z);
            }
            public int Site(float2 p) { var v = map.View; Stamps.SampleSiteHeight(in on, in v, p, out int g); return g; }
        }

        private static W World(uint seed)
        {
            var on = TerrainSpec.SizeClass2(seed, 4);   // task 187a: the fit is the default
            var off = on; off.stampFitOff = 1;              // the pre-185 stamps (-stampfit 0)
            Assert.IsTrue(on.StampFit && !off.StampFit);
            return new W { on = on, off = off, o = Offsets.For(in off), map = SharedMaps.Get(on) };   // the map ignores the flag; the default spec's shared map
        }

        private static string Dir()
        {
            string d = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "TerrainGen", "stampfit185");
            Directory.CreateDirectory(d);
            return d;
        }

        private static IEnumerable<Stamps.Stamp> LandStamps(W w, byte kind)
        {
            int cells = w.on.worldSizeVoxels >> Stamps.CELL_SHIFT;
            for (int gz = 0; gz < cells; gz++)
            for (int gx = 0; gx < cells; gx++)
            {
                if (!Stamps.CellStamp(in w.o, gx, gz, out var s) || s.kind != kind) continue;
                if (w.Site(s.pos) <= w.on.seaLevelY + 30) continue;
                yield return s;
            }
        }

        [Test]
        public void DetailAmpSum_IsTheOctaveSum()
        {
            float sum = 0f;
            for (int i = 0; i < ShapeField.DETAIL_OCTAVES; i++) sum += ShapeField.Amplitude(i);
            Assert.AreEqual(sum, ShapeField.DETAIL_AMP_SUM, 1e-4f);
            Assert.Less(Stamps.GEYSER_REACH_MAX, 1400f, "the basin stays inside the singleton's keep-out");
        }

        // ------------------------------------------------------------------ caves
        /// Along-axis reach (voxels from the site) of the tube's surface opening:
        /// the farthest column (stride 8) whose top S says the carve breaks.
        private static float OpenReach(W w, bool fit, in Stamps.Stamp s, float2 dir, out int cols)
        {
            float r = Stamps.CaveRadius(in s), reach = Stamps.CaveReach(in s) + r;
            float2 side = new float2(-dir.y, dir.x);
            float far = -1f; cols = 0;
            for (float a = -r; a <= reach; a += 8f)
            for (float b = -r; b <= r; b += 8f)
            {
                float2 p = s.pos + dir * a + side * b;
                int x = (int)math.floor(p.x), z = (int)math.floor(p.y);
                var c = w.S(fit, x, z);
                if (!c.carvesTop) continue;
                cols++;
                far = math.max(far, math.dot(new float2(x + 0.5f, z + 0.5f) - s.pos, dir));
            }
            return far;
        }

        [Test]
        [Category("Slow")]
        public void CaveMouth_TubeStaysCovered_FourSeeds_AndForcedDownhill()
        {
            var lines = new List<string>();
            int kept = 0, trenchesOff = 0, total = 0;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            foreach (uint seed in Seeds)
            {
                var w = World(seed); var v = w.map.View;
                int k = 0, rej = 0, rot = 0;
                foreach (var s0 in LandStamps(w, Stamps.CaveMouth))
                {
                    total++;
                    var s = s0; int g = w.Site(s.pos);
                    float r = Stamps.CaveRadius(in s);
                    float offReach = OpenReach(w, false, in s0, s0.dir, out int offCols);
                    bool offTrench = offReach > Stamps.CAVE_MOUTH_MAX + r;
                    if (offTrench) trenchesOff++;
                    bool fit = !Stamps.NearSea(in w.on, in v, s.pos) && Stamps.FitCave(in w.on, in v, ref s, g, out byte q);
                    if (!fit)
                    {
                        rej++;
                        lines.Add($"seed {seed} cave {s0.pos}: DROPPED (OFF opening reach {offReach * 0.1f:F0} m{(offTrench ? ", a TRENCH" : "")})");
                        continue;
                    }
                    k++; kept++; if (!math.all(s.dir == s0.dir)) rot++;
                    float onReach = OpenReach(w, true, in s, s.dir, out int onCols);
                    lines.Add($"seed {seed} cave {s0.pos} ground {g} axis OFF {s0.dir} ON {s.dir}: kept, axis turned {(math.all(s.dir == s0.dir) ? "no" : "yes")}; opening reach ON {onReach * 0.1f:F0} m ({onCols} cols), OFF {offReach * 0.1f:F0} m ({offCols} cols){(offTrench ? " TRENCH" : "")}");
                    Assert.LessOrEqual(onReach, Stamps.CAVE_MOUTH_MAX + r, $"seed {seed} cave at {s0.pos}: the fitted tube opens {onReach * 0.1f:F0} m from its site (a trench)");
                    // S and D agree on the fitted axis: the axis 3/4 down the tube is carved air
                    // (unless that column is near a river / lake, where S switches the carve off).
                    float st = Stamps.CaveReach(in s) * 0.75f, kk = Stamps.CaveSlope(in s);
                    float2 pp = s.pos + s.dir * st;
                    int ax = (int)math.floor(pp.x), az = (int)math.floor(pp.y);
                    int ay = (int)math.floor(g + 0.35f * r - kk * st);
                    var cc = w.S(true, ax, az);
                    if (cc.stamp3D) Assert.AreEqual(GenMat.Air, w.D(true, in cc, ax, ay, az), $"seed {seed} cave {s0.pos}: D does not carve the fitted axis");
                }
                lines.Add($"seed {seed}: {k} caves kept ({rot} turned), {rej} dropped");
            }
            Assert.Greater(kept, 0, "no cave survives the fit on four seeds");
            Assert.Greater(trenchesOff, 0, "negative control: the OFF arm shows no trench, so the test cannot see one");

            // Adversarial: a cave forced onto a stamp-free land site whose slope is just under the
            // tube's own (0.6 at b = 0), pointing straight downhill: the tube runs half buried, a trench.
            // (A slope steeper than the tube leaves it in the air: no carve at all.)
            try {
                var w = World(42u); var v = w.map.View;
                // Candidates: stamp-free land sites with slope 0.4-0.58 (stride 6 cells); the first whose
                // downhill tube really trenches (opening reach > CAVE_MOUTH_MAX + r) is the case.
                float best = 0f, offReach = -1f; float2 site = default; int tried = 0;
                var s = default(Stamps.Stamp); int g = 0; float r = 0f;
                int n = w.on.mapCells, cv = w.on.mapCellVoxels;
                for (int iz = 6; iz < n - 6 && tried < 60; iz += 6)
                for (int ix = 6; ix < n - 6 && tried < 60; ix += 6)
                {
                    float2 p = new float2(ix + 0.5f, iz + 0.5f) * cv;
                    if (Stamps.StampAt(in w.o, p, out _)) continue;
                    if (w.Site(p) <= w.on.seaLevelY + 300 || Stamps.NearSea(in w.on, in v, p)) continue;
                    float2 gr = ShapeField.MapGradient(in v, p.x, p.y);
                    float gl = math.length(gr);
                    if (gl < 0.4f || gl > 0.58f) continue;
                    tried++;
                    var sc = new Stamps.Stamp { kind = Stamps.CaveMouth, pos = p, dir = -gr / gl, a = 0.99f, b = 0f };
                    int gc = w.Site(p);
                    float rc = ForcedReach(w, in sc, gc);
                    if (rc > Stamps.CAVE_MOUTH_MAX + Stamps.CaveRadius(in sc)) { s = sc; g = gc; r = Stamps.CaveRadius(in sc); site = p; best = gl; offReach = rc; tried = 1000; }
                }
                Assert.Greater(offReach, 0f, "no forced downhill site trenches: not adversarial");
                var sf = s;
                bool fit = Stamps.FitCave(in w.on, in v, ref sf, g, out byte q);
                float onReach = fit ? ForcedReach(w, in sf, g) : -1f;
                lines.Add($"ADVERSARIAL seed 42: forced cave at {site} (map slope {best:F2}), downhill axis: opening reach {offReach * 0.1f:F0} m; " +
                          (fit ? $"fit turns it {q} quarter(s): reach {onReach * 0.1f:F0} m" : "fit DROPS it"));
                if (fit) Assert.LessOrEqual(onReach, Stamps.CAVE_MOUTH_MAX + r, "the forced cave's fitted tube is a trench");
            }
            finally
            {
                lines.Add($"total {total} land caves, {kept} kept; OFF trenches {trenchesOff}; {sw.Elapsed.TotalSeconds:F1} s");
                File.WriteAllLines(Path.Combine(Dir(), "caves.txt"), lines);
                UnityEngine.Debug.Log("[StampFit caves]\n" + string.Join("\n", lines));
            }
        }
        private const float ForcedSlope = 0.5f;

        /// A stamp the map does not hold: its opening reach from the natural
        /// column tops (S at those columns, then the same SurfaceCarve test S uses).
        private static float ForcedReach(W w, in Stamps.Stamp s, int g)
        {
            float r = Stamps.CaveRadius(in s), reach = Stamps.CaveReach(in s) + r, far = -1f;
            float2 side = new float2(-s.dir.y, s.dir.x);
            for (float a = -r; a <= reach; a += 8f)
            for (float b = -r; b <= r; b += 8f)
            {
                float2 p = s.pos + s.dir * a + side * b;
                int x = (int)math.floor(p.x), z = (int)math.floor(p.y);
                int h = w.S(true, x, z).height;
                if (Stamps.SurfaceCarve(in s, g, x, h, z, h)) far = math.max(far, math.dot(new float2(x + 0.5f, z + 0.5f) - s.pos, s.dir));
            }
            return far;
        }

        // ------------------------------------------------------------------ ledges
        private static bool OnBoundary(W w, int x, int z, int top)
        {
            float wave = DetailField.LayerWave(in w.o, new float2(x + 0.5f, z + 0.5f));
            int k = DetailField.LayerIndex(w.on.seed, top + wave);
            for (int d = -1; d <= 2; d++) if (ShapeField.LedgeFitTop(w.on.seed, k + d, wave) == top) return true;
            return false;
        }

        /// Checks one ON ledge column; returns the number of ring steps to its +x / +z neighbours.
        private static int CheckLedge(W w, int x, int z, in ColumnSample c, ref int pairs)
        {
            Assert.IsTrue(OnBoundary(w, x, z, c.height), $"seed {w.on.seed} ledge ({x},{z}) top {c.height} is not on a rock-layer boundary");
            Assert.AreEqual(ShapeField.LEDGE_FIT_THICK, c.height - c.open3Hi, $"seed {w.on.seed} ledge ({x},{z}) is not {ShapeField.LEDGE_FIT_THICK} voxels thick");
            int rings = 0;
            for (int d = 0; d < 2; d++)
            {
                var n = w.S(true, x + (d == 0 ? 1 : 0), z + (d == 1 ? 1 : 0));
                if (n.open3Hi <= n.open3Lo) continue;
                pairs++;
                int dh = math.abs(n.height - c.height);
                if (dh > 1 && dh < (int)ShapeField.LEDGE_SPACING - 2) rings++;
            }
            return rings;
        }

        private static int RingsOff(W w, int x, int z)
        {
            var c = w.S(false, x, z);
            if (c.open3Hi <= c.open3Lo) return 0;
            int rings = 0;
            for (int d = 0; d < 2; d++)
            {
                var n = w.S(false, x + (d == 0 ? 1 : 0), z + (d == 1 ? 1 : 0));
                if (n.open3Hi > n.open3Lo && math.abs(n.height - c.height) > 1) rings++;
            }
            return rings;
        }

        [Test]
        [Category("Slow")]
        public void Ledges_FlatShelfOnALayerBoundary_FourSeeds_AndTallestCliff()
        {
            var lines = new List<string>();
            int ledgesOn = 0, ledgesOff = 0, ringsOff = 0, pairsOn = 0;
            long tOn = 0, tOff = 0; int nOn = 0, nOff = 0;
            int bestStep = 0; uint bestSeed = 0; int bx = 0, bz = 0;
            foreach (uint seed in Seeds)
            {
                var w = World(seed);
                float c0 = w.on.worldSizeVoxels * 0.5f;
                int lOn = 0, lOff = 0, checkedD = 0;
                for (int gz = -120; gz < 120; gz++)
                for (int gx = -120; gx < 120; gx++)
                {
                    int x = (int)c0 + gx * 250, z = (int)c0 + gz * 250;
                    long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                    var off = w.S(false, x, z);
                    long t1 = System.Diagnostics.Stopwatch.GetTimestamp();
                    var c = w.S(true, x, z);
                    tOn += System.Diagnostics.Stopwatch.GetTimestamp() - t1; tOff += t1 - t0; nOn++; nOff++;
                    if (off.open3Hi > off.open3Lo) { lOff++; ringsOff += RingsOff(w, x, z); }
                    if (c.open3Hi <= c.open3Lo) continue;
                    lOn++;
                    int rings = CheckLedge(w, x, z, in c, ref pairsOn);
                    Assert.AreEqual(0, rings, $"seed {seed} ledge ({x},{z}): a contour-ring step to a neighbour");
                    if (checkedD < 40)
                    {
                        Assert.AreEqual(GenMat.Air, w.D(true, in c, x, (c.open3Lo + c.open3Hi) / 2, z), $"seed {seed}: no air under the ledge at ({x},{z})");
                        Assert.IsTrue(GenMat.IsSolid(w.D(true, in c, x, c.height, z)), $"seed {seed}: ledge top ({x},{z}) not solid");
                        Assert.IsTrue(GenMat.IsSolid(w.D(true, in c, x, c.open3Hi, z)), $"seed {seed}: ledge ceiling ({x},{z}) not solid");
                        checkedD++;
                    }
                    if (c.height - c.open3Lo > bestStep) { bestStep = c.height - c.open3Lo; bestSeed = seed; bx = x; bz = z; }
                }
                ledgesOn += lOn; ledgesOff += lOff;
                lines.Add($"seed {seed}: ledge columns on a 25 m grid ON {lOn}, OFF {lOff}");
            }
            Assert.Greater(ledgesOn, 0, "no ledge survives on four seeds");
            Assert.Greater(ringsOff, 0, "negative control: the OFF ledges show no contour-ring step");
            // Adversarial: every column of a 64 x 64 window at the tallest shelf found.
            {
                var w = World(bestSeed); int n = 0, rings = 0, pairs = 0;
                for (int dz = -32; dz < 32; dz++)
                for (int dx = -32; dx < 32; dx++)
                {
                    var c = w.S(true, bx + dx, bz + dz);
                    if (c.open3Hi <= c.open3Lo) continue;
                    n++; rings += CheckLedge(w, bx + dx, bz + dz, in c, ref pairs);
                }
                lines.Add($"ADVERSARIAL seed {bestSeed} window at ({bx},{bz}) (shelf {bestStep * 0.1f:F1} m over its ground): {n} ledge columns, {pairs} neighbour pairs, ring steps {rings}");
                Assert.AreEqual(0, rings, "a contour-ring step in the tallest shelf's window");
            }
            double usOn = tOn * 1e6 / System.Diagnostics.Stopwatch.Frequency / nOn, usOff = tOff * 1e6 / System.Diagnostics.Stopwatch.Frequency / nOff;
            lines.Add($"ON ledge columns {ledgesOn}, OFF {ledgesOff}; OFF ring steps {ringsOff}; ON neighbour pairs {pairsOn}, ring steps 0");
            lines.Add($"rough cost (managed S, all land and sea grid columns): ON {usOn:F2} us/column, OFF {usOff:F2} us/column");
            File.WriteAllLines(Path.Combine(Dir(), "ledges.txt"), lines);
            UnityEngine.Debug.Log("[StampFit ledges]\n" + string.Join("\n", lines));
        }

        // ------------------------------------------------------------------ geyser
        private static string GeyserCase(W w, string name, ref int checkedCols)
        {
            var v = w.map.View;
            float2 gs = Stamps.GeyserSite(in w.on, in w.o);
            int h0 = w.Site(gs);
            if (h0 <= w.on.seaLevelY + 20) return $"{name}: the basin site is not on land, no basin";
            int levels = 0; var seen = new HashSet<int>();
            int cols = 0, pools = 0, minR = int.MaxValue, maxR = 0;
            for (int a = 0; a < 64; a++)   // 64 rays: terraces on the ground, the outline's radius
            {
                float ang = a * (math.PI * 2f / 64f);
                float2 dir = new float2(math.cos(ang), math.sin(ang));
                int lastIn = 0;
                for (int d = 0; d < (int)Stamps.GEYSER_REACH_MAX; d += 4)
                {
                    float2 p = gs + dir * d;
                    int x = (int)math.floor(p.x), z = (int)math.floor(p.y);
                    var c = w.S(true, x, z);
                    if (c.stampMat != GenMat.Sinter) continue;
                    lastIn = d; cols++;
                    if (c.water > c.height) { pools++; continue; }
                    ShapeField.SampleMap(in v, x + 0.5f, z + 0.5f, out float hm, out _);
                    int t = Stamps.GeyserTerraceY(in w.on, in v, in w.o, new float2(x + 0.5f, z + 0.5f), h0);
                    if (t == int.MinValue || c.height != t) continue;   // raised by a lake rim / river hold / ledge
                    float rel = c.height - (w.on.seaLevelY + hm);
                    Assert.IsTrue(rel >= -Stamps.GEYSER_RISE - 1f && rel < Stamps.GEYSER_STEP + 1f, $"{name}: terrace ({x},{z}) {rel:F0} voxels off the ground");
                    Assert.AreEqual(0, (h0 - c.height) % Stamps.GEYSER_STEP, $"{name}: terrace ({x},{z}) is not on a step");
                    if (seen.Add(c.height)) levels++;
                }
                if (lastIn > 0) { minR = math.min(minR, lastIn); maxR = math.max(maxR, lastIn); }
            }
            checkedCols += cols;
            // The pools at rest: FluidAtRestTests' tile job over every map cell the pools can reach.
            int n = w.on.mapCells, cv = w.on.mapCellVoxels, rc = (530 + cv) / cv;
            int2 gc = (int2)math.floor(gs / cv);
            var list = new Unity.Collections.NativeList<int2>(Unity.Collections.Allocator.TempJob);
            for (int iz = gc.y - rc; iz <= gc.y + rc; iz++)
            for (int ix = gc.x - rc; ix <= gc.x + rc; ix++)
                if (ix >= 1 && iz >= 1 && ix < n - 1 && iz < n - 1) list.Add(new int2(ix, iz));
            var tot = FluidAtRestTests.RunTiles(w.on, w.o, w.map, list, $"{name} geyser pools (stampfit)", $"stampfit185_geyser_{name}.txt");
            long poolCols = tot[FluidAtRestTests.GEYSER * FluidAtRestTests.K + 0], bad = tot[FluidAtRestTests.GEYSER * FluidAtRestTests.K + 2];
            Assert.AreEqual(0, bad, $"{name}: {bad} geyser pool voxels not at rest");
            return $"{name}: site {gs} vents {h0 - w.on.seaLevelY} voxels over sea; {levels} terrace levels on 64 rays ({cols} sinter cols, {pools} pool cols); " +
                   $"outline radius {minR * 0.1f:F0}-{maxR * 0.1f:F0} m; pools: {poolCols} columns, unsettled {bad}";
        }

        [Test]
        [Category("Slow")]
        public void GeyserBasin_TerracesOnTheGround_PoolsAtRest_FourSeeds_AndOnASlope()
        {
            var lines = new List<string>(); int cols = 0;
            foreach (uint seed in Seeds) lines.Add(GeyserCase(World(seed), $"seed{seed}", ref cols));
            // Adversarial: the basin moved (offsets' peakDir) onto the steepest land site of 64 around the centre.
            {
                var w = World(42u); var v = w.map.View;
                float best = -1f; float2 bestDir = default;
                for (int a = 0; a < 64; a++)
                {
                    float ang = a * (math.PI * 2f / 64f);
                    var o2 = w.o; o2.peakDir = new float2(math.cos(ang), math.sin(ang));
                    float2 gs = Stamps.GeyserSite(in w.on, in o2);
                    if (w.Site(gs) <= w.on.seaLevelY + 200) continue;
                    float gl = math.length(ShapeField.MapGradient(in v, gs.x, gs.y));
                    if (gl > best) { best = gl; bestDir = o2.peakDir; }
                }
                Assert.Greater(best, 0f, "no land site for the moved basin");
                var wm = w; wm.o.peakDir = bestDir;
                lines.Add($"ADVERSARIAL (map slope {best:F2} at the site): " + GeyserCase(wm, "slope42", ref cols));
            }
            Assert.Greater(cols, 0, "no basin column met");
            File.WriteAllLines(Path.Combine(Dir(), "geyser.txt"), lines);
            UnityEngine.Debug.Log("[StampFit geyser]\n" + string.Join("\n", lines));
        }

        // ------------------------------------------------------------------ hoops
        [Test]
        public void Hoops_OnlyOnARiseOrInRock_FourSeeds_AndThePlain()
        {
            var lines = new List<string>(); int keptAll = 0, droppedAll = 0;
            foreach (uint seed in Seeds)
            {
                var w = World(seed); var v = w.map.View;
                int kept = 0, dropped = 0;
                foreach (var s in LandStamps(w, Stamps.Hoop))
                {
                    bool ctx = Stamps.HoopContext(in w.on, in v, s.pos);
                    // The hoop's foot column (along 0, side 0): its stone, unless the sea keep-out took it.
                    int x = (int)math.floor(s.pos.x), z = (int)math.floor(s.pos.y);
                    var c = w.S(true, x, z); var off = w.S(false, x, z);
                    bool stoneOff = off.stampMat == GenMat.StandingStone, stoneOn = c.stampMat == GenMat.StandingStone;
                    if (!stoneOff) continue;   // the OFF hoop is not drawn here either (river, texel)
                    ShapeField.SampleMap(in v, s.pos.x, s.pos.y, out float h, out float rough);
                    lines.Add($"seed {seed} hoop {s.pos}: rough {rough:F2}, context {ctx}, ON stone {stoneOn}");
                    if (!ctx) { dropped++; Assert.IsFalse(stoneOn, $"seed {seed} hoop {s.pos}: stands on a plain"); }
                    else { kept++; if (!Stamps.NearSea(in w.on, in v, new float2(x + 0.5f, z + 0.5f))) Assert.IsTrue(stoneOn, $"seed {seed} hoop {s.pos}: in context but missing"); }
                }
                keptAll += kept; droppedAll += dropped;
                lines.Add($"seed {seed}: hoops kept {kept}, dropped {dropped}");
                // Adversarial: the flattest, smoothest land cell of the map is a plain: no hoop there.
                int n = w.on.mapCells, cv = w.on.mapCellVoxels; float bestR = float.MaxValue; float2 plain = default;
                for (int iz = 8; iz < n - 8; iz += 4)
                for (int ix = 8; ix < n - 8; ix += 4)
                {
                    float2 p = new float2(ix + 0.5f, iz + 0.5f) * cv;
                    if (w.Site(p) <= w.on.seaLevelY + 60) continue;
                    ShapeField.SampleMap(in v, p.x, p.y, out _, out float rr);
                    float score = rr + math.length(ShapeField.MapGradient(in v, p.x, p.y));
                    if (score < bestR) { bestR = score; plain = p; }
                }
                Assert.IsFalse(Stamps.HoopContext(in w.on, in v, plain), $"seed {seed}: the flattest plain {plain} admits a hoop");
            }
            lines.Add($"total hoops kept {keptAll}, dropped {droppedAll}");
            Assert.Greater(keptAll, 0, "no hoop survives on four seeds");
            Assert.Greater(droppedAll, 0, "no hoop stood on a plain (negative control)");
            File.WriteAllLines(Path.Combine(Dir(), "hoops.txt"), lines);
            UnityEngine.Debug.Log("[StampFit hoops]\n" + string.Join("\n", lines));
        }
    }
}
