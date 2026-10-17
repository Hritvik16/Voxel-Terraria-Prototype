# 101-engine-c1-world-clock

Requires: 93g-engine-14d4-cpu-brick-cap-ledger

Approval: owner, 2026-10-15, Amendment 8.15 Q10 (a) (ADOPTED): the owed part E1-E2, D1-D4, **C1**, Q1-Q2 in that order
(`AMENDMENT_8_15_SIMULATION_REACH.md` line ~360). E1 (93a) and D1-D4 (93d-93g) are done; 89/90 (shells default, 93b/93c)
is held on a power-only block (Mac on battery), and C1 does not depend on it. Queued by the supervisor 2026-10-17.
Branch `engine-phase`.

## Change
Implement row **C1** of `AMENDMENT_8_15_SIMULATION_REACH.md` exactly, nothing else: a persistent **world clock** (game
seconds, saved), a **chunk last-seen side table** (§4, line ~228), the **world stage byte** (= 0) in the world-state file
C1 creates, and a **per-chunk generation-stage byte** (§14's hook, line ~425-441). NO behaviour reads them yet (C2 catch-up,
S1 stage flip are later rows). Read §4 and §14 and Appendix D (D.2 / the 8.15 row 6 "NEW" entry) first.
- Saved beside the deltas (same folder rules as `ops.log` / D.1); written atomically; a world with no file loads as clock 0,
  stage 0, all chunks last-seen = never, generation stage 0.
- Does NOT change D.1, `GetVoxel`/`SetVoxel`, the 4.4 table or chunk/brick memory layout (side tables only). If C1 turns out
  to need any of those, STOP_FOR_OWNER.
- Clock advances with game time in play; rigs/capture runs: say what they do (recommended: advance, but never write the file
  unless the run already saves deltas). Last-seen is stamped on eviction/save of a resident chunk.

## Acceptance
- EditMode tests (new `WorldClockTests` or similar): save/load round trip, missing file defaults, last-seen stamped on
  eviction, stage bytes persist, corrupted/short file -> defaults + a warning (no crash).
- Fast EditMode suite green.
- No frame-time measurement (not a performance task). No screenshots needed (nothing visible): say so in `## For the doc`.
- HANDOFF `## For the doc`: one-line result, the file format (fields, bytes), test counts.
