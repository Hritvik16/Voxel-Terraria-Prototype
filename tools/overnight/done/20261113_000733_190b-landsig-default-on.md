# 190b-landsig-default-on

Model: opus

Approval: owner, 2026-11-12, Q-W answer (b): make the landform signatures (`-landsig 1`, task 189, commits `d515aa0` / `36d2c10`) the DEFAULT now, dunes as they are (no taller-dune follow-up). Same shape as 187a (stamps-fit default ON) and 190a (matvariant default OFF). Read TASKS.md's task 189 paragraph and 189's HANDOFF in git (`36d2c10`).
Runs before 191 on purpose: 191's "everything ON vs OFF" preview should show the new default.

## Do
- Flip the default so landform signatures are ON in play, rigs and capture runs; keep `-landsig 0` as the explicit way back. Find every place the default lives
  (`TerrainSpec`, `WorldMap.EnsureLandSigPlane` callers -- the plane is built only when ON, so it is now built by default --, every MapView: near chunks, FarQuery points, shells, shell fill, FarField; scene-builder SerializeField overrides: known landmine, a changed default can silently do nothing; print the effective value in a launch log line).
- Report what building the plane by default costs: map build time and memory of the plane (3 bytes per map cell), from logs the test run already writes. No timing arms.
- Tests: `LandSigTests` keep both arms; tests that pin today's world (height FNV etc.) pin `-landsig 0` explicitly where they check the old path, and gain the ON pin where they check the default. Never loosen a geometry / identity / no-needle assertion. If 190 (`-towns`) / 190a landed, their tests must pass with the new default (towns keep off canyons' slots and benches the same way they keep off features).
- Fast suite while iterating, `SUITE=all` once at the end (generator change). TASKS.md note.
- Prove: a launch log line shows landsig ON by default; default vs `-landsig 0` at 189's canyonlands and range_200up poses (`Logs/overnight/189_shots.sh`; skip the broken `dune_field_low` pose), midday, sun behind camera. Look at each; captions say what to notice.

## End
Done. HANDOFF with pictures. STOP_FOR_OWNER only if flipping the default breaks a frozen contract or a gate.
