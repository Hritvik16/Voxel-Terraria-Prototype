// Assets/Game/PlaygroundCapture.FarBand.cs
//
// -farvisual : THE FAR BAND, SHOWN. DESIGN_LARGE_WORLD_AND_TERRAIN_OVERHAUL.md
// §7 items 1-2 and §2.3, Step 1's visual half.
//
// Requires -farband 1 (the ring is built at startup by Phase4Bootstrapper). At
// each pose x time of day it renders the SAME frame with the far band OFF then
// ON (FarField.RenderEnabled -- one process, one ring, only the march gated),
// plus the band view (which band drew each pixel) and the far step heat. It
// also verifies every pose's camera is in AIR before shooting: a camera inside
// rock "measures" a black frame (CLAUDE.md known issue, found three times).
//
// Frame times here are DIAGNOSTIC, back-to-back in one process -- NOT a budget
// figure. The FPS claim comes from -sustained arms, counterbalanced, in their
// own processes (run-farband.sh).
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Rendering;
using VoxelEngine.WorldGen;

public partial class PlaygroundCapture
{
    private IEnumerator FarVisual()
    {
        Screen.SetResolution(1920, 1080, !HasFlag("-windowed"));
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;
        while (Phase4Bootstrapper.Store == null) yield return null;
        yield return Frames(300);

        string ts = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
        _folder = Path.Combine(Application.persistentDataPath, "FarBand", ts);
        Directory.CreateDirectory(_folder);
        VoxelEngine.Rendering.LightingDriver.ClockPaused = true;
        RaymarchFeature.ShadowQualityLevel = ArgInt("-shadowlevel", 0);
        int gate = ArgInt("-gateres", 960);
        if (gate > 0)
        {
            RaymarchFeature.GateModeOverride = RaymarchFeature.GateResMode.ForcedCustom;
            RaymarchFeature.CustomGateResolution = new Vector2Int(gate, gate * 9 / 16);
        }
        Playground.DebugHideUi = true;

        var far = FarField.Active;
        var cam = Camera.main;
        float fov0 = cam.fieldOfView;

        L($"FAR BAND VISUAL A/B -- {ts}");
        L($"device {SystemInfo.graphicsDeviceName}  {Screen.width}x{Screen.height}  " +
          $"{(Screen.fullScreen ? "FULLSCREEN" : "WINDOWED")}  {(Debug.isDebugBuild ? "DEVELOPMENT" : "RELEASE")}  " +
          $"gate {gate}  shadow rung {RaymarchFeature.ShadowQualityLevel}");
        if (far == null)
        {
            L("FAR BAND NOT BUILT -- run with -farband 1. Nothing to compare.");
            File.WriteAllText(Path.Combine(_folder, "farvisual.txt"), _log.ToString());
            Application.Quit(2);
            yield break;
        }
        L($"far ring: {FarField.LEVELS} levels x {FarField.EDGE}^2, initial fill {far.InitialFillMs:F0} ms, " +
          $"max top {far.MaxHeightVoxels} vox, GPU {far.GpuBytes / 1048576f:F1} MB, far shadows {(far.ShadowsOn ? "ON" : "OFF")}, " +
          $"haze range {far.HazeRangeMetres:F0} m, step caps {far.PrimaryStepCap}/{far.ShadowStepCap}");
        L("Per pose: OFF = shipped frame (sky past the window). ON = far band. band = which band drew each");
        L("pixel (blue voxel window; F0 green, F1 yellow, F2 orange, F3 red, F4 magenta). heat = far steps.");
        L("ms columns are DIAGNOSTIC (back-to-back, one process), not a budget figure.");
        L("");
        L($"{"pose",-24}{"time",7}{"sun",7}{"air?",6}{"ms off",8}{"ms on",8}{"far px%",9}{"far hit%",10}{"steps/ray",11}");

        var st = ColumnSampler.CreateState(Phase4Bootstrapper.Meta);
        try
        {
            Vector3 c = new Vector3(1268f, 0f, 1268f);
            float g = GroundMetres(in st, c.x, c.z);
            float golden = SunCycle.GoldenHourHours;
            float dawn = SunCycle.HoursForElevation(6f, morning: true);
            var times = new (string name, float h)[] { ("dawn", dawn), ("noon", 12f), ("golden", golden), ("night", 22f) };

            var poses = new List<(string name, Vector3 pos, Vector3 euler, float fov, bool allTimes)>
            {
                ("ground_seam",     new Vector3(c.x, g + 2.0f, c.z),   new Vector3(1.5f, 35f, 0f),  fov0, true),
                ("seam_zoom_20m",   new Vector3(c.x, g + 20f, c.z),    new Vector3(4.2f, 35f, 0f),  14f,  true),
                ("ground_south",    new Vector3(c.x, g + 2.0f, c.z),   new Vector3(1.5f, 200f, 0f), fov0, false),
                ("hill_150m",       new Vector3(c.x, 150f, c.z),        new Vector3(12f, 35f, 0f),   fov0, true),
                ("high_300m",       new Vector3(c.x, 300f, c.z),        new Vector3(15f, 35f, 0f),   fov0, true),
                ("map_1000m",       new Vector3(c.x, 1000f, c.z),       new Vector3(89.5f, 0f, 0f),  fov0, false),
                ("map_3000m",       new Vector3(c.x, 3000f, c.z),       new Vector3(89.5f, 0f, 0f),  fov0, false),
                ("coast_from_sea",  new Vector3(c.x, 30f, c.z - 1250f), new Vector3(3f, 0f, 0f),     fov0, false),
            };

            foreach (var (pname, pos, euler, fov, allTimes) in poses)
            {
                foreach (var (tname, hours) in times)
                {
                    if (!allTimes && tname != "golden") continue;
                    SunCycle.SetHours(hours);
                    cam.fieldOfView = fov;
                    SetPose(cam, pos, euler);
                    yield return Frames(90);   // streaming and far strips settle
                    for (int w = 0; w < 1200 && far.Busy; w++) yield return null;
                    yield return Frames(10);

                    bool air = CameraInAir(in st, cam.transform.position);
                    string tag = $"{pname}_{tname}";

                    FarField.RenderEnabled = false;
                    double msOff = 0; yield return Frames(8);
                    for (int i = 0; i < 60; i++) { yield return null; msOff += Time.unscaledDeltaTime * 1000.0; }
                    msOff /= 60.0;
                    yield return CaptureTo($"{tag}_A_off");

                    FarField.RenderEnabled = true;
                    double msOn = 0; yield return Frames(8);
                    for (int i = 0; i < 60; i++) { yield return null; msOn += Time.unscaledDeltaTime * 1000.0; }
                    msOn /= 60.0;
                    RaymarchFeature.CollectShadowStats = true;
                    RaymarchFeature.ResetShadowStats();
                    yield return Frames(10);
                    uint[] s = RaymarchFeature.ReadShadowStats();
                    RaymarchFeature.CollectShadowStats = false;
                    yield return CaptureTo($"{tag}_B_on");

                    if (tname == "golden" || pname == "ground_seam")
                    {
                        RaymarchFeature.DebugViewOverride = (RaymarchFeature.DebugMode)9;
                        RaymarchFeature.UseDebugViewOverride = true;
                        yield return Frames(6);
                        yield return CaptureTo($"{tag}_C_band");
                        RaymarchFeature.DebugViewOverride = (RaymarchFeature.DebugMode)10;
                        yield return Frames(6);
                        yield return CaptureTo($"{tag}_D_heat");
                        RaymarchFeature.UseDebugViewOverride = false;
                    }

                    double pix = (double)RaymarchFeature.LastDispatchResolution.x * RaymarchFeature.LastDispatchResolution.y * 10.0;
                    double farRays = s.Length > 7 ? s[5] : 0, farSteps = s.Length > 7 ? s[6] : 0, farHits = s.Length > 7 ? s[7] : 0;
                    L($"{pname,-24}{tname,7}{SunCycle.ElevationDegrees,6:F1}°{(air ? "yes" : "NO!"),6}{msOff,8:F2}{msOn,8:F2}" +
                      $"{100.0 * farRays / Math.Max(1.0, pix),9:F1}{100.0 * farHits / Math.Max(1.0, farRays),10:F1}" +
                      $"{farSteps / Math.Max(1.0, farRays),11:F1}");
                }
            }
            cam.fieldOfView = fov0;

            L("");
            L($"far ring upkeep over the run: {far.StripsApplied} strips, {far.FullRefills} full refills, " +
              $"{far.ColumnsSampled:N0} columns sampled, {far.BytesUploaded / 1048576.0:F1} MB uploaded, last strip job {far.LastStripJobMs:F1} ms");
        }
        finally { st.Dispose(); }

        File.WriteAllText(Path.Combine(_folder, "farvisual.txt"), _log.ToString());
        Debug.Log("[farvisual]\n" + _log);
        yield return null;
        Application.Quit(0);
    }

    // =====================================================================
    // -farpreview (with -farband 1 -farsource tg2): the Step 2 generator's
    // sizeClass 2 island, drawn by the far band alone. A LOOK at the new world
    // at scale -- 600 m peak, 6 km island, 6.5 km horizon -- not a measurement.
    // Far band only means 0.8 m blocks at the closest range; the voxel detail
    // field D is not in these frames.
    // =====================================================================
    private IEnumerator FarPreview()
    {
        Screen.SetResolution(1920, 1080, !HasFlag("-windowed"));
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;
        while (Phase4Bootstrapper.Store == null) yield return null;
        yield return Frames(200);
        string ts = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
        bool world = Phase4Bootstrapper.IsTg2World;
        _folder = Path.Combine(Application.persistentDataPath, "FarBand", ts + (world ? "_world" : "_preview"));
        Directory.CreateDirectory(_folder);
        VoxelEngine.Rendering.LightingDriver.ClockPaused = true;
        RaymarchFeature.ShadowQualityLevel = 0;
        RaymarchFeature.GateModeOverride = RaymarchFeature.GateResMode.ForcedCustom;
        RaymarchFeature.CustomGateResolution = new Vector2Int(ArgInt("-gateres", 960), ArgInt("-gateres", 960) * 9 / 16);
        Playground.DebugHideUi = true;
        var far = FarField.Active;
        var cam = Camera.main;
        // V2: also runs in a real sizeClass 2 world (-world tg2), where the far
        // band is ALIGNED (offset 0, no sea shift) and the voxel band streams
        // around each pose -- the same poses, now walkable up close.
        if (far == null || !far.UsesTg2)
        {
            L("PREVIEW NEEDS -farband 1 -farsource tg2, or -world tg2");
            File.WriteAllText(Path.Combine(_folder, "farpreview.txt"), _log.ToString());
            Application.Quit(2); yield break;
        }
        var spec = far.Tg2Spec; var map = far.Tg2Map; var o = TerrainGen.Offsets.For(spec);
        int2 off = far.Tg2OffsetVoxels;
        int seaShift = far.Tg2SeaShift;
        // tg2 voxel (x, z) -> engine metres; tg2 absolute Y -> engine metres.
        Vector3 E(float gx, float gy, float gz) => new Vector3((gx - off.x) * 0.1f, (gy - seaShift) * 0.1f, (gz - off.y) * 0.1f);
        float Ground(float gx, float gz) => TgGround(map, gx, gz);

        L(world ? $"SIZECLASS 2 WORLD (-world tg2: voxel band streamed in Y + the far band) -- {ts}"
                : $"SIZECLASS 2 PREVIEW (TerrainGen via the far band) -- {ts}");
        if (RaymarchFeature.Taa) L($"TAA ON{(RaymarchFeature.TaaChecker ? " + CHECKERBOARD" : "")} (sharpen {RaymarchFeature.TaaSharpenAmount:F2}); every shot is taken after >= 90 still frames");
        L($"seed {spec.seed}: world {spec.worldSizeVoxels / 10000f:F0} km, sea at Y {spec.seaLevelY}, peak site {map.PeakPosition}, " +
          $"max top {far.MaxHeightVoxels} (engine voxels), ring initial fill {far.InitialFillMs:F0} ms");
        float c = spec.worldSizeVoxels * 0.5f;
        float2 pk = map.PeakPosition;
        float pkTop = Ground(pk.x, pk.y);
        float2 toC = math.normalizesafe(new float2(c, c) - pk, new float2(1, 0));
        float2 from2k = pk + toC * 20000f;
        var poses = new List<(string, Vector3, Quaternion)>();
        void Look(string n, Vector3 eye, Vector3 at) => poses.Add((n, eye, Quaternion.LookRotation((at - eye).normalized, Vector3.up)));
        Look("peak_from_2km_ground", E(from2k.x, Ground(from2k.x, from2k.y) + 25f, from2k.y), E(pk.x, pkTop - 400f, pk.y));
        Look("summit_view", E(pk.x - toC.x * 300f, pkTop + 250f, pk.y - toC.y * 300f), E(pk.x + toC.x * 40000f, spec.seaLevelY, pk.y + toC.y * 40000f));
        Look("glide_400m", E(c - 16000f, spec.seaLevelY + 4000f, c - 16000f), E(pk.x, pkTop - 800f, pk.y));
        Look("coast_from_sea_30m", E(c, spec.seaLevelY + 300f, c - 36000f), E(c, spec.seaLevelY + 800f, c));
        Look("oblique_3000m", E(c, spec.seaLevelY + 30000f, c - 22000f), E(c, spec.seaLevelY, c + 6000f));
        poses.Add(("map_7000m", E(c, spec.seaLevelY + 70000f, c), Quaternion.Euler(89.5f, 0f, 0f)));
        // Map v2 + §2.5 stamps (2026-09-25): each singleton from a glide, and a
        // crystal cluster, so the new features can be judged by eye.
        var volcSite = TerrainGen.Stamps.VolcanoSite(in spec, in o);
        var treeSite = TerrainGen.Stamps.TreeSite(in spec, in o);
        var geySite = TerrainGen.Stamps.GeyserSite(in spec, in o);
        Look("volcano_from_2km", E(volcSite.x - 20000f, spec.seaLevelY + 5000f, volcSite.y - 6000f), E(volcSite.x, spec.seaLevelY + 2600f, volcSite.y));
        Look("caldera_above", E(volcSite.x - 3000f, spec.seaLevelY + 9000f, volcSite.y - 3000f), E(volcSite.x, spec.seaLevelY + 2000f, volcSite.y));
        if (TerrainGen.Stamps.WorldTreeEnabled) Look("world_tree_1km", E(treeSite.x - 9000f, Ground(treeSite.x - 9000f, treeSite.y) + 600f, treeSite.y - 2000f), E(treeSite.x, Ground(treeSite.x, treeSite.y) - 800f, treeSite.y));
        Look("geyser_basin", E(geySite.x - 2500f, Ground(geySite.x, geySite.y) + 1500f, geySite.y - 2500f), E(geySite.x, Ground(geySite.x, geySite.y), geySite.y));
        Look("rivers_oblique", E(c - 30000f, spec.seaLevelY + 26000f, c - 30000f), E(c, spec.seaLevelY, c));
        if (world)
        {
            // EYE LEVEL, where the voxel band and the far band meet: three dry
            // sites at 25 / 45 / 65% of the island radius, 1.7 m above the
            // ground, looking at the peak.
            foreach (var (en, ee, ea) in Tg2EyeLevelPoses(map)) Look(en, E(ee.x, ee.y, ee.z), E(ea.x, ea.y, ea.z));
            // T5b: a CAP-ROCK LEDGE from 12 m away, eye at the gap's height. The
            // eye column must be open air there (a pose inside rock "measures"
            // a black frame -- CLAUDE.md), so the first of 8 directions whose
            // ground is below the eye is used.
            if (TryLedgePose(spec, map, o, out Vector3 le, out Vector3 lt)) Look("ledge_view", E(le.x, le.y, le.z), E(lt.x, lt.y, lt.z));
            else L("ledge_view: no ledge with an open-air vantage found");
            // CLIFF FEET (2026-10-01, owner: near cliffs drawn by the far band):
            // up to three dry sites, >= 1 km apart, where the ground within ~56 m
            // rises >= 60 m -- past the top of a band centred on the eye. Eye
            // 1.7 m above the ground, looking at the cliff's upper part. Run
            // with -centredband for the old placement (A/B).
            {
                int nC = spec.mapCells, cellV = spec.mapCellVoxels, found = 0;
                var sites = new List<float2>();
                for (int gz = nC / 4; gz < 3 * nC / 4 && found < 3; gz += 5)
                for (int gx = nC / 4; gx < 3 * nC / 4 && found < 3; gx += 5)
                {
                    int i0 = gz * nC + gx;
                    float h0 = map.Height[i0];
                    if (h0 < 300f || (map.Water != null && map.Water[i0] > h0)) continue;
                    float bh = h0; int bx = gx, bz = gz;
                    for (int dz = -7; dz <= 7; dz++)
                    for (int dx = -7; dx <= 7; dx++)
                    {
                        float h = map.Height[(gz + dz) * nC + gx + dx];
                        if (h > bh) { bh = h; bx = gx + dx; bz = gz + dz; }
                    }
                    if (bh - h0 < 600f) continue;
                    float2 sp = new float2((gx + 0.5f) * cellV, (gz + 0.5f) * cellV);
                    bool near = false; foreach (var q in sites) if (math.distance(q, sp) < 10000f) near = true;
                    if (near) continue;
                    // EXACT ground (texel 1), not Ground()'s 0.8 m texel: at a cliff
                    // foot the coarse sample sat under the real voxels and put the
                    // eye inside rock (two black frames, 2026-10-01). The top solid
                    // voxel is S's height by the LOD-0 contract, so above it is air.
                    var exact = TerrainGen.ShapeField.Sample(in spec, map.View, in o, sp.x, sp.y, 1);
                    float g = math.max(math.max(exact.height, exact.water), spec.seaLevelY) + 1;
                    float2 tp = new float2((bx + 0.5f) * cellV, (bz + 0.5f) * cellV);
                    Look($"cliff_foot_{found + 1}", E(sp.x, g + 17f, sp.y), E(tp.x, spec.seaLevelY + h0 + 0.7f * (bh - h0), tp.y));
                    sites.Add(sp); found++;
                }
                L($"cliff feet found: {found}  (band placement: {(VoxelEngine.Streaming.StreamManager.TerrainAwareBand ? "terrain-aware" : "centred, -centredband")})");
            }
        }
        for (int gz = 0, done = 0; gz < (spec.worldSizeVoxels >> TerrainGen.Stamps.CELL_SHIFT) && done == 0; gz++)
        for (int gx = 0; gx < (spec.worldSizeVoxels >> TerrainGen.Stamps.CELL_SHIFT) && done == 0; gx++)
        {
            if (!TerrainGen.Stamps.CellStamp(in o, gx, gz, out var st) || st.kind != TerrainGen.Stamps.CrystalCluster) continue;
            if (Ground(st.pos.x, st.pos.y) < spec.seaLevelY + 300f) continue;
            // Clusters are 2.5-6 m prisms (2026-09-25; they were 40-100 m spires):
            // look from 40 m away, 15 m up.
            Look("crystal_cluster", E(st.pos.x - 350f, Ground(st.pos.x, st.pos.y) + 150f, st.pos.y - 200f), E(st.pos.x, Ground(st.pos.x, st.pos.y) + 20f, st.pos.y));
            done = 1;
        }

        foreach (var (n, pos, rot) in poses)
        {
            foreach (float hours in new[] { SunCycle.GoldenHourHours, 12f })
            {
                SunCycle.SetHours(hours);
                cam.transform.position = pos; cam.transform.rotation = rot;
                yield return Frames(30);
                for (int w = 0; w < 1500 && far.Busy; w++) yield return null;   // ring refills after the jump
                var sm = Phase4Bootstrapper.Streamer;
                int deficit = 0;
                if (world && sm != null)
                {
                    for (int w = 0; w < 3000 && sm.LoadDeficit() > 0; w++) yield return null;   // the voxel band streams in
                    deficit = sm.LoadDeficit();
                }
                yield return Frames(30);
                double ms = 0; for (int i = 0; i < 60; i++) { yield return null; ms += Time.unscaledDeltaTime * 1000.0; }
                string tn = hours == 12f ? "noon" : "golden";
                yield return CaptureTo($"{n}_{tn}");
                if (hours != 12f)
                {
                    RaymarchFeature.DebugViewOverride = (RaymarchFeature.DebugMode)9;
                    RaymarchFeature.UseDebugViewOverride = true;
                    yield return Frames(6);
                    yield return CaptureTo($"{n}_{tn}_band");
                    RaymarchFeature.UseDebugViewOverride = false;
                }
                string mirrorCheck = "";
                if (world && hours != 12f && Phase4Bootstrapper.Clipmap != null)
                {
                    // CPU vs GPU mirror, both ways: resident chunks that differ
                    // on the GPU, and non-resident box positions still drawn.
                    var vr = ClipmapValidator.ValidateRegion(Phase4Bootstrapper.Clipmap, Phase4Bootstrapper.Pool, Phase4Bootstrapper.Store, maxChunks: 4096, validateBrickBodies: false);
                    int stale = ClipmapValidator.CountStaleSlots(Phase4Bootstrapper.Clipmap, Phase4Bootstrapper.Store, out string ex);
                    mirrorCheck = $"  MIRROR lost {vr.mismatchesInCleanChunks} lag {vr.mismatchesInDirtyChunks} stale {stale}" + (ex != null ? $" [{ex}]" : "");
                }
                L($"{n,-24}{tn,7}  eye {pos}  {ms / 60.0:F2} ms (diagnostic)" + mirrorCheck +
                  (world && sm != null ? $"  band {sm.StreamLayer}+-{VoxelEngine.Streaming.StreamManager.V_LOAD_LAYERS}  resident {Phase4Bootstrapper.Store.ResidentCount}  " +
                                         $"deficit {deficit}  dense {Phase4Bootstrapper.Store.DenseBricksHeld}  lruEvictions {sm.LruEvictionsTotal}" : ""));
            }
        }
        if (world)
        {
            // The minimap, photographed once (its own render path is OnGUI, so
            // it only exists in a frame with the overlay on).
            foreach (var (n, pos, rot) in poses)
            {
                if (n != "eye_level_2") continue;
                SunCycle.SetHours(12f);
                cam.transform.position = pos; cam.transform.rotation = rot;
                Minimap.ForceShow = true;
                for (int w = 0; w < 600; w++) yield return null;
                yield return CaptureTo("minimap_check_noon");
                Minimap.ForceShow = false;
                cam.transform.position = pos - new Vector3(0f, 40f, 0f);   // underground: the depth slice
                Minimap.ForceShow = true;
                for (int w = 0; w < 120; w++) yield return null;
                yield return CaptureTo("minimap_check_underground");
                Minimap.ForceShow = false;
            }
        }
        L($"ring upkeep: {far.StripsApplied} strips, {far.FullRefills} refills, {far.BytesUploaded / 1048576.0:F1} MB uploaded");
        File.WriteAllText(Path.Combine(_folder, "farpreview.txt"), _log.ToString());
        Debug.Log("[farpreview]\n" + _log);
        yield return null;
        Application.Quit(0);
    }

    /// A ledge column near the island centre and an open-air eye 12 m from it
    /// at the gap's height (tg2 voxels). Not an iterator (holds the map view).
    private static bool TryLedgePose(TerrainGen.TerrainSpec spec, TerrainGen.WorldMap map, TerrainGen.Offsets o,
                                     out Vector3 eye, out Vector3 target)
    {
        eye = target = default;
        var view = map.View;
        float c = spec.worldSizeVoxels * 0.5f;
        for (int i = 0; i < 20000; i++)
        {
            float ang = i * 2.39996f, r = 800f + i * 1.2f;
            float px = c + math.cos(ang) * r, pz = c + math.sin(ang) * r;
            var col = TerrainGen.ShapeField.Sample(in spec, in view, in o, px + 0.5f, pz + 0.5f, 1);
            if (col.open3Hi <= col.open3Lo + 40) continue;
            float gapY = (col.open3Lo + col.open3Hi) * 0.5f;
            for (int k = 0; k < 8; k++)
            {
                float a = k * 0.785398f;
                float ex = px + math.cos(a) * 120f, ez = pz + math.sin(a) * 120f;
                var ec = TerrainGen.ShapeField.Sample(in spec, in view, in o, ex + 0.5f, ez + 0.5f, 1);
                if (ec.height + 5 >= gapY || ec.open3Hi > ec.open3Lo) continue;
                eye = new Vector3(ex, gapY + 5f, ez);
                target = new Vector3(px, gapY, pz);
                return true;
            }
        }
        return false;
    }

    /// The three EYE-LEVEL poses (tg2 voxels: eye, look-at), shared by
    /// -farpreview and -nearshimmer: dry sites at 25 / 45 / 65% of the island
    /// radius, 1.7 m above the ground, looking at the peak.
    private static List<(string n, float3 eye, float3 at)> Tg2EyeLevelPoses(TerrainGen.WorldMap map)
    {
        var spec = map.Spec;
        float c = spec.worldSizeVoxels * 0.5f;
        float2 pk = map.PeakPosition;
        var poses = new List<(string, float3, float3)>();
        float[] fr = { 0.25f, 0.45f, 0.65f };
        for (int k = 0; k < fr.Length; k++)
        {
            for (int a = 0; a < 36; a++)
            {
                float ang = a * 0.1745f + k * 2.1f;
                float2 q = new float2(c, c) + new float2(math.cos(ang), math.sin(ang)) * spec.islandRadius * fr[k];
                float g = TgGround(map, q.x, q.y);
                if (g < spec.seaLevelY + 150f) continue;
                poses.Add(($"eye_level_{k + 1}", new float3(q.x, g + 17f, q.y), new float3(pk.x, g + 17f + 0.05f * math.distance(q, pk), pk.y)));
                break;
            }
        }
        return poses;
    }

    /// Top-face Y (tg2 absolute voxels) of the sizeClass 2 column, or sea level.
    /// Not an iterator, so it may hold the map's pointer view.
    private static float TgGround(TerrainGen.WorldMap map, float gx, float gz)
    {
        var spec = map.Spec;
        var view = map.View;
        var o = TerrainGen.Offsets.For(spec);
        return math.max(TerrainGen.ShapeField.Sample(in spec, in view, in o, gx, gz, 8).height + 1, spec.seaLevelY + 1);
    }

    private IEnumerator CaptureTo(string name)
    {
        yield return new WaitForEndOfFrame();
        Texture2D t = ScreenCapture.CaptureScreenshotAsTexture();
        try { File.WriteAllBytes(Path.Combine(_folder, name + ".png"), t.EncodeToPNG()); }
        finally { Destroy(t); }
    }

    /// Top of the generated column in METRES (first air above it), or sea level.
    private static float GroundMetres(in ColumnSampler.State st, float xM, float zM)
    {
        ColumnSampler.SampleColumn(in st, (int)math.floor(xM * 10f), (int)math.floor(zM * 10f), out int h, out _);
        return math.max(h + 1, WorldGenConstants.SEA_LEVEL_VOXEL_Y + 1) * 0.1f;
    }

    /// True if the camera's voxel is above the generated column there. Cheap and
    /// generator-based (caves aside, which only ever ADD air).
    private static bool CameraInAir(in ColumnSampler.State st, Vector3 p)
    {
        ColumnSampler.SampleColumn(in st, (int)math.floor(p.x * 10f), (int)math.floor(p.z * 10f), out int h, out _);
        return p.y * 10f > h + 1;
    }
}
