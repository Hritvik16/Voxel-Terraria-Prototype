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
// TASK 158 (-trflood 1, TORCH_LIGHT_PLAN L2): one more arm per row, "flood" = the row's torch through
// the GPU light pool (TorchFloodPool, -torchflood's kernel twin) at its 12 m reach, then a GPU == oracle
// probe at 16 x 9 pixels (the air cell in front of each pixel's first solid voxel, CPU DDA), an OFF
// re-shoot of tr_ground_today_nat after the pool existed (tr_ground_today_nat_off2: byte-identical?),
// and a PILLAR row: a 0.7 x 0.7 x 2.5 m stone pillar 1.5 m beside the ground torch, arms none / today /
// flood (tr_pillar_*). Sheet: python3 -I tools/torchflood158.py <dir> Screenshots/158 <launch log>.
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
        bool floodArm = ArgInt("-trflood", 0) == 1;
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
        // -trfloodb "I,k,wrap": a second flood map (the same pool, only the shader's level -> light uniforms), arm "floodb"
        string fb = ArgValue("-trfloodb");
        float[] fbMap = string.IsNullOrEmpty(fb) ? null : System.Array.ConvertAll(fb.Split(','), x => float.Parse(x, CultureInfo.InvariantCulture));
        float fI0 = TorchFloodPool.Intensity, fK0 = TorchFloodPool.FalloffK, fW0 = TorchFloodPool.Wrap;
        int nArms = arms.Count + (floodArm ? (fbMap != null ? 2 : 1) : 0);
        foreach (var row in rows)
        {
            Phase4Bootstrapper.DebugStreamCentre = row.eye;
            cam.transform.SetPositionAndRotation(row.eye, row.rot);
            yield return Frames(30);
            L($"  {row.row}: torch {Vector3.Distance(row.eye, Ctr(row.torch)):F1} m from the eye");
            for (int ai = -1; ai < nArms; ai++)
            {
                bool isFloodB = ai == arms.Count + 1;
                bool isFlood = ai == arms.Count || isFloodB;
                string armName = ai < 0 ? "none" : isFloodB ? "floodb" : isFlood ? "flood" : arms[ai].name;
                TorchFloodPool.Intensity = isFloodB ? fbMap[0] : fI0;
                TorchFloodPool.FalloffK = isFloodB ? fbMap[1] : fK0;
                TorchFloodPool.Wrap = isFloodB ? fbMap[2] : fW0;
                foreach (var t in all) lights.Unregister(t);
                if (isFlood)
                {
                    lights.Register(row.torch, Materials.Torch);
                    FloodOn(lights, row.row + (isFloodB ? " floodb" : ""));
                }
                else if (ai >= 0)
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
                if (isFlood) { FloodProbe(cam, row.row); TorchFloodPool.Enabled = false; }
                cfg.pointFalloffK = k0; cfg.lightIntensity = i0; cfg.lightRangeM = r0;
            }
        }
        if (floodArm)
        {
            // OFF after the pool existed: the ground row's today native still again (compare bytes with the first)
            foreach (var t in all) lights.Unregister(t);
            lights.Register(tG, Materials.Torch);
            ApplyCompareArm("native");
            cam.ResetProjectionMatrix();
            Phase4Bootstrapper.DebugStreamCentre = eyeG;
            cam.transform.SetPositionAndRotation(eyeG, rg);
            yield return Frames(natFrames);
            yield return CaptureTo("tr_ground_today_nat_off2");

            // PILLAR: 0.7 x 0.7 x 2.5 m, 1.5 m beside the ground torch (along st.v), from 0.3 m into the ground
            int3 pc = tG + st.v * 15;
            int built = 0;
            for (int dx = -3; dx <= 3; dx++)
            for (int dz = -3; dz <= 3; dz++)
            for (int y = g.y - 3; y < g.y + 25; y++)
            {
                var v = new int3(pc.x + dx, y, pc.z + dz);
                if (store.GetVoxel(v) == Materials.Air && st.edits.TrySetVoxel(v, Materials.Stone)) built++;
            }
            yield return Frames(30);
            L($"  pillar: centre column {pc}, {built} voxels written");
            TorchFloodPool.Intensity = fI0; TorchFloodPool.FalloffK = fK0; TorchFloodPool.Wrap = fW0;
            foreach (string armName in new[] { "none", "today", "flood" })
            {
                foreach (var t in all) lights.Unregister(t);
                if (armName != "none") lights.Register(tG, Materials.Torch);
                if (armName == "flood") FloodOn(lights, "pillar");
                foreach (bool play in new[] { true, false })
                {
                    ApplyCompareArm(play ? "fsr3_dyn" : "native");
                    cam.ResetProjectionMatrix();
                    cam.transform.SetPositionAndRotation(eyeG, rg);
                    yield return Frames(play ? settle : natFrames);
                    yield return CaptureTo($"tr_pillar_{armName}_{(play ? "play" : "nat")}");
                }
                if (armName == "flood") { FloodProbe(cam, "pillar"); TorchFloodPool.Enabled = false; }
            }
        }
        foreach (var t in all) { lights.Unregister(t); lights.Register(t, Materials.Torch); }   // back to the launch's values
        L("TORCH REACH done");
    }

    /// Task 158: the flood on for one row's torch, built now (synchronous, before the shots).
    private void FloodOn(LightSourceBuffer lights, string tag)
    {
        TorchFloodPool.Enabled = true;
        TorchFloodPool.KeepOracle = true;
        var pool = TorchFloodPool.Instance;
        pool.Rebuild(lights);
        var e = pool.Current;
        L($"  flood ({tag}): {pool.LastTorches} torch, {e.chunks} lit chunks, air bricks {e.airBricks}, mixed {e.mixedBricks}, " +
          $"escapes {e.escapes}, GPU {e.Bytes / 1048576.0:F2} MB; rebuild {pool.LastRebuildMs:F0} ms (flood {pool.LastFloodMs:F0}, " +
          $"encode {pool.LastEncodeMs:F0}); map I {TorchFloodPool.Intensity:F2} k {TorchFloodPool.FalloffK:F3} reach {TorchFloodPool.ReachM:F1} m wrap {TorchFloodPool.Wrap:F2}");
    }

    /// Task 158: GPU sample == oracle at 16 x 9 pixels. Each pixel's ray is walked voxel by voxel on the CPU
    /// (ChunkStore) to its first non-air voxel; the voxel before it is the air cell the shader samples
    /// (voxel + normal). GPU = TorchFloodProbe (TorchFlood.hlsl), twin = TorchFloodPool.SampleLevels, oracle =
    /// LightFloodReference.Get (exact per voxel).
    private void FloodProbe(Camera cam, string tag)
    {
        var pool = TorchFloodPool.Instance;
        var store = Phase4Bootstrapper.Store;
        var vox = new List<int3>();
        cam.ResetProjectionMatrix();
        for (int j = 0; j < 9; j++)
            for (int i = 0; i < 16; i++)
            {
                Ray r = cam.ViewportPointToRay(new Vector3((i + 0.5f) / 16f, (j + 0.5f) / 9f, 0f));
                if (FloodFirstAirCell(store, r.origin * 10f, r.direction, 1200, out int3 a)) vox.Add(a);
            }
        float4[] gpu = pool.Probe(vox);
        if (gpu.Length != vox.Count) { L($"  probe ({tag}): GPU probe returned {gpu.Length} of {vox.Count} -- FAIL"); return; }
        var o = pool.Oracle;
        float maxTwin = 0f, maxOracle = 0f; int lit = 0, exact = 0, leaks = 0, dark = 0;
        for (int k = 0; k < vox.Count; k++)
        {
            float3 g3 = gpu[k].xyz;
            float3 tw = TorchFloodPool.SampleLevels(pool.Current, vox[k]);
            uint ov = o != null ? o.Get(vox[k]) : 0u;
            float3 or3 = new float3(LightFloodReference.R(ov), LightFloodReference.G(ov), LightFloodReference.B(ov));
            maxTwin = math.max(maxTwin, math.cmax(math.abs(g3 - tw)));
            maxOracle = math.max(maxOracle, math.cmax(math.abs(g3 - or3)));
            if (ov != 0) lit++; else dark++;
            if (math.all(g3 == or3)) exact++;
            if (ov == 0 && math.cmax(g3) > 0f) leaks++;
        }
        bool pass = maxTwin <= 1e-3f;
        L($"  probe ({tag}): {vox.Count} pixels' air cells ({lit} lit, {dark} dark by the oracle); GPU vs C# twin max |diff| {maxTwin:G3} levels " +
          $"-> {(pass ? "PASS" : "FAIL")}; GPU vs oracle: {exact} exact, max |diff| {maxOracle:F1} levels (of {LightFloodReference.ReachLevel(TorchFloodPool.ReachM)}), " +
          $"oracle-dark but GPU-lit {leaks}");
    }

    private static bool FloodFirstAirCell(ChunkStore store, Vector3 p, Vector3 d, int maxSteps, out int3 air)
    {
        var v = new int3((int)math.floor(p.x), (int)math.floor(p.y), (int)math.floor(p.z));
        air = v;
        int3 step = new int3(d.x >= 0 ? 1 : -1, d.y >= 0 ? 1 : -1, d.z >= 0 ? 1 : -1);
        float3 inv = new float3(1f / math.max(math.abs(d.x), 1e-6f), 1f / math.max(math.abs(d.y), 1e-6f), 1f / math.max(math.abs(d.z), 1e-6f));
        float3 tMax = new float3(
            (d.x >= 0 ? (v.x + 1 - p.x) : (p.x - v.x)) * inv.x,
            (d.y >= 0 ? (v.y + 1 - p.y) : (p.y - v.y)) * inv.y,
            (d.z >= 0 ? (v.z + 1 - p.z) : (p.z - v.z)) * inv.z);
        if (store.GetVoxel(v) != Materials.Air) return false;
        for (int i = 0; i < maxSteps; i++)
        {
            int3 prev = v;
            if (tMax.x <= tMax.y && tMax.x <= tMax.z) { v.x += step.x; tMax.x += inv.x; }
            else if (tMax.y <= tMax.z) { v.y += step.y; tMax.y += inv.y; }
            else { v.z += step.z; tMax.z += inv.z; }
            if (store.GetChunk(v >> 7) == null) return false;
            if (store.GetVoxel(v) != Materials.Air) { air = prev; return true; }
        }
        return false;
    }
}
