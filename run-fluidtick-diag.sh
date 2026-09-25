#!/bin/bash
# run-fluidtick-diag.sh -- owner decision 11: WHY does one fluid CA tick take
# 6-7 frames? DIAGNOSIS ONLY -- every arm runs the shipped fluid path; the
# arms vary only the frame rate and the GPU frame queue, and -sustained prints
# the tick period split into readback latency / drain / issue gap.
#   A  shipped:           gate 960, maxQueuedFrames as shipped
#   B  queue depth 1:     gate 960, -maxqueued 1   (is the latency the frame queue?)
#   C  higher frame rate: gate 560                 (is it frames, or milliseconds?)
# Golden hour, orbit, rung 0, live fluid (arena vents), FULLSCREEN, MINUTES each (default 3).
#
#   SKIP_BUILD=1 ./run-fluidtick-diag.sh
set -uo pipefail
APP="Builds/Playground.app"
DATA="$HOME/Library/Application Support/DefaultCompany/Voxel Terraria 1 Byte BrickMap"
MINUTES="${MINUTES:-3}"
mkdir -p Logs
LOG="Logs/fluidtick_diag_$(date +%Y%m%d_%H%M%S).txt"
source tools/unity-lock.sh
unity_lock_acquire
[ -d "$APP" ] || { echo "no $APP -- build it first (run-looksweep.sh builds the same player)"; exit 1; }

run_arm () {  # tag, then extra args
  local tag=$1; shift
  sleep 60
  echo "-- arm $tag ($*)  load: $(uptime | sed 's/.*load averages*: //')" | tee -a "$LOG"
  caffeinate -dims open -n -W "$APP" --args -sustained -motion orbit -shadowlevel 0 \
    -minutes "$MINUTES" -tag "ftick_$tag" -cleardeltas "$@"
  S=$(ls -td "$DATA/Sustained"/*_ftick_$tag/ | head -n 1)
  grep -E "clock PINNED|maxQueuedFrames|FIRST|LAST|fluid CA|TICK PERIOD|issue ->|landed ->|drained ->|ops per tick|histogram" \
    "${S}sustained.txt" | tee -a "$LOG"
}
run_arm A_shipped -gateres 960
run_arm B_queue1  -gateres 960 -maxqueued 1
run_arm C_gate560 -gateres 560
echo "" | tee -a "$LOG"; echo "log: $LOG"
