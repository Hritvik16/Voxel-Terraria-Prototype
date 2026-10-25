// Assets/Game/PlaygroundCapture.HolePillar.cs
//
// -lookreview -cliffshadow 1 -cliffbefore 3 -holepillar 1 (with -holeleap 1): TASK 137's ADVERSARIAL
// identity pose (owner's generality rule). After the cliff spots, a 6.1 m x 6.1 m stone pillar
// 250 m tall is BUILT through the edit-op API (EditOps.Record: stacked box ops, applied at once to
// the resident 0.1 m chunks and redrawn in the shells, as a player's far build would be) 65 m
// toward a 50 deg sun from the start pose. Near ground there is in its shadow, and the sun rays
// from it leave tier 0's band ceiling, cross shell 1's hole (the hole leap's box) and meet the
// pillar in the shells. Capture-only: no timing.
//   SHOTS  from the side (35 m off, 15 m up, looking at the ground between the start and the pillar):
//          pillar_native   the launch's hole leap (ON), native 1080, no upscaler
//          pillar_native0  the hole leap forced OFF (-cliffbefore 3): must be byte-identical
//          pillar_nonear   near sun rays through the shells OFF (task 133 off): the CONTROL; the
//                          pillar's upper shadow must differ here, or the pose proves nothing
//          pillar_play     the play look (FSR 3 dynamic), for the doc
//   The ops land in this launch's ops.log; every rig / play launch passes -cleardeltas.
using System.Collections;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Mirror;
using VoxelEngine.Rendering;
using VoxelEngine.Streaming;

public partial class PlaygroundCapture
{
    private IEnumerator HolePillar(FarField far, Camera cam, StreamManager sm, ShellStreamer sh, Vector3 start, int settle, float wait)
    {
        float elev = ArgFloat("-pillarsun", 50f);
        float hours = SunCycle.HoursForElevation(elev, false);
        SunCycle.SetHours(hours);
        float3 s3 = SunCycle.DirectionAt(hours);
        float2 ts = math.normalizesafe(new float2(s3.x, s3.z));
        Vector3 toSun = new Vector3(ts.x, 0f, ts.y), perp = new Vector3(ts.y, 0f, -ts.x);
        float gE = RefColumn(start.x, start.z).topM;
        Vector3 E = new Vector3(start.x, gE, start.z);
        Vector3 P = E + toSun * 65f;
        float gP = RefColumn(P.x, P.z).topM;
        Vector3 eye = E + perp * 35f; eye.y = RefColumn(eye.x, eye.z).topM + 15f;
        Vector3 M = E + toSun * 25f; M.y = RefColumn(M.x, M.z).topM;
        Quaternion rot = Quaternion.LookRotation((M - eye).normalized, Vector3.up);
        L("");
        L($"HOLE PILLAR (task 137): sun {elev:F0} deg ({hours:F2} h); start ground {E:F1}, pillar at {P.x:F1},{P.z:F1} (ground {gP:F1} m), " +
          $"65 m toward the sun, 250 m tall; eye {eye:F1} looking at {M:F1}");
        Phase4Bootstrapper.RefTier0Centre = null;
        Phase4Bootstrapper.DebugStreamCentre = eye;
        cam.transform.position = eye; cam.transform.rotation = rot;
        yield return NearSettle(far, sm);
        yield return CliffShellWait(sh, wait);
        L($"  [window] band ceiling {RaymarchFeature.ContentCeilingVoxelY * 0.1f - gE:F0} m above the start's ground");

        // ---- build: stacked box ops (|d| <= 30 voxels: 6.1 m cubes) from 5 m under the ground to 250 m up ----
        var store = Phase4Bootstrapper.Store;
        var edits = new EditService();
        edits.AttachWorld(store, store, store, Phase4Bootstrapper.Clipmap);
        var ops = new EditOps(Phase4Bootstrapper.Streamer.Ops, edits, store);
        long redrawn0 = sh != null ? sh.Redrawn : 0;
        int cx = (int)math.floor(P.x * 10f), cz = (int)math.floor(P.z * 10f);
        int y0 = (int)math.floor(gP * 10f) - 50, y1 = (int)math.floor(gP * 10f) + 2500, n = 0;
        for (int y = y0 + 30; y - 30 <= y1; y += 61, n++)
            ops.Record(new EditOp(EditOpKind.Box, new int3(cx, y, cz), 30, Materials.Stone));
        float t0 = Time.realtimeSinceStartup;
        yield return Frames(3);
        while (Time.realtimeSinceStartup - t0 < wait && sh != null && (sh.RedrawsPending > 0 || (far != null && far.PatchesPending > 0)))
        { cam.transform.position = eye; yield return null; }
        L($"  built {n} box ops, {ops.NearVoxelsWritten:N0} 0.1 m voxels written near; shells redrew {(sh != null ? sh.Redrawn - redrawn0 : 0)} chunks, " +
          $"{(sh != null ? sh.RedrawsPending : 0)} still pending after {Time.realtimeSinceStartup - t0:F0} s");
        string held = "";
        if (sh != null) for (int k = sh.FirstShell; k <= sh.LastShell; k++)
            if (sh.Store(k).Holds(new int3(cx, (int)math.floor(gP * 10f) + 1200, cz) >> (7 + k))) held += $" {k}";
        L($"  shells holding the pillar's middle (120 m up):{(held.Length == 0 ? " none" : held)}");
        yield return Frames(30);

        foreach (string arm in new[] { "native", "native0", "play" })
            yield return CliffArm(cam, eye, rot, arm, $"pillar_{arm}", settle);
        bool near0 = RaymarchFeature.NearShadowShells;
        RaymarchFeature.NearShadowShells = false;
        yield return CliffArm(cam, eye, rot, "native", "pillar_nonear", settle);
        RaymarchFeature.NearShadowShells = near0;
        Phase4Bootstrapper.DebugStreamCentre = null;
    }
}
