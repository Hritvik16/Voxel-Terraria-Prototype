# 236-bug-night-torch-memory-peak-6gb: FIXED. Night torch scene peak 5,985 -> 2,538 MB; play's torch fill spiked too (3,445 -> 2,422 MB).

## Cause (proven, not inferred)
Not the flood's CPU memory: its own accounting is ~48 MB, Unity's "allocated" stayed flat, and an EditMode flood of 3.8 M voxels adds only +16-29 MB.
macOS `footprint` samples during the fill: the swing was **IOAccelerator (Metal)**; "Untagged" (native heap) stayed flat.
`TorchFloodPool.Upload` issued ONE `GraphicsBuffer.SetData` per dirty data run, up to 239 calls per upload. On Metal **each call cost ~4 MB of
GPU memory** (about the whole data buffer, whatever it wrote): 239 calls -> +949 MB, 230 -> +900 MB. That memory was held until the GPU finished the frame.
The capture's synchronous `Rebuild` = 14 merges' uploads (903 calls) in ONE frame = +3.5 GB.
**Play spikes too**: the per-frame `Tick` fill (28 torches at once, capture arm `tf_tick` = play's path) did up to 81 calls per frame. Footprint
3,445 MB (launch 1: 3,603), over the 3 GB limit. So placing several torches in play crossed it.

## What changed (commits c140a9a code+docs, 541ee9c evidence)
- `TorchFloodPool.cs`: `-tfupload 1|0` (default 1; 0 = before 236). Mode 1 = ONE SetData per buffer per Upload over the dirty span (`DirtySpan`; the
  words in between already equal the CPU copy), and the synchronous Flush uploads once at its end. Counters `UploadCalls/UploadCallsMax`, `UnionHash()`,
  `MemMark` hook (null = off). No API/layout change; the CPU union is untouched.
- `MemWatch.cs`: `-tfmemmarks` diagnostic (`[tfmem]` line per frame whose footprint moved >= 32 MB, plus marks in Rebuild/Flush/Upload).
- `PlaygroundCapture.TorchCost.cs`: capture arm `tf_tick` (flood ON from an empty pool, filled only by play's Tick).
- `TorchFloodMemoryTests.cs` (new): DirtySpan = one span, clears, ignores stale bits past the length; one flood's CPU transient < 64 MB.
- KNOWN_ISSUES entry, PERF_BUDGET_GUARD's 6 GB note -> FIXED, TASKS.md paragraph.

## Evidence (plan `Logs/overnight/236/PLAN.md`; same build, back to back, `-costpaths torch -costarms tf_on,tf_tick -tfmemmarks`)
| | `-tfupload 0` (old) | `-tfupload 1` (new) |
|---|---|---|
| process peak (night launch) | 5,984.9 MB | **2,538.1 MB** |
| rebuild: SetData calls / most per upload | 903 / 239 | 3 / 3 |
| play fill (tf_tick): max footprint | 3,445 MB | 2,422 MB |
| play fill: calls / most per frame / bytes | 1,607 / 81 / 33.7 MB | 158 / 3 / 105.5 MB |
| IOAccelerator max (footprint samples) | 4,657 MB | 1,156 MB |
| CPU union hash after rebuild | 0B365D765B03507C | 0B365D765B03507C |
| GPU == CPU (VerifyGpu), rebuild and both fills | yes | yes |
| GPU body pool peak / refused; CPU pool peak | 103,611 / 0; 225,775 | 103,611 / 0; 225,775 |
Reports: `Logs/overnight/236/costsplit_upload0_old.txt`, `costsplit_upload1_new.txt`; marks: `up0_tfmem.txt`, `up1_tfmem.txt`; categories: `fp_up0.txt`, `fp_up1.txt`, `fp_tick.txt`.
Day level (task 230) 2,568 MB; the night is now below it.
Stills: cross-launch byte compare is meaningless (even the flood-OFF `full` arm differs: real-time yaw sweep, FSR history), so identity = union hash + VerifyGpu.
Looked at `Screenshots/236/tf_on_old_vs_new.png`: the same torch-lit slope and posts in both halves, with a slight yaw offset.
Tests: own filter PASS 2. Fast suite **PASS 1107 FAIL 0 SKIP 25** (`Logs/overnight/236/fast_suite.out`).
Rule 11/12: no pass added, GPU buffer sizes unchanged. Upload bytes rise (34 -> 105 MB per 28-torch fill, ≤ 1.7 MB per merge frame: a memcpy).
No frame timing taken (rule 10). Not HEAVY.

## Left / notes
- Parked task 170's patch (`tools/overnight/later/170-*.patch`) edits `Upload` (GrowGpu, partial-run uploads): it now conflicts and must be rebased over 236.
  Its per-run upload idea is exactly what cost memory here.
- Capture caveat (not fixed, out of scope): with `-costarms tf_tick,tf_on`, tf_on REUSES tf_tick's pool and does not Rebuild (`_costBaseFlood` is
  read after the torch scene turns the flood off). Put tf_on first. Noted in KNOWN_ISSUES.
- Next: the supervisor's clean `run-budget-check.sh` (needs a new build; idle Mac) can now pass the 3,000 MB hard limit and be `--rebaseline`d.
- Lesson for other GPU buffers (fluid, shells, mirror): count SetData calls per frame, not bytes.

## For the doc
- Night torch scene memory peak: **5,985 -> 2,538 MB** (limit 3,000; day 2,568). `Logs/overnight/236/costsplit_upload{0_old,1_new}.txt`
- Play was affected too: placing 28 torches peaked at **3,445 -> 2,422 MB** while the flood filled (`up0_tfmem.txt` / `up1_tfmem.txt`)
- Cause: 903 tiny GPU uploads in one frame, ~4 MB of Metal memory each. Now 3 uploads (one per buffer).
- Same light: CPU flood hash identical, GPU copy verified equal. GPU pool refusals 0 -> 0.
- Image: `Screenshots/236/tf_on_old_vs_new.png`. Left = old upload, right = new, the night torch scene with the flood ON; the lighting is the same
  (posts and lit slope match; a small yaw offset comes from the real-time sweep).
