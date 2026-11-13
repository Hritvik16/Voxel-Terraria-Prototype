// Assets/TerrainGen/Tests/LandSigTests.cs
//
// Task 189 (WORLD_GEN_LOOK_PLAN C6): landform signatures at walking scale,
// `-landsig 1` (TerrainSpec.landSigOn), default OFF. LandSig.cs.
//   - OFF is today BYTE FOR BYTE: task 188a's column FNV pins (600 land columns,
//     seeds 42 / 7) with the weight plane BUILT on the map, and an FNV over
//     landform columns with and without the plane;
//   - per rule: the dune slip face is downwind and steeper than the stoss;
//     the oriented noise is continuous across its 45-deg bins and the 180-deg
//     wrap; slots and ridges run along the regional downslope; the bench is a
//     flat tread and a riser, continuous at the layer boundaries;
//   - the plane is 0 on keep-out cells (sea / lake / river / easy / route)
//     and present for each landform;
//   - ADVERSARIAL: a ONE-CELL landform patch on a synthetic map (continuous,
//     confined to the bilinear support, no needle); landform columns at a
//     BIOME BORDER (relief on both sides, no step at the border);
//   - band limit with LandSig on; no new needle in the columns;
//   - the shape statistic per landform, 4 seeds, OFF vs ON
//     (Logs/TerrainGen/189/shape189.txt).
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;

namespace TerrainGen.Tests
{
    public unsafe class LandSigTests
    {
        private static readonly uint[] Seeds = { 42u, 7u, 2026u, 31337u };
        private static readonly string[] LF = { "dunes", "canyonlands", "alpine" };
        private const string OUT_DIR = "Logs/TerrainGen/189";

        private struct W
        {
            public TerrainSpec off, on;
            public Offsets o;
            public WorldMap map;
            public MapView view;   // with the plane
        }

        private static W World(uint seed)
        {
            var off = TerrainSpec.SizeClass2(seed, 4);
            var on = off; on.landSigOn = 1;
            var map = SharedMaps.Get(off);   // the map ignores the flag
            map.EnsureLandSigPlane();        // derived; read only while LandSig is on
            return new W { off = off, on = on, o = Offsets.For(in off), map = map, view = map.View };
        }

        private static float H(in TerrainSpec spec, in MapView v, in Offsets o, float x, float z, int texel = 1)
            => ShapeField.Sample(in spec, in v, in o, x, z, texel, false).height;

        /// Cells whose plane weight for landform k is >= minW (random order, deterministic).
        private static List<int2> Cells(in W w, int k, int minW, int count, uint rngSeed)
        {
            var rng = new Unity.Mathematics.Random(rngSeed);
            int n = w.off.mapCells;
            var all = new List<int2>();
            var p = w.map.LandSigPlane;
            for (int z = 8; z < n - 8; z++)
            for (int x = 8; x < n - 8; x++)
                if (p[(z * n + x) * LandSig.PLANE_BYTES + k] >= minW) all.Add(new int2(x, z));
            var res = new List<int2>();
            for (int i = 0; i < count && all.Count > 0; i++) res.Add(all[rng.NextInt(all.Count)]);
            return res;
        }

        private static float2 Centre(in W w, int2 c) => new float2((c.x + 0.5f) * w.off.mapCellVoxels, (c.y + 0.5f) * w.off.mapCellVoxels);

        // ------------------------------------------------------------------
        // OFF == today
        // ------------------------------------------------------------------
        // Task 188a's pins (MatVariantTests.PIN_42 / PIN_7, pre-188a and pre-189 code; -matvariant 1 since task 190a).
        private const uint PIN_42 = 0x6f6c8910u, PIN_7 = 0xeaaad676u;

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

        private static uint LandformHash(in W w, in TerrainSpec spec, in MapView view)
        {
            uint h = 2166136261u;
            for (int k = 0; k < 3; k++)
                foreach (var c in Cells(in w, k, 128, 200, 0x189u + (uint)k))
                {
                    float2 p = Centre(in w, c);
                    for (int j = 0; j < 4; j++) h = (h ^ (uint)H(in spec, in view, in w.o, p.x + j * 13, p.y + j * 7)) * 16777619u;
                }
            return h;
        }

        [Test]
        public void Off_IsTodayByteForByte()
        {
            var a = World(42); var b = World(7);
            Assert.AreEqual(0, a.off.landSigOn, "the default stays OFF (the owner picks it from pictures)");
            Assert.IsFalse(a.off.LandSig);
            Assert.IsTrue(a.view.landSig != null, "the plane is built");
            // the 188a pins were taken with the variant spray: pin the -matvariant 1 arm (task 190a)
            var a1 = a.off; a1.matVariantOn = 1; var b1 = b.off; b1.matVariantOn = 1;
            uint h42 = PinHash(in a1, a.map, in a.view), h7 = PinHash(in b1, b.map, in b.view);
            Debug.Log($"[189] OFF with the plane built: seed 42 fnv 0x{h42:x8} (pin 0x{PIN_42:x8}), seed 7 0x{h7:x8} (pin 0x{PIN_7:x8})");
            Assert.AreEqual(PIN_42, h42, "seed 42: -landsig 0 == before");
            Assert.AreEqual(PIN_7, h7, "seed 7: -landsig 0 == before");
            var bare = a.view; bare.landSig = null;
            uint withPlane = LandformHash(in a, in a.off, in a.view), without = LandformHash(in a, in a.off, in bare);
            uint on = LandformHash(in a, in a.on, in a.view);
            Debug.Log($"[189] landform columns: OFF+plane 0x{withPlane:x8}, OFF bare 0x{without:x8}, ON 0x{on:x8}");
            Assert.AreEqual(without, withPlane, "OFF ignores the plane");
            Assert.AreNotEqual(withPlane, on, "ON changes the landform columns");
            // ON with no plane (an older view) is OFF too.
            Assert.AreEqual(without, LandformHash(in a, in a.on, in bare), "ON without a plane changes nothing");
        }

        // ------------------------------------------------------------------
        // Rules
        // ------------------------------------------------------------------
        [Test]
        public void Dune_SlipFaceIsDownwindAndSteeper()
        {
            foreach (uint seed in Seeds)
            {
                var o = Offsets.For(seed);
                float2 wind = LandSig.Wind(in o), perp = new float2(-wind.y, wind.x);
                var lee = new List<float>(); var stoss = new List<float>();
                var rng = new Unity.Mathematics.Random(seed * 7 + 1);
                for (int t = 0; t < 40; t++)
                {
                    float2 p0 = new float2(rng.NextFloat(5000f, 75000f), rng.NextFloat(5000f, 75000f));
                    for (int i = 0; i < 300; i++)
                    {
                        float2 a = p0 + wind * (i * 10f), b = a + wind * 10f;
                        float s = (LandSig.Dune(in o, b) - LandSig.Dune(in o, a)) / 10f;
                        if (s < 0f) lee.Add(-s); else if (s > 0f) stoss.Add(s);
                    }
                }
                lee.Sort(); stoss.Sort();
                float l90 = lee[(int)(lee.Count * 0.9f)], s90 = stoss[(int)(stoss.Count * 0.9f)], lmax = lee[lee.Count - 1];
                Debug.Log($"[189] seed {seed} dune slope along the wind: lee p90 {l90:F2} max {lmax:F2}, stoss p90 {s90:F2}, ratio {l90 / s90:F2}");
                Assert.Greater(l90, 1.5f * s90, $"seed {seed}: the slip face must be steeper than the stoss");
                Assert.Less(lmax, math.tan(math.radians(45f)), $"seed {seed}: slip face steeper than 45 deg (repose is ~34)");
                // Across the wind the ridges run on: a crest line, not a lump field.
                float along = 0f, across = 0f;
                for (int i = 0; i < 2000; i++)
                {
                    float2 p = new float2(rng.NextFloat(5000f, 75000f), rng.NextFloat(5000f, 75000f));
                    along += math.abs(LandSig.Dune(in o, p + wind * 60f) - LandSig.Dune(in o, p));
                    across += math.abs(LandSig.Dune(in o, p + perp * 60f) - LandSig.Dune(in o, p));
                }
                Assert.Greater(along, 2f * across, $"seed {seed}: dunes are ridges ACROSS the wind");
            }
        }

        /// The column's dunes sharpen the MAP's dune train in phase (the same crests):
        /// LandSig's phase and height rebuild WorldMap.DuneField (to 1 mm: the JIT may
        /// fuse a multiply-add in one copy and not the other).
        [Test]
        public void Dune_PhaseLockedToTheMapDunes()
        {
            foreach (uint seed in Seeds)
            {
                var o = Offsets.For(seed);
                var rng = new Unity.Mathematics.Random(seed * 3 + 5);
                float worst = 0f;
                for (int i = 0; i < 4000; i++)
                {
                    float2 p = new float2(rng.NextFloat(0f, 80000f), rng.NextFloat(0f, 80000f));
                    float t = LandSig.DunePhase(in o, p);
                    t -= math.floor(t);
                    float y = t < 0.75f ? t / 0.75f : (1f - t) / 0.25f;
                    y = y * y * (3f - 2f * y);
                    float e = math.abs(WorldMap.DuneField(in o, p) - y * LandSig.DuneAmp(in o, p));
                    worst = math.max(worst, e);
                    Assert.Less(e, 0.01f, $"seed {seed} at {p}: not the map's dune");
                }
                Debug.Log($"[189] seed {seed}: LandSig dune phase vs WorldMap.DuneField, worst difference {worst:G3} voxels");
            }
        }

        [Test]
        public void Oriented_ContinuousAcrossBinsAndWrap()
        {
            var off = new float2(3.1f, -7.7f);
            var rng = new Unity.Mathematics.Random(189);
            for (int t = 0; t < 50; t++)
            {
                float2 p = new float2(rng.NextFloat(1000f, 79000f), rng.NextFloat(1000f, 79000f));
                float prev = LandSig.Oriented(p, new float2(1f, 0f), 1f, 3000f, 1400f, off);
                for (int d = 1; d <= 720; d++)
                {
                    float a = math.radians(d * 0.5f);
                    float n = LandSig.Oriented(p, new float2(math.cos(a), math.sin(a)), 1f, 3000f, 1400f, off);
                    Assert.Less(math.abs(n - prev), 0.08f, $"jump at {d * 0.5f} deg");
                    prev = n;
                }
                // Orientation is mod 180 deg.
                float n0 = LandSig.Oriented(p, new float2(0.6f, 0.8f), 1f, 3000f, 1400f, off);
                float n1 = LandSig.Oriented(p, new float2(-0.6f, -0.8f), 1f, 3000f, 1400f, off);
                Assert.AreEqual(n0, n1, 1e-4f, "dir and -dir are the same orientation");
                // conf 0 is the isotropic noise, whatever the direction.
                Assert.AreEqual(LandSig.Oriented(p, new float2(1f, 0f), 0f, 3000f, 1400f, off),
                                LandSig.Oriented(p, new float2(0f, 1f), 0f, 3000f, 1400f, off), 1e-6f);
            }
        }

        /// Mean |f(p + u * d) - f(p)| along u vs across it: > 1 = elongated across u.
        private static float Anisotropy(System.Func<float2, float2, float> f, uint seed, float d)
        {
            var rng = new Unity.Mathematics.Random(seed);
            float along = 0f, across = 0f;
            for (int i = 0; i < 3000; i++)
            {
                float2 p = new float2(rng.NextFloat(5000f, 75000f), rng.NextFloat(5000f, 75000f));
                float a = rng.NextFloat(0f, 2f * math.PI);
                float2 u = new float2(math.cos(a), math.sin(a)), q = new float2(-u.y, u.x);
                float f0 = f(p, u);
                along += math.abs(f(p + u * d, u) - f0);
                across += math.abs(f(p + q * d, u) - f0);
            }
            return across / math.max(along, 1e-6f);
        }

        [Test]
        public void SlotsAndRidges_RunDownTheSlope()
        {
            foreach (uint seed in Seeds)
            {
                var o = Offsets.For(seed);
                float rr = Anisotropy((p, u) => LandSig.Ridges(in o, p, u, 1f, 1f), seed, 300f);
                float sr = Anisotropy((p, u) => LandSig.Oriented(p, u, 1f, LandSig.SLOT_LONG, LandSig.SLOT_WIDE, o.strata + new float2(29.7f, -45.1f)), seed + 1, 400f);
                Debug.Log($"[189] seed {seed}: ridge relief change across / along the slope {rr:F2}; slot noise {sr:F2}");
                Assert.Greater(rr, 1.5f, $"seed {seed}: ridges and side valleys must run down the slope");
                Assert.Greater(sr, 1.5f, $"seed {seed}: slot canyons must follow the drainage");
                // A flat has no ridges.
                Assert.AreEqual(0f, LandSig.Ridges(in o, new float2(4000f, 4000f), new float2(1f, 0f), 0f, 0.05f));
            }
        }

        [Test]
        public void Bench_TreadAndRiser_ContinuousAtLayers()
        {
            foreach (uint seed in Seeds)
            {
                var spec = TerrainSpec.SizeClass2(seed, 4); spec.landSigOn = 1;
                var o = Offsets.For(in spec);
                float2 p = new float2(30000f, 41000f);
                float prev = float.NaN; int flat = 0, total = 0;
                for (float h = 50f; h < 3000f; h += 0.5f)
                {
                    float out_ = h - LandSig.BenchCut(in spec, in o, p, h);
                    if (!float.IsNaN(prev))
                    {
                        Assert.GreaterOrEqual(out_ - prev, -1e-2f, $"seed {seed}: the bench is not monotone at h {h}");
                        Assert.Less(out_ - prev, 0.5f / LandSig.BENCH_RISER + 0.05f, $"seed {seed}: a step at h {h} (layer boundary)");
                        if (out_ - prev < 1e-3f) flat++;
                        total++;
                    }
                    prev = out_;
                }
                float share = flat / (float)total;
                Assert.AreEqual(1f - LandSig.BENCH_RISER, share, 0.03f, $"seed {seed}: tread share");
            }
        }

        [Test]
        public void Plane_ZeroOnKeepOut_PresentPerLandform()
        {
            foreach (uint seed in new[] { 42u, 7u })
            {
                var w = World(seed);
                int n = w.off.mapCells; var p = w.map.LandSigPlane; var rec = w.map.Record;
                var any = new int[3]; int keep = 0;
                for (int i = 0; i < n * n; i++)
                {
                    bool k = w.map.Height[i] <= 0f || w.map.Water[i] > Hydrology.NO_WATER * 0.5f || w.map.WaterClass[i] != Hydrology.DRY
                             || (rec.Data[i].flags & (WorldRecord.FLAG_EASY | WorldRecord.FLAG_ROUTE)) != 0;
                    for (int j = 0; j < 3; j++)
                    {
                        byte b = p[i * LandSig.PLANE_BYTES + j];
                        if (k) Assert.AreEqual(0, b, $"seed {seed}: keep-out cell {i % n},{i / n} has landform weight");
                        if (b >= 153) any[j]++;
                    }
                    if (k) keep++;
                }
                Debug.Log($"[189] seed {seed}: keep-out cells {keep}; cells with weight >= 0.6: dunes {any[0]}, canyonlands {any[1]}, alpine {any[2]}");
                for (int j = 0; j < 3; j++) Assert.Greater(any[j], 300, $"seed {seed}: no {LF[j]} cells");
            }
        }

        // ------------------------------------------------------------------
        // Adversarial
        // ------------------------------------------------------------------
        /// A one-cell landform patch on a synthetic sloping map: the relief stays
        /// inside the bilinear support, is continuous, and raises no needle.
        [Test]
        public void Adversarial_OneCellPatch()
        {
            const int n = 64; const float cv = 80f;
            var hA = new NativeArray<float>(n * n, Allocator.Temp); var rA = new NativeArray<float>(n * n, Allocator.Temp);
            var wA = new NativeArray<float>(n * n, Allocator.Temp); var cA = new NativeArray<byte>(n * n, Allocator.Temp);
            var pl = new NativeArray<byte>(n * n * LandSig.PLANE_BYTES, Allocator.Temp);
            try
            {
                for (int z = 0; z < n; z++) for (int x = 0; x < n; x++)
                { int i = z * n + x; hA[i] = 1500f + 30f * x + 6f * z; rA[i] = 0.6f; wA[i] = Hydrology.NO_WATER; }
                var spec = TerrainSpec.SizeClass2(42, 4); spec.landSigOn = 1;
                var o = Offsets.For(in spec);
                var v = new MapView
                {
                    height = (float*)hA.GetUnsafeReadOnlyPtr(), rough = (float*)rA.GetUnsafeReadOnlyPtr(),
                    water = (float*)wA.GetUnsafeReadOnlyPtr(), waterClass = (byte*)cA.GetUnsafeReadOnlyPtr(),
                    landSig = (byte*)pl.GetUnsafePtr(), cells = n, cellVoxels = cv,
                };
                float2 c = new float2(32.5f * cv, 32.5f * cv);
                // The relief's own steepest grade (dune slip face, slot wall, valley side) plus
                // its largest |dh| spread over one cell (the weight's fade across a lone cell).
                float[] ownGrade = { 1f, 25f, 2f };   // dunes, canyonlands (slot walls), alpine
                for (int k = 0; k < 3; k++)
                {
                    for (int i = 0; i < pl.Length; i++) pl[i] = 0;
                    pl[(32 * n + 32) * LandSig.PLANE_BYTES + k] = 255;
                    float worstGrad = 0f, maxUp = 0f, maxAbs = 0f;
                    for (int dz = -120; dz <= 120; dz += 2)
                    {
                        float prev = float.NaN;
                        for (int dx = -120; dx <= 120; dx++)
                        {
                            float2 p = c + new float2(dx, dz);
                            ShapeField.SampleMap(in v, p.x, p.y, out float h0, out _);
                            float dh = LandSig.Relief(in spec, in v, in o, p, h0, 1) - h0;
                            if (math.abs(dx) >= cv || math.abs(dz) >= cv) Assert.AreEqual(0f, dh, $"{LF[k]}: relief outside the patch's support at ({dx},{dz})");
                            if (!float.IsNaN(prev)) worstGrad = math.max(worstGrad, math.abs(dh - prev));
                            prev = dh; maxUp = math.max(maxUp, dh); maxAbs = math.max(maxAbs, math.abs(dh));
                        }
                    }
                    Debug.Log($"[189] ADVERSARIAL one-cell {LF[k]} patch: |dh| max {maxAbs * 0.1f:F1} m, raise max {maxUp * 0.1f:F1} m, worst step per voxel {worstGrad:F2}");
                    Assert.Greater(maxAbs, 1f, $"{LF[k]}: a one-cell patch still shows (scaled by its weight)");
                    Assert.LessOrEqual(maxUp, StampTests.NEEDLE_DROP, $"{LF[k]}: a one-cell patch must not raise a needle");
                    Assert.LessOrEqual(worstGrad, ownGrade[k] + maxAbs / cv, $"{LF[k]}: a discontinuity at the patch");
                }
            }
            finally { hA.Dispose(); rA.Dispose(); wA.Dispose(); cA.Dispose(); pl.Dispose(); }
        }

        /// Landform columns across a BIOME border: the relief does not read the
        /// biome, so it is on both sides and there is no step at the border.
        [Test]
        [Category("Slow")]
        public void Adversarial_BiomeBorder()
        {
            int found = 0;
            var lines = new List<string>();
            foreach (uint seed in Seeds)
            {
                var w = World(seed);
                int n = w.off.mapCells; var rec = w.map.Record; var pl = w.map.LandSigPlane;
                for (int k = 0; k < 3; k++)
                {
                    int got = 0;
                    for (int z = 16; z < n - 16 && got < 2; z += 3)
                    for (int x = 16; x < n - 16 && got < 2; x += 3)
                    {
                        int i = z * n + x;
                        if (pl[i * LandSig.PLANE_BYTES + k] < 180 || rec.Data[i].Biome == rec.Data[i + 1].Biome || pl[(i + 1) * LandSig.PLANE_BYTES + k] < 180) continue;
                        got++; found++;
                        // A 120 m line across the border (x), 1 voxel steps.
                        float x0 = (x + 1) * w.off.mapCellVoxels, z0 = (z + 0.5f) * w.off.mapCellVoxels;
                        float leftMax = 0f, rightMax = 0f, borderStep = 0f, elseStep = 0f, offBorder = 0f, prevOn = float.NaN, prevOff = float.NaN;
                        // Relief on each side: |ON - OFF| over a 52 x 60 m patch (a single
                        // line can run along one dune crest).
                        for (int e = -300; e <= 300; e += 150)
                        for (int d = -600; d <= 600; d += 20)
                        {
                            if (math.abs(d) <= 80) continue;
                            float dh = math.abs(H(in w.on, in w.view, in w.o, x0 + d, z0 + e) - H(in w.off, in w.view, in w.o, x0 + d, z0 + e));
                            if (d < 0) leftMax = math.max(leftMax, dh); else rightMax = math.max(rightMax, dh);
                        }
                        for (int d = -600; d <= 600; d++)
                        {
                            float on = H(in w.on, in w.view, in w.o, x0 + d, z0), off = H(in w.off, in w.view, in w.o, x0 + d, z0);
                            if (!float.IsNaN(prevOn))
                            {
                                float s = math.abs(on - prevOn);
                                if (math.abs(d) <= 80) { borderStep = math.max(borderStep, s); offBorder = math.max(offBorder, math.abs(off - prevOff)); }
                                else elseStep = math.max(elseStep, s);
                            }
                            prevOn = on; prevOff = off;
                        }
                        lines.Add($"seed {seed} {LF[k]} border at cell ({x},{z}) biome {rec.Data[i].Biome}|{rec.Data[i + 1].Biome}: relief max left {leftMax * 0.1f:F1} m right {rightMax * 0.1f:F1} m; step near border {borderStep:F0} vx (OFF {offBorder:F0}), elsewhere {elseStep:F0} vx");
                        Assert.LessOrEqual(borderStep, math.max(offBorder, elseStep) + 2f, $"seed {seed} {LF[k]} border ({x},{z}): a step at the biome border");
                        Assert.Greater(leftMax, 5f, $"seed {seed} {LF[k]} border ({x},{z}): no relief on the left biome");
                        Assert.Greater(rightMax, 5f, $"seed {seed} {LF[k]} border ({x},{z}): no relief on the right biome");
                    }
                }
            }
            Directory.CreateDirectory(OUT_DIR);
            File.WriteAllLines(Path.Combine(OUT_DIR, "border189.txt"), lines);
            foreach (var l in lines) Debug.Log("[189] " + l);
            Assert.Greater(found, 6, "too few landform cells at a biome border on four seeds");
        }

        // ------------------------------------------------------------------
        // Guards
        // ------------------------------------------------------------------
        [Test]
        [Category("Slow")]
        public void BandLimit_WithLandSig()
        {
            int[] texels = { 2, 4, 8, 16, 32, 64, 128 };
            float worst = 0f; int cols = 0;
            foreach (uint seed in new[] { 42u, 2026u })
            {
                var w = World(seed);
                for (int k = 0; k < 3; k++)
                    foreach (var c in Cells(in w, k, 64, 100, 0xb189u + (uint)k))
                    {
                        float2 p = Centre(in w, c) + new float2(17f, -23f);
                        if (Stamps.StampAt(in w.o, p, out var stp) &&
                            (stp.kind == Stamps.Arch || stp.kind == Stamps.Hoop || stp.kind == Stamps.StoneRing || stp.kind == Stamps.Fossil) &&
                            math.distance(stp.pos, p) < 1100f) continue;
                        var fine = ShapeField.Sample(in w.on, in w.view, in w.o, p.x, p.y, 1, false);
                        foreach (int t in texels)
                        {
                            var coarse = ShapeField.Sample(in w.on, in w.view, in w.o, p.x, p.y, t, false);
                            float bound = ShapeField.BandLimitBound(in w.on, t, fine.rough) + 1f;
                            float d = math.abs(coarse.height - fine.height);
                            worst = math.max(worst, d / bound);
                            Assert.LessOrEqual(d, bound, $"seed {seed} {LF[k]} texel {t} at {p}: |{coarse.height} - {fine.height}| > {bound}");
                        }
                        cols++;
                    }
            }
            Debug.Log($"[189] band limit with LandSig: {cols} columns, worst used {worst:P0} of the bound");
            Assert.Greater(cols, 400);
        }

        /// StampTests.NoNeedles_OnTheMap's criterion on the COLUMNS (the map
        /// does not change): at every 8 m local maximum of a 48 x 48-cell window
        /// in each landform, the highest column 40 m away within NEEDLE_DROP.
        /// LandSig may not add a needle: ON's count <= OFF's at the same windows.
        [Test]
        [Category("Slow")]
        public void NoNeedles_LandSigColumns()
        {
            var lines = new List<string>();
            int totOn = 0, totOff = 0;
            foreach (uint seed in Seeds)
            {
                var w = World(seed);
                for (int k = 0; k < 3; k++)
                {
                    int nOn = 0, nOff = 0; float worstOn = 0f, worstOff = 0f;
                    foreach (var c in Cells(in w, k, 153, 3, 0x7ee0u + (uint)k + seed))
                    {
                        const int G = 48;
                        var hOn = new float[G * G]; var hOff = new float[G * G];
                        for (int j = 0; j < G; j++)
                        for (int i = 0; i < G; i++)
                        {
                            float x = (c.x - G / 2 + i + 0.5f) * w.off.mapCellVoxels, z = (c.y - G / 2 + j + 0.5f) * w.off.mapCellVoxels;
                            hOn[j * G + i] = H(in w.on, in w.view, in w.o, x, z);
                            hOff[j * G + i] = H(in w.off, in w.view, in w.o, x, z);
                        }
                        Needles(hOn, G, ref nOn, ref worstOn); Needles(hOff, G, ref nOff, ref worstOff);
                    }
                    lines.Add($"seed {seed} {LF[k]}: needles ON {nOn} (worst drop {worstOn * 0.1f:F1} m), OFF {nOff} (worst {worstOff * 0.1f:F1} m)");
                    totOn += nOn; totOff += nOff;
                    Assert.LessOrEqual(nOn, nOff, $"seed {seed} {LF[k]}: LandSig adds a needle");
                }
            }
            Directory.CreateDirectory(OUT_DIR);
            File.WriteAllLines(Path.Combine(OUT_DIR, "needles189.txt"), lines);
            foreach (var l in lines) Debug.Log("[189] " + l);
        }

        private static void Needles(float[] h, int G, ref int count, ref float worst)
        {
            for (int z = 5; z < G - 5; z++)
            for (int x = 5; x < G - 5; x++)
            {
                float c = h[z * G + x];
                bool isMax = true;
                for (int j = -1; j <= 1 && isMax; j++)
                for (int i = -1; i <= 1 && isMax; i++)
                    if ((i != 0 || j != 0) && h[(z + j) * G + x + i] > c) isMax = false;
                if (!isMax) continue;
                float ring = float.MinValue;
                for (int k = -5; k <= 5; k++)
                {
                    ring = math.max(ring, math.max(h[(z - 5) * G + x + k], h[(z + 5) * G + x + k]));
                    ring = math.max(ring, math.max(h[(z + k) * G + x - 5], h[(z + k) * G + x + 5]));
                }
                worst = math.max(worst, c - ring);
                if (c - ring > StampTests.NEEDLE_DROP) count++;
            }
        }

        // ------------------------------------------------------------------
        // The shape statistic, 4 seeds, OFF vs ON
        // ------------------------------------------------------------------
        private const int TN = 200;        // samples per transect
        private const float TSTEP = 20f;   // 2 m: a 400 m transect

        private static float[] Transect(in TerrainSpec spec, in W w, float2 c, float2 u)
        {
            var h = new float[TN];
            for (int i = 0; i < TN; i++)
            {
                float2 p = c + u * ((i - TN / 2) * TSTEP);
                h[i] = H(in spec, in w.view, in w.o, p.x, p.y) * 0.1f;   // metres
            }
            return h;
        }

        /// The 20-200 m band at the transect's middle 200 m (metres): an 11-sample
        /// (20 m) mean minus a 100-sample (200 m) mean.
        private static float[] BandSignal(float[] h)
        {
            var b = new float[100];
            for (int i = 50; i < 150; i++)
            {
                float lo = 0f; for (int j = -5; j <= 5; j++) lo += h[i + j]; lo /= 11f;
                float hi = 0f; for (int j = -50; j < 50; j++) hi += h[i + j]; hi /= 100f;
                b[i - 50] = lo - hi;
            }
            return b;
        }

        private static float Rms(float[] b) { double s = 0; foreach (float v in b) s += v * v; return (float)math.sqrt(s / b.Length); }

        private static float P(List<float> v, float q) { if (v.Count == 0) return 0f; v.Sort(); return v[math.min(v.Count - 1, (int)(v.Count * q))]; }
        private static float Median(List<float> v) => P(v, 0.5f);

        /// Slot canyons: local minima >= 3 m under both sides 10 m away.
        private static int Slots(float[] h)
        {
            int n = 0;
            for (int i = 5; i < TN - 5; i++)
                if (h[i] <= h[i - 1] && h[i] < h[i + 1] && h[i] < math.min(h[i - 5], h[i + 5]) - 3f) n++;
            return n;
        }

        /// Ridges on the band signal: a maximum over +-10 m standing >= 5 m over the lowest point within +-40 m.
        private static int Ridges(float[] b)
        {
            int n = 0;
            for (int i = 20; i < b.Length - 20; i++)
            {
                bool max = true;
                for (int j = -5; j <= 5 && max; j++) if (j != 0 && b[i + j] > b[i]) max = false;
                if (!max) continue;
                float lo = float.MaxValue; for (int j = -20; j <= 20; j++) lo = math.min(lo, b[i + j]);
                if (b[i] - lo >= 5f) n++;
            }
            return n;
        }

        private struct Stat { public float band, sig, ratio, sigRatio, flat, steep, slotsKm, ridgesKm; public int n; }

        private class Acc
        {
            public List<float> bands = new List<float>(), lee = new List<float>(), stoss = new List<float>();
            public int flat, steep, all, slots, ridges, n; public double sig;
            public List<float> dLee = new List<float>(), dStoss = new List<float>();
            public void Add(float[] h, float[] b)
            {
                bands.Add(Rms(b)); n++;
                for (int i = 0; i + 3 < TN; i++)
                {
                    float s = (h[i + 3] - h[i]) / 6f;   // 6 m slopes
                    all++;
                    if (math.abs(s) < 0.1f) flat++;
                    if (math.abs(s) > 1f) steep++;
                }
                float trend = (h[149] - h[50]) / 198f;   // m per m over the middle 200 m
                for (int i = 50; i < 145; i++)
                {
                    float s = (h[i + 5] - h[i]) / 10f - trend;   // 10 m slopes, detrended
                    if (s < 0f) lee.Add(-s); else stoss.Add(s);
                }
                slots += Slots(h); ridges += Ridges(b);
            }
            public Stat Done() => new Stat
            {
                band = Median(bands), sig = n > 0 ? (float)math.sqrt(sig / n) : 0f,
                ratio = P(lee, 0.9f) / math.max(P(stoss, 0.9f), 1e-3f),
                sigRatio = P(dLee, 0.9f) / math.max(P(dStoss, 0.9f), 1e-3f),
                flat = flat / (float)math.max(all, 1), steep = steep / (float)math.max(all, 1),
                slotsKm = slots / math.max(n * 0.4f, 1e-3f), ridgesKm = ridges / math.max(n * 0.2f, 1e-3f), n = n,
            };
        }

        /// Transects along the wind (dunes) or across the regional downslope
        /// (canyonlands, alpine), OFF and ON at the same places.
        private static void Measure(in W w, int k, List<int2> cells, out Stat off, out Stat on)
        {
            var a = new Acc(); var b = new Acc();
            float2 wind = LandSig.Wind(in w.o);
            foreach (var cell in cells)
            {
                float2 c = Centre(in w, cell);
                LandSig.Orientation(in w.view, c, out float2 dir, out _, out _);
                float2 u = k == 0 ? wind : new float2(-dir.y, dir.x);
                var h0 = Transect(in w.off, in w, c, u); var h1 = Transect(in w.on, in w, c, u);
                var b0 = BandSignal(h0); var b1 = BandSignal(h1);
                a.Add(h0, b0); b.Add(h1, b1);
                double d = 0; for (int i = 0; i < b0.Length; i++) d += (b1[i] - b0[i]) * (b1[i] - b0[i]);
                b.sig += d / b0.Length;
                for (int i = 50; i < 145; i++)
                {
                    float sd = ((h1[i + 5] - h0[i + 5]) - (h1[i] - h0[i])) / 10f;   // the signature's own 10 m slope
                    if (sd < 0f) b.dLee.Add(-sd); else if (sd > 0f) b.dStoss.Add(sd);
                }
            }
            off = a.Done(); on = b.Done();
        }

        [Test]
        [Category("Slow")]
        public void ShapeStatistic_PerLandform_FourSeeds()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Task 189 (C6) shape statistic per landform, OFF (-landsig 0) vs ON (-landsig 1). Map v4, up to 40 transects of 400 m per landform per seed,");
            sb.AppendLine("centred on cells with plane weight >= 0.6 (away from water, easy areas and routes), the SAME places in both arms. Column heights, texel 1, 2 m steps.");
            sb.AppendLine("band    = median RMS of the 20-200 m band (20 m mean minus 200 m mean) over the middle 200 m, metres");
            sb.AppendLine("sig     = RMS of the band's change ON - OFF (the signature itself), metres");
            sb.AppendLine("lee/stoss = p90 of the downwind-facing 10 m slope / p90 of the upwind-facing one, minus the transect's 200 m trend (dunes: transect ALONG the wind)");
            sb.AppendLine("flat / steep = share of 6 m slopes < 0.1 / > 1.0 (45 deg); slots/km = minima >= 3 m under both sides 10 m away; ridges/km = band maxima >= 5 m over +-40 m");
            sb.AppendLine("canyonlands and alpine transects run ACROSS the regional downslope (slots, ridges and side valleys run down it)");
            sb.AppendLine();
            sb.AppendLine("sig l/s = lee/stoss of the signature alone (ON - OFF heights)");
            sb.AppendLine();
            sb.AppendLine("seed   landform     arm  cells  band m  sig m  lee/stoss  sig l/s  flat  steep  slots/km  ridges/km");
            var sum = new Dictionary<string, List<Stat>>();
            var fails = new List<string>();
            foreach (uint seed in Seeds)
            {
                var w = World(seed);
                for (int k = 0; k < 3; k++)
                {
                    var cells = Cells(in w, k, 153, 40, 0x5a9eu + (uint)k * 31u + seed);
                    if (cells.Count < 10) { sb.AppendLine($"{seed,-6} {LF[k],-12} --   {cells.Count,5}  (too few cells at weight >= 0.6 on this seed)"); continue; }
                    Measure(in w, k, cells, out var off, out var on);
                    foreach (var (arm, st) in new[] { ("OFF", off), ("ON", on) })
                    {
                        sb.AppendLine($"{seed,-6} {LF[k],-12} {arm,-4} {st.n,5}  {st.band,6:F2}  {st.sig,5:F2}  {st.ratio,9:F2}  {st.sigRatio,7:F2}  {st.flat,4:P0}  {st.steep,5:P0}  {st.slotsKm,8:F1}  {st.ridgesKm,9:F1}");
                        string key = LF[k] + "|" + arm;
                        if (!sum.ContainsKey(key)) sum[key] = new List<Stat>();
                        sum[key].Add(st);
                    }
                    // Per rule, every seed that has the landform:
                    void Need(bool ok, string what) { if (!ok) fails.Add($"seed {seed} {LF[k]}: {what}"); }
                    Need(on.sig > 1f, "the signature adds < 1 m RMS in the 20-200 m band");
                    if (k == 0) Need(on.ratio > math.max(1.3f, off.ratio), "lee / stoss");
                    if (k == 1) { Need(on.flat > off.flat, "flat share (benches)"); Need(on.steep > off.steep, "steep share (risers, slot walls)"); Need(on.slotsKm > off.slotsKm, "slots"); }
                    if (k == 2) { Need(on.band > off.band, "20-200 m relief across the slope"); Need(on.ridgesKm > off.ridgesKm, "ridges across the slope"); }
                }
            }
            sb.AppendLine();
            sb.AppendLine("MEAN of the seeds              band m  sig m  lee/stoss  sig l/s  flat  steep  slots/km  ridges/km");
            foreach (var kv in sum)
            {
                var l = kv.Value;
                sb.AppendLine($"{kv.Key,-30} {l.Average(q => q.band),6:F2}  {l.Average(q => q.sig),5:F2}  {l.Average(q => q.ratio),9:F2}  {l.Average(q => q.sigRatio),7:F2}  {l.Average(q => q.flat),4:P0}  {l.Average(q => q.steep),5:P0}  {l.Average(q => q.slotsKm),8:F1}  {l.Average(q => q.ridgesKm),9:F1}");
            }
            Directory.CreateDirectory(OUT_DIR);
            File.WriteAllText(Path.Combine(OUT_DIR, "shape189.txt"), sb.ToString());
            Debug.Log("[189] shape statistic\n" + sb);
            Assert.IsEmpty(fails, string.Join("; ", fails));
        }
    }
}
