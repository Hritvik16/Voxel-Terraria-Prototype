// Assets/Game/SettingsMenu.cs
//
// TASK 58 (owner, 2026-10-10): a small in-game graphics settings panel for
// interactive play (`./run-play.sh`). F6 opens/closes it (Esc closes too).
// Every option drives a switch that already existed -- no new rendering:
//
//   Shadow quality   RaymarchFeature.ShadowQualityLevel, the J key's §6.5 rungs
//   Shadow resolution RaymarchFeature.ShadowHalfRes, as -shadowres (task 61: Full, see hint)
//   Roof skip        RaymarchFeature.ShadowRoofSkip, as -roofskip (task 59: OFF, see hint)
//   Upscaler         UpscalerSelect.Apply, as -upscaler (task 47/52)
//   Internal rows    UpscalerSelect.Apply, as -renderrows
//   Shadow edges     Playground.SetSoftShadows, the K key (k_soft 0 = hard)
//   Perf overlay     PlaygroundHud's strip and its F1 detail panel
//   Minimap          Minimap.Visible (F4 also flips it)
//   Game mode        Playground.SetGameMode, as -gamemode (task 64: Survival / Creative)
//   Underwater view  RaymarchFeature.UnderwaterView, as -underwater (task 66b: On)
//   See-through liquids RaymarchFeature.SeeThroughLiquids, as -seethrough (task 75: On)
//
// ORDERED by task 57's cost split (Logs/costsplit_2026-10-10_212527.txt, warm,
// walk at noon, FSR 3 at 540): sun shadows 14.2 / 4.2 ms (wall / open view),
// the FSR 3 pass 6.0 / 6.2 ms, HUD + minimap ~1 ms; rows set the traced pixels
// of a GPU-bound frame. The hints quote 57 only where 57 measured it.
//
// The render-preset path (F3) is NOT offered: its Quality preset is the same
// 960x540 trace as "None at 540" (nearest vs bilinear blit), and Balanced /
// Performance render 640x360 / 480x270, below the owner's 960x540 minimum
// (RenderPreset.Default, 2026-10-01). F3 stays as a dev key.
//
// All options apply LIVE; none needs a restart. A change made HERE is saved
// (PlaySettings.json, persistentDataPath) and applied at the next start; the
// keys J/K/F1/F4 stay quick session toggles and are not saved. Command-line
// flags override the file for one run (PlaySettings.WithArgs). Only an
// interactive run reads the file (Playground.Start: not IsCaptureRun, not
// -fluidprobe), so nothing a rig measures changes.
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class SettingsMenu : MonoBehaviour
{
    public const KeyCode ToggleKey = KeyCode.F6;
    public static bool IsOpen { get; private set; }
    // Task 59's warm A/B on tg2 (Logs/costsplit_2026-10-10_224908 / _225726); same pixels either way.
    // Task 61's warm A/B on tg2 (Logs/costsplit_2026-10-11_001455 / _002546).
    private const string RES_HINT = "Half: one sun-shadow ray per 2x2 pixels (silhouettes still trace their own). On tg2 it saved 0.5-1.3 ms of 29-43 ms. Blockier shadow edges.";
    private const string ROOF_HINT = "Same pixels. On tg2 ON saved 1.5 ms facing a wall at noon but cost 3.5 ms on an open view, and 2-5 ms at a low sun.";

    private static string _path;
    private static PlaySettings _saved = new PlaySettings();
    private static readonly List<string> _overridden = new List<string>();
    private static string _note;
    private static PlaygroundHud _hud;

    private Texture2D _px;
    private GUIStyle _title, _hint, _button, _small;

    // =====================================================================
    // Start-up: saved file + flags -> the engine switches
    // =====================================================================

    /// Playground.Start, interactive runs only. Replaces task 52's "fsr3 dyn
    /// unless a flag says otherwise" with "saved file, unless a flag says
    /// otherwise" (the file's defaults ARE fsr3 dyn).
    public static void LoadAndApply()
    {
        _path = Path.Combine(Application.persistentDataPath, PlaySettings.FileName);
        _saved = PlaySettings.Load(_path, out string err);
        _overridden.Clear();
        var run = PlaySettings.WithArgs(_saved, Environment.GetCommandLineArgs(), _overridden);

        if (!run.presetPath) ApplyUpscaler(run.upscaler, run.rows);
        RaymarchFeature.ShadowQualityLevel = run.shadowLevel;
        RaymarchFeature.ShadowRoofSkip = run.roofSkip;
        RaymarchFeature.ShadowHalfRes = run.shadowRes == "half";
        if (!run.softShadows) Playground.SetSoftShadows(false);
        ApplyOverlay(run.perfOverlay);
        Minimap.Visible = run.minimap;
        RaymarchFeature.UnderwaterView = run.underwaterFog;
        RaymarchFeature.SeeThroughLiquids = run.seeThroughLiquids;
        Playground.SetGameMode(run.gameMode == "survival");
        // -settingsopen: start with the menu open (task 58's -playshot still).
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-settingsopen") >= 0) IsOpen = true;

        Debug.Log($"[PlaySettings] {(File.Exists(_path) ? "loaded" : "no file yet (defaults)")} {_path}" +
                  (err != null ? $" -- {err}" : "") +
                  $"; this run: upscaler {(run.presetPath ? "preset path" : run.upscaler)}, rows {run.rows}, shadows {run.shadowLevel}" +
                  $" {(run.softShadows ? "soft" : "hard")}, shadow res {run.shadowRes}, roof skip {(run.roofSkip ? "on" : "off")}, overlay {run.perfOverlay}, minimap {(run.minimap ? "on" : "off")}, game mode {run.gameMode}, underwater {(run.underwaterFog ? "on" : "off")}, see-through liquids {(run.seeThroughLiquids ? "on" : "off")}" +
                  (_overridden.Count > 0 ? $"; flags override: {string.Join(", ", _overridden)}" : ""));
    }

    private static void ApplyUpscaler(string upscaler, string rows)
    {
        UpscalerSelect.TryParseKind(upscaler, out var k);
        bool dyn = rows == "dyn";
        int r = dyn ? UpscalerSelect.MinRows : int.Parse(rows);
        UpscalerSelect.Apply(k, r, dyn);
    }

    private static PlaygroundHud Hud => _hud != null ? _hud : (_hud = FindAnyObjectByType<PlaygroundHud>());

    private static void ApplyOverlay(int level)
    {
        if (Hud == null) return;
        Hud.StripVisible = level >= 1;
        Hud.DetailVisible = level >= 2;
    }

    private static int CurrentOverlay() =>
        Hud == null ? 0 : Hud.DetailVisible ? 2 : Hud.StripVisible ? 1 : 0;

    /// A choice made in the menu: applied now, written to the file, and no
    /// longer shown as overridden by a flag.
    private static void Save(string field, Action<PlaySettings> set)
    {
        set(_saved);
        _overridden.Remove(field);
        if (field == "upscaler" || field == "rows") _overridden.Remove("upscaler (preset path)");
        _note = PlaySettings.Save(_path, _saved, out string err) ? $"saved to {_path}" : err;
        Debug.Log($"[PlaySettings] {field} -> {_saved.ToJson().Replace("\n", " ")} ({_note})");
    }

    // =====================================================================
    // Open / close
    // =====================================================================

    void Update()
    {
        if (DevMap.IsOpen) { IsOpen = false; return; }
        if (Input.GetKeyDown(ToggleKey)) IsOpen = !IsOpen;
        else if (IsOpen && Input.GetKeyDown(KeyCode.Escape)) IsOpen = false;
        // PlaygroundFlyCamera releases the mouse while open, and Playground
        // stops digging / walking / the other keys (as for the dev map).
    }

    void OnDisable() => IsOpen = false;

    // =====================================================================
    // The panel
    // =====================================================================

    void OnGUI()
    {
        if (!IsOpen || Playground.DebugHideUi || DevMap.IsOpen) return;
        GUI.depth = -10;   // over Playground's help panel and the HUD
        float ui = Mathf.Max(1f, Screen.height / 900f);
        Matrix4x4 prev = GUI.matrix;
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one * ui);
        float vw = Screen.width / ui, vh = Screen.height / ui;
        Styles();

        const float w = 780f, h = 930f;   // + task 64's Game mode row, task 66b's Underwater view row, task 75's See-through liquids row
        var r = new Rect((vw - w) * 0.5f, (vh - h) * 0.5f, w, h);
        Fill(r, new Color(0.03f, 0.05f, 0.08f, 0.96f));
        Fill(new Rect(r.x, r.y, w, 1f), new Color(1f, 1f, 1f, 0.25f));
        Fill(new Rect(r.x, r.yMax - 1f, w, 1f), new Color(1f, 1f, 1f, 0.25f));

        float x = r.x + 20f, y = r.y + 14f;
        GUI.Label(new Rect(x, y, w - 40f, 26f), "<b>SETTINGS</b>   <color=#9fb6c9>F6 or Esc closes -- every option applies now, no restart</color>", _title);
        y += 30f;

        // 0. Game mode (task 64, gameplay step 1). Not a graphics cost.
        int gm = Playground.SurvivalMode ? 0 : 1;
        int pick0 = Row(x, ref y, "Game mode", "gameMode",
            "Survival: dig into your inventory, build cubes from the hotbar (E bag, Q size). Creative: the old free brushes.",
            new[] { "Survival", "Creative" }, gm);
        if (pick0 >= 0 && pick0 != gm)
        {
            Playground.SetGameMode(pick0 == 0);
            Save("gameMode", s => s.gameMode = pick0 == 0 ? "survival" : "creative");
        }

        GUI.Label(new Rect(x, y, w - 40f, 22f), "<b>GRAPHICS</b>", _title);
        y += 22f;
        GUI.Label(new Rect(x, y, w - 40f, 18f), "<color=#9fb6c9>Most frame time first (task 57's cost split: walk at noon, FSR 3 at 540 rows, facing a wall / an open view)</color>", _small);
        y += 28f;

        // 1. Sun shadows: the biggest single cut.
        int lvl = RaymarchFeature.ShadowQualityLevel;
        int pick = Row(x, ref y, "Shadow quality", "shadowLevel",
            "Biggest frame-time cost: Off saved 14.2 ms facing a wall, 4.2 ms on an open view. 1 and 2 not measured.",
            new[] { "Full soft", "Half-res far", "Hard", "Off" }, lvl);
        if (pick >= 0 && pick != lvl)
        {
            RaymarchFeature.ShadowQualityLevel = pick;
            Save("shadowLevel", s => s.shadowLevel = pick);
        }

        // 1a. Half-res sun shadows (task 61).
        int res = RaymarchFeature.ShadowHalfRes ? 1 : 0;
        pick = Row(x, ref y, "Shadow resolution", "shadowRes",
            RES_HINT,
            new[] { "Full", "Half" }, res);
        if (pick >= 0 && pick != res)
        {
            RaymarchFeature.ShadowHalfRes = pick == 1;
            Save("shadowRes", s => s.shadowRes = pick == 1 ? "half" : "full");
        }

        // 1b. The shadow roof skip (task 59): exact, so it only changes cost.
        int roof = RaymarchFeature.ShadowRoofSkip ? 0 : 1;
        pick = Row(x, ref y, "Roof skip", "roofSkip",
            ROOF_HINT,
            new[] { "On", "Off" }, roof);
        if (pick >= 0 && pick != roof)
        {
            RaymarchFeature.ShadowRoofSkip = pick == 0;
            Save("roofSkip", s => s.roofSkip = pick == 0);
        }

        // 2. The upscaler.
        int cur = UpscalerSelect.Active ? (int)UpscalerSelect.Current : -1;
        pick = Row(x, ref y, "Upscaler", "upscaler",
            UpscalerSelect.Active
                ? "FSR 3's own pass costs ~6 ms over None (plain stretch). TAA, FSR 1 and STP not measured in 57."
                : "Now on the old render-preset path (F3 or a flag) -- pick one to leave it. FSR 3's pass costs ~6 ms over None.",
            new[] { "None", "TAA", "FSR 1", "STP", "FSR 3" }, cur);
        if (pick >= 0 && pick != cur)
        {
            string name = PlaySettings.Upscalers[pick];
            string rows = UpscalerSelect.Active ? (UpscalerSelect.Dynamic ? "dyn" : UpscalerSelect.Rows.ToString()) : _saved.rows;
            ApplyUpscaler(name, rows);
            Save("upscaler", s => { s.upscaler = name; s.rows = rows; });
        }

        // 3. Internal rows.
        int rowsCur = !UpscalerSelect.Active ? -1 : UpscalerSelect.Dynamic ? 2 : UpscalerSelect.Rows >= UpscalerSelect.MaxRows ? 1 : 0;
        pick = Row(x, ref y, "Internal resolution", "rows",
            "Rows the voxels are traced at, upscaled to the screen. The frame is GPU-bound: it follows these pixels.",
            new[] { "540", "720", UpscalerSelect.Active && UpscalerSelect.Dynamic ? $"Dynamic (now {UpscalerSelect.Rows})" : "Dynamic 540-720" }, rowsCur);
        if (pick >= 0 && pick != rowsCur)
        {
            string rows = PlaySettings.RowChoices[pick];
            string name = UpscalerSelect.Active ? UpscalerSelect.Name(UpscalerSelect.Current) : _saved.upscaler;
            ApplyUpscaler(name, rows);
            Save("rows", s => { s.rows = rows; s.upscaler = name; });
        }

        // 4. Shadow edges (K).
        int soft = LightingConfig.Active.kSoft > 0f ? 0 : 1;
        pick = Row(x, ref y, "Shadow edges", "softShadows",
            "Soft penumbra (k_soft in LightingConfig.json) or hard edges. Cost not measured; 'Hard' quality is hard either way.",
            new[] { "Soft", "Hard" }, soft);
        if (pick >= 0 && pick != soft)
        {
            Playground.SetSoftShadows(pick == 0);
            Save("softShadows", s => s.softShadows = pick == 0);
        }

        // 5. Perf overlay.
        int ov = CurrentOverlay();
        pick = Row(x, ref y, "Perf overlay", "perfOverlay",
            "The whole HUD + minimap cost ~1 ms together. F1 opens the detail panel for this session.",
            new[] { "Off", "Strip", "Strip + details" }, Hud == null ? -1 : ov);
        if (pick >= 0 && pick != ov && Hud != null)
        {
            ApplyOverlay(pick);
            Save("perfOverlay", s => s.perfOverlay = pick);
        }

        // 6. Minimap.
        int mm = Minimap.Visible ? 0 : 1;
        pick = Row(x, ref y, "Minimap", "minimap",
            "Part of the ~1 ms above. F4 also flips it for this session.",
            new[] { "On", "Off" }, mm);
        if (pick >= 0 && pick != mm)
        {
            Minimap.Visible = pick == 0;
            Save("minimap", s => s.minimap = pick == 0);
        }

        // 7. Underwater view (task 66b). Look only, not measured.
        int uv = RaymarchFeature.UnderwaterView ? 0 : 1;
        pick = Row(x, ref y, "Underwater view", "underwaterFog",
            "With your eyes in water (or oil, sludge, ...): the bed and walls through tinted fog, the surface from below. Off: black.",
            new[] { "On", "Off" }, uv);
        if (pick >= 0 && pick != uv)
        {
            RaymarchFeature.UnderwaterView = pick == 0;
            Save("underwaterFog", s => s.underwaterFog = pick == 0);
        }

        // 8. See-through liquids (task 75). Small cost where liquid is in view.
        int stl = RaymarchFeature.SeeThroughLiquids ? 0 : 1;
        pick = Row(x, ref y, "See-through liquids", "seeThroughLiquids",
            "Water, honey, acid, sludge and oil show what is under them, each by its own clarity. Off: opaque surfaces.",
            new[] { "On", "Off" }, stl);
        if (pick >= 0 && pick != stl)
        {
            RaymarchFeature.SeeThroughLiquids = pick == 0;
            Save("seeThroughLiquids", s => s.seeThroughLiquids = pick == 0);
        }

        y += 4f;
        GUI.Label(new Rect(x, y, w - 40f, 60f),
            "<color=#9fb6c9>Choices made here are saved and used at the next start. Command-line flags " +
            "(-upscaler, -renderrows, -shadowlevel, -shadowres, -roofskip, -gamemode, -underwater, -seethrough) override them for one run. The keys J, K, F1, F4 change " +
            "things for this session only.\n" + (_note ?? $"file: {_path}") + "</color>", _small);

        GUI.matrix = prev;
    }

    /// One option: title, buttons, hint. Returns the clicked index, or -1.
    private int Row(float x, ref float y, string title, string field, string hint, string[] choices, int current)
    {
        bool flag = _overridden.Contains(field) || (field == "upscaler" && _overridden.Contains("upscaler (preset path)"));
        GUI.Label(new Rect(x, y, 200f, 44f), $"<b>{title}</b>" + (flag ? "\n<color=#ffd479>set by a flag</color>" : ""), _title);
        int clicked = -1;
        float bx = x + 200f, bw = Mathf.Min(170f, 540f / choices.Length - 6f);
        Color old = GUI.backgroundColor;
        for (int i = 0; i < choices.Length; i++)
        {
            GUI.backgroundColor = i == current ? new Color(0.35f, 0.75f, 1f) : new Color(0.55f, 0.55f, 0.6f);
            string label = i == current ? $"<b>{choices[i]}</b>" : choices[i];
            if (GUI.Button(new Rect(bx + i * (bw + 6f), y, bw, 26f), label, _button)) clicked = i;
        }
        GUI.backgroundColor = old;
        GUI.Label(new Rect(x + 200f, y + 30f, 560f, 36f), $"<color=#b8c8d6>{hint}</color>", _hint);
        y += 74f;
        return clicked;
    }

    private void Styles()
    {
        if (_title != null) return;
        _title = new GUIStyle(GUI.skin.label) { fontSize = 15, richText = true, normal = { textColor = Color.white } };
        _small = new GUIStyle(GUI.skin.label) { fontSize = 12, richText = true, wordWrap = true, normal = { textColor = Color.white } };
        _hint = new GUIStyle(_small) { fontSize = 12 };
        _button = new GUIStyle(GUI.skin.button) { fontSize = 13, richText = true };
    }

    private void Fill(Rect r, Color c)
    {
        if (_px == null) { _px = new Texture2D(1, 1); _px.SetPixel(0, 0, Color.white); _px.Apply(); }
        Color old = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(r, _px);
        GUI.color = old;
    }
}
