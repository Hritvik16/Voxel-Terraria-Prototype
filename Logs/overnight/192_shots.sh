#!/bin/bash
# Task 192: trees' look. Same build, same poses (180's five + one under a forest crown looking up), four arms:
# task 180's trees (-treestyle 0), then 192's open crowns at -leaffill 30 / 45 / 60. All -trees 1.
cd "/Users/hritvikjv/Desktop/Personal Projects/Unity/Voxel Terraria 1 Byte BrickMap"
LR="$HOME/Library/Application Support/DefaultCompany/Voxel Terraria 1 Byte BrickMap/LookReview"
P="$(cat Logs/overnight/180_poses.txt);$(cat Logs/TerrainGen/trees192/undercrown.txt)"
BASE="-worldsurvey 1 -mapversion 4 -wsarms native -lookwait 45 -wsposes fixed -wsfixed $P -trees 1"
mkdir -p Screenshots/192
first=1
for arm in 180 30 45 60; do
  if [ $arm = 180 ]; then X="-treestyle 0"; else X="-leaffill $arm"; fi
  if [ $first = 1 ]; then SB=""; first=0; else SB=1; fi
  SKIP_BUILD=$SB PRESETS=high EXTRA="$BASE $X" ./run-look-review.sh
  D="$LR/$(ls "$LR" | tail -1)"
  for f in "$D"/ws_fixed_*_native.png; do n=$(basename "$f" _native.png); n=${n#ws_fixed_}; cp "$f" "Screenshots/192/${n}_$arm.png"; done
  cp "$D/lookreview.txt" "Screenshots/192/lookreview_$arm.txt"
  echo "=== ARM $arm DONE ($D)"
done
echo "=== ALL DONE"
