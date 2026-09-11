#!/bin/bash
# run-phase6-qa.sh -- per-system performance + visual QA, one mode per launch.
#
# Fluid has had five sessions of scrutiny; the other five Phase 6 systems have
# had none of comparable depth. This profiles each under its own natural heavy
# use, then a few PAIRS, which nothing has ever isolated.
#
# 300s cooldown before every mode: this machine throttles ~183% back-to-back.
# Wall clock only; no gpuFrameTime against a budget (Amdt 8.10); no Xcode.
set -uo pipefail
UNITY="/Applications/6000.3.10f1/Unity.app/Contents/MacOS/Unity"
APP="Builds/Phase6Qa.app"
RD="$HOME/Library/Application Support/DefaultCompany/Voxel Terraria 1 Byte BrickMap/Phase6Qa"
COOL="${COOL:-300}"
MODES="${MODES:-player ccd edits projectile destruction buoyancy organic dig+fluid boom+swim ccd+edits boundary}"

if [ "${SKIP_BUILD:-0}" != "1" ]; then
  "$UNITY" -batchmode -quit -nographics -projectPath "$(pwd)" \
    -executeMethod Phase5aSceneBuilder.GeneratePhase6Qa -logFile phase6_qa_scene.log || {
      echo "SCENE GEN FAILED"; tail -30 phase6_qa_scene.log; exit 1; }
  rm -rf "$APP"
  "$UNITY" -batchmode -quit -projectPath "$(pwd)" \
    -executeMethod CommandLineBuild.BuildPhase6QaStandalone -logFile phase6_qa_build.log
  [ -d "$APP" ] || { echo "BUILD FAILED"; tail -40 phase6_qa_build.log; exit 1; }
fi

for M in $MODES; do
  FREE=$(df -g . | tail -1 | awk '{print $4}')
  if [ "$FREE" -lt 5 ]; then echo "!! ${FREE}GB free, below the 5GB floor -- stopping"; exit 2; fi
  echo; echo "########## $M   (cooling ${COOL}s, ${FREE}GB free) ##########"
  sleep "$COOL"
  open -n -W "$APP" --args -cleardeltas -qamode "$M"
  D=$(ls -1d "$RD"/*/ 2>/dev/null | sort | tail -1)
  [ -z "$D" ] && { echo "  NO RUN FOLDER"; continue; }
  grep -E "^  frames |^PASS [0-9]+  FAIL|    (PASS|FAIL|note)|inside radius|outside radius" "${D}qa_report.txt" | sed 's/^/  /'
  grep -E "band mean frame|SHARE OF BAND" "${D}qa_report.txt" | head -2 | sed 's/^/  /'
done
echo; echo "QA SWEEP DONE"
