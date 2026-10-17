# 102-shell-march-cost

Requires: 93c-shells-default-in-play

Status: in `later/`, NOT approved to queue yet (performance-changing code needs the owner's go; owner, 2026-10-17 Q63 (a):
"a 'shell march cost' task goes in later/"). Branch `engine-phase`. This IS a performance task (PREAMBLE rule 9 applies).

## Why
Shells ON were +12.7 / +13.9 ms p50 steady (walk_near / walk_open, 17 Oct re-run, `Screenshots/93b/rerun/perf93b_rerun.txt`);
task 99's sky-ray exit took -4.7 / -2.6 ms off. Estimated gap now ~+8 / +11 ms standing still (not measured). Play switched
to shells anyway (93c). What 99's HANDOFF (commit a1d27a2) names as left:
- walk_near: sky rays still take ~82 iterations climbing ~145 m from the eye (~956 m) to the bound (1,100 m world ceiling).
- walk_open: HIT rays (106 iterations each, 6% of px) and air-chunk steps (47% of iterations).
- 93b5's air-brick mask (`-shellmask 1`) is built but OFF (its exit-plane leap cost +5-6 ms).

## Change
ONE exact (0 px) change aimed at the largest of these, chosen after splitting the cost with the existing cost-split arms
(`skyexit_*`, `shellmask_off`, `shellleap_new`, counters ShadowStats 62-64). Then the 93b arms (warm-up + ABBA, AC only,
driver `Screenshots/93b/rerun2/93b-rerun2-driver.sh`) shells OFF vs ON with the change, so the table finally shows the gap
with 99 + this change. Report vs the 3 ms line; no default change beyond the one exact change.
