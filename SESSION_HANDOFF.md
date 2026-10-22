# Session handoff -- 2026-10-22 (supervisor; queue STARTED on 109)

## For the owner
**Why a new session:** you asked for the supervisor to move to Sonnet automatically. A session cannot switch its own
model, so the planning session (Opus) wrote this and started a fresh supervisor with `MODEL=sonnet`. Nothing is wrong.

**What the planning chat covered (22 Oct, first chat of the new week):**
- **Where we were:** last week's limit ran out Sat 17 Oct ~18:40; 106 never ran; nothing ran Sun-Wed. ~24 tasks done
  Oct 15-17 (8.15 D1-D4/E1/C1/Q1, shells on in play, fluid fall, gameplay steps 4-10, BLD1-2, RX1).
- **Usage data (Oct 15-17, from `Logs/overnight/*.json` + transcripts):** queue workers ~59%, supervisor/interactive
  ~41% of the week. Worker cost = context re-reads 40%, cache writes 31%, output 29%; reruns ~10%; CLAUDE.md was
  ~13k tokens re-read every turn.
- **Done in that chat:** CLAUDE.md 51 -> 30 KB (per-task notes moved to `TASKS.md`; workers append there now);
  the runner reads a task file's `Model:` line; models (your call, left to the supervisor): supervisor = Sonnet,
  engine tasks = Opus, gameplay = Opus until a Sonnet trial passes. Commits `3a9018b`, `951aa36`, + this handoff.
- **Your decisions (22 Oct):** before populating the world, make performance, lighting, world gen, visuals (and fluids,
  fast-flight streaming) good; core loop (crafting etc.) after that. Order: performance first. **Stable 30 fps** (60 is
  not reachable on this Mac) = warm, play settings, walking: **p50 <= 30 ms and <= 5% of frames > 33 ms** (today: 42 ms,
  79% over). Merge `engine-phase` -> `main` first. 106 far raycast parked (no user yet). 108 kept. Crafting 107 parked.
  No playtest list: use the known bugs.

**Running:** queue worker on **109 merge to main** (Opus, whole suite first). Then **108** saved edit summaries (Opus),
then **110** frame-budget split (the Sonnet trial).
**Close:** the old planning window (the Opus one) once this session greets you.
**Waiting on you:** nothing. After 110 the supervisor writes 111 (the 30 fps plan) and asks you to pick cuts.

## State for the next supervisor
- **Models:** you run on Sonnet. Task files carry `Model:` (SUPERVISOR.md "Writing a task file"). 110 is the Sonnet
  trial: compare its `Logs/overnight/*110*.json` `total_cost_usd`, `num_turns`, rerun/blocked and report quality vs
  Opus tasks (100/103/105: $4-5, 62-82 turns, first run green). Tell the owner the result in one short paragraph.
- **Next tasks to write (in order), each one change, `Model: opus` unless purely procedural:**
  1. **111 30 fps plan** (planning, STOP_FOR_OWNER): from 110's tables, rank cuts by ms saved x visual cost, each
     with a before/after still (well lit, captioned); include rows (FSR 3 540/720/dyn), shadows, shell march
     (`later/102-shell-march-cost.md`), far band/far shading, quality preset; the frame spikes (79% > 33 ms while
     walking) and shells filling while walking (workers). Target: E4 note in `ENGINE_PHASE_PLAN.md` (2026-10-22).
  2. Then one performance fix per task, each with ONE warm measurement, owner checkpoint between (CLAUDE.md).
  3. Then the lighting amendment (engine step 20a, STOP_FOR_OWNER) with a ms budget from 111.
  4. Then world gen / visual batch: BUGS #8 river lips, straight biome borders + stripes (un-parked), `later/37c`
     highland variation (un-parked), #2 ledge crescent (`later/40`), #11 lava pool not drawn, #4 torch-through-wall
     check; then fluids (engine steps 18, 19, BUGS #3); fast-flight streaming last.
- **Record today's decisions in the "Queue decisions" doc** (one edit): Now tab running line + an Answered entry
  "2026-10-22 (chat): quality pass first, stable 30 fps target, models, merge, 106/107 parked".
- Doc: "Queue decisions" https://claude.ai/code/artifact/681cc873-8163-45da-a51f-32ffca6d7468. Container
  `{"kind":"project","id":"681cc873-8163-45da-a51f-32ffca6d7468"}`, engine "prose". Now tab body `3b5fd194-f74f` (rev 269
  at the last handoff; `read` payload `{"kind":"view","sinceRev":269}` first, never whole). Running line = block
  `mk2vq5dkmp6.121`. Done table `mk2vq5dkmp6.886`. "Waiting on you" heading `mk2vq5dkmp6.573`. Last Q: **Q63**.
  Answered tab body `4e4a30eb-dbfc` (insert at `{"kind":"root"}` side start). Task reports body `d611dfba-5116` (rev 77):
  newest report goes BEFORE heading `mk2vq5dkmp6.81830`. Update grammar: see the 2026-10-17 handoff in git
  (`git show 6d3d948:SESSION_HANDOFF.md`) -- insert/replace/delete ops, Done rows, images via asset upload + blob.
- Screenshot Archive https://claude.ai/artifact/9y7VxxX8FmsDMtVbSyFd2d, version 31; 105's shots not archived yet
  (`tools/archive/README.md`).
- Runner: restart on "queue runner end" with tasks left: `tmux kill-session -t overnight; IDLE_MIN=0
  tools/overnight/start.sh --resume`. It does NOT wait for AC; if on battery below ~25%, `touch tools/overnight/PAUSE`.
  Wait loop: background `until tail -n +N Logs/overnight/runner.log | grep -qE 'BLOCKED|done |queue runner end|FAILED|INCOMPLETE'`.
- `later/`: 102, 106, 107 (has `Model: sonnet` from the cancelled trial -- set it per the trial's result), 37c, 40,
  70/71 (old), 78, 79, 89, 90, 93b.

## Ready-to-paste prompt
> Read tools/overnight/SUPERVISOR.md and SESSION_HANDOFF.md, then supervise the overnight queue as described there until
> nothing is left that can run without me. Only one agent works on the project at a time; you do no engineering
> yourself. Put every question for me in the "Queue decisions" doc. Start by telling me why this new session started and
> what the previous chat covered (SESSION_HANDOFF.md, "For the owner").
