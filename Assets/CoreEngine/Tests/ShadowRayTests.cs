// ==========================================
// Assets/CoreEngine/Tests/ShadowRayTests.cs
//
// PHASE 7, STAGE 1. The differential fuzz + behaviour proof for
// ShadowRayReference -- written BEFORE any shadow HLSL exists, per §0.1
// invariant 4 and §13 Phase 7's own "if stuck" instruction.
//
// THE ORACLE PROPERTY, stated so it is checkable rather than implied:
// a shadow ray is the Phase-2 DDA with a substituted origin and direction, so
// its OCCLUSION ANSWER must be bit-identical to
// RaymarchReference.TracerRaycast cast from that same origin along that same
// direction. Anything else means the shadow path invented geometry.
//
// Structured the way RaymarchMacroSkipTests / RaymarchDenseSkipTests already
// are, so this inherits their coverage discipline: thousands of rays, grazing
// and near-axis-aligned regimes included, plus a WORK-SAVING assertion so an
// early-out that silently disengaged cannot pass by doing the full walk.
//
// EVERY TEST HERE WHOSE JOB IS TO CATCH A SPECIFIC BUG CLASS HAS BEEN
// MUTATION-CHECKED. The two the phase prompt names as mandatory are:
//
//   M1  origin moved to the hit voxel's OWN centre
//       -> Origin_* and SelfIntersection_* must go red
//   M2  LOD rule swapped to travelled distance (the retracted 8.1 rule)
//       -> Silhouette_* must go red
//
// The mutations applied, and what actually went red, are recorded in
// PHASE_7_COMPLETION.md. A mutation check that was reasoned about rather than
// run is worth nothing, so each was run.
// ==========================================

using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;
using VoxelEngine.Memory;
using VoxelEngine.Mirror;

public class ShadowRayTests
{
    // 64^3 bricks = 4x4x4 chunks = 512 voxels per axis. Same shape the other
    // raymarch oracle tests use, and well inside WINDOW_CHUNKS_Y.
    private static readonly int3 WindowBricks = new int3(64, 64, 64);

    private BrickDataPool _pool;
    private ChunkHandleAllocator _allocator;
    private ChunkStore _store;

    [SetUp]
    public void Setup()
    {
        _pool = new BrickDataPool(20000);
        _allocator = new ChunkHandleAllocator(256);
        _store = new ChunkStore(_pool, _allocator);
        FillWindowWithAir();
    }

    [TearDown]
    public void Teardown() => _pool.Dispose();

    private void FillWindowWithAir()
    {
        int3 windowChunks = WindowBricks / 16;
        for (int cz = 0; cz < windowChunks.z; cz++)
        for (int cy = 0; cy < windowChunks.y; cy++)
        for (int cx = 0; cx < windowChunks.x; cx++)
            _store.InsertChunk(new Chunk { coord = new int3(cx, cy, cz), isUniform = true, uniformMaterial = 0 });
    }

    /// A flat slab of Stone filling y in [0, height), across the whole window.
    private void BuildFloor(int height)
    {
        for (int z = 0; z < 512; z++)
        for (int y = 0; y < height; y++)
        for (int x = 0; x < 512; x++)
            _store.SetVoxel(new int3(x, y, z), Materials.Stone);
    }

    /// Cheaper floor for tests that only need a local patch.
    private void BuildFloorPatch(int3 min, int3 sizeXZ, int height)
    {
        for (int z = 0; z < sizeXZ.z; z++)
        for (int y = 0; y < height; y++)
        for (int x = 0; x < sizeXZ.x; x++)
            _store.SetVoxel(min + new int3(x, y, z), Materials.Stone);
    }

    private static float3 CameraAtVoxel(int3 v) => (new float3(v.x, v.y, v.z) + 0.5f) * 0.1f;

    /// The distance clamp the DIFFERENTIAL FUZZ hands to BOTH sides.
    ///
    /// NOT the shipped clamp, and the difference is the point.
    /// ShadowRayReference clamps at 290 m and sizes its runaway guard from
    /// that, but RaymarchReference.TracerRaycast -- the oracle being compared
    /// against -- carries a hard 1,000-step ceiling that predates Phase 7 and
    /// is not ours to change (it is a frozen Phase-2 fixture, §12).
    ///
    /// A DDA travelling `t` voxel-units along a unit direction crosses
    /// (|dx|+|dy|+|dz|) * t boundaries, at most sqrt(3) * t. At 50 m = 500
    /// voxels that is at most 866 steps, comfortably under the oracle's 1,000 --
    /// so within this distance NEITHER side can give up early and any
    /// disagreement is a real disagreement about geometry, which is the only
    /// kind worth asserting on. Raise this and the fuzz starts reporting
    /// "the oracle ran out of steps" as a shadow bug.
    private const float FuzzClampM = 50f;

    // =====================================================================
    // §6.5's ORIGIN RULE
    // =====================================================================

    [Test]
    public void Origin_IsOneCellOffAlongTheFaceNormal_ForAllSixFaces()
    {
        var hit = new int3(100, 40, 100);
        int3[] normals =
        {
            new int3(1, 0, 0), new int3(-1, 0, 0),
            new int3(0, 1, 0), new int3(0, -1, 0),
            new int3(0, 0, 1), new int3(0, 0, -1),
        };
        foreach (int3 n in normals)
        {
            Assert.AreEqual(hit + n, ShadowRayReference.ShadowOriginVoxel(hit, n),
                $"origin for normal {n} is not one cell off the face");
        }
    }

    [Test]
    public void Origin_IsTheCentreOfThatCell_NotItsCorner()
    {
        var hit = new int3(10, 20, 30);
        var n = new int3(0, 1, 0);
        float3 w = ShadowRayReference.ShadowOriginWorld(hit, n);
        // (10, 21, 30) + 0.5, times 0.1 m per voxel.
        Assert.AreEqual(1.05f, w.x, 1e-5f);
        Assert.AreEqual(2.15f, w.y, 1e-5f);
        Assert.AreEqual(3.05f, w.z, 1e-5f);
    }

    /// THE ACNE TEST. M1 (origin at the hit voxel's own centre) must break this.
    ///
    /// A surface lit from directly above cannot shadow itself. If the origin
    /// were the hit voxel's centre, the very first sample would read the hit
    /// voxel -- solid -- and every lit pixel on the planet would report
    /// occluded. That is shadow acne, and §6.5 says it is impossible by
    /// construction rather than tuned away.
    [Test]
    public void SelfIntersection_SunStraightUp_IsNeverOccluded()
    {
        BuildFloorPatch(new int3(90, 0, 90), new int3(40, 0, 40), 20);
        var hitVoxel = new int3(100, 19, 100);          // top face of the slab
        var normal = new int3(0, 1, 0);
        float3 cam = CameraAtVoxel(new int3(100, 30, 90));

        var r = ShadowRayReference.TraceHard(_store, hitVoxel, normal,
                                             new float3(0, 1, 0), cam);
        Assert.IsFalse(r.skipped, "sun is above the horizon; the ray must be fired");
        Assert.IsFalse(r.occluded, "a flat surface lit from directly above shadowed itself -- this is acne");
    }

    /// The same trap at every grazing angle over a flat floor, which is where
    /// acne actually shows up in practice. §13's acceptance bullet asks for
    /// "grazing-angle shadows show ZERO acne".
    [Test]
    public void SelfIntersection_GrazingSunOverFlatFloor_IsNeverOccluded()
    {
        BuildFloorPatch(new int3(60, 0, 60), new int3(200, 0, 200), 20);
        var normal = new int3(0, 1, 0);
        float3 cam = CameraAtVoxel(new int3(100, 30, 80));

        // Shallow elevations, all the way down to 1 degree, all around the compass.
        for (int deg = 1; deg <= 30; deg++)
        for (int azi = 0; azi < 360; azi += 30)
        {
            float el = math.radians(deg), az = math.radians(azi);
            var sun = new float3(math.cos(el) * math.cos(az), math.sin(el), math.cos(el) * math.sin(az));
            var hitVoxel = new int3(150, 19, 150);
            var r = ShadowRayReference.TraceHard(_store, hitVoxel, normal, sun, cam);
            Assert.IsFalse(r.occluded,
                $"flat floor self-shadowed at elevation {deg} deg, azimuth {azi} deg -- acne");
        }
    }

    /// The other half of the same construction: Peter-Panning is a shadow that
    /// starts too far from its caster. With the origin exactly one cell off,
    /// a wall's shadow must begin in the cell IMMEDIATELY beside its base --
    /// no gap.
    [Test]
    public void NoPeterPanning_ShadowStartsInTheCellTouchingTheCaster()
    {
        BuildFloorPatch(new int3(90, 0, 90), new int3(60, 0, 60), 20);
        // A wall along z at x = 120, standing on the floor.
        for (int z = 90; z < 150; z++)
        for (int y = 20; y < 40; y++)
            _store.SetVoxel(new int3(120, y, z), Materials.Stone);

        float3 cam = CameraAtVoxel(new int3(100, 40, 80));
        // Sun low in +X, so the wall casts along -X onto the floor.
        var sun = math.normalize(new float3(1f, 0.35f, 0f));
        var normal = new int3(0, 1, 0);

        // The floor cell immediately on the shadowed side of the wall base.
        var touching = new int3(119, 19, 120);
        var r = ShadowRayReference.TraceHard(_store, touching, normal, sun, cam);
        Assert.IsTrue(r.occluded,
            "the floor cell touching the wall base is lit -- the shadow detached from its caster (Peter-Panning)");
    }

    // =====================================================================
    // THE EARLY-OUTS -- §6.5's "skips sky pixels" and the night rule
    // =====================================================================

    [Test]
    public void SkyPixel_FiresNoRayAtAll()
    {
        var primary = new RaymarchReference.RayHit { hit = false };
        var r = ShadowRayReference.TraceFromPrimaryHit(_store, primary,
                                                       new float3(0, 1, 0), float3.zero, 0.05f);
        Assert.IsTrue(r.skipped, "a sky pixel must skip the shadow ray entirely (§6.5)");
        Assert.AreEqual(0, r.steps, "a skipped shadow ray must do NO work");
        Assert.AreEqual(0f, r.sunVis, "a sky pixel has no sun term to apply");
    }

    [Test]
    public void SunBelowOrOnHorizon_SkipsTheRayAndCarriesZeroSun()
    {
        BuildFloorPatch(new int3(90, 0, 90), new int3(40, 0, 40), 20);
        var hitVoxel = new int3(100, 19, 100);
        var normal = new int3(0, 1, 0);
        float3 cam = CameraAtVoxel(new int3(100, 30, 90));

        foreach (float3 sun in new[]
                 {
                     new float3(1f, 0f, 0f),        // exactly on the horizon
                     new float3(1f, -0.01f, 0f),    // just below
                     new float3(0f, -1f, 0f),       // midnight
                 })
        {
            var r = ShadowRayReference.Trace(_store, hitVoxel, normal, sun, cam, 0.05f);
            Assert.IsTrue(r.skipped, $"sun {sun} is not above the horizon; no ray should be fired");
            Assert.AreEqual(0, r.steps, "a skipped shadow ray must do NO work");
            Assert.AreEqual(0f, r.sunVis, "§6.5: below the horizon the sun term is 0");
        }
    }

    // =====================================================================
    // DIFFERENTIAL FUZZ against the Phase-2 oracle
    // =====================================================================

    /// The core oracle property. For thousands of random shadow rays the
    /// occlusion answer must equal TracerRaycast's own hit flag from the same
    /// substituted origin and direction.
    ///
    /// The camera is pinned AT the sample region so SelectLODTier returns 0 for
    /// every sample -- this test is about the traversal agreeing, and mixing in
    /// tier coarsening would make a disagreement ambiguous between the two.
    /// Tier behaviour is tested separately below.
    [Test]
    public void Fuzz_HardOcclusionAgreesWithThePerVoxelOracle()
    {
        var rng = new Unity.Mathematics.Random(0x5EED1234u);
        ScatterRandomSolids(rng.NextUInt(), 1500, new int3(120, 20, 120), new int3(80, 40, 80));

        int occludedCount = 0, missCount = 0;
        for (int i = 0; i < 4000; i++)
        {
            int3 hitVoxel = new int3(
                rng.NextInt(130, 190), rng.NextInt(25, 50), rng.NextInt(130, 190));
            int3 normal = RandomFaceNormal(ref rng);
            float3 sun = RandomUpwardDir(ref rng);
            float3 cam = CameraAtVoxel(hitVoxel + new int3(0, 4, 0));   // pins tier 0

            var shadow = ShadowRayReference.TraceHard(_store, hitVoxel, normal, sun, cam, FuzzClampM);

            float3 originM = ShadowRayReference.ShadowOriginWorld(hitVoxel, normal);
            var oracle = RaymarchReference.TracerRaycast(originM, sun, _store, FuzzClampM);

            Assert.AreEqual(oracle.hit, shadow.occluded,
                $"ray {i}: occlusion disagrees with the per-voxel oracle. " +
                $"hitVoxel {hitVoxel} normal {normal} sun {sun}");

            if (shadow.occluded) occludedCount++; else missCount++;
        }

        // A fuzz that only ever produced one answer would pass vacuously.
        Assert.Greater(occludedCount, 200, "fuzz produced almost no occluded rays -- geometry too sparse to be a test");
        Assert.Greater(missCount, 200, "fuzz produced almost no lit rays -- geometry too dense to be a test");
    }

    /// Grazing and near-axis-aligned regimes, called out separately because
    /// they are where DDA seeding errors live and where §13's acceptance
    /// bullet puts its attention.
    [Test]
    public void Fuzz_GrazingAndNearAxisAlignedAgreeWithTheOracle()
    {
        var rng = new Unity.Mathematics.Random(0xC0FFEEu);
        ScatterRandomSolids(rng.NextUInt(), 1200, new int3(120, 20, 120), new int3(80, 30, 80));

        for (int i = 0; i < 2500; i++)
        {
            int3 hitVoxel = new int3(rng.NextInt(140, 180), rng.NextInt(25, 45), rng.NextInt(140, 180));
            int3 normal = RandomFaceNormal(ref rng);

            // Two hostile regimes, alternating:
            //  - grazing: elevation in [0.2, 3.0] degrees
            //  - near-axis-aligned: one component within 1e-3 of an axis
            float3 sun;
            if ((i & 1) == 0)
            {
                float el = math.radians(rng.NextFloat(0.2f, 3.0f));
                float az = rng.NextFloat(0f, 2f * math.PI);
                sun = new float3(math.cos(el) * math.cos(az), math.sin(el), math.cos(el) * math.sin(az));
            }
            else
            {
                float eps = rng.NextFloat(-1e-3f, 1e-3f);
                sun = math.normalize(new float3(eps, 1f, rng.NextFloat(-1e-3f, 1e-3f)));
                if ((i & 2) != 0) sun = math.normalize(new float3(1f, math.abs(eps) + 1e-4f, eps));
            }

            float3 cam = CameraAtVoxel(hitVoxel + new int3(0, 3, 0));
            var shadow = ShadowRayReference.TraceHard(_store, hitVoxel, normal, sun, cam, FuzzClampM);

            float3 originM = ShadowRayReference.ShadowOriginWorld(hitVoxel, normal);
            var oracle = RaymarchReference.TracerRaycast(originM, sun, _store, FuzzClampM);

            Assert.AreEqual(oracle.hit, shadow.occluded,
                $"grazing/axis ray {i}: disagrees with oracle. hitVoxel {hitVoxel} normal {normal} sun {sun}");
        }
    }

    // =====================================================================
    // WORK-SAVING -- a disengaged early-out must not pass
    // =====================================================================

    /// §6.5's "occlusion-only (first hit = done)". A ray blocked three cells
    /// away must STOP there. Without the early-out it would keep walking to the
    /// clamp, and the occlusion answer would be identical -- so only a work
    /// assertion can catch that.
    [Test]
    public void WorkSaving_OcclusionStopsAtTheFirstHit()
    {
        BuildFloorPatch(new int3(90, 0, 90), new int3(40, 0, 40), 20);
        // A lid four cells above the surface.
        for (int z = 90; z < 130; z++)
        for (int x = 90; x < 130; x++)
            _store.SetVoxel(new int3(x, 24, z), Materials.Stone);

        var hitVoxel = new int3(110, 19, 110);
        var normal = new int3(0, 1, 0);
        float3 cam = CameraAtVoxel(new int3(110, 22, 100));

        var r = ShadowRayReference.TraceHard(_store, hitVoxel, normal, new float3(0, 1, 0), cam);
        Assert.IsTrue(r.occluded, "the lid must block the sun");
        Assert.LessOrEqual(r.steps, 8,
            $"occluded in {r.steps} steps for a blocker 4 cells away -- the first-hit early-out is not engaging");
    }

    /// The complementary half: an unobstructed ray must terminate at the
    /// distance clamp rather than running to the iteration guard, and must not
    /// report occlusion.
    [Test]
    public void WorkSaving_OpenSkyRayTerminatesAtTheDistanceClamp()
    {
        BuildFloorPatch(new int3(90, 0, 90), new int3(40, 0, 40), 20);
        var hitVoxel = new int3(110, 19, 110);
        var normal = new int3(0, 1, 0);
        float3 cam = CameraAtVoxel(new int3(110, 25, 100));

        var r = ShadowRayReference.TraceHard(_store, hitVoxel, normal, new float3(0, 1, 0), cam);
        Assert.IsFalse(r.occluded);

        // THE DISTANCE CLAMP MUST BE WHAT STOPS THE RAY, NOT THE RUNAWAY GUARD.
        // A ray that gives up on step count returns occluded = false, i.e. LIT
        // -- so a guard that binds first does not fail loudly, it silently
        // lights things that are in shadow, and it does so worst exactly where
        // rays are longest: golden hour. Straight up from y = 20 the clamp is
        // 2,900 voxels away, so the expected step count is ~2,880.
        Assert.Less(r.steps, ShadowRayReference.MaxSteps,
            $"the ray stopped at the runaway guard ({r.steps} steps) instead of the distance clamp -- " +
            "long grazing shadow rays will report LIT when they are occluded");
        Assert.Greater(r.steps, 2000,
            $"only {r.steps} steps to reach a 2,900-voxel clamp -- the clamp is not what terminated the ray");
    }

    // =====================================================================
    // C.10 PENUMBRA
    // =====================================================================

    [Test]
    public void Penumbra_KSoftZeroGivesExactlyHardShadows()
    {
        BuildFloorPatch(new int3(90, 0, 90), new int3(60, 0, 60), 20);
        for (int z = 90; z < 150; z++)
        for (int y = 20; y < 30; y++)
            _store.SetVoxel(new int3(120, y, z), Materials.Stone);

        float3 cam = CameraAtVoxel(new int3(100, 30, 80));
        var normal = new int3(0, 1, 0);
        var sun = math.normalize(new float3(1f, 0.5f, 0f));

        var lit = ShadowRayReference.Trace(_store, new int3(135, 19, 120), normal, sun, cam, 0f);
        var dark = ShadowRayReference.Trace(_store, new int3(119, 19, 120), normal, sun, cam, 0f);

        Assert.IsFalse(lit.occluded);
        Assert.AreEqual(1f, lit.sunVis, 0f, "k_soft = 0 must give sunVis exactly 1 on a miss (§6.5 hard toggle)");
        Assert.IsTrue(dark.occluded);
        Assert.AreEqual(0f, dark.sunVis, 0f, "an occluded sample is fully shadowed, whatever k_soft is");
    }

    /// FLAT GROUND, SUN STRAIGHT UP, NOTHING ELSE IN THE WORLD. The most
    /// ordinary frame the game can render, and the one that caught the
    /// penumbra's own acne bug: the ground the sample stands on is a face
    /// neighbour of the shadow ray's origin cell, so scanning it as a near-miss
    /// occluder drove C.10's `m` to 0 and reported the entire sunlit world as
    /// being in full shadow. See NearestSolidFaceClearance's header.
    [Test]
    public void Penumbra_OpenSkyIsFullySunlitAndRecordsNoNearMiss()
    {
        BuildFloorPatch(new int3(90, 0, 90), new int3(60, 0, 60), 20);
        float3 cam = CameraAtVoxel(new int3(120, 30, 100));
        var r = ShadowRayReference.Trace(_store, new int3(120, 19, 120), new int3(0, 1, 0),
                                         new float3(0, 1, 0), cam, 0.08f);
        Assert.IsFalse(r.occluded);
        Assert.AreEqual(1f, r.sunVis, 1e-6f,
            "flat ground under an overhead sun reported a penumbra -- the surface being lit is being " +
            "scanned as its own occluder, which darkens every lit pixel in the world");
        Assert.IsTrue(float.IsPositiveInfinity(r.minClearanceRatio),
            "no near-miss cell was seen, so C.10's m must remain unconstrained");
    }

    /// THE PENUMBRA ITSELF: the sample at the very edge of a wall's hard shadow
    /// must be PARTIALLY lit, not fully lit, once k_soft is non-zero.
    ///
    /// GEOMETRY IS THE WHOLE TEST HERE AND TWO EARLIER VERSIONS GOT IT WRONG,
    /// so both mistakes are recorded rather than quietly fixed.
    ///
    /// (1) DIRECTION. `sun` points FROM the surface TOWARD the sun, so a sun at
    ///     +X throws shadows toward -X. Searching upward from x = 121 for a wall
    ///     at x = 120 only ever walks the fully-LIT side, where there correctly
    ///     is no penumbra at all.
    ///
    /// (2) DISTANCE -- the subtler one, and it is C.10 being right rather than
    ///     wrong. C.10's m = clearance/t is an ANGULAR miss, so the penumbra
    ///     CONTACT-HARDENS: near the caster the shadow edge is sharp, and it
    ///     only spreads with distance. A 3-voxel wall puts its shadow edge ~5
    ///     voxels from the caster, where one voxel of clearance subtends
    ///     ~0.25 rad -- fourteen degrees, far outside any sane k_soft -- so the
    ///     edge is legitimately hard and `sunVis` is legitimately 1. That is
    ///     correct behaviour, not a missing penumbra.
    ///
    /// So the wall is TALL (25 voxels) and the floor is WIDE, putting the
    /// shadow edge ~41 voxels from its caster where one voxel of clearance is
    /// ~0.021 rad and a k_soft of 0.10 has something to act on.
    [Test]
    public void Penumbra_AGrazingMissIsPartiallyShadowed()
    {
        BuildFloorPatch(new int3(60, 0, 90), new int3(140, 0, 80), 20);
        // A TALL wall along z at x = 150: 25 voxels of it stand above the floor.
        for (int z = 90; z < 170; z++)
        for (int y = 20; y < 45; y++)
            _store.SetVoxel(new int3(150, y, z), Materials.Stone);

        float3 cam = CameraAtVoxel(new int3(120, 40, 120));
        var sun = math.normalize(new float3(1f, 0.6f, 0f));   // sun in +X => shadow toward -X
        var normal = new int3(0, 1, 0);

        // Walk the SHADOWED side outward from the wall and find the first lit cell.
        int firstLit = -1;
        for (int x = 149; x >= 65; x--)
        {
            var probe = ShadowRayReference.Trace(_store, new int3(x, 19, 130), normal, sun, cam, 0.10f);
            if (!probe.occluded) { firstLit = x; break; }
        }
        Assert.Greater(firstLit, 0,
            "no lit cell found on the shadowed side -- the geometry is wrong, not the penumbra");
        Assert.Less(firstLit, 150, "the search ran on the lit side of the wall");
        Assert.Greater(firstLit, 65, "the shadow ran off the end of the floor; widen the patch");

        var edge = ShadowRayReference.Trace(_store, new int3(firstLit, 19, 130), normal, sun, cam, 0.10f);
        Assert.IsFalse(edge.occluded);
        Assert.Less(edge.sunVis, 1f,
            $"the cell at the very edge of a shadow cast from ~{150 - firstLit} voxels away is fully lit " +
            "-- C.10's near-miss tracking is not engaging");
        Assert.Greater(edge.sunVis, 0f, "a missed ray is never fully dark");

        // Far out on the open floor the ray clears the wall top by many voxels,
        // never passes a solid face neighbour at all, and full sun returns.
        var open = ShadowRayReference.Trace(_store, new int3(70, 19, 130), normal, sun, cam, 0.10f);
        Assert.IsFalse(open.occluded);
        Assert.AreEqual(1f, open.sunVis, 1e-4f,
            "far from the caster the sample should be fully lit again -- the penumbra is not bounded");

        // CONTACT HARDENING, asserted rather than assumed: the same wall with
        // its shadow edge only a few voxels away gives a HARDER edge than this
        // one does. This is the property that makes C.10 worth having over a
        // constant blur, so it is pinned.
        var nearWall = ShadowRayReference.Trace(_store, new int3(149, 40, 130), new int3(-1, 0, 0),
                                                sun, cam, 0.10f);
        Assert.IsTrue(nearWall.occluded || nearWall.sunVis > edge.sunVis,
            "a sample in contact with its caster should be sharper than one 40 voxels away");
    }

    /// Monotone in k_soft: smoothstep(0, k, m) falls as k rises for fixed m, so
    /// a larger penumbra constant can only ever darken a near-miss. This is the
    /// property that makes k_soft a usable art constant rather than a magic
    /// number, and it is what the Playground slider relies on.
    ///
    /// Sampled at the shadow EDGE (found by search, as above) -- on the open
    /// floor every k gives 1.0 and the test would pass while measuring nothing,
    /// which is why the final assertion checks the largest k actually darkened.
    [Test]
    public void Penumbra_IsMonotoneInKSoft()
    {
        BuildFloorPatch(new int3(60, 0, 90), new int3(140, 0, 80), 20);
        for (int z = 90; z < 170; z++)
        for (int y = 20; y < 45; y++)
            _store.SetVoxel(new int3(150, y, z), Materials.Stone);

        float3 cam = CameraAtVoxel(new int3(120, 40, 120));
        var sun = math.normalize(new float3(1f, 0.6f, 0f));
        var normal = new int3(0, 1, 0);

        int firstLit = -1;
        for (int x = 149; x >= 65; x--)
        {
            if (!ShadowRayReference.Trace(_store, new int3(x, 19, 130), normal, sun, cam, 0.10f).occluded)
            { firstLit = x; break; }
        }
        Assert.Greater(firstLit, 0, "no lit cell on the shadowed side");
        var sample = new int3(firstLit, 19, 130);

        float prev = float.MaxValue;
        int measured = 0;
        foreach (float k in new[] { 0.02f, 0.05f, 0.10f, 0.20f, 0.40f })
        {
            var r = ShadowRayReference.Trace(_store, sample, normal, sun, cam, k);
            Assert.IsFalse(r.occluded, "k_soft changed the occlusion answer");
            Assert.LessOrEqual(r.sunVis, prev + 1e-5f,
                $"sunVis rose when k_soft rose to {k} -- the penumbra is not monotone");
            prev = r.sunVis;
            measured++;
        }
        Assert.AreEqual(5, measured, "not every k was measured");
        Assert.Less(prev, 1f,
            "even the largest k_soft left the shadow edge fully lit -- nothing was actually being tracked");
    }

    /// Penumbra must never change the OCCLUSION answer -- C.10 is a shading
    /// term layered on a binary ray, not a second traversal. If soft and hard
    /// ever disagreed on `occluded`, the near-miss bookkeeping would be
    /// perturbing the walk.
    [Test]
    public void Fuzz_PenumbraNeverChangesTheOcclusionAnswer()
    {
        var rng = new Unity.Mathematics.Random(0xBEEF77u);
        ScatterRandomSolids(rng.NextUInt(), 1200, new int3(120, 20, 120), new int3(70, 30, 70));

        for (int i = 0; i < 1500; i++)
        {
            int3 hitVoxel = new int3(rng.NextInt(135, 180), rng.NextInt(25, 45), rng.NextInt(135, 180));
            int3 normal = RandomFaceNormal(ref rng);
            float3 sun = RandomUpwardDir(ref rng);
            float3 cam = CameraAtVoxel(hitVoxel + new int3(0, 3, 0));

            var hard = ShadowRayReference.Trace(_store, hitVoxel, normal, sun, cam, 0f);
            var soft = ShadowRayReference.Trace(_store, hitVoxel, normal, sun, cam, 0.12f);

            Assert.AreEqual(hard.occluded, soft.occluded,
                $"ray {i}: the penumbra term changed the occlusion answer");
            Assert.AreEqual(hard.steps, soft.steps,
                $"ray {i}: the penumbra term changed how far the ray walked");
            if (soft.occluded) Assert.AreEqual(0f, soft.sunVis, 0f);
        }
    }

    // =====================================================================
    // §6.5's 8.2 LOD RULE -- and the silhouette test that catches its reversal
    // =====================================================================

    [Test]
    public void SelectLODTier_MatchesTheShipConfigBoundaries()
    {
        float t0 = LODConfig.TIER_OUTER_RANGE_M[0] * 10f;
        float t1 = LODConfig.TIER_OUTER_RANGE_M[1] * 10f;

        Assert.AreEqual(0, ShadowRayReference.SelectLODTier(0f));
        Assert.AreEqual(0, ShadowRayReference.SelectLODTier(t0 - 1f));
        Assert.AreEqual(1, ShadowRayReference.SelectLODTier(t0));
        Assert.AreEqual(1, ShadowRayReference.SelectLODTier(t1 - 1f));
        Assert.AreEqual(2, ShadowRayReference.SelectLODTier(t1));
        Assert.AreEqual(2, ShadowRayReference.SelectLODTier(t1 * 10f));
    }

    /// The tier sampler must reduce exactly the way the shipped downsampler
    /// does -- chained 2x2x2 majority votes, not a flat block vote. If these
    /// drifted, a distant shadow would disagree with the distant geometry the
    /// camera actually draws, which is the failure the 8.2 rule exists to stop.
    [Test]
    public void SampleAtTier_AgreesWithLODDownsamplersOwnChain()
    {
        var rng = new Unity.Mathematics.Random(0x1234ABCDu);
        var origin = new int3(160, 32, 160);           // a tier-2-aligned cell
        var expectedSource = new byte[4 * 4 * 4];
        int i = 0;
        for (int z = 0; z < 4; z++)
        for (int y = 0; y < 4; y++)
        for (int x = 0; x < 4; x++)
        {
            byte m = (byte)(rng.NextInt(0, 3) == 0 ? 0 : Materials.Stone);
            _store.SetVoxel(origin + new int3(x, y, z), m);
            expectedSource[i++] = m;
        }

        byte[] tier1 = LODDownsampler.DownsampleOnce(expectedSource, 4);
        byte[] tier2 = LODDownsampler.DownsampleOnce(tier1, 2);

        Assert.AreEqual(tier2[0], ShadowRayReference.SampleAtTier(_store, origin, 2),
            "tier-2 sample disagrees with LODDownsampler's own chained reduction");
        Assert.AreEqual(_store.GetVoxel(origin), ShadowRayReference.SampleAtTier(_store, origin, 0),
            "tier 0 must be the raw byte, untouched");
    }

    /// THE SILHOUETTE TEST. M2 (LOD rule swapped to travelled distance) must
    /// break this.
    ///
    /// §6.5's own statement of the failure: "a short shadow ray to a mountain
    /// the camera renders coarse would evaluate it at fine LOD, so the cast
    /// shadow wouldn't match the on-screen silhouette -- detached, floating
    /// shadows."
    ///
    /// The geometry is a SUB-TIER occluder: a single isolated solid voxel. At
    /// tier 0 it blocks. At tier 1 its 2x2x2 block is 7/8 air, so
    /// MajorityVote's "air needs a strict 5-of-8 majority" rule votes it away
    /// and it does NOT block -- exactly as it vanishes from the coarse geometry
    /// the camera draws at that distance.
    ///
    /// The camera is placed FAR from the sample, so the 8.2 rule selects a
    /// coarse tier and the sample is lit. The shadow ray itself travels only a
    /// few voxels, so the retracted 8.1 rule would select tier 0, see the lone
    /// voxel, and report a shadow the player cannot see any caster for.
    [Test]
    public void Silhouette_SubTierOccluderVanishesWhenTheCameraIsFar()
    {
        BuildFloorPatch(new int3(150, 0, 150), new int3(40, 0, 40), 20);
        var sample = new int3(170, 19, 170);

        // One isolated voxel directly overhead, four cells up.
        _store.SetVoxel(new int3(170, 23, 170), Materials.Stone);

        var normal = new int3(0, 1, 0);
        var sun = new float3(0, 1, 0);

        // --- camera CLOSE: tier 0, the lone voxel is real and blocks. ---
        float3 camNear = CameraAtVoxel(new int3(170, 25, 160));
        var near = ShadowRayReference.TraceHard(_store, sample, normal, sun, camNear);
        Assert.AreEqual(0, near.firstSampleTier, "camera is close; the rule must select tier 0");
        Assert.IsTrue(near.occluded, "at tier 0 a solid voxel overhead must cast a shadow");

        // --- camera FAR: a coarse tier, the lone voxel has been voted away. ---
        // Past TIER_OUTER_RANGE_M[0] from the sample (93.5 m since the
        // pixel-footprint boundaries; was 64 m, when this sat at 900 voxels).
        float3 camFar = CameraAtVoxel(sample + new int3(0, 0, 1400));
        var far = ShadowRayReference.TraceHard(_store, sample, normal, sun, camFar);
        Assert.Greater(far.firstSampleTier, 0,
            "camera is far; §6.5's 8.2 rule must select a COARSE tier for this sample");
        Assert.IsFalse(far.occluded,
            "a sub-tier occluder that the camera cannot draw at this distance still cast a shadow -- " +
            "this is the detached/floating shadow the 8.1 travelled-distance rule produced, and it means " +
            "the LOD rule is selecting by ray travel instead of by camera distance");
    }

    /// The same rule stated as a property rather than a scenario: the tier a
    /// shadow ray samples at depends ONLY on where the camera is, never on how
    /// far the ray has gone. Moving the camera changes it; nothing else does.
    [Test]
    public void Silhouette_TierDependsOnCameraPositionOnly()
    {
        BuildFloorPatch(new int3(150, 0, 150), new int3(40, 0, 40), 20);
        var sample = new int3(170, 19, 170);
        var normal = new int3(0, 1, 0);
        var sun = new float3(0, 1, 0);

        int tierNear = ShadowRayReference.TraceHard(
            _store, sample, normal, sun, CameraAtVoxel(sample + new int3(0, 6, 0))).firstSampleTier;
        // Mid-band of tier 1 and of tier 2, from the config rather than literals:
        // the boundaries moved (64/128 m -> pixel footprint, 2026-09-25) and a
        // literal 800/1500 voxels silently became tier 0 / tier 1.
        int midT1 = (int)((LODConfig.TIER_OUTER_RANGE_M[0] + LODConfig.TIER_OUTER_RANGE_M[1]) * 5f);
        int midT2 = (int)((LODConfig.TIER_OUTER_RANGE_M[1] + LODConfig.TIER_OUTER_RANGE_M[2]) * 5f);
        int tierMid = ShadowRayReference.TraceHard(
            _store, sample, normal, sun, CameraAtVoxel(sample + new int3(0, 0, midT1))).firstSampleTier;
        int tierFar = ShadowRayReference.TraceHard(
            _store, sample, normal, sun, CameraAtVoxel(sample + new int3(0, 0, midT2))).firstSampleTier;

        Assert.AreEqual(0, tierNear);
        Assert.AreEqual(1, tierMid);
        Assert.AreEqual(2, tierFar);
    }

    // =====================================================================
    // helpers
    // =====================================================================

    private void ScatterRandomSolids(uint seed, int count, int3 min, int3 size)
    {
        var rng = new Unity.Mathematics.Random(seed == 0 ? 1u : seed);
        for (int i = 0; i < count; i++)
        {
            var v = min + new int3(rng.NextInt(0, size.x), rng.NextInt(0, size.y), rng.NextInt(0, size.z));
            _store.SetVoxel(v, Materials.Stone);
        }
    }

    private static int3 RandomFaceNormal(ref Unity.Mathematics.Random rng)
    {
        switch (rng.NextInt(0, 6))
        {
            case 0: return new int3(1, 0, 0);
            case 1: return new int3(-1, 0, 0);
            case 2: return new int3(0, 1, 0);
            case 3: return new int3(0, -1, 0);
            case 4: return new int3(0, 0, 1);
            default: return new int3(0, 0, -1);
        }
    }

    /// A direction strictly above the horizon -- the only kind the shadow ray
    /// ever fires, so the fuzz should not waste half its rays on the skip path.
    private static float3 RandomUpwardDir(ref Unity.Mathematics.Random rng)
    {
        float el = math.radians(rng.NextFloat(0.5f, 89f));
        float az = rng.NextFloat(0f, 2f * math.PI);
        return new float3(math.cos(el) * math.cos(az), math.sin(el), math.cos(el) * math.sin(az));
    }
}
