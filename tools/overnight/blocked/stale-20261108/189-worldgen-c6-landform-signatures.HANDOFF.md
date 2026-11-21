# 186-matrules-default-on: DONE. The simple material rules are now the DEFAULT everywhere (play, rigs, capture runs, tests); `-matrules 0` is the way back to the old layered rock. No frozen contract or gate broken.

## What changed (commit 063fe27)
- `TerrainSpec.matRulesOn` -> `matRulesOff`, same pattern as `riverShapeOff`. 0 = `GroundRules`, so every v4 spec gets the rules unless asked otherwise.
- `TerrainGenChunkSource.MatRulesArg()` now defaults to true; `-matrules 0` sets `matRulesOff = 1`. The chunk source and `FarField` both follow it.
- No SerializeField or scene-builder copy of this flag exists (grepped all of `Assets/`).
- The effective value is printed in the player log:
  - `[StreamManager] tg2 chunk source: map v4, material rules ON (default)`
  - and `material rules ON` at the end of the `[FarField] tg2 WORLD ...` line.
- Tests changed only where they encode the old paint. No assertion was loosened.
  - `CoverTests` and `BiomeBlendTests` check the task-177 cover and task-122 surface, which is now the `-matrules 0` path. They pin `matRulesOff = 1` and reuse the default spec's shared map (the rules never change the map, so there is no extra build).
  - `MaterialRuleTests` / `MaterialRuleChunkTests` ON/OFF arms now use `matRulesOff`.
  - `BrickSkipTests.*_MatRules` became `*_MatRulesOff`: the plain proofs now cover the rules, and the renamed ones cover the old path.
  - `FluidPocketTests.Sites_AtRest_PathsAgree_Adversarial` (task 183, `-fluidpockets 1`) failed at 3 sites on the first whole-suite run (`Logs/overnight/186/all1.out`).
    - Cause: the test picked out "lining" voxels by material id. The rules paint the same ids beside a fluid (sludge shore = Mud, quicksand = PackedSand, a wet bank = Mud). So it was comparing shell vs chunk rule paint, not the pocket.
    - Fix: the test now finds the lining by POSITION (the column's `pkLineLo..pkLineHi`, or `DeepPocket`'s shell). This position is integer-column exact, so it is the same in shells and chunks.
    - The check is stricter at every real lining voxel. Fluid voxels are compared as before. All 11 sites pass, with 1,504-25,774 shell samples compared per site.
- TASKS.md: Task 186 paragraph.

## Evidence
- Fast suite before the fixes: PASS 964 FAIL 4. The 4 failures were the Cover/BiomeBlend old-paint tests (`Logs/overnight/186/fast1.out`).
- **Whole suite, final: PASS 1033 FAIL 0 SKIP 25, 22m59s** (`Logs/overnight/186/all2.out`).
- Launch logs:
  - With NO `-matrules` flag (`Logs/overnight/186/player_default2.log`): `material rules ON (default)` from both StreamManager and FarField.
  - With `-matrules 0` (`player_off.log`): `material rules OFF (-matrules 0)` from both.
- Shots: one build, 3 of 180b's poses, High, 45 s shell wait. Shells s3+ were partly unfilled; the far band covers them. Each lookreview log lists the missing counts.
- **One broken capture, not used.** The FIRST default launch (`LookReview/2026-11-07_165443_high`) showed other places at all 3 poses: snow plain + hoop, mesas, hill + hoop. Same biomes, so the camera most likely faced another way.
  - The relaunch with the same build and args (`2026-11-07_171540_high`) matches the `-matrules 0` geometry exactly.
  - The two player logs are identical except for one Metal surface line. My guess is mouse input into the fullscreen player (the queue runs while the owner works). Unexplained otherwise; worth noting if it recurs.

## Unfinished / next
- Nothing for 186. Task 183's pockets and 185's stampfit, both still default OFF, were tested here only through the whole suite with the rules ON.
- `tools/matrules180b.py` / `matrules182.py` still label their arms "-matrules 1 / 0". They are historical, and both flags still work.

## For the doc
- Default paint: layered rock (177-179) -> simple material rules (180b + 182); `-matrules 0` = the old paint, byte for byte.
- Player log without a flag: `material rules ON (default)` (`Logs/overnight/186/player_default2.log`). With `-matrules 0`: `material rules OFF (-matrules 0)` (`player_off.log`).
- Whole suite: PASS 1033 FAIL 0 SKIP 25, 22m59s (`Logs/overnight/186/all2.out`). 4 old-paint tests pinned to `-matrules 0`; 1 pocket test now finds the lining by position.
- No timing arms; this task changes no cost (the rules' cost is 180b's `cost180b.txt`).
- Images (each is a top/bottom pair at full 1920x1080: top = DEFAULT with no flag, bottom = `-matrules 0`):
  - `Screenshots/186/pair_cliff_snow.png`: the snow cliff face centre-right. Top: one grey stone top to bottom under the snow. Bottom: pale blue rock with tan/brown stripes and patches.
  - `Screenshots/186/pair_oasis_or_swamp.png`: the desert river through the middle. Top: a green grass + dark mud collar along the water. Bottom: sand right to the water, with grey gravel patches.
  - `Screenshots/186/pair_beach.png`: the hills behind the lagoon. Top: grey stone with green patches. Bottom: a red/brown brick mosaic patch at centre and a black square top-left. The red/tan spit in front is the same in both.
