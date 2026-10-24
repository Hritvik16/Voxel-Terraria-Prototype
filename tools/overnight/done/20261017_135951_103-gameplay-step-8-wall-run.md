# 103-gameplay-step-8-wall-run

Requires: 93f1-gameplay-step-5

Approval: owner, 2026-10-11 ~00:10 in chat: "keep queuing tasks, ideally moving forward with gameplay features
instead of optimizations, pick your recommendations, i can always queue something to change them". Queued by the
supervisor 2026-10-11. Branch `engine-phase`.

## Change
Implement **step 8** of `GAMEPLAY_PHASE_PLAN.md` (written by task 63; steps 1-3 = tasks 64-66), exactly as its step table describes, and
nothing else. Read the plan's guardrails and picks first. Where the step leaves a look-and-feel or tuning choice,
pick the recommended option yourself, record it in HANDOFF under "Picks (owner may change)", and carry on: the owner
delegated these. STOP_FOR_OWNER only for: a frozen-API change (GetVoxel, SetVoxel, D.1, the 4.4 table), a
memory-layout change, anything on `main`, or a step that turns out not to fit one task (then do the first useful
half, commit it, and say what remains).

## Acceptance
- The step's own acceptance from the plan table.
- Fast EditMode suite green; `SUITE=all` if anything under `Assets/TerrainGen/` changed.
- No frame-time measurement (owner rule: features over measurement).
- Screenshots of the feature in play (well lit: midday, sun behind the camera), paths in HANDOFF, each with a one-line
  caption of what to look at. Update the F2 help panel if a key was added.
- HANDOFF `## For the doc`: one-line result, what the owner can now do in `./run-play.sh` (keys), the picks, the
  images.

## Queued (supervisor, 2026-10-17 13:5x)
Copied from `later/71-gameplay-step-8.md` (original stays there; its `Requires: 68` is now 93f1, the task that did step 5).
Step 8 = **wall-run and wall-jump** (`GAMEPLAY_PHASE_PLAN.md` row 8). Steps 4-7 and 9a are in (93c1, 93f1 climbing, 93g1 glide,
100 grappling hook, 93d1 health): put it on `PlayerMotor`'s ABILITIES path beside them (rigs keep the legacy Step), numbers in
PlayerConfig.json, and make it hand over cleanly with climb (a slow press into a >= 60 deg face still clings), glide, hook,
dash, swim and vitals. Demo shots: one launch, few poses, short waits (PREAMBLE launch budget).
