# 93b4-shell-march-cost

Requires: 93b3-far-band-after-shells-cost

Approval: supervisor, 2026-10-16 ~20:20, Queue decisions doc Q57 (a) as the default (owner rule "recommended option is
the default" for reversible engineering choices: measurement method / which fix). Follows 93b3's STOP_FOR_OWNER
(`tools/overnight/blocked/93b3-far-band-after-shells-cost.HANDOFF.md` -- read it first). **This IS a performance task:**
PREAMBLE rule 9 (warm steady state, one warm-up launch, ABBA) applies and frame time may be quoted. Branch `engine-phase`.
**Power:** 93b3 ran entirely on battery (21 -> 13%). Before the warm-up, check `pmset -g batt`; if on battery, say so at
the top of the report and treat absolute ms with care (arms inside one launch stay comparable).

## Why
93b3 (`6e66b24`, `Logs/costsplit_2026-10-16_194728.txt`): drawn after the shells the far band costs ~0 ms; switching the
shells to tiers 1-2 saves 15.5 ms (walk_near) / 12.2 ms (walk_open) p50. So the shell march is the cost, and it is NOT split:
the primary `ShellMarch(rayStart, rayDir, ...)` in CSMainBody (~3635) vs the shell-hit sun shadow
`ShellMarch(so, normalize(_SunDir), ...)` (~3948). Leads from 93b3's heat map: walk_open sky rays take 25-48 shell
iterations to end ~51 m out; distant terrain 49-96+; walk_near is almost all tier-0 wall yet still pays +15.5 ms.

## Step 1: split (capture-only arms, no default changes)
ONE ON launch per pose (walk_near, walk_open), same settings as 93b3 (`run-cost-split.sh`, fsr3 540, quality, far band +
far shadows + `-farshade 1 -seethrough 1 -riverlook 1`, noon, `-shells 1 -shellworkers 3 -keeptiers 1`), arms F X X F:
1. `shellshadow_off`: skip the shell-hit sun-shadow march (capture-only switch),
2. a count of shell-march iterations split by ray kind (primary sky / primary hit / shadow) and per-pixel mean, from the
   existing ShadowStats slots or new ones under the capture-only path,
3. if walk_near's +15.5 ms is not explained by its few shell pixels: check whether something runs per pixel even where
   tier 0 hits (setup, a directory read, a shadow march from tier-0 hits into the shells).
Name the line(s) in `Raymarch.compute` that account for most of the cost.

## Step 2: one fix, if it is a bug or a pure early-out
A fix is in scope only if it leaves the image byte-identical or within 1 LSB (e.g. a sky ray ending at the shells' top
bound, a shadow ray skipping shells it cannot hit, work skipped where tier 0 already hit). Shells OFF byte-identical.
Then ONE ABBA set (OFF ON ON OFF, or before/after) of the steady arms after the warm-up: p50 / p99 per pose, before -> after.
**STOP_FOR_OWNER** if the fix changes the look or needs design / memory-layout work (e.g. occupancy or air mips for shells,
a new GPU buffer): report the split and the options with cost and gain.

## Acceptance
- Tests: `ShellHandOverTests` / `ShellOrderTests` still green, plus one for the early-out rule on the CPU twin
  `ShellRayReference` (same hit / same shadow with and without it). Fast suite once at the end.
- Report: the split table (arm, p50 ms, delta), the fix, the ABBA before -> after, warm-up log path.
- Stills: walk_open shell heat map (debug view 13) before/after, and walk_open beauty before/after (noon, sun behind),
  captioned.
- `KNOWN_ISSUES.md` "SHELLS COST +10-15 ms STEADY ..." entry and its CLAUDE.md index line updated.
- `## For the doc` block. Then task 93b is re-run (queue, Requires this task).
