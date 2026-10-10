# Queue supervisor -- what the interactive session does while the queue runs

Owner, 2026-10-02: work through everything that can be done without the owner;
when a task needs the owner, write it up in a document and let another task
take over; ONE agent active on the project at any time.

## Roles
- **Task sessions** do the work: `tools/overnight/run-queue.sh` (tmux session
  `overnight`) runs one headless session per file in `tools/overnight/queue/`.
  A task that stops for the owner, fails or ends without a handoff is moved to
  `tools/overnight/blocked/` (question in `NAME.HANDOFF.md`, uncommitted edits in
  `NAME.patch`) and the runner continues with tasks that do not `Requires:` it.
- **You, the supervisor**, do NO engineering yourself. The running task session
  is the one active agent. You keep the queue fed, keep the runner alive, turn
  blocked tasks into owner questions, and turn the owner's answers into tasks.

## What you may touch while a task runs
- Add or edit files in `tools/overnight/queue/` (the runner reads a task file
  only when it starts it) and move files between `blocked/` and `queue/`.
- `SESSION_HANDOFF.md` and `tools/overnight/*.md`: commit them BY PATH right
  after writing (`git commit <paths>`), never `git add -A`. An uncommitted edit
  can be reverted by the runner if the running task blocks.
- Claude Docs (the owner-facing documents). Nothing else: no source, no Unity,
  no builds, no rigs.

## Start-up (cheap: tails and heads, not whole files)
0. **First message to the owner (owner, 2026-10-08):** relay SESSION_HANDOFF.md's
   `## For the owner` section in plain words -- why this new session started, a short
   summary of the previous chat (asked / decided / changed), what is running, what is
   open -- and name the old session's tmux window to close. Then do the steps below.
   **At most TWO Claude sessions (owner, 2026-10-09, after a 17 GB memory blow-up that
   forced a hard power-off with 7 stale supervisor sessions alive):** this supervisor and
   the queue's headless worker. The task-watcher window (`watch-task.sh`) is not a session.
   Once your first message is out, close the OLD supervisor yourself
   (`tmux kill-window -t <its window>`; it is idle after its handoff), and close any
   other stale Claude windows you find. Never leave two supervisors running.
   Exception (owner, 2026-10-09): ONE short-lived side session the owner asks for (e.g. reviewing
   Gemini's branch, `tools/overnight/REVIEW_GEMINI.md`) may run beside them. It never runs Unity,
   talks to the owner directly, and is closed when done. The supervisor stays on supervision
   and design discussion.
1. `tail -20 Logs/overnight/runner.log`; `ls tools/overnight/queue tools/overnight/blocked`;
   `tmux ls`; is a `run-queue.sh` process alive (`ps -axo pid,command | grep '[r]un-queue'`)?
2. If the runner is not alive, the queue has runnable tasks and there is no
   `tools/overnight/PAUSE`: `tmux kill-session -t overnight 2>/dev/null;
   IDLE_MIN=0 tools/overnight/start.sh --resume` (IDLE_MIN=0 while the owner
   says they are working beside it; the default 15 waits for an idle Mac).
3. Read the owner decisions doc (below) for answers given since the last session.

## The loop
Wait with ONE background command that returns when `runner.log` gains a line
matching `BLOCKED|done |queue runner end|FAILED|INCOMPLETE` (until-loop, sleep 60,
`run_in_background`, timeout 7200000; re-arm on timeout). Do not poll otherwise.
**Owner, 2026-10-04 (replaces the 5-minute rule): NO periodic status updates** -- each
turn resends the whole conversation and drains the weekly allowance. Speak only when a
task starts or finishes, blocks, or needs the owner. The owner watches the running
worker in a Terminal tab (a loop of `tools/overnight/watch-task.sh` that moves to each
new task; open it with osascript at session start if it is not already open).
**Owner, 2026-10-08: ALWAYS keep a Terminal tab open on the running task.** Check at session
start AND at every event (a done/blocked line means the next task is starting):
`ps -axo tty,command | grep -c '^ttys.*[w]atch-task.sh; sleep'` -- 0 means open one:
`osascript -e "tell application \"Terminal\" to do script \"cd '<repo>' && exec bash -c 'while true; do tools/overnight/watch-task.sh; sleep 20; done'\""`
(the `bash -c` keeps the loop visible to ps; the script itself execs python3, so grepping for
`watch-task` alone matches tool shells, not the tab). One tab follows every task in turn.
**Keep supervisor sessions short:** after ~3-4 finished tasks, or whenever the session
feels long, write SESSION_HANDOFF.md, commit it, start the successor with
`tools/new-session.sh`, and tell the owner to switch to it and close the old one.
On each event:
- **done NAME**: read HANDOFF.md's `## For the doc` block. Owner, 2026-10-09: EVERY finished task
  gets a short report section in the doc's "Task reports" tab (newest first): one-line result,
  the before -> after numbers, and its 1-3 images uploaded and captioned (look at each first).
  Plus one row in the Done table on the "Now" tab. Keep the "Now" tab short (running, waiting
  on you, next up); move closed questions to "Answered" (owner: the doc got hard to navigate).
- **BLOCKED NAME**: read `blocked/NAME.HANDOFF.md`. Add its question to the doc:
  a specific multiple-choice question with your recommendation first and why,
  what each answer would cost, and what is waiting on it (`Requires:`). The owner
  prefers specific questions in batches over long explanations.
  **Recommended option is the default (owner, 2026-10-08):** for REVERSIBLE
  engineering choices (sizes, task order, measurement method, which fix of several)
  record the question and your recommendation in the doc as "Going ahead with (a)
  unless you object", and requeue at once. Still ASK and wait for: look and feel,
  game design, architecture/frozen-API/memory-layout changes, anything on `main`,
  anything hard to undo.
- **Owner answered** (in the doc or in chat): append the answer to the blocked
  task file (`## Owner answer (date)`), move it back to `queue/`, or write a new
  task file if the answer changes the task. Close the question in the doc.
- **queue runner end / nothing runnable**: queue the next work from the backlog
  below that needs no owner decision; restart the runner. If nothing is left,
  write the final summary into the doc, update `SESSION_HANDOFF.md`, stop.

## Usage budget (owner, 2026-10-09)
$20 Pro plan; NEVER suggest paid options (credits, upgrades). The WEEKLY limit (resets Thu 06:00 PT)
binds, not the 5-hour window. The supervisor paces the queue itself: keep ~10% of the week in
reserve for the owner's questions, review packs and a blocker fix. The owner cannot tell when to
send a usage screenshot (Claude app > Usage): ASK for one at each phase boundary and before
queueing a big task (B5/B10-sized, ~20-40M tokens) late in the week. Calibration: 47% at Fri
2026-10-09 10:17 (~28 h into the week); ~3/4 workers, ~1/4 supervisors. Supervisor economy: one doc
edit per event, image reads only for review packs, hand off early.

## Bugs list (owner, 2026-10-09)
A bug that does not block the current phase goes into `tools/overnight/BUGS.md` (one row), not
into `queue/`; a ready task file for it waits in `tools/overnight/later/`. Only a blocker is
queued at once. Bugs are fixed in a batch next to related work (e.g. inside the engine phase).

## Writing a task file
One change, with acceptance steps (see the existing files for the
shape). Number it after the last file in queue/done/blocked. `Requires:` line
for real dependencies only. State who approved it and when. Never queue:
terrain-generator code outside the approved B steps, TAA step 4 (shadow tuning,
not approved), switching TAA/checkerboard ON by default (owner compares first),
frozen-API or memory-layout changes, anything the owner has not approved.

## Backlog (what may be queued without asking)
- Follow-ups that finished tasks name in their handoff (QA poses, doc fixes, small
  fixes) -- low risk, no design choices. NOT measure-only tasks (owner, 2026-10-08).
- Already approved and queued: 04 cliff pose A/B, 05-08 TAA steps 1-3.
- **Owner, 2026-10-02: the surface rebuild must account for performance.** Task 10
  measures the v3 streaming baseline (chunk gen ms, admit ms, CPU->GPU upload,
  time-to-visible, deficit, where the LOD hand-offs sit). Queue it BEFORE B1.
  **SUPERSEDED 2026-10-08 (owner): B steps carry NO streaming/perf measurement**
  (PREAMBLE rule 10). Performance is checked ONCE at the milestone after B10.
  **Owner, 2026-10-03 18:3x: the 5% relative pass line (Q8 (a)) is REMOVED** --
  "a bad rule ... how fast chunks load and unload should be mathematically derived,
  not set arbitrarily ... and we want to ensure that this metric is met." Task 16b
  derives ONE absolute WORST-CASE requirement (owner: "calculate the worst case and go from there"; deficit 0 inside the voxel-detail radius at the fastest movement, worst heading, ground-skimming, worst gen cost, worst recentre timing; was: at
  5 m/s, 14 m/s and the brief's 60 m/s flight) and the gap. (B steps no longer report
  against it, 2026-10-08.)
  MEETING it is its own track: after 16b, turn its ranked fixes into an owner
  question, then queue the approved fix one change per task (CLAUDE.md measurement
  rules). The worker-count / QoS sweep belongs to that track now, not to B9.
- **Owner, 2026-10-03: no day/night windows; a pause toggle instead (task 14).**
  When the owner says "pause" / "resume" in chat, run `tools/heavy.sh pause` /
  `resume`. Owner correction 2026-10-03 (task 14b): pause = DRAIN -- whatever is
  running finishes naturally, nothing new heavy starts (builds, tests, players)
  until resume. Never freeze or kill. Tell the owner when `status` says idle.
  The task agent keeps running. Heavy work may run at any time otherwise. IDLE_MIN=0.
- **Surface rebuild order (approved 2026-10-02/03), one B step per task:**
  B1 (15) -> B2 (17) -> B3 (29; owner: "still natural?") -> B4 EASY AREAS ONLY -> B5 ->
  B6 (record only, no streaming check needed) -> B8 -> B9 -> B10 (savanna + badlands;
  palette sign-off). Specs: the rebuild plan doc sections 1, 3 and 4 plus its section 8
  answers. Copy the numbers INTO the task file: task sessions cannot read claude.ai docs.
  **Owner, 2026-10-04: NO placement until the terrain overhaul is done.** Do NOT queue B7
  (towns/villages), B4's start-position half, or any object placement. After B10 those need
  a proper planning round: ask the owner a batch of specific questions (AskUserQuestion /
  the doc) to build context first, then a plan doc for approval -- not a one-line question.
  B4 (easy areas as terrain), B6 (danger map in the record) and B8 (routes) DO stay in the
  terrain overhaul (owner, same day).
- **`ROADMAP.md` (owner, 2026-10-08, later) overrides the roadmap below where they differ:** after
  B10 + merge comes an engine capability + performance phase (plan doc first), and only then the
  player and placement. Do not queue backlog items E1-E10 without an approved plan.
  **The engine-phase plan doc must include (owner, 2026-10-08, Q26 (a)):** ONE compiled
  world-map generator path (e.g. Burst in both Editor and player), so the EditMode map tests,
  PlayMetricsReport and review renders describe the world the IL2CPP player builds (task 32c:
  seed 42 v4 Height FNV differs; KNOWN_ISSUES "Burst vs the EDITOR's Mono"). Accepted until then.
- **Owner, 2026-10-08: features over measurement. Roadmap:** 29 -> 30 -> UploadDirty box
  check (Q23-2) -> B4..B10 back to back -> owner review packs AFTER B5 and AFTER B10 (no
  per-step "still natural?" wait; a step whose own renders look clearly broken still stops)
  -> v4 default + merge to main + ONE milestone perf check -> movement-abilities question
  batch (owner wants very fast movement; get a top speed) -> coarse-first step 1 ->
  placement planning round. Deferred: 0.4 m tier evidence + palette bricks (together, after
  placement). Parked: rig bisect. A review pack = one doc section of captioned renders
  (B2 vs current, per-class slope table), asked as one batch of questions.
- **Owner, 2026-10-02: task 11 split the EditMode suite (fast / `SUITE=all`).**
  Done 2026-10-03: PREAMBLE.md rule 3 says fast suite in every task, `SUITE=all`
  in every B step (generator change) and before any merge to `main`. B-step task
  files should still say `SUITE=all` explicitly.
- Planning without code is allowed as a task that writes a Claude Doc (say so
  explicitly in the task file; the preamble otherwise forbids docs): e.g. the
  surface-rebuild plan ("direction B" of the terrain review) against the
  game-design brief's Decided rows, for the owner to approve.

## The owner decisions doc
One Claude Doc titled "Queue decisions", reused across sessions (its link goes
into SESSION_HANDOFF.md). Sections: Waiting on you (open questions, newest
first), Done (one row per task), Answered (closed questions). Write findings in
the doc, not in chat; tell the owner in one line when a new question lands.

## Images in reports (owner, 2026-10-02)
The owner wants pictures, not only numbers. Every task file that changes what is
on screen or on the map asks for screenshots or map renders and their paths in
HANDOFF. In the doc, a Done row names the key image paths, and the final summary
embeds the few images that show the result (Claude Docs uploads: `guide`
topic.uploads; look at each image before placing it).

## Session economy
Every wake re-sends this session's history. When the size hook says so, or after
~10 events: update `SESSION_HANDOFF.md` (state, doc link, the prompt below),
commit it by path, start a fresh session with `tools/new-session.sh`, and stop.
The handoff STARTS with `## For the owner`: why you are handing off, a summary of
this chat (what the owner asked, what was decided, what changed + commits), what is
running, what is open. Tell the owner in chat which tmux window is the new session
and which (this one) to close.

## Ready-to-paste prompt
> Read tools/overnight/SUPERVISOR.md and SESSION_HANDOFF.md, then supervise the
> overnight queue as described there until nothing is left that can run without
> me. Only one agent works on the project at a time; you do no engineering
> yourself. Put every question for me in the "Queue decisions" doc. Start by
> telling me why this new session started and what the previous chat covered
> (SESSION_HANDOFF.md, "For the owner").
