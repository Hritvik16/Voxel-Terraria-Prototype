// Task 218: does Allocator.Temp inside a job .Run() on a System.Threading.Thread (the chunk / shell
// worker path: StreamManager.WorkerLoop / ShellStreamer.WorkerLoop -> ColumnJob / ShellColumnJob .Run())
// ever give its memory back? Explicit probe: writes Logs/TerrainGen/temp218/probe.txt, asserts nothing.
using System;
using System.IO;
using System.Threading;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace VoxelEngine.Tests
{
    public class TempAllocThreadProbeTests
    {
        [BurstCompile]
        private struct TempJobProbe : IJob
        {
            public int bytes;
            public NativeArray<long> sink;
            public void Execute()
            {
                var a = new NativeArray<byte>(bytes, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                for (int i = 0; i < bytes; i += 4096) a[i] = (byte)i;   // dirty every page
                sink[0] += a[bytes - 4096];
                a.Dispose();
            }
        }

        // FarQuery.PointJob's shape: one Temp allocation per Execute(b) of an IJobParallelFor.
        [BurstCompile]
        private struct TempForProbe : IJobParallelFor
        {
            public int bytes;
            [NativeDisableParallelForRestriction] public NativeArray<long> sink;
            public void Execute(int b)
            {
                var a = new NativeArray<byte>(bytes, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                for (int i = 0; i < bytes; i += 4096) a[i] = (byte)i;
                sink[b] = a[bytes - 4096];
                a.Dispose();
            }
        }

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

        private static void Loop(int n, int bytes)
        {
            var sink = new NativeArray<long>(1, Allocator.Persistent);
            for (int i = 0; i < n; i++) new TempJobProbe { bytes = bytes, sink = sink }.Run();
            sink.Dispose();
        }

        [Test, Explicit("task 218 probe")]
        public void TempInRunJob_WorkerThread_vs_MainThread()
        {
            const int N = 300, B = 1 << 20;   // 300 x 1 MB
            var sb = new System.Text.StringBuilder();
            long f0 = FootprintMB();
            Loop(N, B);
            long f1 = FootprintMB();
            sb.AppendLine($"main thread  : {N} x 1 MB Temp in job.Run(): footprint {f0} -> {f1} MB (+{f1 - f0})");
            long f2 = FootprintMB();
            var th = new Thread(() => Loop(N, B)) { IsBackground = true };
            th.Start(); th.Join();
            long f3 = FootprintMB();
            sb.AppendLine($"worker thread: {N} x 1 MB Temp in job.Run(): footprint {f2} -> {f3} MB (+{f3 - f2})");
            long f4 = FootprintMB();
            var th2 = new Thread(() => Loop(N, B)) { IsBackground = true };
            th2.Start(); th2.Join();
            long f5 = FootprintMB();
            sb.AppendLine($"worker thread, 2nd thread: footprint {f4} -> {f5} MB (+{f5 - f4})");
            const int NB = 4000, BB = 64 << 10;   // 4,000 batches x 64 KB = 250 MB if nothing is given back before the job ends
            var sink = new NativeArray<long>(NB, Allocator.Persistent);
            long f6 = FootprintMB();
            new TempForProbe { bytes = BB, sink = sink }.Schedule(NB, 1).Complete();
            long f7 = FootprintMB();
            sb.AppendLine($"IJobParallelFor Schedule: {NB} batches x 64 KB Temp: footprint {f6} -> {f7} MB (+{f7 - f6})");
            new TempForProbe { bytes = BB, sink = sink }.Run(NB);
            long f8 = FootprintMB();
            sb.AppendLine($"IJobParallelFor Run      : {NB} batches x 64 KB Temp: footprint {f7} -> {f8} MB (+{f8 - f7})");
            sink.Dispose();
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs/TerrainGen/temp218");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "probe.txt"), sb.ToString());
            Debug.Log("[temp218] " + sb);
        }
    }
}
