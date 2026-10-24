# 93b2-shells-fill-while-walking

Requires: 93d-engine-14d1-free-cascade-tiers

Approval: supervisor, 2026-10-15 ~22:40, under the owner's "recommended option is the default" rule (2026-10-08) for
reversible engineering choices: task 93b blocked with Q52 (its HANDOFF calls it Q50; Q50 was taken) and its
recommendation (a) "hold 93c; find why shell 1 is generated and dropped while walking, split the far band's cost after
the shells; re-run 93b after". Recorded in the Queue decisions doc as "going ahead with (a) unless you object".
This is part one of (a). Part two is 93b3. Branch `engine-phase`.

## Why
Task 93b (`tools/overnight/blocked/93b-shells-perf-breakdown.HANDOFF.md`, `Screenshots/93b/perf93b.txt`): during a
5 m/s `MODE=walk` circle with `-shells 1 -shellworkers 3`, shell 1 held 104-149 chunks at the start and 76 at the end;
shells 2-7 held NONE; no shell ever completed. A counter run (`Logs/tg2world_20261015_222142.txt`) generated 2,347
shell chunks in 3 min and held 170, with 1 over-budget frame in 4,912, so the upload cap is not the cause. At the two
pinned poses shells 1-2 (and 3 at walk_open) were FULL, with 621 / 1,140 chunks unstored. So the shells only work
standing still today; the 93c switch (shells default in play) is held on this.

## Step 1: find the cause (no fix yet)
Count, per shell, over one short `MODE=walk` launch (counts only, no frame time; <= ~10 min, PREAMBLE rule 3):
requested, generated, stored, refused because FULL (`ShellStore`), evicted (and why), discarded on arrival (out of band /
camera moved / superseded) and still in flight. Then say which of these explains "2,347 generated, 170 held":
1. **Page cap too small** for the band (`ShellLayout.PageCapFor`, task 83's x1.25): FULL refusals or evict-to-store
   churn (task 82's "a FULL shell evicts, stores fresh").
2. **Order** (`ShellStreamer.OrderKey`, task 85, owner Q47 (a) nearest on screen first): workers stay on chunks just
   outside shell 1's hole, and the eye walks into them, so they are dropped.
3. **Recentre drops**: in-flight or stored chunks thrown away when the shell grids recentre.
4. Something else the counts show.

## Step 2: fix it, if it is a bug in a sound design (one change)
CLAUDE.md working style: most slowdowns here were bugs. Fix ONLY if the fix keeps the design (8.14 shells, Q47 (a)
order, task 85 hand-over) and its memory stays inside what D1 (93d) freed (tiers 1-2, 193 MB) with the peak under 3,072 MB.
**STOP_FOR_OWNER** instead (commit the counters, put the table in HANDOFF) if the fix needs: more memory than that, a
different order policy than Q47 (a), a memory-layout change, or a frozen-API change. Shells OFF must stay byte-identical.

## Acceptance
- A test for the cause (EditMode, CPU twin: e.g. a camera walking at 5 m/s past the order/caps keeps shell 1 filling).
- The same short `MODE=walk` launch: chunks held per shell at the walk's end before -> after, and how long shell 1 takes
  to fill at the start pose. Target: shells 1-3 hold >= 90% of their band at the walk's end. Counts only, no frame time
  (93b is re-run after 93b3 for that).
- Fast suite once at the end. Screenshots: the walk's end pose, `-shellsdebug` tint, before -> after (one launch each),
  midday, sun behind the camera, captioned (which shell colour should now cover what).
- `KNOWN_ISSUES.md` entry "SHELLS COST +10-15 ms STEADY, NEVER FILL WHILE WALKING": update the fill half.
- `## For the doc`: the counts table, the cause in one line, the before/after tint pair.

## Owner answer (2026-10-16, Queue decisions doc Q54 = (a))
Your Q53 (renumbered Q54 in the doc; Q53 was taken) is answered **(a): extend task 83's cave fill to shells 1-2**
(today `FIRST_FILL_SHELL` = 3, owner Q44 (a)). Step 1 and the throughput fix are DONE and committed (`c265106`); do
NOT redo them. This run is ONE change: lower the fill to shells 1-2 (fluids fill too, as 83b does for shells >= 3),
keeping ShellFill's guarantee that rays from outside see the same (`ShellFillTests` extended to shells 1-2).
Acceptance: the same short `MODE=walk` launch as before (`-walkshellwait`, counts only, no frame time): held per shell
at the walk's end and FULL refusals per shell, after 93b2's fix -> after this; target shells 1-3 >= 90% of their band.
If shell 2 still stops at its cap, report how far short (do not raise pools: that was option (b), not chosen).
Report the extra worker time per shell 1-2 chunk. Fast suite once at the end. Tint before/after at the walk's end.

## Owner answer (2026-10-16, Queue decisions doc Q56 = (a))
Keep the fill on shells 1-2 (`52aad5e` stands). Its worker cost is cut in a new performance task,
`93b2d-shell-fill-worker-cost` (goal <= ~10 ms per chunk). 93b2 is closed as done; 93b3 is released.
