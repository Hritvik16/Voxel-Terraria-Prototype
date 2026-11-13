#!/bin/bash
# Task 188a: material rules with the random variant spray (default, -matvariant 1) vs without (-matvariant 0). One build, same poses.
cd "/Users/hritvikjv/Desktop/Personal Projects/Unity/Voxel Terraria 1 Byte BrickMap"
PL="$HOME/Library/Logs/DefaultCompany/Voxel Terraria 1 Byte BrickMap/Player.log"
P="site_frontier|4299.2,525.7,5748.0|4388.0,524.1,5748.0;site_town|2669.6,635.5,3268.0|2788.0,635.1,3268.0;desert_flat|3750.4,559.4,2515.4|3750.4,545.7,2470.4;savanna_flat|2060.8,532.9,4973.0|2060.8,521.5,4928.0;forest_bank_a|4773.8,566.2,4035.7|4773.8,545.2,3990.7;forest_bank_b|5196.8,532.3,3553.0|5196.8,519.3,3508.0"
BASE="-worldsurvey 1 -mapversion 4 -wsarms native -lookwait 45 -wsposes fixed -wsfixed $P"
PRESETS=high EXTRA="$BASE" ./run-look-review.sh
cp "$PL" Logs/overnight/188a/player_on.log
echo "=== ON DONE"
SKIP_BUILD=1 PRESETS=high EXTRA="$BASE -matvariant 0" ./run-look-review.sh
cp "$PL" Logs/overnight/188a/player_off.log
echo "=== ALL DONE"
