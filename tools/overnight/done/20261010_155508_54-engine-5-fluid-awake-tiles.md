# 54-engine-5-fluid-awake-tiles

Approval: owner, 2026-10-09 -- engine plan approved (Q36), Q6 (a); ENGINE PLAN STEP 5 (`ENGINE_PHASE_PLAN.md`
section 3 row 5; BUGS.md #3 "coast fills the fluid pool"). Queued by the supervisor 2026-10-10. Branch
`engine-phase`. Requires: 53-engine-6-cascade-caps (one memory change at a time).

## Goal (plan row 5)
Fluid tiles only for AWAKE fluid. Resting sea, lakes and supported sand cost nothing until disturbed (they are
plain terrain until something wakes them). The wake queue no longer drops a placed box's wakes.
Task 49's evidence (HANDOFF in git `b708f92`): the 1,024-tile pool (32^3 cells, 512 MB) is FULL in every run;
at the coast the residency radius wants 4,667 tiles within 128 m; refused tiles since start: poses 1.03 M,
walk 0.99 M, coast 3.35 M; no tile in any band was all-mobile. Fluid slot high-water at the coast 427k-500k.

## Contract check FIRST (plan section 4, amendment row 5)
Read the plan's section 4 row 5 ("Fluid tick contract ... The 7.2 tiled active set holds awake fluid only") and
Phase 5 decision 11. If doing this needs a change to a frozen contract (the §4.4 state-transition table, the D.1
delta format, a public API, a struct layout in Appendix A/B, or decision 11's tick), STOP_FOR_OWNER before any
code: write the amendment draft (what changes, why, what else it touches) in HANDOFF. If it fits inside the
current contracts (bookkeeping of which tiles are active), go ahead and say in HANDOFF why no contract changes.

## Acceptance (plan row 5)
- `MODE=poolprobe` at the coast: 0 refused tiles; ledger before -> after (fluid pool in use / peak, MB).
- Placed water flows (a placed box of water spreads and settles): screenshots before/after, midday light.
- `run-fluid-tiled.sh` passes (wrap with `tools/with-unity-lock.sh` if it does not take the lock itself).
- Fast EditMode suite green; `SUITE=all` too if any fluid test is in the Slow category.
- Size the pool from the measured awake peak x 1.25 only if the code makes that a one-line change; otherwise
  name it as the follow-up. No timing (PREAMBLE rule 10).

`## For the doc`: refused tiles before -> after, pool in use/peak before -> after, MB, phys_footprint per run,
1-2 images (coast with the sea at rest; placed water flowing) with captions.
