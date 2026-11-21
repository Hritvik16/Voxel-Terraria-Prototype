#!/bin/bash
# Task 210: the giants from far (shells 3-5), elevated cameras so no hill blocks the view (210_shots.sh's far2 / far3 were
# inside / behind terrain). Same build (SKIP_BUILD=1), -trees2 1 vs -trees2 0, seed 42, noon, sun behind (camera on +z).
cd "/Users/hritvikjv/Desktop/Personal Projects/Unity/Voxel Terraria 1 Byte BrickMap"
PL="$HOME/Library/Logs/DefaultCompany/Voxel Terraria 1 Byte BrickMap/Player.log"
O=Logs/overnight/210
P="redwood_far600|3423.2,800.0,5350.4|3423.2,580.0,4750.4;redwood_far1500|3423.2,1150.0,6250.4|3423.2,580.0,4750.4;banyan_far600|5376.8,780.0,6044.8|5376.8,530.0,5444.8"
run() {  # tag flags
  echo "=== far_$1  $(date +%T)"
  SKIP_BUILD=1 PRESETS=high EXTRA="-worldseed 42 -worldsurvey 1 -mapversion 4 -wsarms native -lookwait 40 -wsposes fixed -wsfixed $P -tod 12 $2" ./run-look-review.sh > $O/look_far_$1.out 2>&1
  cp "$PL" $O/player_far_$1.log
  R=$(grep -h "shots:" $O/look_far_$1.out | tail -1 | sed "s/^shots: //"); echo "dir: $R"
  for f in "$R"/ws_fixed_*.png; do n=$(basename "$f" .png); n=${n#ws_fixed_}; n=${n%_native}; cp "$f" "Screenshots/210/${n}_$1.png"; done
  cp "$R/lookreview.txt" "Screenshots/210/lookreview_far_$1.txt"
  grep -h "\[shells\]" "$R/lookreview.txt" | cut -c1-260
}
run t2 "-trees2 1"
run t0 "-trees2 0"
echo "=== ALL DONE $(date +%T)"
