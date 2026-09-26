// Assets/CoreEngine/Tests/TerrainGenChunkSourceTests.cs
//
// V1's gate (design doc §11): chunks from TERRAINGEN for sizeClass 2.
//   Exact       every voxel of a generated surface chunk == TerrainGen's own
//               D (unculled, prepared independently), through the same GenMat ->
//               engine mapping
// THE REFERENCE RUNS UNDER BURST, like the chunk. Burst and the Editor's Mono are
// not byte-identical for float code (CLAUDE.md known issue; TerrainGen's gate is
// Burst self-consistency): the first version of this test compared a Mono S with
// the Burst chunk and failed on a standing stone's edge, one ULP deciding whether
// a column was inside the 6.9 m stone.
//   LOD-0       each column's top solid voxel in the chunk == S's height (except
//               surface-carving stamp columns), the far band's contract
//   Determinism two generations hash identically (ChunkContentHash)
// Legacy worlds are untouched by construction (StreamManager.GenerateBaseline
// takes the old path for sizeClass 0/1; GenerationTests keep their hashes).
using NUnit.Framework;
using Unity.Jobs;
using Unity.Mathematics;
using VoxelEngine.Memory;
using VoxelEngine.WorldGen;

public class TerrainGenChunkSourceTests
{
    private static TerrainGenChunkSource _src;

    [OneTimeSetUp]
    public void BuildOnce() { if (_src == null) _src = new TerrainGenChunkSource(42u); }

    [OneTimeTearDown]
    public void TearDown() { _src?.Dispose(); _src = null; }

    private static byte Read(Chunk c, BrickDataPool pool, int x, int y, int z)
    {
        int bi = ((z >> 3) << 8) | ((y >> 3) << 4) | (x >> 3);
        uint d = c.bricks[bi].data;
        if ((d & 0x80000000u) == 0) return (byte)d;
        int idx = (int)(d & 0x7FFFFFFFu);
        return pool.RawData[idx * 512 + (((z & 7) << 6) | ((y & 7) << 3) | (x & 7))];
    }

    private static int3 SurfaceChunk(out float2 site)
    {
        var spec = _src.Spec; var view = _src.Map.View; var o = _src.O;
        float c = spec.worldSizeVoxels * 0.5f;
        for (int i = 0; i < 400; i++)
        {
            float2 p = new float2(c, c) + new float2((i % 20) - 10, (i / 20) - 10) * 1500f;
            var col = TerrainGen.ShapeField.Sample(in spec, in view, in o, p.x, p.y, 1);
            if (col.height < spec.seaLevelY + 300 || col.water > col.height) continue;
            site = p;
            return new int3((int)p.x >> 7, col.height >> 7, (int)p.y >> 7);
        }
        site = default;
        Assert.Fail("no land site found");
        return default;
    }

    [Unity.Burst.BurstCompile(FloatMode = Unity.Burst.FloatMode.Strict, FloatPrecision = Unity.Burst.FloatPrecision.High, CompileSynchronously = true)]
    private struct RefJob : Unity.Jobs.IJob
    {
        [Unity.Collections.ReadOnly] public Unity.Collections.NativeArray<float> h, r, w;
        [Unity.Collections.ReadOnly] public Unity.Collections.NativeArray<byte> toEngine;
        public TerrainGen.TerrainSpec spec; public TerrainGen.Offsets o; public int3 origin;
        public Unity.Collections.NativeArray<int> top;        // per column: S height, or int.MinValue if excluded
        public Unity.Collections.NativeArray<byte> mats;      // every 3rd voxel, unculled D -> engine

        public unsafe void Execute()
        {
            var map = new TerrainGen.MapView
            {
                height = (float*)Unity.Collections.LowLevel.Unsafe.NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(h),
                rough = (float*)Unity.Collections.LowLevel.Unsafe.NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(r),
                water = (float*)Unity.Collections.LowLevel.Unsafe.NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(w),
                cells = spec.mapCells, cellVoxels = spec.mapCellVoxels,
            };
            int k = 0;
            for (int z = 0; z < 128; z += 3)
            for (int x = 0; x < 128; x += 3)
            {
                int wx = origin.x + x, wz = origin.z + z;
                var col = TerrainGen.ShapeField.Sample(in spec, in map, in o, wx + 0.5f, wz + 0.5f, 1);
                float st = TerrainGen.ShapeField.MaxStep(in spec, in map, in o, wx + 0.5f, wz + 0.5f, 1, col.height);
                var cd = TerrainGen.DetailField.PrepareColumn(in spec, in o, wx, wz);
                top[(z / 3) * 43 + x / 3] = (!col.carvesTop && col.water <= col.height) ? col.height : int.MinValue;
                for (int y = 0; y < 128; y += 3)
                    mats[k++] = toEngine[TerrainGen.DetailField.Material(in spec, in o, in col, in cd, st, wx, origin.y + y, wz)];
            }
        }
    }

    [Test]
    public void SurfaceChunk_IsExact_Lod0_AndDeterministic()
    {
        int3 cc = SurfaceChunk(out _);
        var pool = new BrickDataPool(EngineConfig.BRICKS_PER_CHUNK);
        var alloc = new ChunkHandleAllocator(2);
        var pool2 = new BrickDataPool(EngineConfig.BRICKS_PER_CHUNK);
        var alloc2 = new ChunkHandleAllocator(2);
        var hA = new Unity.Collections.NativeArray<float>(_src.Map.Height, Unity.Collections.Allocator.TempJob);
        var rA = new Unity.Collections.NativeArray<float>(_src.Map.Rough, Unity.Collections.Allocator.TempJob);
        var wA = new Unity.Collections.NativeArray<float>(_src.Map.Water, Unity.Collections.Allocator.TempJob);
        var te = new Unity.Collections.NativeArray<byte>(256, Unity.Collections.Allocator.TempJob);
        var top = new Unity.Collections.NativeArray<int>(43 * 43, Unity.Collections.Allocator.TempJob);
        var refM = new Unity.Collections.NativeArray<byte>(43 * 43 * 43, Unity.Collections.Allocator.TempJob);
        try
        {
            for (int i = 0; i < 256; i++) te[i] = VoxelEngine.Rendering.FarField.GenToEngine((byte)i);
            var chunk = new Chunk();
            _src.Generate(cc, chunk, alloc, pool, null);
            int3 b = cc * 128;
            new RefJob { h = hA, r = rA, w = wA, toEngine = te, spec = _src.Spec, o = _src.O, origin = b, top = top, mats = refM }.Run();
            int mismatches = 0, checkedV = 0, lodCols = 0, dense = 0, k = 0;
            for (int bi = 0; bi < EngineConfig.BRICKS_PER_CHUNK; bi++) if ((chunk.bricks[bi].data & 0x80000000u) != 0) dense++;
            for (int z = 0; z < 128; z += 3)
            for (int x = 0; x < 128; x += 3)
            {
                for (int y = 0; y < 128; y += 3)
                {
                    if (Read(chunk, pool, x, y, z) != refM[k++]) mismatches++;
                    checkedV++;
                }
                int s = top[(z / 3) * 43 + x / 3];
                if (s != int.MinValue && s >= b.y && s < b.y + 128)
                {
                    int t = -1;
                    for (int y = 127; y >= 0; y--)
                    {
                        byte m = Read(chunk, pool, x, y, z);
                        if (m != Materials.Air && m != Materials.Water && m != Materials.Lava) { t = b.y + y; break; }
                    }
                    Assert.AreEqual(s, t, $"LOD-0: column ({b.x + x},{b.z + z}) S {s}, chunk top {t}");
                    lodCols++;
                }
            }
            UnityEngine.Debug.Log($"[V1] chunk {cc}: {checkedV} voxels checked, {mismatches} mismatches; {lodCols} LOD-0 columns; {dense} dense bricks of 4096");
            Assert.AreEqual(0, mismatches, "generated chunk differs from TerrainGen's D (both under Burst)");
            Assert.Greater(lodCols, 50);
            var again = new Chunk();
            _src.Generate(cc, again, alloc2, pool2, null);
            Assert.AreEqual(ChunkContentHash.Hash(chunk, pool), ChunkContentHash.Hash(again, pool2), "two generations differ");
        }
        finally
        {
            pool.Dispose(); pool2.Dispose();
            hA.Dispose(); rA.Dispose(); wA.Dispose(); te.Dispose(); top.Dispose(); refM.Dispose();
        }
    }
}
