# 218-bug-jungle-high-footprint

Model: opus

Approval: supervisor, 2026-11-16, "recommended option is the default" (reversible engineering): Q1 (a) from task 216's HANDOFF. Source: KNOWN_ISSUES
entry "JUNGLE_HIGH FOOTPRINT 9-10 GB", `Logs/overnight/216/killed/`, `Logs/overnight/watchdog.log`. Needs the owner pause lifted (the runner waits).

## Do
1. Problem: today's defaults at jungle_high (5772.8,566.3,5024.2) reach 9-10 GB physical footprint and the memory watchdog kills the player; task 208
   ran the same pose at a 2.8 GB peak. Changed since: 209/209b (pond shores, pocket streams), 210-214 (world trees ON by default since 214).
2. ISOLATE first (CLAUDE.md: find out, don't guess): ONE launch with 216's flags + `-trees2 0`, same pose, <= ~10 min. Then, only if still high, one
   launch with `-trees2 0` plus the 209/209b switch OFF. Read the `[mem]` ledger lines for WHICH pool grows (CPU pool, GPU pool, tree/ledger data, managed).
3. Fix what the isolation names, exactly and data-derived (memory rule: no terrain-specific tuning; prove on a second jungle/forest pose). Efficient by
   construction (PREAMBLE rule 11). If the cause is the dense trees' design (banyan giants): STOP_FOR_OWNER with the numbers and 2 options.
4. Fast suite before, WHOLE suite before done. Pictures of the pose, looked at. KNOWN_ISSUES entry updated; TASKS.md paragraph.

## End
Done with HANDOFF + `## For the doc`.
