# 147-look-soft-sky-s1-oracle: DONE -- CPU oracle `SkyVisReference` + 11 tests green; quadrature table written; no GPU change

## What changed (commit 52248d4, branch engine-phase)
- `Assets/CoreEngine/Rendering/SkyVisReference.cs` (new): skyVis = the cosine-weighted fraction of the face's hemisphere whose hard
  shadow ray (`ShadowRayReference.Trace`, kSoft 0) reaches the sky. Directions with y <= 0 count as GROUND (0, no ray traced).
  Sequence: `SamplePoint(seed, frame)` = the R2 lattice (0.32 fixed point) with a lowbias32 rotation per seed, giving 23-bit u/v.
  It is integer-exact (S2's shader must match the pinned values). `CosineDir` uses sqrt/sincos, which match the GPU to a few ulps.
  `History`: 1/n for 16 samples, then 1/16. Its key is `OriginKey` (the twin of ShadowOriginKey). `Quadrature` = 1,024 directions.
- `Assets/CoreEngine/Tests/SkyVisReferenceTests.cs` (new, 11 tests). Every shape is built from Box `EditOps`; nothing comes from
  the generator. TASKS.md has a "Task 147" paragraph. Report: `Screenshots/147/softsky_quadrature147.txt` (text).

## Evidence
- Filter run `REPORTS=1 tools/run-editmode-filter.sh SkyVisReferenceTests`: PASS 11 FAIL 0. Fast suite: PASS 946 FAIL 0 SKIP 15 (was 936).
- Exact cases (every face, frames 0-255, 8 pixel seeds): the sealed 8.1 m room = 0 and the 0.1 m shell room = 0. Open ground and a
  1-voxel pillar top = 1. An open vertical face = 0.5 and a down face = 0. A 0.1 m wall gives the same answer as a 2 m wall with the
  same near face, in both the quadrature and the running mean.
- Cross-check against closed forms: ledge floor 1.5 m in = 0.277 (2D view factor 0.276); floor under the 2 m hole = 0.063 (square
  aperture formula 0.059).

## FLAGS for the owner / S2 (not blocking S1)
1. **Noise is 2x the plan's estimate.** The plan's sqrt(0.25/16) is 0.125, not 0.06. After 64 frames the per-pixel RMS error is
   0.03-0.05 and single pixels miss by up to ~0.1 (ledge floor 0.192..0.364 around a true 0.277). An offline check
   (`/tmp/sky147/sim.py`, not committed) found NO sequence that keeps every pixel within ±0.05 under the fixed 1/16 mean: R2
   0.037 RMS, a 16-point Sobol net 0.06-0.08, independent random 0.08. So test (4) is stated as RMS over 32 pixels <= 0.05 plus an
   unbiased mean. If S2's look shows too much shimmer, the knobs are S4's spatial filter or a longer mean (1/32): owner Q2.
2. **An open vertical face reads 0.5, today 1** (half of its hemisphere is ground). Fed raw into `skyOpen`, every cliff and wall would
   get about half today's ambient and shadow floor: a global look change. Ground 1 m in front of the 15 m block reads 0.57.
   Recommended for S2: two arms, `-softsky 1` = raw and `-softsky 2` = divided by the face's open value (side faces / 0.5, up faces
   as they are), both on the darkcompare sheet.
3. **Undersides stay 0.** Arch and ledge down faces get 0 by definition, the same as today. The plan's "~0.2-0.5" was not sky light.
   The `lift` column shows what keeping TraceShadowRay's y >= 1e-6 clamp would give instead (ledge underside 0.50, mouth lip 0.44,
   and the wall under the ledge 0.78 instead of 0.28). That is not sky; if the owner wants lit undersides, that is a bounce question.

## Next step
S2 (GPU `-softsky 1`, OFF by default) needs ONE new queue entry from the supervisor; it was not started. Carry flags 1-3 into its file.
Test (6), the box-edge tunnel, belongs to S3.

## For the doc
Quadrature (truth, 1,024 directions) / running mean after 64 frames (mean, RMS error over 32 pixels); today's probe in brackets.
Source: `Screenshots/147/softsky_quadrature147.txt`.
| point | quad | mc (rms) |
|---|---|---|
| sealed room floor / wall / ceiling [0] | 0 / 0 / 0 exact | 0 exact |
| tunnel floor 0.5 / 1 / 2 / 3 / 6 / 12 m in [0] | 0.41 / 0.33 / 0.19 / 0.12 / 0.04 / 0.006 | 0.40 (0.03) .. 0.004 (0.01) |
| tunnel side wall 3 m in / end wall [0] | 0.058 / 0.016 | 0.075 (0.04) / 0.021 (0.03) |
| block face beside the mouth [1] | 0.500 | 0.513 (0.05) |
| ground 1 / 3 / 10 m in front of the mouth [1] | 0.57 / 0.66 / 0.90 | ~same (0.03-0.04) |
| ledge floor 0.5 / 1.5 / 2.9 m in, wall under it [0] | 0.42 / 0.28 / 0.15, 0.28 | 0.44 / 0.29 / 0.16, 0.28 (0.03-0.05) |
| 2 m hole in a 10 m room: below / 2.5 m off / corner [1 / 0 / 0] | 0.063 / 0.032 / 0.002 | 0.056 (0.05) / 0.031 / 0.002 |
Tests (SkyVisReferenceTests, 11): sequence pinned; unit directions in the hemisphere; sealed room = 0; 0.1 m shell room = 0;
open ground = 1, vertical 0.5, down 0, pillar; thin wall = thick; 3 m ledge; 2 m skylight; history 1/n then 1/16; 138 site; report.
Commit 52248d4. No images: this is a CPU-only step, so nothing on screen changed.
