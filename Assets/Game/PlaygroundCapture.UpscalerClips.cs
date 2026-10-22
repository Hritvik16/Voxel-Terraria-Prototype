// Assets/Game/PlaygroundCapture.UpscalerClips.cs
//
// -upscalerclips (with -world tg2; run-upscaler-clips.sh): TASK 50, motion
// clips of task 48's upscalers so the owner can pick Q38 by WATCHING them.
// One launch. Six scripted camera paths, each a fixed list of 270 poses
// (30-frame lead-in, not recorded, + 240 recorded = 8 s at 30 fps), computed
// ONCE from the tg2 map before any arm runs, so frame N is the same pose in
// every arm. Per path, every arm in process (UpscalerSelect.Apply, as 48):
//   native (1920x1080, no AA), taa_540, fsr1_540, stp_540, fsr3_dyn, fsr3_720.
//   walk_cliff     5 m/s, eye 1.6 m over the exact ground, along the foot of
//                  48's near_cliff, looking 30 deg off the cliff toward the walk
//   pan_horizon    20 deg/s pan at eye_level_3, centred on the peak
//   spin_stop      eye_level_2: 30 still frames, 4 s at 180 deg/s (720 deg,
//                  back to the start view), then a dead stop held 4 s
//   fly            40 m/s from eye_level_2 toward the peak, ~30 m over the
//                  ground ahead, pitched 12 deg down
//   water          spawn_day (48's pose), +-40 deg pan
//   night_torches  48's 8-torch ring at eye_level_2, 23:00, +-36 deg pan
// -clippaths walk_cliff,fly records only those (default: all six).
// -realrate: task 51's mode instead (PlaygroundCapture.UpscalerRealRate.cs).
// IDENTICAL FRAMES: Time.captureFramerate = 30; every frame of an arm is
// rendered in sequence. History reset at each arm x path start: two native
// frames (our TAA / FSR 3 see !_taaValid on the next frame) + URP's
// resetHistory (STP). STREAMING NEVER SHOWS: before a frame whose pose
// changes the camera chunk (or while anything streams), the camera is
// switched OFF, the streamer and far band take that pose (the bootstrapper's
// transform stands in for Camera.main) and the run waits until nothing is
// loading, uploading or baking; then the frame renders. A disabled camera
// renders nothing, so no upscaler history sees the wait. Deficit at render is
// logged per frame (and the frames rendered while the far band was still
// baking -- that work runs in the render pass, so it is not waited for); an arm that rendered with deficit > 0 is recorded once
// more, and the log says so.
// Frames: JPEG q95 in <data>/UpscalerClips/<ts>/<path>/<arm>/f0000.jpg;
// <path>/DONE when a path's arms are all written. The wrapper encodes and
// deletes them meanwhile; the next-but-one path waits for encoded_<path>
// (disk: at most two paths of raw frames). A LOOK, not a measurement: no
// frame times are claimed.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using VoxelEngine.Rendering;
using VoxelEngine.Streaming;

public partial class PlaygroundCapture
{
    private static readonly string[] ClipArms = { "native", "taa_540", "fsr1_540", "stp_540", "fsr3_dyn", "fsr3_720" };
    private const int ClipLead = 30, ClipRec = 240, ClipFrames = ClipLead + ClipRec;
    private const float ClipWaitTimeout = 60f;

    private sealed class ClipPath
    {
        public string name, note;
        public float hours = 11f;
        public bool moving;
        public int lead = ClipLead;                     // frames before the first recorded one
        public Vector3[] pos = new Vector3[ClipFrames];
        public Quaternion[] rot = new Quaternion[ClipFrames];
    }

    private IEnumerator UpscalerClips()
    {
        Screen.SetResolution(1920, 1080, FullScreenMode.FullScreenWindow);
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;
        var pg = FindAnyObjectByType<Playground>();
        while (Phase4Bootstrapper.Store == null) yield return null;
        _demoCaption = null;
        yield return Frames(300);
        string ts = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
        string root = Path.Combine(Application.persistentDataPath, "UpscalerClips", ts);
        Directory.CreateDirectory(root);
        LightingDriver.ClockPaused = true;
        Playground.DebugHideUi = true;
        Playground.DebugSuspendFluidSim = true;
        var far = FarField.Active;
        var cam = Camera.main;
        var sm = Phase4Bootstrapper.Streamer;
        var store = Phase4Bootstrapper.Store;
        var boot = FindAnyObjectByType<Phase4Bootstrapper>();
        var acd = cam.GetUniversalAdditionalCameraData();
        // the lights follow THIS camera even while it is switched off for a wait
        foreach (var ld in FindObjectsByType<LightingDriver>(FindObjectsSortMode.None)) ld.SetCentre(cam.transform);
        L($"UPSCALER CLIPS -- {ts}");
        L($"device {SystemInfo.graphicsDeviceName}  screen {Screen.width}x{Screen.height}  " +
          $"{(Debug.isDebugBuild ? "DEVELOPMENT" : "RELEASE")}  preset {(HasFlag("-renderpreset") ? RenderPreset.Current.ToString() : "not set")}  " +
          $"temporal shadows {(RaymarchFeature.TemporalShadows ? "ON" : "OFF")}");
        // the frames must BE 1920x1080. Windowed, the surface twice went to the
        // panel's 2880x1800 mid-run (Player.log "RecreateSurface"), cause not
        // found; fullscreen 1920x1080 is what every other rig uses. Checked on a
        // real capture here, and on every recorded frame (an arm with a wrong-size
        // frame is recorded again).
        {
            int w = 0, h = 0;
            for (int tries = 0; tries < 5; tries++)
            {
                yield return new WaitForEndOfFrame();
                Texture2D t = ScreenCapture.CaptureScreenshotAsTexture();
                w = t.width; h = t.height; Destroy(t);
                if (w == 1920 && h == 1080) break;
                Screen.SetResolution(1920, 1080, FullScreenMode.FullScreenWindow);
                yield return Frames(60);
            }
            L($"captured frame {w}x{h} (Screen {Screen.width}x{Screen.height}, {Screen.fullScreenMode})");
            if (w != 1920 || h != 1080)
            {
                L("NOT 1920x1080 -- stopping");
                File.WriteAllText(Path.Combine(root, "upscalerclips.txt"), _log.ToString());
                Application.Quit(3); yield break;
            }
        }
        L($"bootstrapper transform: {boot?.transform.childCount ?? -1} children, camera is child: {(boot != null && cam.transform.IsChildOf(boot.transform))}");
        if (far == null || !far.UsesTg2 || !Phase4Bootstrapper.IsTg2World || pg == null || boot == null)
        {
            L("needs the Playground with -world tg2");
            File.WriteAllText(Path.Combine(root, "upscalerclips.txt"), _log.ToString());
            Application.Quit(2); yield break;
        }
        var map = far.Tg2Map;
        int2 off = far.Tg2OffsetVoxels;
        int seaShift = far.Tg2SeaShift;
        Vector3 E(float3 g) => new Vector3((g.x - off.x) * 0.1f, (g.y - seaShift) * 0.1f, (g.z - off.y) * 0.1f);
        var eyes = Tg2EyeLevelPoses(map);
        if (eyes.Count < 3) { L($"only {eyes.Count} eye-level poses"); }
        // -realrate (task 51): per-arm frame rate, then clips at that rate
        if (HasFlag("-realrate")) { yield return UpscalerRealRate(root, pg, far, cam, sm, store, boot, acd, map, E); yield break; }
        // -costsplit (task 57): FULL vs one-thing-off arms on the walk, warm ABBA
        if (HasFlag("-costsplit")) { yield return CostSplit(root, pg, far, cam, sm, store, acd); Application.Quit(0); yield break; }

        // ---- the paths (poses fixed here, before any arm) ----
        var paths = new List<ClipPath>();
        if (FindCliffFoot(map, out float3 cfEye, out float3 cfAt))
        {
            var p = ClipWalkCliff(map, cfEye, cfAt);
            for (int f = 0; f < ClipFrames; f++) p.pos[f] = E(new float3(p.pos[f].x, p.pos[f].y, p.pos[f].z));
            paths.Add(p);
        }
        else L("walk_cliff: no cliff foot found -- dropped");
        {
            var (_, e, a) = eyes[eyes.Count - 1];
            paths.Add(ClipPan("pan_horizon", E(e), E(a), 20f, 0f, "eye_level_3, centred on the peak"));
        }
        {
            var (_, e, a) = eyes[Math.Min(1, eyes.Count - 1)];
            var p = ClipSpinStop(E(e), E(a), "eye_level_2");
            paths.Add(p);
        }
        {
            var (_, e, a) = eyes[Math.Min(1, eyes.Count - 1)];
            var p = ClipFly(map, e, a);
            for (int f = 0; f < ClipFrames; f++) p.pos[f] = E(new float3(p.pos[f].x, p.pos[f].y, p.pos[f].z));
            paths.Add(p);
        }
        {
            Vector3 sp = Phase4Bootstrapper.Tg2Spawn(sm.TerrainGenSource);
            var p = new ClipPath { name = "water", note = "spawn_day (48's pose: pitch 8, yaw 30), +-40 deg" };
            for (int f = 0; f < ClipFrames; f++)
            {
                p.pos[f] = sp;
                p.rot[f] = Quaternion.Euler(8f, 30f - 40f + 80f * f / (ClipFrames - 1), 0f);
            }
            paths.Add(p);
        }
        paths.Add(new ClipPath { name = "night_torches", hours = 23f, note = "built below, after the torches are placed" });
        // -clippaths a,b: only those paths (a re-record); default all six
        string only = ArgValue("-clippaths");
        if (!string.IsNullOrEmpty(only))
        {
            var keep = new HashSet<string>(only.Split(','));
            paths.RemoveAll(cp => !keep.Contains(cp.name));
            L($"-clippaths {only}: recording {paths.Count} path(s)");
        }

        L("");
        L($"PATHS: {ClipLead} lead-in + {ClipRec} recorded frames each, Time.captureFramerate 30; arms {string.Join(" ", ClipArms)}");
        Time.captureFramerate = 30;
        int pathIndex = 0;
        var done = new List<string>();
        foreach (var path in paths)
        {
            // disk: at most two paths of raw frames on disk at once
            if (pathIndex >= 2)
            {
                string prev = paths[pathIndex - 2].name;
                float tw = Time.realtimeSinceStartup;
                while (!File.Exists(Path.Combine(root, $"encoded_{prev}")) && !File.Exists(Path.Combine(root, $"failed_{prev}"))
                       && Time.realtimeSinceStartup - tw < 1800f) yield return null;
                L($"(waited {Time.realtimeSinceStartup - tw:F0} s for {prev}'s encode)");
            }
            pathIndex++;
            SunCycle.SetHours(path.hours);
            ApplyCompareArm("native");
            if (path.name == "night_torches")
            {
                // 48's ring: 8 torches 3 m round a spot 10 m ahead of eye_level_2
                var (_, e0, a0) = eyes[Math.Min(1, eyes.Count - 1)];
                Vector3 e = E(e0), fwd = E(a0) - e; fwd.y = 0f; fwd.Normalize();
                cam.transform.position = e;
                cam.transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);
                yield return NearSettle(far, sm);
                float gE = StoreGroundM(store, e.x, e.z, e.y + 10f);
                if (!float.IsNaN(gE)) e.y = gE + 1.7f;
                Vector3 c = e + fwd * 10f;
                float gC = StoreGroundM(store, c.x, c.z, e.y + 15f);
                c.y = float.IsNaN(gC) ? e.y - 1.7f : gC;
                int placed = 0;
                for (int k = 0; k < 8; k++)
                {
                    float ang = k * Mathf.PI / 4f;
                    Vector3 tp = c + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * 3f + Vector3.up * 2f;
                    if (pg.DebugEdits.TrySetVoxel(FloorAt(store, CoordMath.WorldToVoxel(new float3(tp.x, tp.y, tp.z))), Materials.Torch)) placed++;
                }
                var built = ClipPan("night_torches", e, c, 0f, 0f, $"{placed}/8 torches round {c:F1}, 23:00, +-36 deg");
                Vector3 eu = built.rot[0].eulerAngles;
                for (int f = 0; f < ClipFrames; f++)
                {
                    built.rot[f] = Quaternion.Euler(eu.x, eu.y - 36f + 72f * f / (ClipFrames - 1), 0f);
                }
                path.pos = built.pos; path.rot = built.rot; path.note = built.note;
            }
            L("");
            L($"== {path.name}: {path.note}; {path.hours:F0}:00; start {path.pos[0]:F1} end {path.pos[ClipFrames - 1]:F1}");

            // stream the path ONCE (no capture), settling wherever it streams
            cam.transform.position = path.pos[0]; cam.transform.rotation = path.rot[0];
            yield return NearSettle(far, sm);
            var pre = new ClipArmStats();
            yield return ClipRun(path, null, cam, boot, sm, far, store, pre);
            L($"  stream pass: resident {store.ResidentCount}, {pre.waits} waits ({pre.waitFrames} frames, {pre.waitSeconds:F0} s, {pre.timeouts} timeouts), " +
              $"in rock {pre.inRock} frames");

            string pdir = Path.Combine(root, path.name);
            foreach (string arm in ClipArms)
                yield return ClipRecordArm(path, arm, Path.Combine(pdir, arm), null, cam, acd, boot, sm, far, store);
            File.WriteAllText(Path.Combine(pdir, "DONE"), path.name);
            done.Add(path.name);
            File.WriteAllText(Path.Combine(root, "upscalerclips.txt"), _log.ToString());
        }
        Time.captureFramerate = 0;
        L("");
        L($"paths recorded: {string.Join(" ", done)}");
        L($"FSR 3: {RaymarchFeature.Fsr3Status}");
        File.WriteAllText(Path.Combine(root, "upscalerclips.txt"), _log.ToString());
        File.WriteAllText(Path.Combine(root, "ALLDONE"), string.Join(" ", done));
        Debug.Log("[upscalerclips]\n" + _log);
        yield return null;
        Application.Quit(0);
    }

    /// One arm of one path into adir (JPEGs), task 50's way: settle at pose 0,
    /// history reset (two native frames + URP's resetHistory), the path in
    /// sequence; an arm that rendered with deficit > 0 or a wrong-size frame is
    /// recorded once more. afterApply runs right after the arm is applied.
    private IEnumerator ClipRecordArm(ClipPath path, string arm, string adir, Action afterApply, Camera cam,
                                      UniversalAdditionalCameraData acd, Phase4Bootstrapper boot, StreamManager sm,
                                      FarField far, ChunkStore store)
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            Directory.CreateDirectory(adir);
            cam.transform.position = path.pos[0]; cam.transform.rotation = path.rot[0];
            yield return NearSettle(far, sm);
            // history reset: two native frames, then the arm with URP's reset
            ApplyCompareArm("native");
            yield return Frames(2);
            ApplyCompareArm(arm);
            afterApply?.Invoke();
            acd.resetHistory = true;
            var st = new ClipArmStats { residentStart = store.ResidentCount };
            int f0 = Time.frameCount, d0 = RaymarchFeature.PrimaryDispatchCount;
            yield return ClipRun(path, adir, cam, boot, sm, far, store, st);
            var d = RaymarchFeature.LastDispatchResolution;
            L($"  {arm,-9} dispatch {d.x}x{d.y}  traced {RaymarchFeature.PrimaryDispatchCount - d0}/{Time.frameCount - f0}  " +
              $"rows {st.rowsMin}-{st.rowsMax}  written {st.written}  resident {st.residentStart}->{store.ResidentCount}  " +
              $"deficit@render max {st.maxDeficit} ({st.framesWithDeficit} frames)  far band busy {st.farBusyFrames} frames  waits {st.waits} ({st.waitSeconds:F0} s, {st.timeouts} timeouts)  " +
              $"in rock {st.inRock}{(st.badSize > 0 ? $"  NOT 1920x1080: {st.badSize} frames (screen {Screen.width}x{Screen.height})" : "")}{(attempt > 0 ? "  [RE-RECORDED]" : "")}");
            if ((st.framesWithDeficit == 0 && st.badSize == 0) || attempt == 1)
            {
                if (st.framesWithDeficit > 0) L($"  {arm}: STREAMED MID-CLIP twice -- kept, flagged");
                if (st.badSize > 0) L($"  {arm}: WRONG FRAME SIZE twice -- kept, flagged (the encoder will refuse this path)");
                break;
            }
            L($"  {arm}: {(st.badSize > 0 ? "wrong frame size" : "streamed mid-clip")} -- recording again");
            if (st.badSize > 0) { Screen.SetResolution(1920, 1080, FullScreenMode.FullScreenWindow); yield return Frames(60); }
            Directory.Delete(adir, true);
        }
    }

    private sealed class ClipArmStats
    {
        public int waits, waitFrames, timeouts, farBusyFrames, badSize, maxDeficit, framesWithDeficit, written, inRock, residentStart;
        public float waitSeconds;
        public int rowsMin = int.MaxValue, rowsMax;
    }

    /// Plays the path's poses in sequence; writes frames >= path.lead to
    /// dir (null = the stream pass, nothing written).
    private IEnumerator ClipRun(ClipPath path, string dir, Camera cam, Phase4Bootstrapper boot, StreamManager sm,
                                FarField far, ChunkStore store, ClipArmStats st)
    {
        // Not far.Busy: the far band finishes its strips IN the render pass, so
        // it can never clear with the camera off (first run: every wait timed
        // out on it). Frames rendered while it is busy are counted instead.
        // After two timeouts in one run, stop waiting (logged): a stuck
        // counter must not turn 270 frames into 270 timeouts.
        bool Busy() => st.timeouts < 2 && (sm.PendingLoads > 0 || sm.InFlightLoads > 0 || sm.LoadDeficit() > 0 ||
                                          sm.ClipmapDirtyRemaining > 0);
        int3 lastChunk = ClipChunk(path.pos[0]);
        for (int f = 0; f < path.pos.Length; f++)
        {
            Vector3 p = path.pos[f];
            int3 ch = ClipChunk(p);
            if (!ch.Equals(lastChunk) || Busy())
            {
                // the streamer / far band take pose f with the camera OFF (no
                // frame renders, no history moves), then wait for them
                cam.enabled = false;
                Vector3 bootPos = boot.transform.position;
                boot.transform.position = p;
                cam.transform.position = p; cam.transform.rotation = path.rot[f];
                float t0 = Time.realtimeSinceStartup;
                int n = 0;
                do { yield return null; n++; boot.transform.position = p; }
                while (Busy() && Time.realtimeSinceStartup - t0 < ClipWaitTimeout);
                if (Busy())
                {
                    st.timeouts++;
                    L($"    wait TIMEOUT at frame {f}: pending {sm.PendingLoads} in flight {sm.InFlightLoads} deficit {sm.LoadDeficit()} " +
                      $"dirty {sm.ClipmapDirtyRemaining} far busy {far.Busy}");
                }
                st.waits++; st.waitFrames += n; st.waitSeconds += Time.realtimeSinceStartup - t0;
                boot.transform.position = bootPos;
                cam.enabled = true;
            }
            lastChunk = ch;
            cam.transform.position = p; cam.transform.rotation = path.rot[f];
            yield return new WaitForEndOfFrame();
            if (store.GetVoxel(CoordMath.WorldToVoxel(new float3(p.x, p.y, p.z))) != 0) st.inRock++;
            if (dir == null || f < path.lead) { yield return null; continue; }
            int def = sm.LoadDeficit();
            if (far.Busy) st.farBusyFrames++;
            if (def > 0) st.framesWithDeficit++;
            st.maxDeficit = Math.Max(st.maxDeficit, def);
            st.rowsMin = Math.Min(st.rowsMin, UpscalerSelect.Rows);
            st.rowsMax = Math.Max(st.rowsMax, UpscalerSelect.Rows);
            Texture2D t = ScreenCapture.CaptureScreenshotAsTexture();
            if (t.width != 1920 || t.height != 1080) st.badSize++;
            try { File.WriteAllBytes(Path.Combine(dir, $"f{f - path.lead:D4}.jpg"), t.EncodeToJPG(95)); }
            finally { Destroy(t); }
            st.written++;
            yield return null;
        }
    }

    /// The streamer's camera chunk (CHUNK_EDGE_VOXELS = 128 voxels, 12.8 m).
    private static int3 ClipChunk(Vector3 p) =>
        CoordMath.WorldToVoxel(new float3(p.x, p.y, p.z)) >> 7;

    /// A pan at a fixed eye: yaw round the eye->at direction from -half to +half
    /// (degPerSec x 9 s spans the whole 270 frames when half = 0).
    private static ClipPath ClipPan(string name, Vector3 eye, Vector3 at, float degPerSec, float half, string note)
    {
        var p = new ClipPath { name = name, note = note };
        Vector3 eul = Quaternion.LookRotation((at - eye).normalized, Vector3.up).eulerAngles;
        float span = degPerSec * ClipFrames / 30f;
        for (int f = 0; f < ClipFrames; f++)
        {
            p.pos[f] = eye;
            p.rot[f] = Quaternion.Euler(eul.x, eul.y - span * 0.5f + span * f / (ClipFrames - 1), 0f);
        }
        if (degPerSec > 0f) p.note += $"; {degPerSec:F0} deg/s, {span:F0} deg";
        return p;
    }

    /// Exact top-of-ground (tg2 absolute voxels), water and sea included.
    /// Not an iterator (map.View).
    private static float ClipGround(TerrainGen.WorldMap map, float gx, float gz)
    {
        var spec = map.Spec;
        var o = TerrainGen.Offsets.For(spec);
        var s = TerrainGen.ShapeField.Sample(in spec, map.View, in o, gx, gz, 1);
        return math.max(math.max(s.height, s.water), spec.seaLevelY) + 1;
    }

    /// walk_cliff in tg2 VOXELS (pos.y too; the caller converts): 5 m/s (50
    /// voxels/s) sideways along the cliff foot, on whichever side climbs less;
    /// eye 16 voxels over the ground smoothed +-6 frames (never below it + 0.6 m);
    /// the view a fixed 30 deg off the cliff toward the walk.
    private static ClipPath ClipWalkCliff(TerrainGen.WorldMap map, float3 eye, float3 at)
    {
        float2 fwd = math.normalize(at.xz - eye.xz);
        float best = float.MaxValue; float2 side = default; float[] bestG = null;
        foreach (float sgn in new[] { 1f, -1f })
        {
            float2 sd = sgn * new float2(-fwd.y, fwd.x);
            var g = new float[ClipFrames];
            float climb = 0f;
            for (int f = 0; f < ClipFrames; f++)
            {
                float2 q = eye.xz + sd * (f * 50f / 30f);
                g[f] = ClipGround(map, q.x, q.y);
                if (f > 0) climb += math.abs(g[f] - g[f - 1]);
            }
            if (climb < best) { best = climb; side = sd; bestG = g; }
        }
        var p = new ClipPath { name = "walk_cliff", moving = true };
        float pitch = -math.degrees(math.atan2(at.y - (bestG[0] + 16f), math.length(at.xz - eye.xz)));
        float2 lookDir = math.normalize(fwd * math.cos(math.radians(30f)) + side * math.sin(math.radians(30f)));
        float yaw = math.degrees(math.atan2(lookDir.x, lookDir.y));
        for (int f = 0; f < ClipFrames; f++)
        {
            float sum = 0f; int n = 0;
            for (int k = math.max(0, f - 6); k <= math.min(ClipFrames - 1, f + 6); k++) { sum += bestG[k]; n++; }
            float y = math.max(sum / n, bestG[f] - 10f) + 16f;
            float2 q = eye.xz + side * (f * 50f / 30f);
            p.pos[f] = new Vector3(q.x, y, q.y);
            p.rot[f] = Quaternion.Euler(math.clamp(pitch, -30f, 30f), yaw, 0f);
        }
        p.note = $"cliff foot {eye.xz}, 45 m sideways, total climb {best * 0.1f:F1} m, pitch {pitch:F1}, view 30 deg off the cliff";
        return p;
    }

    /// fly in tg2 VOXELS: 40 m/s (400 voxels/s) from eye toward at (the peak),
    /// 30 m over the highest ground from 10 m behind to 30 m ahead, smoothed
    /// +-15 frames, never under the ground + 15 m; pitched 12 deg down.
    private static ClipPath ClipFly(TerrainGen.WorldMap map, float3 eye, float3 at)
    {
        float2 dir = math.normalize(at.xz - eye.xz);
        var raw = new float[ClipFrames];
        var tgt = new float[ClipFrames];
        for (int f = 0; f < ClipFrames; f++)
        {
            float2 q = eye.xz + dir * (f * 400f / 30f);
            raw[f] = ClipGround(map, q.x, q.y);
            float m = raw[f];
            for (float s = -100f; s <= 300f; s += 25f) { float2 r = q + dir * s; m = math.max(m, ClipGround(map, r.x, r.y)); }
            tgt[f] = m + 300f;
        }
        var p = new ClipPath { name = "fly", moving = true };
        float yaw = math.degrees(math.atan2(dir.x, dir.y));
        float minClear = float.MaxValue;
        for (int f = 0; f < ClipFrames; f++)
        {
            float sum = 0f; int n = 0;
            for (int k = math.max(0, f - 15); k <= math.min(ClipFrames - 1, f + 15); k++) { sum += tgt[k]; n++; }
            float y = math.max(sum / n, raw[f] + 150f);
            minClear = math.min(minClear, y - raw[f]);
            float2 q = eye.xz + dir * (f * 400f / 30f);
            p.pos[f] = new Vector3(q.x, y, q.y);
            p.rot[f] = Quaternion.Euler(12f, yaw, 0f);
        }
        p.note = $"from eye_level_2 toward the peak, 360 m, ground {raw[0] * 0.1f:F0}..{raw[ClipFrames - 1] * 0.1f:F0} m (tg2), min clearance {minClear * 0.1f:F1} m";
        return p;
    }
}
