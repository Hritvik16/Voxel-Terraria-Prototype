# 58-settings-menu: DONE -- F6 opens a graphics settings panel in play. It is saved between runs and applies live; BUGS #6 (help "&amp;") is fixed.
## What changed (branch `engine-phase`, commit 3865ad3)
- `Assets/Game/SettingsMenu.cs` (new): the panel. Clickable buttons; it releases the mouse while open (flycam + Playground skip input, like the dev map). F6 or Esc closes it.
- `Assets/CoreEngine/Gameplay/PlaySettings.cs` (new): `PlaySettings.json` in persistentDataPath, beside PlayerConfig/LightingConfig.
  - Pure Parse / Validate / WithArgs. Order: defaults < saved file < flags (`-upscaler`, `-renderrows`, `-shadowlevel`), per field, for that run only.
  - `-renderpreset`/`-taa`/`-checker` with no upscaler flag keep the preset path (task 52's rule).
- `Playground.cs`: an interactive run (not `IsCaptureRun()`, not `-fluidprobe`) loads the file instead of task 52's hard-coded fsr3 dyn. The file's defaults ARE fsr3 dyn, so with no file nothing changes.
  - K now goes through `SetSoftShadows` (shared with the menu). Help is hidden while the menu is open, gains an F6 line, and #6 is fixed (`&`, `↔`, `→`; IMGUI does not decode entities).
- `PlaygroundHud.cs`: public `StripVisible` / `DetailVisible`. `PlaygroundFlyCamera.cs`: no look/move while the menu is open.
- `run-play.sh` keys + comment, `CLAUDE.md` Commands line, `BUGS.md` #6 moved to Fixed. New flag `-settingsopen` (starts with the menu open; used for the still).
- Rig check: every `run-*.sh` that opens Playground.app passes a capture flag except run-play.sh, run-demo.sh (both interactive; they read the file) and run-tg2world MODE=fluidprobe (`-fluidprobe`, excluded by name). So no rig reads the file.

## Options (in the menu's order = task 57's cost ranking). All apply LIVE; none needs a restart.
| option | choices (default) | maps to | hint shown (57 numbers only where 57 measured) |
|---|---|---|---|
| Shadow quality | Full soft / Half-res far / Hard / Off (**Full soft**) | `RaymarchFeature.ShadowQualityLevel` = J rungs 0-3 | Off saved 14.2 / 4.2 ms (wall / open); 1, 2 not measured |
| Upscaler | None / TAA / FSR 1 / STP / FSR 3 (**FSR 3**) | `UpscalerSelect.Apply` = `-upscaler` | FSR 3's pass ~6 ms over None; others not measured in 57 |
| Internal resolution | 540 / 720 / Dynamic (**Dynamic**) | `UpscalerSelect.Apply` = `-renderrows` | GPU-bound: frame follows these pixels |
| Shadow edges | Soft / Hard (**Soft**) | `Playground.SetSoftShadows` = K (k_soft 0) | cost not measured |
| Perf overlay | Off / Strip / Strip + details (**Strip**) | PlaygroundHud strip + F1 panel | HUD + minimap ~1 ms together |
| Minimap | On / Off (**On**) | `Minimap.Visible` (F4 also flips it) | part of that ~1 ms |
- Render preset (F3) is dropped from the menu. Its Quality preset is the same 960x540 trace as "None at 540", and Balanced/Performance (640/480 wide) are below the owner's 960x540 minimum (RenderPreset.Default note). F3 stays as a dev key.
- Saving: only a choice made IN the menu is saved, and it clears the "set by a flag" mark for that field. J/K/F1/F4 stay session-only toggles; the menu shows their live state.
- Known limit: Hard edges (k_soft 0) is undone if LightingConfig.json hot-reloads mid-run. K has the same behaviour.
- Behaviour change (documented in run-play.sh): `EXTRA="-renderrows 720"` alone now keeps the saved upscaler (it used to give none@720).

## Evidence
- Fast suite PASS 647 FAIL 0 SKIP 14, 0 `error CS` (`Logs/overnight/58_fast2.out`). That is 641 + 6 new `PlaySettingsTests`: defaults, save/load round trip, broken/missing file, per-field flag override with last match winning and the file untouched, bad flag values, preset-path flags.
- Live load + override: a saved file with rows 540 and minimap off, run with `-renderrows 720`. Player.log: `[PlaySettings] loaded ...; this run: upscaler fsr3, rows 720, ... minimap off; flags override: rows`. The still shows `UPSCALE fsr3 720` and no minimap (`Logs/overnight/58_play_help.out`). The test file was deleted afterwards, so the owner's next play starts at the defaults.
- No frame-time measurement (rule 10); the HUD fps in the stills is not evidence.

## Not done / next
- The buttons were not clicked in a real session (headless). The click path is IMGUI `GUI.Button` and the same code as the start-up apply. First owner play: press F6, click each option, reopen to check that it saved.

## For the doc
- New: F6 graphics menu, 6 options, all live, saved to `PlaySettings.json`; flags override per field; rigs never read it.
- Tests: fast suite 641 -> 647 PASS, 0 FAIL (`Logs/overnight/58_fast2.out`).
- BUGS #6 fixed: help headings read "BUILD & DIG" (was "BUILD &amp; DIG").
- Image: `Screenshots/58/play_settings_menu.png` (noon at the play start, over the island and sea). The centre panel is the menu, rows ordered by cost: Shadow quality (hint "Off saved 14.2 ms ... 4.2 ms"), Upscaler (FSR 3 lit), Internal resolution ("Dynamic (now 540)" lit), Shadow edges, Perf overlay (Strip), Minimap (On). The footer gives the save path.
- Image: `Screenshots/58/play_help_fixed.png` (same start, F2 help open). Look at the group headings "BUILD & DIG", "FLUID & DESTRUCTION", "LIGHT & TIME", the K/Y lines with ↔ / →, and the new orange **F6** line under VIEW. Top right reads `UPSCALE fsr3 720` and there is no minimap (saved file + `-renderrows 720` flag).
