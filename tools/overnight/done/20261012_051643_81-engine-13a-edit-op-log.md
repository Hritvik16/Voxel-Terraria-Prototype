# 81-engine-13a-edit-op-log

Approval: `ENGINE_PHASE_PLAN.md` (owner, Q36, 2026-10-09) step 13 part a (E5); Amendment 8.14 adopted 2026-10-11
(owner "Approve all (a)", incl. Q3 (a): op log and D.1 by order of time). Queued by the supervisor 2026-10-12 after
task 80 (11b). Branch `engine-phase`. Step 12 and the shell default switch wait for the owner's Q44 (77's pool caps);
13a does not depend on it.

## Change: engine step 13a -- far edits stored as operations and applied on generation
Read first: `AMENDMENT_8_14_LOD_SHELLS.md` contract rows 7-12 (verbatim below is the core; the file wins).
- Row 8: `IEditService.SetVoxel` is FROZEN and still refused outside loaded chunks. A far edit uses a NEW op API
  (e.g. `IEditOps.Record(op)`), main thread only.
- Row 9: new save file D.4 `ops.log`: header `{uint seed, ushort formatVersion, uint opCount}`, ops `{uint seq, byte kind,
  int3 centreVoxel, ushort size, byte material}` (sphere carve/fill, box; D.3 prefab placement only if D.3 exists --
  otherwise reserve the kind value and say so), CRC32 trailer, `.tmp` + atomic rename exactly as D.1. Ops are applied in
  `seq` order inside generation, at each shell's own voxel size. The log does not depend on the preset.
- Row 10: D.1 stays byte for byte. Precedence (Q3 (a)): ops with seq <= S, then D.1, then ops with seq > S; S = op count
  when the chunk's delta was written, kept in a side table inside `ops.log` (`{int3 chunk, uint seqAtSave, uint deltaCrc}`),
  never in D.1. `deltaCrc` mismatch (crash between the two writes) -> "all ops, then D.1". Old saves (no `ops.log`) load
  unchanged.
- Row 11: §4.4 state table unchanged. Row 7: `IWorldQuery.GetVoxel` unchanged.
- Apply ops in BOTH `GenerateChunk`'s output (0.1 m, after generation, before D.1 per the precedence) and
  `GenerateShellChunk` (at shell k's voxel size). Workers apply ops from an immutable snapshot taken on the main thread
  (single-writer rule; no locks in the streaming path). Integer voxel coordinates, bitwise chunk math.
- A near edit through the new API on a RESIDENT chunk: record the op and apply it to the resident chunk the same way
  the existing edit path does (so near and far give the same voxels). Do not change what `SetVoxel` does.
- 13b (redraw of the shells / far band at once when an op lands) is NOT part of this task.

## Acceptance (plan's E5 tests, part a)
- EditMode: `ops.log` round-trip (write/read, CRC, `.tmp` + rename, corrupt trailer rejected); apply-on-generate equals
  the same edit made near on a resident chunk (byte-identical chunk); precedence S cases (op before / after a D.1 save,
  CRC mismatch fallback); an old save without `ops.log` loads byte-identical; shell chunk with an op matches the coarse
  sample of the edited 0.1 m world (same rule as `ShellChunkTests`).
- Fast EditMode green; `SUITE=all` once at the end (generation path touched; PREAMBLE rule 3).
- `run-phase7-rig.sh` result unchanged (no ops recorded in rigs). No frame-time measurement.
- Image: ONE far-crater shot if a capture mode can record an op cheaply (e.g. a `-farop` flag that carves a 20 m sphere
  ~300 m out before the shot, shown with `-shells 1`); otherwise say what 13b needs to show it.
- HANDOFF `## For the doc`: test counts, file format summary, the image (or why none).
