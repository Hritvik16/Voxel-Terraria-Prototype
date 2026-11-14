#!/bin/bash
# Task 197: reproduce 194b's jungle_high pits (+ 195's badlands_wall miss) on the default world with the [mirror] probe.
# Usage: TAG=r1 SKIP_BUILD=0|1 [XF="..."] Logs/overnight/197_repro.sh
cd "/Users/hritvikjv/Desktop/Personal Projects/Unity/Voxel Terraria 1 Byte BrickMap"
PL="$HOME/Library/Logs/DefaultCompany/Voxel Terraria 1 Byte BrickMap/Player.log"
O=Logs/overnight/197
P="forest|3366.4,544.2,4973.0|3366.4,530.7,4928.0;jungle_high|5772.8,566.3,5024.2|5772.8,536.3,4979.2;$(cat Logs/TerrainGen/trees192/undercrown.txt);$(cat Logs/TerrainGen/ores181/poses.txt)"
TAG=${TAG:-r1}
SKIP_BUILD=${SKIP_BUILD:-0} PRESETS=high EXTRA="-worldsurvey 1 -mapversion 4 -wsarms ${ARMS:-native_sf0} -lookwait 45 -wsposes fixed -wsfixed $P -mirrorprobe 1 ${XF:-}" ./run-look-review.sh > $O/look_$TAG.out 2>&1
cp "$PL" $O/player_$TAG.log
grep -h "build OK\|BUILD FAIL\|error CS" $O/look_$TAG.out | head -5
R=$(grep -h "shots:" $O/look_$TAG.out | tail -1 | sed "s/^shots: //"); echo "dir: $R"
echo "DONE $TAG"
