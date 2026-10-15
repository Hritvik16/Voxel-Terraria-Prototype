# 90a-amendment-8-15-simulation-reach

Approval: owner, 2026-10-15 in chat ("Queue a planning task to draft Amendment 8.15 (simulation reach)?" -> "Yes, before 91").
Queued by the supervisor 2026-10-15. Branch `engine-phase`. **PLANNING ONLY: no code, no builds, no launches.** Ends with
`STOP_FOR_OWNER`.

## Why
Task 87 (engine step 14, Amendment 8.14 row 6) would have shrunk the CPU load radius 10 -> 5/6/7 chunks, i.e. GAMEPLAY reach
(`GetVoxel`, `SetVoxel`, physics, fluid residency) 128 m -> 59-82 m. The owner stopped it: "The player needs to be able to
affect terrain outside of range. NPCs would need to interact with terrain out of range. Fluid simulation would look weird
if it is only contained within that 0.1 area." The owner then answered three batches of gameplay questions:
**read `GAMEPLAY_DESIGN_ROUND_2026-10-15.md` first; it is the requirement.** In short: persistent Terraria-style world;
Noita material reactions, spell crafting, powders everywhere, destroy everything (no structural integrity); single player
first, co-op later (keep it possible); ~50-70 creatures near the player; far away the DEFAULT is "freeze, catch up on return",
but fire, lava/floods, powders/collapses and town life keep running coarsely; a far forest fire keeps burning visibly; player
builds and craters ALWAYS visible from far away; nukes 50 m+; wild creatures despawn far, towns persist.

## Deliverable: `AMENDMENT_8_15_SIMULATION_REACH.md` (DRAFT, NOT ADOPTED), in the repo
Read first: `ARCHITECTURE_v8.6.md` §0 (constitution), §4.3-4.4 (window, state table), Appendix D (D.1 deltas);
`AMENDMENT_8_14_LOD_SHELLS.md` (rows 6, 7, 8-13b; D.4 edit ops; sizing table §2/H); `ENGINE_PHASE_PLAN.md` (steps 14-22,
memory budget §2, the "Owner answers, 2026-10-15" section); `tools/overnight/blocked/87-engine-14-smaller-window.md` and its
`.patch`; `FLUID_TICK_DIAGNOSIS.md`; the brief's open Q12 and Q13 (`GAME_DESIGN_BRIEF_SNAPSHOT.md`); KNOWN_ISSUES entries
on the 3 GB cap and fluid pool. Grep the code for what exists (fire? powders? `EditOps`, `ShellStore`, `FluidTileMap`).
Cite sections for every design choice (CLAUDE.md rule 4). The document covers, each with a recommendation and its cost:
1. **Render distance vs simulation distance**, how other games split them (Minecraft simulation distance, Noita, Terraria,
   Valheim, Factorio), and our zones: NEAR (full 0.1 m sim; radius >= today's 128 m, derive it from the eye: a 1.8 m NPC
   ~10 px tall at ~90 m at 540 rows), FAR (coarse, sparse, only where something is active), and FROZEN with catch-up.
2. **Step 14 redone:** shrink only what is DRAWN (GPU mirror + GPU pool) and keep CPU reach; the memory saved (est.) vs 87's.
3. **Far simulation as edit ops:** coarse fire / lava / flood / powder-collapse sim on sparse active regions that writes its
   results as D.4 ops (new op kinds, e.g. replace-material, fluid fill), so the CPU stays the sole terrain writer and the
   result is real on return; tick rate (step 17's fixed tick), determinism for later co-op, budget. Absorbs step 19 (E7).
4. **Catch-up on return:** what resolves instantly when a region re-enters NEAR (fluid at rest, growth by timestamp).
5. **Far builds visible (brief Q12, owner "yes, always"):** D.1 block edits reaching the shells and far band (downsample on
   eviction? a shell override store? ops?) with memory and save-size costs.
6. **Nukes 50 m+ near the player:** a debris budget (what becomes powder/fluid/dust/vanishes) so fluid tiles and powder sim
   stay bounded; far ops already carve.
7. **A far query API** for NPC/projectile/game logic beyond NEAR: answering a voxel from seed + ops + D.1 without residency
   (the generator is a pure function of (x, z); cost per query). This is a NEW API beside the frozen `GetVoxel`, not a
   change to it -- say so explicitly; flag any frozen-contract or §4.4 state-table change as a redesign.
8. **Creatures:** ~70 near, despawn far, towns persist on routines; what the engine owes them now vs in the gameplay phase
   (brief Q13 measurement stays a gameplay-phase task).
9. **Memory under the 3 GB cap** (a table like 8.14's), **a task list** (one change per task, sizes S/M/L, dependencies),
   **where it slots into ENGINE_PHASE_PLAN steps 14-22** (which steps it replaces or reorders), and an ETA estimate.
10. **Questions for the owner:** specific multiple-choice, recommendation first, cost of each answer (the owner prefers
   batches of specific questions over long text). Keep the whole document readable: plain words, short sections,
   numbers marked (est.) vs measured.
Include 2-3 simple diagrams (inline SVG or ASCII) of the zones if they help.

## Acceptance
- The document exists, is committed on `engine-phase`, marked DRAFT, NOT ADOPTED; CLAUDE.md is NOT edited (adoption is the
  owner's call).
- HANDOFF ends with `STOP_FOR_OWNER` and `## For the doc`: a 10-line summary, the question list, the ETA change.
