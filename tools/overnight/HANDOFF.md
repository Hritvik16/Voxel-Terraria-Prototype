# 251-suncache-near-exact-dapples: DONE -- near zone keeps 1-voxel dapples at 100 % (249: 37 %); the cached sun's steps rise x1.1-1.5 (est.)
## What changed (commit 7facf4f)
- `SunCacheReference.cs`: near zone (8.18 sec. 1.3a). If a face is < 12.8 m from the camera and the cache does NOT call it fully lit (vis < 1), the contact
  ray goes on EXACTLY from Q for span + 2 voxels. span = how far up-sun the shadowing run starts, read from the 4 taps already fetched. The leg is capped at
  128 cells. Blocked = shadow; clear past the run's entry = lit (each tap line is clear up-sun of its entry by construction); capped = the cache's answer.
  The result blends into the plain cache answer over 9.6-12.8 m (continuous, no seam). `NearRule` Off/ExactAll/Partial/NotLit (default NotLit); `ExactRay` takes an optional `maxT`.
- `SunCacheOracleTests.cs`: `Eval` adds an "N0+near" row to C1-C6. New Slow test `C4b_NearZoneDapples_ExactLegKeepsThem`: it asserts >= 75 % and
  that Lookup's zone/blend matches the oracle rows; it compares the 3 rules, sweeps the leg cap and reports suns 16/30 for cost. C4 is now
  `[Explicit]` and report-only (the far-zone softening). C8 prints the near rule under moving slots. Reports now go to `Logs/overnight/251/`.
- `AMENDMENT_8_18_CACHED_SUN_SHADOW.md` (still DRAFT, NOT ADOPTED): revision note at the top. Touched: sec. 0, 1.3 (the old "N0 full strength to 12.8 m" claim is withdrawn),
  new 1.3a, 2.4, 3.1-3.3 (step 4a, a seam row, a leg-capped residual), 4 + new 4a (cost), 5, 6 (kill rule 4 rewritten; 247b -> 252b; 247c switches), 7, 8.
- `tools/overnight/later/252b-suncache-kill-timing.md`: the rewritten timed test. NOT in queue/: the supervisor starts the idle gate
  (`tools/overnight/release-when-idle.sh later/252b-suncache-kill-timing.md 20`). TASKS.md Task 251. `Screenshots/251/oracle.txt` (full report, committed).
## Evidence (CPU tests only; no player launch, no timing: rule 10)
- Fast suite before: PASS 1129 FAIL 0 SKIP 25 (`Logs/overnight/251/fast_before.out`, pre-change tree). After: PASS 1129 FAIL 0 SKIP 25 (`fast_after.out`).
  Filter `SunCacheOracleTests` (Explicit C4 included): PASS 13 FAIL 0, 0 `error CS` (`filter1.out`).
- C4b (3 m canopy, 1-voxel gaps, suns 45/60/80 x 8 azimuths, 1,572 dapple faces): NotLit **100 %**. Rule choice:
  ExactAll 100 % at 83.3 steps/face (exact 82.7). Partial **74.7 % -> fails** (sun 45 deg: 18.6 %), 16.4 steps/face. NotLit 100 % at 68.7. NotLit = cheapest passing rule.
- Leg cap sweep: cap 32 fails at 45 deg (13 %); 64 passes 45-80 deg; 128 also covers the 16 deg case. Cap 128 crosses a crown up to 9.6 m above the face
  at a 45 deg sun, but only 3.7 m at 16 deg. Above that the leg is capped and the dapple is softened even near you (residual, in sec. 3.3).
- C1-C9: the cache rows are byte-identical to 249 (diffed). Near rows: slopes 0 false shadows out of band; flat ground 0 acne; pole/wall 0 foot gaps and
  fewer mismatches than N0 (11/15 vs 35/75 of 86,154); 5a/5b/5c 0 leaks; C8 near strength 97-98 %, 0 sentinel reads; C7/C9 unchanged.
- Step proxy (`Logs/overnight/251/oracle_00_step_proxy.txt`): NotLit 58.9 vs exact 80.8 steps/face over all oracle scenes (x0.73); x0.57 on the
  16 deg canopy (half shaded); ~x1.0 under full shade at a high sun. Deep shade costs what today's ray costs.
- Cost at forest_eye-like shares (sec. 4a, est.; 248 coverage: near share 0.30-0.35 of faces on current trees, 0.51-0.56 on old trees):
  steps/face 24.8-26.7 with legs every frame, 15.5-16.0 with legs reused 1 in 4, against contact-only 12.4 and exact 74.0. On a frame basis
  that is x1.40-1.46 (every frame) or x1.10-1.12 (reused) of 247's 2.5 M. **Of 247's step cut, 83-85 % / 96 % survives (old trees 72-75 % / 93-94 %).**
  If ms followed steps, 247's 8-16 ms would become ~9-23 ms (est. only).
- Memory / pass card / slice (rules 11, 12, 14): none. This is CPU test code: nothing is allocated in play and nothing is drawn. HEAVY: no.
  (Design note: the recommended reused-leg form keeps the 8.0 MB sun history that 247 had hoped to free. Step 1 then nets +13.4 MB, not +5.4.)
## Images looked at
- `Screenshots/251/near_dapples_gap1_sun60.png`, 5 panels, left to right: exact | near rule (< 9.6 m) | blend middle (11.2 m) | cache N1 (12.8-25.6 m) | cache N2.
  Look at the dots: panel 2 matches panel 1 dot for dot, panel 3 has the same dots at about half brightness, N1 is faint with dots missing,
  and N2 has a few blurred blobs.
## Left open / design choices made (no owner decision blocks the next step)
- Legs reused 1 in 4 (recommended) vs every frame: 247c builds both behind `-suncachenearreuse`. Today's `-tshadow` history has NO edit
  invalidation, so the near zone adds one: an edit within 25.6 m runs every near leg that frame, which keeps the same-frame edit rule.
- 252b's `suncontactnear` arm is an UPPER bound (legs on every near face, every frame); GO is decided on it, KILL on `suncontact`.
## Next
- Supervisor: start the idle gate for `later/252b-suncache-kill-timing.md`. It can run the same evening as 254 (also a timing task) but not at the same time.

## For the doc
**Task 251: dapples near you are exact again (amendment 8.18, still a draft).** Task 249 showed that the cached sun alone dims 1-voxel leaf
dapples to 37 %. The new rule adds a **near zone**: if a face within 12.8 m of you is not fully lit according to the cache, it keeps tracing the
exact sun ray up through the leaves that shade it (at most 12.8 m along the sun). It fades to the cache's answer between 9.6 and 12.8 m, so no
seam shows. Faces the cache calls lit (open ground) stay cheap. Cutting leaves still shows the change in the same frame.

| 1-voxel dapple strength (suns 45-80 deg) | task 249 (cache only) | task 251 |
|---|---|---|
| near you (< 9.6 m) | 37 % (N0) | **100 %** (exact leg) |
| 9.6-12.8 m | 37 % | fades 100 % -> cache |
| 12.8-25.6 m (N1) | 18-32 % | same (accepted, owner Q-249 (b)) |
| 25.6-51 m (N2) | 1-12 % | same |
| cheaper rule tried ("only where the cache is unsure") | -- | 74.7 % -> fails the 75 % limit |

**Cost (step proxy, est., no timing):** sun work per face at the forest pose goes from 12.4 steps (247's plan) to 15.5-16 (recommended: near
legs reused 1 in 4) or 25-27 (every frame). Today's exact ray is 74. Of 247's planned saving in sun steps, **96 %** (or 83-85 %) survives. The timed test 252b decides.
Image: `Screenshots/251/near_dapples_gap1_sun60.png`. From left: the true sun, the new rule near you (same dots), the blend at 11 m (dimmer),
then the cache at 20 m and 40 m (faint or blurred). Compare panels 1 and 2: they match. Report: `Screenshots/251/oracle.txt`.
