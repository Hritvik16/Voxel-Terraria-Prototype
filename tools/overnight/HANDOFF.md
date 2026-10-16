# 93d1-gameplay-step-9a: DONE -- health, fall damage, air under liquids, lava/acid damage, soft death to the play start
(93b2's STOP_FOR_OWNER Q53, shells 1-2 brick caps, is still open: see `tools/overnight/blocked/93b2-shells-fill-while-walking.HANDOFF.md`.)

## What changed (commit 327169d, branch engine-phase)
- `PlayerVitals.cs` (new, CoreEngine/Gameplay): plain class. It holds health, air, regen and the death state, and has `Sense()`, which uses the same
  feet/centre/head probes as Buoyancy (a gas is not a liquid), and `RespawnTarget()` (the play start, else the death column).
- `PlayerMotor`: one new output field, `LastImpactDownMps` (the downward speed when the feet hit a solid). Nothing else changed; the legacy Step is the same.
- `PlayerConfig`: 12 new tuning fields (all validated): maxHealth, fallSafe/LethalHeightM, oxygenSeconds, oxygenRefillPerS,
  drown/lava/acidDamagePerS, regenDelayS, regenPerS, respawnDelayS. The play config file picks them up automatically.
- `Playground.Vitals.cs` (new): ticks the vitals after each walk step and runs the death screen and respawn (93c1's R path to the play start).
  It also draws the HUD: a HEALTH bar at the left end of the hotbar caption row, an AIR bar at the right end (only shown when not full), a red flash on damage, and the "YOU DIED" screen.
  `Playground.cs`: a dead body does not dig or walk; added an F2 help line; added the `-vitalsdemo` / `-vitals 0` flags.
- Only active in interactive Survival WALK. Creative, fly, rigs, capture runs, the older demos (-movedemo, -strokedemo, ...) and `-vitals 0` take no damage.
- No frozen API, no D.1 or memory-layout change. Nothing new is saved: health is full on every launch.

## Picks (owner may change -- all in PlayerConfig.json except the last three)
- Health 100. Fall damage is linear in fall HEIGHT: no damage up to 4 m, 100 HP at 20 m, so 6.25 HP per metre above 4 m.
  A jump (0.87 m) and a double jump plus dash cost nothing.
- Landing with liquid at your feet takes no fall damage, even in shallow water (Terraria's rule).
- Air lasts 20 s while your head is in a liquid. After that, drowning costs 10 HP/s. Air refills 5x faster than it drains (empty to full in 4 s).
- Lava costs 50 HP/s (2 s kills you); acid costs 20 HP/s. Sludge, oil, honey and quicksand do no damage.
- Health regenerates at 2 HP/s once 8 s have passed without damage.
- The death screen lasts 3 s, then you respawn at the play start with full health and air. The inventory is untouched ("soft").
- R does not heal you (otherwise it would be a free heal). Landing from a mode switch into walk is free: Tab from fly, a respawn, the start pose or a KEEP=1 resume.
- A dash zeroes your falling speed, so a dash just before landing avoids fall damage (Terraria's double jump does the same). Left as a skill move.

## Evidence
- `PlayerVitalsTests` (10): fall curve (0 at 4 m, 25 at 8 m, 50 at 12 m, 100 at 20 m, capped); real motor drops (3 m: 0, 12 m: ~50 +-4, 25 m: dead);
  0.3 m of water cushions a 15 m fall; air 10 s -> half, 20 s -> empty with zero damage before that, 5 s drowning -> 50 HP, refill in 4 s;
  wading in lava is 50/s and kills in 2 s, acid 20/s, water 0; regen delay; lava death -> 3 s screen -> respawn at the start (not in the lava) with full bars;
  Validate keeps lethal above safe. Filter PASS 10; **fast suite PASS 773 FAIL 0 SKIP 14** (`Logs/overnight/93d1-fast.out`).
- One `-vitalsdemo` launch (`Logs/overnight/93d1-demo2.out`, log lines in `Screenshots/93d1/vitalsdemo.txt`):
  12 m fall: impact 23.08 m/s = 12.10 m, 50.6 damage. Air ran out after 20.1 s; drowning then took health to 60.
  Acid for 1 s took 11.7 HP: the demo caps a step at 1/30 s and the game ran under 30 fps, so game time ran slower than real time.
  Lava killed the player. The respawn came 6.9 s after the lava death, **0.0 m from the play start**, and the inventory was **50 -> 50**.
- A bug found in the first launch and fixed: the drowning formula leaked a float-rounding sliver of damage on every step under water.
  That kept the red flash on for the whole dive. The test now asserts exactly 100 HP and no hit while air lasts.

## Next
- 9b (death mode per world) waits for Q3. Otherwise the plan order goes on: RX1 -> 5 -> 6 ...
- Not done here: health does not persist through KEEP=1, and there is no death or damage sound. Those are later rows (9b / audio).

## For the doc
**Result:** gameplay step 9a is done. Survival now has health, fall damage, air under water, lava and acid damage, and a soft death that sends you back to the start.
- **What the owner can do in `./run-play.sh`** (Survival walk, no new keys):
  - Fall more than 4 m and the HEALTH bar drops, with a red flash. 20 m kills.
  - Dive (Ctrl): the AIR bar appears and runs for 20 s, then you drown.
  - Walk into lava or acid and you take damage.
  - At 0 HP you get "YOU DIED", then you are back at the start after 3 s with everything kept.
  - Tuning is in PlayerConfig.json. `EXTRA="-vitals 0"` turns damage off; `EXTRA="-vitalsdemo"` takes the 7 shots.
- Numbers: 12 m fall 100 -> 49.4 HP (curve 50.6); air 20.1 s then drowning to 60 HP; lava 2 s kills; respawn 0.0 m from the play start, inventory 50 -> 50 (`Screenshots/93d1/vitalsdemo.txt`).
- Picks: 100 HP; safe 4 m / lethal 20 m; 20 s air; lava 50 / acid 20 HP/s; regen 2 HP/s after 8 s; 3 s death screen; water at the feet cushions falls.
- Images (`Screenshots/93d1/`):
  - `1_full_health.png`: bottom centre, the red HEALTH 100/100 bar at the left end of the hotbar caption row. Noon, sun behind, at the play start.
  - `2_after_fall_12m.png`: 3 frames after a 12 m drop. The whole frame has a red damage flash, and the bar reads about 52/100.
  - `3_under_water_air.png`: on the bed of a 2.5 m water tank, looking up at the blue surface. The blue AIR bar (right end) is at half (9 s); health is still full.
  - `4_drowning.png`: the same pose once the air has run out. The AIR bar reads "drowning!", the frame has a red flash and health is 60.
  - `5_lava.png`: wading in a lava tank. Health is 59 and the frame is pink from the constant hit flash.
  - `6_you_died.png`: the death screen, "YOU DIED / burned in lava / back to the start in 2 s -- you keep everything".
  - `7_respawned_at_start.png`: back at the play start with full health. The two demo tanks (dark stone) are ahead.
