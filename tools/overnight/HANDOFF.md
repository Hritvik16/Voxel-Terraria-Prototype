STOP_FOR_OWNER -- 93b RE-RUN: shells ON are still 12.7-13.9 ms SLOWER at steady state (rule 3), even though shells 1-3 now fill.
Question (Q61): what happens to 93c (shells default in play)?
 (a) RECOMMENDED: hold 93c, and queue one shell-march cost task (the march is ~16-18 ms of a frame, GPU-bound). 93b4 named the
     candidates: end upward sky rays early (walk_near: 5.8% sky pixels at 94 iters each slow whole SIMD groups), and the air-chunk / air-brick
     steps (77% of iterations at walk_open). Re-run 93b after it.
 (b) switch anyway for the visuals: +13 ms standing still (53 -> 66 ms, 34 -> 48 ms), ~0 while walking (shells 3-7 stay empty then).
 (c) Fact for the owner, not a recommendation: shells ON but NOT drawn (tier 0 + far band only, no tiers 1-2) runs 3-4 ms FASTER than
     today's play (50 / 30 ms) and looks almost the same at both poses (sheets, bottom-left). Tiers 1-2 cost ~4 ms for little visible detail here.

# 93b re-run (shells perf breakdown): DONE (measured), rule 3 -> 93c held

## What changed (commit 5a6bde8, no product code)
- `Screenshots/93b/rerun/perf93b_rerun.txt` (full table + log paths), copies of the 4 cost-split logs, driver + its output.
- KNOWN_ISSUES "SHELLS COST" entry + CLAUDE.md index line: re-run numbers appended.
- No EditMode run: the commit is text only (no code, scene or shader change), so there was nothing for the suite to check.

## Evidence (warm steady state; warm-up `Logs/tg2world_20261017_052347.txt`; ABBA back to back, AC, no owner pause; default mask OFF)
| arm | first run (10-15) | now p50 / p99 ms | >33 ms | delta vs OFF | peak MB | Metal MB |
|---|---|---|---|---|---|---|
| steady walk_near OFF / ON | 52.4 / 67.6 (+15.2) | 53.7, 52.8 / 65.9, 66.0 (p99 85-89 / 107-132) | - | +12.7 (noise 0.9) | - | - |
| steady walk_open OFF / ON | 35.9 / 46.3 (+10.5) | 34.1, 34.1 / 48.0, 47.9 (p99 61-65 / 83) | - | +13.9 (noise 0.0) | - | - |
| moving walk OFF / ON | 42.6 / 39.7 (-2.9) | 41.9, 42.0 / 42.4, 42.1 (p99 98-101 / 97-98) | 79.1, 79.2 / 79.3, 79.4% | +0.3 (noise 0.1) | 2,391, 2,369 / 2,477, 2,366 | 1,203, 1,155 / 1,306, 1,302 |
- Steady logs: `Logs/costsplit_2026-10-17_{053003,053705,054904,060101}.txt` (OFF ON ON OFF). Walk logs: `Logs/tg2world_20261017_{060657,061147,061638,062130}.txt`.
- Shell fill, pinned pose, 120 s: s1 complete at 7 s, s2 at 38 s, s3 at 88-89 s (first run: s1-s2 FULL, 621 / 1,140 missing); s4 94%, s5 28%,
  s6-s7 0; ~600 chunks missing. Filled or not, the cost is the same.
- Where it goes (inside both ON launches): not drawing the shells saves 15.8-17.6 ms (now leaving tier 0 + far band, no tiers 1-2: 50 / 30 ms).
  far_off (which also stops the shell march) saves 17.4-22.1 ms ON vs 2.2-4.1 OFF; far shading -2.0 to +2.5 ms. So shell march ~16-18 ms,
  tiers 1-2 ~4 ms. GPU-bound (render thread 1-3 ms; gpu* 161 vs 117 near, 127 vs 74 open; gpu* read 0.0 in the OFF launches).
- Moving: shell 1 at the walk's start (t+19 s) 1,222 / 2,170, held 1,000-1,860 during the walk, shell 2 400-820, shells 3-7 0; none ever
  complete. 10,600-10,900 chunks generated (50 ms each) vs 2,347 (241 ms) in the first run. So moving ON = OFF because shells 3-7 are absent.
- Memory: peak +42 MB (first run +387): tiers 1-2 are freed now (93d). Metal +125 MB. GPU body pool peak 243,847 of 254,400 (96%), refused 0.
- Looked at `Screenshots/93b/rerun/sheet_walk_open.png` and `sheet_walk_near.png`: OFF, ON and ON-not-drawn look the same apart from the
  snow coast at the left of walk_open (whiter, more even under ON). Tint: the near mountain is tier 0 (blue-grey), the coast is shells (cyan/pink),
  and the ridge top right is shells (yellow/pink). walk_near is almost all tier 0.

## Not done
- Nothing skipped. The `later/` "shell march cost" task was not written: that is rule 2's step, and rule 3 applies (owner decides first).

## For the doc
Task 93b re-run: shells ON vs today's play, same settings (fsr3 540, quality, far band + far shading), warm, ABBA, after 93b2-93g.
- Steady p50: walk_near 53.3 -> 66.0 ms (+12.7; first run +15.2), walk_open 34.1 -> 48.0 ms (+13.9; first run +10.5); noise 0.0-0.9 (`Screenshots/93b/rerun/perf93b_rerun.txt`)
- Shell fill at a standing pose: shell 1 in 7 s, shell 2 in 38 s, shell 3 in 89 s (first run: shells 1-2 never complete)
- Cost: shell march ~16-18 ms; tiers 1-2 (what it replaces) ~4 ms; without either, 3-4 ms faster than today
- Moving: 42.0 -> 42.3 ms p50 (first run -2.9); shells 1-2 mostly there while walking (was ~76 chunks), shells 3-7 still empty
- Memory: peak 2,380 -> 2,422 MB (first run 2,553 -> 2,940), Metal 1,179 -> 1,304 MB
- Rule: ON > 3 ms slower -> 93c held again (STOP_FOR_OWNER, Q61)
Images:
- `Screenshots/93b/rerun/sheet_walk_open.png`: top left OFF, top right ON, bottom left ON with the shells not drawn (tier 0 + far band), bottom right
  the shell tint. Look at the snow coast on the left: ON draws it whiter and more even. The mountain is the same in all three, because tier 0 draws it (blue-grey in the tint).
- `Screenshots/93b/rerun/sheet_walk_near.png`: the same layout at a canyon wall 10-30 m away. All panels look the same, and the tint is tier 0 almost everywhere,
  yet ON costs +12.7 ms here: the few sky pixels at top right march the shells at ~94 iterations each.
