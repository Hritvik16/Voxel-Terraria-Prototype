# 203-look-fluid-pocket-natural-outlines: DONE. Every surface pond (all 7 fluids) has an irregular outline now, default ON; plus an IL2CPP-only bug was found and fixed.

Commit `ff5eda9` (FluidPockets.cs, TerrainGenChunkSource.Pockets.cs, FluidPocketTests.cs, TASKS.md, KNOWN_ISSUES.md, Screenshots/203 text + viewer).
The session was resumed after a usage-limit stop; the first round of shots (before the bug fix) was thrown away.

## What changed
- **Rule** (`FluidPockets` header "TASK 203"): the footprint is the blob G = sum of 3-4 lobes > 0.05. Lobes are scaled copies of the site's ellipse.
  Lobe 1 points to the LOWEST of the 16 rim samples the planner already takes. That part comes from the terrain: the pond reaches toward low
  ground and pulls back from the high side. Lobes 2-3 sit on its flanks, with sizes and angles hashed from the site's own centre. There are no
  terrain constants, and every fluid and biome uses the same rule. Every lobe stays inside the old ellipse and its box, so 183/196's
  containment holds: the plane-cell lookup, the rim and the bank rings. The bed shelves at 196's 30-degree slope, the rim sits flush at L+1,
  and the banks work as in 196. Ponds are at rest by construction.
- **Flags:** `-pocketoutline 0` gives 196's plane byte for byte (FNV 5A0794B7B9ECA569, seed 42, in EditMode AND the player).
  `-fluidpockets 0` is unchanged (Apply is never reached).
- **Why this is the efficient design (rule 11):** it adds no storage (5 bits in the existing plane words). Each column recomputes the shape
  from an integer hash on the worker (Burst). The plane build is still once per world on the main thread: 68-72 ms in the player.
- **Bug (KNOWN_ISSUES, new entry):** in `Pack`, a `?:` inside the word-3 expression compiled under IL2CPP to a `uint8_t` temp. In the PLAYER
  only, rx, rz and depth became 0, so every pond turned into a 0-radius pit inside its bank, in both arms. EditMode (Mono/Burst) was correct,
  so all tests passed. Found by a plane fingerprint in the player log, confirmed in the generated C++, and fixed by using plain statements.
  The `[FluidPockets] plane ...` log line now ends `plane fnv <hex>`: it must equal the EditMode value for the same seed.

## Evidence
- Tests: new `Outline_Rules` (fast). Over 600 random sites: the lobes stay in the box, each pond is one 4-connected piece, rings nest, the bed
  step is <= 2 voxels (2 on 150 of 977,849 neighbour pairs), the pond reaches toward the low side on 599/600, outline off = the ellipse, and
  Pack/Decode round-trips (`Logs/TerrainGen/outline203/shapes.txt`). The existing at-rest/adversarial tests also pass (forced 20-degree
  slope, canyon, cliff). **SUITE=all PASS 1097 FAIL 0 SKIP 30** (`Logs/overnight/203/suite_all.out`).
- Player fingerprint (seed 42): before = 5A0794B7B9ECA569, after = 72CEA9A36AECA569, both equal to EditMode (`Logs/overnight/203/player_{before,after}.log`).
- Memory (rule 11, from the same launches, after vs before): peak 2,482 -> 2,498 MB (+16 MB, within +50). GPU body pool peak 240,501 -> 240,721,
  refused 0 -> 0. CPU pool peak 489,484 -> 490,196 (valve 484,500, unchanged behaviour). `Screenshots/203/lookreview_{before,after}.txt`.
- Shapes vs the old ellipse: area 55 % on average (differs on 45 % of it).

## Gameplay consequence (said plainly)
Ponds are smaller: about 55 % of the old ellipse's area, with less fluid in each. Trees can now grow on ground the ellipse used to take. In
seed 42's jungle, the honey pond is hidden under a crown in the top view (`top_honey_after.png`).

## Not done / next
- The outlines are smooth blobs (clover, kidney or pear shapes), not ragged shorelines. If the owner wants rougher shores, a follow-up could
  add a small noise term to G (same containment). That is an owner decision, so no task was queued for it.
- Shots took 2 launches of 9 poses (~18 min each, longer than the 10-min budget; the task asked for one view per fluid). Shell 2+ was still
  filling at 35 s (see the `[shells]` lines), which is far terrain only.

## For the doc
**Result:** every surface pond (water, lava, oil, acid, sludge, honey, quicksand) now has a natural, irregular outline. It is built from 3-4
overlapping lobes, one leaning toward the lowest ground around the site, and keeps 196's terraced banks. On by default; `-pocketoutline 0` = old ellipse.
- Pond area vs the old ellipse: 100 % -> 55 % (mean of 600 sites; `Logs/TerrainGen/outline203/shapes.txt`).
- Leans downhill: 599 / 600 ponds reach farther toward the low side.
- Memory peak 2,482 -> 2,498 MB; GPU pool refusals 0 -> 0 (`Screenshots/203/lookreview_{before,after}.txt`).
- Tests: SUITE=all PASS 1097 FAIL 0 (`Logs/overnight/203/suite_all.out`).
- Bug found on the way: the player (IL2CPP) built 0-radius ponds from the same code that EditMode built correctly. Fixed, logged in KNOWN_ISSUES.
Images (`Screenshots/203/viewer.html`: Space flips before/after, arrows change pose, Z = full size):
- `Screenshots/203/sheet_203.jpg`: rows = water top, lava top, lava side, oil top, acid top, quicksand top; LEFT = before (ellipse), RIGHT =
  after. In each centre, the round or oval pond becomes a lumpy blob (clover-like water, pear-shaped lava, lobed oil and quicksand).
- `Screenshots/203/top_water_{before,after}.png`: from 22 m above. Look at the dark pond in the middle: a perfect circle -> a clover shape,
  with the same terraced snow bank around it.
- `Screenshots/203/cpu_top_forced_slope_lava.png`: CPU top view of the adversarial 20-degree-slope site. The pond (dark orange) sits inside
  the old ellipse (white dots), with a graded bank around it.
