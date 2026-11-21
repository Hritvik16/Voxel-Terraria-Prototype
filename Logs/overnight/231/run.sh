#!/bin/bash
# task 231 step 3: ONE short launch, stills only (no timing used): the 226 look (FULL, all four pieces) at a LOW SUN (12 deg,
# afternoon, so the sun comes from the side of these south-facing poses) at forest_eye, jungle_eye, redwood_far1 (trees210 poses),
# framed like a screenshot rather than a test target. Existing flags only (-costsun, -costfixed, -costarms none); no code change.
cd "/Users/hritvikjv/Desktop/Personal Projects/Unity/Voxel Terraria 1 Byte BrickMap"
D=Logs/overnight/231
P="$(tr ';' '\n' < Logs/TerrainGen/trees210/poses.txt | grep -E '^(forest_eye|jungle_eye|redwood_far1)\|' | paste -sd';' -)"
source tools/play-flags.sh
BASE="$PLAY_SETTINGS_FLAGS -quality high -renderrows 432 -shellwait 60 -costtimed 1"
echo "== lowsun start $(date +%T)"
SKIP_BUILD=1 EXTRA="$BASE -costsun 12 -costfixed $P -costarms none" ./run-cost-split.sh > $D/launch_lowsun.out 2>&1
echo "== lowsun end $(date +%T)"; tail -2 $D/launch_lowsun.out
R=$(grep -o 'data: .*' $D/launch_lowsun.out | sed 's/^data: //')
[ -n "$R" ] && { mkdir -p Screenshots/231/raw; cp "$R"/stills/*.png Screenshots/231/raw/ 2>/dev/null; cp "$R/costsplit.txt" $D/costsplit_lowsun.txt; }
cp "$HOME/Library/Logs/DefaultCompany/Voxel Terraria 1 Byte BrickMap/Player.log" $D/player_lowsun.log
echo RUN_DONE
