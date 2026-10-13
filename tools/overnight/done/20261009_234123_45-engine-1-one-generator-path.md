# 45-engine-1-one-generator-path


Approval: owner, 2026-10-09 ~23:00 -- engine phase plan approved (Q36 "a"; plan answers Q1 a, Q2 a,
Q5 a, Q6 a, Q7 a, Q8 a; Q3/Q4 open, not needed here). This is ENGINE PLAN STEP 1
(`ENGINE_PHASE_PLAN.md` section 3, row 1). Queued by the supervisor.

Branch: create `engine-phase` from the current `surface-rebuild` HEAD (`git checkout -b engine-phase`)
and do all engine-phase work there. Do NOT touch `main`.

## Goal
ONE compiled world-map generator path: Burst in both the Editor and the IL2CPP player, compiled
synchronously, so the EditMode map tests, `PlayMetricsReport` and review renders describe the world
the player builds (owner Q26 (a), 2026-10-08). Today they differ: task 32c found seed 42 v4
`WorldMap.Height` FNV differs between the Editor (Mono float code) and the player. The player's hash
is `B387F054A29997D4` (task 32c). See KNOWN_ISSUES "Burst vs the EDITOR's Mono are NOT
byte-identical for float code".

## Steps
1. FIRST COMMIT: `git merge --no-ff gemini/lava-torch-light` (bd60a30, lava ignores torch light,
   owner Q34 3a). Review only the code part; its SVO-notes line numbers are known to be wrong (ignore
   them). Compile-check: fast suite + the player build log has no `Shader error` in the Raymarch
   kernels (ShaderCompileCheck alone is not enough, KNOWN_ISSUES). Reshoot the lava pose 4
   ("torches AND lava") from `Screenshots/LavaLight/lavalight.txt` and confirm lava looks unchanged
   under the torches; save to `Screenshots/Engine01/lava_torch_after.png`.
2. Make the world-map generator (the code that builds `WorldMap`, its Height/biome/landform layers)
   run as Burst-compiled jobs in BOTH Editor and player, with synchronous compilation in the Editor
   (`[BurstCompile(CompileSynchronously = true)]` or equivalent), and NO managed fallback path that
   can silently produce a different world. If a piece cannot be Bursted, say which and why in HANDOFF.
3. New EditMode test: Editor seed 42 v4 `WorldMap.Height` FNV == `B387F054A29997D4`. If the player's
   hash itself changes because the code path changed, rebuild the player, record the new player hash
   from the player log, and pin THAT -- the test must assert Editor == player, by evidence.
4. `SUITE=all ./run-editmode-tests.sh` green. List every re-pinned hash/golden value in HANDOFF
   (old -> new, and why it moved).
5. Regenerate the metrics report (`PlayMetricsReport`) and the review renders for seed 42 v4;
   note any metric that moved by more than 1% (expected: small shifts from Mono -> Burst float).

## Report for the owner (put in HANDOFF under `## For the doc`)
- Editor vs player Height FNV, before and after (the core result).
- Suite counts; number of re-pinned hashes.
- 3-5 headline metrics from PlayMetricsReport, before -> after (slope median, % mountainous, % open,
  lake count or similar), each with the delta.
- Images: one review map render before/after pair (side by side, captioned: what to look for), and the
  lava-under-torches shot. If nothing visible changed, say "no visible change" with the pair as proof.

No performance measurement (PREAMBLE rule 10).
