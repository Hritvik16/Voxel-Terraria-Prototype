#!/bin/bash
# Task 210: -trees2 1 (world space-colonization trees) vs -trees2 0 (today's trees), ONE build, same poses, seed 42, noon,
# sun behind the camera (camera on +z looking -z). Poses: Trees2Tests.Poses210 (Logs/TerrainGen/trees210/poses.txt)
# + a redwood at ~1.8 km + 201a's town_eye. -gpupoolprobe 1: per-pose GPU body accounting (tree bodies, refusals).
cd "/Users/hritvikjv/Desktop/Personal Projects/Unity/Voxel Terraria 1 Byte BrickMap"
PL="$HOME/Library/Logs/DefaultCompany/Voxel Terraria 1 Byte BrickMap/Player.log"
O=Logs/overnight/210
mkdir -p $O Screenshots/210
P="$(cat Logs/TerrainGen/trees210/poses.txt);redwood_far3|3423.2,700.0,6550.4|3423.2,549.1,4750.4;town_eye|2724.0,635.4,3319.2|2724.0,636.7,3268.0"
SB=${SB:-0}
run() {  # tag flags
  echo "=== $1  $(date +%T)"
  SKIP_BUILD=$SB PRESETS=high EXTRA="-worldseed 42 -worldsurvey 1 -mapversion 4 -wsarms native -lookwait ${LW:-22} -wsposes fixed -wsfixed $P -tod 12 -gpupoolprobe 1 $2" ./run-look-review.sh > $O/look_$1.out 2>&1
  SB=1
  cp "$PL" $O/player_$1.log
  grep -h 'BUILD\|build OK\|shots:\|error CS' $O/look_$1.out | tail -4
  R=$(grep -h "shots:" $O/look_$1.out | tail -1 | sed "s/^shots: //"); echo "dir: $R"
  for f in "$R"/ws_fixed_*.png; do n=$(basename "$f" .png); n=${n#ws_fixed_}; n=${n%_native}; cp "$f" "Screenshots/210/${n}_$1.png"; done
  cp "$R/lookreview.txt" "Screenshots/210/lookreview_$1.txt"
  grep -h "\[Trees2\]" $O/player_$1.log | cut -c1-600 | head -3
  grep -h '\[mem\]\|\[refused\]' $O/look_$1.out | tail -2 | cut -c1-400
}
run t2 "-trees2 1"
run t0 "-trees2 0"
echo "=== ALL DONE $(date +%T)"
