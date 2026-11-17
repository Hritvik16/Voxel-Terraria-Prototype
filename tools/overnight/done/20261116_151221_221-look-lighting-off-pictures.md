# 221-look-lighting-off-pictures

Model: opus

Approval: supervisor, 2026-11-16 (capture-only QA task, no design choice; owner rule "no picture yet -> QA pose first" for question Q-AN). Source: task 219's
HANDOFF (the big costs at walk_near, High, 432 rows: sky fill 2 ~40 ms, torch flood twin ~28 ms, sun shadows ~28 ms, shells ~8 ms), task 212's cost split,
`tools/play-flags.sh` (play's flags), task 219's `Logs/overnight/219/ladder.sh` and `PlaygroundCapture.CostSplit.cs` (`-costfixed`, `-costarms none`).

## Do
1. CAPTURE ONLY, no frame timing (CLAUDE.md rule: performance only in performance tasks; the ms numbers are task 219's/212's, quote them, do not re-measure).
2. One player launch (<= ~12 min incl. load, release build, play's real flags, High preset, 432 rows as 219): the same three poses as 219 (walk_near,
   forest_eye, jungle_eye, noon, sun behind camera, same still-pose settings) and one NIGHT / torch pose where the torch light matters (reuse task 172 or
   164's torch pose). Arms per pose, as the capture tool allows inside one launch (else two launches, say so): A play defaults; B sky fill 0
   (`-skyfill 0`); C torch flood off (`-torchflood 0`); D sun shadows off (`-shadowlevel 0`, check play-flags for the real switch); E B+C+D all off.
3. Per image-pair: mean |diff| and % pixels differing > 32 against A (same method as 219's `diff.txt`), plus the noise floor from 219 (Ultra vs repeat).
4. LOOK at every image. Build ONE doc-ready sheet per pose (rows = arms, columns = poses, full-size files kept) and write in plain words what each switch
   loses (e.g. dark shade under trees, no soft sky light in shadow, flat torch circle, no cast shadows). Say which is the cheapest look loss per ms
   saved using 219/212's ms numbers, as a short table (arm, ms saved at walk_near from 212, what you lose). Do NOT pick a winner for the owner.
5. No engine code. Fast suite only if code changed (none expected).

## End
Done with HANDOFF + `## For the doc`: the table, the sheets with captions (what to look at), the plain-words list.
