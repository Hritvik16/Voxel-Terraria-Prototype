#!/bin/bash
# Task 216 proof pair: 1 build, launch b1 = GPU view eviction ON (default), b2 = -gpuviewevict 0 (today).
# 208's poses, fewer of them: jungle_high + walk1/walk2 (40 m steps along its view, -Z); -mirrorprobe 1.
# -trees2 0: with world trees ON (214 default) both arms reached a 9-10 GB footprint at jungle_high and the watchdog killed them (Logs/overnight/216/killed/).
cd "/Users/hritvikjv/Desktop/Personal Projects/Unity/Voxel Terraria 1 Byte BrickMap"
PL="$HOME/Library/Logs/DefaultCompany/Voxel Terraria 1 Byte BrickMap/Player.log"
O=Logs/overnight/216
P="jungle_high|5772.8,566.3,5024.2|5772.8,536.3,4979.2;walk1|5772.8,566.3,4984.2|5772.8,536.3,4939.2;walk2|5772.8,566.3,4944.2|5772.8,536.3,4899.2"
SB=0
for r in "b1|1" "b2|0"; do
  n=${r%|*}; ve=${r#*|}
  echo "=== $n gpuviewevict $ve $(date +%T)"
  SKIP_BUILD=$SB PRESETS=high EXTRA="-worldsurvey 1 -mapversion 4 -wsarms native_sf0 -lookwait 45 -wsposes fixed -wsfixed $P -mirrorprobe 1 -trees2 0 -gpuviewevict $ve" ./run-look-review.sh > $O/look_$n.out 2>&1
  cp "$PL" $O/player_$n.log
  grep -h "build OK\|BUILD FAIL\|BUILD ERROR\|error CS" $O/look_$n.out | head -3
  R=$(grep -h "shots:" $O/look_$n.out | tail -1 | sed "s/^shots: //"); echo "dir: $R"
  mkdir -p Screenshots/216/$n; cp "$R"/*.png "$R"/lookreview.txt Screenshots/216/$n/ 2>/dev/null
  [ -n "$R" ] || { echo "no shots, stop"; break; }
  SB=1
done
echo "ALLDONE"
