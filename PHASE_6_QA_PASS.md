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

---

# 7. THE LATE-GAME SIEGE — heavy fluid AND all six systems, sustained 200s

*Added 2026-09-11, after sections 1–6. This is a separate scenario, not a
re-run of the matrix above: everything in §1–§3 tested **one axis at a time**
— systems alone, systems in pairs, fluid at modest volume. The siege is the
first run where **large-scale fluid volume and late-game gameplay intensity
happen together, for long enough that a slow problem has room to appear**.*

Rig `Assets/Game/LateGameSiegeRig.cs`, script `./run-late-game-siege.sh`,
IL2CPP RELEASE standalone, wall clock (`Time.unscaledDeltaTime`) only.
Two runs, **300s idle cooldown before each**, 200s siege + 300-frame quiet
settle, 12,000 measured frames apiece. `gpuFrameTime` is quoted nowhere
against a budget (Amendment 8.10).

## 7.1 What was actually running, simultaneously, for the whole 200s

| | |
|---|---|
| Fluid volume | **~322,000 live voxels held continuously** (band 150K–320K) |
| Pour | closed-loop, throttled on 11,653 of 12,000 frames |
| Destruction | 10 detonations, r=40/30/20, **642,802–650,538 voxels** |
| Editing | 193,965–193,969 placed, 207,707–214,125 dug |
| Player | 12,000 motor steps, continuous traversal |
| CCD | 40 grapple-speed swept passes at a wall |
| Projectiles | 100 traces |
| Buoyancy | 65–112 wet-body frames |
| Total | **7.20–7.27 M voxel writes**, 3.65–3.68 M fluid ops |

Three of the ten detonations fire **inside the water**, so lava, water and
fresh debris are colliding while the player is moving through it.

**The pour is closed-loop on purpose**, and that is what makes §7.2's
question answerable. A fixed pour rate either never reaches the target band,
or slams into `MAX_ACTIVE_FLUID` within seconds and spends the rest of the
run measuring the clamp rather than the load. Topping up only when live
volume falls below the band held it at ~322K for the full run — the segment
table below shows `live avg` pinned at 321,980 / 322,339 from 30s onward.

## 7.2 Performance trajectory — the degradation test

The question this scenario exists to answer: **does it get worse over three
minutes?** It does not.

Per-30s segment p50, both cooled runs:

| segment | run 1 p50 | run 2 p50 | live avg | apply backlog avg |
|---|---|---|---|---|
| 0–30s | 10.18 | 11.78 | ~302,000 | **7,670 / 7,681** |
| 30–60s | 7.58 | 9.08 | 322,000 | **0** |
| 60–90s | 7.41 | 9.58 | 322,000 | **0** |
| 90–120s | 7.13 | 8.43 | 322,000 | **0** |
| 120–150s | 7.22 | 7.93 | 322,000 | **0** |
| 150–180s | 7.34 | 7.99 | 322,000 | **0** |
| 180–210s | 7.41 | 8.02 | 322,000 | **0** |

**Flat to improving** — first→last segment p50 −27.3% and −31.9%. The fall is
the opening pour ramp draining out of segment 1, not the machine getting
faster; from segment 2 onward the trend is level within noise.

Three independent signals that would all rise if something were accumulating,
and none of them do:

- **Apply backlog: 7,670 in segment 1, then exactly 0 for every remaining
  segment.** The §8.5 frame budget (4096 ops/frame) absorbs the opening burst
  and then never falls behind again under sustained load.
- **Managed heap 201 → 212/216 MB, peak 220/221.** A 15 MB rise over 200s
  that plateaus — not creep.
- **Tile pool: 512 acquired, 0 released, cap never exceeded.** Saturated and
  stable, no churn.

Whole-run figures:

| | run 1 | run 2 |
|---|---|---|
| p50 | 7.61 ms | 8.79 ms |
| p99 | 48.47 ms | 48.90 ms |
| max | 225.02 ms | 260.81 ms |
| result | **8 PASS / 0 FAIL** | **8 PASS / 0 FAIL** |

**Driftcheck between the twins: p99 agrees to 0.9%, p50 to 15.5%.** The p99 is
the trustworthy number. **p50 must be quoted as a range, 7.6–8.8 ms** — 15.5%
is wider than this project's own agreement threshold, so a single p50 from
one siege run is not a figure to compare a future change against.

### The tail, reported but not chased

Standing instruction is not to re-chase the tail, so this is recorded, not
investigated. Both runs agree closely and the attribution is **not** what the
isolated tests showed:

- 15 and 16 stutter frames (≥100 ms) out of 12,013.
- **Stutter wall clock is 85.4% / 85.0% `preUpdate`** — engine frame start,
  which includes GC stop-the-world suspend and "main thread not scheduled".
  `update` is ~15%, `postLate` ~0.2%.
- The p99 *band* (worst 120 frames) splits 38.9% preUpdate / 60.6% update —
  so the moderate tail is our work, the extreme tail is not.
- Band upload: **0 bytes/frame, 0 SetData calls/frame**. Not the clipmap.

Consistent with every prior session: the extreme tail is above our code, and
GPU-stage attribution remains impossible on this toolchain.

## 7.3 Correctness under sustained combined load

Every gate passed in both runs:

- **Lava+obsidian conserved exactly** across the quiet settle: 12,345 → 12,345
  and 12,436 → 12,436, **0.00%**. (Water 73,831 → 73,831 is reported as a note,
  not a gate — §7.3 of the architecture destroys water on lava contact by
  design, so a fluid-voxel count is the wrong quantity to conserve.)
- **Zero silent wake failures** — 0 of 131/137 sampled mobile voxels inside
  the radius lacked a tile.
- **Zero op-list readback errors** across 3.65 M ops.
- **Zero ops dropped for non-residency.**
- **Tile pool never exceeded its cap.**
- **40/40 CCD sweeps hit the wall** (see §7.5 — this is where a bug was).
- Every system verified to have actually run, so a silent no-op could not
  make the run look clean.

## 7.4 Visual — does it actually look like the chaos it claims to test?

15 screenshots at 15s intervals plus an after-settle frame, **all viewed**,
not just counted. It does.

- **Large terraced detonation craters** in both flanking masses, with the
  stepped walls that repeated overlapping spheres produce.
- **Water sheets, lava pools and sand mounds spread wide** across the basin
  and down into the craters — the fluid is visibly moving through terrain
  carved during the same run, not sitting in a pre-made bowl.
- **Substantial obsidian formations exactly where lava met water**, which is
  §7.3 confirmed visually at scale rather than in a unit test.
- **Terrain intact throughout** — no holes, no corruption, no z-fighting, no
  chunk-boundary seams, in any of the 16 frames.

### The one visual artifact: persistent suspended fluid cubes

After the 300-frame quiet settle, blocks of fluid remain suspended in mid-air
rather than falling. This is the **tile-pool cap made visible**, and the
arithmetic matches the counter:

- The pour spreads ±260 voxels ⇒ 17×17 = **289 tiles for a single Y layer**,
  against a pool of **512**.
- 289 of 512 does not saturate on its own — **the saturation comes from the Y
  dimension**: falling fluid occupies tiles in roughly two layers at once,
  which puts demand near or past the cap. *(2026-09-11: this reasoning was
  right in direction and roughly right in size — the siege's peak demand has
  since been measured directly at **678**, against the 512 cap. See
  `FLUID_TILE_CAP_RESULTS.md` §7.)*
- The engine then refuses cleanly per §7.7: **283,337 / 294,588 exhaustions
  recorded, 0 ops dropped for non-residency, cap never exceeded**. A voxel
  whose tile cannot be acquired does not move, and it does not corrupt
  anything either.

Same class as the chaos ladder's `MAX_ACTIVE_FLUID` cubes: **a designed limit
becoming visible under deliberate oversubscription, not a defect.** Whether
512 tiles is the right pool size for a scenario this wide is a tuning
question, listed in §7.6.

## 7.5 Two bugs found — both in the rig, neither in the engine

Recorded because a scenario that fails for its own reasons is worse than no
scenario, and both were caught by gates rather than by looking at numbers.

1. **The CCD assertion failed 33/40, reproducibly, in both runs.** Cause: the
   radius-40 detonation at `c.x+65` spans `c.x+25..+105` and **destroyed the
   CCD wall** at `c.x+30..31`. The assertion was correct; the arena was wrong.
   Wall moved to `+50`, right mass to `+80..135`, in-water blast cut to
   radius 20 — **40/40 in both runs since.**
2. **The degradation gate used `Math.Abs`**, so it *failed* a run whose frame
   time **improved 38%** — flagging the scenario getting faster as
   degradation. Made one-sided: only a rising trend is a fault.

Also widened: the conservation census covered ±130 while the pour spreads
±260, so it was gating on 3,577 voxels out of ~194,000 placed. Now
`SpreadVoxels + 12`.

## 7.6 What the siege adds to the open items in §5

1. **Tile pool size (512) vs. wide fluid spreads.** ~~The cap is doing its job
   correctly, but at siege width it is reached ~290,000 times and the visible
   consequence is suspended fluid. Raise the pool, narrow the spread, or
   accept the artifact — a tuning/feel call, not a correctness one.~~
   **SUPERSEDED 2026-09-11 — now measured, see `FLUID_TILE_CAP_RESULTS.md`.**
   The cause is confirmed by a deliberate repro (not inferred from a counter),
   real demand is **~680 tiles**, and a **1024 cap takes the artifact to 0.0%
   with no measurable frame-time cost** for +256 MB. The open decision is
   narrower than "raise, narrow or accept": it is whether +256 MB on an 8 GB
   machine is worth it, and whether to fix the orphan-tile gap that a 1024
   pool exposes at the same time. A recommendation is on the table there.
2. **A siege p50 is not a comparable number.** 15.5% twin spread; use the p99
   (0.9%) or quote p50 as a range.

Nothing here changes §5's existing items, and nothing found in the siege
needs a correctness decision.

## 7.7 Honest limits of this result

- Two runs, not a distribution. Enough to establish the trend is not rising;
  not enough to publish a single p50.
- 200s of siege, not an hour. A problem with a >3-minute onset would not
  appear here.
- The extreme tail is reported, not explained — unchanged standing limitation.
- One arena, one pour geometry. Different spread widths would change where
  the tile pool sits relative to its cap.
