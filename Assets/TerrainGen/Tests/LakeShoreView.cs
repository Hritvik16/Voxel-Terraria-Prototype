// Assets/TerrainGen/Tests/LakeShoreView.cs
//
// Task 42: a top-down picture of one whole lake, for the owner to judge the
// shoreline by eye (REPORTS=1 only; asserts nothing). The lake is picked from
// the map, so the same lake is drawn before and after a generator change: the
// first lake cell (scan order) whose 17 x 17 cell window holds 40-400 lake
// cells, none on the window's edge ring. One pixel = 2 x 2 voxels (0.2 m).
// Lake water: blue, lighter where shallow; other water: dark blue; land:
// hill-shaded, green near the lake level to brown higher up. Ticks on the frame
// mark the 8 m dual-cell lines (where 37e's shores ran).
// Writes Logs/TerrainGen/lake_view_<seed>.png. Uses only APIs that existed
// before task 42 (lake water = water at ShapeField.LakeRimY), so the same file
// draws the old code.
using System.IO;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace TerrainGen.Tests
{
    public class LakeShoreView
    {
        private const int WIN = 17, STEP = 2;

        [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High, CompileSynchronously = true)]
        private struct ViewJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float> h, r, w;
            [ReadOnly] public NativeArray<byte> cls;
            public TerrainSpec spec; public Offsets o;
            public int x0, z0, size;
            [NativeDisableParallelForRestriction] public NativeArray<int> height, water;
            [NativeDisableParallelForRestriction] public NativeArray<byte> kind;   // 0 land, 1 lake water, 2 other water

            public unsafe void Execute(int row)
            {
                var map = new MapView
                {
                    height = (float*)h.GetUnsafeReadOnlyPtr(), rough = (float*)r.GetUnsafeReadOnlyPtr(),
                    water = (float*)w.GetUnsafeReadOnlyPtr(), waterClass = (byte*)cls.GetUnsafeReadOnlyPtr(),
                    cells = spec.mapCells, cellVoxels = spec.mapCellVoxels,
                };
                for (int i = 0; i < size; i++)
                {
                    float x = x0 + i * STEP + 0.5f, z = z0 + row * STEP + 0.5f;
                    var c = ShapeField.Sample(in spec, in map, in o, x, z, 1);
                    int k = row * size + i;
                    height[k] = c.height; water[k] = c.water;
                    kind[k] = c.water <= c.height ? (byte)0
                            : c.waterKind == 0 && c.water == ShapeField.LakeRimY(in spec, in map, x, z, out _) ? (byte)1 : (byte)2;
                }
            }
        }

        [Category("Slow")]
        [TestCase(7u)]
        public void LakeView(uint seed)
        {
            ReportOnly.Require();
            var spec = TerrainSpec.SizeClass2(seed, 4);
            var o = Offsets.For(in spec);
            var map = SharedMaps.Get(spec);
            int n = spec.mapCells, cv = spec.mapCellVoxels;

            int pick = -1;
            for (int c = 0; c < n * n && pick < 0; c++)
            {
                if (map.WaterClass[c] != Hydrology.LAKE) continue;
                int cx = c % n - WIN / 2, cz = c / n - WIN / 2, count = 0; bool edge = false;
                if (cx < 0 || cz < 0 || cx + WIN > n || cz + WIN > n) continue;
                for (int j = 0; j < WIN; j++)
                for (int i = 0; i < WIN; i++)
                {
                    if (map.WaterClass[(cz + j) * n + cx + i] != Hydrology.LAKE) continue;
                    count++;
                    if (i == 0 || j == 0 || i == WIN - 1 || j == WIN - 1) edge = true;
                }
                if (!edge && count >= 40 && count <= 400) pick = c;
            }
            Assert.GreaterOrEqual(pick, 0, "no lake fits the window");
            int wx = pick % n - WIN / 2, wz = pick / n - WIN / 2;
            int x0 = wx * cv, z0 = wz * cv, size = WIN * cv / STEP;

            var hA = new NativeArray<float>(map.Height, Allocator.TempJob);
            var rA = new NativeArray<float>(map.Rough, Allocator.TempJob);
            var wA = new NativeArray<float>(map.Water, Allocator.TempJob);
            var cA = new NativeArray<byte>(map.WaterClass, Allocator.TempJob);
            var hh = new NativeArray<int>(size * size, Allocator.TempJob);
            var ww = new NativeArray<int>(size * size, Allocator.TempJob);
            var kk = new NativeArray<byte>(size * size, Allocator.TempJob);
            var px = new UnityEngine.Color32[size * size];
            string otherText = "";
            float level = map.Water[pick] + spec.seaLevelY;
            try
            {
                new ViewJob { h = hA, r = rA, w = wA, cls = cA, spec = spec, o = o, x0 = x0, z0 = z0, size = size, height = hh, water = ww, kind = kk }
                    .Schedule(size, 4).Complete();
                for (int j = 0; j < size; j++)
                for (int i = 0; i < size; i++)
                {
                    int k = j * size + i;
                    float r, g, b;
                    if (kk[k] == 1)
                    {
                        float d = math.saturate((ww[k] - hh[k]) / 60f);
                        r = math.lerp(110, 25, d); g = math.lerp(170, 70, d); b = math.lerp(230, 160, d);
                    }
                    else if (kk[k] == 2) { r = 30; g = 45; b = 110; }
                    else
                    {
                        int il = math.max(0, i - 1), ir = math.min(size - 1, i + 1), jd = math.max(0, j - 1), ju = math.min(size - 1, j + 1);
                        float gx = (hh[j * size + ir] - hh[j * size + il]) / (2f * STEP), gz = (hh[ju * size + i] - hh[jd * size + i]) / (2f * STEP);
                        float shade = math.saturate(0.75f + 0.6f * (-gx * 0.6f + gz * 0.8f) / math.sqrt(1f + gx * gx + gz * gz));
                        float t = math.saturate((hh[k] - level) / 150f);
                        r = math.lerp(120, 150, t) * shade; g = math.lerp(170, 120, t) * shade; b = math.lerp(80, 90, t) * shade;
                        if (hh[k] == (int)math.floor(level)) { r = 215 * shade; g = 195 * shade; b = 120 * shade; }   // ground exactly at the level
                    }
                    // ticks on the frame at the dual-cell lines (x, z = 40 + 80 k voxels)
                    int vx = x0 + i * STEP, vz = z0 + j * STEP;
                    bool tick = ((vx - cv / 2) % cv < STEP && (j < 12 || j >= size - 12)) || ((vz - cv / 2) % cv < STEP && (i < 12 || i >= size - 12));
                    if (tick) { r = g = b = 255; }
                    px[j * size + i] = new UnityEngine.Color32((byte)r, (byte)g, (byte)b, 255);   // texture row 0 is the PNG's bottom: +z up
                }
                // What the "other water" is: the map cells under it (class, level vs this lake's).
                var other = new System.Collections.Generic.SortedDictionary<string, int>();
                for (int k = 0; k < size * size; k++)
                {
                    if (kk[k] != 2) continue;
                    int i = k % size, j = k / size;
                    int cx = (x0 + i * STEP) / cv, cz = (z0 + j * STEP) / cv, c = cz * n + cx;
                    string key = $"class {map.WaterClass[c]} cell level {map.Water[c] + spec.seaLevelY:F0} column water {ww[k]}";
                    other[key] = other.TryGetValue(key, out int m) ? m + 1 : 1;
                }
                otherText = string.Join("\n  ", System.Linq.Enumerable.Take(System.Linq.Enumerable.Select(other, kv => $"{kv.Key}: {kv.Value} px"), 12));
            }
            finally { hA.Dispose(); rA.Dispose(); wA.Dispose(); cA.Dispose(); hh.Dispose(); ww.Dispose(); kk.Dispose(); }
            var tex = new UnityEngine.Texture2D(size, size, UnityEngine.TextureFormat.RGBA32, false);
            tex.SetPixels32(px); tex.Apply();
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "TerrainGen");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, $"lake_view_{seed}.png");
            File.WriteAllBytes(path, UnityEngine.ImageConversion.EncodeToPNG(tex));
            UnityEngine.Object.DestroyImmediate(tex);
            UnityEngine.Debug.Log($"lake view seed {seed}: cell ({pick % n},{pick / n}) level {level} window x {x0}..{x0 + WIN * cv} z {z0}..{z0 + WIN * cv} voxels -> {path}");
            File.WriteAllText(Path.Combine(dir, $"lake_view_{seed}.txt"), $"seed {seed} lake cell ({pick % n},{pick / n}) level {level} window x {x0}..{x0 + WIN * cv} z {z0}..{z0 + WIN * cv} voxels (0.1 m)\nother water (class 0 dry, 1 lake, 2 river):\n  {otherText}");
        }
    }
}
