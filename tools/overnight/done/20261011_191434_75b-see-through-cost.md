# 75b-see-through-cost

Approval: task 75 (commit 9a28a20) stopped with its cost check in the pre-written "rethink" band: +3.5 ms p50 at a lake
shore (51.1 -> 54.6 / 54.8 ms warm), 0.0 ms with no liquid in view, +4.5 / +5.1 ms facing a wall of water
(Logs/costsplit_2026-10-11_141247.txt). Owner asked for see-through liquids "efficient ... we are already struggling on
framerate". Supervisor picked 75's recommended (b) under the recommended-option rule, 2026-10-11. Branch `engine-phase`.
This is a PERFORMANCE task: CLAUDE.md measurement discipline applies in full (warm steady state, one change, stop after).

## Change
1. **Split the +3.5 ms first, no code change to the look:** how much is the march through the fluid (extra steps), how
   much the end blend / Fresnel / glint, how much the far-band blend. Use the cost-split rig's existing arms
   (`run-cost-split.sh`, `-costpaths lake`, arm `see_on`) plus timer or disable-one-path arms as needed. Warm-up launch
   first, ABBA. Write the split in HANDOFF.
2. **Before changing anything, write what each candidate is expected to save** and pick the ONE change aimed at the
   biggest share, e.g.: cap the fluid march at ~4 m of path / 64 steps instead of 192; march fluid at the brick or
   coarser tier instead of 0.1 m once inside a uniform fluid brick (the occupancy/air-mip skip for "all same fluid");
   stop when transmittance falls under the threshold earlier for the murky fluids. The look must stay as in 75's
   sheets (bed stripes visible to the same depths, within a stripe).
3. Make that one change, re-measure the same arms (warm, ABBA), report before -> after, then STOP (no second change).

## Acceptance
- Target: <= 2.5 ms over OFF at the lake shore pose (p50), and the wall-of-water pose reported. If the one change does
  not reach it, say how far and what the next candidate would be -- do not chain changes.
- 75's `-liquiddemo` sheets re-shot ON with the change: same visible depths (captions say where to compare).
- Fast EditMode green; `run-phase7-rig.sh` PASS 37 with the setting off, shots identical.
- HANDOFF `## For the doc`: the split table, before -> after ms, the warm-up log path, one comparison sheet.
