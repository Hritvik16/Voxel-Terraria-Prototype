STOP_FOR_OWNER -- Q54: the GPU FLUID CA reads its terrain from the GPU mirror box. A 16^3 box means fluid between ~90 m and 128 m stops flowing.
Does this change what the game can simulate? YES, unless you pick (b). The box shrink is built and tested, but it is OPT-IN (`-smallmirror 1`) until you answer.
- Why: `FluidCA.compute` `SampleVoxel` reads the tier-0 mirror and returns AIR outside it. Fluid is live to 128 m (`FLUID_ACTIVE_RADIUS_VOXELS`).
  Today's 32x8x32 box covers +-192 m. The 16^3 box covers only >= 89.6 m. Past that edge, awake water sees rock as air.
  The CPU re-checks every fluid move, so nothing is corrupted. That water simply freezes (its moves are dropped). Amendment 8.15 §1.2 assumed the fluid CA is CPU-side.
- (a) Accept: the fluid CA reach becomes the drawn box: >= 90 m on Low-High, 128 m on Ultra. Clamp the fluid radius to it so far water sleeps cleanly. Flip the default.
  Cost: a rule that depends on the preset (8.15 says NEAR must not), and water you leave flowing 90-128 m away stops until you return.
- (b) **Recommended:** keep fluid at 128 m everywhere. A new task (est. M) gives the fluid CA a small terrain directory of its own for awake-fluid chunks outside the box (~1 MB, est.); D3 maps their bodies too. Then flip the default.
- (c) Drop D2: keep 32x8x32 and go straight to D3, giving up ~150-210 MB.

# 93e-engine-14d2-gpu-mirror-box: BUILT, OPT-IN -- 16^3 GPU mirror box with shells on; CPU reach unchanged; default waits for Q54

## What changed (commit dc68d34, branch engine-phase)
- `ShellLayout.MirrorBox`: Shell0Directory if shell 0's radius fits `BoxInnerReachM` (d/2 - 1 chunks; 16 -> 89.6 m), it is pow2, and it has >= 8 layers.
  Otherwise today's box. Result: **Low/Medium/High 32x8x32 -> 16^3** (shell 0: 59/65/82 m fit), **Ultra keeps 32x8x32** (116 m > 89.6 m).
- `Phase4Bootstrapper`: `-shells 1 -smallmirror 1` (no `-keeptiers`) builds the clipmap with that box, after a pow2 assert. The load radius stays `TG2_LOAD_RADIUS_CHUNKS` = 10. The CPU ring stays 32x16x32.
- `StreamManager.MirrorOrigin` (pure, from SlideWindow): a narrower box centres on the camera by its own size (`>> 1`). Equal widths give the old origin, so shells off is unchanged.
- **Bug found and fixed (new with a box smaller than the resident area):** after a recentre, a slot could still hold a chunk that had just left the box.
  Walk back across a chunk edge and the other chunk was drawn there (LOST). A not-yet-loaded position drew it too (stale).
  Fix: `TerrainClipmap` remembers each slot's owner. On a recentre, every box position whose slot holds another chunk is marked dirty: resident -> upload, absent -> clear. This only runs with the small box.
- `ClipmapValidator.ValidateRegion` skips residents outside the box (not mirrored by design; it used to call them LOST). It counts them as `chunksOutOfBox`.
  Shell pose runs now log a `[mirror]` line (lost/lag/stale).
- KNOWN_ISSUES entry + CLAUDE.md line. No frozen API, no §4.4 change, GPU body buffer untouched (that is 93f/D3).

## Evidence
- `UploadDirtyBoxTests` 9 (6 new):
  - box per preset, pow2, and the CPU load radius is 10;
  - MirrorOrigin;
  - residents outside the box are skipped and counted (57 of 121);
  - a walk out and back gives 0 wrong slots;
  - the control without slot owners reproduces the wrong chunk;
  - a position with no chunk is cleared after a recentre.
  E1's `EditSummaryTests` (5) pass (the edited out-of-box chunk shows in its shell). **SUITE=all PASS 830 FAIL 0 SKIP 23** (`Logs/overnight/93e-all.out`).
- In game, 16^3, after each teleport (`Logs/overnight/93e-mirror2.out`, `Screenshots/93e/mirror_check_farpreview.txt`): **MIRROR lost 0 lag 0 stale 0** at glide_400m, eye_level_3 and cliff_foot_1.
  Resident 3,087 = 21x21x7, so the 128 m reach is kept. 1,792 chunks are in the box and 1,295 resident chunks are outside it.
- Shells off: `run-phase7-rig.sh` **PASS 37 FAIL 0, 19/19 shots byte-identical** to 93d's `Phase7Cycle/2026-10-16_023443` (`Logs/overnight/93e-phase7.out`).
- `run-acceptance-rig.sh` CANNOT pass `-shells 1` (it builds the flat Phase 4 world), so this is the shells-off run (`Phase4Acceptance/2026-10-16_071916`): PASS 51 FAIL 2.
  Gate D is all PASS. Gate C's correctness checks all PASS. Both FAILs are the known §4.3 upload-p99 timing check (Gate C and Gate E; KNOWN_ISSUES; it was red on 10-12 too). No frame time is quoted (rule 10).
- Memory, shells on, same 4 poses and flags as 93d plus 2 (`MODE=poses EXTRA="-shells 1 -shellworkers 3 -gateres 1280 -shellwait 60 -poses eye_level_3,glide_400m,cliff_foot_1,peak_from_2km_ground -memledger -shellsdebug 60"`, after arm `+ -smallmirror 1`):
  - TerrainClipmap ledger row: **513.1 GPU + 147.5 managed -> 439.6 + 74.1 MB (-147)**, the amendment's estimate (dir 128 -> 64 MB, managed 147 -> 74). Ledger SUM 1,907.0 -> 1,758.2 MB.
  - phys_footprint median over 37 samples after start-up: **2,688 -> 2,475 MB (-213)**. The single peak sample only moved 2,703 -> 2,664: the after arm's peak is one start-up spike (2nd sample), and it settles at ~2,470-2,580.
  - Files: `Screenshots/93e/{before,after}/` (farpreview.txt, *_mem.txt, *_peak_breakdown.txt).
- Stills (one launch per arm, 60 s waits; the shells were still missing ~1.7-2.2 k chunks per pose, as in 93d):
  - glide_400m and cliff_foot_1: before and after are pixel-identical.
  - eye_level_3: 2.1% of pixels differ, peak: 0.34%. All of it is the strip between the box edge and 128 m, which shell 1 (0.2 m) now draws instead of tier 0.
  - No seam, hole or popping at the hand-over in the beauty crops.

## Not done / next
- The default stays off until Q54. 93f (D3) Requires 93e and its saving depends on this box. Under (b), D3 must also map the fluid chunks' bodies.
- With the small box, a chunk crossing re-uploads ~112 chunks over ~7 frames (16 per frame). Counted in `RecentreDirtiedTotal`, not timed (rule 10). Step 16's perf milestone should look at it.

## For the doc
Task 93e (engine 14d2, 8.15 row D2): the tier-0 GPU mirror box shrinks to 16^3 with the shells on. Built and tested, but OPT-IN (`-smallmirror 1`): the GPU fluid CA reads terrain from this box (owner Q54).
- Box: Low/Medium/High 32x8x32 -> **16^3** (shell 0 at 59/65/82 m fits the 89.6 m inner reach); Ultra keeps 32x8x32. CPU load radius 10 (128 m) on every preset (3,087 resident at every pose).
- TerrainClipmap memory: 513 GPU + 148 managed -> **440 + 74 MB**. Process footprint median 2,688 -> **2,475 MB** (`Screenshots/93e/{before,after}/`).
- Mirror after recentres: lost 0 / lag 0 / stale 0 (`Screenshots/93e/mirror_check_farpreview.txt`). Phase 7 19/19 byte-identical, PASS 37. SUITE=all PASS 830 FAIL 0.
Images:
- `Screenshots/93e/eye_level_3_sheet.jpg`: left BEFORE, right AFTER. Top row is the beauty shot: the same. Bottom row is the shell tint.
  The blue tier-0 ground now ends at the straight edge of the 16^3 box. The green shell-1 strip beyond it is what the shells now draw. No gap.
- `Screenshots/93e/eye_level_3_edge_zoom.jpg`: 2x crop of the lower right, where the box edge crosses the ground. Top: beauty before/after, no seam or hole. Bottom: tint, the blue/green boundary moved left.
- `Screenshots/93e/peak_strip.jpg`: peak_from_2km_ground rows 700-900. Strips top to bottom: before, after, tint before, tint after. Look for the thin green shell-1 band in the last strip.
