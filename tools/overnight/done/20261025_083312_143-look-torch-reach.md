# 143-look-torch-reach

Model: opus

Approval: owner, 2026-10-25, in chat (AskUserQuestion), Q92 "Look task first (Recommended)": one torch lights only a ~1-2 m glow (139,
`Screenshots/139/dc139_torch.png`). CAPTURE-ONLY side-by-side: NO default change, nothing shipped. Not a performance task (no timing arms;
note the per-light cost from Phase 7 numbers if known).

## Do (ONE launch, short fixed waits)
Use 138/139's built tunnel and sealed room (`-skytiltqa 1` site via `SkyTiltBuild`, torch via the `LightingDriver.Attach` path 139 fixed).
Night, today's lighting (no other change). Arms: today's torch, then 3 settings spanning "lights a small room" to "lights a 10-15 m tunnel
section" (radius / intensity / falloff; state the exact values and which existing parameters they are -- do not add new light models).
Rows: tunnel (looking down it), sealed room (corner view), and one surface spot at night (torch on open ground) so the owner sees outdoor
reach too. Play look (FSR 3 dyn) for the sheet.

## Deliverable
ONE labelled sheet under `Screenshots/143/` (columns in plain words: "today: ~1-2 m", "small room", ...), lit-area numbers per cell (e.g.
pixels above a luma threshold), and in HANDOFF `## For the doc` the sheet path with a caption, the parameter values, and whether any
setting would conflict with Phase 7's light caps / budgets (say so, do not change them). No STOP_FOR_OWNER needed. TASKS.md paragraph if
a capture flag was added. Fast EditMode suite only if code changed (wait out an owner pause; never skip).
