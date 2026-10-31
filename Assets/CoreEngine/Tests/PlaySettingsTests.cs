// Assets/CoreEngine/Tests/PlaySettingsTests.cs
//
// Task 58: the settings menu's saved file and its order -- defaults < saved
// file < command-line flag, per field, the flag for that run only.

using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

public class PlaySettingsTests
{
    [Test]
    public void Defaults_AreTheInteractiveStartBeforeTheMenu()
    {
        var d = new PlaySettings();
        Assert.AreEqual("fsr3", d.upscaler);   // task 52
        Assert.AreEqual("dyn", d.rows);
        Assert.AreEqual(0, d.shadowLevel);     // EngineConfig.SHADOW_QUALITY_LEVEL
        Assert.IsTrue(d.softShadows);
        Assert.AreEqual(1, d.perfOverlay);     // the strip only, F1 panel closed
        Assert.IsTrue(d.minimap);
        Assert.IsFalse(d.roofSkip);            // task 59: OFF after its A/B (owner's call)
        Assert.AreEqual("full", d.shadowRes);  // task 61: Full until the owner judges Half
    }

    [Test]
    public void SaveThenLoad_RoundTripsEveryField()
    {
        string path = Path.Combine(Path.GetTempPath(), "PlaySettingsTests_" + System.Guid.NewGuid().ToString("N"), PlaySettings.FileName);
        try
        {
            var a = new PlaySettings { upscaler = "stp", rows = "720", shadowLevel = 2, softShadows = false, perfOverlay = 2, minimap = false, roofSkip = true, underwaterFog = false, seeThroughLiquids = false, shells = false };
            Assert.IsTrue(PlaySettings.Save(path, a, out string werr), werr);
            var b = PlaySettings.Load(path, out string rerr);
            Assert.IsNull(rerr);
            Assert.AreEqual("stp", b.upscaler);
            Assert.AreEqual("720", b.rows);
            Assert.AreEqual(2, b.shadowLevel);
            Assert.IsFalse(b.softShadows);
            Assert.AreEqual(2, b.perfOverlay);
            Assert.IsFalse(b.minimap);
            Assert.IsTrue(b.roofSkip);
            Assert.IsFalse(b.underwaterFog);
            Assert.IsFalse(b.seeThroughLiquids);
            Assert.IsFalse(b.shells);   // task 93c
        }
        finally { Directory.Delete(Path.GetDirectoryName(path), true); }
    }

    [Test]
    public void MissingOrBrokenFile_GivesDefaults_BadFieldsClampAlone()
    {
        var none = PlaySettings.Load(Path.Combine(Path.GetTempPath(), "no_such_dir_58", PlaySettings.FileName), out string e0);
        Assert.IsNull(e0, "no file is the normal first run, not an error");
        Assert.AreEqual("fsr3", none.upscaler);

        var broken = PlaySettings.Parse("{ \"upscaler\": \"fsr1\", ", out string e1);
        Assert.IsNotNull(e1);
        Assert.AreEqual("fsr3", broken.upscaler);

        var bad = PlaySettings.Parse("{\"upscaler\":\"FSR1\",\"rows\":\"999\",\"shadowLevel\":7,\"perfOverlay\":-1,\"minimap\":false}", out string e2);
        StringAssert.StartsWith("clamped:", e2);
        Assert.AreEqual("fsr1", bad.upscaler, "case is forgiven");
        Assert.AreEqual("dyn", bad.rows);
        Assert.AreEqual(0, bad.shadowLevel);
        Assert.AreEqual(1, bad.perfOverlay);
        Assert.IsFalse(bad.minimap, "a good field survives a bad neighbour");
    }

    [Test]
    public void Flags_OverrideTheSavedFile_PerField_WithoutChangingIt()
    {
        var saved = new PlaySettings { upscaler = "stp", rows = "720", shadowLevel = 2, minimap = false };
        var over = new List<string>();
        var run = PlaySettings.WithArgs(saved, new[] { "app", "-upscaler", "taa", "-upscaler", "fsr1", "-shadowlevel", "1" }, over);

        Assert.AreEqual("fsr1", run.upscaler, "last match wins");
        Assert.AreEqual("720", run.rows, "no -renderrows: the saved value stays");
        Assert.AreEqual(1, run.shadowLevel);
        Assert.IsFalse(run.minimap);
        Assert.IsFalse(run.presetPath);
        CollectionAssert.AreEquivalent(new[] { "upscaler", "shadowLevel" }, over);

        Assert.AreEqual("stp", saved.upscaler, "a flag is for this run only");
        Assert.AreEqual(2, saved.shadowLevel);
    }

    [Test]
    public void BadFlagValue_KeepsTheSavedValue()
    {
        var saved = new PlaySettings { rows = "540" };
        var over = new List<string>();
        var run = PlaySettings.WithArgs(saved, new[] { "-renderrows", "1080", "-shadowlevel", "9" }, over);
        Assert.AreEqual("540", run.rows);
        Assert.AreEqual(0, run.shadowLevel);
        Assert.AreEqual(2, over.Count);
    }

    [Test]
    public void PresetFlags_KeepThePresetPath_UnlessAnUpscalerIsNamed()
    {
        var saved = new PlaySettings();
        Assert.IsTrue(PlaySettings.WithArgs(saved, new[] { "-renderpreset", "quality" }, null).presetPath);
        Assert.IsTrue(PlaySettings.WithArgs(saved, new[] { "-taa", "1" }, null).presetPath);
        Assert.IsFalse(PlaySettings.WithArgs(saved, new[] { "-taa", "1", "-upscaler", "fsr3" }, null).presetPath);
        Assert.IsFalse(PlaySettings.WithArgs(saved, new string[0], null).presetPath);
    }

    [Test]
    public void RoofSkip_OldFileGetsDefault_FlagOverridesForTheRun()
    {
        // A file saved by task 58 has no roofSkip field: it gets the default (OFF).
        var old = PlaySettings.Parse("{\"upscaler\":\"fsr3\",\"rows\":\"dyn\",\"shadowLevel\":0,\"softShadows\":true,\"perfOverlay\":1,\"minimap\":true}", out string e);
        Assert.IsNull(e);
        Assert.IsFalse(old.roofSkip);

        var over = new List<string>();
        var run = PlaySettings.WithArgs(old, new[] { "-roofskip", "0", "-roofskip", "1" }, over);
        Assert.IsTrue(run.roofSkip, "last match wins");
        Assert.IsFalse(old.roofSkip, "the saved settings are untouched");
        CollectionAssert.Contains(over, "roofSkip");

        over.Clear();
        run = PlaySettings.WithArgs(old, new[] { "-roofskip", "yes" }, over);
        Assert.IsFalse(run.roofSkip, "a bad value keeps the saved one");
        CollectionAssert.Contains(over, "-roofskip: bad value 'yes'");
    }

    [Test]
    public void UnderwaterFog_OldFileGetsOn_FlagOverridesForTheRun()
    {
        // Task 66b: a file saved before it has no underwaterFog field: it gets ON.
        var old = PlaySettings.Parse("{\"upscaler\":\"fsr3\",\"rows\":\"dyn\",\"gameMode\":\"survival\"}", out string e);
        Assert.IsNull(e);
        Assert.IsTrue(old.underwaterFog);
        Assert.IsTrue(new PlaySettings().underwaterFog);

        var over = new List<string>();
        var run = PlaySettings.WithArgs(old, new[] { "-underwater", "1", "-underwater", "0" }, over);
        Assert.IsFalse(run.underwaterFog, "last match wins");
        Assert.IsTrue(old.underwaterFog, "the saved settings are untouched");
        CollectionAssert.Contains(over, "underwaterFog");

        over.Clear();
        run = PlaySettings.WithArgs(old, new[] { "-underwater", "off" }, over);
        Assert.IsTrue(run.underwaterFog, "a bad value keeps the saved one");
        CollectionAssert.Contains(over, "-underwater: bad value 'off'");
    }

    [Test]
    public void SeeThroughLiquids_OldFileGetsOn_FlagOverridesForTheRun()
    {
        // Task 75: a file saved before it has no seeThroughLiquids field: it gets ON.
        var old = PlaySettings.Parse("{\"upscaler\":\"fsr3\",\"rows\":\"dyn\",\"underwaterFog\":true}", out string e);
        Assert.IsNull(e);
        Assert.IsTrue(old.seeThroughLiquids);
        Assert.IsTrue(new PlaySettings().seeThroughLiquids);

        var over = new List<string>();
        var run = PlaySettings.WithArgs(old, new[] { "-seethrough", "1", "-seethrough", "0" }, over);
        Assert.IsFalse(run.seeThroughLiquids, "last match wins");
        Assert.IsTrue(old.seeThroughLiquids, "the saved settings are untouched");
        CollectionAssert.Contains(over, "seeThroughLiquids");

        over.Clear();
        run = PlaySettings.WithArgs(old, new[] { "-seethrough", "yes" }, over);
        Assert.IsTrue(run.seeThroughLiquids, "a bad value keeps the saved one");
        CollectionAssert.Contains(over, "-seethrough: bad value 'yes'");
    }

    [Test]
    public void FarShade_OldFileGetsOn_FlagOverridesForTheRun()
    {
        // Task 85b: a file saved before it has no farShade field: it gets ON.
        var old = PlaySettings.Parse("{\"upscaler\":\"fsr3\",\"rows\":\"dyn\",\"seeThroughLiquids\":true}", out string e);
        Assert.IsNull(e);
        Assert.IsTrue(old.farShade);
        Assert.IsTrue(new PlaySettings().farShade);

        var over = new List<string>();
        var run = PlaySettings.WithArgs(old, new[] { "-farshade", "1", "-farshade", "0" }, over);
        Assert.IsFalse(run.farShade, "last match wins");
        Assert.IsTrue(old.farShade, "the saved settings are untouched");
        CollectionAssert.Contains(over, "farShade");

        over.Clear();
        run = PlaySettings.WithArgs(old, new[] { "-farshade", "on" }, over);
        Assert.IsTrue(run.farShade, "a bad value keeps the saved one");
        CollectionAssert.Contains(over, "-farshade: bad value 'on'");
    }

    [Test]
    public void ShadowRes_OldFileGetsFull_FlagOverridesForTheRun_BadValuesClamp()
    {
        // A file saved before task 61 has no shadowRes field: it gets Full.
        var old = PlaySettings.Parse("{\"upscaler\":\"fsr3\",\"rows\":\"dyn\",\"shadowLevel\":0,\"roofSkip\":false}", out string e);
        Assert.IsNull(e);
        Assert.AreEqual("full", old.shadowRes);

        var over = new List<string>();
        var run = PlaySettings.WithArgs(old, new[] { "-shadowres", "full", "-shadowres", "HALF" }, over);
        Assert.AreEqual("half", run.shadowRes, "last match wins, any case");
        Assert.AreEqual("full", old.shadowRes, "the saved settings are untouched");
        CollectionAssert.Contains(over, "shadowRes");

        over.Clear();
        run = PlaySettings.WithArgs(old, new[] { "-shadowres", "quarter" }, over);
        Assert.AreEqual("full", run.shadowRes, "a bad value keeps the saved one");
        CollectionAssert.Contains(over, "-shadowres: bad value 'quarter'");

        // A saved half round-trips; a garbage saved value is clamped to full.
        Assert.AreEqual("half", PlaySettings.Parse("{\"shadowRes\":\"Half\"}", out e).shadowRes);
        Assert.IsNull(e);
        Assert.AreEqual("full", PlaySettings.Parse("{\"shadowRes\":\"x\"}", out e).shadowRes);
        StringAssert.Contains("shadowRes", e);
    }

    [Test]
    public void Shells_OldFileGetsOn_FlagOverridesForTheRun()
    {
        // Task 93c: a file saved before it has no shells field: it gets ON (play's new default).
        var old = PlaySettings.Parse("{\"upscaler\":\"fsr3\",\"rows\":\"dyn\",\"farShade\":true}", out string e);
        Assert.IsNull(e);
        Assert.IsTrue(old.shells);
        Assert.IsTrue(new PlaySettings().shells);
        Assert.IsFalse(PlaySettings.Parse("{\"shells\":false}", out e).shells, "a saved Off is kept");

        var over = new List<string>();
        var run = PlaySettings.WithArgs(old, new[] { "-shells", "1", "-shells", "0" }, over);
        Assert.IsFalse(run.shells, "last match wins");
        Assert.IsTrue(old.shells, "the saved settings are untouched");
        CollectionAssert.Contains(over, "shells");

        over.Clear();
        run = PlaySettings.WithArgs(old, new[] { "-shells", "2" }, over);
        Assert.IsTrue(run.shells, "a bad value keeps the saved one");
        CollectionAssert.Contains(over, "-shells: bad value '2'");
    }

    [Test]
    public void ResolveShells_RigsAndCaptureRunsNeverReadTheFile()
    {
        // Not a play run: the loader must never be called, and the pre-93c rule holds.
        System.Func<PlaySettings> boom = () => { Assert.Fail("a rig/capture run read PlaySettings"); return null; };
        Assert.IsFalse(PlaySettings.ResolveShells(new[] { "-world", "tg2", "-sustained" }, false, boom, out int w));
        Assert.AreEqual(1, w);
        Assert.IsTrue(PlaySettings.ResolveShells(new[] { "-shells", "1", "-shellworkers", "3" }, false, boom, out w));
        Assert.AreEqual(3, w);
        Assert.IsFalse(PlaySettings.ResolveShells(new[] { "-shells", "1", "-shells", "0" }, false, boom, out w), "last match wins");
        Assert.AreEqual(1, w);
    }

    [Test]
    public void ResolveShells_PlayRunTakesTheFileThenTheFlag()
    {
        int loads = 0;
        PlaySettings saved = new PlaySettings();
        System.Func<PlaySettings> load = () => { loads++; return saved; };

        Assert.IsTrue(PlaySettings.ResolveShells(new[] { "-world", "tg2" }, true, load, out int w), "no file = default ON");
        Assert.AreEqual(PlaySettings.PlayShellWorkers, w);
        Assert.AreEqual(3, PlaySettings.PlayShellWorkers, "task 93b's worker count");

        saved = new PlaySettings { shells = false };
        Assert.IsFalse(PlaySettings.ResolveShells(new string[0], true, load, out w), "the saved Off");
        Assert.IsTrue(PlaySettings.ResolveShells(new[] { "-shells", "1", "-shellworkers", "2" }, true, load, out w), "the flag wins for the run");
        Assert.AreEqual(2, w);
        Assert.IsFalse(saved.shells, "a flag never writes the saved settings");

        saved = new PlaySettings();
        Assert.IsFalse(PlaySettings.ResolveShells(new[] { "-shells", "0" }, true, load, out w));
        Assert.AreEqual(4, loads);
    }
    /// Task 145b: tools/play-flags.sh's PLAY_SETTINGS_FLAGS (run-tg2world.sh MODE=playwalk) is PlaySettings'
    /// defaults written as flags. Laid over a file with EVERY field flipped, the flags must give back exactly
    /// the defaults -- so a new field or a changed default fails here until the shell line is updated. The
    /// fields with no flag (softShadows, perfOverlay; minimap is the bare `-minimap`) are listed explicitly.
    [Test]
    public void PlayFlagsFileMatchesPlaySettingsDefaults()
    {
        string path = Path.Combine(UnityEngine.Application.dataPath, "..", "tools", "play-flags.sh");
        Assert.IsTrue(File.Exists(path), path);
        string line = null;
        foreach (string l in File.ReadAllLines(path))
            if (l.StartsWith("PLAY_SETTINGS_FLAGS=\"")) line = l.Substring("PLAY_SETTINGS_FLAGS=\"".Length).TrimEnd('"');
        Assert.IsNotNull(line, "no PLAY_SETTINGS_FLAGS=\"...\" line in " + path);
        string[] args = line.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);

        var d = new PlaySettings();
        var flipped = new PlaySettings
        {
            upscaler = "none", rows = "540", shadowLevel = 3, roofSkip = !d.roofSkip, shadowRes = "half", gameMode = "creative",
            underwaterFog = !d.underwaterFog, seeThroughLiquids = !d.seeThroughLiquids, riverLook = !d.riverLook,
            farShade = !d.farShade, shells = !d.shells,
            // no flag for these: the playwalk run keeps the engine's own value, which IS the default.
            softShadows = d.softShadows, perfOverlay = d.perfOverlay, minimap = d.minimap,
        };
        // Every serialized field must be flipped above (or listed as flagless): a new field fails this guard.
        var keys = new List<string>();
        foreach (var f in typeof(PlaySettings).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            if (!f.IsNotSerialized && !Equals(f.GetValue(flipped), f.GetValue(d))) keys.Add(f.Name);
        var flagless = new[] { "softShadows", "perfOverlay", "minimap" };
        int serialized = 0;
        foreach (var f in typeof(PlaySettings).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            if (!f.IsNotSerialized) serialized++;
        Assert.AreEqual(serialized, keys.Count + flagless.Length, "a PlaySettings field is neither flipped nor flagless: " + string.Join(",", keys));

        var run = PlaySettings.WithArgs(flipped, args, null);
        Assert.AreEqual(d.ToJson(), run.ToJson(), "PLAY_SETTINGS_FLAGS != PlaySettings defaults: " + line);
        Assert.IsFalse(run.presetPath, "the upscaler is named, so -renderpreset must not select the preset path");

        // The two values that are not PlaySettings fields: the preset and the shell workers. RenderPreset lives in
        // Assembly-CSharp (Assets/Game), which this test assembly cannot reference, so its default is read from source.
        var m = System.Text.RegularExpressions.Regex.Match(
            File.ReadAllText(Path.Combine(UnityEngine.Application.dataPath, "Game", "RenderPreset.cs")),
            @"public static Kind Default = Kind\.(\w+);");
        Assert.IsTrue(m.Success, "RenderPreset.Default's declaration not found");
        int pi = System.Array.IndexOf(args, "-renderpreset");
        Assert.IsTrue(pi >= 0 && string.Equals(args[pi + 1], m.Groups[1].Value, System.StringComparison.OrdinalIgnoreCase),
                      "-renderpreset must be RenderPreset.Default (" + m.Groups[1].Value + ")");
        int wi = System.Array.IndexOf(args, "-shellworkers");
        Assert.IsTrue(wi >= 0 && args[wi + 1] == PlaySettings.PlayShellWorkers.ToString(), "-shellworkers must be PlayShellWorkers");
        Assert.IsTrue(System.Array.IndexOf(args, "-minimap") >= 0, "-minimap (play shows it; capture runs hide it unless asked)");
        // Task 159: play's dyn floor (UpscalerSelect, Assembly-CSharp: read from source like RenderPreset.Default).
        var fm = System.Text.RegularExpressions.Regex.Match(
            File.ReadAllText(Path.Combine(UnityEngine.Application.dataPath, "Game", "UpscalerSelect.cs")),
            @"public const int PlayDynFloor = (\d+);");
        Assert.IsTrue(fm.Success, "UpscalerSelect.PlayDynFloor's declaration not found");
        int di = System.Array.IndexOf(args, "-dynfloor");
        Assert.IsTrue(di >= 0 && args[di + 1] == fm.Groups[1].Value, "-dynfloor must be UpscalerSelect.PlayDynFloor (" + fm.Groups[1].Value + ")");
    }
}
