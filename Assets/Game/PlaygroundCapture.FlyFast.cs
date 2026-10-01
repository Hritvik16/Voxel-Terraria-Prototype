// Assets/Game/PlaygroundCapture.FlyFast.cs
//
// -flyfast : THE OWNER'S STUTTER, REPRODUCED. The demo's fly camera moves at
// 14 m/s x 4 with Shift = 56 m/s, and the owner reported 25-35 FPS with
// stutter near the ground. Every existing sustained arm moves at 12 m/s on a
// 60 m circle, which never leaves the resident window -- it cannot see a
// streaming or far-ring cost that only exists when the camera CROSSES terrain.
//
// Motion: a straight line through the island centre at -flyspeed (default
// 60 m/s) and a fixed ABSOLUTE height -flyheight (default 20 m; the tallest
// sizeClass 1 terrain is 12.8 m, so the camera is always in air -- checked per
// frame against the generator and reported). The line is -flyleg metres long
// (default 1500, inside the 954 m coast radius at both ends); at each end the
// heading reverses (ping-pong). Reversal frames are counted, not hidden.
//
// Per frame it records wall-clock ms (Time.unscaledDeltaTime) and, aligned to
// the SAME frame, the far band's CPU phases (FarField.LastFrame: harvest,
// full-level apply, staging SetData, schedule, residency) and its GPU work
// COUNTS (strip scatters, mip-rebuilt levels), plus the streamer's ms and its
// pending loads. GPU cost of the upkeep cannot be timed here (no GPU-stage
// attribution in this workflow, CLAUDE.md); -farmipstress N is the isolation
// arm for it (rebuild N levels' mips EVERY frame) -- the frame-time delta of
// that arm, divided by N, bounds one whole-level mip rebuild.
//
// Wall-clock ms per frame is the number; FrameTimingManager's cpu/gpu split is
// reported as a DISTRIBUTION only (its frames lag by an unknown count, so it
// is not aligned per frame and never used for attribution).
//
// Frame times are only comparable between arms of the same binary run in
// their own processes, counterbalanced (run-flyfast.sh).
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Rendering;
using VoxelEngine.WorldGen;

public partial class PlaygroundCapture
{
    private IEnumerator FlyFast()
    {
        Screen.SetResolution(1920, 1080, !HasFlag("-windowed"));
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;

        var pg = FindAnyObjectByType<Playground>();
        while (Phase4Bootstrapper.Store == null) yield return null;

        int gate = ArgInt("-gateres", 960);
        if (gate > 0)
        {
            RaymarchFeature.GateModeOverride = RaymarchFeature.GateResMode.ForcedCustom;
            RaymarchFeature.CustomGateResolution = new Vector2Int(gate, gate * 9 / 16);
        }
        RaymarchFeature.ShadowQualityLevel = ArgInt("-shadowlevel", 0);
        bool hud = !HasFlag("-nohud");
        Playground.DebugHideUi = !hud;

        for (int i = 0; i < 300; i++) yield return null;
        if (pg != null) { pg.SendMessage("TeleportToArena", SendMessageOptions.DontRequireReceiver); yield return null; }
        SunCycle.SetHours(SunCycle.GoldenHourHours);
        VoxelEngine.Rendering.LightingDriver.ClockPaused = true;
        bool withFluid = !HasFlag("-nofluid");
        if (withFluid && pg != null) pg.SendMessage("DebugOpenAllVents", SendMessageOptions.DontRequireReceiver);

        float speed = ArgFloat("-flyspeed", 60f);
        float height = ArgFloat("-flyheight", 20f);
        float leg = ArgFloat("-flyleg", 1500f);
        float pitch = ArgFloat("-flypitch", height > 100f ? 15f : 3f);
        int seconds = ArgInt("-seconds", 150);
        string tag = ArgValue("-tag") ?? "flyfast";
        string ts = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
        _folder = Path.Combine(Application.persistentDataPath, "FlyFast", $"{ts}_{tag}");
        Directory.CreateDirectory(_folder);

        var st = ColumnSampler.CreateState(Phase4Bootstrapper.Meta);
        var far = FarField.Active;
        var cam = Camera.main;
        var streamer = Phase4Bootstrapper.Streamer;
        try
        {
            // The line: along +x through the island centre (voxel units in st).
            float cx = st.centerX * 0.1f, cz = st.centerZ * 0.1f;
            Vector3 a = new Vector3(cx - leg * 0.5f, height, cz);
            Vector3 b = new Vector3(cx + leg * 0.5f, height, cz);
            cam.transform.position = a;
            cam.transform.rotation = Quaternion.Euler(pitch, 90f, 0f);
            // Settle AT THE START POSE: the teleport from the arena is a
            // window-sized jump, and its refill is not what this probe measures.
            for (int i = 0; i < 600; i++) yield return null;
            for (int w = 0; w < 1800 && far != null && far.Busy; w++) yield return null;
            for (int w = 0; w < 1800 && streamer != null && streamer.PendingLoads > 0; w++) yield return null;

            L($"FLY-FAST STUTTER PROBE -- {tag} -- {ts}");
            L($"device {SystemInfo.graphicsDeviceName}  {Screen.width}x{Screen.height}  " +
              $"{(Screen.fullScreen ? "FULLSCREEN" : "WINDOWED")}  {(Debug.isDebugBuild ? "DEVELOPMENT" : "RELEASE")}  " +
              $"gate {gate}  shadow rung {RaymarchFeature.ShadowQualityLevel}  HUD {(hud ? "ON" : "OFF")}  fluid {(withFluid ? "LIVE" : "OFF")}");
            L($"clock PINNED at {SunCycle.ClockString()}  sun {SunCycle.ElevationDegrees:F1} deg");
            L($"motion: straight ping-pong line {leg:F0} m along +x through the island centre ({cx:F0}, {cz:F0}) m, " +
              $"{speed:F0} m/s, eye y {height:F1} m ABSOLUTE, pitch {pitch:F0} deg, {seconds} s measured");
            L($"FAR BAND {(far != null ? "ON" : "OFF")}" + (far != null
                ? $"  far shadows {(far.ShadowsOn ? "ON" : "OFF")}  mip stress {FarField.MipStressLevels} levels/frame  initial fill {far.InitialFillMs:F0} ms"
                : ""));

            int cap = seconds * 400;
            var ms = new List<float>(cap);
            var rows = new StringBuilder(cap * 64);
            rows.Append("frame\tms\tfarHarvestMs\tfarApplyFullMs\tfarStagingMs\tfarScheduleMs\tfarResidencyMs\t" +
                        "farStrips\tfarFulls\tfarMipLevels\tstreamMs\tpendingLoads\tinRock\treversal\t" +
                        "stSlideMs\tstDispatchMs\tstDrainMs\tstTransferMs\tstValveMs\tstSweepMs\tstClipUploadMs\tstCascadeMs\t" +
                        "stAdmitted\tstEvicted\tstUploadBytes\tgc0\tgc1\theapMb\n");
            var timings = new FrameTiming[1];
            var cpuMain = new List<float>(cap); var gpu = new List<float>(cap);
            int inRockFrames = 0, reversals = 0, frames = 0;
            float dir = 1f;
            int gc0Prev = GC.CollectionCount(0), gc1Prev = GC.CollectionCount(1);
            float runStart = Time.realtimeSinceStartup;
            long strips0 = far != null ? far.StripsApplied : 0, fulls0 = far != null ? far.FullRefills : 0;
            long bytes0 = far != null ? far.BytesUploaded : 0;
            int deficitMax = 0; long deficitSum = 0; int deficitN = 0;
            yield return null;

            while (Time.realtimeSinceStartup - runStart < seconds)
            {
                bool reversal = false;
                Vector3 p = cam.transform.position + new Vector3(dir * speed * Time.unscaledDeltaTime, 0f, 0f);
                if ((dir > 0f && p.x >= b.x) || (dir < 0f && p.x <= a.x))
                {
                    p.x = dir > 0f ? b.x : a.x;
                    dir = -dir;
                    reversal = true; reversals++;
                }
                cam.transform.position = p;
                cam.transform.rotation = Quaternion.Euler(pitch, dir > 0f ? 90f : 270f, 0f);

                yield return null;
                // Resumed in frame N+1 after Update, before LateUpdate: the
                // delta is frame N's duration, and FarField.LastFrame and the
                // streamer's ms still hold what frame N's LateUpdate did.
                float f = Time.unscaledDeltaTime * 1000f;
                ms.Add(f);
                bool rock = !CameraInAir(in st, cam.transform.position);
                if (rock) inRockFrames++;
                var lf = far != null ? far.LastFrame : default;
                int pending = streamer != null ? streamer.PendingLoads : -1;
                rows.Append(frames).Append('\t').Append(f.ToString("F3")).Append('\t')
                    .Append(lf.harvestMs.ToString("F3")).Append('\t').Append(lf.applyFullMs.ToString("F3")).Append('\t')
                    .Append(lf.stagingMs.ToString("F3")).Append('\t').Append(lf.scheduleMs.ToString("F3")).Append('\t')
                    .Append(lf.residencyMs.ToString("F3")).Append('\t')
                    .Append(lf.strips).Append('\t').Append(lf.fulls).Append('\t').Append(lf.mipLevels).Append('\t')
                    .Append(FrameGapProbe.LastStreamerMs.ToString("F3")).Append('\t').Append(pending).Append('\t')
                    .Append(rock ? 1 : 0).Append('\t').Append(reversal ? 1 : 0);
                if (streamer != null)
                    rows.Append('\t').Append(streamer.LastSlideMs.ToString("F3")).Append('\t').Append(streamer.LastDispatchMs.ToString("F3"))
                        .Append('\t').Append(streamer.LastDrainCompletionsMs.ToString("F3")).Append('\t').Append(streamer.LastTransferMs.ToString("F3"))
                        .Append('\t').Append(streamer.LastValveMs.ToString("F3")).Append('\t').Append(streamer.LastSweepMs.ToString("F3"))
                        .Append('\t').Append((streamer.LastUploadMs - streamer.LastCascadeMs).ToString("F3")).Append('\t').Append(streamer.LastCascadeMs.ToString("F3"))
                        .Append('\t').Append(streamer.LastAdmitted).Append('\t').Append(streamer.LastEvicted).Append('\t').Append(streamer.LastUploadBytes);
                else rows.Append("\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0");
                // GC collections that completed since the previous frame. A
                // stop-the-world pause lands before Update, outside every phase
                // timer above -- counting them is the only way to see one.
                int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1);
                rows.Append('\t').Append(g0 - gc0Prev).Append('\t').Append(g1 - gc1Prev)
                    .Append('\t').Append((GC.GetTotalMemory(false) / 1048576.0).ToString("F1"));
                gc0Prev = g0; gc1Prev = g1;
                rows.Append('\n');
                frames++;

                FrameTimingManager.CaptureFrameTimings();
                if (FrameTimingManager.GetLatestTimings(1, timings) > 0)
                {
                    cpuMain.Add((float)timings[0].cpuMainThreadFrameTime);
                    gpu.Add((float)timings[0].gpuFrameTime);
                }
                if (streamer != null && (frames % 30) == 0)
                {
                    int d = streamer.LoadDeficit();
                    deficitMax = Math.Max(deficitMax, d); deficitSum += d; deficitN++;
                }
            }

            yield return Shot("99_end_pose", $"eye {cam.transform.position} fwd {cam.transform.forward}");

            ms.Sort();
            float P(List<float> v, double q) => v.Count == 0 ? 0f : v[Math.Min(v.Count - 1, (int)Math.Floor(q * v.Count))];
            int Over(float t) { int n = 0; foreach (var x in ms) if (x > t) n++; return n; }
            double sum = 0; foreach (var x in ms) sum += x;
            L("");
            L($"frames {ms.Count}  mean {sum / Math.Max(1, ms.Count):F2} ms  p50 {P(ms, 0.5):F2}  p90 {P(ms, 0.9):F2}  " +
              $"p99 {P(ms, 0.99):F2}  p99.9 {P(ms, 0.999):F2}  max {(ms.Count > 0 ? ms[ms.Count - 1] : 0):F2}");
            L($"frames >33 ms {Over(33f)} ({1000.0 * Over(33f) / Math.Max(1, ms.Count):F1}/1000)   " +
              $">50 ms {Over(50f)}   >100 ms {Over(100f)}   in rock {inRockFrames}   reversals {reversals}");
            cpuMain.Sort(); gpu.Sort();
            L($"FrameTimingManager (distribution only, not per-frame aligned): cpu main p50 {P(cpuMain, 0.5):F2} p99 {P(cpuMain, 0.99):F2}   " +
              $"gpu p50 {P(gpu, 0.5):F2} p99 {P(gpu, 0.99):F2}  ({gpu.Count} samples)");
            L($"load deficit (every 30th frame): mean {(deficitN > 0 ? (double)deficitSum / deficitN : 0):F1}  max {deficitMax}");
            if (far != null)
                L($"far ring over the run: {far.StripsApplied - strips0} strips, {far.FullRefills - fulls0} full refills, " +
                  $"{(far.BytesUploaded - bytes0) / 1048576.0:F1} MB uploaded");
            L("");
            L("per-frame rows: flyfast_frames.tsv (tools/report-flyfast.py splits them)");
            File.WriteAllText(Path.Combine(_folder, "flyfast_frames.tsv"), rows.ToString());
        }
        finally { st.Dispose(); }

        File.WriteAllText(Path.Combine(_folder, "flyfast.txt"), _log.ToString());
        Debug.Log("[flyfast]\n" + _log);
        yield return null;
        Application.Quit(0);
    }
}
