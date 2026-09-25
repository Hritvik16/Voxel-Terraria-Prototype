// Assets/Game/PlaygroundCapture.LodPop.cs
//
// -lodpop : IS THE TIER 0 -> 1 -> 2 SWITCH VISIBLE? The owner saw a "pop"
// between tiers in motion. A pop is the difference between what a coarse tier
// draws and what tier 0 would draw at the same pixel, revealed as the boundary
// sweeps across a surface. So this renders, at each ground pose, the SAME frame:
//   R    reference: cascade OFF, the march extended to 290 m (all tier 0)
//   L    cascade on, 8.9's bounds (64 / 128 m, the shipped default)
//   P    cascade on, PIXEL-FOOTPRINT bounds (LODConfig.FrameBoundariesM; not adopted)
//   L_t / P_t  the tier view (debug mode 6) for each, to mask by tier
// tools/lodpop-report.py measures |L - R| and |P - R| on the pixels each tier
// drew. Fluid is off and the clock pinned, so R, L and P differ only by tier.
// Diagnostic frames, not a timing measurement.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Memory;
using VoxelEngine.Mirror;
using VoxelEngine.Rendering;
using VoxelEngine.WorldGen;

public partial class PlaygroundCapture
{
    private IEnumerator LodPop()
    {
        Screen.SetResolution(1920, 1080, !HasFlag("-windowed"));
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;
        while (Phase4Bootstrapper.Store == null) yield return null;
        for (int i = 0; i < 300; i++) yield return null;

        string ts = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
        _folder = Path.Combine(Application.persistentDataPath, "LodPop", ts);
        Directory.CreateDirectory(_folder);
        VoxelEngine.Rendering.LightingDriver.ClockPaused = true;
        SunCycle.SetHours(SunCycle.GoldenHourHours);
        RaymarchFeature.ShadowQualityLevel = ArgInt("-shadowlevel", 0);
        int gate = ArgInt("-gateres", 960);
        RaymarchFeature.GateModeOverride = RaymarchFeature.GateResMode.ForcedCustom;
        RaymarchFeature.CustomGateResolution = new Vector2Int(gate, gate * 9 / 16);
        Playground.DebugHideUi = true;
        Playground.DebugSuspendFluidSim = true;
        var cam = Camera.main;
        var streamer = Phase4Bootstrapper.Streamer;

        L($"LOD TIER POP -- {ts}");
        L($"{Screen.width}x{Screen.height} {(Screen.fullScreen ? "FULLSCREEN" : "WINDOWED")} gate {gate}, fov {cam.fieldOfView:F0}, golden hour, fluid suspended");
        L("R = all tier 0 (cascade off, march to 290 m); L = legacy 64/128 m bounds; P = pixel-footprint bounds");
        L("");

        var st = ColumnSampler.CreateState(Phase4Bootstrapper.Meta);
        float maxRay0 = RaymarchFeature.MaxRayDistance;
        try
        {
            // Off the island centre (the fluid arena), on open ground.
            float cx = st.centerX * 0.1f + 220f, cz = st.centerZ * 0.1f - 140f;
            var poses = new List<(string, float h, float pitch, float yaw)>
            {
                ("ground2m_e", 2f, 4f, 90f), ("ground2m_n", 2f, 4f, 0f),
                ("hill10m_e", 10f, 8f, 90f), ("hill10m_s", 10f, 8f, 180f),
                ("low30m_w", 30f, 14f, 270f),
            };
            foreach (var (name, h, pitch, yaw) in poses)
            {
                float g = GroundMetres(in st, cx, cz);
                cam.transform.position = new Vector3(cx, g + h, cz);
                cam.transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
                yield return Frames(90);
                for (int w = 0; w < 1800 && streamer != null && streamer.PendingLoads > 0; w++) yield return null;
                yield return Frames(120);   // budgeted cascade downsample catches up

                RaymarchFeature.UseLODCascade = false;
                RaymarchFeature.MaxRayDistance = LODConfig.OUTERMOST_RANGE_M * 10f;
                yield return Frames(4);
                yield return CaptureTo($"{name}_R");
                RaymarchFeature.UseLODCascade = true;
                RaymarchFeature.MaxRayDistance = maxRay0;

                foreach (bool legacy in new[] { true, false })
                {
                    RaymarchFeature.LegacyTierBounds = legacy;
                    string tag = legacy ? "L" : "P";
                    yield return Frames(4);
                    yield return CaptureTo($"{name}_{tag}");
                    RaymarchFeature.UseDebugViewOverride = true;
                    RaymarchFeature.DebugViewOverride = RaymarchFeature.DebugMode.LODTier;
                    yield return Frames(3);
                    yield return CaptureTo($"{name}_{tag}_t");
                    RaymarchFeature.UseDebugViewOverride = false;
                    var b = RaymarchFeature.LastTierBoundsM;
                    L($"{name,-12} {tag}: bounds {b[0]:F1} / {b[1]:F1} / {b[2]:F0} m   eye {cam.transform.position}  " +
                      $"air {(CameraInAir(in st, cam.transform.position) ? "yes" : "NO!")}");
                }
                RaymarchFeature.LegacyTierBounds = true;
                // SHADOW ISOLATION (after the lost-update fix the remaining tier
                // difference is thin black streaks in the coarse bands): the same
                // R/L pair with shadows OFF (rung 3), and the sun-visibility view
                // of L. Streaks that vanish at rung 3 are shadow rays.
                int rung0 = RaymarchFeature.ShadowQualityLevel;
                RaymarchFeature.ShadowQualityLevel = 3;
                RaymarchFeature.UseLODCascade = false;
                RaymarchFeature.MaxRayDistance = LODConfig.OUTERMOST_RANGE_M * 10f;
                yield return Frames(4);
                yield return CaptureTo($"{name}_R3");
                RaymarchFeature.UseLODCascade = true;
                RaymarchFeature.MaxRayDistance = maxRay0;
                yield return Frames(4);
                yield return CaptureTo($"{name}_L3");
                RaymarchFeature.ShadowQualityLevel = rung0;
                RaymarchFeature.UseDebugViewOverride = true;
                RaymarchFeature.DebugViewOverride = RaymarchFeature.DebugMode.SunVis;
                yield return Frames(3);
                yield return CaptureTo($"{name}_L_sunvis");
                RaymarchFeature.UseDebugViewOverride = false;
                RaymarchFeature.LegacyTierBounds = false;
                MirrorDiagnostics(name);
            }
        }
        finally
        {
            st.Dispose();
            RaymarchFeature.UseLODCascade = true;
            RaymarchFeature.MaxRayDistance = maxRay0;
        }
        File.WriteAllText(Path.Combine(_folder, "lodpop.txt"), _log.ToString());
        Debug.Log("[lodpop]\n" + _log);
        yield return null;
        Application.Quit(0);
    }
    /// WHY TERRAIN IS MISSING OR WRONG IN A FRAME. The first -lodpop run showed
    /// chunk-shaped holes (sky through the ground) in all three renders, and
    /// black chunk-aligned regions only where a COARSE tier begins. Both point at
    /// the GPU mirror, not the tier distances. This separates the causes:
    ///   not resident          -> streaming/admission (LoadDeficit, map below)
    ///   resident, still dirty -> upload LAG (budgeted, expected while moving)
    ///   resident, clean, wrong-> LOST UPDATE (a real bug; validator "clean")
    private void MirrorDiagnostics(string pose)
    {
        var store = Phase4Bootstrapper.Store;
        var streamer = Phase4Bootstrapper.Streamer;
        var clip = Phase4Bootstrapper.Clipmap;
        var cas = Phase4Bootstrapper.Cascades;
        var pool = Phase4Bootstrapper.Pool;
        int3 o = clip.WindowOriginBricks / EngineConfig.CHUNK_EDGE_BRICKS;
        int3 d = clip.WindowDimsChunks;
        int3 camC = CoordMath.VoxelToChunk(CoordMath.WorldToVoxel(Camera.main.transform.position));
        var sb = new System.Text.StringBuilder();
        int resident = 0;
        for (int rz = d.z - 1; rz >= 0; rz--)
        {
            for (int rx = 0; rx < d.x; rx++)
            {
                var c = new int3(o.x + rx, 0, o.z + rz);
                bool r = store.IsResident(c);
                if (r) resident++;
                bool cam = c.x == camC.x && c.z == camC.z;
                sb.Append(cam ? '@' : (r ? (clip.IsDirty(c) ? 'd' : '#') : '.'));
            }
            sb.Append('\n');
        }
        L($"  [{pose}] window origin {o} dims {d}: {resident}/{d.x * d.z} y=0 chunks resident, load deficit {streamer.LoadDeficit()}, " +
          $"pending {streamer.PendingLoads}, in flight {streamer.InFlightLoads}, clipmap dirty {clip.DirtyCount}, " +
          $"tier1 dirty {cas.TierPool(1).DirtyRemaining}, tier2 dirty {cas.TierPool(2).DirtyRemaining}");
        var cr = ClipmapValidator.ValidateRegion(clip, pool, store, int.MaxValue, false);
        L($"  [{pose}] tier 0 clipmap: {cr.chunksChecked} chunks, {cr.chunksWithMismatch} mismatched, " +
          $"lag {cr.mismatchesInDirtyChunks}, LOST {cr.mismatchesInCleanChunks}");
        foreach (var t in CascadeValidator.ValidateAllTiers(cas, store, pool, 4096, false))
            L($"  [{pose}] tier {t.tier} cascade: {t.chunksChecked} chunks, {t.chunksWithMismatch} mismatched, " +
              $"lag {t.mismatchesInDirtyChunks}, LOST {t.mismatchesInCleanChunks}" +
              (t.samples != null && t.samples.Count > 0 ? $"; e.g. {t.samples[0]}" : ""));
        for (int tier = 1; tier < LODConfig.TIER_COUNT; tier++)
        {
            var ev = CascadeValidator.ValidateEvictedSlots(cas.TierPool(tier), store, 4096);
            L($"  [{pose}] tier {tier} evicted slots: {ev.chunksChecked} checked, {ev.entryMismatches} stale entries");
        }
        L("  residency map ('#' resident+uploaded, 'd' resident+upload pending, '.' NOT resident, '@' camera; north up):");
        L(sb.ToString());
    }
}
