# 200-look-tree-method-examples: DONE (examples only; the world is unchanged). 3 methods x 6 species prototyped at 0.1 m, 16 in-engine poses (24 shots over 2 launches) + CPU sheets, 5 owner questions below.
## What changed (commit 22f2bda)
- `Assets/TerrainGen/TreeGallery.cs` (new): (a) space colonization, (b) L-system / rule branching, (c) template + jitter; species oak, banyan (aerial roots -> prop trunks), redwood (fluted buttress), birch, spruce, acacia. Built ONCE per tree from an integer seed into an 8^3-brick volume, then queried; fills AIR only (Trees.cs's contract).
- `TerrainGenChunkSource.Gallery.cs` (new) + 3 one-line hooks (GatherTrees top, OverlayTrees, PointTree): `-treegallery <layout file>`, absent = OFF, each hook returns at its first line. `Trees.cs` is NOT edited.
- `TreeGalleryTests`: pin, sheets + cost, chunk hook, layout (Explicit). `tools/trees200.py` (sheets + viewer). TASKS.md paragraph.
## Evidence
- Fast suite PASS 1011 FAIL 0 SKIP 21. Filtered (Slow): TodayTrees_Pinned, Methods_Species_SheetsCost, Gallery_Chunk_AirOnly, TreeTests.Chunks_Deterministic_Border_PointAndShellsAgree all PASS.
- Today unchanged: Trees.Build over seed 42's tree cells [512,576)x[256,320) = 1,663 trees, FNV 38EF6100C34F1928 (`Logs/TerrainGen/trees200/today_pin.txt`).
- Hook exact: gallery ON vs OFF over 64 chunks round an oak: 147,366 voxels changed = 147,366 expected (air -> GalleryAt), 0 wrong (`chunktest.txt`).
- Launches: 1 (16 poses, `Logs/overnight/200/player.log`) + 1 reshoot (8 poses, `player2.log`); ~3 min per pose (fresh site streams), so ~55 + 31 min -- over the 10 min budget.
  Run 1's finder put the oak block on a town pad and banyans on snow; run 2 excludes towns / snow tops and picks each species' biome.
  Stills used: run 2 for oak space-col (m/s/c), banyan space-col (m/s), acacia; run 1 for the rest. Two run-2 stills were worse and are NOT used:
  `engine/run2_m_banyan_lsystem.png` (a 3 m bank right in front of the camera hides the trees) and `engine/run2_s_redwood_spacecol.png`
  (only 2 of 3 redwoods drawn, crown thinned: the GPU body pool was FULL, 254,294 of 254,400 in use; all 3 were built, player2.log).
- GPU body pool full in both launches (peak 254,400 / 254,400; 57,639 refusals over run 1) -- as since 195a, plus the gallery's big trees.
## Not done / limits
- Lay of the Land devlog not watched (no video access). No owner doc / artifact (unattended rule); images are cited by path.
- Birch bark uses `MushroomStem` as a pale stand-in (a real birch needs a new GenMat + GenToEngine mapping). Redwood uses PineWood/PineNeedles.
- No timing arms (rule 10): Build() ms are micro-counts (Editor Mono, and the player's own build log line).
## Next step
Supervisor brings Q-T5..Q-T9 to the owner. If Q-T5 (a): a world-ready space-colonization task (Burst build, per-tree cache, brick budget) behind a flag.

## For the doc
- Result: 3 growing methods, 6 species, in-engine at 0.1 m. **Recommended: (a) space colonization** -- crowns read as real oaks / banyans / redwoods, with sky between leaf clumps.
- Size + cost per tree (SC, 3 seeds, `Logs/TerrainGen/trees200/cost.txt`; today = mean of 30 forest broadleaves, `today_pin.txt`):
  today 9.8 m tall, 57.6 k voxels, **266 bricks** | oak 17-20 m x 19-25 m wide, 128-162 k vox, **2,300-2,800 bricks** | banyan 18-21 m x 34-42 m, 0.74-0.93 M vox, **7,500-10,000** |
  redwood 66-69 m x 12-16 m, 0.72-0.77 M vox, **4,200-5,000** | spruce 21-24 m, 57-86 k, 660-1,070 | birch 14-17 m, 15-26 k, 330-510 | acacia 7-9 m x 15-17 m, 19-30 k, 470-650.
  A brick = a 512-byte body on CPU and GPU, so memory follows bricks: one oak ~ 9-11x today's tree (mostly its 2x size), one banyan ~ 28-38x.
- Build() per tree (one thread): player (IL2CPP) oak ~55 ms, banyan 200-290, redwood ~170, small species 8-35; Editor Mono 5-8x slower. Today's Trees.Build: microseconds (no volume).
  Primitives: today 21-28; SC oak ~1,000-1,350 segments + 180-220 leaf clumps; banyan ~1,750-2,400 + 300-425 + 34-40 aerial roots (11-18 prop trunks).
- What giants change (today: one tree per 6.4 m cell, reach 6.3 m, height 24 m, trees drawn to shell 5 = 2.2 km): a banyan needs ~21 m reach (gather 9x9 cells per chunk column instead of 4x4)
  and a redwood ~70 m height (MAX_HEIGHT 240 -> 720 voxels; STAMP_RISE_BOUND 40 m -> 72 m); a redwood spans 6 chunk layers. Past 2.2 km nothing draws trees, so a 66 m grove would pop in;
  twigs (1-3 voxels) vanish from shell 1 (0.2 m) on, clumps last to shell 3. Proposal: giants on their own sparse grid (e.g. 51.2 m cells), normal trees unchanged.
- Images (`Screenshots/200/`, full size + flip in `viewer.html`: arrows, Space = today's tree at the same framing, Z = 100 %):
  - `sheet_methods.jpg` -- rows oak / banyan, columns (a) (b) (c), bottom-left today's trees, same camera each tile: look at the crowns -- (a) has irregular open crowns, (c) is "lollipops".
  - `sheet_species.jpg` -- (a) for oak, banyan, redwood (top row), birch, spruce, acacia (middle), today (bottom); the red post by the middle tree is 1.8 m: compare heights. (Redwood tile: run 1, a town on the right; acacia stands on forest grass.)
  - `sheet_closeup.jpg` -- left: an oak crown, sky visible between leaf clumps and the branches showing; right: today's crown at the same framing, two solid blobs.
  - `proto_banyan.png` / `proto_redwood.png` -- CPU side views, rows (a) (b) (c), 3 seeds: banyan aerial roots and prop trunks; redwood buttress flare and the bare lower trunk.
- Reference photos (links, not re-hosted): banyan https://en.wikipedia.org/wiki/The_Great_Banyan | coast redwood https://en.wikipedia.org/wiki/Sequoia_sempervirens |
  oak https://en.wikipedia.org/wiki/Quercus_robur | birch https://en.wikipedia.org/wiki/Betula_pendula | spruce https://en.wikipedia.org/wiki/Picea_abies |
  acacia https://en.wikipedia.org/wiki/Vachellia_tortilis | style: Lay of the Land devlog youtube.com/@tooley1998 (not watched here).
- **Questions (recommendation first):**
  - Q-T5 Method: **(a) space colonization + a species trunk/limb stage** (natural crowns, gaps by construction) | (b) L-system (tidier, more regular) | (c) template (closest to today).
  - Q-T6 Species, 2-3 per biome: **(a) forest oak + birch (+ redwood groves), jungle banyan + 192's emergent/palm, savanna acacia + 192's baobab, snow spruce + 192's pine, desert 192's cactus (+ oasis palm)** | (b) another list. There is no rain-forest biome: redwoods need a home (wet forest cells, or a new biome).
  - Q-T7 Giants: **(a) real size, rare landmarks on their own sparse grid (banyan ~40 m wide, redwood ~65-70 m)** | (b) scaled down (redwood 30-40 m, banyan ~25 m wide) | (c) keep every tree within 8-20 m (your 2026-11-06 answer).
  - Q-T8 Leaves (close-up): **(a) as shown** | (b) more open | (c) denser.
  - Q-T9 Memory (GPU pool already full): **(a) next task: world-ready (a) with a brick budget per area -- fewer, bigger trees -- and measure** | (b) keep today's tree sizes (8-12 m) with the new method (est. ~1.5-2x today's bricks, not measured) | (c) raise the pool (costs RAM toward the 3 GB cap).
