STOP_FOR_OWNER -- task 165 (torch flood L3, incremental): EXACT everywhere, but memory is over the ~20 MB line and the flood is far slower than the plan's estimate in tg2. Two questions.

**Q1 (memory, 158's 31-torch camp).** The CPU now keeps the 11.2 MB union you approved PLUS one cache per torch (8.1 MB). Exact removal needs the caches: a max over torches can't be undone without the other torches' values. Today, content is 19.3 MB, but allocated is 31 MB (list/buffer doubling). The 2 flood jobs also need up to 27 MB of scratch while they run (freed when idle). The GPU buffers are 20 MB allocated (11.2 used).
(a) Recommended: keep the caches. Trim the slack (CPU about 20 MB, GPU about 12 MB) and run 1 flood job (scratch peak about 14 MB, fills slower).
(b) Drop the CPU copy of the union data: the GPU writes merged bricks through a small scatter kernel. CPU about 11 MB, but more GPU code.
(c) Accept today's numbers.

**Q2 (flood cost in real terrain).** One outdoor torch takes 250 ms of worker time in tg2 (158: 316 ms; plan est. 5-15 ms). In EditMode open ground it is 62 ms (oracle 215).
Only 31% of tg2's lit bricks pass the "direct" test (all-air bbox to the torch); bumpy ground blocks the rest. So an edit in the camp settles in 2.1-2.6 s, not "a few frames". Each camp edit re-floods 10-11 torches, because the torches' 12 m boxes overlap.
(a) Recommended: a follow-up with two exact cuts. First, "anchored" bricks: the air below a torch counts as direct through the first move beside the post. The lower-bound check is cheap. Second, re-flood only the torches whose LIT cells the edit touches, not their whole box. Then measure again.
(b) A shorter reach (8 m is about 1/3 of the volume).
(c) Accept 2-3 s edit latency in dense camps.

# 165-torch-flood-l3-edits: DONE in code (exact, tests green); stopped on memory / cost

## What changed (`cb75562`, engine-phase)
- `TorchFloodBuild.cs` (new). It has the per-torch `TorchFloodCache` (min path cost per 0.2 m cell, per escaped voxel and per 0.4 m air cell; a direct brick is 1 bit), a main-thread `Snapshot` (brick kinds plus 1 bit per voxel of mixed bricks), and four Burst jobs: `FloodBrickJob` (worker, one torch), `MergeJob` (worker), `ApplyJob` (main), `CollectJob`/`TakeJob`.
- `TorchFloodPool.cs` was rewritten around these. Its Tick handles the torch-set diff, edit and chunk-admit/evict queues, up to 2 flood slots, merge batches of 4,096 bricks, dirty-range uploads, `Flush`, `Diff` and `VerifyGpu`. `SampleLevels` and the other twins are unchanged.
- `Encode` now takes an air cell as the MAX of its 2^3 centre voxels (158 used the mean, which does not decompose over torches; at most one corner move brighter). It reads a brick's kind from its content, not the header (164b's caveat).
- Hooks: EditService notifies only on an air<->solid change. `FluidOpListReadback` notifies on air<->fluid (so fluid writes ARE included). The edit-op replay uses `NotifyBox`. `Phase4Bootstrapper` calls `Shutdown`.
- Capture: `-costpaths torch -tfl3 1` (`PlaygroundCapture.TorchFloodL3.cs`). TorchCost now uses `EnsureBuilt`. FloodProbe is twin-only when there is no oracle.
- Flood stays OFF; the `-tfdir` default is still 0 (164's question is open).

## Evidence
- Fast suite: PASS 981 FAIL 0 SKIP 16 (`Logs/overnight/165_fast.out`; was 973/15). `TorchFloodL3Tests` all pass, including Slow `Fuzz_Long` (2 x 300 ops) in a filtered run (`Logs/overnight/165_filter.log`).
- Scratch == `Encode(oracle)` byte for byte in five cases: axis wall, x==z diagonal (450 escapes), closed box + L-bend, 12 m open ground, doorway.
- Incremental == scratch after every fuzz op (680 in total), the doorway sealed / diagonal slot / reopened, evict / re-admit, and removing one of 6 overlapping torches (0 other re-floods). Removing every torch frees every brick, escape and table.
- In-player run, warm chip (the build had just run; report-only, no frame-time arm): `Logs/costsplit_2026-11-02_015047.txt` (instrumented) and `_014115.txt`. Both are summarised in `Screenshots/165/tfl3_165.txt`.
  - Steps: camp build, D1 wall, D2 pit, D3a torch broken, D3b torch re-placed.
  - At every step, live union == fresh rebuild: IDENTICAL. GPU readback of dir / tables / every reachable data word == CPU copy: PASS. GPU vs twin over 25 probe pixels (24 lit): 0 levels.
- Camp: 31 torches; 162 chunks; 133,850 air / 18,794 mixed bricks; 14 escapes (same counts as 158).
  - Async fill: 5.85 s (run 1: 13.7 s); flood job wall mean 335 ms.
  - From-scratch, one thread: 7.9 s (158: 9.8 s); per torch mean 250 ms, max 321.
- Main thread per busy frame:
  - Build: mean 0.65, p50 0.47, p99 2.78, max 4.9 ms.
  - Edits: mean 0.59-0.78, p99 1.26-2.68, max 1.3-4.6 ms.
  - Spikes come from merge apply (max 2.8 ms per 4,096-brick batch) and single snapshots (max 2.9 ms).

## Unfinished / next
- After Q1/Q2: trim slack. Use smaller merge batches (1,024) and budget snapshots by box size, to get p99 under 1 ms. Then do Q2's cuts.
- 164's direction-term question is still open.
- L3's look check (dig through / seal a doorway sequence, window-edge walk) was not shot. The camp stills don't show the D1 wall.

## For the doc
- Exactness: live incremental union == from-scratch rebuild at all 5 in-player steps and after 680 fuzz ops. GPU buffers == CPU copy (readback) at every step.
- Rebuild of 158's 31 torches: 9.8 s main thread -> 5.9 s async on 2 workers. Main thread mean 0.65 ms/frame (p99 2.8, max 4.9). Per torch 316 -> 250 ms of worker time (`Screenshots/165/tfl3_165.txt`).
- Edit latency in the camp: 2.1-2.6 s, 10-11 torch re-floods per edit, 8-12 MB uploaded per edit.
- Memory: content 11.2 MB union + 8.1 MB caches. Allocated 21.1 + 9.9 MB. Scratch peak 27 MB while flooding. GPU 20 MB allocated / 11.2 used.
- `Screenshots/165/tfl3_camp.png`: the camp at night after the async fill. Look at the lower left and centre: warm pools of light around the 1.5 m posts on the slope. The look is the same as 158's flood (no visual change intended).
