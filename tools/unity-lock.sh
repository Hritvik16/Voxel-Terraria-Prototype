# tools/unity-lock.sh -- SOURCE this; do not run it.
#
# ONE Unity build/test/player at a time on this 8 GB Mac, shared with the
# sibling ../VoxelTerraria_compare session, which agreed (2026-09-24) to honour
# the same lock file:
#   - wait while /tmp/voxel-unity.lock exists (unless it is ours, or > 2 h old)
#   - then wait until no Unity / il2cpp / built player has run for 45 s
#   - write "<owner> <start time>" into it; remove it on exit
#
# Process checks match executable NAMES (pgrep -x) and player paths from inside
# this sourced file, so no caller's own command line can match itself -- an
# inline `pgrep -f '<pattern>'` in a shell command matches that very shell,
# which deadlocked a queue here twice.
UNITY_LOCK=/tmp/voxel-unity.lock
UNITY_LOCK_ME="voxel-terraria-1-byte"

unity_busy() {
  pgrep -x Unity >/dev/null || pgrep -x il2cpp >/dev/null ||
    pgrep -f "/Builds/[^/]*\.app/Contents/MacOS/" >/dev/null
}

unity_lock_release() {
  if [ "${UNITY_LOCK_OWNED:-0}" = "1" ]; then rm -f "$UNITY_LOCK"; UNITY_LOCK_OWNED=0; fi
}

unity_lock_acquire() {
  while true; do
    if [ -f "$UNITY_LOCK" ]; then
      local owner age
      owner=$(cut -d' ' -f1 "$UNITY_LOCK")
      age=$(( $(date +%s) - $(stat -f %m "$UNITY_LOCK") ))
      if [ "$owner" = "$UNITY_LOCK_ME" ]; then
        # Already held by this project (an outer script took it). Still never
        # overlap with a straggling process.
        while unity_busy; do sleep 5; done
        return 0
      fi
      if [ "$age" -gt 7200 ]; then echo "removing stale lock ($owner, ${age}s)"; rm -f "$UNITY_LOCK"; continue; fi
      echo "$(date +%H:%M:%S) lock held by $owner -- waiting"; sleep 15; continue
    fi
    local clear=0
    while [ $clear -lt 45 ]; do
      if unity_busy || [ -f "$UNITY_LOCK" ]; then clear=0; else clear=$((clear + 5)); fi
      sleep 5
    done
    # Atomic take (noclobber = O_EXCL): if the sibling took it in the gap
    # since the check above, the write fails and we go back to waiting.
    if ! ( set -o noclobber; echo "$UNITY_LOCK_ME $(date '+%Y-%m-%d %H:%M:%S') pid $$" > "$UNITY_LOCK" ) 2>/dev/null; then
      continue
    fi
    UNITY_LOCK_OWNED=1
    trap unity_lock_release EXIT
    return 0
  done
}
