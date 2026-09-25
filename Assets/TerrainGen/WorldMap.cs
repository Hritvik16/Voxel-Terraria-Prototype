// Assets/TerrainGen/WorldMap.cs
//
// §2.4 WORLD MAP FIRST. Built once per world from the seed, before any voxel:
// the macro landform (organic island envelope, mountain ranges, the dominant
// peak, plateaus, an escarpment, valleys) at mapCellVoxels resolution, plus a
// roughness channel that scales S's fine detail.
//
// v1 SCOPE, STATED: the map is currently an analytic field CACHED on a grid.
// Nothing in it is non-local yet. Rivers drained to the sea, lakes filled to
// their spill level and erosion are the reason the map exists (§2.4) and are
// the next stage; the API is shaped so they slot in as passes over these
// arrays without changing S or D.
//
// Band limit: every NOISE octave here has a wavelength >= 700 voxels (70 m),
// far above the map's 16 m Nyquist. The landmark stamps and plateau/butte
// cliffs are sharper (cliff bands ~20-30 m): at the far band's coarsest
// levels (6.4, 12.8 m texels) a cliff is 2-5 texels wide, so it is resolved,
// not aliased -- but it is the part of the map closest to the limit.
using System;
using System.Runtime.InteropServices;
using Unity.Mathematics;

namespace TerrainGen
{
    /// Blittable read-only view of a map, for Burst. Pointers stay valid while
    /// the owner is alive (WorldMap pins its arrays). A Burst job should hold
    /// NativeArrays and build this view inside Execute, so the job struct itself
    /// carries no raw pointer.
    public unsafe struct MapView
    {
        public float* height;   // voxels relative to sea level
        public float* rough;    // detail amplitude scale
        public int cells;
        public float cellVoxels;
    }

    public sealed class WorldMap : IDisposable
    {
        public readonly TerrainSpec Spec;
        public readonly float[] Height;
        public readonly float[] Rough;
        public readonly float2 PeakPosition;   // voxels
        private GCHandle _hH, _hR;

        public WorldMap(TerrainSpec spec, float[] height, float[] rough, float2 peak)
        {
            Spec = spec; Height = height; Rough = rough; PeakPosition = peak;
            _hH = GCHandle.Alloc(Height, GCHandleType.Pinned);
            _hR = GCHandle.Alloc(Rough, GCHandleType.Pinned);
        }

        public unsafe MapView View => new MapView
        {
            height = (float*)_hH.AddrOfPinnedObject(),
            rough = (float*)_hR.AddrOfPinnedObject(),
            cells = Spec.mapCells,
            cellVoxels = Spec.mapCellVoxels,
        };

        public void Dispose()
        {
            if (_hH.IsAllocated) _hH.Free();
            if (_hR.IsAllocated) _hR.Free();
        }

        // ------------------------------------------------------------------
        // Build
        // ------------------------------------------------------------------
        public static WorldMap Build(TerrainSpec spec)
        {
            int n = spec.mapCells;
            var h = new float[n * n];
            var r = new float[n * n];
            var o = Offsets.For(spec.seed);
            float2 peak = PeakSite(spec, o);
            for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                float2 p = new float2((x + 0.5f) * spec.mapCellVoxels, (z + 0.5f) * spec.mapCellVoxels);
                Macro(in spec, in o, peak, p, out h[z * n + x], out r[z * n + x]);
            }
            return new WorldMap(spec, h, r, peak);
        }

        /// The dominant summit: a seeded site in the island's inner third.
        public static float2 PeakSite(in TerrainSpec spec, in Offsets o)
        {
            float c = spec.worldSizeVoxels * 0.5f;
            float2 u = o.peakDir;                       // unit-ish, seeded
            return new float2(c, c) + u * (spec.islandRadius * 0.32f);
        }

        public const int LANDMARK_CELL = 6000;   // voxels: one candidate per 600 m cell

        /// Height (voxels) added by the meso landmarks at p. One candidate per
        /// LANDMARK_CELL, jittered inside its cell; the 3x3 neighbourhood is
        /// searched because a landmark's footprint (<= 1,100 voxels) is far
        /// smaller than a cell. Shapes: SPIRE (a steep cone), BUTTE (a flat top
        /// with cliff sides), TOR (a low rounded cluster). `edge` is 1 on the
        /// cliff band, for the roughness channel.
        public static float Landmarks(in Offsets o, float2 p, float landWeight, out float edge)
        {
            edge = 0f;
            if (landWeight <= 0f) return 0f;
            int cx = (int)math.floor(p.x / LANDMARK_CELL), cz = (int)math.floor(p.y / LANDMARK_CELL);
            float add = 0f;
            for (int j = -1; j <= 1; j++)
            for (int i = -1; i <= 1; i++)
            {
                int gx = cx + i, gz = cz + j;
                if (Noise.Hash01(gx, 0, gz, o.seed + 101u) > 0.75f) continue;       // 75% of cells hold one
                float2 c = new float2(gx + 0.2f + 0.6f * Noise.Hash01(gx, 1, gz, o.seed + 102u),
                                      gz + 0.2f + 0.6f * Noise.Hash01(gx, 2, gz, o.seed + 103u)) * LANDMARK_CELL;
                float2 d = p - c;
                float dist = math.sqrt(d.x * d.x + d.y * d.y);
                float kind = Noise.Hash01(gx, 3, gz, o.seed + 104u);
                float size = Noise.Hash01(gx, 4, gz, o.seed + 105u);
                if (kind < 0.40f)
                {
                    // SPIRE: radius 250-450 voxels, 80-160 m tall, steep.
                    float r = 250f + 200f * size, h = 800f + 800f * size;
                    float t = math.saturate(1f - dist / r);
                    add = math.max(add, h * t * t * (3f - 2f * t) * t);
                    edge = math.max(edge, t > 0f && t < 0.8f ? 1f : 0f);
                }
                else if (kind < 0.75f)
                {
                    // BUTTE: flat top, radius 500-1,000 voxels, 50-110 m, cliffs ~30 m wide.
                    float r = 500f + 500f * size, h = 500f + 600f * size;
                    float t = Noise.SmoothStep(r, r - 300f, dist);
                    add = math.max(add, h * t);
                    edge = math.max(edge, t > 0.05f && t < 0.95f ? 1f : 0f);
                }
                else
                {
                    // TOR: a low rounded knoll, radius 350-600, 25-55 m.
                    float r = 350f + 250f * size, h = 250f + 300f * size;
                    float t = math.saturate(1f - dist / r);
                    add = math.max(add, h * t * t * (3f - 2f * t));
                }
            }
            return add * landWeight;
        }

        /// The macro landform at p (voxels), height relative to sea level.
        public static void Macro(in TerrainSpec spec, in Offsets o, float2 peak, float2 p, out float height, out float rough)
        {
            float c = spec.worldSizeVoxels * 0.5f;
            float2 q = p - new float2(c, c);
            float d = math.sqrt(q.x * q.x + q.y * q.y) / spec.islandRadius;

            // Organic coast WITHOUT domain warping (owner, §2.5): the envelope
            // plus noise, thresholded at zero. Bays, headlands and islets are
            // where the noise beats the envelope, not where coordinates bend.
            float coast = (1f - d) + Noise.Fbm(p / 9000f + o.coastA, 5) * 0.45f
                                   + Noise.Fbm(p / 2600f + o.coastB, 3) * 0.12f;

            if (coast <= 0f)
            {
                float deep = math.min(1f, -coast * 3.5f);
                height = -(60f + (spec.oceanDepth - 60f) * deep * deep * (3f - 2f * deep));
                rough = 0.25f;
                return;
            }

            float inland = math.saturate(coast * 5f);
            float lowland = spec.lowlandHeight * (0.2f + 0.8f * (Noise.Fbm(p / 4200f + o.low, 4) * 0.5f + 0.5f)) * inland;

            // Mountain ranges: ridged noise inside a low-frequency range mask.
            float rangeMask = Noise.SmoothStep(0.05f, 0.40f, Noise.Fbm(p / 17000f + o.range, 3)) * math.saturate(coast * 3f);
            float ridges = Noise.Ridged(p / 7200f + o.ridge, 4);
            float mountains = rangeMask * ridges * spec.rangeHeight;

            // The dominant peak (§2.2: one ~600 m summit), with ridged spurs.
            float2 dp = p - peak;
            float pd = math.saturate(1f - math.sqrt(dp.x * dp.x + dp.y * dp.y) / 10500f);
            float peakShape = pd * pd * (3f - 2f * pd) * (0.82f + 0.18f * ridges);
            float peakHeight = peakShape * spec.peakHeight;

            float hgt = math.max(lowland + mountains, peakHeight);

            // Plateaus / table-tops: a flat top where a noise field crosses a
            // narrow band -- the band's width sets the cliff steepness.
            float pf = Noise.Fbm(p / 11500f + o.plateau, 3);
            float pm = Noise.SmoothStep(0.30f, 0.335f, pf) * math.saturate(coast * 3f) * (1f - rangeMask) * (1f - pd);
            float plateauTop = 900f + 550f * (Noise.Fbm(p / 30000f + o.plateauTop, 2) * 0.5f + 0.5f);
            hgt = math.max(hgt, hgt + (plateauTop - hgt) * pm);

            // One long escarpment: a step along a very-low-frequency contour.
            float ef = Noise.Fbm(p / 26000f + o.escarp, 2);
            hgt += Noise.SmoothStep(-0.015f, 0.015f, ef) * 380f * math.saturate(coast * 2f);

            // Valleys: cut toward the lowland along another field's zero set.
            float vf = math.abs(Noise.Fbm(p / 9500f + o.valley, 3));
            float valley = 1f - Noise.SmoothStep(0f, 0.07f, vf);
            hgt -= valley * 0.4f * math.max(0f, hgt - 80f);

            // Meso landmarks (§2.5): spires, buttes, tors on a jittered grid, so
            // lowlands and plateaus get something on every horizon too.
            hgt += Landmarks(in o, p, math.saturate(coast * 4f), out float lmEdge);

            // Beaches: flatten the last few metres at the waterline.
            float shore = math.saturate(coast * 12f);
            height = math.max(8f, hgt) * shore + 8f * (1f - shore) - 10f * (1f - inland);

            float edge = pm * (1f - pm) * 4f;
            // Detail scale. Lower on the ranges than first authored: the first
            // far-band preview read as noisy "rock teeth" at 0.8 m blocks.
            rough = 0.35f + 1.0f * rangeMask + 0.7f * pd + 0.8f * edge + 0.5f * lmEdge;
        }
    }

    /// Seed-derived noise-domain offsets. Integer hashing only, so every
    /// project derives the same offsets from the same seed.
    public struct Offsets
    {
        public float2 coastA, coastB, low, range, ridge, plateau, plateauTop, escarp, valley;
        public float2 tempA, moistA, detail, soil, strata, dip;
        public float3 caveA, caveB, cavern, under;
        public float2 peakDir;
        public uint seed;

        public static Offsets For(uint seed)
        {
            var o = new Offsets { seed = seed };
            uint s = seed * 747796405u + 2891336453u;
            o.coastA = Next2(ref s); o.coastB = Next2(ref s); o.low = Next2(ref s);
            o.range = Next2(ref s); o.ridge = Next2(ref s); o.plateau = Next2(ref s);
            o.plateauTop = Next2(ref s); o.escarp = Next2(ref s); o.valley = Next2(ref s);
            o.tempA = Next2(ref s); o.moistA = Next2(ref s); o.detail = Next2(ref s);
            o.soil = Next2(ref s); o.strata = Next2(ref s); o.dip = Next2(ref s);
            o.caveA = new float3(Next2(ref s), Next1(ref s)); o.caveB = new float3(Next2(ref s), Next1(ref s));
            o.cavern = new float3(Next2(ref s), Next1(ref s)); o.under = new float3(Next2(ref s), Next1(ref s));
            // A direction from an angle, without sin/cos: a hashed point on the
            // square, normalised by sqrt (IEEE-exact).
            float2 v = new float2(Next1(ref s), Next1(ref s)) / 1000f;
            float len = math.sqrt(v.x * v.x + v.y * v.y);
            o.peakDir = len > 1e-3f ? v / len : new float2(1f, 0f);
            return o;
        }

        private static uint Step(ref uint s) { s ^= s << 13; s ^= s >> 17; s ^= s << 5; return s; }
        private static float Next1(ref uint s) => (Step(ref s) & 0xFFFFFu) / (float)0xFFFFFu * 2000f - 1000f;
        private static float2 Next2(ref uint s) => new float2(Next1(ref s), Next1(ref s));
    }
}
