// Assets/TerrainGen/Tests/MatVariantTests.cs
//
// Task 188a (owner 2026-11-12, answer (a): "just randomly spraying it is weird
// ... like blotches"): `-matvariant 0` (TerrainSpec.matVariantOff = 1) drops the
// material rules' random VARIANT spray (GroundRules.Column: VariantNoise over
// ~20-25 % of every slot); every column takes its slot's MAIN palette entry.
// Pins:
//   - matVariantOff = 0 (the default, `-matvariant 1`) is today BYTE FOR BYTE:
//     an FNV over height, slot, cover, soil / subsoil depths, the top material
//     and the top 4.8 m of Material, pinned from the pre-188a code (seeds 42, 7);
//   - matVariantOff = 1 has ZERO variant columns, the same rule slot, the same
//     ground, the same Cover byte (trees keep reading the noise patch), and a
//     skin that is the slot's main entry;
//   - near == far on the shape slots at texel <= 22 with the spray off too;
//   - the per-biome share of ground per material, before -> after, 4 seeds, and
//     the materials that leave a biome's surface (Logs/TerrainGen/188a/share188a.txt).
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace TerrainGen.Tests
{
    public class MatVariantTests
    {
        private static readonly string[] BIOME = { "forest", "desert", "snow", "jungle", "savanna", "badlands" };
        private const string OUT_DIR = "Logs/TerrainGen/188a";

        // FNV of PinHash on the pre-188a code (Logs/overnight/188a/pin_before.out).
        private const uint PIN_42 = 0x6f6c8910u, PIN_7 = 0xeaaad676u;

        private static List<int2> LandPoints(in TerrainSpec spec, WorldMap map, int count, uint rngSeed)
        {
            var rng = new Unity.Mathematics.Random(rngSeed);
            var pts = new List<int2>();
            int n = spec.mapCells, cv = spec.mapCellVoxels, guard = 0;
            while (pts.Count < count && guard++ < count * 100)
            {
                int cx = rng.NextInt(2, n - 2), cz = rng.NextInt(2, n - 2);
                int x = cx * cv + rng.NextInt(cv), z = cz * cv + rng.NextInt(cv);
                if (map.Height[cz * n + cx] <= 0f) continue;
                pts.Add(new int2(x, z));
            }
            return pts;
        }

        /// The exact routine that produced PIN_42 / PIN_7 before the change.
        private static uint PinHash(in TerrainSpec spec, WorldMap map)
        {
            var o = Offsets.For(in spec);
            var view = map.View;
            var rng = new Unity.Mathematics.Random(0x188a);
            int n = spec.mapCells, cv = spec.mapCellVoxels, cols = 0, guard = 0;
            uint h = 2166136261u;
            void Mix(int v) { h = (h ^ (uint)v) * 16777619u; }
            while (cols < 600 && guard++ < 60000)
            {
                int cx = rng.NextInt(2, n - 2), cz = rng.NextInt(2, n - 2);
                int x = cx * cv + rng.NextInt(cv), z = cz * cv + rng.NextInt(cv);
                if (map.Height[cz * n + cx] <= 0f) continue;
                var c = ShapeField.Sample(in spec, in view, in o, x + 0.5f, z + 0.5f, 1);
                float st = ShapeField.MaxStep(in spec, in view, in o, x + 0.5f, z + 0.5f, 1, c.height);
                var cd = DetailField.PrepareColumn(in spec, in o, x, z);
                Mix(c.height); Mix(c.mrSlot); Mix(c.cover); Mix(c.mrSoil); Mix(c.mrSub); Mix(c.surfBiome);
                Mix(DetailField.TopMaterial(in spec, in c, st, in o, x, z));
                for (int d = 0; d < 48; d++) Mix(DetailField.Material(in spec, in o, in c, in cd, st, x, c.height - d, z));
                cols++;
            }
            return h;
        }

        // ------------------------------------------------------------------
        [Test]
        public void Variant1_IsTodayByteForByte()
        {
            var s42 = TerrainSpec.SizeClass2(42);
            var s7 = TerrainSpec.SizeClass2(7);
            Assert.AreEqual(0, s42.matVariantOff, "the default keeps the spray (owner picks the default later)");
            Assert.IsTrue(s42.MatVariant);
            uint h42 = PinHash(in s42, SharedMaps.Get(s42)), h7 = PinHash(in s7, SharedMaps.Get(s7));
            Debug.Log($"[188a] variant 1: seed 42 fnv 0x{h42:x8} (pin 0x{PIN_42:x8}), seed 7 fnv 0x{h7:x8} (pin 0x{PIN_7:x8})");
            Assert.AreEqual(PIN_42, h42, "seed 42: -matvariant 1 == pre-188a");
            Assert.AreEqual(PIN_7, h7, "seed 7: -matvariant 1 == pre-188a");
            // and the spray off must actually change something
            var off = s42; off.matVariantOff = 1;
            Assert.AreNotEqual(PIN_42, PinHash(in off, SharedMaps.Get(s42)), "-matvariant 0 changes the materials");
        }

        // ------------------------------------------------------------------
        [Test]
        public void Variant0_NoVariantColumns_SameSlotsGroundAndCover()
        {
            int variantOn = 0, total = 0;
            foreach (uint seed in new uint[] { 42, 7 })
            {
                var on = TerrainSpec.SizeClass2(seed);
                var map = SharedMaps.Get(on);
                var off = on; off.matVariantOff = 1;
                var o = Offsets.For(in on);
                var view = map.View;
                foreach (var p in LandPoints(in on, map, 1500, 0x188a0 + seed))
                {
                    var a = ShapeField.Sample(in on, in view, in o, p.x + 0.5f, p.y + 0.5f, 1);
                    var b = ShapeField.Sample(in off, in view, in o, p.x + 0.5f, p.y + 0.5f, 1);
                    total++;
                    if (GroundRules.IsVariant(a.mrSlot)) variantOn++;
                    Assert.IsFalse(GroundRules.IsVariant(b.mrSlot), $"no variant column at {p}");
                    Assert.AreEqual(a.mrSlot & ~GroundRules.VARIANT_BIT, b.mrSlot, "the rule slot (and fluid bed) is unchanged");
                    Assert.AreEqual(a.cover, b.cover, "trees read the same Cover byte");
                    Assert.AreEqual(a.height, b.height, "the ground never moves");
                    Assert.AreEqual(a.water, b.water);
                    Assert.AreEqual(a.biome, b.biome);
                    Assert.AreEqual(a.surfBiome, b.surfBiome);
                    int slot = GroundRules.Slot(b.mrSlot);
                    if ((b.mrSlot & GroundRules.FLUID_BED) == 0 && b.mrSub != 0)
                        Assert.AreEqual(Cover.Settle(GroundRules.Entry(b.surfBiome, slot, false), 0f),
                                        GroundRules.NearMaterial(in b, 0f, 0), "the skin is the slot's MAIN entry");
                    if (!GroundRules.IsVariant(a.mrSlot))
                    {
                        Assert.AreEqual(a.mrSoil, b.mrSoil, "a column the spray never touched is unchanged");
                        Assert.AreEqual(a.mrSub, b.mrSub);
                    }
                }
            }
            double share = 100.0 * variantOn / math.max(1, total);
            Debug.Log($"[188a] variant columns: -matvariant 1 {share:F1} % of {total} land columns; -matvariant 0 0");
            Assert.Greater(share, 5.0, "the spray exists in the default");
        }

        // ------------------------------------------------------------------
        [Test]
        public void Variant0_NearFar_ShapeSlotsAgree_UpToTexel22()
        {
            var spec = TerrainSpec.SizeClass2(7); spec.matVariantOff = 1;
            var map = SharedMaps.Get(TerrainSpec.SizeClass2(7));
            var o = Offsets.For(in spec);
            var view = map.View;
            int asked = 0;
            foreach (var p in LandPoints(in spec, map, 800, 0x0ff))
            {
                var near = ShapeField.Sample(in spec, in view, in o, p.x + 0.5f, p.y + 0.5f, 1);
                int sn = GroundRules.Slot(near.mrSlot);
                foreach (int t in new[] { 4, 16 })
                {
                    var far = ShapeField.Sample(in spec, in view, in o, p.x + 0.5f, p.y + 0.5f, t);
                    int sf = GroundRules.Slot(far.mrSlot);
                    if (sn == GroundRules.Shore || sf == GroundRules.Shore || sn == GroundRules.Hollow || sf == GroundRules.Hollow
                        || sn == GroundRules.High || sf == GroundRules.High) continue;   // as MaterialRuleTests: own-height slots
                    asked++;
                    Assert.AreEqual(near.mrSlot, far.mrSlot, $"slot differs at texel {t} at {p}");
                    Assert.AreEqual(near.surfBiome, far.surfBiome);
                }
            }
            Assert.Greater(asked, 400);
        }

        // ------------------------------------------------------------------
        private static Dictionary<byte, string> MatNames()
        {
            var d = new Dictionary<byte, string>();
            foreach (var f in typeof(GenMat).GetFields(BindingFlags.Public | BindingFlags.Static))
                if (f.IsLiteral && f.FieldType == typeof(byte)) { byte v = (byte)f.GetRawConstantValue(); if (!d.ContainsKey(v)) d[v] = f.Name; }
            return d;
        }

        /// Report + one assertion: the share of ground per material per skin biome,
        /// before (-matvariant 1) -> after (-matvariant 0), 4 seeds; and the palette's
        /// variant-only materials (they leave the biome's SURFACE with the spray off).
        [Test, Category("Slow")]
        public void FourSeeds_ShareOfGroundPerMaterial_BeforeAfter()
        {
            var names = MatNames();
            string N(byte m) => m == GroundRules.BARE ? "bare" : names.TryGetValue(m, out var s) ? s : $"#{m}";
            var count = new Dictionary<byte, int>[2, GenBiome.Count];
            var cols = new int[2, GenBiome.Count];
            for (int a = 0; a < 2; a++) for (int b = 0; b < GenBiome.Count; b++) count[a, b] = new Dictionary<byte, int>();
            foreach (uint seed in new uint[] { 42, 7, 2026, 31337 })
            {
                var on = TerrainSpec.SizeClass2(seed);
                var map = SharedMaps.Get(on);
                var view = map.View;
                var o = Offsets.For(in on);
                foreach (var p in LandPoints(in on, map, 3000, 0x5a4e + seed))
                    for (int arm = 0; arm < 2; arm++)
                    {
                        var spec = on; spec.matVariantOff = (byte)arm;
                        var c = ShapeField.Sample(in spec, in view, in o, p.x + 0.5f, p.y + 0.5f, 1);
                        if (c.height < spec.seaLevelY || c.water > c.height) continue;   // water tops: not ground
                        float st = ShapeField.MaxStep(in spec, in view, in o, p.x + 0.5f, p.y + 0.5f, 1, c.height);
                        byte m = DetailField.TopMaterial(in spec, in c, st, in o, p.x, p.y);
                        int bi = c.surfBiome < GenBiome.Count ? c.surfBiome : 0;
                        count[arm, bi].TryGetValue(m, out int k); count[arm, bi][m] = k + 1;
                        cols[arm, bi]++;
                    }
            }
            var sb = new StringBuilder();
            sb.AppendLine("Task 188a: share of the SURFACE (DetailField.TopMaterial, land above the sea, no fluid on top) per material per skin biome,");
            sb.AppendLine("seeds 42 / 7 / 2026 / 31337, 3,000 random land columns each. before = -matvariant 1 (today), after = -matvariant 0 (no spray).");
            sb.AppendLine("Stone names are the bare rock showing (steep / crest / sunny faces); stamp, lip and pocket materials are included as they are.");
            for (int b = 0; b < GenBiome.Count; b++)
            {
                sb.AppendLine();
                sb.AppendLine($"{BIOME[b]}: {cols[0, b]} columns");
                var mats = count[0, b].Keys.Union(count[1, b].Keys)
                    .OrderByDescending(m => math.max(count[0, b].GetValueOrDefault(m), count[1, b].GetValueOrDefault(m))).ToList();
                foreach (var m in mats)
                {
                    double x0 = 100.0 * count[0, b].GetValueOrDefault(m) / math.max(1, cols[0, b]);
                    double x1 = 100.0 * count[1, b].GetValueOrDefault(m) / math.max(1, cols[1, b]);
                    if (x0 < 0.1 && x1 < 0.1) continue;
                    sb.AppendLine($"  {N(m),-18} {x0,5:F1} % -> {x1,5:F1} %{(x1 < 0.05 && x0 >= 0.1 ? "   (GONE from the surface)" : "")}");
                }
            }
            // The palette's variant-only materials, per biome: on no MAIN skin entry.
            sb.AppendLine();
            sb.AppendLine("Palette variant entries that are on NO main skin entry of their biome (they leave the surface with the spray off):");
            int[] skins = { GroundRules.Flat, GroundRules.Steep, GroundRules.Hollow, GroundRules.High, GroundRules.Shore, GroundRules.Scree };
            for (byte b = 0; b < GenBiome.Count; b++)
            {
                var main = new HashSet<byte>(skins.Select(s => GroundRules.Entry(b, s, false)));
                var under = new Dictionary<byte, string> {
                    [GroundRules.Soil(b)] = "soil", [GroundRules.Subsoil(b)] = "subsoil", [GroundRules.Stone(b)] = "stone" };
                foreach (int s in skins)
                {
                    byte v = GroundRules.Entry(b, s, true);
                    if (v == GroundRules.BARE || main.Contains(v)) continue;
                    string where = under.TryGetValue(v, out var w) ? $"still underground as the {w}" : "nowhere else in this biome";
                    sb.AppendLine($"  {BIOME[b],-9} {GroundRules.SlotName(s),-7} variant {N(v),-14} -> {where}");
                }
                // a BARE variant: that slot's patches of bare rock go (the rules' own bare slot stays)
                foreach (int s in skins)
                    if (GroundRules.Entry(b, s, true) == GroundRules.BARE && GroundRules.Entry(b, s, false) != GroundRules.BARE)
                        sb.AppendLine($"  {BIOME[b],-9} {GroundRules.SlotName(s),-7} variant bare           -> bare-rock patches on this slot go (the bare / sunny / crest rule stays)");
            }
            Directory.CreateDirectory(OUT_DIR);
            File.WriteAllText(Path.Combine(OUT_DIR, "share188a.txt"), sb.ToString());
            Debug.Log("[188a]\n" + sb);
            for (int b = 0; b < GenBiome.Count; b++) Assert.AreEqual(cols[0, b], cols[1, b], "the spray never moves land or water");
        }
    }
}
