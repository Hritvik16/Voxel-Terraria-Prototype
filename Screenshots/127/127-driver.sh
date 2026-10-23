#!/bin/bash
# task 127: cross-lit comparison stills (-lookcross 1). Capture-only, no timing. Build from 09:51 (SKIP_BUILD).
# 1) High: Q75 poses + biome borders (blend ON)  2) Medium: Q75 poses  3) High, -biomeblend 0 -biometint 0: borders only.
# -shellworkers 6 (last match wins over the script's 3): try1/try2 at 3 workers never filled shells 6-7 at walk height.
cd "$(dirname "$0")/../.."
C="-lookrows 540 -lookcross 1 -lookwait 120 -shellworkers 6 ${CROSS:-}"
Q="x_high,x_ridge,x_coast,x_bird800,x_bird2300"
F="ERROR|shots:|^log:|waited|== |LOOK CROSS|flat plane|no LookReview"
echo "=== $(date +%T) high"
SKIP_BUILD=1 PRESETS=high EXTRA="$C -lookbiome 1 -lookposes $Q,xb_0,xb_1,xb_2" ./run-look-review.sh 2>&1 | grep -E "$F" | tail -40
echo "=== $(date +%T) medium"
SKIP_BUILD=1 PRESETS=medium EXTRA="$C -lookposes $Q" ./run-look-review.sh 2>&1 | grep -E "$F" | tail -30
echo "=== $(date +%T) high blend off"
SKIP_BUILD=1 PRESETS=high EXTRA="$C -lookcrossprefill 0 -lookbiome 1 -lookposes xb_0,xb_1,xb_2 -biomeblend 0 -biometint 0" ./run-look-review.sh 2>&1 | grep -E "$F" | tail -20
echo "=== $(date +%T) ALLDONE"
