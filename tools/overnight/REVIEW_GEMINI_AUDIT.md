# Side session, part 2: check Gemini's test-memory audit + judge Gemini (owner, 2026-10-09)

Same rules as REVIEW_GEMINI.md: no Unity at all (task 37e is running), no edits in the main
repo, cheap reads. You talk to the owner directly.

## A. Check the audit
`../VT_gemini2/GEMINI_TEST_AUDIT.md` (branch `gemini/test-memory-audit`, 457 lines). Gemini
(file tools only, no shell) audited the EditMode tests for memory risk after task 37e's test
grew Unity to 22.5 GB (that test: `tools/overnight/blocked/37e-LakeRestTests.LEAKED.cs.txt`).
- Did it find the real mechanism in the leaked test? Say what it is, with file:line, if you can
  tell from the code (not proven -- nothing was run).
- Check its top findings (at least the top 5) against the actual code: TRUE / FALSE / OVERSTATED,
  one line each, with your own file:line. Check its peak-memory arithmetic on two of them.
- Note anything important it missed (spot-check: SharedMaps, the TerrainGen tests, ChunkStore
  windows built 16 chunks tall in the Raymarch* oracle tests).
- Commit the audit on its branch by path (exclude `.gemini/`) with a "Review" section at the top
  holding your verdicts. Do not change test code; fixes become queue tasks only if the owner says so.

## B. Judge Gemini as an engineer
From both pieces of work -- `73a625b` (lava look; your own review is at the top of
`../VT_gemini/GEMINI_HANDOFF.md`) and this audit -- give the owner a short, frank verdict:
- what it did well, what it got wrong (scope creep? wrong config object? false findings?),
  and whether the errors came from the brief's wording or from Gemini;
- what kind of task it is fit for now: code-only ports, analysis, tooling, engine code,
  terrain code; and with what review;
- whether it is ready for the next step the owner wants: running Unity builds/tests itself.
  Context for that: Gemini CLI 0.63.0 forces an approval prompt (even in YOLO) when a shell
  command reuses a flag, or a word containing / . or :, that appeared in any earlier shell
  output; plain words do not trigger it. The supervisor's plan is a few fixed wrapper commands
  on PATH with plain names and plain-word arguments (e.g. `vt-test fast`, `vt-shots lava`)
  plus an APFS-cloned Unity Library in its worktree, all under the shared Unity lock and the
  memory watchdog. Say whether its work so far justifies that trial.
Keep B under 25 lines. Then tell the owner this window can be closed.
