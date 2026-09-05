// Assets/CoreEngine/Tests/DestructionReducerTests.cs
//
// Correctness proof for §13 Phase 6 file 5 (§8.5 Mass Destruction).
//
// §13's acceptance line: "400K detonation: recovery <=3 frames, one Proxy Drop,
// plausible tally." All three clauses are here by name, plus the frame-split
// machinery that makes the first one true.

using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;
using VoxelEngine.Simulation;

public class DestructionReducerTests
{
    private sealed class FakeStore : IEditService, IWorldQuery, IVoxelResidency
    {
        public readonly Dictionary<int3, byte> Cells = new Dictionary<int3, byte>();
        public byte Fill = Materials.Air;
        public readonly HashSet<int3> NotResidentChunks = new HashSet<int3>();

        public byte GetVoxel(int3 v) => Cells.TryGetValue(v, out byte m) ? m : Fill;
        public bool IsResident(int3 c) => !NotResidentChunks.Contains(c);

        public void SetVoxel(int3 v, byte m)
        {
            if (!IsResident(CoordMath.VoxelToChunk(v))) return;
            Cells[v] = m;
        }

        public void Box(int3 lo, int3 hi, byte m)
        {
            for (int z = lo.z; z <= hi.z; z++)
            for (int y = lo.y; y <= hi.y; y++)
            for (int x = lo.x; x <= hi.x; x++)
                Cells[new int3(x, y, z)] = m;
        }
    }

    private static DestructionReducer Wired(out FakeStore store, out EditService edits)
    {
        store = new FakeStore();
        edits = new EditService();
        edits.AttachWorld(store, store, store, null);
        return new DestructionReducer(edits, store);
    }

    // =====================================================================
    // §13: "400K detonation: recovery <=3 frames, one Proxy Drop, plausible tally"
    // =====================================================================

    [Test]
    public void A400KDetonation_DrainsInAtMostThreeFrames()
    {
        FakeStore s; EditService e;
        var d = Wired(out s, out e);

        int radius = DestructionReducer.RadiusForAtLeast(400000);
        int cells = DestructionReducer.SphereVoxelCount(radius);
        Assert.GreaterOrEqual(cells, 400000, $"radius {radius} covers {cells} voxels");

        // Solid rock across the whole blast, so every cell is real work.
        s.Fill = Materials.Stone;

        d.Detonate(int3.zero, radius);
        int frames = 0;
        while (d.InProgress && frames < 100) { d.Step(); frames++; }

        Assert.IsFalse(d.InProgress, "the event must finish");
        Assert.LessOrEqual(frames, 3,
            $"§13: recovery <=3 frames for a 400K event -- radius {radius} covers " +
            $"{cells} cells, took {frames} frames at " +
            $"{DestructionReducer.DefaultVoxelsPerFrame}/frame");
        Assert.GreaterOrEqual(frames, 2,
            "and it must actually SPLIT -- finishing in one frame would mean the budget " +
            "is not being enforced at all");
    }

    [Test]
    public void A400KDetonation_ProducesExactlyOneProxyDrop()
    {
        FakeStore s; EditService e;
        var d = Wired(out s, out e);
        s.Fill = Materials.Stone;

        int radius = DestructionReducer.RadiusForAtLeast(400000);
        d.Detonate(int3.zero, radius);
        while (d.InProgress) d.Step();

        ProxyDrop drop;
        Assert.IsTrue(d.TryTakeCompleted(out drop), "one drop is produced");
        Assert.IsFalse(d.TryTakeCompleted(out _),
            "and only one -- §8.5: 'one PhysX box regardless of blast size'");
        Assert.AreEqual(1, d.EventsBegun);
        Assert.AreEqual(1, d.EventsCompleted);
    }

    [Test]
    public void TheTally_IsPlausible_MatchingWhatWasActuallyRemoved()
    {
        FakeStore s; EditService e;
        var d = Wired(out s, out e);
        s.Fill = Materials.Stone;

        int radius = 12;
        int expected = DestructionReducer.SphereVoxelCount(radius);

        ProxyDrop drop = d.DetonateNow(int3.zero, radius);

        Assert.AreEqual(expected, drop.TotalVoxels,
            "every covered cell of solid rock is removed and counted");
        Assert.AreEqual(expected, drop.Tally[Materials.Stone],
            "and the histogram attributes all of it to stone");
        Assert.AreEqual(Materials.Stone, drop.DominantMaterial);
        Assert.AreEqual(0, drop.Tally[Materials.Air], "air is never tallied as destroyed");
    }

    [Test]
    public void TheTally_SplitsAcrossMaterials_AndNamesTheDominantOne()
    {
        FakeStore s; EditService e;
        var d = Wired(out s, out e);
        s.Fill = Materials.Air;

        // A slab of stone with a thin seam of sandstone through it.
        s.Box(new int3(-6, -6, -6), new int3(6, 6, 6), Materials.Stone);
        s.Box(new int3(-6, 0, -6), new int3(6, 0, 6), Materials.Sandstone);

        ProxyDrop drop = d.DetonateNow(int3.zero, 6);

        Assert.Greater(drop.Tally[Materials.Stone], 0);
        Assert.Greater(drop.Tally[Materials.Sandstone], 0);
        Assert.AreEqual(drop.TotalVoxels,
            drop.Tally[Materials.Stone] + drop.Tally[Materials.Sandstone],
            "the histogram accounts for every removed voxel and nothing else");
        Assert.AreEqual(Materials.Stone, drop.DominantMaterial,
            "the thicker material dominates");
    }

    [Test]
    public void TheDropIsCentredOnTheBlast_InVoxelsAndMetres()
    {
        FakeStore s; EditService e;
        var d = Wired(out s, out e);
        s.Fill = Materials.Stone;

        int3 centre = new int3(100, 50, -30);
        ProxyDrop drop = d.DetonateNow(centre, 4);

        Assert.AreEqual(centre, drop.CentreVoxel);
        Assert.AreEqual(10.0f, drop.CentreM.x, 1e-3f, "voxels are 0.1 m");
        Assert.AreEqual(5.0f, drop.CentreM.y, 1e-3f);
        Assert.AreEqual(-3.0f, drop.CentreM.z, 1e-3f);
    }

    // =====================================================================
    // Frame-splitting
    // =====================================================================

    [Test]
    public void TheBudgetIsHonoured_AndTheEventResumesExactlyWhereItStopped()
    {
        FakeStore s; EditService e;
        var d = Wired(out s, out e);
        s.Fill = Materials.Stone;

        int radius = 8;
        int expected = DestructionReducer.SphereVoxelCount(radius);

        d.Detonate(int3.zero, radius);
        int frames = 0;
        while (d.InProgress && frames < 10000) { d.Step(64); frames++; }   // tiny budget

        ProxyDrop drop;
        Assert.IsTrue(d.TryTakeCompleted(out drop));
        Assert.AreEqual(expected, drop.TotalVoxels,
            "a heavily split event removes exactly the same cells as an unsplit one -- " +
            "no cell is dropped at a frame boundary and none is done twice");
        Assert.Greater(frames, 10, "and it really did split across many frames");
    }

    [Test]
    public void TheBudgetCountsCellsExamined_NotCellsRemoved()
    {
        // A blast through empty air removes nothing. If the budget counted
        // removals it would sweep the entire bounding box in one frame while
        // "under budget", and the split would silently stop working on exactly
        // the case (a big mostly-empty blast) where it is most needed.
        FakeStore s; EditService e;
        var d = Wired(out s, out e);
        s.Fill = Materials.Air;                       // nothing to destroy

        d.Detonate(int3.zero, 20);
        int frames = 0;
        while (d.InProgress && frames < 10000) { d.Step(100); frames++; }

        Assert.Greater(frames, 100,
            "visiting ~68,000 cells at 100 per frame must take hundreds of frames even " +
            "though zero voxels are removed");
    }

    [Test]
    public void FramesAreReportedOnTheDrop()
    {
        FakeStore s; EditService e;
        var d = Wired(out s, out e);
        s.Fill = Materials.Stone;

        d.Detonate(int3.zero, 6);
        int frames = 0;
        while (d.InProgress) { d.Step(200); frames++; }

        ProxyDrop drop;
        d.TryTakeCompleted(out drop);
        Assert.AreEqual(frames, drop.Frames, "the drop reports how long it took");
    }

    // =====================================================================
    // Guards
    // =====================================================================

    [Test]
    public void OverlappingDetonations_AreRefused_RatherThanInterleaved()
    {
        FakeStore s; EditService e;
        var d = Wired(out s, out e);
        s.Fill = Materials.Stone;

        d.Detonate(int3.zero, 10);
        d.Step(8);                                     // leave it mid-flight
        Assert.IsTrue(d.InProgress);

        Assert.Throws<System.InvalidOperationException>(() => d.Detonate(new int3(50, 0, 0), 10),
            "two live events would interleave their tallies and produce one drop for two " +
            "blasts, or two for one");
    }

    [Test]
    public void SteppingWithNoEvent_IsANoOp()
    {
        FakeStore s; EditService e;
        var d = Wired(out s, out e);
        Assert.AreEqual(0, d.Step());
        Assert.IsFalse(d.InProgress);
    }

    [Test]
    public void AZeroRadiusBlast_RemovesTheSingleCentreVoxel()
    {
        FakeStore s; EditService e;
        var d = Wired(out s, out e);
        s.Fill = Materials.Stone;

        ProxyDrop drop = d.DetonateNow(int3.zero, 0);
        Assert.AreEqual(1, drop.TotalVoxels, "radius 0 is one cell, not zero and not a crash");
    }

    [Test]
    public void ANegativeRadius_IsRejected()
    {
        FakeStore s; EditService e;
        var d = Wired(out s, out e);
        Assert.Throws<System.ArgumentOutOfRangeException>(() => d.Detonate(int3.zero, -1));
    }

    [Test]
    public void VoxelsInUnloadedChunks_AreNotCountedAsDestroyed()
    {
        // §9.4 once more: ChunkStore drops the write, so a tally that counted it
        // anyway would promise the player loot for rock that is still standing.
        FakeStore s; EditService e;
        var d = Wired(out s, out e);
        s.Fill = Materials.Stone;
        // Everything in chunk (0,0,0) is unloaded -- the blast sits inside it.
        s.NotResidentChunks.Add(new int3(0, 0, 0));

        ProxyDrop drop = d.DetonateNow(new int3(64, 64, 64), 4);

        Assert.AreEqual(0, drop.TotalVoxels,
            "nothing was actually removed, so nothing may be tallied");
        Assert.AreEqual(Materials.Air, drop.DominantMaterial);
        Assert.Greater(e.EditsRejectedNotResident, 0, "and the misses are counted");
    }

    // =====================================================================
    // The blast shape agrees with every other sphere brush in the engine
    // =====================================================================

    [Test]
    public void TheBlastUsesTheSameSphereShapeAsMiningAndTheFluidBrush()
    {
        FakeStore s; EditService e;
        var d = Wired(out s, out e);
        s.Fill = Materials.Stone;

        const int r = 5;
        ProxyDrop drop = d.DetonateNow(int3.zero, r);

        int viaSphereCovers = 0;
        for (int z = -r; z <= r; z++)
        for (int y = -r; y <= r; y++)
        for (int x = -r; x <= r; x++)
            if (FluidGpuSimulation.SphereCovers(x, y, z, r)) viaSphereCovers++;

        Assert.AreEqual(viaSphereCovers, drop.TotalVoxels,
            "a detonation and a mining brush of equal radius must cover exactly the same " +
            "cells, or 'radius' means two different things in one engine");
    }

    [Test]
    public void SphereVoxelCountAndRadiusForAtLeast_Agree()
    {
        int r = DestructionReducer.RadiusForAtLeast(400000);
        Assert.GreaterOrEqual(DestructionReducer.SphereVoxelCount(r), 400000);
        Assert.Less(DestructionReducer.SphereVoxelCount(r - 1), 400000,
            "and it is the SMALLEST such radius, not merely a big one");
    }
}
