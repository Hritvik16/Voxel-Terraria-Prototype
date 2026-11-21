#!/bin/bash
# Task 250 (WORLD_FILL_BUDGET sec. 5 E3): voxel grass fringe, COUNTS ONLY (rule 10). 248's runner, fringe off / on.
# ONE dev-kernel build (run-cost-split.sh, VE_ALL_KERNELS=1), then 8 cost-split launches = 2 fringe arms x 2 suns x
# (the same pose list): forest_eye (the dense forest, 248's pose), savanna_eye (the open meadow: task 210's savanna pose,
# Logs/TerrainGen/trees210/poses.txt) and savanna_close (savanna_eye's eye looking at the ground ~5 m ahead: the stills
# that show the stubs). walk_near always runs first in a -costfixed launch. -raystats 1 (32-frame GPU windows), the
# budget settings of 244-248: -quality high -renderrows 432 -shellwait 60. Trees: the default (dense).
cd "/Users/hritvikjv/Desktop/Personal Projects/Unity/Voxel Terraria 1 Byte BrickMap"
source tools/play-flags.sh
O=Logs/overnight/250
mkdir -p $O
COMMON="$PLAY_SETTINGS_FLAGS -quality high -renderrows 432 -shellwait 60"
FIX="forest_eye|3366.4,532.5,4950.0|3366.4,529.7,4928.0;savanna_eye|2060.8,518.8,4950.0|2060.8,520.5,4928.0;savanna_close|2060.8,518.8,4950.0|2060.8,517.0,4945.0"
RS="-costarms none -costtimedpaths none -raystats 1 -rsnoident 1"
if [ "${SKIP_BUILD:-0}" != "1" ]; then
  echo "build start $(date +%T)"
  BUILD_ONLY=1 ./run-cost-split.sh > $O/build.out 2>&1
  tail -2 $O/build.out
  tail -1 $O/build.out | grep -q DONE || { echo "BUILD FAILED"; exit 1; }
fi
for sun in "16:-startsun 16" "noon:"; do
 for fr in "off:-fringe 0" "on:-fringe 1"; do
  stag=${sun%%:*}; sf=${sun#*:}; ftag=${fr%%:*}; ff=${fr#*:}; tag=${ftag}_$stag
  [ -f $O/costsplit_$tag.txt ] && { echo "skip $tag (done)"; continue; }
  echo "launch $tag start $(date +%T)"
  SKIP_BUILD=1 EXTRA="$COMMON $sf $ff -costfixed $FIX $RS" ./run-cost-split.sh > $O/launch_$tag.out 2>&1 < /dev/null
  tail -2 $O/launch_$tag.out
  rep=$(grep -o 'report: [^ ]*' $O/launch_$tag.out | cut -d' ' -f2); cp "$rep" $O/costsplit_$tag.txt 2>/dev/null
  d=$(grep -o 'data: .*' $O/launch_$tag.out | cut -d' ' -f2-); echo "data_$tag: $d" >> $O/dirs.txt
  cp "$HOME/Library/Logs/DefaultCompany/Voxel Terraria 1 Byte BrickMap/Player.log" $O/player_$tag.log
  for p in forest_eye savanna_eye savanna_close; do cp "$d/stills/${p}_full_clean.png" "Screenshots/250/${p}_${tag}.png" 2>/dev/null; done
 done
done
echo "=== ALL DONE $(date +%T)"
