// Assets/Game/PlaygroundCapture.UpscalerRealRate.cs
//
// -upscalerclips -realrate (with -world tg2; run-upscaler-realrate.sh): TASK 51.
// Task 50's clips ran every arm at a fixed 30 fps, which shows quality on
// identical frames but hides each arm's real frame rate. One launch here:
//   LIGHT   noon (as -startview / the M key). Each day path is re-aimed so the
//           sun is behind or beside the camera: the eyes stand on the sun's
//           side of the peak and look at it (spin_stop, fly, pan_horizon_lit),
//           walk_cliff takes a cliff foot whose cliff faces the sun. A
//           candidate's first recorded frame is rendered (native) and kept if
//           its terrain mean luma >= 70/255 and sky <= 1/3 of the frame (sky =
//           a CPU ray per 40x40-px cell that meets no ground in the tg2 map
//           within 16 km); else the next candidate or 8/15 deg more pitch down.
//   PART 1  per path (walk_cliff, spin_stop, fly): streaming PINNED at the
//           path's middle pose (Phase4Bootstrapper.DebugStreamCentre) and
//           settled there, so nothing streams while timing (a moving window
//           recentred on every chunk the walk crossed and every frame
//           streamed); then the arms A..F F..A, each
//           3 s settle + 20 s timed, the camera on the path by REAL time
//           (Time.unscaledDeltaTime; the 8 s path played forward and back),
//           no capture of any kind, fluid simulating as in play. Frame time
//           p50/p95/p99 per pass and pooled, fsr3_dyn's rows, and the frames
//           that rendered while streaming (deficit > 0) are logged.
//   PART 2  per arm x path: task 50's recording (ClipRecordArm: settle, history
//           reset, camera-off streaming waits) with Time.captureDeltaTime = that
//           arm's pooled p50 on that path, so the camera moves as far per frame
//           as in play; 1 s lead-in, 8 s recorded. fsr3_dyn is held at the rows
//           it ran at when timed (UpscalerSelect.HoldDynRows). Frames go to
//           <run>/rr_<path>/<arm>/ with rates.json; the wrapper encodes 60 fps
//           sample-and-hold MP4s into Screenshots/Engine03/clips_realrate/.
//   LIT     walk_cliff_lit and pan_horizon_lit, task 50's fixed 30 fps, in the
//           new light (task 50's two came out backlit), into ../clips/.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using VoxelEngine.Rendering;
using VoxelEngine.Streaming;

public partial class PlaygroundCapture
{
    private const float RateSettle = 3f, RateTimed = 20f, RatePathSeconds = 8f;
    private const float RateMinLuma = 70f, RateMaxSky = 1f / 3f;

    private sealed class RateArm
    {
        public readonly List<float>[] ms = { new List<float>(1024), new List<float>(1024) };
        public readonly List<int> rows = new List<int>(2048);
        public int deficitFrames, farBusyFrames;
        public float P50 => Pct(All(), 0.50f);
        public List<float> All() { var a = new List<float>(ms[0]); a.AddRange(ms[1]); return a; }
        public int MedianRows() { var r = new List<int>(rows); r.Sort(); return r.Count == 0 ? UpscalerSelect.MinRows : r[r.Count / 2]; }
    }

    private sealed class LightCheck { public float luma, sky; }

    private IEnumerator UpscalerRealRate(string root, Playground pg, FarField far, Camera cam, StreamManager sm, ChunkStore store,
                                         Phase4Bootstrapper boot, UniversalAdditionalCameraData acd, TerrainGen.WorldMap map,
                                         Func<float3, Vector3> E)
    {
        var inv = CultureInfo.InvariantCulture;
        float hours = SunCycle.NoonHours;
        SunCycle.SetHours(hours);
        float3 sun = SunCycle.DirectionAt(hours);
        float sunYaw = math.degrees(math.atan2(sun.x, sun.z));
        L("");
        L($"REAL RATE (task 51): {hours:F0}:00, sun elevation {SunCycle.ElevationDegreesAt(hours):F0} deg, azimuth yaw {sunYaw:F0}; " +
          $"views face yaw ~{sunYaw + 180f:F0} (sun behind) or beside it; light check: terrain luma >= {RateMinLuma:F0}, sky <= {RateMaxSky:P0}");
        L($"timing: arms {string.Join(" ", ClipArms)} then reversed, each {RateSettle:F0} s settle + {RateTimed:F0} s timed, real time, fluid ON, no capture");

        // ---- the lit paths ----
        var eyes = RateLitEyes(map, sunYaw);
        L($"{eyes.Count} eye candidates on the sun's side of the peak: {string.Join(" ", eyes.ConvertAll(e => e.n))}");
        var spins = new List<Func<ClipPath>>();
        var flies = new List<Func<ClipPath>>();
        var pans = new List<Func<ClipPath>>();
        foreach (var (n, e, a) in eyes)
        {
            spins.Add(() => { var p = ClipSpinStop(E(e), E(a), n); return p; });
            flies.Add(() => { var p = ClipFly(map, e, a); for (int f = 0; f < ClipFrames; f++) p.pos[f] = E(p.pos[f]); p.note = n + ": " + p.note; return p; });
            pans.Add(() => ClipPan("pan_horizon_lit", E(e), E(a), 20f, 0f, n + ", centred on the peak"));
        }
        var walks = new List<Func<ClipPath>>();
        foreach (var (e, a) in RateLitCliffFeet(map, sunYaw))
            walks.Add(() => { var p = ClipWalkCliff(map, e, a); for (int f = 0; f < ClipFrames; f++) p.pos[f] = E(p.pos[f]); return p; });
        L($"{walks.Count} cliff feet whose cliff faces the sun");

        ApplyCompareArm("native");
        ClipPath walk = null, spin = null, fly = null, pan = null;
        var pick = new ClipPath[1];
        // walk_cliff looks UP a steep face (pitch ~-30 deg), which a 78 deg sun
        // barely lights (first launch: best of 6 feet luma 58): more feet, and
        // one more pitch step down
        yield return RatePick("walk_cliff", walks, pick, cam, far, sm, map, 10, new[] { 0f, 8f, 15f, 25f }); walk = pick[0];
        yield return RatePick("spin_stop", spins, pick, cam, far, sm, map); spin = pick[0];
        yield return RatePick("fly", flies, pick, cam, far, sm, map); fly = pick[0];
        // pan_horizon was task 50's eye_level_3 (its furthest ring): far eyes first
        var pansFar = new List<Func<ClipPath>>();
        for (int i = 0; i < pans.Count; i++) if (eyes[i].n.StartsWith("r0.65")) pansFar.Add(pans[i]);
        for (int i = 0; i < pans.Count; i++) if (!eyes[i].n.StartsWith("r0.65")) pansFar.Add(pans[i]);
        yield return RatePick("pan_horizon_lit", pansFar, pick, cam, far, sm, map); pan = pick[0];
        File.WriteAllText(Path.Combine(root, "upscalerclips.txt"), _log.ToString());

        // ---- PART 1: frame rate per arm, warm, ABBA ----
        var timed = new List<ClipPath>();
        foreach (var p in new[] { walk, spin, fly }) if (p != null) timed.Add(p);
        var results = new Dictionary<string, Dictionary<string, RateArm>>();
        L("");
        L("PART 1 -- frame time per arm (ms; Time.unscaledDeltaTime of every frame in the 20 s window)");
        foreach (var path in timed)
        {
            L("");
            L($"== {path.name}: {path.note}");
            // streaming pinned at the path's middle pose for the whole timing
            // (first launch: the window recentred on every chunk / Y-layer the
            // walk crossed, so every timed frame streamed)
            RatePose(path, RatePathSeconds * 0.5f, out Vector3 mid, out _);
            Phase4Bootstrapper.DebugStreamCentre = mid;
            cam.transform.position = mid; cam.transform.rotation = path.rot[path.lead];
            yield return NearSettle(far, sm);
            float span = 0f;
            for (int f = 0; f < path.pos.Length; f++) span = math.max(span, Vector3.Distance(path.pos[f], mid));
            L($"  streaming pinned at the middle pose {mid:F1} (path reaches {span:F0} m from it): resident {store.ResidentCount}, deficit {sm.LoadDeficit()}");
            var res = new Dictionary<string, RateArm>();
            foreach (string arm in ClipArms) res[arm] = new RateArm();
            results[path.name] = res;
            var order = new List<string>(ClipArms);
            var back = new List<string>(ClipArms); back.Reverse();
            order.AddRange(back);
            var seen = new HashSet<string>();
            foreach (string arm in order)
            {
                int pass = seen.Add(arm) ? 0 : 1;
                yield return RateTime(path, arm, pass, res[arm], cam, acd, sm, far);
                var ms = res[arm].ms[pass];
                L($"  pass {pass + 1} {arm,-9} frames {ms.Count,4}  p50 {Pct(ms, 0.5f),5:F1}  p95 {Pct(ms, 0.95f),5:F1}  p99 {Pct(ms, 0.99f),5:F1} ms  " +
                  $"rows now {UpscalerSelect.Rows}  streaming frames (deficit > 0, both passes so far) {res[arm].deficitFrames}  far busy {res[arm].farBusyFrames}  resident {store.ResidentCount}");
                File.WriteAllText(Path.Combine(root, "upscalerclips.txt"), _log.ToString());
            }
            Phase4Bootstrapper.DebugStreamCentre = null;
        }
        var table = new StringBuilder();
        table.AppendLine("arm       path        pooled p50   p95   p99 ms   fps@p50  pass1 p50  pass2 p50  spread  rows");
        foreach (var path in timed)
        foreach (string arm in ClipArms)
        {
            var r = results[path.name][arm];
            var all = r.All();
            float a = Pct(r.ms[0], 0.5f), b = Pct(r.ms[1], 0.5f);
            float spread = math.abs(a - b) / math.max(1e-3f, math.min(a, b));
            var rr = new List<int>(r.rows); rr.Sort();
            table.AppendLine($"{arm,-9} {path.name,-11} {Pct(all, 0.5f),8:F1} {Pct(all, 0.95f),5:F1} {Pct(all, 0.99f),5:F1}    {1000f / Pct(all, 0.5f),6:F1}  {a,9:F1}  {b,9:F1}  {spread,5:P0}{(spread > 0.10f ? " >10%" : "")}" +
                             $"  {(rr.Count > 0 ? $"{rr[0]}-{rr[rr.Count - 1]} (median {r.MedianRows()})" : "-")}");
        }
        L("");
        L("PART 1 TABLE");
        L(table.ToString());

        // ---- PART 2 (real-rate clips) + the two lit 30 fps clips ----
        var jobs = new List<(string dir, ClipPath path, bool real)>();
        foreach (var p in timed) jobs.Add(("rr_" + p.name, p, true));
        if (walk != null) jobs.Add(("walk_cliff_lit", walk, false));
        if (pan != null) jobs.Add(("pan_horizon_lit", pan, false));
        L("");
        L($"PART 2 -- clips: {string.Join(" ", jobs.ConvertAll(j => j.dir))}");
        Playground.DebugSuspendFluidSim = true;
        for (int ji = 0; ji < jobs.Count; ji++)
        {
            var (dname, path, real) = jobs[ji];
            if (ji >= 2)
            {
                // disk: at most two paths of raw frames on disk at once
                string prev = jobs[ji - 2].dir;
                float tw = Time.realtimeSinceStartup;
                while (!File.Exists(Path.Combine(root, $"encoded_{prev}")) && !File.Exists(Path.Combine(root, $"failed_{prev}"))
                       && Time.realtimeSinceStartup - tw < 1800f) yield return null;
                L($"(waited {Time.realtimeSinceStartup - tw:F0} s for {prev}'s encode)");
            }
            string pdir = Path.Combine(root, dname);
            Directory.CreateDirectory(pdir);
            L("");
            L($"== {dname}: {path.note}");
            Time.captureDeltaTime = 0f;
            cam.transform.position = path.pos[0]; cam.transform.rotation = path.rot[0];
            yield return NearSettle(far, sm);
            var pre = new ClipArmStats();
            yield return ClipRun(path, null, cam, boot, sm, far, store, pre);
            L($"  stream pass: resident {store.ResidentCount}, {pre.waits} waits ({pre.waitSeconds:F0} s)");
            var rates = new StringBuilder("{\"seconds\": 8, \"arms\": {");
            foreach (string arm in ClipArms)
            {
                ClipPath clip = path;
                Action hold = null;
                if (real)
                {
                    var r = results[path.name][arm];
                    float dt = r.P50 / 1000f;
                    clip = RateClip(path, dt);
                    int rows = r.MedianRows();
                    if (arm == "fsr3_dyn") hold = () => UpscalerSelect.HoldDynRows(rows);
                    rates.Append($"{(arm == ClipArms[0] ? "" : ", ")}\"{arm}\": {{\"ms\": {(dt * 1000f).ToString("F2", inv)}, \"frames\": {clip.pos.Length - clip.lead}}}");
                    Time.captureDeltaTime = dt;
                    L($"  {arm}: captureDeltaTime {dt * 1000f:F2} ms ({1f / dt:F1} fps), lead {clip.lead} + {clip.pos.Length - clip.lead} frames" +
                      (hold != null ? $", dyn held at {rows} rows" : ""));
                }
                else Time.captureFramerate = 30;
                yield return ClipRecordArm(clip, arm, Path.Combine(pdir, arm), hold, cam, acd, boot, sm, far, store);
                Time.captureDeltaTime = 0f;
            }
            if (real) File.WriteAllText(Path.Combine(pdir, "rates.json"), rates.Append("}}").ToString());
            File.WriteAllText(Path.Combine(pdir, "DONE"), dname);
            File.WriteAllText(Path.Combine(root, "upscalerclips.txt"), _log.ToString());
        }
        Time.captureDeltaTime = 0f;
        L("");
        L($"clips recorded: {string.Join(" ", jobs.ConvertAll(j => j.dir))}");
        L($"FSR 3: {RaymarchFeature.Fsr3Status}");
        File.WriteAllText(Path.Combine(root, "upscalerclips.txt"), _log.ToString());
        File.WriteAllText(Path.Combine(root, "ALLDONE"), string.Join(" ", jobs.ConvertAll(j => j.dir)));
        Debug.Log("[upscalerclips -realrate]\n" + _log);
        yield return null;
        Application.Quit(0);
    }

    /// One timed run: settle at pose 0, history reset, then the path by real
    /// time (forward 0..8 s, back, forward ...); the frames after RateSettle s
    /// go into r.ms[pass]. Fluid simulates (as in play) only while timing.
    private IEnumerator RateTime(ClipPath path, string arm, int pass, RateArm r, Camera cam,
                                 UniversalAdditionalCameraData acd, StreamManager sm, FarField far)
    {
        cam.transform.position = path.pos[path.lead]; cam.transform.rotation = path.rot[path.lead];
        yield return NearSettle(far, sm);
        ApplyCompareArm("native");
        yield return Frames(2);
        ApplyCompareArm(arm);
        acd.resetHistory = true;
        Playground.DebugSuspendFluidSim = false;
        var ms = r.ms[pass];
        float t = 0f;
        while (t < RateSettle + RateTimed)
        {
            float s = t % (2f * RatePathSeconds);
            RatePose(path, s < RatePathSeconds ? s : 2f * RatePathSeconds - s, out Vector3 p, out Quaternion q);
            cam.transform.SetPositionAndRotation(p, q);
            yield return null;
            float dt = Time.unscaledDeltaTime;
            t += dt;
            if (t <= RateSettle) continue;
            ms.Add(dt * 1000f);
            r.rows.Add(UpscalerSelect.Rows);
            if (sm.LoadDeficit() > 0) r.deficitFrames++;
            if (far.Busy) r.farBusyFrames++;
        }
        Playground.DebugSuspendFluidSim = true;
    }

    /// The pose at path time s (s = 0 is the first recorded frame of the 30 fps
    /// path; negative s is the lead-in), interpolated between its frames.
    private static void RatePose(ClipPath path, float s, out Vector3 p, out Quaternion q)
    {
        int last = path.pos.Length - 1;
        float x = math.clamp(path.lead + s * 30f, 0f, last);
        int i0 = (int)math.floor(x), i1 = math.min(i0 + 1, last);
        float k = x - i0;
        p = Vector3.Lerp(path.pos[i0], path.pos[i1], k);
        q = Quaternion.Slerp(path.rot[i0], path.rot[i1], k);
    }

    /// The 30 fps path re-sampled at a fixed step dt: 1 s lead-in, 8 s recorded.
    private static ClipPath RateClip(ClipPath src, float dt)
    {
        int lead = Mathf.CeilToInt(1f / dt), n = Mathf.CeilToInt(RatePathSeconds / dt - 1e-3f);
        var c = new ClipPath { name = src.name, note = src.note, hours = src.hours, moving = src.moving, lead = lead,
                               pos = new Vector3[lead + n], rot = new Quaternion[lead + n] };
        for (int k = 0; k < lead + n; k++) RatePose(src, (k - lead) * dt, out c.pos[k], out c.rot[k]);
        return c;
    }

    /// spin_stop at a fixed eye: 30 still frames, 4 s at 180 deg/s (720 deg,
    /// back to the start view), then a dead stop held 4 s.
    private static ClipPath ClipSpinStop(Vector3 ee, Vector3 at, string note)
    {
        var p = new ClipPath { name = "spin_stop", note = note };
        Vector3 eul = Quaternion.LookRotation((at - ee).normalized, Vector3.up).eulerAngles;
        for (int f = 0; f < ClipFrames; f++)
        {
            int k = math.clamp(f - ClipLead + 1, 0, 120);        // 120 frames x 6 deg = 720 deg
            p.pos[f] = ee;
            p.rot[f] = Quaternion.Euler(eul.x, eul.y + 6f * k, 0f);
        }
        p.note += $"; spin frames {ClipLead}..{ClipLead + 119} (recorded 0.0-4.0 s), stopped from 4.0 s";
        return p;
    }

    /// Picks the first candidate (or the same one pitched 8 / 15 deg further
    /// down) whose first recorded frame passes the light check; none passing,
    /// the best seen (sky check first, then luma), logged as such. At most 6
    /// candidates are rendered.
    private IEnumerator RatePick(string name, List<Func<ClipPath>> cands, ClipPath[] outPath, Camera cam,
                                 FarField far, StreamManager sm, TerrainGen.WorldMap map, int maxCands = 6, float[] pitches = null)
    {
        pitches ??= new[] { 0f, 8f, 15f };
        outPath[0] = null;
        ClipPath best = null; float bestScore = float.MinValue; string bestWhy = "";
        var lc = new LightCheck();
        for (int i = 0; i < cands.Count && i < maxCands; i++)
        {
            ClipPath basePath = cands[i]();
            foreach (float dp in pitches)
            {
                ClipPath p = dp == 0f ? basePath : ClipPitched(basePath, dp);
                cam.transform.position = p.pos[p.lead]; cam.transform.rotation = p.rot[p.lead];
                if (dp == 0f) yield return NearSettle(far, sm);
                else yield return Frames(20);
                yield return new WaitForEndOfFrame();
                RateLight(cam, far, map, lc);
                bool ok = lc.luma >= RateMinLuma && lc.sky <= RateMaxSky;
                L($"  light {name} cand {i} pitch +{dp:F0}: terrain luma {lc.luma:F0}, sky {lc.sky:P0}, yaw {p.rot[p.lead].eulerAngles.y:F0} -> {(ok ? "OK" : "no")}  ({p.note})");
                float score = (lc.sky <= RateMaxSky ? 1000f : 0f) + lc.luma;
                if (score > bestScore) { bestScore = score; best = p; bestWhy = $"cand {i} pitch +{dp:F0} (luma {lc.luma:F0}, sky {lc.sky:P0})"; }
                if (ok)
                {
                    if (dp > 0f) p.note += $"; pitched {dp:F0} deg further down for the light check";
                    outPath[0] = p;
                    L($"  {name}: cand {i} pitch +{dp:F0} picked");
                    yield break;
                }
            }
        }
        outPath[0] = best;
        L(best == null ? $"  {name}: NO candidate -- dropped" : $"  {name}: none passed the light check -- kept the best, {bestWhy}");
    }

    private static ClipPath ClipPitched(ClipPath src, float dp)
    {
        var c = new ClipPath { name = src.name, note = src.note, hours = src.hours, moving = src.moving, lead = src.lead,
                               pos = (Vector3[])src.pos.Clone(), rot = new Quaternion[src.rot.Length] };
        for (int f = 0; f < c.rot.Length; f++)
        {
            Vector3 e = src.rot[f].eulerAngles;
            c.rot[f] = Quaternion.Euler(e.x + dp, e.y, e.z);
        }
        return c;
    }

    /// The light check on the frame just rendered: mean Rec.601 luma of the
    /// cells whose centre ray meets ground in the tg2 map, and the sky fraction.
    /// Not an iterator (map.View).
    private static void RateLight(Camera cam, FarField far, TerrainGen.WorldMap map, LightCheck lc)
    {
        Texture2D t = ScreenCapture.CaptureScreenshotAsTexture();
        Color32[] px = t.GetPixels32();
        int w = t.width, h = t.height;
        UnityEngine.Object.Destroy(t);
        var spec = map.Spec;
        var o = TerrainGen.Offsets.For(spec);
        float maxH = spec.seaLevelY + 500f;
        foreach (float hh in map.Height) maxH = math.max(maxH, spec.seaLevelY + hh + 500f);
        int2 off = far.Tg2OffsetVoxels;
        Vector3 c = cam.transform.position;
        float3 org = new float3(c.x * 10f + off.x, c.y * 10f + far.Tg2SeaShift, c.z * 10f + off.y);
        const int GX = 48, GY = 27;
        int sky = 0, ground = 0; double lum = 0;
        for (int j = 0; j < GY; j++)
        for (int i = 0; i < GX; i++)
        {
            Ray ray = cam.ViewportPointToRay(new Vector3((i + 0.5f) / GX, (j + 0.5f) / GY, 0f));
            float3 d = math.normalize((float3)ray.direction);
            bool isSky = true;
            for (float s = 2f; s < 160000f; s += math.max(4f, s * 0.015f))
            {
                float3 q = org + d * s;
                if (d.y >= 0f && q.y > maxH) break;
                float g = spec.seaLevelY + 1;
                if (q.x >= 0f && q.z >= 0f && q.x < spec.worldSizeVoxels && q.z < spec.worldSizeVoxels)
                {
                    var cs = TerrainGen.ShapeField.Sample(in spec, map.View, in o, q.x, q.z, s < 3000f ? 1 : 8);
                    g = math.max(math.max(cs.height, cs.water), spec.seaLevelY) + 1;
                }
                if (q.y <= g) { isSky = false; break; }
            }
            if (isSky) { sky++; continue; }
            ground++;
            int x0 = i * w / GX, x1 = (i + 1) * w / GX, y0 = j * h / GY, y1 = (j + 1) * h / GY;
            double sum = 0; int n = 0;
            for (int y = y0; y < y1; y += 2)
            for (int x = x0; x < x1; x += 2)
            {
                Color32 p = px[y * w + x];
                sum += 0.299 * p.r + 0.587 * p.g + 0.114 * p.b; n++;
            }
            lum += sum / math.max(1, n);
        }
        lc.sky = sky / (float)(GX * GY);
        lc.luma = ground > 0 ? (float)(lum / ground) : 0f;
    }

    /// Eye candidates (tg2 voxels) on the SUN's side of the peak, looking at it
    /// (so away from the sun): radii 0.45 / 0.65 / 0.3 / 0.55 of the island
    /// radius from the peak, 0 / +-30 / +-55 deg off the sun's azimuth; dry
    /// land 15 m over the sea, eye 1.7 m over the exact ground, look-at as
    /// Tg2EyeLevelPoses. Named r<radius>_d<deg>.
    private static List<(string n, float3 eye, float3 at)> RateLitEyes(TerrainGen.WorldMap map, float sunYaw)
    {
        var spec = map.Spec;
        var o = TerrainGen.Offsets.For(spec);
        float2 pk = map.PeakPosition;
        var list = new List<(string, float3, float3)>();
        foreach (float r in new[] { 0.45f, 0.65f, 0.3f, 0.55f })
        foreach (float dd in new[] { 0f, 30f, -30f, 55f, -55f })
        {
            float a = math.radians(sunYaw + dd);
            float2 q = pk + new float2(math.sin(a), math.cos(a)) * spec.islandRadius * r;
            if (q.x < 0f || q.y < 0f || q.x >= spec.worldSizeVoxels || q.y >= spec.worldSizeVoxels) continue;
            var s = TerrainGen.ShapeField.Sample(in spec, map.View, in o, q.x, q.y, 1);
            if (s.water > s.height || s.height < spec.seaLevelY + 150f) continue;
            float g = s.height + 1;
            list.Add(($"r{r:F2}_d{dd:+0;-0;0}", new float3(q.x, g + 17f, q.y), new float3(pk.x, g + 17f + 0.05f * math.distance(q, pk), pk.y)));
        }
        return list;
    }

    /// Cliff feet (FindCliffFeet) whose view of the cliff points within 60 deg
    /// of straight away from the sun, so the cliff face is sunlit; at least
    /// 200 m apart; at most 10.
    private static List<(float3 eye, float3 at)> RateLitCliffFeet(TerrainGen.WorldMap map, float sunYaw)
    {
        var keep = new List<(float3, float3)>();
        foreach (var (e, a) in FindCliffFeet(map, 2000))
        {
            float yaw = math.degrees(math.atan2(a.x - e.x, a.z - e.z));
            float dy = Mathf.DeltaAngle(yaw, sunYaw + 180f);
            if (math.abs(dy) > 60f) continue;
            bool near = false;
            foreach (var (k, _) in keep) near |= math.distance(k.xz, e.xz) < 2000f;
            if (near) continue;
            keep.Add((e, a));
            if (keep.Count == 10) break;
        }
        return keep;
    }

    /// Percentile (nearest rank) of a list of frame times.
    private static float Pct(List<float> v, float q)
    {
        if (v.Count == 0) return float.NaN;
        var s = new List<float>(v); s.Sort();
        return s[math.clamp((int)math.ceil(q * s.Count) - 1, 0, s.Count - 1)];
    }
}
