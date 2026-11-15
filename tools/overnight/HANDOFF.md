# 205-look-white-dashes-play-check: DONE. Dashes STILL SHOW in play (FSR 3 only dims them). Fix built behind `-dashfix` (default 0 = OFF); mode 2 recommended.
## Step 1: play check (198's poses + overhang + cave mouth; noon, sun behind; arms native / play FSR 3 dyn; `-dashprobe 1` both arms)
- FSR 3 turns a dash row into a fainter light line or a soft streak; it does not remove it. Predicted-dash pixels that show as dashes,
  native -> play: forest_breaks wall box 66 -> 14-18, cliff_snow ledge 122 -> 8-9, overhang tree crowns 784 -> 296-321 (`Logs/overnight/205/look_a3.out`, `look_a4.out`).
- NEW (oracle side split): EVERY predicted dash lies ABOVE the four far-texel column tops (forest 374/374, cliff_snow 1,296/1,296, 0 below).
  Tree crowns and cacti sit above the tops too, so they fail the same test and ALSO show tread stripes (overhang pose).
## Step 3: the fix, `-dashfix 0|1|2` (`RaymarchFeature.DashFix` -> `_DashFix`)
- Where the bracket test fails, sky-open hits (x skyVis, so caves keep their face light; undersides never change) get task 120's filtered cube
  light with the column slope. `Raymarch.compute`: `FarSlopeCode` (FarSlopeNormal's test as 0/1/2/3), `CubeFilterWeightOpen`, one call site
  in the voxel/shell path under `if (_DashFix != 0)`. The far band's call is unchanged.
- **2 = TOP faces only (REC.):** dashes gone at native and in play (forest box 66 -> 0, play 14 -> 0; cliff_snow 122 -> 6, play 8 -> 1). Side faces keep their light.
  Changed px: forest 0.42 % (ledge lines + pine crowns), cliff_snow 1.05 % (2 ledge bands), overhang 4.4 % (crowns lose their stripes, a little
  darker: mean -6.8 lum on changed px), cave 0.16 % (cactus tops, far ridge; the cave interior unchanged).
- 1 = every face but undersides: kills the dashes too, but flattens trees/cacti into a camouflage pattern and adds pale side-face stripes (overhang 7.4 %).
- 0 = the old call byte for byte by construction. Evidence: `fixed_cliff` native is pixel-identical to 198's pre-change frame; forest 99.83 % /
  cliff_snow 99.96 % identical, which is under the noise between two OFF launches of the same build (99.05 %, overhang 84.65 %).
- Efficient by construction: one int uniform, no buffers, no extra rays. Memory: all arms in the same launches: ledger peak/pools identical OFF vs
  ON, GPU body pool refused 0, CPU pool peak 423,864 < valve. No timing arms (rule 10). The extra cost is one `FarSlopeCode` per far voxel/shell hit
  where the old code returned early, so it costs about the same as before.
## Also changed
- World survey disables `PlaygroundFlyCamera`: a click on the player window (owner at the Mac) made it overwrite the camera rotation every frame;
  launch 1 shot 6/20 frames at the wrong view. KNOWN_ISSUES entry (other capture modes still have it).
- Survey pre-apply of the last arm strips `_dfN`. Launch 2 died on `native_df1` as the last arm (FormatException; player killed, relaunched SKIP_BUILD).
- `-dashprobe`: later arms counted (`DashCount`) against arm 1's predicted pixels; ABOVE/below split. Arm suffix `_dfN`. `LadderArms` keeps any `native_*`.
- Commit: see `git log -1` (205: ...). Files: Raymarch.compute, RaymarchFeature.cs, PlaygroundCapture.{DashProbe,WorldSurvey,RowsLook}.cs,
  Tests/DashFixTests.cs, TASKS.md, KNOWN_ISSUES.md, `Logs/overnight/205/*` (drivers, logs, diff tools).
## Evidence
- Fast suite PASS 1023 FAIL 0 SKIP 21 (`Logs/overnight/205/fast.out`); filter DashFixTests + CubeLookTests 5/5 earlier.
- 4 launches (budget overrun, reasons): L1 camera turned by a click; L2 died (arm parse); L3 OK (mode 2 was "below only" = no effect, data
  showed why); L4 OK with the top-face mode. L4's forest OFF shot caught a loading shell: the forest OFF frame is L3's (same OFF code).
- Looked at: all zooms below + full frames + diff maps.
## Not done / next
- No default change (owner). If (a) is chosen: default `DashFix = 2` (+ PlaySettings entry if wanted), a fast-suite run, one look launch.
- Not checked: motion (FSR 3 in motion is likely worse than these still frames), night / low sun, a sun-facing camera.

## For the doc
- Result: the white wall dashes still show in play (FSR 3 dims them to a faint line). New `-dashfix 2` removes them at native and in play. Default is OFF until you decide.
- Numbers (`Logs/overnight/205/look_a4.out`, `look_a3.out`): forest wall box dash px over sun-lit treads 66 (native) -> 14 (play) -> 0 (play + fix 2);
  cliff_snow ledge 122 -> 8 -> 1. Pixels changed by fix 2: forest 0.42 %, cliff_snow 1.05 %, tree pose 4.4 %, cave 0.16 % (cave interior 0).
  Memory: +0 (one shader int); refusals 0 both ways.
- **Question (Q-AG, wall dashes):** turn the fix on by default? (a) REC: yes, `-dashfix 2` (top faces only). The dashes go; ledges at 100+ m read
  as wall, and tree crowns lose their bright tread stripes and get a little darker. (b) `-dashfix 1`: also removes them, but trees and cacti look flat (not rec.).
  (c) Leave it OFF: the dashes stay as faint lines in play.
- Images (`Screenshots/205/viewer.html`, full size):
  - `Screenshots/205/205_forest_dashes_4x.png`: the forest wall, 4x. Top-left native: the row of white dashes at the bottom. Top-right play:
    still a faint line. Bottom: fix 2, native and play: the line is gone and nothing else moves.
  - `Screenshots/205/205_trees_4x.png`: tree crowns, 4x (what the fix also changes). Top-left native: bright stripes. Top-right fix 2: no stripes, slightly darker.
    Bottom-left fix 1: flat camouflage (why not 1).
  - `Screenshots/205/205_diff_overhang_df2.png`: where fix 2 changes a frame (blue = darker): the tree crowns and the far cliff only. Arches, ground and caves are unchanged.
