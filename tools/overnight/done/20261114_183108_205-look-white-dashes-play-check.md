# 205-look-white-dashes-play-check

Model: opus

Approval: SUPERVISOR, 2026-11-14 -- task 198's recommendation ("3's check first; if dashes still show in play, option 1"),
recorded in the doc as "going ahead unless you object". The FIX stays behind a flag, default OFF: the look call is the owner's.

## Background (task 198, HANDOFF; commit ac3ec59)
White 1-px dashes on steep walls = sun-lit voxel TREADS that keep their own face light where `FarSlopeNormal`'s bracket test
fails (`p.y < lo - tv || p.y > hi + tv`), so `CubeFilterWeight` = 0 and task 120's filtered cube light is skipped.
`Screenshots/198/198_178c_before_zoom.png` shows the bug. `-dashprobe 1` counts them (forest 66/66, cliff_snow 122/125 predicted).

## Do
1. ONE launch, 198's poses (forest_breaks, cliff, cliff_snow + 178c's canyon wall), noon, sun behind the camera, arms
   `-wsarms native,fsr3` (play's FSR 3 dyn). Do dashes show in the PLAY arm? Count them with `-dashprobe 1` in both arms.
2. If FSR 3 hides them (no visible dash at 100 %): stop there; report with pictures. No code change.
3. If they still show: build 198's OPTION 1 behind a new flag (default OFF, e.g. `-dashfix 1`): filtered cube light for
   SKY-OPEN hits even when the bracket test fails; face light kept only for sky-closed hits (caves, undersides). Pixel-diff
   flag ON vs OFF at the same poses + one cave pose + one overhang pose: every changed region named (the side faces in the
   fallback bands will change -- show them). `-dashfix 0` byte for byte unchanged.
- Fast suite once at the end if code changed. TASKS.md paragraph. No timing arms (rule 10). Launch budget applies.

## Pictures
Full size, `viewer.html`: native vs play, and (if built) fix OFF vs ON zoomed 4x on the dashes AND on a changed side face
and a cave. Captions say where to look.

## End
Done with HANDOFF + pictures; the owner question (fix ON by default?) goes through `## For the doc`.
