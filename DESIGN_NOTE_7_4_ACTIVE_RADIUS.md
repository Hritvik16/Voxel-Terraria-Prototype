# Design note — §7.4's near-player active radius

**Status: proposal for review. No code written against this yet.**
Written after Step 0's measurement, which changes what this has to solve.

---

## 0. What Step 0 established, and why it reshapes this

Measured against Playground's own 8192-slot region:

| | |
|---|---|
| allocations per **live** voxel | **24.5** |
| placed voxels to exhaust 8,192 slots | **406** |
| `everAllocated` / cap at stall | **10,984 / 8,192** |
| voxels left frozen unsupported | **105** |

Two consequences that constrain everything below:

1. **Slot indices are consumed by churn, not by live fluid.** 107 live voxels
   cost 2,622 allocations. `AllocSlot` is a bump allocator with no free list
   (PHASE_5C_COMPLETION.md §5), and it bumps *before* the capacity check, so
   even failed promotions burn indices — exhaustion is self-reinforcing and
   irrecoverable for the region's lifetime.

2. **§7.4 alone would make this worse, not better.** A moving radius bounds the
   *live* set, which is what §7.7 assumes when it calls forced demotion "a rare
   safety valve". But bounding the live set means *more* wake/sleep cycling, and
   every wake burns a fresh index. Without a recycling allocator, adding §7.4
   accelerates the failure it is supposed to prevent.

**So this note covers two changes, and the order matters: the allocator first,
the radius second.** Shipping the radius alone would be a regression.

---

## 1. Which shape §7.4 should take

The prompt asks whether the right shape is *(i)* a moving window that re-centres
and drives wake/sleep at its boundary, or *(ii)* §7.6's existing per-voxel
proximity wake generalised to use player position.

**It is already (ii), and it is already built on the GPU.** This is not a
judgement call — it is what the shader does today:

```hlsl
bool WithinActiveRadius(int3 v)
{
    int3 d = v - _PlayerVoxel.xyz;
    return (d.x*d.x + d.y*d.y + d.z*d.z) <= (_ActiveRadiusVoxels * _ActiveRadiusVoxels);
}
```

- `CSPromote` refuses to promote a cell outside the radius (`DBG(16)`, a code
  distinct from not-mobile — someone anticipated needing to tell them apart).
- `CSIntent` force-demotes a slot whose home has left the radius:
  `slot.stateFlags = FLAG_FORCE_DEMOTE; SlotAtBuffer[home] = NONE;` — commented
  in the shader as "§7.4 near-player scope / §7.7 forced demotion".

Both directions of §7.4 exist, per-voxel, and are load-bearing. **What is
missing is entirely CPU-side:**

- `FluidGpuSimulation.PlayerVoxel` is a settable property uploaded every tick —
  and **nothing ever updates it.** Playground sets it once at construction to
  the arena centre; every rig does the same.
- `ActiveRadiusVoxels` defaults to `FLUID_ACTIVE_RADIUS_VOXELS` = 1280 voxels
  (128 m). The Playground arena's half-diagonal is 55 voxels. **The radius is 23×
  larger than the region it is meant to bound, so the test never fires.**

**Therefore the work is not "build §7.4". It is "drive §7.4".** The region box
stays exactly where it is — it is the CA's addressing space (§7.2's op-list
indexing, `RegionIndex`'s shifts) and moving it is a different and much larger
change. What moves is the *activity* inside it, which is what §7.4 actually
describes: "fluid simulates only within an active radius around the player."

> **This is why option (i) is rejected.** A moving *region* would have to
> re-base `RegionOriginVoxels`, which re-indexes every slot's `homeIndex` and
> every in-flight op in the readback queue. §7.2's op-list is addressed by
> region cell index; changing the origin under an in-flight batch would apply
> ops to the wrong voxels. That is a §7.2 change, not a §7.4 one.

---

## 2. The allocator: making sleep actually return capacity

§7.6 says sleep "frees the slot". §7.7 says forced demotion "frees the
furthest-from-player slots". **Both already do free the binding**
(`SlotAtBuffer[home] = NONE`) — what neither does is return the *index*.

Proposed: a **GPU free list**, popped by `AllocSlot` and pushed by the paths
that already clear a slot.

```
Counters[1]  bump allocator  (unchanged, the fallback when the free list is empty)
FreeList[]   RWStructuredBuffer<uint>, capacity _SlotCapacity
Counters[2]  free-list count
```

- `AllocSlot`: try `InterlockedAdd(Counters[2], -1)`; if the pre-value was > 0,
  reuse `FreeList[pre-1]`. Otherwise fall back to bumping `Counters[1]`.
- On demote/sleep/orphan-free: `InterlockedAdd(Counters[2], 1, slotIdx)` then
  `FreeList[slotIdx] = s`.

**§7.3 compliance.** The file's header states "There is exactly one
InterlockedAdd in this file, in `AllocSlot`, and it is NOT the claim path." The
free list adds atomics to the *same* non-claim path — promotion and demotion,
never Intent→Claim→Commit. §7.3's no-atomics rule is about the claim protocol
and is untouched.

**One correctness hazard, called out rather than discovered later:** a slot
must be pushed to the free list *exactly once*. `CSIntent` has several paths
that clear a slot (orphan self-free, ownership guard, reacted, force-demote) and
`CSSweep` has more. Double-pushing hands the same index to two live slots, which
is silent state corruption of exactly the kind §7.3 exists to prevent. **The push
must therefore happen in one place**, gated on an ownership test, not sprinkled
at each clear site.

**Fix the ordering bug too:** bump *after* the capacity check, or a failed
promotion keeps burning indices.

---

## 3. Avoiding boundary thrash

A player oscillating at the radius edge must not wake/sleep a slot every frame.

**Hysteresis, two radii:**

- promote while `d² ≤ R_wake²`
- demote only when `d² > R_sleep²`, with `R_sleep = R_wake × 1.15`

A slot promoted at the boundary must then travel 15% of the radius before it can
be demoted, and vice versa. With the shipped radius that is ~19 m of movement per
cycle — far beyond any per-frame oscillation.

**A re-centre threshold as well, and it does separate work.** `PlayerVoxel` is
uploaded per tick; updating it every frame means the boundary sweeps continuously
and a ring of slots at the edge flickers even without hysteresis, because the
radius itself moves sub-voxel amounts. Re-centre only when the player has moved
more than `RecentreThresholdVoxels` (proposed: 16 voxels = 1.6 m) since the last
update. The two mechanisms are complementary: the threshold quantises *when* the
boundary moves, hysteresis widens *where* it is.

---

## 4. Composition with the global pool budget

The prompt asks whether "only what's within the active radius holds slots, and
everything else is already asleep per §7.6" composes, rather than assuming it.

**It composes, with one caveat, and one thing that is genuinely untested.**

- Fluid the player created elsewhere and walked away from is **terrain bytes**,
  not slots. §7.6: "Dormant = zero GPU slots, zero sim cost." Once demoted, a
  distant pool costs nothing. So the *live* budget is bounded by the radius, as
  §7.7 assumes.
- **The caveat:** that is only true once the allocator recycles. Today the
  distant pool's slots are demoted but their indices are gone forever, so the
  budget is bounded by *cumulative history*, not by live extent. §2 is a
  prerequisite for this section's claim, not an independent improvement.
- **Untested (§9.7's own list):** *several simultaneous active regions.* A
  moving radius makes this reachable in ordinary play for the first time — walk
  between two pools and both are in range. Step 2 must cover it.

---

## 5. What this must not disturb

- **§9.4's residency-edge guard.** `VoxelCollision`/`RequestWake` already refuse
  out-of-region wakes, and `FluidOpListReadback` drops ops for non-resident
  chunks. Adding a radius test does **not** replace either: an in-radius,
  in-region cell in an *unloaded* chunk must still be refused. The radius is an
  additional gate, never a substitute. Step 2 re-runs the §9.4 case under the new
  logic rather than trusting that it still holds.
- **§9.5's admission guard.** A single-frame position jump larger than the
  streaming window makes `ChunkStore` refuse the insert. Now that `PlayerVoxel`
  follows the player, a teleport also jumps the radius — potentially demoting
  everything and re-promoting elsewhere in one tick. That must degrade cleanly.
- **CLAUDE.md's single-writer and authoritative-byte rules.** Nothing here adds a
  CPU writer; the radius is a GPU-side gate on promotion, and the terrain byte
  remains authoritative for demoted fluid (which is exactly why demotion loses
  nothing).

---

## 6. Flagged for a human — I am not deciding these

1. **The radius value.** `FLUID_ACTIVE_RADIUS_VOXELS = 1280` already exists,
   with a rationale (match C.5's LOD0 boundary) and an explicit disclaimer:
   *"ASSUMPTION, NOT MEASURED. Phase 5b's GPU-lane cost measurement is the gate
   that has any business tuning it."* **I will use the existing constant and not
   invent a different number.** But note it is 23× the Playground arena's
   half-diagonal, so within a single small arena the radius still never bites —
   the mechanism will be *correct* and *inert* there unless the region is larger
   or the radius smaller. Making the Playground demonstrate §7.4 needs one of
   those two numbers changed, and that is a game-design call.

2. **Hysteresis ratio and re-centre threshold** (1.15× and 16 voxels above) are
   engineering defaults chosen to be obviously safe, not measured. They are
   cheap to change and I will expose both as constants.

3. **The world's generation boundary.** §7.4 says nothing about what happens
   when the active radius extends past generated/resident terrain. The existing
   §9.4 guard already refuses those wakes, so the *safe* behaviour is inherited —
   but "should fluid at the edge of the loaded world simulate at all" is
   unanswered by the spec and I am not inventing an answer.

---

## 7. Proposed order of work

1. Free-list allocator + fix the bump-before-check ordering. **Prerequisite.**
2. Drive `PlayerVoxel` from the real player, with re-centre threshold.
3. Hysteresis radii for promote vs demote.
4. Test coverage per §9.7's untested list.
5. Playground + guide (Step 3).

Step 0's regression rig re-runs at the end: the same 406-voxel pour must no
longer stall.

---

## 8. What playtesting found afterwards: demotion had no inverse

**Added after the §7.4 work above shipped.** This section is a correction to it,
not a footnote — the note as written above was wrong by omission, and the gap it
left was visible in ordinary play within a session.

§7.4 states both directions of the same mechanism:

> "On approach it wakes (slots allocate on GPU); on departure it sleeps back to
> static terrain."

§3 above reasoned carefully about the *departure* half and about not thrashing
the boundary. It never asked what performs the *approach* half. Nothing did.

**The symptom.** A cluster of water voxels hanging in mid-air beside the fluid
arena, visibly disconnected from the terrain with gaps beneath it, that never
resolved no matter how long you watched or how close you stood.

**The measurement** (`run-playtest-bugs.sh`, step A2). A pour watched from 13 m
back left 54 unsupported voxels. The same 54 at 16 m back. The same 54 after
re-centring the radius onto the arena and settling for 1500 ticks. A count that
does not move under a change that should move it is the whole signal.

**Which failure it was.** `ReadSlotAtCell` reported `-1` for all 54: they owned
no slot, so CSIntent's force-demote had cleaned up correctly — this was *not*
the orphaned-slot leak its own doc comment warns about. The control settled it:
issuing `RequestWake` for those exact cells, changing nothing else, took 54 → 1.

**The deadlock.** Neither promotion path can start a region that is fully asleep:

- `CSPromote` is **request-driven**. It walks `WakeRequests`. Moving the player
  generates none, because §7.4's radius is a GPU-side gate and the CPU never
  learns which cells crossed it.
- `CSWakeScan` is **activity-driven**. It requires a neighbour carrying a
  `WakeMark` set by a descending move *this tick*. When every slot in the region
  has been demoted, no cell carries a mark, so there is nothing to propagate
  from. It is a deadlock, not a slow wake.

This is why §7.7's "distant fluid freezes mid-flow, no state lost" is true and
still not sufficient: no *state* is lost, but without an inverse the freeze is
permanent, and permanently-frozen mid-flow fluid is indistinguishable from a
rendering bug to anyone playing.

**The fix.** A radius-driven seed in `CSWakeScan`, enabled for
`RecentreSeedTicks` ticks after the active centre moves: a mobile cell inside
the wake radius, owning no slot, with **a legal descent target that is Air**,
wakes without needing a marked neighbour.

The predicate is deliberately narrower than "everything in radius". Waking every
mobile cell would allocate a slot per voxel of a settled lake and spike §7.7's
pool for the `_SleepTicks` it takes them all to sleep again. "A descent target
is Air" reuses the exact five directions §7.4's tiers 1 and 2 can move in, so it
cannot drift from the Intent hierarchy, and a settled body of water fails it
everywhere except its own perimeter. It does not catch a cell that can only move
laterally, and does not need to: seeding the descenders is enough, because they
move, set marks, and hand the region back to ordinary propagation.

**Scope — this was never a demo-value artifact.** The Playground's 128-voxel
demo radius is what makes it fire at 13 m rather than 147 m. At the shipped
`FLUID_ACTIVE_RADIUS_VOXELS = 1280` the identical deadlock occurs whenever a
player leaves a region and returns. Raising the demo radius would have hidden
the symptom and left the defect.

**What §6 item 1 got right and what it missed.** It correctly predicted that the
mechanism would be "correct and inert" in a small arena at the shipped radius,
and that demonstrating §7.4 in the Playground needed a smaller radius. Lowering
it did demonstrate §7.4 — including the half that was missing. That is an
argument for dogfooding at values where a mechanism actually engages, not
against it.

**Not measured.** `RecentreSeedTicks = 4` is an engineering default: one tick is
enough in principle, and the margin exists so a tick where §7.7's pool guard
refuses an allocation is retried rather than stranding exactly the cells the
seed exists to rescue. The GPU-lane cost of the seeded ticks has not been
measured and is not claimed.

---

## 9. §6 item 1, finally measured: the shipped radius cannot bite

**Added after §8.** §6 item 1 flagged `FLUID_ACTIVE_RADIUS_VOXELS = 1280` as
never measured and declined to invent a different number. It has now been
measured — `run-fluid-scale.sh`, step 3 — and the result is not a tuning
observation but a structural one.

**For the radius to gate anything, the region must be larger than the radius.**
A §7.2 region is a *dense per-cell map*: four GPU buffers (`claim`, `slotAt`,
`reacted`, `wakeMark`), 4 bytes each, **16 bytes per cell**, measured and
confirmed identical at three sizes:

| region | cells | per-cell buffers | half-diagonal | does a 1280 radius bite? |
|---|---|---|---|---|
| 64³ | 262,144 | 4.0 MB | 55 vox | no |
| 128³ | 2,097,152 | 32.0 MB | 110 vox | no |
| 256³ | 16,777,216 | 256.0 MB | 221 vox | no |

The smallest power-of-two region whose half-diagonal reaches 1280 voxels is
**2048³ = 8,589,934,592 cells = 128 GB** of per-cell buffers alone — and
`CSClear`, `CSCommit` and `CSWakeScan` each dispatch over *every cell every
tick*, so the per-tick cost scales with region volume regardless of how much
fluid is actually live.

So at any region size that can exist, the shipped radius is at least 5.8×
larger than the region's own half-diagonal. **§7.4's gate is inert by
construction at the shipped constant**: every cell in the region is always
inside the radius, `WithinActiveRadius` is always true, and `BeyondSleepRadius`
is never true.

### 9.1 What this does and does not mean

It does **not** mean §7.4 is broken. The mechanism is proven correct in policy
(`FluidActiveRegionTests`) and proven to demote on departure and wake on
approach on the GPU (`run-playtest-bugs.sh`, steps A2/A2m). The Playground
exercises it precisely *because* its demo radius is 128 rather than 1280.

It means the **shipped constant selects a regime no single region can reach**,
so in the shipped configuration §7.4 costs its per-cell dispatch and delivers
no gating.

### 9.2 A DESIGN FORK — flagged, not decided

Three readings, and neither §7.2, §7.4 nor §2.5 settles which is intended:

- **(a) The radius is mis-sized.** It was derived to match C.5's LOD0 boundary
  (128 m), a *rendering* distance, and nothing checked it against the region it
  has to fit inside. Under this reading it should be derived from the region —
  some fraction of the half-diagonal — and the LOD0 correspondence abandoned.
- **(b) The region is the wrong shape.** If a 128 m active radius is genuinely
  wanted, the region cannot stay a dense per-cell map; it needs to be sparse or
  tiled, at which point the per-tick full-region dispatches go too. That is a
  §7.2 redesign, not a constant change.
- **(c) The radius belongs one level up.** §7.4 may be meant to select which of
  *many* regions tick, not which cells within one tick. `run-fluid-scale.sh`
  step 2 shows 2/4/8 simultaneous regions scale linearly with no cross-region
  interference, so the multi-region path is real and cheap. Under this reading
  the per-cell radius gate is the wrong mechanism at the wrong level.

I am not choosing between these. (a) is a one-line change that makes the
mechanism live immediately; (b) and (c) are architecture. The measurement above
is what any of the three needs in order to be decided on evidence.

### 9.3 Slot occupancy, measured (no timing claim)

From the same rig, step 1 — peak *simultaneous* slot occupancy per placed
voxel, which is the number that sizes a pool:

| live voxels placed | peak slots | slots/voxel | allocations/voxel |
|---|---|---|---|
| 500 | 1,500 | 3.00 | 3.00 |
| 2,000 | 6,000 | 3.00 | 3.00 |
| 8,000 | 22,620 | 2.83 | 2.83 |
| 32,000 | 84,244 | 2.63 | 2.63 |

The ratio *improves* with scale and the free list returned every slot at every
rung, so A.5's allocator is healthy across two orders of magnitude. At the worst
measured ratio a 500,000-slot pool holds roughly 166,000 live voxels in one
region.

**§2.5's ~500,000 active-fluid target is not tested by this and is not
claimed.** The line above is arithmetic on a measured ratio for a single region;
whether the engine sustains that figure world-wide is a different question, and
the honest answer is still that nobody has asked it.
