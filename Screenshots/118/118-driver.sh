#!/bin/bash
# task 118: the voxel sun-shadow clamp, -shadowclamp 128 vs today's (inherit = primary ray's tier-2 bound, ~290 m).
# Play settings: High (the shells' default preset), 540 rows, FSR 3, shells ON (3 workers), far band + far shading.
# 1) build + look stills (no timing): walk_near / walk_open / ridge / high, golden hour sun behind + beside, full vs clamp128.
# 2) NOT RUN in task 118 (the clamp is a no-op with shells on: identical kernel input). warm-up (discarded), then back to back: cost-split noon then golden (-costsun 14.5), arms clamp128 x2 (F X X F X X F),
#    then MODE=walk golden (-tod 17) 3 min x 4: A B B A (A = full, B = -shadowclamp 128).
cd "$(dirname "$0")/../.."
step() { echo "=== $(date +%T) $1   power: $(pmset -g batt | head -1)"; }
step look
PRESETS="high" EXTRA="-lookrows 540 -lookposes walk_near,walk_open,ridge,high -lookbursts 0 -lookgolden 1 -lookclamp 128 -lookwait 60" ./run-look-review.sh 2>&1 | grep -E "BUILD|build|shots:|^log:|waited|== |golden|error" | tail -30
WALK="-renderpreset quality -upscaler fsr3 -renderrows 540 -farband 1 -farshade 1 -seethrough 1 -riverlook 1 -tod 17 -shells 1 -shellworkers 3"
step warmup;  MODE=stream MOTION=walk ./run-tg2world.sh 2>&1 | grep -E "^log:|NOTE|report" | tail -3
for sun in noon golden; do
  if [ "$sun" = golden ]; then X="-costsun 14.5"; else X=""; fi
  step "costsplit_$sun"
  SKIP_BUILD=1 EXTRA="-farshade 1 -seethrough 1 -riverlook 1 -costtimed 15 -shells 1 -shellworkers 3 -shellwait 90 -costarms clamp128,clamp128 $X" ./run-cost-split.sh 2>&1 | tail -2
done
for arm in A B B A; do
  if [ "$arm" = A ]; then X=""; else X="-shadowclamp 128"; fi
  step "walk_$arm"
  MODE=walk MINUTES=3 EXTRA="$WALK $X" ./run-tg2world.sh 2>&1 | grep -E "^log:|NOTE|WHOLE RUN|report" | tail -4
done
step ALLDONE
