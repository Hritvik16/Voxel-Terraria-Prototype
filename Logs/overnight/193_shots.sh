#!/bin/bash
# Task 193: sky fill. ONE build + ONE launch: 180's forest, 192's jungle_high and under_crown (-trees 1), the
# survey's cave_in trench and cavern (a true cave), noon; arms OFF / -skyfill 1 / -skyfill 2 at native 1080p.
cd "/Users/hritvikjv/Desktop/Personal Projects/Unity/Voxel Terraria 1 Byte BrickMap"
P="forest|3366.4,544.2,4973.0|3366.4,530.7,4928.0;jungle_high|5772.8,566.3,5024.2|5772.8,536.3,4979.2;$(cat Logs/TerrainGen/trees192/undercrown.txt)"
BASE="-worldsurvey 1 -mapversion 4 -wsarms native_sf0,native_sf1,native_sf2 -lookwait 45 -wsposes fixed,cave_in,cavern -wsfixed $P -trees 1"
PRESETS=high EXTRA="$BASE" ./run-look-review.sh
echo "=== ALL DONE"
