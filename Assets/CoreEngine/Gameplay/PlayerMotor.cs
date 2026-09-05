// ==========================================
// Assets/CoreEngine/Gameplay/PlayerMotor.cs
//
// The movement mechanism for §13 Phase 6 file 1. PlayerController is the
// MonoBehaviour shell; THIS is where the physics lives, as a plain class with
// no Unity lifecycle, so EditMode can drive it frame by frame against a
// synthetic ChunkStore and assert on exact positions.
//
// =========================================================================
// WHICH SIDE OF §8.1'S FORK THIS IS
// =========================================================================
// §8.1 and §13 both name a Phase-6 decision point: a character controller
// sampling CPU terrain, or the 540-BoxCollider PhysX treadmill. §13:
//   "build the simple version first: a character controller with ~6-12 DDA
//    probes against CPU terrain (the reviewer's Option B). Only if it proves
//    insufficient, escalate to PhysicsTreadmill.cs."
// This is the simple version. PhysX never sees the world; every collision
// answer comes from IWorldQuery.GetVoxel. No Rigidbody, no BoxCollider, no
// treadmill. Escalating is a human decision (§13, "don't thrash between them")
// and nothing here quietly starts it.
//
// ONE DELIBERATE DEVIATION, FLAGGED RATHER THAN SLIPPED IN. §8.1 says "~6-12
// DDA probes". This tests the player's AABB against every voxel it covers
// instead of sampling 6-12 fixed points. Same mechanism -- CPU GetVoxel,
// no PhysX -- but exhaustive rather than sparse, because a sparse probe set has
// a specific failure mode this avoids for free: with a 0.6 m body (6 voxels)
// and probes at the corners, a 1-3 voxel wide pillar fits BETWEEN the probes
// and the player walks through it. That is exactly the kind of "the simple
// controller can't do the job" evidence §13 says to weigh for escalation, and
// it would be self-inflicted. Cost is bounded by the substep cap, not by body
// size, since only a thin shell of new cells can be entered per substep.
// PERFORMANCE OF THIS CHOICE IS NOT MEASURED -- no rig covers it yet, and it
// is not claimed to be free.
//
// =========================================================================
// THE TWO CARRIED-FORWARD LESSONS (see IsBlocking and the note on teleports)
// =========================================================================
// 1. Air is ambiguous. ChunkStore.GetVoxel returns Air for a chunk that is not
//    loaded, deliberately and frozen (§12). Movement must not read that as
//    "empty space" -- see IsBlocking, which asks IVoxelResidency first.
// 2. A single-frame position jump larger than the streaming window makes
//    ChunkStore refuse the insert (PHASE_5C_COMPLETION.md §9.5). Continuous
//    walking cannot do that; Teleport can. See the note there.

using Unity.Mathematics;

public sealed class PlayerMotor
{
    /// Metres per voxel. CoordMath.WorldToVoxel is floor(worldPos * 10), so this
    /// is 0.1 by construction, not by choice. Named because a bare 0.1f in
    /// collision maths is unreadable.
    public const float VoxelSizeM = 0.1f;

    /// Pushed off a contact plane by this much so the next frame's overlap test
    /// does not immediately re-detect the surface it just resolved against.
    /// 0.1 mm: far below a voxel, far above float noise at world coordinates in
    /// the ~1300 m range this island uses.
    private const float SkinM = 1e-4f;

    /// How far below the feet to look when deciding "grounded". Two thirds of a
    /// millimetre more than the skin, so a player resting exactly on a surface
    /// reads as grounded rather than flickering.
    private const float GroundProbeM = 0.02f;

    /// Hard ceiling on substeps per Step(). At the default caps this is never
    /// approached (terminal velocity needs ~18); it exists so that a hot-reloaded
    /// config with an absurd terminalSpeedMps cannot turn one frame into an
    /// unbounded loop. Hitting it means the move is truncated, which is visible,
    /// rather than the frame hanging, which is not.
    private const int MaxSubsteps = 256;

    private readonly IWorldQuery _world;
    private readonly IVoxelResidency _residency;

    public PlayerConfig Config;

    // ---- State. Public because tests and the rig assert on it directly. ----

    /// Feet-centre, metres. The AABB is
    ///   x,z: PositionM.xz +/- bodyWidthM/2
    ///   y  : PositionM.y .. PositionM.y + bodyHeightM
    public float3 PositionM;
    public float3 VelocityMps;
    public bool Grounded;

    /// Seconds since the player was last grounded. Coyote time compares to this.
    public float TimeSinceGroundedS;

    // ---- Per-Step diagnostics, for rigs and tests. Not gameplay state. ----
    public int LastSubstepCount;
    public bool LastStepUpApplied;
    public bool LastBlockedHorizontally;
    /// Set when a move was refused because the terrain there is NOT LOADED,
    /// as opposed to solid. A rig that sees this climbing is watching the
    /// player outrun the streamer, which is a streaming problem, not a
    /// movement bug -- and it would otherwise be invisible.
    public bool LastBlockedByNonResident;

    public PlayerMotor(IWorldQuery world, IVoxelResidency residency, PlayerConfig config)
    {
        _world = world;
        _residency = residency;
        Config = config ?? new PlayerConfig();
    }

    // =====================================================================
    // Terrain queries
    // =====================================================================

    /// Does this voxel stop the player?
    ///
    /// NON-RESIDENT COUNTS AS BLOCKING. This is the §9.4 lesson applied to
    /// movement. GetVoxel would answer Air here and the player would walk into
    /// -- and then fall through -- a chunk that simply has not streamed in yet.
    /// Failing closed means the player stops at the edge of the loaded world,
    /// which is visible and harmless, instead of falling out of it, which is
    /// neither. The streaming window is far larger than the player's body, so
    /// in normal play this never fires; it is a backstop, not a mechanism.
    ///
    /// FLUID DOES NOT BLOCK. §8.2: "fluid doesn't block movement the way solid
    /// terrain does", and §8.6 gives buoyancy its own three probes. Water, lava
    /// and honey are passable here. Sand does block -- it is a falling SOLID
    /// (MaterialRules.IsFallingSolidMaterial), not a fluid, and standing on a
    /// sand pile has to work.
    public bool IsBlocking(int3 voxel)
    {
        if (_residency != null && !_residency.IsResident(CoordMath.VoxelToChunk(voxel)))
            return true;

        byte m = _world.GetVoxel(voxel);
        if (m == Materials.Air) return false;
        return !MaterialRules.IsFluidMaterial(m);
    }

    /// True if the chunk under this voxel is not loaded. Only used to explain
    /// WHY a move was blocked, never to decide whether it was.
    private bool IsNonResident(int3 voxel)
        => _residency != null && !_residency.IsResident(CoordMath.VoxelToChunk(voxel));

    /// Does the AABB at `feetCentre` overlap anything blocking?
    public bool OverlapsSolid(float3 feetCentre) => OverlapsSolid(feetCentre, out _);

    public bool OverlapsSolid(float3 feetCentre, out bool nonResident)
    {
        nonResident = false;
        float half = Config.bodyWidthM * 0.5f;
        float3 lo = new float3(feetCentre.x - half, feetCentre.y, feetCentre.z - half);
        float3 hi = new float3(feetCentre.x + half, feetCentre.y + Config.bodyHeightM, feetCentre.z + half);

        // The AABB's max face sits exactly on a voxel boundary when the player
        // is snapped to one. Nudging the max inward keeps that from counting the
        // next voxel along as overlapping, which would wedge the player.
        int3 vlo = CoordMath.WorldToVoxel(lo);
        int3 vhi = CoordMath.WorldToVoxel(hi - SkinM);

        for (int z = vlo.z; z <= vhi.z; z++)
        for (int y = vlo.y; y <= vhi.y; y++)
        for (int x = vlo.x; x <= vhi.x; x++)
        {
            int3 v = new int3(x, y, z);
            if (!IsBlocking(v)) continue;
            nonResident = IsNonResident(v);
            return true;
        }
        return false;
    }

    /// Is there support within GroundProbeM below the feet?
    public bool IsGroundedAt(float3 feetCentre)
        => OverlapsSolid(feetCentre - new float3(0f, GroundProbeM, 0f));

    // =====================================================================
    // Stepping
    // =====================================================================

    /// Advances one frame.
    ///
    /// `wishDirXZ` is the desired horizontal direction in world space; magnitude
    /// above 1 is clamped, so a caller may pass a raw stick vector. `jumpPressed`
    /// is EDGE-triggered -- the caller is responsible for not holding it, which
    /// is what makes coyote time observable rather than papered over by autohop.
    public void Step(float dt, float2 wishDirXZ, bool jumpPressed)
    {
        if (dt <= 0f) return;
        PlayerConfig c = Config;

        LastStepUpApplied = false;
        LastBlockedHorizontally = false;
        LastBlockedByNonResident = false;

        // ---- 1. Horizontal acceleration -------------------------------
        float wishLen = math.length(wishDirXZ);
        float2 wishDir = wishLen > 1e-5f ? wishDirXZ / wishLen : float2.zero;
        float wishScale = math.min(wishLen, 1f);

        float2 vel = new float2(VelocityMps.x, VelocityMps.z);

        if (wishLen > 1e-5f)
        {
            float accel = Grounded ? c.accelerationMps2 : c.accelerationMps2 * c.airControl;
            float2 target = wishDir * (c.maxSpeedMps * wishScale);
            float2 delta = target - vel;
            float deltaLen = math.length(delta);
            float step = accel * dt;
            vel += deltaLen > step && deltaLen > 1e-6f ? delta / deltaLen * step : delta;
        }
        else if (Grounded)
        {
            // Friction is ground-only; air keeps its momentum, which is what
            // airControl exists to modulate.
            float speed = math.length(vel);
            float drop = c.frictionMps2 * dt;
            vel = speed > drop && speed > 1e-6f ? vel * ((speed - drop) / speed) : float2.zero;
        }

        VelocityMps.x = vel.x;
        VelocityMps.z = vel.y;

        // ---- 2. Jump, with coyote time --------------------------------
        // Checked BEFORE gravity so a jump on the exact frame of leaving the
        // ground gets the full impulse rather than one frame of gravity first.
        bool canJump = Grounded || TimeSinceGroundedS <= c.coyoteTimeSeconds;
        if (jumpPressed && canJump)
        {
            VelocityMps.y = c.jumpImpulseMps;
            Grounded = false;
            // Consume the coyote window, or a single press could be spent twice
            // across two frames of the same fall.
            TimeSinceGroundedS = c.coyoteTimeSeconds + 1f;
        }

        // ---- 3. Gravity ------------------------------------------------
        VelocityMps.y -= c.gravityMps2 * dt;
        if (VelocityMps.y < -c.terminalSpeedMps) VelocityMps.y = -c.terminalSpeedMps;

        // ---- 4. Integrate with collision --------------------------------
        float3 delta3 = VelocityMps * dt;
        MoveWithCollision(delta3);

        // ---- 5. Grounded state ------------------------------------------
        // GroundProbeM LOOKS AHEAD, IT DOES NOT STOP THE FALL. Zeroing the
        // downward velocity here as well cost a visible 2 cm hover: the probe
        // sees the surface up to GroundProbeM early, so killing the velocity on
        // that signal parks the player just above the ground and "feet rest on
        // the surface" quietly becomes false. Landing is MoveAxis's job -- it
        // snaps to the contact plane and zeroes VelocityMps.y there. This flag
        // only answers "may I jump", where reading ground slightly early is the
        // intended tolerance.
        bool groundedNow = VelocityMps.y <= 0f && IsGroundedAt(PositionM);
        if (groundedNow)
        {
            Grounded = true;
            TimeSinceGroundedS = 0f;
        }
        else
        {
            Grounded = false;
            TimeSinceGroundedS += dt;
        }
    }

    /// Splits the move so no substep advances more than maxSubstepVoxels, then
    /// resolves each axis separately.
    ///
    /// THIS IS WHAT MAKES "NO TUNNELING AT WALK/RUN SPEEDS" STRUCTURAL rather
    /// than a property of the frame rate: the player can never translate far
    /// enough in one collision test to skip over a wall, whatever dt is. It is
    /// NOT the §8.2 swept CCD -- that is file 2, for the 60 m/s grapple, where
    /// substepping a single frame into hundreds of steps stops being sensible.
    private void MoveWithCollision(float3 delta)
    {
        float maxStepM = math.max(Config.maxSubstepVoxels * VoxelSizeM, 1e-3f);
        float dist = math.length(delta);
        if (dist <= 1e-9f) { LastSubstepCount = 0; return; }

        int steps = (int)math.ceil(dist / maxStepM);
        if (steps < 1) steps = 1;
        if (steps > MaxSubsteps) steps = MaxSubsteps;
        LastSubstepCount = steps;

        float3 per = delta / steps;
        for (int i = 0; i < steps; i++)
        {
            // Y first, so Grounded is meaningful when the horizontal pass asks
            // whether a step-up is allowed.
            MoveAxis(1, per.y);
            MoveAxisWithStepUp(0, per.x);
            MoveAxisWithStepUp(2, per.z);
        }
    }

    /// Moves along one axis, snapping exactly to the blocking voxel's face.
    /// Returns true if the full amount was travelled.
    private bool MoveAxis(int axis, float amount)
    {
        if (amount == 0f) return true;

        float3 candidate = PositionM;
        candidate[axis] += amount;

        bool nonResident;
        if (!OverlapsSolid(candidate, out nonResident))
        {
            PositionM = candidate;
            return true;
        }

        // Blocked. Snap to the voxel boundary rather than simply refusing the
        // move, or the player rests up to a substep away from the surface --
        // visible as a gap under the feet and as a jump that starts late.
        float half = Config.bodyWidthM * 0.5f;
        float extentLo = axis == 1 ? 0f : half;
        float extentHi = axis == 1 ? Config.bodyHeightM : half;

        float snapped;
        if (amount > 0f)
        {
            float leadingEdge = candidate[axis] + extentHi;
            float boundary = math.floor(leadingEdge * (1f / VoxelSizeM)) * VoxelSizeM;
            snapped = boundary - extentHi - SkinM;
        }
        else
        {
            float leadingEdge = candidate[axis] - extentLo;
            float boundary = math.floor(leadingEdge * (1f / VoxelSizeM)) * VoxelSizeM + VoxelSizeM;
            snapped = boundary + extentLo + SkinM;
        }

        float3 snapPos = PositionM;
        snapPos[axis] = snapped;

        // Only accept the snap if it is actually free AND does not overshoot
        // past where we already were. If the snap is not free (a corner case
        // where another axis was already interpenetrating), stay put: refusing
        // to move is always safe, moving into solid never is.
        bool movingForward = amount > 0f
            ? snapped >= PositionM[axis]
            : snapped <= PositionM[axis];
        if (movingForward && !OverlapsSolid(snapPos)) PositionM = snapPos;

        if (axis == 1) VelocityMps.y = 0f;
        else
        {
            LastBlockedHorizontally = true;
            if (nonResident) LastBlockedByNonResident = true;
        }
        return false;
    }

    /// Horizontal move that tries a step-up when blocked.
    ///
    /// §13's acceptance line is "3-voxel steps climbable un-jumped", and
    /// stepHeightVoxels is the tunable behind it. The sequence is the standard
    /// one: lift by the step height, retry the move, then settle back down onto
    /// whatever is under the new position. Every stage is checked for overlap,
    /// so a step-up can never end inside geometry or under a low ceiling.
    private void MoveAxisWithStepUp(int axis, float amount)
    {
        if (amount == 0f) return;

        float3 before = PositionM;
        if (MoveAxis(axis, amount)) return;          // wasn't blocked
        if (Config.stepHeightVoxels <= 0) return;

        // Only from the ground. Without this the player can climb a wall in
        // mid-air by holding into it, one step per frame.
        if (!Grounded && TimeSinceGroundedS > Config.coyoteTimeSeconds) return;

        // MoveAxis may have snapped us flush against the wall; step from where
        // we started so the lift is measured against open space.
        float3 start = before;
        float lift = Config.stepHeightVoxels * VoxelSizeM;

        float3 lifted = start + new float3(0f, lift, 0f);
        if (OverlapsSolid(lifted)) return;           // no headroom to lift into

        float3 liftedMoved = lifted;
        liftedMoved[axis] += amount;
        if (OverlapsSolid(liftedMoved)) return;      // still blocked up there

        // Settle back down: the step must be something to stand ON, not a
        // hop into open air. Descend at most the lift, in voxel increments.
        float3 settled = liftedMoved;
        for (int i = 0; i < Config.stepHeightVoxels; i++)
        {
            float3 down = settled - new float3(0f, VoxelSizeM, 0f);
            if (OverlapsSolid(down)) break;
            settled = down;
        }

        // Accept only if we ended up supported and genuinely higher than we
        // started; otherwise this was a hop over a hole, not a step onto a ledge.
        if (settled.y > start.y - SkinM && IsGroundedAt(settled) && !OverlapsSolid(settled))
        {
            PositionM = settled;
            LastStepUpApplied = true;
            LastBlockedHorizontally = false;
        }
    }

    // =====================================================================
    // Teleport
    // =====================================================================

    /// Snaps the player somewhere, bypassing collision and integration.
    ///
    /// THE FAST-TRAVEL HAZARD, RECORDED HERE BECAUSE THIS IS THE ONLY PLACE IT
    /// CAN ARISE (PHASE_5C_COMPLETION.md §9.5): ChunkStore refuses an insert --
    /// cleanly, with a named exception, no corruption -- when a single-frame
    /// position jump exceeds the streaming window. Phase 5d tripped it with a
    /// real camera teleport. Continuous walk/run/jump CANNOT: Step is bounded by
    /// maxSpeedMps and terminalSpeedMps, both metres-per-second against a window
    /// measured in hundreds of metres.
    ///
    /// This method is the exception, and so is anything built on it later --
    /// respawn, a fast-travel feature, a debug warp. If you add one, move the
    /// streaming window with the player and let it fill BEFORE or ACROSS the
    /// jump; do not assume the streamer will catch up on its own. Movement
    /// itself is safe; snapping is what is not.
    public void Teleport(float3 feetCentre)
    {
        PositionM = feetCentre;
        VelocityMps = float3.zero;
        Grounded = IsGroundedAt(feetCentre);
        TimeSinceGroundedS = Grounded ? 0f : Config.coyoteTimeSeconds + 1f;
    }

    /// Lifts the player straight up out of anything they are inside, up to
    /// `maxVoxels`. Used after a teleport onto generated terrain, where the
    /// requested spot may be underground. Returns false if it could not free
    /// them, which the caller should treat as "that was a bad spawn point".
    public bool ResolveSpawn(int maxVoxels = 64)
    {
        for (int i = 0; i <= maxVoxels; i++)
        {
            float3 p = PositionM + new float3(0f, i * VoxelSizeM, 0f);
            if (!OverlapsSolid(p))
            {
                PositionM = p;
                Grounded = IsGroundedAt(p);
                TimeSinceGroundedS = Grounded ? 0f : Config.coyoteTimeSeconds + 1f;
                return true;
            }
        }
        return false;
    }
}
