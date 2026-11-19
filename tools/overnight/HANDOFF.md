# 241-perf-slice-budgets: DONE (plan only) -- `PERF_SLICE_BUDGETS.md`: 3 tiers, per-system ms/MB slices, admission rule, forest levers, architecture alternatives

No code, no build, no Unity, no launch, no timing. Owner decisions D1-D6 are in the doc's sec. 5 (not a STOP: nothing is blocked on them).

## What changed
- NEW `PERF_SLICE_BUDGETS.md` (repo root). TASKS.md paragraph. Arithmetic scripts `Logs/overnight/241/scale.py` (GPU-class scaling)
  and `Logs/overnight/241/bars.py` (chart) -- local only, `Logs/overnight/` is git-ignored. Commit: the "241: PERF_SLICE_BUDGETS.md" commit on engine-phase.
- Not touched: `PERF_BUDGET.json`, gates, product code. Previous HANDOFF (170's STOP_FOR_OWNER scatter-upload question) is still open
  for the owner; it lives in `tools/overnight/blocked/` / commit 0582866.

## Main findings (every number sourced in the doc; est. = arithmetic, guess = judgement)
- M1 Air = tier A ("low") at best, and only with 237's low-sun/bounce/dyn-rows package and a 50 ms (20 fps) cap for the dense forest at
  dawn/dusk. Today it misses tier A at 3 of 4 worst poses (235: forest 16 deg 135.4, jungle 70.0, night 52.7, walk 35.4 ms).
- Hardware is not the forest fix: 30 fps at forest 16 deg needs ~4.9x the M1 GPU at 432 rows, ~7x at 540; 60 fps at 720 rows ~30x
  (est., model: CPU floor 7.1 unscaled + GPU/k, pixel share from 219). Ignores bandwidth, Metal vs other APIs, thermals (stated).
- Tier-A slices = 8.17's adopted shares (sum 33.3). Memory: CPU pool 278.3 MB, GPU bodies 124.2, flood 38.3 (170), shells est. 388,
  fluid est. 240; process peak 2,512-2,621 MB, headroom 379 -> proposal: ~250 MB reserved for the placement phase.
- Forest levers F1-F7 with est./guess savings, look cost, memory, risk, amendment, and the one capture each. Recommended order: forest
  cost-split capture -> F1+F2 (low-sun cap, bounce) -> F3 solid crown cores (content; also frees GPU bodies) -> sun-steps counter + F4
  foliage early-out -> F5 dyn floor -> F6 (amendment) -> F7 density last. Stack: 50 ms reachable at mid-guesses with 360 rows.
- Architecture: lighting in foliage at low sun is design-shaped (sun + sky = 80 of 135 ms); "rest" (55 ms forest/jungle, 43 night)
  never split, so a bug is not ruled out (124: halving sun rays saved 0 ms). Recommendation: no renderer replacement; a sun shadow map
  (a) or foliage light proxy (c) only on the named evidence; meshing (b) not justified (+200-800 MB guess, attacks only the march).

## Flags for the supervisor / owner
- **Unexplained:** walk_near noon 60.6 ms (232, TASKS.md) vs 34.1 ms (235) a day later; 235's walk arc heads away from the sun, so it may
  just be a lighter view. "Walk 35 ms" should not be read as solved.
- PREAMBLE rule 12 / PERF_BUDGET_GUARD name a P5 `[sun]` rays/steps counter; I found no such counter in code (only `-shelliters`,
  `-farsteps`). Doc sec. 4 proposes it (capture-only, ~20 lines).
- The task asked to add the doc to the owner doc tab; PREAMBLE rule 7 forbids claude.ai docs in this session -> supervisor, please copy
  "For the doc" below.

## Evidence
- Fast suite not needed (no code). Sources read: 235 check.txt + [mem] lines, PLAN_PATH_TO_33MS, PERF_BUDGET_GUARD/.json, PERF_EXACT_CUTS,
  8.17 sec. 3-4, 8.14 preset table, 219 table.md, 170 HANDOFF, RESEARCH_LAY_OF_THE_LAND. Arms named in captures were grep-checked to exist.
- Image looked at: `Screenshots/241/budget_bars.png` -- one bar per worst pose with the 33.3 / 50 ms lines; renders correctly, labels clear.

## Next step
Owner answers D1-D6. If D4 = yes: queue ONE performance task = 237 sec. 2's forest cost split (+ sun arms, + `-startsun 0 skyfill0`,
+ one `-trees2 0` FULL launch). It decides F1/F2 and splits the forest "rest".

## For the doc
**241 -- performance budgets per machine tier (plan, no timing).** `PERF_SLICE_BUDGETS.md`.
- The M1 Air can be the **minimum spec ("tier A")**: 30 fps on typical views, dense forest at dawn/dusk allowed 20 fps, memory < 3 GB --
  only after the cuts in its sec. 5/6. Today: forest dawn 135 ms, jungle 70, night camp 53, walk 35 (task 235).
- **Better hardware does not fix the forest**: 30 fps there needs ~4.9x the Air's GPU even at today's resolution (est.).
- Making the forest fit, in order: shorter low-sun shadows + cheaper bounce at low sun (~-30..-60 ms, guess) -> solid crown cores
  (guess -10..-25) -> foliage shortcut for light rays -> lower resolution in heavy views -> fewer trees LAST. Mid-guess: ~50 ms.
- Architecture: not proven broken; don't replace the renderer. A cheaper sun-shadow design is the first big change IF the next forest
  capture shows the sun still > 20 ms after the cheap cuts.
- Six decisions for you (D1-D6): tier A = Air with a 20 fps forest cap? keep High view range? what to drop first? etc.
- Image: `Screenshots/241/budget_bars.png` -- one bar per worst pose; look at how far each bar runs past the 33 ms dashed line, and that
  in the forest bar orange (sun) + blue (sky fill) are 80 of the 135 ms.
