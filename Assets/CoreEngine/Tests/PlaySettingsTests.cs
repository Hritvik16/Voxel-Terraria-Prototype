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
        Assert.IsTrue(d.roofSkip);             // task 59: ON for play
    }

    [Test]
    public void SaveThenLoad_RoundTripsEveryField()
    {
        string path = Path.Combine(Path.GetTempPath(), "PlaySettingsTests_" + System.Guid.NewGuid().ToString("N"), PlaySettings.FileName);
        try
        {
            var a = new PlaySettings { upscaler = "stp", rows = "720", shadowLevel = 2, softShadows = false, perfOverlay = 2, minimap = false, roofSkip = false };
            Assert.IsTrue(PlaySettings.Save(path, a, out string werr), werr);
            var b = PlaySettings.Load(path, out string rerr);
            Assert.IsNull(rerr);
            Assert.AreEqual("stp", b.upscaler);
            Assert.AreEqual("720", b.rows);
            Assert.AreEqual(2, b.shadowLevel);
            Assert.IsFalse(b.softShadows);
            Assert.AreEqual(2, b.perfOverlay);
            Assert.IsFalse(b.minimap);
            Assert.IsFalse(b.roofSkip);
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
    public void RoofSkip_OldFileGetsOn_FlagOverridesForTheRun()
    {
        // A file saved by task 58 has no roofSkip field: it must come up ON.
        var old = PlaySettings.Parse("{\"upscaler\":\"fsr3\",\"rows\":\"dyn\",\"shadowLevel\":0,\"softShadows\":true,\"perfOverlay\":1,\"minimap\":true}", out string e);
        Assert.IsNull(e);
        Assert.IsTrue(old.roofSkip);

        var over = new List<string>();
        var run = PlaySettings.WithArgs(old, new[] { "-roofskip", "1", "-roofskip", "0" }, over);
        Assert.IsFalse(run.roofSkip, "last match wins");
        Assert.IsTrue(old.roofSkip, "the saved settings are untouched");
        CollectionAssert.Contains(over, "roofSkip");

        over.Clear();
        run = PlaySettings.WithArgs(old, new[] { "-roofskip", "yes" }, over);
        Assert.IsTrue(run.roofSkip, "a bad value keeps the saved one");
        CollectionAssert.Contains(over, "-roofskip: bad value 'yes'");
    }
}
