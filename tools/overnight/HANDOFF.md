# 18-coast-pose-black-bar-check: DIAGNOSED. The bar is a far-band hand-off fault in the window's non-resident strip; the exact code path is not pinned (probe queued as 19); nothing fixed

Previous handoffs still waiting on the owner:
- 15 (B1): accept v4's walk worker-gen p50 (373-389 ms vs 187-372 ms), or approve a `-biomescale` A/B? The full text is in d6f0abc's HANDOFF.md.

## What changed (commit on surface-rebuild, see `git log -1`)
- `Assets/Game/PlaygroundCapture.FarBand.cs`: diagnostic captures for `coast_from_sea_30m` only.
  - Golden: views 11 (voxel stop), 10 (far heat) and 6 (voxel tier).
  - Both times of day: a `_farshadowoff` beauty shot.
  - A `[task18]` log line: window origin/dims, content Y band, sun direction.
  - Engine code is unchanged.
- `KNOWN_ISSUES.md`: new bullet "BLACK BAR OVER THE SEA ...". `CLAUDE.md`: one index line.
- `tools/overnight/queue/19-coast-bar-pixel-probe.md`: an evidence task to pin the mechanism.

## Evidence
- One extra poses run: `BUILD=1 MODE=poses ./run-tg2world.sh` (map v3).
  - Logs: `Logs/overnight/18_poses.out`, `Logs/tg2world_20261003_180732.txt`.
  - Shots: `FarBand/2026-10-03_181049_world/`.
  - Coast pose: deficit 0; MIRROR lost 0, lag 0, stale 0.
  - Window origin chunks (296, 34, 15), dims 32x16x32, content Y [4992, 5888). Resident 3087 = the 21x21 (+-128 m) load square x 7 layers.
- The bar is pixel-identical in v3 (07:47) and v4 (16:16), so it is NOT from B1.
  - It is in every `-world tg2` poses run since 2026-09-26 (36 runs scanned).
  - It is in no `-farpreview` run, so the voxel window is involved.
- Findings:
  - The bar pixels are far F0 hits (view 9), not voxel hits.
  - Not a far shadow: unchanged with far shadows off.
  - Not a capped voxel march (view 11 has no magenta or red).
  - The far march hits almost at once there (view 10).
  - The colour is an unlit water face, RGB (1,5,13).
- Geometry (60 deg FOV, 0.8 deg pitch):
  - Bar bottom, row ~754 = the far edge of the load square, 137.6 m out.
  - Bar top, row ~692 = rays meeting the WINDOW's far edge (201.6 m) at sea level.
  - Right edge, screen x 915 = x chunk 312, the camera's chunk. Right of it, the same strip draws normal sea.
- Most likely mechanism: the far march starts at the voxel march's end, already under the far sea top. `FarMarch` then returns a -z side-face hit on the first water texel outside the window.
  - That requires `FarStartT` to read the strip as resident.
  - The CPU bitmap should say it is not resident. The left-only extent is unexplained, so this is not proven.
- The two dark arcs are SEPARATE: they are voxel LOD tier seams.
  - The dark rows equal the view 6 tier changes in every column tested (777/840 for tier 1->2, 1048 for 0->1).
- Fast EditMode: **PASS 606 FAIL 0 SKIP 12** (`Logs/overnight/18_editmode.out`).
- Peak footprint this run: 3425 MB (over the 3 GB cap, as already known).

## Images looked at (folder `~/Library/Application Support/DefaultCompany/Voxel Terraria 1 Byte BrickMap/FarBand/2026-10-03_181049_world/`)
- `coast_from_sea_30m_noon.png`: the black bar is left of centre, about 2/3 down, over the sea. Below it are the two thin dark arcs (tier seams).
- `coast_from_sea_30m_noon_farshadowoff.png`: the same frame with far shadows off. The bar is unchanged, so it is not a shadow.
- `coast_from_sea_30m_golden_band.png`: the bar's rows are green (far F0). Blue (voxel) begins at row 754 across the whole width.
- `coast_from_sea_30m_golden_view10.png`: a blue rectangle exactly where the bar is (an immediate far hit). The rest of the sea is yellow.
- `coast_from_sea_30m_golden_view11.png`: only green, blue and grey, with no magenta or red. The voxel march was not capped over resident terrain.
- `coast_from_sea_30m_golden_view6.png`: the red/green and green/blue tier boundaries lie exactly on the noon shot's two dark arcs.

## Left / notes
- No fix is applied or described as certain. A small fix is likely: in `FarStartT` or the residency bits, or never start the far march below the far surface. It needs task 19's numbers first.
- `Assets/Scenes/Playground.unity` (modified by the build) and the queue files deleted by the runner are left as found.

## Next step
- Task 19 (pixel probe plus two shifted/yawed poses) pins the code path and queues a fix for owner approval.
- 17 (B2) still waits on the owner's answer to 15.
