# Phase 6 Completion Record — Physics & Editing — **DRAFT**

> **THIS IS A DRAFT FOR REVIEW, NOT A PHASE CLOSURE.**
> First written unattended overnight on 2026-09-05; **substantially revised
> later the same day** with the results of the §3.6 gate, the two playtest-bug
> fixes, the §7.4 driving mechanism, the first real `run-acceptance-rig.sh`
> numbers of Phase 6, and a pre-existing/regression verdict on the frame-time
> stutter. Closing a phase is the project owner's call, made after reading the
> evidence — not something an agent declares. Nothing here should be read as
> "Phase 6 is done". §6's NOT TESTED bucket is smaller than it was and is still
> not empty, and §7 now carries a **design fork** that wants a human.

---

# MORNING VERDICT — 2026-09-11 — **A RECOMMENDATION, NOT A CLOSURE**

**I did not change the DRAFT marker. That is yours.**

## Recommendation: READY TO CLOSE — conditional on five minutes in Playground

Every open item that *can* be settled with evidence has been settled. What is
left is one category an agent cannot settle, and it needs your hands, not more
measurement.

### The five §10 blockers, as they now stand

| # | §10's blocker (2026-09-05) | now |
|---|---|---|
| 1 | "A design fork is open — the shipped fluid radius cannot bite inside any region that can exist" | **RESOLVED.** Stale. Reading (b) was built (§7.2's tiled substrate) and the radius is measured releasing at ~85 m and re-acquiring on return; footprint 264 MB identical at r = 128/640/1280 |
| 2 | "The GPU lane is entirely unmeasured" | **PERMANENT LIMITATION, not a task.** Restated so it stops being re-opened — no future session can close it |
| 3 | "One rig is red — a rare fluid stall not reproducible enough to isolate" | **REPRODUCED AND ACCEPTED.** 4 of 12 runs (33%) vs the 35% baseline; 1–2 voxels of ~2,800; cause already isolated, the *fix* is what is blocked by #2. The rig is **not red** — it passes with its rate window |
| 4 | "The §8.1 treadmill fork was never forced and never costed" | **RESOLVED: keep the probe controller.** 20 siege runs CCD-clean; the only 2 failures trace to a rig bug that destroyed the wall being asserted against |
| 5 | "Feel-based items remain feel-based" | **STILL OPEN, AND DELIBERATELY SO — this is the condition on the recommendation** |

### What shipped since the draft was written

`FLUID_TILE_POOL_CAPACITY` 512 → **1024** (measured demand 603–779; at 512 the
overflow was *visible* as frozen cubes). The orphaned-tile fix. `MAX_ACTIVE_FLUID`
500,000 → **750,000**. `MaxOpsAppliedPerFrameDefault` 4096 → **1024** (−30.8%
p99 at real scale). Playground raised to showcase it.

### The condition: five minutes in Playground

**Do not sign off without this.** Four things are measured, screenshotted and
described in this document, and *none* of them can be decided from a
measurement (§10 of the open items, below):

1. **Movement feel** — it collides correctly; does it play well?
2. **Eviction visuals** (§3.6) — the valve holds the cap; is what you see
   while building acceptable?
3. **Flood-front judgement** — it looks like chaos in the captures; does it
   look like *good* chaos in motion?
4. **Frozen fluid at deliberate oversubscription** — now only reachable by
   abusing the 750,000 clamp, not in ordinary play. Leave, or refuse visibly?

Playground is now tuned to show all four at real scale (92,000 vent voxels,
~200,000 live slots). If those four feel right, I see nothing else standing
between this and closure.

### Why I am recommending rather than declaring

Every serious bug in this project's history — the frozen-fluid ordering bug,
the scratch-pool leak, the tile-cap freeze — was caught by a human checkpoint,
never by an automated gate alone. **This session is itself an example.** The
suite was fully green while Playground silently emitted 310 voxels instead of
92,000, because a scene builder was overriding the constants; no gate noticed,
and only looking at a screenshot did. A clean table is not the same as a
correct system.

---

## 0.0 What changed since the first draft (read this first)

| | First draft | Now |
|---|---|---|
| **§3.6 LRU valve** (§13's named memory gate) | never reached — attack peaked 388,280 vs a 425,000 mark | **PASSES.** Peak 426,720, 401–544 LRU evictions, cap never exceeded, edit succeeds under live pressure, edits survive eviction |
| **CPU-lane frame time** | unmeasured | **MEASURED** — `run-acceptance-rig.sh`, 51 PASS / 0 FAIL. Upload p99 **0.721 ms** vs a 1.0 ms budget |
| **Frame-time stutter** | unknown | p99 32% over budget, **proven PRE-EXISTING** — reproduced on the pre-Phase-6 commit at equal magnitude |
| **§7.4 active radius** | not driven at all | driven, with hysteresis + re-centre threshold; **wake-on-approach was missing and is fixed** |
| **Shipped fluid radius** | never measured | measured: **cannot bite inside any region that can exist** — a design fork, §7.2 |
| **Delta saves under pressure** | assumed fine | **silent edit loss found and fixed** (scratch pool never reset) |
| **Sandbox rig** | 34 PASS | **41 PASS / 0 FAIL** |

Three real defects were found and fixed this week, and two of the three were
invisible until a test was made big enough or honest enough to reach them.
That pattern is the most important thing in this document.

---

## 0. What this phase was

§13 Phase 6: "the player, physics against CPU terrain (exact) and
bounded-latency fluid (§7.2), editing, destruction, buoyancy."

Six files, in §13's own order. All six now exist; five were greenfield and one
(`EditService`) was a Phase 0.5 stub whose `SetVoxel` threw.

| # | File | Was | Now |
|---|------|-----|-----|
| 1 | `PlayerController.cs` | stub, threw | probe-based controller + `PlayerMotor` + `PlayerConfig` |
| 2 | `SweptCCD.cs` | absent | swept pass, fluid accumulation, depenetration backstop |
| 3 | `EditService.cs` | Phase 0.5 stub | the §8.3 path, batch variants, prefabs, tool tiers |
| 4 | `ProjectileTrace.cs` | absent | CPU `GetVoxel` DDA on a shared walker |
| 5 | `DestructionReducer.cs` | absent | frame-split mass destruction + Proxy Drop |
| 6 | `Buoyancy.cs` | absent | three probes, C.6, and §8.2's speed clamp wired |

---

## 1. The §8.1 fork, decided by trying the cheap option

§8.1 and §13 both name a Phase-6 decision point: a character controller sampling
CPU terrain, or the 540-`BoxCollider` PhysX treadmill. §13: *"build the simple
version first… Only if it proves insufficient, escalate."*

**The simple version was built and did not prove insufficient.** PhysX never sees
the world; every collision answer comes from `IWorldQuery.GetVoxel`. No
`Rigidbody`, no `BoxCollider`, no treadmill. **The escalation was never
triggered and remains a live human decision**, not something this work closed.

### 1.1 One deviation from §8.1's wording, flagged rather than slipped in

§8.1 says "~6–12 DDA probes". `PlayerMotor` instead tests the player's AABB
against **every voxel it covers**. Same mechanism — CPU `GetVoxel`, no PhysX —
but exhaustive rather than sparse.

**Why:** with a 0.6 m body (6 voxels) and probes at the corners, a 1–3 voxel
pillar fits *between* the probes and the player walks through it. That is
exactly the "the simple controller can't do the job" evidence §13 wants weighed
for escalation, and it would have been self-inflicted.

**What it costs is not known.** Cost is bounded by the substep cap rather than
body size, but **no rig measures it**. This deviation is the single most likely
thing in Phase 6 to need revisiting on performance grounds.

---

## 2. Where the spec could not be followed literally

Three places. None was resolved quietly; each is recorded in the file that hit
it and is listed here so the set is visible in one place.

### 2.1 "In a Burst job" (§8.4) and "Burst-parallel" (§8.5) are not reachable

`StructHeaders.cs` declares `public class Chunk` holding a managed
`BrickHandle[]`, and `ChunkStore` holds a managed array of those. **Burst
compiles only unmanaged data**, so no job can read or write the world as it is
currently laid out — not even §8.5's read-only tally, which would parallelise
cleanly.

`VoxelRayWalker` is deliberately unmanaged so the traversal is Burst-legal the
moment the world is. Making the world Burst-legal is a §3.2/§3.3 memory-layout
change — **an architecture decision, deliberately not taken here.**

### 2.2 §8.5's "the CPU never loops per-voxel on the main thread" vs the single-writer invariant

CLAUDE.md: *"only the main thread mutates `ChunkStore`… If you find yourself
reaching for a lock anywhere in the streaming path, that's a sign the
single-writer rule was about to be violated."*

**The hard invariant won.** Writes stay on the main thread. What bounds the cost
is the other half of §8.5's own sentence — frame-splitting under a work budget —
which is what §13's "recovery ≤3 frames" actually measures anyway.

### 2.3 §8.6 reads registry data that had never been populated

A.7's `MaterialData` declares `density` and `viscosityDrag`; §8.6 says buoyancy
takes them "from the Registry". **Nothing in the project had ever written either
field.** The struct existed; the numbers did not.

`MaterialRules` now carries both tables, added additively beside the flags and
tick-interval tables. Values are seeded from real figures so the *ordering* is
defensible (honey sinks in water, lava outweighs both). **They are content to
tune, not settled physics, and no claim is made that they feel right.**

---

## 3. Two gaps found that are not Phase 6's to fill

- **`PlayerFeedback.Emit` does not exist.** §8.1 describes an engine-side event
  bus that `EditService`, `Buoyancy` and `DestructionReducer` call into "on the
  relevant moments (a block breaks, a splash occurs, a hit lands)". There is no
  such type anywhere in the project. **Not created** — an engine-wide bus three
  systems are meant to call is not a side effect of the projectile file. Each
  system returns its event data to the caller instead, which is where a bus
  would get it.
- **`BRICK_POOL_HIGH_WATER_FRACTION` is flagged in `EngineConfig` line 57 as an
  "ASSUMPTION, flagged, Phase 6 gate".** See §5.3 for what the checkerboard
  actually established about it.

---

## 4. Shared code, and why it was factored rather than copied

Three extractions, each because two callers were about to hold their own copy of
one answer — the shape that produced the brush-guard bug (`19b8ac6`), where the
vent path enforced a limit the paint path silently ignored.

- **`VoxelCollision`** — "does this voxel stop a body", shared by `PlayerMotor`
  and `SweptCCD`. Two copies would mean a body stopped by one pass and passed by
  the other.
- **`VoxelRayWalker`** — the DDA stepping machine, shared by `SweptCCD` and
  `ProjectileTrace`. Two hand-rolled traversals is two chances to get
  `tMax`/`tDelta` subtly different, and the failure would look like a projectile
  bug rather than a duplication bug. A test asserts the two agree about where a
  wall is.
- **`FluidGpuSimulation.SphereCovers`** — already existed; now also used by
  `EditService`'s brushes and `DestructionReducer`'s blast, so "radius" means
  one thing engine-wide.

Two rules live in `VoxelCollision` and are load-bearing:

1. **A non-resident chunk BLOCKS** (§9.4 applied to movement). `GetVoxel`
   answers Air for an unloaded chunk, deliberately and frozen (§12).
2. **Fluid does not block; sand does.** Sand is a falling *solid*.

**§8.6 deliberately inverts rule 1.** Buoyancy treats an unloaded chunk as
**dry**, because missing buoyancy for a frame is a missed splash, while invented
buoyancy from unloaded terrain launches the player at the window edge. Both
directions are tested.

---

## 5. §13's acceptance assertions, with measured numbers

### 5.1 Per-file, all green

| Rig | Result |
|-----|--------|
| `run-phase6-brushguard.sh` | `PASS 30  FAIL 0` |
| `run-phase6-player.sh` | `PASS 35  FAIL 0` |
| `run-phase6-ccd.sh` | `PASS 19  FAIL 0` |
| `run-phase6-edit.sh` | `PASS 34  FAIL 0` |

Selected measured figures:

- **3-voxel step climbed by exactly 0.300 m**, with a 4-voxel control stopping
  flush at the face (leading edge 1315.200 m vs face 1315.20 m).
- **Jump 0.822 m** against a closed-form v²/2g of 0.874 m.
- **§8.1 hot reload end to end:** rewriting `PlayerConfig.json` mid-run took the
  next jump from 0.822 m → 2.154 m (2.62×, predicted 2.56×), no recompile, and
  reverted when the file was restored.
- **0.2 m wall at 60 m/s: 20/20 stop at the face**, with an endpoint-only
  control confirming a naive check misses it entirely.
- **3-voxel water sheet at 60 m/s: 0.300 m traversed**, primary material water,
  both endpoints dry.
- **Tool tiers measured 10 / 40 / 200 vox/s exactly**, strictly ordered.
- **Breach drainage:** basin 900 → 821, apron 0 → 78, total conserved at 900.

### 5.2 EditMode

```
PASS 387  FAIL 0  SKIP 0      (baseline at phase start: 249/0/0; +138 tests)
```

### 5.3 The integrated acceptance test — RAN, and it changed the picture

`run-phase6-sandbox.sh` — **`PASS 34  FAIL 0  RESULT: PASSED`**

| §13 item | Measured |
|---|---|
| walk / jump | walked 20.60 m, never inside terrain; jumped 0.822 m |
| 0.2 m wall, grapple 60 m/s | 20/20 stop at the face; projectile agrees at 2.000 m |
| adversarial checkerboard | 86,715 edits; dense bricks peak **388,280 / 500,000** |
| drill 60 s @ 200 vox/s | **11,808** of a nominal 12,000 |
| save / reload round trip | probe chunk evicted, **27 deltas reloaded, hole survived, 0 rejected** |
| coalesce on fill-in | **925 bricks** collapsed back to uniform, pool slots returned |
| 400K detonation | radius 46 = 407,597 cells, **3 frames**, **one** Proxy Drop |
| 60 m/s dive | fluid contact frame **2**, 100% submerged, **+0.34 m/s²**, no slam |
| flood front | 4,378 ops; front reached the player frame 93, buoyancy same frame |
| CPU-lane total | p50 **17.30 ms**, p99 **38.87 ms** — provisional, see §6 |

**It earned its keep by finding a real engine defect** (§5.4), and its first two
runs failed 6 and then 2 assertions — every one of which was either that defect
or a scenario the rig itself had built wrong. None was papered over.

**Two gaps the run reports rather than hides:**

- **Pool pressure was never reached.** Peak 388,280 stayed under the 425,000
  high-water mark, so §3.6's LRU eviction path was never asked to fire. The
  attack is bounded by the resident window. **`EngineConfig` line 57's
  "ASSUMPTION, flagged, Phase 6 gate" is therefore still ungated.**
- ~~The probe chunk never evicted~~ — **FIXED AND NOW PASSING.** The walk was a
  flat 8 chunks against an evict radius of 15; it now derives the distance from
  `Streamer.EvictRadiusChunks` (21 chunks, 269 m) and walks rather than
  teleports, since a single-frame jump past the window trips ChunkStore's
  admission guard (§9.5) — a different failure from the one under test. The
  drilled hole survives a real eviction round trip.

### 5.4 The defect the integrated rig found

The staleness signal added for §8.2's speed clamp reset only when a **voxel was
applied**, so it climbed without bound whenever fluid was merely **settled** —
the rig measured **336 "stale" frames beside a pond at rest**. Fed to the clamp,
that would have throttled the player to 20 m/s during ordinary play with no
fluid anywhere near them.

It now resets when the op-list **comes back**: an empty batch is a healthy
readback reporting nothing moved; a stall is the readback not returning at all,
which is what §8.2 actually protects against. Worst observed staleness went
**336 → 5 frames**, and at 5 the clamp correctly engages.

**No isolated rig could have found this.** It needs a live CA, a settled body of
water, and something asking about staleness in the same run.

## 6. Evidence classification

### CORRECTNESS PROVEN

- **Movement:** gravity, landing, friction, air control, diagonal speed not
  exceeding max, ceilings, coyote time (including that it is spent once and not
  once per frame), 3-voxel step climbed / 4-voxel refused / step height tunable,
  no tunneling swept across speed **and start phase**, fluid passable but sand
  solid, non-resident blocks, spawn resolution.
- **CCD:** all four of §13's clauses for file 2 including its own named vacuity
  check, in EditMode and on real terrain.
- **Editing:** the §8.3 sequence in order, no-op fast paths not dirtying the
  chunk, batch/sphere/prefab with Air skipped, tool budget carrying fractions,
  unloaded-chunk edits refused and counted.
- **Destruction:** frame-split resumption removing exactly the same cells as an
  unsplit run, exactly one Proxy Drop per event, tally accounting for every
  removed voxel and nothing else.
- **Buoyancy:** C.6 in both directions (float and sink), scaling with submerged
  fraction, drag ordering from the registry, the speed clamp engaging at the
  §8.2 threshold using the *same policy object* as §8.2, and one-way sampling
  asserted by checking 50 samples change zero voxels.
- **The shared walker visits an unbroken chain of cells**, each one axis-step
  from the last, covering the segment end to end with no gaps or overlaps.
- **§3.6's LRU valve** — added 2026-09-05, and this is §13's named memory gate.
  Peak 426,720 dense bricks against a 425,000 high-water mark and a 500,000 hard
  cap; 401–544 LRU evictions; the cap never exceeded; the triggering edit
  succeeds *while the valve is engaged* (probed during pressure, not after); and
  a far-edge voxel round-trips through eviction, which is §3.6's "never lost
  progress (edits are in the delta, §4.2)".
- **§7.4's active radius** — hysteresis (wake 1× / sleep 1.15×) and the
  re-centre threshold proven as policy in `FluidActiveRegionTests`, with the
  control test showing a single radius *does* thrash; and proven on the GPU
  in `run-playtest-bugs.sh`, including that a region which slept mid-flow
  **wakes again on approach** (0 voxels left hanging, against 35 with the seed
  disabled — a built-in mutation control, not a one-off manual check).
- **Collision above the generation ceiling.** `VoxelCollision` rule 1 no longer
  reports permanently-non-resident sky as solid; a player at 300 m falls
  (44.37 m in 2 s, measured) instead of hanging. Mutation-checked in both
  directions, including 4 pre-existing §9.4 guards that catch the over-fix.
- **Delta saves survive an eviction storm.** The scratch pool used to build the
  pristine baseline is reset between saves, so `SaveDelta` no longer starts
  failing after ~10 chunks. Asserted end-to-end: no resident chunk is left
  `deltaDirty` after a flush.

### PERFORMANCE — CPU LANE MEASURED, GPU LANE STILL NOT

**Measured** (`run-acceptance-rig.sh`, release standalone, Apple M1, 960×540,
run `2026-09-05_144650`, **51 PASS / 0 FAIL**) — the only source CLAUDE.md
trusts:

| §2.2 item | Budget | Measured |
|---|---|---|
| terrain upload, steady | ≤1.0 ms | **p99 0.721 ms** ✓ |
| peak upload bytes/frame | 3.145 MB | 2.906 MB ✓ |
| our whole `update` interval | (CPU lane totals 3.2 ms) | p50 **0.15** / p99 4.23 ms |
| frame total | 16.6 ms | p50 **10.05** ✓ / p99 **21.98** ✗ / max 222.76 |

The upload p99 is the item CLAUDE.md's known-issues bullet lists as *red* at
0.98–1.63 ms. It measured 0.721 ms here. One run does not overturn a documented
variance, but it is green in this one.

**Still not measured, and deliberately not guessed:**

- **The entire GPU lane** (raymarch ≤9.0 ms, Fluid CA ≤3.5 ms, GPU total
  ≤13.0 ms). Per-stage attribution does not exist in this workflow —
  AMENDMENT_8_9 §0 Rule 1 rules out Xcode, Rule 2 states `FrameTimingManager`
  cannot report Performance State, and AMENDMENT_8_10 measured `gpuFrameTime`
  inflated ~2.6–2.7×. No GPU-lane figure is quoted against a budget anywhere in
  this phase, and none should be.
- **The exhaustive-AABB deviation** (§1.1) is still not costed individually,
  though `update` at 0.15 ms p50 bounds everything it is inside.
- **The §7.4 seed's GPU cost.** `RecentreSeedTicks = 4` is an engineering
  default; the cost of a seeded tick is not claimed.
- The frame-split budget still bounds **voxels per frame, not time**.

### NOT TESTED AT ALL

- ~~**§3.6's LRU eviction path.**~~ — **CLOSED 2026-09-05.** The attack was
  sized off the measured world (idle ~330,000 dense bricks, so it must add
  ~95,000) rather than off a guess, and now reaches the mark. See CORRECTNESS
  PROVEN above. `BRICK_POOL_HIGH_WATER_FRACTION = 0.85` is no longer *ungated* —
  the valve demonstrably fires at it and holds the cap. Whether 0.85 is the
  *right* fraction, and whether the resulting eviction is visually acceptable
  under normal building (§3.6's own wording), is still a judgement nobody has
  made.
- **§2.5's ~500,000 active-fluid target.** Untested and unclaimed. What *is*
  measured: peak occupancy is 2.63–3.00 slots per live voxel, so a 500,000-slot
  pool holds ~166,000 live voxels in one region. Whether the engine sustains
  §2.5's figure world-wide has not been asked.
- ~~Coalescing on fill-in~~ — **CLOSED.** Refilling the drilled region collapsed
  925 bricks back to uniform and returned pool slots (284,889 → 284,018 dense).
  All three clauses of §13's drill line now pass together.
- **Steep slopes, overhangs, cliffs.** File 1's walk crossed 0.30 m of relief
  over 31 m — a gently contoured snow plateau, not rugged ground.
- **Swimming** as a movement mode. Buoyancy produces forces; nothing consumes
  them in `PlayerMotor` yet.
- **The flood-front by-feel judgement** (§13's own wording) — inherently manual.
- **Whether the movement feels good.** §8.1 says no automated test captures it.
- **The 540-collider treadmill** — never built, by design.

---

## 7. Carried forward

### 7.1 The integrated rig runs, but the machine barely allows it

Three attempts were **killed by the host during the IL2CPP build** (system
memory pressure), and one earlier session-wide stall left the disk at zero bytes
with no shell command able to execute at all. The volume is 228 GiB and sat
between 72% and 100% full throughout; each standalone build costs ~2.4 GB plus a
large parallel-compile memory spike.

**What worked:** launching the rig detached (`nohup … &`) so the harness's
own low-memory reaper takes only the waiting shell, not the build. Every
successful integrated run in this session used that.

This is an environment limit, not a code one, but it makes the integrated rig
expensive to re-run — worth knowing before assuming it can be iterated cheaply.

### 7.2 RESOLVED 2026-09-11 — the shipped fluid radius, and the §7.2 rewrite that answered it

**This section previously read "A DESIGN FORK that wants a human" and listed
three unchosen readings. It was stale, and it contradicted the project's own
later record. Reading (b) was chosen and built; the fork is closed.**

The original finding stands as written for the DENSE design it described: a
§7.2 region was a dense per-cell map at 16 B/cell, so for §7.4's radius to gate
anything the region had to exceed the radius, and
`FLUID_ACTIVE_RADIUS_VOXELS = 1280` needs a 2048³ region — **128 GB**. At every
affordable size the gate was inert by construction.

**The three readings offered were (a) the radius is mis-sized, (b) the region is
the wrong shape, (c) the radius belongs one level up. (b) was implemented** as
§7.2's sparse tiled active set (`FluidTileMap`, `ChunkFluidMask`,
`FluidTileResidency`). Memory became O(fluid present) instead of O(radius³),
which makes the shipped radius affordable and hands the radius its real job:
bounding simulation cost by gating which tiles stay resident. (a) and (c) are
moot — the constant no longer needs changing, and tiles *are* the "many regions"
(c) proposed.

**The gate is not merely affordable now; it is measured working**, which is the
part that makes this resolved rather than merely re-architected:

| evidence | result |
|---|---|
| `FLUID_SCALE_ARCHITECTURE_RESULTS.md` §2 — footprint vs radius | **264 MB, identical at r = 128, 640 and 1280.** The footprint does not move with the radius |
| §3, "the radius actually gates" | standing on a pool → 2 tiles resident; **walked ~85 m away → the tile is RELEASED; returned → RE-ACQUIRED and simulating** |
| §4, explosion scatter | 441 chunks scanned by one `ulong` each; **0 of 219 pockets still holding mobile material failed to wake** |
| `run-fluid-tiled.sh` (19 PASS / 0 FAIL, re-run 2026-09-11) | asserts "the active-set footprint does not move with the radius — the entire claim of the design" |
| Playground, session of 2026-09-10 | 1,123 voxels of still water sampled outside the wake radius, **0 wake failures** — they sit in the wake/sleep hysteresis band and are correctly NOT promoted |
| Late-game siege, every run | "no silent wake failure: 0 of N sampled mobile voxels inside the radius lack a tile" |

The first and second rows are the fork's two halves answered directly: the
footprint stopped scaling with the radius, and the radius started releasing and
re-waking.

**What remains open from this area is NOT this fork**, and is tracked elsewhere
so it is not mistaken for it:

- Whether the tiled CA meets §2.2's ≤3.5 ms **GPU** budget is unknown and not
  claimed — that is the permanent toolchain limitation (open item 4), not a
  design question.
- The tile pool size, which `FLUID_SCALE_ARCHITECTURE_RESULTS.md` §6 called "an
  assumption … untested", **has since been measured and shipped at 1024**
  (demand 603–779 across cooled siege runs; 512 was overflowing visibly).
  See `FLUID_TILE_CAP_RESULTS.md`.

### 7.3 The frame-time stutter — REAL, and NOT Phase 6's

p99 frame time is **21.98 ms against §2.2's 16.6 ms** (32% over), with 8 frames
of 1340 exceeding 100 ms, worst 222.76 ms. `preUpdate` — the engine frame start —
is **99.7% of the stutter wall clock**; our `update` is 0.1% and `postLate` 0.2%.

**It is pre-existing.** Built and ran the acceptance rig on `c20a547`, the last
commit before Phase 6 file 1, with a byte-identical rig and `FrameGapProbe`:

| Gate C | pre-Phase-6 `c20a547` | HEAD |
|---|---|---|
| frame p50 | 9.59 | 9.92 |
| frame **p99** | **25.15** | **22.18** |
| frame max | 222.02 | 222.76 |
| preUpdate p99 | 20.89 | 20.46 |
| `update` p99 | 5.62 | **4.23** |
| stutter frames | 8 of 1316 | 8 of 1340 |

Phase 6 is *slightly better* at the tail. This is not this phase's defect and
was deliberately not chased further here. **One lead for whoever does**, present
identically in both builds: `GC collections DURING stutter frames: gen0 +2
gen1 +2 gen2 +2` — full gen2 collections coincide with the stutters, and GC
stop-the-world suspend is exactly what `preUpdate` contains.

### 7.4 Open questions for a human

- **The §8.1 treadmill fork** remains open. Nothing forced escalation; the probe
  controller's cost is now bounded by `update` p50 0.15 ms but not isolated.
- **Burst legality of the world** (§2.1) — a §3.2/§3.3 layout decision that
  §8.4 and §8.5 both currently wait on.
- **`PlayerFeedback`** (§3) — where it lives and who owns it.
- **Density and drag values** (§2.3) — content, seeded plausibly, untuned.
- **Whether `BRICK_POOL_HIGH_WATER_FRACTION = 0.85` is the RIGHT fraction.** No
  longer ungated — the valve fires at it and holds the cap — but §3.6 also asks
  whether the resulting eviction is *visually acceptable under normal building*,
  and that is a judgement, not a measurement.
- **`WaitForIdle` is rig-only today.** It now runs the §3.6 valve as it drains
  (it did not, and walked the pool into exhaustion). If any production path ever
  bulk-drains, this is the invariant it must keep.

---

## 8b. Suite — final consolidated run, 2026-09-11

Every rig in the project's history, serialized, at the fully shipped
configuration (tile pool 1024, `MAX_ACTIVE_FLUID` 750,000, apply budget 1024).

| rig | result |
|---|---|
| `run-acceptance-rig.sh` (terrain-only) | **53 / 0** — frame total p50/p99 **8.94 / 20.32**; Gate C §4.3 upload p99 **0.741 ms** vs the 1.0 ms budget; peak 2.98 MB vs the 3.145 MB cap |
| `run-phase5a-rig.sh` | 5 scenarios, ledger balanced, 0 duplicate ownership |
| `run-phase5c-rig.sh` | **170 / 0** |
| `run-phase5d-rig.sh` | **19 / 0** |
| `run-fluid-activity.sh` | **20 / 0** (floating 0 this run) |
| `run-phase6-brushguard.sh` | **16 / 14** — pre-existing, failure set **byte-identical** to the previous sweep, logged in CLAUDE.md |
| `run-phase6-sandbox.sh` | **43 / 0** |
| `run-fluid-scale.sh` | **4 / 0** — third rung now reports a 750,000-slot pool, picked up with no edit |
| `run-fluid-tiled.sh` | **19 / 0** |
| **late-game siege** (shipped config) | **8 / 0** |
| EditMode | **499 / 0** |

Siege detail at the shipped configuration:

```
tile pool cap 1024   apply budget 1024 ops/frame   SLOT CEILING 750,000
live slots peak      411,395 / 750,000
PEAK TILE DEMAND     792 (cap 1024)
FROZEN-FLUID CENSUS  0 of 2,232 sampled  ->  0.0%
no tile outlived its chunk   0 of 830 resident (300 retired during the run)
lava+obsidian        13,479 -> 13,479  (0.00%)
WHOLE RUN  p50 8.20  p99 35.29  max 155.06 ms
```

**Nothing regressed.** The only red is the pre-existing brushguard rig, whose
failing assertions are dense-path predicates that no longer describe a tiled
build — the rig is stale, not the product (CLAUDE.md known issues).

Note the siege p99 of **35.29 ms** against the 43–48 ms typical of earlier
runs: consistent with the apply budget moving to 1024, though this is a single
run and the A/B in open item 2 is the measurement that established it.

---

## 8. Suite — one consolidated run, 2026-09-05

Every line below was run against the current tree in one pass, after all of
this week's fixes. Nothing here is carried over from an earlier session.

```
EditMode                     PASS 414  FAIL 0  SKIP 0
Phase 6 brush guard          PASS  30  FAIL 0
Phase 6 player               PASS  35  FAIL 0
Phase 6 CCD                  PASS  19  FAIL 0
Phase 6 edit                 PASS  34  FAIL 0
Phase 6 sandbox (integrated) PASS  41  FAIL 0     <- was 34; §3.6 gate now included
Playtest bugs (diagnostic)   PASS  13  FAIL 0
Fluid scale (counters only)  PASS   4  FAIL 0
run-acceptance-rig.sh        PASS  51  FAIL 0
```

`run-fluid-activity.sh` is the one line that is **not** clean — see §9.

## 9. The intermittent fluid stall — real, but NOT a regression

**Corrected 2026-09-05.** An earlier revision of this section said *"that rig's
last recorded result was 20 PASS / 0 FAIL, so this is a change, not a known
state."* **That was wrong**, and it was wrong in the specific way this project
keeps warning about: it compared two single samples of a stochastic quantity.

### The A/B that settled it

`run-fluid-activity.sh` step 2 leaves 1–2 water voxels unsupported out of
~2,900, tripping a `floating > 0` assertion. Built the commit immediately before
this session's fixes (`d4ba1ff`) in a scratch worktree and ran matched samples:

| commit | runs failing | floating counts |
|---|---|---|
| `d4ba1ff` (pre-fix) | **2 of 6** | 0, 2, 0, 0, 1, 0 |
| `114ec64` (post-fix) | **2 of 6** | 2, 1, 0, 0, 0, 0 |

**Identical.** Fisher exact **p = 1.000**; including three earlier post-fix runs
(5 of 9 overall), p = 0.608. The first `d4ba1ff` run scored 20/0 and would have
"proven" this session caused the regression; the second run of the *same binary*
scored 16/4. There is no regression and no earlier boundary to find — the metric
simply fails about a third of the time at any commit.

`d4ba1ff` is flaky in a second, independent place too: one run failed step 5's
"NO MASS LOST across eviction (1134 → 527)" because the chunk was not resident
when mass was counted — the same §9.4 residency-versus-loss trap this rig's own
history already records.

### The stall underneath it is still real

Ruled out by measurement, not argument: **not** an early stop (0 immediate and 0
deferred wake requests outstanding, still floating after 600 further ticks);
**not** pool exhaustion (848 of 8,192); **not** the §7.2 region boundary
(straight-down descent stays in-region there); **not** §7.4's re-centre seed
(step 2 never calls `UpdatePlayerPosition`, so it is never armed, and it only
ever wakes *more* cells).

It is the same shape as the §7.4 bug fixed this week — a cell needing a wake that
nothing asks for — with a different trigger: `CSWakeScan`'s propagation needs a
neighbour carrying a `WakeMark` from a descending move *this tick*, and a local
neighbourhood that goes quiet in the same tick has nothing left to propagate
from.

### What changed in the rig

The assertion is now a **rate over a 10-run window**, recorded across runs, and
fails only when the rate is clearly worse than the measured 35% baseline. This
owns regression detection only — **a non-zero rate is still reported as an open
defect on every run**, so a green run can never be read as "no floaters". The
old single-sample boolean could not tell a regression from noise and produced
exactly one false regression report that cost a session to disprove.

### Why it is not fixed

The obvious fix is to let `CSWakeScan`'s radius-driven seed run always rather
than only after a re-centre. The dispatch already covers every cell every tick,
so the added work is bounded — but it lands in §2.2's GPU lane, which this
workflow cannot measure at all. That trade is not one to make blind.

## 10. The verdict this draft supports — and does not

**Closing the phase is the owner's call. This document does not close it.**

What changed in Phase 6's favour today: §13's one named §3.6 memory gate now
genuinely passes, the CPU lane is measured against §2.2 for the first time and
is inside budget, the frame-time stutter is proven to be inherited rather than
caused here, and three real defects were found and fixed — two of which existed
only because a test was too small or too forgiving to reach them.

What a reader should weigh against that:

1. **A design fork is open** (§7.2): the shipped fluid active radius cannot
   bite inside any region that can exist. It needs a decision, not more
   measurement.
2. **The GPU lane is entirely unmeasured** and cannot be measured in this
   workflow. §2.2's raymarch, Fluid CA and GPU-total budgets have no numbers
   against them, and the honest position is that nobody knows.
3. **One rig is red** (§9) — a rare fluid stall that is not yet reproducible
   enough to isolate.
4. **The §8.1 treadmill fork** was never forced and never costed.
5. **Feel-based items remain feel-based**: whether movement is good, whether
   §3.6's eviction is visually acceptable under normal building, and §13's own
   flood-front judgement.

None of 1–5 is a correctness failure. All five are the kind of thing that is
much cheaper to decide before a phase is declared closed than after.


---

> **The section below was added 2026-09-10 by an agent session. It changes
> NOTHING about this document's DRAFT status, which remains the project
> owner's call.** It is a pointer, so the open decisions from the fluid
> performance line of work are visible from here rather than only from
> `FLUID_PERFORMANCE_AB_RESULTS.md`.

---

# OPEN ITEMS REQUIRING A HUMAN DECISION

**One place, so this does not have to be mined out of five documents.**
Last consolidated 2026-09-10 (`e71cd01`). Every item below is a judgment call
with evidence for each option, deliberately **left undecided** by the agent
sessions that surfaced them. None is a correctness failure.

### 1. Multi-instance fluid-simulation stopgap — RESOLVED 2026-09-11: **KEEP**

§9.7's fix had `EditService` hold a **list** of separate `FluidGpuSimulation`
instances so two player-placed pools would both wake. The tiled substrate
subsumes this structurally — acceptance scenario D shows **one** CA instance
covering two pools 30 m apart (2/2 tiles resident, 10,610 voxel writes).

**Decision: keep it.** "No longer load-bearing for the tiled path" is not
sufficient reason to delete a working mechanism the **dense** path still
depends on, and the dense path is the retained regression baseline for four
phases of proofs.

**Evidence it is not dead weight:** the dense path is still exercised and still
green on every sweep — `run-fluid-activity.sh` **19 PASS / 0 FAIL** in this
session's final sweep, and it runs the dense region, not tiles. Deleting the
stopgap is really the same decision as retiring the dense path; that is a
larger call and nothing forces it now.

**Reversible either way**, which is why this is a low-risk default rather than
a hedge: the tiled path does not consult the list at all.

### 2. Fluid apply budget — RESOLVED 2026-09-11: **SHIPPED AT 1024**

`FluidOpListReadback.MaxOpsAppliedPerFrameDefault` is now **1024** (was 4096).

**The previous verdict — "not decidable from the numbers, a question about how
the game should feel" — was drawn from a sweep at 8,000 and 32,000 live voxels.
The engine now runs ~320,000 in the siege, a 10× larger scenario, and at that
scale it IS decidable.**

Re-measured on the late-game siege, cooled 300s, **A/B/B/A** so the arms are
position-balanced against thermal drift (4096 took positions 1 and 4, 1024 took
2 and 3 — mean position 2.5 each):

| budget | p50 (two runs) | p50 mean | p99 (two runs) | p99 mean |
|---|---|---|---|---|
| **1024** | 7.99, 8.10 | **8.04** | 31.20, 29.37 | **30.29** |
| 4096 | 8.40, 8.20 | 8.30 | 45.52, 42.01 | 43.77 |
| | | **−3.1%** | | **−30.8%** |

Within-arm p99 spread was 1.83 ms (1024) and 3.51 ms (4096), so a **13.5 ms
gap is far outside the noise**. **At real scale this is not a tradeoff** — 1024
wins on p99 *and* p50, which the small scenario did not predict. All four runs
8 PASS / 0 FAIL with a 0.0% frozen-fluid census.

**What 1024 costs**, recorded so it is not rediscovered as a surprise:

- Applied throughput ~**16% lower** (6.46 M vs 7.67 M voxel writes per run).
- The opening apply backlog is larger and drains slower: **26,884 average in
  the first 30 s, clear by ~60 s**, against 7,301 clear by 30 s.
- Live volume consequently **overshoots** the pour's target band early (412 K vs
  321 K average in segment 2), because fluid whose moves have not been applied
  yet stays live.

None of that broke a gate. The axis this project has been chasing all along is
stutter, and 1024 removes 13.5 ms of p99 for 16% of throughput that nothing is
currently short of.

**Revisit if** fluid fidelity under heavy load ever becomes the complaint
instead of smoothness — `-applybudget N` on the siege rig re-runs this A/B in
about 40 minutes.

### 3. Should §4.3's 1.0 ms upload gate apply under live fluid at all? — NEW

The cascade fix halved the **median** (upload_ms p50 6.60 → 3.99, downsample
p50 6.29 → 3.52) but **§4.3 is a p99 gate and the p99 barely moved**
(~8.8 → ~8.3 ms). It still fails at ~8× budget with fluid, while **terrain-only
now passes at p99 0.613 ms with 0 FAIL**.

- **Keep one gate:** a budget that only holds without fluid is not a budget.
- **Scope it per rig:** the number was derived for terrain streaming; a live
  fluid load is a different workload, and this is the same reasoning already
  accepted for the terrain-identity gates (CLAUDE.md's RIG SELECTION RULE).

Deciding this needs a target for what fluid *should* cost, which does not
exist yet. Attributing the remaining p99 tail is a separate isolation job and
is **not** claimed to be understood.

### 4. §2.2's GPU-lane budget — PERMANENT TOOLCHAIN LIMITATION. NOTHING TO RESOLVE, EVER, IN THIS WORKFLOW

§2.2 budgets the fluid CA at ≤3.5 ms **on the GPU lane**. This toolchain
cannot attribute GPU stages: no Xcode and no Instruments (Amendment 8.9
Rule 1), `gpuFrameTime` is inflated ~2.6–2.7× (Amendment 8.10) and is read
nowhere, and per-kernel Metal attribution is a **confirmed dead end** — Unity
merges the CA's eight dispatches into one encoder, so only the first kernel is
ever named.

Every fluid figure on record is therefore **CPU-lane or wall-clock**. Whether
the tiled CA meets §2.2 is **unknown and unknowable in this workflow**.

**Restated plainly 2026-09-11, because it keeps being re-opened: this is not a
pending task and has no resolution to reach.** It has been true and unchanged
for the entire project. It is a property of the toolchain (no Xcode by
standing rule, no Instruments, `gpuFrameTime` inflated ~2.6–2.7×, Unity merging
the CA's dispatches into one encoder), not a gap in the work. The correct
handling is the one already in force: never quote a GPU figure against a
budget, and treat wall clock as the only trusted source. **Nothing a future
session does will close this**; it should be read as a constraint on what can
ever be claimed, not as an item awaiting effort.

### 5. Gas / fire / density-layered stacking — UNSPECIFIED, NOT A GAP TO FILL SILENTLY

Real Noita mechanics with **no specification anywhere in this architecture**.
Repeatedly deliberately not invented. Any implementation needs a design
conversation and a spec first. Not blocked by the substrate work.

### 6. Large-scale chaos — PARTLY RESOLVED 2026-09-11; both ceilings have moved

The ladder ran 50,000 → 150,000 → 400,000 → 1,002,923 placed fluid voxels
under continuous pours, overlapping detonations and a moving, digging player.
**All four rungs passed 6/0.**

**Both ceilings this item named have since been raised on measured evidence:**

| ceiling | then | now | why |
|---|---|---|---|
| tile pool | 512, 31,400 refusals at the top rung | **1024** | measured demand 603–779; at 512 the overflow was visible as frozen cubes |
| `MAX_ACTIVE_FLUID` | 500,000 | **750,000** | ladder to 1.5M live, 8/8 gates green; 750K costs nothing measurable |

The tile-pool half is **fully resolved** — the frozen-fluid census is **0.0%**
in every siege run since, including at 1.5M live.

**What remains is the same gameplay-feel question, at a higher threshold:** is
fluid visibly frozen in mid-air acceptable *when a player genuinely
oversubscribes the new 750,000 clamp*? Nothing in ordinary play reaches it —
the siege peaks at ~320,000 — so this is now a question about deliberate abuse
rather than about normal use. **Still not decided, and still not an agent's
call** (see the feel-based items below).

### 8. The intermittent fluid stall — RESOLVED 2026-09-11 as KNOWN AND ACCEPTED

Re-measured with a fresh, larger sample this session: **12 consecutive runs of
`run-fluid-activity.sh`**, one build, same scenario.

| floating voxels at quiescence | runs |
|---|---|
| 0 | 8 |
| 1 | 3 |
| 2 | 1 |

**4 of 12 = 33%**, against the recorded 35% baseline. P(≤4 failures in 12 at a
true rate of 0.35) = **0.58** — indistinguishable. The rate has not moved, in
either direction, across everything shipped since it was first recorded.

**It reproduces, and it reproduces with exactly the documented signature**, so
this is not an unreproducible ghost:

```
1-2 floating at quiescence. Pending wake requests: 0 immediate, 0 deferred.
after 600 extra ticks: 2 floating (was 2)
STILL FLOATING after 600 further ticks -- a genuine stall, not an early stop.
```

Magnitude is **1–2 voxels out of ~2,800 placed (0.04–0.07%)**, every time. No
run has ever produced a large floater count.

**A nuance worth recording**, because it misleads at a glance: the per-burst
counters show `floating` reaching 90–98 mid-run. That is fluid *in flight* and
is normal — the defect is only floaters **at quiescence**, and the two are
different measurements in the same log.

**Why it is ACCEPTED rather than fixed.** The cause is already isolated (§9):
`CSWakeScan`'s propagation needs a neighbour carrying a `WakeMark` from a
descending move *that tick*, and a local neighbourhood going quiet in the same
tick has nothing left to propagate from. The obvious fix — let the
radius-driven seed run every tick rather than only after a re-centre — lands
squarely in §2.2's **GPU lane, which this toolchain cannot measure at all**
(open item 4). Trading an unmeasurable amount of per-tick GPU work for two
voxels in 2,800 is not a trade to make blind, and forcing it without a
measurable cost is exactly the kind of change this project's rules exist to
prevent.

**Status: KNOWN, ACCEPTED, low-rate intermittent.** Regression detection is
owned by the rig's 10-run rate window; a non-zero rate is still reported on
every run, so a green run can never be misread as "no floaters".

### 9. The §8.1 treadmill-vs-probe fork — RESOLVED 2026-09-11: **KEEP THE PROBE CONTROLLER**

§13 says "build the simple version first… Only if it proves insufficient,
escalate." The question this session asked is narrow and answerable: **did
anything, anywhere in the QA pass or the siege testing, ever hit the failure
signature that would force escalation?** No.

**CCD across every siege run on record** — the assertion that would catch a
probe controller missing collisions at speed:

| result | runs |
|---|---|
| **PASS**, all sweeps hit the wall (40/40, 12/12, 8/8) | **20** |
| FAIL, 33/40 | 2 |

**Both failures were traced to a rig bug, not the controller**: a radius-40
detonation at `c.x+65` spans `c.x+25..+105` and destroyed the CCD wall at
`c.x+30..31`. The assertion was right and the arena was wrong; after the wall
moved, 40/40 in every subsequent run. That is recorded in
`PHASE_6_QA_PASS.md` §7.5.

**The QA pass looked for the other half of the signature and found none:** "no
clipping through terrain, no jitter at rest, camera stable through
grapple-speed CCD passes."

**Recommendation: keep the probe controller.** PhysX never sees the world; no
`Rigidbody`, no `BoxCollider`, no 540-collider treadmill. Nothing measured has
asked for one, and building it speculatively would add a whole physics
integration to carry with no evidence it is needed. **If the signature ever
does appear** — clipping at speed, missed CCD sweeps not traceable to the
arena — the escalation is still available and §8.1 still describes it.

### 10. Feel-based items — NOT RESOLVABLE BY AN AGENT, AND NOT RESOLVED HERE

**This is the one category that needs the user's own hands on the controls.**
Listing it explicitly so it is not mistaken for an oversight or quietly
defaulted:

- **Movement feel** — whether the probe controller *feels* right to play, as
  distinct from whether it collides correctly (which it does; see item 9).
- **Eviction visual acceptability** (§3.6) — `BRICK_POOL_HIGH_WATER_FRACTION
  = 0.85` is no longer ungated and the valve holds the cap, but §3.6 also asks
  whether the resulting eviction is *visually acceptable under normal
  building*. That is a judgement, not a measurement.
- **Flood-front judgement** — whether large-scale fluid *reads* correctly in
  motion. The numbers say it is correct and the screenshots say it looks like
  chaos; whether it looks like **good** chaos is a different question.
- **Frozen fluid at deliberate oversubscription** (item 6) — acceptable, or
  worth a visible refusal?

An agent can measure these, screenshot them and describe them — and this line
of work has. It cannot decide them. **Five minutes in Playground is the
instrument.**

### 11. Horizon FPS investigation — RESOLVED 2026-09-12, see `FPS_INVESTIGATION_RESULTS.md`

A reported "FPS drop when looking at the horizon" was investigated end to end.
**The horizon was not the cause.** Sustained rate went **65.1 → 75.6 FPS** by
reverting a Playground *demo* change (its local `_activeRadiusVoxels`, raised
128 → 1280 the previous session, which drove ~486 resident tiles through the
CA's region passes). **No engine constant was touched.**

Full write-up, including three measurement traps that each produced a
confident wrong answer before being caught, is in
**`FPS_INVESTIGATION_RESULTS.md`**. The parts that outlive this one
investigation:

- **Windowed capture is worthless on this machine** — identical runs returned
  26.9 and 122.8 FPS. Fullscreen reproduces to 2.9%.
- **Percentile frame time misleads** — the display is 120 Hz ProMotion and the
  app out-presents it, so a third of frames block in `present` regardless of
  workload. Use frames ÷ elapsed wall time.
- **Counterbalance every sweep** — a ~7% position/thermal decay per run
  sequence will otherwise read as an effect. It did, twice.

**Two shader fixes were tried, measured and REJECTED** (air-mip reuse in the
tier path, 67.0 → 44.9 FPS; tier dense inner loop, 66.0 → 61.4 FPS). Both are
written up with their numbers so they are not re-attempted blind.

**Deferred, ranked, not urgent** — we sit 26% above the 60 FPS target:
1. Fluid CA region passes scale with *resident tiles*, not live fluid
   (~11 FPS, 75.6 → 87). Touches §7.3 and the §3.9 sync contract; needs its
   own session and oracle.
2. LOD cascade costs 16.8% at high resolution because tiers 1/2 have no
   air-mip pyramid (a documented scope cut).

**A mild stutter was reported during sustained play. It was not isolated or
chased this session.** If it persists it is a candidate for the same
methodology — fullscreen only, frames ÷ elapsed-time, counterbalanced A/B.

### 7. The p99 frame-time tail is PERMANENT AND TOOLCHAIN-BOUND, not a to-do

Restated here because it keeps being re-opened. Across five sessions, **ten
candidates were eliminated by measurement** (GC, in-run thermal, LOD cascade,
vsync/present, streaming starvation, upload volume, CPU apply, GPU time, CA
dispatch rate, rig overhead), and a **per-system toggle sweep turned every
subsystem off one at a time — including fluid — and the tail did not move**
(disabling fluid gave the *worst* figure of the eight configurations).

On an affected frame Unity reports main thread ~9 ms, present wait 0.0, GPU
~30 ms inflated: **nothing the engine times accounts for the wall clock.**

Separating what remains needs GPU-stage attribution, which this workflow does
not have and cannot get (no Xcode, no Instruments; Unity merges the CA's
dispatches into a single Metal encoder). **This is a standing limitation of
the toolchain, not an open task.** p50 is stable and quotable at every scale
measured; p99 is not, and should be quoted as a range or not at all.

### A methodological note that outranks most of the above

**This machine throttles monotonically and back-to-back runs are not
comparable.** An uncooled ON/OFF/ON/OFF sequence measured the *same* config at
4.172 / 4.751 / 11.812 ms — a 183% spread. Every timing figure in this document
from 2026-09-10 onward was taken with **300 s idle cooldowns between runs**.
Figures recorded before that date carry unquantified thermal inflation,
particularly the tail: Gate C frame p99 was recorded at 72–76 ms uncooled and
measures 50–68 ms cooled, **for both configs**.

*(Identical section maintained in `FLUID_PERFORMANCE_AB_RESULTS.md`; that file carries the full measurements behind each item.)*
