// Assets/CoreEngine/Tests/DrawnEdgeFadeTests.cs
//
// Task 125 (VISUAL_REVIEW_114, the hard sea/sky line): the line is the far band's square, not the world's
// edge. Task 121 faded the haze to full there, but the far water's sky reflection and the grade came after
// the haze. -edgefade (ON) moves that fade to the pixel's last step, toward exactly the sky a miss shows.
// The look is checked with stills (run-look-review.sh -lookedge 1); this covers the flag, that it reaches
// the shader, that -edgefade 0 is task 121's path, that the fade runs after every see-through blend, and
// the capture's band-edge constant.
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

public class DrawnEdgeFadeTests
{
    const string Shader = "Assets/CoreEngine/Rendering/Raymarch.compute";

    [Test]
    public void Flag_DefaultOn_NeedsSkyHaze_AndIsBound()
    {
        string cs = File.ReadAllText("Assets/CoreEngine/Rendering/RaymarchFeature.cs");
        StringAssert.Contains("EdgeFade = SkyHaze && ParseFlagOn(System.Environment.GetCommandLineArgs(), \"-edgefade\")", cs, "-skyhaze 0 stays the pre-121 haze");
        StringAssert.Contains("(SkyLook ? 1 : 0) | (SkyHaze ? 2 : 0) | (EdgeFade ? 4 : 0)", cs, "the flag reaches the shader as bit 2");
    }

    [Test]
    public void Shader_OffPathIsTask121_FadeIsTheLastStep_TowardTheMissColour()
    {
        string src = File.ReadAllText(Shader);
        int haze = src.IndexOf("float3 FarShadeApplyHaze(");
        Assert.Greater(haze, 0);
        string hazeBody = src.Substring(haze, src.IndexOf("\n}", haze) - haze);
        StringAssert.Contains("if ((_SkyLook & 4) == 0) h = max(h, DrawnEdgeAmount(rayDir, distVoxels));", hazeBody, "-edgefade 0 = task 121's fade inside the haze");

        int fade = src.IndexOf("float3 DrawnEdgeFade(");
        Assert.Greater(fade, 0);
        string fadeBody = src.Substring(fade, src.IndexOf("\n}", fade) - fade);
        StringAssert.Contains("LookShoulder(LookAtmosphere(rayDir), _LookGrade.z)", fadeBody, "the target is the miss colour without the disc");
        StringAssert.Contains("color = float4(LookToLinear(LookShoulder(LookSkyBackground(rayDir), _LookGrade.z)), 1.0);", src, "a miss is shouldered the same way");

        int call = src.IndexOf("color.rgb = DrawnEdgeFade(color.rgb, rayDir, hit ? currentDist : fh.t);");
        Assert.Greater(call, 0, "the fade is applied in the body");
        StringAssert.Contains("if ((_SkyLook & 6) == 6", src.Substring(call - 400, 400), "only with -skyhaze and -edgefade");
        Assert.Greater(call, src.LastIndexOf("SeeReflection(rayDir, sunVisF)"), "after the far water's see-through blend");
        Assert.Greater(call, src.LastIndexOf("SeeReflection(rayDir, sunVis))"), "after the voxel water's see-through blend");
        Assert.Less(call, src.IndexOf("---- 66b: THE FLUID BETWEEN THE EYE"), "before the in-fluid view (which it skips)");
    }

    [Test]
    public void CaptureBandEdge_MatchesTheShader()
    {
        string src = File.ReadAllText(Shader);
        int radius = int.Parse(Regex.Match(src, @"#define FAR_USE_RADIUS (\d+)").Groups[1].Value);
        int levels = int.Parse(Regex.Match(src, @"#define FAR_LEVELS (\d+)").Groups[1].Value);
        StringAssert.Contains("const float bandM = 440f * 128f * 0.1f;", File.ReadAllText("Assets/Game/PlaygroundCapture.LookReview.cs"));
        Assert.AreEqual(440 * 128, radius * (8 << (levels - 1)), "LookEdgeAB's predicted band-edge rows use the shader's square");
    }
}
