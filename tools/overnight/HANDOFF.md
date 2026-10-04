# 26c-palette-brick-design: DONE. Histogram measured; `AMENDMENT_8_13_PALETTE_BRICKS.md` written, DRAFT, NOT ADOPTED. With all four formats (1b/2b/4b/RAW), a tier-0 dense brick averages 92.5 B (18.1% of 512 B). Brick bodies at today's caps would drop from 1,194 to 232 MB (CPU+GPU; derived, not measured).

## What changed (commit below; no engine code)
- `Assets/CoreEngine/Tests/PaletteHistogramReport.cs`: new report-only test (`REPORTS=1`, `[Category("Slow")]`, Timeout 1 h; asserts nothing).
- `AMENDMENT_8_13_PALETTE_BRICKS.md`: the design.
- `AMENDMENT_8_13_PALETTE_BRICKS_histogram.txt`: a copy of the full output, because `Logs/` is gitignored.
- `tools/overnight/queue/31-memledger-pool-peaks.md`: one follow-up (diagnostics only).

## Evidence
- **Histogram command:** `REPORTS=1 tools/run-editmode-filter.sh PaletteHistogramReport`. Log `Logs/overnight/26c/hist.out` (PASS 1); output `Logs/TerrainGen/palette_histogram.txt`; 435 s.
- **Chunk set:** map v4, seed 42, the real `TerrainGenChunkSource`. Three sites, each 21×21 columns × 7 layers centred on the ground layer = 3,087 chunks: spawn (`Tg2Spawn`'s spiral), peak (summit_view), from2k (peak_from_2km_ground). The real band is terrain-aware and can sit a layer off.
- **Distinct materials per dense brick** (air counted):

  | | 1 | 2 | 3-4 | 5-16 | 17+ | dense |
  |---|---|---|---|---|---|---|
  | tier 0, all sites | 0% | 61.91% | 37.99% | 0.10% | 0% | 1,209,004 |
  | tier 0, peak | 0% | 56.10% | 43.85% | 0.05% | 0% | 473,347 |
  | tier 1 (0.2 m) | 0% | 48.78% | 50.83% | 0.39% | 0% | 240,968 |
  | tier 2 (0.4 m) | 0% | 38.42% | 60.37% | 1.20% | 0% | 49,602 |

  - No brick has more than 7 distinct values.
  - Only 34% of 2-value bricks are air + one material. Most are solid/solid boundaries, so the format must not assume air is one of the palette entries.
- **Expected savings** (decimal MB, CPU and GPU each; amendment §8):

  | | at today's cap | palette, same brick cap | at measured need (palette) |
  |---|---|---|---|
  | tier 0 (750k) | 384.0 | 72.2 | 255.5 -> 48.0 (499k) |
  | tier 1 (320k) | 163.8 | 33.3 | 47.2 -> 9.6 (92k) |
  | tier 2 (96k) | 49.2 | 10.6 | 9.9 -> 2.1 (19k) |
  | CPU+GPU total | 1,194 | 232 | 625 -> 119 |

  - The brick counts and caps are MEASURED. The MB are DERIVED. The phys_footprint change is NOT measured.
  - Memory comes back only when the caps are set in words (amendment T4 and T6).
- **Fast EditMode suite:** PASS 618 FAIL 0 SKIP 12, identical to task 26 (the new test is Slow, so it is excluded). Log `Logs/overnight/26c/suite.out`.
- No player build and no screenshots: the task needed neither. No performance claims.
- **Found while reading the code:**
  - The A.3 Volatile bit [30] is not implemented anywhere.
  - The shipped clipmap entry copies the CPU handle polarity ([31] = 1 means dense), which is the opposite of what A.8 says.
  - The tg2 cascade caps are 3.5-5× the band's measured need, even at 512 B per brick. That is a lever of ~200 MB independent of palettes; it needs a real `PeakUsed` first (task 31).

## Design summary (amendment §3-§7)
- **Handle and clipmap entry:** bits [29:28] are the format code (0 = RAW, today's body; 1/2/3 = 1/2/4-bit). Bits [27:0] are the body base in 4-byte words. Both stay 4 B.
- **Bodies:** 68 / 132 / 272 / 512 B, with the palette words in front of the indices. All decode math is shifts and masks.
- **Allocation:** each chunk still gets one contiguous run (in words). Cascade pools get one range per chunk per tier.
- **Frozen contracts unchanged:** `GetVoxel`, `SetVoxel`, D.1 (it expands to 512 B at the codec boundary) and §4.4. `ChunkContentHash` values are unchanged.
- **Persistence:** saves never contain the in-memory format.

## Owner choices (amendment §10; recommended option first)
1. Tier-0 cap after T3: (c) need + 251k headroom at 132 B, ~81 MB per side / (a) 72 MB / (b) 177 MB.
2. Formats: all four (one decode) / drop 1-bit (+40 B per brick).
3. Cascade pool full: write a uniform-majority coarse brick (changes 8.9's behaviour) / keep throwing and size at worst case.
4. Ship behind `-palettebricks 1`, off by default until T3's evidence is in / default on once green.

## Next step
- The supervisor turns the amendment into an owner question.
- Once it is adopted, queue **T1**. The ready spec is the amendment's §12: `31-T1-brickbody-codec`. Renumber it, because 31 is now taken by the memledger task. T1 is a CPU codec plus tests, not wired into anything.
- Plan after T1: T2 re-index only (RAW), T3 palette live behind the flag, T4 tier-0 cap, T5 cascades, T6 cascade caps, T7 an optional shader palette cache.
