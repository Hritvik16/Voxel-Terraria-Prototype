# tools/play-flags.sh -- task 145b: the flags that make a player launch look like PLAY. Sourced (not run) by
# run-play.sh and run-tg2world.sh MODE=playwalk, so the two cannot drift apart.
#
# PLAY_WORLD_FLAGS: what run-play.sh passes for the world itself (tg2, map $MAPV, far band, far shadows).
#   Needs MAPV set before sourcing (run-play.sh: default 4).
# PLAY_SETTINGS_FLAGS: PlaySettings' DEFAULTS (Assets/CoreEngine/Gameplay/PlaySettings.cs) written as flags, plus
#   RenderPreset.Default (quality) and PlaySettings.PlayShellWorkers (3). An interactive run gets these from the
#   settings file's defaults; a capture run (-sustained, ...) never reads the file, so a rig that wants the play
#   path passes these. run-play.sh must NOT pass them: they would override the owner's saved F6 choices.
#   PlaySettingsTests.PlayFlagsFileMatchesPlaySettingsDefaults pins this line against the class: a new
#   PlaySettings field, or a changed default, fails that test until this line is updated.
#   Not expressible as flags in a capture run (see TASKS.md task 145b): gameMode survival and the movement
#   abilities (Playground._moveAbilities = IsPlaySettingsRun), the HUD perf overlay level. softShadows true and
#   the scripted -sustained camera need no flag.
PLAY_WORLD_FLAGS="-world tg2 -mapversion ${MAPV:-4} -farband 1 -farshadow 1"
PLAY_SETTINGS_FLAGS="-renderpreset quality -upscaler fsr3 -renderrows dyn -shadowlevel 0 -roofskip 0 -shadowres full -gamemode survival -underwater 1 -seethrough 1 -riverlook 1 -farshade 1 -shells 1 -shellworkers 3 -minimap"
