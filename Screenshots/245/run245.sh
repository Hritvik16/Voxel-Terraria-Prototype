#!/bin/bash
# Task 245: foliage light early-out. ONE dev-kernel build (run-cost-split.sh, VE_ALL_KERNELS=1), then two cost-split launches
# with the budget's settings (as task 244), -foliageout 1 (side buffer built from launch), nothing timed (counts only, rule 10).
# Per path: FULL (early-out ON, eps 1/8 default), still arms foliage_off (shader OFF) and foliage_e2 (eps 1/2); each: stills + raystats.
# Paths: walk_near, forest_eye, oak_under (task 243's pose). 16 deg and noon.
cd "/Users/hritvikjv/Desktop/Personal Projects/Unity/Voxel Terraria 1 Byte BrickMap"
source tools/play-flags.sh
O=Logs/overnight/245
COMMON="$PLAY_SETTINGS_FLAGS -quality high -renderrows 432 -shellwait 60"
FIX="forest_eye|3366.4,532.5,4950.0|3366.4,529.7,4928.0;oak_under|3327.8,518.5,4897.4|3324.8,527.0,4894.4"
RS="-foliageout 1 -costarms none -costtimedpaths none -coststillarms foliage_off,foliage_e2 -raystats 1 -rsnoident 1"
if [ "${SKIP_BUILD:-0}" != "1" ]; then
  echo "build start $(date +%T)"
  BUILD_ONLY=1 ./run-cost-split.sh > $O/build.out 2>&1
  tail -2 $O/build.out
  tail -1 $O/build.out | grep -q DONE || { echo "BUILD FAILED"; exit 1; }
fi
for arm in "16:-startsun 16" "noon:-startsun 0"; do
  tag=${arm%%:*}; f=${arm#*:}
  echo "launch $tag start $(date +%T)"
  SKIP_BUILD=1 EXTRA="$COMMON $f -costfixed $FIX $RS" ./run-cost-split.sh > $O/launch_$tag.out 2>&1 < /dev/null
  tail -2 $O/launch_$tag.out
  rep=$(grep -o 'report: [^ ]*' $O/launch_$tag.out | cut -d' ' -f2); cp "$rep" $O/costsplit_$tag.txt 2>/dev/null
  d=$(grep -o 'data: .*' $O/launch_$tag.out | cut -d' ' -f2-); echo "data_$tag: $d" >> $O/dirs.txt
  cp "$HOME/Library/Logs/DefaultCompany/Voxel Terraria 1 Byte BrickMap/Player.log" $O/player_$tag.log
done
echo "=== ALL DONE $(date +%T)"
