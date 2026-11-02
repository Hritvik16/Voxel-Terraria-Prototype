# 168c-perf-c1-shadow-continuation

Model: opus

Approval: owner, 2026-11-01, doc Q102 "a for all" (order B -> D or C1 -> the other -> E -> milestone; C1's GPU scratch 3-6 MB approved; exact cuts only).
A PERFORMANCE task, ONE change. Requires: 168 done (D measured ~0 ms, stays OFF behind `-roofskip 1`, so C1 is next); read `tools/overnight/HANDOFF.md`
of 168 (now in `tools/overnight/done/`) and `PERF_EXACT_CUTS.md` section 2 row C1. 168b (the 30 fps ceiling check) runs before this; read its handoff too: if it
says exact cuts cannot reach the target, still finish C1 (already approved) but note it.

## Do
C1: move the near sun ray's shell continuation (133) and the shell-hit sun ray out of the big kernel into a small kernel (like `CSShellPrepass`); a short pass
applies the sun visibility. First check, on paper and in a test, that sun visibility enters the shading linearly; if it does not, split the shading instead.
Behind a flag. Raw stills lean vs today **0 px** at walk_near / walk_open / cliff1 (any non-zero px outside animated water: stop, do not measure). Fast suite once.
Measure on AC only: warm-up discarded, then cost split at `-renderrows 432` ABBA new vs today, walk_near / walk_open. Scratch 3-6 MB max.

## Decision table
- >= 1 ms beyond noise at either view and 0 px: ON in play by default.
- ~0: keep behind its flag, say so. Either way end normally; the supervisor queues E next.
- STOP_FOR_OWNER only for a non-exact result or scratch beyond 6 MB.

## For the doc
Before/after ms per view with noise; memory added; 0 px proof.
