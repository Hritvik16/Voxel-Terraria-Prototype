#!/bin/bash
# run-phase7-lighting-ab.sh -- AMENDMENT_8_12_PHASE7_GATE.md's substitute gate.
#
# §13 Phase 7's two budget gates CANNOT BE RUN AS WRITTEN here. Read
# AMENDMENT_8_12_PHASE7_GATE.md §2 before using anything this produces: no Xcode
# (Amdt 8.9 Rule 1), Performance State unreadable (Rule 2), gpuFrameTime
# inflated ~2.6-2.7x (Amdt 8.10), and Unity merges the dispatches into one Metal
# encoder so per-pass attribution does not exist even in principle.
#
# WHAT THIS MEASURES INSTEAD: end-to-end sustained rate, frames / elapsed wall
# time, which IS measurable and is what a player experiences.
#
# EVERY METHOD CHOICE BELOW EXISTS BECAUSE ITS ABSENCE ALREADY PRODUCED A WRONG
# ANSWER IN THIS PROJECT:
#   fullscreen only        FPS §0.1 -- 69.6 vs 26.9 FPS for identical windowed runs
#   frames / elapsed       FPS §0.2 -- exactly 600 of 1800 frames over 16.6 ms in
#                          FOUR different isolation arms, regardless of workload
#   300 s cooldowns        the same config measured 4.172 / 4.751 / 11.812 ms hot
#   counterbalanced        FPS §0.3 -- a one-directional sweep gave a clean, wrong result
#   ONE session, ONE build FPS §3.2 -- a cross-session A/B was wrong because the
#                          UNTOUCHED arm had drifted on a path the change cannot touch
#   one config per launch  fluid persists; a later "idle" config is not idle
#
# ARMS (AMENDMENT_8_12 §4):
#   A  pre-Phase-7 commit build   B  level 3 off      C  level 2 hard
#   D  level 1 half-res           E  level 0 full penumbra
# x scenarios: golden hour, and golden hour WITH LIVE FLUID (§13's true combined gate)
# x gates: the shipped 960x540, and a forced 1920 where cost rises above one
#   refresh interval and stops hiding under the display cap.
#
# RUNTIME. The full counterbalanced matrix is 40 launches at ~360 s each -- about
# four hours. That is intended: this is the phase's headline measurement, not a
# quick check. Reduce it deliberately and say so:
#   ARMS="B E" SCENARIOS="golden" GATES="960" REPS=1 ./run-phase7-lighting-ab.sh
# A reduced run is still a real measurement of what it covers; it is NOT the
# §13 gate, and the report says which arms actually ran.
#
# Usage:  ./run-phase7-lighting-ab.sh
#         COOL=300 ARMS="A B C D E" SCENARIOS="golden combined" GATES="960 1920" REPS=2
set -uo pipefail

UNITY="/Applications/6000.3.10f1/Unity.app/Contents/MacOS/Unity"
APP="Builds/Playground.app"
BASE_APP="Builds/Phase7BaselineA.app"
OUT="$HOME/Library/Application Support/DefaultCompany/Voxel Terraria 1 Byte BrickMap/Phase7LightingAB"
COOL="${COOL:-300}"
ARMS="${ARMS:-A B C D E}"
SCENARIOS="${SCENARIOS:-golden combined}"
GATES="${GATES:-960 1920}"
REPS="${REPS:-2}"          # 2 = counterbalanced (forward pass, then reverse)
SECONDS_PER_RUN="${SECONDS_PER_RUN:-60}"
mkdir -p Logs "$OUT"
# No lock of its own (run it under tools/with-unity-lock.sh); sourced for unity_hold_wait,
# so an owner pause (tools/heavy.sh pause) stops the matrix at the next launch (task 16).
source tools/unity-lock.sh

TS=$(date +%Y-%m-%d_%H%M%S)
REPORT="$OUT/ab_${TS}.txt"

say() { echo "$@" | tee -a "$REPORT"; }

say "PHASE 7 LIGHTING A/B -- AMENDMENT_8_12_PHASE7_GATE.md's substitute gate"
say "started $TS   host $(sw_vers -productVersion)   cooldown ${COOL}s   ${SECONDS_PER_RUN}s per run"
say ""
say "THIS IS NOT §13's BUDGET GATE. §13 asks for per-pass ms with Performance"
say "State checked; neither is obtainable in this workflow (AMENDMENT_8_12 §2)."
say "Headline metric is TRUE SUSTAINED RATE = frames / elapsed wall time."
say "Percentiles are diagnostics only -- 120 Hz ProMotion, the app out-presents"
say "the display, and exactly one frame in three lands over 16.6 ms regardless"
say "of workload (FPS_INVESTIGATION §0.2). NEVER quote p99 as a headline."
say ""
say "ARM A CAVEAT, stated up front: arm A is the PRE-PHASE-7 binary, which has no"
say "-lightingab mode, so it is measured with -stutterprobe instead. The two are"
say "measurement-equivalent (same pose, same settle, same metric) but -stutterprobe"
say "also attaches FrameGapProbe, whose bookkeeping biases arm A SLIGHTLY SLOWER --"
say "i.e. AGAINST finding a Phase 7 regression, the conservative direction."
say ""

# ---------------------------------------------------------------- build
if [ "${SKIP_BUILD:-0}" != "1" ]; then
  say "== Building the Phase 7 arm (one binary; every rung is a config number) =="
  "$UNITY" -batchmode -quit -nographics -projectPath "$(pwd)" \
    -executeMethod Phase5aSceneBuilder.GeneratePlayground -logFile Logs/phase7_ab_scene.log || {
      say "SCENE GEN FAILED"; tail -30 Logs/phase7_ab_scene.log; exit 1; }
  rm -rf "$APP"
  unity_hold_wait   # owner pause stops here, at the launch boundary (task 16)
  "$UNITY" -batchmode -quit -projectPath "$(pwd)" \
    -executeMethod CommandLineBuild.BuildPlaygroundStandalone -logFile Logs/phase7_ab_build.log
  if grep -q "Shader error" Logs/phase7_ab_build.log; then
    say "SHADER ERROR:"; grep -E "Shader error" Logs/phase7_ab_build.log; exit 1; fi
  [ -d "$APP" ] || { say "BUILD FAILED"; tail -40 Logs/phase7_ab_build.log; exit 1; }
fi

# Arm A is a DIFFERENT BINARY -- the pre-Phase-7 commit -- and is built in a
# detached worktree so the working tree is never disturbed. It is the only arm
# that cannot come from the same build, and that is the point of it: it is the
# honest regression baseline.
if echo "$ARMS" | grep -qw A && [ "${SKIP_BUILD:-0}" != "1" ]; then
  BASE_REF="${BASE_REF:-$(git rev-list -n1 --grep='Phase 6' HEAD 2>/dev/null || echo main)}"
  say "== Building arm A from the pre-Phase-7 commit ($BASE_REF) =="
  rm -rf .phase7-baseline
  unity_hold_wait   # owner pause stops here, at the launch boundary (task 16)
  if git worktree add -f --detach .phase7-baseline "$BASE_REF" >>Logs/phase7_ab_build.log 2>&1; then
    ( cd .phase7-baseline && \
      "$UNITY" -batchmode -quit -nographics -projectPath "$(pwd)" \
        -executeMethod Phase5aSceneBuilder.GeneratePlayground -logFile ../Logs/phase7_ab_baseline_scene.log && \
      "$UNITY" -batchmode -quit -projectPath "$(pwd)" \
        -executeMethod CommandLineBuild.BuildPlaygroundStandalone -logFile ../Logs/phase7_ab_baseline_build.log )
    rm -rf "$BASE_APP"
    [ -d ".phase7-baseline/Builds/Playground.app" ] && cp -R ".phase7-baseline/Builds/Playground.app" "$BASE_APP"
  fi
  if [ ! -d "$BASE_APP" ]; then
    say "  !! arm A could not be built. It is DROPPED rather than substituted --"
    say "     a baseline from a different session would reintroduce exactly the"
    say "     drift FPS_INVESTIGATION §3.2 records as producing a wrong number."
    ARMS=$(echo "$ARMS" | sed 's/A//')
  fi
fi

# ---------------------------------------------------------------- run
run_one() {   # arm scenario gate rep
  local arm="$1" scen="$2" gate="$3" rep="$4"
  local app="$APP" flags="" mode="-lightingab -goldenhour" resultdir="LightingAB" resultfile="lighting_ab.txt"
  case "$arm" in
    # ARM A RUNS -stutterprobe, NOT -lightingab, AND THAT IS NOT A SHORTCUT.
    # Arm A is the PRE-PHASE-7 BINARY. It has no -lightingab handler -- that mode
    # did not exist when it was built -- so passing it would fall through
    # PlaygroundCapture.Start's flag chain, launch a plain playable scene, and
    # NEVER QUIT. (It did exactly that on the first attempt and hung the matrix;
    # hence also the timeout below.)
    #
    # -stutterprobe is the right vehicle because it is MEASUREMENT-EQUIVALENT,
    # not merely available. Read both in PlaygroundCapture.cs: each waits for the
    # store, settles 300 frames, calls TeleportToArena, sets rotation
    # Euler(pitch=0, YAW 90, 0) -- the same GroundHorizon pose -- honours
    # -nofluid and -gateres identically, settles a further 180 frames, then
    # samples Time.unscaledDeltaTime and reports the same
    # "TRUE SUSTAINED RATE: frames / elapsed" line. The sequences match.
    #
    # THE ONE REAL DIFFERENCE, STATED: -stutterprobe attaches FrameGapProbe,
    # which adds per-frame attribution bookkeeping that -lightingab does not run.
    # That biases arm A SLOWER, i.e. AGAINST finding a Phase 7 regression -- the
    # conservative direction. It is recorded here and in the report rather than
    # waved past, because an unstated instrumentation difference between two arms
    # is exactly the confound FPS_INVESTIGATION §3.2 is about.
    A) app="$BASE_APP"
       mode="-stutterprobe -stutterpitch 0 -stutterseconds $SECONDS_PER_RUN"
       resultdir="StutterProbe"; resultfile="stutter_probe.txt" ;;
    B) flags="-shadowlevel 3" ;;
    C) flags="-shadowlevel 2" ;;
    D) flags="-shadowlevel 1" ;;
    E) flags="-shadowlevel 0" ;;
  esac
  [ "$scen" = "golden" ] && flags="$flags -nofluid"

  local free; free=$(df -g . | tail -1 | awk '{print $4}')
  if [ "$free" -lt 5 ]; then say "!! ${free}GB free, below the 5GB floor -- stopping"; exit 2; fi

  unity_hold_wait   # owner pause stops here, before the cooldown and launch (task 16)

  say ""
  say "---- arm $arm  scenario $scen  gate $gate  rep $rep   (cooling ${COOL}s, ${free}GB free) ----"
  sleep "$COOL"

  # A HARD TIMEOUT, because a rig that can hang the whole matrix on one bad arm
  # is worse than a rig that drops one arm. Generous: settle + measure + startup
  # is about 2 minutes, so 8 is a runaway guard rather than a bound on the run.
  ( open -n -W "$app" --args $mode -fullscreen \
      -gateres "$gate" -abseconds "$SECONDS_PER_RUN" $flags ) &
  local pid=$!
  local waited=0
  while kill -0 $pid 2>/dev/null && [ $waited -lt 480 ]; do sleep 5; waited=$((waited+5)); done
  if kill -0 $pid 2>/dev/null; then
    say "  !! arm $arm did not quit within 480 s -- killed. NO NUMBER FROM THIS ARM."
    pkill -f "$(basename "$app" .app)" 2>/dev/null
    kill $pid 2>/dev/null
    return
  fi

  local d; d=$(ls -1d "$HOME/Library/Application Support/DefaultCompany/Voxel Terraria 1 Byte BrickMap/$resultdir"/*/ 2>/dev/null | sort | tail -1)
  if [ -z "$d" ] || [ ! -f "${d}${resultfile}" ]; then
    say "  NO RUN FOLDER -- the arm produced nothing"; return; fi
  sed 's/^/  /' "${d}${resultfile}" | tee -a "$REPORT" >/dev/null
  grep -E "TRUE SUSTAINED RATE" "${d}${resultfile}" | sed 's/^/  /'
}

for rep in $(seq 1 "$REPS"); do
  # COUNTERBALANCED: even reps run the arms in reverse, so a monotone
  # position/thermal decay across the sequence cannot be read as an arm effect.
  ORDER="$ARMS"
  [ $((rep % 2)) -eq 0 ] && ORDER=$(echo "$ARMS" | tr ' ' '\n' | tail -r | tr '\n' ' ')
  say ""
  say "=========== REP $rep   order: $ORDER ==========="
  for gate in $GATES; do
    for scen in $SCENARIOS; do
      for arm in $ORDER; do run_one "$arm" "$scen" "$gate" "$rep"; done
    done
  done
done

say ""
say "=========== DONE ==========="
say "Report: $REPORT"
say ""
say "READ THE RAW NUMBERS, NOT A SUMMARY. Quote TRUE SUSTAINED RATE only, and"
say "state which arms actually ran -- a reduced matrix is a real measurement of"
say "what it covered and is NOT §13's gate."
echo "Report: $REPORT"
