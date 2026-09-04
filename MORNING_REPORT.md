# Overnight run — morning report

**Session:** evening 2026-09-03 → morning 2026-09-04
**Queue:** items 1–10, worked in priority order
**Outcome:** 10 of 10 items reached. 12 commits. Suite went 229 → **239 PASS / 0 FAIL / 0 SKIP**.
**Nothing was weakened to make anything pass.** One assertion was *rebuilt* under
item 4's stated exception, and that rebuild is argued from measurement below.

---

## 1. Per queue item

| # | Item | Status | Commit |
|---|------|--------|--------|
| 1 | Snow palette | **DONE** | `520f704` |
| 2 | Floating lava voxel | **DONE — real bug, fixed in both implementations** | `e31c95c` |
| 3 | §4 divergence experiment | **DONE — answered** | `c474cb9` |
| 4 | Correct §7.8 assertion | **DONE — tie-break branch** | `511a85c`, `7db243b` |
| 5 | PHASE_5B_COMPLETION.md | **DONE** | `1e25eb8`, `5056762`, `7db243b` |
| 6 | Harden async readback regression | **DONE — and its rationale corrected** | `f4ac773` |
| 7 | Spawn/world mismatch | **DONE** | `de3d6d4` |
| 8 | Full regression sweep | **DONE — all green** | (evidence below) |
| 9 | Optimization tier | **DONE — 6 proposals, ZERO implemented** | `406338e` |
| 10 | Extra credit | **DONE — lava settle-time test** | `2fe6c2d` |

### 1 — Snow palette

0.86 → **0.72** (`0.715, 0.735, 0.760`), both in `MaterialRules.cs` (authoritative)
and mirrored in `Raymarch.compute`.

**Traversal untouched, verified three ways:** the shader diff is **one hunk, one
line**, inside `MaterialAlbedo`, which is called once at the end of `CSMain` on a
voxel the DDA has already decided was hit; `ShaderCompileCheck` reported 0 errors;
98/98 traversal + validator tests pass. Comparison screenshot captured. Did not
iterate further on the value.

### 2 — Floating lava voxel: a REAL bug, not benign

Reproduced **in isolation, not in Playground** (per its own rule), on a
**non-flat** floor — a staircase basin. Instrumented first: added
`CountFloatingMobile()` / `TryFindFloatingMobile()` to the CPU oracle so the
defect could be *counted* before anything was changed.

**Root cause:** a lateral move woke the destination's neighbourhood but never woke
**what was sitting on top of the cell it vacated**. That voxel had already gone to
sleep with support beneath it; the support left sideways; nothing told it the
world had changed. It hung there permanently. A flat floor never shows this
because nothing moves laterally out from under anything.

Fixed in **both** implementations, which is the part that mattered:
- CPU oracle: `WakeAbove(home)` on *every* commit, not just descents.
- `FluidCA.compute`: two-level `WakeMark` — narrow mark (1) on the vacated cell so
  what can descend into it re-checks, broad mark (2) on descents.

Pinned by `FluidSlopedTerrainTests` (4 tests: staircase water/sand/lava, overhang water).

### 3 — §4 divergence: ANSWERED BY EXPERIMENT

Both experiments you asked for, run as specified:

**(a) Is the GPU stable run-to-run?** Added `-repeat` to the rig. Same scenario, N
repeats: the GPU produces the **identical** distribution every time. It is
deterministic; it is not racing.

**(b) Does the CPU's own distribution change if you permute its tie-break?** Added
`TieBreakSalt` to `FluidReferenceCPU`, mixed into the hash. Changing only the salt
— *same physics, same rules, same seed* — **moves the CPU's own final layout**, by
a margin of the same order as the CPU↔GPU gap being investigated.

**Conclusion: §7.8 tie-break variance, not a translation bug.** The CPU oracle
has no more claim to "the" correct layout than the GPU does. Two conforming
implementations of §7.3 that break ties differently produce different
*arrangements* of the same conserved mass. The old test was asserting on
something the architecture never promised.

### 4 — The assertion was REBUILT, not loosened

This is item 4's tie-break branch, so per your instruction I did **not** relax the
old check. I replaced it with the §7.8-shaped one:

```
ASSERTED:      conserved counts  +  settled-layer occupancy (full set match)  +  no floating drops
REPORTED ONLY: surface height, occupied-layer count
```

The demotions are the point: those two are *arrangement* statistics, exactly what
experiment (b) proved is not determined by the spec. The code carries a comment
saying why exact-layout equality was wrong. Sweep result: **5/5 scenarios MATCH.**

### 6 — Hardened, and the reason for it CORRECTED

Made `MaxFramesInFlight` overridable, added `-inflight N`, wrote
`FluidReadbackInvariantTests` (3 tests) driving the conservation-GAIN scenario
with more than one frame in flight and asserting the ledger catches it.

**Finding worth your attention:** re-measuring showed the conservation gain
**no longer reproduces** at `-inflight 3`. The throttle's original justification
is **stale** — the self-validating move-op supersedes it. I corrected the comments
rather than copying the old reasoning forward. The throttle now stands on a
current, stated reason, not an inherited one.

### 7 — Spawn derived, not hard-coded

`Phase4Bootstrapper.DeriveIslandSpawn(sizeClass)` now derives from
`WorldGenConstants.DeriveIslandGeometry`; the literal `(140.8, 12, 140.8)` default
became `Vector3.zero` meaning "derive". Any scene that set an explicit spawn still
wins. Pinned by `IslandSpawnTests` (3 tests). Every existing scene verified.

### 8 — Full regression sweep, clean build

| Check | Result |
|---|---|
| EditMode suite | **PASS 235 → 239, FAIL 0, SKIP 0** |
| Traversal + validator | **98 / 98** |
| ShaderCompileCheck | **0 errors** |
| Phase 5a rig | 20/20 frames, **0 ledger imbalance, 0 duplicate ownership** |
| Phase 5b rig | **5 / 5 MATCH** |
| Phase 4 acceptance rig | **PASS 51 / FAIL 0** |

The Phase 4 result is **better than the documented baseline** (50 PASS / 1 FAIL):
Gate C's upload p99 measured **0.598 ms** against the 1.0 ms budget. Read that as
one sample of a known-variable metric (CLAUDE.md records 0.98–1.63 ms across runs),
**not** as the upload-budget issue being closed. One good run is not a trend.

### 9 — Optimization tier: PROPOSALS ONLY

`OPTIMIZATION_CANDIDATES.md`, 6 ranked candidates. **Nothing implemented.** Two
reasons, both yours: 9c blocks fluid-CA optimization until §4's correctness is
established, and the intermittent GPU floater below means it is not; and no
EngineConfig hard limit was raised (one candidate *lowers* a buffer).

The document is built from **algorithmic facts** — thread counts, buffer sizes,
redundant work — rather than millisecond attribution, because per-kernel GPU
timing does not exist in this project and the two available figures are unusable
for it. Highlights: the CA dispatches ~590k threads/tick **even at rest**; the
op-list reads back 2 MB/frame to carry a peak of 138 ops (**475× oversized**).

The document's own closing point is that we cannot yet rank #1 against #3 without
per-dispatch timing, and that resolving that means either accepting A/B wall-clock
with kernels disabled, or revisiting AMENDMENT_8_9 §0 Rule 1 deliberately. **That
is a decision for you, not something I should route around.**

### 10 — Chose the lava settle-time test

Of your three options I took the **§8.1 rest-detector blind spot**, because it was
a gap the codebase had explicitly flagged and left instructions for, not new scope.

`PHASE_5A_COMPLETION.md` §8.1 warned that `ChangedCellsThisTick` reads "at rest" 5
ticks out of every 6 for Lava, and that every existing rest assertion used Water or
Sand (interval 1) so nothing caught it. `FluidSlowViscositySettleTests` uses **both**
remedies §8.1 named, so they pin each other:
- The blind spot is now **a test, not a paragraph** — a 4-tick quiet window *is*
  reached while lava is still mid-fall with slots alive.
- Rest measured by `ActiveSlotCount == 0` is not fooled: lava settles by tick 216,
  **honey by tick 870** (interval 30 — never simulated to rest anywhere in the
  suite before tonight), mixed water+lava by 180. All conserved, none floating.

I checked your other two bullets and neither needed work: `PaletteSyncTests`
already parses the shader and pins all 12 colours plus the self-lit list, so the
snow fix was covered the moment it landed; and every other fix tonight already has
a test.

---

## 2. Decisions, and why

1. **Fixed the floater in the CPU oracle too, not just the GPU.** The oracle is
   only worth having if it is independently right. Fixing one side would have made
   them agree while both were wrong.
2. **Did not loosen the §4 assertion — rebuilt it.** Item 4's exception applied,
   but only because experiment (b) *established* tie-break variance first. The
   order mattered: measure, then change the test.
3. **Demoted surface height and layer count to reported-only.** They are
   arrangement statistics the spec does not determine. Keeping them asserted would
   have kept the suite red for a non-defect.
4. **Corrected the readback throttle's rationale instead of inheriting it.** The
   old reason no longer reproduces. A guard with a stale justification is how a
   guard gets deleted later by someone who checks.
5. **Implemented zero optimizations.** 9c blocked the fluid ones and the floater
   below means §4's correctness is not established. Ranking without per-kernel
   timing would have been guessing.
6. **Corrected my own test, not the simulation, in item 10.** I asserted
   `ChangedCellsThisTick == 0` on the same tick `ActiveSlotCount` hit 0. The last
   slot can commit a move and then be freed by the post-commit sweep within one
   tick, so those legitimately coincide once. My assertion was wrong.
7. **Stopped chasing the intermittent floater.** It did not reproduce on the next
   run, and one intermittent GPU-only defect could have eaten the rest of the queue.
   Recorded rather than ground on — per your hard-stop rule.

---

## 3. STILL OPEN

### The one you should read first

**An INTERMITTENT GPU-only floating drop.** One sweep reported
`floating drops GPU 1 / CPU 0` on `pour_water`. It **did not reproduce** on the
next run. The CPU oracle was clean, so it is GPU-side only, and the prime suspect
is the shader port of the wake fix (`WakeMark` narrow/broad) under some ordering
the CPU path cannot hit. Recorded in `7db243b`.

This is why **item 9's fluid optimizations stayed unimplemented** — under 9c, the
CA is not established correct while this is outstanding.

### Also open

- **The two manual gates you reserved:** the claim-race test (`Phase5b_ClaimStress`,
  untouched as instructed) and the Xcode timing pass.
- **Gate C upload p99 variability** — one 0.598 ms run does not close a metric that
  ranges 0.98–1.63 ms.
- **The render-range mismatch** (`AMENDMENT_8_11`, draft, not adopted) — pre-existing,
  outside this queue, untouched.
- **Per-kernel GPU timing does not exist**, which caps how far item 9 can go.

### Flagged, not done (per your "flag, don't do")

No atomic was added to the claim path (§7.3/8.4). Nothing needed one.

---

## 4. Is Phase 5b closeable?

**No — and not only because of the two manual gates.** There is a **third** item:
the intermittent GPU floating drop. It is a correctness observation on the GPU CA,
it is unexplained, and it is not covered by either manual gate.

Stated the way you asked me to keep these separate:

- **CORRECTNESS PROVEN:** conservation on all 5 scenarios; the §7.8 steady-state
  invariants (counts, settled-layer occupancy, no floaters) on all 5; the
  floating-drop fix, on both implementations; the readback ledger; slow-viscosity
  settle behaviour. 239 EditMode tests.
- **CORRECTNESS OPEN:** one intermittent GPU-only floater, seen once, unexplained.
- **PERFORMANCE PROVISIONAL:** WALL figures only, not Xcode-verified. GPU figures
  remain labelled inflated ~2.6–2.7× per Amendment 8.10 and are not quoted as
  results anywhere.
- **NOT TESTED:** the claim-race stress test, and the Xcode timing pass. Yours.

I have not written "Phase 5 is complete" anywhere, and it isn't.

**Suggested first move this morning:** run `Phase5b_ClaimStress` yourself. If the
claim path has an ordering hole, it is the most plausible single explanation for
an intermittent GPU-only floater, and that is the one test I was told not to touch.
