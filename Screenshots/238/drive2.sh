#!/bin/bash
cd "$(dirname "$0")/../../.."
set -uo pipefail
D=Logs/overnight/238
source tools/play-flags.sh
source tools/unity-lock.sh
unity_lock_acquire
DATA="$HOME/Library/Application Support/DefaultCompany/Voxel Terraria 1 Byte BrickMap"
idle() { echo "$(ioreg -c IOHIDSystem | awk '/HIDIdleTime/ {print int($NF/1e9); exit}')/$(Logs/budget/hididle)"; }
touch "$D/.watch"
( while true; do
    R=$(find "$DATA/UpscalerClips" -mindepth 1 -maxdepth 1 -type d -newer "$D/.watch" 2>/dev/null | sort | tail -n 1); N=0
    [ -n "$R" ] && [ -f "$R/costsplit.txt" ] && N=$(grep -cE '^ +[0-9]+ [a-z0-9_]+ +pass' "$R/costsplit.txt")
    echo "$(date +%s) $(Logs/budget/hididle) $(ioreg -c IOHIDSystem | awk '/HIDIdleTime/ {print int($NF/1e9); exit}') $N $(basename "${R:-none}")"; sleep 5
  done ) > "$D/idlewatch.txt" 2>&1 &
WPID=$!
trap 'kill $WPID 2>/dev/null; unity_lock_release' EXIT
echo "start $(date +%T) idle $(idle)"
if [ "${SKIP_BUILD:-0}" != "1" ]; then
  DEV_KERNELS=0 BUILD_ONLY=1 ./run-cost-split.sh > "$D/build.out" 2>&1
  tail -1 "$D/build.out" | grep -q DONE || { echo "BUILD FAILED"; tail -5 "$D/build.out"; exit 2; }
  echo "build end $(date +%T)"
fi
COMMON="$PLAY_SETTINGS_FLAGS -quality high -renderrows 432 -shellwait 60 -startsun 16 -costfixed forest_eye|3366.4,532.5,4950.0|3366.4,529.7,4928.0 -costtimedpaths forest_eye"
launch() {  # tag extra
  touch "$D/.watch"; echo "$1 start $(date +%T) epoch $(date +%s) idle $(idle)"
  SKIP_BUILD=1 EXTRA="$COMMON $2" ./run-cost-split.sh > "$D/launch_$1.out" 2>&1 < /dev/null
  r=$(sed -n 's/^report: \([^ ]*\) .*/\1/p' "$D/launch_$1.out"); cp "$r" "$D/costsplit_$1.txt" 2>/dev/null
  dd=$(sed -n 's/^report: [^ ]* *data: \(.*\)$/\1/p' "$D/launch_$1.out"); echo "$dd" > "$D/data_$1.txt"
  echo "$1 end $(date +%T) epoch $(date +%s) idle $(idle) report $r"
}
launch D "-costarms far_off,skypale_off,grade_off"
echo "ALLDONE $(date +%T)"
