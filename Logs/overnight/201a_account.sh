#!/bin/bash
# Task 201a: GPU body pool accounting (-gpupoolprobe 1) at 195a/197's poses + 200's redwood (gallery), seed 42, noon.
# Arms: TAG=on (everything ON, default) / TAG=off (-trees 0 -towns 0). One launch per arm.
cd "/Users/hritvikjv/Desktop/Personal Projects/Unity/Voxel Terraria 1 Byte BrickMap"
PL="$HOME/Library/Logs/DefaultCompany/Voxel Terraria 1 Byte BrickMap/Player.log"
O=Logs/overnight/201a
cp Logs/TerrainGen/trees200/gallery_42.txt /tmp/treegallery_42.txt
RW="$(tr ';' '\n' < Logs/TerrainGen/trees200/poses_42.txt | grep '^s_redwood_spacecol|')"
P="forest|3366.4,544.2,4973.0|3366.4,530.7,4928.0;jungle_high|5772.8,566.3,5024.2|5772.8,536.3,4979.2;town_eye|2724.0,635.4,3319.2|2724.0,636.7,3268.0;badlands_wall|3098.9,527.5,5797.7|3128.9,525.5,5797.7;$RW"
TAG=${TAG:-on}
[ "${P3:-0}" = 1 ] && P="$(echo "$P" | tr ';' '\n' | grep -E '^(forest|town_eye|s_redwood_spacecol)\|' | paste -sd';' -)"
echo "=== $TAG  $(date +%T)"
SKIP_BUILD=${SB:-0} PRESETS=high EXTRA="-worldseed 42 -worldsurvey 1 -mapversion 4 -wsarms native -lookwait 40 -wsposes fixed -wsfixed $P -tod 12 -treegallery /tmp/treegallery_42.txt -gpupoolprobe 1 ${XF:-}" ./run-look-review.sh > $O/look_$TAG.out 2>&1
cp "$PL" $O/player_$TAG.log
grep -h 'BUILD\|build OK\|shots:\|error CS' $O/look_$TAG.out | tail -4
R=$(grep -h "shots:" $O/look_$TAG.out | tail -1 | sed "s/^shots: //"); echo "dir: $R"
for f in "$R"/ws_fixed_*.png; do n=$(basename "$f" .png); n=${n#ws_fixed_}; n=${n%_native}; cp "$f" "Screenshots/201a/${n}_$TAG.png"; done
cp "$R/lookreview.txt" "Screenshots/201a/lookreview_$TAG.txt"
grep -h "WSPOSE\|\[mem\]\|\[gpupool\]\|\[gpuseal\]\|\[shells\]" "$R/lookreview.txt" | cut -c1-420
echo "=== ALL DONE $(date +%T)"
