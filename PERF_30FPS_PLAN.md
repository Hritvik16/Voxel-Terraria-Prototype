# PERF_30FPS_PLAN.md -- the plan to reach stable 30 fps (task 111, 2026-10-22)

Status: PLANNING ONLY, nothing is approved. The owner picks cuts (section 5). No code, no new measurement went into this
document. Every number cites its source. A number marked **est.** was not measured as an arm. It comes from the arithmetic shown.

**Target (owner, 2026-10-22; `ENGINE_PHASE_PLAN.md` E4):** warm steady state, play settings (shells ON, FSR 3), walking:
**p50 <= 30 ms AND <= 5% of frames > 33 ms.**

**Source of today's numbers:** task 110 (warm, AC, noon, shells ON, FSR 3 at 540 rows, quality preset High, far band + far shadows,
temporal shadow reuse ON). Logs: `Logs/costsplit_2026-10-22_122517.txt` (L1), `Logs/costsplit_2026-10-22_130301.txt` (L2),
walks `Logs/tg2world_20261022_133936.txt` and `..._134432.txt`, warm-up `Logs/tg2world_20261022_121916.txt`.

## 1. The gap

**Walking: p50 41.0 ms -> 30 (-11 ms, -27%), and frames > 33 ms 72-78% -> 5%. That needs p95 <= 33 ms. p95 is not reported; p99 is 99 ms.
Standing still: walk_near 60.7 -> 30 (-30.7 ms, -51%), walk_open 43.0 -> 30 (-13.0 ms, -30%).**

Notes on reading the gap:
- The 5% line is the hard part. Views that face a cliff (walk_near, 60.7 ms) are the slow tail of the walk, so in practice the
  5% line asks for walk_near-type views at <= ~33 ms. That is about -28 ms there (est.), plus removing the spikes behind the 99 ms p99.
- The walk is GPU-bound: the CPU floor is 5-7 ms (`tiny_gate`), present wait is 0. So cutting GPU pixels and rays is what counts.
- **The moving number flatters us.** On the walk, shells 3-7 hold NOTHING, and shell 1 holds 917-1,801 of 2,170 chunks
  (`[shells] held` lines, `Logs/tg2world_20261022_133936.txt`). The steady arms march filled shells. Once the shells fill while walking
  (row 9), the moving cost moves toward the steady 43-61 ms (est.).
- Everything was measured at noon (sun 78 deg). Sun shadows cost more at a low sun (task 59: +2-5 ms there). This plan does not
  have a golden-hour number (section 3, step 0).

## 2. Candidate cuts, ranked

Ranked by ms saved per unit of look given up, with exact (0 px) cuts first. "Near / open" are the 110 steady poses. The ms are
**not additive.** Rows 1-3 scale every pixel cost below them, so after a row cut each later row saves proportionally less.
**far_off (15-18 ms) is not its own cut.** It also stops the shell march (1 iteration per ray, 110 Table 1 note), so it overlaps
row 3/4. The far band's own cost is `farprim_off` 0.4-0.9 ms.

| # | Cut | What it removes | ms saved near / open (walk) | Look cost, in plain words | Frozen API / memory layout | Effort |
|---|---|---|---|---|---|---|
| 1 | **Shell column sky leap** (`later/102`, made concrete): a rising shell ray above its chunk column's highest stored brick (and the far band's height there) leaps to the column's edge. Today it climbs brick by brick up to the global `SolidTopY` (task 99). | Most of the sky-ray iterations. In walk_near, 5.8% of pixels (the top-right sky) run ~82-94 iterations each, and their SIMD groups wait (93b4 counters; 99 HANDOFF). | **est.** 3-8 / 1-2 (walk 1-3). The measured ceiling is `shellmarch_off` 12.8 / 18.8. | None: exact, 0 px, like 99 (proven by a CPU twin + test). | A small per-column table in the shell page tail (KB; 93b5's mask did the same). No frozen API. | 1 |
| 2 | **Quality preset Medium on the M1** (`-quality medium` exists, `ShellLayout.cs:134`). The shells get sized for 540 rows, which is what play actually renders (dyn sits at 540: frames are > 18 ms, `UpscalerSelect.Tick`). Today High sizes them for 720. | Shell iterations: each shell's band is c x 0.1 x 2^k m, with iterations per band ~ c, and c ~ sizing rows (8.14 §1). 540/720 = 0.75x, and 7 shells instead of 8. | **est.** 2-4 / 4-6 (walk 2-4): 25-35% of the remaining march. | At 540 rows, coarser voxels arrive at 1.5-2.0 px, as 8.14 rule A intends; today they arrive at ~1.1-1.5 px, which is detail nobody sees. Beyond 3.3 km (not 6.6 km) the 2.5D far band draws: no overhangs or caves past 3.3 km. The drawn 0.1 m area shrinks from 82 to 65 m; **simulation reach stays 128 m** (8.15). Memory -171 MB (8.14 §H.2: 855 -> 684 MB). Shells to fill: 841 columns instead of 1,164, so they fill sooner while walking. | No API. 8.14 §H.2 names High as the M1 headline: **owner decision.** | 0-1 (default + preset-driven FSR row range, §H.2 Q10) |
| 3 | **Internal rows below 540**: FSR 3 dyn with a floor of 432 (or 8.14 Medium's 405), and the dyn target moved from 16.7 to ~30 ms (`UpscalerSelect.DynTargetMs`), so the rows drop only on heavy views. | Traced pixels: 432 rows = 0.64x, 405 = 0.56x, 360 = 0.44x of 540's. | **est.** 432: 11-17 / 8-12 (walk 7-12); 405: 13-21 / 9-14; 360: 17-27 / 12-18. From 110's fit (pixel part = FULL - tiny_gate = 53.6 / 38.0 ms, incl. ~5 ms FSR 3) and task 51's measured 540 -> 720 (+53-56% for 1.78x pixels, `Logs/upscalerrealrate_2026-10-10_130258.txt`). The low end discounts to 70% of linear, after the 2026-09-26 Playground point (0.34x pixels gave -38%, not -66%). | Softer picture: voxel edges and thin ledges less crisp, and FSR 3 shows more ghosting and shimmer at 2.5-3x upscale. **No stills below 540 exist yet** (step 2 takes them). | No API. Below the owner's 960x540 minimum (2026-10-01 brief; Q38 (a) 540-720): **owner decision.** | 1 (floor + target + `CostApply` honouring `-renderrows`) |
| 4 | **Shells OFF in play** (undo 93c). Tiers 1-2 draw again. | The whole shell path. | **Measured** 13.4 / 13.6 (`shells_off`; walk not measured, est. less, since shells are mostly empty on the walk). | At both 110 poses at noon it looks the same (image A below). It loses 3D range past the 0.1 m window (the far band is 2.5D) and 8.14's direction. Saved edits beyond the window are drawn by the far band only. Memory: tiers 1-2 +193 MB (93d), shell pools -~390 MB (est.). | Reverses owner Q63 (a): **owner decision.** | 0 (setting) |
| 5 | **Sun shadows, cheaper**: (a) distance clamp, `-shadowclamp` 128 m (default 290 m); (b) temporal reuse 1 frame in 8 instead of 4. **Not** a §6.5 rung: rungs 0-2 are within 3 ms of each other (KNOWN_ISSUES "§6.5's shadow fallback ladder"). | Shadow ray length (a) or the number of rays (b). | **est.** (a) 2-4 / 0-1: Phase 7 measured 2.5 ms in Playground (`PHASE_7_COMPLETION.md` l.123), never on tg2. (b) 3-6 / 1-2. Measured: half-res (task 61, `-shadowres half`) only 0.5-1.3 of 29-43; sun OFF 15.7 / 6.0. | (a) Mountains more than 128 m away stop shadowing you (late-day long shadows cut short). (b) Moving shadows lag ~2 frames more when the sun or an edit changes them. Sun OFF = flat, unshadowed terrain (not proposed). | No API. Default change: owner sees stills at golden hour. | 1 each |
| 6 | **Cheaper upscaler**: STP or FSR 1 instead of FSR 3. | The FSR 3 pass. | **Measured** (task 51, older build): STP -2.7 / -3.1, FSR 1 -4.9 / -5.4, TAA -3.3 / -4.5 (two views, p50). 110: FSR 3 pass 5-6 (`ups_none`). | The owner picked FSR 3 after watching tasks 48/50/51's clips. FSR 1 is spatial: it shimmers on voxel edges while moving. STP is temporal, softer. | Reverses Q38 (a): **owner decision.** | 0 (setting) |
| 7 | **Far band shading/shadows off** (`fshadow_off`, `fshade_off`, `farprim_off`). | Far-band sun rays and far shading. | **Measured** far shadows 2.0 / 0.35 (mean, near is inside noise), far shading -1.0 / +1.0 (noise), far primary 0.85 / 0.35. Total <= ~3. | None at noon (image C). At a low sun, distant ridges lose their cast shadows and look flatter. | None. | 0-1 |
| 8 | **Saved-summary build off the main thread** (task 108's open cost). | ~3.1 ms per evicted edited chunk on the main thread (`SavedEditSummaries.cs` via `StreamManager.SaveDelta`), up to 4 per frame (`MAX_CHUNK_SAVES_PER_FRAME`). | **est.** 0 at p50; removes up to ~12-24 ms frames when walking away from edits. The walk has no edits, so 110 cannot see it. | None. | No API, no save-format change (D.5 unchanged). Reversible engineering. | 1 |
| 9 | **Shell fill workers** (93b2's open question). | Not a frame-time cut: shells 3-7 never fill while walking. | **est.** 0 or **worse**: filled shells march more (toward the steady cost). 3 shell workers on a fanless M1 share its heat budget with the GPU. | Today: far terrain while walking is drawn by the far band instead of the shells. | Changes Q47 (a) order policy if rationed. | 1-2 |
| 10 | **Spike isolation** (moving p99 99 ms; KNOWN_ISSUES "WHILE THE CAMERA MOVES"). `FrameGapProbe` + a per-frame log of frames > 50 ms on the walk, then one fix. | Unknown until isolated. | Unknown. Needed for the 5% line. | None expected. | Depends on the fix. | 1 + 1 |
| -- | Too small to plan on (110, < noise) | torch ~0, fluid ~0, HUD/minimap 0.3, shell shadow 1.0-1.3 | -- | -- | -- | -- |

Rows that were asked for and what they say:
- **FSR 3 at 540 / 720 / dynamic:** 540 is today. **720 costs +24-37 ms** (task 51 measured +33 / +27, i.e. 1.53-1.56x). **Dynamic is 540 in practice**: it steps down at > 18 ms smoothed, and every frame here is above that. So dyn only helps once its floor (row 3) and target change.
- **The quality preset:** row 2 (and Low = 480 sizing / 6 shells pairs with a 360-480 row floor, row 3).
- **Shell march:** row 1 (exact), row 2 (coarser = preset), the mask (93b5 (a): exact cheaper leap, **ceiling ~2 ms, measured**, not ranked), fewer shells (inside row 2), lower `ShellIterCap` 384 (walk_open hit rays need ~106, so a cap low enough to save time cuts off real hits: not ranked).
- **Gaps in the evidence:** no tiers_off or see-through-off arm exists; nothing was measured below 540 rows; noon only.

### Images (not committed; `Screenshots/**` is git-ignored)
- **A.** `Screenshots/93b/sheet_walk_open.png`: compare the top-left panel (shells OFF) with the top-right panel (shells ON). The ridge, the
  orange mountain at the right and the snow at the left are the same; the bottom-right tint panel shows the shells drawing the middle distance.
  So row 4 costs no look at this pose.
- **B.** `Screenshots/93b/sheet_walk_near.png`: in any panel, the sky is only the strip at the top-right corner (~6% of pixels). Those few
  pixels carry most of the shell march's 12.8 ms at walk_near. That is row 1's target.
- **C.** `Screenshots/110/walk_open_full.png` vs `Screenshots/93b3/split/walk_open_fshadow_off.png`: look at the far orange mountain at the right
  and the distant ridges. At noon, far shadows ON and OFF are indistinguishable, so row 7's look cost needs a low-sun still to judge.
- **D.** `Screenshots/110/walk_near_full.png`: walk_near at FULL. A sandstone cliff fills ~90% of the frame at close range, and the HUD reads
  27 fps (36.9 ms, one frame). This is the view the 5% line is about.

## 3. Recommended order (one change per task, a checkpoint after each)

Each step is ONE change. Each runs ONE warm measurement: the warm-up launch (`MODE=stream MOTION=walk ./run-tg2world.sh`, discarded), then
back to back, AC only: one cost-split launch with FULL vs the change's arm at walk_near / walk_open (ABBA inside), and `MODE=walk` 3 min
x 4 (A B B A). It reports walk p50, % > 33 ms, p99, and near/open p50. Then **STOP for the owner** (CLAUDE.md measurement rules).
Running sum = the **est.** midpoint after each step, starting from near 60.7 / open 43.0 / walk 41.0. Each step's saving is scaled by
the earlier steps (section 2 note).

| Step | Change | Outcomes -> what we do | Running est. near / open / walk p50 |
|---|---|---|---|
| 0 | (no perf change) `CostApply` honours `-renderrows`; the walk report adds p95, and a golden-hour walk arm (sun ~15 deg) | Bundled into step 2's task as the measurement fix. It gives the 5% line a direct number. | 60.7 / 43.0 / 41.0 |
| 1 | Row 1, shell column sky leap (exact) | >= 2 ms at near or open -> keep ON. < 1 ms at both -> off by default, and the shell march line closes (as 93b5 did). In between -> keep (exact, costs nothing). | ~55 / ~41.5 / ~39 |
| 2 | Row 2, Medium preset in play (if Q2 (a)) | >= 2 ms walk -> keep. ~0 -> keep for the memory and fill gain only if the owner wants that (Q2), else back to High. Slower -> revert. | ~53 / ~36.5 / ~36 |
| 3 | Row 3, dyn floor 432 + 30 ms target (if Q1 (a)); stills at 432 / 540 first | Walk p50 <= 30 -> keep and go on to the 5% line. Look rejected by the owner -> revert, and steps 4-5 cannot reach p50 alone (see below). | ~40 / ~28 / ~28 |
| 4 | Row 5 (a), shadow clamp 128 m (then 5 (b) as its own step if needed) | >= 2 ms at near -> keep, if the golden-hour still is accepted. < 1 ms -> revert, try (b). | ~37 / ~26.5 / ~26.5 |
| 5 | Row 10, spike isolation (measurement + one fix task) | It names the subsystem behind frames > 50 ms -> one fix task. If nothing is named (pure GPU variance), the 5% line rests on near-type views alone. | p50 unchanged; p99 ? |
| 6 | Only if near-type views are still > 33 ms: dyn floor 405 or 360 (Q1 (b)), **or** STP (row 6) | Near <= 33 and % > 33 <= 5 -> target met; then row 9 (fill), re-measured, since filled shells add cost back. | 405: ~34 / 360: ~30 near (est.) |

Row 8 (summary-build spike) is engineering for real play with edits. It is outside the walk measurement, so it goes in any free slot (one task, tests only).

**Honest reading of the sum.**
- **p50 <= 30 while walking is reachable by step 3 (est. ~28 ms)**, but only with rows below 540. Without row 3, steps 1-2 + 4 reach
  ~34 ms walk (est.), and the only other levers this size are shells OFF (row 4, -13 ms) or sun shadows OFF (-6 to -16 ms).
- **The 5%-over-33-ms line is NOT reached with confidence by the listed cuts.** It needs cliff-facing views <= ~33 ms. That is est. ~37 ms
  after steps 1-4, ~30-34 ms only with a 360-405 row floor (step 6), plus spike fixes of unknown size (step 5). On top of that, shells that
  fill while walking (row 9) add back part of the steady cost.
- What else would be needed if step 6 still misses: an exact shell air-chunk mip (new GPU buffer = memory layout, an amendment; the air-chunk
  leaps are 34-47% of shell iterations, 93b5) or a cheaper primary tier-0 march. Both are design work and are not costed here.

## 4. What needs the owner's approval (CLAUDE.md)

- **Default-on look changes:** rows below 540 (row 3; owner minimum 960x540, Q38 (a)); High -> Medium on the M1 (row 2; 8.14 §H.2 headline);
  the dyn target 16.7 -> ~30 ms (row 3); any shadow default (row 5); upscaler swap (row 6, Q38 (a)); shells off (row 4, Q63 (a)).
- **Performance-changing code** (every step 1-6) needs approval written in its task file.
- **Memory layout:** row 1 adds a small per-column table to the shell page buffer tail (as 93b5's mask did). It is not a frozen structure,
  but it is a layout addition. The air-chunk mip (section 3, last bullet) would need an amendment.
- **Frozen APIs** (`IWorldQuery.GetVoxel`, `IEditService.SetVoxel`, D.1, §4.4): **no row touches them.** Row 8 keeps the D.5 format.

## 5. Questions for the owner (recommendation first)

- **Q1 Internal rows.** (a) **FSR 3 dyn 432-540, target ~30 ms**: rows drop only on heavy views; stills at 432 vs 540 before you decide;
  (b) dyn 360-540 (most headroom, softest); (c) keep the 540 minimum: 30 fps then needs shells OFF or sun shadows OFF.
- **Q2 Quality preset on the M1.** (a) **Medium**: shells sized for the 540 rows we render; far band past 3.3 km; drawn 0.1 m area 65 m,
  simulation stays 128 m; -171 MB; (b) keep High; (c) Low (480 sizing, 6 shells, only with Q1 (b)).
- **Q3 Shells.** (a) **keep shells ON and cut their march** (steps 1-2); (b) shells OFF in play until the march is cheaper (-13 ms measured,
  same look at noon, loses 3D range past the window; reverses Q63 (a)).
- **Q4 Sun shadows.** (a) **try the 128 m clamp, then 1-in-8 temporal reuse, one task each, judged on golden-hour stills**;
  (b) turn half-res shadows on now (-0.5-1.3 ms, blockier edges); (c) no shadow cut.
- **Q5 Upscaler.** (a) **keep FSR 3; STP (-3 ms) is step 6's fallback**; (b) STP now; (c) FSR 1 now (-5 ms, shimmers).
- **Q6 The step order** in section 3 (exact cut first, then preset, rows, shadows, spikes). (a) **as written**; (b) rows first (largest
  lever; steps 1-2 then measure smaller).
- **Q7 Shell fill while walking** (93b2's question). (a) **park it until 30 fps is met**: filling adds cost back; (b) now.
- Going ahead with (a), reversible engineering: **Q8** the summary build moves to a worker job (row 8; no format change).
  **Q9** step 0's measurement fixes (`-renderrows` in the cost split, p95 in the walk report, a golden-hour arm).
