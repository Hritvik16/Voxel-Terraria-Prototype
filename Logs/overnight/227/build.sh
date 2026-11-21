#!/bin/bash
# task 227 (copy of 226's): the Playground player WITHOUT VE_ALL_KERNELS (the dev *Diag kernels stripped; the shade probe
# views 15-18 live in CSMainFarShade, which a debug view falls back to). run-cost-split.sh's build steps otherwise.
cd "/Users/hritvikjv/Desktop/Personal Projects/Unity/Voxel Terraria 1 Byte BrickMap"
UNITY_BIN="/Applications/6000.3.10f1/Unity.app/Contents/MacOS/Unity"; APP="Builds/Playground.app"
source tools/unity-lock.sh
unset VE_ALL_KERNELS
export UNITY_SHADER_COMPILER_TASK_TIMEOUT_MINUTES=60
unity_lock_acquire
rm -rf "$APP"
"$UNITY_BIN" -batchmode -quit -nographics -projectPath "$(pwd)" -executeMethod Phase5aSceneBuilder.GeneratePlayground -logFile Logs/costsplit_scene.log
unity_hold_wait
"$UNITY_BIN" -batchmode -quit -projectPath "$(pwd)" -executeMethod CommandLineBuild.BuildPlaygroundStandalone -logFile Logs/costsplit_build.log
if grep -q "Shader error\|error CS" Logs/costsplit_build.log Logs/costsplit_scene.log; then
  echo "BUILD ERROR:"; grep -hE "Shader error|error CS" Logs/costsplit_build.log Logs/costsplit_scene.log | sort -u | head; exit 1
fi
[ -d "$APP" ] || { echo "BUILD FAILED"; tail -n 20 Logs/costsplit_build.log; exit 1; }
echo "build ok"
