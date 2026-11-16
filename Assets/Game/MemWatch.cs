// Assets/Game/MemWatch.cs
//
// Task 218 DIAGNOSTIC (default OFF): `-memwatch <MB>` logs one `[memwatch]` line every 10 s of real time
// (phys_footprint, Unity's allocated / reserved / graphics-driver totals, the managed heap) and dumps
// MemoryLedger.Report() ONCE when the footprint first reaches <MB>, so a runaway footprint is split by
// owner from inside the player instead of guessed at. Costs one proc_pid_rusage call per 10 s.
using System;
using UnityEngine;

public static class MemWatch
{
    private static int _limitMB = -1;   // -1 = args not read yet, 0 = off
    private static float _next;
    private static bool _dumped;

    public static void Tick()
    {
        if (_limitMB < 0)
        {
            _limitMB = 0;
            var a = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < a.Length; i++)
                if (a[i] == "-memwatch" && int.TryParse(a[i + 1], out int v)) _limitMB = Math.Max(0, v);   // last match wins
            if (_limitMB > 0) Debug.Log($"[memwatch] ON: a line every 10 s, MemoryLedger.Report() once at {_limitMB} MB");
        }
        if (_limitMB == 0 || Time.realtimeSinceStartup < _next) return;
        _next = Time.realtimeSinceStartup + 10f;
        MemoryLedger.ProcessFootprint(out long now, out long peak);
        long mb = now >> 20;
        Debug.Log($"[memwatch] t {Time.realtimeSinceStartup:F0} s frame {Time.frameCount} | footprint {mb} MB (peak {peak >> 20}) | unity allocated " +
                  $"{UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() >> 20} MB, reserved {UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong() >> 20} MB, " +
                  $"gfx driver {UnityEngine.Profiling.Profiler.GetAllocatedMemoryForGraphicsDriver() >> 20} MB, managed heap {GC.GetTotalMemory(false) >> 20} MB, " +
                  $"mono used {UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong() >> 20} / heap {UnityEngine.Profiling.Profiler.GetMonoHeapSizeLong() >> 20} MB");
        if (!_dumped && mb >= _limitMB)
        {
            _dumped = true;
            var sb = new System.Text.StringBuilder($"[memwatch] LEDGER at {mb} MB\n");
            foreach (var line in MemoryLedger.Report()) sb.AppendLine(line);
            Debug.Log(sb.ToString());
        }
    }
}
