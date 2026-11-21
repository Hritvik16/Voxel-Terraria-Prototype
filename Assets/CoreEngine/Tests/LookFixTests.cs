// Assets/CoreEngine/Tests/LookFixTests.cs
//
// Task 225 (owner Q-AN "why are the tree trunks pitch black"): two exact fixes to the look's display<->linear handling
// (Raymarch.compute _LookFix; RaymarchFeature.LookExactSrgb `-lookexact`, SkyShareLinear `-skylinear`; both ON).
// The look is checked with stills (cost split arms lookfix_off / lookexact_off / skylinear_off); this covers the flags,
// their binding, and the arithmetic each fix rests on, against Unity's own sRGB curve (Mathf.*Space = the encode URP
// applies on output; task 225's probe ramp measured byte 2/255 -> 22, the sRGB value, not a 2.2 power's 28).
using System.IO;
using NUnit.Framework;
using UnityEngine;

public class LookFixTests
{
    const string Shader = "Assets/CoreEngine/Rendering/Raymarch.compute";

    // The shader's two curves, mirrored (LookToLinear bit 0 on / off).
    static float ExactToLinear(float c) => c <= 0.04045f ? c / 12.92f : Mathf.Pow((c + 0.055f) / 1.055f, 2.4f);
    static float ExactToDisplay(float c) => c <= 0.0031308f ? c * 12.92f : 1.055f * Mathf.Pow(c, 1f / 2.4f) - 0.055f;

    [Test]
    public void Flags_DefaultOn_ReachTheShader()
    {
        string cs = File.ReadAllText("Assets/CoreEngine/Rendering/RaymarchFeature.cs");
        StringAssert.Contains("LookExactSrgb = ReadIntArg(\"-lookexact\", 1) != 0;", cs, "ON by default");
        StringAssert.Contains("SkyShareLinear = ReadIntArg(\"-skylinear\", 1) != 0;", cs, "ON by default");
        StringAssert.Contains("(LookExactSrgb ? 1 : 0) | (SkyShareLinear ? 2 : 0)", cs, "bit 0 / bit 1");
        StringAssert.Contains("\"_LookFix\", data.lookFix)", cs, "the bits reach the shader");
    }

    [Test]
    public void Shader_CurvesAreTheSrgbPair_SkyShareSitesUseTheHelper()
    {
        string s = File.ReadAllText(Shader);
        StringAssert.Contains("lerp(pow((c + 0.055) / 1.055, 2.4), c / 12.92, step(c, 0.04045))", s, "LookToLinear: the sRGB EOTF");
        StringAssert.Contains("lerp(1.055 * pow(c, 1.0 / 2.4) - 0.055, c * 12.92, step(c, 0.0031308))", s, "LookToDisplay: the sRGB encode");
        StringAssert.Contains("if ((_LookFix & 1) == 0) return pow(c, 2.2);", s, "OFF = the old curve");
        StringAssert.Contains("pow(saturate(skyVis), 1.0 / 2.2) : skyVis", s, "bit 1: the share in linear light; OFF = old");
        // Every look-1 skyOpen goes through LookSkyOpen; look 0 (linear lighting, line `ambientScale`) keeps skyVis as is.
        Assert.AreEqual(1, Count(s, "lerp(_UndergroundAmbientFloor, 1.0, v)"), "only inside LookSkyOpen");
        Assert.AreEqual(1, Count(s, "lerp(_UndergroundAmbientFloor, 1.0, skyVis)"), "look 0's ambientScale only");
        Assert.AreEqual(0, Count(s, "1.0 / 2.2);   // LookToLinear's inverse"), "the inverses follow LookToLinear");
    }

    [Test]
    public void ExactCurve_MatchesUnitySrgb_AndRoundTrips()
    {
        for (int i = 0; i <= 255; i++)
        {
            float d = i / 255f;
            Assert.AreEqual(Mathf.GammaToLinearSpace(d), ExactToLinear(d), 2e-4f, $"EOTF at {i}");
            Assert.AreEqual(d, Mathf.LinearToGammaSpace(ExactToLinear(d)), 1e-3f, $"display {i} reaches the screen unchanged");
            Assert.AreEqual(d, ExactToDisplay(ExactToLinear(d)), 1e-4f, $"inverse at {i}");
        }
    }

    [Test]
    public void OldCurve_CrushedTheShadedTrunkRange()
    {
        // Task 225's probe: a shaded jungle trunk's look value 0.030, a sunlit one 0.067, a shaded forest trunk 0.147.
        float Old(float d) => Mathf.LinearToGammaSpace(Mathf.Pow(d, 2.2f));
        Assert.Less(Old(0.030f), 0.030f * 0.25f, "5x darker on screen than the look's value");
        Assert.Less(Old(0.067f), 0.067f * 0.6f);
        Assert.AreEqual(0.5f, Old(0.5f), 0.01f, "mid-tones: the two curves agree");
        // Bit 1: a 25 % open sky gave 0.25^2.2 = 4.7 % of the open sky's linear light; now 25 %.
        float share = 0.25f, open = 0.4f;   // any display-space open-sky light
        Assert.AreEqual(0.047f, Mathf.Pow(open * share, 2.2f) / Mathf.Pow(open, 2.2f), 0.001f);
        Assert.AreEqual(share, Mathf.Pow(open * Mathf.Pow(share, 1f / 2.2f), 2.2f) / Mathf.Pow(open, 2.2f), 1e-4f);
    }

    static int Count(string s, string sub)
    {
        int n = 0;
        for (int i = s.IndexOf(sub); i >= 0; i = s.IndexOf(sub, i + 1)) n++;
        return n;
    }
}
