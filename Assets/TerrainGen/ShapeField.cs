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
        /// Absolute Y of the still-water surface over this column (lake or
        /// river, map v2), or NO_WATER_Y. Water fills (height, water].
        public int water;
        public const int NO_WATER_Y = int.MinValue;
        /// 0 = water, 1 = lava (the volcano's caldera lake).
        public byte waterKind;

        // ---- §2.5 stamps (Stamps.Column) ----
        /// Air intervals [lo, hi) in this column (arches, hoops, ribs, the
        /// space under the tree's canopy). Never contain the top voxel.
        public int open1Lo, open1Hi, open2Lo, open2Hi;
        /// The stamp's own material for y in [stampBodyLo, height]; 0 = none
        /// (natural geology, e.g. an arch).
        public byte stampMat;
        public int stampBodyLo;
        /// D must run Stamps.Carve3D in this column (mouths, fissures, the skull
        /// cave, root caves, vents, a crater core).
        public bool stamp3D;
        /// The column's TOP voxel is carved by a surface opening (cave mouth,
        /// fissure, skull mouth): the LOD-0 oracle excludes exactly these.
        public bool carvesTop;
        /// The stamp site's map ground (absolute Y), for D's 3D tests.
        public int stampGround;
        /// v3 T5: the REGIONAL ground (absolute Y) -- the map's mean height over
        /// +-120 m. D runs a passage layer below it; where a valley or cliff cuts
        /// under that level, the passage breaks out through the wall.
        public float regional;
        /// v3 T5b: the air under a CAP-ROCK LEDGE, [lo, hi). Like the stamp
        /// intervals, never contains the top voxel (the ledge IS the top).
        public int open3Lo, open3Hi;
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
            {
                float acc = macro;
                SampleMap(in map, x + 1200f, z, out float r0, out _); acc += r0;
                SampleMap(in map, x - 1200f, z, out float r1, out _); acc += r1;
                SampleMap(in map, x, z + 1200f, out float r2, out _); acc += r2;
                SampleMap(in map, x, z - 1200f, out float r3, out _); acc += r3;
                c.regional = spec.seaLevelY + acc * 0.2f;
            }
            float wl = SampleWater(in map, x, z);
            c.water = wl > Hydrology.NO_WATER * 0.5f ? spec.seaLevelY + (int)math.floor(wl) : ColumnSample.NO_WATER_Y;
            // §2.5 stamps. Skipped for a dry (pre-v2) map view, which is what
            // every older job builds -- they keep their exact prior output.
            if (map.water != null) Stamps.Column(in spec, in map, in o, p, texelVoxels, ref c);
            if (map.water != null && spec.mapVersion >= 3) Ledge(in spec, in map, in o, p, ref c);
            return c;
        }

        // ------------------------------------------------------------------
        // v3 T5b CAP-ROCK LEDGES (owner, 2026-09-26: "do the buttes and
        // overhangs and let me know if it is worth it"). At the FOOT of a cliff
        // -- the map stands >= LEDGE_MIN_STEP higher within a few metres, which
        // is what the layered regions' terraces and the buttes make -- the
        // column's top is raised to a shelf LEDGE_BELOW_CAP under the cliff's
        // cap level, with air between its own ground and the shelf's underside:
        // a lip of hard rock over a soft undercut. Width and presence vary
        // along the cliff by a 2-D noise, so a cliff gets ledges in runs, not
        // a uniform band. 2-D only (owner: no 3-D noise), per column, and the
        // top voxel stays the column's top, so every consumer of S (the far
        // band, LOD-0, the air fast path) is unchanged in kind.
        // Skipped over water (a river at the cliff foot keeps its surface) and
        // in stamp columns (their intervals win).
        // ------------------------------------------------------------------
        public const float LEDGE_PROBE = 55f;          // voxels towards the cliff (5.5 m, max width)
        public const int LEDGE_MIN_STEP = 150;         // 15 m higher within the probe = a cliff
        public const int LEDGE_BELOW_CAP = 30;         // the shelf sits 3 m under the cap level
        public const int LEDGE_MIN_GAP = 25;           // no gap under 2.5 m (a crawl is not a ledge)

        private static void Ledge(in TerrainSpec spec, in MapView map, in Offsets o, float2 p, ref ColumnSample c)
        {
            if (c.stamp3D || c.open1Hi > c.open1Lo || c.water > c.height || c.height < spec.seaLevelY + 60) return;
            float n = noise.snoise(p / 900f + o.detail + new float2(71f, -13f));
            if (n < -0.15f) return;                                        // ~40% of cliff length has none
            float w = LEDGE_PROBE * (0.45f + 0.55f * math.saturate(n + 0.15f));
            SampleMap(in map, p.x, p.y, out float own, out _);
            float best = own;
            for (int k = 0; k < 8; k++)
            {
                SampleMap(in map, p.x + w * DirX(k), p.y + w * DirZ(k), out float h, out _);
                best = math.max(best, h);
            }
            if (best - own < LEDGE_MIN_STEP) return;
            int top = spec.seaLevelY + (int)math.floor(best) - LEDGE_BELOW_CAP;
            float thick = 25f + 20f * (0.5f + 0.5f * noise.snoise(p / 300f + o.detail + new float2(-40f, 88f)));
            int under = top - (int)thick;
            if (top <= c.height || under - (c.height + 1) < LEDGE_MIN_GAP) return;
            c.open3Lo = c.height + 1;
            c.open3Hi = under;
            c.height = top;
        }

        /// The column as the FAR BAND draws it. The far band is a 2.5D heightfield
        /// (one top per texel), so a ledge column drawn at its top fills the
        /// undercut down to the ground: a pillar up to ~10 m tall at the cliff
        /// foot (task 32c: the slab at the v4 pose cliff_foot_1). From map v4 the
        /// far band draws the ground under the ledge instead; the cliff behind it
        /// keeps its own columns, and the voxels keep the ledge. v3 is unchanged.
        public static void FarColumn(in TerrainSpec spec, ref ColumnSample c)
        {
            if (spec.mapVersion < 4 || c.open3Hi <= c.open3Lo) return;
            c.height = c.open3Lo - 1;   // Ledge: open3Lo is the column's own ground + 1
            c.open3Lo = c.open3Hi = 0;
        }

        private static float DirX(int k) => k == 0 ? 1f : k == 1 ? 0.7071f : k == 2 ? 0f : k == 3 ? -0.7071f : k == 4 ? -1f : k == 5 ? -0.7071f : k == 6 ? 0f : 0.7071f;
        private static float DirZ(int k) => DirX((k + 6) & 7);

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
        /// Map v4 multiplies both wavelengths by o.biomeScale (B1, larger
        /// biomes); at 1 the products are exactly 15000f and 12000f, so v3 is
        /// byte-identical.
        public static byte Biome(in Offsets o, float2 p)
        {
            float s = o.biomeScale > 0f ? o.biomeScale : 1f;
            float t = Noise.Fbm(p / (15000f * s) + o.tempA, 3) * 2.4f;
            float m = Noise.Fbm(p / (12000f * s) + o.moistA, 3) * 2.4f;
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

        /// Still-water level (voxels rel. sea) at (x, z): the highest level among
        /// the 2x2 cells around the point, so a lake's water reaches every
        /// column its basin holds (the terrain, not the cell edge, is the
        /// shore). NO_WATER when none of the four has water or the map is dry.
        public static unsafe float SampleWater(in MapView map, float x, float z)
        {
            if (map.water == null) return Hydrology.NO_WATER;
            float gx = x / map.cellVoxels - 0.5f, gz = z / map.cellVoxels - 0.5f;
            int ix = (int)math.floor(gx), iz = (int)math.floor(gz);
            int n = map.cells;
            float w = Hydrology.NO_WATER;
            for (int j = 0; j <= 1; j++)
            for (int i = 0; i <= 1; i++)
            {
                int cx = math.clamp(ix + i, 0, n - 1), cz = math.clamp(iz + j, 0, n - 1);
                w = math.max(w, map.water[cz * n + cx]);
            }
            return w;
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
