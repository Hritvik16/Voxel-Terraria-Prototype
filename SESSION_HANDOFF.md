# Session handoff -- 2026-10-01 (long design + engine session)

## State
- Branch `design-brief` (main = `3094e71`, the reviewed merge). Builds are PAUSED by
  the owner until they say go; `tools/overnight/PAUSE` exists (untracked).
- EditMode last run: PASS 626 FAIL 0 SKIP 2.
- `Assets/Scenes/Playground.unity` shows modified: scene-builder object ids only.

## Done today (commits on design-brief)
- Thick depth layers replace striped strata (12cef45); tg2 brick pool 1.0 M -> 750k
  (ada1669); walk steady state ~2.95-3.0 GB.
- 960 is the default preset; bilinear final blit (ed27d06).
- Terrain-aware vertical band placement + tests + cliff QA poses (b63f941).
- Rig: -motion walk, footprint breakdown, -memledger (829e8fe, e525008).
- Session economy: KNOWN_ISSUES.md split, session-size hook, overnight queue (5d121d8).

## Documents (owner reads these)
- Game Design Brief: https://claude.ai/code/artifact/d31b4321-77c3-4026-93d2-460c190950bf
- Queue decisions: https://claude.ai/code/artifact/681cc873-8163-45da-a51f-32ffca6d7468
- TAA proposal (steps 1-3 approved): https://claude.ai/code/artifact/6745ee4a-4af7-482f-b07d-822e46270918
- Scope & ETA: https://claude.ai/code/artifact/e649e29b-82eb-42cb-b947-df23073ee081
- Terrain review: https://claude.ai/code/artifact/7443de71-6716-4f99-869b-98d3c6814fd7

## Overnight queue (running since 2026-10-02 08:40, IDLE_MIN=0, nice 10, tmux session `overnight`)
- Runner now: one task at a time; a task that stops for the owner, fails or ends
  without a handoff is set aside in `tools/overnight/blocked/` (its uncommitted
  edits saved as a patch) and the queue goes on with tasks that don't `Require:`
  it; each task waits for 15 min of no keyboard/mouse input (IDLE_MIN).
- Queued: 03 steep far slopes -> rock (owner 2c), 04 cliff band A/B pose,
  05 TAA shimmer metric, 06 TAA core (needs 05), 07 TAA risk checks (needs 06),
  08 checkerboard (needs 07). TAA steps 1-3 approved; step 4 (shadow tuning) NOT.
- Owner decisions 2026-10-02: keep cd28987 (wall rock); 2c; cliff pose yes;
  follow-up tasks OK; Unity 7 not yet (beta Dec 2026, release Q1 2027).

## Owner decisions 2026-10-02 (later)
- Task 03: answer (a), keep the slope rock rule; closed (4f2ce28).
- TERRAIN DIRECTION PICKED: **B "Rules for play"** (terrain review section 13); the owner
  counts D (the design brief) as done. Q2 (a): plan first, then TAA -> task
  `04b-surface-rebuild-plan` (planning only, writes a Claude Doc, ends STOP_FOR_OWNER).
  No implementation tasks until the owner approves that plan.

## Waiting on the owner
- 03 BLOCKED (`tools/overnight/blocked/03-*.HANDOFF.md`, commit eac21bd): the far
  band already uses the voxels' rock rule (agrees with the voxel majority on ~93%
  of texels), so far and near match; 30 deg slopes are green in both. Question:
  (a) keep as is / (b) rock from ~45 deg on both (generator change) / (c) from
  ~30 deg on both (~60% of land turns rock) / (d) far-only dither. Recommended:
  (a) now; decide slope colour with the movement bands in the surface rebuild.
- Queue decisions doc (owner questions, done table, answers):
  https://claude.ai/code/artifact/681cc873-8163-45da-a51f-32ffca6d7468 (created 2026-10-02; Q1 = task 03).

## Ready-to-paste prompt
> Read tools/overnight/SUPERVISOR.md and SESSION_HANDOFF.md, then supervise the
> overnight queue as described there until nothing is left that can run without
> me. Only one agent works on the project at a time; you do no engineering
> yourself. Put every question for me in the "Queue decisions" doc.
