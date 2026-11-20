// Assets/CoreEngine/Rendering/RayStatsReport.cs
//
// TASK 244 (capture only; PERF_SLICE_BUDGETS sec. 4 / P5, levers F4 and F6): turns the RayStats counters
// (Raymarch.compute, RaymarchFeature.CollectRayStats / -raystats 1) into the capture report's table. CPU only, no Unity
// objects: the layout and the histogram bins are the shader's (RS_HDR / RS_REC / RS_HIST, RayStatsBin), twinned here and
// pinned by RayStatsReportTests.
//   [0] frames, [1] in-bounds primary pixels, [2..7] spare; then 12 records of 200 uints: kind (0 primary, 1 sun, 2 sky)
//   x 4 + band (0-10 m, 10-40 m, 40-82 m, 82 m+ / no hit): [0] rays, [1] steps, [2] tier-0 steps, [3] blocked, [4] blocked
//   by a tier-0 leaf voxel, [5] stopped by the foliage early-out (task 245), [8..199] steps-per-ray histogram.

using System.Text;

namespace VoxelEngine.Rendering
{
    public static class RayStatsReport
    {
        public const int HDR = 8, REC = 200, HIST = 8, BINS = 192, KINDS = 3, BANDS = 4;
        public const int LEN = HDR + KINDS * BANDS * REC;   // = RaymarchFeature.RAYSTATS_LEN
        public static readonly string[] KindNames = { "primary", "sun", "sky" };
        public static readonly string[] BandNames = { "0-10 m", "10-40 m", "40-82 m", "82 m+" };

        /// The shader's RayStatsBin: 0-63 exact, 64-1087 in 16s, 1088-5119 in 64s, 191 = 5120 and over.
        public static int Bin(uint s) => s < 64u ? (int)s : (s < 1088u ? 64 + (int)((s - 64u) >> 4) : (int)System.Math.Min(128u + ((s - 1088u) >> 6), 191u));
        /// Smallest step count that falls in bin b.
        public static uint BinLow(int b) => b < 64 ? (uint)b : (b < 128 ? 64u + (uint)(b - 64) * 16u : 1088u + (uint)(b - 128) * 64u);
        /// Largest step count in bin b (uint.MaxValue for the last, open bin).
        public static uint BinHigh(int b) => b >= BINS - 1 ? uint.MaxValue : BinLow(b + 1) - 1u;

        public static int Rec(int kind, int band) => HDR + (kind * BANDS + band) * REC;

        /// The q-quantile of a histogram (q in (0, 1]) as the bin holding it: [lo, hi]; false when empty.
        public static bool Quantile(ulong[] hist, double q, out uint lo, out uint hi)
        {
            lo = hi = 0u;
            ulong total = 0;
            foreach (ulong h in hist) total += h;
            if (total == 0) return false;
            double want = q * total;
            ulong cum = 0;
            for (int b = 0; b < hist.Length; b++)
            {
                cum += hist[b];
                if (cum >= want && hist[b] > 0) { lo = BinLow(b); hi = BinHigh(b); return true; }
            }
            lo = BinLow(hist.Length - 1); hi = BinHigh(hist.Length - 1);
            return true;
        }

        /// Histogram of one kind over a set of bands (bandMask bit k = band k).
        public static ulong[] Hist(uint[] d, int kind, int bandMask)
        {
            var h = new ulong[BINS];
            for (int band = 0; band < BANDS; band++)
            {
                if ((bandMask & (1 << band)) == 0) continue;
                int r = Rec(kind, band);
                for (int b = 0; b < BINS; b++) h[b] += d[r + HIST + b];
            }
            return h;
        }

        static string Q(ulong[] h, double q)
        {
            if (!Quantile(h, q, out uint lo, out uint hi)) return "-";
            if (hi == uint.MaxValue) return $">={lo}";
            return lo == hi ? lo.ToString() : $"{lo}-{hi}";
        }

        static void Row(StringBuilder sb, uint[] d, int kind, int bandMask, string bandName, double frames, ulong pixels)
        {
            ulong rays = 0, steps = 0, t0 = 0, blk = 0, leaf = 0, fol = 0;
            for (int band = 0; band < BANDS; band++)
            {
                if ((bandMask & (1 << band)) == 0) continue;
                int r = Rec(kind, band);
                rays += d[r]; steps += d[r + 1]; t0 += d[r + 2]; blk += d[r + 3]; leaf += d[r + 4]; fol += d[r + 5];
            }
            ulong[] h = Hist(d, kind, bandMask);
            double avg = rays > 0 ? (double)steps / rays : 0.0;
            sb.Append($"    {KindNames[kind],-8}{bandName,-9}{rays / frames,11:N0}{(pixels > 0 ? 100.0 * rays / pixels : 0.0),7:F1}%" +
                      $"{steps / frames,13:N0}{avg,8:F1}{Q(h, 0.5),11}{Q(h, 0.95),11}" +
                      $"{(steps > 0 ? 100.0 * t0 / steps : 0.0),8:F0}%{(rays > 0 ? 100.0 * blk / rays : 0.0),8:F0}%{(rays > 0 ? 100.0 * leaf / rays : 0.0),7:F0}%{(rays > 0 ? 100.0 * fol / rays : 0.0),7:F1}%\n");
        }

        /// The table for one counter window. d.Length must be LEN; frames = d[0] (the GPU's own count).
        public static string Format(uint[] d, string title)
        {
            var sb = new StringBuilder();
            if (d == null || d.Length < LEN) { sb.Append($"  [raystats] {title}: no data\n"); return sb.ToString(); }
            double frames = System.Math.Max(1u, d[0]);
            ulong pixels = d[1];
            sb.Append($"  [raystats] {title}: {d[0]} frames (GPU-counted), {pixels / frames:N0} primary px/frame; per frame, " +
                      "band = camera distance of the ray's pixel / face (no hit or far band = 82 m+)\n");
            sb.Append("    kind    band      rays/frame  %px  steps/frame  avg/ray  p50 steps  p95 steps  tier-0  blocked   leaf  folstop\n");
            for (int kind = 0; kind < KINDS; kind++)
            {
                for (int band = 0; band < BANDS; band++) Row(sb, d, kind, 1 << band, BandNames[band], frames, pixels);
                Row(sb, d, kind, (1 << BANDS) - 1, "all", frames, pixels);
            }
            sb.Append("    (%px = rays per primary pixel; tier-0 = share of the steps taken in the tier-0 window (sun: TraceShadowRay; the rest is\n" +
                      "     task 133's shell continuation, task 80's shell-hit ray, the far band's ray); blocked = rays that ended on terrain\n" +
                      "     (primary: hit); leaf = rays stopped by a tier-0 LEAF voxel (primary: the hit is leaf); folstop = rays the foliage\n" +
                      "     early-out stopped (task 245, -foliageout); p50 / p95 = the histogram bin\n" +
                      "     holding it: exact below 64 steps, 16-step bins to 1087, 64-step bins to 5119)\n");
            return sb.ToString();
        }
    }
}
