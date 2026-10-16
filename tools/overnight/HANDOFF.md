# 93f1-gameplay-step-5: DONE -- walk band (<= 30 deg auto-step), sliding on steeper ground, climbing faces >= 60 deg with stamina

## What changed (commit 9cc0940, branch engine-phase)
- **Step 5 = the original plan row** (GAMEPLAY_PHASE_PLAN.md 2026-10-11, "unchanged" in the 10-15 update). Only `PlayerMotor.Step(dt, in MotorInput)` changes;
  the legacy `Step` used by rigs and PlayerMotorTests is untouched (guardrail 5). No frozen API, no memory layout.
- `PlayerMotor` (header note "SLOPES AND CLIMBING"):
  - **Walk band:** an auto-step is taken only if the next 0.5 m rises <= 3 voxels (30 deg). `RiseAheadVoxels` takes the highest solid run above the feet over 5 columns, on 3 lines across the body.
  - **Scramble:** when the ground under the feet falls away CONTINUOUSLY by more than that over 0.5 m (no column-to-column jump > 3 voxels), the body slides downhill at up to 4.5 m/s.
    While sliding, the keys steer across the slope only, so W uphill does nothing. Space still jumps. A cliff edge is a ledge you can stand on, not a slope.
  - **Climb:** pressing into a face that rises >= 9 voxels over 0.5 m (60 deg; any wall >= 0.9 m) for 0.2 s makes you cling.
    W climbs up at 1.5 m/s, A/D moves sideways, Ctrl climbs down; releasing W or pressing Space drops you. At the top the normal step-up mantles you onto it.
    The grip holds while the face is within 0.15 m, because a 70 deg face leans back.
  - **Stamina:** 7 s of climbing. It refills at 3.5/s only when RESTING: grounded, not sliding, and the whole 0.6 x 0.6 m footprint supported (the brief's rest ledge).
    At 0 you let go and cannot cling again until rested.
  - **Grip:** `MaterialRules.ClimbGrip`. Loose faces (sand, powder snow) climb x0.6 and tire x1.5. Slick faces (packed ice, glass) climb x0.4 and tire x2.5.
  - **Bug found and avoided on this path:** the legacy step-up settles by subtracting 0.1 m in floats. With the feet exactly on a row and no skin (after Teleport), it lands a hair under the row, sees the step as an overlap, and refuses a valid step.
    Result: a 25 or 45 deg ramp stalls at 0.5 m. The abilities path now settles in whole rows + SkinM. The legacy path is unchanged (rigs); noted in KNOWN_ISSUES.
- `PlayerConfig`: 13 new numbers (walkSlopeMaxDeg, slopeBaselineM, climbMinSlopeDeg, slide*, climb*, stamina, grip scales), all validated.
- HUD: a stamina bar under the dash bar (shown while not full) with "resting", plus "CLIMBING <material> (loose/slick)" and "SLIDING -- Space to scramble".
  F2 help and the run-play.sh key list are updated. CLAUDE.md and KNOWN_ISSUES ("runs up ~70 deg": FIXED IN PLAY) are updated too.
- `-climbdemo` (`Playground.ClimbDemo.cs`): builds a stone pad ahead of the play start with a 6 m wall, a 50 deg ramp and a 4 m packed-ice pillar, then takes 6 shots.
  Noon, sun behind the camera, vitals off.
- `MovementSetATests.Dash_StopsAtAWall_WithoutEnteringIt` now sets climbSpeedMps = 0. It held W into a 4 m wall for 4 s, which now climbs it; the test pins the dash.

## Evidence
- `ClimbSlopeTests` (11, new):
  - 25 deg ramp walked to the top with no slide.
  - 45 deg: 0 auto-steps (refused as steep); hopping reaches >= 1.5 m.
  - 50 deg slides more than 1 m in 2 s, even with W held uphill.
  - A 3-voxel step on flat ground is still taken.
  - A ledge edge does not slide.
  - 70 deg is climbed and mantled to 4.3 m, using stamina but not all of it.
  - A 20 m wall: climb 10.5 m (stamina x speed), fall, no re-grip while empty.
  - Stamina refills on a 0.6 m ledge, not on a 0.4 m perch.
  - Sand climbs 0.6x and ice 0.4x as fast, and both tire faster.
  - Ctrl climbs down; releasing the keys lets go.
  - Legacy still steps onto 45 deg.
- Fast suite **PASS 796 FAIL 0 SKIP 14** (`Logs/overnight/93f1-fast.out`, wall 3m53s). Nothing under Assets/TerrainGen/ changed, so SUITE=all was not run.
- Play (`Screenshots/93f1/climbdemo_log.txt`, third launch):
  - Wall: climbed 2.96 m in 2.31 s wall clock (1.28 m/s; the demo clamps motor dt to 1/30 s at about 25 fps, so motor time runs slower than wall time; the tests show exactly 1.5).
  - Mantled onto the 6 m top with 3.1 of 7 s stamina left; resting on top refilled 3.22 -> 4.53 s.
  - 50 deg ramp with W held uphill: slid 2.7 m down at 4.50 m/s.
  - Ice: 0.57 m/s, about 6.2 s of stamina spent in 2.6 s.
- No frame-time measurement (feature task).

## Picks (owner may change; all numbers in PlayerConfig.json)
1. **Walk 30 deg / Climb 60 deg** over a 0.5 m baseline. Between them is Scramble: slide, jump to make progress. So 45 deg stairs (3-voxel risers on 3-voxel treads) slide; 3-on-6 stairs (27 deg) walk.
2. **Every face >= 60 deg is climbable**, built walls included (walking into your own 1 m wall for 0.2 s climbs it). The brief's Sheer band (no 0.4 m perches; needs grapple) is NOT classified yet: that belongs with step 7 (grapple).
3. Climbing **1.5 m/s**, **7 s stamina** (10.5 m, above the brief's 8 m early rest spacing), refill 3.5/s (full in 2 s), **0.2 s grab delay**, slide 4.5 m/s.
4. Space while clinging = **let go** (no wall jump: that is step 8). Clinging does not refill the double jump.
5. Grip classes: loose = sand, tg2 sand, beach sand, packed sand, powder snow; slick = packed ice, glass.

## Left / next
- Legacy step-up float drift: rigs and PlayerMotorTests still use it, and it stalls on long ramps. Fixing it is a legacy behaviour change; owner's call, not done.
- Next gameplay task in the queue: 93g1 (step 6, gliding).

## For the doc
- **Result:** in play you can no longer walk up cliffs. Ground up to 30 deg is walked, 30-60 deg slides you down (jump to scramble), and faces >= 60 deg are climbed with a 7 s stamina bar that refills on a 0.6 m ledge.
- **In `./run-play.sh`:** walk into a wall >= 0.9 m and keep W held: W up, A/D sideways, Left Ctrl down, release W or Space to drop. Watch the yellow stamina bar under the dash bar. Ice and sand climb slower.
- **Numbers:**
  - 70 deg slope: before, run up un-jumped; after, climbed at 1.5 m/s and mantled (4.3 m test).
  - 45 deg: before, walked; after, 0 auto-steps, needs hops.
  - 50 deg with W held uphill: slides down at 4.5 m/s (play log).
  - Wall reach on one stamina bar: 10.5 m (test).
  - Ice 0.4x speed, 2.5x drain (play log: 0.57 m/s).
  - Fast suite 785 -> 796 PASS.
- **Images:**
  - `Screenshots/93f1/1_site.png`: the test site from 7 m back. Left, the 50 deg ramp (its 4.7 m high end facing you); centre, the 6 m stone wall; right, the 4 m packed-ice pillar.
  - `Screenshots/93f1/2_climbing_wall_up.png`: halfway up the wall looking up. The wall top edge is against the sky; the HUD reads "CLIMBING stone" and the yellow stamina bar shows 5.0 s.
  - `Screenshots/93f1/4_on_top_resting.png`: standing on the wall top (grey strip, bottom) after the mantle, 6 m above the pad. The stamina bar refills with the label "resting".
  - `Screenshots/93f1/5_sliding_50deg.png`: on the 50 deg ramp looking downhill. The stepped grey ramp drops toward the pad, and the HUD reads "SLIDING -- too steep, Space to scramble" while W is held uphill.
  - `Screenshots/93f1/6_climbing_ice.png`: climbing the packed-ice pillar. The HUD reads "CLIMBING packed ice (slick)" and the stamina bar is red and nearly empty after 2.6 s.
  - `Screenshots/93f1/3_climbing_wall_down.png`: the same climb looking down the face at the pad below.
