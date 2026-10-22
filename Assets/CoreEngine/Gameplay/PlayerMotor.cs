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

// =========================================================================
// SLOPES AND CLIMBING (task 93f1, GAMEPLAY_PHASE_PLAN.md step 5; the brief's
// movement bands). Step(dt, in MotorInput) only; the legacy Step keeps its
// 3-voxel step-up on every blocked move (rigs, PlayerMotorTests).
//   Walk     an auto-step is taken only if the ground ahead rises at most
//            walkSlopeMaxDeg over slopeBaselineM (RiseAheadVoxels: the
//            highest column of the next 0.5 m, three lines across the body).
//   Scramble steeper ground under the feet slides you downhill
//            (UpdateSlide: a CONTINUOUS drop over the baseline -- a cliff
//            edge is a ledge you may stand on, not a slope); jumping works.
//   Climb    pressing into a face rising >= climbMinSlopeDeg over the
//            baseline for climbGrabDelayS clings (StepClimb): climbSpeedMps
//            up (Ctrl down, strafe sideways), stamina drains, letting go of
//            the key / Space releases, the top is mantled by the normal
//            step-up. Stamina refills only when RESTING: grounded, not
//            sliding, the whole footprint supported (a 0.6 m ledge).
//            Loose / slick faces (MaterialRules.ClimbGrip) are slower and
//            tire faster.
// All whole-voxel column counts on CPU terrain, read through IsBlocking.
// =========================================================================

// =========================================================================
// GLIDING (task 93g1, GAMEPLAY_PHASE_PLAN.md step 6; the brief: "hold Space
// in the air after the double jump; sink ~2 m/s, forward ~10 m/s"). Step(dt,
// in MotorInput) only. Airborne, out of fluid, not climbing or dashing, the
// air jumps spent, falling (vy <= 0) and Space HELD: the body glides
// (StepGlide). Fall speed eases to glideSinkMps (a faster fall is braked at
// glideBrakeMps2, a slower one gains gravity), horizontal speed eases to
// glideSpeedMps along the LOOK yaw (the mouse steers; S = glideSlowSpeedMps,
// A/D drift glideStrafeMps sideways). Glide ratio = speed / sink = 5 by
// default. Letting go of Space falls normally; landing, water, a dash or a
// cling end it. A glide landing hits at ~glideSinkMps, far under the 4 m fall
// damage threshold (PlayerVitals).
// =========================================================================

// =========================================================================
// GRAPPLING HOOK (task 100, GAMEPLAY_PHASE_PLAN.md step 7; the brief: "fire
// at solid within 25 m (VoxelRayWalker), reel in, swing"). Step(dt, in
// MotorInput) only. HookPressed fires from the eye along LookDir: the DDA
// passes air and fluid and bites the first SOLID voxel within hookRangeM; an
// unloaded chunk (or nothing in range) is a miss -- never an anchor. The rope
// runs from RopeAttachM (the hands, 3/4 of the body up) to the face point.
//   Reel   HookHeld: gravity off, the velocity eases to hookReelSpeedMps
//          toward the anchor; the rope shortens as you go. Stops (velocity
//          zero, HookArrived) at hookMinLengthM or when terrain blocks the
//          pull -- MoveWithCollision, so the stop is AT the wall, never in it.
//   Swing  released in the air: gravity, the keys pump at hookSwingAccelMps2
//          (no speed cap), the rope is inextensible (ApplyRope projects the
//          body back onto the sphere and drops the outward velocity).
//   On the ground, not reeling: walk physics, the rope only keeps you within
//          its length (a tether).
// Ends: Space (the press still jumps / air-jumps, so a swing can end in a
// double jump and then a glide), a dash, a swim, a cling, a landing after a
// swing (a normal landing: PlayerVitals reads LastImpactDownMps as ever), the
// anchor voxel dug out or unloaded, Teleport. Pressing again re-aims. The
// reel is 12 m/s, under the 4 m fall-damage speed (13.3 m/s at g = 22).
// The rope does not wrap around terrain (a straight constraint).
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
    /// Unit look direction (camera forward, pitch included): the hook's aim.
    public float3 LookDir;
    /// Edge-triggered: fire the grappling hook (again = re-aim). Held: reel in.
    public bool HookPressed, HookHeld;

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
            LookDir = fwd3,
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
    /// Task 93d1 (gameplay 9a): the downward speed (m/s, >= 0) at which the
    /// feet hit a solid this step, 0 if they did not. Read by PlayerVitals for
    /// fall damage; the motor itself does nothing with it.
    public float LastImpactDownMps;

    // ---- Slopes and climbing (task 93f1; Step(dt, in MotorInput) only). ----

    /// Standing on ground steeper than the walk band: pushed along SlideDir.
    public bool Sliding;
    public float2 SlideDir;
    /// Clinging to a face; ClimbDir points INTO it (one axis, +-1).
    public bool Climbing;
    public int2 ClimbDir;
    /// The face's material and its MaterialRules.ClimbGrip class.
    public byte ClimbFace;
    public byte ClimbGrip;
    /// Seconds of climbing left (PlayerConfig.climbStaminaS when full).
    public float StaminaS;
    /// On a rest ledge this step: stamina refills.
    public bool Resting;
    /// An auto-step was refused this step because the ground ahead rises
    /// above the walk band; LastRiseVoxels is what was measured.
    public bool LastSteepRefused;
    public int LastRiseVoxels;
    /// Gliding this step (task 93g1): Space held in the air after the air jumps.
    public bool Gliding;
    // ---- Grappling hook (task 100; Step(dt, in MotorInput) only). ----

    /// The hook is in a solid voxel; the rope holds.
    public bool Hooked;
    /// Where it bit (a point on the voxel's face), and that voxel.
    public float3 HookAnchorM;
    public int3 HookVoxel;
    /// The rope's current length (RopeAttachM to the anchor), metres.
    public float RopeLengthM;
    /// Reeling in this step; the reel was stopped by the anchor or a wall.
    public bool Reeling, HookArrived;
    /// This step fired the hook; it bit; the hook let go this step.
    public bool LastHookFired, LastHookHit, LastHookReleased;
    // ---- Wall-run and wall-jump (task 103; Step(dt, in MotorInput) only). ----

    /// Running along a wall this step; WallDir points INTO it (one axis, +-1).
    public bool WallRunning;
    public int2 WallDir;
    /// Seconds into the current run.
    public float WallRunTimeS;
    /// This step started a wall-run; this step jumped off a wall.
    public bool LastWallRunStarted, LastWallJump;
    /// The wall last run on or jumped off this airtime (axis, dir, plane
    /// column): it may not be used again until the feet touch ground or
    /// fluid, so walls are chained by alternating (a chimney), not repeated.
    private int3 _lastWall;
    private bool _hasLastWall;
    /// Seconds the keys have pressed into a climbable face (grab delay).
    private float _climbHoldS;
    /// The current step runs with abilities (the slope rule applies).
    private bool _abilityStep;

    public bool Dashing => DashTimeLeftS > 0f;

    public PlayerMotor(IWorldQuery world, IVoxelResidency residency, PlayerConfig config)
    {
        _world = world;
        _residency = residency;
        Config = config ?? new PlayerConfig();
        StaminaS = Config.climbStaminaS;
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
        LastHookFired = LastHookHit = LastHookReleased = false;
        LastWallRunStarted = LastWallJump = false;
        Reeling = false;
        LastImpactDownMps = 0f;
        LastSteepRefused = false;
        _abilityStep = abilities;
        bool jumpPressed = input.JumpPressed;

        if (!abilities)
        {
            Swimming = false;
            SwimFluid = Materials.Air;
            DashTimeLeftS = 0f;
            Climbing = Sliding = Resting = Gliding = false;
            Hooked = false;
            WallRunning = false;
        }
        else
        {
            if (SwimLockS > 0f) SwimLockS = math.max(0f, SwimLockS - dt);
            if (DashCooldownS > 0f) DashCooldownS = math.max(0f, DashCooldownS - dt);
            if (StaminaS > c.climbStaminaS) StaminaS = c.climbStaminaS;
            Gliding = false;
            UpdateSwimming();
            if (Swimming) { AirJumpsUsed = 0; DashUsedInAir = false; DashTimeLeftS = 0f; Climbing = Sliding = Resting = WallRunning = false; _hasLastWall = false; ReleaseHook(); }

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

            // Grappling hook (task 100): fire / re-aim; Space or a lost anchor
            // lets go -- the Space press is NOT consumed (jump / air jump).
            if (input.HookPressed && !Swimming) FireHook(input.LookDir);
            if (Hooked && (jumpPressed || !AnchorHolds())) ReleaseHook();
            if (Hooked) WallRunning = false;

            // Dash: not in fluid, not while one runs, off cooldown, one per airtime.
            if (input.DashPressed && !Swimming && !Climbing && !Dashing && DashCooldownS <= 0f
                && (Grounded || !DashUsedInAir) && c.dashSpeedMps > 0f && c.dashDurationS > 0f)
            {
                float wl = math.length(input.WishXZ);
                float2 dir = wl > 1e-5f ? input.WishXZ / wl : math.normalizesafe(input.ForwardXZ, new float2(0f, 1f));
                DashDir = dir;
                DashTimeLeftS = c.dashDurationS;
                DashCooldownS = c.dashCooldownS;
                if (!Grounded) DashUsedInAir = true;
                LastDashStarted = true;
                WallRunning = false;
                ReleaseHook();
            }

            if (Dashing) { StepDash(dt); UpdateGrounded(dt); return; }
            if (Swimming)
            {
                StepSwim(dt, input);
                MoveWithCollision(VelocityMps * dt);
                UpdateGrounded(dt);
                return;
            }

            // Climb: cling after pressing into a Climb-band face; a frame
            // that lets go falls through to the walk physics below.
            if (!Climbing && !WallRunning) TryStartClimb(dt, input);
            if (Climbing) ReleaseHook();
            if (Climbing)
            {
                if (StepClimb(dt, input)) return;
                jumpPressed = false;              // Space let go; it is not also a jump
            }

            // Hooked: held = reel in; in the air = swing; on the ground the
            // walk physics below run and the rope is applied after the move.
            if (Hooked && input.HookHeld)
            {
                StepReel(dt);
                UpdateGrounded(dt);
                return;
            }
            if (Hooked && !Grounded)
            {
                StepSwing(dt, input);
                return;
            }
            UpdateSlide();

            // Wall-run (task 103): along a wall beside the body, airborne and
            // fast; a frame that ends it falls through to the walk physics.
            if (!WallRunning) TryStartWallRun(input, jumpPressed);
            if (WallRunning && StepWallRun(dt, input, jumpPressed)) return;

            // Glide: after the air jumps, falling, Space held (task 93g1).
            if (CanGlide(input, jumpPressed))
            {
                Gliding = true;
                StepGlide(dt, input);
                MoveWithCollision(VelocityMps * dt);
                UpdateGrounded(dt);
                if (Grounded) Gliding = false;
                return;
            }
        }

        // ---- 1. Horizontal acceleration -------------------------------
        float2 wishDirXZ = input.WishXZ;
        float wishLen = math.length(wishDirXZ);
        float2 wishDir = wishLen > 1e-5f ? wishDirXZ / wishLen : float2.zero;
        float wishScale = math.min(wishLen, 1f);

        float2 vel = new float2(VelocityMps.x, VelocityMps.z);

        if (Sliding)
        {
            // The slope owns the downhill axis: the keys steer across it
            // only, so holding W uphill does not hold you on a 45 deg slope.
            float2 perp = new float2(-SlideDir.y, SlideDir.x);
            float accel = Grounded ? c.accelerationMps2 : c.accelerationMps2 * c.airControl;
            float vAlong = math.dot(vel, SlideDir), vPerp = math.dot(vel, perp);
            float tPerp = math.dot(wishDir, perp) * c.maxSpeedMps * wishScale;
            vPerp = MoveToward(vPerp, tPerp, accel * dt);
            float a = c.slideAccelMps2 * dt;
            vAlong = vAlong < c.slideSpeedMps ? math.min(vAlong + a, c.slideSpeedMps)
                                              : math.max(c.slideSpeedMps, vAlong - a);
            vel = SlideDir * vAlong + perp * vPerp;
        }
        else if (wishLen > 1e-5f)
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
        else if (abilities && jumpPressed && TryWallJump(input))
        {
            // Wall-jump (task 103): in the air against a wall, Space kicks off
            // it; the air jump is kept.
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
        if (Hooked) ApplyRope();          // grounded tether (task 100)

        // ---- 5. Grounded state ------------------------------------------
        UpdateGrounded(dt);
        if (abilities) UpdateRest(dt);
    }

    /// Task 93g1: may this step glide? A jump press that can still be spent
    /// (coyote or an air jump) is spent instead.
    private bool CanGlide(in MotorInput input, bool jumpPressed)
    {
        PlayerConfig c = Config;
        if (!input.UpHeld || c.glideSpeedMps <= 0f) return false;
        if (Grounded || Swimming || Climbing || Dashing || Sliding || WallRunning) return false;
        if (AirJumpsUsed < c.airJumps || VelocityMps.y > 0f) return false;
        if (jumpPressed && TimeSinceGroundedS <= c.coyoteTimeSeconds) return false;
        return true;
    }

    /// One gliding frame: sets VelocityMps (the caller integrates it).
    private void StepGlide(float dt, in MotorInput input)
    {
        PlayerConfig c = Config;
        float vy = VelocityMps.y;
        float sink = -c.glideSinkMps;
        VelocityMps.y = vy < sink ? MoveToward(vy, sink, c.glideBrakeMps2 * dt)
                                  : MoveToward(vy, sink, c.gravityMps2 * dt);

        float2 fwd = math.normalizesafe(input.ForwardXZ, new float2(0f, 1f));
        float2 right = new float2(fwd.y, -fwd.x);
        float2 w = input.WishXZ;
        float wl = math.length(w);
        if (wl > 1f) w /= wl;
        float along = math.dot(w, fwd), strafe = math.dot(w, right);
        float speed = along < -0.3f ? c.glideSlowSpeedMps : c.glideSpeedMps;
        float2 target = fwd * speed + right * (strafe * c.glideStrafeMps);
        float2 vel = new float2(VelocityMps.x, VelocityMps.z);
        float2 delta = target - vel;
        float dl = math.length(delta), stepA = c.glideAccelMps2 * dt;
        vel += dl > stepA && dl > 1e-6f ? delta / dl * stepA : delta;
        VelocityMps.x = vel.x;
        VelocityMps.z = vel.y;
    }

    // =====================================================================
    // Grappling hook (task 100)
    // =====================================================================

    /// Where the hook is fired from: the eye (0.2 m under the top of the body,
    /// PlayerController's 1.6 m for the 1.8 m body).
    public float3 HookOriginM => PositionM + new float3(0f, Config.bodyHeightM - 0.2f, 0f);
    /// Where the rope holds the body: the hands, 3/4 of the body up.
    public float3 RopeAttachM => PositionM + new float3(0f, Config.bodyHeightM * 0.75f, 0f);

    /// The hook's ray: the first SOLID voxel within rangeM along dir. Air and
    /// fluid are passed; an unloaded chunk (below the generated top) or
    /// nothing in range is a miss. `pointM` = the point on the face it
    /// entered (or the ray's end on a miss), `distM` its distance.
    public bool RayHook(float3 originM, float3 dir, float rangeM, out float3 pointM, out int3 voxel, out float distM)
    {
        dir = math.normalizesafe(dir, new float3(0f, 0f, 1f));
        var w = VoxelRayWalker.Create(originM, originM + dir * rangeM);
        while (w.MoveNext())
        {
            int3 v = w.Voxel;
            if (VoxelCollision.IsNonResident(_residency, v))
            {
                if (VoxelCollision.AboveGeneratedContent(v)) continue;   // sky the generator never fills
                break;                                                    // not loaded: a miss, never an anchor
            }
            byte m = _world.GetVoxel(v);
            if (m == Materials.Air || MaterialRules.IsFluidMaterial(m)) continue;
            voxel = v;
            distM = w.TEnter;
            pointM = originM + dir * w.TEnter;
            return true;
        }
        voxel = default;
        distM = rangeM;
        pointM = originM + dir * rangeM;
        return false;
    }

    /// Fires the hook from HookOriginM along `lookDir` (re-aims if hooked).
    /// Returns whether it bit.
    public bool FireHook(float3 lookDir)
    {
        ReleaseHook();
        LastHookReleased = false;
        LastHookFired = true;
        PlayerConfig c = Config;
        if (c.hookRangeM <= 0f) return false;
        if (!RayHook(HookOriginM, lookDir, c.hookRangeM, out float3 p, out int3 v, out _)) return false;
        Hooked = true;
        HookAnchorM = p;
        HookVoxel = v;
        RopeLengthM = math.max(c.hookMinLengthM, math.length(p - RopeAttachM));
        HookArrived = false;
        LastHookHit = true;
        return true;
    }

    public void ReleaseHook()
    {
        if (Hooked) LastHookReleased = true;
        Hooked = Reeling = HookArrived = false;
    }

    /// The anchor voxel is still loaded and solid (digging it out drops you).
    private bool AnchorHolds()
        => !VoxelCollision.IsNonResident(_residency, HookVoxel) && IsBlocking(HookVoxel);

    /// One reeling frame: toward the anchor at hookReelSpeedMps, no gravity,
    /// never closer than hookMinLengthM; a wall in the way stops it there.
    private void StepReel(float dt)
    {
        PlayerConfig c = Config;
        Reeling = true;
        Gliding = Sliding = false;
        float3 to = HookAnchorM - RopeAttachM;
        float dist = math.length(to);
        float room = dist - c.hookMinLengthM;
        if (room <= 1e-4f) { VelocityMps = float3.zero; HookArrived = true; RopeLengthM = math.max(c.hookMinLengthM, dist); return; }
        float3 dir = to / dist;

        float3 dv = dir * c.hookReelSpeedMps - VelocityMps;
        float dl = math.length(dv), a = c.hookReelAccelMps2 * dt;
        VelocityMps += dl > a && dl > 1e-6f ? dv / dl * a : dv;

        float3 delta = VelocityMps * dt;
        float along = math.dot(delta, dir);
        if (along > room) delta *= room / along;      // stop exactly at the minimum length
        float3 before = PositionM;
        MoveWithCollision(delta);
        float want = math.length(delta), moved = math.length(PositionM - before);
        float nd = math.length(HookAnchorM - RopeAttachM);
        // Arrived: at the minimum length, or terrain took 90% of the pull (a
        // face beside the anchor still lets the body slide up it first).
        HookArrived = nd <= c.hookMinLengthM + 0.01f || (want > 1e-6f && moved < 0.1f * want);
        if (HookArrived) VelocityMps = float3.zero;   // the pull stops at the wall
        RopeLengthM = math.min(RopeLengthM, math.max(c.hookMinLengthM, nd));
    }

    /// One swinging frame (hooked, airborne, not reeling): gravity, the keys
    /// pump, the rope holds; a landing ends the hook.
    private void StepSwing(float dt, in MotorInput input)
    {
        PlayerConfig c = Config;
        Sliding = false;
        float2 w = input.WishXZ;
        float wl = math.length(w);
        if (wl > 1f) w /= wl;
        VelocityMps.x += w.x * c.hookSwingAccelMps2 * dt;
        VelocityMps.z += w.y * c.hookSwingAccelMps2 * dt;
        VelocityMps.y -= c.gravityMps2 * dt;
        if (VelocityMps.y < -c.terminalSpeedMps) VelocityMps.y = -c.terminalSpeedMps;
        MoveWithCollision(VelocityMps * dt);
        ApplyRope();
        UpdateGrounded(dt);
        if (Grounded) ReleaseHook();
    }

    /// The rope is inextensible: past RopeLengthM the body is moved back onto
    /// the sphere (through the collision move) and the outward velocity dropped.
    private void ApplyRope()
    {
        float3 d = RopeAttachM - HookAnchorM;
        float dist = math.length(d);
        if (dist <= RopeLengthM || dist < 1e-5f) return;
        float3 n = d / dist;
        MoveWithCollision(-n * (dist - RopeLengthM));
        float vr = math.dot(VelocityMps, n);
        if (vr > 0f) VelocityMps -= n * vr;

        // Terrain refused part of that (a body hanging against the face the
        // anchor is in: the correction points INTO the wall): close the rest
        // vertically, onto the sphere above/below where the body now is.
        d = RopeAttachM - HookAnchorM;
        float L2 = RopeLengthM * RopeLengthM, h2 = math.lengthsq(d.xz);
        if (math.length(d) <= RopeLengthM + 1e-3f || h2 >= L2) return;   // free swing: float residue only
        bool below = d.y < 0f;
        float ty = HookAnchorM.y + (below ? -1f : 1f) * math.sqrt(L2 - h2);
        MoveWithCollision(new float3(0f, ty - RopeAttachM.y, 0f));
        if (below ? VelocityMps.y < 0f : VelocityMps.y > 0f) VelocityMps.y = 0f;
    }

    private static float MoveToward(float v, float target, float maxDelta)
        => math.abs(target - v) <= maxDelta ? target : v + math.sign(target - v) * maxDelta;

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
            _hasLastWall = false;
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

        if (axis == 1)
        {
            if (amount < 0f) LastImpactDownMps = math.max(LastImpactDownMps, -VelocityMps.y);
            VelocityMps.y = 0f;
        }
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
        // A climber may too: that is how the top of a face is mantled.
        if (!Grounded && TimeSinceGroundedS > Config.coyoteTimeSeconds && !Swimming && !Climbing) return;

        // Task 93f1: the WALK BAND. With abilities an auto-step is taken only
        // when the next slopeBaselineM rises no more than walkSlopeMaxDeg;
        // the legacy path keeps the old step-up everywhere.
        if (_abilityStep)
        {
            int walkMax = WalkMaxRiseVoxels;
            int dir = amount > 0f ? 1 : -1;
            float lead = before[axis] + dir * Config.bodyWidthM * 0.5f + amount;
            int rise = RiseAheadVoxels(before, axis, dir, lead, walkMax + 1);
            LastRiseVoxels = rise;
            if (rise > walkMax) { LastSteepRefused = true; return; }
        }

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
        if (_abilityStep)
        {
            // Task 93f1: settle in WHOLE ROWS, each candidate at row * 0.1 m +
            // SkinM. Repeated 0.1 m float subtractions from feet sitting exactly
            // on a row (no skin: after Teleport/ResolveSpawn) land a hair under
            // the row, read the step itself as an overlap, stop a row high and
            // refuse a valid step -- the legacy path below still does that, and
            // stalls on long ramps; rigs keep it.
            int top = (int)math.floor(liftedMoved.y * 10f + 1e-3f);
            for (int r = top; r >= top - Config.stepHeightVoxels; r--)
            {
                float3 down = liftedMoved;
                down.y = r * VoxelSizeM + SkinM;
                if (OverlapsSolid(down)) break;
                settled = down;
            }
        }
        else
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
    // Wall-run and wall-jump (task 103)
    // =====================================================================

    /// A wall counts within this reach of the body's side (a flush body is
    /// 0.0001 m off it; a run pressing along a rough cliff opens a column).
    private const float WallReachM = 0.15f;
    /// While running, the body is pressed into the wall this fast so contact
    /// holds; the collision stops it at the face.
    private const float WallPressMps = 1f;
    /// The wall must block the column beside the body at these heights above
    /// the feet (voxels): hip and chest, so a kerb or a fence is not a wall.
    private const int WallLowVoxels = 5, WallHighVoxels = 12;

    /// Is there a wall on the body's `axis`/`dir` side within WallReachM?
    /// `plane` = the column it was found in (the wall's identity with axis, dir).
    public bool WallBeside(int axis, int dir, out int plane)
    {
        int other = axis == 0 ? 2 : 0;
        float half = Config.bodyWidthM * 0.5f;
        int y0 = CoordMath.WorldToVoxel(PositionM).y;
        int cNear = (int)math.floor((PositionM[axis] + dir * (half + 0.011f)) * 10f);
        int cFar = (int)math.floor((PositionM[axis] + dir * (half + WallReachM)) * 10f);
        int p = (int)math.floor(PositionM[other] * 10f);
        for (int c = cNear; ; c += dir)
        {
            int3 v = int3.zero;
            v[axis] = c;
            v[other] = p;
            v.y = y0 + WallLowVoxels;
            bool low = IsBlocking(v);
            v.y = y0 + WallHighVoxels;
            if (low && IsBlocking(v)) { plane = c; return true; }
            if (c == cFar) break;
        }
        plane = 0;
        return false;
    }

    private static int3 WallId(int axis, int dir, int plane) => new int3(axis, dir, plane);

    /// The same wall as the last one used this airtime: same side, its plane
    /// within 3 voxels (a natural cliff's face wanders by a column or two).
    private bool IsLastWall(int axis, int dir, int plane)
        => _hasLastWall && _lastWall.x == axis && _lastWall.y == dir && math.abs(_lastWall.z - plane) <= 3;

    /// Start a run: airborne, near the top of a jump or falling slowly, moving
    /// along a wall beside the body at wallRunMinSpeedMps or more with the keys
    /// along it (>= 0.5) and not pulling away. A press INTO a face stays the
    /// climb's (TryStartClimb runs first); a held glide stays a glide.
    private void TryStartWallRun(in MotorInput input, bool jumpPressed)
    {
        PlayerConfig c = Config;
        if (c.wallRunSpeedMps <= 0f || c.wallRunMaxS <= 0f || StaminaS <= 0f || jumpPressed) return;
        if (Grounded || Swimming || Climbing || Dashing || Hooked || Sliding) return;
        if (VelocityMps.y > c.wallRunStartUpMps || VelocityMps.y < -c.wallRunMaxEntryFallMps) return;
        if (CanGlide(input, jumpPressed)) return;
        float2 w = input.WishXZ;
        float wl = math.length(w);
        if (wl < 0.3f) return;
        float2 wd = w / wl;
        for (int axis = 0; axis <= 2; axis += 2)
        {
            // A wall across `axis` is run along the other horizontal axis.
            float vAlong = axis == 0 ? VelocityMps.z : VelocityMps.x;
            if (math.abs(vAlong) < c.wallRunMinSpeedMps) continue;
            float wAlong = (axis == 0 ? wd.y : wd.x) * math.sign(vAlong);
            if (wAlong < 0.5f) continue;
            for (int dir = -1; dir <= 1; dir += 2)
            {
                if ((axis == 0 ? wd.x : wd.y) * dir < -0.3f) continue;   // keys pull away from it
                if (!WallBeside(axis, dir, out int plane) || IsLastWall(axis, dir, plane)) continue;
                WallRunning = true;
                LastWallRunStarted = true;
                WallDir = axis == 0 ? new int2(dir, 0) : new int2(0, dir);
                WallRunTimeS = 0f;
                _lastWall = WallId(axis, dir, plane);
                _hasLastWall = true;
                _climbHoldS = 0f;
                VelocityMps.y = c.wallRunStartUpMps;
                if (axis == 0) VelocityMps.x = 0f; else VelocityMps.z = 0f;
                return;
            }
        }
    }

    /// One wall-running frame. Returns false when the run ended this frame
    /// (wall gone, time or stamina out, keys released / turned into the wall
    /// / pulled away, Ctrl, or stopped by something ahead): the caller then
    /// runs the walk physics. Space jumps off the wall.
    private bool StepWallRun(float dt, in MotorInput input, bool jumpPressed)
    {
        PlayerConfig c = Config;
        int axis = WallDir.x != 0 ? 0 : 2;
        int dir = WallDir.x != 0 ? WallDir.x : WallDir.y;
        int alongAxis = axis == 0 ? 2 : 0;
        float2 n = new float2(WallDir.x, WallDir.y);
        float2 along = axis == 0 ? new float2(0f, 1f) : new float2(1f, 0f);
        float vA = math.dot(VelocityMps.xz, along);
        float sgn = vA >= 0f ? 1f : -1f;

        if (jumpPressed)
        {
            WallJump(axis, dir, _lastWall.z);
            MoveWithCollision(VelocityMps * dt);
            UpdateGrounded(dt);
            return true;
        }

        float2 w = input.WishXZ;
        float wl = math.length(w);
        if (wl > 1f) w /= wl;
        if (input.DownHeld || StaminaS <= 0f || WallRunTimeS >= c.wallRunMaxS
            || math.dot(w, along) * sgn < 0.2f || math.dot(w, n) >= 0.8f || math.dot(w, n) < -0.6f
            || math.abs(vA) < 0.5f * c.wallRunMinSpeedMps || !WallBeside(axis, dir, out _))
        {
            WallRunning = false;
            return false;
        }

        vA = sgn * MoveToward(math.abs(vA), c.wallRunSpeedMps, c.wallRunAccelMps2 * dt);
        float vy = math.max(VelocityMps.y - c.gravityMps2 * c.wallRunGravityScale * dt, -c.wallRunMaxSinkMps);
        float2 hv = along * vA + n * WallPressMps;
        VelocityMps = new float3(hv.x, vy, hv.y);
        float before = PositionM[alongAxis];
        MoveWithCollision(VelocityMps * dt);
        float moved = math.abs(PositionM[alongAxis] - before);
        // Only the run's own speed is kept: leaving the wall carries no push into it.
        if (axis == 0) VelocityMps.x = 0f; else VelocityMps.z = 0f;
        StaminaS = math.max(0f, StaminaS - dt);
        WallRunTimeS += dt;
        Resting = false;
        UpdateGrounded(dt);
        if (Grounded) WallRunning = false;
        else if (moved < 0.25f * math.abs(vA) * dt)
        {
            // Stopped by something ahead on the wall: the run ends, no speed kept along it.
            WallRunning = false;
            if (alongAxis == 0) VelocityMps.x = 0f; else VelocityMps.z = 0f;
        }
        return true;
    }

    /// Kick off the wall on `axis`/`dir` (dir points INTO it): pushed out of
    /// its face at wallJumpPushMps, up at jumpImpulseMps x wallJumpUpScale;
    /// the speed along the wall is kept.
    private void WallJump(int axis, int dir, int plane)
    {
        PlayerConfig c = Config;
        float2 n = axis == 0 ? new float2(dir, 0f) : new float2(0f, dir);
        float2 along = axis == 0 ? new float2(0f, 1f) : new float2(1f, 0f);
        float2 hv = along * math.dot(VelocityMps.xz, along) - n * c.wallJumpPushMps;
        VelocityMps = new float3(hv.x, c.jumpImpulseMps * c.wallJumpUpScale, hv.y);
        StaminaS = math.max(0f, StaminaS - c.wallJumpStaminaS);
        WallRunning = Gliding = false;
        Grounded = false;
        TimeSinceGroundedS = c.coyoteTimeSeconds + 1f;
        _lastWall = WallId(axis, dir, plane);
        _hasLastWall = true;
        _climbHoldS = 0f;
        LastWallJump = true;
    }

    /// Space in the air (not a ground / coyote jump): is there a wall to kick
    /// off? The side the keys press into most is tried first; the wall used
    /// last this airtime is skipped. Jumps and returns true if one was found.
    private bool TryWallJump(in MotorInput input)
    {
        PlayerConfig c = Config;
        if (c.wallJumpPushMps <= 0f || StaminaS <= 0f || Grounded || Swimming) return false;
        if (!FindWall(input.WishXZ, out int axis, out int dir, out int plane)) return false;
        WallJump(axis, dir, plane);
        return true;
    }

    /// A wall-jump is possible now (the HUD's hint): airborne, stamina left
    /// and a usable wall beside the body.
    public bool WallJumpReady
        => Config.wallJumpPushMps > 0f && StaminaS > 0f && !Grounded && !Swimming && !Climbing
           && FindWall(float2.zero, out _, out _, out _);

    private bool FindWall(float2 wish, out int axis, out int dir, out int plane)
    {
        axis = dir = plane = 0;
        float best = float.NegativeInfinity;
        bool found = false;
        for (int i = 0; i < 4; i++)
        {
            int a = i < 2 ? 0 : 2, d = (i & 1) == 0 ? 1 : -1;
            float into = (a == 0 ? wish.x : wish.y) * d;
            if (into <= best) continue;
            if (!WallBeside(a, d, out int p) || IsLastWall(a, d, p)) continue;
            best = into; axis = a; dir = d; plane = p; found = true;
        }
        return found;
    }

    // =====================================================================
    // Slopes and climbing (task 93f1)
    // =====================================================================

    /// Baseline in whole columns (0.5 m = 5).
    public int BaselineColumns => math.max(1, (int)math.round(Config.slopeBaselineM / VoxelSizeM));

    /// The most the ground may rise over the baseline for an auto-step
    /// (30 deg over 0.5 m: 2.89 -> 3 voxels).
    public int WalkMaxRiseVoxels => RiseVoxelsFor(Config.walkSlopeMaxDeg);

    /// The least a face must rise over the baseline to be climbed
    /// (60 deg over 0.5 m: 8.66 -> 9 voxels).
    public int ClimbMinRiseVoxels => math.max(WalkMaxRiseVoxels + 1, RiseVoxelsFor(Config.climbMinSlopeDeg));

    private int RiseVoxelsFor(float deg)
        => (int)math.min(64f, math.floor(math.tan(math.radians(math.clamp(deg, 0f, 89f)))
                                          * BaselineColumns + 0.5f));

    /// How far the ground rises above the feet over the baseline ahead, in
    /// whole voxels, capped at `cap`: the highest solid run starting at the
    /// feet row, over BaselineColumns columns from the one holding `leadM`
    /// (a coordinate on `axis`, just past the body's leading face), on three
    /// lines across the body (both sides and the centre).
    public int RiseAheadVoxels(float3 feetM, int axis, int dir, float leadM, int cap)
    {
        int other = axis == 0 ? 2 : 0;
        int y0 = CoordMath.WorldToVoxel(feetM).y;
        int c0 = (int)math.floor(leadM * 10f);
        float inset = Config.bodyWidthM * 0.5f - 0.02f;
        int n = BaselineColumns;
        int best = 0;
        for (int line = -1; line <= 1 && best < cap; line++)
        {
            int p = (int)math.floor((feetM[other] + line * inset) * 10f);
            for (int k = 0; k < n && best < cap; k++)
            {
                int3 v = int3.zero;
                v[axis] = c0 + dir * k;
                v[other] = p;
                int r = 0;
                while (r < cap)
                {
                    v.y = y0 + r;
                    if (!IsBlocking(v)) break;
                    r++;
                }
                if (r > best) best = r;
            }
        }
        return best;
    }

    /// The body's face on `axis`/`dir` has something blocking within `reachM`
    /// (default: flush against it).
    public bool TouchingFace(int axis, int dir, float reachM = 0.011f)
    {
        float3 p = PositionM;
        p[axis] += dir * reachM;
        return OverlapsSolid(p);
    }

    /// A clinging body keeps its grip while the face is within this reach: a
    /// face that leans back (70 deg) opens a gap of up to a column between steps.
    private const float ClimbReachM = 0.15f;

    private float LeadOf(int axis, int dir) => PositionM[axis] + dir * (Config.bodyWidthM * 0.5f + 0.011f);

    /// The material of the face ahead, chest high down to the feet, centre line first.
    private byte FaceMaterial(int axis, int dir)
    {
        int other = axis == 0 ? 2 : 0;
        int y0 = CoordMath.WorldToVoxel(PositionM).y;
        int c0 = (int)math.floor(LeadOf(axis, dir) * 10f);
        float inset = Config.bodyWidthM * 0.5f - 0.02f;
        for (int i = 0; i < 3; i++)
        {
            int line = i == 0 ? 0 : i == 1 ? -1 : 1;
            int3 v = int3.zero;
            v[axis] = c0;
            v[other] = (int)math.floor((PositionM[other] + line * inset) * 10f);
            for (int r = 10; r >= 0; r--)
            {
                v.y = y0 + r;
                if (IsBlocking(v)) return _world.GetVoxel(v);
            }
        }
        return Materials.Air;
    }

    /// Cling when the keys have pressed into a Climb-band face for
    /// climbGrabDelayS. Either horizontal axis the wish leans into (>= 0.6 of it).
    private void TryStartClimb(float dt, in MotorInput input)
    {
        PlayerConfig c = Config;
        float2 w = input.WishXZ;
        float wl = math.length(w);
        if (StaminaS <= 0f || c.climbSpeedMps <= 0f || wl < 0.3f) { _climbHoldS = 0f; return; }
        float2 wd = w / wl;
        bool xFirst = math.abs(wd.x) >= math.abs(wd.y);
        int climbMin = ClimbMinRiseVoxels;
        for (int i = 0; i < 2; i++)
        {
            int axis = (i == 0) == xFirst ? 0 : 2;
            float comp = axis == 0 ? wd.x : wd.y;
            if (math.abs(comp) < 0.6f) continue;
            int dir = comp > 0f ? 1 : -1;
            if (!TouchingFace(axis, dir)) continue;
            if (RiseAheadVoxels(PositionM, axis, dir, LeadOf(axis, dir), climbMin) < climbMin) continue;
            _climbHoldS += dt;
            if (_climbHoldS < c.climbGrabDelayS) return;
            Climbing = true;
            Sliding = false;
            ClimbDir = axis == 0 ? new int2(dir, 0) : new int2(0, dir);
            ClimbFace = FaceMaterial(axis, dir);
            ClimbGrip = MaterialRules.ClimbGrip(ClimbFace);
            VelocityMps = float3.zero;
            _climbHoldS = 0f;
            return;
        }
        _climbHoldS = 0f;
    }

    public float ClimbSpeedScale => ClimbGrip == MaterialRules.GripLoose ? Config.climbLooseSpeedScale
                                  : ClimbGrip == MaterialRules.GripSlick ? Config.climbSlickSpeedScale : 1f;
    public float ClimbDrainScale => ClimbGrip == MaterialRules.GripLoose ? Config.climbLooseDrainScale
                                  : ClimbGrip == MaterialRules.GripSlick ? Config.climbSlickDrainScale : 1f;

    /// One climbing frame. Returns false when the climber let go (keys no
    /// longer into the face, Space, or stamina out): the caller then runs
    /// the walk physics for this frame.
    private bool StepClimb(float dt, in MotorInput input)
    {
        PlayerConfig c = Config;
        float2 d = new float2(ClimbDir.x, ClimbDir.y);
        float2 perp = new float2(-d.y, d.x);
        float2 w = input.WishXZ;
        float wl = math.length(w);
        if (wl > 1f) w /= wl;
        if (math.dot(w, d) < 0.2f || input.JumpPressed || StaminaS <= 0f)
        {
            Climbing = false;
            return false;
        }

        // Up the face (Ctrl: down), strafe along it, and pressed into it so
        // contact holds on a face that leans back (a 70 deg slope recedes).
        float speed = c.climbSpeedMps * ClimbSpeedScale;
        float2 onWall = new float2(math.dot(w, perp), input.DownHeld ? -1f : 1f);
        float owl = math.length(onWall);
        if (owl > 1f) onWall /= owl;
        float2 hv = d * speed + perp * (onWall.x * speed);
        VelocityMps = new float3(hv.x, onWall.y * speed, hv.y);
        MoveWithCollision(VelocityMps * dt);
        StaminaS = math.max(0f, StaminaS - dt * ClimbDrainScale);

        bool mantled = LastStepUpApplied;
        UpdateGrounded(dt);
        int axis = ClimbDir.x != 0 ? 0 : 2;
        int dir = ClimbDir.x != 0 ? ClimbDir.x : ClimbDir.y;
        if (mantled || (Grounded && input.DownHeld) || !TouchingFace(axis, dir, ClimbReachM))
        {
            // Over the top (the step-up stood us on it), down at the foot,
            // or the face ended: walking resumes next frame.
            Climbing = false;
            VelocityMps = new float3(0f, math.min(VelocityMps.y, 0f), 0f);
        }
        Resting = false;
        return true;
    }

    /// Ground under the feet that falls away CONTINUOUSLY (no column-to-column
    /// jump over MaxSlopeColumnJump voxels) by more than the walk band over the
    /// baseline, in any axis direction from the body's centre column: slide
    /// down it. A jump between columns is a ledge edge, which you may stand on.
    private const int MaxSlopeColumnJump = 3;   // = the 0.3 m step: a taller drop is a ledge

    private void UpdateSlide()
    {
        Sliding = false;
        if (!(Grounded || (TimeSinceGroundedS <= Config.coyoteTimeSeconds && VelocityMps.y <= 0f))) return;
        int walkMax = WalkMaxRiseVoxels;
        int n = BaselineColumns;
        int3 f = CoordMath.WorldToVoxel(PositionM);
        int depth = n * MaxSlopeColumnJump + MaxSlopeColumnJump;
        int h0 = TopBelow(f, depth);
        if (h0 == int.MinValue) return;                  // centre over a drop: a ledge edge
        float2 g = float2.zero;
        int best = 0;
        for (int i = 0; i < 4; i++)
        {
            int3 step = i == 0 ? new int3(1, 0, 0) : i == 1 ? new int3(-1, 0, 0) : i == 2 ? new int3(0, 0, 1) : new int3(0, 0, -1);
            int prev = h0;
            bool ok = true;
            for (int k = 1; k <= n && ok; k++)
            {
                int h = TopBelow(f + step * k, depth);
                if (h == int.MinValue || math.abs(prev - h) > MaxSlopeColumnJump) ok = false;
                else prev = h;
            }
            int drop = ok ? h0 - prev : 0;
            if (drop <= 0) continue;
            if (drop > best) best = drop;
            g += new float2(step.x, step.z) * drop;
        }
        if (best <= walkMax || math.lengthsq(g) < 1e-6f) return;
        Sliding = true;
        SlideDir = math.normalize(g);
    }

    /// The highest blocking row in the column at or below `v`'s feet row - 1,
    /// at most `depth` rows down; int.MinValue when there is none.
    private int TopBelow(int3 v, int depth)
    {
        for (int r = 1; r <= depth; r++)
        {
            int3 p = new int3(v.x, v.y - r, v.z);
            if (IsBlocking(p)) return p.y;
        }
        return int.MinValue;
    }

    /// Stamina refills while RESTING: grounded, not sliding or climbing, and
    /// the whole footprint supported (every corner has ground within the step
    /// height below) -- the brief's 0.6 x 0.6 m rest ledge.
    private void UpdateRest(float dt)
    {
        Resting = Grounded && !Sliding && !Climbing && !Swimming && !WallRunning && FootprintSupported();
        if (Resting) StaminaS = math.min(Config.climbStaminaS, StaminaS + Config.climbStaminaRefillPerS * dt);
    }

    public bool FootprintSupported()
    {
        float h = Config.bodyWidthM * 0.5f - 0.01f;
        int depth = math.max(1, Config.stepHeightVoxels);
        for (int i = 0; i < 4; i++)
        {
            float3 p = PositionM + new float3((i & 1) == 0 ? -h : h, 0f, (i & 2) == 0 ? -h : h);
            if (TopBelow(CoordMath.WorldToVoxel(p), depth) == int.MinValue) return false;
        }
        return true;
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
        Climbing = Sliding = Resting = Gliding = WallRunning = false;
        _hasLastWall = false;
        WallRunTimeS = 0f;
        ReleaseHook();
        _climbHoldS = 0f;
        StaminaS = Config.climbStaminaS;
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
