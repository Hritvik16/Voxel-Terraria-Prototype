// Assets/Game/PlaygroundCapture.SkyTilt.cs
//
// -lookreview -cliffshadow 1 -blackshape 2 -skytiltqa 1: TASK 138's look check of the tilted
// sky probe (RaymarchFeature.SkyProbeTilt, `-skytilt`). After task 136's cliff2 poses (each with
// the arms native = the launch's probe, tilt = 1, tilt2 = 2 (side faces only), nosky), shapes
// are BUILT through the edit-op API (EditOps.Record, as a player's build would be; the
// generality rule: nothing taken from the generator) on the flattest ground 40-80 m behind A:
//   a stone block 15.1 m cube on the ground, its face toward the sun-20 azimuth (snapped to an
//   axis), a straight 4.1 m tunnel 12 m into it (the cave MOUTH), and a sealed 5.1 m room under
//   it (the sealed CAVE, 4 m of rock all round).
//   SHOTS (each native / tilt / tilt2):
//     st_mouth20_*  14 m in front of the face, sun 20 deg behind the camera: the OPEN FACE in sun
//                   (must not change) and the mouth (how far in the light goes)
//     st_mouthnoon_* the same at noon
//     st_tunnel_*   3 m inside the mouth looking deeper (the down-face rule's view dependence)
//     st_sealed_*   inside the sealed room (must stay dark)
// Capture-only: no timing. The ops land in this launch's ops.log; rig / play launches pass -cleardeltas.
using System.Collections;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Mirror;
using VoxelEngine.Rendering;
using VoxelEngine.Streaming;

public partial class PlaygroundCapture
{
    private IEnumerator SkyTiltBuilt(FarField far, Camera cam, StreamManager sm, ShellStreamer sh, Vector3 eyeA, int settle, float wait)
    {
        yield return SkyTiltBuild(far, cam, sm, sh, eyeA, wait);
        float h20 = _st.h20; int3 room = _st.room;
        Vector3 aV = _st.aV, vV = _st.vV, eyeMouth = _st.eyeMouth, mouthC = _st.mouthC;

        string[] arms = { "native", "tilt", "tilt2" };
        // ---- mouth at sun 20 and noon: the open face in sun + the mouth ----
        SunCycle.SetHours(h20);
        yield return BsPose(far, cam, sm, sh, "st_mouth20", eyeMouth, mouthC, settle, wait, arms);
        SunCycle.SetHours(12f);
        yield return BsPose(far, cam, sm, sh, "st_mouthnoon", eyeMouth, mouthC, settle, wait, arms);
        SunCycle.SetHours(h20);
        // ---- inside the tunnel, looking deeper (slightly up, at the ceiling) ----
        Vector3 eyeT = mouthC - aV * 3f + Vector3.down * 0.6f;
        yield return BsPose(far, cam, sm, sh, "st_tunnel", eyeT, eyeT - aV * 8f + Vector3.up * 1.6f, settle, wait, arms);
        // ---- inside the sealed room, looking at a wall corner and the ceiling ----
        Vector3 eyeR = new Vector3(room.x + 0.5f, room.y + 0.5f, room.z + 0.5f) * 0.1f;
        yield return BsPose(far, cam, sm, sh, "st_sealed", eyeR, eyeR + (vV + aV) * 2.5f + Vector3.up * 1.2f, settle, wait, arms);
        Phase4Bootstrapper.DebugStreamCentre = null;
        L("SKY TILT BUILT done");
    }

    /// Task 139: the site and edit ops of SkyTiltBuilt, shared with DarkCompare. Results in _st.
    private struct StSite { public float h20; public int3 C, a, v, room; public Vector3 aV, vV, eyeMouth, mouthC; public EditService edits; }
    private StSite _st;

    private IEnumerator SkyTiltBuild(FarField far, Camera cam, StreamManager sm, ShellStreamer sh, Vector3 eyeA, float wait)
    {
        float h20 = SunCycle.HoursForElevation(20f, false);
        float3 s3 = SunCycle.DirectionAt(h20);
        // the mouth faces the axis nearest the sun-20 azimuth
        int3 a = math.abs(s3.x) >= math.abs(s3.z) ? new int3(s3.x >= 0 ? 1 : -1, 0, 0) : new int3(0, 0, s3.z >= 0 ? 1 : -1);
        int3 v = new int3(a.z, 0, -a.x);
        Vector3 aV = new Vector3(a.x, 0, a.z), vV = new Vector3(v.x, 0, v.z);

        // the flattest 24 m square among candidates 40-80 m from A
        Vector3 best = eyeA; float bestR = float.MaxValue;
        for (int ring = 40; ring <= 80; ring += 20)
        for (int k = 0; k < 8; k++)
        {
            float ang = k * math.PI / 4f;
            Vector3 c = eyeA + new Vector3(math.cos(ang), 0f, math.sin(ang)) * ring;
            float lo = float.MaxValue, hi = float.MinValue;
            for (int i = -2; i <= 2; i++)
            for (int j = -2; j <= 2; j++)
            {
                float t = RefColumn(c.x + i * 6f, c.z + j * 6f).topM;
                lo = math.min(lo, t); hi = math.max(hi, t);
            }
            if (hi - lo < bestR) { bestR = hi - lo; best = c; }
        }
        float g = RefColumn(best.x, best.z).topM;
        int gv = (int)math.floor(g * 10f);
        int3 C = new int3((int)math.floor(best.x * 10f), gv, (int)math.floor(best.z * 10f));
        L("");
        L($"SKY TILT BUILT (task 138): site {best:F1} (ground {g:F1} m, height range {bestR:F1} m over 24 m), mouth faces {a}, sun 20 deg = {h20:F2} h");

        Phase4Bootstrapper.RefTier0Centre = null;
        Vector3 faceM = new Vector3(C.x + a.x * 75 + 0.5f * a.x, gv, C.z + a.z * 75 + 0.5f * a.z) * 0.1f;   // the block's face plane
        Vector3 mouthC = faceM + Vector3.up * 2.5f;
        Vector3 eyeMouth = mouthC + aV * 14f; eyeMouth.y = math.max(mouthC.y, RefColumn(eyeMouth.x, eyeMouth.z).topM + 1.6f);
        Phase4Bootstrapper.DebugStreamCentre = eyeMouth;
        cam.transform.position = eyeMouth; cam.transform.rotation = Quaternion.LookRotation((mouthC - eyeMouth).normalized, Vector3.up);
        yield return NearSettle(far, sm);
        yield return CliffShellWait(sh, wait);

        // ---- build ----
        var store = Phase4Bootstrapper.Store;
        var edits = new EditService();
        edits.AttachWorld(store, store, store, Phase4Bootstrapper.Clipmap);
        var ops = new EditOps(Phase4Bootstrapper.Streamer.Ops, edits, store);
        long redrawn0 = sh != null ? sh.Redrawn : 0;
        ops.Record(new EditOp(EditOpKind.Box, C + new int3(0, 45, 0), 75, Materials.Stone));          // block: g-3 .. g+12 m
        for (int k = 0; k < 3; k++)                                                                     // tunnel, 4.1 m, 12.25 m deep
            ops.Record(new EditOp(EditOpKind.Box, C + a * (75 - 20 - 41 * k) + new int3(0, 25, 0), 20, 0));
        int3 room = C + new int3(0, -60, 0);
        ops.Record(new EditOp(EditOpKind.Box, room, 25, 0));                                            // sealed room g-8.5 .. g-3.5 m
        float t0 = Time.realtimeSinceStartup;
        yield return Frames(3);
        while (Time.realtimeSinceStartup - t0 < wait && sh != null && (sh.RedrawsPending > 0 || (far != null && far.PatchesPending > 0)))
        { cam.transform.position = eyeMouth; yield return null; }
        L($"  built: {ops.NearVoxelsWritten:N0} 0.1 m voxels written near; shells redrew {(sh != null ? sh.Redrawn - redrawn0 : 0)} chunks");
        yield return Frames(30);
        _st = new StSite { h20 = h20, C = C, a = a, v = v, room = room, aV = aV, vV = vV, eyeMouth = eyeMouth, mouthC = mouthC, edits = edits };
    }
}
