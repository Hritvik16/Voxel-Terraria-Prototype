#!/bin/bash
# task 93b third run (after 99, sky-ray exit ON by default; mask OFF): same arms as 93b-rerun-driver.sh.
# AC only: waits for AC before the warm-up; aborts (POWER LOST) if AC is gone before any later arm.
cd "$(dirname "$0")/../.."
COMMON="-farshade 1 -seethrough 1 -riverlook 1"
SH="-shells 1 -shellworkers 3"
ac() { pmset -g batt | head -1 | grep -q "AC Power"; }
step() { echo "=== $(date +%T) $1   power: $(pmset -g batt | head -1)"; ac || { echo "POWER LOST before $1"; exit 3; }; }
until ac; do sleep 30; done
echo "=== $(date +%T) AC present, starting"
step warmup;  MODE=stream MOTION=walk ./run-tg2world.sh 2>&1 | grep -E "^log:|NOTE" | tail -3
for A in OFF ON ON OFF; do
  step "costsplit $A"
  if [ $A = ON ]; then X="$COMMON -costtimed 15 $SH -shellwait 120 -costarms shells_off,far_off,fshade_off"
  else X="$COMMON -costtimed 15 -costarms far_off,fshade_off"; fi
  SKIP_BUILD=1 EXTRA="$X" ./run-cost-split.sh 2>&1 | grep -E "^report:|NOTE|WARN" | tail -3
done
for A in OFF ON ON OFF; do
  step "walk $A"
  X="-renderpreset quality -upscaler fsr3 -renderrows 540 -farband 1 $COMMON -tod 12 -memledger -frametiming"
  [ $A = ON ] && X="$X $SH"
  MODE=walk MINUTES=3 EXTRA="$X" ./run-tg2world.sh 2>&1 | grep -E "peak phys|WHOLE RUN|^log:|NOTE|WARNING"
done
step ALLDONE
