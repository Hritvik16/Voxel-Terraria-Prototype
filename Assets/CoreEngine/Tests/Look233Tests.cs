// Task 233 (owner Q-AP (a)): the three cheap look pieces' CPU side -- the start sun (-startsun), the pale sky's
// lighting colours (-skypale: hue moves, brightness does not; night untouched) and the grade's day-factor weight (-grade).
// The GPU side (Raymarch.compute SkyClouds / Grade233) is judged on the task's stills; this pins the contracts the
// stills rely on.
using System.IO;
using NUnit.Framework;
using Unity.Mathematics;

public class Look233Tests
{
    private static float Luma(float3 c) => math.dot(c, new float3(0.2126f, 0.7152f, 0.0722f));

    [TearDown] public void Reset() { SunCycle.PaleSky = false; }

    [Test]
    public void StartSun_IsMorning_AtTheDefaultElevation()
    {
        float h = SunCycle.HoursForElevation(SunCycle.StartSunLookDegrees, morning: true);
        Assert.Less(h, SunCycle.NoonHours, "the start sun is a MORNING sun (a 20-minute day would set an evening one at once)");
        Assert.AreEqual(SunCycle.StartSunLookDegrees, SunCycle.ElevationDegreesAt(h), 0.05f);
        // 16 deg: past the day factor's knee (full brightness), still golden (halo warmth > 0).
        Assert.AreEqual(1f, SunCycle.LookDayFactorAt(h), 1e-4f);
        Assert.Greater(SunCycle.LookSkyRimAt(h).w, 0.3f, "the horizon rim is still there at the start sun");
    }

    [Test]
    public void StartSunZero_IsNoon()
    {
        Assert.AreEqual(0f, SunCycle.StartSunDefaultDegrees, "default noon: the low sun was over budget (task 233)");
        float keep = SunCycle.StartSunDegrees;
        try
        {
            SunCycle.StartSunDegrees = 0f;
            Assert.IsFalse(SunCycle.StartSunOn);
            Assert.AreEqual(SunCycle.NoonHours, SunCycle.PlayStartHours);
            SunCycle.StartSunDegrees = 16f;
            Assert.AreEqual(16f, SunCycle.ElevationDegreesAt(SunCycle.PlayStartHours), 0.05f);
        }
        finally { SunCycle.StartSunDegrees = keep; }
    }

    [Test]
    public void PaleSky_LightingKeepsItsBrightness_AndOnlyMovesHue()
    {
        foreach (float deg in new[] { 6f, 16f, 25f, 45f })
        {
            float h = SunCycle.HoursForElevation(deg, morning: true);
            SunCycle.PaleSky = false;
            float3 amb0 = SunCycle.LookAmbientAt(h), zen0 = SunCycle.LookSkyZenithAt(h), hor0 = SunCycle.LookSkyHorizonAt(h);
            SunCycle.PaleSky = true;
            float3 amb1 = SunCycle.LookAmbientAt(h), zen1 = SunCycle.LookSkyZenithAt(h), hor1 = SunCycle.LookSkyHorizonAt(h);
            Assert.AreEqual(Luma(amb0), Luma(amb1), 1e-4f, $"ambient brightness moved at {deg} deg");
            Assert.AreEqual(Luma(zen0), Luma(zen1), 1e-4f, $"lighting zenith brightness moved at {deg} deg");
            Assert.AreEqual(Luma(hor0), Luma(hor1), 1e-4f, $"lighting horizon brightness moved at {deg} deg");
            // less blue: the blue share of the lit sky drops (the shade turns less cold)
            Assert.Less(zen1.z / (zen1.x + zen1.y + zen1.z), zen0.z / (zen0.x + zen0.y + zen0.z), $"zenith not paler at {deg} deg");
        }
    }

    [Test]
    public void PaleSky_AtNight_IsTheOldNightSky()
    {
        float h = SunCycle.MidnightHours + 0.5f;
        SunCycle.PaleSky = false;
        float3 zen0 = SunCycle.LookSkyZenithAt(h), hor0 = SunCycle.LookSkyHorizonAt(h), amb0 = SunCycle.LookAmbientAt(h);
        SunCycle.PaleSky = true;
        Assert.AreEqual(zen0, SunCycle.LookSkyZenithAt(h));
        Assert.AreEqual(hor0, SunCycle.LookSkyHorizonAt(h));
        Assert.AreEqual(amb0, SunCycle.LookAmbientAt(h));
        float4 pz = SunCycle.PaleSkyZenithAt(h), ph = SunCycle.PaleSkyHorizonAt(h);
        Assert.AreEqual(zen0, pz.xyz, "the eye's night zenith");
        Assert.AreEqual(hor0, ph.xyz, "the eye's night horizon");
        Assert.AreEqual(0f, ph.w, "cloud / grade weight at night");
    }

    [Test]
    public void PaleSky_ByDay_IsPalerAndBrighterThanToday()
    {
        float h = SunCycle.HoursForElevation(16f, morning: true);
        SunCycle.PaleSky = false;
        float3 zen0 = SunCycle.LookSkyZenithAt(h);
        float4 pz = SunCycle.PaleSkyZenithAt(h);
        Assert.Greater(Luma(pz.xyz), Luma(zen0), "the eye's zenith is brighter (231: their sky luma 0.74 vs ours 0.39)");
        Assert.AreEqual(1f, pz.w, 1e-4f);
    }

    [Test]
    public void Shader_CloudsOnSkyPixelsOnly_GradeBeforeTheWrite()
    {
        string src = File.ReadAllText("Assets/CoreEngine/Rendering/Raymarch.compute");
        StringAssert.Contains("if (!hit && !farHit) skyBg = SkyClouds(rayDir, skyBg);", src);
        StringAssert.Contains("color.rgb = Grade233(color.rgb, id.xy, width, height);", src);
        // Grade233 and SkyClouds both return early on a zero weight (off, or night).
        StringAssert.Contains("if (k <= 0.0) return lin;", src);
        StringAssert.Contains("_SkyPaleHorizon.w <= 0.0", src);
    }
}
