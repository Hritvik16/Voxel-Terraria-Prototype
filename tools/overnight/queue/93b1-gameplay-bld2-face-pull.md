# 93b1-gameplay-bld2-face-pull

Requires: 93a1-gameplay-bld1-drag-line

Approval: owner, 2026-10-15 (supervisor chat, answer to task 92's Q1, replacing the plan's rec. (a) box drag):
"I would just look into how Lay of the Land does this. It's a pretty intricate system, so I don't expect us to figure it
out right now, but for now very simply we can place one block down, we can extend it into a line by selecting one of
its faces and then that line we select one of the faces of [to extend it into a plane]". Q4 (a): alternate with engine
tasks. Queued by the supervisor 2026-10-15. Branch `engine-phase`.

## Change (BLD2, redefined by the owner: FACE PULL)
Implement row BLD2 of `GAMEPLAY_PHASE_PLAN.md` as a **face pull** (push-pull, like SketchUp), not the box drag:
- Aim at a face of a placed shape (one block -> its face; a BLD1 line -> one of its long faces; a plane -> its face),
  press the pull input and drag along the face normal: the shape's cells are repeated N times along that normal, N
  following the drag, with the ghost preview + cell count, Esc cancels, release commits. Block -> line, line -> plane,
  plane -> box (thickening). Pulling inward (negative N) digs the same cells (worker's pick whether to include; record).
- "The shape" = the most recent stroke the player placed (block, BLD1 line or a previous pull) while it is intact;
  anything else -> the single aimed block. Worker picks the input (e.g. hold a key + LMB drag) and records it.
- Survival cost per P7 (place until the slot runs out); one `EditService` call per pull (BLD1's batched path);
  mobile-solid wake guard kept. Guardrails §2 of the plan apply.
- Not in this task: studying Lay of the Land's system (owner: later; leave a plan row "BLD-R research" if 93a1 did
  not add one).

## Acceptance
- EditMode: pulling a block face by N gives an N+1 line in the face normal's direction; pulling a line's side face gives
  a plane of exactly line x N cells; pulling a plane gives a box; cost = cells placed; stops when the slot runs out;
  BLD1 unchanged.
- Fast EditMode suite green; `SUITE=all` if anything under `Assets/TerrainGen/` changed. No frame-time measurement.
- Screenshots in play (midday, sun behind the camera): block -> line -> wall -> thick wall in three pulls; a small hut
  (4 walls + flat roof) built with lines and pulls; the preview mid-pull. Captions say what to look at. F2 help updated.
- HANDOFF `## For the doc`: one-line result, the keys in `./run-play.sh`, the picks, the images.
