# Task 228 -- what each outcome makes us do (written BEFORE the measured launch, 2026-11-17)
Measured: one warm-up (MODE=stream MOTION=walk ./run-tg2world.sh, discarded), then the cost split back to back (same poses,
sun and flags as 227: walk_near moving + forest_eye + jungle_eye, noon, High, 432 rows, play flags).
Arms: FULL = -sunfar 32 (coarse beyond 32 m), sunfar_off (exact = before), sunfar64, sun_off (no sun rays: the ceiling);
the rig's own order F, then X X F per arm (ABBA).
- sunfar_off - FULL >= 5 ms p50 at walk_near AND the sunvis/clean diffs vs sunfar_off stay near the noise floor in the near
  half of the frame -> report coarse-32 as the recommended Air setting (default stays OFF: the task forbids changing presets).
- sunfar64 saves >= 70 % of what FULL saves -> recommend 64 m instead (less visible change for nearly the same ms).
- FULL saves < 2 ms at every pose -> the far sun rays are not where the 28 ms goes (as 124 found for the ray count): STOP
  optimising, report the split (sun_off vs sunfar_off = the whole sun cost now), name the next suspect (near canopy rays
  / the reuse lookup). No second performance change in this task.
- A visible defect in the near range (diff above the floor at the bottom of the frame, or a lit/dark flip on near ground)
  -> a bug: fix, re-shoot stills only.
- Far shadows visibly blockier (squares on far trees / ground under them) -> expected trade: show it in the sheet and
  let the owner judge; not a stop.
