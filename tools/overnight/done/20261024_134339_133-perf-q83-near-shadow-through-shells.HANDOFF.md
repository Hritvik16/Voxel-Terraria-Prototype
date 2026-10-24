STOP_FOR_OWNER -- one performance-affecting change made (Q83 fix), now ON by default. Please look. Question: keep it ON although a noon canyon pose (walk_near) pays +6.1 ms p50? Low sun costs +1.1 to +1.9 ms. A cheaper follow-up could remove most of the noon cost (see "Left open"). Options: (a) keep ON (recommended), (b) ON plus that follow-up next, (c) OFF.

# 133-perf-q83-near-shadow-through-shells: DONE. Tier-0 sun rays that leave the 0.1 m window or the band ceiling unblocked now continue through the shells. The lit square round the player in a far cliff's shadow is gone. ON by default (`-nearshadowshells 0` = before).

## What changed (commit b0bee33)
- `Raymarch.compute`: `TraceShadowRay` reports `exitT`, the t at which it left the window or ceiling unblocked (-1 otherwise).
  - In the shadow block, right after it, such a ray runs `ShellMarch(..., shadow=true)` from exitT. This is the same march a shell hit's sun ray uses.
  - It runs before the 2x2 and temporal reuse, so reusing lanes get the same answer.
  - With the flag on, the window and ceiling bound the tier-0 part, not the 128 m `_MaxRayDistance` (shorter than the window's far corners).
  - Far-shading kernel only, shells drawn, sun only (no point lights).
- **Deviation from the task text:** it runs INLINE, in both the pre-pass path and the one-pass path. It is NOT handed to CSShellPrepass.
  - Pass 1 draws every group without a primary shell ray to completion. Handing a shadow ray over would make pass 2 re-run tier 0 (primary + shadow) for that whole group.
  - Under a low sun that is most of the tier-0 screen, far over the 5 ms budget. Inline measured +1-2 ms.
- `RaymarchFeature.cs`: `NearShadowShells` (ON; `-nearshadowshells 0|1`); `STATS_LEN` 80 (77-79 = rays handed on, their shell iterations, blocked).
- Capture only:
  - cost-split arms `nearsh_on` / `nearsh_off` and a `[nearsh]` counters line;
  - `-cliffbefore 1` in the task-131 cliff capture (A/C with the flag forced OFF first);
  - `tools/nearshadow133.py` (before/after sheets and window-edge numbers). TASKS.md "Task 133".

## Evidence
- Fast suite PASS 921 FAIL 0 SKIP 15, run twice: before (`Logs/overnight/133-tests.out`) and after the default flip (`133-tests2.out`).
- Correctness: one cliff launch at sun 30 deg, same two spots as 131 (`Logs/lookreview_20261024_092606.txt`). Looked at both sheets and both top-edge crops.
  - cliff1: A is grey from the feet out (was white snow to ~100 m). C: the bright square is gone. The shadow is continuous across the window line, and real lit snow at the bottom crosses it continuously.
  - cliff2: the straight cut on the window's top edge is now the valley wall's own diagonal shadow edge. The sand on the left is still lit. No fringe along the window edge.
  - The black crescent on the rock wall (131) is unchanged and unrelated.
- Identity, no-upscaler raw stills ON vs OFF:
  - 0 px at walk_near and walk_open at noon, and at sun 20 deg with the shells filled.
  - The first sun-20 launch showed 152k px in walk_near's far horizon only. Its OFF still was shot while shells 3-7 were still filling (779 chunks missing).
  - The reversed launch (`-shellwait 300`) gave 0 px, so that was fill timing, not this change.
- Timing: warm (warm-up `Logs/tg2world_20261024_110807.txt`, discarded), AC, ABBA, no pause during the arms. Decisions were written first (`Screenshots/133/decisions_before_measuring.txt`). Table: `Screenshots/133/perf133.txt`.
- An earlier attempt (09:46-11:07) was voided. The Mac went to battery, the owner paused, and then every player launch quit about 5 s after start. They worked again once AC was back. Cause not found; no crash report, watchdog idle.
- The player on disk (`Builds/Playground.app`) was built before the default flip, so it still defaults to OFF until the next build.

## Left open
- **Noon cost** (walk_near +6.1 ms): every lit tier-0 ray leaves through the band ceiling (100% of traced rays). It then crosses the shells' hole above the band, which ShellMarch treats as air, in 12.8 m tier-0-chunk leaps. It climbs until it is above the shells' highest solid brick (1100 m, the whole world's max), about 8 iterations per ray.
  - Possible exact follow-up: let a shadow ray in that hole region jump to the hole box's exit in one step, or use a local height bound instead of the global 1100 m.
  - That is a performance change, so it is not queued; it needs the owner's approval.
- No caster outside the window at walk_near or walk_open ("blocked 0"). The timing is the cost of checking, not of finding shadows.

## For the doc
| warm, AC, quality, FSR 3 540 rows | OFF (before) | ON (default now) | change |
|---|---|---|---|
| cliff1 C: luma just inside / outside the window edge | 191 / 147 | 127 / 126 | square gone (`Screenshots/133/nearshadow133.txt`) |
| cliff1 A: tier-0 ground luma (shell ground 70) | 145 | 101 | shadowed from the feet out |
| sun 20 deg p50, walk_near / walk_open (2 launches) | 68.0 / 54.2 and 67.2 / 52.3 | 69.6 / 55.5 and 68.3 / 54.2 | +1.1 to +1.9 ms (`Logs/costsplit_2026-10-24_111411.txt`, `_114818.txt`) |
| noon p50, walk_near / walk_open | 69.7 / 44.6 | 75.8 / 45.6 | +6.1 / +1.1 ms (`Logs/costsplit_2026-10-24_112128.txt`) |
| MODE=walk 3 min x4, sun ~30 deg: p50 / > 33 ms | 13.28 ms / 1.63% | 14.73 ms / 2.19% | +1.45 ms, +0.56 points |
| memory | | | none added (peak 2209-2223 MB both) |
- `Screenshots/133/owner/cliff1_before_after.jpg`: left = before, right = after.
  - Top row (A, standing in the cliff's shadow): the white snow on the left turns grey on the right, all the way to your feet.
  - Bottom row (C, 250 m up, yellow = the 0.1 m window): the bright square inside the yellow outline is gone, and the shadow runs straight through the window.
- `Screenshots/133/owner/cliff2_before_after.jpg`, bottom row: near the top of the yellow outline, the straight shadow cut becomes the wall's natural diagonal edge.
- `Screenshots/133/cliff2_C_topedge_crop.png`: a zoom on that edge. Left: the straight window cut. Right: a continuous shadow with no seam.
- Default changed: yes, ON.
- Gameplay consequence in plain words: ground near the player now darkens correctly when a cliff or hill more than ~100 m away blocks the sun. Under a low sun you no longer stand in a lit square inside a big shadow. Nothing about reach, simulation (8.15's 128 m), edits or saved data changes. The cost is about 1-2 ms per frame in most views and about 6 ms at noon in a deep canyon.
