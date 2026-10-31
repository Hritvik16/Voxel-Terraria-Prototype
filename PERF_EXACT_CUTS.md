# PERF_EXACT_CUTS.md -- exact-only cuts toward 30 fps (task 160, 2026-10-31, PLAN ONLY)

Owner, 2026-10-31: half-res shells, temporal shell reuse, a cheaper upscaler, shells off and far shadows off "are all bad picture
quality". So this plan lists only cuts that leave the picture **byte-identical** (raw no-upscaler stills 0 px, as tasks 132/134/137/153
proved theirs), stacked on task 159 (play: FSR 3 dyn 432-540 at a 30 ms target). No code, no default and no look change went into it.

**No new measurement.** The task allowed one warm launch. I wrote a 4-line capture-only cost arm (`skyprobe_off`) and the cost split's
432-row clamp, then found the Mac **on battery at 15 % and discharging**. A build plus a warm-up plus a cost split (~1 h of Unity) could
run it flat, and battery numbers aren't comparable to AC (task 08). So I reverted both edits and moved that measurement into cut B's task (section 4).
Every number below cites its log. **est.** = arithmetic, not an arm.

## 0. Starting point (after 159)

| | value | source |
|---|---|---|
| walk p50 / p95 / > 33 ms (play, 432-540 dyn) | **32.2-33.1 / 61-75 ms / 47-50 %** | 159 ABBA, **battery** (`Logs/tg2world_20261031_{110242,110933}.txt`) |
| rows on the walk | 432 on 81-89 % of frames | same |
| steady walk_near / walk_open at 540 (AC, warm) | 61.0 / 36.1 ms | 146 `Logs/costsplit_2026-10-25_115751.txt` |
| steady walk_near / walk_open at 432 | **~47 / ~28 ms est.** | 540 minus pixel part (53.9 / 31.7) x 0.36 x 0.7 (111's discount) |
| target (E4) | walk p50 <= 30 **and** <= 5 % of frames > 33 ms (p95 ~33) | PERF_30FPS_PLAN |

**Read every estimate below through the dyn controller.** At a 30 ms target with a 27-33 ms dead band (159), a cut on a view already
near 30 ms is spent on ROWS (sharper picture), not on ms. Exact cuts therefore show up mainly as (1) lower ms on the heavy views that
sit at the 432 floor (the p95 tail, walk_near-type) and (2) a higher mean row count on light ones. **A/Bs must pin `-renderrows 432`**
(cost split) or report `meanRows` (walk tsv, 159), or they will read as "nothing changed".

## 1. Where the walk frame goes (today's build)

GPU-bound: present wait 0, CPU main-thread work ~6 ms of a 34.5 ms frame (115). Arms are NOT additive. The residual row is the
subtraction (FULL minus the other rows) and carries their non-additivity.

| pass | walk_near 540 | walk_open 540 | 432 est. near / open | measured? source |
|---|---|---|---|---|
| CPU + fixed floor (`tiny_gate`: 256x144, no upscaler) | 7.1 | 4.4 | ~7 / ~4.4 | measured, 146 |
| **sun shadows, all** (`sun_off`) | **17.2** | 5.5 | ~11-14 / ~3.5-4.4 | measured, 146 |
| . of it: near sun rays continued through the shells (133) | 6.1 (noon) | 1.1 | ~4-5 / ~0.8 | measured, 133 (`Screenshots/133/perf133.txt`); walk ~1.5 (150) |
| . of it: far-band shadows | 2.0 | 0.35 | ~1.5 / ~0.3 | measured, 93b3 (older build) |
| . of it: shell-hit sun rays | 1.0-3.0 | 1.0-2.4 | ~1-2 | measured, 110 / 93b4 (older builds) |
| . of it: tier-0 sun ray block (trace + reuse lookup + history) | ~6-8 | ~2 | ~5 / ~1.5 | **est.** remainder. **Halving the traced rays saved 0 ms** (124) |
| shell path (`shells_off`: pre-pass + march + shell-hit shading) | 8.8 | 5.5 | ~6-7 / ~3.5-4.4 | measured, 146 |
| far band beyond the shells (`far_off` - `shells_off`) | ~0 | ~2.4 | ~0 / ~2 | measured difference, 146; far primary 0.85 / 0.35 (93b3) |
| FSR 3 (`ups_none`) | 5.7 | 5.4 | **~5.5 / ~5.4 (fixed: 1080p output)** | measured, 146; 432 est. |
| HUD + minimap (`hud_off`) | -0.2 | +0.1 | 0 | measured, 146 (shown or hidden: 0) |
| torch rays, fluid | ~0 | ~0 | ~0 | measured, 110 (day walk has no torches; night 32 torches +5.7 / +8.3, 151) |
| **residual: tier-0 primary march + surface shading + sky probe + haze / grade + motion / TAA buffers + copies** | **~22** | **~13** | **~15 / ~9** | **est.** by subtraction |
| . of it: the vertical sky probe (`TraceShadowRay` per tier-0 hit pixel, EVERY frame, no reuse: ~4x the sun's traced rays) | ? | ? | ? | **never measured** (no arm; `-noskyprobe` is launch-wide only) |
| FULL | 61.0 | 36.1 | ~47 / ~28 | measured / est. |

**Spikes (the p95 / p99 story).** 115 (`Screenshots/115/spike_attribution.txt`): frames > 50 ms AND > local median + 15 ms are 13-16 % of the
walk. 55-60 % are pacing pairs (a long frame then a short one, net ~0). 19-25 % are main-thread streaming (net +4.5..+6.9 ms each).
16-22 % are GPU waits with little CPU work; these are most of the > 100 ms frames, and 53 of 54 / 30 of 34 of them had a shell upload
(> 300 KB: +7.5..+11.5 ms excess, a correlation, not proven). GC is 1-3 %. 123's eviction slicing removed the slide spikes, but crossing frames stay
54-60 ms (GPU wait +10 ms) and p99 did not move (OFF). Counterfactual (every spike set to its local median): p95 75 -> 61-66, p99 ~100 -> 73-78.
**The tail is mostly slow VIEWS, plus upload/crossing frames.**

**150's unexplained half** (~2 ms on the 1.0-2.0 min leg vs 2026-10-22, at 540): still unexplained. Untested differences: `-gamemode survival`,
`-shadowlevel 0 -roofskip 0 -shadowres full`, `-renderrows dyn`, `-memledger -frametiming`, the build delta. At 432 it is est. ~1.5 ms, and the
dyn controller absorbs it. No cut targets it, so it is not ranked; B's launch can add one `-gamemode creative` walk arm if wanted.

**What the evidence says about the kind of cut that pays** (the most useful finding here):
| cut | work removed | frame |
|---|---|---|
| 116 column sky leap / 93b5 mask / 153 height bound / 137 hole leap | iterations -23..-64 % | ~0 each |
| 124 sun reuse 8 | traced sun rays -48 % | ~0 |
| 132 R1 shell pre-pass (march moved OUT of the big kernel, same work) | 0 | **-4.2 / -6.9** |
| 134 R6 kernel diet (unused fields + OFF blocks compiled OUT, same work) | 0 | **-7.1 / -5.5** |
| 130 E1: the same shell march alone vs in the big kernel | 0 | 8.1 vs 18.2 ms (~55 % is context) |

Cuts that remove ITERATIONS inside `CSMainBody` don't pay; cuts that change WHAT IS COMPILED into it, or move work OUT of it, do. The
likely cause is register pressure / occupancy in the 5,600-line kernel (PERF_SHELL_MARCH_PLAN H1; no register counts without a capture).
**The best exact candidates are therefore structural, not algorithmic.** `CSMainBody` still inlines `ShellMarch` 3x (one-pass primary
at ~4585, dead while the pre-pass is ON; near sun continuation ~4916; shell-hit sun ray ~4974) and `TraceShadowRay` 2x (sun, sky probe).

## 2. Exact candidates, per pass

ms = saving at 432 rows, **est.**, near / open / walk p50. "Exact" = raw stills 0 px + a CPU twin or kernel-twin test, as 132/134/137/153.

| id | cut | pass | est. ms | why we believe it / check method | approvals | effort |
|---|---|---|---|---|---|---|
| **B** | **Kernel diet 2.** Compile the play kernels (`CSMainFarShade`, `CSShellPrepass`) WITHOUT what is OFF in play: the one-pass primary `ShellMarch` call (dead with the pre-pass ON), traversal modes 0/2/3 (play = the default 4), the tier 1-2 cascade march (`cascadeAvailable` is false whenever shells draw), debug views 2-11 (134 removed 12-14) + `DebugOut` / `isDebugPixel`, the half-res and rung-1 quad shadow paths (play: `-shadowres full -shadowlevel 0`), roof skip / hole leap / height bound / sky tilt (OFF), dev colours. A twin kernel keeps today's code for every other setting and F6 toggle (134's `ShellDiagKernels` pattern). | all (register pressure) | **1-4 / 1-3 / 1-3** | 134 removed a similar amount for -7.1 / -5.5 at 540. Could be ~0 if the Metal compiler already drops uniform-dead code: only trying it tells. Check: lean vs twin raw stills 0 px at walk_near / walk_open / cliff1, cost split ABBA at `-renderrows 432` | none (Q72 (b) precedent) | 1 |
| **D** | **Incremental roof pyramid, then roof skip ON** (task 59's option (c)). Rebuild the HeightMip pyramid only for the ring columns of mirror chunks written this frame (the CPU is the sole writer and knows them, plus the dilation margin), not every column every frame. | tier-0 sun ray block; **also the vertical sky probe** (a straight-up ray above its column's top exits at once) | **0-3 / 0-1 / 0-1** (more if the sky probe proves costly) | 59: exact (0 px at 9 poses, Gate 3 green); steps -39 %; lost only because the full rebuild cost ~3 ms/frame; skip alone -4.5 ms near noon, -0.5..+0.8 elsewhere (540). Check: incremental == full rebuild byte for byte after streaming + edit ops (test); `roof_on` - `roof_build` arm | none (the pyramid exists; same buffer) | 1-2 |
| **C1** | **Shadow continuation out of the big kernel** (R1 for shadows). The near sun ray's shell continuation (133) and the shell-hit sun ray hand (origin, t, pixel) to a small kernel, like `CSShellPrepass`. A short pass applies the sun visibility. Two of the three `ShellMarch` copies leave `CSMainBody`. | sun: nearsh + shell-hit | **2-4 / 0.5-1 / 0.5-1.5** (+ part of B's effect) | inline cost measured 6.1 / 1.1 (133) + 1-3 (110 / 93b4); 130: a march alone is ~45 % of in-kernel. Risk: exact only if sun visibility enters the shading linearly, or the shading is split: check first. Raw stills 0 px | GPU scratch ~8-12 B/px = **~3-6 MB at <= 540 rows** (owner, as R1's 9.9 MB) | 2 |
| **E** | **Upload pacing / no driver sync.** Cap shell + clipmap upload bytes per frame (FIFO), and write them through a staging ring (`GraphicsBuffer` `LockBufferForWrite`) instead of `SetData` on in-flight buffers. | spikes (p95 / p99) | p50 0; **p95 -2..-8, p99 -10..-20** | 115: > 100 ms frames coincide with > 300 KB shell uploads; crossing frames +10 ms GPU wait (123). Exact AT REST; during streaming a chunk may land 1-2 frames later (already asynchronous). Check: `-spikelog` walk ABBA, > 100 ms count + p99 | owner: "lands a frame later" counts as exact? | 1-2 |
| **C2** | **Deferred sun pass**: the whole sun block (tier-0 trace + reuse lookup + C1's continuation) in its own kernel between the march and the shading. | sun, all | **3-7 / 1-2 / 1-3** (ceiling `sun_off` ~11-14 / ~4) | Same pattern as R1. But the tier-0 part does not scale with traced rays (124), so first find out what it is (B's launch: `reuse1`, `reuse16`, `nearsh_off`, `fshadow_off`). Design task first | scratch ~16 B/px (~5-8 MB) | 3 |
| I | **FSR 3's 1:1 output copy**: FSR writes straight into the camera target (if the formats allow). | copies | 0.2-0.5 (fixed, all views) | 1080p RGBA copy on the M1. Per-encoder GPU trace sizes it (`tools/capture-gpu-trace.sh`, xctrace; Xcode-beta is present) | none | 1 |
| G | Share the sky probe / sun ray within an 8x8 group by origin cell (the ray depends only on the cell): 2-5x fewer rays on near surfaces. | sky probe, sun | 0-1 | 124 says ray count isn't the driver. Only if B's `skyprobe_off` arm shows >= 2 ms and D didn't take it | none | 1 |
| H | Async compute: overlap FSR 3 (bandwidth) with the next frame's march (ALU). | FSR 3 | 0-3 | Unity / Metal async-compute support unverified on this machine (`SystemInfo.supportsAsyncCompute` not logged). Check that first | none | 2 |
| F | R5 chunk-state bitmap (PERF_SHELL_MARCH_PLAN). | shell march | 0-1 | **Not recommended**: 130's E2 found the march execution-bound (second warm march +11.3 / +12.6 ms, as costly as the first), so directory-read latency (R5's target) is not the driver | layout addition | 1-2 |
| -- | Tier-0 primary march vs an ideal DDA | residual | ? | No counter of tier-0 PRIMARY steps exists in play logs (only sun / shell / far). AirMip + occupancy leaps (8.7 / 8.8) are already in. Add a Diag-kernel counter + `RaymarchReference` count only if B-D leave the residual dominant | -- | -- |
| -- | Done or nothing to win | -- | 0 | minimap hidden (146 `hud_off` 0), saved-summary build (moved off the main thread, 113), torch / fluid on the day walk (~0), R2 ray compaction (152: SIMD efficiency 0.91 / 0.93) | -- | -- |

## 3. Ranking and the honest sum

**Rank (est. walk_near ms at 432, confidence):** B 1-4 (medium; best precedent) > C1 2-4 (medium) > D 0-3 (medium-low; up if the sky probe
is costly) > C2 3-7 (low until B's arms explain the sun block; biggest) > E (tail only, p99 -10..-20) > I 0.2-0.5 (certain, small) > G, H, F.

**Sum, B + C1 + D (+ I), est.:** walk_near ~47 -> **~38-44 ms**, walk_open ~28 -> ~25-27, walk p50 32-33 -> **~28-31** (much of it
spent on rows by the dyn controller). **With C2 as well:** near ~33-40.
- **p50 <= 30: plausible** with B-D on top of 159 (159 already sits at 32-33 on battery, likely lower on AC).
- **<= 5 % of frames > 33 ms (p95 ~33): NOT reachable with exact cuts alone (est.).** Today's p95 is 61-75. B-D cut the heavy views by ~3-9 ms,
  E removes part of the spike tail (115's counterfactual: removing EVERY spike only reached p95 61-66), C2 maybe 3-7 more. **p95 lands ~45-55 ms (est.).**
- **The smallest remaining gap: cliff-facing (walk_near-type) views at ~36-44 ms vs 33 after B + C1 + D + E, i.e. -5..-11 ms on those views,
  plus whatever crossing-frame tail E leaves.** Exact options beyond this list would be new design (an amendment-level restructure of
  `CSMainBody` into march / shadow / shade passes, of which C1 and C2 are the first two steps).

## 4. Recommended order (one task per cut, a checkpoint after each, AC power only)

1. **B: kernel diet 2**, and in the SAME launch the missing data (no extra launch): cost split at `-renderrows 432` (needs the 1-line
   `CostRows` clamp -> `UpscalerSelect.CaptureMinRows`, which I wrote and reverted) with arms `diet2_off`, `skyprobe_off` (new, 3 lines:
   `RaymarchFeature.UseSkyProbeAmbient = arm != "skyprobe_off" && base`), `sun_off`, `nearsh_off`, `fshadow_off`, `reuse1`, `reuse16`,
   `roof_on`, `roof_build`, `shells_off`, `ups_none`, `tiny_gate`; plus one per-encoder GPU trace (`tools/capture-gpu-trace.sh`) for the
   pre-pass / main passes / FSR 3 / copies. Outcomes: diet2 >= 1 ms -> keep ON; ~0 -> drop it, and the structural theory weakens (C1 / C2 down-ranked).
   `skyprobe_off` >= 2 ms -> D next, else C1 next. `reuse1` ~ `reuse16` ~ FULL but `sun_off` large -> the sun block's cost is fixed per
   pixel (lookup / history / code), which makes C2 the lever.
2. **D or C1**, picked by step 1's arms (above).
3. **The other of D / C1.**
4. **E: upload pacing** (p95 / p99), judged on a `-spikelog` walk.
5. **Milestone walk on AC** (the next perf milestone; 159 was on battery). Then C2's design task only if near views are still > ~38 ms.
I is bundled into whichever task next touches the upscaler path. G, H, F are not recommended now.
