# 193-look-sky-light-in-shade

Model: opus

Approval: owner, 2026-11-12 (chat), "Yeah" to the supervisor's proposal: shaded places are near-black at noon, fix it as a lighting look task, behind a switch, default OFF, pictures; the owner accepted that it may cost some frame time. Runs AFTER 192 (owner: fix the trees' look first), so judge the canopy case with 192's open crowns if 192 landed.
Look-first order (owner 2026-11-05): lighting -> world gen -> visuals. Look tasks record a rough cost from the runs they already do; no extra timing arms.

Read first: `WORLD_GEN_LOOK_PLAN.md` "Not world gen: handed to lighting and visuals" (first bullet), KNOWN_ISSUES.md entries on lighting/shadows, the lighting code path the play preset uses (sky ambient, AO, shadows, torch flood task 175), TASKS.md for any existing sky-visibility / AO flag before adding a new one.

## Problem (supervisor looked at `Screenshots/180/fixed_jungle_2_after.png`: everything under the jungle canopy is pure black at noon)
- Shaded walls of an open trench are pure black at noon (`cave_in` pose, task 173 survey): no sky ambient reaches deep narrow cuts.
- Ground and trunks under tree crowns are near-black at noon (180 forest / jungle poses with `-trees 1`).
Real shade at noon is darker than sun, never black: the open sky above still lights it, and sunlit ground bounces light in.

## Do
- Find WHY these spots get ~zero light (no sky ambient term? sky visibility = 0 from a too-short or binary occlusion test? AO crushing it?). Report the cause with numbers from the shader/CPU path before changing anything.
- Fix it exactly and from data: e.g. sky light scaled by how much sky a point actually sees (a visibility estimate from the occupancy/mip data the renderer already has), plus a cheap one-bounce term from sunlit ground if needed. No per-biome or per-pose constants; nothing tuned to the current terrain. Behind a switch (e.g. `-skyfill 1`), default OFF.
- Adversarial cases: a real cave (must stay dark -- no light leaking through rock), a 1-voxel crack, an overhang, a deep trench, under a crown. Tests where the math is testable on the CPU side.
- Fast suite; TASKS.md paragraph. Rough cost: the frame-time the shot launch already prints, OFF vs ON (no rig).

## Pictures (look at each; captions say what to notice)
One launch: `cave_in` trench, a forest and a jungle canopy pose with `-trees 1` (180's poses), one true cave interior, midday, sun behind camera: OFF vs ON, full size, `viewer.html` flip viewer.

## End
Done; default stays OFF; the owner decides from the pictures. STOP_FOR_OWNER only if the fix needs a frozen-API or memory-layout change.
