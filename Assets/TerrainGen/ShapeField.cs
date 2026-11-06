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
// height. Borders are the climate fields' contour lines; since task 122 a
// column's SURFACE material follows a warped, dithered copy of them
// (SurfaceBiome) -- the biome itself does not.
using Unity.Mathematics;

namespace TerrainGen
{
    public struct ColumnSample
    {
        public int height;       // absolute Y of the top SOLID voxel
        public byte biome;       // GenBiome
        /// Task 122: the biome whose MATERIALS this column wears (surface, soil,
        /// the beach rule, its strata/rock). Equal to `biome` except in a border's
        /// blend band (ShapeField.SurfaceBiome); ores, pocket liquids, crystals,
        /// danger and the world record keep `biome`.
        public byte surfBiome;
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
        /// Map v4 (task 37e): the column's ground BEFORE a lake rim or shore
        /// shelf raised it (ShapeField.LakeRim), or 0 = not raised. D carves
        /// caves by depth under THIS ground, so the fill is never carved and
        /// the caves under it stay where they were.
        public int rimGround;
        /// v3 T5b: the air under a CAP-ROCK LEDGE, [lo, hi). Like the stamp
        /// intervals, never contains the top voxel (the ledge IS the top).
        public int open3Lo, open3Hi;
        /// Map v4 river (task 76, Rivers.Column): voxels [lipLo, height] are a
        /// LIP (GenMat.RiverLip) holding the river's next step up; 0 = none.
        public int lipLo;
        /// Map v4 river (task 76): caves carve only BELOW this Y (a river is
        /// near: Rivers.MAX_DEPTH under its lowest level); 0 = no limit.
        public int riverFloor;
        /// Task 177: the ground-cover slot (Cover.Flat .. Cover.Shore, bits 0-2)
        /// and its patch variant (bit 3), from the column's shape (Cover.Choose).
        /// 0 (flat) when TerrainSpec.CoverSlots is off or only the height was asked for.
        public byte cover;
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

        public static ColumnSample Sample(in TerrainSpec spec, in MapView map, in Offsets o,
                                          float x, float z, int texelVoxels)
            => Sample(in spec, in map, in o, x, z, texelVoxels, true);

        /// surfaceBiome false: a caller that reads only the HEIGHT (a slope's
        /// neighbour) skips the border blend; surfBiome is then just biome.
        public static ColumnSample Sample(in TerrainSpec spec, in MapView map, in Offsets o,
                                          float x, float z, int texelVoxels, bool surfaceBiome)
            => Sample(in spec, in map, in o, x, z, texelVoxels, surfaceBiome, texelVoxels);

        /// coverTexel (task 177): the footprint the cover is drawn at, in voxels.
        /// Cover.Choose drops its noise octaves shorter than two footprints, as
        /// the height drops detail octaves, so a far texel or a coarse shell voxel
        /// shows the patch it lies in, not an aliased per-texel dither. A shell
        /// samples heights at texel 1 but draws one voxel per 2^shell columns.
        public static unsafe ColumnSample Sample(in TerrainSpec spec, in MapView map, in Offsets o,
                                                 float x, float z, int texelVoxels, bool surfaceBiome, int coverTexel)
        {
            SampleMap(in map, x, z, out float macro, out float rough);
            float2 p = new float2(x, z);
            float detail = 0f;
            int oct = OctavesFor(texelVoxels);
            for (int i = 0; i < oct; i++)
                detail += Amplitude(i) * OctaveNoise(in o, p, i);
            var c = new ColumnSample();
            float hRel = macro + detail * rough;
            bool scree = false;
            if (spec.Cliffs && map.water != null) hRel = FaceRelief(in map, in o, p, macro, hRel, texelVoxels, out scree);   // task 178
            c.height = spec.seaLevelY + (int)math.floor(hRel);
            c.height = math.max(c.height, spec.worldBottomY + 1);
            if (o.biomeBlend != 0 && surfaceBiome)
            {
                c.biome = BiomeAndBorder(in o, p, true, out float border);
                c.surfBiome = SurfaceBiome(in o, p, c.biome, border);
            }
            else c.surfBiome = c.biome = Biome(in o, p);
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
            int rimY = int.MinValue; bool lakeWater = false;
            if (map.waterClass != null && spec.mapVersion >= 4) LakeRim(in spec, in map, in o, x, z, ref c, out rimY, out lakeWater);   // v4: lakes at rest
            int riverHoldY = int.MinValue; bool nearRiver = false;
            if (map.flowDir != null && map.waterClass != null && spec.mapVersion >= 4)
                Rivers.Column(in spec, in map, x, z, texelVoxels, rimY, lakeWater, ref c, out riverHoldY, out nearRiver);   // task 76: rivers at rest
            // §2.5 stamps. Skipped for a dry (pre-v2) map view, which is what
            // every older job builds -- they keep their exact prior output.
            if (map.water != null) Stamps.Column(in spec, in map, in o, p, texelVoxels, ref c, riverHoldY);
            if (nearRiver)
            {
                // Task 76: no stamp opens air beside or under a river (cave
                // mouths, fissures, the skull cave, arch / hoop / rib openings),
                // as NearLake keeps them from lakes. The stamp's solid body stays.
                c.open1Lo = c.open1Hi = c.open2Lo = c.open2Hi = 0;
                c.carvesTop = false; c.stamp3D = false;
            }
            if (map.water != null && spec.mapVersion >= 3) Ledge(in spec, in map, in o, p, ref c);
            if (surfaceBiome && spec.CoverSlots)   // task 177: last, on the column's final height / water
            {
                c.cover = Cover.Choose(in spec, in map, in o, p, macro, rough, c.height, c.water, c.regional, coverTexel);
                // Task 178: scree is steep ground (the palette's steep cell), wherever the
                // apron reaches -- a test on the map alone, so every footprint agrees.
                if (scree && !(c.water > c.height) && Cover.Slot(c.cover) != Cover.Shore)
                    c.cover = (byte)((c.cover & Cover.VARIANT_BIT) | Cover.Steep);
            }
            return c;
        }

        // ------------------------------------------------------------------
        // TASK 178 (WORLD_GEN_LOOK_PLAN C4, finding 4: "every sheer face is one
        // smooth grey material ... no layers, cracks, ledges or scree"). FACE
        // RELIEF from the MAP SLOPE. The detail octaves add a fixed HEIGHT, so on
        // a face of slope s they move the face sideways by only height / s: a
        // 167 m cliff came out smooth. Here every term is driven by the map's
        // own slope s (the Catmull-Rom gradient, C0 across cells) and by height,
        // never by a place or a pose:
        //   weight w   smoothstep from FACE_FROM_DEG to FACE_FULL_DEG (the
        //              record's 45 deg climb | sheer edge, -5 / +10 deg)
        //   fluting    +-FLUTE_DEPTH (0.8 m) SIDEWAYS: a height term scaled by
        //              min(s, FACE_SLOPE_CAP); ridged noise of FLUTE_WAVE (5 m)
        //              in plan. On a steep face a plan noise barely changes down
        //              the face, so it reads as VERTICAL flutes and ribs; it
        //              ramps in from 55 to FLUTE_FULL_DEG (70 deg).
        //   ledges     a terrace on height + the rock layers' wave: a flat tread
        //              (LEDGE_TREAD of each LEDGE_SPACING = 4 m of height), then
        //              a steeper riser. Treads run along the strata.
        //   scree      at the FOOT: the map's concavity along each axis at
        //              +-SCREE_PROBE (20 m), capped by the higher side (so a
        //              uniform face gets none and a gorge only one side's
        //              worth), beyond SCREE_KNEE x the probe (the higher side
        //              rising over 45 deg, the record's sheer edge); SCREE_SHARE
        //              of it is an apron (~35 deg off a 74 deg face), at most
        //              SCREE_CAP (10 m), with rubble lumps on top.
        // Skipped under water and within ~20 m of a lake or river cell (their
        // banks are Rivers' / LakeRim's), below the sea and for dry map views.
        // TASK 178b (owner 2026-11-06, 178 Q2 (a) "keep routes smooth"): also
        // skipped on ROUTE cells (the world record's FLAG_ROUTE, MapView.route):
        // B8 already eased the map along the route, and the relief (the scree
        // apron above all) put Sheer steps back on it. RouteWeight fades the
        // relief back in over ROUTE_BLEND_CELLS map cells beyond the corridor's
        // edge (distance to the nearest route cell's square, smoothstep), so no
        // new step forms there: the scree's worst added grade is
        // 1.5 * SCREE_CAP / (2 cells = 16 m) ~ 0.94. A function of the map
        // alone, like the scree, so every footprint agrees; a convex blend of
        // two bounded heights, so the band-limit bound is unchanged.
        // BAND LIMIT: fluting, ledges and rubble drop below 2 footprints like an
        // octave (BandLimitBound(spec, ..) adds RELIEF_BAND_BOUND); the scree
        // itself reads the map only, so every footprint gets the same apron.
        // ------------------------------------------------------------------
        public const float FACE_FROM_DEG = Danger.CLIMB_DEG - 5f, FACE_FULL_DEG = Danger.CLIMB_DEG + 10f;
        public const float FLUTE_WAVE = 50f, FLUTE_DEPTH = 8f, FACE_SLOPE_CAP = 4f;
        /// Fluting ramps in from FACE_FULL_DEG to FLUTE_FULL_DEG: a plan noise reads as a
        /// VERTICAL flute only where one feature spans more height than width (s >~ 2);
        /// on a 40-55 deg slope it read as crumpled noise (178's first top views).
        public const float FLUTE_FULL_DEG = 70f;
        public const float LEDGE_SPACING = 40f, LEDGE_TREAD = 0.35f;
        public const float SCREE_PROBE = 200f, SCREE_KNEE = 1f, SCREE_SHARE = 0.2f, SCREE_CAP = 100f, SCREE_MIN = 5f;
        public const float RUBBLE_WAVE = 15f, RUBBLE_AMP = 3f;
        /// Most a footprint's dropped relief can move a column (flutes + ledge + rubble).
        public const float RELIEF_BAND_BOUND = FLUTE_DEPTH * FACE_SLOPE_CAP + LEDGE_TREAD * LEDGE_SPACING + RUBBLE_AMP;
        /// Map cells round the column checked for lake / river water (+-2 = ~20 m).
        public const int RELIEF_DRY_CELLS = 2;
        /// Task 178b: the relief fades in over this many map cells beyond a route
        /// cell (RouteWeight; <= 2, its search radius).
        public const int ROUTE_BLEND_CELLS = 2;

        /// The column's height (voxels rel. sea) with the face relief; `h` is
        /// macro + detail, `macro` the map at p. scree: the apron covers p by at
        /// least SCREE_MIN (a function of the map alone).
        public static float FaceRelief(in MapView map, in Offsets o, float2 p, float macro, float h, int texel, out bool scree)
        {
            scree = false;
            if (h <= 0f || WetNear(in map, p)) return h;
            float rw = RouteWeight(in map, p);   // task 178b
            if (rw >= 1f) return h;
            float h0 = h;
            float2 g = MapGradient(in map, p.x, p.y);
            float s = math.sqrt(g.x * g.x + g.y * g.y);
            float w = FaceWeight(s);
            if (w > 0f)
            {
                float wf = math.smoothstep(math.tan(math.radians(FACE_FULL_DEG)), math.tan(math.radians(FLUTE_FULL_DEG)), s);
                if (wf > 0f && FLUTE_WAVE >= 2f * texel)
                {
                    float n = noise.snoise(p / FLUTE_WAVE + o.detail + new float2(-31.7f, 57.3f));
                    h += wf * FLUTE_DEPTH * math.min(s, FACE_SLOPE_CAP) * (1f - 2f * math.abs(n));
                }
                if (LEDGE_SPACING >= 2f * texel)
                {
                    float t = (h + DetailField.LayerWave(in o, p)) / LEDGE_SPACING;
                    float f = t - math.floor(t);
                    float tf = f < LEDGE_TREAD ? 0f : (f - LEDGE_TREAD) / (1f - LEDGE_TREAD);
                    h -= w * LEDGE_SPACING * (f - tf);
                }
            }
            float fill = ScreeFill(in map, p, macro);
            if (fill > 0f)
            {
                scree = fill >= SCREE_MIN;
                float top = macro + fill;
                if (RUBBLE_WAVE >= 2f * texel)
                    top += RUBBLE_AMP * math.saturate(fill / SCREE_CAP * 4f) * noise.snoise(p / RUBBLE_WAVE + o.detail + new float2(83.1f, 19.9f));
                h = math.max(h, top);
            }
            if (rw > 0f)
            {
                h = math.lerp(h, h0, rw);
                scree = (1f - rw) * fill >= SCREE_MIN;
            }
            return h;
        }

        /// Task 178b: 1 on a route cell (MapView.route), falling to 0 at
        /// ROUTE_BLEND_CELLS cells from the nearest one (smoothstep of the
        /// distance from p to that cell's square); 0 for a view without routes.
        public static unsafe float RouteWeight(in MapView map, float2 p)
        {
            if (map.route == null) return 0f;
            int n = map.cells, r = ROUTE_BLEND_CELLS;
            float c = map.cellVoxels, blend = r * c;
            int ix = (int)math.floor(p.x / c), iz = (int)math.floor(p.y / c);
            if (map.route[math.clamp(iz, 0, n - 1) * n + math.clamp(ix, 0, n - 1)] != 0) return 1f;
            float d2 = blend * blend;
            for (int dz = -r; dz <= r; dz++)
            for (int dx = -r; dx <= r; dx++)
            {
                int cx = ix + dx, cz = iz + dz;
                if (cx < 0 || cz < 0 || cx >= n || cz >= n || map.route[cz * n + cx] == 0) continue;
                float ex = math.max(math.max(cx * c - p.x, p.x - (cx + 1) * c), 0f);
                float ez = math.max(math.max(cz * c - p.y, p.y - (cz + 1) * c), 0f);
                d2 = math.min(d2, ex * ex + ez * ez);
            }
            return 1f - math.smoothstep(0f, blend, math.sqrt(d2));
        }

        /// The scree apron's height over the map at p (voxels, 0 = none): a
        /// function of the map alone.
        public static float ScreeFill(in MapView map, float2 p, float macro)
        {
            SampleMap(in map, p.x + SCREE_PROBE, p.y, out float hE, out _);
            SampleMap(in map, p.x - SCREE_PROBE, p.y, out float hW, out _);
            SampleMap(in map, p.x, p.y + SCREE_PROBE, out float hN, out _);
            SampleMap(in map, p.x, p.y - SCREE_PROBE, out float hS, out _);
            float cx = math.min(hE + hW - 2f * macro, math.max(hE, hW) - macro);
            float cz = math.min(hN + hS - 2f * macro, math.max(hN, hS) - macro);
            float ex = math.max(cx, cz) - SCREE_KNEE * SCREE_PROBE;
            return ex > 0f ? math.min(SCREE_SHARE * ex, SCREE_CAP) : 0f;
        }

        /// The face weight at map slope s (0 below FACE_FROM_DEG, 1 from FACE_FULL_DEG).
        public static float FaceWeight(float s)
            => math.smoothstep(math.tan(math.radians(FACE_FROM_DEG)), math.tan(math.radians(FACE_FULL_DEG)), s);

        /// A lake or river cell within RELIEF_DRY_CELLS cells of p's cell.
        public static unsafe bool WetNear(in MapView map, float2 p)
        {
            int n = map.cells, r = RELIEF_DRY_CELLS;
            int ix = (int)math.floor(p.x / map.cellVoxels), iz = (int)math.floor(p.y / map.cellVoxels);
            for (int dz = -r; dz <= r; dz++)
            for (int dx = -r; dx <= r; dx++)
            {
                int cx = math.clamp(ix + dx, 0, n - 1), cz = math.clamp(iz + dz, 0, n - 1), i = cz * n + cx;
                if (map.waterClass != null ? map.waterClass[i] != Hydrology.DRY : map.water[i] > Hydrology.NO_WATER * 0.5f) return true;
            }
            return false;
        }

        /// The gradient of SampleMap's height (voxels per voxel): the Catmull-Rom
        /// derivative, continuous across cell edges (the spline is C1).
        public static unsafe float2 MapGradient(in MapView map, float x, float z)
        {
            float gx = x / map.cellVoxels - 0.5f;
            float gz = z / map.cellVoxels - 0.5f;
            int ix = (int)math.floor(gx), iz = (int)math.floor(gz);
            float fx = gx - ix, fz = gz - iz;
            float4 wx = CatmullRom(fx), wz = CatmullRom(fz), dx = CatmullRomD(fx), dz = CatmullRomD(fz);
            float hx = 0f, hz = 0f;
            int n = map.cells;
            for (int j = 0; j < 4; j++)
            {
                int cz = math.clamp(iz - 1 + j, 0, n - 1);
                float rw = 0f, rd = 0f;
                for (int i = 0; i < 4; i++)
                {
                    float v = map.height[cz * n + math.clamp(ix - 1 + i, 0, n - 1)];
                    rw += wx[i] * v;
                    rd += dx[i] * v;
                }
                hx += wz[j] * rd;
                hz += dz[j] * rw;
            }
            return new float2(hx, hz) / map.cellVoxels;
        }

        private static float4 CatmullRomD(float t)
        {
            float t2 = t * t;
            return new float4(
                -1.5f * t2 + 2f * t - 0.5f,
                4.5f * t2 - 5f * t,
                -4.5f * t2 + 4f * t + 0.5f,
                1.5f * t2 - t);
        }

        /// BandLimitBound plus what task 178's face relief can add at a footprint:
        /// the ledge terrace stretches a height difference by up to 1 / (1 - tread),
        /// and fluting, ledges and rubble drop like octaves.
        public static float BandLimitBound(in TerrainSpec spec, int texelVoxels, float rough)
        {
            float b = BandLimitBound(texelVoxels, rough);
            return spec.Cliffs && texelVoxels > 1 ? b / (1f - LEDGE_TREAD) + RELIEF_BAND_BOUND : b;
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
            int a = Sample(in spec, in map, in o, x + spacing, z, texel, false).height;
            int b = Sample(in spec, in map, in o, x - spacing, z, texel, false).height;
            int c = Sample(in spec, in map, in o, x, z + spacing, texel, false).height;
            int d = Sample(in spec, in map, in o, x, z - spacing, texel, false).height;
            int m = math.max(math.max(math.abs(a - centreHeight), math.abs(b - centreHeight)),
                             math.max(math.abs(c - centreHeight), math.abs(d - centreHeight)));
            return m / (float)spacing;
        }

        /// Climate -> biome. Nearest climate centre in (temperature, moisture).
        /// Map v4 multiplies both wavelengths by o.biomeScale (B1, larger
        /// biomes); at 1 the products are exactly 15000f and 12000f, so v3 is
        /// byte-identical.
        public static byte Biome(in Offsets o, float2 p) => BiomeAndBorder(in o, p, false, out _);

        /// Biome plus (wantBorder) `border`: the climate-space distance from the
        /// column's climate to the nearest bisector with another centre, i.e. how
        /// far the climate must move before the biome changes. One body for both,
        /// so the biome is the same bits either way.
        public static byte BiomeAndBorder(in Offsets o, float2 p, bool wantBorder, out float border)
        {
            float s = o.biomeScale > 0f ? o.biomeScale : 1f;
            if (o.biomeSet == 6) return Biome6(in o, p, s, wantBorder, out border);
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
            border = wantBorder ? Border(b, df, dd, ds, dj, float.MaxValue, float.MaxValue) : 0f;
            return b;
        }

        /// Map v4's six biomes (B10, TerrainSpec.BiomeSet 6). One octave of
        /// climate (the 2nd and 3rd octaves only added km-scale meanders --
        /// extra biome changes -- at x3, never detail a player sees at 100 m)
        /// at amplitude 1.6, so the climate stays nearer the forest centre than
        /// v3's 2.4 three-octave field. The four old centres are unchanged;
        ///   savanna  (0.8, 0)     hot, between desert's dryness and jungle's wet
        ///   badlands (-0.3, -0.8) dry and cool-temperate: the cold side of desert
        /// Shares on land at x3.25 (seeds 42/7/2026/31337): savanna 9/11/20/4%,
        /// badlands 8/11/15/32%.
        private static byte Biome6(in Offsets o, float2 p, float s, bool wantBorder, out float border)
        {
            float t, m;
            if (o.biomeVary != 0)
            {
                t = (Climate3(p / o.climWave.x + o.tempA) - o.climMean.x) * o.climGain.x;
                m = (Climate3(p / o.climWave.y + o.moistA) - o.climMean.y) * o.climGain.y;
            }
            else
            {
                t = Noise.Fbm(p / (15000f * s) + o.tempA, 1) * 1.6f;
                m = Noise.Fbm(p / (12000f * s) + o.moistA, 1) * 1.6f;
            }
            float df = t * t + m * m;
            float dd = (t - 0.8f) * (t - 0.8f) + (m + 0.8f) * (m + 0.8f);
            float ds = (t + 0.9f) * (t + 0.9f) + (m - 0.1f) * (m - 0.1f);
            float dj = (t - 0.8f) * (t - 0.8f) + (m - 0.8f) * (m - 0.8f);
            float dv = (t - 0.8f) * (t - 0.8f) + m * m;
            float db = (t + 0.3f) * (t + 0.3f) + (m + 0.8f) * (m + 0.8f);
            byte b = GenBiome.Forest; float best = df;
            if (dd < best) { best = dd; b = GenBiome.Desert; }
            if (ds < best) { best = ds; b = GenBiome.Snow; }
            if (dj < best) { best = dj; b = GenBiome.Jungle; }
            if (dv < best) { best = dv; b = GenBiome.Savanna; }
            if (db < best) { b = GenBiome.Badlands; }
            border = wantBorder ? Border(b, df, dd, ds, dj, dv, db) : 0f;
            return b;
        }

        /// The climate centre of biome b (both fields share these).
        private static float2 Centre(byte b) =>
            b == GenBiome.Desert ? new float2(0.8f, -0.8f) : b == GenBiome.Snow ? new float2(-0.9f, 0.1f)
            : b == GenBiome.Jungle ? new float2(0.8f, 0.8f) : b == GenBiome.Savanna ? new float2(0.8f, 0f)
            : b == GenBiome.Badlands ? new float2(-0.3f, -0.8f) : new float2(0f, 0f);

        /// BiomeAndBorder's `border` from the squared distances to each centre
        /// (MaxValue = a centre the field does not have): min over the other
        /// centres j of (d_j - d_b) / (2 |c_j - c_b|), the distance to the bisector.
        private static float Border(byte b, float df, float dd, float ds, float dj, float dv, float dbl)
        {
            float d = b == GenBiome.Desert ? dd : b == GenBiome.Snow ? ds : b == GenBiome.Jungle ? dj
                    : b == GenBiome.Savanna ? dv : b == GenBiome.Badlands ? dbl : df;
            float2 cb = Centre(b);
            float r = float.MaxValue;
            r = math.min(r, Bisector(b, GenBiome.Forest, df, d, cb));
            r = math.min(r, Bisector(b, GenBiome.Desert, dd, d, cb));
            r = math.min(r, Bisector(b, GenBiome.Snow, ds, d, cb));
            r = math.min(r, Bisector(b, GenBiome.Jungle, dj, d, cb));
            r = math.min(r, Bisector(b, GenBiome.Savanna, dv, d, cb));
            r = math.min(r, Bisector(b, GenBiome.Badlands, dbl, d, cb));
            return r;
        }

        private static float Bisector(byte b, byte j, float dj, float d, float2 cb)
            => j == b || dj == float.MaxValue ? float.MaxValue : (dj - d) / (2f * math.distance(Centre(j), cb));

        // ------------------------------------------------------------------
        // TASK 176 (C1, WORLD_GEN_LOOK_PLAN.md; owner 2026-11-05 Q2 (a) "vary
        // climate", design direction "a wacky multi-topography like One Piece
        // but with a realistic twist"): B10's climate was ONE octave at 4.9 /
        // 3.9 km wavelength. Across a ~5 km island that is nearly a linear ramp,
        // so the nearest-centre rule cut it into straight parallel belts (173's
        // survey map: 1.7-2.9 km straight borders on all 8 layout seeds; most
        // biomes in one region only).
        //   Now the wavelength is a FRACTION OF THE ISLAND RADIUS (moisture
        // 0.75 R, temperature x1.25 as before), so the climate rises and falls
        // across any world size, with two low octaves (0.35, 0.2 of the first,
        // Noise.Fbm's lacunarity and offsets) that bend the borders at the
        // 300-900 m scale. Still climate fields only and NO domain warp (§2.6,
        // owner rule): a border is a contour of (t, m), and height is never read.
        //   NORMALISED PER WORLD: the raw field's mean and sd over the island
        // disc (radius islandRadius, the coast's nominal edge; CLIMATE_GRID^2
        // samples, seed-keyed noise only, no map) map it to mean 0, sd
        // CLIMATE_SD. So every seed spreads its climate over the same span of
        // the six centres: a seed can no longer be 53 % jungle (seed 2 before)
        // or 1 % desert (seed 31337). Computed once per spec in Offsets.Compute.
        // Chosen on 20 seeds' land masks (tools/biome176.py); accepted on 8.
        // ------------------------------------------------------------------
        //   TASK 176c (owner 2026-11-05 "size 2", 176b's Size 2): the wavelength
        // 0.75 -> 1.5 x islandRadius plus a 4th octave (0.12), so regions grow
        // ~2-3x (area-weighted median 44-65 -> 106-186 ha) and the extra octave
        // keeps the longer borders bent (straight <= 1.42 km on 176b's 4 seeds).
        public const float CLIMATE_WAVE_R = 1.5f;    // moisture wavelength / islandRadius (176: 0.75)
        public const float CLIMATE_TEMP_X = 1.25f;   // temperature wavelength / moisture's (B10's 15000 : 12000)
        public const float CLIMATE_OCT2 = 0.35f, CLIMATE_OCT3 = 0.2f, CLIMATE_OCT4 = 0.12f;
        public const float CLIMATE_SD = 0.7f;        // climate units: the spread over the island
        public const int CLIMATE_GRID = 64;          // normalisation samples per side of the disc's box

        /// The raw climate at q (noise units), before normalisation: 4 octaves
        /// since 176c (the name is 176's; amplitudes 1 / OCT2 / OCT3 / OCT4).
        public static float Climate3(float2 q)
        {
            float a = noise.snoise(q);
            q = q * 2.03f + new float2(17.3f, -9.1f);
            a += CLIMATE_OCT2 * noise.snoise(q);
            q = q * 2.03f + new float2(17.3f, -9.1f);
            a += CLIMATE_OCT3 * noise.snoise(q);
            q = q * 2.03f + new float2(17.3f, -9.1f);
            return a + CLIMATE_OCT4 * noise.snoise(q);
        }

        /// Offsets.Compute's step for a BiomeVary spec: wavelengths and the
        /// per-world normalisation of the climate over the island disc.
        public static void ClimateNormalise(in TerrainSpec spec, ref Offsets o)
        {
            o.biomeVary = 1;
            float wm = CLIMATE_WAVE_R * spec.islandRadius;
            o.climWave = new float2(wm * CLIMATE_TEMP_X, wm);
            float c = spec.worldSizeVoxels * 0.5f, r = spec.islandRadius;
            double st = 0, stt = 0, sm = 0, smm = 0; int k = 0;
            for (int j = 0; j < CLIMATE_GRID; j++)
            for (int i = 0; i < CLIMATE_GRID; i++)
            {
                float2 d = new float2((i + 0.5f) / CLIMATE_GRID * 2f - 1f, (j + 0.5f) / CLIMATE_GRID * 2f - 1f);
                if (d.x * d.x + d.y * d.y > 1f) continue;
                float2 p = new float2(c, c) + d * r;
                float t = Climate3(p / o.climWave.x + o.tempA), m = Climate3(p / o.climWave.y + o.moistA);
                st += t; stt += (double)t * t; sm += m; smm += (double)m * m; k++;
            }
            double mt = st / k, mm = sm / k;
            double sdt = math.sqrt(math.max(1e-12, stt / k - mt * mt)), sdm = math.sqrt(math.max(1e-12, smm / k - mm * mm));
            o.climMean = new float2((float)mt, (float)mm);
            o.climGain = new float2((float)(CLIMATE_SD / sdt), (float)(CLIMATE_SD / sdm));
        }

        // ------------------------------------------------------------------
        // TASK 122: BIOME BORDER BLEND (VISUAL_REVIEW_114 #5, owner Q6 (a),
        // 2026-10-22). The biome field's borders are contour lines of a climate
        // with a ~40-50 km wavelength, so they run nearly straight for km, and
        // every column on one side wore one surface. Now a column's SURFACE (and
        // soil) is the biome at a jittered point p + J(p):
        //   two warp octaves (800 m / 150 m wavelength, +-60 / +-15 m) bend the line,
        //   a clump octave (40 m, +-18 m) and a PATCH octave (10 m, +-14 m; task
        //   176 C1, was a per-0.8 m-cell hash dither that read as pixel noise up
        //   close) mix both sides' materials in a band a few tens of metres wide,
        //   in coherent patches of metres, denser toward each side.
        // Surface, soil AND rock follow it (a cliff's strata would otherwise keep
        // the straight line). The biome itself (ores, pocket liquids, crystals,
        // danger, the world record) is the unjittered one, so the biome x landform
        // maps are unchanged. Deterministic:
        // seed-keyed noise and an integer hash of the column's cell.
        // EARLY OUT: a column whose climate is more than |J|max x the climate's
        // Lipschitz bound from every bisector cannot change biome under any J,
        // so it skips the 8 noise calls (exact while |grad snoise| <=
        // SNOISE_GRAD_BOUND; measured max 7.37 over 6e7 points).
        // ------------------------------------------------------------------
        public const float BLEND_WARP1_WAVE = 8000f, BLEND_WARP1_AMP = 600f;   // voxels: 800 m, +-60 m
        public const float BLEND_WARP2_WAVE = 1500f, BLEND_WARP2_AMP = 150f;   // 150 m, +-15 m
        public const float BLEND_CLUMP_WAVE = 400f, BLEND_CLUMP_AMP = 180f;    // 40 m, +-18 m
        public const float BLEND_PATCH_WAVE = 100f, BLEND_PATCH_AMP = 140f;    // 10 m, +-14 m
        /// |J| bound (voxels): each octave's vector is at most amp * sqrt(2).
        public const float BLEND_REACH = (BLEND_WARP1_AMP + BLEND_WARP2_AMP + BLEND_CLUMP_AMP + BLEND_PATCH_AMP) * 1.41422f;
        public const float SNOISE_GRAD_BOUND = 8f;
        /// |grad| of the 3-octave Fbm (the four-biome field) over snoise's: sum amp_i 2.03^i / norm.
        private const float FBM3_GRAD = (0.5f + 0.25f * 2.03f + 0.125f * 2.03f * 2.03f) / 0.875f;

        /// The climate distance a jitter of BLEND_REACH can move at most.
        public static float BlendClimateReach(in Offsets o)
        {
            float s = o.biomeScale > 0f ? o.biomeScale : 1f;
            if (o.biomeVary != 0)
            {
                // |grad t| <= gain_t (1 + oct2 2.03 + oct3 2.03^2 + oct4 2.03^3) SNOISE_GRAD_BOUND / wave_t, same for m
                const float g3 = 1f + CLIMATE_OCT2 * 2.03f + CLIMATE_OCT3 * 2.03f * 2.03f + CLIMATE_OCT4 * 2.03f * 2.03f * 2.03f;
                float lt = o.climGain.x / o.climWave.x, lm = o.climGain.y / o.climWave.y;
                return BLEND_REACH * g3 * SNOISE_GRAD_BOUND * math.sqrt(lt * lt + lm * lm);
            }
            float amp = o.biomeSet == 6 ? 1.6f : 2.4f * FBM3_GRAD;
            const float invW = 1.0671874e-4f;   // sqrt(1/15000^2 + 1/12000^2)
            return BLEND_REACH * amp * SNOISE_GRAD_BOUND * invW / s;
        }

        /// The jitter J(p), voxels.
        public static float2 BlendOffset(in Offsets o, float2 p)
        {
            float2 a = o.tempA.yx + new float2(31.7f, -12.9f), b = o.moistA.yx + new float2(-57.1f, 44.3f);
            float2 j = BLEND_WARP1_AMP * new float2(noise.snoise(p / BLEND_WARP1_WAVE + a), noise.snoise(p / BLEND_WARP1_WAVE + b));
            j += BLEND_WARP2_AMP * new float2(noise.snoise(p / BLEND_WARP2_WAVE + b + 9.1f), noise.snoise(p / BLEND_WARP2_WAVE + a - 7.3f));
            j += BLEND_CLUMP_AMP * new float2(noise.snoise(p / BLEND_CLUMP_WAVE + a + 3.7f), noise.snoise(p / BLEND_CLUMP_WAVE + b - 5.9f));
            j += BLEND_PATCH_AMP * new float2(noise.snoise(p / BLEND_PATCH_WAVE + b + 1.3f), noise.snoise(p / BLEND_PATCH_WAVE + a - 8.6f));
            return j;
        }

        /// The surface biome of the column at p whose biome is `biome` and whose
        /// climate is `border` from the nearest bisector (BiomeAndBorder).
        public static byte SurfaceBiome(in Offsets o, float2 p, byte biome, float border)
        {
            if (border > BlendClimateReach(in o)) return biome;
            return Biome(in o, p + BlendOffset(in o, p));
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
            bool riversApart = map.flowDir != null && map.waterClass != null;   // map v4 with a flow map: rivers are Rivers.Column's
            for (int j = 0; j <= 1; j++)
            for (int i = 0; i <= 1; i++)
            {
                int cx = math.clamp(ix + i, 0, n - 1), cz = math.clamp(iz + j, 0, n - 1);
                if (riversApart && map.waterClass[cz * n + cx] == Hydrology.RIVER) continue;
                w = math.max(w, map.water[cz * n + cx]);
            }
            return w;
        }

        // ------------------------------------------------------------------
        // Map v4 LAKE RIMS (task 37e, owner Q31 part 2 (a), 2026-10-09): every
        // lake is at rest at ONE level. The map's 2x2 water rule (SampleWater)
        // ends a lake on the 8 m grid while its ground is the Catmull-Rom map
        // plus the detail octaves, so next to a lake the ground of a DRY column
        // dipped below the lake's level (detail noise, the basin's shallow fringe
        // that Hydrology leaves dry, the spill point): water stood beside air,
        // and the first wake drained the lake (task 39: 85-90k such cells at
        // seed 42's shore). The level is right (the map's spill level); the RIM
        // was low. Lowering the level instead would need every shore column's S
        // at map build (non-local).
        //   NATURAL SHORES (task 42, owner Q35 3 (a)): 37e kept the 2x2 wet area
        // and ended it with a shelf, so the shore WAS the 8 m grid (seed 42's
        // lake a rectangle). Now a lake's water is not the 2x2 rule's: a column
        // within LakeReach (72-104 voxels, noise-varied) of a lake cell's centre
        // is that lake's water wherever its ground is below the level, so the
        // shoreline is the ground's own contour. Near the reach the bed rises at
        // SHELF_SLOPE to the level (a gentle shelf where the ground is low there,
        // a spill point or the dry fringe), so no deep water stands beside the
        // raised rim (B8 read such a bank as Sheer). The shelf is gentle for its
        // first SHELF_W, then steepens (SHELF_CURVE), so a lake keeps its depth a
        // few metres in; the bound's dry-cell part gives every lake bank that
        // wadeable first SHELF_W.
        //   Beyond the reach, a column within RIM_FULL of a lake cell stands at
        // least at that lake's level (falling away at RIM_SLOPE beyond; a dip
        // under it is folded up, RIM_FOLD, not clamped into a flat plain); a
        // 2x2 lake column out of reach becomes rim too. A neighbour of a lake
        // water column is within REACH_MAX + 1.5 < RIM_FULL of that lake cell,
        // so it is lake water at the level or ground at or above it: no lake
        // voxel has air beside or diagonally below it -- per column, no pass
        // over the voxels. Rivers keep their levels: one higher than the lake
        // stays as it is (an inlet), a lower one within reach is lake, beyond
        // reach rim (the outlet gets a sill). Under a HIGHER lake's rim the
        // column is rim, never a lower lake's water (two levels never touch).
        // ------------------------------------------------------------------
        public const float RIM_FULL = 1.5f;     // map cells: the rim's full-height radius around a lake cell
        public const float RIM_SLOPE = 0.5f;    // voxels down per voxel beyond it (27 degrees)
        public const int RIM_FOLD = 8;          // voxels: the highest bump a folded dip makes over the rim
        public const float REACH_MAX = 104f;    // voxels: lake water's furthest reach from a lake cell's centre
        public const float REACH_VAR = 32f;     // voxels: the noise takes up to this off it
        public const float SHELF_SLOPE = 0.4f;  // voxels up per voxel toward the reach (22 degrees before the noise's tilt)
        public const float SHELF_W = 40f;       // voxels: the gentle part's width; beyond it the shelf steepens
        public const float SHELF_CURVE = 0.02f; // voxels per voxel^2 beyond SHELF_W (70 degrees 6 m further in): no depth cap

        /// Map v4: how far (voxels) lake water reaches from a lake cell's centre
        /// at (x, z): REACH_MAX less a noise share of REACH_VAR, so the edge of a
        /// lake's low ground follows no grid. Always > 0.71 cell + 1.5 voxels (a
        /// column inside one lake's 2x2 cells is that lake's water).
        public static float LakeReach(in Offsets o, float x, float z)
        {
            float2 p = new float2(x, z);
            float nz = 0.8f * noise.snoise(p / 112f + o.detail + new float2(71.3f, -23.9f))
                     + 0.2f * noise.snoise(p / 40f + o.detail + new float2(-17.1f, 55.7f));
            return REACH_MAX - REACH_VAR * math.saturate(0.5f + 0.5f * nz);
        }

        /// `rimY`: the rim level here (int.MinValue = no lake near); `lakeWater`:
        /// the column ends as a lake's own water. Rivers.Column reads both.
        private static unsafe void LakeRim(in TerrainSpec spec, in MapView map, in Offsets o, float x, float z, ref ColumnSample c,
                                           out int rimY, out bool lakeWater)
        {
            rimY = int.MinValue; lakeWater = false;
            float reach = LakeReach(in o, x, z);
            LakeNear(in map, x, z, reach, out float rim, out float level, out float dl, out float nearest);
            if (rim <= Hydrology.NO_WATER * 0.5f) return;
            int ry = spec.seaLevelY + (int)math.floor(rim);
            rimY = ry;
            int wy = level > Hydrology.NO_WATER * 0.5f ? spec.seaLevelY + (int)math.floor(level) : int.MinValue;
            if (wy == ry && c.water <= wy)
            {
                // The lake's own water, over a shelf that meets the level at the
                // reach. Its width is a LOWER bound on the distance to the reach's
                // edge (so the bed is never left below a raised rim): the nearest
                // disc's edge, or the nearest dry-side cell square -- every point
                // out of reach lies in one (its nearest cell centre is < 0.71 cell
                // away and is no lake cell). The second keeps a lake's middle at
                // its own depth.
                float e = math.max(reach - dl, DrySideDistance(in map, x, z, level));
                float steep = math.max(0f, e - SHELF_W);
                int bed = wy - (int)math.floor(SHELF_SLOPE * e + SHELF_CURVE * steep * steep);
                if (c.height < bed) { c.rimGround = c.height; c.height = bed; }
                if (c.height < wy) { c.water = wy; c.waterKind = 0; lakeWater = true; }
                return;
            }
            // Rim: out of reach, or under a higher lake's rim. Higher water (a
            // river inlet) keeps its own; so does a river exactly at the level.
            if (c.water > ry) return;
            if (c.water == ry && c.water != spec.seaLevelY + (int)math.floor(LakeLevel2x2(in map, x, z))) return;
            // FOLDED up, not clamped: a dip of d under the rim becomes a bump of
            // d/2 (at most RIM_FOLD) over it, so the raised ground keeps its
            // texture instead of a flat plain at the level. The bump grows in at
            // SHELF_SLOPE past the reach: no step beside the shelf's top.
            if (c.height < ry)
            {
                int fold = math.min(math.min((ry - c.height) >> 1, RIM_FOLD), (int)math.floor(SHELF_SLOPE * math.max(0f, nearest - reach)));
                c.rimGround = c.height; c.height = ry + fold;
            }
        }

        /// Map v4: the level (absolute Y) of the lake whose water the column at
        /// (x, z) is wherever its ground is below it (LakeRim's lake branch), or
        /// int.MinValue (out of every lake's reach, under a higher lake's rim,
        /// no water classes, pre-v4). A river at the same level is not this.
        public static unsafe int LakeWaterY(in TerrainSpec spec, in MapView map, in Offsets o, float x, float z)
        {
            if (map.waterClass == null || spec.mapVersion < 4) return int.MinValue;
            LakeNear(in map, x, z, LakeReach(in o, x, z), out float rim, out float level, out _, out _);
            if (level <= Hydrology.NO_WATER * 0.5f) return int.MinValue;
            int wy = spec.seaLevelY + (int)math.floor(level);
            return wy == spec.seaLevelY + (int)math.floor(rim) ? wy : int.MinValue;
        }

        /// The distance (voxels) from (x, z) to the nearest DRY-SIDE map cell's
        /// square for the lake at `level`: a cell that is not that lake's and
        /// holds no water at or above its level (a river inlet higher than the
        /// lake is not a dry side: its water covers the shared bed -- seed 7's
        /// 1.5 m wall, task 37e). At most 3.5 cells: every cell past the window
        /// is at least that far, so it stays a lower bound (the shelf's drop at
        /// that width is over 100 m: no lake is capped by it).
        private static unsafe float DrySideDistance(in MapView map, float x, float z, float level)
        {
            float cv = map.cellVoxels;
            int ix = (int)math.floor(x / cv - 0.5f), iz = (int)math.floor(z / cv - 0.5f), n = map.cells;
            float best = 3.5f * cv;
            for (int j = -3; j <= 4; j++)
            for (int i = -3; i <= 4; i++)
            {
                int cx = ix + i, cz = iz + j;
                if (cx < 0 || cz < 0 || cx >= n || cz >= n) continue;
                int k = cz * n + cx;
                byte wc = map.waterClass[k];
                if (wc == Hydrology.LAKE && map.water[k] == level) continue;
                if (wc != Hydrology.DRY && map.water[k] >= level) continue;
                float dx = math.max(0f, math.max(cx * cv - x, x - (cx + 1) * cv));
                float dz = math.max(0f, math.max(cz * cv - z, z - (cz + 1) * cv));
                best = math.min(best, math.sqrt(dx * dx + dz * dz));
            }
            return best;
        }

        /// The highest LAKE level among the 2x2 cells SampleWater takes its max
        /// over at (x, z), or NO_WATER.
        private static unsafe float LakeLevel2x2(in MapView map, float x, float z)
        {
            float cv = map.cellVoxels;
            int ix = (int)math.floor(x / cv - 0.5f), iz = (int)math.floor(z / cv - 0.5f), n = map.cells;
            float lv = Hydrology.NO_WATER;
            for (int j = 0; j <= 1; j++)
            for (int i = 0; i <= 1; i++)
            {
                int k = math.clamp(iz + j, 0, n - 1) * n + math.clamp(ix + i, 0, n - 1);
                if (map.waterClass[k] == Hydrology.LAKE) lv = math.max(lv, map.water[k]);
            }
            return lv;
        }

        /// One pass over the lake cells near (x, z): `rim` the highest of each
        /// lake cell's level, full within RIM_FULL and falling at RIM_SLOPE beyond
        /// (map units, or NO_WATER); `level` the highest level of a lake cell
        /// closer than `reach` and `dl` the distance to its nearest such cell;
        /// `nearest` the distance to the nearest lake cell (exact up to 4 cells).
        private static unsafe void LakeNear(in MapView map, float x, float z, float reach,
                                            out float rim, out float level, out float dl, out float nearest)
        {
            rim = level = Hydrology.NO_WATER; dl = nearest = float.MaxValue;
            float cv = map.cellVoxels;
            int ix = (int)math.floor(x / cv - 0.5f), iz = (int)math.floor(z / cv - 0.5f);
            int n = map.cells;
            float full = RIM_FULL * cv;
            // Cells beyond this window are >= 4 cells away: past the slope's reach
            // unless the ground is > (4 - RIM_FULL) x RIM_SLOPE cells (10 m)
            // below the lake there.
            for (int j = -3; j <= 4; j++)
            for (int i = -3; i <= 4; i++)
            {
                int cx = math.clamp(ix + i, 0, n - 1), cz = math.clamp(iz + j, 0, n - 1);
                int k = cz * n + cx;
                if (map.waterClass[k] != Hydrology.LAKE) continue;
                float dx = (cx + 0.5f) * cv - x, dz = (cz + 0.5f) * cv - z;
                float d = math.sqrt(dx * dx + dz * dz);
                float lk = map.water[k];
                nearest = math.min(nearest, d);
                rim = math.max(rim, lk - math.max(0f, d - full) * RIM_SLOPE);
                if (d < reach)
                {
                    if (lk > level) { level = lk; dl = d; }
                    else if (lk == level) dl = math.min(dl, d);
                }
            }
        }

        /// The lake rim's absolute Y at (x, z) (see above), or int.MinValue
        /// when no lake is near or the map has no water classes / is pre-v4.
        /// Lake water (LakeRim) always stands exactly at this Y. `nearest`: the
        /// distance (voxels) to the nearest lake cell's centre, exact up to 4
        /// cells (float.MaxValue when none is that near).
        public static unsafe int LakeRimY(in TerrainSpec spec, in MapView map, float x, float z, out float nearest)
        {
            nearest = float.MaxValue;
            if (map.waterClass == null || spec.mapVersion < 4) return int.MinValue;
            LakeNear(in map, x, z, 0f, out float rim, out _, out _, out nearest);
            return rim <= Hydrology.NO_WATER * 0.5f ? int.MinValue : spec.seaLevelY + (int)math.floor(rim);
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
