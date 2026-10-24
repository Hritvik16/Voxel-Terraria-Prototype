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

## Supervisor decision (2026-10-17 03:2x): (b), recommended option is the default (owner rule 2026-10-08), Queue decisions Q59
Your STOP_FOR_OWNER (blocked/93b5-shell-air-brick-mask.HANDOFF.md) offered (a) one non-exact leap per group, (b) exact by
construction, (c) leave it off. Going ahead with **(b)**: every shell-march leap computes t from the exit plane
((exit plane - start) x tDelta form), in `ShellMarch` AND `ShellRayReference.March`, so one group leap ends exactly where the
last brick leap would. Then replace the exact inner loop with ONE group leap per empty group (the `shellgroup` arm's speed).
Continue from commit 70ddcf5 / 2c1fa45; do not redo the mask builder.
Acceptance for this continuation:
- `ShellAirMaskTests` (2) holds as written: mask on = off in EVERY Result field (t included) on the ray set; (3) mutation still FAILS.
  `ShellHandOverTests`, `ShellOrderTests`, `ShellMarchCacheTests` green (their CPU twin now uses the new t form; if an expected
  value moves, say by how much and why, never loosen a tolerance to hide it).
- Identity: `-coststillraw` mask on vs off 0 px at walk_near and walk_open. Also new-t vs TODAY's commit (2c1fa45) stills: report
  the px that differ (expected 0; an LSB-level diff is acceptable ONLY if under 0.01% of pixels, cite the max channel delta).
- Perf: one warm-up, one ABBA per pose like run 2/3 (F X X F, X = shellmask_off): p50/p99 + iterations/pixel. If the mask ON is
  faster by >= 1 ms p50 at both poses, make it DEFAULT ON (`-shellmask 0` = off) and update CLAUDE.md / KNOWN_ISSUES; otherwise
  leave it OFF and say so (no further STOP needed).
- Fast suite once at the end. `## For the doc` block. 93b re-runs after this (it Requires 93b5).
STOP_FOR_OWNER only if the new t form cannot keep the CPU twin and GPU agreeing, or changes more than 0.01% of pixels vs today.

## Supervisor decision 2 (2026-10-17 04:4x): (b) close the air-brick line, recommended option is the default (Queue decisions Q60)
The exact group leap (251505e) is not >= 1 ms faster at both poses and moves 0.021% px vs today. Closed: the mask stays built
(+33 KB) and OFF (`-shellmask 1`), mask off = today's march byte for byte. No cheaper-leap task. 93b re-runs next; the shells'
+12-15 ms is elsewhere (air-chunk leaps 34-47%, tier-0 leaps 17-23% of iterations, per-iteration checks).
