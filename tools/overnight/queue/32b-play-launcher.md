# 32b-play-launcher

Approval: owner, 2026-10-08 (supervisor chat): the Playground is what the owner hops into
to check everything out, and there is no one command that opens the NEWEST world.
`run-demo.sh` opens the old world (or the tg2 far band only), and the surface-rebuild work
(map v4: B1-B3 and the B steps after) only shows with `-world tg2 -mapversion 4`.
Reversible tooling; queued by the supervisor 2026-10-08 under the "recommended option is the
default" rule. No performance arms.

## Do (one change)
- `run-play.sh`: launch `Builds/Playground.app` INTERACTIVELY on the newest world:
  `-world tg2 -mapversion 4` + the far band, using the interactive render preset (no capture
  flags, so it plays like a player sees it). Holds the Unity lock while open (like
  `run-demo.sh`). `MAPV=3` opens the old generator for comparison. `BUILD=1` rebuilds the
  Playground first (through the lock).
- On start, print the build's commit, date and whether it was DIRTY (from
  `Builds/Playground.app.buildinfo.txt`) and warn if it is older than the branch tip.
- Print the in-game keys that exist today (F4 TAA, F5 detail-at-speed, C / I far band, the fly
  camera, edit / fluid tools) -- read them from the code, do not guess.
- One line in CLAUDE.md "Commands" and in `run-demo.sh`'s header pointing to it.

## Acceptance
- Launch it once (through the lock), take one screenshot at the spawn, confirm it is the v4
  world (the `-mapversion` log line), then quit. Caption the shot in HANDOFF.
