// Assets/TerrainGen/Tests/BiomeSizeDump.cs
//
// Task 176b tooling, report only (REPORTS=1): candidate biome REGION SIZES on
// the real generator. For each seed it builds the real map (SharedMaps), takes
// its land mask and height shade, and writes biome planes for:
//   b10    TerrainSpec.biomeVaryOff = 1 (B10's belts), via ShapeField.Biome
//   c176   the map's own record plane (task 176, CLIMATE_WAVE_R 0.75)
//   <var>  each C1SIZES variant: climate wavelength k x islandRadius and Fbm
//          octave amplitudes, normalised over the island disc exactly as
//          ShapeField.ClimateNormalise does, classified by nearest centre as
//          Biome6 does.
// The variant path is CHECKED against the generator: Climate(q, live octaves)
// must equal ShapeField.Climate3 (to 1e-5; Mono rounds a + c * x per call site), and the 0.75 / live-octaves
// variant (with the map's Burst climMean/climGain) must reproduce the record
// plane (mismatching cells are counted and printed; Burst vs Mono float code
// differs in ~1e-4 of the cells, KNOWN_ISSUES; the gate is 1e-3). No generator code changes:
// this only reads ShapeField.
// Output: Logs/TerrainGen/c1/<C1TAG or "sizes">/<plane>/seed<S>_{land,biome,height}.bin + meta.txt,
// the format tools/biome176.py reads.
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Unity.Mathematics;

namespace TerrainGen.Tests
{
    public class BiomeSizeDump
    {
        static readonly uint[] Seeds = { 42u, 7u, 2026u, 31337u };

        // name:k:amp2,amp3,... (amp1 = 1). Default = the four 176b sizes.
        const string DefaultSizes = "w100:1.0:0.35,0.2;w150:1.5:0.35,0.2,0.12;w200:2.0:0.35,0.2;w350:3.5:0.35,0.2";

        struct Variant { public string name; public float k; public float[] amps; }

        static List<Variant> Parse(string s)
        {
            var list = new List<Variant>();
            foreach (var part in s.Split(';'))
            {
                if (part.Trim().Length == 0) continue;
                var f = part.Split(':');
                var amps = new List<float> { 1f };
                foreach (var a in f[2].Split(',')) if (a.Trim().Length > 0) amps.Add(float.Parse(a, System.Globalization.CultureInfo.InvariantCulture));
                list.Add(new Variant { name = f[0], k = float.Parse(f[1], System.Globalization.CultureInfo.InvariantCulture), amps = amps.ToArray() });
            }
            return list;
        }

        /// ShapeField.Climate3's chain with N octaves: same lacunarity, offsets and summation order.
        static float Climate(float2 q, float[] amps)
        {
            float a = noise.snoise(q);
            for (int i = 1; i < amps.Length; i++)
            {
                q = q * 2.03f + new float2(17.3f, -9.1f);
                a += amps[i] * noise.snoise(q);
            }
            return a;
        }

        /// Biome6's nearest-centre rule (same centres, same comparison order).
        static byte Nearest6(float t, float m)
        {
            float df = t * t + m * m;
            float dd = (t - 0.8f) * (t - 0.8f) + (m + 0.8f) * (m + 0.8f);
            float ds = (t + 0.9f) * (t + 0.9f) + (m - 0.1f) * (m - 0.1f);
            float dj = (t - 0.8f) * (t - 0.8f) + (m - 0.8f) * (m - 0.8f);
            float dv = (t - 0.8f) * (t - 0.8f) + m * m;
            float db = (t + 0.3f) * (t + 0.3f) + (m + 0.8f) * (m + 0.8f);
            byte b = GenBiome.Forest; float best = df;
            if (dd < best) { best = dd; b = GenBiome.Desert; }
            if (ds < best) { best = ds; b = GenBiome.Snow; }
            if (dj < best) { best = dj; b = GenBiome.Jungle; }
            if (dv < best) { best = dv; b = GenBiome.Savanna; }
            if (db < best) { b = GenBiome.Badlands; }
            return b;
        }

        /// ShapeField.ClimateNormalise for a variant (wavelengths, mean, gain).
        static void Normalise(in TerrainSpec spec, in Offsets o, float k, float[] amps, out float2 wave, out float2 mean, out float2 gain)
        {
            float wm = k * spec.islandRadius;
            wave = new float2(wm * ShapeField.CLIMATE_TEMP_X, wm);
            float c = spec.worldSizeVoxels * 0.5f, r = spec.islandRadius;
            int G = ShapeField.CLIMATE_GRID;
            double st = 0, stt = 0, sm = 0, smm = 0; int n = 0;
            for (int j = 0; j < G; j++)
            for (int i = 0; i < G; i++)
            {
                float2 d = new float2((i + 0.5f) / G * 2f - 1f, (j + 0.5f) / G * 2f - 1f);
                if (d.x * d.x + d.y * d.y > 1f) continue;
                float2 p = new float2(c, c) + d * r;
                float t = Climate(p / wave.x + o.tempA, amps), m = Climate(p / wave.y + o.moistA, amps);
                st += t; stt += (double)t * t; sm += m; smm += (double)m * m; n++;
            }
            double mt = st / n, mm = sm / n;
            double sdt = math.sqrt(math.max(1e-12, stt / n - mt * mt)), sdm = math.sqrt(math.max(1e-12, smm / n - mm * mm));
            mean = new float2((float)mt, (float)mm);
            gain = new float2((float)(ShapeField.CLIMATE_SD / sdt), (float)(ShapeField.CLIMATE_SD / sdm));
        }

        static byte[] Plane(in TerrainSpec spec, in Offsets o, float[] amps, float2 wave, float2 mean, float2 gain)
        {
            int n = spec.mapCells;
            var b = new byte[n * n];
            for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                var p = new float2((x + 0.5f) * spec.mapCellVoxels, (z + 0.5f) * spec.mapCellVoxels);
                float t = (Climate(p / wave.x + o.tempA, amps) - mean.x) * gain.x;
                float m = (Climate(p / wave.y + o.moistA, amps) - mean.y) * gain.y;
                b[z * n + x] = Nearest6(t, m);
            }
            return b;
        }

        static void Write(string root, string plane, uint seed, byte[] land, byte[] biome, byte[] shade, Dictionary<string, System.Text.StringBuilder> metas, string metaLine)
        {
            string d = Path.Combine(root, plane);
            Directory.CreateDirectory(d);
            File.WriteAllBytes(Path.Combine(d, $"seed{seed}_land.bin"), land);
            File.WriteAllBytes(Path.Combine(d, $"seed{seed}_biome.bin"), biome);
            File.WriteAllBytes(Path.Combine(d, $"seed{seed}_height.bin"), shade);
            if (!metas.TryGetValue(plane, out var sb)) metas[plane] = sb = new System.Text.StringBuilder();
            sb.AppendLine(metaLine);
        }

        [Test]
        [Category("Slow")]
        public void Report_DumpBiomeSizes()
        {
            ReportOnly.Require();
            string tag = System.Environment.GetEnvironmentVariable("C1TAG") ?? "sizes";
            string root = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "TerrainGen", "c1", tag);
            Directory.CreateDirectory(root);
            var variants = Parse(System.Environment.GetEnvironmentVariable("C1SIZES") ?? DefaultSizes);
            var live = new[] { 1f, ShapeField.CLIMATE_OCT2, ShapeField.CLIMATE_OCT3 };
            var metas = new Dictionary<string, System.Text.StringBuilder>();
            var log = new System.Text.StringBuilder();

            // 1. the octave chain equals Climate3 bit for bit
            //    (to float rounding: Mono's JIT may contract a + c * x differently for a const c; measured 1 ulp)
            var rnd = new Unity.Mathematics.Random(176u);
            float maxDiff = 0f; int exact = 0;
            for (int i = 0; i < 20000; i++)
            {
                float2 q = rnd.NextFloat2(-2000f, 2000f);
                float d = math.abs(ShapeField.Climate3(q) - Climate(q, live));
                maxDiff = math.max(maxDiff, d); if (d == 0f) exact++;
            }
            log.AppendLine($"[176b] Climate vs ShapeField.Climate3 on 20000 points: {exact} bit-equal, max |diff| {maxDiff:E2}");
            Assert.Less(maxDiff, 1e-5f, "the octave chain is not ShapeField.Climate3's");

            foreach (uint seed in Seeds)
            {
                var spec = TerrainSpec.SizeClass2(seed);
                Assert.IsTrue(spec.BiomeVary, "SizeClass2 is expected to be a BiomeVary spec");
                var map = SharedMaps.Get(spec);
                int n = spec.mapCells;
                var land = new byte[n * n]; var rec = new byte[n * n]; var shade = new byte[n * n];
                for (int i = 0; i < n * n; i++)
                {
                    float h = map.Height[i];
                    land[i] = (byte)(h > 0f ? (map.WaterClass[i] == Hydrology.LAKE || map.WaterClass[i] == Hydrology.RIVER ? 2 : 1) : 0);
                    rec[i] = map.Record.Data[i].Biome;
                    shade[i] = (byte)UnityEngine.Mathf.Clamp(h / 24f, 0f, 255f);
                }
                var o = Offsets.For(in spec);
                string metaLine = $"{seed} {o.tempA.x:R} {o.tempA.y:R} {o.moistA.x:R} {o.moistA.y:R} {o.biomeScale:R} {spec.islandRadius:R} {spec.mapCellVoxels} {n} {spec.worldSizeVoxels}";

                // b10: the same spec with the varied climate off, through the generator's own Biome
                var offSpec = spec; offSpec.biomeVaryOff = 1;
                var oOff = Offsets.For(in offSpec);
                var b10 = new byte[n * n];
                for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                    b10[z * n + x] = ShapeField.Biome(in oOff, new float2((x + 0.5f) * spec.mapCellVoxels, (z + 0.5f) * spec.mapCellVoxels));
                Write(root, "b10", seed, land, b10, shade, metas, metaLine);
                Write(root, "c176", seed, land, rec, shade, metas, metaLine);

                // 2. the variant path reproduces the record plane (0.75, live octaves, the map's Burst normalisation)
                var chk = Plane(in spec, in o, live, o.climWave, o.climMean, o.climGain);
                int bad = 0; for (int i = 0; i < n * n; i++) if (chk[i] != rec[i]) bad++;
                Normalise(in spec, in o, ShapeField.CLIMATE_WAVE_R, live, out var w75, out var mu75, out var g75);
                log.AppendLine($"[176b] seed {seed}: variant path vs record plane: {bad} of {n * n} cells differ; " +
                               $"Mono normalisation vs Burst: mean {mu75 - o.climMean} gain {g75 - o.climGain} wave {w75 - o.climWave}");
                Assert.LessOrEqual(bad, n * n / 1000, $"seed {seed}: the variant path does not reproduce the generator ({bad} cells)");

                foreach (var v in variants)
                {
                    Normalise(in spec, in o, v.k, v.amps, out var w, out var mu, out var g);
                    Write(root, v.name, seed, land, Plane(in spec, in o, v.amps, w, mu, g), shade, metas, metaLine);
                }
            }
            foreach (var kv in metas) File.WriteAllText(Path.Combine(root, kv.Key, "meta.txt"), kv.Value.ToString());
            File.WriteAllText(Path.Combine(root, "check.txt"), log.ToString());
            UnityEngine.Debug.Log(log.ToString() + $"[176b] dumped {Seeds.Length} seeds x {variants.Count + 2} planes to {root}");
        }
    }
}
