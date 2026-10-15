# 93d1-gameplay-step-9a

Requires: 65-gameplay-step-2 93c1-gameplay-step-4

Approval: owner, 2026-10-11 (gameplay steps, "pick your recommendations"); step 9 split into 9a + 9b by the
2026-10-15 design round (task 92, `7107b4f`, P22); Q4 (a) alternate with engine tasks (2026-10-15). Replaces
`later/72-gameplay-step-9` (which would have built "keep inventory on death" -- do NOT). Queued by the supervisor
2026-10-15. Branch `engine-phase`.

## Change
Implement **9a** of `GAMEPLAY_PHASE_PLAN.md` (health, fall damage, oxygen while diving, lava/acid damage, respawn at
the start; SOFT death only; minimal bar HUD), exactly as its row describes, and nothing else. NOT 9b (death mode per
world): its classic rule is open until the object-placement phase (owner, 2026-10-15); hardcore = (a) world locks.
Read the plan's §2 guardrails first. Where the step leaves a look-and-feel or tuning choice, pick the
recommended option yourself, record it in HANDOFF under "Picks (owner may change)", and carry on. STOP_FOR_OWNER only
for: a frozen-API change (GetVoxel, SetVoxel, D.1, the 4.4 table), a memory-layout change, anything on `main`, or a step
that turns out not to fit one task (then do the first useful half, commit it, and say what remains).

## Acceptance
- The step's own acceptance from the plan table.
- Fast EditMode suite green; `SUITE=all` if anything under `Assets/TerrainGen/` changed.
- No frame-time measurement (owner rule: features over measurement).
- Screenshots of the feature in play (well lit: midday, sun behind the camera), paths in HANDOFF, each with a one-line
  caption of what to look at. Update the F2 help panel if a key was added.
- HANDOFF `## For the doc`: one-line result, what the owner can now do in `./run-play.sh` (keys), the picks, the
  images.
