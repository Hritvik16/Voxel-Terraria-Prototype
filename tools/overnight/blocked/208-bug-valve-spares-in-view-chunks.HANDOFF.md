STOP_FOR_OWNER -- rule 11 budget: GPU refusals went UP (857 -> 1,190) with the fix ON. The fix itself works and is committed, default ON.
Question (recommended: a):
  (a) Keep ON. Queue a GPU-side follow-up: when an in-view chunk is refused, the GPU body pool drops the bodies of out-of-view chunks
      in the mirror box first (the hand-over draws those). Same idea as this task, applied to the GPU pool. It needs your approval
      because it is new eviction design.
  (b) Keep ON and accept the higher refusals (refused chunks are drawn by 197's hand-over; the frames look clean).
  (c) Default back to `-valveview 0` until the GPU side is fixed.

# 208-bug-valve-spares-in-view-chunks: DONE (code), STOPPED on budget. The valve no longer evicts chunks the player can see: in-view victims 19,488 -> 0.
## What changed (commit be67746; no pool size, layout, shader or API change)
- `StreamManager.ApplyPoolPressureValve` with a frustum ranks victims in one scan + one sort per call. Order: out of view, then in view beyond
  the load radius, then in view within it (last resort, counted + `[valve] evicted an IN-VIEW NEAR chunk` log). Farthest (Chebyshev XZ) first;
  LRU then the coord break ties. Unchanged: pressure test, 32-victim guard per call, camera 5x5 exemption, EvictChunk (save before drop).
- Efficient by construction: no per-chunk memory (a reused scratch list of ~3 k x 24 B). The old valve scanned every resident chunk once per
  victim (up to 32 scans); the new one scans once. The frustum comes from `GeometryUtility.CalculateFrustumPlanes(Camera, Plane[])` into a
  cached array (no alloc), main thread only.
- Frustum: `Phase4Bootstrapper.LateUpdate` sets `SetViewFrustum` before `Streamer.Update`. With no frustum, the old LRU order runs.
  `-valveview 0` = the old order (launch-wide, `StreamManager.ValveViewOrder`).
- Counters `ValveInViewEvictionsTotal`, `ValveInViewNearEvictionsTotal` (counted in both orders) on `-mirrorprobe 1`'s `[mirror]` lines.
- 197's hand-over and 207's retry: untouched. Tests `ValveViewOrderTests` (6). TASKS.md paragraph, KNOWN_ISSUES entry, CLAUDE.md index line.
## Evidence
- Whole suite **PASS 1123 FAIL 0 SKIP 30** (`Logs/overnight/208/all.out`). Fast before = 207's run on identical code (PASS 1041 FAIL 0).
  Filter run 8/8 (`filter1.out`). Old order in the test world: 11 of 32 victims in view; new order: 0.
- Launch pair, one build (`Logs/overnight/208_pair.sh`): b1 = ON (`Logs/overnight/208/look_b1.out`), b2 = `-valveview 0` (`look_b2.out`).
  Poses: forest, jungle_high, walk1-3 (40 m steps from jungle_high along its view), town_eye; `-mirrorprobe 1`.

  | OFF -> ON | forest | jungle_high | walk1 | walk2 | walk3 | town_eye |
  |---|---|---|---|---|---|---|
  | in-view valve victims (running) | 3,777 -> 0 | 6,816 -> 0 | 10,444 -> 0 | 13,230 -> 0 | 16,315 -> 0 | 19,488 -> 0 |
  | ABSENT over solid | 5 -> 0 | 5 -> 7 | 9 -> 3 | 21 -> 3 | 12 -> 5 | 0 -> 0 |
  | hand-over columns | 66 -> 27 | 119 -> 66 | 95 -> 62 | 112 -> 50 | 65 -> 25 | 1 -> 0 |
  | GPU refused at the shot | 0 -> 61 | 0 -> 138 | 0 -> 90 | 0 -> 78 | 0 -> 31 | 0 -> 0 |
  | GPU pool in use | 217.6 k -> 254.4 k | 234.9 k -> 254.4 k | 251.4 k -> 254.4 k | 203.0 k -> 254.4 k | 224.6 k -> 254.4 k | 146 k both |

  Total valve evictions 83,149 -> 78,139. Still ~12 k per pose: the load set is bigger than the valve line, so evicted far chunks reload.
- Why refusals rose: the old valve dropped NEAR in-box chunks, and that freed GPU bodies. Now the victims are far out-of-view chunks, mostly
  outside the 16-chunk mirror box, so they hold no GPU bodies. The GPU pool stays full (201a: demand > cap), and refused chunks stay refused
  (`[mirror +5s]`: 24/24 still missing ON vs 1-5 OFF). They are drawn by the hand-over, and total hand-over columns are lower ON (230 vs 458).
- Shots (looked at): jungle_high OFF has a coarse shell block on the right (blue water face, grey rock face, blocky grass). ON is clean forest.
  walk2 has the same pattern (OFF: grey rock + water face at the right edge; ON: the lake shore is correct). 197's evicted-pit pose is
  jungle_high (`Screenshots/197/v/2_evicted_before.png`): ON shows forest there.
- Memory (rule 11), OFF -> ON: CPU pool peak 494,867 -> 495,648 (valve 484,500); GPU body pool peak 254,400 both, refusals 857 -> 1,190 (OVER
  budget); process "now" at matching poses +24-30 MB; process peak 2,688 -> 2,821 MB (+133, over +50; at the jungle_high transition;
  207 saw ~140 MB of launch noise with identical code. Unproven either way. The new code holds < 0.1 MB).
- Rough cost (rule 10, not a timing claim; settle frames, one launch each): forest 153 -> 152 ms, jungle_high 205 -> 236, walk1-3
  209/202/198 -> 215/209/195, town 134 -> 133. jungle_high +31 ms, as 207 saw +20 there; unverified.
## Not done / next
- GPU-side view priority (option a above), owner's call. The valve churn itself (load set > valve line) is the pool-size matter, unchanged.
- Launches ran ~25 min each (6 poses x lookwait 45 + shells), over the ~10 min guideline. The next proof should use fewer poses.

## For the doc
- Chunks the player can see, dropped by the CPU pool valve: 19,488 -> 0 over 6 poses (`Logs/overnight/208/look_b2.out` -> `look_b1.out`).
- Columns drawn by shell 1's coarse copy at the shot: 458 -> 230 in total. Non-resident chunks over solid ground: 52 -> 18.
- Cost found: the GPU body pool now stays full. Refusals 857 -> 1,190; 31-138 chunks wait at each shot (drawn by the hand-over). Owner question above.
- Whole suite PASS 1123 FAIL 0 (+6 tests `ValveViewOrderTests`).
- Images:
  - `Screenshots/208/b2/ws_fixed_jungle_high_native_sf0.png` (BEFORE, old valve): look at the right third. There is a coarse block with a
    vertical blue water face and a grey rock face: dropped in-view chunks drawn by shell 1.
  - `Screenshots/208/b1/ws_fixed_jungle_high_native_sf0.png` (AFTER): the same view is full forest to the right edge.
  - `Screenshots/208/b2/ws_fixed_walk2_native_sf0.png` vs `Screenshots/208/b1/ws_fixed_walk2_native_sf0.png`: right edge. Before has a grey
    cliff and a blocky water face; after shows the lake shore and trees with their shadows.
