# 93c-shells-default-in-play: DONE -- play starts with the LOD shells ON; rigs unchanged; peak memory 2,512 -> 2,378 MB

**Which rule applied:** Step 0 was replaced by the owner's Q63 (a) (switch now, no 93b measurement). No frame-time arms (rule 10).
**Standing-still frames in play are expected ~8-11 ms SLOWER** than the old default (estimate: 93b re-run +12.7 / +13.9 ms minus
task 99's -4.7 / -2.6 ms; NOT measured), about equal while walking. Checked at the next performance milestone (`later/102-shell-march-cost.md`).

## What changed (commit a24ab64)
- `PlaySettings.cs`: saved field `shells` (default ON; an older file gets ON), `-shells 0|1` overrides per run (bad value kept +
  reported), `PlayShellWorkers = 3` (93b's count; `-shellworkers N` overrides), `ResolveShells(args, playRun, loadSaved, out workers)`:
  a non-play run never calls the loader and keeps the pre-93c rule (`-shells 1` only, 1 worker, last match wins).
- `Phase4Bootstrapper.cs`: decides once at boot (`ShellsOn`, `ShellWorkers`; playRun = tg2 + `Playground.IsPlaySettingsRun()` + a
  Playground in the scene); every former `ArgInt("-shells")` reader (small mirror, GPU pool, tiers wanted, ShellStreamer) uses it.
  Logs `[Phase4Bootstrapper] play run: LOD shells ON, 3 workers`. No change to 93d's tier logic, cascades, `-detailspeed`, `FarStartT`.
- `SettingsMenu.cs`: row 10 "LOD shells" On/Off (the one option applied at the NEXT start; hint shows this run's state), panel 1004 -> 1078 px.
  NOT seen on screen (no menu shot taken) -- the owner should glance at F6 once.
- `Playground.ShellsDemo.cs` (NEW, `-shellsdemo`): 3 play poses + 93a's far ridge pose, tint shots, STEP 14 memory line per pose.
  `PlaygroundCapture.FarBand.cs`: `Tg2EyeLevelPoses`/`TgGround` private -> internal. `run-play.sh` header, CLAUDE.md, KNOWN_ISSUES line 61.
- Demos run through `run-play.sh` now get shells unless they pass `-shells 0` (they are play runs).

## Evidence
- `PlaySettingsTests` 14/14 (3 new: round trip, old file -> ON + flag override/last match/bad value, rigs never read the file, play run
  file-then-flag). `SUITE=all` PASS 925 FAIL 0 SKIP 25 (`Logs/overnight/93c-all.out`).
- `run-phase7-rig.sh`: PASS 37 FAIL 0, 19/19 shots byte-identical to 2026-10-16_214816 (`Logs/overnight/93c-phase7.out`).
- Two launches (OFF must be its own boot: shells are a boot decision): `EXTRA="-shellsdemo -shells 0"` and `EXTRA="-shellsdemo"`
  (no flag, no PlaySettings.json -> the default ON path). Logs `Logs/overnight/93c-player-{off,on}.log`, summary `Screenshots/93c/93c.txt`.
- Memory (in-process phys_footprint lifetime peak / external `footprint` 10 s samples / IOAccelerator = Metal):
  OFF 2,512 / 2,425 / 1,233 MB; ON 2,378 / 2,321 / 1,215 MB. Under the 3,000 MB cap. ON: tiers 1-2 not allocated, mirror 16^3,
  GPU body pool peak 232,465 of 254,400 (91%), 0 refused.
- Missing shell chunks at the 45 s shots: eye_level_2 1:0 2:741 3:1908 4:987; glide_400m 1:0 2:288 3:1244; peak_from_2km (2 km
  teleport) 1:975 2:1449 3:1582 4:987. Ridge: shells 1-4 FILLED after 179 s (cap 240), then 1-4 all 0, 5:190 6:182 7:16.
- Stills look the same OFF vs ON at the 3 poses (no holes, seams or horizon change seen); the ridge changed (below).

## For the doc
- Rule: owner Q63 (a), switch without the measurement; standing-still play frames ~8-11 ms slower (estimate, unmeasured).
- Peak process memory OFF -> ON default: 2,512 -> 2,378 MB (-134 MB; external sampler 2,425 -> 2,321); Metal 1,233 -> 1,215 MB; cap 3,000 MB.
- Rigs: phase7 PASS 37 FAIL 0, 19/19 shots identical; SUITE=all PASS 925 FAIL 0.
- Shells 1-4 around 93a's far pose fill in 179 s from a fresh start (3 workers, standing still).
- `Screenshots/93c/93c_sheet.png` -- rows eye_level_2 / glide_400m / peak_from_2km / ridge93a; columns OFF, ON, ON tinted by shell
  (blue tier 0, green 1, yellow 2, orange 3, red 4, magenta 5, grey = far band). Look at rows 1-3: OFF and ON match (at 45 s the
  distant terrain is still mostly far band, grey in the tint); row 4: the ridge left of centre goes from striped to smooth.
- `Screenshots/93c/93c_ridge_crop4x.png` -- the owner's blocky ridge, 4x crop: top = 93a (far band, grey tint: dark vertical stripes
  and stepped ledges); middle = 93c shells filled: the ridge is now drawn by SHELL 3 (orange tint), the plain behind by shell 5,
  and the stripes are GONE -- it reads as smooth 0.8 m facets (shell 3's voxel size), still faceted at 4x but not striped; bottom-left
  = 93c shells OFF (tiers 1-2 + far band), striped like 93a. So the stripes were the 2.5D far band, not the generator's surface.
  Full frames: `Screenshots/93c/on_ridge93a.png` / `_tint.png`, 4x singles `93c_ridge_93a_4x.png`, `93c_ridge_on_4x.png`.

## Next
- The F6 row was not looked at on screen. The frame-time cost is unmeasured by design (next performance milestone / task 102).
