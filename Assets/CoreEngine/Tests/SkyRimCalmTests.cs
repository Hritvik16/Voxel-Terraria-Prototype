// Assets/CoreEngine/Tests/SkyRimCalmTests.cs
//
// Task 126 (owner Q77, 2026-10-23): the sky rim's red calmed at dusk (and a little at sunset), `-skyrimscale`.
// The look is checked with stills (run-look-review.sh -looksky 1 -lookrim 1); this covers the flag, that the
// shape reaches the shader, that scale 1 is task 121's rim, that golden hour and noon do not change, and
// that dusk is calmer / tighter while sunset keeps most of its glow.
using System.IO;
using NUnit.Framework;
using Unity.Mathematics;

public class SkyRimCalmTests
{
    static float Dusk => SunCycle.HoursForElevation(-3f, false);
    static float Sunset => SunCycle.HoursForElevation(2f, false);

    [Test]
    public void Flag_IsBound_AndReachesTheShader()
    {
        string cs = File.ReadAllText("Assets/CoreEngine/Rendering/RaymarchFeature.cs");
        StringAssert.Contains("SkyRimScale = Mathf.Clamp01(ReadFloatArg(\"-skyrimscale\", SkyRimScaleDefault))", cs);
        StringAssert.Contains("SunCycle.LookSkyRimAt(hrs, SkyRimScale)", cs);
        StringAssert.Contains("\"_LookSkyRimShape\", data.lookSkyRimShape", cs);
        Assert.Less(RaymarchFeature.SkyRimScaleDefault, 1f, "the default is calmer than task 121");
        string src = File.ReadAllText("Assets/CoreEngine/Rendering/Raymarch.compute");
        StringAssert.Contains("lerp(_LookSkyRimShape.x, 1.0, facing * facing)", src);
        StringAssert.Contains("pow(cosSun, _LookSkyRimShape.y)", src);
    }

    [Test]
    public void ScaleOne_IsTask121()
    {
        // Task 121's own numbers (Screenshots/121/run3/lookreview.txt), and its shader constants 0.4 / 8.
        float4 d = SunCycle.LookSkyRimAt(Dusk, 1f);
        Assert.AreEqual(0.5461177f, d.x, 1e-4f); Assert.AreEqual(0.239602f, d.y, 1e-4f);
        Assert.AreEqual(0.1663922f, d.z, 1e-4f); Assert.AreEqual(0.8737993f, d.w, 1e-4f);
        foreach (float h in new[] { Dusk, Sunset, 17f, SunCycle.NoonHours })
        {
            float4 s = SunCycle.LookSkyRimShapeAt(h, 1f);
            Assert.AreEqual(0.4f, s.x, 1e-6f); Assert.AreEqual(8f, s.y, 1e-6f);
            Assert.AreEqual(SunCycle.LookSkyRimAt(h), SunCycle.LookSkyRimAt(h, 1f));
        }
    }

    [Test]
    public void GoldenHourAndNoon_DoNotChange()
    {
        float sc = RaymarchFeature.SkyRimScaleDefault;
        foreach (float h in new[] { 17f, SunCycle.NoonHours, 16f })
        {
            Assert.GreaterOrEqual(SunCycle.ElevationDegreesAt(h), 6f);
            Assert.AreEqual(SunCycle.LookSkyRimAt(h, 1f), SunCycle.LookSkyRimAt(h, sc), $"{h} h rim");
            Assert.AreEqual(SunCycle.LookSkyRimShapeAt(h, 1f), SunCycle.LookSkyRimShapeAt(h, sc), $"{h} h shape");
        }
    }

    [Test]
    public void Dusk_Calmer_Sunset_ALittle()
    {
        float sc = RaymarchFeature.SkyRimScaleDefault;
        float4 d0 = SunCycle.LookSkyRimAt(Dusk, 1f), d1 = SunCycle.LookSkyRimAt(Dusk, sc);
        float4 s0 = SunCycle.LookSkyRimAt(Sunset, 1f), s1 = SunCycle.LookSkyRimAt(Sunset, sc);
        Assert.Less(d1.w, 0.6f * d0.w, "dusk: much weaker rim");
        Assert.Greater(d1.z / d1.x, d0.z / d0.x, "dusk: bluer (violet-brown), not red");
        Assert.Less(s1.w, s0.w, "sunset: a little weaker");
        Assert.Greater(s1.w, 0.75f * s0.w, "sunset: still glowing");
        Assert.AreEqual(s0.xyz, s1.xyz, "sunset keeps its orange");
        float4 dShape = SunCycle.LookSkyRimShapeAt(Dusk, sc), sShape = SunCycle.LookSkyRimShapeAt(Sunset, sc);
        Assert.Less(dShape.x, 0.4f, "dusk: less rim away from the sun");
        Assert.Greater(dShape.y, sShape.y, "dusk's glow is tighter than sunset's");
        Assert.Greater(sShape.y, 8f);
    }
}
