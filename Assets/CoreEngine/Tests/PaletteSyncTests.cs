// Assets/CoreEngine/Tests/PaletteSyncTests.cs
//
// Guards the ONE duplication this presentation pass introduced: the authored
// material palette exists in C# (MaterialPalette, the authoritative table) and
// is mirrored in HLSL (Raymarch.compute's MaterialAlbedo), because wiring a new
// registry buffer through RaymarchFeature's render-graph pass would have meant
// touching render setup for a colour change.
//
// A silent mismatch between the CPU palette (debug overlays, tools) and the GPU
// palette (what you actually look at) is exactly the kind of drift nobody
// notices for a month, so it is a test rather than a comment asking nicely.
//
// ---------------------------------------------------------------------------
// EXTENDED BY PHASE 7 -- ADDED TO, NEVER LOOSENED. What changed and why the
// pin is still meaningful:
//
//   1. Materials.Torch (id 14) joins the authored list. It is a new material
//      with an authored colour, so leaving it out would have quietly shrunk
//      what this file guards while the file still looked green.
//
//   2. A SECOND mirrored table now exists and is pinned the same way.
//      Raymarch.compute gained MaterialEmissive() and MaterialEmissiveTint()
//      because Stage 0 found there is NO GraphicsBuffer<MaterialData> anywhere
//      in this project -- A.7 is declared in StructHeaders and read by nothing
//      -- so Phase 7 mirrored the two numbers it needs exactly the way the
//      palette beside them is already mirrored, for MaterialPalette's own
//      stated reason. A mirror without a sync test is the drift this file
//      exists to stop, so the mirror arrives with its test in the same commit.
//      The pin goes through MaterialRules.TryGetLight -- the PUBLIC API the
//      rest of the engine uses -- rather than through the private tables, so
//      it checks the value a torch actually emits, not an internal.
//
//   3. LavaIsTheOnlySelfLitMaterial is UNCHANGED and still passes. A torch is
//      deliberately NOT self-lit: the self-lit path is a pre-Phase-7 shading
//      cheat that skips AO and the hemisphere term, and a torch takes the real
//      C.9 emissive path instead. Taking both would double-count it. That this
//      assertion still holds after the combine was rewritten is evidence the
//      rewrite did not quietly reclassify anything.
// ---------------------------------------------------------------------------
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

public class PaletteSyncTests
{
    private const string ShaderPath = "Assets/CoreEngine/Rendering/Raymarch.compute";

    private static Dictionary<byte, float[]> ParseShaderPalette(string fn = "MaterialAlbedo")
    {
        string src = File.ReadAllText(ShaderPath);
        int start = src.IndexOf("float3 " + fn + "(uint mat)");
        Assert.Greater(start, 0, fn + " not found in " + ShaderPath);
        int end = src.IndexOf('}', start);
        string body = src.Substring(start, end - start);

        var rx = new Regex(@"mat\s*==\s*(\d+)u\)\s*return\s*float3\(\s*([\d.]+)\s*,\s*([\d.]+)\s*,\s*([\d.]+)\s*\)");
        var map = new Dictionary<byte, float[]>();
        foreach (Match m in rx.Matches(body))
        {
            byte id = byte.Parse(m.Groups[1].Value);
            map[id] = new[]
            {
                float.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
                float.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture),
                float.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture),
            };
        }
        return map;
    }

    /// LightingConfig.look = 1's palette: MaterialPalette.OfLook against the
    /// shader's MaterialAlbedoLook, by the same parse. Also pins that the two
    /// tables are genuinely different, so a copy-paste of one over the other
    /// cannot pass silently.
    [Test]
    public void LookPalette_MatchesShader_AndDiffersFromThePhase7Palette()
    {
        var shader = ParseShaderPalette("MaterialAlbedoLook");
        byte[] authored =
        {
            Materials.Stone, Materials.Grass, Materials.Sand, Materials.MossyStone,
            Materials.Water, Materials.Snow, Materials.Sandstone, Materials.JungleGrass,
            Materials.Deepstone, Materials.Lava, Materials.Honey, Materials.Obsidian,
            Materials.Torch, Materials.TorchBlue, Materials.TorchGreen, Materials.TorchPurple,
        };
        foreach (byte id in authored)
        {
            Assert.IsTrue(shader.ContainsKey(id), $"material {id} missing from MaterialAlbedoLook");
            var cpu = MaterialPalette.OfLook(id);
            var missing = MaterialPalette.Missing;
            Assert.IsFalse(cpu.R == missing.R && cpu.G == missing.G && cpu.B == missing.B,
                $"material {id} has no look-1 colour");
            Assert.AreEqual(cpu.R, shader[id][0], 0.0005f, $"material {id} red differs (CPU vs shader, look 1)");
            Assert.AreEqual(cpu.G, shader[id][1], 0.0005f, $"material {id} green differs (CPU vs shader, look 1)");
            Assert.AreEqual(cpu.B, shader[id][2], 0.0005f, $"material {id} blue differs (CPU vs shader, look 1)");
        }
        var g0 = MaterialPalette.Of(Materials.Grass);
        var g1 = MaterialPalette.OfLook(Materials.Grass);
        Assert.Greater(System.Math.Abs(g0.G - g1.G), 0.05f, "look-1 grass should not equal the Phase 7 grass");
    }

    [Test]
    public void ShaderPalette_MatchesMaterialPalette()
    {
        var shader = ParseShaderPalette();
        byte[] authored =
        {
            Materials.Stone, Materials.Grass, Materials.Sand, Materials.MossyStone,
            Materials.Water, Materials.Snow, Materials.Sandstone, Materials.JungleGrass,
            Materials.Deepstone, Materials.Lava, Materials.Honey, Materials.Obsidian,
            Materials.Torch,   // Phase 7
            Materials.TorchBlue, Materials.TorchGreen, Materials.TorchPurple,   // Stage G
        };

        foreach (byte id in authored)
        {
            Assert.IsTrue(shader.ContainsKey(id),
                $"material {id} is authored in MaterialPalette but missing from the shader's MaterialAlbedo");
            var cpu = MaterialPalette.Of(id);
            float[] gpu = shader[id];
            Assert.AreEqual(cpu.R, gpu[0], 0.0005f, $"material {id} red channel differs (CPU vs shader)");
            Assert.AreEqual(cpu.G, gpu[1], 0.0005f, $"material {id} green channel differs (CPU vs shader)");
            Assert.AreEqual(cpu.B, gpu[2], 0.0005f, $"material {id} blue channel differs (CPU vs shader)");
        }
    }

    [Test]
    public void EveryGeneratedMaterial_HasAnAuthoredColour()
    {
        // A material generation can actually place must never fall through to
        // the "unauthored" magenta.
        byte[] generated =
        {
            Materials.Stone, Materials.Grass, Materials.Sand, Materials.MossyStone,
            Materials.Water, Materials.Snow, Materials.Sandstone, Materials.JungleGrass,
            Materials.Deepstone,
        };
        var missing = MaterialPalette.Missing;
        foreach (byte id in generated)
        {
            var c = MaterialPalette.Of(id);
            Assert.IsFalse(c.R == missing.R && c.G == missing.G && c.B == missing.B,
                $"material {id} is generated by world-gen but has no authored colour");
        }
    }

    // =====================================================================
    // PHASE 7 -- the emissive mirror (§6.6, A.7's `emissive` field)
    // =====================================================================

    private static Dictionary<byte, float> ParseShaderEmissive()
    {
        string src = File.ReadAllText(ShaderPath);
        int start = src.IndexOf("float MaterialEmissive(uint mat)");
        Assert.Greater(start, 0, "MaterialEmissive not found in " + ShaderPath);
        int end = src.IndexOf('}', start);
        string body = src.Substring(start, end - start);

        var rx = new Regex(@"mat\s*==\s*(\d+)u\)\s*return\s*([\d.]+)");
        var map = new Dictionary<byte, float>();
        foreach (Match m in rx.Matches(body))
            map[byte.Parse(m.Groups[1].Value)] =
                float.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
        return map;
    }

    private static Dictionary<byte, float[]> ParseShaderEmissiveTint()
    {
        string src = File.ReadAllText(ShaderPath);
        int start = src.IndexOf("float3 MaterialEmissiveTint(uint mat)");
        Assert.Greater(start, 0, "MaterialEmissiveTint not found in " + ShaderPath);
        int end = src.IndexOf('}', start);
        string body = src.Substring(start, end - start);

        var rx = new Regex(@"mat\s*==\s*(\d+)u\)\s*return\s*float3\(\s*([\d.]+)\s*,\s*([\d.]+)\s*,\s*([\d.]+)\s*\)");
        var map = new Dictionary<byte, float[]>();
        foreach (Match m in rx.Matches(body))
            map[byte.Parse(m.Groups[1].Value)] = new[]
            {
                float.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
                float.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture),
                float.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture),
            };
        return map;
    }

    [Test]
    public void ShaderEmissiveStrength_MatchesMaterialRules()
    {
        var shader = ParseShaderEmissive();
        foreach (byte id in new[] { Materials.Torch, Materials.Lava,
                                   Materials.TorchBlue, Materials.TorchGreen, Materials.TorchPurple })
        {
            Assert.IsTrue(shader.ContainsKey(id),
                $"material {id} has an emissive strength in MaterialRules but none in the shader");
            Assert.AreEqual(MaterialRules.Emissive(id), shader[id], 0.0005f,
                $"material {id}: emissive strength differs between MaterialRules and the shader");
        }

        // And nothing emits in the shader that does not emit on the CPU. A
        // shader-only emissive would glow with no light source behind it -- the
        // §3.8 buffer would never carry it, so it would light itself and
        // nothing else, which is the confusing half of a mismatch.
        foreach (var kv in shader)
            Assert.Greater(MaterialRules.Emissive(kv.Key), 0f,
                $"the shader gives material {kv.Key} an emissive strength but MaterialRules does not");
    }

    /// Pinned through TryGetLight -- the PUBLIC API §3.8's buffer is built from
    /// -- rather than through the tint table directly, so what is checked is
    /// the colour a torch actually emits into the world, not an internal that
    /// happens to feed it.
    [Test]
    public void ShaderEmissiveTint_MatchesTheLightAMaterialEmits()
    {
        var tint = ParseShaderEmissiveTint();
        var strength = ParseShaderEmissive();

        foreach (byte id in new[] { Materials.Torch, Materials.Lava,
                                   Materials.TorchBlue, Materials.TorchGreen, Materials.TorchPurple })
        {
            Assert.IsTrue(tint.ContainsKey(id), $"material {id} has no emissive tint in the shader");
            Assert.IsTrue(MaterialRules.TryGetLight(id, out float r, out float g, out float b, out _),
                $"material {id} is emissive in the shader but TryGetLight refuses it");

            // The shader's emitted colour is tint * strength; TryGetLight
            // returns exactly that product.
            Assert.AreEqual(r, tint[id][0] * strength[id], 0.0005f, $"material {id} red differs");
            Assert.AreEqual(g, tint[id][1] * strength[id], 0.0005f, $"material {id} green differs");
            Assert.AreEqual(b, tint[id][2] * strength[id], 0.0005f, $"material {id} blue differs");
        }
    }

    [Test]
    public void LavaIsTheOnlySelfLitMaterial()
    {
        // The self-lit path skips AO and the hemisphere term. Anything else
        // acquiring that flag would silently stop receiving shading.
        for (int i = 0; i < 256; i++)
            Assert.AreEqual(i == Materials.Lava, MaterialPalette.IsSelfLit((byte)i),
                $"material {i} self-lit flag unexpected");
    }
}
