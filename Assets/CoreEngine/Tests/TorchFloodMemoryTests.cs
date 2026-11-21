// ==========================================
// TorchFloodMemoryTests.cs -- task 236
//
// The night torch scene peaked at ~6.0 GB (day 2.57 GB) whenever the torch flood was built. Cause (task 236's
// `footprint` samples): IOAccelerator (Metal) memory -- one GraphicsBuffer.SetData per dirty data run, each staging
// its own GPU memory until the GPU finished the frame; the synchronous rebuild did 14 merges' uploads in ONE frame.
// EditMode has no GPU (pools run with Gpu false), so these tests bound the two halves that do run here:
//   - DirtySpan: the upload's dirty set becomes ONE span (one SetData per buffer per Upload, -tfupload 1)
//   - one flood job's CPU-side transient (the job itself was suspected first; it holds only its Persistent
//     scratch): a torch on a post in rough ground, footprint sampled every ~0.5 ms while the pool floods it.
// ==========================================

using System;
using System.Threading;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using VoxelEngine.Memory;
using VoxelEngine.Rendering;
using VoxelEngine.Streaming;

public class TorchFloodMemoryTests
{
    [System.Runtime.InteropServices.DllImport("/usr/lib/libSystem.B.dylib")]
    private static extern int proc_pid_rusage(int pid, int flavor, byte[] buffer);
    [System.Runtime.InteropServices.DllImport("/usr/lib/libSystem.B.dylib")]
    private static extern int getpid();

    private static long Footprint(byte[] buf)
    {
        if (proc_pid_rusage(getpid(), 4, buf) != 0) return -1;
        return (long)BitConverter.ToUInt64(buf, 72);
    }

    private BrickDataPool _pool;
    private TorchFloodPool _flood;

    [TearDown]
    public void Teardown()
    {
        _flood?.Dispose(); _flood = null;
        _pool?.Dispose(); _pool = null;
    }

    /// One torch, reach 12 m, in ground strewn with small stone blocks (most bricks searched, not direct).
    /// Returns (sampled footprint peak - footprint before) in MB, and the flood's reached voxels.
    private long FloodTransientMB(out float reached)
    {
        _pool = new BrickDataPool(80000);
        var store = new ChunkStore(_pool, new ChunkHandleAllocator(512));
        for (int cz = 0; cz < 3; cz++)
        for (int cy = 0; cy < 3; cy++)
        for (int cx = 0; cx < 3; cx++)
            store.InsertChunk(new Chunk { coord = new int3(cx, cy, cz), isUniform = true, uniformMaterial = cy == 0 ? Materials.Stone : (byte)0 });
        var edits = new EditService();
        edits.AttachWorld(store, store, store, null);
        var ops = new EditOps(EditOpLog.InMemory(236u), edits, store);
        var t = new int3(192, 143, 192);
        var rng = new System.Random(236);
        for (int i = 0; i < 900; i++)
        {
            var c = new int3(t.x + rng.Next(-118, 119), 128 + rng.Next(0, 30), t.z + rng.Next(-118, 119));
            if (math.abs(c.x - t.x) < 4 && math.abs(c.z - t.z) < 4) continue;
            ops.Record(new EditOp(EditOpKind.Box, c, (ushort)rng.Next(1, 4), Materials.Stone));
        }
        for (int y = 128; y < 143; y++) ops.Record(new EditOp(EditOpKind.Box, new int3(t.x, y, t.z), 0, Materials.Stone));
        ops.Record(new EditOp(EditOpKind.Box, t, 0, Materials.Torch));

        _flood = new TorchFloodPool(store, 12f, false);
        _flood.SetTorches(new[] { (t, Materials.Torch) });
        GC.Collect();
        var buf = new byte[512];
        long before = Footprint(buf), max = before;
        bool stop = false;
        var sampler = new Thread(() =>
        {
            var b = new byte[512];
            while (!Volatile.Read(ref stop)) { long f = Footprint(b); if (f > Interlocked.Read(ref max)) Interlocked.Exchange(ref max, f); Thread.Sleep(0); }
        });
        sampler.Start();
        _flood.Flush();
        long after = Footprint(buf);
        Volatile.Write(ref stop, true);
        sampler.Join();
        Assert.AreEqual(1, _flood.FloodsDone, "one flood ran");
        reached = _flood.FloodLog[0].y;
        long scratch = _flood.ScratchBytes + _flood.CacheBytes + _flood.UnionBytes;
        TestContext.WriteLine($"[task 236] flood: reached {reached:F0} voxels, lit bricks {_flood.FloodLog[0].z:F0}, direct {_flood.FloodLog[0].w:F0}, " +
                              $"{_flood.FloodLog[0].x:F0} ms | footprint before {before >> 20} MB, sampled peak {max >> 20} MB (+{(max - before) >> 20}), " +
                              $"after {after >> 20} MB (+{(after - before) >> 20}) | pool's own scratch + cache + union {scratch >> 20} MB");
        return (max - before) >> 20;
    }

    [Test]
    public void DirtySpan_ScatteredUnits_OneSpan_AndClears()
    {
        var d = new NativeArray<ulong>(64, Allocator.Temp);
        int units = 64 * 64 - 10;
        var rng = new System.Random(236);
        int lo = int.MaxValue, hi = -1, runsOld = 0, last = -1000;
        for (int u = 3; u < units; u += 65 + rng.Next(0, 40))   // > 64 apart: the old path made one SetData per unit
        {
            d[u >> 6] |= 1UL << (u & 63);
            lo = math.min(lo, u); hi = math.max(hi, u);
            if (u - last > 64) runsOld++;
            last = u;
        }
        d[(units + 3) >> 6] |= 1UL << ((units + 3) & 63);   // a stale bit past the length: ignored, as before
        Assert.Greater(runsOld, 30, "the old path's SetData calls for this set");
        Assert.IsTrue(TorchFloodPool.DirtySpan(d, units, out int u0, out int u1));
        Assert.AreEqual(lo, u0); Assert.AreEqual(hi, u1);
        for (int w = 0; w < d.Length; w++) Assert.AreEqual(0UL, d[w], $"word {w} cleared");
        Assert.IsFalse(TorchFloodPool.DirtySpan(d, units, out _, out _), "nothing dirty: no upload");
        d[0] = 1UL << 63; d[63] = 1UL;   // units 63 and 4032: the span reaches the last dirty unit below the length
        Assert.IsTrue(TorchFloodPool.DirtySpan(d, units, out u0, out u1));
        Assert.AreEqual(63, u0); Assert.AreEqual(4032, u1);
        d.Dispose();
    }

    [Test]
    public void OneFlood_UntrackedTransient_IsBounded()
    {
        long mb = FloodTransientMB(out float reached);
        Assert.Greater(reached, 1_000_000f, "the scene searches many voxels (the case that piled memory up)");
        // The job's own scratch (TorchFloodScratch, ~13 MB at reach 12 m) and the cache are Persistent; anything
        // per searched voxel is not allowed. 64 MB = the scratch with a wide margin for the Editor's own noise.
        Assert.Less(mb, 64, "one flood's footprint transient (MB)");
    }
}
