Approved by the owner 2026-10-02: terrain direction **B, "Rules for play"** (terrain
review section 13; the owner considers direction D, the design brief, done), and
"plan next, then TAA" (Queue decisions Q2 (a)).

PLANNING ONLY. No code, no Unity, no builds, no rigs, no edits under Assets/.
Exception to preamble rule 7: this task MAY read Claude Docs and MUST write its
result as ONE new Claude Doc (Claude Docs connector, `batch` to create, one
`update` per section). If the Claude Docs tools are not available in this
session, write the same content to `SURFACE_REBUILD_PLAN.md` at the repo root,
commit it, and say so at the top of HANDOFF.md (the supervisor publishes it).

Goal: a plan for rebuilding the tg2 surface as direction B, concrete enough
that the owner can approve it and the work can then be queued as small,
measured tasks.

Read (targeted: search / sections, not whole docs):
- Terrain review, "Terrain, Play & Scale -- Where the World Stands":
  https://claude.ai/code/artifact/7443de71-6716-4f99-869b-98d3c6814fd7
  section 13 "Directions" (row B), "Do now, whatever the direction", and the
  measurements B must improve (slope distribution, open land, landmarks).
- Game Design Brief: https://claude.ai/code/artifact/d31b4321-77c3-4026-93d2-460c190950bf
  Only its "Decided" rows bind the plan. "Leaning"/"Open" rows become questions.
- `DESIGN_LARGE_WORLD_AND_TERRAIN_OVERHAUL.md` section 2 and sections 12-15.
- `KNOWN_ISSUES.md`: the TG2 entries (3 GB cap, walking height, band placement,
  placeholder player on 70 degree slopes).
- The current generator only as far as needed to say what is kept and where
  the new rules plug in (`Assets/TerrainGen/`: grep, don't read whole files).

The plan doc ("Surface rebuild plan", lead sentence = what B builds and in how
many steps) must contain:
1. The rules: each B rule (start zone, large biome zones, region types by
   flatness, placed landmarks, town sites, erosion as a light valley pass,
   danger layout, routes -- see "Owner corrections" below) with its ONE
   controlling number, its default, and which Decided row of the brief it serves.
2. Kept as is: caves, rivers, palette D, far band, streaming, determinism
   tests, thick depth layers. Say what each new rule must not break.
3. The "world record" (what is where: region, biome, landform, features,
   water, town sites) that later object placement reads -- format and memory
   cost, within the 3 GB cap (KNOWN_ISSUES: tg2 is already over it).
4. Steps, like v3's T1-T6: each one change with an acceptance check that is a
   NUMBER measured on seeds (e.g. % open land, median slope, distance from
   start to easy land, biome patch size), plus EditMode and determinism. Order
   them so each step is playable/viewable on its own.
5. The "Do now, whatever the direction" engine fixes: which are already done
   (check git log / KNOWN_ISSUES), which belong before step 1.
6. Risks and what is NOT in scope (movement rules: the player is a placeholder;
   object placement itself; snow lines or biome-by-height -- the owner rejected
   those; biomes and landform are independent maps).
7. Questions for the owner: specific multiple-choice, recommendation first,
   batched, only where the brief has no Decided row.

## Owner corrections to direction B (2026-10-02)
Direction B was written BEFORE the design brief; where they disagree, the brief wins.
- NO "barriers at zone borders" in the sense of walls that keep the player out.
  The character will be very mobile (climb, glide, grapple, flight): progress
  is gated by DANGER and MATERIALS, not walls. A border can be dangerous, never
  impassable.
- NO distance-based "difficulty zones". Difficulty = altitude + biome x landform
  + world progress (brief, Decided). Plan the danger layout from those inputs.
- ROUTES (owner, 2026-10-02): the easy areas (~10% of land, several areas)
  must be reachable from each other, and the way between them may cross harder
  ground. An under-equipped player should be able to cross it, or sneak in and
  get some loot out, with real risk. Along "routes" between easy areas (not
  literal roads: corridors of terrain) the danger should be somewhat LOWER than
  the ground around them. The plan should say how routes are chosen (which easy
  areas connect, how wide, how much the danger drops, how a player can find one
  -- landmarks, sight lines) and how this is measured on seeds. The owner flagged
  this for the rebuild phase: plan it, and put any open choices in item 7.

Finish: HANDOFF.md starting with `STOP_FOR_OWNER`, the plan doc's link, and the
questions from item 7 copied verbatim (the supervisor moves them into the
"Queue decisions" doc). Do not queue implementation tasks: the owner approves
the plan first. Commit only HANDOFF.md (and SURFACE_REBUILD_PLAN.md if used).
