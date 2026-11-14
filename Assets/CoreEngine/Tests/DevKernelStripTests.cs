// ==========================================
// Assets/CoreEngine/Tests/DevKernelStripTests.cs
//
// TASK 194b: player builds leave five Raymarch kernels out (Assets/Editor/DevKernelStrip.cs, build time only;
// VE_ALL_KERNELS=1 keeps them). The Editor assembly is not reachable from here, so these tests read the sources:
// the list names real kernels, none of them a kernel play dispatches, each one guarded in RaymarchFeature
// (KernelStripped / KernelStrippedWarn) so a run that asks for it draws with play's kernel instead of dispatching
// a kernel the build does not have; and the build stamp's `kernels` line parses back to the same list.
// ==========================================
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using VoxelEngine.Rendering;

public class DevKernelStripTests
{
    const string Strip = "Assets/Editor/DevKernelStrip.cs";
    const string Shader = "Assets/CoreEngine/Rendering/Raymarch.compute";
    const string Feature = "Assets/CoreEngine/Rendering/RaymarchFeature.cs";

    static readonly string[] Expected =
        { "CSMainFarShadeDiag", "CSShellPrepassDiag", "CSShellProbe", "CSMainChecker", "CSMainDetail" };

    // What play dispatches (task 134 lean / 161 diet-2 / 168 roof / 168c sun defer / 158 torch flood, the height
    // pyramid, far band, TAA). None of these may ever be stripped.
    static readonly string[] PlayKernels =
    {
        "CSMain", "CSMainFarShade", "CSShellPrepass", "CSMainFlood", "CSMainFarShadeFlood",
        "CSMainFarShadeLean", "CSShellPrepassLean", "CSMainFarShadeLeanRoof", "CSMainFarShadeLeanSun", "CSSunDeferLean",
        "BuildHeightRaw", "BuildHeightDilate", "BuildHeightLevel", "BuildHeightRawCols", "BuildHeightDilateCols",
        "BuildHeightLevelCols", "FarScatter", "FarMipBuild", "TaaResolve", "TaaSharpen",
    };

    static string[] StripList()
    {
        string src = File.ReadAllText(Strip);
        var block = Regex.Match(src, @"Kernels\s*=\s*\{([^}]*)\}", RegexOptions.Singleline);
        Assert.IsTrue(block.Success, "DevKernelStrip.Kernels initialiser not found");
        return Regex.Matches(block.Groups[1].Value, "\"([A-Za-z0-9_]+)\"").Cast<Match>().Select(m => m.Groups[1].Value).ToArray();
    }

    static string[] ShaderKernels() =>
        Regex.Matches(File.ReadAllText(Shader), @"^#pragma kernel (\w+)", RegexOptions.Multiline)
             .Cast<Match>().Select(m => m.Groups[1].Value).ToArray();

    [Test]
    public void StripList_IsTheFiveDevKernels_AllRealAndNoneDispatchedByPlay()
    {
        string[] list = StripList();
        CollectionAssert.AreEquivalent(Expected, list, "change this test only with the owner's approval (task 194b)");
        string[] kernels = ShaderKernels();
        foreach (string k in list)
        {
            CollectionAssert.Contains(kernels, k, $"{k} is not a #pragma kernel in Raymarch.compute");
            CollectionAssert.DoesNotContain(PlayKernels, k, $"{k} is a play kernel");
        }
        foreach (string k in PlayKernels) CollectionAssert.Contains(kernels, k, $"play kernel {k} missing from Raymarch.compute");
    }

    [Test]
    public void EveryStrippedKernel_IsGuardedInRaymarchFeature()
    {
        string src = File.ReadAllText(Feature);
        foreach (string k in StripList())
        {
            // CSShellPrepassDiag is picked only while shellDiag holds, which the CSMainFarShadeDiag guard clears.
            string guard = k == "CSShellPrepassDiag" ? "CSMainFarShadeDiag" : k;
            StringAssert.IsMatch(@"KernelStripped(Warn)?\(""" + guard + @"""", src, $"no KernelStripped guard for {k}");
        }
        StringAssert.Contains("shellDiag ? \"CSShellPrepassDiag\" : \"CSShellPrepass\"", src);
    }

    [Test]
    public void StampLine_ParsesBack()
    {
        string stamp = "commit    abc123 (clean)\nsubject   x\nbuilt     2026-11-13 12:00:00Z\nunity     6000.3.10f1\n" +
                       "kernels   stripped " + string.Join(",", Expected);
        CollectionAssert.AreEqual(Expected, KernelStripStamp.Parse(stamp));
        CollectionAssert.IsEmpty(KernelStripStamp.Parse("commit    abc\nkernels   all\n"));
        CollectionAssert.IsEmpty(KernelStripStamp.Parse("commit    abc\n"));   // stamp from before 194b
        CollectionAssert.IsEmpty(KernelStripStamp.Parse(null));
        StringAssert.Contains("kernels   {DevKernelStrip.StampValue}", File.ReadAllText("Assets/Editor/BuildStamp.cs"));
    }
}
