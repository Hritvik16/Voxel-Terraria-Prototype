# PLAN_PATH_TO_33MS.md -- what it would take to reach 33.3 ms (30 fps) at the WORST poses (task 237, 2026-11-18, PLAN ONLY)

No code, no builds, no timing in this task. Every number cites the log or task that measured it. **est.** = arithmetic on measured
numbers. **guess** = a judgement with no measurement behind it. "Unmeasured" = nobody has timed it.

Sources: `Logs/budget/2026-11-17_235256/check.txt` + its four `costsplit_*.txt` (task 235: warm, AC, play flags, High, FSR 3 at 432
rows pinned), `PERF_EXACT_CUTS.md` (task 160), `AMENDMENT_8_17_RENDER_STEPS.md`, TASKS.md 118/124/146/161/168-168e/193/210-214/219/226-233,
KNOWN_ISSUES ("sec. 6.5 ladder does NOT work as a budget mechanism"). Picture of the four poses: `Screenshots/235/sheet_worst_case.png`
(one panel per pose; look at how much of forest_eye is leaf and trunk at eye level).

## 0. Short answer

- **30 fps at forest_eye with a 16 deg sun is NOT reachable on this M1 Air with today's look.** The same forest at NOON is already
  99.2 ms (3.0x the frame, 235's `startsun_off` arm). Making the low sun free would still leave a 3x gap. The forest itself (its tree
  density, at render rows of 432) is the problem, and the low sun adds about a third on top.
- Reaching ~33 ms there would need most of: render rows down to ~216 (a 5x upscale to 1080p: soft, FSR shimmer on leaves), long
  dawn/dusk shadows cut short, no ground-bounce fill in forest shade, and lighter trees (task 210-213's forest was ~+57 % frame cost,
  cold) -- or a new sun-shadow design that is amendment-level and unproven (sec. 3, row S7).
- The other three worst poses CAN plausibly reach ~30 fps: walk_near 16 deg with exact or near-exact cuts, jungle noon and torches at
  night only if the render rows may drop below 432 when a view is heavy (Package 3).

## 1. The worst poses and their cost split (235, p50 ms; % of 33.3 ms)

| pose | FULL | sun | sky fill | torch flood | rest | p95 | noon control |
|---|---|---|---|---|---|---|---|
| forest_eye, sun 16 deg | **135.4** (407 %) | 54.7 | 25.5 | 0 (day) | 55.2 | 198.2 | 99.2 (`startsun_off`) |
| jungle_eye, noon | **70.0** (210 %) | 15.5 | -0.8 (~0) | 0 | 55.3 | 99.9 | -- |
| torch_camp, night (play = `tf_on`) | **52.7** (158 %) | 0.4 | 0.9 | 8.4 (`tf_on` - `tl_none`) | 43.0 | 76.5 | -- |
| walk_near, sun 16 deg | **35.4** (106 %) | 12.9 | -0.5 (~0) | 0 | 23.0 (shells 7.0 of it) | 86.9 | 34.1 |

Noise: walk_near's six FULL runs spread 30.3-43.7 ms (range 13.5), forest_eye 11.2, night 8.8, jungle 4.4. Arms are not additive;
"rest" = FULL - sun - sky fill - flood carries that error. Every row is at 432 render rows, FSR 3 to 1080p.

**Why the low sun costs so much (from code + measurements, not a new run):** a sun ray at 16 deg runs ~3.5x farther horizontally
before it leaves the near window than one at 78 deg. At forest_eye the sun step goes 18.2 ms (noon) -> 45.0 (16 deg) in task 233's
ABBA and 54.7 in 235. Sky fill mode 2 (play since 194a) also traces **one sun ray from each ground-bounce hit** (TASKS 193), so a low
sun can lengthen those rays too. Whether sky fill's 25.5 ms at forest is a low-sun effect or a forest effect is NOT known: sky fill
at forest at noon has never been timed, and FULL noon vs 16 deg (36.2 ms) is about the sun step's excess alone (36.5).

## 2. The "rest" (~55 ms at forest / jungle, 43 at night, 23 at walk): what is known, what is not

| piece of "rest" | ms | source | at forest / jungle |
|---|---|---|---|
| CPU + fixed floor (`tiny_gate`: 256x144, no upscaler) | 7.1 | 146, walk_near, 540 rows | **unknown**; forest and jungle keep a streaming deficit (573 / 942 chunks), so main-thread streaming work may be larger there |
| FSR 3 upscale (`ups_none`) | 5.6 | 146; fixed cost at 1080p output | same everywhere (est.) |
| Shells (`shells_off`) | 7.0 | 235 walk_near (212: 8.4) | **unknown** |
| Far band past the shells | ~0-2.4 | 146 (walk_near / walk_open) | **unknown** |
| Look passes: voxel vary, crevice, aerial haze, bloom (226); grade (233) | ? | never timed (226 was stills only; 233 timed only the sun) | **unknown** |
| Tier-0 primary march + surface shading + copies | remainder | 160 est. ~15 at walk_near (older build) | **unknown**; the biggest suspect at forest/jungle (leaf and trunk voxels at eye level) |
| Trees (not a step: they make the primary march, sun rays and sky rays all longer) | +57 % FULL | 210 / 213 cold native settle frames, forest_eye, noon, sky fill 0: old trees 163.9 -> 210 trees 222.8 -> 213 dense 257.2 ms | ratio only; never A/B'd warm at 432 rows |

**What render rows do (219, measured, pre-227 build):** at forest_eye 432 -> 540 -> 720 rows cost 155.7 -> 214.3 -> 383.9 ms, so
**67-82 %** of the forest frame scales with pixels (fixed part ~27-52 ms). At walk_near (109.8 -> 169.1) and jungle_eye (115.5 -> 187.3)
it is **~95-100 %**. Night: never measured (I assume 85 %, guess).

**The ONE cheap capture that would split "rest" (do NOT run here):** one cost split at the forest pose, the arms all exist today:
`./run-cost-split.sh` with `$PLAY_SETTINGS_FLAGS -quality high -renderrows 432 -shellwait 60 -startsun 16 -costfixed forest_eye
-costtimedpaths forest_eye -costarms tiny_gate,ups_none,shells_off,far_off,lotl_off,look233_off` (+ warm-up; ~25 min of Unity).
It splits CPU floor / FSR 3 / shells / far band / the look passes; what is left is the tier-0 primary march + shading. The tree share
needs a launch flag (`-trees2 0`, one FULL-only launch), so it cannot be an arm. If the same launch also carried
`sunres4,clamp64,sunfar64,reuse16` it would answer sec. 3's biggest unknowns (every sun lever below was timed only at NOON, where the
rays are short; at 16 deg they may finally pay) -- each extra arm adds ~1.5 min.

## 3. Every known candidate cut

ms = saving at the named pose, 432 rows. Exact = byte-identical stills (or a proven twin). Memory = GPU/CPU bytes added (+) or freed (-).

| id | cut (switch) | forest 16 | jungle noon | night | walk 16 | what the owner would SEE | memory | risk | exact? |
|---|---|---|---|---|---|---|---|---|---|
| S1 | **Low-res sun** (`-sunres 4`, task 232, built, OFF) | unmeasured at 16 deg (noon: -2.5 +-8.9, i.e. ~0) | ~0 (232: 70.5 -> 69.3, noise 27) | 0 (no sun) | unmeasured (noon -1.5) | nothing in practice; a far sub-pixel pole line loses shadow in ~1.7 % of its pixels, within 1 voxel of its edge | +2.65 MB | low (built, tested) | near-exact |
| S2 | **Sun-ray length cap at low sun** (`clampN` arm exists, task 118; would also need the 133 continuation + the bounce's sun ray capped) | **unmeasured**; ceiling = the low-sun excess: sun 54.7 -> ~18-25 (noon 18.2, 233) = **-30..-37 (guess, upper bound)** | 0 (noon rays are short; 118: no-op at noon) | 0 | -1..-3 (guess; noon 12.1 vs 12.9) | at dawn/dusk, shadows cast by anything farther than N m vanish: a hill's long shadow across a valley, distant trees no longer shading a clearing; forest floor gets more sun patches | 0 | medium: the cap length must come from the sun angle and the window, not the terrain; prove on a tall cliff + thin pole | approximate |
| S3 | **Coarse far sun** (`-sunfar`, task 228, built, OFF) | unmeasured at 16 deg (noon: -6.1) | ~-0.6 | 0 | unmeasured (noon -2) | DEFECT: brick-shaped false shadows on steep bare slopes; far forest loses sun patches (`Screenshots/228/sheets/walk_far_hill.png`) | 0 | high until the climb guard is fixed | approximate, with a known defect |
| S4 | **Temporal sun reuse 1 in 8 / 16** (`reuseN` arm, task 124) | unmeasured at 16 deg (noon: 0) | 0 | 0 | unmeasured | shadow edges lag / smear for a few frames when the camera or a shadow-caster moves | 0 | low | approximate (temporal) |
| S5 | **Sky fill mode 1 at low sun / in forest** (`-skyfill 1`) or cap the bounce's sun ray | **unmeasured**: between 0 and -25.5 (`skyfill0` = -25.5) | ~0 | ~0 | ~0 | shade under canopy and trunk undersides lose the warm ground-bounce: cooler, darker shade | 0 | low | approximate (look change) |
| S6 | **Sky fill off** (`-skyfill 0`) | **-25.5** (235) | ~0 | -0.9 | ~0 | the "pitch black under trees" look of before 193/194a returns (owner fixed it on purpose) | -2.65 MB history | low | look change |
| S7 | **Sun-space shadow map** of the near window, refreshed in slices as the sun moves (NEW DESIGN, amendment-level) | guess: sun 54.7 -> ~3-8, and the bounce's sun ray too | guess -10 | 0 | guess -8 | shadow edges at its texel size (blockier at 0.1 m voxels unless the map is large); everything else the same | +6-26 MB (guess, by size) | high: new pass, several tasks, unproven on M1; derived from the CPU-written mirror, so no invariant breaks | approximate |
| R1 | **Dyn rows floor 432 -> 360** (FSR 3 3x per axis) | est. -27..-35 (67-82 % pixel share, 0.69 px) | est. -21 | est. -14 (guess share) | est. 0 to -10 (dyn only drops when > 33 ms) | softer picture, more FSR shimmer on leaves and thin trunks, only on heavy views (the controller picks rows from frame time: data-derived) | -1 MB (smaller histories) | low (play has `dyn` today) | approximate; owner called half-res / cheaper upscaling "bad picture quality" (2026-10-31, PERF_EXACT_CUTS) |
| R2 | **Dyn rows floor 432 -> 288** (3.75x) | est. -50..-62 | est. -37 | est. -25 (guess share) | same | visibly soft; leaves shimmer; small text-like detail lost | -1.5 MB | low | approximate; same owner objection, stronger |
| T1-T5 | **8.17 structural steps** (hit buffer, sun / sky / flood steps out of the big kernel; amendment 8.17 sec. 4) | unmeasured; walk_near est. -6..-23 total (low confidence); scaling to forest guess -10..-25 | guess -5..-15 | T2 (flood out) est. -2..-6 | est. -6..-23 | nothing (each step proven 0 px) | +3-8 MB scratch | medium: 168c's first try (sun defer) was slower and not exact | exact by design |
| E | **Upload ring** (`-uploadring 1`, task 168e, built, OFF) | p50 0; p95 / p99 down (walk: p99 65.3 -> 52.9) | p95 only | p95 only | p95 only; >33 ms frames 19.1 -> 16.2 % | streaming chunks may land 1-2 frames later | +4.5 MiB staging | low | exact at rest |
| W | **Lighter trees** (`-trees2dense 0` = 210 placement; `-trees2 0` = old trees; owner chose dense in Q-AJ) | est. -17 (dense -> 210, ratio 257/223) / est. -49 (old trees, ratio 1.57) | unmeasured | 0 | unmeasured | sparser forest (210: 6.0 trees per 51.2 m square vs 9.3) / the old tree shapes | -25 % world tree bricks (210 vs dense); fewer GPU bodies, jungle refusals may drop | low technically, but it undoes an owner look decision | look change |
| L | Start the day at 25 deg instead of 16 (`-startsun 25`) | sun step 45.0 -> 35.9 (233) | 0 | 0 | ~0 | less dramatic start light | 0 | -- | **does not fix the worst case:** the day cycle passes 16 deg every dawn and dusk |
| -- | Shadow rung 1 / 2 (sec. 6.5 ladder) | ~0 | ~0 | -- | ~0 | softer/harder edges | 0 | -- | KNOWN_ISSUES: rungs 0-2 within 3 ms; only OFF recovers time. Not a lever. |
| -- | Done already, nothing left: kernel diet 2 (161, ON), roof skip (168, ~0), admission budget (168d, ~0), flood only near torches (229, ON), low-res sky fill 4x4 (227, ON), night torch memory (236, ON) | | | | | | | | |

## 4. Three packages (est. p50 ms per pose; ranges carry the guesses above)

Arms do not add exactly; I subtract the middle of each range and then apply rows as a pixel-share scale. Treat every cell as +-20 %.

| package | forest 16 deg | jungle noon | night torches | walk 16 deg | what changes on screen |
|---|---|---|---|---|---|
| today | 135 | 70 | 53 | 35 | -- |
| **P1 Keep the look** (exact / near-exact: E + S1 + 8.17 T1-T5) | **~110-125** (guess) | ~55-65 | ~40-50 | **~28-32** | nothing visible; 5-7 engine tasks |
| **P2 Cheap low sun** (P1 + S2 cap + S5 bounce cap or mode 1 at low sun) | **~65-90** (guess) | ~55-65 | ~40-50 | ~27-31 | at dawn/dusk: no long distant shadows, cooler forest shade |
| **P3 Heavy views drop rows** (P2 + dyn floor 288 when > 33 ms) | **~35-55** (est. from 219's pixel share) | **~25-30** | **~21-26** (guess share) | ~27-31 (stays at >= 432) | P2's dawn trade + a soft, shimmery picture in forests / jungle / torch-lit camps only |

- **P1** gets walk_near to ~30 fps; forest at dawn stays ~8 fps, jungle ~16, night ~22. It is the only package that keeps the look exactly.
- **P2** is the honest best "keep resolution" result: forest at dawn ~11-15 fps, i.e. about forest-at-noon after P1 (~75-90) minus what the bounce cap saves.
- **P3** gets three of four worst poses to ~30 fps. Forest at dawn ~18-28 fps.
- **30 fps at forest 16 deg** would need P2 + rows down to 216 (est. ~25-45: 33 ms only at the optimistic end), so in practice also
  sky fill off in shade (S6) and/or the 210 or old trees (W), or S7 (new sun design) in place of the cap. That is the forest losing its density, its bounce light, its long dawn shadows AND most of its
  sharpness. My recommendation: do not chase 30 there. Set the dense-forest worst case to **20 fps (50 ms)** and 30 fps everywhere else.

**Rules respected:** no terrain-specific tuning (the cap length would come from the sun angle and the window size, rows from frame
time, both proven on an adversarial case: a tall cliff's long shadow, a thin pole); efficient by construction (S1, S2, S5, R1/R2 add
0-2.65 MB; S7 is the only one that adds a pass and needs approval); no frozen API or memory-layout change in any package.

**Order if the owner picks P2 or P3:** (1) the one capture in sec. 2 with the sun arms (decides whether S1/S4 pay at low sun, which
would make S2's visible trade unnecessary); (2) S2 + S5 as one look-checked task with stills at dawn; (3) the dyn floor (P3 only);
(4) 8.17 T1-T5, one per task. E (upload ring) can flip ON anytime: exact, tail only.
