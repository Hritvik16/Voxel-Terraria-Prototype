// Assets/TerrainGen/ShapeField.cs
//
// S(x, z, lod) -- THE SHAPE FIELD (§4.1). 2D, deterministic, evaluable at any
// resolution. Returns the column's top solid voxel and its biome.
//
// BAND-LIMITED BY CONSTRUCTION. `texelVoxels` is the footprint the caller will
// sample at (1 = a voxel column; 8..128 = far-band texels 0.8..12.8 m). Detail
// octaves whose wavelength is below 2 x texelVoxels are skipped, so a coarse
// evaluation is cheaper AND alias-free instead of a point sample of the fine
// field that shimmers as the camera moves. The macro landform comes from the
// world map, whose content is already >= 70 m in wavelength (WorldMap.cs).
//
// THE LOD-0 CONTRACT: Sample(x, z, 1).height IS the top solid voxel D produces
// for that column wherever D does not carve the top (TerrainGenOracleTests).
//
// Biomes are INDEPENDENT OF TOPOGRAPHY (owner, §2.6): climate fields only, never
// height. Borders are the climate fields' contour lines -- organic, no warp.
using Unity.Mathematics;

namespace TerrainGen
{
    public struct ColumnSample
    {
        public int height;       // absolute Y of the top SOLID voxel
        public byte biome;       // GenBiome
        public float rough;      // map detail scale at this column
    }

    public static class ShapeField
    {
        public const int DETAIL_OCTAVES = 7;

        // Wavelengths (voxels) and base amplitudes (voxels) of the detail octaves.
        // 64 m down to 1 m. Amplitudes scale with the map's roughness channel.
        public static float Wavelength(int i) => 640f / (1 << i);
        public static float Amplitude(int i)
        {
            switch (i)
            {
                case 0: return 40f;
                case 1: return 24f;
                case 2: return 14f;
                case 3: return 8f;
                case 4: return 4.5f;
                case 5: return 2.5f;
                default: return 1.3f;
            }
        }

        /// Number of detail octaves S includes at this footprint.
        public static int OctavesFor(int texelVoxels)
        {
            int n = 0;
            for (int i = 0; i < DETAIL_OCTAVES; i++)
                if (Wavelength(i) >= 2f * texelVoxels) n = i + 1;
            return n;
        }

        /// Worst-case |S(texel) - S(1)|: the removed octaves' amplitudes (snoise
        /// is within [-1, 1]) times roughness, plus one voxel for the floor().
        public static float BandLimitBound(int texelVoxels, float rough)
        {
            float b = 0f;
            for (int i = OctavesFor(texelVoxels); i < DETAIL_OCTAVES; i++) b += Amplitude(i);
            return b * rough + 1f;
        }

        public static unsafe ColumnSample Sample(in TerrainSpec spec, in MapView map, in Offsets o,
                                                 float x, float z, int texelVoxels)
        {
            SampleMap(in map, x, z, out float macro, out float rough);
            float2 p = new float2(x, z);
            float detail = 0f;
            int oct = OctavesFor(texelVoxels);
            for (int i = 0; i < oct; i++)
                detail += Amplitude(i) * OctaveNoise(in o, p, i);
            var c = new ColumnSample();
            c.height = spec.seaLevelY + (int)math.floor(macro + detail * rough);
            c.height = math.max(c.height, spec.worldBottomY + 1);
            c.biome = Biome(in o, p);
            c.rough = rough;
            return c;
        }

        /// Detail octave i at p, in [-1, 1]. One function, so the determinism
        /// diagnostics evaluate exactly what Sample evaluates.
        public static float OctaveNoise(in Offsets o, float2 p, int i)
        {
            float w = Wavelength(i);
            return noise.snoise(p / w + o.detail + new float2(i * 13.7f, i * -7.9f));
        }

        /// Largest height step to a 4-neighbour at `spacing` voxels, per voxel.
        /// At spacing 1 this is EXACTLY the falling-solid stability test: a sand
        /// top is stable at rest iff no neighbour is more than 1 voxel lower.
        public static float MaxStep(in TerrainSpec spec, in MapView map, in Offsets o,
                                    float x, float z, int spacing, int centreHeight)
        {
            int texel = spacing;
            int a = Sample(in spec, in map, in o, x + spacing, z, texel).height;
            int b = Sample(in spec, in map, in o, x - spacing, z, texel).height;
            int c = Sample(in spec, in map, in o, x, z + spacing, texel).height;
            int d = Sample(in spec, in map, in o, x, z - spacing, texel).height;
            int m = math.max(math.max(math.abs(a - centreHeight), math.abs(b - centreHeight)),
                             math.max(math.abs(c - centreHeight), math.abs(d - centreHeight)));
            return m / (float)spacing;
        }

        /// Climate -> biome. Nearest climate centre in (temperature, moisture).
        public static byte Biome(in Offsets o, float2 p)
        {
            float t = Noise.Fbm(p / 15000f + o.tempA, 3) * 2.4f;
            float m = Noise.Fbm(p / 12000f + o.moistA, 3) * 2.4f;
            // centres: forest (0, 0), desert (0.8, -0.8), snow (-0.9, 0.1), jungle (0.8, 0.8)
            float df = t * t + m * m;
            float dd = (t - 0.8f) * (t - 0.8f) + (m + 0.8f) * (m + 0.8f);
            float ds = (t + 0.9f) * (t + 0.9f) + (m - 0.1f) * (m - 0.1f);
            float dj = (t - 0.8f) * (t - 0.8f) + (m - 0.8f) * (m - 0.8f);
            byte b = GenBiome.Forest; float best = df;
            if (dd < best) { best = dd; b = GenBiome.Desert; }
            if (ds < best) { best = ds; b = GenBiome.Snow; }
            if (dj < best) { b = GenBiome.Jungle; }
            return b;
        }

        /// Catmull-Rom over the map (C1: no creases along 8 m cell edges).
        public static unsafe void SampleMap(in MapView map, float x, float z, out float height, out float rough)
        {
            float gx = x / map.cellVoxels - 0.5f;
            float gz = z / map.cellVoxels - 0.5f;
            int ix = (int)math.floor(gx), iz = (int)math.floor(gz);
            float fx = gx - ix, fz = gz - iz;
            float4 wx = CatmullRom(fx), wz = CatmullRom(fz);
            float h = 0f, r = 0f;
            int n = map.cells;
            for (int j = 0; j < 4; j++)
            {
                int cz = math.clamp(iz - 1 + j, 0, n - 1);
                float hr = 0f, rr = 0f;
                for (int i = 0; i < 4; i++)
                {
                    int cx = math.clamp(ix - 1 + i, 0, n - 1);
                    int idx = cz * n + cx;
                    hr += wx[i] * map.height[idx];
                    rr += wx[i] * map.rough[idx];
                }
                h += wz[j] * hr;
                r += wz[j] * rr;
            }
            height = h;
            rough = math.max(0.05f, r);
        }

        private static float4 CatmullRom(float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return new float4(
                -0.5f * t3 + t2 - 0.5f * t,
                1.5f * t3 - 2.5f * t2 + 1f,
                -1.5f * t3 + 2f * t2 + 0.5f * t,
                0.5f * t3 - 0.5f * t2);
        }
    }
}
