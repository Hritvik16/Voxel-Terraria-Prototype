# 165-torch-flood-l3-edits

Model: opus

Approval: owner, 2026-11-01, doc Q101 parts 1 (a) and 2 (a). `TORCH_LIGHT_PLAN.md` step L3. Read 158's handoff first
(`tools/overnight/done/20261101_151000_158-torch-flood-l2-gpu.md` + `tools/overnight/blocked/158-torch-flood-l2-gpu.HANDOFF.md`).
Flood stays OFF by default (the look is decided by 164's question). Independent of 164's direction term (the sampler is not touched here).

## Do
- CPU copy = the GPU's compressed form (0.2 m mixed cells + escape, 0.4 m air bricks), not per-voxel. A torch place/remove or a terrain edit
  (EditService write, fluid writes included if cheap; else note them) re-floods only the torches whose 12 m box it touches; result must equal a
  from-scratch rebuild EXACTLY (test: random edits vs full rebuild, adversarial: sealed rooms, 0.1 m diagonals, a wall closing a doorway).
- Flood cost: pure-air bricks flooded as a unit + a sparse scratch (157's est. ~10x fewer steps), on a worker thread, one torch per job; the
  main thread only uploads (single-writer rule: the worker writes only its own scratch). Lights stream in over a few frames.
- Report: rebuild time for 158's 31 outdoor torches (was 9.8 s main thread), per-torch worker ms, main-thread ms per frame while flooding,
  CPU memory. Correctness via EditMode tests + a GPU probe as in 158; no frame-time arms (not a perf task). Fast suite once.

## End
End normally if exact and the main thread stays under ~1 ms/frame while flooding; STOP_FOR_OWNER only for a non-exact result or memory over
~20 MB for 158's scene.
