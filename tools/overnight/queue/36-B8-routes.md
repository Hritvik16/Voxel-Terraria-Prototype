# 36-B8-routes

Requires: 35-B6-danger-record

Surface rebuild step B8. Approval:
- Plan approved by the owner 2026-10-02 (Q11 (a)); rule 9, "Routes in detail" + the B8 row.
- Owner answers (plan section 8): Q3 (a) spanning tree plus extra links so every area has two
  ways out -- owner added: **routes must NOT look constructed for the player; it should feel
  like there are several options.** Q4 (a) a 60 m corridor, one danger tier lower, eased to
  Scramble at worst. Q5 (a) routes follow valleys and passes with the next waypoint in sight --
  owner added: **natural-looking routes, with a landmark in sight the player may be trying to
  reach.**
- B7 (towns) is NOT done (owner 2026-10-04: no placement until the overhaul is done). Routes
  join EASY AREAS only.
- Owner 2026-10-08: no performance arms (PREAMBLE rule 10). Queued by the supervisor 2026-10-08.

Branch `surface-rebuild`. Terrain-generator change: `SUITE=all`. v4 only; v3 byte-identical.

## The one change (v4 only) -- plan rule 9
- **Which areas connect:** a spanning tree over the easy areas by least travel cost, plus extra
  links so every area has >= 2 ways out.
- **Path cost:** each 8 m cell costs (band cost) x (1 + base danger); Walk 1, Scramble 2,
  Climb 6, Sheer 30. Sheer is allowed (a route never fails) but reported. If an 8 m search is
  too slow for the build-time gate, run it on a 16 m grid.
- **Easing inside the corridor (60 m wide):** cut a pass where the path crosses a ridge, by at
  most **20 m** of lowering; smooth slopes to Scramble at most. No spikes, natural shape -- no
  road-like straight cuts or uniform-width trenches.
- **Danger drop:** corridor cells store base danger minus one tier (never below 0); the land
  around keeps its full danger.
- **Finding a route:** prefer valleys and passes from which the next waypoint (a pass, the next
  easy area or a signature feature) is in sight. Record: route list (polyline of cells, width,
  endpoints, `mapVersion`), `route corridor` flag.
- **More than one way past:** where a route crosses a Climb or Sheer band, report whether a
  second crossing exists within 500 m (report only).

## Acceptance (seeds 42, 7, 2026, 31337 + the 20-seed map-only sweep)
- Easy-area graph connected on all seeds and the 20-seed sweep; every area **2+ links**.
- On **80%+** of route length the corridor danger is >= 1 tier below the land 100-250 m to
  either side.
- Walk + Scramble **85%+** of route length, sampled at **0.5 m** along the centre line (voxel
  ground, not the 8 m map), and **0 m of Sheer**.
- Detour **<= 1.6x** straight-line distance. Next waypoint in sight from **70%+** of route
  length. Passes lowered **<= 20 m**. Second crossings near Climb bands reported.
- Total map build **<= 32 s** (v3 about 27 s); needle guard green; B4/B5/B6 gates still pass.
- Hashes stable and seed-dependent; v3 unchanged. `SUITE=all` 0 FAIL.

## Pictures
- Route PNG per seed (routes + corridors over danger or region class), captioned.
- Seed 42: poses at route midpoints looking along the route (2 m eye on exact ground), and one
  `MODE=poses` run on v4 read for holes, floating geometry, 90%-sea frames.
- Say in HANDOFF, with a picture, whether a route reads as natural or constructed (owner's
  requirement). Clearly broken renders stop (STOP_FOR_OWNER); otherwise no stop.
