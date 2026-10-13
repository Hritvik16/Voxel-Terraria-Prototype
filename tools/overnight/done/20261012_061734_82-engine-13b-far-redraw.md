# 82-engine-13b-far-redraw

Approval: `ENGINE_PHASE_PLAN.md` (owner, Q36, 2026-10-09) step 13 part b (E5: "redrawn in the shells and far band at
once"); Amendment 8.14 adopted 2026-10-11 (rows 9, 12). Queued by the supervisor 2026-10-12 after task 81 (13a).
Branch `engine-phase`. Does not depend on the owner's Q44 (77's shell pool caps): keep the caps; a refused chunk is
not an error.

## Change: engine step 13b -- far edits redraw at once
Read task 81's `tools/overnight/HANDOFF.md` first (`IEditOps.Record`, `EditOpLog.Snapshot`, `EditOpApply.ApplyToShell`,
`GenerateShellChunk(..., EditOpSnapshot)`, `-farop`). Its leftovers are this task:
1. **Shells:** when `IEditOps.Record` lands an op, every STORED shell chunk the op touches (shells 1..N) is re-made
   with the op and re-uploaded through the normal budgeted upload (directory entry published last; 3 MiB/frame cap,
   row 14). Cheapest correct way first: apply the op to the stored chunk's bricks (ApplyToShell on a worker-owned copy)
   rather than regenerating, if that is byte-identical to regenerating with the op (test it). Chunks in flight get it
   on arrival (as 81's `CatchUpOps` does for 0.1 m).
2. **Far band:** the 2.5D far band (FarField) updates the columns an op touches (height / material, and water depth
   if carving under water), so a crater beyond the last shell -- or where a shell is absent -- is visible too.
   Main thread writes the CPU copy; the GPU texture is written FROM it (CPU sole writer). Say what the far band can
   and cannot show (it is 2.5D: an overhang or tunnel cannot appear; a crater or a mound can).
3. **Row 12, hand edits in shells:** a shell brick in a chunk that has a D.1 delta or a live (resident) edit is made
   by downsampling the resident 0.1 m chunk (majority vote, as today's cascades), so a big hand-dug pit also shows in
   shell 1-2 when walking away. If this does not fit, leave it as a named leftover.
4. 81's small leftover: the in-flight catch-up runs the fluid wake scan.
- Single writer (main thread mutates stores; workers write scratch they own); no locks in the streaming path; no
  frozen API / D.1 / §4.4 change; pow2 rings, bitwise integer coordinates.

## Acceptance
- EditMode: an op recorded AFTER shells 1-7 are stored -> every touched stored shell chunk equals a fresh
  `GenerateShellChunk` with the op (0 differ), uploads byte-exact, stale slots 0; far band columns under the op equal a
  fresh far-band build with the op; (3) if built: downsample == the cascade vote rule on a hand-edited chunk.
- Fast EditMode green; the suite once at the end (`SUITE=all`: generation path).
- `run-phase7-rig.sh` result unchanged and byte-identical (no ops in rigs). No frame-time measurement.
- **Images (one player launch):** `-farop` recorded AFTER the shells have filled (add a delay flag if needed), seen from
  ~360 m (a shell) and from ~3 km (far band), before/after side by side, plus the tint shot. Look at them.
- HANDOFF `## For the doc`: test numbers, frames from op to visible (shell and far band), the before/after sheet.
