# Amendment 8.13 — Palette Brick Bodies

**STATUS: DRAFT, NOT ADOPTED.** Written 2026-10-04 by overnight task 26c. The
owner adopts it after reading it. Until then ARCHITECTURE_v8.6 §3.4 / A.3 / A.4
/ A.8 and §11.3 stand unchanged, and no engine code implements this.

**Approval so far:** the owner, 2026-10-04 (in chat with the supervisor): "lets
try your way". That is per-brick material palettes. It is NOT surface-only
"tickets". Tickets were rejected because buried voxels would be re-derived from
a generator that still changes every B step.

## 1. What changes, in one paragraph

Today a dense brick body is always 512 bytes, one material byte per voxel
(at-a-glance line 15, §3.4, A.4). The change: a dense body becomes **a small
palette of the materials the brick holds, plus one 1-, 2- or 4-bit index per
voxel**. The 512-byte raw body stays as a fourth format, used for bricks with
more than 16 materials and as the rollback format. Every voxel's material stays
exact. Nothing is re-derived from the generator. An edit is still a write of a
few bits in place.

The handle (A.3) and the clipmap entry (A.8) stay 4 bytes. Two of their index
bits now carry the format, and the index counts 4-byte words instead of 512-byte
slots. The GPU mirror stays a verbatim copy of the CPU pool (§3.7). The frozen
contracts do not change: `IWorldQuery.GetVoxel`, `IEditService.SetVoxel`, the D.1
delta format and the §4.4 table (see §6).

## 2. The measurement that picks the formats (MEASURED)

`Assets/CoreEngine/Tests/PaletteHistogramReport.cs` is report-only and `Slow`.
Run it with `REPORTS=1 tools/run-editmode-filter.sh PaletteHistogramReport`.
Output: `Logs/TerrainGen/palette_histogram.txt` (435 s, 2026-10-04).

Setup:
- Map v4, seed 42, through the real `TerrainGenChunkSource`. This is generated
  terrain only: no edits, no fluid.
- Three sites. Each is 21×21 chunk columns (`TG2_LOAD_RADIUS_CHUNKS` = 10) × 7
  layers centred on the site's ground layer. That is 3,087 chunks, the resident
  count the v4 poses report.
  - **spawn**: `Tg2Spawn`'s spiral, which the walk circles.
  - **peak**: `map.PeakPosition`, the summit_view pose.
  - **from2k**: the peak_from_2km_ground pose.
- Air counts as a material.
- Tiers 1 and 2 come from the shipped `LODDownsampler` chain, cut into 8³ bricks
  as `CascadeTierPool.WriteChunkFromDownsampled` cuts them.

| fraction of dense bricks with N distinct values | 1 | 2 | 3–4 | 5–16 | 17+ | dense bricks |
|---|---|---|---|---|---|---|
| tier 0, all sites | 0.00% | 61.91% | 37.99% | 0.10% | 0.00% | 1,209,004 |
| tier 0, peak (the worst site) | 0.00% | 56.10% | 43.85% | 0.05% | 0.00% | 473,347 |
| tier 1 (0.2 m), all sites | 0.00% | 48.78% | 50.83% | 0.39% | 0.00% | 240,968 |
| tier 2 (0.4 m), all sites | 0.00% | 38.42% | 60.37% | 1.20% | 0.00% | 49,602 |

- No generated brick holds more than 7 distinct values.
- 51% of tier-0 dense bricks hold air.
- Only 34% of the 2-value bricks are "air + one material". The rest are
  solid/solid boundaries (strata, soil over rock). So the format must not assume
  air is one of the palette entries.

Mean body size per format set. These are DERIVED from the histogram and the §3
layout:

| format set | tier 0 all | tier 0 peak | tier 1 peak | tier 2 peak |
|---|---|---|---|---|
| 8b only (today) | 512 B | 512 B | 512 B | 512 B |
| 2b + 4b + 8b | 132.1 B (25.8%) | 132.1 B | 132.2 B | 132.4 B |
| **1b + 2b + 4b + 8b (recommended)** | **92.5 B (18.1%)** | **96.2 B (18.8%)** | **104.2 B** | **110.6 B** |

1-bit is worth having: 62% of bricks have exactly 2 values. The four formats
share one decode, parameterised by a 2-bit code, so 4-bit costs no extra code.
4-bit is what keeps player-built bricks of 5–16 materials off 512 B.

## 3. Body formats and the bit layout

**Handle (A.3, CPU) and clipmap entry (A.8, GPU).**

A note on A.8 first. The shipped clipmap entry is a verbatim copy of the CPU
handle: bit [31] = 1 means dense, the same as A.3. A.8 says the opposite
polarity. Nothing reads A.8's polarity, and this amendment corrects A.8 to match
the code.

```
[31]    1 = dense | 0 = uniform (material in [7:0])            -- unchanged
[30]    Volatile (§3.10), CPU only; the GPU ignores it          -- unchanged
[29:28] FORMAT code c (new; these bits were always 0 because the index is < 2^28)
          c = 0  RAW 8-bit: 128 words of material bytes, no palette (today's body)
          c = 1  1-bit: 1 palette word (entries in bytes 0..1) + 16 index words  =  68 B
          c = 2  2-bit: 1 palette word (entries in bytes 0..3) + 32 index words  = 132 B
          c = 3  4-bit: 4 palette words (16 entries)          + 64 index words  = 272 B
[27:0]  body base, in 4-byte WORDS (was: slot index in 512-byte slots). 2^28 words = 1 GiB per pool.
```

Code 0 is RAW on purpose. Every handle that exists today (bits [29:28] = 0) is
then already a valid RAW handle once the index is read as `slot << 7`. Raw also
stays a valid format forever, which is what gives the rollback switch (§9).

**Index math. Shifts and masks only (CLAUDE.md hard invariant).**

```
c     = (h >> 28) & 3
base  = h & 0x0FFFFFFF
L     = (c + 3) & 3                    // log2 bits per voxel: c0->3 (8b), c1->0, c2->1, c3->2
hdr   = (0x4110 >> (c << 2)) & 0xF     // palette words: c0 0, c1 1, c2 1, c3 4
bit   = vIdx << L                      // vIdx = C.1 LocalVoxelIndex, 0..511
word  = data[base + hdr + (bit >> 5)]
v     = (word >> (bit & 31)) & ((1u << (1u << L)) - 1u)
mat   = (c == 0) ? v : (data[base + (v >> 2)] >> ((v & 3) << 3)) & 0xFF
```

The allocation sizes (17, 33, 68 and 128 words) are not powers of two. They do
not need to be: no address is ever masked or taken modulo a body size. Every
index computed inside a body is a shift or a mask. The power-of-two rule applies
to window and ring dimensions and is untouched.

**Rejected layout:** bodies of 64, 128, 256 and 512 B with the palette kept
somewhere else. A separate palette needs either a second index, which means a
64-bit handle and a doubled clipmap (+134 MB GPU at today's mirror), or a
parallel indirection read on every hit.

**Canonical encoding.** The generator and the downsampler build the palette in
ascending material-ID order, so air (0) is entry 0 whenever it is present. They
pick the smallest format that fits. This keeps worker output deterministic.
Bricks written by edits may be non-canonical. Nothing compares encodings: every
comparison decodes (§7).

## 4. Where the format is read

**CPU.** One static codec, `BrickBody` (`VoxelEngine.Memory`), provides
`Read(handle, raw, vIdx)`, `TryWrite`, `DecodeTo(byte[512])` and
`Encode(byte[512]) -> (code, words)`. Every consumer goes through it.
`ChunkStore.GetVoxel` keeps its signature and result: it gains a shift, a mask
and, for codes 1–3, one palette read. `ChunkFluidMask` can skip any brick whose
palette holds no mobile material, so its 512-voxel scan becomes 1–16 compares.

**GPU.** One HLSL function, `ReadBrickMaterial(buffer, handle, vIdx)`, goes in a
shared include. It replaces the eight hand-inlined
`BrickDataBuffer[(poolIdx * 128) + (vIdx >> 2)]` sites:
- Raymarch.compute lines 1096, 1169–1170 (tiers), 1484, 1719, 2456 and 2484
- RaymarchStripped.compute line 186
- FluidCA.compute line 406

**Cost of a micro-step hit (§6.3).** These counts come from the decode above
and are NOT measured:

| format | dependent reads per step |
|---|---|
| today | 1 |
| RAW | 1 |
| codes 1–3, as first implemented (T3) | 2: the index word, then the palette word |

The cached version (T7, optional) reads palette word 0 once when the ray enters
the dense brick and keeps it in a register. That holds the whole palette for
codes 1 and 2, which are 99.9% of generated bricks. The cost per step is then
the same single read as today, plus about 4 ALU ops (shift, and, sub, shift).
Code 3 needs a second palette read only at the hit, and only when the index is
4 or more.

Occupancy-only tests (shadow and AO `!= 0`) resolve the material the same way.
The palette is not air-first after edits, so they cannot test the index alone.

**LOD downsampler (§6.4, 8.9).**
- Input: `ExtractChunkTier0MaterialsInto` decodes through `BrickBody.DecodeTo`.
  The vote is unchanged.
- Output: `CascadeTierPool.WriteChunkFromDownsampled` encodes each coarse 8³
  brick with the same encoder. Cascade pools hold palette bodies too, with the
  same handle bits in the tier clipmaps.

## 5. Allocation: the contiguous-run allocator stays

`BrickDataPool`'s free list of runs exists so that one chunk's bodies upload as
one `SetData`. The 2026-08-31 measurement:
- scattered slots gave runs/slots 0.224 and ~241 driver calls per frame at p99
- contiguous chunks give one call per chunk

The design keeps that, in words:

**Tier 0, admission (`StreamManager.TransferToSharedPool`).**
- The worker encodes the chunk into its scratch pool, packed back to back from
  word 0.
- The main thread asks once for `TryAllocRange(totalWords)`. It then does ONE
  `NativeArray.Copy` of the whole packed range and rebases each dense handle's
  [27:0] field by `runBase`.
- Today that step is one 512-B copy per brick, so the main-thread work shrinks.
- The fallback when no run fits is unchanged in shape: per-brick
  `TryAllocRange(words(c))`, which reproduces today's scattered path.

**Tier 0, edits (`SetVoxel`).** A promotion allocates the new size near the
brick's own base, then frees the old body. That is today's `AllocNear` hint,
generalised to a length. The ChunkStore comment records that the hint held
runs/slots at 0.0205 over 4,000 resident-edit cycles; this generalisation keeps
it.

**Units and the valve.** The pool's unit becomes the word. The §3.6 valve's
high-water mark becomes `0.85 × capWords`, and `IsUnderPoolPressure` keeps its
meaning. Dense-brick counts stay as a diagnostic.

**Cascade pools.** Today they use single-slot `Alloc`/`Free` with a flat stack,
because the run list's O(runs) insert MEASURED 0.15–0.18 ms → 0.66–0.70 ms on the
cascade phase when it was paid per brick.
- New rule: **one range per chunk per tier**. `WriteChunkFromDownsampled` already
  writes all of a chunk's 512 + 64 coarse bricks at once. It frees the chunk's
  previous range and allocates one new one, so the O(runs) cost is paid once per
  chunk, not per brick.
- This must be measured; it is not assumed (T5's acceptance).
- A cascade pool still has no valve (`Alloc` throws). T5 adds a graceful
  fallback: when the pool is full, the coarse brick is written as its majority
  uniform material. That is a visual approximation at 0.2–0.4 m distance
  instead of a crash. This is a behaviour change to 8.9's cascade, and the owner
  must approve it with T5.

## 6. State transitions

**SetVoxel (§8.3), frozen signature, same result.**
- **Uniform brick, new material.** Today this allocates 512 B. Now it allocates
  a code-1 body (68 B) with palette {old, new} and sets one index bit.
- **Dense body, material already in the palette.** The write is one word
  read-modify-write. No allocation.
- **Dense body, material not in the palette.**
  1. If the palette has an entry that no voxel uses (found by one scan of the
     512 indices, CPU only, on this miss path only), that entry is reused.
  2. Otherwise the brick is promoted to the next code (1 → 2 → 3 → RAW): it is
     re-encoded into a new body near the old one, and the old body is freed.
  3. RAW is terminal.
- **Uniform chunk forced populated.** Unchanged.

**Coalescing (§4.5). Background, main thread, frees only.**
- Today's "all 512 bytes equal → uniform" becomes "one palette entry in use →
  uniform".
- New **demotion**: a body whose used entries fit a smaller code is re-encoded
  smaller. Hysteresis: demote only if the brick has not been written for N
  coalescer passes (N to be measured), so a brick that is being edited does not
  oscillate.
- Coalescing still can never fail or race. It only ever shrinks.

**Volatile (§3.10, 8.2).** A brick that fluid forces dense from uniform air is
born as code 1 {air, water}: 68 B instead of 512 B. That is ~7.5× less pool
pressure from a pour, which is the exact pressure the eviction-spiral fix exists
to contain. The free-at-zero rule is unchanged.

Note, found while reading the code: the A.3 Volatile bit [30] is reserved but
not implemented anywhere today. This amendment neither depends on it nor
implements it.

**Fluid churn.**
- Fluid write-ops (A.9 as shipped, `FluidGpuSimulation.FluidWriteOp`) carry
  world voxel coordinates and are applied through `SetVoxel`. They never address
  a pool slot, so they do not change.
- A water/air brick keeps both entries while water moves inside it. Moves are
  bit writes, with no promotion and no allocation.
- The fluid CA's own terrain read (FluidCA.compute line 406) moves to
  `ReadBrickMaterial`.

**§4.4 table.** Untouched. No new state, no new transition.

## 7. Frozen contracts and persistence

| contract | after this amendment |
|---|---|
| `IWorldQuery.GetVoxel` | same signature, same byte for every voxel |
| `IEditService.SetVoxel` | same signature and effect; the allocation size changes behind it |
| D.1 delta | **byte-identical format**, `formatVersion` unchanged; `DeltaCodec` decodes to 512 B on save and encodes on load |
| §4.4 transitions | unchanged |
| `ChunkContentHash` | unchanged values: it already hashes effective voxel bytes, not representation |

**Not frozen, but changed.** The A.3 and A.8 bit layouts, A.4 (the body is no
longer always 512 B) and §11.3's rows. This is a **memory-layout redesign**,
which is why it is an amendment. CLAUDE.md lists none of these as frozen public
APIs.

**Determinism and generator independence.**
- A save never contains the in-memory format. A deviating dense brick is written
  as a 512-B D.1 record, as today. So the saves do not depend on the format,
  and old saves load unchanged.
- The palette adds no dependence on the generator version. Every voxel,
  including buried ones, stays stored where it is resident. That is the property
  that made the owner reject tickets.
- The only generator dependence is the existing one in §4.1: unedited bricks
  are never saved. This amendment does not change it.

## 8. Expected savings (decimal MB, CPU and GPU each)

Sources:
- **Measured:** the brick counts (this histogram; the real-run peak of 499k
  tier-0 dense bricks at the mountain poses, KNOWN_ISSUES 2026-10-01) and the
  caps (`EngineConfig`).
- **Derived:** every MB below, from those counts × the §2 means.
- **Not measured:** the effect on phys_footprint. T4 measures it with
  `-memledger`.

| pool | cap today (bricks) | today, at cap | palette, same brick cap | today, at need | palette, at need |
|---|---|---|---|---|---|
| tier 0 | 750,000 | 384.0 | 72.2 (96.2 B mean) | 255.5 (499k) | 48.0 |
| tier 1 (tg2) | 320,000 | 163.8 | 33.3 (104.2 B) | 47.2 (92k) | 9.6 |
| tier 2 (tg2) | 96,000 | 49.2 | 10.6 (110.6 B) | 9.9 (19k) | 2.1 |
| **sum, one side** | | **597.0** | **116.1** | 312.6 | 59.7 |
| **CPU + GPU** | | **1,194** | **232** (−962) | 625 | 119 |

Pools are allocated at CAPACITY (KNOWN_ISSUES). So memory comes back only when
the caps are set in words (T4, T6). The tier-0 cap choice is the owner's (§10,
choice 1):
- **(a)** keep 750k bricks of capacity at the measured mean: 72.2 MB per side,
  saving 312 MB per side.
- **(b)** need plus today's 251k-brick headroom at the worst case of 512 B:
  48.0 + 128.5 = 176.5 MB per side, saving 207.5 MB per side.
- **(c)** need plus 251k bricks at 132 B (built bricks of ≤ 4 materials):
  81.1 MB per side, saving 303 MB per side.

The §3.6 valve still makes every option safe under abuse: edits succeed, and far
chunks evict.

**Separate observation (derived, not this amendment).** The measured band needs
92k (tier 1) and 19k (tier 2) dense coarse bricks, against tg2 caps of 320k and
96k. Those caps are 3.5–5× the need even at 512 B. They were a "FIRST SIZING
2×" (`EngineConfig.Tg2CascadeTierPoolCap`) and were never re-derived from a tg2
`PeakUsed`. Lowering them is an independent lever worth about 200 MB at 2× the band need
(CPU + GPU) that needs no new format. It needs a measured `PeakUsed` first, and
`MemoryLedger` does not print one today.

## 9. What breaks: construction sites to change (grep, 2026-10-04)

**Non-test code** with 512-B body math or the `0x3FFFFFFF` / `0x7FFFFFFF` index
masks. There are 28 mask sites and these files:
- **Memory:** `BrickDataPool`, `ChunkStore`, `Coalescer`, `ChunkFluidMask`
- **Mirror:** `TerrainClipmap`, `ClipmapValidator`, `CascadeTierPool`,
  `CascadeValidator`, `LODCascadeManager`, `LODDownsampler`
- **Streaming:** `StreamManager`, `DeltaCodec`
- **WorldGen and simulation:** `GenerateChunk`, `TerrainGenChunkSource`,
  `FluidReferenceCPU`
- **Rigs:** `Phase3AcceptanceRig`, `Phase4AcceptanceRig`
- **Shaders:** `Raymarch.compute`, `RaymarchStripped.compute`,
  `FluidCA.compute`

**Tests** that build dense handles or read `RawData` directly. These all move to
`BrickBody`:
- `LODDownsamplerTests`, `LODSharedChainTests`, `AirMipTests`,
  `AirMipPackedTests`, `OccupancyMaskTests`
- `ChunkFluidMaskTests`, `GenerationTests`, `GenerationDiagnosticTests`
- `FarBandSeamOracleTests`, `TerrainGenChunkSourceTests`
- `PaletteHistogramReport`

**Oracle windows.** The `WINDOW_CHUNKS_Y` landmine is not triggered: no window
dimension changes. Some oracle windows (`RaymarchOccupancyTests`,
`RaymarchMipTests`) build their content through `SetVoxel`; those get the new
format automatically. The ones that poke handles directly are in the list
above.

**Shaders.** `ShaderCompileCheck.CheckAll` can report a green shader that does
not build (KNOWN_ISSUES). So every shader-touching task's acceptance includes a
real player build and a rendered pose. A green compile check alone is not
enough.

## 10. Choices for the owner (recommended first)

1. **Tier-0 cap after T3** (§8): **(c) need + 251k headroom at 132 B, about
   81 MB per side** (recommended), or (a) 72 MB, or (b) 177 MB.
2. **Formats:** **all four, 1b/2b/4b/RAW** (recommended; one decode), or drop
   1-bit (+40 B per brick, about 19 MB per side at need).
3. **Cascade fallback when full** (§5): **uniform-majority coarse brick**
   (recommended), or keep throwing and size cascade caps at worst case.
4. **Ship order:** **behind `-palettebricks 1`, off by default until T3's
   evidence is read** (recommended), or default on as soon as T3 is green.

## 11. Implementation plan: one change per task

Each task ends with the fast EditMode suite (`SUITE=all` where generation
changes) and a commit, which is the rollback point. Pixel-identical means the
v4 poses (`MAPV=4 MODE=poses`) compared with task 26's `145512_world` (61 shots;
`minimap_check_*` are known noise). Every timed arm is quoted at warm steady
state (CLAUDE.md).

| task | the one change | acceptance |
|---|---|---|
| **T1** | `BrickBody` CPU codec + exhaustive tests. Not wired into anything. | Round-trip of every code on random, histogram-shaped and adversarial bricks; `TryWrite` hit, miss-reuse and promote paths; demote; encode determinism. No runtime file changes (grep). |
| **T2** | **Re-index only.** Handle [27:0] becomes a word base (`slot << 7`) and all CPU, GPU and test sites go through `BrickBody` and `ReadBrickMaterial`, but only RAW (code 0) is ever written. | EditMode `SUITE=all`; poses pixel-identical and MIRROR 0/0/0; acceptance rig PASS set unchanged (Gate C/D identity hashes unchanged); player build renders. |
| **T3** | **Palette live on tier 0** behind `-palettebricks 1`, encoded by generation and admission, with the SetVoxel promotion, coalescer demotion and `DeltaCodec` boundary. Caps unchanged. | `SUITE=all`; poses pixel-identical with the flag on and off; persistence gates (0 save failures, 0 reload hash mismatches); warm ABBA frame-time walk (flag off/on); warm stream probe (generation and admission not slower); `PeakUsed` in words logged. |
| **T4** | Tier-0 cap set in words (owner's choice 1). `MemoryLedger` prints `PeakUsed`/`InUse` per pool. | `-memledger` before/after (phys_footprint, IOAccelerator, native); mountain poses deficit 0, no LRU evictions; contiguity (runs per admitted chunk) reported. |
| **T5** | Cascade pools: palette bodies + one range per chunk per tier + full-pool fallback (owner's choice 3). | `SUITE=all` (`LODDownsampler` and `CascadeValidator` tests); poses pixel-identical; the cascade phase ms (`cascade UploadDirty`, worker cascade writes) is not slower than today (0.85 ms p50 writes). |
| **T6** | Cascade caps set in words from a measured tg2 `PeakUsed`. | `-memledger` before/after; no cascade exhaustion over the poses + walk + rig. |
| **T7** (only if T3's A/B shows a GPU cost) | The shader caches palette word 0 in a register for each dense brick it enters. | Pixel-identical; warm ABBA frame time. |

## 12. First task, ready to queue

```
# 31-T1-brickbody-codec
Requires: owner adoption of AMENDMENT_8_13_PALETTE_BRICKS.md (DRAFT until then).
Approval: <owner, date>.
Add Assets/CoreEngine/Memory/BrickBody.cs (namespace VoxelEngine.Memory): the §3 handle
bits ([29:28] code, [27:0] word base; code 0 = RAW), Read / TryWrite (hit, unused-entry
reuse, promote-needed result) / DecodeTo(byte[512]) / Encode(byte[512]) -> smallest code,
canonical ascending palette. Bitwise index math only. NOT wired into ChunkStore, pools,
streaming or shaders -- no runtime behaviour changes (show the grep).
Tests (fast, Assets/CoreEngine/Tests/BrickBodyTests.cs): round-trip every code on
(a) the 1..7-distinct shapes PaletteHistogramReport found, (b) 200k random bricks of
1..256 distinct values, (c) every single-voxel write sequence that walks a brick from
uniform through codes 1->2->3->RAW and back down by demotion; encode is deterministic
(same input, same words). Fast EditMode suite before commit.
Done when: tests green, HANDOFF names the API and the next task (T2 re-index).
Do not: change any existing file except to add the new ones.
```
