// Assets/CoreEngine/Tests/StreamingRequirementTests.cs
//
// Task 16b: pins StreamingRequirement's formulas on hand-worked cases, and pins
// the LIVE inputs, so a constant change shows up here as a changed requirement
// instead of silently invalidating the KNOWN_ISSUES table.
//
// Hand-worked (r = 10, L = 7, E = 12.8 m, v = 60 m/s, 60 Hz):
//   side 21, face 21 x 7 = 147, layer 21 x 21 = 441, v / E = 4.6875 /s
//   level diagonal   sqrt2 x 4.6875 x 147                = 974.48 /s
//   straight down    4.6875 x 441                         = 2067.19 /s
//   worst 3-D        4.6875 x sqrt(2 x 147^2 + 441^2)
//                  = 4.6875 x sqrt(237699) = 4.6875 x 487.5438 = 2285.36 /s
//   bursts           147 (face), (2 x 21 - 1) x 7 = 287 (corner), 2 x 441 = 882 (Y)
//   band             441 x 7 = 3087; cold fill 3087 / 2285.36 = 1.3508 s
//   margin           (10 - 10) x 12.8 = 0 -> no latency budget (unmeetable)
//   admits / frame   2285.36 / 60 = 38.09
// Prefetch variant (request 13, detail 10): margin 38.4 m,
//   budget 38.4 / 60 - 1/60 = 0.62333 s; level diagonal sqrt2 x 4.6875 x 189 = 1252.90 /s.
using NUnit.Framework;
using VoxelEngine.Streaming;

public class StreamingRequirementTests
{
    private static StreamingRequirement.Inputs Case(int request, int detail) => new StreamingRequirement.Inputs
    {
        vMaxMps = 60f, chunkEdgeM = 12.8f, requestRadiusChunks = request, detailRadiusChunks = detail,
        bandLayers = 7, verticalShiftLayers = 2, frameHz = 60f,
    };

    [Test]
    public void WorstCase_HandWorked_Today()
    {
        var r = StreamingRequirement.Compute(Case(10, 10));
        Assert.AreEqual(21, r.side);
        Assert.AreEqual(974.48, r.aHorizontal, 0.01);
        Assert.AreEqual(2067.19, r.aVertical, 0.01);
        Assert.AreEqual(2285.36, r.aMax, 0.01);
        Assert.AreEqual(147, r.faceBurst);
        Assert.AreEqual(287, r.cornerBurst);
        Assert.AreEqual(882, r.verticalBurst);
        Assert.AreEqual(3087, r.bandChunks);
        Assert.AreEqual(0.0, r.marginM, 1e-9);
        Assert.IsFalse(r.latencyMeetable, "request point == R_d: no throughput meets the worst case");
        Assert.AreEqual(0.0, r.latencyBudgetS, 1e-9);
        Assert.AreEqual(38.09, r.admitsPerFrame, 0.01);
        Assert.AreEqual(1.3508, r.coldFillS, 1e-3);
    }

    [Test]
    public void WorstCase_HandWorked_PrefetchMargin()
    {
        var r = StreamingRequirement.Compute(Case(13, 10));
        Assert.AreEqual(38.4, r.marginM, 1e-4);
        Assert.IsTrue(r.latencyMeetable);
        Assert.AreEqual(0.62333, r.latencyBudgetS, 1e-4);
        Assert.AreEqual(1252.90, r.aHorizontal, 0.01);
    }

    [Test]
    public void SustainedSpeed_InvertsAMax()
    {
        var i = Case(10, 10);
        double cap = StreamingRequirement.Compute(i).aMax;
        Assert.AreEqual(60.0, StreamingRequirement.SustainedSpeedMps(i, cap), 1e-3);
        // 17.2 chunks/s (task 16b: 7 workers at the slow mode's ~406 ms) sustains ~0.45 m/s.
        Assert.AreEqual(17.2 * 12.8 / 487.5438, StreamingRequirement.SustainedSpeedMps(i, 17.2), 1e-4);
    }

    // The live constants this requirement was derived from (KNOWN_ISSUES
    // "STREAMING REQUIREMENT"). If one changes, update that entry's table too.
    [Test]
    public void LiveInputs_MatchTheDerivation()
    {
        var i = StreamingRequirement.Live();
        Assert.AreEqual(12.8f, i.chunkEdgeM, 1e-5f);
        Assert.AreEqual(10, i.requestRadiusChunks);
        Assert.AreEqual(10, i.detailRadiusChunks);
        Assert.AreEqual(7, i.bandLayers);
        Assert.AreEqual(2, i.verticalShiftLayers);
        var r = StreamingRequirement.Compute(i);
        Assert.AreEqual(2285.36, r.aMax, 0.01);
        Assert.IsFalse(r.latencyMeetable);
    }

    [Test]
    public void Verdict_Formats()
    {
        Assert.AreEqual("REQUIRED 100.0/s, CAPACITY 25.0/s -> NOT MET (short by 4.0x)",
            StreamingRequirement.Verdict(100, 25, "/s"));
        Assert.AreEqual("REQUIRED 10.0/s, CAPACITY 25.0/s -> MET", StreamingRequirement.Verdict(10, 25, "/s"));
    }

    // ---- Task 24: the voxel-detail radius at speed ----
    // Hand-worked (load radius 128 m, L_target 0.62 s, 60 Hz -> 0.636667 s per m/s):
    //   v = 0     M = 0                    R_d = 128 m (today's, exactly)
    //   v = 5.2   M = 5.2 x 0.636667 = 3.3107   R_d = 124.6893 m
    //   v = 14    M = 8.9133                    R_d = 119.0867 m
    //   v = 56    M = 35.6533                   R_d = 92.3467 m
    //   v = 60    M = 38.2                      R_d = 89.8 m = 7.0156 chunks (approved: ~7, ~90 m)
    //   v = 120   M = 76.4 -> 51.6 < floor      R_d = 64 m; budget 64/120 - 1/60 = 0.51667 s
    private const double LOAD_M = 128.0;

    [Test]
    public void DetailRadius_AtRest_IsTodaysExactly()
    {
        Assert.AreEqual(LOAD_M, StreamingRequirement.DetailRadiusM(0.0, LOAD_M, 60.0));
        Assert.AreEqual(LOAD_M, StreamingRequirement.DetailRadiusM(-1.0, LOAD_M, 60.0));
        Assert.AreEqual(LOAD_M, StreamingRequirement.DetailRadiusM(double.NaN, LOAD_M, 60.0));
        // ...and that is the Live() R_d with the flag off (no margin).
        var r = StreamingRequirement.Compute(Case(10, 10));
        Assert.AreEqual(10 * 12.8, LOAD_M - r.marginM, 1e-4);
    }

    [Test]
    public void DetailRadius_HandWorked()
    {
        Assert.AreEqual(124.6893, StreamingRequirement.DetailRadiusM(5.2, LOAD_M, 60.0), 1e-3);
        Assert.AreEqual(119.0867, StreamingRequirement.DetailRadiusM(14.0, LOAD_M, 60.0), 1e-3);
        Assert.AreEqual(92.3467, StreamingRequirement.DetailRadiusM(56.0, LOAD_M, 60.0), 1e-3);
        double rd60 = StreamingRequirement.DetailRadiusM(60.0, LOAD_M, 60.0);
        Assert.AreEqual(89.8, rd60, 1e-6);
        Assert.AreEqual(7.0156, rd60 / 12.8, 1e-3);
        Assert.AreEqual(64.0, StreamingRequirement.DetailRadiusM(120.0, LOAD_M, 60.0), 1e-9, "floor");
        // Continuous and monotone: walking barely moves it.
        Assert.Less(LOAD_M - StreamingRequirement.DetailRadiusM(5.2, LOAD_M, 60.0), 3.4);
    }

    [Test]
    public void DetailRadius_LeavesTheTargetBudget()
    {
        foreach (double v in new[] { 1.0, 5.2, 14.0, 56.0, 60.0, 100.0 })
        {
            double rd = StreamingRequirement.DetailRadiusM(v, LOAD_M, 60.0);
            Assert.AreEqual(StreamingRequirement.DETAIL_LATENCY_TARGET_S,
                StreamingRequirement.LatencyBudgetS(v, LOAD_M, rd, 60.0), 1e-9, $"v {v}");
        }
        Assert.AreEqual(0.51667, StreamingRequirement.LatencyBudgetS(120.0, LOAD_M, 64.0, 60.0), 1e-4, "below the floor the budget shrinks");
        Assert.AreEqual(0.0, StreamingRequirement.LatencyBudgetS(60.0, LOAD_M, LOAD_M, 60.0), "today: no budget");
        // Compute() with the metre override: margin 38.2 m, budget 0.62 s.
        var i = Case(10, 10);
        i.detailRadiusM = 89.8;
        var r = StreamingRequirement.Compute(i);
        Assert.AreEqual(38.2, r.marginM, 1e-4);
        Assert.IsTrue(r.latencyMeetable);
        Assert.AreEqual(0.62, r.latencyBudgetS, 1e-6);
    }

    [Test]
    public void DetailSpeedFilter_StillIsExactlyZero()
    {
        var f = new DetailSpeedFilter();
        float t = 0f;
        for (int k = 0; k < 200; k++) { t += (k % 3 == 0) ? 0.031f : 0.012f; f.Push(t, new Unity.Mathematics.float2(4000.25f, -812.5f)); }
        Assert.AreEqual(0f, f.SpeedMps);
    }

    [Test]
    public void DetailSpeedFilter_IgnoresFrameJitter()
    {
        // 60 m/s along a line with frame times alternating 8 / 33 ms: distance
        // follows time, so every window reads 60 exactly (to float rounding).
        var f = new DetailSpeedFilter();
        double t = 0, x = 0;
        for (int k = 0; k < 300; k++)
        {
            double dt = (k & 1) == 0 ? 0.008 : 0.033;
            t += dt; x += 60.0 * dt;
            f.Push((float)t, new Unity.Mathematics.float2((float)x, 0f));
            if (k > 2) Assert.AreEqual(60f, f.SpeedMps, 0.05f, $"frame {k}");
        }
    }

    [Test]
    public void DetailSpeedFilter_OneFastFrameIsBounded()
    {
        // Walking 5.2 m/s at 60 Hz, then ONE frame jumps 2 m (e.g. a step-up snap):
        // the mean rises by at most ~2 m / 0.5 s = 4 m/s, and only for 0.5 s.
        var f = new DetailSpeedFilter();
        float t = 0f, x = 0f;
        for (int k = 0; k < 120; k++) { t += 1f / 60f; x += 5.2f / 60f; f.Push(t, new Unity.Mathematics.float2(x, 0f)); }
        t += 1f / 60f; x += 2f; f.Push(t, new Unity.Mathematics.float2(x, 0f));
        Assert.Less(f.SpeedMps, 5.2f + 4.1f);
        for (int k = 0; k < 40; k++) { t += 1f / 60f; x += 5.2f / 60f; f.Push(t, new Unity.Mathematics.float2(x, 0f)); }
        Assert.AreEqual(5.2f, f.SpeedMps, 0.01f, "the spike has left the window");
        // A teleport (one frame > 128 m) restarts the window instead of reading 7,000 m/s.
        t += 1f / 60f; x += 500f; f.Push(t, new Unity.Mathematics.float2(x, 0f));
        Assert.AreEqual(0f, f.SpeedMps);
    }
}
