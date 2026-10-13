STOP_FOR_OWNER -- 33b: the start is now noon with a level camera, but the 45 s acceptance shot is at NIGHT. The task's premise was wrong: the first view is dim because of the clock, not the "start-up light".
The day lasts 120 s (`LightingConfig.json` `dayLengthSeconds: 120`), so 45 s after noon the clock reads 21:00.
32d's dim shot had the same cause (the default clock starts at 12:00 and also reached about 21:00).
**Question (pick one):**
- (a) At the start, pause the clock at noon. P runs it. The task said "do not pause", so I did not.
- (b) Make the day longer for play. 120 s is a lighting-tuning value; 20 min would move about 0.7 h in 45 s.
- (c) Keep it as it is. You start at noon, and the run-play shot uses SHOT=15 instead of 45.
Recommended: **(b)**. The start stays at noon, the clock still runs, and the light stays usable while you look around.

# 33b-play-start-view: code DONE (`f09ca48`). Acceptance "full daylight at 45 s" FAILED (see above). Fast suite green.

## What changed
- `run-play.sh` passes a new flag, **`-startview`**. Only this script passes it.
- `Playground.SettleStartPose(startView)`: after 32d's dry-column move, it now does three things:
  - sets `SunCycle.SetHours(NoonHours)` (the M key's state); the clock keeps running;
  - picks the heading with `MostOpenYaw`: 16 level rays from the eye, the longest clear run to 64 m (ties keep the arena's yaw 45);
  - sets the pitch to 0 through the new `PlaygroundFlyCamera.SetLook(yaw, pitch)`, so mouse look and walk mode continue from the level view instead of snapping back to 18 deg.
- These do not change: capture runs, rigs, `TeleportToArena` via SendMessage, the old non-tg2 world, and the F key (it still does 32d's settle only).
- The logs now report more:
  - the `start pose` line ends with `-startview: noon, level look (yaw N) at t S s`;
  - the `-playshot` line now ends with `t S s, clock HH:MM`.

## Evidence
- Fast EditMode: **PASS 623 FAIL 0 SKIP 13**, run before and after the final edit (`Logs/overnight/33b/editmode_fast.out`, `editmode_fast2.out`).
- In all 4 runs the `-playshot` line says **`(air)`**. The settle happens at t ≈ 12-14 s, about when the SHOT timer starts.
- All four shots are in `Screenshots/Play/`:
  - `start_v4_33b.png`, v4 SHOT=45, clock 21:00, yaw 113. It is a night frame: dark lake across the lower half, dark mountains at the left. The far ridge is about 35% down the frame, under a dark sky.
  - `start_v4_33b_15s.png`, v4 SHOT=15, clock 15:00. **Full daylight.** It looks across the lake to distant blue mountains, with sky in the top third. The far ridge/horizon is about 45-55% down, roughly mid-frame. Green slopes are at the left and right edges, water in the lower half.
  - `start_v3_33b.png`, MAPV=3 SHOT=45, clock 21:00, yaw 45. Night: a grey sky over a dark plateau. The horizon is about 65-70% down; a slope is at the right.
  - `start_v3_33b_15s.png`, MAPV=3 SHOT=15. **Daylight.** A pale sky fills the upper two thirds over grey/white stone ground. The horizon is about 70% down, because the eye is on an 807 m plateau looking out.
- Compare with `start_v4_32d.png`. That shot was taken before this task: dusk, pitched down, facing a hillside, with no sky in frame.
- In every shot the F2 controls panel and the HUD cover the centre of the frame (unchanged; F2 hides them).
- No performance arms were run (rule 10).

## Left / next
- The owner's answer to the question above. For (a) or (b), the change is one line in `SettleStartPose` or in `LightingConfig.json`. Then re-shoot with SHOT=45.
- `Assets/Scenes/Playground.unity` shows as modified, but not by this task. It was already modified when the task started (the scene builder regenerates it), and I left it uncommitted.
