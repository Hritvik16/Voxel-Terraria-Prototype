// ==========================================
// Assets/CoreEngine/Tests/SunDeferTests.cs
//
// TASK 168c (PERF_EXACT_CUTS C1). CSMainFarShadeLeanSun hands its two sun-ray shell marches
// (task 133's near continuation, task 80's shell-hit ray) to CSSunDeferLean. The task asked
// first: does sunVis enter the shading LINEARLY (so a later pass could just add or scale a
// term)? It does not -- these tests pin that on a CPU twin of the look combine's sun path,
// and pin the shader's answer to it: the shading is SPLIT (both outcomes shaded, the blocked
// one kept in SunDeferB), every task-168c line sits under VE_SUNDEFER, and the deferred march
// is called with the same arguments as the inline ones. The picture itself is proven by raw
// stills (sun defer on vs off, 0 px; task 168c's report).
// ==========================================
using System;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

public class SunDeferTests
{
    const string Src = "Assets/CoreEngine/Rendering/Raymarch.compute";

    static float Saturate(float x) => Math.Max(0f, Math.Min(1f, x));
    static float Lerp(float a, float b, float t) => a + (b - a) * t;

    // The look combine's sun path for one channel, term for term (Raymarch.compute, LOOK 1):
    // sunExposure = lerp((1 - darkness) * skyOpen, 1, sunVis); direct = sun * sunLight * sunExposure * softened;
    // lit = base * ((direct + ambient) * grade); colour = LookToLinear(lit) = pow(saturate(lit), 2.2).
    static float Shade(float sunVis, float baseC, float sun, float ambient)
    {
        const float darkness = 0.55f, skyOpen = 1f, sunLight = 0.9f, softened = 0.8f, grade = 1f;
        float sunExposure = Lerp((1f - darkness) * skyOpen, 1f, sunVis);
        float direct = sun * sunLight * sunExposure * softened;
        float lit = baseC * ((direct + ambient) * grade);
        return (float)Math.Pow(Saturate(lit), 2.2);
    }

    [Test]
    public void SunVis_EntersTheShadingNonLinearly()
    {
        // Linear in sunVis would mean f(s) = f(0) + s * (f(1) - f(0)) for every s.
        float f0 = Shade(0f, 0.5f, 1.2f, 0.3f), f1 = Shade(1f, 0.5f, 1.2f, 0.3f), fh = Shade(0.5f, 0.5f, 1.2f, 0.3f);
        Assert.Greater(Math.Abs(fh - 0.5f * (f0 + f1)), 1e-3f, "pow 2.2 bends it");
        // And a bright face clips: the colour stops moving with sunVis at all.
        float c0 = Shade(0.6f, 0.9f, 2.0f, 0.4f), c1 = Shade(1f, 0.9f, 2.0f, 0.4f), cz = Shade(0f, 0.9f, 2.0f, 0.4f);
        Assert.AreEqual(c0, c1, 0f, "saturate clips");
        Assert.Less(cz, c1, "but sunVis 0 still darkens it");
        // So the deferred pass cannot rebuild the blocked colour from the drawn one: the shader shades both.
        string src = File.ReadAllText(Src).Replace("\r\n", "\n");
        StringAssert.Contains("sunVis = (sdPass == 0u) ? sdVisU : 0.0;", src);
        StringAssert.Contains("if (sdPass != 0u) { if (inBounds) SunDeferB[id.xy] = color; } else", src);
        StringAssert.Contains("ResultTexture[tid.xy] = SunDeferB[tid.xy];", src);
    }

    [Test]
    public void Shader_DeferredMarchIsTheInlineMarch()
    {
        string src = File.ReadAllText(Src).Replace("\r\n", "\n");
        const string tail = "normalize(_SunDir), {0}, true, rayStart, max(32, _ShellIterCap >> 1), 0.0, ShellSkyY(false));";
        // The two inline calls (every other kernel) and the deferred one, which reads its start / tStart back.
        StringAssert.Contains("ShellMarch(nso, " + string.Format(tail, "sh.exitT"), src);
        StringAssert.Contains("ShellMarch(so, " + string.Format(tail, "0.0"), src);
        StringAssert.Contains("ShellMarch(so, " + string.Format(tail, "asfloat(q.d)"), src);
        StringAssert.Contains("sdPending = true; sdStart = nso; sdT0 = sh.exitT;", src);
        StringAssert.Contains("sdPending = true; sdStart = so; sdT0 = 0.0;", src);
        StringAssert.Contains("#pragma kernel CSMainFarShadeLeanSun VE_FAR_SHADE VE_DIET2 VE_SUNDEFER\n", src);
        StringAssert.Contains("#pragma kernel CSSunDeferLean VE_FAR_SHADE VE_DIET2 VE_SUNDEFER\n", src);
    }

    /// Every line that names a task-168c identifier sits inside an `#ifdef VE_SUNDEFER` (or an `#if` naming it),
    /// so every other kernel compiles from the pre-task source.
    [Test]
    public void Shader_EveryTask168cLineIsUnderVE_SUNDEFER()
    {
        string[] lines = File.ReadAllText(Src).Replace("\r\n", "\n").Split('\n');
        var stack = new System.Collections.Generic.List<bool>();   // per open #if: does it (or its #else) mean VE_SUNDEFER on?
        var ids = new Regex(@"\b(sdPass|sdPending|sdStart|sdT0|sdVisU|sdVao|sdPoint|sdOmni|sdBleed|sdPasses|SunDeferB|SUN_DEFER_MARK)\b");
        int checkedLines = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            string t = lines[i].Trim();
            if (t.StartsWith("#if"))
            {
                bool on = Regex.IsMatch(t, @"^#ifdef\s+VE_SUNDEFER\b") || (t.StartsWith("#if ") && t.Contains("defined(VE_SUNDEFER)") && !t.Contains("!defined(VE_SUNDEFER)"));
                stack.Add(on);
                continue;
            }
            if (t.StartsWith("#else")) { if (stack.Count > 0) stack[stack.Count - 1] = false; continue; }
            if (t.StartsWith("#endif")) { if (stack.Count > 0) stack.RemoveAt(stack.Count - 1); continue; }
            if (t.StartsWith("//") || !ids.IsMatch(lines[i].Split(new[] { "//" }, StringSplitOptions.None)[0])) continue;
            if (t.StartsWith("#pragma") || t.StartsWith("#define")) { if (!t.StartsWith("#define")) continue; }
            Assert.IsTrue(stack.Contains(true), $"line {i + 1} uses a task-168c name outside VE_SUNDEFER: {t}");
            checkedLines++;
        }
        Assert.Greater(checkedLines, 20, "the scan found the task's lines");
    }

    [Test]
    public void Rule_NeedsFlagAndPrepass()
    {
        Assert.IsTrue(KernelDiet2Rule.SunDeferKernel(true, true));
        Assert.IsFalse(KernelDiet2Rule.SunDeferKernel(true, false), "no pre-pass: no ShellPre to carry the rays");
        Assert.IsFalse(KernelDiet2Rule.SunDeferKernel(false, true));
    }
}
