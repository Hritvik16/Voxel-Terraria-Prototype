#!/bin/bash
# Opens a Terminal window per overnight session: the runner (tmux `overnight`) and every task
# the runner has started (watch-task.sh: done ones print, the running one follows live).
#   tools/overnight/open-views.sh            runner + all tasks in runner.log
#   tools/overnight/open-views.sh 04 05      runner + just these
# Window > Merge All Windows turns them into tabs of one window. Read-only: starts no agent.
cd "$(dirname "$0")/../.." || exit 1
ROOT=$(pwd)
open_win() {   # $1 title, $2 command
  osascript - "$1" "$2" <<'EOF' >/dev/null
on run argv
  tell application "Terminal"
    set t to do script (item 2 of argv)
    set custom title of t to (item 1 of argv)
    activate
  end tell
end run
EOF
}
if tmux has-session -t overnight 2>/dev/null; then
  open_win "runner" "tmux attach -t overnight"
fi
if [ $# -gt 0 ]; then names=("$@"); else
  names=($(sed -n 's/.* start \([0-9][0-9]-[a-z0-9-]*\)$/\1/p' Logs/overnight/runner.log | awk '!s[$0]++'))
fi
for n in "${names[@]}"; do
  open_win "task $n" "cd '$ROOT' && tools/overnight/watch-task.sh '$n'"
done
