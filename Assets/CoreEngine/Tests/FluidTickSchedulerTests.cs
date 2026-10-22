// Assets/CoreEngine/Tests/FluidTickSchedulerTests.cs
//
// Engine step 17 (task 91): the fluid CA ticks at a FIXED rate, independent of
// frame rate. Pins FluidTickScheduler against fake clocks and a fake pipeline:
//
//   SAME RATE     -- 30 and 60 FPS issue the same ticks per simulated second.
//   NEVER ABOVE R -- T seconds never issue more than R*T + 1 ticks, and no
//                    window bursts after a stall.
//   BACK-PRESSURE -- never a tick while canIssue is false (the one op-list in
//                    flight bound, FluidOpListReadback.CanIssue).
//   SHORTFALL     -- when the frame rate cannot deliver R, the missing ticks
//                    are counted, not silently lost.
//
// The fake pipeline refuses a new tick for `busyFrames` frames after each one,
// the shape FLUID_TICK_DIAGNOSIS.md section 1 measured (latency + drain ~4.6-7
// frames per tick).

using NUnit.Framework;
using System;
using System.Collections.Generic;
using VoxelEngine.Simulation;

public class FluidTickSchedulerTests
{
    private struct Run
    {
        public long Issued, Missed;
        public List<double> Times;
    }

    /// Steps a scheduler for `seconds` at frame times from `dt(frame)`, with a
    /// pipeline busy for `busyFrames` frames after each issued tick.
    private static Run Simulate(FluidTickScheduler s, double seconds, Func<int, double> dt, int busyFrames)
    {
        var times = new List<double>();
        double now = 0.0;
        int busyUntil = 0;
        for (int f = 0; now < seconds; f++)
        {
            bool canIssue = f >= busyUntil;
            if (s.ShouldIssue(now, canIssue))
            {
                Assert.IsTrue(canIssue, $"issued at frame {f} while canIssue was false");
                times.Add(now);
                busyUntil = f + busyFrames;
            }
            now += dt(f);
        }
        return new Run { Issued = s.IssuedTicks, Missed = s.MissedTicks, Times = times };
    }

    private static void AssertNeverAboveR(Run r, float rate, double seconds, string label)
    {
        Assert.LessOrEqual(r.Issued, (long)Math.Floor(rate * seconds) + 1, $"{label}: {r.Issued} ticks in {seconds} s exceeds R*T+1");
        // Any 1 s window: fewer than R + 2 (at most one banked tick).
        for (int i = 0; i < r.Times.Count; i++)
        {
            int n = 0;
            for (int j = i; j < r.Times.Count && r.Times[j] < r.Times[i] + 1.0; j++) n++;
            Assert.Less(n, rate + 2f, $"{label}: {n} ticks in the 1 s window from {r.Times[i]:F3} s");
        }
    }

    [Test]
    public void SameTicksPerSecond_At30And60Fps()
    {
        const double T = 60.0;
        float R = FluidTickScheduler.DefaultRateHz;
        // Pipeline busy 4 frames per tick at either frame rate (latency is
        // fixed in FRAMES, section 1; task 97 measured latency 2 + a one-frame
        // drain (option B) + gap 1 = 3-4.5 frames): capacity 7.5/s at 30 FPS,
        // 15/s at 60. Was 5 frames (6/s at 30) while R was 4.5.
        var r30 = Simulate(new FluidTickScheduler(), T, _ => 1.0 / 30.0, 4);
        var r60 = Simulate(new FluidTickScheduler(), T, _ => 1.0 / 60.0, 4);
        Assert.LessOrEqual(Math.Abs(r30.Issued - r60.Issued), 1, $"30 FPS {r30.Issued} vs 60 FPS {r60.Issued} ticks in {T} s");
        Assert.GreaterOrEqual(r30.Issued, (long)Math.Floor(R * T) - 1, $"30 FPS delivered {r30.Issued} of {R * T}");
        Assert.AreEqual(0, r30.Missed, "30 FPS has the capacity for R: nothing may be missed");
        Assert.AreEqual(0, r60.Missed, "60 FPS has the capacity for R: nothing may be missed");
        AssertNeverAboveR(r30, R, T, "30 FPS");
        AssertNeverAboveR(r60, R, T, "60 FPS");
    }

    [Test]
    public void JitteryFrameTimes_StillDeliverR_AndNeverExceedIt()
    {
        const double T = 60.0;
        float R = FluidTickScheduler.DefaultRateHz;
        var rng = new System.Random(91);
        var dts = new double[100000];
        for (int i = 0; i < dts.Length; i++) dts[i] = 0.010 + rng.NextDouble() * 0.020; // 33-100 FPS
        var r = Simulate(new FluidTickScheduler(), T, f => dts[f], 4);
        AssertNeverAboveR(r, R, T, "jitter");
        Assert.GreaterOrEqual(r.Issued + r.Missed, (long)Math.Floor(R * T) - 1, "issued + missed must account for every due tick");
        Assert.GreaterOrEqual(r.Issued, (long)Math.Floor(R * T * 0.97), $"jitter delivered {r.Issued} of {R * T}");
    }

    [Test]
    public void LowFrameRate_CountsShortfall_NeverBursts()
    {
        const double T = 60.0;
        float R = FluidTickScheduler.DefaultRateHz;
        // 15 FPS, 5 busy frames: capacity 3 ticks/s < R. Fluid slows, visibly.
        var r = Simulate(new FluidTickScheduler(), T, _ => 1.0 / 15.0, 5);
        Assert.Less(r.Issued, (long)(R * T), "15 FPS cannot deliver R");
        Assert.Greater(r.Missed, 0, "the shortfall must be counted");
        long due = (long)Math.Floor(R * T) + 1;
        Assert.LessOrEqual(Math.Abs(r.Issued + r.Missed - due), 2, $"issued {r.Issued} + missed {r.Missed} vs due {due}");
        AssertNeverAboveR(r, R, T, "15 FPS");

        // A 3 s hitch: the missed ticks are dropped and counted, not replayed.
        var s = new FluidTickScheduler();
        var hitch = Simulate(s, 10.0, f => f == 100 ? 3.0 : 1.0 / 60.0, 5);
        Assert.GreaterOrEqual(hitch.Missed, (long)(3.0 * R) - 1, "a 3 s hitch drops ~13 ticks");
        AssertNeverAboveR(hitch, R, 10.0, "hitch");
    }

    [Test]
    public void NeverIssues_WhilePipelineRefuses()
    {
        var s = new FluidTickScheduler();
        for (int f = 0; f < 600; f++)
            Assert.IsFalse(s.ShouldIssue(f / 60.0, false), $"frame {f}: issued with canIssue false");
        Assert.AreEqual(0, s.IssuedTicks);
        Assert.Greater(s.BlockedFrames, 0);
        Assert.Greater(s.MissedTicks, 0, "10 s of refusals is a counted shortfall");
    }

    [Test]
    public void Legacy_IssuesWheneverPipelineAllows()
    {
        // -fluidhz 0: decision 11's old behaviour, rate follows FPS.
        var r30 = Simulate(new FluidTickScheduler(0f), 10.0, _ => 1.0 / 30.0, 5);
        var r60 = Simulate(new FluidTickScheduler(0f), 10.0, _ => 1.0 / 60.0, 5);
        Assert.AreEqual(60, r30.Issued, 1);
        Assert.AreEqual(120, r60.Issued, 1);
    }
}
