// ==========================================
// Assets/CoreEngine/Rendering/ShadowRayReference.cs
//
// PHASE 7, STAGE 1. The CPU shadow oracle -- §0.1 invariant 4 ("prove on the
// CPU before the GPU") and §0.3's Sibling Pattern, applied BEFORE a single line
// of shadow HLSL exists.
//
// §13 Phase 7's own "if stuck" note names the method and this file follows it
// literally:
//
//     "the shadow ray is the Phase-2 DDA with a new origin + early-out -- if it
//      misbehaves, compare against the Phase-2 RaymarchReference with the
//      shadow origin/direction substituted."
//
// So the traversal here is NOT a second DDA. Occlusion is decided by calling
// RaymarchReference.TracerRaycast -- the proven per-voxel oracle every other
// tracer in this project is fuzzed against -- with a substituted origin and
// direction. The only genuinely new logic in this file is:
//
//   1. WHERE the ray starts          (§6.5's origin rule)
//   2. WHICH tier a sample reads     (§6.5's 8.2 LOD rule)
//   3. THE PENUMBRA ACCUMULATOR      (C.10)
//
// and each of those has its own test plus a mutation check.
//
// ---------------------------------------------------------------------------
// 1. THE ORIGIN RULE (§6.5) -- AND WHY THERE IS NO BIAS PARAMETER
// ---------------------------------------------------------------------------
// §6.5: "Origin: the center of the first voxel ADJACENT to the hit face (start
// one cell off the surface along the face normal). Self-intersection is
// impossible by construction -- no bias to tune. Acne and Peter-Panning cease
// to be a possible bug category, because there is no resolution mismatch
// between what renders and what shadows: it is the same data."
//
// §13's failure-signature list says the same thing from the other end: "Shadow
// acne/Peter-Panning -> shadow-ray origin isn't starting one cell off the hit
// face; FIX THE ORIGIN, DON'T ADD A BIAS HACK."
//
// ShadowOriginVoxel() and ShadowOriginWorld() therefore take (hitVoxel,
// hitNormal) and NOTHING ELSE. There is deliberately no epsilon argument, no
// optional offset, and no overload that accepts one -- a later caller cannot
// add a bias without editing this file and confronting this comment, which is
// the entire point. A bias term here would be a defect, not a tuning knob.
//
// ---------------------------------------------------------------------------
// 2. THE 8.2 LOD RULE -- CAMERA DISTANCE, NOT TRAVELLED DISTANCE
// ---------------------------------------------------------------------------
// §6.5, and this is an amendment that REVERSED an earlier rule, so it is worth
// quoting rather than paraphrasing:
//
//     "the shadow ray selects LOD by CAMERA DISTANCE TO THE CURRENT SAMPLE
//      POINT, NOT by the shadow ray's own traveled distance. The 8.1 rule
//      (coarsen by traveled distance) was wrong: a short shadow ray to a
//      mountain the camera renders coarse would evaluate it at fine LOD, so the
//      cast shadow wouldn't match the on-screen silhouette -- detached,
//      floating shadows. Sampling by camera-distance guarantees the shadow
//      evaluates the SAME geometry the primary ray drew."
//
// A LATENT TRAP THIS FILE EXISTS TO CLOSE. Raymarch.compute has no
// SelectLODTier function at all: tier selection is four inline lines in CSMain
// comparing `currentDist` against _TierOuterRangeVoxels. For a PRIMARY ray that
// is correct -- but only by accident of the ray's origin, because a primary
// ray starts at the camera, so its travelled distance IS its camera distance.
// A shadow ray starts at a surface. Reusing `currentDist` there is EXACTLY the
// 8.1 rule the amendment retracted, and it would look like working code.
//
// So SelectLODTier below takes a parameter named `cameraDistanceVoxels`, Trace
// takes an explicit `cameraPosM`, and the tier is recomputed per sample from
// distance(samplePoint, camera) -- never from `t`. Stage 3 extracts the same
// function into the shader and calls it from both sites, so the rule is
// defined once (§0.1 invariant 8) and the shadow call site is FORCED to name
// which distance it is passing.
//
// WHY THE TIER IS SAMPLED AND NOT MERELY COMPUTED. A tier number with no
// consequence cannot be tested: the mutation check "swap to travelled distance
// and confirm the silhouette test goes red" only means something if the tier
// changes what the ray SEES. So SolidAtTier() below samples the coarse cell the
// way the real cascade does -- by LODDownsampler.MajorityVote, the same
// already-fuzz-proven 2x2x2 reduction the shipped downsampler uses, applied in
// a chain (tier 2 is a vote over tier-1 votes, not a flat 4^3 vote). That is a
// new COMPOSITION of proven pieces, not new untrusted vote math, and the
// composition is checked against LODDownsampler.DownsampleOnce in the tests.
//
// ---------------------------------------------------------------------------
// 3. C.10 PENUMBRA -- WHAT `clearance` AND `t` ACTUALLY ARE HERE
// ---------------------------------------------------------------------------
// C.10: "track m = min(clearance_i / t_i) over near-miss cells; sunVis =
// smoothstep(0, k_soft, m) on miss, 0 on hit; k_soft -> 0 = hard shadows."
//
// The ratio is an ANGLE. A ray that passes `clearance` voxels beside an
// occluder at travelled distance `t` misses it by roughly clearance/t radians,
// and k_soft is then the sun's angular radius: the fraction of the disc still
// visible is smoothstep(0, k_soft, missAngle). That reading is what gives
// contact hardening for free -- near the contact point t is tiny, so one
// voxel of clearance is a large angle and the sun returns immediately; far
// from it the same voxel is a small angle and the edge spreads.
//
// DISCRETISATION, stated so it is reviewable rather than inferred:
//   - at each DDA cell the ray occupies, its six FACE neighbours are tested at
//     the current tier;
//   - for each SOLID face neighbour, `clearance` is the perpendicular distance
//     from the ray's current position to that neighbour's near face plane, in
//     voxel units, so it lies in [0,1];
//   - `t` is travelled distance in voxel units, floored at 1 so the first cell
//     off the surface cannot divide by ~0 and blacken every lit pixel.
//
// k_soft <= 0 is the spec's own degenerate case and is handled as a BRANCH, not
// as a limit: sunVis is exactly 1.0 on a miss and exactly 0.0 on a hit. That is
// the hard-shadow toggle §6.5 requires, and ShadowRayTests pins it.
//
// ---------------------------------------------------------------------------
// WHAT THIS ORACLE DOES **NOT** COVER -- stated here, not buried in a report
// ---------------------------------------------------------------------------
// Tier 0 is fuzz-proven against the per-voxel walk. Tiers 1 and 2 are NOT, and
// never have been: Amendment 8.10 §5.7 and PHASE_2_COMPLETION.md §6.2 both
// record that the tier-1/2 traversal branch in Raymarch.compute has no CPU
// equivalent at all. Shadow rays inherit that gap exactly. What this file
// proves about tiers 1/2 is that the SELECTION rule and the SAMPLING reduction
// are right; it proves nothing about the shader's coarse-tier traversal, for
// shadow rays any more than for primary rays.
// ==========================================

using Unity.Mathematics;
using VoxelEngine.Memory;
using VoxelEngine.Mirror;

public static class ShadowRayReference
{
    /// One shadow ray's outcome.
    public struct ShadowResult
    {
        /// True when the ray hit something before the clamp: the sample is in
        /// full shadow. Occlusion-only -- WHAT was hit is not recorded, because
        /// nothing downstream may branch on it (§6.5: "occlusion-only, first hit
        /// = done").
        public bool occluded;

        /// C.9's `sunVis`, 0..1. Exactly 0 when occluded. Exactly 1 on a miss
        /// when kSoft <= 0 (hard shadows) or when nothing came near the ray.
        public float sunVis;

        /// C.10's `m` -- the minimum clearance/t ratio seen, i.e. the smallest
        /// angular miss. float.PositiveInfinity when no near-miss cell was seen
        /// at all. Diagnostic; sunVis is the output that matters.
        public float minClearanceRatio;

        /// Voxels visited. The work-saving assertions in the tests read this:
        /// an early-out that silently disengaged would walk the full clamp.
        public int steps;

        /// The §6.5 origin actually used, in voxel coordinates. Surfaced so a
        /// test can assert the origin rule directly rather than inferring it
        /// from an outcome.
        public int3 originVoxel;

        /// True when no ray was fired at all: no primary hit (sky), or the sun
        /// is at or below the horizon. sunVis is 0 and occluded is false --
        /// §6.5's "below the horizon: skip shadow rays, sun term 0, ambient
        /// carries the night."
        public bool skipped;

        /// The tier the FIRST sample was taken at. Diagnostic, and the cheapest
        /// observable difference between the 8.2 rule and the 8.1 rule it
        /// replaced.
        public int firstSampleTier;
    }

    // =====================================================================
    // §6.5's origin rule
    // =====================================================================

    /// The first voxel adjacent to the hit face: one cell off along the face
    /// normal. See the file header for why this takes no bias argument.
    public static int3 ShadowOriginVoxel(int3 hitVoxel, int3 hitNormal) => hitVoxel + hitNormal;

    /// The CENTRE of that voxel, in metres -- what the ray is actually cast
    /// from. (voxel + 0.5) * 0.1, the same voxel-centre convention the existing
    /// raymarch tests use.
    public static float3 ShadowOriginWorld(int3 hitVoxel, int3 hitNormal)
    {
        int3 v = ShadowOriginVoxel(hitVoxel, hitNormal);
        return (new float3(v.x, v.y, v.z) + 0.5f) * 0.1f;
    }

    // =====================================================================
    // §6.5's 8.2 LOD rule
    // =====================================================================

    /// §6.5: `shadowLOD = SelectLODTier(length(sampleWorldPos - cameraWorldPos))`.
    ///
    /// The parameter is named `cameraDistanceVoxels` and not `dist` on purpose:
    /// the one way to get this wrong is to pass the ray's own travelled
    /// distance, and a caller that has to write the argument name has to
    /// confront which distance it holds.
    ///
    /// Boundaries come from LODConfig, never from a literal here -- §0.1
    /// invariant 8. Mirrors Raymarch.compute's inline comparison exactly
    /// (>= tier-1 bound => 2, >= tier-0 bound => 1, else 0), which is what
    /// makes it safe for Stage 3 to replace those four lines with a call.
    public static int SelectLODTier(float cameraDistanceVoxels)
    {
        float tier0Outer = LODConfig.TIER_OUTER_RANGE_M[0] * 10f;
        float tier1Outer = LODConfig.TIER_OUTER_RANGE_M[1] * 10f;
        if (cameraDistanceVoxels >= tier1Outer) return 2;
        if (cameraDistanceVoxels >= tier0Outer) return 1;
        return 0;
    }

    /// The ray-travel clamp of §6.5's "clamps at max distance (island
    /// diagonal)", in METRES.
    ///
    /// DELIBERATE, FLAGGED READING OF THE SPEC. Taken literally the island
    /// diagonal is ~2,700 m (the island is ~1,909 m across per
    /// AMENDMENT_8_11_RENDER_RANGE.md). But the renderer draws 290 m and the
    /// resident window is 409.6 m across, so a ray clamped at 2,700 m would
    /// leave the window within a few hundred metres and then walk thousands of
    /// voxels of guaranteed-nothing before the clamp noticed. Tier 2's outer
    /// bound IS the effective island diagonal in this build, because no data
    /// exists past it and the primary ray already stops there.
    ///
    /// This is NOT an implementation of Amendment 8.11 and must not be read as
    /// one: 8.11 is DRAFT, NOT ADOPTED, and is out of scope. If 8.11 is ever
    /// adopted and render range grows, this constant follows LODConfig
    /// automatically and needs no edit.
    public static float MaxShadowDistanceM => LODConfig.TIER_OUTER_RANGE_M[LODConfig.TIER_COUNT - 1];

    // =====================================================================
    // Tier sampling -- the consequence that makes the LOD rule testable
    // =====================================================================

    /// The material a tier-`tier` sample at world voxel `voxel` resolves to.
    ///
    /// Tier 0 is the raw byte. Coarser tiers apply LODDownsampler's own
    /// majority vote IN A CHAIN -- tier 2 is a vote over tier-1 votes, not a
    /// flat 4^3 vote -- because that is what the shipped downsampler produces
    /// and a shadow that disagreed with the drawn geometry would defeat the
    /// entire point of the 8.2 rule.
    ///
    /// Arithmetic shift is correct for negative coordinates by construction
    /// (§2.3), so there is no negative-quadrant special case.
    public static byte SampleAtTier(ChunkStore store, int3 voxel, int tier)
    {
        if (tier <= 0) return store.GetVoxel(voxel);

        int factor = LODConfig.DownsampleFactor(tier);          // 2, 4, ...
        int3 baseVoxel = (voxel >> tier) << tier;               // coarse cell's min corner

        // Gather the factor^3 block, x-fastest, matching LODDownsampler's
        // ordering so the chain below reduces it identically.
        var buf = new byte[factor * factor * factor];
        int i = 0;
        for (int z = 0; z < factor; z++)
        for (int y = 0; y < factor; y++)
        for (int x = 0; x < factor; x++)
            buf[i++] = store.GetVoxel(baseVoxel + new int3(x, y, z));

        int edge = factor;
        while (edge > 1)
        {
            buf = LODDownsampler.DownsampleOnce(buf, edge);
            edge >>= 1;
        }
        return buf[0];
    }

    /// Solid test at a tier. Air (0) is the only non-solid material, matching
    /// the raymarcher's own `mat != 0` hit test everywhere else.
    public static bool SolidAtTier(ChunkStore store, int3 voxel, int tier) =>
        SampleAtTier(store, voxel, tier) != 0;

    // =====================================================================
    // The ray
    // =====================================================================

    /// Hard occlusion only -- no penumbra. This is §13's "start hard (binary
    /// occlusion), verify, THEN add the penumbra term", kept as its own entry
    /// point so the hard path can be proven before the soft one exists and
    /// re-proven after it does.
    ///
    /// Implemented BY the Phase-2 oracle with a substituted origin and
    /// direction, per §13's "if stuck" instruction -- not by a second DDA.
    public static ShadowResult TraceHard(ChunkStore store, int3 hitVoxel, int3 hitNormal,
                                         float3 sunDir, float3 cameraPosM, float maxDistM = 0f)
        => Trace(store, hitVoxel, hitNormal, sunDir, cameraPosM, kSoft: 0f, maxDistM: maxDistM);

    /// The full §6.5 / C.10 shadow ray.
    ///
    /// `sunDir` points FROM the surface TOWARD the sun (the direction the ray
    /// travels). `cameraPosM` is the camera in metres and is what the 8.2 rule
    /// measures tier by. `kSoft` is C.10's penumbra constant; <= 0 means hard
    /// shadows.
    /// `maxDistM` <= 0 means the shipped clamp (MaxShadowDistanceM). It is an
    /// explicit parameter for one reason: the differential fuzz has to bound
    /// BOTH sides of the comparison identically, and the Phase-2 oracle it is
    /// compared against carries its own hard 1,000-step ceiling. Handing both
    /// the same short distance is what keeps a disagreement meaningful instead
    /// of being one implementation giving up before the other. Mirrors
    /// RaymarchReference.TracerRaycast's own `maxDist` parameter, which exists
    /// for the same reason.
    public static ShadowResult Trace(ChunkStore store, int3 hitVoxel, int3 hitNormal,
                                     float3 sunDir, float3 cameraPosM, float kSoft,
                                     float maxDistM = 0f)
    {
        var r = new ShadowResult
        {
            occluded = false,
            sunVis = 0f,
            minClearanceRatio = float.PositiveInfinity,
            steps = 0,
            originVoxel = ShadowOriginVoxel(hitVoxel, hitNormal),
            skipped = false,
            firstSampleTier = 0,
        };

        // §6.5: "Below the horizon: skip shadow rays (sun term = 0), ambient
        // carries the night." A sun exactly ON the horizon is also skipped --
        // its N.L term is 0 anyway, so tracing would buy nothing and a grazing
        // ray is the most expensive one there is.
        if (!(sunDir.y > 0f))
        {
            r.skipped = true;
            return r;
        }

        float3 dir = math.normalize(sunDir);
        float3 originM = ShadowOriginWorld(hitVoxel, hitNormal);
        float3 originVox = originM * 10f;
        float maxVox = (maxDistM > 0f ? maxDistM : MaxShadowDistanceM) * 10f;

        // ---- integer DDA setup, identical in shape to RaymarchReference's ----
        int3 step = (int3)math.sign(dir);
        float3 tDelta = math.abs(1f / dir);
        int3 voxel = r.originVoxel;
        float3 tMax = new float3(
            step.x > 0 ? (voxel.x + 1f - originVox.x) : (originVox.x - voxel.x),
            step.y > 0 ? (voxel.y + 1f - originVox.y) : (originVox.y - voxel.y),
            step.z > 0 ? (voxel.z + 1f - originVox.z) : (originVox.z - voxel.z)
        ) * tDelta;

        float t = 0f;
        bool first = true;

        while (t < maxVox && r.steps < MaxSteps)
        {
            r.steps++;

            // §6.5's 8.2 rule, evaluated per sample: tier from the CAMERA's
            // distance to THIS sample point, never from `t`.
            float3 sampleM = (new float3(voxel.x, voxel.y, voxel.z) + 0.5f) * 0.1f;
            float camDistVox = math.distance(sampleM, cameraPosM) * 10f;
            int tier = SelectLODTier(camDistVox);
            if (first) { r.firstSampleTier = tier; first = false; }

            if (SolidAtTier(store, voxel, tier))
            {
                // Occlusion-only early exit: first hit is the whole answer.
                r.occluded = true;
                r.sunVis = 0f;
                r.minClearanceRatio = 0f;
                return r;
            }

            // ---- C.10: near-miss clearance at this cell ----
            if (kSoft > 0f)
            {
                float3 posVox = originVox + dir * t;
                float clearance = NearestSolidFaceClearance(store, voxel, posVox, dir, tier);
                if (clearance < float.PositiveInfinity)
                {
                    // t floored at one voxel: the cell one step off the surface
                    // is at t ~ 0, and dividing by it would darken every lit
                    // pixel that has any neighbour at all.
                    float ratio = clearance / math.max(1f, t);
                    if (ratio < r.minClearanceRatio) r.minClearanceRatio = ratio;
                }
            }

            // ---- advance one voxel ----
            if (tMax.x < tMax.y)
            {
                if (tMax.x < tMax.z) { voxel.x += step.x; t = tMax.x; tMax.x += tDelta.x; }
                else                 { voxel.z += step.z; t = tMax.z; tMax.z += tDelta.z; }
            }
            else
            {
                if (tMax.y < tMax.z) { voxel.y += step.y; t = tMax.y; tMax.y += tDelta.y; }
                else                 { voxel.z += step.z; t = tMax.z; tMax.z += tDelta.z; }
            }
        }

        // Missed. C.10: sunVis = smoothstep(0, k_soft, m) on miss.
        // kSoft <= 0 is the spec's own hard-shadow degenerate case and is a
        // BRANCH, not a limit -- smoothstep with a zero-width edge is undefined.
        r.sunVis = (kSoft <= 0f || float.IsPositiveInfinity(r.minClearanceRatio))
            ? 1f
            : math.smoothstep(0f, kSoft, r.minClearanceRatio);
        return r;
    }

    /// Iteration guard -- DERIVED FROM THE DISTANCE CLAMP, not chosen.
    ///
    /// FOUND BY A FAILING TEST, AND WORTH RECORDING BECAUSE THE FIRST VALUE WAS
    /// WRONG IN A WAY THAT WOULD HAVE BEEN INVISIBLE. This was originally 1000,
    /// to match RaymarchReference.TracerRaycast's own bound. But this is a
    /// PER-VOXEL walk and the clamp is 290 m = 2,900 voxels, so the guard bound
    /// every long ray hundreds of voxels before the clamp did -- and a ray that
    /// gives up returns `occluded = false`, i.e. LIT. At golden hour, where
    /// shadow rays are longest and most grazing, that would have silently lit
    /// things that are in shadow: a wrong answer that looks like a rendering
    /// choice. WorkSaving_OpenSkyRayTerminatesAtTheDistanceClamp is the test
    /// that caught it and is the reason it is now derived.
    ///
    /// DERIVATION: a DDA travelling `t` voxel-units along a unit direction `d`
    /// crosses (|dx| + |dy| + |dz|) * t cell boundaries, and that sum is at
    /// most sqrt(3) for a unit vector. So the clamp distance in voxels times
    /// sqrt(3), plus slack for the partial first and last cells, is a bound no
    /// legal ray can reach -- which is what makes the DISTANCE clamp the thing
    /// that actually terminates a ray, with this left as a pure runaway guard.
    ///
    /// The shader is NOT bound this way and does not need to be: its
    /// _MaxOuterIterations counts OUTER iterations, each of which can leap a
    /// whole air brick or air-mip cell, so 1,024 of them cover 2,900 voxels
    /// easily. The two numbers count different things and must not be equated.
    public static int MaxSteps =>
        (int)(MaxShadowDistanceM * 10f * 1.7320509f) + 8;

    /// C.10's `clearance`: the perpendicular distance, in voxel units, from
    /// `posVox` to the near face plane of the closest SOLID face neighbour of
    /// `voxel` THAT LIES AHEAD OF THE RAY. PositiveInfinity when no such
    /// neighbour exists -- i.e. this cell places no constraint on the penumbra.
    ///
    /// ---------------------------------------------------------------------
    /// THE `dot(dir, n) < 0` TEST IS NOT AN OPTIMISATION. IT IS THE PENUMBRA'S
    /// VERSION OF THE ORIGIN RULE, AND WITHOUT IT EVERY LIT PIXEL GOES BLACK.
    /// ---------------------------------------------------------------------
    /// Found by Penumbra_OpenSkyIsFullySunlitAndRecordsNoNearMiss failing on
    /// the most ordinary geometry there is: flat ground, sun straight up.
    ///
    /// A shadow ray starts one cell off the surface it is shading (§6.5's
    /// origin rule). That surface is therefore ALWAYS a face neighbour of the
    /// origin cell, and it is always solid -- it is the thing being lit. Scan
    /// it as a near-miss occluder and `clearance` is ~0 at t ~ 0, so C.10's
    /// `m = min(clearance/t)` collapses to 0, `smoothstep(0, k_soft, 0)` is 0,
    /// and the renderer reports that every surface in the world is in full
    /// shadow. That is shadow acne wearing a different hat, and §6.5's
    /// instruction for acne applies unchanged: fix the geometry of the test,
    /// do not add a bias.
    ///
    /// THE FIX IS CORRECT BY CONSTRUCTION, not by tuning. Skip any neighbour in
    /// direction `n` with `dot(dir, n) < 0` -- an occluder strictly BEHIND the
    /// advancing ray. The receiving surface is at `-hitNormal` from the origin,
    /// and for any pixel the sun actually lights, `dot(dir, hitNormal) > 0` (it
    /// is C.9's `max(0, N.L)` being positive). So
    /// `dot(dir, -hitNormal) < 0` always, and the receiver is excluded for
    /// every lit pixel with no epsilon and nothing to tune. The same test also
    /// excludes the ground a grazing ray is skimming, for the same reason and
    /// with the same guarantee.
    ///
    /// WHAT IT COSTS, STATED HONESTLY: an occluder the ray is RECEDING from is
    /// dropped rather than tracked, so a near-miss behind the ray cannot darken
    /// it. That is a conservative discretisation -- it can only ever let more
    /// light through, never less -- and it is the right visual answer anyway
    /// (a ray moving out from behind a wall is leaving its shadow). It is a
    /// deliberate trade for a construction that cannot self-shadow, not an
    /// oversight. An occluder exactly beside the ray (`dot == 0`, e.g. a wall
    /// alongside a vertical ray) is KEPT -- the test is strictly less-than.
    ///
    /// Face neighbours only, not the 26-neighbourhood: a diagonal neighbour is
    /// never nearer to the ray than the two face cells between it and the ray,
    /// so including it could only ever restate a constraint one of them already
    /// made, at twice the sample cost.
    private static float NearestSolidFaceClearance(ChunkStore store, int3 voxel, float3 posVox,
                                                   float3 dir, int tier)
    {
        float best = float.PositiveInfinity;

        // +X / -X: the neighbour's near face plane is the shared boundary.
        if (dir.x >= 0f && SolidAtTier(store, voxel + new int3(1, 0, 0), tier))
            best = math.min(best, math.max(0f, (voxel.x + 1f) - posVox.x));
        if (dir.x <= 0f && SolidAtTier(store, voxel - new int3(1, 0, 0), tier))
            best = math.min(best, math.max(0f, posVox.x - voxel.x));

        if (dir.y >= 0f && SolidAtTier(store, voxel + new int3(0, 1, 0), tier))
            best = math.min(best, math.max(0f, (voxel.y + 1f) - posVox.y));
        if (dir.y <= 0f && SolidAtTier(store, voxel - new int3(0, 1, 0), tier))
            best = math.min(best, math.max(0f, posVox.y - voxel.y));

        if (dir.z >= 0f && SolidAtTier(store, voxel + new int3(0, 0, 1), tier))
            best = math.min(best, math.max(0f, (voxel.z + 1f) - posVox.z));
        if (dir.z <= 0f && SolidAtTier(store, voxel - new int3(0, 0, 1), tier))
            best = math.min(best, math.max(0f, posVox.z - voxel.z));

        return best;
    }

    // =====================================================================
    // The sky-skip, enforced rather than documented
    // =====================================================================

    /// §6.5: "skips sky pixels (no primary hit => no shadow ray)".
    ///
    /// Takes the PRIMARY ray's own result so the caller cannot forget the
    /// check: a miss returns skipped=true having fired nothing, and
    /// ShadowRayTests asserts steps==0 on that path. Passing a RayHit rather
    /// than a bool is deliberate -- it means the hit voxel and normal come from
    /// the same object that says whether there was a hit at all, so they cannot
    /// be mismatched.
    public static ShadowResult TraceFromPrimaryHit(ChunkStore store,
                                                   RaymarchReference.RayHit primary,
                                                   float3 sunDir, float3 cameraPosM, float kSoft)
    {
        if (!primary.hit)
        {
            return new ShadowResult
            {
                occluded = false,
                sunVis = 0f,
                minClearanceRatio = float.PositiveInfinity,
                steps = 0,
                skipped = true,
            };
        }
        return Trace(store, primary.voxelCoord, primary.normal, sunDir, cameraPosM, kSoft);
        // (maxDistM deliberately left at the shipped clamp here: this is the
        //  production entry point, not the fuzz's.)
    }
}
