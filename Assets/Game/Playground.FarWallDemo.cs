// Assets/Game/Playground.FarWallDemo.cs
//
// -farwall (task 93a, Amendment 8.15 §5 "Live"): a block-built tower ~100 m out
// -- past shell 0's hand-over (69 m on High), inside the CPU's 128 m -- before
// and after edit summaries. Needs -shells 1. Noon, clock paused, the sun behind
// the camera. Edit summaries start OFF (the pre-93a picture); a 6 x 25 x 6 m
// stone tower goes in through EditService.SetBox (SetVoxel edits, NOT an op).
// TODAY TIER 0 DRAWS IT at 100 m (the shells only run where tier 0's march
// misses, and the GPU mirror box, +-204 m, holds every resident chunk), so the
// near pair is the same picture; the summary shows from a FAR pose 230 m out,
// where the chunks are no longer resident and only a shell can draw the tower:
// near_before, far_before (OFF), back near (the chunks reload from D.1),
// summaries ON, near_after, far_after -- each with a -shellsdebug tint shot.
// ./run-play.sh EXTRA="-farwall -shells 1 -shellworkers 3". Shots:
// persistentDataPath/FarWallDemo/.
// -farwallsaved (task 108, 8.15 E2): summaries ON throughout; the tower goes in,
// near shot, then the FAR pose once every tower chunk is evicted (its D.1 and
// D.5 saved): saved summaries switched OFF (the covering shell chunks redraw:
// the generator look, what 93a left after a regeneration) and back ON (redrawn
// from D.5: the tower). Shots farwall_near, farwall_saved_off, farwall_saved_on.

using System.Collections;
using Unity.Mathematics;
using UnityEngine;

public partial class Playground
{
    private IEnumerator FarWallDemo()
    {
        float t0 = Time.unscaledTime;
        while (Time.unscaledTime - t0 < 25f || _player == null || !_player.Ready) yield return null;
        var shells = Phase4Bootstrapper.Shells;
        if (shells == null) { Debug.Log("[FarWall] shells OFF (pass -shells 1): nothing to show"); Application.Quit(); yield break; }
        _showHelp = false;
        _demoHideState = true;
        DebugHideUi = true;
        SunCycle.SetHours(SunCycle.NoonHours);
        VoxelEngine.Rendering.LightingDriver.ClockPaused = true;
        string dir = System.IO.Path.Combine(Application.persistentDataPath, "FarWallDemo");
        System.IO.Directory.CreateDirectory(dir);
        if (_cam == null && Camera.main != null) _cam = Camera.main.GetComponent<PlaygroundFlyCamera>();
        _mode = MoveMode.Fly;
        bool savedMode = HasArg("-farwallsaved");
        shells.SummariesEnabled = savedMode;
        var camT = Camera.main.transform;

        // The tower: ~100 m from the start, as far AWAY from the sun as a site allows (so the
        // camera has the sun behind it): its ground below the eye and every chunk of it
        // resident (the CPU keeps +-3 layers around the eye; edits elsewhere are refused).
        float dist = TryArgFloat("-farwalldist", out float fd) ? fd : 100f;
        float2 away = -math.normalizesafe(SunCycle.Direction.xz, new float2(0f, 1f));
        float awayYaw = math.atan2(away.x, away.y);
        int3 f = CoordMath.WorldToVoxel(_player.Motor.PositionM);
        var src = Phase4Bootstrapper.Streamer.TerrainGenSource;
        var spec = src.Spec; var view = src.Map.View; var o = src.O;
        int wx = 0, wz = 0, ground = 0;
        const int W = 60, H = 250;   // 6 m square, 25 m tall
        bool found = false;
        for (int h = 0; h <= 12 && !found; h++)
        for (int dd = 0; dd < 3 && !found; dd++)
        {
            float yr = awayYaw + ((h & 1) == 0 ? 1 : -1) * ((h + 1) >> 1) * math.radians(15f);
            float r = (dist + dd * 8f) * 10f;
            wx = f.x + (int)math.round(math.sin(yr) * r) - W / 2; wz = f.z + (int)math.round(math.cos(yr) * r) - W / 2;
            int gmax = int.MinValue; bool wet = false;
            for (int z = wz; z < wz + W; z += 10)
            for (int x = wx; x < wx + W; x += 10)
            {
                var col = TerrainGen.ShapeField.Sample(in spec, in view, in o, x, z, 1);
                gmax = math.max(gmax, col.height); wet |= col.water > col.height;
            }
            ground = gmax;
            if (wet || ground > f.y + 50 || ground < f.y - 250) continue;
            found = _store.IsResident(CoordMath.VoxelToChunk(new int3(wx, ground - 20, wz))) &&
                    _store.IsResident(CoordMath.VoxelToChunk(new int3(wx + W - 1, ground + H, wz + W - 1)));
            if (found) Debug.Log($"[FarWall] site: heading {h} ({math.degrees(yr - awayYaw):F0} deg from the sun-away heading), {r * 0.1f:F0} m");
        }
        if (!found) Debug.Log("[FarWall] NO SITE with its ground below the eye and resident: using the last one tried");
        int3 lo = new int3(wx, ground - 20, wz), hi = new int3(wx + W - 1, ground + H, wz + W - 1);
        float3 top = new float3(wx + W / 2, ground + H / 2, wz + W / 2) * 0.1f;
        float3 near = new float3(f.x * 0.1f, math.max(f.y * 0.1f + 15f, SurfaceYAt(f.x, f.z) * 0.1f + 15f), f.z * 0.1f);
        // FAR pose: on the tower-to-camera line, 230 m from the tower, 60 m above its top --
        // past the CPU's 128 m (the tower's chunks are evicted, so tier 0 cannot draw it)
        // and inside shell 2's band (138-276 m on High).
        float2 back = math.normalizesafe(near.xz - top.xz, new float2(0f, 1f));
        float3 far = new float3(top.x + back.x * 230f, top.y + H * 0.05f + 60f, top.z + back.y * 230f);
        float3 at = near;
        IEnumerator Hold(int frames)
        {
            float3 look = top - at;
            for (int i = 0; i < frames; i++)
            {
                camT.position = at;
                _cam?.SetLook(math.degrees(math.atan2(look.x, look.z)), -math.degrees(math.atan2(look.y, math.length(look.xz))));
                yield return null;
            }
        }
        IEnumerator Settle(float maxS)
        {
            float t1 = Time.unscaledTime;
            yield return Hold(30);
            while (Time.unscaledTime - t1 < maxS && !shells.Idle) yield return Hold(1);
            yield return Hold(30);
        }
        IEnumerator Shots(string tag)
        {
            Debug.Log($"[FarWall] {tag}: camera {at} m, {math.length(top - at):F0} m from the tower's middle; " + Census());
            yield return DemoShot(dir, "farwall_" + tag);
            RaymarchFeature.ShellTint = true;
            yield return Hold(10);
            yield return DemoShot(dir, "farwall_" + tag + "_tint");
            RaymarchFeature.ShellTint = false;
        }
        int3 c0 = CoordMath.VoxelToChunk(lo), c1 = CoordMath.VoxelToChunk(hi);
        int TowerResident()
        {
            int n = 0;
            for (int cz = c0.z; cz <= c1.z; cz++) for (int cy = c0.y; cy <= c1.y; cy++) for (int cx = c0.x; cx <= c1.x; cx++)
                if (_store.IsResident(new int3(cx, cy, cz))) n++;
            return n;
        }
        string Census()
        {
            string s = $"tower chunks resident {TowerResident()}/{(c1.x - c0.x + 1) * (c1.y - c0.y + 1) * (c1.z - c0.z + 1)}; " +
                       $"summaries {(shells.SummariesEnabled ? "ON" : "OFF")}, saved {(shells.SavedSummariesEnabled ? "ON" : "OFF")} held {shells.SavedHeld}: notices {shells.SummaryNotices}, tracked {shells.EditedChunksTracked}, " +
                       $"built {shells.SummariesBuilt}, held {shells.SummariesHeld} ({shells.SummaryBytes} B), refused {shells.SummariesRefused}, " +
                       $"dropped {shells.SummariesDropped}, summary redraws {shells.SummaryRedrawsQueued}, redrawn {shells.Redrawn}, pending {shells.RedrawsPending}, " +
                       $"main ms {shells.SummaryBuildMsMain:F1}; covering shell chunks held:";
            for (int k = shells.FirstShell; k <= shells.LastShell; k++)
            {
                int held = 0, want = 0;
                for (int cz = c0.z; cz <= c1.z; cz++) for (int cy = c0.y; cy <= c1.y; cy++) for (int cx = c0.x; cx <= c1.x; cx++)
                {
                    int3 sc = new int3(cx, cy, cz) >> k;
                    if (shells.Wanted(k, sc)) { want++; if (shells.Store(k).Holds(sc)) held++; }
                }
                if (want > 0) s += $" shell {k} {held}/{want};";
            }
            s += " missing per shell:";
            for (int k = shells.FirstShell; k <= shells.LastShell; k++) s += $" {k}:{shells.Missing(k)}";
            return s;
        }

        // 1. NEAR, summaries off: the tower goes in (SetVoxel edits).
        yield return Settle(60f);
        int written = _edits.SetBox(lo, hi, Materials.Stone);
        Debug.Log($"[FarWall] tower {lo}..{hi} (voxels), chunks {c0}..{c1}, {written} voxels written by SetVoxel; far pose {far} m");
        yield return Hold(60);
        if (savedMode)
        {
            yield return Settle(30f);
            yield return Shots("near");
            at = far;
            float te = Time.unscaledTime;
            while (Time.unscaledTime - te < 60f && TowerResident() > 0) yield return Hold(1);
            yield return Settle(45f);
            var saved = Phase4Bootstrapper.Streamer.SavedSummaries;
            Debug.Log($"[FarWall] saved: {TowerResident()} tower chunks resident after {Time.unscaledTime - te:F1} s; D.5 written {saved.Written} " +
                      $"({saved.BytesWritten} B; their D.1 {saved.DeltaBytesSeen} B), build {saved.BuildMsTotal:F1} ms total, write {saved.WriteMsTotal:F1} ms, " +
                      $"rejected {saved.Rejected}, store RAM {saved.RamBytes} B (index {saved.Count}, LRU {saved.LruBytes} B); shells hold {shells.SavedHeld} saved");
            shells.SavedSummariesEnabled = false;
            yield return Settle(45f);
            yield return Shots("saved_off");
            shells.SavedSummariesEnabled = true;
            yield return Settle(45f);
            yield return Shots("saved_on");
            Application.Quit();
            yield break;
        }
        yield return Shots("near_before");
        // 2. FAR, summaries off: the tower's chunks leave the CPU; the shells never had it.
        at = far;
        yield return Settle(45f);
        yield return Shots("far_before");
        // 3. NEAR again: the chunks reload from their D.1 (edit marks from its records),
        // summaries on -> built -> the covering shell chunks redrawn.
        at = near;
        float tr = Time.unscaledTime;
        while (Time.unscaledTime - tr < 40f && TowerResident() < (c1.x - c0.x + 1) * (c1.y - c0.y + 1) * (c1.z - c0.z + 1)) yield return Hold(1);
        shells.SummariesEnabled = true;
        float ts = Time.unscaledTime;
        int frames = 0;
        long built0 = shells.SummariesBuilt, redraws0 = shells.SummaryRedrawsQueued;
        do { yield return Hold(1); frames++; }
        while (Time.unscaledTime - ts < 30f && (shells.SummariesBuilt == built0 || shells.SummaryRedrawsQueued == redraws0 || shells.RedrawsPending > 0));
        Debug.Log($"[FarWall] reload after {ts - tr:F1} s; summaries built and redrawn {frames} frames ({Time.unscaledTime - ts:F2} s) after turning them on");
        yield return Settle(20f);
        yield return Shots("near_after");
        // 4. FAR again: the chunks are evicted (summaries dropped), the shell 2 chunk
        // drawn with the summary keeps it until it is generated again.
        at = far;
        yield return Settle(45f);
        yield return Shots("far_after");
        Application.Quit();
    }
}
