# Task 238 -- what the ~58 ms "rest" is at forest_eye, sun 16 deg (2026-11-20)

Capture: `PLAN_PATH_TO_33MS.md` sec. 2's cost split. Play's player (DEV_KERNELS=0, built 16:51 at `2d2e0ea`), `$PLAY_SETTINGS_FLAGS
-quality high -renderrows 432 -shellwait 60 -startsun 16 -costfixed forest_eye|3366.4,532.5,4950.0|3366.4,529.7,4928.0 -costtimedpaths forest_eye`
(the pose pinned in `PERF_BUDGET.json`). WARM STEADY STATE: one warm-up `MODE=stream MOTION=walk ./run-tg2world.sh`, numbers thrown away
(`Logs/tg2world_20261120_172018.txt`), then launches back to back: A 17:25-18:42, B 18:42-19:37, C 19:37-20:07, D 20:08-20:52.
Each arm runs X X F after an F (ABBA style); a run is a 180 s streaming settle (forest never reaches deficit 0), 3 s settle, 20 s timed.

| launch | arms | report |
|---|---|---|
| A | tiny_gate, ups_none, shells_off, far_off, lotl_off, look233_off | `costsplit_A.txt` |
| B | sunres4, clamp64, sunfar64, reuse16 | `costsplit_B.txt` |
| C | FULL with `-trees2 0` (old trees) | `costsplit_C_trees2off_partial.txt` |
| D | far_off (rerun: A's was dirty), skypale_off, grade_off | `costsplit_D.txt` |

## 1. The split (p50 ms, forest_eye, 16 deg, 432 rows, FSR 3 to 1080p)

FULL this session: A 140.4 (7 runs, range 29.9: it crept up 128 -> 146 over the launch), B 142.3 (range 3.5), D 132.3 (range 11.2);
235: 135.4. Sun 54.7 and sky fill 25.5 are 235's (not re-run). **rest = ~138 - 54.7 - 25.5 = ~58 ms.**

| piece of "rest" | ms | noise | source | walk_near (for scale) |
|---|---|---|---|---|
| CPU + fixed floor (`tiny_gate`: 256x144 march, no upscaler) | **18.6** | passes 17.0 / 19.3 | A | 7.1 (146) |
| Shells (`shells_off`) | **15.0** | 0.6 | A | 7.0 (235) |
| Far band past the shells (`far_off`) | **11.2** | 5.4 | D (A's 0.6 had owner input in both timed windows) | 0-2.4 (146) |
| Look 226: vary, crevice, aerial haze, bloom (`lotl_off`) | **-3.1 (~0)** | 6.7 | A | -- |
| Look 233: pale sky + clouds (`skypale_off`) | **2.6** | 5.8 | D | -- |
| Look 233: colour grade (`grade_off`) | **4.4** | 3.1 | D | -- |
| FSR 3 upscale pass | ~5.6 | -- | 146 (NOT re-measured: `ups_none` measures something else here, see sec. 3) | 5.6 |
| **remainder = tier-0 primary march + surface shading + copies** | **~4 (0-22)** | +-15 | by subtraction | ~15 (160 est.) |

Arms are not additive. The remainder is ~4 ms if the CPU floor runs in series with the GPU, and up to ~22 ms if it hides under the
GPU-bound frame (this rig has no per-stage timing). Either way the tier-0 march is NOT most of "rest" at this pose.

Other numbers from the same launches:
- `look233_off` = **30.6**: it also returns the 16 deg start sun to noon. Minus sky + grade (7.0), the low start sun costs ~24 here
  (235's `startsun_off`: 35.0).
- **Trees** (C, one launch, not interleaved): `-trees2 0` (the old trees) FULL 85.6 / 87.1 vs dense 140.4 (A) = **-54 ms**. With old trees the sun
  step is 33.0 (FULL 86.4 - sun_off 53.5) vs 54.7 dense. The two stills differ in content: the old trees put big solid trunks across
  the near frame (the primary rays stop early), while the dense trees are porous crowns at eye level. That matches 248 (porous ~2 ms per 1 %).

## 2. Sun levers at 16 deg (B: FULL range 3.5 ms, so these are the cleanest numbers here)

| lever | saved at 16 deg | at noon (earlier tasks) |
|---|---|---|
| `sunres4` (S1, low-res sun, 232) | **10.9** (noise 3.2) | -2.5 +- 8.9 (~0) |
| `clamp64` (S2, sun-ray length cap 64 m, 118) | **8.9** (0.2) | 0 (no-op) |
| `sunfar64` (S3, coarse far sun, 228; known slope defect) | **10.3** (0.1) | -6.1 |
| `reuse16` (S4, temporal sun reuse 1 in 16, 124) | **5.2** (1.7) | 0 |

The low-sun levers pay ~9-11 ms each at 16 deg, about a fifth of the 54.7 ms sun step each. They overlap: not additive.

## 3. Still unknown

1. **`ups_none` = 59.3 ms** (75.0 / 73.3 vs FULL 138.1 / 128.5). This arm drops FSR 3, its inputs (per-frame sub-pixel jitter, motion vectors) and
   URP post-processing. The upscale pass itself was 5.6 at walk_near, so ~54 ms is unexplained. Hypothesis (code, not measured): FSR 3's
   jitter moves each pixel's hit across leaf-voxel edges every frame. That misses the temporal sun history, which is keyed by shadow-origin
   voxel with a 4-pixel probe (`Raymarch.compute` ~5909), and maybe sky fill's history too. Without jitter, a still pose reuses nearly all of it.
   `reuse16` saving only 5.2 fits that. Test: a capture-only arm setting `RaymarchFeature.Fsr3ZeroJitter` (the field exists; only the
   RowsLook capture sets it). That is a code change, so it was not done here.
2. Whether the 18.6 ms CPU floor adds to the GPU time or hides under it (sets the remainder between ~4 and ~22).
3. Sun and sky fill were not re-split this session (235's values used).
4. Memory for `-trees2 0`: launch C was stopped before its `[mem]` line (see process notes).

## 4. Decision table (written in the task before measuring)

| row | condition | result | applies? |
|---|---|---|---|
| 1 | tier-0 march + shading is most of "rest" -> 8.17 T1-T5 next | remainder ~4 (at most ~22) of ~58 | **no** |
| 2 | CPU/streaming floor or shells big at forest -> streaming/shell task first | floor 18.6 + shells 15.0 + far band 11.2 = ~45 of ~58 (all 1.6-5x walk_near) | **YES** |
| 3 | look passes over ~8 ms -> propose a cheaper look | 226 ~0; 233 sky 2.6 + grade 4.4 = 7.0, each within 1-2x noise | no (borderline) |
| 4 | sun levers pay at 16 deg -> queue S2 | clamp64 8.9, sunres4 10.9, sunfar64 10.3, reuse16 5.2 | **YES** |

**Chosen next step: row 2, a forest streaming-floor / shell task.** The forest keeps a 520-690-chunk deficit, so the main thread never idles:
tiny_gate costs 18.6 here vs 7.1 at walk_near (deficit 0). Shells cost 15.0 and the far band 11.2. **S2 (row 4) gets queued behind it.**
Before either, the jitter test (sec. 3.1) is the one diagnostic that could move the biggest number. It is a one-line, capture-only arm, but it is code.

## 5. Memory (rule 11; no code changed, so there is no switch to compare except trees)

| launch | CPU brick pool peak (cap 570,000; valve 484,500) | GPU body pool peak / refused | process peak |
|---|---|---|---|
| A | 498,923 (over the valve: it ran) | 245,704 of 254,400 / 0 | 2,731.3 MB |
| B | 495,923 | 245,704 / 0 | 2,493.6 MB |
| D | 496,045 | 245,704 / 0 | 2,592.1 MB |

Rule 14 slice: nothing drawn changed. HEAVY: no.

## 6. Process notes (owner input; deviations)

- Idle: `ioreg` HIDIdleTime was logged as asked, but a running player resets it every ~10 s (it declares user activity), so it reads 0-10 s
  throughout. Arms were judged by `Logs/budget/hididle` (real key/mouse/scroll events only; task 212). Per-run table: `input_per_run.txt`.
- Try 1 (16:56) was stopped at once: the owner was using the Mac (real input every few seconds). It restarted after 20 min idle (17:19).
- Owner input during timed windows: A forest runs 11-12 (`far_off` both passes) -> rerun in D (clean). Input only in the untimed settle:
  A run 13 (FULL pass 5, kept), B run 3 (sunres4 pass 2, kept). All of launch D was clean.
- The named single 10-arm launch was split into A + B with the same flags. At forest each run waits the 180 s settle (~3.7 min), so 10 arms is
  ~2 h in one launch. The split loses only half on a crash or owner disturbance.
- C was meant to be FULL only, but with no `-costarms` it fell back to the default 8 arms (~90 min). It was stopped after FULL x2, sun_off x2 and torch_off x2.
- D added `skypale_off`, `grade_off`: sec. 2's `look233_off` also switches the start sun off, so on its own it cannot tell the look's cost.
- Scripts: `drive.sh`, `drive2.sh`, `perrun.py` (copies here).

Images (git-ignored, in this folder): `A/forest_eye_full_clean.png` (FULL, dense trees), `C/forest_eye_full_clean.png` (old trees),
plus per-arm stills `A/forest_eye_<arm>.png`, `B/...`.
