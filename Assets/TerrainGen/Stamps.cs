// Assets/TerrainGen/Stamps.cs
//
// §2.5's MESO FEATURES AND FANTASY LANDMARKS, as analytic stamps (the owner's
// rule: overhangs, arches and cliff lips come ONLY from stamps, never from a
// general 3D overhang field). One candidate per STAMP CELL (409.6 m, a power
// of two), placed in the cell's inner 40% so its footprint (<= 1,100 voxels)
// never leaves the cell -- a column consults exactly one cell. Three levels:
//
//   MAP  (WorldMap.Macro, 8 m cells; hydrology sees them; they count as
//        landmarks)     volcano + caldera, world tree, meteor craters
//   S    (ShapeField.Sample, analytic per column; dropped where the feature is
//        thinner than 2 texels, like a noise octave)
//                       arches, hoops, stone rings, fossils (skull + ribs),
//                       crystal clusters, geyser-basin pools; plus each MAP
//                       stamp's column materials and openings
//
// 2026-09-25 (owner: "I don't like the tall cone shaped things ... too
// unnatural"): the crystal SPIRES -- four map-level cones 40-100 m tall and
// 7-12 m wide -- became crystal CLUSTERS: 5-9 squat prisms 2.5-6 m tall on the
// ground, a thing to find rather than a tower. WorldMap's spires, narrow buttes
// and spike beacons went the same way (domes, terraced mesas, knolls).
//   D    (DetailField.Material, 3D, near the player only)
//                       cave mouths, fissures, the skull cave, the tree's root
//                       caves, the crater's glowing core, geyser vents, giant
//                       cavern mushrooms
//
// THE LOD-0 CONTRACT: an OPENING (arch, hoop, rib, the space under the tree's
// canopy) never includes a column's top voxel, so S's height stays the top
// solid voxel. Only cave mouths and fissures break the top; S flags those
// columns (ColumnSample.carvesTop), computed with the SAME inside-test D uses,
// and the LOD-0 oracle excludes exactly them.
//
// Heights: MAP functions work in voxels relative to sea level (the map's
// convention); S/D functions in absolute voxel Y.
using Unity.Mathematics;

namespace TerrainGen
{
    public static class Stamps
    {
        public const int CELL_SHIFT = 12;
        public const int CELL = 1 << CELL_SHIFT;        // 409.6 m

        public const byte None = 0, CaveMouth = 1, Fissure = 2, Arch = 3, StoneRing = 4, Hoop = 5,
                          CrystalCluster = 6, MeteorCrater = 7, Fossil = 8;
        public const byte Volcano = 20, WorldTree = 21, GeyserBasin = 22;

        public struct Stamp
        {
            public byte kind;
            public float2 pos, dir;       // voxels; unit axis
            public float a, b, c;         // [0,1) shape parameters
            public int gx, gz;
        }

        private static float H(int x, int y, int z, uint s) => Noise.Hash01(x, y, z, s);

        private static float2 HashDir(int gx, int gz, uint seed)
        {
            float2 v = new float2(H(gx, 40, gz, seed) - 0.5f, H(gx, 41, gz, seed) - 0.5f);
            float l = math.sqrt(v.x * v.x + v.y * v.y);
            return l > 1e-3f ? v / l : new float2(1f, 0f);
        }

        /// The (land-agnostic) candidate of stamp cell (gx, gz). Callers decide
        /// whether it is on land (MAP: the coast field; S/D: the map height).
        public static bool CellStamp(in Offsets o, int gx, int gz, out Stamp s)
        {
            s = default;
            float k = H(gx, 30, gz, o.seed + 201u);
            byte kind = k < 0.16f ? CaveMouth : k < 0.27f ? Fissure : k < 0.39f ? Arch : k < 0.47f ? StoneRing
                      : k < 0.53f ? Hoop : k < 0.65f ? CrystalCluster : k < 0.72f ? MeteorCrater : k < 0.78f ? Fossil : None;
            if (kind == None) return false;
            s.kind = kind; s.gx = gx; s.gz = gz;
            s.pos = new float2(gx + 0.3f + 0.4f * H(gx, 31, gz, o.seed + 202u),
                               gz + 0.3f + 0.4f * H(gx, 32, gz, o.seed + 203u)) * CELL;
            s.dir = HashDir(gx, gz, o.seed + 204u);
            s.a = H(gx, 33, gz, o.seed + 205u);
            s.b = H(gx, 34, gz, o.seed + 206u);
            s.c = H(gx, 35, gz, o.seed + 207u);
            return true;
        }

        public static bool StampAt(in Offsets o, float2 p, out Stamp s)
            => CellStamp(in o, (int)math.floor(p.x) >> CELL_SHIFT, (int)math.floor(p.y) >> CELL_SHIFT, out s);

        private static float2 Perp(float2 d) => new float2(-d.y, d.x);

        // ---- singleton sites: three directions around the island, away from the peak ----
        public static float2 VolcanoSite(in TerrainSpec spec, in Offsets o)
            => Centre(spec) + Perp(o.peakDir) * (spec.islandRadius * 0.46f);
        public static float2 TreeSite(in TerrainSpec spec, in Offsets o)
            => Centre(spec) - Perp(o.peakDir) * (spec.islandRadius * 0.40f);
        public static float2 GeyserSite(in TerrainSpec spec, in Offsets o)
            => Centre(spec) - o.peakDir * (spec.islandRadius * 0.42f);
        private static float2 Centre(in TerrainSpec spec) => new float2(spec.worldSizeVoxels * 0.5f, spec.worldSizeVoxels * 0.5f);

        // ================================================================
        // MAP LEVEL
        // ================================================================
        public const float VOLCANO_BASE_R = 9000f, VOLCANO_H = 3600f, CALDERA_R = 1500f, CALDERA_DEPTH = 700f, LAVA_DEPTH = 130f;
        public const float TREE_CANOPY_R = 1300f, TREE_TRUNK_R = 220f;

        /// Canopy top above the ground under it (voxels), for d < TREE_CANOPY_R.
        public static float TreeCanopyTop(float d) => 2500f + 500f * math.sqrt(math.max(0f, 1f - (d / TREE_CANOPY_R) * (d / TREE_CANOPY_R)));
        public static float TreeCanopyBottom(float d) => 2150f + 250f * math.sqrt(math.max(0f, 1f - (d / TREE_CANOPY_R) * (d / TREE_CANOPY_R)));

        /// The volcano's cone height above sea (0 outside it) and whether p is
        /// on the caldera's flat floor (its lava lake).
        public static float VolcanoHeight(in TerrainSpec spec, in Offsets o, float2 p, out bool caldera, out float lavaLevel)
        {
            float2 q = p - VolcanoSite(in spec, in o);
            float d = math.sqrt(q.x * q.x + q.y * q.y);
            caldera = false;
            float rimH = 200f + VOLCANO_H * math.pow(1f - CALDERA_R / VOLCANO_BASE_R, 1.3f);
            float floorH = rimH - CALDERA_DEPTH;
            lavaLevel = floorH + LAVA_DEPTH;
            if (d >= VOLCANO_BASE_R) return 0f;
            if (d > CALDERA_R) return 200f + VOLCANO_H * math.pow(1f - d / VOLCANO_BASE_R, 1.3f);
            if (d > CALDERA_R * 0.86f)
            {
                float t = (d - CALDERA_R * 0.86f) / (CALDERA_R * 0.14f);   // steep inner wall
                return floorH + (rimH - floorH) * t * t * (3f - 2f * t);
            }
            caldera = true;
            return floorH;
        }

        /// Everything the map adds for stamps at p. `hgt` is the map height so
        /// far (voxels rel. sea); returns the new height. `edge` feeds the
        /// roughness channel (sharp stamps get less fine noise).
        public static float MacroApply(in TerrainSpec spec, in Offsets o, float2 p, float hgt, float inland, ref float edge)
        {
            // Volcano: the cone replaces lower terrain; the caldera floor is flat.
            float v = VolcanoHeight(in spec, in o, p, out bool caldera, out _);
            if (caldera) hgt = v;
            else if (v > 0f)
            {
                // The cone REPLACES the ranges toward its summit (fully inside the
                // inner half of its base radius, blending out to max() at the base):
                // a range poking up through the cone made horns on the flanks and
                // notched the rim (2026-09-25, StampTests.NoNeedles).
                float2 vq = p - VolcanoSite(in spec, in o);
                float w = math.saturate((VOLCANO_BASE_R - math.sqrt(vq.x * vq.x + vq.y * vq.y)) / (0.5f * VOLCANO_BASE_R));
                hgt = math.max(v, math.lerp(hgt, v, w));
            }

            // World tree: its canopy is added by WorldMap.TreeCanopy AFTER the
            // summit rounding, over the rounded ground. Built here, a horn under
            // the tree was lifted 300 m into the canopy (seed 7) and the rounding
            // had to leave the whole tree alone.
            float2 tq = p - TreeSite(in spec, in o);
            float td = math.sqrt(tq.x * tq.x + tq.y * tq.y);

            // Singletons own their areas at the map level too (see Column).
            float2 gq = p - GeyserSite(in spec, in o);
            if (v > 0f || td < TREE_CANOPY_R + 200f || gq.x * gq.x + gq.y * gq.y < 1400f * 1400f) return hgt;
            if (inland <= 0.2f || !StampAt(in o, p, out Stamp s)) return hgt;
            float2 q = p - s.pos;
            float dist = math.sqrt(q.x * q.x + q.y * q.y);
            if (s.kind == MeteorCrater)
            {
                // A bowl with a raised rim; hydrology turns deep bowls into crater lakes.
                float R = 500f + 400f * s.a, D = 150f + 150f * s.b, rim = 0.35f * D;
                float x = dist / R;
                if (x < 1f) { edge = math.max(edge, 0.6f); return hgt - D * (1f - x * x) + rim * x * x * x * x; }
                if (x < 1.8f) { float t = 1f - (x - 1f) / 0.8f; return hgt + rim * t * t; }
            }
            return hgt;
        }

        // ================================================================
        // S LEVEL: per column
        // ================================================================
        /// Fills the column's stamp fields and returns its new top (absolute Y).
        /// `ground` is the column's top before this stamp (absolute Y);
        /// `macroH` the map height (rel. sea) used for the MAP stamps' materials.
        public static unsafe void Column(in TerrainSpec spec, in MapView map, in Offsets o, float2 p, int texel, ref ColumnSample c)
        {
            c.open1Lo = c.open1Hi = c.open2Lo = c.open2Hi = 0;
            int ground = c.height;

            // ---- volcano (map): its rock, its lava lake ----
            float vh = VolcanoHeight(in spec, in o, p, out bool caldera, out float lava);
            if (vh > 0f)
            {
                c.stampMat = GenMat.VolcanicRock;
                c.stampBodyLo = c.height - 600;       // 60 m of volcanic rock under the cone's surface
                if (caldera) { c.water = spec.seaLevelY + (int)math.floor(lava); c.waterKind = 1; }
            }

            // ---- world tree (map height; S gives its trunk, canopy, the void below) ----
            float2 tq = p - TreeSite(in spec, in o);
            float td = math.sqrt(tq.x * tq.x + tq.y * tq.y);
            if (td < TREE_CANOPY_R)
            {
                c.water = ColumnSample.NO_WATER_Y;   // no map river across the leaves
                int top = c.height;
                int groundY = top - (int)math.floor(TreeCanopyTop(td));
                if (td < TREE_TRUNK_R)
                {
                    c.stampMat = GenMat.Bark; c.stampBodyLo = groundY + 1;
                }
                else if (td < TREE_CANOPY_R - 60f)
                {
                    int bottom = groundY + (int)math.floor(TreeCanopyBottom(td));
                    c.open1Lo = groundY + 1;
                    c.open1Hi = math.min(bottom, top - 40);        // >= 4 m of canopy always stays
                    c.stampMat = GenMat.Leaves; c.stampBodyLo = c.open1Hi;
                }
                else { c.stampMat = GenMat.Leaves; c.stampBodyLo = groundY + 1; }
                c.stamp3D = td < TREE_TRUNK_R * 3.5f;   // root caves
            }

            // ---- geyser basin (singleton): a flat sinter terrace with pools ----
            float2 gq = p - GeyserSite(in spec, in o);
            float gd = math.sqrt(gq.x * gq.x + gq.y * gq.y);
            if (gd < 1200f)
            {
                SampleSiteHeight(in spec, in map, GeyserSite(in spec, in o), out int h0);
                float w = math.saturate((1200f - gd) / 300f);
                int flat = (int)math.round(math.lerp(c.height, h0, w));
                if (h0 > spec.seaLevelY + 20)
                {
                    c.height = flat;
                    // The basin owns its water: a map lake or river computed for
                    // the UNflattened ground would stand above the terrace.
                    c.water = ColumnSample.NO_WATER_Y;
                    if (w >= 1f)
                    {
                        c.stampMat = GenMat.Sinter; c.stampBodyLo = flat - 12;
                        for (int j = 0; j < 9; j++)
                        {
                            float2 pc = PoolCentre(in spec, in o, j);
                            float pr = 50f + 60f * H(j, 92, 1, o.seed + 222u);
                            float2 dq = p - pc;
                            if (dq.x * dq.x + dq.y * dq.y < pr * pr)
                            {
                                c.height = flat - 25;
                                c.water = flat - 5;            // below the terrace: contained, at rest
                                c.waterKind = 0;
                                c.stamp3D = true;              // vents at pool centres
                                break;
                            }
                        }
                    }
                }
            }

            // The singletons OWN their areas: no cell stamp inside the volcano's
            // cone, the tree's canopy or the geyser basin (a cell stamp there
            // would build on the ground as it was BEFORE the singleton reshaped it).
            if (vh > 0f || td < TREE_CANOPY_R + 200f || gd < 1400f) return;
            if (!StampAt(in o, p, out Stamp s)) return;
            float2 q = p - s.pos;
            float along = math.dot(q, s.dir), side = math.dot(q, Perp(s.dir));
            float dist = math.sqrt(q.x * q.x + q.y * q.y);

            if (s.kind == MeteorCrater)
            {
                if (dist < 300f)
                {
                    c.stamp3D = true;                    // the glowing core under the bowl
                    SampleSiteHeight(in spec, in map, s.pos, out c.stampGround);
                }
                return;
            }
            // Everything below needs dry land at the stamp.
            SampleSiteHeight(in spec, in map, s.pos, out int siteGround);
            if (siteGround <= spec.seaLevelY + 30) return;

            if (s.kind == CaveMouth || s.kind == Fissure)
            {
                if (dist > 1100f) return;
                c.stamp3D = true;
                c.stampGround = siteGround;
                c.carvesTop = SurfaceCarve(in s, siteGround, (int)math.floor(p.x), c.height, (int)math.floor(p.y), c.height);
                return;
            }
            // The remaining S stamps are thin (30-60 voxels): dropped below 2 texels.
            if (texel > 16) return;

            if (s.kind == CrystalCluster)
            {
                // 5-9 squat prisms within 18 m of the site, each 1.5-3 m across and
                // 2.5-6 m tall, standing on the column's own ground, gently domed
                // on top. Coloured by biome.
                if (dist > 220f) return;
                int count = 5 + (int)(5f * s.a);
                float best = 0f;
                for (int i = 0; i < count; i++)
                {
                    float2 pc = s.pos + new float2(H(s.gx * 16 + i, 96, s.gz, o.seed + 231u) - 0.5f,
                                                   H(s.gx * 16 + i, 97, s.gz, o.seed + 232u) - 0.5f) * 360f;
                    float pr = 15f + 15f * H(s.gx * 16 + i, 98, s.gz, o.seed + 233u);
                    float2 dq = p - pc;
                    float dd = math.sqrt(dq.x * dq.x + dq.y * dq.y);
                    if (dd >= pr) continue;
                    float ph = 25f + 35f * H(s.gx * 16 + i, 99, s.gz, o.seed + 234u);
                    float xr = dd / pr;
                    best = math.max(best, ph * (1f - 0.35f * xr * xr));
                }
                if (best < 1f) return;
                c.height = ground + (int)math.floor(best);
                c.stampMat = CrystalFor(c.biome); c.stampBodyLo = ground + 1;
                return;
            }

            if (s.kind == Arch)
            {
                float L = 300f + 200f * s.a, Hh = 150f + 150f * s.b, T = 45f, W = 90f, leg = 60f;
                if (math.abs(side) >= W * 0.5f || math.abs(along) >= L * 0.5f) return;
                int top = ground + (int)Hh;
                float half = L * 0.5f - leg;
                if (math.abs(along) < half)
                {
                    float u = along / half;
                    c.open1Lo = ground + 1;
                    c.open1Hi = ground + 1 + (int)math.floor((Hh - T) * math.sqrt(1f - u * u));
                }
                c.height = top;
                c.stampMat = 0;          // natural rock: D's own geology
                return;
            }
            if (s.kind == Hoop)
            {
                float Ro = 120f + 80f * s.a, th = 30f, W = 40f, cy = Ro - 20f;
                if (math.abs(side) >= W * 0.5f || math.abs(along) >= Ro) return;
                float outer = math.sqrt(Ro * Ro - along * along);
                c.height = ground + (int)math.floor(cy + outer);
                int lower = ground + (int)math.floor(cy - outer);
                if (lower > ground + 1) { c.open2Lo = ground + 1; c.open2Hi = lower; }
                float Ri = Ro - th;
                if (math.abs(along) < Ri)
                {
                    float inner = math.sqrt(Ri * Ri - along * along);
                    c.open1Lo = ground + (int)math.floor(cy - inner);
                    c.open1Hi = ground + (int)math.floor(cy + inner);
                }
                c.stampMat = GenMat.StandingStone; c.stampBodyLo = ground + 1;
                return;
            }
            if (s.kind == StoneRing)
            {
                float R = 160f + 80f * s.a;
                if (dist > R + 40f || dist < R - 40f) return;
                // 12 stones, evenly spaced, from the rational points of the circle
                // (no sin/cos): t = tan(theta/2) for theta = 0, 30, 60 degrees,
                // replicated to the four quadrants.
                for (int i = 0; i < 12; i++)
                {
                    float t = (i % 3) == 0 ? 0f : ((i % 3) == 1 ? 0.267949f : 0.577350f);
                    float2 u = new float2((1f - t * t) / (1f + t * t), 2f * t / (1f + t * t));
                    int quad = i / 3;
                    for (int r = 0; r < quad; r++) u = Perp(u);
                    float2 l = q - u * R;
                    float rad = math.dot(l, u), tan = math.dot(l, Perp(u));
                    if (math.abs(rad) < 14f && math.abs(tan) < 22f)
                    {
                        c.height = ground + 70 + (int)(40f * H(s.gx * 16 + i, 95, s.gz, o.seed + 230u));
                        c.stampMat = GenMat.StandingStone; c.stampBodyLo = ground + 1;
                        return;
                    }
                }
                return;
            }
            if (s.kind == Fossil)
            {
                // Skull: a bone dome; ribs behind it along +dir, each a half-hoop
                // across the axis; a spine ridge between them.
                float Rs = 200f;
                if (dist < Rs)
                {
                    float x = dist / Rs;
                    c.height = ground + (int)math.floor(180f * math.sqrt(1f - x * x));
                    c.stampMat = GenMat.Bone; c.stampBodyLo = ground + 1;
                    c.stamp3D = true;                    // the skull cave
                    c.stampGround = siteGround;
                    c.carvesTop = SurfaceCarve(in s, siteGround, (int)math.floor(p.x), c.height, (int)math.floor(p.y), c.height);
                    return;
                }
                if (along < Rs || along > Rs + 800f || math.abs(side) > 240f) return;
                int k = (int)math.floor((along - Rs - 60f) / 110f);
                float ribT = Rs + 60f + 110f * k + 55f;
                float Rr = 230f - 18f * k;
                if (k >= 0 && k < 7 && math.abs(along - ribT) < 16f && math.abs(side) < Rr)
                {
                    float outer = math.sqrt(Rr * Rr - side * side);
                    c.height = ground + (int)math.floor(outer);
                    float Ri = Rr - 32f;
                    if (math.abs(side) < Ri) { c.open1Lo = ground + 1; c.open1Hi = ground + (int)math.floor(math.sqrt(Ri * Ri - side * side)); }
                    c.stampMat = GenMat.Bone; c.stampBodyLo = ground + 1;
                }
                else if (math.abs(side) < 22f)
                {
                    c.height = ground + 40;              // spine
                    c.stampMat = GenMat.Bone; c.stampBodyLo = ground + 1;
                }
            }
        }

        /// Geyser pool j's centre: inside +-420 of the site, so every pool (radius
        /// <= 110) lies wholly on the basin's FLAT terrace (w == 1 within 900).
        public static float2 PoolCentre(in TerrainSpec spec, in Offsets o, int j)
            => GeyserSite(in spec, in o) + new float2(H(j, 90, 1, o.seed + 220u) - 0.5f, H(j, 91, 1, o.seed + 221u) - 0.5f) * 840f;

        private static byte CrystalFor(byte biome) => biome == GenBiome.Snow ? GenMat.CrystalBlue
            : biome == GenBiome.Desert ? GenMat.CrystalAmber : biome == GenBiome.Jungle ? GenMat.CrystalGreen : GenMat.CrystalRose;

        /// The site's ground (absolute Y) from the MAP alone (no detail octaves):
        /// cheap, and the same wherever it is evaluated from.
        public static unsafe void SampleSiteHeight(in TerrainSpec spec, in MapView map, float2 site, out int y)
        {
            ShapeField.SampleMap(in map, site.x, site.y, out float h, out _);
            y = spec.seaLevelY + (int)math.floor(h);
        }
        private static unsafe int SiteGround(in TerrainSpec spec, in MapView map, float2 site)
        { SampleSiteHeight(in spec, in map, site, out int y); return y; }

        // ================================================================
        // D LEVEL: 3D
        // ================================================================
        /// Is (x, y, z) inside a cave-mouth / fissure / skull-cave opening of
        /// stamp s? `siteGround` is the stamp site's map ground (absolute Y, S
        /// stores it in ColumnSample.stampGround); `colTop` the column's top.
        /// ONE function for S's carvesTop and D's carve, so the LOD-0 exclusion
        /// is exact.
        public static bool SurfaceCarve(in Stamp s, int siteGround, int x, int y, int z, int colTop)
        {
            float3 v = new float3(x + 0.5f, y + 0.5f, z + 0.5f);
            if (s.kind == CaveMouth)
            {
                // An inclined tube from the surface, 30-40 degrees down, 45-70 voxels radius.
                int g = siteGround;
                float r = 45f + 25f * s.a;
                float3 e = new float3(s.pos.x, g + r * 0.35f, s.pos.y);
                float3 d = math.normalize(new float3(s.dir.x, -(0.6f + 0.25f * s.b), s.dir.y));
                float t = math.clamp(math.dot(v - e, d), 0f, 1000f);
                float3 closest = e + d * t;
                float3 off = v - closest;
                return math.dot(off, off) < r * r;
            }
            if (s.kind == Fissure)
            {
                // A narrow slot along the stamp axis, 12-25 voxels wide at the
                // middle, pinching out at the ends, 30-50 m deep below the column top.
                float2 q = new float2(v.x, v.z) - s.pos;
                float along = math.dot(q, s.dir), side = math.dot(q, Perp(s.dir));
                float L = 350f + 250f * s.a;
                if (math.abs(along) >= L) return false;
                float u = along / L;
                float w = (12f + 13f * s.b) * (1f - u * u);
                float depth = 300f + 200f * s.c;
                return math.abs(side) < w * 0.5f && y > colTop - depth;
            }
            if (s.kind == Fossil)
            {
                // The skull cave: a hollow dome with a mouth facing away from the ribs.
                int g = siteGround;
                float3 c = new float3(s.pos.x, g + 70f, s.pos.y);
                float3 dv = v - c;
                if (dv.x * dv.x / (130f * 130f) + dv.y * dv.y / (80f * 80f) + dv.z * dv.z / (130f * 130f) < 1f) return true;
                float2 q = new float2(v.x, v.z) - s.pos;
                float along = math.dot(q, -s.dir), side = math.dot(q, Perp(s.dir));
                return along > 0f && along < 260f && math.abs(side) < 45f && v.y > g + 15f && v.y < g + 90f;
            }
            return false;
        }

        /// D's 3D stamp pass, for voxels in columns S flagged stamp3D (and the
        /// cavern layer's mushrooms). 255 = no stamp here.
        public static byte Carve3D(in TerrainSpec spec, in Offsets o, in ColumnSample c, int x, int y, int z)
        {
            float2 p = new float2(x + 0.5f, z + 0.5f);
            if (c.stamp3D)
            {
                // World tree root caves: a shaft down the trunk, four radiating tunnels.
                float2 tq = p - TreeSite(in spec, in o);
                float td2 = tq.x * tq.x + tq.y * tq.y;
                if (td2 < (TREE_TRUNK_R * 3.5f) * (TREE_TRUNK_R * 3.5f))
                {
                    int groundY = c.height - (int)math.floor(TreeCanopyTop(math.sqrt(td2)));
                    if (td2 < 60f * 60f && y > groundY - 110 && y < groundY + 60) return GenMat.Air;       // shaft
                    if (y > groundY - 110 && y < groundY - 50)
                    {
                        float ax = math.abs(tq.x), az = math.abs(tq.y);
                        if ((ax < 35f || az < 35f) && td2 < (TREE_TRUNK_R * 3.4f) * (TREE_TRUNK_R * 3.4f)) return GenMat.Air; // tunnels
                    }
                    float2 door = tq;   // a door in the trunk, facing +x
                    if (door.x > 0f && math.abs(door.y) < 40f && y > groundY && y < groundY + 60 && td2 < TREE_TRUNK_R * TREE_TRUNK_R) return GenMat.Air;
                }
                // Geyser vents: a narrow water-filled shaft under each pool centre.
                float2 gq = p - GeyserSite(in spec, in o);
                if (gq.x * gq.x + gq.y * gq.y < 1200f * 1200f)
                {
                    for (int j = 0; j < 9; j += 3)
                    {
                        float2 pc = PoolCentre(in spec, in o, j);
                        float2 dq = p - pc;
                        if (dq.x * dq.x + dq.y * dq.y < 8f * 8f && y <= c.height && y > c.height - 220) return GenMat.Water;
                    }
                }
                if (StampAt(in o, p, out Stamp s))
                {
                    if (s.kind == CaveMouth || s.kind == Fissure || s.kind == Fossil)
                        if (SurfaceCarve(in s, c.stampGround, x, y, z, c.height)) return GenMat.Air;
                    if (s.kind == MeteorCrater)
                    {
                        // The glowing core: an ore sphere just under the bowl's centre.
                        int g = c.stampGround;
                        float3 dv = new float3(x + 0.5f - s.pos.x, y + 0.5f - (g - 30f), z + 0.5f - s.pos.y);
                        if (math.dot(dv, dv) < 70f * 70f) return GenMat.MeteorOre;
                    }
                }
            }
            return 255;
        }

        /// Giant mushrooms in the cavern layer (§2.5): one candidate per 1,024^3
        /// cell, kept only where the cavern field is open at its cap. Solid.
        public const int MUSHROOM_CELL_SHIFT = 10;
        public static byte Mushroom(in TerrainSpec spec, in Offsets o, int x, int y, int z)
        {
            if (y >= spec.CavernTopY || y < spec.UnderworldTopY) return 255;
            int cx = x >> MUSHROOM_CELL_SHIFT, cy = y >> MUSHROOM_CELL_SHIFT, cz = z >> MUSHROOM_CELL_SHIFT;
            if (H(cx, cy, cz, o.seed + 240u) > 0.55f) return 255;
            float3 cap = new float3(cx + 0.35f + 0.3f * H(cx, cy, cz, o.seed + 241u),
                                    cy + 0.45f + 0.2f * H(cx, cy, cz, o.seed + 242u),
                                    cz + 0.35f + 0.3f * H(cx, cy, cz, o.seed + 243u)) * (1 << MUSHROOM_CELL_SHIFT);
            float3 v = new float3(x + 0.5f, y + 0.5f, z + 0.5f) - cap;
            if (math.abs(v.x) > 300f || math.abs(v.z) > 300f || v.y > 90f || v.y < -420f) return 255;
            // Only where the cavern is open at the cap (evaluated once per cell).
            float cav = noise.snoise(cap / 1500f + o.cavern) + 0.3f * noise.snoise(cap / 450f + o.cavern + 20f);
            if (cav <= 0.66f) return 255;
            float capR = 220f + 60f * H(cx, cy, cz, o.seed + 244u);
            float r2 = v.x * v.x + v.z * v.z;
            if (v.y > 0f && v.y < 80f && r2 < capR * capR * (1f - v.y / 90f)) return GenMat.MushroomCap;
            if (v.y <= 0f && r2 < 38f * 38f) return GenMat.MushroomStem;
            return 255;
        }
    }
}
