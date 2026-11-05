# 166-look-fsr3-softening

Model: opus

Approval: owner, 2026-11-01, doc Q103 (a): keep dyn 432-540, and look into why FSR 3 softens the picture and draws faint dark dotted outlines
along block edges at EVERY height (task 162: native vs 720 differs 3.10 at walk_open, 720 vs 432 only +0.86; viewer
https://claude.ai/artifact/UPjq7GD4mvtAzS5BvvU35r, shots `LookReview/2026-10-31_210933_high/`). LOOK task, capture only: no frame-time arms;
nothing changes in play unless the owner picks it. Read `TASKS.md` "Task 52" / "Task 162" and the FSR 3 integration first.

## Do
Find the cause before trying fixes ("let's find out"): check what FSR 3 is given and how -- RCAS sharpening on/off and strength, the reactive /
transparency-composition masks (none? sky, water, far shells), motion vectors (missing or wrong for ray-marched pixels / shells / far band?
static camera should give 0), depth format and near/far convention, jitter sequence and its match to the projection, exposure / pre-exposure,
mip bias for any texture sampling, and whether the HUD or a post pass is upscaled. Write which inputs were wrong or missing.
Then at most THREE candidate fixes behind flags (e.g. sharpening strength, correct motion vectors / reactive mask), each judged by stills at
the five 162 poses at 432 and 540 vs native (mean |dlum|, % px > 8) and 159's motion bursts (no new ghosting/shimmer). Fast suite once if code changes.

## Deliverable
Publish-ready full-size shots: native / today 432 / each fix 432 (and 540 for the best one) at 1920x1080, plus 2x crops of the dotted edges.
The supervisor will add them to the full-size viewer (owner: 5-across strips are thumbnails). Handoff: a table per pose and fix.

## End
STOP_FOR_OWNER: "ship fix X in play?" with the recommendation first. Mention any cost the fix may add (estimate only, not measured here).
