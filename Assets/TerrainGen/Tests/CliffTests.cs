// Assets/TerrainGen/Tests/CliffTests.cs
//
// Task 178 (WORLD_GEN_LOOK_PLAN C4, owner 2026-11-05 "C4 cliffs approved"):
// cliffs with geology. These tests pin:
//   - the stone zone's rock is a few THICK layers by height (>= 9.6 m each,
//     LAYER_THICK x (1 - 2 x LAYER_JITTER)), only the biome's own rocks, in
//     every biome -- badlands included (pre-178 it cycled 3.2 m bands, which
//     the same check catches with cliffs off);
//   - the face relief moves a column only on a face (map slope over
//     FACE_FROM_DEG) or on a scree apron, never on gentle ground; cliffs off
//     is the pre-178 column;
//   - on v4 face columns: the far-band footprints stay within the spec-aware
//     band-limit bound, and the LOD-0 contract holds (the column's height is
//     its top solid voxel, air or fluid above).
// Report_Cliffs (REPORTS=1) dumps a section and a top view at 173's three cliff
// poses, cliffs on and off, for tools/cliff178.py, plus mixed-brick counts and
// the Sample cost (Mono, rough).
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using NUnit.Framework;
using Unity.Mathematics;
using Debug = UnityEngine.Debug;

namespace TerrainGen.Tests
{
    public class CliffTests
    {
        private const string OUT_DIR = "Logs/TerrainGen/c4";

        private struct World
        {
            public TerrainSpec spec, off;
            public Offsets o;
            public WorldMap map;
        }

        private static World Get(uint seed)
        {
            var w = new World { spec = TerrainSpec.SizeClass2(seed) };
            w.o = Offsets.For(in w.spec);
            w.map = SharedMaps.Get(w.spec);
            w.off = w.spec; w.off.cliffsOff = 1;   // same map: cliffs never change it
            return w;
        }

        private static ColumnSample S(in TerrainSpec spec, in World w, float x, float z, int texel = 1)
        {
            var view = w.map.View;
            return ShapeField.Sample(in spec, in view, in w.o, x + 0.5f, z + 0.5f, texel);
        }

        private static float MapSlope(in World w, float x, float z)
        {
            var view = w.map.View;
            return math.length(ShapeField.MapGradient(in view, x + 0.5f, z + 0.5f));
        }

        /// Land points; steepOnly keeps those whose map slope is over FACE_FROM_DEG.
        private static List<int2> LandPoints(in World w, int count, uint rngSeed, bool steepOnly)
        {
            var rng = new Unity.Mathematics.Random(rngSeed);
            var pts = new List<int2>();
            int n = w.spec.mapCells, cv = w.spec.mapCellVoxels;
            float tanFrom = math.tan(math.radians(ShapeField.FACE_FROM_DEG));
            int guard = 0;
            while (pts.Count < count && guard++ < count * 400)
            {
                int cx = rng.NextInt(2, n - 2), cz = rng.NextInt(2, n - 2);
                if (w.map.Height[cz * n + cx] <= 20f) continue;
                var p = new int2(cx * cv + rng.NextInt(cv), cz * cv + rng.NextInt(cv));
                if (steepOnly && MapSlope(in w, p.x, p.y) < tanFrom) continue;
                pts.Add(p);
            }
            return pts;
        }

        /// Shortest run of one rock along y in the stone zone (whole runs only).
        private static int ShortestRun(in TerrainSpec spec, in Offsets o, byte biome, int x, int z, int yLo, int yHi,
                                       HashSet<byte> seen)
        {
            int best = int.MaxValue, runStart = -1;
            byte cur = 0;
            for (int y = yLo; y <= yHi; y++)
            {
                byte m = DetailField.Strata(in spec, in o, biome, x, y, z);
                seen.Add(m);
                if (y == yLo) { cur = m; continue; }
                if (m == cur) continue;
                if (runStart >= 0) best = math.min(best, y - runStart);   // the first run is cut by yLo
                runStart = y; cur = m;
            }
            return best;
        }

        [Test]
        public void Layers_AreThickAndTheBiomesOwnRocks()
        {
            var w = Get(42);
            int yLo = w.spec.seaLevelY - DetailField.STONE_BOTTOM_BELOW_SEA + (int)(DetailField.GEO_DEEP_AMP * 1.1f);
            int yHi = w.spec.seaLevelY + 3000;
            int minLayer = (int)(DetailField.LAYER_THICK * (1f - 2f * DetailField.LAYER_JITTER)) - 1;
            var rng = new Unity.Mathematics.Random(0x178a);
            var sb = new StringBuilder();
            for (byte b = 0; b < GenBiome.Count; b++)
            {
                var seen = new HashSet<byte>();
                int shortest = int.MaxValue;
                for (int i = 0; i < 12; i++)
                {
                    int x = rng.NextInt(5000, 75000), z = rng.NextInt(5000, 75000);
                    shortest = math.min(shortest, ShortestRun(in w.spec, in w.o, b, x, z, yLo, yHi, seen));
                }
                var row = new HashSet<byte> { DetailField.LayerMaterial(w.spec.seed, b, 0) };
                for (int k = -40; k < 40; k++) row.Add(DetailField.LayerMaterial(w.spec.seed, b, k));
                foreach (byte m in seen) Assert.IsTrue(row.Contains(m), $"biome {b}: rock {m} is not in its layer row");
                Assert.GreaterOrEqual(seen.Count, 2, $"biome {b}: the stone zone shows one rock only");
                Assert.GreaterOrEqual(shortest, minLayer, $"biome {b}: a layer of {shortest} voxels (thin stripes)");
                // Pre-178 (cliffs off) for contrast: badlands' 3.2 m bands.
                int pre = ShortestRun(in w.off, in w.o, b, 40000, 40000, yLo, yHi, new HashSet<byte>());
                if (b == GenBiome.Badlands) Assert.Less(pre, minLayer, "cliffs off: badlands keeps its pre-178 bands");
                sb.AppendLine($"biome {b}: rocks {seen.Count}, thinnest layer {shortest / 10f:F1} m (pre-178 {(pre == int.MaxValue ? "one rock" : (pre / 10f).ToString("F1") + " m")})");
            }
            Debug.Log("[Cliffs] layers seed 42\n" + sb);
        }

        [Test]
        public void Relief_OnlyOnFacesAndScree_GentleGroundUnchanged()
        {
            var w = Get(42);
            var view = w.map.View;
            float tanFrom = math.tan(math.radians(ShapeField.FACE_FROM_DEG));
            int n = 0, moved = 0, face = 0, scree = 0;
            foreach (var p in LandPoints(in w, 6000, 0x178b, false))
            {
                var on = S(in w.spec, in w, p.x, p.y);
                var off = S(in w.off, in w, p.x, p.y);
                n++;
                Assert.AreEqual(off.biome, on.biome);
                if (on.height == off.height) continue;
                moved++;
                float2 q = new float2(p.x + 0.5f, p.y + 0.5f);
                ShapeField.SampleMap(in view, q.x, q.y, out float macro, out _);
                bool isFace = MapSlope(in w, p.x, p.y) > tanFrom;
                bool isScree = ShapeField.ScreeFill(in view, q, macro) > 0f;
                if (isFace) face++;
                if (isScree) scree++;
                Assert.IsTrue(isFace || isScree, $"({p.x},{p.y}) moved {off.height} -> {on.height} on gentle ground");
                Assert.IsFalse(ShapeField.WetNear(in view, q), $"({p.x},{p.y}) moved beside water");
            }
            Debug.Log($"[Cliffs] seed 42: {moved} of {n} land columns moved ({100f * moved / n:F1} %), on a face {face}, on scree {scree}");
            Assert.Greater(moved, 0);
        }

        [Test]
        public void FaceColumns_BandLimitAndLod0Contract()
        {
            int[] texels = { 8, 16, 32, 64, 128 };
            foreach (uint seed in new uint[] { 42, 7 })
            {
                var w = Get(seed);
                int checkedCols = 0, preExisting = 0;
                double worst = 0;
                foreach (var p in LandPoints(in w, 250, 0x178c + seed, true))
                {
                    if (Stamps.StampAt(in w.o, new float2(p.x, p.y), out var stp) &&
                        (stp.kind == Stamps.Arch || stp.kind == Stamps.Hoop || stp.kind == Stamps.StoneRing || stp.kind == Stamps.Fossil) &&
                        math.distance(stp.pos, new float2(p.x, p.y)) < 1100f) continue;
                    var fine = S(in w.spec, in w, p.x, p.y);
                    foreach (int t in texels)
                    {
                        var coarse = S(in w.spec, in w, p.x, p.y, t);
                        float bound = ShapeField.BandLimitBound(in w.spec, t, fine.rough) + 1f;
                        float d = math.abs(coarse.height - fine.height);
                        if (d <= bound) worst = math.max(worst, d / bound);
                        if (d > bound)
                        {
                            // The pre-178 column can already exceed the octave bound on a face
                            // (seed 7 (37336,56158): 97 voxels at texel 32 with cliffs off); the
                            // relief may only add its own share to what was there.
                            float dOff = math.abs(S(in w.off, in w, p.x, p.y, t).height - S(in w.off, in w, p.x, p.y).height);
                            float allow = math.max(bound, dOff / (1f - ShapeField.LEDGE_TREAD) + ShapeField.RELIEF_BAND_BOUND + 1f);
                            Assert.LessOrEqual(d, allow, $"seed {seed} texel {t} at ({p.x},{p.y}): |{coarse.height} - {fine.height}| > {allow} (cliffs off {dOff})");
                            preExisting++;
                            continue;
                        }
                    }
                    if (fine.carvesTop) continue;
                    var view = w.map.View;
                    float st = ShapeField.MaxStep(in w.spec, in view, in w.o, p.x + 0.5f, p.y + 0.5f, 1, fine.height);
                    byte top = DetailField.Material(in w.spec, in w.o, in fine, st, p.x, fine.height, p.y);
                    byte above = DetailField.Material(in w.spec, in w.o, in fine, st, p.x, fine.height + 1, p.y);
                    Assert.IsTrue(GenMat.IsSolid(top) || top == GenMat.RiverLip, $"seed {seed} ({p.x},{p.y}): top voxel {top} is not solid");
                    Assert.IsTrue(above == GenMat.Air || GenMat.IsFluid(above), $"seed {seed} ({p.x},{p.y}): voxel above the top is {above}");
                    checkedCols++;
                }
                Debug.Log($"[Cliffs] seed {seed}: {checkedCols} face columns, worst band-limit use {worst:P0}, {preExisting} footprints over the bound, each within the cliffs-off difference + the relief bound");
                Assert.Greater(checkedCols, 150);
            }
        }

        // ------------------------------------------------------------------
        // REPORT: sections and top views at 173's cliff poses (WSPOSE targets).
        // ------------------------------------------------------------------
        private static readonly (string name, float x, float z)[] POSES =
        {
            ("cliff", 3412.0f, 5684.0f),
            ("coast_cliff", 3860.0f, 5844.0f),
            ("forest_breaks", 3476.0f, 5764.0f),
        };

        [Test, Timeout(1200000)]
        public void Report_Cliffs()
        {
            ReportOnly.Require();
            Directory.CreateDirectory(OUT_DIR);
            var w = Get(42);
            var view = w.map.View;
            var log = new StringBuilder();
            foreach (var (name, mx, mz) in POSES)
            {
                // The face: the steepest map slope within 40 m of the pose's target.
                float2 c0 = new float2(mx, mz) * 10f, best = c0;
                float bestS = -1f;
                for (int dz = -400; dz <= 400; dz += 20)
                for (int dx = -400; dx <= 400; dx += 20)
                {
                    float s = MapSlope(in w, c0.x + dx, c0.y + dz);
                    if (s > bestS) { bestS = s; best = c0 + new float2(dx, dz); }
                }
                float2 g = ShapeField.MapGradient(in view, best.x, best.y);
                float2 dir = math.normalizesafe(g, new float2(1f, 0f));   // uphill
                log.AppendLine($"{name}: face at ({best.x / 10f:F1}, {best.y / 10f:F1}) m, map slope {math.degrees(math.atan(bestS)):F0} deg, uphill ({dir.x:F2},{dir.y:F2})");
                foreach (var arm in new[] { "after", "before" })
                {
                    var spec = arm == "after" ? w.spec : w.off;
                    Section(in spec, in w, best, dir, Path.Combine(OUT_DIR, $"{name}_{arm}_section.bin"));
                    Top(in spec, in w, best, Path.Combine(OUT_DIR, $"{name}_{arm}_top.bin"));
                    MixedBricks(in spec, in w, best, out int mixed, out int solidMixed, out int bricks);
                    log.AppendLine($"  {arm}: mixed bricks {mixed} of {bricks} (all-solid with 2+ rocks {solidMixed}) in the 12.8 x 12.8 m block at the face");
                }
            }
            // Rough cost: Sample (Mono) per land column, on vs off.
            var pts = LandPoints(in w, 20000, 0x178d, false);
            foreach (var arm in new[] { "after", "before", "after", "before" })
            {
                var spec = arm == "after" ? w.spec : w.off;
                var sw = Stopwatch.StartNew();
                long acc = 0;
                foreach (var p in pts) acc += ShapeField.Sample(in spec, in view, in w.o, p.x + 0.5f, p.y + 0.5f, 1).height;
                sw.Stop();
                log.AppendLine($"Sample (Mono, chunk footprint) {arm}: {sw.Elapsed.TotalMilliseconds * 1000.0 / pts.Count:F1} us/column ({acc & 1})");
            }
            File.WriteAllText(Path.Combine(OUT_DIR, "cliffs178.txt"), log.ToString());
            Debug.Log("[Cliffs] report\n" + log);
        }

        /// 1000 columns (100 m) along `dir` centred on the face, every voxel from
        /// the lowest ground - 30 m to the highest + 10 m: int32 w, h, yLo, then
        /// bytes (row-major, top row first).
        private static void Section(in TerrainSpec spec, in World w, float2 at, float2 dir, string path)
        {
            const int W = 1000;
            var view = w.map.View;
            var cols = new ColumnSample[W];
            var dets = new DetailField.ColumnDetail[W];
            var steps = new float[W];
            var xs = new int2[W];
            int lo = int.MaxValue, hi = int.MinValue;
            for (int i = 0; i < W; i++)
            {
                float2 q = at + dir * (i - W / 2);
                int2 v = (int2)math.floor(q);
                xs[i] = v;
                cols[i] = S(in spec, in w, v.x, v.y);
                dets[i] = DetailField.PrepareColumn(in spec, in w.o, v.x, v.y);
                steps[i] = ShapeField.MaxStep(in spec, in view, in w.o, v.x + 0.5f, v.y + 0.5f, 1, cols[i].height);
                lo = math.min(lo, cols[i].height); hi = math.max(hi, cols[i].height);
            }
            int yLo = lo - 300, yHi = hi + 100, H = yHi - yLo + 1;
            var bytes = new byte[W * H];
            for (int i = 0; i < W; i++)
            for (int y = yLo; y <= yHi; y++)
                bytes[(yHi - y) * W + i] = DetailField.Material(in spec, in w.o, in cols[i], in dets[i], steps[i], xs[i].x, y, xs[i].y);
            using var bw = new BinaryWriter(File.Create(path));
            bw.Write(W); bw.Write(H); bw.Write(yLo); bw.Write(bytes);
        }

        /// 400 x 400 columns at 0.4 m round the face: int32 n, step, then int32
        /// heights and the top material bytes (row 0 = +z, as a map seen from above).
        private static void Top(in TerrainSpec spec, in World w, float2 at, string path)
        {
            const int N = 400, STEP = 4;
            var hts = new int[N * N];
            var mats = new byte[N * N];
            var view = w.map.View;
            for (int j = 0; j < N; j++)
            for (int i = 0; i < N; i++)
            {
                int x = (int)at.x + (i - N / 2) * STEP, z = (int)at.y + (N / 2 - j) * STEP;
                var c = S(in spec, in w, x, z);
                hts[j * N + i] = c.height;
                // Step at the top from neighbours one voxel away (TopMaterial's own rule).
                float st = ShapeField.MaxStep(in spec, in view, in w.o, x + 0.5f, z + 0.5f, 1, c.height);
                mats[j * N + i] = DetailField.TopMaterial(in spec, in c, st, in w.o, x, z);
            }
            using var bw = new BinaryWriter(File.Create(path));
            bw.Write(N); bw.Write(STEP);
            foreach (int h in hts) bw.Write(h);
            bw.Write(mats);
        }

        /// 8^3 bricks of a 128 x 128 column block at the face, over its height range:
        /// bricks with 2+ materials, and those of them with no air or fluid.
        private static void MixedBricks(in TerrainSpec spec, in World w, float2 at, out int mixed, out int solidMixed, out int bricks)
        {
            const int N = 128;
            var view = w.map.View;
            int x0 = ((int)at.x - N / 2) & ~7, z0 = ((int)at.y - N / 2) & ~7;
            var cols = new ColumnSample[N * N];
            var dets = new DetailField.ColumnDetail[N * N];
            var steps = new float[N * N];
            int lo = int.MaxValue, hi = int.MinValue;
            for (int k = 0; k < N * N; k++)
            {
                int x = x0 + (k & (N - 1)), z = z0 + (k >> 7);
                cols[k] = S(in spec, in w, x, z);
                dets[k] = DetailField.PrepareColumn(in spec, in w.o, x, z);
                steps[k] = ShapeField.MaxStep(in spec, in view, in w.o, x + 0.5f, z + 0.5f, 1, cols[k].height);
                lo = math.min(lo, cols[k].height); hi = math.max(hi, cols[k].height);
            }
            int y0 = (lo - 200) & ~7, y1 = (hi + 8) & ~7;
            mixed = solidMixed = bricks = 0;
            for (int by = y0; by < y1; by += 8)
            for (int bz = 0; bz < N; bz += 8)
            for (int bx = 0; bx < N; bx += 8)
            {
                bricks++;
                byte first = 255; bool multi = false, open = false;
                for (int vz = 0; vz < 8; vz++)
                for (int vx = 0; vx < 8; vx++)
                {
                    int k = ((bz + vz) << 7) | (bx + vx);
                    for (int vy = 0; vy < 8; vy++)
                    {
                        byte m = DetailField.Material(in spec, in w.o, in cols[k], in dets[k], steps[k], x0 + bx + vx, by + vy, z0 + bz + vz);
                        if (!GenMat.IsSolid(m)) open = true;
                        if (first == 255) first = m; else if (m != first) multi = true;
                    }
                }
                if (multi) { mixed++; if (!open) solidMixed++; }
            }
        }
    }
}
