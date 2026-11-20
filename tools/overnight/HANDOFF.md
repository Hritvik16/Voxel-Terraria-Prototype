# 40-bug-ledge-view-crescent: DIAGNOSED, no code change. Crescent = zero-ambient shade on a sun-shadowed undercut; already fixed in play (skyfill 2)

## Finding
- Reproduced `ledge_view` (seed 42 map v4, eye 4791.55, 601.20, 3467.82) at noon and golden hour with the capture default (`-skyfill 0`):
  pure-black wedge under the top overhang + a large black face under the ledge. Both are sun-shadowed and have rock straight above the face
  cell, so the vertical sky probe (task 136) gives them zero ambient -> exactly 0,0,0.
- Not the far band: the band view (`ledge_view_golden_band.png`) is flat sky, so voxels draw the whole frame. Not a tier seam, not NaN: with
  `-skyfill 2` (task 193, play's default since 194a) the same faces are dark but textured, geometry intact, no hole.
- So the "fix" already exists. I did not change the rigs' default (they deliberately stay at skyfill 0 for byte-identical comparisons;
  switching is a look/default decision). No code changed, so no EditMode / phase7 runs were needed (none were made).
- Not done: the full `MODE=poses` sheet regression read (nothing changed to regress); no performance arms (rule 10). Memory/pass cards: n/a.

## Question for the owner (not blocking)
Make `-skyfill 2` the default for `MODE=poses` / capture runs too? (a) yes, so pose sheets match play (recommended; old sheets stop being
pixel-comparable); (b) leave as is, knowing black faces under overhangs appear in rig shots only.

## Files
`TASKS.md` (task 40 paragraph), `tools/overnight/BUGS.md` (#2 diagnosed). Logs: `Logs/tg2world_20261119_155328.txt` (skyfill 0), `Logs/tg2world_20261119_155729.txt` (skyfill 2).

## For the doc
- Cause: sky-probe zero ambient on shadowed undercut (not seam/terrain). Fixed in play by skyfill 2; rig default unchanged.
- `Screenshots/40/ledge_view_noon.png`: BEFORE (skyfill 0): look at the black wedge top-centre and the black face bottom-left/centre.
- `Screenshots/40/skyfill2/ledge_view_noon.png`: AFTER (skyfill 2): same pose, same faces now dark brown with visible voxel texture; no black.
