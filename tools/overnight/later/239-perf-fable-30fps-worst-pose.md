# 239-perf-fable-30fps-worst-pose
Model: fable
Requires: 238-perf-split-rest-at-forest
Approval: OWNER 2026-11-18 (chat): "let a fable model take a crack at getting the fps down to 30 in the worst case". Supervisor queues it AFTER 238 (needs its split) and for the night (timing; idle Mac).
## Goal
Forest at a 16 deg sun (235 baseline 135 ms, p50, 432 rows, FSR 3 to 1080p) and the other worst poses in `Logs/budget/2026-11-17_235256/check.txt` as close to 33.3 ms as can be reached. Read first: `PLAN_PATH_TO_33MS.md`, `Logs/overnight/238/` report, `PERF_EXACT_CUTS.md`, amendments 8.15/8.17, CLAUDE.md.
## How (free to choose, but in this order)
1. Start from 238's split: attack the biggest measured piece. Think about NEW designs, not just the known switches: the plan's S7 (sun-space shadow map of the near window, derived from the CPU-written mirror, amendment-level), a cheaper low-sun ray scheme that is exact or provably bounded, a hit-buffer / step split (8.17 T1-T5), tree-heavy-view cost. Fresh ideas welcome.
2. One performance change per measurement; ABBA at warm steady state; compare against the 235 baseline with `tools/overnight/run-budget-check.sh`; keep `FAIL 0` tests (fast suite each change).
3. Exact or proven-bounded changes: go ahead and keep them. **Changes that alter the look (softer picture, lost shadows, lower rows) are NOT applied by default:** build them behind a switch, OFF, show before/after stills, and put them to the owner. No terrain-specific tuning; data-derived rules, adversarial case proven. Memory: report vs switch OFF, within PREAMBLE rule 11. Frozen APIs and memory layout: flag before changing (STOP_FOR_OWNER).
4. Stop after 3 kept changes or when the forest pose is under ~60 ms or nothing more is reachable; never chain unmeasured changes.
## Output
HANDOFF `## For the doc`: per change, forest 16 deg p50 before -> after and the other poses; honest statement of how far from 33.3 ms; stills of the forest pose before/after (look at them).
