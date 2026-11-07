# 184c-test-suite-last-5-minutes

Model: opus

Approval: supervisor, 2026-11-07, "Going ahead with (a)+(c) unless the owner objects" -- recommended option of task 184b's STOP_FOR_OWNER. The owner's standing rule (2026-11-07): `SUITE=all` must be <= 15 min, "70 mins of waiting is insane".
Read `tools/overnight/blocked/184b-test-suite-trim.HANDOFF.md` (commit 9520e4e; all = 20m46s, fast = 2m34s). Test code + runner only; no product code.

## Do
1. (a) Per-seed generator tests (FluidAtRest, LakeShore, LakeLevel, OreRule): in `all` keep the ADVERSARIAL seed 31337 only (the seed that exposes the most), the other seeds (42, 7, 2026) go to Nightly. Pick the kept seed from the data (which seed made the most mutation / failure catches in 184b's mutant runs), not by habit. Nightly keeps all 4. This is an owner-visible tradeoff: state it in HANDOFF.
2. (c) Run the managed column-sampling / chunk loops of MaterialRule.FourSeeds, Cover.FourSeeds, OreRule deposit table, FluidPocket.Sites, MaterialRuleChunk.Shells on worker threads (Parallel.For / jobs over disjoint scratch, single-writer rules respected: workers write only their own scratch; the main thread merges). Results must be BYTE-IDENTICAL to the serial version: assert that in each trimmed test's own check (compare a hash against a serial run once, in a Nightly test).
3. Re-run the mutation proofs M1-M5b of 184b on the changed tests that matter (OreRule, MaterialRule) and show they still fail.
4. Measure: `SUITE=all` twice and fast twice at the end; report wall. Target `all` <= 15 min. If still > 15 min after both, list the next 5 slowest tests with the reason and STOP_FOR_OWNER with options (do not weaken a test).
5. Also commit the proposed CLAUDE.md budget line to HANDOFF only (owner edits CLAUDE.md).

## End
Done when SUITE=all <= 15 min, fast <= 3 min, same pass counts + the new Nightly-only cases accounted for.
