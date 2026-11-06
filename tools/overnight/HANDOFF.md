# 175-torch-flood-on-in-play: DONE -- the torch flood is ON in play by default (dir 2, I 1.6, k 0.35); `-torchflood 0` / F6 "Point lights" = the old torches

## What changed (commit "175: ..." on engine-phase; `git log -1 --grep '^175:'`)
- `TorchFloodPool.cs`: `PlayDirMode` 2, `PlayIntensity` 1.6, `PlayFalloffK` 0.35.
  - `ApplyPlayDefaults(on)`: play values, then ParseArgs (this run's -tf* flags still win), then `SetEnabled`.
  - `SetEnabled`: OFF -> ON calls ResetAll, because edits are not tracked while the flood is OFF.
- `PlaySettings.cs`: `torchFlood` (default true; an old file gets on) and the `-torchflood 0|1` override.
- `SettingsMenu.cs`: LoadAndApply calls ApplyPlayDefaults. The F6 menu gets a live "Torch light" row (Flood / Point lights), and the log line names the torch light.
- Rigs and capture runs keep I 2.0, k 0.10, dir 0, flood OFF, so every frozen rig baseline stays comparable.
  - **Exception: `tools/play-flags.sh` PLAY_SETTINGS_FLAGS** (MODE=playwalk) now has `-torchflood 1 -tfdir 2 -tfi 1.6 -tfk 0.35`. Its guard test `PlayFlagsFileMatchesPlaySettingsDefaults` requires every PlaySettings default, and now also pins the three values.
- QA additions to `PlaygroundCapture.TorchCine.cs`:
  - Arm token `+p` = play's own default path, no flags.
  - Scene `edit` = place / dig through a play-shaped EditService.
  - Flood memory and per-torch cost logged after every build.
- `tools/torchplay175.py`. Tests `PlaySettingsTests.TorchFlood_*`. TASKS.md paragraph; one line in CLAUDE.md's play commands.

## Evidence
- Fast suite: **PASS 1000 FAIL 0 SKIP 16** (`Logs/overnight/175_fast.out`).
- Launch (capture only, no flood flags): `Logs/lookreview_20261105_213456.txt`; numbers `Screenshots/175/torchplay175.txt`; viewer `Screenshots/175/viewer.html`.
  - Launch 1 (`Logs/lookreview_20261105_212332.txt`, sheets in `Screenshots/175_run1_broken432/`) had a bug in MY arm: it forced the flood OFF before each `+p` arm, so the 432 arm was shot mid-rebuild.
  - Fixed and reshot: `Logs/lookreview_20261105_213456.txt`. All numbers below are from this second launch.
- The log's play arm line: "PlaySettings default torchFlood True -> flood ON, dir 2, I 1.60, k 0.35; FSR 3 sc 1.0 reactive True". The defaults really do apply.
- Edits re-flood (camp, 540 play arm, survival-shaped EditService):

  | Edit | Settle | Re-floods | Torches |
  |---|---|---|---|
  | Place a torch on a post | 4.4 s | 7 + 1 new flood | 9 -> 10 (it registered itself) |
  | Dig a 361-voxel pit | 5.8 s | 10 | 10 |
  | Dig the middle torch | 5.7 s | 9 | 10 -> 9 (it unregistered itself) |

- Looked at `tp175_play_vs_off.png`, `tp175_edit.png`, `tp175_edit_crops.png` (below).
  - Cave and wall: a clear gain.
  - Camp: a broader, softer warm wash with no clipping (clip 0.04 %).
  - The pit is hidden behind the ridge crest from this camera. Its re-flood shows only as a faint relight (0.47 % of px).
- **Rough cost** (look-stage rule, from these runs; no timing arms):
  - Memory, 9-torch camp: CPU union 3.1 MB (content 1.5) + caches 2.9 MB (2.3); flood-job scratch peak 27 MB, only while flooding; GPU 2.5 MB. Wall camp (6 torches): CPU 2.2 + 1.9 MB.
  - Per torch: **~840-990 ms worker mean (max 1.5 s)** on the camp ridge; 161 ms in the cave.
    - 169 measured 226 ms (a 31-torch camp, cost-split run, pre-176 terrain).
    - So settle is 4-6 s per edit here, vs 169's 1.2-2.1 s. A torch placed in play lights up a few seconds late.
    - Not comparable 1:1 (different terrain since 176, the launch renders native 1080 alongside, warm chip). Worth a look in the performance stage.
  - The flood kernel is used whenever the pool is Ready, even with 0 torches. 158 measured it as cheaper than the 32-light loop (+5.6 / +6.3 ms saved p50).

## For the owner (no decision needed)
- Placing or digging near torches relights after ~4-6 s on this camp. That is slower than 169's 1-2 s; I suggest the performance stage looks at it (named here, not queued).
- MODE=playwalk now runs with the flood ON, because it mirrors play. Its old numbers are not comparable for scenes with torches.
- 174 left `-fsr3sc 10 -fsr3reactive 1` out of PLAY_SETTINGS_FLAGS (they are not PlaySettings fields), so playwalk still runs FSR 3 sc 0, no mask. Not changed here.

## For the doc
- Cave, play default vs `-torchflood 0`: black px **73.0 % -> 38.7 %**. Wall: lit px **2.8 % -> 6.1 %**. Camp15: lit **5.4 % -> 9.4 %**, clip 0.04 % (`Screenshots/175/torchplay175.txt`).
- 40 m strafe flicker (lit px; step 2's re-report with sc10 + mask, on post-176 terrain):

  | Arm | Flicker |
  |---|---|
  | Flood ON at 540 | 7.19 |
  | Flood ON at 432 | 7.66 |
  | Point lights | 6.07 |
  | Before 174 | 6.03 |

  - That makes the flood +1.1 to +1.6 over OFF here. 174b found +2.7 (4.2 -> 6.9) without sc10 + mask.
- Walk flicker: flood 540 **3.68** / 432 4.33, vs point lights 4.05. Walking, the flood is no worse.
- Edits: place / dig pit / dig torch re-flood **8 / 10 / 9** torches, settle **4.4 / 5.8 / 5.7 s**. Memory for 9 torches: **CPU 6.0 MB + GPU 2.5 MB** (scratch peak 27 MB while flooding).
- Images:
  - `Screenshots/175/tp175_play_vs_off.png`: rows camp15 / cave / wall; left = play default (flood), right = `-torchflood 0`.
    - Look at the cave's evenly lit tunnel (left) vs one lit side (right).
    - Look at the wall face's warm glow (left) vs small speckled patches (right).
  - `Screenshots/175/tp175_edit.png`: the 4 edit steps.
    - Top-right: the new torch at lower right lights its own pool.
    - Bottom-right vs bottom-left: the middle torch is gone.
  - `Screenshots/175/tp175_edit_crops.png`, bottom row (4x): the dug torch's post goes dark and its warm pool on the slope disappears.
