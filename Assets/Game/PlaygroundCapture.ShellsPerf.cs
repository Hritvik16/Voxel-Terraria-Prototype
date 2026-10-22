// Assets/Game/PlaygroundCapture.ShellsPerf.cs
//
// TASK 93b (owner, 2026-10-15; Q49 + Amendment 8.15 Q10 (a)): the shells
// performance breakdown. CAPTURE-ONLY helpers; nothing here runs in play.
//   -costsplit with -shells 1: before a path's timed runs, wait up to
//     -shellwait s (default 120) at the pinned pose for shells to fill, log
//     what is still missing per shell, and shoot the -shellsdebug tint still
//     (<run>/stills/<path>_shelltint.png) at the path's first pose.
//     Arms (CostApply): shells_off = RaymarchFeature.DrawShells false (tiers
//     1-2 draw again, shell streaming keeps running); fshade_off = far shading
//     off (FULL keeps the launch's -farshade).
//   -sustained: every frame kept for whole-run p50 / p99 / >33 ms share; with
//     shells, when each shell first had nothing missing or in flight (s from
//     the walk's start) and the stored chunks per shell at the end.
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using VoxelEngine.Mirror;
using VoxelEngine.Rendering;

public partial class PlaygroundCapture
{
    /// Per shell: stored chunks / missing / in flight (FULL = page cap hit).
    private static string ShellCountsLine(ShellStreamer sh)
    {
        var sb = new StringBuilder();
        int stored = 0, missing = 0;
        for (int k = sh.FirstShell; k <= sh.LastShell; k++)
        {
            int st = sh.Store(k).ChunksStored, m = sh.Missing(k);
            stored += st; missing += m;
            sb.Append($"  s{k} {st}/{m}/{sh.InFlightIn(k)}{(sh.ShellFull(k) ? " FULL" : "")}");
        }
        return $"stored/missing/in flight:{sb}   total stored {stored}, missing {missing}, pending {sh.Pending}; " +
               $"requested {sh.Requested}, generated {sh.Generated} ({(sh.Generated > 0 ? sh.GenMsTotal / sh.Generated : 0):F0} ms each on a worker), " +
               $"dropped stale {sh.DroppedStale}, over-budget frames {sh.OverBudgetFrames} (of {Time.frameCount})";
    }

    /// Task 93b2: chunks held per shell, and FULL.
    private static string ShellHeldLine(ShellStreamer sh)
    {
        var sb = new StringBuilder();
        for (int k = sh.FirstShell; k <= sh.LastShell; k++)
            sb.Append($"  s{k} {sh.Store(k).ChunksStored}/{sh.Store(k).ChunksStored + sh.Missing(k) + sh.InFlightIn(k)}{(sh.ShellFull(k) ? " FULL" : "")}");
        return sb.ToString();
    }

    /// Task 93b2 (-walkshellwait S): wait at the walk's start pose until every
    /// shell has nothing missing (or is FULL), at most S s; log when each did.
    private IEnumerator WalkShellPrefill(ShellStreamer sh, float wait)
    {
        float t0 = Time.realtimeSinceStartup, nextLog = 10f;
        var doneAt = new float[sh.Layout.ShellCount];
        for (int k = 0; k < doneAt.Length; k++) doneAt[k] = -1f;
        while (Time.realtimeSinceStartup - t0 < wait)
        {
            float t = Time.realtimeSinceStartup - t0;
            ShellTrack(sh, doneAt, t);
            if (t >= nextLog) { L($"  [shells] start pose t+{t:F0} s: held{ShellHeldLine(sh)}"); nextLog += 10f; }
            bool all = true;
            for (int k = sh.FirstShell; k <= sh.LastShell; k++) all &= doneAt[k] >= 0f;
            if (all) break;
            yield return null;
        }
        L($"  [shells] start-pose wait {Time.realtimeSinceStartup - t0:F0} s (cap {wait:F0} s); complete at (s; - = not within the wait):{ShellDoneLine(sh, doneAt)}");
    }

    /// Marks, per shell, the first time (s since t0) nothing was missing or in flight.
    private static void ShellTrack(ShellStreamer sh, float[] doneAt, float t)
    {
        for (int k = sh.FirstShell; k <= sh.LastShell; k++)
            if (doneAt[k] < 0f && (sh.ShellFull(k) || (sh.Missing(k) == 0 && sh.InFlightIn(k) == 0)))
                doneAt[k] = t;
    }

    private static string ShellDoneLine(ShellStreamer sh, float[] doneAt)
    {
        var sb = new StringBuilder();
        for (int k = sh.FirstShell; k <= sh.LastShell; k++)
            sb.Append(doneAt[k] < 0f ? $"  s{k} -" : $"  s{k} {doneAt[k]:F0}");
        return sb.ToString();
    }

    /// -costsplit + shells: fill wait at the pinned pose, then the tint still.
    private IEnumerator CostShellWait(ClipPath path, Camera cam, string tintStill)
    {
        var sh = ShellStreamer.Active;
        if (sh == null) yield break;
        float wait = ArgFloat("-shellwait", 120f), t0 = Time.realtimeSinceStartup;
        var doneAt = new float[sh.Layout.ShellCount];
        for (int k = 0; k < doneAt.Length; k++) doneAt[k] = -1f;
        while (Time.realtimeSinceStartup - t0 < wait)
        {
            ShellTrack(sh, doneAt, Time.realtimeSinceStartup - t0);
            int missing = 0;
            for (int k = sh.FirstShell; k <= sh.LastShell; k++) if (!sh.ShellFull(k)) missing += sh.Missing(k);
            if (missing == 0 && sh.InFlight == 0 && sh.Pending == 0) break;
            yield return null;
        }
        L($"  [shells] fill wait {Time.realtimeSinceStartup - t0:F0} s (cap {wait:F0} s); {ShellCountsLine(sh)}");
        L($"  [shells] complete at (s; - = not within the wait):{ShellDoneLine(sh, doneAt)}");
        cam.transform.position = path.pos[0]; cam.transform.rotation = path.rot[0];
        bool hideUi = Playground.DebugHideUi, mapOn = Minimap.Visible;
        Playground.DebugHideUi = true; Minimap.Visible = false;
        RaymarchFeature.ShellTint = true;
        yield return Frames(30);
        ScreenCapture.CaptureScreenshot(tintStill); yield return Frames(2);
        RaymarchFeature.ShellTint = false;
        Playground.DebugHideUi = hideUi; Minimap.Visible = mapOn;
        yield return Frames(4);
    }

    /// -sustained: the whole run's frame-time percentiles (every frame kept).
    private static string WholeRunLine(List<float> ms)
    {
        if (ms.Count == 0) return "  WHOLE RUN: no frames";
        int over = 0; foreach (float v in ms) if (v > 33f) over++;
        return $"  WHOLE RUN ({ms.Count} frames): p50 {Pct(ms, 0.5f):F2} ms   p99 {Pct(ms, 0.99f):F2} ms   " +
               $">33 ms {100f * over / ms.Count:F2}% of frames";
    }
}
