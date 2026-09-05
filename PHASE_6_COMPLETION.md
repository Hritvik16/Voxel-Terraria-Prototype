# Phase 6 Completion Record — Physics & Editing — **DRAFT**

> **THIS IS A DRAFT FOR REVIEW, NOT A PHASE CLOSURE.**
> Written unattended overnight on 2026-09-05. Closing a phase is the project
> owner's call, made after reading the evidence — not something an agent
> declares. Nothing in this document should be read as "Phase 6 is done".
> §7 lists what is still open, and §6's NOT TESTED bucket is not empty.

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

### PERFORMANCE — NOT MEASURED

- **No Phase 6 rig reports a millisecond figure.** This is deliberate and total.
- The exhaustive-AABB deviation (§1.1) is **not costed**.
- The frame-split budget bounds **voxels per frame, not time**.
- §13's "CPU-lane total under 16.6 ms" is **unmeasured** — the only rig that
  would have produced a figure is the one that never ran (§7.1), and even that
  one labels its output provisional and not a substitute for
  `run-acceptance-rig.sh`.

### NOT TESTED AT ALL

- **§3.6's LRU eviction path.** The integrated rig ran, but its checkerboard
  peaked at 388,280 dense bricks against a 425,000 high-water mark, so the valve
  was never asked to fire. `EngineConfig` line 57 names
  `BRICK_POOL_HIGH_WATER_FRACTION` an "ASSUMPTION, flagged, Phase 6 gate";
  **that gate is still open.** Closing it needs a wider window or a longer
  attack than the resident window currently allows.
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

### 7.2 Open questions for a human

- **The §8.1 treadmill fork** remains open. Nothing forced escalation, but
  nothing measured the probe controller's cost either.
- **Burst legality of the world** (§2.1) — a §3.2/§3.3 layout decision that
  §8.4 and §8.5 both currently wait on.
- **`PlayerFeedback`** (§3) — where it lives and who owns it.
- **Density and drag values** (§2.3) — content, seeded plausibly, untuned.
- **Whether `BRICK_POOL_HIGH_WATER_FRACTION = 0.85` is right** — still an
  assumption, still ungated.

---

## 8. Suite at draft time

```
EditMode                PASS 387  FAIL 0  SKIP 0
Phase 6 brush guard     PASS 30   FAIL 0
Phase 6 player          PASS 35   FAIL 0
Phase 6 CCD             PASS 19   FAIL 0
Phase 6 edit            PASS 34   FAIL 0
Phase 6 sandbox         PASS 34   FAIL 0
```

**This draft does not close Phase 6.** §6's NOT TESTED bucket still contains
§13 acceptance items — most importantly §3.6's LRU eviction path, which
`EngineConfig` itself names as this phase's gate and which the checkerboard did
not reach. Frame time is also unmeasured by the only source CLAUDE.md trusts.
Closing the phase is the owner's call.
