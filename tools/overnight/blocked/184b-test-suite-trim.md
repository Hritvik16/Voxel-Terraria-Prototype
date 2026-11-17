# 184b-test-suite-trim

Model: opus

Approval: owner, 2026-11-07: "the test suite should never be more than max 15 mins. 70 mins of waiting is insane for development." HARD BUDGET: `SUITE=all ./run-editmode-tests.sh` <= 15 min wall; the fast suite <= 3 min. Test-infrastructure work only: no product code changes.

## Evidence to start from
`TestResults_all.xml` (last SUITE=all, 1,133 cases, 4,264 s total; the 25 slowest = 71 %). Worst: RedrawTests.Shells1to7_OpRecordedAfterTheFill_RedrawsEveryTouchedChunk_ByteExact_StaleFree 774 s,
ShellFillTests.Fill_KeepsWhatRaysSee_KeepsMouths_DropsDenseBricks 331 s, OreRuleTests.FourSeeds_StartHidden_AndTable 249 s, ShellColumnReuseTests.WalkingTwin_5mps_Shell1KeepsFilling 174 s,
ShellChunkTests.CoarseFirst_AgreesWithMajorityVote_AndReportsCost 108 s, FluidAtRestTests.RiverSeaLava_AreAtRest(2026) 94 s, BrickSkipTests.Shells_SkipOnAndOff_ByteIdentical (+_Ores) ~90 s each.

## Do
1. Per-test table (duration, category, what world/seeds/levels it builds) for the top ~40, from the XML. Find WHY each is slow before changing it (world size, 4 seeds, 7 shell levels, repeated rebuilds of the same map that `SharedMaps` could share, Burst off in Editor, per-chunk diffing).
2. Cut by exact means, in this order: share builds (SharedMaps / one world per spec per run); shrink the world / number of chunks / shell levels to the smallest that still reaches every code path the assertion covers; split 4-seed tests into one case per seed so they are not one 4x serial test; only then reduce seeds on tests whose seed-dependence is not the point (keep >= 2 seeds incl. one adversarial for generator-rule tests).
3. NEVER weaken an assertion, loosen a tolerance, or delete a test. For every trimmed test, prove it still catches its bug: re-run it against a deliberately broken variant (a mutation of the code it guards, reverted after) and show it still fails, for the 5 largest trims at least. Record in HANDOFF.
4. Anything that still cannot fit: tag `[Category("Slow")]`, and add a THIRD tier documented in the runner (e.g. `SUITE=nightly`) that the queue runs only before a merge to `main`; `SUITE=all` itself must still be <= 15 min and must include every generator/rule test at least once.
5. Report-only tests stay opt-in. Check test-runner overhead too (domain reloads, asset refresh, the 45 s lock wait is not counted).
6. Measure: run `SUITE=all` and fast twice at the end; report wall for each. (This is a test-suite task, so timing it is the task.) Quote the Mac state (power, other Unity processes: the lock covers that).
7. HANDOFF: before/after per-test table for the trimmed tests, total wall before -> after, the mutation proofs, and a proposed one-line CLAUDE.md / PREAMBLE edit stating the budgets (do NOT edit CLAUDE.md yourself; the owner edits it).

## End
Done when SUITE=all <= 15 min and fast <= 3 min with the same pass counts (no fewer assertions). If the budget cannot be met without weakening a test: STOP_FOR_OWNER with the list of the tests and the options.
