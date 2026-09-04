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
        q.Add(4242, 0);

        var dst = Dest(8);
        Assert.AreEqual(0, q.Collect((c, st) => mirrorClean, dst, dst.Length),
            "a request must NOT be dispatched while its chunk is still dirty");
        Assert.AreEqual(1, q.PendingCount, "and it must still be pending, not dropped");

        Assert.AreEqual(0, q.Collect((c, st) => mirrorClean, dst, dst.Length));
        Assert.AreEqual(1, q.PendingCount, "still held across further ticks");

        mirrorClean = true;                       // LateUpdate uploaded the chunk
        Assert.AreEqual(1, q.Collect((c, st) => mirrorClean, dst, dst.Length),
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
        q.Add(1, 0); q.Add(2, 0); q.Add(3, 0);
        var dst = Dest(8);
        Assert.AreEqual(3, q.Collect((c, st) => true, dst, dst.Length));
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
        for (int c = 0; c < 6; c++) q.Add(c, 0);
        var dst = Dest(16);
        int n = q.Collect((c, st) => c % 2 == 0, dst, dst.Length);
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
        for (int i = 0; i < 50; i++) Assert.IsTrue(q.Add(777, 0));
        Assert.AreEqual(1, q.PendingCount);
        Assert.AreEqual(1, q.QueuedTotal);
        Assert.AreEqual(49, q.CoalescedTotal);
        Assert.AreEqual(0, q.RejectedFullTotal, "coalescing must not consume capacity");

        var dst = Dest(8);
        Assert.AreEqual(1, q.Collect((c, st) => true, dst, dst.Length));
        Assert.IsTrue(q.Add(777, 0), "after release the same cell may be queued again");
        Assert.AreEqual(1, q.PendingCount);
    }

    [Test]
    public void AStuckRequest_IsReleasedAfterMaxDeferTicks_AndCounted()
    {
        // If a chunk never reports clean, holding forever would leak the queue
        // and silently stop waking that cell -- the very failure being fixed.
        // Release it, and make it VISIBLE rather than a quiet fallback.
        var q = new FluidWakeQueue(64, 3);
        q.Add(9, 0);
        var dst = Dest(8);
        for (int t = 0; t < 3; t++)
            Assert.AreEqual(0, q.Collect((c, st) => false, dst, dst.Length), $"held at tick {t}");

        Assert.AreEqual(1, q.Collect((c, st) => false, dst, dst.Length),
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
        Assert.IsTrue(q.Add(1, 0)); Assert.IsTrue(q.Add(2, 0)); Assert.IsTrue(q.Add(3, 0));
        Assert.IsFalse(q.Add(4, 0), "overflow returns false rather than throwing");
        Assert.AreEqual(1, q.RejectedFullTotal);
        Assert.AreEqual(3, q.PendingCount);
    }

    [Test]
    public void ADestinationTooSmall_KeepsTheRemainderQueued()
    {
        // Partial drain must not silently discard the tail.
        var q = new FluidWakeQueue(64, 120);
        for (int c = 0; c < 10; c++) q.Add(c, 0);
        var dst = Dest(4);
        Assert.AreEqual(4, q.Collect((c, st) => true, dst, dst.Length));
        Assert.AreEqual(6, q.PendingCount, "the rest stay queued for the next tick");
        Assert.AreEqual(6, q.Collect((c, st) => true, new int[16], 16));
    }

    [Test]
    public void Clear_DropsPendingRequestsAndAllowsRequeue()
    {
        var q = new FluidWakeQueue(8, 120);
        q.Add(5, 0); q.Add(6, 0);
        q.Clear();
        Assert.AreEqual(0, q.PendingCount);
        Assert.IsTrue(q.Add(5, 0), "membership must be cleared too, or the cell can never requeue");
        Assert.AreEqual(1, q.PendingCount);
    }

    [Test]
    public void ACellEditedEveryTick_StillComesReady_RatherThanStarving()
    {
        // THE SECOND BUG, found by the Phase 5c hold_paint case (holding the
        // place button). The first version of this queue gated on "is the chunk
        // clean right now". A cell edited every frame leaves its chunk DIRTY at
        // every tick -- it is uploaded between the ticks, never during one --
        // so the request never came ready and escaped only via the stale
        // timeout. The rig measured it exactly: 8 voxels placed where 30 were
        // asked for, over 900 ticks, with a 120-tick stale interval.
        //
        // The stamp fixes it: readiness is "uploaded SINCE I asked", which an
        // is-it-dirty boolean cannot express.
        var q = new FluidWakeQueue(64, 120);
        var dst = Dest(8);
        long epoch = 5;                       // mirror's upload epoch
        int released = 0;

        for (int frame = 0; frame < 30; frame++)
        {
            q.Add(1234, epoch);               // edit, stamped with the CURRENT epoch
            // tick: ready iff the chunk was uploaded strictly after the stamp
            released += q.Collect((c, st) => epoch > st, dst, dst.Length);
            epoch++;                          // the upload pass, after the tick
        }

        // EVERY OTHER FRAME is the correct answer here, not every frame. In
        // this (TickFirst) ordering the upload lands after the tick, so an edit
        // made in frame F is released by the tick in frame F+1 -- one frame of
        // latency, by design. The contrast that matters is with the old gate,
        // which released NOTHING in 30 frames because the chunk was dirty at
        // every tick and the stale timeout is 120.
        Assert.GreaterOrEqual(released, 14,
            "a cell edited every tick must keep coming ready, roughly every other frame");
        Assert.AreEqual(0, q.ReleasedStaleTotal,
            "and it must come ready HONESTLY, not by hitting the stale timeout");
    }

    [Test]
    public void CoalescingKeepsTheOldestStamp_SoRepeatedEditsCannotDeferForever()
    {
        // If coalescing took the NEWER stamp, a cell edited every frame would
        // push its own release target forward every frame and never resolve --
        // reintroducing the starvation under a different mechanism.
        var q = new FluidWakeQueue(64, 120);
        q.Add(7, 10);                          // first edit at epoch 10
        for (int i = 0; i < 20; i++) q.Add(7, 100 + i);   // later edits, same cell
        var dst = Dest(4);

        // An upload at epoch 11 is later than the FIRST edit and must release it.
        Assert.AreEqual(1, q.Collect((c, st) => 11L > st, dst, dst.Length),
            "the oldest stamp governs, so an upload just after the first edit releases it");
        Assert.AreEqual(0, q.ReleasedStaleTotal);
    }
}
