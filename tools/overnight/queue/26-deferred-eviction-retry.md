# 26-deferred-eviction-retry

Requires: 21-poses-mirror-lost-check OWNER-APPROVES-26
**NEEDS OWNER APPROVAL** (it changes streaming eviction, §4.5). The runner holds this
task until `OWNER-APPROVES-26` is deleted from the Requires line (no such task exists).

Fix the MIRROR lost/stale counts at the first poses of a `MAPV=4` poses run
(KNOWN_ISSUES "SAVE-CAPPED EVICTIONS ARE NEVER RETRIED", found by task 21).

## The bug (task 21: deterministic, inferred from code + counts, not instrumented)
`StreamManager.EvictOutOfRange` runs ONLY on a frame where the camera chunk changes
(`allowSlide && cameraMoved`). It evicts at most `MAX_CHUNK_SAVES_PER_FRAME` (4)
`deltaDirty` chunks per call. Every other out-of-range chunk that needs a save is
skipped (`continue`), and nothing retries it until the camera crosses another chunk
boundary. A poses run teleports and then holds still, so the skipped chunks STAY
RESIDENT outside the store band and the mirror box. `TerrainClipmap.UploadDirty` has
no "inside the box" test for resident chunks, so their uploads land in the toroidal
slot of a chunk position INSIDE the box:
- if a resident chunk owns that slot, it reads as "lost";
- if no resident chunk owns it, it reads as "stale".

Evidence: map v4, extra residents above the 21x21x7 = 3087 box were 9 -> 5 -> 1 -> 0
over the first four poses, i.e. 4 evicted per teleport. Repeated in 3 of 3 v4 runs.
Map v3 never exceeded 3087 and had 0/0/0.

## Do
- In `StreamManager.Update`, remember when `EvictOutOfRange` deferred any chunk
  (a bool set where the save cap `continue`s). On later frames, call
  `EvictOutOfRange(_lastCameraChunk)` while that is set, even if the camera did not
  move. Keep the 4-saves-per-frame cap.
- Do not change `TerrainClipmap`'s upload/eviction-clear rule in this task. A
  defensive box test in `UploadDirty` is a separate change.
- Measure: fast EditMode suite; then `MAPV=4 MODE=poses ./run-tg2world.sh` x2
  (`BUILD=1` on the first). Expect MIRROR 0/0/0 on every pose and resident <= 3087.
  `./run-acceptance-rig.sh` once (eviction path), compared with its previous report.
