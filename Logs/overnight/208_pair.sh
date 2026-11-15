#!/bin/bash
# Task 208 proof pair: 1 build, launch b1 = valve spares in-view chunks (default), b2 = -valveview 0 (the old pure-LRU order).
# Poses forest / jungle_high / town_eye (197's) + a 3-step walk from jungle_high (40 m steps along its view, -Z), -mirrorprobe 1.
cd "/Users/hritvikjv/Desktop/Personal Projects/Unity/Voxel Terraria 1 Byte BrickMap"
PL="$HOME/Library/Logs/DefaultCompany/Voxel Terraria 1 Byte BrickMap/Player.log"
O=Logs/overnight/208
W="walk1|5772.8,566.3,4984.2|5772.8,536.3,4939.2;walk2|5772.8,566.3,4944.2|5772.8,536.3,4899.2;walk3|5772.8,566.3,4904.2|5772.8,536.3,4859.2"
P="forest|3366.4,544.2,4973.0|3366.4,530.7,4928.0;jungle_high|5772.8,566.3,5024.2|5772.8,536.3,4979.2;$W;$(cut -d";" -f1 Logs/TerrainGen/towns190/poses_42.txt)"
SB=0
for r in "b1|1" "b2|0"; do
  n=${r%|*}; vv=${r#*|}
  echo "=== $n valveview $vv $(date +%T)"
  SKIP_BUILD=$SB PRESETS=high EXTRA="-worldsurvey 1 -mapversion 4 -wsarms native_sf0 -lookwait 45 -wsposes fixed -wsfixed $P -mirrorprobe 1 -valveview $vv" ./run-look-review.sh > $O/look_$n.out 2>&1
  cp "$PL" $O/player_$n.log
  grep -h "build OK\|BUILD FAIL\|BUILD ERROR\|error CS" $O/look_$n.out | head -3
  R=$(grep -h "shots:" $O/look_$n.out | tail -1 | sed "s/^shots: //"); echo "dir: $R"
  mkdir -p Screenshots/208/$n; cp "$R"/*.png "$R"/lookreview.txt Screenshots/208/$n/ 2>/dev/null
  [ -n "$R" ] || { echo "no shots, stop"; break; }
  SB=1
done
echo "ALLDONE"
