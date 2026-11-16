// Assets/CoreEngine/Tests/DashFixTests.cs
//
// Task 205 (task 198's option 1, `-dashfix 1|2`; task 215: default 2, `-dashfix 0` = old): the white 1-px dashes on steep walls are sun-lit
// treads lit by their own face where FarSlopeNormal's bracket test fails. The fix keeps the filtered cube light
// there for sky-open hits. The look is checked with stills (-worldsurvey arms native_df1 / dyn_df1); this covers
// the flag (2 by default since task 215, last match wins), its binding, and the shader contract: OFF calls the old
// CubeFilterWeight unchanged; FarSlopeCode 2 == FarSlopeNormal's true; mode 2 = top faces only, undersides never.
using System.IO;
using NUnit.Framework;

public class DashFixTests
{
    [Test]
    public void Flag_Default2_LastMatchWins()
    {
        string cs = File.ReadAllText("Assets/CoreEngine/Rendering/RaymarchFeature.cs");
        // RaymarchFeature derives from a URP type the test assembly does not reference: read its text.
        int ps = cs.IndexOf("public static int ParseArgInt(string[] a, string flag, int def)");
        Assert.Greater(ps, 0);
        string body = cs.Substring(ps, 300);
        StringAssert.Contains("int v = def;", body, "absent = the default");
        StringAssert.Contains("if (a[i] == flag) v = int.TryParse(a[i + 1], out int n) ? n : def;", body, "the last match wins");
        StringAssert.Contains("DashFix = ValidDashFix(ParseArgInt(System.Environment.GetCommandLineArgs(), \"-dashfix\", 2));", cs, "task 215: 2 (top faces) by default");
        StringAssert.Contains("public static int ValidDashFix(int v) => v == 1 || v == 2 ? v : 0;", cs, "only 1 and 2 turn it on");
        StringAssert.Contains("\"_DashFix\", DashFix)", cs, "the flag reaches the shader");
    }

    [Test]
    public void Shader_OffIsTheOldCall_OnKeepsUndersidesAndCaves()
    {
        string src = File.ReadAllText("Assets/CoreEngine/Rendering/Raymarch.compute");
        int on = src.IndexOf("if (_DashFix != 0)");
        Assert.Greater(on, 0, "the fix is behind _DashFix");
        string site = src.Substring(on, 600);
        StringAssert.Contains("(normal.y > 0 || (_DashFix == 1 && normal.y == 0)) ? skyVis : 0.0", site,
                              "mode 2: top faces only; mode 1: sides too; undersides and sky-closed hits keep their face light");
        StringAssert.Contains("cubeW = CubeFilterWeight(rayStart + rayDir * currentDist, currentDist, lm, nSlope);", site,
                              "OFF = the task 120 call, unchanged");
        // FarSlopeCode: 2 exactly where FarSlopeNormal's bracket test holds, 1 / 3 below / above it, 0 with no ring.
        StringAssert.Contains("return (p.y < lo - tv) ? 1u : (p.y > hi + tv) ? 3u : 2u;", src, "1 below, 3 above the bracket");
        int w = src.IndexOf("float CubeFilterWeightOpen(");
        Assert.Greater(w, 0);
        string body = src.Substring(w, 500);
        StringAssert.Contains("if (code == 2u) return w;", body);
        StringAssert.Contains("if (code == 1u || code == 3u) return w * saturate(open);", body, "both sides of the bracket");
        // The far band's own call is untouched (heightfield hits lie inside the bracket by construction).
        StringAssert.Contains("cubeWF = CubeFilterWeight(rayStart + rayDir * fh.t, fh.t, fm, nSlopeF);", src);
    }
}
