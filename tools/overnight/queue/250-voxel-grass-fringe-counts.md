# 250-voxel-grass-fringe-counts

Model: opus

Approval: SUPERVISOR 2026-11-20, experiment E3 of `WORLD_FILL_BUDGET.md` sec. 5 ("E3 -- 246c-voxel-grass-fringe-counts"; owner question
Q-246e recommends E1 then E3; E1 is done as task 248). Queued now under the never-idle rule: content behind a flag, DEFAULT OFF, counts
only. The owner uses the Mac all day: NO frame times (rule 10). This is the cheapest answer to "will small foliage hurt performance too?"
-- the owner asked exactly that on 2026-11-20.

Do E3 as written in `WORLD_FILL_BUDGET.md` sec. 5:
- Generator flag `-fringe 1` (DEFAULT OFF): 1-2 voxel grass stubs (0.1 m voxels, 1-3 tall) on grass-material surface voxels, written ONLY into
  surface bricks that are already dense -- no new bricks. Add an assertion/counter that reports new bodies created by fringe = 0.
  Placement from local data only (surface material, slope, a hash of the world position), never tuned to particular terrain
  (memory: no terrain-specific engineering). Stubs must not block the player (check the collision path; if stubs would collide, say so
  and make them non-solid only if that is a one-line, flagged change -- otherwise report it as a finding).
- Give fringe its OWN material id (task 248's note: otherwise it counts as "ground" in the RayStats split). A new GenMat needs a mapping in
  `FarField.GenToEngine` (KNOWN_ISSUES). Extend `RayStatsMatClass` + its CPU twin with a "fringe" class only if a spare counter exists
  without growing the record; otherwise count it inside "ground" and get fringe coverage from an off/on difference -- say which you did.
- `-raystats 1` at forest_eye and ONE open meadow pose (pick an existing grass pose from the capture list; name it), sun 16 deg and noon,
  fringe off / on: 8 launches, counts only.
- Report tier-0 steps per 1 % of screen covered by fringe, next to the leaf rate (~0.3 M per 1 %, ~2 ms; task 248, sec. 1b) and the solid
  rate (~0). If fringe is << the leaf rate, say plainly that voxel grass can carry short clutter at ~0 MB; if not, say that E2 (the
  separate mesh layer, Q-246a) is needed.
- One still per pose, fringe off vs on, at the same pose and sun, close enough to see the stubs (look at it before captioning).

Tests: a generator test that `-fringe 0` output is byte-identical to today (hash), and one that fringe adds 0 bodies on a synthetic patch;
the RayStats twin test if the class was added. Generator change: `SUITE=all` before and after (CLAUDE.md).

Memory per rule 11 (fringe on vs off: GPU body pool peak + refusals, CPU pool + valve, process peak; expect ~+0 bodies). Pass card per
rule 12 (no new pass). Update `WORLD_FILL_BUDGET.md` sec. 1b / 2 / 5 (mark E3 done) and `TASKS.md`.

`## For the doc`: a 3-row table (leaf / wood / fringe: steps per 1 %, est. ms per 1 %, MB), one sentence answer to "can grass be voxels?",
the off/on still with a caption that says where to look.
