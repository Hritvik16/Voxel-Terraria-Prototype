# Overnight handoff

**13-fly-pose-no-voxels-check: DONE. It is a DESIGNED BOUNDARY, not a bug.** The fly pose sits on a cliff lip. The ground it looks at is a plain at ~752 m, which is below the 7-layer band, so the far band draws it. Commit `233562a` on design-brief. There is no product behaviour change, and no fix is needed for correctness. A design question for the owner is below; it does not block anything.

## What changed
- **`Assets/Game/PlaygroundCapture.FlySweep.cs` (new): the `-flysweep` probe.** It is observe-only. At task 10's pose XZ and heading, it sweeps eye height (2/8/16/32/48/63 m above the ground directly below) x pitch (8/30 deg down). For each pose it records:
  - beauty, tier (6), band (9) and stop (11) views, with % of the frame per class;
  - shader debug-pixel probes at 3 rows (ray pitch, hit, exit distance, steps/cap, maxDist);
  - a CPU-store ray oracle and a world-map ray oracle (where the ray meets the surface; in band? in load square?);
  - a mirror check after the first pose.
- **Wiring:** `PlaygroundCapture.cs` dispatches the flag. `Playground.cs` has `-flysweep` in its three capture lists. `run-tg2world.sh` gains `MODE=flysweep`.
- **Docs:** the KNOWN_ISSUES fly-pose bullet was rewritten with the finding. The CLAUDE.md index line was updated.

## Evidence
- **Run:** `Logs/tg2world_20261003_105334.txt`; report and shots in `~/Library/Application Support/DefaultCompany/Voxel Terraria 1 Byte BrickMap/FlySweep/2026-10-03_105638_flysweep_20261003_105334/`.
- **Voxel % of the frame:** 0 at every pose except 16 m/30 deg (1.5), 8 m/30 deg (6.7), 2 m/30 deg (20.4) and 2 m/8 deg (1.5).
- **Threshold:** voxels appear only when the eye is ≤16 m over the lip and pitched down. Even then they show only the near lip and the higher sides, never the plain.
- **No GPU bug:**
  - steps ≤44 of 1024 (no iteration cap);
  - each ray leaves through the band floor with no hit, and the CPU store has no solid voxel on the same ray;
  - mirror check: lost 0, lag 0, stale 0 (3087 chunks).
- **The world-map oracle puts the visible surface at 750-754 m on every probe ray: "in band False".**
- **Cause, from the code:**
  - `BandHolds` (`StreamManager.cs:348`) keeps the eye's layer and the one below. So the plain (layer 58) can join the band only with the eye below ~832 m.
  - Below that, `BandScore` (`:354`, distance-weighted, ignores view direction) picked 60..66 (768 m floor).
  - The 8 deg centre rays meet the ground 397-1504 m out, beyond the ±128 m load square. That is also by design.
- **Screenshots I looked at:**
  - `h63_p08_beauty` reproduces task 10's frame.
  - `h63_p08_band` has no blue: far band only, F1 nearest.
  - `h02_p30_band` has blue only on the near lip and the sides; the drop to the plain is F0.
- **Caveat:** the in-process classifier mislabels the graded far-level colours (F1 yellow is read as "F2"). Use voxel % and the far total only, not the per-level split.
- **EditMode fast suite:** `PASS 605 FAIL 0 SKIP 12` (3m01s).
- I quote no frame-time numbers.

## Gotchas hit (cost two extra runs)
- **Run 1 (`10:31`) and run 2 (`10:47`) are INVALID.** `-flysweep` was missing from Playground's capture lists, so the walk player drove the camera at (4014, 796.2, 3999.6), inside rock. Beauty came out black and every debug view read 100% "voxel". Only rows that re-applied the pose each frame (the debug-pixel probes) were valid. This is the trap documented at `Playground.cs:393`; it is now in KNOWN_ISSUES too.
- **Run 2's mirror check said stale 309.** That was with the camera jumping between two poses every frame, so it is not evidence. Run 3, stable, said stale 0.
- **Peak footprint was 3200 MB** in run 1, the known over-the-cap issue. Nothing here changed it.

## Owner decision (not blocking)
Should the band reach ground you look DOWN onto from a ledge? Two options would change this, and both are design changes:
- a view-aware `BandScore` (weight the columns in front of the camera);
- a taller band (memory).

Task 10's fly numbers stay valid as a streaming baseline. Its fly still simply shows the far band.

## Next step
The next queued task, if any. If the owner wants a fly still that shows voxels, the QA fix is a fly line that does not run along a cliff lip, or the `-flyclear` eye rule measured from the ground directly below.
