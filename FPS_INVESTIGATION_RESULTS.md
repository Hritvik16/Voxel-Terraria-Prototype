# Horizon FPS investigation — 2026-09-11/12

**Reported symptom:** "there seems to be a drop in fps when looking at the
horizon."

**Outcome:** the horizon was **not** the cause. Sustained frame rate went
**65.1 → 75.6 FPS** by reverting a Playground demo change made the previous
session. No engine constant was touched: `FLUID_ACTIVE_RADIUS_VOXELS` is still
1280, `MAX_ACTIVE_FLUID` 750,000, `FLUID_TILE_POOL_CAPACITY` 1024, LOD tiers,
tier ranges and render distance all unchanged.

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

## 2. ROOT CAUSE — the demo active radius, via resident tile count

§7.4's wake radius decides how many §7.2 tiles stay resident. The CA's three
region passes — `CSClear`, `CSCommit`, `CSWakeScan` — dispatch over
`activeTileCount × 32768` cells **every tick**, whether or not that fluid is
moving.

The previous session raised Playground's own `_activeRadiusVoxels` from **128
to the shipped 1280** to "showcase the shipped config". In this scene that made
**~486 tiles resident** — about **15.9 M cells per pass, ~48 M threads per
tick** — to service roughly **1,700 live voxels** of ambient ocean water that
is sitting still.

| configuration | FPS |
|---|---|
| radius 1280 (previous session) | 65.5 |
| **radius 128 (reverted)** | **75.75** |
| CA suspended entirely | 87 |

Two runs each, reproducing to 0.3 FPS.

**Sustained check**, moving at 12 m/s, fullscreen, shipped gate:

```
before   10800 frames in 165.84 s = 65.1 FPS   (mean frame 15.36 ms)
after     5400 frames in  71.45 s = 75.6 FPS   (mean frame 13.23 ms)
flat: last fifth vs first fifth -0.3%
```

15.36 ms left only **1.24 ms of margin** against the 16.6 ms a 60 FPS frame
allows — which is why a warmer or busier run dipped under, and why the reverse
pitch sweep saw 57.6 FPS.

**The engine constant was not changed.** `FLUID_ACTIVE_RADIUS_VOXELS` remains
1280 and every rig still runs it. Playground's local value was 128 for the
project's entire history before the previous session; the comment at the field
records the measurement and how to put it back.

---

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

We are at **75.6 FPS sustained**, 26% above the 60 FPS target. Neither item
below is needed to hold 60, and both are Phase-sized.

### 5.1 Fluid CA region passes — worth ~11 FPS (75.6 → 87)

The three region passes scale with **resident tiles**, not with live fluid, so
settled ambient water is swept every tick forever. The fix is to bound them to
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

## 6. A mild stutter, reported and NOT chased

A mild stutter was reported during sustained play. **It was not isolated or
chased this session.** If it persists, it is a candidate for exactly the
methodology in §0.4 — fullscreen only, frames ÷ elapsed-time as the metric,
and counterbalanced A/B — rather than percentile frame time, which §0.2 shows
will mislead on this hardware.

---

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
