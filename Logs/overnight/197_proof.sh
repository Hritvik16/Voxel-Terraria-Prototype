#!/bin/bash
# Task 197 proof: 3 launches (1 build), poses forest / jungle_high / town_eye / 181's walls, arms _lh0 (194's rule only) then the fix.
cd "/Users/hritvikjv/Desktop/Personal Projects/Unity/Voxel Terraria 1 Byte BrickMap"
PL="$HOME/Library/Logs/DefaultCompany/Voxel Terraria 1 Byte BrickMap/Player.log"
O=Logs/overnight/197
P="${POSESET:-forest|3366.4,544.2,4973.0|3366.4,530.7,4928.0;jungle_high|5772.8,566.3,5024.2|5772.8,536.3,4979.2;$(cut -d";" -f1 Logs/TerrainGen/towns190/poses_42.txt);$(cat Logs/TerrainGen/ores181/poses.txt)}"
SB=${SB:-0}
for i in ${RUNS:-1 2 3}; do   # q-runs: end-of-frame probe (task 197 second build)
  echo "=== ${PFX:-p}$i $(date +%T)"
  SKIP_BUILD=$SB PRESETS=high EXTRA="-worldsurvey 1 -mapversion 4 -wsarms native_sf0_lh0,native_sf0 -lookwait 45 -wsposes fixed -wsfixed $P -mirrorprobe 1" ./run-look-review.sh > $O/look_${PFX:-p}$i.out 2>&1
  cp "$PL" $O/player_${PFX:-p}$i.log
  grep -h "build OK\|BUILD FAIL\|error CS" $O/look_${PFX:-p}$i.out | head -3
  R=$(grep -h "shots:" $O/look_${PFX:-p}$i.out | tail -1 | sed "s/^shots: //"); echo "dir: $R"
  mkdir -p Screenshots/197/${PFX:-p}$i; cp "$R"/*.png "$R"/lookreview.txt Screenshots/197/${PFX:-p}$i/ 2>/dev/null
  [ -n "$R" ] || { echo "no shots, stop"; break; }
  SB=1
done
echo "ALLDONE"
