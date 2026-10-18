// Assets/Game/Playground.ShellsDemo.cs
//
// -shellsdemo (task 93c, play defaults to the LOD shells): play's own frame path
// (FSR 3 dyn, far shading, see-through liquids -- whatever PlaySettings + flags
// give this run) at noon, clock paused, fly mode, UI hidden. Run it twice, OFF
// and ON (the shells are a BOOT decision, PlaySettings.ResolveShells):
//   ./run-play.sh EXTRA="-shellsdemo -shells 0"   and   ./run-play.sh EXTRA="-shellsdemo"
// Three poses with the sun behind the camera, each after a SHORT fixed wait
// (-shellsdemowait S, default 45; the launch budget), and the missing shell
// chunks logged: eye_level_2 (PlaygroundCapture's site, looking away from the
// sun), glide_400m and peak_from_2km (the peak from its sun side, 400 m / 25 m
// over the ground). Then, shells ON only, task 93a's FAR pose (the blocky ridge)
// waited until shells 1-4 have nothing missing (cap -shellsdemoridge S, 240),
// plain + -shellsdebug tint. The STEP 14 memory line after every pose.
// Shots: persistentDataPath/ShellsDemo/<on|off>_<pose>[ _tint].png.

using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public partial class Playground
{
    private IEnumerator ShellsDemo()
    {
        float t0 = Time.unscaledTime;
        while (Time.unscaledTime - t0 < 20f || _player == null || !_player.Ready) yield return null;
        var shells = Phase4Bootstrapper.Shells;
        var far = VoxelEngine.Rendering.FarField.Active;
        string arm = shells != null ? "on" : "off";
        _showHelp = false;
        _demoHideState = true;
        DebugHideUi = true;
        SunCycle.SetHours(SunCycle.NoonHours);
        VoxelEngine.Rendering.LightingDriver.ClockPaused = true;
        string dir = System.IO.Path.Combine(Application.persistentDataPath, "ShellsDemo");
        System.IO.Directory.CreateDirectory(dir);
        if (_cam == null && Camera.main != null) _cam = Camera.main.GetComponent<PlaygroundFlyCamera>();
        _mode = MoveMode.Fly;
        var camT = Camera.main.transform;
        float wait = TryArgFloat("-shellsdemowait", out float wa) ? wa : 45f;
        float ridgeCap = TryArgFloat("-shellsdemoridge", out float rc) ? rc : 240f;
        Debug.Log($"[ShellsDemo] arm {arm} (Phase4Bootstrapper.ShellsOn {Phase4Bootstrapper.ShellsOn}, workers {Phase4Bootstrapper.ShellWorkers}); " +
                  $"wait {wait:F0} s per pose, ridge cap {ridgeCap:F0} s");
        if (far == null || !far.UsesTg2) { Debug.Log("[ShellsDemo] needs the tg2 world + far band"); Application.Quit(); yield break; }

        // tg2 voxels == engine voxels in a real tg2 world (far band aligned: offset 0, no sea shift).
        var map = far.Tg2Map; var spec = map.Spec;
        float2 sunXZ = math.normalizesafe(SunCycle.Direction.xz, new float2(0f, 1f));   // toward the sun
        float2 pk = map.PeakPosition;
        float pkTop = PlaygroundCapture.TgGround(map, pk.x, pk.y);
        var poses = new List<(string n, float3 eye, float3 at)>();
        foreach (var (n, e, a) in PlaygroundCapture.Tg2EyeLevelPoses(map))
            if (n == "eye_level_2")
                poses.Add((n, e * 0.1f, new float3(e.x - sunXZ.x * 1000f, e.y - 30f, e.z - sunXZ.y * 1000f) * 0.1f));
        // The peak from its SUN side (the sun behind the camera), as glide_400m / peak_from_2km_ground's ranges.
        float2 g4 = pk + sunXZ * 22600f, p2 = pk + sunXZ * 20000f;
        poses.Add(("glide_400m", new float3(g4.x, spec.seaLevelY + 4000f, g4.y) * 0.1f, new float3(pk.x, pkTop - 800f, pk.y) * 0.1f));
        poses.Add(("peak_from_2km", new float3(p2.x, PlaygroundCapture.TgGround(map, p2.x, p2.y) + 250f, p2.y) * 0.1f,
                   new float3(pk.x, pkTop - 400f, pk.y) * 0.1f));

        float3 at = 0f, look = 0f;
        IEnumerator Hold(int frames)
        {
            float3 d = look - at;
            for (int i = 0; i < frames; i++)
            {
                camT.position = at;
                _cam?.SetLook(math.degrees(math.atan2(d.x, d.z)), -math.degrees(math.atan2(d.y, math.length(d.xz))));
                yield return null;
            }
        }
        string Missing()
        {
            if (shells == null) return "shells OFF";
            string s = "missing per shell:";
            for (int k = shells.FirstShell; k <= shells.LastShell; k++) s += $" {k}:{shells.Missing(k)}";
            return s + $", in flight/pending {(shells.Idle ? "idle" : "busy")}";
        }
        IEnumerator Shots(string tag)
        {
            Debug.Log($"[ShellsDemo] {arm}_{tag}: camera {at} m looking at {look} m; {Missing()}");
            yield return DemoShot(dir, $"{arm}_{tag}");
            if (shells != null)
            {
                RaymarchFeature.ShellTint = true;
                yield return Hold(10);
                yield return DemoShot(dir, $"{arm}_{tag}_tint");
                RaymarchFeature.ShellTint = false;
            }
            Debug.Log("[ShellsDemo] mem " + MemoryLedger.Step14Line().Trim());
        }

        foreach (var (n, e, a) in poses)
        {
            at = e; look = a;
            float ts = Time.unscaledTime;
            while (Time.unscaledTime - ts < wait) yield return Hold(1);
            yield return Hold(20);
            yield return Shots(n);
        }

        // Task 93a's far pose (its -farwall run: camera (3904.6, 978.5, 4083.8) m, looking at the
        // tower's middle) -- the "blocky ridge" the owner flagged. Shells ON: wait for 1-4 filled.
        at = new float3(3904.616f, 978.5f, 4083.802f);
        look = new float3(4103.8f, 906.0f, 3968.8f);
        if (shells != null)
        {
            float tr = Time.unscaledTime;
            bool Filled() { for (int k = 1; k <= math.min(4, shells.LastShell); k++) if (k >= shells.FirstShell && shells.Missing(k) > 0) return false; return true; }
            yield return Hold(30);
            while (Time.unscaledTime - tr < ridgeCap && !Filled()) yield return Hold(1);
            Debug.Log($"[ShellsDemo] ridge: shells 1-4 {(Filled() ? "FILLED" : "NOT filled at the cap")} after {Time.unscaledTime - tr:F0} s");
            yield return Hold(60);
        }
        else
        {
            float ts = Time.unscaledTime;
            while (Time.unscaledTime - ts < wait) yield return Hold(1);
        }
        yield return Shots("ridge93a");
        Debug.Log("[ShellsDemo] done");
        Application.Quit();
    }
}
