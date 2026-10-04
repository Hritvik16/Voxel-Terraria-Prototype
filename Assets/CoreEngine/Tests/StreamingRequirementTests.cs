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
}
