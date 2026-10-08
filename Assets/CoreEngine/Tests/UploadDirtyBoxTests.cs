// Assets/CoreEngine/Tests/UploadDirtyBoxTests.cs
//
// Task 32 (KNOWN_ISSUES "STRAY RESIDENT CHUNKS ALIAS INTO THE MIRROR"):
// TerrainClipmap.UploadDirty wrote a resident chunk into its toroidal slot
// without checking that the chunk lies inside the mirror box, so a stray
// resident chunk overwrote the in-box chunk sharing that slot. Now it is
// skipped, counted (UploadStats.skippedOutOfBox) and parked, and re-dirtied if
// the box comes back to it while it is still resident.
//
// The mirror here is 8x4x8 chunks inside the default 32x16x32 store ring, so a
// chunk 8 chunks along x shares the in-box chunk's MIRROR slot but has its own
// STORE slot: both can be resident at once, which is the stray case. Checked on
// the CPU copy of the clipmap (CpuMirrorHandle) -- the suite runs -nographics.
using NUnit.Framework;
using Unity.Mathematics;
using VoxelEngine.Memory;

public class UploadDirtyBoxTests
{
    private static readonly int3 MirrorDims = new int3(8, 4, 8);
    private static readonly int3 InBox = new int3(1, 0, 1);
    private static readonly int3 Stray = new int3(9, 0, 1);   // 9 & 7 == 1: InBox's slot

    private BrickDataPool _pool;
    private ChunkStore _store;
    private TerrainClipmap _clip;

    [SetUp]
    public void SetUp()
    {
        _pool = new BrickDataPool(4096);
        _store = new ChunkStore(_pool, new ChunkHandleAllocator(64));
        _clip = new TerrainClipmap(MirrorDims, _pool.Capacity);
        _clip.SetWindowOrigin(int3.zero);
    }

    [TearDown]
    public void TearDown() { _clip?.Dispose(); _pool?.Dispose(); }

    private void Insert(int3 coord, byte material)
    {
        _store.InsertChunk(new Chunk { coord = coord, isUniform = true, uniformMaterial = material });
        _clip.MarkDirty(coord);
    }

    private void AssertSlot(int3 coord, uint expected, string what)
    {
        foreach (var b in new[] { new int3(0, 0, 0), new int3(7, 3, 11), new int3(15, 15, 15) })
            Assert.AreEqual(expected, _clip.CpuMirrorHandle(coord, b), $"{what}: brick {b}");
    }

    [Test]
    public void InBoxChunk_UploadsAsBefore()
    {
        Insert(InBox, Materials.Stone);
        var s = _clip.FlushAllDirty(_store, _pool);

        Assert.AreEqual(1, s.chunksUploaded);
        Assert.AreEqual(0, s.skippedOutOfBox);
        Assert.AreEqual(0, _clip.SkippedOutOfBoxTotal);
        Assert.IsFalse(_clip.IsDirty(InBox));
        Assert.AreEqual(_clip.UploadEpoch, _clip.LastUploadEpoch(InBox));
        AssertSlot(InBox, Materials.Stone, "in-box chunk");
    }

    [Test]
    public void StrayResidentChunk_IsSkippedAndCounted_InBoxSlotUnchanged()
    {
        Insert(InBox, Materials.Stone);
        _clip.FlushAllDirty(_store, _pool);

        Insert(Stray, Materials.Sand);
        Assert.IsTrue(_store.IsResident(InBox) && _store.IsResident(Stray), "both must be resident");
        var s = _clip.FlushAllDirty(_store, _pool);

        Assert.AreEqual(1, s.skippedOutOfBox);
        Assert.AreEqual(1, _clip.SkippedOutOfBoxTotal);
        Assert.AreEqual(0, s.chunksUploaded);
        Assert.IsFalse(_clip.IsDirty(Stray), "a skipped chunk leaves the dirty set");
        Assert.AreEqual(-1L, _clip.LastUploadEpoch(Stray), "the stray must never reach its slot");
        AssertSlot(InBox, Materials.Stone, "aliased in-box slot");
    }

    [Test]
    public void SkippedChunk_IsReDirtied_WhenTheBoxReachesIt()
    {
        Insert(InBox, Materials.Stone);
        Insert(Stray, Materials.Sand);
        _clip.FlushAllDirty(_store, _pool);
        AssertSlot(InBox, Materials.Stone, "before the slide");

        // Slide the box 8 chunks along +x: Stray is in it, InBox is not.
        _clip.SetWindowOrigin(new int3(8, 0, 0));
        var s = _clip.FlushAllDirty(_store, _pool);

        Assert.AreEqual(1, s.chunksUploaded, "the parked chunk must upload once it is in the box");
        Assert.AreEqual(0, s.skippedOutOfBox);
        Assert.AreEqual(_clip.UploadEpoch, _clip.LastUploadEpoch(Stray));
        AssertSlot(Stray, Materials.Sand, "re-entered chunk");

        // Parked only once: a further pass does nothing.
        Assert.AreEqual(0, _clip.FlushAllDirty(_store, _pool).chunksUploaded);
    }
}
