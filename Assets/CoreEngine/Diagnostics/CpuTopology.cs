// ==========================================
// Assets/CoreEngine/Diagnostics/CpuTopology.cs
//
// Performance-core count, queried rather than assumed.
//
// WHY THIS EXISTS: StreamManager sized its generation worker pool from
// Environment.ProcessorCount - 1, which on this machine is 7. But an M1 Air is
// 4 PERFORMANCE + 4 EFFICIENCY cores, and ProcessorCount counts all eight, so
// the pool oversubscribes the only cores that can actually help -- and it is
// those same performance cores that Unity's main and render threads need.
// Measured consequence: frames of 1000ms+ while every Unity counter reported a
// healthy ~21ms frame, because the main thread was not being SCHEDULED.
//
// NEITHER .NET NOR UNITY EXPOSES A PERFORMANCE-CORE COUNT.
// Environment.ProcessorCount and SystemInfo.processorCount both report the
// total (8 here). The only source on macOS is sysctl:
//
//     hw.nperflevels             number of performance levels (2 on Apple Silicon)
//     hw.perflevel0.logicalcpu   level 0 = the FAST cores  (4 on an M1 Air)
//     hw.perflevel1.logicalcpu   level 1 = the efficiency cores
//
// These keys exist only on Apple Silicon with heterogeneous cores. An Intel Mac
// has a single performance level and no perflevel0 key at all, so every call
// here is failure-tolerant and reports Available=false rather than inventing a
// number. A wrong core count silently mis-sizes the thread pool, which is
// precisely the failure this file exists to fix -- guessing would reintroduce
// it one layer down.
using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace VoxelEngine.Diagnostics
{
    public static class CpuTopology
    {
        /// True only if the performance-core count was actually read from the
        /// OS. False on Intel Macs, non-Apple platforms, or any failure.
        public static bool Available { get; private set; }

        /// Performance ("P") cores, or 0 when Available is false.
        public static int PerformanceCores { get; private set; }

        /// Efficiency ("E") cores, or 0 when unknown.
        public static int EfficiencyCores { get; private set; }

        /// Human-readable provenance, so a log line can state where the number
        /// came from instead of leaving the reader to trust it.
        public static string Source { get; private set; } = "not queried";

        private static bool _queried;

        public static void EnsureQueried()
        {
            if (_queried) return;
            _queried = true;

#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
            if (TryReadInt("hw.perflevel0.logicalcpu", out int p) && p > 0)
            {
                PerformanceCores = p;
                Available = true;
                Source = "sysctl hw.perflevel0.logicalcpu";

                if (TryReadInt("hw.perflevel1.logicalcpu", out int e) && e > 0)
                    EfficiencyCores = e;
                return;
            }

            Source = "sysctl hw.perflevel0.logicalcpu unavailable " +
                     "(Intel Mac, or homogeneous cores, or sysctl failed)";
#else
            Source = "not macOS";
#endif
            Available = false;
            PerformanceCores = 0;
        }

        /// Worker count bounded to performance cores, leaving RESERVED of them
        /// for Unity's main and render threads. Returns fallback when the
        /// topology could not be read -- the caller decides what fallback means,
        /// this never fabricates a topology.
        public static int WorkersForPerformanceCores(int reserved, int fallback)
        {
            EnsureQueried();
            if (!Available) return fallback;
            return Mathf.Max(1, PerformanceCores - reserved);
        }

        public static string Describe()
        {
            EnsureQueried();
            return Available
                ? $"P={PerformanceCores} E={EfficiencyCores} total={SystemInfo.processorCount} via {Source}"
                : $"performance-core count UNAVAILABLE ({Source}); total={SystemInfo.processorCount}";
        }

#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
        [DllImport("libc", SetLastError = true)]
        private static extern int sysctlbyname(string name, IntPtr oldp, ref IntPtr oldlenp,
                                               IntPtr newp, IntPtr newlen);

        private static bool TryReadInt(string name, out int value)
        {
            value = 0;
            IntPtr buf = IntPtr.Zero;
            try
            {
                IntPtr len = new IntPtr(sizeof(int));
                buf = Marshal.AllocHGlobal(sizeof(int));
                int rc = sysctlbyname(name, buf, ref len, IntPtr.Zero, IntPtr.Zero);
                if (rc != 0 || len.ToInt64() != sizeof(int)) return false;
                value = Marshal.ReadInt32(buf);
                return true;
            }
            catch (Exception e)
            {
                // DllImport can fail outright under some scripting backends.
                // Treated as "unavailable", never as a reason to guess.
                Debug.LogWarning($"[CpuTopology] sysctl '{name}' threw: {e.GetType().Name} {e.Message}");
                return false;
            }
            finally
            {
                if (buf != IntPtr.Zero) Marshal.FreeHGlobal(buf);
            }
        }
#endif
    }
}
