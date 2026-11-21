STOP_FOR_OWNER -- Q-249: amendment 8.18's kill rule (4) FIRED. On the CPU, the cached sun keeps 1-voxel leaf gaps (dapples) at only
37 % of their true brightness in the finest map (limit 75 %). Choose: (a) drop the 8.18 cached-sun direction; (b) keep it, but let the
per-face contact ray (8 cells, exact) carry 1-voxel dapples near the camera and accept that the cache softens them further out;
(c) keep it and accept softened 1-voxel dapples everywhere (in N0, 2-voxel gaps keep 49-73 % and 3-voxel gaps 68-83 %). Recommendation: (b) needs a
design change in 8.18 sec. 1.3 written first; do not start 247b (the timed test) until this is answered.

# 249-suncache-cpu-oracle (8.18 step 247a): DONE -- kill rule (4) fires; rules (1) and (5) pass
## What changed (commit be035ee)
- `Assets/CoreEngine/Rendering/SunCacheReference.cs` (CPU twin of the DRAFT 8.18 cache: run-map lines, N0-N3 maps 512^2 with `& 511` ring,
  32^2 keyed tiles, contact ray = `ShadowRayReference.Trace` capped at 8 cells, bilinear run rule, prev/cur/next slots, dirty boxes).
- `Assets/CoreEngine/Tests/SunCacheOracleTests.cs`: C1-C9 (the amendment's 9 cases) + line-vs-exact self check + run-rule/line pins.
  C1-C8 `[Category("Slow")]`; C4 also `[Explicit]` (it is the kill rule that fires) so SUITE=all stays green.
- `AMENDMENT_8_18_CACHED_SUN_SHADOW.md` 247a: result note (stays DRAFT). `TASKS.md` Task 249. `Screenshots/249/oracle.txt` (full report).
- Two twin fixes found on the way (already in the note): line centres sat on voxel planes in N2/N3 at axis-aligned sun azimuths ->
  1/8-voxel line offset; the staleness as written is 2 build periods (22 mrad = 75 % of the 30 mrad soft spread, target < 1/3) -> build
  each slot with the sun 2 periods ahead (mean 1.2 mrad, max 11.2 mrad).
## Evidence (CPU tests only; no player launch, no timing: rule 10)
- Fast suite: PASS 1128 FAIL 0 SKIP 25 (`Logs/overnight/249/fast_after.out`). After that, only the test file changed (step-proxy print,
  `[Explicit]` on C4); filter run `SunCacheOracleTests` after it: 11 pass, 1 fail = C4 by design (the kill rule), 0 `error CS`
  (`Logs/overnight/249/filter7.out`).
- Kill (1) slope sweep, 10-85 deg x suns 8/16/30/60 x 8 azimuths x 4 maps: 0 false shadows away from cast-shadow edges (limit 0.5 %).
- Kill (2) flat ground, suns 2-90 deg: 0 acne. (3) pole/wall: 0 foot gaps; worst map 0.013 % mismatched faces away from cast-shadow edges.
- Kill (4) 1-voxel dapples, N0, suns 45/60/80: 37.4 % strength over 1572 faces -> FIRES. Per sun: 80 deg 49 %, 60 deg 38 %, 45 deg 13 %.
  N1 18-32 %, N2 1-12 %, N3 0-4 %. Cause: the 4 bilinear taps reach one texel from the face, and a 1-voxel gap in a leaf layer lets less than
  a voxel's width of light through once the sun is off vertical. A continuous model agrees (N0 at most 66 %), so no 0.05 m map passes.
- Kill (5) 5a deep room: 0 leaks. Residual: under rock thinner than the map's cap, N2/N3 leak (5b/5c, up to 116 of 600 faces at N3, 16 deg).
- (6) 1-voxel crack deeper than the cap reads shadow at N3 (53 of 53 lit faces). (7) one-voxel edit: 0 lines changed outside the
  dirty rectangle, 0 stale, matches a from-scratch cache; one 0.8 m brick at 16 deg dirties 650/225/100/49 lines (est. 500/130/35/10).
- (8) moving camera + sun, 360 frames: 0 sentinel texels read, 0 fallbacks; (9) ring wrap 511->0: 0 answers changed, no aliased tile.
- Step proxy (raw 0.1 m DDA cells, no leaps): exact ray 74.0 steps/face; contact ray 12.4; line build N0 59.3, N1 81.9, N2 125.9,
  N3 177.2 steps/line (`Logs/overnight/249/oracle_00_step_proxy.txt`).
- Memory / pass card / slice (rules 11, 12, 14): none -- CPU test code only, nothing allocated in play, nothing drawn. HEAVY: no.
## Images looked at
- `Screenshots/249/dapples_gap1_sun60.png` -- 5 panels left to right: exact sun ray, then cache N0, N1, N2, N3 (sun 60 deg, 1-voxel leaf
  gaps, brightness = sunlight). Compare the dots: sharp and bright in panel 1, dim (and some missing) in N0, faint in N1, few blurred
  spots in N2, gone in N3.
## Next
- Owner answers Q-249. 247b (timed test) is on hold until then. If (b): a design task rewrites 8.18 sec. 1.3 (contact-ray carries
  sub-texel dapples) and re-runs C4 with that rule.

## For the doc
Task 249 -- does the cached sun (amendment 8.18) give the same answer as today's exact sun ray? CPU-only check, 9 scenes.
**Verdict:** mostly yes. Slopes, flat ground, poles/walls, deep caves, edits, moving sun and ring wrap all match. One real failure:
**single-voxel holes in leaves (dapples) come out too dim** -- 37 % of their true brightness in the sharpest map, against the 75 % the
amendment requires. Kill rule (4) fires, so the owner must decide before the timed test runs.

| check | result | limit |
|---|---|---|
| false shadows on slopes, away from cast-shadow edges | 0.000 % | 0.5 % |
| acne on flat ground, sun 2-90 deg | 0 faces | 0 |
| light leaks into deep caves | 0 | 0 |
| 1-voxel dapple strength, N0 (0.05 m) | **37 %** (80 deg sun 49 %, 45 deg 13 %) | 75 % -> KILL |
| 2-voxel / 3-voxel dapples, N0 | 49-73 % / 68-83 % | (not a rule) |
| thin-roof caves at N3 (0.4 m) | up to 116 / 600 faces leak | residual |
| cost proxy: exact ray vs contact ray | 74.0 vs 12.4 steps per face | -- |

Image: `Screenshots/249/dapples_gap1_sun60.png` -- left panel is the exact sun through 1-voxel leaf gaps (bright dots); the next four are
the cache's maps N0 to N3. Look at how the dots dim in N0 and vanish by N3: that loss is the failure.
Report: `Screenshots/249/oracle.txt`.
