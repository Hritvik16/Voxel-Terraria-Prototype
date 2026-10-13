# 85-engine-12-handover-near-first: DONE. Shell chunks stream nearest-on-screen first (Q47 (a)); shells hand over by distance.
## What changed (commit `1bb7e9e`, branch engine-phase)
- `ShellStreamer.cs`: one request order across all shells. The key is `OrderKey` = max(distance from the camera to the chunk's
  nearest point, the shell's inner hand-over distance) x (1 - 0.25 x cos(angle to the camera's smoothed velocity)), with the plain
  distance breaking ties. The per-shell lists are re-keyed whenever any shell's camera chunk changes, and NextCandidate merges
  their ends. Coarsest-first is gone. Also new: `InFlightIn(k)` and `TakeRequestOrder` (for tests).
- Step 12, shells-on path only (`Raymarch.compute` ShellMarch and its CPU twin `ShellRayReference.March`, new `tTier0` arg): the shells
  march from the camera. Inside tier 0's band, min(its 68.75 m hand-over, where tier 0's march stopped), a chunk tier 0 holds is
  leapt. A chunk it does not hold (non-resident, or outside its window or layers) is read from shell 1, 2, ... The shells path no longer calls `FarStartT`,
  and the far band starts at the shells' end. `RaymarchFeature`: `-detailspeed` is off while shells draw. Shadow rays pass tTier0 = 0 (unchanged).
- Capture: `-poses a,b,c` keeps only the named poses. A `[shells] complete at` line gives the time each shell finished per pose.
- Tests: `ShellOrderTests` (3) and `ShellHandOverTests.TierZerosBand_HeldIsLeapt_NotHeldIsShellOne`. Amendment 8.14 §3 "Order" and row 5,
  plus a CLAUDE.md paragraph.
## Evidence
- Order, fixed camera at spawn, High: 7,508 requests. All 2,170 shell-1 chunks are requested by #2,911; the first shell >= 3 chunk is #3,102.
  The first request is shell 1 (before: all of shell 7 went first). No coarse chunk farther than a shell-1 chunk comes before it (asserted).
- Prefetch margin > 0 on every preset and shell (Screenshots/85/margin85.txt). High: shell 0 20.8 m, shell 1 16.1 m (0.27 s at 60 m/s), s2 32 m,
  s7 1,592 m. Smallest anywhere: Low shell 1, 10.7 m.
- ONE launch (`BUILD=1 MODE=poses EXTRA="-shells 1 -shellworkers 3 -gateres 1280 -shellwait 75 -poses ..."`, Logs/overnight/85-poses.out,
  copied to Screenshots/85/wait75/poses85.txt). Chunks stored/missing at 75 s, 83's wait75 -> 85:
  | pose | s1 | s2 | s3-7 stored |
  | peak_from_2km_ground | 0/2170 -> **1390/775** | 0/2170 -> 219/1951 | 100/586/299/137/16 -> 0 |
  | glide_400m | 0/2170 -> **1212/953** | 0/2001 -> 177/1824 | 567/637/308/139/16 -> 0 |
  | eye_level_1 | 0/2170 -> **1050/1115** | 0/2170 -> 151/2019 | 143/544/308/137/16 -> 0 |
  | cliff_foot_1 | 256/1909 -> **1212/956 FULL** | 494/1671 -> 351/1814 | 593/573/298/143/16 -> 0 |
  Shells 1-2 did not complete within 75 s, before or after. At ~16-19 chunks/s, shell 1 alone (2,170 chunks) needs ~2 min.
  Pixels drawn by s1/s2: peak none -> 3,059/10,705; eye_level_1 none -> 150k/54k. Peak process 2,995 -> 2,999 MB (cap 3 GB). No frame time quoted (rule 10).
- Sheets I looked at:
  - Screenshots/85/wait75/sheet_shells_1.jpg (the 4 poses: OFF / ON / tint).
  - Screenshots/85/wait75/before_after_83_vs_85.jpg, the columns 83 ON | 83 tint | 85 ON | 85 tint:
  - Row 1 (peak): 85's tint has a yellow s2 strip past the blue tier-0 plane, where 83 was grey. Mountains are still grey (far band) in both.
  - Row 3 (eye_level_1): green s1 and yellow s2 bands in the middle distance. 83's frame was all tier 0, but this pose moved after 83 (83b HANDOFF).
  - Rows 2 and 4 (glide, cliff_foot_1): framed differently from 83, so they are not a matched pair. Glide shows no shell pixel either time.
    Cliff_foot_1 is all tier 0 (blue). The ON column shows no holes, seams or floating cells.
  - Caveats: in 83 these poses ran 1st/3rd/10th/13th of 16, with chunks left over from earlier poses; here they ran 1-4.
- Whole suite **PASS 767 FAIL 0 SKIP 21** (Logs/overnight/85-all.out; 83b 763 + 4 new). run-phase7-rig **PASS 37 FAIL 0, 19/19 shots
  byte-identical** to 83b's (Phase7Cycle/2026-10-13_005521 vs 2026-10-12_235030), so the shells-off path is unchanged.
## Found / left (owner's call, not done)
- **glide_400m spends the whole wait on sky.** The camera is ~400 m up, and the s1/s2 boxes (+-153 / +-307 m, all axes) are mostly air chunks.
  Those generate at about the same rate as surface chunks (glide 18.5/s, eye level 16/s). So near-first fills s1-2 with nothing visible,
  and s3-4, which hold the visible ground, get 0. Proposed fix: skip a chunk the column data PROVES is air before it is queued (the
  `DetailField.UniformSamples` max-height test from 82b, done before generation), or rank it last. This changes performance, so it needs
  owner approval.
- s1 FULL at cliff_foot_1 (2 refusals; s1 cap 150k bricks vs ~237k needed, 83's finding). The s3 page cap is 1% over (83b). Neither changed here.
- The shells-off path keeps FarStartT and -detailspeed until shells become the default (a later task, per the plan).
## Next step
Owner: the air-chunk skip above (Q). Then 11c / the default switch per the plan.

## For the doc
- Shell-1 chunks stored after a 75 s wait (83 coarse-first -> 85 near-first): peak 0 -> 1,390; glide 0 -> 1,212; eye_level_1 0 -> 1,050;
  cliff_foot_1 256 -> 1,212 of 2,165 (Logs/overnight/83-poses-wait75.out -> Screenshots/85/wait75/poses85.txt).
- Request order at spawn: the first shell-1 request moves from after all 7,508 - 2,170 coarse chunks to #1. Shell 1 finishes by #2,911 and the
  first shell >= 3 chunk is #3,102 (ShellOrderTests).
- Fill time for shells 1-2: not reached within 75 s, before or after (~2 min needed at ~17 chunks/s).
- Prefetch margin, High: shell 0 20.8 m, shell 1 16.1 m, shell 2 32.2 m. Every preset > 0 (Screenshots/85/margin85.txt).
- `Screenshots/85/wait75/before_after_83_vs_85.jpg`: row 1 (peak) tint column: yellow s2 strip past the blue tier-0 plane in 85, grey
  (far band) in 83. Row 3 (eye_level_1) 85 tint: green s1 and yellow s2 bands in the middle distance.
- `Screenshots/85/wait75/sheet_shells_1.jpg`: the 4 poses after 75 s, OFF / ON / tint. The ON column has no holes or seams.
