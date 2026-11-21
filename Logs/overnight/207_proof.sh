#!/bin/bash
# Task 207 proof: 1 build + 1 launch, poses forest / jungle_high / town_eye (197's), arms: native_sf0 (retry only what fits,
# after the pose settle) then native_sf0_rf0 (the pre-207 retry for that arm's 60 frames + probe).
cd "/Users/hritvikjv/Desktop/Personal Projects/Unity/Voxel Terraria 1 Byte BrickMap"
PL="$HOME/Library/Logs/DefaultCompany/Voxel Terraria 1 Byte BrickMap/Player.log"
O=Logs/overnight/207
P="forest|3366.4,544.2,4973.0|3366.4,530.7,4928.0;jungle_high|5772.8,566.3,5024.2|5772.8,536.3,4979.2;$(cut -d";" -f1 Logs/TerrainGen/towns190/poses_42.txt)"
echo "=== a1 $(date +%T)"
SKIP_BUILD=${SB:-0} PRESETS=high EXTRA="-worldsurvey 1 -mapversion 4 -wsarms native_sf0,native_sf0_rf0 -lookwait 45 -wsposes fixed -wsfixed $P -mirrorprobe 1" ./run-look-review.sh > $O/look_a1.out 2>&1
cp "$PL" $O/player_a1.log
grep -h "build OK\|BUILD FAIL\|error CS" $O/look_a1.out | head -3
R=$(grep -h "shots:" $O/look_a1.out | tail -1 | sed "s/^shots: //"); echo "dir: $R"
mkdir -p Screenshots/207/a1; cp "$R"/*.png "$R"/lookreview.txt Screenshots/207/a1/ 2>/dev/null
echo "ALLDONE"
