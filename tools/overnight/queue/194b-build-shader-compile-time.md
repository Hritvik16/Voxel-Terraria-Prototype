# 194b-build-shader-compile-time

Model: opus

Approval: OWNER, 2026-11-13 (chat): "Yes this next to speed up development" -- cut the player build's shader compile time.

## The problem
Since task 193 grew `Assets/CoreEngine/Rendering/Raymarch.compute`, a player build spends ~30 min compiling it:
7 kernels compile in parallel, each 13-17+ min of CPU (`Logs/shadercompiler-UnityShaderCompiler-*.log`, build at
11:18-11:52 on 13 Nov: 4 kernels done at 11:43, 3 more at ~11:52). 194's first build hit the known
`CSMainFarShadeDiag` compiler TIMEOUT (as in task 134) and needed a retry at a 60-min timeout
(`Logs/overnight/194/build_timeout1.log`). Every task that touches the shader pays this again.

## Do
1. Measure first, from the logs you already have and ONE forced rebuild if needed: per-kernel compile time
   (which kernels, how many variants each, which ones dominate). Report it as a table.
2. Cut it WITHOUT changing what the game draws or how fast it runs. In this order of preference:
   - kernels the PLAYER never dispatches (diagnostic `*Diag` kernels, capture/test-only kernels): keep them out of
     player builds (a separate .compute file used only where they're needed, or a define the build sets) -- the
     captures that use them must still work;
   - unused variants / keywords (`multi_compile` that could be `shader_feature` or stripped);
   - splitting the file so an edit to one part doesn't recompile everything, if Unity's cache allows it here
     (check: an edit to a shared include invalidates every file that includes it -- say what you found);
   - anything else that is pure build plumbing.
   Code changes that could change the GPU's runtime (`[unroll]` -> `[loop]`, restructuring a march loop) are NOT
   allowed in this task: list them in HANDOFF with their estimated compile saving; the owner decides later.
3. Prove nothing changed on screen: the same 3-4 capture poses (one with `-skyfill 2`, one with trees ON) before vs
   after, pixel diff 0 (or explain each differing pixel). No frame-time arms (rule 10).
4. Report build compile time before -> after (wall time and per-kernel), and a cold rebuild after editing
   Raymarch.compute vs after editing only an unrelated file.
- Fast suite before the commit. TASKS.md paragraph. If a build helper script's timeout should change, say why.
- Mind the lock and the launch budget; forced rebuilds are long, keep them to the minimum that proves the cut.

## End
Done with HANDOFF (compile-time table, pixel-diff proof). STOP_FOR_OWNER only if no cut is possible without a
runtime-affecting change.
