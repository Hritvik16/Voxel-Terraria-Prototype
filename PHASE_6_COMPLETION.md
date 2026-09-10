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

### 7.2 A DESIGN FORK that wants a human — the shipped fluid radius

**Measured 2026-09-05** (`run-fluid-scale.sh` step 3, and
`DESIGN_NOTE_7_4_ACTIVE_RADIUS.md` §9). A §7.2 region is a *dense per-cell map*
— four 4-byte GPU buffers, **16 B/cell**, confirmed identical at 64³/128³/256³ —
and `CSClear`/`CSCommit`/`CSWakeScan` each dispatch over every cell every tick.

For §7.4's radius to gate anything, the region must be *larger* than the radius.
The smallest power-of-two region whose half-diagonal reaches
`FLUID_ACTIVE_RADIUS_VOXELS = 1280` is **2048³ = 128 GB** of per-cell buffers.
At 256³ — already 256 MB — the radius is 5.8× the region's own half-diagonal.

**So at the shipped constant, §7.4's gate is inert by construction:**
`WithinActiveRadius` is always true and `BeyondSleepRadius` never is. The
mechanism is proven correct and proven to wake on approach; it is the *constant*
that selects a regime no single region can reach.

Three readings, and no document settles which is intended:

- **(a) The radius is mis-sized.** It was derived from C.5's LOD0 boundary — a
  *rendering* distance — and nothing checked it against the region it must fit
  inside. A one-line change makes the mechanism live immediately.
- **(b) The region is the wrong shape.** If a 128 m active radius is genuinely
  wanted, the region cannot stay a dense per-cell map. That is a §7.2 redesign.
- **(c) The radius belongs one level up** — selecting which of *many* regions
  tick, not which cells within one. 2/4/8 simultaneous regions scale linearly
  with no cross-region interference (flat 26.6% utilisation), so that path is
  real and cheap.

I have not chosen. (a) is a constant; (b) and (c) are architecture.

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

### 1. Retire the multi-instance fluid-simulation stopgap? — OPEN since 2026-09-06

§9.7's fix had `EditService` hold a **list** of separate `FluidGpuSimulation`
instances so two player-placed pools would both wake. The tiled substrate
subsumes this structurally — acceptance scenario D shows **one** CA instance
covering two pools 30 m apart (2/2 tiles resident, 10,610 voxel writes),
because a tile exists wherever fluid is.

- **Keep it:** the list is still what the **dense** path needs, and the dense
  path is the retained regression baseline for four phases of proofs.
  Removing a working mechanism the older path depends on buys nothing today.
- **Retire it:** it is no longer load-bearing for the tiled substrate, and
  carrying two ways to do the same thing has its own cost.

**Retiring it is really the same decision as retiring the dense path**, which
is the larger call. Evidence: `FLUID_SCALE_ARCHITECTURE_RESULTS.md` §4b.

### 2. Fluid apply budget: 1024 or 4096 ops/frame? — OPEN, measured 2026-09-10

Cooled sweep, driftchecks 0.2–4.0%:

| volume | budget | pump p99 | frame p50 | voxel writes | vs 4096 |
|---|---|---|---|---|---|
| 8,000 | **1024** | **0.995** | **8.900** | 268,990 | **−39.0%** |
| 8,000 | 4096 | 3.549 | 9.701 | 440,786 | — |
| 32,000 | **1024** | **0.969** | **7.605** | 320,356 | **−68.7%** |
| 32,000 | 4096 | 3.794 | 10.223 | 1,022,646 | — |

**16384 is ruled out on the evidence** — at 8,000 it buys +2.9% throughput for
+2.9 ms of pump p99 (throughput has already saturated by 4096); at 32,000 it
costs +9.5 ms pump p99 *and* +5.2 ms frame p50.

1024 vs 4096 is **not** decidable from the numbers:

- **1024** puts the apply burst under 1 ms at both volumes and gives the best
  frame p50 at 32,000 (7.6 vs 10.2). Choose if frame smoothness dominates.
- **4096** simulates fluid 1.6×–3.2× faster under load. Choose if fluid
  fidelity under heavy load dominates.

This is a question about how the game should feel, not a measurement.
**4096 remains the default** only because nothing beats it outright.

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

### 4. §2.2's GPU-lane budget is permanently unattributable here — STANDING LIMITATION, NOT A TASK

§2.2 budgets the fluid CA at ≤3.5 ms **on the GPU lane**. This toolchain
cannot attribute GPU stages: no Xcode and no Instruments (Amendment 8.9
Rule 1), `gpuFrameTime` is inflated ~2.6–2.7× (Amendment 8.10) and is read
nowhere, and per-kernel Metal attribution is a **confirmed dead end** — Unity
merges the CA's eight dispatches into one encoder, so only the first kernel is
ever named.

Every fluid figure on record is therefore **CPU-lane or wall-clock**. Whether
the tiled CA meets §2.2 is **unknown and unknowable in this workflow**. This
is not a pending measurement; it should stop being re-litigated each session.

### 5. Gas / fire / density-layered stacking — UNSPECIFIED, NOT A GAP TO FILL SILENTLY

Real Noita mechanics with **no specification anywhere in this architecture**.
Repeatedly deliberately not invented. Any implementation needs a design
conversation and a spec first. Not blocked by the substrate work.

### A methodological note that outranks most of the above

**This machine throttles monotonically and back-to-back runs are not
comparable.** An uncooled ON/OFF/ON/OFF sequence measured the *same* config at
4.172 / 4.751 / 11.812 ms — a 183% spread. Every timing figure in this document
from 2026-09-10 onward was taken with **300 s idle cooldowns between runs**.
Figures recorded before that date carry unquantified thermal inflation,
particularly the tail: Gate C frame p99 was recorded at 72–76 ms uncooled and
measures 50–68 ms cooled, **for both configs**.

*(Identical section maintained in `FLUID_PERFORMANCE_AB_RESULTS.md`; that file carries the full measurements behind each item.)*
