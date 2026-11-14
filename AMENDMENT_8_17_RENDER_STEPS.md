# AMENDMENT 8.17 -- RENDER STEPS AND FRAME BUDGET

**STATUS: DRAFT, NOT ADOPTED.** Task 199 (plan only, 2026-11-14). No code, no default change, no Unity launch went into it.
Owner doc: https://claude.ai/artifact/6JF22Hg7USmgb75F19UQgB (same content plus charts and the owner questions Q1-Q7).
Owner answers it builds on (chat 2026-11-13, `tools/overnight/OWNER_TOPICS.md` topic 3, plus R1 / R2):
P1 (a) the M1 Air is the MINIMUM machine; P2 (a) split the drawing into separate steps before reflections / more light;
P3 (a) a frame budget per feature; P4 the cinematic references (modded-Minecraft shader packs, Lay of the Land);
R1 (a) water reflects the sky AND the land; R2 ores / metals, ice and polished stone shine, wet rock dropped.

Numbers are from existing logs (each cited). **est.** = arithmetic, not a measurement.

## 1. Why (evidence)

`PERF_EXACT_CUTS.md` section 1: cutting iterations INSIDE `CSMainBody` (116, 93b5, 153, 137; 124's half of the sun rays) saved ~0 ms each.
Moving work OUT of the kernel (132 R1, -4.2 / -6.9 ms) or compiling OFF code out (134 R6, -7.1 / -5.5; 161/167 diet 2, -6.0 / -4.5) paid.
Task 168c (C1) failed because it shaded TWICE inside the big kernel (not exact: +-1 level in 12 / 55 px; slower).
Reflections, metal / ore shine and more light would all land in the same kernel. So the split is done once, before them.

**Finding during this plan (code reading; cost NOT measured):** since task 175 put the torch flood on in play, play draws the
flood twin `CSMainFarShadeFlood` (`RaymarchFeature.cs` ~1373-1379). Diet 2 requires `!floodFarTwin` (~1425), so play has most
likely lost diet 2's -6.0 / -4.5 ms (167). Sky fill mode 2 (194a) is also on in play; 193 measured x1.35-2.5 per frame at native
1080 (cold capture, `Screenshots/193/lookreview193.txt`). Neither is in the numbers below. Step T0 measures both.

## 2. Presets (P1)

- **Full** (the designed look, stronger machines): 720 rows to native 1080; sun traced every frame with softer edges; sky fill 2-4
  rays / px; reflections with their own sun-shadow ray; shine with mirror; bloom, clouds, haze, sun shafts, depth of field.
- **Air** (M1 8 GB, 30 fps): today's play picture unchanged (FSR 3 dyn 432-540; shells, far band, full-res sun shadows reuse 1 in 4,
  sky fill mode 2, torch flood); new features lighter (reflections without a shadow ray at the reflected point; highlight-only shine
  except ice; bloom / clouds / haze yes; no sun shafts, no DoF). Memory cap 3 GB (today's peak 3,091 MB,
  `Logs/tg2world_20261105_201249_mem_peak_breakdown.txt`).
- The owner's 2026-10-31 rejection (half-res shells, temporal shell reuse, cheaper upscaler, shells off, far shadows off) was about
  the default look when the M1 was the only target. Recommended reading: it protects today's picture on Air; it does not cover new
  features. Unclear -> owner Q1.
- Judging Full without the hardware: per-step ms on the M1 at Full settings, pixel part scaled by pixel count (native 1080 = 6.25x
  432 rows), divided by an assumed GPU ratio. 193's native-1080 forest ran 163-285 ms (cold) -> Full needs very roughly 5-8x the
  M1 for 30 fps at native 1080 (est.). Risk: a Windows / NVIDIA path is untested (UAV limits, wave intrinsics, float code-gen) (Q4).

## 3. Frame budget (P3), Air, walk_near p50 at 432 rows

Measured: `Logs/costsplit_2026-11-02_095908.txt` (168b, warm AC, diet 2): FULL 37.2 ms p50 / 65.5 p95; sun_off saves 13.4,
skyprobe_off 6.2, shells_off 7.2, all three ("ceiling") 19.7 (arms overlap: alone 26.8). CPU floor 7.1 and FSR 3 5.7 from
`Logs/costsplit_2026-10-25_115751.txt` (146, 540 rows). Walk_open FULL 20.8 ms: already inside the budget.

| step | today (ms) | share (ms) | basis of "today" |
|---|---|---|---|
| CPU + fixed floor | 7.1 | 5.5 | measured, 146 tiny_gate (540 rows) |
| Upscaler (FSR 3) | 5.6 | 5.0 | measured, 146 ups_none (fixed at 1080p output) |
| Tier-0 march + far band + shading / post | 5.0 | 5.0 | est.: ceiling 17.7 minus CPU and FSR 3 |
| Shells | 5.3 | 4.0 | est.: 7.2 alone, scaled into the joint 19.7 |
| Sun shadows | 9.8 | 5.0 | est.: 13.4 alone, scaled into the joint 19.7 |
| Sky light (old vertical probe) | 4.6 | 2.5 | est.: 6.2 alone, scaled; sky fill 2 since then, heavier |
| Reserve: reflections | -- | 2.0 | |
| Reserve: characters / NPCs | -- | 1.5 | |
| Reserve: torches at night | -- | 1.0 | 151: night torch rays +5.7 / +8.3 before the flood |
| Reserve: fluids | -- | 0.5 | |
| Reserve: spikes / pacing | -- | 1.3 | 168e's ring (flag off): p99 65.3 -> 52.9 |
| **Total** | **37.4** | **33.3** | today's features get 27.0 |

Today's play heavy view is likely ~43-52 ms (est.) after sky fill 2 and the flood twin. Full: same steps and shares as fractions of
the frame on the stronger machine.

**Rule for look tasks (from adoption):** a look task names its step. It takes its rough cost from the launch it already does: the
feature OFF and ON as two arms at the same pose, at play rows (432, FSR 3), never native 1080. HANDOFF prints
`step <name>: +a ms (cold capture, est.) vs share b ms`, and `OVER SHARE` when a > b. No extra launch, no rig. Over share does
not block the task; it is listed for the next performance milestone. After T6 the per-encoder GPU trace (`tools/capture-gpu-trace.sh`,
`tools/gpu161.py`) gives per-step ms directly.

## 4. Render steps (P2)

Today (play): shell pre-pass (`CSShellPrepassLean`, ShellPre 20 B/px) -> `CSMainFarShade*` (ONE kernel: tier-0 march, far band, sun
ray + near continuation + shell-hit sun ray + reuse, sky fill ray + history, torch flood, point lights, shading, see-through liquids,
haze, grade) -> FSR 3 -> HUD.

Target:
- **S1 Surface**: tier-0 march + shell pre-pass + far band. Writes the hit buffer (hit cell, t, material byte, face, source tier,
  flags: 16 B) + liquid surface (surface t, path length, fluid material, wave normal: 8 B), depth and motion for FSR 3.
- **S2 Sun shadow** (= C2, absorbs C1): all sun rays incl. task 133's continuation and task 80's shell-hit ray, reuse 1 in 4 +
  history. Writes sun visibility (R32F, 4 B).
- **S3 Sky light**: sky fill mode 2 ray + history. Writes sky + bounce (2 x R32F, 8 B).
- **S4 Local light**: torch flood + point lights + lava. Writes light RGB (RGBA32F, 16 B). Removes the flood kernel twin.
- **S5 Reflections** (new, R1 / R2): reflective pixels only. Writes reflection colour (RGBA16F, 8 B).
- **S6 Shade + compose**: material colour, cube light (120), sun / sky / local light, see-through liquid (75) + Fresnel mix with S5,
  haze, grade. The shading code is compiled ONCE, here.
- **S7 Post**: bloom, sun shafts (Full); then FSR 3 with today's inputs (colour, depth, motion, reactive mask) and the HUD.
- S2-S5 read only the hit buffer and write one output each; none writes terrain. Each kernel binds <= 8 UAVs (today's non-diet
  kernel needs 9 and fails in player builds, task 198).
- Stored at full float precision so S6 sees bit-identical inputs; compressing later is a look change (owner OK).

Memory (render pixels: 432 rows 331,776 / 540 518,400 / 720 921,600 / 1080 2,073,600):
- today's per-pixel set (colour 4, ShellPre 20, sun history 16, sky history 16, FSR 3 depth + motion 8, dyn rows 8) = 72 B/px:
  22.8 / 35.6 / 63.3 / 142.4 MB.
- net added (hit 24 + sun 4 + sky 8 + light 16 + reflection 8 - ShellPre 20) = +40 B/px: **+12.7 / +19.8 / +35.2 / +79.1 MB**.
  Air allocates for 540 rows: +19.8 MB (~0.6 % of 3 GB). Bandwidth ~37 MB/frame at 432 rows: est. +0.5..+1.5 ms, inside each
  task's estimate.

**Exact, proven per step (132 / 134 method):** old kernel kept as a twin behind `-steps 0`; raw no-upscaler stills 0 px at
walk_near, walk_open, cliff1, a lake shore, a night torch pose (in-place flip in one launch); a kernel-twin EditMode test of moved
calls and their arguments (as `SunDeferTests`, `KernelDiet2Tests`); a deterministic +-1-level result stops for the owner (Q3).

**Not touched:** CPU sole terrain writer; GPU mirror memory layout (clipmap, air mips, occupancy, shell bricks); the 1-byte voxel;
streaming; frozen APIs (`IWorldQuery.GetVoxel`, `IEditService.SetVoxel`, D.1 delta, section 4.4); FSR 3's inputs; the fluid sim.

**Ordered tasks (one change each, checkpoint after each; est. ms = walk_near at 432 on Air):**

| # | task | checkpoint | est. ms | confidence |
|---|---|---|---|---|
| T0 | measure today's play (perf task, warm AC): cost split with play flags, arms flood_off, skyfill 0, sun_off, shells_off, ceiling; one GPU trace | flood twin >= 3 ms -> T2 first; sky fill >= 4 ms -> T4 before T3 | 0 | measurement |
| T1 | hit buffer + S1; one kernel does lighting + shading from it | 0 px, twin test, cost split ABBA | -1..-5 | medium-low |
| T2 | S4 out (flood + point lights); flood twin gone | 0 px at night pose | -2..-6 (if T0 confirms) | medium |
| T3 | S2 sun step (C2 + C1) | 0 px at noon poses | -2..-7 | low-medium |
| T4 | S3 sky step | 0 px in shade poses | -1..-4 | low |
| T5 | S6 alone; delete dead twins (Diag, Flood, LeanSun) | 0 px; debug views work | 0..-1 | high (small) |
| T6 | per-step cost report from the GPU trace | report lists S1-S7 | 0 | high |
| T7 | S5 water reflections, sky + land (R1), look task | pictures + step cost line | +1..+3 lake views | low |
| T8 | shine table (R2) | pictures + step cost line | +0.2..+1 | medium |

Sum T1-T5 est. -6..-23 ms (low confidence) against a needed -16..-25 to bring today's features to their 27 ms of shares.

## 5. Reflections and shine (R1, R2)

- S1 already stops at the water surface and continues the bent ray (task 75). S5 reflects the view ray about the wave normal and
  marches it with S1's code (tier 0 -> shells -> far band); no hit = sky colour. Full lights the reflected point with its own sun-shadow
  ray; Air uses the sun angle + HeightMip roof test (Q5). S6 mixes refracted and reflected colour by Fresnel (Schlick, water ~0.02 at
  normal incidence -> ~1 at grazing). First version skips under-water total internal reflection and lava.
- Cost est.: water is 0-10 % of pixels on a walk, 20-40 % on a lake shore; one reflected ray ~ one primary ray (~5 ms per 100 % of
  pixels at 432, est.) -> Air +1..+3 ms; Full at native 1080 +7..+20 on the M1 (+1.5..+5 on a 4x GPU).
- Shine: a 256-entry per-material table (roughness, F0 / metal, mirror strength; ~1 KB GPU), indexed by the material byte the hit
  buffer carries. No change to the 1-byte voxel or mirror memory. Filled beside `FarField.GenToEngine`. Candidates: Water (1),
  PackedIce (22), Copper / Iron / Gold / Cobalt (65-68), Emberstone (69), MeteorOre (86), crystals (81-84), Obsidian (73), Marble (51);
  polished stone needs a NEW GenMat + GenToEngine mapping. Wet rock dropped (owner). Highlight = sun lobe in S6 from S2's
  visibility (no ray, est. +0.1..+0.3 ms); mirror via S5 only where roughness is low. Risk: sub-pixel ore specks twinkle; FSR 3's
  reactive mask (174) must cover them.

## 6. Cinematic look (P4)

Have: sun shadows (S2), sky light / AO (S3, 193), coloured block light (S4, 175), grading (S6), FSR 3 AA.
Partly: one-bounce ground light (S3), a faint aerial haze (S6).
Missing: water reflections (S5), PBR shine (S5 / S6), clouds (S6), bloom (S7), sun shafts (S7, Full), DoF (S7, Full).
References (opened 2026-11-14): https://modrinth.com/shader/bsl-shaders, https://modrinth.com/shader/complementary-reimagined,
https://www.sonicether.com/seus/, https://modrinth.com/shader/rethinking-voxels,
https://store.steampowered.com/app/2776090/Lay_of_the_Land/ (minimum GTX 1660 Ti "Low, 1080p native, 30 fps";
recommended RTX 3060 Ti "High, Hardware Raytracing Enabled, 60 fps"; devlog https://www.youtube.com/@tooley1998/, not watched),
https://www.pcgamesn.com/lay-of-the-land/minecraft-like-game-steam-release-date.

## 7. Owner questions (full text in the doc)

Q1 what Air may drop (rec. (a) nothing drawn today); Q2 adopt and start T0 -> T1 (rec. (a)); Q3 exactness rule (rec. (a) 0 px,
+-1 level stops for the owner); Q4 which stronger machines (rec. (a) Apple Silicon first; 3 GB cap only on 8 GB machines);
Q5 reflected-point lighting on Air (rec. (a) no extra shadow ray); Q6 what shines first (rec. (a) water, ice, metal ores);
Q7 next cinematic step (rec. (a) clouds + haze + bloom).
