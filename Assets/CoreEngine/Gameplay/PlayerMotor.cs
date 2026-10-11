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
//    "empty space". The rule lives in VoxelCollision, shared with SweptCCD --
//    two passes with two copies of "what is solid" is how a body stopped by one
//    gets passed by the other.
// 2. A single-frame position jump larger than the streaming window makes
//    ChunkStore refuse the insert (PHASE_5C_COMPLETION.md §9.5). Continuous
//    walking cannot do that; Teleport can. See the note there.

// =========================================================================
// MOVEMENT SET A (task 65, GAMEPLAY_PHASE_PLAN.md step 2): swim/dive, double
// jump, dash. Only Step(dt, in MotorInput) uses them. The legacy
// Step(dt, wish, jump) -- every rig and every pre-existing test -- runs the
// exact old arithmetic: no swimming (a body sinks through water, as
// APlayerDoesNotStandOnWater pins), no air jump, no dash.
//
// SWIMMING STILL SAMPLES ONE-WAY (§8.6). The motor reads fluid bytes at the
// Buoyancy probe positions and never writes or wakes anything. Swim = the
// Center probe (half the body height) is in a liquid -- ">= 50% submerged".
// While swimming, gravity is replaced by a target velocity: WASD along the
// look direction, Space up, Ctrl down, capped at swimSpeedMps scaled down by
// the fluid's drag relative to water's; with no vertical input the body
// floats so its swimFloatHeightM point sits at the water line.
// =========================================================================

using Unity.Mathematics;

/// One frame of player intent for Step(dt, in MotorInput). Build it with
/// FromLook so every caller turns keys + camera angles into the same vectors.
public struct MotorInput
{
    /// World-space horizontal wish, exactly the legacy Step's `wishDirXZ`.
    public float2 WishXZ;
    /// The same keys in the CAMERA's frame, pitch included: swimming moves
    /// along this (look down + W = dive).
    public float3 LookWish;
    /// Unit horizontal look direction. A dash with no move key goes this way.
    public float2 ForwardXZ;
    /// Edge-triggered jump (ground jump, double jump, jump out of water).
    public bool JumpPressed;
    /// Held: swim up. Held: swim down.
    public bool UpHeld, DownHeld;
    /// Edge-triggered dash.
    public bool DashPressed;

    /// `move` = (strafe, forward) in -1..1 from the keys; Unity camera angles
    /// (pitch positive = looking down).
    public static MotorInput FromLook(float2 move, float yawDeg, float pitchDeg)
    {
        float y = math.radians(yawDeg), p = math.radians(pitchDeg);
        float sy = math.sin(y), cy = math.cos(y), sp = math.sin(p), cp = math.cos(p);
        var fwd3 = new float3(sy * cp, -sp, cy * cp);
        var right3 = new float3(cy, 0f, -sy);
        return new MotorInput
        {
            WishXZ = new float2(move.x * cy + move.y * sy, move.y * cy - move.x * sy),
            LookWish = right3 * move.x + fwd3 * move.y,
            ForwardXZ = new float2(sy, cy),
        };
    }
}

public sealed class PlayerMotor
{
    /// Metres per voxel. CoordMath.WorldToVoxel is floor(worldPos * 10), so this
    /// is 0.1 by construction, not by choice. Named because a bare 0.1f in
    /// collision maths is unreadable.
    public const float VoxelSizeM = VoxelCollision.VoxelSizeM;

    /// Pushed off a contact plane by this much so the next frame's overlap test
    /// does not immediately re-detect the surface it just resolved against.
    /// 0.1 mm: far below a voxel, far above float noise at world coordinates in
    /// the ~1300 m range this island uses.
    private const float SkinM = VoxelCollision.SkinM;

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

    // ---- Movement set A state (Step(dt, in MotorInput) only). ----

    /// The Center probe is in a liquid this step: gravity is off, the swim
    /// target velocity drives the body.
    public bool Swimming;
    /// The liquid being swum in, Air when not swimming.
    public byte SwimFluid = Materials.Air;
    /// Air jumps spent since the last landing / entering fluid.
    public int AirJumpsUsed;
    /// Seconds left in the current dash; > 0 while dashing.
    public float DashTimeLeftS;
    /// Seconds until the next dash may start (the HUD's cooldown bar).
    public float DashCooldownS;
    /// A dash was started in this airtime (one per airtime).
    public bool DashUsedInAir;
    public float2 DashDir;
    /// After a jump out of water, swimming stays off this long so the swim
    /// target velocity does not cancel the jump before the body leaves.
    public float SwimLockS;
    public bool LastDoubleJump, LastDashStarted, LastWaterJump;

    public bool Dashing => DashTimeLeftS > 0f;

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
    public bool IsBlocking(int3 voxel) => VoxelCollision.IsBlocking(_world, _residency, voxel);

    /// Does the AABB at `feetCentre` overlap anything blocking?
    public bool OverlapsSolid(float3 feetCentre) => OverlapsSolid(feetCentre, out _);

    public bool OverlapsSolid(float3 feetCentre, out bool nonResident)
        => VoxelCollision.OverlapsSolid(_world, _residency, feetCentre,
                                        Config.bodyWidthM, Config.bodyHeightM, out nonResident);

    /// Is there support within GroundProbeM below the feet?
    public bool IsGroundedAt(float3 feetCentre)
        => OverlapsSolid(feetCentre - new float3(0f, GroundProbeM, 0f));

    // =====================================================================
    // Stepping
    // =====================================================================

    /// Advances one frame. THE LEGACY PATH: walk, jump, step-up only -- no
    /// swim, no air jump, no dash -- exactly as before movement set A, so rigs
    /// and the existing PlayerMotorTests are unchanged.
    ///
    /// `wishDirXZ` is the desired horizontal direction in world space; magnitude
    /// above 1 is clamped, so a caller may pass a raw stick vector. `jumpPressed`
    /// is EDGE-triggered -- the caller is responsible for not holding it, which
    /// is what makes coyote time observable rather than papered over by autohop.
    public void Step(float dt, float2 wishDirXZ, bool jumpPressed)
        => StepCore(dt, new MotorInput { WishXZ = wishDirXZ, JumpPressed = jumpPressed }, false);

    /// Advances one frame with movement set A (swim/dive, double jump, dash).
    /// With no fluid around and no dash or air jump pressed it does exactly
    /// what the legacy Step does.
    public void Step(float dt, in MotorInput input) => StepCore(dt, input, true);

    private void StepCore(float dt, in MotorInput input, bool abilities)
    {
        if (dt <= 0f) return;
        PlayerConfig c = Config;

        LastStepUpApplied = false;
        LastBlockedHorizontally = false;
        LastBlockedByNonResident = false;
        LastDoubleJump = LastDashStarted = LastWaterJump = false;
        bool jumpPressed = input.JumpPressed;

        if (!abilities)
        {
            Swimming = false;
            SwimFluid = Materials.Air;
            DashTimeLeftS = 0f;
        }
        else
        {
            if (SwimLockS > 0f) SwimLockS = math.max(0f, SwimLockS - dt);
            if (DashCooldownS > 0f) DashCooldownS = math.max(0f, DashCooldownS - dt);
            UpdateSwimming();
            if (Swimming) { AirJumpsUsed = 0; DashUsedInAir = false; DashTimeLeftS = 0f; }

            // Jump out of water: only at the surface (head probe dry), at the
            // normal jump impulse; swimming then stays off for SwimLockS.
            if (Swimming && jumpPressed && !InLiquid(Buoyancy.ProbePosition(PositionM, c.bodyHeightM, BuoyancyProbe.Head), out _))
            {
                Swimming = false;
                SwimFluid = Materials.Air;
                SwimLockS = c.waterJumpSwimLockS;
                VelocityMps.y = c.jumpImpulseMps;
                Grounded = false;
                TimeSinceGroundedS = c.coyoteTimeSeconds + 1f;
                jumpPressed = false;
                LastWaterJump = true;
            }

            // Dash: not in fluid, not while one runs, off cooldown, one per airtime.
            if (input.DashPressed && !Swimming && !Dashing && DashCooldownS <= 0f
                && (Grounded || !DashUsedInAir) && c.dashSpeedMps > 0f && c.dashDurationS > 0f)
            {
                float wl = math.length(input.WishXZ);
                float2 dir = wl > 1e-5f ? input.WishXZ / wl : math.normalizesafe(input.ForwardXZ, new float2(0f, 1f));
                DashDir = dir;
                DashTimeLeftS = c.dashDurationS;
                DashCooldownS = c.dashCooldownS;
                if (!Grounded) DashUsedInAir = true;
                LastDashStarted = true;
            }

            if (Dashing) { StepDash(dt); UpdateGrounded(dt); return; }
            if (Swimming)
            {
                StepSwim(dt, input);
                MoveWithCollision(VelocityMps * dt);
                UpdateGrounded(dt);
                return;
            }
        }

        // ---- 1. Horizontal acceleration -------------------------------
        float2 wishDirXZ = input.WishXZ;
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
        else if (abilities && jumpPressed && AirJumpsUsed < c.airJumps)
        {
            // Double jump: SETS the vertical speed (a falling body gets the
            // same lift as a rising one), reset by landing or entering fluid.
            VelocityMps.y = c.jumpImpulseMps * c.airJumpImpulseScale;
            AirJumpsUsed++;
            LastDoubleJump = true;
        }

        // ---- 3. Gravity ------------------------------------------------
        VelocityMps.y -= c.gravityMps2 * dt;
        if (VelocityMps.y < -c.terminalSpeedMps) VelocityMps.y = -c.terminalSpeedMps;

        // ---- 4. Integrate with collision --------------------------------
        float3 delta3 = VelocityMps * dt;
        MoveWithCollision(delta3);

        // ---- 5. Grounded state ------------------------------------------
        UpdateGrounded(dt);
    }

    /// GroundProbeM LOOKS AHEAD, IT DOES NOT STOP THE FALL. Zeroing the
    /// downward velocity here as well cost a visible 2 cm hover: the probe
    /// sees the surface up to GroundProbeM early, so killing the velocity on
    /// that signal parks the player just above the ground and "feet rest on
    /// the surface" quietly becomes false. Landing is MoveAxis's job -- it
    /// snaps to the contact plane and zeroes VelocityMps.y there. This flag
    /// only answers "may I jump", where reading ground slightly early is the
    /// intended tolerance.
    private void UpdateGrounded(float dt)
    {
        bool groundedNow = VelocityMps.y <= 0f && IsGroundedAt(PositionM);
        if (groundedNow)
        {
            Grounded = true;
            TimeSinceGroundedS = 0f;
            AirJumpsUsed = 0;
            DashUsedInAir = false;
        }
        else
        {
            Grounded = false;
            TimeSinceGroundedS += dt;
        }
    }

    // =====================================================================
    // Movement set A
    // =====================================================================

    /// A LIQUID: a fluid that is not a gas (steam is a fluid but nobody swims
    /// in it). Non-resident reads dry, Buoyancy's rule: missing a swim for a
    /// frame is harmless, inventing one at the window edge is a launch.
    private bool InLiquid(float3 pointM, out byte material)
    {
        if (!VoxelCollision.IsFluid(_world, _residency, CoordMath.WorldToVoxel(pointM), out material)) return false;
        if (MaterialRules.IsGasMaterial(material)) { material = Materials.Air; return false; }
        return true;
    }

    private bool InLiquid(int3 voxel)
    {
        byte m;
        return VoxelCollision.IsFluid(_world, _residency, voxel, out m) && !MaterialRules.IsGasMaterial(m);
    }

    /// ">= 50% submerged" = the Buoyancy Center probe (half the body) is in a liquid.
    private void UpdateSwimming()
    {
        byte m = Materials.Air;
        bool wet = SwimLockS <= 0f
                   && InLiquid(Buoyancy.ProbePosition(PositionM, Config.bodyHeightM, BuoyancyProbe.Center), out m);
        Swimming = wet;
        SwimFluid = wet ? m : Materials.Air;
    }

    /// The swim speed cap in this fluid: swimSpeedMps in water, scaled by
    /// water's drag / this fluid's drag when that is thicker (oil 0.6x,
    /// lava 0.25x, sludge 0.17x).
    public float SwimCapMps(byte fluid)
    {
        float drag = MaterialRules.ViscosityDrag(fluid);
        float waterDrag = MaterialRules.ViscosityDrag(Materials.Water);
        return Config.swimSpeedMps * (drag > waterDrag ? waterDrag / drag : 1f);
    }

    private void StepSwim(float dt, in MotorInput input)
    {
        PlayerConfig c = Config;
        float cap = SwimCapMps(SwimFluid);

        float3 wish = input.LookWish;
        wish.y += (input.UpHeld ? 1f : 0f) - (input.DownHeld ? 1f : 0f);
        float wl = math.length(wish);
        if (wl > 1f) wish /= wl;
        float3 target = wish * cap;
        // No vertical intent: float so the swimFloatHeightM point sits at the water line.
        if (math.abs(wish.y) < 0.05f) target.y = FloatTargetVy();
        float tl = math.length(target);
        if (tl > cap) target *= cap / tl;

        // Arriving faster than the cap (a dive from height): the fluid's drag
        // bleeds the excess, never below the cap.
        float3 v = VelocityMps;
        float sp = math.length(v);
        if (sp > cap)
        {
            float drag = math.max(MaterialRules.ViscosityDrag(SwimFluid), 0.5f);
            v *= math.max(cap, sp * math.exp(-drag * dt)) / sp;
        }

        float3 d = target - v;
        float dl = math.length(d);
        float step = c.swimAccelMps2 * dt;
        v += dl > step ? d / dl * step : d;
        VelocityMps = v;
    }

    /// Vertical speed that brings the float point to the water line above (or
    /// below) it, proportional to the distance, limited to swimFloatRiseMps.
    private float FloatTargetVy()
    {
        PlayerConfig c = Config;
        float3 fp = PositionM + new float3(0f, c.swimFloatHeightM, 0f);
        int3 v = CoordMath.WorldToVoxel(fp);
        float surfaceY;
        if (InLiquid(v))
        {
            int k = 1;
            while (k <= 40 && InLiquid(v + new int3(0, k, 0))) k++;
            if (k > 40) return c.swimFloatRiseMps;            // deep: rise at full float speed
            surfaceY = (v.y + k) * VoxelSizeM;
        }
        else
        {
            int maxK = (int)math.ceil(c.swimFloatHeightM / VoxelSizeM) + 1;
            int k = 1;
            while (k <= maxK && !InLiquid(v - new int3(0, k, 0))) k++;
            if (k > maxK) return -c.swimFloatRiseMps;
            surfaceY = (v.y - k + 1) * VoxelSizeM;
        }
        return math.clamp((surfaceY - fp.y) * c.swimFloatGainPerS, -c.swimFloatRiseMps, c.swimFloatRiseMps);
    }

    /// One dash frame: dashSpeedMps along DashDir with gravity held off, swept
    /// through the same substep collision as walking (no tunnelling). The frame
    /// the dash ends in is split so the dash covers exactly speed x duration
    /// whatever the frame rate; after it the body keeps maxSpeedMps. A wall
    /// ends the dash.
    private void StepDash(float dt)
    {
        PlayerConfig c = Config;
        float part = math.min(dt, DashTimeLeftS);
        DashTimeLeftS -= part;
        float2 exitV = DashDir * c.maxSpeedMps;
        float2 hv = (DashDir * (c.dashSpeedMps * part) + exitV * (dt - part)) / dt;
        VelocityMps = new float3(hv.x, 0f, hv.y);
        MoveWithCollision(VelocityMps * dt);
        if (LastBlockedHorizontally)
        {
            DashTimeLeftS = 0f;
            VelocityMps.x = 0f;
            VelocityMps.z = 0f;
        }
        else if (DashTimeLeftS <= 0f)
        {
            DashTimeLeftS = 0f;
            VelocityMps.x = exitV.x;
            VelocityMps.z = exitV.y;
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
        // A swimmer may step up too: that is how a body leaves a lake onto a
        // low bank (Swimming is never set on the legacy path).
        if (!Grounded && TimeSinceGroundedS > Config.coyoteTimeSeconds && !Swimming) return;

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
        DashTimeLeftS = 0f;
        AirJumpsUsed = 0;
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
