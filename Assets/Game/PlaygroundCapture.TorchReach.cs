// Assets/Game/PlaygroundCapture.TorchReach.cs
//
// -lookreview -cliffshadow 1 -blackshape 2 -torchreach 1: TASK 143, a CAPTURE-ONLY side-by-side of how
// far one torch reaches (owner Q92). No default or look change: each arm sets the three EXISTING C.11
// point-light knobs on LightingConfig.Active for its own shots and restores them --
//   pointFalloffK (k in atten = 1/(1 + d^2 k), d in metres), lightIntensity (multiplies every light's
//   colour) and lightRangeM (A.6's hard cutoff radius). The range is baked into a light when it is
//   registered (LightSourceBuffer.Register), so every arm re-registers its torch; k and intensity are
//   read on every Upload.
//   ARMS  none   the torch voxel stays, its light is unregistered (the baseline for lit-pixel counts)
//         today  the launch's values (the lighting preset's, logged)
//         room / half / tunnel  three wider settings (-trarms "k,I,R;k,I,R;k,I,R" overrides)
//   Each arm twice: _play (FSR 3 dyn, for the sheet) and _nat (native 1080, for the counts).
//   ROWS  at night (-nighttod, default 0.5 h), ONE registered torch per row, the others unregistered:
//         tunnel  task 138's built tunnel, eye at the mouth looking in, torch by the -v wall 10.2 m in
//         room    the sealed room, eye high in one corner, torch -trlift up in the opposite corner
//         ground  open ground 6 m in front of the block's face (torch on a -trlift stone post), eye 12 m back, 3.5 m up
// Shots tr_<row>_<arm>_<play|nat>.png; sheet + counts: python3 -I tools/torchreach143.py <dir> Screenshots/143.
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
    private struct TrArm { public string name; public float k, intensity, range; }

    private IEnumerator TorchReach(FarField far, Camera cam, StreamManager sm, ShellStreamer sh, Vector3 eyeA, int settle, float wait)
    {
        var cfg = LightingConfig.Active;
        var arms = new List<TrArm> { new TrArm { name = "today", k = cfg.pointFalloffK, intensity = cfg.lightIntensity, range = cfg.lightRangeM } };
        string[] names = { "room", "half", "tunnel" };
        string spec = ArgValue("-trarms");
        if (string.IsNullOrEmpty(spec)) spec = "0.35,1.6,8;0.15,2.4,14;0.06,3.0,24";
        string[] sets = spec.Split(';');
        for (int i = 0; i < sets.Length && i < names.Length; i++)
        {
            var p = sets[i].Split(',');
            arms.Add(new TrArm { name = names[i], k = float.Parse(p[0], CultureInfo.InvariantCulture),
                                 intensity = float.Parse(p[1], CultureInfo.InvariantCulture),
                                 range = float.Parse(p[2], CultureInfo.InvariantCulture) });
        }
        int natFrames = ArgInt("-dcnatframes", 20);
        float night = ArgFloat("-nighttod", 0.5f);
        L("");
        L($"TORCH REACH (task 143): night {night:F2} h; play settle {settle}, native {natFrames} frames; arms (k, intensity, range m):");
        foreach (var a in arms) L($"  {a.name,-7} k {a.k:F3}  intensity {a.intensity:F2}  range {a.range:F1}");

        yield return SkyTiltBuild(far, cam, sm, sh, eyeA, wait);
        var st = _st;
        var store = Phase4Bootstrapper.Store;

        // torches: tunnel floor 10.2 m in by the -v wall (task 139's); the room's floor corner; open ground
        // -trlift N (voxels, default 15 = 1.5 m): a torch ON the floor (0) lights its own floor only at grazing
        // incidence, so the floor stays dark whatever the reach (the first launch's finding); 1.5 m = wall height
        int lift = ArgInt("-trlift", 15);
        int3 tT = st.C + st.a * -27 + st.v * -19 + new int3(0, 5 + lift, 0);
        int3 tR = st.room + (st.v + st.a) * 23 + new int3(0, -25 + lift, 0);
        int3 g = st.C + st.a * 135 + st.v * 50;                         // 6 m out from the face, 5 m beside the mouth axis
        g.y = (int)math.floor(RefColumn((g.x + 0.5f) * 0.1f, (g.z + 0.5f) * 0.1f).topM * 10f) - 3;
        for (int i = 0; i < 40 && store.GetVoxel(g) != Materials.Air; i++) g.y++;
        int3 tG = g + new int3(0, lift, 0);   // on a stone post 'lift' voxels tall

        var driver = FindAnyObjectByType<LightingDriver>();
        if (driver == null) { L("  TORCH REACH: no LightingDriver -- skipped"); yield break; }
        driver.Attach(st.edits);   // a torch registers its point light only through an attached buffer (task 139)
        var lights = driver.Lights;
        int before = lights.RegisteredCount;
        for (int y = 0; y < lift; y++) st.edits.TrySetVoxel(g + new int3(0, y, 0), Materials.Stone);
        bool okT = st.edits.TrySetVoxel(tT, Materials.Torch), okR = st.edits.TrySetVoxel(tR, Materials.Torch),
             okG = st.edits.TrySetVoxel(tG, Materials.Torch);
        yield return Frames(60);
        L($"  torches lifted {lift * 0.1f:F1} m: tunnel {tT} {(okT ? "placed" : "REFUSED")}, room {tR} {(okR ? "placed" : "REFUSED")}, " +
          $"ground {tG} {(okG ? "placed" : "REFUSED")}; lights registered {before} -> {lights.RegisteredCount}");
        int3[] all = { tT, tR, tG };

        Vector3 Ctr(int3 v) => new Vector3(v.x + 0.5f, v.y + 0.5f, v.z + 0.5f) * 0.1f;
        Vector3 eyeT = st.mouthC + st.aV * 0.5f + Vector3.down * 0.6f;
        Quaternion rt = DcAim(cam, eyeT, eyeT - st.aV * 10f + Vector3.down * 0.8f, "tunnel");
        Vector3 eyeR = Ctr(st.room - (st.v + st.a) * 20 + new int3(0, 8, 0));
        Quaternion rr = DcAim(cam, eyeR, Ctr(tR) + Vector3.down * (lift * 0.1f - 0.8f), "room");
        Vector3 tg = Ctr(tG);
        Vector3 eyeG = tg + st.aV * 12f; eyeG.y = RefColumn(eyeG.x, eyeG.z).topM + 3.5f;
        Quaternion rg = DcAim(cam, eyeG, tg + Vector3.down * (lift * 0.1f - 0.5f), "ground");

        float k0 = cfg.pointFalloffK, i0 = cfg.lightIntensity, r0 = cfg.lightRangeM;
        SunCycle.SetHours(night);
        RaymarchFeature.UseDebugViewOverride = false;
        var rows = new (string row, int3 torch, Vector3 eye, Quaternion rot)[]
        {
            ("tunnel", tT, eyeT, rt), ("room", tR, eyeR, rr), ("ground", tG, eyeG, rg),
        };
        foreach (var row in rows)
        {
            Phase4Bootstrapper.DebugStreamCentre = row.eye;
            cam.transform.SetPositionAndRotation(row.eye, row.rot);
            yield return Frames(30);
            L($"  {row.row}: torch {Vector3.Distance(row.eye, Ctr(row.torch)):F1} m from the eye");
            for (int ai = -1; ai < arms.Count; ai++)
            {
                string armName = ai < 0 ? "none" : arms[ai].name;
                foreach (var t in all) lights.Unregister(t);
                if (ai >= 0)
                {
                    cfg.pointFalloffK = arms[ai].k; cfg.lightIntensity = arms[ai].intensity; cfg.lightRangeM = arms[ai].range;
                    lights.Register(row.torch, Materials.Torch);   // re-registered: picks up this arm's range
                }
                foreach (bool play in new[] { true, false })
                {
                    ApplyCompareArm(play ? "fsr3_dyn" : "native");
                    cam.ResetProjectionMatrix();
                    cam.transform.SetPositionAndRotation(row.eye, row.rot);
                    yield return Frames(play ? settle : natFrames);
                    yield return CaptureTo($"tr_{row.row}_{armName}_{(play ? "play" : "nat")}");
                }
                cfg.pointFalloffK = k0; cfg.lightIntensity = i0; cfg.lightRangeM = r0;
            }
        }
        foreach (var t in all) { lights.Unregister(t); lights.Register(t, Materials.Torch); }   // back to the launch's values
        L("TORCH REACH done");
    }
}
