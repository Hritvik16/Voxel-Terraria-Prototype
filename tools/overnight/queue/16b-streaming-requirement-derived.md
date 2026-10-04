# 16b-streaming-requirement-derived

Approval: owner, 2026-10-03 18:3x, in chat: "[The 5% rule] is a bad rule. Remove it.
How fast chunks load and unload should be mathematically derived, not set
arbitrarily like this. And we want to ensure that this metric is met."
Queued by the supervisor. This task DERIVES the requirement, measures the gap and
proposes fixes. It changes NO engine behaviour (that comes after the owner
approves a fix). Analysis + tooling + docs only.

## Why
Every B step had a relative pass line (worker chunk-gen p50 on v4 no more than 5%
worse than v3). The owner removed it. B1 (task 15) showed why it was unusable:
on the SAME build, v3's walk worker-gen p50 swings between ~200 ms and ~370 ms
run to run (207/372/213/187 ms), and v4 was 373-389 ms in 4 of 4 runs
(`Logs/overnight/b1_stream_summary.txt`). The replacement is an ABSOLUTE
requirement derived from the geometry and the player's speed.

## The goal it derives from
- CLAUDE.md "Definition of stable": load deficit 0 throughout traversal.
- In player terms: every chunk inside the voxel-detail radius (where the far band
  hands over to voxels) is generated, admitted and on the GPU BEFORE the camera is
  close enough to see that it is missing.
- Speeds to evaluate (from the game-design brief, Decided rows, and the rigs):
  walking 5 m/s (`-motion walk`), 14 m/s (the fly arm), and **late-game flight
  ~60 m/s** (brief: "Late-game flight: fast, about 60 m/s", Decided). Give the
  formulas in terms of speed v, and state the largest v the current pipeline
  sustains.
- The brief already estimates, unchecked: at 60 m/s a player crosses the 128 m
  detail band in ~2 s and needs ~700 new chunks/s against ~100 today, and says
  air-only chunks are nearly free. VERIFY or correct these with the real numbers.

## Derive (cite file:line for every constant)
1. Geometry: chunk edge in metres; CPU window extent in X/Z (`WINDOW_CHUNKS_*`) and
   the generated Y range (`MAX_GENERATED_CHUNK_Y`, the 7-layer band); the GPU
   mirror extent; the voxel-detail radius R_d; when and by how much the window
   recentres (hysteresis), since that sets the burst size.
2. Arrival rate A(v): chunks entering the window per second, straight along an
   axis AND diagonal (worst case). Split into chunks that need real generation
   vs air-only / skipped chunks (check what the pipeline actually skips).
3. Latency budget L(v): time from a chunk entering the window (being requested) to
   it being needed on screen = (prefetch margin between window edge and R_d) / v.
   Account for the recentre step: a chunk may be requested later than it entered.
4. Requirements per speed:
   - sustained generate + admit throughput >= A(v),
   - request -> on-GPU p99 <= L(v),
   - upload bytes per frame within the per-frame budget at the target frame rate,
   - a cold start / teleport fill time (state it; no target is given, report it).
5. Unload: chunks leaving the window per second (same A(v)) and what eviction costs
   per chunk, so unload is covered too.

## Measure the gap (use existing data first)
- Task 10's baseline (KNOWN_ISSUES "STREAMING BASELINE v3") and B1's 14 arms
  (`Logs/overnight/b1_arm_*.out`, `b1_stream_summary.txt`, `streamprobe_chunks.tsv`).
- Per speed, a table: required vs measured for throughput, request->on-GPU p50/p95,
  deficit, upload; the factor by which each is short; which stage is the limit.
- Capacity: workers x (1000 / worker gen ms). Report it for BOTH the fast mode
  (~200 ms) and the slow mode (~370 ms), and note that v4 hit the fast mode 0 of 4.
- At most 2 player launches, only to confirm the new report line (below) on a v3
  and a v4 walk. Check `pmset -g batt` and the owner-pause NOTE.

## Deliverables
- `KNOWN_ISSUES.md` entry "STREAMING REQUIREMENT (derived 2026-10-0x)": formulas,
  constants with file:line, the per-speed table, the gap. One index line in CLAUDE.md.
- A small pure function (e.g. `StreamingRequirement` in the streaming code, no
  runtime effect) that computes A(v), L(v) and the throughput requirement from the
  LIVE constants, plus an EditMode test that pins the formula on a hand-worked case
  (so a constant change updates the requirement instead of silently invalidating it).
- `MODE=stream ./run-tg2world.sh` prints, per arm: `REQUIRED <x>/s, MEASURED <y>/s
  -> MET / NOT MET (short by <f>x)` and the same for latency. This replaces the
  5% comparison as every B step's streaming report.
- In HANDOFF: the ranked fix candidates to MEET the requirement at 5 m/s first, then
  60 m/s, each with its expected gain and the evidence for it. Examples to examine,
  not a list to implement: why a chunk takes ~350 ms on a worker vs ~62 ms alone;
  what decides the fast vs slow mode; worker count / thread QoS; generation order by
  distance; skipping air-only chunks; for 60 m/s the brief's options (more
  throughput, a smaller detail band while flying fast, or far band at speed).
  **Implement none of them.** Start HANDOFF with `STOP_FOR_OWNER` ONLY if a
  constant turns out not to be derivable (say which and why); otherwise finish
  normally and put "Owner: approve a fix" at the top.
- Fast EditMode suite 0 FAIL.
