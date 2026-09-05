// Assets/CoreEngine/Tests/AboveWorldCeilingTests.cs
//
// THE DOMAIN OF §9.4's FAIL-CLOSED RULE, pinned.
//
// The bug these exist to catch: in fly mode at height, pressing Tab to switch
// back to walking left the player hanging. They did not fall, could not move,
// and nothing was logged. It was not the motor -- gravity and collision were
// provably fine a few metres lower.
//
// The cause was VoxelCollision rule 1 applied outside its domain. StreamManager
// .RebuildPendingSet only ever admits cy in [0, MAX_GENERATED_CHUNK_Y], so every
// chunk above the ceiling is permanently non-resident -- by construction, not by
// timing, and no amount of waiting streams one in. Rule 1 read that as "unknown,
// so assume rock", which wrapped the body in phantom solid on all six sides.
//
// WHY FOUR TESTS AND NOT ONE. "Sky is passable" alone is satisfied by deleting
// rule 1 outright, which would reintroduce the §9.4 bug it exists to prevent --
// a body walking into, and falling through, world that has merely not streamed
// in yet. So the two permissive cases are stated WITH the two fail-closed
// controls that bound them, and a mutation that breaks either direction fails
// here.

using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;
using VoxelEngine.Streaming;

public class AboveWorldCeilingTests
{
    private sealed class FakeWorld : IWorldQuery
    {
        public readonly HashSet<int3> Solid = new HashSet<int3>();
        public byte GetVoxel(int3 v) => Solid.Contains(v) ? Materials.Stone : Materials.Air;

        public void Floor(int y)
        {
            for (int z = -200; z <= 200; z++)
            for (int x = -200; x <= 200; x++)
                Solid.Add(new int3(x, y, z));
        }
    }

    /// Models the REAL streamer rather than a convenient stub: exactly the chunk
    /// layers RebuildPendingSet admits are resident, and nothing else ever
    /// becomes resident no matter how long you wait.
    private sealed class GeneratedLayersOnly : IVoxelResidency
    {
        public bool IsResident(int3 c)
            => c.y >= 0 && c.y <= StreamManager.MAX_GENERATED_CHUNK_Y;
    }

    private static PlayerConfig Cfg() => new PlayerConfig
    {
        accelerationMps2 = 55f,
        maxSpeedMps = 5.2f,
        jumpImpulseMps = 6.2f,
        airControl = 0.35f,
        frictionMps2 = 60f,
        stepHeightVoxels = 3,
        coyoteTimeSeconds = 0.12f,
        gravityMps2 = 22f,
        terminalSpeedMps = 55f,
        bodyWidthM = 0.6f,
        bodyHeightM = 1.8f,
        maxSubstepVoxels = 0.5f,
    };

    private const float Dt = 1f / 60f;

    /// Well above the generated layer, and asserted to be so rather than
    /// assumed -- if MAX_GENERATED_CHUNK_Y ever grows, this height must grow
    /// with it or these tests quietly stop testing anything.
    private const float SkyM = 300f;

    [Test]
    public void TheTestHeightIsGenuinelyAboveTheGenerationCeiling()
    {
        int3 c = CoordMath.VoxelToChunk(CoordMath.WorldToVoxel(new float3(0f, SkyM, 0f)));
        Assert.Greater(c.y, StreamManager.MAX_GENERATED_CHUNK_Y,
            $"chunk y {c.y} must be above the ceiling for these tests to mean anything");
        Assert.IsFalse(new GeneratedLayersOnly().IsResident(c),
            "and it must be permanently non-resident");
    }

    // =====================================================================
    // Permissive: sky the generator can never fill is not solid
    // =====================================================================

    [Test]
    public void HighInPermanentlyNonResidentSky_ThePlayerFalls()
    {
        // THE REPORTED SYMPTOM, as an assertion. Before the fix this fell 0.00 m.
        var w = new FakeWorld();
        w.Floor(100);
        var m = new PlayerMotor(w, new GeneratedLayersOnly(), Cfg());
        m.Teleport(new float3(0f, SkyM, 0f));

        float y0 = m.PositionM.y;
        for (int i = 0; i < 120; i++) m.Step(Dt, float2.zero, false);   // 2 seconds

        Assert.Greater(y0 - m.PositionM.y, 10f,
            $"the player must fall out of the sky (fell {y0 - m.PositionM.y:F2} m in 2 s); " +
            "0 m means empty sky is being treated as solid again");
    }

    [Test]
    public void AndKeepsFallingUntilItLandsOnRealTerrain()
    {
        // Falling at all is not enough -- it must arrive, through every chunk
        // layer between the ceiling and the ground, and stop on the floor.
        var w = new FakeWorld();
        w.Floor(100);
        var m = new PlayerMotor(w, new GeneratedLayersOnly(), Cfg());
        m.Teleport(new float3(0f, SkyM, 0f));

        for (int i = 0; i < 900 && !m.Grounded; i++) m.Step(Dt, float2.zero, false);
        for (int i = 0; i < 30; i++) m.Step(Dt, float2.zero, false);

        Assert.IsTrue(m.Grounded, "the fall must end on the floor, not in mid-air");
        Assert.AreEqual(10.1f, m.PositionM.y, 0.02f,
            "voxel row y=100 puts its top face at 10.1 m");
    }

    [Test]
    public void ResolveSpawnSucceedsInTheSky_RatherThanFailingSilently()
    {
        // The second half of the symptom: the spawn resolver reported failure
        // through a bool nobody was showing, so the player simply stayed put
        // with no explanation anywhere.
        var w = new FakeWorld();
        w.Floor(100);
        var m = new PlayerMotor(w, new GeneratedLayersOnly(), Cfg());
        m.Teleport(new float3(0f, SkyM, 0f));

        Assert.IsTrue(m.ResolveSpawn(), "open sky is a perfectly good spawn point");
        Assert.AreEqual(SkyM, m.PositionM.y, 0.001f,
            "and it must not need to lift the body at all to find one");
    }

    // =====================================================================
    // THE CONTROLS. These bound the fix; without them "sky is passable" is
    // satisfied by deleting rule 1 and reintroducing the §9.4 bug.
    // =====================================================================

    [Test]
    public void AChunkNotYetStreamedAtGroundLevel_STILL_Blocks()
    {
        // §9.4's actual case: inside the generated band, non-resident means
        // "not loaded YET" and really might be rock. This must keep failing
        // closed, or a body walks into and falls through unstreamed world.
        var w = new FakeWorld();
        var missing = new NotResident();
        int3 v = new int3(5, 40, 5);                    // chunk (0,0,0), inside the band

        Assert.AreEqual(0, CoordMath.VoxelToChunk(v).y, "this case must be at ground level");
        Assert.IsTrue(VoxelCollision.IsBlocking(w, missing, v),
            "a non-resident chunk inside the generated band must still block");
        Assert.IsFalse(VoxelCollision.AboveGeneratedContent(v),
            "and it is not above the ceiling, which is why");
    }

    [Test]
    public void BelowTheBottomOfTheWorld_STILL_Blocks()
    {
        // The deliberate asymmetry. Above the ceiling is sky the generator
        // guarantees is empty; below cy=0 is off the bottom of a world with no
        // floor, where letting a body through means falling forever.
        var w = new FakeWorld();
        int3 v = new int3(5, -40, 5);

        Assert.Less(CoordMath.VoxelToChunk(v).y, 0, "this case must be below the world");
        Assert.IsTrue(VoxelCollision.IsBlocking(w, new NotResident(), v),
            "below the world must stay fail-closed");
        Assert.IsFalse(VoxelCollision.AboveGeneratedContent(v),
            "'above the ceiling' must not be true for coordinates below the world");
    }

    private sealed class NotResident : IVoxelResidency
    {
        public bool IsResident(int3 c) => false;
    }

    [Test]
    public void SolidTerrainAboveTheCeilingWouldStillBlock_IfItCouldExist()
    {
        // The fix must key on RESIDENCY, not on altitude. If a chunk above the
        // ceiling ever does become resident and does hold rock, that rock is
        // still solid -- the exemption is for the non-resident case only.
        var w = new FakeWorld();
        int3 v = CoordMath.WorldToVoxel(new float3(0f, SkyM, 0f));
        w.Solid.Add(v);

        Assert.IsTrue(VoxelCollision.IsBlocking(w, new AllResident(), v),
            "resident rock is solid at any altitude");
    }

    private sealed class AllResident : IVoxelResidency
    {
        public bool IsResident(int3 c) => true;
    }
}
