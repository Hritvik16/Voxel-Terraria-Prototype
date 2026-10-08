# 16-pause-gate-in-launch-loops

Requires: 14b-pause-drain-only

Follow-up from 14b (tooling only, no owner decision needed). An owner pause
(`tools/heavy.sh pause`) takes effect at the next `unity_lock_acquire`. Scripts
that take the lock ONCE and then launch several players in a loop therefore drain
to their END, not to the next launch: `run-lavalight-ab.sh`, `run-upscale-ab.sh`,
`run-roofskip-ab.sh`, `run-perf60.sh`, `run-looksweep.sh`, `run-overhaul-batch.sh`,
`run-flyfast.sh`, `run-farband.sh`, and `run-phase7-lighting-ab.sh` (no lock of
its own; ~4 h under `with-unity-lock.sh`).

## Change
- In each of those loops, call `unity_hold_wait` (tools/unity-lock.sh) right
  before every player launch / Unity batch step after the first, so a pause stops
  them at the next launch boundary. `unity_hold_wait` returns at once when not
  paused. `run-phase7-lighting-ab.sh` must `source tools/unity-lock.sh` for it.
- Keep the lock held while waiting (do NOT release and re-acquire); `resume`
  refreshes the lock's mtime, so the 2 h stale rule does not fire.
- Never edit a script while it is running (check first).

## Acceptance
- `bash -n` on every edited script.
- One cheap loop script (pick the shortest, e.g. `run-perf60.sh` with its
  smallest settings) with a pause during launch 1: launch 1 completes, the script
  prints `held by owner pause -- waiting` before launch 2, and continues after
  `resume`; `tools/heavy.sh status` lists it under "waiting on the pause".
- Fast EditMode 0 FAIL before committing. Update the KNOWN_ISSUES OWNER PAUSE bullet.
