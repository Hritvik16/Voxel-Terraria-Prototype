# 218b-bug-gpu-view-eviction-proof

Model: opus
Requires: 218-bug-jungle-high-footprint

Approval: supervisor, 2026-11-16, "recommended option is the default": Q2 of task 216's HANDOFF, in its (b) form if 218 fixed the footprint, else (a).
Source: `tools/overnight/blocked/216-bug-gpu-view-priority-eviction.HANDOFF.md`, commit 3aa8acd (code done, default OFF), `Logs/overnight/216_pair.sh`.

## Do
1. Run 216's launch pair (ON then `-gpuviewevict 0`, 3-4 poses, <= ~10 min a launch) with defaults if 218 fixed jungle_high; if 218 is not
   done or STOPPED, use `-trees2 0` (the world task 208 measured) and say so.
2. Target (216's file): GPU refusals at the shot DOWN against `-gpuviewevict 0` (back to <= 857), process peak not up, CPU pool peak + valve counts.
   Look at the jungle_high and walk2 pictures.
3. If refusals go down: flip the default ON (one line each in `Phase4Bootstrapper` + `TerrainClipmap.ViewEvict`), fast suite then SUITE=all,
   update TASKS.md / KNOWN_ISSUES 208 entry. If they cannot be cut: STOP_FOR_OWNER with the numbers.

## End
Done with HANDOFF + `## For the doc`.
