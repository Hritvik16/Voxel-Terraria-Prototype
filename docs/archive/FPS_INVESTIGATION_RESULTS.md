# Horizon FPS investigation — 2026-09-11/12

**Reported symptom:** "there seems to be a drop in fps when looking at the
horizon."

**Outcome: the horizon was not the cause, and no fix was required.** Once
measured correctly the scene sustains **~69.8 FPS ± 0.4** at the full shipped
fluid radius — a **16% margin** over 60 FPS. No engine constant was changed:
`FLUID_ACTIVE_RADIUS_VOXELS` 1280 (128 m), `MAX_ACTIVE_FLUID` 750,000,
`FLUID_TILE_POOL_CAPACITY` 1024, LOD tiers, tier ranges and render distance
all exactly as specified.

> ### A correction that is part of the result
>
> Midway through, this investigation reported a fix: reverting Playground's
> demo `_activeRadiusVoxels` from 1280 to 128, measured at 65.5 → 75.75 FPS.
> **That was a unit error and the "fix" has been undone.** Voxels are 0.1 m:
> 1280 voxels is **128 metres**, while 128 voxels is **12.8 metres** — the two
> share their digits and differ by a factor of ten. The speedup was real
> arithmetic reached by simulating a tenth of the radius, which is exactly the
> fluid-scale compromise the project forbids.
>
> The field now **references** `EngineConfig.FLUID_ACTIVE_RADIUS_VOXELS`
> rather than restating it, and every place a radius is displayed now shows
> metres beside voxels ("1280v / 128m"). See §2.

---

## 0. Read this first — three measurement traps

**Every performance number taken in this project's player builds is suspect
unless it was taken the way §0.4 below describes.** Three separate traps were
found in one night, and each one produced a confident, wrong answer before it
was caught.

### 0.1 Windowed capture is worthless on this machine

Identical back-to-back runs of the same binary with the same flags:

```
rep1 nofluid   69.6 FPS      rep2 nofluid   26.9 FPS
rep1 fluid    108.4 FPS      rep2 fluid     82.5 FPS
```

A 2.6× swing between repetitions, and **"fluid ON" measuring faster than
"fluid OFF"** — an inversion with no physical reading. The macOS compositor
throttles a windowed, non-frontmost or occluded surface, and that dominates
everything else.

**Fullscreen reproduces to 2.9%** (66.5 / 68.3 / 68.4 across three runs) and is
the only valid mode. `-fullscreen` on the probe; it is the default for
`-pitchsweep`.

### 0.2 Percentile frame time is misleading here — the display is 120 Hz

The app presents faster than the display can show, so a large fraction of
frames block in `present` and land in the tail **by pacing, not by cost**. The
tell was unmistakable: four different isolation arms — full kernel, stripped
kernel, windowed, fullscreen — each returned **exactly 600 of 1800** frames
over 16.6 ms. Exactly one frame in three, regardless of workload.

Compounding it, this is an **Apple M1 with a 120 Hz ProMotion display**. Any
reading near 120 FPS is the refresh cap, not headroom — the "fluid ON = 122
FPS" numbers are the cap being hit, not fluid being free.

**Use frames ÷ elapsed wall time.** The probe reports it as
`TRUE SUSTAINED RATE` and the percentiles are kept only as diagnostics.

### 0.3 The first pitch sweep confounded pitch with fluid accumulation

The sweep opened the vents and then visited 25 pitches in order, so later
pitches had been pouring for 70+ seconds longer than earlier ones. It produced
a clean, convincing, **wrong** result:

> looking down 105–132 FPS, horizon and above a flat ~69 FPS — "the horizon
> costs 2×"

Running the identical sweep **in reverse order** did not reproduce it, and
running it with fluid off removed it entirely. What looked like a property of
pitch was a property of *when in the sweep* the pitch was measured.

### 0.4 The method that survived

- **Fullscreen only.**
- **frames ÷ elapsed wall time**, never p50/p99, as the headline.
- **Counterbalanced ordering** (A B B A, or forward + reverse), because a
  ~7% position/thermal decay per run-sequence is present and will otherwise be
  read as an effect.
- **Both directions of every sweep**, for the same reason.

---

## 1. The horizon is not a cliff — it is ~7%

With fluid off, fullscreen, the frame rate is **flat across all 25 pitches**
from −80° to +40°:

| pass | range |
|---|---|
| forward | 62.4 – 64.6 FPS |
| reverse | 57.6 – 61.5 FPS |

The ~4 FPS gap *between* passes is position/thermal decay, not pitch.

At the shipped 960×540 gate this is partly the display cap, so the sweep was
repeated at a forced 1920 gate to push GPU cost well above one refresh
interval. Counterbalancing the two orders:

| pitch | forward | reverse | mean |
|---|---|---|---|
| −80 | 24.0 | 21.8 | 22.9 |
| −40 | 24.0 | 21.8 | 21.9 |
| −20 | 20.8 | 22.1 | 21.5 |
| 0 | 20.4 | 22.4 | 21.4 |
| +20 | 21.6 | 22.2 | 21.9 |
| +40 | 22.5 | 22.5 | 22.5 |

**The horizon is ~7% dearer than looking down.** Real, and worth knowing, but
not the reported symptom. A 13% step that appeared at −20 in the forward pass
did not reproduce in reverse.

Ray length *is* a genuine cost — capping iterations 1024 → 256 → 128 → 64 gives
69.5 → 79.2 → 89.0 → 99.4 FPS — but it saturates by 512, and capping truncates
render distance, which is non-negotiable. **Not a usable lever.**

---

## 2. There was no root cause to fix — plus the unit error, in full

### 2.1 What the scene actually does, measured correctly

Five repetitions, fullscreen, frames ÷ elapsed wall time, at the **full
shipped radius** (1280 voxels / 128 m):

| condition | FPS |
|---|---|
| moving 12 m/s | 69.4, 69.8, 69.8 |
| static | 70.2, 69.7 |
| **mean** | **69.8 ± 0.4** — mean frame 14.33 ms |

**2.34 ms inside the 16.67 ms a 60 FPS frame allows: a 16% margin.** Stable
over a 102 s run (last fifth vs first −2.4%).

### 2.2 The unit error, and why it produced a convincing wrong answer

§7.4's wake radius decides how many §7.2 tiles stay resident, and the CA's
three region passes (`CSClear`, `CSCommit`, `CSWakeScan`) dispatch over
`activeTileCount × 32768` cells every tick. So the radius really does drive a
large cost, and shrinking it really does raise the frame rate:

| radius | | FPS |
|---|---|---|
| 1280 voxels | **128 m — shipped** | 65.5 (short hot runs) / **69.8 (five cool runs)** |
| 128 voxels | 12.8 m — a tenth | 75.75 |

The measurement was sound. The **interpretation** was not: 128 was read as
"the historical demo value, therefore a legitimate setting" when it is a 10×
reduction in simulated fluid radius. A real speedup obtained by deleting
90% of the work is not a fix.

An earlier figure of 65.5 FPS for the shipped radius also turns out to be low
— it came from 20-second runs taken hot during a long sequence of builds. The
five cool repetitions in 2.1 disagree with it and are the ones to trust.

### 2.3 What was changed so it cannot recur

- The demo field **references** `EngineConfig.FLUID_ACTIVE_RADIUS_VOXELS`
  instead of restating it, so it cannot drift from the spec.
- Any future reduction must be written as a visible fraction of the constant
  (`/ 10`), so it reads as a reduction in review.
- **Metres appear beside voxels** wherever a radius is shown: the HUD reads
  `1280v / 128m wake, 1472v / 147.2m sleep`, and the placement-refusal message
  does the same.
- `EngineConfig`'s constant carries the unit — and this incident — in its own
  comment.

## 3. Two fixes tried and REJECTED — do not re-attempt these blind

Both looked correct, both were measured, both were reverted.

### 3.1 Reusing tier 0's air-mip in the tier 1/2 path — 67.0 → 44.9 FPS

The tier 1/2 path has no air-mip pyramid (a documented scope cut), so empty
coarse bricks are skipped one at a time while tier 0 leaps `8<<k` spans.
Amendment 8.11 §1 establishes tiers share tier 0's extent, and tier data is
downsampled from the same voxels, so consulting tier 0's mip is *sound*.

**It was still much slower.** Checking the mip at the top of the tier path
preempted the tier path's larger brick skip with a smaller mip leap, and paid
up to four mip reads per iteration on occupied bricks where nothing was
skipped at all.

**If retried:** only consult the mip on the already-empty path, and only accept
a level whose span strictly exceeds the skip it replaces.

### 3.2 Mirroring tier 0's dense inner loop into the tier path — 66.0 → 61.4 FPS

The tier dense path samples one coarse voxel then `continue`s to the outer
loop, so crossing an 8-coarse-voxel brick costs 8 full outer iterations
(bounds test, tier selection, `ReadClipmapTier`, handle decode). Tier 0 uses an
inner loop instead. Mirroring that structure looked strictly better.

It is not. Built **old and new side by side as two apps** and run A/B/B/A in
one session:

| | gate 1920 | shipped gate |
|---|---|---|
| OLD | 24.1 / 24.2 | **66.0 / 66.0** |
| NEW | 23.0 / 23.0 | **61.4 / 61.0** |

**−7.3% at the shipped gate.** Reverted.

This also produced a lesson: an earlier reading suggested the change had
improved the cascade's overhead from 16.8% to 6.7%, but that comparison was
across sessions and the *cascade-OFF* arm had drifted 27.15 → 24.5 FPS on a
code path the change cannot touch. **Only same-session A/B is trustworthy.**

---

## 4. Ruled out by measurement

- **Per-slot dispatch size.** Cutting `MAX_ACTIVE_FLUID` 750,000 → 8,192 — a
  91× smaller dispatch for `CSReact`/`CSIntent`/`CSSweep`/`CSRecycle` — changed
  the frame rate **not at all** (65.6 vs 65.5). This also clears the previous
  session's ceiling raise of any blame.
- **GC**: +1 gen0/gen1/gen2 across the whole p99 band.
- **Uploads**: 0 bytes/frame and 0 SetData calls/frame in the band.
- **CPU update work**: `update` is 0.5% of the p99 band; `preUpdate` ~10%.

---

## 5. Remaining headroom — ranked, DEFERRED, not urgent

We are at **69.8 FPS sustained** at the full shipped radius, 16% above the
60 FPS target. Neither item below is needed to hold 60, and both are
Phase-sized.

### 5.1 Fluid CA region passes — the largest single cost in the frame

The three region passes scale with **resident tiles**, not with live fluid, so
settled ambient water is swept every tick forever. Suspending the CA entirely
measured **87 FPS** against 65.5 with it running, so this is the dominant
single cost in the frame — bigger than the raymarcher and far bigger than the
horizon. It is also why the radius has such leverage (§2.2). The fix is to bound them to
tiles that actually hold live cells — `FluidTileMap` already tracks `_slotLive`
per tile, so the information exists.

**Why it was not done tonight:** it touches the §7.3 CA and the §3.9 CPU/GPU
sync contract, and the failure mode is fluid that silently stops simulating —
the exact defect class this project has fought repeatedly. It needs its own
session, its own oracle, and the existing fluid rigs as gates.

### 5.2 LOD cascade air-mip gap — ~17% at high resolution

`nocascade` measures 27.15 vs 23.25 FPS at a forced 1920 gate
(counterbalanced), so the cascade **costs** 16.8% where it should save. Cause
is known and documented in the shader: tiers 1/2 have no air-mip pyramid.
Both attempts in §3 failed. A correct fix must extend skips without ever
shrinking one.

---

## 6. NOT the same thing as the original reported drop — read this before claiming anything fixed

Two different symptoms have been discussed and they must not be conflated.

**(a) This investigation — "FPS drops when looking at the horizon."** Measured
flat across pitch (§1), ~7% at most once isolated. **Nothing was broken and
nothing was fixed.** The scene sustains ~69.8 FPS.

**(b) The ORIGINAL reported drop — 48.9 → 35.2 FPS under heavy sustained fluid
load during active play. THIS SESSION DID NOT FIX THAT, and it must not be
reported as fixed.** That symptom is the separately documented frame-time
tail: chased across several sessions, attributed as far as this toolchain
permits, and **permanently unattributable beyond that point** because §2.2's
budget lives on the GPU lane and no per-kernel timing exists here (no Xcode by
Amendment 8.9 Rule 1, `gpuFrameTime` inflated ~2.6–2.7× by Amendment 8.10,
Unity merging the CA's dispatches into a single encoder). See
`PHASE_6_COMPLETION.md` open item 4 and §7 of its open-items list.

**(c) A mild stutter during sustained play**, reported separately. **It was not
isolated or chased this session.** If it persists it is a candidate for exactly
the methodology in §0.4 — fullscreen only, frames ÷ elapsed-time as the metric,
counterbalanced A/B — and specifically *not* percentile frame time, which §0.2
shows will mislead on this hardware.

## 7. The instrument

`PlaygroundCapture` gained two modes and a set of isolation levers, all in the
real gameplay scene:

| flag | what it does |
|---|---|
| `-pitchsweep` | frame cost vs camera pitch, −80° to +40° |
| `-pitchreverse` | same sweep reversed — breaks the ordering confound |
| `-stutterprobe` | single pose, with `FrameGapProbe` attribution |
| `-fullscreen` | **required for any valid number** (§0.1) |
| `-gateres N` | force internal render resolution, to measure above the display cap |
| `-maxiter N` | per-ray iteration cap — bounds ray length |
| `-noairmip` / `-nocascade` / `-nosim` | disable one accelerator or the CA |
| `-movespeed N` | fly forward, so streaming is live |
| `-nofluid` | skip opening the vents |

`Playground.DebugSuspendFluidSim` is a measurement seam, not a configuration.
