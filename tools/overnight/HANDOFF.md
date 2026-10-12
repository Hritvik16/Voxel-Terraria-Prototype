# 80-engine-11b-shell-raymarch: DONE. Shells 1-7 draw behind `-shells 1` (default OFF), with distance hand-over, absent -> coarser, seams fixed; rigs unchanged
(77's Q1, the pool caps, is still with the owner. It is now visible in the frames, see "Not done".)
## What changed (commit `2f6ca67`, branch engine-phase)
- `Raymarch.compute` `ShellMarch` (+ `ShellResolve/Lookup/ChunkState`): marches shells 1..N from ONE shared directory/page/brick buffer.
  Each shell gets a base offset (`ShellStore.SharedBuffers`; 3 buffers, because 21 would pass Metal's 31-buffer limit). Count, rings,
  published boxes, holes and bounds come from a constant buffer sized for 8 (row 1). It starts where tier 0 ends or a column is non-resident
  (`FarStartT`). Rules: by distance; absent/refused -> coarser (Q2 (a), row 15); hole -> finer; tier 0's space is leapt unless tier 0 is
  missing there; far band only past the last shell or where no shell has the chunk (row 5). Never coarser mid-cell; a start inside a coarse
  solid cell is a TOP-face hit: this is the seam fix, no LOD side-face dark arcs.
  Shell hits get a shell sun ray (ignores solid cells within 3 of its own cells: the coarser-shell self-shadow). Tier-0 AO and the sky probe
  are not read for shell hits (they darkened the sea past 128 m). The river lip (136) is drawn as water.
- `ShellRayReference.cs` = the CPU twin. `ShellHandOverTests` (5): every boundary of High's 7 shells, 4 azimuths; absent -> next stored shell
  -> far band; past the last -> far band (level ray: 33 iterations, cap 384); tier-0 space leapt / tier-0 gap -> far band; seam top face.
- `RaymarchFeature`: `DrawShells` (on when a streamer is Active), `ShellTint`, `ShellIterCap` (`-shelliters`). Cascade tiers 1-2 do NOT
  draw while shells draw (row 2), but they stay ALLOCATED: freeing them would break the one-launch OFF/ON comparison. Free them when the
  default switches. ShadowStats 8-39 = per-shell px metric. `Phase4Bootstrapper`: `ShellStreamer.Active`; `-shellsdebug` also tints.
- `PlaygroundCapture.FarBand`: with `-shells 1`, `MODE=poses` shoots each pose at noon OFF/ON/tint after waiting up to `-shellwait` s
  (default 75) for the shells to fill. `tools/shells80-sheet.py` makes the sheets. CLAUDE.md Commands has one new paragraph.
## Evidence
- Fast EditMode **PASS 703 FAIL 0 SKIP 14** (Logs/overnight/80-fast.out); filtered ShellHandOver 5/5 + ShellStorage 4/4 (incl. Slow fill).
- `run-phase7-rig.sh` on the FINAL shader: **PASS 37 FAIL 0, 19/19 shots byte-identical** to 77's run (Phase7Cycle/2026-10-12_051216 vs
  _015234). Same after the first shader version (_035824). Build: 0 Shader error.
- Player, 2 launches (the 2nd re-shot after fixing 3 defects the 1st showed): validators shells 1-7 stale 0, pool errors 0, GPU directory
  mismatches 0 (shared buffers + bases), gen errors 0 (Screenshots/80/shells_occupancy_quit.txt). Peak footprint 2,959 MB with shells AND
  cascades allocated (Logs/tg2world_20261012_043323_mem.txt). No frame time measured (rule 10).
- Looked at all 16 poses x 3 (sheets below). Shells ON matches OFF in layout everywhere, with more detail at 140 m-2 km.
  - Gone: the dark arcs (cascade seams) on the snow at peak_from_2km and on the sea at coast_from_sea_30m.
  - Seam / colour jumps found: (1) a vertical light/dark split down the far peak at peak_from_2km: shell 6 left, far band right. The chunk is
    refused (FULL) and the far band shades differently. (2) Chunk-shaped patches where a refused chunk falls back to the next shell
    (glide_400m, geyser_basin tint). (3) cliff_foot_2: a dark straight-edged wedge on the ridge. The straight edge is tier 0's 89.6 m band
    ceiling; above it shell 1 draws the backlit cliff in shadow, nearer than 69 m (2.5 px). This is vertical coverage (11c/14), not a seam.
  - Missing land: none. The far band fills every absent chunk. At oblique_3000m / map_7000m / rivers_oblique the shells draw NOTHING:
    shells 6-7 are FULL with the chunks of the first poses (their box covers the whole island, so nothing is ever evicted).
- **BUGS #2 (ledge crescent): not reproduced.** Today's `ledge_view` finds a different ledge: the whole frame is a tier-0 wall (921,600 of
  921,600 px tier 0), with no crescent OFF or ON. Shells never draw in that frame, so 11b can neither cause nor fix it there. Leave
  later/40 open until the B5 pose is re-found.
## Not done / next
- Occupancy bits / air mips for shells: not built. Uniform chunks and bricks leap whole; no iteration-cap overrun was seen in the shots.
  Build them in the perf step if shell cost shows.
- **Q1 is now visible in the frames.** Shells 1-2 run out of PAGES first (320/320 pages, only 91k/150k and 119k/119k bricks); 3-7 run out
  of bricks. Final refusals s1-s7 = 24/36/41/50/48/55/43 (missing waiting: 1674/1711/1891/790/330/90/0). Fixing the caps is the owner's
  Q1, plus `PAGES_PER_COLUMN` for shells 1-2.
- Rivers in coarse shells show as broken blue specks, not lines (narrow channels at 0.8-3.2 m voxels). One unexplained small dark mark
  (a "?" shape) on the snow at glide_400m in ON.
- Next: the owner's Q1, then the default switch + retire cascade tiers 1-2 (frees their memory), and 11c (vertical / shell 0).
## For the doc
- Largest voxel on screen, max px over the 16 poses at 720 rows (screen-centre angle): **s1 0.97** (2.53 only where shell 1 draws above tier
  0's band), **s2 2.14, s3 2.12, s4 3.69, s5 4.48, s6 4.50, s7 3.14**. 8.14 §1's adopted rule A puts the next shell's voxel at **2 px** at its
  inner bound: the plan row's "<= 1 px" is rule B's wording. Shells 1-3 meet 2 px; 4-7 exceed it where refused chunks fall back to the next
  coarser shell (Screenshots/80/farpreview.txt `[shells]` lines).
- Refusals per shell (run total) s1-s7: 24 / 36 / 41 / 50 / 48 / 55 / 43; FULL at almost every pose (pages for s1-2, bricks for s3-7).
- Phase 7 rig 37/0, 19/19 byte-identical with shells OFF. Peak footprint 2,959 MB with shells + cascades both allocated.
- `Screenshots/80/sheet_shells_1.jpg`: rows peak_from_2km, summit_view, glide_400m, coast_from_sea_30m, oblique_3000m; columns OFF / ON /
  tint (tier 0 blue, shells 1-7 green, yellow, orange, red, magenta, cyan, white; far band grey). Row 1: compare the snow plain's dark arc in
  OFF (gone in ON) and the peak's left/right split. Row 4: compare the black arcs on the sea in OFF (gone in ON). Row 5: all grey, far band.
- `Screenshots/80/sheet_shells_3.jpg`: rows eye_level_3, ledge_view, cliff_foot_1-3. Row 2 is BUGS #2's pose: all tier 0, no crescent.
  Row 4: the dark wedge over the ridge in ON, above tier 0's band (green in the tint).
- `Screenshots/80/coast_seam_off_run1_run2.png`: the sea at the hand-over. Top: OFF, a black arc. Middle: the first launch, a darker strip
  (tier-0 AO / sky probe read at a shell hit). Bottom: the final, flat.
