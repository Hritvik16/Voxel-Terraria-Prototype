// Assets/CoreEngine/Tests/ScratchPoolReuseTests.cs
//
// THE SCRATCH-POOL LEAK THAT SILENTLY LOST EDITS.
//
// StreamManager.SaveDelta regenerates a pristine baseline chunk into a POOLED
// scratch context so it can diff it against the live chunk. `ResetScratch` was
// an empty method body, so the baseline's dense bricks were never returned. The
// scratch pool holds exactly BRICKS_PER_CHUNK (4096) bricks and a baseline of
// this world costs roughly 400, so after about ten saves through the same
// context the pool was dry -- and from then on every SaveDelta through it threw
// "BrickDataPool exhausted", caught the exception, and returned false.
//
// Returning false there means THE DELTA WAS NEVER WRITTEN. That is silent edit
// loss, and it breaks §4.2's round-trip and §3.6's "never lost progress (edits
// are in the delta)" -- the latter exactly when the LRU valve is evicting, which
// is when the most deltas are written and when losing them matters most.
//
// It hid because nothing flushed enough dirty chunks at once: the acceptance
// rig's Gate D writes two deltas, and MAX_CHUNK_SAVES_PER_FRAME is 4. It
// surfaced only once §13's checkerboard was sized large enough to actually
// reach §3.6's high-water mark and drive a real eviction storm.
//
// THE CONTROL IS THE POINT. "Reset makes reuse work" is also satisfied by a
// pool that never runs out, so the no-Reset case is asserted to fail. Without
// that, a mutation making Reset a no-op would leave these tests green.

using System;
using NUnit.Framework;
using VoxelEngine.Memory;

public class ScratchPoolReuseTests
{
    /// What one SaveDelta costs a scratch pool: a baseline chunk's dense
    /// bricks. Measured on this world at ~400 per chunk (acceptance rig:
    /// ~330,000 dense bricks over ~800 resident chunks).
    private const int BricksPerBaseline = 400;

    private const int ScratchCapacity = EngineConfig.BRICKS_PER_CHUNK;   // 4096

    /// One SaveDelta's worth of allocation against a scratch pool.
    private static void AllocateOneBaseline(BrickDataPool pool)
    {
        for (int i = 0; i < BricksPerBaseline; i++) pool.Alloc();
    }

    [Test]
    public void APooledScratchContext_SurvivesManyConsecutiveDeltaSaves()
    {
        // THE BUG, as an assertion. 50 saves is an ordinary eviction storm --
        // the checkerboard run flushed far more than that.
        using (var pool = new BrickDataPool(ScratchCapacity))
        {
            for (int save = 0; save < 50; save++)
            {
                Assert.DoesNotThrow(() => AllocateOneBaseline(pool),
                    $"delta save {save} exhausted the scratch pool; before the fix this threw " +
                    "at about save 10, and StreamManager.SaveDelta swallows the exception and " +
                    "returns false -- so the delta is silently not written");
                pool.Reset();
            }
        }
    }

    [Test]
    public void WithoutTheReset_TheSameSequenceExhausts()
    {
        // THE CONTROL. If this does not throw, the test above proves nothing.
        using (var pool = new BrickDataPool(ScratchCapacity))
        {
            Assert.Throws<InvalidOperationException>(() =>
            {
                for (int save = 0; save < 50; save++) AllocateOneBaseline(pool);
            }, "a 4096-brick pool must NOT survive 50 un-reset baselines of 400 bricks each; " +
               "if it does, this test is not modelling the real scratch pool");
        }
    }

    [Test]
    public void ResetReturnsEveryBrick_NotMostOfThem()
    {
        using (var pool = new BrickDataPool(ScratchCapacity))
        {
            AllocateOneBaseline(pool);
            Assert.AreEqual(BricksPerBaseline, pool.InUse, "the baseline is holding bricks");

            pool.Reset();
            Assert.AreEqual(0, pool.InUse,
                "Reset must return the pool to fully free -- a partial reset would leak more " +
                "slowly and be harder to see, not be safe");
        }
    }

    [Test]
    public void ResetRestoresTheWholePoolAsOnePiece_ForARangeAwarePool()
    {
        // The tier-0 pool is range-aware and allocates a chunk's bodies as ONE
        // contiguous run (see BrickDataPool's header: it is what collapses ~460
        // driver calls into one). A Reset that freed every brick but left the
        // free list fragmented would restore capacity and silently destroy that
        // property.
        using (var pool = new BrickDataPool(ScratchCapacity, rangeAware: true))
        {
            // Fragment it deliberately: many single allocations, then free the
            // even ones, so the free list is in pieces.
            var idx = new int[1000];
            for (int i = 0; i < idx.Length; i++) idx[i] = pool.Alloc();
            for (int i = 0; i < idx.Length; i += 2) pool.Free(idx[i]);
            Assert.Greater(pool.FreeRunCount, 1, "the pool really is fragmented now");

            pool.Reset();

            Assert.AreEqual(1, pool.FreeRunCount,
                "after Reset the free list must be a single run, as it is on construction");
            Assert.IsTrue(pool.TryAllocRange(ScratchCapacity, out _),
                "and the entire capacity must be allocatable as one contiguous range");
        }
    }

    [Test]
    public void APoolCanBeResetAndFullyReused_ManyTimesOver()
    {
        // Reuse is the whole point of pooling the contexts; a Reset that worked
        // once and then degraded would reproduce the bug more slowly.
        using (var pool = new BrickDataPool(256))
        {
            for (int cycle = 0; cycle < 200; cycle++)
            {
                for (int i = 0; i < 256; i++) pool.Alloc();
                Assert.AreEqual(256, pool.InUse, $"cycle {cycle}: pool should be full");
                pool.Reset();
                Assert.AreEqual(0, pool.InUse, $"cycle {cycle}: pool should be empty again");
            }
        }
    }
}
