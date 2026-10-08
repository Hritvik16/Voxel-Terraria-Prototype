#!/bin/bash
# run-demo.sh -- launch the Playground to LOOK at the world/terrain overhaul.
#   ./run-demo.sh          today's world + the far band (C toggles it, I cycles views)
#   ./run-demo.sh tg2      the new sizeClass 2 island (TerrainGen), far band only
# Holds the shared Unity lock while the player is open.
# To play the NEWEST world (tg2, map v4, interactive preset), use ./run-play.sh.
set -uo pipefail
source tools/unity-lock.sh
unity_lock_acquire
APP="Builds/Playground.app"
if [ "${1:-}" = "tg2" ]; then
  open -n -W "$APP" --args -farband 1 -farshadow 1 -farsource tg2
else
  open -n -W "$APP" --args -farband 1 -farshadow 1
fi
