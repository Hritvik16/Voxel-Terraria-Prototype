// ==========================================
// Assets/CoreEngine/Gameplay/PlayerVitals.cs
//
// Task 93d1 (GAMEPLAY_PHASE_PLAN.md step 9a): health, fall damage, oxygen
// while diving, lava/acid damage, soft death. A plain class like PlayerMotor:
// no Unity lifecycle, so EditMode proves the curves on a synthetic world.
//
// WHO TICKS IT: Playground's interactive Survival walk, once per motor step
// (Sense() after the step, then Step()). Rigs, capture runs and Creative never
// create one, so nothing they measure changes (plan §2 guardrail 5).
//
// SOFT DEATH (P22's "soft = respawn, keep everything"): at 0 HP the body is
// Dead for respawnDelayS, then the caller moves it to the play start and calls
// Respawn(). Nothing here touches the inventory -- that is the "keep
// everything". 9b (death mode per world) is NOT this task.
//
// The motor knows nothing about damage; it only reports LastImpactDownMps.

using Unity.Mathematics;

public enum DamageCause : byte
{
    None = 0,
    Fall = 1,
    Drowning = 2,
    Lava = 3,
    Acid = 4,
}

/// What the body touched this step. Built by PlayerVitals.Sense from the
/// world, or by hand in tests.
public struct VitalsInput
{
    /// Downward speed at which the feet hit a solid this step (PlayerMotor.LastImpactDownMps).
    public float ImpactDownMps;
    /// A liquid (not a gas) at the feet probe: cushions a landing.
    public bool FeetInLiquid;
    /// A liquid at the head probe: no breathing.
    public bool HeadInLiquid;
    public bool TouchingLava;
    public bool TouchingAcid;
}

public sealed class PlayerVitals
{
    public float Health;
    public float OxygenS;
    public bool Dead;
    /// Seconds since death (counts while Dead).
    public float DeadForS;
    /// Seconds since the last damage (regen waits regenDelayS).
    public float SinceDamageS;
    /// The last thing that did damage, how much its last hit was, and how long
    /// ago (for the HUD's flash and the death screen).
    public DamageCause LastCause;
    public float LastHit;
    public float LastHitAgeS = 999f;
    /// Damage taken this step (summed over causes).
    public float DamageThisStep;
    public int Deaths;
    /// The height the last damaging fall was worth, metres (the death screen says it).
    public float LastFallHeightM;

    public PlayerVitals(PlayerConfig c) { Respawn(c); }

    public bool RespawnDue(PlayerConfig c) => Dead && DeadForS >= c.respawnDelayS;

    /// Full health and breath, alive again. The caller moves the body.
    public void Respawn(PlayerConfig c)
    {
        Health = c.maxHealth;
        OxygenS = c.oxygenSeconds;
        Dead = false;
        DeadForS = 0f;
        SinceDamageS = 999f;
        DamageThisStep = 0f;
    }

    /// The fall height a landing speed is worth, v^2 / 2g, metres.
    public static float FallHeightM(PlayerConfig c, float impactDownMps)
        => impactDownMps <= 0f ? 0f : impactDownMps * impactDownMps / (2f * c.gravityMps2);

    /// The landing speed a fall of `heightM` reaches (no drag), m/s.
    public static float ImpactSpeedMps(PlayerConfig c, float heightM)
        => math.sqrt(2f * c.gravityMps2 * math.max(0f, heightM));

    /// THE FALL CURVE: linear in the fall height, 0 at fallSafeHeightM, the
    /// whole of maxHealth at fallLethalHeightM, capped there.
    public static float FallDamage(PlayerConfig c, float impactDownMps)
    {
        float h = FallHeightM(c, impactDownMps);
        if (h <= c.fallSafeHeightM) return 0f;
        float k = (h - c.fallSafeHeightM) / math.max(0.1f, c.fallLethalHeightM - c.fallSafeHeightM);
        return c.maxHealth * math.min(1f, k);
    }

    /// One step. Damage, then breath, then regen; at 0 HP the body dies.
    public void Step(float dt, in VitalsInput i, PlayerConfig c)
    {
        DamageThisStep = 0f;
        if (dt <= 0f) return;
        LastHitAgeS += dt;
        if (Dead) { DeadForS += dt; return; }

        // Fall: a landing with the feet in a liquid is cushioned.
        if (i.ImpactDownMps > 0f && !i.FeetInLiquid)
        {
            float fall = FallDamage(c, i.ImpactDownMps);
            if (fall > 0f) { LastFallHeightM = FallHeightM(c, i.ImpactDownMps); Hurt(fall, DamageCause.Fall); }
        }

        if (i.TouchingLava) Hurt(c.lavaDamagePerS * dt, DamageCause.Lava);
        if (i.TouchingAcid) Hurt(c.acidDamagePerS * dt, DamageCause.Acid);

        // Breath: drains in real seconds under a liquid, refills in air.
        if (i.HeadInLiquid)
        {
            // Drowning starts the moment the breath runs out (the part of dt
            // past it). dt - before, NOT dt - (before - after): that leaks a
            // float-rounding sliver of damage (and the HUD's red flash) every
            // step while the breath lasts.
            float drowning = dt - OxygenS;
            OxygenS = math.max(0f, OxygenS - dt);
            if (drowning > 0f) Hurt(c.drownDamagePerS * drowning, DamageCause.Drowning);
        }
        else OxygenS = math.min(c.oxygenSeconds, OxygenS + c.oxygenRefillPerS * dt);

        if (DamageThisStep > 0f) SinceDamageS = 0f;
        else
        {
            SinceDamageS += dt;
            if (SinceDamageS >= c.regenDelayS) Health = math.min(c.maxHealth, Health + c.regenPerS * dt);
        }

        if (Health <= 0f)
        {
            Health = 0f;
            Dead = true;
            DeadForS = 0f;
            Deaths++;
        }
    }

    private void Hurt(float amount, DamageCause cause)
    {
        if (amount <= 0f) return;
        Health -= amount;
        DamageThisStep += amount;
        LastCause = cause;
        LastHit = amount;
        LastHitAgeS = 0f;
    }

    // =====================================================================
    // Sensing: the same three probes Buoyancy and the motor use
    // =====================================================================

    /// What the body at `feetCentre` touches, after a motor step that reported
    /// `impactDownMps`. Non-resident reads dry (the Buoyancy rule).
    public static VitalsInput Sense(IWorldQuery world, IVoxelResidency residency, float3 feetCentre,
                                    float bodyHeightM, float impactDownMps)
    {
        var i = new VitalsInput { ImpactDownMps = impactDownMps };
        byte feet = Probe(world, residency, Buoyancy.ProbePosition(feetCentre, bodyHeightM, BuoyancyProbe.Feet));
        byte centre = Probe(world, residency, Buoyancy.ProbePosition(feetCentre, bodyHeightM, BuoyancyProbe.Center));
        byte head = Probe(world, residency, Buoyancy.ProbePosition(feetCentre, bodyHeightM, BuoyancyProbe.Head));
        i.FeetInLiquid = feet != Materials.Air;
        i.HeadInLiquid = head != Materials.Air;
        i.TouchingLava = feet == Materials.Lava || centre == Materials.Lava || head == Materials.Lava;
        i.TouchingAcid = feet == Materials.Acid || centre == Materials.Acid || head == Materials.Acid;
        return i;
    }

    /// The liquid at a point, or Air (dry, a gas, a solid or not loaded).
    private static byte Probe(IWorldQuery world, IVoxelResidency residency, float3 pointM)
    {
        if (!VoxelCollision.IsFluid(world, residency, CoordMath.WorldToVoxel(pointM), out byte m)) return Materials.Air;
        return MaterialRules.IsGasMaterial(m) ? Materials.Air : m;
    }

    // =====================================================================
    // Where a soft death goes
    // =====================================================================

    /// The respawn spot: the play start (TerrainGen.PlayStart, step 4) when
    /// this run has one, else the column where the body died (the old R key's
    /// "surface under you", for runs without a play start). Never the death
    /// pose itself when a start exists -- dying in lava must not respawn in it.
    public static float3 RespawnTarget(bool playStartActive, float3 playStartM, float3 deathFeetM)
        => playStartActive ? playStartM : deathFeetM;
}
