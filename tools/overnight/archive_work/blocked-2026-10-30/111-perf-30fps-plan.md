# 111-perf-30fps-plan

Model: opus

Approval: owner, 2026-10-22 in chat: stable 30 fps (warm, play settings, walking: p50 <= 30 ms AND <= 5% of frames
> 33 ms) is the first goal of the quality pass. Queued by the supervisor 2026-10-22 after task 110.

This is a PLANNING task: it writes ONE repo document, `PERF_30FPS_PLAN.md` (NOT a claude.ai doc; sessions cannot reach
those). No product code, no tests, no rig, no new timing launch. It ends with STOP_FOR_OWNER: the owner picks cuts.

## Inputs (read, do not re-measure)
- `tools/overnight/done/` 110 HANDOFF content is in `Logs/overnight/110-driver.out`, `Logs/costsplit_2026-10-22_122517.txt`,
  `Logs/costsplit_2026-10-22_130301.txt`, `Logs/tg2world_20261022_133936.txt`, `..._134432.txt`.
  Summary: GPU-bound (CPU floor 5-7 ms). FULL p50 60.7 ms walk_near / 43.0 ms walk_open; moving 41 ms p50, 99 ms p99,
  72-78% of frames > 33 ms. Largest costs (ms saved if OFF): shell primary march 12.8-18.8; far band 15-18 (overlaps the
  march); sun shadows 6 (open) - 16 (near); shells overall 13-14; FSR3 pass 5-6; shell shadow 1; far shadows 0-2.4;
  far shading ~1; torch, fluid, HUD ~0; sky-ray exit already saves 1.6-3.7. Pixel-scaling part of the frame ~38-53 ms
  (two-point fit only).
- Gaps in 110: `-renderrows` is ignored by the cost split (`CostApply` hardcodes 540), so 360/720 were NOT measured;
  tiers_off and see-through-off arms do not exist.
- Earlier ideas: `tools/overnight/later/102-shell-march-cost.md`, KNOWN_ISSUES "SHELLS COST +10-15 ms" (93b3-93b5: air-brick
  mask `-shellmask 1` -2.1 / +0.8 ms; sky exit), `ENGINE_PHASE_PLAN.md` E4, `AMENDMENT_8_14`/`8_15` (what shells owe).
- Open cost from task 108: the saved-summary build runs on the main thread, ~3.1 ms each, up to ~12-24 ms in a frame that
  evicts 4 edited chunks (`SavedEditSummaries.cs`, `StreamManager.SaveDelta`). Include as a frame-spike candidate.
- Frame spikes while moving (p99 99 ms, 72-78% over 33 ms) and shells not filling while walking (workers): include.

## Write `PERF_30FPS_PLAN.md`
1. Gap in one line: needed vs today, steady and moving (arithmetic from the inputs).
2. A ranked table of candidate cuts, one row each: cut, what it removes, ms saved (cite the 110 row; say "estimate" when
   it is not a measured arm), visual cost in plain words (read the code / earlier screenshots in `Screenshots/93b*`,
   `Screenshots/110`, FSR/upscaler comparison folders; LOOK at 2-4 existing images and cite them with captions), risk to
   frozen APIs / memory layout (flag any), and effort in tasks. Rows must include: FSR 3 at 540 / 720 / dynamic rows and a
   lower internal row count, the quality preset, sun shadows (cheaper rung or distance), shell march (mask, fewer shells,
   coarser steps, `later/102`), far band + far shading/shadows, the 108 summary-build spike, shell fill workers.
   Do not claim a combination's total: say which cuts overlap (far_off overlaps shell march).
3. A recommended ORDER of one-change-per-task fixes, each with its single warm measurement and what each outcome would make
   us do (CLAUDE.md measurement rules), and a checkpoint after each. Show a running sum that reaches the target or states
   honestly that the listed cuts do not reach it and what else would be needed.
4. Questions for the owner as a batch of specific multiple-choice items (look and feel trade-offs, which cuts to accept),
   recommendation first. Reversible engineering choices get "going ahead with (a)".
5. Anything needing the owner's approval by CLAUDE.md (frozen API, memory layout, default-on changes).

Commit `PERF_30FPS_PLAN.md` + HANDOFF.md only. HANDOFF starts with `STOP_FOR_OWNER`, then the questions; end with
`## For the doc` (the ranked table's top 6 rows and the gap line; image paths with captions).
