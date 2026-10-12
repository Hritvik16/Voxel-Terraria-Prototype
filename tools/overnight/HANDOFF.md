# 75b-see-through-cost: DONE -- see-through at the lake shore +5.0 ms -> +2.0 ms (target <= 2.5 met); commit `59c55cb` (engine-phase)
## What changed
- `Raymarch.compute`: the march through a fluid now stops where the bed's weight (1 - F) * T falls under **10%** (`SEE_CUT`,
  was 2%). `SeeCutFade` fades the bed out over the last 30% of that path, so the earlier stop leaves no edge on a sloping bed.
  The far band uses the same cut, so near and far still match. Step cap (192), table, blend: unchanged.
- Diagnostic arms kept: `see_noblend / see_nofar / see_nomarch` (`RaymarchFeature.DebugSeeSkip`, cost split only).
  The three candidate arms were measured once and then removed.
- Note: a previous session of this task ran the split (19:30) and the heat stills, then hit the usage limit; this session went on from there.
## Split (step 1; warm-up Logs/tg2world_20261011_192327.txt, Logs/costsplit_2026-10-11_193015.txt, lake_shore, p50)
| arm | p50 ms | vs OFF |
|---|---|---|
| OFF (5 runs) | 62.3 | -- |
| see_on | 67.1 | +5.0 |
| no end blend | 67.0 | +4.7 -> blend ~0.1-0.3 ms |
| no far-band blend | 67.2 | +4.9 -> far ~0 |
| no march (blend on the surface) | 62.2 | -0.1 -> **the march is all of it** |
## Candidates (step 2). Expected before measuring vs measured (warm-up Logs/tg2world_20261011_223335.txt, Logs/costsplit_2026-10-11_224010.txt)
- step cap 192 -> 64: expected 1-2 ms; **saved 0.0** (no ray uses more than 64 extra steps).
- coarser tier past 3 m / 6 m in the fluid (like shadow travel LOD): expected 1.5-2.5 ms; **2.9 ms WORSE** (+8.5).
- cut 2% -> 5%: expected ~1 ms; **saved 1.0** (+4.6). So the cost follows the path length. The pick was 10% + fade (expected ~2 ms saved).
## After (step 3; warm-up Logs/tg2world_20261011_231039.txt, Logs/costsplit_2026-10-11_231717.txt, ABBA)
- lake_shore: OFF 52.3, ON 54.2 / 54.0 -> **+1.9 / +2.2 ms** (before: +5.0 split run, +5.6 candidate run).
- lake_away (no liquid in view): -0.1 / 0.0. **lake_wall: +0.1 / +0.1, but there is no water in view any more**: after task 76 the
  lakes are at rest and the standing wall at task 75's first pose is gone (still `.../UpscalerClips/2026-10-11_224010/stills/lake_wall_see_on_clean.png`
  shows dry sand terraces and rock). The wall-of-water case can't be measured at this pose now.
- Caveat: OFF itself was different in each launch (62.3 / 58.1 / 52.3 ms), so compare the gaps measured within a launch.
  As a share of the frame: 8-10% -> 3.6-4.2%.
## Checks
- Fast EditMode **PASS 695 FAIL 0 SKIP 14** (Logs/overnight/75b-tests.out).
- `run-phase7-rig.sh` (setting off) **PASS 37 FAIL 0**, **19/19 shots pixel-identical** to task 75's run (Phase7Cycle/2026-10-11_134515 vs _233444).
- `-liquiddemo` re-shot ON (Logs/overnight/75b-demo.out; shots Screenshots/75b/shots/). Looked at both sheets: the same stripes
  show in every pool (water all five, acid/honey only the shallow ones, as in 75); lake shallows and bank the same.
## Not done / next
- Next candidate if more is wanted (not done, one change only): march a uniform-fluid run at the brick level without re-entering
  the outer loop (chain leaps through uniform water bricks, as `_TraversalMode == 2` does for air), or a cut on luminance instead of
  the clearest channel (the green channel sets the path today). Both need their own task.
- A new wall-of-water pose (a stepped river or a placed water box) if the owner wants that case measured.
## For the doc
- See-through cost at the lake shore, p50 warm ABBA: **+5.0 ms -> +2.0 ms** (+1.9 / +2.2; Logs/costsplit_2026-10-11_193015.txt -> Logs/costsplit_2026-10-11_231717.txt).
- Split of the +5.0 ms: march through the fluid ~5.0, end blend ~0.1-0.3, far-band blend ~0 (Logs/costsplit_2026-10-11_193015.txt).
- Candidates: step cap 64 saved 0.0 ms; coarser tier cost +2.9 ms; cut 5% saved 1.0 ms (Logs/costsplit_2026-10-11_224010.txt).
- No liquid in view: 0.0 ms before and after. Phase 7 rig 37/37, shots identical. Warm-ups: Logs/tg2world_20261011_192327.txt, _223335.txt, _231039.txt.
- `Screenshots/75b/sheet_pools_before_after.jpg`: left = task 75 (cut 2%), right = 75b (cut 10% + fade). Compare the stripe rows in
  each pool (rows 1-4, middle pool): the same stripes show in both columns, from the water pool's deepest to honey's shallow two.
- `Screenshots/75b/sheet_lake_before_after.jpg`: row 1 (shore shallows): the sand bank fading into the blue looks the same in both columns.
  Rows 2-4 differ only where task 76 changed the rivers (cascade instead of slab staircase), not because of this change.
