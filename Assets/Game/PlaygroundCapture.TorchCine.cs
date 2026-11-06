// Assets/Game/PlaygroundCapture.TorchCine.cs
//
// -lookreview -torchcine 1 (run-look-review.sh EXTRA=): TASK 172, owner Q105 / Q107 2026-11-02. A LOOK, capture
// only, no frame times. Do the RECOMMENDED torch settings together (torch flood ON, -tfdir 2, FSR 3 -fsr3sc 10)
// look cinematic next to TODAY's play look (flood OFF, FSR 3 sc 0)? Six night scenes a player would see, every
// arm in the same frame state (clock paused, fluid suspended, 60 frames settle per arm):
//   camp15   9 torches (8 on 1.5 m posts in a 4 m ring + 1 on a 0.5 m post in the middle) on the start pose's
//            most open level heading, 40 m ahead; the eye 15 m from the camp centre, looking at it
//   camp40   the same camp from 40 m (the start pose), still + a slow strafe burst (0.05 m/frame, 60 lead + 4):
//            do the far torches fade or pop / shimmer
//   cave     a 3 m wide, 3.2 m high, 12 m deep tunnel carved into the nearest sun-independent cliff foot
//            (FindCliffFeet, nearest to the start), 2 wall torches at 4 m and 9 m deep; the eye 8 m outside
//   wall     6 wall torches every 3 m along the same cliff face (22 m beside the cave), 1.5 m over the local
//            ground; the eye at the row's left end, 9 m out, looking along it (corners, steps)
//            No cliff in reach (or the face not found within 40 m): a 30 x 14 m stone block, 10 m tall, is built
//            on the camp's heading and both scenes use it (logged as SYNTHETIC)
//   water    a lone torch on a 1.5 m post, 1.5 m from the nearest shore within 90 m of the start (store scan,
//            2 m grid); none: a 8 x 5 m, 0.5 m deep pond is dug beside the camp site and filled (logged)
//   walk     the camp, walking through the 3 m gap between two ring torches at -tcwalk m/s (default 4), stepped
//            per FRAME at 30 fps: 60 lead + 8 kept frames per arm, so native frame k is the truth for arm frame k
// Arms (-tcarms, default 540,720,432_sc10+f,540_sc10+f,native,native+f): LadderArms' syntax, "+f" = the torch
// flood ON with -tfdir 2 for that arm (TorchFloodPool.Enabled + DirMode; built once per scene with ResetAll, the
// wait logged). Every other arm has the flood OFF (play's 32-light loop + torch shadow rays). Play's dyn 432-540
// rows cannot be pinned for a still, so both ends are shot.
// Each scene's torches (and posts) are removed before the next. Shots: tc_<scene>_<arm>.png, bursts
// tc_<scene>_<arm>_<k>.png. Sheets: python3 -I tools/torchcine172.py <LookReview dir> Screenshots/172
using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Mirror;
using VoxelEngine.Rendering;
using VoxelEngine.Streaming;

public partial class PlaygroundCapture
{
    private IEnumerator TorchCine(FarField far, Camera cam, StreamManager sm, ShellStreamer sh, ChunkStore store, Vector3 centre, float wait)
    {
        string armArg = ArgValue("-tcarms");
        // today = play's FSR 3 dyn 540-720 rows, flood OFF (both ends shot); recommended = dyn 432-540 + sc10 + flood (both ends)
        string[] arms = (string.IsNullOrEmpty(armArg) ? "540,720,432_sc10+f,540_sc10+f,native,native+f" : armArg).Split(',');
        for (int i = 0; i < arms.Length; i++)
        {
            int fi = arms[i].IndexOf("+f", StringComparison.Ordinal);   // "+f" or "+fN" (task 174b: -tcsets entry N)
            arms[i] = fi < 0 ? LadderArms(arms[i])[0] : LadderArms(arms[i].Substring(0, fi))[0] + arms[i].Substring(fi);
        }
        // Task 174b: -tcsets "I/k[/wrap],..." = the flood's level -> light map per "+fN" arm (N 1-based); "+f" = the launch's own
        // -tfi / -tfk / -tfwrap. TorchFloodPool.Params is read every frame (Bind), so a set needs no flood rebuild.
        float fI0 = TorchFloodPool.Intensity, fK0 = TorchFloodPool.FalloffK, fW0 = TorchFloodPool.Wrap;
        var sets = new List<float3>();
        string setArg = ArgValue("-tcsets");
        if (!string.IsNullOrEmpty(setArg))
            foreach (string t in setArg.Split(','))
            {
                var q = t.Split('/');
                float P(int j, float d) => j < q.Length ? float.Parse(q[j], System.Globalization.CultureInfo.InvariantCulture) : d;
                sets.Add(new float3(P(0, fI0), P(1, fK0), P(2, fW0)));
            }
        var driver = FindAnyObjectByType<LightingDriver>();
        if (store == null || driver == null || driver.Lights == null) { L("TORCH CINE: no store / LightingDriver -- skipped"); yield break; }
        LightingDriver.ClockPaused = true;
        Playground.DebugSuspendFluidSim = true;
        Playground.DebugHideUi = true;
        float night = ArgFloat("-nighttod", 0.5f);
        SunCycle.SetHours(night);
        int dir0 = TorchFloodPool.DirMode;
        var edits = new EditService();
        edits.AttachWorld(store, store, store, Phase4Bootstrapper.Clipmap);
        L("");
        L($"TORCH CINE (task 172): arms {string.Join(", ", arms)}; night {night:F2} h (sun elevation {SunCycle.ElevationDegreesAt(night):F0} deg); " +
          $"flood reach {TorchFloodPool.ReachM:F1} m; light knobs k {LightingConfig.Active.pointFalloffK:F2} I {LightingConfig.Active.lightIntensity:F2} " +
          $"range {LightingConfig.Active.lightRangeM:F1} m; lights already registered {driver.Lights.RegisteredCount}");
        L($"  flood sets: +f = I {fI0:F2} k {fK0:F2} wrap {fW0:F2}" +
          string.Concat(System.Linq.Enumerable.Select(System.Linq.Enumerable.Range(0, sets.Count), j => $"; +f{j + 1} = I {sets[j].x:F2} k {sets[j].y:F2} wrap {sets[j].z:F2}")));

        string sceneArg = ArgValue("-tcscenes");   // e.g. "cave,water": shoot only these (setup still runs)
        var want = string.IsNullOrEmpty(sceneArg) ? null : new HashSet<string>(sceneArg.Split(','));
        bool Want(string n) => want == null || want.Contains(n);
        Vector3? profC = null; Vector3 profFwd = Vector3.forward;   // task 174b: the radial-profile torch (camp centre, water torch)
        var placed = new List<int3>();      // every voxel this task wrote (posts, torches) -> restored to air per scene
        var torches = new List<int3>();
        bool floodBuilt = false;

        float Ground(float xM, float zM, float yTop) => StoreGroundM(store, xM, zM, yTop);
        int3 Vox(Vector3 p) => new int3((int)math.floor(p.x * 10f), (int)math.floor(p.y * 10f), (int)math.floor(p.z * 10f));
        bool Torch(int3 v)
        {
            if (store.GetVoxel(v) != Materials.Air) return false;
            if (!edits.TrySetVoxel(v, Materials.Torch)) return false;
            driver.Lights.Register(v, Materials.Torch);
            torches.Add(v); placed.Add(v);
            floodBuilt = false;
            return true;
        }
        bool PostTorch(float xM, float zM, float yTop, int lift)
        {
            float g = Ground(xM, zM, yTop);
            if (float.IsNaN(g)) return false;
            int x = (int)math.floor(xM * 10f), z = (int)math.floor(zM * 10f), y0 = (int)math.round(g * 10f);
            for (int i = 0; i < lift; i++)
            {
                var p = new int3(x, y0 + i, z);
                if (store.GetVoxel(p) == Materials.Air && edits.TrySetVoxel(p, Materials.Stone)) placed.Add(p);
            }
            return Torch(new int3(x, y0 + lift, z));
        }
        void Clear()
        {
            foreach (var t in torches) driver.Lights.Unregister(t);
            for (int i = placed.Count - 1; i >= 0; i--) edits.TrySetVoxel(placed[i], Materials.Air);
            torches.Clear(); placed.Clear(); floodBuilt = false;
        }
        IEnumerator Arm(string arm)
        {
            int fi = arm.IndexOf("+f", StringComparison.Ordinal);
            bool flood = fi >= 0;
            ApplyCompareArm(flood ? arm.Substring(0, fi) : arm);
            int si = flood && arm.Length > fi + 2 ? int.Parse(arm.Substring(fi + 2)) : 0;
            float3 fs = si >= 1 && si <= sets.Count ? sets[si - 1] : new float3(fI0, fK0, fW0);
            TorchFloodPool.Intensity = fs.x; TorchFloodPool.FalloffK = fs.y; TorchFloodPool.Wrap = fs.z;
            TorchFloodPool.DirMode = flood ? 2 : dir0;
            TorchFloodPool.Enabled = flood;
            if (flood && !floodBuilt)
            {
                var pool = TorchFloodPool.Instance;
                pool.ResetAll(store);
                float t0 = Time.realtimeSinceStartup; int fr = 0;
                do { yield return null; fr++; } while ((pool.Busy || !TorchFloodPool.Ready) && Time.realtimeSinceStartup - t0 < 90f);
                L($"    flood built: {pool.TorchCount} torches in {Time.realtimeSinceStartup - t0:F1} s ({fr} frames), ready {TorchFloodPool.Ready}, " +
                  $"busy {pool.Busy}; bricks air {pool.AirBricks} mixed {pool.MixedBricks}, escapes {pool.Escapes}, chunks {pool.Chunks}");
                floodBuilt = true;
            }
            cam.ResetProjectionMatrix();
        }
        IEnumerator Shoot(string scene, Vector3 eye, Quaternion rot)
        {
            Phase4Bootstrapper.DebugStreamCentre = eye;
            cam.transform.SetPositionAndRotation(eye, rot);
            WriteProj(scene);
            foreach (string arm in arms)
            {
                yield return Arm(arm);
                cam.transform.SetPositionAndRotation(eye, rot);
                yield return Frames(60);
                yield return CaptureTo($"tc_{scene}_{ArmFile(arm)}");
            }
            L($"  {scene}: eye {eye:F1}, yaw {rot.eulerAngles.y:F0}, pitch {(rot.eulerAngles.x > 180f ? rot.eulerAngles.x - 360f : rot.eulerAngles.x):F0}; " +
              $"lights registered {driver.Lights.RegisteredCount}{(driver.Lights.CapIsBinding ? " (CAP BINDING)" : "")}");
        }
        // Task 174b: viewport positions (x, y from the bottom-left, z = depth m) of every torch, of the torch + 2 m along the
        // camera's right (the 'near post' disc radius), and -- when profC is set -- of the ground round that torch at r 0.25..10 m
        // on 16 azimuths (az k x 22.5 deg from the scene's 'fwd'): tc_<scene>_proj.csv, for tools/torchtune174b.py.
        void WriteProj(string scene)
        {
            cam.ResetProjectionMatrix();
            var sb = new System.Text.StringBuilder("kind,i,r,az,vx,vy,vz\n");
            void Row(string k, int i, float r, float az, Vector3 w)
            {
                var vp = cam.WorldToViewportPoint(w);
                sb.Append(FormattableString.Invariant($"{k},{i},{r:F2},{az:F1},{vp.x:F5},{vp.y:F5},{vp.z:F2}\n"));
            }
            for (int i = 0; i < torches.Count; i++)
            {
                var tw = new Vector3((torches[i].x + 0.5f) * 0.1f, (torches[i].y + 0.5f) * 0.1f, (torches[i].z + 0.5f) * 0.1f);
                Row("torch", i, 0f, 0f, tw);
                Row("torchR", i, 2f, 0f, tw + cam.transform.right * 2f);
            }
            if (profC.HasValue)
            {
                var c = profC.Value;
                for (int a = 0; a < 16; a++)
                {
                    float az = a * math.PI / 8f;
                    var dirW = profFwd * math.cos(az) + new Vector3(profFwd.z, 0f, -profFwd.x) * math.sin(az);
                    for (int ri = 1; ri <= 40; ri++)
                    {
                        float r = ri * 0.25f;
                        var q = c + dirW * r;
                        float g = Ground(q.x, q.z, c.y + 6f);
                        if (float.IsNaN(g) || math.abs(g - c.y) > 3f) continue;
                        Row("prof", a, r, a * 22.5f, new Vector3(q.x, g + 0.02f, q.z));
                    }
                }
            }
            try { System.IO.File.WriteAllText(System.IO.Path.Combine(_folder, $"tc_{scene}_proj.csv"), sb.ToString()); }
            catch (Exception e) { L($"  proj {scene}: {e.Message}"); }
        }
        IEnumerator Burst(string scene, int lead, int n, Func<int, (Vector3, Quaternion)> pose)
        {
            foreach (string arm in arms)
            {
                yield return Arm(arm);
                var p0 = pose(0);
                cam.transform.SetPositionAndRotation(p0.Item1, p0.Item2);
                yield return Frames(30);
                yield return FsrLiveBurst($"tc_{scene}_{ArmFile(arm)}", lead, n, i => { var p = pose(i); cam.transform.SetPositionAndRotation(p.Item1, p.Item2); });
            }
        }
        Quaternion LookAt(Vector3 eye, Vector3 at) => Quaternion.LookRotation((at - eye).normalized, Vector3.up);

        // ---------------- camp ----------------
        var eyeS = new Vector3(centre.x, WalkEyeY(centre.x, centre.z, centre.y), centre.z);
        float bestYaw = 0f, best = -1f;
        for (int k = 0; k < 8; k++)
        {
            float yaw = k * math.PI / 4f;
            float d = CostFreeDistanceM(eyeS, new float2(math.sin(yaw), math.cos(yaw)), 400f);
            if (d > best) { best = d; bestYaw = yaw; }
        }
        var fwd = new Vector3(math.sin(bestYaw), 0f, math.cos(bestYaw));
        var side = new Vector3(fwd.z, 0f, -fwd.x);
        Phase4Bootstrapper.DebugStreamCentre = eyeS;
        cam.transform.SetPositionAndRotation(eyeS, Quaternion.LookRotation(fwd, Vector3.up));
        yield return NearSettle(far, sm);
        yield return FsrLiveShellWait(sh, wait);
        float gS = Ground(eyeS.x, eyeS.z, eyeS.y + 20f);
        if (!float.IsNaN(gS)) eyeS.y = gS + 1.7f;
        Vector3 C = eyeS + fwd * 40f;
        float gC = Ground(C.x, C.z, eyeS.y + 30f);
        C.y = float.IsNaN(gC) ? eyeS.y - 1.7f : gC;
        int ring = 0;
        for (int i = 0; i < 8; i++)
        {
            float a = (i + 0.5f) * math.PI / 4f;   // rotated half a step: the walk passes between two posts
            var p = C + fwd * (4f * math.cos(a)) + side * (4f * math.sin(a));
            if (PostTorch(p.x, p.z, C.y + 20f, 15)) ring++;
        }
        bool mid = PostTorch(C.x, C.z, C.y + 20f, 5);
        L("");
        L($"== camp: start eye {eyeS:F1}, heading yaw {math.degrees(bestYaw):F0} (free {best:F0} m); camp centre {C:F1} (store ground " +
          $"{(float.IsNaN(gC) ? "NOT FOUND" : gC.ToString("F1"))}), ring torches {ring}/8 + middle {(mid ? 1 : 0)}");
        yield return Frames(30);
        {
            var e15 = C - fwd * 15f;
            float g = Ground(e15.x, e15.z, C.y + 20f);
            e15.y = (float.IsNaN(g) ? C.y : g) + 1.7f;
            profC = C; profFwd = fwd;
            if (Want("camp15")) yield return Shoot("camp15", e15, LookAt(e15, C + Vector3.up * 0.8f));
            profC = null;
            var rot40 = LookAt(eyeS, C + Vector3.up * 0.8f);
            if (Want("camp40")) yield return Shoot("camp40", eyeS, rot40);
            if (Want("camp40pan")) yield return Burst("camp40pan", 60, 4, i => (eyeS + side * (0.05f * i), rot40));
            L($"  camp40pan: strafe 0.05 m/frame, 60 lead + 4");
            // walk: from 11 m before the centre to ~2 m before it, between the two ring posts at +-22.5 deg
            float v = ArgFloat("-tcwalk", 4f) / 30f;
            var w0 = C - fwd * 11f;
            float wy = float.MinValue;
            for (int i = 0; i <= 68; i++)
            {
                var q = w0 + fwd * (v * i);
                float gq = Ground(q.x, q.z, C.y + 20f);
                if (!float.IsNaN(gq)) wy = math.max(wy, gq);
            }
            if (wy == float.MinValue) wy = C.y;
            var walkRot = Quaternion.Euler(10f, math.degrees(bestYaw), 0f);
            if (Want("walk")) yield return Burst("walk", 60, 8, i => (new Vector3(w0.x, wy + 1.7f, w0.z) + fwd * (v * i), walkRot));
            L($"  walk: {v * 30f:F1} m/s, from {w0:F1} along the heading, eye {wy + 1.7f:F1} (highest ground + 1.7), pitch 10 down; 60 lead + 8 kept");
        }
        Clear();
        yield return Frames(10);

        // ---------------- cliff: cave + wall row ----------------
        Vector3 wEye = default, wFwd = default; bool haveCliff = false; string cliffNote = "";
        if (far != null && far.Tg2Map != null)
        {
            int2 off = far.Tg2OffsetVoxels; int seaShift = far.Tg2SeaShift;
            Vector3 E(float3 g) => new Vector3((g.x - off.x) * 0.1f, (g.y - seaShift) * 0.1f, (g.z - off.y) * 0.1f);
            Vector3 bEye = default, bAt = default; float bD = float.MaxValue;
            foreach (var (e, a) in FindCliffFeet(far.Tg2Map, 2000))
            {
                Vector3 ee = E(e);
                float d = math.distance(new float2(ee.x, ee.z), new float2(centre.x, centre.z));
                if (d < bD) { bD = d; bEye = ee; bAt = E(a); }
            }
            if (bD < float.MaxValue)
            {
                Phase4Bootstrapper.DebugStreamCentre = bEye;
                cam.transform.SetPositionAndRotation(bEye, LookAt(bEye, bAt));
                yield return NearSettle(far, sm);
                float g = Ground(bEye.x, bEye.z, bEye.y + 10f);
                if (!float.IsNaN(g)) bEye.y = g + 1.7f;
                wEye = bEye; wFwd = new Vector3(bAt.x - bEye.x, 0f, bAt.z - bEye.z).normalized;
                haveCliff = true;
                cliffNote = $"cliff foot {bD:F0} m from the start, eye {bEye:F1}, looking at {bAt:F1}";
            }
            else cliffNote = "no cliff foot found";
        }
        // the face: march along wFwd at 1.5 m over the eye's ground
        float FaceDist(Vector3 from, Vector3 dir, float maxM)
        {
            for (float t = 0f; t < maxM; t += 0.1f)
                if (store.GetVoxel(Vox(from + dir * t)) != Materials.Air) return t;
            return -1f;
        }
        float D = haveCliff ? FaceDist(wEye - Vector3.up * 0.2f, wFwd, 40f) : -1f;
        if (D < 0f)
        {
            // SYNTHETIC fallback: a stone block 30 m wide (side), 14 m deep, 10 m tall, 25 m ahead on the camp heading
            L($"== cliff: {cliffNote}{(haveCliff ? "; no face within 40 m" : "")} -> SYNTHETIC block on the camp heading");
            wEye = eyeS; wFwd = fwd;
            Phase4Bootstrapper.DebugStreamCentre = wEye;
            cam.transform.SetPositionAndRotation(wEye, Quaternion.LookRotation(wFwd, Vector3.up));
            yield return NearSettle(far, sm);
            var s2 = new Vector3(wFwd.z, 0f, -wFwd.x);
            int built = 0;
            for (float u = -15f; u < 15f; u += 0.1f)
            for (float d = 25f; d < 39f; d += 0.1f)
            {
                var q = wEye + wFwd * d + s2 * u;
                float g = Ground(q.x, q.z, wEye.y + 30f);
                if (float.IsNaN(g)) continue;
                int x = (int)math.floor(q.x * 10f), z = (int)math.floor(q.z * 10f), y0 = (int)math.round(g * 10f);
                built += edits.SetBox(new int3(x, y0 - 2, z), new int3(x, y0 + 100, z), Materials.Stone);
            }
            L($"  synthetic block: {built} voxels");
            yield return Frames(30);
            D = FaceDist(wEye - Vector3.up * 0.2f, wFwd, 40f);
        }
        else L($"== cliff: {cliffNote}; face {D:F1} m ahead");
        if (D > 0f)
        {
            var s = new Vector3(wFwd.z, 0f, -wFwd.x);
            Vector3 face = wEye - Vector3.up * 0.2f + wFwd * D;   // 1.5 m over the eye's ground, on the face
            yield return FsrLiveShellWait(sh, Math.Min(wait, 30f));
            // ---- wall row: 6 torches every 3 m along the face, each against the local face, 1.5 m over its ground ----
            int rowN = 0;
            Vector3 rowFirst = default, rowLast = default;
            for (int k = 0; k < 6; k++)
            {
                var p = face - wFwd * 5f + s * ((k - 2.5f) * 3f);
                float g = Ground(p.x, p.z, p.y + 3f);
                if (float.IsNaN(g)) continue;
                p.y = g + 1.5f;
                float d = FaceDist(p, wFwd, 15f);
                if (d < 0.2f) continue;
                var tp = p + wFwd * (d - 0.1f);
                if (Torch(Vox(tp))) { rowN++; if (rowN == 1) rowFirst = tp; rowLast = tp; }
            }
            L($"  wall row: {rowN}/6 torches along the face");
            if (rowN >= 2)
            {
                var rowMid = (rowFirst + rowLast) * 0.5f;
                var we = rowFirst - wFwd * 9f - s * 4f;
                float g = Ground(we.x, we.z, we.y + 10f);
                we.y = (float.IsNaN(g) ? rowFirst.y - 1.5f : g) + 1.7f;
                if (Want("wall")) yield return Shoot("wall", we, LookAt(we, rowMid));
            }
            Clear();
            yield return Frames(10);

            // ---- cave: 22 m to the side ----
            var m = face + s * 22f;
            float gm = Ground(m.x - wFwd.x * 6f, m.z - wFwd.z * 6f, m.y + 4f);
            float floorY = float.IsNaN(gm) ? m.y - 1.5f : gm;
            float dm = FaceDist(new Vector3(m.x, floorY + 1.5f, m.z) - wFwd * 6f, wFwd, 20f);
            if (dm > 0f)
            {
                var mouth = new Vector3(m.x, floorY, m.z) - wFwd * 6f + wFwd * dm;
                int carved = 0;
                for (float d = -0.5f; d < 12f; d += 0.05f)   // half-voxel steps: a diagonal tunnel at 0.1 m skipped columns (172 rerun: rock bars in the mouth)
                for (float u = -1.5f; u < 1.5f; u += 0.05f)
                {
                    var q = mouth + wFwd * d + s * u;
                    int x = (int)math.floor(q.x * 10f), z = (int)math.floor(q.z * 10f), y0 = (int)math.round(floorY * 10f);
                    carved += edits.SetBox(new int3(x, y0, z), new int3(x, y0 + 31, z), Materials.Air);
                }
                // wall torches 1.6 m up: 4 m deep on the left wall, 9 m deep on the right (the last air voxel before rock)
                int caveN = 0;
                foreach (var (dd, sg) in new[] { (4f, -1f), (9f, 1f) })
                {
                    var c0 = mouth + wFwd * dd + Vector3.up * 1.6f;
                    float sd = FaceDist(c0, s * sg, 3f);
                    if (sd > 0.1f && Torch(Vox(c0 + s * sg * (sd - 0.1f)))) caveN++;
                }
                var ce = mouth - wFwd * 7f;
                float g = Ground(ce.x, ce.z, floorY + 10f);
                ce.y = math.max(float.IsNaN(g) ? floorY : g, floorY) + 1.7f;   // never below the mouth floor (172 rerun)
                L($"  cave: mouth {mouth:F1} ({dm - 6f:F1} m off the row's face line), carved {carved} voxels (3 x 3.2 x 12.5 m), torches {caveN}/2");
                if (Want("cave")) yield return Shoot("cave", ce, LookAt(ce, mouth + wFwd * 4f + Vector3.up * 1.2f));
            }
            else L("  cave: no face 22 m to the side -- skipped");
            Clear();
            yield return Frames(10);
        }
        else L("  cliff: still no face -- cave and wall skipped");

        // ---------------- water: a lone torch on the nearest shore ----------------
        {
            Phase4Bootstrapper.DebugStreamCentre = eyeS;
            cam.transform.SetPositionAndRotation(eyeS, Quaternion.LookRotation(fwd, Vector3.up));
            yield return NearSettle(far, sm);
            Vector3 land = default, wat = default; float bd = float.MaxValue;
            for (float dz = -90f; dz <= 90f; dz += 2f)
            for (float dx = -90f; dx <= 90f; dx += 2f)
            {
                float x = eyeS.x + dx, z = eyeS.z + dz;
                float g = Ground(x, z, eyeS.y + 40f);
                if (float.IsNaN(g)) continue;
                if (store.GetVoxel(Vox(new Vector3(x, g - 0.05f, z))) != Materials.Water) continue;
                for (int k = 0; k < 8; k++)
                {
                    float a = k * math.PI / 4f;
                    float lx = x + 3f * math.cos(a), lz = z + 3f * math.sin(a);
                    float gl = Ground(lx, lz, eyeS.y + 40f);
                    if (float.IsNaN(gl) || gl < g || gl > g + 0.6f) continue;
                    if (store.GetVoxel(Vox(new Vector3(lx, gl - 0.05f, lz))) == Materials.Water) continue;
                    float d = dx * dx + dz * dz;
                    if (d < bd) { bd = d; land = new Vector3(lx, gl, lz); wat = new Vector3(x, g, z); }
                }
            }
            string how;
            if (bd == float.MaxValue)
            {
                // a LEVEL stone terrace (18 x 14 m, cut flat, 4 m plinth) beside the camp site with a flush 8 x 5 m pond,
                // 0.6 m deep, so the shore torch and the water share one level (172 rerun: on the slope the torch stood 4.7 m over the pond)
                var pc = C + side * 8f;
                float g = Ground(pc.x, pc.z, C.y + 20f);
                if (float.IsNaN(g)) g = C.y;
                int y0 = (int)math.round(g * 10f), x0 = (int)math.floor(pc.x * 10f), z0 = (int)math.floor(pc.z * 10f);
                int dug = edits.SetBox(new int3(x0 - 90, y0, z0 - 70), new int3(x0 + 90, y0 + 80, z0 + 70), Materials.Air);
                dug += edits.SetBox(new int3(x0 - 90, y0 - 40, z0 - 70), new int3(x0 + 90, y0 - 1, z0 + 70), Materials.Stone);
                int filled = edits.SetBox(new int3(x0 - 40, y0 - 6, z0 - 25), new int3(x0 + 40, y0 - 1, z0 + 25), Materials.Water);
                yield return Frames(10);
                wat = new Vector3(x0 * 0.1f, y0 * 0.1f, z0 * 0.1f + 1.5f);
                land = new Vector3(x0 * 0.1f, y0 * 0.1f, (z0 - 25) * 0.1f - 1.5f);   // 1.5 m back from the pond's -z edge
                how = $"no shore within 90 m -> SYNTHETIC level terrace + pond beside the camp site (terrace {dug}, water {filled} voxels)";
            }
            else how = $"nearest shore {math.sqrt(bd):F0} m from the start";
            var toW = new Vector3(wat.x - land.x, 0f, wat.z - land.z).normalized;
            var tPos = land;   // the land sample 3 m from a water column: the torch stands ~1-3 m back from the edge
            bool ok = PostTorch(tPos.x, tPos.z, tPos.y + 5f, 15);
            var along = new Vector3(toW.z, 0f, -toW.x);
            var we = tPos - along * 7f - toW * 1f;
            float gw = Ground(we.x, we.z, tPos.y + 10f);
            we.y = (float.IsNaN(gw) ? tPos.y : gw) + 1.7f;
            L("");
            L($"== water: {how}; land {land:F1}, water column {wat:F1}; torch on a 1.5 m post {(ok ? "placed" : "REFUSED")}");
            yield return FsrLiveShellWait(sh, Math.Min(wait, 20f));
            profC = tPos; profFwd = toW;   // az 0 = toward the water, 180 = away from it
            if (ok && Want("water")) yield return Shoot("water", we, LookAt(we, tPos + toW * 2f + Vector3.up * 0.5f));
            profC = null;
            Clear();
        }

        TorchFloodPool.Enabled = false;
        TorchFloodPool.DirMode = dir0;
        TorchFloodPool.Intensity = fI0; TorchFloodPool.FalloffK = fK0; TorchFloodPool.Wrap = fW0;
        Playground.DebugHideUi = false;
        SunCycle.SetHours(SunCycle.NoonHours);
        Phase4Bootstrapper.DebugStreamCentre = null;
        L("TORCH CINE done");
    }

    private static string ArmFile(string arm) => arm.Replace("+f", "_flood");
}
