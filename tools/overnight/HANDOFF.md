# 136-qa-black-shape-cliff2: DONE (capture + diagnosis only). The black shape is the VERTICAL SKY PROBE: tier-0 rock under an overhang, out of the sun, gets ~0 light.

## What changed
- `Assets/Game/PlaygroundCapture.BlackShape.cs` (new, capture-only): `-lookreview -cliffshadow 1 -blackshape N`. Hook in `PlaygroundCapture.CliffShadow.cs`.
- `tools/blackshape136.py` (the sheet). `TASKS.md`: one paragraph. `Screenshots/136/blackshape136.txt`: the CPU probe log.
- No product code, default or look change.

## Evidence
- Run: `Logs/lookreview_20261024_181746.txt`. Shots in `LookReview/2026-10-24_182151_high/` (`bs_*.png`).
  - Shells: s1-s2 full at A. s3-s7 were still filling after the 90 s cap (1,592 missing); the tier-0 window was complete.
  - A reproduces task 135 exactly: 35,538 pure-black pixels (max channel <= 4). The other 5,709 px that differ are the unfilled far shells.
- **Who drew them** (debug view 14 decoded): 100% tier-0 HITS, at 85-98 m. The window edge toward the sun is 108 m away.
  - So they are not misses, not out-of-box, and not shells.
- **CPU probe** on the ChunkStore, column x = 1780 of A:
  - Every black pixel is a rock face, n = (0,0,-1), with rock straight above its face cell, **23-29 m up** (`BLOCKED 25.9 m up`).
  - Every grey wall pixel just above the "teeth" is `open to the window top`.
  - Both groups face away from the sun (sun ray blocked at 0.1-2 m).
  - The black faces sit 2-4 m deeper (z 52925-52941 vs 52902-52914): it is an undercut under a rock ledge.
- **Shading path** (`Raymarch.compute`, look 1): a blocked vertical probe gives skyVis = 0.
  - Then `skyOpen = _UndergroundAmbientFloor` (0.02-0.06). That multiplies BOTH the ambient and the shadow floor.
  - So a face that is sun-shadowed AND has rock straight overhead goes to ~0, RGB (1,1,2).
- **Proof arm** (`nosky` = `UseSkyProbeAmbient` off): black pixels drop 35,538 -> 189 at A and 210,605 -> 317 at close.
  - The face then shades like the wall around it (sheet panel 4).
- **Noon** (sun 78 deg): still 35,345 black px. The ledge also blocks a near-vertical sun. Not a low-sun effect.
- **Other overhang** (cliff2's stone arches, 38 m, shadow side): 93,696 black px. The inside of every arch span is pure black; `nosky` -> 0 px.
- Looked at: bs_A_native, bs_A_nosky, bs_close_native, bs_side_native, bs_other_native, and the sheet.
  - The side view shows the black band under the overhanging ledge. The arches are black on their inner undersides.

## How often a player sees it
- Wherever tier 0 (within ~100 m) draws a face that has solid rock anywhere straight above it in the 0.1 m window AND is out of the sun.
  - Examples: undercut cliff feet, arch and bridge undersides, ledges, cave mouths.
- Any time of day. Common in arch/cliff landform regions, and very visible: pure black next to grey rock.
- It flips to grey as the window moves past (the probe only sees tier 0), so it can also pop.
- Shells never show it: they skip the sky probe.

## Proposed fix (owner's call; not done)
- (a) **Recommended: tilt the probe out of the face.** Use dir = normalize(up + n_horizontal) for side faces and a horizontal ray for down faces.
  - An undercut 2-4 m deep with its ledge 25 m up then sees sky. Sealed caves stay dark.
  - Same ray count, but no longer axis-aligned, so air-mip leaps shrink. **Estimate +0.3-1 ms** at play res (not measured).
  - The CPU oracle and the sky-probe tests (`ShadowRayReference`, dir (0,1,0)) need the same direction.
- (b) Average the vertical ray and the tilted ray: a softer result. **Estimate +1-2 ms** (one extra ray per tier-0 hit).
- (c) Free but cruder: never let a sky-probe-blocked face fall below the plain shadow floor when the occluder is > N m up. Caves with high ceilings would brighten.
- Phase 7 already flagged the probe "FLAGGED, NOT DECIDED" (the comment above the probe in `Raymarch.compute`). Any fix needs a look check at cave mouths.

## Unfinished / next
- Nothing open in this task. The next step is the owner's choice of fix. Its look check reuses `-blackshape 2` (A/close/other, `nosky` = target look).

## For the doc
| what | value |
|---|---|
| black px at cliff2 A, native 1080 | 35,538 (same as 135) -> 189 with the vertical sky probe off |
| black px, 15 m closer | 210,605 -> 317 (probe off) |
| black px at noon (sun 78 deg) | 35,345 (unchanged: not a low-sun effect) |
| black px, stone arches 38 m (2nd overhang) | 93,696 -> 0 (probe off) |
| what drew them | 100% tier-0 hits at 85-98 m; rock 23-29 m straight above each face (`Screenshots/136/blackshape136.txt`) |

- `Screenshots/136/blackshape136.png`: six panels.
  - Top row: (1) cliff2 A in the play look at native 1080. The red box at the right is the black shape at the foot of the near cliff. (2) That box zoomed: pure black under a jagged ledge line. (3) Debug view 14: the box is all orange = tier-0 hits, not sky or shells.
  - Bottom row: (4) the same view with the vertical sky probe off: the box is ordinary grey rock. (5) Side view from 39 m: the black is the undercut beneath an overhanging ledge. (6) A second overhang, the stone arches from 38 m: the inside of every arch is black too.
- Cause in plain words: the game decides "is this rock under open sky?" with ONE ray straight up. Under a ledge or an arch that ray hits rock, so the game treats the face as deep underground and gives it almost no light. When the sun cannot reach it either, it turns black.
