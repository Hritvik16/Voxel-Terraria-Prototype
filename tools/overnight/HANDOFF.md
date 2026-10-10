STOP_FOR_OWNER -- 41-lava-look-shots: is the new lava right, and should emissive bleed stay on?
Look at `Screenshots/LavaLook/contact_sheet.png` (rows = 6 poses; columns = BEFORE / bleed 0 / bleed 1 / SVO reference on the night-pool row).
Questions: (1) Keep the new lava look? (2) Turn bleed on by default (`LightingConfig.emissiveBleed = 1`)? (3) Fix the pale lava under torches (pose 4, below) by dropping `pointTerm` on lava faces? Not done; it needs your OK.

# 41-lava-look-shots: DONE. gemini/lava-look merged, compiles, fast suite green, three arms shot.

## What changed
- Merge commit of `gemini/lava-look` (73a625b) into `surface-rebuild`. No conflicts. Pre-merge "before" HEAD = 478237d.
- One fix inside the merged code, `Assets/Game/PlaygroundCapture.cs`: `-emissivebleed` was parsed only inside `Sustained()`, so `-lavalight` ignored it. It is now parsed at the top of `Start()`, so every mode honours it. Same line, moved.
- Shots: `Screenshots/LavaLook/{before,bleed0,bleed1}/` (the 6 lava-light-ON shots + `lavalight.txt` each), plus `contact_sheet.png`.

## Evidence (correctness only; no performance arms, rule 10)
- Fast EditMode: **PASS 631 FAIL 0 SKIP 14** (`Logs/overnight/41/suite.out`), 0 `error CS`.
- `ShaderCompileCheck.CheckAll`: every shader OK, Raymarch.compute OK (`Logs/overnight/41/shadercheck.log`).
- Player build (`Logs/lavalight_build.log`, 19:44): "Compiling compute shader Raymarch" present, **0 `Shader error`**, 0 `error CS`.
- BEFORE used the 19:26 build. That build is HEAD 478237d: no Assets change since e4b6a02 (19:21), and it used the same scene builder and build method.
- Arms: before `Logs/lavalight_ab_20261009_193521.txt`; bleed 0 `Logs/lavalight_ab_20261009_194243.txt`; bleed 1 launched by hand (`-lavalight -cleardeltas -emissivebleed 1`), data `.../LavaLight/2026-10-09_194703/`.
- Pixel diff, share of pixels changed by more than 8 levels:

| pose | before vs bleed 0 | bleed 0 vs bleed 1 |
|---|---|---|
| 1 cavern wide | 27.7% | 3.6% |
| 2 wall beside lava | 12.7% | 7.7% |
| 3 cavern far end | 37.2% | 3.9% |
| 4 torches + lava | 21.8% | 5.2% |
| 5 night pool | 0.7% | 0.01% |
| 6 golden hour | 0.7% | 0.01% |

  For poses 1 and 4, the masks show the before-vs-bleed-0 change covers exactly the lava area. Walls, floor and torches are identical.

## What I SEE
- **Cavern lava (poses 1-3) IS deep orange at bleed 0**, the same orange as the night pool. Before, it was pale gold. The lava aggregate lights do NOT wash it out.
  - Contact sheet rows 1-3, columns 1 vs 2: the lava floor goes from pale gold to orange.
- **BUT under torches (pose 4, 40 torches) the lava goes PALE YELLOW**, paler than before.
  - The cause is `baseColor * faceLight`: faceLight carries the torch point light, so strong torch light now bleaches lava.
  - The candidate fix is the one the review named: drop `pointTerm` (or at least the torch part) on lava faces. NOT done.
  - Contact sheet row 4: compare the lava strip in column 1 (orange) with columns 2-3 (pale yellow with orange specks).
- **Nothing but lava differs between BEFORE and bleed 0.** The diff masks show only lava pixels, and the torch-lit walls in pose 4 are identical.
- **Bleed 1** adds a one-voxel glowing rim on the faces that touch lava: the floor edge in poses 1/3, and the wall foot in pose 2 (row 2, column 3, bottom edge).
  - It reaches only edge-neighbours of an emitter; walls further away are not warmer.
  - Torches are lit as before, but the pose-4 torch walls are already near white, so no visible change there.
- Night pool (row 5): orange with bleed 0, closer to the SVO's orange (far right) than the old gold. The SVO's lava is broken up with dark crust and obsidian; ours is one flat orange disc.
- Pose 4 bleed 0 vs bleed 1 also differs at scattered single lava voxels. Bleed is gated off on emitters, so this is probably the lava itself changing between launches (hot/cool or fluid). Not chased.

## Left open / next
- Owner decisions above. Far-band lava keeps its old colour (not shaded by this block).
- The bleed's cost (4 extra window reads per pixel when on) is unmeasured; it is due at the engine milestone.
