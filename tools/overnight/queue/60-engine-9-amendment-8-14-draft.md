# 60-engine-9-amendment-8-14-draft

Approval: owner, 2026-10-09 (engine plan Q36 "a": ENGINE PLAN STEP 9, planning only, STOP_FOR_OWNER). Order
confirmed by the owner's Q42 answer 2026-10-10 (step 8 = roof skip, task 59; step 9 follows). Queued by the
supervisor 2026-10-10. Branch `engine-phase`.
**PLANNING ONLY:** no code, no Unity, no builds, no rigs. You MAY read code and logs. Write ONE local file,
`AMENDMENT_8_14_LOD_SHELLS_DRAFT.md`, commit it, and end with `STOP_FOR_OWNER` at the top of HANDOFF (the
supervisor publishes it to the owner as a doc; you do not touch claude.ai).

## What the draft covers (ENGINE_PHASE_PLAN.md section 3 step 9 and section 4 rows 1-3; read both)
1. **LOD shells replace 8.9's three tiers (64/128/290 m) and 8.10's cascade tiers 1-2.** About 8 cube shells,
   each starting where its voxel is <= 1 px at the internal rows, vertically too. Ring sizes stay powers of two
   (CLAUDE.md invariant; §6.2 phantom terrain). Replaces 8.11 (draft, never adopted).
2. **CPU stays sole terrain writer** (row 2): shells are generated on the CPU with Burst and uploaded; GPU only
   stores them. Name the later GPU-shell option only as the step-16 fallback (owner Q2 (a)).
3. **Edit-op log beside D.1** (row 3): a new save section of operations, applied on generation; D.1's format
   stays frozen; precedence ops first, then D.1. Flag clearly that this sits next to a frozen API.
4. Also (plan step 9 text): the far hand-over no longer tied to residency (step 12), the 0.1 m window shrink to
   detail radius + margin (step 14), and fluid patches staying resident around awake fluid (row 4: check the
   frozen §4.4 state table; a new state = redesign, flag it).

## Inputs that bind the numbers
- Resolution: owner Q38 (a) chose FSR 3 DYNAMIC, 540-720 rows, 1080p output (task 52 made it play's default).
  Plan Q3 said shells sized for 720 cost ~1.78x memory and generation of 540. The draft must give the shell
  table for BOTH 540 and 720 (sizes, voxel size per shell, chunks, MB, upload/frame) and recommend one.
- Top speed: Q1 (a) 60 m/s; Q2 (a) when generation falls behind, show the next coarser shell.
- Memory: 3 GB process cap (M1 8 GB). Use tasks 53/55's ledger numbers as today's baseline.
- Frame time (task 57, `tools/overnight/done/57-*` HANDOFF or git `2c6e0bb`): GPU-bound; 49.4 / 30.4 ms
  (near wall / open), of which ~19 / ~8 ms is the main voxel march no single switch removes; tiers 1-2 cost
  0.3 / 2.9 ms. Estimate what shells do to march cost, step by step, and label every estimate "(est.)".
- Coarse-first generation (step 10) needs a solid/air agreement threshold set HERE.

## Shape of the file (the owner reads it; plain words, short sentences)
1. `## For the owner`: 10 lines max -- what changes, what it buys (frame time, view distance, memory), what it
   costs, what is frozen and stays frozen.
2. The shell table(s), the memory ledger before/after, the upload budget.
3. Contract changes, each marked NEW / CHANGED / UNCHANGED (frozen API) with the section it amends.
4. Risks and what is measured at step 16 / 23.
5. `## Questions for the owner`: 3-6 specific multiple-choice questions, recommended option first as (a) with
   one line of why and what each option costs. Include the 540-vs-720 shell sizing question.

## Acceptance
The file exists, committed; every number has a source (log path, ledger, file:line) or "(est.)"; HANDOFF starts
with `STOP_FOR_OWNER` and lists the questions; `## For the doc` = the For-the-owner block + the questions.
