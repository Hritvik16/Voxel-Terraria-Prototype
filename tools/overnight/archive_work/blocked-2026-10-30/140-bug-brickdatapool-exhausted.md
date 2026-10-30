# 140-bug-brickdatapool-exhausted

Model: opus

Approval: supervisor, 2026-10-25, under the owner's rule of the same day ("dont ever leave no task running") and the backlog rule
(bug fixes found by finished tasks). Tasks 137 and 138 both logged `InvalidOperationException: BrickDataPool exhausted. LRU valve failed
or cap is too low.` 16 times in the standard warm-up walk (`MODE=stream MOTION=walk ./run-tg2world.sh`, e.g.
`Logs/tg2world_20261024_231406.txt`). A bug fix inside a frozen system is fine; changing a pool CAP / memory budget or a frozen API is NOT
(stop and ask). Not a performance task: no timing arms.

## Do
1. Find where it throws (stack in the log), which pool, its cap and peak, and why the LRU valve did not free space first. Say what the
   player would see when it throws (a missing chunk / hole / stale brick, or nothing) -- check one screenshot if it is visible.
2. If the cause is a bug (wrong count, valve not running, leak, ordering) and the fix is small and contained: fix it, with a test that
   reproduces the exhaustion (EditMode, synthetic pool) and passes after. Re-run the warm-up walk once: 0 exceptions.
3. If the only fix is a larger cap or a different memory budget, do NOT change it: STOP_FOR_OWNER with the numbers (cap, peak, MB) and
   the options.
4. Fast EditMode suite once at the end (wait out an owner pause; never skip it).

## Deliverable
HANDOFF `## For the doc`: cause in plain words, what a player saw, the fix (or the question), exception count before -> after with the
log paths. TASKS.md paragraph if a flag or test was added.
