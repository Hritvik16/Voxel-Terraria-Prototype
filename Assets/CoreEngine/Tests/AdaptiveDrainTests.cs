// Assets/CoreEngine/Tests/AdaptiveDrainTests.cs
//
// Task 97, option B of FLUID_TICK_DIAGNOSIS.md section 3: ADAPTIVE DRAIN. A
// landed op-list batch of at most FluidOpListReadback.WholeBatchMaxOps ops is
// applied whole in the frame it lands; bigger batches keep the §8.5 slicing at
// MaxOpsAppliedPerFrame. Pins OpDrainCursor.BudgetFor (the only new logic) and
// what it does to the tick period:
//
//   SMALL WHOLE   -- a batch <= the threshold drains in ONE frame.
//   BIG UNCHANGED -- a batch over it is sliced exactly as before (same slices,
//                    same frame count), so the 2026-09-11 siege is untouched.
//   OFF           -- threshold 0 = the pre-97 drain, for every batch size.
//   EXACTLY ONCE  -- every op still yielded once, in order, across batches.
//   PERIOD        -- with the diagnosis's ~3,100 ops/tick, the tick period
//                    drops by the drain (latency + drain - 1 + gap frames), and
//                    the fixed-rate scheduler at DefaultRateHz then has no
//                    shortfall where the old drain had one.
//
// The apply itself needs a ComputeShader (not in batchmode), so this pins the
// arithmetic Playground's readback runs, as OpDrainCursorTests does.

using NUnit.Framework;
using System.Collections.Generic;
using VoxelEngine.Simulation;

public class AdaptiveDrainTests
{
    private const int PerFrame = FluidOpListReadback.MaxOpsAppliedPerFrameDefault;     // 1024
    private const int Whole = FluidOpListReadback.WholeBatchMaxOpsDefault;             // 4096

    /// Drains one batch the way PumpAndApply does: one Take per frame with the
    /// budget BudgetFor gives for the batch. Returns the frames it took and the
    /// slice boundaries.
    private static int DrainFrames(int count, int wholeMax, List<int> slices = null, List<int> seen = null)
    {
        var c = new OpDrainCursor();
        c.Stage(count);
        int frames = 0;
        while (!c.IsEmpty && frames < count + 4)
        {
            Assert.IsTrue(c.Take(OpDrainCursor.BudgetFor(c.Count, PerFrame, wholeMax), out int from, out int to));
            frames++;
            slices?.Add(to);
            if (seen != null) for (int i = from; i < to; i++) seen.Add(i);
        }
        Assert.IsTrue(c.IsEmpty, $"batch {count}: {c.Remaining} ops left after {frames} frames");
        return frames;
    }

    [Test]
    public void SmallBatch_DrainsInOneFrame()
    {
        foreach (int n in new[] { 1, 200, PerFrame, PerFrame + 1, 3100, Whole })
            Assert.AreEqual(1, DrainFrames(n, Whole), $"batch {n} <= {Whole} must apply whole");
        Assert.AreEqual(4, DrainFrames(3100, 0), "the diagnosis's 3,100-op batch took 4 frames before option B");
    }

    [Test]
    public void BigBatch_SlicedExactlyAsBefore()
    {
        foreach (int n in new[] { Whole + 1, 5000, 32768, 65536 })
        {
            var on = new List<int>(); var off = new List<int>();
            int fOn = DrainFrames(n, Whole, on), fOff = DrainFrames(n, 0, off);
            Assert.AreEqual(fOff, fOn, $"batch {n}: frames differ");
            CollectionAssert.AreEqual(off, on, $"batch {n}: slice boundaries differ");
            Assert.AreEqual((n + PerFrame - 1) / PerFrame, fOn, $"batch {n}: not 1024 per frame");
        }
    }

    [Test]
    public void Off_IsThePre97Budget_ForEveryBatch()
    {
        foreach (int n in new[] { 0, 1, 200, 1024, 3100, 4096, 70000 })
        {
            Assert.AreEqual(PerFrame, OpDrainCursor.BudgetFor(n, PerFrame, 0), $"batch {n}, threshold 0");
            Assert.AreEqual(PerFrame, OpDrainCursor.BudgetFor(n, PerFrame, -1), $"batch {n}, threshold -1");
        }
        // A batch already under the per-frame budget is unchanged by option B too.
        Assert.AreEqual(PerFrame, OpDrainCursor.BudgetFor(200, PerFrame, Whole));
        Assert.AreEqual(Whole, OpDrainCursor.BudgetFor(Whole, PerFrame, Whole));
        Assert.AreEqual(PerFrame, OpDrainCursor.BudgetFor(Whole + 1, PerFrame, Whole));
        Assert.Greater(Whole, PerFrame, "the threshold must exceed the per-frame budget or option B does nothing");
    }

    [Test]
    public void EveryOp_ExactlyOnce_InOrder_AcrossMixedBatches()
    {
        foreach (int n in new[] { 0, 1, 1023, 1025, 3100, 4096, 4097, 9000 })
        {
            var seen = new List<int>();
            DrainFrames(n, Whole, null, seen);
            Assert.AreEqual(n, seen.Count, $"batch {n}");
            for (int i = 0; i < n; i++) Assert.AreEqual(i, seen[i], $"batch {n}: op {i} missing, repeated or out of order");
        }
    }

    /// Frames from one issue to the next in Playground's order (ShouldIssue on
    /// CanIssue, then PumpAndApply): the batch lands `latency` frames after the
    /// issue, drains in `drain` frames counting the landing one, and CanIssue is
    /// first seen true the frame after (gap 1). Section 1: latency + drain - 1 + gap.
    private static int PeriodFrames(int latency, int ops, int wholeMax) => latency + DrainFrames(ops, wholeMax) - 1 + 1;

    [Test]
    public void TickPeriod_DropsByTheDrain()
    {
        // Section 1's arm A: ~4 frames latency (histogram mode), 3,109 ops per tick:
        // 7.25 measured frames per tick, the model gives 4 + 4 - 1 + 1 = 8.
        Assert.AreEqual(8, PeriodFrames(4, 3109, 0));
        Assert.AreEqual(5, PeriodFrames(4, 3109, Whole), "option B: latency + 1-frame drain - 1 + gap");
        // Light batches (the arena pour, ~200 ops) were already one frame: no change.
        Assert.AreEqual(PeriodFrames(4, 200, 0), PeriodFrames(4, 200, Whole));
    }

    /// The fixed-rate scheduler over a fake pipeline whose period is
    /// `periodFrames`: issued and missed ticks over `seconds` at `fps`.
    private static (long issued, long missed) Schedule(float rate, double fps, int periodFrames, double seconds)
    {
        var s = new FluidTickScheduler(rate);
        int busyUntil = 0;
        double now = 0.0;
        for (int f = 0; now < seconds; f++, now += 1.0 / fps)
            if (s.ShouldIssue(now, f >= busyUntil)) busyUntil = f + periodFrames;
        return (s.IssuedTicks, s.MissedTicks);
    }

    [Test]
    public void DefaultRate_NeedsOptionB_ForHeavyBatches_At30Fps()
    {
        // 30 FPS, latency 3 frames (FLUID_TICK_DIAGNOSIS: 3.6-3.8 mean, task 97's
        // arena runs), 3,100 ops/tick: the old drain gives 3 + 4 = 7 frames = 4.3/s,
        // option B 3 + 1 = 4 frames = 7.5/s.
        float R = FluidTickScheduler.DefaultRateHz;
        var off = Schedule(R, 30.0, PeriodFrames(3, 3100, 0), 60.0);
        var on = Schedule(R, 30.0, PeriodFrames(3, 3100, Whole), 60.0);
        Assert.AreEqual(0, on.missed, $"option B at 30 FPS must deliver R = {R} (issued {on.issued})");
        Assert.GreaterOrEqual(on.issued, (long)(R * 60.0) - 1);
        Assert.Greater(off.missed, 0, $"without option B R = {R} must fall short at 30 FPS (the reason for B)");
    }
}
