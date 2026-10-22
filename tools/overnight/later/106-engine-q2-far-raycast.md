# 106-engine-q2-far-raycast

Requires: 104-engine-q1-far-query-getvoxels 93c-shells-default-in-play

Approval: owner, 2026-10-15, Amendment 8.15 Q10 (a) (ADOPTED): owed part E1-E2, D1-D4, C1, Q1, **Q2** in that order. Queued by
the supervisor 2026-10-17. Branch `engine-phase`. (`Requires: 93c` only for run order; no code dependency.)

## Change
Implement row **Q2** of `AMENDMENT_8_15_SIMULATION_REACH.md` (§7, ~line 281) exactly: `IFarQuery.Raycast(origin, dir, maxDist)`,
a coarse march over far-band columns + ops, refined with Q1's point queries near a hit (for spells and NPC sight beyond
128 m). Beside the frozen `GetVoxel` and Q1's `GetVoxels`; no frozen signature, D.1 or 4.4 change (else STOP_FOR_OWNER).
Same single-writer rule as Q1 (immutable snapshots, no locks). Fluid far away = generated water + FluidLevel ops, as §7 says.

## Acceptance
- EditMode tests (`FarRaycastTests`): rays vs a voxel-exact reference march over generated chunks (hit point / voxel / normal
  equal, or name the allowed tolerance and why), rays over ops (crater, mound), rays that miss, rays starting inside NEAR,
  long rays (> 1 km).
- Report the cost per ray (test timing / report line; NOT a frame-time measurement -- rule 10).
- Fast EditMode suite green. No screenshots (nothing visible). `## For the doc`: result, API, test counts, cost per ray.
