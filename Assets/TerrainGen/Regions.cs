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
            return new Layout { seeds = seeds.ToArray(), kind = kind };
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

        /// Per-archetype weights at p (sum 1) and the nearest region's index.
        /// `w` must hold KINDS floats.
        public static int Weights(Layout L, in Offsets o, float2 p, float[] w)
        {
            // Organic borders: bend the sample position with a 2-D warp.
            float2 q = p + new float2(Noise.Fbm(p / 7000f + o.coastB + 17f, 3), Noise.Fbm(p / 7000f + o.coastA + 29f, 3)) * WARP;
            float dmin = float.MaxValue; int nearest = 0;
            for (int i = 0; i < L.Count; i++)
            {
                float d = math.distance(q, L.seeds[i]);
                if (d < dmin) { dmin = d; nearest = i; }
            }
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
