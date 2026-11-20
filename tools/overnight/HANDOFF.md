STOP_FOR_OWNER -- F3 (solid crown cores) does NOT do what the plan expected. Built and tested behind `-crowncore 1|2` (DEFAULT OFF, play unchanged).
Q-243: (a) drop F3, go on with F4 (recommended: the literal rule does nothing, the variant that works is a big look change and crashed the player);
(b) keep the closed-crown look (`-crowncore 2`): next task = find the shell-fill crash, then a forest cost split; (c) leave it parked.

# 243-forest-solid-crown-cores: implemented (switch OFF), FINDING = crowns are nearly all skin; mode 2 changes the look a lot + player crash

## What changed (commit: see `git log -1`, "243:")
- `Assets/TerrainGen/CrownCore.cs` (new): the fill, ONCE per library prototype on the library's worker threads (bit planes; no per-chunk/frame cost).
  Rule from the crown's own voxels: hull = enclosed along x, y, z by the crown (mode 1) or by its closing with a box of the crown's own clump
  radius (mode 2); core = hull minus a 2-voxel skin; only AIR -> LEAF, only connected to the grown crown, only in bricks the prototype already has.
- `Trees2.BuildProto(.., core)`, `ProtoBuild.placeBricks` (placement unchanged), `TerrainSpec.crownCore`, `-crowncore` / `SetCrownCore(byte)`,
  `Trees2Library.Acquire(core)/SetCoreMode/CoreMode`. Map v4 only (v3 has no trees -> untouched). `-wshours H` for the world survey (it forced noon).
- Tests: `CrownCoreTests` (adversarial shapes incl. tiny / all-skin / bowl / hollow shell / 400x80x400 lens, brute-force oracle, closing gaps,
  real prototypes OFF byte-identical); `CrownCoreWorldTests` (world: same placement, only air -> leaf; REPORTS=1 histogram). KNOWN_ISSUES + TASKS.md.

## Evidence
- Histogram (`Logs/TerrainGen/trees243/histogram.txt`, forest_eye 82 m footprint, dense trees): OFF 13,036 leaf bricks, 0 fully solid, 62 % under
  10 % filled. Mode 1: 13,037 / 0 solid (the crowns are small flat clumps with sky between: nothing is "inside"). Mode 2: 14,222 / 313 solid (2.2 %).
- Memory, launches (`Screenshots/243/table243.txt`, noon, OFF -> mode 2): forest_eye tree GPU bodies 93,081 -> 92,291; oak_under refusals 20-21 -> 19;
  sealed 86,839 -> 87,473; process peak 2,559-2,631 -> 2,566 MB; CPU pool peak ~same; library 10.6 MB both, build ~0.9-1.4 s both. Within budget.
  A first mode-2 build that also filled pure-interior bricks was OVER budget (prototype bricks +21 %, tree bodies +4.5 %, refusals 21 -> 35) -> fixed.
- CRASH: 3 of 3 launches of the final mode-2 build died in the Burst `ShellFillJob` (null read, shell worker; KNOWN_ISSUES top entry, .ips paths
  there). Not reproduced in EditMode (450 shell chunks). OFF launches of the same build: fine (off_noonB, off_16).
- `SUITE=all ./run-editmode-tests.sh`: PASS 1198 FAIL 0 SKIP 36, wall 45m43s (`Logs/overnight/243_all.out`; the fast set is a subset). No frame-time arms (rule 10). Pass card: no GPU pass added or changed (content only).
- Slice (rule 14): tier A forest at 16 deg, plan guess -10..-25 ms -- UNMEASURED; memory: 0 MB (within the forest slice; nothing taken).
- P4: trees / instances identical ON vs OFF (same placement, test-proven); tree bodies -0.8 % at forest_eye.

## Pictures looked at (full size: `Screenshots/243/viewer.html`; arrows = pose, Space = OFF / ON / 4x diff, S = sun, Z = full size)
- forest_eye: from outside the crowns become solid blobs (sky gaps through crowns closed, smoother outline); diff is crowns + birch trunks only.
- oak_under: OFF = canopy with many sky holes; ON = an almost closed black ceiling (mean luma 20.7 -> 8.3 at noon).
- oak_inside: OFF = loose clumps and limbs; ON = dark leaf walls with some FLAT 0.8 m brick faces (where the fill stops at a brick edge).

## For the doc
Task 243 -- solid crown cores (F3): built behind a switch, OFF; the plan's premise does not hold.
- Fully solid leaf bricks at forest_eye: 0 today -> 0 (rule as written) -> 313 of 14,222 (2.2 %, "closed crown"), `Logs/TerrainGen/trees243/histogram.txt`
- GPU memory, closed crown vs OFF: tree bodies 93,081 -> 92,291; refusals 21 -> 19; peak 2,571 -> 2,566 MB (`Screenshots/243/table243.txt`)
- Frame time: not measured (rule 10). Player crash with the closed crown: 3 of 3 launches (shell fill), unresolved.
- `Screenshots/243/sheet243.jpg`: rows = forest_eye / oak_under / oak_inside at noon, then forest_eye / oak_under at 16 deg; columns OFF | ON | 4x
  difference. Look at the middle column's crowns (solid, no sky through them) and row 2 (the canopy above you turns into a black ceiling).
- `Screenshots/243/oak_inside_c2_noon.png`: inside an oak with the closed crown -- note the flat square leaf walls (brick edges).
- Question: approve solid crown cores as default? Recommended NO (Q-243 (a) at the top: drop F3, go to F4).
