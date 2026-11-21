#!/bin/bash
# Task 200: the tree gallery (EXAMPLES ONLY) -- one build, ONE launch, 16 fixed poses on seed 42, noon, native 1080.
# Layout + poses: TreeGalleryTests.Gallery_Layout (Logs/TerrainGen/trees200/gallery_42.txt, poses_42.txt).
cd "/Users/hritvikjv/Desktop/Personal Projects/Unity/Voxel Terraria 1 Byte BrickMap"
PL="$HOME/Library/Logs/DefaultCompany/Voxel Terraria 1 Byte BrickMap/Player.log"
O=Logs/overnight/200
cp Logs/TerrainGen/trees200/gallery_42.txt /tmp/treegallery_42.txt   # the repo path has spaces; EXTRA is word-split
P="$(cat Logs/TerrainGen/trees200/poses_42.txt)"
SB=${SB:-0}
echo "=== gallery $(date +%T)"
SKIP_BUILD=$SB PRESETS=high EXTRA="-worldseed 42 -worldsurvey 1 -mapversion 4 -wsarms native -lookwait 30 -wsposes fixed -wsfixed $P -tod 12 -treegallery /tmp/treegallery_42.txt" ./run-look-review.sh > $O/look.out 2>&1
cp "$PL" $O/player.log
grep -h 'BUILD\|build OK\|shots:' $O/look.out | tail -3
R=$(grep -h "shots:" $O/look.out | tail -1 | sed "s/^shots: //"); echo "dir: $R"
for f in "$R"/ws_fixed_*_native.png; do n=$(basename "$f" _native.png); n=${n#ws_fixed_}; cp "$f" "Screenshots/200/engine/${n}.png"; done
cp "$R/lookreview.txt" Screenshots/200/engine/lookreview.txt
grep -h "\[TreeGallery\]" $O/player.log | cut -c1-260
grep -h "WSPOSE\|\[shells\]\|\[pools\]\|\[mem\]\|\[refused\]\|missing" "$R/lookreview.txt" | cut -c1-300
echo "=== ALL DONE $(date +%T)"
