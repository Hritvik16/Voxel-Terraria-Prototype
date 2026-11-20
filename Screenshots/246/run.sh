#!/bin/bash
# Task 246: world-fill cost plan. COUNTS ONLY (rule 10; owner on the Mac all day, no frame times quoted).
# ONE dev-kernel build (run-cost-split.sh, VE_ALL_KERNELS=1), then 8 cost-split launches = 4 tree arms x 2 suns,
# task 244's -raystats 1 at forest_eye and walk_open (pinned at task 244's noon walk_open middle pose so every arm sees the
# same view; walk_near always runs first in a -costfixed launch). Budget settings as 244/245: -quality high -renderrows 432.
# Tree arms (world-gen flags, so one launch each): off = -trees 0; today = -trees2 0 (pre-210 trees);
# sparse = -trees2dense 0 (task 210 placement); dense = default (task 213/214, -trees2 1).
cd "/Users/hritvikjv/Desktop/Personal Projects/Unity/Voxel Terraria 1 Byte BrickMap"
source tools/play-flags.sh
O=Logs/overnight/246
COMMON="$PLAY_SETTINGS_FLAGS -quality high -renderrows 432 -shellwait 60"
FIX="forest_eye|3366.4,532.5,4950.0|3366.4,529.7,4928.0;walk_open|4035.5,973.4,3850.9|4057.3,973.4,3853.8"
RS="-costarms none -costtimedpaths none -raystats 1 -rsnoident 1"
if [ "${SKIP_BUILD:-0}" != "1" ]; then
  echo "build start $(date +%T)"
  BUILD_ONLY=1 ./run-cost-split.sh > $O/build.out 2>&1
  tail -2 $O/build.out
  tail -1 $O/build.out | grep -q DONE || { echo "BUILD FAILED"; exit 1; }
fi
for sun in "16:-startsun 16" "noon:-startsun 0"; do
 for tr in "off:-trees 0" "today:-trees2 0" "sparse:-trees2 1 -trees2dense 0" "dense:-trees2 1"; do
  stag=${sun%%:*}; sf=${sun#*:}; ttag=${tr%%:*}; tf=${tr#*:}; tag=${ttag}_$stag
  [ -f $O/costsplit_$tag.txt ] && { echo "skip $tag (done)"; continue; }
  echo "launch $tag start $(date +%T)"
  SKIP_BUILD=1 EXTRA="$COMMON $sf $tf -costfixed $FIX $RS" ./run-cost-split.sh > $O/launch_$tag.out 2>&1 < /dev/null
  tail -2 $O/launch_$tag.out
  rep=$(grep -o 'report: [^ ]*' $O/launch_$tag.out | cut -d' ' -f2); cp "$rep" $O/costsplit_$tag.txt 2>/dev/null
  d=$(grep -o 'data: .*' $O/launch_$tag.out | cut -d' ' -f2-); echo "data_$tag: $d" >> $O/dirs.txt
  cp "$HOME/Library/Logs/DefaultCompany/Voxel Terraria 1 Byte BrickMap/Player.log" $O/player_$tag.log
 done
done
echo "=== ALL DONE $(date +%T)"
