# 93b5-shell-air-brick-mask

Requires: 93b4-shell-march-cost

Approval: **owner, 2026-10-17, in chat: "i say a)"** = Queue decisions doc Q58 (a). This approves a memory-layout ADDITION:
one new GPU buffer (a 64-bit air-brick mask per paged shell chunk, ~50 KB) plus its ShellStore upload. Nothing else in the
layout changes. Branch `engine-phase`. **This IS a performance task:** PREAMBLE rule 9 (warm steady state, one warm-up
launch, ABBA) applies and frame time may be quoted. Check `pmset -g batt` first and say in the report if on battery.

## Why
93b4 (`7a5231e`; read `tools/overnight/blocked/93b4-shell-march-cost.HANDOFF.md` and the KNOWN_ISSUES "SHELLS COST" entry):
the shells' +12-15 ms over tiers 1-2 is the PRIMARY shell march (13.7 ms walk_near / 19.1 ms walk_open p50), and 75% / 43%
of its iterations are one-brick `LeapSpanReseedClosedForm(... (voxel >> sh) << sh, 1 << sh ...)` steps (sh = k + 3) through
AIR bricks of paged shell chunks. walk_near: 5.8% of pixels at 94 iterations each hold their SIMD groups; walk_open: 30
iterations per pixel. The read caches (93b4) are in and stay.

## The change (one)
- Per paged shell chunk, a 64-bit mask: bit g set = the 4x4x4-brick group g (of the chunk's 4x4x4 groups) holds at least one
  non-air brick. Built on the CPU where the chunk's page is written (`ShellStore.Store`, including `replace: true` redraws,
  op / edit-summary redraws, cave-fill output), uploaded with the page / directory flush through the existing staging path,
  freed with the page. Size it from the shell page cap (`ShellLayout.PageCapFor`); report the MB.
- `ShellMarch`: in a paged chunk, when the ray's current brick group's bit is 0, leap the whole group (the same closed-form
  leap at sh = k + 3 + 2) instead of one brick; otherwise as today. Exact: an empty group contains only air bricks, so the
  hit / ABSENT result, its t and its material must be unchanged.
- CPU twin `ShellRayReference.March` gets the same rule behind a flag; the mask builder is shared or mirrored.
- A capture-only switch to turn the mask leap off (`_DebugShellSkip` bit, like 93b4's caches) for the ABBA and identity check.

## Acceptance
- Tests: a new `ShellAirMaskTests`: (1) the mask matches a brute-force scan of the page for generated, op-redrawn and filled
  chunks; (2) `ShellRayReference.March` with the mask on vs off gives every Result field equal on >= 3,000 rays (reuse
  `ShellMarchCacheTests`' ray set: hole corners, shadows); (3) a mutation (a mask bit cleared on a non-air group) FAILS (2).
  `ShellHandOverTests`, `ShellOrderTests`, `ShellMarchCacheTests` stay green. Fast suite once at the end.
- Identity: no-upscaler stills (`-coststillraw`) mask on vs off: 0 pixels differ at walk_near and walk_open (static scene).
  If any differ, find why before measuring: it is a bug, not an acceptable LSB.
- Perf: one warm-up, then ONE ABBA in one launch per pose (F X X F; X = mask off), same settings as 93b4 (`run-cost-split.sh`,
  fsr3 540, quality, far band + far shadows, `-farshade 1 -seethrough 1 -riverlook 1`, noon, `-shells 1 -shellworkers 3
  -keeptiers 1`): p50 / p99 per pose before -> after, shell iterations per ray and per pixel before -> after (the `[shell ...]`
  counter lines), warm-up log path. The estimate to check against: iterations -40-50% walk_near / -25-30% walk_open.
- Stills: walk_open shell heat map (debug view 13) mask off / on, and walk_open beauty (noon, sun behind), captioned.
- `KNOWN_ISSUES.md` "SHELLS COST ..." entry and its CLAUDE.md index line updated with the new number.
- `## For the doc` block. Then 93b is re-run (queue, Requires this task).
- STOP_FOR_OWNER only if the mask cannot be exact, or if it needs more than ~1 MB of GPU memory.
