# 195a-defaults-trees-towns-on

Model: opus

Approval: OWNER, 2026-11-13, Queue decisions doc Q-X answer (a): "Trees and towns ON once 192, 193 and 194 are
done ... I'll flip them after that unless you object." All three are done (192 open crowns, 193 sky fill + 194a play
default, 194 pit fix). Q-Y (b): leaf fill stays 45 (already `Trees.DEFAULT_LEAF_FILL`).

## Do
1. Trees ON by default (`-trees` defaults to 1). Towns ON by default (`-towns` 1), and whatever towns need to exist
   (`-sites 1` if towns are placed on sites -- check `TerrainGenChunkSource.Towns.cs` and say what you found).
   `-trees 0 -towns 0` (and `-sites 0` if changed) must give the pre-195a world byte for byte.
2. Do NOT touch ores (195), fluid pockets (stay OFF; 196), leaf fill, sky fill.
3. Check everything that assumes these are OFF by default: tests pinning the default, the F6 menu if it lists them,
   rigs whose identity/hash gates need the old world (pass the `0` flags explicitly there and name each one).
- Generator default changed -> `SUITE=all` once at the end. TASKS.md paragraph.
- No timing arms. Memory: report the process peak and near-pool refused counts the shot launch already prints, old vs
  new default (191 measured 2,47x-2,61x MB with everything ON; the cap is 3,000 MB). If the new default goes over 3,000 MB
  at any pose: STOP_FOR_OWNER with the numbers, defaults left OFF.

## Pictures (one launch, 3-4 poses, midday, sun behind camera)
191's forest, snow and town poses: new default vs the old flags. Captions say what changed.

## End
Done with HANDOFF + pictures.
