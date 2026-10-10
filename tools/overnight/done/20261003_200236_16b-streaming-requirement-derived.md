# 16b-streaming-requirement-derived

Approval: owner, 2026-10-03, in chat:
- 18:3x: "[The 5% rule] is a bad rule. Remove it. How fast chunks load and unload
  should be mathematically derived, not set arbitrarily like this. And we want to
  ensure that this metric is met."
- ~20:00, on a first draft that evaluated a list of speeds (5 / 14 / 60 m/s):
  "Not a good way to do this. We need to calculate the WORST CASE and go from there."
Queued by the supervisor. This task DERIVES the worst-case requirement, measures
the gap and proposes fixes. It changes NO engine behaviour (that comes after the
owner approves a fix). Analysis + tooling + docs only.

## Why
Every B step had a relative pass line (worker chunk-gen p50 on v4 no more than 5%
worse than v3). The owner removed it. B1 (task 15) showed why it was unusable:
on the SAME build, v3's walk worker-gen p50 swings between ~200 ms and ~370 ms
run to run (207/372/213/187 ms), and v4 was 373-389 ms in 4 of 4 runs
(`Logs/overnight/b1_stream_summary.txt`). The replacement is ONE absolute
requirement: the worst case the game can produce. If the engine meets the worst
case, every easier case (walking, slow flight) is met by construction.

## The goal
- CLAUDE.md "Definition of stable": load deficit 0 throughout traversal.
- In player terms: every chunk inside the voxel-detail radius R_d (where the far
  band hands over to voxels) is generated, admitted and on the GPU BEFORE the
  camera is close enough to see that it is missing -- in the worst case.

## Build the worst case (every factor at its worst; justify each from code or the brief)
- **Speed:** the fastest movement the game will have. The brief's Decided row:
  "Late-game flight: fast, about 60 m/s". If anything in code or the brief allows
  faster (fall speed, a dash, a teleport-like jump), use that and say so.
- **Heading:** the direction that brings the most new chunks into the window per
  metre (diagonal across X/Z, plus vertical motion if it adds Y layers).
- **Altitude / terrain:** skimming the ground, so NO chunk gets the air-only
  discount; every chunk in the generated Y band needs full generation. Use the
  densest real terrain (most solid / surface chunks per column on the four seeds,
  v3 and v4) and the per-chunk generation cost at its WORST observed level (p99,
  slow worker mode), not the median.
- **Timing:** the chunk enters just after a window recentre, so it waits the
  full hysteresis step before it is even requested; the recentre burst arrives at
  once.
- **Frame budget:** admission + upload must fit the main-thread share of a 60 FPS
  frame (16.7 ms) while that burst lands.
- **Cold start / teleport / respawn:** the whole window empty at once. A separate
  worst case: derive the fill time it implies and say what target it needs (none
  is set; propose one and mark it as a proposal).
- **Unload:** chunks leaving the window at the same worst rate; eviction cost per
  chunk must also fit.
The brief's own rough estimate is UNCHECKED and NOT worst-case: "at 60 m/s a player
crosses the 128 m detail band in ~2 s ... roughly 700 new chunks a second, against
about 100 today; air-only chunks are nearly free". Recompute it from the worst case.

## Derive (cite file:line for every constant)
Chunk edge in metres; CPU window extent in X/Z (`WINDOW_CHUNKS_*`) and the
generated Y range (`MAX_GENERATED_CHUNK_Y`, the 7-layer band); GPU mirror extent;
R_d; the recentre rule. From these and the worst case above:
- **A_max:** chunks entering the window per second (and per recentre burst).
- **L_min:** the latency budget, request -> on-GPU = (prefetch margin between the
  window edge and R_d, minus the recentre delay) / v_max.
- **Requirement:** sustained generate + admit throughput >= A_max; request ->
  on-GPU at its worst (p99) <= L_min; burst admission + upload within the frame
  budget; unload >= A_max. One table, each line with its formula and number.
- If the margin (window edge to R_d) is too small for ANY plausible throughput at
  v_max, say so plainly: then the worst case itself must change (a design choice
  for the owner), not just the engine speed.

## Measure the gap (existing data first)
- Task 10's baseline (KNOWN_ISSUES "STREAMING BASELINE v3") and B1's 14 arms
  (`Logs/overnight/b1_arm_*.out`, `b1_stream_summary.txt`, `streamprobe_chunks.tsv`).
- Capacity = workers x (1000 / worker gen ms) at the WORST observed gen time (slow
  mode), and also in the fast mode for reference. Note v4 hit the fast mode 0 of 4.
- One table: each requirement line vs measured capacity, the shortfall factor,
  and which stage is the limit. Also state the largest speed the pipeline sustains
  today under the same worst-case assumptions (information only, not a target).
- At most 2 player launches, only to confirm the new report line (below) on a v3
  and a v4 run. Check `pmset -g batt` first (the Mac was on battery at 18:41) and
  the owner-pause NOTE.

## Deliverables
- `KNOWN_ISSUES.md` entry "STREAMING REQUIREMENT (worst case, derived 2026-10-0x)":
  the worst case and why each factor is worst, formulas, constants with file:line,
  the requirement table, the gap. One index line in CLAUDE.md.
- A small pure function (e.g. `StreamingRequirement` in the streaming code, no
  runtime effect) that computes A_max, L_min and the throughput requirement from
  the LIVE constants, plus an EditMode test that pins the formula on a hand-worked
  case (so a constant change updates the requirement instead of silently
  invalidating it).
- `MODE=stream ./run-tg2world.sh` prints, per arm, measured capacity against the
  WORST-CASE requirement: `REQUIRED <x>/s, CAPACITY <y>/s -> MET / NOT MET (short by
  <f>x)` and the same for latency. This replaces the 5% comparison as every B
  step's streaming report.
- In HANDOFF: ranked fixes to MEET the worst case, each with expected gain and the
  evidence. Examples to examine, not a list to implement: why a chunk takes
  ~350 ms on a worker vs ~62 ms alone; what decides the fast vs slow mode; worker
  count / thread QoS; generation order by distance; and, if engine speed alone
  cannot close the gap, the levers that change the worst case itself (a larger
  prefetch margin, a smaller detail radius at speed, the far band at speed).
  **Implement none of them.** Start HANDOFF with `STOP_FOR_OWNER` ONLY if a
  constant turns out not to be derivable (say which and why); otherwise finish
  normally and put "Owner: approve a fix" at the top.
- Fast EditMode suite 0 FAIL.
