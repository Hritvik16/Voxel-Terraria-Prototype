// Assets/Game/PlaygroundCapture.FallStrip.cs
//
// -fallstrip (task 94, AMENDMENT_8_16): a water, a lava and a honey block (8^3
// voxels each, 3 m apart) released from the same height in the fluid arena;
// stills at +0.5 / +1 / +2 / +4 s wall clock, first with the gravity fall OFF
// (BEFORE, the v8.6 rule), then -- after clearing the site -- ON (AFTER), in
// ONE launch. Noon, clock paused, camera looking AWAY from the sun. Each shot
// logs every block's FRONT (lowest voxel, CPU store) in metres below the
// release height. Not a measurement of time: shots wait on the wall clock, the
// fronts are read from the store at the shot. Launch:
//   BUILD=1 MODE=fluidprobe PROBEWORLD="" EXTRA="-fallstrip" ./run-tg2world.sh
using System;
using System.Collections;
using System.IO;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Memory;
using VoxelEngine.Rendering;
using VoxelEngine.Simulation;

public partial class PlaygroundCapture
{
    private IEnumerator FallStrip()
    {
        Screen.SetResolution(1920, 1080, !HasFlag("-windowed"));
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;
        var pg = FindAnyObjectByType<Playground>();
        while (Phase4Bootstrapper.Store == null) yield return null;
        yield return Frames(300);
        string ts = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
        _folder = Path.Combine(Application.persistentDataPath, "FarBand", ts + "_fluidprobe_fallstrip");
        Directory.CreateDirectory(_folder);
        LightingDriver.ClockPaused = true;
        SunCycle.SetHours(12f);
        RaymarchFeature.GateModeOverride = RaymarchFeature.GateResMode.ForcedCustom;
        RaymarchFeature.CustomGateResolution = new Vector2Int(960, 540);
        Playground.DebugHideUi = true;
        var cam = Camera.main;
        var store = Phase4Bootstrapper.Store;
        L($"FALL STRIP (task 94) -- {ts}");
        if (pg == null || pg.DebugFluid == null || pg.DebugEdits == null)
        {
            L("no Playground fluid simulation");
            File.WriteAllText(Path.Combine(_folder, "fluidprobe.txt"), _log.ToString());
            Application.Quit(2); yield break;
        }
        var sim = pg.DebugFluid;
        var sched = pg.DebugTickScheduler;
        L($"  tick {(sched == null || sched.Legacy ? "legacy" : $"{sched.RateHz:F2} Hz")}, fall table dt {sim.FallTickSeconds:F4} s, " +
          $"terminal {FluidFall.TerminalMpsFor(sim.FallTickSeconds):F2} m/s = {FluidFall.TerminalVoxelsPerTick(sim.FallTickSeconds)} voxels/tick");

        pg.SendMessage("TeleportToArena", SendMessageOptions.DontRequireReceiver);
        yield return Frames(240);
        int3 centre = pg.DebugArenaCentre;

        // Columns along the axis perpendicular to the sun's horizontal direction;
        // the camera stands on the sun's side looking away from it.
        float2 sxz = math.normalizesafe(SunCycle.Direction.xz);
        if (math.lengthsq(sxz) < 0.5f) sxz = new float2(0f, -1f);
        float2 away = -sxz;                       // camera looks this way
        float2 side = new float2(-away.y, away.x);
        byte[] mats = { Materials.Water, Materials.Lava, Materials.Honey };
        string[] names = { "water", "lava", "honey" };
        const int E = 8;                          // block edge, voxels
        var lo = new int3[3];
        int ground = int.MinValue;
        for (int i = 0; i < 3; i++)
        {
            float2 o = side * ((i - 1) * 30f);    // 3 m apart
            lo[i] = new int3(centre.x + (int)math.round(o.x) - E / 2, 0, centre.z + (int)math.round(o.y) - E / 2);
            for (int z = lo[i].z; z < lo[i].z + E; z++)
            for (int x = lo[i].x; x < lo[i].x + E; x++)
            {
                float g = StoreGroundM(store, x * 0.1f + 0.05f, z * 0.1f + 0.05f, centre.y * 0.1f + 40f);
                if (!float.IsNaN(g)) ground = math.max(ground, (int)math.round(g * 10f));
            }
        }
        if (ground == int.MinValue) ground = centre.y;
        // Release height: up to 20 m over the highest ground under the blocks, lowered
        // until every block and its fall path is air in resident chunks.
        int drop = 200;
        bool ok = false;
        for (; drop >= 60 && !ok; drop -= 10)
        {
            ok = true;
            for (int i = 0; i < 3 && ok; i++)
            {
                int3 a = new int3(lo[i].x, ground + 1, lo[i].z), b = new int3(lo[i].x + E - 1, ground + drop + E, lo[i].z + E - 1);
                if (!store.IsResident(CoordMath.VoxelToChunk(b)) || !store.IsResident(CoordMath.VoxelToChunk(a))) ok = false;
                else if (SolidIn(store, a, b) > 0) ok = false;
            }
            if (ok) break;
        }
        if (!ok) { L("no clear 6 m+ drop at the arena"); File.WriteAllText(Path.Combine(_folder, "fluidprobe.txt"), _log.ToString()); Application.Quit(2); yield break; }
        int release = ground + drop;              // bottom face of each block
        for (int i = 0; i < 3; i++) lo[i].y = release;
        L($"  arena centre {centre}, ground y {ground} ({ground * 0.1f:F1} m), release y {release} = {drop * 0.1f:F1} m above it");

        // Camera: 32 m back on the sun side, at the middle of the drop.
        float3 mid = new float3(centre.x * 0.1f, (ground + drop * 0.5f) * 0.1f, centre.z * 0.1f);
        Vector3 camPos = new Vector3(mid.x - away.x * 32f, mid.y + 2f, mid.z - away.y * 32f);
        cam.transform.position = camPos;
        cam.transform.rotation = Quaternion.LookRotation(new Vector3(mid.x, mid.y, mid.z) - camPos, Vector3.up);
        yield return Frames(120);

        float[] secs = { 0.5f, 1f, 2f, 4f };
        foreach (bool on in new[] { false, true })
        {
            string arm = on ? "after" : "before";
            sim.GravityFall = on;
            yield return Frames(30);
            int n = 0;
            for (int i = 0; i < 3; i++) n += pg.DebugEdits.SetBox(lo[i], lo[i] + (E - 1), mats[i]);
            double t0 = Time.unscaledTimeAsDouble;
            long ticks0 = sched?.IssuedTicks ?? 0;
            L("");
            L($"---- {arm.ToUpperInvariant()} (gravity fall {(on ? "ON" : "OFF")}): placed {n} voxels, frame {Time.frameCount} ----");
            yield return CaptureTo($"fall_{arm}_0s");
            foreach (float s in secs)
            {
                while (Time.unscaledTimeAsDouble - t0 < s) yield return null;
                yield return CaptureTo($"fall_{arm}_{s:0.0}s".Replace('.', 'p'));
                double t = Time.unscaledTimeAsDouble - t0;
                var sb = new System.Text.StringBuilder();
                sb.Append($"  +{s:0.0} s (wall {t:F2} s, ticks {(sched?.IssuedTicks ?? 0) - ticks0}):");
                for (int i = 0; i < 3; i++)
                {
                    int f = FrontY(store, lo[i], E, ground, mats[i]);
                    sb.Append($"  {names[i]} front {(f == int.MinValue ? "gone" : $"{(release - f) * 0.1f:F1} m")}");
                }
                L(sb.ToString());
            }
            L($"  fast-fall ops applied (whole run) {pg.DebugReadback?.FastFallOpsApplied ?? -1}, fall-ahead tiles {sim.FallAheadTilesTotal}, " +
              $"op tiles refused {sim.OpTilesRefusedTotal}");
            // Clear the site (fluid, and anything lava turned to on the ground) and let it settle.
            for (int i = 0; i < 3; i++)
                pg.DebugEdits.SetBox(new int3(lo[i].x - 12, ground + 1, lo[i].z - 12),
                                     new int3(lo[i].x + E + 11, release + E + 2, lo[i].z + E + 11), Materials.Air);
            yield return Frames(60);
        }
        File.WriteAllText(Path.Combine(_folder, "fluidprobe.txt"), _log.ToString());
        Debug.Log("[PlaygroundCapture]\n" + _log);
        yield return null;
        Application.Quit(0);
    }

    /// Lowest y holding `m` within 12 voxels of the block's footprint, between the
    /// ground and the block's top; int.MinValue if none.
    private static int FrontY(ChunkStore store, int3 lo, int e, int ground, byte m)
    {
        for (int y = ground + 1; y <= lo.y + e; y++)
        for (int z = lo.z - 12; z < lo.z + e + 12; z++)
        for (int x = lo.x - 12; x < lo.x + e + 12; x++)
            if (store.GetVoxel(new int3(x, y, z)) == m) return y;
        return int.MinValue;
    }
}
