STOP_FOR_OWNER: Amendment 8.15 (simulation reach) is drafted and needs your answers to Q1-Q10 (end of
`AMENDMENT_8_15_SIMULATION_REACH.md`; "all (a)" approves it). Task 87 stays on hold until then.

# 90a-amendment-8-15-simulation-reach: DONE (planning only). Draft written and committed, marked DRAFT, NOT ADOPTED.
## What changed
- New `AMENDMENT_8_15_SIMULATION_REACH.md` (commit `2a70afd`, branch engine-phase). CLAUDE.md NOT edited; no code, builds or launches.
## Evidence (read, not measured)
- Code read: live reach 128 m (`EngineConfig.cs:268` load radius 10, `:479` fluid radius 1,280 voxels); with shells on,
  0.1 m is drawn only to shell 0's hand-over (`Raymarch.compute:3625`), and shells hold generator + ops only
  (`ShellStreamer.cs` header, `:418-422`). **So today a block-by-block edit 69-128 m away is live but not drawn** (code
  reading; not seen in a shot). No fire exists (only an `IsFlammable` flag, `MaterialRules.cs:37`). The GPU body buffer
  is sized to the CPU pool and indexed by the CPU handle (`TerrainClipmap.cs:225-241`).
- Memory baseline: peak 2,999 MB with shells on (task 85, `390e0de`); per-owner rows from task 55's ledger (via 8.14 §3).
## Left / notes
- Every number in the draft is (est.) except those citing a task/ledger. Other games' render/sim splits are from
  general knowledge, not looked up (labelled as such).
- D.4 gains a format version 2 (additive); §3.7 changes if Q2 (a). Both are flagged in the draft's §10.
## Next step
Owner answers Q1-Q10. Then the supervisor queues the tasks in the answered order (draft §11). 91 (step 17) does not depend on this.

## For the doc
**Summary (10 lines)**
1. Three zones: NEAR (fully live, 128 m fixed on every machine), FAR-ACTIVE (sparse coarse sim where something big happens), FROZEN (paused, caught up on return).
2. NEAR from the eye: a 1.8 m creature is 10 px at 93 m (540 rows), 124 m (720, M1 High), 186 m (1080) -> keep today's 128 m.
3. Step 14 redone: shrink only what is DRAWN (GPU box 32x32x8 -> 16³ + its own GPU brick pool); CPU reach stays 128 m.
4. Saving vs today's measured 2,999 MB: -675 MB (est.) vs task 87's -855 MB; ~180 MB is the price of keeping reach.
5. Far fire / floods / lava / powders run on 0.8 m cells in 51.2 m tiles at ~1 tick/s and write their results as new D.4 ops (burn, fluid level, powder settle): CPU stays the sole terrain writer; real on return. Replaces step 19a.
6. Far builds always visible: a coarse "edit summary" saved beside each D.1 (+~12% save size, 10-30 MB RAM, est.), drawn by shells and far band. Fixes the 69-128 m gap found today.
7. Catch-up: world clock + per-chunk last-seen time; fluid settles flat, powder falls, growth by timestamp on load.
8. Nukes: one carve op + a debris budget (10% rubble at rest, <= 200 k voxels; <= 64 fluid tiles awake; dust render-only).
9. New `IFarQuery` beside the frozen `GetVoxel`: exact (resident -> D.1 -> generator + ops), ~1-3 µs/point (est.), plus a coarse raycast.
10. Process memory ~2,370 MB (~2,470 with the light grid; est.) -> ~530 MB left for the game under 3 GB.

**Questions (recommended first):** Q1 NEAR radius (a 128 m fixed / b 154 m / c per machine) · Q2 what step 14 shrinks
(a GPU box + own GPU pool / b box only / c nothing) · Q3 far builds (a edit summaries / b regenerate on demand / c ops only) ·
Q4 far-sim order (a floods/lava first, then powders, fire / b fire first / c floods only) · Q5 far fire model (a 2.5D cells
with real burn ops / b 3D coarse / c visual only) · Q6 catch-up (a instant on load / b fast-forward / c none) · Q7 nuke
debris (a budgeted / b all simulated / c pure carve) · Q8 far query (a exact + raycast / b heights only / c none) ·
Q9 co-op determinism (a far sim deterministic, near host-authoritative / b all deterministic / c later) · Q10 when
(a "engine owes now" ~13 sessions, then lighting, then far sims / b all before lighting / c only the shrink now).

**ETA change:** steps 14 + 19 (~5 sessions) become 17 tasks, ~26 sessions (~32 with retries): **net +~21 sessions
(+~27 with retries), about +1.5-2 weeks** of queue time; ~13 sessions of it are needed before steps 16/20.
No images: planning only (the draft has two ASCII diagrams of the zones and the far-sim data flow).
