# Phase 5B Completion Record — Fluids: GPU Port (Shipped Path)

**Project:** Voxel Terraria 1 Byte BrickMap
**Spec:** ARCHITECTURE_v8.6.md §13 Phase 5b, §7.2, §7.3, §7.8, §8.4
**Date:** September 3, 2026 (updated September 4, twice — see §10)
**Branch:** `phase5a-fluid-reference`
**EditMode suite:** `PASS 249  FAIL 0  SKIP 0`
**Validation rig:** 5 scenarios, conservation MATCHES on all five; the
§7.8-shaped steady-state assertion MATCHES on **5 of 5**, with floating drops
`GPU 0 / CPU 0` on every scenario (see §4 and §10)
**Hardware:** Apple M1 Air (fanless, 8GB unified memory)
**Engine:** Unity 6000.3.10f1, release standalone

---

## PHASE 5B IS NOT CLOSEABLE. READ §4, §8 AND §10 BEFORE TREATING FLUID AS DONE.

**Status as of Sept 4 (second update).** The GPU CA runs, conserves mass exactly
on every scenario, produces obsidian, leaves no drop stranded in mid-air, and is
visible in 3D through the shipped raymarcher with no fluid-specific render code.
**All five** scenarios now match the oracle on every asserted steady-state check.

**The automated side is clean.** The two items that previously blocked it —
`pour_water`'s surface height and the unexplained intermittent floating drop —
are both resolved; see §4.4 and §10. What remains is **two gates that need a
human and cannot be closed here at all**: the Metal claim-race test and any
Xcode-verified timing. Two §13 acceptance assertions also remain untested behind
them (pool exhaustion, CPU-lane apply cost) — see §5 and `PHASE_5C_COMPLETION.md` §8.

The §4 question this document previously left open — variance or bug — **has
been answered by experiment**, and the answer turned out to be *both*, for
different statistics. See §4.

**A defect class none of this could catch was found afterwards, by a human
clicking:** every hand-placed fluid voxel froze, because the shipping frame
ordering is the opposite of the one every rig here uses. That work, the rig
built to close it, and the resolution of the intermittent floater live in
`PHASE_5C_COMPLETION.md`. §10 summarises what it changed about this document.

All timing here remains **PROVISIONAL — NOT XCODE-VERIFIED**.

---

## 1. Scope delivered (§13 Phase 5b's file list)

1. **`CoreEngine/Simulation/FluidCA.compute`** — `FluidReferenceCPU` ported to
   HLSL. Eight kernels: `CSClear`, `CSPromote`, `CSReact`, `CSIntent`,
   `CSCommit`, `CSWakeScan`, `CSSweep`, `CSFinalize`. Claims are **plain
   stores**; the only atomics are the slot allocator and op-slot reservation,
   neither in the claim path (§7.3, 8.4).
2. **`CoreEngine/Simulation/FluidOpListReadback.cs`** — `AsyncGPUReadback` of
   the bounded op-list, applied through **`ChunkStore.SetVoxel`** (§8.3's path
   and the engine's single terrain writer), which marks chunks dirty itself.
3. **Wake-scan hook in `EditService`** — hook only. `SetVoxel` remains the
   Phase 0.5 stub that throws; Phase 6 owns it, per §13's explicit instruction.
4. **Renderer wiring** — zero code. Constructing a `TerrainClipmap` sets
   `TerrainClipmap.Active`, which `RaymarchFeature` already reads.

Supporting: `FluidGpuSimulation.cs` (buffers + dispatch), `Phase5bBasin` +
`Phase5bValidationRig` (oracle comparison), `Phase5bDemo` (demo + playable),
`FluidClaimStress.compute` + `Phase5bClaimStress` (§7.3's Metal test, **not
run**), `Assets/Editor/ShaderCompileCheck.cs`.

---

## 2. Evidence classification

### CORRECTNESS PROVEN

- **Mass conservation on the GPU path**, all five scenarios, against the CPU
  oracle on identical inputs: 150/150, 24/24, 62/62, 150/150, 150/150.
- **No fluid is ever stranded in mid-air**, on either implementation, including
  on staircase and overhang geometry (§4.5). Asserted as an exact zero.
- **All five scenarios match the oracle on every ASSERTED steady-state check**
  (conserved count, settled-layer occupancy, floating drops = 0 on both sides).
  Surface height and occupied-layer count are REPORTED, not asserted, because
  they were measured to move run-to-run on the GPU — §4.3.
- **Full CA pipeline executes**: `promote.ALLOCATED=47`, `intent.awake=47`,
  `intent.CLAIMS=24`, `commit.claims_seen=24`, `commit.APPLIED=24`.
- **Sand's final distribution matches the oracle exactly** (per-layer delta 0).
- **Lava's viscosity is real** — 62 drops at interval 6 take 648 ticks to settle.
- **Water+Lava → Obsidian on the GPU path** (demo: obsidian 0 → 5).
- **The op-list is bounded to changed cells** (§13's named failure signature):
  peak 24–48 ops/frame against 24–130 active slots, and **0 ops/frame at rest
  while slots still exist** — the sharpest form of that test.
- **Fluid renders through the shipped raymarcher with no fluid-specific code**
  (§3.10) — verified by looking at the captures, not by counters.
- The EditMode suite is green at **249** tests, including 4 sloped-terrain
  regressions (§4.5), 4 tie-break-variance tests (§4.2), 4 slow-viscosity settle
  tests closing 5a §8.1's rest-detector blind spot, and 10 wake-queue tests from
  the edit-path work (`PHASE_5C_COMPLETION.md`).

### PERFORMANCE — PROVISIONAL, NOT XCODE-VERIFIED

Wall clock only (`Time.unscaledDeltaTime`), per AMENDMENT_8_9 §0 Rule 1's "no
Xcode, ever, for any performance measurement". `gpuFrameTime` is **deliberately
never read** — Amendment 8.10 measured it inflated ~2.6–2.7× on this hardware.
Per 8_9 §0 Rule 2 the sweep carries a `REPEAT_driftcheck`; `FrameTimingManager`
cannot report Performance State, so that is the only throttle evidence available.

| config | p50 | p99 |
|---|---|---|
| raymarch only (fluid CA off) | 0.84 | 2.56 |
| fluid CA only (raymarch off) | 0.80 | 2.17 |
| **GATE: fluid CA + raymarch** | **0.87–3.31** | **2.6–5.5** |
| REPEAT_driftcheck | 0.85–3.30 | 2.5–5.2 |

Driftcheck spread 0.4–5.5% across runs — within 10%, so decent (not conclusive)
evidence the machine was not mid-throttle. **These numbers cannot close §13's
performance gate.** They vary by ~4× across runs depending on how much fluid is
live, which is itself a reason not to lean on them.

### NOT TESTED AT ALL

- **§7.3's Metal claim-race verification.** `Phase5b_ClaimStress` is built and
  ready; it has **never been run**. Until it is, nothing here is evidence the
  plain-write claim is safe on this toolchain.
- **Xcode-verified GPU-lane timing**, deferred by decision (§9).
- ~~**The playable scene has not been played by a human.**~~ **RESOLVED, and it
  immediately paid for itself.** A human played it and found that every
  hand-placed fluid voxel froze in mid-air — a defect class every gate in 5a and
  5b was blind to, because the rigs use the opposite frame ordering to every
  shipping scene. See `PHASE_5C_COMPLETION.md`.
- **Pool exhaustion** (§13: "debug-shrink the GPU pool, breach it"). Not run on
  the GPU path. `AllocSlot` has the guarded no-op, untested here. **This became
  more important on Sept 4:** `AllocSlot` is a bump allocator with no free list,
  so slot indices are consumed by churn rather than by live fluid — measured at
  ~35 allocations per live voxel. Exhaustion is therefore reachable in ordinary
  play at §2.5's target scale, not only under a debug-shrunk pool. See
  `PHASE_5C_COMPLETION.md` §5 and `OPTIMIZATION_CANDIDATES.md` #7.
- **Streaming interaction.** The demo and rig use one static chunk. Fluid has
  never run while chunks stream in/out, and the CA's region is fixed at the
  origin — a moving active region is untested.
- **Honey on the GPU path.** Defined, never poured on the GPU path. It now
  settles in the CPU oracle (`FluidSlowViscositySettleTests`: interval 30,
  settles by tick 870, conserved, nothing floating) — the suite's first
  slow-viscosity rest coverage — but the GPU path has still never seen it.
- Everything in Phase 6/7.

---

## 3. Root causes found and fixed

Stated plainly, because each cost real time and each is a trap for the next
person.

### 3.1 Metal silently dropped `CSPromote`

```
Shader error in 'FluidCA': 'SetHome': output parameter 's' not completely
initialized at kernel CSPromote at FluidCA.compute(234) (on metal)
```

`CSPromote` built a `FluidSlot` by assigning two fields then calling
`SetHome(inout FluidSlot, …)`, which reads the whole struct through the `inout`.
Metal rejects it. **Unity dropped kernel index 1 and silently skipped every
dispatch** — no slots were ever allocated, so Intent and Commit had nothing to
do and the entire CA looked like it ran and did nothing. The runtime said only
`Kernel at index (1) is invalid`.

**`Assets/Editor/ShaderCompileCheck.cs` did not catch it** — it compiles for the
Editor's platform, not the standalone Metal target. Both rig scripts now grep
the build log for `Shader error` and fail, because **a dropped kernel does not
fail the build**. That gate immediately caught a second error (`undeclared
identifier '_MaxOpsPerFrame'`) before it could reach a run.

### 3.2 The GPU was reading an all-Air clipmap

`ChunkStore.SetVoxel` sets `chunk.dirty`, but `TerrainClipmap` keeps its **own**
`_dirtyChunks` set, fed only by `StreamManager.MarkDirty`. The Phase 5b basin
has no `StreamManager`, so `UploadDirty` early-returned on an empty dirty set
every frame. The CA was correct to refuse to promote anything —
`promote.rej_notmobile=47`. Fixed by marking dirty on every edit and, via
`FluidOpListReadback.OnVoxelApplied`, on every applied op.

### 3.3 The async readback failure — transport *shape*

The op-list was an `AppendStructuredBuffer` plus a small separate counter
buffer, needing two `AsyncGPUReadback` requests per frame. The **small** request
failed intermittently with `hasError` and no reason logged by Unity, while the
large op-list request beside it always succeeded. Five narrower hypotheses were
each tried and each failed to move it: in-flight cap, ring-buffering,
replacing `CopyCount` with a shader-maintained counter, cutting 4 requests to 2,
and back-pressure. It still failed when both requests read the **same** buffer,
which is what ruled out buffer aliasing.

**Fix:** one plain `RWStructuredBuffer` where element 0 is a header carrying the
op count (published by a single-thread `CSFinalize`) and elements 1.. are the
ops. One buffer, one request per frame. Readback errors went 15–24 per scenario
→ **0**.

**Honest limit:** I cannot explain *why* the small request specifically failed.
What is established is that the two-request shape was the trigger and the
single-buffer shape is reliable. Recorded as that, not as understood.

### 3.4 Conservation GAIN under async latency

With the readback working but three op-lists outstanding, `sand_column` reported
**GPU 25 vs CPU 24** — a gain of one grain. The CA decides moves from terrain
(§7.3's Air-Only rule), but its own commits only reach terrain after the CPU
applies the op-list and re-uploads. A second tick run before that lands decides
against terrain missing its own prior decisions, and commits the same source
twice.

**Fix:** `MaxFramesInFlight = 1` — one CA tick per **applied** op-list. This is
§3.9's frame order read literally. **It costs tick rate**: the CA now ticks once
per readback round-trip rather than once per frame. Raising it trades
conservation for throughput, which §7.3 does not permit.

### 3.5 Four rig defects that were producing false divergences

The rig was wrong more often than the CA was.

1. **The two sims were fed different inputs.** Source emission was gated on
   `Store.GetVoxel` alone, so the oracle received a drop whenever the *GPU's*
   source cell was free even if its own was not. Both must be clear now.
2. **Back-pressured frames run no tick but were counted as quiet.** `lava_vent`
   declared rest after 12 frames in which nothing had been asked to happen, then
   compared a sim that had not moved. It went from a vacuous "1 drop, 0 ops" to
   a real 62-drop run that conserves exactly.
3. **Scenarios that never reached rest were compared mid-motion**, which §7.8
   explicitly forbids. Not reaching rest is now recorded as a divergence.
4. **`place_block`/`mine_drop` picked their edit target from the GPU's state and
   applied it to both sims.** §7.2's readback lag means the two are legitimately
   at different points mid-flight, so the edit destroyed a drop in one and empty
   air in the other — measured as exactly mirror-image ±1 counts (GPU 150/CPU
   149 and GPU 149/CPU 150). The target must now be a cell both sims agree on.

### 3.6 Three demo defects found by looking, not by counting

1. The camera sat at negative X/Z — **outside the clipmap window**, where every
   ray starts out of bounds and is killed. That renders as a flat blue fill and
   reads as "the fluid is missing". The run reported "demo complete".
2. Water and lava sources were 32 voxels apart; the pools never touched and
   **obsidian stayed 0 for a whole run while the caption claimed a reaction**.
3. Material counts in the report were **sampled every other cell and scaled by
   4** — estimates presented as evidence. Now exact.

---

## 4. THE DIVERGENCE — ANSWERED BY EXPERIMENT

Two experiments were run to separate §7.8's sanctioned tie-break variance from a
real translation bug. They give different answers for different statistics, and
that distinction is the finding.

### 4.1 Is the GPU stable against itself? (5 repeats of `pour_water`)

```
run 0..4 conserved 150 every time (oracle 150 every time)
ticks to rest 235 / 232 / 250 / 212 / 225      <- not order-stable, per §7.8
worst per-layer delta, GPU vs its OWN other runs : 2
worst per-layer delta, CPU vs its OWN other runs : 0
worst per-layer delta, GPU vs CPU (same run)     : 2
```

The GPU differs from **itself** by exactly as much as it differs from the
oracle, while the CPU is bit-deterministic against itself.

### 4.2 Does permuting the oracle's OWN tie-break order move its answer?

`FluidReferenceCPU.TieBreakSalt` reorders equally-legal destinations without
changing which are legal (conserved count identical at every salt, asserted).
At the rig's exact geometry and pour:

```
per-layer occupancy   salt deltas 1,0,0,2,0   worst 2   -> LAYOUT, moves
surface height        2,2,2,2,2,2             stable    -> LEVEL, does not move
occupied-layer count  2,2,2,2,2,2             stable    -> LEVEL, does not move
full-layer count      0,0,0,0,0,0             stable    -> LEVEL, does not move
```

### 4.3 Conclusion, and what changed because of it

**Per-layer occupancy equality was the wrong assertion.** It moves by 2 under a
pure reordering of the oracle's own preferences, so it was never a property of
the rules — only of one arbitrary ordering. §7.8 says exactly this: *"identical
inputs give visually identical outcomes, not bit-identical frames... assert
steady-state invariants, never frame-exact positions."*

It was replaced with five checks, each an **exact equality or an exact zero** —
no tolerance was introduced:

1. conserved count — exact
2. full-layer set — exact ("settled-layer occupancy", forced by mass)
3. **floating drops — exact zero, both sides** — strictly NEW, see §4.5

**Surface height and occupied-layer count were on this list and were demoted to
REPORTED ONLY, on evidence, not to obtain a pass.** They were added here because
permuting the *oracle's* tie-break left them stable (2,2,2,2,2), which made them
look rule-determined. That perturbation was too weak: the GPU's concurrent claim
resolution reorders far more than reordering one slot's preference list.
Measured directly, five GPU repeats of the same scenario:

```
surface height per GPU run : 2,2,2,1,2      <- MOVES
surface height per CPU run : 2,2,2,2,2      <- stable
```

and across two consecutive sweeps the scenario failing this check *changed*
(`pour_water`, then `mine_drop`). A statistic that varies run-to-run on one
implementation cannot be required to match another exactly — that is layout, and
§7.8 says not to test it. Both are still **printed every run**, so a systematic
shift stays visible to a human. The rationale is duplicated at the assertion
site in `Phase5bValidationRig.cs`.

The per-layer spread is likewise printed and labelled "REPORTED ONLY, not
asserted".

### 4.4 What still diverges, and what it is — RESOLVED

**Superseded.** This section previously read: "`pour_water`: surface height GPU
y=1 vs oracle y=2 … this is **not** tie-break variance — 4.2 shows surface height
does not wobble under permutation … confirming or refuting this is the one
automated item blocking closure."

**That conclusion was wrong, and it was wrong because 4.2's perturbation was too
weak.** Permuting the oracle's tie-break moves one slot's preference list; the
GPU's concurrent claim resolution reorders far more than that. Measured directly
against the GPU rather than inferred from the oracle, surface height **does**
move run-to-run on the GPU (2,2,2,1,2) while staying stable on the CPU, and the
scenario that failed the check changed between sweeps. It is layout, it is
§7.8's stated freedom, and it is now REPORTED rather than asserted — see §4.3.

The rig matches on **5 of 5**. Nothing about `pour_water` is outstanding.

The lesson is worth more than the result: **an experiment that perturbs only the
reference implementation cannot establish what the other implementation
determines.** The same mistake in a different form is recorded in
`PHASE_5C_COMPLETION.md` §3.3, where a control that shifted tick phase by one
tick moved the settled layout in 13 of 14 cases.

### 4.5 A REAL BUG WAS FOUND AND FIXED ON THE WAY (both implementations)

The Playground dogfood scene put fluid on generated terrain for the first time
and a lava voxel appeared to hang in mid-air. Reproduced in the **oracle**, not
diagnosed in Playground:

```
Water_OnStaircase   1 voxel floating at int3(3, 3, 22)
Water_OnOverhang    1 voxel floating at int3(5, 2, 16)
```

**Root cause.** 5a §5.2's "only a DESCENDING move wakes" is right about the
destination and wrong about the source. A lateral move still *empties* the cell
it left, and anything above that cell gains a legal downward move. If it had
already slept, nothing told it, and it sat with Air beneath it forever. Flat
floors hide this entirely — a drop and the cell below it drain together — and
5a/5b only ever tested flat basins.

**Fix.** Vacating a cell always wakes what could descend into it, and *only*
that: the cell straight above plus the four down-diagonal sources. It never
wakes a lateral neighbour at the same level, which is what the shuffle-sustain
cycle needed, so it cannot reintroduce the never-resting surface.
`PartiallyFilledSurface_StillReachesRest` pins that and stays green.

Pinned by four new regression tests (`FluidSlopedTerrainTests`) on staircase and
overhang geometry — the CA's first non-flat test coverage.

## 5. §13 Phase 5b's acceptance assertions

| assertion | status |
|---|---|
| All of 5a's behavioural assertions as steady-state invariants | **MET** — conservation on all five; the §7.8-shaped steady-state set matches on **5 of 5**, floating drops zero on both sides (§4.3) |
| Live fluid visible while falling (authoritative-byte proof) | **MET** — see §6 |
| Pool exhaustion: distant drops freeze, zero errors, no device removal | **NOT TESTED** |
| Op-list genuinely bounded, correlates with changed cells | **MET** — 24–48 ops/frame vs 24–130 slots; 0 at rest |
| GPU-lane cost against the interim budget | **PROVISIONAL ONLY** (§2) |
| CPU-lane op-list-apply cost markedly lighter than 5a's full CA | **NOT MEASURED** |

---

## 6. The demo

`./run-phase5b-demo.sh` — release standalone, 11 screenshots at 1920×1080,
quits itself. Final run: water 645, sand 500, lava 73, **obsidian 5**, stone
67,918.

Verified **by opening the images**: three distinct falling streams (teal water,
olive lava, gold sand), water pooled and spread across the floor, a clean sand
cone at its angle of repose, and magenta obsidian voxels where water meets lava.
Stone floor, walls and ledges all rendered by the shipped raymarcher.

Colours are the shader's dev palette (`(mat-1)%8`), which `Content.cs` documents
as "explicitly not final art" — lava reads olive rather than orange.

**The playable build is the same binary.** Launch `Builds/Phase5bDemo.app` with
no arguments: WASD + right-mouse to fly, Q/E down/up, Shift to sprint.
`1` water, `2` sand, `3` lava, `4` place a 3×3×3 stone block at the crosshair,
`5` mine a 3×3×3, `0` stop all sources, `R` reset the world.

---

## 7. Deviations from the spec, logged

1. **§10.2's Xcode gate → in-engine wall clock.** Follows AMENDMENT_8_9 §0
   Rule 1's standing project rule; mitigated by a `REPEAT_driftcheck` per Rule 2.
   Every number is labelled PROVISIONAL in the rig's own output.
2. **A.9's `FluidWriteOp` extended from a single-cell write to a move record**
   (32 bytes: dst, material+expected, src, flags). A.9's single-cell form cannot
   survive an edit landing between a move being decided and applied, because the
   two halves are separate records. Ops are now re-validated against current
   terrain and applied both-or-neither.
3. **Reactions run in their own dispatch** rather than inside Intent. On the GPU,
   a reaction reserving a cell races a claim targeting it, and unlike the claim
   race that one is not safe — it breaks conservation. The dispatch boundary is
   the barrier.
4. **`CSWakeScan` wakes fluid neighbours on the GPU**, where §7.6 describes the
   *edit* path waking slots via CPU→GPU upload. Fluid waking its own neighbours
   is the CA's business and was latency-bound through the op-list.
5. **`MaxFramesInFlight = 1`**, against §7.2's "typically 1–3 frames". A
   correctness bound, not a tuning choice (§3.4).

---

## 8. Gaps carried forward

- ~~**`pour_water`'s surface-height difference (§4.4)**~~ — **RESOLVED.** It is
  layout, it moves run-to-run on the GPU, and §7.8 says not to assert it. The
  rig matches 5 of 5. See §4.4.
- ~~**The intermittent GPU floating drop**~~ — **the class is understood and
  fixed**, with slot-ownership evidence, in `PHASE_5C_COMPLETION.md` §4. Read
  §4.2 there for the part that is *not* established: the single pre-fix sighting
  recorded here cannot be attributed to that cause and is not claimed to be. If
  it recurs, capture `FluidGpuSimulation.ReadSlotAtCell` for the stranded cell
  first — owned vs unowned splits the diagnosis immediately.
- **This rig's own rest detector was not changed.** It still uses a 12-tick quiet
  window, which is the `PHASE_5A_COMPLETION.md` §8.1 blind-spot shape: a queued
  wake request produces no ops, so a not-yet-looked-at voxel can read as
  "floating". The 5c rig additionally requires `DeferredWakeRequests == 0`;
  porting that here would make this rig's rest measure strictly honest.
- **The Metal claim-race test has never been run.** Until it is, §7.3's
  plain-write claim is unverified on this toolchain, and the whole claim design
  rests on it.
- **Throughput cost of `MaxFramesInFlight = 1`** — the CA ticks once per readback
  round-trip. Unmeasured, and it will matter when fluid shares a frame with
  streaming.
- **Fixed active region at the origin.** The CA's region never moves. §7.4's
  near-player scope on a streaming world is untested.
- **Pool exhaustion, CPU-lane apply cost, honey** — all untested.
- **Phase 5a's `ChangedCellsThisTick` blind spot** for slow fluids (5a §8.1)
  still applies; the 5b rig works around it with a 12-tick quiet window.

---

## 9. Sign-off

**Phase 5b is not closeable. The automated side is clean; the blocking items are
the two manual gates.**

The GPU port runs, conserves mass exactly on all five scenarios, produces
obsidian, strands no fluid in mid-air, and is visibly correct in 3D through the
shipped raymarcher with no fluid-specific render code. Its steady state matches
the oracle on **5 of 5** scenarios under an assertion that was rebuilt from
measurement rather than assumption, and the edit path a player actually drives is
covered separately by `PHASE_5C_COMPLETION.md` (`PASS 170 FAIL 0`).

Blocking, and neither is automatable here:
1. The Metal claim-race test — **never run**; needs a human on this machine.
2. Xcode-verified timing — deferred by decision, needs a human.

Open behind those, and smaller — but open, so "closeable pending only the two
manual gates" would be an overstatement:
3. **Pool exhaustion** — a §13 acceptance assertion, still untested, and §5 of
   the 5c record raises its priority.
4. **CPU-lane op-list-apply cost** — a §13 acceptance assertion, never measured.

**What this does not license:**

- It does not say fluid is correct. Conservation is proven; distribution is not.
- It does not say the claim design is safe on Metal — **that test has not run**.
- It does not say fluid is fast. No Xcode-verified number exists, by decision.
- It does not say fluid works with streaming. It has only ever run on one
  static chunk.

**Frozen by this phase passing:** nothing yet — 5b has not passed.

**Suite at sign-off:**

```
EditMode      PASS 249  FAIL 0  SKIP 0
Phase 5b rig  5/5 MATCH, floating drops GPU 0 / CPU 0 on all five
Phase 5c rig  PASS 170  FAIL 0   (the edit path — see PHASE_5C_COMPLETION.md)
```

---

## 10. What changed after this document was first written (Sept 4, second update)

Recorded here rather than silently edited in, so the reasoning that was *wrong*
stays legible.

| what this document said | what is true now | where |
|---|---|---|
| steady state matches on **4 of 5**; `pour_water` blocks closure | matches on **5 of 5**; surface height is layout and is REPORTED, not asserted | §4.3, §4.4 |
| surface height is rule-determined, "does not wobble under permutation" | **wrong** — it moves run-to-run on the GPU (2,2,2,1,2). 4.2's perturbation only reordered the *oracle*, which cannot establish what the GPU determines | §4.4 |
| an intermittent GPU floating drop, unexplained | the class is understood, diagnosed with slot-ownership evidence, and fixed; the single pre-fix sighting is **not** claimed to be the same cause | `PHASE_5C_COMPLETION.md` §4 |
| the playable scene has not been played by a human | it has, and it found a defect class every gate here was blind to | §2, `PHASE_5C_COMPLETION.md` §0 |
| `AllocSlot` has a guarded no-op, untested | still untested, and now known to be a **bump allocator with no free list** (~35 allocations per live voxel), which makes exhaustion reachable in ordinary play at scale | §2, `PHASE_5C_COMPLETION.md` §5 |

**Not changed by any of this:** the Metal claim-race test has still never been
run, no Xcode-verified timing exists, fluid has still never run while chunks
stream, and §4.3's upload p99 is still red — `PHASE_5C_COMPLETION.md` §7 shows
this work neither caused nor fixed the current numbers.
