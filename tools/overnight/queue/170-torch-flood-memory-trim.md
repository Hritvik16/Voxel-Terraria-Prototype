# 170-torch-flood-memory-trim

Model: opus

Approval: owner, 2026-11-02, doc Q106 Part 1 answer (a). Requires: 169 done (same files; avoid conflicts).
Read `tools/overnight/blocked/165-torch-flood-l3-edits.HANDOFF.md` ("Unfinished / next", Q1).

## Do
Keep the per-torch caches. Trim the list/buffer slack so CPU ends near 20 MB allocated (was 31) and GPU near 12 MB (was 20). Run ONE flood job
at a time (scratch peak about 14 MB, was 27). Also from 165's list: merge batches of 1,024 bricks and budget snapshots by box size, so the
main-thread p99 per busy frame drops under 1 ms (was 2.8). Output must stay byte-identical.

## Acceptance
- `TorchFloodL3Tests` incl. Slow `Fuzz_Long`, scratch == `Encode(oracle)`, incremental == scratch, GPU vs twin 0 levels, all still green. Fast suite once.
- Report-only in-player run as 165 (`-costpaths torch -tfl3 1`): CPU/GPU allocated MB, scratch peak, fill time for 31 torches, main-thread ms mean/p99/max.
- Handoff: before -> after table. No frame-time arms. Flood stays OFF by default.
