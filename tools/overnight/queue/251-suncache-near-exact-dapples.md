# 251-suncache-near-exact-dapples

Model: opus

Approval: OWNER 2026-11-20 ("go with recommended for all questions"): Q-247 (a) staged trial of amendment 8.18; Q-249 (b) keep the cached
sun, but near the camera the exact ray carries 1-voxel leaf dapples, and further out the cache may soften them. CPU tests + design only.
NO frame times (rule 10); 238 may be timing in the same evening -- this task launches no player.

Context: task 249 (`tools/overnight/blocked/249-suncache-cpu-oracle.HANDOFF.md`, `Screenshots/249/oracle.txt`) built `SunCacheReference` +
`SunCacheOracleTests`; kill rule (4) fired: 1-voxel N0 dapples keep 37 % (limit 75 %); 2-voxel 49-73 %, 3-voxel 68-83 %. All other rules pass.

1. Rewrite `AMENDMENT_8_18_CACHED_SUN_SHADOW.md` sec. 1.3 (and every section it touches: budget, kill rules, 247b) for option (b): a
   near zone where the per-face sun answer is the EXACT ray (`ShadowRayReference.Trace` semantics, no cap) or an exact ray long enough to
   cross the canopy, and the cache beyond it. Choose the zone rule from data (distance from camera, or "cache says partial"/edge
   texels only -- whichever keeps 1-voxel dapples >= 75 % near you for the least exact-ray work). Blend across the boundary so no seam
   shows. Keep the same-frame edit rule. Stays DRAFT, NOT ADOPTED until 247b.
2. Implement the rule in the CPU twin; add C4b (1-voxel dapples inside the near zone, suns 45/60/80, >= 75 %) and keep C4 as the far-zone
   report (Explicit, prints the softening). Re-run C1-C9: none may regress.
3. Re-estimate cost with 249's step proxy (raw DDA steps per face): exact 74.0, contact 12.4 today. Report steps/face for the new rule at
   forest_eye-like screen shares (use 248's band coverage: 22.9 % leaf at 10-40 m, wood 19 % inside 10 m on old trees). Say plainly how
   much of 247's 54.7 -> 8-16 ms guess survives, as a step ratio (no ms claims beyond "est.").
4. Rewrite the 247b timed-test task as `tools/overnight/later/252b-suncache-kill-timing.md` (idle Mac, ~45 min, release by
   `tools/overnight/release-when-idle.sh`, log HID idle before/after each arm, rerun an arm the owner touched; kill thresholds updated
   for the new rule). Do NOT move it into queue/ -- the supervisor starts the idle gate.

Tests: fast suite before and after; filter `SunCacheOracleTests`. Memory / pass card / slice: none (CPU test code). Update `TASKS.md`.

`## For the doc`: one paragraph on the near-zone rule, a table of dapple strength near / far vs 249, the step ratio, and one 5-panel still
like `Screenshots/249/dapples_gap1_sun60.png` (exact | new rule near | new rule far ...) if it shows the fix (look before captioning).
