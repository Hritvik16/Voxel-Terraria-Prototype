// Assets/CoreEngine/Tests/EditServiceTests.cs
//
// Correctness proof for §13 Phase 6 file 3 (§8.3): the edit path, batch
// variants, prefabs, tool tiers, and the wake-scan hook.
//
// EditService is an ORCHESTRATOR over the frozen ChunkStore.SetVoxel writer, so
// these tests drive it against a fake writer and assert on the SEQUENCE it
// produces: what was written, what was marked dirty on the mirror, and what was
// refused. The real ChunkStore is exercised by Phase6EditRig.

using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;
using VoxelEngine.Simulation;   // FluidGpuSimulation.SphereCovers -- the one brush shape

public class EditServiceTests
{
    /// A writer and world in one, like ChunkStore is in production.
    private sealed class FakeStore : IEditService, IWorldQuery, IVoxelResidency
    {
        public readonly Dictionary<int3, byte> Cells = new Dictionary<int3, byte>();
        public readonly HashSet<int3> NotResidentChunks = new HashSet<int3>();
        public readonly List<int3> Writes = new List<int3>();

        public byte GetVoxel(int3 v) => Cells.TryGetValue(v, out byte m) ? m : Materials.Air;
        public bool IsResident(int3 c) => !NotResidentChunks.Contains(c);

        public void SetVoxel(int3 v, byte m)
        {
            // Mirrors ChunkStore's own behaviour: a write to an unloaded chunk
            // is silently dropped. EditService must not rely on this to notice.
            if (!IsResident(CoordMath.VoxelToChunk(v))) return;
            Cells[v] = m;
            Writes.Add(v);
        }
    }

    private sealed class FakeDirty : IChunkDirtySink
    {
        public readonly List<int3> Marked = new List<int3>();
        public void MarkDirty(int3 c) => Marked.Add(c);
    }

    private static EditService Wired(out FakeStore store, out FakeDirty dirty)
    {
        store = new FakeStore();
        dirty = new FakeDirty();
        var e = new EditService();
        e.AttachWorld(store, store, store, dirty);
        return e;
    }

    // =====================================================================
    // The §8.3 path
    // =====================================================================

    [Test]
    public void SetVoxel_Writes_AndMarksTheMirrorDirty()
    {
        FakeStore s; FakeDirty d;
        var e = Wired(out s, out d);

        Assert.IsTrue(e.TrySetVoxel(new int3(10, 20, 30), Materials.Stone));

        Assert.AreEqual(Materials.Stone, s.GetVoxel(new int3(10, 20, 30)), "the voxel is written");
        CollectionAssert.Contains(d.Marked, CoordMath.VoxelToChunk(new int3(10, 20, 30)),
            "and the chunk is marked dirty on the GPU mirror -- the step ChunkStore cannot do");
        Assert.AreEqual(1, e.VoxelsWritten);
    }

    [Test]
    public void SetVoxel_IsANoOp_WhenTheMaterialAlreadyMatches()
    {
        FakeStore s; FakeDirty d;
        var e = Wired(out s, out d);
        e.TrySetVoxel(new int3(1, 1, 1), Materials.Stone);
        d.Marked.Clear();

        Assert.IsFalse(e.TrySetVoxel(new int3(1, 1, 1), Materials.Stone),
            "§8.3's no-op fast path: writing the material already there changes nothing");
        Assert.AreEqual(1, e.EditsNoOp);
        CollectionAssert.IsEmpty(d.Marked,
            "and must NOT dirty the chunk -- a no-op that still queues an upload spends " +
            "the §4.3 byte budget on nothing");
    }

    [Test]
    public void EditingAnUnloadedChunk_IsRefusedAndCounted_NotSilentlySwallowed()
    {
        // §9.4 applied to editing. ChunkStore drops the write; a mining tool
        // that cannot tell that from success reports breaking a block that is
        // still there.
        FakeStore s; FakeDirty d;
        var e = Wired(out s, out d);
        int3 v = new int3(500, 20, 500);
        s.NotResidentChunks.Add(CoordMath.VoxelToChunk(v));

        Assert.IsFalse(e.TrySetVoxel(v, Materials.Stone), "the edit must report failure");
        Assert.AreEqual(1, e.EditsRejectedNotResident, "and be counted as a residency miss");
        Assert.AreEqual(0, e.VoxelsWritten, "not as a write");
        CollectionAssert.IsEmpty(s.Writes, "nothing reached the writer");
        CollectionAssert.IsEmpty(d.Marked, "and nothing was marked dirty");
    }

    [Test]
    public void AttachWorld_RejectsANullResidencySource()
    {
        var s = new FakeStore();
        var e = new EditService();
        Assert.Throws<System.ArgumentNullException>(() => e.AttachWorld(s, s, null, null));
    }

    [Test]
    public void UsingTheServiceBeforeWiring_ThrowsClearly_RatherThanNullReferencing()
    {
        var e = new EditService();
        var ex = Assert.Throws<System.InvalidOperationException>(
            () => e.TrySetVoxel(new int3(0, 0, 0), Materials.Stone));
        StringAssert.Contains("AttachWorld", ex.Message);
    }

    [Test]
    public void ADirtySink_IsOptional()
    {
        var s = new FakeStore();
        var e = new EditService();
        e.AttachWorld(s, s, s, null);                 // headless: no mirror
        Assert.IsTrue(e.TrySetVoxel(new int3(2, 2, 2), Materials.Stone),
            "a test or tool with no GPU mirror must still be able to edit");
    }

    // =====================================================================
    // Batch variants
    // =====================================================================

    [Test]
    public void SetBox_FillsInclusiveBounds_AndCountsOnlyRealChanges()
    {
        FakeStore s; FakeDirty d;
        var e = Wired(out s, out d);

        int changed = e.SetBox(new int3(0, 0, 0), new int3(2, 2, 2), Materials.Stone);
        Assert.AreEqual(27, changed, "3x3x3 inclusive");
        Assert.AreEqual(Materials.Stone, s.GetVoxel(new int3(2, 2, 2)), "the far corner is included");

        int again = e.SetBox(new int3(0, 0, 0), new int3(2, 2, 2), Materials.Stone);
        Assert.AreEqual(0, again, "re-filling the same box changes nothing");
    }

    [Test]
    public void SetBox_AcceptsItsBoundsInEitherOrder()
    {
        FakeStore s; FakeDirty d;
        var e = Wired(out s, out d);
        Assert.AreEqual(27, e.SetBox(new int3(2, 2, 2), new int3(0, 0, 0), Materials.Stone),
            "lo and hi swapped must not silently produce an empty box");
    }

    [Test]
    public void SetSphere_MatchesTheEngineWideBrushShape()
    {
        FakeStore s; FakeDirty d;
        var e = Wired(out s, out d);

        int changed = e.SetSphere(new int3(0, 0, 0), 1, Materials.Stone);
        Assert.AreEqual(7, changed, "radius 1 is the centre plus 6 faces, as SphereCovers defines");
        Assert.AreEqual(Materials.Air, s.GetVoxel(new int3(1, 1, 1)), "corners are excluded");
    }

    // =====================================================================
    // Prefabs
    // =====================================================================

    [Test]
    public void PlacePrefab_StampsMaterials_AndSkipsAirSoASilhouetteIsPossible()
    {
        FakeStore s; FakeDirty d;
        var e = Wired(out s, out d);

        // A 2x1x1 prefab: one stone, one hole.
        var mats = new byte[] { Materials.Stone, Materials.Air };
        int changed = e.PlacePrefab(new int3(5, 5, 5), new int3(2, 1, 1), mats);

        Assert.AreEqual(1, changed, "only the non-air cell is written");
        Assert.AreEqual(Materials.Stone, s.GetVoxel(new int3(5, 5, 5)));
        Assert.AreEqual(Materials.Air, s.GetVoxel(new int3(6, 5, 5)),
            "the Air cell is SKIPPED, not written -- otherwise a prefab could only ever " +
            "be a solid box, and placing one would carve a hole around itself");
    }

    [Test]
    public void PlacePrefab_RejectsAMismatchedMaterialCount()
    {
        FakeStore s; FakeDirty d;
        var e = Wired(out s, out d);
        var ex = Assert.Throws<System.ArgumentException>(
            () => e.PlacePrefab(int3.zero, new int3(2, 2, 2), new byte[3]));
        StringAssert.Contains("8", ex.Message, "the message should say what was expected");
    }

    [Test]
    public void PlacePrefab_OrdersMaterialsXMajorThenYThenZ()
    {
        FakeStore s; FakeDirty d;
        var e = Wired(out s, out d);

        // 2x2x1: indices 0,1 are y=0; indices 2,3 are y=1.
        var mats = new byte[] { Materials.Stone, Materials.Sand, Materials.Grass, Materials.Snow };
        e.PlacePrefab(int3.zero, new int3(2, 2, 1), mats);

        Assert.AreEqual(Materials.Stone, s.GetVoxel(new int3(0, 0, 0)));
        Assert.AreEqual(Materials.Sand,  s.GetVoxel(new int3(1, 0, 0)));
        Assert.AreEqual(Materials.Grass, s.GetVoxel(new int3(0, 1, 0)));
        Assert.AreEqual(Materials.Snow,  s.GetVoxel(new int3(1, 1, 0)));
    }

    // =====================================================================
    // Tool tiers (§13: "tools at 10/40/200 vox/s")
    // =====================================================================

    [Test]
    public void TheThreeTiers_AreTheRatesSection13NamesA()
    {
        Assert.AreEqual(3, EditService.Tiers.Length);
        Assert.AreEqual(10f, EditService.Tiers[0].VoxelsPerSecond, 1e-4f);
        Assert.AreEqual(40f, EditService.Tiers[1].VoxelsPerSecond, 1e-4f);
        Assert.AreEqual(200f, EditService.Tiers[2].VoxelsPerSecond, 1e-4f);
    }

    [Test]
    public void ToolBudget_AccumulatesFractionsAcrossFrames_SoASlowToolDigsAtAll()
    {
        // 10 vox/s at 60 Hz is 0.167 voxels per frame. Truncating per frame
        // yields zero forever and the slowest tier never removes anything.
        var b = new EditService.ToolBudget();
        int total = 0;
        for (int i = 0; i < 60; i++) total += b.Accrue(1f / 60f, 10f);

        Assert.AreEqual(10, total, 1,
            "one second at 10 vox/s must yield ~10 voxels, not 0 and not 600");
    }

    [Test]
    public void ToolBudget_ScalesWithTheTier()
    {
        float dt = 1f / 60f;
        int[] totals = new int[3];
        for (int t = 0; t < 3; t++)
        {
            var b = new EditService.ToolBudget();
            for (int i = 0; i < 60; i++) totals[t] += b.Accrue(dt, EditService.Tiers[t].VoxelsPerSecond);
        }

        Assert.AreEqual(10, totals[0], 1, "hand");
        Assert.AreEqual(40, totals[1], 1, "drill");
        Assert.AreEqual(200, totals[2], 1, "bore");
        Assert.Less(totals[0], totals[1], "a slower tier must remove strictly less rock per second");
        Assert.Less(totals[1], totals[2]);
    }

    [Test]
    public void ToolBudget_YieldsNothingForZeroOrNegativeTime()
    {
        var b = new EditService.ToolBudget();
        Assert.AreEqual(0, b.Accrue(0f, 200f));
        Assert.AreEqual(0, b.Accrue(-1f, 200f));
        Assert.AreEqual(0, b.Accrue(1f, 0f));
    }

    [Test]
    public void MineSphereBudgeted_RemovesAtMostTheBudget()
    {
        FakeStore s; FakeDirty d;
        var e = Wired(out s, out d);
        e.SetBox(new int3(-4, -4, -4), new int3(4, 4, 4), Materials.Stone);

        int removed = e.MineSphereBudgeted(int3.zero, 3, 5);
        Assert.AreEqual(5, removed, "a 5-voxel budget removes exactly 5");
    }

    [Test]
    public void MineSphereBudgeted_CanEventuallyClearTheWholeBrush()
    {
        // Regression for a real bug: the nearest-first loop matched
        // x*x+y*y+z*z == r*r, but squared distances are 0,1,2,3,4,5,6... and are
        // not perfect squares. Cells at d^2 = 2, 3, 5, 6 were never visited by
        // any r, so the tool carved a sparse star and could never finish the
        // hole however large the budget.
        FakeStore s; FakeDirty d;
        var e = Wired(out s, out d);
        e.SetBox(new int3(-4, -4, -4), new int3(4, 4, 4), Materials.Stone);

        int expected = 0;
        for (int z = -2; z <= 2; z++)
        for (int y = -2; y <= 2; y++)
        for (int x = -2; x <= 2; x++)
            if (FluidGpuSimulation.SphereCovers(x, y, z, 2)) expected++;

        int removed = e.MineSphereBudgeted(int3.zero, 2, 10000);
        Assert.AreEqual(expected, removed,
            $"an unlimited budget must clear every cell of the radius-2 brush ({expected})");

        for (int z = -2; z <= 2; z++)
        for (int y = -2; y <= 2; y++)
        for (int x = -2; x <= 2; x++)
            if (FluidGpuSimulation.SphereCovers(x, y, z, 2))
                Assert.AreEqual(Materials.Air, s.GetVoxel(new int3(x, y, z)),
                    $"cell ({x},{y},{z}) at d^2={x * x + y * y + z * z} must be mined");
    }

    [Test]
    public void MineSphereBudgeted_WorksOutwardFromTheCentre()
    {
        FakeStore s; FakeDirty d;
        var e = Wired(out s, out d);
        e.SetBox(new int3(-4, -4, -4), new int3(4, 4, 4), Materials.Stone);

        e.MineSphereBudgeted(int3.zero, 3, 1);
        Assert.AreEqual(Materials.Air, s.GetVoxel(int3.zero),
            "the first voxel a rate-limited tool removes is the one it is pointed at, " +
            "so the hole grows outward instead of scattering");
    }

    [Test]
    public void MineSphereBudgeted_DoesNothingWithNoBudget()
    {
        FakeStore s; FakeDirty d;
        var e = Wired(out s, out d);
        e.SetBox(new int3(-2, -2, -2), new int3(2, 2, 2), Materials.Stone);
        Assert.AreEqual(0, e.MineSphereBudgeted(int3.zero, 2, 0));
    }

    // =====================================================================
    // The wake scan is still wired (Phase 5b's hook, unchanged)
    // =====================================================================

    [Test]
    public void WithNoFluidAttached_TheWakeScanIsANoOp_NotAnError()
    {
        FakeStore s; FakeDirty d;
        var e = Wired(out s, out d);
        Assert.IsFalse(e.HasFluidSimulation);
        Assert.DoesNotThrow(() => e.TrySetVoxel(new int3(3, 3, 3), Materials.Stone));
        Assert.AreEqual(0, e.WakeScansRun, "no simulation means no scan to run");
    }

    [Test]
    public void ABatchRunsOneWakeScan_NotOnePerVoxel()
    {
        // 40x40x40 is 64,000 voxels; per-cell scanning would be 1.7 million
        // neighbour probes for one prefab placement.
        FakeStore s; FakeDirty d;
        var e = Wired(out s, out d);
        e.SetBox(new int3(0, 0, 0), new int3(9, 9, 9), Materials.Stone);

        Assert.AreEqual(1000, e.VoxelsWritten, "every voxel is written");
        Assert.LessOrEqual(e.WakeScansRun, 1,
            "but the neighbourhood is scanned at most once for the whole box");
    }
}
