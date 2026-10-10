# 44-engine-phase-plan-doc

Approval: owner, ROADMAP.md (2026-10-08) step 3: "Engine capability + performance phase. Starts
with ONE plan doc for the owner: targets, order, memory budget." Terrain phase closed 2026-10-09
(task 43 merged it to local main). Owner 2026-10-09: "get the main stuff out of the way."
Queued by the supervisor.

**PLANNING ONLY. NO code, NO Unity, NO builds, NO rigs.** This task WRITES A CLAUDE DOC (the
preamble's no-docs rule is lifted for this task only). Read files and logs; do not run anything
heavy. End with STOP_FOR_OWNER: the plan needs the owner's approval before any engine task.

## Read first (targeted reads, not whole files)
- `ROADMAP.md` -- the whole file: backlog E1-E10, the Notes, and "Tensions the step-3 plan must
  resolve" 1-6. The plan must answer every tension.
- `tools/overnight/BUGS.md` -- every Open row gets a home in the plan (batched next to related work).
- `KNOWN_ISSUES.md` -- the entries the backlog touches (streaming requirement task 16b, TAA,
  checkerboard, pool full, 3 GB cap, Burst vs Editor Mono).
- `tools/overnight/SUPERVISOR.md` "ROADMAP.md (owner, 2026-10-08, later)" bullet: the plan MUST
  include ONE compiled world-map generator path (Q26 (a): e.g. Burst in Editor and player, so
  EditMode map tests / PlayMetricsReport / review renders describe the world the IL2CPP player builds).
- Lighting input (owner 2026-10-09): what the owner liked in the SVO build is "how light reacts to
  its surroundings". `../VT_gemini/SVO_LIGHTING_NOTES.md` (branch `gemini/lava-torch-light`; its
  line numbers are WRONG, see its review header) -- main finding: the SVO traces shadow rays for the
  nearest torches, ours has no point-light occlusion (torch-through-wall, Phase 7 §10 flag); the SVO
  has emissive bleed on, ours off. That branch's lava fix (bd60a30) must be merged by the first
  engine task that builds. This goes into E9.
- The latest measured numbers on record (do not measure anything new): KNOWN_ISSUES, the last
  `Phase4Acceptance`/tg2world reports, tasks 16b/24/26/27/28/30 handoffs in `tools/overnight/done/`.
  Quote each number with its date and source; mark old ones as old.
- CLAUDE.md: hard invariants, frozen APIs, measurement discipline (warm steady state; measure only
  in performance tasks; one change per measured task).

## The doc (title "Engine phase plan", one Claude Doc, plain words, short)
1. **Targets** -- one table: E1-E10, today (number + date + source) vs wanted, and how each is
   proven (which rig / test, warm steady state). Flag targets that conflict.
2. **Memory budget** -- the 3 GB cap split across CPU window, GPU mirror/cascade, far band, fluid,
   new LOD shells, light grid; what must be reclaimed first (ROADMAP tension 2) with estimates
   marked as estimates.
3. **Order** -- numbered steps, ONE change per task, each with its acceptance, its rough size (S/M/L),
   and what it depends on. Put measurement tasks only where a decision depends on their result
   (CLAUDE.md: write what each result would make us do). Include: the one compiled generator path,
   the BUGS.md rows, the gemini lava merge, the fluid items E6-E8.
4. **Amendments needed** -- every spec/frozen-API/invariant change (8.9 LOD bounds, D.1 op log,
   CPU-sole-writer for coarse shells, ...): what changes and why; nothing here is approved yet.
5. **Questions for the owner** -- ONE batch, at most ~8, each multiple choice with the recommended
   option first and why, and what each answer costs. Must include: the design TOP SPEED (one
   number; only record on file is 60 m/s late-game flight -- do not ask about movement design),
   E1 internal resolution (540 / 720 / dynamic), the torch-through-wall / light-propagation choice
   for E9, and the order of the first 3 tasks.
6. **Usage estimate** -- rough number of worker tasks, so the owner can pace it on the $20 plan
   (never suggest paid options).

Pictures: only if an existing screenshot shows a target problem better than words (caption says
what to look at). Keep the doc readable in ~10 minutes.

## HANDOFF
STOP_FOR_OWNER: "Engine phase plan ready for approval" + the doc link + the question list (one line
each). Also leave a markdown copy of the doc as `ENGINE_PHASE_PLAN.md` in the repo root (commit it)
so later task sessions, which cannot read claude.ai docs, can use it.
