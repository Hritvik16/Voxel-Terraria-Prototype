# 156-look-rows-432-vs-540

Model: opus

Approval: supervisor, 2026-10-30, to keep the queue fed on the owner's quality-pass order (perf first). CAPTURE-ONLY: no default,
look or product change. Not a performance task (no timing arms).
Why: `PERF_30FPS_PLAN.md` step 3 (row 3, owner question Q1) is the next 30 fps cut after 153 (built, no gain, kept OFF): FSR 3 dyn
with a floor of 432 internal rows and a ~30 ms target, est. walk p50 ~40 -> ~28 ms. The plan says the owner sees stills at 432 vs 540
BEFORE deciding. Walk p50 today 44.7 ms (task 146); goal 33.

## Do
1. Find how play sets internal rows (`UpscalerSelect`, `-renderrows`, `-renderpreset`). If 432 cannot be forced for a capture, add the
   smallest capture-only way to pin it (a flag value; the play default and `PlaySettings.json` stay unchanged); fast suite once if code changed.
2. Capture, FSR 3 on, 1080p output, same poses / same frame / same sun for both arms: walk_near and walk_open (task 146's poses), one
   cliff / overhang pose, one far horizon pose, one night torch pose (151's room torches). Midday, sun behind the camera (owner rule:
   comparison images well lit) except the night pose.
3. Build one side-by-side sheet per pose (LEFT 540, RIGHT 432) plus a 3x crop sheet of the spots where they differ most (thin far
   features, edges, foliage/ore specks, the 0.1 m window edge). Every image gets a caption that names the panel and what to look at.

## Deliverable
HANDOFF starting with STOP_FOR_OWNER: one question, "Q1 internal rows: (a) RECOMMENDED dyn 432-540 with a ~30 ms target, (b) keep 540,
(c) go lower (405/360)", with the sheets, the plan's estimated gain for each answer, and what each answer queues next. TASKS.md paragraph.
