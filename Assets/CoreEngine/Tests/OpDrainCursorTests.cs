// Assets/CoreEngine/Tests/OpDrainCursorTests.cs
//
// The three properties §8.5's frame budget on the fluid apply depends on, and
// which nothing else can catch:
//
//   NEVER DROP    -- carrying work forward is only correct if the carried part
//                    actually runs later. An off-by-one on a half-open range
//                    loses one op per batch, silently, forever.
//   NEVER REPEAT  -- the mirror failure. Re-applying an op is a mass GAIN,
//                    which is the defect class FluidReadbackInvariantTests
//                    already exists over.
//   NEVER REORDER -- two writes to the same voxel in one batch must resolve in
//                    the order the CA emitted them, or the final state differs
//                    from what the simulation intended.
//
// The apply path itself needs a ComputeShader and cannot run in batchmode, so
// these pin the arithmetic rather than the GPU. That split is deliberate: the
// arithmetic is where a carry-forward bug actually lives.

using NUnit.Framework;
using System;
using System.Collections.Generic;
using VoxelEngine.Simulation;

public class OpDrainCursorTests
{
    /// Drains a staged batch with a fixed budget, returning every index in the
    /// order it was handed out.
    private static List<int> DrainAll(int count, int budget, out int takes)
    {
        var cursor = new OpDrainCursor();
        cursor.Stage(count);
        var seen = new List<int>();
        takes = 0;
        // Bounded so a cursor that never advances fails as a hang-free
        // assertion rather than spinning the test runner forever.
        for (int guard = 0; guard < count + 16; guard++)
        {
            if (!cursor.Take(budget, out int from, out int to)) break;
            takes++;
            for (int i = from; i < to; i++) seen.Add(i);
        }
        Assert.IsTrue(cursor.IsEmpty, $"cursor still holds {cursor.Remaining} after draining");
        return seen;
    }

    [Test]
    public void EveryOpIsYieldedExactlyOnce_AtEveryBudget()
    {
        foreach (int count in new[] { 0, 1, 2, 7, 64, 4096, 65536 })
            foreach (int budget in new[] { 1, 2, 3, 7, 4096, int.MaxValue })
            {
                var seen = DrainAll(count, budget, out _);
                Assert.AreEqual(count, seen.Count,
                    $"count={count} budget={budget}: yielded {seen.Count} of {count}");
                for (int i = 0; i < count; i++)
                    Assert.AreEqual(i, seen[i],
                        $"count={count} budget={budget}: index {i} missing, repeated or out of order");
            }
    }

    [Test]
    public void OrderIsStrictlyIncreasing_AcrossCarryForward()
    {
        // The carry boundary is where a reorder would appear -- within one
        // Take the loop is trivially ordered.
        var seen = DrainAll(1000, 7, out int takes);
        Assert.Greater(takes, 1, "budget 7 over 1000 ops must carry forward many times");
        for (int i = 1; i < seen.Count; i++)
            Assert.AreEqual(seen[i - 1] + 1, seen[i], $"order broken at {i}");
    }

    [Test]
    public void ABudgetLargerThanTheBatch_TakesItAllAndDoesNotOverrun()
    {
        var cursor = new OpDrainCursor();
        cursor.Stage(10);
        Assert.IsTrue(cursor.Take(999, out int from, out int to));
        Assert.AreEqual(0, from);
        Assert.AreEqual(10, to, "must not hand out indices past the staged count");
        Assert.IsTrue(cursor.IsEmpty);
    }

    [Test]
    public void AnEmptyOrExhaustedCursorYieldsNothing()
    {
        var cursor = new OpDrainCursor();
        Assert.IsFalse(cursor.Take(100, out _, out _), "nothing staged");

        cursor.Stage(3);
        Assert.IsTrue(cursor.Take(100, out _, out _));
        Assert.IsFalse(cursor.Take(100, out _, out _), "already drained");
    }

    [Test]
    public void ANonPositiveBudgetTakesNothing_AndKeepsTheOpsForLater()
    {
        // A zero budget must STALL, not discard. If it dropped the batch the
        // ops would be gone with nothing recording it.
        var cursor = new OpDrainCursor();
        cursor.Stage(5);
        Assert.IsFalse(cursor.Take(0, out _, out _));
        Assert.AreEqual(5, cursor.Remaining, "a zero budget must not consume ops");
        Assert.IsTrue(cursor.Take(int.MaxValue, out _, out int to));
        Assert.AreEqual(5, to);
    }

    [Test]
    public void StagingOverAnUndrainedBatchIsRefused()
    {
        // THE BOUND ON THE BUFFER. PumpAndApply refuses to take another
        // readback while ops are pending, and CanIssue refuses to let the CA
        // tick. If those ever regress, this throws instead of silently
        // overwriting a partly-applied batch and losing its tail.
        var cursor = new OpDrainCursor();
        cursor.Stage(10);
        cursor.Take(4, out _, out _);
        Assert.AreEqual(6, cursor.Remaining);
        Assert.Throws<InvalidOperationException>(() => cursor.Stage(10));
    }

    [Test]
    public void WouldCarry_AgreesWithWhatTakeActuallyDoes()
    {
        // The counters BudgetLimitedFrames / BatchesCarriedForward are derived
        // from WouldCarry, so a disagreement would mis-report how often the
        // budget bit -- and that figure is the evidence the fix works.
        foreach (int count in new[] { 1, 10, 100 })
            foreach (int budget in new[] { 1, 9, 10, 11, 1000 })
            {
                var cursor = new OpDrainCursor();
                cursor.Stage(count);
                bool predicted = cursor.WouldCarry(budget);
                cursor.Take(budget, out _, out _);
                Assert.AreEqual(predicted, !cursor.IsEmpty,
                    $"count={count} budget={budget}: WouldCarry said {predicted}");
            }
    }

    [Test]
    public void TheDefaultBudgetIsBounded_AndSmallEnoughToSplitARealBatch()
    {
        // A default equal to (or above) the op-list cap would make the budget
        // a no-op while looking present -- the shape of fix that reports
        // success and changes nothing.
        Assert.Greater(FluidOpListReadback.MaxOpsAppliedPerFrameDefault, 0);
        Assert.Less(FluidOpListReadback.MaxOpsAppliedPerFrameDefault, 65536,
            "the budget must be able to actually split a full op-list batch");
    }
}
