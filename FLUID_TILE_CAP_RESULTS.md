# The frozen-fluid artifact: cause, cap ladder, and the options

*Opened 2026-09-11. Companion to `PHASE_6_QA_PASS.md` §7 (the late-game
siege), which is where the artifact was first seen and where its cause was
inferred but not demonstrated.*

**Status: SHIPPED as of 2026-09-11.** §§0–10 below are the investigation that
produced the recommendation; **§11 is the shipped state and the final
numbers.** The recommendation in §9 was accepted: the cap is now 1024 and the
orphaned-tile gap is fixed. `MAX_ACTIVE_FLUID` is unchanged at 500,000.

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

## 6. The cooled ladder (Step 2)

Six runs, 300s idle cooldown before each, `-holdseconds 60` (3,600 measured
frames per run), two passes so every cap carries a driftcheck twin.
**4 PASS / 0 FAIL in every run**, and the twins agree on every verdict.

| cap | peak demand | refusals | marker cube | GPU active set | GPU total (sim) |
|---|---|---|---|---|---|
| **512** | 844–849 | **97,400 / 97,529** | **FROZEN** 1,728 / 1,728 | 264 MB | 286 MB |
| **1024** | 877 / 877 | 0 | FELL 0 / 1,728 | 520 MB | 542 MB |
| **2048** | 876 / 877 | 0 | FELL 0 / 1,728 | 1032 MB | 1054 MB |

**A cap of 1024 fixes this scenario completely** — zero refusals, not merely
fewer. 2048 buys nothing beyond it, because demand is ~877 and a pool only has
to exceed demand.

### 6a. Demand is self-suppressing at a binding cap

Worth naming, because it is a trap for anyone sizing a pool from a saturated
run: cap 512 reports demand **844–849** while the non-binding caps report
**876–877**. Refused fluid cannot spread, so it never generates the demand it
would have generated. **Only a non-binding run yields an honest demand
figure**, and a saturated run's number is an underestimate by construction.

### 6b. Frame time: no cap-attributable cost is detectable

The first ladder ran the caps in the same order in both passes, which
confounds cap with position-in-pass. A third pass was run in **reverse order**
(2048 → 1024 → 512) to break that. All nine runs:

| pass | order | p50 by position |
|---|---|---|
| 1 | 512, 1024, 2048 | 10.49, 10.28, 10.29 |
| 2 | 512, 1024, 2048 | 10.32, 11.91, 12.10 |
| 3 | **2048, 1024, 512** | 10.86, 11.80, 11.76 |

Grouped two ways:

| grouping | means | spread of means |
|---|---|---|
| by **cap** (512 / 1024 / 2048) | 10.86, 11.33, 11.08 | 0.47 ms, **not monotonic** |
| by **position in pass** (1 / 2 / 3) | 10.56, 11.33, 11.38 | 0.83 ms, **monotonic** |

In the reversed pass the **largest** cap was the **fastest**, which is the
opposite of what a cap cost would produce. Within-cap range (1.44–1.81 ms)
exceeds the entire between-cap spread (0.47 ms).

**Conclusion: no frame-time cost attributable to the cap, bounded at roughly
the measurement noise (~4% between cap means, against 13–17% within-cap
scatter).** The 4× memory increase from 512 to 2048 did not show up in wall
clock at this demand level.

**A methodology finding that applies beyond this document: 300s cooldowns do
not fully absorb drift across a three-run sequence.** Position within a
measurement session is the largest single term in these numbers. Ladders
should be run counterbalanced, and a single-order ladder should not be trusted
to separate the variable under test from where the run sat in the session.

### 6c. What raising the cap actually costs

1. **Memory, always, whether used or not** — 0.5 MiB × cap, allocated at
   construction. 512 → 1024 is +256 MB on an 8 GB machine.
2. **Dispatch, but only when the cap was binding** — `DispatchCells` is
   `_activeTileCount × TileCells`, so per-tick work follows *resident* tiles.
   This repro went 512 → ~882 resident, 1.72× the dispatch, and still showed
   no measurable wall-clock difference.
3. **Nothing else.** No refusals, no correctness change, water conserved
   exactly in every run at every cap.

---

## 7. The real scenario: the siege at both caps

The lattice's demand is whatever the rig dials it to, so nothing can be
recommended from it alone. The late-game siege — heavy fluid plus all six
Phase 6 systems, 200s — was re-run cooled at both caps, with a **direct census
of the artifact** rather than a screenshot impression.

| | cap 512 | cap 1024 |
|---|---|---|
| peak tile demand | 678 | 682 / 692 |
| refusals | 283,784 | **0** |
| tiles resident | 512 / 512 | 795 / 787 of 1024 |
| **frozen-fluid census** | **90 of 2,677 (3.4%)** | **0 of 2,279 and 0 of 2,265 (0.0%)** |
| live slots peak | 321,695 / 500,000 | 321,158 / 321,200 |
| p50 / p99 | 8.59 / 47.82 ms | 8.60 / 50.97 and 8.51 / 51.89 ms |
| lava+obsidian | 12,395 → 12,395 (0.00%) | exact in both runs |
| gates | 8 PASS / 0 FAIL | **7 PASS / 1 FAIL** — see §7.2 |

**Real heavy play wants ~680 tiles, not thousands.** A 1024 pool covers it with
~35% headroom and takes the artifact to **exactly zero**, at 8.60 vs 8.59 ms
p50 — indistinguishable, and inside the noise §6b established.

### 7.1 Visual confirmation

The after-settle frames were compared directly. At cap 512 the scene has
several unmistakable **suspended water cubes** hanging in mid-air plus floating
sand and stone blocks. At cap 1024, in the otherwise identical scene — same
craters, same lava, same obsidian, same stone arch — **every one of them is
gone.**

One floater remains in both: a dark slab upper-left. That is **obsidian**,
which `MaterialRules` defines as `Define(Materials.Obsidian, 0u, 0)` — flags
zero, an ordinary static solid. It is §7.3's product formed in mid-air where
lava met falling water, it is not mobile, and the tile pool has nothing to do
with it. It belongs to the already-resolved "floating debris from destruction
is not a defect" category and should not be re-reported as this bug.

### 7.2 Raising the cap SURFACED something the small pool was hiding

This is the one result that argues against treating 1024 as a free win.

| | cap 512 | cap 1024 (two runs) |
|---|---|---|
| op-list total | 3.6 M | **7.9 M / 8.3 M** |
| ops dropped non-resident | **0** | **4,442,181 / 4,392,230** (53–56%) |

Op traffic more than doubled and over half of it is discarded. It reproduces
to within 1.1% across two runs, so it is not noise.

**It is not corruption.** That path is §9.4's documented guard: the op is
refused, the material stays exactly where it was, the counter is incremented.
Conservation was still exact in both runs, and frame time did not move — so
the wasted work is currently costing nothing measurable. But it is real work
being thrown away, and it appears only once the pool is large enough to hold
the tiles that generate it.

**Why — measured, not guessed.** Two candidates were possible: fluid pressing
outward against the streaming edge (the guard doing its job, just more often),
or tiles **outliving their chunk's residency**, since `Refresh` tests §9.4 at
*acquire* time only and `ReleaseBeyondSleep` tests only the radius. A counter
that is nonzero only in the second case was added and run:

```
TILES WHOSE CHUNK IS NO LONGER RESIDENT: 15 of 787
```

**So the second gap is real, not hypothetical** — a tile can and does outlive
its chunk. At cap 512 the pool saturates near the player and never has spare
capacity to hold such a tile, which is why the count is 0 there: the small cap
was masking it.

What this run does **not** establish is the split. 4.4 M drops over 12,000
frames is ~370/frame, and 15 orphaned tiles could plausibly supply all of that
or only part of it. **Apportioning the two causes would need another
measurement and was not done.**

---

## 8. Step 3 — the slot cap: not reached, and not implicated

Step 3 was conditional on tiles being available while slots ran out. That
condition is not met anywhere in this work:

| scenario | live slots peak | % of MAX_ACTIVE_FLUID |
|---|---|---|
| lattice, cap 512 (frozen) | 39,788 | **8.0%** |
| lattice, cap 2048 | 72,345 | 14.5% |
| siege, cap 512 | 321,695 | 64.3% |
| siege, cap 1024 (artifact gone) | 321,158 | 64.2% |

The marker cube froze with slots at **8%**. The siege's slot usage is
**identical at both caps** — 64% either way — while the artifact goes from
3.4% to 0. Slots are not what changed and not what was binding.

**Raising `MAX_ACTIVE_FLUID` would fix nothing here, and would not be free:**
more slots means more concurrent GPU simulation, which is the one cost the
tile cap did *not* impose. No experiment was run on it, because there is no
measurement showing it is the constraint — running one would be answering a
question the evidence says is not being asked.

---

## 9. RECOMMENDATION (Step 4) — a decision for a human, not a change made here

### 9.1 Yes: a higher tile cap alone fixes it, and cheaply in time

- **1024 is the right number, not 2048.** Demand is ~680 real / ~880 synthetic.
  1024 clears both with headroom; 2048 buys nothing and doubles the memory.
- **Cost in frame time: none measurable** (§6b, nine cooled runs,
  counterbalanced).
- **Cost in memory: +256 MB, always, allocated at the cap** — 264 → 520 MB.
  On this 8 GB machine that is the real price, and it is paid whether or not
  a player ever spreads that much fluid.
- **Correctness: unchanged.** Conservation exact, zero wake failures, zero
  readback errors, water conserved at every cap.

### 9.2 But it is not a free win, and the caveat is the decision

Raising the cap to 1024 **exposes 4.4 M discarded ops and confirms that tiles
outlive chunk residency** (§7.2). Costless today in frame time, and not
corruption — but it is a latent gap that the 512 cap was concealing, and
shipping 1024 makes it live.

Three ways forward, and **which one is right is a judgement call about how
much memory a fanless 8 GB machine should spend on fluid**:

| option | fixes the artifact | memory | risk |
|---|---|---|---|
| **A. cap 1024** | completely (0.0%) | +256 MB | surfaces §7.2's op waste |
| **B. cap 1024 + release tiles whose chunk is evicted** | completely | +256 MB | small, targeted change to `Refresh`; removes the §7.2 caveat |
| **C. keep 512, make the refusal visible** | no — makes it *legible* | 0 | player is told, not surprised |

**Recommended: B**, on the evidence — the artifact is real and visible in
ordinary play, 1024 resolves it at no measurable time cost, and the one
side-effect has a known, contained cause worth fixing at the same time rather
than shipping around. **A** is acceptable if the orphan-tile work is deferred
deliberately rather than forgotten.

### 9.3 If the memory is judged too expensive, the honest fallback (option C)

Not implemented, per instruction — the caps *can* solve it, so this is the
alternative rather than the plan. But the pattern already exists and the gap
is specific:

`Playground.TryPaintBrush` **already** refuses a mobile brush outside §7.4's
wake radius, with a red status line and a `Act.Refused` flash, and the HUD
already shows `pool full Nx (refused cleanly)`. **What it does not guard is
the pool.** Inside the radius with a full pool, placement is accepted and the
fluid silently never simulates — which is exactly the frozen cube.

So option C is: extend that existing guard to also refuse when
`FluidTileMap.Acquire` would fail for the target tile, reusing the same
message path. That turns a silent frozen blob into the same visible refusal
the radius already produces. It is a real behaviour change (placements that
used to succeed would start failing at the edge of a busy scene), which is why
it is a decision and not a cleanup.

### 9.4 A cheaper lever exists but is a redesign, not a knob

§5's tile-edge arithmetic: memory for the thin-and-wide shape is linear in
tile edge, so T=16 would **halve** the cost of exactly the case that breaks.
It is blocked by `ChunkFluidMask` packing a chunk's sub-tiles into one ulong
(4³ = 64 at T=32; T=16 needs 512). Recorded so it is not rediscovered; not
proposed for now.

---

## 10. What was NOT established

- **The split between the two causes of the 4.4 M dropped ops** (§7.2). The
  orphan-tile gap is confirmed to exist; its share is not measured.
- **Whether cap 1024 holds up beyond 200s.** Both siege runs at 1024 showed
  the same flat-to-improving trajectory as 512, but no longer run was done.
- **Any slot-cap experiment.** Deliberately not run — §8.
- **GPU-stage attribution for any of it.** Standing toolchain limitation.
- **Demand for scenarios wider than the siege.** Demand scales with spread;
  a pour twice as wide would need roughly twice the tiles, and 1024 is sized
  to *this* scenario plus ~35%, not to an arbitrary one.

---

# 11. SHIPPED (2026-09-11)

Both changes from §9's option **B** are in.

## 11.1 The cap: `EngineConfig.FLUID_TILE_POOL_CAPACITY = 1024`

One constant, eleven call sites — Playground, `Phase4AcceptanceRig`'s
hardcoded literal, nine rigs, eight scene-builder defaults. It carries its own
justification and its own cost, so the next person to consider raising it
finds the arithmetic rather than repeating the experiment.

Checked before changing: **no rig asserts on a particular cap value.** Every
gate is `PeakResidentTiles <= TileCapacity`, which is cap-agnostic; the "cap
WAS reached" lines are notes. Nothing was tuned to 512, so nothing needed
re-tuning off it.

**Headroom, corrected.** §7 quoted ~35% headroom from a single run measuring
demand 678. Across the four A/B runs below, peak demand ranged **603–779**.
1024 still clears it, but the honest figure at the observed maximum is
**~24% headroom**, not 35%. Demand varies run to run with where destruction
happens to throw material.

## 11.2 `FluidTileResidency.ReleaseOrphaned`

Gate 1 was only ever tested once: `Refresh` refuses to *admit* a tile whose
chunk is not resident, then never asks again, and `ReleaseBeyondSleep` tests
only the radius. A chunk evicted under a live tile left that tile resident
indefinitely — holding a pool slot and dispatching every tick against terrain
the CPU no longer had.

The sweep runs inside `Refresh`, in the release phase **before** admission,
for the same reason the radius release does: otherwise a pool full of dead
tiles refuses live ones for a whole cycle. `Stats.TilesReleasedOrphaned` is
kept separate from `TilesReleasedByRadius` — conflating them is how this gap
stayed invisible.

Five tests, **mutation-checked with 5 mutants, all killed** (no-op; predicate
inverted; counts-but-never-releases; swept-after-admission;
misattributed-to-radius). Tests cover both directions — a release rule that is
too eager is a tile that stops simulating while its world is right there.

## 11.3 The result: a counterbalanced A/B/B/A

Four 200s sieges, 300s cooled before each, cap 1024 throughout, the only
variable being the sweep. A `-noorphan` seam restores the pre-fix behaviour so
this is a same-build, same-session comparison rather than a number measured
against a previous session's thermal history.

| pos | arm | p50 | p99 | op-list total | discard | orphans retired |
|---|---|---|---|---|---|---|
| 1 | off | 8.48 | 42.21 | 7,926,929 | **56.2%** | 0 |
| 2 | **ON** | 8.70 | 48.76 | 4,170,944 | **5.9%** | 382 |
| 3 | **ON** | 8.61 | 48.96 | 4,216,045 | **6.0%** | 382 |
| 4 | off | 8.71 | 49.42 | 8,156,792 | **52.0%** | 0 |

| | before | after | |
|---|---|---|---|
| op-discard rate | 54.1% | **6.0%** | −89% relative |
| op-list total | 8.04 M | **4.19 M** | **−48% — half the op traffic was dead tiles** |
| p50 | 8.595 | 8.655 | +0.7% (0.06 ms) |

**No frame-time cost is measurable.** The +0.7% is smaller than the within-arm
scatter (ON 0.09 ms, OFF 0.23 ms), and the arms are position-balanced by
construction — OFF took positions 1 and 4, ON took 2 and 3, mean position 2.5
each. That balancing is not ceremony: position produced a 0.83 ms spread in
§6b, larger than the effect being measured here.

Both sweep-ON runs retired **exactly 382** orphaned tiles. Frozen-fluid census
**0.0%** in all four runs. lava+obsidian conserved **exactly** in all four.

## 11.4 The remaining 6% is correct behaviour, not a residual defect

It is fluid pressing on the **streaming edge** — §9.4's guard refusing to
simulate into unloaded world. `FluidOpListReadback` documents this itself: a
non-zero value "means fluid is live next to a streaming edge". The material
stays exactly where it is; nothing is lost. **This is not being chased.**

## 11.5 One gate was replaced, and it was not a weakened assertion

Flagged explicitly because that distinction is the whole point of the rule
against loosening gates.

`OpsDroppedNonResident == 0` was **stricter than the engine's own contract**.
A pour across ±260 voxels reaches the streaming edge by construction, so zero
is unachievable — and the gate was red on every siege run at cap 1024
**including the pre-fix baseline arms**. A permanently-red gate is worse than
no gate: it teaches the reader to skip the result.

It is replaced by an assertion on what *is* meant to be zero and what a
regression would actually break — **no tile may outlive its chunk** — with the
drop rate kept as a prominent note directly beneath it, carrying both the
before and after figures. Losing sight of that number is how the orphan bug
hid in the first place.

---

# 12. Regression sweep (Step 4) — and two failures

The whole suite, serialized, at the new cap. **Six of eight rigs and EditMode
match baseline exactly. Two rigs fail, and only one of them is caused by this
work.**

| rig | baseline | now | |
|---|---|---|---|
| `run-phase5a-rig.sh` | 5 scenarios, ledger balanced, no dup ownership | same | ✅ |
| `run-phase5c-rig.sh` | 170 / 0 | 170 / 0 | ✅ |
| `run-phase5d-rig.sh` | 19 / 0 | 19 / 0 | ✅ |
| `run-fluid-activity.sh` | 19 / 0 | 19 / 0 | ✅ |
| `run-phase6-sandbox.sh` | 43 / 0 | 43 / 0 | ✅ |
| `run-fluid-scale.sh` | 4 / 0 | 4 / 0 | ✅ |
| `run-phase6-brushguard.sh` | 30 / 0 | **16 / 14** | ❌ **pre-existing, not this work** |
| `run-fluid-tiled.sh` | 19 / 0 | **17 / 2** | ❌ **caused by the cap** |
| EditMode | 494 / 0 | **499 / 0** | ✅ (+5 new) |
| late-game siege (shipped config) | — | **8 / 0** | ✅ |

## 12.1 `run-fluid-tiled.sh` — the cap crosses a documented memory budget

Two assertions fail, and they are not threshold quibbles:

```
FAIL  the active set at the shipped radius is under 512 MB (520 MB)
FAIL  and orders of magnitude smaller than the dense region it replaces
```

The second is `denseCells * 16 / activeSet > 400`. At 1024 the ratio is 252×.

**Can both be satisfied at all?** The dense equivalent is 2048³ × 16 B = 128 GB.

| cap | active set | < 512 MB? | ratio | > 400×? | clears observed demand 779? |
|---|---|---|---|---|---|
| 512 | 264 MB | yes | 496× | yes | **no** |
| 640 | 328 MB | yes | 400× | no | **no** |
| **896** | **456 MB** | **yes** | 287× | no | **yes** (~15% headroom) |
| 1024 | 520 MB | **no** | 252× | no | yes (~31% headroom) |
| 2048 | 1032 MB | no | 127× | no | yes |

- **The `> 400×` assertion is incompatible with real demand.** It requires
  cap ≤ 639; peak demand across the four A/B siege runs reached **779**. No cap
  satisfies both. This assertion must be re-based or the artifact re-accepted —
  there is no third option.
- **The `< 512 MB` budget CAN be kept**, at cap 896: 456 MB, clearing 779 with
  ~15% headroom instead of 1024's ~31%.
- Worth noting the 400 threshold is **stricter than the claim it is labelled
  with**. The message says "orders of magnitude smaller"; 252× is still more
  than two orders of magnitude. 400 was calibrated when the cap was 512.

**The design's central claim is intact.** The assertion that matters —
"the active-set footprint does not move with the radius … the entire claim of
the design" — **passed**. What changed is the absolute size, not the
invariance.

**Neither threshold was changed here.** They encode a stated design budget, and
re-basing a budget to fit a number that just breached it is the exact move the
measurement rules exist to prevent. **This is a decision:** keep 1024 and
re-base both thresholds on the demand evidence, or drop to 896 and keep the
512 MB budget while re-basing only the ratio.

## 12.2 `run-phase6-brushguard.sh` — pre-existing, and NOT from this work

14 failures, all of the form "the target cell really is outside the arena" /
"outside the arena is REFUSED". The rig asserts through `Fluid.InRegion` and
`Fluid.SphereFitsInRegion` — **dense-path predicates**. Under tiling
`InRegion(v)` means "is v inside a RESIDENT TILE", which `Playground`'s own doc
comment already flags as the wrong predicate for a placement guard ("would
refuse to start a new pool anywhere"), and `SphereFitsInRegion` tests the dense
region box, which under tiling is a 64³ box at world origin.

**Ruled out as this session's doing, by experiment rather than by argument.**
Raising the cap changes how many tiles are resident, which changes `InRegion`,
so it was a real candidate. Re-running the rig with only
`FLUID_TILE_POOL_CAPACITY` reverted to 512:

```
cap 1024 -> PASS 16  FAIL 14
cap  512 -> PASS 16  FAIL 14     (identical)
```

The rig is unchanged since `c20a547`, which predates session 7 wiring
`Playground` to the tiled substrate, and no brushguard log exists after
session 6 — so it has been failing since Playground was tiled and nothing
re-ran it until now.

**This is the same class as CLAUDE.md's RIG SELECTION RULE**: an assertion
whose premise no longer applies, not a product defect. The brush guard itself
is correct and is exercised — `Playground.TryPaintBrush` refuses mobile brushes
outside §7.4's wake radius. What is stale is how the rig *asks* the question.
Fixing it means rewriting the rig's arena predicate to the wake radius, which
is a real piece of work and is **not** something to fold into a tile-cap
session.
