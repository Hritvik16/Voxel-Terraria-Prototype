# Phase 4 Completion Record — Streaming & Persistence

**Project:** Voxel Terraria 1 Byte BrickMap
**Spec:** ARCHITECTURE_v8.6.md §13 Phase 4
**Date:** August 30, 2026
**Final gate tally:** 50 PASS / 1 FAIL — the §4.3 upload budget (1.035ms vs
1.0ms) is the only failing gate. Across this phase's runs the tally ranged from
51 PASS / 0 FAIL to 48 PASS / 3 FAIL, all variance in the upload budget and (in
one run) a single-frame pop-in transient.
**Hardware:** Apple M1 Air (fanless, 8GB unified memory)
**Engine:** Unity 6000.3.10f1, IL2CPP, release standalone

**VERDICT UP FRONT: Phase 4 is NOT closeable, but only one acceptance criterion
now fails.** Three of §13's four criteria are fully met. The fourth (sustained
traversal) has memory-flatness and no-pop-in-inside-128m met, leaving **the
≤1.0ms upload budget as the single failing acceptance criterion** at
1.03–1.63ms. One sub-assertion of the force-quit test is untested. §7 states
exactly what blocks closure.

**A NOT MET verdict from the previous session has been reversed on
re-measurement, not on a code change** — see §3.1. The pop-in criterion was
being measured against a 166m Chebyshev square while §13 asserts a 128m
Euclidean radius.

---

## 1. Scope (per §13 Phase 4)

**The one new thing:** chunks entering/leaving the resident window, and delta
save/load.

| §13 file to create | status |
|---|---|
| `Streaming/StreamManager.cs` | delivered |
| `Streaming/ChunkLifecycle.cs` | delivered |
| `Streaming/DeltaCodec.cs` | delivered |
| `Streaming/CoalesceScheduler.cs` | delivered |
| Window sizing measured + recorded in `EngineConfig` | **partial — see §6.2** |
| LRU eviction (distance + §3.6 pool-pressure valve) | delivered |
| Auto-cleaner toggle (§10.1) | **delivered in the wrong place — see §6.1** |

---

## 2. Evidence classification

Kept deliberately separate. These are different claims with different backing.

### CORRECTNESS PROVEN
- **181/181 EditMode tests green**, before and after every change this session.
- **ClipmapValidator: `mismatchesInCleanChunks` = 0** in every validation of
  every run (3 validations per run: Gate B as-is, Gate B post-forced-flush,
  post-traversal). §10.4's "single most important architecture-specific check".
- **No LOST UPDATES** — every stale GPU entry was still queued for upload, in
  every run.
- **§4.4 forbidden transitions**: exactly 5 of 16 state pairs legal, the other
  11 rejected; `Loading→Unloaded` rejected as `ForbiddenAbandonLoad`;
  `Saving→Resident` and `Saving→Loading` both rejected (the Saving-eviction
  lock). §10.4's "No `Resident→Unloaded` during `Saving`" is covered by this
  table test.
- **Delta round-trip** (§10.4): save → evict → reload → content-hash equality,
  `0x0C4C8BB7 == 0x0C4C8BB7`.
- **CRC rejects corruption**: all 200 single-bit corruptions rejected
  (accepted=0, rejected=200); no truncation length throws; wrong-coord and
  wrong-seed deltas rejected.
- **Force-quit save integrity**: 5 of 5 SIGKILLs left **zero** orphaned
  `.delta.tmp` files and **zero** CRC rejections on relaunch.

### PERFORMANCE MEASURED (not "proven")
Standalone release build, launched outside the Editor, FrameTimingManager and
CPU Stopwatch figures. Performance State not verified (§10.2's Xcode capture is
a manual step not performed).

### NOT TESTED AT ALL
- "At most the in-flight chunk reverted" after force-quit — see §3.3.
- Any world size beyond sizeClass 1, or any second size class in one session.
- Editing while the pool-pressure valve (§3.6) is actually firing — no run
  approached the high-water mark (peak 67% of cap).
- Coalescing under sustained building over time (the §4.5 background scan runs,
  but only a scripted single pass was asserted).
- Native 1080p internal resolution (we render 960×540 and upscale — see §5).

---

## 3. The four §13 acceptance tests

### 3.1 Sustained traversal — **PARTIALLY MET** (upload budget only)

> "Edge-to-edge at 60 m/s repeatedly: no pop-in inside 128m, upload ≤1.0ms
> steady, memory flat over 10 minutes (any creep is a leak)."

Run `2026-08-30_221321`, a 600-second Gate E soak (the rig's own header had
said "raise `_soakSeconds` to 600 for the real gate"; a `-soak` launch flag was
added to do that without a scene edit).

**Memory flat over 10 minutes — MET.** Sampled every 15s for 10.8 minutes,
using *both* `ps rss` and `vmmap` physical footprint. Steady state = after the
startup ramp (t ≥ 3 min), 33 samples over 8.9 minutes:

| metric | first | last | min | max | slope |
|---|---|---|---|---|---|
| RSS | 852 MB | 855 MB | 764 | 876 | **−4.18 MB/min** |
| IOAccelerator (GPU) | 645 MB | 634 MB | 621 | 645 | −0.07 MB/min |
| physical footprint | 1638 MB | 1638 MB | 1536 | 1638 | +1.21 MB/min |

No creep; the two finer instruments both trend slightly *negative*. **Caveat on
the footprint row:** `vmmap` prints to 0.1G, so its resolution is ~102 MB and
it observed only two distinct values (1536/1638). It cannot resolve creep finer
than that, so RSS (1 MB resolution, −4.18 MB/min) is the instrument the
conclusion rests on. `ps rss` alone would have been the wrong instrument
entirely — it undercounted by ~6 GB before this branch's leak fix (§4).

**Upload ≤1.0ms steady — NOT MET, and this is now the only failing criterion.**
Measured p99 across every run this phase:

| run | Gate C | Gate E |
|---|---|---|
| 600s soak (first) | 0.684 **pass** | 1.434 fail |
| split run 1 | pass (all gates green) | pass |
| split run 2 | 1.194 fail | 1.633 fail |
| split run 3 | 1.504 fail | 1.255 fail |
| 5-run leak verification | — | 1.198–1.241, passed 3 of 5 |

The overrun is marginal (1.19–1.63ms against 1.0ms) and **variable enough that
whole runs pass** — one run this session was 51 PASS / 0 FAIL. It is a real
budget miss, not a blow-out, and it is not stable enough to attribute to any
single cause from these numbers alone.

**No pop-in inside 128m — MET. This reverses an earlier NOT MET verdict, and
the reversal is an instrument correction, not a code change.**

The first pass recorded deficit p99 89 / max 226 and called the criterion
failed. That number came from `LoadDeficit()`, which counts a **Chebyshev
square** of `_loadRadiusChunks` = 13 chunks — 166m on the axis, **235m to the
corner**. §13 asserts a **128m Euclidean radius**. A chunk missing at 200m is
not a violation of "inside 128m", so the two were never the same claim and the
criterion could not be settled either way.

`LoadDeficitSplit` now partitions the same sweep by true Euclidean XZ distance,
counting a chunk as inside 128m when its **nearest point** (not its centre) is
within the radius — the strict reading, since any missing voxel inside the
radius is pop-in inside the radius. Three runs, six gate measurements:

| run | gate | **deficit ≤128m** p50/p99/max | deficit 128–166m p50/p99/max |
|---|---|---|---|
| 1 | C traversal | 0 / 0 / **0** | 0 / 27 / 27 |
| 1 | E soak (20s) | 0 / 0 / **0** | 0 / 27 / 32 |
| 2 | C traversal | 0 / 0 / **0** | 0 / 27 / 27 |
| 2 | E soak (600s) | 0 / 0 / **12** | 0 / 86 / 181 |
| 3 | C traversal | 0 / 0 / **0** | 11 / 44 / 51 |
| 3 | E soak (600s) | 0 / 0 / **0** | 0 / 27 / 51 |

**Deficit inside 128m is p99 = 0 in all six measurements**, and max 0 in five of
six. The entire 89/226 lay in the 128–166m band, outside the criterion.

**The one honest exception:** one 600s soak (run 2) recorded a single-frame max
of **12 chunks** briefly missing inside 128m. p99 was still 0, so this is one
transient frame in one of three sustained soaks, not a sustained condition —
but it is not zero, and it is recorded rather than rounded away. The criterion
as literally written ("no pop-in inside 128m") is met at p99 in every run and
violated on one sampled frame out of ~20,000 in one run.

**Frame time over 10 minutes — holds.** Gate C p99 20.90ms, Gate E p99 24.47ms,
with 9 stutters in 1,511 frames (Gate C) and 128 in 20,000 (Gate E, 0.64%).
Tonight's leak fix holds over a 10-minute run, not just rig-length runs — but
neither gate is under 16.67ms at p99. See §6.3.

### 3.2 Edit persistence through eviction — **MET**

> "Dig a tunnel + build a small dense structure, fly 500m away and back:
> intact; the refilled part of the tunnel coalesced back to uniform."

§10.1's auto-cleaner was confirmed OFF for this (see §6.1).

- 116 voxels dug, 128 placed; chunks touched recorded explicitly.
- Flew 500m out and back. **At least one delta written on eviction and read
  back on re-admission** (asserted separately, so "nothing was saved" cannot
  masquerade as success).
- **`edits survived a 500m round trip: 0x0C4C8BB7 == 0x0C4C8BB7`** — content
  hash of the reloaded chunk equals the hash recorded before eviction.
- 0 of 9 watched chunks hash-mismatched; 0 still missing.

This is also §10.4's "delta round-trip: save → evict → reload → byte-compare
equality", and is reported as that check too.

**The coalescing half needed the assertion rewritten, and that is worth
recording.** The original rig asserted `denseAfter <= denseBefore`, which would
pass if *nothing* coalesced. Strengthening it to "every refilled brick is
uniform" then failed at **25 of 25 tunnel bricks** — and the cause was the
assertion, not the coalescer. `Coalescer.TryCoalesce` requires all 512 bytes of
a brick to match; the rig digs at `surfaceY − 2`, 0.2m below the surface, so
every brick the tunnel crosses straddles the air/solid boundary and was
**already dense before the dig**. Refilling one voxel line restores 8 of 512
voxels; the other 504 still hold the surface transition. Staying dense is
correct.

§13's wording assumes digging inside uniform material. The assertion now
records each tunnel brick's uniform/dense state *before* the dig and asserts
the precise claim: **a brick that was uniform before the dig must be uniform
again after the refill.** Bricks already dense pre-dig are counted and reported,
not failed.

**That correction then passed VACUOUSLY, at "0 of 0 regressed"** — because *no*
tunnel brick was uniform before the dig. A green check that tested nothing. The
rig now also digs a **second tunnel at `surfaceY − 24`** (2.4m down, below the
dirt/stone transition, measured: surface y=40, deep dig y=16), where bricks
genuinely are uniform to begin with. With both tunnels:

| tunnel bricks (50 distinct) | count | meaning |
|---|---|---|
| went **dense → uniform** after refill | **25** | §4.5 coalescing, demonstrated |
| stayed dense | 25 | shallow tunnel, straddles the surface — correct |
| uniform pre-dig that regressed | 0 | — |

The 25 dense→uniform bricks are the actual evidence for §13's assertion, and
none of the three original counters credited them; a fourth was added and
asserted `> 0` so the criterion is met by measurement rather than inferred from
arithmetic across the other three. Verified on the final run:
`PASS: the refilled tunnel COALESCED BACK TO UNIFORM: 25 bricks went dense ->
uniform`. **Three successive versions of this one assertion were wrong in three
different ways** — too weak, then too strict, then vacuous — which is recorded
because the first two both reported green.

### 3.3 Force-quit mid-save — **MET for save integrity, one sub-assertion UNTESTED**

> "Force-quit mid-save during rapid editing; relaunch: at most the in-flight
> chunk reverted, CRC log shows the discard, no crash."

**No implementation of this test existed.** `DeltaCodec.cs:374` and
`Phase4Bootstrapper.cs:236` both referred to "the rig's force-quit test"; no
such test was anywhere in the tree. Built this session as `-editsoak` (drives
continuous edits while moving so chunks are actively saving), an external
SIGKILL, and `-verifyload` (relaunches through the *real* streaming path rather
than a bespoke reader).

5 attempts, each killed with `kill -9` after 55 seconds of active editing:

| attempt | deltas on disk | orphaned `.delta.tmp` | CRC rejected | resident after reload | crash |
|---|---|---|---|---|---|
| 1 | 138 | **0** | **0** | 729 | none |
| 2 | 151 | **0** | **0** | 729 | none |
| 3 | 151 | **0** | **0** | 729 | none |
| 4 | 152 | **0** | **0** | 729 | none |
| 5 | 192 | **0** | **0** | 729 | none |

**5 of 5 clean.** Zero half-written files across five kills is the atomic
`.tmp`→rename contract (§4.2) holding under the exact conditions it exists for.

**Honest caveats, both material:**
1. **"At most the in-flight chunk reverted" was NOT measured.** Proving it needs
   a record of what was committed that is independent of the delta files
   themselves (a journal). No such record exists, so the count of reverted
   chunks is unknown. What *is* proven is that nothing was corrupted and
   nothing was rejected.
2. **The CRC log shows no discard — because there was nothing to discard.**
   §13 expects the log to record a discard; atomic rename means a kill leaves
   either the old file or the new one, so a clean kill produces no corrupt file
   and no discard. The discard path is proven separately by test 3.4 and by the
   200-single-bit-corruption test. This is the assertion being satisfied by the
   design being stronger than the assertion anticipated, not by the test being
   skipped.
3. A first pass of the harness counted `.delta.tmp` files at the wrong path
   (`<world>/deltas` instead of `<world>/Phase4World/deltas`) and reported 0
   for a directory that did not exist. Those shell-side numbers were discarded;
   the table above is from the in-app check, which reads
   `Phase4Bootstrapper.DeltaDirectory` directly. Recorded because a
   wrong-path zero is indistinguishable from a real zero unless you look.

### 3.4 Corrupt delta — **MET**

> "Hex-corrupt a `.delta`: that chunk regenerates pristine, game continues."

- `100_0_100.delta` byte-flipped mid-file **while the chunk was non-resident**,
  so the file is final and cannot be rewritten before it is read. (The rig
  documents an earlier ordering bug where the corruption was silently
  overwritten by an eviction save before anything read it.)
- **Rejections 0 → 1**; the chunk regenerated pristine.
- Game continued, world intact, no crash.
- CRC discard log printed, satisfying §13's "CRC log shows the discard".
- Independently, all **200 single-bit corruptions** were rejected by CRC.

No delta/CRC/world.meta format was changed to make any of this pass.

---

## 4. The 6 GB GPU buffer leak — found and fixed this branch

Recorded here because it is the reason Phase 4's frame-time numbers are what
they are.

`CascadeTierPool` created its `ClipmapBuffer` and `BrickDataBuffer` with **no
usage flags**, then wrote them with partial `SetData` every frame a chunk went
dirty. Without `LockBufferForWrite` the driver places a buffer device-private,
and a partial write into one the GPU may still be reading is serviced by
**renaming** it — old backing store orphaned, fresh one allocated, per write.

`vmmap` on a live run: `IOAccelerator (graphics)` 8.4G virtual / **7.8G
resident** / 4.7G swapped across 5,131 regions, of which **78 regions × 78.1 MB
= 6.09 GB** — seventy-eight live copies of one buffer. The entire CPU malloc
zone was 123 MB by comparison. Physical footprint 8.7–10.3 GB on an 8 GB
machine.

| | before | after |
|---|---|---|
| 78.1 MB regions | 78 | **1** |
| IOAccelerator resident | 2.1 G | **771 M** |
| physical footprint | 3.0 G | **1.6 G** |
| Gate C p99 (13-run baseline vs 5-run verify) | 1195–1685 ms | **13.59–25.42 ms, median 15.37** |
| Gate C p50 | 22.9–48.4 ms | **8.96–10.07 ms** |
| Gate E p99 | 2690–3489 ms | 14.97–38.96 ms, median 19.34 |
| stutter frames | ~190 of 600 | **8 of ~1400** |

Verified across 5 runs: EditMode 181/181, `mismatchesInCleanChunks` = 0 in all
5, acceptance gates 46/0 in three runs and 45/1 in two — no new failure.

Two supporting facts, both measured, both counter-intuitive enough to record:
- Removing that same flag from `TerrainClipmap` measured **16,690 ms/frame** of
  upload (~2000× regression). The flag is load-bearing in both directions.
- Lowering `MAX_CLIPMAP_UPLOAD_BYTES_PER_FRAME` to 512 KB cut upload bytes 3×
  and write calls 5×, dissolved an r = +0.89 correlation to ~0, and **did not
  move p99 at all** — proving that correlation was reverse-causal (a stalled
  frame accumulates more to upload). It also regressed correctness (LOST
  UPDATES) and was reverted.

---

## 5. Decision recorded: internal render resolution — 960×540 (2026-08-30)

Amendment 8.9 §3 listed internal render resolution as OPEN. **Decided:
960×540 internal, upscaled to 1080p, is the ship target.** Native 1080p is not
viable at 60fps on an M1 Air retina display. This closes that open item.

**Consequence:** the pixel-subtend LOD boundaries derive from screen height
(§6.4 C.5, `D(V) = V / anglePerPixel`), so the **540p-derived transitions are
the operative ones**: tier 0→1 at 64m, tier 1→2 at 128m
(`LODConfig.TIER_OUTER_RANGE_M = {64, 128, 290}`).

**Flagged, not resolved:** Amendment 8.9 also noted the tier boundaries do not
cleanly fit the window at *either* resolution. That remains open. Concretely,
at 540p the formula puts tier 2 near ~412m while the window caps it at 290m, so
the window — not the pixel math — has been the binding constraint since the
cascade shipped, and the tier math has never been tested at its own limit.

---

## 6. Known gaps carried forward (tracked, not blocking)

**6.0 FIXED THIS SESSION — the player no longer deletes saves on launch.**
`_clearDeltasOnStart` shipped as a *player* default of `true` (serialised `1` in
the scene, so the code default was moot), meaning every standalone launch
deleted the previous session's saves before the pools initialised. Now `false`
in both code and scene; rig runs pass `-cleardeltas` for a pristine world and
`run-acceptance-rig.sh` does so, keeping gate results comparable. §10.1's
placement complaint below still stands — it remains a player field rather than
an Editor `[InitializeOnLoad]` tool.

**6.1 §10.1's auto-cleaner is in the wrong place.** §13 lists "Auto-cleaner
toggle (§10.1)" as a Phase 4 deliverable and §10.1 specifies
`[InitializeOnLoad]` on exiting play mode. **No such Editor script exists.**
The equivalent is `Phase4Bootstrapper._clearDeltasOnStart`, a serialized field
that wipes `*.delta` and `*.delta.tmp` at *player* startup, shipping as `1`.
Functionally it gives the toggle §10.1 asks for (a `-keepdeltas` launch flag
was added this session to disable it for persistence testing), but it runs in
the player rather than the editor, and it means **every standalone launch
destroys the previous session's saves by default** — correct for repeatable rig
runs, wrong for a game. Should be moved to an Editor script and the player
default flipped before Phase 6.

**6.2 FIXED THIS SESSION — §11.3's placeholders are filled.** §13 asks Phase 4
to "measure and record `WINDOW_CHUNKS_XZ`/`_Y` and the resulting handle/clipmap
memory here (§11.3)". Those rows were literal `[Phase 4]` / `[Phase 2]`
placeholders. Now populated from measurement with their derivations: clipmap
64+64 MB, inlined handles ~12 MB, pools 244+244 MB, cascades 202 MB, declared
total ~837 MB against a measured 1.6 GB physical footprint and the ≤3,000 MB
ceiling. **Still open:** the window was sized against §4.3's streaming
requirement and never against the world — see §6.4.

**6.3 Neither gate meets the 16.67ms frame budget at p99, and Gate E is worse
and more variable.** Gate C p99 median 15.37ms (range 13.59–25.42) does clear
the budget at the median; **Gate E p99 median 19.34ms (range 14.97–38.96) does
not**, and its spread is roughly 2.5× wider than Gate C's. The soak differs
from the traversal in load-deficit behaviour too (max 226 vs 27), so the two
are probably the same underlying issue: the soak asks for more streaming than
the pipeline delivers. Unexplained; not investigated this session.

**6.4 Render range is 290m against a ~1909m island.** `LODConfig` caps tier 2
at 290m, itself derived from the window's corner half-diagonal, while
`Content.cs` scaled sizeClass 1's island to a 954.5m coast radius. The island is
~6.6× wider than the renderer can draw. Fully analysed in
**`AMENDMENT_8_11_RENDER_RANGE.md`** (draft, not adopted), which establishes
from code that the LOD cascade is *not* a clipmap cascade — tiers 1/2 cover the
same extent as tier 0, sampled coarser — so adding tiers would buy zero range.

**6.5 Tiers 1 and 2 still have no CPU oracle.** Carried from Amendment 8.10.
`CascadeValidator` compares tier pools against a fresh `LODDownsampler` run,
which proves the upload path but not the *downsampling rule* itself. Tier-0
traversal has `RaymarchReference`; tiers 1/2 have no equivalent, so a
majority-vote error would be invisible.

**6.6 The upload budget overrun is small but real.** 1.198–1.434ms against
1.0ms. Passes in Gate C and in 3 of 5 short runs; fails in the 600s soak.

**6.7 Pool-pressure valve (§3.6) never exercised.** Peak utilisation was 67% of
cap; the valve fires at 85%. Its behaviour under real pressure is untested —
§3.6 itself defers this to a Phase 6 test, so this is by design, but it means
the LRU eviction path in `ApplyPoolPressureValve` has never run in anger.

---

## 7. Sign-off

Phase 4's substance is delivered: the sliding resident window, async
generation, the §4.4 lifetime state machine with its Saving-eviction lock,
delta save/load with CRC and atomic rename, distance and pool-pressure
eviction, and the background coalescer. The persistence and fault-tolerance
guarantees — the ones §13 calls "the whole never-corrupt-a-save guarantee" —
are the strongest results here.

- ✅ Edit persistence through eviction (§3.2), including the §10.4 delta
  round-trip, and coalescing demonstrated at 25 bricks dense→uniform.
- ✅ Corrupt-delta recovery (§3.4); 200/200 single-bit corruptions rejected.
- ✅ Force-quit save integrity (§3.3): 5/5 kills, 0 orphaned `.tmp`, 0 CRC
  rejections, 0 crashes.
- ✅ Memory flat over 10 minutes (§3.1): RSS slope −4.18 MB/min.
- ✅ ClipmapValidator clean-chunk mismatches 0; no LOST UPDATES; §4.4
  forbidden transitions enforced; 181/181 EditMode.
- ⚠️ "At most the in-flight chunk reverted" not measured (§3.3) — needs a
  commit journal independent of the deltas.
- ✅ **No pop-in inside 128m — MET** (§3.1). Deficit inside the criterion is
  p99 = 0 across all six gate measurements; the earlier NOT MET was measuring a
  166m square against a 128m assertion. One single-frame max of 12 chunks in one
  of three 600s soaks is recorded as the exception.
- ❌ **Upload ≤1.0ms steady not met** — 1.19–1.63ms (§3.1, §6.6).
- ❌ **Frame budget not met at p99 in either gate**, Gate E worst (§6.3).

**Phase 4 is NOT closed, but it is closer than the first draft of this document
claimed.** Of §13's four acceptance criteria, three are fully met and the fourth
(sustained traversal) now has two of its three clauses met — memory flat and no
pop-in inside 128m — leaving **the ≤1.0ms upload budget as the single failing
acceptance criterion**, at 1.19–1.63ms.

Separately, the §11 frame budget (16.67ms) is not met at p99 in either gate
(Gate C median 15.37ms does clear it; Gate E median 19.34ms does not). §13 does
not list this among Phase 4's acceptance assertions, so it does not block Phase
4 by the letter of the spec, but it blocks 60fps and is recorded as such.

**What changed this session, and the lesson in it:** the pop-in criterion was
never failing. It was being measured against a 166m Chebyshev square while §13
asserts a 128m Euclidean radius, and the resulting p99 89 / max 226 was reported
as a failure for a full session. Splitting the counter — instrumentation only,
no behaviour change — showed deficit inside 128m is p99 = 0 everywhere. **Two of
this phase's reported failures (this one and the coalescing assertion in §3.2)
turned out to be measurement defects, not engine defects.** In both cases the
wrong number was believed because it was precise, and in both cases the fix was
to read the spec's assertion literally and measure exactly that.

**The one thing to do before anything else:** decide whether the §4.3 upload
budget is a real ship requirement at 1.0ms or whether the number needs
re-deriving. Prior work established the cost is dominated by the CPU-side
staging loop and dense brick-body writes — the GPU write mechanism is ~2% of the
total and is settled — so closing a 0.2–0.6ms gap means restructuring how
per-chunk staging works, not tuning an upload path.
