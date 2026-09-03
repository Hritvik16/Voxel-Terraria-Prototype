# Phase 5A Completion Record — Fluids: CPU Reference (Correctness Oracle)

**Project:** Voxel Terraria 1 Byte BrickMap
**Spec:** ARCHITECTURE_v8.6.md §13 Phase 5a, §7.1–§7.8
**Date:** September 3, 2026
**Branch:** `phase5a-fluid-reference`
**Final tally:** EditMode `PASS 218  FAIL 0  SKIP 0` (190 pre-existing + 28 new)
**Acceptance rig:** 5 scenarios, 20/20 frames captured, 0 frames with a ledger
imbalance, 0 frames with duplicate slot ownership, all 5 `[replay matched]`
**Hardware:** Apple M1 Air (fanless, 8GB unified memory)
**Engine:** Unity 6000.3.10f1, release standalone (the rig), batchmode EditMode
(the tests)

---

## THIS FILE COVERS 5a ONLY. FLUID IS NOT "DONE".

**Phase 5b — the GPU port — has not started.** No file of it exists: there is
no `FluidCA.compute`, no `FluidOpListReadback.cs`, no wake-scan hook in
`EditService`, and the shipped raymarcher has no knowledge of fluid.

What this phase produced is the thing §7.2 calls *"the CPU version is what
tells you the GPU version is right"* — a correctness oracle. Nothing in this
document says fluid works in the game. It says the **rules** are correct on the
CPU, on a private sandbox array, where they can be breakpointed. §13's own
framing: *"5a is a correctness oracle; 5b is what ships."*

Anyone reading this later: if you need "does the player see water flow", that
is Phase 5b and it is untouched.

---

## 1. Scope (per §13 Phase 5a)

**The one new thing:** the fluid CA rules, proven correct on the CPU via
conservation unit tests.

**Files created, in §13's mandated order:**

1. `Assets/CoreEngine/Simulation/FluidLedger.cs` — the conservation counter.
   Written and proven **before** any fluid rule existed, per §13's build steps.
2. `Assets/CoreEngine/Simulation/FluidReferenceCPU.cs` — Clear → Intent+Claim →
   Commit, Air-Only, orphan self-free, sleep/wake, viscosity, reactions,
   near-player active radius.
3. `Assets/CoreEngine/Tests/FluidConservationTests.cs` — 28 tests, entirely
   against `FluidReferenceCPU`.
4. `Assets/Game/Phase5aBasin.cs` + `Assets/Scenes/Phase 5a Basin.unity` — §13's
   `Phase5a_Basin` scene and debug overlay.
5. `Assets/Game/Phase5aAcceptanceRig.cs` + `run-phase5a-rig.sh` — script-driven
   acceptance pass (not in §13; added because a manual click-through is not
   reviewable evidence).

**Supporting, additive:**

- `Assets/ContentModules/MaterialRules.cs` — A.7 flags, §7.4 tick intervals,
  §7.6 reaction pairs. One place, per §0.1 invariant 8.
- `Materials.Lava = 11`, `Honey = 12`, `Obsidian = 13` in `Content.cs`.
  Additive; nothing renumbered. **Deliberately excluded from
  `ContentVersionHash()`** — generation cannot emit them, so folding them in
  would invalidate every existing `world.meta` for a change that cannot alter a
  single generated voxel.
- `EngineConfig`: `MAX_ACTIVE_FLUID` (500,000, transcribed from §0.2, not
  measured), `FLUID_ACTIVE_RADIUS_VOXELS` (1280 = 128 m, assumption),
  `FLUID_SLEEP_TICKS` (8, assumption).

**Explicitly untouched, per §13's "5a needs only Phase 1 green":** `ChunkStore`,
`BrickDataPool`, `StreamManager`, `TerrainClipmap`, the delta system, eviction,
`IEditService`. The sandbox's own edit entry point is named `EditVoxel`, not
`SetVoxel`, specifically so it can never be confused with the shipped path.

---

## 2. Evidence classification

Kept in the three buckets CLAUDE.md requires, because blurring them is how a
phase gets called done that isn't.

### CORRECTNESS PROVEN

Proven by the EditMode suite (`./run-editmode-tests.sh`, `PASS 218 FAIL 0`) and
by the standalone acceptance rig. Every item below has a number attached in §3.

- Mass conservation under motion, asserted **every tick**, across every
  scenario in the suite — gravity, diagonals, horizontals, reactions, edits,
  pool exhaustion, forced demotion.
- The Air-Only claim rule (§7.3) and the single-writer Commit.
- Orphan self-free on edit (§7.3) — the "mine a falling drop" mechanism.
- Sleep and wake (§7.6), including that a settled pool holds zero slots.
- Viscosity intervals (§7.4): Water 1, Lava 6, Honey 30 — exact cadence.
- Falling-solid Intent restriction (§7.5) and the resulting angle of repose.
- Water + Lava → Obsidian (§7.6), both slots freed, mass booked as consumed.
- Determinism (§7.8) — bit-identical across two runs of the same scenario, and
  across a full rebuild (the rig's two-pass replay, and run-to-run).
- Slot-pool exhaustion is a guarded no-op (§7.7) — freezes fluid, never eats it.
- Forced demotion outside the active radius (§7.4/§7.7) — frees the slot, not
  the byte.
- Tie-break has no directional bias (§13's named failure signature).

### PERFORMANCE — NOT MEASURED, AND CORRECTLY SO

**No frame time was recorded in this phase, deliberately.** §13 Phase 5a:
*"This implementation does not need to be fast. Its only job is to be
unambiguously correct."* The reference is single-threaded, allocates scratch
arrays sized to the whole sandbox, and rescans every voxel to count mobile
bytes on demand — all of which would be indefensible in shipped code and are
fine here.

`Phase5aAcceptanceRig` prints no milliseconds on purpose. Per CLAUDE.md the
only trusted frame-time source is `run-acceptance-rig.sh`'s Phase 4 standalone
output, and a second rig printing ms would eventually be quoted as if it were
the same evidence. **Fluid's performance budget is a Phase 5b measurement**
(§13 5b: GPU-lane cost against the interim budget, CPU-lane op-list-apply cost).

### NOT TESTED AT ALL

Listed so nobody assumes coverage that does not exist.

- **A human in Play mode has never driven this scene.** The acceptance rig
  invokes the same delegates the buttons invoke (see §4), which covers the
  scenario code and the overlay's data — but **not** IMGUI layout, the two
  slice sliders, or the Pause / Step / Reset buttons. Those remain unexercised
  by anything.
- **Honey is defined but never poured.** Its interval is asserted in a unit
  test; no basin scenario and no rig capture uses it.
- **The Volatile brick flag (§3.10)** — dense bricks created by fluid entering
  uniform air, and their immediate free-list return. Not applicable to a
  standalone array with no brick pool; it is a 5b/Phase 6 concern.
- **Everything in §13 Phase 5b**, without exception.
- Reactions other than Water+Lava (`IsFlammable` fire, acid dissolve). The flag
  bits exist in `MaterialRules`; no materials use them and no test covers them.

---

## 3. The §13 acceptance assertions, with measured numbers

§13 Phase 5a lists five behavioural assertions. All five pass. Numbers are from
the final EditMode run and rig run `2026-09-03_160314`.

### 3.1 Conservation — the primary invariant — **MET**

*"total non-air fluid bytes constant absent reactions/edits, asserted every
tick"*

Asserted after **every tick** in every scenario, not sampled. Zero deltas
throughout. Representative counts: 8 drops in → 8 out over 64 ticks; 150 poured
→ 150 present over 300 ticks; 120 grains → 120 present; 250 poured → 250
present at rest in the rig.

The ledger is double-entry: mobile-byte count changes only via
`RecordExternalAdd` / `RecordExternalRemove` (edits) or `RecordReactionConsumed`
(§7.6). **Motion must change it by exactly zero.** On mismatch it reports the
tick and the signed delta — `Ledger_ReportsExactTickAndSignedDelta` asserts the
message contains both, so §13's *"we lost exactly N drops on tick M"* standard
is enforced by a test rather than by intent.

### 3.2 A column pours and settles flat; occupancy → 0 within N ticks — **MET**

*N = 400 budgeted and justified in the test; **actual rest at tick 24**.*

16-drop column into a 4×4 enclosed basin = exactly one full layer. At rest:
every interior cell of y=1 is water, nothing above it, `ChangedCellsThisTick`
0, **`ActiveSlotCount` 0** — a settled pool holds no slots (§7.6).

The awkward case is covered separately: `PartiallyFilledSurface_StillReachesRest`
pours 20 drops into a 36-cell layer, so surface drops always have an adjacent
air cell to shuffle into. **Rest at tick 29, 0 slots.** See §5 for why this
needed a deliberate reading of §7.6.

### 3.3 Place a block into a falling stream, repeatedly — **MET**

90 ticks, stone slammed into the stream every 7th tick and mined out two ticks
later, in a walled shaft so the stream stays a stream at every motion tier.
Ledger clean **every tick**. Final count exactly `poured − overwritten`.

Rig confirmation: stone placed at `int3(21, 19, 32)` directly under a falling
drop; 250 poured, 250 present at rest, delta 0 at all four captured frames.

### 3.4 Mine a falling drop → orphan self-frees next tick — **MET**

Measured, and this is the §7.3 mechanism working exactly as specified:

- Immediately after the edit: `ActiveSlotCount` **unchanged** (1 → 1). The
  orphan is *not* freed eagerly by the edit.
- One tick later: `OrphansFreedThisTick` **1**, `ActiveSlotCount` **0**,
  mined drop gone from the array, ledger balanced.
- Then 10 further ticks: `OrphansFreedThisTick` **0** every tick — the orphan
  counter returns to 0 at steady state.

Rig confirmation: `mined material 6 at int3(21, 20, 32)`, ledger delta 0 at all
four frames.

### 3.5 Sand piles at ~45°; Water+Lava → Obsidian, both slots freed — **MET**

**Sand repose: apexY 5, base half-width 5, slope 38.7°, at rest by tick 148**
for 120 grains. Asserted in a 30–60° band. **The honest number is 38.7°, not
45** — the base layer spreads one cell past the ideal cone. §7.5 says "~45°";
this is that, approximately, and the exact figure is recorded rather than
rounded toward the spec.

`FallingSolids_DoNotMoveHorizontally` separately pins §7.5's Intent restriction:
a grain on a flat floor with the horizontal tier enabled *for fluids* still
refuses to slide sideways.

**Water+Lava:** 1 reaction on tick 1; afterwards 1 obsidian, 0 lava, 0 water, 0
mobile bytes, and **2 bytes booked as `ReactionConsumed`** — consumed, not lost,
which is the distinction the ledger exists to make. Both slots free by tick 2:
the slot that ran the neighbour scan frees inside Intent, its counterpart on its
own next Intent via the orphan check. That asymmetry is §7.3's mechanism, not a
bug.

`LavaFallingIntoWater_Reacts_AndConservesThroughout` covers the dynamic case —
lava falling into a full water layer, 120 ticks, conservation asserted every
tick, exactly one obsidian produced.

### 3.6 Additional assertions beyond §13's list

- **Determinism (§7.8):** two sandboxes, same scenario, bit-identical across all
  32,768 cells. The rig's two-pass replay matched pass 1's rest tick in all 5
  scenarios, and rest ticks were identical across a full delete-and-rebuild.
- **Tie-break bias (§13 failure signature):** 100 independent drops, each on its
  own 1-cell peak with four equally legal down-diagonals. Distribution
  **+X=22 −X=36 +Z=19 −Z=23**, 0 unresolved. No systematic drift.
- **Viscosity cadence (§7.4):** first move on tick **1 / 6 / 30** for Water /
  Lava / Honey, then exactly 10 further moves in `interval × 10` ticks.
  `Viscosity_DoesNotAgeAGatedSlotTowardSleep` pins that a gated slot does not
  age toward sleep while waiting — without it, honey (interval 30) would be
  freed by `FLUID_SLEEP_TICKS` (8) before ever taking a turn, and would be
  immobile rather than slow.
- **Pool exhaustion (§7.7):** cap 4 against 8 drops — promotions refused, no
  throw, 8 bytes still present after 40 ticks.
- **Forced demotion (§7.4):** distant drop demoted on tick 1, byte frozen
  mid-air exactly where it was, 2 mobile bytes still present after 30 ticks.

---

## 4. The acceptance rig — final run, verbatim

`./run-phase5a-rig.sh` builds a **release standalone** (§10.2 — the Editor is
not the reference environment for "does this work" any more than for timing),
launches it, drives all five scenarios, captures four frames each, quits itself,
and opens the output folder.

**It drives the buttons, not the simulation.** `Phase5aBasin` exposes one
`Scenarios` table of `(Id, Label, Action)`; `OnGUI` renders a button per entry
and the rig invokes *the same entry's delegate*. There is no second copy of the
mapping for the two to disagree about — if the rig named its own methods, a
button wired to the wrong handler would still produce a clean rig run, which is
the exact class of bug the manual click-through existed to catch.

Two passes per scenario: pass 1 finds the rest tick, pass 2 rebuilds and
replays the identical tick sequence capturing at 0 / 25 / 50 / 100%. Sound only
because the reference is deterministic (§7.8) — and the rig *asserts* the replay
lands on the same tick rather than assuming it.

Run `2026-09-03_160314`, verbatim:

```
Phase 5a Basin — acceptance rig
run          2026-09-03_160314
build        release standalone, OSXPlayer
screen       1600x900
colour space Linear (Air palette 18,18,24 -> stored texel 2,2,2)
rest rule    12 consecutive ticks with 0 changed cells (cap 1500)
scenarios    driven via Phase5aBasin.Scenarios[i].Run — the same delegate the buttons use

---------------- pour_water  ("Pour water") ----------------
pass 1: rest at tick 302
overlay action line: pouring water at int3(21, 30, 32) (250 drops)
pass 2: ended at tick 302 (pass 1 rest was 302)  [replay matched]
  start  tick     0  slots     0  changed     0  dupOwn 0  ledger OK (exp 0 / act 0 / delta 0)  -> pour_water_tick0000_start.png
  25pct  tick    75  slots    48  changed    80  dupOwn 0  ledger OK (exp 75 / act 75 / delta 0)  -> pour_water_tick0075_25pct.png
  50pct  tick   151  slots    71  changed   104  dupOwn 0  ledger OK (exp 151 / act 151 / delta 0)  -> pour_water_tick0151_50pct.png
  rest   tick   302  slots     0  changed     0  dupOwn 0  ledger OK (exp 250 / act 250 / delta 0)  -> pour_water_tick0302_rest.png

---------------- sand_column  ("Drop sand column") ----------------
pass 1: rest at tick 29
overlay action line: dropped a 24-grain sand column at x=32 z=32
pass 2: ended at tick 29 (pass 1 rest was 29)  [replay matched]
  start  tick     0  slots    24  changed     0  dupOwn 0  ledger OK (exp 24 / act 24 / delta 0)  -> sand_column_tick0000_start.png
  25pct  tick     7  slots    24  changed    46  dupOwn 0  ledger OK (exp 24 / act 24 / delta 0)  -> sand_column_tick0007_25pct.png
  50pct  tick    14  slots    23  changed    32  dupOwn 0  ledger OK (exp 24 / act 24 / delta 0)  -> sand_column_tick0014_50pct.png
  rest   tick    29  slots    18  changed     0  dupOwn 0  ledger OK (exp 24 / act 24 / delta 0)  -> sand_column_tick0029_rest.png

---------------- lava_vent  ("Lava vent") ----------------
pass 1: rest at tick 637
overlay action line: lava vent open at int3(42, 30, 32) (ticks every 6 -- §7.4)
pass 2: ended at tick 637 (pass 1 rest was 637)  [replay matched]
  start  tick     0  slots     0  changed     0  dupOwn 0  ledger OK (exp 0 / act 0 / delta 0)  -> lava_vent_tick0000_start.png
  25pct  tick   159  slots    27  changed     0  dupOwn 0  ledger OK (exp 27 / act 27 / delta 0)  -> lava_vent_tick0159_25pct.png
  50pct  tick   318  slots    40  changed    78  dupOwn 0  ledger OK (exp 53 / act 53 / delta 0)  -> lava_vent_tick0318_50pct.png
  rest   tick   637  slots     0  changed     0  dupOwn 0  ledger OK (exp 62 / act 62 / delta 0)  -> lava_vent_tick0637_rest.png

---------------- place_block  ("Place block into stream") ----------------
  setup: ran "Pour water" for 10 ticks first so there is a stream to act on
pass 1: rest at tick 295
  setup: ran "Pour water" for 10 ticks first so there is a stream to act on
overlay action line: placed stone at int3(21, 19, 32), directly under a falling drop
pass 2: ended at tick 295 (pass 1 rest was 295)  [replay matched]
  start  tick    10  slots    10  changed    20  dupOwn 0  ledger OK (exp 10 / act 10 / delta 0)  -> place_block_tick0010_start.png
  25pct  tick    73  slots    42  changed    76  dupOwn 0  ledger OK (exp 73 / act 73 / delta 0)  -> place_block_tick0073_25pct.png
  50pct  tick   147  slots    73  changed    86  dupOwn 0  ledger OK (exp 147 / act 147 / delta 0)  -> place_block_tick0147_50pct.png
  rest   tick   295  slots     0  changed     0  dupOwn 0  ledger OK (exp 250 / act 250 / delta 0)  -> place_block_tick0295_rest.png

---------------- mine_drop  ("Mine a falling drop") ----------------
  setup: ran "Pour water" for 10 ticks first so there is a stream to act on
pass 1: rest at tick 290
  setup: ran "Pour water" for 10 ticks first so there is a stream to act on
overlay action line: mined material 6 at int3(21, 20, 32) -- watch orphans-freed go to 1 next tick
pass 2: ended at tick 290 (pass 1 rest was 290)  [replay matched]
  start  tick    10  slots    10  changed    20  dupOwn 0  ledger OK (exp 9 / act 9 / delta 0)  -> mine_drop_tick0010_start.png
  25pct  tick    72  slots    45  changed    72  dupOwn 0  ledger OK (exp 71 / act 71 / delta 0)  -> mine_drop_tick0072_25pct.png
  50pct  tick   145  slots    73  changed    92  dupOwn 0  ledger OK (exp 144 / act 144 / delta 0)  -> mine_drop_tick0145_50pct.png
  rest   tick   290  slots     0  changed     0  dupOwn 0  ledger OK (exp 249 / act 249 / delta 0)  -> mine_drop_tick0290_rest.png


================ SUMMARY ================
scenarios driven                5
frames captured                 20 (expected 20)
frames with ledger NOT balanced 0
frames with duplicate ownership 0
RESULT: every captured frame balanced, zero duplicate ownership.
```

**Reading the numbers correctly, two places it would be easy to misread:**

- `sand_column` shows **18 slots at rest**. That is not a leak. The rig captures
  at the *first quiet tick* — the moment motion stops — and sleep takes
  `FLUID_SLEEP_TICKS` (8) further ticks of aging. Sand stops because it is
  *blocked* (§7.5 gives it no horizontal move), so its slots are still counting
  down. Water scenarios show 0 slots at rest because for water, sleeping is
  *what ends* the motion.
- `lava_vent` shows a 30-cell column spanning vent to floor at 50%. Also not a
  hang: lava's interval is 6 and the drop falls 30 cells, so one drop takes
  30 × 6 = **180 ticks** to land. The column is the stream, still in transit.

---

## 5. Two deliberate readings of the spec

Both are flagged in code at the point they matter, not buried here. **5b must
port these readings, not the literal sentences**, or its steady state will not
match this oracle's.

### 5.1 §7.6's "no-move counter" is implemented as "no *downward* move"

Taken literally — any move resets the counter — a drop on a partly-filled pool
surface always has an adjacent air cell at its own level, shuffles sideways
forever, and the basin never reaches rest. §13's *"occupancy → 0 within N ticks
of rest"* becomes unsatisfiable for any pour that does not happen to complete
its top layer exactly.

Measured before the change: **5–7 lateral moves per tick still at tick 2000.**
After: **rest at tick 16**, 0 slots.

### 5.2 Only a *descending* move wakes the neighbourhood

The first attempt at 5.1 was not enough on its own. With lateral moves also
waking, the cycle is exact: a drop shuffles sideways, ages, sleeps — and a
*neighbour's* lateral shuffle re-promotes it with a fresh counter, so the sleep
counter can never win. A descending move genuinely changes the local fluid
level; a lateral shuffle across a flat surface changes nothing for a neighbour
to reconsider. Spread still works because every drop that falls in, and every
drop that descends off a stack, hands its neighbourhood a fresh lateral budget.

**This was diagnosed by measurement, not by reasoning** — a trace showing the
non-convergence at tick 2000 is what identified the cycle.

---

## 6. The `_slotAt` ownership guard — proven unreachable, retained anyway

`FluidReferenceCPU.IntentPass` contains a guard, `if (_slotAt[home] != s)`, whose
original comment claimed it closed a live duplication hole: *slot A orphaned out
of cell C, a different same-material slot B moves into C, A's cached-material
comparison spuriously matches again, two slots own one cell, and committing both
reads as a conservation GAIN.*

**That claim was false for the current implementation, and it had never been
tested when it was written.**

**How it was established — by experiment, not argument:**

1. Two tests were written to construct the scenario:
   `OrphanedSlot_ThenDifferentSlotMovesIntoSameCell_NoDuplicateOwnership` (the
   staged case, in a 1-wide walled shaft so the colliding drop's *only* legal
   destination is the vacated cell), and
   `DuplicateOwnershipHunt_EveryOrphanRefillTiming` (a 40-combination sweep:
   gap 1–4 cells × delay 0–4 ticks × {leave Air, refill with the same
   material}). Both assert the ledger *and* `CountDuplicateSlotOwnership()`
   on every tick.
2. The guard was commented out. Full suite: **PASS, 0 failures.** No
   conservation gain, nothing to report.
3. The guard was replaced with a hard throw:
   `if (_slotAt[home] != s) throw new InvalidOperationException(...)`. Full
   suite: **nothing thrown, across all tests** — including the 600-tick
   randomised chaos test that produces 116 orphan frees.

**Why it cannot fire here.** For a live slot A homed at C to find
`_slotAt[C] != A`, another slot must take C, and only two places assign
`_slotAt`:

- **`CommitPass` moving into C** requires `_voxels[C] == Air`. But if C is Air
  while A is live caching a non-Air material, A's byte comparison mismatches —
  and Intent runs that comparison for *every live slot, every tick, with nothing
  able to `continue` past it, strictly before `CommitPass`.* A is always freed
  first.
- **`TryPromote(C)`** requires `_slotAt[C] == NONE`. `FreeSlot` clears that entry
  only when the departing slot is the recorded owner, and `CommitPass` writes
  `_slotAt[home] = NONE`, `_slotAt[d] = s` and `SetHome` as consecutive
  statements *before* any `WakeNeighbourhood` call — so there is no window in
  which a live slot's home carries a NONE entry.

So §7.3's byte comparison is sufficient on its own, exactly as the spec says.

**RETAINED as defence-in-depth**, not deleted. §7.3 offers an optional
brick-granularity red-black `IJobParallelFor` decomposition for a parallelised
CPU reference, and 5b's GPU port has genuinely non-deterministic ordering
(§7.8). Both reintroduce the ordering freedom that makes this reachable. The
`_slotAt` field's *promotion-dedupe* role is load-bearing today regardless.

**The claim is now checked automatically rather than by inspection.** The test
fixture's `[TearDown]` asserts `OwnershipGuardFiredTotal == 0` and
`CountDuplicateSlotOwnership() == 0` on every sandbox any test built, after
every test. It is deliberately a `[TearDown]` and not a `[OneTimeTearDown]`:
NUnit reports the latter at fixture level, and `run-editmode-tests.sh` tallies
`test-case` elements, so it could fail without changing `FAIL 0`.

---

## 7. Two rendering defects found by the rig, and fixed

Both were **presentation only** — no simulation code was touched, and all 20
frames still report ledger OK / dupOwn 0 on identical rest ticks, which is what
demonstrates the fix disturbed nothing.

Worth noting *how* they were found: **neither was visible in an earlier
Editor-side texture dump**, which bypassed the draw path and did no colour
conversion. That is the case for §10.2 applying to correctness runs and not just
to timing.

### 7.1 The side view rendered upside down — root cause: double compensation

`DrawSlice` wrapped the draw in `GUIUtility.ScaleAroundPivot(new Vector2(1,-1),
r.center)`, justified as *"textures are bottom-up and the GUI is top-down"*.

**The actual bug: `GUI.DrawTexture` already performs that mapping** — it draws
the texture's top row at the rect's top edge. `SetPixels32` is bottom-up, so
`_sidePixels[y*sx+x] = voxel(x,y,z)` correctly puts world y=0 in the texture's
bottom row, and `DrawTexture` presents it upright unaided. The manual flip was a
*second* correction applied to an already-correct image, and it inverted the
result: the stone floor drew along the top edge, a settled sand pile hung from
it apex-down, and the caption still claimed "up is +Y".

**The pixel indexing was never wrong.** Deleting the flip was the entire fix.
Verified by probe: panel top rows read air, bottom rows read sand; the pile now
rests on the floor with its apex up. Removing the shared flip also reoriented
the top view, whose caption now correctly reads "up is +Z".

### 7.2 Palette washed out — root cause: an uncancelled sRGB encode

Air `(18,18,24)` rendered as `(75,75,86)`, stone `(104,104,112)` as
`(171,171,177)`, water `(48,122,224)` as `(120,184,241)` — each exactly
`linearToSRGB(intended)`. Air arriving as mid-grey is why the basin interior
read as solid stone in the first captures.

**Measured model** (three rig runs to pin, and worth writing down because it is
not guessable from the code): **the IMGUI draw + `ScreenCapture` path applies
`linearToSRGB` twice, and an sRGB-flagged texture cancels one of them in the
hardware sample.** That single model reproduces every measurement exactly —
including why the *first* attempted fix produced **bit-identical pixels**:
storing `f⁻¹(p)` in a linear float texture yields `f(f(f⁻¹(p))) = f(p)`, the
original broken value. Identical output from two very different inputs is what
cracked it.

**Fix:** keep the texture `RGBA32` and sRGB-flagged (`linear: false` is now
passed explicitly, because it is load-bearing) and store the **single** inverse
of the palette.

Measured on the regenerated screenshots:

| material | intended | measured | error |
|---|---|---|---|
| Stone | (104,104,112) | (104,104,112) | 0 — exact |
| Sand | (206,184,126) | (206,184,126) | 0 — exact |
| Water | (48,122,224) | (49,123,224) | 1/255 |
| Lava | (226,88,24) | (226,88,0) | blue clamps |
| Air | (18,18,24) | (0,0,0) | clamps |

**Disclosed residual, not fixed:** the two darkest channel values clamp to zero
against an 8-bit linear intermediate downstream of the texture. A linear-float
texture with the double inverse was tried — exact on paper — and measured
*worse* (Stone and Sand exact, but Air and Lava's blue → 0), because extra
source precision cannot fix a crush that happens downstream. Air rendering pure
black rather than near-black is the intended reading of empty space, and is
darker than the page background behind it, so the panel edge stays legible. The
reported defect — washing *out* — is gone.

---

## 8. Known gaps carried forward (disclosed, not blocking 5a)

### 8.1 `ChangedCellsThisTick` reads "at rest" for slow-viscosity fluids — NOT FIXED

`ChangedCellsThisTick == 0` counts *change*, not *quiescence*. §7.4's viscosity
intervals mean a material can be legitimately idle while very much in motion:
**lava evaluates one tick in 6, so five of every six ticks report zero** while a
lava stream is visibly falling; honey would be 29 of every 30.

Observed directly: the basin overlay printed `<at rest>` and a first-rest tick
of **317** during a window in which a lava column was demonstrably still
descending — a drop needs 30 × 6 = 180 ticks to fall the basin's height.

**Nothing currently tested is affected.** Every rest-based assertion in the suite
uses Water or Sand, both tick-interval 1; this was checked, not assumed. The
acceptance rig uses a 12-tick quiet window, which clears lava's 6.

**Deliberately not fixed**, and annotated in both places — on
`FluidReferenceCPU.ChangedCellsThisTick` (the metric) and on the tests'
`TickToRest` (its consumer). Changing the metric now would change what the green
tests mean. **Whoever writes a lava or honey settle-time test:** either size the
quiet window above that material's `MaterialRules.TickInterval`, or assert on
`ActiveSlotCount == 0` instead, which has no such blind spot because a
viscosity-gated slot is still an allocated slot.

### 8.2 Sand's angle of repose is 38.7°, not 45°

§7.5 says "~45°". Measured 38.7° (apexY 5, base half-width 5) — the base layer
spreads one cell past the ideal cone. Asserted in a 30–60° band. Recorded rather
than rounded toward the spec; tighten the rule only if the look is wrong in
play, which is a Phase 6 judgement.

### 8.3 A scene-generation race, fixed, but flagged for 5b's rig

`Phase5aSceneBuilder` originally relied on `AddComponent` picking up the C#
field initializers on `Phase5aBasin`. Under `-executeMethod` immediately after a
script edit it does not: one regeneration wrote `_bricksX/_bricksY/_bricksZ` all
`0`, and every scenario in the resulting standalone died with
`ArgumentException: bricksX must be a positive power of two (got 0)`.

Fixed — every serialized value is now written explicitly through
`SerializedObject`, and an unknown field name throws so a rename fails the
generator loudly instead of leaving a zero behind. **Flagged here because 5b's
acceptance rig will regenerate a scene the same way**: a generator whose output
depends on compile timing is not a generator.

### 8.4 The rig could report success on zero evidence — fixed

The same failed run reported `RESULT: every captured frame balanced, zero
duplicate ownership` off **zero captured frames** — vacuously true of no frames.
The rig now asserts the expected frame count (`scenarios × 4`), reports
`RESULT: FAILED` when short, exits non-zero, and `run-phase5a-rig.sh` propagates
that. **Any rig that can pass on an absence of evidence is worse than no rig.**

### 8.5 Assumptions on loan

`FLUID_SLEEP_TICKS` (8), `FLUID_ACTIVE_RADIUS_VOXELS` (1280) and
`MAX_ACTIVE_FLUID` (500,000) are all marked ASSUMPTION or SPEC-MANDATED in
`EngineConfig`, none measured. §0.2's own "raise only if" for `MAX_ACTIVE_FLUID`
names a Phase 5 measurement — that is a **5b** measurement, not this phase's.

---

## 9. Sign-off

**Phase 5a is closeable.** All five of §13's acceptance assertions are met with
measured numbers, the EditMode suite is `PASS 218 FAIL 0 SKIP 0`, and the
standalone acceptance rig captures 20/20 frames with zero ledger imbalances and
zero duplicate slot ownership across all five scenarios.

**What this does NOT license:**

- It does not say fluid works in the game. **Phase 5b has not started.**
- It does not say fluid is fast. **No frame time was measured**, correctly —
  §13 says this implementation does not need to be fast, and its performance
  question belongs to 5b.
- It does not say the basin scene has been used by a human. **Play mode remains
  unexercised**; the rig covers the scenario handlers and the overlay's data,
  not IMGUI layout, the sliders, or Pause/Step/Reset.

**What 5b inherits.** A deterministic oracle (§7.8) it can diff against
step-by-step, the way Phase 2's raymarcher was debugged against
`RaymarchReference`; a conservation ledger that names the tick and the signed
delta on any loss; and two readings of §7.6 (§5 above) that must be ported as
*behaviour*, not as the literal sentences, or the GPU steady state will not
match this oracle's.

**Per §0.1 invariant 6, the following are frozen by this phase passing:**
`FluidReferenceCPU`'s public surface (`Tick`, `EditVoxel`, `GetVoxel`,
`SampleFluid`, the counters), `FluidLedger`'s ledger contract, and
`MaterialRules`' flag bit positions (which are Appendix B's, not this file's).
Bug fixes inside them are normal; signature changes are a redesign and need
flagging as such.

**Suite at sign-off:**

```
PASS 218  FAIL 0  SKIP 0
```
