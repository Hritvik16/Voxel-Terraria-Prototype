# 91-engine-17-fixed-rate-fluid-tick

Approval: engine plan step 17 (`ENGINE_PHASE_PLAN.md` row 17, owner-approved plan; E6 "a fixed tick rate, independent of
frame rate"; amendment row 5 "fluid tick contract"). Owner, 2026-10-15 in chat: "17 only" while the gameplay planning round
runs. Queued by the supervisor 2026-10-15. Branch `engine-phase`. Step 5 (awake-only fluid tiles, tasks 54/55) is done.

## Change (one change: option C of `FLUID_TICK_DIAGNOSIS.md` section 3)
Read `FLUID_TICK_DIAGNOSIS.md` in full first (section 1's measurement, section 3's table). Today one CA tick takes ~6-7
frames (7.25 measured at 33 FPS) because of the one-op-list-in-flight pipeline, so fluid speed follows frame rate.
- Add a **fixed-rate scheduler**: issue a tick at most every 1/R s, and never more often than the pipeline allows
  (`CanIssue`). **R = 4.5 Hz** (the diagnosis: today's worst case at 33 FPS is ~4.6 ticks/s). Lava keeps its own ratio
  (today every 6th tick) expressed against the fixed rate. R is one constant / config value, named, documented.
- **Never raise `MaxFramesInFlight`** and do not touch options A, B, D or E (A needs a correctness proof, E breaks the
  CPU-sole-writer invariant). B may be named as the next step in HANDOFF.
- When the frame rate is too low to deliver R, fluid slows: count the shortfall (missed ticks/s) and print it in the
  fluid probe output, so it is visible, not silent.
- Phase 5's decision 11 (6-frame tick) is superseded: say so in the HANDOFF and in a one-line note where decision 11 is
  documented (amendment row 5). No frozen API changes; the §7.2 tiled active-set contract is unchanged.

## Acceptance
- EditMode test: the scheduler issues the same ticks per simulated second at two frame rates (e.g. 30 and 60 FPS fake
  clocks), never exceeds R, never issues while `CanIssue` is false.
- Fluid correctness rigs green, with results quoted: `run-fluid-tiled.sh` (and its memory assertions as rebased
  2026-09-11), `MODE=fluidprobe ./run-tg2world.sh` (ticks/s and shortfall), `MODE=poolprobe` at the coast (no refusals).
  `FluidAtRestTests` green. RIG SELECTION RULE: identity gates C/D fail by design with live fluid -- never loosen them.
- Not a performance task: quote ticks/s (a rate the change defines), NOT frame time (PREAMBLE rule 10).
- One short clip or a 3-still strip of a pour at two frame caps (e.g. `-fpscap 30` vs 60 if a cap flag exists; say if not)
  showing the same fluid front at the same wall-clock time. Midday, sun behind the camera. Captions say what to look at.
- Fast EditMode while iterating, `SUITE=all` once at the end.
- HANDOFF `## For the doc`: ticks/s before -> after at 30 and 60 FPS, lava rate, shortfall line, rig results, the strip.
