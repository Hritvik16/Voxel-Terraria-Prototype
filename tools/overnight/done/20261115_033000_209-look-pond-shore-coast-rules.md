# 209-look-pond-shore-coast-rules

Model: opus

Approval: OWNER, 2026-11-14, Queue decisions doc Q-AF answer "a", with his words: "why can't we use the same rules we use for
the island coast? I want the ponds to have a natural outline."

## Background
Task 203 (`FluidPockets`, TASKS.md "Task 203") gave ponds 3-4 overlapping elliptical lobes. The owner still wants a NATURAL outline
and asks why the island-coast rules are not used.

## Do
1. FIRST find how the world's island / sea coast outline is made (grep TerrainGen for the coast / shoreline code: noise warp, fbm,
   threshold, whatever it is). Write in HANDOFF, in plain words, whether the same rule can drive a pond's outline and, if not
   all of it, which part cannot and why (pond footprint must stay inside the planner-checked ellipse/box, integer Q16 math, the
   plane bits are 5 spare bits, no per-pond stored data). CLAUDE.md: coordinate math stays integer; no "float precision" excuses.
2. Build the answer: ragged shore from the coast rule (reused or an exact integer equivalent of it, data-derived, no per-terrain
   tuning) INSIDE 203's containment, and the bank terraces / rings (`LobeRing`, 196 basins) follow the new outline. Behind a
   switch (e.g. `-pocketshore 0` = 203's outline byte for byte), default ON if tests are green and the pictures look natural.
3. Same rule for every fluid and biome. Ponds must stay one 4-connected body, rings nested, bed step <= 2 voxels (extend
   `Outline_Rules`). Fast suite; SUITE=all because `Assets/TerrainGen/` changes (owner-approved here).
4. PREAMBLE rule 11: report memory peak, GPU body pool peak + refusals, CPU pool vs the switch OFF from launches you already do.
   Budget: refusals not up, peak +50 MB. No timing arms (rule 10).
## Pictures
Full size, `viewer.html`: top-down and oblique of 5+ ponds, 203 vs new, several fluids/biomes, plus the island coast beside a pond
for comparison. Captions say what to look at. TASKS.md paragraph.
