STOP_FOR_OWNER: SUITE=all is now 20m46s (was 72m33s), NOT <= 15 min. The fast suite meets its budget (2m34s). What is left in `all` is generator/rule tests that must stay there, and I found no further exact cut I could prove in this task. Which way?
- **(a) RECOMMENDED, with (c): run seed 42 in Nightly for the per-seed generator tests** (FluidAtRest, LakeShore, LakeLevel, OreRule). `all` keeps the adversarial seed 31337 only; nightly keeps all 4 seeds. Saves ~240 s, giving ~16.8 min. This breaks the task's "keep >= 2 seeds in all" rule, which is why it needs you.
- **(b) Accept `all` ~21 min** as the budget.
- **(c) Follow-up task (exact, test code only): run the managed column-sampling / chunk loops on worker threads.** Targets: MaterialRule.FourSeeds 78 s, Cover.FourSeeds 76 s, the OreRule deposit table 34 s per seed, FluidPocket.Sites 62 s, MaterialRuleChunk.Shells 43 s. Estimate -150 to -200 s (not proven on this fanless M1). With (a): ~13.5-14.5 min.

# 184b-test-suite-trim: three tiers + exact assert cuts. fast 2m34s (budget met), all 20m46s (budget missed), nightly = everything

## What changed (commit 9520e4e; test code + runner only, no product code)
- `run-editmode-tests.sh`:
  - `SUITE=fast` = `!Slow`; `SUITE=all` = `!Nightly`; `SUITE=nightly` = everything (before a merge to main).
  - `FILTER=` and `EXTRA=` pass through to Unity. In FILTER, write `(42)` as `\(42\)` (it is a regex).
  - A Nightly test is always also Slow.
- Nightly (101 attribute lines, 48 files, by measured time in the old 72-min XML):
  - the shell fills
  - the long fuzzers
  - both `Cost_OnVsOff` reports (one asserts nothing, the other only `dense > 0`)
  - every engine test >= 2 s
  - seeds 7 and 2026 of FluidAtRest / LakeShore / LakeLevel / OreRule
- Generator tests >= 2 s moved from fast to Slow (they stay in all). MaterialRule and Cover FourSeeds assert POOLED over seeds, so they stay whole in all.
- `OreRuleTests.FourSeeds` is now one case per seed. Its assert was the sum of per-seed failures, so the per-seed cases assert the same thing. Report: `Logs/TerrainGen/ores181/table_<seed>.txt`, now with phase timings.
- New `FastAssert` helper, plus guarded per-voxel asserts (`if (x != y) Assert.AreEqual(x, y, msg)`). Same check, same message; NUnit is only called when a check fails.

## Before -> after, per trimmed test (s; before = old 72-min run, after = this session)
| test | before | after | how |
|---|---|---|---|
| MaterialRuleChunk Chunks_Deterministic | 52.9 | 6.2 | FastAssert + guard |
| OccupancyMask RebuildRegion_AfterAddingSolid / AllAir / AfterClearing | 19.6 / 18.2 / 17.5 | 4.8 / 3.1 / 3.4 | guard (Nightly) |
| AirMip RebuildRegion_AfterClearing / AllAir | 15.9 / 15.3 | 1.6 / 1.7 | guard; back in FAST |
| Tree Chunks_Deterministic_Border | 9.8 | 6.3 | FastAssert + guard |
| OreRule FourSeeds | 250 (one test) | 65 + 60 in all | split; 7 / 2026 Nightly |

- Whole suite:
  - all: 72m33s -> **20m46s / 20m47s**
  - fast: ~9 min of test time in the old fast tier (514 s) -> **2m34s / 2m40s** (the 2m40s run includes a recompile)
- Pass counts:
  - all: PASS 1027 FAIL 0 SKIP 25 (twice)
  - fast: PASS 966 FAIL 0 SKIP 19 (twice)
  - Nightly-only: PASS 80 FAIL 0 SKIP 10, 51m16s
  - 1027 + 80 = 1107 = the old 1104 + 3 new OreRule cases; skips 35 = 35. No test was deleted or weakened.
- Logs: `Logs/overnight/184b/final_{fast,all}{1,2}.out`, `nightly_only.out`, and the XMLs beside them.
- Mac state: AC power, the Unity lock held, no other Unity process. Not a timing-arm task; wall is quoted as measured.

## Mutation proofs (product code broken, then reverted with git checkout; 0 `MUTATION` lines left)
Log: `Logs/overnight/184b/mutants.out`, `mutant5b.out`.
- M1 OccupancyMask.Build (a stray bit in the last level-1 cell) -> AllAir FAILS "Level 1 cell 8388607 should have an all-clear mask".
- M2 AirMip.Build (the same stray bit) -> AllAir FAILS "Level 1 cell 8388607 should be air (0)".
- M3 GroundRules.NearMaterial (the skin band returns Air) -> MaterialRuleChunk FAILS "cliff_snow int3(413, 43, 330) voxel 703739: air / fluid must not change".
- M4 tree overlay (the "air only" check removed) -> Tree FAILS "voxel 281309: a tree replaced a non-air voxel (148)".
- M5 OreRules.StartHides -> always true:
  - Not caught. It did not expose any ore (the depth guard still held), so there was nothing to catch.
  - M5b: start-zone cells skip the depth guard -> OreRule(42) FAILS "rule ore with open sky beside it near a start" (23 exposed voxels at the zone's steepest cell).

## Other findings
- Map builds are 1.6 s each, so sharing them is no lever.
- The Editor already runs managed code in Release (EditorPrefs ScriptDebugInfoEnabled = 0). `-releaseCodeOptimization` measured 0 gain (`ab_*.out`).
- FluidAtRest / LakeShore / LakeLevel are already all-core Burst jobs. Their asserts cover every river and lake column, so they cannot be cut exactly.
- The old 72-min XML was overwritten by the first new `all` run (it is git-ignored). Its "before" numbers above come from this session's printout of it.

## Proposed edits (owner edits these; I did not)
- CLAUDE.md, the Commands block: "Budgets: fast (`./run-editmode-tests.sh`) <= 3 min, `SUITE=all` <= 15 min (generator changes), `SUITE=nightly` (~70 min) only before a merge to main; a test > 2 s is Slow, an engine test > 2 s or a cost report is Nightly (always also Slow)."
- tools/overnight/PREAMBLE.md line 23: "...`SUITE=all` when the task changes terrain-generator code; `SUITE=nightly` instead when preparing a merge to `main`."

## Next step
Owner picks (a) / (b) / (c). For (c), the supervisor queues the parallel-loops task (test code only, mutation proofs as here).

## For the doc
- SUITE=all wall: 72m33s -> 20m46s (`Logs/overnight/184_all.out` -> `Logs/overnight/184b/final_all2.out`)
- fast wall: ~9 min of test time -> 2m34s (`final_fast2.out`)
- nightly (everything): `all` + 51m16s of nightly-only = ~72 min, PASS 1107 (= 1104 + 3 split cases), FAIL 0
- biggest single exact cut: MaterialRuleChunk Chunks_Deterministic 52.9 -> 6.2 s (per-voxel NUnit asserts guarded)
- No images: this is a test-infrastructure task with nothing visible.
