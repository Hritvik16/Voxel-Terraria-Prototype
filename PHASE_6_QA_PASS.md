# Phase 6 QA pass — 2026-09-11

A findings document, not a verdict. Nothing here says "QA passed" or "QA
failed"; it says what was measured and seen.

**Method.** One rig (`Phase6QaRig`), one mode per process launch, **300 s idle
cooldown before every mode** — this machine throttles ~183% back-to-back.
Wall clock only. No `gpuFrameTime` against any budget (Amendment 8.10), no
Xcode or Instruments (8.9 Rule 1), no Performance State field. 65 s per mode
(130 s for `organic`), screenshots at start/mid/end, all viewed.

---

## 1. Performance matrix — every Phase 6 system under its own heavy use

Fluid has had five sessions of scrutiny. The other five systems had none of
comparable depth. This is the first time anyone has asked whether continuous
digging or back-to-back detonations has its own tail.

| mode | heavy-use case | p50 | p99 | max | preUpdate / update / postLate |
|---|---|---|---|---|---|
| `player` | walk + jump + steps | 11.40 | 16.54 | **1151.09** | 40 / 3 / 56% |
| `destruction` | back-to-back r10 detonations | 11.74 | 14.72 | 231.50 | 63 / 19 / 18% |
| `edits` | continuous dig, MAX tier (bore, 200 vox/s) | 11.82 | 15.07 | 233.37 | 65 / 34 / 1% |
| `projectile` | rapid-fire DDA traces | 11.86 | 14.67 | 229.55 | 84 / 5 / 11% |
| `ccd` | repeated grapple-speed sweeps | 12.26 | 16.93 | 313.16 | 62 / 8 / 30% |
| `ccd+edits` | sweeps through terrain being dug | 12.74 | 17.45 | 233.47 | 57 / 8 / 35% |
| `boundary` | fluid system on, trace fluid | 11.83 | 18.17 | 197.12 | 75 / 24 / 2% |
| `organic` | **sustained brush placement while walking, 130 s** | 13.16 | 21.17 | 217.47 | 58 / 22 / 21% |
| `boom+swim` | detonations while submerged | 13.30 | 22.73 | 222.99 | 59 / 15 / 26% |
| `dig+fluid` | heavy digging into live fluid | 13.59 | 24.00 | 195.98 | 58 / 17 / 25% |
| `buoyancy` | extended swimming | 13.68 | 23.95 | **617.79** | 44 / 4 / 53% |

**No non-fluid system has a tail of its own.** Digging at the maximum tool
tier, back-to-back r10 detonations, and rapid-fire projectiles all sit at
**p99 14.67–15.07 ms** — the *quietest* three modes measured. The concern that
one of them might have been hiding a fluid-sized cost is answered: none is.

---

## 2. A NEW LEAD: the tail does correlate with fluid when systems are isolated

| | p50 | p99 |
|---|---|---|
| six modes with **no fluid** | 11.40 – 12.74 | **14.67 – 17.45** |
| four modes **with fluid** | 13.16 – 13.68 | **21.00 – 24.00** |

**The bands do not overlap.** Fluid costs roughly **+1.5 ms p50 and +6 ms p99**.

Driftcheck twins, cooled:

| mode | p50 A/B | spread | p99 A/B | spread |
|---|---|---|---|---|
| `edits` (no fluid) | 11.82 / 11.82 | **0.0%** | 15.07 / 14.84 | **1.5%** |
| `dig+fluid` | 13.59 / 13.59 | **0.0%** | 24.00 / 23.47 | **2.2%** |
| `organic` | 13.16 / 13.12 | **0.3%** | 21.17 / 21.00 | **0.8%** |
| `projectile` (no fluid) | 11.86 / 13.30 | 12.1% | 14.67 / 16.02 | 9.2% |

Three of four are tight. Projectile's twin ran hot, but even its worst p99
(16.02) stays below the fluid band.

### Reconciling this with last session's toggle sweep

Last session's sweep toggled one system off at a time **inside the full
six-system rig** and found the tail unmoved — `-nofluid` gave the *worst*
figure of eight. That is not contradicted. The two measure different things:

- The toggle sweep measured fluid's **marginal** contribution against a
  baseline where five other systems were already running.
- This matrix measures fluid's **isolated** contribution against a quiet
  baseline.

Both can be true, and together they say something neither says alone: fluid
adds a measurable ~6 ms to p99 **when little else is running**, and that
contribution is **swamped** once the rest of the stack is active. The
larger, still-unattributed tail documented across sessions 2–5 remains
unattributed and remains the dominant term.

**This is a lead, not a diagnosis.** It was not chased further this session.

---

## 3. Pairwise interactions — no interaction effect found

| pair | p99 | vs the worse of its two parts |
|---|---|---|
| `ccd+edits` | 17.45 | ccd 16.93, edits 15.07 → **+0.5 ms** |
| `boom+swim` | 22.73 | destruction 14.72, buoyancy 23.95 → **−1.2 ms** |
| `dig+fluid` | 24.00 | edits 15.07, buoyancy-as-fluid-proxy 23.95 → **+0.05 ms** |

Every pair lands at or just below its worse component. **No pair is worse
than either alone**, and none approaches the full six-system combined rig.
Nothing interacts badly.

---

## 4. Visual QA

Screenshots viewed at start/mid/end for every mode. Sorted as asked:

### 4a. Genuine defects — fixed
**None found.**

### 4b. Genuine defects — found, not fixed, flagged for a decision
**None found.**

### 4c. Expected behaviour, documented so it is not re-reported as a bug

- **Scattered static water near the radius edge — EXPLAINED, not a defect.**
  This was the specific item to investigate. `boundary` mode poured a line of
  water straddling the wake radius and censused every mobile voxel against
  tile residency:

  ```
    inside radius, HAS tile     0
    inside radius, NO tile      0   <-- a wake failure would appear here
    outside radius, no tile     0
    outside radius, HAS tile 1123
  ```

  **Zero wake failures.** All 1123 voxels sit in the **hysteresis band**:
  past the wake radius (1280) so they do not promote, but inside the sleep
  radius (~1478) so their tiles are not yet released. They are supposed to
  sit still. That band is 198 voxels (~20 m) wide, which is why a scatter of
  motionless water near a boundary is visible and correct.

- **Unsupported voxels after destruction** — already resolved as normal for
  this genre; the `destruction` end frame shows an eroded mass with a
  connected overhang, no disconnected floaters beyond that accepted case.

- **Terrain**: no holes, no z-fighting, no missing LOD tiers, no seams at
  chunk boundaries in any mode.
- **Fluid**: water/sand/lava/obsidian all render with distinct correct
  colours; **obsidian appears exactly at lava–water contact** in `organic`,
  confirming §7.3's reaction visually; no fluid rendered outside where it was
  placed; no popping or flicker at tile or radius boundaries.
- **Player/physics**: no clipping through terrain, no jitter at rest, camera
  stable through grapple-speed CCD passes.
- **HUD/UI**: all panels legible under load, no overlap, and the **build
  stamp renders correctly** (`commit … built …`).

### 4d. Observations worth recording, not defects

- **`player` mode produced a single 1151 ms frame**, and `buoyancy` a 617 ms
  one. Both are single outliers far above their p99 (16.54 and 23.95); both
  modes are **postLate-dominated** (56% / 53%) where every other mode is
  preUpdate-dominated. Not investigated — it is one frame in 3900, and the
  standing instruction is not to re-chase the tail.

---

## 5. Open items for a human

1. **The fluid p99 lead** (§2). Worth chasing or not? It is ~6 ms against a
   quiet baseline and is swamped under full load.
2. Nothing else. No defect was found that needs a decision.

## 6. What this pass did not cover

- GPU-stage attribution — impossible on this toolchain (standing limitation).
- The large unattributed tail — explicitly out of scope this session.
- Disk/file cleanup — explicitly deferred.
