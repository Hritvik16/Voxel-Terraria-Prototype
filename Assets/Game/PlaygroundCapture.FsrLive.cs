// Assets/Game/PlaygroundCapture.FsrLive.cs
//
// -lookreview -fsrlive 1 (run-look-review.sh EXTRA=): TASK 171, owner Q107 (a) 2026-11-02.
// CAPTURE-ONLY, a LOOK (no frame times). Task 166's F2 (-fsr3sc 10: FSR 3's auto
// shading-change detection off) under LIVE light and moving water, before it becomes the
// play default: does anything leave a ghost trail or lag behind a light change?
// Every scene is shot per arm (default native, fsr3_432, fsr3_432_sc10, fsr3_540,
// fsr3_540_sc10; -fsrlivearms with LadderArms' syntax) as a burst of CONSECUTIVE frames.
// Everything that moves is stepped per FRAME (not per second), so frame k of every arm
// shows the same state and native frame k is the ground truth (except "pour", below).
//   sun      walk_open (LookReview's pose), clock RUNNING from -fsrlivesunh (16 h): the
//            sun stepped per frame as play's 20-minute day (play) and the rigs' 120 s day
//            (fast, 10x) at 30 fps, plus a still clock (still = the arm's own baseline
//            error vs native). 60 lead + 4 captured frames.
//   slide    noon, the most open of 8 level headings at the start pose, pitched 15 deg
//            down: a 1.5 m water cube 0.4 m over the ground 7 m ahead, slid sideways
//            0.1 m per frame (3 m/s at 30 fps; fluid sim suspended, so it is exact per
//            frame). 60 lead + 4.
//   pour     same view, fluid sim RUNNING: a 2 x 2 x 3 m water column released 1.5 m
//            over the ground; rounds of 24 lead + 4 frames per arm, two rounds (falling
//            and splashing, then spreading). NOT frame-exact across arms (the CA ticks on
//            the wall clock): judged by eye, each arm against its own frames. The water
//            is removed afterwards.
//   torch    night (-nighttod, 0.5 h): task 151's 32-torch scene (CostTorchScene), its
//            first pose. still: 60 lead + 4. on: one more torch 1 m over the ground 5 m
//            ahead, 1.5 m to the side, lit at frame 60 -- frames 59 (before) .. 65 kept;
//            off: it goes out at frame 30, frames 29 .. 35 kept. move: a torch moved
//            sideways 0.1 m per frame, 60 lead + 4. Torches do not flicker in this
//            engine (no flicker code exists), so a lit / unlit / moving torch is the
//            light change there is.
// Shots: <scene>_<arm>_<k>.png in the LookReview folder. Sheets and numbers:
//   python3 -I tools/fsr171.py <LookReview dir> Screenshots/171
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Mirror;
using VoxelEngine.Rendering;
using VoxelEngine.Streaming;

public partial class PlaygroundCapture
{
    private IEnumerator FsrLive(FarField far, Camera cam, StreamManager sm, ShellStreamer sh, ChunkStore store,
                                Vector3 centre, Vector3 openEye, Quaternion openRot, float wait)
    {
        string armArg = ArgValue("-fsrlivearms");
        string[] arms = LadderArms(string.IsNullOrEmpty(armArg) ? "native,432,432_sc10,540,540_sc10" : armArg);
        var pg = FindAnyObjectByType<Playground>();
        var driver = FindAnyObjectByType<LightingDriver>();
        LightingDriver.ClockPaused = true;   // the clock is stepped per FRAME below: the same sun in every arm
        Playground.DebugSuspendFluidSim = true;
        L("");
        L($"FSR LIVE (task 171): arms {string.Join(", ", arms)}; every burst is consecutive frames, state stepped per frame");

        // ---- sun: walk_open, clock running ----
        float h0 = ArgFloat("-fsrlivesunh", 16f);
        SunCycle.SetHours(h0);
        L("");
        L($"== sun: walk_open eye {openEye:F1}, yaw {openRot.eulerAngles.y:F0}; from {h0:F2} h (sun elevation {SunCycle.ElevationDegreesAt(h0):F0} deg)");
        Phase4Bootstrapper.DebugStreamCentre = openEye;
        cam.transform.SetPositionAndRotation(openEye, openRot);
        ApplyCompareArm(arms[Math.Min(1, arms.Length - 1)]);
        yield return NearSettle(far, sm);
        yield return FsrLiveShellWait(sh, wait);
        foreach (var (tag, day) in new[] { ("still", 0f), ("play", 1200f), ("fast", 120f) })
        {
            float dh = day > 0f ? 24f / (day * 30f) : 0f;   // hours per frame at 30 fps
            foreach (string arm in arms)
            {
                ApplyCompareArm(arm);
                cam.ResetProjectionMatrix();
                cam.transform.SetPositionAndRotation(openEye, openRot);
                yield return FsrLiveBurst($"sun_{tag}_{arm}", 60, 4, i => SunCycle.SetHours(h0 + dh * i));
            }
            L($"  sun_{tag}: {(day > 0f ? $"{day:F0} s day = {dh * 15f * 1000f:F1} millideg of hour angle per frame" : "clock still")}, " +
              $"frames 60-63 at {h0 + dh * 60:F3}-{h0 + dh * 63:F3} h");
        }
        SunCycle.SetHours(SunCycle.NoonHours);

        // ---- slide + pour: noon, open level heading at the start pose ----
        var eye = new Vector3(centre.x, WalkEyeY(centre.x, centre.z, centre.y), centre.z);
        float bestYaw = 0f, best = -1f;
        for (int k = 0; k < 8; k++)
        {
            float yaw = k * math.PI / 4f;
            float d = CostFreeDistanceM(eye, new float2(math.sin(yaw), math.cos(yaw)), 400f);
            if (d > best) { best = d; bestYaw = yaw; }
        }
        var fwd = new Vector3(math.sin(bestYaw), 0f, math.cos(bestYaw));
        var rot = Quaternion.Euler(15f, math.degrees(bestYaw), 0f);
        Phase4Bootstrapper.DebugStreamCentre = eye;
        cam.transform.SetPositionAndRotation(eye, rot);
        yield return NearSettle(far, sm);
        yield return FsrLiveShellWait(sh, wait);
        var edits = new EditService();
        edits.AttachWorld(store, store, store, Phase4Bootstrapper.Clipmap);
        Vector3 site = eye + fwd * 7f;
        bool slideAlongX = Mathf.Abs(fwd.x) < Mathf.Abs(fwd.z);   // the voxel axis most across the view
        int3 sc = new int3((int)math.floor(site.x * 10f), 0, (int)math.floor(site.z * 10f));
        const int E = 15, travel = 64;
        int3 axis = slideAlongX ? new int3(1, 0, 0) : new int3(0, 0, 1);
        int3 start = sc - axis * (travel / 2) - (slideAlongX ? new int3(0, 0, E / 2) : new int3(E / 2, 0, 0));
        int3 sweepHi = start + axis * (travel + E - 1) + (slideAlongX ? new int3(0, 0, E - 1) : new int3(E - 1, 0, 0));
        int gMax = int.MinValue;
        for (int z = start.z; z <= sweepHi.z; z += 2)
        for (int x = start.x; x <= sweepHi.x; x += 2)
        {
            float g = StoreGroundM(store, x * 0.1f + 0.05f, z * 0.1f + 0.05f, eye.y + 15f);
            if (!float.IsNaN(g)) gMax = math.max(gMax, (int)math.round(g * 10f));
        }
        if (gMax == int.MinValue) gMax = (int)math.floor((eye.y - 1.6f) * 10f);
        int lift = 4;
        while (lift < 60 && SolidIn(store, new int3(start.x, gMax + lift, start.z), new int3(sweepHi.x, gMax + lift + E - 1, sweepHi.z)) > 0) lift += 5;
        start.y = gMax + lift; sweepHi.y = start.y + E - 1;
        bool slideOk = SolidIn(store, start, sweepHi) == 0;
        L("");
        L($"== slide: eye {eye:F1}, yaw {math.degrees(bestYaw):F0} (free {best:F0} m), pitch 15 down; 1.5 m water cube {lift * 0.1f:F1} m over " +
          $"the highest ground (y {gMax}) 7 m ahead, slid along {(slideAlongX ? "x" : "z")} 1 voxel per frame; sweep {start}..{sweepHi} " +
          $"{(slideOk ? "clear" : "NOT CLEAR -- skipped")}");
        if (slideOk)
        {
            foreach (string arm in arms)
            {
                ApplyCompareArm(arm);
                cam.ResetProjectionMatrix();
                cam.transform.SetPositionAndRotation(eye, rot);
                edits.SetBox(start, start + new int3(E - 1, E - 1, E - 1), Materials.Water);
                yield return FsrLiveBurst($"slide_{arm}", 60, 4, i =>
                {
                    if (i == 0) return;
                    int3 trail = start + axis * (i - 1), lead = start + axis * (i - 1 + E);
                    int3 span = slideAlongX ? new int3(0, E - 1, E - 1) : new int3(E - 1, E - 1, 0);
                    edits.SetBox(trail, trail + span, Materials.Air);
                    edits.SetBox(lead, lead + span, Materials.Water);
                });
                edits.SetBox(start, sweepHi, Materials.Air);
            }
        }

        // ---- pour: fluid sim running ----
        if (pg != null && pg.DebugEdits != null)
        {
            int3 plo = new int3(sc.x - 10, 0, sc.z - 10), phi = new int3(sc.x + 9, 0, sc.z + 9);
            int pg0 = int.MinValue;
            for (int z = plo.z; z <= phi.z; z += 2)
            for (int x = plo.x; x <= phi.x; x += 2)
            {
                float g = StoreGroundM(store, x * 0.1f + 0.05f, z * 0.1f + 0.05f, eye.y + 15f);
                if (!float.IsNaN(g)) pg0 = math.max(pg0, (int)math.round(g * 10f));
            }
            if (pg0 == int.MinValue) pg0 = gMax;
            plo.y = pg0 + 15; phi.y = plo.y + 29;
            int3 rlo = new int3(sc.x - 100, pg0 - 60, sc.z - 100), rhi = new int3(sc.x + 99, phi.y + 2, sc.z + 99);
            var before = new HashSet<int3>();
            for (int z = rlo.z; z <= rhi.z; z++)
            for (int y = rlo.y; y <= rhi.y; y++)
            for (int x = rlo.x; x <= rhi.x; x++)
                if (store.GetVoxel(new int3(x, y, z)) == Materials.Water) before.Add(new int3(x, y, z));
            if (SolidIn(store, plo, phi) == 0)
            {
                Playground.DebugSuspendFluidSim = false;
                int placed = pg.DebugEdits.SetBox(plo, phi, Materials.Water);
                L("");
                L($"== pour: fluid sim RUNNING; {placed} water voxels at {plo}..{phi} (bottom 1.5 m over ground y {pg0}); " +
                  $"water already in the 20 x 20 m clean-up box: {before.Count}");
                for (int r = 0; r < 2; r++)
                    foreach (string arm in arms)
                    {
                        ApplyCompareArm(arm);
                        cam.ResetProjectionMatrix();
                        cam.transform.SetPositionAndRotation(eye, rot);
                        yield return FsrLiveBurst($"pour_r{r}_{arm}", 24, 4, null);
                        int lowest = int.MaxValue, n = 0;
                        for (int z = sc.z - 40; z < sc.z + 40; z++)
                        for (int y = pg0 - 20; y <= phi.y; y++)
                        for (int x = sc.x - 40; x < sc.x + 40; x++)
                            if (store.GetVoxel(new int3(x, y, z)) == Materials.Water) { n++; lowest = math.min(lowest, y); }
                        L($"  pour_r{r}_{arm}: frame {Time.frameCount}, water in the 8 m box {n}, lowest y {(n > 0 ? lowest.ToString() : "-")}, " +
                          $"fluid ticks {pg.DebugFluidTicks}");
                    }
                Playground.DebugSuspendFluidSim = true;
                int cleared = 0;
                for (int z = rlo.z; z <= rhi.z; z++)
                for (int y = rlo.y; y <= rhi.y; y++)
                for (int x = rlo.x; x <= rhi.x; x++)
                {
                    var p = new int3(x, y, z);
                    if (store.GetVoxel(p) == Materials.Water && !before.Contains(p) && pg.DebugEdits.TrySetVoxel(p, Materials.Air)) cleared++;
                }
                L($"  pour: {cleared} water voxels removed afterwards");
                yield return Frames(30);
            }
            else L("pour: column not clear -- skipped");
        }
        else L("pour: no Playground edit service -- skipped");

        // ---- torch: night, task 151's scene ----
        float night = ArgFloat("-nighttod", 0.5f);
        SunCycle.SetHours(night);
        var paths = new List<ClipPath>();
        L("");
        yield return CostTorchScene(centre, night, far, sm, cam, paths);
        if (paths.Count == 0 || driver == null || driver.Lights == null) { L("torch: no scene -- skipped"); yield break; }
        Vector3 te = paths[0].pos[0];
        Quaternion tr = paths[0].rot[0];
        Phase4Bootstrapper.DebugStreamCentre = te;
        cam.transform.SetPositionAndRotation(te, tr);
        yield return FsrLiveShellWait(sh, Math.Min(wait, 20f));
        var tf = tr * Vector3.forward; tf.y = 0f; tf.Normalize();
        bool tAlongX = Mathf.Abs(tf.x) < Mathf.Abs(tf.z);
        int3 tAxis = tAlongX ? new int3(1, 0, 0) : new int3(0, 0, 1);
        Vector3 tSide = tAlongX ? Vector3.right : Vector3.forward;
        if (Vector3.Dot(Vector3.Cross(Vector3.up, tf), tSide) < 0f) tSide = -tSide;
        int3 Vox(Vector3 p) => new int3((int)math.floor(p.x * 10f), (int)math.floor(p.y * 10f), (int)math.floor(p.z * 10f));
        int3 TorchAt(Vector3 p)
        {
            float g = StoreGroundM(store, p.x, p.z, te.y + 10f);
            var v = Vox(new Vector3(p.x, float.IsNaN(g) ? te.y - 0.6f : g + 1f, p.z));
            for (int i = 0; i < 20 && store.GetVoxel(v) != Materials.Air; i++) v.y++;
            return v;
        }
        L($"== torch: {paths[0].note}; sun elevation {SunCycle.ElevationDegreesAt(night):F0} deg; lights registered {driver.Lights.RegisteredCount}" +
          $"{(driver.Lights.CapIsBinding ? " (CAP BINDING)" : "")}");
        int3 onT = TorchAt(te + tf * 5f + tSide * 1.5f);
        L($"  on/off torch at {onT} (5 m ahead, 1.5 m to the side, 1 m over the ground)");
        void SetTorch(int3 v, bool on)
        {
            if (on) { edits.TrySetVoxel(v, Materials.Torch); driver.Lights.Register(v, Materials.Torch); }
            else { driver.Lights.Unregister(v); edits.TrySetVoxel(v, Materials.Air); }
        }
        foreach (string arm in arms)
        {
            ApplyCompareArm(arm);
            cam.ResetProjectionMatrix();
            cam.transform.SetPositionAndRotation(te, tr);
            yield return FsrLiveBurst($"torch_still_{arm}", 60, 4, null);
            yield return FsrLiveBurst($"torch_on_{arm}", 59, 7, i => { if (i == 60) SetTorch(onT, true); });
            yield return FsrLiveBurst($"torch_off_{arm}", 29, 7, i => { if (i == 30) SetTorch(onT, false); });
        }
        int3 mv0 = TorchAt(te + tf * 5f - tSide * 3.2f);
        int mvSign = Vector3.Dot(new Vector3(tAxis.x, 0f, tAxis.z), tSide) >= 0f ? 1 : -1;
        L($"  moving torch from {mv0}, {mvSign * 1:+0;-0} voxel along {(tAlongX ? "x" : "z")} per frame (left -> right in frame), height fixed");
        foreach (string arm in arms)
        {
            ApplyCompareArm(arm);
            cam.ResetProjectionMatrix();
            cam.transform.SetPositionAndRotation(te, tr);
            int3 cur = mv0;
            SetTorch(cur, true);
            yield return FsrLiveBurst($"torch_move_{arm}", 60, 4, i =>
            {
                if (i == 0) return;
                int3 next = mv0 + tAxis * (mvSign * i);
                if (store.GetVoxel(next) != Materials.Air) return;   // blocked: the torch stops there (logged by the frames)
                SetTorch(cur, false); cur = next; SetTorch(cur, true);
            });
            SetTorch(cur, false);
            L($"  torch_move_{arm}: ended at {cur}");
        }
        SunCycle.SetHours(SunCycle.NoonHours);
        Phase4Bootstrapper.DebugStreamCentre = null;
    }

    private IEnumerator FsrLiveShellWait(ShellStreamer sh, float wait)
    {
        if (sh == null) yield break;
        float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < wait)
        {
            int missing = 0;
            for (int k = sh.FirstShell; k <= sh.LastShell; k++) if (!sh.ShellFull(k)) missing += sh.Missing(k);
            if (missing == 0 && sh.InFlight == 0 && sh.Pending == 0) break;
            yield return null;
        }
        L($"  [shells] waited {Time.realtimeSinceStartup - t0:F0} s (cap {wait:F0} s): {ShellCountsLine(sh)}");
    }

    /// `lead + n` consecutive frames; step(i) runs before frame i renders (the camera does not move);
    /// frames i >= lead are kept in memory and written after, so the capture does not hold a frame up.
    private IEnumerator FsrLiveBurst(string name, int lead, int n, Action<int> step)
    {
        var shots = new List<Texture2D>(n);
        for (int i = 0; i < lead + n; i++)
        {
            step?.Invoke(i);
            yield return new WaitForEndOfFrame();
            if (i >= lead) shots.Add(ScreenCapture.CaptureScreenshotAsTexture());
        }
        for (int i = 0; i < shots.Count; i++)
        {
            try { File.WriteAllBytes(Path.Combine(_folder, $"{name}_{i}.png"), shots[i].EncodeToPNG()); }
            finally { Destroy(shots[i]); }
        }
    }
}
