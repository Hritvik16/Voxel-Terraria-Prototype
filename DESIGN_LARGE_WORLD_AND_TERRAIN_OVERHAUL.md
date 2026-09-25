# A much larger world with long render range, and the terrain overhaul that feeds it

**Status: DESIGN PROPOSAL, NOT ADOPTED.** Written 2026-09-24 for the owner (decision 13).
No code was written for this. It builds on `AMENDMENT_8_11_RENDER_RANGE.md` (also a
draft), whose facts it reuses and does not repeat at length.

Every number is tagged **ESTABLISHED** (read from code or a recorded measurement,
cited) or **ESTIMATED** (derived, not measured). Nothing ESTIMATED here should be
quoted as a result. §7 lists what to measure before committing to anything.

---

## 0. Summary

**The request:** a world several kilometres across, with Palworld-like view
distance. That means mountains and coastlines visible to the horizon, not a hard
cutoff at 290 m. The terrain-generation overhaul comes first, and must be able to
produce cheap, coarse, far-away data directly.

**The recommendation is three bands, built in this order:**

| Band | Distance | Representation | Status |
|---|---|---|---|
| Near | 0 – ~290 m | today's voxel window (0.1 m), unchanged | exists |
| Far | ~250 m – 3+ km | **a heightfield clipmap: 2.5D height and colour, 4 nested levels, generated straight from the terrain generator's 2D fields** | new (F) |
| Mid (optional) | ~290 m – 1.2 km | Amendment 8.11 Option B: a true voxel cascade | **deferred**; build only if §7's seam test says the far band isn't enough |

The terrain overhaul (T) is designed around one idea. **Split generation into a 2D
"shape" function, evaluable at any resolution, and a 3D "detail" function run
only near the player.** That split is what makes the far band cheap. It also
fixes the other blocker: a world whose tallest point today is **12 m**.

**Estimated total:** three phase-sized pieces (T, F, V below), about 25–40 MB of
new GPU memory, and a far-band per-frame GPU cost that is **unknown until
prototyped**. §7's first measurement exists to find that number before anything
else is committed.

---

## 1. Facts today

| Fact | Value | Source |
|---|---|---|
| Island (sizeClass 1) | 954.5 m coast radius, ~1.9 km across | ESTABLISHED, `Content.cs` / 8.11 §0 |
| Max render distance | 290 m (the window's corner half-diagonal) | ESTABLISHED, `LODConfig.TIER_OUTER_RANGE_M` |
| Resident window | 32 × 32 chunks = 409.6 m square | ESTABLISHED, `EngineConfig.WINDOW_CHUNKS_XZ` |
| LOD tiers 1/2 | **same extent** as tier 0, coarser sampling: they cut cost, not range | ESTABLISHED, 8.11 §1 |
| Coarse data source | derived only from **resident** chunks | ESTABLISHED, 8.11 §3 |
| Tallest terrain | `MAX_TERRAIN_HEIGHT` 120 voxels = **12 m**; only chunk layer y=0 generates | ESTABLISHED, `Content.cs:133`, `StreamManager.MAX_GENERATED_CHUNK_Y = 0` |
| Camera altitude ceiling | `MIRROR_CHUNKS_Y` 4 = **51.2 m**; above it every ray starts out of bounds and the screen goes black | ESTABLISHED, `EngineConfig.cs` comment on `MIRROR_CHUNKS_Y` |
| Generation cost | column sampling = 5.16 ms of 5.58 ms per chunk (92.6%), measured **before** Burst | ESTABLISHED, `ColumnSampleJob.cs` header |
| Generator shape | per column: `SampleColumn → height + biome`, plus analytic anchor features (`FeatureCarve`: mountains, craters, caves) | ESTABLISHED, `ColumnSampleJob.cs`, `FeatureCarve.cs` |
| Memory now | physical footprint **1.6 GB** steady state | ESTABLISHED, `PHASE_4_COMPLETION.md` (footprint table) |
| Budget | §11.3 ≤ 3,000 MB; 8 GB machine | ESTABLISHED |

**Two blockers besides range:** the world is 12 m tall, and the camera can't fly
above 51 m. Palworld-like terrain has mountains hundreds of metres high, and a
viewpoint you can glide from. Both have to change along with range.

## 2. What "Palworld-like" is taken to mean (confirm or correct these)

These are **targets I've assumed**. They weren't measured from Palworld, and the
owner should adjust them:

| Target | Proposed value |
|---|---|
| World size | a new **sizeClass 2**, about 6–8 km across (additive per §D.2; sizeClass 0/1 untouched) |
| Terrain height range | sea floor to peaks of ~250–300 m (≈ 2,500–3,000 voxels) |
| Visible terrain | silhouettes and colour to ≥ 3 km; recognisable shapes (ridges, coast, lakes) to ~1.5 km |
| Near detail | unchanged: 0.1 m voxels, editable, shadowed, within ~250 m |
| Far detail | not editable in place; follows edits only where they change a column's top (§4.4) |
| Flight / gliding | camera up to ~400 m without the screen going black |

## 3. Why the current design can't simply be stretched

This is covered in detail in 8.11 §§1–4. In short:

- Raising `TIER_OUTER_RANGE_M` makes rays march further, then die at the window
  guard. **Cost rises; range doesn't.**
- Growing the window (8.11 Option A) is quadratic. XZ = 64 is ESTIMATED at about
  +1.8 GB, past §11.3.
- Converting to a true voxel cascade (Option B) reaches about 1.16 km with three
  tiers, but still needs a coarse data source beyond residency, and touches §0.3's
  protected layout. That's a phase in itself, and it still stops at about 1.2 km.
- **None of these reaches a 3 km horizon affordably.** Beyond about 1 km a
  0.1 m-voxel world is sub-pixel anyway. A pixel at 960×540 and a 60° field of view
  covers about 2.1 m at 1 km (ESTIMATED: 2·tan(30°)·1000 / 540). That's the
  regime where every large-world engine switches to a 2.5D heightfield or impostors.

## 4. The proposal

### 4.1 T: terrain-generation overhaul (first, and the foundation)

**Split the generator in two:**

1. **Shape field `S(x, z, lod)` → `{height, biome, surface material, water level}`.**
   Deterministic, 2D, and evaluable at any resolution. It's **band-limited**: at a
   level whose texel is `t` metres, it skips noise octaves with wavelength below
   `2t`. Coarse evaluation is therefore both cheaper and alias-free, rather than a
   point-sampled fine field that shimmers as the camera moves. Anchored features
   (mountains, craters, ridgelines, rivers) stay analytic, as `FeatureCarve` already
   is, so they evaluate at any resolution for free.
2. **Detail field `D(x, y, z)` → material.** Caves, overhangs, arches, ore, strata.
   It runs **only for resident near chunks**, on top of `S`. The far band never
   calls it, because caves and overhangs are invisible at a kilometre.

**Taller world:** raise the height range to ~250–300 m. That means
`MAX_GENERATED_CHUNK_Y` goes past 0 and the vertical residency changes (§4.3).
Height noise and anchors are re-authored at the new scale.

**Rules the overhaul must keep:**
- Burst strict-float determinism: the existing Burst-vs-Mono byte-identity
  benchmark extends to `S` at every LOD.
- New content goes in an additive sizeClass 2 (§D.2); content hashes for 0/1 are untouched.
- **`S` at LOD 0 must equal the voxel surface exactly**, meaning the top solid
  voxel of every column `D` produces, wherever `D` doesn't carve the top. That's an
  EditMode oracle; §4.2's seam depends on it.

**Cost (ESTIMATED):** one phase. The generator code is about 1,400 lines today
(`WorldGen/*.cs`). The overhaul mostly restructures it; it doesn't grow it. The
new oracles are LOD consistency, determinism and the band-limit check.

### 4.2 F: far-field heightfield clipmap (the range)

**Data:** four nested, camera-centred rings of 1024 × 1024 texels. Each texel holds
height (u16, 0.1 m units), surface-colour index (u8) and flags (u8: water,
edited), 4 bytes in all.

| Level | Texel | Extent | Covers out to (half-extent) |
|---|---|---|---|
| F0 | 0.8 m | 819 m | 410 m |
| F1 | 1.6 m | 1.64 km | 819 m |
| F2 | 3.2 m | 3.28 km | 1.64 km |
| F3 | 6.4 m | 6.55 km | **3.28 km** |

**Memory:** 4 × 1024² × 4 B = **16 MB**, plus a max-height pyramid per level for
empty-space skipping (+33%), about **21 MB** in total (ESTIMATED arithmetic, not
measured). **It's a separate buffer; the clipmap and brick layouts §0.3 protects
are untouched.**

**Filling it:** straight from `S(x, z, lod)` on the existing generation workers,
toroidally, one strip of rows or columns at a time as the camera crosses a texel.
The rings are power-of-two, with bitwise wrap, per CLAUDE.md's hard invariant.
Cost at 60 m/s traversal: F0 advances about 75 texel rows a second, i.e. about 77 k
columns/s. At the pre-Burst ~0.31 µs/column (5.16 ms / 16,384 columns), that's
**~24 ms of worker CPU per second, ~2.4% of one core** (ESTIMATED; band-limited
coarse levels will be cheaper). Upload is about 300 KB/s, far inside §4.3's
3.1 MB/frame budget. **No chunk residency is involved**, which is exactly what
8.11 §3 said was missing.

**Rendering, and why it's small:** today, a ray that leaves the voxel window ends
with `hit = false` and draws sky (`Raymarch.compute`'s window guard). F turns
that exit into a **hand-off**. The ray continues from the exit point as a
heightfield march in F0 → F3, stepping by the max-height pyramid. **This
project's roof-skip pyramid (commit `5856e2b`) already builds that kind of
structure from the clipmap**, so the technique is in the codebase and measured.
It's roughly 20–60 steps per far pixel (ESTIMATED), and only for pixels whose ray
escaped the window. Today those pixels cost nearly nothing.

- **Shading:** the look-1 path with the far texel's colour index and a normal from
  height differences; distance haze as today. Sun shadows on far terrain use a
  heightfield march toward the sun on the same pyramid. Rung 3 ("off") applies
  per band.
- **The seam at ~250–290 m:** within an overlap band both representations exist.
  Blend by distance, and let haze carry the transition (the look already hazes
  toward 290 m). §4.1's LOD-0 oracle makes the two surfaces agree to within a
  texel; §7 measures whether that's visible.
- **Edits:** a CPU edit that changes a column's top voxel updates that column's
  F0–F3 texels (max/mean by level) from the CPU state, which keeps the invariant
  that the CPU is the only terrain writer. Edits are sparse, so this is cheap and
  bounded. Edited far texels persist with the deltas, so a return visit shows
  them.

**Cost (ESTIMATED):** one phase: the data ring and streamer, the GPU pass, far
shadows, the seam blend, and a CPU heightfield-march oracle (the analogue of
`RaymarchReference`) for GPU-vs-CPU pixel agreement.
**§0.3 impact:** the primary traversal's *exit* behaviour changes, from
`hit = false` to a hand-off. That's a traversal change and needs review, but it
needs no layout or sync-contract change.

### 4.3 V: vertical range and altitude (needed by T and by flight)

- **Advance a ray to the window's entry plane instead of killing it**, the fix
  `EngineConfig.cs` already names for the 51.2 m ceiling. With F in place, a ray
  that never enters the window is simply a far-band ray. That removes the
  altitude ceiling as a black-screen failure.
- **Surface-following vertical residency:** keep the mirror a modest number of
  chunk layers tall (about 8, i.e. 102 m), centred on the camera's height above
  the local surface, rather than growing it to cover 300 m of mountain. Anything
  outside is drawn by F. Mirror memory grows linearly: today's 4 layers → 8 layers
  is about +64 MB of clipmap per copy (ESTIMATED from 64 MB per 4 layers,
  8.11 §1). The dense brick pool tracks surface *area*, which mountains raise
  only modestly (ESTIMATED; measure).
- `WINDOW_CHUNKS_Y` (CPU, 16) versus `MIRROR_CHUNKS_Y` (GPU) stay separate, per
  CLAUDE.md's landmine note. The oracle tests that build tall synthetic windows
  are unaffected.

**Cost (ESTIMATED):** about half a phase, and it can land inside T or F.

### 4.4 Deferred: M, the mid voxel cascade (8.11 Option B)

Build this only if §7's seam test shows the heightfield looks wrong at 290 m –
1.2 km. That would be because overhangs or cliff faces read as mush there, which
at a ~2 m pixel footprint (see §3) is unlikely. It's a phase plus a §0.3
review; 8.11 costs it at about 1.4 GB of pools and clipmaps (ESTIMATED).

## 5. Cost table

| Piece | GPU memory | CPU | GPU per frame | Engineering | §0.3 |
|---|---|---|---|---|---|
| T terrain overhaul | 0 | generation re-split; far `S` is cheaper per column than today's | 0 | ~1 phase | generation determinism (review) |
| F far heightfield | ~21 MB | ~2–3% of one worker core at 60 m/s | **UNKNOWN**: far pixels go from ~free to 20–60 heightfield steps plus a shadow march | ~1 phase | traversal exit (review) |
| V vertical / altitude | ~+64 MB per mirror copy (8 layers) | small | small (the entry-plane advance saves work) | ~0.5 phase | clipmap Y dims (review) |
| M mid voxel cascade (deferred) | ~+600 MB (8.11) | coarse chunk generation | tier-1/2 marches | ~1 phase | layout + sync (review) |

**Against the budget:** T + F + V adds about 150 MB to a 1.6 GB footprint
(ESTIMATED), comfortably inside §11.3. **Frame time is the open risk, not memory.**

## 6. Order of work, with gates

1. **Prototype F on today's world, before T.** The sizeClass 1 island is already
   1.9 km across, 6.6× wider than what we can draw, so it's a ready-made test bed.
   Fill F from today's `SampleColumn` (not yet band-limited). Measure the
   far-pixel cost with the sustained orbit rig and a high-altitude pose, and look
   at the 290 m seam. **This single measurement decides whether the plan holds.**
2. **T**, the overhaul, with the LOD-0 equality oracle and band-limited `S`.
3. **V** with T, because the taller world needs it.
4. **F** in production form (edits, persistence, shadows, oracle).
5. Re-baseline every rig. This is also the natural Unity-upgrade boundary
   (`UNITY_VERSION_RESEARCH_2026-09.md` §4).
6. **M** only if the step-1 seam evidence demands it.

## 7. Measure before deciding

1. **The far-band GPU cost** (step 1 prototype): sustained golden-hour orbit plus a
   300 m-altitude pose, far band on vs off, with the counterbalanced arms this
   project uses.
2. **Seam visibility at 250–290 m**: before/after stills at three times of day,
   read by eye.
3. **`S` cost per column under Burst** at 4, 6 and 8 octaves. The 0.31 µs figure
   is pre-Burst.
4. **Dense-brick peak with 8 mirror layers over mountainous sizeClass-2 terrain**,
   using `BrickDataPool.PeakUsed`, because the "modest" surface-area growth in §4.3
   is an estimate.
5. Whether the owner's picture of "Palworld-like" matches §2's targets.
