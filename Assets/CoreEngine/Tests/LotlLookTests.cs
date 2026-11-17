// Assets/CoreEngine/Tests/LotlLookTests.cs
//
// Task 226 (owner Q-AN 2026-11-16: "cheap look polish ... closer to Lay of the Land"): four look pieces, each its own
// switch (RaymarchFeature.VoxelVary `-voxvary`, Crevice `-crevice`, AerialHaze `-aerial`, Bloom `-bloom`). The look
// itself is judged on stills (cost split arms lotl_off / X_only / X_off); this pins the flags, that each piece is gated
// so OFF is the pre-226 code path, bloom's place in the frame (after the last writer, before FSR 3), and the per-voxel
// variation's arithmetic on a CPU mirror (stable, mean-neutral, bounded, gone where it would alias).
using System.IO;
using NUnit.Framework;
using UnityEngine;

public class LotlLookTests
{
    const string Shader = "Assets/CoreEngine/Rendering/Raymarch.compute";
    const string Feature = "Assets/CoreEngine/Rendering/RaymarchFeature.cs";

    [Test]
    public void Flags_DefaultOn_ReachTheShader()
    {
        string cs = File.ReadAllText(Feature);
        StringAssert.Contains("VoxelVary = ReadIntArg(\"-voxvary\", 1) != 0;", cs);
        StringAssert.Contains("Crevice = ReadIntArg(\"-crevice\", 1) != 0;", cs);
        StringAssert.Contains("AerialHaze = ReadIntArg(\"-aerial\", 1) != 0;", cs);
        StringAssert.Contains("Bloom = ReadIntArg(\"-bloom\", 1) != 0;", cs);
        StringAssert.Contains("(VoxelVary ? 1 : 0) | (Crevice ? 2 : 0) | (AerialHaze ? 4 : 0)", cs);
        StringAssert.Contains("\"_LotlLook\", data.lotlLook)", cs, "the bits reach the shader");
        StringAssert.Contains("\"_LotlParams\", data.lotlParams)", cs);
        StringAssert.Contains("(AerialHaze ? AerialHazeRangeM : SkyHazeRangeM)", cs, "aerial OFF = task 121's range");
    }

    [Test]
    public void Shader_EveryPieceIsGated_OffIsTheOldPath()
    {
        string s = File.ReadAllText(Shader);
        StringAssert.Contains("if ((_LotlLook & 1) != 0 && !selfLit && MaterialEmissive(lm) <= 0.0 && !MaterialIsClearFluid(RiverLipAsWater(lm)))", s);
        StringAssert.Contains("if ((_LotlLook & 2) != 0)   // task 226 bit 1", s);
        StringAssert.Contains("((_LotlLook & 4) != 0 ? _LotlParams.w : 0.5) * h)", s, "aerial OFF = task 121's 0.5");
        // Crevice rides FaceAO's value (no new occupancy read) and fades with it (aoFade) where VAO fades.
        StringAssert.Contains("direct *= lerp(1.0, lerp(vao, 1.0, aoFade), _LotlParams.z);", s);
        // The variation is per DRAWN cell, as LookAlbedo's colour is.
        StringAssert.Contains("baseColor *= LotlVoxelVary(lm, _CellHash != 0 ? (voxel >> cellShift) << cellShift : voxel,", s);
    }

    [Test]
    public void Bloom_KernelsExist_RunAfterTheLastWriter_BeforeFsr3()
    {
        string s = File.ReadAllText(Shader);
        foreach (var k in new[] { "BloomDown", "BloomBlur", "BloomAdd" })
        {
            StringAssert.Contains("#pragma kernel " + k + "\n", s);
            CollectionAssert.Contains(VoxelEngine.Rendering.KernelHealth.RaymarchKernels, k);
        }
        string cs = File.ReadAllText(Feature);
        int taa = cs.IndexOf("cmd.EndSample(\"VE.Raymarch.Taa\");");
        int bloom = cs.IndexOf("cmd.BeginSample(\"VE.Raymarch.Bloom\");");
        int fsr = cs.IndexOf("blitSource = UpsRecordFsr3(");
        Assert.Greater(taa, 0); Assert.Greater(bloom, taa, "bloom after TAA (and the march / sun-defer before it)");
        Assert.Greater(fsr, bloom, "bloom before FSR 3's pass: no pass at output resolution");
        StringAssert.Contains("bd.width = Mathf.Max(1, (dispatchW + 3) >> 2);", cs, "quarter of the render rows");
        StringAssert.Contains("if (bloomWant && passData.look == 1)", cs, "look 1 only");
    }

    // ---- CPU mirror of VoxelHash / LotlVoxelVary ----
    static float VoxelHash(int x, int y, int z)
    {
        unchecked
        {
            uint ux = (uint)(x + (1 << 20)), uy = (uint)(y + (1 << 20)), uz = (uint)(z + (1 << 20));
            uint h = ux * 73856093u ^ uy * 19349663u ^ uz * 83492791u;
            h ^= h >> 13; h *= 1274126177u; h ^= h >> 16;
            return (h & 0xFFFFFFu) / (float)0xFFFFFF;
        }
    }
    static Vector3 Vary(float luma, int x, int y, int z, float fade)
    {
        float a = RaymarchFeature.VaryAmplitude, hue = RaymarchFeature.VaryHue;
        float amp = Mathf.Lerp(a, a * 0.5f, Mathf.Clamp01(luma)) * fade;
        float b = VoxelHash(z + 7919, x + 104729, y + 1299709) * 2f - 1f;
        float t = VoxelHash(y + 15485863, z + 32452843, x + 49979687) * 2f - 1f;
        float v = 1f + amp * b, ht = hue * fade * t;
        return new Vector3(v * (1f + ht), v, v * (1f - ht));
    }

    [Test]
    public void Vary_MeanNeutral_Bounded_StableAndGoneWhenFaded()
    {
        double sumG = 0, sumR = 0; float lo = 9f, hi = -9f; int n = 0;
        for (int x = -40; x < 40; x++)
        for (int y = -5; y < 5; y++)
        for (int z = -40; z < 40; z++)
        {
            Vector3 f = Vary(0.3f, x * 7 + 3000, y + 600, z * 5 + 2000, 1f);
            sumG += f.y; sumR += f.x; n++;
            lo = Mathf.Min(lo, f.y); hi = Mathf.Max(hi, f.y);
            Assert.AreEqual(f, Vary(0.3f, x * 7 + 3000, y + 600, z * 5 + 2000, 1f), "same voxel, same colour");
        }
        Assert.AreEqual(1.0, sumG / n, 0.01, "no brightness drift: the mean stays the material's colour");
        Assert.AreEqual(1.0, sumR / n, 0.01, "no hue drift");
        Assert.LessOrEqual(hi, 1f + RaymarchFeature.VaryAmplitude + 1e-4f);
        Assert.GreaterOrEqual(lo, 1f - RaymarchFeature.VaryAmplitude - 1e-4f);
        Assert.Greater(hi - lo, RaymarchFeature.VaryAmplitude, "the swing is actually there");
        Assert.AreEqual(Vector3.one, Vary(0.3f, 11, 22, 33, 0f), "faded (voxel < 2 px): exactly the old colour");
        // A light material swings less (it would clip): white's amplitude is half the darkest's.
        float dark = Mathf.Abs(Vary(0f, 5, 6, 7, 1f).y - 1f), white = Mathf.Abs(Vary(1f, 5, 6, 7, 1f).y - 1f);
        Assert.AreEqual(dark * 0.5f, white, 1e-5f);
    }

    [Test]
    public void Arms_LotlOffAndOnlyAndOff_AreDocumentedInTheCostSplit()
    {
        string c = File.ReadAllText("Assets/Game/PlaygroundCapture.CostSplit.cs");
        StringAssert.Contains("if (arm == \"lotl_off\" || arm == piece + \"_off\") return false;", c);
        StringAssert.Contains("RaymarchFeature.Bloom = CostLotlPiece(arm, \"bloom\", _costBaseBloom);", c);
    }
}
