#!/bin/bash
# Task 193 retake: the survey's cave_in is now an enclosed tube; task 173's OPEN trench pose, same three arms.
cd "/Users/hritvikjv/Desktop/Personal Projects/Unity/Voxel Terraria 1 Byte BrickMap"
P="trench173|4343.5,840.4,3874.7|4358.6,829.1,3868.0"
SKIP_BUILD=1 PRESETS=high EXTRA="-worldsurvey 1 -mapversion 4 -wsarms native_sf0,native_sf1,native_sf2 -lookwait 45 -wsposes fixed -wsfixed $P -trees 1" ./run-look-review.sh
echo "=== ALL DONE"
