# 81-engine-13a-edit-op-log: DONE. Far edits are stored as D.4 ops and applied on generation (0.1 m chunks and shells); SetVoxel, D.1 and §4.4 unchanged
## What changed (commit `e6798cd`, branch engine-phase)
- `Memory/IEditOps.cs` (row 8): `uint Record(EditOp op)`, a NEW API beside the frozen `IEditService`. `Gameplay/EditOps.cs` implements it.
  It appends the op to the log, then applies it at once to the RESIDENT chunks it reaches, one chunk at a time, via
  `EditService.WriteVoxelDeferred` + `NotifyEditedRegion` (exactly what SetSphere/SetBox do). Chunks that are not resident get the op on generation.
- `Streaming/EditOpLog.cs` (row 9, D.4 `ops.log` in the delta folder): header `{uint seed, ushort formatVersion=1, uint opCount}`, then ops
  `{uint seq, byte kind, int3 centreVoxel, ushort size, byte material}` (20 B each), then `{uint sideCount}` + side entries
  `{int3 chunk, uint seqAtSave, uint deltaCrc}` (20 B each), then a CRC32 trailer (WorldMeta.Crc32). Written `.tmp` + rename via `DeltaCodec.WriteAtomic`.
  Kinds: 1 Sphere (SphereCovers rule), 2 Box (|d| <= size); Air = carve. **3 = RESERVED for D.3 prefabs: no D.3 `.vx` format exists**, so
  Append refuses it and a log containing it is rejected. seq is 1-based and contiguous. Size cap 4096 voxels.
  A rejected file (CRC, seed, version, length, seq, kind) is moved to `ops.log.rejected` and the log starts empty.
  Mutators are main-thread only (asserted). Workers read `Snapshot`, an immutable object that is replaced after each change (no locks).
- `Streaming/EditOpApply.cs`: `ApplyToChunk` does ChunkStore.SetVoxel's steps on storage the caller owns (a wholly covered brick becomes
  uniform). `ApplyToShell` sets a coarse voxel iff the op covers its row-13 sample. A top voxel whose sample the op misses keeps its surface colour.
- `StreamManager` (row 10): the worker builds generation, then ops 0..S, then D.1, then ops S..N.
  S comes from the side table: no entry -> 0; deltaCrc mismatch -> N ("all ops, then D.1").
  `SaveDelta`'s baseline = generation + every op. The side entry is written BEFORE the delta (`NoteDeltaAboutToBeWritten`).
  With zero ops nothing is written, so rigs and old saves never get an `ops.log`.
  If an op is recorded while a chunk is in flight, the chunk gets it on arrival (`CatchUpOps`, through ChunkStore.SetVoxel + mirror dirty).
- `GenerateShellChunk(shell, coord, out, EditOpSnapshot)` is an overload, and `ShellStreamer.Ops` passes the log in. Phase4Bootstrapper sets
  `Shells.Ops` and deletes `ops.log` together with the deltas (-cleardeltas / scene flag). `-farop` demo: `Playground.FarOpDemo.cs` + `RecordFarOp`.
## Evidence
- `EditOpTests` 10/10 (Logs/overnight/81-filter.log):
  - codec: round trip, deterministic bytes, `.tmp` renamed; corrupt trailer, corrupt body, truncated file and wrong seed are all rejected and moved aside.
  - the reserved kind and calls from a worker thread are refused; the S rules hold.
  - tg2 near vs far: **byte-identical** in 2 chunks (53,218 and 11,250 voxels written), including an op across the y seam.
  - end to end through StreamManager: a far op streamed in == the same op made near.
  - precedence: a hand edit made after op 1 survives the reload; op 2 (after the save) lands on top.
  - CRC-mismatch fallback: the old delta's 2 bricks win over ops 2-3, everything else = ops; the save reopens from disk with the same voxels.
  - old save with no ops: no ops.log is written and the reload is identical.
  - shells 1-7 with ops: 0 wrong voxels, 0 stale brick flags. Shell 1 vs the edited 0.1 m world: 15,138 covered samples, 0 differ.
- **SUITE=all PASS 751 FAIL 0 SKIP 19** (Logs/overnight/81-all.out; covers the fast set).
- `run-phase7-rig.sh` (fresh build): **PASS 37 FAIL 0, 19/19 shots byte-identical** to task 80's (Phase7Cycle/2026-10-12_061424 vs _051216). No frame time measured (rule 10).
- `-farop` took 3 launches (Logs/overnight/81-farop*.out, Screenshots/81/farop_log.txt).
  - Launch 1: default 1 shell worker. The crater's chunk was still queued after 79 s, so the far band drew that spot (no crater).
  - Launch 2: `-shellworkers 3`. Shell 3 held the chunk, but a ridge blocked the view.
  - Launch 3: camera 200 m out and 300 m up (361 m away). Shells 2 and 3 hold the crater chunk; shell 2 is FULL elsewhere (77's Q1).
  - Looked at both shots: a bare grey-brown bowl ~100 px wide at the crosshair, with a black shadow on its west wall. The expected size of a
    40 m crater at 361 m is ~114 x 95 px. The tint shot puts it in shell 3 (orange).
## Not done / next (13b and later)
- **No redraw when an op lands (13b).** Shell chunks generated before the op keep their old voxels. The far band ignores ops. Hence the demo
  records its op before anything streams.
- Row 12's "shell bricks of a chunk with a D.1 delta or live edits = downsample of the resident chunk" is not built: shells see ops, not hand edits.
- The in-flight catch-up does not run the fluid wake scan (it is rare: an op recorded during the few ms a chunk is generating).
- `ops.log` is rewritten on every delta save once ops exist (small: 20 B per op and per edited chunk). Batch the writes per frame if it ever shows.
## For the doc
- New save file D.4 `ops.log`: 10 B header + 20 B per op + 4 + 20 B per saved chunk (S, deltaCrc) + CRC32. Load = ops <= S, D.1, ops > S.
  D.1 bytes are unchanged and no `ops.log` exists until the first far edit.
- Near vs far edit: **0 voxels differ** in 2 tg2 chunks (64k voxels written). Shells 1-7 with ops: **0 wrong** coarse voxels.
  Shell 1 vs the edited 0.1 m world: **0 of 15,138** covered samples differ.
- Tests: EditOpTests **10/10**; SUITE=all **751 / 0 / 19**; Phase 7 rig **37/0, 19/19 identical**.
- `Screenshots/81/farop_crater_sheet.jpg`: top: the whole frame, 361 m above and to the side of the 20 m crater (red box). Bottom: the box
  enlarged 2x. Inside the red ring is the crater: a bare-ground bowl with a black shadow on its left wall, drawn by shell 3 (0.8 m voxels).
- `Screenshots/81/farop_crater_tint.png`: the same frame tinted by shell. The crater sits in orange (shell 3); yellow is shell 2 and blue is
  tier 0. The far band (grey) shows no crater; that is 13b's job.
