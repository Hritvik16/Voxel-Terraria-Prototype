# 206-bug-debug-views-player-build: DONE. Debug views 1-3 + the debug pixel work in a player; the torch-flood kernel (play's, flood on) was broken too and is fixed.
## What was really broken (launch A, shader unchanged: `Logs/overnight/206/look_A.out`, `Screenshots/206/player_errors206.txt`)
- Metal compiles a kernel at its FIRST dispatch. Three kernels failed there (Player.log `program_source:..: error`, `Metal: Failed to create compute library`):
  `CSMainFarShade` (debug views / pixel): `as_type<float>(bool)` from SkyHistFetch's early return in an unrolled loop (task 193);
  `CSMainFarShadeFlood` + `CSMainFlood`: the same AND 33 Metal buffer slots (cap 31) since 193's SkyHistR/W. Play with the flood on (175's default) uses
  `CSMainFarShadeFlood` once the pool is ready, so **play in a player has drawn stale frames with the torch flood ready since task 193 (2026-11-13)**.
- 198's "more uavs (9) > 8" message did NOT appear in either launch; the UAV cut (below) was done anyway (cheap, exact).
- Launch A: debug views 1-3 = beauty, 0 px differ; the whole 2nd pose = the 1st pose's last frame (0 px differ): stale images, as 198 said.
## Fix (commit 6a61ea5)
- `Raymarch.compute`, outside diet 2 only: `HEIGHT_MIP_READ` = read-only `HeightMipRO` (9 -> 8 UAVs; RaymarchFeature binds the same buffer);
  SkyHistFetch walks the 4 probes backwards and keeps the last match (= the first; CPU twin test on all 3,888 patterns); flood twins fold
  `_UseLODCascade` to 0 (`#define` after diet 2's pair: 33 -> 29 slots), RaymarchFeature picks a flood twin only while the cascade is off (always with shells on).
- Exact: 17 of 21 compiled kernels have byte-identical Metal source vs the pre-task build, incl. CSMainFarShadeLean/LeanRoof/LeanSun, CSSunDeferLean,
  CSShellPrepass(Lean), Build*, Far*, Taa* (`Screenshots/206/msl206.txt`). Changed: CSMain, CSMainFarShade, CSMainFlood, CSMainFarShadeFlood; 0 bool casts left.
- LOUD: `ComputeShader.IsSupported` said TRUE for the broken kernels, and native Metal errors never reach `Application.logMessageReceived` (tried in A: 0 seen).
  So `KernelHealth` (new) runs a GPU HEARTBEAT: before an unverified non-diet kernel's dispatch DebugBuffer[0..2] = NaN, read back next record; NaN = never
  ran -> ONE `[kernels] FAILED` error, blit pass clears to MAGENTA, `RaymarchFeature.KernelFailFrames`. 2 good reads = verified, then nothing per frame.
- Survey arms `_tf1` (flood on), `_dvN` (debug view N), `_px` (debug pixel); `[kernel]` / `[dbgpx]` lines; summary at the end. TASKS.md paragraph; KNOWN_ISSUES entry rewritten (FIXED).
## Evidence (launch B, fixed: `Logs/overnight/206/look_B.out`; 2 poses fixed_cliff / fixed_forest_breaks, native 1920x1080 no upscaler)
- Player.log B: 0 `program_source` errors. Heartbeat: `CSMainFarShade` and `CSMainFarShadeFlood` verified; FAILED none; KernelFailFrames 0.
- Debug pixel (centre): dir (0.0007,0.5529,-0.8332), 12 steps, 529 iters, hit 822.9 (cliff); (-0.0005,-0.1956,-0.9807), 11 steps, 287 iters, 1150.2 (forest).
  Launch A, same arms: dir NaN (the kernel never ran: the heartbeat's signal, seen on the real bug).
- Debug views vs beauty, B: 100 % px differ at both poses (mean |d| 64-113); A: 0 % (the bug). Magenta 0 % everywhere (nothing failed in B).
- Beauty before/after: fixed_cliff raw native A vs B 0 px. The 2nd pose has no valid "before" (A's were stale frames); the byte-identical diet-2 Metal
  covers every pose. In B, CSMainFarShade's beauty = Lean's at fixed_cliff (0 px); the flood twin with no torches = Lean (0 px).
- Fast suite PASS 1033 FAIL 0 SKIP 21 (`Logs/overnight/206/fast.out`); filter runs 16/16.
- Memory (rule 11): no new buffers (an alias of the same buffer, a 4-byte readback while kernels are unverified). Ledger at the first pose A -> B: GPU body pool
  peak 90,984 -> 91,781, refused 0 -> 0; CPU pool peak 172,304 both (valve 484,500); process peak 2,533 -> 2,651 MB. Shell pools are the same, and the same
  code gives 2,512-2,652 MB across 198/205's launches, so I read the +118 MB as launch noise. It is not proven: no OFF/ON pair in one build.
- Looked at `Screenshots/206/206_debug_views_sheet.png` (below).
## Not done / caveats
- The magenta paint has not run in a player (no broken kernel was left to trip it); its input (NaN on a broken kernel) was read in launch A, the state machine is CPU-tested.
- Diet-2 kernels have no DebugOut, so the heartbeat cannot check them; `run-look-review.sh`'s Player.log grep still covers them.
- Flood + tier cascade (shells OFF + torch flood) now draws analytic torches. It never worked anywhere since 193.
- 2 launches (A = the bug's evidence + the "before", B = the proof), not 1.
- Next: owner/supervisor may want to know that play's torch flood was broken in players for a day (no action needed now).

## For the doc
- Result: debug views and the debug pixel work in a player build again. Play's torch-light kernel had been broken in player builds since task 193 by the
  same kind of fault, and is fixed too. A kernel that fails to load now shows a magenta frame plus an error line instead of the last good image.
- Numbers: kernels failing in a player 3 -> 0 (Player.log errors 6 -> 0, `Screenshots/206/player_errors206.txt`); debug view vs beauty, px differ 0 % -> 100 %;
  debug pixel NaN -> real values (12 steps, hit 823 m); torch-flood kernel Metal buffer slots 33 -> 29 (cap 31); play kernels' compiled code 17/21 kernels byte-identical
  incl. all play (diet-2) ones, beauty 0 px at fixed_cliff (`Screenshots/206/msl206.txt`, `cmp206.txt`); memory +0 by design (refusals 0 -> 0).
- Image: `Screenshots/206/206_debug_views_sheet.png`. Top row = cliff pose, bottom row = forest pose. Columns 1-2 are BEFORE: debug view 1 is the plain
  picture, and in the bottom row both frames still show the cliff (stale frames). Columns 3-5 are AFTER: step heat, uniform/dense and normals are real debug
  colours. Column 6 is the torch-flood kernel, now drawing the forest wall. Full-size shots are in `Screenshots/206/A|B/`.
