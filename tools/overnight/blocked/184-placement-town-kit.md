# 184-placement-town-kit

Model: opus

Approval: owner, 2026-11-06: Placement plan APPROVED incl. batch 2 answer W1 (a): a MODULAR KIT first (supervisor queues plan step 3, first stage only; reversible, behind a flag, default OFF: `-towns 1`).
Read `GAME_DESIGN_BRIEF_SNAPSHOT.md` (Towns section), the site plan / reserved town sites (grep `town`, `site`, `Reserve` in `Assets/TerrainGen/`), and how tasks 180/181 add placed content behind a flag.

## Do
- A small kit of voxel building parts: floor/foundation, wall, door gap, window, corner post, pitched roof, plus 3 house shapes assembled from them (1-room hut, 2-room house, a small hall).
  Materials: wood, stone, thatch/shingle from existing Materials only (mapping rule: every new GenMat needs a FarField.GenToEngine entry); style may vary by biome via the biome's palette, by rule.
- Place buildings ONLY at the town / village sites already reserved along the easy areas (about 3 towns + about 10 villages and outposts on a medium island, at least one frontier town).
  If no sites are reserved yet in the record, read how the site plan stores them and say what you found; do not invent a new site planner (STOP_FOR_OWNER if one is needed).
- Buildings only: no residents, no interiors beyond an empty room + door. Each building's FOOTPRINT (integer cell box + id) goes into the world record (brief: NPCs will later need it). If this needs a frozen-API or save-format change: STOP_FOR_OWNER with options.
- Rules read generator data (site flat area, slope, biome); no seed constants; terrain under a building gets a simple foundation to the ground, no stilts/terraces yet (stage 2, later). Checks on 4 seeds + 1 adversarial (steep site).
- CPU stays sole writer; LOD 0, shells and far band agree. Whole suite (generator change) + fast suite. Feature note in TASKS.md. Rough cost (memory per town) from the task's own runs only; no timing arms.
- Shots, full size, ON vs OFF: a village from eye height, a town from 100 m, one interior doorway. One caption each.

## End
Done, default stays OFF. STOP_FOR_OWNER only if a frozen-API/save-format change or a site planner is needed, or the pictures look wrong. HANDOFF with counts per seed and pictures.
