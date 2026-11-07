# 186-matrules-default-on

Model: opus

Approval: owner, 2026-11-07, Q-M answer (a): make the simple material rules (`-matrules 1`) the DEFAULT now (tasks 180b + 182 fixed the defects). Read `tools/overnight/done/*182*` HANDOFF notes in git (`4867115`) and TASKS.md 180b/182 paragraphs.

## Do
- Flip the default so the rules are ON in play, rigs and capture runs; keep `-matrules 0` as the explicit way back to the layered rock. Find every place the default lives
  (TerrainSpec default, scene builder SerializeField overrides -- known landmine: a changed default can silently do nothing; verify the effective value is printed in a log line).
- Tests that assume the old paint: update them ONLY where they encode old paint, never loosen a geometry / identity assertion. Run fast + whole suite (generator change; if task 184b has already landed, use its budgets).
- Prove: a launch log shows matrules on by default; ON-by-default vs `-matrules 0` shots at 3 poses (desert river bank, snow cliff, beach). Captions. Feature note in TASKS.md; no timing arms.

## End
Done. HANDOFF with pictures. STOP_FOR_OWNER only if flipping the default breaks a frozen contract or a gate.
