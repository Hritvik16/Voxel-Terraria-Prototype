#!/bin/bash
# task 124: temporal sun-shadow reuse one frame in 8 (-shadowreuse 8) vs today's 4.
# Play settings: High, 540 rows, FSR 3, shells ON (3 workers), far band + far shading, temporal shadows ON (the preset's).
# 1) build + look stills (no timing): walk_near / walk_open, golden hour, reuse 1 (reference) / 4 / 8: still, moving sun, edit.
# 2) warm-up (discarded), then back to back: cost-split noon then golden (-costsun 14.5), arms reuse8 x2 (F X X F X X F).
cd "$(dirname "$0")/../.."
step() { echo "=== $(date +%T) $1   power: $(pmset -g batt | head -1)"; }
step look
PRESETS="high" EXTRA="-lookrows 540 -lookposes walk_near,walk_open -lookbursts 0 -lookgolden 1 -lookreuse 8 -lookwait 60" ./run-look-review.sh 2>&1 | grep -E "BUILD|build|shots:|^log:|waited|== |golden|reuse|error|ERROR" | tail -30
step warmup;  MODE=stream MOTION=walk ./run-tg2world.sh 2>&1 | grep -E "^log:|NOTE|report" | tail -3
for sun in noon golden; do
  if [ "$sun" = golden ]; then X="-costsun 14.5"; else X=""; fi
  step "costsplit_$sun"
  SKIP_BUILD=1 EXTRA="-farshade 1 -seethrough 1 -riverlook 1 -costtimed 15 -shells 1 -shellworkers 3 -shellwait 90 -costarms reuse8,reuse8 $X" ./run-cost-split.sh 2>&1 | tail -2
done
step ALLDONE
