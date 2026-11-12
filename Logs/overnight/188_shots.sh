#!/bin/bash
# Task 188 shots: trees ON (-trees 1), shell pack ON (the new default) vs -shellpack 0 (before 188), one build.
cd "/Users/hritvikjv/Desktop/Personal Projects/Unity/Voxel Terraria 1 Byte BrickMap"
P="$(cat Logs/overnight/188_poses.txt)"
PRESETS=high EXTRA="-worldsurvey 1 -mapversion 4 -wsarms native -lookwait 45 -wsposes fixed -wsfixed $P -trees 1 -shellpack 0" ./run-look-review.sh
echo "=== BEFORE DONE"
SKIP_BUILD=1 PRESETS=high EXTRA="-worldsurvey 1 -mapversion 4 -wsarms native -lookwait 45 -wsposes fixed -wsfixed $P -trees 1" ./run-look-review.sh
echo "=== ALL DONE"
