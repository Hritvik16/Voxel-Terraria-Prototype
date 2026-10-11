#!/bin/bash
# run-play.sh -- hop into the NEWEST world and play it like a player sees it.
#   ./run-play.sh            the tg2 island on map v4 (surface rebuild B1+) + the far band
#   MAPV=3 ./run-play.sh     the old generator (map v3), for comparison
#   BUILD=1 ./run-play.sh    rebuild the Playground first (scene + release player)
#   KEEP=1 ./run-play.sh     keep your edits from the last session (default: fresh world;
#                            v3 and v4 share one delta folder, so edits would cross over)
#   SHOT=60 ./run-play.sh    one screenshot 60 s after startup (-playshot; path printed at exit)
#   EXTRA="-upscaler fsr1 -renderrows 540" ...   extra player flags (e.g. -detailspeed 1)
# Upscaler (task 52, owner Q38 (a)): play STARTS WITH FSR 3 DYNAMIC -- 1080p output from
# 540-720 internal rows chosen on frame time, as `-upscaler fsr3 -renderrows dyn`. Override
# with EXTRA="-upscaler none|taa|fsr1|stp|fsr3 -renderrows 540|720|dyn"; EXTRA="-renderpreset
# quality" (or -taa 1 / -checker 1) gives the old preset path. F3 leaves the upscaler for
# the presets (restart to get it back). The F1 HUD and the strip show "UPSCALE fsr3 dyn <rows>".
# Settings menu (task 58): F6 in play -- shadows, upscaler, rows, shadow edges, perf overlay,
# minimap; live, saved to PlaySettings.json (beside PlayerConfig.json) and applied at the next
# start. -upscaler / -renderrows / -shadowlevel in EXTRA override the file PER FIELD for that
# run (so -renderrows 720 alone keeps the saved upscaler). Rigs never read the file.
# Start pose (task 32d): the fluid arena's basin can be a lake bed (v4) or the pose can
# sit inside a hill (v3), so a tg2 session stands on the nearest dry column instead;
# the Player.log "[Playground] start pose" lines say where, and what the eye is in.
# Start view (task 33b): -startview also sets the clock to noon (the M key) and levels
# the camera (yaw kept); the clock runs on from there. Only this script passes it.
# Day length (task 33b, owner Q28 (b)): -daylength 1200, a 20-minute day; rigs and
# capture runs keep LightingConfig's 120 s (the player ignores the flag in them).
# Interactive: no capture flags, so Playground applies the interactive render
# preset (RenderPreset.Default) and then FSR 3 dyn, exactly as a player session would. Holds the
# shared Unity lock while the player is open (like run-demo.sh).
set -uo pipefail
UNITY_BIN="/Applications/6000.3.10f1/Unity.app/Contents/MacOS/Unity"
APP="Builds/Playground.app"
INFO="$APP.buildinfo.txt"
MAPV="${MAPV:-4}"
source tools/unity-lock.sh
unity_lock_acquire
mkdir -p Logs

if [ "${BUILD:-0}" = "1" ]; then
  echo "building the Playground (Logs/play_scene.log, Logs/play_build.log) ..."
  rm -rf "$APP"
  "$UNITY_BIN" -batchmode -quit -nographics -projectPath "$(pwd)" -executeMethod Phase5aSceneBuilder.GeneratePlayground -logFile Logs/play_scene.log
  "$UNITY_BIN" -batchmode -quit -projectPath "$(pwd)" -executeMethod CommandLineBuild.BuildPlaygroundStandalone -logFile Logs/play_build.log
  if grep -q "error CS" Logs/play_scene.log Logs/play_build.log; then grep -h "error CS" Logs/play_*.log | sort -u; exit 1; fi
  if grep -q "Shader error" Logs/play_build.log; then grep "Shader error" Logs/play_build.log; exit 1; fi
  [ -d "$APP" ] || { echo "BUILD FAILED (see Logs/play_build.log)"; exit 1; }
  echo "build OK"
fi
[ -d "$APP" ] || { echo "no $APP -- run BUILD=1 ./run-play.sh"; exit 1; }

# Provenance (BuildStamp.cs writes $INFO beside the bundle).
if [ -f "$INFO" ]; then
  BC=$(awk '$1=="commit"{print $2}' "$INFO")
  BDIRTY=$(grep -q '^commit.*(DIRTY)' "$INFO" && echo "DIRTY (uncommitted edits were built in)" || echo "clean")
  BDATE=$(awk '$1=="built"{print $2, $3}' "$INFO")
  echo "build: commit $BC, built $BDATE, $BDIRTY"
  HEAD=$(git rev-parse --short HEAD)
  if [ -n "$BC" ] && ! git merge-base --is-ancestor "$HEAD" "$BC" 2>/dev/null; then
    if git merge-base --is-ancestor "$BC" "$HEAD" 2>/dev/null; then
      N=$(git rev-list --count "$BC..$HEAD")
      CODE=$(git rev-list --count "$BC..$HEAD" -- Assets Packages ProjectSettings)
      echo "WARNING: build is $N commit(s) older than the branch tip $HEAD ($CODE touch Assets/Packages/ProjectSettings) -- BUILD=1 to rebuild"
    else
      echo "WARNING: build commit $BC is not on this branch's history (tip $HEAD) -- BUILD=1 to rebuild"
    fi
  fi
else
  echo "WARNING: no $INFO -- build provenance unknown; BUILD=1 to rebuild"
fi

cat <<KEYS
world: -world tg2 -mapversion $MAPV (far band always on in tg2), far shadows on
keys (Playground.cs RegisterDefaultToys, PlaygroundFlyCamera, PlaygroundHud, Minimap, DevMap):
  click         capture the mouse (first click only captures)   Esc  release it
  Tab           walk <-> fly      R respawn on ground    F go to the fluid arena (the start pose)
  WASD          move; walk: Space jump; fly: E up, Q down, Left Shift fast
  mouse L / R   dig (held) / place the held item (held)    wheel or 1-9  pick item
  Z / X         dig tool smaller / larger
  V vent here (pours the held fluid or sand)   0 stop vents   B bomb   G chaos burst   T shoot
  F1 HUD   F2 help   F3 render preset quality/balanced/performance (leaves the FSR 3 default)
  F4 TAA on/off, preset path only (also hides/shows the minimap -- both listen to F4)
  F5 voxel detail radius at speed on/off
  F6 GRAPHICS SETTINGS menu (saved; Esc or F6 closes)
  C far band on/off   I far band view (beauty -> bands -> far step heat)
  O dev map (whole island + underground slice; Esc/O closes)
  N golden hour  M noon  L midnight  P pause clock  , / . scrub time
  J shadow quality 0-3   K hard/soft shadows   U look Brickmap/Phase 7
  [ / ] light preset   Y lighting view (sun vis -> shadow heat)
KEYS

DELTA="-cleardeltas"; [ "${KEEP:-0}" = "1" ] && DELTA="-keepdeltas"
SHOTARG=""; [ -n "${SHOT:-}" ] && SHOTARG="-playshot $SHOT"
open -n -W "$APP" --args -world tg2 -mapversion "$MAPV" -farband 1 -farshadow 1 -startview -daylength 1200 $DELTA $SHOTARG ${EXTRA:-}
PL="$HOME/Library/Logs/DefaultCompany/Voxel Terraria 1 Byte BrickMap/Player.log"
grep -E "\[FarField\] tg2 WORLD|\[Playground\] (-playshot|start pose)" "$PL" 2>/dev/null | cut -c1-200
grep "\[UpscalerSelect\]" "$PL" 2>/dev/null | sed -n '1p;$p' | cut -c1-200   # the start and the last dyn step
