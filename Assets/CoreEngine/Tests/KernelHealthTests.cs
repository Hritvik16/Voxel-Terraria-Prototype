// ==========================================
// Assets/CoreEngine/Tests/KernelHealthTests.cs
//
// TASK 206 (bug from task 198): debug views 1-14 and the debug pixel were broken in PLAYER builds -- CSMainFarShade
// failed at run time: HLSLcc turned SkyHistFetch's early return into `as_type<float>(bool)`, which Metal rejects;
// the torch-flood twins (CSMainFarShadeFlood = play's kernel with the flood on, CSMainFlood) had 33 Metal buffer
// slots (cap 31). The frame silently kept the last image. What can be tested CPU-side:
//   * KernelHealth's heartbeat state machine and decision (the loud path: one error, a magenta frame, NaN debug slots);
//   * its kernel-name list = the shader's #pragma kernel lines;
//   * the shader fix's shape: every non-diet-2 kernel reads the height pyramid through the read-only alias, the
//     diet-2 play kernels keep the RW read and the old SkyHistFetch loop (their code must not change);
//   * SkyHistFetch's new loop (backwards, keep the last match) returns what the old one (forwards, first match)
//     did, on every probe pattern;
//   * RaymarchFeature binds the alias, checks every dispatched kernel, and paints the frame on a failure.
// The GPU side (the kernels load in a player, debug views draw) is proven by the task's player launch only:
// ShaderCompileCheck can report a green shader that does not build (KNOWN_ISSUES).
// ==========================================
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using VoxelEngine.Rendering;

public class KernelHealthTests
{
    const string Shader = "Assets/CoreEngine/Rendering/Raymarch.compute";
    const string Feature = "Assets/CoreEngine/Rendering/RaymarchFeature.cs";

    [Test]
    public void Heartbeat_NaN_MarksFailed_AndStaysFailed()
    {
        int good = 0;
        var s = KernelHealth.Next(KernelHealth.State.Unknown, ref good, float.NaN);
        Assert.AreEqual(KernelHealth.State.Failed, s, "a NaN slot = the kernel did not overwrite it = it never ran");
        Assert.AreEqual(KernelHealth.State.Failed, KernelHealth.Next(s, ref good, 0.5f), "a Metal compile failure is permanent: sticky");
    }

    [Test]
    public void Heartbeat_GoodReads_Verify_AndStayVerified()
    {
        int good = 0;
        var s = KernelHealth.State.Unknown;
        for (int i = 1; i < KernelHealth.VerifyReads; i++)
        {
            s = KernelHealth.Next(s, ref good, -0.3f);
            Assert.AreEqual(KernelHealth.State.Unknown, s, $"read {i}: not yet verified");
        }
        s = KernelHealth.Next(s, ref good, 0.7f);
        Assert.AreEqual(KernelHealth.State.Verified, s);
        Assert.AreEqual(KernelHealth.State.Verified, KernelHealth.Next(s, ref good, float.NaN),
            "verified kernels are no longer checked (nothing per frame): a later NaN read is ignored");
        // Zero is a valid ray-direction component (a level ray): only NaN means "not run".
        good = 0;
        Assert.AreNotEqual(KernelHealth.State.Failed, KernelHealth.Next(KernelHealth.State.Unknown, ref good, 0f));
    }

    [Test]
    public void Decision_FailsWhenUnsupportedOrHeartbeatFailed()
    {
        Assert.IsFalse(KernelHealth.Decide(true, KernelHealth.State.Unknown));
        Assert.IsFalse(KernelHealth.Decide(true, KernelHealth.State.Verified));
        Assert.IsTrue(KernelHealth.Decide(true, KernelHealth.State.Failed));
        Assert.IsTrue(KernelHealth.Decide(false, KernelHealth.State.Unknown));
    }

    [Test]
    public void Heartbeat_OnlyWhenThePixelIsInsideTheDispatch()
    {
        Assert.IsTrue(KernelHealth.PixelInside(new UnityEngine.Vector2Int(700, 400), 960, 540));
        Assert.IsFalse(KernelHealth.PixelInside(new UnityEngine.Vector2Int(700, 400), 640, 360), "outside: never written, no check");
        Assert.IsFalse(KernelHealth.PixelInside(new UnityEngine.Vector2Int(960, 10), 960, 540));
        Assert.IsFalse(KernelHealth.PixelInside(new UnityEngine.Vector2Int(-1, 10), 960, 540));
    }

    [Test]
    public void KernelNameList_IsTheShadersPragmaList()
    {
        string src = File.ReadAllText(Shader);
        var pragmas = Regex.Matches(src, @"^#pragma kernel ([A-Za-z0-9_]+)", RegexOptions.Multiline)
                           .Cast<Match>().Select(m => m.Groups[1].Value).ToArray();
        CollectionAssert.AreEquivalent(pragmas, KernelHealth.RaymarchKernels);
    }

    [Test]
    public void Shader_NonDietKernels_ReadHeightPyramidReadOnly_DietKeepsRW()
    {
        string src = File.ReadAllText(Shader);
        var block = Regex.Match(src, @"#ifdef VE_DIET2\s*\n#define HEIGHT_MIP_READ HeightMip\s*\n#else\s*\nStructuredBuffer<uint> HeightMipRO;\s*\n#define HEIGHT_MIP_READ HeightMipRO\s*\n#endif");
        Assert.IsTrue(block.Success, "the HEIGHT_MIP_READ alias block (diet 2 = RW, else read-only)");
        // ReadHeight is the only raymarch read of the pyramid; the Build* kernels and HeightVerify keep the RW buffer.
        var rh = Regex.Match(src, @"uint ReadHeight\(int L, int bx, int bz\)\s*\{(.*?)\n\}", RegexOptions.Singleline);
        Assert.IsTrue(rh.Success);
        StringAssert.Contains("HEIGHT_MIP_READ[", rh.Groups[1].Value);
        StringAssert.DoesNotContain("HeightMip[", rh.Groups[1].Value);
    }

    [Test]
    public void Shader_FloodTwins_FoldTheCascadeOff()
    {
        string src = File.ReadAllText(Shader);
        var m = Regex.Match(src, @"#endif\s*\n#if defined\(VE_TORCH_FLOOD\) && !defined\(VE_DIET2\)\s*\n(//[^\n]*\n)+#define _UseLODCascade 0\s*\n#endif");
        Assert.IsTrue(m.Success, "the flood twins fold _UseLODCascade to 0 (a macro after diet 2's folded / uniform pair)");
        int pair = src.IndexOf("#ifdef VE_DIET2\nstatic const int _UseLODCascade = 0;");
        Assert.IsTrue(pair >= 0 && pair < m.Index, "the macro comes after the uniform's declaration");
    }

    [Test]
    public void Shader_SkyHistFetch_NoReturnInsideTheLoop_OutsideDiet2()
    {
        string src = File.ReadAllText(Shader);
        var f = Regex.Match(src, @"uint2 SkyHistFetch\(.*?\n\}", RegexOptions.Singleline);
        Assert.IsTrue(f.Success);
        string body = f.Value;
        int ifd = body.IndexOf("#ifdef VE_DIET2"), els = body.IndexOf("#else"), end = body.IndexOf("#endif");
        Assert.IsTrue(ifd >= 0 && els > ifd && end > els, "diet 2 keeps the old loop, the others get the new one");
        string diet = body.Substring(ifd, els - ifd), other = body.Substring(els, end - els);
        StringAssert.Contains("if (hv.y == key) return hv;", diet);
        StringAssert.DoesNotContain("return hv", other);
        StringAssert.Contains("for (int q = 3; q >= 0; q--)", other);
        StringAssert.Contains("if (hv.y == key) found = hv;", other);
    }

    // The two loops as the shader has them: inBounds[q], keys[q] (the probe's stored key), vals[q].
    static (uint, uint) OldFetch(bool[] inB, uint[] keys, uint[] vals, uint key)
    {
        for (int q = 0; q < 4; q++) if (inB[q] && keys[q] == key) return (vals[q], keys[q]);
        return (0u, 0xFFFFFFFFu);
    }
    static (uint, uint) NewFetch(bool[] inB, uint[] keys, uint[] vals, uint key)
    {
        var found = (0u, 0xFFFFFFFFu);
        for (int q = 3; q >= 0; q--) if (inB[q] && keys[q] == key) found = (vals[q], keys[q]);
        return found;
    }

    [Test]
    public void SkyHistFetch_NewLoop_EqualsOldLoop_OnEveryPattern()
    {
        // Every in-bounds mask (16) x every key pattern over 3 symbols incl. 0xFFFFFFFF, the "none" key (81) x 3 keys.
        uint[] sym = { 7u, 9u, 0xFFFFFFFFu };
        int n = 0;
        for (int m = 0; m < 16; m++)
        for (int kp = 0; kp < 81; kp++)
        foreach (uint key in sym)
        {
            var inB = new bool[4]; var keys = new uint[4]; var vals = new uint[4];
            int t = kp;
            for (int q = 0; q < 4; q++) { inB[q] = ((m >> q) & 1) != 0; keys[q] = sym[t % 3]; t /= 3; vals[q] = 100u + (uint)q; }
            Assert.AreEqual(OldFetch(inB, keys, vals, key), NewFetch(inB, keys, vals, key), $"mask {m} keys {kp} key {key}");
            n++;
        }
        Assert.AreEqual(16 * 81 * 3, n);
    }

    [Test]
    public void Feature_BindsTheAlias_ChecksKernels_PaintsFailedFrames()
    {
        string src = File.ReadAllText(Feature);
        StringAssert.Contains("cmd.SetComputeBufferParam(data.compute, data.kPrimary, \"HeightMipRO\", data.heightMip);", src);
        StringAssert.Contains("KernelHealth.Failed(activeCompute, passData.kPrimary)", src);
        StringAssert.Contains("KernelHealth.Failed(activeCompute, passData.kShellPrepass)", src);
        StringAssert.Contains("KernelHealth.Failed(activeCompute, passData.kSunDefer)", src);
        StringAssert.Contains("if (data.kernelFailed) context.cmd.ClearRenderTarget(false, true, Color.magenta);", src);
        StringAssert.Contains("if (data.debugNaN) cmd.SetBufferData(data.debugBuffer, s_debugNaN, 0, 0, 3);", src);
        StringAssert.Contains("KernelHealth.Report(cs, k, r.GetData<float>()[0])", src);
        // The flood twins fold the cascade off: they may only be picked while it is off.
        StringAssert.Contains("TorchFloodPool.Ready && !useStripped && !useMemoryProbe && !cascadeThisFrame", src);
    }
}
