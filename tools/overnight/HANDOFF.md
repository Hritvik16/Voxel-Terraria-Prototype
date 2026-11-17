STOP_FOR_OWNER -- Q (task 228): coarse far sun shadows save little (2 / 6 / ~0 ms) and, as built, draw false shadows on steep slopes.
Pick one: (a) RECOMMENDED: drop "coarse far" and cut the NEAR sun work next, the way 227 cut sky fill (one sun ray per 4x4 block,
shared by same-plane faces). Today's sun cost is 18-30 ms per pose and at least 2/3 of it is near receivers. (b) Fix the guard (coarse only
after ~27 m of ray travel, where the soft-shadow edge is already a brick wide) and re-measure: no false shadows, but probably less than
the 2-6 ms saved here. (c) Leave it: the switch stays OFF (`-sunfar 0`), play unchanged.

# 228-perf-sun-shadow-coarse-far: DONE as an experiment, switch OFF; commits 442ff28 (code + tests), 70a947e (runner, plan, reports)

## What changed
- `-sunfar <m>` (default 0 = off; presets unchanged). Applies to a sun ray whose receiver is >= m from the camera. Once the ray has climbed
  two bricks above its origin, a DENSE brick counts as solid: one directory read, no per-voxel steps. Covers tier 0 (`TraceShadowRay`),
  task 133's continuation and the shell-hit sun ray (`ShellMarch`; new last arg `coarseY`, `SunCoarseY`). Near receivers, the sky probe,
  primary marches and CSSunDeferLean pass `SUN_COARSE_NEVER` = the old path. The cascade tiers are not drawn under shells, so the only
  coarse copy without new memory is the brick directory itself. No frozen API or layout change.
- Capture arms `sunfar_off` / `sunfarNN`; run line prints `sunfar N m`. CPU twin `ShadowRayReference.Trace(coarseY)` + `SunFarCoarseTests`.
## Evidence
- Fast suite `PASS 1081 FAIL 0 SKIP 25` (`Logs/overnight/228/fast.out`; +5 new tests). Twin proofs: off / near receivers exact; the rule
  only adds occlusion; bumpy flat ground (sun 15/30/78 deg) has no self-shadow; ADVERSARIAL thin pole: exact 1 voxel, coarse stays inside the pole's
  brick (<= 8); tall wall: coarse keeps the whole exact shadow and widens it by <= 8 voxels. On sparse canopy-like blobs coarse added a lot
  (67 exact hits -> 604): sparse leaves become solid bricks.
- Timing, WARM steady state (warm-up `Logs/tg2world_20261117_071854.txt`), one cost split, ABBA (F, then X X F), noon, High, 432 rows,
  play flags (`Logs/overnight/228/costsplit_day.txt`; plan written first: `Logs/overnight/228/PLAN.md`). p50 ms:
  pose          exact (before)  coarse>=32 m   coarse>=64 m   sun off   -> sun cost now / saved by coarse 32
  walk_near         72.0           70.0           71.5          49.3     -> 22.7 / 2.0
  forest_eye       102.0           95.9           97.8          72.0     -> 30.0 / 6.1
  jungle_eye        66.8         ~66.2 (one pass 74.7)  66.2    48.9     -> 18.0 / ~0.6 (noise)
  Per the plan: not the >= 5 ms at walk_near that would make it the recommendation. 64 m keeps only 28 % (walk) / 69 % (forest) of 32 m's saving.
- Pictures (`Logs/overnight/228/diff.txt`, mean|d| / px>32 % vs exact): forest clean 2.20/0.58 (floor 2.06/0.36), sunvis 6.92/4.48;
  jungle clean 1.71/0.09; walk_near sunvis 15.3/9.4. The bottom third (nearest ground) is unchanged in forest/jungle sunvis (0.20 / 0.05).
  DEFECT: on walk_near's bare steep hillside a two-brick vertical climb is not enough. The ray is still inside the slope's surface bricks, which
  gives rows of brick-shaped dark triangles where the exact path is fully lit. In the forest, far interiors lose their sun patches and a trunk's
  lit side goes dark. 64 m shows the same defect, only farther out. Part of the ms saved is probably these false early hits.
- Rule 11/12: P1 0 bytes added (one float uniform). Process peak 2,639.0 MB vs 227's 2,564.9 (+74 MB). This is a different launch; the OFF
  arm ran in the same process, and nothing is allocated, so I read it as launch-to-launch spread, not this change. The supervisor's budget
  check can confirm. GPU refusals 0 / 0 / 4,797 (227: 0 / 0 / 5,787). CPU pool peak 496,530 (227: 497,865). P2: no new pass (the primary
  kernel's sun block, 8.17 step S2). P5: no counter in the stripped build. Not HEAVY.
## Left / next
- Not committed (not mine): `Assets/Scenes/Playground.unity`, `Logs/TerrainGen/*` edits. Images git-ignored (`Screenshots/228/`).
- If (a): a 227-style low-res sun task (the 4x4 share rule, the history record, and SkyFillReference's tests are the template).

## For the doc
- Coarse far sun shadows (`-sunfar 32`, OFF by default): walk_near 72.0 -> 70.0 ms, forest_eye 102.0 -> 95.9, jungle_eye 66.8 -> ~66.2
  (warm, ABBA, noon; `Logs/overnight/228/costsplit_day.txt`).
- What sun shadows cost now: 22.7 / 30.0 / 18.0 ms (walk / forest / jungle, exact minus sun off). Far receivers are only 2-6 ms of it.
- Memory: 0 bytes added.
- `Screenshots/228/sheets/walk_far_hill.png`: the far bare hillside at walk_near. Left = exact, right = coarse 32 m; top = picture,
  bottom = sun visibility (white lit, black shadow). Look at the right half: rows of dark triangles on a slope that is fully lit on the left.
- `Screenshots/228/sheets/sheet_sunvis.png`: rows walk_near / forest / jungle; columns exact | coarse 32 m | coarse 64 m | sun off.
  Row 1, columns 2-3 show the triangle field. Rows 2-3 change only in the middle distance.
- `Screenshots/228/sheets/zoom_forest_eye_clean.png`: forest at 3x, exact | coarse 32 m | |d| x4. The far forest floor loses its sun
  patches and the big trunk's lit side goes dark.
