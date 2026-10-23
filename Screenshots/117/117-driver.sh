#!/bin/bash
# task 117: High vs Medium (-quality), both at 540 rows, FSR 3, shells ON.
# 1) build + look stills (no timing): one launch per preset, walk_near / walk_open / high, -lookrows 540.
# 2) warm-up (discarded), then back to back: cost-split launches H M M H (FULL + shells_off),
#    then MODE=walk 3 min x 4: A B B A (A = High, B = -quality medium). Play-like walk flags as 116 + -memledger.
cd "$(dirname "$0")/../.."
step() { echo "=== $(date +%T) $1   power: $(pmset -g batt | head -1)"; }
step look
PRESETS="high medium" EXTRA="-lookrows 540 -lookposes walk_near,walk_open,high -lookbursts 0 -lookwait 90" ./run-look-review.sh 2>&1 | grep -E "BUILD|build|shots:|^log:|waited|== " | tail -20
WALK="-renderpreset quality -upscaler fsr3 -renderrows 540 -farband 1 -farshade 1 -seethrough 1 -riverlook 1 -tod 12 -shells 1 -shellworkers 3 -memledger"
step warmup;  MODE=stream MOTION=walk ./run-tg2world.sh 2>&1 | grep -E "^log:|NOTE|report" | tail -3
for arm in A B B A; do
  if [ "$arm" = A ]; then X="-quality high"; else X="-quality medium"; fi
  step "costsplit_$arm"
  SKIP_BUILD=1 EXTRA="-farshade 1 -seethrough 1 -riverlook 1 -costtimed 15 -shells 1 -shellworkers 3 -shellwait 120 -costarms shells_off $X" ./run-cost-split.sh 2>&1 | tail -2
done
for arm in A B B A; do
  if [ "$arm" = A ]; then X="-quality high"; else X="-quality medium"; fi
  step "walk_$arm"
  MODE=walk MINUTES=3 EXTRA="$WALK $X" ./run-tg2world.sh 2>&1 | grep -E "^log:|NOTE|WHOLE RUN|report" | tail -4
done
step ALLDONE
