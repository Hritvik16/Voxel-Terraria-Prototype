#!/bin/bash
# Task 195a: trees + towns ON by default -- new default vs -trees 0 -towns 0 (the pre-195a world), 191's forest,
# snow and town poses, seed 42, midday. One build, one launch per arm (the flags are per launch).
cd "/Users/hritvikjv/Desktop/Personal Projects/Unity/Voxel Terraria 1 Byte BrickMap"
PL="$HOME/Library/Logs/DefaultCompany/Voxel Terraria 1 Byte BrickMap/Player.log"
O=Logs/overnight/195a
P="forest|3366.4,544.2,4973.0|3366.4,530.7,4928.0;snow|4595.2,537.6,5792.2|4595.2,527.6,5747.2;$(cat Logs/TerrainGen/towns190/poses_42.txt | cut -d';' -f1,2)"
SB=${SB:-0}
mkdir -p Screenshots/195a
run() {  # name flags
  echo "=== $1  $(date +%T)"
  SKIP_BUILD=$SB PRESETS=high EXTRA="-worldseed 42 -worldsurvey 1 -mapversion 4 -wsarms native -lookwait 45 -wsposes fixed -wsfixed $P -tod 12 $2" ./run-look-review.sh > $O/look_$1.out 2>&1
  SB=1
  cp "$PL" $O/player_$1.log
  grep -h 'BUILD\|build OK\|shots:' $O/look_$1.out | tail -3
  R=$(grep -h "shots:" $O/look_$1.out | tail -1 | sed "s/^shots: //"); echo "dir: $R"
  for f in "$R"/ws_fixed_*_native.png; do n=$(basename "$f" _native.png); n=${n#ws_fixed_}; cp "$f" "Screenshots/195a/${n}_$1.png"; done
  cp "$R/lookreview.txt" "Screenshots/195a/lookreview_$1.txt"
  grep -h "tg2 chunk source\|POOL FULL\|\[Towns\]" $O/player_$1.log | cut -c1-600 | head -4
  grep -h "WSPOSE\|\[shells\]\|\[pools\]\|\[mem\]\|\[refused\]\|missing" "$R/lookreview.txt" | cut -c1-400
}
run default ""
run off "-trees 0 -towns 0"
echo "=== ALL DONE $(date +%T)"
