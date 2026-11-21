# 236 plan -- night torch scene peaks ~6.0 GB (tf_on), day 2.57 GB

Facts before starting: every launch that built the flood (tf_on, task 221/229/230) peaks 6,025-6,074 MB; every torch-scene
launch without it 2,470-2,558 MB. The flood's own accounting (229 Player.log) is union 12.9 + caches 8.7 + scratch 26.1 MB:
the +3.5 GB is NOT in anything TorchFloodPool counts. The arm also switches the kernel ("kernel lean" vs "diet2").

## Step 1 -- where (one launch, night scene, arm tf_on only, no timing)
Diagnostic marks (off by default, `-tfmemmarks`): phys_footprint now/peak + Unity allocated / reserved / gfx driver at
Rebuild's start, after ResetAll, after each Flush step, after Flush, end; then every frame for ~120 frames after tf_on.
- Peak jumps INSIDE Rebuild/Flush, Unity "allocated" moves with it -> a native (NativeArray/Temp) pile-up in the sync path:
  find the step, fix it (bounded scratch / dispose), proven by the same marks.
- Jumps inside Rebuild, Unity counters flat -> unaccounted native/Burst/Temp (main-thread Temp across one 6 s frame):
  split Flush per job type (flood / collect / merge / apply) with an arm switch, fix the one that moves.
- Jumps in the FRAMES AFTER, gfx driver or nothing Unity sees moves -> the GPU side (flood kernel / its first-dispatch
  Metal compile / buffers): isolate with a capture arm drawing the old kernel with the pool built; fix or STOP_FOR_OWNER.
## Step 2 -- play (same launch if possible): a capture arm `tf_tick` = flood ON, pool reset, filled ONLY by play's
  per-frame Tick (LightingDriver), 28 torches registered at once (= placing torches in play). Marks per frame.
  Spikes -> play is affected (say so, fix covers both). No spike -> capture-only, say so.
## Step 3 -- fix the cause found, switch to the old path if not trivial. Step 4 -- the same launch again: peak before/after,
  refusals, flood result byte-compare (union hash) old vs new path.

## RESULT (2026-11-17)
Step 1: the jump is inside Rebuild, in the flush steps' "floods finished" interval; Unity counters flat -> not native.
`footprint` samples: IOAccelerator (Metal). Cause: one SetData per dirty run; ~4 MB of GPU memory per call (upload0 log:
239 calls -> +949 MB, 230 -> +900), held to the frame end; 903 calls in the rebuild's one frame. Step 2: play's Tick fill
spikes too (3,445 / 3,603 MB). Step 3: -tfupload 1 (one span per buffer per Upload; Flush uploads once). Step 4: night peak
5,985 -> 2,538 MB; fill 3,445 -> 2,422 MB; union hash 0B365D765B03507C both; GPU == CPU both; refusals 0 both.
