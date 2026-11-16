#!/bin/bash
# Task 214: -trees2 default ON. ONE build, two arms, 3 poses (210's cameras: Logs/TerrainGen/trees210/poses.txt),
# seed 42, noon, sun behind the camera, -gpupoolprobe 1 (per-pose GPU body accounting).
#   def  (no tree flag)              the new default (task 213's dense world trees)
#   t0   -trees2 0                   today's trees
cd "/Users/hritvikjv/Desktop/Personal Projects/Unity/Voxel Terraria 1 Byte BrickMap"
PL="$HOME/Library/Logs/DefaultCompany/Voxel Terraria 1 Byte BrickMap/Player.log"
O=Logs/overnight/214
mkdir -p $O Screenshots/214
P="$(tr ';' '\n' < Logs/TerrainGen/trees210/poses.txt | grep -E '^(forest_eye|forest_100m|savanna_100m)\|' | paste -sd';' -)"
SB=${SB:-0}
run() {  # tag flags
  echo "=== $1  $(date +%T)"
  SKIP_BUILD=$SB PRESETS=high EXTRA="-worldseed 42 -worldsurvey 1 -mapversion 4 -wsarms native -lookwait ${LW:-30} -wsposes fixed -wsfixed $P -tod 12 -gpupoolprobe 1 $2" ./run-look-review.sh > $O/look_$1.out 2>&1
  SB=1
  cp "$PL" $O/player_$1.log
  grep -h 'BUILD\|build OK\|error CS' $O/look_$1.out | tail -2
  R=$(grep -h "shots:" $O/look_$1.out | tail -1 | sed "s/^shots: //"); echo "dir: $R"
  for f in "$R"/ws_fixed_*.png; do n=$(basename "$f" .png); n=${n#ws_fixed_}; n=${n%_native}; cp "$f" "Screenshots/214/${n}_$1.png"; done
  cp "$R/lookreview.txt" "Screenshots/214/lookreview_$1.txt"
  grep -h "\[Trees2\]\|world trees" $O/player_$1.log | cut -c1-700 | head -1
  grep -h '\[mem\]\|\[refused\]' $O/look_$1.out | tail -2 | cut -c1-400
}
run def ""
run t0 "-trees2 0"
echo "=== ALL DONE $(date +%T)"
