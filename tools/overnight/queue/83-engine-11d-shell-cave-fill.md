# 83-engine-11d-shell-cave-fill

Approval: owner answered **Q44 (a)** in the Queue decisions doc, 2026-10-12 (task 77's Q1: "the coarse shells need
1.5-3.4x the estimated bricks because of caves"). Queued by the supervisor 2026-10-12. Branch `engine-phase`.
This amends Amendment 8.14 (one new contract row) -- approved by that answer.

## The problem (77 + 80 HANDOFFs)
Shell pools fill with UNDERGROUND bricks nobody can see from far away: at seed 42's spawn shell 6 needs ~194k dense
bricks vs 57k estimated (cap 71.7k); shells 3-7 run out of BRICKS, shells 1-2 run out of PAGES first (320/320 pages,
`PAGES_PER_COLUMN`). Of stored dense bricks 71% contain air (caves), 29% are material-only (ores, strata). Refused
chunks fall back to the far band: 80's sheets show a light/dark split on far peaks, chunk-shaped patches, and at
oblique_3000m the shells draw nothing (shells 6-7 full of chunks from earlier poses; their box covers the whole
island so nothing is evicted).

## Change (owner's (a))
1. In `GenerateShellChunk`, for shells k >= 3 (275 m+ on High; use the layout's shell index, every preset alike):
   - **Fill enclosed underground air:** air that is not connected to the sky/surface (caves, caverns) becomes solid.
     Keep air that is open to the outside (cave mouths on cliff faces, overhangs, canyons, anything a ray from outside
     could reach). Define "enclosed" cheaply and conservatively (e.g. below the column's ground height h by more than
     a margin of one shell voxel, and not reachable from a non-enclosed neighbour within the chunk); when unsure, keep
     the air. Water/fluid voxels are never filled.
   - **Flatten material-only bricks:** a brick that is all solid but mixed material, and NOT the surface (no air or
     fluid neighbour in the brick's 6-neighbourhood at shell resolution), becomes uniform with its majority material,
     so it costs no dense brick.
   - The surface itself is untouched: row 13's top-voxel colour and the sample rule stay exact above the fill depth.
   - Far edits (81's ops) are applied BEFORE the fill, so a far crater or tunnel opening to the sky stays open.
2. Shells 1-2: raise `PAGES_PER_COLUMN` (or the page cap) so pages are no longer the first limit; size it from the
   measured peak x 1.25 (row 1's rule).
3. Add the new contract row to `AMENDMENT_8_14_LOD_SHELLS.md` (row 12/13 note): "below the surface, shells >= 3 are
   NOT equal to the generator: enclosed air filled, interior mixed bricks flattened (owner Q44 (a), 2026-10-12)".
   Update `ShellChunkTests` so exactness holds at and above the surface, and a new test proves: (i) no visible surface
   voxel changes (compare a top-down and 4 side-view raycasts of a shell chunk before/after the fill: 0 differ),
   (ii) open cave mouths stay open, (iii) dense brick count drops.
- CPU sole writer; workers write scratch they own; pow2 rings; bitwise integer coordinates; no frozen API change.

## Acceptance
- Dense bricks needed per shell at seed 42's spawn, before -> after (77's `Logs/Shells/fill.txt` method); refusals per
  shell over the 16 poses (80's `[shells]` lines), before -> after. Target: shells 3-7 fit their caps at every pose.
- Fast EditMode green; `SUITE=all` once at the end (generator path).
- `run-phase7-rig.sh` result unchanged and byte-identical (shells off in rigs). No frame-time measurement.
- **Images (one player launch, `-shells 1`):** 80's pose sheets again (`MODE=poses`, `tools/shells80-sheet.py`) --
  peak_from_2km, glide_400m, oblique_3000m at least -- OFF / ON / tint, next to 80's sheet. Look at them: the peak
  split and the patches should be gone where shells now fit; say what remains.
- If eviction (shells 6-7 never evict because their box covers the island) still leaves a shell full after the fill,
  say so with numbers; do not redesign eviction in this task.
- HANDOFF `## For the doc`: bricks before -> after per shell, refusals before -> after, memory per shell, the sheet.
