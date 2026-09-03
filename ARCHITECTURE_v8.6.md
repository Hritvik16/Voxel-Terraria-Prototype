# Voxel Engine Specification — v8.6

**Revision 8.6 — Final Consistency Pass (Implementation-Ready)**
**Engine:** Unity 6000.3.10f1 (Unity 6.3) · **Hardware:** Apple Silicon M1, 8GB Unified Memory (M1 Air — no fan, throttles under sustained load)
**Project Goal:** Terraria in 3D — a fully destructible 0.1m-voxel island with discrete fluid simulation, a full day/night cycle with voxel-precise moving shadows, rendered as unapologetically blocky cubes.
**Date:** July 20, 2026

---

## The Current Architecture At A Glance (Post-8.6 — Read This First)

Five revision rounds each changed real decisions; the changelogs below record *history*. This section states only what is **current**, so a fresh reader — or an AI being prompted with excerpts — never has to reconstruct the present by applying five diffs. If any changelog below appears to contradict this section, this section wins.

**Where everything lives now:**
- **Terrain state:** CPU, two-tier uniform-or-dense (chunk → inlined `BrickHandle[4096]` → 512-byte dense brick bodies in a capped pool). CPU is the *only* writer of terrain, ever, via one path: `SetVoxel` (§3, §8.3).
- **Terrain on GPU:** a flat, toroidal, read-only **clipmap** (one direct index per macro-step, no pointer tree), uploaded one-way from the CPU. Near-player/just-edited chunks upload same-frame, always; only distant prefetch chunks are spread across frames (§3.7).
- **Fluid:** *computed* on the GPU (Intent → plain-write Claim → destination-driven Commit — **no atomics anywhere**, §7.3), but *applied* to terrain by the CPU via a small bounded write-op list read back asynchronously and fed through the same `SetVoxel` as mining (§7.2). Settled fluid is an ordinary terrain byte — zero lag; only mid-move fluid has bounded (~1–3 frame) latency, mitigated by the depenetration backstop + speed clamp (§8.2).
- **A CPU fluid reference** (`FluidReferenceCPU`, single-threaded) is built first and kept forever as the correctness oracle the GPU port is validated against (Phase 5a → 5b). Same pattern as `RaymarchReference` for the DDA — the **Sibling Pattern** (§0.3).
- **Rendering:** one raymarch compute kernel — clipmap macro-skip + 8³ dense-brick micro-step, fused per-pixel sun-shadow ray (camera-distance LOD selection, wave-uniform branch guard), 3 LOD tiers (0.1 / 0.4 / 1.6m), simple global-capped point lights (per-brick clustering and BFS propagation are v1.5) (§6).
- **Generation:** pure CPU/Burst function, deterministic, unit-tested before it is ever rendered; content (materials/biomes/features) defined in code for v1 (§5).
- **Physics:** Rigidbody player + probe-first character controller (540-collider treadmill only as escalation), swept CCD with fluid-traversal accumulation, depenetration backstop — all reading CPU terrain directly (§8).
- **Streaming:** 3D window, single-writer StreamManager, explicit lifetime state machine with forbidden-transition asserts, per-chunk CRC + atomic-rename deltas, LRU eviction as the memory valve, `Volatile` fluid-transient bricks freed immediately (§3.6, §3.10, §4).
- **Cut from v1:** Hydrology flow, the chained-abuse chaos ladder, the 5-rung degradation ladder, BFS light propagation, per-brick light clustering, LOD tiers 4–5, worldwide fluid simulation. Object/NPC/town/combat content is **the second document**, built on §5.4's substrate guarantee — nothing in it requires touching this engine's memory layout.
- **Non-negotiables:** the Twelve Invariants and the Hard Limits table (§0) — every limit lives in `EngineConfig`, every phase test measures rather than assumes, frame-time may degrade under abuse but memory-safety and save-integrity never do.
- **Build order:** Phases 0 → 0.5 → 1 → 2 → 3 → 4 → 5a/5b → 6 → 7 → 8, one new untrusted thing per phase, CPU-proof before GPU-port, public APIs freeze on pass (implementations may be bug-fixed). Phase 8's hour-long soak passing is the definition of done for this document.

---

## What Changed in 8.6 — Final Consistency Pass

A requested full-document audit before implementation begins. No design changes — only reconciliation of text that earlier patch-rounds left describing superseded states as current:

1. **§3.5 rewritten** — still described the 8.1 CPU-resident fluid pool ("zero frame delay," "the reason 8.1 moved fluids off the GPU") as current, directly contradicting Chapter 7's GPU-resident design. Now matches.
2. **§8.2's header** still claimed "Zero-Latency" for fluid; corrected to "exact for solids, bounded-latency for moving fluid."
3. **§11.3's memory table** still listed "Fluid Slot Pool (CPU)"; corrected to GPU, and the GPU claim-cell + write-op-list buffers (which the table was missing entirely) added as Phase 5b gates.
4. **§2.2's GPU-total note** still called the 13.0ms total "a Phase 5b gate," contradicting the 8.5 audit that moved the three-way combined gate to Phase 7; aligned.
5. **Coalescer path conflict** — Ch.12 placed `Coalescer.cs` in `Memory/` while Phase 4's file list created it in `Streaming/`. Resolved: Phase 1 owns `Memory/Coalescer.cs` (the pure check, now in its file list), Phase 4 owns `Streaming/CoalesceScheduler.cs` (the background caller), Ch.12 updated.
6. **Phase 1** still said it *defines* `IWorldQuery`/`IEditService` — but Phase 0.5 (added in 8.4) generates them; Phase 1 now *implements* the 0.5 stubs, and its prerequisite includes 0.5.
7. **Phase 5b's `EditService` wake-scan hook** clarified as an additive hook on the Phase 0.5 stub — not a pull-forward of Phase 6's implementation.
8. **TOC** still titled Chapter 7 "CPU-Resident"; **§6.3's header** still said "Mirror-Aware"; both corrected. Minor wording cleanups in §10.4.
9. **Total sizing** updated (~12–16 weeks including Phase 0.5) with an honest note on what AI assistance does and doesn't compress.
10. **Added "The Current Architecture At A Glance"** (above) — one authoritative current-state page, so the five historical changelogs can never mislead a fresh reader or a header-first AI prompt.

---

## What Changed in 8.5 — A Phase-Dependency Audit

Not a design review — a direct, requested check: **does any phase's acceptance test secretly require something from a later phase**, the exact failure mode that cost weeks in v7. Every phase's acceptance test and failure-signature table was read against what's actually built by that point, not just its declared prerequisite.

**Two real bugs found and fixed:**
1. **Phase 5b's acceptance test asked to measure fluid cost "alongside raymarch+shadow"** — but shadow rays don't exist until Phase 7. This was un-executable as written. Fixed: Phase 5b now measures fluid + primary raymarch only; the true three-way combined budget (raymarch + shadow + fluid) is now an explicit Phase 7 acceptance criterion, the first phase where all three ingredients actually exist (§13, §11.2).
2. **Phase 5b's failure-signature table pointed at "the speed-clamp threshold (§8.2)"** — a Phase 6 system, not built yet at Phase 5b. Fixed: the Phase 5b diagnostic now only references what exists at that phase (raw op-list latency), with an explicit note that the clamp-based diagnostic becomes available in Phase 6.

**Two ambiguities closed (not bugs, but the same class of confusion):**
3. Phases 2–5 all say "fly," "tour," or "dig" before the real player controller or mining tool exist (Phase 6). Added one blanket clarification: these phases use a bare debug flycam and direct `SetVoxel` calls, never the Phase 6 player/tools.
4. Phase 5's prerequisite was stated as a blanket "Phase 4 green" for both 5a and 5b, but 5a is a self-contained CPU basin test that only touches Phase 1's memory model. Split: 5a genuinely needs only Phase 1; 5b needs Phase 4.

**Everything else in Chapters 0–12 and the rest of Chapter 13 is unchanged** — this was a targeted audit against one specific question, not a new design pass.

---

## What Changed in 8.4 (and Why)

Prompted by direct pushback on two points, plus a further round of edge-case and AI-workflow review.

**1. GPU atomics removed from the fluid claim step (§7.3) — the substantive fix.** `InterlockedMin` claim resolution is real, contended atomics under heavy convergent fluid flow (many drops targeting one destination — the base of any waterfall) can slow a dispatch enough to risk the macOS GPU watchdog killing the command buffer, and Metal's tooling gives poor visibility into *why* — a contention-induced watchdog kill and an infinite-loop watchdog kill look identical in the logs. **Fix: replace the atomic claim with a plain (non-interlocked) 32-bit write**, relying on the ordinary GPU/CPU hardware guarantee that a single aligned word store cannot tear — multiple threads writing different candidate IDs to the same claim slot is a safe race (one arbitrary value survives, never a corrupted mix), and §7.8 already establishes that only conservation/steady-state correctness matters, never a specific deterministic winner. The destination cell (not the source) performs the actual commit, so every write in the pipeline ends up single-writer by construction. **No atomics anywhere in the fluid claim path.** This must still be confirmed on Metal specifically in Phase 5b — a hardware guarantee stated with confidence is not the same as a measurement.

**2. Fluid latency clarified, not architecturally changed.** Reviewed directly: the bounded op-list latency (§7.2) only affects *actively moving* fluid — settled/dormant fluid is an ordinary terrain byte with zero lag, identical to solid ground, exactly like §3.10 already states. The raymarcher and physics probes read the same applied bytes, so visual and physical state never disagree with each other — the lag is a uniform pipeline delay, not a visual/physical desync. The one real remaining risk (the leading edge of a fast-moving flood front at the exact instant of player contact) is narrow and is now an explicit Phase 6/7 playtest item rather than an assumption either way (§13).

**Adopted from further review, applied where they hold up against the current architecture (some do, one doesn't fully apply anymore — noted honestly, not rubber-stamped):**
- CPU-reference domain decomposition (if parallelized) moves to **brick granularity** (B=8 voxels), not voxel granularity — partition boundaries align with the engine's existing brick structure (§7.3).
- **`WaveActiveAnyTrue(hasPrimaryHit)`** guards the shadow-ray branch in the raymarch kernel — a wave that's entirely sky skips the divergent shadow path instead of every lane paying for one lane's hit (§6.5).
- **Near-player chunks are never subject to the multi-frame clipmap-upload spread** — only distant incoming ring chunks are throttled; a wall broken right in front of the player uploads same-frame, always (§3.7, §4.3). This closes a real bug the multi-frame spread rule could otherwise cause.
- **Global light cap is the explicit v1 default**, not a hedge — per-brick clustering is fully deferred to v1.5 (§3.8).
- **Phase 0.5** — all public interfaces and empty stubs, generated before Phase 1 begins, giving a compile-time dependency graph from day one (§13).
- **The Sibling Pattern**, named explicitly: the CPU-reference-vs-shipped-path relationship already used for fluid (§7) and the raymarcher (§13 Phase 2) is a general, reusable technique — any subsystem getting a nontrivial optimization should keep a deliberately dumb, obviously-correct sibling to swap in and isolate "logic bug" from "optimization bug" (§0.1, §13).
- **Five properties of a good tracer bullet**, defined explicitly rather than left implicit (§13).
- **Header-first AI prompting**: when directing an AI to implement or modify a subsystem, feed it Chapter 0 + Appendix A/B + the relevant frozen interface *first*, not the whole document — keeps context tight, reduces hallucinated struct fields and accidental edits to locked logic (§0.3).
- **`PlayerFeedback` event bus** (Game layer only — sound, particle, screen shake, HUD hooks for mining/damage/splash events) — game feel plumbing that was genuinely missing (§8.1).
- Hard limits: tightened with concrete numeric thresholds **where a real threshold can be stated now**; left as explicit Phase-N measurements where fabricating a precise number would violate our own measurement discipline (§0.2).
- Chapter 0 should also be extracted as a standalone `ENGINE_CONSTITUTION.md` for quick reference / AI-prompting — the version in this document remains the source of truth (§0).

**Declined / corrected against the current design, not adopted as originally worded:** the "Volatile-brick atomic cache-bouncing" fix assumed heavy parallel CPU fluid work; after 8.3 moved fluid computation to the GPU, the CPU's job is sequentially applying a small bounded op-list — there is no parallel contention there to begin with. The "post-commit sweep instead of mid-tick atomics" advice is retained, but scoped correctly to the *optional* parallelized CPU reference implementation only (§3.10, §7.3), not applied as if it were fixing a bug in the shipped path that doesn't exist there.

---

## What Changed in 8.3 (and Why)

8.1 moved fluid simulation fully to the CPU to eliminate `AsyncGPUReadback` latency risk. On reflection (prompted directly by review), that was the wrong side of a real trade. **8.3 moves fluid simulation back to the GPU**, for two reasons that hold up under scrutiny:

1. **Hardware fit.** Fluid CA is close to embarrassingly parallel — thousands of independent per-voxel decisions, contention only at claim time (resolved by a safe plain-write race, §7.3, 8.4 — no atomics needed). That is exactly what a GPU is for and exactly what CPU Burst SIMD is comparatively weak at, even parallelized.
2. **CPU orchestration headroom.** The CPU main thread also carries terrain upload, the physics treadmill, CCD, buoyancy, destruction reduction, streaming, and future mob/inventory/UI logic — all inherently serial, single-thread work with nowhere else to go. In an end-game chaos scene (chained explosions into a flooded crater while the player mines), every one of those systems spikes at once. Spending 3.5ms of that scarce, serial budget on work the GPU could absorb in parallel was backwards.

**The reintroduced risk, and how it's actually resolved (not hand-waved):** moving fluid to the GPU naively reopens the exact problem 8.1 fixed — two processors both thinking they own terrain, which is what made v7's Directory genuinely hard to reason about. 8.3 avoids this with a **command-list pattern** rather than a full revert:

- GPU runs Intent→Claim→Commit every frame (claims resolve via a safe plain word-write race, §7.3, 8.4 — no atomics, no contention hotspot; the CPU-specific red-black decomposition from 8.2 is unnecessary for the shipped path and is retained only for the optional parallelized CPU reference, §7.2, now at brick granularity).
- GPU Commit does **not** write an independently-authoritative fluid buffer. It emits a small, bounded **write-op list** — `(voxel position, new material)` — for cells that actually changed this tick. Most active slots are asleep or unmoved; the list is far smaller than full active-fluid state.
- CPU reads the op-list back (async, still a few frames of latency — smaller data than a full mirror, but latency is not eliminated) and applies it through **the exact same `SetVoxel` path already used for mining and building.** No second terrain-write mechanism exists anywhere.

**This preserves the one invariant that actually mattered — CPU is the only thing that ever writes terrain — with zero exception**, while letting fluid computation run where the hardware wants it to run. It is not v7's model (where GPU and CPU both had write authority and the contradiction had to be explicitly closed); it is GPU-computes, CPU-applies, one writer, always.

**What this costs, stated plainly:** buoyancy/CCD still see fluid moves a few frames late — the same *class* of risk 8.1 removed, now reintroduced in bounded form. The mitigation is not new invention: the CCD depenetration backstop (already spec'd) catches "ended up inside something" regardless of cause, and a speed clamp engages if op-list readback stalls beyond ~2 frames. This is the same safety pattern v7 and pre-8.1 v8 already used successfully in design; it is a deliberate, bounded trade for real CPU headroom during exactly the chaotic scenes that matter, not an oversight.

**What does NOT change:** the CPU fluid reference implementation (Phase 5a) stays — it remains the correctness oracle, proven with conservation unit tests before the GPU port is trusted (Constitution invariant 4, §0.1). Phase 5b (GPU port + command-list + readback) is now the shipped path rather than an optional/deferred one. The Volatile-brick-eviction fix (8.2, §3.10) is unaffected — fluid writes, whether CPU- or GPU-decided, still land through `SetVoxel` and still need immediate freeing of transient fluid-created dense bricks.

---

## What Changed in 8.2 (and Why)

8.2 folds in a third review round. One reviewer found **six concrete, mechanism-level bugs** in the 8.1 spec; the others pushed process structure for AI-assisted development. The bugs are fixed; the *useful* process structure is added as a new Chapter 0; the "rewrite the whole spec as AI prompt-fodder / let the AI write everything" framing is **declined** — see the note at the end of this section for why.

**Six real bugs fixed:**

1. **Transient dense-brick eviction spiral (§3.10, §4.5, §8.3).** 8.1 said writing fluid into a uniform-air brick forces it dense and it "coalesces back when settled" — via a *background* job. A waterfall through open air allocates hundreds of dense bricks/second; if they only free lazily, the pool fills with empty-dense bricks and the LRU valve evicts the player's actual buildings to house air the water already left. **Fix:** a fluid-created dense brick is flagged `Volatile`; when its active-fluid count hits zero it returns to the free-list **immediately**, not on the background sweep.
2. **Shadow-ray LOD mismatch / ghost shadows (§6.5).** 8.1 coarsened the shadow ray by *its own traveled distance*, so a short shadow ray to a distant-but-close-to-player mountain evaluated fine geometry the camera drew coarse — detached shadows. **Fix:** shadow rays select LOD by **camera distance to the sample point**, matching primary-ray geometry exactly.
3. **Parallel fluid CA race (§2.2, §7.3).** 8.1 named `IJobParallelFor` as the fluid scaling lever without specifying that naive per-slot parallelism corrupts a shared grid. **Fix:** mandatory **8-phase (red-black 2×2×2) spatial domain decomposition** — parallel jobs only ever touch non-adjacent regions, so no two threads race a claim cell. Zero locks.
4. **Fluid CCD tunneling (§8.2).** 8.1's swept CCD clamped only on *solid*, so a 60 m/s player phased through a 3-voxel water layer with no splash/drag/damage. **Fix:** the sweep accumulates fluid-traversed distance and hands it to buoyancy/damage even when no solid hit occurs.
5. **Unclustered point-light loop (§3.8, §6.5).** 8.1 summed "lights within range" per pixel with no count bound — 2M pixels × N lights breaches the GPU budget underground. **Fix:** a hard **per-brick light cap** (small fixed array of light indices per populated brick); the raymarcher iterates ≤N, not the global list. (v1 may start with a small *global* cap since v1 lights are sparse; per-brick clustering is the scaling form.)
6. **Clipmap upload spike (§3.7, §4.3).** Quantified: crossing a chunk boundary at 60 m/s rewrites ~8.4 MB of clipmap in one frame, breaching the ≤1.0ms upload budget. 8.1 mentioned spreading updates; 8.2 makes it **mandatory** (spread the ring-slab update across 2–3 frames via velocity prefetch) and gives the explicit toroidal (power-of-two masked) indexing formula so it isn't reinvented wrong.

**Every one of these six is still an unverified prediction** — well-reasoned, but a prediction. Each fix is encoded *and* its underlying risk is tagged as a Phase 5/6/7 measurement, not asserted as fact. The discipline that rejects unmeasured optimizations rejects unmeasured bug-claims too.

**Added — Chapter 0, the Engine Constitution + hard-limits table:** a one-page set of invariants and a table of every hard ceiling (memory pools, light count, active fluid, upload bytes/frame, shader kernel count). This exists because AI assistance (or a tired solo dev) will "helpfully" raise a cap to fix a symptom and silently blow the 8GB budget. The limits are load-bearing and centralized.

**Added — lightweight AI-assist structure** (folded into existing chapters, not a rewrite): per-subsystem state-machine tables where transitions matter (§4.4), "tracer bullet" first steps in the riskiest phases (§13), mandatory per-subsystem diagnostic dumps on validation failure (§10.4), controls-as-data + hot-reload for player feel (§8.1), and cross-system integration milestones (§13).

**Adopted structural simplification — the Brick Handle Pool is inlined into the Chunk (§3.3).** Each populated chunk now owns its `BrickHandle[4096]` directly (allocated on populate, freed on evict) instead of indexing a separate pool. Memory-identical (~16KB/populated chunk either way), one fewer indirection in every `GetVoxel`, fewer places to make an off-by-one. A legal internal change under the freeze-APIs rule.

**Declined — "rewrite the spec as machine-readable prompt-fodder and let the AI write the rest."** Two reviewers pushed this; they contradict each other on the core point, and the correct one wins. The v7 Phase 2 disaster — the entire reason this project was rebuilt — was an *undiagnosable bug in logic the developer couldn't inspect*. "Let the AI generate 10,000 lines you don't deeply understand, debug integration after" reproduces that failure at larger scale. The opposite reviewer is right: the developer must remain the architect who understands the system; vertical slices and CPU-first proving exist to preserve *your* comprehension, not to be optimized away. 8.2 adds structure that helps whether you or an AI types the code (constitution, limits, state machines, diagnostics, tracers) — but does not reorganize around the premise that you'll understand your own engine less. That premise is the trap, not the workflow.

---

## What Changed in 8.1 (and Why)

8.1 folds in external review that stress-tested v8 specifically on Apple Silicon and solo-dev feasibility. Five changes were adopted because they had a real technical or evidence basis; several louder suggestions were **declined** and the reasons are recorded, because a spec that caves to every critic becomes incoherent.

**Adopted:**

1. **The GPU mirror is now a flat clipmap, not a pointer tree.** v8 uploaded a chunk→brick→data pointer chain the raymarcher walked with 2–3 dependent reads per step — exactly the TBDR-hostile pointer-chase v7 §3.3.3 rejected. 8.1 keeps the sparse two-tier structure *on the CPU* (memory savings) but uploads the resident window as a **single flat 3D brick-index grid** the raymarcher indexes with one direct calculation per step (§3.7). This is the single most important correctness change in 8.1.
2. **Fluids are CPU-resident in v1.** v8 ran fluids on the GPU and pulled a 96³ mirror back each frame via `AsyncGPUReadback` for physics — a 1–2 frame delay that, at 60 m/s, means the player moves up to 2m before physics sees water (clip-through risk). 8.1 keeps the fluid CA entirely in a Burst job on the CPU. Physics reads it with **zero frame delay**, and an entire sync paradigm (`AsyncGPUReadback` for the fluid mirror) is deleted (§7, §8.2). The GPU→CPU direction now carries *only* small telemetry.
3. **"Freeze phases" is now "freeze public APIs, not implementations."** The v8 rule ("never modify a passed phase") was psychologically corrosive and practically false — you *will* fix bugs and add hooks in earlier systems. The real, achievable discipline: a passed phase's **public interface** (`IWorldQuery.GetVoxel`, `IEditService.SetVoxel`, `GenerateChunk`, the uploader contract) is frozen; its internals may be bug-fixed and extended. Redesign is the smell to avoid, not editing (§12, §13 preamble).
4. **Phase 1 empirically tests `LockBufferForWrite` vs `SetData` for the read-heavy terrain buffer.** Unity's docs confirm `LockBufferForWrite` may land the buffer in CPU-visible memory that the GPU reads slowly across the bus; for a buffer written once but read millions of times per frame (the terrain clipmap), `SetData` into device-local memory can be dramatically faster to read despite the upload copy. This is a measured go/no-go in Phase 1, not an assumption (§3.7, §13 Phase 1).
5. **Every phase gets an "if stuck 3 days" fallback and a real, file-level build order.** The v8 phase guide was too abstract to execute. 8.1's Chapter 13 lists actual files, actual step order, actual test assertions, and actual failure signatures per phase (§13).

**Declined, with reasons:**

- **"Ship meshes instead of raymarching."** This deletes the project's stated visual identity (0.1m cubes, exact voxel shadows, live fluid rendering). It's a reasonable *different project*, not a simplification of this one. If you want that project, that's a separate decision — this document builds the raymarched one you asked for.
- **"Cut fluids to dormant-only wholesale."** Subsumed by change #2 instead: CPU-resident fluids let you *scope the behavior* down (dormant + local pooling, §7.4) without an architectural amputation, and let you scale it back up in v1.5 without a rewrite.
- **"Merge away the CPU-reference-first fluid split."** The whole point of the split is that fluids were v7's single biggest unmeasured risk. Proving the rules on the CPU with conservation unit tests *before* trusting them is the discipline that de-risks them. With fluids now CPU-resident (change #2), the "port to GPU" half simply becomes optional/deferred — but the CPU-reference-with-tests half stays.
- **"Cut exact shadow rays before measuring."** Phase 1 already measured 7.75ms against the 9.0ms budget under throttling, and the shadow ray is the Phase-2 DDA with a new origin + early-out. The right response to "shadows might be expensive at golden hour" is a specific measurement gate (Phase 7) with a ready fallback rung (half-res distant shadows / hard-only toggle), not pre-emptive amputation of a stated first-class feature. Kept, with the fallback made explicit (§6.5, §9).

---

## Why v8 Exists At All (v7 → v8)

v7 was not broken. Its Phase 1 passed against real measurements (7.75ms GPU under thermal throttling against the ≤9.0ms raymarch budget; dense/run pixel-parity confirmed; two-phase DDA clean). **This is not a repudiation of v7's correctness — it is a repudiation of v7's *debuggability* for a solo developer.**

The concrete evidence: weeks lost to an undiagnosable terrain-deformation bug in v7's Phase 2, matching v7's own documented Phase 2 failure signature ("chunk seams = a pass reading neighbor state; feature drift = something generating lazily"). Root cause is structural: v7 generates terrain as **five ordered GPU compute passes mutating shared Directory memory**, where a bug surfaces as an artifact several passes removed from its cause, in a shader you cannot breakpoint. No chunk format fixes that; only moving the logic somewhere inspectable does.

v8 changes four things versus v7 and carries the rest over:

| # | Change | Replaces |
|---|---|---|
| 1 | Power-of-two bitwise coordinates (`>>`, `&`, no Euclidean div/mod) | v7 §2.3 floor_div/mod (a documented bug source) |
| 2 | Two-tier uniform-or-dense chunk/brick storage, CPU-authoritative | v7 Ch.3 RLE Directory (runs, overflow, dense-override ladder, skip masks) |
| 3 | CPU/Burst world generation, unit-testable | v7 §5.3 ordered GPU compute pass pipeline |
| 4 | CPU-authoritative state; GPU is a flat read-only canvas | v7's bidirectional Directory sync |

**Cut from v1 scope** (deferrable, none core to dig/build/fight): Hydrology flow simulation, the chained-1.6M chaos ladder, the 5-rung degradation ladder. Rivers in v1 are static dormant water.

**Carried from v7** (restated, not redesigned): the raymarcher's macro-skip → micro-step shape (Phase-1-proven), LOD cascades, the fused exact shadow ray, fluid Intent→Claim→Commit, per-chunk delta saves (CRC + atomic rename), LRU eviction as the memory valve. (The sparse BFS light pool is deferred to v1.5; v1 uses simple point lights, §3.8.)

---

## The Two Rules That Shape This Document

**Rule 1 — One new untrusted thing per phase.** If a phase's acceptance test fails, there is exactly one plausible place to look. This is the structural fix for the v7 Phase 2 disaster and it drives the whole memory design (CPU-authoritative → generation and editing are debuggable in plain C#) and the phase plan (Ch.13).

**Rule 2 — Prove correctness where you can breakpoint before chasing speed where you can't.** Generation and fluids are CPU/Burst with unit tests first. The GPU only ever gets data that's already proven correct on the CPU.

**On freezing phases (the 8.1 correction):** when a phase passes, its **public interface** freezes — later phases consume it and never restructure it. Bug fixes and additive hooks to a passed system are normal and fine. *Redesigning* a passed system is the smell to avoid. If a later phase seems to need a passed system redesigned, the phase boundary was drawn wrong.

---

## Table of Contents

0. The Engine Constitution
1. Design Goals
2. Global Constraints
3. Core Memory Model
4. Streaming & Persistence
5. World Generation
6. Rendering
7. Simulation (Fluids) — GPU-Resident, CPU-Applied
8. Gameplay Interface
9. Fault Tolerance
10. Tooling & Validation
11. Performance Budget
12. Module Organization
13. Implementation Guide (Phases 0–8) — the executable core of this document
- Appendix A: Buffer & Struct Layouts
- Appendix B: Bitfields
- Appendix C: Equations
- Appendix D: Data Formats

---

# 0. The Engine Constitution

These are the invariants that survive every phase and every AI-assisted change. If a proposed change violates one, the change is wrong — not the constitution. This chapter is deliberately short enough to re-read in two minutes.

## 0.1 The Twelve Invariants

1. **The CPU is the only thing that ever writes terrain.** All terrain state lives in CPU memory and every terrain change — mining, building, generation, *and fluid moves* — applies through the single `SetVoxel` path. The GPU may **compute** fluid motion (§7) but never writes terrain directly; it emits a bounded write-op list the CPU applies. The GPU is a read-only canvas for rendering everything else.
2. **All coordinates are integer, all coordinate math is bitwise.** Power-of-two units only; `>>` and `&`, never `/` or `%`. `CoordMath` is the single implementation.
3. **No hidden allocations on the hot path.** Pools are pre-allocated. The per-frame render/edit/fluid paths allocate nothing.
4. **Prove on the CPU before the GPU.** Generation and fluids get a CPU reference with unit tests before any compute-shader form is trusted.
5. **One new untrusted thing per phase.** If a phase test fails there is exactly one place to look.
6. **Public APIs freeze on phase pass; implementations may be bug-fixed.** Redesign of a passed system is the smell; editing its internals is normal.
7. **Every subsystem has a hard limit (§0.2) and a visual debugger (§10.3).** No unbounded growth, no invisible state.
8. **Every hard limit is defined once, in `EngineConfig`, and read everywhere.** Tuning is a number change, never a code change.
9. **Simplicity beats elegance; the dumbest implementation that works, wins.** Do not optimize before profiling. If two implementations are equal, keep the one explainable to a tired developer in 30 seconds.
10. **Never trust AI-generated (or any) code without a test or a measurement.** Confidence is not correctness. A well-explained race condition is still a race condition.
11. **Frame-time may degrade under abuse; memory-safety and save-integrity never do.** The one non-negotiable floor.
12. **Fixes and risks are tracked separately.** A plausible bug is a hypothesis with a measurement attached, not a fact.

## 0.2 Hard Limits Table (all defined in `EngineConfig`)

Every ceiling the engine must respect. **An AI assistant will "helpfully" raise one of these to fix a symptom and silently blow the 8GB shared-memory budget** — so they are centralized, load-bearing, and changed only with a re-derivation against §11.3.

| Limit | v1 starting value | Where enforced | Raise only if… |
|---|---|---|---|
| `BRICK_POOL_CAP` | 750,000 bricks (~384MB ×2) | §3.4, §3.6 LRU | Phase 6 shows normal building evicts too aggressively |
| `MAX_ACTIVE_LIGHTS` (v1 global cap) | 32 (placeholder — a handful of placed torches) | §3.8, §6.5 | Phase 7 measurement if v1 content wants more simultaneous lights |
| `MAX_LIGHTS_PER_BRICK` (v1.5 per-brick clustering, not built in v1) | 8 | §3.8, §6.5 | n/a until v1.5 |
| `MAX_ACTIVE_FLUID` | ~500,000 near-player (pool hard cap higher) | §7.4, §7.7 | Phase 5 shows near-player scope insufficient |
| `MAX_CLIPMAP_UPLOAD_BYTES_PER_FRAME` | ~3MB (forces multi-frame spread) | §3.7, §4.3 | never (this is the anti-stutter guarantee) |
| `MAX_FLUID_OPLIST_BYTES_PER_FRAME` | bounded (only changed cells, not full active-fluid state) | §3.9, §7 | Phase 5b measurement |
| `WINDOW_CHUNKS_XZ` / `_Y` | Phase 4 measurement | §4.3 | Phase 4 measurement |
| `MAX_ACTIVE_COLLIDERS` | 540 (or ~12 probes if controller suffices) | §8.1 | never (PhysX object-count guard) |
| `LOD_TIERS` | 3 | §6.4 | never for v1 |
| `MAX_SHADER_KERNELS` | tracked, not hard | §10.3 HUD | reviewed, not auto-raised |

## 0.3 What AI Assistance May and May Not Touch

This project assumes heavy AI code-generation. The line is drawn at **comprehension-critical systems** — the ones where a silent behavioral change corrupts saves or desyncs CPU/GPU and you'd never notice until it's load-bearing.

**AI may freely generate/modify** (with tests): UI, gameplay feel, tools, editor utilities, debug views, particles, tests themselves, and the *internals* of any subsystem behind a frozen API.

**AI may NOT modify without explicit review**: `CoordMath` (coordinate/bitwise math), the chunk/brick/clipmap memory layout and serialization (delta + `world.meta` formats), the CPU/GPU sync contract (§3.9), generation determinism (§5.3), and the streaming state machine (§4.4). A change to any of these can make old saves unloadable or silently desync state — the exact undiagnosable-bug class this whole rebuild exists to avoid.

**The AI debugging rule (the analog of "if stuck 3 days"):** if an AI fails to fix a bug in a subsystem after ~3 attempts, do not accept a 4th increasingly-clever patch — **delete that subsystem's generated code and regenerate from its formal spec + tests.** AI code is cheap; a pile of half-understood patches is the expensive thing. This is only safe *because* every subsystem has a frozen API and tests to regenerate against — which is why those come first.

**Header-first prompting (8.4):** when directing an AI to implement or modify any subsystem, feed it **Chapter 0 (this chapter) + Appendix A/B (struct/bitfield layouts) + the relevant frozen interface** *first*, before the rest of the document or existing code. A full-document-plus-codebase context window dilutes exactly the invariants that matter most, and a diluted context is when an AI is most likely to hallucinate a struct field or quietly "improve" locked logic. Keeping the constitution and layouts as the front of every prompt is cheap and directly reduces that failure mode. *(This is also why Chapter 0 is designed to be extractable as its own standalone `ENGINE_CONSTITUTION.md` — same content, quick to paste at the top of any prompt, without dragging in the whole spec. This document remains the source of truth; the standalone file is a derived convenience copy, not a fork.)*

**The Sibling Pattern (8.4 — naming a technique already used twice in this document):** for any subsystem that gets a nontrivial performance optimization, keep a deliberately **dumb, obviously-correct sibling implementation** alongside the optimized one, used to isolate "the logic is wrong" from "the optimization is wrong." This document already uses this pattern twice — `RaymarchReference` (plain C#) vs. the HLSL DDA (§13 Phase 2), and `FluidReferenceCPU` (Phase 5a) vs. the GPU CA port (Phase 5b) — without previously naming it as a reusable principle. Apply it to any *other* subsystem that later gets a clever optimization (a faster coalescing scan, a smarter eviction heuristic, a batched destruction reducer): write the dumb version first, keep it, and when the optimized version misbehaves, compare against the sibling before guessing.

---

# 1. Design Goals

## 1.1 What We're Building

Terraria's loop — dig, build, fight, get absurd mobility, reshape the world at will — in true 3D. The visual identity is **hard-edged 0.1m cubes**: no marching cubes, no smoothing, no interpolation. At 0.1m per cube against a 1.5m player this reads as a fine-grained voxel sculpture — 15 cubes tall, closer to Teardown's density than Minecraft's. A full day/night cycle with voxel-precise moving shadows is a first-class rendering requirement (§6.5), kept through a measured gate rather than assumed free.

**The design question to ask on every subsystem** (the reviewer's best single piece of advice): *if I delete this entire subsystem, does the game stop being fun?* For digging, building, exploring — yes. For exact shadows, flowing water, long-distance LOD — no, individually. This is why those three are the first things scoped down under pressure (§9 fallback rungs), and why the core loop is what Phases 1–6 build first.

## 1.2 Hardware & Engine Target — Everything Through Unity

Apple Silicon M1, 8GB Unified Memory, Unity 6000.3.10f1. Hard rule: **never touch Metal directly.** Every capability goes through Unity's API surface.

| Capability | Unity API |
|---|---|
| GPU compute | `ComputeShader.Dispatch` / `CommandBuffer.DispatchCompute` |
| GPU buffers | `GraphicsBuffer` (never legacy `ComputeBuffer`) |
| CPU→GPU terrain upload | **tested in Phase 1**: `LockBufferForWrite` vs `SetData` — the read-heavy terrain clipmap may prefer `SetData`'s device-local placement (§3.7) |
| GPU→CPU reads | `AsyncGPUReadback` only, and in v8.1 **only small telemetry** — no fluid mirror, no terrain (§3.9) |
| GPU pass ordering | one `CommandBuffer` per frame |
| CPU parallelism | Burst `IJob`/`IJobParallelFor` + `NativeArray` — **the workhorse of v8.1** (generation, editing, and fluids all live here) |
| Entity physics | classic `Rigidbody`/`BoxCollider` (PhysX) |
| Capability checks | `SystemInfo.supportsComputeShaders`, `maxGraphicsBufferSize` at boot |

**One Unity 6 constraint on every GPU struct (Appendix A):** the HLSL→MSL cross-compiler widens `min16float`/`half` inside buffers to 32-bit. **All GPU-visible structs use only 8/16/32-bit integer fields plus 32-bit floats.**

## 1.3 Non-Goals (v1)

Multiplayer, non-Apple-Silicon targets, save-format migration, wiring/fishing/farming, bit-exact fluid replay, spreading biomes, bounce GI (ambient term stands in), Hydrology flow, the chaos-abuse robustness ladder, and full 4M-worldwide fluid simulation (v1 targets fluid *near the player*, §7.4). Object/NPC/ore placement content is a later document — v8.1 guarantees the substrate supports it with zero further storage work (§5.4).

## 1.4 Performance Targets

- **60 FPS sustained** (16.6ms) at 1080p, two independent lanes (GPU + CPU main thread) each under 16.6ms (§2.2).
- **≤3.0 GB process footprint**, itemized (§11.3), with the brick pool sized *aggressively low first* on a shared 8GB machine and raised only if measurement allows.
- **Zero crashes and zero save corruption under any input.** This is the one hard floor. A frame-time floor under deliberate abuse is explicitly *not* a v1 contract; memory-safety and save-integrity are (§9).

---

# 2. Global Constraints

## 2.1 Memory

Process ceiling **3.0 GB** (§11.3), on a machine where CPU and GPU share one 8GB pool — so every megabyte the brick pools claim is a megabyte the CPU heap and Unity runtime can't use. This is why §3.6 caps the brick pool and §11.3 sizes it low-first.

## 2.2 Frame Budget (16.6ms) — Two Lanes

**GPU lane** (one `CommandBuffer`):

| Pass | Budget | Note |
|---|---|---|
| Raymarch incl. fused sun-shadow rays | ≤ 9.0ms | primary + shadow in one kernel; shadow cost is a Phase 7 gate with a fallback rung |
| Fluid CA (Intent/Claim/Commit, §7) | ≤ 3.5ms | **back on the GPU lane in 8.3** — claims resolve via a safe plain-write race (8.4, no atomics); emits a bounded op-list, does not write terrain directly |
| Local light drain | ≤ 0.5ms | amortized queue |
| **GPU steady total** | **≤ 13.0ms** | fluid portion gated Phase 5b (vs primary raymarch only); the full three-way combination (raymarch + shadow + fluid) is gated in **Phase 7**, the first phase where all three exist (§11.2, audit) |

**CPU main-thread lane** (lightened in 8.3 — this is the point):

| Work | Budget |
|---|---|
| Fluid op-list apply (via `SetVoxel`, §7.3/§8.3) | ≤ 0.5ms — cheap direct writes; GPU already decided *where*, CPU just applies (a Phase 5b measurement, expected far lighter than running the full CA) |
| Terrain upload (dirty chunks → clipmap) | ≤ 1.0ms steady |
| Gameplay: treadmill, CCD, depenetration, buoyancy (vs CPU terrain — bounded-latency for fluid cells, §7.2) | ≤ 1.5ms |
| Generation/decode (Burst, async off main thread) | load frames only |
| StreamManager queues | ≤ 0.2ms |
| Unity overhead, UI, audio, and future gameplay systems (mobs, inventory) | remainder — **this headroom is the reason for the 8.3 change** |

**The lane rebalance is the 8.3 change, in the other direction from 8.1:** fluid computation moves back to the GPU lane (hardware-appropriate for an embarrassingly-parallel CA) and the CPU lane is lightened to just applying a bounded op-list plus its existing orchestration work. This is a direct response to the concern that a chaotic end-game scene loads many CPU-bound systems simultaneously — mining, destruction reduction, streaming, treadmill, buoyancy, future mob/inventory logic — and none of that work can move to the GPU, so it should not compete with something (fluid CA) that can.

**Still unverified, still a measurement gate, not a promise:** the ≤3.5ms GPU fluid figure matches v7's original GPU budget shape for a comparable active-voxel target — a more defensible starting point than 8.1's CPU-repurposed number, since it's at least the right processor for the workload, but it is still unmeasured on this specific engine and hardware. **Phase 5b is where this is proven, not assumed.** Two things to watch specifically:
- **GPU headroom is now tighter** (13.0ms of the 16.6ms budget, vs. 8.1's 9.5ms) — the shadow-ray gate (§6.5, Phase 7) and the fluid gate (Phase 5b) now compete for the same lane. If both land at their budget ceilings simultaneously (raymarch+shadow at golden hour *and* heavy fluid activity), the combined total needs its own explicit capture — add this as a Phase 7 combined-load test, not just each system measured in isolation.
- **The op-list apply cost on the CPU (≤0.5ms) is a placeholder**, not a measurement — cheaper than running the full CA is a reasonable expectation (GPU already decided *where*; CPU just writes), but "reasonable expectation" is exactly the phrase that needs a number attached before it's trusted.

**If the GPU lane doesn't fit once fluid is added back (the fallback, direction updated from 8.2):**
1. Reduce the fluid active-radius (§7.4) — smaller near-player bubble, fewer active GPU threads, smaller op-list.
2. Lower fluid tick rate for fluid outside the immediate few-meter bubble (a cheap two-speed split).
3. If the shadow ray and fluid genuinely cannot both fit, the shadow fallback rungs (§6.5) engage first — shadows degrade before fluid does, since "does the game stop being fun without X" (§1.1) answers differently for the two.
Since v1 targets fluid *near the player* (§7.4), the realistic active count is expected to be far below the 4M ceiling — expected, not yet measured.


## 2.3 Spatial Metrics & Coordinate System — Power-of-Two, Bitwise

**Every spatial unit is a power of two of the one below, so all coordinate math is shifts and masks — no division, no modulo, no sign traps.**

- **Voxel**: 0.1 m cube — atomic unit for terrain and fluid.
- **Brick**: 8×8×8 voxels = 512 voxels = 0.8m³ = a 512-byte material array. *(The "512 bytes = 4 cache lines" alignment is a rationale, not a load-bearing guarantee — the load-bearing property is that 8 is a power of two. Don't justify a decision on cache lines until measured.)*
- **Chunk**: 16×16×16 bricks = 128×128×128 voxels = 12.8m³. Unit of streaming/saving/eviction.
- **Player**: 6×15×6 voxels, camera eye 1.4m.

**Coordinate system:** left-handed, Y-up, matching Unity. Origin at island center; sea level Y=0.

**The chain — identical in C# and HLSL (`CoordMath`, C.1), pure bitwise:**

```
VoxelCoord (int3) = floor(worldPos * 10)      // the one float→int floor; single tested op
localVoxel = VoxelCoord & 7                    // 0..7 in brick
BrickCoord = VoxelCoord >> 3
localBrick = BrickCoord & 15                    // 0..15 in chunk
ChunkCoord = BrickCoord >> 4
voxelIndex = (localVoxel.z<<6)|(localVoxel.y<<3)|localVoxel.x   // 0..511
brickIndex = (localBrick.z<<8)|(localBrick.y<<4)|localBrick.x   // 0..4095
```

Arithmetic shift + two's-complement mask are correct for negative integers by construction, so v7's negative-quadrant mirroring bug class is *removed*, not tested against. The only residual sign subtlety is the single `floor(worldPos*10)`, one op with one test. `CoordMath` is the sole implementation, C# and HLSL byte-identical.

## 2.4 World Size & Height — 3D Streaming

v8.1 streams a bounded 3D box around the player (residency by true 3D distance), not v7's full-height columns. Resident memory is bounded by the streaming window (§4.3), never total world volume — the guarantee delivered by the uniform-sticky-note property (§3), not RLE runs.

| Preset | Width × Depth | Height | Chunks (X×Z) | Height (chunks) |
|---|---|---|---|---|
| Small (default) | 2,560m² | 1,024m | 200×200 | 80 |
| Medium | 3,840m² | 1,536m | 300×300 | 120 |
| Large | 5,120m² | 2,048m | 400×400 | 160 |

## 2.5 Throughput Budget (v1)

| Metric | Value | Meaning |
|---|---|---|
| Burst player speed | 60 m/s (1.0m/frame) | why §8.2's swept CCD + depenetration + the fluid speed-clamp all exist |
| Mining (early/mid/end) | 10 / 40 / 150–200 vox/s | edit path §8.3 sized against 200/s |
| Single explosion | ≤ 400,000 voxels/frame | ~9.2m crater; must not corrupt/leak; may resolve over 2–3 frames (§8.5) |
| Peak active fluid (v1, near player) | target ~500,000; hard pool 4,000,000 | v1 simulates near the player (§7.4); the pool retains 4M headroom but v1 gameplay won't approach it |

**Adversarial memory ceiling (§3.6):** a player checkerboarding distinct materials through their whole visible radius so nothing stays uniform — the case naive sizing fails by ~5–6×, handled by the hard pool cap + LRU. An explicit Phase 6 test.

---

# 3. Core Memory Model

## 3.1 The Whole Model in One Paragraph

Terrain lives in plain CPU memory as a two-level tree of "uniform or populated" nodes. A **chunk** is either *uniform* (one material, ~5 bytes) or *populated* (owns 4,096 **brick** entries). A brick is either *uniform* (one material) or *dense* (a 512-byte array of material IDs). You pay for a real array only where the world is non-uniform — surface skin, cave walls, ore veins, player edits. Everything else is a sticky note. **The CPU is the only thing that ever writes terrain** (§0.1 invariant 1) — but as of 8.3, the GPU **computes** fluid motion and hands the CPU a small list of writes to apply. The GPU also holds a **flat, read-only clipmap** of the resident window for drawing — never a pointer tree, never a direct terrain writer.

```
CPU (sole terrain writer; fluid write-ops applied here via SetVoxel):
  Sparse Chunk Table        — every chunk ever generated/edited     §3.2
  Resident Window           — flat ring array of chunks             §3.3
  Chunk.bricks[4096]        — inlined handle array per populated chunk §3.3
  Brick Data Pool           — the only raw voxel arrays (capped)     §3.4, §3.6
  Fluid write-op buffer     — read back from GPU, applied via SetVoxel §3.9, §7
  Light source list         — simple point lights (v1)              §3.8
GPU (computes fluid, renders everything; never writes terrain directly):
  Terrain Clipmap           — one flat 3D brick-index grid           §3.7
  GPU Brick Data            — flat mirror of dense brick bodies      §3.7
  Fluid Slot Pool + CA      — Intent/Claim/Commit, emits write-ops   §3.5, §7
  Material Registry         — 256 × 32B                              §3.9
  Light source buffer       — positions/colors for the raymarcher   §3.8
```

## 3.2 CPU Sparse Chunk Table

`Dictionary<int3, ChunkRecord>` keyed by `ChunkCoord`, entries only for chunks ever generated/edited. Answers "is this resident, where?" and (via `deltaByteLength>0`) "ever edited?" (the persistence invariant, §4.1).

**Single-writer rule:** only `StreamManager` (main thread) mutates it; worker jobs push completions into a `NativeQueue` drained once per frame on the main thread. No locks anywhere in streaming.

## 3.3 Resident Window & Inlined Brick Handles

The hot path (`GetVoxel` during physics/editing/generation) must not hash. Resident chunks live in a **flat fixed-size 3D ring array** indexed by `(ChunkCoord - windowOrigin) & windowMask` per axis (window dims power-of-two, §4.3): O(1), no dictionary on the hot path. The `Dictionary` (§3.2) serves only the cold "ever existed / where's the delta" question.

Each resident `Chunk` (A.2): uniform (one material, no brick array) or populated. **A populated chunk owns its `BrickHandle[4096]` directly (8.2 — inlined, no separate handle pool):** the 16KB table is allocated when the chunk populates and freed when it evicts. Each `BrickHandle` (A.3): uniform (material ID) or dense (index into the Brick Data Pool). `GetVoxel` becomes `chunk.bricks[brickIndex]` — one fewer indirection than 8.1's handle-block-pool design, one fewer place for an off-by-one, and memory-identical (~16KB/populated chunk either way). Backing the 4096-handle arrays with a pooled allocator (to avoid GC churn on populate/evict) is an internal implementation detail behind the frozen `GetVoxel`/`SetVoxel` API — do it if Phase 4 shows GC pressure, skip it otherwise; either way the public path is `chunk.bricks[index]`.

## 3.4 Brick Data Pool — The Only Raw Voxels

One pre-allocated array of **512-byte brick bodies** (A.4). A dense handle indexes it. This is v7's Static Brick Pool, direct-indexed. **Hard-capped** against budget (§11.3), sized low-first on the shared 8GB machine; the cap is enforced by the LRU valve (§3.6).

## 3.5 Fluid Slot Pool — GPU-Resident (8.3)

Individually-addressable active-fluid slots, **on the GPU**, updated by the fluid CA compute dispatch (§7). A slot holds home-voxel address, cached material, sleep/viscosity counters (A.5). **Material truth always lives in the terrain byte** (§3.10); slots are a pure motion overlay whose decisions reach terrain only through the write-op list the CPU applies via `SetVoxel` (§7.2). Physics reads fluid presence from CPU terrain bytes — exact for settled fluid (the overwhelmingly common case: a dormant drop *is* an ordinary terrain byte), bounded-latency for fluid mid-move (§8.2). Pool sized for the v1 near-player target (§2.5) with headroom — far below v7's 25M-slot worldwide pool, because v1 simulates near the player only (§7.4); exact size is a Phase 5b gate. A same-layout CPU mirror of the slot struct backs `FluidReferenceCPU`, the Phase 5a correctness oracle (§7.2).

## 3.6 The Memory Worst Case & The LRU Valve

Memory grows only when a uniform node is forced dense. Adversarial checkerboarding through the whole visible radius would drive every resident chunk fully dense (~2MB/chunk) to ~5–6× the ceiling — unbounded relative to budget. **The valve:** the Brick Data and Handle pools are hard-capped; past a high-water mark, `StreamManager` LRU-evicts the coldest resident chunk, returning its block + dense bricks. The player occupies a tiny fraction of the window; cold chunks ≥2 rings out always exist, so **the triggering edit always succeeds** — you push the eviction radius inward, never fail. Under abuse the far edges of a giant checkerboard base lose detail and pop back on turn; never a crash, never lost progress (edits are in the delta, §4.2). Real pool/window sizes and whether eviction is visually acceptable under *normal* aggressive building are a **Phase 6 test**, not an assumption.

## 3.7 The GPU Terrain Clipmap — Flat, One-Way, Read-Only (8.1's Key Fix)

**v8's mistake, corrected:** v8 uploaded a `ChunkMirror → BrickMirror → BrickData` pointer chain; the raymarcher did 2–3 dependent memory reads per DDA step to resolve a voxel — the TBDR pointer-chase v7 §3.3.3 explicitly rejected. 8.1 keeps the sparse tree **on the CPU** and uploads a **flat clipmap** the GPU indexes directly.

**Structure:**
- **Terrain Clipmap** — a single flat `GraphicsBuffer<uint>` sized to the resident window in *bricks* (windowChunks × 16³ bricks). Entry `[brickX,brickY,brickZ]` (indexed by one bitwise calc, no tree walk) is either "uniform, material in low byte" or "dense, index into GPU Brick Data." **One direct index per macro step.** The clipmap re-centers on the player like a texture clipmap: as the player crosses a chunk boundary, only the newly-entered slab of brick entries is rewritten (ring-buffer addressing, same masking as §3.3), not the whole grid.
- **GPU Brick Data** — flat `GraphicsBuffer` mirror of the CPU Brick Data Pool's dense bodies; a dense clipmap entry indexes it. Micro-stepping reads 512 contiguous bytes — cache-coherent.

**Why this is both faster and simpler:** the raymarcher's inner loop is now `index = bitwise(pos); entry = clipmap[index];` — one read to know if it can leap a uniform brick, a second only when it must enter a dense brick. No dependent-pointer latency, maximal TBDR cache coherency. It costs a slightly larger flat footprint (uniform bricks occupy a 4-byte entry in the clipmap even though their CPU form is smaller) — an accepted trade, quantified in §11.3.

**Upload discipline (the safety property):** the CPU marks chunks dirty on any change; once per frame `TerrainUploader` rewrites only the changed clipmap entries + any changed dense bodies. **No readback, no N-1 contract, no generation revalidation for terrain** — the GPU cannot write terrain, so "did the GPU change this?" has no answer to get wrong.

**The upload mechanism is tested, not assumed:** the terrain clipmap is written rarely but **read millions of times per frame** by the raymarcher. Unity's `LockBufferForWrite` may place it in CPU-visible memory the GPU reads slowly across the bus; `SetData` pays an upload copy but lands device-local for fast reads. **Phase 1 benchmarks both on the actual M1 and picks the winner for this buffer.** (Rarely-read buffers may still prefer `LockBufferForWrite`; the choice is per-buffer.)

**The boundary-crossing upload spike (8.2 — fixes micro-stutter):** at 60 m/s a chunk boundary is crossed every ~0.21s, and re-centering the clipmap ring on one new chunk-slab rewrites on the order of **~8 MB of clipmap entries in a single frame** — far past the ≤1.0ms upload budget, a visible hitch. **Fix (mandatory, not optional):** the ring-slab update is **spread across 2–3 frames** using the velocity prefetch of §4.3 — as the player *approaches* a boundary, the incoming slab is uploaded incrementally ahead of need, capped at `MAX_CLIPMAP_UPLOAD_BYTES_PER_FRAME` (§0.2). The clipmap is toroidal (ring-buffer) addressed so only the entering slab is touched, never the whole grid. **Explicit toroidal index (in `CoordMath.hlsl`, so it is never reinvented as a naive wrap):** for power-of-two window dimensions, `wrapped = worldBrick & (WINDOW_BRICKS − 1)` per axis, then `flat = wrapped.x + wrapped.y·W + wrapped.z·W·H`. *(Phase 4 test: sustained 60 m/s traversal shows no upload frame exceeding the byte cap and no stutter on boundary crossings.)*

**The missing invariant (8.4): near-player chunks are never subject to this spread.** The multi-frame throttle exists for the *incoming ring* — chunks the player hasn't reached yet. It must **never** delay a chunk the player is standing in, adjacent to, or has just edited: if the wall you just broke were subject to the same multi-frame queue as an unreached horizon chunk, you could see or collide with a stale wall for a frame or two — a real, player-visible bug, not a theoretical one. **Rule:** any chunk within the immediate collision/CCD radius (§8.2) or that was just marked dirty by an edit (§8.3) uploads **this frame, unconditionally**, exempt from `MAX_CLIPMAP_UPLOAD_BYTES_PER_FRAME`. Only chunks entering the window *ahead of* the player via prefetch are subject to the spread. *(Phase 4/6 test: dig or place a block at point-blank range while sprinting — the change is visible and collidable the same frame, with zero exception, even mid-boundary-crossing.)*

**Residual risk + mitigation:** the clipmap can silently drift from CPU truth (forgotten dirty-mark, struct-layout mismatch, off-by-one). A **debug-only clipmap validator** (§10.4) periodically reads back a region and byte-compares against the CPU source, failing loudly. Built Phase 2, same day as the uploader.

## 3.8 Lighting Storage — Simple Point Lights (v1)

**v8's BFS light-propagation pool is deferred to v1.5** (it's a lot of code for "torches light caves," and light-around-corners isn't a v1 necessity). v1 uses a **flat `GraphicsBuffer<LightSource>`** (position, color, range — A.6).

**Light cap (fixes the per-pixel light death spiral) — global cap is the v1 default (8.4, tightened from 8.2's per-brick-first framing):** a naive "sum all lights in range per pixel" loop is 2M pixels × N lights — 64 torches in a cave ≈ 133M attenuation calcs/frame, breaching the GPU budget underground. **v1 default:** a single small **global** cap `MAX_ACTIVE_LIGHTS` (§0.2) on the total number of simultaneously-active lights the raymarcher considers — simplest possible fix, sufficient because v1 lights are genuinely sparse (a handful of placed torches, not a lit city). **Per-brick clustering (each brick carrying its own ≤8 nearby light indices) is deferred to v1.5** as the scaling form for dense torch placement — building it now would be solving a problem v1's content scope doesn't have. Attenuation `atten = 1/(1 + d²·k)` (C.11); debug-viewable, tunable by eye. *(Phase 7 test: place `MAX_ACTIVE_LIGHTS` torches in view, confirm the raymarch budget holds; placing more than the cap should degrade gracefully — furthest-from-player lights drop first — not silently ignore new torches.)* The sparse BFS pool returns in v1.5 for around-corner light, behind the same shading interface so terrain traversal is untouched.

## 3.9 Material Registry & The Sync Contract

**Material Registry:** 256-entry `GraphicsBuffer<MaterialData>` (32B, A.7), baked at boot from code-defined tables (§5.5). 8 KB.

**The complete v8.1 sync contract:**

| Direction | What crosses | Mechanism | Staleness |
|---|---|---|---|
| CPU → GPU | terrain clipmap, brick data, registry, light sources, camera params, fluid slot state | `SetData` or `LockBufferForWrite` per §3.7's per-buffer test | none (dispatch after write) |
| GPU → CPU | **fluid write-op list** (bounded — only cells that changed this tick, §7.3) | `AsyncGPUReadback` | ≤2-3 frames, bounded; mitigated by CCD depenetration + speed clamp (§8.2), never a correctness risk (§0.1 inv. 1 still holds — CPU applies every op via `SetVoxel`) |
| GPU → CPU | small telemetry (frame timing, occupancy) | `AsyncGPUReadback` | irrelevant (telemetry) |

**The closed rule (updated for 8.3):** CPU logic never reads GPU *terrain* memory, and the GPU never writes terrain directly. The one GPU→CPU state path that exists is the **fluid write-op list** — small, bounded, and applied through the same `SetVoxel` every mining/building edit uses. This is narrower than v7's Directory-read contradiction (there, both processors read *and* wrote a shared structure) and narrower than pre-8.1 v8's full 96³ fluid-mirror readback (this is a bounded list of *changes*, not a full state cube). The CPU remains the sole terrain authority with zero exception; only the *decision* of where fluid moves happens elsewhere.

**Frame pass order (8.3):**
`GPU CommandBuffer: terrain upload (dirty, from last frame's applied ops + edits) → fluid Clear/Intent+Claim/Commit → emit write-op list → light drain → raymarch (primary + fused shadow) → present. Then, off the critical path: async readback of this frame's write-op list, applied via SetVoxel next frame, marking chunks dirty for the following upload.`
Player edits (mining/building) are applied to CPU terrain and uploaded before the raymarch, so *those* are never stale. Fluid moves have the readback-bound latency described above — the raymarcher renders the fluid state as of the *last applied* op-list, not the tick that just ran on GPU this frame. This is the one place in the engine where "what's drawn" and "what physics + gameplay know" can differ by a couple of frames, and it's bounded and mitigated (§8.2), not silent.

## 3.10 The Authoritative-Byte Rule

The terrain material byte (dense array, or implied by a uniform brick/chunk) is **always the current visible material, awake or asleep.** The fluid CA writes every move into the terrain bytes (§7.3), so the raymarcher and fused shadow rays read one authoritative source with zero knowledge of the fluid pool. Live pouring fluid renders and shadows correctly for free — it *is* terrain bytes mid-flow.

**The Volatile transient rule (8.2 — fixes the eviction spiral):** writing fluid into a uniform-air brick forces it dense (a moving drop makes its brick non-uniform). Fluid pouring through open air would otherwise allocate hundreds of dense bricks/second that only free on the background coalescer — filling the pool with empty-dense bricks and triggering the LRU valve to evict the player's *real* structures to house air the water already left. **Fix:** a dense brick created by fluid entering uniform-air is flagged `Volatile` (a bit in the brick's handle). When a `Volatile` brick's active-fluid count drops to zero, its 512-byte body is returned to the free-list **immediately** — not on the background sweep. Non-volatile dense bricks (dug tunnels, built structures) still coalesce lazily via §4.5.

**How the count is maintained without contention (8.4 — clarified against the actual 8.3 shipped design):** after 8.3 moved fluid computation to the GPU, the CPU's role is applying a small bounded write-op list **sequentially** (§7.2) — there is no parallel Burst job hammering a shared `activeFluidCount` from many threads in the shipped path, so there is no atomic-cache-bouncing risk to fix there in the first place; a plain, non-atomic increment/decrement during the sequential apply loop is already safe and correct. The one place this genuinely matters is the **optional parallelized CPU reference** (Phase 5a, if `IJobParallelFor`'d for speed, which it need not be) — there, maintain `Volatile` active-counts via a **post-commit single-threaded sweep** over touched bricks rather than mid-tick atomic increments scattered across parallel job threads, avoiding false-sharing/cache-line contention in that specific optional path. *(This is a Phase 5/6 measurement either way: confirm transient dense-brick count tracks active-fluid count, not cumulative fluid distance traveled.)* (Writing into a uniform brick forces it dense first; it coalesces back when the fluid settles, §4.5.)
# 4. Streaming & Persistence

## 4.1 Principle

Procedural generation is immutable law — **terrain is never saved.** Only player deviations (the Delta Ledger) persist, one file per touched chunk. A never-edited chunk costs zero bytes, and the *absence* of a delta file is itself information: that chunk is bit-exactly its baseline. (In v1 this invariant carries no runtime load — v7 built river connectivity on it; v8 cuts Hydrology — but it stays because delta save/load depends on it and it costs nothing.)

## 4.2 Delta Format & Atomic Saves

On unload (distance eviction or pool-pressure eviction, §3.6 — both funnel here), each populated chunk's bricks are compared against a fresh baseline regeneration (§5). Bricks matching baseline drop free; deviating bricks serialize (D.1): uniform bricks as a material ID, dense bricks as their 512-byte body, each tagged with its `brickIndex`. Decode is near-memcpy.

**Fault tolerance:** CRC32 trailer; write `.tmp` then atomic rename. On load, CRC mismatch or truncation ⇒ **discard the delta, regenerate the pristine baseline** for that one chunk. Worst case: edits lost in one 12.8m chunk — never a corrupted world, never a crash.

## 4.3 Paging Window & Budgets

Sized against **60 m/s**: a 12.8m chunk boundary crossed every ~0.21s flat-out. Window: a power-of-two box (for the §3.3 ring-index masking) enclosing the 128m LOD0 radius plus a hysteresis ring, bounded vertically. **The exact window dimensions are a Phase 4 measurement**, chosen to cover 128m + one ring horizontally and a sensible vertical band (the player rarely needs the full 1,024m height resident). Placeholder for derivation, not a decided number: 32×32 chunks horizontal (409.6m, comfortably over 128m + ring) × 16 chunks vertical (204.8m band centered on the player).

- **Steady-state**: terrain upload ≤1.0ms/frame CPU.
- **Burst**: ring-crossing frames generate/decode new chunks async on worker threads; only the upload of finished chunks touches the main thread, batched.
- **Prefetch ordering**: incoming edge chunks load by angular proximity to the velocity vector.

## 4.4 Lifetime State Machine

`Unloaded → Loading → Resident → Saving → Unloaded`. All transitions performed solely by `StreamManager` draining its queue once per frame (§3.2's single-writer rule). No locks.

**Explicit transition table (8.2 — so the state logic is a contract, not prose):**

| From | To | Trigger | Condition |
|---|---|---|---|
| Unloaded | Loading | streaming request (enter window) | free residency slot exists |
| Loading | Resident | generation/decode completes | CRC passes (loaded) or generation done |
| Resident | Saving | eviction selected | `deltaDirty == true` |
| Resident | Unloaded | eviction selected | `deltaDirty == false` (nothing to save) |
| Saving | Unloaded | save completes | atomic rename succeeded |

**Forbidden transitions (assert on violation):** `Loading → Unloaded` (finish or fail explicitly, never abandon mid-load), `Saving → Loading` / `Saving → Resident` (a chunk mid-save is immutable until the rename lands — the Saving-eviction lock), any transition not in the table above. These asserts are cheap and catch the entire class of "why did this chunk revert / double-save" bugs at the source.

## 4.5 Eviction & Coalescing

**Eviction** (distance or pool pressure): save delta → free the chunk's inlined `BrickHandle[4096]` array and return its dense bricks to the Brick Data free-list → mark the GPU clipmap slot free → remove from the resident window.

**Coalescing** (the opposite of going dense — reclaims memory during play): a low-priority background job periodically scans populated chunks. A dense brick whose 512 bytes are all one material becomes a uniform brick (512B reclaimed). A populated chunk whose 4,096 bricks are all uniform *and equal* becomes a uniform chunk (16KB reclaimed). This is what lets a filled-in tunnel or a settled fluid region return to sticky-note cost. Coalescing only ever *frees* memory and only touches already-resident chunks — it can never fail or race (single-writer, main-thread-scheduled). It replaces v7's per-column run compaction with a far simpler "is this array uniform?" check.

---
# 5. World Generation — CPU/Burst, Unit-Testable

## 5.1 The Core Change

v7 generated terrain as five ordered GPU compute passes mutating shared Directory memory — the source of the undiagnosable Phase 2 bug. **v8 generates terrain as plain CPU functions (Burst-compiled `IJob`s), producing a chunk's brick data as an ordinary array you can assert against in a unit test and step through in a debugger.** Generation output is trusted *before* it is ever rendered (Phase 3 proves generation with unit tests; Phase 3b is the first time it's drawn — §13).

## 5.2 Two-Stage Model

**Stage 1 (once, world creation, CPU main thread):** global planning. Generates the `FeatureAnchor` set (non-overlapping bounding volumes for mountains, craters, etc.), Voronoi biome seeds, and persists them in `world.meta` (D.2), so generation is deterministic regardless of exploration order. Sub-second even for Large.

**Stage 2 (per chunk, on demand, Burst job):** a pure function `GenerateChunk(seed, anchors, biomeSeeds, ChunkCoord) → brick data`. Deterministic and order-independent: the same chunk always generates identically. This is the function unit tests hammer in Phase 3.

## 5.3 The Generation Function — Simple, Ordered, In-Process

For a requested chunk, the Burst job runs these steps in order, writing into a local brick array, then hands the finished array to `StreamManager` for residency + upload. **All steps are ordinary code in one job — no cross-pass shared-memory hazards, because there is no shared memory; each chunk generates in isolation into its own array.**

1. **Heightfield & biome** — for each of the chunk's 16×16 brick columns (XZ), sample the 2D heightfield and Voronoi biome ID. This decides, per column, where air/surface/bulk/deep boundaries fall and which biome's strata apply. *(2D heightfield + strata, per the deferred-content note — no 3D noise required for v1 terrain; 3D features come from anchors in step 3.)*
2. **Fill uniform bricks** — every brick fully above the surface is uniform Air; every brick fully in the bulk is uniform bulk-material (the biome's bulk stratum — stone for Forest, mossy stone for Jungle, sandstone for Desert, etc.); every brick fully in the deep layer is uniform deep-material. This is where the sticky-note economy pays off: the vast majority of a chunk's 4,096 bricks are set in this step at ~5 bytes each.
3. **Dense only the surface skin & feature intersections** — a brick straddling the surface boundary, or intersected by a feature anchor (a cave, an overhang, a mountain's carved profile), becomes dense and is filled voxel-by-voxel. This is the only step that allocates 512-byte arrays, and only where the world is genuinely mixed.
4. **Biome material assignment** — the material written in steps 2–3 is chosen from the column's `BiomeDefinition` strata (surface/bulk/deep). The storage mechanism is identical across biomes; only the material IDs differ. *(This is why "why stone?" has a clean answer: the bulk material is per-biome data, not a hardcoded assumption.)*

**Determinism guarantee:** `GenerateChunk` is a pure function of its inputs. Identical worlds regardless of exploration order. This is *easier* to guarantee and test in v8 than v7 — plain C# unit tests over a pure function, no reasoning about GPU thread-group execution order.

## 5.4 Substrate Guarantee for Future Content (Ores, NPCs, Objects)

Object/ore/NPC *placement* is deferred to a later content document, but v8 guarantees the substrate supports it with **zero further storage work**:

- **Ore veins**: a vein is a small set of voxels inside the bulk. Placing it is exactly the "brick goes dense, a few voxels get the ore material" operation that mining and building already use (§8.3). No new mechanism.
- **Structures/prefabs**: writing a prefab is a batch of `SetVoxel` calls (§8.3), forcing bricks dense as needed. No new mechanism.
- **NPCs/objects**: entities are Rigidbodies (§8.1), independent of terrain storage entirely.

The content document adds *data* (where ores go, which structures exist) and possibly a placement pass in Stage 2, but touches no memory-layout code. This satisfies the "additions fine, modifications not" rule for the terrain subsystem.

## 5.5 v1 Content Scope

- **Heightfield + coastline** producing an island bounded by ocean (ocean = the boundary condition beyond the coastline radius, not a biome entry).
- **A handful of biomes** (Forest / Desert / Snow / Jungle as a starting roster), each a `BiomeDefinition` (surface/bulk/deep strata).
- **A handful of feature anchors** (a mountain, a crater, caves) as `FeatureDefinition`s carved in step 3.
- **Static dormant water** placed by generation where the heightfield dips below sea level or a feature holds a pool. No flow (Hydrology cut). It renders and behaves as settled liquid (§7.6).

**Content is defined in code for v1** (adopted feedback): `Materials`, `Biomes`, `Features` are plain static C# tables (`public const byte Stone = 2;`, `static BiomeDefinition Forest = new(...)`). This is unit-testable, debuggable, diffable, and needs no boot-time asset baking — and the code *is* the registry snapshot, so the "a content update alters old saves" problem disappears (the definitions are versioned with the source). The ScriptableObject asset pipeline returns in v1.5 when designers need to tweak content without recompiling; `world.meta` reserves a version-hash field for that transition.

---
# 6. Rendering — Carried From v7, Retargeted to the New Storage

## 6.1 Aesthetic Contract

Raw axis-aligned cubes, flat-shaded, hard edges, no smoothing. Normals come from the DDA step mask (every face is ±X/±Y/±Z). The look is carried by albedo, the lighting of §6.5, and voxel ambient occlusion (VAO). The unsmoothed 0.1m cube is the fixed geometric commitment; beauty comes from lighting, atmosphere, and shading, never geometric smoothing. Palette/mood/post are deferred to visual prototyping in Phases 2 and 7.

## 6.2 Ray Generation

A `ScriptableRendererFeature` + `CommandBuffer` intercepts camera matrices and dispatches one thread per pixel (2,073,600 at 1080p; 8×8 groups). Rays reconstruct from inverse projection/view; the result `RWTexture2D` blits to the camera. Unity's mesh pipeline renders only Rigidbody entities, composited by depth.

## 6.3 DDA Traversal (Clipmap-Aware) — Same Shape As v7's Phase-1-Proven Traversal

The traversal is structurally identical to what Phase 1 already measured working — macro-skip then micro-step — just reading the flat clipmap (§3.7) instead of RLE runs. **One direct index per macro step, no pointer tree** (the 8.1 clipmap fix).

**Macro-step (brick granularity):** at the ray's current position compute the flat clipmap index (one bitwise calc, C.1/§3.7) and read that one entry:
- **Uniform brick** (Air → advance to the brick's far exit plane in one step; a ray crossing open sky costs one lookup per brick, not per voxel; solid → intersect at the entry boundary and shade without touching brick data).
- **Dense brick** → micro-step. No chunk→brick→data pointer chase — the clipmap entry is the answer in one read.

**Micro-step (voxel granularity, inside a dense brick):** 0.1m DDA through the 8×8×8 brick. `voxelIndex` per C.1; fetch the byte; `0x00` (Air) → step, `>0x00` → hit; normal from step direction. Bounded at ≤8 steps per axis crossing — the *inherent, benign* DDA cost v7 §6.3 already identified as fine (it was never the divergence problem; only v7's 1,080-brick dense *columns* needed a skip mask, and those don't exist here — a dense unit is a tiny 8³ brick, so no skip mask is needed at all). Under the authoritative-byte rule (§3.10) this byte is always the true current material including fluid mid-flow.

**No skip mask, no run scan** — the macro-skip is a direct table lookup (uniform → leap), the micro-step is a bounded 8-cube walk. This is strictly simpler than v7's run-scan + dense-column-skip-mask traversal, and covers the same cases.

## 6.4 LOD Cascades — 3 Tiers in v1 (adopted feedback)

v8 specified 5 LOD tiers; **v1 uses 3** — each extra tier is more memory, downsampling, validation, and bugs, for detail nobody notices at distance. The two dropped tiers return in v1.5 if long-distance clarity ever demands them.

| LOD | Voxel | Range |
|---|---|---|
| 0 | 0.1m | 0–128m (full raymarch + physics boundary) |
| 1 | 0.4m | 128–512m |
| 2 | 1.6m | 512m–world edge |

Derivation C.5 (a 0.1m voxel subtends ~1px at ~103m at 60° vFOV @1080p, rounded to 128). LOD1–2 populated by majority-vote downsampling into flat cascade pools at chunk load (ceiling measured in Phase 2). **One traversal algorithm parameterized by voxel size** — no separate code path per tier, and it serves shadow rays too (§6.5).

## 6.5 Lighting — Two Systems, One Combine

**Local emissive light — simple point lights (v1, §3.8):** a flat `GraphicsBuffer<LightSource>` of (position, color, range). On a surface hit the raymarcher sums attenuated contributions from lights within range (`atten = 1/(1 + d²·k)`, C.11). No propagation, no BFS, no per-brick storage — light does not bend around corners in v1 (a torch behind a wall does not light the far side). This is a deliberate v1 simplification; the sparse BFS propagation pool (v7-style) returns in v1.5 for around-corner light, behind the same shading interface so the terrain traversal is untouched by the swap.

**Directional sun & exact voxel shadows — the fused shadow ray (kept, with a measured gate and a ready fallback):** no shadow maps (projecting far-cascade texels onto 0.1m cubes guarantees acne/Peter-Panning). Instead, in the **same raymarch kernel**, immediately after a pixel's primary ray resolves a hit, the thread fires **one secondary DDA ray from the hit voxel toward the sun** through the same clipmap/LOD data:
- **Origin**: the center of the first voxel *adjacent* to the hit face (start one cell off the surface along the face normal). Self-intersection is impossible by construction — no bias to tune. Acne and Peter-Panning cease to be a possible bug category, because there is no resolution mismatch between what renders and what shadows: it is the same data.
- **LOD (8.2 — fixes ghost shadows)**: the shadow ray selects LOD by **camera distance to the current sample point**, NOT by the shadow ray's own traveled distance. The 8.1 rule (coarsen by traveled distance) was wrong: a short shadow ray to a mountain the camera renders coarse would evaluate it at fine LOD, so the cast shadow wouldn't match the on-screen silhouette — detached, floating shadows. Sampling by camera-distance guarantees the shadow evaluates the *same* geometry the primary ray drew. *(HLSL: `shadowLOD = SelectLODTier(length(sampleWorldPos − cameraWorldPos))`.)* Phase 7 test: a player in a distant mountain's shadow sees the shadow edge align exactly with the visible silhouette.
- **Early exit & cost**: occlusion-only (first hit = done), skips sky pixels (no primary hit ⇒ no shadow ray), leaves terrain fast at upward sun angles via macro-skip, clamps at max distance (island diagonal). Grazing golden-hour rays travel farthest but mostly through uniform-Air leaps at coarse LOD — **measured in Phase 7, not assumed**, against the ≤9.0ms combined budget.
- **Wave-uniform branch guard (8.4):** GPU threads execute in lockstep groups (waves/warps); if even one lane in a wave needs a shadow ray, the whole wave pays for entering that divergent branch, even though only the needing lanes are unmasked. Guard the shadow branch with `WaveActiveAnyTrue(hasPrimaryHit)` so a wave that is *entirely* sky skips instantiating the shadow-ray code path altogether, rather than every lane in an all-sky wave paying branch-entry cost for nothing. This is a real, standard GPU technique, not a hypothetical — worth building into `Raymarch.compute` from the start rather than retrofitting after a Phase 7 budget miss.
- **Soft penumbrae without multi-sampling** (C.10): the single ray tracks its minimum clearance ratio to near-miss occluders and maps it to a penumbra factor. Hard shadows remain a toggle.

**Day/night**: sun direction + color are two animated uniforms from the in-game clock. No cascades to re-render, no staleness — the shadow is exact every frame at every distance. Below the horizon: skip shadow rays (sun term = 0), ambient carries the night. Because shadow rays read the authoritative bytes (§3.10), **live pouring fluid casts correct moving shadows for free**.

**The shadow-cost gate and fallback rung (adopted feedback):** shadow rays double the ray count in the worst case. The claim "shadow work fits in the ≤9.0ms raymarch budget" is **not assumed — it is a specific Phase 7 capture at golden hour** (longest grazing rays), Performance State checked. If it fails: fallback rung 1 = distant shadow rays (beyond LOD1) drop to half-resolution with neighbor reuse (near shadows, the ones by the player, untouched); rung 2 = a hard-shadow-only mode (no penumbra tracking); rung 3 = shadows off, sun becomes a flat N·L term with ambient. The feature ships if the gate passes; the game ships regardless because the fallback rungs exist. This is why shadows are *kept* rather than pre-cut — measurement decides, not intuition.

**Final combine** (C.9): `color = albedo × (sunColor × sunVis × max(0, N·L) + pointLights + ambientSky) + emissive`, then VAO. `ambientSky` is a cheap hemisphere term, also the stand-in for bounce light into cave mouths (true GI is a non-goal). Underground reads near-zero ambient — caves are genuinely dark and torch-lit.

## 6.6 Materials

The 1-byte ID indexes the Material Registry for albedo/emissive; emissives additionally enqueue into the light system when placed.

---

---

# 7. Simulation (Fluids) — GPU-Resident, CPU-Applied (8.3)

## 7.1 Model

Discrete falling-sand fluid in 3D: every active cell is one drop/grain in a 0.1m³ voxel — no velocity fields, no fractional mass. Motion is *decided* by a GPU compute CA; motion is *applied to terrain* exclusively by the CPU (§0.1 invariant 1). **Material truth always lives in the terrain byte** (§3.10), never in GPU-side fluid state alone.

## 7.2 Why GPU, With a CPU Reference for Correctness (the 8.3 change)

8.1 moved fluids fully to the CPU to eliminate `AsyncGPUReadback` latency risk. **8.3 moves the computation back to the GPU** for two reasons that outweigh that risk once properly mitigated:

- **Hardware fit.** Fluid CA is close to embarrassingly parallel (independent per-cell decisions, contention only at claim time, resolved by a safe plain-write race, §7.3 — no atomics needed at all). This is what a GPU is for; CPU Burst SIMD is a comparatively weak fit for the same workload even parallelized.
- **CPU orchestration headroom.** The CPU main thread also carries terrain upload, the treadmill, CCD, buoyancy sampling, destruction reduction, streaming, and future mob/inventory/UI logic — all inherently serial and un-offloadable. In a chaotic end-game scene (chained explosions into a flooded crater while the player mines) every one of those systems spikes at once; fluid computation should not compete with them for the same scarce serial budget.

**The command-list pattern (this is what makes it safe, not a hand-wave):** GPU Commit does **not** write an independently-authoritative terrain buffer — that would reopen the exact CPU/GPU dual-authority contradiction 8.1 closed. Instead:
1. GPU runs Clear → Intent → Claim → Commit every frame. Claims resolve via a **plain word write**, not an atomic (§7.3, 8.4) — no CPU-style parallel-race concern, and no atomic-contention hotspot either.
2. Commit appends `(voxelPosition, newMaterial)` to a small **write-op list** — only for cells that actually changed this tick. Sleeping/unmoved slots produce nothing.
3. CPU reads the op-list back via `AsyncGPUReadback` (bounded size, not a full state cube) and applies every op through **the exact same `SetVoxel` path used by mining and building** (§8.3). No second terrain-write mechanism exists anywhere in the engine.

**What this preserves:** the CPU remains the *only* thing that ever writes terrain, with zero exception — §0.1 invariant 1 holds exactly as stated. **What this costs:** the applied fluid state lags the GPU's decision by roughly the readback pipeline delay (typically 1–3 frames) — the same *class* of latency 8.1 removed, now reintroduced in bounded, mitigated form (§8.2: CCD depenetration backstop + a speed clamp on stale op-list delivery). This is a deliberate trade for CPU headroom during exactly the scenes that need it, not an oversight.

**The CPU reference implementation is kept, not discarded** (Constitution invariant 4, §0.1): a single-threaded CPU version of Clear/Intent+Claim/Commit is built first (Phase 5a), proven against conservation unit tests in a debugger, and used as the correctness oracle the GPU port (Phase 5b) is validated against. The GPU version is what ships; the CPU version is what tells you the GPU version is right. The CPU-specific 8-phase red-black spatial decomposition (needed only if you parallelize the CPU reference with `IJobParallelFor`) is retained as a technique note in §7.3 for that reference implementation — it is not needed on the GPU path, where atomic claims already resolve races natively.

## 7.3 Intent → Claim → Commit — No Atomics (8.4)

The CA tick (one GPU dispatch per tick; the CPU reference mirrors this logic single-threaded or phase-parallel):

- **Clear**: reset claim cells for active regions to a `NONE` sentinel.
- **Intent**: each slot first runs the **orphan self-free check** — read its home voxel byte; if it mismatches the cached material (an edit or reaction changed it), free the slot and stop. (This is the *entire* "mining an active drop" mechanism — no voxel→slot table.) Survivors pick **one** destination under the conservation rule: **a legal destination must have been Air at tick-start.**
- **Claim — plain write, no atomic (8.4, the fix for the watchdog/debuggability risk):** the source thread writes its own slot index into the destination's claim slot with an **ordinary store**, not `InterlockedMin`. If two sources target the same destination, both write the same 32-bit-aligned location — a race, but a *safe* one: a single aligned word store cannot tear on any real GPU, so the slot ends up holding exactly one of the candidate values, never a corrupted mix. Which one survives is arbitrary (whichever physically lands last) — and that's fine, because §7.8 already establishes correctness requires only conservation and steady-state behavior, never a specific deterministic winner. **This removes the contended-atomic hotspot entirely**: no `InterlockedMin` pileup at convergence points (the base of a waterfall, the exact case that used to risk a slow dispatch and a possible watchdog kill), and no failure mode where a debugging session has to distinguish "the GPU is slow because of contention" from "the GPU is stuck in a loop" — a torn/corrupted claim value is not a category of bug that can occur here.
- **Commit — destination-driven, single-writer by construction:** a second pass dispatches over Air destination cells (not sources). Each destination reads its own claim slot; if it holds a valid slot index, that destination is the *only* thread in the entire dispatch that will ever write to that cell or to the winning source's vacated home cell (because a given source can only ever be the recorded winner of the one destination slot it wrote to — a source writes its ID to exactly one place this tick). The destination writes its own new material, clears the source's home byte to Air, updates the slot's home coordinate, and **appends the resulting op to the write-op list** (§7.2). Cells whose claim slot is still `NONE` do nothing.

**Why this is safe, stated plainly:** every byte in the pipeline has exactly one writer once the claim race resolves — sources never write to a destination and destinations never write to a source's old home except through the single winning path. The only race in the whole design is the claim-slot word write, and word writes are safe from tearing by construction — this is a hardware guarantee, not a promise about scheduling. **Still verify on Metal specifically in Phase 5b** — a stated hardware guarantee is not the same as a measurement on this actual toolchain, and that verification is cheap (write a stress test that floods one destination cell with hundreds of contending sources, assert the surviving value is always one of the valid candidates, never garbage).

**CPU-reference-only note — brick-granularity decomposition (8.4, tightened from 8.2's voxel-granularity):** if the CPU reference (§7.2, Phase 5a) is parallelized with `IJobParallelFor` for speed (optional — its job is correctness, not throughput), partition at **brick granularity (B=8 voxels)**, not per-voxel, in a 2×2×2 red-black pattern of *bricks*. This aligns partition boundaries with the engine's existing brick structure (§2.3) rather than an arbitrary voxel lattice, which is both simpler to reason about and avoids subtle boundary-adjacency cases where a fine voxel-level partition could still let two phases touch the same neighborhood across a brick seam. Run `IJobParallelFor` one phase at a time (sync between phases) so non-adjacent brick-regions never race a claim cell. Not required if the CPU reference stays single-threaded, which is the simpler and recommended default.

**Mass conservation** (the primary Phase 5a unit-test invariant, checked against the CPU reference, then re-checked as a steady-state invariant on the GPU port in 5b): under Air-Only no drop claims an occupied cell; commits write disjoint won-Air cells; no overwrite path exists. A packed column shifts one cell per tick — at 60Hz this reads as pouring.

## 7.4 Fluid Behavior & v1 Scope

Intent hierarchy: (1) straight down; (2) four down-diagonals, order-randomized; (3) four horizontals, order-randomized (pooling). Viscosity via tick intervals: Water every tick, Lava every 6, Honey every 30.

**v1 activity scope** (matches the cut of Hydrology): fluid simulates only within an **active radius around the player.** Distant water is a settled terrain byte that looks like water but does not tick. On approach it wakes (slots allocate on GPU); on departure it sleeps back to static terrain. This keeps the write-op list small (§7.2) and is dramatically cheaper than worldwide simulation, while being *behaviorally identical* near the player. Full worldwide/high-count simulation is a v1.5+ concern.

## 7.5 Falling Solids

Same pipeline; Intent limited to down + down-diagonals — ~45° angle of repose.

## 7.6 Reactions & Sleep

Reactions during the Intent neighbor scan against Registry flag pairs (Water+Lava→Obsidian, fire via `IsFlammable`, Acid dissolves→Air) — these also emit write-ops. Sleep: a no-move counter frees the slot at threshold — nothing to write back, the byte already holds the material (via a prior applied op). Dormant = zero GPU slots, zero sim cost (how static generated water, §5.5, costs nothing until disturbed). Any adjacent edit re-promotes: the edit path scans the edit's neighborhood for fluid/falling materials and wakes GPU slots (§8.3) — this wake signal is a small CPU→GPU upload (§3.9), not a readback, so it is immediate.

## 7.7 Pool Pressure

If active slots approach the GPU pool cap, a forced-demotion pass frees the furthest-from-player slots (identical to natural sleep — distant fluid freezes mid-flow, no state lost). Underflow on promotion is a guarded no-op — retry next tick. No invalid write, ever. With the near-player scope (§7.4) this is a rare safety valve, not a routine path.

## 7.8 Determinism

The CPU reference *is* deterministic given a fixed tick order — this is exactly why it's the correctness oracle. The GPU port's scheduling is not order-stable (identical inputs give visually identical outcomes, not bit-identical frames) — tests on the GPU path assert steady-state invariants (final levels, conserved counts) against the CPU reference's output, never frame-exact positions. World gen (§5.3) remains the strongest guarantee (bit-exact); never conflate the three tiers of determinism (world gen > CPU fluid reference > GPU fluid port).

## 7.9 (Reserved)

Hydrology / river flow is cut from v1 (§1.3). Static dormant water only. This section is a placeholder so the v1.5 flow system slots in without renumbering.

---

# 8. Gameplay Interface

## 8.1 Physics: Rigidbody/Collider + Treadmill

PhysX `Rigidbody`/`BoxCollider` for player, drops, projectiles, NPCs. The world has no colliders; a **treadmill** of ~540 pre-allocated `BoxCollider`s (6×15×6) parented to the player has each collider's `enabled` toggled per frame from nearby voxels. PhysX never sees the island.

**Physics always reads CPU terrain via `GetVoxel`** — including for fluid, since applied fluid moves land in terrain bytes through `SetVoxel` (§7.2's command-list pattern). There is no separate fluid-mirror structure to read: fluid presence *is* terrain material, exactly like solid ground, just with bounded latency on how recently a given voxel's fluid state was applied (§7.2, §8.2).

*(The 540-collider treadmill is unproven; a character controller with ~6–12 DDA probes may suffice. Legitimate Phase 6 fork: build the simpler probe-based controller first; escalate to the full collider cage only if it can't do the job — arbitrary-geometry collision against dense voxel terrain. A Phase 6 decision point, §13, not pre-decided.)*

**Controls as data + hot reload (8.2 — the single highest-value tool for tuning game feel):** the player controller's feel parameters — acceleration, max speed, jump impulse, air control, friction, step height, coyote time — live as tunable numbers in `PlayerConfig` (JSON), **not** hardcoded in the controller logic. A `[RuntimeInitializeOnLoadMethod]` reloads `PlayerConfig` from disk at runtime, so you tweak jump height and acceleration **while playing, without recompiling.** An AI can generate the controller *mechanism* correctly and still produce a controller that feels terrible; feel is found by iterating numbers in real time, not by regenerating code. This is the difference between a controller that passes "climbs a 3-voxel step" and one that's actually fun to move in — and no automated test captures the latter, so the tuning loop must be instant.

**`PlayerFeedback` event bus (8.4 — the missing gameplay-response plumbing):** mining, building, taking damage, entering fluid, and destruction all need a *response* — sound, particle, screen shake, HUD update — that this document's engine chapters deliberately don't specify, since it's Game-layer content, not engine architecture. What's missing is the **hook**: a small, engine-side event bus (`PlayerFeedback.Emit(EventType, position, magnitude)`) that `EditService` (§8.3), `Buoyancy` (§8.6), and `DestructionReducer` (§8.5) call into on the relevant moments (a block breaks, a splash occurs, a hit lands), with the actual sound/particle/shake/HUD response implemented entirely in `Game/`, subscribing to the bus. This keeps the engine ignorant of *what* game-feel response happens (an engine invariant, §12's ownership rules) while guaranteeing the *hook* exists from the start, rather than being bolted on awkwardly once fifteen engine call-sites need it retrofitted.

## 8.2 Swept CCD + Depenetration — Against CPU Terrain (Exact for Solids, Bounded-Latency for Moving Fluid)

**Primary CCD (swept):** above ~18 m/s a swept pass runs before integration — a 3×3 DDA ray bundle traces `PositionPrev → PositionNext` against CPU terrain (`GetVoxel`); first solid hit clamps to the impact plane.

**Fluid pass-through accumulation (8.2 — fixes fluid tunneling):** the swept pass clamps only on *solid*, so at 60 m/s (1m/frame, 10 voxels) a player crossing a thin (e.g. 3-voxel) water or lava layer between frames would phase through with no splash, drag, or damage — the probes sample only the endpoints, both dry. **Fix:** the same DDA sweep accumulates the continuous distance traveled *inside* fluid voxels and the primary fluid material encountered, returned alongside the solid hit:
```
struct CCDResult { float solidHitDistance; float fluidTraversedDistance; byte primaryFluidMaterial; }
```
`fluidTraversedDistance > 0` triggers buoyancy/drag/damage and a splash even when no solid hit occurred and the endpoint is dry. *(Phase 6 test: fly through a 3-voxel water sheet at 60 m/s — splash fires, drag applies, no phase-through.)*

**Secondary (depenetration backstop):** each frame after PhysX integration, the treadmill's terrain scan doubles as an overlap check: any cage voxel in solid ⇒ binary-search back along the movement vector to the last free position, zero the offending velocity component. A silent permanent tunnel is impossible.

**Staleness at 60 m/s (8.3 — reintroduced, bounded, mitigated):** both CCD defenses read CPU terrain via `GetVoxel`, which is exact for solid terrain (edits apply same-frame, §8.3) but *bounded-stale for fluid* — a voxel's fluid material reflects the last **applied** op-list, roughly 1–3 frames behind the GPU's actual decision (§7.2). This reopens a version of the risk 8.1 removed. Two mitigations, both already load-bearing elsewhere in the spec, not new invention:
- **The depenetration backstop** (below) already catches "ended up somewhere wrong" regardless of *why* — a stale-fluid false negative (player thinks a cell is Air, it's actually about to be water) degrades to, at worst, "no splash this specific frame," never a physics violation, since fluid doesn't block movement the way solid terrain does.
- **A speed clamp** (to ~20 m/s, matching the original v7/pre-8.1-v8 pattern) engages if op-list readback stalls beyond ~2-3 frames (tracked via a timestamp on the last applied batch), same mechanism as the terrain-mirror clamp historically used for exactly this class of risk.
This is a bounded, mitigated trade for the CPU headroom §7.2 argues for — not a silent regression.

## 8.3 Mining & Editing — One CPU Path

All edits go through **`EditService.SetVoxel`** (and batch variants) on the CPU:

```
SetVoxel(worldCoord, material):
  resolve chunk (resident window, §3.3)
  if chunk uniform and material == chunk.material: return          # no-op
  if chunk uniform: populate (alloc chunk.bricks[4096], all uniform = old material)
  resolve brick handle
  if brick uniform and material == brick.material: return          # no-op
  if brick uniform: make dense (alloc 512B body, fill with old material)
  body[voxelIndex] = material
  mark chunk dirty (→ clipmap upload, §3.7)
  scan 26-neighborhood for fluid/falling materials → wake slots (§7.6)
  mark chunk delta-dirty (→ save, §4.2)
```

Straight-line, testable with `Assert.AreEqual` against a plain array. No atomics, no run-splitting, no orphan handling here (orphan handling is in the fluid slot, §7.3). Applies immediately to CPU terrain; the GPU sees it after the frame's upload (§3.9 order), closing the edit-vs-fluid race by ordering.

## 8.4 Projectiles

Swept segments traced against CPU terrain via `GetVoxel` in a Burst job. No GPU raycast service (v7 needed one only because terrain lived on the GPU) — deleted. A 2.5m/frame arrow can't tunnel a 0.1m wall; the DDA visits every cell.

## 8.5 Mass Destruction

A Burst job over the event's bounding region calls the batch `SetVoxel` path: read material (for a tally histogram), write Air, mark dirty. A 400K event splits across 2–3 frames if it exceeds a per-frame work budget. On completion, one **Proxy Drop** Rigidbody carries the aggregate tally — one PhysX box regardless of blast size. The CPU never loops per-voxel on the main thread (Burst-parallel, frame-split if large).

## 8.6 Buoyancy

Three probes (Feet, Center, Head) sampled from **CPU terrain bytes** via `GetVoxel` — the same bounded-latency fluid state described in §8.2 (typically 1–3 frames behind the GPU's decision, mitigated by the depenetration backstop + speed clamp). Probe IDs drive upward force + drag from the Registry (`F = (ρf − ρe)·g·Vsub`, C.6). A 60 m/s dive gets buoyancy within that same bounded window of entering water — not instant, but never a silent lakebed slam, since the speed clamp engages before staleness could exceed the mitigated bound. The sim never knows the player exists (one-way sampling).

---

# 9. Fault Tolerance

**The kept contract:** under any input or failure, **the process never crashes and the save never corrupts.** v8.1 cuts the graceful-degradation *ladder* from v1 but keeps every *safety* guarantee, plus the shadow fallback rungs. A frame-time floor under abuse is not a v1 contract; memory-safety and save-integrity are.

| Failure | Defense | Where |
|---|---|---|
| Crash/power-loss mid-save | CRC32 + atomic rename; per-chunk blast radius | §4.2 |
| Corrupt delta on load | discard, regenerate pristine baseline | §4.2 |
| Brick/handle pool exhaustion (incl. adversarial checkerboard) | hard cap + LRU eviction of coldest chunk; edit always succeeds | §3.6 |
| Fluid pool pressure | forced distance-demotion + guarded no-op on underflow | §7.7 |
| Edit destroys an active drop | orphan self-free rule | §7.3 |
| Edit vs. fluid same-frame race | edits apply+upload same frame; fluid ops apply next frame via the same `SetVoxel` path (pass order) | §3.9, §8.3 |
| Live fluid invisible to renderer/shadows | authoritative-byte rule (once an op is applied) | §3.10, §6.3, §6.5 |
| GPU clipmap drifts from CPU truth | debug-only byte-compare validator | §3.7, §10.4 |
| GPU writing terrain directly (v7's actual contradiction) | never happens — GPU only emits a write-op list; CPU is the sole applier via `SetVoxel` | §3.9, §7.2 |
| Fluid op-list readback lag at 60 m/s (reintroduced in 8.3, bounded) | depenetration backstop (any-cause overlap correction) + speed clamp on stale op-list delivery | §7.2, §8.2 |
| CCD ray-bundle geometric miss | post-move overlap + depenetration backstop | §8.2 |
| Shadow rays too costly at golden hour | measured gate + fallback rungs (half-res distant / hard-only / off) | §6.5 |
| Shadow acne / Peter-Panning | fused exact shadow ray — no resolution mismatch exists | §6.5 |
| Chunk-table races | single-writer StreamManager, no locks | §3.2, §4.4 |
| Mid-save eviction race | Saving-state eviction lock | §4.4 |

**Boot-time capability gate:** verify `supportsComputeShaders`, `maxGraphicsBufferSize` sufficient, and run the Phase-1 buffer read-path benchmark result. Any capability failure → clear diagnostic and clean exit before any pool allocates.

---

# 10. Tooling & Validation

## 10.1 Editor Auto-Cleaner
`[InitializeOnLoad]` on exiting play mode deletes `*.delta` before pools init. Toggle off for persistence testing (Phase 4).

## 10.2 Native Profiling (Xcode) — The Measurement Rule
All performance tests run on **standalone Apple-Silicon builds** (`Create Xcode Project`, IL2CPP, Apple Silicon only — never Universal). GPU Frame Capture (Metal), per-dispatch timing via `CommandBuffer.BeginSample`, buffer hex inspector vs Appendix A. **Check the "Performance State" field on every capture** — if not "Full," the M1 Air is throttling and the number is a *lower bound*, not the truth. Editor numbers are never trusted for any ms figure that matters.

## 10.3 Debug Render Passes & HUD
1. Step-count heatmap (blue ≤10 / green ≤30 / red ≥100) — red sky = clipmap/traversal bug.
2. Boundary wireframes (brick / chunk).
3. Uniform/dense overlay — the sticky-note economy made visible; the fastest way to spot a chunk gone needlessly dense.
4. Clipmap validator overlay — flashes on any CPU/GPU mismatch (§10.4).
5. Fluid wake/sleep thermal (CPU pool).
6. Sun-visibility view + shadow-ray step-count heat.
7. HUD: pool occupancies (brick data, handle, fluid), streaming backlog, per-pass ms (GPU) + CPU-lane ms, sun angle/time, memory high-water.

## 10.4 Automated Validation Suite (debug builds)
- **Clipmap equality** — periodic region readback of GPU Brick Data vs CPU pool (the §3.7 validator). The single most important architecture-specific check in the suite.
- Total pool allocation constant at steady state (growth = leak).
- Coalescing correctness (a brick reported uniform genuinely has 512 equal bytes).
- Fluid byte-conservation (constant absent reactions/edits).
- Determinism — `GenerateChunk` byte-identical across calls/orders (pure-function test).
- Delta round-trip — save → evict → reload → byte-compare equality.
- No `Resident→Unloaded` during `Saving`.

**Per-subsystem diagnostic dumps (8.2 — for fast failure triage, human or AI):** every subsystem maintains a small ring buffer of its last N operations, dumped to a file when any validation assertion fails. Concretely:
- **Raymarcher:** if a single ray's step count exceeds a threshold (e.g. >1000), log its origin, direction, and last ~10 clipmap indices traversed. If the clipmap validator fires, log the brick coordinate, the CPU byte, and the GPU byte.
- **Streaming:** if memory grows >2% over 100 frames, log the last ~10 eviction decisions and the current free-list counts.
- **Fluid:** if conservation drifts, log the tick, the delta count, and the region.
This converts a vague failure ("physics feels wrong," "memory creeping") into the exact state that caused it — the single highest-leverage debugging aid when code is generated fast (by you or an AI) and bugs arrive fast.

---

# 11. Performance Budget

## 11.1 CPU (per frame)
Fluid op-list apply (via `SetVoxel`) ≤0.5ms (8.3 — the GPU decided, CPU just writes; a Phase 5b measurement, not yet proven); terrain upload ≤1.0ms; treadmill/CCD/depenetration/buoyancy ≤1.5ms; generation/decode async off main thread. **The CPU lane is deliberately lightened in 8.3** to preserve headroom for orchestration during chaotic scenes (§2.2) — this is the point of moving fluid compute to the GPU.

## 11.2 GPU (per frame)
Raymarch incl. fused shadow rays ≤9.0ms (Phase 7 gate + fallback rungs); fluid CA (Intent/Claim/Commit) ≤3.5ms (8.3 — back on GPU; claims resolve via a safe plain-write race, 8.4, no atomics; emits a bounded op-list rather than writing terrain); light drain ≤0.5ms; steady total ≤13.0ms. **The true combined gate (raymarch + shadow + fluid, together) can only be measured in Phase 7** — the first phase where all three exist simultaneously (audit correction: an earlier draft mistakenly asked Phase 5b to measure this before shadows existed). Phase 5b measures fluid alongside the primary raymarch only; Phase 7 measures the full three-way combination as its own explicit gate.

## 11.3 Memory — Re-Derived, Sized Low-First

Decimal MB. **Derived** or **[Phase N gate]**. No line copied from v7. On the shared 8GB machine, the brick pool is sized **aggressively low first** and raised only if measurement allows (adopted feedback — CPU and GPU fight for the same RAM).

| Buffer | Formula / Source | MB |
|---|---|---|
| Brick Data Pool (CPU) | cap × 512B; **cap now 500K, re-derived vs measured peak 336,731 (2026-08-30)** | 244 |
| GPU Brick Data (clipmap dense mirror) | same cap × 512B | 244 |
| Terrain Clipmap (flat window grid) | mirrorChunks(32×4×32) × 4096 bricks × 4B, CPU mirror + GPU buffer | 64 + 64 |
| Inlined brick handles (CPU) | resident populated chunks × 4096 × 4B; 729 resident at load radius 13 × 16KB | ~12 |
| Fluid Slot Pool (GPU, §3.5) | near-player target × slot size (far below v7's 25M worldwide pool) | [Phase 5b] |
| Fluid claim cells + write-op list (GPU) | claim cells for active regions + `MAX_FLUID_OPLIST_BYTES_PER_FRAME` (§0.2) | [Phase 5b] |
| Light source buffer | small (sparse point lights) | ~1 |
| Material Registry + misc | 256×32B + slack | ~1 |
| LOD Cascade Pools (3 tiers, not 5) | tier1 cap 160K × 512B + tier2 cap 48K × 512B, each CPU + GPU; caps re-derived 2026-08-30 vs measured peaks 112,566 / 27,067 | 202 |
| Unity runtime, Metal driver, framebuffers, managed heap | **[Phase 1 baseline — measure fresh]** | [Phase 1] |
| **Total** | must land ≤ **3,000** | ≤3,000 |

**Phase 4 measurement (2026-08-30), filling the rows above.** Every figure is
from `BrickDataPool.PeakUsed` and `vmmap` on a release standalone build, not
derivation: tier-0 pool peak 336,731 of a 750K cap (44.9%) → cap re-derived to
500K, keeping the §3.6 valve (0.85 × cap = 425K) 26.2% clear of the peak;
cascade tier-1 peak 112,566 → cap 160K; tier-2 peak 27,067 → cap 48K. Cascade
pools have no valve (`Alloc` throws), so they carry MORE relative headroom than
tier 0, not less. Declared total across all rows above is ~837 MB; **measured
physical footprint is 1.6 GB** including Unity/Metal/framebuffers, against the
≤3,000 MB ceiling. The gap between 837 MB declared and 1.6 GB measured is
runtime and driver overhead, and it is why `ps rss` is not a sufficient
instrument here — before the 2026-08-30 cascade buffer fix it under-reported a
6 GB GPU buffer leak entirely (see PHASE_4_COMPLETION.md §4).

**Starting the brick pool at 750K (not v8's 1.5M) halves the two largest lines (~768MB → ~384MB, saving ~768MB)** — the highest-leverage budget lever, adopted directly from review. If Phase 6's checkerboard test shows 750K evicts too aggressively under *normal* building, raise it then, with data. The 3-tier LOD (vs 5) also removes two cascade pools' worth of memory, downsampling, and validation.

## 11.4 SSD
Worst-case fully-edited chunk delta ≈ 4,096 × 512B ≈ 2MB; realistic KB-scale. `world.meta` KB-scale. Realistic worlds tens of MB.

## 11.5 The v1 Worst Case (Scoped)
A 400K detonation near static water that wakes into flow, during a 60 m/s traversal, at golden hour, with a dense checkerboard base nearby. **Hard guarantees: no out-of-bounds write, no lost edit, no save corruption, memory never exceeds the cap.** Frame-time may breach ≤3 frames; safety never breaches. Phase 8's scripted scene.

---

# 12. Module Organization

```
Assets/
├── CoreEngine/                      # engine-only; no upward refs
│   ├── Coord/          CoordMath.cs, CoordMath.hlsl
│   ├── Memory/         ChunkStore.cs (window + GetVoxel/SetVoxel + IWorldQuery/IEditService),
│   │                   BrickDataPool.cs, ChunkHandleAllocator.cs (optional pooled backing), StructHeaders.cs/.hlsl, Coalescer.cs
│   ├── Mirror/         TerrainClipmap.cs, ClipmapValidator.cs
│   ├── Streaming/      StreamManager.cs, ChunkLifecycle.cs, DeltaCodec.cs, CoalesceScheduler.cs
│   ├── WorldGen/       WorldMeta.cs, AnchorPlanner.cs, GenerateChunk.cs, FeatureCarve.cs
│   ├── Rendering/      RaymarchFeature.cs, Raymarch.compute (+ RaymarchReference.cs),
│   │                   LodDownsample.compute, LightSourceBuffer.cs, SunCycle.cs
│   ├── Simulation/     FluidReferenceCPU.cs (Phase 5a correctness oracle, single-threaded),
│   │                   FluidSlotPool.cs + FluidCA.compute (Phase 5b, shipped GPU path, plain-write claims — no atomics, §7.3),
│   │                   FluidOpListReadback.cs (async readback + apply via SetVoxel), DemotionPass.cs
│   ├── Gameplay/       PlayerController.cs (probe-first) / PhysicsTreadmill.cs (escalation),
│   │                   SweptCCD.cs, EditService.cs, ProjectileTrace.cs,
│   │                   DestructionReducer.cs, Buoyancy.cs
│   └── Diagnostics/    DebugPasses, HUD, ValidationSuite
├── ContentModules/                  # data only (code-defined in v1)
│   ├── Config/         EngineConfig.cs
│   └── Content.cs      Materials / Biomes / Features tables (§5.5)
└── Game/                            # player-facing; consumes CoreEngine public API only
```

**Ownership (asmdef-enforced):** `CoreEngine` has no upward references; `Game` sees only public interfaces (`IWorldQuery`, `IEditService`). Every `.compute` `#include`s `CoordMath.hlsl` + `StructHeaders.hlsl`.

**The freeze-the-API commitment (8.1 wording):** when a phase passes, its **public interface** freezes — `IWorldQuery.GetVoxel`, `IEditService.SetVoxel`, `GenerateChunk`, the clipmap upload contract, the fluid sampler. Later phases consume these and never restructure them. **Bug fixes and additive hooks to a passed system's internals are normal and expected** — the rule forbids *redesign*, not *editing*. If a later phase seems to require redesigning a passed system, the boundary was drawn wrong; that's a signal to reconsider the boundary, not a license to thrash.

---

# 13. Implementation Guide — Phases 0–8

This chapter is the executable core of the document. Each phase gives: **the one new thing**, **prerequisites**, **files to create (in order)**, **the exact build steps**, **the scene**, **the acceptance test with concrete assertions**, **failure signatures** (symptom → likely cause), **the "if stuck 3 days" fallback**, and **solo-dev notes**. Rough calendar sizing is honest, not pressure.

**The governing discipline, restated:** one new untrusted thing per phase (Rule 1); prove on CPU before GPU (Rule 2); freeze *public APIs* on pass, not implementations (the 8.1 correction). Within every phase the order is always: **data structures → the code that writes them → the code that reads them → the debug view → the acceptance test.** Build the debug view before chasing the test.

**Tracer bullets (8.2, properties defined 8.4 — the first step of every GPU/parallel phase):** before building a full subsystem, build a minimal end-to-end path through it and verify that, then expand. **Five properties of a good tracer**, checked against each one below:
1. **Minimal** — small enough to hold in your head entirely (a rough guide: under ~100 lines).
2. **End-to-end** — touches every layer the full feature will touch (if the real feature crosses CPU→GPU, so does the tracer; a tracer that skips a layer doesn't prove that layer works).
3. **Independently assertable** — has its own pass/fail, not "eyeball it and see."
4. **Permanent** — becomes a regression test kept forever, not deleted once the full feature exists.
5. **Built first** — exists before the full implementation, not extracted from it afterward (a tracer carved out of working code proves nothing new; one built first is what catches the bug).
- **Phase 2 tracer:** `TracerRaycast` — cast exactly one ray from a hardcoded origin/direction through the clipmap, return the hit voxel color. ~50 lines. Prove it before full-screen dispatch.
- **Phase 5 tracer:** `TracerFluidDrop` — one slot, move it exactly one tick down, assert the terrain byte moved. Prove it before the full pool/CA.
Build the tracer, verify it, keep it as a regression test, then expand to the full implementation.

**Integration milestones (8.2 — because systems ship, not components):** AI-generated (and hand-written) code tends to make each *component* pass its own test while the *interactions* break. Alongside the phase gates, these cross-system milestones must each be demonstrated — they are the real "is this a game" checks:
- **M-A:** mine a single block (Phase 6).
- **M-B:** mine a whole mountain apart (Phase 6).
- **M-C:** mine a cave network (Phase 6).
- **M-D:** mine *while streaming* — dig at the edge of the loaded window as it pages (Phase 6 + 4).
- **M-E:** mine *while fluids update* — breach a water body and keep digging (Phase 6 + 5).
- **M-F:** mine *while lighting updates* — dig toward a torch / open a cave to sunlight (Phase 7).
- **M-G:** mine *while saving* — edit a chunk as it evicts and saves, reload, verify (Phase 6 + 4).
Each milestone is a scripted scene that exercises two or more subsystems at once. Passing all phase tests but failing M-D/E/G is the classic "components work, system doesn't" failure — these milestones catch it.

**The universal "if stuck 3 days" protocol** (applies to every phase; individual phases add specifics; the AI-specific form is in §0.3 — regenerate from spec+tests after ~3 failed fix attempts rather than accepting a 4th clever patch):
1. Reduce to the smallest standalone reproduction — a scene with one object, not the full project.
2. If it reproduces in isolation → the bug is in the algorithm; re-read the relevant section and check against the CPU reference (where one exists).
3. If it does *not* reproduce in isolation → the bug is in the wiring/integration; rebuild the wiring, don't rewrite the algorithm.
4. If you've lost more than a week: move to the next phase and come back. Sometimes you need the rest of the system built to understand what the bug is supposed to look like. Leave a `// KNOWN BUG:` comment and a failing test so you don't forget.

---

**A cross-phase clarification, added on audit (closes an ambiguity, not a bug per se):** Phases 2 through 5 all reference "flying," "touring," or "digging" during their acceptance tests, before the real player controller or `EditService` mining tool exist (both are Phase 6). **These earlier phases use a bare debug fly-camera and direct `SetVoxel`/`GenerateChunk` calls from test code** — never the Phase 6 player or mining tool. If an acceptance test in Phases 2–5 seems to need the "real" player or tools, that's a misreading: build a minimal debug harness instead, exactly as `Phase1Validator` (Phase 1) already does. This is stated once, here, rather than re-qualified in every phase.

---

## Phase 0 — Coordinate Math + Config *(pure C#, no engine, ~1–2 days)*

**The one new thing:** bitwise power-of-two coordinate conversion, plus the config assets everything reads.

**Prerequisites:** Unity project created — URP template, IL2CPP scripting backend, **Apple Silicon only** (never Universal — Rosetta invalidates every timing you'll ever take). Burst, Collections, Mathematics packages installed. Folder tree + asmdefs (Ch.12) created **while empty** (ten minutes now vs. multi-day retrofit later). Git initialized with Unity's `.gitignore` from commit one.

**Files to create, in order:**
1. `CoreEngine/Coord/CoordMath.cs` — the C.1 chain: `WorldToVoxel`, `VoxelToBrick`, `VoxelToChunk`, `LocalVoxelIndex`, `LocalBrickIndex`, all as `static` `[BurstCompile]`-compatible methods using only `>>`, `&`, and one `math.floor` for the float→int step.
2. `CoreEngine/Coord/CoordMath.hlsl` — byte-identical logic (same shifts/masks), for later `#include` in every `.compute`.
3. `ContentModules/Config/EngineConfig.cs` — a plain static class (not ScriptableObject in v1) holding the sizes everything references: `BRICK_EDGE=8`, `CHUNK_EDGE_BRICKS=16`, `WINDOW_CHUNKS_XZ`, `WINDOW_CHUNKS_Y`, `BRICK_POOL_CAP`. Values are placeholders now, tuned in Phases 4/6, but **every system reads them from here** so tuning never means editing multiple files.
4. `CoreEngine.Tests/CoordMathTests.cs` — the edit-mode test assembly.

**Build steps:**
- Implement `CoordMath` with the exact C.1 formulas.
- Write tests covering: origin, positive coords, and **negative coords explicitly** — assert a voxel at world −0.05m and +0.05m resolve to adjacent voxels with correctly-signed brick/chunk coords, and that no mirroring/offset appears in any negative quadrant. Assert `voxelIndex` and `brickIndex` round-trip (index → local coord → index).

**Scene:** none. Pure test assembly.

**Acceptance test (concrete assertions):**
- `WorldToVoxel(new float3(-0.05f,0,0))` == `int3(-1,0,0)` (not `0` — the classic truncation bug).
- `VoxelToChunk(int3(-1,-1,-1))` == `int3(-1,-1,-1)` (arithmetic shift, not truncating divide).
- All index round-trips exact for a sampling of locals across the full 0..511 / 0..4095 range.
- **All tests green.**

**Failure signatures:**
- A negative-coordinate test off by one → you used `/` or `%` somewhere instead of `>>`/`&`, or forgot the `math.floor` on the float step. Grep the file for `/` and `%`.
- HLSL and C# disagree later → the two files drifted; they must be logically identical. This is why they're written together, now.

**If stuck 3 days:** this phase is small enough that "stuck" means a sign-convention misunderstanding — write out the two's-complement bit pattern of `-1 >> 3` by hand and confirm it's `-1`, not `0`.

**Solo-dev notes:** no GPU, no rendering, nothing to look at — deliberate. This is the one piece of math everything depends on; prove it, freeze its public API, and never think about coordinate signs again.

---

## Phase 0.5 — All Interfaces & Scaffolds *(8.4, ~2-3 days)*

**The one new thing:** nothing behavioral — this phase generates the **skeleton** every later phase fills in, so the whole project has a compile-time dependency graph from day one instead of interfaces being invented ad hoc as each phase needs them.

**Prerequisites:** Phase 0 green.

**Build steps:**
- Generate every public interface named anywhere in this document: `IWorldQuery` (`GetVoxel`), `IEditService` (`SetVoxel` + batch variants), `IFluidSampler`, `IChunkProvider`. Empty method bodies (`throw new NotImplementedException()`), full signatures matching Appendix A's struct shapes.
- Generate empty stub classes/MonoBehaviours for every file named in Ch.12's module tree that a later phase will fill in — `ChunkStore`, `TerrainClipmap`, `StreamManager`, `GenerateChunk`, `RaymarchFeature`, `FluidReferenceCPU`, `FluidCA.compute` (an empty kernel), `PlayerController`, `EditService`, etc.
- Compile. The project should build successfully with **zero real logic**, referencing only interfaces and empty stubs.

**Why this earns its own phase rather than being folded into Phase 1:** generating structs/interfaces is fast and mechanical (for you or an AI) — doing it once, up front, means every later phase's code is written *against a contract the compiler already checks*, rather than each phase inventing its own shape for "the thing Phase N+1 will need." Interface mismatches surface immediately as compile errors, not as Phase 4 integration-hell surprises.

**Scene:** none. This phase has no runtime behavior to demonstrate.

**Acceptance test:** the empty project compiles clean, referencing every frozen interface named in this document, with zero implementation.

**Failure signatures:** a later phase needs a method that doesn't exist on a frozen interface → the interface was incomplete; add the method (additive, not a redesign) and note it, rather than reaching around the interface into a concrete class.

**If stuck:** this phase should not produce a "stuck" state — it is pure scaffolding with no logic to debug. If it feels hard, the document is being asked to specify something it hasn't actually decided yet; resolve that decision first.

**Solo-dev notes:** this is the cheapest phase in the whole project and the one most worth not skipping if you're leaning on AI assistance — every interface written here is one an AI can be told to implement *against*, rather than improvise around.

---

## Phase 1 — The Memory Model *(still pure C#, ~1 week)*

**The one new thing:** the two-tier uniform/dense chunk/brick structure and `GetVoxel`/`SetVoxel`. Still no rendering, still no GPU decisions — proven by unit tests, not eyes.

**Prerequisites:** Phase 0 and Phase 0.5 green (the interfaces this phase implements were generated in 0.5).

**Files to create, in order:**
1. `CoreEngine/Memory/StructHeaders.cs` + `StructHeaders.hlsl` — A.2/A.3/A.4/A.7 struct layouts, `[StructLayout(LayoutKind.Sequential)]`, mirrored in HLSL. Single source of truth.
2. `CoreEngine/Memory/BrickDataPool.cs` — flat `NativeArray<byte>` of `BRICK_POOL_CAP × 512`, free-list stack, `Alloc()`/`Free()`.
3. `CoreEngine/Memory/ChunkHandleAllocator.cs` — hands out `BrickHandle[4096]` arrays to populating chunks (a pooled backing allocator to avoid GC churn; the chunk *owns* the array via the frozen API, this just backs it).
4. `CoreEngine/Memory/Coalescer.cs` — the pure `TryCoalesce(chunk)` check (§4.5); the background *scheduler* that calls it periodically arrives in Phase 4.
5. `CoreEngine/Memory/ChunkStore.cs` — the resident-window ring array of `Chunk`, plus the two hot methods `GetVoxel(int3)` and `SetVoxel(int3, byte)`, **implementing the `IWorldQuery`/`IEditService` interfaces generated in Phase 0.5** (this phase fills the stubs; it does not re-declare them).
6. `CoreEngine.Tests/MemoryModelTests.cs`.
7. `Game/Phase1Validator.cs` — a MonoBehaviour that runs the buffer round-trip test (step below) on-screen.

**Build steps:**
- Implement `GetVoxel` as the three-step lookup (C.1): chunk → (uniform? return) → brick handle → (uniform? return) → dense body byte.
- Implement `SetVoxel` per §8.3's pseudocode, including the uniform→populated and uniform→dense expansions and the no-op fast paths.
- Implement `Coalescer.TryCoalesce(chunk)` (the §4.5 check) here as a pure method, tested now even though the background job that calls it comes in Phase 4.
- **In parallel (independent sub-task):** a throwaway buffer test — create a `GraphicsBuffer`, write a known pattern via **both** `LockBufferForWrite` and `SetData`, dispatch a trivial compute shader that reads it heavily (e.g. sums it N times), and **time both read paths in a standalone build**. Record which is faster to *read* on the M1. This decides §3.7's per-buffer upload mechanism for the terrain clipmap. **If `LockBufferForWrite` reads slowly (host-local) and `SetData` reads fast (device-local), the clipmap uses `SetData`.** If `LockBufferForWrite` is not zero-copy *at all* on M1, note it — the native-plugin fallback question is deferred but recorded.
- **Measure fresh base overhead:** empty-project standalone build resident memory. This becomes §11.3's overhead line. Do NOT reuse v7's 743MB.

**Scene:** minimal — `Phase1Validator` running the buffer test and printing PASS/FAIL + the read-timing comparison.

**Acceptance test (concrete assertions):**
- Write a known pattern via `SetVoxel` across a test region spanning chunk and brick boundaries (incl. negative coords); read back via `GetVoxel`; assert byte-equality against a plain reference `byte[,,]`.
- Assert a fresh chunk is uniform; assert first edit makes it populated; assert first edit in a brick makes that brick dense; assert `Coalescer` returns a refilled brick/chunk to uniform.
- Assert pool free-lists return to their starting free count after alloc→free cycles (no leak).
- Buffer read-timing recorded; base overhead recorded.

**Failure signatures:**
- Round-trip mismatch at a boundary → `GetVoxel`/`SetVoxel` local-index math disagrees with `CoordMath`; they must both call `CoordMath`, never recompute.
- Pool free count drifts → a `Free()` path is missed (usually the uniform→dense expansion allocating without a matching free on coalesce).
- Struct hex garbage later → a `bool` (marshals unpredictably) or `half` (Metal widens) crept into a GPU struct; use only the A-appendix integer/float fields.

**If stuck 3 days:** the memory model is fully CPU — every bug here is reproducible in a unit test. If a test fails, shrink the region to 2×2×2 voxels and step through `SetVoxel` in the debugger. There is no excuse to guess in this phase.

**Solo-dev notes:** the entire terrain model is provable with `Assert.AreEqual` here — no shader, no hex inspector, no visual guessing. This phase is what makes the *rest* of the project debuggable; over-invest in the tests. Every terrain bug caught here is one you never chase through a compute shader.

---

## Phase 2 — First Light: A Rendered Generated Island *(GPU appears, ~2–3 weeks)*

**The one new thing:** the flat GPU clipmap + the DDA raymarcher, drawing a *generated* island. This phase merges v8's old Phase 2 (raymarcher) and Phase 3b (view generation) on purpose — **the reviewer's best morale point**: you should see the thing you're building in week ~4, not week ~7. The data structure (Phase 1) and — for the simplest generator — a trivial heightfield are the only trusted inputs; the new thing is clipmap + traversal.

**Prerequisites:** Phase 1 green.

**Files to create, in order:**
1. `CoreEngine/Mirror/TerrainClipmap.cs` — the flat `GraphicsBuffer<uint>` window grid + `GraphicsBuffer` GPU brick data; `MarkDirty(chunk)`; `UploadDirty()` using the Phase-1-chosen mechanism (`SetData` or `LockBufferForWrite`).
2. `CoreEngine/Mirror/ClipmapValidator.cs` — **built the same day as the uploader** — debug-only region readback + byte-compare vs CPU Brick Data Pool, `Assert`/log on mismatch (§10.4).
3. `CoreEngine/WorldGen/GenerateChunk.cs` — for this phase, the *simplest* version: a 2D heightfield (one noise call per XZ column), filling uniform Air above / uniform Stone below / dense only the surface-straddling bricks. No features, no biomes yet — those are Phase 3.
4. `CoreEngine/Rendering/Raymarch.compute` — the DDA: macro-step over the clipmap (uniform skip / dense enter), micro-step the 8³ dense brick, flat-shaded cubes, normals from step mask, VAO. `#include CoordMath.hlsl` + `StructHeaders.hlsl`.
5. `CoreEngine/Rendering/RaymarchFeature.cs` — the `ScriptableRendererFeature` dispatching one thread/pixel and blitting the result.
6. `CoreEngine/Diagnostics/DebugPasses.cs` — heatmap (step count), boundary wireframes, uniform/dense overlay.
7. `Game/Phase2Scene` wiring.

**Build steps (critical ordering):**
- **Write the DDA in C# first** (`CoreEngine/Rendering/RaymarchReference.cs`), as a plain function that casts a ray at the Phase-1 structure and returns the hit voxel. Unit-test it: known ray → known hit voxel. *Only then* port it line-by-line to HLSL. Debugging traversal math in a shader without a CPU reference is the single biggest time sink in the whole project — this is non-negotiable.
- Build clipmap upload + validator; confirm the validator is green on a hand-authored chunk before generation is wired.
- Wire the simple `GenerateChunk` → `ChunkStore` → `UploadDirty`. Now the island renders.
- LOD: **v1 uses 3 tiers, not 5** (adopted feedback — 0.1m / 0.4m / 1.6m; §6.4 table trimmed). Build the downsampler for those three; measure the cascade pool ceiling (§11.3).

**Scene:** `Phase2_Island` — a fly camera over a generated heightfield island.

**Acceptance test (concrete assertions):**
- The island renders as hard 0.1m cubes; normals correct from every angle including from inside a dug-out pocket (use `SetVoxel` to carve one, confirm interior faces shade correctly).
- Step-count heatmap: sky deep blue (few steps), no red/stalled rays.
- A hand-authored dense brick renders pixel-identically to a uniform brick of the same material at comparable heatmap cost (the clipmap's uniform-skip working).
- **ClipmapValidator green the entire session.**
- **Xcode capture, Performance State checked:** raymarch ≤8.0ms at 1080p, camera 1m from a fully-detailed wall (the +1.0ms shadow allowance is Phase 7's). If Performance State ≠ Full, note the number is a lower bound.
- Hex-inspect a clipmap region: matches `CoordMath` addressing and A.8 layout.

**Failure signatures:**
- Red sky in the heatmap → macro-step not advancing by the uniform brick's extent (leap logic wrong).
- Terrain mirrored/offset in negative quadrants → a second coordinate implementation crept into the shader; it must `#include CoordMath.hlsl`, not recompute.
- Clipmap validator fires → dirty-mark missed, struct layout mismatch, or dispatch group-count off-by-one (`ceil(threads/groupSize)`).
- Dense brick renders differently from uniform twin → micro-step index or the clipmap dense-entry decode is wrong; compare against `RaymarchReference`.

**If stuck 3 days:** you have a CPU DDA reference and a clipmap validator — between them, any render bug is either "traversal disagrees with reference" (algorithm) or "GPU data ≠ CPU data" (validator fires, wiring). Determine which in minutes, then follow the universal protocol.

**Solo-dev notes:** the DDA is the hardest code in the project and the CPU-reference-first rule is what makes it tractable. The hand-authored chunk and the reference DDA are permanent regression fixtures — never delete them. When this phase passes you are *looking at your game*; that payoff is why Phase 2 was merged forward.

---

## Phase 3 — Full Generation *(CPU-proven before drawn, ~1 week)*

**The one new thing:** the full `GenerateChunk` — features, biomes, static water — as a pure CPU function, proven by unit test (3a) before its output is viewed through the trusted Phase-2 renderer (3b). This split is the direct fix for the v7 Phase 2 catastrophe.

**Prerequisites:** Phase 2 green.

**Files to create, in order:**
1. `CoreEngine/WorldGen/WorldMeta.cs` — `world.meta` writer/reader (D.2), CRC + atomic rename.
2. `CoreEngine/WorldGen/AnchorPlanner.cs` — Poisson feature anchors + Voronoi biome seeds, persisted to `world.meta`.
3. `ContentModules/Content.cs` — the code-defined `Materials`, `Biomes`, `Features` tables (§5.5).
4. `CoreEngine/WorldGen/GenerateChunk.cs` — extend the Phase-2 version to §5.3 steps 1–4: heightfield+biome per column, uniform fill, dense surface/feature bricks, biome strata materials, static water below sea level.
5. `CoreEngine/WorldGen/FeatureCarve.cs` — per-anchor carve kernels (a mountain, a crater, a cave) as plain functions.
6. `CoreEngine.Tests/GenerationTests.cs`.

**Build steps:**
- **3a (no rendering):** implement generation; unit-test it before wiring to the renderer.
- **3b:** the only new wiring is feeding the richer output through Phase 2's `ChunkStore`/uploader. Renderer trusted, generation trusted → any visual wrongness is the wiring or the mirror, both already instrumented.

**Scene:** `Phase3_Island` — seed 42, free-fly.

**Acceptance test (concrete assertions):**
- **3a:** determinism — `GenerateChunk(seed42, coord)` byte-identical across repeated calls and across opposite visit orders (assert on a hash of the output). Uniform/dense distribution sane — assert the dense-brick fraction of a typical chunk is small (surface skin only), not the whole chunk. Biome strata correct per column. A feature anchor produces dense bricks only where it intersects.
- **3b:** island tour — coastline, distinct biomes with correct strata, features reachable, caves with clearance, static water pooled below sea level; ClipmapValidator green; the uniform/dense overlay shows mostly-flat terrain with dense confined to surfaces.

**Failure signatures:**
- Determinism test fails → a generation step reads state outside its inputs (impurity). Binary-search: disable steps 4→1 until the hash stabilizes; the last-disabled step is the culprit. **You find this in a debugger, not by staring at a mountain** — the whole point of v8.
- 3b looks wrong but 3a passes → wiring or mirror; ClipmapValidator isolates which.
- Seams between chunks → a step reads a neighbor chunk's data; generation must be per-chunk pure.

**If stuck 3 days:** 3a failures are pure-function failures — fully reproducible in a test, binary-searchable by step. 3b failures with 3a green are two-suspect wiring bugs. Neither should consume three days if you follow the binary-search.

**Solo-dev notes:** this is the phase v7 got wrong and v8 is built to get right. Generation is ordinary C# now. Build the biome/feature debug tint before chasing the tour test — generation bugs are invisible in code and obvious from the air.

---

## Phase 4 — Streaming & Persistence *(~1–2 weeks)*

**The one new thing:** chunks entering/leaving the resident window, and delta save/load.

**Prerequisites:** Phase 3 green.

**Files to create, in order:**
1. `CoreEngine/Streaming/StreamManager.cs` — single-writer, `NativeQueue` plumbing, the §4.4 lifetime state machine with the Saving-eviction lock.
2. `CoreEngine/Streaming/ChunkLifecycle.cs` — the state transitions.
3. `CoreEngine/Streaming/DeltaCodec.cs` — D.1 encode/decode, CRC, atomic `.tmp`→rename, uniform + dense brick records.
4. `CoreEngine/Streaming/CoalesceScheduler.cs` — the background job that periodically calls Phase-1's `Memory/Coalescer.TryCoalesce` on resident chunks (the check itself already exists and is tested; this phase only adds the scheduling).
5. Window sizing in `EngineConfig` — **measure and record** `WINDOW_CHUNKS_XZ`/`_Y` and the resulting handle/clipmap memory here (§11.3). Because these are config values read everywhere (Phase 0), tuning them is a number change, not a code change.
6. LRU eviction (distance + the §3.6 pool-pressure valve) in `StreamManager`.
7. Auto-cleaner toggle (§10.1).

**Scene:** `Phase4_Stream` — island, fly capped at 60 m/s, HUD showing pool occupancies + upload ms + memory.

**Acceptance test (concrete assertions):**
- Edge-to-edge at 60 m/s repeatedly: no pop-in inside 128m, upload ≤1.0ms steady, **memory flat over 10 minutes** (any creep is a leak).
- Dig a tunnel + build a small dense structure, fly 500m away and back: intact; the refilled part of the tunnel coalesced back to uniform (check the uniform/dense overlay).
- **Force-quit mid-save during rapid editing; relaunch:** at most the in-flight chunk reverted, CRC log shows the discard, no crash.
- Hex-corrupt a `.delta`: that chunk regenerates pristine, game continues.

**Failure signatures:**
- Memory creep → a pool free path missed on eviction, or coalescing not running.
- Pop-in inside 128m → window too small or prefetch ordering wrong; adjust the config value, re-measure.
- You reach for a lock → you violated the single-writer rule (§3.2); restructure so `StreamManager` owns the mutation, don't add the lock.

**If stuck 3 days:** streaming bugs are ordering bugs. The single-writer discipline means every mutation has one home — if a mutation is happening somewhere else, that's the bug. Run the force-quit test repeatedly; it's the whole fault-tolerance contract in one keystroke.

**Solo-dev notes:** ~1–2 weeks. Test the corrupt-delta and force-quit paths even though they feel silly — they *are* the "never corrupt a save" guarantee.

---

## Phase 5 — Fluids: CPU Reference (5a), Then GPU Port (5b) *(~3 weeks total)*

**The one new thing (5a):** the fluid CA rules, proven correct on the CPU where you can breakpoint, via conservation unit tests. **The one new thing (5b):** the same rules ported to GPU compute with the plain-write claim pattern (no atomics, §7.3) and the command-list pattern (§7.2), validated against 5a's output. 5a is a correctness oracle; 5b is what ships (8.3 — fluid moved back to GPU for hardware fit + CPU orchestration headroom, §2.2).

**Prerequisites (split on audit — 5a is more independent than the blanket statement implied):** **5a needs only Phase 1 green** — it's a self-contained CPU basin test against the memory model, touching no streaming/delta/eviction machinery at all; it can start as soon as Phase 1 passes, in parallel with Phases 2–4 if you want the throughput. **5b needs Phase 4 green** (it wires into the shipped renderer and the streaming-aware wake-scan). The phase-numbering stays sequential for a single-threaded reading of this document, but if you're ever running two threads of work, 5a doesn't have to wait.

### Phase 5a — CPU Reference (Correctness Oracle)

**Files to create, in order:**
1. `CoreEngine/Simulation/FluidReferenceCPU.cs` — a single-threaded (default) or 8-phase-parallel (optional, §7.3) Burst implementation of Clear / Intent+Claim / Commit, Air-Only, orphan self-free, sleep/wake, viscosity, reactions. Near-player active radius (§7.4).
2. `CoreEngine.Tests/FluidConservationTests.cs` — runs entirely against `FluidReferenceCPU`.

**Build steps:**
- Start **gravity-only** (no diagonals, no pooling); verify conservation; then add down-diagonals; then horizontals — one tier at a time, re-running conservation after each.
- Build the conservation counter **before** the fluid rules — it turns "the water looks weird" into "we lost exactly N drops on tick M."
- This implementation does not need to be fast. Its only job is to be *unambiguously correct* and give 5b something to check against.

**Scene:** `Phase5a_Basin` — enclosed basin; buttons: pour water, drop sand column, lava vent, place-block-into-stream, mine-a-falling-drop. Renders via a debug overlay reading `FluidReferenceCPU` directly (not the shipped renderer, which comes online in 5b).

**Acceptance test (concrete assertions):**
- **Conservation** (the primary invariant): total non-air fluid bytes constant absent reactions/edits, asserted every tick. A column pours and settles flat; occupancy → 0 within N ticks of rest.
- Place a block into a falling stream repeatedly → no lost drops; ledger clean.
- Mine a falling drop → orphan self-frees next tick; orphan counter 0 at steady state.
- Sand piles at ~45°; water+lava → obsidian, both slots freed.

**Failure signatures:**
- Conservation drifts down → Air-Only rule violated (a drop claimed an occupied cell); check the claim comparison.
- Drops vanish on edit → orphan check not running before movement in Intent.
- Water drifts one direction consistently → parity/tie-break bias; add the per-frame hash (C.7).

**If stuck 3 days:** the CA is single-threaded, deterministic, and fully on the CPU — every bug is reproducible in a unit test with the conservation counter pinpointing the exact tick of loss. Start gravity-only, add one tier at a time; the tier you just added is your suspect.

### Phase 5b — GPU Port (Shipped Path)

**Files to create, in order:**
1. `CoreEngine/Simulation/FluidCA.compute` — the same Clear/Intent+Claim/Commit logic, GPU-side, using a **plain (non-atomic) word write** for claims (§7.3, 8.4) — safe by the word-tearing guarantee, verified for Metal specifically as this file's first test.
2. `CoreEngine/Simulation/FluidOpListReadback.cs` — reads the bounded write-op list back via `AsyncGPUReadback`, applies each op through **the same `SetVoxel`** used by mining/building (§8.3), marks chunks dirty.
3. Wake-scan hook in `EditService` (CPU→GPU upload of newly-disturbed regions — immediate, not a readback). *`EditService` at this point is still the Phase 0.5 stub — this phase adds only the wake-scan hook to it; the full mining/building tool tiers arrive in Phase 6. Adding a hook to a stub is additive and legal; do not pull Phase 6's implementation forward.*
4. Wire the shipped renderer (Phase 2's raymarcher) to draw the fluid state as applied via the op-list.

**Build steps:**
- Port `FluidReferenceCPU`'s logic to HLSL line-by-line, exactly as Phase 2 ported the raymarch DDA — with a working CPU reference already in hand, this is a translation exercise, not a design exercise.
- Run the *same* scenarios from 5a's acceptance test on the GPU port; compare **steady-state invariants** (final levels, conserved counts) against 5a's output — not frame-exact positions (§7.8: GPU scheduling isn't order-stable).

**Scene:** `Phase5b_Basin` — same basin, now rendered live through the shipped raymarcher with real GPU fluid.

**Acceptance test (concrete assertions):**
- All of 5a's behavioral assertions reproduced as steady-state invariants on the GPU path.
- Live fluid visible while falling (authoritative-byte proof — the renderer shows it with no fluid-specific code, once an op is applied).
- Pool exhaustion (debug-shrink the GPU pool, breach): distant drops freeze, zero errors, no device removal.
- **The op-list is genuinely bounded**, not accidentally the size of the full active-slot count — instrument and log its size per frame; assert it correlates with *changed* cells, not total active cells.
- **GPU-lane cost measured against the interim budget** (fluid CA + Phase 2's primary raymarch only — **shadow rays do not exist until Phase 7, so they are correctly absent from this measurement**): confirm fluid CA + raymarch together stay under a working ceiling with headroom left for the ≤9.0ms shadow allowance Phase 7 will add. *(The full three-way combined gate — raymarch + shadow + fluid, all together, ≤13.0ms — is a **Phase 7 acceptance criterion**, not this phase's, because shadow rays are the one ingredient Phase 5b doesn't have yet. See Phase 7 below.)*
- **CPU-lane op-list-apply cost measured** (§11.1 gate): should be markedly lighter than 5a's full-CA cost, since the GPU already decided *where* — confirm this is true, don't assume it.

**Failure signatures:**
- GPU port's steady-state invariants diverge from the CPU reference → the HLSL port has a translation bug; diff against `FluidReferenceCPU` step by step, the same way Phase 2's raymarcher was debugged against `RaymarchReference`.
- Op-list size scales with total active slots instead of changed cells → the Commit kernel is appending unconditionally instead of only on actual moves; check the write-op emission is gated on "claim succeeded and cell changed."
- Fluid moves visibly lag the GPU's decision by more than a couple of frames → check the applied-op-list timestamp directly (log it) at this phase; **the speed-clamp mitigation (§8.2) does not exist until Phase 6**, so at this phase the only available diagnostic is the raw readback latency, not a "is the clamp engaging" check. If this phase's raw latency already looks too large, that's data Phase 6 will need, not a Phase 5b failure by itself.

**If stuck 3 days:** you have a proven CPU reference and a bounded op-list to inspect — any GPU-port bug is either "disagrees with the reference" (algorithm, compare step by step) or "the op-list looks wrong" (wiring/emission logic). Determine which before guessing.

**Solo-dev notes:** the Air-Only rule feels too conservative watching a packed column shift one cell/tick — resist "optimizing" it in either implementation; that slowness *is* the pour, and every shortcut reintroduces the deletion bug. The CPU reference is not throwaway work — it's the thing that makes the GPU port debuggable at all, exactly the same role `RaymarchReference` played for the DDA in Phase 2.

---

## Phase 6 — Physics & Editing *(~2 weeks)*

**The one new thing:** the player, physics against CPU terrain (exact) and bounded-latency fluid (§7.2), editing, destruction, buoyancy.

**Prerequisites:** Phase 5 green.

**Files to create, in order:**
1. `CoreEngine/Gameplay/PlayerController.cs` — **build the simple version first**: a character controller with ~6–12 DDA probes against CPU terrain (the reviewer's Option B). Only if it proves insufficient, escalate to `PhysicsTreadmill.cs` (the 540-collider cage). This is the Phase-6 fork noted in §8.1 — decide by trying the cheap option first.
2. `CoreEngine/Gameplay/SweptCCD.cs` — swept pass + depenetration backstop (§8.2), both vs CPU terrain.
3. `CoreEngine/Gameplay/EditService.cs` — the §8.3 path (mining/building/prefab), tool tiers, wake-scan into the Phase-5 fluid pool.
4. `CoreEngine/Gameplay/ProjectileTrace.cs` — CPU `GetVoxel` DDA (§8.4).
5. `CoreEngine/Gameplay/DestructionReducer.cs` — Burst batch destruction + Proxy Drops (§8.5).
6. `CoreEngine/Gameplay/Buoyancy.cs` — three probes from CPU terrain bytes, bounded-latency for fluid (§8.6).

**Scene:** `Phase6_Sandbox` — island spawn; tools at 10/40/200 vox/s; bomb (400K); grapple (60 m/s); a static water body.

**Acceptance test (concrete assertions):**
- Walk/jump; 3-voxel steps climbable un-jumped.
- 0.2m wall, grapple in at 60 m/s: stop at the face, 20/20. Disable the sweep → confirm phasing (test isn't vacuous). Re-enable, force a missed ray → depenetration catches it, one corrected frame, never a tunnel.
- **Adversarial checkerboard (§3.6):** build a striped structure filling the visible radius; brick pool climbs; at the cap, coldest chunks LRU-evict; edits keep succeeding at full speed; frame time doesn't collapse; **memory never exceeds the cap.**
- Drill one region 60s at 200 vox/s: memory sane, persists through save/reload, coalesces on fill-in.
- 400K detonation: recovery ≤3 frames, one Proxy Drop, plausible tally.
- 60 m/s dive into water: buoyancy engages within the mitigated bounded window (§8.2 — a few frames at most, backstopped by depenetration + speed clamp), never an unmitigated slam. This is the concrete proof the 8.3 fluid-on-GPU tradeoff is bounded, not silently broken.
- **Flood-front playtest (8.4 — the narrow case, not assumed fine):** trigger an explosion that breaches a water body, and have the player standing at/entering the leading edge of the resulting flood at the instant it arrives. Explicitly judge, by feel, whether the bounded op-list latency (§7.2) is perceptible at this specific leading-edge-of-motion moment — this is the one place the reintroduced fluid latency could plausibly be felt (settled/dormant fluid, the vast majority of gameplay, has zero lag by construction, §3.10). If it feels laggy, tighten the speed clamp threshold or shrink the active radius (§7.4) and re-test, rather than assuming the bound is imperceptible without checking.
- **CPU-lane total measured** (§2.2): fluid + gameplay + upload under 16.6ms.

**Failure signatures:**
- Tunneling at 60 m/s → sweep threshold too high or depenetration not running post-integration.
- Checkerboard crashes or exceeds cap → the LRU valve isn't firing; the whole §3.6 story is under test here.
- Physics feels laggy near water beyond the mitigated bound → check the op-list readback isn't stalling silently; confirm the speed clamp actually engages when it should (§8.2).

**If stuck 3 days:** the character-controller-first approach means if collision is misbehaving you're debugging ~12 probes, not 540 colliders. If the simple controller genuinely can't do the job, that's data — escalate to the treadmill deliberately, don't thrash between them.

**Solo-dev notes:** the checkerboard test is where §3.6's entire memory story is proven true or found wanting — run it hard. Keep any provisional projectile motion purely visual until a trace confirms, so retro-correction never becomes desync.

---

## Phase 7 — Lighting & Day/Night *(~1 week)*

**The one new thing:** simple point lights + the fused sun-shadow ray (kept, gated, with fallback rungs).

**Prerequisites:** Phase 6 green.

**Files to create, in order:**
1. `CoreEngine/Rendering/LightSourceBuffer.cs` — the flat point-light buffer (§3.8).
2. `CoreEngine/Rendering/SunCycle.cs` — clock → sun direction/color uniforms.
3. Shadow ray added to `Raymarch.compute` — start hard (binary occlusion), verify, then add the penumbra term (C.10). Then the point-light sampling loop (C.11) and the combine (C.9).
4. `CoreEngine/Diagnostics/` — sun-visibility debug view + time-of-day scrubber.

**Scene:** `Phase7_Cycle` — a cave with a large chamber, a mountain in view, placeable torches, a time scrubber.

**Acceptance test (concrete assertions):**
- A torch lights its surroundings via falloff (v1: does *not* bend around a wall — that's v1.5); deep cave genuinely dark.
- Scrub dawn→dusk: mountain shadow sweeps continuously and exactly (no cascade staleness); a single protruding voxel casts a crisp discrete shadow; grazing-angle shadows show **zero acne, zero Peter-Panning by construction** (any artifact ⇒ shadow-ray origin not one cell off the surface).
- Bomb a cave roof at noon: sunlight reaches the floor the **same frame**; pour water off a ledge → **moving shadow** (authoritative-byte bonus).
- **The budget gate (Performance State checked):** raymarch + shadows ≤9.0ms across a full cycle **including golden hour** (capture it specifically). **If it fails, engage the §6.5 fallback rungs and re-measure** — the game ships either way.
- **The true combined GPU-lane gate (moved here from Phase 5b, since this is the first phase where all three ingredients actually exist):** raymarch + shadow rays + fluid CA, all running together, at golden hour, with near-player fluid active — sustained ≤13.0ms, Performance State checked (§2.2, §11.2). **If this specific combination breaches budget even though raymarch+shadow alone (previous bullet) and fluid+raymarch alone (Phase 5b) each passed independently, that is new information** — the two systems compete for the lane in a way isolated testing couldn't show. Apply the fallback order in §2.2 (shrink fluid active radius → two-speed fluid tick → shadow fallback rungs) and re-measure this combined scenario specifically, not just the isolated pieces.

**Failure signatures:**
- Shadow acne/Peter-Panning → shadow-ray origin isn't starting one cell off the hit face; fix the origin, don't add a bias hack.
- Golden-hour frame collapse (shadow+raymarch alone) → grazing rays too expensive; this is exactly what the fallback rungs are for — engage rung 1 (half-res distant shadows) and confirm near shadows stay sharp.
- Golden-hour frame collapse **only when fluid is also active** (passes with fluid idle, fails with fluid running) → the two systems are contending for GPU time/bandwidth in a way neither isolated test revealed; this is exactly why the combined gate above exists as its own line item, not an assumption that "sum of two passing numbers" is safe.

**If stuck 3 days:** the shadow ray is the Phase-2 DDA with a new origin + early-out — if it misbehaves, compare against the Phase-2 `RaymarchReference` with the shadow origin/direction substituted. Point lights are a trivial sampling loop; if lighting looks wrong, render the light positions (debug view) first.

**Solo-dev notes:** the fused shadow ray is a day of code, not a week. The real work is tuning (penumbra constant, ambient curve, sun-color ramp) — that's what the scrubber is for. Tune by eye in real time.

---

## Phase 8 — Chaos & Soak *(~1 week + soak, definition of done)*

**The one new thing:** everything at once, for an hour, asserting nothing breaks.

**Prerequisites:** Phases 0–7 green.

**Files to create, in order:**
1. `CoreEngine/Diagnostics/ValidationSuite.cs` — every §10.4 assertion running continuously (clipmap equality, conservation, no leak, coalescing correctness, no `Resident→Unloaded` during `Saving`).
2. `Game/Phase8Chaos.cs` — the scripted §11.5 sequence + a results logger.

**Scene:** `Phase8_Chaos` — a static water body over a cave, a dense checkerboard base nearby, time locked to golden hour; scripted loop: accelerate to 60 m/s across a chunk boundary, fire a 400K detonation breaching the water, all within a second — randomized offsets, one hour.

**Acceptance test:** across the hour — **zero crashes, zero validation-suite failures, memory never exceeds the cap, every save loads cleanly.** Frame-time may breach ≤3 frames per detonation; memory-safety and save-integrity never breach.

**Failure signatures:** any suite assertion firing points directly at its subsystem (clipmap drift, conservation loss, leak). The suite is the diagnosis.

**If stuck 3 days:** run the loop 5 minutes before an hour — most failures show in the first two loops. A failure here is a regression in a specific subsystem the suite names; go fix that subsystem's phase, re-run its phase test, then re-soak.

**Solo-dev notes:** archive the passing log + the build together as your regression baseline. **This scene passing is the definition of done for the v1 prototype.**

---

*End of implementation guide. Total rough sizing: ~12–16 weeks of focused solo work (sum of the per-phase estimates above, including Phase 0.5), front-loaded on the two hardest pieces (the DDA in Phase 2, fluids in Phase 5), each de-risked by a CPU reference you can breakpoint. AI assistance compresses the mechanical parts (scaffolding, struct headers, test boilerplate, ports from proven references) but not the measurement gates or the debugging of the two hard pieces — plan around the gates, not around generation speed. Build Phase 0 first; Phase 8 passing is done.*

---

# Appendix A: Buffer & Struct Layouts

All GPU-visible structs: 8/16/32-bit integer fields + 32-bit floats only (§1.2). C# mirrors use `[StructLayout(LayoutKind.Sequential)]`, field order identical to HLSL.

**A.1 ChunkRecord** — CPU-only (§3.2)
```
struct ChunkRecord {
    int   residentSlot;      // index into resident window, or NONE
    byte  state;             // 0 Unloaded,1 Loading,2 Resident,3 Saving
    ushort generation;
    uint  lastTouchedFrame;  // LRU key (§3.6, §4.5)
    uint  deltaByteLength;   // 0 ⇒ pristine baseline (§4.1)
    uint  crc32;
}
```

**A.2 Chunk** — CPU resident-window entry (§3.3)
```
struct Chunk {
    int3  coord;
    bool  isUniform;
    byte  uniformMaterial;
    BrickHandle[4096] bricks; // inlined, allocated iff populated (backed by a pool to avoid GC churn)
    bool  dirty;             // changed since last clipmap upload
    bool  deltaDirty;        // edited since last save
}
```

**A.3 BrickHandle** — CPU, 4B, 4096/populated chunk (§3.3) — packed uint
```
// [31]    1=dense (index in [29:0] into Brick Data Pool) | 0=uniform (material in [7:0])
// [30]    Volatile: dense brick created by fluid entering uniform-air (§3.10);
//         freed immediately when active-fluid count hits 0, not on the background sweep
```

**A.4 Brick body** — CPU Brick Data Pool + GPU mirror (§3.4, §3.7)
```
512 bytes, one material ID per voxel, indexed by voxelIndex 0..511 (C.1).
```

**A.5 FluidSlot** — GPU, fluid CA (§3.5, §7) — 8.3: back on GPU; a CPU-side mirror of this struct also backs `FluidReferenceCPU` (Phase 5a), same layout, used only as the correctness oracle
```
struct FluidSlot {
    int   brickDataIndex;    // home brick (next-free when free, intrusive list)
    ushort localVoxelOffset; // 0..511
    byte  materialID;        // cached; validated vs home byte (orphan check §7.3)
    byte  sleepCounter;
    byte  viscosityPhase;
    byte  stateFlags;        // Appendix B
}
```

**A.9 FluidWriteOp** — GPU-emitted, CPU-read (§3.9, §7.2) — the bounded command-list record
```
struct FluidWriteOp {
    uint  brickIndex;        // which Brick Data Pool entry (matches CPU addressing, C.1)
    ushort voxelIndex;       // 0..511 within the brick
    byte  newMaterial;       // the material SetVoxel should write
    byte  reserved;
}
// Emitted only for cells that actually changed this tick — NOT one per active slot.
// Read back via AsyncGPUReadback; CPU applies each op via SetVoxel (§8.3), identical
// code path to a mining/building edit. This is the entire GPU→CPU terrain-adjacent
// contract for fluid; no other mechanism exists.
```

**A.6 LightSource** — GPU, flat point-light buffer (§3.8) — 8.1: replaces v8's LightBrickRecord
```
struct LightSource {
    float x, y, z;           // world position
    float r, g, b;           // color
    float range;             // cutoff radius
    float k;                 // falloff constant (C.11)
}
```

**A.7 MaterialData** — GPU, 32B × 256 (§3.9)
```
struct MaterialData {
    float albedoR, albedoG, albedoB;   // 12B
    float emissive;                    // 4B
    float viscosityDrag;               // 4B (buoyancy)
    float density;                     // 4B (C.6)
    uint  flags;                       // 4B (Appendix B)
    uint  tickInterval;                // 4B (§7.4)
}
```

**A.8 Terrain Clipmap entry** — GPU, 4B per brick slot in the flat window grid (§3.7) — 8.1
```
// flat index = clipmapBrickIndex(brickCoord)  (one bitwise calc, ring-masked, no tree walk)
// [31] 1=uniform (material in [7:0]) | 0=dense (index into GPU Brick Data in [30:0])
```

# Appendix B: Bitfields
- `BrickHandle` — [31] dense flag; [30] Volatile (fluid-created, §3.10); uniform: [7:0] material; dense: [29:0] index.
- Clipmap entry — [31] dense flag; uniform: [7:0] material; dense: [30:0] index.
- `FluidSlot.stateFlags` — [0] Awake, [1] BidPlaced, [2] WonClaim, [3] ForceDemote, [7:4] reserved.
- `MaterialData.flags` — [0] IsFluid, [1] IsFallingSolid, [2] IsFlammable, [3] DamagesPlayer, [4] IsEmissive, [5] Dissolves, [31:6] reserved.

# Appendix C: Equations
- **C.1** Coordinate chain — §2.3 (bitwise; sole implementation in `CoordMath`, C#/HLSL identical).
- **C.2** Coastline: `R_coast = halfWidth + fBm(X,Z)·k`; ocean beyond `√(X²+Z²) > R_coast` (§5).
- **C.5** LOD boundary: `D₀ ≈ 0.1/θpx ≈ 103m → 128m`; v1 tiers 0.1 / 0.4 / 1.6m (§6.4).
- **C.6** Buoyancy: `F = (ρf − ρe)·g·Vsub` (§8.6).
- **C.7** Fluid tie-variety parity: `(X+Y+Z+frame) mod 2` + per-frame hash (§7.3).
- **C.9** Shading combine: `albedo × (sunColor·sunVis·max(0,N·L) + pointLights + ambientSky) + emissive`, then VAO (§6.5).
- **C.10** Soft penumbra (single shadow ray): track `m = min(clearance_i / t_i)` over near-miss cells; `sunVis = smoothstep(0, k_soft, m)` on miss, 0 on hit; `k_soft→0` = hard shadows (§6.5).
- **C.11** Point-light attenuation (v1): `atten = 1/(1 + d²·k)`, summed over lights within `range`; `k` an art constant (§3.8, §6.5).

# Appendix D: Data Formats
**D.1 Delta file** (`{cx}_{cy}_{cz}.delta`): header `{int3 chunkCoord, uint seed, ushort formatVersion, ushort recordCount}`; per deviating brick `{ushort brickIndex, byte kind}` — kind 0 uniform (+1 material byte), kind 1 dense (+512-byte body); trailer `{uint crc32}`. Near-memcpy decode.

**D.2 World file** (`world.meta`): `{uint seed, byte sizeClass, ushort formatVersion, uint anchorCount, uint biomeSeedCount, uint contentVersionHash}` + frozen `FeatureAnchor[]` + Voronoi biome seeds. Written once, read-only. The `contentVersionHash` reserves the v1.5 ScriptableObject-migration slot (§5.5). CRC32 trailer, atomic-write as D.1.

**D.3 Prefab (`.vx`)**: `{ushort sx,sy,sz}` + RLE material bytes. Placed via batch `SetVoxel` (§5.4, §8.3).

---

*End of specification. Phase 0 is the first thing you build; Phase 8 passing is the definition of done for the v1 prototype. The governing discipline throughout: one new untrusted thing per phase; prove correctness on the CPU before chasing performance on the GPU; freeze public APIs on pass, not implementations; and on every subsystem ask — if I delete this entirely, does the game stop being fun?*
