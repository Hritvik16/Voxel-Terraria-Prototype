// Assets/TerrainGen/Regions.cs
//
// TERRAINGEN v3, STAGE T1 -- REGIONS (design doc §13; owner, 2026-09-25: "like
// in One Piece: one island should have lots of varying topographies";
// reference: the Minecraft mods Tectonic / Terralith).
//
// The island is split into ~12 organic regions. Each gets a TOPOGRAPHY
// ARCHETYPE: how its land is pushed up (the starting shape), how easily its
// rock erodes, and how steep its slopes may stand. Erosion (Erosion.cs, T2) then
// turns those into valleys, ridges, canyons and plains; this file only decides
// WHERE each kind of land is and what it starts as.
//
// Placement: the dominant peak's region is ALPINE and the volcano's is
// VOLCANIC (both pinned to their sites); the rest are dart-thrown over the
// island. Seeds near the coast draw from the coastal archetypes, inland seeds
// from the inland ones, each list shuffled per seed so every island differs.
// Borders are the seeds' Voronoi edges bent by a 2-D noise warp (organic, no
// straight lines) and blended over ~150 m, so an archetype change reads as a
// pass, an escarpment or a valley rather than a seam.
//
// Map v4 (surface rebuild B2, plan rule 2): the seeds are the same, but the
// archetypes are dealt by LANDFORM CLASS (gentle / broken / steep) so that the
// gentle class covers spec.GentleShare of the land (AssignByFlatness).
//
// No 3-D noise (owner constraint). Only +, -, *, /, sqrt, floor and 2-D noise,
// per the determinism contract (TerrainSpec.cs): the map is built by managed
// code in both the Editor (Mono) and the player (IL2CPP).
using System.Collections.Generic;
using Unity.Mathematics;

namespace TerrainGen
{
    public static class Regions
    {
        public const byte Alpine = 0, Highlands = 1, Canyonlands = 2, Volcanic = 3, Plains = 4,
                          Badlands = 5, Dunes = 6, CoastalCliffs = 7, Plateau = 8, Wetland = 9;
        public const int KINDS = 10;
        public const int TARGET_REGIONS = 12;
        /// Seeds closer than this fraction of the island radius are rejected.
        public const float MIN_SEPARATION = 0.34f;
        /// Border blend width (voxels): regions fade into each other over ~2x this.
        public const float BLEND = 1500f;         // (800 read as crisp lines in the first review)
        public const float WARP = 2600f;          // voxels of organic border wander

        public static string Name(byte k)
        {
            switch (k)
            {
                case Alpine: return "alpine range";
                case Highlands: return "rolling highlands";
                case Canyonlands: return "canyonlands";
                case Volcanic: return "volcanic";
                case Plains: return "river plains";
                case Badlands: return "badlands";
                case Dunes: return "dune sea";
                case CoastalCliffs: return "sea cliffs";
                case Plateau: return "high plateau";
                case Wetland: return "wetland basin";
                default: return "?";
            }
        }

        public sealed class Layout
        {
            public float2[] seeds;
            public byte[] kind;
            public int Count => seeds.Length;
        }

        /// The region layout of a world (deterministic from the seed).
        public static Layout Build(in TerrainSpec spec, in Offsets o)
        {
            float c = spec.worldSizeVoxels * 0.5f, R = spec.islandRadius;
            var seeds = new List<float2> { WorldMap.PeakSite(in spec, in o), Stamps.VolcanoSite(in spec, in o) };
            for (int i = 0; i < 800 && seeds.Count < TARGET_REGIONS; i++)
            {
                float2 p = new float2(c, c) + new float2(Noise.Hash01(i, 1, 7, spec.seed + 501u) - 0.5f,
                                                         Noise.Hash01(i, 2, 7, spec.seed + 502u) - 0.5f) * (2f * R);
                if (math.distance(p, new float2(c, c)) > 0.95f * R) continue;
                if (WorldMap.Coast(in spec, in o, p) <= 0.04f) continue;
                bool ok = true;
                foreach (var s in seeds) if (math.distance(s, p) < MIN_SEPARATION * R) { ok = false; break; }
                if (ok) seeds.Add(p);
            }
            var kind = new byte[seeds.Count];
            kind[0] = Alpine; kind[1] = Volcanic;
            byte[] coastal = Shuffle(new[] { CoastalCliffs, Plains, Dunes, Wetland, CoastalCliffs, Plains }, spec.seed + 511u);
            byte[] inland = Shuffle(new[] { Highlands, Canyonlands, Plateau, Badlands, Alpine, Highlands, Canyonlands, Badlands }, spec.seed + 512u);
            int ci = 0, ii = 0;
            for (int s = 2; s < seeds.Count; s++)
            {
                bool nearCoast = WorldMap.Coast(in spec, in o, seeds[s]) < 0.33f;
                kind[s] = nearCoast ? coastal[ci++ % coastal.Length] : inland[ii++ % inland.Length];
            }
            var L = new Layout { seeds = seeds.ToArray(), kind = kind };
            if (spec.mapVersion >= 4) AssignByFlatness(in spec, in o, L);
            return L;
        }

        // ------------------------------------------------------------------
        // LANDFORM CLASSES (surface rebuild plan rule 2, map v4 / B2). How
        // walkable a region's land is meant to be: gentle = river plains,
        // rolling highlands, wetland basin, plateau tops; broken = eroded
        // breaks (badlands), dunes, canyonlands; steep = alpine, sea cliffs,
        // volcanic.
        // ------------------------------------------------------------------
        public const byte Gentle = 0, Broken = 1, Steep = 2;
        public const int CLASSES = 3;
        /// Broken : steep split of the land the gentle class does not cover.
        public const float BROKEN_OF_REST = 0.6f;

        public static byte ClassOf(byte k)
        {
            switch (k)
            {
                case Plains: case Highlands: case Wetland: case Plateau: return Gentle;
                case Badlands: case Dunes: case Canyonlands: return Broken;
                default: return Steep;
            }
        }

        /// Land-share estimate grid (per edge, over the whole map): every 4th
        /// map cell, 32 m. Region areas are ~1-3 km^2, so its error is well
        /// under a point of share.
        public const int CLASS_GRID = 256;
        /// Coast value past which a cell is land on the final map (the beach
        /// in BuildV3 dips the first ~0.02 of the coast field under the sea).
        public const float LAND_COAST = 0.02f;

        /// Each region's land area, in CLASS_GRID cells: the same nearest-seed
        /// rule (warped Voronoi) that writes WorldMap.Region.
        public static int[] LandCells(in TerrainSpec spec, in Offsets o, Layout L)
        {
            var area = new int[L.Count];
            float step = (float)spec.mapCells * spec.mapCellVoxels / CLASS_GRID;
            for (int z = 0; z < CLASS_GRID; z++)
            for (int x = 0; x < CLASS_GRID; x++)
            {
                float2 p = new float2((x + 0.5f) * step, (z + 0.5f) * step);
                if (WorldMap.Coast(in spec, in o, p) <= LAND_COAST) continue;
                area[Nearest(L, Warp(in o, p), out _)]++;
            }
            return area;
        }

        /// v4: deal every free region (all but the pinned alpine peak and
        /// volcano, which stay steep) a class so the land shares come nearest
        /// gentle = G, broken = 0.6 (1 - G), steep = 0.4 (1 - G), with at least
        /// one gentle and one broken region; then an archetype of that class,
        /// coastal or inland as v3 chooses. Exhaustive over 3^free (<= 3^10 =
        /// 59,049 sums of <= 10 integers). The gentle error weighs 4x: it is
        /// the acceptance number. Ties keep the first assignment in counting
        /// order, so the result is a pure function of the seed and the share.
        public static void AssignByFlatness(in TerrainSpec spec, in Offsets o, Layout L)
        {
            int[] area = LandCells(in spec, in o, L);
            int free = L.Count - 2;
            if (free <= 0) return;
            long total = 0; foreach (int a in area) total += a;
            float G = spec.GentleShare * total, B = BROKEN_OF_REST * (1f - spec.GentleShare) * total;
            float S = total - G - B;
            int combos = 1; for (int i = 0; i < free; i++) combos *= CLASSES;
            int best = 0; float bestCost = float.MaxValue;
            var cls = new int[free];
            for (int c = 0; c < combos; c++)
            {
                int r = c; long g = 0, b = 0, st = area[0] + area[1]; int ng = 0, nb = 0;
                for (int i = 0; i < free; i++)
                {
                    int k = r % CLASSES; r /= CLASSES;
                    if (k == Gentle) { g += area[i + 2]; ng++; }
                    else if (k == Broken) { b += area[i + 2]; nb++; }
                    else st += area[i + 2];
                }
                if (ng == 0 || (nb == 0 && free > 1)) continue;
                float cost = 4f * math.abs(g - G) + math.abs(b - B) + math.abs(st - S);
                if (cost < bestCost) { bestCost = cost; best = c; }
            }
            for (int i = 0; i < free; i++) { cls[i] = best % CLASSES; best /= CLASSES; }

            // Archetypes within a class, per-seed shuffled lists as in v3.
            byte[][] coastal =
            {
                Shuffle(new[] { Plains, Wetland, Plains, Highlands }, spec.seed + 521u),
                Shuffle(new[] { Dunes, Badlands, Dunes }, spec.seed + 522u),
                new[] { CoastalCliffs },
            };
            byte[][] inland =
            {
                Shuffle(new[] { Highlands, Plateau, Plains, Highlands, Plateau }, spec.seed + 523u),
                Shuffle(new[] { Canyonlands, Badlands, Dunes, Canyonlands }, spec.seed + 524u),
                new[] { Alpine },
            };
            var ci = new int[CLASSES]; var ii = new int[CLASSES];
            for (int i = 0; i < free; i++)
            {
                int s = i + 2, k = cls[i];
                bool nearCoast = WorldMap.Coast(in spec, in o, L.seeds[s]) < 0.33f;
                L.kind[s] = nearCoast ? coastal[k][ci[k]++ % coastal[k].Length] : inland[k][ii[k]++ % inland[k].Length];
            }
        }

        private static byte[] Shuffle(byte[] a, uint seed)
        {
            for (int i = a.Length - 1; i > 0; i--)
            {
                int j = (int)(Noise.Hash01(i, 3, 9, seed) * (i + 1));
                (a[i], a[j]) = (a[j], a[i]);
            }
            return a;
        }

        /// Organic borders: bend the sample position with a 2-D warp.
        private static float2 Warp(in Offsets o, float2 p) =>
            p + new float2(Noise.Fbm(p / 7000f + o.coastB + 17f, 3), Noise.Fbm(p / 7000f + o.coastA + 29f, 3)) * WARP;

        private static int Nearest(Layout L, float2 q, out float dmin)
        {
            dmin = float.MaxValue; int nearest = 0;
            for (int i = 0; i < L.Count; i++)
            {
                float d = math.distance(q, L.seeds[i]);
                if (d < dmin) { dmin = d; nearest = i; }
            }
            return nearest;
        }

        /// Per-archetype weights at p (sum 1) and the nearest region's index.
        /// `w` must hold KINDS floats.
        public static int Weights(Layout L, in Offsets o, float2 p, float[] w)
        {
            float2 q = Warp(in o, p);
            int nearest = Nearest(L, q, out float dmin);
            for (int k = 0; k < KINDS; k++) w[k] = 0f;
            float sum = 0f;
            for (int i = 0; i < L.Count; i++)
            {
                float t = (math.distance(q, L.seeds[i]) - dmin) / BLEND;
                if (t >= 3f) continue;
                float f = 1f / (1f + t * t);
                f *= f;                                  // rational falloff: no exp (determinism contract)
                w[L.kind[i]] += f; sum += f;
            }
            for (int k = 0; k < KINDS; k++) w[k] /= sum;
            return nearest;
        }

        // ------------------------------------------------------------------
        // ARCHETYPES: the starting shape (voxels above sea, before erosion and
        // before the coast envelope), erodibility E, and the steepest stable
        // slope (rise over run) for the talus pass. Hard-cap archetypes
        // (canyonlands, plateau) alternate hard and soft layers with height.
        // ------------------------------------------------------------------
        /// Erodibility is tuned on tools/erosion-lab.c against the dumped inputs
        /// of four seeds (2026-09-25): at these values every slope carries its
        /// own valley network while mean land height stays ~80% of the target;
        /// 1/10 of them left hillsides as smooth noise, 2.5x over-dissects.
        public struct Params { public float erodibility, talus, rough, minLake; public bool layered; }

        public static Params P(byte k)
        {
            switch (k)
            {
                // minLake: voxels of standing water before a hollow counts as a lake
                // (first review: flat regions were dotted with puddle lakes).
                case Alpine:        return new Params { erodibility = 0.030f, talus = 1.40f, rough = 1.15f, minLake = 12f };
                case Highlands:     return new Params { erodibility = 0.070f, talus = 0.55f, rough = 0.55f, minLake = 20f };
                case Canyonlands:   return new Params { erodibility = 0.050f, talus = 0.80f, rough = 0.65f, minLake = 20f, layered = true };
                case Volcanic:      return new Params { erodibility = 0.045f, talus = 0.90f, rough = 0.70f, minLake = 12f };
                case Plains:        return new Params { erodibility = 0.080f, talus = 0.25f, rough = 0.30f, minLake = 30f };
                case Badlands:      return new Params { erodibility = 0.130f, talus = 1.10f, rough = 0.85f, minLake = 30f };
                case Dunes:         return new Params { erodibility = 0.020f, talus = 0.60f, rough = 0.35f, minLake = 60f };
                case CoastalCliffs: return new Params { erodibility = 0.035f, talus = 1.80f, rough = 0.90f, minLake = 12f };
                case Plateau:       return new Params { erodibility = 0.025f, talus = 0.80f, rough = 0.50f, minLake = 20f, layered = true };
                case Wetland:       return new Params { erodibility = 0.060f, talus = 0.20f, rough = 0.20f, minLake = 6f };
                default:            return new Params { erodibility = 0.050f, talus = 0.8f, rough = 0.5f, minLake = 12f };
            }
        }

        /// Starting height of archetype k at p (voxels rel. sea), before the
        /// coast envelope. 2-D noise only.
        public static float Shape(byte k, in TerrainSpec spec, in Offsets o, float2 p)
        {
            float u(float2 s, int oct) => Noise.Fbm(s, oct) * 0.5f + 0.5f;   // [0, 1]-ish
            switch (k)
            {
                case Alpine:
                    return 900f + 2900f * Noise.Ridged(p / 9000f + o.ridge, 4) + 500f * u(p / 20000f + o.range, 2);
                case Highlands:
                    return 250f + 950f * u(p / 6500f + o.low, 4);
                case Canyonlands:
                    return 1800f + 700f * u(p / 16000f + o.plateau, 2);
                case Volcanic:
                {
                    float v = Stamps.VolcanoHeight(in spec, in o, p, out _, out _);
                    return math.max(v, 250f + 350f * u(p / 9000f + o.escarp, 3));
                }
                case Plains:
                    return 60f + 220f * u(p / 9000f + o.low + 13f, 3);
                case Badlands:
                    return 550f + 800f * u(p / 5000f + o.valley, 4);
                case Dunes:
                    return 110f + 220f * u(p / 12000f + o.low + 41f, 2);
                case CoastalCliffs:
                    return 900f + 1000f * u(p / 7000f + o.plateauTop, 3);
                case Plateau:
                    return 2200f + 400f * u(p / 22000f + o.plateauTop + 7f, 2);
                case Wetland:
                    return 30f + 70f * u(p / 6000f + o.valley + 5f, 3);
                default:
                    return 100f;
            }
        }

        /// Hard cap-rock layers for the layered archetypes: the top 40% of
        /// every 60 m band resists erosion ~12x -- what leaves benches, mesas
        /// and cliff bands when the soft layers between them wash out. (25 m
        /// bands, the first try, were below what the 16 m erosion grid resolves.)
        public const float LAYER = 600f;
        public static bool HardLayer(float h)
        {
            float f = h / LAYER - math.floor(h / LAYER);
            return f > 0.60f;
        }

        /// Terracing for the layered archetypes on the fine map: each 30 m of
        /// height is a flat bench for 72% of its span and a cliff for the rest --
        /// the stepped walls and mesa tops canyonlands are read by.
        public const float TERRACE = 300f;
        public static float Terrace(float h)
        {
            float k = math.floor(h / TERRACE), f = h / TERRACE - k;
            return (k + Noise.SmoothStep(0.72f, 1f, f)) * TERRACE;
        }
    }
}
