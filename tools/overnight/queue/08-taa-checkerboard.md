Requires: 07-taa-risk-checks

Approved by the owner 2026-10-02 (TAA proposal step 3 ticked: "go ahead with
step 3 once step 2 is in and measured"). ONE performance change, measured.

Goal: trace half the primary rays per frame (checkerboard, alternating each
frame) and let TAA's history fill the other half. Its own switch (e.g.
`-checker`, implies TAA), OFF by default.

Background (proposal, walk on the island at 960, 2026-10-01): ~32 ms a frame,
~5 ms fixed and ~27 ms scaling with pixels traced, so checkerboard should save
roughly 13 ms. Target 16.7 ms (60 FPS) -- the estimate lands near it, not safely
under.

Acceptance:
- EditMode 0 FAIL; build 0 Shader error; switch OFF byte-identical poses.
- ON: stills and task 05's shimmer metric vs TAA-only; look for checker
  patterns, ghosting on fluid and edits (repeat task 07's checks 1-3 briefly).
- Cost: `MODE=walk MINUTES=3`, three balanced reps: TAA-only vs TAA+checker.
  Report mean / p99 of 30 s windows and whether 16.7 ms is reached.
Do NOT start step 4 (shadow tuning) -- not approved; if 16.7 ms is not reached,
say by how much in HANDOFF. Commit with the evidence.
