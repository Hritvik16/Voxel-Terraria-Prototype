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
set -uo pipefail
cd "$(git rev-parse --show-toplevel)" || exit 1
command -v tmux >/dev/null || { echo "tmux not installed"; exit 1; }
[ -f SESSION_HANDOFF.md ] || { echo "no SESSION_HANDOFF.md -- write the handoff first"; exit 1; }

prompt="$(awk '/^## Ready-to-paste prompt/{f=1; next} /^## /{f=0} f && /^>/{sub(/^> ?/, ""); print}' SESSION_HANDOFF.md | tr '\n' ' ' | sed 's/  */ /g; s/ $//')"
[ -n "$prompt" ] || { echo "SESSION_HANDOFF.md has no '## Ready-to-paste prompt' block"; exit 1; }

cmd="umask 022 && cd $(printf '%q' "$PWD") && claude $(printf '%q' "$prompt")"
target="$(tmux list-sessions -F '#{session_attached} #{session_name}' 2>/dev/null | awk '$1 > 0 {print $2; exit}')"
if [ -n "$target" ]; then
  # "$target:" (trailing colon) = that SESSION, next free window; a bare "0"
  # reads as window index 0 and fails with "index 0 in use".
  tmux new-window -t "$target:" -n "claude-fresh" -c "$PWD" "$SHELL -ic $(printf '%q' "$cmd"); exec $SHELL -i" \
    || { echo "tmux new-window failed"; exit 1; }
  echo "fresh session started in tmux window 'claude-fresh' of session '$target' (C-b w to switch)"
else
  name="voxel"; tmux has-session -t "$name" 2>/dev/null && name="voxel-$(date +%H%M)"
  tmux new-session -d -s "$name" -c "$PWD" "$SHELL -ic $(printf '%q' "$cmd"); exec $SHELL -i" \
    || { echo "tmux new-session failed"; exit 1; }
  echo "fresh session started in tmux session '$name' -- attach with: tmux attach -t $name"
fi
