# Task 232 -- what each outcome makes us do (written BEFORE the measured launch, 2026-11-17)
Measured: one warm-up (MODE=stream MOTION=walk ./run-tg2world.sh, discarded), then ONE cost split back to back with
227/228's poses, sun and flags (walk_near moving + forest_eye + jungle_eye, noon, High, 432 rows, play flags; the
quality preset turns task 56's temporal shadows ON, so "before" = today's play path: 1-in-4 tile rotation + key reuse).
Arms: FULL = -sunres 4 (low-res sun, 4x4 blocks), sunres_off (before), sun_off (no sun rays: the ceiling);
the rig's own order F, then X X F per arm (ABBA).
Night torch_camp is NOT launched: the sun is below the horizon there (227's night run: -76 deg, 0 sun rays traced), so
the low path never runs and the picture is identical by construction.
- sunres_off - FULL >= 5 ms p50 at walk_near AND the pictures are clean (sunvis diff vs sunres_off near the 228 noise floor
  away from sub-pixel shadows; NO triangle field on 228's hillside crop; shadow edges in place) -> flip the default to
  -sunres 4 (RaymarchFeature.SunRes), report ms saved per pose.
- saving < 5 ms at walk_near -> keep the switch OFF (default 1), report the split; not a STOP_FOR_OWNER.
- saving >= 5 ms but a visible defect (false or missing shadows beyond sub-pixel ones, smearing on the walk) -> keep OFF,
  show it in the sheet, name the cause. No second performance change in this task.
- FULL slower than or equal to sunres_off -> the inline orphans (edges, steep far slopes where no same-plane neighbour
  exists) still make whole SIMD groups wait (124's finding): keep OFF, report, name orphans as the next suspect.
- a kernel fails to load (KernelHealth error, magenta frame, "Property not set" for CSSunTrace) -> a bug: fix, rebuild,
  rerun the launch once.
