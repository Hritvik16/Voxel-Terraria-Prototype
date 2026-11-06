// Assets/TerrainGen/Tests/CoverTests.cs
//
// Task 177 (WORLD_GEN_LOOK_PLAN C2 + C3, owner 2026-11-05): ground cover is a
// SLOT chosen from the ground's shape, filled from the biome's palette row
// (Cover.cs). These tests pin:
//   - the palette: every cell is BARE or a solid GenMat (PaletteSyncTests checks
//     each has an engine colour), and the generator's path IS the row lookup;
//   - a biome is DATA: a test-only "fire" row (high ground = magma crust) gets
//     its materials on the real columns without any rule change;
//   - slot shares on 4 seeds: every slot exists, no biome is > 85 % one slot;
//   - the speckle: adjacent top voxels of differing material, before (cover
//     slots off = the pre-177 surface) vs after, in the same land windows;
//   - cover off (-coverslots 0) is the pre-177 surface; the slot is the same
//     at every far-band footprint (it ignores texelVoxels).
// Report_CoverMaps (REPORTS=1) dumps the island's slot / cover planes for
// tools/cover177.py (maps coloured by slot and by material).
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace TerrainGen.Tests
{
    public class CoverTests
    {
        private static readonly uint[] SEEDS = { 42, 7, 2026, 31337 };
        private static readonly string[] BIOME = { "forest", "desert", "snow", "jungle", "savanna", "badlands" };
        private const string OUT_DIR = "Logs/TerrainGen/c2c3";
        private static readonly float[] CUTS = { 1f / 3f, 1f / 2f, 2f / 3f };

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
            w.off = w.spec; w.off.coverSlotsOff = 1;   // same map: the cover never changes it
            return w;
        }

        private static ColumnSample S(in TerrainSpec spec, in World w, float x, float z, int texel = 1)
        {
            var view = w.map.View;
            return ShapeField.Sample(in spec, in view, in w.o, x + 0.5f, z + 0.5f, texel);
        }

        /// Land columns: random points in land cells, the beach band excluded or not.
        private static List<int2> LandPoints(in World w, int count, uint rngSeed)
        {
            var rng = new Unity.Mathematics.Random(rngSeed);
            var pts = new List<int2>();
            int n = w.spec.mapCells, cv = w.spec.mapCellVoxels;
            int guard = 0;
            while (pts.Count < count && guard++ < count * 50)
            {
                int cx = rng.NextInt(2, n - 2), cz = rng.NextInt(2, n - 2);
                if (w.map.Height[cz * n + cx] <= 0f) continue;
                pts.Add(new int2(cx * cv + rng.NextInt(cv), cz * cv + rng.NextInt(cv)));
            }
            return pts;
        }

        [Test]
        public void Palette_EveryCellIsBareOrASolidMaterial()
        {
            for (byte b = 0; b < GenBiome.Count; b++)
            {
                var row = Cover.Row(b);
                for (int s = 0; s < Cover.SlotCount; s++)
                for (int v = 0; v < 2; v++)
                {
                    byte m = row.Get(s, v == 1);
                    Assert.IsTrue(m == Cover.BARE || (GenMat.IsSolid(m) && m < 96),
                        $"{BIOME[b]} {Cover.SlotName(s)}{(v == 1 ? " variant" : "")} = {m}: not BARE and not a solid GenMat < 96");
                    byte cover = (byte)(s | (v == 1 ? Cover.VARIANT_BIT : 0));
                    Assert.AreEqual(m, Cover.PaletteMaterial(b, cover), "Row() and the packed table disagree");
                }
            }
        }

        /// The generator's top material IS the palette lookup of the column's
        /// slot: Settle(row cell), or the column's rock where the cell is BARE
        /// or the voxel's step is sheer. No biome name anywhere in that path.
        [Test]
        public void TopMaterial_IsTheBiomesPaletteCell()
        {
            var w = Get(42);
            int checkedN = 0;
            foreach (var p in LandPoints(in w, 4000, 0x177a))
            {
                var c = S(in w.spec, in w, p.x, p.y);
                if (c.height < w.spec.seaLevelY || c.water > c.height || c.stampMat != 0 || (c.lipLo != 0 && c.height >= c.lipLo)) continue;
                float st = ShapeField.MaxStep(in w.spec, w.map.View, in w.o, p.x + 0.5f, p.y + 0.5f, 1, c.height);
                byte top = DetailField.TopMaterial(in w.spec, in c, st, in w.o, p.x, p.y);
                byte cell = Cover.Material(Cover.Row(c.surfBiome), c.cover);
                byte expect = cell == Cover.BARE || st > DetailField.SHEER_STEP
                    ? DetailField.Strata(in w.spec, in w.o, c.surfBiome, p.x, c.height, p.y)
                    : Cover.Settle(cell, st);
                Assert.AreEqual(expect, top, $"top at {p} (biome {BIOME[c.surfBiome]}, slot {Cover.SlotName(Cover.Slot(c.cover))})");
                checkedN++;
            }
            Assert.Greater(checkedN, 3000);
        }

        /// The owner's example: a later "fire" biome whose high ground is lava.
        /// It is ONE palette row (data); the slot rules have no biome input, so
        /// the real columns' slots give its materials unchanged. Not shipped:
        /// the row lives only in this test.
        [Test]
        public void FireBiome_IsOnePaletteRow_NoRuleChange()
        {
            var fire = new CoverRow
            {
                flat = GenMat.Ash, flatVar = GenMat.VolcanicRock,
                steep = Cover.BARE, steepVar = GenMat.Obsidian,
                hollow = GenMat.Obsidian, hollowVar = GenMat.Ash,
                high = GenMat.MagmaCrust, highVar = GenMat.MagmaCrust,
                shore = GenMat.Ash, shoreVar = GenMat.Ash,
            };
            var w = Get(42);
            var counts = new int[Cover.SlotCount];
            int magma = 0;
            var pts = LandPoints(in w, 6000, 0xf1e);
            // plus columns on the dominant summit, where the high slot lives
            var rng = new Unity.Mathematics.Random(0x9eaa);
            for (int i = 0; i < 1500; i++) pts.Add((int2)(w.map.PeakPosition + rng.NextFloat2(-2500f, 2500f)));
            foreach (var p in pts)
            {
                var c = S(in w.spec, in w, p.x, p.y);
                int s = Cover.Slot(c.cover);
                counts[s]++;
                byte m = Cover.Material(in fire, c.cover);
                Assert.AreEqual(fire.Get(s, Cover.IsVariant(c.cover)), m);
                if (s == Cover.High)
                {
                    Assert.AreEqual(GenMat.MagmaCrust, m, "fire's high ground is magma crust");
                    Assert.Greater(c.height - w.spec.seaLevelY, (Cover.HIGH_FRACTION - Cover.HIGH_JITTER) * w.spec.peakHeight - 1f);
                    magma++;
                }
                // and the SAME column in a shipped biome gets that biome's cell for the same slot
                var snow = Cover.Row(GenBiome.Snow);
                Assert.AreEqual(snow.Get(s, Cover.IsVariant(c.cover)), Cover.PaletteMaterial(GenBiome.Snow, c.cover));
            }
            Debug.Log($"[Cover] fire row on seed 42: slots flat {counts[0]} steep {counts[1]} hollow {counts[2]} high {counts[3]} shore {counts[4]}; magma-crust columns {magma}");
            Assert.Greater(magma, 20, "the fire row's high slot must appear on the summit");
            for (int s = 0; s < Cover.SlotCount; s++) Assert.Greater(counts[s], 0, $"slot {Cover.SlotName(s)} absent");
        }

        [Test]
        public void CoverOff_IsThePre177Surface_AndTheSlotHoldsAcrossFootprints()
        {
            var w = Get(7);
            int[] texels = { 4, 16, 32, 128 };
            var same = new int[texels.Length]; var asked = new int[texels.Length];
            foreach (var p in LandPoints(in w, 1500, 0x0ff))
            {
                var off = S(in w.off, in w, p.x, p.y);
                Assert.AreEqual(0, off.cover, "cover off: no slot computed");
                var on = S(in w.spec, in w, p.x, p.y);
                Assert.AreEqual(off.height, on.height, "the cover never moves the ground");
                Assert.AreEqual(off.surfBiome, on.surfBiome);
                // the far band's footprints: same cover wherever the ground lands on the same
                // side of the shore / high cut (they use the height) -- exactly up to texel 22,
                // where Cover's band limit keeps every octave; beyond it the fine noises drop.
                for (int t = 0; t < texels.Length; t++)
                {
                    var far = S(in w.spec, in w, p.x, p.y, texels[t]);
                    int sOn = Cover.Slot(on.cover), sFar = Cover.Slot(far.cover);
                    if (sOn == Cover.Shore || sFar == Cover.Shore || sOn == Cover.High || sFar == Cover.High) continue;
                    if (on.water > on.height || far.water > far.height) continue;
                    asked[t]++;
                    if (on.cover == far.cover) same[t]++;
                    else if (texels[t] <= 22) Assert.Fail($"cover differs at texel {texels[t]} at {p}: {on.cover} vs {far.cover}");
                }
            }
            var line = new StringBuilder("[Cover] seed 7, same cover as texel 1:");
            for (int t = 0; t < texels.Length; t++) line.Append($" texel {texels[t]} {100.0 * same[t] / math.max(1, asked[t]):F1} %");
            Debug.Log(line.ToString());
            var v3 = TerrainSpec.SizeClass2(42, 3);
            Assert.IsFalse(v3.CoverSlots, "map v3 keeps its surface");
        }

        // ------------------------------------------------------------------
        // Acceptance on 4 seeds: slot shares per biome and the speckle metric.
        // ------------------------------------------------------------------

        [Test, Category("Slow")]
        public void SlotShares_AndSpeckle_FourSeeds()
        {
            Directory.CreateDirectory(OUT_DIR);
            var sb = new StringBuilder();
            sb.AppendLine("Task 177 cover slots: per-biome slot shares (land columns, % of the biome) and the speckle metric.");
            sb.AppendLine("speckle = % of top voxels (0.1 m) with 3-4 of their 4 neighbours another material (isolated: a dither, not a patch edge);");
            sb.AppendLine("edges = % of adjacent top-voxel pairs that differ (patch edges count too). 40 windows of 4.8 m per seed on land;");
            sb.AppendLine("before = -coverslots 0 (the pre-177 surface), after = cover slots; same windows.");
            var fails = new List<string>();
            foreach (uint seed in SEEDS)
            {
                var w = Get(seed);
                var count = new int[GenBiome.Count, Cover.SlotCount];
                var tot = new int[GenBiome.Count];
                int highShape = 0;
                var wouldHigh = new int[3];   // non-steep land over 1/3, 1/2, 2/3 of the range (the high cut's alternatives)
                int landN = 0;
                foreach (var p in LandPoints(in w, 12000, seed * 7919u + 1u))
                {
                    var c = S(in w.spec, in w, p.x, p.y);
                    if (c.height <= w.spec.seaLevelY) continue;
                    count[c.surfBiome, Cover.Slot(c.cover)]++;
                    tot[c.surfBiome]++;
                    if (c.height - w.spec.seaLevelY > (Cover.HIGH_FRACTION + Cover.HIGH_JITTER) * w.spec.peakHeight) highShape++;
                    landN++;
                    int sl = Cover.Slot(c.cover);
                    if (c.height - w.spec.seaLevelY > (CUTS[0] - Cover.HIGH_JITTER) * w.spec.peakHeight && sl != Cover.Shore && c.water <= c.height)
                    {
                        // the real rule with the cut moved: peakHeight scaled so HIGH_FRACTION * it = the cut
                        var view = w.map.View;
                        ShapeField.SampleMap(in view, p.x + 0.5f, p.y + 0.5f, out float macro, out float rough);
                        for (int f = 0; f < 3; f++)
                        {
                            var sp = w.spec; sp.peakHeight *= CUTS[f] / Cover.HIGH_FRACTION;
                            if (Cover.ChooseSlot(in sp, in view, in w.o, new float2(p.x + 0.5f, p.y + 0.5f), macro, rough, c.height, c.water, c.regional, 1) == Cover.High)
                                wouldHigh[f]++;
                        }
                    }
                }
                sb.AppendLine($"seed {seed}");
                int[] slotTotal = new int[Cover.SlotCount];
                for (int b = 0; b < GenBiome.Count; b++)
                {
                    if (tot[b] == 0) { sb.AppendLine($"  {BIOME[b],-9} (no samples)"); continue; }
                    var line = new StringBuilder($"  {BIOME[b],-9} n={tot[b],5}:");
                    int max = 0;
                    for (int s = 0; s < Cover.SlotCount; s++)
                    {
                        line.Append($" {Cover.SlotName(s)} {100.0 * count[b, s] / tot[b],5:F1}");
                        max = math.max(max, count[b, s]); slotTotal[s] += count[b, s];
                    }
                    sb.AppendLine(line.ToString());
                    if (tot[b] >= 300)
                    {
                        if (100.0 * max / tot[b] > 85.0) fails.Add($"seed {seed} {BIOME[b]}: one slot > 85 %");
                        foreach (int s in new[] { Cover.Flat, Cover.Steep, Cover.Hollow })
                            if (count[b, s] == 0) fails.Add($"seed {seed} {BIOME[b]}: no {Cover.SlotName(s)}");
                    }
                }
                for (int s = 0; s < Cover.SlotCount; s++)
                    if ((s != Cover.High || highShape > 0) && slotTotal[s] == 0) fails.Add($"seed {seed}: slot {Cover.SlotName(s)} absent");
                sb.AppendLine($"  columns above the high cut + jitter: {highShape}; the high slot's share of land with its cut at 1/3 | 1/2 | 2/3 (shipped) of the range: "
                              + $"{100.0 * wouldHigh[0] / landN:F1} | {100.0 * wouldHigh[1] / landN:F1} | {100.0 * wouldHigh[2] / landN:F1} % of land");

                // Speckle: 4.8 m windows on land (above the beach band), before vs after.
                // speckle = isolated top voxels: 3 or 4 of the 4 neighbours another
                // material (a dither scores ~30 % of its voxels, a patch edge ~0).
                // edges = adjacent pairs that differ (patch edges count here too).
                var rng = new Unity.Mathematics.Random(seed * 31u + 5u);
                const int WIN = 48, WINDOWS = 40;
                var acc = new long[GenBiome.Count + 1, 6];   // pairs, edges before, edges after, voxels, isolated before, after
                int placed = 0, guard = 0;
                while (placed < WINDOWS && guard++ < 4000)
                {
                    int n = w.spec.mapCells, cv = w.spec.mapCellVoxels;
                    int cx = rng.NextInt(2, n - 2), cz = rng.NextInt(2, n - 2);
                    if (w.map.Height[cz * n + cx] <= Cover.SHORE_ABOVE + 10) continue;
                    int x0 = cx * cv, z0 = cz * cv;
                    var b0 = Window(in w, in w.off, x0, z0, WIN, out _, out bool wet0);
                    var b1 = Window(in w, in w.spec, x0, z0, WIN, out byte biome, out bool wet1);
                    if (wet0 || wet1) continue;   // lakes and rivers: the water is the top
                    placed++;
                    foreach (int row in new[] { biome, GenBiome.Count })
                    {
                        for (int k = 0; k < WIN; k++)
                        for (int i = 0; i < WIN; i++)
                        {
                            int a = k * WIN + i;
                            if (i + 1 < WIN) { acc[row, 0]++; if (b0[a] != b0[a + 1]) acc[row, 1]++; if (b1[a] != b1[a + 1]) acc[row, 2]++; }
                            if (k + 1 < WIN) { acc[row, 0]++; if (b0[a] != b0[a + WIN]) acc[row, 1]++; if (b1[a] != b1[a + WIN]) acc[row, 2]++; }
                            if (i == 0 || k == 0 || i == WIN - 1 || k == WIN - 1) continue;
                            acc[row, 3]++;
                            if (Isolated(b0, a, WIN)) acc[row, 4]++;
                            if (Isolated(b1, a, WIN)) acc[row, 5]++;
                        }
                    }
                }
                string Pct(long a, long b) => (100.0 * a / math.max(1, b)).ToString("F2");
                int T = GenBiome.Count;
                sb.AppendLine($"  {placed} windows: speckle (isolated voxels) before {Pct(acc[T, 4], acc[T, 3])} % -> after {Pct(acc[T, 5], acc[T, 3])} %;"
                              + $" material edges before {Pct(acc[T, 1], acc[T, 0])} % -> after {Pct(acc[T, 2], acc[T, 0])} %");
                for (int b = 0; b < GenBiome.Count; b++)
                    if (acc[b, 3] > 0)
                        sb.AppendLine($"    {BIOME[b],-9} speckle {Pct(acc[b, 4], acc[b, 3])} -> {Pct(acc[b, 5], acc[b, 3])} %, edges {Pct(acc[b, 1], acc[b, 0])} -> {Pct(acc[b, 2], acc[b, 0])} %  ({acc[b, 3]} voxels)");
                if (!(acc[T, 5] < acc[T, 4])) fails.Add($"seed {seed}: speckle did not go down ({Pct(acc[T, 4], acc[T, 3])} -> {Pct(acc[T, 5], acc[T, 3])} %)");
            }
            File.WriteAllText(Path.Combine(OUT_DIR, "shares177.txt"), sb.ToString());
            Debug.Log("[Cover] " + sb);
            Assert.IsEmpty(fails, string.Join("; ", fails));
        }

        private static bool Isolated(byte[] t, int a, int win)
        {
            int d = (t[a] != t[a - 1] ? 1 : 0) + (t[a] != t[a + 1] ? 1 : 0) + (t[a] != t[a - win] ? 1 : 0) + (t[a] != t[a + win] ? 1 : 0);
            return d >= 3;
        }

        /// The top material of a WIN x WIN block of columns, steps from the
        /// columns' own heights (as the chunk source does).
        private static byte[] Window(in World w, in TerrainSpec spec, int x0, int z0, int win, out byte biome, out bool wet)
        {
            int G = win + 2;
            var cols = new ColumnSample[G * G];
            for (int k = 0; k < G; k++)
            for (int i = 0; i < G; i++)
                cols[k * G + i] = S(in spec, in w, x0 + i - 1, z0 + k - 1);
            var top = new byte[win * win];
            wet = false;
            biome = cols[(win / 2 + 1) * G + win / 2 + 1].surfBiome;
            for (int k = 0; k < win; k++)
            for (int i = 0; i < win; i++)
            {
                int g = (k + 1) * G + i + 1;
                var c = cols[g];
                if (c.water > c.height) wet = true;
                int h = c.height;
                int m = math.max(math.max(math.abs(cols[g + 1].height - h), math.abs(cols[g - 1].height - h)),
                                 math.max(math.abs(cols[g + G].height - h), math.abs(cols[g - G].height - h)));
                top[k * win + i] = DetailField.TopMaterial(in spec, in c, m, in w.o, x0 + i, z0 + k);
            }
            return top;
        }

        // ------------------------------------------------------------------
        // Report: the island's slot and cover planes for tools/cover177.py.
        // ------------------------------------------------------------------

        [Test, Category("Slow")]
        public void Report_CoverMaps()
        {
            ReportOnly.Require();
            Directory.CreateDirectory(OUT_DIR);
            const int N = 512;
            foreach (uint seed in SEEDS)
            {
                var w = Get(seed);
                float step = (float)w.spec.worldSizeVoxels / N;
                var bytes = new byte[N * N * 4];   // slot|variant (255 = sea / water), surface biome, top material after, before
                var swOn = new System.Diagnostics.Stopwatch(); var swOff = new System.Diagnostics.Stopwatch();
                for (int k = 0; k < N; k++)
                for (int i = 0; i < N; i++)
                {
                    int x = (int)((i + 0.5f) * step), z = (int)((k + 0.5f) * step);
                    int o = (k * N + i) * 4;
                    swOn.Start(); var c = S(in w.spec, in w, x, z); swOn.Stop();
                    bool water = c.height < w.spec.seaLevelY || c.water > c.height;
                    const float st = 0f;   // map scale: the slot's material, not a voxel's settle
                    bytes[o] = water ? (byte)255 : c.cover;
                    bytes[o + 1] = c.surfBiome;
                    bytes[o + 2] = DetailField.TopMaterial(in w.spec, in c, st, in w.o, x, z);
                    swOff.Start(); var cOff = S(in w.off, in w, x, z); swOff.Stop();
                    bytes[o + 3] = DetailField.TopMaterial(in w.off, in cOff, st, in w.o, x, z);
                }
                File.WriteAllBytes(Path.Combine(OUT_DIR, $"cover_seed{seed}.bin"), bytes);
                string cost = $"seed {seed}: ShapeField.Sample (Mono, {N * N} columns, sea included) cover on {swOn.Elapsed.TotalMilliseconds * 1000.0 / (N * N):F1} us, off {swOff.Elapsed.TotalMilliseconds * 1000.0 / (N * N):F1} us per column";
                File.AppendAllText(Path.Combine(OUT_DIR, "cost177.txt"), cost + "\n");
                Debug.Log("[Cover] " + cost);
            }
            Debug.Log($"[Cover] wrote {SEEDS.Length} cover planes ({N}^2) to {OUT_DIR}");
        }
    }
}
