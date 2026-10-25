// Assets/Game/PlaygroundCapture.DarkCompare.cs
//
// -lookreview -cliffshadow 1 -blackshape 2 -darkcompare 1: TASK 139, a CAPTURE-ONLY side-by-side of
// what to do about pure-black overhangs and caves (owner Q91). No default or look change: each arm
// sets LightingConfig.Active.undergroundAmbientFloor or RaymarchFeature.UseSkyProbeAmbient for its own
// shots and restores them.
//   ARMS  today  the launch's floor (the quality preset's undergroundAmbientFloor, logged)
//         fNN    floor = NN / 100, three values from -dcfloors (default 0.12,0.25,0.40)
//         nosky  the sky probe off: every face lit as open sky (the "fully open" end of the range; a
//                soft sky term (option C) can only be approximated here: it would sit between the
//                floor arms and nosky on open overhangs, and near the floor arms deep in a cave)
//   Each arm twice: _play (FSR 3 dyn, for the sheet) and _nat (native 1080, for pure-black counts).
//   ROWS  cliff2 A (the ledge) and the stone arches (task 136) at noon and dusk (sun 10 deg, evening);
//         task 138's built block (SkyTiltBuild: 15 m stone block, 12 m tunnel, sealed room): the mouth
//         noon / dusk, inside the tunnel and the sealed room noon / night (-nighttod, default 0.5 h);
//         then ONE torch each in the tunnel and the room (EditService.TrySetVoxel, Materials.Torch, as
//         -farshadeshots does) and tunnel / room at night with arms today / f2nd / nosky.
// -dconly torch: only the build and the two torch rows (a re-shoot).
// Shots dc_<row>_<arm>_<play|nat>.png; sheets + counts: python3 -I tools/darkcompare139.py <dir> Screenshots/139.
// Capture-only: no timing. The block's ops land in this launch's ops.log (launches pass -cleardeltas).
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Mirror;
using VoxelEngine.Rendering;
using VoxelEngine.Streaming;

public partial class PlaygroundCapture
{
    private IEnumerator DarkCompare(FarField far, Camera cam, StreamManager sm, ShellStreamer sh,
                                    Vector3 eyeA, Quaternion rotA, Vector3 fwdH, int settle, float wait)
    {
        var cfg = LightingConfig.Active;
        var floors = new List<float>();
        string fArg = ArgValue("-dcfloors");
        foreach (var f in (string.IsNullOrEmpty(fArg) ? "0.12,0.25,0.40" : fArg).Split(','))
            floors.Add(float.Parse(f, CultureInfo.InvariantCulture));
        var all = new List<string> { "today" };
        foreach (float f in floors) all.Add("f" + Mathf.RoundToInt(f * 100f));
        all.Add("nosky");
        string[] arms = all.ToArray();
        string[] torchArms = { "today", arms[2], "nosky" };
        int natFrames = ArgInt("-dcnatframes", 20);
        float noon = 12f, dusk = SunCycle.HoursForElevation(10f, false), night = ArgFloat("-nighttod", 0.5f);
        L("");
        L($"DARK COMPARE (task 139): today's floor {cfg.undergroundAmbientFloor:F3}; arms {string.Join(" ", arms)}; " +
          $"noon 12.00 h, dusk (sun 10 deg) {dusk:F2} h, night {night:F2} h; play settle {settle}, native {natFrames} frames");

        bool torchOnly = ArgValue("-dconly") == "torch";   // re-shoot only the build + the torch rows
        // ---- cliff2 A, the ledge (already streamed by BlackShape) ----
        if (!torchOnly)
        {
            yield return DcRow(cam, eyeA, rotA, "ledge_noon", noon, arms, settle, natFrames);
            yield return DcRow(cam, eyeA, rotA, "ledge_dusk", dusk, arms, settle, natFrames);

            // ---- the stone arches, from their shadow side ----
            Vector3 o = BsOtherPoint();
            Vector3 eo = o - fwdH * 28f; eo.y = RefColumn(eo.x, eo.z).topM + 1.6f;
            Quaternion ro = DcAim(cam, eo, o, "arches");
            yield return NearSettle(far, sm);
            yield return CliffShellWait(sh, wait);
            yield return DcRow(cam, eo, ro, "arches_noon", noon, arms, settle, natFrames);
            yield return DcRow(cam, eo, ro, "arches_dusk", dusk, arms, settle, natFrames);
        }

        // ---- task 138's built block: mouth, tunnel, sealed room ----
        yield return SkyTiltBuild(far, cam, sm, sh, eyeA, wait);
        var st = _st;
        Quaternion rm = DcAim(cam, st.eyeMouth, st.mouthC, "mouth");
        Vector3 eyeT = st.mouthC - st.aV * 3f + Vector3.down * 0.6f;
        Vector3 eyeR = new Vector3(st.room.x + 0.5f, st.room.y + 0.5f, st.room.z + 0.5f) * 0.1f;
        Quaternion rt = Quaternion.LookRotation((-st.aV * 8f + Vector3.up * 1.6f).normalized, Vector3.up);
        Quaternion rr = Quaternion.LookRotation(((st.vV + st.aV) * 2.5f + Vector3.up * 1.2f).normalized, Vector3.up);
        if (!torchOnly)
        {
            yield return DcRow(cam, st.eyeMouth, rm, "mouth_noon", noon, arms, settle, natFrames);
            yield return DcRow(cam, st.eyeMouth, rm, "mouth_dusk", dusk, arms, settle, natFrames);

            DcAim(cam, eyeT, eyeT - st.aV * 8f + Vector3.up * 1.6f, "tunnel");
            yield return Frames(30);
            yield return DcRow(cam, eyeT, rt, "tunnel_noon", noon, arms, settle, natFrames);
            yield return DcRow(cam, eyeT, rt, "tunnel_night", night, arms, settle, natFrames);

            DcAim(cam, eyeR, eyeR + (st.vV + st.aV) * 2.5f + Vector3.up * 1.2f, "sealed");
            yield return Frames(30);
            yield return DcRow(cam, eyeR, rr, "sealed_noon", noon, arms, settle, natFrames);
            yield return DcRow(cam, eyeR, rr, "sealed_night", night, arms, settle, natFrames);
        }

        // ---- one torch each: tunnel floor 10.2 m in (7 m ahead of the eye, by the -v wall); the room's
        //      far corner 0.5 m above the eye ----
        int3 tT = st.C + st.a * -27 + st.v * -19 + new int3(0, 5, 0);
        int3 tR = st.room + (st.v + st.a) * 24 + new int3(0, 5, 0);
        var driver = FindAnyObjectByType<LightingDriver>();
        if (driver != null) driver.Attach(st.edits);   // a torch registers its point light only through an attached buffer
        int before = driver != null ? driver.Lights.RegisteredCount : 0;
        bool okT = st.edits.TrySetVoxel(tT, Materials.Torch), okR = st.edits.TrySetVoxel(tR, Materials.Torch);
        yield return Frames(60);
        L($"  torches: tunnel {tT} {(okT ? "placed" : "REFUSED")}, room {tR} {(okR ? "placed" : "REFUSED")}; " +
          $"lights registered {before} -> {(driver != null ? driver.Lights.RegisteredCount : 0)}");
        cam.transform.SetPositionAndRotation(eyeT, rt);
        yield return Frames(30);
        yield return DcRow(cam, eyeT, rt, "tunnel_torch", night, torchArms, settle, natFrames);
        cam.transform.SetPositionAndRotation(eyeR, rr);
        yield return Frames(30);
        yield return DcRow(cam, eyeR, rr, "sealed_torch", night, torchArms, settle, natFrames);
        L("DARK COMPARE done");
    }

    /// Points the camera (no coroutine: the caller waits); returns the rotation.
    private Quaternion DcAim(Camera cam, Vector3 eye, Vector3 look, string name)
    {
        Quaternion rot = Quaternion.LookRotation((look - eye).normalized, Vector3.up);
        L($"  [{name}] eye {eye:F1} looking at {look:F1} ({Vector3.Distance(eye, look):F0} m)");
        Phase4Bootstrapper.DebugStreamCentre = eye;
        cam.transform.SetPositionAndRotation(eye, rot);
        return rot;
    }

    private IEnumerator DcRow(Camera cam, Vector3 eye, Quaternion rot, string row, float hours, string[] arms, int settle, int natFrames)
    {
        SunCycle.SetHours(hours);
        L($"  {row}: {hours:F2} h");
        var cfg = LightingConfig.Active;
        float f0 = cfg.undergroundAmbientFloor;
        bool sky0 = RaymarchFeature.UseSkyProbeAmbient;
        RaymarchFeature.UseDebugViewOverride = false;
        foreach (string arm in arms)
        {
            if (arm == "nosky") RaymarchFeature.UseSkyProbeAmbient = false;
            else if (arm != "today") cfg.undergroundAmbientFloor = int.Parse(arm.Substring(1)) / 100f;
            foreach (bool play in new[] { true, false })
            {
                ApplyCompareArm(play ? "fsr3_dyn" : "native");
                cam.ResetProjectionMatrix();
                cam.transform.SetPositionAndRotation(eye, rot);
                yield return Frames(play ? settle : natFrames);
                yield return CaptureTo($"dc_{row}_{arm}_{(play ? "play" : "nat")}");
            }
            cfg.undergroundAmbientFloor = f0;
            RaymarchFeature.UseSkyProbeAmbient = sky0;
        }
    }
}
