# 58-settings-menu

Requires: 57-engine-7-frame-cost-split

Approval: owner, 2026-10-10 ~20:10 in chat: "Small one after 57" -- a small in-game graphics settings menu that
exposes switches that ALREADY exist. (The owner noted players customise graphics, sometimes shadows and foliage
density; foliage density is NOT wanted.) Queued by the supervisor 2026-10-10. Branch `engine-phase`.

## Goal
One in-game settings panel for `./run-play.sh` (interactive play only). Options, using the existing code paths
(no new rendering features):
- Upscaler: none / TAA / FSR 1 / STP / FSR 3 (today `-upscaler`, default FSR 3 since task 52).
- Internal rows: 540 / 720 / dynamic (today `-renderrows`).
- Shadow quality (today the `J` rungs 0-3) and hard/soft (`K`).
- Render preset path (today F3) only if it still means something next to the upscaler choice; otherwise drop it
  and say why in HANDOFF.
- HUD/perf overlay and minimap on/off.
ORDER the options by task 57's cost split (read its HANDOFF): the settings that move frame time most come first,
and each option shows a one-line hint ("biggest frame-time cost" etc. only where 57 measured it).

## Rules
- Saved between runs (a small JSON file beside the existing `PlayerConfig.json` / `LightingConfig.json`, wherever
  those live), applied at start. Command-line flags still override the saved file for that run.
- Rigs and capture modes (`IsCaptureRun()` and every `run-*.sh` that passes capture flags) IGNORE the saved file,
  so nothing any rig measures changes. Check every `run-*.sh` launch line as task 52 did.
- Open/close with one key not used today (list the keys in use first; Esc already releases the mouse). The F2
  help panel lists it.
- Changing an option applies live where the code allows; where it needs a restart, say so in the menu.
- Fix BUGS #6 in the same pass: the F2 help panel prints HTML entities ("BUILD &amp; DIG"); mark it fixed in
  `tools/overnight/BUGS.md`.
- No frame-time measurement (PREAMBLE rule 10). Fast EditMode suite 0 FAIL; a small EditMode test for the
  save/load + flag-override order.

## Acceptance / For the doc
Two midday stills (sun behind the camera): the menu open over the play start, and the help panel showing the
fixed "&" text. List every option, its default, what it maps to, and which need a restart.
