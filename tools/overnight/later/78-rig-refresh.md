# 78-rig-refresh (FAR FUTURE -- parked, not queued)

Approval: owner, 2026-10-11 in chat: "a lot of rig tests we have are not updated to the latest state of the game. We
might want to put in a task for the far future to take care of this." Parked by the supervisor in `later/`.
**When:** before engine step 23 (the engine-phase milestone runs every rig), or earlier if the owner asks. Likely
more than one task: this file is the audit + plan; it may split itself into follow-ups.

## Change
1. **Audit every `run-*.sh` (~55 scripts) and its capture mode**: what it was built to prove, which phase, whether it
   still runs, and what the game has changed under it. Known drift to check at least (KNOWN_ISSUES / BUGS):
   - Rigs run the Playground/arena or the old v3 world, not tg2 map v4 (rivers, ores, presets after 8.14).
   - Rigs stay Creative, use the legacy `PlayerMotor.Step`, no upscaler (play: FSR 3 dyn), fluid pool 1,024 tiles
     (play: 480), measure WINDOWED (`run-acceptance-rig.sh`), ignore `PlaySettings.json`.
   - "The interactive Playground no longer renders the way the rigs do" (KNOWN_ISSUES).
   - `-sustained -motion orbit` passes through rock; "every Playground probe measured inside a hole".
   - `run-phase6-brushguard.sh` red since Playground was tiled (the rig is stale, not the product).
   - `run-fluid-tiled.sh` memory assertions rebased 2026-09-11; Gates C/D assume no live fluid (RIG SELECTION RULE).
   - Gate C's steady-state p99 upload check red.
   - One-off A/B and diagnostic scripts from finished tasks (`run-roofskip-ab.sh`, `run-upscaler-*`,
     `run-fluidtick-diag.sh`, ...) that nobody needs any more.
2. **Classify each:** keep as is / update to the current game / merge into another rig / retire (move to
   `tools/retired-rigs/`, never delete history). Owner rule: identity/hash gates are never loosened to make them pass.
3. **Write `RIG_REFRESH_PLAN.md`:** the table (script, purpose, status, verdict, effort S/M/L), a proposed small
   standing set (e.g. correctness: EditMode + phase 7 + acceptance + fluid; performance: one warm frame-time rig on
   tg2 v4 at the High preset), and the follow-up tasks. STOP_FOR_OWNER for approval before changing or retiring
   any rig.

## Acceptance
- Every script in the table, each verdict with a one-line reason; no rig changed in this task.
- No frame-time measurement (reading old reports is fine).
