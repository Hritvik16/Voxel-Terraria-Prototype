// ==========================================
// Assets/CoreEngine/Tests/RayStatsReportTests.cs
//
// TASK 244 (capture only, -raystats 1): the ray-work counters. The GPU side cannot run here, so these pin what can go
// wrong without a launch: (1) the C# twin of the shader's histogram bins and buffer layout, (2) the report's averages
// and quantiles on synthetic buffers (an adversarial all-in-the-open-bin case included), and (3) that NO play kernel
// can touch the counters: every RayStats write sits under #ifdef VE_SHELL_DIAG (the *Diag kernels), and the switches
// default OFF. Pixel identity ON vs OFF is the launch's job (tools/rspair244.py on the _raw_rs* stills).
// ==========================================
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using VoxelEngine.Rendering;

public class RayStatsReportTests
{
    const string Shader = "Assets/CoreEngine/Rendering/Raymarch.compute";

    [Test]
    public void Bins_TwinTheShader_AndCoverEveryCount()
    {
        string src = File.ReadAllText(Shader);
        StringAssert.Contains("uint RayStatsBin(uint s) { return s < 64u ? s : (s < 1088u ? 64u + ((s - 64u) >> 4) : min(128u + ((s - 1088u) >> 6), 191u)); }", src);
        StringAssert.Contains("#define RS_HDR  8u", src);
        StringAssert.Contains("#define RS_REC  200u", src);
        StringAssert.Contains("#define RS_HIST 8u", src);
        StringAssert.Contains("uint RayStatsBand(float tVox) { return tVox < 100.0 ? 0u : (tVox < 400.0 ? 1u : (tVox < 820.0 ? 2u : 3u)); }", src);
        Assert.AreEqual(RayStatsReport.REC, RayStatsReport.HIST + RayStatsReport.BINS, "a record = 8 scalars + the histogram");
        Assert.AreEqual(RayStatsReport.LEN, RaymarchFeature.RAYSTATS_LEN);
        Assert.AreEqual(8 + 12 * 200, RayStatsReport.LEN);
        // Every count lands in a bin whose [low, high] holds it; bins are contiguous and increasing.
        for (uint s = 0; s < 7000u; s++)
        {
            int b = RayStatsReport.Bin(s);
            Assert.IsTrue(b >= 0 && b < RayStatsReport.BINS, $"{s} -> bin {b}");
            Assert.IsTrue(RayStatsReport.BinLow(b) <= s && s <= RayStatsReport.BinHigh(b), $"{s} -> bin {b} [{RayStatsReport.BinLow(b)}, {RayStatsReport.BinHigh(b)}]");
        }
        for (int b = 1; b < RayStatsReport.BINS; b++)
            Assert.AreEqual(RayStatsReport.BinHigh(b - 1) + 1u, RayStatsReport.BinLow(b), $"bin {b} contiguous");
        Assert.AreEqual(191, RayStatsReport.Bin(uint.MaxValue));
        Assert.AreEqual(63, RayStatsReport.Bin(63u));
        Assert.AreEqual(64, RayStatsReport.Bin(64u));
        Assert.AreEqual(127, RayStatsReport.Bin(1087u));
        Assert.AreEqual(128, RayStatsReport.Bin(1088u));
        Assert.AreEqual(190, RayStatsReport.Bin(5119u));
        Assert.AreEqual(191, RayStatsReport.Bin(5120u));
    }

    static uint[] Synthetic(params (int kind, int band, uint steps, uint t0, bool blk, bool leaf)[] rays)
    {
        var d = new uint[RayStatsReport.LEN];
        foreach (var r in rays)
        {
            int o = RayStatsReport.Rec(r.kind, r.band);
            d[o]++; d[o + 1] += r.steps; d[o + 2] += r.t0;
            if (r.blk) d[o + 3]++;
            if (r.leaf) d[o + 4]++;
            d[o + RayStatsReport.HIST + RayStatsReport.Bin(r.steps)]++;
        }
        return d;
    }

    [Test]
    public void Quantiles_ExactBelow64_BinRangeAbove_OpenBinSaysSo()
    {
        var rays = new List<(int, int, uint, uint, bool, bool)>();
        for (uint k = 1; k <= 100; k++) rays.Add((1, 0, k, k, false, false));   // sun, 0-10 m: 1..100 steps
        uint[] d = Synthetic(rays.ToArray());
        ulong[] h = RayStatsReport.Hist(d, 1, 1);
        Assert.IsTrue(RayStatsReport.Quantile(h, 0.5, out uint lo, out uint hi));
        Assert.AreEqual(50u, lo); Assert.AreEqual(50u, hi);
        Assert.IsTrue(RayStatsReport.Quantile(h, 0.95, out lo, out hi));
        Assert.AreEqual(RayStatsReport.BinLow(RayStatsReport.Bin(95u)), lo, "95 is in the 16-step bin 80-95");
        Assert.IsTrue(lo <= 95u && 95u <= hi);
        Assert.IsFalse(RayStatsReport.Quantile(RayStatsReport.Hist(d, 0, 15), 0.95, out _, out _), "empty kind");
        // Adversarial: every ray past the last bound.
        uint[] big = Synthetic((2, 3, 100000u, 0u, true, false), (2, 3, 6000u, 0u, true, false));
        string t = RayStatsReport.Format(big, "big");
        StringAssert.Contains(">=5120", t);
    }

    [Test]
    public void Format_PerFrameAveragesAndShares()
    {
        // 2 frames, 400 primary pixels; sun 0-10 m: 10 rays of 30 steps (20 tier 0), 4 blocked, 3 by leaf.
        var rays = new List<(int, int, uint, uint, bool, bool)>();
        for (int k = 0; k < 10; k++) rays.Add((1, 0, 30u, 20u, k < 4, k < 3));
        uint[] d = Synthetic(rays.ToArray());
        d[0] = 2; d[1] = 400;
        string t = RayStatsReport.Format(d, "pose");
        StringAssert.Contains("2 frames (GPU-counted), 200 primary px/frame", t);
        var row = Regex.Match(t, @"sun\s+0-10 m\s+(\S+)\s+(\S+)%\s+(\S+)\s+(\S+)\s+(\S+)\s+(\S+)\s+(\S+)%\s+(\S+)%\s+(\S+)%");
        Assert.IsTrue(row.Success, t);
        Assert.AreEqual("5", row.Groups[1].Value, "rays per frame");
        Assert.AreEqual("2.5", row.Groups[2].Value, "% of primary pixels");
        Assert.AreEqual("150", row.Groups[3].Value, "steps per frame");
        Assert.AreEqual("30.0", row.Groups[4].Value, "avg");
        Assert.AreEqual("30", row.Groups[5].Value, "p50");
        Assert.AreEqual("30", row.Groups[6].Value, "p95");
        Assert.AreEqual("67", row.Groups[7].Value, "tier-0 share");
        Assert.AreEqual("40", row.Groups[8].Value, "blocked");
        Assert.AreEqual("30", row.Groups[9].Value, "leaf");
        StringAssert.Contains("no data", RayStatsReport.Format(null, "x"));
    }

    // ---- Task 248 (WORLD_FILL_BUDGET sec. 5 E1): the primary hit's material split, [6] wood / [7] ground ----

    [Test]
    public void MatClass_TwinsTheShader_WoodIsTask225sClass1()
    {
        string src = File.ReadAllText(Shader).Replace("\r\n", "\n");
        StringAssert.Contains("uint RayStatsMatClass(uint mat)\n{\n" +
            "    if ((mat >= 204u && mat <= 207u) || mat == 147u || mat == 215u || mat == 221u) return 1u;\n" +
            "    if (RayStatsLeaf(mat)) return 0u;\n" +
            "    return (mat >= 128u && mat <= 223u) ? 2u : 0u;\n}", src);
        // The wood set is the same as the shade probe's class 1 (task 225), so the two can never drift apart.
        StringAssert.Contains("uint cls = ((mat >= 204u && mat <= 207u) || mat == 147u || mat == 215u || mat == 221u) ? 1u", src);
        StringAssert.Contains("    if (matCls == 1u) InterlockedAdd(RayStats[b + 6u], 1u);", src);
        StringAssert.Contains("    else if (matCls == 2u) InterlockedAdd(RayStats[b + 7u], 1u);", src);
        // Only the PRIMARY call classifies, and only a voxel hit; the sun and sky calls pass 0u (their [6]/[7] stay 0).
        StringAssert.Contains("(uint)steps, hit, hit && RayStatsLeaf(mat), false, hit ? RayStatsMatClass(mat) : 0u);", src);
        StringAssert.Contains("RayStatsAdd(1u, rsBand, rsSunSteps, rsSunT0, rsSunBlk, rsSunLeaf, rsSunFol, 0u);", src);
        StringAssert.Contains("fillSample <= 0.0, rsLeaf, rsFol, 0u);", src);
        Assert.AreEqual(4, Regex.Matches(src, @"RayStatsAdd\(").Count, "the helper + three call sites");
        // Every id 0..255 lands in exactly one class; counts per class are pinned.
        int leaf = 0, wood = 0, ground = 0, other = 0;
        for (uint m = 0; m < 256u; m++)
        {
            bool isLeaf = (m >= 183u && m <= 186u) || m == 216u;
            int c = RayStatsReport.MatClass(m);
            if (isLeaf) { Assert.AreEqual(0, c, $"leaf {m} is counted in [4], not [6]/[7]"); leaf++; }
            else if (c == 1) wood++;
            else if (c == 2) ground++;
            else other++;
        }
        Assert.AreEqual(5, leaf);
        Assert.AreEqual(7, wood, "147 birch bark, 204-207 woods, 215 bark, 221 cactus");
        Assert.AreEqual(96 - 5 - 7, ground);
        Assert.AreEqual(256 - 96, other);
        Assert.AreEqual(2, RayStatsReport.MatClass(138u), "grass");
        Assert.AreEqual(0, RayStatsReport.MatClass(1u), "engine id (water etc.) -> other");
        Assert.AreEqual(0, RayStatsReport.MatClass(224u), "past the generator table -> other");
    }

    /// A primary ray in (band) that hit (cls: 'l' leaf, 'w' wood, 'g' ground, 'o' other, 'n' no hit), as the shader writes it.
    static void AddPrimary(uint[] d, int band, char cls, int n)
    {
        int o = RayStatsReport.Rec(0, band);
        for (int k = 0; k < n; k++)
        {
            d[o]++; d[o + 1] += 10u; d[o + RayStatsReport.HIST + RayStatsReport.Bin(10u)]++;
            if (cls == 'n') continue;
            d[o + 3]++;
            if (cls == 'l') d[o + RayStatsReport.LEAF]++;
            if (cls == 'w') d[o + RayStatsReport.WOOD]++;
            if (cls == 'g') d[o + RayStatsReport.GROUND]++;
        }
    }

    [Test]
    public void MatSplit_SumsToRays_PerBandAndAll_FormatPrintsScreenShares()
    {
        Assert.AreEqual(4, RayStatsReport.LEAF);
        Assert.AreEqual(6, RayStatsReport.WOOD);
        Assert.AreEqual(7, RayStatsReport.GROUND);
        var d = new uint[RayStatsReport.LEN];
        d[0] = 1; d[1] = 1000;
        AddPrimary(d, 0, 'w', 170); AddPrimary(d, 0, 'g', 60); AddPrimary(d, 0, 'l', 10);              // 24 % of the screen
        AddPrimary(d, 1, 'l', 230); AddPrimary(d, 1, 'w', 50); AddPrimary(d, 1, 'g', 140); AddPrimary(d, 1, 'o', 16); // 43.6 %
        AddPrimary(d, 2, 'g', 95);                                                                        // 9.5 %
        AddPrimary(d, 3, 'n', 200); AddPrimary(d, 3, 'g', 29);                                            // 22.9 %
        // Sun rays with a leaf block and their own [6]/[7] left 0: they must not leak into the primary split.
        int s = RayStatsReport.Rec(1, 1); d[s] = 500; d[s + 3] = 400; d[s + 4] = 300;
        var b1 = RayStatsReport.Mat(d, 1 << 1);
        Assert.AreEqual(436ul, b1.rays); Assert.AreEqual(230ul, b1.leaf); Assert.AreEqual(50ul, b1.wood);
        Assert.AreEqual(140ul, b1.ground); Assert.AreEqual(16ul, b1.other); Assert.AreEqual(0ul, b1.none);
        var all = RayStatsReport.Mat(d, 15);
        Assert.AreEqual(1000ul, all.rays);
        Assert.AreEqual(all.rays, all.leaf + all.wood + all.ground + all.other + all.none, "the classes partition the rays");
        Assert.AreEqual(200ul, all.none); Assert.AreEqual(220ul, all.wood); Assert.AreEqual(240ul, all.leaf);
        string t = RayStatsReport.Format(d, "pose");
        var row = Regex.Match(t, @"\n\s+10-40 m\s+([\d.]+)%\s+([\d.]+)%\s+([\d.]+)%\s+([\d.]+)%\s+([\d.]+)%\s+([\d.]+)%\s+([\d.]+)%\s+([\d.]+)%\s+([\d.]+)%");
        Assert.IsTrue(row.Success, t);
        string[] want = { "43.6", "23.0", "5.0", "14.0", "1.6", "0.0", "23.0", "20.6", "6.6" };
        for (int k = 0; k < want.Length; k++) Assert.AreEqual(want[k], row.Groups[k + 1].Value, $"10-40 m column {k}");
        var a = Regex.Match(t, @"\n\s+all\s+([\d.]+)%\s+([\d.]+)%\s+([\d.]+)%\s+([\d.]+)%\s+([\d.]+)%\s+([\d.]+)%");
        Assert.IsTrue(a.Success, t);
        Assert.AreEqual("100.0", a.Groups[1].Value); Assert.AreEqual("24.0", a.Groups[2].Value);
        Assert.AreEqual("22.0", a.Groups[3].Value); Assert.AreEqual("20.0", a.Groups[6].Value);
        // Adversarial: a corrupt window with more classified hits than hits clamps 'other' to 0, never wraps.
        var bad = new uint[RayStatsReport.LEN];
        int o0 = RayStatsReport.Rec(0, 0); bad[o0] = 5; bad[o0 + 3] = 2; bad[o0 + RayStatsReport.WOOD] = 3;
        Assert.AreEqual(0ul, RayStatsReport.Mat(bad, 1).other);
    }

    [Test]
    public void DiagTwins_FoldTheCascadeOff_SoTheyFitMetalsBufferTable()
    {
        // CSMainFarShadeDiag had 32 Metal buffer slots (cap 31) and never loaded in a player; RayStats made it 33.
        string src = File.ReadAllText(Shader).Replace("\r\n", "\n");
        var m = Regex.Match(src, @"#if defined\(VE_SHELL_DIAG\) && !defined\(VE_TORCH_FLOOD\)\s*\n(//[^\n]*\n)+#define _UseLODCascade 0\s*\n#endif");
        Assert.IsTrue(m.Success, "the *Diag twins fold _UseLODCascade to 0");
        int pair = src.IndexOf("#ifdef VE_DIET2\nstatic const int _UseLODCascade = 0;");
        Assert.IsTrue(pair >= 0 && pair < m.Index, "the macro comes after the uniform's declaration");
        string feat = File.ReadAllText("Assets/CoreEngine/Rendering/RaymarchFeature.cs");
        StringAssert.Contains("bool shellDiag = !diagCascade && ShellDiagKernels(_debugMode)", feat);
        StringAssert.Contains("!ShellKernelDiet || CollectShadowStats || CollectRayStats ||", feat);
    }

    [Test]
    public void PlayKernels_NeverTouchTheCounters_SwitchesDefaultOff()
    {
        Assert.IsFalse(RaymarchFeature.CollectRayStats, "OFF unless a capture turns it on");
        Assert.IsFalse(RaymarchFeature.DebugFreezeTemporal, "OFF unless a capture turns it on");
        string src = File.ReadAllText(Shader).Replace("\r\n", "\n");
        StringAssert.Contains("#ifdef VE_SHELL_DIAG\n#define RAY_STATS_ON (_RayStats != 0)\n#else\n#define RAY_STATS_ON false\n#endif", src);
        StringAssert.Contains("#pragma kernel CSSkyFillTraceDiag VE_FAR_SHADE VE_SKYFILL_TRACE VE_SHELL_DIAG", src);
        // Every use outside the helper's own body (RayStats[ / RayStatsAdd( / RAY_STATS_ON) is inside #ifdef VE_SHELL_DIAG.
        string[] lines = src.Split('\n');
        var stack = new List<string>();
        int uses = 0;
        bool inHelper = false;
        for (int i = 0; i < lines.Length; i++)
        {
            string l = lines[i].Trim();
            if (l.StartsWith("void RayStatsAdd(")) { inHelper = true; continue; }
            if (inHelper) { if (l == "}") inHelper = false; continue; }
            if (l.StartsWith("#ifdef ") || l.StartsWith("#ifndef ") || l.StartsWith("#if ")) { stack.Add(l); continue; }
            if (l.StartsWith("#else") || l.StartsWith("#elif")) { stack[stack.Count - 1] = "!" + stack[stack.Count - 1]; continue; }
            if (l.StartsWith("#endif")) { stack.RemoveAt(stack.Count - 1); continue; }
            if (l.StartsWith("#define RAY_STATS_ON")) continue;
            bool use = l.Contains("RayStats[") || l.Contains("RayStatsAdd(") || l.Contains("RAY_STATS_ON");
            if (!use || l.StartsWith("//")) continue;
            uses++;
            Assert.IsTrue(stack.Contains("#ifdef VE_SHELL_DIAG"), $"Raymarch.compute line {i + 1} uses the counters outside #ifdef VE_SHELL_DIAG: {l}");
        }
        Assert.GreaterOrEqual(uses, 8, "the counter sites were found");
    }
}
