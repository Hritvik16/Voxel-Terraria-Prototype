# 84-coarse-column-fields  (PARKED: owner-gated; queue only when the owner says so)

Proposal from the owner's side session (gen-talk, 2026-10-12), passed on at the owner's request. Runs after 82b
(engine step 15, brick skip). NOT approved yet: needs the owner's go (Queue decisions doc, Q45).

## Why
The column pass is ~60% of a chunk's cost (KNOWN_ISSUES task 23). Per 0.1 m column we evaluate fields that change
only over kilometres:
- `DetailField.PrepareColumn`: ~15-20 snoise/Fbm calls (deep/cavern/under offsets, tunY0-3, underLo/Hi, pillars;
  wavelengths 1,800-6,000 m; `DetailField.cs:89-145`).
- Biome temp/moist change over 12-15 km (`ShapeField.cs:244`).
Minecraft evaluates expensive noise only at the corners of 4x8x4-block cells and interpolates; we do it per column.

## Step 0 (decides the rest; a good standalone first task)
Isolated EditMode timing split of the column pass: `ShapeField.Sample` vs `PrepareColumn`, as task 30 did it.
Decision rule (stated before measuring):
- PrepareColumn >= ~30% of the column pass -> do the coarse-grid change (below).
- Less -> target `ShapeField.Sample` instead: 4 columns at once with float4 noise (can stay byte-identical under
  Strict mode: each lane does the same IEEE ops).

## Change (if step 0 says so)
In `ColumnJob`, evaluate the km-scale fields on a coarse grid (one sample per 8x8 columns = per brick, or per 16x16)
and interpolate bilinearly. Fine fields stay exact per column: height S, MaxStep, stamps, rivers, lips.
NOT byte-identical by design (a terrain-generator change: `SUITE=all`; owner looks at poses).

## Acceptance
- Isolated timing check, ABBA (warm, PREAMBLE rule 9).
- Largest shift in cave / tunnel / strata boundaries, in voxels.
- Before/after pose shots (caves, strata faces, a cliff), looked at.

Context (side session): no shipped game generates 0.1 m voxels at runtime (Teardown's levels are handmade; runtime
generators use ~1 m blocks). ~30 ns/voxel is already fast; the waste is per-column work, not per-voxel code.
