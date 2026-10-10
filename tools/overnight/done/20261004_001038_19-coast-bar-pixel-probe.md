# 19-coast-bar-pixel-probe

Requires: 18-coast-pose-black-bar-check

Evidence task (follow-up of 18). Diagnosis only: NO fix without owner approval.

Read the KNOWN_ISSUES.md bullet "BLACK BAR OVER THE SEA AT `coast_from_sea_30m`"
first. Task 18 established WHERE the bar is: the window's non-resident strip, z chunks
42-46, only for x chunks < 312 (the camera's chunk). It also established WHAT the bar
is: far F0 hits drawn as an unlit water side face. The geometry fits a far march that
starts at the voxel march's end, under the sea, instead of at the strip entry
(`FarStartT`). The code path is NOT pinned, nor why only the left half is affected.

Pin it with ONE `MODE=poses` run (BUILD=1), at the coast pose only:
1. A DebugOut pixel probe at one bar pixel, about (500, 720) of 1920x1080, and at one
   control pixel in the same rows right of the bar, about (1400, 720).
   - Mind Phase3AcceptanceRig's note that DebugPixel coordinate mapping must be
     validated, not assumed.
   - Write these into unused DebugOut slots. DebugOut is 128 floats; 0-45 are taken
     (34-43 is the shadow ray ring), so use 46 onwards and check with grep first:
     currentDist, steps, maxDist, the FarStartT result, fh.t, fh.normal, fh.level,
     and FarChunkResident for the strip column the ray crosses at z chunk 44.
   - The probe is diagnostic-only and writes for the one debug pixel. It must not
     change the shading of any other pixel.
2. CPU side: log `FarField`'s `_residentBits` words 27-31 for that frame, and the
   Clipmap window origin/dims passed to `UpdateResidency` next to the shader's
   `_WindowOriginBricksPacked`.
3. Two extra poses (capture code only):
   - the coast pose moved +1 chunk (+12.8 m) in x: does the bar's right edge follow
     the camera's chunk?
   - the coast pose yawed 180 deg: does the bar follow screen-left or world -x?

Report which mechanism it is, with the numbers. Describe the smallest fix and queue
it as a task for owner approval (do NOT apply it). Fast EditMode suite before
committing. Update the KNOWN_ISSUES bullet and the CLAUDE.md index line.
