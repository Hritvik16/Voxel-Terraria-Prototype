#!/bin/bash
# Task 190: per seed, eye-level town + village and the town from 150 m (poses from TownTests), -towns 1. One build, 4 launches.
cd "/Users/hritvikjv/Desktop/Personal Projects/Unity/Voxel Terraria 1 Byte BrickMap"
PL="$HOME/Library/Logs/DefaultCompany/Voxel Terraria 1 Byte BrickMap/Player.log"
mkdir -p Logs/overnight/190
SB=0
for S in 42 7 2026 31337; do
  P="$(cat Logs/TerrainGen/towns190/poses_$S.txt)"
  echo "=== seed $S poses: $P"
  SKIP_BUILD=$SB PRESETS=high EXTRA="-worldseed $S -worldsurvey 1 -mapversion 4 -wsarms native -lookwait 45 -wsposes fixed -wsfixed $P -towns 1" ./run-look-review.sh > Logs/overnight/190/look_$S.out 2>&1
  SB=1
  cp "$PL" Logs/overnight/190/player_$S.log
  grep -h 'BUILD\|shots:' Logs/overnight/190/look_$S.out | tail -3
  grep -h "WORLD SURVEY\|\[Towns\]\|\[pools\]\|POOL FULL" Logs/overnight/190/player_$S.log | cut -c1-400 | head -12; R=$(grep -h "shots:" Logs/overnight/190/look_$S.out | tail -1 | sed "s/^shots: //"); grep -h "\[pools\]" "$R/lookreview.txt" | cut -c1-400
done
echo "=== ALL DONE"
