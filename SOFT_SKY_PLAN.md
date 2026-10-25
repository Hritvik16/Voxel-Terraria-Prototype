# SOFT_SKY_PLAN -- graded sky light: dark caves AND shaded (not black) open overhangs

Task 142 (owner Q91 "Soft sky light", 2026-10-25). PLAN ONLY: no product code, no default or look change.
Spec basis: ARCHITECTURE_v8.6 §6.5 / C.9 -- `ambientSky` is "a cheap hemisphere term, also the stand-in for bounce
light into cave mouths ... Underground reads near-zero ambient -- caves are genuinely dark and torch-lit". Bounce GI is
a non-goal (cut list). A graded sky-visibility factor on that term is inside §6.5. It is not a new lighting model.

## 1. What is wrong today (136 / 138 / 139)

`Raymarch.compute:4896-4908`: on a tier-0 hit, one ray goes STRAIGHT UP (`TraceShadowRay(..., SkyProbeDir(...))`), and
`skyVis` is 0 or 1. Then `skyOpen = lerp(_UndergroundAmbientFloor, 1, skyVis)` (`:5146`, also `:3772` see-behind and
`:5370` the old path) scales BOTH the ambient AND the sun-shadow floor
(`sunExposure = lerp((1 - _LookShadowDarkness) * skyOpen, 1, sunVis)`). So a face that is blocked straight up and also
out of the sun goes to floor x ambient = pure black (High preset floor 0.02). That one yes/no answer is wrong both ways:

| case | true sky fraction (cosine-weighted) | today |
|---|---|---|
| open plain | 1 | 1 |
| ledge / arch underside, open to the side | ~0.2-0.5 | 0 -> black (136: 35,345 px at cliff2; arches 87,621) |
| cave mouth, 2-5 m in | ~0.05-0.3 | 0 -> black (139: 62,821 px) |
| sealed room / deep tunnel | 0 | 0 (correct) |

What has already failed: 138 tilted the single ray (stripes and dots, new black on arch legs, +1.9 to +3.8 ms). 139 tried
raised floors (the pure-black COUNT goes to ~0 at 0.25, but overhangs still LOOK near-black, and a sealed room turns grey
at noon) and the probe switched off (overhangs look right, but every cave is lit as if outdoors). Each of these still gives
ONE answer per face. The fix is a FRACTION: how much of the sky hemisphere the face actually sees.

Also today: shell and far-band hits (beyond the 0.1 m window, 59-116 m by preset) take `skyVis = 1`, with no probe
(`:4896` `shellAtHit == 0`). So an overhang crossing the hand-over goes from black to fully lit (an existing pop).

## 2. Candidates

### A. One cosine-weighted sky ray per pixel per frame, averaged over frames (RECOMMENDED)
- **What:** replace the vertical probe with ONE ray per tier-0 hit pixel, in a cosine-weighted direction over the face's
  hemisphere. The direction comes from a low-discrepancy sequence indexed by (pixel, frame), e.g. a 16-direction stratified
  set, rotated per pixel. Same `TraceShadowRay` (air-mip leaps, content-ceiling exit), same ray count as today. The result
  goes into a per-pixel history keyed on the surface point, the same key as the sun-shadow history
  (`ShadowOriginKey(voxel + normal)`, `:1830`). It is a running mean: weight 1/n for the first 16 samples, then a fixed
  alpha of about 1/16. A key mismatch (newly seen surface, edited surface) restarts the count.
- **Look:** a sealed cave has every ray blocked, so the result is EXACTLY 0 on every frame: dark, with zero noise. An open
  plain is exactly 1, with zero noise. Only partly open faces vary (overhang undersides, mouths, canyon feet), and those are
  the graded faces we want. Ledges and arches read as shaded rock, darkest deep under the lip. A cave mouth fades to dark
  over a few metres. A 2 m skylight throws a soft bright patch on the floor below it.
- **Noise / lag:** each sample is 0 or 1. With a 16-sample mean the remaining wobble is about +-6% of ambient where the
  true fraction is ~0.5 (estimate: sqrt(0.25/16)). It is about +-12% on the first 2-3 frames of a newly revealed surface.
  FSR 3's own history softens both. Lag: about 16 frames (~0.5 s at 30 fps) for a surface's value to settle after an edit
  above it. Camera motion keeps the history (the key is a world position), so walking does not reset it. Only
  disocclusions restart. The sun moving changes nothing: sky visibility does not depend on the sun, unlike sun shadows.
- **GPU memory:** a separate history pair (vis f16 + count, key u32 = 8 B/px, ping-pong): ~8.3 MB at 960x540, ~14.7 MB
  at 1280x720. Packing it into the existing `ShadowHist` uint2 instead would cost 0 MB, but it would change 124's buffer
  and its "written only when a sun ray is needed" rule. Recommended: the separate buffer.
- **Cost at 540 rows (estimate, not measured):** about +2 to +4 ms p50 over today. Basis: 138's tilted ray, same count,
  only the direction changed, cost +1.9 / +2.4 ms (tilt 1) and +3.8 / +2.3 ms (tilt 2 side faces), walk_near / walk_open
  (`Logs/costsplit_2026-10-24_232016.txt`). Today's vertical probe is cheap (Phase 7 A.5: -0.3 ms when removed) because a
  vertical ray exits at the content ceiling in a few leaps. Slanted rays are longer. The cost knob if it is too high: trace
  on the 2x2 quad lead only (the sun ray's `reuse || isQuadLead` pattern). But 124 found that halving sun rays saved
  nothing measurable, so do not count on it until measured.
- **Edits:** exact. The ray reads the CPU-written mirror (§0 single writer untouched). A fresh surface restarts its
  history. A surface whose OWN voxel did not change (a cave floor under a newly dug hole) keeps its key and drifts to the
  new value in ~0.5 s (owner Q6).
- **0.1 m window edge:** a slanted ray that leaves the tier-0 box SIDEWAYS currently returns "open" (`:2006` sets `exitT`,
  `sunVis` stays 1). A deep cave or long tunnel near the box edge would leak light, and the leak would pop as the box
  recentres. The fix is already built for sun rays: task 133's `_NearShadowShells` continues a ray that left the box
  through the shells (`:4796`). Step S3 makes the sky ray use the same path. Upward exits through the content ceiling are
  correct as they are.
- **Frozen APIs / layouts:** none. `IWorldQuery`, `IEditService`, D.1, §4.4 and Appendices A/B are untouched. Only new
  shader uniforms, one render-side history buffer, and a flag.
- **Tests / oracles:** CPU twin `SkyVisReference`: the same direction sequence (bit-for-bit index -> direction) over
  `ShadowRayReference.Trace`, plus a 1,024-direction quadrature as the truth. Adversarial cases, built with edit ops so
  nothing is tuned to tg2: (1) a sealed room gives exactly 0 for every frame index; (2) an open plain gives exactly 1;
  (3) a 0.1 m-thick player roof blocks fully; (4) the underside of a 3 m ledge is inside (0.1, 0.6) and matches the
  quadrature to +-0.05 after 64 frames; (5) under a 2 m hole in a 10 m room's roof, the floor directly below is brighter
  than the corners, and the corners are under 0.05; (6) a tunnel crossing the box edge stays dark (S3); (7) history: a key
  change resets the count, and the running mean converges. `SkyProbeTiltTests` stay (the flag stays). 139's
  `-darkcompare` capture gets a "soft sky" column.

### B. A few fixed directions (e.g. vertical + 4 at 45 deg), rotated over frames
- **Look:** the same idea as A, but it can only take N+1 levels (0, 1/5, ... 1). Overhang gradients show as STEPS
  (contour bands), and each direction's hits alias into stripes on arch legs, as 138's tilted ray did, though each stripe
  is only 1/5 as deep. Sealed caves are 0 and open ground is 1, both exact.
- **Noise / lag:** none when standing still, after a full rotation (5 frames). The stripes are stable, not noisy.
- **Memory / cost:** like A if rotated with one ray a frame (+2-4 ms, ~8 MB). All N in one frame: about +8-15 ms, not
  affordable.
- **Edits / window edge / APIs / tests:** like A (the same S3 fix, the same oracle with the fixed set).
- **Verdict:** A's cost, a worse look (banding). B is simply A with too few directions.

### C. A coarse sky-light volume (Minecraft-style skylight flood)
- **What:** per 0.8 m cell of the tier-0 box, light 1 above each column's top, flooded through air with a falloff per
  cell. One texture read per pixel, trilinear.
- **Look:** no noise. Sealed caves are 0 by construction. Gradients follow flood distance, not what the face sees: blobby,
  0.8 m-blocky light. A 0.1 m player wall or roof does NOT block a 0.8 m cell that is mostly air, so it LEAKS. Making any
  solid voxel block its cell darkens ground faces instead. Neither choice is exact at 0.1 m, which breaks the
  generality rule for player builds.
- **Memory:** 204.8 x 102.4 x 204.8 m at 0.8 m = 256 x 128 x 256 cells x 1 B = 8.4 MB (67 MB at 0.4 m).
- **Cost:** sampling ~0.2 ms. But the flood runs on every chunk admission and every edit, and depends on the chunks ABOVE
  (pops while streaming fills top-down). It needs either CPU worker time (workers are already the streaming bottleneck,
  KNOWN_ISSUES "STREAMING BASELINE") or a new GPU propagation pass with dirty-region tracking.
- **APIs:** a new derived per-chunk structure and its GPU buffer. That is a memory-layout addition, so FLAG it as a design
  change under §0.
- **Verdict:** the cheapest per frame, the biggest build, and not exact for thin geometry. Not now. It could come back
  later as the far-hit (shell) sky term, where 0.8 m cells match the data's own resolution.

## 3. Recommendation: A

A is the only candidate that is exact for any geometry: generator output, future features and 0.1 m player builds alike.
It holds no terrain constant. It is a Monte-Carlo estimate of a defined quantity. It spends noise only where the answer
really is "partly", and it reuses the trace, the history key and the shell continuation that are already built.
§6.5's "caves genuinely dark" holds exactly (zero rays escape, so 0). The ambient floor stays an art constant.

### Step list (one change per task; each has a look check and ONE measurement)
| step | change | look check | measurement |
|---|---|---|---|
| S1 | CPU oracle `SkyVisReference` + the direction sequence + tests (1)-(5), (7). No GPU change. | none (CPU only) | quadrature values at 138's built site (sealed room, tunnel, mouth, ledge) printed to a report |
| S2 | GPU `-softsky 1`, OFF by default: cosine ray replaces the vertical probe; history buffer; 1/n then 1/16 mean | 139's `-darkcompare` sheet + a "soft sky" column (noon / dusk / night), plus one walking still pair for noise | one warm cost-split, softsky on vs off (walk_near / walk_open) |
| S3 | sky rays that leave the box sideways continue through the shells (133's path) + test (6) | a pose with a tunnel near the box edge, moving across the recentre | pixel count of the leak, on vs off (no timing) |
| S4 | ONLY IF S2's look shows noise the owner rejects: a 3x3 same-plane spatial filter on the history | the same S2 sheet | one cost-split |
| S5 | owner: make `-softsky 1` the play default, and set the floor (Q1) and curve (Q3) | final sheet | the next perf milestone (no separate arm) |
| later | sky for shell hits (Q5), if wanted | -- | -- |

## 4. Questions for the owner (recommended answer first)
1. **How dark is a SEALED cave at noon?** (a) Recommended: today's floor, 0.02: near-black, you need a torch, and day or
   night makes no difference. (b) 0.05: the walls are faintly visible at noon. (c) 0.12: dim grey, as in 139's 0.12 column.
2. **How much noise is acceptable?** (a) Recommended: a faint shimmer under overhangs while moving, and newly seen
   surfaces settling over about half a second, with FSR 3 smoothing it (no extra step). (b) None visible ever: add S4's
   spatial filter (~+0.5-1 ms estimated).
3. **Does sky light come through a 2 m hole into a 10 m room?** (a) Recommended: physically. A bright soft patch under
   the hole, the corners near-black. (b) Stretched: the whole room dim grey (a vis^0.5 curve, so light carries further
   into mouths and rooms).
4. **Cost.** The estimate is +2 to +4 ms p50 at 540 rows (from 138), while play is still above the 33 ms target.
   (a) Recommended: build it behind the flag and measure in S2; if it is over +3 ms, try 1 ray per 2x2 before deciding.
   (b) Only ship it if it is at most +1 ms.
5. **Beyond the 0.1 m window (shells, 59-116 m by preset):** (a) Recommended: keep today's rule (far faces fully sky-lit)
   for now. Far overhangs read lit, and the pop at the hand-over gets smaller (shaded to lit, instead of today's black to
   lit). Caves out there are already closed by the shell cave fill. (b) Also trace sky rays for shell hits (cost unknown;
   the shell march is the most expensive pass, 93b).
6. **Digging a skylight:** (a) Recommended: the light below fades in over about 0.5 s, at no extra cost. (b) Instant:
   reset the history inside each edit's box (an extra step).

Related, not a question here: dark caves only play well with a torch that reaches further than 139's ~1-2 m glow. That is
task 143 (torch reach look), already queued.

Existing shots that explain the problem: `Screenshots/139/dc139_surface.png` (rows 1-4: the ledge crescent and arch
undersides stay black through floor 0.40 and turn into lit rock only in "sky check off", the look A aims for, but with
caves kept dark). `Screenshots/139/dc139_caves.png` (rows 1-2: the sealed room and tunnel; "sky check off" lights them
like outdoors, which A must NOT do).
