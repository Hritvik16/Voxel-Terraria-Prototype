# RIG_REFRESH_PLAN.md -- audit of every `run-*.sh` and the plan to refresh them

Task 144 (from `tools/overnight/later/78-rig-refresh.md`, owner-approved 2026-10-11). AUDIT + PLAN ONLY:
no rig was changed, retired or run in this task. Evidence = the scripts themselves, the newest report in each
rig's output folder (`~/Library/Application Support/DefaultCompany/Voxel Terraria 1 Byte BrickMap/<Rig>/`),
`Logs/`, `KNOWN_ISSUES.md`, `TASKS.md` and which queue/done task files name each script.
**Nothing below is a new measurement.**

## 1. The short version

- **54 scripts.** All 25 `-executeMethod` build/scene entry points they call still exist in `Assets/` (checked by
  name), so none is broken at the "does it compile and build" level -- but **30 are not named in any queue or done
  task file** (the queue started 2026-10-01), and the phase 5/6 and fluid rigs last reported on 2026-09-10..12,
  before Phase 7, the terrain overhaul and amendments 8.14-8.16.
- **What the game is today vs what rigs exercise:** play = tg2 on map v4 (rivers, ores, biome blend), Survival,
  the new `PlayerMotor.Step(dt, in MotorInput)`, FSR 3 dynamic 540-720 rows, LOD shells ON, fluid pool 480 tiles,
  fluid fall (8.16), fluid at 128 m everywhere (8.15 / task 93e), `PlaySettings.json`. **No rig runs that
  combination.** The closest are `run-tg2world.sh` (tg2 v4, but `MODE=walk` uses `-renderpreset performance`
  = 480x270 FSR 1, shells OFF) and `run-cost-split.sh` (tg2 v4, quality preset, FSR 3 pinned at 540/720 rows,
  fullscreen 1080p, shells only if passed). Rigs never read `PlaySettings.json` (by design, task 58/93c).
- **Verdicts:** keep 9 (5 need only the lock / a small fix) · update 6 · merge 7 (then retire) · retire 32.
- **Proposed standing set (section 4):** correctness = EditMode + `run-phase7-rig.sh` + `run-acceptance-rig.sh`
  + `run-fluid-tiled.sh` + ONE new "gameplay-stack" rig on tg2; performance = ONE warm frame-time rig on tg2 v4
  with the play defaults (`run-perf-milestone.sh`, built from `run-tg2world.sh` walk + `run-cost-split.sh`).

## 2. Status of the drift the task named

| Drift (task 78 list) | Status found 2026-10-25 |
|---|---|
| Rigs run Playground/arena or synthetic scenes, not tg2 v4 | TRUE for 39 of 54. Only tg2world, cost-split, look-review, play, upscaler-*, shadow-dots pass `-world tg2`. The correctness rigs (acceptance, phase 7, fluid-tiled, phase 5/6) use their own synthetic scenes -- that is CORRECT for identity/conservation gates (they need a controlled world) and should stay. |
| Creative, legacy `PlayerMotor.Step`, no upscaler, pool 1,024, windowed, ignore `PlaySettings.json` | TRUE for every rig (TASKS.md task 64: "rigs keep the legacy Step"; task 55: rigs keep 1,024 tiles; task 93c: rigs read no PlaySettings, shells only with `-shells 1`). Acceptance rig launches `open -n -W ... -cleardeltas` = windowed. Only cost-split/upscaler-realrate run fullscreen 1080p with an upscaler. |
| "Interactive Playground no longer renders the way the rigs do" | STILL TRUE, and wider than 2026-09-26: play now differs by FSR 3 dyn + shells + Survival + motor, not only the render preset. |
| `-sustained -motion orbit` passes through rock; probes "inside a hole" | STILL TRUE (owner kept it for historical comparisons). Users today: run-farband, run-fixes, run-lavalight-ab, run-roofskip-ab, run-upscale-ab, run-looksweep, run-perf60 (uses `-orbitheight 8` arms), run-phase7-lighting-ab, run-fluidtick-diag. All are in the retire/replace group below. |
| `run-phase6-brushguard.sh` red since Playground was tiled | STILL RED: last report `Phase6BrushGuard/20260911_225955` RESULT FAILED (15/15). Rig premise (dense `InRegion`) is stale, product is fine (KNOWN_ISSUES). |
| `run-fluid-tiled.sh` rebased 2026-09-11 | GREEN: `FluidTiled/20261017_050829` PASS 19 FAIL 0 (task 94, after 8.16). Rebase recorded in source; nothing to do. |
| Gates C/D assume no live fluid | Unchanged and correct (RIG SELECTION RULE). `run-acceptance-fluid.sh` exists for the fluid case; its C/D reds are by design. Never loosen. |
| Gate C steady-state p99 upload red | STILL RED: last three runs (2026-10-16 x2, 2026-10-17) PASS 52 FAIL 1, p99 1.315-1.538 ms vs 1.0 ms. Everything else green. |
| One-off A/B / diagnostic scripts | 32 to retire (section 3; 19 of them are pure one-off A/Bs, section 3c). |
| (new) Old scripts don't take the Unity lock | 29 scripts have no `unity_lock_acquire` / `with-unity-lock`: every phase 4/5/6 and fluid rig, `run-editmode-tests.sh`, `run-phase7-rig.sh`, `run-acceptance-rig.sh`, `run-phase7-rung1.sh`, `run-playground.sh`, `run-playtest-bugs.sh`. |
| (new) `MODE=walk` is not the play path | `run-tg2world.sh` MODE=walk = `-renderpreset performance -gateres 0`, no `-shells 1`, no FSR 3. Task 146 (perf milestone) asks for "the PLAY defaults" from it, which needs `EXTRA=` flags today. |
| (new) Copied header | `run-fluid-activity.sh` lines 1-30 are a verbatim copy of `run-phase6-sandbox.sh`'s header ("§13 PHASE 6'S INTEGRATED ACCEPTANCE RIG"); only the build/scene names differ. |

## 3. The table

Status key: **G** last report green · **R** red · **?** no run since 2026-09-24 (behaviour under today's engine unknown)
· **L** a look/capture tool, no pass/fail. "Last" = newest output or log found. Effort: S < 1 h, M ~ one task, L > one task.

### 3a. Correctness gates

| Script | Purpose / phase | Last, status | Verdict | Effort | Reason |
|---|---|---|---|---|---|
| run-editmode-tests.sh | EditMode suite (fast / `SUITE=all`) | daily, G (PASS 935) | keep | S | Add the lock inside (today every caller wraps it). |
| run-phase7-rig.sh | §13 Phase 7 lighting gates, Phase 7 Cycle scene | 2026-10-17, G 37/0 | keep | S | Still the lighting correctness gate; add the lock. |
| run-acceptance-rig.sh | Phase 4 gates A-D: admission, upload budget, persistence identity | 2026-10-17, R 52/1 (Gate C p99) | keep + owner Q2 | S | Identity/persistence gates are irreplaceable; add the lock; Gate C's p99 is a TIMING check inside a correctness rig, measured cold+windowed -- owner decides its role (Q2). |
| run-fluid-tiled.sh | §7.2 sparse tiled fluid acceptance | 2026-10-17, G 19/0 | keep | S | Green after 8.16; add the lock. |
| run-acceptance-fluid.sh | Phase 4 rig + live tiled fluid (C/D red by design) | 2026-10-02 (task 07) | keep | S | The only streaming x fluid frame-time + no-crash rig; add the lock; arena world -- its timing is NOT play's. |
| run-phase5a-rig.sh | Phase 5a basin: conservation, ownership, 5 scenarios | 2026-09-11 ? | merge into gameplay-stack rig | M | Its conservation asserts predate 8.16 fall; keep the conservation asserts, drop the scene. |
| run-phase5b-rig.sh | 5b GPU port vs CPU oracle | 2026-09 ? | update | M | GPU vs CPU-oracle fluid parity is still worth a gate; must be re-checked against 8.16 (fall rule changed in both paths?) before trusting a red. |
| run-phase5b-check.sh | 5b fast checkpoint (sand_column only) | 2026-09 ? | retire | - | Iteration helper for a finished fix; 5b-rig covers it. |
| run-phase5c-rig.sh | 5c edit-path stress | 2026-09-11 G | merge into gameplay-stack rig | M | Edit path is frozen (`IEditService`); fold its stress into one rig. |
| run-phase5d-rig.sh | 5d streaming x fluid conservation across eviction | 2026-09-11 G | update | M | Conservation across residency edges is still load-bearing; fluid now runs to 128 m via the task 93e directory, which 5d predates. |
| run-phase6-player.sh | Phase 6 file 1, player controller | never output ? | merge into gameplay-stack rig | M | Tests the legacy Step; the shipped motor is the new one. |
| run-phase6-ccd.sh | Phase 6 file 2, swept CCD (§8.2) | never output ? | merge into gameplay-stack rig | M | CCD is frozen and still used; one rig is enough. |
| run-phase6-edit.sh | Phase 6 file 3, edit service (§8.3) | never output ? | merge (same) | M | As above. |
| run-phase6-brushguard.sh | Phase 6 brush guard | 2026-09-11 R 15/15 | update (rewrite predicate to the wake radius) or merge | M | Rig stale, product fine (KNOWN_ISSUES); never loosen, re-express the arena predicate. |
| run-phase6-sandbox.sh | §13 Phase 6 integrated: all systems sequentially (dense fluid) | 2026-09-11 G | retire (superseded) | - | Builds the old DENSE simulation (its own header); combined supersedes it. |
| run-phase6-combined.sh | All Phase 6 systems concurrently on tiled fluid | never output ? | **update -> the gameplay-stack rig** | L | Best base for the new correctness rig: move to tg2 v4, Survival, new motor, pool 480. |
| run-fluid-activity.sh | FluidActivity rig (header is a copy of sandbox's) | 2026-09-12 G | retire | - | Purpose duplicated by fluid-tiled + combined; header misleading. |

### 3b. Performance rigs

| Script | Purpose / phase | Last, status | Verdict | Effort | Reason |
|---|---|---|---|---|---|
| run-tg2world.sh | tg2 v4 poses, stream/walk/orbit/shimmer/fluid/pool probes, 3 GB footprint | 2026-10-25, L | keep + update | M | The live perf tool (tasks 145/146 use it). Add `MODE=playwalk` = walk with the play defaults (FSR 3 dyn, `-shells 1`, quality preset, fullscreen) so the milestone stops depending on hand-typed EXTRA. |
| run-cost-split.sh | per-frame ms split at walk_near/walk_open, FSR 3, fullscreen | 2026-10-24, L | keep -> merge into run-perf-milestone | M | The current baseline source (2026-10-22). |
| run-phase7-lighting-ab.sh | Amdt 8.12 substitute budget gate, 4 shadow rungs, arena orbit | 2026-09-12 | update or retire -- **owner Q3** | L | An ADOPTED amendment's gate, so not retired silently; it runs the arena orbit (through rock) at the 960 gate. Recommend re-basing it as an arm of run-perf-milestone on tg2. |
| run-perf60.sh | 60 fps target arms (presets, orbitheight 8) | 2026-09-26 | retire | - | Target is now 30 fps on tg2 (task 146); presets changed. |
| run-fluid-benchmark.sh | §13 5b perf gate, Playground | 2026-09 ? | retire | - | Arena fluid; fluid cost now measured by tg2 fluidprobe / acceptance-fluid. |
| run-fluid-ab.sh | dense vs tiled wall clock | 2026-09 | retire | - | Question closed (tiled shipped). |
| run-fluid-scale.sh | §7 slot/memory counters | 2026-09-12 | retire | - | Dense-slot era; pool memory now in tg2 `MODE=poolprobe`. |
| run-fluid-chaos.sh | chaos ladder of fluid volume | 2026-09-10 G | retire (or merge as a stress arm later) | - | Arena world, legacy motor; revive only if the gameplay-stack rig needs a stress arm. |
| run-late-game-siege.sh | heavy fluid + all Phase 6 systems, sustained | 2026-09 ? | merge into gameplay-stack rig (soak arm) | M | The "everything at once for minutes" idea is good; its world is not. |
| run-phase6-qa.sh | per-system perf + visual QA | 2026-09-11 G | retire | - | One-off profiling session. |

### 3c. Diagnostics and A/Bs from finished tasks -- all retire

| Script | What it answered | Last | Verdict | Reason |
|---|---|---|---|---|
| run-fixes.sh | 2026-09-25 visual fixes A/B | 2026-09-25 | retire | Fixes shipped. |
| run-fluid-stagger.sh | staggered-drop diagnosis | 2026-09-11 | retire | Diagnosis only, closed. |
| run-fluid-tilecap.sh | frozen cube / tile cap ladder | 2026-09-11 | retire | Cap decided (1,024 rigs / 480 play). |
| run-fluidtick-diag.sh | why a CA tick took 6-7 frames | 2026-09-24 | retire | Superseded by task 91/97 fixed-rate ticks. |
| run-flyfast.sh | 2026-09-25 stutter + seams | 2026-09-25 | retire | Diagnosis done (tasks 16-26). |
| run-lavalight-ab.sh | lava aggregate light ON/OFF | 2026-10-09 | retire | Shipped (owner decision 8). |
| run-lodpop.sh | LOD tier switch visibility | 2026-09-25 | retire | Answered; shells replaced the question. |
| run-looksweep.sh | cinematic grade over a day | 2026-09-24 | retire | Superseded by run-look-review.sh. |
| run-overhaul-batch.sh | 2026-09-25 brief batch | 2026-10-03 | retire | One-off batch. |
| run-phase7-rung1.sh | Stage E rung 1 cost | 2026-09-13 | retire | Answered in PHASE_7_COMPLETION; no lock. |
| run-roofskip-ab.sh | shadow roof skip A/B | 2026-10-10 | retire | Shipped ON (task 59). |
| run-shadow-dots.sh | task 56 shadow dots | 2026-10-10 | retire | Fixed. |
| run-torchshadow-shots.sh | task 46 torch shadow pairs | 2026-10-10 | retire | Shipped. |
| run-upscale-ab.sh | URP upscalers vs native | 2026-10-03 | retire | Decided: FSR 3 (task 52). |
| run-upscaler-smoke.sh | task 47 one still per upscaler | 2026-10-10 | retire | Done. |
| run-upscaler-compare.sh | task 48 shimmer per upscaler | 2026-10-10 | retire | Done. |
| run-upscaler-clips.sh | task 50 clips | 2026-10-10 | retire | Done (the `-upscalerclips` C# mode stays: cost-split and look-review use it). |
| run-upscaler-realrate.sh | task 51 real-rate clips | 2026-10-10 | retire | Done. |
| run-playtest-bugs.sh | step-0 diagnosis of two playtest bugs | 2026-09 | retire | Closed. |

### 3d. Look / play launchers

| Script | Purpose | Last | Verdict | Effort | Reason |
|---|---|---|---|---|---|
| run-play.sh | play the newest world | 2026-10-17+ | keep | - | The owner's launcher. |
| run-look-review.sh | task 114 look review per preset | 2026-10-25 | keep | - | Current visual QA tool (task 143 used it). |
| run-devmap.sh | photograph the dev map | 2026-09-25 | keep | S | Useful for world-gen work (quality pass); default world should become tg2 v4 (check `LEGACY`). |
| run-farband.sh | far band visual / ground arms / preview | 2026-10-03 | merge `preview`/`visual` into run-tg2world, retire the arena `ground` arms | S | tg2world already has `farpreview`. |
| run-demo.sh | look at the overhaul / tg2 | 2026-10-08 | retire | - | Its own header points to run-play.sh. |
| run-playground.sh | Playground first look | 2026-09-10 | retire | - | Superseded by run-play.sh; no lock. |
| run-phase7-demo.sh | Phase 7 §12 review, watchable | 2026-09-24 | retire | - | Phase 7 review done. |
| run-phase5b-demo.sh | 5b demo scene | 2026-09 | retire | - | Demo of a closed phase. |

Count: keep 9 (editmode, phase7-rig, acceptance-rig, fluid-tiled, acceptance-fluid, tg2world, play, look-review,
devmap) · update 6 (5b-rig, 5d-rig, brushguard, phase6-combined, phase7-lighting-ab [owner Q3], cost-split->milestone)
· merge 7 (5a, 5c, 6-player, 6-ccd, 6-edit, late-game-siege -> gameplay-stack; farband -> tg2world), each retired
once merged · retire 32 now (3 in 3a, 6 in 3b, 19 in 3c, 4 in 3d). 9 + 6 + 7 + 32 = 54.

## 4. Proposed standing set

**Correctness (before any merge to `main`, and at the engine-phase milestone):**
1. `SUITE=all ./run-editmode-tests.sh`
2. `./run-phase7-rig.sh` (lighting)
3. `./run-acceptance-rig.sh` (streaming, upload budget, persistence identity) -- synthetic world on purpose
4. `./run-fluid-tiled.sh` (tiled fluid acceptance)
5. NEW `./run-gameplay-stack.sh` = phase6-combined moved onto tg2 v4: Survival, the new motor, pool 480, shells ON,
   fluid fall; folds in 5a's conservation asserts, 5c's edit stress, 6-player/ccd/edit's checks, brush guard
   (wake-radius predicate) and late-game-siege's soak as one launch with sections. Identity/hash checks stay
   strict; anything that legitimately changes under live fluid is asserted as conservation, not identity.
6. `./run-acceptance-fluid.sh` only when a change touches fluid x streaming.

**Performance (only in performance tasks / milestones):**
- NEW `./run-perf-milestone.sh`: ONE warm-up launch (discarded) + ABBA arms of the PLAY path on tg2 v4 -- quality
  preset, FSR 3 dyn, shells ON, fullscreen 1080p, Survival walk (standard walk + walk_near/walk_open cost split),
  printing p50/p95/p99, share > 33 ms and the dyn row choice, plus the 3 GB footprint line. Optional arm for 8.12's
  shadow rungs if the owner says so (Q3). This replaces perf60, phase7-lighting-ab's arena orbit, fluid-benchmark
  and the frame-total line of the acceptance rig as "the number that matters".

**Look:** `run-play.sh`, `run-look-review.sh`, `run-devmap.sh`, `run-tg2world.sh` (poses/farpreview/probes).

## 5. Follow-up tasks (each needs the owner's OK; none runs a timing arm except R5)

| # | Task | Effort | Depends |
|---|---|---|---|
| R1 | Retire: `git mv` the 32 scripts to `tools/retired-rigs/` + a README row each (what it answered, last report path). C# capture modes and scenes stay. **DONE task 149**: 31 moved; `run-playground.sh` left (cited by `tools/capture-gpu-trace.sh`). Task 154 retired it too (capture-gpu-trace.sh's hint now names `BUILD=1 ./run-play.sh` + the trace build). | S | Q1 |
| R2 | Lock: add `unity_lock_acquire` inside the 5 kept lockless rigs (editmode, phase7, acceptance, acceptance-fluid, fluid-tiled) (no behaviour change). **DONE task 148**, plus the 4 lockless 'update' rigs (5b, 5d, brushguard, combined). | S | - |
| R3 | `run-gameplay-stack.sh` from phase6-combined on tg2 v4 (section 4 item 5); then retire 5a/5c/6-* scripts. | L | Q4 |
| R4 | Update 5b-rig and 5d-rig for 8.15/8.16 (or fold into R3); brush guard predicate rewrite. | M | R3 |
| R5 | `run-perf-milestone.sh` (tg2world `MODE=playwalk` + cost split, warm-up built in). First run = engine-phase milestone. | M | Q2, Q3 |
| R6 | Docs: CLAUDE.md "build-run-review loop" and "Commands" point at the new set; KNOWN_ISSUES rig bullets closed or moved. | S | R1-R5 |

## 6. Questions for the owner (STOP_FOR_OWNER)

- **Q1 -- retire the 32 scripts listed as "retire"?** (a, recommended) yes, move them to `tools/retired-rigs/`
  (history kept, `git log` intact); (b) keep them where they are; (c) retire only section 3c (19 one-off A/Bs).
- **Q2 -- what is the acceptance rig's frame time for?** Today CLAUDE.md says its Gate C "frame total" is the number
  that matters, but it runs a synthetic world, windowed, cold, at the 960 gate -- not what you play. (a, recommended)
  the acceptance rig becomes correctness-only: Gate C's p99 upload check stays as written (still red, still
  reported), and frame time is quoted only from run-perf-milestone; (b) keep both as today.
- **Q3 -- Amendment 8.12's shadow-rung gate (`run-phase7-lighting-ab.sh`)?** (a, recommended) re-base it as an arm
  of run-perf-milestone on tg2 (needs a short amendment note, since 8.12 names the arena orbit); (b) keep the arena
  rig as is for historical comparison; (c) retire it.
- **Q4 -- build the gameplay-stack rig now or after the quality pass?** (a, recommended) after the quality pass
  (no gameplay content changes until then; R1/R2/R5 can go first); (b) now.
