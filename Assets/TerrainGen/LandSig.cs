// TASK 189 (WORLD_GEN_LOOK_PLAN C6, finding 6: "landforms that do not show at
// walking scale" -- a dune sea under jungle is a flat lawn, canyonlands a
// rolling sand sheet, mountains smooth cones). Behind `-landsig 1`
// (TerrainSpec.LandSig), default OFF: with it off nothing here runs and every
// byte is as before.
//
// Each landform gets a MESO shape (20-200 m) that depends on the landform and
// the ground's own data, NEVER on the biome:
//   dunes      the map's own transverse dune train (WorldMap.DuneField, across
//              the seed's wind), sharpened IN PHASE at the column: a long
//              straight stoss slope, a sharp brink and a straight slip face
//              (DUNE_LEE of each DUNE_WAVE) on the lee side. Between
//              crests the 16 m / 8 m generic octaves are smoothed too
//              (DUNE_SMOOTH_OCTAVES): wind-sorted ground.
//   canyonlands benches: the column is terraced on the rock layers
//              (DetailField.LayerBoundary, the same strata the faces show), a
//              flat tread and a steep riser (BENCH_RISER of each layer); and
//              SLOT CANYONS: the zero lines of a noise stretched ALONG the
//              regional downslope (they follow the drainage), cut SLOT_DEPTH
//              with a flat floor and near-vertical walls, in patches.
//   alpine     ridgelines and side valleys: a ridged noise stretched along the
//              regional downslope (sharp crest lines and rounded valleys that
//              run down the mountainside), scaled by the regional slope, so
//              flats get none. Mostly CARVED (RIDGE_MEAN of the noise's range is
//              raise): the crests stay near the ground, the side valleys cut.
// "Regional downslope" is the map's gradient over +-ORIENT_PROBE. A spatially
// varying ROTATION of the noise frame would shear it across the world (a tiny
// angle change times an 80 km coordinate), so the stretched noise is evaluated
// in FOUR FIXED frames (0/45/90/135 deg) and the two bracketing the local
// orientation are blended (tent weights, variance kept). On ground with no
// clear slope it blends to the isotropic noise.
//
// WHERE: a per-cell weight plane (BuildPlane; PLANE_BYTES per map cell): the
// region archetype weights (Regions.Weights, the same blend the map uses, so a
// tiny patch gets a fraction and a landform border fades over Regions.BLEND),
// times a fade from KEEP-OUT cells -- sea, lake or river water, easy areas and
// route corridors (the world record's flags) -- from
// FADE_FROM to FADE_TO map cells (a chamfer distance). Bilinear at the column,
// so the height stays continuous everywhere. Signature FEATURES are NOT kept
// out: a great peak is the alpine range's own summit (its ridges are the point),
// a great canyon or mesa sits in its landform; a crater lake is water.
//
// MESO OCTAVES: the generic detail octaves are isotropic lumps; the first
// MESO_OCTAVES of them (64 m and 32 m, ShapeField.Wavelength) are what makes
// every landform the same rolling sheet. Where a signature applies they are
// scaled by MesoKeep = 1 - MESO_GIVE x (the summed weight): the landform's own
// shape carries that band instead. Finer octaves (16 m and below) stay.
//
// BAND LIMIT: each term drops below its own footprint (like an octave);
// BandBound adds what a dropped term and the bench's stretch can move.
// No seed constants: every input is a function of the seed's offsets, the map
// and the record. Nothing is tuned to a place or a pose.
using Unity.Mathematics;

namespace TerrainGen
{
    public static unsafe class LandSig
    {
        public const int PLANE_BYTES = 3, DUNE = 0, CANYON = 1, ALPINE = 2;
        /// Region weights are computed every 1 << NODE_SHIFT cells (32 m) and
        /// interpolated (Regions.BLEND is 150 m, so this is exact enough).
        public const int NODE_SHIFT = 2;
        /// Fade from keep-out cells, in map cells (16 m .. 48 m).
        public const float FADE_FROM = 2f, FADE_TO = 6f;

        // ---- dunes (voxels) ----
        /// The map dune's wavelength (160 m), lee share (40 m slip face) and top
        /// height (26 m) -- WorldMap.DuneField's numbers; the column adds DUNE_GAIN
        /// of the height as the sharp profile (up to 13 m: a ~18 deg slip face on
        /// its own, ~30-40 deg with the map's surviving dune under it).
        public const float DUNE_WAVE = 1600f, DUNE_LEE = 0.25f, DUNE_AMP_MAX = 260f, DUNE_GAIN = 0.5f;
        // ---- canyonlands ----
        public const float BENCH_RISER = 0.2f;
        public const float SLOT_LONG = 4000f, SLOT_WIDE = 2000f, SLOT_N = 0.025f, SLOT_WALL = 0.4f,
                           SLOT_DEPTH = 120f, SLOT_PATCH = 3000f;
        /// Slots are ~5 m wide: drawn at footprints up to this many voxels.
        public const int SLOT_MAX_TEXEL = 8;
        // ---- alpine ----
        public const float RIDGE_LONG = 4000f, RIDGE_WIDE = 1200f, RIDGE_AMP = 500f, RIDGE_MEAN = 0.15f;
        public const float RIDGE_SLOPE_FROM = 0.1f, RIDGE_SLOPE_FULL = 0.4f;
        // ---- orientation ----
        /// 200 m: the mountainside's direction, not a bump's. A summit and the ground 40 m
        /// round it see nearly the same slope, so ridges never carve a ring round an
        /// untouched top (a needle; NoNeedles_LandSigColumns).
        public const float ORIENT_PROBE = 2000f, ORIENT_FROM = 0.03f, ORIENT_FULL = 0.12f;

        public const int MESO_OCTAVES = 2;
        /// Dunes also smooth the next octaves (16 m, 8 m): wind-sorted ground has
        /// no lumps of its own between the dunes' crests (and a symmetric 16 m lump
        /// is as steep as a slip face, so it would hide the dune's asymmetry).
        public const int DUNE_SMOOTH_OCTAVES = 4;
        public const float MESO_GIVE = 0.75f;
        /// The share of the broad detail octaves kept at weights w (1 = all, OFF).
        public static float MesoKeep(float3 w) => 1f - MESO_GIVE * math.saturate(w.x + w.y + w.z);
        /// The share of generic detail octave i kept at weights w (1 = all, OFF).
        public static float OctaveKeep(float3 w, int i) =>
            i < MESO_OCTAVES ? MesoKeep(w) : i < DUNE_SMOOTH_OCTAVES ? 1f - MESO_GIVE * math.saturate(w.x) : 1f;

        /// Most a dropped term or the bench stretch moves a column at a footprint > 1.
        public static float BandBound(float b) =>
            b / BENCH_RISER + DUNE_GAIN * DUNE_AMP_MAX + RIDGE_AMP + SLOT_DEPTH + ShapeField.LEDGE_MAX;

        // ------------------------------------------------------------------
        // The weight plane
        // ------------------------------------------------------------------
        public static byte[] BuildPlane(WorldMap map)
        {
            var spec = map.Spec;
            int n = spec.mapCells;
            float cv = spec.mapCellVoxels;
            var o = Offsets.Compute(in spec);
            var L = map.RegionLayout;
            var rec = map.Record;
            bool flags = rec != null && rec.Cells == n;
            const byte KEEP_FLAGS = WorldRecord.FLAG_EASY | WorldRecord.FLAG_ROUTE;
            // 1. Chamfer (3-4) distance to the nearest keep-out cell, in thirds of a cell.
            const int BIG = 1 << 20;
            var d = new int[n * n];
            for (int i = 0; i < n * n; i++)
            {
                bool keep = map.Height[i] <= 0f || map.Water[i] > Hydrology.NO_WATER * 0.5f || map.WaterClass[i] != Hydrology.DRY
                            || (flags && (rec.Data[i].flags & KEEP_FLAGS) != 0);
                d[i] = keep ? 0 : BIG;
            }
            for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                int i = z * n + x, v = d[i];
                if (x > 0) v = math.min(v, d[i - 1] + 3);
                if (z > 0)
                {
                    v = math.min(v, d[i - n] + 3);
                    if (x > 0) v = math.min(v, d[i - n - 1] + 4);
                    if (x < n - 1) v = math.min(v, d[i - n + 1] + 4);
                }
                d[i] = v;
            }
            for (int z = n - 1; z >= 0; z--)
            for (int x = n - 1; x >= 0; x--)
            {
                int i = z * n + x, v = d[i];
                if (x < n - 1) v = math.min(v, d[i + 1] + 3);
                if (z < n - 1)
                {
                    v = math.min(v, d[i + n] + 3);
                    if (x < n - 1) v = math.min(v, d[i + n + 1] + 4);
                    if (x > 0) v = math.min(v, d[i + n - 1] + 4);
                }
                d[i] = v;
            }
            // 2. Region weights on the node grid.
            int m = (n >> NODE_SHIFT) + 2;
            var wc = new float[m * m * PLANE_BYTES];
            var w = new float[Regions.KINDS];
            for (int j = 0; j < m; j++)
            for (int i = 0; i < m; i++)
            {
                float2 p = new float2(((i << NODE_SHIFT) + 0.5f) * cv, ((j << NODE_SHIFT) + 0.5f) * cv);
                Regions.Weights(L, in o, p, w);
                int k = (j * m + i) * PLANE_BYTES;
                wc[k + DUNE] = w[Regions.Dunes]; wc[k + CANYON] = w[Regions.Canyonlands]; wc[k + ALPINE] = w[Regions.Alpine];
            }
            // 3. The cell plane: bilinear node weights x the keep-out fade.
            var plane = new byte[n * n * PLANE_BYTES];
            int mask = (1 << NODE_SHIFT) - 1;
            float inv = 1f / (1 << NODE_SHIFT);
            for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                int i = z * n + x;
                float fade = Noise.SmoothStep(FADE_FROM * 3f, FADE_TO * 3f, d[i]);
                if (fade <= 0f) continue;
                int i0 = x >> NODE_SHIFT, j0 = z >> NODE_SHIFT;
                float fx = (x & mask) * inv, fz = (z & mask) * inv;
                int a = (j0 * m + i0) * PLANE_BYTES, b = a + PLANE_BYTES, c = a + m * PLANE_BYTES, e = c + PLANE_BYTES;
                for (int k = 0; k < PLANE_BYTES; k++)
                {
                    float v = math.lerp(math.lerp(wc[a + k], wc[b + k], fx), math.lerp(wc[c + k], wc[e + k], fx), fz);
                    plane[i * PLANE_BYTES + k] = (byte)math.round(255f * math.saturate(v * fade));
                }
            }
            return plane;
        }

        /// The (dune, canyon, alpine) weights at p: the plane, bilinear between cell centres.
        public static float3 Weights(in MapView map, float2 p)
        {
            if (map.landSig == null) return float3.zero;
            int n = map.cells;
            float gx = p.x / map.cellVoxels - 0.5f, gz = p.y / map.cellVoxels - 0.5f;
            int x0 = math.clamp((int)math.floor(gx), 0, n - 2), z0 = math.clamp((int)math.floor(gz), 0, n - 2);
            float fx = math.saturate(gx - x0), fz = math.saturate(gz - z0);
            byte* s = map.landSig;
            int a = (z0 * n + x0) * PLANE_BYTES, b = a + PLANE_BYTES, c = a + n * PLANE_BYTES, e = c + PLANE_BYTES;
            float3 w00 = new float3(s[a], s[a + 1], s[a + 2]), w10 = new float3(s[b], s[b + 1], s[b + 2]);
            float3 w01 = new float3(s[c], s[c + 1], s[c + 2]), w11 = new float3(s[e], s[e + 1], s[e + 2]);
            return math.lerp(math.lerp(w00, w10, fx), math.lerp(w01, w11, fx), fz) * (1f / 255f);
        }

        // ------------------------------------------------------------------
        // The column
        // ------------------------------------------------------------------
        /// The column's height (voxels rel. sea) with the landform signatures;
        /// `h` is macro + detail. Unchanged where every weight is 0.
        public static float Relief(in TerrainSpec spec, in MapView map, in Offsets o, float2 p, float h, int texel)
            => Relief(in spec, in map, in o, p, h, texel, Weights(in map, p));

        /// As above with the weights already read (ShapeField.Sample).
        public static float Relief(in TerrainSpec spec, in MapView map, in Offsets o, float2 p, float h, int texel, float3 w)
        {
            if (w.x <= 0f && w.y <= 0f && w.z <= 0f) return h;
            float dh = 0f;
            if (w.x > 0f && DUNE_LEE * DUNE_WAVE >= 2f * texel) dh += w.x * Dune(in o, p);
            float2 dir = new float2(1f, 0f); float conf = 0f, slope = 0f;
            if (w.y > 0f || w.z > 0f) Orientation(in map, p, out dir, out conf, out slope);
            if (w.z > 0f && RIDGE_WIDE >= 2f * texel) dh += w.z * Ridges(in o, p, dir, conf, slope);
            h += dh;
            if (w.y > 0f)
            {
                if (ShapeField.LEDGE_SPACING >= 2f * texel) h -= w.y * BenchCut(in spec, in o, p, h);
                if (texel <= SLOT_MAX_TEXEL) h -= w.y * SlotDepth(in o, p, dir, conf);
            }
            return h;
        }

        /// Dune relief (voxels, mean 0): the map's own dune train (WorldMap.DuneField,
        /// 160 m apart, survives in the map at about half its height, smoothed by
        /// the 8 m cells), sharpened IN PHASE: the same crests, plus a straight
        /// stoss, a sharp brink and a straight slip face (DUNE_LEE of the wave)
        /// DUNE_GAIN of the map dune's height. A second train at another
        /// wavelength would beat against the map's and cancel the asymmetry.
        public static float Dune(in Offsets o, float2 p)
        {
            float t = DunePhase(in o, p);
            t -= math.floor(t);
            float y = t < 1f - DUNE_LEE ? t / (1f - DUNE_LEE) : (1f - t) / DUNE_LEE;
            return DUNE_GAIN * DuneAmp(in o, p) * (y - 0.5f);
        }

        // MUST stay the same expressions as WorldMap.DuneField (pinned by
        // LandSigTests.Dune_PhaseLockedToTheMapDunes).
        public static float DunePhase(in Offsets o, float2 p)
        {
            float2 wind = Wind(in o);
            float2 perp = new float2(-wind.y, wind.x);
            return math.dot(p, wind) / DUNE_WAVE + 0.35f * Noise.Fbm(p / 5000f + o.soil, 3) + 0.08f * math.dot(p, perp) / DUNE_WAVE;
        }

        public static float DuneAmp(in Offsets o, float2 p) =>
            DUNE_AMP_MAX * (0.6f + 0.4f * (Noise.Fbm(p / 6000f + o.soil + 3f, 2) * 0.5f + 0.5f));

        /// The dune wind: the map's DuneField wind (one per seed).
        public static float2 Wind(in Offsets o) => math.normalizesafe(o.dip + new float2(0.3f, 0.1f), new float2(1f, 0f));

        /// The regional downslope direction at p, how sure it is (0 on flats) and the slope.
        public static void Orientation(in MapView map, float2 p, out float2 dir, out float conf, out float slope)
        {
            ShapeField.SampleMap(in map, p.x + ORIENT_PROBE, p.y, out float x1, out _);
            ShapeField.SampleMap(in map, p.x - ORIENT_PROBE, p.y, out float x0, out _);
            ShapeField.SampleMap(in map, p.x, p.y + ORIENT_PROBE, out float z1, out _);
            ShapeField.SampleMap(in map, p.x, p.y - ORIENT_PROBE, out float z0, out _);
            float2 g = new float2(x1 - x0, z1 - z0) * (0.5f / ORIENT_PROBE);
            slope = math.length(g);
            dir = slope > 1e-6f ? g / slope : new float2(1f, 0f);
            conf = Noise.SmoothStep(ORIENT_FROM, ORIENT_FULL, slope);
        }

        private static float2 Frame(int i) =>
            i == 0 ? new float2(1f, 0f) : i == 1 ? new float2(0.70710678f, 0.70710678f) : i == 2 ? new float2(0f, 1f) : new float2(-0.70710678f, 0.70710678f);

        private static float Aniso(float2 p, int i, float along, float across, float2 off)
        {
            float2 d = Frame(i), q = new float2(-d.y, d.x);
            return noise.snoise(new float2(math.dot(p, d) / along, math.dot(p, q) / across) + off + new float2(i * 17.3f, i * -9.1f));
        }

        /// A noise stretched `along` x `across` with its long axis on `dir`
        /// (orientation mod 180 deg), blended to an isotropic noise of `across`
        /// as conf -> 0. Unit-variance-ish like snoise.
        public static float Oriented(float2 p, float2 dir, float conf, float along, float across, float2 off)
        {
            float a = math.atan2(dir.y, dir.x) * (4f / math.PI);   // 45 deg bins
            a -= 4f * math.floor(a * 0.25f);                        // [0, 4): mod 180 deg
            int i0 = math.min((int)math.floor(a), 3);
            float f = a - i0;
            int i1 = (i0 + 1) & 3;
            float n = 0f;
            if (conf > 0f)
            {
                float n0 = Aniso(p, i0, along, across, off), n1 = Aniso(p, i1, along, across, off);
                n = ((1f - f) * n0 + f * n1) / math.sqrt((1f - f) * (1f - f) + f * f);
            }
            if (conf >= 1f) return n;
            float ni = noise.snoise(p / across + off + new float2(-53.9f, 27.1f));
            return (conf * n + (1f - conf) * ni) / math.sqrt(conf * conf + (1f - conf) * (1f - conf));
        }

        /// Alpine ridges and side valleys (voxels): sharp crests on the noise's
        /// zero lines, rounded valleys between, running down the regional slope.
        public static float Ridges(in Offsets o, float2 p, float2 dir, float conf, float slope)
        {
            float steep = Noise.SmoothStep(RIDGE_SLOPE_FROM, RIDGE_SLOPE_FULL, slope);
            if (steep <= 0f) return 0f;
            float n = Oriented(p, dir, conf, RIDGE_LONG, RIDGE_WIDE, o.strata + new float2(11.3f, 71.9f));
            return steep * RIDGE_AMP * (RIDGE_MEAN - math.abs(n));
        }

        /// How much the canyonland bench lowers a column whose height is h:
        /// the rock layer holding the top keeps a flat tread at its base for
        /// 1 - BENCH_RISER of its thickness, then a riser up to the next layer.
        public static float BenchCut(in TerrainSpec spec, in Offsets o, float2 p, float h)
        {
            float gl = spec.seaLevelY + h + DetailField.LayerWave(in o, p);
            int k = DetailField.LayerIndex(spec.seed, gl);
            float b0 = DetailField.LayerBoundary(spec.seed, k), span = DetailField.LayerBoundary(spec.seed, k + 1) - b0;
            float f = (gl - b0) / span;
            float s = f < 1f - BENCH_RISER ? 0f : (f - (1f - BENCH_RISER)) / BENCH_RISER;
            return span * (f - s);
        }

        /// The slot canyon's depth at p (voxels, >= 0): a flat floor, steep walls.
        public static float SlotDepth(in Offsets o, float2 p, float2 dir, float conf)
        {
            float patch = Noise.SmoothStep(-0.1f, 0.3f, noise.snoise(p / SLOT_PATCH + o.strata + new float2(-63.1f, 8.3f)));
            if (patch <= 0f) return 0f;
            float n = Oriented(p, dir, conf, SLOT_LONG, SLOT_WIDE, o.strata + new float2(29.7f, -45.1f));
            float u = math.abs(n) / SLOT_N;
            if (u >= 1f) return 0f;
            return SLOT_DEPTH * patch * (1f - Noise.SmoothStep(1f - SLOT_WALL, 1f, u));
        }
    }
}
