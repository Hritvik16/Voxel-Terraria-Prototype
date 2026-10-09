# B5 REVIEW PACK -- 34-B5-signature-features: DONE. Every acceptance line passes on seeds 42/7/2026/31337, SUITE=all PASS 645 FAIL 0 SKIP 15. The canyons are SHALLOW (see "For the owner").

## Images (all in `Screenshots/B5/`)
- `features_four_seeds.png`: the feature map per seed, drawn over the region-class map. Footprints: white = great peak, black = volcano, red = canyon, brown = mesa, cyan = crater lake, yellow = B4 easy areas. Check that the five footprints per seed are spread out and that none touches yellow.
- `feature_view_four_seeds.png`: the 3 km visibility test on a 128 m grid. Green dots see a feature, red dots see none. Note that the red dots cluster on the outer coasts, e.g. seed 42's west and north-west.
- `features_seed42_shots.png`: each seed-42 feature from a 2 m eye on the exact ground (`ground directly below: CPU store top == map`). Panels:
  [1] peak from 2.0 km (top-left): mid-distance, drawn by the far band -- this is the far-band shot (no dry vantage 3+ km out). [2]/[3] canyons from their rims: no canyon reads. [4] mesa from 1.5 km: the grey flat top by the shore, seen from above. [5] mesa from 2.25/2.0 km: a small dark block on the horizon. Dusk light (every flysweep).
- `canyons_seed42_aerial.png`: the two seed-42 canyons from 120 m above the rim, looking 30 deg down.
  Left [2]: a short brown-striped trench in green ground. Right [3]: a river valley beside a red cliff wall. Neither looks "great".
- `seed42_v3_B2_B5_maps.png`: v3 | B2 | B5, top row region class, bottom row slope bands.
  B5: one peak cone where B2 had two (seed 42 drew no volcano); red canyon cuts west and east in the slope bands; a smoothed green easy area on the west.
- `walk_B2_vs_B5.png`: B2 (left) vs now (right) at the three walk spots, 2 m eye, 5 deg down.
  Plains and canyonlands unchanged. Alpine 3948,3932: B5's peak now stands there (ground 1021 m vs 662 m), so the eye overlooks the island instead of a lake.
- `poses_v4_b5_sheet2.png`: `MODE=poses` v4, noon, after the summit_view fix.
  No holes, no floating terrain; coast frame ~60% sea. `summit_view` renders now (first run black: eye 7 m inside B5's peak, fixed here).
  - **`ledge_view` (row 2, second panel) shows a solid black crescent of rock.** The band view says voxels draw it, not the far band. The map has no fin there (0 cells anywhere on seed 42 stand > 30 m above both neighbours). So it is a lighting/render artefact on a tall wall near canyon [3]. NOT investigated.
- Tables: `slope_table_v3_B2_B3b_B5.md` (full), `b5_features_four_seeds.txt` (the PlayMetrics B5 report: features, spacing, visibility, hashes, build time).

| seed | median slope v3 / B2 / B5 | land <15 deg | >45 deg | open | easy ground | gentle/broken/steep med (B5) | easy areas |
|---|---|---|---|---|---|---|---|
| 42 | 30.1 / 19.8 / 14.3 | 29.5 / 42.5 / 51.7% | 29.1 / 15.1 / 11.9% | 4.1 / 11.2 / 11.3% | 16.4 / 28.2 / 36.5% | 9 / 21 / 34 | 10.0%, 4 |
| 7 | 27.8 / 20.8 / 13.0 | 25.6 / 39.4 / 55.7% | 23.8 / 14.9 / 14.7% | 1.3 / 8.8 / 11.3% | 15.3 / 27.6 / 37.6% | 9 / 18 / 38 | 10.0%, 4 |
| 2026 | 26.3 / 20.0 / 13.3 | 32.1 / 37.9 / 55.4% | 22.9 / 9.7 / 8.9% | 4.0 / 5.1 / 5.9% | 20.9 / 24.3 / 38.8% | 10 / 20 / 29 | 10.0%, 4 |
| 31337 | 28.9 / 20.7 / 13.9 | 26.8 / 34.4 / 54.1% | 24.9 / 7.2 / 7.4% | 2.0 / 6.6 / 7.2% | 13.1 / 23.9 / 35.9% | 11 / 19 / 28 | 10.0%, 4 |

## For the owner (questions, not a stop)
1. **The canyons are shallow.** Canyon depth below the higher rim, max / median, in metres:
   seed 42: 61/9 and 159/27; seed 7: 160/35 and 140/71; seed 2026: 35/2 and 45/34; seed 31337: 54/35.
   The rule "every world has a way down" is met on paper, but seed 2026's first canyon is a 2 m ditch at the median. A canyon head can start on 70 m ground, which lets some canyons run through lowland. A 100 m head minimum plus scoring upland paths was tried. It left seed 2026 with no canyon and broke B4's easy areas on seeds 42 and 31337, so it was reverted (`CANYON_HEAD_MIN` comment, `Logs/overnight/b5/filter18.out`).
   Options: (a) accept for now and deepen canyons in a later step, (b) add a minimum depth and let the canyon pick a higher head, or relax B4, (c) wait for sinkholes and sky caverns as the way down.
2. The `ledge_view` black crescent: queue a bug task?

## What changed
- `005815e` (earlier session): pool draw (Common 3 : Uncommon 1, Medium 5, >= 1 canyon, 1.2 km spacing), placement scored on line of sight and accepted only when B4 still seats its areas, record list + `FLAG_FEATURE`/site id; v4's pinned peak/volcano gone; v3 unchanged.
- This commit (generated terrain unchanged, same hashes): `Features.CanyonDepth`, named `CANYON_HEAD_MIN` (still 700); PlayMetrics rim shots + depth; FlySweep stands on the map ground when its start height is inside rock; FarBand `summit_view` eye clears B5's peak; the `Screenshots/B5/` pack.

## Evidence
- SUITE=all **PASS 645 FAIL 0 SKIP 15** (`Logs/overnight/b5/suite_all2.out`). The FeatureTests numbers are in `TestResults_all.xml`.
- Features per seed: 5 drawn (want 5), 0 rare, 2/2/2/1 ways down.
  Min spacing 1219/1319/1370/1252 m. Visible within 3 km: 82.1/85.8/93.4/91.3% (>= 80%). Method: land eyes on a 128 m grid (every 16th 8 m cell), 2 m over the map ground. Each eye casts one ray per feature target, sampled every 8 m cell against the ground or water surface. An eye on a footprint counts as seeing.
- Easy areas: 4 per seed at 10.0%; no feature cell is an easy cell.
- Peak broadness, seed 42: summit 586 m; mean ground 150/300/600 m out 501/414/186 m. The needle guard is green.
- v4 build time vs v3: 25.2 vs 26.8 s, 28.0 vs 26.6, 25.9 vs 26.8, 23.4 vs 26.5 (all <= v3 + 2 s).
- Record hashes (stable, seed-dependent): E14BB8B2EFB48009, AF9BCC1FEDE9E44E, 258C629D3157F9DD, A0311666C8BA3225. v3 is byte-identical (suite).
- Logs: `Logs/overnight/b5/` (arm_*.out, pics*.sh). Poses: `FarBand/2026-10-08_224205_world`. No performance arms (rule 10).

## Next
- The supervisor builds the owner's B5 review pack from the images above. Then 35-B6.
