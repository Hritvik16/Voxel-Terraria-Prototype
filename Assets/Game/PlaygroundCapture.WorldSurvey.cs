// Assets/Game/PlaygroundCapture.WorldSurvey.cs
//
// -lookreview -worldsurvey 1 (run-look-review.sh EXTRA=): TASK 173, owner 2026-11-05 "look fantastic first"
// (lighting -> WORLD GEN -> visuals). A LOOK, capture only: no generator, shader or default change, no frame
// times. Where does today's island (tg2, map v4) fall short? Midday (clock paused at noon), sun behind the
// camera where the pose allows, fluid suspended, HUD + minimap hidden, every pose shot at 1920x1080 twice in
// the same frame state: "native" (no upscaler, the ground truth of the generator's shapes) and "fsr3_540"
// (play's FSR 3 at the middle of its dyn range).
// POSES (all chosen from the generator's own data, so any seed gets the same KIND of survey):
//   start_walk / start_view  the play start: eye 1.6 m over the ground on the most open heading with the sun
//                            behind; and the same spot 160 m back, 35 m up
//   pair_<biome>_<landform>  every biome x landform pair in the world record with >= -wspaircells cells (300 =
//                            1.9 ha), up to -wspairs (18) by area: the cell deepest inside its landform AND its
//                            biome; eye 160 m back, 35 m over the higher of the two grounds, the sun-most-behind
//                            heading whose line of sight to the target is clear
//   feature_<k>              each signature feature of the record (FeatureList): from 1.6 x its radius + 300 m,
//                            80 m up; the canyon from inside, 25 m over its floor at a third of its path, along it
//   coast_cliff / coast_typ  land cells next to the sea: the highest relief 240 m inland, and the median one,
//                            seen from 220 m offshore looking inland
//   range_200up              the highest map cell, from 1.5 km, 200 m over the ground there
//   river                    the river cell with the largest size + valley depth (>= 10 m above the sea),
//                            150 m along the river's axis, 25 m over the water
//   cliff                    the Sheer (movement band 3) cell with the tallest drop over 3 cells whose face is
//                            sun-lit, from 70 m out at 40 % of its height
//   cave_mouth / cave_in     the nearest land CaveMouth stamp (Stamps.SurfaceCarve's tube): outside looking in,
//                            and 20 m down the tube looking deeper (re-found in the loaded voxels if solid)
//   tunnel / cavern          the most open enclosed air found by a 2 m store scan 40 m under the start's ground,
//                            and 200 m below sea level under the start (DetailField's tunnels / caverns); also shot "native_lit": undergroundAmbientFloor -wsfloor (1) for that arm only, a
//                            capture-only SHAPE light (play keeps a sealed cave black until a torch)
// Output: ws_<pose>_<arm>.png, worldsurvey_map.png (record cells: biome colour x hill shade, sea, rivers), and
// "WSPOSE name|eye x,y,z|target x,y,z|note" lines for tools/survey173.py (sheets, viewer, annotated map).
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Mirror;
using VoxelEngine.Rendering;
using VoxelEngine.Streaming;

public partial class PlaygroundCapture
{
    private struct WsPose
    {
        public string name, note;
        public Vector3 eye, at;
        public int scan;   // 0 = fixed; 1 = cave tube (verify air); 2 = underground scan (tunnel / cavern)
    }

    private IEnumerator WorldSurvey(FarField far, Camera cam, StreamManager sm, ShellStreamer sh, ChunkStore store, Vector3 centre, float wait)
    {
        var src = Phase4Bootstrapper.Streamer != null ? Phase4Bootstrapper.Streamer.TerrainGenSource : null;
        if (src == null || src.Map == null || src.Map.Record == null) { L("WORLD SURVEY: no tg2 source / record -- skipped"); yield break; }
        LightingDriver.ClockPaused = true;
        Playground.DebugSuspendFluidSim = true;
        Playground.DebugHideUi = true;
        float noon = SunCycle.NoonHours;
        SunCycle.SetHours(noon);
        float3 sun = SunCycle.DirectionAt(noon);
        float2 shz = math.normalizesafe(new float2(sun.x, sun.z));
        float2 away = -shz;   // a camera looking along `away` has the sun behind it
        string armArg = ArgValue("-wsarms");
        string[] arms = string.IsNullOrEmpty(armArg) ? new[] { "native", "fsr3_540" } : LadderArms(armArg);
        string only = ArgValue("-wsposes");
        var onlySet = string.IsNullOrEmpty(only) ? null : new HashSet<string>(only.Split(','));

        var spec = src.Spec; var map = src.Map; var rec = map.Record;
        int cells = rec.Cells; float cellM = rec.CellVoxels * 0.1f;
        float seaM = spec.seaLevelY * 0.1f;
        string[] bn = { "forest", "desert", "snow", "jungle", "savanna", "badlands", "b6", "b7" };
        L("");
        L($"WORLD SURVEY (task 173): seed {spec.seed}, map v{spec.mapVersion}, {cells}^2 record cells of {cellM:F0} m, sea {seaM:F1} m; " +
          $"noon {noon:F2} h, sun elevation {SunCycle.ElevationDegreesAt(noon):F0} deg, heading away from the sun ({away.x:F2}, {away.y:F2}); " +
          $"arms {string.Join(", ", arms)}; shell wait cap {wait:F0} s; start {centre:F1}");

        float Ground(float xM, float zM) => WalkEyeY(xM, zM, -1e9f) - 1.6f;   // generator top (ground, water or sea)
        Vector3 CellM(int cx, int cz) => new Vector3((cx + 0.5f) * cellM, 0f, (cz + 0.5f) * cellM);
        TerrainGen.RecordCell R(int cx, int cz) => rec.AtCell(cx, cz);
        float MapH(int cx, int cz) { cx = math.clamp(cx, 0, cells - 1); cz = math.clamp(cz, 0, cells - 1); return seaM + map.Height[cz * cells + cx] * 0.1f; }   // map heights are voxels RELATIVE to sea level
        bool Clear(Vector3 a, Vector3 b)
        {
            for (int i = 2; i < 48; i++)
            {
                Vector3 p = Vector3.Lerp(a, b, i / 48f);
                if (Ground(p.x, p.z) > p.y - 1f) return false;
            }
            return true;
        }
        var poses = new List<WsPose>();
        // A view of `at` from `back` m away, `up` m over the higher ground: the clear heading closest to sun-behind.
        void AddView(string name, Vector3 at, float back, float up, string note)
        {
            at.y = Ground(at.x, at.z);
            Vector3 best = Vector3.zero; float bestDot = -9f; bool any = false;
            for (int pass = 0; pass < 2 && !any; pass++)
            for (int j = 0; j < 16; j++)
            {
                float yaw = j * math.PI / 8f;
                float2 h = new float2(math.sin(yaw), math.cos(yaw));
                float d = math.dot(h, away);
                Vector3 e = at - new Vector3(h.x, 0f, h.y) * back;
                e.y = math.max(Ground(e.x, e.z), at.y) + up * (pass == 0 ? 1f : 2f);
                bool ok = Clear(e, at + Vector3.up * 3f);
                if (ok && d > bestDot) { bestDot = d; best = e; any = true; }
                if (!any && pass == 1 && j == 15) { best = e; }
            }
            if (!any)
            {
                float2 h = away;
                best = at - new Vector3(h.x, 0f, h.y) * back; best.y = math.max(Ground(best.x, best.z), at.y) + up * 2f;
                note += "; LINE OF SIGHT BLOCKED on every heading";
            }
            else note += $"; sun-behind dot {bestDot:F2}";
            poses.Add(new WsPose { name = name, eye = best, at = at + Vector3.up * 3f, note = note });
        }

        // ---------------- start ----------------
        {
            var e = new Vector3(centre.x, Ground(centre.x, centre.z) + 1.6f, centre.z);
            float bestF = -1f; float2 bh = away;
            for (int j = 0; j < 16; j++)
            {
                float yaw = j * math.PI / 8f;
                float2 h = new float2(math.sin(yaw), math.cos(yaw));
                if (math.dot(h, away) < 0f) continue;
                float f = CostFreeDistanceM(e, h, 1500f);
                if (f > bestF) { bestF = f; bh = h; }
            }
            var at = e + new Vector3(bh.x, 0f, bh.y) * 100f; at.y = e.y;
            var rc = R((int)(centre.x / cellM), (int)(centre.z / cellM));
            poses.Add(new WsPose { name = "start_walk", eye = e, at = at,
                note = $"the play start, eye 1.6 m, free distance {bestF:F0} m; {bn[rc.Biome & 7]} x {(rc.IsSea ? "sea" : TerrainGen.Regions.Name(rc.Archetype))}" });
            AddView("start_view", new Vector3(centre.x, 0f, centre.z), 160f, 35f, "the play start from 160 m back, 35 m up");
        }

        // ---------------- biome x landform pairs ----------------
        int minCells = ArgInt("-wspaircells", 300), maxPairs = ArgInt("-wspairs", 18);
        var count = new Dictionary<int, int>();
        var bestCell = new Dictionary<int, (int cx, int cz, int score)>();
        const int S = 25;   // 200 m
        for (int cz = 0; cz < cells; cz++)
        for (int cx = 0; cx < cells; cx++)
        {
            var c = R(cx, cz);
            if (c.IsSea) continue;
            int key = c.Biome * 16 + c.Archetype;
            count.TryGetValue(key, out int n); count[key] = n + 1;
            if (((cx | cz) & 1) != 0) continue;
            int same = 0;
            if (R(cx + S, cz).Biome == c.Biome) same++;
            if (R(cx - S, cz).Biome == c.Biome) same++;
            if (R(cx, cz + S).Biome == c.Biome) same++;
            if (R(cx, cz - S).Biome == c.Biome) same++;
            int score = c.Depth * 8 + same * 6 + (c.IsFeature ? 0 : 1);
            if (!bestCell.TryGetValue(key, out var b) || score > b.score) bestCell[key] = (cx, cz, score);
        }
        var keys = new List<int>(count.Keys);
        keys.Sort((a, b) => count[b].CompareTo(count[a]));
        int land = 0; foreach (var k in keys) land += count[k];
        L($"  biome x landform pairs on land ({land} cells = {land * cellM * cellM / 1e6f:F1} km^2):");
        int shot = 0;
        foreach (int key in keys)
        {
            int b = key >> 4, a = key & 15;
            string lf = TerrainGen.Regions.Name((byte)a);
            bool take = count[key] >= minCells && shot < maxPairs && bestCell.ContainsKey(key);
            L($"    {bn[b & 7],-9} x {lf,-18} {count[key],7} cells {100f * count[key] / land,5:F1} %{(take ? "  -> shot" : "")}");
            if (!take) continue;
            var bc = bestCell[key];
            var rc = R(bc.cx, bc.cz);
            AddView($"pair_{bn[b & 7]}_{lf.Replace(' ', '-')}", CellM(bc.cx, bc.cz), 160f, 35f,
                    $"{bn[b & 7]} x {lf}: {count[key]} cells ({100f * count[key] / land:F1} % of land); cell ({bc.cx},{bc.cz}) landform depth {rc.Depth * TerrainGen.WorldRecord.DEPTH_STEP_M} m, danger {rc.Danger}, band {rc.MoveBand}");
            shot++;
        }

        // ---------------- signature features ----------------
        for (int fi = 0; fi < rec.FeatureList.Length; fi++)
        {
            var f = rec.FeatureList[fi];
            if (f == null) continue;
            string fn = TerrainGen.Features.Name(f.kind).Replace(' ', '-') + "-" + fi;   // unique: a world can hold two canyons
            if (f.kind == TerrainGen.Features.GreatCanyon && f.path != null && f.path.Length > 12)
            {
                int i = f.path.Length / 3, j = math.min(f.path.Length - 1, i + 8);
                var p = new Vector3(f.path[i].x * 0.1f, 0f, f.path[i].y * 0.1f);
                var q = new Vector3(f.path[j].x * 0.1f, 0f, f.path[j].y * 0.1f);
                p.y = Ground(p.x, p.z) + 25f; q.y = Ground(q.x, q.z) + 5f;
                poses.Add(new WsPose { name = "feature_" + fn, eye = p, at = q, note = $"{fn} from inside: path point {i}/{f.path.Length}, 25 m over the floor, looking down the path" });
                AddView("feature_" + fn + "_out", new Vector3(f.centre.x * 0.1f, 0f, f.centre.y * 0.1f), 600f, 120f, $"{fn} from 600 m, 120 m up");
                continue;
            }
            float back = f.radiusM * 1.6f + 300f;
            AddView("feature_" + fn, new Vector3(f.centre.x * 0.1f, 0f, f.centre.y * 0.1f), back, 80f, $"{fn} (radius {f.radiusM:F0} m) from {back:F0} m, 80 m up");
        }

        // ---------------- coast ----------------
        {
            var coast = new List<(int cx, int cz, float2 inland, float relief)>();
            for (int cz = 4; cz < cells - 4; cz += 2)
            for (int cx = 4; cx < cells - 4; cx += 2)
            {
                if (R(cx, cz).IsSea) continue;
                float2 s = 0f;
                if (R(cx + 3, cz).IsSea) s += new float2(1, 0);
                if (R(cx - 3, cz).IsSea) s -= new float2(1, 0);
                if (R(cx, cz + 3).IsSea) s += new float2(0, 1);
                if (R(cx, cz - 3).IsSea) s -= new float2(0, 1);
                if (math.lengthsq(s) < 0.5f) continue;
                float2 inl = -math.normalize(s);
                if (math.dot(inl, away) < 0.3f) continue;   // looking inland with the sun behind
                float rel = MapH(cx + (int)math.round(inl.x * 30f), cz + (int)math.round(inl.y * 30f)) - seaM;
                coast.Add((cx, cz, inl, rel));
            }
            L($"  coast: {coast.Count} sun-lit coastal cells (step 2)");
            if (coast.Count > 0)
            {
                coast.Sort((a, b) => a.relief.CompareTo(b.relief));
                void Coast(string n, (int cx, int cz, float2 inland, float relief) c, float up)
                {
                    var at = CellM(c.cx, c.cz); at.y = Ground(at.x, at.z);
                    var e = at - new Vector3(c.inland.x, 0f, c.inland.y) * 220f; e.y = seaM + up;
                    poses.Add(new WsPose { name = n, eye = e, at = at + Vector3.up * 5f,
                        note = $"coast cell ({c.cx},{c.cz}), ground {c.relief:F0} m over the sea 240 m inland; from 220 m offshore, {up:F0} m over the sea" });
                }
                Coast("coast_cliff", coast[coast.Count - 1], 40f);
                Coast("coast_typ", coast[coast.Count / 2], 25f);
            }
        }

        // ---------------- mountain range ----------------
        {
            int bx = 0, bz = 0; float bh = -1e9f;
            for (int cz = 0; cz < cells; cz += 2)
            for (int cx = 0; cx < cells; cx += 2)
            {
                float h = MapH(cx, cz);
                if (h > bh) { bh = h; bx = cx; bz = cz; }
            }
            var pk = CellM(bx, bz); pk.y = Ground(pk.x, pk.z);
            var e = pk - new Vector3(away.x, 0f, away.y) * 1500f; e.y = Ground(e.x, e.z) + 200f;
            poses.Add(new WsPose { name = "range_200up", eye = e, at = pk, note = $"the highest map cell ({bx},{bz}) at {pk.y:F0} m, from 1.5 km, 200 m over the ground there" });
        }

        // ---------------- river ----------------
        if (map.FlowDir != null)
        {
            int rx = -1, rz = -1; float best = -1f;
            for (int cz = 8; cz < cells - 8; cz++)
            for (int cx = 8; cx < cells - 8; cx++)
            {
                int i = cz * cells + cx;
                if (map.WaterClass[i] != TerrainGen.Hydrology.RIVER) continue;
                float h = seaM + map.Height[i] * 0.1f;
                if (h < seaM + 10f) continue;
                float size = (map.FlowDir[i] >> TerrainGen.Rivers.FLOW_SIZE_SHIFT) & 0xFF;
                float valley = math.min(math.min(MapH(cx + 10, cz), MapH(cx - 10, cz)), math.min(MapH(cx, cz + 10), MapH(cx, cz - 10))) - h;
                float sc = size + math.clamp(valley, 0f, 60f);
                if (sc > best) { best = sc; rx = cx; rz = cz; }
            }
            if (rx >= 0)
            {
                double sxx = 0, szz = 0, sxz = 0;
                for (int dz = -8; dz <= 8; dz++)
                for (int dx = -8; dx <= 8; dx++)
                {
                    int i = (rz + dz) * cells + rx + dx;
                    if (map.WaterClass[i] != TerrainGen.Hydrology.RIVER) continue;
                    sxx += dx * dx; szz += dz * dz; sxz += dx * dz;
                }
                float ang = 0.5f * (float)Math.Atan2(2 * sxz, sxx - szz);
                float2 ax = new float2(math.cos(ang), math.sin(ang));
                if (math.dot(ax, away) < 0f) ax = -ax;
                var at = CellM(rx, rz); at.y = Ground(at.x, at.z);
                var e = at - new Vector3(ax.x, 0f, ax.y) * 150f; e.y = at.y + 25f;
                poses.Add(new WsPose { name = "river", eye = e, at = at, note = $"river cell ({rx},{rz}) score {best:F0} (size + valley m), 150 m along its axis, 25 m up" });
            }
        }

        // ---------------- cliff ----------------
        {
            int fx = -1, fz = -1; float best = 0f; float2 down = 0f;
            for (int cz = 4; cz < cells - 4; cz += 2)
            for (int cx = 4; cx < cells - 4; cx += 2)
            {
                var c = R(cx, cz);
                if (c.IsSea || c.MoveBand != 3) continue;
                float2 g = new float2(MapH(cx + 1, cz) - MapH(cx - 1, cz), MapH(cx, cz + 1) - MapH(cx, cz - 1));
                if (math.lengthsq(g) < 1e-4f) continue;
                float2 dn = -math.normalize(g);           // downhill = where the camera stands
                if (math.dot(-dn, away) < 0.2f) continue;   // the camera looks uphill at the face with the sun behind
                float drop = MapH(cx - (int)math.round(dn.x * 3f), cz - (int)math.round(dn.y * 3f)) - MapH(cx + (int)math.round(dn.x * 3f), cz + (int)math.round(dn.y * 3f));
                if (drop > best) { best = drop; fx = cx; fz = cz; down = dn; }
            }
            if (fx >= 0)
            {
                var at = CellM(fx, fz); at.y = Ground(at.x, at.z);
                var e = at + new Vector3(down.x, 0f, down.y) * 70f;
                float g0 = Ground(e.x, e.z);
                e.y = math.max(g0 + 1.6f, g0 + best * 0.4f);
                poses.Add(new WsPose { name = "cliff", eye = e, at = at, note = $"Sheer cell ({fx},{fz}), {best:F0} m drop over 48 m, from 70 m out" });
            }
        }

        // ---------------- caves ----------------
        {
            var o = src.O;
            float2 c0 = new float2(centre.x, centre.z) * 10f;
            int g0x = (int)math.floor(c0.x) >> TerrainGen.Stamps.CELL_SHIFT, g0z = (int)math.floor(c0.y) >> TerrainGen.Stamps.CELL_SHIFT;
            bool found = false; float bestD = float.MaxValue; TerrainGen.Stamps.Stamp bs = default; int bg = 0;
            for (int gz = g0z - 8; gz <= g0z + 8; gz++)
            for (int gx = g0x - 8; gx <= g0x + 8; gx++)
            {
                if (!TerrainGen.Stamps.CellStamp(in o, gx, gz, out var s) || s.kind != TerrainGen.Stamps.CaveMouth) continue;
                float gm = Ground(s.pos.x * 0.1f, s.pos.y * 0.1f);
                if (gm < seaM + 3.5f) continue;   // Stamps: dry land only (seaLevel + 30 voxels)
                float d = math.distance(s.pos, c0);
                if (d < bestD) { bestD = d; bs = s; bg = (int)math.round(gm * 10f); found = true; }
            }
            if (found)
            {
                float r = 45f + 25f * bs.a;
                float3 e0 = new float3(bs.pos.x, bg + r * 0.35f, bs.pos.y);
                float3 d = math.normalize(new float3(bs.dir.x, -(0.6f + 0.25f * bs.b), bs.dir.y));
                var mouth = new Vector3(e0.x, e0.y, e0.z) * 0.1f;
                var outE = mouth - new Vector3(bs.dir.x, 0f, bs.dir.y) * 25f; outE.y = Ground(outE.x, outE.z) + 3f;
                poses.Add(new WsPose { name = "cave_mouth", eye = outE, at = mouth, note = $"nearest land CaveMouth stamp, {bestD * 0.1f:F0} m from the start, tube radius {r * 0.1f:F1} m; 25 m outside looking in" });
                float3 inV = e0 + d * 200f, deeper = e0 + d * 400f;
                poses.Add(new WsPose { name = "cave_in", eye = new Vector3(inV.x, inV.y, inV.z) * 0.1f, at = new Vector3(deeper.x, deeper.y, deeper.z) * 0.1f, scan = 1,
                    note = "20 m down the CaveMouth tube, looking deeper" });
            }
            else L("  cave: no land CaveMouth stamp within 8 stamp cells of the start");
            float gS = Ground(centre.x, centre.z);
            poses.Add(new WsPose { name = "tunnel", eye = new Vector3(centre.x, gS - 40f, centre.z), at = new Vector3(centre.x + 10f, gS - 40f, centre.z), scan = 2,
                note = "the most open enclosed air 40 m under the start's ground (2 m store scan)" });
            poses.Add(new WsPose { name = "cavern", eye = new Vector3(centre.x, seaM - 200f, centre.z), at = new Vector3(centre.x + 10f, seaM - 200f, centre.z), scan = 2,
                note = "the most open enclosed air 200 m below sea level under the start (2 m store scan)" });
        }

        // ---------------- the map ----------------
        WriteSurveyMap(map, spec, Path.Combine(_folder, "worldsurvey_map.png"));
        L($"  {poses.Count} poses; map worldsurvey_map.png ({cells} px, +x right, +z UP; a pixel = one {cellM:F0} m cell)");

        // ---------------- shoot ----------------
        foreach (var p0 in poses)
        {
            if (onlySet != null && !onlySet.Contains(p0.name) && !onlySet.Contains(p0.name.Split('_')[0])) continue;   // a name or a kind (the name up to its first "_")
            var p = p0;
            float t0 = Time.realtimeSinceStartup;
            Phase4Bootstrapper.DebugStreamCentre = p.eye;
            cam.transform.SetPositionAndRotation(p.eye, Quaternion.LookRotation((p.at - p.eye).normalized, Vector3.up));
            ApplyCompareArm(arms[arms.Length - 1]);
            cam.ResetProjectionMatrix();
            yield return NearSettle(far, sm);
            if (p.scan != 0)
            {
                bool inAir = store.GetVoxel(CoordMath.WorldToVoxel(new float3(p.eye.x, p.eye.y, p.eye.z))) == 0;
                if (p.scan == 2 || !inAir)
                {
                    var air = FindOpenAir(store, p.eye, p.scan == 2 ? 40 : 12, p.scan == 2 ? 16 : 8);
                    if (air.ok)
                    {
                        p.eye = air.eye; p.at = air.at;
                        p.note += $"; scan: eye {air.eye:F1}, free {air.free:F0} m ahead, ceiling {air.ceil:F1} m";
                        Phase4Bootstrapper.DebugStreamCentre = p.eye;
                        cam.transform.SetPositionAndRotation(p.eye, Quaternion.LookRotation((p.at - p.eye).normalized, Vector3.up));
                        yield return NearSettle(far, sm);
                    }
                    else p.note += "; scan: NO enclosed open air found (shot as placed)";
                }
            }
            yield return FsrLiveShellWait(sh, wait);
            var rot = Quaternion.LookRotation((p.at - p.eye).normalized, Vector3.up);
            var poseArms = new List<string>(arms);
            if (p.scan == 2) poseArms.Add("native_lit");   // a sealed cave is black at noon: add a SHAPE light (capture only)
            foreach (string arm in poseArms)
            {
                bool lit = arm == "native_lit";
                float floor0 = LightingConfig.Active.undergroundAmbientFloor;
                if (lit) LightingConfig.Active.undergroundAmbientFloor = ArgFloat("-wsfloor", 1f);
                ApplyCompareArm(lit ? "native" : arm);
                cam.ResetProjectionMatrix();
                cam.transform.SetPositionAndRotation(p.eye, rot);
                yield return Frames(60);
                yield return CaptureTo($"ws_{p.name}_{arm}");
                LightingConfig.Active.undergroundAmbientFloor = floor0;
            }
            float pitch = rot.eulerAngles.x > 180f ? rot.eulerAngles.x - 360f : rot.eulerAngles.x;
            L($"WSPOSE {p.name}|{p.eye.x:F1},{p.eye.y:F1},{p.eye.z:F1}|{p.at.x:F1},{p.at.y:F1},{p.at.z:F1}|{p.note}");
            L($"  {p.name}: yaw {rot.eulerAngles.y:F0} pitch {-pitch:F0} deg, distance {Vector3.Distance(p.eye, p.at):F0} m, {Time.realtimeSinceStartup - t0:F0} s");
        }
        Phase4Bootstrapper.DebugStreamCentre = null;
    }

    /// The most open enclosed air near `c` in the loaded store (2 m grid, +-rH m across, +-rV m up/down):
    /// an air voxel with a floor within 4 m below and a ceiling within 30 m above, scored by the free distance
    /// along its 8 level headings. The eye goes 1.6 m over that floor, looking along the freest heading.
    private static (bool ok, Vector3 eye, Vector3 at, float free, float ceil) FindOpenAir(ChunkStore store, Vector3 c, int rH, int rV)
    {
        float Free(int3 v, int3 step, int max)
        {
            for (int i = 1; i <= max; i++) if (store.GetVoxel(v + step * i) != 0) return i * 0.1f;
            return -1f;
        }
        int3 c0 = CoordMath.WorldToVoxel(new float3(c.x, c.y, c.z));
        float bestS = -1f; (bool, Vector3, Vector3, float, float) best = (false, c, c, 0f, 0f);
        for (int dy = -rV * 10; dy <= rV * 10; dy += 20)
        for (int dz = -rH * 10; dz <= rH * 10; dz += 20)
        for (int dx = -rH * 10; dx <= rH * 10; dx += 20)
        {
            int3 v = c0 + new int3(dx, dy, dz);
            if (store.GetVoxel(v) != 0) continue;
            float down = Free(v, new int3(0, -1, 0), 40);
            if (down < 0f) continue;
            float up = Free(v, new int3(0, 1, 0), 300);
            if (up < 0f) continue;
            if (up + down < 2.5f) continue;   // room to stand: no crawl spaces
            int3 f = v - new int3(0, (int)math.round(down * 10f) - 16, 0);   // 1.6 m over the floor
            if (store.GetVoxel(f) != 0) continue;
            float sum = 0f, bf = 0f; int3 bd = new int3(1, 0, 0);
            for (int k = 0; k < 8; k++)
            {
                int3 st = new int3((int)math.round(math.cos(k * math.PI / 4f)), 0, (int)math.round(math.sin(k * math.PI / 4f)));
                float fr = Free(f, st, 400) * (k % 2 == 1 ? 1.414f : 1f);
                if (fr < 0f) fr = 40f;
                sum += fr;
                if (fr > bf) { bf = fr; bd = st; }
            }
            float sc = sum + bf;
            if (sc > bestS)
            {
                bestS = sc;
                var eye = new Vector3(f.x + 0.5f, f.y + 0.5f, f.z + 0.5f) * 0.1f;
                var at = eye + new Vector3(bd.x, -0.15f, bd.z).normalized * 10f;
                best = (true, eye, at, bf, up + down - 1.6f);
            }
        }
        return best;
    }

    /// Record cells as an image: sea (depth-shaded blue), land in its biome's colour x a NW hill shade, lakes and
    /// rivers light blue. Pixel (cx, cz) = record cell; Unity writes row 0 at the bottom, so +z is UP.
    private static void WriteSurveyMap(TerrainGen.WorldMap map, TerrainGen.TerrainSpec spec, string path)
    {
        var rec = map.Record; int n = rec.Cells;
        var biomeCol = new Color32[] {
            new Color32(86, 140, 70, 255), new Color32(220, 196, 130, 255), new Color32(235, 240, 245, 255), new Color32(40, 110, 60, 255),
            new Color32(190, 180, 90, 255), new Color32(190, 110, 70, 255), new Color32(150, 150, 150, 255), new Color32(150, 150, 150, 255) };
        var px = new Color32[n * n];
        float sea = 0f;   // map heights are relative to sea level
        for (int cz = 0; cz < n; cz++)
        for (int cx = 0; cx < n; cx++)
        {
            int i = cz * n + cx;
            var c = rec.Data[i];
            float h = map.Height[i];
            if (c.IsSea)
            {
                float d = math.saturate((sea - h) / 1500f);
                px[i] = new Color32((byte)(40 - 25 * d), (byte)(90 - 50 * d), (byte)(150 - 50 * d), 255);
                continue;
            }
            if (map.WaterClass[i] != TerrainGen.Hydrology.DRY) { px[i] = new Color32(90, 170, 230, 255); continue; }
            float hx = map.Height[cz * n + math.min(cx + 1, n - 1)] - map.Height[cz * n + math.max(cx - 1, 0)];
            float hz = map.Height[math.min(cz + 1, n - 1) * n + cx] - map.Height[math.max(cz - 1, 0) * n + cx];
            float3 nrm = math.normalize(new float3(-hx, 160f, -hz));
            float shade = math.saturate(math.dot(nrm, math.normalize(new float3(-1f, 1.2f, 1f))));
            float alt = math.saturate((h - sea) / 6000f);
            var b = biomeCol[c.Biome & 7];
            float k = (0.45f + 0.65f * shade) * (0.85f + 0.3f * alt);
            px[i] = new Color32((byte)math.min(255f, b.r * k), (byte)math.min(255f, b.g * k), (byte)math.min(255f, b.b * k), 255);
        }
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
        try { tex.SetPixels32(px); tex.Apply(); File.WriteAllBytes(path, tex.EncodeToPNG()); }
        finally { UnityEngine.Object.Destroy(tex); }
    }
}
