# 168e-perf-e-upload-pacing: DONE, kept OFF behind `-uploadring 1`. The ring is exact, but the gain misses the ON gate: > 33 ms -2.9 points (gate 5), > 100 ms 6 -> 5 (gate: halve). p99 improves 12 ms.

This session resumed the 16:41 run, which hit the usage limit. That run's code and ABBA arms were already on disk; this session reviewed them and found and fixed 2 bugs (below).

## What changed (`ca79847`, engine-phase)
- `UploadRing.cs` (new). Every per-frame mirror write goes through it instead of `SetData`:
  - TerrainClipmap: entries, bodies, packed air-mip, fluid directory.
  - ShellStore: bricks, pages, directory, heights.
- How it works: each write is copied into a `LockBufferForWrite` staging slot. Each producer pass then flushes once, and `UploadScatter.compute` copies staging -> destination on the GPU. A slot is reused only after a 4-byte AsyncGPUReadback ticket shows the GPU has passed it.
- Upload cap: while the ring is on, the clipmap + shells per-frame budget is 1 MiB (`-uploadcap KB`).
- Checks: `UploadShadow.cs` (`-uploadcheck`) reads the GPU buffers back and compares them with a CPU record of every write. Capture `-costringpair` flips the ring, re-uploads the whole mirror, drains it and takes stills.
- Bug 1 fixed this session, chunks that never landed: under the 1 MiB cap, a dense far chunk costing more than (cap - 585 KB air-mip reserve) was deferred on every pass.
  - Effect while standing still: 119 chunks still dirty after 40 s, and 24,455 px of stale terrain at walk_open.
  - Fix in `TerrainClipmap.cs`: with the ring on, the first non-exempt chunk of each pass always uploads. Ring OFF is unchanged (the largest chunk always fits the 3 MiB budget).
- Bug 2 fixed this session, temporaries over the limit: one large body run made a single 68 MB temporary, past `TEMP_LIMIT` (32 MiB).
  - Fix: writes now go in slot-sized pieces, so temporaries stay under the limit.
- Docs: `TASKS.md` (task 168e paragraph) and the `PERF_EXACT_CUTS.md` row E result line.

## Evidence
- Tests: `UploadRingTests` PASS 3 (`Logs/overnight/168e_filter2.out`).
  - A CPU "GPU" runs dispatches 0-4 frames late; drained == sequential SetData byte for byte over 12 seeds.
  - The negative control (guards ignored) corrupts, as it should.
  - New assert: no temporary is larger than one slot.
- Fast suite: PASS 997 FAIL 0 SKIP 16 (`Logs/overnight/168e_fast.out`; was 994).
- In-player exactness, build with both fixes (`Logs/costsplit_2026-11-02_193709.txt`):
  - GPU vs CPU shadow: 0 of 57.1 M (walk_near) and 0 of 67.2 M (walk_open) written words differ, ring ON and OFF.
  - Raw stills: ring ON == ring OFF at 0 px at both poses; raw/raw2/flip/back all 0 px.
  - Whole-mirror re-upload drains fully: 10.3-10.9 s with the ring (1 MiB cap) vs 4.2-4.4 s with SetData (3 MiB).
  - The earlier build (`Logs/costsplit_2026-11-02_170804.txt`) had 620 / 24,455 px differing and 85 / 119 chunks never drained.
- Cost split walk_near p50: 35.4 (ring) vs 35.5 ms (off); walk_open 24.1 vs 24.2. No p50 change.
- Timing: warm steady state. Warm-up discarded (`Logs/tg2world_20261102_165835.txt`), then ABBA `MODE=playwalk` 3 min, 432 rows, `-spikelog`, on AC.
  - A = SetData, B = ring. Logs `Logs/tg2world_20261102_{171509 a1, 171955 b1, 172443 b2, 172929 a2}.txt`, summary `Logs/overnight/168e_measure.out`.
  - **The Mac was NOT quiet:** `mediaanalysisd` ran at 113-127 % CPU during b1, b2 and a2. `ReportCrashService` ran at 91 % (b1) and `duetexpertd` at 94 % (b2). a1 was the quietest arm, so the noise favoured A, not B.
  - These arms ran on the build BEFORE the 2 fixes. In play, dirty-at-end was 0 in both B arms, so bug 1 bit only while standing still. They were not re-timed: any result keeps the flag OFF.

| warm ABBA (whole run) | A SetData (a1 / a2) | B ring (b1 / b2) |
|---|---|---|
| > 33 ms share | 17.53 / 20.58 % (19.1) | 15.76 / 16.62 % (16.2) |
| p95 | 44.3 / 45.6 ms | 41.7 / 41.9 ms |
| p99 | 64.4 / 66.3 ms | 52.5 / 53.3 ms |
| > 100 ms frames (spikes.csv) | 2 / 4 | 2 / 3 |

- Ring memory: 4.5 MiB permanent (4 x 1 MiB staging + 4 x 128 KiB records).
  - Bursts (startup fill, whole-mirror re-upload) add temporaries up to 31.5 MiB, which are freed afterwards.
  - Up to 1.1 MiB can still be held at the end of a run.
  - The steady +5.6 MiB is inside the +6 MB gate. The burst peak is not: it matters only if the ring is turned ON.
- Still looked at: `walk_open_ring_on_raw.png` shows the normal hill view, terrain about 60 % of the frame, sea on the left, no holes.

## Top remaining source
In the spikes, 84-86 % of > 33 ms frames have no CPU section >= 5 ms, with ring ON as with SetData. So the driver's SetData wait was NOT the main cause of the tail. What remains is GPU-bound frames (walk_near p50 35.4 ms with gpu* ~75) and fluid tick pacing (168d: 24-27 %).

## Not done / next
- For the owner, if ever turning the ring ON:
  - Is +12 ms on p99 worth it when streamed terrain lands about 2.5x slower (mirror drain 4.3 -> 10.5 s at the 1 MiB cap)?
  - Is a 31.5 MiB burst peak acceptable? A `-uploadcap 2048` arm would trade some of the p99 gain for faster landing (not measured).
- Next in the order: the GPU-bound near view, or fluid tick pacing.

## For the doc
- > 33 ms share 19.1 -> 16.2 % (-2.9 points); p95 44.9 -> 41.8 ms; p99 65.3 -> 52.9 ms; > 100 ms frames 6 -> 5. Warm ABBA playwalk, Mac not quiet (`Logs/overnight/168e_measure.out`).
- Exact: 0 of 67.2 M written words differ (GPU readback), raw stills 0 px, after the progress-rule fix (`Logs/costsplit_2026-11-02_193709.txt`). Before the fix, 24,455 px stale at walk_open.
- Ring memory 4.5 MiB permanent, temporaries up to 31.5 MiB during bursts. Mirror drain 4.3 -> 10.5 s under the 1 MiB cap.
- Largest remaining source: GPU-bound frames (85 % of > 33 ms frames have no CPU section >= 5 ms), then fluid tick pacing.
- `Screenshots/168e/walk_open_stale_vs_fixed.png`: three crops of the far hill at walk_open (right side of the frame).
  - Left: ring ON before the fix, with extra orange specks on the hill from stale chunks.
  - Middle: ring OFF.
  - Right: ring ON after the fix, identical to the middle.
- `Screenshots/168e/walk_open_before_fix_diff_x4.png`: the before-fix difference (x4). Look at the bright patch at the right edge, mid-height: that is the only area that differed.
