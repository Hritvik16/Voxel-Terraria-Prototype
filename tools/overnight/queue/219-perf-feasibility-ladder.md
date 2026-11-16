# 219-perf-feasibility-ladder

Model: opus
Requires: 218b-bug-gpu-view-eviction-proof

Approval: OWNER, 2026-11-16 (chat): "write the feasibility question and queue it"; then: "first shouldn't we go down the presets and see which one
works/not? Ensuring the picture quality doesn't change too much and you tell me the best setting, what works on this mac, and what the highest settings
would need?" A PERFORMANCE task: timed arms allowed (CLAUDE.md measurement rules: ONE warm-up launch, discarded, then arms back to back, warm steady
state, release standalone player, never the Editor). Source: task 212 (play 106.8 ms at walk_near vs rig p50 23.7 ms), `PERF_EXACT_CUTS.md`,
`tools/play-flags.sh` (play's real flags), `MODE=playwalk ./run-tg2world.sh`, AMENDMENT_8_14 (presets Low / Medium / High / Ultra, auto-pick, FSR 3 rows).

## Do
1. Before measuring, write in HANDOFF what each result makes us do: no setting reaches 30 fps -> owner question (cut features / lower target / stronger
   machine); the lowest passes -> it is the floor, question is its look; a middle one passes -> that is the answer for this Mac.
2. WALK DOWN THE LADDER, all four presets (Ultra, High, Medium, Low), play's real flags otherwise (trees, shells, torch flood, FSR 3 dyn), plus at the
   best-looking preset that fails, one arm with `-renderrows 540` and one with `-renderrows 720` (the FSR 3 row ranges) to see whether render
   resolution alone gets it there. Poses: walk_near (212's), a dense-forest pose and a jungle pose (use 218's fix, or `-trees2 0` and say so). Reuse
   212's script; change no engine code. Launches <= ~10 min each, 3 poses per launch.
3. Per arm report: frame p50, p99, % of frames over 33 ms, process peak MB, GPU refusals. "Works" = p50 <= 33.3 ms AND <= 5 % of frames over 33 ms
   AND peak under 3,000 MB; say which of the three each arm misses.
4. PICTURE QUALITY: the same 3 poses, same time of day (noon, sun behind camera, memory: comparison images well lit), one frame per preset. Build a
   side-by-side per pose (Ultra left, each lower preset right, labelled) AND a per-preset difference number against Ultra (mean abs pixel difference,
   share of pixels changed > a stated threshold). LOOK at every image and say in words what each step down loses (shadows, torch light, far detail,
   shell range, sharpness, tree density) -- the owner decides "too much" by eye, so give full-size files and a captioned doc-ready sheet.
5. Answer the owner's three questions in the HANDOFF: (a) the BEST setting on this Mac (highest preset that works, with its look cost); (b) what
   works on this Mac (every arm that passes); (c) what the highest settings need: for Ultra and High, measured ms / 33.3 = the speed-up factor
   over this M1 Air, and the largest single costs from the data 212's tooling already gives (no new tooling). Do not name hardware you have not
   measured; say "about N times this GPU's throughput" only.
6. Reconcile in words why 212's 106.8 ms and the rig's 23.7 ms differ (which flags differ).

## End
Done with HANDOFF + `## For the doc`: a table arm x (p50, p99, % > 33 ms, peak MB, works?), the side-by-sides, and the three answers.
