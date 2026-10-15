# 92-gameplay-plan-update

Approval: owner, 2026-10-15 in chat (gameplay planning round, batches 1-5; the round's agreed next step is "fold all
answers into a gameplay plan update for approval", SESSION_HANDOFF 2026-10-15). Queued by the supervisor 2026-10-15.
Branch `engine-phase`. **PLANNING ONLY: no code, no Unity, no builds, no launches, no tests.** Ends with `STOP_FOR_OWNER`.

## Why
`GAMEPLAY_PHASE_PLAN.md` (task 63, 2026-10-11) was written from agent's picks before the owner's design round. The owner
has now answered five batches: **read `GAMEPLAY_DESIGN_ROUND_2026-10-15.md` first; it is the requirement** (Decided unless
marked OPEN). Parked gameplay task files `tools/overnight/later/67-72` (gameplay steps 4-9) were written from the old plan.

## Do
1. Update `GAMEPLAY_PHASE_PLAN.md` in place (keep its shape: "Owner may change" list, then steps). Every line that an owner
   answer now decides changes from "agent's pick" to "owner, 2026-10-15" with the answer; picks the round contradicts are
   replaced; new items get steps. At least:
   - World stages: few big jumps (Terraria Hardmode style, one boss flips the world: ores, creatures, some biomes). Name what
     the engine owes (a world-wide change pass over saved chunks/far data) and where it sits.
   - Death per world (soft / classic / hardcore at world creation).
   - Building: Lay of the Land style click-and-drag strokes (line growing into a plane for walls/roofs), cylinder, cone roof,
     paths drawn on the ground, terrain sculpt raise/lower, select your own build and use it as a brush / custom prefab.
     Order these into steps (smallest useful first: the drag line -> plane stroke on today's sized-cube brush).
   - Reactions: batch 1 + batch 5 lists. Group by what the existing fluid/material CA can do now vs what needs new
     simulation (heat, gas pressure, electricity, growth). Do not design the CA here; list steps and dependencies.
   - Spells: OPEN (leaning Noita wand slots, owner finds it complex). Keep combat steps that do not depend on it; mark the
     rest as waiting.
   - Far-away rules (batches 2-3): reference Amendment 8.15 (task 90a's draft; may still be waiting on the owner) instead of
     restating it; gameplay steps that need far simulation say "Requires 8.15".
2. Mark each of `later/67-72` as: still valid / needs rewrite (one line why) / obsolete -- in the plan, NOT by editing them.
3. Top of the plan: a `## For the owner` summary (plain words, under 15 lines): what changed vs the 2026-10-11 plan, the new
   step order, and at most 4 specific multiple-choice questions where the round left a real gap (recommendation first).
4. Commit the plan by path. HANDOFF starts with `STOP_FOR_OWNER` + those questions; `## For the doc`: the new step list
   (one line each) and the questions. No images (planning only; say so).

## Owner answer (2026-10-15, supervisor chat)
- Q1 plane stroke: face pull, not the box drag: "place one block down, extend it into a line by selecting one of its
  faces, then [pull] one of the line's faces [into a plane]"; study Lay of the Land's system later -> task 93b1.
- Q2 hardcore: (a) world locks to a fly-only viewer.
- Q3 classic: OPEN until the object-placement phase ("you only lose some items") -> 9b not queued.
- Q4: (a) alternate gameplay and engine tasks -> queued 93a1 BLD1, 93b1 BLD2, 93c1 step 4, 93d1 9a, 93e1 RX1,
  93f1 step 5, 93g1 step 6 between the 8.15 block; 94 fluid gravity fall; 97.
Task 92 itself was complete (plan written, 7107b4f); moved to done/.
