#!/bin/bash
# tools/session-size-hint.sh -- UserPromptSubmit hook (.claude/settings.json).
#
# Owner, 2026-10-01: usage on a Pro plan is the scarce resource, and a long
# session re-sends its whole history every turn. When this session's transcript
# crosses another STEP_MB, tell the owner (systemMessage) and tell Claude
# (additionalContext) to consider finishing the current step, writing a
# handoff, and continuing in a fresh session. Warns ONCE per step, not on every
# prompt. Transcript bytes are a proxy for context size, not an exact count.
STEP_MB="${SESSION_HINT_STEP_MB:-8}"
input="$(cat)"
transcript="$(printf '%s' "$input" | jq -r '.transcript_path // empty' 2>/dev/null)"
session="$(printf '%s' "$input" | jq -r '.session_id // "unknown"' 2>/dev/null)"
[ -n "$transcript" ] && [ -f "$transcript" ] || exit 0

bytes=$(stat -f %z "$transcript" 2>/dev/null || stat -c %s "$transcript" 2>/dev/null || echo 0)
mb=$((bytes / 1048576))
step=$((mb / STEP_MB))
[ "$step" -ge 1 ] || exit 0

state="${TMPDIR:-/tmp}/claude-session-hint-${session}"
last=$(cat "$state" 2>/dev/null || echo 0)
[ "$step" -gt "$last" ] || exit 0
echo "$step" > "$state"

jq -n --arg mb "$mb" '{
  systemMessage: ("Session transcript is ~" + $mb + " MB. A fresh session for the next task is likely cheaper on plan usage."),
  hookSpecificOutput: {
    hookEventName: "UserPromptSubmit",
    additionalContext: ("SESSION SIZE: this transcript is ~" + $mb + " MB, so every turn re-sends a large history. If the new request is a separate task (or this one is at a natural stopping point), first finish and commit the current step, write SESSION_HANDOFF.md (what was done, evidence, state, next step, and a ready-to-paste prompt for a fresh session), then tell the owner in one line that starting a fresh session with that prompt will save usage. For queued or overnight work, add the task to tools/overnight/queue/ instead (each queued task runs in its own fresh session). Do not stop mid-change to do this.")
  }
}'
exit 0
