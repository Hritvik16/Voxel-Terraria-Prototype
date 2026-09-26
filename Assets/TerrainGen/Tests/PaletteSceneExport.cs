// Assets/TerrainGen/Tests/PaletteSceneExport.cs
//
// Owner, 2026-09-25, on the first palette board: "I'd need to see the actual
// terrain and numerous seeds to decide." This exports REAL GENERATED VOLUMES --
// DetailField.Material, the function a chunk is generated from -- for several
// seeds and several kinds of terrain, as GenMat-id grids that
// tools/palette-render.py ray-traces (sun + shadows) in every candidate palette.
// Nothing is painted by hand: the palette is the only variable.
//
// Scenes per seed (step = voxels per sample; 1 voxel = 0.1 m). SINCE v3
// (§13, 2026-09-25): the island, one 512 m block per topography REGION, an
// alpine close-up and the volcano; the biome blocks, coast and cave scenes
// below were round 2's list (caves are being replaced, T4):
//   island      the whole island, 16 m x 2 m samples
//   forest / desert / snow / jungle
//               a 256 m block of that biome with real relief, 1 m samples,
//               chosen on the map (biome at the centre and all four corners)
//   volcano     the cone and its caldera lava lake, 2 m samples
//   coast       a 256 m stretch of shoreline, 1 m samples
//   cutaway     128 m down from the surface into the cavern layer, 1 m
//               (the renderer cuts a quadrant away to show the inside)
//   underworld  the lava sea's chamber, 1 m (renderer lifts the roof off)
//   close_<biome>
//               19.2 m cubes at NATIVE voxel scale (0.1 m) on a steep slope:
//               what a player standing there sees
//
// Report-only: asserts nothing, and runs only when PALETTE3D=1 is set (it takes
// minutes), so the normal suite reports it as skipped. PALETTE_SEEDS="1,2,3"
// overrides the seed list.
//
// Format per scene (.vxv): "VXV1", int32 nx, ny, nz, stepXZ, stepY, ox, oy, oz
// (absolute tg2 voxels of sample (0,0,0)), then nx*ny*nz bytes, index
// (y * nz + z) * nx + x, y upward. manifest.json lists the scenes.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Unity.Mathematics;

namespace TerrainGen.Tests
{
    public class PaletteSceneExport
    {
        private static readonly uint[] DefaultSeeds = { 42u, 7u, 2026u, 31337u };

        [Test, Timeout(3600000)]   // ~90 s a seed; the 180 s default stopped the first run mid-seed
        public void Report_PaletteScenes3D()
        {
            if (Environment.GetEnvironmentVariable("PALETTE3D") != "1")
                Assert.Ignore("report only: set PALETTE3D=1 (tools/run-editmode-filter.sh PaletteSceneExport)");
            uint[] seeds = DefaultSeeds;
            string env = Environment.GetEnvironmentVariable("PALETTE_SEEDS");
            if (!string.IsNullOrEmpty(env))
            {
                var parts = env.Split(',');
                seeds = new uint[parts.Length];
                for (int i = 0; i < parts.Length; i++) seeds[i] = uint.Parse(parts[i].Trim());
            }
            string root = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "TerrainGen", "palette3d");
            Directory.CreateDirectory(root);
            var log = new StringBuilder();
            foreach (uint seed in seeds) ExportSeed(seed, Path.Combine(root, "seed_" + seed), log);
            File.WriteAllText(Path.Combine(root, "export_log.txt"), log.ToString());
            UnityEngine.Debug.Log("[PaletteScenes3D]\n" + log);
        }

        private sealed class Ctx
        {
            public TerrainSpec spec;
            public Offsets o;
            public WorldMap map;
            public string dir;
            public StringBuilder log, manifest;
            public int scenes;
        }

        private static void ExportSeed(uint seed, string dir, StringBuilder log)
        {
            Directory.CreateDirectory(dir);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var spec = TerrainSpec.SizeClass2(seed);
            var ctx = new Ctx { spec = spec, o = Offsets.For(seed), map = WorldMap.Build(spec), dir = dir, log = log, manifest = new StringBuilder() };
            log.AppendLine($"seed {seed}: map built in {sw.ElapsedMilliseconds} ms, peak {ctx.map.PeakPosition}");
            ctx.manifest.Append("{\"seed\": ").Append(seed).Append(", \"seaLevelY\": ").Append(spec.seaLevelY)
                        .Append(", \"cavernTopY\": ").Append(spec.CavernTopY).Append(", \"underworldTopY\": ").Append(spec.UnderworldTopY)
                        .Append(", \"scenes\": [");
            try
            {
                float c = spec.worldSizeVoxels * 0.5f;

                // ---- the whole island ----
                {
                    int half = 30000, stepXZ = 160, stepY = 20;
                    float maxH = 0; foreach (float h in ctx.map.Height) maxH = math.max(maxH, h);
                    int y0 = spec.seaLevelY - 400, y1 = spec.seaLevelY + (int)maxH + 200;
                    Box(ctx, "island", "The whole island", (int)c - half, (int)c - half, 2 * half / stepXZ, 2 * half / stepXZ, y0, y1, stepXZ, stepY, "island");
                }

                // ---- v3 (§13): one block per topography region ----
                // A 512 m block around each archetype's first region seed, 2 m
                // samples, heights fitted to the ground in the box.
                if (ctx.map.RegionLayout != null)
                {
                    var L = ctx.map.RegionLayout;
                    var seen = new bool[Regions.KINDS];
                    for (int si = 0; si < L.Count; si++)
                    {
                        byte k = L.kind[si];
                        if (seen[k]) continue;
                        seen[k] = true;
                        const int SIZE = 5120, STEP = 20;
                        int bx = (int)L.seeds[si].x - SIZE / 2, bz = (int)L.seeds[si].y - SIZE / 2;
                        ColumnRangeCoarse(ctx, bx, bz, SIZE, out int lo, out int hi);
                        string nm = "region_" + Regions.Name(k).Replace(' ', '_');
                        Box(ctx, nm, $"{Regions.Name(k)} (512 m block)", bx, bz, SIZE / STEP, SIZE / STEP,
                            math.max(lo - 200, spec.seaLevelY - 200), hi + 150, STEP, STEP, "block");
                        if (k == Regions.Alpine && si == 0)
                        {
                            FindSteep(ctx, bx + SIZE / 4, bz + SIZE / 4, SIZE / 2, out int sx, out int sz, out int sy);
                            ColumnRange(ctx, sx - 96, sz - 96, 192, out int cLo, out int cHi, out _);
                            Box(ctx, "close_alpine", "alpine range at native voxel scale (19.2 m)", sx - 96, sz - 96, 192, 192, cLo - 40, cHi + 30, 1, 1, "close");
                        }
                    }
                }

                // ---- the volcano ----
                {
                    float2 v = Stamps.VolcanoSite(in spec, in ctx.o);
                    int half = 9600, step = 60;
                    int y1 = spec.seaLevelY + (int)Stamps.VOLCANO_H + 1500;
                    int y0 = spec.seaLevelY - 200;
                    Box(ctx, "volcano", "The volcano and its caldera lava lake (1.9 km)", (int)v.x - half, (int)v.y - half, 2 * half / step, 2 * half / step, y0, y1, step, 30, "volcano");
                }
            }
            finally
            {
                ctx.manifest.Append("]}");
                File.WriteAllText(Path.Combine(dir, "manifest.json"), ctx.manifest.ToString());
                ctx.map.Dispose();
            }
            log.AppendLine($"seed {seed}: {ctx.scenes} scenes in {sw.ElapsedMilliseconds} ms");
        }

        /// Samples the box and writes it. x0/z0/y0/y1 absolute tg2 voxels; nx/nz
        /// sample counts; ny from (y1 - y0) / stepY.
        private static void Box(Ctx ctx, string name, string title, int x0, int z0, int nx, int nz, int y0, int y1, int stepXZ, int stepY, string kind)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var spec = ctx.spec; var o = ctx.o; var map = ctx.map;
            y0 = math.max(y0, 0);
            int ny = math.max(1, (y1 - y0) / stepY);
            var vol = new byte[(long)nx * ny * nz];
            int texel = math.clamp(stepXZ, 1, 128);
            Parallel.For(0, nz, z =>
            {
                var view = map.View;
                for (int x = 0; x < nx; x++)
                {
                    int gx = x0 + x * stepXZ, gz = z0 + z * stepXZ;
                    var col = ShapeField.Sample(in spec, in view, in o, gx + 0.5f, gz + 0.5f, texel);
                    float st = ShapeField.MaxStep(in spec, in view, in o, gx + 0.5f, gz + 0.5f, texel, col.height);
                    var cd = DetailField.PrepareColumn(in o, gx, gz);
                    for (int y = 0; y < ny; y++)
                        vol[((long)y * nz + z) * nx + x] = DetailField.Material(in spec, in o, in col, in cd, st, gx, y0 + y * stepY, gz);
                }
            });
            using (var f = new BinaryWriter(File.Create(Path.Combine(ctx.dir, name + ".vxv"))))
            {
                f.Write(Encoding.ASCII.GetBytes("VXV1"));
                f.Write(nx); f.Write(ny); f.Write(nz); f.Write(stepXZ); f.Write(stepY); f.Write(x0); f.Write(y0); f.Write(z0);
                f.Write(vol);
            }
            if (ctx.scenes++ > 0) ctx.manifest.Append(", ");
            ctx.manifest.Append($"{{\"name\": \"{name}\", \"title\": \"{title}\", \"kind\": \"{kind}\", \"nx\": {nx}, \"ny\": {ny}, \"nz\": {nz}, \"stepXZ\": {stepXZ}, \"stepY\": {stepY}, \"x0\": {x0}, \"y0\": {y0}, \"z0\": {z0}}}");
            ctx.log.AppendLine($"  {name,-14} {nx}x{ny}x{nz} step {stepXZ}/{stepY} at ({x0},{y0},{z0}) in {sw.ElapsedMilliseconds} ms");
        }

        private static float MapH(Ctx ctx, int cx, int cz)
        {
            int n = ctx.spec.mapCells;
            cx = math.clamp(cx, 0, n - 1); cz = math.clamp(cz, 0, n - 1);
            return ctx.map.Height[cz * n + cx];
        }

        private static bool NearSingleton(Ctx ctx, float2 p, float margin)
        {
            var spec = ctx.spec; var o = ctx.o;
            if (math.distance(p, Stamps.VolcanoSite(in spec, in o)) < Stamps.VOLCANO_BASE_R + margin) return true;
            if (Stamps.InTreeArea(in spec, in o, p, 400f + margin)) return true;
            if (math.distance(p, Stamps.GeyserSite(in spec, in o)) < 1600f + margin) return true;
            return false;
        }

        /// A block of `biome` on dry land, relief as close to 120 m as the seed
        /// offers. Purity is checked on a 5x5 grid; a pure block wins, and a
        /// block with >= 21 of 25 in-biome samples is the fallback (seed 42 has
        /// no fully pure 256 m forest block). Relief over 250 m (the peak's
        /// flanks) is refused: it renders as a tower, not a landscape.
        private static bool FindBiomeBlock(Ctx ctx, byte biome, int size, out int bx, out int bz, out float lo, out float hi)
        {
            bx = bz = 0; lo = hi = 0;
            int n = ctx.spec.mapCells, cv = (int)ctx.spec.mapCellVoxels, span = size / cv;
            float best = float.MaxValue;
            for (int cz = 0; cz + span < n; cz += 8)
            for (int cx = 0; cx + span < n; cx += 8)
            {
                float2 p0 = new float2(cx * cv, cz * cv);
                float2 mid = p0 + size * 0.5f;
                if (NearSingleton(ctx, mid, size)) continue;
                if (ShapeField.Biome(in ctx.o, mid) != biome) continue;
                // The WHOLE block must be this biome: a 5x5 grid, not just the
                // corners (round 2's first "forest" block was mostly snow).
                int inBiome = 0;
                for (int j = 0; j <= 4; j++)
                for (int i = 0; i <= 4; i++)
                    if (ShapeField.Biome(in ctx.o, p0 + new float2(i, j) * (size / 4f)) == biome) inBiome++;
                if (inBiome < 21) continue;
                float mn = float.MaxValue, mx = float.MinValue;
                int wet = 0, cells = 0;
                for (int z = 0; z <= span; z += 2)
                for (int x = 0; x <= span; x += 2)
                {
                    float h = MapH(ctx, cx + x, cz + z);
                    mn = math.min(mn, h); mx = math.max(mx, h);
                    int ci = math.clamp(cz + z, 0, n - 1) * n + math.clamp(cx + x, 0, n - 1);
                    if (ctx.map.Water[ci] > h) wet++;
                    cells++;
                }
                if (mn < 40f) continue;                        // wholly dry land, 4 m above the sea
                if (mx - mn > 2500f) continue;
                // Relief near 120 m; a block that is mostly lake shows water, not the biome.
                float score = math.abs((mx - mn) - 1200f) + 4000f * wet / math.max(1, cells) + 3000f * (25 - inBiome);
                if (score < best) { best = score; bx = cx * cv; bz = cz * cv; lo = mn; hi = mx; }
            }
            return best < float.MaxValue;
        }

        /// A close-up site: slope near 0.7 over 2 m (grass lip, soil and exposed
        /// strata together), then, among the 25 best by slope, the first whose
        /// 19.2 m box spans 6-22 m of height -- a box around a 60 m cliff renders
        /// as a striped tower, not as ground someone stands on.
        private static void FindSteep(Ctx ctx, int bx, int bz, int size, out int sx, out int sz, out int sy)
        {
            var spec = ctx.spec; var o = ctx.o; var view = ctx.map.View;
            sx = bx + size / 2; sz = bz + size / 2; sy = spec.seaLevelY;
            var cands = new List<(float score, int x, int z, int h)>();
            for (int z = 200; z < size - 200; z += 40)
            for (int x = 200; x < size - 200; x += 40)
            {
                int gx = bx + x, gz = bz + z;
                int h = ShapeField.Sample(in spec, in view, in o, gx + 0.5f, gz + 0.5f, 1).height;
                int hx = ShapeField.Sample(in spec, in view, in o, gx + 20.5f, gz + 0.5f, 1).height;
                int hz = ShapeField.Sample(in spec, in view, in o, gx + 0.5f, gz + 20.5f, 1).height;
                float g = math.length(new float2(hx - h, hz - h)) / 20f;
                // Steep, but not a wall: a ~45 degree slope fits the 19.2 m box and
                // shows the grass lip, the soil and exposed strata together.
                cands.Add((-math.abs(g - 0.7f), gx, gz, h));
            }
            cands.Sort((a, b) => b.score.CompareTo(a.score));
            int bestRange = int.MaxValue;
            for (int i = 0; i < math.min(25, cands.Count); i++)
            {
                var c = cands[i];
                ColumnRange(ctx, c.x - 96, c.z - 96, 192, out int lo, out int hi, out float wet);
                // Dry ground: round 2's first pass put three close-ups under a lake.
                if (wet > 0.1f) continue;
                int r = hi - lo;
                if (r >= 60 && r <= 220) { sx = c.x; sz = c.z; sy = c.h; return; }
                if (r < bestRange) { bestRange = r; sx = c.x; sz = c.z; sy = c.h; }
            }
        }

        /// Lowest and highest map ground (absolute Y) over a box, on the map grid.
        private static void ColumnRangeCoarse(Ctx ctx, int x0, int z0, int size, out int lo, out int hi)
        {
            var spec = ctx.spec; var o = ctx.o; var view = ctx.map.View;
            lo = int.MaxValue; hi = int.MinValue;
            for (int z = 0; z <= size; z += 80)
            for (int x = 0; x <= size; x += 80)
            {
                int h = math.max(ShapeField.Sample(in spec, in view, in o, x0 + x + 0.5f, z0 + z + 0.5f, 8).height, spec.seaLevelY);
                lo = math.min(lo, h); hi = math.max(hi, h);
            }
        }

        /// Lowest and highest top voxel over a size x size voxel box (4-voxel
        /// grid), and the fraction of those columns under water (sea or lake).
        private static void ColumnRange(Ctx ctx, int x0, int z0, int size, out int lo, out int hi, out float wet)
        {
            var spec = ctx.spec; var o = ctx.o; var view = ctx.map.View;
            lo = int.MaxValue; hi = int.MinValue;
            int w = 0, n = 0;
            for (int z = 0; z < size; z += 4)
            for (int x = 0; x < size; x += 4)
            {
                var c = ShapeField.Sample(in spec, in view, in o, x0 + x + 0.5f, z0 + z + 0.5f, 1);
                lo = math.min(lo, c.height); hi = math.max(hi, c.height);
                if (c.height < spec.seaLevelY || c.water > c.height) w++;
                n++;
            }
            wet = w / (float)math.max(1, n);
        }

        private static bool FindCoast(Ctx ctx, out int x, out int z)
        {
            // Walk from the island centre outward along a few bearings; take the
            // first bearing whose shoreline is rocky (steepest drop), not a spit.
            var spec = ctx.spec; float c = spec.worldSizeVoxels * 0.5f;
            x = z = 0; float bestDrop = -1f;
            for (int a = 0; a < 16; a++)
            {
                float ang = a * math.PI * 2f / 16f;
                float2 d = new float2(math.cos(ang), math.sin(ang));
                float prev = MapH(ctx, (int)(c / spec.mapCellVoxels), (int)(c / spec.mapCellVoxels));
                for (float r = 8000f; r < 38000f; r += 80f)
                {
                    float2 p = new float2(c, c) + d * r;
                    float h = MapH(ctx, (int)(p.x / spec.mapCellVoxels), (int)(p.y / spec.mapCellVoxels));
                    if (h < 0f && prev >= 0f)
                    {
                        if (NearSingleton(ctx, p, 2560f)) break;
                        float2 back = p - d * 800f;
                        float drop = MapH(ctx, (int)(back.x / spec.mapCellVoxels), (int)(back.y / spec.mapCellVoxels));
                        if (drop > bestDrop) { bestDrop = drop; x = (int)p.x; z = (int)p.y; }
                        break;
                    }
                    prev = h;
                }
            }
            return bestDrop >= 0f;
        }

        /// A HALF-open spot of the underworld chamber among a few candidates
        /// (air or lava on a coarse grid just above the lava sea): all-open is a
        /// featureless lava plane, all-closed is rock; half shows shore and pillars.
        private static void FindUnderworld(Ctx ctx, out int bx, out int bz)
        {
            var spec = ctx.spec; var view = ctx.map.View; var o = ctx.o;
            float c = spec.worldSizeVoxels * 0.5f;
            bx = (int)c; bz = (int)c; float best = float.MaxValue;
            int y = spec.worldBottomY + 450;
            for (int i = 0; i < 16; i++)
            {
                float ang = i * 2.39996f, r = 2000f + i * 1200f;
                int px = (int)(c + math.cos(ang) * r), pz = (int)(c + math.sin(ang) * r), open = 0;
                for (int dz = -900; dz <= 900; dz += 300)
                for (int dx = -900; dx <= 900; dx += 300)
                {
                    int gx = px + dx, gz = pz + dz;
                    var col = ShapeField.Sample(in spec, in view, in o, gx + 0.5f, gz + 0.5f, 1);
                    var cd = DetailField.PrepareColumn(in o, gx, gz);
                    byte m = DetailField.Material(in spec, in o, in col, in cd, 0f, gx, y, gz);
                    if (m == GenMat.Air || m == GenMat.Lava) open++;
                }
                float score = math.abs(open / 49f - 0.55f);
                if (score < best) { best = score; bx = px; bz = pz; }
            }
        }

        private static bool FindLowland(Ctx ctx, out int x, out int z, out int y)
        {
            var spec = ctx.spec; var view = ctx.map.View; var o = ctx.o;
            float c = spec.worldSizeVoxels * 0.5f;
            x = z = y = 0;
            for (int i = 0; i < 400; i++)
            {
                float ang = i * 2.39996f, r = 3000f + i * 60f;
                float2 p = new float2(c, c) + new float2(math.cos(ang), math.sin(ang)) * r;
                if (NearSingleton(ctx, p, 1500f)) continue;
                var col = ShapeField.Sample(in spec, in view, in o, p.x, p.y, 1);
                int rel = col.height - spec.seaLevelY;
                if (rel < 100 || rel > 600 || col.water > col.height) continue;
                x = (int)p.x; z = (int)p.y; y = col.height;
                return true;
            }
            return false;
        }
    }
}
