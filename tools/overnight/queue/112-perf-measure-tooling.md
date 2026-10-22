# 112-perf-measure-tooling

Model: opus

Approval: supervisor, 2026-10-22, "going ahead with (a)" (reversible tooling, task 111 Q9); no product-code behaviour change.
Not a performance task: no timing launches. Proves itself with tests/log lines (rule 10).

## Change (tooling only, from `PERF_30FPS_PLAN.md` Q9 and task 110's gap)
1. `PlaygroundCapture.CostSplit.cs`: `CostApply` hardcodes `UpscalerSelect.Apply(..., 540, ...)`. Honour `-renderrows N` (default
   stays 540 so every existing arm and log is unchanged).
2. The `MODE=walk` report (`-frametiming`): print p95 beside p50/p99 (the 30 fps line is "<= 5% of frames > 33 ms" = p95 <= 33).
3. A golden-hour walk arm: allow the walk circle with `-tod` set to a low sun (e.g. 17) through `run-tg2world.sh` flags, if not already
   possible; document the exact command in the HANDOFF.
Tests: an EditMode test for the percentile helper if one is added; otherwise a short batch check that the flags parse (log line).
Fast suite once at the end. No launch beyond ONE short (<= 3 min) check that `-renderrows 720` shows in the log; do not record frame times.
HANDOFF: what changed, the commands, one `## For the doc` line. Add a TASKS.md paragraph.
