# 104-engine-q1-far-query-getvoxels

Requires: 101-engine-c1-world-clock

Approval: owner, 2026-10-15, Amendment 8.15 Q10 (a) (ADOPTED): owed part E1-E2, D1-D4, C1, **Q1**, Q2 in that order
(`AMENDMENT_8_15_SIMULATION_REACH.md` ~line 360). C1 is done (101). Queued by the supervisor 2026-10-17. Branch `engine-phase`.

## Change
Implement row **Q1** of the amendment exactly (§7, ~line 275): a NEW `IFarQuery.GetVoxels(NativeArray<int3> points,
NativeArray<byte> results)` as a Burst job, beside the frozen `IWorldQuery.GetVoxel` (unchanged). Per point: resident chunk
-> else the D.1 brick if that chunk's delta holds it (an LRU of parsed delta indices) -> else the generator (`DetailField`
column + `ShapeField`) with ops <= S, then ops > S (task 81's load order). **Exact:** equal to what the chunk would generate.
Raycast is Q2 (not here). No frozen signature, D.1 or 4.4 change; if one turns out to be needed, STOP_FOR_OWNER.
Single-writer rule: the job reads only immutable snapshots (ops `Snapshot`, a main-thread copy of what it needs from
resident chunks / the delta index) -- no locks.

## Acceptance
- EditMode tests (`FarQueryTests`): random points vs generated chunks (as `EditOpTests` does), incl. points under ops and
  in chunks with a D.1 delta, resident vs not resident, edges (chunk/brick borders, negative coords, above/below the world).
- Report the cost per point (a test-timing or report-only line is fine; NOT a frame-time measurement -- rule 10).
- Fast EditMode suite green; `SUITE=all` if anything under `Assets/TerrainGen/` changed.
- No screenshots (nothing visible); `## For the doc`: result, API, test counts, cost per point.
