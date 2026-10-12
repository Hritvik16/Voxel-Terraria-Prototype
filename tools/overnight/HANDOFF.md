# 77-engine-11a-shell-storage: DONE. Task 74's Q1/Q2 applied; shell storage + upload built, validators 0, rigs unchanged. One owner question (not blocking 11b)
## Question for the owner (needed before shell pool caps are final; 11b can start without it)
**Q1. The coarse shells hold ~1.5-3.4x the dense bricks 8.14 estimated, because of caves underground.** At seed 42's spawn, shell 6 needs
~194k dense bricks (71.6k stored + 122.7k refused) against an estimate of 57k (cap 71.7k), and shell 7 needs ~20k against 13.8k. In the
player run shells 3, 4 and 5 also filled their caps (~102k each) and refused more. Of the stored dense bricks, 71% contain air (caves,
caverns) and 29% are material-only (ores, strata). Refusing is safe (row 15: the coarser shell shows; stale 0), but distant land goes missing.
(a, recommended) In shells >= 3, fill enclosed underground air and flatten material-only bricks to one material (caves are not visible
from 275 m+). This breaks row 12's "coarse voxel == generator voxel" below the surface only; one amendment row plus a test.
(b) Raise the caps to the measured peaks: shells cost ~2-3x 8.14's 388 MB (est.), above the 3 GB cap. (c) Keep the caps and accept holes.
## What changed
- `3aeca7e` Q1 (a) + Q2 (a) (supervisor defaults): `GenerateShellChunk` samples fine index 2^(k-1)-1, and a column's top solid coarse voxel
  takes the material of the fine voxel at the ground height h. Amendment 8.14 row 13 amended. `ShellChunkTests` stay exact (0 differ, now
  including 1,213-1,849 top voxels per shell), and the agreement report gains a "surface vs 0.1 m" column.
- `e72835d` Step 11a: `ShellLayout.From(preset)` (ContentModules/Config), `ShellStore` / `ShellStreamer` / `ShellValidator` /
  `ShellDebugView` (CoreEngine/Mirror, namespace VoxelEngine.Mirror), `ShellStorageTests`. Two-level directory (ABSENT / UNIFORM|mat /
  PAGE|page; 16 KB pages; 512 B bodies in BrickDataPool layout), zeroed at construction, refuses when full, budgeted multi-frame upload
  (directory entry published last), dedicated workers, main-thread upload inside what tier 0 leaves of the 3 MiB cap.
  Phase4Bootstrapper: `-shells 1` (tg2 only, **OFF by default**: nothing draws shells before 11b, and the pools would sit beside the
  cascades they replace, +549 MB peak), `-quality low|medium|high|ultra` (default high), `-shellworkers N`, `-shellsdebug S`.
- `2837be2` Fixed a starvation found in the player run: a full coarse shell kept regenerating its refused chunks while shells 1-4 got
  nothing. Now a refused chunk waits until its shell has room, and a full shell yields the workers to the finer shells.
## Evidence
- SUITE=all **PASS 736 FAIL 0 SKIP 19** (Logs/overnight/77-all.out, after the generator change); fast after the last fix **698/0/14**
  (Logs/overnight/77-fast2.out). ShellStorageTests 4/4, including the Slow fill (Logs/Shells/fill.txt).
- Validators: EditMode (High shells 6-7, real generator, two camera legs): stale slots 0, pool errors 0, uploads byte-exact, every update
  inside its 1 MiB budget. Player, 3 runs, all 7 shells: stale 0, pool errors 0, **GPU directory readback mismatches 0**, over-budget
  frames 0, generation errors 0 (Logs/Shells/player_shells_*.txt).
- Memory A/B (same build, same pose run): peak 2,823 -> 3,372 MB; Metal (IOAccelerator) 1,042 -> 1,408 MB (+366 MB, the shells allocate
  372 MB); native "Untagged" +193 MB, of which ~45 MB is accounted for (Logs/tg2world_20261012_013704 vs _011934, *_mem_peak_breakdown.txt).
- Rigs: `run-phase7-rig.sh` **PASS 37 FAIL 0, 19/19 shots byte-identical** to 75b's run (Phase7Cycle/2026-10-11_233444 vs 2026-10-12_015234).
  Acceptance rig **53 PASS / 1 FAIL**: the known flaky steady-state upload p99 (1.24 ms vs 1.0; KNOWN_ISSUES "Gate C is red on ONE
  upload-budget check"). Previous run (2026-10-04) had 52/2 on the same check. No frame time quoted. I looked at Traverse_49: grass, a
  sand band, water, three hills, a clean horizon.
## Not done / next
- The shells never filled completely in the pose run: ~440-640 ms per shell chunk with 2 workers beside tier 0, and thousands of chunks
  per pose (steps 15/16). Occupancy bits / air mips for shells come with 11b. Shell 0 is still the 128 m window (step 14).
  PlaySettings / F6 / auto-pick (rows 16-17) are not done. While a shell is full its cheap uniform chunks wait too.
- Next: 11b (raymarch + seams + the "largest voxel <= 1 px" pose metric), ideally after Q1.
## For the doc
- Top-voxel colour vs the real 0.1 m surface, shells 1-4 pooled: **coarse 99.99 / 99.37 / 98.88 / 97.81%**. The majority vote itself
  reaches only 96.2 / 91.8 / 73.5 / 56.1%, so 74's "vs vote" line (96 / 76 / 60 / 55% -> now 96 / 91 / 72 / 54%) measured the wrong
  reference past shell 2 (Logs/TerrainGen/coarse_first.txt vs coarse_first_74.txt). Q2: pooled shell 1 voxels 97.2 -> 98.5%, heights 99.0 -> 99.6%.
- Memory per shell, High, as allocated (bodies + pages + directory): 82 / 66 / 58 / 58 / 58 / 40 / 10 MB = **372 MB**; 8.14's formula
  gives 383 MB against §H.2's 388. Per preset, shells 1+: Low 166 / Medium 274 / High 383 / Ultra 674 MB vs 169 / 277 / 388 / 677
  (Logs/Shells/ledger.txt).
- Dense bricks needed vs estimated, seed 42 spawn: shell 6 **~194k vs 57k**, shell 7 ~20k vs 14k (Logs/Shells/fill.txt from `e72835d`'s run).
- Player peak footprint with shells on: 2,823 -> 3,372 MB (+366 MB Metal), off by default.
- `Screenshots/77/77_river_plains.png`: bottom row (shell 3). The vote's top view (left) shows blue-grey bare patches; the coarse top view
  (second) keeps the light snow colour. Task 74's version (Screenshots/74/74_river_plains.png) showed it the other way round.
- `Screenshots/77/shells_occupancy_leg0.png`: High shells 6 (left) and 7 (right) around the spawn, one cell per chunk column. Grey = the
  hole the finer shell covers (the camera is outlined in it), green = stored, red = refused (pool full). The red edge is the shortfall in Q1.
- `Screenshots/77/player_shells_t90.png`: the player run 90 s in, shells 1-7 from left to right. Shells 1-4 are yellow (still missing).
  In shells 6-7 the stored green ring is around an earlier pose; the camera (outlined) has moved on.
