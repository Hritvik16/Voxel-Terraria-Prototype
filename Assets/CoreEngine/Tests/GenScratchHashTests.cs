// Assets/CoreEngine/Tests/GenScratchHashTests.cs
//
// Task 223 (audit of Temp/TempJob allocations around jobs): the near-chunk and shell-chunk generators' scratch moved from
// Allocator.TempJob to Allocator.Persistent (it lives for a whole chunk build on a streaming worker thread, past TempJob's
// 4-frame lifetime). The allocator must not change a single output byte. This Explicit probe hashes the output of
// TerrainGenChunkSource.Generate (near chunks, 3x2x3 around a land surface voxel) and GenerateShellChunk (shells 1-4, which
// includes FillShellChunk from shell FIRST_FILL_SHELL on) and writes the hash to Logs/TerrainGen/temp223/hash_<tag>.txt.
// Run it on the old and the new code and compare the two files (HANDOFF 223).
using System.IO;
using NUnit.Framework;
using Unity.Mathematics;
using VoxelEngine.Memory;
using VoxelEngine.WorldGen;

public class GenScratchHashTests
{
    private const ulong FNV_OFFSET = 14695981039346656037UL, FNV_PRIME = 1099511628211UL;

    private static ulong Mix(ulong h, byte b) => (h ^ b) * FNV_PRIME;

    private static byte Read(Chunk c, BrickDataPool pool, int x, int y, int z)
    {
        if (c.isUniform) return c.uniformMaterial;
        int bi = ((z >> 3) << 8) | ((y >> 3) << 4) | (x >> 3);
        uint d = c.bricks[bi].data;
        if ((d & 0x80000000u) == 0) return (byte)d;
        int idx = (int)(d & 0x3FFFFFFFu);
        return pool.RawData[idx * 512 + (((z & 7) << 6) | ((y & 7) << 3) | (x & 7))];
    }

    private static int3 SurfaceVoxel(TerrainGenChunkSource src)
    {
        var spec = src.Spec; var view = src.Map.View; var o = src.O;
        float c = spec.worldSizeVoxels * 0.5f;
        for (int i = 0; i < 400; i++)
        {
            float2 p = new float2(c, c) + new float2((i % 20) - 10, (i / 20) - 10) * 1500f;
            var col = TerrainGen.ShapeField.Sample(in spec, in view, in o, p.x, p.y, 1);
            if (col.height < spec.seaLevelY + 300 || col.water > col.height) continue;
            return new int3((int)p.x, col.height, (int)p.y);
        }
        Assert.Fail("no land site found");
        return default;
    }

    [Test, Explicit("task 223 probe: writes the generator output hash; run before/after an allocator change")]
    public void GeneratorOutput_Hash()
    {
        string tag = System.Environment.GetEnvironmentVariable("HASH_TAG") ?? "run";
        var src = new TerrainGenChunkSource(42u);
        try
        {
            int3 s = SurfaceVoxel(src);
            int3 c0 = s >> 7;
            ulong hn = FNV_OFFSET, hs = FNV_OFFSET;
            int nonUniform = 0;
            var pool = new BrickDataPool(EngineConfig.BRICKS_PER_CHUNK * 20);
            var alloc = new ChunkHandleAllocator(32);
            try
            {
                for (int dz = -1; dz <= 1; dz++)
                for (int dy = -1; dy <= 0; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    var ch = new Chunk();
                    src.Generate(c0 + new int3(dx, dy, dz), ch, alloc, pool, null);
                    if (!ch.isUniform) nonUniform++;
                    for (int z = 0; z < 128; z++)
                    for (int y = 0; y < 128; y++)
                    for (int x = 0; x < 128; x++)
                        hn = Mix(hn, Read(ch, pool, x, y, z));
                }
            }
            finally { pool.Dispose(); }

            using (var sc = new TerrainGenChunkSource.ShellChunk())
            {
                for (int k = 1; k <= 4; k++)
                {
                    src.GenerateShellChunk(k, s >> (7 + k), sc);
                    hs = Mix(hs, (byte)(sc.IsUniformAir ? 1 : 0));
                    if (sc.IsUniformAir) continue;
                    for (int i = 0; i < sc.Mats.Length; i++) hs = Mix(hs, sc.Mats[i]);
                    for (int i = 0; i < sc.BrickMat.Length; i++) hs = Mix(hs, sc.BrickMat[i]);
                    for (int i = 0; i < sc.BrickUniform.Length; i++) hs = Mix(hs, (byte)(sc.BrickUniform[i] ? 1 : 0));
                }
            }

            Directory.CreateDirectory("Logs/TerrainGen/temp223");
            string line = $"tag {tag}  seed 42  surface {s}  near 18 chunks (non-uniform {nonUniform}) hash {hn:X16}  shells 1-4 hash {hs:X16}";
            File.WriteAllText($"Logs/TerrainGen/temp223/hash_{tag}.txt", line + "\n");
            UnityEngine.Debug.Log("[GenScratchHash] " + line);
            Assert.Greater(nonUniform, 0, "the probe must cover real terrain, not only uniform chunks");
        }
        finally { src.Dispose(); }
    }
}
