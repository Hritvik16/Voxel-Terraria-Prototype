Owner: approve a fix. Also choose a prefetch margin or detail radius at speed: the latency line cannot be met by any engine speed.

# 16b-streaming-requirement-derived: DONE. Worst case = 2,285 chunks/s with a 0 s latency budget; today's capacity is 7-16 /s (141-334x short); nothing implemented

Still waiting on the owner from earlier: 15 (B1, accept v4's gen cost or A/B?), which blocks 17 (B2).

## What changed (surface-rebuild: c8807c2, 0723fc8)
- `Assets/CoreEngine/Streaming/StreamingRequirement.cs`: pure function, no runtime effect. It computes A_max, the bursts, L_min, admits per frame and the cold fill from the live constants.
- `Assets/CoreEngine/Tests/StreamingRequirementTests.cs` (5 tests): pin it on hand-worked cases, plus the live inputs.
- `PlaygroundCapture.StreamProbe.cs`: new report section 7, `REQUIRED x/s, CAPACITY y/s -> MET / NOT MET (short by fx)` at gen p50 and p99, plus latency, main-thread, bursts and the sustained speed. `run-tg2world.sh`: comment only. `tools/streamprobe-table.py`: parses the section-7 lines.
- `KNOWN_ISSUES.md`: new entry "STREAMING REQUIREMENT", with the worst case, formulas, constants (file:line), requirement table and gap. `CLAUDE.md`: one index line.
- Queued `20-worker-contention-probe.md` (evidence only, for fix 1).

## The worst case, and the key finding
- The worst case is 60 m/s (the brief; terminal fall is 55 m/s), heading in a diagonal dive about 65 deg down a mountainside. A Y layer adds 441 chunks, against 147 for an X/Z plane. Every chunk is full cost.
  - A_max = 2,285 /s: 974 level diagonal plus the dive. The brief's ~700 is the level, axis-aligned case.
  - That is 38 admits and 38 evictions per frame.
  - Bursts: 147 per plane, 287 at a corner, 882 per Y recentre.
- **The latency budget is 0.** Voxels hand over to the far band at the residency edge (`FarStartT`), so R_d = the load radius. A chunk is requested in the frame its near face reaches r x E = 128 m, which is already R_d.
  - There is no request-side hysteresis in X/Z. The task assumed one; the code has none.
  - So no throughput meets the line, even at a walk.
- Cold start or teleport: 3,087 chunks, 1.35 s at A_max. A 2 s target is PROPOSED, not set.

## Evidence
- Fast EditMode: **PASS 611 FAIL 0 SKIP 12** (`Logs/overnight/16b_editmode.out`); all 5 new tests pass.
- Existing data first: task 10 + B1, 21 arms, per-chunk TSVs. Capacity = 7 x 1000 / (gen + downsample), non-uniform chunks:
  - fly 56: 7.1 /s at p50, 2.2 /s at p99. Walk slow mode v3: 16.0 / 6.1. v4: 15.2 / 6.9. Walk fast mode (v3 only): 29.0 / 9.3.
  - Ideal (isolated 58 ms x 7 threads): ~121 /s, still 19x short.
- 2 confirming launches (walk, AC power, no owner-pause NOTE, one build):
  - v3 `Logs/tg2world_20261003_201553.txt`: CAPACITY 16.2 /s, NOT MET (141x); p99 7.5 /s (303x). Latency p99 34.1 s against 0. Main thread 41.0 ms against 16.7.
  - v4 `Logs/tg2world_20261003_202723.txt`: 13.9 /s (164x), p99 6.8 /s (334x), 35.4 s, 37.8 ms. Peak footprint 3175 / 3155 MB (over the 3 GB cap, as known).
- Other lines, from existing data:
  - Admission cap 16/frame vs 38 needed. Clipmap 16 chunks/frame vs 38. About 9.9 MB/frame vs the 3 MB cap (estimate).
  - Eviction about 0.12 / 0.52 ms per chunk (estimate): 4.5 / 20 ms per frame.
- No screenshots: this task did not ask for any, so none were opened.

## Ranked fixes (none implemented)
1. **Worker contention.** A chunk costs 49-65 ms alone but 190-460 ms p50 (up to 3.2 s p99) on a worker, and the fast/slow mode is unexplained.
   - Gain up to 6x (slow walk) to 17x (fly): about 120 /s.
   - Task 20 measures this first (gen ms vs 1/2/4/7 workers). Suspects: oversubscription (7 workers + `SetMinThreads(ProcessorCount*2)` + Unity jobs on 4P+4E), memory bandwidth, allocation/GC.
2. **Coarse-first generation for the outer ring** (design; changes the chunk content contract). At 64-128 m the chunk is drawn at tier 2 (0.4 m), which is 1/64 of the voxels.
   - Gain: roughly an order of magnitude on most of the band.
   - This is the only lever on the list that plausibly closes the ~19x left after fix 1.
3. **Main thread and upload** (2-4x short): raise or derive `MAX_CHUNK_LOADS_PER_FRAME` and `MAX_CLIPMAP_CHUNKS_PER_FRAME` from A_max, and cut admit cost (~1 ms, most of it cascade writes).
   - Only matters after 1 and 2.
4. **Change the worst case (owner):**
   - (a) Detail radius at speed, e.g. 7 chunks at 60 m/s with requests still at 10: M = 38.4 m, L_min = 0.62 s, A_max unchanged.
   - (b) Request to 13, the most a 32-chunk window allows, with detail at 10: same 0.62 s, but A_max becomes 3,640 /s.
   - (c) Cap vertical flight speed: the dive is 57% of A_max. Level-only is 974 /s.
   - (d) Fewer band layers at speed.
   - (a) or (b) also needs the far hand-over decoupled from residency.
- Generation order is already nearest-first plus heading. It cannot fix a throughput gap.

## Left / notes
- The "slow worker mode" p99 used is from the walk arms. The worst observed (fly 56, p99 3,230 ms) is also tabled.
- The air discount is excluded by the worst case. Real terrain is 37-86% uniform (nearly free) chunks, so measured admission (20-53 /s) runs above non-uniform capacity.
- `Assets/Scenes/Playground.unity` (build side effect) and the queue files deleted by the runner are left as found.

## Next step
The owner picks a 4(a)-(d) worst-case lever and approves task 20's result → fix 1. After any change, `MODE=stream ./run-tg2world.sh` section 7 is the verdict.
