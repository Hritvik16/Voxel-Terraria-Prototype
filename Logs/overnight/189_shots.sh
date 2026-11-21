#!/bin/bash
# Task 189: landform signatures OFF (default) vs ON (-landsig 1). One build, same poses (task 173's survey).
cd "/Users/hritvikjv/Desktop/Personal Projects/Unity/Voxel Terraria 1 Byte BrickMap"
PL="$HOME/Library/Logs/DefaultCompany/Voxel Terraria 1 Byte BrickMap/Player.log"
P="pair_jungle_dune-sea|4148.0,588.7,5188.0|4148.0,556.7,5028.0;pair_desert_canyonlands|4708.0,727.1,2964.0|4708.0,695.1,2804.0;range_200up|3860.0,743.9,5168.0|3860.0,1095.0,3668.0"
BASE="-worldsurvey 1 -mapversion 4 -wsarms native -lookwait 45 -wsposes fixed -wsfixed $P"
PRESETS=high EXTRA="$BASE" ./run-look-review.sh
cp "$PL" Logs/overnight/189/player_off.log
echo "=== OFF DONE"
SKIP_BUILD=1 PRESETS=high EXTRA="$BASE -landsig 1" ./run-look-review.sh
cp "$PL" Logs/overnight/189/player_on.log
echo "=== ALL DONE"
