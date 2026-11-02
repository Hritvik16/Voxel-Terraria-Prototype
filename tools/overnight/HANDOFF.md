STOP_FOR_OWNER -- task 164 (look: torch-flood direction terms): CODE DONE, SHEETS NOT SHOT. You paused builds at 23:42:34
(`tools/overnight/PAUSE` + `tools/heavy.sh pause`) just as the look player started. The player quit within seconds and left no shots
(`Logs/lookreview_20261101_232102.txt`: "build OK", then an empty LookReview folder). I did not relaunch against the pause.

**Q1 (recommended (a)):** (a) when the Mac is free, `tools/heavy.sh resume`, `rm tools/overnight/PAUSE`, and requeue 164 as it is. The
built player already has the code, so the rerun can use `SKIP_BUILD=1` (~10 min launch + sheet + fast suite ~3 min). It should then end
with the planned doc question, "which direction term (or flat)?". (b) Pick from the test numbers below without pictures (not recommended:
this is a look question).

# 164-look-torch-flood-direction: three new direction terms behind `-tfdir`, tests green; the look launch was cut short by the owner pause

## What changed (`d2b7916`, branch engine-phase)
- `TorchFlood.hlsl` `FloodLight`: direction factor `wrap + (1 - wrap) x ndl`, where ndl is set by `_FloodParams2.w` (`TorchFloodPool.DirMode`,
  `-tfdir`). 0 = 158's (unchanged maths). 1 = `FloodGradCos`: the normalised 3-axis gradient from central differences over 0.4 m.
  2 = `FloodTorchCos` (in `Raymarch.compute`, under `#ifdef VE_TORCH_FLOOD`): the Lambert cosine toward the selected torch whose straight
  distance matches the flood's path within 8 % + 0.25 m; the weight fades to 0 as the path bends, and mode 1 takes over there.
  3 = flat (0.5). Every mode only scales a nonzero level, so sealed spaces stay exactly dark. Default 0, and the flood stays OFF.
- `TorchFloodPool.cs`: `DirMode`, the `-tfdir` flag, C# twins `LightTwin` / `GradCos` / `TorchCos`. `TorchFloodPoolTests` +2.
- Capture (`PlaygroundCapture.TorchReach.cs`): `-trarms none` keeps only the "today" arm, and `-trfloodmodes 0,1,2,3` adds one flood arm
  per mode (`flood`, `flooddir1..3`) on every row and the pillar row. Sheet tool `tools/torchflood164.py` (per row, a 3x2 grid of 960x540
  play tiles with the crop area outlined, plus a crop sheet of the native frame at 2x). TASKS.md paragraph.

## Evidence
- Filtered `TorchFloodPoolTests`: PASS 11 FAIL 0 (`Logs/overnight/164_filter.out`). These include 158's 9 tests (sealed walls exact)
  and 2 new ones:
  - `Direction_EveryMode_SealedBoxStaysExactlyDark`: modes 0-3 x 6 normals, every voxel outside the 0.1 m box is exactly 0.
  - `Direction_FloorBesideTorch_FacetSteps` (the facet metric): the largest jump in the direction factor between 0.1 m neighbours on a
    floor beside a torch on a 1.5 m post. **Mode 0: 0.525, mode 1: 0.264, mode 2: 0.018, mode 3: 0.000.**
    Reading: mode 1 only halves the jumps (the chamfer's gradient is constant across whole regions, so a 0.4 m stencil only softens the
    edges). Mode 2 removes them (a smooth Lambert cosine). Mode 3 is flat by definition.
- Player build with the new shader: "build OK", no Shader error (`Logs/lookreview_build.log`). `Raymarch_FloodCodeOnlyUnderIfdef` is
  green, so the OFF kernels still compile from 158's source.
- **NOT RUN:** the fast suite (the pause also holds EditMode runs) and the look launch. There are no images, so nothing visible has
  been checked yet.

## Unfinished / next
1. After resume: `SKIP_BUILD=1 PRESETS=high EXTRA="-cliffshadow 1 -blackshape 2 -torchreach 1 -trflood 1 -trarms none -trfloodmodes 0,1,2,3" ./run-look-review.sh`,
   then `python3 -I tools/torchflood164.py <shots dir> Screenshots/164 <log>`. Check the crop boxes (`CROP_<ROW>=x,y`; defaults are
   guessed from 158's sheet: room floor square, tunnel left wall). Then run the fast suite and ask the doc question.
2. Expected risks to look for in mode 2: a band where the weight fades at the visibility edge (round the pillar or a tunnel corner), and
   whether its torch loop (selected torches only, distance checks without rays) shows up in cost later (a perf task, not this one).

## For the doc
- Facet metric (largest direction-factor jump between 0.1 m neighbours on a floor beside a 1.5 m post torch; EditMode,
  `TorchFloodPoolTests.Direction_FloorBesideTorch_FacetSteps`): 158's term 0.525 -> central differences 0.264 -> toward-the-torch 0.018
  -> flat 0.
- Sealed spaces: exactly 0 light under all 4 terms (0.1 m closed box, 6 face normals).
- No images yet: the look launch was stopped by the owner pause at 23:42 (`Logs/lookreview_20261101_232102.txt`, empty shots folder).
  Reference for the facets until then: `Screenshots/158/torchflood158.png`, the room row, "FLOOD 12 m" column: the bright square on the
  floor under the torch and the straight wedges along the wall corners.
