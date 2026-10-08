# 32b-play-launcher: DONE. `./run-play.sh` opens the newest world (tg2, map v4). FOUND: the start pose is under lake water, so the first frame is black. Fix queued as task 38.

## What changed (commit `252c00a`)
- `run-play.sh` (new) launches `Builds/Playground.app` interactively with
  `-world tg2 -mapversion 4 -farband 1 -farshadow 1`. It passes no capture flag, so
  Playground applies `RenderPreset.Default` the way a player session does.
  - Holds the Unity lock for the whole session (`unity_lock_acquire`, like run-demo.sh).
  - `MAPV=3` opens the old generator.
  - `BUILD=1` rebuilds the scene and the player first (logs: `Logs/play_scene.log`, `Logs/play_build.log`).
  - `KEEP=1` passes `-keepdeltas`. The default is `-cleardeltas`, because v3 and v4 share the delta folder `Phase4World_tg2`.
  - `SHOT=S` takes one screenshot S s after startup. `EXTRA` passes extra flags through.
  - On start it prints the build's commit, date and DIRTY/clean from `Builds/Playground.app.buildinfo.txt`. If the build is older than HEAD, it warns with the number of commits behind (and how many touch Assets/Packages/ProjectSettings).
  - It prints the in-game keys, read from the code: `Playground.RegisterDefaultToys`, `PlaygroundFlyCamera`, `PlaygroundHud` (F1), `Minimap`, `DevMap`. At exit it greps the `[FarField] tg2 WORLD ... map vN` line and the `-playshot` line from Player.log.
- `Assets/Game/Playground.cs`: new opt-in flag `-playshot S`.
  - I needed it because `screencapture` fails in a headless session ("could not create image from display").
  - It is not a capture mode: not in `IsCaptureRun`, so the preset, HUD and controls are unchanged.
  - It writes one PNG to `persistentDataPath/PlayShots/` and logs the eye position and the voxel material at the eye.
  - Without the flag it does nothing.
- One line in `CLAUDE.md` "Commands"; one line in `run-demo.sh`'s header.

## Evidence
- Fast EditMode suite, before and after the code change: `PASS 622 FAIL 0 SKIP 12` both times
  (`Logs/overnight/32b/editmode_fast.out`, `editmode_fast2.out`).
- `BUILD=1 SHOT=45 ./run-play.sh`, launched 3 times through the lock, quit with SIGTERM (`Logs/overnight/32b/play*.out`):
  - The build-provenance line printed correctly: `commit ad67110, ..., DIRTY`.
  - Player.log: `[FarField] tg2 WORLD (aligned): world map v4 (biome x3, record 6291456 B)`, so this IS the v4 world.
  - `[Playground] -playshot: ... eye 4004.4,655.4,3974.8 m = voxel int3(40044, 6553, 39748) material 6 (WATER), mode Walk`.
- `Screenshots/Play/spawn_v4.png`: the spawn shot, 1920x1080.
  Caption: the whole 3D view is BLACK behind the HUD panels because the camera is under water. Look at the minimap
  (top right): the yellow player marker sits in a dark-blue lake. Left panel: "buoyancy 100% submerged
  in water". The yellow lines are the fluid wake-radius box. The F2 help panel is open in the centre.
- The `run-play.sh` header documents the workaround: Tab (fly), then hold E to rise out of the water.

## Found, not fixed (outside one change)
- **Black start on tg2.** Interactive Start always ends in `TeleportToArena()`, and on tg2 v4 (seed 42)
  that arena pose has the eye inside water. Task 38 (`tools/overnight/queue/38-play-start-pose.md`)
  will find out why (a natural lake bed passing `TryFindNaturalBasin`, or the arena's own vents filling
  the basin) and lift the pose into air.
- **F4 does two things.** `Minimap` toggles itself on F4 AND the F4 toy toggles TAA. `run-play.sh` says so.
- **The in-game F2 help is out of date.** It says "1-6 / scroll pick item" (there are 15 items; the number keys
  reach 1-9) and "F4 minimap" (F4 is also TAA). It does not list F5.
- The serialized `PlayerController._spawnM` (1280, 40, 1268) is from the old world. On tg2 it logs
  "could not free the spawn"; this is harmless because the arena teleport moves the player afterwards. Noted in task 38.

## Next step
- Run task 38. Then `SHOT=45 ./run-play.sh` should show terrain, and the `-playshot` line should say `(air)`.
