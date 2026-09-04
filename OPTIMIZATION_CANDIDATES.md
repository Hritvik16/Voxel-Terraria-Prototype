# Optimization Candidates — ranked, with evidence

**Date:** September 4, 2026
**Status:** PROPOSALS AWAITING APPROVAL. **Nothing in this document has been
implemented.**

---

## Read this before acting on anything below

**We have no trustworthy performance data.** Constitution invariant #9 says "do
not optimize before profiling", and the profiling we can currently do is:

- `Time.unscaledDeltaTime` — real, but **PROVISIONAL — NOT XCODE-VERIFIED**, and
  it varies ~4× between runs of the same rig depending on how much fluid is live.
- `FrameTimingManager.gpuFrameTime` — **inflated ~2.6–2.7× on this hardware**
  (Amendment 8.10), and an earlier session used it to produce a retracted result.
  Never quoted here.
- Per-kernel GPU timing — **does not exist**. AMENDMENT_8_9 §0 Rule 1 rules out
  Xcode, and `FrameTimingManager` has no per-dispatch breakdown.

So this list is deliberately built from **algorithmic facts that are true
regardless of a profiler** — thread counts, buffer sizes, redundant work — not
from millisecond attribution. Each entry states what is *measured*, what is
*read from the code*, and what is *assumed*.

**Nothing here may be treated as "this is the slow part".** It is "this is work
we can prove is being done, that may not need doing."

### Rules this list obeys

- **No fluid-CA implementation until §4's correctness is closed** (queue item
  9c). The intermittent GPU floating drop recorded in `PHASE_5B_COMPLETION.md`
  means the CA is not yet established as correct, so every fluid entry below is
  a proposal only, regardless of how safe it looks.
- **No `EngineConfig` hard limit (§0.2) is raised** anywhere in this document.
  One entry *lowers* a buffer size, which §0.2's warning does not cover — it
  warns against raising ceilings to fix symptoms.
- **Nothing touches §0.3's review list**: `CoordMath`, memory layout or
  serialization, the §3.9 sync contract, generation determinism, or the
  streaming state machine.

---

## Ranked candidates

### 1. The CA dispatches over the ENTIRE region every tick, even at rest
**Evidence: read directly from `FluidGpuSimulation.Tick`. Not assumed.**

```
Dispatch(_kClear,    _regionCellCount);   // 131,072 threads (5b basin)
Dispatch(_kCommit,   _regionCellCount);   // 131,072
Dispatch(_kWakeScan, _regionCellCount);   // 131,072
Dispatch(_kReact,    _slotCapacity);      //  65,536
Dispatch(_kIntent,   _slotCapacity);      //  65,536
Dispatch(_kSweep,    _slotCapacity);      //  65,536
```

**~590,000 threads per tick regardless of activity.** The rig measures
`0 ops/frame at rest` with active slots present — i.e. the whole CA runs to
produce nothing, indefinitely, for as long as a basin sits still. In the
Playground's 128³ region that is **1,048,576 threads × 3 region passes**.

`CSWakeScan` is the most expensive of these per thread: up to **32 neighbour
`WakeMark` reads** (5 narrow + 27 broad) plus a `SampleVoxel` clipmap walk, for
every cell in the region, every tick.

**Proposed fix.** Skip the whole tick when there is provably nothing to do: no
awake slots and no pending wake requests. The CPU already knows the wake-request
count before dispatching, and a slot high-water of zero is already tracked in
`Counters[0]`. This is an early-out, not a redesign.

**What could break.** A tick skipped while a slot is still awake would freeze
fluid. The guard must be "no awake slots AND no wake requests", and the
awake-slot count currently requires a readback — so this likely needs a small
GPU-maintained "active slots" counter, which is additive.

**Verifiable tonight?** Partly. Correctness is covered by the existing sweep
(any wrongly-skipped tick shows as a scenario failing to reach rest). The *win*
is not measurable without per-kernel timing.

---

### 2. The op-list readback transfers 2 MB/frame to carry ~50 ops
**Evidence: measured. `maxOpsPerFrame = 65536`, `FluidWriteOp.SizeBytes = 32`.**

`65536 × 32 B = 2 MB`, read back **in full every frame**, because
`AsyncGPUReadback.Request(buffer)` transfers the whole buffer and the op count
is only known after it arrives (element 0 is the header).

Measured peak op counts across every sweep run tonight:

| scenario | peak ops/frame |
|---|---|
| pour_water | 55–60 |
| sand_column | 24 |
| lava_vent | 22–86 |
| place_block | 57–67 |
| mine_drop | 56–65 |

**Peak observed: 138.** The buffer is sized **475× the worst case ever seen.**

**Proposed fix.** Lower `maxOpsPerFrame` to 4096 (128 KB/frame, still ~30×
headroom over the worst observation). This is a **reduction**, so §0.2's
"never raise a hard limit" warning does not apply — but §0.2 does list
`MAX_FLUID_OPLIST_BYTES_PER_FRAME` as a real limit, so it deserves a stated
derivation rather than a guess.

**What could break.** Op-list overflow silently drops moves. The rig already
instruments `append overflow` and it has been **0 in every run**, so the check
exists; it must be confirmed still 0 after the change, at a heavier fluid load
than tonight's deliberately-tiny budgets.

**Verifiable tonight?** Yes — the sweep asserts conservation and reports
overflow. **But it is a fluid-path change, so 9c blocks it.**

---

### 3. `CSWakeScan`'s broad pass re-reads 27 neighbours to answer a 5-cell question
**Evidence: read from the shader. Not measured.**

The narrow pass (5 reads) and broad pass (27 reads) both run for every region
cell. The broad pass only matters for cells adjacent to a **descending** commit,
which is a small minority of cells in a settled basin.

**Proposed fix.** Have `CSCommit` write the broad mark into the neighbours
directly (27 writes per descending commit, of which there are few) instead of
having every cell in the region scan 27 neighbours looking for one. Trades a
region-wide gather for a sparse scatter.

**What could break.** Concurrent commits writing the same neighbour — but the
value written is a constant (2), so it is an identical-value race, the same
class §7.3 already relies on for claims. **It must NOT become an atomic.**

**Verifiable tonight?** Correctness yes (the sweep). Win, no.

---

### 4. `FluidReferenceCPU.CountMobileBytes` is O(all voxels) and runs per tick in tests
**Evidence: read from the code. Test-only path.**

The conservation ledger rescans the entire sandbox array every tick. For the
rig-shaped 64×32×64 oracle that is 131,072 byte tests **per tick**, and
`FluidTieBreakVarianceTests` runs it thousands of times.

**Deliberately NOT proposed for optimization.** Its comment says it is dumb on
purpose so it cannot share a bug with the ledger it audits (§0.1 invariant 9).
Listed here only so nobody "finds" it later and optimizes away the independence
that makes it an oracle. **The correct action is to leave it alone.**

---

### 5. `Phase5bBasin.CountMaterialWorld` / `LayerProfileWorld` walk the world per call
**Evidence: read from the code. Rig-only path.**

Each is a full 64×32×64 `ChunkStore.GetVoxel` walk, and the rig calls several per
scenario plus once per repeat. This is rig cost, not engine cost — it inflates
sweep wall-clock but nothing shipped.

**Proposed fix.** Single pass computing count and profile together. Low value,
low risk, purely a rig-runtime saving.

---

### 6. Region buffers are allocated for the whole region regardless of activity
**Evidence: read from the code.**

`_claim`, `_slotAt`, `_reacted`, `_wakeMark` are each `regionCellCount × 4 B`.
At the Playground's 128×64×128 that is **4 × 4 MB = 16 MB** of GPU memory for a
region that holds at most a few hundred active voxels.

**Not proposed as an optimization.** Flagged because it interacts with §11.3's
memory budget and with §7.4's *unbuilt* moving active radius — when that radius
is built, this sizing is the thing that decides whether it is affordable.
**A design question for the fluid scope, not an overnight change.**

---

## What I would do first, if approved

1. **#2 (op-list size)** — the clearest measured waste (475× oversized), the
   easiest to verify, and the existing overflow instrumentation already guards
   the failure mode. Blocked only by 9c.
2. **#1 (skip idle ticks)** — the largest algorithmic win, but needs an
   additive active-slot counter first.
3. **#3** — only after per-kernel timing exists, because it trades one cost for
   another and we cannot currently see which is bigger.

**#4 and #6 should be left alone**, for reasons stated above.

---

## What is still missing before any of this is worth doing

**Per-kernel GPU timing.** Every entry above is "work that is provably being
done", not "work that is provably expensive". Without per-dispatch attribution
we cannot tell whether `CSWakeScan` costs 10× `CSCommit` or 1/10th of it, and
picking between #1 and #3 needs exactly that.

The project's own rule (AMENDMENT_8_9 §0 Rule 1) rules out the usual tool. The
honest options are (a) accept relative A/B wall-clock with kernels selectively
disabled, which the validation rig's config sweep already demonstrates the
pattern for, or (b) revisit Rule 1 deliberately. Both are decisions for a human.
