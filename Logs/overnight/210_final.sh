#!/bin/bash
# Task 210: the FINAL code (192's jungle trees at 14/16 pay for the banyans) -- rebuild, then -trees2 1 vs -trees2 0 at the
# jungle / banyan / forest poses of 210_shots.sh (same cameras) + the redwood from 600 m / 1.5 km (a third, -trees2 1 only
# launch: the survey waits for the shells, cap -lookwait 200 s; 210_far.sh's 40 s left shells 3-5 empty), -gpupoolprobe 1, seed 42, noon.
cd "/Users/hritvikjv/Desktop/Personal Projects/Unity/Voxel Terraria 1 Byte BrickMap"
PL="$HOME/Library/Logs/DefaultCompany/Voxel Terraria 1 Byte BrickMap/Player.log"
O=Logs/overnight/210
P="$(tr ';' '\n' < Logs/TerrainGen/trees210/poses.txt | grep -E '^(forest_100m|jungle_eye|jungle_100m|banyan_near)\|' | paste -sd';' -)"
PF="redwood_far600|3423.2,800.0,5350.4|3423.2,580.0,4750.4;redwood_far1500|3423.2,1150.0,6250.4|3423.2,580.0,4750.4"
SB=0
run() {  # tag flags
  echo "=== final_$1  $(date +%T)"
  SKIP_BUILD=$SB PRESETS=high EXTRA="-worldseed 42 -worldsurvey 1 -mapversion 4 -wsarms native -lookwait ${LW:-22} -wsposes fixed -wsfixed $P -tod 12 -gpupoolprobe 1 $2" ./run-look-review.sh > $O/look_final_$1.out 2>&1
  SB=1
  cp "$PL" $O/player_final_$1.log
  grep -h 'BUILD\|build OK\|error CS' $O/look_final_$1.out | tail -2
  R=$(grep -h "shots:" $O/look_final_$1.out | tail -1 | sed "s/^shots: //"); echo "dir: $R"
  for f in "$R"/ws_fixed_*.png; do n=$(basename "$f" .png); n=${n#ws_fixed_}; n=${n%_native}; cp "$f" "Screenshots/210/final_${n}_$1.png"; done
  cp "$R/lookreview.txt" "Screenshots/210/lookreview_final_$1.txt"
  grep -h "\[Trees2\] library" $O/player_final_$1.log | cut -c1-200 | head -1
  grep -h '\[mem\]\|\[refused\]' $O/look_final_$1.out | tail -2 | cut -c1-400
}
run t2 "-trees2 1"
run t0 "-trees2 0"
P="$PF" LW=200 run farwait_t2 "-trees2 1"   # the giants from far, shells given up to 200 s (the survey's cap) to stream
echo "=== ALL DONE $(date +%T)"
