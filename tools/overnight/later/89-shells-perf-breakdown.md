# 89-shells-perf-breakdown

Requires: 87-engine-14-smaller-window

Approval: owner, 2026-10-15 in chat. On Q49 (shells ON by default in play, cascade tiers 1-2 retired) the owner said
"I want to say yes just for the visuals but can you give me a performance breakdown?" and then "I would need to know
frame time but I feel the new method should win out on that ... Yes queue this." Queued by the supervisor 2026-10-15.
Branch `engine-phase`. **This IS a performance task:** PREAMBLE rule 9 (warm steady state) applies, frame time may be
quoted. No product code change beyond capture-only flags; no default changes.

## Why, and what each result does (decided before measuring, CLAUDE.md)
No shell task ever measured frame time (rule 10). Memory is known: peak 2,999 MB with shells + tiers 1-2 both allocated
(task 85), shells +366 MB Metal on High (task 77). Task 90 switches the play default; its rule comes from this task:
- ON p50 <= OFF p50 + 1 ms (or faster): task 90 switches the default as written.
- ON 1-3 ms slower: task 90 still switches (visuals + memory), and this task writes `later/` task file "shell march cost"
  naming the cost source this split found.
- ON > 3 ms slower, or ON p99 / ">33 ms frames" share clearly worse: write `STOP_FOR_OWNER` in HANDOFF with the table;
  task 90 must not switch.

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
   allocated under ON (task 90 removes them; estimate the saving from the task-53/55 ledger: ~480 MB).
Noise floor = the spread between the two OFF runs. Check `pmset -g batt` before each launch (AC only; battery arms were
wrong once). `tools/heavy.sh` pause note -> rerun that arm.

## Report
Table: arm, p50 / p99 ms, >33 ms share, delta vs OFF, peak MB, Metal MB, log paths, warm-up log path. Which rule above
applies, in one line. One still per arm at the same walk pose (midday, sun behind the camera -- owner rejected backlit
stills) plus the `-shellsdebug` tint of the ON still, captioned with what to look at.
End with `## For the doc`: the table, the rule that applies, the stills.
