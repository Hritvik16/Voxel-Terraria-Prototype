// Assets/Game/PlaygroundCapture.FluidProbe.cs
//
// -fluidprobe (overnight task 09): DOES FLUID SIMULATE IN THE TG2 WORLD, and if
// not, which step stops it? Diagnostic only -- no timing here is a measurement.
//
// With -world tg2: task 07's -taarisk fluid placement, reproduced exactly --
// eye_level_1, a 1.5 m sphere dug ~10 m ahead, a 7x21x7 water box 2-4 m above
// the hole and a 7x16x7 lava box 3 m to the right. Without -world: the same two
// boxes at the Playground's arena (the control: there fluid is known to move).
// Then, at fixed frame offsets, every stage between "the byte is in the store"
// and "an op moved it" is logged: CPU material counts in and below the boxes,
// the chunk's fluid-tile mask, tile residency (pool use, exhaustions), the
// clipmap window and the chunk's upload epoch (CSPromote reads the MIRROR, not
// the store), the wake queue, the readback's op counters, and the CA's own
// per-tick GPU debug counters (FluidGpuSimulation.DebugDump).
//
// Task 93: the tg2 site is checked on the store first (map v4 put task 07's
// site inside a hillside, so nothing could move): the task-07 site's solid
// count is logged, then the first heading (45 degree steps) whose two boxes are
// all air is used.
//
// Task 91 (engine step 17, fixed-rate fluid tick): every report point also
// prints FLUID RATE -- ticks/s since placement, the scheduler's rate, lava's
// rate and the SHORTFALL (due ticks the frame rate could not deliver).
// `-fpscap N` caps the frame rate (Application.targetFrameRate) for the whole
// run; `-fluidstrip` replaces the frame offsets with WALL-CLOCK seconds after
// placement (shots at 2, 4 and 8 s) so two caps can be compared still by still.
//
// Task 93e (owner Q55 (b)): `-fluidfar D` (tg2) puts the site ~D m from
// eye_level_1 instead of 8-20 m: headings in 22.5 degree steps, distances D, D-6,
// D+6, D-12, D+12, the first within 120 m (3D) whose boxes are all air, whose water chunk is OUTSIDE the
// tier-0 mirror box (so the CA reads it through the fluid terrain directory)
// and that the eye can see (a CPU-store line of sight). The water box is 21 x 31
// x 21; it implies -fluidstrip, and each shot (2, 8, 20 s) also has a 6 degree
// telephoto twin (`*_tele`). Before = `-smallmirror 1 -fluiddir 0` (frozen).
using System;
using System.Collections;
using System.IO;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Memory;
using VoxelEngine.Rendering;
using VoxelEngine.Simulation;
using VoxelEngine.Streaming;

public partial class PlaygroundCapture
{
    private IEnumerator FluidProbe()
    {
        Screen.SetResolution(1920, 1080, !HasFlag("-windowed"));
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;
        int fpsCap = ArgInt("-fpscap", 0);
        if (fpsCap > 0) Application.targetFrameRate = fpsCap;
        float farM = ArgFloat("-fluidfar", 0f);
        bool strip = HasFlag("-fluidstrip") || farM > 0f;
        bool noShots = HasFlag("-stripnoshots");
        var pg = FindAnyObjectByType<Playground>();
        while (Phase4Bootstrapper.Store == null) yield return null;
        yield return Frames(300);
        bool tg2 = Phase4Bootstrapper.IsTg2World;
        string ts = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
        _folder = Path.Combine(Application.persistentDataPath, "FarBand", ts + "_fluidprobe_" + (tg2 ? "tg2" : "arena"));
        Directory.CreateDirectory(_folder);
        LightingDriver.ClockPaused = true;
        SunCycle.SetHours(12f);
        RaymarchFeature.GateModeOverride = RaymarchFeature.GateResMode.ForcedCustom;
        RaymarchFeature.CustomGateResolution = new Vector2Int(960, 540);
        Playground.DebugHideUi = true;
        var cam = Camera.main;
        var store = Phase4Bootstrapper.Store;
        var far = FarField.Active;
        var sm = Phase4Bootstrapper.Streamer;
        L($"FLUID PROBE -- {ts}  world {(tg2 ? "tg2" : "default (arena control)")}  fps cap {(fpsCap > 0 ? fpsCap.ToString() : "none")}" +
          $"{(strip ? "  -fluidstrip (wall-clock shots 2/4/8 s)" : "")}");
        if (pg == null || pg.DebugFluid == null || pg.DebugEdits == null)
        {
            L("no Playground fluid simulation (pg/DebugFluid/DebugEdits null)");
            File.WriteAllText(Path.Combine(_folder, "fluidprobe.txt"), _log.ToString());
            Application.Quit(2); yield break;
        }

        int3 hole, wLo, wHi, lc;
        if (tg2)
        {
            if (far == null || !far.UsesTg2) { L("tg2 world without a tg2 far field"); Application.Quit(2); yield break; }
            var map = far.Tg2Map;
            int2 off = far.Tg2OffsetVoxels;
            int seaShift = far.Tg2SeaShift;
            Vector3 E(float3 g) => new Vector3((g.x - off.x) * 0.1f, (g.y - seaShift) * 0.1f, (g.z - off.y) * 0.1f);
            var poses = Tg2EyeLevelPoses(map);
            Vector3 eye1 = E(poses[0].eye), at1 = E(poses[0].at);
            // ---- task 07's edit/fluid setup, line for line ----
            Vector3 fwd = at1 - eye1; fwd.y = 0f; fwd.Normalize();
            cam.transform.position = eye1;
            cam.transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);
            yield return NearSettle(far, sm);
            float gEye = StoreGroundM(store, eye1.x, eye1.z, eye1.y + 10f);
            if (!float.IsNaN(gEye)) eye1.y = gEye + 1.7f;
            // Task 93: on map v4 the ground 8-20 m ahead of eye_level_1 (towards the peak) is above the eye,
            // so task 07's straight-ahead search fell back to a guess that put the hole, the water box and the
            // lava box INSIDE the hillside: every fluid voxel was walled in by rock (intent.no_destination =
            // all of them) and "nothing moved". The guess site is still censused below so the report shows it;
            // the probe then turns in 45 degree steps until a site's two boxes are all air in the store.
            int wr = farM > 0f ? 10 : 3, wTop = farM > 0f ? 50 : 40;
            void Site(Vector3 g, Vector3 r, out int3 h, out int3 lo, out int3 hi, out int3 l)
            {
                h = CoordMath.WorldToVoxel(new float3(g.x, g.y - 0.4f, g.z));
                lo = h + new int3(-wr, 20, -wr); hi = h + new int3(wr, wTop, wr);
                l = CoordMath.WorldToVoxel(new float3(g.x + r.x * 3f, g.y + 1.5f, g.z + r.z * 3f));
            }
            Vector3 legacy = Vector3.zero; bool legacyFound = false;
            foreach (float d in new[] { 10f, 13f, 16f, 8f, 20f })
            {
                Vector3 p = eye1 + fwd * d;
                float g = StoreGroundM(store, p.x, p.z, eye1.y + 15f);
                if (float.IsNaN(g) || g > eye1.y - 0.5f) continue;
                legacy = new Vector3(p.x, g, p.z); legacyFound = true; break;
            }
            if (!legacyFound) { legacy = eye1 + fwd * 10f; legacy.y = eye1.y - 1.7f; }
            Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
            {
                Site(legacy, right, out _, out int3 lo0, out int3 hi0, out int3 l0);
                L($"task-07 site ({(legacyFound ? "ground found" : "no open ground ahead, a guess")}) {legacy:F1}: solid voxels in water box " +
                  $"{SolidIn(store, lo0, hi0)}/1029, in lava box {SolidIn(store, l0 + new int3(-3, 0, -3), l0 + new int3(3, 15, 3))}/784");
            }
            Vector3 target = legacy; bool found = false; float turned = 0f;
            Vector3 fwd0 = fwd;
            int turns = farM > 0f ? 16 : 8;
            float step = farM > 0f ? 22.5f : 45f;
            float[] dists = farM > 0f ? new[] { farM, farM - 6f, farM + 6f, farM - 12f, farM + 12f } : new[] { 10f, 13f, 16f, 8f, 20f };
            var clipF = TerrainClipmap.Active;
            // Far mode: the eye stays over eye_level_1's x/z but at max(its height, the site's ground + 15 m)
            // (at 1.7 m the hills hide most ground 110 m away). Per site; the chosen one is kept.
            Vector3 eyeF = eye1, eyeSite = eye1;
            int rejGround = 0, rejBox = 0, rejFar = 0, rejLos = 0, rejSolid = 0;
            float GroundAt(float x, float z)
            {
                if (farM <= 0f) return StoreGroundM(store, x, z, eye1.y + 15f);
                int3 v = CoordMath.WorldToVoxel(new float3(x, eye1.y + 150f, z));
                if (store.GetVoxel(v) != 0) return float.NaN;
                for (int i = 0; i < 3000; i++, v.y--)
                    if (store.GetVoxel(v) != 0) return (v.y + 1) * 0.1f;
                return float.NaN;
            }
            // Far mode: if this pose has no site, the next eye-level pose (teleport + settle) is tried.
            int poseIdx = 0;
            for (int pi = 0; pi < (farM > 0f ? poses.Count : 1) && !found; pi++)
            {
                if (pi > 0)
                {
                    eye1 = E(poses[pi].eye);
                    Vector3 atP = E(poses[pi].at);
                    fwd0 = atP - eye1; fwd0.y = 0f; fwd0.Normalize();
                    cam.transform.position = eye1;
                    cam.transform.rotation = Quaternion.LookRotation(fwd0, Vector3.up);
                    yield return NearSettle(far, sm);
                    float gP = StoreGroundM(store, eye1.x, eye1.z, eye1.y + 10f);
                    if (!float.IsNaN(gP)) eye1.y = gP + 1.7f;
                    clipF = TerrainClipmap.Active;
                    L($"far site search: no site at {poses[pi - 1].n}, trying {poses[pi].n}");
                }
                poseIdx = pi;
                for (int turn = 0; turn < turns && !found; turn++)
                {
                    float deg = ((turn + 1) >> 1) * step * ((turn & 1) == 1 ? 1f : -1f);   // 0, +45, -45, +90, -90, +135, -135, 180
                    Vector3 f = Quaternion.AngleAxis(deg, Vector3.up) * fwd0;
                    Vector3 r = Vector3.Cross(Vector3.up, f).normalized;
                    foreach (float d in dists)
                    {
                        Vector3 p = eye1 + f * d;
                        float g = GroundAt(p.x, p.z);
                        if (float.IsNaN(g) || (farM <= 0f && g > eye1.y - 0.5f)) { rejGround++; continue; }
                        Site(new Vector3(p.x, g, p.z), r, out _, out int3 lo, out int3 hi, out int3 l);
                        if (farM > 0f)
                        {
                            eyeF = new Vector3(eye1.x, Mathf.Max(eye1.y, g + 15f), eye1.z);
                            int3 wcc = CoordMath.VoxelToChunk((lo + hi) >> 1);
                            if (clipF == null || clipF.ContainsChunk(wcc)) { rejBox++; continue; }   // must be outside the mirror box
                            // ...and inside the fluid's 128 m wake radius, which is 3D: <= 120 m from the eye.
                            if (Vector3.Distance(eyeF, new Vector3(p.x, g + 3.5f, p.z)) > 120f) { rejFar++; continue; }
                            if (!StoreLineOfSight(store, eyeF, new Vector3(p.x, g + 2.5f, p.z))) { rejLos++; continue; }
                        }
                        if (SolidIn(store, lo, hi) + SolidIn(store, l + new int3(-3, 0, -3), l + new int3(3, 15, 3)) > 0) { rejSolid++; continue; }
                        target = new Vector3(p.x, g, p.z); fwd = f; right = r; turned = deg; found = true; eyeSite = eyeF; break;
                    }
                }
            }
            if (found) L($"site: {(farM > 0f ? poses[poseIdx].n + ", " : "")}turned {turned:F0} deg from the task-07 heading, both boxes all air");
            else L($"NO OPEN SITE in {turns} headings x {dists.Length} distances: using the task-07 site (expect rock-walled boxes)");
            if (farM > 0f) L($"far site search (-fluidfar {farM:F0}): rejected no ground {rejGround}, inside the mirror box {rejBox}, over 120 m (3D) {rejFar}, " +
                             $"no line of sight {rejLos}, boxes not air {rejSolid}");
            if (farM > 0f) eye1 = eyeSite;
            Vector3 look = target + right * 1.5f + Vector3.up * (farM > 0f ? 2.5f : 0.5f);
            cam.transform.position = eye1;
            cam.transform.rotation = Quaternion.LookRotation((look - eye1).normalized, Vector3.up);
            L($"view: eye {eye1:F1}, ground target {target:F1} ({Vector3.Distance(eye1, target):F1} m)");
            yield return NearSettle(far, sm);
            yield return Frames(90);
            Site(target, right, out hole, out wLo, out wHi, out lc);
            int dug = pg.DebugEdits.SetSphere(hole, 15, Materials.Air);
            L($"dug {dug} voxels at {hole} on frame {Time.frameCount}");
            yield return Frames(66);
        }
        else
        {
            pg.SendMessage("TeleportToArena", SendMessageOptions.DontRequireReceiver);
            yield return null;
            Vector3 b = cam.transform.position;
            cam.transform.position = new Vector3(b.x - 5.5f, b.y + 7.5f, b.z - 5.5f);
            cam.transform.rotation = Quaternion.Euler(34f, 45f, 0f);
            yield return Frames(240);
            hole = pg.DebugArenaCentre;
            // Task 97: `-fluidbox N` = half-width of the arena's water box (default 3: 7x21x7
            // = 1,029 voxels, ~200 ops/tick, under the 1024/frame drain budget). N = 10 (21x21x21)
            // is a heavy pour whose batches exceed it, where option B's adaptive drain matters.
            int bw = Math.Max(1, ArgInt("-fluidbox", 3));
            wLo = hole + new int3(-bw, 20, -bw); wHi = hole + new int3(bw, 40, bw);
            lc = hole + new int3(30, 15, 0);
            L($"arena centre {hole}, eye {cam.transform.position:F1}");
        }

        L("");
        L("---- BEFORE placement (FLUID RATE here = the last 0 s) ----");
        _rate0 = (Time.unscaledTimeAsDouble, Time.frameCount, pg.DebugTickScheduler?.IssuedTicks ?? 0, pg.DebugTickScheduler?.MissedTicks ?? 0);
        DrainSnap(pg.DebugReadback, out _drain0);
        ProbeState(pg, store, hole, wLo, wHi, lc, true);

        int nW = pg.DebugEdits.SetBox(wLo, wHi, Materials.Water);
        int nL = pg.DebugEdits.SetBox(lc + new int3(-3, 0, -3), lc + new int3(3, 15, 3), Materials.Lava);
        int f0 = Time.frameCount;
        var sched = pg.DebugTickScheduler;
        _rate0 = (Time.unscaledTimeAsDouble, f0, sched?.IssuedTicks ?? 0, sched?.MissedTicks ?? 0);
        DrainSnap(pg.DebugReadback, out _drain0);
        L("");
        L($"PLACED water {nW} voxels {wLo}..{wHi}, lava {nL} voxels at {lc}+(-3,0,-3)..(3,15,3), frame {f0}");
        L($"  same frame: wake queued {pg.DebugFluid.WakeRequestsQueuedTotal}, deferred {pg.DebugFluid.DeferredWakeRequests}, " +
          $"rej_region {pg.DebugFluid.WakeRejectedOutOfRegion}, rej_full {pg.DebugFluid.WakeRejectedFull}");
        if (strip)
        {
            float[] secs = noShots ? new[] { 1f, 2f, 4f, 8f, 12f, 20f, 40f } : new[] { 1f, 2f, 4f, 8f, 12f, 20f };
            foreach (float k in secs)
            {
                while (Time.unscaledTimeAsDouble - _rate0.t < k) yield return null;
                L("");
                L($"---- +{k:F0} s wall clock (frame {Time.frameCount}, +{Time.frameCount - f0} frames) ----");
                ProbeState(pg, store, hole, wLo, wHi, lc, false);
                // Task 97: `-stripnoshots` = the rate arms. A capture is a ~0.3-0.5 s frame, which drops a
                // due tick at any R (3 SHORTFALL ticks per launch at 4.5 Hz and 6.5 Hz alike at 20 FPS).
                if (!noShots && (farM > 0f ? (k == 2f || k == 8f || k == 20f) : (k == 2f || k == 4f || k == 8f)))
                {
                    yield return CaptureTo($"strip_{k:F0}s");
                    if (farM > 0f)
                    {
                        float fov = cam.fieldOfView;
                        cam.fieldOfView = 6f;
                        yield return Frames(3);
                        yield return CaptureTo($"strip_{k:F0}s_tele");
                        cam.fieldOfView = fov;
                    }
                }
            }
        }
        int[] at = strip ? new int[0] : new[] { 1, 2, 3, 5, 10, 20, 45, 90, 180, 225, 400 };
        foreach (int k in at)
        {
            while (Time.frameCount - f0 < k) yield return null;
            if (k == 180) pg.DebugFluid.RequestSlotCountersAsync();
            L("");
            L($"---- +{k} frames (frame {Time.frameCount}) ----");
            ProbeState(pg, store, hole, wLo, wHi, lc, k == 20 || k == 225);
            if (k == 45 || k == 225) yield return CaptureTo($"probe_{k}");
        }
        File.WriteAllText(Path.Combine(_folder, "fluidprobe.txt"), _log.ToString());
        Debug.Log("[PlaygroundCapture]\n" + _log);
        yield return null;
        Application.Quit(0);
    }

    /// Task 93e: no solid voxel in the CPU store on the segment a..b (0.1 m steps,
    /// the last 2 m left out: the target sits on the ground).
    private static bool StoreLineOfSight(ChunkStore store, Vector3 a, Vector3 b)
    {
        Vector3 d = b - a;
        float len = d.magnitude;
        d /= len;
        for (float t = 0.5f; t < len - 2f; t += 0.1f)
        {
            Vector3 p = a + d * t;
            if (store.GetVoxel(CoordMath.WorldToVoxel(new float3(p.x, p.y, p.z))) != 0) return false;
        }
        return true;
    }

    /// Non-air voxels in the inclusive box [lo, hi] of the CPU store.
    private static int SolidIn(ChunkStore store, int3 lo, int3 hi)
    {
        int n = 0;
        for (int z = lo.z; z <= hi.z; z++)
        for (int y = lo.y; y <= hi.y; y++)
        for (int x = lo.x; x <= hi.x; x++)
            if (store.GetVoxel(new int3(x, y, z)) != 0) n++;
        return n;
    }

    /// Wall clock, frame and scheduler counters at placement (FLUID RATE's zero).
    private (double t, int frame, long issued, long missed) _rate0;

    /// Task 97: the readback's drain counters at placement (the DRAIN line's zero).
    private struct DrainCounters
    {
        public long landed, latency, drain, gaps, gapFrames, ops, carried, whole, applyFrames;
        public double applyMs;
        public long[] hist;
    }
    private DrainCounters _drain0;

    private static void DrainSnap(FluidOpListReadback rb, out DrainCounters d)
    {
        d = default;
        if (rb == null) return;
        d.landed = rb.TicksLanded; d.latency = rb.LandLatencyFramesTotal; d.drain = rb.DrainFramesTotal;
        d.gaps = rb.IssueGapsCounted; d.gapFrames = rb.IssueGapFramesTotal; d.ops = rb.OpsInLandedBatchesTotal;
        d.carried = rb.BatchesCarriedForward; d.whole = rb.WholeBatchesApplied;
        d.applyFrames = rb.ApplyFrames; d.applyMs = rb.ApplyMsTotal; d.hist = (long[])rb.ApplyMsHistogram.Clone();
    }

    /// Task 97: tick period split since placement (latency + drain - 1 + gap frames, as in
    /// FLUID_TICK_DIAGNOSIS.md section 1) and the apply's main-thread cost, p99 / max.
    private string DrainLine(FluidOpListReadback rb)
    {
        DrainSnap(rb, out DrainCounters n);
        DrainCounters o = _drain0;
        long landed = n.landed - o.landed, gaps = n.gaps - o.gaps, frames = n.applyFrames - o.applyFrames;
        double D(long v, long c) => c > 0 ? v / (double)c : 0.0;
        double p99 = 0.0;
        if (frames > 0 && o.hist != null)
        {
            long want = (long)Math.Ceiling(0.99 * frames), acc = 0;
            for (int i = 0; i < n.hist.Length; i++)
                if ((acc += n.hist[i] - o.hist[i]) >= want) { p99 = (i + 1) * 0.1; break; }
        }
        return $"  DRAIN (task 97): whole-batch threshold {(rb.WholeBatchMaxOps > 0 ? rb.WholeBatchMaxOps + " ops" : "OFF (-drainwhole 0)")}, " +
               $"budget {FluidOpListReadback.MaxOpsAppliedPerFrame}/frame; since placement: batches landed {landed}, " +
               $"ops/batch {D(n.ops - o.ops, landed):F0}, latency {D(n.latency - o.latency, landed):F2} + drain {D(n.drain - o.drain, landed):F2} " +
               $"+ gap {D(n.gapFrames - o.gapFrames, gaps):F2} frames, carried {n.carried - o.carried}, applied whole {n.whole - o.whole}; " +
               $"apply ms mean {(frames > 0 ? (n.applyMs - o.applyMs) / frames : 0.0):F2} p99 {p99:F1} max (run) {rb.ApplyMsMax:F2} over {frames} frames";
    }

    private void ProbeState(Playground pg, ChunkStore store, int3 hole, int3 wLo, int3 wHi, int3 lc, bool refresh)
    {
        var fl = pg.DebugFluid;
        var rb = pg.DebugReadback;
        var tiles = fl.Tiles;
        var clip = TerrainClipmap.Active;

        // CPU truth: material counts in the boxes and in the column of air/hole below them.
        int wIn = 0, wBelow = 0, lIn = 0, lBelow = 0;
        int3 lLo = lc + new int3(-3, 0, -3), lHi = lc + new int3(3, 15, 3);
        for (int z = wLo.z - 10; z <= wHi.z + 10; z++)
        for (int x = wLo.x - 10; x <= wHi.x + 10; x++)
        for (int y = hole.y - 25; y <= wHi.y + 2; y++)
        {
            byte m = store.GetVoxel(new int3(x, y, z));
            if (m == Materials.Water) { if (y >= wLo.y && x >= wLo.x && x <= wHi.x && z >= wLo.z && z <= wHi.z) wIn++; else wBelow++; }
        }
        for (int z = lLo.z - 10; z <= lHi.z + 10; z++)
        for (int x = lLo.x - 10; x <= lHi.x + 10; x++)
        for (int y = lLo.y - 25; y <= lHi.y + 2; y++)
        {
            byte m = store.GetVoxel(new int3(x, y, z));
            if (m == Materials.Lava) { if (y >= lLo.y && x >= lLo.x && x <= lHi.x && z >= lLo.z && z <= lHi.z) lIn++; else lBelow++; }
        }
        L($"  CPU store: water in box {wIn}, elsewhere nearby {wBelow}; lava in box {lIn}, elsewhere nearby {lBelow}");

        int3 wc = (wLo + wHi) / 2;
        int3 cam = fl.PlayerVoxel;
        float dist = math.distance((float3)wc, (float3)cam);
        L($"  centre: PlayerVoxel {cam}, water centre {wc}, dist {dist:F0}v; wake r {fl.ActiveRadiusVoxels}v, sleep r {fl.SleepRadiusVoxels}v, " +
          $"within wake {FluidActiveRegion.WithinWakeRadius(wc, cam, fl.ActiveRadiusVoxels)}; re-centres {fl.RecentresTotal}");

        foreach (var (nm, v) in new[] { ("water", wc), ("lava", lc + new int3(0, 7, 0)) })
        {
            int3 cc = CoordMath.VoxelToChunk(v);
            Chunk ch = store.GetChunk(cc);
            int3 tc = tiles.TileOf(v);
            string mask = ch == null ? "chunk NOT RESIDENT" :
                $"mask bit {((ch.fluidTileMask & ChunkFluidMask.BitFor(v)) != 0UL ? 1 : 0)} (mask 0x{ch.fluidTileMask:X16}, dirty 0x{ch.fluidTileDirty:X16})";
            string mir = "no clipmap";
            if (clip != null)
            {
                int3 rel = CoordMath.VoxelToBrick(v) - clip.WindowOriginBricks;
                bool inWin = math.all(rel >= 0) && math.all(rel < clip.WindowDimsBricks);
                mir = $"mirror window originBricks {clip.WindowOriginBricks} dimsBricks {clip.WindowDimsBricks} -> in window {inWin}; " +
                      $"chunk dirty {clip.IsDirty(cc)}, lastUpload {clip.LastUploadEpoch(cc)} / epoch {clip.UploadEpoch}; " +
                      $"in mirror box {clip.ContainsChunk(cc)}, fluid terrain dir {(clip.FluidDirEnabled ? $"on: holds it {clip.FluidDirHolds(cc)}, entries {clip.FluidDirEntries} (peak {clip.FluidDirPeakEntries}), uploads {clip.FluidDirUploadsTotal}, refused {clip.FluidDirRefusedTotal}" : "off")}";
            }
            L($"  {nm} voxel {v} chunk {cc}: {mask}; tile {tc} slot {tiles.TryGetSlot(tc)}; {mir}");
        }

        L($"  tiles: resident {tiles.ResidentTiles}/{tiles.TileCapacity} (peak {tiles.PeakResidentTiles}), acquired {tiles.TilesAcquiredTotal}, " +
          $"released {tiles.TilesReleasedTotal}, POOL EXHAUSTIONS {tiles.PoolExhaustionsTotal}; gpu active tiles {fl.ActiveTileCount}");
        if (refresh)
        {
            // The same call Playground makes every 20 frames, with the same args;
            // made here once more only to read its Stats.
            var st = FluidTileResidency.Refresh(store, tiles, cam, fl.ActiveRadiusVoxels, fl.SleepRadiusVoxels);
            L($"  residency refresh: chunks scanned {st.ChunksScanned}, non-resident {st.ChunksSkippedNonResident}, tiles w/ fluid {st.TilesWithFluid}, " +
              $"acquired {st.TilesAcquired}, already {st.TilesAlreadyResident}, REFUSED POOL FULL {st.TilesRefusedPoolFull}, " +
              $"released radius {st.TilesReleasedByRadius}, orphaned {st.TilesReleasedOrphaned}");
        }
        L($"  wake: queued {fl.WakeRequestsQueuedTotal}, dispatched {fl.WakeDispatchedTotal}, pending {fl.PendingWakeRequests}, deferred {fl.DeferredWakeRequests}, " +
          $"coalesced {fl.WakeCoalescedTotal}, released-stale {fl.WakeReleasedStaleTotal}, rej_region {fl.WakeRejectedOutOfRegion}, rej_full {fl.WakeRejectedFull}");
        if (rb != null)
            L($"  readback: ticks issued {rb.RequestsIssuedTotal}, canIssue {rb.CanIssue}, inFlight {rb.FramesInFlight}, pendingOps {rb.PendingOps}, " +
              $"ops total {rb.OpsTotal}, applied writes {rb.AppliedVoxelWrites}, stale dropped {rb.StaleOpsDropped}, " +
              $"non-resident dropped {rb.OpsDroppedNonResident}, skipped issues {rb.SkippedIssuesTotal}, overflow frames {rb.AppendOverflowFramesTotal}");
        var ts = pg.DebugTickScheduler;
        if (ts != null)
        {
            double dt = Math.Max(1e-3, Time.unscaledTimeAsDouble - _rate0.t);
            long issued = ts.IssuedTicks - _rate0.issued, missed = ts.MissedTicks - _rate0.missed;
            L($"  FLUID RATE (task 91): {(ts.Legacy ? "LEGACY -fluidhz 0" : $"fixed {ts.RateHz:F2} Hz")}, lava " +
              $"{(ts.Legacy ? "1/" + MaterialRules.TickInterval(Materials.Lava) + " of ticks" : $"{ts.RateHz / MaterialRules.TickInterval(Materials.Lava):F2} Hz")}; " +
              $"since placement {dt:F2} s, {Time.frameCount - _rate0.frame} frames = {(Time.frameCount - _rate0.frame) / dt:F1} FPS: " +
              $"ticks {issued} = {issued / dt:F2}/s, SHORTFALL missed {missed} = {missed / dt:F2}/s, blocked frames (total) {ts.BlockedFrames}");
        }
        if (rb != null) L(DrainLine(rb));
        L($"  slots: live (last async) {fl.LastLiveSlots} / cap {fl.SlotCapacity}; suspended {Playground.DebugSuspendFluidSim}");
        L($"  GPU last tick: {fl.DebugDump()}");
    }
}
