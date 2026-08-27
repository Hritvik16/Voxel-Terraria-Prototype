// Assets/CoreEngine/WorldGen/AnchorPlanner.cs
//
// Phase 3, file 2 of the spec's ordered list (§13 Phase 3): Stage 1 global
// planning (§5.2) — "Generates the FeatureAnchor set (non-overlapping bounding
// volumes for mountains, craters, etc.), Voronoi biome seeds, and persists them
// in world.meta (D.2), so generation is deterministic regardless of exploration
// order. Sub-second even for Large."
//
// Poisson placement is dart-throwing with a minimum-separation rejection test
// (sum of radii + margin), seeded by Unity.Mathematics.Random so two runs with
// the same seed produce byte-identical plans (asserted in GenerationTests).
// If darts can't land after MAX_ATTEMPTS the planner emits FEWER anchors and
// logs it — it never loops forever and never overlaps.
using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace VoxelEngine.WorldGen
{
    public static class AnchorPlanner
    {
        // v1 counts for the sizeClass-0 dev region. All voxel units.
        private const int MOUNTAIN_COUNT = 2;
        private const int CRATER_COUNT = 2;
        private const int CAVE_COUNT = 3;
        private const int BIOME_SEED_COUNT = 6;

        private const float SEPARATION_MARGIN = 60f; // between anchor bounding circles
        private const int MAX_ATTEMPTS = 800;

        // RADIUS CHOICES (revised — see chat, first Phase 3 acceptance run):
        // the original 300-450 mountain radius needed two circles up to 1000
        // voxels apart inside a disc only 1155 voxels across — geometrically
        // feasible only near-diametrically-opposite, which 400 random attempts
        // rarely found (observed: 1/2 placed). Radii below are sized so the
        // worst-case required separation is well under half the placement
        // disc's diameter, making success the common case rather than a
        // lucky one, instead of just raising MAX_ATTEMPTS and hoping.
        public static WorldMetaData Plan(uint seed, byte sizeClass)
        {
            WorldGenConstants.DeriveIslandGeometry(sizeClass,
                out float cx, out float cz, out float coastR, out _);

            // Random(0) is invalid for Unity.Mathematics.Random — guard.
            var rng = new Unity.Mathematics.Random(seed == 0 ? 0x9E3779B9u : seed * 2654435761u + 1u);

            var anchors = new List<FeatureAnchor>();

            // ---- Mountains: height-add features, well inside the coastline ----
            PlaceHeightAnchors(ref rng, anchors, FeatureKind.Mountain, MOUNTAIN_COUNT,
                cx, cz, placementRadius: coastR * 0.55f,
                radiusMin: 150f, radiusMax: 240f, magMin: 35f, magMax: 55f);

            // ---- Craters: height-subtract bowls; floors can dip below sea level
            //      and pool water (§5.5 "a feature holds a pool") ----
            PlaceHeightAnchors(ref rng, anchors, FeatureKind.Crater, CRATER_COUNT,
                cx, cz, placementRadius: coastR * 0.70f,
                radiusMin: 70f, radiusMax: 110f, magMin: 14f, magMax: 20f);

            // ---- Caves: 3D horizontal capsules, constrained ABOVE sea level
            //      (so the water-fill pass can never flood them — v1 keeps
            //      "caves with clearance" and static water strictly separate)
            //      and BELOW the local base terrain (so they're underground,
            //      breaching the surface only where hills dip = natural mouths).
            int cavesPlaced = 0;
            for (int i = 0; i < CAVE_COUNT; i++)
            {
                bool placed = false;
                for (int attempt = 0; attempt < MAX_ATTEMPTS && !placed; attempt++)
                {
                    float radius = rng.NextFloat(6f, 9f);
                    float halfLen = rng.NextFloat(25f, 45f);
                    float2 pos = RandomInDisc(ref rng, cx, cz, coastR * 0.55f);
                    float2 dir = math.normalize(rng.NextFloat2Direction());

                    // yMin: clearance above sea level. yMax: derived from the
                    // ACTUAL terrain height at this XZ, not a hardcoded ceiling —
                    // the base heightfield only reaches INLAND_BASE+HILL_AMPLITUDE
                    // (28+24=52 voxels), so a fixed yMax=44 combined with the old
                    // "sample cy blind, then check burial" order meant most draws
                    // asked for a ceiling (up to cy+radius+4=57) the terrain could
                    // never provide (observed: 1/3 caves placed). Sampling baseH
                    // FIRST and deriving cy's range from it means every draw that
                    // reaches the RNG call below is already geometrically valid.
                    int baseH = ColumnSampler.SampleBaseHeight(seed, sizeClass, (int)pos.x, (int)pos.y);
                    float yMin = WorldGenConstants.SEA_LEVEL_VOXEL_Y + radius + 4f;
                    float yMax = baseH - radius - 4f; // must stay buried under THIS column
                    if (yMin >= yMax) continue; // this XZ can't host a cave (near coast/low terrain) — try another position, not a dead end
                    float cy = rng.NextFloat(yMin, yMax);

                    var candidate = new FeatureAnchor
                    {
                        kind = FeatureKind.Cave,
                        cx = pos.x, cy = cy, cz = pos.y,
                        radius = radius, magnitude = 0f,
                        dirX = dir.x, dirZ = dir.y, halfLength = halfLen,
                        salt = rng.NextUInt(),
                    };
                    if (Separated(anchors, candidate)) { anchors.Add(candidate); placed = true; cavesPlaced++; }
                }
            }
            if (cavesPlaced < CAVE_COUNT)
                UnityEngine.Debug.LogWarning($"[AnchorPlanner] Only placed {cavesPlaced}/{CAVE_COUNT} caves after {MAX_ATTEMPTS} attempts each (seed {seed}).");

            // ---- Voronoi biome seeds: guarantee all four §5.5 biomes appear
            //      by round-robin assignment (i % 4), positions Poisson-ish ----
            var seeds = new List<BiomeSeed>();
            for (int i = 0; i < BIOME_SEED_COUNT; i++)
            {
                float2 pos = RandomInDisc(ref rng, cx, cz, coastR * 0.90f);
                seeds.Add(new BiomeSeed { x = pos.x, z = pos.y, biomeId = (byte)(i % Biomes.Table.Length) });
            }

            return new WorldMetaData
            {
                seed = seed,
                sizeClass = sizeClass,
                formatVersion = WorldMeta.FORMAT_VERSION,
                contentVersionHash = WorldGenConstants.ContentVersionHash(),
                anchors = anchors.ToArray(),
                biomeSeeds = seeds.ToArray(),
            };
        }

        private static void PlaceHeightAnchors(ref Unity.Mathematics.Random rng,
            List<FeatureAnchor> anchors, FeatureKind kind, int count,
            float cx, float cz, float placementRadius,
            float radiusMin, float radiusMax, float magMin, float magMax)
        {
            int placedCount = 0;
            for (int i = 0; i < count; i++)
            {
                bool placed = false;
                for (int attempt = 0; attempt < MAX_ATTEMPTS && !placed; attempt++)
                {
                    float radius = rng.NextFloat(radiusMin, radiusMax);
                    float mag = rng.NextFloat(magMin, magMax);
                    float2 pos = RandomInDisc(ref rng, cx, cz, placementRadius);
                    var candidate = new FeatureAnchor
                    {
                        kind = kind, cx = pos.x, cy = 0f, cz = pos.y,
                        radius = radius, magnitude = mag,
                        dirX = 0f, dirZ = 0f, halfLength = 0f,
                        salt = rng.NextUInt(),
                    };
                    if (Separated(anchors, candidate)) { anchors.Add(candidate); placed = true; placedCount++; }
                }
            }
            if (placedCount < count)
                UnityEngine.Debug.LogWarning($"[AnchorPlanner] Only placed {placedCount}/{count} {kind} anchors.");
        }

        private static float2 RandomInDisc(ref Unity.Mathematics.Random rng, float cx, float cz, float radius)
        {
            // sqrt for uniform area density; fully deterministic.
            float r = math.sqrt(rng.NextFloat()) * radius;
            float a = rng.NextFloat() * 2f * math.PI;
            return new float2(cx + math.cos(a) * r, cz + math.sin(a) * r);
        }

        // Non-overlap per §5.2: XZ bounding circles (radius + cave reach) must
        // not intersect, plus a margin. Caves' effective XZ reach includes the
        // capsule half-length.
        private static bool Separated(List<FeatureAnchor> existing, in FeatureAnchor candidate)
        {
            float cr = EffectiveXZRadius(candidate);
            for (int i = 0; i < existing.Count; i++)
            {
                var e = existing[i];
                float dx = e.cx - candidate.cx, dz = e.cz - candidate.cz;
                float minDist = EffectiveXZRadius(e) + cr + SEPARATION_MARGIN;
                if (dx * dx + dz * dz < minDist * minDist) return false;
            }
            return true;
        }

        private static float EffectiveXZRadius(in FeatureAnchor a)
            => a.kind == FeatureKind.Cave ? a.halfLength + a.radius : a.radius;
    }
}