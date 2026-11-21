#!/bin/bash
# Task 195: rule ores ON by default -- new default vs -ores 0, 181's wall poses, midday. One build, one launch per arm.
cd "/Users/hritvikjv/Desktop/Personal Projects/Unity/Voxel Terraria 1 Byte BrickMap"
PL="$HOME/Library/Logs/DefaultCompany/Voxel Terraria 1 Byte BrickMap/Player.log"
O=Logs/overnight/195
P="$(cat Logs/TerrainGen/ores181/poses.txt)"
SB=${SB:-0}
run() {  # name flags
  echo "=== $1  $(date +%T)"
  SKIP_BUILD=$SB PRESETS=high EXTRA="-worldsurvey 1 -mapversion 4 -wsarms native -lookwait 45 -wsposes fixed -wsfixed $P -tod 12 $2" ./run-look-review.sh > $O/look_$1.out 2>&1
  SB=1
  cp "$PL" $O/player_$1.log
  grep -h 'BUILD\|build OK\|shots:' $O/look_$1.out | tail -3
  R=$(grep -h "shots:" $O/look_$1.out | tail -1 | sed "s/^shots: //"); echo "dir: $R"
  grep -h "tg2 chunk source\|POOL FULL" $O/player_$1.log | cut -c1-400 | head -3
  grep -h "WSPOSE\|\[shells\]\|\[pools\]\|\[mem\]\|missing\|peak" "$R/lookreview.txt" | cut -c1-400
}
run default ""
run off "-ores 0"
echo "=== ALL DONE $(date +%T)"
