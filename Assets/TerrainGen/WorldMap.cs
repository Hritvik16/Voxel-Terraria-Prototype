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
using System.Collections.Generic;
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
        /// v3 (§13): the region layout and, per cell, the nearest region's
        /// archetype (Regions.*; 255 = sea). Null / empty on a v2 map.
        public Regions.Layout RegionLayout { get; private set; }
        public byte[] Region { get; private set; }
        /// v4 (B1): the per-cell world record. Null below v4.
        public WorldRecord Record { get; private set; }
        /// T6 inselbergs that raised at least one erosion-grid cell (v3: ~15-40
        /// per seed; v4: 0, owner Q7 (a)).
        public int InselbergCount { get; private set; }
        /// Map v4 (B4): the seeded easy-area sites; null below v4. The flagged
        /// cells and the area list are in Record (EasyAreas.Mark).
        public EasyAreas.Site[] EasySites { get; private set; }
        /// Map v4 (B5): the signature features drawn from the pool, in slot
        /// order (Features.cs); null below v4. The record flags their cells.
        public Feature[] SignatureFeatures { get; private set; }
        /// Map v4 (B8): the routes between the easy areas (Routes.cs); null
        /// below v4. The map is already eased along them; the record flags them.
        public Route[] Routes { get; private set; }
        /// Routes.LastBuild for this map (diagnostics: links, cut and fill cells).
        public string RouteBuild { get; private set; }
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
        public static WorldMap Build(TerrainSpec spec) =>
            spec.mapVersion >= 4 ? BuildV4(spec) : spec.mapVersion >= 3 ? BuildV3(spec) : BuildV2(spec);

        // ------------------------------------------------------------------
        // v4 (surface rebuild): v3's recipe with the B steps switched on by
        // spec.mapVersion >= 4 inside BuildV3, plus the world record.
        //   B1 (owner 2026-10-03): the record; larger biomes, which are not in
        //      the map at all (Offsets.For(spec).biomeScale in ShapeField.Biome).
        //   B2 (plan rule 2): region archetypes dealt by landform class to hit
        //      spec.GentleShare of the land (Regions.AssignByFlatness); no T6
        //      inselbergs (owner Q7 (a)).
        //   B3 (plan rule 5): erosion as a light valley pass.
        //   B4 (plan rule 3): easy areas seeded before erosion (EasyAreas.cs),
        //      flagged in the record with their list.
        //   B5 (plan rule 6): no pinned peak or volcano; signature features
        //      drawn from the pool and placed (Features.cs), listed and flagged
        //      in the record.
        //   B8 (plan rule 9): routes between the easy areas, found on a first
        //      record (its easy areas, danger and bands), eased into the map
        //      (Routes.cs); the record is then built again on the eased map,
        //      keeping the first record's easy areas, and flags the corridors.
        // A v3 spec takes none of these branches and stays byte-identical.
        // ------------------------------------------------------------------
        private static WorldMap BuildV4(TerrainSpec spec)
        {
            // The record's biome plane is a function of the spec alone (~7 s in
            // the Editor's Mono, measured 2026-10-03), so it runs on a worker
            // while BuildV3 does: the array is the task's own until Result, and
            // the values do not depend on the thread. What stays on the
            // critical path is the two distance transforms.
            var biomes = System.Threading.Tasks.Task.Run(() => WorldRecord.BiomePlane(spec));
            var map = BuildV3(spec);
            map.Record = WorldRecord.Build(map, biomes.Result);
            map.Routes = TerrainGen.Routes.Build(map, map.Record);
            map.RouteBuild = TerrainGen.Routes.LastBuild;
            if (map.Routes != null) map.Record = WorldRecord.Build(map, biomes.Result, map.Record);
            return map;
        }

        // ------------------------------------------------------------------
        // v3 (§13, T1 + T2): regions -> starting shapes -> erosion -> fine map.
        // ------------------------------------------------------------------
        public const int EROSION_ITERATIONS = 240;
        /// B3 (plan rule 5): on v4 erosion is a light valley pass -- a quarter of
        /// the iterations, and the land starts at EROSION_START_V4 of its target
        /// instead of 0.2, so it rises 40% of its height during the pass instead
        /// of 80% (half the uplift). Rivers still cut valleys and channels; the
        /// slopes between them are no longer dissected by uplift.
        public const int EROSION_ITERATIONS_V4 = 60;
        public const float EROSION_START_V4 = 0.6f;
        public const float PEAK_ALLOWANCE = 1.12f;
        public static Erosion.Stats LastErosion;

        private static WorldMap BuildV3(TerrainSpec spec)
        {
            int n = spec.mapCells, m = n / 2;
            float cm = spec.mapCellVoxels * 2f;
            var o = Offsets.For(in spec);   // v3: exactly For(seed); v4 adds the drawn volcano (B5)
            var L = Regions.Build(in spec, in o);
            // B5 (v4): the pool's draw; the volcano (its site is in o) and the
            // great peak are placed now, the rest after erosion.
            bool feat = spec.mapVersion >= 4;
            byte[] kinds = feat ? Features.Draw(in spec) : null;
            var feats = feat ? new List<Feature>() : null;
            float2 peak = feat ? Features.PlaceEarly(in spec, in o, L, kinds, feats) : PeakSite(spec, o);
            float2 vs = Stamps.VolcanoSite(in spec, in o);
            var w = new float[Regions.KINDS];
            bool inselbergs = spec.mapVersion < 4;   // B2: none on v4 (owner Q7 (a))
            var inselbergIds = new HashSet<int2>();

            // ---- starting shapes on the coarse (erosion) grid ----
            var hc = new float[m * m]; var ec = new float[m * m]; var tc = new float[m * m]; var lc = new bool[m * m];
            // B4 (v4): what EasyAreas.Pick reads -- gentle-class weight (the
            // wetland basin left out: it is shaped to hold lakes), and the
            // cells no easy area may cover (sea, coast, peak or volcano raised).
            bool easy = spec.mapVersion >= 4;
            var gw = easy ? new float[m * m] : null; var bad = easy ? new bool[m * m] : null;
            for (int z = 0; z < m; z++)
            for (int x = 0; x < m; x++)
            {
                int i = z * m + x;
                float2 p = new float2((x + 0.5f) * cm, (z + 0.5f) * cm);
                float coast = Coast(in spec, in o, p);
                if (coast <= 0f) { hc[i] = OceanFloor(in spec, coast); tc[i] = 1f; if (easy) bad[i] = true; continue; }
                Regions.Weights(L, in o, p, w);
                float H = 0f, E = 0f, T = 0f, lay = 0f;
                for (byte k = 0; k < Regions.KINDS; k++)
                {
                    if (w[k] <= 0f) continue;
                    var pr = Regions.P(k, spec.mapVersion);   // B3b: v4 region softening
                    H += w[k] * Regions.Shape(k, in spec, in o, p);
                    E += w[k] * pr.erodibility; T += w[k] * pr.talus; if (pr.layered) lay += w[k];
                }
                float shaped = H;   // B4: before the peak and the volcano raise it
                // The dominant peak (§2.2), broad, in its alpine region. Started
                // PEAK_ALLOWANCE above its target: erosion takes ~10% off a summit
                // (541 m of 600 at the tuned erodibility, TerrainGenTests.Targets).
                float2 dp = p - peak;
                float pd = math.saturate(1f - math.sqrt(dp.x * dp.x + dp.y * dp.y) / 10500f);
                H = math.max(H, pd * pd * (3f - 2f * pd) * (0.82f + 0.18f * Noise.Ridged(p / 7200f + o.ridge, 4)) * spec.peakHeight * PEAK_ALLOWANCE);
                // The volcano's cone everywhere it reaches (it is wider than a region
                // is apart from its neighbours, so a region blend would clip it).
                H = math.max(H, Stamps.VolcanoHeight(in spec, in o, p, out _, out _));
                if (feat)
                {
                    // B5: a drawn peak or volcano brings its own rock wherever it
                    // stands (the alpine / volcanic erodibility and talus that
                    // v3's pinned regions under them gave), and no cap-rock layers.
                    float2 vq = p - vs;
                    float wv = math.saturate((Stamps.VOLCANO_BASE_R - math.sqrt(vq.x * vq.x + vq.y * vq.y)) / (0.3f * Stamps.VOLCANO_BASE_R));
                    float wp = math.saturate(pd * 3f);
                    var ap = Regions.P(Regions.Alpine, spec.mapVersion); var vp = Regions.P(Regions.Volcanic, spec.mapVersion);
                    E = math.lerp(math.lerp(E, ap.erodibility, wp), vp.erodibility, wv);
                    T = math.lerp(math.lerp(T, ap.talus, wp), vp.talus, wv);
                    lay *= 1f - math.max(wp, wv);
                }
                if (easy)
                {
                    for (byte k = 0; k < Regions.KINDS; k++)
                        if (w[k] > 0f && Regions.ClassOf(k) == Regions.Gentle && k != Regions.Wetland) gw[i] += w[k];
                    // B5: only where the peak or the volcano raises the land 30 m+
                    // (their whole footprints left no room for the easy areas)
                    bad[i] = coast <= EasyAreas.COAST_MIN || H > shaped + (feat ? Features.BAD_RAISE : 0f);
                }
                // Island envelope: land rises from the shore; sea cliffs rise sharply.
                float env = math.lerp(math.saturate(coast * 5f), math.saturate(coast * 14f), w[Regions.CoastalCliffs]);
                // A little jitter so flats never drain in ruler-straight lines.
                hc[i] = math.max(4f, H * env + 20f * Noise.Fbm(p / 1500f + o.detail + 3f, 2));
                // T6: inselbergs everywhere outside the mountains -- hard rock, so
                // erosion cuts their flanks but does not level them. (Round 2,
                // 2026-09-26: flat regions alone gave 19 landmarks and 1.5% of
                // land seeing one in every direction within 3 km; the interior
                // needs its own. In the layered regions the fine map's terracing
                // turns the dome into a stepped butte.)
                float open = 1f - w[Regions.Alpine] - w[Regions.Volcanic];
                if (inselbergs && open > 0.3f && coast > 0.15f)
                {
                    float ib = Inselberg(in o, p, out float hard, out int2 ibCell);
                    if (ib > 0f)
                    {
                        hc[i] += ib * open; E *= math.lerp(1f, 0.1f, hard); T = math.max(T, math.lerp(T, 2.5f, hard));
                        inselbergIds.Add(ibCell);
                    }
                }
                ec[i] = E; tc[i] = T; lc[i] = lay > 0.5f;
            }
            // B5 (plan rule 6): canyons, mesas and crater lakes, placed before the
            // easy areas, which are then seeded outside every footprint.
            if (feat)
            {
                Features.PlaceLate(in spec, in o, m, cm, hc, gw, bad, kinds, feats);
                Features.MarkBad(feats, m, cm, bad);
            }
            // B4 (plan rule 3): seed the easy areas and press their starting relief.
            EasyAreas.Site[] easySites = null;
            if (easy)
            {
                easySites = EasyAreas.Pick(in spec, m, cm, hc, gw, bad);
                for (int z = 0; z < m; z++)
                for (int x = 0; x < m; x++)
                {
                    int i = z * m + x;
                    float t = EasyAreas.Weight(easySites, in o, new float2((x + 0.5f) * cm, (z + 0.5f) * cm), out int site);
                    if (t <= 0f) continue;
                    hc[i] = EasyAreas.Press(hc[i], easySites[site].level, t);
                    tc[i] = math.lerp(tc[i], EasyAreas.TALUS, t);
                    if (t > 0.5f) lc[i] = false;
                }
            }
            if (DumpErosionInputsTo != null) DumpErosionInputs(DumpErosionInputsTo + $"_{spec.seed}.bin", m, hc, ec, tc, lc);
            LastErosion = spec.mapVersion >= 4
                ? Erosion.Run(m, hc, ec, tc, lc, cm, EROSION_ITERATIONS_V4, EROSION_START_V4)
                : Erosion.Run(m, hc, ec, tc, lc, cm, EROSION_ITERATIONS);
            // B5: the features' heights on the eroded ground.
            if (feat) Features.Finish(in spec, m, cm, hc, feats);

            // ---- the fine map: upsample, then per-cell detail ----
            var h = new float[n * n]; var r = new float[n * n]; var reg = new byte[n * n];
            var lakeMin = new float[n * n];
            for (int i = 0; i < lakeMin.Length; i++) lakeMin[i] = Hydrology.LAKE_MIN_DEPTH;
            for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                int i = z * n + x;
                float2 p = new float2((x + 0.5f) * spec.mapCellVoxels, (z + 0.5f) * spec.mapCellVoxels);
                float coast = Coast(in spec, in o, p);
                if (coast <= 0f) { h[i] = OceanFloor(in spec, coast); r[i] = 0.25f; reg[i] = 255; continue; }
                // Bilinear from the coarse grid (cell centres).
                float gx = (x + 0.5f) * 0.5f - 0.5f, gz = (z + 0.5f) * 0.5f - 0.5f;
                int x0 = math.clamp((int)math.floor(gx), 0, m - 2), z0 = math.clamp((int)math.floor(gz), 0, m - 2);
                float fx = math.saturate(gx - x0), fz = math.saturate(gz - z0);
                float a = math.lerp(hc[z0 * m + x0], hc[z0 * m + x0 + 1], fx);
                float b = math.lerp(hc[(z0 + 1) * m + x0], hc[(z0 + 1) * m + x0 + 1], fx);
                float hh = math.lerp(a, b, fz);
                int near = Regions.Weights(L, in o, p, w);
                reg[i] = L.kind[near];
                float rough = 0f, layered = 0f, minLake = 0f;
                for (byte k = 0; k < Regions.KINDS; k++)
                {
                    if (w[k] <= 0f) continue;
                    var pr = Regions.P(k);
                    rough += w[k] * pr.rough; minLake += w[k] * pr.minLake;
                    if (pr.layered) layered += w[k];
                }
                // B4: an easy area is smooth -- a wetland's detail, no benches, no dunes.
                float et = easy ? EasyAreas.Weight(easySites, in o, p, out _) : 0f;
                if (et > 0f)
                {
                    rough = math.lerp(rough, EasyAreas.ROUGH, et); layered *= 1f - et;
                    minLake = math.lerp(minLake, math.max(minLake, EasyAreas.MIN_LAKE), et);
                }
                // Canyonlands and plateaus: benches and cliff bands.
                if (layered > 0f && hh > 20f) hh = math.lerp(hh, Regions.Terrace(hh), layered);
                lakeMin[i] = minLake;
                if (w[Regions.Dunes] > 0f) hh += (et > 0f ? w[Regions.Dunes] * (1f - et) : w[Regions.Dunes]) * DuneField(in o, p);
                // Beaches: the last metres at the waterline (not under sea cliffs).
                float shore = math.saturate(coast * 12f);
                float beach = math.max(8f, hh) * shore + 8f * (1f - shore) - 10f * (1f - math.saturate(coast * 5f));
                hh = math.lerp(beach, hh, w[Regions.CoastalCliffs]);
                // The caldera, fresh (erosion would have worn its rim).
                float vd = math.distance(p, vs);
                if (vd < Stamps.CALDERA_R * 1.4f)
                {
                    float v = Stamps.VolcanoHeight(in spec, in o, p, out _, out _);
                    float t = math.saturate((Stamps.CALDERA_R * 1.4f - vd) / (Stamps.CALDERA_R * 0.25f));
                    hh = math.lerp(hh, v, t);
                }
                hh = Stamps.CraterApply(in spec, in o, p, hh, math.saturate(coast * 5f));
                if (feat) hh = Features.Apply(feats, p, hh, Noise.SmoothStep(0f, Features.BANK_SHORE, coast) * (1f - et));   // B5, fresh like the craters
                h[i] = hh; r[i] = rough;
            }
            // Rock shows where the eroded land is steep.
            for (int z = 1; z < n - 1; z++)
            for (int x = 1; x < n - 1; x++)
            {
                int i = z * n + x;
                float gxs = (h[i + 1] - h[i - 1]) * 0.5f, gzs = (h[i + n] - h[i - n]) * 0.5f;
                float slope = math.sqrt(gxs * gxs + gzs * gzs) / spec.mapCellVoxels;
                r[i] += 0.5f * Noise.SmoothStep(0.6f, 1.4f, slope);
            }
            TrimSpikes(in spec, in o, h);
            var water = new float[n * n];
            var flow = new float[n * n];
            var cls = new byte[n * n];
            var hydro = Hydrology.Run(n, h, r, water, flow, cls, lakeMin);
            ClearCalderaLake(in spec, in o, water, cls);
            if (feat && peak.x <= Features.NO_SITE_XZ)
            {
                // no great peak drawn: PeakPosition (poses, the minimap) is the highest cell
                int bi = 0;
                for (int i = 1; i < h.Length; i++) if (h[i] > h[bi]) bi = i;
                peak = new float2((bi % n + 0.5f) * spec.mapCellVoxels, (bi / n + 0.5f) * spec.mapCellVoxels);
            }
            return new WorldMap(spec, h, r, peak, water, flow, hydro, cls)
            {
                RegionLayout = L, Region = reg, InselbergCount = inselbergIds.Count, EasySites = easySites, SignatureFeatures = feats?.ToArray(),
            };
        }

        /// TUNING SEAM (tools/erosion-lab.c): when set, BuildV3 writes the erosion
        /// grid's inputs to <path>_<seed>.bin -- int32 n, then n*n float32 each
        /// of target, erodibility, talus, then n*n bytes of the layered flag.
        public static string DumpErosionInputsTo;

        private static void DumpErosionInputs(string path, int m, float[] hc, float[] ec, float[] tc, bool[] lc)
        {
            using var f = new System.IO.BinaryWriter(System.IO.File.Create(path));
            f.Write(m);
            foreach (float v in hc) f.Write(v);
            foreach (float v in ec) f.Write(v);
            foreach (float v in tc) f.Write(v);
            foreach (bool v in lc) f.Write((byte)(v ? 1 : 0));
        }

        // ------------------------------------------------------------------
        // T6 INSELBERGS (2026-09-26; owner kept §2.5's "a landmark in every
        // direction" and asked for natural ways to meet it). A lone massif of
        // hard rock rising from flat land -- Uluru, Sugarloaf -- is the natural
        // landmark flat country has. One candidate per INSELBERG_CELL (1.1 km),
        // 85% occupied, jittered; a steep-walled, round-topped dome 300-500 m
        // across and 90-160 m tall (erosion takes ~20-30 m off; the landmark
        // test wants >= 60 m over the 1 km mean); broad enough for the needle
        // guard. `hard` (0..1) marks its rock for the erosion.
        // ------------------------------------------------------------------
        public const float INSELBERG_CELL = 11000f;

        public static float Inselberg(in Offsets o, float2 p, out float hard) => Inselberg(in o, p, out hard, out _);

        /// `cell`: the INSELBERG_CELL holding the tallest dome at p (its id).
        public static float Inselberg(in Offsets o, float2 p, out float hard, out int2 cell)
        {
            hard = 0f; cell = int2.zero;
            int cx = (int)math.floor(p.x / INSELBERG_CELL), cz = (int)math.floor(p.y / INSELBERG_CELL);
            float best = 0f;
            for (int j = -1; j <= 1; j++)
            for (int i = -1; i <= 1; i++)
            {
                int gx = cx + i, gz = cz + j;
                if (Noise.Hash01(gx, 60, gz, o.seed + 701u) > 0.85f) continue;
                float2 c = new float2(gx + 0.25f + 0.5f * Noise.Hash01(gx, 61, gz, o.seed + 702u),
                                      gz + 0.25f + 0.5f * Noise.Hash01(gx, 62, gz, o.seed + 703u)) * INSELBERG_CELL;
                float R = 1500f + 1000f * Noise.Hash01(gx, 63, gz, o.seed + 704u);
                float H = 900f + 700f * Noise.Hash01(gx, 64, gz, o.seed + 705u);
                float2 u = HashUnit(gx, gz, o.seed + 706u);
                float2 lobeOff = new float2(Noise.Hash01(gx, 65, gz, o.seed + 707u), Noise.Hash01(gx, 66, gz, o.seed + 708u)) * 100f;
                float x = IrregularX(p - c, R, u, 1f + 0.5f * Noise.Hash01(gx, 67, gz, o.seed + 709u), lobeOff);
                if (x >= 1f) continue;
                // Steep walls, rounded top: sqrt(1 - x^4).
                float x2 = x * x;
                float t = math.sqrt(math.max(0f, 1f - x2 * x2));
                if (H * t > best) { best = H * t; hard = Noise.SmoothStep(1f, 0.85f, x); cell = new int2(gx, gz); }
            }
            return best;
        }

        /// Dunes (v3 dune sea): asymmetric ridges across a hashed wind -- a
        /// long windward slope, a steep slip face -- bent by a 2-D warp.
        public static float DuneField(in Offsets o, float2 p)
        {
            float2 wind = math.normalizesafe(o.dip + new float2(0.3f, 0.1f), new float2(1f, 0f));
            float2 perp = new float2(-wind.y, wind.x);
            // 160 m apart, up to 26 m tall (the first try, 90 m / 12 m, barely showed).
            float s = math.dot(p, wind) / 1600f + 0.35f * Noise.Fbm(p / 5000f + o.soil, 3) + 0.08f * math.dot(p, perp) / 1600f;
            float t = s - math.floor(s);
            float y = t < 0.75f ? t / 0.75f : (1f - t) / 0.25f;
            y = y * y * (3f - 2f * y);
            float amp = 260f * (0.6f + 0.4f * (Noise.Fbm(p / 6000f + o.soil + 3f, 2) * 0.5f + 0.5f));
            return y * amp;
        }

        /// The island envelope (§2.5): the coast value at p, > 0 on land.
        public static float Coast(in TerrainSpec spec, in Offsets o, float2 p)
        {
            float c = spec.worldSizeVoxels * 0.5f;
            float2 q = p - new float2(c, c);
            float d = math.sqrt(q.x * q.x + q.y * q.y) / spec.islandRadius;
            return (1f - d) + Noise.Fbm(p / 9000f + o.coastA, 5) * 0.45f + Noise.Fbm(p / 2600f + o.coastB, 3) * 0.12f;
        }

        public static float OceanFloor(in TerrainSpec spec, float coast)
        {
            float deep = math.min(1f, -coast * 3.5f);
            return -(60f + (spec.oceanDepth - 60f) * deep * deep * (3f - 2f * deep));
        }

        /// The caldera holds LAVA, not the flood's water: clear the whole
        /// caldera lake, found by its level (see BuildV2's note).
        private static void ClearCalderaLake(in TerrainSpec spec, in Offsets o, float[] water, byte[] cls)
        {
            int n = spec.mapCells;
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
        }

        private static WorldMap BuildV2(TerrainSpec spec)
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
            TrimSpikes(in spec, in o, h);
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

        /// CUESTA profile in [0, 1] (2026-09-25; the round domes read as "large
        /// rounded hills" from above). An asymmetric ridge: the crest runs along
        /// `u` (half-length Ls, wandering, fading out over the last 40% at each
        /// end); on one side a long smooth BACK SLOPE of width Wb (the way up), on
        /// the other a terraced SCARP of width Ws (the cliff, the view). `scarp`
        /// is true on the cliff band, for the roughness channel.
        public static float CuestaT(float2 q, float2 u, float Ls, float Wb, float Ws, float2 noiseOff, out bool scarp)
        {
            scarp = false;
            float along = math.dot(q, u), side = q.x * -u.y + q.y * u.x;
            side += 0.15f * Wb * noise.snoise(new float2(along / (Ls * 0.8f), 0.5f) + noiseOff);
            float a = math.abs(along) / (Ls * (1f + 0.2f * noise.snoise(new float2(along / Ls * 1.7f, 3.1f) + noiseOff)));
            float end = Noise.SmoothStep(1f, 0.6f, a);
            if (end <= 0f) return 0f;
            float across;
            if (side >= 0f)
            {
                float t = math.saturate(1f - side / Wb);
                across = t * t * (3f - 2f * t);
            }
            else
            {
                float w = math.saturate(1f + side / Ws);
                float s3 = w * 3f, k = math.floor(s3);
                across = math.min(1f, (k + Noise.SmoothStep(0.7f, 1f, s3 - k)) / 3f);
                scarp = w > 0f && w < 1f;
            }
            return across * end;
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
                    Stamps.InTreeArea(in spec, in o, p, 1500f) ||
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
                // Cuesta: the back slope's width from the slope limit (smoothstep
                // peaks at 1.5 H / Wb), a terraced scarp a third of the rise wide,
                // the crest 1.2-2x the back slope's width long.
                float Wb = math.max(2200f, 1.5f * rise / DOME_MAX_SLOPE);
                float Ws = math.max(350f, rise * 0.35f);
                float Ls = Wb * (1.2f + 0.8f * Noise.Hash01(bx, 10, bz, o.seed + 305u));
                float2 lobeOff = new float2(Noise.Hash01(bx, 13, bz, o.seed + 306u), Noise.Hash01(bx, 14, bz, o.seed + 307u)) * 100f;
                float extent = mesa ? R + math.max(B, L) : math.max(Ls * 1.25f, Wb * 1.2f) + Ws;
                int rc = (int)math.ceil(extent / spec.mapCellVoxels) + 1;
                for (int z = math.max(0, cz - rc); z <= math.min(n - 1, cz + rc); z++)
                for (int x = math.max(0, cx - rc); x <= math.min(n - 1, cx + rc); x++)
                {
                    float2 q = new float2((x + 0.5f) * spec.mapCellVoxels, (z + 0.5f) * spec.mapCellVoxels) - p;
                    float d = math.sqrt(q.x * q.x + q.y * q.y);
                    bool cliff = false;
                    float t = mesa ? MesaT(q, d, R, B, L, rampDir) : CuestaT(q, rampDir, Ls, Wb, Ws, lobeOff, out cliff);
                    if (t <= 0f) continue;
                    int i = z * n + x;
                    float nh = h[i] + (top - h[i]) * t;
                    if (nh > h[i]) { h[i] = nh; r[i] = math.max(r[i], mesa || cliff ? 0.8f : 0.4f); }
                }
                count++;
            }
            return count;
        }

        // ------------------------------------------------------------------
        // SPIKE TRIM (2026-09-25). The ridged ranges make horns wherever the
        // noise touches zero at a POINT rather than along a line (seed 42 had
        // summits falling 45-82 m within 40 m on every side). The first fix, an
        // opening with a round paraboloid, removed them -- and also rounded every
        // ridge ACROSS its crest, which made the whole island soft (owner: "too
        // rounded ... overcompensated"). This is a UNION OF LINE OPENINGS: a 1-D
        // parabolic opening along each of four directions (0, 45, 90, 135 deg),
        // keeping the MAX of the four. A crest that is long in at least one
        // direction (a ridge, an arete, an escarpment or mesa edge) survives
        // intact along that direction; only a peak that is sharp in EVERY
        // direction -- a spike -- is cut down. It only ever lowers terrain.
        // The caldera and its rim, and the geyser basin's terrace, are left alone.
        // ------------------------------------------------------------------
        /// Voxels of height per map cell squared along a line: 40 m (5 cells)
        /// from a trimmed tip the ground has fallen at most 7 x 25 = 175 voxels.
        public const float SUMMIT_CURVATURE = 7f;
        public const int SUMMIT_WINDOW = 6;   // cells each side (48 m)

        public static void TrimSpikes(in TerrainSpec spec, in Offsets o, float[] h)
        {
            int n = spec.mapCells, W = SUMMIT_WINDOW;
            var best = new float[n * n];
            for (int i = 0; i < best.Length; i++) best[i] = float.MinValue;
            var e = new float[n * n];
            int2[] dirs = { new int2(1, 0), new int2(0, 1), new int2(1, 1), new int2(1, -1) };
            foreach (int2 dv in dirs)
            {
                float k = SUMMIT_CURVATURE * (dv.x != 0 && dv.y != 0 ? 2f : 1f);   // diagonal steps are sqrt(2) cells
                // Erosion along the line: e(x) = min_t h(x + t d) + k t^2.
                for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                {
                    float m = float.MaxValue;
                    for (int t = -W; t <= W; t++)
                    {
                        int xx = x + t * dv.x, zz = z + t * dv.y;
                        if (xx < 0 || zz < 0 || xx >= n || zz >= n) continue;
                        m = math.min(m, h[zz * n + xx] + k * t * t);
                    }
                    e[z * n + x] = m;
                }
                // Dilation along the same line, and the union over directions.
                for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                {
                    float m = float.MinValue;
                    for (int t = -W; t <= W; t++)
                    {
                        int xx = x + t * dv.x, zz = z + t * dv.y;
                        if (xx < 0 || zz < 0 || xx >= n || zz >= n) continue;
                        m = math.max(m, e[zz * n + xx] - k * t * t);
                    }
                    int i = z * n + x;
                    best[i] = math.max(best[i], m);
                }
            }
            float2 vs = Stamps.VolcanoSite(in spec, in o), gs = Stamps.GeyserSite(in spec, in o);
            for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                int i = z * n + x;
                if (best[i] >= h[i]) continue;
                float2 p = new float2((x + 0.5f) * spec.mapCellVoxels, (z + 0.5f) * spec.mapCellVoxels);
                // The caldera and its rim keep their edge: a curvature limit cuts a
                // crest that sharp and the wall stops containing the lava.
                if (math.distance(p, vs) < Stamps.CALDERA_R * 1.15f || math.distance(p, gs) < 1600f) continue;
                h[i] = best[i];
            }
        }

        /// The world tree's canopy (map level): the leaf dome's top over the
        /// ground under it. Runs after TrimSpikes so it sits on trimmed ground.
        public static void TreeCanopy(in TerrainSpec spec, in Offsets o, float[] h)
        {
            if (!Stamps.WorldTreeEnabled) return;
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
        /// Beacons for the owner's note -- and then the round DOMES, which made
        /// the island "too rounded and soft"): CUESTA, an asymmetric ridge with
        /// a walkable back slope and a terraced cliff; MESA, a flat lookout top
        /// on a terraced cliff band with one walkable ramp; CRAGS, a cluster of
        /// small rocky knobs; KNOLL, a low irregular hill.
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
                if (kind < 0.35f)
                {
                    // CUESTA: 40-100 m, crest 200-350 m long (half), back slope
                    // >= 2.6 x its height wide, terraced scarp. Footprint < 4,500.
                    float h = 400f + 600f * size;
                    float Ls = 2000f + 1500f * Noise.Hash01(gx, 5, gz, o.seed + 106u);
                    float Wb = math.max(1200f, 2.6f * h), Ws = 300f + 0.3f * h;
                    float t = CuestaT(d, u, Ls, Wb, Ws, lobeOff, out bool cliff);
                    if (t > 0f) { add = math.max(add, h * t); if (cliff) edge = 1f; }
                }
                else if (kind < 0.62f)
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
                else if (kind < 0.85f)
                {
                    // CRAGS: a cluster of 3-6 rocky knobs, 30-55 m across and 8-20 m
                    // tall, within 80 m of the centre -- exposed rock to scramble
                    // over, not a tower. Rough (edge) so D's detail makes it rocky.
                    // No smaller: at the map's 8 m cells a narrower knob is below
                    // TrimSpikes' curvature and would be cut flat.
                    if (dist < 1400f)
                    {
                        int count = 3 + (int)(4f * size);
                        for (int kk = 0; kk < count; kk++)
                        {
                            float2 kc = c + new float2(Noise.Hash01(gx * 8 + kk, 20, gz, o.seed + 111u) - 0.5f,
                                                       Noise.Hash01(gx * 8 + kk, 21, gz, o.seed + 112u) - 0.5f) * 1600f;
                            float kr = 300f + 250f * Noise.Hash01(gx * 8 + kk, 22, gz, o.seed + 113u);
                            float kh = 80f + 120f * Noise.Hash01(gx * 8 + kk, 23, gz, o.seed + 114u);
                            float kx = math.saturate(math.distance(p, kc) / kr);
                            if (kx < 1f) { add = math.max(add, kh * (1f - kx * kx)); edge = 1f; }
                        }
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
        /// TerrainSpec.BiomeScale (map v4, B1). 1 from For(seed); 0 (a default
        /// struct) is read as 1. A v4 world's Offsets MUST come from For(spec),
        /// or its biomes silently fall back to v3's.
        public float biomeScale;
        /// TerrainSpec.BiomeSet (B10): 6 = the six-biome field; anything else
        /// (0 from For(seed) or a default struct) = the four-biome field.
        public int biomeSet;
        /// Map v4 (B5): the drawn volcano's site (Features.VolcanoSite; off the
        /// map when none was drawn), valid when v4Sites != 0. Only For(spec)
        /// sets it: Stamps.Column reads it per voxel column under Burst.
        public float2 volcanoV4;
        public int v4Sites;

        /// The offsets for this spec: For(seed) plus the spec's biome scale
        /// and, on v4, the drawn volcano's site.
        public static Offsets For(in TerrainSpec spec)
        {
            var o = For(spec.seed);
            o.biomeScale = spec.BiomeScale;
            o.biomeSet = spec.BiomeSet;
            if (spec.mapVersion >= 4) { o.volcanoV4 = Features.VolcanoSite(in spec, in o); o.v4Sites = 1; }
            return o;
        }

        public static Offsets For(uint seed)
        {
            var o = new Offsets { seed = seed, biomeScale = 1f };
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
