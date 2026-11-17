# 206-bug-debug-views-player-build

Model: opus

Approval: SUPERVISOR, 2026-11-14 -- bug from task 198 (KNOWN_ISSUES entry added by 198). Owner-independent bug fix; no look change.

## The problem (task 198 HANDOFF "Second finding")
Debug views 1-14 and the debug pixel are broken in PLAYER builds: they switch to `CSMainFarShade`, which fails at run time.
Player.log: "more uavs (9) than the maximum supported (8)" and a Metal error `as_type cast from 'bool' to 'float'`
(program_source:10570/10585). The frame keeps the last good image and `DebugBuffer` reads 0, so it silently LOOKS fine.
Logs: `Logs/overnight/198/` (launches a1/a2).

## Do
1. Fix the debug path of the non-diet kernel exactly: bring it to <= 8 UAVs (which buffer can be bound differently or
   shared only on the debug path) and fix the bool -> float select. Play kernels' compiled code must not change (check the
   kernel-diet stamp / strip list from 134 and 194b): BEAUTY frames byte-identical (raw no-upscaler stills 0 px) at 2 poses.
2. Make the failure LOUD: if a kernel fails to load at run time, log an error and mark the frame (so a debug capture can
   never again silently return the last beauty image). Test where it can be tested CPU-side.
3. Proof: one launch, debug views 1-3 and the debug pixel at one pose show real debug output (not beauty, not zeros).
- `ShaderCompileCheck.CheckAll` can report a green shader that does not build (KNOWN_ISSUES): prove it in a player build.
- Fast suite once at the end. TASKS.md paragraph; update the KNOWN_ISSUES entry. No timing arms.

## End
Done with HANDOFF + one picture per working debug view.
