#!/bin/bash
# run-late-game-siege.sh -- heavy fluid + all six Phase 6 systems, sustained.
#
# The one test this line of work has been building toward: large fluid volume
# AND full gameplay load AND long enough for a slow problem to appear.
# Everything before tested one axis at a time.
#
# 300s cooldown before every run (this machine throttles ~183% back-to-back),
# and two runs so the numbers carry a driftcheck.
set -uo pipefail
UNITY="/Applications/6000.3.10f1/Unity.app/Contents/MacOS/Unity"
APP="Builds/LateGameSiege.app"
RD="$HOME/Library/Application Support/DefaultCompany/Voxel Terraria 1 Byte BrickMap/LateGameSiege"
COOL="${COOL:-300}"
RUNS="${RUNS:-2}"
SECS="${SECS:-200}"
TILECAP="${TILECAP:-1024}"
# ORPHAN=off restores the pre-fix behaviour (the A/B baseline arm).
ORPHAN="${ORPHAN:-on}"
CEILING="${CEILING:-0}"
TARGETLIVE="${TARGETLIVE:-320000}"
[ "$ORPHAN" = "off" ] && OFLAG="-noorphan 1" || OFLAG=""

if [ "${SKIP_BUILD:-0}" != "1" ]; then
  "$UNITY" -batchmode -quit -nographics -projectPath "$(pwd)" \
    -executeMethod Phase5aSceneBuilder.GenerateLateGameSiege -logFile siege_scene.log || {
      echo "SCENE GEN FAILED"; tail -30 siege_scene.log; exit 1; }
  rm -rf "$APP"
  "$UNITY" -batchmode -quit -projectPath "$(pwd)" \
    -executeMethod CommandLineBuild.BuildLateGameSiegeStandalone -logFile siege_build.log
  [ -d "$APP" ] || { echo "BUILD FAILED"; tail -40 siege_build.log; exit 1; }
fi

for i in $(seq 1 "$RUNS"); do
  FREE=$(df -g . | tail -1 | awk '{print $4}')
  [ "$FREE" -lt 5 ] && { echo "!! ${FREE}GB free, below the 5GB floor -- stopping"; exit 2; }
  echo; echo "########## SIEGE RUN $i/$RUNS  cap ${TILECAP} ceiling ${CEILING} target ${TARGETLIVE}  (cooling ${COOL}s, ${FREE}GB free) ##########"
  sleep "$COOL"
  open -n -W "$APP" --args -cleardeltas -siegeseconds "$SECS" -tilecap "$TILECAP" $OFLAG -ceiling "$CEILING" -targetlive "$TARGETLIVE"
  D=$(ls -1d "$RD"/*/ 2>/dev/null | sort | tail -1)
  [ -z "$D" ] && { echo "  NO RUN FOLDER"; continue; }
  echo "  folder $(basename "$D")"
  sed -n '/FRAME TIME PER 30s SEGMENT/,/WHOLE RUN/p' "${D}siege_report.txt" | sed 's/^/  /'
  grep -E "^  WHOLE RUN|    (PASS|FAIL|note)|^  live slots peak|^  tiles |^  placed |^  PEAK TILE|^  ORPHANED TILES|^  op-list total|^  GPU |^  SLOT CEILING" "${D}siege_report.txt" | sed 's/^/  /'
  grep -oE "^PASS [0-9]+  FAIL [0-9]+" "${D}siege_report.txt" | sed 's/^/  >>> /'
done
echo; echo "SIEGE DONE"
