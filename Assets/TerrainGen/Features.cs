// Assets/TerrainGen/Features.cs
//
// SIGNATURE FEATURES (map v4, surface rebuild B5, plan rule 6; owner Q6 (a),
// Q7 (a); Game Design Brief "The pool", Decided 2026-10-01). Each world DRAWS
// its big landmarks from a pool instead of pinning one peak and one volcano:
// natural landforms, exaggerated for drama; not every world gets every one.
//
//   pool today   great peak (Common), great canyon (Common, the WAY DOWN),
//                mesa (Common), volcano (Uncommon), crater lake (Uncommon).
//                Waterfall, sea arches, sinkhole, sky cavern and the colossal
//                arch are later tasks; the rare-wonder tier is empty, so a
//                world draws 0 rare wonders today (cap 1 / 1 / 2).
//   count        Small 3, Medium 5, Large 7 (sizeClass 2, 8 km, is Medium --
//                the same reading as EasyAreas.CountFor).
//   draw         slot 0 is always a great canyon (every world gets a way
//                down; it is the only way-down shape today). The other slots
//                are weighted draws, Common 3 : Uncommon 1, with at most 2
//                canyons, 2 mesas and 1 of each other kind.
//
// Placement, in build order (WorldMap.BuildV3, v4 only):
//   1. volcano    Offsets.For(spec) -- a pure function of the spec, because
//                 Stamps.Column (Burst, per voxel column) needs its site for
//                 the lava lake and the cone's rock. A hashed site inland with
//                 the whole cone on land, off the geyser basin.
//   2. great peak on the starting shapes: the most ALPINE point (region
//                 weight) inland, clear of the volcano -- the peak stands in
//                 its range. Same shape as v3's dominant peak (broad, 1.05 km).
//   3. the rest   still on the starting shapes, BEFORE the easy areas are
//                 seeded: canyons, mesas and crater lakes, each at the
//                 candidate that the most land not yet seeing a feature would
//                 see within 3 km (line of sight over the starting shapes --
//                 the visibility rule's own test, estimated), then a smaller
//                 bonus for ground that ridges hide from afar
//                 (you find it cresting a rise), less a cost on gentle land
//                 (left for the easy areas). Finish sets their heights on the
//                 eroded ground.
//   Every feature: centres >= MIN_SEP (1.2 km) apart and footprints GAP apart,
//   footprint on land and off the geyser basin. B4's easy areas are seeded
//   AFTER, outside every footprint (MarkBad -> EasyAreas.Pick's `bad`), and
//   EasyAreas.Mark skips feature cells: no cell is ever both easy and feature.
//   (Placing features after the easy areas, the first try, fitted 2-3 of 5:
//   six pressed sites and their margins cover most of an ~17 km^2 island.)
//
// Shapes (post-erosion ones applied on the fine map, like the caldera and the
// craters, so they are fresh):
//   mesa         MesaT's flat top (150-250 m radius) on a three-ledge cliff
//                band with one ramp (WorldMap.MesaT), irregular outline,
//                120-180 m above its foot.
//   crater lake  CraterApply's bowl and rim, scaled up (220-320 m radius,
//                up to 60-90 m deep, rim half the depth); Hydrology fills it.
//   canyon       a meandering gorge from an inland head (70 m+) to the
//                nearest sea 1.5-4.4 km away: a floor 40 m wide that only falls toward the
//                mouth (no pit, so no lake; it drains), and terraced walls
//                (Regions.Terrace) up to the ground on both sides.
//                B5b (owner Q29.4 (a)): the floor is pinned near the sea, so
//                through low land the walls stayed low (median 2-35 m deep on
//                5 of 7 canyons); the banks are now a TABLELAND the canyon cuts:
//                raised to floor + BANK_DEPTH (135 m) on the side whose ground
//                is higher, BANK_LOW of that on the other (a full bank on both
//                sides hid the canyon and the peak from the lowland beyond it:
//                seed 42 fell to 77.5% seeing a feature), ramped up from the
//                mouth over BANK_RAMP (the way in), flat for BANK_RIM past the
//                wall top, easing back to the ground over BANK_SLOPE x the rise.
//                Rivers that cross it cut side canyons (Gaps), not dams. It
//                only raises, yields to the easy areas and the shore, and
//                leaves placement and the floor as they were.
//
// RECORD (WorldRecord): the feature list (kind, centre, footprint radius,
// way-down flag, mapVersion) and, on each footprint cell, FLAG_FEATURE and
// site = feature index + 1 (site ids 1-15 are features; later steps' sites
// take ids from 16).
//
// Determinism: +, -, *, /, sqrt, floor and 2-D noise in everything the map
// reads (TerrainSpec.cs contract). VisibleShare (measurement only, never part
// of the map) uses cos/sin.
using System.Collections.Generic;
using Unity.Mathematics;

namespace TerrainGen
{
    public sealed class Feature
    {
        public byte kind;
        public byte tier;
        public int2 centre;            // voxels (x, z); a canyon's path midpoint
        public float radiusM;          // footprint radius from the centre (a canyon: half its length + its half width)
        public bool wayDown;
        public int mapVersion;
        public int cellCount;          // footprint cells flagged in the record
        // shape, voxels
        public float baseH, rise, R, B, L;   // mesa: foot, rise, top radius, cliff band, ramp run; crater: -, rim, bowl radius, depth
        public float2 dir, noiseOff;
        public float footR;                  // voxels: footprint radius (point features)
        public float2[] path; public float[] floor; public float[] halfW;   // canyon centre line (head first), floor, half width
        public float[] bank;                 // canyon: the tableland's rise above the floor per path point (B5b)
        public float[] halfWNat;             // canyon: the half width on the ground as it was (no tableland)
        public float highSide;               // canyon: +1 the full bank is left of the path (head to mouth), -1 right
        public float[] gap; public int2 gapLo; public int gapW, gapH; public float gapCell;   // canyon: side-canyon cuts in the tableland (0-1, coarse cells)
        public short[] segLo, segHi;         // canyon: per coarse cell of that grid, the path segments CanyonAt must try (-1: none in reach)
        public float2 bbMin, bbMax;          // canyon bounds incl. its walls
    }

    public static class Features
    {
        public const byte GreatPeak = 1, GreatCanyon = 2, Mesa = 3, Volcano = 4, CraterLake = 5;
        public const byte Common = 0, Uncommon = 1, Rare = 2;
        public const float MIN_SEP = 12000f;          // voxels: centres 1.2 km apart
        public const float GAP = 1000f;               // voxels between footprints
        public const float NO_SITE_XZ = -1000000f;    // an absent site: far off the map
        public const float PEAK_R = 10500f;           // the dominant peak's radius (BuildV3)
        /// The peak's footprint: 0.8 of its radius, where it stands ~60 m above
        /// its surroundings; the last 20% is a gentle foot others may share.
        public const float PEAK_FOOT = 0.8f * PEAK_R;
        public const float GEYSER_R = 1600f;          // the geyser basin and its margin
        public const float VIEW = 30000f;             // 3 km: the visibility rule (owner Q6 (a))
        // canyon
        public const float CANYON_STEP = 400f, CANYON_MAX_STEPS = 110, CANYON_MIN_LEN = 15000f;
        public const float CANYON_DEPTH = 2000f, CANYON_FLOOR_MIN = 15f, CANYON_GRADE = 0.006f;   // B5b: 6 m/km (was 12): the floor carved deeper
        public const float MESA_STRETCH = 1.2f;
        /// Placement score per unit of gentle-class weight taken (GentleOf);
        /// 0.3 at the centre only left seed 42 two easy areas of four.
        public const float GENTLE_COST = 0.5f;
        /// MarkBad's reach around a canyon's centre line (voxels).
        public const float BAD_CANYON = 800f;
        /// BuildV3 (v4): a starting cell is no place for an easy area where the
        /// peak or the volcano raises it more than this (voxels).
        public const float BAD_RAISE = 300f;
        /// Score for ridges hiding a feature (Hidden, 0-1). Small: the 80%
        /// visibility rule comes first (0.25 left seeds at 76-78%).
        public const float HIDE_BONUS = 0.1f;
        public const float CANYON_WANDER = 1500f;
        /// A canyon's head stands this high on the starting shapes (voxels). 70 m
        /// lets some canyons run mostly through lowland (seed 2026: 35 m deep at
        /// most); 100 m plus a score for upland paths (tried 2026-10-08) left
        /// seed 2026 no canyon and broke B4 on seeds 42 and 31337.
        public const float CANYON_HEAD_MIN = 700f;
        public const float HALF_FLOOR = 200f, WALL = 2f, CANYON_CUT = 2200f, CANYON_HALF_MAX = 1400f;
        /// B5b: the tableland a canyon cuts (voxels): its rise above the floor,
        /// reached BANK_RAMP up from the mouth; the flat rim past the wall top;
        /// the outer slope's run per unit of rise (smoothstep: ~14 deg mean,
        /// ~21 deg at its steepest); +-BANK_NOISE on its top.
        public const float BANK_DEPTH = 1350f, BANK_RAMP = 12000f, BANK_RIM = 1000f, BANK_SLOPE = 4f, BANK_NOISE = 100f, BANK_LOW = 0.35f;
        /// The full bank gives way to the low one over +-SIDE_BLEND of lateral
        /// offset (voxels): no step on the path's line past the head.
        public const float SIDE_BLEND = 700f;
        /// The tableland eases down to the shore over this much of WorldMap.
        /// Coast (~340 m; coast x 5, ~0.5 km, kept seed 2026's coastal canyon
        /// under 100 m everywhere).
        public const float BANK_SHORE = 0.12f;
        public const float BANK_REACH = HALF_FLOOR + BANK_DEPTH / WALL + BANK_RIM + BANK_SLOPE * BANK_DEPTH;
        /// B5b: a channel crossing the tableland (GAP_RIVERS x a river's
        /// catchment on the eroded ground) cuts it to the ground over GAP_R
        /// (voxels): a side canyon, not a dam (a dammed outlet flooded an
        /// easy area on seed 31337).
        public const float GAP_R = 560f, GAP_RIVERS = 2f;
        /// The farthest a canyon's footprint reaches from its centre line: the
        /// widest walls and the tableland's rim beside them (voxels).
        public const float FOOT_MAX = CANYON_HALF_MAX + BANK_RIM;

        public static string Name(byte k)
        {
            switch (k)
            {
                case GreatPeak: return "great peak";
                case GreatCanyon: return "great canyon";
                case Mesa: return "mesa";
                case Volcano: return "volcano";
                case CraterLake: return "crater lake";
                default: return "?";
            }
        }

        public static byte TierOf(byte k) => k == Volcano || k == CraterLake ? Uncommon : Common;
        public static int Cap(byte k) => k == GreatCanyon || k == Mesa ? 2 : 1;
        public static bool IsWayDown(byte k) => k == GreatCanyon;
        private static float Weight(byte k) => TierOf(k) == Common ? 3f : 1f;

        /// Small 3, Medium 5, Large 7 (the brief); sizes as EasyAreas.CountFor.
        public static int CountFor(in TerrainSpec spec) =>
            spec.worldSizeVoxels < 80000 ? 3 : spec.worldSizeVoxels <= 120000 ? 5 : 7;
        /// Rare wonders allowed: 1 / 1 / 2. None exist in the pool today.
        public static int RareMaxFor(in TerrainSpec spec) => spec.worldSizeVoxels <= 120000 ? 1 : 2;

        private static readonly byte[] Pool = { GreatPeak, GreatCanyon, Mesa, Volcano, CraterLake };

        /// The kinds this world draws, in slot order (slot 0 the way down).
        public static byte[] Draw(in TerrainSpec spec)
        {
            int count = CountFor(in spec);
            var kinds = new List<byte> { GreatCanyon };
            var n = new int[8]; n[GreatCanyon] = 1;
            for (int j = 1; j < count; j++)
            {
                float total = 0f;
                foreach (byte k in Pool) if (n[k] < Cap(k)) total += Weight(k);
                if (total <= 0f) break;
                float r = Noise.Hash01(j, 31, 7, spec.seed + 901u) * total;
                byte pick = 0;
                foreach (byte k in Pool)
                {
                    if (n[k] >= Cap(k)) continue;
                    pick = k; r -= Weight(k);
                    if (r < 0f) break;
                }
                kinds.Add(pick); n[pick]++;
            }
            return kinds.ToArray();
        }

        private static bool Has(byte[] kinds, byte k) { foreach (byte x in kinds) if (x == k) return true; return false; }

        // 12 directions, 30 degrees apart (literal: no trig in the map).
        private static readonly float2[] Ring12 =
        {
            new float2(1f, 0f), new float2(0.8660254f, 0.5f), new float2(0.5f, 0.8660254f), new float2(0f, 1f),
            new float2(-0.5f, 0.8660254f), new float2(-0.8660254f, 0.5f), new float2(-1f, 0f), new float2(-0.8660254f, -0.5f),
            new float2(-0.5f, -0.8660254f), new float2(0f, -1f), new float2(0.5f, -0.8660254f), new float2(0.8660254f, -0.5f),
        };

        // ------------------------------------------------------------------
        // 1. the volcano (pure; Offsets.For(spec) stores it)
        // ------------------------------------------------------------------
        /// The drawn volcano's site, or (NO_SITE_XZ, NO_SITE_XZ) when this world
        /// did not draw one. `o` needs only its coast offsets.
        public static float2 VolcanoSite(in TerrainSpec spec, in Offsets o)
        {
            if (!Has(Draw(in spec), Volcano)) return new float2(NO_SITE_XZ, NO_SITE_XZ);
            float c = spec.worldSizeVoxels * 0.5f, R = spec.islandRadius;
            float2 geyser = Stamps.GeyserSite(in spec, in o);
            for (int i = 0; i < 96; i++)
            {
                float2 p = new float2(c, c) + new float2(Noise.Hash01(i, 41, 3, spec.seed + 911u) - 0.5f,
                                                         Noise.Hash01(i, 42, 3, spec.seed + 912u) - 0.5f) * (1.4f * R);
                if (WorldMap.Coast(in spec, in o, p) < 0.35f) continue;
                if (math.distance(p, geyser) < Stamps.VOLCANO_BASE_R + GEYSER_R + GAP) continue;
                bool ok = true;
                for (int a = 0; a < 12 && ok; a++)
                    if (WorldMap.Coast(in spec, in o, p + Ring12[a] * Stamps.VOLCANO_BASE_R) < 0.05f) ok = false;
                if (ok) return p;
            }
            return Stamps.VolcanoSiteV3(in spec, in o);
        }

        // ------------------------------------------------------------------
        // 2. the great peak (starting shapes)
        // ------------------------------------------------------------------
        /// Diagnostics of the last placement (like EasyAreas.LastPick).
        public static string LastPlace = "";

        /// The list so far: the volcano (if drawn) and the great peak (if drawn
        /// and it fits). Returns the peak's site or NO_SITE.
        public static float2 PlaceEarly(in TerrainSpec spec, in Offsets o, Regions.Layout L, byte[] kinds, List<Feature> feats)
        {
            LastPlace = "";
            if (Has(kinds, Volcano))
            {
                float2 vs = Stamps.VolcanoSite(in spec, in o);
                feats.Add(new Feature { kind = Volcano, tier = Uncommon, centre = new int2((int)vs.x, (int)vs.y), footR = Stamps.VOLCANO_BASE_R,
                                        radiusM = Stamps.VOLCANO_BASE_R / 10f, mapVersion = spec.mapVersion });
            }
            float2 peak = new float2(NO_SITE_XZ, NO_SITE_XZ);
            if (!Has(kinds, GreatPeak)) return peak;
            float2 geyser = Stamps.GeyserSite(in spec, in o);
            var w = new float[Regions.KINDS];
            float best = float.MinValue; int tried = 0;
            const float STEP = 2000f;
            int g = (int)(spec.worldSizeVoxels / STEP);
            for (int z = 0; z < g; z++)
            for (int x = 0; x < g; x++)
            {
                float2 p = new float2((x + 0.5f) * STEP, (z + 0.5f) * STEP);
                float coast = WorldMap.Coast(in spec, in o, p);
                if (coast < 0.45f || math.distance(p, geyser) < PEAK_R + GEYSER_R + GAP) continue;
                bool clear = true;
                foreach (var f in feats) if (math.distance(p, (float2)f.centre) < math.max(MIN_SEP, PEAK_FOOT + f.footR + GAP)) clear = false;
                if (!clear) continue;
                tried++;
                Regions.Weights(L, in o, p, w);
                float s = w[Regions.Alpine] + 0.3f * math.min(coast, 1f) + 0.1f * Noise.Hash01(x, 51, z, spec.seed + 921u);
                if (s > best) { best = s; peak = p; }
            }
            if (best > float.MinValue)
                feats.Add(new Feature { kind = GreatPeak, tier = Common, centre = new int2((int)peak.x, (int)peak.y), footR = PEAK_FOOT,
                                        radiusM = PEAK_FOOT / 10f, mapVersion = spec.mapVersion });
            LastPlace += $"peak: {tried} candidates, {(best > float.MinValue ? $"at ({peak.x:F0}, {peak.y:F0})" : "NONE")}; ";
            return peak;
        }

        // ------------------------------------------------------------------
        // 3. canyons, mesas, crater lakes (placed on the starting shapes,
        //    BEFORE the easy areas, which then avoid them; finished on the
        //    eroded grid)
        // ------------------------------------------------------------------
        private sealed class Ctx
        {
            public TerrainSpec spec; public Offsets o; public int m; public float cm; public float[] hc;
            public float[] gentle; public float2 geyser;
            public float H(float2 p)
            {
                float gx = p.x / cm - 0.5f, gz = p.y / cm - 0.5f;
                int x0 = math.clamp((int)math.floor(gx), 0, m - 2), z0 = math.clamp((int)math.floor(gz), 0, m - 2);
                float fx = math.saturate(gx - x0), fz = math.saturate(gz - z0);
                float a = math.lerp(hc[z0 * m + x0], hc[z0 * m + x0 + 1], fx);
                float b = math.lerp(hc[(z0 + 1) * m + x0], hc[(z0 + 1) * m + x0 + 1], fx);
                return math.lerp(a, b, fz);
            }
            public float Coast(float2 p) => WorldMap.Coast(in spec, in o, p);
            /// Line of sight over the starting shapes (or the sea surface), every 2 cells.
            public bool Sight(float2 e, float he, float2 t, float ht)
            {
                int steps = (int)(math.distance(e, t) / (2f * cm));
                for (int s = 1; s < steps; s++)
                {
                    float u = s / (float)steps;
                    if (math.max(H(math.lerp(e, t, u)), 0f) > math.lerp(he, ht, u)) return false;
                }
                return true;
            }
            /// Gentle-class weight (B4's, wetland left out) at the nearest cell.
            public float Gentle(float2 p)
            {
                int x = math.clamp((int)(p.x / cm), 0, m - 1), z = math.clamp((int)(p.y / cm), 0, m - 1);
                return gentle != null ? gentle[z * m + x] : 0f;
            }
            /// Rejections by reason (diagnostics): low, sea, -, geyser, path, spacing.
            public int[] rej = new int[6];
        }

        /// Places every slot not placed yet (all but the volcano and the peak),
        /// hardest kind first, on the starting shapes hc (m x m, cm voxels); a kind
        /// that fits nowhere is replaced by the first of mesa, crater lake,
        /// canyon still under its cap that does. `gentle`: the gentle-class
        /// weight per cell -- features leave the gentlest land to the easy areas.
        public static void PlaceLate(in TerrainSpec spec, in Offsets o, int m, float cm, float[] hc, float[] gentle, bool[] bad,
                                     byte[] kinds, List<Feature> feats)
        {
            var c = new Ctx { spec = spec, o = o, m = m, cm = cm, hc = hc, gentle = gentle, geyser = Stamps.GeyserSite(in spec, in o) };
            // visibility estimate: eyes on land every 400 m, 2 m above the starting
            // shapes (the peak and the volcano are already in them)
            var land = new List<float2>();
            for (float z = 2000f; z < spec.worldSizeVoxels; z += 4000f)
            for (float x = 2000f; x < spec.worldSizeVoxels; x += 4000f)
                if (c.Coast(new float2(x, z)) > Regions.LAND_COAST) land.Add(new float2(x, z));
            var covered = new bool[land.Count];
            foreach (var f in feats) Cover(c, f, land, covered);
            var n = new int[8];
            foreach (var f in feats) n[f.kind]++;
            // hardest first: a canyon needs a clear line to the sea, a crater lake
            // high ground, a mesa only room (slot order left seed 7 a canyon short)
            var order = new List<int>();
            foreach (byte k in new[] { GreatCanyon, CraterLake, Mesa })
                for (int j = 0; j < kinds.Length; j++) if (kinds[j] == k) order.Add(j);
            // Depth-first over each slot's best SEARCH_TOP candidates (its own kind,
            // then the substitutes), the greedy choice first: a seed greedy fills
            // takes exactly greedy's result. Bounded by SEARCH_BUDGET candidate
            // scans; the fullest placement found stands if none is complete.
            // A complete placement must leave B4 its easy areas: EasyAreas.Pick on
            // the same inputs (with these footprints marked) must seat the count.
            var specCopy = spec;
            int want = EasyAreas.CountFor(in spec);
            int Seats(List<Feature> fs)
            {
                var b = (bool[])bad.Clone();
                MarkBad(fs, m, cm, b);
                return EasyAreas.Pick(in specCopy, m, cm, hc, gentle, b).Length;
            }
            var st = new Search { budget = SEARCH_BUDGET, checks = SEARCH_CHECKS, best = new List<Feature>(feats), seats = Seats, easyWant = want };
            int before = feats.Count;
            bool done = Place(c, st, kinds, order, 0, n, land, covered, feats);
            if (!done) { feats.Clear(); feats.AddRange(st.best); }
            LastPlace += $"late: {feats.Count - before} of {order.Count} placed, {SEARCH_BUDGET - st.budget} scans, {SEARCH_CHECKS - st.checks} easy checks " +
                         $"(seats {st.bestSeats} of {want}){(done ? "" : ", INCOMPLETE")}; ";
        }

        public const int SEARCH_TOP = 3, SEARCH_BUDGET = 40, SEARCH_CHECKS = 8;
        private sealed class Search
        {
            public int budget, checks, easyWant, bestSeats = -1;
            public List<Feature> best;
            public System.Func<List<Feature>, int> seats;
        }

        private static bool Place(Ctx c, Search st, byte[] kinds, List<int> order, int idx, int[] n, List<float2> land, bool[] covered, List<Feature> feats)
        {
            if (idx == order.Count)
            {
                // complete: keep the one that leaves the most easy-area seats
                if (st.checks-- <= 0) return false;
                int seated = st.seats(feats);
                if (feats.Count > st.best.Count || seated > st.bestSeats) { st.best = new List<Feature>(feats); st.bestSeats = seated; }
                return seated >= st.easyWant;
            }
            if (feats.Count > st.best.Count && st.bestSeats < 0) st.best = new List<Feature>(feats);
            int slot = order[idx] + 1;
            byte want = kinds[order[idx]];
            var tryKinds = new List<byte> { want };
            foreach (byte alt in new[] { Mesa, CraterLake, GreatCanyon }) if (alt != want) tryKinds.Add(alt);
            foreach (byte k in tryKinds)
            {
                if (n[k] >= Cap(k)) continue;
                if (st.budget-- <= 0) return false;
                foreach (var f in Top(c, k, slot, land, covered, feats))
                {
                    var cov = (bool[])covered.Clone();
                    Cover(c, f, land, cov);
                    feats.Add(f); n[k]++;
                    if (Place(c, st, kinds, order, idx + 1, n, land, cov, feats))
                    {
                        if (k != want) LastPlace += $"slot {slot}: {Name(want)} -> {Name(k)}; ";
                        return true;
                    }
                    feats.RemoveAt(feats.Count - 1); n[k]--;
                    if (st.budget <= 0 || st.checks <= 0) return false;
                }
            }
            return false;
        }

        /// Marks the eyes that see f within 3 km on the starting shapes (an
        /// estimate of VisibleShare before the map exists).
        private static void Cover(Ctx c, Feature f, List<float2> eyes, bool[] seen)
        {
            var tops = Tops(c, f);
            for (int j = 0; j < eyes.Count; j++)
            {
                if (seen[j]) continue;
                float he = c.H(eyes[j]) + 20f;
                foreach (var (tp, th) in tops)
                    if (math.distance(eyes[j], tp) <= VIEW && c.Sight(eyes[j], he, tp, th)) { seen[j] = true; break; }
            }
        }

        /// Placement's targets and their heights on the starting shapes: the
        /// summit area, the caldera rim, the mesa top (foot + rise), the crater
        /// rim, the canyon walls half way down to a near-sea floor.
        private static List<(float2, float)> Tops(Ctx c, Feature f)
        {
            var t = new List<(float2, float)>();
            float2 o = (float2)f.centre;
            switch (f.kind)
            {
                case GreatPeak: t.Add((o, c.H(o))); for (int a = 0; a < 12; a += 2) { float2 q = o + Ring12[a] * (0.3f * PEAK_R); t.Add((q, c.H(q))); } break;
                case Volcano: for (int a = 0; a < 12; a += 2) { float2 q = o + Ring12[a] * Stamps.CALDERA_R; t.Add((q, c.H(q))); } break;
                case Mesa: { float top = c.H(o) + f.rise; t.Add((o, top)); for (int a = 0; a < 12; a += 2) t.Add((o + Ring12[a] * (0.8f * f.R), top)); break; }
                case CraterLake: for (int a = 0; a < 12; a += 2) { float2 q = o + Ring12[a] * (1.1f * f.R); t.Add((q, c.H(q) + 0.5f * f.B)); } break;
                case GreatCanyon:
                    for (int k = 0; k + 1 < f.path.Length; k += 5)
                    {
                        float2 a = f.path[k], d = math.normalizesafe(f.path[k + 1] - a), side = new float2(-d.y, d.x) * (HALF_FLOOR + 300f);
                        float mid = 0.5f * c.H(a);
                        t.Add((a + side, mid)); t.Add((a - side, mid));
                    }
                    break;
            }
            return t;
        }

        /// The kind's SEARCH_TOP best-scoring candidates at this point, best first.
        private static List<Feature> Top(Ctx c, byte kind, int slot, List<float2> land, bool[] covered, List<Feature> feats)
        {
            var spec = c.spec;
            float step = 1600f;
            int g = (int)(spec.worldSizeVoxels / step);
            var top = new List<(float s, Feature f)>(); int fits = 0;
            for (int z = 0; z < g; z++)
            for (int x = 0; x < g; x++)
            {
                float2 p = new float2((x + 0.5f) * step, (z + 0.5f) * step);
                var f = kind == GreatCanyon ? Canyon(c, p, x, z, slot) : kind == Mesa ? MesaAt(c, p, x, z, slot) : CraterAt(c, p, x, z, slot);
                if (f == null) continue;
                if (!Clear(c, f, feats)) { c.rej[5]++; continue; }
                fits++;
                // visibility gain (share of land newly seeing a feature), then ridges
                int gain = 0;
                var probe = new bool[covered.Length];
                System.Array.Copy(covered, probe, covered.Length);
                Cover(c, f, land, probe);
                for (int j = 0; j < probe.Length; j++) if (probe[j] && !covered[j]) gain++;
                float hide = Hidden(c, (float2)f.centre, kind == GreatCanyon ? c.H((float2)f.centre) : f.baseH + f.rise);
                float s = gain / (float)math.max(1, land.Count) + HIDE_BONUS * hide - GENTLE_COST * GentleOf(c, f)
                        + 0.05f * Noise.Hash01(x, 60 + slot, z, spec.seed + 931u);
                int at = top.Count;
                while (at > 0 && top[at - 1].s < s) at--;
                if (at < SEARCH_TOP) { top.Insert(at, (s, f)); if (top.Count > SEARCH_TOP) top.RemoveAt(SEARCH_TOP); }
            }
            LastPlace += $"slot {slot} {Name(kind)}: {fits} fit (rejected: low {c.rej[0]} sea {c.rej[1]} geyser {c.rej[3]} path {c.rej[4]} spacing {c.rej[5]}); ";
            System.Array.Clear(c.rej, 0, c.rej.Length);
            var r = new List<Feature>();
            foreach (var t in top) r.Add(t.f);
            return r;
        }

        /// Gentle-class weight a feature takes: at a point feature's centre, or
        /// the mean along a canyon (which crosses the low land to the sea).
        private static float GentleOf(Ctx c, Feature f)
        {
            if (f.path == null) return c.Gentle((float2)f.centre);
            float sum = 0f;
            foreach (var a in f.path) sum += c.Gentle(a);
            return sum / f.path.Length;
        }

        /// Share of 6 directions in which ground 0.6-1.5 km out stands within
        /// 40 m of `top` or above it: a ridge that hides the feature from the
        /// country beyond until you crest it.
        private static float Hidden(Ctx c, float2 p, float top)
        {
            int hid = 0;
            for (int a = 0; a < 12; a += 2)   // 6 of the 12 directions
            {
                float mx = float.MinValue;
                for (float r = 6000f; r <= 15000f; r += 1500f) mx = math.max(mx, c.H(p + Ring12[a] * r));
                if (mx >= top - 400f) hid++;
            }
            return hid / 6f;
        }

        /// The footprint is on land (off the beach) and off the geyser basin;
        /// checked at the centre and 12 points on the ring r.
        private static bool FootprintOk(Ctx c, float2 p, float r)
        {
            if (math.distance(p, c.geyser) < r + GEYSER_R + GAP) { c.rej[3]++; return false; }
            if (c.Coast(p) < 0.06f) { c.rej[1]++; return false; }
            for (int a = 0; a < 12; a++)
                if (c.Coast(p + Ring12[a] * r) < 0.06f) { c.rej[1]++; return false; }
            return true;
        }

        /// Spacing against everything placed: centres MIN_SEP apart, footprints
        /// GAP apart, and a mesa's ramp tip outside the others' footprints.
        private static bool Clear(Ctx c, Feature f, List<Feature> feats)
        {
            foreach (var e in feats)
            {
                foreach (var (me, other) in new[] { (f, e), (e, f) })
                {
                    if (me.kind != Mesa) continue;
                    float2 tip = (float2)me.centre + me.dir * MesaReach(me);
                    if (other.path == null) { if (math.distance(tip, (float2)other.centre) < other.footR + GAP) return false; }
                    else foreach (var a in other.path) if (math.distance(tip, a) < CANYON_HALF_MAX + GAP) return false;
                }
                if (math.distance((float2)f.centre, (float2)e.centre) < MIN_SEP) return false;
                if (f.path == null && e.path == null)
                {
                    if (math.distance((float2)f.centre, (float2)e.centre) < f.footR + e.footR + GAP) return false;
                }
                else if (f.path != null && e.path != null)
                {
                    foreach (var a in f.path) foreach (var b in e.path)
                        if (math.distance(a, b) < 2f * CANYON_HALF_MAX + GAP) return false;
                }
                else
                {
                    var cy = f.path != null ? f : e; var pt = f.path != null ? e : f;
                    foreach (var a in cy.path)
                        if (math.distance(a, (float2)pt.centre) < pt.footR + CANYON_HALF_MAX + GAP) return false;
                }
            }
            return true;
        }

        private static float2 HashUnit(int x, int z, uint seed)
        {
            float2 v = new float2(Noise.Hash01(x, 11, z, seed) - 0.5f, Noise.Hash01(x, 12, z, seed) - 0.5f);
            float l = math.sqrt(v.x * v.x + v.y * v.y);
            return l > 1e-3f ? v / l : new float2(1f, 0f);
        }

        private static Feature MesaAt(Ctx c, float2 p, int x, int z, int slot)
        {
            uint s = c.spec.seed + 941u + (uint)slot * 7u;
            float g = c.H(p);
            if (c.Coast(p) < 0.2f || g < 150f) { c.rej[0]++; return null; }
            float R = 1500f + 1000f * Noise.Hash01(x, 1, z, s), rise = 1200f + 600f * Noise.Hash01(x, 2, z, s);
            float B = 900f, L = rise / 0.5f;          // the ramp: ~27 deg, the one way up
            // the irregular outline (stretch MESA_STRETCH, lobes HILL_LOBE) scales the whole
            // profile; the spacing radius is the cliff band's -- the ramp is one narrow
            // tongue beyond it, inside the GAP or reaching into it (InFootprint is exact)
            float foot = MESA_STRETCH * (1f + WorldMap.HILL_LOBE) * (R + B);
            if (!FootprintOk(c, p, MESA_STRETCH * (R + B))) return null;
            return new Feature { kind = Mesa, tier = Common, centre = new int2((int)p.x, (int)p.y), footR = foot, radiusM = foot / 10f,
                                 mapVersion = c.spec.mapVersion, baseH = g, rise = rise, R = R, B = B, L = L,
                                 dir = HashUnit(x, z, s + 3u), noiseOff = new float2(Noise.Hash01(x, 4, z, s), Noise.Hash01(x, 5, z, s)) * 100f };
        }

        private static Feature CraterAt(Ctx c, float2 p, int x, int z, int slot)
        {
            uint s = c.spec.seed + 951u + (uint)slot * 7u;
            float g = c.H(p);
            if (c.Coast(p) < 0.2f || g < 500f) { c.rej[0]++; return null; }
            // depth; Finish keeps the bowl floor >= 15 m above the sea (a lake of its own)
            float R = 2200f + 1000f * Noise.Hash01(x, 1, z, s), D = 600f + 300f * Noise.Hash01(x, 2, z, s);
            float foot = 1.8f * R;
            if (!FootprintOk(c, p, foot)) return null;
            return new Feature { kind = CraterLake, tier = Uncommon, centre = new int2((int)p.x, (int)p.y), footR = foot, radiusM = foot / 10f,
                                 mapVersion = c.spec.mapVersion, baseH = g, rise = 0.5f * D, R = R, B = D };
        }

        /// A canyon headed at p: a meandering line toward the nearest sea at
        /// least CANYON_MIN_LEN away (12 headings); null if the head is low, no
        /// heading qualifies, or the line crosses the geyser basin or the shore
        /// before its mouth. Its floor is set by Finish.
        private static Feature Canyon(Ctx c, float2 p, int x, int z, int slot)
        {
            uint s = c.spec.seed + 961u + (uint)slot * 7u;
            if (c.Coast(p) < 0.35f || c.H(p) < CANYON_HEAD_MIN) { c.rej[0]++; return null; }
            float2 off = new float2(Noise.Hash01(x, 1, z, s), Noise.Hash01(x, 2, z, s)) * 100f;
            // toward the nearest sea of 12 headings that is at least CANYON_MIN_LEN away
            float bestD = float.MaxValue; int bestA = -1;
            for (int a = 0; a < 12; a++)
            {
                for (float t = CANYON_STEP; t <= CANYON_MAX_STEPS * CANYON_STEP; t += CANYON_STEP)
                    if (c.Coast(p + Ring12[a] * t) <= 0f)
                    {
                        if (t >= CANYON_MIN_LEN && t < bestD) { bestD = t; bestA = a; }
                        break;
                    }
            }
            if (bestA < 0) { c.rej[4]++; return null; }
            // a meandering line: lateral wander up to CANYON_WANDER, none at the head
            float2 down = Ring12[bestA], lateral = new float2(-down.y, down.x);
            var pts = new List<float2> { p };
            bool sea = false;
            for (int k = 1; k <= CANYON_MAX_STEPS + 10; k++)
            {
                float t = k * CANYON_STEP;
                float2 q = p + down * t + lateral * (CANYON_WANDER * noise.snoise(new float2(t / 9000f, 0f) + off) * (t / (t + 3000f)));
                pts.Add(q);
                float cq = c.Coast(q);
                if (cq <= 0f) { sea = true; break; }
            }
            // the line must stay on land until the sea it was aimed at
            for (int k = 1; k < pts.Count - 1; k++)
                if (math.distance(pts[k], p) < bestD - 2f * CANYON_STEP && c.Coast(pts[k]) <= 0.02f) { c.rej[4]++; return null; }
            if (!sea || (pts.Count - 1) * CANYON_STEP < CANYON_MIN_LEN) { c.rej[4]++; return null; }
            // clear of the geyser basin over its walls
            foreach (var a in pts)
                if (math.distance(a, c.geyser) < GEYSER_R + CANYON_HALF_MAX + GAP) { c.rej[3]++; return null; }
            int nP = pts.Count;
            float2 lo = pts[0], hi = pts[0];
            foreach (var a in pts) { lo = math.min(lo, a); hi = math.max(hi, a); }
            float2 mid = pts[nP / 2];
            float len = (nP - 1) * CANYON_STEP;
            return new Feature { kind = GreatCanyon, tier = Common, wayDown = true, centre = new int2((int)mid.x, (int)mid.y),
                                 footR = 0.5f * len + CANYON_HALF_MAX, radiusM = (0.5f * len + CANYON_HALF_MAX) / 10f, mapVersion = c.spec.mapVersion,
                                 path = pts.ToArray(),
                                 bbMin = lo - BANK_REACH, bbMax = hi + BANK_REACH };
        }

        /// The starting-shape cells (m x m) at the CORE of each late feature --
        /// the mesa's cliff band, the crater's bowl, the canyon's floor and
        /// lower walls -- where the easy areas must not be seeded (EasyAreas.
        /// Pick's `bad`; the peak and the volcano mark theirs in BuildV3). The
        /// outer footprint may share an easy area's fade; EasyAreas.Mark keeps
        /// the flagged cells off it. (Marking whole footprints left seeds 42
        /// and 7 one easy area of four.)
        public static void MarkBad(List<Feature> feats, int m, float cm, bool[] bad)
        {
            foreach (var f in feats)
            {
                if (f.kind == GreatPeak || f.kind == Volcano) continue;
                float reach = f.path != null ? BAD_CANYON : f.kind == Mesa ? MESA_STRETCH * (f.R + f.B) : 1.2f * f.R;
                float2 lo = (float2)f.centre - reach, hi = (float2)f.centre + reach;
                if (f.path != null)
                {
                    lo = f.path[0]; hi = f.path[0];
                    foreach (var a in f.path) { lo = math.min(lo, a); hi = math.max(hi, a); }
                    lo -= reach; hi += reach;
                }
                int x0 = math.max(0, (int)(lo.x / cm)), z0 = math.max(0, (int)(lo.y / cm));
                int x1 = math.min(m - 1, (int)(hi.x / cm)), z1 = math.min(m - 1, (int)(hi.y / cm));
                for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    float2 p = new float2((x + 0.5f) * cm, (z + 0.5f) * cm);
                    bool inside;
                    if (f.path == null) inside = math.distance(p, (float2)f.centre) < reach;
                    else
                    {
                        inside = false;
                        foreach (var a in f.path) if (math.distance(p, a) < reach) { inside = true; break; }
                    }
                    if (inside) bad[z * m + x] = true;
                }
            }
        }

        /// After erosion, on the eroded grid hc: each mesa's foot, each crater's
        /// depth (its floor >= 15 m above the sea), each canyon's floor -- at
        /// most the rim less CANYON_DEPTH, at least a CANYON_GRADE climb above
        /// the mouth's 1.5 m, falling only toward the mouth (no pit, no lake) --
        /// and its half width.
        public static void Finish(in TerrainSpec spec, int m, float cm, float[] hc, List<Feature> feats)
        {
            var c = new Ctx { spec = spec, m = m, cm = cm, hc = hc };
            foreach (var f in feats)
            {
                if (f.kind == Mesa) f.baseH = c.H((float2)f.centre);
                else if (f.kind == CraterLake)
                {
                    f.baseH = c.H((float2)f.centre);
                    f.B = math.clamp(f.B, 100f, f.baseH - 150f);
                    f.rise = 0.5f * f.B;
                }
                else if (f.kind == GreatCanyon)
                {
                    int nP = f.path.Length;
                    f.floor = new float[nP]; f.halfW = new float[nP]; f.bank = new float[nP]; f.halfWNat = new float[nP];
                    // the full bank goes on the side whose ground is higher (300 m
                    // out): the lowland beyond the other side keeps its view of
                    // the canyon and the land behind it. (Choosing the side with
                    // fewer easy areas instead cost seed 42 2 points of the 80%.)
                    float lean = 0f;
                    for (int k = 0; k + 1 < nP; k++)
                    {
                        float2 d = math.normalizesafe(f.path[k + 1] - f.path[k]), left = new float2(-d.y, d.x) * 3000f;
                        lean += c.H(f.path[k] + left) - c.H(f.path[k] - left);
                    }
                    f.highSide = lean >= 0f ? 1f : -1f;
                    float run = float.MaxValue;
                    for (int k = 0; k < nP; k++)
                    {
                        float rim = c.H(f.path[k]);
                        run = math.min(run, rim - CANYON_DEPTH);
                        float least = CANYON_FLOOR_MIN + CANYON_GRADE * (nP - 1 - k) * CANYON_STEP;
                        f.floor[k] = math.max(run, least);   // both terms only fall toward the mouth
                        f.bank[k] = BANK_DEPTH * math.saturate((nP - 1 - k) * CANYON_STEP / BANK_RAMP);
                        f.halfWNat[k] = math.clamp(HALF_FLOOR + (rim - f.floor[k]) / WALL, HALF_FLOOR, CANYON_HALF_MAX);
                        rim = math.max(rim, f.floor[k] + f.bank[k]);
                        f.halfW[k] = math.clamp(HALF_FLOOR + (rim - f.floor[k]) / WALL, HALF_FLOOR, CANYON_HALF_MAX);
                    }
                }
            }
            Gaps(in spec, m, cm, hc, feats);
        }

        /// B5b: the side canyons. Drainage on the eroded ground (the same eps
        /// flood as Hydrology), and on each canyon's tableland a cut of radius
        /// GAP_R round every cell with GAP_RIVERS x a river's catchment
        /// (Hydrology.RIVER_MIN_CELLS of fine cells), outside the canyon's own cut.
        private static void Gaps(in TerrainSpec spec, int m, float cm, float[] hc, List<Feature> feats)
        {
            bool any = false;
            foreach (var f in feats) if (f.kind == GreatCanyon) any = true;
            if (!any) return;
            int count = m * m;
            var lvl = new float[count]; var par = new int[count]; var ord = new int[count];
            Hydrology.Flood(m, hc, Hydrology.FLOOD_EPS, lvl, par, ord);
            var flow = new float[count];
            for (int i = 0; i < count; i++) flow[i] = hc[i] > 0f ? 1f : 0f;
            for (int k = count - 1; k >= 0; k--)
            {
                int c = ord[k], p = par[c];
                if (p >= 0 && hc[c] > 0f) flow[p] += flow[c];
            }
            float fine = spec.mapCellVoxels / cm;
            float river = GAP_RIVERS * Hydrology.RIVER_MIN_CELLS * fine * fine;
            int r = (int)math.ceil(GAP_R / cm);
            foreach (var f in feats)
            {
                if (f.kind != GreatCanyon) continue;
                int x0 = math.max(0, (int)(f.bbMin.x / cm)), z0 = math.max(0, (int)(f.bbMin.y / cm));
                int x1 = math.min(m - 1, (int)(f.bbMax.x / cm)), z1 = math.min(m - 1, (int)(f.bbMax.y / cm));
                f.gapLo = new int2(x0, z0); f.gapW = x1 - x0 + 1; f.gapH = z1 - z0 + 1; f.gapCell = cm;
                f.gap = new float[f.gapW * f.gapH];
                Segs(f);
                for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    int i = z * m + x;
                    if (hc[i] <= 0f || flow[i] < river) continue;
                    float2 p = new float2((x + 0.5f) * cm, (z + 0.5f) * cm);
                    if (!CanyonAt(f, p, out float s, out _, out float dt, out float lat) || dt <= 0f) continue;
                    dt *= SideOf(f, lat);
                    if (s < HALF_FLOOR + dt / WALL) continue;   // inside the cut: drains along the floor
                    for (int dz = -r; dz <= r; dz++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        int xx = x + dx - x0, zz = z + dz - z0;
                        if (xx < 0 || zz < 0 || xx >= f.gapW || zz >= f.gapH) continue;
                        float d = math.sqrt(dx * dx + dz * dz) * cm;
                        if (d >= GAP_R) continue;
                        float g = 1f - Noise.SmoothStep(0.5f * GAP_R, GAP_R, d);
                        int j = zz * f.gapW + xx;
                        if (g > f.gap[j]) f.gap[j] = g;
                    }
                }
            }
        }

        /// CanyonAt's search, cut down per coarse cell and exact: the nearest
        /// segment to any point in a cell lies within the cell centre's nearest
        /// distance + 2 half diagonals of it (a superset range, in path order,
        /// so ties resolve as in the full loop); none when even the nearest is
        /// out of BANK_REACH. (The full loop over the enlarged boxes cost the
        /// map build ~1-2 s.)
        private static void Segs(Feature f)
        {
            int nS = f.path.Length - 1, cells = f.gapW * f.gapH;
            f.segLo = new short[cells]; f.segHi = new short[cells];
            float hd = f.gapCell * 0.7072f + 1f;
            var d = new float[nS];
            for (int z = 0; z < f.gapH; z++)
            for (int x = 0; x < f.gapW; x++)
            {
                float2 c = new float2((f.gapLo.x + x + 0.5f) * f.gapCell, (f.gapLo.y + z + 0.5f) * f.gapCell);
                float dmin = float.MaxValue;
                for (int k = 0; k < nS; k++)
                {
                    float2 a = f.path[k], ab = f.path[k + 1] - a;
                    float t = math.saturate(math.dot(c - a, ab) / math.dot(ab, ab));
                    d[k] = math.distance(c, a + ab * t);
                    dmin = math.min(dmin, d[k]);
                }
                int j = z * f.gapW + x;
                if (dmin - hd >= BANK_REACH) { f.segLo[j] = -1; f.segHi[j] = -1; continue; }
                int lo = 0, hi = nS - 1;
                while (d[lo] > dmin + 2f * hd) lo++;
                while (d[hi] > dmin + 2f * hd) hi--;
                f.segLo[j] = (short)lo; f.segHi[j] = (short)hi;
            }
        }

        /// The side-canyon cut at p (0-1), bilinear on the coarse cells.
        private static float GapAt(Feature f, float2 p)
        {
            float gx = p.x / f.gapCell - 0.5f - f.gapLo.x, gz = p.y / f.gapCell - 0.5f - f.gapLo.y;
            int x0 = (int)math.floor(gx), z0 = (int)math.floor(gz);
            if (x0 < 0 || z0 < 0 || x0 + 1 >= f.gapW || z0 + 1 >= f.gapH) return 0f;
            float fx = gx - x0, fz = gz - z0;
            int i = z0 * f.gapW + x0;
            float a = math.lerp(f.gap[i], f.gap[i + 1], fx), b = math.lerp(f.gap[i + f.gapW], f.gap[i + f.gapW + 1], fx);
            return math.lerp(a, b, fz);
        }

        // ------------------------------------------------------------------
        // shapes on the fine map
        // ------------------------------------------------------------------
        /// The fine map's height at p after the post-erosion features. `bankW`
        /// (0-1) scales a canyon's tableland: less on the shore and in an easy area.
        public static float Apply(List<Feature> feats, float2 p, float h, float bankW = 1f)
        {
            foreach (var f in feats)
            {
                if (f.kind == Mesa)
                {
                    float2 q = p - (float2)f.centre;
                    float reach = MesaReach(f);
                    if (q.x * q.x + q.y * q.y >= reach * reach) continue;
                    float t = MesaWeight(f, q);
                    if (t > 0f) h = math.max(h, math.lerp(h, f.baseH + f.rise, t));
                }
                else if (f.kind == CraterLake)
                {
                    float2 q = p - (float2)f.centre;
                    float d = math.sqrt(q.x * q.x + q.y * q.y), x = d / f.R;
                    if (x < 1f) h = h - f.B * (1f - x * x) + f.rise * x * x * x * x;
                    else if (x < 1.8f) { float t = 1f - (x - 1f) / 0.8f; h += f.rise * t * t; }
                }
                else if (f.kind == GreatCanyon)
                {
                    if (p.x < f.bbMin.x || p.y < f.bbMin.y || p.x > f.bbMax.x || p.y > f.bbMax.y) continue;
                    if (!CanyonAt(f, p, out float s, out float fl, out float dt, out float lat)) continue;
                    dt *= SideOf(f, lat);
                    float bw = BankShare(f, p, bankW);
                    if (dt > 0f && bw > 0f)
                    {
                        float s0 = HALF_FLOOR + dt / WALL + BANK_RIM;
                        float w = bw * (1f - Noise.SmoothStep(s0, s0 + BANK_SLOPE * dt, s));
                        float top = fl + dt + BANK_NOISE * noise.snoise(p / 6000f + (f.path[0] / 1000f));
                        if (w > 0f && top > h) h = math.lerp(h, top, w);
                    }
                    if (s < CANYON_CUT) h = math.min(h, fl + Regions.Terrace(WallRise(s)));
                }
            }
            return h;
        }

        /// How far a mesa reaches at most: its ramp's tip on the irregular outline.
        public static float MesaReach(Feature f) => MESA_STRETCH * (1f + WorldMap.HILL_LOBE) * (f.R + math.max(f.B, f.L));

        /// Mesa profile weight at q = p - centre: MesaT on an irregular outline.
        private static float MesaWeight(Feature f, float2 q)
        {
            float x = WorldMap.IrregularX(q, f.R, f.dir, MESA_STRETCH, f.noiseOff);
            return WorldMap.MesaT(q, x * f.R, f.R, f.B, f.L, f.dir);
        }

        /// Height of the canyon's allowed surface above its floor at s voxels
        /// from the centre line: the floor, then a WALL slope, then steeper and
        /// steeper so it clears any ground by CANYON_CUT (no step at the edge).
        private static float WallRise(float s)
        {
            float u = math.max(0f, s - HALF_FLOOR), v = math.max(0f, u - 1000f);
            return WALL * u + 0.01f * v * v;
        }

        /// B5b: the tableland's share at p before its outer slope: the shore,
        /// the easy areas (WorldMap passes both as `bankW`; BankW recomputes it)
        /// and the side canyons give way.
        private static float BankShare(Feature f, float2 p, float bankW) => f.gap != null ? bankW * (1f - GapAt(f, p)) : bankW;

        /// What WorldMap passes Apply as `bankW` at p.
        public static float BankW(in TerrainSpec spec, in Offsets o, EasyAreas.Site[] easy, float2 p)
            => Noise.SmoothStep(0f, BANK_SHORE, WorldMap.Coast(in spec, in o, p)) * (1f - EasyAreas.Weight(easy, in o, p, out _));

        /// The share of the full bank on lateral offset `lat`.
        private static float SideOf(Feature f, float lat) => BANK_LOW + (1f - BANK_LOW) * Noise.SmoothStep(-SIDE_BLEND, SIDE_BLEND, lat * f.highSide);

        /// Distance from p to the canyon's centre line, the floor and the
        /// tableland's rise there (the full one), and p's lateral offset from
        /// the nearest segment's line (> 0 left of the path, head to mouth);
        /// false past BANK_REACH.
        private static bool CanyonAt(Feature f, float2 p, out float dist, out float fl, out float bank, out float lat)
        {
            dist = float.MaxValue; fl = 0f; bank = 0f; lat = 0f;
            int k0 = 0, k1 = f.path.Length - 2;
            if (f.segLo != null)
            {
                int cx = (int)math.floor(p.x / f.gapCell) - f.gapLo.x, cz = (int)math.floor(p.y / f.gapCell) - f.gapLo.y;
                if (cx >= 0 && cz >= 0 && cx < f.gapW && cz < f.gapH)
                {
                    int j = cz * f.gapW + cx;
                    if (f.segLo[j] < 0) return false;
                    k0 = f.segLo[j]; k1 = f.segHi[j];
                }
            }
            for (int k = k0; k <= k1; k++)
            {
                float2 a = f.path[k], ab = f.path[k + 1] - a;
                float t = math.saturate(math.dot(p - a, ab) / math.dot(ab, ab));
                float2 d = p - (a + ab * t);
                float dd = d.x * d.x + d.y * d.y;
                if (dd < dist)
                {
                    dist = dd; fl = math.lerp(f.floor[k], f.floor[k + 1], t); bank = math.lerp(f.bank[k], f.bank[k + 1], t);
                    float2 q = p - a;
                    lat = (ab.x * q.y - ab.y * q.x) / math.sqrt(math.dot(ab, ab));
                }
            }
            dist = math.sqrt(dist);
            return dist < BANK_REACH;
        }

        /// True on the feature's footprint (the record's FLAG_FEATURE cells).
        /// `spec`, `o`, `easy` give a canyon's BankW. A pure function of the
        /// feature and the spec, never of the map's heights (the routes ease
        /// those after the first record).
        public static bool InFootprint(Feature f, float2 p, in TerrainSpec spec, in Offsets o, EasyAreas.Site[] easy)
        {
            float2 q = p - (float2)f.centre;
            float d = math.sqrt(q.x * q.x + q.y * q.y);
            switch (f.kind)
            {
                case GreatPeak: return d < PEAK_FOOT;
                case Volcano: return d < Stamps.VOLCANO_BASE_R;
                case Mesa: return d < MesaReach(f) && MesaWeight(f, q) > 0.01f;
                case CraterLake: return d < f.footR;
                case GreatCanyon:
                {
                    if (p.x < f.bbMin.x || p.y < f.bbMin.y || p.x > f.bbMax.x || p.y > f.bbMax.y) return false;
                    float best = float.MaxValue; float hw = 0f, hwNat = 0f;
                    for (int k = 0; k + 1 < f.path.Length; k++)
                    {
                        float2 a = f.path[k], ab = f.path[k + 1] - a;
                        float t = math.saturate(math.dot(p - a, ab) / math.dot(ab, ab));
                        float2 e = p - (a + ab * t);
                        float dd = e.x * e.x + e.y * e.y;
                        if (dd < best) { best = dd; hw = math.lerp(f.halfW[k], f.halfW[k + 1], t); hwNat = math.lerp(f.halfWNat[k], f.halfWNat[k + 1], t); }
                    }
                    // B5b: the walls reach the ground as it was, or the
                    // tableland where it stands (it gives way to an easy area,
                    // the shore, a side canyon); then 20 m, or on the
                    // tableland its rim, the BANK_RIM you look in from:
                    // s < min(hw, max(hwNat, wall)) + lerp(20 m, BANK_RIM, share), the bank read only between.
                    float sd = math.sqrt(best);
                    if (sd >= hw + BANK_RIM) return false;
                    if (sd < hwNat + 200f) return true;
                    CanyonAt(f, p, out _, out _, out float dt, out float lat);
                    float share = BankShare(f, p, BankW(in spec, in o, easy, p));
                    float wall = HALF_FLOOR + dt * SideOf(f, lat) * share / WALL;
                    return sd < math.min(hw, math.max(hwNat, wall)) + math.lerp(200f, BANK_RIM, share);
                }
                default: return false;
            }
        }

        /// Flags the footprint cells (FLAG_FEATURE, site = index + 1) and fills
        /// the record's feature list. Runs before EasyAreas.Mark.
        public static void Mark(WorldMap map, WorldRecord rec)
        {
            var feats = map.SignatureFeatures;
            if (feats == null) return;
            var spec = map.Spec;
            var o = Offsets.For(in spec);   // B5b: BankW for the canyons
            int n = spec.mapCells, cv = spec.mapCellVoxels;
            for (int k = 0; k < feats.Length; k++)
            {
                var f = feats[k];
                float2 lo, hi;
                if (f.path != null) { lo = f.bbMin + (BANK_REACH - FOOT_MAX); hi = f.bbMax - (BANK_REACH - FOOT_MAX); }   // the walls' and the rim's box
                else { float r = f.kind == Mesa ? MesaReach(f) : f.footR; lo = (float2)f.centre - r; hi = (float2)f.centre + r; }
                int x0 = math.max(0, (int)(lo.x / cv)), z0 = math.max(0, (int)(lo.y / cv));
                int x1 = math.min(n - 1, (int)(hi.x / cv)), z1 = math.min(n - 1, (int)(hi.y / cv));
                int count = 0;
                for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    int i = z * n + x;
                    if (map.Height[i] <= 0f || rec.Data[i].site != 0) continue;
                    if (!InFootprint(f, new float2((x + 0.5f) * cv, (z + 0.5f) * cv), in spec, in o, map.EasySites)) continue;
                    rec.Data[i].flags |= WorldRecord.FLAG_FEATURE;
                    rec.Data[i].site = (byte)(k + 1);
                    count++;
                }
                f.cellCount = count;
            }
            rec.FeatureList = feats;
        }

        // ------------------------------------------------------------------
        // MEASUREMENT (acceptance and the report; never part of the map)
        // ------------------------------------------------------------------
        /// Where an eye sees the feature from: the summit area and the upper
        /// flanks (half the radius out: the upper half of a peak or a cone),
        /// the caldera rim, the mesa's top edge, the crater rim, or the
        /// canyon's walls at mid-height (to see a canyon is to see into it).
        /// Voxels (x, z).
        public static List<float2> Targets(Feature f, WorldMap map)
        {
            var t = new List<float2>();
            float2 c = (float2)f.centre;
            void Ring(float r, int count)
            {
                for (int a = 0; a < count; a++)
                {
                    float ang = a * 2f * math.PI / count;
                    t.Add(c + new float2(math.cos(ang), math.sin(ang)) * r);
                }
            }
            switch (f.kind)
            {
                case GreatPeak:
                {
                    int n = map.Spec.mapCells, cv = map.Spec.mapCellVoxels, r = (int)(0.4f * PEAK_R / cv);
                    int cx = f.centre.x / cv, cz = f.centre.y / cv, bi = -1; float bh = float.MinValue;
                    for (int z = math.max(0, cz - r); z <= math.min(n - 1, cz + r); z++)
                    for (int x = math.max(0, cx - r); x <= math.min(n - 1, cx + r); x++)
                        if (map.Height[z * n + x] > bh) { bh = map.Height[z * n + x]; bi = z * n + x; }
                    if (bi >= 0) t.Add(new float2((bi % n + 0.5f) * cv, (bi / n + 0.5f) * cv));
                    Ring(0.3f * PEAK_R, 8);
                    Ring(0.5f * PEAK_R, 8);   // the upper half of the mountain
                    break;
                }
                case Volcano: Ring(Stamps.CALDERA_R, 12); Ring(0.5f * Stamps.VOLCANO_BASE_R, 8); break;
                case Mesa: t.Add(c); Ring(0.8f * f.R, 12); break;
                case CraterLake: Ring(1.1f * f.R, 12); break;
                case GreatCanyon:
                    for (int k = 0; k + 1 < f.path.Length; k += 5)
                    {
                        float2 a = f.path[k], d = math.normalizesafe(f.path[k + 1] - a);
                        float2 side = new float2(-d.y, d.x) * (HALF_FLOOR + 0.5f * (f.halfW[k] - HALF_FLOOR));
                        t.Add(a + side); t.Add(a - side);
                    }
                    break;
            }
            return t;
        }

        /// True when nothing on the map (ground, or the sea surface) rises
        /// above the straight line from (e, he) to (t, ht), sampled every map
        /// cell, nearest cell. Heights in voxels relative to sea.
        public static bool LineOfSight(WorldMap map, float2 e, float he, float2 t, float ht)
        {
            int n = map.Spec.mapCells; float cv = map.Spec.mapCellVoxels;
            int steps = (int)(math.distance(e, t) / cv);
            for (int s = 1; s < steps; s++)
            {
                float u = s / (float)steps;
                float2 q = math.lerp(e, t, u);
                int x = math.clamp((int)(q.x / cv), 0, n - 1), z = math.clamp((int)(q.y / cv), 0, n - 1);
                if (Surface(map, z * n + x) > math.lerp(he, ht, u)) return false;
            }
            return true;
        }

        /// What you stand on or look across: the ground, a lake's or river's
        /// surface, or the sea's (voxels rel. sea).
        public static float Surface(WorldMap map, int i)
        {
            float w = map.Water[i];
            return math.max(math.max(map.Height[i], 0f), w != Hydrology.NO_WATER ? w : 0f);
        }

        /// A canyon's depth on the final map (metres): at every path point the
        /// higher of the two rims (ground 40 m past its half width) less the
        /// floor there; the deepest and the median, mouth excluded.
        public static void CanyonDepth(Feature f, WorldMap map, out float maxM, out float medianM) => CanyonDepth(f, map, out maxM, out medianM, out _);

        /// ... and `profile`: the depth (m) at each tenth of the path, head to
        /// mouth, as "L|R" rims above the floor (L = left of the path).
        public static void CanyonDepth(Feature f, WorldMap map, out float maxM, out float medianM, out string profile)
        {
            int n = map.Spec.mapCells; float cv = map.Spec.mapCellVoxels;
            float H(float2 p) { int x = math.clamp((int)(p.x / cv), 0, n - 1), z = math.clamp((int)(p.y / cv), 0, n - 1); return map.Height[z * n + x]; }
            var d = new List<float>();
            for (int k = 0; k + 3 < f.path.Length; k++)
            {
                float2 a = f.path[k], dir = math.normalizesafe(f.path[k + 1] - a), side = new float2(-dir.y, dir.x) * (f.halfW[k] + 400f);
                d.Add((math.max(H(a + side), H(a - side)) - H(a)) / 10f);
            }
            var pr = new System.Text.StringBuilder();
            for (int q = 0; q < 10 && f.path.Length > 4; q++)
            {
                int k = q * (f.path.Length - 4) / 10;
                float2 a = f.path[k], dir = math.normalizesafe(f.path[k + 1] - a), side = new float2(-dir.y, dir.x) * (f.halfW[k] + 400f);
                pr.Append($"{(H(a + side) - H(a)) / 10f:F0}|{(H(a - side) - H(a)) / 10f:F0} ");
            }
            profile = pr.ToString().TrimEnd();
            d.Sort();
            maxM = d.Count > 0 ? d[d.Count - 1] : 0f; medianM = d.Count > 0 ? d[d.Count / 2] : 0f;
        }

        /// A canyon's walls on the final map: the land cells between the floor's
        /// edge and the half width that stand over 5 m above the floor, and the
        /// share of them steeper than 45 deg (central differences, 8 m cells).
        public static void CanyonWalls(Feature f, WorldMap map, out int cells, out float steepShare)
        {
            int n = map.Spec.mapCells; float cv = map.Spec.mapCellVoxels;
            float[] h = map.Height;
            int x0 = math.max(1, (int)(f.bbMin.x / cv)), z0 = math.max(1, (int)(f.bbMin.y / cv));
            int x1 = math.min(n - 2, (int)(f.bbMax.x / cv)), z1 = math.min(n - 2, (int)(f.bbMax.y / cv));
            cells = 0; int steep = 0;
            for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
            {
                int i = z * n + x;
                if (h[i] <= 0f) continue;
                float2 p = new float2((x + 0.5f) * cv, (z + 0.5f) * cv);
                float best = float.MaxValue, hw = 0f, fl = 0f;
                for (int k = 0; k + 1 < f.path.Length; k++)
                {
                    float2 a = f.path[k], ab = f.path[k + 1] - a;
                    float t = math.saturate(math.dot(p - a, ab) / math.dot(ab, ab));
                    float2 e = p - (a + ab * t);
                    float dd = e.x * e.x + e.y * e.y;
                    if (dd < best) { best = dd; hw = math.lerp(f.halfW[k], f.halfW[k + 1], t); fl = math.lerp(f.floor[k], f.floor[k + 1], t); }
                }
                float s = math.sqrt(best);
                if (s <= HALF_FLOOR || s >= hw || h[i] < fl + 50f) continue;
                cells++;
                float gx = (h[i + 1] - h[i - 1]) * 0.5f, gz = (h[i + n] - h[i - n]) * 0.5f;
                if (gx * gx + gz * gz > cv * cv) steep++;
            }
            steepShare = cells > 0 ? steep / (float)cells : 0f;
        }

        public struct Visibility
        {
            public int eyes, seeing, near;   // land eyes; seeing a feature within 3 km; with a target within 3 km
            public float Share => eyes > 0 ? seeing / (float)eyes : 0f;
            /// Of the land within 3 km of a feature, the share that sees none:
            /// found by cresting a rise, not from where you stand.
            public float HiddenNear => near > 0 ? (near - seeing) / (float)near : 0f;
            public int[] seenBy;             // per feature: eyes that see it
            public List<int> eyeCell; public List<bool> eyeSees;   // per eye: map cell, sees a feature
        }

        /// The acceptance rule (owner Q6 (a)): share of land that sees a
        /// signature feature within 3 km from a 2 m eye; an eye on a feature's
        /// footprint (the record's FLAG_FEATURE) sees that one. Eyes on every
        /// `eyeStride`-th map cell of land (16 = a 128 m grid), 2 m above the
        /// ground or the water standing on it; per feature its Targets; a ray
        /// each to the target's surface + 1 m, sampled every map cell (8 m)
        /// against the ground or the water surface (lake, river, sea), nearest cell.
        public static Visibility VisibleShare(WorldMap map, int eyeStride = 16)
        {
            var spec = map.Spec;
            int n = spec.mapCells; float cv = spec.mapCellVoxels;
            float[] h = map.Height;
            var feats = map.SignatureFeatures ?? new Feature[0];
            var targets = new List<float2>[feats.Length];
            for (int k = 0; k < feats.Length; k++) targets[k] = Targets(feats[k], map);
            float G(float2 p)
            {
                int x = math.clamp((int)(p.x / cv), 0, n - 1), z = math.clamp((int)(p.y / cv), 0, n - 1);
                return Surface(map, z * n + x);
            }
            var v = new Visibility { seenBy = new int[feats.Length], eyeCell = new List<int>(), eyeSees = new List<bool>() };
            for (int z = eyeStride / 2; z < n; z += eyeStride)
            for (int x = eyeStride / 2; x < n; x += eyeStride)
            {
                if (h[z * n + x] <= 0f) continue;
                v.eyes++;
                int own = map.Record != null && map.Record.Data[z * n + x].IsFeature ? map.Record.Data[z * n + x].site - 1 : -1;
                float2 e = new float2((x + 0.5f) * cv, (z + 0.5f) * cv);
                float he = Surface(map, z * n + x) + 20f;   // on a lake or river: 2 m above its surface
                bool near = false, seen = false;
                for (int k = 0; k < feats.Length; k++)
                {
                    bool seesK = k == own;   // standing on it
                    if (seesK) near = true;
                    if (!seesK)
                    foreach (var tg in targets[k])
                    {
                        float dist = math.distance(e, tg);
                        if (dist > VIEW) continue;
                        near = true;
                        if (LineOfSight(map, e, he, tg, G(tg) + 10f)) { seesK = true; break; }
                    }
                    if (seesK) { v.seenBy[k]++; seen = true; }
                }
                if (near) v.near++;
                if (seen) v.seeing++;
                v.eyeCell.Add(z * n + x); v.eyeSees.Add(seen);
            }
            return v;
        }
    }
}
