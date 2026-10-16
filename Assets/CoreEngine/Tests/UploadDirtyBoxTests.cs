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
//
// Task 93e (8.15 row D2): with the shells on, the box shrinks to the preset's
// ShellLayout.MirrorBox (16^3 on Low-High) INSIDE the unchanged 128 m load
// radius, so out-of-box residents are the normal case, not a stray. The tests
// below run that geometry scaled down (an 8^3 box, residents to +-5 chunks, as
// 16^3 against +-10): sizes per preset, out-of-box residents skipped and
// counted, and after every recentre each box position holds its own chunk or
// air (no LOST slot, no stale slot) with TerrainClipmap.RecentreDirtiesStaleSlots.
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

    // ---- Task 93e: the small box ------------------------------------------------

    [Test]
    public void MirrorBox_PerPreset_Pow2_Shell0Fits_LoadRadiusStays10()
    {
        var small = new int3(16, 16, 16);
        foreach (QualityPreset p in new[] { QualityPreset.Low, QualityPreset.Medium, QualityPreset.High, QualityPreset.Ultra })
        {
            var L = ShellLayout.From(p);
            int3 box = L.MirrorBox;
            Assert.IsTrue(ShellLayout.IsPow2(box.x) && ShellLayout.IsPow2(box.y) && ShellLayout.IsPow2(box.z), $"{p}: {box} pow2");
            Assert.IsTrue(ShellLayout.BoxFitsShell0(box, L.Shell0RadiusM), $"{p}: shell 0 ({L.Shell0RadiusM} m) inside {box}");
            Assert.AreEqual(p == QualityPreset.Ultra ? ShellLayout.TodayMirrorBox : small, box, $"{p} box");
        }
        // The fit, in metres: 16 chunks reach >= 89.6 m from anywhere in the camera's chunk;
        // High's shell 0 is 69 + 13 = 82 m; Ultra's 103 + 13 = 116 m does not fit, so it keeps 32 x 8 x 32.
        Assert.AreEqual(89.6f, ShellLayout.BoxInnerReachM(small), 1e-3f);
        Assert.AreEqual(82f, ShellLayout.From(QualityPreset.High).Shell0RadiusM, 1e-3f);
        Assert.IsFalse(ShellLayout.BoxFitsShell0(small, ShellLayout.From(QualityPreset.Ultra).Shell0RadiusM));
        Assert.IsFalse(ShellLayout.BoxFitsShell0(new int3(16, 4, 16), 50f), "the vertical band needs >= 8 layers");
        Assert.IsFalse(ShellLayout.BoxFitsShell0(new int3(24, 16, 24), 50f), "not a power of two (§6.2)");
        // The CPU side does not shrink: 128 m on every preset, and it fits the 32-chunk store ring.
        Assert.AreEqual(10, EngineConfig.TG2_LOAD_RADIUS_CHUNKS);
        Assert.GreaterOrEqual(VoxelEngine.Streaming.StreamManager.MaxLoadRadiusChunks(
            new int3(EngineConfig.WINDOW_CHUNKS_XZ, EngineConfig.WINDOW_CHUNKS_Y, EngineConfig.WINDOW_CHUNKS_XZ)),
            EngineConfig.TG2_LOAD_RADIUS_CHUNKS);
    }

    [Test]
    public void MirrorOrigin_CentresANarrowBoxByItsOwnSize_SameWidthUnchanged()
    {
        int3 store = new int3(32, 16, 32), cam = new int3(-37, 5, 101);
        int3 storeOrigin = cam - store / 2;
        int3 o = VoxelEngine.Streaming.StreamManager.MirrorOrigin(cam, storeOrigin, store, new int3(16, 16, 16), true);
        Assert.AreEqual(cam - new int3(8, 8, 8), o, "16^3: cam - 8 .. cam + 7 on every axis");
        Assert.AreEqual(new int3(storeOrigin.x, cam.y - 4, storeOrigin.z),
            VoxelEngine.Streaming.StreamManager.MirrorOrigin(cam, storeOrigin, store, new int3(32, 8, 32), true), "today's box");
        Assert.AreEqual(storeOrigin,
            VoxelEngine.Streaming.StreamManager.MirrorOrigin(cam, storeOrigin, store, store, false), "flat world");
    }

    private const int R = 5;                          // resident radius, chunks (as 10)
    private static readonly int3 Small = new int3(8, 8, 8);   // the box (as 16^3)

    private static byte Mat(int3 c) => (byte)(1 + ((c.x + 64) * 7 + (c.z + 64) * 3) % 250);

    /// Residents |dx|, |dz| <= R around `cam` (layer 0), every one dirty.
    private void FillAround(int3 cam)
    {
        for (int dz = -R; dz <= R; dz++)
        for (int dx = -R; dx <= R; dx++)
        {
            int3 c = new int3(cam.x + dx, 0, cam.z + dz);
            if (_store.IsResident(c)) continue;
            _store.InsertChunk(new Chunk { coord = c, isUniform = true, uniformMaterial = Mat(c) });
            _clip.MarkDirty(c);
        }
    }

    private void Recentre(int3 cam)
    {
        _clip.SetWindowOrigin(VoxelEngine.Streaming.StreamManager.MirrorOrigin(
            cam, int3.zero, _store.WindowDims, _clip.WindowDimsChunks, true));
    }

    /// Box positions whose CPU-mirror slot is wrong: a resident chunk not holding
    /// its own material (LOST) or a non-resident position not air (stale).
    private int WrongSlots(out string example)
    {
        example = null; int wrong = 0;
        int3 d = _clip.WindowDimsChunks, o = _clip.WindowOrigin;
        for (int z = 0; z < d.z; z++) for (int y = 0; y < d.y; y++) for (int x = 0; x < d.x; x++)
        {
            int3 c = o + new int3(x, y, z);
            uint want = _store.IsResident(c) ? Mat(c) : 0u;
            uint got = _clip.CpuMirrorHandle(c, new int3(3, 5, 7));
            if (got != want) { wrong++; if (example == null) example = $"{c}: {got} want {want} resident {_store.IsResident(c)}"; }
        }
        return wrong;
    }

    private void UseSmallBox(bool dirtyStale)
    {
        _clip.Dispose();
        _clip = new TerrainClipmap(Small, _pool.Capacity) { RecentreDirtiesStaleSlots = dirtyStale };
    }

    [Test]
    public void SmallBox_ResidentsOutside_AreSkippedAndCounted_InsideUpload()
    {
        UseSmallBox(true);
        Recentre(int3.zero);
        FillAround(int3.zero);
        var s = _clip.FlushAllDirty(_store, _pool);
        Assert.AreEqual(8 * 8, s.chunksUploaded, "the box's layer-0 positions");
        Assert.AreEqual(11 * 11 - 8 * 8, s.skippedOutOfBox, "residents outside the box: skipped, counted");
        Assert.AreEqual(0, WrongSlots(out string ex), ex);
    }

    [Test]
    public void SmallBox_WalkOutAndBack_NoLostNoStaleSlots()
    {
        UseSmallBox(true);
        Recentre(int3.zero);
        FillAround(int3.zero);
        _clip.FlushAllDirty(_store, _pool);
        // Out along +x (new residents enter at x = 6..8), then back past the start.
        foreach (int x in new[] { 1, 2, 3, 2, 1, 0, -1, 0 })
        {
            int3 cam = new int3(x, 0, 0);
            Recentre(cam);
            FillAround(cam);   // admission; residents are not evicted, as within the hysteresis ring
            _clip.FlushAllDirty(_store, _pool);
            Assert.AreEqual(0, WrongSlots(out string ex), $"camera x {x}: {ex}");
        }
        Assert.Greater(_clip.RecentreDirtiedTotal, 0);
    }

    [Test]
    public void SmallBox_WithoutSlotOwners_WalkBackDrawsTheWrongChunk()
    {
        // The control: the gap RecentreDirtiesStaleSlots closes is real.
        UseSmallBox(false);
        Recentre(int3.zero);
        FillAround(int3.zero);
        _clip.FlushAllDirty(_store, _pool);
        Recentre(new int3(1, 0, 0)); FillAround(new int3(1, 0, 0)); _clip.FlushAllDirty(_store, _pool);
        Recentre(int3.zero); _clip.FlushAllDirty(_store, _pool);
        // x = -4 is resident and clean, but its slot now holds x = 4's chunk.
        Assert.Greater(WrongSlots(out _), 0);
    }

    [Test]
    public void SmallBox_PositionNotResident_IsClearedAfterARecentre()
    {
        UseSmallBox(true);
        Recentre(int3.zero);
        FillAround(int3.zero);
        _clip.FlushAllDirty(_store, _pool);
        // Jump +3 without admitting: the box is -1 .. 6; x = 6 has no chunk and its
        // slot holds x = -2 (still resident, now outside). It must read air.
        Recentre(new int3(3, 0, 0));
        _clip.FlushAllDirty(_store, _pool);
        Assert.AreEqual(0, WrongSlots(out string ex), ex);
    }
}
