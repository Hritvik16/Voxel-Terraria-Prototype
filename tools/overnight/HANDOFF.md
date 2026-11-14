# 199-plan-render-steps-and-frame-budget: DONE (plan only). Owner doc + `AMENDMENT_8_17_RENDER_STEPS.md` (DRAFT, NOT ADOPTED); 7 owner questions below.
Owner doc: https://claude.ai/artifact/6JF22Hg7USmgb75F19UQgB (6 sections, a budget chart, a steps diagram, Q1-Q7).
## What changed
- `AMENDMENT_8_17_RENDER_STEPS.md` (new; repo copy of the doc), `TASKS.md` (task 199 paragraph), `tools/overnight/BUGS.md` (row 199).
  No code, no default, no Unity launch, no tests (no code). Commit: see `git log --grep '^199:'`.
## The plan in five lines
- Presets: **Full** (designed look, stronger machines, 720-1080 rows) and **Air** (M1, 30 fps; recommended: today's play picture unchanged,
  only NEW features lighter). Full is judged by a scaled estimate: very roughly 5-8x the M1's GPU for 30 fps at native 1080 (est., 193's
  163-285 ms native forest).
- Budget (Air, walk_near p50, 432 rows, 168b warm AC `Logs/costsplit_2026-11-02_095908.txt` + 146 `Logs/costsplit_2026-10-25_115751.txt`):
  today 37.4 ms vs 33.3; today's features get 27.0 ms of shares, the rest is reserves (reflections 2, NPCs 1.5, torches 1, fluids 0.5, spikes 1.3).
  Over share: sun 9.8 vs 5.0, sky 4.6 vs 2.5, shells 5.3 vs 4.0, CPU 7.1 vs 5.5, FSR 3 5.6 vs 5.0 (sun / sky / shells split in proportion
  from overlapping arms: alone 26.8, together 19.7).
- Steps: S1 Surface (writes a per-pixel hit buffer) -> S2 Sun (= C2, absorbs C1) / S3 Sky / S4 Local light / S5 Reflections (each reads the
  hit buffer) -> S6 Shade + compose (shading compiled once) -> S7 Post -> FSR 3. +40 B/px: +12.7 / +19.8 / +35.2 / +79.1 MB at 432 / 540 / 720 / 1080 rows.
  Exact per step: twin `-steps 0`, raw stills 0 px, kernel-twin test. Tasks T0 (measure) .. T8 (shine), est. T1-T5 -6..-23 ms (low confidence)
  vs -16..-25 needed.
- R1: S5 reflects about the water's wave normal, marches with S1's code, Fresnel-mixed with task 75's refraction in S6; Air +1..+3 ms in
  lake views (est.). R2: a 256-entry per-material table (roughness, F0, mirror), indexed by the hit buffer's material byte; no voxel-layout change;
  polished stone needs a new GenMat + GenToEngine mapping.
- Cinematic: have shadows, sky light, coloured torch light, grading; missing reflections, shine, clouds, bloom, sun shafts, stronger haze.
## Finding (code reading, NOT measured) -> BUGS.md row 199
Since task 175 (torch flood ON in play) play draws `CSMainFarShadeFlood` (`RaymarchFeature.cs` ~1373-1379), and diet 2 requires `!floodFarTwin`
(~1425). So play has likely lost diet 2's -6.0 / -4.5 ms (167). Sky fill mode 2 (194a, x1.35-2.5 at native, 193) is also unmeasured in play.
Today's play walk_near est. ~43-52 ms. Plan step T0 measures it; T2 (S4 out) removes the twin.
## Not done / limits
- Images could not be uploaded to the doc (no upload tool in this session): stills are cited by path with captions.
- Lay of the Land's devlog (youtube.com/@tooley1998) was not watched (no video access); its store page names no rendering techniques.
- No queue follow-up added: T0 is a performance measurement and needs the owner's approval of the design (Q2) first.
## Next step
Supervisor brings Q1-Q7 to the owner. If Q2 (a): queue T0 (performance task, warm AC, 1 warm-up + the cost split with play flags) and then T1.
## For the doc
- Result: plan written; render steps S1-S7 with a per-pixel hit buffer; budget 33.3 ms per step for the M1 Air preset; the questions below decide adoption.
- Heavy cliff view today (168b, warm AC, 432 rows): **37.2 ms p50 / 65.5 p95** vs 33.3; est. ~43-52 in play now (sky fill 2 + flood twin, unmeasured).
- Over share: sun shadows **9.8 vs 5.0 ms**, sky light **4.6 vs 2.5**, shells **5.3 vs 4.0** (`Logs/costsplit_2026-11-02_095908.txt`).
- Split memory: **+19.8 MB** at Air's 540 rows (+79 MB at native 1080). Reflections on Air: est. **+1..+3 ms** in lake views.
- Images (repo paths, not committed):
  - `Screenshots/183/on/ws_fixed_surface_water_native.png` -- the pond in the centre is a dark navy disc with no sky reflection: R1's gap.
  - `Screenshots/191/on42_wide_400m.png` -- the lakes on the left are flat blue with no mirrored mountain; the sky is a plain gradient (no clouds), with only faint haze on the far coast.
  - `Screenshots/193/forest_sf2.png` -- shade under the crowns is lit now (sky fill 2); the sky is still flat, with no bloom or sun shafts.
- Questions: Q1 what Air may drop (rec. nothing drawn today) - Q2 adopt 8.17 and start T0 -> T1 (rec. yes) - Q3 exactness: 0 px, +-1 level
  stops for you (rec.) - Q4 stronger machines = Apple Silicon first; 3 GB cap only on 8 GB machines (rec.) - Q5 reflections on Air without
  an extra shadow ray (rec.) - Q6 shine first: water, ice, metal ores (rec.) - Q7 next look: clouds + haze + bloom (rec.).
