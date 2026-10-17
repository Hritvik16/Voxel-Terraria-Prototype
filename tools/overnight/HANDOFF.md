# 93g1 gameplay step 6 (GLIDING): DONE -- hold Space after the double jump, 10 m/s forward, 2 m/s down, soft landing

(The previous HANDOFF, 93b5's STOP_FOR_OWNER on the shell air-brick mask, still waits for the owner: its question is in
`tools/overnight/blocked/93b5-shell-air-brick-mask.HANDOFF.md`. 93b Requires 93b5.)

## What changed (commit 0a87243, engine-phase)
- `PlayerMotor` (abilities path `Step(dt, in MotorInput)` only; the legacy rig Step never glides): new state `Gliding`, `CanGlide`
  + `StepGlide`. The rule: airborne, not swimming/climbing/dashing/sliding, the air jumps spent (`AirJumpsUsed >= airJumps`), falling
  (vy <= 0), Space HELD (`MotorInput.UpHeld`, already Space in play). Fall speed eases to `glideSinkMps` (braked at 30 m/s^2 when faster,
  gains gravity when slower); horizontal velocity eases (8 m/s^2) to `glideSpeedMps` along the LOOK yaw; S = `glideSlowSpeedMps`,
  A/D drift `glideStrafeMps`. Release Space = normal gravity; re-hold re-opens (still one double jump per airtime). Landing, water,
  a dash or a cling (glide into a cliff holding W) end it. Teleport clears it.
- `PlayerConfig`: `glideSinkMps 2`, `glideSpeedMps 10` (0 = glide off), `glideSlowSpeedMps 4`, `glideStrafeMps 3`, `glideAccelMps2 8`,
  `glideBrakeMps2 30`, validated. Existing PlayerConfig.json files keep working (missing fields take these defaults).
- HUD (`DrawMoveHud`): "GLIDING 10.0 m/s, sinking 2.0 m/s ..." and "double jump spent: hold Space to glide"; the state label is
  wider (the GLIDING line was clipped at the old 440 px). F2 help + `run-play.sh` key list have a glide line. CLAUDE.md task line.
- New `GlideTests` (7). New `-glidedemo` (`Playground.GlideDemo.cs`; vitals stay ON so the landing is judged by the real damage path).

## Evidence
- Fast suite: `PASS 821 FAIL 0 SKIP 14` (4m24s). Filter run during work: GlideTests + MovementSetA + ClimbSlope + PlayerMotor + PlayerConfig 77/77.
- GlideTests: ratio = speed/sink = 5 +- 0.02 over 5 s steady (sink 2.000, speed 10.000 m/s, heading = look yaw); a glide from 30 m lands
  at <= 2.01 m/s, FallDamage 0, feet on the floor, ~15 s down; the same drop without Space does damage; no glide before the double jump;
  release -> gravity, re-hold -> back to 2 m/s; yaw 90 steers to +x, S -> 4 m/s, D -> 3 m/s drift; glide into a pool -> swimming;
  glideSpeedMps 0 -> never glides.
- Play (`Screenshots/93g1/glidedemo_log.txt`, second launch, seed 42 v4): launch site 99 m from the play start, 144.5 m above sea,
  lane yaw 205 (sun-away 180). Glide 124.4 m across, 25.1 m down, **ratio 4.96**; impact **2.00 m/s -> 0 damage** (a free fall of that
  height = 100 = death); health 100 -> 100; **0 frames held by unstreamed terrain** at 10 m/s (streaming kept up on this 16 s glide).

## Picks (owner may change; all in PlayerConfig.json)
1. Steering: the MOUSE steers (heading = look yaw), W/no key = full speed, S = air brake 4 m/s, A/D = 3 m/s sideways drift. (Alt: WASD
   steers like walking.)
2. Glide opens only once FALLING after the double jump (holding Space through the double jump glides from its apex); no glide without
   spending the air jump (the brief's "after the double jump").
3. Opening brake 30 m/s^2 (a fast fall turns into a glide in ~1-1.5 s, no instant stop); turn rate 8 m/s^2.
4. No stamina / glide time limit and no item gate (P15: every ability unlocked); a glider item can come with crafting.

## Not done / follow-ups (no new task queued)
- The look of the glider: there is no player model in first person, so only the HUD says GLIDING (no wing/cloth visual, no wind sound).
- Long glides from the island's real peaks (300+ m drops) are not shot; a ~25 m drop was the best lane within 220 m of the start.

## For the doc
- **Result:** gliding works in play: double jump, keep Space held -> 10 m/s forward, 2 m/s down (ratio 5), soft landing, no fall damage.
- **In `./run-play.sh`:** jump, Space again in the air (double jump), keep **Space held**: you glide where you look; **S** brakes,
  **A/D** drift, let go of Space to drop. Jump off a summit.
- Numbers: glide ratio 5.00 (test) / 4.96 (play, 124.4 m across for 25.1 m down); landing impact 2.00 m/s -> 0 HP lost (the same drop
  falling = 100 HP, death); 0 frames stopped by unstreamed terrain at 10 m/s; tests 7 new, fast suite 821/0.
- Picks: mouse steers, S brakes, A/D drift; opens after the double jump once falling; no stamina limit.
- Images (Screenshots/93g1/):
  - `1_summit.png` -- standing on the snow summit, looking out along the glide lane; a rocky dome sits on the horizon, the sea behind it.
  - `2_gliding.png` -- 2.5 s into the glide: the HUD line above the dash bar reads "GLIDING 10.0 m/s, sinking 2.0 m/s"; the slope falls away below.
  - `3_looking_down.png` -- 6 s in, camera tilted down: the snow terraces ~19 m below the glider.
  - `4_landed.png` -- after landing 124 m away: HEALTH 100/100 (bottom left), no damage from the landing.
