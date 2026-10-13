# 63-gameplay-phase-plan

Approval: owner, 2026-10-11 ~00:10 in chat, going to sleep: "keep queuing tasks, ideally moving forward with gameplay
features instead of optimizations, pick your recommendations, i can always queue something to change them if i dont
like them later." This OVERRIDES ROADMAP.md's order for now (step 4 "player and placement" no longer waits for the
engine phase to finish). Queued by the supervisor 2026-10-11. Branch `engine-phase`.
**PLANNING ONLY:** no code, no Unity, no builds, no rigs. Write ONE local file `GAMEPLAY_PHASE_PLAN.md`, commit it.
Do NOT end with STOP_FOR_OWNER: the owner delegated the picks. End DONE; tasks 64-66 implement its steps 1-3.

## Inputs (read them)
- `GAME_DESIGN_BRIEF_SNAPSHOT.md` (plain-text copy of the owner's design brief, the design authority): its
  **Decided** rows bind; **Leaning** rows are the default; **Open** rows are yours to recommend.
- `DESIGN_LARGE_WORLD_AND_TERRAIN_OVERHAUL.md` sections 2 and 12-15 (terrain intent, owner decisions), `ROADMAP.md`,
  `ENGINE_PHASE_PLAN.md` (what the engine phase still owes; its steps resume later), `KNOWN_ISSUES.md` (the
  placeholder player runs up ~70 deg slopes; tg2 over the 3 GB cap), `tools/overnight/BUGS.md`.
- The code as it is: the placeholder player / build & dig (`Assets/Game/`, F2 help panel), `PlaySettings`, the F6
  menu, the world record (task B6 danger map), `Tg2Spawn` (a placeholder).

## What the plan must contain
1. A one-paragraph goal: the first PLAYABLE loop on the tg2 island, Terraria-like, carried by the overworld.
2. Picks for every Open/undecided item the first steps need, each one line: the pick, why, how cheap to reverse.
   Mark them "agent's pick (owner may change)". Do NOT decide combat/story/UI style beyond what a step needs.
3. An ordered step table like ENGINE_PHASE_PLAN.md section 3: one change per step, acceptance (EditMode tests,
   screenshots, metrics -- NO frame-time measurement, owner rule: features over measurement), size S/M/L,
   dependencies. Steps 1-3 must each fit ONE unattended task (S or M), be useful on their own, and need no owner
   decision. Prefer, in this order: the core loop (dig -> item -> inventory/hotbar -> place), then the brief's
   Decided movement abilities on the placeholder player (e.g. swimming/diving, climbing, dash/double jump -- tuned
   to feel good, top speed noted), then ores by depth + biome x landform (Decided sources). Towns, NPCs and the
   trees voxels-vs-meshes question (brief: Open, needs a measurement) come later in the table, not in steps 1-3.
4. Guardrails that every step obeys: CPU is the sole terrain writer (edits through `IEditService.SetVoxel` / the
   existing edit path); frozen APIs (`GetVoxel`, `SetVoxel`, D.1, the 4.4 table) untouched -- a step that needs a
   change STOPS_FOR_OWNER instead; no memory-layout change; terrain-generator changes (`Assets/TerrainGen/`) run
   `SUITE=all`; rigs and capture modes keep their behaviour; new play options go in `PlaySettings` + the F6 menu.
5. "Owner may change" list at the top: every pick in one line each, so the owner can skim and object.

## Acceptance
- `GAMEPLAY_PHASE_PLAN.md` committed; HANDOFF names steps 1-3 in one line each and has a `## For the doc` block
  (goal, the picks list, the step table).
