// Assets/Game/PlaygroundCapture.TorchFloodL3.cs
//
// -costsplit -costpaths torch -tfl3 1 (run-cost-split.sh EXTRA=): TASK 165, TORCH_LIGHT_PLAN L3. In task 151's
// 32-torch camp (night, the same scene task 158 timed), INSTEAD of the timed arms (no frame-time arm: not a perf task):
//   A  the flood switched on with the INCREMENTAL pool (TorchFloodPool.Tick each frame from LightingDriver): frames and
//      seconds until every torch is lit, the main-thread ms of every busy Tick (n, mean, p99, max), the flood jobs'
//      wall ms (schedule -> seen done), GPU / CPU memory, upload bytes
//   B  GPU probe at 16 x 9 pixels: GPU sample == the C# twin of the live union (as task 158)
//   C  a fresh pool rebuilt from scratch, each torch's flood run alone on this thread (its worker cost, timed);
//      the live union must equal it EXACTLY (TorchFloodPool.Diff)
//   D  three edits, each followed by "wait until idle" and the same exactness check + GPU probe:
//      D1 a 2 x 2 m stone wall (0.1 m thick) 1 m beside an inner-ring torch, D2 a 1 x 1 x 1 m pit dug 1.5 m from
//      another, D3 a torch broken (light unregistered, voxel -> air) and placed again
//   stills: tfl3_camp.png after A, tfl3_wall.png after D1 (same pose; look at the wall's far side going dark).
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Rendering;
using VoxelEngine.Streaming;

public partial class PlaygroundCapture
{
    private IEnumerator TorchFloodL3(Camera cam, string stills)
    {
        var store = Phase4Bootstrapper.Store;
        var driver = FindAnyObjectByType<LightingDriver>();
        if (_costTorches == null || _costLights == null || store == null || driver == null) { L("  -tfl3: no torch scene -- skipped"); yield break; }
        L("");
        L($"TORCH FLOOD L3 (task 165): {_costTorches.Count} torches, reach {TorchFloodPool.ReachM:F1} m, dir mode {TorchFloodPool.DirMode}");
        var edits = new EditService();
        edits.AttachWorld(store, store, store, Phase4Bootstrapper.Clipmap);

        // ---- A: async build ----
        TorchFloodPool.Enabled = true;
        var pool = TorchFloodPool.Instance;
        pool.ResetAll(store);
        float t0 = Time.realtimeSinceStartup;
        int frames = 0;
        do { yield return null; frames++; } while ((pool.Busy || !TorchFloodPool.Ready) && frames < 6000);
        float wall = Time.realtimeSinceStartup - t0;
        L($"  A async build: {frames} frames, {wall:F2} s until idle; {L3Stats(pool)}");
        yield return Frames(20);
        Playground.DebugHideUi = true; Minimap.Visible = false;
        yield return Frames(10);
        ScreenCapture.CaptureScreenshot(Path.Combine(stills, "tfl3_camp.png")); yield return Frames(3);

        // ---- B + C ----
        FloodProbe(cam, "A camp");
        L3Exact(pool, store, "A camp");

        // ---- D: edits ----
        var inner = _costTorches.GetRange(0, math.min(16, _costTorches.Count));
        int3 Ground(int x, int z, int yHint)
        {
            int y = yHint + 40;
            for (int i = 0; i < 200 && store.GetVoxel(new int3(x, y, z)) == Materials.Air; i++) y--;
            return new int3(x, y, z);
        }
        // D1: a wall 1 m to the torch's +x side, 2 m wide (z), from the ground 2 m up, 0.1 m thick
        {
            int3 t = inner[0];
            int written = 0;
            for (int dz = -10; dz < 10; dz++)
            {
                int3 g = Ground(t.x + 10, t.z + dz, t.y);
                for (int h = 1; h <= 20; h++) if (edits.TrySetVoxel(new int3(t.x + 10, g.y + h, t.z + dz), Materials.Stone)) written++;
            }
            yield return L3Wait(pool, $"D1 wall beside torch {t} ({written} voxels)");
            FloodProbe(cam, "D1 wall");
            L3Exact(pool, store, "D1 wall");
            ScreenCapture.CaptureScreenshot(Path.Combine(stills, "tfl3_wall.png")); yield return Frames(3);
        }
        // D2: a pit 1.5 m from another torch
        {
            int3 t = inner[math.min(4, inner.Count - 1)];
            int written = 0;
            for (int dx = 0; dx < 10; dx++)
                for (int dz = 0; dz < 10; dz++)
                {
                    int3 g = Ground(t.x + 15 + dx, t.z + dz, t.y);
                    for (int h = 0; h < 10; h++) if (edits.TrySetVoxel(new int3(g.x, g.y - h, g.z), Materials.Air)) written++;
                }
            yield return L3Wait(pool, $"D2 pit beside torch {t} ({written} voxels)");
            FloodProbe(cam, "D2 pit");
            L3Exact(pool, store, "D2 pit");
        }
        // D3: break a torch, then place it again
        {
            int3 t = inner[math.min(8, inner.Count - 1)];
            _costLights.Unregister(t);
            edits.TrySetVoxel(t, Materials.Air);
            yield return L3Wait(pool, $"D3a torch {t} broken");
            L3Exact(pool, store, "D3a broken");
            edits.TrySetVoxel(t, Materials.Torch);
            _costLights.Register(t, Materials.Torch);
            yield return L3Wait(pool, $"D3b torch {t} placed again");
            FloodProbe(cam, "D3b replaced");
            L3Exact(pool, store, "D3b replaced");
        }
        Playground.DebugHideUi = false;
        TorchFloodPool.Enabled = false;
        L("TORCH FLOOD L3 done");
    }

    private IEnumerator L3Wait(TorchFloodPool pool, string what)
    {
        pool.BusyTickMs.Clear();
        int f0 = pool.FloodsDone, m0 = pool.MergesDone; long u0 = pool.UploadBytesTotal;
        float t0 = Time.realtimeSinceStartup;
        int frames = 0;
        do { yield return null; frames++; } while (pool.Busy && frames < 3000);
        L($"  {what}: idle after {frames} frames, {Time.realtimeSinceStartup - t0:F2} s; floods {pool.FloodsDone - f0}, merges {pool.MergesDone - m0}, " +
          $"uploaded {(pool.UploadBytesTotal - u0) / 1024.0:F0} KB; {L3Ticks(pool)}");
        yield return Frames(10);
    }

    private static string L3Ticks(TorchFloodPool pool)
    {
        var t = new List<float>(pool.BusyTickMs);
        if (t.Count == 0) return "main thread: no busy tick";
        t.Sort();
        double sum = 0; foreach (float v in t) sum += v;
        var ph = new System.Text.StringBuilder();
        string[] names = { "edits+poll", "flood done", "merge apply", "upload", "snapshots", "merge start" };
        for (int i = 0; i < 6; i++) ph.Append($"{(i > 0 ? ", " : "")}{names[i]} {pool.PhaseSumMs[i]:F1}/{pool.PhaseMaxMs[i]:F2}");
        pool.ResetPhaseStats();
        return $"main thread per busy frame: n {t.Count}, mean {sum / t.Count:F3} ms, p50 {t[t.Count / 2]:F3}, p99 {t[(int)(0.99f * (t.Count - 1))]:F3}, " +
               $"max {t[t.Count - 1]:F3} (phase sum/max ms: {ph})";
    }

    private static string L3Stats(TorchFloodPool pool)
    {
        var fl = pool.FloodLog;
        double ms = 0, mx = 0, reached = 0, direct = 0, lit = 0;
        foreach (var f in fl) { ms += f.x; mx = math.max(mx, f.x); reached += f.y; lit += f.z; direct += f.w; }
        int n = math.max(1, fl.Count);
        return $"floods {pool.FloodsDone} (job wall mean {ms / n:F1} ms, max {mx:F1}; searched voxels mean {reached / n:N0}, lit bricks mean {lit / n:N0} " +
               $"of which direct {direct / n:N0}), merges {pool.MergesDone}; {pool.Chunks} lit chunks, air bricks {pool.AirBricks}, mixed {pool.MixedBricks}, " +
               $"escapes {pool.Escapes}; GPU {pool.GpuBytes / 1048576.0:F2} MB allocated; CPU content: union {pool.UnionContentBytes / 1048576.0:F2} + caches " +
               $"{pool.CacheContentBytes / 1048576.0:F2} MB, allocated: union {pool.UnionBytes / 1048576.0:F2} + caches {pool.CacheBytes / 1048576.0:F2}; " +
               $"job scratch peak {pool.ScratchPeakBytes / 1048576.0:F2} MB (now {pool.ScratchBytes / 1048576.0:F2}); uploaded {pool.UploadBytesTotal / 1048576.0:F2} MB; {L3Ticks(pool)}";
    }

    /// The live union vs a fresh from-scratch pool (no GPU), each flood run alone on this thread and timed.
    private void L3Exact(TorchFloodPool live, ChunkStore store, string tag)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var fresh = new TorchFloodPool(store, TorchFloodPool.ReachM, false);
        var sig = new List<(int3, byte)>();
        foreach (var r in _costLights.SnapshotRegistered())
            if (!MaterialRules.IsMobile(r.material)) sig.Add((r.voxel, r.material));
        TorchFloodPool.TimeFloods = true;
        fresh.FloodRunMs.Clear();
        fresh.SetTorches(sig);
        fresh.Flush();
        TorchFloodPool.TimeFloods = false;
        double total = sw.Elapsed.TotalMilliseconds;
        string d = TorchFloodPool.Diff(live.Current, fresh.Current);
        var r2 = new List<float>(fresh.FloodRunMs);
        r2.Sort();
        double sum = 0; foreach (float v in r2) sum += v;
        L($"  exact ({tag}): live union vs from-scratch {(d == null ? "IDENTICAL -> PASS" : "DIFFER -> FAIL: " + d)}; scratch rebuild of {sig.Count} torches " +
          $"{total:F0} ms on one thread (floods {sum:F0} ms: per torch mean {(r2.Count > 0 ? sum / r2.Count : 0):F1}, median {(r2.Count > 0 ? r2[r2.Count / 2] : 0):F1}, " +
          $"max {(r2.Count > 0 ? r2[r2.Count - 1] : 0):F1} ms)");
        fresh.Dispose();
        string g = live.VerifyGpu();
        L($"  gpu readback ({tag}): dir / tables / every reachable data word {(g == null ? "== the CPU copy -> PASS" : "DIFFER -> FAIL: " + g)}");
    }
}
