// Assets/Game/PlaygroundCapture.StreamProbe.cs
//
// -streamprobe (task 10, 2026-10-02): THE STREAMING BASELINE on the tg2 world.
// Owner: "track how long it takes for a chunk to load on the cpu, travel to
// the gpu ... I can see the lod changing right around us as the camera moves."
// Measurement only. It turns on StreamTrace (opt-in, observe-only) and reports
// p50 / p95 / max per stage:
//   1. worker: generate + cascade downsample ms per chunk, chunks/s, workers
//   2. main thread: admitting one completion (transfer copy, cascade writes)
//   3. admission -> tier-0 clipmap upload; bytes per frame vs §0.2's 3 MB cap
//      and the upload pass's phases (format conversion vs copies)
//   4. request -> on the GPU, the distance it happens at, and LATE chunks: the
//      camera was within the 64 m full-detail ring before the chunk arrived
//   5. load deficit over time
//   6. shots of the same pose fully loaded (still) and mid-motion, each in
//      beauty, the LOD-tier view (6) and the band view (9)
//   7. (task 16b) measured capacity vs the WORST-CASE requirement
//      (StreamingRequirement): REQUIRED / CAPACITY -> MET / NOT MET. This
//      replaced B1's "no more than 5% worse than v3" comparison.
//
// -streammotion walk : the MODE=walk circle (-orbitradius, default 150 m) at
//                      1.6 m eye, -movespeed default 5 m/s.
// -streammotion fly  : a straight ping-pong line through the spawn (-flyleg,
//                      default 1200 m), heading chosen among 8: at least 90%
//                      land, then the least relief (max - min ground); eye
//                      -flyclear (12 m) above the highest ground within
//                      -flywin (24 m) along the line, -movespeed default 56 m/s
//                      (the demo's Shift fly; 14 is its normal speed). Never
//                      leaves the island the way -stutterprobe does
//                      (KNOWN_ISSUES). First try (eye 20 m over the highest
//                      ground within 80 m, the most-land heading) crossed a
//                      ridge line 717-915 m high: above every valley the
//                      89.6 m band sat in air and the frame was far band only.
// Frame-time figures are this process's own Time.unscaledDeltaTime; compare
// arms only as repeated, separate launches (KNOWN_ISSUES: a short cooled arm
// is not FPS evidence).
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Rendering;
using VoxelEngine.Streaming;

public partial class PlaygroundCapture
{
    private static bool GroundTopM(float xM, float zM, out float topM)
    {
        var src = Phase4Bootstrapper.Streamer.TerrainGenSource;
        var spec = src.Spec; var view = src.Map.View; var o = src.O;
        var col = TerrainGen.ShapeField.Sample(in spec, in view, in o, xM * 10f, zM * 10f, 1);
        int top = math.max(math.max(col.height, col.water), spec.seaLevelY);
        topM = (top + 1) * 0.1f;
        return col.height > spec.seaLevelY && col.water <= col.height;
    }

    private IEnumerator StreamProbe()
    {
        Screen.SetResolution(1920, 1080, !HasFlag("-windowed"));
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;
        var pg = FindAnyObjectByType<Playground>();
        while (Phase4Bootstrapper.Store == null) yield return null;
        int gate = ArgInt("-gateres", 0);
        if (gate > 0)
        {
            RaymarchFeature.GateModeOverride = RaymarchFeature.GateResMode.ForcedCustom;
            RaymarchFeature.CustomGateResolution = new Vector2Int(gate, gate * 9 / 16);
        }
        RaymarchFeature.ShadowQualityLevel = ArgInt("-shadowlevel", 0);
        Playground.DebugHideUi = true;
        for (int i = 0; i < 300; i++) yield return null;
        if (pg != null) { pg.SendMessage("TeleportToArena", SendMessageOptions.DontRequireReceiver); yield return null; }
        SunCycle.SetHours(SunCycle.GoldenHourHours);
        VoxelEngine.Rendering.LightingDriver.ClockPaused = true;

        string motion = ArgValue("-streammotion") ?? "fly";
        bool fly = motion != "walk";
        float speed = ArgFloat("-movespeed", fly ? 56f : 5f);
        int seconds = ArgInt("-seconds", fly ? 150 : 180);
        string tag = ArgValue("-tag") ?? ("stream" + motion);
        string ts = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
        _folder = Path.Combine(Application.persistentDataPath, "StreamProbe", $"{ts}_{tag}");
        Directory.CreateDirectory(_folder);
        var cam = Camera.main;
        var streamer = Phase4Bootstrapper.Streamer;
        var far = FarField.Active;
        if (streamer == null || streamer.TerrainGenSource == null)
        {
            L("STREAM PROBE needs -world tg2 (a TerrainGen source); none -- nothing measured.");
            File.WriteAllText(Path.Combine(_folder, "streamprobe.txt"), _log.ToString());
            Application.Quit(1); yield break;
        }

        // ---- the path, as a pure function of distance travelled s ----
        Vector3 spawn = cam.transform.position;
        float radiusM = ArgFloat("-orbitradius", 150f);
        float leg = ArgFloat("-flyleg", 1200f);
        float clear = ArgFloat("-flyclear", 12f);
        float flyWin = ArgFloat("-flywin", 24f);
        float relief = 0f;
        float pitch = ArgFloat("-flypitch", fly ? 8f : 0f);
        Vector3 dir = Vector3.right; float landFrac = 0f;
        float[] eyeTable = null; const float STEP = 4f;
        if (fly)
        {
            float bestRelief = float.MaxValue, bestLand = -1f;
            for (int h = 0; h < 8; h++)
            {
                float a = h * Mathf.PI / 4f;
                var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                int land = 0, n = 0; float lo = float.MaxValue, hi = float.MinValue;
                for (float x = -leg * 0.5f; x <= leg * 0.5f; x += 10f, n++)
                {
                    if (GroundTopM(spawn.x + d.x * x, spawn.z + d.z * x, out float top)) land++;
                    lo = Math.Min(lo, top); hi = Math.Max(hi, top);
                }
                float f = (float)land / n;
                // At least 90% land, then the flattest; else the most land.
                bool better = (f >= 0.9f && (bestLand < 0.9f || hi - lo < bestRelief)) || (bestLand < 0.9f && f > bestLand);
                if (better) { bestLand = f; bestRelief = hi - lo; dir = d; }
            }
            landFrac = bestLand; relief = bestRelief;
            int cells = (int)(leg / STEP) + 1;
            var ground = new float[cells];
            Vector3 a0 = spawn - dir * (leg * 0.5f);
            for (int i = 0; i < cells; i++) { GroundTopM(a0.x + dir.x * i * STEP, a0.z + dir.z * i * STEP, out ground[i]); }
            eyeTable = new float[cells];
            int win = (int)(flyWin / STEP);
            for (int i = 0; i < cells; i++)
            {
                float m = float.MinValue;
                for (int j = Math.Max(0, i - win); j <= Math.Min(cells - 1, i + win); j++) m = Math.Max(m, ground[j]);
                eyeTable[i] = m + clear;
            }
        }
        float period = fly ? 2f * leg : 2f * Mathf.PI * radiusM;
        float s0 = fly ? leg * 0.5f : 0f;
        void Pose(float s)
        {
            if (fly)
            {
                float ph = Mathf.Repeat(s, period);
                float x = ph < leg ? ph : period - ph;
                Vector3 a0 = spawn - dir * (leg * 0.5f);
                Vector3 p = a0 + dir * x;
                float fi = Mathf.Clamp(x / STEP, 0f, eyeTable.Length - 1.001f);
                int i0 = (int)fi;
                p.y = Mathf.Lerp(eyeTable[i0], eyeTable[i0 + 1], fi - i0);
                cam.transform.position = p;
                cam.transform.rotation = Quaternion.LookRotation(ph < leg ? dir : -dir, Vector3.up) * Quaternion.Euler(pitch, 0f, 0f);
            }
            else
            {
                float ang = s / radiusM;
                Vector3 p = spawn + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * radiusM;
                p.y = WalkEyeY(p.x, p.z, p.y);
                cam.transform.position = p;
                var tangent = new Vector3(-Mathf.Sin(ang), 0f, Mathf.Cos(ang));
                cam.transform.rotation = Quaternion.LookRotation(tangent, Vector3.up) * Quaternion.Euler(pitch, 0f, 0f);
            }
        }

        // ---- settle at the shot pose, fully loaded ----
        Pose(s0);
        for (int i = 0; i < 600; i++) yield return null;
        // Time-based: a fresh 3087-chunk band takes tens of seconds to fill
        // (the first try's 3600-frame cap ran out at deficit 207). The fill
        // time is itself reported: it is how long a teleport takes to load.
        int settleFrames = 0;
        float settleStart = Time.realtimeSinceStartup, settleCap = ArgFloat("-settlecap", 240f);
        while (Time.realtimeSinceStartup - settleStart < settleCap && (streamer.PendingLoads > 0 || streamer.InFlightLoads > 0 ||
               streamer.LoadDeficit() > 0 || streamer.ClipmapDirtyRemaining > 0 || (far != null && far.Busy)))
        { settleFrames++; yield return null; }
        float settleSeconds = Time.realtimeSinceStartup - settleStart;
        int settleDeficit = streamer.LoadDeficit();

        L($"STREAM PROBE -- {tag} -- {ts}");
        L($"device {SystemInfo.graphicsDeviceName}  {Screen.width}x{Screen.height}  {(Screen.fullScreen ? "FULLSCREEN" : "WINDOWED")}  " +
          $"{(Debug.isDebugBuild ? "DEVELOPMENT" : "RELEASE")}  preset {(HasFlag("-renderpreset") ? RenderPreset.Current.ToString() : "as shipped")}  " +
          $"gate {gate}  shadow rung {RaymarchFeature.ShadowQualityLevel}  HUD OFF  fluid not opened  clock {SunCycle.ClockString()}");
        L($"streamer: {streamer.WorkerCount} chunk-gen workers ({(StreamManager.GenWorkersOverride > 0 ? "-genworkers override" : "default")}, ProcessorCount {System.Environment.ProcessorCount}), load radius {streamer.LoadRadiusChunks} chunks ({streamer.LoadRadiusChunks * 12.8f:F1} m), " +
          $"evict {streamer.EvictRadiusChunks}, band layers {StreamManager.V_LOAD_LAYERS * 2 + 1}, admit cap {EngineConfig.MAX_CHUNK_LOADS_PER_FRAME}/frame, " +
          $"clipmap cap {EngineConfig.MAX_CLIPMAP_CHUNKS_PER_FRAME} chunks / {EngineConfig.MAX_CLIPMAP_UPLOAD_BYTES_PER_FRAME / 1048576.0:F1} MB per frame (exempt radius {EngineConfig.UPLOAD_EXEMPT_RADIUS_CHUNKS})");
        L(fly ? $"motion FLY: straight ping-pong {leg:F0} m through spawn ({spawn.x:F0}, {spawn.z:F0}) heading ({dir.x:F2}, {dir.z:F2}), land {landFrac * 100f:F0}% of the line, relief {relief:F0} m, " +
                $"eye {clear:F0} m over the highest ground within {flyWin:F0} m, {speed:F0} m/s, pitch {pitch:F0} deg down, {seconds} s"
              : $"motion WALK: circle r {radiusM:F0} m round spawn ({spawn.x:F0}, {spawn.z:F0}), eye 1.6 m over ground, {speed:F0} m/s, {seconds} s");
        L($"settle at the shot pose (fill of a fresh band): {settleSeconds:F1} s after the first 600 frames, {settleFrames} frames, deficit {settleDeficit}, pending {streamer.PendingLoads}, dirty {streamer.ClipmapDirtyRemaining}");
        L($"LOD hand-offs (code): tier0 0.1 m <= {LODConfig.TIER_OUTER_RANGE_M[0]:F0} m, tier1 0.2 m <= {LODConfig.TIER_OUTER_RANGE_M[1]:F0} m, " +
          $"tier2 0.4 m <= {LODConfig.TIER_OUTER_RANGE_M[2]:F0} m but voxel DATA only inside the load square (+-{streamer.LoadRadiusChunks * 12.8f:F0} m XZ, " +
          $"{(StreamManager.V_LOAD_LAYERS * 2 + 1) * 12.8f:F1} m tall band); far band F0..F4 texels 0.8/1.6/3.2/6.4/12.8 m, rings ~{FarField.USE_RADIUS_TEXELS * 0.8f:F0} m x 2^L");

        {
            // Pose diagnostic: what the CPU store holds straight below the eye.
            var store = Phase4Bootstrapper.Store;
            float3 e = cam.transform.position;
            int3 v = new int3((int)math.floor(e.x * 10f), (int)math.floor(e.y * 10f), (int)math.floor(e.z * 10f));
            int firstSolid = int.MinValue;
            for (int y = v.y; y > v.y - 2000; y--)
                if (store.GetVoxel(new int3(v.x, y, v.z)) != Materials.Air) { firstSolid = y; break; }
            GroundTopM(e.x, e.z, out float gTop);
            streamer.LayerRange(out int blo, out int bhi);
            L($"shot pose: eye {e.x:F1} {e.y:F1} {e.z:F1} m, voxel at eye {store.GetVoxel(v)}, ground top (map) {gTop:F1} m, " +
              $"first solid voxel below the eye (CPU store) {(firstSolid == int.MinValue ? "none within 200 m" : (firstSolid * 0.1f).ToString("F1") + " m")}, " +
              $"band layers {blo}..{bhi} ({blo * 12.8f:F1}-{(bhi + 1) * 12.8f:F1} m), mirror origin {Phase4Bootstrapper.Clipmap.WindowOrigin}");
        }
        // ISOLATED generation cost: the workers are idle now (deficit 0), so
        // regenerate up to 8 solid chunks of this band on the MAIN thread into a
        // private scratch pool (nothing shared is touched; the result is
        // thrown away). Separates "generation is slow" from "slow under the
        // contention of 7 workers + main + render threads" in stage 1.
        {
            var iso = new List<float>();
            var probeCoords = new List<int3>();
            foreach (var ch in Phase4Bootstrapper.Store.ResidentChunks())
                if (!ch.isUniform && probeCoords.Count < 8) probeCoords.Add(ch.coord);
            var isoPool = new VoxelEngine.Memory.BrickDataPool(EngineConfig.BRICKS_PER_CHUNK);
            try
            {
                foreach (var c in probeCoords)
                {
                    var warm = new VoxelEngine.Memory.Chunk();
                    streamer.TerrainGenSource.Generate(c, warm, new VoxelEngine.Memory.ChunkHandleAllocator(2), isoPool, null);
                    isoPool.Reset();
                    long t0 = StreamTrace.Now;
                    var ch = new VoxelEngine.Memory.Chunk();
                    streamer.TerrainGenSource.Generate(c, ch, new VoxelEngine.Memory.ChunkHandleAllocator(2), isoPool, null);
                    iso.Add((float)StreamTrace.Ms(StreamTrace.Now - t0));
                    isoPool.Reset();
                    yield return null;
                }
            }
            finally { isoPool.Dispose(); }
            iso.Sort();
            L($"isolated generate, main thread, workers idle: {iso.Count} solid chunks of this band, p50 {Pct(iso, 0.5):F1} ms  max {(iso.Count > 0 ? iso[iso.Count - 1] : 0):F1} ms");
        }

        yield return ShotSet("still", "fully loaded, camera still");

        // ---- measure ----
        StreamTrace.Reset();
        // Task 25: GC activity over the measured walk (start/end counters).
        int[] gcN0 = { System.GC.CollectionCount(0), System.GC.CollectionCount(1), System.GC.CollectionCount(2) };
        long monoUsed0 = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong(), monoHeap0 = UnityEngine.Profiling.Profiler.GetMonoHeapSizeLong();
        StreamTrace.Enabled = true;
        int cap = seconds * 300;
        var rows = new StringBuilder(cap * 120);
        rows.Append("frame\tt\tms\tstreamMs\tadmitted\tevicted\tpending\tinflight\tuploadBytes\tchunksUp\tdeferred\tdirtyLeft\t" +
                    "sortMs\tstagingMs\tclipSetMs\tmipMs\tpackMs\tbrickSetMs\tpackUpMs\tcascadeMs\ttransferMs\tdrainMs\t" +
                    "missingGpu\tmissingIn64\tdeficit\tdeficitIn128\tband\tcamY\n");
        var ms = new List<float>(cap);
        var upBytes = new List<float>(cap); var upChunks = new List<float>(cap);
        var phStage = new List<float>(); var phClip = new List<float>(); var phMip = new List<float>(); var phPack = new List<float>();
        var phBrick = new List<float>(); var phPackUp = new List<float>(); var phSort = new List<float>(); var phCasc = new List<float>(); var phTransfer = new List<float>();
        var phUploadTotal = new List<float>();
        var deficits = new List<float>(); var deficits128 = new List<float>(); var missIn64 = new List<float>(); var missGpu = new List<float>();
        int overBudget = 0, frames = 0, bandSwitches0 = streamer.BandSwitchesTotal;
        long admitted0 = streamer.ChunksAdmittedTotal, discarded0 = streamer.DiscardedOnArrivalTotal;
        var winMs = new List<float>(); var winDef = new List<int>(); var winAdm = new List<int>();
        var windows = new StringBuilder();
        float winStart = 0f; long winAdm0 = admitted0;
        float s = s0;
        float runStart = Time.realtimeSinceStartup;
        int lastDef = 0, lastDef128 = 0;
        Pose(s);
        yield return null;
        while (Time.realtimeSinceStartup - runStart < seconds)
        {
            s += speed * Time.unscaledDeltaTime;
            Pose(s);
            yield return null;
            float f = Time.unscaledDeltaTime * 1000f;
            float t = Time.realtimeSinceStartup - runStart;
            ms.Add(f);
            var us = streamer.LastUploadStats;
            upBytes.Add(streamer.LastUploadBytes); upChunks.Add(us.chunksUploaded);
            if (streamer.LastUploadBytes > EngineConfig.MAX_CLIPMAP_UPLOAD_BYTES_PER_FRAME) overBudget++;
            if (us.chunksUploaded > 0)
            {
                phStage.Add((float)us.stagingMs); phClip.Add((float)us.clipmapSetMs); phMip.Add((float)us.mipRebuildMs);
                phPack.Add((float)us.packRegionMs); phBrick.Add((float)us.brickSetMs); phPackUp.Add((float)us.packUploadMs);
                phSort.Add((float)us.sortMs); phUploadTotal.Add((float)(streamer.LastUploadMs - streamer.LastCascadeMs));
            }
            phCasc.Add((float)streamer.LastCascadeMs);
            if (streamer.LastAdmitted > 0) phTransfer.Add((float)streamer.LastTransferMs);
            float3 cp = cam.transform.position;
            StreamTrace.MissingNow(cp, out int open, out int in64);
            missGpu.Add(open); missIn64.Add(in64);
            if ((frames % 10) == 0)
            {
                streamer.LoadDeficitSplit(out int i128, out int o128);
                lastDef = i128 + o128; lastDef128 = i128;
                deficits.Add(lastDef); deficits128.Add(lastDef128);
                winDef.Add(lastDef);
            }
            winMs.Add(f);
            rows.Append(frames).Append('\t').Append(t.ToString("F3")).Append('\t').Append(f.ToString("F3")).Append('\t')
                .Append(FrameGapProbe.LastStreamerMs.ToString("F3")).Append('\t').Append(streamer.LastAdmitted).Append('\t')
                .Append(streamer.LastEvicted).Append('\t').Append(streamer.PendingLoads).Append('\t').Append(streamer.InFlightLoads).Append('\t')
                .Append(streamer.LastUploadBytes).Append('\t').Append(us.chunksUploaded).Append('\t').Append(us.chunksDeferred).Append('\t')
                .Append(us.dirtyRemaining).Append('\t').Append(us.sortMs.ToString("F3")).Append('\t').Append(us.stagingMs.ToString("F3")).Append('\t')
                .Append(us.clipmapSetMs.ToString("F3")).Append('\t').Append(us.mipRebuildMs.ToString("F3")).Append('\t')
                .Append(us.packRegionMs.ToString("F3")).Append('\t').Append(us.brickSetMs.ToString("F3")).Append('\t')
                .Append(us.packUploadMs.ToString("F3")).Append('\t').Append(streamer.LastCascadeMs.ToString("F3")).Append('\t')
                .Append(streamer.LastTransferMs.ToString("F3")).Append('\t').Append(streamer.LastDrainCompletionsMs.ToString("F3")).Append('\t')
                .Append(open).Append('\t').Append(in64).Append('\t').Append(lastDef).Append('\t').Append(lastDef128).Append('\t')
                .Append(streamer.StreamLayer).Append('\t').Append(cp.y.ToString("F1")).Append('\n');
            frames++;
            if (t - winStart >= 10f)
            {
                winMs.Sort();
                int dmax = 0; double dsum = 0; foreach (int d in winDef) { dmax = Math.Max(dmax, d); dsum += d; }
                long adm = streamer.ChunksAdmittedTotal - winAdm0;
                windows.AppendLine($"  {winStart,5:F0}-{t,4:F0} s  frame p50 {Pct(winMs, 0.5),5:F1} p95 {Pct(winMs, 0.95),5:F1} max {winMs[winMs.Count - 1],6:F1} ms   " +
                                   $"deficit mean {(winDef.Count > 0 ? dsum / winDef.Count : 0),6:F1} max {dmax,5}   admitted {adm / (t - winStart),6:F1}/s");
                winMs.Clear(); winDef.Clear(); winStart = t; winAdm0 = streamer.ChunksAdmittedTotal;
            }
        }
        float runSeconds = Time.realtimeSinceStartup - runStart;
        long admittedRun = streamer.ChunksAdmittedTotal - admitted0;

        // ---- the same pose, mid-motion: keep moving until the path next
        // crosses it heading the same way, then shoot three consecutive frames.
        float next = s0 + period * Mathf.Ceil((s - s0) / period + 1e-4f);
        while (s + speed * Time.unscaledDeltaTime < next) { s += speed * Time.unscaledDeltaTime; Pose(s); yield return null; }
        s = next;
        int missing64AtShot; { Pose(s); StreamTrace.MissingNow(cam.transform.position, out _, out missing64AtShot); }
        int defAtShot = streamer.LoadDeficit();
        var modes = new[] { (-1, "beauty"), (6, "tier"), (9, "band") };
        foreach (var (mode, name) in modes)
        {
            RaymarchFeature.UseDebugViewOverride = mode >= 0;
            if (mode >= 0) RaymarchFeature.DebugViewOverride = (RaymarchFeature.DebugMode)mode;
            Pose(s);
            yield return new WaitForEndOfFrame();
            Texture2D tex = ScreenCapture.CaptureScreenshotAsTexture();
            try { File.WriteAllBytes(Path.Combine(_folder, $"moving_{name}.png"), tex.EncodeToPNG()); }
            finally { Destroy(tex); }
            yield return null;
            s += speed * Time.unscaledDeltaTime;
        }
        RaymarchFeature.UseDebugViewOverride = false;
        StreamTrace.Enabled = false;
        int[] gcN1 = { System.GC.CollectionCount(0), System.GC.CollectionCount(1), System.GC.CollectionCount(2) };
        long monoUsed1 = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong(), monoHeap1 = UnityEngine.Profiling.Profiler.GetMonoHeapSizeLong();
        L($"moving_beauty/tier/band.png -- the still pose, mid-motion at {speed:F0} m/s (3 consecutive frames, camera moving); " +
          $"deficit {defAtShot}, chunks missing on the GPU inside 64 m {missing64AtShot}");

        // ---- report ----
        var done = StreamTrace.Done;
        var gen = new List<float>(); var genSolid = new List<float>(); var down = new List<float>(); var admitMs = new List<float>();
        var transfer = new List<float>(); var casc = new List<float>(); var reqDisp = new List<float>(); var jobWait = new List<float>();
        var complWait = new List<float>(); var admUp = new List<float>(); var admUpF = new List<float>(); var reqUp = new List<float>();
        var reqUpF = new List<float>(); var distReq = new List<float>(); var distUp = new List<float>(); var late = new List<float>(); var lateF = new List<float>();
        var genDownSolid = new List<float>();
        int uniformN = 0;
        double workerMsSum = 0;
        foreach (var e in done)
        {
            gen.Add((float)e.genMs); down.Add((float)e.downMs); if (!e.uniform) { genSolid.Add((float)e.genMs); genDownSolid.Add((float)(e.genMs + e.downMs)); } else uniformN++;
            workerMsSum += e.genMs + e.downMs;
            admitMs.Add((float)e.admitMs); transfer.Add((float)e.transferMs); casc.Add((float)e.cascadeMs);
            reqDisp.Add((float)StreamTrace.Ms(e.tDispatch - e.tRequest));
            long workStart = e.tWorkerDone - (long)((e.genMs + e.downMs) * System.Diagnostics.Stopwatch.Frequency / 1000.0);
            jobWait.Add((float)StreamTrace.Ms(workStart - e.tDispatch));
            complWait.Add((float)StreamTrace.Ms(e.tAdmit - e.tWorkerDone));
            admUp.Add((float)StreamTrace.Ms(e.tUpload - e.tAdmit)); admUpF.Add(e.fUpload - e.fAdmit);
            reqUp.Add((float)StreamTrace.Ms(e.tUpload - e.tRequest)); reqUpF.Add(e.fUpload - e.fRequest);
            distReq.Add(e.distRequestM); distUp.Add(e.distUploadM);
            if (e.fNear >= 0) { late.Add((float)StreamTrace.Ms(e.tUpload - e.tNear)); lateF.Add(e.fUpload - e.fNear); }
        }
        string Row(string name, List<float> v, string unit = "ms", string fmt = "F2")
        {
            v.Sort();
            return $"  {name,-44} n {v.Count,6}  p50 {Pct(v, 0.5).ToString(fmt),8}  p95 {Pct(v, 0.95).ToString(fmt),8}  max {(v.Count > 0 ? v[v.Count - 1] : 0).ToString(fmt),8} {unit}";
        }
        L("");
        L(Row("frame time", ms) + $"   (>33 ms: {CountOver(ms, 33f)} of {ms.Count}; {runSeconds:F0} s)");
        L("");
        L($"1. CPU GENERATION (worker threads: {streamer.WorkerCount}) -- {done.Count} chunks reached the GPU ({uniformN} uniform)");
        L(Row("generate (GenerateBaseline), all", gen));
        L(Row("generate, non-uniform chunks only", genSolid));
        L(Row("cascade downsample on the worker", down));
        {
            // Task 25: wall vs the worker thread's own CPU time, non-uniform chunks.
            var cpuG = new List<float>(); var ratio = new List<float>(); var stall = new List<float>();
            int gcHit = 0, nonUni = 0; var wallGcHit = new List<float>(); var wallGcNone = new List<float>();
            foreach (var e in done)
            {
                if (e.uniform) continue;
                nonUni++;
                if (e.gcDuringWork > 0) { gcHit++; wallGcHit.Add((float)e.genMs); } else wallGcNone.Add((float)e.genMs);
                if (e.genCpuMs < 0) continue;
                cpuG.Add((float)e.genCpuMs); stall.Add((float)(e.genMs - e.genCpuMs));
                if (e.genCpuMs > 0) ratio.Add((float)(e.genMs / e.genCpuMs));
            }
            L(Row("generate, non-uniform: worker THREAD CPU", cpuG) + (cpuG.Count == 0 ? "   (thread CPU clock unavailable)" : ""));
            L(Row("generate, non-uniform: wall - CPU (not running)", stall));
            L(Row("generate, non-uniform: wall / CPU", ratio, "x"));
            L(Row("generate, non-uniform, no GC during the work", wallGcNone));
            L(Row("generate, non-uniform, >=1 GC during the work", wallGcHit) + $"   ({gcHit} of {nonUni} chunks)");
            L($"  GC over the walk: collections gen0/1/2 {gcN1[0] - gcN0[0]}/{gcN1[1] - gcN0[1]}/{gcN1[2] - gcN0[2]}" +
              $" ({(gcN1[0] - gcN0[0]) / runSeconds:F2}/s; cumulative allocated bytes: not in Unity's GC API)" +
              $", mono used {monoUsed0 / 1048576.0:F0} -> {monoUsed1 / 1048576.0:F0} MB, heap {monoHeap0 / 1048576.0:F0} -> {monoHeap1 / 1048576.0:F0} MB" +
              $", incremental GC {UnityEngine.Scripting.GarbageCollector.isIncremental}");
            // One forced full collection AFTER the measurement, timed: the
            // stop-the-world pause a non-incremental collection of this heap costs.
            long g0 = StreamTrace.Now; System.GC.Collect(); long g1 = StreamTrace.Now;
            L($"  forced full GC.Collect after the walk (pause estimate, not during it): {StreamTrace.Ms(g1 - g0):F1} ms");
        }
        L(Row("request -> dispatch (pending queue)", reqDisp));
        L(Row("dispatch -> worker start (job queue)", jobWait));
        L($"  admitted {admittedRun} in {runSeconds:F0} s = {admittedRun / runSeconds:F1} chunks/s; worker busy {workerMsSum / 1000.0 / (runSeconds * streamer.WorkerCount) * 100:F0}% of {streamer.WorkerCount} threads (lower bound: counts only chunks that reached the GPU)");
        L("2. MAIN-THREAD ADMISSION (DrainCompletions, per chunk)");
        L(Row("worker done -> admitted (completion queue)", complWait));
        L(Row("admit one chunk, total", admitMs));
        L(Row("  of which transfer scratch -> shared pool", transfer));
        L(Row("  of which cascade tier 1/2 writes", casc));
        L(Row("per-frame transfer (frames that admitted)", phTransfer));
        L("3. CPU -> GPU (tier-0 clipmap UploadDirty)");
        L(Row("admitted -> uploaded", admUp));
        L(Row("admitted -> uploaded", admUpF, "frames", "F0"));
        L(Row("bytes uploaded per frame (all frames)", upBytes, "B", "F0") + $"   cap {EngineConfig.MAX_CLIPMAP_UPLOAD_BYTES_PER_FRAME}; frames over cap {overBudget}");
        L(Row("chunks uploaded per frame", upChunks, "", "F0"));
        L(Row("upload pass total (frames that uploaded)", phUploadTotal));
        L(Row("  sort dirty set", phSort));
        L(Row("  staging: 4096-handle chunk table (convert)", phStage));
        L(Row("  clipmap SetData (copy)", phClip));
        L(Row("  air-mip RebuildRegion (convert)", phMip));
        L(Row("  air-mip PackRegion (convert)", phPack));
        L(Row("  brick-body SetData runs (copy)", phBrick));
        L(Row("  packed air-mip SetData (copy)", phPackUp));
        L(Row("cascade UploadDirty per frame (all frames)", phCasc));
        L("4. TIME TO VISIBLE (request -> first frame in the GPU mirror)");
        L(Row("request -> uploaded", reqUp));
        L(Row("request -> uploaded", reqUpF, "frames", "F0"));
        L(Row("distance at request (nearest point)", distReq, "m", "F0"));
        L(Row("distance when uploaded", distUp, "m", "F0"));
        L(Row("LATE: inside 64 m before uploaded, missing for", late) + $"   ({late.Count} of {done.Count} chunks)");
        L(Row("LATE, frames", lateF, "frames", "F0"));
        L(Row("chunks missing on GPU, per frame", missGpu, "", "F0"));
        L(Row("  of which inside 64 m (full-detail ring)", missIn64, "", "F0"));
        L($"  dropped from the trace: {StreamTrace.DroppedUnrequested} un-requested (camera moved on), {StreamTrace.DroppedDiscarded} discarded on arrival, " +
          $"{StreamTrace.DroppedEvicted} evicted before upload; still open at the end {StreamTrace.OpenCount}");
        L("5. LOAD DEFICIT (chunks in the load square + band not resident; every 10th frame)");
        L(Row("deficit", deficits, "", "F0"));
        L(Row("deficit, nearest point inside 128 m", deficits128, "", "F0"));
        L($"  band switches {streamer.BandSwitchesTotal - bandSwitches0}, discarded on arrival {streamer.DiscardedOnArrivalTotal - discarded0}");
        L("  10 s windows:");
        _log.Append(windows);
        // 7. Task 16b: the worst case (KNOWN_ISSUES "STREAMING REQUIREMENT").
        // Capacity = workers x 1000 / (generate + downsample) per NON-uniform
        // chunk -- the worst case has no air fast path -- at p50 (sustained)
        // and p99 (worst observed).
        {
            var req = StreamingRequirement.Live();
            var rq = StreamingRequirement.Compute(req);
            genDownSolid.Sort(); reqUp.Sort(); admitMs.Sort();
            int w = streamer.WorkerCount;
            double g50 = Pct(genDownSolid, 0.5), g99 = Pct(genDownSolid, 0.99);
            double cap50 = g50 > 0 ? w * 1000.0 / g50 : 0, cap99 = g99 > 0 ? w * 1000.0 / g99 : 0;
            double up99 = Pct(reqUp, 0.99) / 1000.0, adm50 = Pct(admitMs, 0.5);
            double frameMs = 1000.0 / req.frameHz, mainMs = rq.admitsPerFrame * adm50;
            L("7. WORST-CASE REQUIREMENT (StreamingRequirement; KNOWN_ISSUES \"STREAMING REQUIREMENT\")");
            L($"  worst case: {req.vMaxMps:F0} m/s, diagonal dive (worst 3-D heading), {rq.side}x{rq.side} chunks x {req.bandLayers} layers, " +
              $"every chunk full cost; prefetch margin {rq.marginM:F1} m (request radius {req.requestRadiusChunks}, detail radius {req.detailRadiusChunks})");
            L($"  throughput (gen p50 {g50:F0} ms, {w} workers): {StreamingRequirement.Verdict(rq.aMax, cap50, "/s")}");
            L($"  throughput (gen p99 {g99:F0} ms, {w} workers): {StreamingRequirement.Verdict(rq.aMax, cap99, "/s")}");
            L($"    level flight only (no dive): REQUIRED {rq.aHorizontal:F1}/s; measured admitted this run {admittedRun / runSeconds:F1}/s (incl. {uniformN} uniform chunks)");
            L(rq.latencyMeetable
                ? $"  latency: REQUIRED request->GPU p99 <= {rq.latencyBudgetS:F3} s, MEASURED p99 {up99:F3} s -> {(up99 <= rq.latencyBudgetS ? "MET" : $"NOT MET (short by {up99 / rq.latencyBudgetS:F1}x)")}"
                : $"  latency: REQUIRED request->GPU p99 <= 0 s (margin {rq.marginM:F1} m), MEASURED p99 {up99:F3} s -> NOT MET (no throughput can meet a zero budget: the worst case itself must change)");
            L($"  main thread: {rq.admitsPerFrame:F1} admits/frame x admit p50 {adm50:F2} ms = {mainMs:F1} ms vs a {frameMs:F1} ms frame -> {(mainMs <= frameMs ? "MET" : $"NOT MET (short by {mainMs / frameMs:F1}x)")}" +
              $"; MAX_CHUNK_LOADS_PER_FRAME {EngineConfig.MAX_CHUNK_LOADS_PER_FRAME}");
            L($"  bursts: X/Z plane {rq.faceBurst}, corner {rq.cornerBurst}, Y recentre {rq.verticalBurst} chunks; cold start / teleport {rq.bandChunks} chunks " +
              $"-> {rq.coldFillS:F2} s at the required rate (PROPOSED target, not set)");
            L($"  largest worst-case speed sustained at this capacity: {StreamingRequirement.SustainedSpeedMps(req, cap50):F2} m/s (p50), " +
              $"{StreamingRequirement.SustainedSpeedMps(req, cap99):F2} m/s (p99) -- information only");
        }
        L("");
        L("per-frame rows: streamprobe_frames.tsv; per-chunk rows: streamprobe_chunks.tsv");
        // task 27: workStartS = the worker's start on the chunk, s since launch (the Stopwatch
        // clock re-zeroed on realtimeSinceStartup's); the "walk start" line gives the walk's t0 on that scale.
        L($"walk start {runStart:F2} s since launch, s0 {s0:F1} m, {speed:F1} m/s -- chunks' workStartS is on the same scale");
        long launchTick = System.Diagnostics.Stopwatch.GetTimestamp() - (long)(Time.realtimeSinceStartupAsDouble * System.Diagnostics.Stopwatch.Frequency);
        var cr = new StringBuilder(done.Count * 100);
        cr.Append("cx\tcy\tcz\tuniform\tgenMs\tdownMs\treqDispMs\tjobWaitMs\tcomplWaitMs\tadmitMs\ttransferMs\tcascadeMs\tadmUpMs\treqUpMs\treqUpFrames\tdistReqM\tdistUpM\tlateMs\tgenCpuMs\tdownCpuMs\tgcN\tworkStartS\n");
        foreach (var e in done)
        {
            long workStart = e.tWorkerDone - (long)((e.genMs + e.downMs) * System.Diagnostics.Stopwatch.Frequency / 1000.0);
            cr.Append(e.coord.x).Append('\t').Append(e.coord.y).Append('\t').Append(e.coord.z).Append('\t').Append(e.uniform ? 1 : 0).Append('\t')
              .Append(e.genMs.ToString("F3")).Append('\t').Append(e.downMs.ToString("F3")).Append('\t')
              .Append(StreamTrace.Ms(e.tDispatch - e.tRequest).ToString("F2")).Append('\t').Append(StreamTrace.Ms(workStart - e.tDispatch).ToString("F2")).Append('\t')
              .Append(StreamTrace.Ms(e.tAdmit - e.tWorkerDone).ToString("F2")).Append('\t').Append(e.admitMs.ToString("F3")).Append('\t')
              .Append(e.transferMs.ToString("F3")).Append('\t').Append(e.cascadeMs.ToString("F3")).Append('\t')
              .Append(StreamTrace.Ms(e.tUpload - e.tAdmit).ToString("F2")).Append('\t').Append(StreamTrace.Ms(e.tUpload - e.tRequest).ToString("F2")).Append('\t')
              .Append(e.fUpload - e.fRequest).Append('\t').Append(e.distRequestM.ToString("F1")).Append('\t').Append(e.distUploadM.ToString("F1")).Append('\t')
              .Append(e.fNear >= 0 ? StreamTrace.Ms(e.tUpload - e.tNear).ToString("F2") : "").Append('\t')
              .Append(e.genCpuMs.ToString("F3")).Append('\t').Append(e.downCpuMs.ToString("F3")).Append('\t').Append(e.gcDuringWork).Append('\t')
              .Append((StreamTrace.Ms(workStart - launchTick) / 1000.0).ToString("F3")).Append('\n');
        }
        File.WriteAllText(Path.Combine(_folder, "streamprobe_chunks.tsv"), cr.ToString());
        File.WriteAllText(Path.Combine(_folder, "streamprobe_frames.tsv"), rows.ToString());
        File.WriteAllText(Path.Combine(_folder, "streamprobe.txt"), _log.ToString());
        Debug.Log("[streamprobe]\n" + _log);
        yield return null;
        Application.Quit(0);
    }

    private static float Pct(List<float> sorted, double q)
        => sorted.Count == 0 ? 0f : sorted[Math.Min(sorted.Count - 1, (int)Math.Floor(q * sorted.Count))];

    private static int CountOver(List<float> v, float t) { int n = 0; foreach (var x in v) if (x > t) n++; return n; }

    /// Beauty, LOD-tier view (6: tier 0 blue, 1 green, 2 red, far band grey)
    /// and band view (9: voxels blue, far F0..F4 green/yellow/orange/red/magenta).
    private IEnumerator ShotSet(string prefix, string caption)
    {
        yield return Shot(prefix + "_beauty", caption);
        RaymarchFeature.DebugViewOverride = RaymarchFeature.DebugMode.LODTier; RaymarchFeature.UseDebugViewOverride = true;
        yield return Shot(prefix + "_tier", caption + " -- LOD tier view");
        RaymarchFeature.DebugViewOverride = RaymarchFeature.DebugMode.FarBand;
        yield return Shot(prefix + "_band", caption + " -- band view");
        RaymarchFeature.UseDebugViewOverride = false;
    }
}
