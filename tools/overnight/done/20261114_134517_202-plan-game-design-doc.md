# 202-plan-game-design-doc

Model: opus

Approval: OWNER, chat 2026-11-13, G1 (a): start the GAME-DESIGN DOCUMENT now as a planning task, beside the look work
(the 2026-11-05 order says the placement phase is opened by a game-design doc written with the owner; this starts it early).

**PLAN ONLY. No code.** Writes ONE Claude Doc "Game design" for the owner (allowed here explicitly, SUPERVISOR.md "Planning
without code"); repo copy `GAME_DESIGN.md` marked **DRAFT -- owner answers pending**. CLAUDE.md rule 5: there is no
game-design document; `ARCHITECTURE_v8.6.md` is engine only -- do not cite it for game design.

## Sources (read; cite each decision's source and date)
The game-design brief (CLAUDE.md rule 5; `GAME_DESIGN_BRIEF_SNAPSHOT.md` is its repo snapshot) -- its Decided rows bind;
`DESIGN_LARGE_WORLD_AND_TERRAIN_OVERHAUL.md` §2, §12-15; `GAMEPLAY_DESIGN_ROUND_2026-10-15.md`; `GAMEPLAY_PHASE_PLAN.md`;
`ROADMAP.md`; `tools/overnight/OWNER_TOPICS.md` (13 Nov: trees, towns V1-V4, performance P1-P4, reflections, ponds);
owner answers in the Queue decisions doc (Questions / Answered tabs: e.g. skills + weapons with material grades, throwing
fluids (flamethrower), castles and procedural buildings that fit any ground, characters later with a humanoid design tool,
lakes of every fluid). Do not invent decisions: a thing the owner has not said is "Open".

## The doc
1. One-page pitch in plain words (what the player does, the feel), from the owner's own words.
2. Sections: world and travel (towns as safe stops on the route network, danger map: V4 (a)); settlements (V1-V3);
   resources and materials (ores, trees, fluids); building / editing; survival; combat and skills; creatures / NPCs; progression;
   the core loop (minute / hour / session). Each row tagged Decided / Leaning / Open with its source.
3. What the PLACEMENT phase must deliver for this design (objects, flora, settlements, fluids) and what must exist before
   CHARACTER work -- a checklist the supervisor can turn into tasks.
4. QUESTIONS for the owner in batches of 4-6, specific, recommendation first, what each answer costs or unlocks. The owner
   prefers specific questions over long explanations.

## End
HANDOFF with the doc link and `## For the doc` (one-line result, the first question batch). No STOP_FOR_OWNER: questions
travel through HANDOFF. Commit the repo copy. No tests (no code).
