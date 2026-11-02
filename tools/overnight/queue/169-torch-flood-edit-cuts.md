# 169-torch-flood-edit-cuts

Model: opus

Approval: supervisor, 2026-11-02, "going ahead with (a)" for 165's Q2 (reversible engineering choice, exact cuts only). Doc Q106.
Read `tools/overnight/blocked/165-torch-flood-l3-edits.HANDOFF.md` first (code `cb75562`, branch engine-phase).

## Do
Two EXACT cuts to the torch flood's per-edit cost; the flood stays OFF by default and its output must not change:
1. "Anchored" bricks: air below a torch counts as direct through the first move beside the post (cheap lower-bound check), so more of tg2's
   lit bricks (31% today) take the direct path.
2. On an edit, re-flood only the torches whose LIT cells the edit touches, not every torch whose 12 m box contains it.
Do NOT touch memory layout / slack / job counts (165's Q1 is open with the owner).

## Acceptance
- Output identical: `TorchFloodL3Tests` incl. Slow `Fuzz_Long`, scratch == `Encode(oracle)` byte for byte in the five cases, incremental == scratch
  after every fuzz op, GPU vs twin 0 levels. Add a test that cut 2 skips a torch the edit cannot change and re-floods one it can (adversarial: an
  edit just outside a torch's lit cells but inside its box, and one that opens a new path).
- Fast suite once. Report-only in-player run as 165 did (`-costpaths torch -tfl3 1`): per-torch worker ms, re-floods per camp edit, settle time
  (165: 250 ms, 10-11 re-floods, 2.1-2.6 s). No frame-time arms.
- Handoff: before -> after for those numbers, and the proof it is exact.
