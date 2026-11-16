#!/bin/bash
# Task 213: the denser forest. ONE build, three arms, same 4 poses (210's cameras: Logs/TerrainGen/trees210/poses.txt),
# seed 42, noon, sun behind the camera, -gpupoolprobe 1 (per-pose GPU body accounting).
#   d   -trees2 1                    task 213's denser forest (the new meaning of -trees2 1)
#   s   -trees2 1 -trees2dense 0     task 210's placement byte for byte
#   t0  -trees2 0                    today's trees
cd "/Users/hritvikjv/Desktop/Personal Projects/Unity/Voxel Terraria 1 Byte BrickMap"
PL="$HOME/Library/Logs/DefaultCompany/Voxel Terraria 1 Byte BrickMap/Player.log"
O=Logs/overnight/213
mkdir -p $O Screenshots/213
P="$(tr ';' '\n' < Logs/TerrainGen/trees210/poses.txt | grep -E '^(forest_eye|forest_100m|snow_100m|savanna_100m)\|' | paste -sd';' -)"
SB=${SB:-0}
run() {  # tag flags
  echo "=== $1  $(date +%T)"
  SKIP_BUILD=$SB PRESETS=high EXTRA="-worldseed 42 -worldsurvey 1 -mapversion 4 -wsarms native -lookwait ${LW:-30} -wsposes fixed -wsfixed $P -tod 12 -gpupoolprobe 1 $2" ./run-look-review.sh > $O/look_$1.out 2>&1
  SB=1
  cp "$PL" $O/player_$1.log
  grep -h 'BUILD\|build OK\|error CS' $O/look_$1.out | tail -2
  R=$(grep -h "shots:" $O/look_$1.out | tail -1 | sed "s/^shots: //"); echo "dir: $R"
  for f in "$R"/ws_fixed_*.png; do n=$(basename "$f" .png); n=${n#ws_fixed_}; n=${n%_native}; cp "$f" "Screenshots/213/${n}_$1.png"; done
  cp "$R/lookreview.txt" "Screenshots/213/lookreview_$1.txt"
  grep -h "\[Trees2\] library" $O/player_$1.log | cut -c1-700 | head -1
  grep -h '\[mem\]\|\[refused\]' $O/look_$1.out | tail -2 | cut -c1-400
}
run d "-trees2 1"
run s "-trees2 1 -trees2dense 0"
run t0 "-trees2 0"
echo "=== ALL DONE $(date +%T)"
