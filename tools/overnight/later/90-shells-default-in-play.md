# 90-shells-default-in-play

Requires: 89-shells-perf-breakdown

Approval: owner, 2026-10-15 in chat, Q49: "yes just for the visuals" conditional on the frame time task 89 measures
(the rule is in 89's task file, copied below). Engine plan rows 11 / 12 ("they replace cascade tiers 1-2"),
Amendment 8.14 rows 1 and 5. Queued by the supervisor 2026-10-15. Branch `engine-phase`.

## Step 0 (decides whether to do anything)
Read task 89's result (`tools/overnight/done/*89-shells-perf-breakdown*` and the HANDOFF it left in git history:
`git log --grep '^89: HANDOFF'`). Rule, decided before measuring:
- ON p50 <= OFF + 1 ms: do the change below.
- ON 1-3 ms slower: do the change below; 89 has written the `later/` cost task.
- ON > 3 ms slower, or 89 wrote STOP_FOR_OWNER: change nothing, write `STOP_FOR_OWNER` quoting 89's table.

## Change (one change: play defaults to shells, and shells-on stops allocating tiers 1-2)
1. Play (`./run-play.sh`, `PlaySettings.json`) starts with shells ON (`-shells 1` behaviour, the shell worker count 89
   used). Add an F6 row "LOD shells" (PlaySettings field `shells`), and `-shells 0|1` overrides per run as flags do today.
2. When shells are ON, cascade tiers 1-2 are NOT allocated (today they stop drawing but stay allocated, task 80). Their
   pools, GPU buffers and native/managed state are skipped at start-up; nothing reads them on the shells path (check
   every reader: `ShellMarch`, validators, the HUD, memledger, `ClipmapValidator`).
3. **Rigs and capture runs are unchanged:** they never read PlaySettings, keep shells OFF unless they pass `-shells 1`,
   and stay byte-identical (`run-phase7-rig.sh` 19/19 shots identical, PASS 37 FAIL 0).
4. Do NOT delete the cascade code or retire `-detailspeed` / `FarStartT` (shells-off path still uses them); that is a
   later cleanup. No frozen API changes.

## Acceptance
- Tests: with shells ON no tier 1-2 allocation happens (a start-up test or a memledger assertion); PlaySettings
  round-trips the new field; shells-off byte-identical as above. Fast suite while iterating, `SUITE=all` at the end.
- Memory (not frame time): peak process MB and Metal MB in play with shells ON, before (tiers allocated) -> after, at
  the same poses. Must be under the 3 GB cap; report the number.
- Stills, ONE launch (<= ~10 min, PREAMBLE launch budget): play at 3 poses (e.g. eye_level_2, glide_400m,
  peak_from_2km), midday with the sun behind the camera, OFF vs ON, captions naming what to look at (horizon shapes,
  seams, any holes where shells had not filled).
- HANDOFF `## For the doc`: which rule applied, peak MB before -> after, the sheet.
