# Amendment 8.11 — Render Range vs. World Size

**Status: DRAFT, NOT ADOPTED.** Written 2026-08-30 for human review.
Nothing in this document has been implemented. `LOD_TIERS` remains 3,
`TIER_OUTER_RANGE_M` remains `{64, 128, 290}`, `WINDOW_CHUNKS_XZ` remains 32,
and `Content.cs`'s sizeClass scaling is untouched.

**Every claim is tagged ESTABLISHED (read from code, file:line cited) or
ESTIMATED (derived, not measured).** The distinction is the point of the
document — §0.1 invariant 12 requires a plausible bug be "a hypothesis with a
measurement attached, not a fact", and the same standard applies to a plan.

---

## 0. The problem in one line

The island is **~1,909 m across**; the renderer can draw **290 m**. It is
6.6× wider than anything that can be shown.

| quantity | value | source |
|---|---|---|
| island coast radius, sizeClass 1 | 954.5 m | ESTABLISHED — `Content.cs:127` |
| island coast falloff | 290.9 m | ESTABLISHED — `Content.cs:128` |
| max render distance | 290 m | ESTABLISHED — `LODConfig.TIER_OUTER_RANGE_M[2]` |
| resident window width | 409.6 m | ESTABLISHED — `WINDOW_CHUNKS_XZ=32 × 12.8 m` |

These two numbers were never reconciled. §11.3 lists both the
"Terrain Clipmap (flat window grid)" and "LOD Cascade Pools" rows as
**`[Phase 4]`** and **`[Phase 2]`** — placeholders never filled in. The window
was sized against the *streaming* requirement (§4.3, 60 m/s traversal), and the
island was scaled 9.09× in `Content.cs` to fix a *different* problem (traversal
spending 85% of its time over open ocean). Neither change consulted the other.

---

## 1. THE DECISIVE FACT: the cascade is not a clipmap cascade

**ESTABLISHED. Tiers 1 and 2 cover exactly the same spatial extent as tier 0,
sampled coarser. They reduce traversal cost. They do not extend range.**

Three independent confirmations in the code:

**(a) Construction** — `CascadeTierPool.cs:83-103`:
```csharp
_windowDimsChunks = windowDimsChunks;              // SAME window as tier 0
_chunkMask        = windowDimsChunks - int3(1,1,1);// SAME ring mask
_coarseBricksPerChunkEdge = 128 / factor / 8;      // FEWER bricks per chunk
_windowDimsCoarseBricks   = windowDimsChunks * _coarseBricksPerChunkEdge;
```
The chunk extent is the constructor argument, identical for every tier;
`Phase4Bootstrapper.cs:127-128` passes the same `mirrorChunks` to
`TerrainClipmap` and `LODCascadeManager`. Only the per-chunk brick count
shrinks: 16 → 8 → 4 coarse bricks per chunk edge.

**(b) The shader says so in a comment** — `Raymarch.compute:562-565`:
> "ONE guard covers all three tiers. **Tiers 1 and 2 cover the same spatial
> volume as tier 0, sampled coarser**, so a voxel inside the tier-0 window is
> inside theirs by construction. No per-tier origin uniforms are needed and
> there is deliberately only one bounds site."

**(c) Addressing is identical** — `CascadeTierPool.ChunkSlot` (line 171) is
`chunkCoord & _chunkMask`, the same toroidal ring index the shader applies to
all tiers at `Raymarch.compute:196` and `:224`. There is no per-tier origin
uniform, by deliberate design.

### Consequence: the memory shrinks per tier instead of the range growing

| tier | voxel | bricks/chunk edge | clipmap | extent |
|---|---|---|---|---|
| 0 | 0.1 m | 16 | 64.0 MB | 409.6 m |
| 1 | 0.2 m | 8 | 8.0 MB | **409.6 m** |
| 2 | 0.4 m | 4 | 1.0 MB | **409.6 m** |

A true clipmap cascade spends *constant* memory per level and *doubles* extent
per level. This one spends 1/8 the memory per level and holds extent fixed. It
is a level-of-detail optimisation, not a range extension — which is exactly
what Amendment 8.9/8.10 asked for (frame cost), and it succeeded at that.

---

## 2. Why raising `TIER_OUTER_RANGE_M` alone does nothing

**ESTABLISHED.** The 290 m limit is enforced **twice**, and the config constant
is the *looser* of the two:

1. **Config** — `RaymarchFeature.cs:274-276` feeds `TIER_OUTER_RANGE_M × 10`
   into `_TierOuterRangeVoxels`; `Raymarch.compute:516` sets
   `maxDist = _MaxRayDistance` (= tier 2's outer bound).
2. **Window bounds guard** — `Raymarch.compute:572-580`, the first statement in
   the march loop:
   ```hlsl
   int3 rel = VoxelToBrick(voxel) - _WindowOriginBricksPacked.xyz;
   if (rel.x < 0 || ... || rel.z >= (int)_WindowDimsBricks.z) { hit = false; break; }
   ```

The guard exists because `ReadClipmap` masks toroidally with no bounds check,
so a read outside the window **aliases silently back inside** — the phantom-
terrain bug of `PHASE_3_COMPLETION.md §6.2`. It cannot simply be relaxed.

Raising `TIER_OUTER_RANGE_M[2]` past 290 m makes rays march further and then
terminate at the window edge with `hit = false`. **Cost rises, visible range
does not.** The real limit is 204.8 m axis-aligned / 289.6 m diagonal — the
window's own geometry, which is why 290 was chosen in the first place
(`LODConfig.cs:43-49` documents this derivation explicitly).

---

## 3. The binding constraint: cascade data comes from resident chunks

**ESTABLISHED.** `CascadeTierPool.UploadDirty(ChunkStore store, ...)`
(line 183) and `SubmitPrecomputed` (line 163) both derive tier data by
downsampling chunks **resident in `ChunkStore`**. There is no source of coarse
data for a chunk that is not resident.

So cascade extent ≤ ChunkStore residency ≤ load radius ≤ `WINDOW_CHUNKS_XZ`.
**Extending range is therefore not a tier-count question at all.** Adding
tier 3 and tier 4 at the current design would add two more same-extent,
coarser copies of the same 409.6 m — more memory, more downsample cost, and
**zero additional range**. `LOD_TIERS = 3` is not what is limiting range, and
raising it would not help.

---

## 4. Options, with costs

### Option A — grow `WINDOW_CHUNKS_XZ` (quadratic)

ESTABLISHED scaling (clipmap = `XZ² × 4 × 4096 × 4 B`), ESTIMATED pool growth:

| XZ | corner reach | tier0 clipmap | tier0 pool (est.) | total added (est.) |
|---|---|---|---|---|
| 32 (now) | 289.6 m | 64 MB ×2 | 244 MB ×2 | — |
| 64 | 579.3 m | 256 MB ×2 | ~976 MB ×2 | **~+1.8 GB** |
| 128 | 1158.5 m | 1024 MB ×2 | ~3.9 GB ×2 | far past budget |

Pool growth is ESTIMATED: dense bricks track the terrain *surface*, so a 4×
area increase implies ~4× the measured 336,731 peak ≈ 1.35 M bricks, needing a
cap near 2 M. Measured footprint after tonight's fix is **1.6 GB**; XZ=64 would
put it around **3.4 GB**, past §11.3's ≤3,000 MB ceiling and back into the
memory-pressure regime that caused the 1,384 ms p99 this branch just fixed.
**Not recommended without measuring the actual dense-brick peak at XZ=64
first** — the 4× figure is geometry, not data.

### Option B — convert to a TRUE cascade (each tier 2× extent, 2× voxel)

ESTIMATED. Constant memory per level, doubling extent:

| tier | voxel | extent | corner reach |
|---|---|---|---|
| 0 | 0.1 m | 409.6 m | 289.6 m |
| 1 | 0.2 m | 819.2 m | 579.3 m |
| 2 | 0.4 m | 1638.4 m | **1158.5 m** |

**Three tiers — the count we already have — would reach 1158 m and cover the
954.5 m island**, because extent doubles instead of resolution halving. Each
level costs roughly one tier-0 clipmap (~64 MB ×2) plus a pool sized for
~one tier-0 surface (~336 k bricks ≈ 172 MB ×2), so ESTIMATED ~1.4 GB total
pools+clipmaps versus today's 837 MB. Plausibly inside §11.3.

**But it requires a coarse data source beyond the resident window** (§3), which
today does not exist. That means:
- per-tier window origins and per-tier bounds guards in the shader (the current
  single-guard design is explicitly predicated on shared extent)
- per-tier ring addressing in `CascadeTierPool`
- a streaming path that produces downsampled data for non-resident chunks —
  either generating them coarsely on workers, or a separate far-field structure

**§0.3 impact: this touches the chunk/brick/clipmap memory layout and the
CPU/GPU sync contract (§3.9) — both on the "may NOT modify without explicit
review" list.** Scale: a new subsystem plus a shader rewrite of the traversal
bounds logic, with new oracles. This is Phase-sized work, not a patch.

### Option C — shrink the island to fit (zero engineering)

`Content.cs:124-128` scales sizeClass 1's geometry by 9.0909 from sizeClass 0.
Choosing a smaller multiplier so the coast radius lands near 250–290 m makes
the island fully visible immediately. **Cost: one constant.** It must be
ADDITIVE per §D.2 (a new sizeClass, not a mutation of 0 or 1) because content
hashes are recorded against existing classes — `Content.cs:114-116` states this
rule explicitly.

Downside: it re-creates the exact problem sizeClass 1 was introduced to fix —
the window being wider than the world, traversal over open ocean. There is a
window of multipliers where both hold (island ≈ window ≈ 300–400 m); sizeClass 1
overshot it by ~3×.

### Option D — distance fog (cosmetic, compatible with all of the above)

The screenshot evidence is a **hard flat cutoff against sky** — terrain is
complete and hole-free right up to the boundary. What reads as "broken" is the
abruptness, not the distance. A distance fade costs a few shader lines and no
memory, and would make 290 m read as an atmospheric horizon rather than a
missing world. It does not add range and should not be confused with a fix.

---

## 5. Recommendation

**Short term: Option D + Option C.** Fog makes the current range look
intentional; an additively-defined sizeClass sized near the window makes the
world match the renderer. Together they cost roughly a day and no memory, and
they unblock playtesting immediately.

**Long term: Option B, as its own phase.** It is the only option that both
reaches the island and stays inside §11.3, and it is what "LOD cascade" is
normally understood to mean. It should not be attempted as an amendment to
Phase 4 — it needs its own gate, its own oracle, and a §0.3 review.

**Option A is not recommended at any size**: quadratic memory against a fixed
8 GB budget, and XZ=64 alone likely re-opens the memory pressure that produced
the 1,384 ms p99 this branch just eliminated.

**Do not raise `LOD_TIERS`.** §0.2 says "never for v1", and per §3 it would not
extend range anyway under the current design — the constraint is extent, not
tier count. If Option B is adopted, 3 tiers suffice.

---

## 6. What must be measured before any of this is decided

1. **Dense-brick peak at XZ=64** — the ~4× estimate in Option A is geometry.
   `BrickDataPool.PeakUsed` (added 2026-08-30) makes this a one-run measurement.
2. **Whether tier 2 at 0.4 m is visually acceptable at 579–1158 m.** Amendment
   8.9 §3 flags tier 2's outer bound as an ASSUMPTION, and its pixel-subtend
   derivation is unresolved between 1080p and the 960×540 internal resolution
   (`LODConfig.cs:52-59`). At 540 the formula gives tier 2 ≈ 412 m, already
   past the 290 m the window allows — so the window has been the binding
   constraint since the cascade shipped, and the pixel math has never actually
   been tested at its own limit.
3. **§11.3's empty rows.** The clipmap and cascade lines should be filled in
   with tonight's measured numbers regardless of which option is chosen.
