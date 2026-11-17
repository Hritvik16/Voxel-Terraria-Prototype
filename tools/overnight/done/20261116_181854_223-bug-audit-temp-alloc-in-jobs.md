# 223-bug-audit-temp-alloc-in-jobs

Model: opus

Approval: supervisor follow-up to task 218 (owner rule: efficient by construction, queue never idle). Owner-independent, no design change.

## Why
Task 218 found that `Allocator.Temp` inside an IJobParallelFor is not returned until the job ends, so a per-point gather piled up memory (jungle 10 GB -> 2.5 GB after the fix).
Other places may have the same pattern: the "allocation older than 4 frames" warnings from `TerrainGenChunkSource.Generate` and `TorchFloodBuild` were never checked.

## Do
1. Grep every `Allocator.Temp`, `new NativeArray`, `NativeList`, `stackalloc` and `UnsafeUtility.Malloc` used inside a Burst job's `Execute(int)` of an IJobParallelFor/IJobFor under Assets/ (TerrainGen, Mirror, Fluid, Lighting, Streaming). List each: file:line, job type, size per call, calls per job.
2. For each one that allocates per iteration (not per batch), estimate peak = size x iterations in flight, and mark OK or a pile-up risk. Plain table in `AUDIT_TEMP_ALLOC.md` at the repo root.
3. Fix only the exact ones whose peak is above 50 MB or that show the "older than 4 frames" warning, the same way as 218 (Malloc'd scratch per worker or per batch, freed in the job). Each fix must keep output bit-identical (a test or a hash before/after) and keep the memory ledger no higher (rule 11).
4. If none need a fix, say so. No frame timing. No frozen API or memory-layout change: if one would be needed, STOP_FOR_OWNER.
5. Fast suite must pass if code changed. Write HANDOFF.md with the table and the before/after memory numbers.
