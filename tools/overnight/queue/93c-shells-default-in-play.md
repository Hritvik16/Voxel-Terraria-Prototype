# 93c-shells-default-in-play

Requires: 93b-shells-perf-breakdown 93a-engine-14e1-edit-summaries-live

(Was `tools/overnight/later/90-shells-default-in-play.md`; copied and updated by task 90b. The original stays in later/.)

Approval: owner, 2026-10-15 in chat, Q49: "yes just for the visuals" conditional on the frame time task 93b (was 89)
measures (the rule is copied below). Re-approved in order by Amendment 8.15 Q10 (a) (owner, 2026-10-15;
`tools/overnight/done/90a-amendment-8-15-simulation-reach.md` "## Owner answer"). Engine plan rows 11 / 12, Amendment
8.14 rows 1 and 5. Branch `engine-phase`.

## What changed since the original file
- **One change only: the play default.** The original's item 2 ("shells ON stops allocating cascade tiers 1-2") is now
  its own task, `93d-engine-14d1-free-cascade-tiers` (8.15 row D1). Do not do it here.
- **Requires E1 (93a):** with shells the play default, a block edit 69-128 m away would otherwise be live but not drawn
  (8.15 §5). If 93a is not done, this task must not run.

## Step 0 (decides whether to do anything)
Read task 93b's result (`tools/overnight/done/*93b-shells-perf-breakdown*` and its HANDOFF in git history:
`git log --grep '^93b: HANDOFF'`). Rule, decided before measuring:
- ON p50 <= OFF + 1 ms: do the change below.
- ON 1-3 ms slower: do the change below; 93b has written the `later/` cost task.
- ON > 3 ms slower, or 93b wrote STOP_FOR_OWNER: change nothing, write `STOP_FOR_OWNER` quoting 93b's table.

## Change (one change: play defaults to shells)
1. Play (`./run-play.sh`, `PlaySettings.json`) starts with shells ON (`-shells 1` behaviour, the shell worker count 93b
   used). Add an F6 row "LOD shells" (PlaySettings field `shells`), and `-shells 0|1` overrides per run as flags do today.
2. **Rigs and capture runs are unchanged:** they never read PlaySettings, keep shells OFF unless they pass `-shells 1`,
   and stay byte-identical (`run-phase7-rig.sh` 19/19 shots identical, PASS 37 FAIL 0).
3. Do NOT touch tier 1-2 allocation (93d), the cascade code, `-detailspeed` or `FarStartT`. No frozen API changes.

## Acceptance
- Tests: PlaySettings round-trips the new field; the flag overrides it; rigs/capture never read it. Own tests while
  iterating (`-testFilter`), `SUITE=all` once at the end (`tools/with-unity-lock.sh`).
- Memory (not frame time, PREAMBLE rule 10): peak process MB and Metal MB in play, shells OFF -> ON default, same poses.
  Must be under the 3 GB cap (task 85 measured 2,999 MB with both allocated); report the number. If it is over, write
  `STOP_FOR_OWNER` (93d frees 193 MB, est. from the task 55 ledger).
- Stills, ONE launch (<= ~10 min, short fixed waits, PREAMBLE launch budget): play at 3 poses (e.g. eye_level_2,
  glide_400m, peak_from_2km), midday with the sun behind the camera, OFF vs ON, captions naming what to look at
  (horizon shapes, seams, any holes where shells had not filled; report missing shell counts).
- HANDOFF `## For the doc`: which rule applied, peak MB OFF -> ON, the sheet.
