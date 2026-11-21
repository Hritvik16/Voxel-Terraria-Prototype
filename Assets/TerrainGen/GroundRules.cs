// Assets/TerrainGen/GroundRules.cs
//
// Task 180b (owner, 2026-11-06: "the layered rock tables are too complicated and
// the world looks off"; memory simple-material-rules). THE DEFAULT since task 186
// (owner 2026-11-07, Q-M (a)); `-matrules 0` (TerrainSpec.matRulesOff = 1) is the
// 177-179 path, byte for byte. With it on, a biome's ground is a SHORT LIST OF RULES over a few local
// inputs, and THE BIOME PAINTS EVERYTHING: skin, soil, subsoil AND stone come
// from one palette row per biome; the inputs only choose WHICH of the row's
// materials goes WHERE. No 3-D noise, no per-voxel search, no LAYER_ROCKS, no
// partner-brick mix, no rock-biome layer A/B (they stay for the old path).
// The world is "one wacky island with a realistic twist": the RULES are
// physical; the PALETTE ROWS are where the wackiness lives (a desert oasis
// grows grass, a jungle's deep stone is jade serpentine).
//
// MATERIAL = f(column values, depth). Per COLUMN, once (Column, called last in
// ShapeField.Sample): slope and curvature at 6 m (Cover's probe), height over
// the sea, the side facing the sun, the nearest fresh water (MAP-BAKED shore
// field, BakeShore: one pass per map) and the ground's height over that
// water's surface, the steepest ground within 20 m upslope, the world record's
// route flag, and ONE slow 2-D noise per biome (BiomeNoise, 300 m). Per VOXEL
// only the depth below the column's ORIGINAL generated surface is new (Band).
//
// THE SLOT RULES, first match wins (ChooseSlot; MaterialRuleTests pins every
// conflict pair named here):
//   1 shore   ground at most SHORE_ABOVE (2.2 m) over the sea: beach / sea bed.
//             high + shore: shore (a beach is never "high").
//   2 hollow  under a lake or river (its bed). Under lava (the caldera) the
//             bed is the FLUID's own shore material (FLUID_BED, FluidShore).
//   3 steep   the 6 m slope over 30 deg (on high ground 45 deg: only faces),
//             +-6 deg patch jitter, and SUN_DEG steeper on the shaded side
//             (shaded slopes keep their snow / moss longer). It is BARE (no
//             skin, no soil: the stone shows) on a CREST or on the SUNNY side
//             ("sunny sides show dry rock"); else the row's steep skin.
//             steep + wet: steep (mud does not stay on a 30 deg bank).
//   4 hollow  a WET BANK: fresh water within WET_REACH (12 m) and the ground
//             at most WET_ABOVE (1 m) over that water's surface: the mud
//             collar / oasis / swamp. A 3 m bank is wet at its foot only.
//             wet + high: wet (a tarn's bank is a bank).
//   5 high    altitude over 2/3 of the world's height range (the snow line),
//             lower on the shaded side by HIGH_SUN of the range.
//   6 scree   flat-ish ground with ground over SCREE_UP_DEG (40 deg) within
//             20 m UPSLOPE, or task 178's apron flag: the row's rubble.
//             scree + route: the route (a route corridor keeps its surface).
//   7 dip     >= 0.4 m under its 6 m ring: soil collects (twice as deep),
//             the flat skin on top. hollow + crest (a saddle on a ridge):
//             the dip wins -- soil collects wherever the ground is concave.
//   8 steep   a convex crest above the +-120 m mean: thin skin (as Cover).
//   9 flat    everything else.
//
// DEPTH BANDS (Band), monotone in depth so a brick's samples can be proven
// uniform from the two ends (DetailField.UniformSamples):
//   0 skin      depth < 0.3 m: the slot's entry (falling solids Settle'd)
//   1 soil      to mrSoil: the biome's soil; a beach's sand body, a scree's
//               rubble, a lava bed's crust continue instead
//   2 subsoil   to mrSub
//   3 rock      everything deeper. WHICH rock is ABSOLUTE (RockAt), never by
//               depth: the biome's stone down to the generator's own stone /
//               deep-stone boundary (sea - DetailField.STONE_BOTTOM_BELOW_SEA
//               + the wavy deepOffset, the old path's line), deep stone under
//               it, then the cavern rock and the shared underworld.
// A BARE entry or a sheer voxel step (> DetailField.SHEER_STEP) skips 0-2.
// The rock deeper than brickGeologyDepth (20 m) is per BRICK as before (its
// boundaries by the brick centre's height and offsets).
// TASK 182 (defect 1 of 180b): the deep stone was "deeper than 11-19 m below
// the column's top", so the lower part of every tall face (a sheer column's
// side is all "deep" below that) turned deep stone, in 0.8 m brick steps --
// snow's blue slate read as black boxes. A face is one stone now; the deep
// stone is a world layer, seen only where the ground cuts below it.
//
// SHELL EXPOSURE (task 182, defect 2 of 180b): a coarse shell voxel (2^k
// columns wide) samples ONE column, so on a slope the voxels under its top are
// bared sideways down to the lower neighbour column, at depths far below the
// 0.3 m skin: far hills showed a brick-sized soil / subsoil / stone mosaic.
// ColumnSample.mrExpose (shells only, ShellColumnJob) is the depth to which the
// column's coarse voxels are exposed: max(2^k, h - the lowest of its four
// coarse neighbours). A voxel above that depth contains the LOD-0 surface (the
// fine columns there wear their skin), so it is band 0; a voxel deeper is not
// exposed and takes its own depth's band (the fine column's band at that
// point). At 2^k = 1 the rule is a no-op: a column's drop to a fine neighbour is
// its stepAtTop, <= SHEER_STEP = SKIN unless sheer (then rock first).
//
// BIOME BORDERS: the skin biome is the ROCK'S smooth border warp
// (ShapeField.RockBiomes' rockA / rockB, 800 m + 150 m warp, +-29 m split),
// interleaved in 25 m plan patches inside the split zone (BlendNoise), so a mud
// bank turns into a sand bank over a ~60 m band of patches, never in one step.
// Stone takes rockA alone (one smooth contact line: no plan patches on faces,
// which drew 178's vertical drips). Column sets c.surfBiome to the skin biome,
// so trees and the far band follow the same border.
//
// FLUIDS: water uses the biome's wet-bank (hollow) entry; every OTHER fluid
// owns its shore (FLUID_SHORE, a per-fluid table: lava -> magma crust, oil ->
// shale, ...), not biome x fluid. It lines the caldera's bed and the inner
// wall of every sealed fluid pocket (DetailField.Material; there is no
// generated steam, so steam has no row).
//
// EDITS: every input is the GENERATED map and the generated column (height,
// water map, record), never a live voxel or live fluid. A dug pit shows the
// subsoil, stone and deep stone of the ORIGINAL surface; a dammed river keeps
// its bed and banks; placed blocks are their own material; regenerating a
// chunk is a pure function of seed + edit log; the CPU stays the sole terrain
// writer. Overhangs and arches come only from STAMPS, which carry their own
// materials (stampMat / Carve3D run before these rules). Runtime regrowth (grass
// over exposed dirt, a new wet bank behind a dam) is OUT of scope: it would be
// one deterministic tick re-applying ChooseSlot's skin to exposed soil.
//
// ORE HOOK (task 181, not this task): the existing ores (Ores.At, by absolute
// layer) run unchanged after bands 0-2. A rule-based ore would hook in
// DetailField.Material's MatRules branch right before RockAt: a band (stone /
// deep), the column's biome and a noise threshold -- per voxel only the depth.
//
// BAND LIMIT: as Cover -- every noise and smooth-height octave shorter than two
// footprints is dropped, so at texel <= 22 (chunks, inner shells) the shape
// slots (steep, bare, scree, dip, crest, flat) are the same at every footprint;
// shore / bed / wet / high use the column's own height, as Cover's do.
using Unity.Mathematics;

namespace TerrainGen
{
    /// One biome's palette row as data (tests, tools, the owner's table).
    public struct MaterialRow
    {
        public byte flat, flatVar, steep, steepVar, hollow, hollowVar, high, highVar, shore, shoreVar,
                    rubble, rubbleVar, soil, subsoil, stone, deep, cavern;
    }

    /// Everything ChooseSlot reads, so the conflict pairs can be tested directly.
    public struct RuleInputs
    {
        public bool shoreLow, underWater, wet, route, screeApron;
        public float slopeDeg, dip, rel, overSea, peak, n, sunny, upSlopeDeg;
    }

    public static class GroundRules
    {
        // ---- slots (ColumnSample.mrSlot bits 0-2; bit 3 variant; bit 4 fluid bed) ----
        public const byte Flat = 0, Steep = 1, Hollow = 2, High = 3, Shore = 4, Scree = 5, Dip = 6, Bare = 7;
        public const int SlotCount = 8;
        public const byte VARIANT_BIT = 8, FLUID_BED = 16;
        public static int Slot(byte s) => s & 7;
        public static bool IsVariant(byte s) => (s & VARIANT_BIT) != 0;
        public static string SlotName(int s)
            => s == Steep ? "steep" : s == Hollow ? "hollow" : s == High ? "high" : s == Shore ? "shore"
             : s == Scree ? "scree" : s == Dip ? "dip" : s == Bare ? "bare" : "flat";
        /// The Cover slot (trees, tools) that a rule slot stands for.
        public static byte LegacySlot(int s)
            => s == Scree || s == Bare ? Cover.Steep : s == Dip ? Cover.Flat : (byte)s;

        public const byte BARE = Cover.BARE;
        public const int SKIN = DetailField.SURFACE_THICKNESS;   // 0.3 m

        // ---- rule constants (voxels; 0.1 m). Thresholds Cover already has are Cover's. ----
        /// Sun side: SunCycle's arc leans toward +Z (LightingConfig.sunArcTilt > 0),
        /// so a slope whose normal points +Z faces the sun. A world convention, not a seed value.
        public const float SUN_Z = 1f;
        /// The steep edge moves this many degrees per unit of shade (|sunny| <= ~0.7).
        public const float SUN_DEG = 10f;
        /// Steep ground facing the sun this much (normal.z) is bare rock.
        public const float SUNNY_BARE = 0.25f;
        /// The snow line moves by this fraction of the height range per unit of sun.
        public const float HIGH_SUN = 0.08f;
        /// Wet bank: fresh water within this reach (12 m) ...
        public const float WET_REACH = 120f;
        /// ... and the ground at most this far over its surface (1 m).
        public const float WET_ABOVE = 10f;
        /// Scree: ground steeper than this within SCREE_PROBE upslope.
        public const float SCREE_UP_DEG = 40f, SCREE_PROBE = 200f;
        /// The shore field's reach in map cells (4 x 8 m: further than WET_REACH * 1.4 + a cell).
        public const int SHORE_R = 4;
        /// One slow noise per biome (300 m) and the border blend's plan patches (25 m).
        public const float BIOME_WAVE = 3000f, BLEND_WAVE = 250f;
        /// Soil / subsoil thickness factor by the biome noise.
        public const float THICK_NOISE = 0.35f;                     // soil / subsoil x (1 +- 0.35)
        public const float POCKET_LINING = 1.1f;                    // pocket wall lining: d^2 in [1, 1.1)

        // ---- THE PALETTE: one row per biome (GenBiome ids) ----
        // flat, flatVar, steep, steepVar, hollow, hollowVar, high, highVar, shore, shoreVar,
        // rubble, rubbleVar, soil, subsoil, stone, deep stone, cavern rock. Variant == main: none.
        public const int ROW = 17;
        private static readonly byte[] PALETTE = {
            // forest: grass on loam over ochre clay over granite; mud collars, gravel talus
            GenMat.Grass, GenMat.JungleGrass,   BARE, GenMat.Gravel,            GenMat.Mud, GenMat.Loam,
            GenMat.Gravel, BARE,                GenMat.BeachSand, GenMat.Gravel, GenMat.Gravel, GenMat.Gravel,
            GenMat.Loam, GenMat.OchreClay,      GenMat.Granite, GenMat.Slate,  GenMat.Shale,
            // desert: sand over packed sand over pink siltstone over orange sandstone; an OASIS by water
            GenMat.Sand, GenMat.PackedSand,     BARE, BARE,                     GenMat.Grass, GenMat.Mud,
            GenMat.CreamSandstone, BARE,        GenMat.BeachSand, GenMat.BeachSand, GenMat.Gravel, GenMat.PackedSand,
            GenMat.PackedSand, GenMat.PinkSiltstone, GenMat.OrangeSandstone, GenMat.RedSandstone, GenMat.MaroonMudstone,
            // snow: snow, wind-packed ice on shaded steeps, frozen banks, dark talus, schist
            GenMat.Snow, GenMat.PackedIce,      GenMat.PackedIce, BARE,         GenMat.PackedIce, GenMat.Snow,
            GenMat.Snow, GenMat.PackedIce,      GenMat.Snow, GenMat.Gravel,     GenMat.Gravel, GenMat.Gravel,
            GenMat.PackedIce, GenMat.Gravel,    GenMat.SilverSchist, GenMat.BlueSlate, GenMat.Basalt,
            // jungle: swamp mud by water, mossy talus, laterite subsoil, karst, jade deep down
            GenMat.JungleGrass, GenMat.Grass,   BARE, GenMat.MossRock,          GenMat.Mud, GenMat.JungleGrass,
            GenMat.MossRock, BARE,              GenMat.BeachSand, GenMat.BeachSand, GenMat.MossRock, GenMat.Gravel,
            GenMat.Mud, GenMat.Laterite,        GenMat.KarstLimestone, GenMat.JadeSerpentine, GenMat.BlackBasalt,
            // savanna: golden grass, green collars by water, red earth, kopje granite
            GenMat.SavannaGrass, GenMat.PackedSand, BARE, GenMat.Gravel,        GenMat.Grass, GenMat.Mud,
            BARE, GenMat.Gravel,                GenMat.BeachSand, GenMat.BeachSand, GenMat.Gravel, GenMat.RedEarth,
            GenMat.RedEarth, GenMat.PinkSiltstone, GenMat.KopjeGranite, GenMat.Ironstone, GenMat.Gneiss,
            // badlands: cracked clay, hardpan, ochre subsoil, ONE red stone (no stripes: owner)
            GenMat.BadlandsClay, GenMat.Hardpan, BARE, GenMat.Hardpan,          GenMat.Mud, GenMat.Hardpan,
            GenMat.CreamBand, BARE,             GenMat.BeachSand, GenMat.BeachSand, GenMat.Hardpan, GenMat.OchreBand,
            GenMat.Hardpan, GenMat.OchreBand,   GenMat.RedBand, GenMat.UmberMudstone, GenMat.PlumShale,
        };
        private const int I_RUBBLE = 10, I_SOIL = 12, I_SUB = 13, I_STONE = 14, I_DEEP = 15, I_CAVERN = 16;

        private static int RowAt(byte biome) => (biome < GenBiome.Count ? biome : 0) * ROW;

        public static MaterialRow Row(byte biome)
        {
            int o = RowAt(biome);
            return new MaterialRow
            {
                flat = PALETTE[o], flatVar = PALETTE[o + 1], steep = PALETTE[o + 2], steepVar = PALETTE[o + 3],
                hollow = PALETTE[o + 4], hollowVar = PALETTE[o + 5], high = PALETTE[o + 6], highVar = PALETTE[o + 7],
                shore = PALETTE[o + 8], shoreVar = PALETTE[o + 9], rubble = PALETTE[o + 10], rubbleVar = PALETTE[o + 11],
                soil = PALETTE[o + 12], subsoil = PALETTE[o + 13], stone = PALETTE[o + 14], deep = PALETTE[o + 15],
                cavern = PALETTE[o + 16],
            };
        }

        /// The skin entry of a slot (Dip wears the flat skin, Scree the rubble; Bare is BARE).
        public static byte Entry(byte biome, int slot, bool variant)
        {
            int o = RowAt(biome), v = variant ? 1 : 0;
            switch (slot)
            {
                case Bare: return BARE;
                case Scree: return PALETTE[o + I_RUBBLE + v];
                case Dip: return PALETTE[o + v];
                default: return PALETTE[o + slot * 2 + v];   // Flat 0, Steep 1, Hollow 2, High 3, Shore 4
            }
        }

        public static byte Soil(byte biome) => PALETTE[RowAt(biome) + I_SOIL];
        public static byte Subsoil(byte biome) => PALETTE[RowAt(biome) + I_SUB];
        public static byte Stone(byte biome) => PALETTE[RowAt(biome) + I_STONE];
        public static byte DeepStone(byte biome) => PALETTE[RowAt(biome) + I_DEEP];
        public static byte CavernRock(byte biome) => PALETTE[RowAt(biome) + I_CAVERN];

        /// THE PER-FLUID SHORE TABLE: what lines a non-water fluid (its bed, a pocket's
        /// wall). 0 for water: water uses the biome's hollow entry. No steam is generated.
        public static byte FluidShore(byte fluid)
        {
            switch (fluid)
            {
                case GenMat.Lava: return GenMat.MagmaCrust;
                case GenMat.Oil: return GenMat.Shale;
                case GenMat.Acid: return GenMat.OchreClay;
                case GenMat.Sludge: return GenMat.Mud;
                case GenMat.Honey: return GenMat.OchreBand;
                case GenMat.Quicksand: return GenMat.PackedSand;
                default: return 0;
            }
        }

        /// A sealed pocket's inner wall lining (d^2 in [1, POCKET_LINING)).
        public static byte PocketLining(byte fluid, byte biome)
        {
            byte m = FluidShore(fluid);
            if (m != 0) return m;
            byte h = Entry(biome, Hollow, false);
            return h == BARE ? Stone(biome) : h;
        }

        // ------------------------------------------------------------------
        // THE SLOT CHOICE: a pure function of RuleInputs (the header's list).
        // ------------------------------------------------------------------
        public static int ChooseSlot(in RuleInputs i)
        {
            if (i.shoreLow) return Shore;
            if (i.underWater) return Hollow;
            float n = i.n;
            bool high = i.overSea > (Cover.HIGH_FRACTION + Cover.HIGH_JITTER * n + HIGH_SUN * i.sunny) * i.peak;
            float steepAt = (high ? Cover.FACE_DEG : Cover.STEEP_DEG) + Cover.STEEP_JITTER_DEG * n - SUN_DEG * i.sunny;
            bool crest = -i.dip > Cover.RIDGE_DIP * (1f + 0.5f * n);
            if (i.slopeDeg > steepAt) return crest || i.sunny > SUNNY_BARE ? Bare : Steep;
            if (i.wet) return Hollow;
            if (high) return High;
            if (!i.route && (i.screeApron || i.upSlopeDeg > SCREE_UP_DEG + Cover.STEEP_JITTER_DEG * n)) return Scree;
            if (i.dip > Cover.HOLLOW_DIP * (1f - 0.5f * n)) return Dip;
            if (crest && i.rel > 0f) return Steep;
            return Flat;
        }

        /// Keep an octave of wavelength `wave` at this footprint (>= 2 texels), as Cover.
        private static bool Keeps(float wave, int texel) => wave >= 2f * texel;

        /// ONE slow 2-D noise per biome, in [-1, 1] (0 where band-limited away).
        public static float BiomeNoise(in Offsets o, float2 p, byte biome, int texel)
        {
            if (!Keeps(BIOME_WAVE, texel)) return 0f;
            float k = biome * 37.1f;
            return noise.snoise(p / BIOME_WAVE + o.soil.yx + new float2(11.3f + k, -27.7f - 0.61f * k));
        }

        /// The border blend's plan patches: > 0 = the skin wears rockA, else rockB.
        public static float BlendNoise(in Offsets o, float2 p, int texel)
            => Keeps(BLEND_WAVE, texel) ? noise.snoise(p / BLEND_WAVE + o.strata.yx + new float2(-33.9f, 58.1f)) : 0f;

        // ------------------------------------------------------------------
        // THE SHORE FIELD (one pass per map): per map cell, the nearest lake /
        // river cell within SHORE_R cells -- ties by (d^2, dz, dx) -- packed as
        // bit 15 = found, bits 0-3 dx + 8, bits 4-7 dz + 8. MapView.shore holds
        // the baked plane; without it ShoreCell scans, with the same answer.
        // ------------------------------------------------------------------
        public const ushort SHORE_FOUND = 0x8000;

        public static unsafe ushort ShoreCell(in MapView map, int cx, int cz)
        {
            if (map.water == null) return 0;
            int n = map.cells, best = int.MaxValue;
            ushort e = 0;
            for (int dz = -SHORE_R; dz <= SHORE_R; dz++)
            for (int dx = -SHORE_R; dx <= SHORE_R; dx++)
            {
                int x = cx + dx, z = cz + dz;
                if (x < 0 || z < 0 || x >= n || z >= n) continue;
                if (!(map.water[z * n + x] > Hydrology.NO_WATER * 0.5f)) continue;
                int d2 = dx * dx + dz * dz;
                if (d2 < best) { best = d2; e = (ushort)(SHORE_FOUND | ((dz + 8) << 4) | (dx + 8)); }
            }
            return e;
        }

        /// The whole plane: ShoreCell for every cell, by stamping round each water
        /// cell (same comparison and tie order, so the same bytes; few % of cells are water).
        public static unsafe ushort[] BakeShore(in MapView map)
        {
            int n = map.cells;
            var e = new ushort[n * n];
            if (map.water == null) return e;
            var best = new int[n * n];
            for (int i = 0; i < best.Length; i++) best[i] = int.MaxValue;
            for (int wz = 0; wz < n; wz++)
            for (int wx = 0; wx < n; wx++)
            {
                if (!(map.water[wz * n + wx] > Hydrology.NO_WATER * 0.5f)) continue;
                for (int dz = -SHORE_R; dz <= SHORE_R; dz++)
                for (int dx = -SHORE_R; dx <= SHORE_R; dx++)
                {
                    int cx = wx - dx, cz = wz - dz;   // the cell that sees this water cell at (dx, dz)
                    if (cx < 0 || cz < 0 || cx >= n || cz >= n) continue;
                    int i = cz * n + cx;
                    // ShoreCell's order: d^2 first, then the scan order (dz, then dx), first wins.
                    int key = ((dx * dx + dz * dz) << 8) | ((dz + 8) << 4) | (dx + 8);
                    if (key < best[i]) { best[i] = key; e[i] = (ushort)(SHORE_FOUND | ((dz + 8) << 4) | (dx + 8)); }
                }
            }
            return e;
        }

        /// The nearest fresh water to p: the box distance to its cell (voxels) and its
        /// level (voxels rel. sea). False when none within the field's reach.
        public static unsafe bool NearestWater(in MapView map, float2 p, out float dist, out float level)
        {
            dist = float.MaxValue; level = Hydrology.NO_WATER;
            if (map.water == null) return false;
            float cv = map.cellVoxels;
            int n = map.cells;
            int cx = math.clamp((int)math.floor(p.x / cv), 0, n - 1), cz = math.clamp((int)math.floor(p.y / cv), 0, n - 1);
            ushort e = map.shore != null ? map.shore[cz * n + cx] : ShoreCell(in map, cx, cz);
            if ((e & SHORE_FOUND) == 0) return false;
            int wx = cx + (e & 15) - 8, wz = cz + ((e >> 4) & 15) - 8;
            level = map.water[wz * n + wx];
            float2 d = math.max(math.abs(p - new float2((wx + 0.5f) * cv, (wz + 0.5f) * cv)) - 0.5f * cv, 0f);
            dist = math.length(d);
            return true;
        }

        // ------------------------------------------------------------------
        // THE COLUMN PASS (ShapeField.Sample, last, when spec.MatRules).
        // ------------------------------------------------------------------
        /// The rule inputs of the column at p (c: its height, water, regional so far).
        public static unsafe RuleInputs Inputs(in TerrainSpec spec, in MapView map, in Offsets o, float2 p,
                                               float macro, float rough, bool screeApron, int texel, in ColumnSample c)
        {
            float n = Cover.Patch(in o, p, texel);
            int oct = math.min(Cover.COVER_OCTAVES, ShapeField.OctavesFor(texel));
            float h0 = macro + rough * Cover.LowDetail(in o, p, oct);
            float hE = Cover.SmoothHeight(in map, in o, p + new float2(Cover.COVER_SPAN, 0f), oct);
            float hW = Cover.SmoothHeight(in map, in o, p - new float2(Cover.COVER_SPAN, 0f), oct);
            float hN = Cover.SmoothHeight(in map, in o, p + new float2(0f, Cover.COVER_SPAN), oct);
            float hS = Cover.SmoothHeight(in map, in o, p - new float2(0f, Cover.COVER_SPAN), oct);
            float gx = (hE - hW) * (0.5f / Cover.COVER_SPAN), gz = (hN - hS) * (0.5f / Cover.COVER_SPAN);
            float g2 = gx * gx + gz * gz, gl = math.sqrt(g2);

            var ri = new RuleInputs
            {
                shoreLow = c.height <= spec.seaLevelY + Cover.SHORE_ABOVE,
                underWater = c.water > c.height,
                slopeDeg = math.degrees(math.atan(gl)),
                dip = 0.25f * (hE + hW + hN + hS) - h0,
                rel = h0 - (c.regional - spec.seaLevelY),
                overSea = c.height - spec.seaLevelY,
                peak = spec.peakHeight,
                n = n,
                sunny = SUN_Z * -gz / math.sqrt(1f + g2),   // the normal's z: + faces the sun
                screeApron = screeApron && !(c.water > c.height),
            };
            int cell = -1;
            {
                float cv = map.cellVoxels;
                int cx = (int)math.floor(p.x / cv), cz = (int)math.floor(p.y / cv);
                if (cx >= 0 && cz >= 0 && cx < map.cells && cz < map.cells) cell = cz * map.cells + cx;
            }
            ri.route = map.route != null && cell >= 0 && map.route[cell] != 0;
            if (!ri.shoreLow && !ri.underWater)
            {
                if (NearestWater(in map, p, out float wd, out float wl))
                    ri.wet = wd <= WET_REACH * (1f + 0.4f * n) &&
                             c.height - (spec.seaLevelY + (int)math.floor(wl)) <= WET_ABOVE * (1f + 0.5f * n);
                if (gl > 1e-4f)
                {
                    // The steepest of the two 10 m segments upslope (smooth height, as the slope).
                    float2 u = new float2(gx, gz) / gl;
                    float h1 = Cover.SmoothHeight(in map, in o, p + u * (0.5f * SCREE_PROBE), oct);
                    float h2 = Cover.SmoothHeight(in map, in o, p + u * SCREE_PROBE, oct);
                    float s = math.max(h1 - h0, h2 - h1) / (0.5f * SCREE_PROBE);
                    ri.upSlopeDeg = math.degrees(math.atan(math.max(s, 0f)));
                }
            }
            return ri;
        }

        public static void Column(in TerrainSpec spec, in MapView map, in Offsets o, float2 p,
                                  float macro, float rough, bool screeApron, int texel, ref ColumnSample c)
        {
            // The skin biome: the rock's smooth border warp, plan-patched inside the split zone.
            byte bA = DetailField.RockA(in c), bB = DetailField.RockB(in c);
            byte skin = bA == bB ? bA : (BlendNoise(in o, p, texel) > 0f ? bA : bB);
            c.surfBiome = skin;
            var ri = Inputs(in spec, in map, in o, p, macro, rough, screeApron, texel, in c);
            int slot = ChooseSlot(in ri);
            float vn = Cover.VariantNoise(in o, p, texel);
            float bn = BiomeNoise(in o, p, skin, texel);
            bool noisePatch = vn > Cover.VARIANT_ABOVE - 0.15f * bn;
            // Task 188a / 190a: no random spray on the MATERIALS by default (`-matvariant 1` = the spray)
            // (every column takes its slot's MAIN entry); trees keep the noise patch.
            bool variant = noisePatch && spec.MatVariant;
            byte s8 = (byte)(slot | (variant ? VARIANT_BIT : 0));
            if (slot == Hollow && ri.underWater && c.waterKind == 1) s8 |= FLUID_BED;
            c.mrSlot = s8;
            c.cover = (byte)(LegacySlot(slot) | (noisePatch ? Cover.VARIANT_BIT : 0));   // trees read the Cover slot

            // Thickness by slot x the biome noise.
            float f = 1f + THICK_NOISE * bn;
            int soil, sub;
            switch (slot)
            {
                case Dip: soil = 16; sub = 30; break;
                case Hollow: soil = 10; sub = 15; break;
                case High: soil = 4; sub = 10; break;
                case Shore: soil = 15; sub = 10; break;
                case Scree: soil = 15; sub = 10; break;
                case Steep: soil = 0; sub = 6; break;
                case Bare: soil = 0; sub = 0; break;
                default: soil = 8; sub = 20; break;
            }
            byte entry = (s8 & FLUID_BED) != 0 ? GenMat.MagmaCrust : Entry(skin, slot, variant);
            int soilD = 0, subD = 0;
            if (entry != BARE && slot != Bare)
            {
                soilD = math.max(SKIN, (int)math.floor(soil * f));
                subD = soilD + (int)math.floor(sub * f);
            }
            c.mrSoil = (byte)soilD;
            c.mrSub = (byte)subD;
        }

        // ------------------------------------------------------------------
        // PER VOXEL: the depth band and its material.
        // ------------------------------------------------------------------

        /// 0 skin, 1 soil, 2 subsoil, 3 rock (RockAt picks which, by absolute height),
        /// for the voxel `depth` under its ground. Non-decreasing in depth. A shell
        /// voxel above the column's exposure depth (mrExpose; 0 outside shells) is skin.
        public static int Band(in ColumnSample c, float stepAtTop, int depth)
        {
            if (c.mrSub == 0 || stepAtTop > DetailField.SHEER_STEP) return 3;
            if (depth < SKIN || depth < c.mrExpose) return 0;
            if (depth < c.mrSoil) return 1;
            if (depth < c.mrSub) return 2;
            return 3;
        }

        /// The material of band 0-2 in this column (never BARE: Band skips those).
        public static byte NearMaterial(in ColumnSample c, float stepAtTop, int band)
        {
            byte b = c.surfBiome;
            int slot = Slot(c.mrSlot);
            bool fluidBed = (c.mrSlot & FLUID_BED) != 0;
            if (band == 0)
                return fluidBed ? GenMat.MagmaCrust : Cover.Settle(Entry(b, slot, IsVariant(c.mrSlot)), stepAtTop);
            if (band == 1)
            {
                if (fluidBed) return GenMat.MagmaCrust;
                if (slot == Shore) return Cover.Settle(Entry(b, Shore, IsVariant(c.mrSlot)), stepAtTop);
                if (slot == Scree) return Entry(b, Scree, IsVariant(c.mrSlot));
                return Soil(b);
            }
            return Subsoil(b);
        }

        /// The deep stone's top (absolute Y before the wavy deepOffset): the
        /// generator's own stone / deep-stone boundary (DetailField.LayerRock's).
        public static int DeepTop(in TerrainSpec spec) => spec.seaLevelY - DetailField.STONE_BOTTOM_BELOW_SEA;

        /// Band 3's rock, ABSOLUTE at height y (+ the wavy offsets): the shared
        /// underworld, the biome's cavern rock, its deep stone, else its stone.
        public static byte RockAt(in TerrainSpec spec, in ColumnSample c, float y, float deepOff, float cavernOff, float underOff)
        {
            if (y + underOff < spec.UnderworldTopY) return DetailField.UnderworldRockAt(in spec, y + underOff);
            byte rb = DetailField.RockA(in c);
            if (y + cavernOff < spec.CavernTopY) return CavernRock(rb);
            return y + deepOff < DeepTop(in spec) ? DeepStone(rb) : Stone(rb);
        }

        /// RockAt for every y in [yLo, yHi] and every offset in its range, or 255.
        public static byte RockRange(in TerrainSpec spec, in ColumnSample c, float yLo, float yHi,
                                     float dLo, float dHi, float cLo, float cHi, float uLo, float uHi)
        {
            float gLo = yLo + uLo, gHi = yHi + uHi;
            if (gHi < spec.UnderworldTopY)
            {
                // DetailField.LayerRockRange's underworld proof (the same two cuts).
                int magma = spec.worldBottomY + DetailField.LAVA_ABOVE_FLOOR + 200, ash = spec.UnderworldTopY - 300;
                if (gHi < magma) return GenMat.MagmaCrust;
                if (!(gLo >= magma)) return 255;
                if (gLo >= ash) return GenMat.Ash;
                return gHi < ash ? GenMat.Obsidian : (byte)255;
            }
            if (!(gLo >= spec.UnderworldTopY)) return 255;
            byte rb = DetailField.RockA(in c);
            if (yHi + cHi < spec.CavernTopY) return CavernRock(rb);
            if (!(yLo + cLo >= spec.CavernTopY)) return 255;
            float deep = DeepTop(in spec);
            if (yHi + dHi < deep) return DeepStone(rb);
            if (!(yLo + dLo >= deep)) return 255;
            return Stone(rb);
        }

        /// The far band's top colour: Material at depth 0 (no caves at the top).
        /// TopMaterial asks only for columns at or above the sea, so band 3 is the
        /// stone: the deep stone's top is 50 m under the sea and |deepOffset| <=
        /// GEO_DEEP_AMP (8 m); the cavern top and the underworld lie deeper still.
        public static byte Top(in ColumnSample c, float stepAtTop)
        {
            int band = Band(in c, stepAtTop, 0);
            if (band <= 2) return NearMaterial(in c, stepAtTop, band);
            return Stone(DetailField.RockA(in c));
        }
    }
}
