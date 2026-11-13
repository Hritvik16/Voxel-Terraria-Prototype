# 193-look-sky-light-in-shade: DONE. Shade at noon is no longer black: `-skyfill 1` (sky) / `-skyfill 2` (sky + ground bounce), default OFF. A true cave stays 100 % black. The owner decides from the pictures; the cost is large (below).

## Why shade was black (cause, from the shader)
`Raymarch.compute` AMBIENT SKY VISIBILITY traced ONE ray straight up per tier-0 hit: skyVis = 0 or 1. Then skyOpen = lerp(floor, 1, skyVis), with floor = 0.02 on High, scales both the ambient and the sun-shadow floor. So a face whose straight-up ray is blocked, and that is also out of the sun, draws at 2 %. That covers ground and trunks under a crown, a leaning tube wall, and EVERY down face (its own voxel blocks the ray). AO was not the cause.

## What changed (commit 391e2a0)
- `Raymarch.compute`: the same call site now traces ONE cosine-weighted direction per pixel per frame over the face's hemisphere. The sequence is SkyVisReference's R2, seeded by the surface cell's key, so a voxel face converges to its own stratified mean.
  - S = open share of the sky half. A side face's lobe is folded upward, so an open side face = 1, as today.
  - Mode 2 adds B = share of the ground half that lands on sunlit ground: the ray runs to its first solid voxel, then one sun ray from there. `ShadowHit.hitV/hitT` and `ShadowEntryNormal` are new.
  - Per-pixel history `SkyHistR/W`: 8 B/px, ping-pong, reprojected like the sun history. Running mean 1/n then 1/16.
  - Mode 2 mixes S and B by the sky and bounce terms' lumas at the face normal.
  - Unchanged: shells and far hits keep skyVis 1; OFF is the old code path (the history is freed).
- `RaymarchFeature.cs`: `SkyFill` (`-skyfill`), the buffers and the bindings.
- `SkyFillReference.cs`: the CPU twin. `SkyFillReferenceTests` (7 tests).
- WorldSurvey: arm suffix `_sfN`, plus a `[frame]` mean/p50 line over each arm's 60 settle frames. TASKS.md: task 193 paragraph.

## Evidence
- **Fast suite: PASS 994, FAIL 0, SKIP 20** (`Logs/overnight/193/fast.out`).
- **SkyFillReferenceTests 7/7** (`Logs/overnight/193/TestResults_skyfill.xml`):
  - A sealed 2 m rock room and a 0.1 m shell room: exactly 0 on every face and every frame, both modes.
  - A 1-voxel crack in a 1.1 m roof: the floor under it gets 1.2 %; 2 m aside it is exactly 0.
  - Open ground, and a pillar side over sunlit ground: exactly 1.
  - 3 m ledge: underside B 0.42 (mode 1 keeps 0); the wall under it S 0.56. The running mean matches the quadrature (rms 0.04-0.06).
  - Lattice canopy: ground 0.50, trunk 0.52, crown underside 0.54.
  - The GPU's entry face equals the DDA's on 1,765 rays.
- **Trade, stated plainly:** an OPEN but deep narrow slot gets darker than today. Today its walls are fully lit (the straight-up ray escapes). Now a 1 m × 6 m trench wall gets S 0.29 / 0.047 / 0.012 at 0.9 / 2.9 / 5.8 m down. That is physical, and it is not tuned.
- **Shots** (one build + launch `Logs/lookreview_20261113_080409.txt`; one-pose retake `_085114.txt`). Pure-black share OFF → mode 1 → mode 2
  (`Screenshots/193/black193.txt`): forest 27.5 → 11.8 → 12.2 %; jungle_high 10.8 → 4.9 → 3.8 %; under_crown 83.2 → 53.2 → 54.6 %;
  cavern (true cave) 100 % in all three. Open-slope control: mean luma 137.5 → 134 (-3 %; open voxel steps see slightly less than the whole sky).
- **Looked at:**
  - `forest`: OFF has black trunks, black crown undersides and black ground under the crowns. In mode 2 the trunks are brown, the undersides shaded green, the ground in shade dark green.
  - `under_crown`: OFF is a black floor. ON shows the shaded grass texture and brown trunks; deep shade stays dark.
  - `cave_in`: today it is an ENCLOSED tube, not 173's trench. The walls lift only near its openings and the deep tube stays dark.
  - `open_slope` (173's trench spot): the trench is gone after later generator changes, so it serves as a control. OFF and ON look nearly the same.
  - `jungle_high`: the orange/white boxes are 191's near-pool pits, present in every arm.

## Rough cost (capture launch, native 1920x1080, NOT a rig, cold, streaming running)
Settle-frame p50, OFF → mode 1 → mode 2: forest 163 → 230 → 285 ms; jungle_high 195 → 337 → 300; under_crown 144 → 219 → 221;
cave_in 132 → 289 → 331; cavern 101 → 140 → 136; open slope 144 → 195 → 196.
That is **×1.35 to ×2.5**. The bounce adds little over the sky alone. Slanted sky rays run much longer than the old vertical one. Play renders 4-7× fewer pixels (FSR 3 at 540-720 rows), so the absolute cost there is unknown until a performance milestone measures it.

## Not done / open
- Plan step S3: a sky ray that leaves the 0.1 m window sideways counts as open, so a cave right at the window edge could leak. Not seen in these shots; unfixed.
- No open-trench picture: 173's trench no longer exists. The CPU trench test covers that case.
- Cheaper variants (owner approval needed, since they change performance): trace on the 2x2 quad lead only; sample every other frame; a shorter clamp for sky rays.

## Next step
The owner picks from `Screenshots/193/viewer.html`: OFF, sky (1), or sky + bounce (2), and whether the cost is acceptable before a performance milestone measures it in play.

## For the doc
- Pure-black share at noon, OFF → `-skyfill 2`: forest 27.5 → 12.2 %, jungle 10.8 → 3.8 %, under a crown 83.2 → 54.6 % (`Screenshots/193/black193.txt`).
- True cave 200 m down: 100 % black in every arm. Sealed rooms are exactly 0 in the CPU tests (no light through rock).
- Ledge underside: 0 → 0.42 sky+bounce openness. Crack in a roof: 1.2 % under it, 0 two metres aside (`SkyFillReferenceTests`).
- Rough cost: capture-launch p50 at native 1080p is ×1.35-2.5 (forest 163 → 285 ms; `Screenshots/193/lookreview193.txt` `[frame]` lines).
- Images:
  - `Screenshots/193/forest_sf0.png` vs `forest_sf2.png`: the trees in the middle and at right. Black trunks and crown undersides become brown trunks and shaded green undersides; the ground under the crowns goes from black to dark green.
  - `Screenshots/193/under_crown_sf0.png` vs `under_crown_sf2.png`: the lower two thirds. A black forest floor becomes shaded grass; the trunks at right turn brown.
  - `Screenshots/193/cavern_sf2.png`: the whole frame stays black. A real cave is still dark with sky fill on.
