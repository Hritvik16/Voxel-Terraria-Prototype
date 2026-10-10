# 52-engine-3e-fsr3-dyn-default: DONE. `./run-play.sh` now starts with FSR 3 dynamic (1080p out, 540-720 internal rows), as `-upscaler fsr3 -renderrows dyn` gives.

## What changed (branch `engine-phase`, commit 08b336d)
- `Assets/Game/Playground.cs` (Start): if no `-upscaler`/`-renderrows` flag is given, the run is interactive (`!IsCaptureRun()`), and it passes none of `-renderpreset`/`-taa`/`-checker`, `UpscalerSelect.Apply(Fsr3, 540, dyn)` runs after the default RenderPreset. That is the same order and call the flags take.
  - Why `-renderpreset` opts out: `-fluidprobe` (`run-tg2world.sh MODE=fluidprobe`) is NOT in the capture list but passes `-renderpreset quality`. Without the opt-out that rig would have changed. `-taa`/`-checker` mean "the old TAA path" (FSR 3 would make them no-ops).
  - Checked every `run-*.sh` launch line. The only launches without a capture flag are `run-play.sh`, `run-demo.sh` (interactive, so both get the new default) and fluidprobe (unchanged, see above).
- F3 (no code change): `RenderPreset.Apply` -> `UpscalerSelect.Deactivate`, so the first press goes to Balanced, then Performance, then Quality, all on the old preset path. FSR 3 comes back only on restart. The frame is not broken.
- F4: still toggles `RaymarchFeature.Taa`. Under FSR 3 that has no visible effect, because FSR 3's resolve replaces ours. The status line now says "(no visible effect: fsr3 resolves the frame; F3 for the presets)". After F3 it works as before. F4 still also toggles the minimap.
- `Assets/Game/PlaygroundHud.cs`: the existing lines show the upscaler and its rows. The top strip shows `UPSCALE fsr3 dyn 540`; the F1 panel's `render` line ends `upscaler fsr3 dyn 540`. After F3 both read `off (<preset>)`. No new UI.
- `UpscalerSelect.cs` header comment updated.
- `run-play.sh`: header explains the default and the override flags. The keys list gives the F3/F4 notes. At exit it prints the first and last `[UpscalerSelect]` log lines.
- `CLAUDE.md`:
  - Commands section: one line on the play default and its overrides.
  - TAA and checkerboard index bullets reworded (OFF on the preset path; play uses FSR 3).
  - `MAPV=3` wording: "pre-rebuild generator: same shapes, but since task 45 built with Burst floats, so not byte-identical to the old map".
- `KNOWN_ISSUES.md`: the TAA and checkerboard entries got the same notes.
- Disk: deleted task 48's 78 raw stills `Screenshots/Engine03/compare/*__*.png`. They were untracked, so there is nothing to commit. The folder went from 248 MB to 72 MB. Kept `sheet_{540,720,dyn,crops,motion_crops}.png`, `risk_*` and `upscalercompare.txt`.

## Evidence
- Fast suite PASS 634 FAIL 0 SKIP 14 (`Logs/overnight/52_fast.out`). Its log shows the tests ran at 15:10. The HUD file was edited at about 15:07, so the run included it.
- Build OK (`BUILD=1 SHOT=60 ./run-play.sh`, `Logs/overnight/52_play.out`, built from the dirty tree at ae4a578 = this commit's code).
- Player.log:
  - `[UpscalerSelect] upscaler fsr3, internal 960x540 (dyn) -> 1920x1080` at startup, with no flags.
  - 0 `dyn ->` steps in the ~75 s session, so it stayed at 540 rows. The strip read 23 fps / 42.7 ms (feel only, not a measurement), far above the 15 ms climb threshold. That matches task 51 (fsr3_dyn sat at 540 on walk).
- The player does not quit after `-playshot`; I quit it with osascript after the shot was logged.
- No frame-time measurement (rule 10).

## Not done / notes for the owner
- Pre-existing, not caused by this task: the top-right strip is right-anchored, so its fps text sits under the minimap box (visible in the still: "23 fps (42.7 ms)" half covered). Adding the UPSCALE tag only widened the strip to the left. Fix if wanted: move the strip left of the minimap, or below it.
- At 540 rows the dyn controller will only climb when smoothed frames drop under 15 ms, so at today's rates play stays at 540. That is what the owner picked: it climbs as the engine phase frees time.

## Next
Queue task 53 (engine step 6 cascade caps).

## For the doc
- Play now starts with **FSR 3 dynamic**: 1080p output from 960x540 internal (dyn range 540-720 rows, 36-row steps, 16.7 ms target).
  - Before: RenderPreset Quality, 960-wide gate, no upscaler (task 47's flags only).
  - After: `upscaler fsr3, internal 960x540 (dyn) -> 1920x1080` in Player.log, with no flags. In this session it chose 540 rows and logged 0 steps in ~75 s (it climbs only below 15 ms smoothed).
- Overrides: `EXTRA="-upscaler none|taa|fsr1|stp|fsr3 -renderrows 540|720|dyn" ./run-play.sh`. `-renderpreset quality` (or `-taa 1` / `-checker 1`) gives the old preset path. F3 in play also hands the frame to the presets until restart.
- Rigs and capture modes: unchanged (no upscaler unless they pass the flags).
- Disk: task 48 raw stills deleted, `Screenshots/Engine03/compare/` 248 -> 72 MB.
- Image: `Screenshots/Engine03/play52/play_fsr3dyn_default.png` is the `SHOT=60 ./run-play.sh` still at 13:12, near noon, 1920x1080.
  - Top-right strip: "UPSCALE fsr3 dyn 540".
  - Lower half: the sunlit coast, the lake disc (bottom left) and the yellow-brown hills render cleanly, no broken frame. The F2 help panel covers the middle as it does at every start.
  - The sun is nearly overhead, so there is no strong backlight.
