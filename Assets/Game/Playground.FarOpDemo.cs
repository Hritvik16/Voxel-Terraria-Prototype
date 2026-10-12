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

using System.Collections;
using Unity.Mathematics;
using UnityEngine;

public partial class Playground
{
    private IEnumerator FarOpDemo()
    {
        float t0 = Time.unscaledTime;
        while (Time.unscaledTime - t0 < 25f || _player == null || !_player.Ready) yield return null;
        var centre = Phase4Bootstrapper.FarOpCentre;
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
}
