# 93c-shells-default-in-play

Requires: 93a-engine-14e1-edit-summaries-live 99-shell-sky-ray-exit

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

## Step 0 -- REPLACED by the owner (2026-10-17 ~13:10, Q63 (a)): switch now, 93b skipped
93b's third run did not happen (Mac on battery; `tools/overnight/blocked/93b-shells-perf-breakdown.HANDOFF.md`). The supervisor
showed the owner the estimate (17 Oct re-run +12.7 / +13.9 ms steady p50 minus task 99's -4.7 / -2.6 ms = roughly +8 / +11 ms,
equal while walking) and the owner chose to **switch play to shells now, without the measurement**: D1-D4's memory savings,
far detail and far-edit visibility only reach play with shells on. Frame time is checked at the next performance milestone;
the remaining march work is `later/102-shell-march-cost.md`. So: no Step 0 rule, do the change below. No frame-time arms
(rule 10). In HANDOFF, say plainly that standing-still frames in play are expected ~8-11 ms slower (estimate, not measured).

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
- **Owner's blocky-ridge check (owner, 2026-10-15, on 93a's `farwall_far_*` shots: "kinda blocky, our 0.1 m
  granularity doesn't shine ... the yellow/grey ridge and the topography there look off"):** 93a's `-shellsdebug` twin
  shows that ridge and the mountain left of it were drawn by the 2.5D FAR BAND (grey tint), because shell 3+ had not
  streamed yet (shell 3 0/6 around the tower, ~1,500 shell-3 chunks missing after 93a's short wait). In the same launch,
  re-shoot 93a's far pose (`-farwall` far pose, camera (3904.6, 978.5, 4083.8) m, its heading) with shells ON, and for this
  ONE pose wait until the shells covering the frame are filled (missing 0 for shells 1-4 inside the view, cap 4 min; say
  what is still missing at the cap). The launch budget's short-wait rule is lifted for this pose only, because the
  question IS what the filled shells look like. Shoot plain + `-shellsdebug` tint, plus a 4x crop of the ridge. Caption:
  which shell draws the ridge now, and whether it still looks stepped/striped (if it does, that is the generator's
  surface, not the far band -- say so).
- HANDOFF `## For the doc`: which rule applied, peak MB OFF -> ON, the sheet, the blocky-ridge pair (93a vs filled).
