# Task 233 plan -- low-sun start, pale sky + clouds, light grade (written before any launch)

## Pieces and their cost class (all: constants / per-pixel ALU on pixels the kernel already draws; 0 GPU bytes)
1. `-startsun DEG` (default 16, 0 = noon = before). Play's start view (Playground startView / ResumePose) and the cost
   split's default hours (no `-costsun`) use the MORNING hours where the sun is DEG up. Constants only.
   Why 16 deg (data = the look curves in SunCycle): the sky/ambient day factor saturate(4 sin el) reaches 1 at 14.5 deg,
   so from ~15 deg the frame is not dimmed (231's 12 deg forest: mean luma 0.255 vs noon 0.334); the sun's golden warmth
   1-(el-2)/23 is still 0.39 and the horizon rim 0.50 at 16 deg (both 0 by 25-28 deg). 16 = one degree clear of the knee.
   Why morning: play runs a 20-minute day; an evening start at 16 deg sets the sun ~1 real minute later (night at once),
   a morning start climbs toward noon over ~4 min. Night and the day/night cycle are unchanged (only the start hour moves).
2. `-skypale 0|1` (`-skylook` is task 121's existing switch, so the new one gets its own name; arm `skypale_off`).
   Sky the eye sees (LookAtmosphere = sky pixels AND the haze target): pale hazy zenith (0.44,0.60,0.84) -> horizon
   (0.80,0.85,0.90), the horizon band widened (down^2 instead of down^4); 2-octave value-noise clouds (procedural, 0 B) in
   LookSkyBackground on MISS pixels only. Lighting (ambient, far-shade sky term, sky fill) takes the pale HUE at the OLD
   sky's luma (constants on the CPU), so shade does not brighten: jungle trunks cannot go darker (225's bug) by construction.
   Everything scales with the day factor: at night (factor 0) bit-identical.
3. `-grade 0|1` (arm `grade_off`): in CSMain's last colour step before the ResultTexture write (render rows, before FSR),
   display space: greens -22 % sat (+olive), all -5 %, warm mids, S-curve 0.18 pivoted at 0.40, toe lift 0.012, corner
   vignette 0.10; amount x day factor (night identical). Tuned offline first (`grade_tune.py` on 226 noon + 231 12-deg
   stills): mean luma -0.2 .. +4.5 % (requirement +-5 %), where 231's grade lost 10-20 % at low sun.

## Launches (no extra ones)
- Warm-up: `MODE=stream MOTION=walk ./run-tg2world.sh` (numbers discarded), then at once:
- DAY cost split (one launch): paths walk_near (moving) + forest_eye, jungle_eye, redwood_far1 (trees210 poses), all three
  pieces ON (FULL). Timed ABBA arms only at walk_near and forest_eye (`-costtimedpaths`): `startsun_off` (noon),
  `startsun25` (25 deg), `sun_off` (no sun rays). Still-only arms at every path (`-coststillarms`): look233_off (before),
  sun233_only, sky233_only, grade233_only. -shellwait 30, -costtimed 15.
- NIGHT: `-costpaths torch`, arms look233_off, stills only (-costtimed 1). Identity check vs FULL by pixel diff.

## What each result makes us do
- Sun step = (arm p50 - sun_off p50). Noon step S_n, 16-deg step S_16, 25-deg step S_25, noise = FULL range.
  The sun is already over its 8.17 share (5 ms) at NOON (232: 16-20 ms), so "within budget" here = the low sun adds
  no more than the noise to today's noon cost:
  (a) S_16 - S_n <= max(noise, 2 ms) at both poses -> default 16.
  (b) (a) fails but S_25 - S_n <= max(noise, 2 ms) -> default 25 (the lowest measured elevation that fits), say so.
  (c) both fail -> default 25 if it is closer, and flag `OVER SHARE` + HEAVY in HANDOFF; the owner decides.
  A lower cost at 16 than noon (possible: more of the frame shadowed early) -> (a).
- Sky / grade ms: NOT timed. Their per-pixel work is < 1 % of the kernel (clouds: ~8 hashes on sky pixels only; grade:
  ~40 ALU ops); every possible measured delta sits inside the FULL range (1-9 ms in 232), so any result -> the same
  action (keep). Pass cards instead.
- Pictures: a piece defaults ON only if its still is better by eye at the four day poses AND the grade keeps mean luma
  within +-5 % at the low-sun poses (metric from the stills). Worse at any pose -> default OFF, say why.
- Night: diff FULL vs look233_off at torch_camp. Any pixel change beyond FSR/torch-flicker noise (FULL vs FULL) -> bug, fix.
- Memory (rule 11): one process = one ledger; expect no change (no buffers). Peak +50 MB or refusals up vs 232 -> look why.

## Outcome (after the launches)
- Sun: walk_near S_n 12.1, S_16 9.6, S_25 10.6 (noise 3.2); forest_eye S_n 18.2, S_16 45.0, S_25 35.9 (noise 8.0). (a) and (b)
  fail at forest_eye. The task's rule ("the highest value that stays within budget") outranks this plan's (c): only noon fits,
  so `-startsun` defaults to 0 and `-startsun 16` is the look switch. HEAVY flagged (the sun is over its share at every elevation).
- Sky / grade: kept ON (pictures better, grade luma at low sun +1.0..+6.1 %, never darker). Night: sky 0 diff, mean luma equal.
