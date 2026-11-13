#!/bin/bash
# Task 189: an extra dune pose inside a solid dune field (seed 42 cell (467,589), plane weight >= 0.6 around), OFF vs ON. No rebuild.
cd "/Users/hritvikjv/Desktop/Personal Projects/Unity/Voxel Terraria 1 Byte BrickMap"
PL="$HOME/Library/Logs/DefaultCompany/Voxel Terraria 1 Byte BrickMap/Player.log"
P="dune_field|3740.0,575.0,4876.0|3740.0,546.6,4716.0;dune_field_low|3740.0,562.0,4796.0|3740.0,546.6,4716.0"
BASE="-worldsurvey 1 -mapversion 4 -wsarms native -lookwait 45 -wsposes fixed -wsfixed $P"
SKIP_BUILD=1 PRESETS=high EXTRA="$BASE" ./run-look-review.sh
cp "$PL" Logs/overnight/189/player_off2.log
echo "=== OFF DONE"
SKIP_BUILD=1 PRESETS=high EXTRA="$BASE -landsig 1" ./run-look-review.sh
cp "$PL" Logs/overnight/189/player_on2.log
echo "=== ALL DONE"
