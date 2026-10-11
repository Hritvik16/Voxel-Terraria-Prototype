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
            var a = new PlaySettings { upscaler = "stp", rows = "720", shadowLevel = 2, softShadows = false, perfOverlay = 2, minimap = false, roofSkip = true, underwaterFog = false, seeThroughLiquids = false };
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
}
