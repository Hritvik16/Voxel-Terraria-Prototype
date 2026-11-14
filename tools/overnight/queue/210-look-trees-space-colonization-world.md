# 210-look-trees-space-colonization-world

Model: opus

Approval: OWNER, 2026-11-14, Queue decisions doc Q-AD answer "a for all": Q-AD1 (a) space colonization; Q-AD2 (a) species list
below; Q-AD3 (a) giants at real size as rare landmarks on their own sparse grid; Q-AD4 (a) leaves as task 200 showed; Q-AD5 (a)
make (a) world-ready with a memory budget per area (fewer, bigger trees) and measure it.
Owner's words with the answer: "Lets be careful how much we increase the memory cap by. We still need it to at least play on
this mac at low settings 30 fps." So memory is a HARD limit here, not a nice-to-have.

## Background (task 200 HANDOFF, commits 22f2bda / 29dccc6)
- `Assets/TerrainGen/TreeGallery.cs`: method (a) space colonization plus a trunk / limb stage per species. It is built once per tree
  from an integer seed into an 8^3-brick volume, then queried, and fills AIR only (Trees.cs's contract). Gallery hooks are in
  `TerrainGenChunkSource.Gallery.cs` (`-treegallery <layout>`).
- Bricks per tree: today 266; oak 2,300-2,800; banyan 7,500-10,000; redwood 4,200-5,000; spruce 660-1,070; birch 330-510;
  acacia 470-650 (`Logs/TerrainGen/trees200/cost.txt`). A brick = a 512-byte body on the CPU AND the GPU.
- Build() per tree in the player (IL2CPP, one thread): oak ~55 ms, banyan 200-290, redwood ~170, small species 8-35.
- The GPU body pool is FULL today (peak 254,400 / 254,400 in both 200 launches). 195a's memory peak: 2,504 of 3,000 MB.
- Giants: a banyan needs ~21 m reach (gather 9x9 cells instead of 4x4); a redwood ~70 m height (MAX_HEIGHT 240 -> 720 voxels,
  STAMP_RISE_BOUND 40 -> 72 m) and spans 6 chunk layers. Trees are drawn to shell 5 (2.2 km). Twigs vanish from shell 1 on.

## Species (Q-AD2 (a))
forest: oak + birch (+ rare redwood groves in the wettest forest cells; there is no rain-forest biome) | jungle: banyan + 192's
emergent and palm | savanna: acacia + 192's baobab | snow: spruce + 192's pine | desert: 192's cactus (+ oasis palm near water).
Birch bark: add a real pale-bark GenMat with its `FarField.GenToEngine` mapping (KNOWN_ISSUES: every new GenMat needs one).

## Do
1. Behind a flag `-trees2 1` (absent = today's trees, byte for byte: keep 200's pin test `TodayTrees_Pinned` green).
2. Make method (a) world-ready: build trees on WORKER threads (Burst where it pays), cache the built volume per tree seed so
   chunks share it, and keep the main thread the single writer (CLAUDE.md). Deterministic across chunk borders and runs:
   `TreeTests.Chunks_Deterministic_Border_PointAndShellsAgree` style test for the new path.
3. Memory budget, the hard part. Pick the density so that, with `-trees2 1`, (i) the 195a memory peak (2,504 MB) grows by
   at most ~150 MB, and the total stays under 3,000 MB; (ii) GPU body pool refusals do not grow against `-trees2 0` at the same
   poses; (iii) NO pool is made bigger. Fewer, bigger trees: a brick budget per area (e.g. per 51.2 m cell). Giants go on
   their own sparse grid as rare landmarks. Report the budget numbers you chose and why.
4. Pictures: forest, jungle, savanna, snow and desert at eye height and from ~100 m, `-trees2 1` vs `-trees2 0`, same camera,
   midday, sun behind the camera. Also one redwood grove and one banyan from far away (shell 3-5 range: do they pop in?).
   Plus a full-size flip viewer (`viewer.html`, as 200).
5. Numbers: memory peak, GPU pool peak and refusals, CPU pool peak and valve evictions, generation time per far chunk, all
   vs `-trees2 0`, from the launches you already do (no timing arms, rule 10; a rough cost line only).
- Fast suite before and after; WHOLE suite before done (`Assets/TerrainGen/` change). TASKS.md paragraph.
- STOP_FOR_OWNER if the budget in step 3 can only be met with trees so sparse that a forest no longer reads as a forest.
  Show pictures of the best density that fits and the numbers for one step denser.

## End
Done with HANDOFF + `## For the doc` (memory table, 2-3 pictures with captions naming what to look at). Default stays OFF:
turning it on is the owner's call after the pictures.
