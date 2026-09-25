#!/bin/bash
# tools/with-unity-lock.sh <command> [args...]
#
# Runs any older runner script (run-editmode-tests.sh, run-phase7-rig.sh,
# run-acceptance-rig.sh, ...) under the shared one-Unity-at-a-time lock
# (tools/unity-lock.sh). Waits for the lock, holds it for the command's whole
# run, releases it on exit, and passes the command's exit code through.
#   tools/with-unity-lock.sh ./run-editmode-tests.sh
set -uo pipefail
source "$(dirname "$0")/unity-lock.sh"
unity_lock_acquire
echo "$(date +%H:%M:%S) lock acquired -- running: $*"
"$@"
rc=$?
echo "$(date +%H:%M:%S) finished rc=$rc"
exit $rc
