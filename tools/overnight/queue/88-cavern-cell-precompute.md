# 88-cavern-cell-precompute

Approval: owner, 2026-10-15 in chat (supervisor's question "86 found a byte-identical speed-up ... queue it?" ->
"Queue it, small (Recommended)"). Follow-up named in task 86's HANDOFF ("Left / notes"). Queued by the supervisor
2026-10-15. Branch `engine-phase`. This touches `Assets/TerrainGen/`, so `SUITE=all` at the end (CLAUDE.md).

## Change (one change, output bytes must not move)
Task 86 left `DetailField.PrepareColumn` at ~309 ns per column, mostly the cavern cell loop: 4 cells x 9 candidates x
`Hash01` per column. Those inputs are constant over a chunk column (one 102.4 m cavern cell covers it), so compute the
cavern candidates ONCE per chunk column (or per job, whichever the code shape makes simple: e.g. a small struct beside
86's `NodeCache`, filled lazily when the cell changes) and let each column read them.
- Must stay a pure function of (x, z): chunks, shells (`ShellColumnJob`), shell fill sub-samples, FarField far edits and
  the dev map all keep agreeing. A column whose cavern cell differs from the cached one must still get the right answer
  (cache keyed by the cell, not assumed per chunk) -- shell chunks span many cavern cells at shells >= 4.
- `-coarsecols 0` and map v3 must stay byte-identical too.
- Keep 86's `PrepareColumnExact` (or an equivalent uncached reference) for the test.

## Acceptance
- Byte-identical test: chunk bytes (FNV) for a set of 0.1 m chunks AND shell chunks at shells 1, 3, 5, 7 across a cavern
  cell boundary, cached vs uncached; plus `PrepareColumn` vs the reference on 16k random columns. Reuse `BrickSkipTests` /
  `CoarseColumnTests` helpers where they fit.
- Timing (direction check, NOT a frame-time or streaming claim): `ColumnPassSplitTests` A/B in the Editor with Burst, one
  thread, ABBA, warm (PREAMBLE rule 9: one warm-up launch first, log path in HANDOFF). Report PrepareColumn ns/column and
  whole 7-layer chunk column ms, before -> after, plus shell chunks 1-7.
- Fast EditMode while iterating; `SUITE=all` once at the end. No screenshots needed (bytes do not change); say so.
- HANDOFF `## For the doc`: ns/column before -> after, chunk column ms before -> after, shells, suite line.
