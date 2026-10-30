STOP_FOR_OWNER: approve the rig refresh plan (`RIG_REFRESH_PLAN.md` section 6, Q1-Q4) before any rig is changed or retired.
- Q1 retire 32 scripts to `tools/retired-rigs/`? (a, rec.) yes / (b) no / (c) only the 19 one-off A/Bs.
- Q2 acceptance rig = correctness only, frame time quoted only from a new tg2 play-path rig? (a, rec.) yes / (b) keep as today.
- Q3 Amdt 8.12 shadow-rung gate: (a, rec.) re-base as an arm of the new perf rig on tg2 / (b) keep the arena rig / (c) retire.
- Q4 build the gameplay-stack correctness rig (a, rec.) after the quality pass / (b) now.

# 144-rig-refresh-audit: DONE (audit + plan only; no rig changed, retired or run)

## What changed (commit below)
- `RIG_REFRESH_PLAN.md` (new): every one of the 54 `run-*.sh` in a table (purpose/phase, last report + status,
  verdict, effort, one-line reason); the status of each drift item task 78 named; a proposed standing set; follow-ups R1-R6.
- `TASKS.md`: "Task 144" paragraph.

## Findings
- All 25 `-executeMethod` build/scene methods the scripts call still exist (checked by name) -> nothing is broken at build level.
- Verdicts: keep 9 · update 6 · merge 7 (into a new `run-gameplay-stack.sh` based on `run-phase6-combined.sh`, on tg2 v4,
  Survival, new motor, pool 480, shells ON) · retire 32 (19 one-off A/Bs from finished tasks + 13 superseded rigs/launchers).
- Latest reports read (not re-run): acceptance rig 2026-10-17 PASS 52 FAIL 1 (Gate C steady p99 upload 1.36 ms vs 1.0;
  three runs 1.315-1.538); Phase 7 rig 2026-10-17 PASS 37 FAIL 0; fluid-tiled 2026-10-17 PASS 19 FAIL 0; brush guard
  still FAILED (2026-09-11, stale rig per KNOWN_ISSUES); phase 5/6 + fluid rigs last reported 2026-09-10..12 (before
  Phase 7, the overhaul, 8.14-8.16) -> their behaviour today is unknown, not failed.
- No rig runs play's combination (tg2 v4 + Survival + new motor + FSR 3 dyn + shells + pool 480). `run-tg2world.sh`
  MODE=walk uses `-renderpreset performance` (480x270 FSR 1), shells off -- task 146 asks for "the PLAY defaults" and
  must pass them via EXTRA today. Proposed fix: `MODE=playwalk` / `run-perf-milestone.sh` (follow-up R5).
- 29 scripts do not take the shared Unity lock, incl. the kept editmode/phase7/acceptance/acceptance-fluid/fluid-tiled (R2).
- `run-fluid-activity.sh`'s header is a verbatim copy of `run-phase6-sandbox.sh`'s (misleading; retire).

## Evidence
- Fast suite PASS 935 FAIL 0 SKIP 15 (`Logs/overnight/144-tests.out`) -- doc-only change.
- Reports read under `~/Library/Application Support/DefaultCompany/Voxel Terraria 1 Byte BrickMap/`
  (`Phase4Acceptance/2026-10-17_002443`, `Phase7Cycle/2026-10-17_174852`, `FluidTiled/20261017_050829`,
  `Phase6BrushGuard/20260911_225955`, ...). No frame-time measurement, no player launch.

## Unfinished / next
- Nothing changed in any rig, by the task's rule. Next: owner answers Q1-Q4; then R2 (lock, S) and R1 (retire, S) can be
  queued; R5 (perf-milestone rig) before/with task 146; R3 (gameplay-stack rig, L) after the quality pass if Q4 (a).
- No follow-up task file added (all follow-ups need the owner's answers).

## For the doc
- Rigs audited: 54 -> verdicts keep 9 / update 6 / merge 7 / retire 32 (`RIG_REFRESH_PLAN.md` section 3).
- Rigs on the play path (tg2 v4 + FSR 3 dyn + shells + Survival): 0 today -> 1 proposed (`run-perf-milestone.sh`).
- Correctness gates' last state: acceptance 52/1 (Gate C p99 1.36 ms vs 1.0 ms), Phase 7 37/0, fluid-tiled 19/0,
  brush guard red (stale rig); 16 phase 5/6/fluid rigs not run since 2026-09-12.
- Scripts without the shared Unity lock: 29 (5 of them in the proposed standing set).
- No images: an audit of scripts and old reports has nothing visible to show.
