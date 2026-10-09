// Assets/TerrainGen/Danger.cs
//
// BASE DANGER + MOVEMENT BAND (map v4, surface rebuild B6, plan rule 8; owner
// Q11 (a)). Written into WorldRecord's `hazard` byte per 8 m map cell:
//   bits 0-2  base danger 0-7 = altitude tier + landform tier + biome tier
//   bits 3-4  movement band from the 8 m map slope (Walk, Scramble, Climb, Sheer)
//   bits 5-7  spare (zero)
// World progress adds to danger at run time, so it is never stored. Distance
// from the start is NOT an input (owner: no distance zones); B6's acceptance
// gates |correlation of danger with distance| <= 0.3.
//
// THE TIER TABLE IS DATA (Tiers.Default), one table to retune once creatures
// exist; the code below only adds its three lookups and clamps to 7.
//   altitude  thirds of the world's own land height range [0, highest land],
//             EasyAreas' cut (under a third of the highest land is the lowest
//             third), so every easy cell is altitude tier 0
//   landform  Regions.ClassOf: gentle 0, broken 1, steep 2
//   biome     per GenBiome id. The plan's rows: forest, savanna 0; alpine taiga,
//             badlands 1; jungle, desert, snow 2; volcanic ashlands 3. Savanna and
//             badlands (B10), alpine taiga and ashlands have no GenBiome id yet;
//             they take their row when they get one. A biome with no row throws:
//             a new biome must be given a tier.
// Sea cells (map height <= 0) carry hazard 0: read the archetype (IsSea) first.
// Lakes and rivers are land and are rated like the ground under them; a land
// cell the layout left outside every region (Region 255) rates as gentle.
//
// THE MOVEMENT BANDS live here and only here (EasyAreas and PlayMetricsReport
// read them): Walk < 15, Scramble 15-30, Climb 30-45, Sheer >= 45 deg -- the
// review's cut points, PlayMetricsReport's provisional bands, fixed by B6.
// Slope = central differences over the 8 m map (one-sided at the map edge),
// compared as tan^2, so no atan runs and the record does not depend on one.
//
// Determinism: +, -, *, / and compares on the map's floats (TerrainSpec.cs
// contract); the record is never saved, so co-op peers rebuild these bytes.
using System;
using Unity.Mathematics;

namespace TerrainGen
{
    public static class Danger
    {
        public const byte Walk = 0, Scramble = 1, Climb = 2, Sheer = 3;
        public const int BANDS = 4;
        /// Band upper edges in degrees (Walk < 15 <= Scramble < 30 <= Climb < 45 <= Sheer).
        public const float WALK_DEG = 15f, SCRAMBLE_DEG = 30f, CLIMB_DEG = 45f;
        /// The same edges as tan^2 of the slope: tan^2 15, tan^2 30 (= 1/3), tan^2 45 (= 1).
        public const float WALK_TAN2 = 0.0717967697f, SCRAMBLE_TAN2 = 0.333333333f, CLIMB_TAN2 = 1f;
        public const int MAX = 7;
        public const byte NONE = 255;              // a biome the table does not rate

        public sealed class Tiers
        {
            public byte[] altitude;                // per third of the land height range, lowest first
            public byte[] landform;                // per Regions class: gentle, broken, steep
            public byte[] biome;                   // per GenBiome id (16 slots); NONE = no row
        }

        public static readonly Tiers Default = new Tiers
        {
            altitude = new byte[] { 0, 1, 2 },
            landform = new byte[] { 0, 1, 2 },
            biome = BiomeRows(
                (GenBiome.Forest, 0),              // savanna 0 when it gets an id (B10)
                (GenBiome.Jungle, 2), (GenBiome.Desert, 2), (GenBiome.Snow, 2)),
                                                   // alpine taiga 1, badlands 1 (B10), volcanic ashlands 3
        };

        private static byte[] BiomeRows(params (byte id, byte tier)[] rows)
        {
            var t = new byte[16];
            for (int i = 0; i < t.Length; i++) t[i] = NONE;
            foreach (var r in rows) t[r.id] = r.tier;
            return t;
        }

        /// tan^2 of the 8 m map slope at cell i: central differences, one-sided at the edge.
        public static float SlopeTan2(float[] h, int n, int cellVoxels, int i)
        {
            int x = i & (n - 1), z = i / n;
            int x0 = math.max(x - 1, 0), x1 = math.min(x + 1, n - 1);
            int z0 = math.max(z - 1, 0), z1 = math.min(z + 1, n - 1);
            float gx = (h[z * n + x1] - h[z * n + x0]) / ((x1 - x0) * cellVoxels);
            float gz = (h[z1 * n + x] - h[z0 * n + x]) / ((z1 - z0) * cellVoxels);
            return gx * gx + gz * gz;
        }

        public static byte BandOfTan2(float t2) =>
            t2 < WALK_TAN2 ? Walk : t2 < SCRAMBLE_TAN2 ? Scramble : t2 < CLIMB_TAN2 ? Climb : Sheer;

        public static byte BandOfDegrees(float deg) =>
            deg < WALK_DEG ? Walk : deg < SCRAMBLE_DEG ? Scramble : deg < CLIMB_DEG ? Climb : Sheer;

        /// The land's highest map height (voxels above the sea); the altitude
        /// thirds cut [0, this] -- EasyAreas' `hi`.
        public static float HighestLand(float[] h)
        {
            float hi = 0f;
            for (int i = 0; i < h.Length; i++) if (h[i] > 0f) hi = math.max(hi, h[i]);
            return hi;
        }

        /// 0, 1 or 2: which third of [0, hi] height h is in (h < hi / 3 is the lowest).
        public static int AltitudeThird(float h, float hi) => h < hi / 3f ? 0 : h < 2f * hi / 3f ? 1 : 2;

        public static int LandformClassOf(byte region) => region == 255 ? Regions.Gentle : Regions.ClassOf(region);

        public static int Of(Tiers t, int altitudeThird, int landformClass, byte biome)
        {
            byte b = t.biome[biome & 15];
            if (b == NONE) throw new InvalidOperationException($"Danger: biome {biome} has no row in the tier table");
            return math.min(MAX, t.altitude[altitudeThird] + t.landform[landformClass] + b);
        }

        public static byte Pack(int danger, byte band) => (byte)(danger | (band << 3));

        /// Writes every cell's hazard byte (WorldRecord.Build, after the biome is in).
        public static void Mark(WorldMap map, WorldRecord rec, Tiers t = null)
        {
            t = t ?? Default;
            var spec = map.Spec;
            int n = spec.mapCells, cv = spec.mapCellVoxels;
            float[] h = map.Height;
            byte[] region = map.Region;
            float hi = HighestLand(h);
            for (int i = 0; i < h.Length; i++)
            {
                if (h[i] <= 0f) { rec.Data[i].hazard = 0; continue; }
                int d = Of(t, AltitudeThird(h[i], hi), LandformClassOf(region[i]), rec.Data[i].Biome);
                rec.Data[i].hazard = Pack(d, BandOfTan2(SlopeTan2(h, n, cv, i)));
            }
        }
    }
}
