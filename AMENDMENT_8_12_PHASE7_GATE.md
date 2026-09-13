# Amendment 8.12 — Phase 7's Budget Gate: Why It Cannot Be Run As Written, and What Replaces It

**Date:** September 12, 2026
**Status:** the substitute gate. Adopted for Phase 7 measurement.
**Relationship to earlier amendments:** this finally closes the open item
Amendment 8.9 §4 left standing — *"the spec's Phase 2 gate is written against
Xcode… **Recommended resolution, not yet formally adopted:** treat the harness's
own worst-case, driftcheck-validated number as the project's *substitute* gate,
explicitly documented as a deviation."* That recommendation was never adopted.
`PHASE_2_COMPLETION.md` §5 hit the same wall and recorded it as *"formally
unresolved"*. `PHASE_6_COMPLETION.md` open item 4 escalated it to *"a permanent
toolchain limitation. Nothing to resolve, ever, in this workflow."* This
document stops re-discovering that and writes down the substitute instead.

**Read this whole document before quoting any Phase 7 performance number.** It
follows 8.9's and 8.10's discipline: **every number below is either VERIFIED
(with the run it came from) or OPEN (a decision not yet made).** Nothing is
invented to fill a gap.

---

## 1. The literal spec text, quoted rather than paraphrased

§13 Phase 7 states two acceptance gates:

> **The budget gate (Performance State checked):** raymarch + shadows ≤9.0ms
> across a full cycle **including golden hour** (capture it specifically). **If
> it fails, engage the §6.5 fallback rungs and re-measure** — the game ships
> either way.

> **The true combined GPU-lane gate (moved here from Phase 5b, since this is the
> first phase where all three ingredients actually exist):** raymarch + shadow
> rays + fluid CA, all running together, at golden hour, with near-player fluid
> active — sustained ≤13.0ms, Performance State checked (§2.2, §11.2).

§11.2 restates both and adds that the combined gate *"can only be measured in
Phase 7 — the first phase where all three exist simultaneously (audit
correction: an earlier draft mistakenly asked Phase 5b to measure this before
shadows existed)."*

§10.2 defines how: *"GPU Frame Capture (Metal), per-dispatch timing via
`CommandBuffer.BeginSample`… **Check the "Performance State" field on every
capture** — if not "Full," the M1 Air is throttling and the number is a *lower
bound*, not the truth."*

---

## 2. Why neither gate can be run as written — four independent reasons

Each of these alone is sufficient. All four hold simultaneously.

**2.1 — No Xcode. Ever.** `AMENDMENT_8_9_LOD_AND_RESOLUTION.md` §0 Rule 1:
*"No Xcode, ever, for any performance measurement. The project uses only the
in-engine automated benchmark harness… **This is a deliberate workflow
decision, not a limitation to work around.** Any future session that suggests
Xcode should be corrected immediately."* §10.2's whole method is an Xcode
capture. It is not available, by project rule rather than by circumstance.

**2.2 — "Performance State checked" has no equivalent.** Amendment 8.9 §0
Rule 2: *"`FrameTimingManager` cannot report Performance State (thermal
throttling status). This is a real, permanent limitation of the chosen
workflow, not a bug to fix."* Both §13 gates carry that qualifier. It cannot be
satisfied, approximated, or waived silently — so any number produced here is,
in §10.2's own words, **a lower bound rather than the truth**, and this document
says so rather than dropping the qualifier.

**2.3 — `gpuFrameTime` is inflated ~2.6–2.7× and has already produced a
retracted result.** `AMENDMENT_8_10_LOD_CASCADE_PERFORMANCE.md`'s CORRECTION
NOTICE: a claim of *"23.73ms, 1.42× over the entire 16.67ms budget"* **was
wrong and is retracted** — it read `gpu_avg` as an absolute figure when the
harness's own header says it is a relative signal only. The inflation was then
measured at a near-constant 2.6–2.7× across all 21 configs. `PHASE_3_COMPLETION`
§5 independently measured 2.49–2.74× at every pose and gives the decisive
argument: *"a frame cannot complete in 10 ms of wall time if the GPU genuinely
needs 27 ms, because steady-state frame time is bounded below by GPU time."*
**No figure in this document comes from `gpuFrameTime`.**

**2.4 — Per-pass attribution does not exist at all.** Both gates are *per-pass*
budgets ("raymarch + shadows", "raymarch + shadow + fluid"). Unity merges the
dispatches into a single Metal encoder, so there is no per-dispatch breakdown to
read even in principle. `FPS_INVESTIGATION_RESULTS.md` §6b names this as the
reason the frame-time tail is *"permanently unattributable beyond that point"*,
and `PHASE_6_COMPLETION.md` open item 4 records it as settled: **nothing to
resolve, ever, in this workflow.**

**Consequence, stated plainly: no per-pass millisecond figure may be quoted
against §11.2's 9.0 ms or 3.5 ms lines by this phase or any other, and none is.**

---

## 3. What is measured instead — the substitute gate

**END-TO-END SUSTAINED RATE.** Not because it is equivalent — it is not — but
because it is the strongest thing this toolchain can actually establish, and
because it is the number that corresponds to what a player experiences.

### 3.1 The method, and why each part of it is load-bearing

Every element below exists because its absence has already produced a wrong
answer in this project.

| element | why | source |
|---|---|---|
| **Release standalone only** | a Development build carries profiling overhead | `CommandLineBuild.cs` header |
| **FULLSCREEN ONLY** | identical back-to-back windowed runs returned 69.6 and 26.9 FPS, and "fluid ON" measured *faster* than "fluid OFF" — the macOS compositor throttles a windowed surface. Fullscreen reproduces to 2.9% | FPS §0.1 |
| **frames ÷ elapsed wall time as the headline** | 120 Hz ProMotion; the app out-presents the display, and **exactly 600 of 1800 frames** landed over 16.6 ms in *four different isolation arms* — one frame in three, regardless of workload. Percentiles measure pacing here, not cost | FPS §0.2 |
| **300 s cooldown before every run** | the same config measured 4.172 / 4.751 / 11.812 ms uncooled — a 183% spread | Phase 6 QA |
| **counterbalanced order (A/B/B/A or forward+reverse)** | a sweep run in one order produced a clean, convincing, **wrong** result that did not reproduce reversed | FPS §0.3 |
| **same session for every arm** | a cross-session comparison produced a wrong cascade-overhead number because the *untouched* arm had drifted 27.15 → 24.5 FPS on a code path the change could not touch | FPS §3.2 |
| **all rungs in ONE binary** | the direct consequence of the row above: `EngineConfig.SHADOW_QUALITY_LEVEL` is overridable per process launch so every arm is the same build | this phase |
| **one config per process launch** | fluid persists, so a later "idle" config in the same process is not idle; and back-to-back configs in one process once produced a physically impossible ordering | `PlaygroundCapture.cs` header |

### 3.2 What the substitute CAN establish

- Whether Phase 7 **regressed** the shipped frame rate, and by how much — the
  arms differ only in a config number, in one binary, in one session.
- Whether each of §6.5's four rungs is **affordable**, and the *relative* cost
  of the ladder's steps, which is exactly the input §6.5's own
  "measurement decides, not intuition" needs.
- Whether the **combined** load (raymarch + shadow + fluid, golden hour) holds
  60 FPS sustained with margin — §13's own stated concern, at whole-frame
  granularity.
- Whether the cost is **visible above the display cap**, via the forced
  `-gateres 1920` arm, where GPU cost exceeds one refresh interval.

### 3.3 What the substitute CANNOT establish — stated, not hedged

- **It cannot say the raymarch+shadow pass is ≤9.0 ms**, or any other per-pass
  figure. That is §2.4 above and it is permanent in this workflow.
- **It cannot report Performance State**, so every figure here is a *lower
  bound* on performance in §10.2's sense — the machine may have been throttling.
  The 300 s cooldowns reduce this; they do not measure it.
- **It cannot attribute** a regression to the shadow ray versus the point-light
  loop versus the sky probe, except by the crude ablation of switching rungs.
- **It says nothing about a GPU-lane budget split.** §2.2's two-lane model is
  not observable here. A whole-frame number that fits does not prove the GPU
  lane fits; it proves the frame fits, which is a weaker and different claim.

### 3.4 The pass criterion

**60 FPS sustained with margin, at 1080p output from the 960×540 internal
gate**, with the shadows-off baseline **re-established in the same session**.

Explicitly *not* compared against the 69.8 ± 0.4 FPS in FPS §2.1: that is a
prior session's number, and §3.2's row above is the reason.

---

## 4. Arms

Five arms × two scenarios × two gate resolutions, counterbalanced, 300 s cooled,
one process launch each, all from one binary in one session.

| arm | config |
|---|---|
| **A** | pre-Phase-7 commit build — the honest regression baseline |
| **B** | Phase 7, `SHADOW_QUALITY_LEVEL = 3` (off) |
| **C** | Phase 7, level 2 (hard only) |
| **D** | Phase 7, level 1 (half-res distant, quad reuse) |
| **E** | Phase 7, level 0 (full penumbra, C.10) |

Scenarios:
- **(i) GOLDEN HOUR** — the longest grazing rays; §13 says to capture it
  specifically, and §6.5 identifies it as the worst case for shadow-ray cost.
- **(ii) THE TRUE COMBINED GATE** — golden hour **with near-player fluid live**.
  This is the whole reason §11.2 moved the gate to Phase 7.

Gates: the shipped **960×540**, and a forced **1920** (`-gateres 1920`). The
shipped gate sits near the display cap where real cost can hide; the forced gate
pushes cost above one refresh interval where it becomes visible. Both are
reported; neither alone is the answer.

---

## 5. Results

**OPEN — not yet run at the time this section was written.** Filled in by
`./run-phase7-lighting-ab.sh`; every figure will carry its run folder. Until
then there is no Phase 7 performance number, and any that appears elsewhere
without a run folder attached is not from this document.

---

## 6. A known CPU-side cost that must be ruled out before arm (ii) is read as
## "new information"

§13 says that if the combined arm breaches while the isolated arms pass, *"that
is new information — the two systems compete for the lane in a way isolated
testing couldn't show."* **Before reaching that conclusion, one already-measured
and entirely separate cost has to be excluded.**

`FLUID_PERFORMANCE_AB_RESULTS.md` §3 measured, under live fluid:

| Gate C | terrain only | with fluid |
|---|---|---|
| `upload_ms` p50 | 0.001 ms | **6.595 ms** |
| cascades total p50 / p99 | 0.00 / 0.11 ms | **6.36 / 8.95 ms** |
| — downsample | 0.00 / 0.00 ms | **6.35 / 8.64 ms** |

**6.36 of those 6.595 ms is the LOD cascade re-downsampling fluid-touched
chunks**, because the resident-chunk loop marks both mirrors from the same
`chunk.dirty` flag. That is a **CPU main-thread** cost, it is documented, it has
a recommended fix that is explicitly *not* Phase 7's, and it has nothing to do
with shadow rays contending with fluid for the GPU lane.

A combined-arm breach that is actually this would be misattributed to the shadow
ray by anyone who had not read that section. It is named here so it cannot be.

---

## 7. If the ladder has to step down

§6.5 is unambiguous that this is a designed outcome, not a failure: *"The
feature ships if the gate passes; **the game ships regardless because the
fallback rungs exist.** This is why shadows are *kept* rather than pre-cut —
measurement decides, not intuition."* Amendment 8.10 §6.4 went further and
predicted it: *"the doc's three shadow fallback rungs are likely to be
**needed**, not optional — plan for engaging them rather than being surprised."*

The headroom this lands in was already known before a line of Phase 7 was
written, and is worth restating so a step-down is read as expected rather than
alarming:

- raymarch alone measured **11.95 ms** at `GroundHorizon`, 960×540 (PHASE_2 §5);
- against §11.2's own ≤9.0 ms raymarch line, **it already did not pass** — and
  that line is supposed to *include* Phase 7's shadow rays;
- current sustained play is **14.33 ms mean, ~2.34 ms of margin** (FPS §2.1);
- PHASE_3 §5: *"plan against 6 ms, not 16.67 ms"*;
- §6.5: shadow rays **double the ray count in the worst case**.

### 7.1 The fork §2.2 states in two contradictory ways

If the lane genuinely does not fit, §2.2 gives a numbered fallback order —

> 1. Reduce the fluid active-radius (§7.4). 2. Lower fluid tick rate outside the
> immediate bubble. 3. If the shadow ray and fluid genuinely cannot both fit,
> the shadow fallback rungs (§6.5) engage first —

— whose list puts fluid first and whose final clause puts **shadows** first
(*"shadows degrade before fluid does, since 'does the game stop being fun
without X' answers differently for the two"*). §13's own restatement gives the
fluid-first order.

**These disagree, and this document does not pick.** Both directions are
measured and the fork is flagged for the owner. Any reduction of
`FLUID_ACTIVE_RADIUS_VOXELS` is written as a visible fraction of the constant
(`/ 10`), never as a literal — the unit error there (1280 voxels = 128 m, 128
voxels = 12.8 m) already produced one retracted "fix", and the constant now
carries that incident in its own comment.

---

## 8. One deliberate reading of §6.5 recorded here, because it is adjacent to an
## undecided amendment

§6.5 says the shadow ray *"clamps at max distance (island diagonal)"*. Taken
literally that is ~2,700 m: `AMENDMENT_8_11_RENDER_RANGE.md` establishes the
island is ~1,909 m across.

**The implementation clamps at the same `_MaxRayDistance` the primary ray uses**
— tier 2's outer bound, 290 m. The reasoning, ESTABLISHED from the config
rather than assumed: the renderer draws 290 m and the resident window is 409.6 m
across, so a ray clamped at 2,700 m would leave the window within a few hundred
metres and then be terminated by the window-bounds guard anyway, having walked
thousands of voxels of guaranteed-nothing first. Tier 2's bound **is** the
effective island diagonal in this build, because no data exists past it.

**This is not an implementation of Amendment 8.11, which remains DRAFT, NOT
ADOPTED and out of scope.** The clamp follows `LODConfig`, so if render range is
ever changed by a decision on 8.11, this follows it with no edit.
