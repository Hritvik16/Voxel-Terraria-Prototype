// Assets/CoreEngine/Tests/GenScratchHashTests.cs
//
// Task 223 (audit of Temp/TempJob allocations around jobs): the near-chunk and shell-chunk generators' scratch moved from
// Allocator.TempJob to Allocator.Persistent (it lives for a whole chunk build on a streaming worker thread, past TempJob's
// 4-frame lifetime). The allocator must not change a single output byte. This Explicit probe hashes the output of
// TerrainGenChunkSource.Generate (near chunks, 3x2x3 around a land surface voxel) and GenerateShellChunk (shells 1-4, which
// includes FillShellChunk from shell FIRST_FILL_SHELL on) and writes the hash to Logs/TerrainGen/temp223/hash_<tag>.txt.
// Run it on the old and the new code and compare the two files (HANDOFF 223).
// Task 224: the scratch is now REUSED per worker (ColumnPass's voxel scratch, ShellScratch). The probe hashes both paths --
// one-shot scratch per chunk (null pass / null scratch) and ONE reused pass + scratch for every chunk, as a worker does --
// and asserts they agree. VoxelReuseIsExact_DenseThenSparse / ShellScratchReuseIsExact_DenseThenSparse are the adversarial
// cases: a dense chunk, then (with the scratch also scribbled with junk) sparser chunks, against fresh scratch.
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

    private static ulong HashNear(TerrainGenChunkSource src, int3 c0, TerrainGenChunkSource.ColumnPass pass, out int nonUniform)
    {
        ulong hn = FNV_OFFSET;
        nonUniform = 0;
        var pool = new BrickDataPool(EngineConfig.BRICKS_PER_CHUNK * 20);
        var alloc = new ChunkHandleAllocator(32);
        try
        {
            for (int dz = -1; dz <= 1; dz++)
            for (int dy = -1; dy <= 0; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                var ch = new Chunk();
                src.Generate(c0 + new int3(dx, dy, dz), ch, alloc, pool, null, pass);
                if (!ch.isUniform) nonUniform++;
                for (int z = 0; z < 128; z++)
                for (int y = 0; y < 128; y++)
                for (int x = 0; x < 128; x++)
                    hn = Mix(hn, Read(ch, pool, x, y, z));
            }
        }
        finally { pool.Dispose(); }
        return hn;
    }

    private static ulong HashShell(ulong hs, TerrainGenChunkSource.ShellChunk sc)
    {
        hs = Mix(hs, (byte)(sc.IsUniformAir ? 1 : 0));
        if (sc.IsUniformAir) return hs;
        for (int i = 0; i < sc.Mats.Length; i++) hs = Mix(hs, sc.Mats[i]);
        for (int i = 0; i < sc.BrickMat.Length; i++) hs = Mix(hs, sc.BrickMat[i]);
        for (int i = 0; i < sc.BrickUniform.Length; i++) hs = Mix(hs, (byte)(sc.BrickUniform[i] ? 1 : 0));
        return hs;
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
            ulong hn = HashNear(src, c0, null, out int nonUniform);   // one-shot scratch per chunk (223's path)
            ulong hnR;
            using (var pass = new TerrainGenChunkSource.ColumnPass())   // task 224: one worker's reused scratch
                hnR = HashNear(src, c0, pass, out _);

            ulong hs = FNV_OFFSET, hsR = FNV_OFFSET;
            using (var sc = new TerrainGenChunkSource.ShellChunk())
            using (var sx = new TerrainGenChunkSource.ShellScratch())
            {
                for (int k = 1; k <= 4; k++)
                {
                    src.GenerateShellChunk(k, s >> (7 + k), sc);
                    hs = HashShell(hs, sc);
                }
                for (int k = 1; k <= 4; k++)
                {
                    src.GenerateShellChunk(k, s >> (7 + k), sc, null, null, sx);
                    hsR = HashShell(hsR, sc);
                }
            }

            Directory.CreateDirectory("Logs/TerrainGen/temp223");
            string line = $"tag {tag}  seed 42  surface {s}  near 18 chunks (non-uniform {nonUniform}) hash {hn:X16}  shells 1-4 hash {hs:X16}"
                        + $"  reused-scratch near {hnR:X16} shells {hsR:X16}";
            File.WriteAllText($"Logs/TerrainGen/temp223/hash_{tag}.txt", line + "\n");
            UnityEngine.Debug.Log("[GenScratchHash] " + line);
            Assert.Greater(nonUniform, 0, "the probe must cover real terrain, not only uniform chunks");
            Assert.AreEqual(hn, hnR, "near: reused per-worker scratch must give the same bytes as one-shot scratch");
            Assert.AreEqual(hs, hsR, "shells: reused per-worker scratch must give the same bytes as one-shot scratch");
        }
        finally { src.Dispose(); }
    }

    // ---- Task 224: adversarial reuse checks (dense chunk first, then sparser ones, scratch also scribbled) ----

    private static void AssertSameChunk(Chunk a, Chunk b, BrickDataPool pool, string what)
    {
        Assert.AreEqual(a.isUniform, b.isUniform, what + ": isUniform");
        if (a.isUniform) { Assert.AreEqual(a.uniformMaterial, b.uniformMaterial, what + ": uniform material"); return; }
        for (int bi = 0; bi < EngineConfig.BRICKS_PER_CHUNK; bi++)
        {
            uint da = a.bricks[bi].data, db = b.bricks[bi].data;
            Assert.AreEqual(da & 0x80000000u, db & 0x80000000u, $"{what}: brick {bi} pooled vs uniform");
            if ((da & 0x80000000u) == 0) Assert.AreEqual(da, db, $"{what}: brick {bi} uniform material");
        }
        for (int z = 0; z < 128; z++)
        for (int y = 0; y < 128; y++)
        for (int x = 0; x < 128; x++)
        {
            byte va = Read(a, pool, x, y, z), vb = Read(b, pool, x, y, z);
            if (va != vb) Assert.Fail($"{what}: voxel ({x},{y},{z}) reused {va} fresh {vb}");
        }
    }

    private static long[] Copy(long[] a) => (long[])a.Clone();

    private static void AssertSameDelta(long[] before, long[] mid, long[] after, int from, int to, string what)
    {
        for (int i = from; i < to; i++)
            Assert.AreEqual(mid[i] - before[i], after[i] - mid[i], $"{what}: stats slot {i} (reused vs fresh)");
    }

    [Test]
    public void VoxelReuseIsExact_DenseThenSparse()
    {
        var src = new TerrainGenChunkSource(42u);
        var pool = new BrickDataPool(EngineConfig.BRICKS_PER_CHUNK * 12);
        var alloc = new ChunkHandleAllocator(32);
        try
        {
            int3 c0 = SurfaceVoxel(src) >> 7;
            // Targets (seed 42: the next column's ground is one chunk lower): its surface chunk (column refilled; most
            // bricks proven air, mats unwritten there, so a stale dense chunk would show through), the one below it
            // (proven-solid bricks), the dense column's own chunk below (column shared, as a worker does), and one that
            // is wholly air (the fast path, never touches the scratch).
            int3[] targets = { c0 + new int3(1, -1, 0), c0 + new int3(1, -2, 0), c0 + new int3(0, -1, 0), c0 + new int3(1, 0, 0) };
            using (var pass = new TerrainGenChunkSource.ColumnPass())
            {
                int voxelTargets = 0; long provenAir = 0, provenSolid = 0;
                string info = "";
                for (int t = 0; t < targets.Length; t++)
                for (int scribble = 0; scribble < 2; scribble++)
                {
                    var dense = new Chunk();
                    src.Generate(c0, dense, alloc, pool, null, pass);   // dense chunk first, on this "worker"'s pass
                    Assert.IsFalse(dense.isUniform, "the dense chunk must be real terrain");
                    if (scribble == 1) pass.Scribble((byte)(0xA5 + t));   // worst case: every scratch byte junk
                    var before = Copy(src.SkipStats);
                    var reused = new Chunk();
                    src.Generate(targets[t], reused, alloc, pool, null, pass);
                    var mid = Copy(src.SkipStats);
                    var fresh = new Chunk();
                    src.Generate(targets[t], fresh, alloc, pool, null, null);   // fresh one-shot scratch
                    AssertSameDelta(before, mid, Copy(src.SkipStats), 0, 4, $"target {targets[t]} scribble {scribble}");
                    AssertSameChunk(reused, fresh, pool, $"target {targets[t]} scribble {scribble}");
                    if (mid[3] > before[3]) voxelTargets++;   // went through the voxel pass (uses the scratch)
                    provenAir += mid[0] - before[0]; provenSolid += mid[2] - before[2];
                    if (scribble == 0) info += $" {targets[t]}: uniform {reused.isUniform} voxel {mid[3] - before[3]} air {mid[0] - before[0]} solid {mid[2] - before[2]};";
                    foreach (var c in new[] { dense, reused, fresh })
                        if (!c.isUniform) { for (int bi = 0; bi < EngineConfig.BRICKS_PER_CHUNK; bi++) { uint d = c.bricks[bi].data;
                            if ((d & 0x80000000u) != 0) pool.Free((int)(d & 0x3FFFFFFFu)); } alloc.Free(c.bricks); }
                }
                UnityEngine.Debug.Log("[GenScratchReuse] near targets:" + info);
                Assert.GreaterOrEqual(voxelTargets, 4, "at least two targets must go through the voxel pass:" + info);
                Assert.Greater(provenAir, 0, "some bricks must be proven air (mats left unwritten there):" + info);
                Assert.Greater(provenSolid, 0, "some bricks must be proven solid:" + info);
            }
        }
        finally { pool.Dispose(); src.Dispose(); }
    }

    [Test, Category("Slow")]   // ~12 s
    public void ShellScratchReuseIsExact_DenseThenSparse()
    {
        var src = new TerrainGenChunkSource(42u);
        try
        {
            int3 s = SurfaceVoxel(src);
            int filled = 0;
            using (var sx = new TerrainGenChunkSource.ShellScratch())
            using (var scDense = new TerrainGenChunkSource.ShellChunk())
            using (var scR = new TerrainGenChunkSource.ShellChunk())
            using (var scF = new TerrainGenChunkSource.ShellChunk())
            {
                for (int k = 1; k <= 3; k++)
                {
                    int3 c = s >> (7 + k);
                    int3[] targets = { c + new int3(1, 0, 0), c + new int3(1, 1, 0), c + new int3(0, -1, 1) };
                    foreach (var tc in targets)
                    for (int scribble = 0; scribble < 2; scribble++)
                    {
                        src.GenerateShellChunk(k, c, scDense, null, null, sx);   // dense surface shell chunk first
                        Assert.IsFalse(scDense.IsUniformAir, $"shell {k}: the dense chunk must be real terrain");
                        if (scribble == 1) sx.Scribble((byte)(0x5A + k));
                        var b0 = Copy(src.SkipStats); var f0 = Copy(src.FillStats);
                        src.GenerateShellChunk(k, tc, scR, null, null, sx);
                        var b1 = Copy(src.SkipStats); var f1 = Copy(src.FillStats);
                        src.GenerateShellChunk(k, tc, scF, null, null, null);   // fresh one-shot scratch
                        string what = $"shell {k} target {tc} scribble {scribble}";
                        AssertSameDelta(b0, b1, Copy(src.SkipStats), 4, 8, what);
                        AssertSameDelta(f0, f1, Copy(src.FillStats), 0, 8, what);
                        Assert.AreEqual(scF.IsUniformAir, scR.IsUniformAir, what + ": uniform air");
                        Assert.AreEqual(scF.FillRan, scR.FillRan, what + ": fill ran");
                        if (scR.FillRan) filled++;
                        if (scR.IsUniformAir) continue;
                        Assert.AreEqual(HashShell(FNV_OFFSET, scF), HashShell(FNV_OFFSET, scR), what + ": bytes");
                    }
                }
            }
            Assert.GreaterOrEqual(filled, 4, "the fill must run on several targets for this to test its scratch");
        }
        finally { src.Dispose(); }
    }
}
