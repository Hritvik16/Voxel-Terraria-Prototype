# 190a-matvariant-default-off

Model: opus

Approval: owner, 2026-11-12, Q-V answer (a): make "materials by the rules only, no random variant spray" (`-matvariant 0`, task 188a, commits `1031594` / `642d485`) the DEFAULT now. Same shape as task 187a (stamps-fit default ON) and 186 (`063fe27`, material rules default ON). Read TASKS.md's task 188a paragraph and 188a's HANDOFF in git (`642d485`).
Runs before 191 on purpose: 191's "everything ON vs OFF" preview should show the new default.

## Do
- Flip the default so the variant spray is OFF in play, rigs and capture runs; keep `-matvariant 1` as the explicit way back to the old look. Find every place the default lives
  (`TerrainSpec.matVariantOff`, `TerrainGenChunkSource`, `FarField`'s far-band spec, scene-builder SerializeField overrides: known landmine, a changed default can silently do nothing; print the effective value in a launch log line).
- `MatVariantTests`: re-pin to the new default; the old byte-for-byte pin stays, now on the explicit `-matvariant 1` arm. Never loosen an identity / FNV / geometry assertion; a test that checks the old spray pins `-matvariant 1`. TREES must stay unchanged (188a: tree density patches keep the noise VARIANT bit) -- keep that test green.
- If 189 (`-landsig`) or 190 (`-towns`) landed, their tests must still pass with the new default.
- Fast suite while iterating, `SUITE=all` once at the end (generator change). TASKS.md note; no timing arms.
- Prove: a launch log line shows matvariant OFF by default; default vs `-matvariant 1` shots at 3 of 188a's poses (`Logs/overnight/188a_shots.sh`: desert flat, savanna flat, one forest river bank), midday, sun behind camera. Look at each; captions say what to notice.

## End
Done. HANDOFF with pictures. STOP_FOR_OWNER only if flipping the default breaks a frozen contract or a gate.
