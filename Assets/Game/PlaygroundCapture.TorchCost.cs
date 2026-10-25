// Assets/Game/PlaygroundCapture.TorchCost.cs
//
// -costsplit -costpaths torch (run-cost-split.sh EXTRA=): TASK 151, owner Q95. What a wider torch
// reach costs per frame, in the cost split's interleaved F X X F harness (PlaygroundCapture.CostSplit.cs).
//   SCENE   night (-nighttod, default 0.5 h), the start pose's ground, the most open of 8 headings.
//           32 torches = MAX_ACTIVE_LIGHTS, every one selected every frame:
//             inner 16  a ring of radius 8 m centred 12 m in front of the eye (4-20 m away, all in view
//                       at pose 0), each on a 1.5 m stone post (task 143: a torch on the floor lights its
//                       floor only at grazing incidence)
//             outer 16  a ring of radius -tlouter m (default 40) round the eye. The task asked for ~half
//                       outside the tier-0 window, but in tg2 that window is 16^3 chunks (+-102 m) and the
//                       resident CPU ring ends near its edge, so a torch outside it cannot be placed (the
//                       first launch: a 108 m ring, 10 of 16 refused); the log counts how many are inside
//           Torch voxels are written through an EditService (ChunkStore.SetVoxel, the CPU writer); their
//           lights are registered here, not through the §6.6 enqueue, so each arm controls them.
//   PATH    torch_camp: the eye still, 12 deg down, yaw swept +-30 deg over 8 s (ping-pong by real time).
//   ARMS    tl_K_I_R  (e.g. tl_0.06_3.0_24) sets the three C.11 knobs on LightingConfig.Active --
//                     pointFalloffK, lightIntensity, lightRangeM -- and re-registers the 32 torches
//                     (the range is baked at LightSourceBuffer.Register); FULL = the launch's values
//           tl_none   the 32 lights unregistered (the torch voxels stay): what the torches cost at all
//           Every other arm (and FULL) restores the launch's values. Lava's aggregate reads the same three
//           knobs (LightSourceBuffer.LavaLight), so an arm changes lava too; no lava is in this scene.
// Lit px per arm: python3 -I tools/torchcost151.py <run dir>/stills (each arm's first-pass _clean.png vs tl_none's).
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Rendering;
using VoxelEngine.Streaming;

public partial class PlaygroundCapture
{
    private static List<int3> _costTorches;
    private static LightSourceBuffer _costLights;
    private static float _costBaseK = -1f, _costBaseI, _costBaseR;

    /// Task 151: places the 32 torches and returns the torch_camp path (empty list if anything is missing).
    private IEnumerator CostTorchScene(Vector3 centre, float hours, FarField far, StreamManager sm, Camera cam, List<ClipPath> outPaths)
    {
        var store = Phase4Bootstrapper.Store;
        var clip = Phase4Bootstrapper.Clipmap;
        var driver = FindAnyObjectByType<LightingDriver>();
        if (store == null || clip == null || driver == null || driver.Lights == null)
        { L("  -costpaths torch: no store / clipmap / LightingDriver -- skipped"); yield break; }

        // the most open of 8 level headings (the inner ring wants flat-ish ground in front)
        var eye = new Vector3(centre.x, WalkEyeY(centre.x, centre.z, centre.y), centre.z);
        float bestYaw = 0f, best = -1f;
        for (int k = 0; k < 8; k++)
        {
            float yaw = k * math.PI / 4f;
            float d = CostFreeDistanceM(eye, new float2(math.sin(yaw), math.cos(yaw)), 400f);
            if (d > best) { best = d; bestYaw = yaw; }
        }
        var fwd = new Vector3(math.sin(bestYaw), 0f, math.cos(bestYaw));
        Phase4Bootstrapper.DebugStreamCentre = eye;
        cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(fwd, Vector3.up));
        yield return NearSettle(far, sm);

        var edits = new EditService();
        edits.AttachWorld(store, store, store, clip);
        int3 dims = clip.WindowDimsChunks, org = clip.WindowOrigin;
        const int chunkV = EngineConfig.CHUNK_EDGE_BRICKS * EngineConfig.BRICK_EDGE;
        int3 boxLo = org * chunkV, boxHi = (org + dims) * chunkV;
        float halfX = dims.x * chunkV * 0.05f;
        const int lift = 15;                                             // 1.5 m post, as task 143
        var torches = new List<int3>(32);
        int refused = 0, inBox = 0;
        void Place(float xM, float zM)
        {
            int x = (int)math.floor(xM * 10f), z = (int)math.floor(zM * 10f);
            int y = (int)math.floor((WalkEyeY(xM, zM, eye.y) + 10f) * 10f);
            for (int i = 0; i < 600 && store.GetVoxel(new int3(x, y, z)) == Materials.Air; i++) y--;   // first solid going down
            var t = new int3(x, y + 1 + lift, z);
            bool ok = true;
            for (int i = 1; i <= lift; i++) { var p = new int3(x, y + i, z); if (store.GetVoxel(p) == Materials.Air) ok &= edits.TrySetVoxel(p, Materials.Stone); }
            ok &= store.GetVoxel(t) == Materials.Torch || edits.TrySetVoxel(t, Materials.Torch);
            if (!ok) { refused++; return; }
            torches.Add(t);
            if (math.all(t >= boxLo) && math.all(t < boxHi)) inBox++;
        }
        Vector3 c = eye + fwd * 12f;
        for (int i = 0; i < 16; i++) { float a = i * math.PI / 8f; Place(c.x + 8f * math.cos(a), c.z + 8f * math.sin(a)); }
        float rOut = ArgFloat("-tlouter", 40f);
        for (int i = 0; i < 16; i++) { float a = (i + 0.5f) * math.PI / 8f; Place(eye.x + rOut * math.cos(a), eye.z + rOut * math.sin(a)); }
        yield return Frames(30);

        var cfg = LightingConfig.Active;
        _costTorches = torches; _costLights = driver.Lights;
        _costBaseK = cfg.pointFalloffK; _costBaseI = cfg.lightIntensity; _costBaseR = cfg.lightRangeM;
        CostTorchApply("full");
        L($"  torch scene (task 151): {hours:F2} h; eye {eye:F1}, heading yaw {math.degrees(bestYaw):F0} (free {best:F0} m); " +
          $"tier-0 window {dims} chunks from {org} (half x {halfX:F1} m); torches placed {torches.Count} (refused {refused}), " +
          $"inside the window {inBox}; inner ring r 8 m at 12 m ahead, outer ring r {rOut:F1} m; lights registered {driver.Lights.RegisteredCount}; " +
          $"launch knobs k {_costBaseK:F3} intensity {_costBaseI:F2} range {_costBaseR:F1} m; torch shadow rays {cfg.torchShadowRays}");

        int n = (int)(RatePathSeconds * 30f) + 1;
        var path = new ClipPath { name = "torch_camp", hours = hours, moving = false, lead = 0, pos = new Vector3[n], rot = new Quaternion[n] };
        for (int f = 0; f < n; f++)
        {
            float yaw = math.degrees(bestYaw) + 30f * math.sin(math.PI * 2f * f / (n - 1));   // 0 -> +30 -> 0 -> -30 -> 0
            path.pos[f] = eye; path.rot[f] = Quaternion.Euler(12f, yaw, 0f);
        }
        path.note = $"still eye {eye:F1}, pitch 12 down, yaw {math.degrees(bestYaw):F0} +-30 deg over {RatePathSeconds:F0} s; 32 torches (see the line above)";
        outPaths.Add(path);
    }

    /// Task 151 (owner: "keep lava's aggregate light"): after the timed runs, a 3 x 3 m lava slab 0.3 m deep
    /// on the ground 8 m behind the eye (fluid suspended: it does not flow), the 32 torch lights off, one
    /// still per arm's knobs (lava's aggregate reads the same three) -> stills/lava_<arm>.png.
    private IEnumerator CostTorchLava(Camera cam, string stills, string[] arms)
    {
        var store = Phase4Bootstrapper.Store;
        var driver = FindAnyObjectByType<LightingDriver>();
        if (_costTorches == null || store == null || driver == null) yield break;
        Playground.DebugSuspendFluidSim = true;
        Vector3 eye = cam.transform.position;
        var back = -(cam.transform.rotation * Vector3.forward); back.y = 0f; back.Normalize();
        Vector3 c = eye + back * 8f;
        var edits = new EditService();
        edits.AttachWorld(store, store, store, Phase4Bootstrapper.Clipmap);
        driver.Attach(edits);                                   // lava registers into its tile's aggregate (§6.6)
        int cx = (int)math.floor(c.x * 10f), cz = (int)math.floor(c.z * 10f), placed = 0;
        for (int dx = -15; dx < 15; dx++)
            for (int dz = -15; dz < 15; dz++)
            {
                int x = cx + dx, z = cz + dz;
                int y = (int)math.floor((WalkEyeY(x * 0.1f, z * 0.1f, eye.y) + 10f) * 10f);
                for (int i = 0; i < 600 && store.GetVoxel(new int3(x, y, z)) == Materials.Air; i++) y--;
                for (int h = 1; h <= 3; h++) if (edits.TrySetVoxel(new int3(x, y + h, z), Materials.Lava)) placed++;
            }
        foreach (var t in _costTorches) _costLights.Unregister(t);
        var rot = Quaternion.LookRotation((c - eye) + Vector3.down * 2.5f, Vector3.up);
        cam.transform.SetPositionAndRotation(eye, rot);
        Phase4Bootstrapper.DebugStreamCentre = eye;
        yield return Frames(60);
        L($"  lava stills (task 151): slab at {c:F1}, {placed} lava voxels written, torch lights off; lights registered {_costLights.RegisteredCount}");
        var cfg = LightingConfig.Active;
        foreach (string arm in arms)
        {
            CostTorchApply(arm);
            foreach (var t in _costTorches) _costLights.Unregister(t);
            Playground.DebugHideUi = true; Minimap.Visible = false;
            yield return Frames(40);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(stills, $"lava_{arm}.png")); yield return Frames(3);
            L($"    lava_{arm}: k {cfg.pointFalloffK:F3} intensity {cfg.lightIntensity:F2} range {cfg.lightRangeM:F1} m " +
              $"(lava range x{cfg.lavaLightRangeScale:F2}, intensity x{cfg.lavaLightIntensity:F2})");
        }
        CostTorchApply("full");
    }

    /// Task 151: the arm's three knobs (tl_K_I_R), no torch lights (tl_none) or the launch's values.
    private static void CostTorchApply(string arm)
    {
        if (_costTorches == null || _costLights == null || _costBaseK < 0f) return;
        var cfg = LightingConfig.Active;
        cfg.pointFalloffK = _costBaseK; cfg.lightIntensity = _costBaseI; cfg.lightRangeM = _costBaseR;
        if (arm.StartsWith("tl_") && arm != "tl_none")
        {
            var p = arm.Substring(3).Split('_');
            if (p.Length == 3)
            {
                cfg.pointFalloffK = float.Parse(p[0], CultureInfo.InvariantCulture);
                cfg.lightIntensity = float.Parse(p[1], CultureInfo.InvariantCulture);
                cfg.lightRangeM = float.Parse(p[2], CultureInfo.InvariantCulture);
            }
        }
        foreach (var t in _costTorches) _costLights.Unregister(t);
        if (arm != "tl_none") foreach (var t in _costTorches) _costLights.Register(t, Materials.Torch);
    }
}
