// Assets/Game/PlaygroundCapture.FlySweep.cs
//
// -flysweep (task 13, 2026-10-03): WHY task 10's fly still pose drew no voxels.
// Evidence only: it moves the camera, switches debug views and reads the
// shader's debug pixel; nothing generated, uploaded or drawn changes.
//
// At task 10's fly pose XZ (4014.0, 3999.6) m and heading (0.71, 0, 0.71) it
// photographs still poses at eye heights -heights (default 2 8 16 32 48 63) m
// above the ground directly below (the first solid voxel in the CPU store)
// and pitches -pitches (default 8 30) deg down, each fully loaded, in:
//   tier view (6)   tier 0 blue / 1 green / 2 red / miss grey
//   band view (9)   voxels blue / far F0..F4 green..magenta / sky dark
//   stop view (11)  voxel hit blue / march CAPPED over resident data red
//                   (magenta = capped, far drew it) / far drew it green /
//                   march ended uncapped and far missed grey
// and reports each class as % of the frame. Per pose it also reads the
// shader's debug pixel (RaymarchFeature.DebugBuffer: ray dir, steps,
// maxDist, distance at loop exit, hit) at three screen rows and marches the
// same ray through the CPU store (ChunkStore.GetVoxel) as an oracle: where
// the voxel march SHOULD have hit, and whether that chunk is resident and
// inside the GPU mirror box.
// Release standalone via `MODE=flysweep ./run-tg2world.sh`.
using System;
using System.Collections;
using System.IO;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Rendering;
using VoxelEngine.Streaming;

public partial class PlaygroundCapture
{
    private IEnumerator FlySweep()
    {
        Screen.SetResolution(1920, 1080, !HasFlag("-windowed"));
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;
        var pg = FindAnyObjectByType<Playground>();
        while (Phase4Bootstrapper.Store == null) yield return null;
        RaymarchFeature.ShadowQualityLevel = ArgInt("-shadowlevel", 0);
        Playground.DebugHideUi = true;
        for (int i = 0; i < 300; i++) yield return null;
        if (pg != null) { pg.SendMessage("TeleportToArena", SendMessageOptions.DontRequireReceiver); yield return null; }
        SunCycle.SetHours(SunCycle.GoldenHourHours);
        VoxelEngine.Rendering.LightingDriver.ClockPaused = true;

        string ts = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
        string tag = ArgValue("-tag") ?? "flysweep";
        _folder = Path.Combine(Application.persistentDataPath, "FlySweep", $"{ts}_{tag}");
        Directory.CreateDirectory(_folder);
        var cam = Camera.main;
        var streamer = Phase4Bootstrapper.Streamer;
        var far = FarField.Active;
        var store = Phase4Bootstrapper.Store;
        var clip = Phase4Bootstrapper.Clipmap;
        if (streamer == null || streamer.TerrainGenSource == null)
        {
            L("FLY SWEEP needs -world tg2; none -- nothing measured.");
            File.WriteAllText(Path.Combine(_folder, "flysweep.txt"), _log.ToString());
            Application.Quit(1); yield break;
        }

        float px = ArgFloat("-sweepx", 4014.0f), pz = ArgFloat("-sweepz", 3999.6f);
        Vector3 dir = new Vector3(0.70710678f, 0f, 0.70710678f);
        float[] heights = ParseFloats(ArgValue("-heights") ?? "63 48 32 16 8 2");
        float[] pitches = ParseFloats(ArgValue("-pitches") ?? "8 30");

        Vector3 pos = new Vector3(px, 868.8f, pz);
        float pitch0 = 8f;
        void Apply() { cam.transform.position = pos; cam.transform.rotation = Quaternion.LookRotation(dir, Vector3.up) * Quaternion.Euler(pitch0, 0f, 0f); }
        bool Busy() => streamer.PendingLoads > 0 || streamer.InFlightLoads > 0 || streamer.LoadDeficit() > 0 ||
                       streamer.ClipmapDirtyRemaining > 0 || (far != null && far.Busy);
        IEnumerator Settle()
        {
            for (int i = 0; i < 120; i++) { Apply(); yield return null; }
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 240f && Busy()) { Apply(); yield return null; }
            for (int i = 0; i < 30; i++) { Apply(); yield return null; }
        }

        // ---- the ground directly below, from the CPU store at task 10's eye ----
        yield return Settle();
        int3 v0 = new int3((int)math.floor(px * 10f), 0, (int)math.floor(pz * 10f));
        int top = int.MinValue;
        for (int y = (int)math.floor(pos.y * 10f); y > (int)math.floor(pos.y * 10f) - 2000; y--)
            if (store.GetVoxel(new int3(v0.x, y, v0.z)) != Materials.Air) { top = y; break; }
        GroundTopM(px, pz, out float mapTop);
        float ground = top == int.MinValue ? mapTop : (top + 1) * 0.1f;

        L($"FLY SWEEP -- task 13 -- {ts}");
        L($"device {SystemInfo.graphicsDeviceName}  {Screen.width}x{Screen.height}  {(Screen.fullScreen ? "FULLSCREEN" : "WINDOWED")}  " +
          $"{(Debug.isDebugBuild ? "DEVELOPMENT" : "RELEASE")}  preset {(HasFlag("-renderpreset") ? RenderPreset.Current.ToString() : "as shipped")}  " +
          $"dispatch {RaymarchFeature.LastDispatchResolution.x}x{RaymarchFeature.LastDispatchResolution.y}  fov {cam.fieldOfView:F1}  " +
          $"iter cap {RaymarchFeature.MaxOuterIterations}  cascade {(RaymarchFeature.UseLODCascade ? "ON" : "OFF")}  clock {SunCycle.ClockString()}");
        L($"pose XZ ({px:F1}, {pz:F1}) m, heading ({dir.x:F2}, 0, {dir.z:F2}); ground directly below: CPU store top {(top == int.MinValue ? "none" : (ground).ToString("F1") + " m")}, map {mapTop:F1} m");
        L($"mirror box {clip.WindowDimsChunks} chunks; load radius {streamer.LoadRadiusChunks} chunks (+-{streamer.LoadRadiusChunks * 12.8f:F0} m XZ)");
        foreach (var c in Camera.allCameras)
            L($"camera '{c.name}' enabled {c.enabled} depth {c.depth} target {(c.targetTexture != null ? c.targetTexture.name : "screen")} pos {c.transform.position} main {c == cam}");
        L("");
        L("Per pose: % of frame. tier: t0/t1/t2/miss. band: vox/F0/F1/F2/F3/F4/sky. stop: hit/CAP(red)/CAP+far/far/end(grey).");
        L("probe rows: shader debug pixel (dispatch px) -> ray pitch, shader hit?, dist at loop exit m, steps/cap, maxDist m;");
        L("            CPU oracle (same ray through ChunkStore.GetVoxel) -> first solid at m, its chunk, resident?, in mirror box?");
        L("            MAP oracle (same ray against the world map's surface) -> where the visible surface is, in band?, in load square?");
        L("");

        var palTier = new (string, Color)[] { ("miss", new Color(0.05f, 0.05f, 0.05f)), ("t0", new Color(0.1f, 0.3f, 0.9f)),
                                              ("t1", new Color(0.15f, 0.85f, 0.2f)), ("t2", new Color(0.9f, 0.15f, 0.15f)) };
        var palBand = new (string, Color)[] { ("sky", new Color(0.02f, 0.02f, 0.03f)), ("vox", new Color(0.15f, 0.35f, 0.90f)),
                                              ("F0", new Color(0.15f, 0.80f, 0.25f)), ("F1", new Color(0.90f, 0.90f, 0.15f)),
                                              ("F2", new Color(0.95f, 0.55f, 0.10f)), ("F3", new Color(0.90f, 0.15f, 0.15f)),
                                              ("F4", new Color(0.85f, 0.20f, 0.85f)) };
        var palStop = new (string, Color)[] { ("end", new Color(0.25f, 0.25f, 0.27f)), ("hit", new Color(0.15f, 0.35f, 0.90f)),
                                              ("CAP", new Color(0.95f, 0.10f, 0.10f)), ("CAP+far", new Color(0.85f, 0.20f, 0.85f)),
                                              ("far", new Color(0.15f, 0.80f, 0.25f)) };

        foreach (float h in heights)
        {
            foreach (float p in pitches)
            {
                pos = new Vector3(px, ground + h, pz); pitch0 = p;
                yield return Settle();
                streamer.LayerRange(out int blo, out int bhi);
                string name = $"h{h:00}_p{p:00}";
                L($"== {name}: eye {pos.y:F1} m ({h:F0} m over the ground below), pitch {p:F0} down, band layers {blo}..{bhi} ({blo * 12.8f:F1}-{(bhi + 1) * 12.8f:F1} m), " +
                  $"mirror origin {clip.WindowOrigin}, deficit {streamer.LoadDeficit()}, pending {streamer.PendingLoads}, dirty {streamer.ClipmapDirtyRemaining}, " +
                  $"tier bounds {RaymarchFeature.LastTierBoundsM[0]:F0}/{RaymarchFeature.LastTierBoundsM[1]:F0}/{RaymarchFeature.LastTierBoundsM[2]:F0} m");

                yield return SweepShot(name + "_beauty", -1, null, null);
                string sTier = null, sBand = null, sStop = null;
                yield return SweepShot(name + "_tier", 6, palTier, s => sTier = s);
                yield return SweepShot(name + "_band", 9, palBand, s => sBand = s);
                yield return SweepShot(name + "_stop", 11, palStop, s => sStop = s);
                L($"   tier {sTier} | band {sBand} | stop {sStop}   (camera at capture {_sweepCamAtShot})");
                if (!_sweepValidated)
                {
                    // After the first pose's shots, so its readbacks cannot affect them.
                    _sweepValidated = true;
                    var vr = ClipmapValidator.ValidateRegion(clip, Phase4Bootstrapper.Pool, store, maxChunks: 4096, validateBrickBodies: false);
                    int stale = ClipmapValidator.CountStaleSlots(clip, store, out string ex);
                    L($"   MIRROR CHECK: {vr.chunksChecked} resident chunks, lost {vr.mismatchesInCleanChunks}, lag {vr.mismatchesInDirtyChunks}, stale {stale}" + (ex != null ? $" [{ex}]" : "") +
                      $", outOfBox {clip.SkippedOutOfBoxTotal}");
                }

                var res = RaymarchFeature.LastDispatchResolution;
                foreach (var (rowName, fy) in new[] { ("centre", 0.5f), ("lower", 0.25f), ("bottom", 0.0625f) })
                {
                    RaymarchFeature.DebugPixel = new Vector2Int(res.x / 2, (int)(res.y * fy));
                    for (int i = 0; i < 4; i++) { Apply(); yield return null; }
                    var d = new float[64];
                    RaymarchFeature.DebugBuffer.GetData(d, 0, 0, 64);
                    var rd = math.normalize(new float3(d[0], d[1], d[2]));
                    float rayPitch = math.degrees(math.asin(-rd.y));
                    string oracle = CpuOracle(store, clip, (float3)pos * 10f, rd, 4000) + " | MAP: " + MapOracle(pos, rd, blo, bhi, streamer.LoadRadiusChunks);
                    L($"   probe {rowName,-6} px ({res.x / 2},{(int)(res.y * fy)}) ray {rayPitch:F1} deg down: shader {(d[45] > 0.5f ? "HIT" : "miss")} tier {d[17]:F0}, " +
                      $"exit at {d[19] * 0.1f:F1} m, steps {d[7]:F0}/{RaymarchFeature.MaxOuterIterations}, maxDist {d[16] * 0.1f:F1} m | CPU: {oracle}");
                }
            }
        }
        RaymarchFeature.UseDebugViewOverride = false;
        File.WriteAllText(Path.Combine(_folder, "flysweep.txt"), _log.ToString());
        Debug.Log("[flysweep]\n" + _log);
        yield return null;
        Application.Quit(0);
    }

    private bool _sweepValidated;
    private string _sweepCamAtShot = "";

    /// Where the ray meets the WORLD MAP's surface (ground, water or sea top,
    /// GroundTopM), in 0.5 m steps up to 2 km: distance, height, and whether
    /// that point is inside the loaded band's Y range and the load square --
    /// i.e. whether voxel data for it can exist at all.
    private static string MapOracle(Vector3 eye, float3 rd, int blo, int bhi, int loadRadiusChunks)
    {
        int3 camChunk = new int3((int)math.floor(eye.x * 10f) >> 7, 0, (int)math.floor(eye.z * 10f) >> 7);
        for (float t = 0f; t < 2000f; t += 0.5f)
        {
            float3 q = (float3)eye + rd * t;
            GroundTopM(q.x, q.z, out float g);
            if (q.y <= g)
            {
                int cx = (int)math.floor(q.x * 10f) >> 7, cz = (int)math.floor(q.z * 10f) >> 7;
                bool inSq = math.abs(cx - camChunk.x) <= loadRadiusChunks && math.abs(cz - camChunk.z) <= loadRadiusChunks;
                bool inBand = g >= blo * 12.8f && g < (bhi + 1) * 12.8f;
                return $"surface at {t:F1} m (horizontal {math.length(q.xz - ((float3)eye).xz):F0} m), top {g:F1} m, in band {inBand}, in load square {inSq}";
            }
        }
        return "no surface within 2 km";
    }

    private static float[] ParseFloats(string s)
    {
        var parts = s.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
        var r = new float[parts.Length];
        for (int i = 0; i < parts.Length; i++) r[i] = float.Parse(parts[i], System.Globalization.CultureInfo.InvariantCulture);
        return r;
    }

    /// Voxel DDA from `start` (voxel units) along `dir` through the CPU store,
    /// up to maxVox voxels: the first non-air voxel, i.e. what the voxel march
    /// should hit if the mirror and its acceleration structures match the store.
    private static string CpuOracle(ChunkStore store, TerrainClipmap clip, float3 start, float3 dir, int maxVox)
    {
        int3 v = (int3)math.floor(start);
        int3 step = new int3(dir.x >= 0 ? 1 : -1, dir.y >= 0 ? 1 : -1, dir.z >= 0 ? 1 : -1);
        float3 ad = math.max(math.abs(dir), 1e-6f);
        float3 tDelta = 1f / ad;
        float3 tMax = new float3(
            (step.x > 0 ? (v.x + 1 - start.x) : (start.x - v.x)) * tDelta.x,
            (step.y > 0 ? (v.y + 1 - start.y) : (start.y - v.y)) * tDelta.y,
            (step.z > 0 ? (v.z + 1 - start.z) : (start.z - v.z)) * tDelta.z);
        float t = 0f;
        while (t < maxVox)
        {
            if (store.GetVoxel(v) != Materials.Air)
            {
                int3 c = new int3(v.x >> 7, v.y >> 7, v.z >> 7);
                int3 rel = c - clip.WindowOrigin;
                bool inBox = math.all(rel >= 0) && math.all(rel < clip.WindowDimsChunks);
                return $"first solid at {t * 0.1f:F1} m, voxel y {v.y * 0.1f:F1} m, chunk {c}, resident {store.IsResident(c)}, in mirror box {inBox}";
            }
            if (tMax.x < tMax.y && tMax.x < tMax.z) { v.x += step.x; t = tMax.x; tMax.x += tDelta.x; }
            else if (tMax.y < tMax.z) { v.y += step.y; t = tMax.y; tMax.y += tDelta.y; }
            else { v.z += step.z; t = tMax.z; tMax.z += tDelta.z; }
        }
        return $"no solid within {maxVox * 0.1f:F0} m";
    }

    /// One debug-view shot (mode < 0 = beauty), with each palette class as %
    /// of the frame (nearest of the linear and sRGB-encoded palette colours).
    private IEnumerator SweepShot(string name, int mode, (string, Color)[] pal, Action<string> result)
    {
        RaymarchFeature.UseDebugViewOverride = mode >= 0;
        if (mode >= 0) RaymarchFeature.DebugViewOverride = (RaymarchFeature.DebugMode)mode;
        for (int i = 0; i < 8; i++) yield return null;
        yield return new WaitForEndOfFrame();
        _sweepCamAtShot = Camera.main != null ? $"{Camera.main.transform.position} fwd {Camera.main.transform.forward}" : "no main camera";
        Texture2D t = ScreenCapture.CaptureScreenshotAsTexture();
        try
        {
            File.WriteAllBytes(Path.Combine(_folder, name + ".png"), t.EncodeToPNG());
            if (pal != null)
            {
                var px = t.GetPixels32();
                var counts = new int[pal.Length];
                var ref0 = new float3[pal.Length * 2];
                for (int k = 0; k < pal.Length; k++)
                {
                    var c = pal[k].Item2;
                    ref0[2 * k] = new float3(c.r, c.g, c.b) * 255f;
                    ref0[2 * k + 1] = new float3(Mathf.LinearToGammaSpace(c.r), Mathf.LinearToGammaSpace(c.g), Mathf.LinearToGammaSpace(c.b)) * 255f;
                }
                for (int i = 0; i < px.Length; i++)
                {
                    float3 q = new float3(px[i].r, px[i].g, px[i].b);
                    int best = 0; float bd = float.MaxValue;
                    for (int k = 0; k < ref0.Length; k++) { float dd = math.lengthsq(q - ref0[k]); if (dd < bd) { bd = dd; best = k >> 1; } }
                    counts[best]++;
                }
                var sb = new System.Text.StringBuilder();
                for (int k = 0; k < pal.Length; k++) sb.Append(pal[k].Item1).Append(' ').Append((100.0 * counts[k] / px.Length).ToString("F1")).Append(k + 1 < pal.Length ? "  " : "");
                result?.Invoke(sb.ToString());
            }
        }
        finally { Destroy(t); }
        RaymarchFeature.UseDebugViewOverride = false;
    }
}
