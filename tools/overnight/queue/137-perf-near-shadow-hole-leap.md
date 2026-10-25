# 137-perf-near-shadow-hole-leap

Model: opus

Approval: owner, 2026-10-24, in chat: "1 works for now" = the noon canyon shortcut from task 133's "Left open". The owner also asked
(same answer): "are we maybe putting ourselves into a trap by engineering for specific terrain when the whole point of this proc gen
system ... is to be able to put in new features later?" So this task carries a GENERALITY RULE (below). PERFORMANCE task: ONE change,
measured, then STOP_FOR_OWNER.

## Background (task 133 HANDOFF, `tools/overnight/done/*133*.HANDOFF.md`, "Left open")
At noon every lit tier-0 sun ray that leaves through the 7-layer band ceiling crosses the shells' hole above the band (treated as air)
in 12.8 m leaps until it is above the shells' highest solid brick (1100 m, the WORLD max) -- ~8 iterations per ray; walk_near
(canyon) pays +6.1 ms p50. Idea: an exact one-step exit from the hole region, and/or a height bound derived from the data near the
ray instead of the global 1100 m.

## Generality rule (owner's concern; required)
- No terrain-specific constants, thresholds or pose-specific paths. Any bound must be DERIVED from the data the GPU already has
  (e.g. a per-column / per-region max-solid-height kept up to date when chunks/shells load AND when the player edits), so it stays
  correct for any generator output, future features (taller structures, floating islands) and player builds.
- Exact: same image (byte-identical no-upscaler stills) everywhere, not just at the measured poses.
- Correctness proof includes an ADVERSARIAL case: a player-built or edited tall pillar / overhang placed so its shadow must reach the
  near ground through the hole region (use the edit API in a capture pose), plus 131/133's cliff1 and cliff2 spots.
- If a data-derived bound needs a new GPU structure or changes memory layout, STOP_FOR_OWNER with the design first (do not build).

## Do
1. Implement behind a flag (`-holeleap 1`; 0 = today). 2. Identity checks above (+ noon and sun 20 at walk_near/walk_open).
3. Measure: decisions written first; on the charger (`pmset -g batt`); warm-up discarded; ONE warm ABBA at walk_near and walk_open at
   noon and sun 20, plus one walk arm. 4. Default ON only if faster at noon walk_near beyond noise, no worse elsewhere, and identical.
5. Fast EditMode suite once at the end (run it even if the owner pauses: wait for the pause to end rather than skip it).

## Deliverable
HANDOFF `## For the doc`: ABBA table, identity results incl. the adversarial pillar (one before|after still, caption), memory, default
decision, and one paragraph on how the bound stays correct for new features and edits. Then STOP_FOR_OWNER. TASKS.md paragraph.
