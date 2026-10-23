# PERF_SHELL_MARCH_PLAN.md -- why the shell march is slow on the M1 GPU (task 128, 2026-10-23, PLAN ONLY)

Owner Q80 (a). No code, build or launch was made for this document. Experiments below are proposals; none is started.
Sources: KNOWN_ISSUES "SHELLS COST", HANDOFFs 93b3/93b4/93b5/99/115/116/117/123, TASKS 118/124, `Raymarch.compute` `ShellMarch`
(~3067-3250) and its primary call in the kernel (~4198), and the counter lines of the existing cost-split logs (re-read, not re-run).

## 1. What the evidence already says

Settings everywhere: play (FSR 3 at 540 rows = 960x540 = 518,400 px, High, far band + far shading, noon), warm, AC power.

- **The cost is real and is the primary march.** Shells not drawn vs ON: 50 -> 66 ms (walk_near), 30 -> 48 ms (walk_open), i.e. +16 / +18 ms
  (93b re-run). `shellmarch_off` (primary shell march + the far march after it skipped) saves +13.7 / +19.1; the shell-hit sun shadow
  +3.0 / +2.4 (noise level); far band after the shells ~0 (93b3); far shading kernel vs plain `CSMain` with shells ON -0.1 / +1.2 (93b3
  `fshade_off`: the play kernel's diagnostic extras are not the cost). Frames are GPU-bound (115: 28 of 34.5 ms is the GPU wait; 123).
- **Not display pacing.** `vSyncCount: 0`; the 115 walk CSVs (`Screenshots/115/walk{1,2}_spikes.csv`, 7,900 frames) spread evenly over
  the 16.7 ms phase (each tenth 310-377 frames). Checked offline for this plan.
- **Work vs cost does not line up at the POSE level.** Today's default march (column leap OFF; `Logs/costsplit_2026-10-22_202941.txt`):

  | pose | shell rays/frame | iterations | directory reads | page reads | cost | ns per iteration (frame-wide) |
  |---|---|---|---|---|---|---|
  | walk_near | 30,209 sky (5.8 % of px) | 2.48 M | 0.42 M | 1.86 M | +16 ms | ~6.5 |
  | walk_open | 31.8 k hit + 290.9 k no-hit | 12.41 M | 4.34 M | 6.08 M | +18 ms | ~1.4 |

  walk_near does 5x fewer iterations and 10x fewer reads for the same ms. If 30 k rays x 82 iterations cost 16 ms, then at ~1,000 resident
  lanes each iteration would take ~50 us. That only works if very few SIMD groups are in flight and each one waits on long latency chains.
- **Inside a pose, distinct DIRECTORY reads track the ms; iterations and page reads do not.** Directory reads = `ShellResolve` calls after
  93b4's one-entry cache, i.e. ~one per CHUNK a ray enters. Computed from the counter lines of the logs named in each HANDOFF:

  | change (task) | iterations | directory reads | page reads | p50 change, ms (noise); minus = faster |
  |---|---|---|---|---|
  | sky-ray exit (99) near / open | -13 % / -18 % | **-26 % / -38 %** | -13 % / -16 % | **-4.7 (0.9) / -2.6 (0.6)** |
  | air-brick mask (93b5) near / open | -55 % / -29 % | -3 % / 0 % | -- | +2.8 (0.1) / +0.6 |
  | column sky leap (116) near / open | -64 % / -23 % | +1 % / 0 % | -85 % / -46 % | +1.9 (pooled; OFF passes 4 ms apart) / -0.2 |
  | Medium preset (117) open | +1 % | -11 % | -- | -0.8 (0.3-1.6) |
  | read caches (93b4) | 0 | -78 % / -49 % (re-reads of the SAME entry) | 0 | -2.6 / -0.6 |

  The cache row is not a contradiction: it removed re-reads of an entry the same lane had just read, which hit L1. The pattern fits a
  march whose time is set by the chunk crossings and their dependent read chain (directory -> page -> body, each address depending on
  the last), not by the brick steps between them. Four cuts is a small sample, so this is a correlation, not a proof.
- **Leap ALU is not free.** 93b5 (b)'s exact exit-plane leap alone, same iterations, cost +5.0 / +6.1 ms. 116's column leap swapped
  ~1.6 M cheap brick leaps (walk_near) for 0.21 M heavy ones (`ShellBoxLeapOk` + `ShellLeapBox` + 1-4 more page reads each) and broke even.
- **The 3 null cuts did not change hit rays.** walk_open's 6 % hit rays take 102-106 iterations in every arm.
- **124 points the same way outside the shells:** halving the voxel sun rays traced (126 k -> 66 k a frame) saved nothing measurable.
  Ray count is not the cost driver anywhere we have looked.

## 2. Hypotheses, ranked

| # | hypothesis | how it would show | supports | contradicts | rank |
|---|---|---|---|---|---|
| H1 | **Kernel context: low occupancy / register spills.** `ShellMarch` is inlined into the 5,600-line primary kernel, with the caller's live state (ray, tier-0 result, fluid pass, shading inputs) held across its loop. ~60 live values in the loop itself (my estimate from the code). Few SIMD groups per core, so every dependent read's latency is exposed. | Per-iteration cost far above its ~150-250 instructions. Cost hardly scales with ray count (near ~ open). Heavier ALU per step hurts (+5-6 ms). | Pose table (6.5 vs 1.4 ns per iteration). 93b5 (b). 116 broke even. | Nothing measured. Note: the arms in one build all share one register allocation, so this can only show as a MULTIPLIER on H2/H5, never as an arm delta. | 1 |
| H2 | **Dependent memory latency per chunk visit.** `ShellDir` ring (random), then a 16 KB page in `ShellPages`, then `ShellBricks`. Hundreds of MB, so cache and GPU-TLB misses. `ShellResolve`'s hole descent adds serial reads. | ms track distinct chunk visits, not iterations or bytes. | The directory-read table (99 vs 93b5/116/117). | Bandwidth can't be it: near reads 10x less for the same ms. So it is LATENCY, which only hurts if H1 holds too. | 2 |
| H3 | **SIMD divergence.** (a) Trip count: 32 lanes wait for the longest ray, e.g. a 106-step hit ray among 20-31-step sky rays at walk_open. (b) Path: each iteration has 5+ branches (tier-0 leap, resolve, uniform chunk, page, cell / hit, leaps) that serialize. | Low SIMD efficiency (sum of lane steps / (32 x the longest lane's steps)). | walk_open mixes ray kinds. 116 cut sky rays, not the hit rays that set a group's length. | walk_near's sky patch is coherent (every lane a sky ray, ~82 steps) yet costs as much. So it can't be the main cause there. | 3 |
| H4 | **ALU per iteration.** `ShellByDistance` loop each step, `ShellResolve`'s per-shell loops with constant arrays indexed per lane (`_ShellLo/Hi/Cam/Ring[k]`), the closed-form leap with 3 `ReseedTMaxAxis`, `ShellTier0Holds`. | Cost moves with the leap formula. | 93b5 (b) +5-6 ms. | Per-iteration cost differs 4.6x between poses with the same code. | 4 |
| H5 | **Work after the hit, charged to the shells** (shading, shell-hit shadow, far shading, haze). | Cost scales with hit pixels. | -- | walk_near has 26 hit rays a frame and still +16 ms. shellshadow_off is at the noise level, far arms ~0. | 5 (out) |
| H6 | Thread-group shape / ray order (Morton, tiles). | Coherence gains from reordering. | -- | Groups are already 8x8 screen tiles (32-lane SIMD = 8x4). Morton inside a tile changes little. Ordering only matters through H3. | 6 (fold into H3) |
| -- | Bandwidth-bound; vsync / pacing. | -- | -- | Ruled out above (10x reads, same ms; frame-time phase histogram flat). | out |

**What I could not determine without a GPU capture (Metal frame debugger, not in this workflow):** the kernel's register count, spills
and occupancy; per-dispatch GPU time (we only see frame time); cache and TLB hit rates; real SIMD efficiency. Also whether the
base kernel (shells off) is itself occupancy-starved. ~30-50 ms for a 518 k-pixel frame hints at it, but FSR 3, shadows and the far band
share that time.

## 3. Experiments (one build, capture-only arms, no default or look change)

All three fit in ONE player build, ONE warm-up and ONE or TWO `-costsplit` launches (F X X F at walk_near and walk_open). E3 needs only
the counter window.

**E1 -- split-kernel probe (tests H1).** A capture-only kernel `CSShellProbe` in `Raymarch.compute`, 8x8 like the main one, calls the
same `ShellMarch` and writes only `t` / end / iterations to a scratch buffer. Arm `shellsplit`: the main kernel writes each pixel's
`tTier0` and a "needs shells" bit (it already knows both at ~4196) and skips its own march (`shellmarch_off`'s path). Then the probe runs
the march for the flagged pixels, with identical rays and identical iterations.
- A: `shellsplit` - `shellmarch_off` <= ~1/3 of `FULL` - `shellmarch_off` -> the march is cheap on its own and the mega-kernel context
  (H1) is most of the cost. Next: R1 (+R2). Best case ~10-14 ms.
- B: about the same as in-kernel -> the march is slow by itself (H2/H3/H4). Read E2 and E3.
- Cost: ~150 lines (kernel, 2 buffers ~2 MB at 540 rows, per-kernel bindings, the arm). The image is wrong in this arm (misses draw sky):
  timing only.

**E2 -- cold vs warm march (H2 vs H3/H4).** Arm `shellsplit2`: the probe marches every flagged pixel TWICE. The second call's start t gets
a uniform salt of 0, so the compiler can't merge the two calls. The second march repeats the first's addresses with warm caches/TLB.
- A: the second march adds >= ~3/4 of the first's cost -> execution-bound (ALU / divergence). Memory-layout work is dropped. Next: E3's
  answer, then R2 / R6.
- B: it adds <= ~1/3 -> first-touch misses dominate (H2). Next: R5 (no directory read for absent / hole / uniform chunks), then R4.
- Running it inside the small probe kernel keeps H1's register pressure out of the comparison. Cost: ~15 lines on top of E1. Gain: none
  by itself; it picks between R5 and R2.

**E3 -- divergence counters (H3; no timing).** In the counter window, per 8x4 half-tile (= one 32-lane SIMD if threads are numbered x
first), use `groupshared` `InterlockedMax` / `InterlockedAdd` of shell iterations to give SIMD efficiency = sum / (32 x max). Also count
how many path kinds each SIMD saw per iteration. No wave intrinsics are needed (SM6 is unavailable here).
- A: efficiency >= 0.6 at both poses -> divergence is out; R2 is not worth designing.
- B: <= 0.35 at walk_open -> R2 (compaction / sorting by ray kind) is the walk_open lever. walk_near is expected to stay high. If it
  doesn't, the per-step branch union is the problem (R6).
- Cost: ~40 lines, counters only.

Dropped, because every outcome leads to the same action:
- a `-farshade 0` launch (93b3's `fshade_off` already answers it);
- re-timing ray counts (116 / 117 / 124 answered it);
- a post-hit shading arm (walk_near has no hits).

**Machine cost of E1-E3 together:** one build (~15-20 min), one warm-up (~6 min), one or two cost-split launches (~20 min each), and the
fast suite. About 1.5 h of Unity time plus ~half a session of coding.

## 4. Candidate restructures (after the experiments; none is approved)

None of these touches simulation (amendment 8.15: fluid / edits / physics stay NEAR 128 m on every preset). None touches a frozen API
(`IWorldQuery`, `IEditService`, D.1, §4.4). All are render-internal.

| | restructure | fixes | gameplay consequence | layout / memory | owner questions |
|---|---|---|---|---|---|
| R1 | **Shell pre-pass kernel**: main kernel = tier-0 march + flags; a small kernel marches the shells; a shade pass finishes those pixels (E1 made real). | H1 | None: same image, exact. Far edits stay 3D (Q66 (a)): the shells still draw them. | +~10 MB GPU scratch at 540 rows (~18 MB at 720). One more full-screen pass (~0.5-1 ms). | Accept +10-18 MB under the 3 GB cap (play peaks at ~2.4 GB today)? |
| R2 | **Ray compaction**: R1's pre-pass appends flagged pixels to a list, sorted into buckets (sky / likely hit, from last frame's end). `DispatchIndirect` over the list. | H3, and walk_near's underfilled tail | None (exact). | +4 B/px list. Needs R1. | Only with R1. |
| R3 | **Half-res shell march + depth-aware upsample** (full res only on edges). | ray count | Far terrain softer. Thin far features (rivers, ponds, small far edits) can break up, exactly what 117 / 127 flagged. Overlaps 129's 0.1 m comparison and FSR 3 (already 540 -> 1080). | Small. | Is softer terrain past ~70 m acceptable? NOT recommended: the evidence says cost does not scale with ray count (near ~ open, 124). |
| R4 | **Temporal shell-hit reuse**: start the march just before last frame's reprojected hit. | chunk visits (H2) | Not exact. A hill streaming in, or a far edit or summary redraw, could appear a frame late unless invalidated. Disocclusion falls back to the full march. | Per-pixel history ~4 B/px. | Accept a 1-frame lag at distance? 124's shadow reuse halved rays for 0 ms, so only after E2 = B. |
| R5 | **Chunk-state bitmap per shell** (2 bits: absent / hole / uniform air / paged) in a small buffer. Resolve and the hole descent read it instead of the directory ring. Uniform-air chunks are crossed without the ring read. | H2 | None (exact). | New GPU buffer, KB-sized (like 93b5's mask). Owner approval for a layout addition. | Approve a ~tens-of-KB buffer? |
| R6 | **Kernel diet**: hoist `ShellByDistance` to a per-band test, shrink `ShellHit`, move the 93b4-116 diagnostic fields and counters to a capture-only variant, and remove the OFF-by-default mask / column-leap / plane-leap blocks from the play kernel (Q72 (b)). | H1 / H4 | None (exact). | None. | Q72 (b): drop the dead OFF paths from the play kernel? |

## 5. Recommended order (at most 3; one checkpoint after each)

1. **E1 + E2 + E3 together**, as ONE capture-only task (one build, one warm-up, F X X F at both poses). E1 is the decisive one: it is the
   only test of H1, and its code is R1's first half.
2. Then exactly ONE restructure, picked by the results: E1 = A -> R1 (then R2 if E3 = B); E1 = B and E2 = B -> R5; E1 = B and E2 = A -> R6, plus R2 if E3 = B.
3. One warm ABBA of that restructure vs today's default (`MODE=walk` + cost split) before anything else changes.

**Question for the owner (Q81):** how should we look into the shell march's cost?
- **(a) Recommended.** Approve the probe task (E1-E3: one build, capture-only arms, ~1.5 h of Unity time, no look or default change),
  then decide on ONE restructure from its result.
- (b) Skip the probes and trial R1 (shell pre-pass kernel, +10-18 MB GPU) directly. Faster if H1 is right, wasted if it is not.
- (c) Only E3 (divergence counters, no timing). Cheapest, but it cannot test H1, which ranks first.
- (d) Stop here: accept the +16-18 ms standing-still cost and move on to lighting / world gen.
