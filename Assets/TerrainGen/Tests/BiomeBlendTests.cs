// Assets/TerrainGen/Tests/BiomeBlendTests.cs
//
// Task 122 (VISUAL_REVIEW_114 #5, owner Q6 (a)): the biome BORDER BLEND
// (ShapeField.SurfaceBiome). A column's surface and soil follow a warped,
// dithered copy of the biome field; the biome itself (rock, ores, liquids, the
// world record) does not move. These tests pin:
//   - the early-out is exact (a column far from every bisector cannot change),
//     resting on SNOISE_GRAD_BOUND, which is measured here;
//   - the record biome is the same bits with and without the border distance;
//   - the band's shape along E-W transects: several switches per band (mixed,
//     not a moved line), a width of tens of metres, and a line that is bent
//     away from the straight climate contour;
//   - blend off (Offsets.biomeBlend 0, -biomeblend 0, map v3) = the old surface,
//     and the same seed gives the same surface.
// Report_BiomeBlendMapPng (REPORTS=1) writes the before/after map renders.
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace TerrainGen.Tests
{
    public class BiomeBlendTests
    {
        private const uint SEED = 42;
        private static TerrainSpec _spec;
        private static Offsets _o;
        private static WorldMap _map;

        [OneTimeSetUp]
        public void BuildOnce()
        {
            if (_map != null) return;
            _spec = TerrainSpec.SizeClass2(SEED);
            _o = Offsets.For(in _spec);
            _map = SharedMaps.Get(_spec);
        }

        private static byte Surf(in Offsets o, float2 p)
        {
            byte b = ShapeField.BiomeAndBorder(in o, p, true, out float border);
            return ShapeField.SurfaceBiome(in o, p, b, border);
        }

        [Test]
        public void SnoiseGradient_WithinTheEarlyOutBound()
        {
            var rng = new Unity.Mathematics.Random(0x51ee7u);
            const float e = 2e-3f;
            float worst = 0f;
            for (int i = 0; i < 400000; i++)
            {
                float2 p = rng.NextFloat2(-40f, 40f);
                float gx = (noise.snoise(p + new float2(e, 0f)) - noise.snoise(p - new float2(e, 0f))) / (2f * e);
                float gz = (noise.snoise(p + new float2(0f, e)) - noise.snoise(p - new float2(0f, e))) / (2f * e);
                worst = math.max(worst, math.length(new float2(gx, gz)));
            }
            Debug.Log($"[BiomeBlend] max |grad snoise| {worst:F2} over 400k points (bound {ShapeField.SNOISE_GRAD_BOUND})");
            Assert.Less(worst, ShapeField.SNOISE_GRAD_BOUND);
        }

        [Test]
        public void EarlyOut_IsExact_AndTheRecordBiomeIsUnchanged()
        {
            Assert.AreEqual(1, _o.biomeBlend, "v4 default: blend on");
            float reach = ShapeField.BlendClimateReach(in _o);
            var rng = new Unity.Mathematics.Random(0xb1e9du);
            int inBand = 0, changed = 0;
            const int N = 300000;
            for (int i = 0; i < N; i++)
            {
                float2 p = rng.NextFloat2(0f, _spec.worldSizeVoxels);
                byte b = ShapeField.BiomeAndBorder(in _o, p, true, out float border);
                Assert.AreEqual(ShapeField.Biome(in _o, p), b, "same biome with and without the border distance");
                byte j = ShapeField.Biome(in _o, p + ShapeField.BlendOffset(in _o, p));
                if (border > reach) Assert.AreEqual(b, j, $"early-out not exact at {p} (border {border}, reach {reach})");
                else inBand++;
                if (j != b) changed++;
            }
            Debug.Log($"[BiomeBlend] climate reach {reach:F3}: {100.0 * inBand / N:F1}% of columns take the full path, " +
                      $"{100.0 * changed / N:F2}% wear the other side's surface");
            Assert.Greater(changed, 0);
            Assert.Less(changed, N / 20, "the blend is a border band, not a repaint");
        }

        [Test]
        public void Band_IsMixed_TensOfMetresWide_AndNotStraight()
        {
            // E-W transects at one sample per dither cell (8 voxels).
            const int ROWS = 64, STEP = 8;
            int n = _spec.worldSizeVoxels / STEP;
            var widths = new List<float>(); var switches = new List<int>(); var offsets = new List<float>();
            var rec = new byte[n]; var sur = new byte[n];
            for (int r = 0; r < ROWS; r++)
            {
                float z = (r + 0.5f) * _spec.worldSizeVoxels / ROWS + 0.5f;
                for (int i = 0; i < n; i++)
                {
                    float2 p = new float2(i * STEP + 0.5f, z);
                    byte b = ShapeField.BiomeAndBorder(in _o, p, true, out float border);
                    rec[i] = b; sur[i] = ShapeField.SurfaceBiome(in _o, p, b, border);
                }
                // Group surface switches closer than 30 m into one band.
                int first = -1, last = -1, count = 0;
                void Close()
                {
                    if (count == 0) return;
                    widths.Add((last - first) * STEP * 0.1f); switches.Add(count);
                    float centre = (first + last) * 0.5f; float best = float.MaxValue;
                    for (int k = math.max(1, first - 200); k < math.min(n, last + 200); k++)
                        if (rec[k] != rec[k - 1] && math.abs(k - centre) < math.abs(best)) best = k - centre;
                    if (best != float.MaxValue) offsets.Add(best * STEP * 0.1f);
                    count = 0;
                }
                for (int i = 1; i < n; i++)
                {
                    if (sur[i] == sur[i - 1]) continue;
                    if (count > 0 && (i - last) * STEP > 300) Close();
                    if (count == 0) first = i;
                    last = i; count++;
                }
                Close();
            }
            widths.Sort(); switches.Sort();
            float sd = 0f, mean = 0f;
            foreach (float f in offsets) mean += f;
            mean /= math.max(1, offsets.Count);
            foreach (float f in offsets) sd += (f - mean) * (f - mean);
            sd = math.sqrt(sd / math.max(1, offsets.Count));
            float wMed = widths[widths.Count / 2], wP90 = widths[widths.Count * 9 / 10];
            int sMed = switches[switches.Count / 2];
            string line = $"[BiomeBlend] {widths.Count} border bands on {ROWS} E-W transects: width median {wMed:F0} m, p90 {wP90:F0} m; " +
                          $"surface switches per band median {sMed}; band centre vs the climate contour: sd {sd:F0} m";
            Debug.Log(line);
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "TerrainGen");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "biome_blend.txt"), line + "\n");
            Assert.Greater(widths.Count, 20, "seed 42 has biome borders");
            Assert.GreaterOrEqual(sMed, 3, "a band mixes both sides (not one moved line)");
            Assert.That(wMed, Is.InRange(10f, 80f), "a few tens of metres");
            Assert.Greater(sd, 8f, "the line is bent off the straight climate contour");
        }

        [Test]
        public void Off_GivesTheOldSurface_AndTheSameSeedTheSameSurface()
        {
            var off = _spec; off.biomeBlendOff = 1;
            Assert.IsFalse(off.BiomeBlend);
            Assert.AreEqual(0, Offsets.For(in off).biomeBlend);
            Assert.IsFalse(TerrainSpec.SizeClass2(SEED, 3).BiomeBlend, "map v3 keeps its bytes");
            var o0 = _o; o0.biomeBlend = 0;
            var o2 = Offsets.For(in _spec);
            var view = _map.View;
            var rng = new Unity.Mathematics.Random(0x7a11u);
            int differ = 0;
            for (int i = 0; i < 3000; i++)
            {
                float2 p = rng.NextFloat2(0f, _spec.worldSizeVoxels);
                var a = ShapeField.Sample(in _spec, in view, in _o, p.x, p.y, 1);
                var b = ShapeField.Sample(in _spec, in view, in o2, p.x, p.y, 1);
                var c = ShapeField.Sample(in _spec, in view, in o0, p.x, p.y, 1);
                var h = ShapeField.Sample(in _spec, in view, in _o, p.x, p.y, 1, false);
                Assert.AreEqual(a.surfBiome, b.surfBiome, "same seed, same surface");
                Assert.AreEqual(c.biome, c.surfBiome, "blend off: the surface is the biome");
                Assert.AreEqual(a.biome, c.biome, "the blend never moves the biome");
                Assert.AreEqual(a.height, h.height, "height-only samples skip the blend, same height");
                Assert.AreEqual(Surf(in _o, p), a.surfBiome);
                if (a.surfBiome != a.biome) differ++;
            }
            Debug.Log($"[BiomeBlend] {differ}/3000 random columns wear the other side's surface");
        }

        // ---------------------------------------------------------------
        private static Color32 BiomeRgb(byte b) =>
            b == GenBiome.Forest ? new Color32(78, 150, 58, 255) : b == GenBiome.Desert ? new Color32(226, 196, 130, 255)
            : b == GenBiome.Snow ? new Color32(236, 240, 246, 255) : b == GenBiome.Jungle ? new Color32(34, 104, 40, 255)
            : b == GenBiome.Savanna ? new Color32(206, 170, 70, 255) : new Color32(196, 112, 96, 255);

        /// Before (record biome, the pre-122 surface) | after (surface biome),
        /// side by side: the whole 8 km world at 8 m/px, and a 1 km crop at
        /// 1 m/px around the land border nearest the 114 'high' view
        /// (eye 3857, 3659 m looking +x; the crop is centred 500 m ahead).
        [Test]
        public void Report_BiomeBlendMapPng()
        {
            ReportOnly.Require();
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "Screenshots", "122");
            Directory.CreateDirectory(dir);
            int cells = _spec.mapCells;
            float LandH(float xv, float zv)
            {
                int cx = math.clamp((int)(xv / _spec.mapCellVoxels), 0, cells - 1), cz = math.clamp((int)(zv / _spec.mapCellVoxels), 0, cells - 1);
                return _map.Height[cz * cells + cx];
            }
            void Render(string name, float2 lo, float voxPerPx, int n)
            {
                var tex = new Texture2D(2 * n + 8, n, TextureFormat.RGBA32, false);
                var px = new Color32[(2 * n + 8) * n];
                for (int i = 0; i < px.Length; i++) px[i] = new Color32(0, 0, 0, 255);
                for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float2 p = lo + (new float2(x, y) + 0.5f) * voxPerPx;
                    float h = LandH(p.x, p.y);
                    Color32 before, after;
                    if (h <= 0f) before = after = new Color32(30, 80, 150, 255);
                    else
                    {
                        byte b = ShapeField.BiomeAndBorder(in _o, p, true, out float border);
                        before = BiomeRgb(b); after = BiomeRgb(ShapeField.SurfaceBiome(in _o, p, b, border));
                    }
                    px[y * (2 * n + 8) + x] = before;
                    px[y * (2 * n + 8) + n + 8 + x] = after;
                }
                tex.SetPixels32(px); tex.Apply();
                File.WriteAllBytes(Path.Combine(dir, name), tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                Debug.Log($"[BiomeBlend] wrote Screenshots/122/{name}");
            }
            Render("biome_map_before_after.png", new float2(0f, 0f), _spec.worldSizeVoxels / 1024f, 1024);
            // The land border nearest 500 m ahead of the 114 high pose.
            float2 target = new float2(43570f, 36590f), best = target; float bestD = float.MaxValue;
            for (float dz = -15000f; dz <= 15000f; dz += 100f)
            for (float dx = -15000f; dx <= 15000f; dx += 100f)
            {
                float2 p = target + new float2(dx, dz);
                if (LandH(p.x, p.y) <= 30f) continue;
                if (ShapeField.Biome(in _o, p) == ShapeField.Biome(in _o, p + new float2(100f, 0f)) &&
                    ShapeField.Biome(in _o, p) == ShapeField.Biome(in _o, p + new float2(0f, 100f))) continue;
                float d = math.lengthsq(new float2(dx, dz));
                if (d < bestD) { bestD = d; best = p; }
            }
            Debug.Log($"[BiomeBlend] crop centre ({best.x * 0.1f:F0}, {best.y * 0.1f:F0}) m");
            Render("biome_crop_1km_before_after.png", best - 5120f, 10f, 1024);
            Render("biome_crop_200m_before_after.png", best - 1000f, 2f, 1000);
        }
    }
}
