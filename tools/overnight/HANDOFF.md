# 83b-engine-11d-shell-fluid-fill: DONE. The shell fill now also fills ENCLOSED fluids; at spawn s3-s7 fit their brick caps.
## What changed (commit `67b84ed`, branch engine-phase)
- `TerrainGenChunkSource.ShellFill.cs`: `ShellFillFluids` on by default (owner Q46 (a)). The flood is unchanged. Fluids are pass
  voxels, so any fluid connected to open air, a cave mouth, an op box, the chunk-face halo, or another liquid's surface
  (task 75 see-through) is reached and kept. Only fluid nothing reaches is filled.
- `ShellFillTests`: Gen/TearDown pin fluids on, so test (i)'s outside rays run with fluids filled. New `SeaAndLake_SurfaceFluid_Unchanged`
  (Slow). The need report now has C = 83 (fluids kept) and D = 83b and writes `need83b.txt`.
- Amendment 8.14 row 13b and the CLAUDE.md task-83 paragraph each got one clause.
## Evidence
- Test (i), fluids filled: same lines as 83: 2.6 M outside rays, 0 differ (Screenshots/83b/fill83_tests_83b.txt). Entrances 11 -> 11,
  0 lost. Dense bricks in the test chunks: s4-s7 39/34/45/61% -> 35/15/16/24% of the generator's.
- Sea/lake test (Screenshots/83b/fill83b_water.txt), shells 3-7, a sea and a lake site, 2 chunks each: 0 fluid voxels that open
  water reaches changed (sea s3 alone has 1.84 M of them), 0 outside rays differ. Enclosed fluid below them was filled: 1-14k voxels/chunk.
- Bricks needed at spawn (Screenshots/83b/need83b.txt): s3-s7 fit their brick caps (table below). **Pages: s3 324 vs cap 320, 1% OVER**
  (83: 657); s4 260, s5 162 now fit. Not changed (no cap changes in this task).
- Poses run, ONE launch (`BUILD=1 MODE=poses EXTRA="-shells 1 -shellworkers 3 -gateres 1280 -shellwait 360"`,
  Logs/overnight/83b-poses.out, shots FarBand/2026-10-12_214720_world). The Mac was on battery; no frame time is quoted.
  - Refusals over the 16 poses, s1-s7: 83 = 18/30/41/41/45/44/6 -> 83b = **2/10/0/0/0/0/0**.
  - Poses where a shell was FULL: 83 = 11/12/12/12/12/13/3 -> 83b = 2/5/0/0/0/0/0.
  - Peak process 2,951 -> **2,985 MB** (cap 3 GB): s3-6 now hold all their chunks.
- **Shells 1-2 starve worse (Q47, KNOWN, not fixed).** s3-7 now take every wanted chunk, and the coarsest-first order spends the
  360 s wait on them. 11 of 16 poses hit the 360 s limit (83: 0). At geyser/eye_level/ledge/cliff_foot, s1 held 0 chunks
  (missing 2,170) and s2 held 0-222.
- Sheets looked at (Screenshots/83b/sheet_shells_1..4.jpg vs Screenshots/83/):
  - Sheet 1: the ON column looks the same as 83's. Summit_view tint (row 2): the grey far-band block at the right horizon is gone
    (the cyan s6 band is unbroken: s6 stores 182/182). Rows 1, 3, 4 same; row 5 (oblique_3000m) is still all far band.
  - Sheet 2 row 2 (geyser_basin, tint): grey rectangles where s1-2 had no chunks yet (Q47). 83's frame was yellow/green there.
    The ON column looks the same, because the far band fills those holes.
  - Sheet 2 row 4: eye_level_1 shows a different view than in 83, in the OFF column too. The pose itself moved; the shells did not cause it.
- oblique_3000m / rivers_oblique: no shell pixel although s5-7 hold 143-180 chunks, and none are full. In 83 this looked like
  FULL shells; it is not that. Unexplained; worth a look in 11c.
- Whole suite **PASS 763 FAIL 0 SKIP 21** (Logs/overnight/83b-all.out). run-phase7-rig **PASS 37 FAIL 0, 19/19 shots byte-identical**
  to 83's (Phase7Cycle/2026-10-12_235030 vs _180337).
## Left / next
- Q47 (s1-2 starve behind s3-7): the owner's call, now more pressing. s3 pages are over cap by 4 (324/320); the fix is a
  1.25x margin, about +1.3 MB. Needs approval.
- Next step: Q47, then 11c / the default switch per the plan.

## For the doc
- Dense bricks at spawn, 83 -> 83b (cap): s3 106k -> 91.6k (102.4k); s4 142k -> 72.2k (102.4k); s5 170k -> 62.3k (102.4k);
  s6 94.0k -> 27.5k (71.7k); s7 13.3k -> 4.2k (17.3k). All fit; before, only s7 did. Screenshots/83b/need83b.txt.
- Refusals over the 16 poses, s1-s7: 18/30/41/41/45/44/6 -> 2/10/0/0/0/0/0 (Logs/overnight/83b-poses.out).
- Pages s3/s4/s5: 657/598/331 -> 324/260/162 (cap 320 each; s3 still 1% over).
- Peak process 2,951 -> 2,985 MB (cap 3 GB).
- Surface fluid: 0 changed voxels, 0 changed outside rays at a sea and a lake, shells 3-7 (Screenshots/83b/fill83b_water.txt).
- `Screenshots/83b/sheet_shells_1.jpg` vs `Screenshots/83/sheet_shells_1.jpg`: in the row 2 (summit_view) tint, the grey block at
  the right horizon is gone (s6 complete). Everything else looks the same, as expected.
- `Screenshots/83b/sheet_shells_2.jpg` row 2 (geyser_basin, tint): grey rectangles are where s1-2 starved (Q47). The ON column
  is unchanged.
