#!/bin/bash
# Task 192 retake: 180's jungle pose put the eye inside the (taller) new jungle crowns -> black frames.
# Same target, eye 20 m higher (over the canopy), all four arms, one pose each.
cd "/Users/hritvikjv/Desktop/Personal Projects/Unity/Voxel Terraria 1 Byte BrickMap"
LR="$HOME/Library/Application Support/DefaultCompany/Voxel Terraria 1 Byte BrickMap/LookReview"
P="jungle_high|5772.8,566.3,5024.2|5772.8,536.3,4979.2"
BASE="-worldsurvey 1 -mapversion 4 -wsarms native -lookwait 45 -wsposes fixed -wsfixed $P -trees 1"
for arm in 180 30 45 60; do
  if [ $arm = 180 ]; then X="-treestyle 0"; else X="-leaffill $arm"; fi
  SKIP_BUILD=1 PRESETS=high EXTRA="$BASE $X" ./run-look-review.sh
  D="$LR/$(ls "$LR" | grep '_high$' | tail -1)"
  cp "$D/ws_fixed_jungle_high_native.png" "Screenshots/192/jungle_high_$arm.png"
  cp "$D/lookreview.txt" "Screenshots/192/lookreview_jungle_$arm.txt"
  echo "=== ARM $arm DONE ($D)"
done
echo "=== ALL DONE"
