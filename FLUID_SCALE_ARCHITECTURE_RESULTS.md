# §7.2 sparse tiled active set — results

**What this is:** substrate work on the fluid simulation's memory model, done
2026-09-05/06 against `DESIGN_NOTE_7_2_TILED_ACTIVE_SET.md`. It is **not** a
phase deliverable and deliberately does not touch `PHASE_6_COMPLETION.md`'s
closure status — whether and how this relates to a phase boundary is the
project owner's call.

**What it solves:** one specific blocker — the fluid active set's memory scaled
as O(radius³), which made §7.4's shipped radius unreachable. **It is not
"Noita parity"**; see §7 for what is still missing.

---

## 1. The headline numbers

| | Before (dense region) | After (sparse tiles) |
|---|---|---|
| Active-set memory at the **shipped** radius (1280 voxels / 128 m) | **128 GB** (a 2048³ region) — unreachable | **264 MB**, measured from actual GPU allocations |
| Does the footprint change with the radius? | it *is* the radius, cubed | **no** — identical at r=128, 640, 1280 |
| Does §7.4's radius gate anything? | **no** — inert by construction at every affordable size | **yes** — measured releasing and re-waking |
| Concurrent disconnected fluid pockets | one region's extent | **512 tiles** over ±90 m, cap reached and handled |

`run-fluid-tiled.sh`: **16 PASS / 0 FAIL.**

---

## 2. Acceptance scenario A — the shipped radius, measured

Numbers are summed from the `GraphicsBuffer` objects themselves (`count × stride`),
not recomputed from the configuration, because a formula can be right about a
design and wrong about the code.

```
  radius   active set   total GPU   cells        fits ring
     128     264.0 MB     274.5 MB   16,777,216   yes
     640     264.0 MB     274.5 MB   16,777,216   yes
    1280     264.0 MB     274.5 MB   16,777,216   yes
```

The footprint is identical at all three because it is a function of the tile
pool (512 tiles × 32³ cells × 16 B) plus a fixed 128³ directory — **the radius
does not appear in it at all.** That is the whole claim of the design.

**Configuration:** tile edge 32 voxels, 128³ toroidal tile directory (8.4 MB),
tile pool capped at 512.

---

## 3. Acceptance scenario B — the radius actually gates

| step | result |
|---|---|
| standing on a pool | 2 tiles resident, **795 slots allocated, 2,706 voxel writes** |
| walked ~85 m away | the tile left behind is **released** |
| returned | the tile is **re-acquired** and simulating again |

The wake half is what the sparse design nearly lost: with no dense cell sweep,
nothing knows where dormant fluid is. It is answered by `ChunkFluidMask` — one
`ulong` per chunk — not by scanning.

---

## 4. Acceptance scenario C — explosion scatter

220 pockets of water / sand / lava scattered over ±90 m, deterministic seed.

```
  residency scan   441 chunks read (one ulong each), 733 tiles flagged
  concurrent       512 tiles (cap 512) -- the cap WAS reached
  motion           51,261 ops emitted, 77,694 voxel writes applied
  slots            highWater 12,024 / 65,536
  pool refusals    2,424, every one a §7.7 guarded no-op, cap never exceeded
  wake audit       0 of 220 pockets silently failed to wake
```

The wake audit is the one that matters: every pocket still holding mobile
material was verified to be inside a resident tile. That is the defect class
`DESIGN_NOTE_7_2` §9 exists to prevent, audited rather than asserted.

The cap being reached is a **feature of the run, not a failure** — it exercises
the same refuse-cleanly discipline §3.6's LRU valve already proved for terrain.

---

## 5. Three defects found, each by measurement

**1. Silent dispatch truncation — the significant one.** Compute thread groups
are capped at **65535 per dimension**. A dense 64³ region is 4,096 groups and
never came close; 488 tiles × 32,768 cells is **250,000**. Exceeding the cap
does not fail — the dispatch is silently truncated. It presented as `CSIntent`
placing 3,253 claims that `CSCommit` never saw: promote and intent counters
healthy, op-list empty, zero motion. Located with the shader's own §10.4 stage
counters, which is exactly what they exist for. Fixed with a 2-D dispatch grid;
dispatches that still fit stay 1-D, so the dense path is bit-identical.

**2. CPU/GPU addressing disagreement.** `InRegion`/`RegionIndex`/`RegionVoxel`
on the CPU still used the dense region box, so every wake request at world
coordinates was rejected out-of-region and nothing promoted.

**3. Buffer initialisation sized by the region, not the allocation.** Under
tiling the per-cell buffers are `TileCapacity × TileCells`; initialising only
the first `_regionCellCount` entries would leave `SlotAt` as garbage, which by
its own comment makes `CSPromote` believe every cell already owns a slot.

**Plus one design correction:** the wake queue is now keyed on the **world
voxel**, not a cell index. A tile's slot is not stable across the deferral
window (up to 120 ticks), so a stored cell index could address a different
tile's cells by the time it was released — §6.2's aliasing failure in a third
buffer.

---

## 6. Compromises and limitations accepted

- **No GPU timing, anywhere.** §2.2 budgets the fluid CA on the GPU lane and
  this workflow cannot attribute GPU stages (AMENDMENT_8_9 §0 Rules 1–2;
  AMENDMENT_8_10's 2.6–2.7× inflation). **Whether the tiled CA meets §2.2's
  ≤3.5 ms is unknown and is not claimed.** The design makes cost proportional
  to fluid present rather than to radius volume; whether the constant is
  affordable is a separate, currently unanswerable question.
- **The 512-tile cap is an assumption**, sized against §2.5's target with
  headroom. Scenario C reached it, so it is now at least *exercised* — but
  whether 512 is the right number for real play is untested, exactly as
  `BRICK_POOL_HIGH_WATER_FRACTION` was before a rig drove it to its limit.
- **Tile edge 32 is load-bearing in two places**: it must divide 128 (so a tile
  can never straddle a chunk boundary — the Phase 5d bug class), and 4³ = 64
  tiles per chunk is what makes the wake mask exactly one `ulong`.
- **The dense path is retained**, not deleted. It is byte-identical behind
  `_Tiled == 0` and every pre-existing rig still exercises it.
- **`ChunkFluidMask` clearing is deferred**, off the write path, under a budget.
  Over-reporting costs one acquire-then-free; under-reporting is fluid that
  never wakes, so the error direction is chosen deliberately.
- **§2.5's ~500,000 active-fluid target remains untested** and is not claimed.

---

## 7. NOT ADDRESSED — flagged for a future design conversation

**This does not deliver "full Noita parity."** Two mechanics that Noita has and
this architecture has **no specification for** were deliberately not invented:

- **Gas / smoke diffusion.** Nothing in the architecture document specifies gas
  behaviour, buoyant transport, or dissipation.
- **Density-layered fluid stacking** (oil floating on water, and the general
  case of two fluids resolving by density rather than one displacing the other).
  §7.4's intent hierarchy moves a single mobile voxel into Air; it has no notion
  of two fluids exchanging places by density.

Both would need a design conversation and a spec before any implementation.
Neither is blocked by the substrate work here — if anything the tiled active set
makes them cheaper to reach — but they are genuinely absent, and this document
should not be read as implying otherwise.

Also still open, from `DESIGN_NOTE_7_4` §9: the intermittent single-voxel
settling stall (1–2 voxels per ~2,900, ~⅓ of runs, unchanged by this work and
pre-existing).
