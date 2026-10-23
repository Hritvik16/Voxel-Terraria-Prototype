#!/bin/bash
# task 116: warm-up (discarded), then back to back: ONE cost-split launch (per pose F X X F,
# F = column leap ON (default), X = colleap_off), then MODE=walk 3 min x 4: A B B A
# (A = leap ON, B = -shellcolleap 0). Play-like settings as task 115's walks (noon, FSR 3 at 540, High, shells ON).
cd "$(dirname "$0")/../.."
step() { echo "=== $(date +%T) $1   power: $(pmset -g batt | head -1)"; }
WALK="-renderpreset quality -upscaler fsr3 -renderrows 540 -farband 1 -farshade 1 -seethrough 1 -riverlook 1 -tod 12 -shells 1 -shellworkers 3"
step warmup;  MODE=stream MOTION=walk ./run-tg2world.sh 2>&1 | grep -E "^log:|NOTE|report" | tail -3
step costsplit
SKIP_BUILD=1 EXTRA="-farshade 1 -seethrough 1 -riverlook 1 -costtimed 15 -shells 1 -shellworkers 3 -shellwait 120 -costarms colleap_off -coststillraw" ./run-cost-split.sh 2>&1 | tail -3
for arm in A B B A; do
  if [ "$arm" = A ]; then X=""; else X="-shellcolleap 0"; fi
  step "walk_$arm"
  MODE=walk MINUTES=3 EXTRA="$WALK $X" ./run-tg2world.sh 2>&1 | grep -E "^log:|NOTE|WHOLE RUN|report" | tail -4
done
step ALLDONE
