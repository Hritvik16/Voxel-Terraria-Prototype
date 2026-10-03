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
**Owner, 2026-10-02: a status update in the terminal every 5 minutes while a task
runs.** So the wait also returns after 300 s; then run `tools/overnight/status.sh 8`
and give 2-5 plain lines (what the task did, what it is waiting on, queue changes).
On each event:
- **done NAME**: `head -30 tools/overnight/HANDOFF.md`. One line into the doc's
  "Done" table (result, commit, where the evidence is).
- **BLOCKED NAME**: read `blocked/NAME.HANDOFF.md`. Add its question to the doc:
  a specific multiple-choice question with your recommendation first and why,
  what each answer would cost, and what is waiting on it (`Requires:`). The owner
  prefers specific questions in batches over long explanations.
- **Owner answered** (in the doc or in chat): append the answer to the blocked
  task file (`## Owner answer (date)`), move it back to `queue/`, or write a new
  task file if the answer changes the task. Close the question in the doc.
- **queue runner end / nothing runnable**: queue the next work from the backlog
  below that needs no owner decision; restart the runner. If nothing is left,
  write the final summary into the doc, update `SESSION_HANDOFF.md`, stop.

## Writing a task file
One change, measured, with acceptance steps (see the existing files for the
shape). Number it after the last file in queue/done/blocked. `Requires:` line
for real dependencies only. State who approved it and when. Never queue:
terrain-generator code (no direction picked yet), TAA step 4 (shadow tuning,
not approved), switching TAA/checkerboard ON by default (owner compares first),
frozen-API or memory-layout changes, anything the owner has not approved.

## Backlog (what may be queued without asking)
- Follow-ups that finished tasks name in their handoff (QA poses, evidence
  runs, doc fixes) -- low risk, no design choices.
- Already approved and queued: 04 cliff pose A/B, 05-08 TAA steps 1-3.
- **Owner, 2026-10-02: the surface rebuild must account for performance.** Task 10
  measures the v3 streaming baseline (chunk gen ms, admit ms, CPU->GPU upload,
  time-to-visible, deficit, where the LOD hand-offs sit). Queue it BEFORE B1.
  Every B-step task file must re-run that measurement and report each stage
  against the baseline (better / worse / unchanged), next to its map metrics.
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

## Ready-to-paste prompt
> Read tools/overnight/SUPERVISOR.md and SESSION_HANDOFF.md, then supervise the
> overnight queue as described there until nothing is left that can run without
> me. Only one agent works on the project at a time; you do no engineering
> yourself. Put every question for me in the "Queue decisions" doc.
