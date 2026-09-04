// Assets/CoreEngine/Tests/FluidWakeQueueTests.cs
//
// Pins the fix for the frozen-manually-placed-fluid bug (see FluidWakeQueue's
// header for the full failure chain). The defect was that a wake request was
// evaluated against a GPU mirror that did not yet contain the edit, and was
// then destroyed -- so the voxel was never simulated at all.
//
// These tests inject the readiness predicate, so they exercise the real
// decision with no compute device. The end-to-end proof is the Phase 5b rig.
using System.Collections.Generic;
using NUnit.Framework;
using VoxelEngine.Simulation;

public class FluidWakeQueueTests
{
    private static int[] Dest(int n) => new int[n];

    [Test]
    public void RequestIsHeld_WhileTheMirrorIsStale_AndReleasedWhenItCatchesUp()
    {
        // THE REGRESSION. Before the fix this request was dispatched
        // immediately, read pre-edit terrain, allocated no slot, and was gone.
        var q = new FluidWakeQueue(64, 120);
        bool mirrorClean = false;
        q.Add(4242);

        var dst = Dest(8);
        Assert.AreEqual(0, q.Collect(_ => mirrorClean, dst, dst.Length),
            "a request must NOT be dispatched while its chunk is still dirty");
        Assert.AreEqual(1, q.PendingCount, "and it must still be pending, not dropped");

        Assert.AreEqual(0, q.Collect(_ => mirrorClean, dst, dst.Length));
        Assert.AreEqual(1, q.PendingCount, "still held across further ticks");

        mirrorClean = true;                       // LateUpdate uploaded the chunk
        Assert.AreEqual(1, q.Collect(_ => mirrorClean, dst, dst.Length),
            "once the mirror is current the request must be released");
        Assert.AreEqual(4242, dst[0]);
        Assert.AreEqual(0, q.PendingCount);
        Assert.AreEqual(1, q.ReleasedReadyTotal);
        Assert.AreEqual(0, q.ReleasedStaleTotal);
    }

    [Test]
    public void CleanMirror_ReleasesOnTheSameTick_SoRigBehaviourIsUnchanged()
    {
        // Compatibility guard. Every rig uploads before ticking
        // (Phase5bBasin.Tick: "GPU sees this frame's edits"), so the mirror is
        // already current and the queue must add ZERO latency there. If this
        // fails, the fix has changed the behaviour the 5b baseline was measured
        // against and that baseline is no longer comparable.
        var q = new FluidWakeQueue(64, 120);
        q.Add(1); q.Add(2); q.Add(3);
        var dst = Dest(8);
        Assert.AreEqual(3, q.Collect(_ => true, dst, dst.Length));
        Assert.AreEqual(0, q.PendingCount);
        CollectionAssert.AreEquivalent(new[] { 1, 2, 3 }, new List<int> { dst[0], dst[1], dst[2] });
    }

    [Test]
    public void ReadyAndStaleCells_AreSeparatedInTheSameTick()
    {
        // A partly-uploaded window: some chunks current, some not. The ready
        // ones must go now; the stale ones must wait rather than being dropped
        // alongside them.
        var q = new FluidWakeQueue(64, 120);
        for (int c = 0; c < 6; c++) q.Add(c);
        var dst = Dest(16);
        int n = q.Collect(c => c % 2 == 0, dst, dst.Length);
        Assert.AreEqual(3, n);
        Assert.AreEqual(3, q.PendingCount);
        for (int i = 0; i < n; i++) Assert.AreEqual(0, dst[i] % 2, "only ready cells released");
    }

    [Test]
    public void DuplicateRequestsForOneCell_AreCoalesced()
    {
        // Holding RMB re-edits the same cell every frame. Without coalescing
        // the queue fills with copies of one cell and starts rejecting real
        // requests -- which would reintroduce the original bug under exactly
        // the input that found it.
        var q = new FluidWakeQueue(4, 120);
        for (int i = 0; i < 50; i++) Assert.IsTrue(q.Add(777));
        Assert.AreEqual(1, q.PendingCount);
        Assert.AreEqual(1, q.QueuedTotal);
        Assert.AreEqual(49, q.CoalescedTotal);
        Assert.AreEqual(0, q.RejectedFullTotal, "coalescing must not consume capacity");

        var dst = Dest(8);
        Assert.AreEqual(1, q.Collect(_ => true, dst, dst.Length));
        Assert.IsTrue(q.Add(777), "after release the same cell may be queued again");
        Assert.AreEqual(1, q.PendingCount);
    }

    [Test]
    public void AStuckRequest_IsReleasedAfterMaxDeferTicks_AndCounted()
    {
        // If a chunk never reports clean, holding forever would leak the queue
        // and silently stop waking that cell -- the very failure being fixed.
        // Release it, and make it VISIBLE rather than a quiet fallback.
        var q = new FluidWakeQueue(64, 3);
        q.Add(9);
        var dst = Dest(8);
        for (int t = 0; t < 3; t++)
            Assert.AreEqual(0, q.Collect(_ => false, dst, dst.Length), $"held at tick {t}");

        Assert.AreEqual(1, q.Collect(_ => false, dst, dst.Length),
            "after maxDeferTicks the request is released best-effort");
        Assert.AreEqual(9, dst[0]);
        Assert.AreEqual(1, q.ReleasedStaleTotal, "and it is counted as stale, not as a normal release");
        Assert.AreEqual(0, q.ReleasedReadyTotal);
        Assert.AreEqual(0, q.PendingCount);
    }

    [Test]
    public void QueueIsBounded_AndOverflowIsCountedNotThrown()
    {
        var q = new FluidWakeQueue(3, 120);
        Assert.IsTrue(q.Add(1)); Assert.IsTrue(q.Add(2)); Assert.IsTrue(q.Add(3));
        Assert.IsFalse(q.Add(4), "overflow returns false rather than throwing");
        Assert.AreEqual(1, q.RejectedFullTotal);
        Assert.AreEqual(3, q.PendingCount);
    }

    [Test]
    public void ADestinationTooSmall_KeepsTheRemainderQueued()
    {
        // Partial drain must not silently discard the tail.
        var q = new FluidWakeQueue(64, 120);
        for (int c = 0; c < 10; c++) q.Add(c);
        var dst = Dest(4);
        Assert.AreEqual(4, q.Collect(_ => true, dst, dst.Length));
        Assert.AreEqual(6, q.PendingCount, "the rest stay queued for the next tick");
        Assert.AreEqual(6, q.Collect(_ => true, new int[16], 16));
    }

    [Test]
    public void Clear_DropsPendingRequestsAndAllowsRequeue()
    {
        var q = new FluidWakeQueue(8, 120);
        q.Add(5); q.Add(6);
        q.Clear();
        Assert.AreEqual(0, q.PendingCount);
        Assert.IsTrue(q.Add(5), "membership must be cleared too, or the cell can never requeue");
        Assert.AreEqual(1, q.PendingCount);
    }
}
