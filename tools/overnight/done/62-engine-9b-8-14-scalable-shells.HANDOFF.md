STOP_FOR_OWNER -- Amendment 8.14 draft revised: shells now scale to the hardware through a quality preset. Five new questions ("all (a)" approves):
1. Q7. Presets: (a) four, Low / Medium / High (= the M1, pinned by every rig) / Ultra, one menu row (rec.; 4 layouts to test, 3 checkable here only for memory/correctness); (b) three, drop Medium; (c) four + a separate "View distance" (shell count) row.
2. Q8. First-launch pick: (a) lowest of memory / GPU / CPU tiers from `SystemInfo`, thresholds in draft §H.3, no benchmark (rec.; GPU-name rules need upkeep); (b) RAM only (sends weak-iGPU 16 GB laptops to Ultra); (c) (a) + a 10 s benchmark (warm-chip skew up to 2.2x, est. size M).
3. Q9. Changing it in F6: (a) applies on next launch, row says "(restart)" (rec.; no new teardown path); (b) live teardown + re-prime (est. size M); (c) shell count live, rest on restart.
4. Q10. Preset also sets FSR 3 rows: (a) yes as a default, top = sizing rows, bottom = 3/4 (High = today's 540-720), "Render rows" still overrides (rec.); (b) no, 540-720 everywhere; (c) locked together.
5. Q11. Render range changing on its own while playing: (a) no, menu setting; FSR 3 dyn holds frame rate (rec.); (b) yes, drop outer shells under load; (c) decide after step 23.

# 62-engine-9b-8-14-scalable-shells: DONE (planning only). Draft revised and committed; needs the owner's answers.
## What changed (branch `engine-phase`)
- 7e39aad: `AMENDMENT_8_14_LOD_SHELLS_DRAFT.md` only. New section **H. Scaling to the hardware** right after "For the owner":
  H.1 survey (Minecraft, Distant Horizons, Unreal scalability/`r.ViewDistanceScale`/HLOD, Unity, how defaults are picked; labelled "from general knowledge, not looked up") + what we copy;
  H.2 preset table; H.3 auto-pick thresholds; H.4 F6 / restart; H.5 frame rate vs presets; H.6 invariants on every preset.
  (Named "H", not "0", so it can't be confused with the architecture's §0.2 that the draft cites for the upload cap.)
- Contract row 1 rewritten (runtime `ShellLayout.From(preset)`, pools/directories allocated at start-up, shader constants not `#define`s); rows 6/7/9/11/15 note per-preset effects; NEW rows 16 (quality preset in `PlaySettings`, `-quality`) and 17 (`QualityAuto.Pick`, fixture tests).
- Owner's answers recorded: Q1 replaced by presets (M1 = 720 sizing = High), Q2-Q6 (a). §2a/§3 relabelled as the High (M1) headline; §3 adds per-preset totals; §6 adds "only the M1 exists here" and "wrong pick" risks.
## Evidence
- Planning only: no code, Unity, builds or rigs. EditMode not run (no code changed).
- Every preset number is (est.), computed with the draft's own §2 method (ring = ±ceil(c/128), hollow minus ±(floor(c/256)-1), densities 3.36/2.93/2.33/2.0 from `Logs/overnight/55_pool_ledger.out`, caps peak x 1.25). The method reproduces the draft's existing 540/720 tables exactly (cols 112/160, flux 86/99, shell 0 305/364 MB).
- Cross-check: Ultra's shell 0 footprint (21x21 columns) is exactly today's window, whose measured peak is 379,527 bricks.
- "Rest of process" 1,483 MB = the draft's ~2,338 MB at poses minus High's 855 MB terrain (tasks 53/55); held constant across presets.
## Unfinished / notes
- `SystemInfo.graphicsMemorySize` on Apple silicon has never been logged here (only printed by RaymarchAutoBenchmark/CaptureRig); the draft says the first build of the step prints it.
- Low cannot go much below ~2.0 GB with this amendment alone (1,483 MB isn't terrain); the draft names the fluid-pool and shell-0 two-level-directory cuts but does not propose them.
## Next step
- Owner answers Q7-Q11 (or "all (a)"); then the supervisor publishes the draft and the plan's step 10 (coarse-first generation) starts.

## For the doc
Task 62: the 8.14 draft now sizes the shells per machine. Nothing built; every number (est.) from the draft's own formulas.
| preset | sizing rows (FSR 3 dyn) | shells / 3D range | 0.1 m radius | terrain MB (today 1,322) | process | full detail keeps up to |
|---|---|---|---|---|---|---|
| Low | 480 (360-480) | 6 / 1.5 km | 59 m | 526 | ~2.0 GB | 5-8 m/s on its class |
| Medium | 540 (405-540) | 7 / 3.3 km | 65 m | 684 | ~2.2 GB | 8-12 m/s |
| **High = M1 8 GB** | 720 (540-720) | 8 / 6.6 km | 82 m | 855 | ~2.3 GB (< 3 GB cap) | 7-11 m/s (M1 capacity, task 28) |
| Ultra | 1080 (810-1080) | 7 / 6.6 km | 116 m | 1,497 | ~3.0 GB (16 GB+ machines) | 9-14 m/s on its class |
- Beyond the last shell the far band draws (Q6 (a)). Rigs pin High. Range is cheap (a shell is ~10-120 MB); shell 0 (255-718 MB) is what the presets really trade.
- Auto-pick = lowest of memory / GPU / CPU tiers (M1 Air 8 GB -> High; Intel iGPU Mac -> Low; M2 Pro 16 GB -> Ultra).
- Images: none -- a planning revision with nothing rendered.
- Questions: Q7-Q11 above.
