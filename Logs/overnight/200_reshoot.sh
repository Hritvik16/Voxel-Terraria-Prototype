#!/bin/bash
# Task 200 reshoot: the 5 blocks re-sited by Gallery_Layout run 2 (town pad / snow / town in frame), 8 poses, no rebuild.
cd "/Users/hritvikjv/Desktop/Personal Projects/Unity/Voxel Terraria 1 Byte BrickMap"
PL="$HOME/Library/Logs/DefaultCompany/Voxel Terraria 1 Byte BrickMap/Player.log"
O=Logs/overnight/200
cp Logs/TerrainGen/trees200/gallery_42.txt /tmp/treegallery_42.txt
P="$(tr ';' '\n' < Logs/TerrainGen/trees200/poses_42.txt | grep -E '^(m_oak_spacecol|s_oak_spacecol|c_oak_spacecol|m_banyan_spacecol|s_banyan_spacecol|m_banyan_lsystem|s_redwood_spacecol|s_acacia_spacecol)\|' | paste -sd';' -)"
echo "=== reshoot $(date +%T): $P"
SKIP_BUILD=1 PRESETS=high EXTRA="-worldseed 42 -worldsurvey 1 -mapversion 4 -wsarms native -lookwait 30 -wsposes fixed -wsfixed $P -tod 12 -treegallery /tmp/treegallery_42.txt" ./run-look-review.sh > $O/look2.out 2>&1
cp "$PL" $O/player2.log
R=$(grep -h "shots:" $O/look2.out | tail -1 | sed "s/^shots: //"); echo "dir: $R"
for f in "$R"/ws_fixed_*_native.png; do n=$(basename "$f" _native.png); n=${n#ws_fixed_}; cp "$f" "Screenshots/200/engine/${n}.png"; done
cp "$R/lookreview.txt" Screenshots/200/engine/lookreview2.txt
grep -h "\[TreeGallery\] /tmp" $O/player2.log | cut -c1-260
grep -h "WSPOSE\|\[shells\]\|\[mem\]" "$R/lookreview.txt" | cut -c1-260
echo "=== ALL DONE $(date +%T)"
