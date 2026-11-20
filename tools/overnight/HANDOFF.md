# 247-cached-sun-shadow-design: DONE -- `AMENDMENT_8_18_CACHED_SUN_SHADOW.md` (DRAFT, NOT ADOPTED); owner question Q-247 (not blocking the queue)

## What changed (commit 96f2f17; docs only)
- `AMENDMENT_8_18_CACHED_SUN_SHADOW.md` (new): representation, build, edits, lookup, precision traps, budget, look, prototype tasks 247a-d,
  risks + kill tests, spec change table. `TASKS.md`: Task 247 paragraph.
- Numbering: 246 called its clutter layer "8.18 draft" in passing (no file); this takes 8.18 per the task file, clutter becomes 8.19.

## Design in one paragraph
Sun-space "run maps": one line per texel traced FROM the sun side through the tier-0 mirror, storing the first solid run (entry, exit or a
16-texel cap; 2 x 16 bit). Lookup per lit face = §6.5's origin cell (unchanged, no bias) + an exact contact ray of <= 8 cells + 4 taps; a
texel says shadow only if the point is past the run's exit, so a face on its own surface is lit (no acne) and 228's slope trap is closed for
slopes > 3.6 deg off grazing. Maps N0-N3 (512^2, 0.05/0.1/0.2/0.4 m, to 102 m), far map F (= task 133's continuation), step 2 shell maps S4-S9
(= task 80's ray). 3 slots + crossfade as the sun moves (periods 16-64 frames: staleness < 1/3 of today's soft spread), CPU-marked demand tiles,
edits re-trace ~700 lines per changed brick in the SAME frame (mirror upload -> build -> lookup).

## Evidence (no launches, no timing: plan only)
- Inputs read: ARCHITECTURE §6.5 + C.10 + 8.2; 8.12, 8.14, 8.17; PERF_SLICE_BUDGETS sec. 2/6/7; PLAN_PATH_TO_33MS S1-S7; TASKS 56/124/228/232/
  244/245/246; code: TraceShadowRay, SunTraceFace, sky-fill bounce leg, TerrainClipmap.UploadDirty, SunCycle/-daylength 1200.
- Counts used (244, `Logs/overnight/244/costsplit_16.txt`, `costsplit_noon.txt`): forest_eye 16 deg sun 153.6 k rays, 9.26 M steps (47 % in
  shells); noon 155.0 k, 4.42 M; walk_near 16 deg 83.4 k, 2.36 M. Times from 235/233 (sun 54.7 / 18.2 / 12.9 ms).
- Memory arithmetic (est.): step 1 13.4 MB, with shell maps 27.0 MB; frees the 8.0 MB sun history at 432 rows -> net +5.4 / +19.0 MB.
- No EditMode run (no code changed). No images (nothing drawn).
- Rule 11/12/14: no pass added yet; the doc carries the future pass card (CSSunCacheBuild: fixed resolution, S2 step, 5.0 ms share) and the
  tier-A slice (sun shadows 5.0 ms; memory from headroom). HEAVY: no.

## Next
Owner answers Q-247. If yes: queue 247a (CPU twin + oracle sweep, EditMode only, runs while the owner works), then 247b (one timed
`suncontact` kill arm, idle Mac ~45 min), then 247c / 247d. Not queued by me: they need the adoption.

## For the doc
Task 247 -- a new way to compute sunlight (draft amendment 8.18). Plain words: today every lit pixel sends its own ray to the sun every frame;
in a forest at dawn those rays creep through leaves and cost 54.7 of 135 ms. The new way traces lines from the sun side a few times a second
into small maps, and each pixel then does a tiny exact ray (<= 0.8 m) plus four map reads. Cutting leaves updates the map in the same frame.
Shadows stay per voxel face; leaf dapples stay full strength within ~13 m; long dawn shadows stay. It does not finish the forest alone
(est. ~79-93 ms after it) but removes two give-ups: capping long low-sun shadows and the bounce sun ray.

| pose | sun today | sun with cache (guess) | other |
|---|---|---|---|
| forest_eye 16 deg | 54.7 ms | 8-16 ms | sky fill bounce -4..-10 ms (guess) |
| forest_eye noon | 18.2 ms | 5-9 ms | |
| walk_near 16 deg | 12.9 ms | 6-11 ms | little gain (short rays today) |
| torch night | 0 | 0 | cache idle |
| GPU memory | -- | +13.4 MB (step 1) / +27.0 MB (step 2) | minus 8.0 MB sun history (432 rows) |
| one edited brick | -- | ~700 lines, 0.1-0.4 ms, same frame | |

Diagram: the ASCII figure in sec. 0 of `AMENDMENT_8_18_CACHED_SUN_SHADOW.md` (sun lines through a crown; contact ray from the face; map lookup).
Images: none (plan only; nothing was drawn).

**Q-247: adopt amendment 8.18 and build the prototype?** Recommendation: **yes, as a staged trial** -- 247a (correctness, EditMode, no idle
Mac) and 247b (one ~45 min overnight timed arm) decide it before any GPU code; if 247b's contact-only arm does not save, stop and put the
effort into F3 (solid crown cores) and splitting the forest's unexplained 55 ms "rest".
