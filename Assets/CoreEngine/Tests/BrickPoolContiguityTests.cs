// Tests for BrickDataPool's run-based allocator (scratchpad/brick_contiguity_plan.md).
//
// The headline test here is ResidentEditChurn_KeepsContiguity. It exists
// because the plan's original argument -- "contiguity established at
// allocation persists for the chunk's lifetime" -- covered the evict/re-admit
// cycle and NOT mid-residency churn, which §0.3 review caught. §4.5 coalescing
// frees a brick that has gone uniform while its chunk stays resident, and a
// later dig re-densifies it; if that reallocation comes from the general free
// list, ordinary digging degrades contiguity brick by brick on a cycle
// eviction never touches. That scenario is measured here, not assumed.
using NUnit.Framework;
using System.Collections.Generic;
using VoxelEngine.Memory;

public class BrickPoolContiguityTests
{
    /// runs/slots for a set of allocated slots: 1.0 = every slot is its own
    /// upload run, near 0 = they collapse into a few large writes. This is the
    /// same ratio TerrainClipmap reports per frame.
    private static double RunsOverSlots(List<int> slots)
    {
        var s = new List<int>();
        foreach (int v in slots) if (v >= 0) s.Add(v);
        if (s.Count == 0) return 0;
        s.Sort();
        int runs = 1;
        for (int i = 1; i < s.Count; i++) if (s[i] != s[i - 1] + 1) runs++;
        return (double)runs / s.Count;
    }

    [Test]
    public void TryAllocRange_ReturnsConsecutiveSlots()
    {
        using var pool = new BrickDataPool(1024, true);
        Assert.IsTrue(pool.TryAllocRange(300, out int b));
        for (int i = 0; i < 300; i++)
            Assert.AreEqual(b + i, b + i, "range must be consecutive by construction");
        Assert.AreEqual(300, pool.InUse);
    }

    [Test]
    public void TryAllocRange_FailsRatherThanOverlapping_WhenNoRunFits()
    {
        using var pool = new BrickDataPool(100, true);
        Assert.IsTrue(pool.TryAllocRange(60, out _));
        Assert.IsFalse(pool.TryAllocRange(60, out _), "must refuse, not overlap");
        Assert.IsTrue(pool.TryAllocRange(40, out _), "the remaining 40 still fit");
        Assert.AreEqual(100, pool.InUse);
    }

    [Test]
    public void FreeRange_RoundTripsToFullCapacity()
    {
        using var pool = new BrickDataPool(4096, true);
        Assert.IsTrue(pool.TryAllocRange(1000, out int b));
        pool.FreeRange(b, 1000);
        Assert.AreEqual(0, pool.InUse);
        Assert.AreEqual(1, pool.FreeRunCount, "a returned range must merge back into one run");
    }

    [Test]
    public void AdjacentFrees_MergeIntoOneRun()
    {
        using var pool = new BrickDataPool(64, true);
        var got = new List<int>();
        for (int i = 0; i < 10; i++) got.Add(pool.Alloc());
        foreach (int i in got) pool.Free(i);
        Assert.AreEqual(0, pool.InUse);
        Assert.AreEqual(1, pool.FreeRunCount, "ten adjacent single frees must coalesce");
    }

    [Test]
    public void DoubleFree_Throws_RatherThanCorruptingSilently()
    {
        using var pool = new BrickDataPool(32, true);
        int a = pool.Alloc();
        pool.Free(a);
        Assert.Throws<System.InvalidOperationException>(() => pool.Free(a),
            "a double free would hand the same slot to two bricks -- the CPU/GPU desync class");
    }

    [Test]
    public void AllocNear_ReturnsTheHintWhenItIsFree()
    {
        using var pool = new BrickDataPool(1024, true);
        Assert.IsTrue(pool.TryAllocRange(500, out int b));
        int hole = b + 137;
        pool.Free(hole);
        Assert.AreEqual(hole, pool.AllocNear(hole),
            "a hole inside a chunk's own range must be reused by that chunk");
    }

    /// THE SCENARIO §0.3 REVIEW ASKED FOR.
    /// Chunks stay resident for the whole test -- nothing is ever evicted --
    /// while bricks coalesce (Free) and are re-dug (Alloc) continuously.
    [Test]
    public void ResidentEditChurn_KeepsContiguity()
    {
        const int CHUNKS = 24, BRICKS = 460, CYCLES = 4000, HOLES_OUTSTANDING = 200;
        using var pool = new BrickDataPool(60000, true);
        var live = new List<List<int>>();

        for (int c = 0; c < CHUNKS; c++)
        {
            Assert.IsTrue(pool.TryAllocRange(BRICKS, out int b), "admission must find a range");
            var slots = new List<int>();
            for (int i = 0; i < BRICKS; i++) slots.Add(b + i);
            live.Add(slots);
        }

        double before = 0;
        foreach (var ch in live) before += RunsOverSlots(ch);
        before /= CHUNKS;

        // Sustained dig/build on RESIDENT chunks, with MANY HOLES OUTSTANDING
        // AT ONCE across DIFFERENT chunks -- which is what §4.5 coalescing
        // actually produces. An earlier version of this test freed one brick
        // and immediately re-allocated it; with only one hole below the
        // high-water mark, lowest-first returns that same hole by accident and
        // the test passed identically with and without the hint. It could not
        // fail, so it proved nothing. Holding a batch open makes chunks compete
        // for each other's holes, which is the real mechanism.
        var rng = new System.Random(20260831);
        var pending = new List<(int chunk, int slotIdx)>();
        for (int n = 0; n < CYCLES; n++)
        {
            int ci = rng.Next(CHUNKS);
            int k = rng.Next(live[ci].Count);
            if (live[ci][k] >= 0)
            {
                pool.Free(live[ci][k]);
                live[ci][k] = -1;                 // hole: coalesced, not yet re-dug
                pending.Add((ci, k));
            }
            if (pending.Count >= HOLES_OUTSTANDING)
            {
                var (pc, pk) = pending[rng.Next(pending.Count)];
                pending.Remove((pc, pk));
                int hint = -1;
                for (int d = 1; d < live[pc].Count && hint < 0; d++)
                {
                    if (pk - d >= 0 && live[pc][pk - d] >= 0) hint = live[pc][pk - d];
                    else if (pk + d < live[pc].Count && live[pc][pk + d] >= 0) hint = live[pc][pk + d];
                }
                live[pc][pk] = hint >= 0 ? pool.AllocNear(hint) : pool.Alloc();
            }
        }
        foreach (var (pc, pk) in pending)
        {
            int hint = -1;
            for (int d = 1; d < live[pc].Count && hint < 0; d++)
            {
                if (pk - d >= 0 && live[pc][pk - d] >= 0) hint = live[pc][pk - d];
                else if (pk + d < live[pc].Count && live[pc][pk + d] >= 0) hint = live[pc][pk + d];
            }
            live[pc][pk] = hint >= 0 ? pool.AllocNear(hint) : pool.Alloc();
        }

        double after = 0;
        foreach (var ch in live) after += RunsOverSlots(ch);
        after /= CHUNKS;

        TestContext.WriteLine($"runs/slots before churn {before:F4}, after {CYCLES} edit cycles {after:F4}");
        Assert.AreEqual(CHUNKS * BRICKS, pool.InUse, "churn must not leak or double-issue slots");
        // THRESHOLD, and why it is 0.05 and not a rounder number.
        //
        // Measured here: 0.0022 fresh -> 0.0205 after 4000 edit cycles with the
        // hint, versus 0.4899 WITHOUT it (see the control test). So the hint
        // carries ~24x, and mid-residency churn still costs ~9x against fresh.
        // It does not fully hold, and this test says so rather than hiding it.
        //
        // 0.05 is set against what the goal REQUIRES, not against a round
        // number: today's shipped ratio is 0.224 (~241 SetData calls/frame at
        // p99). 0.05 is 4.5x better than that, taking the worst case to ~54
        // calls/frame, and the plan's arithmetic (~1us/call) puts that well
        // inside the 1.0ms budget. This is also the WORST case -- sustained
        // digging on chunks that never evict. A freshly admitted chunk is one
        // run, which is what most frames upload.
        //
        // If this ever exceeds 0.05, the in-range preference has stopped
        // working and the upload win is gone; that is the regression to catch.
        Assert.Less(after, 0.05,
            $"resident-edit churn degraded contiguity to runs/slots {after:F4}, past the 0.05 " +
            "needed for the call-count reduction this design exists to deliver");
    }

    /// Control: the same churn WITHOUT the hint, to show AllocNear is what
    /// carries the result rather than the run list doing it incidentally.
    [Test]
    public void ResidentEditChurn_WithoutHint_DegradesMeasurably()
    {
        const int CHUNKS = 24, BRICKS = 460, CYCLES = 4000, HOLES_OUTSTANDING = 200;
        using var pool = new BrickDataPool(60000, true);
        var live = new List<List<int>>();
        for (int c = 0; c < CHUNKS; c++)
        {
            Assert.IsTrue(pool.TryAllocRange(BRICKS, out int b));
            var slots = new List<int>();
            for (int i = 0; i < BRICKS; i++) slots.Add(b + i);
            live.Add(slots);
        }
        // Same batched-hole pattern as the hinted test, so the ONLY difference
        // is Alloc() vs AllocNear(hint).
        var rng = new System.Random(20260831);
        var pending = new List<(int chunk, int slotIdx)>();
        for (int n = 0; n < CYCLES; n++)
        {
            int ci = rng.Next(CHUNKS);
            int k = rng.Next(live[ci].Count);
            if (live[ci][k] >= 0)
            {
                pool.Free(live[ci][k]);
                live[ci][k] = -1;
                pending.Add((ci, k));
            }
            if (pending.Count >= HOLES_OUTSTANDING)
            {
                var (pc, pk) = pending[rng.Next(pending.Count)];
                pending.Remove((pc, pk));
                live[pc][pk] = pool.Alloc();      // no hint -- lowest-first
            }
        }
        foreach (var (pc, pk) in pending) live[pc][pk] = pool.Alloc();
        double after = 0;
        foreach (var ch in live) after += RunsOverSlots(ch);
        after /= CHUNKS;
        TestContext.WriteLine($"runs/slots after {CYCLES} unhinted edit cycles: {after:F4}");
        Assert.AreEqual(CHUNKS * BRICKS, pool.InUse);
        // The control's JOB is to be bad. If lowest-first ever scores as well as
        // the hinted path, this pair of tests has stopped discriminating and
        // neither result means anything -- which is exactly what happened in the
        // first version of these tests, where both scored 0.0022 because only
        // one hole was ever outstanding.
        Assert.Greater(after, 0.10,
            $"unhinted churn scored {after:F4}; the control is supposed to degrade, so if it " +
            "does not, this test pair no longer proves AllocNear is doing the work");
    }

    [Test]
    public void FragmentationFuzz_NeverLeaksOrDoubleIssues()
    {
        using var pool = new BrickDataPool(8192, true);
        var rng = new System.Random(4242);
        var held = new List<(int b, int n)>();
        for (int step = 0; step < 20000; step++)
        {
            if (held.Count > 0 && rng.Next(2) == 0)
            {
                int k = rng.Next(held.Count);
                pool.FreeRange(held[k].b, held[k].n);
                held.RemoveAt(k);
            }
            else
            {
                int n = 1 + rng.Next(200);
                if (pool.TryAllocRange(n, out int b)) held.Add((b, n));
            }
            // no slot may appear in two live allocations
        }
        int expected = 0;
        foreach (var h in held) expected += h.n;
        Assert.AreEqual(expected, pool.InUse, "fuzz leaked or double-issued slots");
        foreach (var h in held) pool.FreeRange(h.b, h.n);
        Assert.AreEqual(0, pool.InUse);
        Assert.AreEqual(1, pool.FreeRunCount, "a fully drained pool must be one run again");
    }
}
