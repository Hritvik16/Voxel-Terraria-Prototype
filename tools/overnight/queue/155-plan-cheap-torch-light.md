# 155-plan-cheap-torch-light

Model: opus

Approval: owner, 2026-10-30, doc Q99 answer (a): keep the shipped 2.9 m torch reach now, and design a cheaper torch light. PLAN ONLY in
this task: no product code, no default/look change. Not a performance task (no timing arms; cost estimates from existing numbers are fine).

## Goal (the look the owner asked for)
Torches that light 10-15 m (task 143's "10-15 m" column) without today's cost. Task 151: today every pixel sums every torch in range with
a shadow ray each, every frame; 32 torches at 10-15 m cost +15 to +23 ms (range-14 variant +3 to +7), the shipped 2.9 m room arm ~0, and
today's torches already cost +5.7 / +8.3 ms vs none. ROADMAP E9 wants "at least Terraria-in-3D": effectively many lights, far lights later.

## Read first
143 / 151 HANDOFFs and `Screenshots/151/torchcost151.txt`, `LightingConfig.json` + its loader, the point-light path in `Raymarch.compute`,
Phase 7 docs (`PHASE_7_PLAN.md`, `AMENDMENT_8_12_PHASE7_GATE.md`, §10 flags incl. torch-through-wall), `SOFT_SKY_PLAN.md` (a sky-light
design may share a volume or cache with this one), KNOWN_ISSUES "EMISSIVE + MOBILE MATERIALS MUST NOT REGISTER AS POINT LIGHTS".
Generality rule (owner, 2026-10-24): no terrain-specific constants; must hold for any generator output, player builds and edits.
Hard invariants: CPU is the sole terrain writer; single-writer; power-of-two windows; frozen APIs (flag any change as a redesign).

## Do
Write `TORCH_LIGHT_PLAN.md` (short): 2-3 candidate designs (e.g. a light volume flood-filled through the voxel grid when a torch is placed
or a block changes, Terraria-style, sampled per pixel; a per-brick / clustered light list with cached shadow visibility; a coarse light
volume from the shells), each with: how it looks (reach, wall leaks, hard vs soft, colour), GPU/CPU memory against the 3 GB cap, estimated
ms at 540 rows with 32 / 200 torches, update cost on edits and streaming (no pops at the 0.1 m window edge), lava and other emissives,
what it needs from frozen APIs or layouts (flag it), and test/oracle changes. Pick ONE recommendation and a step list (one change per
task, each with a look check + one measurement). Include 4-6 SPECIFIC owner questions with a recommended answer each (e.g. may light pass
round corners; how many torches must a base hold; coloured light).

## Deliverable
`TORCH_LIGHT_PLAN.md` committed; HANDOFF starting with STOP_FOR_OWNER listing the questions (recommendation first), with one picture per
question where an existing shot (143's sheet) explains it. No new launches required.
