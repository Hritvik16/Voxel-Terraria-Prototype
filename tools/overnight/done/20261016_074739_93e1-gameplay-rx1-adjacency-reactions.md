# 93e1-gameplay-rx1-adjacency-reactions

Requires: 63-gameplay-phase-plan

Approval: owner, 2026-10-15 design round (reactions batch 1, recorded by task 92 `7107b4f`, row RX1 of
`GAMEPLAY_PHASE_PLAN.md`); Q4 (a) alternate with engine tasks (2026-10-15). Queued by the supervisor 2026-10-15.
Branch `engine-phase`.

## Change
Implement **RX1 (adjacency reactions, table rows only)** of `GAMEPLAY_PHASE_PLAN.md`, exactly as its row describes
(water + lava -> stone + steam; water + loam/dirt -> mud; sand + lava -> glass, a new material; snow/ice + lava ->
water), in `MaterialRules`' existing pair table, and nothing else. The CA is NOT redesigned. Count free material ids
first (plan §6 risk): if the 1-byte id space cannot fit the new materials, STOP_FOR_OWNER (guardrail 3).
Read the plan's §2 guardrails first. Where the step leaves a look-and-feel or tuning choice, pick the
recommended option yourself, record it in HANDOFF under "Picks (owner may change)", and carry on. STOP_FOR_OWNER only
for: a frozen-API change (GetVoxel, SetVoxel, D.1, the 4.4 table), a memory-layout change, anything on `main`, or a step
that turns out not to fit one task (then do the first useful half, commit it, and say what remains).

## Acceptance
- One EditMode test per pair (CPU reference `FluidReferenceCPU` and the GPU path agree, as today's water+lava tests do);
  conservation tests still green.
- Fast EditMode suite green; `SUITE=all` if anything under `Assets/TerrainGen/` changed. No frame-time measurement.
- One screenshot per pair in play (midday, sun behind the camera), each captioned with what to look at.
- HANDOFF `## For the doc`: one-line result, the new materials and ids, the picks, the images.
