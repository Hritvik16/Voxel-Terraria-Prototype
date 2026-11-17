# 187a-stampfit-default-on

Model: opus

Approval: owner, 2026-11-12, Q-F answer (a): make "stamps sit in the land" (`-stampfit 1`, task 185, commit `c7ab7e4`) the DEFAULT now. Same shape as task 186 (`063fe27`, material rules default ON). Read TASKS.md task 185 paragraph and 185's HANDOFF in git (`65ebcb0`).

## Do
- Flip the default so stamp fitting is ON in play, rigs and capture runs; keep `-stampfit 0` as the explicit way back. Find every place the default lives
  (TerrainSpec default -- follow 186's `matRulesOff` pattern -- the chunk source, FarField, scene-builder SerializeField overrides: known landmine, a changed default can silently do nothing; print the effective value in a log line).
- Tests that encode the old stamp shape: update them ONLY there, pinning `-stampfit 0` where a test checks the old path; never loosen a geometry / identity / no-needle assertion. Fast suite while iterating, `SUITE=all` once at the end (generator change).
- If task 187 (site planner) has landed, check its site tests still pass with stamps fitted (sites keep out of features/stamps).
- Prove: a launch log line shows stampfit ON by default; default vs `-stampfit 0` shots at the 3 poses task 185 used (cave mouth, ledge, geyser basin). Look at each, captions. TASKS.md note; no timing arms.

## End
Done. HANDOFF with pictures. STOP_FOR_OWNER only if flipping the default breaks a frozen contract or a gate.
