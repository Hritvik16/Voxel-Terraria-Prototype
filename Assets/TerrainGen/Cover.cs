// Assets/TerrainGen/Cover.cs
//
// Task 177 (WORLD_GEN_LOOK_PLAN C2 + C3 as ONE mechanism; owner, 2026-11-05:
// "instead of saying grass or snow, can't we use materials from that biome? ...
// The point is it might not always be snow").
//
// GROUND COVER = (SLOT, BIOME). The rules below choose only a SLOT from the
// ground's shape; they never name a material. Each biome owns a PALETTE row
// that says what fills each slot (and an optional patch variant). Adding a
// biome is filling one row; the rules do not change (CoverTests proves it with
// a test-only "fire" row whose high ground is magma crust).
//
//   slot     chosen where (first match wins)
//   shore    the ground is at most SHORE_ABOVE over the sea: the beach band and
//            the sea bed (before 177: a beach rule that named BeachSand)
//   hollow   under a lake or river (its bed)
//   steep    the 6 m-scale slope is over ~30 deg (the record's scramble|climb
//            band edge, Danger.SCRAMBLE_DEG); on high ground over ~45 deg
//            (climb|sheer, Danger.CLIMB_DEG: only faces stay bare there)
//   high     altitude over HIGH_FRACTION of the world's height range
//            (spec.peakHeight, the dominant summit every seed is built to;
//            2/3 = the record's top altitude third, Danger.AltitudeThird)
//   steep    below the high ground, a crest: >= RIDGE_DIP above its 6 m ring
//            AND above the +-120 m mean (c.regional)
//   hollow   a dip >= HOLLOW_DIP under its 6 m ring below the +-120 m mean,
//            or beside fresh water (a lake/river cell within ~16 m, ground at
//            most FRESH_BANK over its level)
//   flat     everything else
//
// Every input exists for every column of every seed: the map (height, water),
// the regional mean ShapeField.Sample already computes, and a SMOOTH height
// Hs = macro + rough * (detail octaves 0-2, wavelengths >= 16 m) sampled at
// +-COVER_SPAN (6 m). Every threshold is jittered by one coherent patch noise
// (6 m + 25 m), so slot borders run as organic patch edges of metres, never as
// a per-voxel dither (finding 2's speckle). BAND LIMIT: every cover noise and
// smooth-height octave shorter than two footprints (`texel`: 1 for chunks, the
// shell's voxel, the far band's texel) is dropped, as ShapeField drops detail
// octaves for the height -- else a far texel samples a 4.5 m patch noise once
// per 12.8 m and the far band turns into per-texel camo. At texel <= 22 (every
// chunk and the inner shells) nothing is dropped, so they choose the same slot.
//
// What fills a slot is DetailField's business (Material / TopMaterial /
// UniformSamples): a palette value of BARE means "no skin: the column's own
// rock shows" (steep ground shows the biome's strata, badlands keeps its
// bands). Falling solids still settle per voxel (Settle): the generator only
// places sand / snow where they are stable at rest.
using Unity.Mathematics;

namespace TerrainGen
{
    /// One biome's palette row, as data. Only tests and tools build these; the
    /// generator reads the packed table (Cover.PaletteMaterial).
    public struct CoverRow
    {
        public byte flat, flatVar, steep, steepVar, hollow, hollowVar, high, highVar, shore, shoreVar;

        public byte Get(int slot, bool variant)
        {
            switch (slot)
            {
                case Cover.Steep: return variant ? steepVar : steep;
                case Cover.Hollow: return variant ? hollowVar : hollow;
                case Cover.High: return variant ? highVar : high;
                case Cover.Shore: return variant ? shoreVar : shore;
                default: return variant ? flatVar : flat;
            }
        }
    }

    public static class Cover
    {
        // ---- slots (ColumnSample.cover, bits 0-2; bit 3 = the patch variant) ----
        public const byte Flat = 0, Steep = 1, Hollow = 2, High = 3, Shore = 4;
        public const int SlotCount = 5;
        public const byte VARIANT_BIT = 8;
        public static int Slot(byte cover) => cover & 7;
        public static bool IsVariant(byte cover) => (cover & VARIANT_BIT) != 0;
        public static string SlotName(int s) => s == Steep ? "steep" : s == Hollow ? "hollow" : s == High ? "high" : s == Shore ? "shore" : "flat";

        /// Palette value: no skin, the column's own rock (DetailField.Strata) shows.
        public const byte BARE = 255;

        // ---- the shape rules' constants (voxels; 0.1 m) ----
        /// Half-span of the smooth-height probe: the slope / curvature scale.
        public const float COVER_SPAN = 60f;
        /// Detail octaves in the smooth height: 0-2 = 64, 32, 16 m.
        public const int COVER_OCTAVES = 3;
        /// tan of the steep slope edge, the record's scramble | climb edge.
        public const float STEEP_DEG = Danger.SCRAMBLE_DEG;
        /// On high ground only faces are steep: the record's climb | sheer edge.
        public const float FACE_DEG = Danger.CLIMB_DEG;
        /// Jitter of that edge by the patch noise (degrees).
        public const float STEEP_JITTER_DEG = 6f;
        /// A crest: the centre stands this far above its 6 m ring (0.5 m).
        public const float RIDGE_DIP = 5f;
        /// A dip: the centre lies this far below its 6 m ring (0.4 m).
        public const float HOLLOW_DIP = 4f;
        /// The `high` slot from this fraction of the world's height range.
        public const float HIGH_FRACTION = 2f / 3f;
        /// Its jitter, as a fraction of the range (a ragged line, not a contour).
        public const float HIGH_JITTER = 0.05f;
        /// Shore: the ground at most this far over the sea (= the pre-177 beach band top).
        public const int SHORE_ABOVE = 22;
        /// Beside fresh water: a water cell centre within this reach (16 m) ...
        public const float FRESH_REACH = 160f;
        /// ... and the ground at most this far over its level (2.5 m).
        public const int FRESH_BANK = 25;
        /// Patch noise wavelengths (6 m patches clustered at 25 m) and the variant's (15 m, 4.5 m).
        public const float PATCH_WAVE = 60f, CLUSTER_WAVE = 250f, VARIANT_WAVE = 150f, VARIANT_FINE_WAVE = 45f;
        /// The variant covers a slot where its noise exceeds this (~20-25 % of each slot).
        public const float VARIANT_ABOVE = 0.3f;

        // ---- THE PALETTE: biome x (slot main, slot variant), task 177's first proposal ----
        // Row order = GenBiome ids; columns = flat, flatVar, steep, steepVar, hollow,
        // hollowVar, high, highVar, shore, shoreVar. Variant == main means none.
        public const int ROW = 2 * SlotCount;
        private static readonly byte[] PALETTE = {
            // forest
            GenMat.Grass, GenMat.JungleGrass,        BARE, GenMat.Gravel,                 GenMat.Gravel, GenMat.Loam,
            GenMat.Gravel, BARE,                     GenMat.BeachSand, GenMat.Gravel,
            // desert
            GenMat.Sand, GenMat.PackedSand,          BARE, GenMat.CreamSandstone,         GenMat.PackedSand, GenMat.Gravel,
            GenMat.CreamSandstone, BARE,             GenMat.BeachSand, GenMat.BeachSand,
            // snow (steep: wind-packed ice with rock outcrops -- the snow biome keeps its white flanks)
            GenMat.Snow, GenMat.PackedIce,           GenMat.PackedIce, BARE,              GenMat.PackedIce, GenMat.Snow,
            GenMat.Snow, GenMat.PackedIce,           GenMat.Snow, GenMat.Gravel,
            // jungle
            GenMat.JungleGrass, GenMat.Grass,        BARE, GenMat.MossRock,               GenMat.Mud, GenMat.JungleGrass,
            GenMat.MossRock, BARE,                   GenMat.BeachSand, GenMat.BeachSand,
            // savanna
            GenMat.SavannaGrass, GenMat.RedEarth,    BARE, GenMat.Gravel,                 GenMat.Grass, GenMat.RedEarth,
            BARE, GenMat.Gravel,                     GenMat.BeachSand, GenMat.BeachSand,
            // badlands
            GenMat.BadlandsClay, GenMat.Hardpan,     BARE, GenMat.Hardpan,                GenMat.Hardpan, GenMat.Gravel,
            GenMat.CreamBand, BARE,                  GenMat.BeachSand, GenMat.BeachSand,
        };

        /// The material a biome's palette puts in this cover (slot + variant), or BARE.
        public static byte PaletteMaterial(byte biome, byte cover)
        {
            int b = biome < GenBiome.Count ? biome : 0;
            return PALETTE[b * ROW + Slot(cover) * 2 + (IsVariant(cover) ? 1 : 0)];
        }

        /// A biome's row as data (tests, tools, the owner's table).
        public static CoverRow Row(byte biome)
        {
            int o = (biome < GenBiome.Count ? biome : 0) * ROW;
            return new CoverRow
            {
                flat = PALETTE[o], flatVar = PALETTE[o + 1], steep = PALETTE[o + 2], steepVar = PALETTE[o + 3],
                hollow = PALETTE[o + 4], hollowVar = PALETTE[o + 5], high = PALETTE[o + 6], highVar = PALETTE[o + 7],
                shore = PALETTE[o + 8], shoreVar = PALETTE[o + 9],
            };
        }

        /// The row's material for a cover: what DetailField does with PaletteMaterial.
        public static byte Material(in CoverRow row, byte cover) => row.Get(Slot(cover), IsVariant(cover));

        /// A falling solid only where it is stable at rest (the voxel's top step
        /// <= 1, ShapeField.MaxStep); elsewhere its packed form. Material data,
        /// not a biome rule: the pre-177 Surface did exactly this per biome.
        public static byte Settle(byte m, float stepAtTop)
        {
            if (stepAtTop <= 1f) return m;
            switch (m)
            {
                case GenMat.Sand: case GenMat.BeachSand: return GenMat.PackedSand;
                case GenMat.Snow: return GenMat.PackedIce;
                default: return m;
            }
        }

        // ------------------------------------------------------------------
        // THE SLOT RULES. Pure function of the column: same answer wherever it
        // is asked (chunk, shell, far band, point query), and no biome input.
        // ------------------------------------------------------------------

        /// The smooth height Hs (voxels rel. sea) at p: macro + octaves 0-2.
        public static float SmoothHeight(in MapView map, in Offsets o, float2 p, int octaves)
        {
            ShapeField.SampleMap(in map, p.x, p.y, out float macro, out float rough);
            return macro + rough * LowDetail(in o, p, octaves);
        }

        private static float LowDetail(in Offsets o, float2 p, int octaves)
        {
            float d = 0f;
            for (int i = 0; i < octaves; i++) d += ShapeField.Amplitude(i) * ShapeField.OctaveNoise(in o, p, i);
            return d;
        }

        /// An octave of wavelength `wave` is kept at this footprint (>= 2 texels).
        private static bool Keeps(float wave, int texel) => wave >= 2f * texel;

        /// The coherent patch noise in about [-1, 1] that jitters every threshold.
        public static float Patch(in Offsets o, float2 p, int texel)
            => (Keeps(PATCH_WAVE, texel) ? 0.65f * noise.snoise(p / PATCH_WAVE + o.soil + new float2(17.3f, -41.9f)) : 0f)
             + (Keeps(CLUSTER_WAVE, texel) ? 0.35f * noise.snoise(p / CLUSTER_WAVE + o.soil + new float2(-63.1f, 25.7f)) : 0f);

        public static float VariantNoise(in Offsets o, float2 p, int texel)
            => (Keeps(VARIANT_WAVE, texel) ? 0.7f * noise.snoise(p / VARIANT_WAVE + o.strata + new float2(38.9f, 71.3f)) : 0f)
             + (Keeps(VARIANT_FINE_WAVE, texel) ? 0.3f * noise.snoise(p / VARIANT_FINE_WAVE + o.strata + new float2(-12.7f, -90.1f)) : 0f);

        /// The cover byte (slot | variant) of the column at p. macro / rough are
        /// the map at p (ShapeField.Sample's own), height / water / regional the
        /// column's final values; texel the footprint (see BAND LIMIT above).
        public static byte Choose(in TerrainSpec spec, in MapView map, in Offsets o, float2 p,
                                  float macro, float rough, int height, int water, float regional, int texel)
        {
            int slot = ChooseSlot(in spec, in map, in o, p, macro, rough, height, water, regional, texel);
            return (byte)(slot | (VariantNoise(in o, p, texel) > VARIANT_ABOVE ? VARIANT_BIT : 0));
        }

        public static int ChooseSlot(in TerrainSpec spec, in MapView map, in Offsets o, float2 p,
                                     float macro, float rough, int height, int water, float regional, int texel)
        {
            if (height <= spec.seaLevelY + SHORE_ABOVE) return Shore;
            if (water > height) return Hollow;

            float n = Patch(in o, p, texel);
            int oct = math.min(COVER_OCTAVES, ShapeField.OctavesFor(texel));
            float h0 = macro + rough * LowDetail(in o, p, oct);
            float hE = SmoothHeight(in map, in o, p + new float2(COVER_SPAN, 0f), oct);
            float hW = SmoothHeight(in map, in o, p - new float2(COVER_SPAN, 0f), oct);
            float hN = SmoothHeight(in map, in o, p + new float2(0f, COVER_SPAN), oct);
            float hS = SmoothHeight(in map, in o, p - new float2(0f, COVER_SPAN), oct);
            float gx = (hE - hW) * (0.5f / COVER_SPAN), gz = (hN - hS) * (0.5f / COVER_SPAN);
            float slopeDeg = math.degrees(math.atan(math.sqrt(gx * gx + gz * gz)));
            float dip = 0.25f * (hE + hW + hN + hS) - h0;          // > 0: the centre lies low (hollow); < 0: a crest
            float rel = h0 - (regional - spec.seaLevelY);          // over the +-120 m mean

            // High ground keeps its cover up to the FACES (C3: "snow on flats and in
            // hollows; steep faces stay bare"): there only ground over FACE_DEG is steep.
            bool high = height - spec.seaLevelY > (HIGH_FRACTION + HIGH_JITTER * n) * spec.peakHeight;
            if (slopeDeg > (high ? FACE_DEG : STEEP_DEG) + STEEP_JITTER_DEG * n) return Steep;
            if (high) return High;
            if (-dip > RIDGE_DIP * (1f + 0.5f * n) && rel > 0f) return Steep;
            if (dip > HOLLOW_DIP * (1f - 0.5f * n) && rel < 0f) return Hollow;
            if (BesideFreshWater(in map, p, h0, FRESH_REACH * (1f + 0.4f * n))) return Hollow;
            return Flat;
        }

        /// A lake / river cell centre within `reach` of p whose level is at most
        /// FRESH_BANK under the ground (hRel: the SMOOTH ground, voxels rel. sea,
        /// so the answer ignores the footprint like the other shape inputs).
        private static unsafe bool BesideFreshWater(in MapView map, float2 p, float hRel, float reach)
        {
            if (map.water == null) return false;
            float cv = map.cellVoxels;
            int ix = (int)math.floor(p.x / cv), iz = (int)math.floor(p.y / cv);
            int r = (int)math.ceil(reach / cv);
            int n = map.cells;
            float reach2 = reach * reach;
            for (int dz = -r; dz <= r; dz++)
            for (int dx = -r; dx <= r; dx++)
            {
                int cx = ix + dx, cz = iz + dz;
                if (cx < 0 || cz < 0 || cx >= n || cz >= n) continue;
                float w = map.water[cz * n + cx];
                if (w < Hydrology.NO_WATER * 0.5f || hRel - w > FRESH_BANK) continue;
                float ex = (cx + 0.5f) * cv - p.x, ez = (cz + 0.5f) * cv - p.y;
                if (ex * ex + ez * ez <= reach2) return true;
            }
            return false;
        }
    }
}
