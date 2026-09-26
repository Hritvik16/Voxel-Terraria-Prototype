// Assets/TerrainGen/WorldMap.cs
//
// §2.4 WORLD MAP FIRST. Built once per world from the seed, before any voxel:
// the macro landform (organic island envelope, mountain ranges, the dominant
// peak, plateaus, an escarpment, valleys) at mapCellVoxels resolution, plus a
// roughness channel that scales S's fine detail.
//
// v2 (2026-09-25): the analytic field is followed by Hydrology.Run -- lakes
// filled to their spill level, rivers drained to the sea, light erosion -- the
// non-local features the map exists for (§2.4). It adds two channels: Water (a
// still-water level per cell, or Hydrology.NO_WATER) and Flow (catchment).
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
        /// Still-water level per cell (voxels rel. sea) or Hydrology.NO_WATER.
        /// OPTIONAL: null means a dry map (every job that predates map v2).
        public float* water;
        public int cells;
        public float cellVoxels;
    }

    public sealed class WorldMap : IDisposable
    {
        public readonly TerrainSpec Spec;
        public readonly float[] Height;
        public readonly float[] Rough;
        public readonly float[] Water;
        public readonly float[] Flow;
        /// Hydrology.DRY / LAKE / RIVER per cell.
        public readonly byte[] WaterClass;
        public readonly float2 PeakPosition;   // voxels
        public readonly Hydrology.Stats Hydro;
        public int BeaconCount { get; private set; }
        private GCHandle _hH, _hR, _hW;

        public WorldMap(TerrainSpec spec, float[] height, float[] rough, float2 peak,
                        float[] water = null, float[] flow = null, Hydrology.Stats hydro = default, byte[] waterClass = null)
        {
            Spec = spec; Height = height; Rough = rough; PeakPosition = peak;
            Water = water ?? Filled(height.Length, Hydrology.NO_WATER);
            Flow = flow ?? new float[height.Length];
            WaterClass = waterClass ?? new byte[height.Length];
            Hydro = hydro;
            _hH = GCHandle.Alloc(Height, GCHandleType.Pinned);
            _hR = GCHandle.Alloc(Rough, GCHandleType.Pinned);
            _hW = GCHandle.Alloc(Water, GCHandleType.Pinned);
        }

        private static float[] Filled(int n, float v) { var a = new float[n]; for (int i = 0; i < n; i++) a[i] = v; return a; }

        public unsafe MapView View => new MapView
        {
            height = (float*)_hH.AddrOfPinnedObject(),
            rough = (float*)_hR.AddrOfPinnedObject(),
            water = (float*)_hW.AddrOfPinnedObject(),
            cells = Spec.mapCells,
            cellVoxels = Spec.mapCellVoxels,
        };

        public void Dispose()
        {
            if (_hH.IsAllocated) _hH.Free();
            if (_hR.IsAllocated) _hR.Free();
            if (_hW.IsAllocated) _hW.Free();
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
            int beacons = Beacons(in spec, in o, h, r, peak);
            RoundSummits(in spec, in o, h);
            TreeCanopy(in spec, in o, h);
            var water = new float[n * n];
            var flow = new float[n * n];
            var cls = new byte[n * n];
            var hydro = Hydrology.Run(n, h, r, water, flow, cls);
            // The caldera is a depression, so the flood filled it with WATER to
            // its rim; it holds LAVA instead, at its own level (Stamps.Column
            // reports the kind per column). Drop the flood's water there -- the
            // WHOLE caldera lake, found by its level: with the rim rounded, that
            // lake can reach past any fixed radius, and cutting it at a radius
            // left a ring of water "leaking" into the dry caldera.
            float2 vsite = Stamps.VolcanoSite(in spec, in o);
            int vcx = math.clamp((int)(vsite.x / spec.mapCellVoxels), 0, n - 1), vcz = math.clamp((int)(vsite.y / spec.mapCellVoxels), 0, n - 1);
            float calLevel = water[vcz * n + vcx];
            for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                float2 p = new float2((x + 0.5f) * spec.mapCellVoxels, (z + 0.5f) * spec.mapCellVoxels);
                float2 q = p - vsite;
                float d2 = q.x * q.x + q.y * q.y;
                int i = z * n + x;
                bool calderaLake = calLevel != Hydrology.NO_WATER && water[i] == calLevel && d2 < 4f * Stamps.CALDERA_R * Stamps.CALDERA_R;
                if (d2 < Stamps.CALDERA_R * Stamps.CALDERA_R * 1.1f || calderaLake) { water[i] = Hydrology.NO_WATER; cls[i] = Hydrology.DRY; }
            }
            return new WorldMap(spec, h, r, peak, water, flow, hydro, cls) { BeaconCount = beacons };
        }

        // ------------------------------------------------------------------
        // BEACONS (§2.5 rule 1, 2026-09-25): "always a landmark on the horizon".
        // The jittered landmarks below give only ~21 qualifying summits: most
        // are outranked by the ranges or by a neighbour. This pass GUARANTEES a
        // candidate per 600 m cell of land: at the cell's highest point it
        // raises a landform to BEACON_RISE above the highest ground within
        // 384 m, so it is a local summit standing >= 60 m above its
        // surroundings by construction (the landmark metric's own definition).
        // Singleton areas (volcano, tree, geyser basin) are skipped.
        //
        // SHAPES (owner, 2026-09-25: "I don't like the tall cone shaped things
        // ... too unnatural ... not even sure how they'd be fun for traversal").
        // The first version raised 50-70 m wide spikes and columns 90-140 m
        // tall. Beacons are now landforms you can climb: a broad DOME (an
        // inselberg; radius set so its steepest slope stays under
        // DOME_MAX_SLOPE) or a terraced MESA with one walkable ramp to a flat
        // lookout top. Nothing is taller than MAX_ASPECT x its footprint radius.
        // ------------------------------------------------------------------
        public const int BEACON_CELLS = 75;            // map cells per beacon cell (600 m)
        public const float BEACON_RISE = 700f;         // voxels (70 m) above the local max, + up to 40 m
        /// Steepest slope of a dome, rise over run. (1 - x^2)^1.5 peaks at
        /// 1.5 h / r, so r >= 1.5 h / DOME_MAX_SLOPE. 0.45 is ~24 degrees.
        public const float DOME_MAX_SLOPE = 0.45f;
        /// No landform here is taller than this fraction of its footprint radius.
        public const float MAX_ASPECT = 0.35f;

        /// Dome profile in [0, 1]: 1 at the centre, 0 at radius r. Rounded top,
        /// smooth toe, steepest slope 1.5 h / r at x = 0.71.
        public static float DomeT(float d, float r)
        {
            float x = math.saturate(d / r);
            float u = 1f - x * x;
            return u * math.sqrt(u);
        }

        /// Terraced mesa profile in [0, 1]: 1 on the flat top (d < R), three
        /// ledges down a cliff band of width B, and -- inside a 36 degree sector
        /// around `rampDir` -- a straight ramp of run L instead of the cliff, so
        /// the top is reachable on foot. `toP` is p - centre (not normalised).
        public static float MesaT(float2 toP, float d, float R, float B, float L, float2 rampDir)
        {
            if (d <= R) return 1f;
            float u = math.saturate((R + B - d) / B);          // 0 at the foot, 1 at the top edge
            float s3 = u * 3f, k = math.floor(s3);
            float cliff = math.min(1f, (k + Noise.SmoothStep(0.7f, 1f, s3 - k)) / 3f);
            float along = d > 1e-3f ? math.dot(toP / d, rampDir) : 1f;
            float w = Noise.SmoothStep(0.951f, 0.988f, along);  // cos 18 deg .. cos 9 deg
            float ramp = math.saturate(1f - (d - R) / L);
            return math.max(cliff, w * ramp);
        }

        /// A unit vector from the hash (no trig, like Stamps.HashDir).
        private static float2 HashUnit(int x, int z, uint seed)
        {
            float2 v = new float2(Noise.Hash01(x, 11, z, seed) - 0.5f, Noise.Hash01(x, 12, z, seed) - 0.5f);
            float l = math.sqrt(v.x * v.x + v.y * v.y);
            return l > 1e-3f ? v / l : new float2(1f, 0f);
        }

        /// IRREGULAR FOOTPRINT (2026-09-25). The first domes were perfect circles
        /// with one profile, and the island read as bubble wrap. A hill is now
        /// stretched along a hashed axis `u` and its outline wanders +-`lobe`
        /// with a low-frequency noise of the DIRECTION, so it has spurs and bays.
        /// Returns the normalised distance: 0 at the centre, 1 on the outline.
        public const float HILL_LOBE = 0.3f;

        public static float IrregularX(float2 q, float r, float2 u, float stretch, float2 noiseOff)
        {
            float along = math.dot(q, u) / stretch, side = q.x * -u.y + q.y * u.x;
            float dl = math.sqrt(along * along + side * side);
            if (dl < 1e-3f) return 0f;
            float2 dn = new float2(along, side) / dl;
            float rr = r * (1f + HILL_LOBE * noise.snoise(dn * 1.2f + noiseOff));
            return dl / rr;
        }

        public static int Beacons(in TerrainSpec spec, in Offsets o, float[] h, float[] r, float2 peak)
        {
            int n = spec.mapCells, count = 0;
            int bc = (n + BEACON_CELLS - 1) / BEACON_CELLS;
            for (int bz = 0; bz < bc; bz++)
            for (int bx = 0; bx < bc; bx++)
            {
                // Highest land cell in the beacon cell's inner 60%.
                int best = -1; float bestH = 200f;             // >= 20 m above sea
                int x0 = bx * BEACON_CELLS + BEACON_CELLS / 5, z0 = bz * BEACON_CELLS + BEACON_CELLS / 5;
                int span = BEACON_CELLS * 3 / 5;
                for (int z = z0; z < math.min(n, z0 + span); z++)
                for (int x = x0; x < math.min(n, x0 + span); x++)
                    if (h[z * n + x] > bestH) { bestH = h[z * n + x]; best = z * n + x; }
                if (best < 0) continue;
                int cx = best % n, cz = best / n;
                float2 p = new float2((cx + 0.5f) * spec.mapCellVoxels, (cz + 0.5f) * spec.mapCellVoxels);
                // Mountains are their own landmarks, and the dominant peak must stay
                // the dominant peak (§2.2): no beacon on high ground or near it.
                if (bestH > 0.55f * spec.peakHeight || math.distance(p, peak) < 12000f) continue;
                if (math.distance(p, Stamps.VolcanoSite(in spec, in o)) < Stamps.VOLCANO_BASE_R ||
                    math.distance(p, Stamps.TreeSite(in spec, in o)) < Stamps.TREE_CANOPY_R + 1500f ||
                    math.distance(p, Stamps.GeyserSite(in spec, in o)) < 2500f) continue;
                // Highest ground within 384 m (48 map cells).
                float localMax = bestH;
                for (int z = math.max(0, cz - 48); z <= math.min(n - 1, cz + 48); z++)
                for (int x = math.max(0, cx - 48); x <= math.min(n - 1, cx + 48); x++)
                    localMax = math.max(localMax, h[z * n + x]);
                float top = math.min(localMax + BEACON_RISE + 400f * Noise.Hash01(bx, 7, bz, o.seed + 301u),
                                     0.85f * spec.peakHeight);    // never rivals the dominant summit
                if (top < localMax + 600f) continue;             // could not stand out: not a beacon
                float rise = top - bestH;                        // above the site's own ground
                // A beacon that must climb > 250 m to clear its neighbourhood sits
                // beside a mountain, which is already the landmark.
                if (rise > 2500f) continue;
                bool mesa = Noise.Hash01(bx, 8, bz, o.seed + 302u) < 0.35f;
                float2 rampDir = HashUnit(bx, bz, o.seed + 304u);
                float R = 900f + 600f * Noise.Hash01(bx, 9, bz, o.seed + 303u);   // mesa top 90-150 m
                float B = math.max(450f, rise * 0.8f);                             // terraced cliff band
                float L = rise * 3.5f;                                             // ramp run: ~16 degrees
                // Dome radius from the slope limit, divided by the narrowest lobe
                // so even the steepest side of the irregular outline respects it.
                float rad = math.max(2200f, 1.5f * rise / DOME_MAX_SLOPE) / (1f - HILL_LOBE);
                float stretch = 1f + 0.7f * Noise.Hash01(bx, 10, bz, o.seed + 305u);
                float2 lobeOff = new float2(Noise.Hash01(bx, 13, bz, o.seed + 306u), Noise.Hash01(bx, 14, bz, o.seed + 307u)) * 100f;
                float extent = mesa ? R + math.max(B, L) : rad * stretch * (1f + HILL_LOBE);
                int rc = (int)math.ceil(extent / spec.mapCellVoxels) + 1;
                for (int z = math.max(0, cz - rc); z <= math.min(n - 1, cz + rc); z++)
                for (int x = math.max(0, cx - rc); x <= math.min(n - 1, cx + rc); x++)
                {
                    float2 q = new float2((x + 0.5f) * spec.mapCellVoxels, (z + 0.5f) * spec.mapCellVoxels) - p;
                    float d = math.sqrt(q.x * q.x + q.y * q.y);
                    float t = mesa ? MesaT(q, d, R, B, L, rampDir) : DomeT(IrregularX(q, rad, rampDir, stretch, lobeOff), 1f);
                    if (t <= 0f) continue;
                    int i = z * n + x;
                    float nh = h[i] + (top - h[i]) * t;
                    if (nh > h[i]) { h[i] = nh; r[i] = math.max(r[i], mesa ? 0.8f : 0.4f); }
                }
                count++;
            }
            return count;
        }

        // ------------------------------------------------------------------
        // SUMMIT CURVATURE LIMIT (2026-09-25). The ridged ranges make horns
        // wherever the noise touches zero at a POINT rather than along a line:
        // an isolated maximum no crest rounding removes (StampTests.NoNeedles:
        // seed 42 had summits falling 45-75 m within 40 m on every side). A
        // grey-scale OPENING with a paraboloid structuring element removes
        // every peak sharper than the paraboloid and leaves broader shapes --
        // ranges, domes, mesa tops, the escarpment -- exactly as they were. It
        // only ever lowers terrain. Separable (the paraboloid k(dx^2 + dz^2) over
        // a square window), so two 1-D passes per stage. The singletons keep
        // their deliberate edges (the basin terrace, the caldera and its rim; the
        // tree canopy is added afterwards, TreeCanopy); the volcano's flanks are
        // rounded like any slope.
        // ------------------------------------------------------------------
        /// Voxels of height per map cell squared: at 40 m (5 cells) the rounded
        /// summit has fallen 7 x 25 = 175 voxels (17.5 m).
        public const float SUMMIT_CURVATURE = 7f;
        public const int SUMMIT_WINDOW = 6;   // cells each side (48 m)

        public static void RoundSummits(in TerrainSpec spec, in Offsets o, float[] h)
        {
            int n = spec.mapCells, W = SUMMIT_WINDOW;
            float k = SUMMIT_CURVATURE;
            var a = new float[n * n];
            var b = new float[n * n];
            // Erosion: e(x) = min_y h(y) + k |x - y|^2, rows then columns.
            for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                float m = float.MaxValue;
                for (int d = -W; d <= W; d++) { int xx = x + d; if (xx < 0 || xx >= n) continue; m = math.min(m, h[z * n + xx] + k * d * d); }
                a[z * n + x] = m;
            }
            for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                float m = float.MaxValue;
                for (int d = -W; d <= W; d++) { int zz = z + d; if (zz < 0 || zz >= n) continue; m = math.min(m, a[zz * n + x] + k * d * d); }
                b[z * n + x] = m;
            }
            // Dilation: o(x) = max_y e(y) - k |x - y|^2.
            for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                float m = float.MinValue;
                for (int d = -W; d <= W; d++) { int xx = x + d; if (xx < 0 || xx >= n) continue; m = math.max(m, b[z * n + xx] - k * d * d); }
                a[z * n + x] = m;
            }
            float2 vs = Stamps.VolcanoSite(in spec, in o), gs = Stamps.GeyserSite(in spec, in o);
            for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                float m = float.MinValue;
                for (int d = -W; d <= W; d++) { int zz = z + d; if (zz < 0 || zz >= n) continue; m = math.max(m, a[zz * n + x] - k * d * d); }
                int i = z * n + x;
                if (m >= h[i]) continue;
                float2 p = new float2((x + 0.5f) * spec.mapCellVoxels, (z + 0.5f) * spec.mapCellVoxels);
                // The caldera and its rim keep their edge: a curvature limit cuts a
                // crest that sharp down by 50 m and the wall stops containing the
                // lava (StampTests.Volcano). Nothing pokes through the rim any more
                // -- MacroApply lets the cone replace the ranges near its summit.
                if (math.distance(p, vs) < Stamps.CALDERA_R * 1.15f || math.distance(p, gs) < 1600f) continue;
                h[i] = m;
            }
        }

        /// The world tree's canopy (map level): the leaf dome's top over the
        /// ground under it. Runs after RoundSummits so it sits on rounded ground.
        public static void TreeCanopy(in TerrainSpec spec, in Offsets o, float[] h)
        {
            int n = spec.mapCells;
            float2 ts = Stamps.TreeSite(in spec, in o);
            int rc = (int)math.ceil(Stamps.TREE_CANOPY_R / spec.mapCellVoxels) + 1;
            int cx = (int)(ts.x / spec.mapCellVoxels), cz = (int)(ts.y / spec.mapCellVoxels);
            for (int z = math.max(0, cz - rc); z <= math.min(n - 1, cz + rc); z++)
            for (int x = math.max(0, cx - rc); x <= math.min(n - 1, cx + rc); x++)
            {
                float2 p = new float2((x + 0.5f) * spec.mapCellVoxels, (z + 0.5f) * spec.mapCellVoxels);
                float td = math.distance(p, ts);
                if (td < Stamps.TREE_CANOPY_R) h[z * n + x] = math.max(h[z * n + x], 8f) + Stamps.TreeCanopyTop(td);
            }
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
        /// searched, which covers any footprint under 7,200 voxels (the jitter
        /// keeps a centre >= 1,200 from its cell's edge). `edge` is 1 on a cliff
        /// band, for the roughness channel.
        ///
        /// Shapes (2026-09-25, replacing SPIRE and the narrow BUTTE -- see
        /// Beacons for the owner's note): DOME, a broad rounded hill (radius
        /// 180-300 m, elongated and lobed, gentle slopes); MESA, a flat lookout
        /// top (radius 80-160 m) on a terraced cliff band with one walkable
        /// ramp; KNOLL, a low rounded hill. Height <= MAX_ASPECT x footprint radius.
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
                if (Noise.Hash01(gx, 0, gz, o.seed + 101u) > 0.60f) continue;       // 60% of cells hold one
                float2 c = new float2(gx + 0.2f + 0.6f * Noise.Hash01(gx, 1, gz, o.seed + 102u),
                                      gz + 0.2f + 0.6f * Noise.Hash01(gx, 2, gz, o.seed + 103u)) * LANDMARK_CELL;
                float2 d = p - c;
                float dist = math.sqrt(d.x * d.x + d.y * d.y);
                float kind = Noise.Hash01(gx, 3, gz, o.seed + 104u);
                float size = Noise.Hash01(gx, 4, gz, o.seed + 105u);
                float2 u = HashUnit(gx, gz, o.seed + 107u);
                float stretch = 1f + 0.7f * Noise.Hash01(gx, 6, gz, o.seed + 108u);
                float2 lobeOff = new float2(Noise.Hash01(gx, 7, gz, o.seed + 109u), Noise.Hash01(gx, 8, gz, o.seed + 110u)) * 100f;
                if (kind < 0.30f)
                {
                    // DOME: radius 1,800-3,000 voxels (x stretch, +-lobe), height
                    // 0.20-0.27 x radius. Footprint <= 3,000 x 1.7 x 1.3 < 7,200.
                    float r = 1800f + 1200f * size, h = r * (0.20f + 0.07f * Noise.Hash01(gx, 5, gz, o.seed + 106u));
                    float x = IrregularX(d, r, u, stretch, lobeOff);
                    if (x < 1f) add = math.max(add, h * DomeT(x, 1f));
                }
                else if (kind < 0.60f)
                {
                    // MESA: top radius 800-1,600, 30-70 m, 40 m terraced band, ramp.
                    float R = 800f + 800f * size, h = 300f + 400f * Noise.Hash01(gx, 5, gz, o.seed + 106u);
                    const float B = 400f;
                    float L = h * 3.5f;
                    if (dist < R + math.max(B, L))
                    {
                        float t = MesaT(d, dist, R, B, L, u);
                        add = math.max(add, h * t);
                        edge = math.max(edge, dist > R && dist < R + B ? 1f : 0f);
                    }
                }
                else
                {
                    // KNOLL: radius 600-1,000 (irregular), height 0.2-0.3 x radius, (1 - x^2)^2.
                    float r = 600f + 400f * size, h = r * (0.2f + 0.1f * Noise.Hash01(gx, 5, gz, o.seed + 106u));
                    float x = math.saturate(IrregularX(d, r, u, stretch, lobeOff)), w = 1f - x * x;
                    add = math.max(add, h * w * w);
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

            // Meso landmarks (§2.5): domes, mesas, knolls on a jittered grid, so
            // lowlands and plateaus get something on every horizon too.
            hgt += Landmarks(in o, p, math.saturate(coast * 4f), out float lmEdge);
            // Map-level stamps: volcano, world tree, meteor craters.
            hgt = Stamps.MacroApply(in spec, in o, p, hgt, inland, ref lmEdge);

            // Beaches: flatten the last few metres at the waterline.
            float shore = math.saturate(coast * 12f);
            height = math.max(8f, hgt) * shore + 8f * (1f - shore) - 10f * (1f - inland);

            float edge = pm * (1f - pm) * 4f;
            // Detail scale. Lower on the ranges than first authored: the first
            // far-band preview read as noisy "rock teeth" at 0.8 m blocks.
            // On the volcano the cone replaces the ranges (Stamps.MacroApply), so
            // their roughness goes too, with the same weight: a mesa's cliff flag
            // or the range mask left over the rim put enough fine detail on the
            // caldera wall to dip it below the lava (StampTests.Volcano).
            float2 vq = p - Stamps.VolcanoSite(in spec, in o);
            float coneW = math.saturate((Stamps.VOLCANO_BASE_R - math.sqrt(vq.x * vq.x + vq.y * vq.y)) / (0.5f * Stamps.VOLCANO_BASE_R));
            rough = 0.35f + (1f - coneW) * (1.0f * rangeMask + 0.7f * pd + 0.8f * edge + 0.5f * lmEdge);
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
