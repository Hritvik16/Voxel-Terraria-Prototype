# 128-plan-shell-march-gpu-cost

Model: opus

Approval: owner, 2026-10-23, Q80 (a) in the Queue decisions doc ("yes" to investigating the shell march): a PLAN-ONLY task first.
No product code, no shader change, no builds, no rig/measurement launches (rule 10 does not apply: nothing is measured). Output is a
document in the repo; the supervisor puts it in front of the owner for approval BEFORE any experiment or code change.

## Question
Shells cost +12-16 ms per frame on every preset (93b, 93b3-5, 117), yet three exact cuts did nothing: iterations -37..-64% (116), smaller
shells (117), fewer shadow rays (124); crossing frames are GPU-bound too (115, 123). So the cost is NOT the step count. Why is the shell
march slow on the M1 GPU (8-core, Metal, no SM6 wave intrinsics here, no Metal capture in this workflow)?

## Deliverable: `PERF_SHELL_MARCH_PLAN.md` (repo root, under ~150 lines)
1. What the evidence already says (cite KNOWN_ISSUES "SHELLS COST", the HANDOFFs in `tools/overnight/done/*93b*`, `*116*`, `*117*`, `*124*`):
   the numbers that rule hypotheses out. Read `ShellMarch` and the primary march in `Raymarch.compute` (grep; targeted reads).
2. Ranked hypotheses for the per-iteration cost, each with how it would show up and what already contradicts or supports it. Cover at least:
   SIMD divergence (neighbouring pixels exiting at different steps / different shells), occupancy / register pressure (the kernel's
   live state, the per-shell constants, `ShellMarch` inlined), memory traffic (cache lines per iteration, the brick / mask / palette
   reads, buffer layout, 64-bit loads, 93b4's finding that read caches saved only 0.6-2.4 ms), ALU vs bandwidth bound, the per-pixel
   work AFTER the hit (shading, shadows) charged to the shells, thread-group shape / dispatch size / ray-order (Morton / tile order).
3. For each, ONE cheap discriminating experiment: what to change or disable in a probe build, what a result of A vs B would mean, and
   what we would do in each case (if every outcome leads to the same action, say so and drop it). Estimate cost (builds, minutes) and
   the best-case gain in ms. Prefer experiments that need no new infrastructure (existing `-costarms`, `-shellmask`, `-shells`).
4. Candidate restructures (e.g. tile-coherent ray ordering, splitting the march into passes by shell, lower-res shell march with
   upsample, compacting active rays, caching the shell hit per tile across frames), each with the gameplay consequence (far edits stay
   3D? Q66; simulation reach, amendment 8.15), frozen-API / layout impact, and the design questions the owner would have to answer.
5. A recommended order of at most 3 experiments, and the question to ask the owner (specific, multiple choice).
Do not start any of them. Say plainly what you could not determine without a GPU capture.

## Finish
HANDOFF (`STOP_FOR_OWNER`: approve experiment order?) with `## For the doc`: the ranked hypotheses table and the recommended order.
Commit the plan file. TASKS.md paragraph (one line). No suite run needed (no code).
