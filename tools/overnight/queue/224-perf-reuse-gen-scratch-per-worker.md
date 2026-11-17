# 224-perf-reuse-gen-scratch-per-worker

Model: opus

Approval: supervisor follow-up to task 223's efficiency note (owner rule 2026-11-14: efficient by construction; queue never idle). Owner-independent, no look change, no design change.

## Why
Task 223 moved the generator's scratch (Generate: mats/uniform/isUniform/stats ~2.06 MB; FillShellChunk ~0.46 MB) to Allocator.Persistent, but each chunk build still mallocs and frees it.
Per-worker REUSED scratch (allocated once per streaming worker thread, cleared per chunk) removes ~2 MB of malloc/free churn per chunk and keeps peak memory flat.

## Do
1. Read `AUDIT_TEMP_ALLOC.md` and task 223 in TASKS.md. Extend the per-worker scratch (`ColumnPass` / `ShellChunk` or the nearest existing per-worker holder) so Generate / GenerateShellChunkRaw / FillShellChunk reuse it. Single-writer rule: scratch owned by exactly one worker, never shared.
2. Output must stay BIT-IDENTICAL: `GenScratchHashTests.GeneratorOutput_Hash` must print the same hashes as 223's (near 69CE4AAEAE2F03BC, shells 7E504985E4B9FDDF). Clear-per-use must be exact (no stale data from the previous chunk): add an adversarial test that builds a dense chunk then a uniform/empty one on the same worker and compares against fresh scratch.
3. Memory (rule 11): one launch pair (jungle_high + walk1, as 223's `Logs/overnight/223_pair.sh`) before/after: process peak must not go up more than +50 MB, refusals not up, warnings still 0. Expect peak flat or slightly lower.
4. No frame timing. No frozen API or memory-layout change: if needed, STOP_FOR_OWNER. Fast suite must pass. HANDOFF.md with numbers and a "For the doc" section.
