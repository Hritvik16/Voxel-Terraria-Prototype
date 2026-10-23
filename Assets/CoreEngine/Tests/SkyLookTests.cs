// Assets/CoreEngine/Tests/SkyLookTests.cs
//
// Task 121 (VISUAL_REVIEW_114 #4 + #6): the sky look -- horizon-weighted gradient, time-of-day rim tint,
// sun disc, shorter + desaturating haze. The look is checked with stills (run-look-review.sh -looksky 1);
// this covers the flags, that they reach the shader, that the old sky formula is still the off path,
// and the rim curve (0 by day and at night, warm at golden hour, redder at dusk).
using System.IO;
using NUnit.Framework;
using Unity.Mathematics;

public class SkyLookTests
{
    [Test]
    public void Flags_DefaultOn_AndAreBound()
    {
        string cs = File.ReadAllText("Assets/CoreEngine/Rendering/RaymarchFeature.cs");
        StringAssert.Contains("SkyLook = ParseFlagOn(System.Environment.GetCommandLineArgs(), \"-skylook\")", cs);
        StringAssert.Contains("SkyHaze = SkyLook && ParseFlagOn(System.Environment.GetCommandLineArgs(), \"-skyhaze\")", cs);
        StringAssert.Contains("\"_SkyLook\", (SkyLook ? 1 : 0) | (SkyHaze ? 2 : 0)", cs, "the flags reach the shader");
        StringAssert.Contains("\"_LookSkyRim\", data.lookSkyRim", cs);
        StringAssert.Contains("(SkyHaze ? SkyHazeRangeM : FarShadeHazeRangeM) * 10f", cs, "-skyhaze 0 keeps the 3500 m range");
        Assert.AreEqual(3500f, RaymarchFeature.FarShadeHazeRangeM, "the old range is unchanged");
        Assert.Less(RaymarchFeature.SkyHazeRangeM, RaymarchFeature.FarShadeHazeRangeM);
    }

    [Test]
    public void Shader_OffPathIsTheOldSky_AndDiscOnlyInTheSky()
    {
        string src = File.ReadAllText("Assets/CoreEngine/Rendering/Raymarch.compute");
        StringAssert.Contains("baseSky = lerp(_LookSkyHorizon.rgb, _LookSkyZenith.rgb, saturate(dir.y * 0.5 + 0.5));", src, "-skylook 0 = the old gradient");
        StringAssert.Contains("color = float4(LookToLinear(LookShoulder(LookSkyBackground(rayDir), _LookGrade.z)), 1.0);", src, "a miss sees the disc");
        int haze = src.IndexOf("float3 FarShadeApplyHaze(");
        Assert.Greater(haze, 0);
        int end = src.IndexOf("\n}", haze);
        Assert.AreEqual(-1, src.Substring(haze, end - haze).IndexOf("LookSkyBackground"), "the haze target has no sun disc");
        StringAssert.Contains("if ((_SkyLook & 2) != 0)", src.Substring(haze, end - haze), "desaturation only with -skyhaze on");
    }

    [Test]
    public void Rim_ZeroByDayAndNight_WarmAtGoldenHour_RedderAtDusk()
    {
        Assert.AreEqual(0f, SunCycle.LookSkyRimAt(SunCycle.NoonHours).w, 1e-6f, "noon: no rim");
        Assert.AreEqual(0f, SunCycle.LookSkyRimAt(SunCycle.MidnightHours).w, 1e-6f, "night: no rim");
        float4 golden = SunCycle.LookSkyRimAt(17f), dusk = SunCycle.LookSkyRimAt(SunCycle.HoursForElevation(-3f, false));
        Assert.Greater(golden.w, 0.3f, "golden hour: the rim shows");
        Assert.Greater(dusk.w, 0.5f, "dusk: the rim shows");
        Assert.Greater(golden.x, golden.z, "golden rim is warm");
        Assert.Less(dusk.y / dusk.x, golden.y / golden.x, "dusk is redder than golden hour");
        Assert.Less(math.cmax(dusk.xyz), math.cmax(golden.xyz), "dusk is dimmer");
    }
}
