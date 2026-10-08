# 26c-palette-brick-design

Approval: owner, 2026-10-04 ~13:50, in chat with the supervisor: "lets try your way" --
reduce terrain memory with per-brick material palettes (option "palette" below), NOT
surface-only material tickets. This task is DESIGN + one measurement only. No engine code.
Queued by the supervisor; runs before B3 because the owner wants terrain memory down soon.

## Why
Terrain bricks are 8x8x8 with a 512-byte body (1 material byte per voxel) whenever a brick is
not uniform (ARCHITECTURE_v8.6 at-a-glance line 15, section 3, Appendix A.4). The CPU pool
is 750k slots (~384 MB) and the GPU mirror holds the same bodies again (mirror 635 MB at the
2026-10-01 ledger); the cascade tier pools are also 512 B bodies, CPU + GPU (spec table
~line 856). KNOWN_ISSUES "THE TG2 WORLD IS OVER THE 3 GB CAP" has the ledger.
A mixed brick usually holds air plus 1-3 materials. A per-brick palette (e.g. up to 4
entries, 2 bits per voxel = 128 B + palette) keeps EVERY voxel's material exact -- no
dependence on the generator for buried voxels, editing stays a bit write -- at ~1/4 the body.
Rejected for now (owner): surface-only "tickets" (the compare project's Brickmap), because
buried materials would be re-derived from a generator that is still changing every B step.

## Do
1. **Measure (report-only, no engine change):** a histogram of DISTINCT materials per dense
   brick (count air as a material) on map v4, seed 42, over the chunks a walk and the v4 poses
   touch (or a fixed set of ~2,000 chunks around the spawn plus the mountain poses -- say which),
   generated through the real `TerrainGenChunkSource`. Report-only EditMode test
   (`ReportOnly.Require()`, `[Category("Slow")]` if > 10 s) or an `-executeMethod`; also the
   same histogram for cascade tier 1 and 2 bodies if they are cheap to get. Print: fraction of
   dense bricks with 1 / 2 / 3-4 / 5-16 / 17+ distinct values.
2. **Write `AMENDMENT_8_13_PALETTE_BRICKS.md`, marked DRAFT, NOT ADOPTED** (adoption is the
   owner's, after reading it). Cite spec sections for every change. It must cover:
   - Body formats (e.g. 1-bit / 2-bit / 4-bit palette + full 512 B fallback) chosen FROM the
     histogram, and the expected MB saved for the CPU pool, the GPU mirror and the cascade
     pools at today's caps and at the measured need. Say which numbers are measured vs derived.
   - Where the format lives: clipmap entry / A.x struct bit layout (power-of-two sizes; bitwise
     index math only, CLAUDE.md hard invariant), GPU mirror layout, the raymarch micro-step fetch
     (cost per hit: a shift + mask + palette read) and the LOD downsampler.
   - Allocation: size classes vs the pool's contiguous-run allocator (`BrickDataPool.cs` --
     runs exist to keep uploads at one call per chunk; the design must not undo that).
   - State transitions: SetVoxel promotion when a brick gains a material beyond its palette;
     coalescing (spec ~line 539) and demotion; the Volatile fluid rule (8.2) and fluid churn.
   - Frozen contracts: `IWorldQuery.GetVoxel`, `IEditService.SetVoxel`, the D.1 delta format
     and the 4.4 transition table stay UNCHANGED (e.g. D.1 keeps 512 B records by expanding at
     the codec boundary). If any of them cannot stay unchanged, say so explicitly -- that is a
     redesign and the owner must see it.
   - Persistence and determinism: saves must not depend on the generator version.
   - What breaks: oracle tests and construction sites that assume 512 B bodies (grep), the
     `WINDOW_CHUNKS_Y` oracle windows (CLAUDE.md landmine), shader compile checks.
   - An implementation plan as ONE change per task, each with its acceptance check (pixel-
     identical poses, EditMode, memory ledger `-memledger` before/after, warm stream probe so
     generation/admission are not slowed) and a rollback point.
3. Fast EditMode suite before committing (`./run-editmode-tests.sh`, through the unity lock).

## Done when
HANDOFF.md: the histogram (with the command/log path), the expected savings table, the
amendment's file name, the open choices the owner must make (start with the recommended one),
and the first implementation task as a ready-to-queue spec. No STOP_FOR_OWNER needed: the
supervisor turns the amendment into an owner question.

## Do not
Change engine code, the generator, pool caps, or any frozen API. No player builds needed
unless the measurement requires one (say why). No performance claims.
