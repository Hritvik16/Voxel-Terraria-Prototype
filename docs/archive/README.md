# docs/archive -- finished or one-off reports moved out of the repo root

Moved by task 154 (2026-10-30, owner 2026-10-25: "remove the bloat"). `git mv`, content
unchanged, so `git log --follow` keeps each file's history. Older docs, amendments, completion
records and code comments still cite these by bare filename; the file now lives here.
Live references in CLAUDE.md, TASKS.md, KNOWN_ISSUES.md, PLAYGROUND_GUIDE.md and
ENGINE_PHASE_PLAN.md were updated to `docs/archive/<name>.md`.

| File | What it was | Why archived |
|---|---|---|
| `GEMINI_HANDOFF.md` | Gemini's report on the lava/torch light branch (2026-10-09) | one-off external-agent handoff, reviewed in `tools/overnight/REVIEW_GEMINI*.md` |
| `GEMINI_TASK.md` | the task brief given to Gemini (2026-10-09) | one-off, finished |
| `HANDOFF_2026-09-24.md` | session handoff during Phase 7 (`phase7-lighting`) | superseded by `SESSION_HANDOFF.md`; Phase 7 closed |
| `MORNING_REPORT.md` | overnight report, 2026-09-03/04 | one-off report |
| `FPS_INVESTIGATION_RESULTS.md` | horizon FPS investigation, 2026-09-11/12 (windowed capture is worthless here) | finished investigation; findings live in KNOWN_ISSUES.md |
| `FLUID_PERFORMANCE_AB_RESULTS.md` | dense vs tiled fluid A/B, 2026-09-10 | finished results (Phase 6) |
| `FLUID_SCALE_ARCHITECTURE_RESULTS.md` | §7.2 sparse tiled active set results, 2026-09-11 | finished results (Phase 6) |
| `FLUID_TILE_CAP_RESULTS.md` | frozen-fluid artifact, tile-cap ladder, 2026-09-11 | finished results (Phase 6) |
| `FLUID_TICK_DIAGNOSIS.md` | why one fluid CA tick took 6-7 frames (decision 11), 2026-09-24 | answered: fixed-rate tick (task 91) and adaptive drain (task 97) |
| `ARCHITECTURE_COMPARISON.md` | three architectures and the cost of comparing them fairly, 2026-09-17 | one-off analysis |
| `UNITY_VERSION_RESEARCH_2026-09.md` | Unity version research and upgrade proposal (decision 12) | one-off research |
| `PHASE_7_READING_NOTES.md` | what each document constrained for Phase 7, 2026-09-12 | Phase 7 closed (`9b3ee50`) |
| `VISUAL_REVIEW_114.md` | visual look diagnosis (task 114, 2026-10-22) | diagnosis done; its items became tasks 119-125 (see TASKS.md) |
| `OPTIMIZATION_CANDIDATES.md` | ranked optimisation proposals, 2026-09-04 | superseded by `PERF_30FPS_PLAN.md` / `PERF_SHELL_MARCH_PLAN.md` |
