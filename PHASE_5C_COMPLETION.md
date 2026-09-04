# Phase 5C Completion Record — The Edit Path (Player-Driven Fluid)

**Project:** Voxel Terraria 1 Byte BrickMap
**Spec:** ARCHITECTURE_v8.6.md §8.3 (edit path), §7.6 (sleep/wake), §3.9 (CPU/GPU
sync contract), §7.8 (GPU determinism), §4.3 (upload budget)
**Date:** September 4, 2026
**EditMode suite:** `PASS 249  FAIL 0  SKIP 0`
**Phase 5c rig:** `PASS 170  FAIL 0` — 14 cases × 3 frame orderings, `RESULT: PASSED`
**Hardware:** Apple M1 Air (fanless, 8GB unified memory)
**Engine:** Unity 6000.3.10f1, release standalone

**Relationship to 5b:** this is not a new phase in §13. It is the record of a
defect class 5b could not have caught and of the rig built to close it. It
resolves 5b's `pour_water` item and its intermittent-floater item; see
`PHASE_5B_COMPLETION.md` §4.4 and §8, which now point here.

---

## 0. How this was found

**By a human clicking.** 5b §2's "NOT TESTED AT ALL" list said "the playable
scene has not been played by a human." It has now been, and the first thing that
happened was:

> "PLAcing any fluid manually via clicks makes it freeze in the air or no
> simulate"

followed by

> "Does this also take care of the issue where editing a block under water also
> doesnt make the water mov?"

Both were the same bug. Every automated gate in 5a and 5b was green at the time.
That is the finding worth keeping: **the rigs were all measuring a frame
ordering no shipping scene uses.**

---

## 1. The bug

Deterministic. **100% of hand-placed fluid**, in every real scene.

```
Playground.Update()                        <- the CA ticks HERE
  Edit(v, Water)
    ChunkStore.SetVoxel(v, Water)           CPU state: water. Authoritative.
    Clipmap.MarkDirty(chunk)                QUEUED for upload -- not uploaded
    EditService.NotifyEdited(v)             -> FluidGpuSimulation.RequestWake(v)
  FluidGpuSimulation.Tick(clipmap)
    CSPromote reads SampleVoxel(v)          <- reads the GPU CLIPMAP: still Air
    IsMobile(Air) == false                  -> no slot allocated
    _wakeCount = 0                          <- REQUEST DESTROYED, never retried

Phase4Bootstrapper.LateUpdate()             <- the upload happens HERE
  Clipmap.UploadDirty(...)                  the voxel reaches the GPU, too late
```

The voxel arrives on the GPU a frame later, is drawn, and sits there forever
with no slot and no pending request. Mass is never lost — §7.2 holds throughout —
the voxel is simply never *simulated*.

**Mining under settled fluid failed identically.** §8.3 ends an edit by waking
the 26-neighbourhood; those requests were destroyed the same way.

**Vents died from the same cause.** `Emit()` only refills a source cell it reads
as `Air`, so one frozen voxel at the source stopped the vent after a single
voxel — which looked like a separate bug and was not.

### Why every existing gate was green

`Phase5bBasin.Tick()` uploads **before** ticking, and says so on the line:

```csharp
Clipmap.UploadDirty(Store, _pool);      // GPU sees this frame's edits
Fluid.Tick(Clipmap);
```

The rig had the ordering right. **Nothing enforced that a caller must**, and
`Playground` + `Phase4Bootstrapper` do the opposite: tick in `Update`, upload in
`LateUpdate`. Unity guarantees `Update` runs first, so the shipping order was
inverted from the tested order and no test could see it.

---

## 2. The fix, and why it is not a redesign

**Not a redesign.** §3.9 already says the mirror lags authoritative CPU state.
§8.3 already ends an edit by waking slots. What was missing is that those two
facts *interact*: a wake must not be **evaluated** until the mirror has caught
up. `FluidWakeQueue` enforces exactly that and nothing else.

### 2.1 Rejected: the scene-level fix

Adding one `UploadDirty` before `Tick` in `Playground`, matching the rig, was
rejected for two reasons:

1. It spends §4.3's `MAX_CLIPMAP_UPLOAD_BYTES_PER_FRAME` **twice in one frame**.
   §0.2 forbids raising that cap; calling the budgeted path twice raises it in
   effect.
2. **It is still racy.** `UploadDirty` is budgeted — under a streaming backlog
   the edited chunk can be deferred behind others (`TerrainClipmap.cs:323`,
   "Deferred chunks stay in `_dirtyChunks` and upload on a later frame"), so the
   request would still be consumed against a stale mirror, just less often.
   Turning "always broken" into "intermittently broken" is worse than leaving it
   broken, because the next person sees a flake.

### 2.2 The upload-epoch mechanism

Readiness is **"has an upload happened since I asked"**, not "is the chunk clean
right now". A boolean cannot express the first, and the difference is
load-bearing — see 2.3.

`TerrainClipmap` gained two additive members (nothing existing reads them):

```csharp
public long UploadEpoch { get; }                  // ++ once per UploadDirty pass
public long LastUploadEpoch(int3 chunkCoord);     // -1 if never uploaded
```

A wake request is stamped with `UploadEpoch` **at the moment of the edit**, and
released when:

```
!IsDirty(chunk)                    // mirror already current: nothing to wait for
|| LastUploadEpoch(chunk) > stamp  // an upload after the edit, which must contain it
```

Any upload strictly after an edit necessarily contains that edit, because
`UploadDirty` copies the chunk's current CPU state.

**Compatibility is asserted, not assumed.** With a clean mirror — every rig,
every pre-existing test — requests release on the **same tick**, so the 5b
baseline stays comparable. `CleanMirror_ReleasesOnTheSameTick_SoRigBehaviourIsUnchanged`
pins it.

### 2.3 Two defects in the fix itself, both found by the rig

Recorded because both were caught by measurement rather than review, and both
would have been shipped by a plausible-looking implementation.

**(a) Starvation.** The first gate was `!IsDirty(chunk)`. A cell edited every
frame leaves its chunk dirty at *every* tick — it uploads *between* ticks, never
during one — so requests never came ready and escaped only via the 120-tick
stale timeout. The `hold_paint` case measured it exactly: **8 voxels placed
where 30 were asked, over 900 ticks**, and 900/120 = 7.5. Holding the place
button is the input that produces this, which is precisely how the original bug
was reported.

**(b) The epoch test alone was incomplete.** §8.3 wakes the edited cell's whole
26-neighbourhood, and those neighbours are routinely in a **different chunk that
was never edited** and so is never dirty — no upload ever advances their epoch,
so they waited out the full stale timeout. `large_pour` showed this as voxels
that looked permanently stranded but were merely 120 ticks from being looked at.
This is the source of §4's floater resolution below.

A third, smaller one: an unknown mirror epoch returned `-1`, which reads as
"already uploaded" and reintroduced the original freeze for the **first edit of
every scene** (`vent_sustained` placed 1 voxel instead of 40). The default is now
conservative — an unknown mirror errs toward waiting.

---

## 3. The Phase 5c rig

`./run-phase5c-rig.sh` — release standalone, quits itself, writes
`phase5c_report.txt` plus a screenshot.

### 3.1 Its central design

**Every case runs in both frame orderings, and the two must agree.**

| ordering | who does this | why it is in the rig |
|---|---|---|
| `MirrorFirst` | Phase 5a/5b rigs: upload, then tick | the ordering that was tested |
| `TickFirst` | Playground / `Phase4Bootstrapper`: tick in `Update`, upload in `LateUpdate` | **the ordering every real scene uses** |
| `MirrorFirstOffset` | *control*: `MirrorFirst` + one idle tick | isolates tick-phase from ordering |

Asserting only "fluid falls" would have passed **before** the fix, in
`MirrorFirst`. Ordering equivalence is the invariant the bug actually violated.

### 3.2 The 14 cases

`place_in_air`, `place_on_ground`, `mine_under_water`, `mine_under_sand`,
`mine_under_stack`, `mine_deep_column`, `wake_after_long_sleep`,
`place_solid_into_water`, `hold_paint`, `vent_sustained`, `lava_water_react`,
`large_pour`, `edit_far_from_fluid`, `edit_at_region_edge`.

Each asserts, per ordering: exact conserved counts; **zero floating mobile
voxels**; **that the disturbance actually made the CA do work** (`AppliedOps`
increased — the assertion the frozen-fluid bug needed and that no existing test
had, since a frozen voxel conserves perfectly and floats nowhere if you only
look at an already-settled basin); plus case-specific geometry.

### 3.3 What the control established, BEFORE any assertion was changed

The first version asserted that the **exact final layout** was identical across
orderings. It failed 12 of 13 cases while every behavioural assertion passed in
both. Rather than relax it on a hunch, the control answered it:

```
CONTROL -- MirrorFirst vs MirrorFirstOffset (SAME ordering, tick phase +1):
  => a one-tick phase shift alone moved the layout in 13/14 cases.
```

§7.4's tie-break hashes the tick number. A one-tick phase shift alone moves the
layout, so **layout equality across orderings is not a property the architecture
provides**, and asserting it was asserting a coincidence. This is the same trap
5b §4 fell into, answered the same way: experiment first, then rewrite the
assertion.

Replaced with the §7.8-shaped set — **conserved counts, floating count,
occupied-layer set**. Raw layout fingerprints are still **REPORTED** every run,
so a systematic shift stays visible to a human.

### 3.4 Three scenario defects of mine, fixed rather than asserted around

Logged because each produced a *plausible-looking failing test* that was wrong.

1. **The mining cases stood their voxel on a 1-wide pillar.** A liquid slides off
   a 1-wide pillar and lands on the floor, so those cases were measuring "water
   is a liquid" and failing their own precondition (settled at y=1, not y=5).
   They use a sealed shaft now, diagonals walled too, because §7.4's Intent
   hierarchy includes diagonal moves.
2. **`hold_paint` and `lava_water_react` fed the two orderings different input** —
   mass depended on drain rate, so the comparison was meaningless (30 vs 8
   voxels from one nominal input). Both are mass-budgeted now.
3. **The reaction assertion encoded a rule the engine does not have.** MEASURED:
   `lava + obsidian == 8` holds in every ordering, but water is **not** consumed
   one-for-one — 5 obsidian formed having consumed only 4 water, i.e. two lava
   reacted against the same water cell in one tick. Now asserted as an
   inequality, which is what is true.

### 3.5 Rest detection

`Settle()` originally required only "no ops applied", which has the **same blind
spot `PHASE_5A_COMPLETION.md` §8.1 flagged for viscosity**: a queued wake request
produces no ops, so the world reads as settled while work is pending. It now
also requires `DeferredWakeRequests == 0`. With that, zero stranded voxels
appear anywhere.

---

## 4. The intermittent GPU floating drop — what is now understood

`PHASE_5B_COMPLETION.md` carried an unexplained observation: one sweep reported
`floating drops GPU 1 / CPU 0` on `pour_water`, which did not reproduce.

### 4.1 What was established, with evidence

`large_pour` (220 voxels) reproduced a stranded voxel far more often than the 5b
sweep did, which made it diagnosable. A diagnostic was added **before** any fix,
because a stranded voxel has two causes needing opposite fixes:

```
note  slots: high-water 7551, EVER ALLOCATED 7551, capacity 65536
note  STRANDED 1 voxel(s); first at int3(24, 2, 26), material below = 0
note      its cell's owning slot = -1 -> unowned, so promotion was possible and did not happen.
```

- **Not slot exhaustion** — 7551 of 65536. The hypothesis was tested and refuted
  by measurement, not by argument.
- **Not the active radius** — `FLUID_ACTIVE_RADIUS_VOXELS` is 1280; the whole
  64³ basin qualifies.
- **Not a stuck slot** — the cell was **unowned** (`SlotAt == -1`). Both
  `CSPromote` and `CSWakeScan` skip owned cells, so an owned cell would have
  been unwakeable; an unowned one means promotion was *possible* and simply had
  not happened.

That left one explanation: **the voxel had not been looked at yet.** Cause 2.3(b)
— a wake request for a cell in a chunk that needs no upload waited out the full
`MaxWakeDeferTicks`, while the rest detector (3.5) declared rest because a queued
request produces no ops. The voxel was never stranded; the rig was measuring rest
too early and the queue was holding a request that had nothing to wait for.

With the predicate completed and the rest detector hardened:

- Phase 5c: **zero stranded voxels across all 42 scenario runs** (14 × 3).
- Phase 5b: **`floating drops GPU 0 / CPU 0` on all five scenarios**, including
  `pour_water` — the scenario the original observation was recorded against.

### 4.2 What is NOT established — read this before calling it closed

**The single pre-fix 5b observation cannot be attributed to this cause, and is
not claimed to be.** `FluidWakeQueue` did not exist when that floater was seen,
so the specific delay mechanism above cannot have caused it.

What the investigation established is the **mechanism class**: a voxel that is
unowned and merely not-yet-looked-at reads as "floating" if rest is measured by
ops alone — and the 5b rig measures rest with a 12-tick quiet window on exactly
that kind of signal, which is the §8.1 blind spot. So the original observation is
**consistent with** an early-rest artifact rather than a stranded voxel, but no
slot-ownership evidence was captured at the time and it did not reproduce.

Classified honestly:

- **PROVEN:** the floater class reproduced in 5c is understood, diagnosed with
  slot-ownership evidence, and fixed. It no longer occurs.
- **NOT PROVEN:** that the original 5b sighting was the same thing. It is
  consistent, unreproduced, and the 5b rig's own rest detector was **not**
  changed. If it recurs, capture `ReadSlotAtCell` for the stranded cell first —
  owned vs unowned splits the diagnosis immediately.

---

## 5. Scaling finding: `AllocSlot` never recycles (DISCLOSED, NOT ACTIONED)

Measured while chasing §4, kept because it is a real scaling limit and not a
bug today.

```hlsl
uint AllocSlot()
{
    uint idx;
    InterlockedAdd(Counters[1], 1u, idx);   // bump allocator. No free list.
    return idx;
}
```

`Counters[1]` only ever increases. Freed slots (`CSSweep` on sleep) are never
reused, so slot indices are consumed by *churn*, not by live fluid.

**Measured, 220-voxel pour:** 7,446–8,150 slots ever allocated against a capacity
of 65,536 — roughly **35 allocations per live voxel**. Fine at this scale.

**Why it matters:** §2.5's ~500,000 near-player active target would exhaust
65,536 indices long before reaching it. `CSPromote` handles exhaustion as §7.7's
guarded no-op — "retry next tick" — but **nothing re-requests**, and the counter
only grows, so in practice exhaustion means voxels silently stop being
simulated. That is the same *symptom* as the bug this document is about, from a
different cause.

**Not actioned tonight,** and deliberately: it is a change to the CA's allocator
under §7.3/§7.7, there is no evidence it bites at any scale currently tested, and
§2.5's target has never been run. Logged as candidate **#7** in
`OPTIMIZATION_CANDIDATES.md`. It also raises the priority of 5b's untested
"pool exhaustion" acceptance item (§13), because this makes exhaustion reachable
in ordinary play rather than only under a debug-shrunk pool.

---

## 6. Evidence classification

### CORRECTNESS PROVEN

- **The edit path wakes fluid in both frame orderings.** 14 cases × 3 orderings,
  `PASS 170 FAIL 0`, including placing in air, mining out from under settled
  water/sand/a stack/a 12-deep column, and mining under fluid asleep far past
  `FLUID_SLEEP_TICKS`.
- **Frame ordering is not observable** in conserved counts, floating count, or
  occupied-layer set. Exact layout is *not* asserted, on the evidence of 3.3.
- **Zero floating mobile voxels** at rest in every case, both implementations
  (5c GPU-vs-GPU across orderings; 5b GPU-vs-oracle, all five scenarios).
- **The wake queue's own rules**, in EditMode with no device: held while stale,
  released when current, ready/stale split in one tick, duplicate coalescing
  keeping the oldest stamp, bounded overflow, partial drain, stale release
  counted rather than silent, and the starvation case of 2.3(a).
- **Suite:** `PASS 249  FAIL 0  SKIP 0` (was 239).

### PERFORMANCE — PROVISIONAL, NOT XCODE-VERIFIED

- **The Phase 5c rig produces no timing numbers at all, deliberately.** It runs
  synchronous readback for determinism, which alone disqualifies any figure it
  could report.
- **The upload-epoch write's cost was measured, not assumed** — see §7. It is not
  measurable in §4.3's budget.
- Everything in `PHASE_5B_COMPLETION.md` §2 still stands and is unchanged by this
  work. No Xcode-verified number exists. `gpuFrameTime` remains inflated ~2.6–2.7×
  (Amendment 8.10) and is quoted nowhere.

### NOT TESTED AT ALL

- **§7.3's Metal claim-race verification** — `Phase5b_ClaimStress` still has
  **never been run**. Unchanged by this work, and untouched by it.
- **Xcode-verified timing** — deferred by decision.
- **Pool exhaustion** — still untested, and §5 raises its priority.
- **Streaming interaction** — 5c uses a fixed region at the origin like 5b.
  Fluid has still never run while chunks stream in and out.
- **Honey on the GPU path.** Honey now settles in the CPU oracle
  (`FluidSlowViscositySettleTests`, interval 30, settles by tick 870) but has
  still never been poured through the GPU CA.
- **A moving active region** (§7.4) — still unbuilt.

---

## 7. Phase 4 acceptance: the variance investigation

The Phase 4 acceptance rig came back **`PASS 47  FAIL 4`** against **`51 / 0`**
the previous night. The four failures are both halves of the §4.3 upload p99
(Gate C 1.884 ms, Gate E 1.552 ms, against the 1.0 ms budget).

**This was not assumed to be pre-existing.** `TerrainClipmap` is on the budgeted
upload path and it is the only file this work changed that Phase 4 uses (Phase 4
runs no fluid), so the epoch write was the only suspect. It A/Bs cleanly — the
full acceptance rig was re-run with `_lastUploadEpoch[chunkCoord] = _uploadEpoch;`
commented out:

| | staging p50 / p99 | Gate C upload p99 | result |
|---|---|---|---|
| with the epoch write | 0.07 / 0.91 ms | 1.884 ms | PASS 47 FAIL 4 |
| **without** it | 0.05 / 0.74 ms | **2.306 ms** | PASS 47 FAIL 4 |

The run **without** the change was slightly worse. The epoch write is one
`Dictionary<int3,long>` insert per uploaded chunk (`int3` implements
`IEquatable`, so it does not box) and is not measurable in the budget. The
measurement is recorded at the line in `TerrainClipmap.cs` so nobody re-derives
it.

**Conclusion: machine-state variance on an already-documented variable metric,
not a regression from this work.** CLAUDE.md already records this metric ranging
0.98–1.63 ms across runs and whole runs passing or failing on it. Nothing here
closes §4.3's upload p99 and nothing here caused it. Per AMENDMENT_8_9 §0 Rule 2,
this workflow cannot report Performance State, so thermal/throttle state is not
observable — which is exactly why the A/B, rather than a second sample, was the
right experiment.

---

## 8. What is closeable, plainly

**The automated side of Phase 5b is now clean.** Both items that blocked it are
resolved:

1. **`pour_water`'s steady-state divergence** — resolved by rebuilding the
   assertion on §7.8's actual guarantees (5b §4.3). The rig matches on **5 of 5**
   scenarios. Surface height and occupied-layer count are REPORTED, not asserted,
   because they were **measured** to vary run-to-run on the GPU (2,2,2,1,2) while
   stable on the CPU — a statistic that moves on one implementation cannot be
   required to match another exactly.
2. **The intermittent floating drop** — understood and fixed for the class
   reproduced here, with the honest limit stated in §4.2.

**Still required before Phase 5b can be called closed, and none of it is
automatable here:**

1. **The Metal claim-race test** (`Phase5b_ClaimStress`) — never run. Until it
   is, §7.3's plain-write claim is unverified on this toolchain and the whole
   claim design rests on it.
2. **Xcode-verified timing** — deferred by decision.

**Plus two §13 acceptance items that are neither manual gates nor resolved:**
**pool exhaustion** (untested, and §5 makes it more likely to matter) and
**CPU-lane op-list-apply cost** (never measured). These are smaller than the two
manual gates and are automatable, but they are open, and "closeable pending only
the two manual gates" would be an overstatement while they are.

So: **the two manual gates are the blocking items; two untested §13 assertions
remain open behind them.**

---

## 9. Sign-off

The edit path — the way a player actually touches fluid — is now covered by a
rig that runs every case in the frame ordering shipping scenes use, and it is
green. Three defects in the fix itself and three in the rig's own scenarios were
found by measurement and fixed rather than asserted around.

**What this does not license:**

- It does not say fluid is correct. Conservation and the §7.8 steady-state
  invariants are proven; **distribution is not, and §7.8 says not to test it.**
- It does not say the claim design is safe on Metal — **that test has not run.**
- It does not say fluid is fast. The 5c rig reports no timing at all, by design.
- It does not say fluid works with streaming. It has still only run on one
  static chunk with a fixed region.
- It does not close §4.3's upload p99. §7 shows this work did not cause the
  current numbers; it did not fix them either.

**Suite at sign-off:**

```
EditMode      PASS 249  FAIL 0  SKIP 0
Phase 5c rig  PASS 170  FAIL 0   RESULT: PASSED
Phase 5b rig  5/5 MATCH, floating drops GPU 0 / CPU 0 on all five
Phase 4 rig   PASS 47   FAIL 4   (§4.3 upload p99; see §7 — not caused here)
```
