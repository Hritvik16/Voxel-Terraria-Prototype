// Assets/Game/Playground.FarOpDemo.cs
//
// -farop (task 81, engine step 13a): ONE picture of a far edit. Phase4Bootstrapper
// records a 20 m sphere-carve op ~300 m from the spawn before anything streams
// (RecordFarOp); the shells generate that area with the op applied
// (GenerateShellChunk + EditOpApply). This coroutine flies the camera 200 m from
// the crater, 300 m above it (~360 m away), looking at it, at noon, waits for the shells to
// fill, and shoots it plain and with the shell tint. Needs -shells 1.
// An INTERACTIVE run: ./run-play.sh EXTRA="-farop -shells 1". Shots:
// persistentDataPath/FarOpDemo/. Then quits.
//
// -farop -faroplate (task 82, engine step 13b): the op is recorded AFTER the
// shells have filled, so what shows is the REDRAW. Shots, before and after the
// op: pose A ~360 m (a shell draws it) and pose B ~3 km away (1.5 km out, 2.6 km up) with
// shell drawing off (the far band draws it), plus tint shots. The log gives the
// frames from Record to the far band's patches going up and to the shells'
// redraws being stored.

using System.Collections;
using Unity.Mathematics;
using UnityEngine;

public partial class Playground
{
    private IEnumerator FarOpDemo()
    {
        float t0 = Time.unscaledTime;
        while (Time.unscaledTime - t0 < 25f || _player == null || !_player.Ready) yield return null;
        var centre = Phase4Bootstrapper.FarOpCentre;   // -faroplate: the site, op not yet recorded
        var shells = Phase4Bootstrapper.Shells;
        if (centre == null || shells == null)
        {
            Debug.Log($"[FarOp] nothing to show: op {(centre == null ? "not recorded" : "recorded")}, shells {(shells == null ? "OFF (pass -shells 1)" : "on")}");
            Application.Quit(); yield break;
        }
        _showHelp = false;
        _demoHideState = true;
        SunCycle.SetHours(SunCycle.NoonHours);
        VoxelEngine.Rendering.LightingDriver.ClockPaused = true;
        string dir = System.IO.Path.Combine(Application.persistentDataPath, "FarOpDemo");
        System.IO.Directory.CreateDirectory(dir);
        if (_cam == null && Camera.main != null) _cam = Camera.main.GetComponent<PlaygroundFlyCamera>();
        _mode = MoveMode.Fly;

        var src = Phase4Bootstrapper.Streamer.TerrainGenSource;
        var spec = src.Spec; var view = src.Map.View; var o = src.O;
        var camT = Camera.main.transform;
        float3 tgt = (float3)centre.Value * 0.1f;
        float3 here = camT.position;
        float2 d = math.normalizesafe(tgt.xz - here.xz, new float2(0f, 1f));
        float2 camXZ = tgt.xz - d * 200f;   // 200 m out, 300 m up: ~56 deg down, over any ridge between
        var ground = TerrainGen.ShapeField.Sample(in spec, in view, in o, camXZ.x * 10f, camXZ.y * 10f, 1);
        float camY = math.max(tgt.y + 300f, math.max(ground.height, ground.water) * 0.1f + 30f);
        float3 camM = new float3(camXZ.x, camY, camXZ.y);
        float3 look = tgt - camM;
        camT.position = camM;
        _cam?.SetLook(math.degrees(math.atan2(look.x, look.z)), -math.degrees(math.atan2(look.y, math.length(look.xz))));
        if (Phase4Bootstrapper.FarOpLate != null)
        {
            yield return FarOpLateShots(dir, camM, tgt, d, shells);
            Application.Quit(); yield break;
        }

        // Let the shells fill around the new pose (up to -shellwait s, default 75).
        float tw = Time.unscaledTime, wait = TryArgFloat("-shellwait", out float sw) ? sw : 75f;
        for (int i = 0; i < 30; i++) { camT.position = camM; yield return null; }
        while (Time.unscaledTime - tw < wait && !shells.Idle) { camT.position = camM; yield return null; }
        for (int i = 0; i < 60; i++) { camT.position = camM; yield return null; }

        string held = "";
        for (int k = shells.FirstShell; k <= shells.LastShell; k++)
        {
            int3 cs = centre.Value >> (7 + k);
            if (shells.Store(k).Holds(cs)) held += $" {k}";
            Debug.Log($"[FarOp] shell {k} chunk {cs}: wanted {shells.Wanted(k, cs)} held {shells.Store(k).Holds(cs)} refused {shells.IsRefused(k, cs)} " +
                      $"in flight {shells.IsInFlight(k, cs)} full {shells.ShellFull(k)} missing {shells.Missing(k)}");
        }
        Debug.Log($"[FarOp] camera {camM} m, crater {tgt} m, {math.length(look):F0} m away; shells idle {shells.Idle} after {Time.unscaledTime - tw:F0} s; " +
                  $"shells holding the crater's chunk:{(held.Length == 0 ? " none" : held)}; ops in the log {Phase4Bootstrapper.Streamer.Ops.Count}");
        yield return DemoShot(dir, "farop_crater");
        RaymarchFeature.ShellTint = true;
        for (int i = 0; i < 10; i++) { camT.position = camM; yield return null; }
        yield return DemoShot(dir, "farop_crater_tint");
        RaymarchFeature.ShellTint = false;
        Application.Quit();
    }

    /// Task 82: B before (+ tint), then pose A until a shell holds the crater's chunk
    /// and the shells are idle, A before, record the op at A and count frames to
    /// visible, A after (+ tint), B after (+ tint).
    private IEnumerator FarOpLateShots(string dir, float3 camA, float3 tgt, float2 d, VoxelEngine.Mirror.ShellStreamer shells)
    {
        var camT = Camera.main.transform;
        var src = Phase4Bootstrapper.Streamer.TerrainGenSource;
        var spec = src.Spec; var view = src.Map.View; var o = src.O;
        // 1.5 km out, 2.6 km up: 3 km away, ~60 deg down, so no ridge hides the crater (18 deg did).
        float2 bXZ = tgt.xz - d * 1500f;
        var gB = TerrainGen.ShapeField.Sample(in spec, in view, in o, bXZ.x * 10f, bXZ.y * 10f, 1);
        float3 camB = new float3(bXZ.x, math.max(tgt.y + 2600f, math.max(gB.height, gB.water) * 0.1f + 50f), bXZ.y);
        var far = VoxelEngine.Rendering.FarField.Active;

        IEnumerator Pose(float3 at, int frames)
        {
            float3 look = tgt - at;
            camT.position = at;
            _cam?.SetLook(math.degrees(math.atan2(look.x, look.z)), -math.degrees(math.atan2(look.y, math.length(look.xz))));
            for (int i = 0; i < frames; i++) { camT.position = at; yield return null; }
        }
        IEnumerator ShotB(string tag)
        {
            // Shell drawing off: past the cascades the far band is what draws here.
            RaymarchFeature.DrawShells = false;
            yield return Pose(camB, 20);
            yield return DemoShot(dir, $"farop_B_3km_farband_{tag}");
            RaymarchFeature.DrawShells = true;
            RaymarchFeature.ShellTint = true;
            yield return Pose(camB, 20);
            yield return DemoShot(dir, $"farop_B_3km_tint_{tag}");
            RaymarchFeature.ShellTint = false;
        }

        // B before; then A, where the shells must hold the crater's chunk before the op lands.
        yield return ShotB("before");
        yield return Pose(camA, 30);
        var op = Phase4Bootstrapper.FarOpLate.Value;
        bool HeldAnywhere()
        {
            for (int k = shells.FirstShell; k <= shells.LastShell; k++)
                if (shells.Store(k).Holds(op.centreVoxel >> (7 + k))) return true;
            return false;
        }
        float tw = Time.unscaledTime, wait = TryArgFloat("-shellwait", out float sw) ? sw : 75f;
        while (Time.unscaledTime - tw < wait && (!shells.Idle || !HeldAnywhere())) { camT.position = camA; yield return null; }
        Debug.Log($"[FarOp] pose A after {Time.unscaledTime - tw:F0} s: shells idle {shells.Idle}, crater chunk held {HeldAnywhere()}");
        for (int i = 0; i < 30; i++) { camT.position = camA; yield return null; }
        yield return DemoShot(dir, "farop_A_360m_before");

        // Record, then count frames until the far band's patches are up and the shells' redraws stored.
        // DemoShot resumes at the END of a frame: the first LateUpdate to see the op is the
        // next frame's, so nothing is checked before frame 2.
        long redrawn0 = shells.Redrawn, patches0 = far != null ? far.PatchesApplied : 0;
        uint seq = new EditOps(Phase4Bootstrapper.Streamer.Ops, null, null).Record(op);
        int f = 0, farFrames = -1, shellFrames = -1;
        float t0 = Time.unscaledTime, farS = -1f, shellS = -1f;
        while (f < 3000 && (farFrames < 0 || shellFrames < 0))
        {
            camT.position = camA; yield return null; f++;
            if (f < 2) continue;
            // The far band's last patch was issued this frame; its scatter runs in this frame's render.
            if (farFrames < 0 && (far == null || (far.PatchesPending == 0 && far.PendingScatters.Count == 0)))
            { farFrames = f; farS = Time.unscaledTime - t0; }
            if (shellFrames < 0 && shells.RedrawsPending == 0) { shellFrames = f; shellS = Time.unscaledTime - t0; }
        }
        Debug.Log($"[FarOp] op #{seq} {op} recorded AFTER the shells filled. Far band: {(far == null ? "none" : $"{far.PatchesApplied - patches0} patches up")} " +
                  $"after {farFrames} frames ({farS:F2} s). Shells: {shells.RedrawsQueued} chunks queued, {shells.Redrawn - redrawn0} redrawn, " +
                  $"{shells.OpsCaughtUp} caught up on arrival, all stored after {shellFrames} frames ({shellS:F2} s)");
        string held = "";
        for (int k = shells.FirstShell; k <= shells.LastShell; k++)
            if (shells.Store(k).Holds(op.centreVoxel >> (7 + k))) held += $" {k}";
        Debug.Log($"[FarOp] shells holding the crater's chunk:{(held.Length == 0 ? " none" : held)}; replace with no room {SumNoRoom(shells)}");
        for (int i = 0; i < 10; i++) { camT.position = camA; yield return null; }
        yield return DemoShot(dir, "farop_A_360m_after");
        RaymarchFeature.ShellTint = true;
        for (int i = 0; i < 10; i++) { camT.position = camA; yield return null; }
        yield return DemoShot(dir, "farop_A_360m_tint_after");
        RaymarchFeature.ShellTint = false;
        yield return ShotB("after");
    }

    private static long SumNoRoom(VoxelEngine.Mirror.ShellStreamer s)
    {
        long n = 0;
        for (int k = s.FirstShell; k <= s.LastShell; k++) n += s.Store(k).ReplaceNoRoom;
        return n;
    }
}
