## YOUR TASK: 11-editmode-suite-speedup

Approved by the owner 2026-10-02 (in chat with the supervisor): speed up the
EditMode suite that every task runs; "yes" to the one choice in it -- the SLOW
group runs only before a merge to `main` and on any change to the terrain
generator, not in every task.

Measured by the supervisor (TestResults.xml of 2026-10-02 14:25): 874 s per run
= ~217 s Unity start/compile + 657 s of tests; 630 cases; the 20 slowest are 87%
of test time. Top: RegionTests.Regions_VarietyAndReport 108 s,
PaletteSampleTests.Report_PaletteSamples 86 s, TerrainGenTests.Determinism_
SameSeedSameWorld_DifferentSeedDifferent 54 s, RegionTests.MapV3_IsDeterministic
53 s, StreamingTests.TerrainAwareBand_ShiftsTowardTheCliff... 38 s,
VerticalStreamingTests.TallWorld_StreamsABand... 36 s, ShadowRayTests.Fuzz_
PenumbraNeverChangesTheOcclusionAnswer 34 s, StampTests.NoNeedles_OnTheMap 26 s.
Several build the same full map (~27 s per seed) again and again.

Do, in this order, test code and runner scripts only (no engine or generator
change):
1. Share built maps within one test run: build each (seed, sizeClass, version)
   map at most once and reuse it across fixtures (a static cache in the test
   assembly). A determinism test that must build twice still builds twice.
2. Report-only tests that assert nothing become opt-in by environment
   variable, like `PlayMetricsReport` (PLAYMETRICS=1). Check each candidate
   really asserts nothing before moving it; list them in HANDOFF.
3. Tag what is still slow (> ~10 s) with `[Category("Slow")]`.
   `run-editmode-tests.sh` runs the FAST suite by default (excluding Slow) and
   the whole suite with `SUITE=all`. Do not edit any `run-*.sh` while it is
   running. Keep the `PASS n FAIL n SKIP n` line; make it also say which
   suite ran.
4. Docs: CLAUDE.md "Commands" -- fast suite for every change; `SUITE=all`
   before any merge to `main` and on every terrain-generator change. Do NOT
   edit tools/overnight/PREAMBLE.md or SUPERVISOR.md (the supervisor will).

Acceptance (all in HANDOFF):
- Before/after wall time of the fast suite and of `SUITE=all`, from the
  scripts' own output (one run each is enough; this is not a frame-time claim).
- `SUITE=all` runs the same 630 test cases (same full names; compare the XML
  before/after, opt-in reports counted as skipped) and is 0 FAIL.
- The fast suite is 0 FAIL, and lists how many cases it left out.
Commit.
