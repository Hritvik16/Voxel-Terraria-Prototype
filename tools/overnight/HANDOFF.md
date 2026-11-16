# 218b-bug-gpu-view-eviction-proof: DONE -- refusals down (small), task 216's GPU view eviction is now DEFAULT ON

NOTE for the supervisor: `tools/overnight/PAUSE` exists. The owner (or `tools/heavy.sh pause`) created it at 10:43 when they paused heavy work
mid-task. The owner resumed `heavy.sh` at 11:37, but this PAUSE file is still there. I did not delete it: remove it to let the queue go on.

## What changed (commit 4a61ed7 + this HANDOFF commit)
- `Phase4Bootstrapper.cs`: `-gpuviewevict` default 1. `TerrainClipmap.ViewEvict.cs`: `ViewEvict = true`, header comment updated. `0` = 207's path.
- TASKS.md "Task 218b" paragraph (Task 216 heading now says default ON), KNOWN_ISSUES 208 entry (216 sub-bullet with the numbers), CLAUDE.md index line.
- Script `Logs/overnight/218b_pair.sh` = 216's pair without `-trees2 0` (218 fixed jungle_high). Logs are in `Logs/overnight/218b/` (git-ignored).

## Evidence: one build, b1 = ON, b2 = `-gpuviewevict 0`, today's defaults (trees ON), high preset, `-mirrorprobe 1`
| pose | refusals over run ON / OFF | refused at shot ON / OFF (in view+5x5) | eviction acted |
|---|---|---|---|
| jungle_high | 58 / 61 | 59 (22) / 61 (23) | 1 victim, 370 bodies |
| walk1 | 70 / 81 | 27 (6) / 29 (8) | 14 victims, 12 requesters mapped |
| walk2 | 70 / 81 | 0 / 0 | 17 victims (8,183 bodies), 13 mapped |
- Target "back to <= 857": both arms are far under it (208's 857 / 1,190 were a different run). The direction is DOWN and attributable (12-13 in-view
  chunks were mapped by the eviction). The effect is SMALL. Why: the chunks still refused at the shot sit on the box's far edge (x 443/458, z 384-386;
  Chebyshev 7-8 from camera chunk (451,392)). Almost no out-of-view chunk is farther than that, so by design no victim is found.
- Suites: fast PASS 1061 FAIL 0 SKIP 24 (`Logs/overnight/218b/fast.out`). WHOLE suite PASS 1142 FAIL 0 SKIP 34, wall 36m30s (`all.out`). The first
  SUITE=all try was cut off when the Unity test process got SIGTERM at 10:46, 3 min after the owner pause (`all_killed.out`, no failures). I reran it after the resume.
- Pictures (`Screenshots/218b/b1|b2/`): looked at jungle_high (both) and walk2. Dense jungle canopy, ponds, town and snow line are complete, with no holes
  or floaters in either arm. Pixel diff ON vs OFF: walk2 0 px, walk1 2,373 px (a crop of the distant left trees looks identical by eye),
  jungle_high 36 k px in the far top band (rows 0-453; snow peaks, nothing missing).
- Player logs: b1 printed 391 "allocation older than its permitted lifetime of 4 frames" warnings from `TerrainGenChunkSource.Generate`'s
  NativeArray Dispose (b2 189; 218's fix run 745). This was there before (timing-dependent) and is not this task's. Flagged; nothing queued.

## Memory (rule 11), ON vs OFF, ledger `[mem]` lines (no extra launch)
- Process peak 2,598 vs 2,510 MB (+88). This is not the eviction: the peak was already reached at the FIRST shot, when the eviction had acted once
  (1 victim). The code holds < 0.1 MB (one list <= 4,096 longs) and no GPU memory. 218's OFF-equivalent run peaked at 2,560 MB, so start-up load
  varies by about 50-90 MB between launches. GPU body pool full in both (peak 254,391 vs 254,394 of 254,400). CPU brick pool peak 496,264 vs 495,941.
  Valve evictions over the run 32,088 vs 35,204 (fewer ON).

## Left / next
- The far-edge refusals stay refused (hand-over draws them). Cutting them needs more GPU bodies or fewer bodies per chunk (byte-identical
  duplicates, 201a's "next lever"; chunk-border sealed bricks). That is an owner/perf-stage decision, not queued.

## For the doc
- GPU view eviction (task 216) is now ON by default. GPU refusals over the run, ON vs OFF: 81 -> 70 at walk1 and walk2, 61 -> 58 at jungle_high (`Logs/overnight/218b/look_b1|b2.out`).
- Chunks refused while in view at the shot: 23 -> 22 (jungle_high), 8 -> 6 (walk1), 0 -> 0 (walk2). The cut is small because most of those chunks sit at the
  far edge of the GPU box, where nothing out of view is farther away to give up its memory.
- Memory: process peak 2,598 vs 2,510 MB (launch variance, reached before the eviction had done anything). CPU pool and GPU pool are unchanged.
  Valve evictions went down, 35,204 -> 32,088.
- Tests: whole suite PASS 1142 FAIL 0; fast PASS 1061 FAIL 0.
- `Screenshots/218b/b1/ws_fixed_jungle_high_native_sf0.png`: jungle_high with eviction ON, the full jungle canopy to the snow line, ponds on the right, no holes.
- `Screenshots/218b/b2/ws_fixed_jungle_high_native_sf0.png`: the same pose OFF. It looks the same; only the far top band (snow peaks) differs, by a few pixels.
- `Screenshots/218b/b1/ws_fixed_walk2_native_sf0.png`: walk2 ON, the town top-left and two ponds right, identical to OFF (0 px differ).
