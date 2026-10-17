# 105-gameplay-step-10-tool-tiers

Requires: 93c-shells-default-in-play 66-gameplay-step-3

Approval: owner, 2026-10-11 ~00:10 in chat: "keep queuing tasks, ideally moving forward with gameplay features instead of
optimizations, pick your recommendations"; gameplay alternates with engine tasks (Q50 (4)). Queued by the supervisor
2026-10-17. Branch `engine-phase`. (`Requires: 93c` only so the owner's shells switch runs first; no code dependency -- if
93c blocks, the supervisor drops it from this line.)

## Change
Implement **step 10** of `GAMEPLAY_PHASE_PLAN.md` ("Hardness and tool tiers as gear"), nothing else. The step was first
written (the plan's first version, `git log --diff-filter=A -- GAMEPLAY_PHASE_PLAN.md`) as: a per-material hardness table;
the dig rate comes from the held tool vs hardness; each ore (task 66: copper/iron/gold/cobalt/emberstone) needs a tool tier.
Acceptance then: EditMode rate table, "too hard" refusal, a screenshot of the refusal at an iron vein with a copper tool.
Where that conflicts with the CURRENT plan's picks/guardrails (e.g. P11: dev keys incl. dig tiers live in Survival), the
current plan wins; record it under "Picks (owner may change)". Where the step leaves a tuning or look choice, pick the
recommended option yourself. How the player GETS a better tool is crafting (step 11, not here): give Survival a starting
tool and a way to switch tiers that P11 allows, and say so. STOP_FOR_OWNER only for a frozen-API change, a memory-layout
change, anything on `main`, or a step that does not fit one task (then do the first useful half and say what remains).

## Acceptance
- The step's acceptance above; fast EditMode suite green (`SUITE=all` if anything under `Assets/TerrainGen/` changed).
- No frame-time measurement. Screenshots (one launch, few poses, short waits, midday with the sun behind the camera):
  the refusal at an iron vein with a copper tool, and digging the same vein with the right tier. Captions say what to look at.
  Update the F2 help panel if a key changed.
- HANDOFF `## For the doc`: result, what the owner can now do in `./run-play.sh`, the hardness/tier table, picks, images.
