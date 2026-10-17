#!/bin/bash
# task 99: warm-up (discarded), then ONE cost-split launch: F X X F per pose, F = sky exit OFF (default), X = skyexit_on.
cd "$(dirname "$0")/../.."
step() { echo "=== $(date +%T) $1   power: $(pmset -g batt | head -1)"; }
step warmup;  MODE=stream MOTION=walk ./run-tg2world.sh 2>&1 | grep -E "^log:|NOTE" | tail -3
step costsplit
SKIP_BUILD=1 EXTRA="-farshade 1 -seethrough 1 -riverlook 1 -costtimed 15 -shells 1 -shellworkers 3 -shellwait 120 -costarms skyexit_on -coststillraw" ./run-cost-split.sh 2>&1 | tail -3
step ALLDONE
