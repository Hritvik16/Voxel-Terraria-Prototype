# 32-uploaddirty-box-check

Approval: owner, 2026-10-08, Queue decisions Q23 part 2 (a): "`UploadDirty` inside-the-box
check (skip and count)" as a small fix after task 30. Queued by the supervisor 2026-10-08.

Branch `surface-rebuild`. Bug fix, NOT a performance task: no stream probe, rig or
frame-time arms (PREAMBLE rule 10). Fast EditMode suite.

## Why
KNOWN_ISSUES "STRAY RESIDENT CHUNKS ALIAS INTO THE MIRROR": `TerrainClipmap.UploadDirty`
(`Assets/CoreEngine/Mirror/TerrainClipmap.cs`, ~line 339) writes a resident chunk into its
toroidal slot without checking that the chunk is inside the mirror box. A stray chunk then
overwrites an in-box chunk ("lost") or fills a non-resident box position ("stale"). Task 26
(commit d4165c9) fixed the cause (deferred evictions); this is the defensive check.

## Do (one change)
- In `UploadDirty`, before a dirty chunk is written to its slot, test that it lies inside
  the CURRENT mirror box (same integer bitwise box math the clipmap already uses for its
  origin -- no float, no modulo). Outside: do NOT upload, drop it from the dirty set (it
  will be re-dirtied if it re-enters the box through the normal admission path -- verify
  that, and say how), and count it.
- Add the count to `UploadStats` (e.g. `skippedOutOfBox`) and print it wherever the other
  UploadStats fields are reported. Do not change any public signature (frozen APIs).
- EditMode test: a store with a chunk resident outside the mirror box, marked dirty; after
  `UploadDirty` the in-box slot it aliases is unchanged and `skippedOutOfBox == 1`. Plus a
  test that an in-box chunk still uploads exactly as before.
- KNOWN_ISSUES: update the STRAY RESIDENT CHUNKS entry ("STILL OPEN: UploadDirty has no
  in-box test" -> fixed, with the commit) and the CLAUDE.md index line.

## Acceptance
- Fast suite 0 FAIL, new tests green. `ClipmapValidator.CountStaleSlots`-based tests green.
- One `MODE=poses` run on v4 (pictures, not timing): `MIRROR 0/0/0` on every pose, shots
  read for holes or floating geometry, and the new skip count printed (expected 0).
