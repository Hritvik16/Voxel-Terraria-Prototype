#!/bin/bash
# Task 190a: the no-spray DEFAULT vs -matvariant 1 (the old spray). One build, same 3 poses of 188a, noon.
cd "/Users/hritvikjv/Desktop/Personal Projects/Unity/Voxel Terraria 1 Byte BrickMap"
PL="$HOME/Library/Logs/DefaultCompany/Voxel Terraria 1 Byte BrickMap/Player.log"
mkdir -p Logs/overnight/190a
P="desert_flat|3750.4,559.4,2515.4|3750.4,545.7,2470.4;savanna_flat|2060.8,532.9,4973.0|2060.8,521.5,4928.0;forest_bank_a|4773.8,566.2,4035.7|4773.8,545.2,3990.7"
BASE="-worldsurvey 1 -mapversion 4 -wsarms native -lookwait 45 -wsposes fixed -wsfixed $P"
PRESETS=high EXTRA="$BASE" ./run-look-review.sh
cp "$PL" Logs/overnight/190a/player_default.log
echo "=== DEFAULT DONE"
SKIP_BUILD=1 PRESETS=high EXTRA="$BASE -matvariant 1" ./run-look-review.sh
cp "$PL" Logs/overnight/190a/player_variant1.log
echo "=== ALL DONE"
