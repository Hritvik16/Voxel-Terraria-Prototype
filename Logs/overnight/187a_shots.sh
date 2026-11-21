#!/bin/bash
# Task 187a: stamp fit ON by DEFAULT (no -stampfit flag) vs -stampfit 0, task 185's 3 poses.
cd "/Users/hritvikjv/Desktop/Personal Projects/Unity/Voxel Terraria 1 Byte BrickMap"
PL="$HOME/Library/Logs/DefaultCompany/Voxel Terraria 1 Byte BrickMap/Player.log"
P="cave42_over|5870.9,577.9,3063.5|5923.7,543,3079.5;wetland_basin|3060,589,4532|2900,528,4532;river|4317,574,5094|4204,549,4996"
BASE="-worldsurvey 1 -mapversion 4 -wsarms native -lookwait 45 -wsposes fixed -wsfixed $P"
PRESETS=high EXTRA="$BASE" ./run-look-review.sh
cp "$PL" Logs/overnight/187a/player_default.log
echo "=== DEFAULT DONE"
SKIP_BUILD=1 PRESETS=high EXTRA="$BASE -stampfit 0" ./run-look-review.sh
cp "$PL" Logs/overnight/187a/player_off.log
echo "=== OFF DONE"
grep -h 'stamp fit' Logs/overnight/187a/player_default.log Logs/overnight/187a/player_off.log
echo "=== ALL DONE"
