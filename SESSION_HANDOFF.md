# Session handoff -- 2026-10-09 19:00 (supervisor; queue running 37e, then 41)

## For the owner
**Why a new session:** this supervisor chat got very long (12:35 to 19:00, a crash in the middle),
and every message re-sent all of it -- one of the biggest costs on a day that used 15% of the week.
You asked for the handoff at 18:50.

**What the previous chat covered:**
- **SVO look.** Compared Gemini's SVO shots with ours (`Screenshots/SVOLook/`). Our lava renders pale
  gold (LOOK 1's self-lit formula); our days are darker mainly because of the noon "cinematic grade"
  (owner decisions 2-4, Sep 24); you can try `EXTRA="-lookgrade 0" ./run-play.sh`. You parked the
  grade question "for now".
- **The Mac crash (hard power-off ~15:2x) was a Unity test run,** not Claude/Terminal: 37e's new test
  grew Unity to a 22.5 GB footprint (seen again at 15:53 and killed). Fixes: `tools/mem-watchdog.sh`
  (64e7c6a; started by every locked Unity run; kills Unity past 8 GB footprint or 10 GB swap and
  PAUSEs the queue), 37e re-queued to rewrite its test from scratch, small and bounded.
- **Sessions:** at most supervisor + queue worker, plus ONE short side session you ask for
  (SUPERVISOR.md step 0). Closing a Terminal tab does NOT always end its Claude -- type `/exit` first.
  The new supervisor closes the old one's window itself.
- **Gemini** (your Google plan; shell disabled because Gemini CLI 0.63.0 forces approval prompts on
  any command reusing a flag seen in earlier output, even in YOLO):
  - lava-look port on branch `gemini/lava-look` (worktree `../VT_gemini`), reviewed and fixed by a
    Claude side session (73a625b: it had changed torches/sludge too, wrote the wrong config object).
  - test-memory audit (`../VT_gemini2`, 62a5266): reviewer verdict -- missed the real 37e mechanism
    and inflated severities; some hygiene findings real (undisposed pools in LOD/cascade/fluid tests).
  - Verdict so far: OK for code-only ports from a clear reference, with review; not for analysis or
    builds yet. The "Gemini runs builds" trial (plain-word wrapper commands + APFS-cloned Library) is
    an idea only, not built.
- **Usage:** weekly 62% at Fri 18:48 (resets Thu 05:59); with the ~10% reserve ~28% is left, about
  4%/day. Plan you agreed: only 37e and 41 run, then the queue stops until you review 41; no new side
  sessions or Gemini reviews this week unless you ask.

**Running:** 37e (flat lake water) -- it hit the 5-hour limit at 17:17; retries every 20 min, the
window reset 18:40. Then 41 (merge Gemini's lava branch, compile check, before/after lava shots,
STOP_FOR_OWNER with pictures).
**Open for you:** nothing until 41's pictures; then the B10 review pack.
**Close:** this old supervisor is closed by the new one (it is the claude process whose window shows
this conversation). Also `/exit` any leftover side-session tab.

## State
- Branch `surface-rebuild`, not pushed. Runner in tmux `overnight` (IDLE_MIN=0, started 15:59).
- Queue: 37e (running; uncommitted edits in FarField.cs, TerrainGenChunkSource.cs, DetailField.cs,
  ShapeField.cs from its first session -- the retry continues from them), 41-lava-look-shots
  (Requires 37e). Blocked: `blocked/37e-lake-flat-water.HANDOFF.md` + the LEAKED test (reference
  only). Later: 37c, 38, 40. Last suite SUITE=all 654/0/17 (37d, c44381a).
- After 41: STOP the queue (it ends by itself if nothing else is queued; queue nothing new).
- Gemini worktrees: `../VT_gemini` (gemini/lava-look, merged by task 41), `../VT_gemini2`
  (gemini/test-memory-audit, report only -- never merge; delete the worktree when the owner agrees).
- Watchdog log: `Logs/overnight/watchdog.log`. Bugs list: `tools/overnight/BUGS.md` (37e memory entry).
- Handoffs of finished tasks that later tasks overwrite are copied to `tools/overnight/done/NAME.HANDOFF.md`
  (35, 37 B10, 37b). The B10 pack needs `done/37-B10-savanna-badlands.HANDOFF.md`.

## Next for the supervisor
- On each done: one Done row in the doc (insert after header row mk2vq5dkmp6.1425; 5 cells: time,
  task, result, commits, evidence) and replace "Running now" mk2vq5dkmp6.55626 (last h 96f3f861 at
  rev 408).
- **B10 review pack after 37e** (one doc section, captioned images, ONE batch of questions):
  1. Palette pick a/b/c per biome for savanna + badlands (`Screenshots/B10/candidates_noon.png`,
     `candidates_golden.png`, `palette_candidates_swatches.png`).
  2. Seed 42's diagonal biome stripes OK, or an edge-blend/boundary-warp step? (`seed42_b8_b10_biome.png`)
  3. Hard straight biome borders in the far band (`far_views_a.png`).
  4. Canyon tablelands look (`Screenshots/B5b/`; the rim poses were poor -- say so).
  5. Rename the landform "Badlands" (e.g. "breaks") vs the biome.
  6. 37e's lake pictures.
  Then ask about the v4 default + merge to `main` (SUITE=all
  first), then start the engine-phase plan doc (SUPERVISOR.md / ROADMAP.md: Q26's one compiled
  generator, E1's pass conditions from the TAA verdict, the fluid notes, the bugs list).
- Watcher race (tooling, low priority, add to BUGS.md if it recurs): when the watcher loop restarts
  in the seconds before a new task's transcript exists, it follows the OLD task's file until that
  task ends. Fix by hand: kill the watcher's python child (`ps -axo pid,ppid | awk '$2==<loop pid>'`);
  the loop restarts in 20 s.
- Images in the doc: Artifact publish (url = doc link, asset true, file_paths) -> Docs `batch` with
  `{"verb":"create","object":"blob","engine":"blob","payload":{"asset":"<id>"}}` -> `![alt](blob/<minted>)`
  plus an italic caption line below (alt text is not shown as a caption).

## Documents
- Queue decisions: https://claude.ai/code/artifact/681cc873-8163-45da-a51f-32ffca6d7468
  (doc id 681cc873-8163-45da-a51f-32ffca6d7468, body node 8460ce71-6ba5, last rev 408;
  "Waiting on you" lead mk2vq5dkmp6.27623; Q31/Q32/Q33 sections below it, all answered)
- Surface rebuild plan: https://claude.ai/code/artifact/f6222d44-1457-4a92-9460-3390d44e75fd
- Game Design Brief: https://claude.ai/code/artifact/d31b4321-77c3-4026-93d2-460c190950bf
- Terrain review: https://claude.ai/code/artifact/7443de71-6716-4f99-869b-98d3c6814fd7

## Notes
- No periodic updates; speak only when a task starts/finishes/blocks or the owner is needed.
- Unblocking a STOP_FOR_OWNER task needs the owner's answer (the auto-mode classifier refused a
  supervisor default on 9 Oct).
- A `Requires:` line must name real task files only. Name order = run order.

## Ready-to-paste prompt
> Read tools/overnight/SUPERVISOR.md and SESSION_HANDOFF.md, then supervise the overnight
> queue as described there until nothing is left that can run without me. Only one agent
> works on the project at a time; you do no engineering yourself. Put every question for me
> in the "Queue decisions" doc. No periodic status updates: tell me only when a task starts or
> finishes, blocks, or needs me. When this session gets long, hand off to a fresh one yourself.
> Start by telling me why this new session started and what the previous chat covered
> (SESSION_HANDOFF.md, "For the owner").
