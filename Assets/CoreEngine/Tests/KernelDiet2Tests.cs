// ==========================================
// Assets/CoreEngine/Tests/KernelDiet2Tests.cs
//
// TASK 161 (PERF_EXACT_CUTS B, KERNEL DIET 2). CSMainFarShadeLean / CSShellPrepassLean
// (Raymarch.compute VE_DIET2) fold a list of uniforms to `static const`; they are
// exact ONLY while KernelDiet2Rule.Settings refuses every frame where one of
// those settings differs from its folded value. These tests pin the two lists to each
// other: the shader's folded values, each with its uniform kept for every other kernel,
// and the C# check's answer at play's values and with each one changed. The picture
// itself is proven by raw stills (lean vs twin 0 px, task 161's report).
// ==========================================
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

public class KernelDiet2Tests
{
    const string Src = "Assets/CoreEngine/Rendering/Raymarch.compute";

    // The folded uniforms and their values, as KernelDiet2Rule.Settings requires them.
    static readonly (string type, string name, string value)[] Folded =
    {
        ("int", "_UseLODCascade", "0"),
        ("int4", "_DebugModePacked", "int4(0, 0, 0, 0)"),
        ("int", "_TraversalMode", "4"),
        ("int", "_UseDevColors", "1"),
        ("int", "_SkyProbeTilt", "0"),
        ("float", "_ShadowHalfRes", "0.0"),
        ("int", "_Look", "1"),
        ("int", "_ShadowRoofSkip", "0"),
        ("int", "_ShellHoleLeap", "0"),
        ("int", "_ShellHeightBound", "0"),
    };

    [Test]
    public void Shader_FoldsEachUniformOnlyUnderVE_DIET2()
    {
        string src = File.ReadAllText(Src).Replace("\r\n", "\n");
        foreach (var (type, name, value) in Folded)
        {
            string pat = @"#ifdef VE_DIET2\nstatic const " + Regex.Escape(type) + " " + Regex.Escape(name) + " = " + Regex.Escape(value)
                         + @";[^\n]*\n#else\n" + Regex.Escape(type) + @"\s+" + Regex.Escape(name) + @";\n#endif";
            Assert.AreEqual(1, Regex.Matches(src, pat).Count, $"{name}: expected exactly one folded / uniform pair = {value}");
            Assert.AreEqual(1, Regex.Matches(src, @"\n" + Regex.Escape(type) + @"\s+" + Regex.Escape(name) + @";").Count,
                            $"{name}: declared as a uniform more than once");
        }
        StringAssert.Contains("#pragma kernel CSMainFarShadeLean VE_FAR_SHADE VE_DIET2", src);
        StringAssert.Contains("#pragma kernel CSShellPrepassLean VE_FAR_SHADE VE_DIET2", src);
        // The task-134 play kernels (the twins) are not built with VE_DIET2.
        StringAssert.Contains("#pragma kernel CSMainFarShade VE_FAR_SHADE\n", src);
        StringAssert.Contains("#pragma kernel CSShellPrepass VE_FAR_SHADE\n", src);
    }

    static bool Play(bool on = true, bool dbgPx = false, int debugMode = 0, int trav = 4, int level = 0, float half = 0f,
                     int roof = 0, int tilt = 0, int look = 1, bool dev = true, bool hole = false, bool hb = false) =>
        KernelDiet2Rule.Settings(on, dbgPx, debugMode, trav, level, half, roof, tilt, look, dev, hole, hb);

    [Test]
    public void Settings_PlayValuesOnly()
    {
        Assert.IsTrue(Play(), "play's values");
        Assert.IsTrue(Play(level: 3), "sun off (rung 3) keeps the kernel: _ShadowQualityLevel is not folded");
        Assert.IsTrue(Play(level: 2), "rung 2 keeps it too");
        Assert.IsTrue(Play(roof: 2), "roof build-only leaves the shader's _ShadowRoofSkip 0");
        Assert.IsFalse(Play(on: false), "-diet2 0");
        Assert.IsFalse(Play(dbgPx: true), "a DebugOut reader");
        Assert.IsFalse(Play(debugMode: 6), "a debug view");
        foreach (int t in new[] { 0, 1, 2, 3 }) Assert.IsFalse(Play(trav: t), $"traversal mode {t}");
        Assert.IsFalse(Play(level: 1), "rung 1 (quad reuse)");
        Assert.IsFalse(Play(half: 0.001f), "half-res shadows");
        Assert.IsFalse(Play(roof: 1), "roof skip");
        Assert.IsFalse(Play(tilt: 1), "sky tilt 1");
        Assert.IsFalse(Play(tilt: 2), "sky tilt 2");
        Assert.IsFalse(Play(look: 0), "look 0");
        Assert.IsFalse(Play(dev: false), "dev colours off");
        Assert.IsFalse(Play(hole: true), "hole leap");
        Assert.IsFalse(Play(hb: true), "height bound");
    }
}
