// Assets/CoreEngine/Tests/PointTreeMemoryTests.cs
//
// Task 218: the point query (FarQuery.PointJob, one IJobParallelFor) with the world trees (-trees2, default
// since 214) re-gathered the library trees for EVERY new column and GatherLib took its scratch from
// Allocator.Temp, which a parallel-for job gives back only when the whole job ends (TempAllocThreadProbeTests:
// 4,000 batches x 64 KB = +235 MB). The survey's -mirrorprobe asks ~1M points in one call at jungle_high:
// ~23 KB a column -> the player passed 9-10 GB and the memory watchdog killed it (task 216).
//   PointQuery_Trees2_FootprintFlat   16,384 air points in 16,384 distinct columns, one GetVoxels call:
//                                     the process footprint may not grow by more than 48 MB
//                                     (Logs/TerrainGen/temp218/point.txt).
using System;
using System.IO;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using VoxelEngine;
using VoxelEngine.Memory;
using VoxelEngine.Streaming;
using VoxelEngine.WorldGen;

public class PointTreeMemoryTests
{
    [System.Runtime.InteropServices.DllImport("/usr/lib/libSystem.B.dylib")]
    private static extern int proc_pid_rusage(int pid, int flavor, byte[] buffer);
    [System.Runtime.InteropServices.DllImport("/usr/lib/libSystem.B.dylib")]
    private static extern int getpid();

    private static long FootprintMB()
    {
        var buf = new byte[512];
        if (proc_pid_rusage(getpid(), 4, buf) != 0) return -1;
        return (long)(BitConverter.ToUInt64(buf, 72) >> 20);
    }

    private sealed class Empty : IDisposable
    {
        private readonly BrickDataPool _pool = new BrickDataPool(16);
        public readonly ChunkStore store;
        public Empty() { store = new ChunkStore(_pool, new ChunkHandleAllocator(1)); }
        public void Dispose() => _pool.Dispose();
    }

    [Test]
    public void PointQuery_Trees2_FootprintFlat()
    {
        int keep = TerrainGenChunkSource.Trees2TestOverride;
        TerrainGenChunkSource.Trees2TestOverride = 1;   // the default world: dense world trees
        TerrainGenChunkSource src;
        try { src = new TerrainGenChunkSource(42u); src.SetTrees(true); }
        finally { TerrainGenChunkSource.Trees2TestOverride = keep; }
        using var srcD = src;
        // Task 192's forest pose (Trees2Tests' oak), 128 x 128 columns 3 voxels apart = 3 x 3 chunk columns.
        // y 7000 voxels (700 m): air above that ground (~530 m) and its trees, so every point runs the tree step (PointTree).
        const int G = 128;
        int cx = 33664 - 192, cz = 49280 - 192;
        var pts = new NativeArray<int3>(G * G, Allocator.TempJob);
        var res = new NativeArray<byte>(G * G, Allocator.TempJob);
        try
        {
            for (int j = 0; j < G; j++) for (int i = 0; i < G; i++) pts[j * G + i] = new int3(cx + 3 * i, 7000, cz + 3 * j);
            using var empty = new Empty();
            using var fq = new FarQuery(empty.store, empty.store, src, EditOpLog.InMemory(42u), null, 42u);
            // warm: one small call builds the job / Burst code and the column caches
            var p0 = new NativeArray<int3>(64, Allocator.TempJob); var r0 = new NativeArray<byte>(64, Allocator.TempJob);
            try { for (int i = 0; i < 64; i++) p0[i] = pts[i]; fq.GetVoxels(p0, r0); } finally { p0.Dispose(); r0.Dispose(); }
            GC.Collect();
            long f0 = FootprintMB();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            fq.GetVoxels(pts, res);
            sw.Stop();
            long f1 = FootprintMB();
            int air = 0; for (int i = 0; i < res.Length; i++) if (res[i] == Materials.Air) air++;
            string line = $"{G * G} points (distinct columns, y 7000 voxels), one GetVoxels: footprint {f0} -> {f1} MB (+{f1 - f0}), " +
                          $"{sw.Elapsed.TotalMilliseconds:F0} ms, air {air}";
            Directory.CreateDirectory(Path.Combine("Logs", "TerrainGen", "temp218"));
            File.WriteAllText(Path.Combine("Logs", "TerrainGen", "temp218", "point.txt"), line + "\n");
            UnityEngine.Debug.Log("[temp218] " + line);
            Assert.Greater(air, G * G / 2, "the points are meant to be air (the tree step runs only for air): " + line);
            Assert.Less(f1 - f0, 48, "the point query's footprint grew: " + line);
        }
        finally { pts.Dispose(); res.Dispose(); }
    }
}
