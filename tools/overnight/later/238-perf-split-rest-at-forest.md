# 238-perf-split-rest-at-forest
Model: opus
Approval: SUPERVISOR 2026-11-18, the one capture `PLAN_PATH_TO_33MS.md` sec. 2 names to split the ~55 ms "rest" at the worst pose. NEEDS AN IDLE MAC (~25-35 min of Unity, one warm-up launch first per CLAUDE.md warm-steady-state rule).
Decision table written BEFORE measuring: if the tier-0 march + shading is most of "rest" -> P1's T1-T5 steps (exact) are the next tasks; if CPU/streaming floor or shells is big at forest -> a streaming/shell task first; if the look passes (226/233) are over ~8 ms -> propose a cheaper look pass (owner decides, it changes the look); if the sun levers (`sunres4,clamp64,sunfar64,reuse16`) pay at 16 deg -> S2 gets queued. Every outcome picks a different next task, so the measurement is worth running.
## Do
1. Read `PLAN_PATH_TO_33MS.md` sec. 2-3. Run exactly the capture it names: `./run-cost-split.sh` with `$PLAY_SETTINGS_FLAGS -quality high -renderrows 432 -shellwait 60 -startsun 16 -costfixed forest_eye -costtimedpaths forest_eye -costarms tiny_gate,ups_none,shells_off,far_off,lotl_off,look233_off,sunres4,clamp64,sunfar64,reuse16` after one discarded warm-up launch; plus one FULL-only launch with `-trees2 0` for the tree share. Use `tools/unity-lock.sh`; Mac must be idle (hididle).
2. Report the split table (ms per piece, forest 16 deg), what is still unknown, the sun levers' savings AT 16 deg, and which row of the decision table applies. Memory vs switch OFF as usual.
## Rules
No code changes. Commit only the report. `## For the doc`: the split table and the chosen next step, one image (the forest_eye still).
