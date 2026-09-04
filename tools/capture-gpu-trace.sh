#!/bin/bash
# tools/capture-gpu-trace.sh
#
# Automated GPU timing capture via Instruments' command-line driver (xctrace).
# NO Xcode GUI is involved and none is opened.
#
# ---------------------------------------------------------------------------
# RELATIONSHIP TO AMENDMENT_8_9 §0 RULE 1 ("No Xcode, ever, for any
# performance measurement")
# ---------------------------------------------------------------------------
# That rule was written against the Xcode GUI capture workflow. This script is
# a DELIBERATE, USER-AUTHORISED revisit of it, using the command-line tool
# instead, because per-kernel GPU attribution does not exist by any other means
# in this project (OPTIMIZATION_CANDIDATES.md's closing section says so, and
# says the choice is a human's to make). It was made.
#
# What this produces is NOT covered by the frame-time discipline in CLAUDE.md:
# it is per-encoder GPU duration, not frame time. ./run-acceptance-rig.sh
# remains the only trusted frame-time source. Nothing here replaces it.
#
# ---------------------------------------------------------------------------
# WHY DEVELOPER_DIR IS SET EXPLICITLY
# ---------------------------------------------------------------------------
# xctrace ships inside Xcode, not the Command Line Tools. On this machine
# `xcode-select -p` points at /Library/Developer/CommandLineTools, so plain
# `xcrun xctrace` fails with "requires Xcode". Xcode-beta lives in ~/Downloads
# and is NOT selected system-wide -- pointing DEVELOPER_DIR at it for the
# duration of this script avoids `sudo xcode-select -s`, which would change the
# toolchain for every other build on this machine (including Unity's).
set -uo pipefail

XCODE_DEV="${XCODE_DEV:-$HOME/Downloads/Xcode-beta.app/Contents/Developer}"
APP="Builds/PlaygroundTrace.app"
BIN="$APP/Contents/MacOS/Voxel Terraria 1 Byte BrickMap"

# xctrace --launch is given a WRAPPER SCRIPT, not the binary directly.
# MEASURED: launching the Unity binary directly under xctrace made it exit(1)
# 1.16 s in, before Unity even wrote a log -- the recording "succeeded" and
# contained nothing but startup. Launching a one-line exec wrapper works and
# runs the full window. Cause not established; the wrapper is the workaround.
WRAPPER="$(mktemp -t vecapture).sh"
printf '#!/bin/bash\nexec "%s" -gputrace -logFile /tmp/ve_gputrace.log\n' "$BIN" > "$WRAPPER"
chmod +x "$WRAPPER"
trap 'rm -f "$WRAPPER"' EXIT
OUT_DIR="${OUT_DIR:-gpu-traces}"
LIMIT="${LIMIT:-15s}"
STAMP=$(date +%Y%m%d_%H%M%S)
TRACE="$OUT_DIR/fluid_trace_$STAMP.trace"

if [ ! -x "$XCODE_DEV/usr/bin/xctrace" ]; then
  echo "FAIL: xctrace not found at $XCODE_DEV/usr/bin/xctrace"
  echo "      xctrace ships with Xcode, not the Command Line Tools."
  echo "      Set XCODE_DEV to an Xcode.app's Contents/Developer directory."
  exit 1
fi
export DEVELOPER_DIR="$XCODE_DEV"
XCTRACE="$XCODE_DEV/usr/bin/xctrace"

if [ ! -d "$APP" ]; then
  echo "FAIL: $APP not found. It is a DEVELOPMENT build, built separately"
  echo "      from Builds/Playground.app on purpose. Build it with:"
  echo "        /Applications/6000.3.10f1/Unity.app/Contents/MacOS/Unity \\"
  echo "          -batchmode -quit -projectPath . \\"
  echo "          -executeMethod CommandLineBuild.BuildPlaygroundTraceStandalone \\"
  echo "          -logFile pg_trace_build.log"
  exit 1
fi

# The labels this trace is for live in the BINARY. A stale build captures
# beautifully and shows nothing, which is the most expensive kind of failure
# in this project -- so refuse to guess.
NEWEST_SRC=$(find Assets -name '*.cs' -newer "$BIN" -print -quit 2>/dev/null)
if [ -n "$NEWEST_SRC" ]; then
  echo "FAIL: $APP is OLDER than $NEWEST_SRC"
  echo "      The debug-group labels come from the build. Rebuild first:"
  echo "        ./run-playground.sh"
  exit 1
fi

mkdir -p "$OUT_DIR"
echo "== Recording (Metal System Trace, $LIMIT) =="
echo "   template : Metal System Trace"
echo "   target   : $BIN -gputrace   (via exec wrapper $WRAPPER)"
echo "   output   : $TRACE"
echo

# --launch takes the executable INSIDE the bundle, not the .app.
# --no-prompt keeps it non-interactive (no privacy dialog).
# -gputrace primes the arena (water+sand+lava all venting) and never quits;
# xctrace's --time-limit terminates it.
set -x
"$XCTRACE" record \
  --template 'Metal System Trace' \
  --time-limit "$LIMIT" \
  --no-prompt \
  --output "$TRACE" \
  --launch -- "$WRAPPER"
RC=$?
set +x

# SUCCESS IS THE TRACE EXISTING, NOT THE EXIT CODE. MEASURED: this xctrace
# (27.0) printed "Recording completed. Saving output file..." and wrote a 133 MB
# trace, then exited 54. Gating on $? threw away a good capture.
if [ ! -d "$TRACE" ]; then
  echo "FAIL: recording did not produce $TRACE (xctrace exit $RC)"
  exit 1
fi
[ $RC -ne 0 ] && echo "note: xctrace exited $RC but the trace was written; continuing."

echo
echo "== Table of contents (discovering the actual schema names) =="
# The correct --xpath is NOT guessed: it is read from this run's own TOC. The
# schema names differ between Instruments versions and templates, so the
# exporter below selects from what THIS trace actually contains.
"$XCTRACE" export --input "$TRACE" --toc --output "$TRACE.toc.xml" 2>&1 | tail -3
echo "TOC written: $TRACE.toc.xml"
grep -o 'schema="[a-z0-9-]*"' "$TRACE.toc.xml" | sort -u | sed 's/^/    /'

echo
echo "== Exporting tables =="
python3 tools/parse-gpu-trace.py --trace "$TRACE" --xctrace "$XCTRACE"
