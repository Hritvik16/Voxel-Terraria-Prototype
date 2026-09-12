// Assets/CoreEngine/Tests/IslandSpawnTests.cs
//
// Pins the bug that put a spawn point in the middle of the ocean.
//
// Phase4Bootstrapper defaulted its camera spawn to a hardcoded
// (140.8, 12, 140.8) m. That was correct for sizeClass 0. Content.cs then added
// sizeClass 1 additively (§D.2) and moved the island centre to ~1280 m -- and
// nothing connected the two, so the literal silently became a point in deep
// water 1.1 km from land. Every new scene that added the component spawned
// there; the Playground dogfood scene did exactly that and its terrain scan
// reported "no basin found near spawn", which looked like a scan bug and was
// not one.
//
// These tests live in CoreEngine.Tests, which cannot reference
// Assembly-CSharp, so they pin the GEOMETRY the fix derives from rather than
// the MonoBehaviour. That is the part that would silently drift again.
using NUnit.Framework;
using UnityEngine;

public class IslandSpawnTests
{
    private const byte ShippedSizeClass = 1;   // Phase4Bootstrapper.SIZE_CLASS

    [Test]
    public void TheOldHardcodedSpawn_IsOutsideTheIsland_ForTheShippedSizeClass()
    {
        // The regression itself, stated as a fact rather than a memory.
        WorldGenConstants.DeriveIslandGeometry(ShippedSizeClass,
            out float cx, out float cz, out float coastR, out _);

        const float staleSpawnMetres = 140.8f;
        float staleVoxels = staleSpawnMetres * 10f;
        float dx = staleVoxels - cx, dz = staleVoxels - cz;
        float dist = Mathf.Sqrt(dx * dx + dz * dz);

        Assert.Greater(dist, coastR,
            $"the old hardcoded spawn is {dist:F0} voxels from the island centre and the " +
            $"coast radius is {coastR:F0} — if this ever becomes false the geometry moved " +
            "again and the whole rationale below needs rechecking");
    }

    [Test]
    public void DerivedSpawn_LandsWellInsideTheCoastline()
    {
        // What the fix must produce: a point comfortably on land, for every size
        // class the content tables define -- not just the one shipping today.
        for (byte sizeClass = 0; sizeClass <= 1; sizeClass++)
        {
            WorldGenConstants.DeriveIslandGeometry(sizeClass,
                out float cx, out float cz, out float coastR, out _);

            // Mirrors Phase4Bootstrapper.DeriveIslandSpawn: island centre,
            // pulled 12 m south so the island fills the view.
            float sx = cx * 0.1f;
            float sz = cz * 0.1f - 12f;

            float dx = sx * 10f - cx, dz = sz * 10f - cz;
            float dist = Mathf.Sqrt(dx * dx + dz * dz);

            Assert.Less(dist, coastR * 0.5f,
                $"sizeClass {sizeClass}: derived spawn is {dist:F0} voxels from centre, " +
                $"coast radius {coastR:F0} — it should be well inside, not near the shore");
        }
    }

    [Test]
    public void DerivedSpawn_IsAboveAnyTerrainItCouldLandOn()
    {
        // The spawn altitude is a fixed clearance rather than a terrain probe,
        // because nothing is generated when it is computed. It must therefore
        // clear the tallest terrain the generator can produce.
        float spawnY = (WorldGenConstants.MAX_TERRAIN_HEIGHT + 20) * 0.1f;
        Assert.Greater(spawnY, WorldGenConstants.MAX_TERRAIN_HEIGHT * 0.1f,
            "spawn altitude must clear MAX_TERRAIN_HEIGHT");
        Assert.Less(spawnY, EngineConfig.MIRROR_CEILING_METRES,
            "spawn altitude must stay under the GPU mirror's camera ceiling " +
            "(EngineConfig.MIRROR_CHUNKS_Y), or every ray starts out of bounds " +
            "and the screen goes black");
    }
}
