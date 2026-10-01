#!/bin/bash
# run-fixes.sh -- measures the 2026-09-25 visual fixes, each isolated by a
# runtime switch inside ONE build so every A/B is the same binary:
#   tests    EditMode suite (stops on red)
#   build    RELEASE Playground build (compile + shader gates)
#   lodpop   -lodpop: legacy 64/128 m tier bounds vs pixel-footprint bounds,
#            each against an all-tier-0 reference frame (tools/lodpop-report.py)
#   shimmer  -farshimmer: far level by footprint off vs on (quarter-pixel pairs)
#   lodcost  in-air orbit (-orbitheight 8), -nohud, far band off:
#            A = -tierbounds legacy, P = pixel bounds, A P P A
#   farcost  far band on at 300 m and in map view: F0 = -farfootprint 0,
#            F1 = footprint on, F0 F1 F1 F0 each
#   STAGES="build lodpop" ./run-fixes.sh     MINUTES=3 (per sustained arm)
set -uo pipefail
UNITY_BIN="/Applications/6000.3.10f1/Unity.app/Contents/MacOS/Unity"
APP="Builds/Playground.app"
DATA="$HOME/Library/Application Support/DefaultCompany/Voxel Terraria 1 Byte BrickMap"
MINUTES="${MINUTES:-3}"
STAGES="${STAGES:-tests build lodpop shimmer lodcost farcost}"
RUN_ID="$(date +%Y%m%d_%H%M%S)"
mkdir -p Logs
LOG="Logs/fixes_${RUN_ID}.txt"
has() { [[ " $STAGES " == *" $1 "* ]]; }
source tools/unity-lock.sh
unity_lock_acquire
echo "run $RUN_ID  stages: $STAGES  minutes/arm: $MINUTES" | tee -a "$LOG"

if has tests; then
  ./run-editmode-tests.sh > Logs/fixes_${RUN_ID}_editmode.txt 2>&1
  rc=$?
  tail -n 3 Logs/fixes_${RUN_ID}_editmode.txt | tee -a "$LOG"
  if [ $rc -ne 0 ]; then echo "EDITMODE RED -- stopping" | tee -a "$LOG"; grep -E "Failed|error CS" unity_test.log | head -20 | tee -a "$LOG"; exit 1; fi
fi

if has build; then
  rm -rf "$APP"
  "$UNITY_BIN" -batchmode -quit -nographics -projectPath "$(pwd)" -executeMethod Phase5aSceneBuilder.GeneratePlayground -logFile Logs/fixes_scene.log
  "$UNITY_BIN" -batchmode -quit -projectPath "$(pwd)" -executeMethod CommandLineBuild.BuildPlaygroundStandalone -logFile Logs/fixes_build.log
  if grep -q "error CS" Logs/fixes_build.log Logs/fixes_scene.log; then grep -h "error CS" Logs/fixes_*.log | sort -u | tee -a "$LOG"; exit 1; fi
  if grep -q "Shader error" Logs/fixes_build.log; then grep "Shader error" Logs/fixes_build.log | tee -a "$LOG"; exit 1; fi
  [ -d "$APP" ] || { echo "BUILD FAILED" | tee -a "$LOG"; exit 1; }
  echo "build OK (0 error CS, 0 Shader error)" | tee -a "$LOG"
fi

if has lodpop; then
  echo "== lodpop ==" | tee -a "$LOG"
  caffeinate -dims open -n -W "$APP" --args -lodpop -cleardeltas
  R=$(ls -td "$DATA/LodPop"/*/ 2>/dev/null | head -n 1)
  cat "${R}lodpop.txt" | tee -a "$LOG"; python3 tools/lodpop-report.py "$R" | tee -a "$LOG"; echo "shots: $R" | tee -a "$LOG"
fi

if has shimmer; then
  echo "== far shimmer ==" | tee -a "$LOG"
  caffeinate -dims open -n -W "$APP" --args -farshimmer -farband 1 -farshadow 1 -cleardeltas
  R=$(ls -td "$DATA/FarBand"/*_shimmer/ 2>/dev/null | head -n 1)
  cat "${R}farshimmer.txt" | tee -a "$LOG"; python3 tools/farshimmer-report.py "$R" | tee -a "$LOG"; echo "shots: $R" | tee -a "$LOG"
fi

ARMS="Logs/fixes_${RUN_ID}_arms.tsv"; : > "$ARMS"
arm() {  # arm <scenario> <label> <args...>
  local scen="$1" label="$2"; shift 2
  local tag="fx_${RUN_ID}_${scen}_${label}"
  sleep 60
  echo "-- $scen $label  load: $(uptime | sed 's/.*load averages*: //')" | tee -a "$LOG"
  caffeinate -dims open -n -W "$APP" --args -sustained -motion orbit -gateres 960 -shadowlevel 0 -nohud \
    -minutes "$MINUTES" -tag "$tag" "$@"
  local S; S=$(ls -td "$DATA/Sustained"/*_"$tag"/ 2>/dev/null | head -n 1)
  if [ -n "$S" ]; then printf "%s\t%s\t%s\n" "$scen" "$label" "$S" >> "$ARMS"
    grep -E "FIRST|LAST|WORST|FAR RAYS" "${S}sustained.txt" | tee -a "$LOG"
  else echo "   NO OUTPUT for $tag" | tee -a "$LOG"; fi
}
if has lodcost; then
  echo "== lodcost (in-air orbit, far off, -nohud) ==" | tee -a "$LOG"
  for l in A P P A; do
    case $l in A) arm lodcost A -farband 0 -orbitheight 8 -tierbounds legacy ;; P) arm lodcost P -farband 0 -orbitheight 8 ;; esac
  done
fi
if has farcost; then
  for sc in "high:-orbitheight 300 -orbitpitch 15" "map:-orbitheight 1000 -orbitpitch 70"; do
    name="${sc%%:*}"; args="${sc#*:}"
    echo "== farcost $name (far on + shadows, -nohud) ==" | tee -a "$LOG"
    for l in F0 F1 F1 F0; do
      case $l in F0) arm "$name" F0 -farband 1 -farshadow 1 -farfootprint 0 $args ;; F1) arm "$name" F1 -farband 1 -farshadow 1 $args ;; esac
    done
  done
fi
if [ -s "$ARMS" ]; then
  echo "" | tee -a "$LOG"
  python3 - "$ARMS" <<'PY' | tee -a "$LOG"
import sys, os
from collections import defaultdict
by = defaultdict(list)
for l in open(sys.argv[1]).read().splitlines():
    sc, lab, f = l.split("\t")
    ms = [float(x.split("\t")[1]) for x in open(os.path.join(f, "sustained.tsv")).read().splitlines()[1:] if x.strip()]
    if ms: by[(sc, lab)].append(sum(ms) / len(ms))
print("SUSTAINED ARMS -- mean of 30 s windows (ms/frame), RELEASE fullscreen gate 960, HUD hidden")
for sc in sorted({k[0] for k in by}):
    labs = sorted(l for (s, l) in by if s == sc)
    for lab in labs:
        v = by[(sc, lab)]
        print(f"  {sc:<8} {lab:<3} reps {len(v)}  mean {sum(v)/len(v):7.2f}  spread {max(v)-min(v):5.2f}  ({', '.join(f'{x:.2f}' for x in v)})")
    if len(labs) == 2:
        a, b = (by[(sc, labs[0])], by[(sc, labs[1])])
        d = sum(b)/len(b) - sum(a)/len(a); noise = max(max(a)-min(a), max(b)-min(b))
        print(f"  {sc:<8} {labs[1]} - {labs[0]} = {d:+.2f} ms  ({'REAL' if abs(d) > noise else 'WITHIN NOISE'}: spread {noise:.2f})")
PY
fi
echo "log: $LOG"
