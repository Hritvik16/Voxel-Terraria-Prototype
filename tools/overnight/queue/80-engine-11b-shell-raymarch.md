# 80-engine-11b-shell-raymarch

Approval: `ENGINE_PHASE_PLAN.md` (owner, Q36, 2026-10-09) step 11 part b; Amendment 8.14 adopted 2026-10-11 (owner
"Approve all (a)"). Queued by the supervisor 2026-10-12 after task 77 (11a). Branch `engine-phase`.
Task 77's open question (coarse shells need 1.5-3.4x the estimated bricks; cave fill / caps / holes) is with the
owner and does NOT block this task: keep 77's caps, a refused chunk shows the coarser shell (row 15).

## Change: engine step 11b -- raymarch through the shells, and their seams
Read first: `AMENDMENT_8_14_LOD_SHELLS.md` section 2a (High table) and contract rows 1, 2, 5, 15; task 77's
`tools/overnight/HANDOFF.md` (ShellStore / ShellStreamer / ShellDebugView, `-shells 1`, `-quality`, `-shellsdebug`).
1. `Raymarch.compute` marches shells 1..N from the shell directories 77 uploads (two-level: ABSENT / UNIFORM|mat /
   PAGE), shell count and bounds from a constant buffer (arrays sized for 8), not `#define`s (row 1).
2. **Hand-over by distance (row 5, rule A):** a ray reads shell k between its bounds (High: shell 1 69-138 m,
   2 138-275, 3 275-550, 4 550-1,100, 5 1,100-2,200, 6 2,200-4,400, 7 4,400-6,600). If that chunk is absent or
   refused, it reads shell k+1 (Q2 (a)); the far band draws only past the last shell or where the last shell is
   absent. Up to 8 hand-overs per ray (DDA restart). Shell 0 is still today's 128 m tier-0 window until step 14:
   hand over from tier 0 where it ends/is absent, so nothing between 69 and 128 m changes source twice.
3. **Seams:** no gaps, cracks or dark arcs where shells meet (the "dark arcs = voxel LOD tier seams" in KNOWN_ISSUES
   "BLACK BAR OVER THE SEA"). This also targets BUGS #2 (black crescent under the ledge at `ledge_view`;
   `tools/overnight/later/40-bug-ledge-view-crescent.md`): check it with shells on; if it is gone, say so; if it is
   not a seam, say what it is and leave it.
4. Occupancy bits / air mips for shells if the march needs them to skip empty space (77 left them to 11b).
5. New pose metric (plan row 11 acceptance): **largest voxel on screen <= 1 px** per pose (report the max px size of
   the voxel each ray stopped in, per shell), printed in the pose report.
- Shells stay behind `-shells 1`, **OFF by default** in play and rigs (shells + cascades together are over the 3 GB
  cap; switching the default and retiring cascade tiers 1-2 waits for the owner's answer to 77's Q1). With `-shells 1`
  the cascade tiers 1-2 should not draw (row 2: shells replace them); free them if that is simple, else say so.
- CPU sole terrain writer; no D.1 / §4.4 / frozen API change; pow2 rings, bitwise integer coordinates (§6.2 rule).
- If 11b does not fit one task, do raymarch + hand-over first, commit, and leave seams/metric as named leftovers.

## Acceptance
- Validator/EditMode tests for the hand-over (a ray at each shell boundary reads the right shell; absent -> next
  shell; past the last -> far band). Fast EditMode green; the suite once at the end (PREAMBLE rule 3).
- `run-phase7-rig.sh` result unchanged and shots byte-identical with shells OFF (default).
- **Images (one player launch):** the standard tg2 poses (incl. `ledge_view` and a far horizon / coast pose),
  shells OFF vs ON side by side as a contact sheet; one shot with `-shellsdebug` tinting each shell. Look at them:
  name any seam, hole, colour jump or missing distant land, and where.
- No frame-time measurement (PREAMBLE rule 10). HANDOFF `## For the doc`: largest-voxel px per shell, refusals per
  shell at the poses, the contact sheet with captions, BUGS #2 status.
