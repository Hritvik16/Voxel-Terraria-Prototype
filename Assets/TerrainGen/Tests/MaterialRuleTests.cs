// Assets/TerrainGen/Tests/MaterialRuleTests.cs
//
// Task 180b: the simple material rules (GroundRules.cs, `-matrules 1`). Pins:
//   - the palette is data (every entry BARE or a solid GenMat; every biome has
//     its own soil, subsoil, stone, deep stone, cavern rock; every non-water
//     fluid owns a shore material);
//   - the CONFLICT PAIRS of the header's priority list, on synthetic inputs
//     (steep + wet, high + shore, wet + high, hollow + crest, scree + route,
//     sun / shade on the steep edge and the snow line, bed over everything);
//   - OFF touches nothing (no rule field set, same ground), ON never moves the
//     ground and keeps the Cover slot trees read;
//   - near == far on the shape slots at texel <= 22 (the band limit);
//   - depth bands in order down every column: a dug pit shows the ORIGINAL
//     surface's skin, soil, subsoil, stone (Material reads the
//     generated column only; the same voxel asked twice, or through a baked
//     or scanned shore field, is the same byte);
//   - task 182: a tall face is ONE stone: no deep stone above the absolute
//     stone / deep-stone boundary, whatever the depth below the column's top
//     (the tallest sheer columns of 4 seeds); deep stone under it;
//   - a sealed pocket's wall is lined with its fluid's own shore material;
//   - 4 seeds + adversarial cases (river across a biome border, oasis, tall
//     cliff, canyon floor, lake outlet), and the per-biome table the owner
//     reads the rules from (Screenshots/180b/table180b.txt).
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace TerrainGen.Tests
{
    public class MaterialRuleTests
    {
        private static readonly uint[] SEEDS = { 42, 7, 2026, 31337 };
        private static readonly string[] BIOME = { "forest", "desert", "snow", "jungle", "savanna", "badlands" };
        private const string OUT_DIR = "Logs/TerrainGen/180b";
        private const string SHOT_DIR = "Screenshots/180b";

        private struct World
        {
            public TerrainSpec off, on;
            public Offsets o;
            public WorldMap map;
        }

        private static World Get(uint seed)
        {
            var w = new World { off = TerrainSpec.SizeClass2(seed) };
            w.map = SharedMaps.Get(w.off);   // the default spec's map: the rules never change it
            w.off.matRulesOff = 1;   // task 186: the rules are the default; OFF = -matrules 0
            w.o = Offsets.For(in w.off);
            w.on = w.off; w.on.matRulesOff = 0;
            return w;
        }

        private static ColumnSample S(in TerrainSpec spec, in World w, float x, float z, int texel = 1)
        {
            var view = w.map.View;
            return ShapeField.Sample(in spec, in view, in w.o, x + 0.5f, z + 0.5f, texel);
        }

        private static float Step(in TerrainSpec spec, in World w, int x, int z, int h)
        {
            var view = w.map.View;
            return ShapeField.MaxStep(in spec, in view, in w.o, x + 0.5f, z + 0.5f, 1, h);
        }

        private static List<int2> LandPoints(in World w, int count, uint rngSeed)
        {
            var rng = new Unity.Mathematics.Random(rngSeed);
            var pts = new List<int2>();
            int n = w.off.mapCells, cv = w.off.mapCellVoxels, guard = 0;
            while (pts.Count < count && guard++ < count * 50)
            {
                int cx = rng.NextInt(2, n - 2), cz = rng.NextInt(2, n - 2);
                if (w.map.Height[cz * n + cx] <= 0f) continue;
                pts.Add(new int2(cx * cv + rng.NextInt(cv), cz * cv + rng.NextInt(cv)));
            }
            return pts;
        }

        // ------------------------------------------------------------------
        [Test]
        public void Palette_IsData_EveryBiomePaintsEverything()
        {
            for (byte b = 0; b < GenBiome.Count; b++)
            {
                var r = GroundRules.Row(b);
                byte[] all = { r.flat, r.flatVar, r.steep, r.steepVar, r.hollow, r.hollowVar, r.high, r.highVar, r.shore, r.shoreVar,
                               r.rubble, r.rubbleVar, r.soil, r.subsoil, r.stone, r.deep, r.cavern };
                foreach (byte m in all) Assert.IsTrue(m == GroundRules.BARE || GenMat.IsSolid(m), $"{BIOME[b]}: {m} is neither BARE nor solid");
                byte[] body = { r.soil, r.subsoil, r.stone, r.deep, r.cavern, r.rubble, r.shore };
                foreach (byte m in body) Assert.AreNotEqual(GroundRules.BARE, m, $"{BIOME[b]}: a body material cannot be BARE");
                Assert.AreNotEqual(r.stone, r.deep, $"{BIOME[b]}: stone and deep stone differ (a pit shows both)");
            }
            foreach (byte f in new[] { GenMat.Lava, GenMat.Honey, GenMat.Oil, GenMat.Acid, GenMat.Sludge, GenMat.Quicksand })
                Assert.IsTrue(GenMat.IsSolid(GroundRules.FluidShore(f)), $"fluid {f} owns a solid shore");
            Assert.AreEqual(0, GroundRules.FluidShore(GenMat.Water), "water uses the biome's hollow entry");
            Assert.AreEqual(GroundRules.Row(GenBiome.Jungle).hollow, GroundRules.PocketLining(GenMat.Water, GenBiome.Jungle));
            Assert.AreEqual(GenMat.MagmaCrust, GroundRules.PocketLining(GenMat.Lava, GenBiome.Snow), "lava: its own crust in every biome");
        }

        // ------------------------------------------------------------------
        private static RuleInputs Base() => new RuleInputs { peak = 6000f, overSea = 500f, slopeDeg = 10f };

        [Test]
        public void ConflictPairs_FirstMatchWins()
        {
            Assert.AreEqual(GroundRules.Flat, GroundRules.ChooseSlot(Base()));
            // steep + wet: steep (mud does not stay on a 30 deg bank); on the sunny side bare
            var i = Base(); i.slopeDeg = 40f; i.wet = true;
            Assert.AreEqual(GroundRules.Steep, GroundRules.ChooseSlot(i), "steep + wet");
            i.sunny = 0.5f;
            Assert.AreEqual(GroundRules.Bare, GroundRules.ChooseSlot(i), "steep + wet, sunny side");
            // high + shore: shore
            i = Base(); i.shoreLow = true; i.overSea = 5000f;
            Assert.AreEqual(GroundRules.Shore, GroundRules.ChooseSlot(i), "high + shore");
            // wet + high: the bank
            i = Base(); i.wet = true; i.overSea = 5000f;
            Assert.AreEqual(GroundRules.Hollow, GroundRules.ChooseSlot(i), "wet + high");
            // bed beats steep and high
            i = Base(); i.underWater = true; i.slopeDeg = 60f; i.overSea = 5000f;
            Assert.AreEqual(GroundRules.Hollow, GroundRules.ChooseSlot(i), "bed + steep + high");
            // hollow + crest: a saddle on a ridge (concave, above the mean) collects soil
            i = Base(); i.dip = 6f; i.rel = 50f;
            Assert.AreEqual(GroundRules.Dip, GroundRules.ChooseSlot(i), "dip on a ridge");
            i.dip = -6f;
            Assert.AreEqual(GroundRules.Steep, GroundRules.ChooseSlot(i), "a gentle crest: thin skin");
            i.slopeDeg = 40f;
            Assert.AreEqual(GroundRules.Bare, GroundRules.ChooseSlot(i), "a steep crest: bare rock");
            // scree + route: the route
            i = Base(); i.upSlopeDeg = 55f;
            Assert.AreEqual(GroundRules.Scree, GroundRules.ChooseSlot(i), "scree under a cliff");
            i.route = true;
            Assert.AreEqual(GroundRules.Flat, GroundRules.ChooseSlot(i), "scree + route");
            i = Base(); i.screeApron = true; i.route = true;
            Assert.AreEqual(GroundRules.Flat, GroundRules.ChooseSlot(i), "apron + route");
            i.route = false;
            Assert.AreEqual(GroundRules.Scree, GroundRules.ChooseSlot(i), "apron");
            // scree + wet: the bank
            i = Base(); i.upSlopeDeg = 55f; i.wet = true;
            Assert.AreEqual(GroundRules.Hollow, GroundRules.ChooseSlot(i), "scree + wet");
            // sun and shade on the steep edge: 32 deg is steep (bare) facing the sun, not shaded
            i = Base(); i.slopeDeg = 32f; i.sunny = -0.3f;
            Assert.AreEqual(GroundRules.Flat, GroundRules.ChooseSlot(i), "32 deg, shaded");
            i.sunny = 0.3f;
            Assert.AreEqual(GroundRules.Bare, GroundRules.ChooseSlot(i), "32 deg, sunny");
            // the snow line: lower on the shaded side
            i = Base(); i.overSea = 4020f; i.sunny = -0.5f;
            Assert.AreEqual(GroundRules.High, GroundRules.ChooseSlot(i), "at the line, shaded");
            i.sunny = 0.5f;
            Assert.AreEqual(GroundRules.Flat, GroundRules.ChooseSlot(i), "at the line, sunny");
        }

        // ------------------------------------------------------------------
        [Test]
        public void Off_IsUntouched_On_KeepsTheGround_AndTheCoverSlot()
        {
            var w = Get(7);
            int on0 = 0;
            foreach (var p in LandPoints(in w, 600, 0x180b))
            {
                var a = S(in w.off, in w, p.x, p.y);
                var b = S(in w.on, in w, p.x, p.y);
                Assert.AreEqual(0, a.mrSlot | a.mrSoil | a.mrSub | a.mrExpose, "off: no rule field");
                Assert.AreEqual(a.height, b.height, "the rules never move the ground");
                Assert.AreEqual(a.water, b.water);
                Assert.AreEqual(a.biome, b.biome, "the biome (record, ores, danger) is untouched");
                int slot = GroundRules.Slot(b.mrSlot);
                Assert.AreEqual(GroundRules.LegacySlot(slot), Cover.Slot(b.cover), "trees read the Cover slot");
                Assert.AreEqual(0, b.mrExpose, "exposure: shells only");
                Assert.GreaterOrEqual(b.mrSub, b.mrSoil);
                if (b.mrSoil != 0) Assert.GreaterOrEqual(b.mrSoil, GroundRules.SKIN);
                var rb = DetailField.RockA(in b);
                Assert.IsTrue(b.surfBiome == rb || b.surfBiome == DetailField.RockB(in b), "skin biome: one of the rock's two");
                if (slot != GroundRules.Flat) on0++;
            }
            Assert.Greater(on0, 50, "the rules pick other slots than flat");
        }

        // ------------------------------------------------------------------
        [Test]
        public void NearFar_ShapeSlotsAgree_UpToTexel22()
        {
            var w = Get(7);
            int[] texels = { 4, 16, 32, 128 };
            var same = new int[texels.Length]; var asked = new int[texels.Length];
            foreach (var p in LandPoints(in w, 1200, 0x0ff))
            {
                var near = S(in w.on, in w, p.x, p.y);
                int sn = GroundRules.Slot(near.mrSlot);
                for (int t = 0; t < texels.Length; t++)
                {
                    var far = S(in w.on, in w, p.x, p.y, texels[t]);
                    int sf = GroundRules.Slot(far.mrSlot);
                    // shore / bed / wet / high use the column's own height (as Cover's shore / high)
                    if (sn == GroundRules.Shore || sf == GroundRules.Shore || sn == GroundRules.Hollow || sf == GroundRules.Hollow
                        || sn == GroundRules.High || sf == GroundRules.High) continue;
                    asked[t]++;
                    if (near.mrSlot == far.mrSlot && near.surfBiome == far.surfBiome) same[t]++;
                    else if (texels[t] <= 22) Assert.Fail($"slot differs at texel {texels[t]} at {p}: {near.mrSlot}/{near.surfBiome} vs {far.mrSlot}/{far.surfBiome}");
                }
            }
            var line = new StringBuilder("[MatRules] seed 7, same slot + skin biome as texel 1:");
            for (int t = 0; t < texels.Length; t++) line.Append($" texel {texels[t]} {100.0 * same[t] / math.max(1, asked[t]):F1} % of {asked[t]}");
            Debug.Log(line.ToString());
            Assert.Greater(asked[0], 600);
        }

        // ------------------------------------------------------------------
        /// The column top-down: every voxel the caves, ores, pockets and stamps leave
        /// alone is its band's material, the bands never go back up, and the deep
        /// stone starts at mrDeep. The material is a function of the GENERATED column
        /// (no live input): asked again, or through a baked shore field, it is the same.
        [Test]
        public unsafe void Depth_BandsInOrder_PitShowsTheOriginalSurface()
        {
            var w = Get(42);
            var view = w.map.View;
            var baked = GroundRules.BakeShore(in view);
            fixed (ushort* bp = baked)
            {
                var bview = view; bview.shore = bp;
                int checkedVox = 0, cols = 0;
                var seen = new HashSet<string>();
                var pits = new StringBuilder();
                foreach (var p in LandPoints(in w, 300, 0xd16))
                {
                    var c = S(in w.on, in w, p.x, p.y);
                    var cb = ShapeField.Sample(in w.on, in bview, in w.o, p.x + 0.5f, p.y + 0.5f, 1);
                    Assert.AreEqual(c.mrSlot, cb.mrSlot, "baked shore field == scan"); Assert.AreEqual(c.mrSoil, cb.mrSoil);
                    Assert.AreEqual(c.mrSub, cb.mrSub); Assert.AreEqual(c.surfBiome, cb.surfBiome);
                    if (c.stampMat != 0 || c.stamp3D || c.open3Hi > c.open3Lo || c.lipLo != 0 || c.water > c.height) continue;
                    float st = Step(in w.on, in w, p.x, p.y, c.height);
                    var cd = DetailField.PrepareColumn(in w.on, in w.o, p.x, p.y);
                    int last = 0;
                    var seq = new List<string>();
                    byte prevM = 255;
                    for (int d = 0; d < 260; d++)
                    {
                        int y = c.height - d;
                        int band = GroundRules.Band(in c, st, d);
                        Assert.GreaterOrEqual(band, last, "bands never go back up"); last = band;
                        Assert.LessOrEqual(band, 3, "task 182: no depth band for the deep stone");
                        byte m = DetailField.Material(in w.on, in w.o, in c, in cd, st, p.x, y, p.y);
                        Assert.AreEqual(m, DetailField.Material(in w.on, in w.o, in c, in cd, st, p.x, y, p.y), "asked twice");
                        if (!GenMat.IsSolid(m) || (m >= Ores.Copper && m <= Ores.Copper + 4)) continue;   // a cave, pocket or ore
                        byte want = band <= 2 ? GroundRules.NearMaterial(in c, st, band) : GroundRules.RockAt(in w.on, in c, y, cd.deepOffset, cd.cavernOffset, cd.underOffset);
                        Assert.AreEqual(want, m, $"({p.x},{y},{p.y}) depth {d} band {band}");
                        checkedVox++;
                        if (m != prevM) { seq.Add($"{d * 0.1f:F1} m {m}"); prevM = m; }
                    }
                    cols++;
                    string key = $"{GroundRules.SlotName(GroundRules.Slot(c.mrSlot))}/{BIOME[c.surfBiome]}";
                    if (seen.Add(key)) pits.AppendLine($"  {key,-18} pit at ({p.x},{p.y}): " + string.Join(" | ", seq));
                }
                Directory.CreateDirectory(OUT_DIR);
                File.WriteAllText(Path.Combine(OUT_DIR, "pits.txt"), "Task 180b: a dug pit's cut-away (depth where each material starts; GenMat ids), one per slot/biome, seed 42\n" + pits);
                Debug.Log($"[MatRules] {cols} columns, {checkedVox} voxels in their band\n{pits}");
                Assert.Greater(checkedVox, 30000);
            }
        }

        // ------------------------------------------------------------------
        /// Task 182, defect 1 of 180b: the deep stone is chosen by ABSOLUTE height
        /// (GroundRules.DeepTop + the wavy deepOffset), never by depth below the
        /// column's top. Adversarial case: the tallest sheer columns (a single 0.1 m
        /// column stepping 20 m+ to a neighbour: its whole side is a face) of 4
        /// seeds. Every voxel of the face, top to the neighbour's ground, where the
        /// caves / pockets / stamps leave it, is the column's ONE stone; and RockAt
        /// is deep stone exactly under the boundary (both sides, with margin).
        [Test]
        [Category("Slow")]   // task 184b: test-suite tier
        public void TallFace_IsOneStone_NoDeepStoneAboveTheAbsoluteBoundary()
        {
            int faces = 0, faceVox = 0, tallest = 0;
            var log = new StringBuilder("Task 182: tallest sheer faces (seed: face height m, voxels checked)\n");
            foreach (uint seed in SEEDS)
            {
                var w = Get(seed);
                var view = w.map.View;
                // the tallest sheer columns among many land points (their whole side is bare)
                var cand = new List<(int st, int2 p)>();
                // A sheer column is a 0.1 m line along a face: find steep 4 m spans, then the
                // span's biggest single-column step along that axis.
                var seenCol = new HashSet<int2>();
                foreach (var q in LandPoints(in w, 8000, 0x182a ^ seed))
                for (int ax = 0; ax < 2; ax++)
                {
                    int2 u = ax == 0 ? new int2(1, 0) : new int2(0, 1);
                    if (math.abs(S(in w.on, in w, q.x + 20 * u.x, q.y + 20 * u.y).height - S(in w.on, in w, q.x - 20 * u.x, q.y - 20 * u.y).height) < 150) continue;
                    int best = 0; int2 bp = q;
                    int prev = S(in w.on, in w, q.x - 20 * u.x, q.y - 20 * u.y).height;
                    for (int t = -19; t <= 20; t++)
                    {
                        int2 pt = q + t * u;
                        int h = S(in w.on, in w, pt.x, pt.y).height;
                        if (math.abs(h - prev) > best) { best = math.abs(h - prev); bp = h > prev ? pt : pt - u; }
                        prev = h;
                    }
                    if (best <= 60 || !seenCol.Add(bp)) continue;
                    var c = S(in w.on, in w, bp.x, bp.y);
                    if (c.stampMat != 0 || c.stamp3D || c.open3Hi > c.open3Lo || c.lipLo != 0 || c.water > c.height) continue;
                    int lo = math.min(math.min(S(in w.on, in w, bp.x + 1, bp.y).height, S(in w.on, in w, bp.x - 1, bp.y).height),
                                      math.min(S(in w.on, in w, bp.x, bp.y + 1).height, S(in w.on, in w, bp.x, bp.y - 1).height));
                    if (c.height - lo > 60) cand.Add((c.height - lo, bp));
                }
                cand.Sort((a, b) => b.st.CompareTo(a.st));
                int seedTop = 0, seedVox = 0;
                for (int i = 0; i < math.min(8, cand.Count); i++)
                {
                    var p = cand[i].p;
                    var c = S(in w.on, in w, p.x, p.y);
                    float st = Step(in w.on, in w, p.x, p.y, c.height);
                    var cd = DetailField.PrepareColumn(in w.on, in w.o, p.x, p.y);
                    byte stone = GroundRules.Stone(DetailField.RockA(in c)), deep = GroundRules.DeepStone(DetailField.RockA(in c));
                    for (int d = 0; d < cand[i].st; d++)
                    {
                        int y = c.height - d;
                        byte m = DetailField.Material(in w.on, in w.o, in c, in cd, st, p.x, y, p.y);
                        if (!GenMat.IsSolid(m) || Ores.IsOre(m)) continue;   // a cave, pocket or ore
                        int band = GroundRules.Band(in c, st, d);
                        if (band <= 2) continue;   // a skin / soil lip at the top of a not-quite-sheer column
                        Assert.AreNotEqual(deep, m, $"seed {seed} ({p.x},{y},{p.y}) depth {d}: deep stone on a face above the boundary");
                        Assert.AreEqual(stone, m, $"seed {seed} ({p.x},{y},{p.y}) depth {d}: a face is one stone");
                        seedVox++;
                    }
                    seedTop = math.max(seedTop, cand[i].st);
                    faces++;
                }
                faceVox += seedVox; tallest = math.max(tallest, seedTop);
                log.AppendLine($"  seed {seed}: {math.min(8, cand.Count)} faces of {cand.Count} sheer columns, tallest {seedTop * 0.1f:F1} m, {seedVox} voxels one stone");

                // the boundary itself: deep stone exactly under DeepTop + deepOffset
                var c0 = S(in w.on, in w, cand.Count > 0 ? cand[0].p.x : w.on.worldSizeVoxels / 2, cand.Count > 0 ? cand[0].p.y : w.on.worldSizeVoxels / 2);
                byte rb = DetailField.RockA(in c0);
                int top = GroundRules.DeepTop(in w.on);
                float dOff = 37.5f;
                Assert.AreEqual(GroundRules.Stone(rb), GroundRules.RockAt(in w.on, in c0, top - dOff, dOff, 0f, 0f));
                Assert.AreEqual(GroundRules.DeepStone(rb), GroundRules.RockAt(in w.on, in c0, top - dOff - 1f, dOff, 0f, 0f));
                Assert.AreEqual(GroundRules.DeepStone(rb), GroundRules.RockRange(in w.on, in c0, top - 400, top - 200, -160f, 160f, 0f, 0f, 0f, 0f));
                Assert.AreEqual(GroundRules.Stone(rb), GroundRules.RockRange(in w.on, in c0, top + 200, top + 400, -160f, 160f, 0f, 0f, 0f, 0f));
                Assert.AreEqual(255, GroundRules.RockRange(in w.on, in c0, top - 100, top + 100, -160f, 160f, 0f, 0f, 0f, 0f));
            }
            Directory.CreateDirectory(OUT_DIR);
            File.WriteAllText(Path.Combine(OUT_DIR, "tallface182.txt"), log.ToString());
            Debug.Log(log.ToString());
            Assert.Greater(tallest, 200, "the adversarial case exists: a face taller than the old 11-19 m deep-stone depth");
            Assert.Greater(faceVox, 2000);
        }

        // ------------------------------------------------------------------
        [Test]
        public void PocketWall_IsLinedWithItsFluidsShore()
        {
            var w = Get(42);
            int found = 0;
            int lo = (w.off.seaLevelY - w.off.underworldTopBelowSea) >> DetailField.POCKET_CELL_SHIFT;
            int hi = (w.off.seaLevelY - DetailField.POCKET_MAX_TOP_BELOW_SEA) >> DetailField.POCKET_CELL_SHIFT;
            int c0 = (w.off.worldSizeVoxels / 2 - 6000) >> DetailField.POCKET_CELL_SHIFT;
            for (int cz = c0; cz < c0 + 24 && found < 12; cz++)
            for (int cx = c0; cx < c0 + 24 && found < 12; cx++)
            for (int cy = lo; cy <= hi && found < 12; cy++)
            {
                if (!DetailField.TryGetPocket(in w.off, in w.o, cx, cy, cz, out var pk)) continue;
                // a voxel just outside the ellipsoid along +x: d^2 ~ 1.05
                int x = (int)math.floor(pk.centre.x + pk.radii.x * 1.025f), y = (int)math.floor(pk.centre.y), z = (int)math.floor(pk.centre.z);
                float3 q = (new float3(x, y, z) + 0.5f - pk.centre) / pk.radii;
                float d2 = math.lengthsq(q);
                if (d2 < 1f || d2 >= GroundRules.POCKET_LINING) continue;
                var c = S(in w.on, in w, x, z);
                if (c.height - y <= DetailField.CARVE_MIN_DEPTH || c.riverFloor != 0) continue;
                byte on = DetailField.Material(in w.on, in w.o, in c, 0f, x, y, z);
                Assert.AreEqual(GroundRules.PocketLining(pk.fluid, c.surfBiome), on, $"pocket {pk.fluid} at ({x},{y},{z})");
                var co = S(in w.off, in w, x, z);
                Assert.IsTrue(GenMat.IsSolid(DetailField.Material(in w.off, in w.o, in co, 0f, x, y, z)), "off: the shell is rock");
                found++;
            }
            Assert.Greater(found, 2, "pockets found under the island");
        }

        // ------------------------------------------------------------------
        // 4 seeds: the adversarial cases and the owner's per-biome table.
        // ------------------------------------------------------------------
        private static readonly int[] DEPTHS = { 0, 5, 20, 60, 150, 220 };   // voxels: skin, 0.5, 2, 6, 15, 22 m
        private static readonly string[] DEPTH_NAMES = { "top 0-0.3 m", "0.5 m", "2 m", "6 m", "15 m", "22 m" };

        [Test, Category("Slow")]
        public void FourSeeds_AdversarialCases_AndTheTable()
        {
            var slotN = new long[GenBiome.Count, GroundRules.SlotCount];
            var matN = new Dictionary<int, long>[GenBiome.Count, DEPTHS.Length];
            for (int b = 0; b < GenBiome.Count; b++) for (int d = 0; d < DEPTHS.Length; d++) matN[b, d] = new Dictionary<int, long>();
            var colsN = new long[GenBiome.Count];
            var rep = new StringBuilder();
            long borderWet = 0, borderWetA = 0, borderWetB = 0, oasis = 0, cliffs = 0, canyon = 0, outletCols = 0, outletWet = 0;
            var oasisMats = new Dictionary<int, long>();
            foreach (uint seed in SEEDS)
            {
                var w = Get(seed);
                var view = w.map.View;
                int G = 24 * 10;   // 24 m grid over the island's box
                int half = (int)(w.off.islandRadius * 1.25f);
                int cxW = w.off.worldSizeVoxels / 2;
                long seedCols = 0;
                for (int z = cxW - half; z < cxW + half; z += G)
                for (int x = cxW - half; x < cxW + half; x += G)
                {
                    var c = S(in w.on, in w, x, z);
                    if (c.height <= w.off.seaLevelY) continue;
                    int slot = GroundRules.Slot(c.mrSlot);
                    byte b = c.surfBiome;
                    seedCols++; colsN[b]++; slotN[b, slot]++;
                    float st = Step(in w.on, in w, x, z, c.height);
                    // the table: material at each depth (first solid at or under it)
                    if (c.stampMat == 0 && !c.stamp3D)
                    {
                        var cd = DetailField.PrepareColumn(in w.on, in w.o, x, z);
                        for (int di = 0; di < DEPTHS.Length; di++)
                        {
                            byte m = DetailField.Material(in w.on, in w.o, in c, in cd, st, x, c.height - DEPTHS[di], z);
                            matN[b, di][m] = matN[b, di].TryGetValue(m, out long k) ? k + 1 : 1;
                        }
                    }
                    ShapeField.SampleMap(in view, x + 0.5f, z + 0.5f, out float macro, out float rough);
                    var ri = GroundRules.Inputs(in w.on, in view, in w.o, new float2(x + 0.5f, z + 0.5f), macro, rough, false, 1, in c);
                    // river / lake bank across a biome border: both sides' banks occur
                    byte bA = DetailField.RockA(in c), bB = DetailField.RockB(in c);
                    if (slot == GroundRules.Hollow && bA != bB)
                    {
                        borderWet++;
                        if (b == bA) borderWetA++; else borderWetB++;
                    }
                    // oasis: a desert bank
                    if (slot == GroundRules.Hollow && b == GenBiome.Desert && !(c.water > c.height) && c.stampMat == 0 && !(st > DetailField.SHEER_STEP))
                    {
                        oasis++;
                        byte top = DetailField.TopMaterial(in w.on, in c, st, in w.o, x, z);
                        oasisMats[top] = oasisMats.TryGetValue(top, out long k) ? k + 1 : 1;
                        Assert.IsTrue(top == GenMat.Grass || top == GenMat.Mud || top == GenMat.RiverLip, $"oasis top {top} at ({x},{z})");
                    }
                    // tall cliff: a > 60 deg 6 m slope is steep or bare; a sheer face shows the biome's stone
                    if (ri.slopeDeg > 60f && !ri.shoreLow && !ri.underWater)
                    {
                        cliffs++;
                        Assert.IsTrue(slot == GroundRules.Steep || slot == GroundRules.Bare, $"cliff slot {slot} at ({x},{z}) seed {seed}");
                        if (st > DetailField.SHEER_STEP && c.lipLo == 0 && c.stampMat == 0)
                            Assert.AreEqual(GroundRules.Stone(DetailField.RockA(in c)), DetailField.TopMaterial(in w.on, in c, st, in w.o, x, z), "a sheer face is the biome's stone");
                    }
                    // canyon floor: gentle ground under > 46 deg within 20 m upslope is never plain flat / bare
                    if (ri.slopeDeg < 20f && ri.upSlopeDeg > 47f && !ri.route)
                    {
                        canyon++;
                        Assert.IsTrue(slot == GroundRules.Scree || slot == GroundRules.Hollow || slot == GroundRules.High || slot == GroundRules.Shore,
                                      $"canyon floor slot {GroundRules.SlotName(slot)} at ({x},{z}) seed {seed}");
                    }
                }
                // lake outlets: dry gentle ground within 4 m of the junction's water, at most 0.5 m over it, is a bank
                int n = w.off.mapCells, cv = w.off.mapCellVoxels, outlets = 0;
                for (int i = 0; i < n * n && outlets < 40; i += 7)
                {
                    if (w.map.WaterClass[i] != Hydrology.LAKE) continue;
                    int ix = i % n, iz = i / n; bool river = false;
                    for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++)
                    {
                        int j = (iz + dz) * n + ix + dx;
                        if (j >= 0 && j < n * n && w.map.WaterClass[j] == Hydrology.RIVER) river = true;
                    }
                    if (!river) continue;
                    outlets++;
                    for (int z = iz * cv - 2 * cv; z < iz * cv + 3 * cv; z += 20)
                    for (int x = ix * cv - 2 * cv; x < ix * cv + 3 * cv; x += 20)
                    {
                        var c = S(in w.on, in w, x, z);
                        ShapeField.SampleMap(in view, x + 0.5f, z + 0.5f, out float macro, out float rough);
                        var ri = GroundRules.Inputs(in w.on, in view, in w.o, new float2(x + 0.5f, z + 0.5f), macro, rough, false, 1, in c);
                        if (ri.shoreLow || ri.underWater) continue;
                        if (!GroundRules.NearestWater(in view, new float2(x + 0.5f, z + 0.5f), out float wd, out float wl)) continue;
                        if (wd > 40f || c.height - (w.off.seaLevelY + (int)math.floor(wl)) > 5) continue;
                        outletCols++;
                        int slot = GroundRules.Slot(c.mrSlot);
                        Assert.IsTrue(slot == GroundRules.Hollow || slot == GroundRules.Steep || slot == GroundRules.Bare,
                                      $"outlet bank slot {GroundRules.SlotName(slot)} at ({x},{z}) seed {seed}");
                        if (slot == GroundRules.Hollow) outletWet++;
                    }
                }
                rep.AppendLine($"seed {seed}: {seedCols} land columns on a 24 m grid, {outlets} lake outlets");
            }
            rep.AppendLine($"adversarial: banks in a biome border zone {borderWet} (skin = side A {borderWetA}, side B {borderWetB}); "
                         + $"desert oasis banks {oasis}; > 60 deg cliffs {cliffs}; canyon floors {canyon}; lake-outlet bank columns {outletCols} ({outletWet} wet, rest steep)");
            var om = new StringBuilder();
            foreach (var kv in oasisMats) om.Append($" {kv.Key}:{kv.Value}");
            rep.AppendLine($"oasis top materials (GenMat:count):{om}");
            Assert.Greater(borderWetA, 0, "a border bank wears side A somewhere");
            Assert.Greater(borderWetB, 0, "... and side B: banks blend, not one step");
            Assert.Greater(cliffs, 20); Assert.Greater(canyon, 5); Assert.Greater(outletCols, 5);

            // ---- the owner's table ----
            var t = new StringBuilder();
            t.AppendLine("Task 180b -- what the material rules produced, 4 seeds (42 7 2026 31337), land columns on a 24 m grid, by skin biome.");
            t.AppendLine("Slots: share of the biome's columns. Materials: share of columns whose voxel at that depth under the GENERATED top is that material");
            t.AppendLine("(Air / water = a cave or pocket at that depth). Rules: GroundRules.cs header; palette rows: GroundRules.PALETTE.");
            for (int b = 0; b < GenBiome.Count; b++)
            {
                if (colsN[b] == 0) continue;
                t.AppendLine();
                t.Append($"{BIOME[b]} ({colsN[b]} columns)  slots:");
                int present = 0; double maxShare = 0;
                for (int s = 0; s < GroundRules.SlotCount; s++)
                {
                    double sh = 100.0 * slotN[b, s] / colsN[b];
                    if (slotN[b, s] > 0) present++;
                    maxShare = math.max(maxShare, sh);
                    t.Append($" {GroundRules.SlotName(s)} {sh:F1}%");
                }
                t.AppendLine();
                for (int di = 0; di < DEPTHS.Length; di++)
                {
                    var l = new List<KeyValuePair<int, long>>(matN[b, di]);
                    l.Sort((a, c) => c.Value.CompareTo(a.Value));
                    long tot = 0; foreach (var kv in l) tot += kv.Value;
                    var line = new StringBuilder($"    {DEPTH_NAMES[di],-12}");
                    for (int k = 0; k < l.Count && k < 6; k++) line.Append($" {MatName((byte)l[k].Key)} {100.0 * l[k].Value / math.max(1, tot):F0}%");
                    t.AppendLine(line.ToString());
                }
                if (colsN[b] > 500)
                {
                    Assert.GreaterOrEqual(present, 4, $"{BIOME[b]}: at least 4 slots occur");
                    Assert.Less(maxShare, 85.0, $"{BIOME[b]}: no slot over 85 %");
                }
            }
            t.AppendLine();
            t.Append(rep);
            Directory.CreateDirectory(OUT_DIR); Directory.CreateDirectory(SHOT_DIR);
            File.WriteAllText(Path.Combine(OUT_DIR, "table180b.txt"), t.ToString());
            File.WriteAllText(Path.Combine(SHOT_DIR, "table180b.txt"), t.ToString());
            Debug.Log(t.ToString());
        }

        private static string MatName(byte m)
        {
            foreach (var f in typeof(GenMat).GetFields())
                if (f.IsLiteral && f.FieldType == typeof(byte) && (byte)f.GetRawConstantValue() == m) return f.Name;
            if (m >= Ores.Copper && m <= Ores.Copper + 4) return "Ore" + (m - Ores.Copper);
            return m.ToString();
        }
    }
}
