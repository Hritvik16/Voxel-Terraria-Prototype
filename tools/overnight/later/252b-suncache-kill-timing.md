# 252b-suncache-kill-timing

Model: opus

Approval: OWNER 2026-11-20 ("go with recommended for all questions"): Q-247 (a) staged trial of amendment 8.18, of which this is step
247b, the one timed kill test; Q-249 (b) the near zone (task 251, `AMENDMENT_8_18_CACHED_SUN_SHADOW.md` sec. 1.3a). Rewritten from
8.18 sec. 6's 247b outline by task 251 so its arms and kill thresholds match the near-zone rule.

NEEDS AN IDLE MAC (~45 min of Unity): parked in `later/`. The supervisor releases it with
`tools/overnight/release-when-idle.sh later/252b-suncache-kill-timing.md 20` (20 min without input). Do NOT run it while the owner works.
This IS a performance task (rule 10 allows timing here); rule 9 applies: one discarded warm-up launch, then the arms back to back.

## Decision table (written before measuring; arms below)
`suncontact` = the cache's cheapest per-face part (contact ray everywhere, no near legs): the LOWER bound of the lookup's cost.
`suncontactnear` = the same plus the near zone's legs on EVERY face < 12.8 m (an UPPER bound: the real rule runs a leg only where the
cache says "not fully lit", task 251 measured legs on ~60-100 % of faces under a canopy and ~0 % on open lit ground).
Quote both as `arm - sun_off` at forest_eye 16 deg (the frame's sun step cost of that per-face part).
- `suncontact - sun_off` > 15 ms -> **KILL** 8.18: even without the near zone the per-face kernel eats the saving (the cost is the
  kernel, not the ray length: 124/232's warning). Next: F3 solid crown cores and the "rest" split instead.
- `suncontactnear - sun_off` < 8 ms -> **GO**: queue 247c (GPU step 1, `-suncache 1`, OFF by default) with the near zone.
- anything else -> **owner call**: report both numbers, the step ratio task 251 predicted, and walk_near's numbers.
- walk_near 16 deg is reported, not a kill rule: every receiver there is inside the near zone, so 8.18 is expected to save little there
  (task 251's estimate); `suncontactnear` slower than FULL at walk_near is a finding to report, not a kill.
Every outcome picks a different next step, so the measurement is worth running.

## Do
1. Add TWO capture-only cost arms to `Assets/Game/PlaygroundCapture.CostSplit.cs` (no product behaviour change; one shader keyword or
   uniform pair read only when the arm is on, default off; shader pins updated):
   - `suncontact`: the far-shading kernels cap the tier-0 sun ray (`TraceShadowRay`) at 8 cells and skip task 133's shell continuation,
     plus 8 dummy taps of a constant 1x1 texture (the 4 bilinear taps x 2 slots of 8.18 sec. 3.2). Task 80's shell-hit ray unchanged.
   - `suncontactnear`: as `suncontact`, but a face whose camera distance is < 12.8 m (8.18 `NEAR_ZONE_VOX`) caps its tier-0 ray at
     8 + 128 cells (contact + `NEAR_LEG_CAP`) instead of 8, still without the continuation.
   Read `KNOWN_ISSUES.md` (shadows, cost split) and TASKS.md tasks 124 / 232 / 238 first. Fast EditMode suite once before committing.
2. One discarded warm-up launch (`MODE=stream MOTION=walk ./run-tg2world.sh`, ~6 min). Then cost splits back to back, no idle gap over
   ~2 min, ABBA (`./run-cost-split.sh`, 238's flags): `$PLAY_SETTINGS_FLAGS -quality high -renderrows 432 -shellwait 60 -startsun 16
   -costfixed forest_eye,walk_near -costtimedpaths forest_eye,walk_near -costarms sun_off,suncontact,suncontactnear,suncontactnear,suncontact,sun_off`
   (FULL = today is in every launch). Use `tools/unity-lock.sh` (the runner holds it).
3. **Idle log:** before and after EVERY timed arm, log `ioreg -c IOHIDSystem | awk '/HIDIdleTime/ {print int($NF/1e9); exit}'` (seconds
   idle) and `Logs/budget/hididle` (238's `Screenshots/238/drive2.sh` does both; copy its `idle()` helper and idlewatch). If the owner
   touched the Mac during an arm (idle after < arm length), rerun that arm ONCE and mark it; if it happens again, report the arm as
   "owner active" and do not quote it. Rigs that print `NOTE: owner paused during this run`: rerun that arm.
4. Report (warm steady state, the warm-up's log path): FULL / sun_off / suncontact / suncontactnear p50 and p95 at forest_eye 16 deg and
   walk_near 16 deg; `arm - sun_off` for both new arms; which decision row applies. `-raystats 1` sun rays/steps per band for FULL vs the
   two arms (rule 12 P5) if the cost split already prints them. Memory per rule 11 vs FULL (process peak, GPU pool peak + refusals, CPU
   pool + valve): expected ~0 (capture-only arms).

## Rules
One code change (the two arms). Commit the arms + text reports; images are not committed. HEAVY: no (capture-only arms, no new pass).
`## For the doc`: the 4-arm table at both poses, the decision row, and one sentence on what it means for 8.18 (go / kill / owner call).
No image needed (nothing drawn changes in play); say so.
