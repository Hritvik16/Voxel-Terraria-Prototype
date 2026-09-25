# The three architectures — what they are, and what it would cost to compare them fairly

Analysis of `github.com/Hritvik16/VoxelTerraria` (both folders) against this
project, 2026-09-17. **Everything below was verified by cloning, importing and
building, not by reading alone** — where a claim rests on reading only, it says so.

---

## 1. What the three projects actually are

| | **Voxel-Terraria** | **VoxelTerraria_Brickmap** | **THIS project** |
|---|---|---|---|
| data structure | **SVO** — `SVONode{childIndex, material}`, octree descent, + a dense 32³ chunk pool as hybrid fallback | **1-bit dual-state brickmap** (`DualState1Bit`, 1024 uints = 1 bit/voxel) with a switchable `BrickMap32Bit` mode | **1-byte brickmap** — 8³ bricks, 16³-brick chunks, flat GPU clipmap |
| chunk size | 32³ (32,768 voxels) | 32³ | 128³ voxels (16³ bricks of 8³) |
| material storage | 1 uint per voxel in the dense pool | **occupancy bit + a compacted "ticket" of SURFACE materials only** (`TICKET_SIZE 8192`) | 1 byte per voxel, 256 materials |
| code | 26 files, **4,300 lines** | 53 files, **8,433 lines** | 171 files, **65,086 lines** |
| tests | 0 | 0 | **562 EditMode, 14,019 lines** |
| docs | none | none | 27 files, **15,126 lines** |
| scenes committed | **0** | 1 | 30 (built by code) |
| Unity | 6000.3.10f1 | 6000.3.10f1 | 6000.3.10f1 |
| history | — | 50 commits, 2026-03-27 → 04-17 | phases 1–7 |

**The naming is a genuine trap:** the GitHub "Brickmap" is **1 BIT** per voxel
(solid/air, with materials stored only for surface voxels). This project is
**1 BYTE** per voxel. They are different architectures, not versions of one.

---

## 2. Do they run? Verified by building, not assumed

### VoxelTerraria_Brickmap — compiles, builds, runs, but draws no terrain

- **Imports with 0 compile errors.** Unity regenerated 22 missing ProjectSettings.
- **Builds a standalone successfully** (111 MB, 0 errors).
- **Runs**: sky, atmosphere, sun, biome minimap and its own FPS counter all work.
- **No voxel terrain renders.**

**Why, traced to root cause.** The repo's `.gitignore` line 88 excludes `*.asset`.
That rule stripped out the **entire ScriptableObject configuration layer**:

- `URP.asset` and `URP_Renderer.asset` — the latter held the
  `RayTracingRenderFeature` *and its settings*. Only the `.meta` files survived.
- `VoxelMaterialPalette` — a `ScriptableObject` with `[CreateAssetMenu]`, so it
  lives in a `.asset`. **13 GUIDs the scene references cannot be resolved.**

I rebuilt the URP pipeline and renderer and re-attached the feature with its
compute shader and blit shader wired (`BringUpURP.cs`, `WireFeature.cs` in that
clone). It still draws no voxels, because `RayTracingRenderFeature` early-returns
on null settings and the material palette the ChunkManager needs is simply gone.

**The files can be recreated. The authored VALUES in them cannot** — material
colours, densities, thresholds, pool sizes were never committed anywhere.

### Voxel-Terraria (SVO) — does not compile

**24 compile errors**, all one family: code written against `NativeArray<T>`
(`.IsCreated`, `.Dispose`) while the fields are declared as managed arrays
(`ChunkData[]`, `Vector3Int[]`). It is **frozen mid-refactor**. Its own first
commit is titled *"kinda broken, kinda not"*.

It also has **no committed scene at all** — `.gitignore` line 77 excludes
`Voxel-Terraria/Assets/Scenes/*_TestScene.unity`, and its `EditorBuildSettings`
points at a `SampleScene.unity` that is not in the repo.

**So there is no measurable performance number for either project today**, and
the "60+ FPS" in a commit message cannot be reproduced or trusted as comparable —
it was measured on an unknown pose with unknown settings. Given what §K of
`PHASE_7_COMPLETION.md` found about *this* project measuring itself inside a pit,
I would treat that number as unverified rather than as a target.

---

## 3. Three ideas in there that are worth having — one is directly useful now

### 3.1 Per-chunk height map with roof-skip / ray teleport — HIGH VALUE

`VoxelTraversal.hlsl:268`. A `StructuredBuffer<float> _ChunkHeightMap` holds each
chunk's max terrain height. Then:

- ray is **above** the roof and pointing flat or up → **skip the whole chunk**;
- ray is above and pointing down → **teleport t to the roof plane** analytically.

**Why this matters here specifically.** Our golden-hour shadow ray costs 9–12 ms
(§L.2) and *points at the sun* — `direction.y > 0` — which is exactly the
"flat or up, skip the chunk entirely" case. Our air-mip (§8.8) skips empty bricks
but must descend a hierarchy to do it; a roof test is **one float compare per
chunk** and can discard a whole 12.8 m chunk at once. This is the most promising
untried lever for the one problem this project has not solved.

### 3.2 Same-chunk neighbour fast path — DIRECTLY APPLICABLE

`VoxelFastPath.hlsl`, 16 lines. Before resolving a neighbour through the global
chunk table, check whether it is inside the chunk already fetched, and if so index
it directly.

**Our `ShadowClearance` does up to six `ShadowSolidAt` calls per dense step, each
a full `ReadClipmap` + brick resolve** — and at 8³ bricks most of those six
neighbours are in the brick just read. This is a small, local, measurable change.

### 3.3 Surface-only material storage — INTERESTING, NOT FOR NOW

1 bit occupancy + a compacted ticket of materials for surface voxels only. You
never see an interior voxel, so storing its material is waste. Against our 1 byte
per voxel this is a real memory argument — but it is an architecture change, not
an optimisation, and §3.10's authoritative-byte rule is load-bearing here.

Also present and worth knowing about: a **greedy mesher / compute-rasteriser
backend** switchable against the raytracer, **8 clipmap layers**, and a
`MacroMaskBaker` building 4×4×4 occupancy masks (our `OccupancyMask` is the
equivalent).

---

## 4. What it would take to bring them to parity — honest estimate

"Parity" is measured against what this project *is*, which is mostly not features:
**65k lines, 562 tests, 30 rigs, CPU oracles for every GPU path, and 15k lines of
recorded evidence.** The features are the small half.

### VoxelTerraria_Brickmap (1-bit) — the closer one

| stage | work | estimate |
|---|---|---|
| **A. Bring-up** | re-author the ScriptableObject layer (material palette, URP assets), get terrain rendering, verify against a screenshot | **1–2 sessions** |
| **B. Feature parity** | persistence (D.1 deltas, save/load), streaming + eviction + LRU, sun shadows with penumbra, tonemapping, §3.8 light cap, player/physics parity, fluid promoted from "attempt 2 lol" to §7's tiled substrate | **8–14 sessions** |
| **C. Verification parity** | ~560 tests, CPU oracles (raymarch, fluid, shadow), 30 rig scripts, the measurement methodology | **12–20 sessions** |
| | **total** | **~21–36 sessions** |

### Voxel-Terraria (SVO) — much further away

| stage | work | estimate |
|---|---|---|
| **A0. Make it compile** | finish the abandoned NativeArray↔managed refactor (24 errors, mechanical) | **~0.5 session** |
| **A. Bring-up** | **reconstruct the scene from nothing** — every inspector reference and tuned value is lost — plus the same ScriptableObject layer | **2–4 sessions** |
| **B. Feature parity** | everything above, plus fluids/lighting/torches/biome depth it does not have | **14–22 sessions** |
| **C. Verification parity** | as above, and an SVO needs *different* oracles (tree invariants, node pooling) | **14–22 sessions** |
| | **total** | **~31–48 sessions** |

**Uncertainty is dominated by stage C, and it is the stage that matters.** This
project's confidence comes from its oracles and rigs, not its features — and §K
showed that even with all of that, four sessions of numbers were invalid because
of one bad camera pose. A comparison between three engines where two have no test
suite would produce numbers nobody should trust.

**Wall-clock:** these are mostly unattended-session sized. The binding constraint
is not my time, it is that **each real measurement costs 5–30 minutes of cooled,
sustained runs**, and stage C is nearly all measurement.

---

## 5. Recommendation

**Do not bring all three to parity to find out which is best.** The cost is
30–80 sessions, most of it rebuilding test apparatus twice, and the answer would
still be confounded by the two ports being younger and less tuned.

**The cheap version of the same question:** the architectures differ in a handful
of specific mechanisms, and those can be tested *inside this engine*, where the
oracles, rigs and sustained-probe methodology already exist.

1. **Port the height-map roof skip (§3.1) into our shadow ray and measure it.**
   ~1 session, and it targets the one unsolved problem — the 9–12 ms grazing
   shadow cost. If it works, it is worth more than the whole comparison.
2. **Port the same-chunk neighbour fast path (§3.2) into `ShadowClearance`.**
   ~half a session.
3. **If the 1-bit memory argument matters later**, measure it as a variant of
   *this* engine rather than as a separate project.

**Worth doing regardless, cheaply:** fix the SVO project's 24 compile errors and
commit the missing `.asset` files to that repo (remove `*.asset` from its
`.gitignore`). That is ~1 session and it stops both projects from decaying
further — right now neither can be run by anyone, including their author.
