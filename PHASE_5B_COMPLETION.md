# Phase 5B Completion Record — Fluids: GPU Port (Shipped Path)

**Project:** Voxel Terraria 1 Byte BrickMap
**Spec:** ARCHITECTURE_v8.6.md §13 Phase 5b, §7.2, §7.3, §7.8, §8.4
**Date:** September 3, 2026
**Branch:** `phase5a-fluid-reference`
**EditMode suite:** `PASS 218  FAIL 0  SKIP 0`
**Validation rig:** 5 scenarios, conservation MATCHES on all five; final-level
distribution matches on 1 of 5 (see §4)
**Hardware:** Apple M1 Air (fanless, 8GB unified memory)
**Engine:** Unity 6000.3.10f1, release standalone

---

## PHASE 5B IS NOT CLOSEABLE. READ §4 AND §8 BEFORE TREATING FLUID AS DONE.

What works: the GPU CA runs, conserves mass exactly on every scenario, produces
obsidian, and is visible in 3D through the shipped raymarcher with no
fluid-specific render code. What does not: the water's *final distribution*
still differs from the CPU oracle on 4 of 5 scenarios, and whether that is
§7.8 tie-break nondeterminism or a translation bug **has not been determined**.
The assertion has not been loosened to hide it.

Two things §13 requires are also not done by this record: the Metal claim-race
verification (built, not run — it needs a human on this machine) and any
Xcode-verified timing. All timing here is **PROVISIONAL — NOT XCODE-VERIFIED**.

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
- The 218-test EditMode suite (Phase 5a's oracle) is untouched and green.

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
- **The playable scene has not been played by a human.** It builds and the demo
  mode of the same build runs; the flycam and keybinds are unexercised.
- **Pool exhaustion** (§13: "debug-shrink the GPU pool, breach it"). Not run on
  the GPU path. `AllocSlot` has the guarded no-op, untested here.
- **Streaming interaction.** The demo and rig use one static chunk. Fluid has
  never run while chunks stream in/out, and the CA's region is fixed at the
  origin — a moving active region is untested.
- **Honey.** Defined, never poured on the GPU path.
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

## 4. THE UNRESOLVED DIVERGENCE

Final validation sweep, async transport, all five at rest:

| scenario | conserved count | final levels (worst per-layer delta) |
|---|---|---|
| pour_water | 150 vs 150 **MATCH** | 10 — **DIVERGED** |
| sand_column | 24 vs 24 **MATCH** | 0 — **MATCH** |
| lava_vent | 62 vs 62 **MATCH** | 1 — **DIVERGED** |
| place_block | 150 vs 150 **MATCH** | 11 — **DIVERGED** |
| mine_drop | 150 vs 150 **MATCH** | 8 — **DIVERGED** |

**Mass is exact everywhere.** What differs is *where* the water ends up: roughly
10 of 150 drops settle one layer higher in one sim than the other. Both are
valid rest states — nothing is moving in either.

**What is known:** sand (no horizontals, §7.5) matches exactly. Lava (62 drops,
interval 6) is off by 1. Water (150 drops, horizontals enabled) is off by ~10.
The divergence scales with horizontal spreading, which is where claim
contention is highest.

**What is NOT known, and was not determined:** whether this is §7.8's sanctioned
tie-break nondeterminism (GPU claim ties resolve arbitrarily, CPU by slot order)
or a real translation difference in the sleep/wake timing. The experiments that
would settle it — running the same scenario repeatedly to see whether the GPU's
own result is stable, and permuting the CPU oracle's tie-break order — **were
not run.** That is the single highest-value next step.

**The assertion has not been loosened.** Per-layer delta must be 0 to pass. If
this turns out to be legitimate tie-break variance, the correct response is to
define the invariant §7.8 actually specifies (fill height / surface level), not
to widen a tolerance until the current numbers fit.

`CSWakeScan` was added during this investigation so fluid-internal waking
happens on the GPU in the same tick rather than round-tripping through the CPU
op-list. **It did not close the gap** (delta stayed ~10). It is kept on
correctness grounds — immediate wake matches the oracle's semantics — and
explicitly not as a fix.

---

## 5. §13 Phase 5b's acceptance assertions

| assertion | status |
|---|---|
| All of 5a's behavioural assertions as steady-state invariants | **PARTIAL** — conservation yes on all five; final levels only sand |
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

- **The final-level divergence (§4)** — the blocking item.
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

**Phase 5b is not closeable.** The GPU port runs, conserves mass exactly on all
five scenarios, produces obsidian, and is visibly correct in 3D through the
shipped raymarcher with no fluid-specific render code. But its steady state
matches the oracle's on 1 of 5 scenarios by the strict assertion, and the reason
is undetermined.

**What this does not license:**

- It does not say fluid is correct. Conservation is proven; distribution is not.
- It does not say the claim design is safe on Metal — **that test has not run**.
- It does not say fluid is fast. No Xcode-verified number exists, by decision.
- It does not say fluid works with streaming. It has only ever run on one
  static chunk.

**Frozen by this phase passing:** nothing yet — 5b has not passed.

**Suite at sign-off:**

```
PASS 218  FAIL 0  SKIP 0
```
