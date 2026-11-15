# 211-plan-gdd-batch-a-answers: DONE. A1-A6 folded into the Game design doc (live, rev 51) and `GAME_DESIGN.md`; 5 new owner questions C1-C5.

## What changed
- Claude Doc "Game design" (https://claude.ai/artifact/BprGXbxRr6W5MPdDAXxYp8), edited directly. No comments were on it. The owner's typo fix ("practiced", rev 18) is kept.
- `GAME_DESIGN.md` (same changes, owner in 3rd person) + a `TASKS.md` "Task 211" paragraph. Commit 69c5cc4 (plus the HANDOFF commit).
- No Unity launch and no tests: plan only. Rule 11 does not apply (nothing drawn or stored).

## Decisions and judgment calls (all marked in the doc)
- **Design principle** goes under the one-line brief, in the owner's words. I checked every Decided row: none conflicts outright. Two rows got a note:
  - "Editing towns" is split. "Fully editable" stays Decided. The new row "How residents react to edits" (anger, leaving, repairs) is **Leaning**: under the principle, reactions should fall out of the town rules, not come from a scripted list (C2).
  - "Spell crafting" (already Open) notes that wand slots and modifiers go against the principle.
- **A4 palette (checked in `GenMat.cs`)**: the 11 free ids are GenMat 9, 18, 26-29, 38, 39, 59, 94, 95 (engine 137, 146, 154-157, 166, 167, 187, 222, 223). Ores take 65-69.
  - A species' trunk block IS its wood (you pick up what you dug, Plan P2-P4). Oak, birch, spruce, acacia, jungle and cactus already have a trunk block each.
  - New: redwood wood (shares PineWood today), baobab wood, palm wood, plus 3 fruits = **6 ids, leaving 5**. Bushes, flowers and town materials therefore need a palette review first.
  - Seeds are items: no id. Regrowth = one record per felled tree, growing back the same task 210 library tree.
- **A2 numbers (TASKS 187/190)**: today's Medium island has 3 towns, 1 frontier town and ~10 villages/outposts, with ~1.35 km fair-share spacing.
  - 2-3 cities out of 3 town sites leaves almost no plain towns. The checklist notes the town target (land / 90 k cells) may need to rise; that goes to the towns plan doc.
  - Spacing keeps at most one city inside the 128 m live reach.
- A6 (a)'s option text said "inside your claimed area", which Terraria does not have. I did not decide this; it is question C5.

## Next step
- The supervisor copies C1-C5 to the Questions tab (below). Once they are answered, the town-safety section becomes rows.
- Batch B is still unanswered.

## For the doc
What changed in the Game design doc:
- Status line: "batch A answered 14 Nov (Q-AE); town-safety questions C1-C5 and batch B pending".
- New **design principle** under the pitch: "keep all rules simple and depend on emergent behavior" (A6).
- Core loop: all 3 rows are now Decided. Session row: the world does not change for a long while, and the first flip comes late, like hardmode (A1 b). The pitch and Progression's Stages row say the same.
- Settlements: How many / Size tiers = 2-3 cities per Medium world (A2 c). Your own settlement = Terraria's move-in rule (A6 a). Safety = "as safe as the townspeople keep it" (A5 c). New Leaning row: How residents react to edits.
- New Open section **Town safety by NPC behaviour**: 5 rules (danger map; lights and walls lower it; people lower it; people stay while housed and safe; growth = the same rules), a table of 6 missing pieces, and questions C1-C5.
- Resources: wood / fruit / seeds / regrowth Decided (A4 a). Creatures: Bodies = one humanoid generator (A3 a).
- Placement checklist: a cities item (187/190 numbers), wood + fruit = 6 of the 11 free palette ids, a door/bed/light in every home, a town-safety item before character work.
- Each A1-A6 now has its answer under it.
- No images: plan only, nothing visible changed.

New questions (copy to the Questions tab; the first option is the recommendation):
- **C1. What lowers danger?** (a) Lights, closed rooms and people, each in a radius, on one danger map (~0.55 MB, 1 byte per 8 m cell on Medium; your base is safe once lit and walled). (b) People only. (c) Lights and rooms only, like Terraria (then "as safe as the townspeople keep it" no longer holds).
- **C2. What makes a resident leave?** (a) No valid room (door, bed, light), or home danger above a threshold for a few in-game days (one check per resident per day; wrecking a town empties it, so edits react for free). (b) Only a missing room. (c) Never; only their mood changes.
- **C3. Guards?** (a) Ordinary residents with one more rule: at night, walk between the town's lights and fight what spawns; one per few residents (the streets give the paths). (b) Fixed posts at the gates. (c) No guards.
- **C4. How does a settlement step up to town and city?** (a) By resident count alone, the same for yours and generated ones (e.g. 5 / 15 / 40); each step brings new kinds of NPC (merchant, trainer, guard captain). (b) Count plus a placed hall or charter item. (c) Yours stays a village.
- **C5. Does a settlement need a claimed area?** (A6 (a) said "inside your claimed area"; Terraria has none.) (a) No claim: rooms and residents within ~64 m count as one settlement. (b) A claim marker you place. (c) Generated towns keep their disc; yours needs a claim.
