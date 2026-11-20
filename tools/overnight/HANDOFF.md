# 246-world-fill-cost-plan: DONE -- `WORLD_FILL_BUDGET.md` (plan + capture counts; no engine code, no frame times). Owner questions Q-246a-e (not blocking)

## What changed (commit: see `git log -1`; docs + text logs only)
- `WORLD_FILL_BUDGET.md` (new, repo root): owner summary, count table + cost model, fill classes with render paths (citing ARCHITECTURE_v8.6
  §5.4/6.1/6.2/6.3/6.5), shipped-game notes (sourced), per-class tier-A budget, placement admission rule, experiments E1-E4 as
  queue-ready outlines, questions for the owner.
- `Screenshots/246/` (text): 8 costsplit reports, `table_raw.txt`, `model.txt`, `coverage_proxy.txt`, `run.sh`, `parse.py`, `model.py`, `sheet.py`, `run_log.txt`.
- `TASKS.md`: Task 246 paragraph.
- A previous session of this task (usage-limit restarts) had already started `run.sh` detached; all 8 launches finished at 11:56. I reused them.

## Evidence (counts only, rule 10; one dev-kernel build 10:17, 8 launches 10:17-11:56, `Logs/overnight/246/`)
- Every player log shows the right tree mode and no `[kernels] FAILED`.
- walk_open's pinned pose has no trees in view (cliff + sea): all arms within 0.8 %. It is the noise control.
- forest_eye, dense vs off:
  - total steps/frame 34.11 -> 32.94 M (16 deg, -3 %); 30.46 -> 28.41 M (noon, -7 %)
  - tier-0 (0.1 m) steps 16.42 -> 22.99 M (+40 %); 13.72 -> 20.03 M (+46 %)
  - sky steps +115 % / +106 %; sun steps per ray +24 % / +55 %
  - leaf 28 % of the screen; pixels changed 38.5-39.7 %
- Rate: +0.225-0.267 M tier-0 steps per 1 % of screen in leaves, i.e. est. 1.3-1.8 ms per % at tier A (calibrated on PERF_SLICE_BUDGETS
  sec. 6's tree share, not measured). Solid near content: est. -0.15..-0.2 M per % (decomposition of the pre-210 arm).
- Memory (rule 11 lines; forest_eye):
  - GPU body pool peak: off 153,364; sparse 221,323; dense 245,704 (96.6 %); pre-210 254,400 (full, refused 26-27)
  - CPU pool peak: 436,947 -> ~493 k (past the valve 484,500)
  - process peak 2,511-2,630 MB across arms: noise, no order by arm
- Looked at `Screenshots/246/sheet246.jpg`. Rows 1-2 (forest_eye 16 deg / noon): off = an open valley with mountains, pre-210 = a huge
  near trunk + dark firs, sparse = scattered trees, dense = trees over ~40 % of the frame hiding the valley. Row 3 (walk_open) is
  identical across arms.
- Not done: no primary-hit material split exists (trunk coverage not counted; E1 adds it). No EditMode run (no code changed).
- Pass card: no pass added or changed. HEAVY: no. Slice (rule 14): none consumed (plan); the doc proposes the per-class slices.

## Next
Owner answers Q-246a-e. If Q-246e = yes: queue E1 (246a RayStats material split) then E3 (246c voxel grass fringe counts). E2 (clutter
layer) only after Q-246a and an amendment draft. The forest timing milestone (238) should check the 1.3-1.8 ms per % estimate.

## For the doc
Task 246 -- what filling the world costs (`WORLD_FILL_BUDGET.md`). Summary: trees do not make rays longer overall; they make them creep
through see-through leaves 0.1 m at a time. Solid things (rocks, logs, buildings) are cheap to draw and cost memory. See-through things
cost ~1.3-1.8 ms per 1 % of the screen (est.). Grass and small clutter must go on a separate near-camera layer (an amendment), not into
the voxels (est. +15-60 ms).

| forest_eye (per frame) | off | dense (default) |
|---|---|---|
| all ray steps, 16 deg / noon | 34.11 / 30.46 M | 32.94 / 28.41 M (-3 / -7 %) |
| fine 0.1 m steps | 16.42 / 13.72 M | 22.99 / 20.03 M (+40 / +46 %) |
| sky-light steps | 0.93 / 0.85 M | 2.00 / 1.74 M (+115 / +106 %) |
| leaf % of screen | 0 | 28 % |
| GPU body pool peak (cap 254,400) | 153,364 | 245,704 |

(`Screenshots/246/model.txt`, `costsplit_*.txt`)

| class | render path | est. ms tier A | est. MB | amendment? |
|---|---|---|---|---|
| solid props | voxels, sealed interiors | ~0 ray cost (+0.5 shading, guess) | 40 (bodies) | no |
| porous foliage | voxels; % of screen budget; solid-cored bushes | 1.3-1.8 per 1 % of screen | 0 new | no |
| ground clutter | separate near-camera cube-mesh layer, no shadows | ~2 (guess, E2) | 10 | **yes** |
| thin things | voxels, sparse | 0.3 | 5 | no |
| moving things | entity meshes (spec §5.4/6.2) | 1.5 reserve + 0.3 | 60 | only for real shadows |
| lights | torch flood | 1.0 night reserve | 0 new | no |

Images:
- `Screenshots/246/forest_eye_off_vs_dense_16.jpg`: left trees OFF, right dense trees, same pose, 16 deg sun. Look at how the trees hide
  the far valley: the ray work moves from cheap far terrain into the crowns.
- `Screenshots/246/sheet246.jpg`: rows forest_eye 16 deg / noon / walk_open 16 deg; columns off / pre-210 / sparse / dense. Row 3 is the
  control: no trees there, identical in every column.

Questions (recommended answer first):
- Q-246a: put grass and small clutter on a separate near-camera layer (an amendment)? Yes.
- Q-246b: make bushes solid inside? Yes.
- Q-246c: take the clutter layer's ~2 ms from the reflections reserve, or wait for the sun savings (F1/F2)? Wait for F1/F2.
- Q-246d: creatures with no real shadow at first? Yes.
- Q-246e: queue E1 then E3 now? Yes.
