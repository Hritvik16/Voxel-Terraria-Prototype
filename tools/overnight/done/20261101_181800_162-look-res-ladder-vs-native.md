# 162-look-res-ladder-vs-native

Model: opus

Approval: owner, 2026-10-31, doc Q100 answer: "(a)" plus "I also want comparisons with native 1080p and maybe other resolutions above
432 upscaled to 1080p in order to really compare". 159 already shipped 432-540 dyn; this is the look the owner wants beside it.
CAPTURE ONLY: no frame-time arms, no default changes. The fast suite only if code changes.

## Do
Extend 156's `-lookreview -lookrowsab` (or add a ladder flag) so ONE launch shoots, at the SAME frame per pose, these internal heights:
**native 1080 (no upscaler, the reference)**, FSR 3 at **720, 648, 540, 432**. Poses: 156's five (walk_open, walk_near, high #1, high #2,
night torch) at midday with the sun behind the camera except the night one (owner: comparison images well lit).
Plus 159's motion bursts (still / walk / turn, 4 consecutive frames after 60 lead frames) at walk_open for native, 540, 432.

## Deliverables (`Screenshots/162/`)
- One sheet per pose: the 5 heights side by side, labelled "native 1080 / 720 / 648 / 540 / 432 (now in play)". Under each tile: mean |lum
  diff| vs native and the % of pixels differing by > 8 levels.
- One crop sheet: 3x crops of the 3 spots that differ most from native, all 5 heights in a row.
- One motion sheet: the burst frames for native / 540 / 432 and the flicker numbers (159's metric).
- A short table in the handoff: height, mean |diff| vs native per pose, flicker, and the frame time each height costs from EXISTING logs only
  (146 at 540, 159 at 432; mark the others "not measured").

## End
STOP_FOR_OWNER: one question for the doc with the sheets: "keep dyn 432-540 / raise the floor to N / native is worth it at any cost?"
with the recommendation first, and say plainly what each answer does to the 30 fps goal.

## Owner answer (2026-11-01, doc Q103): (a) keep dyn 432-540 AND look into FSR 3 softening / dotted block-edge outlines -> task 166. Viewer: https://claude.ai/artifact/UPjq7GD4mvtAzS5BvvU35r
