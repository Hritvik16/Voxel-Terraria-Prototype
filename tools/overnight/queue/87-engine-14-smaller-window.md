# 87-engine-14-smaller-window

Approval: engine plan step 14 (`ENGINE_PHASE_PLAN.md` row 14, owner-approved plan) and Amendment 8.14 row 6 (ADOPTED
2026-10-11). Queued by the supervisor 2026-10-13 after 85b; runs after 86. Branch `engine-phase`. Steps 12 (task 85) and
13 (tasks 81/82) are done, so its prerequisites are met.

## Change (one change: shrink the 0.1 m window on the SHELLS path)
Read Amendment 8.14 rows 1, 6, 7, 11, 15 and its sizing table (section 2/H) first; the numbers below are copied from it.
1. With `-shells 1`, the 0.1 m window (shell 0) is sized by the quality preset from `ShellLayout`: radius = shell 0's
   hand-over + a 12.8 m prefetch margin = **Low 59 m, Medium 65 m, High 82 m, Ultra 116 m** (today 128 m). CPU load radius
   (`EngineConfig.cs:268`, today 10 chunks) -> **5 / 6 / 7 / 10**. Beyond it shell 1 (0.2 m) draws.
2. **The CPU ring stays 32 x 32 x 16 on every preset** (`WINDOW_CHUNKS_Y` stays 16 -- CLAUDE.md landmine; oracle tests build
   16-tall windows). Only the load radius inside it changes.
3. GPU mirror box for shell 0: **16^3 on Low/Medium/High**, Ultra keeps today's 32 x 32 x 8 (task 32's out-of-box skip already
   handles resident chunks outside it). Pow2 asserted. The 0.1 m pool cap: `PeakUsed` x 1.25 after the shrink (High is the
   only preset measurable here; the others keep the amendment's estimate: 130 k / 182 k / 242 k / 474 k bricks).
4. **Awake fluid (Q4 (a)):** chunks holding awake fluid stay resident inside the 32-chunk CPU ring even outside the new radius
   ("eviction not selected"; §4.4 table unchanged). Beyond the ring waits for step 19 -- do not build that.
5. **Shells OFF keeps today's 128 m window and load radius 10, byte-identical.** Cascade tiers 1-2 are tied to tier-0 slots,
   so the shrink is only valid where shells draw beyond the window. (The owner is being asked, Q49, whether shells become the
   play default; that is a separate task. Do not change any default here.)
6. Frozen APIs untouched: `GetVoxel` still answers resident chunks only (its reach becomes 59-116 m with shells on);
   `SetVoxel` is still refused outside loaded chunks; a far edit uses `IEditOps.Record` (task 81).

## Acceptance
- Tests: `ShellLayout` radius / load radius / mirror box per preset (pow2 asserted); a fluid chunk outside the radius stays
  resident; an edit beyond the new radius via `IEditOps.Record` shows after the chunk loads (reuse `EditOpTests` helpers);
  shells-off path byte-identical (`run-phase7-rig.sh` 19/19 shots identical to 85b's, PASS 37 FAIL 0).
- Memory (not frame time): peak process MB with `-shells 1` before -> after at the same poses, and the 0.1 m pool `PeakUsed`
  (the plan estimates -400 to -600 MB on High). Report the measured number; set the High cap from it.
- `run-acceptance-rig.sh` gates C and D green with shells ON if the rig can pass `-shells 1`; if it cannot, say so and
  report the shells-off run only. Quote NO frame time (PREAMBLE rule 10); gates' pass/fail only. Never loosen a gate.
- Stills, ONE launch (<= ~10 min, short fixed waits, PREAMBLE launch budget), shells ON, before (`-window128` or the
  previous build) vs after, at 3 poses where the 82 m boundary is in view (e.g. eye_level_3, glide_400m, cliff_foot_1), plus
  the `-shellsdebug` tint so the shell-0 / shell-1 boundary is visible. Look for seams, holes or popping at the boundary.
  Sheet with captions saying what to look at.
- Fast EditMode while iterating, `SUITE=all` once at the end.
- HANDOFF `## For the doc`: window radius before -> after, peak MB before -> after, gates C/D, the sheet.

## Requeued (supervisor, 2026-10-15)
The 2026-10-13 run never started work: every attempt hit the weekly usage limit (runner.log 05:57-12:46, api 429), so it
was set aside as FAILED. No code was touched. Run it exactly as written. Owner context (2026-10-15): Q49 is answered
"yes for the visuals, but show me the frame time first" -> tasks 89 (measure) and 90 (default) follow this one; this task
still changes no default.
