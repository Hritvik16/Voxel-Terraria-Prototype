// Assets/CoreEngine/Tests/SealedBrickTests.cs
//
// Task 201a: sealed bricks take no GPU body (TerrainClipmap.Sealed.cs). Reads the
// clipmap's CPU shadow of its GPU buffers (the suite runs -nographics).
//
// Covered: a solid chunk seals exactly its 14^3 inner bricks (border bricks never),
// and they hold no GPU slot; one air voxel at a brick corner unseals exactly the 8
// bricks whose brick-or-shell holds it (water / glass / torch likewise, not a rock
// swap); the ORACLE: on a noisy chunk with caves, water, glass and a torch, the GPU
// view (shadow clipmap + bodies) gives every voxel the CPU's seal-solidness and, at
// every voxel that is non-solid or touches a non-solid voxel (26-neighbourhood), the
// CPU's exact material -- what the exactness argument needs; a chunk too big for the
// pool without sealing fits with it; `SealHiddenBricks = false` = the old upload.
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;
using VoxelEngine.Memory;

public class SealedBrickTests
{
    private static readonly int3 Box = new int3(2, 2, 2);
    private BrickDataPool _pool;
    private ChunkHandleAllocator _alloc;
    private ChunkStore _store;
    private readonly List<TerrainClipmap> _clips = new List<TerrainClipmap>();

    [SetUp]
    public void SetUp()
    {
        TerrainClipmap.SealHiddenBricks = true;
        _pool = new BrickDataPool(16384);
        _alloc = new ChunkHandleAllocator(16);
        _store = new ChunkStore(_pool, _alloc);
    }

    [TearDown]
    public void TearDown()
    {
        TerrainClipmap.SealHiddenBricks = true;
        foreach (var c in _clips) c.Dispose();
        _clips.Clear();
        _pool?.Dispose();
    }

    private TerrainClipmap NewClip(int gpuCap)
    {
        var clip = new TerrainClipmap(Box, _pool.Capacity, gpuCap) { RecentreDirtiesStaleSlots = true };
        _clips.Add(clip);
        clip.EnableCpuShadow();
        clip.SetWindowOrigin(int3.zero);
        return clip;
    }

    /// A chunk whose 4096 bricks are ALL dense, voxel material = mat(x, y, z) (chunk-local voxels).
    private Chunk InsertDenseChunk(int3 c, System.Func<int, int, int, byte> mat)
    {
        var ch = new Chunk { coord = c, isUniform = false, bricks = _alloc.Alloc() };
        var raw = _pool.RawData;
        for (int bz = 0; bz < 16; bz++)
        for (int by = 0; by < 16; by++)
        for (int bx = 0; bx < 16; bx++)
        {
            int slot = _pool.Alloc();
            ch.bricks[(bz << 8) | (by << 4) | bx].data = 0x80000000u | (uint)slot;
            for (int vz = 0; vz < 8; vz++)
            for (int vy = 0; vy < 8; vy++)
            for (int vx = 0; vx < 8; vx++)
                raw[slot * 512 + ((vz << 6) | (vy << 3) | vx)] = mat(bx * 8 + vx, by * 8 + vy, bz * 8 + vz);
        }
        _store.InsertChunk(ch);
        return ch;
    }

    private static byte Rock(int x, int y, int z) => (byte)(((x ^ y ^ z) & 1) == 0 ? Materials.Stone : Materials.Sandstone);

    private static void AssertGreen(TerrainClipmap clip, BrickDataPool pool, ChunkStore store, string what)
    {
        var r = ClipmapValidator.ValidateRegion(clip, pool, store);
        Assert.IsTrue(r.pass, $"{what}: {r.Describe()}");
    }

    private static int SealedIn(TerrainClipmap clip, int3 c, out bool borderSealed)
    {
        int n = 0; borderSealed = false;
        for (int z = 0; z < 16; z++) for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++)
        {
            if (!clip.IsSealedBrick(c, new int3(x, y, z))) continue;
            n++;
            if (x == 0 || y == 0 || z == 0 || x == 15 || y == 15 || z == 15) borderSealed = true;
        }
        return n;
    }

    [Test]
    public void SolidChunk_SealsExactlyTheInnerBricks_WithNoGpuBody()
    {
        var clip = NewClip(8192);
        int3 c = new int3(0, 0, 0);
        Chunk ch = InsertDenseChunk(c, Rock);
        clip.MarkDirty(c);
        clip.FlushAllDirty(_store, _pool);
        Assert.AreEqual(14 * 14 * 14, SealedIn(clip, c, out bool border), "14^3 inner bricks sealed");
        Assert.IsFalse(border, "a chunk-border brick is never sealed");
        Assert.AreEqual(4096 - 2744, clip.GpuPoolInUse, "sealed bricks take no GPU body");
        Assert.AreEqual(2744, clip.SealedBricksNow);
        int inner = (7 << 8) | (7 << 4) | 7;
        Assert.AreEqual(-1, clip.GpuSlotOf((int)(ch.bricks[inner].data & 0x3FFFFFFFu)), "inner brick unmapped");
        Assert.AreEqual(Rock(56, 56, 56), (byte)clip.ExpectedGpuHandle(c, new int3(7, 7, 7), ch.bricks[inner].data), "staged = its voxel 0's material");
        AssertGreen(clip, _pool, _store, "solid");

        // Evicted: every slot and every sealed count is released.
        _store.EvictChunk(c); clip.MarkDirty(c);
        clip.FlushAllDirty(_store, _pool);
        Assert.AreEqual(0, clip.GpuPoolInUse);
        Assert.AreEqual(0, clip.SealedBricksNow);
    }

    [TestCase((byte)0, TestName = "OneNonSolidVoxel_UnsealsTheEightBricksAroundIt(air)")]
    [TestCase((byte)6, TestName = "OneNonSolidVoxel_UnsealsTheEightBricksAroundIt(water)")]
    [TestCase((byte)24, TestName = "OneNonSolidVoxel_UnsealsTheEightBricksAroundIt(glass)")]
    [TestCase((byte)14, TestName = "OneNonSolidVoxel_UnsealsTheEightBricksAroundIt(torch)")]
    public void OneNonSolidVoxel_UnsealsTheEightBricksAroundIt(byte m)
    {
        var clip = NewClip(8192);
        int3 c = new int3(1, 0, 0);
        InsertDenseChunk(c, Rock);
        clip.MarkDirty(c);
        clip.FlushAllDirty(_store, _pool);
        Assert.AreEqual(2744, SealedIn(clip, c, out _));

        // Voxel (40,40,40) = brick (5,5,5)'s voxel 0 and the shell corner of bricks 4..5 on every axis.
        _store.SetVoxel(c * 128 + new int3(40, 40, 40), m);
        clip.MarkDirty(c);
        clip.FlushAllDirty(_store, _pool);
        Assert.AreEqual(2744 - 8, SealedIn(clip, c, out _), "exactly the 8 bricks whose brick or shell holds it");
        for (int z = 4; z <= 5; z++) for (int y = 4; y <= 5; y++) for (int x = 4; x <= 5; x++)
            Assert.IsFalse(clip.IsSealedBrick(c, new int3(x, y, z)), $"brick {x},{y},{z}");
        Assert.IsTrue(clip.IsSealedBrick(c, new int3(3, 4, 4)) && clip.IsSealedBrick(c, new int3(6, 5, 5)), "the next bricks stay sealed");
        Assert.AreEqual(4096 - 2736, clip.GpuPoolInUse);
        AssertGreen(clip, _pool, _store, "edited");

        // A rock-for-rock swap keeps everything sealed.
        _store.SetVoxel(c * 128 + new int3(40, 40, 40), Materials.Obsidian);
        clip.MarkDirty(c);
        clip.FlushAllDirty(_store, _pool);
        Assert.AreEqual(2744, SealedIn(clip, c, out _), "solid again");
        AssertGreen(clip, _pool, _store, "refilled");
    }

    [Test]
    public void Oracle_GpuViewMatchesCpu_WhereverARayCanLook()
    {
        var clip = NewClip(8192);
        int3 c = new int3(0, 1, 1);
        // Rock with spherical caves (air), a flooded cave (water), glass and a torch, mixed rock materials.
        byte Mat(int x, int y, int z)
        {
            float3 p = new float3(x, y, z);
            if (math.distance(p, new float3(30, 70, 40)) < 13f) return Materials.Air;
            if (math.distance(p, new float3(90, 40, 85)) < 9f) return Materials.Water;
            if (math.distance(p, new float3(64, 100, 20)) < 4f) return Materials.Glass;
            if (x == 100 && y == 100 && z == 100) return Materials.Torch;
            if (y > 118 && ((x * 7 + z * 3) % 11) < 4) return Materials.Air;   // a ragged surface
            int h = (x * 73856093) ^ (y * 19349663) ^ (z * 83492791);
            return (h & 7) == 0 ? Materials.Deepstone : (h & 7) == 1 ? Materials.MossyStone : Rock(x, y, z);
        }
        Chunk ch = InsertDenseChunk(c, Mat);
        clip.MarkDirty(c);
        clip.FlushAllDirty(_store, _pool);
        int sealedN = SealedIn(clip, c, out bool border);
        Assert.IsFalse(border);
        Assert.Greater(sealedN, 1000, "most of the rock is sealed");
        Assert.Less(sealedN, 2744, "the caves, pond, glass, torch and surface unseal some");
        AssertGreen(clip, _pool, _store, "oracle chunk");

        // The GPU view: the shadow clipmap + bodies, exactly as the shader reads them.
        uint[] clipGpu = new uint[clip.WindowDimsBricks.x * clip.WindowDimsBricks.y * clip.WindowDimsBricks.z];
        clip.ReadClipmapForValidation(clipGpu);
        uint[] bodies = new uint[clip.GpuPoolCapacity * 128];
        clip.ReadBodiesForValidation(bodies, 0, bodies.Length);
        byte Gpu(int x, int y, int z)
        {
            uint h = clipGpu[clip.GpuIndexOf(c, new int3(x >> 3, y >> 3, z >> 3))];
            if ((h & 0x80000000u) == 0) return (byte)(h & 0xFF);
            int vi = ((z & 7) << 6) | ((y & 7) << 3) | (x & 7);
            int w = (int)(h & 0x3FFFFFFFu) * 128 + (vi >> 2);
            return (byte)((bodies[w] >> ((vi & 3) * 8)) & 0xFF);
        }
        var raw = _pool.RawData;
        const int N = 128;
        var cpu = new byte[N * N * N]; var gpu = new byte[N * N * N]; var solid = new bool[N * N * N];
        for (int z = 0; z < N; z++) for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
        {
            int i = (z * N + y) * N + x;
            uint h = ch.bricks[((z >> 3) << 8) | ((y >> 3) << 4) | (x >> 3)].data;
            cpu[i] = raw[(int)(h & 0x3FFFFFFFu) * 512 + (((z & 7) << 6) | ((y & 7) << 3) | (x & 7))];
            gpu[i] = Gpu(x, y, z);
            solid[i] = TerrainClipmap.IsSealSolid(cpu[i]);
        }
        int exposed = 0, differInside = 0;
        for (int z = 0; z < N; z++) for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
        {
            int i = (z * N + y) * N + x;
            byte g = gpu[i], k = cpu[i];
            if (TerrainClipmap.IsSealSolid(g) != solid[i]) Assert.Fail($"solidness at {x},{y},{z}: GPU {g} CPU {k}");
            if ((g != 0) != (k != 0)) Assert.Fail($"non-zero at {x},{y},{z}: GPU {g} CPU {k}");
            // Exposed = non-solid itself, or a 26-neighbour is (the chunk edge counts as exposed).
            bool exp = x == 0 || y == 0 || z == 0 || x == N - 1 || y == N - 1 || z == N - 1;
            for (int dz = -1; dz <= 1 && !exp; dz++) for (int dy = -1; dy <= 1 && !exp; dy++) for (int dx = -1; dx <= 1 && !exp; dx++)
                if (!solid[((z + dz) * N + y + dy) * N + x + dx]) exp = true;
            if (exp) { exposed++; if (g != k) Assert.Fail($"exposed voxel {x},{y},{z}: GPU {g} CPU {k}"); }
            else if (g != k) differInside++;
        }
        Assert.Greater(exposed, 10000);
        Assert.Greater(differInside, 0, "sealed interiors do read differently (the cut is real)");
    }

    [Test]
    public void PoolTooSmallUnsealed_FitsSealed()
    {
        var clip = NewClip(2000);
        int3 c = new int3(0, 0, 1);
        InsertDenseChunk(c, Rock);
        clip.MarkDirty(c);
        clip.FlushAllDirty(_store, _pool);
        Assert.AreEqual(0, clip.GpuPoolRefusedTotal, "1,352 bodies fit 2,000");
        Assert.IsFalse(clip.IsGpuRefused(c));
        AssertGreen(clip, _pool, _store, "fits");

        TerrainClipmap.SealHiddenBricks = false;
        var off = NewClip(2000);
        int3 d = new int3(1, 0, 1);
        InsertDenseChunk(d, Rock);
        off.MarkDirty(d);
        off.FlushAllDirty(_store, _pool);
        Assert.AreEqual(1, off.GpuPoolRefusedTotal, "4,096 bodies do not fit 2,000 unsealed");
    }

    [Test]
    public void SwitchOff_IsTheOldUpload()
    {
        TerrainClipmap.SealHiddenBricks = false;
        var clip = NewClip(8192);
        int3 c = new int3(1, 1, 1);
        Chunk ch = InsertDenseChunk(c, Rock);
        clip.MarkDirty(c);
        clip.FlushAllDirty(_store, _pool);
        Assert.AreEqual(4096, clip.GpuPoolInUse);
        Assert.AreEqual(0, clip.SealedBricksNow);
        for (int i = 0; i < 4096; i++)
            Assert.AreEqual(clip.GpuHandleOf(ch.bricks[i].data), clip.ExpectedGpuHandle(c, new int3(i & 15, (i >> 4) & 15, i >> 8), ch.bricks[i].data));
        AssertGreen(clip, _pool, _store, "off");
    }

    [Test]
    public void SwitchedOffMidRun_ReuploadClearsTheMask()
    {
        var clip = NewClip(8192);
        int3 c = new int3(0, 1, 0);
        InsertDenseChunk(c, Rock);
        clip.MarkDirty(c);
        clip.FlushAllDirty(_store, _pool);
        Assert.AreEqual(2744, clip.SealedBricksNow);
        TerrainClipmap.SealHiddenBricks = false;
        Assert.AreEqual(1, clip.DebugRedirtyBox(_store));
        clip.FlushAllDirty(_store, _pool);
        Assert.AreEqual(0, clip.SealedBricksNow, "an OFF upload clears the slot's mask");
        Assert.AreEqual(0, SealedIn(clip, c, out _));
        Assert.AreEqual(4096, clip.GpuPoolInUse);
        AssertGreen(clip, _pool, _store, "off again");
        TerrainClipmap.SealHiddenBricks = true;
        clip.DebugRedirtyBox(_store);
        clip.FlushAllDirty(_store, _pool);
        Assert.AreEqual(2744, clip.SealedBricksNow);
        Assert.AreEqual(4096 - 2744, clip.GpuPoolInUse);
        AssertGreen(clip, _pool, _store, "on again");
    }
}
