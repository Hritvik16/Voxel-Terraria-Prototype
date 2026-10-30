# 93b-shells-perf-breakdown

Requires: 99-shell-sky-ray-exit

(Was `tools/overnight/later/89-shells-perf-breakdown.md`; copied and updated by task 90b. The original stays in later/.)

Approval: owner, 2026-10-15 in chat. On Q49 (shells ON by default in play, cascade tiers 1-2 retired) the owner said
"I want to say yes just for the visuals but can you give me a performance breakdown?" and then "I would need to know
frame time but I feel the new method should win out on that ... Yes queue this." Re-approved in order by Amendment 8.15
Q10 (a) (owner, 2026-10-15; `tools/overnight/done/90a-amendment-8-15-simulation-reach.md` "## Owner answer": "89/90
(shells default)" after E1). Branch `engine-phase`. **This IS a performance task:** PREAMBLE rule 9 (warm steady
state) applies, frame time may be quoted. No product code change beyond capture-only flags; no default changes.

## What changed since the original file
- It no longer waits for task 87: 87 is SUPERSEDED by Amendment 8.15 (step 14 redone as D1-D4, tasks 93d-93g); do
  not apply `tools/overnight/blocked/87-*`. The measurement is of today's shells path (CPU reach 128 m, tiers 1-2 still
  allocated, E1's edit summaries present if 93a landed -- the walk has no player edits, so E1 adds no work here).
- Task 90 is now `93c-shells-default-in-play` and switches ONLY the play default; freeing tiers 1-2 is task 93d (D1).

## Why, and what each result does (decided before measuring, CLAUDE.md)
No shell task ever measured frame time (rule 10). Memory is known: peak 2,999 MB with shells + tiers 1-2 both allocated
(task 85), shells +366 MB Metal on High (task 77). Task 93c switches the play default; its rule comes from this task:
- ON p50 <= OFF p50 + 1 ms (or faster): 93c switches the default as written.
- ON 1-3 ms slower: 93c still switches (visuals + memory), and this task writes a `later/` task file "shell march cost"
  naming the cost source this split found.
- ON > 3 ms slower, or ON p99 / ">33 ms frames" share clearly worse: write `STOP_FOR_OWNER` in HANDOFF with the table;
  93c must not switch.

## Arms (two launches per arm, ABBA: OFF ON ON OFF, straight after ONE warm-up launch)
Setting = the play default, pinned so rows do not move between arms: `-upscaler fsr3 -renderrows 540`, quality High,
far band + far shadows + far shading as `./run-play.sh` uses them (copy its flags; `run-cost-split.sh` already does this
for task 57 -- reuse it, adding `-shells 1 -shellworkers 3` for ON). OFF = today's play (no `-shells`).
1. **Steady (shells filled):** the task-57 cost-split walk piece, streaming pinned, after a fixed fill wait long enough
   for shells 1-7 to finish at the start pose (use `-shellwait`; say how many shell chunks were still missing). p50 / p99,
   CPU main-thread ms vs total frame (CPU- or GPU-bound?).
2. **Moving (streaming live):** `MODE=walk` circle, p50 / p99, share of frames > 33 ms, shell chunks stored per shell at
   the end, and how long shell 1 took to fill at the start pose.
3. Inside ONE ON launch, if cheap: the existing cost-split arms "far band off" and "far shading off", so the table shows
   whether the shell march or something beside it costs. Skip if it does not fit; name it as not run.
4. Memory, both arms: peak process MB and Metal MB (`-memledger` / `*_mem_peak_breakdown.txt`), with tiers 1-2 still
   allocated under ON (93d removes them; task 55 ledger: tiers 1-2 = 193 MB = 79.0 GPU + 67.6 native + 46.1 managed).
Noise floor = the spread between the two OFF runs. Check `pmset -g batt` before each launch (AC only; battery arms were
wrong once). `tools/heavy.sh` pause note -> rerun that arm. Shared Unity lock for every launch.

## Report
Table: arm, p50 / p99 ms, >33 ms share, delta vs OFF, peak MB, Metal MB, log paths, warm-up log path. Which rule above
applies, in one line. One still per arm at the same walk pose (midday, sun behind the camera -- owner rejected backlit
stills) plus the `-shellsdebug` tint of the ON still, captioned with what to look at.
End with `## For the doc`: the table, the rule that applies, the stills.

## Re-run (supervisor, 2026-10-15 ~22:40, Q52 (a): going ahead unless the owner objects)
The first run blocked (STOP_FOR_OWNER): ON +15.2 / +10.5 ms p50 steady, shells never filled while walking. Its report is
`tools/overnight/blocked/93b-shells-perf-breakdown.HANDOFF.md` and `Screenshots/93b/perf93b.txt` (commits becf7f1, a7d811a).
93b2 (fill while walking) and 93b3 (far band after the shells) ran first. Re-run the SAME arms with the code already in
place (no new capture code needed), and add one column "first run" so the table shows first run -> now. Same three
rules decide 93c. Write the output to `Screenshots/93b/rerun/` so the first run's report stays.

## Note (supervisor, 2026-10-17 04:4x): 93b5 closed, Q60 (b)
93b5's air-brick mask is built but OFF by default (`-shellmask 1`); mask off = the pre-93b5 march byte for byte. Measure with
the DEFAULT (mask off). Read `tools/overnight/blocked/93b5-shell-air-brick-mask.HANDOFF.md` for the iteration split (air-chunk
leaps 34-47%, tier-0 leaps 17-23%) and the 93b4 read caches already in.

## Owner answer (2026-10-17, Q61 (a)) -- third run
The 2026-10-17 re-run stopped (rule 3: ON +12.7 / +13.9 ms steady; its report `tools/overnight/blocked/93b-shells-perf-breakdown.HANDOFF.md`,
`Screenshots/93b/rerun/perf93b_rerun.txt`, commits 5a6bde8 / f349d22). Owner chose (a): task 99 (shell sky-ray exit) first, then re-run
93b. Run the SAME arms again with whatever default 99 leaves (sky exit ON only if 99 made it the default; mask OFF). Add a column
"re-run 17 Oct" so the table shows first run -> re-run -> now. Write to `Screenshots/93b/rerun2/`. Same three rules decide 93c.
If 99 stopped for the owner, this task does not run (the runner holds it).

## Owner answer (2026-10-17 ~13:10, Q63 (a)): SKIPPED
Not run a third time: the owner switched play to shells without it (93c). Kept here for the next performance milestone
(driver ready: `Screenshots/93b/rerun2/93b-rerun2-driver.sh`, AC only).
