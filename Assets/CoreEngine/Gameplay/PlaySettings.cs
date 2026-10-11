// ==========================================
// Assets/CoreEngine/Gameplay/PlaySettings.cs
//
// TASK 58 (owner, 2026-10-10: "a small in-game graphics settings menu"). The
// player's saved graphics choices for INTERACTIVE play (`./run-play.sh`). Every
// option is a switch that already existed as a flag or a key; the menu
// (Assets/Game/SettingsMenu.cs) only drives those.
//
// WHERE THE FILE LIVES: Application.persistentDataPath/PlaySettings.json, beside
// PlayerConfig.json and LightingConfig.json. It is written only when the menu
// changes something, so a run without the file starts exactly as before.
//
// ORDER, lowest to highest: the defaults below (= the interactive start before
// task 58) < the saved file < the command line, PER FIELD and for that run
// only. A flag never writes the file. Rigs and capture runs never read it
// (Playground.Start decides; this class does no IO it is not asked to).
//
// Pure static Parse / WithArgs over strings so EditMode can pin the order
// without a scene (PlaySettingsTests).

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

[Serializable]
public sealed class PlaySettings
{
    public const string FileName = "PlaySettings.json";
    public static readonly string[] Upscalers = { "none", "taa", "fsr1", "stp", "fsr3" };
    public static readonly string[] RowChoices = { "540", "720", "dyn" };
    public static readonly string[] ShadowResChoices = { "full", "half" };

    // ---- The saved fields (JsonUtility: public fields only) ----
    /// none | taa | fsr1 | stp | fsr3 (UpscalerSelect.Kind; `-upscaler`).
    public string upscaler = "fsr3";
    /// 540 | 720 | dyn (`-renderrows`).
    public string rows = "dyn";
    /// §6.5's rungs, the J key: 0 full soft, 1 half-res beyond LOD1, 2 hard, 3 off (`-shadowlevel`).
    public int shadowLevel = 0;
    /// The K key: false = k_soft 0 (hard). True leaves LightingConfig's k_soft alone.
    public bool softShadows = true;
    /// PlaygroundHud: 0 off, 1 the top strip (default), 2 strip + the F1 detail panel.
    public int perfOverlay = 1;
    /// The corner minimap (F4 also flips it).
    public bool minimap = true;
    /// Task 59 (owner Q42 (a)): the shadow roof skip (RaymarchFeature.ShadowRoofSkip,
    /// `-roofskip`), exact by construction. OFF by default after task 59's warm A/B
    /// on tg2 (Logs/costsplit_2026-10-10_224908 / _225726 / _230831): its per-frame
    /// pyramid build costs ~3 ms and the skip saves <1 ms, so ON was 1.5 ms faster
    /// only facing a wall at noon and 2-5 ms slower otherwise. Owner's call (HANDOFF 59).
    /// A file saved before task 59 has no such field and so gets this default.
    public bool roofSkip = false;
    /// Task 61: full | half (RaymarchFeature.ShadowHalfRes, `-shadowres`): one sun
    /// ray per 2x2 pixels at all distances, edge-aware. Full by default (the owner
    /// judges the look); a file saved before task 61 gets this default.
    public string shadowRes = "full";

    /// Runtime only: -renderpreset / -taa / -checker without -upscaler or
    /// -renderrows asked for the old preset frame path, so no upscaler is applied.
    [NonSerialized] public bool presetPath;

    public PlaySettings Clone() => (PlaySettings)MemberwiseClone();

    public string ToJson() => JsonUtility.ToJson(this, true);

    /// Puts every field back in range; returns what it changed, or null.
    public string Validate()
    {
        var notes = new List<string>();
        if (Array.IndexOf(Upscalers, upscaler) < 0) { notes.Add($"upscaler '{upscaler}' -> fsr3"); upscaler = "fsr3"; }
        if (Array.IndexOf(RowChoices, rows) < 0) { notes.Add($"rows '{rows}' -> dyn"); rows = "dyn"; }
        if (Array.IndexOf(ShadowResChoices, shadowRes) < 0) { notes.Add($"shadowRes '{shadowRes}' -> full"); shadowRes = "full"; }
        if (shadowLevel < 0 || shadowLevel > 3) { notes.Add($"shadowLevel {shadowLevel} -> 0"); shadowLevel = 0; }
        if (perfOverlay < 0 || perfOverlay > 2) { notes.Add($"perfOverlay {perfOverlay} -> 1"); perfOverlay = 1; }
        return notes.Count == 0 ? null : string.Join(", ", notes);
    }

    /// Never throws. Empty or malformed -> defaults, with `error` set; out-of-range
    /// fields are clamped one by one (the rest of the file still counts).
    public static PlaySettings Parse(string json, out string error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(json)) { error = "empty file"; return new PlaySettings(); }
        PlaySettings s;
        try { s = JsonUtility.FromJson<PlaySettings>(json); }
        catch (Exception e) { error = "malformed JSON: " + e.Message; return new PlaySettings(); }
        if (s == null) { error = "malformed JSON (null result)"; return new PlaySettings(); }
        if (s.upscaler != null) s.upscaler = s.upscaler.ToLowerInvariant();
        if (s.shadowRes != null) s.shadowRes = s.shadowRes.ToLowerInvariant();
        string clamped = s.Validate();
        if (clamped != null) error = "clamped: " + clamped;
        return s;
    }

    /// The saved settings with this run's command-line flags laid over them.
    /// `saved` is not modified. Last match wins, like UpscalerSelect /
    /// PlaygroundCapture. A bad flag value is ignored (the saved value stays)
    /// and listed in `overridden` as "flag: bad value".
    public static PlaySettings WithArgs(PlaySettings saved, string[] args, List<string> overridden)
    {
        var s = saved.Clone();
        string u = Last(args, "-upscaler"), r = Last(args, "-renderrows"), sh = Last(args, "-shadowlevel"), rs = Last(args, "-roofskip"), sr = Last(args, "-shadowres");
        if (u != null)
        {
            string v = u.ToLowerInvariant();
            if (Array.IndexOf(Upscalers, v) >= 0) { s.upscaler = v; overridden?.Add("upscaler"); }
            else overridden?.Add($"-upscaler: bad value '{u}'");
        }
        if (r != null)
        {
            string v = r.ToLowerInvariant();
            if (Array.IndexOf(RowChoices, v) >= 0) { s.rows = v; overridden?.Add("rows"); }
            else overridden?.Add($"-renderrows: bad value '{r}'");
        }
        if (sh != null)
        {
            if (int.TryParse(sh, out int n) && n >= 0 && n <= 3) { s.shadowLevel = n; overridden?.Add("shadowLevel"); }
            else overridden?.Add($"-shadowlevel: bad value '{sh}'");
        }
        if (rs != null)
        {
            if (rs == "0" || rs == "1") { s.roofSkip = rs == "1"; overridden?.Add("roofSkip"); }
            else overridden?.Add($"-roofskip: bad value '{rs}'");
        }
        if (sr != null)
        {
            string v = sr.ToLowerInvariant();
            if (Array.IndexOf(ShadowResChoices, v) >= 0) { s.shadowRes = v; overridden?.Add("shadowRes"); }
            else overridden?.Add($"-shadowres: bad value '{sr}'");
        }
        // Task 52's rule, kept: asking for the preset path without naming an
        // upscaler keeps the preset path.
        s.presetPath = u == null && r == null && (Has(args, "-renderpreset") || Has(args, "-taa") || Has(args, "-checker"));
        if (s.presetPath) overridden?.Add("upscaler (preset path)");
        return s;
    }

    // ---- File IO (the menu's; never called by rigs) ----

    public static PlaySettings Load(string path, out string error)
    {
        error = null;
        try
        {
            if (!File.Exists(path)) return new PlaySettings();
            return Parse(File.ReadAllText(path), out error);
        }
        catch (Exception e) { error = "read failed: " + e.Message; return new PlaySettings(); }
    }

    public static bool Save(string path, PlaySettings s, out string error)
    {
        error = null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, s.ToJson());
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
            return true;
        }
        catch (Exception e) { error = "write failed: " + e.Message; return false; }
    }

    private static string Last(string[] args, string flag)
    {
        string v = null;
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == flag) v = args[i + 1];
        return v;
    }

    private static bool Has(string[] args, string flag)
    {
        foreach (string a in args) if (string.Equals(a, flag, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
