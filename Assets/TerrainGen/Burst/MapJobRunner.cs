// Assets/TerrainGen/Burst/MapJobRunner.cs
//
// ENGINE PLAN STEP 1 (owner Q26 (a), 2026-10-09): the world-map generator's
// Burst job. TerrainGen itself references Unity.Mathematics only (its
// portability contract, TerrainSpec.cs), so the job that compiles it lives in
// this companion assembly and registers itself as MapJobs.Runner when the
// Editor loads (InitializeOnLoadMethod) and when the player starts
// (RuntimeInitializeOnLoadMethod, SubsystemRegistration: before any scene).
// A project that copies TerrainGen copies this folder with it; without it,
// WorldMap.Build throws (there is no managed fallback).
//
// On macOS it also gives the map arena its memory straight from the OS (mmap /
// munmap): a freed malloc block keeps its dirty pages in the process there
// (measured 2026-10-10: 100 MB touched, freed, still 101 MB of footprint), and
// the build touches ~100 MB.
using System;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using UnityEngine;
using TerrainGen;

namespace TerrainGenBurst
{
    /// The chunk jobs' float settings (TerrainGenChunkSource): Strict, High.
    [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High, CompileSynchronously = true)]
    internal unsafe struct GenJob : IJob
    {
        [NativeDisableUnsafePtrRestriction] public GenIO* io;

        public void Execute()
        {
            int managed = 0;
            MarkManaged(ref managed);
            io->ranManaged = managed;
            MapJobs.Execute(io);
        }

        /// Removed by Burst: `m` stays 0 only when the job ran Burst-compiled.
        [BurstDiscard] private static void MarkManaged(ref int m) => m = 1;
    }

    public static unsafe class MapJobRunner
    {
        public static void Run(IntPtr io) => new GenJob { io = (GenIO*)io }.Run();

#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
        [DllImport("libc")] private static extern IntPtr mmap(IntPtr addr, UIntPtr len, int prot, int flags, int fd, IntPtr offset);
        [DllImport("libc")] private static extern int munmap(IntPtr addr, UIntPtr len);
        private const int PROT_READ_WRITE = 3, MAP_PRIVATE_ANON = 0x0002 | 0x1000;

        private static IntPtr Map(long bytes)
        {
            IntPtr p = mmap(IntPtr.Zero, (UIntPtr)(ulong)bytes, PROT_READ_WRITE, MAP_PRIVATE_ANON, -1, IntPtr.Zero);
            if (p == new IntPtr(-1)) throw new OutOfMemoryException("TerrainGen: mmap of the map arena failed");
            return p;
        }

        private static void Unmap(IntPtr p, long bytes) => munmap(p, (UIntPtr)(ulong)bytes);
#endif

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#endif
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Register()
        {
            MapJobs.Runner = Run;
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
            MapJobs.BlockAlloc = Map;
            MapJobs.BlockFree = Unmap;
#endif
        }
    }
}
