// Assets/CoreEngine/Tests/FarRockSlopeTests.cs
//
// Task 119 (VISUAL_REVIEW_114 #3): the far sun-shadow ray starts one texel up (FarShadowLift: the
// real cause of the dashes was the staircase shadowing itself), and far-band step sides are rock by SLOPE (Raymarch.compute,
// FAR_ROCK_SLOPE_NY over FarSlopeNormal, far-shading kernel) instead of by FAR_WALL_LIP.
// The look is checked with stills (run-look-review.sh); this covers the flag's text and the shader text.
using System.IO;
using NUnit.Framework;

public class FarRockSlopeTests
{
    [Test]
    public void Flag_DefaultsOn_AndIsBound()
    {
        // RaymarchFeature derives from a URP type the test assembly does not reference: read its text.
        string cs = File.ReadAllText("Assets/CoreEngine/Rendering/RaymarchFeature.cs");
        StringAssert.Contains("if (a[i] == flag) on = a[i + 1] != \"0\";", cs, "ON unless the last value is 0");
        StringAssert.Contains("ParseFlagOn(a, \"-farrockslope\")", cs);
        StringAssert.Contains("ParseFlagOn(System.Environment.GetCommandLineArgs(), \"-farshadowlift\")", cs);
        StringAssert.Contains("\"_FarRockSlope\", FarRockSlope ? 1 : 0", cs, "the flags reach the shader");
        StringAssert.Contains("\"_FarShadowLift\", FarShadowLift ? 1 : 0", cs);
    }

    [Test]
    public void Shader_SlopeRuleIsInTheFarShadeKernelOnly_AndKeepsTheLipFallback()
    {
        string src = File.ReadAllText("Assets/CoreEngine/Rendering/Raymarch.compute");
        int use = src.IndexOf("_FarRockSlope != 0 && FarSlopeNormal(");
        Assert.Greater(use, 0, "the slope rule asks FarSlopeNormal");
        int ifdef = src.LastIndexOf("#ifdef VE_FAR_SHADE", use);
        int endif = src.LastIndexOf("#endif", use);
        Assert.Greater(ifdef, endif, "the slope rule sits inside #ifdef VE_FAR_SHADE, so other kernels keep the lip byte for byte");
        StringAssert.Contains("> FAR_WALL_LIP) ? 1u : 0u;", src, "the lip rule stays as the fallback (a uint since task 121: Metal)");
        StringAssert.Contains("#define FAR_ROCK_SLOPE_NY 0.766", src, "rock above ~40 deg (cos 40 = 0.766)");
    }

    [Test]
    public void Shader_ShadowLiftIsOneTexel_InTheFarShadeKernelOnly()
    {
        string src = File.ReadAllText("Assets/CoreEngine/Rendering/Raymarch.compute");
        int use = src.IndexOf("if (_FarShadowLift != 0) hp.y += (float)(8 << fh.level);");
        Assert.Greater(use, 0, "the far sun-shadow ray starts one texel of the hit's level up");
        Assert.Greater(src.LastIndexOf("#ifdef VE_FAR_SHADE", use), src.LastIndexOf("#endif", use),
            "inside #ifdef VE_FAR_SHADE: rigs without far shading keep their shadows byte for byte");
        Assert.Less(use, src.IndexOf("FarHit fs = FarMarch(hp,"), "before the shadow march");
    }
}
