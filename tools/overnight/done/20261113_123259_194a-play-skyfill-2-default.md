# 194a-play-skyfill-2-default

Model: opus

Approval: OWNER, 2026-11-13, Queue decisions doc Q-Z answer (a): "Mode 2 ON in play now; the performance
stage makes it cheaper." Task 193 built `-skyfill 1|2` (sky / sky + ground bounce), default OFF.

## Do
1. PLAY starts with sky fill mode 2: the same path task 52 used for FSR 3 in play (`./run-play.sh` / the play
   preset), so a plain `./run-play.sh` has it. `-skyfill 0` still turns it off for one run.
2. F6 settings menu (task 58): a "Shade light" entry Off / Sky / Sky + bounce, saved in `PlaySettings.json`;
   flags still override it per field for one run.
3. Rigs, capture runs and `-fluidprobe` do NOT change: they never read PlaySettings and keep sky fill OFF
   unless they pass `-skyfill`. Say so in TASKS.md and check one rig's launch line to prove it.
- Fast suite before the commit; a test if the default plumbing is reachable from EditMode. TASKS.md paragraph
  (update the play line in CLAUDE.md's Commands section only if it lists play defaults, like the FSR 3 line).
- No timing arms, no new picture launch (193's pictures stand). One screenshot of the F6 menu if the menu
  can be captured in an existing capture mode; skip it otherwise.

## End
Done with HANDOFF.
