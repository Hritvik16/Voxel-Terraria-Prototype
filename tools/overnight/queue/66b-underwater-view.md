# 66b-underwater-view

Approval: owner delegated picks on 2026-10-11 ~00:10 ("pick your recommendations, i can always queue something to
change them"). Task 65 found that diving shows a BLACK screen (HANDOFF `c7887c2`, `Screenshots/65/2_dive_lake_bed.png`).
Queued by the supervisor 2026-10-11 as a gameplay blocker for diving. Branch `engine-phase`. Look-and-feel change:
reversible, behind a play setting.

## Change (one change)
When the camera eye is inside a fluid cell (water, oil, sludge; lava may stay as is if it is simpler -- say which), the
raymarcher must draw the world instead of black: march through fluid cells as a participating medium (per-fluid tint
and exponential fog by distance travelled in fluid, a few metres of visibility in water), hit the lake bed and walls
as usual and shade them, and show the surface from below (a lighter band / sky tint where the ray exits the fluid).
- Find why it is black first (eye-in-fluid start state in `Raymarch.compute`? the fluid layer treated as opaque at
  t=0?) and say it in HANDOFF.
- Viewing water from ABOVE stays exactly as today (opaque surface) -- do not change that look in this task.
- Rigs and capture modes must render byte-identical when the eye is not in fluid (no rig pose is underwater; confirm
  with the phase 7 rig and one existing capture you can diff).
- Play setting only if needed for a fallback (`PlaySettings.underwaterFog` on by default is fine).
- CPU stays the sole terrain writer; no fluid-sim change; no memory-layout change.

## Acceptance
- `./run-play.sh`-equivalent demo (`-movedemo` from task 65 dives in a map lake): screenshots at 1 m and 6 m under
  water at noon (lake bed and walls visible, tinted), and one looking up at the surface from below. Captions say
  what to look at.
- Fast EditMode suite green; `run-phase7-rig.sh` PASS count unchanged (Gate 3 unchanged).
- No frame-time measurement (features over measurement); note if the eye-in-fluid path is obviously expensive.
