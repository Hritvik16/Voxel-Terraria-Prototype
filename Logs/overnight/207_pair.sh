#!/bin/bash
# Task 207 proof pair: 1 build, launch b1 = retry only what fits (default), b2 = -retryfit 0 (the pre-207 retry, launch-wide).
# Poses forest / jungle_high / town_eye (197's), one arm native_sf0, -mirrorprobe 1.
cd "/Users/hritvikjv/Desktop/Personal Projects/Unity/Voxel Terraria 1 Byte BrickMap"
PL="$HOME/Library/Logs/DefaultCompany/Voxel Terraria 1 Byte BrickMap/Player.log"
O=Logs/overnight/207
P="forest|3366.4,544.2,4973.0|3366.4,530.7,4928.0;jungle_high|5772.8,566.3,5024.2|5772.8,536.3,4979.2;$(cut -d";" -f1 Logs/TerrainGen/towns190/poses_42.txt)"
SB=0
for r in "b1|1" "b2|0"; do
  n=${r%|*}; rf=${r#*|}
  echo "=== $n retryfit $rf $(date +%T)"
  SKIP_BUILD=$SB PRESETS=high EXTRA="-worldsurvey 1 -mapversion 4 -wsarms native_sf0 -lookwait 45 -wsposes fixed -wsfixed $P -mirrorprobe 1 -retryfit $rf" ./run-look-review.sh > $O/look_$n.out 2>&1
  cp "$PL" $O/player_$n.log
  grep -h "build OK\|BUILD FAIL\|error CS" $O/look_$n.out | head -3
  R=$(grep -h "shots:" $O/look_$n.out | tail -1 | sed "s/^shots: //"); echo "dir: $R"
  mkdir -p Screenshots/207/$n; cp "$R"/*.png "$R"/lookreview.txt Screenshots/207/$n/ 2>/dev/null
  [ -n "$R" ] || { echo "no shots, stop"; break; }
  SB=1
done
echo "ALLDONE"
