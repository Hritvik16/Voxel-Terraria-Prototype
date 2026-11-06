// Assets/TerrainGen/DetailField.cs
//
// D(x, y, z) -- THE DETAIL FIELD (§4.1). 3D, run only for resident near chunks,
// on top of S. The far band never calls it.
//
//   above the column   air, or sea water up to sea level (static, at rest)
//   surface / soil     per biome; steep faces expose the strata (cliffs read
//                      as geology, §2.6); sand and powder snow only where the
//                      top is STABLE AT REST (no neighbour > 1 voxel lower)
//   rock layers        THICK LAYERS BY DEPTH (2026-10-01, owner: "I don't like
//                      the underground bands"; game-design brief, Underground
//                      materials), each biome with its own rock in every layer:
//                        stone        surface down to sea - 50 m (+-8 m wavy)
//                        deep stone   down to sea - 150 m (the cavern top)
//                        cavern rock  down to sea - 400 m (the underworld top)
//                      The thin striped strata (5 bands cycling every 16.5 m)
//                      are gone: they were a default nobody chose and they made
//                      most underground bricks mixed (dense).
//   underworld         below sea - 400 m, SHARED by every biome: ash (top 30 m),
//                      obsidian, magma crust (20 m above the lava); an open
//                      lava sea at rest
//   caves              STRUCTURED (v3 T4, 2026-09-26; owner: no 3-D noise):
//                      three layers of meandering tunnels (the zero-lines of
//                      2-D fields, at depths set by 2-D fields; one layer
//                      follows the terrain 18-30 m down), placed caverns with a
//                      2-D floor and a domed ceiling, the underworld chamber
//                      between a 2-D floor and ceiling with pillars. All of it
//                      is per-COLUMN open intervals (PrepareColumn); a voxel
//                      costs a few comparisons.
//   ores               map v4 (gameplay step 3, Ores.cs): integer-hash blobs
//                      in layer rock deeper than 6 m, one ore per layer
//   fluid pockets      sealed ellipsoids, generated AT REST: fluid below a flat
//                      level, air above; a shell no cave may breach (§2.7)
//
// THE LOD-0 CONTRACT (S's header): nothing here carves within CARVE_MIN_DEPTH of
// the column top, so the top solid voxel is always S's height. Surface cave
// mouths and fissures (§2.5) will break that for their own columns; the oracle
// then excludes exactly those columns, by construction of the stamp.
using Unity.Mathematics;

namespace TerrainGen
{
    public static class DetailField
    {
        public const int CARVE_MIN_DEPTH = 60;        // 6 m
        public const int SURFACE_THICKNESS = 3;       // 0.3 m of grass/sand/snow
        public const int POCKET_CELL_SHIFT = 9;       // one pocket candidate per 51.2 m cube
        public const int POCKET_CELL = 1 << POCKET_CELL_SHIFT;
        public const int POCKET_MAX_TOP_BELOW_SEA = 1000; // pockets sit >= 100 m below sea:
                                                          // below the deepest ocean floor (90 m)

        /// Everything D needs that varies per COLUMN, not per voxel: the 2D
        /// noise terms of the strata, soil, cavern and underworld bands. A chunk
        /// generator computes it once per column (PrepareColumn) and calls the
        /// overload below for each voxel; the result is identical by construction.
        public struct ColumnDetail
        {
            public float deepOffset;      // added to y at the stone / deep-stone boundary (wavy)
            public float cavernOffset;
            public float underOffset;
            /// Task 178: added to y at the stone zone's thick rock layers (LayerWave;
            /// 0 when TerrainSpec.Cliffs is off).
            public float layerOffset;
            public int soilDepth;
            // ---- T4 structured caves, per column ----
            /// Tunnel layer k: |line field| / width (a passage needs < 1), the
            /// passage centre (layer 0: DEPTH below the column top; layers 1-2:
            /// absolute Y) and its half height. 99 = no passage here.
            public float tun0, tun1, tun2, tunY0, tunY1, tunY2, tunH0, tunH1, tunH2;
            /// Layer 3 (T5): DEPTH below the column's REGIONAL ground, so it breaks
            /// out through valley walls and cliffs.
            public float tun3, tunY3, tunH3;
            /// Up to two cavern open intervals [lo, hi), absolute Y (lo == hi: none).
            public int cav0Lo, cav0Hi, cav1Lo, cav1Hi;
            /// The underworld chamber's open interval [lo, hi) (lo == hi: a pillar).
            public int underLo, underHi;
            /// Gameplay step 3: ore density multiplier in 1/16ths (Ores.MUL_ONE = x1),
            /// the column's biome x landform pair (OreTable.PairMul). PrepareColumn sets
            /// x1 on map v4 and 0 (no ore) below; a generator holding the world record
            /// overwrites it from OreTable.DensityPlane.
            public byte oreMul;
        }

        /// Amplitudes of the wavy layer-boundary offsets (PrepareGeology).
        public const float GEO_DEEP_AMP = 80f, GEO_CAVERN_AMP = 25f, GEO_UNDER_AMP = 20f;

        /// The geology terms only (layer boundary offsets, soil): what the
        /// surface and LayerRock need. No caves.
        private static ColumnDetail PrepareGeology(in Offsets o, int x, int z, bool layers)
        {
            float2 p = new float2(x, z);
            // Each layer boundary is a wavy surface: the offset shifts the
            // boundary's height, column by column.
            var cd = new ColumnDetail();
            cd.deepOffset = GEO_DEEP_AMP * noise.snoise(p / 2600f + o.strata);
            cd.cavernOffset = GEO_CAVERN_AMP * noise.snoise(p / 3000f + o.strata + 90f);
            cd.underOffset = GEO_UNDER_AMP * noise.snoise(p / 1800f + o.strata - 70f);
            float n = noise.snoise(p / 300f + o.soil) * 0.5f + 0.5f;
            cd.soilDepth = 12 + (int)math.floor(n * 12f);
            if (layers) cd.layerOffset = LayerWave(in o, p);
            return cd;
        }

        // ---- T4 constants ----
        public const float TUNNEL_SCALE = 2200f;      // voxels: meander wavelength of a passage
        public const float TUNNEL_WIDTH = 0.012f;     // line-field half width (~2-3 m passages)
        public const int CAVERN_CELL_SHIFT = 10;      // one cavern candidate per 102.4 m cell
        public const int CAVERN_CELL = 1 << CAVERN_CELL_SHIFT;
        public const int CAVERN_CELL_Y = 500;
        public const int LAVA_ABOVE_FLOOR = 300;      // the lava sea's level above the world floor

        /// Everything D needs per COLUMN: the geology terms and every cave's
        /// open interval through this column. Computed once per column.
        ///
        /// TASK 86 (owner Q45 (a)): COARSE CAVE FIELDS. The cave terms are 25
        /// 2-D noises with wavelengths of 200-6,000 voxels, ~0.85 of this
        /// function's cost (step 0: PrepareColumn was 61.7% of the column pass,
        /// Logs/TerrainGen/column_pass_split.txt). With spec.coarseColumnShift
        /// s > 0 they are evaluated only at the nodes of an ABSOLUTE 2^s grid
        /// (CaveTerms; nodes at columns = 2^s - 1 mod 2^s) and interpolated bilinearly to the column; the cheap
        /// non-linear steps (abs, the gates, the cavern hashes) stay per column.
        /// The geology, soil and ore terms are exact per column, so strata
        /// boundaries do not move. A pure function of (x, z): every caller (0.1 m
        /// chunks, shells, shell fill, far edits) agrees, with or without a
        /// NodeCache. s = 0 = per column, byte-identical to the pre-86 code
        /// (CoarseColumnTests measure the boundary shift against it).
        public static ColumnDetail PrepareColumn(in TerrainSpec spec, in Offsets o, int x, int z)
        {
            int s = spec.coarseColumnShift;
            if (s <= 0) return FinishColumn(in spec, in o, CaveTerms(in o, new float2(x, z)), x, z);
            int m = (1 << s) - 1, xr = x - m, zr = z - m, nx = xr >> s, nz = zr >> s;
            float inv = 1f / (1 << s), fx = (xr & m) * inv, fz = (zr & m) * inv;
            var n00 = CaveTerms(in o, nx, nz, s);
            var n10 = (xr & m) != 0 ? CaveTerms(in o, nx + 1, nz, s) : n00;   // weight 0: never read
            var n01 = (zr & m) != 0 ? CaveTerms(in o, nx, nz + 1, s) : n00;
            var n11 = (xr & m) != 0 && (zr & m) != 0 ? CaveTerms(in o, nx + 1, nz + 1, s) : n00;
            return FinishColumn(in spec, in o, Bilerp(in n00, in n10, in n01, in n11, fx, fz), x, z);
        }

        /// The same, reading the grid nodes through `cache` (same bytes as the
        /// uncached form; a loop over neighbouring columns evaluates each node
        /// about once instead of up to four times).
        public static unsafe ColumnDetail PrepareColumn(in TerrainSpec spec, in Offsets o, int x, int z, ref NodeCache cache)
        {
            int s = spec.coarseColumnShift;
            fixed (CavernCells* cav = &cache.Caverns)
            {
                var cc = spec.cavernCellCacheOff != 0 ? null : cav;   // task 88's A arm: uncached
                if (s <= 0) return FinishColumn(in spec, in o, CaveTerms(in o, new float2(x, z)), x, z, cc);
                int m = (1 << s) - 1, xr = x - m, zr = z - m, nx = xr >> s, nz = zr >> s;
                float inv = 1f / (1 << s), fx = (xr & m) * inv, fz = (zr & m) * inv;
                var n00 = cache.Get(in o, nx, nz, s);
                var n10 = (xr & m) != 0 ? cache.Get(in o, nx + 1, nz, s) : n00;
                var n01 = (zr & m) != 0 ? cache.Get(in o, nx, nz + 1, s) : n00;
                var n11 = (xr & m) != 0 && (zr & m) != 0 ? cache.Get(in o, nx + 1, nz + 1, s) : n00;
                return FinishColumn(in spec, in o, Bilerp(in n00, in n10, in n01, in n11, fx, fz), x, z, cc);
            }
        }

        /// The pre-86 per-column reference (every cave term exact at the column),
        /// whatever spec.coarseColumnShift says. Tests and reports only.
        public static ColumnDetail PrepareColumnExact(in TerrainSpec spec, in Offsets o, int x, int z)
            => FinishColumn(in spec, in o, CaveTerms(in o, new float2(x, z)), x, z);

        /// The raw smooth cave noises at one point, before any abs / gate /
        /// threshold (those do not interpolate). Tunnel layer k: (half height,
        /// gate, width, line) Fbm values; ty: the four depth Fbms; cv: cavern
        /// nR, nF, underworld pillar, floor; uHi: underworld ceiling.
        public struct CaveNode
        {
            public float4 t0, t1, t2, t3, ty, cv;
            public float uHi, pad0, pad1, pad2;
            public const int FLOATS = 28;
        }

        /// Node (nx, nz) of the 2^s grid sits at column ((nx << s) + 2^s - 1, ..):
        /// offset so that shell k > s's columns (origin + (i << k) + 2^(k-1) - 1,
        /// TerrainGenChunkSource.Shell.cs) fall ON nodes and need one node each.
        private static CaveNode CaveTerms(in Offsets o, int nx, int nz, int s)
            => CaveTerms(in o, new float2((nx << s) + (1 << s) - 1, (nz << s) + (1 << s) - 1));

        private static CaveNode CaveTerms(in Offsets o, float2 p)
        {
            var n = new CaveNode();
            n.t0 = TunnelTerms(p, o.caveA.xy, o.caveA.z);
            n.t1 = TunnelTerms(p, o.caveB.xy, o.caveB.z);
            n.t2 = TunnelTerms(p, o.cavern.xy, o.cavern.z);
            n.t3 = TunnelTerms(p, o.under.xy + 43f, o.under.z);
            n.ty = new float4(Noise.Fbm(p / 3500f + o.caveA.xy + 11f, 2), Noise.Fbm(p / 4000f + o.caveB.xy + 13f, 2),
                              Noise.Fbm(p / 4000f + o.cavern.xy + 17f, 2), Noise.Fbm(p / 3000f + o.under.xy + 47f, 2));
            n.cv = new float4(Noise.Fbm(p / 400f + o.cavern.xy + 23f, 2), Noise.Fbm(p / 200f + o.cavern.xy + 29f, 2),
                              Noise.Fbm(p / 1800f + o.under.xy + 31f, 3), Noise.Fbm(p / 3000f + o.under.xy + 37f, 3));
            n.uHi = Noise.Fbm(p / 2200f + o.under.xy + 41f, 3);
            return n;
        }

        private static float4 TunnelTerms(float2 p, float2 off, float seedZ)
            => new float4(Noise.Fbm(p / 1500f + off + seedZ + 5f, 2), Noise.Fbm(p / 6000f + off + seedZ + 3f, 2),
                          Noise.Fbm(p / 2500f + off + seedZ + 7f, 2), Noise.Fbm(p / TUNNEL_SCALE + off + seedZ, 3));

        private static CaveNode Bilerp(in CaveNode a, in CaveNode b, in CaveNode c, in CaveNode d, float fx, float fz)
        {
            var n = new CaveNode();
            n.t0 = math.lerp(math.lerp(a.t0, b.t0, fx), math.lerp(c.t0, d.t0, fx), fz);
            n.t1 = math.lerp(math.lerp(a.t1, b.t1, fx), math.lerp(c.t1, d.t1, fx), fz);
            n.t2 = math.lerp(math.lerp(a.t2, b.t2, fx), math.lerp(c.t2, d.t2, fx), fz);
            n.t3 = math.lerp(math.lerp(a.t3, b.t3, fx), math.lerp(c.t3, d.t3, fx), fz);
            n.ty = math.lerp(math.lerp(a.ty, b.ty, fx), math.lerp(c.ty, d.ty, fx), fz);
            n.cv = math.lerp(math.lerp(a.cv, b.cv, fx), math.lerp(c.cv, d.cv, fx), fz);
            n.uHi = math.lerp(math.lerp(a.uHi, b.uHi, fx), math.lerp(c.uHi, d.uHi, fx), fz);
            return n;
        }

        /// A small direct-mapped cache of grid nodes, owned by one loop (a job's
        /// local; never shared). default = empty. 128 x 2 slots: a row-major
        /// sweep over up to 128 node columns evaluates each node once.
        public unsafe struct NodeCache
        {
            public const int SLOTS = 256;
            private fixed int _kx[SLOTS];
            private fixed int _kz[SLOTS];
            private fixed byte _used[SLOTS];
            private fixed float _v[SLOTS * CaveNode.FLOATS];
            /// Node evaluations since the cache was made (diagnostics).
            public int Misses;
            /// Task 88: the cavern candidates of the last cells seen.
            public CavernCells Caverns;

            public CaveNode Get(in Offsets o, int nx, int nz, int s)
            {
                int slot = (nx & 127) | ((nz & 1) << 7);
                fixed (float* v = _v)
                {
                    var p = (CaveNode*)(v + slot * CaveNode.FLOATS);
                    if (_used[slot] != 0 && _kx[slot] == nx && _kz[slot] == nz) return *p;
                    *p = CaveTerms(in o, nx, nz, s);
                    _kx[slot] = nx; _kz[slot] = nz; _used[slot] = 1; Misses++;
                    return *p;
                }
            }
        }

        /// The cheap per-column half: geology and soil exact, the cave terms
        /// from `n` (exact or interpolated), the cavern cell hashes exact.
        private static ColumnDetail FinishColumn(in TerrainSpec spec, in Offsets o, in CaveNode n, int x, int z)
        {
            unsafe { return FinishColumn(in spec, in o, in n, x, z, null); }
        }

        /// `cav` != null: the cavern candidates come from that cache (task 88;
        /// same bytes), null: computed here for this column.
        private static unsafe ColumnDetail FinishColumn(in TerrainSpec spec, in Offsets o, in CaveNode n, int x, int z,
                                                        CavernCells* cav)
        {
            var cd = PrepareGeology(in o, x, z, spec.Cliffs);
            cd.oreMul = spec.mapVersion >= 4 ? Ores.DEFAULT_MUL : (byte)0;
            float2 p = new float2(x, z);
            // Tunnels: the zero-line of a 2-D field meanders like a river in plan;
            // a second field gives its depth, a third its size, a fourth where it runs.
            cd.tun0 = TunnelLine(n.t0, out float h0);
            cd.tun1 = TunnelLine(n.t1, out float h1);
            cd.tun2 = TunnelLine(n.t2, out float h2);
            cd.tun3 = TunnelLine(n.t3, out float h3);
            cd.tunH0 = h0; cd.tunH1 = h1; cd.tunH2 = h2; cd.tunH3 = h3 * 1.3f;
            cd.tunY3 = 200f + 150f * (n.ty.w * 0.5f + 0.5f);   // below the regional ground
            cd.tunY0 = 180f + 120f * (n.ty.x * 0.5f + 0.5f);        // depth below the top
            cd.tunY1 = spec.seaLevelY - 500f - 300f * (n.ty.y * 0.5f + 0.5f);
            cd.tunY2 = spec.seaLevelY - 1100f - 300f * (n.ty.z * 0.5f + 0.5f);

            // Caverns: placed halls in the cavern layer, one candidate per cell.
            float nR = n.cv.x, nF = n.cv.y;
            int gx = x >> CAVERN_CELL_SHIFT, gz = z >> CAVERN_CELL_SHIFT;
            int band0 = spec.UnderworldTopY + 300;
            int cells = math.max(0, (spec.CavernTopY - 200 - band0) / CAVERN_CELL_Y);
            if (cav != null && cells <= CAVERN_CY_MAX) cav->Find(in o, gx, gz, band0, cells, p, nR, nF, ref cd);
            else
            {
                int found = 0;
                for (int cy = 0; cy < cells && found < 2; cy++)
                for (int j = -1; j <= 1 && found < 2; j++)
                for (int i = -1; i <= 1 && found < 2; i++)
                {
                    if (!CavernCandidate(in o, gx + i, cy, gz + j, band0, out var k)) continue;
                    if (!CavernInterval(in k, p, nR, nF, out int lo, out int hi)) continue;
                    if (found == 0) { cd.cav0Lo = lo; cd.cav0Hi = hi; } else { cd.cav1Lo = lo; cd.cav1Hi = hi; }
                    found++;
                }
            }

            // The underworld chamber: floor and ceiling from 2-D fields; pillars
            // where the ceiling comes down to the floor.
            float pil = n.cv.z;
            if (pil < 0.42f)
            {
                cd.underLo = spec.worldBottomY + 200 + (int)(150f * (n.cv.w * 0.5f + 0.5f));
                cd.underHi = spec.UnderworldTopY - 250 - (int)(250f * (n.uHi * 0.5f + 0.5f));
            }
            return cd;
        }

        /// A tunnel layer from its TunnelTerms (t = half height, gate, width,
        /// line): |line| / width, and the passage half height.
        private static float TunnelLine(float4 t, out float halfH)
        {
            halfH = 22f + 14f * (t.x * 0.5f + 0.5f);
            if (t.y < -0.15f) return 99f;          // passages end and begin
            float w = TUNNEL_WIDTH * (0.7f + 0.6f * (t.z * 0.5f + 0.5f));
            return math.abs(t.w) / w;
        }

        // ---- Task 88: CAVERN CELL CANDIDATES, once per cell ----
        // A column tests the cavern candidates of its 3 x 3 cells x `cells`
        // layers (seed 42 tg2: 36), each 1-10 Hash01. They depend only on the
        // cell (gx, gz) -- one 102.4 m cell covers a whole 0.1 m chunk column --
        // so CavernCells keeps the passing candidates of the last cells seen and
        // each column runs only the geometry. Byte-identical (the same two
        // functions, the same order); keyed by the cell, so shell chunks that
        // span many cells (shells >= 4) still read the right ones.
        public const int CAVERN_CY_MAX = 8;             // layers a cache holds; more -> uncached (seed 42 tg2: 4)
        private const int CAVERN_CANDS = CAVERN_CY_MAX * 9;

        /// One cavern candidate (a cell whose first hash passes): 9 floats.
        private struct CavernCand
        {
            public float2 c, u;
            public float R, stretch, cyc, floorDepth, ceilH;
            public const int FLOATS = 9;
        }

        /// Cell (gx, cy, gz)'s cavern, if it has one.
        private static bool CavernCandidate(in Offsets o, int gx, int cy, int gz, int band0, out CavernCand k)
        {
            k = default;
            uint sd = o.seed + 601u;
            if (Noise.Hash01(gx, cy, gz, sd) > 0.45f) return false;
            k.c = new float2(gx + 0.3f + 0.4f * Noise.Hash01(gx, cy, gz, sd + 1u),
                             gz + 0.3f + 0.4f * Noise.Hash01(gx, cy, gz, sd + 2u)) * CAVERN_CELL;
            k.R = 200f + 220f * Noise.Hash01(gx, cy, gz, sd + 3u);
            k.stretch = 1f + 0.6f * Noise.Hash01(gx, cy, gz, sd + 4u);
            float2 u = new float2(Noise.Hash01(gx, cy, gz, sd + 5u) - 0.5f, Noise.Hash01(gx, cy, gz, sd + 6u) - 0.5f);
            float ul = math.sqrt(u.x * u.x + u.y * u.y);
            k.u = ul > 1e-3f ? u / ul : new float2(1f, 0f);
            k.cyc = band0 + cy * CAVERN_CELL_Y + CAVERN_CELL_Y * 0.5f + (Noise.Hash01(gx, cy, gz, sd + 7u) - 0.5f) * 100f;
            k.floorDepth = 60f + 60f * Noise.Hash01(gx, cy, gz, sd + 8u);
            k.ceilH = 100f + 120f * Noise.Hash01(gx, cy, gz, sd + 9u);
            return true;
        }

        /// The open interval of candidate k's cavern through column p.
        private static bool CavernInterval(in CavernCand k, float2 p, float nR, float nF, out int lo, out int hi)
        {
            lo = hi = 0;
            float2 q = p - k.c;
            float along = math.dot(q, k.u) / k.stretch, side = q.x * -k.u.y + q.y * k.u.x;
            float r2 = (along * along + side * side) / (k.R * k.R) * (1f + 0.25f * nR);
            if (r2 >= 1f) return false;
            lo = (int)math.floor(k.cyc - k.floorDepth * (1f - 0.5f * r2) + 20f * nF);
            hi = (int)math.floor(k.cyc + k.ceilH * math.sqrt(1f - r2));
            return hi > lo + 2;
        }

        /// The passing cavern candidates round the last cells seen, 4 slots by
        /// cell parity (a 2 x 2 block of cells without thrashing). Owned by one
        /// loop (inside NodeCache; never shared); assumes one seed and spec per
        /// cache, as the grid nodes do. default = empty.
        public unsafe struct CavernCells
        {
            private fixed int _gx[4];
            private fixed int _gz[4];
            private fixed int _n[4];
            private fixed byte _used[4];
            private fixed float _v[4 * CAVERN_CANDS * CavernCand.FLOATS];
            /// Cells whose candidates were computed (diagnostics).
            public int Misses;

            /// The uncached loop's result for column p of cell (gx, gz):
            /// the same candidates in the same order (cy, j, i), first two hits.
            internal void Find(in Offsets o, int gx, int gz, int band0, int cells, float2 p, float nR, float nF,
                               ref ColumnDetail cd)
            {
                int slot = (gx & 1) | ((gz & 1) << 1);
                fixed (float* v = _v)
                {
                    var list = (CavernCand*)(v + slot * CAVERN_CANDS * CavernCand.FLOATS);
                    if (_used[slot] == 0 || _gx[slot] != gx || _gz[slot] != gz)
                    {
                        int n = 0;
                        for (int cy = 0; cy < cells; cy++)
                        for (int j = -1; j <= 1; j++)
                        for (int i = -1; i <= 1; i++)
                            if (CavernCandidate(in o, gx + i, cy, gz + j, band0, out var k)) list[n++] = k;
                        _n[slot] = n; _gx[slot] = gx; _gz[slot] = gz; _used[slot] = 1; Misses++;
                    }
                    int count = _n[slot], found = 0;
                    for (int t = 0; t < count && found < 2; t++)
                    {
                        if (!CavernInterval(in list[t], p, nR, nF, out int lo, out int hi)) continue;
                        if (found == 0) { cd.cav0Lo = lo; cd.cav0Hi = hi; } else { cd.cav1Lo = lo; cd.cav1Hi = hi; }
                        found++;
                    }
                }
            }
        }

        /// Convenience form: prepares the column itself (slow in a loop).
        /// `stepAtTop` is ShapeField.MaxStep at spacing 1 for this column (the
        /// falling-solid stability test).
        public static byte Material(in TerrainSpec spec, in Offsets o, in ColumnSample c, float stepAtTop,
                                    int x, int y, int z)
        {
            var cd = PrepareColumn(in spec, in o, x, z);
            return Material(in spec, in o, in c, in cd, stepAtTop, x, y, z);
        }

        public static byte Material(in TerrainSpec spec, in Offsets o, in ColumnSample c, in ColumnDetail cd,
                                    float stepAtTop, int x, int y, int z)
            => Material(in spec, in o, in c, in cd, stepAtTop, x, y, z, BrickCull.None);

        // ------------------------------------------------------------------
        // BRICK CULLING (2026-09-25, design doc §9 step 2). D is ~140 ns/voxel
        // underground because every voxel below 6 m evaluates 2-6 3D simplex
        // noises for caves. Those noises are SMOOTH at 440-2400 voxel scales,
        // so over an 8^3 brick (half-diagonal 6.93 voxels) each can change by
        // at most SNOISE_GRAD_BOUND * 6.93 / scale. One evaluation at the brick
        // centre therefore PROVES, for most bricks, that no voxel in the brick
        // can pass a carve test -- and the per-voxel code skips that test.
        //
        // EXACT, NOT APPROXIMATE: a culled Material is byte-identical to the
        // unculled one whenever SNOISE_GRAD_BOUND really bounds |grad snoise|.
        // TerrainGenTests measures the gradient (and asserts the margin) and
        // compares culled vs unculled over cave-dense chunks voxel for voxel.
        // ------------------------------------------------------------------
        public const int BRICK = 8;
        /// Upper bound on |grad noise.snoise(float3)| (per unit input). Measured
        /// max is asserted to sit below this by a 1.25x margin in the tests.
        public const float SNOISE_GRAD_BOUND = 10f;   // measured max 6.985 (1.43x margin)
        private const float BRICK_HALF_DIAG = 6.9282f;      // 4 * sqrt(3)
        private const float CULL_EPS = 1e-4f;               // float rounding of p / scale

        public struct BrickCull
        {
            public bool noPocket;        // no pocket or shell reaches the brick
            public bool noTunnel;        // spaghetti test cannot pass anywhere in it
            public bool noCheese;        // cavern-layer cheese test cannot pass
            public bool underClosed;     // underworld open-volume test cannot pass
            // V2: brick geology (TerrainSpec.brickGeologyDepth) evaluated ONCE
            // per brick, at its centre column -- the rule's own definition, so
            // passing it in is exact. Without it (None) Material prepares it
            // per voxel, which the exactness tests use as the reference.
            public bool hasGeo;
            public float geoDeep, geoCavern, geoUnder, geoLayer;
            public static BrickCull None => default;
        }

        /// The cull record of the 8^3 brick whose MIN corner is (bx, by, bz)
        /// (all multiples of BRICK). Valid for every voxel inside that brick.
        public static BrickCull CullBrick(in TerrainSpec spec, in Offsets o, int bx, int by, int bz)
        {
            var r = new BrickCull();

            // Pockets: a brick is inside exactly one POCKET_CELL (both power-of-two
            // aligned), so one lookup and an AABB test decide it for all 512 voxels.
            int cx = bx >> POCKET_CELL_SHIFT, cy = by >> POCKET_CELL_SHIFT, cz = bz >> POCKET_CELL_SHIFT;
            if (!TryGetPocket(in spec, in o, cx, cy, cz, out PocketInfo pk)) r.noPocket = true;
            else
            {
                float3 ext = pk.radii * 1.35f + 1f;
                float3 lo = new float3(bx, by, bz), hi = lo + BRICK;
                r.noPocket = math.any(hi < pk.centre - ext) || math.any(lo > pk.centre + ext);
            }

            // T4's caves are per-column intervals: nothing left to cull per brick.
            r.noTunnel = true; r.noCheese = true; r.underClosed = true;

            if (spec.brickGeologyDepth > 0)
            {
                var g = PrepareGeology(in o, bx + BRICK / 2, bz + BRICK / 2, spec.Cliffs);
                r.hasGeo = true; r.geoDeep = g.deepOffset; r.geoCavern = g.cavernOffset; r.geoUnder = g.underOffset; r.geoLayer = g.layerOffset;
            }
            return r;
        }

        /// Material with a brick's cull record. Byte-identical to the unculled
        /// form (see the block above); `cull` must be CullBrick of the brick
        /// containing (x, y, z).
        public static byte Material(in TerrainSpec spec, in Offsets o, in ColumnSample c, in ColumnDetail cd,
                                    float stepAtTop, int x, int y, int z, in BrickCull cull)
        {
            int h = c.height;
            if (y > h)
            {
                if (y <= spec.seaLevelY) return GenMat.Water;
                if (y <= c.water) return c.waterKind == 1 ? GenMat.Lava : GenMat.Water;
                return GenMat.Air;
            }
            if (y <= spec.worldBottomY) return GenMat.Bedrock;
            if (c.lipLo != 0 && y >= c.lipLo) return GenMat.RiverLip;   // task 76

            // §2.5 stamp openings (arches, hoops, ribs, under the tree's canopy).
            // Never the top voxel (Stamps.cs header), so S stays the top solid.
            // Map v4 (task 37e): an opening below the column's still-water level
            // holds that water -- a hoop or arch standing in a lake has water
            // under it, not an air pocket the lake drains into.
            if ((y >= c.open1Lo && y < c.open1Hi) || (y >= c.open2Lo && y < c.open2Hi) || (y >= c.open3Lo && y < c.open3Hi))
                return spec.mapVersion >= 4 && y <= c.water ? (c.waterKind == 1 ? GenMat.Lava : GenMat.Water) : GenMat.Air;
            if (c.stamp3D)
            {
                byte st = Stamps.Carve3D(in spec, in o, in c, x, y, z);
                if (st != 255) return st;
            }

            // Under a cap-rock LEDGE (ShapeField.Ledge) the ground is the top of
            // the gap's floor, not the shelf: caves, tunnels and the surface /
            // soil rules measure depth from there, or the ground under a ledge
            // counts as 15 m deep and gets carved away (LedgesTest found it).
            int hg = (c.open3Hi > c.open3Lo && y < c.open3Lo) ? c.open3Lo - 1 : h;
            int depth = hg - y;
            // Map v4 (task 37e): a lake rim's fill is not ground to carve; caves
            // go by the ground under it (ColumnSample.rimGround).
            int hc = c.rimGround != 0 ? math.min(hg, c.rimGround) : hg;
            if (hc - y > CARVE_MIN_DEPTH && (c.riverFloor == 0 || y < c.riverFloor))   // task 76: none beside a river
            {
                bool inShell = false;
                if (!cull.noPocket)
                {
                    byte pocket = Pocket(in spec, in o, x, y, z, out inShell);
                    if (pocket != 255) return pocket;
                }
                if (!inShell)
                {
                    // Caverns; giant mushrooms stand inside them.
                    if ((y >= cd.cav0Lo && y < cd.cav0Hi) || (y >= cd.cav1Lo && y < cd.cav1Hi))
                    {
                        byte mush = Stamps.Mushroom(in spec, in o, x, y, z);
                        return mush != 255 ? mush : GenMat.Air;
                    }
                    // The underworld chamber over the lava sea.
                    if (y < spec.UnderworldTopY && y >= cd.underLo && y < cd.underHi)
                        return y <= spec.worldBottomY + LAVA_ABOVE_FLOOR ? GenMat.Lava : GenMat.Air;
                    // Tunnels: an elliptical passage around each layer's centre line.
                    if (InTunnel(cd.tun0, hc - cd.tunY0, cd.tunH0, y) || InTunnel(cd.tun1, cd.tunY1, cd.tunH1, y) ||
                        InTunnel(cd.tun2, cd.tunY2, cd.tunH2, y) || InTunnel(cd.tun3, c.regional - cd.tunY3, cd.tunH3, y))
                        return GenMat.Air;
                }
            }
            if (c.stampMat != 0 && y >= c.stampBodyLo) return c.stampMat;

            // V0's opt-in brick geology (TerrainSpec.brickGeologyDepth): one layer
            // decision per 8^3 brick, from the brick centre's column detail, so a
            // wavy layer boundary steps by whole bricks deep down. This form
            // re-prepares that column per voxel -- fine for measuring; a chunk
            // generator that adopts the rule passes the brick's detail in.
            if (spec.brickGeologyDepth > 0 && depth > spec.brickGeologyDepth)
            {
                int gy = (y & ~(BRICK - 1)) + BRICK / 2;
                float gDeep, gCavern, gUnder, gLayer;
                if (cull.hasGeo) { gDeep = cull.geoDeep; gCavern = cull.geoCavern; gUnder = cull.geoUnder; gLayer = cull.geoLayer; }
                else
                {
                    var bd = PrepareGeology(in o, (x & ~(BRICK - 1)) + BRICK / 2, (z & ~(BRICK - 1)) + BRICK / 2, spec.Cliffs);
                    gDeep = bd.deepOffset; gCavern = bd.cavernOffset; gUnder = bd.underOffset; gLayer = bd.layerOffset;
                }
                // Ores (gameplay step 3, map v4): the host layer is the brick's own.
                if (cd.oreMul != 0 && spec.mapVersion >= 4)
                {
                    byte ore = Ores.At(in spec, cd.oreMul, c.biome, Ores.Layer(in spec, gy, gDeep, gCavern, gUnder), x, y, z);
                    if (ore != 0) return ore;
                }
                return LayerRock(in spec, c.surfBiome, gy, gDeep, gCavern, gUnder, gLayer);
            }

            if (spec.CoverSlots)
            {
                // Task 177: the slot's palette material as a SURFACE_THICKNESS skin
                // (none where it is BARE or the voxel's step is sheer), soil under
                // it except on steep ground (skin over rock).
                byte cm = Cover.PaletteMaterial(c.surfBiome, c.cover);
                if (cm != Cover.BARE && !(stepAtTop > SHEER_STEP) && y >= spec.CavernTopY)
                {
                    if (depth < SURFACE_THICKNESS) return Cover.Settle(cm, stepAtTop);
                    if (Cover.Slot(c.cover) != Cover.Steep && depth < cd.soilDepth) return Soil(c.surfBiome);
                }
            }
            else if (!(stepAtTop > 1.5f) && y >= spec.CavernTopY)
            {
                if (depth < SURFACE_THICKNESS) return Surface(in spec, in c, stepAtTop);
                if (depth < cd.soilDepth) return Soil(c.surfBiome);
            }
            // Ores (gameplay step 3, map v4): only in rock deeper than 6 m (no cliff faces).
            if (cd.oreMul != 0 && depth > Ores.MIN_DEPTH && spec.mapVersion >= 4)
            {
                byte ore = Ores.At(in spec, cd.oreMul, c.biome, Ores.Layer(in spec, y, cd.deepOffset, cd.cavernOffset, cd.underOffset), x, y, z);
                if (ore != 0) return ore;
            }
            return LayerRock(in spec, c.surfBiome, y, cd.deepOffset, cd.cavernOffset, cd.underOffset, cd.layerOffset);
        }

        // ------------------------------------------------------------------
        // WHOLE-BRICK PROOFS (engine step 15, task 82b). A chunk generator asks,
        // per column of an 8^3 brick, whether Material gives every sample y of
        // the brick the SAME material -- one call from the column data instead
        // of eight Material calls. It walks Material's branches over the y RANGE
        // [yLo, yHi]: each branch is taken by every sample or by none, else the
        // answer is 255 = unproven, and the caller runs the per-voxel pass (so
        // 255 is always safe; only a wrong non-255 answer could change output).
        // EXACT BY CONSTRUCTION: each test is Material's own comparison at the
        // end of the range where it is hardest to pass. Integer tests are exact;
        // the float layer tests (y + offset < limit) are monotone in both terms,
        // so the bound at the end holds for every sample. Tunnels get a 2-voxel
        // margin for the rounding of (y - yc). BrickSkipTests compares chunks and
        // shell chunks with the skip on and off, byte for byte.
        // ------------------------------------------------------------------

        /// Bound on |snoise| used for the layer offsets when the caller does not
        /// know each sample's fine brick (shells): 2x the noise's +-1 range.
        public const float GEO_OFFSET_MARGIN = 2f;

        /// Material of every sample y = yLo + j * yStep (j < count) of column
        /// (x, z), or 255 when the column data cannot prove them all equal.
        /// noPocket: CullBrick's pocket test passes for every sample's fine brick.
        /// geoExact: all samples lie in ONE fine brick whose CullBrick geology is
        /// (geoDeep, geoCavern, geoUnder); otherwise the offsets are only bounded.
        public static byte UniformSamples(in TerrainSpec spec, in ColumnSample c, in ColumnDetail cd, float stepAtTop,
                                          int x, int z, int yLo, int yStep, int count, bool noPocket,
                                          bool geoExact, float geoDeep, float geoCavern, float geoUnder, float geoLayer)
        {
            int yHi = yLo + yStep * (count - 1);
            int h = c.height;
            if (yLo > h)
            {
                if (yLo > spec.seaLevelY && yLo > c.water) return GenMat.Air;
                if (yHi <= spec.seaLevelY) return GenMat.Water;
                if (yLo > spec.seaLevelY && yHi <= c.water) return c.waterKind == 1 ? GenMat.Lava : GenMat.Water;
                return 255;
            }
            if (yHi > h) return 255;
            if (yLo <= spec.worldBottomY) return yHi <= spec.worldBottomY ? GenMat.Bedrock : (byte)255;
            if (c.lipLo != 0 && yHi >= c.lipLo) return 255;
            if (Meets(c.open1Lo, c.open1Hi, yLo, yHi) || Meets(c.open2Lo, c.open2Hi, yLo, yHi) || Meets(c.open3Lo, c.open3Hi, yLo, yHi))
                return 255;
            if (c.stamp3D) return 255;

            int hg = h;   // Material's ledge rule; the range is wholly below or above the gap here
            if (c.open3Hi > c.open3Lo && yHi < c.open3Lo) hg = c.open3Lo - 1;
            int hc = c.rimGround != 0 ? math.min(hg, c.rimGround) : hg;
            if (hc - yLo > CARVE_MIN_DEPTH && (c.riverFloor == 0 || yLo < c.riverFloor))   // some sample may carve
            {
                if (!noPocket) return 255;
                if (Meets(cd.cav0Lo, cd.cav0Hi, yLo, yHi) || Meets(cd.cav1Lo, cd.cav1Hi, yLo, yHi)) return 255;
                if (Meets(cd.underLo, math.min(cd.underHi, spec.UnderworldTopY), yLo, yHi)) return 255;
                if (MayTunnel(cd.tun0, hc - cd.tunY0, cd.tunH0, yLo, yHi) || MayTunnel(cd.tun1, cd.tunY1, cd.tunH1, yLo, yHi) ||
                    MayTunnel(cd.tun2, cd.tunY2, cd.tunH2, yLo, yHi) || MayTunnel(cd.tun3, c.regional - cd.tunY3, cd.tunH3, yLo, yHi))
                    return 255;
            }
            if (c.stampMat != 0 && yHi >= c.stampBodyLo) return yLo >= c.stampBodyLo ? c.stampMat : (byte)255;

            // The rock rules: brick geology deeper than brickGeologyDepth, the
            // per-voxel rules above it. A range across that depth is split there
            // and both parts must prove the same material.
            int bgd = spec.brickGeologyDepth;
            if (bgd > 0 && hg - yLo > bgd)
            {
                if (hg - yHi > bgd) return GeologyRock(in spec, in c, in cd, x, z, yLo, yStep, count, geoExact, geoDeep, geoCavern, geoUnder, geoLayer);
                int nGeo = (hg - bgd - 1 - yLo) / yStep + 1;   // the samples with y < hg - bgd
                byte deep = GeologyRock(in spec, in c, in cd, x, z, yLo, yStep, nGeo, geoExact, geoDeep, geoCavern, geoUnder, geoLayer);
                if (deep == 255) return 255;
                return ShallowRock(in spec, in c, in cd, stepAtTop, x, z, yLo + nGeo * yStep, yStep, count - nGeo, hg) == deep ? deep : (byte)255;
            }
            return ShallowRock(in spec, in c, in cd, stepAtTop, x, z, yLo, yStep, count, hg);
        }

        /// UniformSamples' tail where every sample is deeper than brickGeologyDepth:
        /// ore, then LayerRock at the sample's fine-brick centre height.
        private static byte GeologyRock(in TerrainSpec spec, in ColumnSample c, in ColumnDetail cd, int x, int z,
                                        int yLo, int yStep, int count, bool geoExact, float geoDeep, float geoCavern, float geoUnder, float geoLayer)
        {
            int yHi = yLo + yStep * (count - 1);
            float dLo, dHi, cLo, cHi, uLo, uHi, lLo, lHi;
            if (geoExact)
            {
                if ((yLo >> 3) != (yHi >> 3)) return 255;
                dLo = dHi = geoDeep; cLo = cHi = geoCavern; uLo = uHi = geoUnder; lLo = lHi = geoLayer;
            }
            else
            {
                dHi = GEO_DEEP_AMP * GEO_OFFSET_MARGIN; cHi = GEO_CAVERN_AMP * GEO_OFFSET_MARGIN; uHi = GEO_UNDER_AMP * GEO_OFFSET_MARGIN;
                lHi = spec.Cliffs ? LAYER_WAVE_BOUND * GEO_OFFSET_MARGIN : 0f;
                dLo = -dHi; cLo = -cHi; uLo = -uHi; lLo = -lHi;
            }
            int gyLo = (yLo & ~(BRICK - 1)) + BRICK / 2, gyHi = (yHi & ~(BRICK - 1)) + BRICK / 2;
            if (cd.oreMul != 0 && spec.mapVersion >= 4 &&
                OreMay(in spec, c.biome, cd.oreMul, x, z, yLo, yStep, count, gyLo, gyHi, dLo, dHi, cLo, cHi, uLo, uHi))
                return 255;
            return LayerRockRange(in spec, c.surfBiome, gyLo, gyHi, dLo, dHi, cLo, cHi, uLo, uHi, lLo, lHi);
        }

        /// UniformSamples' tail where no sample is deeper than brickGeologyDepth:
        /// surface, soil, ore, then LayerRock at the sample's own height.
        private static byte ShallowRock(in TerrainSpec spec, in ColumnSample c, in ColumnDetail cd, float stepAtTop, int x, int z,
                                        int yLo, int yStep, int count, int hg)
        {
            int yHi = yLo + yStep * (count - 1);
            int depthLo = hg - yHi, depthHi = hg - yLo;
            if (spec.CoverSlots)
            {
                byte cm = Cover.PaletteMaterial(c.surfBiome, c.cover);   // Material's task 177 branch
                if (cm != Cover.BARE && !(stepAtTop > SHEER_STEP) && yHi >= spec.CavernTopY)
                {
                    if (yLo < spec.CavernTopY) return 255;
                    if (depthLo < SURFACE_THICKNESS) return depthHi < SURFACE_THICKNESS ? Cover.Settle(cm, stepAtTop) : (byte)255;
                    if (Cover.Slot(c.cover) != Cover.Steep && depthLo < cd.soilDepth) return depthHi < cd.soilDepth ? Soil(c.surfBiome) : (byte)255;
                }
            }
            else if (!(stepAtTop > 1.5f) && yHi >= spec.CavernTopY)
            {
                if (yLo < spec.CavernTopY) return 255;
                if (depthLo < SURFACE_THICKNESS) return depthHi < SURFACE_THICKNESS ? Surface(in spec, in c, stepAtTop) : (byte)255;
                if (depthLo < cd.soilDepth) return depthHi < cd.soilDepth ? Soil(c.surfBiome) : (byte)255;
            }
            float d = cd.deepOffset, cv = cd.cavernOffset, u = cd.underOffset, l = cd.layerOffset;
            if (cd.oreMul != 0 && depthHi > Ores.MIN_DEPTH && spec.mapVersion >= 4 &&
                OreMay(in spec, c.biome, cd.oreMul, x, z, yLo, yStep, count, yLo, yHi, d, d, cv, cv, u, u))
                return 255;
            return LayerRockRange(in spec, c.surfBiome, yLo, yHi, d, d, cv, cv, u, u, l, l);
        }

        /// [yLo, yHi] meets the interval [lo, hi) (empty when hi <= lo).
        private static bool Meets(int lo, int hi, int yLo, int yHi) => hi > lo && yHi >= lo && yLo < hi;

        /// InTunnel can pass for some y in [yLo, yHi]. It needs t < 1 and
        /// |y - yc| < halfH; 2 voxels of margin cover the float rounding of y - yc.
        private static bool MayTunnel(float t, float yc, float halfH, int yLo, int yHi)
        {
            if (t >= 1f) return false;
            return !((double)yLo - yc >= (double)halfH + 2.0 || (double)yc - yHi >= (double)halfH + 2.0);
        }

        /// Ore can appear at some sample: the host layer is not provably one
        /// layer over [lyLo, lyHi] and the offset ranges, or a sample's brick
        /// passes Ores' cell and deposit tests.
        private static bool OreMay(in TerrainSpec spec, byte biome, int oreMul, int x, int z, int yLo, int yStep, int count,
                                   int lyLo, int lyHi, float dLo, float dHi, float cLo, float cHi, float uLo, float uHi)
        {
            float lo = lyLo, hi = lyHi;
            int layer;
            int s500 = spec.seaLevelY - STONE_BOTTOM_BELOW_SEA, s1000 = spec.seaLevelY - Ores.GOLD_TOP_BELOW_SEA;
            if (hi + uHi < spec.UnderworldTopY) layer = 4;
            else if (!(lo + uLo >= spec.UnderworldTopY)) return true;
            else if (hi + cHi < spec.CavernTopY) layer = 3;
            else if (!(lo + cLo >= spec.CavernTopY)) return true;
            else if (hi + dHi < s500)
            {
                if (hi + dHi < s1000) layer = 2;
                else if (lo + dLo >= s1000) layer = 1;
                else return true;
            }
            else if (!(lo + dLo >= s500)) return true;
            else layer = 0;
            int chance = Ores.CellChance(layer, biome, oreMul);
            if (chance <= 0) return false;
            for (int j = 0; j < count; j++)
            {
                int y = yLo + j * yStep;
                if (j > 0 && (y >> 3) == ((y - yStep) >> 3)) continue;   // same brick as the last sample
                if (Ores.BrickMayHold(in spec, chance, x, y, z)) return true;
            }
            return false;
        }

        /// LayerRock for every y in [yLo, yHi] and every offset in its range, or
        /// 255 when that is not one material.
        private static byte LayerRockRange(in TerrainSpec spec, byte biome, int yLo, int yHi,
                                           float dLo, float dHi, float cLo, float cHi, float uLo, float uHi, float lLo, float lHi)
        {
            float lo = yLo, hi = yHi;
            float gLo = lo + uLo, gHi = hi + uHi;
            if (gHi < spec.UnderworldTopY)
            {
                int magma = spec.worldBottomY + LAVA_ABOVE_FLOOR + 200, ash = spec.UnderworldTopY - 300;
                if (gHi < magma) return GenMat.MagmaCrust;
                if (!(gLo >= magma)) return 255;
                if (gLo >= ash) return GenMat.Ash;
                return gHi < ash ? GenMat.Obsidian : (byte)255;
            }
            if (!(gLo >= spec.UnderworldTopY)) return 255;
            if (hi + cHi < spec.CavernTopY) return BiomeRock(biome, 2);
            if (!(lo + cLo >= spec.CavernTopY)) return 255;
            if (hi + dHi < spec.seaLevelY - STONE_BOTTOM_BELOW_SEA) return BiomeRock(biome, 1);
            if (!(lo + dLo >= spec.seaLevelY - STONE_BOTTOM_BELOW_SEA)) return 255;
            if (spec.Cliffs)
            {
                // Task 178: LayerIndex is monotone in g = y + layerOffset, so every
                // sample's layer lies in [kLo, kHi]; one rock across them proves it.
                int kLo = LayerIndex(spec.seed, lo + lLo), kHi = LayerIndex(spec.seed, hi + lHi);
                if (kHi - kLo > 4) return 255;
                byte m0 = LayerMaterial(spec.seed, biome, kLo);
                for (int k = kLo + 1; k <= kHi; k++) if (LayerMaterial(spec.seed, biome, k) != m0) return 255;
                return m0;
            }
            if (biome == GenBiome.Badlands) return (yLo >> 5) == (yHi >> 5) ? BadlandsBand(yLo) : (byte)255;
            return BiomeRock(biome, 0);
        }

        /// CullBrick's pocket test for every fine brick inside the box [lo, hi)
        /// (multiples of BRICK): true when no pocket or its shell reaches any.
        public static bool NoPocketInBox(in TerrainSpec spec, in Offsets o, int3 lo, int3 hi)
        {
            int3 c0 = lo >> POCKET_CELL_SHIFT, c1 = (hi - 1) >> POCKET_CELL_SHIFT;
            float3 flo = lo, fhi = hi;
            for (int cz = c0.z; cz <= c1.z; cz++)
            for (int cy = c0.y; cy <= c1.y; cy++)
            for (int cx = c0.x; cx <= c1.x; cx++)
            {
                if (!TryGetPocket(in spec, in o, cx, cy, cz, out PocketInfo pk)) continue;
                float3 ext = pk.radii * 1.35f + 1f;
                if (!(math.any(fhi < pk.centre - ext) || math.any(flo > pk.centre + ext))) return false;
            }
            return true;
        }

        /// The material of the column's top voxel -- what the far band colours a
        /// texel with. Same rules as Material at depth 0.
        public static byte TopMaterial(in TerrainSpec spec, in ColumnSample c, float stepAtTop, in Offsets o, int x, int z)
        {
            if (c.height < spec.seaLevelY) return GenMat.Water;
            if (c.water > c.height) return c.waterKind == 1 ? GenMat.Lava : GenMat.Water;
            if (c.lipLo != 0 && c.height >= c.lipLo) return GenMat.RiverLip;   // task 76
            if (c.stampMat != 0 && c.height >= c.stampBodyLo) return c.stampMat;
            if (spec.CoverSlots)
            {
                byte cm = Cover.PaletteMaterial(c.surfBiome, c.cover);   // task 177 (Material at depth 0)
                if (cm == Cover.BARE || stepAtTop > SHEER_STEP) return Strata(in spec, in o, c.surfBiome, x, c.height, z);
                return Cover.Settle(cm, stepAtTop);
            }
            if (stepAtTop > 1.5f) return Strata(in spec, in o, c.surfBiome, x, c.height, z);
            return Surface(in spec, in c, stepAtTop);
        }

        /// Task 177: with cover slots on, a voxel whose top step exceeds this
        /// (~72 deg) shows its rock whatever the slot (a cliff the 6 m slope
        /// probe did not see). Below it the slot decides: before 177 every step
        /// over 1.5 showed rock, which dithered rock into the cover on any slope
        /// near 56 deg (WORLD_GEN_LOOK_PLAN finding 2).
        public const float SHEER_STEP = 3f;

        private static byte Surface(in TerrainSpec spec, in ColumnSample c, float stepAtTop)
        {
            bool stable = stepAtTop <= 1f;   // falling solids only where stable at rest
            // Beaches: the last couple of metres above the waterline.
            // Task 122: the surface biome (the border blend), never the rock's.
            if (c.height <= spec.seaLevelY + 22 && c.height >= spec.seaLevelY - 30 && c.surfBiome != GenBiome.Snow)
                return stable ? GenMat.BeachSand : GenMat.PackedSand;
            switch (c.surfBiome)
            {
                case GenBiome.Desert: return stable ? GenMat.Sand : GenMat.PackedSand;
                case GenBiome.Snow: return stable ? GenMat.Snow : GenMat.PackedIce;
                case GenBiome.Jungle: return GenMat.JungleGrass;
                case GenBiome.Savanna: return GenMat.SavannaGrass;
                case GenBiome.Badlands: return GenMat.BadlandsClay;
                default: return GenMat.Grass;
            }
        }

        private static byte Soil(byte biome)
        {
            switch (biome)
            {
                case GenBiome.Desert: return GenMat.PackedSand;
                case GenBiome.Snow: return GenMat.PackedIce;
                case GenBiome.Jungle: return GenMat.Mud;
                case GenBiome.Savanna: return GenMat.RedEarth;
                case GenBiome.Badlands: return GenMat.Hardpan;
                default: return GenMat.Loam;
            }
        }

        /// Stone ends this far below sea level (voxels; the wavy deepOffset moves
        /// the boundary +-8 m). Deep stone runs from there to the cavern top.
        public const int STONE_BOTTOM_BELOW_SEA = 500;

        /// The rock at height y in a column (no surface, soil or caves): the
        /// thick depth layers, the biome's own rock in each, the shared
        /// underworld at the bottom. Exposed cliffs show this too.
        public static byte Strata(in TerrainSpec spec, in Offsets o, byte biome, int x, int y, int z)
        {
            var cd = PrepareGeology(in o, x, z, spec.Cliffs);
            return LayerRock(in spec, biome, y, cd.deepOffset, cd.cavernOffset, cd.underOffset, cd.layerOffset);
        }

        private static byte LayerRock(in TerrainSpec spec, byte biome, float y, float deepOff, float cavernOff, float underOff,
                                      float layerOff)
        {
            if (y + underOff < spec.UnderworldTopY) return UnderworldRock(in spec, y + underOff);
            if (y + cavernOff < spec.CavernTopY) return BiomeRock(biome, 2);
            if (y + deepOff < spec.seaLevelY - STONE_BOTTOM_BELOW_SEA) return BiomeRock(biome, 1);
            if (spec.Cliffs) return LayerMaterial(spec.seed, biome, LayerIndex(spec.seed, y + layerOff));
            if (biome == GenBiome.Badlands) return BadlandsBand((int)y);
            return BiomeRock(biome, 0);
        }

        // ------------------------------------------------------------------
        // TASK 178 (WORLD_GEN_LOOK_PLAN C4; owner 2026-11-05 "C4 cliffs approved",
        // 2026-10-01 "thick depth layers", no thin stripes in any biome). THE
        // STONE ZONE'S THICK LAYERS. Above the stone / deep-stone boundary
        // (sea - 50 m) the rock is a stack of horizontal layers by HEIGHT, each
        // LAYER_THICK (24 m) +- LAYER_JITTER, whose boundaries wave by LayerWave
        // (+-4 m over ~90 m, +-1.5 m over ~26 m). Every layer has a ROLE from a
        // hash of its index (main ~1/2, light accent ~3/10, dark accent ~1/5);
        // the biome's row of LAYER_ROCKS says which rock fills each role -- the
        // same split as Cover's slots and palette: the rule names no material.
        // A cliff, a canyon wall or any bare steep slope therefore shows the
        // same few thick bands at the same heights, in its biome's colours, and
        // so does a tunnel wall or a dug hole: it is the rock, not a skin.
        // The layer boundaries are the same heights in every biome, so at a
        // biome border only the colours change. Badlands' 3.2 m bands are gone
        // with it (its row: red / cream / ochre, thick); the brief's open row on
        // striped badlands stays open (owner: no stripes until said otherwise).
        // ------------------------------------------------------------------
        public const float LAYER_THICK = 240f;        // voxels: 24 m nominal
        public const float LAYER_JITTER = 0.3f;       // each boundary moves +-30 % (layers 9.6-38.4 m)
        public const float LAYER_WAVE_AMP = 40f, LAYER_WAVE = 900f, LAYER_RIPPLE_AMP = 15f, LAYER_RIPPLE = 260f;
        /// |LayerWave| <= this (|snoise| <= 1).
        public const float LAYER_WAVE_BOUND = LAYER_WAVE_AMP + LAYER_RIPPLE_AMP;

        /// Rock per (biome, role): main, light accent, dark accent. Row order = GenBiome ids.
        private static readonly byte[] LAYER_ROCKS = {
            GenMat.Granite, GenMat.Limestone, GenMat.Slate,                       // forest
            GenMat.OrangeSandstone, GenMat.CreamSandstone, GenMat.RedSandstone,   // desert
            GenMat.SilverSchist, GenMat.Marble, GenMat.BlueSlate,                 // snow
            GenMat.KarstLimestone, GenMat.Laterite, GenMat.MossRock,              // jungle
            GenMat.KopjeGranite, GenMat.Gneiss, GenMat.Ironstone,                 // savanna
            GenMat.RedBand, GenMat.CreamBand, GenMat.OchreBand,                   // badlands
        };

        /// The wavy offset added to y before the layer lookup (voxels).
        public static float LayerWave(in Offsets o, float2 p)
            => LAYER_WAVE_AMP * noise.snoise(p / LAYER_WAVE + o.strata + new float2(29.3f, -77.1f))
             + LAYER_RIPPLE_AMP * noise.snoise(p / LAYER_RIPPLE + o.strata + new float2(-51.9f, 13.7f));

        /// Boundary k's height: layer k is [LayerBoundary(k), LayerBoundary(k + 1)).
        private static float LayerBoundary(uint seed, int k)
            => (k + LAYER_JITTER * (2f * Noise.Hash01(k, 0, 0, seed + 1781u) - 1f)) * LAYER_THICK;

        /// The layer holding height g (= y + layerOffset). Monotone in g.
        public static int LayerIndex(uint seed, float g)
        {
            int k = (int)math.floor(g / LAYER_THICK);
            if (g < LayerBoundary(seed, k)) return k - 1;
            if (g >= LayerBoundary(seed, k + 1)) return k + 1;
            return k;
        }

        /// The layer's role: 0 main (~50 %), 1 light accent (~30 %), 2 dark accent (~20 %).
        public static int LayerRole(uint seed, int k)
        {
            float r = Noise.Hash01(k, 1, 0, seed + 1783u);
            return r < 0.5f ? 0 : (r < 0.8f ? 1 : 2);
        }

        public static byte LayerMaterial(uint seed, byte biome, int k)
            => LAYER_ROCKS[(biome < GenBiome.Count ? biome : 0) * 3 + LayerRole(seed, k)];

        /// Badlands' exposed rock (B10): flat bands by height, 3.2 m each
        /// (y >> 5), in an 8-band, 25.6 m cycle: red, red, cream, ochre,
        /// ochre, red, cream, red. Cliffs and the far band's steep texels
        /// (TopMaterial -> Strata) show the stripes.
        public static byte BadlandsBand(int y)
        {
            switch ((y >> 5) & 7)
            {
                case 2: case 6: return GenMat.CreamBand;
                case 3: case 4: return GenMat.OchreBand;
                default: return GenMat.RedBand;
            }
        }

        /// Each biome's rock per layer (0 stone, 1 deep stone, 2 cavern rock),
        /// from its palette-D set: a mid-tone stone near the surface, darker
        /// rock below. The biome's other two rocks are kept for veins and
        /// accents (object placement). Recolouring is a palette change only.
        public static byte BiomeRock(byte biome, int layer)
        {
            switch (biome)
            {
                case GenBiome.Desert: return layer == 0 ? GenMat.OrangeSandstone : (layer == 1 ? GenMat.RedSandstone : GenMat.MaroonMudstone);
                case GenBiome.Snow:   return layer == 0 ? GenMat.SilverSchist : (layer == 1 ? GenMat.BlueSlate : GenMat.Basalt);
                case GenBiome.Jungle: return layer == 0 ? GenMat.KarstLimestone : (layer == 1 ? GenMat.MossRock : GenMat.BlackBasalt);
                case GenBiome.Savanna: return layer == 0 ? GenMat.KopjeGranite : (layer == 1 ? GenMat.Ironstone : GenMat.Gneiss);
                case GenBiome.Badlands: return layer == 0 ? GenMat.RedBand : (layer == 1 ? GenMat.UmberMudstone : GenMat.PlumShale);
                default:              return layer == 0 ? GenMat.Granite : (layer == 1 ? GenMat.Slate : GenMat.Shale);
            }
        }

        /// The shared underworld (every biome): ash in its top 30 m, magma crust
        /// in the 20 m above the lava sea, obsidian between.
        private static byte UnderworldRock(in TerrainSpec spec, float g)
        {
            if (g < spec.worldBottomY + LAVA_ABOVE_FLOOR + 200) return GenMat.MagmaCrust;
            if (g >= spec.UnderworldTopY - 300) return GenMat.Ash;
            return GenMat.Obsidian;
        }

        // ------------------------------------------------------------------
        // Caves (T4): see PrepareColumn. `t` = |line| / width, yc the centre.
        private static bool InTunnel(float t, float yc, float halfH, int y)
        {
            if (t >= 1f) return false;
            float v = (y - yc) / halfH;
            return t * t + v * v < 1f;
        }

        // ------------------------------------------------------------------
        // Sealed fluid pockets. One candidate per POCKET_CELL cube, centred in
        // [170, 342] with radii <= 90 (shell <= 121.5), so the pocket and its shell never leave the cell --
        // which is what lets a voxel test only its own cell.
        // Returns 255 outside every pocket; inShell = inside the no-carve shell.
        // ------------------------------------------------------------------
        public struct PocketInfo
        {
            public float3 centre, radii;
            public float level;      // flat fluid surface (absolute Y): fluid below, air above
            public byte fluid;       // GenMat fluid code
        }

        /// The pocket of pocket cell (cx, cy, cz), if it has one. Deterministic
        /// from the seed; the pocket and its no-carve shell lie inside the cell.
        public static bool TryGetPocket(in TerrainSpec spec, in Offsets o, int cx, int cy, int cz, out PocketInfo info)
        {
            info = default;
            float3 centre = new float3(cx, cy, cz) * POCKET_CELL + new float3(
                170f + 172f * Noise.Hash01(cx, cy, cz, o.seed + 1u),
                170f + 172f * Noise.Hash01(cx, cy, cz, o.seed + 2u),
                170f + 172f * Noise.Hash01(cx, cy, cz, o.seed + 3u));
            float3 radii = new float3(
                45f + 45f * Noise.Hash01(cx, cy, cz, o.seed + 4u),
                25f + 35f * Noise.Hash01(cx, cy, cz, o.seed + 5u),
                45f + 45f * Noise.Hash01(cx, cy, cz, o.seed + 6u));
            int topLimit = spec.seaLevelY - POCKET_MAX_TOP_BELOW_SEA;
            if (centre.y + radii.y * 1.35f > topLimit) return false;              // too shallow
            if (centre.y - radii.y * 1.35f < spec.UnderworldTopY) return false;   // the underworld has its lava sea
            bool cavern = centre.y < spec.CavernTopY;
            if (Noise.Hash01(cx, cy, cz, o.seed) >= (cavern ? 0.45f : 0.35f)) return false;
            float fill = 0.35f + 0.55f * Noise.Hash01(cx, cy, cz, o.seed + 7u);
            info.centre = centre;
            info.radii = radii;
            info.level = centre.y - radii.y + 2f * radii.y * fill;
            info.fluid = PocketFluid(in o, cavern, cx, cy, cz, centre);
            return true;
        }

        /// Returns 255 outside every pocket; inShell = inside the no-carve shell.
        public static byte Pocket(in TerrainSpec spec, in Offsets o, int x, int y, int z, out bool inShell)
        {
            inShell = false;
            // Power-of-two cell, arithmetic shift == floor division (CLAUDE.md's
            // bitwise coordinate rule).
            int cx = x >> POCKET_CELL_SHIFT, cy = y >> POCKET_CELL_SHIFT, cz = z >> POCKET_CELL_SHIFT;
            if (!TryGetPocket(in spec, in o, cx, cy, cz, out PocketInfo pk)) return 255;
            float3 q = (new float3(x, y, z) + 0.5f - pk.centre) / pk.radii;
            float d2 = q.x * q.x + q.y * q.y + q.z * q.z;
            if (d2 >= 1.35f * 1.35f) return 255;
            if (d2 >= 1f) { inShell = true; return 255; }                          // solid shell: falls through to rock
            return (y + 0.5f > pk.level) ? GenMat.Air : pk.fluid;                 // FLAT level: at rest
        }

        private static byte PocketFluid(in Offsets o, bool cavern, int cx, int cy, int cz, float3 centre)
        {
            float r = Noise.Hash01(cx, cy, cz, o.seed + 8u);
            if (cavern)
            {
                if (r < 0.30f) return GenMat.Acid;
                if (r < 0.60f) return GenMat.Sludge;
                if (r < 0.80f) return GenMat.Water;
                return GenMat.Lava;
            }
            byte biome = ShapeField.Biome(in o, centre.xz);
            if (biome == GenBiome.Desert) return r < 0.55f ? GenMat.Oil : (r < 0.8f ? GenMat.Quicksand : GenMat.Water);
            if (biome == GenBiome.Jungle) return r < 0.35f ? GenMat.Honey : GenMat.Water;
            return r < 0.15f ? GenMat.Oil : GenMat.Water;
        }

    }
}
