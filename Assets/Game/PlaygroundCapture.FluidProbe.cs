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
        L($"FLUID PROBE -- {ts}  world {(tg2 ? "tg2" : "default (arena control)")}");
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
            Vector3 target = Vector3.zero; bool found = false;
            foreach (float d in new[] { 10f, 13f, 16f, 8f, 20f })
            {
                Vector3 p = eye1 + fwd * d;
                float g = StoreGroundM(store, p.x, p.z, eye1.y + 15f);
                if (float.IsNaN(g) || g > eye1.y - 0.5f) continue;
                target = new Vector3(p.x, g, p.z); found = true; break;
            }
            if (!found) { target = eye1 + fwd * 10f; target.y = eye1.y - 1.7f; L("no open ground found ahead; using a guess"); }
            Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
            Vector3 look = target + right * 1.5f + Vector3.up * 0.5f;
            cam.transform.position = eye1;
            cam.transform.rotation = Quaternion.LookRotation((look - eye1).normalized, Vector3.up);
            L($"view: eye {eye1:F1}, ground target {target:F1} ({Vector3.Distance(eye1, target):F1} m)");
            yield return NearSettle(far, sm);
            yield return Frames(90);
            hole = CoordMath.WorldToVoxel(new float3(target.x, target.y - 0.4f, target.z));
            int dug = pg.DebugEdits.SetSphere(hole, 15, Materials.Air);
            L($"dug {dug} voxels at {hole} on frame {Time.frameCount}");
            yield return Frames(66);
            wLo = hole + new int3(-3, 20, -3); wHi = hole + new int3(3, 40, 3);
            lc = CoordMath.WorldToVoxel(new float3(target.x + right.x * 3f, target.y + 1.5f, target.z + right.z * 3f));
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
            wLo = hole + new int3(-3, 20, -3); wHi = hole + new int3(3, 40, 3);
            lc = hole + new int3(30, 15, 0);
            L($"arena centre {hole}, eye {cam.transform.position:F1}");
        }

        L("");
        L("---- BEFORE placement ----");
        ProbeState(pg, store, hole, wLo, wHi, lc, true);

        int nW = pg.DebugEdits.SetBox(wLo, wHi, Materials.Water);
        int nL = pg.DebugEdits.SetBox(lc + new int3(-3, 0, -3), lc + new int3(3, 15, 3), Materials.Lava);
        int f0 = Time.frameCount;
        L("");
        L($"PLACED water {nW} voxels {wLo}..{wHi}, lava {nL} voxels at {lc}+(-3,0,-3)..(3,15,3), frame {f0}");
        L($"  same frame: wake queued {pg.DebugFluid.WakeRequestsQueuedTotal}, deferred {pg.DebugFluid.DeferredWakeRequests}, " +
          $"rej_region {pg.DebugFluid.WakeRejectedOutOfRegion}, rej_full {pg.DebugFluid.WakeRejectedFull}");
        int[] at = { 1, 2, 3, 5, 10, 20, 45, 90, 180, 225, 400 };
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
                      $"chunk dirty {clip.IsDirty(cc)}, lastUpload {clip.LastUploadEpoch(cc)} / epoch {clip.UploadEpoch}";
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
        L($"  slots: live (last async) {fl.LastLiveSlots} / cap {fl.SlotCapacity}; suspended {Playground.DebugSuspendFluidSim}");
        L($"  GPU last tick: {fl.DebugDump()}");
    }
}
