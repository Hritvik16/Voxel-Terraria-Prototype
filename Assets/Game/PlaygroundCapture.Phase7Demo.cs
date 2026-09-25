// Assets/Game/PlaygroundCapture.Phase7Demo.cs
//
// -phase7demo : THE OWNER'S PHASE 7 REVIEW, AUTOMATED.
//
// PHASE_7_COMPLETION.md §12 is a five-minute hands-on walkthrough whose answers
// close the phase. This mode plays the same walkthrough by itself, in a window
// the owner can watch, with a caption bar saying what to look at, and saves a
// screenshot at every moment a §10 question turns on. It ends by writing
// index.html (open it in a browser) and index.md beside the images.
//
// It is a LOOK, not a measurement. Windowed, back-to-back, no cooldown: nothing
// here is evidence about frame time, and it prints no frame times on purpose.
//
// The demo installs SHIPPED LightingConfig defaults for its own run (it never
// writes the file), so both looks are compared at the values they ship with --
// the live LightingConfig.json on this machine still carries Stage J's
// ambient 0.055, which would make the old look read far darker than it ships.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Mathematics;
using UnityEngine;

public partial class PlaygroundCapture
{
    private string _demoCaption;
    private string _demoSection;
    private readonly List<(string section, string file, string caption)> _demoShots =
        new List<(string, string, string)>();
    private readonly List<(string title, string question)> _demoSections =
        new List<(string, string)>();

    private void OnGUI()
    {
        if (string.IsNullOrEmpty(_demoCaption)) return;
        float h = Mathf.Max(54f, Screen.height * 0.07f);
        var r = new Rect(0f, Screen.height - h, Screen.width, h);
        GUI.color = new Color(0f, 0f, 0f, 0.72f);
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = Color.white;
        var st = new GUIStyle(GUI.skin.label)
        {
            fontSize = Mathf.RoundToInt(h * 0.30f),
            alignment = TextAnchor.MiddleLeft,
            wordWrap = true,
        };
        st.normal.textColor = Color.white;
        GUI.Label(new Rect(18f, r.y, Screen.width - 36f, h),
                  (_demoSection != null ? _demoSection + "  ·  " : "") + _demoCaption, st);
    }

    private void Section(string title, string question)
    {
        _demoSection = title;
        _demoSections.Add((title, question));
        L("");
        L($"== {title} ==");
        L($"   decides: {question}");
    }

    private IEnumerator DemoShot(string name, string caption)
    {
        _demoCaption = caption;
        yield return Shot(name, caption);
        _demoShots.Add((_demoSection, name + ".png", caption));
    }

    private IEnumerator DemoPose(Camera cam, int3 eyeVox, int3 lookVox, int settle = 16)
    {
        var eye = new Vector3(eyeVox.x * 0.1f, eyeVox.y * 0.1f, eyeVox.z * 0.1f);
        var at = new Vector3(lookVox.x * 0.1f, lookVox.y * 0.1f, lookVox.z * 0.1f);
        cam.transform.position = eye;
        if ((at - eye).sqrMagnitude > 1e-4f)
            cam.transform.rotation = Quaternion.LookRotation((at - eye).normalized, Vector3.up);
        for (int i = 0; i < settle; i++) yield return null;
    }

    private IEnumerator DemoWorldPose(Camera cam, Vector3 pos, Vector3 euler, int settle = 90)
    {
        cam.transform.position = pos;
        cam.transform.rotation = Quaternion.Euler(euler);
        for (int i = 0; i < settle; i++) yield return null;
    }

    private static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

    private IEnumerator Phase7Demo()
    {
        Screen.SetResolution(1920, 1080, false);
        var pg = FindAnyObjectByType<Playground>();
        while (Phase4Bootstrapper.Store == null) yield return null;
        _demoCaption = "Phase 7 review demo — streaming the world in…";
        yield return Frames(300);

        string ts = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
        _folder = Path.Combine(Application.persistentDataPath, "Phase7Demo", ts);
        Directory.CreateDirectory(_folder);

        // Shipped defaults for this run only -- see the header.
        LightingConfig.DebugSetActive(new LightingConfig());
        VoxelEngine.Rendering.LightingDriver.ClockPaused = true;
        RaymarchFeature.ShadowQualityLevel = 0;
        RaymarchFeature.LookOverride = 1;

        var store = Phase4Bootstrapper.Store;
        var edits = new EditService();
        edits.AttachWorld(store, store, store, Phase4Bootstrapper.Clipmap);
        var driver = FindAnyObjectByType<VoxelEngine.Rendering.LightingDriver>();
        if (driver != null) driver.Attach(edits);
        var cam = Camera.main;

        L($"PHASE 7 REVIEW DEMO -- {ts}");
        L($"device {SystemInfo.graphicsDeviceName}, {Screen.width}x{Screen.height} WINDOWED, " +
          $"{(Debug.isDebugBuild ? "DEVELOPMENT" : "RELEASE")}");
        L("A look, not a measurement. No frame times are reported, deliberately.");

        int3 camVox = CoordMath.WorldToVoxel(new float3(cam.transform.position.x,
                                                        cam.transform.position.y,
                                                        cam.transform.position.z));
        int3 peak = DeepestNear(store, camVox, 240);
        int surf = SurfaceYAt(store, peak.x, peak.z);
        int3 top = new int3(peak.x, surf, peak.z);
        int3 sky = new int3(peak.x, surf + 55, peak.z - 90);
        L($"working around the highest terrain nearby: {peak}, surface y={surf}");

        // Representative poses: the Stage K lesson is that a camera in a pit
        // flatters everything, so the look comparison uses open ground.
        var groundPos = new Vector3(1268f, 7.5f, 1268f); var groundEuler = new Vector3(2f, 35f, 0f);
        var widePos = new Vector3(1280f, 14f, 1250f);    var wideEuler = new Vector3(10f, 0f, 0f);
        var highPos = new Vector3(1300f, 24f, 1240f);    var highEuler = new Vector3(26f, -18f, 0f);
        float golden = SunCycle.GoldenHourHours;

        // ================================================================
        Section("1. The look: Brickmap (NEW) vs Phase 7 as specified (OLD)",
                "Is the new look what you want? (U toggles it live in the Playground.)");
        var lookScenes = new (string name, float hours, Vector3 pos, Vector3 euler, string what)[]
        {
            ("1a_ground_noon",   12f,    groundPos, groundEuler, "open ground at noon, standing height"),
            ("1b_ground_golden", golden, groundPos, groundEuler, "open ground at golden hour"),
            ("1c_wide_noon",     12f,    widePos,   wideEuler,   "wide view at noon"),
            ("1d_high_golden",   golden, highPos,   highEuler,   "high view at golden hour"),
        };
        foreach (var s in lookScenes)
        {
            SunCycle.SetHours(s.hours);
            yield return DemoWorldPose(cam, s.pos, s.euler);
            RaymarchFeature.LookOverride = 1; yield return Frames(4);
            yield return DemoShot(s.name + "_NEW", $"NEW look — {s.what}");
            RaymarchFeature.LookOverride = 0; yield return Frames(4);
            yield return DemoShot(s.name + "_OLD", $"OLD look — {s.what}");
        }
        SunCycle.SetHours(12f);
        yield return DemoPose(cam, top + new int3(-35, 30, -35), top, 30);
        RaymarchFeature.LookOverride = 1; yield return Frames(4);
        yield return DemoShot("1e_peak_noon_NEW", "NEW look — the highest ground nearby, noon (snow if there is any)");
        RaymarchFeature.LookOverride = 0; yield return Frames(4);
        yield return DemoShot("1e_peak_noon_OLD", "OLD look — the highest ground nearby, noon");
        RaymarchFeature.LookOverride = 1;

        // ================================================================
        Section("2. Dawn to dusk",
                "Does the shadow sweep continuously, with no snapping and no speckle at low sun?");
        yield return DemoPose(cam, sky, top, 30);
        float[] hours = { 6.3f, 7f, 8f, 10f, 12f, 14f, 16f, golden, 17.8f, 19f, 0f };
        for (int i = 0; i < hours.Length; i++)
        {
            SunCycle.SetHours(hours[i]);
            yield return Frames(10);
            yield return DemoShot($"2_{i:00}_{SunCycle.ClockString().Replace(":", "")}",
                $"{SunCycle.ClockString()} — {SunCycle.PhaseName}, sun {SunCycle.ElevationDegrees:F1}°");
        }
        _demoCaption = "LIVE: a full day in 20 s. Watch the shadows run across the terrain — any stepping is a defect.";
        for (float t = 0f; t < 20f; t += Time.unscaledDeltaTime)
        {
            SunCycle.SetHours(6f + 12.5f * (t / 20f));
            yield return null;
        }

        // ================================================================
        Section("3. Shadow quality at golden hour (§10 flag 4)",
                "Which shadow rung ships? Only 'off' saves real time; 0/1/2 cost about the same.");
        SunCycle.SetHours(golden);
        string[] rungNames = { "full soft", "half-res distant", "hard only", "off" };
        for (int pass = 0; pass < 2; pass++)
        {
            if (pass == 0) yield return DemoWorldPose(cam, groundPos, groundEuler, 40);
            else yield return DemoPose(cam, top + new int3(-14, 8, -14), top, 30);
            string where = pass == 0 ? "open ground" : "close on a shadow edge";
            for (int r = 0; r < 4; r++)
            {
                RaymarchFeature.ShadowQualityLevel = r;
                yield return Frames(6);
                yield return DemoShot($"3{(pass == 0 ? "a" : "b")}_rung{r}",
                    $"rung {r} ({rungNames[r]}) — {where}");
            }
        }
        RaymarchFeature.ShadowQualityLevel = 0;
        float kSoft = LightingConfig.Active.kSoft;
        LightingConfig.Active.kSoft = 0f; yield return Frames(6);
        yield return DemoShot("3c_hard_edges", "rung 0 with k_soft = 0 — HARD edges (compare with 3b_rung0, soft)");
        LightingConfig.Active.kSoft = kSoft;

        // ================================================================
        Section("4. Frame rate vs sharpness (§N.5 option 1)",
                "Is 560×315 internal sharp enough? It is what holds ~60 FPS at golden hour.");
        SunCycle.SetHours(golden);
        yield return DemoWorldPose(cam, groundPos, groundEuler, 30);
        RaymarchFeature.GateModeOverride = RaymarchFeature.GateResMode.ForcedCustom;
        RaymarchFeature.CustomGateResolution = new Vector2Int(960, 540);
        yield return Frames(10);
        yield return DemoShot("4a_gate_960", "internal 960×540 (shipped) — measured in Stage K: 26.5 ms / 37.7 FPS at golden hour");
        RaymarchFeature.CustomGateResolution = new Vector2Int(560, 315);
        yield return Frames(10);
        yield return DemoShot("4b_gate_560", "internal 560×315 — measured in Stage O: 16.3–16.8 ms / 59–61 FPS at golden hour");
        RaymarchFeature.GateModeOverride = RaymarchFeature.GateResMode.UseInspector;

        // ================================================================
        Section("5. Caves and torches (§10 flags 1 and 3)",
                "Is the dark cave dark enough? Is light leaking through a wall acceptable for v1? N·L on or off?");
        SunCycle.SetHours(12f);
        int cy = Mathf.Max(16, surf - 34);
        int3 c0 = new int3(peak.x, cy, peak.z);
        _demoCaption = "carving a sealed cave under the peak…";
        Carve(edits, c0, 15);
        Carve(edits, c0 + new int3(17, 3, -6), 11);
        Carve(edits, c0 + new int3(-14, -4, 9), 12);
        yield return Frames(40);
        yield return DemoPose(cam, c0 + new int3(0, 2, -13), c0);
        yield return DemoShot("5a_cave_dark", "sealed cave at noon, no torch — §6.5 wants this genuinely dark");
        int3 torch = c0 + new int3(0, -9, 0);
        edits.TrySetVoxel(torch, Materials.Torch);
        yield return Frames(20);
        yield return DemoPose(cam, torch + new int3(0, 3, -9), torch);
        yield return DemoShot("5b_torch_3m", "one torch, from 3 m");
        yield return DemoPose(cam, torch + new int3(8, 10, -8), torch);
        yield return DemoShot("5c_torch_high", "one torch, from above and to the side");
        RaymarchFeature.PointLightNdotL = false; yield return Frames(6);
        yield return DemoShot("5d_torch_no_NdotL", "same, N·L OFF — every face lit alike (C.9 literal; flag 3)");
        RaymarchFeature.PointLightNdotL = true; yield return Frames(6);

        // A stone wall between the camera and the torch, one voxel thick.
        int wz = torch.z - 5;
        for (int x = -12; x <= 12; x++)
        for (int y = -3; y <= 14; y++)
            edits.TrySetVoxel(new int3(torch.x + x, torch.y + y, wz), Materials.Stone);
        yield return Frames(20);
        yield return DemoPose(cam, new int3(torch.x + 5, torch.y + 6, wz - 7),
                              new int3(torch.x - 2, torch.y - 1, wz - 2));
        yield return DemoShot("5e_torch_behind_wall",
            "torch is BEHIND this wall — floor on this side still lit: light leaks through (flag 1)");

        // ================================================================
        Section("6. Bomb a cave roof at noon (§12 d)",
                "Does sunlight reach the floor the moment the roof is gone?");
        SunCycle.SetHours(12f);
        int3 under = c0 + new int3(17, 3, -6);
        yield return DemoPose(cam, under + new int3(0, -6, -9), under + new int3(0, 14, 0), 30);
        yield return DemoShot("6a_roof_before", "under the roof at noon — dark");
        int shaftTop = SurfaceYAt(store, under.x, under.z) + 2;
        for (int y = under.y; y <= shaftTop; y += 6) Carve(edits, new int3(under.x, y, under.z), 7);
        yield return Frames(2);
        yield return DemoShot("6b_roof_open", "roof removed (shaft to the surface) — sun on the floor, no waiting");
        yield return Frames(30);
        yield return DemoShot("6c_roof_open_settled", "half a second later — should be identical to 6b");

        // ================================================================
        Section("7. Moving fluid, moving shadows; lava at night (§12 e, §10 flag 9)",
                "Do falling fluids cast moving shadows? Is it OK that a lava pool lights nothing around it?");
        if (pg != null)
        {
            pg.SendMessage("TeleportToArena", SendMessageOptions.DontRequireReceiver);
            yield return null;
            var ac = Camera.main;
            Vector3 basePos = ac.transform.position;
            ac.transform.position = new Vector3(basePos.x - 5.5f, basePos.y + 7.5f, basePos.z - 5.5f);
            ac.transform.rotation = Quaternion.Euler(34f, 45f, 0f);
            SunCycle.SetHours(SunCycle.HoursForElevation(18f, morning: false));
            yield return Frames(20);
            yield return DemoShot("7a_arena_before", "the arena basin, sun at 18°");
            pg.SendMessage("StartChaosBurst", SendMessageOptions.DontRequireReceiver);
            pg.SendMessage("DebugOpenAllVents", SendMessageOptions.DontRequireReceiver);
            yield return Frames(90);
            yield return DemoShot("7b_pour_1", "CHAOS BURST falling — each falling block casts its own shadow");
            yield return Frames(20);
            yield return DemoShot("7c_pour_2", "a moment later — the shadows moved with the fluid");
            _demoCaption = "letting the fluid settle…";
            yield return Frames(900);
            yield return DemoShot("7d_settled_day", "settled: water, sand, lava, obsidian where they met");
            SunCycle.SetHours(0f);
            yield return Frames(10);
            yield return DemoShot("7e_settled_midnight", "midnight — lava glows but lights nothing around it (flag 9)");
        }

        // ================================================================
        Section("8. Debug views",
                "Reference only: where the shadows fall, and where shadow rays are expensive.");
        SunCycle.SetHours(golden);
        yield return DemoWorldPose(cam, groundPos, groundEuler, 30);
        RaymarchFeature.DebugViewOverride = RaymarchFeature.DebugMode.SunVis;
        RaymarchFeature.UseDebugViewOverride = true; yield return Frames(6);
        yield return DemoShot("8a_sun_visibility", "sun visibility: white lit, black shadowed");
        RaymarchFeature.DebugViewOverride = RaymarchFeature.DebugMode.ShadowHeat; yield return Frames(6);
        yield return DemoShot("8b_shadow_heat", "shadow-ray cost: blue cheap → red expensive");
        RaymarchFeature.UseDebugViewOverride = false;

        _demoCaption = "done — writing index.html";
        WriteDemoIndex(ts);
        File.WriteAllText(Path.Combine(_folder, "demo_log.txt"), _log.ToString());
        Debug.Log("[phase7demo]\n" + _log);
        yield return Frames(30);
        Application.Quit(_shots >= 40 ? 0 : 1);
    }

    private void WriteDemoIndex(string ts)
    {
        var md = new StringBuilder();
        var html = new StringBuilder();
        md.AppendLine($"# Phase 7 review demo — {ts}").AppendLine();
        md.AppendLine("A look, not a measurement: windowed, back-to-back, no frame times.").AppendLine();
        html.AppendLine("<!doctype html><meta charset=utf-8><title>Phase 7 review</title>");
        html.AppendLine("<style>body{font:15px/1.45 -apple-system,sans-serif;margin:24px;background:#111;color:#eee}" +
                        "h1{font-size:22px}h2{font-size:18px;margin-top:40px;border-top:1px solid #444;padding-top:16px}" +
                        ".q{color:#f5c56b}.g{display:grid;grid-template-columns:repeat(auto-fill,minmax(460px,1fr));gap:14px}" +
                        "figure{margin:0}img{width:100%;border-radius:4px;cursor:zoom-in}figcaption{font-size:13px;color:#bbb}</style>");
        html.AppendLine($"<h1>Phase 7 review demo — {ts}</h1>");
        html.AppendLine("<p>A look, not a measurement: windowed, back-to-back, no frame times. Click an image for full size.</p>");
        foreach (var (title, question) in _demoSections)
        {
            md.AppendLine($"## {title}").AppendLine().AppendLine($"**Decides:** {question}").AppendLine();
            html.AppendLine($"<h2>{Esc(title)}</h2><p class=q>Decides: {Esc(question)}</p><div class=g>");
            foreach (var (section, file, caption) in _demoShots)
            {
                if (section != title) continue;
                md.AppendLine($"![{caption}]({file})  ").AppendLine($"`{file}` — {caption}").AppendLine();
                html.AppendLine($"<figure><a href=\"{file}\"><img src=\"{file}\" loading=lazy></a>" +
                                $"<figcaption><b>{file}</b> — {Esc(caption)}</figcaption></figure>");
            }
            html.AppendLine("</div>");
        }
        File.WriteAllText(Path.Combine(_folder, "index.md"), md.ToString());
        File.WriteAllText(Path.Combine(_folder, "index.html"), html.ToString());
    }

    private static string Esc(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
