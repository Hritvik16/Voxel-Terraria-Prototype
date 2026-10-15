# 90b-adopt-amendment-8-15

Approval: owner, 2026-10-15 in the supervisor chat: answered the 8.15 draft's Q1-Q10 (all (a)) plus two gaps the supervisor
added. The answers are in `tools/overnight/done/90a-amendment-8-15-simulation-reach.md` ("## Owner answer") -- read them
first. Queued by the supervisor 2026-10-15. Branch `engine-phase`. **PLANNING ONLY: no code, no Unity, no builds, no
launches, no tests.** Docs in the repo only (no claude.ai).

## Do
1. `AMENDMENT_8_15_SIMULATION_REACH.md`: change DRAFT to **ADOPTED 2026-10-15** and fold the answers in (the Questions
   section becomes a short "Owner answers" record). Add, each as a short section and a row in §11's task table:
   - **Look-ahead + far visuals (Q1 extra):** prefetch along the velocity at speed (relate to KNOWN_ISSUES "STREAMING
     REQUIREMENT, WORST CASE" and step 16), and VISUAL-ONLY far creatures (herds, birds drawn past 128 m, not simulated).
     Gameplay-phase rows unless an engine hook is owed now; say which.
   - **Stage flip (gap A):** world stage number in the save; the generator takes the stage as an input; a one-time pass adds
     new ores / biome spread ONLY in never-touched chunks (no D.1, no ops), so builds and digs are never overwritten; shells
     and far band redraw as areas reload. Name what the engine owes and when (likely gameplay phase; one hook now if cheap).
   - **Far reactions (gap B):** heat / gas / electricity / growth freeze far away; growth, melt and freeze resolve from
     elapsed time in the catch-up step (C2's hook); gas explosions and electricity near only.
2. `CLAUDE.md` "Ground truth" item 2: add 8.15 after 8.14 in the override order (one line, file name, adopted date). Do not
   rewrite anything else in CLAUDE.md.
3. Write the queue task files for the **first block of Q10 (a)'s order** into `tools/overnight/queue/`, one change each,
   numbered from 93 upward in run order, each with an `Approval:` line citing this owner answer, acceptance steps (tests,
   metrics, screenshots where something is visible; no frame-time claims, PREAMBLE rule 10) and real `Requires:` lines:
   E1, then 89 and 90 (copy and update `tools/overnight/later/89-shells-perf-breakdown.md` and `90-shells-default-in-play.md`;
   leave the originals in later/), then D1, D2, D3, D4. Copy the numbers and rules the worker needs INTO each file.
   D2-D4 implement the redone step 14 (Q2 (a)); write in D2's file that `tools/overnight/blocked/87-*` is superseded and must
   not be applied. Stop there: C1/Q1/Q2/E2 and later are written by the supervisor when these are done.
4. Commit by path. HANDOFF: what changed, the list of task files written (one line each), anything in the answers you found
   ambiguous. No `STOP_FOR_OWNER` unless an answer cannot be applied as written. `## For the doc`: the adopted summary in
   <= 8 lines and the new queue order. No images (planning only; say so).
