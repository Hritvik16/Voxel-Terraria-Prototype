# 157-torch-flood-l1-oracle

Model: opus

Approval: owner, 2026-10-30, chat "a to all" on task 155's six questions (`TORCH_LIGHT_PLAN.md` §4; answers recorded in
`tools/overnight/done/*155-plan-cheap-torch-light.md`): plan A, the light flood, 12 m, 200 torches capped at 48 MB + 48 MB, RGB per-channel
max, 1-3 frame updates, today's glow kept beyond the 0.1 m window.
Step L1 of `TORCH_LIGHT_PLAN.md` §3. CPU ONLY: no GPU, shader, default or look change. Not a performance task (worker ms per flood
in the metrics report is a CPU microbenchmark in EditMode, not a frame-time claim).

## Do
Exactly the plan's L1 row: CPU oracle `LightFloodReference` (6-connected air, no corner cutting, chamfer distance, RGB per-channel max,
sparse per brick) with incremental add/remove, and the plan's tests 1-6 (sealed walls incl. the corner-touching diagonal give exactly
0; open-air falloff within 8% of Euclidean; L-bend path-length falloff; fuzz incremental == from-scratch byte for byte; removing every
torch frees every slot; admit/evict/re-admit == from-scratch). Generality rule (owner 2026-10-24): no terrain-specific constants;
adversarial cases above are the proof. Hard invariants hold (the oracle reads ChunkStore, writes only its own scratch).
Look check: oracle slice PNGs (top-down light) of 138's tunnel, a sealed room, an L-bend, a diagonal 0.1 m wall.
Metrics report: lit / mixed / air bricks and MB per torch (room, outdoor), split-cell share at 0.2 m (decides L2's storage), worker ms
per flood. Fast suite once.

## Deliverable
HANDOFF + "For the doc" (slice PNGs with captions naming what to look at); TASKS.md paragraph. If the metrics break the plan's memory
estimate (200 torches > 48 MB per side) or need a frozen-API/layout change, STOP_FOR_OWNER with the numbers; otherwise name L2 as next.
