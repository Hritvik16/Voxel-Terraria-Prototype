# 200-look-tree-method-examples

Model: opus

Approval: OWNER, chat 2026-11-13 (trees talk, `tools/overnight/OWNER_TOPICS.md` topic 1). Owner's words: today's trees
"look like a child drew them, not natural at all, and the leaves don't really have any space between them". Answers:
- T1: still 0.1 m cubes, but CLOSER TO REAL TREES, e.g. BANYAN and MASSIVE REDWOOD. 5-6 tree types, or more if the biomes
  need them; each type recognizable, each tree unique (procedural).
- T2: "I'd need to see examples of what the end products would look like" -> THIS TASK: examples, before choosing a method.
- T3 (a): 2-3 species per biome, each with its own settings, varied tree by tree.
- T4 (a): trees first; bushes / grass / flowers later.

**EXAMPLES ONLY. The world does not change:** `-trees` default and today's `Trees.cs` output stay byte for byte (a test
pins it). The prototype lives beside it, reached only by a new capture flag (e.g. `-treegallery 1`, default OFF), which
places prototype trees on a flat, open test patch for pictures. No default flips, no world-gen rule change.

## Do
1. Read `Trees.cs` (header: a tree is a pure function of seed + its 6.4 m cell; `Build()` makes primitives; consumers ask
   `Contains()` per voxel; trees overwrite air only; shells 1-5 sample the same) and TASKS.md tasks 180 / 192. Any method
   must fit that contract (built once per tree from an integer hash, then queried) or the doc must say what it would change.
2. Prototype 2-3 GROWING METHODS at 0.1 m: (a) space colonization (branches grow toward attraction points filling a
   crown envelope; leaf clumps at branch tips with air gaps), (b) an L-system / rule-based branching, (c) optionally a
   template + variation baseline. Plus what makes giants work: banyan aerial roots dropping to the ground and thickening into
   extra trunks; redwood trunk taper, buttress, high narrow crown.
3. **Pictures (owner must SEE the end product):** in-engine stills, midday, sun behind the camera, native 1080, the same
   framing per sheet:
   - METHOD sheet: the same two species (one broadleaf e.g. oak, one banyan) by each method, 3 trees each.
   - SPECIES sheet with the method you recommend: 5-6 species chosen for the biomes the world has (`Regions.cs` /
     biome list; e.g. banyan for jungle, redwood for temperate rain forest, oak, birch/aspen, pine/spruce, acacia or palm
     for dry / coast), 3 instances each (unique but recognizable), a person-height marker for scale.
   - A close-up of one crown showing the leaf gaps, and today's tree (`-trees 1` default world) at the same framing.
   - Reference photos LINKED (not re-hosted) per species; Lay of the Land's trees as the style reference (owner, 13 Nov).
   - `viewer.html` for full-size flip / zoom (owner rule 2026-11-01).
4. Cost per species (numbers, no timing arms -- rule 10): voxels per tree (trunk / leaf), primitives per tree, `Build()`
   CPU cost from an EditMode micro-count, and the memory effect (the near GPU body pool is FULL since 195a). Say plainly
   what a 60-90 m redwood means for the 6.4 m tree cell, chunk / shell reach and LOD (the owner's 2026-11-06 answer was
   "Terraria-sized 8-20 m"; massive redwoods change that -- a question, not a decision).
5. Write the owner questions into HANDOFF `## For the doc` (method choice, species list, giant sizes, recommendation first,
   what each costs). No STOP_FOR_OWNER: questions travel through HANDOFF.

- Fast suite once at the end (new code). TASKS.md paragraph. Launch budget applies; reuse one session of launches.

## End
Done with HANDOFF + pictures + questions.
