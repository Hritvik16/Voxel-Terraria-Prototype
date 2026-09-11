# The frozen-fluid artifact: cause, cap ladder, and the options

*Opened 2026-09-11. Companion to `PHASE_6_QA_PASS.md` §7 (the late-game
siege), which is where the artifact was first seen and where its cause was
inferred but not demonstrated.*

**Status of this document: MEASUREMENT AND OPTIONS. It recommends; it does not
decide.** Nothing in the shipped configuration is changed by it —
`FluidTileMap`'s cap stays at 512 and `MAX_ACTIVE_FLUID` stays at 500,000
until a human says otherwise.

---

## 0. The question

The siege ended with blocks of fluid suspended in mid-air that never settled.
The write-up attributed this to the 512-tile pool cap. That attribution was
**inferred** — from a counter (294,588 exhaustions) and some arithmetic about
how many tiles a ±260-voxel pour spans — and inference is not evidence. Three
things needed establishing:

1. Is it actually the tile cap, or the slot cap, or something else?
2. Does raising the cap fix it, and what does raising it cost?
3. If no affordable cap fixes it, what is the honest alternative?

---

## 1. The two caps are reached by opposite shapes

This is the fact the whole diagnosis turns on, and it is why "the pool was
full" and "we ran out of fluid" are different failures with different fixes.

A tile is 32³ = 32,768 cells and costs **one pool slot however little fluid it
holds**. `MAX_ACTIVE_FLUID` is 500,000 live voxels; 512 tiles could hold
16.7 **million**. So:

| shape | what runs out first | what the other cap is doing |
|---|---|---|
| dense, compact (a deep pool) | **slots** | tiles nearly empty, pool barely touched |
| thin, wide (a spread pour) | **tiles** | slots barely touched |

The siege's pour spreads ±260 voxels. That is the thin-and-wide shape, so the
tile cap is the one it should press against — but "should" is a prediction,
and §2 is the test.

---

## 2. The repro (`FluidTileCapRig`, `./run-fluid-tilecap.sh`)

Deliberately built to press one cap and not the other:

- **A sparse lattice** — one 4³ pocket (64 voxels) per tile, on a 32-voxel
  pitch, across ±384 voxels. Tile demand ≈ 880; slot demand ≈ 40,000, which is
  **8% of `MAX_ACTIVE_FLUID`**.
- **One marker cube** — 12³ of water, in mid-air, 34 voxels of cleared air
  beneath it, placed *after* the lattice has taken the pool. It sits at the
  **far (+x, +z) corner** deliberately: `FluidTileResidency.Refresh` scans
  chunks from lo to hi, so the last-scanned tiles are the ones that lose the
  race for a full pool. That makes the outcome deterministic rather than a
  coin-flip on iteration order.
- Then **nothing else is placed** and the sim runs quiet for 300 frames.

The cube is the observable: pool exhausted → its tiles are refused and its
original box still holds ~100% of its voxels; pool available → it falls and
the box empties.

The cap is read from the command line, so **every cap value runs the same
binary against the same scenario** — no IL2CPP rebuild sits between two
numbers that are supposed to differ only by a pool size.

---

## 3. Peak tile DEMAND — the number that makes "raise the cap" answerable

`Refresh` already knew this and threw it away. Demand is counted
**post-radius-gate** as `TilesAlreadyResident + TilesAcquired +
TilesRefusedPoolFull` — the tiles that passed all three of Refresh's gates and
genuinely wanted a slot. (`Stats.TilesWithFluid` is counted *before* the radius
test and overcounts by whatever sits in the chunk AABB but outside the sphere,
so it is not the right number.)

Both `FluidTileCapRig` and `LateGameSiegeRig` now report it. Without it,
"raise the cap" has no target to raise it **to**.

A prior figure already existed and agrees: `FLUID_SCALE_ARCHITECTURE_RESULTS.md`
§4's explosion-scatter scenario recorded **733 tiles flagged** against the
512 cap with 2,280 refusals, for 220 pockets over ±90 m. Two unrelated wide
scenarios, both landing in the 700–900 range, both over 512.

---

## 4. What a bigger pool costs

**Memory is linear and exactly predictable.** Per-cell buffers are
`_claim`, `_slotAt`, `_reacted`, `_wakeMark` — 4 B each, 16 B/cell — and
`_cellCount = TileCapacity × 32,768`. So **0.5 MiB per tile**, allocated at the
cap whether or not any fluid exists:

| cap | predicted | measured active set | measured total (sim) |
|---|---|---|---|
| 512 | 256 MiB | 264.0 MB | 286.1 MB |
| 1024 | 512 MiB | 520.0 MB | 542.1 MB |
| 2048 | 1024 MiB | **1032.0 MB** | 1054.1 MB |

Linear, confirmed rather than assumed — the constant +8 MB is the tile
directory, +22 MB the slots and op ring. **This machine has 8 GB of unified
memory**, so cap 2048 is an eighth of the whole machine for fluid alone.

**There is a second cost, and it is not memory.** `DispatchCells` is
`_activeTileCount × TileCells` — per-tick GPU work tracks *resident* tiles, not
capacity. A bigger pool therefore costs dispatch **exactly in the scenarios
where the cap was binding**, because that is when the extra tiles actually
become resident (512 → 885 resident in this repro, 1.73× the dispatch). "Free
headroom" is the wrong model: the headroom is free until it is used, and it is
used precisely when it was needed.

---

## 5. The tile EDGE is the other lever, and it is a redesign, not a knob

For the thin-and-wide shape that actually fails, total memory for a sheet of
footprint W × W and tile edge T is `(W/T)² × T³ × 16 B = W² × T × 16 B` —
**linear in T**. Halving the tile edge halves the memory for exactly the case
that breaks:

| tile edge | tiles for a 768² sheet | memory |
|---|---|---|
| 32 (shipped) | 576 | 302 MB |
| 16 | 2,304 | 151 MB |
| 8 | 9,216 | 75 MB |

**This is not a tuning value.** `ChunkFluidMask` packs a chunk's sub-tile
occupancy into **one ulong** — 4³ = 64 sub-tiles at T=32, which is the entire
reason the residency scan is "read one ulong per chunk". T=16 gives 8³ = 512
sub-tiles and does not fit; the mask would become eight ulongs and the scan,
the tile-bit math and their tests all change. `FluidTileMap` also documents
`T | 128` as a **correctness** property, not a convenience — it is what keeps a
tile inside one chunk so residency is decidable per tile.

Recorded here because it is the only option that improves the failing case
without paying for it somewhere else. It is a redesign and needs a deliberate
decision.

---
