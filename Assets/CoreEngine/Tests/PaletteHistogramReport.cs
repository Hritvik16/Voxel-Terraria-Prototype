// Assets/CoreEngine/Tests/PaletteHistogramReport.cs
//
// Task 26c (AMENDMENT_8_13_PALETTE_BRICKS.md, DRAFT): how many DISTINCT
// materials (air counts as one) a dense 8^3 brick holds, measured on map v4,
// seed 42, through the real TerrainGenChunkSource -- the number that picks the
// palette body formats. REPORT ONLY: asserts nothing, changes no engine code.
//
// CHUNK SET (fixed, so two runs compare): three sites, each the 21 x 21 chunk
// columns a load radius of 10 covers (EngineConfig.TG2_LOAD_RADIUS_CHUNKS) x
// the 7 layers of the vertical band (StreamManager.V_LOAD_LAYERS = 3 each side)
// centred on the site's ground layer -- 3,087 chunks per site, the resident
// count the v4 poses report:
//   spawn     Phase4Bootstrapper.Tg2Spawn's spiral (the walk circles it)
//   peak      map.PeakPosition (the summit_view pose stands beside it)
//   from2k    2 km from the peak toward the centre (peak_from_2km_ground)
// The real band is terrain-aware (StreamManager.PlaceBand) and can sit a layer
// off; this set centres on the ground, which is where the dense bricks are.
//
// Tier 1 / 2 are the cascade bodies (§6.4, 8.9: 0.2 m / 0.4 m) made by the
// shipped LODDownsampler chain from the same chunks and cut into 8^3 bricks
// exactly as CascadeTierPool.WriteChunkFromDownsampled does.
//
// Run: REPORTS=1 tools/run-editmode-filter.sh PaletteHistogramReport
// Out: Logs/TerrainGen/palette_histogram.txt
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using Unity.Mathematics;
using VoxelEngine.Memory;
using VoxelEngine.Mirror;
using VoxelEngine.WorldGen;

public class PaletteHistogramReport
{
    private sealed class Hist
    {
        public readonly long[] byDistinct = new long[257];
        public long dense, withAir, pairWithAir;
        public void Add(ulong a, ulong b, ulong c, ulong d)
        {
            int n = math.countbits(a) + math.countbits(b) + math.countbits(c) + math.countbits(d);
            byDistinct[n]++; dense++;
            if ((a & 1ul) != 0) { withAir++; if (n == 2) pairWithAir++; }
        }
        public void Merge(Hist o)
        {
            for (int i = 0; i < byDistinct.Length; i++) byDistinct[i] += o.byDistinct[i];
            dense += o.dense; withAir += o.withAir; pairWithAir += o.pairWithAir;
        }
    }

    // Body bytes per format with the amendment's layout: palette header in
    // 4-byte words in front of the packed indices (1b: 1 word, 2b: 1 word,
    // 4b: 4 words; 8b: no palette, the bytes ARE the materials).
    private static readonly int[] FormatBits = { 1, 2, 4, 8 };
    private static readonly int[] FormatBytes = { 4 + 64, 4 + 128, 16 + 256, 512 };

    private static int BytesFor(int distinct, int[] set)
    {
        foreach (int f in set) if (distinct <= (1 << FormatBits[f])) return FormatBytes[f];
        return 512;
    }

    private static readonly (string name, int[] set)[] Sets =
    {
        ("8b only (today)", new[] { 3 }),
        ("4b + 8b", new[] { 2, 3 }),
        ("2b + 8b", new[] { 1, 3 }),
        ("2b + 4b + 8b", new[] { 1, 2, 3 }),
        ("1b + 2b + 4b + 8b", new[] { 0, 1, 2, 3 }),
    };

    private static void Describe(StringBuilder sb, string title, Hist h)
    {
        sb.AppendLine($"  {title}: {h.dense:N0} dense bricks");
        if (h.dense == 0) return;
        long b1 = h.byDistinct[1], b2 = h.byDistinct[2], b34 = h.byDistinct[3] + h.byDistinct[4], b516 = 0, b17 = 0;
        for (int i = 5; i <= 16; i++) b516 += h.byDistinct[i];
        for (int i = 17; i <= 256; i++) b17 += h.byDistinct[i];
        double P(long v) => 100.0 * v / h.dense;
        sb.AppendLine($"    distinct  1: {P(b1),6:F2}%   2: {P(b2),6:F2}%   3-4: {P(b34),6:F2}%   5-16: {P(b516),6:F2}%   17+: {P(b17),6:F2}%");
        var fine = new StringBuilder("    fine:");
        for (int i = 1; i <= 16; i++) if (h.byDistinct[i] > 0) fine.Append($" {i}={h.byDistinct[i]}");
        if (b17 > 0) fine.Append($" 17+={b17}");
        sb.AppendLine(fine.ToString());
        sb.AppendLine($"    holds air: {P(h.withAir):F2}%   2-material bricks that are air + one: {(b2 > 0 ? 100.0 * h.pairWithAir / b2 : 0):F2}% of 2s");
        foreach (var (name, set) in Sets)
        {
            double bytes = 0;
            for (int i = 1; i <= 256; i++) bytes += (double)h.byDistinct[i] * BytesFor(i, set);
            sb.AppendLine($"    {name,-20} mean {bytes / h.dense,6:F1} B/brick = {100.0 * bytes / (h.dense * 512.0),5:F1}% of 512 B");
        }
    }

    private static void AddBrick(Hist h, byte[] flat, int edge, int ox, int oy, int oz)
    {
        ulong a = 0, b = 0, c = 0, d = 0;
        for (int z = 0; z < 8; z++)
        for (int y = 0; y < 8; y++)
        {
            int row = (ox) + edge * ((oy + y) + edge * (oz + z));
            for (int x = 0; x < 8; x++)
            {
                int m = flat[row + x];
                ulong bit = 1ul << (m & 63);
                switch (m >> 6) { case 0: a |= bit; break; case 1: b |= bit; break; case 2: c |= bit; break; default: d |= bit; break; }
            }
        }
        if (math.countbits(a) + math.countbits(b) + math.countbits(c) + math.countbits(d) > 1) h.Add(a, b, c, d);
    }

    [Test, Category("Slow"), Timeout(3600000)]
    public void DistinctMaterialsPerDenseBrick_MapV4Seed42()
    {
        // CoreEngine.Tests does not reference TerrainGen.Tests (ReportOnly); same rule.
        if (System.Environment.GetEnvironmentVariable("REPORTS") != "1") Assert.Ignore("report only: set REPORTS=1");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var src = new TerrainGenChunkSource(42u, 4);
        var pool = new BrickDataPool(EngineConfig.BRICKS_PER_CHUNK);
        var handles = new ChunkHandleAllocator(2);
        var scratch = new LODDownsampler.DownsampleScratch();
        var sb = new StringBuilder();
        try
        {
            var spec = src.Spec; var view = src.Map.View; var o = src.O;
            float c = spec.worldSizeVoxels * 0.5f;
            float2 spawn = new float2(c, c);
            for (int i = 0; i < 2000; i++)          // Phase4Bootstrapper.Tg2Spawn, verbatim
            {
                float an = i * 2.39996f, r = 40f * i;
                float2 p = new float2(c + r * math.cos(an), c + r * math.sin(an));
                var col = TerrainGen.ShapeField.Sample(in spec, in view, in o, p.x, p.y, 1);
                if (col.height < spec.seaLevelY + 200 || col.water > col.height) continue;
                spawn = p; break;
            }
            float2 pk = src.Map.PeakPosition;
            float2 toC = math.normalizesafe(new float2(c, c) - pk, new float2(1, 0));
            var sites = new (string, float2)[] { ("spawn", spawn), ("peak", pk), ("from2k", pk + toC * 20000f) };

            sb.AppendLine($"Distinct materials per dense brick (air counts), map v4 seed 42, TerrainGenChunkSource (Editor, Burst)");
            sb.AppendLine($"chunk set per site: 21x21 columns x 7 layers centred on the site's ground layer; {System.DateTime.Now:yyyy-MM-dd HH:mm}");
            sb.AppendLine("formats: 1b 68 B, 2b 132 B, 4b 272 B (palette words in front of packed indices), 8b 512 B");
            var all = new Hist[3] { new Hist(), new Hist(), new Hist() };
            int R = EngineConfig.TG2_LOAD_RADIUS_CHUNKS;
            foreach (var (name, site) in sites)
            {
                var col = TerrainGen.ShapeField.Sample(in spec, in view, in o, site.x, site.y, 1);
                int gy = math.max(col.height, spec.seaLevelY) >> 7;
                int3 cc = new int3((int)site.x >> 7, gy, (int)site.y >> 7);
                var h = new Hist[3] { new Hist(), new Hist(), new Hist() };
                int chunks = 0, uniform = 0;
                for (int dz = -R; dz <= R; dz++)
                for (int dx = -R; dx <= R; dx++)
                for (int y = math.max(0, gy - 3); y <= math.min(src.TopLayerChunkY, gy + 3); y++)   // bottom up: the column memo skips air above
                {
                    var coord = new int3(cc.x + dx, y, cc.z + dz);
                    var chunk = new Chunk();
                    pool.Reset();
                    src.Generate(coord, chunk, handles, pool, null);
                    chunks++;
                    if (chunk.isUniform) { uniform++; continue; }
                    var raw = pool.RawData;
                    var body = new byte[512];
                    for (int bi = 0; bi < EngineConfig.BRICKS_PER_CHUNK; bi++)
                    {
                        uint d = chunk.bricks[bi].data;
                        if ((d & 0x80000000u) == 0) continue;
                        int start = (int)(d & 0x3FFFFFFFu) << 9;
                        ulong a0 = 0, a1 = 0, a2 = 0, a3 = 0;
                        for (int k = 0; k < 512; k++)
                        {
                            int m = raw[start + k];
                            ulong bit = 1ul << (m & 63);
                            switch (m >> 6) { case 0: a0 |= bit; break; case 1: a1 |= bit; break; case 2: a2 |= bit; break; default: a3 |= bit; break; }
                        }
                        h[0].Add(a0, a1, a2, a3);
                    }
                    if (LODDownsampler.BuildChain(chunk, pool, scratch) > 0)
                    {
                        for (int t = 1; t <= 2; t++)
                        {
                            byte[] flat = LODDownsampler.ChainResultFor(t, scratch);
                            int edge = 128 >> LODDownsampler.StepsForTier(t);
                            for (int bz = 0; bz < edge; bz += 8)
                            for (int by = 0; by < edge; by += 8)
                            for (int bx = 0; bx < edge; bx += 8)
                                AddBrick(h[t], flat, edge, bx, by, bz);
                        }
                    }
                    handles.Free(chunk.bricks);
                }
                sb.AppendLine();
                sb.AppendLine($"SITE {name}: centre chunk {cc}, ground Y {col.height}; {chunks} chunks, {uniform} uniform, {chunks - uniform} populated; {sw.Elapsed.TotalSeconds:F0} s so far");
                for (int t = 0; t < 3; t++) { Describe(sb, $"tier {t}", h[t]); all[t].Merge(h[t]); }
                WriteOut(sb);   // partial result after each site
            }
            sb.AppendLine();
            sb.AppendLine("ALL SITES");
            for (int t = 0; t < 3; t++) Describe(sb, $"tier {t}", all[t]);
            sb.AppendLine($"wall {sw.Elapsed.TotalSeconds:F0} s");
        }
        finally { pool.Dispose(); src.Dispose(); }

        WriteOut(sb);
        UnityEngine.Debug.Log("[26c palette histogram]\n" + sb);
    }

    private static void WriteOut(StringBuilder sb)
    {
        string dir = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Logs", "TerrainGen");
        System.IO.Directory.CreateDirectory(dir);
        System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "palette_histogram.txt"), sb.ToString());
    }
}

