#!/bin/bash
# tools/new-session.sh -- start a FRESH interactive Claude Code session in tmux,
# seeded with the "Ready-to-paste prompt" from SESSION_HANDOFF.md (owner,
# 2026-10-01: start the new session automatically instead of asking me to).
#
# Opens a new window in the attached tmux session when there is one (switch
# with C-b n / C-b w), otherwise a detached session `voxel` (`tmux attach -t
# voxel`). Runs through an interactive zsh so the owner's `claude` wrapper
# (claude-auto-retry) applies. The OLD session should then be closed: two live
# sessions both draw on the plan's usage.
# MODEL=sonnet|opus tools/new-session.sh -- start it on that model (owner, 2026-10-22: supervisor on Sonnet).
set -uo pipefail
cd "$(git rev-parse --show-toplevel)" || exit 1
command -v tmux >/dev/null || { echo "tmux not installed"; exit 1; }
[ -f SESSION_HANDOFF.md ] || { echo "no SESSION_HANDOFF.md -- write the handoff first"; exit 1; }

prompt="$(awk '/^## Ready-to-paste prompt/{f=1; next} /^## /{f=0} f && /^>/{sub(/^> ?/, ""); print}' SESSION_HANDOFF.md | tr '\n' ' ' | sed 's/  */ /g; s/ $//')"
[ -n "$prompt" ] || { echo "SESSION_HANDOFF.md has no '## Ready-to-paste prompt' block"; exit 1; }

cmd="umask 022 && cd $(printf '%q' "$PWD") && claude ${MODEL:+--model $(printf '%q' "$MODEL")} $(printf '%q' "$prompt")"
# Owner 2026-11-06: a new WINDOW inside the attached tmux session was invisible to the owner -> always a detached
# session, then open a Terminal window attached to it so the new session is on screen.
name="voxel-sup"; tmux has-session -t "$name" 2>/dev/null && name="voxel-sup-$(date +%H%M)"
tmux new-session -d -s "$name" -c "$PWD" "$SHELL -ic $(printf '%q' "$cmd"); exec $SHELL -i" \
  || { echo "tmux new-session failed"; exit 1; }
osascript - "$name" <<'OSA' >/dev/null 2>&1
on run argv
  tell application "Terminal"
    set t to do script ("tmux attach -t " & (item 1 of argv))
    set custom title of t to "Supervisor (new)"
    activate
  end tell
end run
OSA
echo "fresh session started in tmux session '$name' and opened in a new Terminal window (or: tmux attach -t $name)"
