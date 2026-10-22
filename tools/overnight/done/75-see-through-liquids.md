# 75-see-through-liquids

Approval: owner, 2026-10-11 in chat: "water and some other liquids should be see through? Sometimes I see an opaque
block." Asked which: chose **all liquids, each its own clarity** ("realistic and also makes for more interesting
gameplay later. We need it to be efficient though. We are already struggling on framerate"); order: right after 73
(73 is blocked for the owner and 74 had already started, so this runs after 74). Queued by the supervisor 2026-10-11.
Branch `engine-phase`. Look change, behind an F6 setting (on by default in play).

## Change (one change)
Liquids seen from ABOVE (or from the side) become see-through, each with its own clarity, reusing 66b's in-fluid
march (participating medium: per-fluid tint + exponential absorption by distance travelled in fluid):
- A small per-fluid table (absorption per metre + tint; constants, not a new buffer if avoidable). Starting picks,
  agent may tune and must record them under "Picks (owner may change)":
  water clearest (bed visible to ~4-6 m, blue-green), acid murky green (~1.5 m), honey amber (~1 m), sludge
  ~0.5 m, oil near-opaque (~0.2 m), lava opaque + its glow as today.
- When the primary ray hits a fluid surface: keep shading the surface (cheap Fresnel-ish sky/sun tint at the top
  face), then continue the march through fluid cells accumulating transmittance, and shade what is behind (bed,
  walls, other fluid) attenuated. Opaque fluids (lava) stop at the surface exactly as today.
- **Efficiency is a requirement (owner):** early-out when transmittance < ~2% or after a per-fluid max distance;
  cap the extra steps; no refraction, no second shadow ray through water (use the bed's normal sun shadow, or a
  depth-darkening term if that is cheaper -- say which); see the distance rule below.
- **Same look at every distance (owner, 2026-10-11: "they should look the same regardless of what distance they're
  at").** The clarity must match across the 0.1 m voxels, the coarser LOD/cascade tiers and the 2.5D far band. The
  far band already knows each water texel's level and ground height, so apply the same per-fluid absorption to
  (level - ground) depth there (cheap, no march). Show near / mid / far (~800 m) shots of the same lake; no visible
  change in water colour or clarity at a tier boundary.
- Also find and fix "sometimes I see an opaque block" if it is a distinct bug (e.g. a fluid cell drawn as a solid
  cube, a side face, LOD tier); say what it was. Rivers' slab shape is NOT this task (that is 73's R1).
- CPU stays the sole terrain writer; no fluid-sim change; no memory-layout change. Rigs/capture runs keep today's
  look unless the setting is passed (byte-identical with it off).

## Acceptance
- Screenshots at noon, sun behind the camera, each fluid seen from above at ~3 m and ~15 m (a test pool per fluid
  in the Playground arena is fine), plus a tg2 lake shore looking down into shallow water; before/after pairs.
  One-line captions saying what to look at.
- Fast EditMode suite green; `run-phase7-rig.sh` PASS count unchanged and shots identical with the setting off.
- **One cost check (owner asked for efficiency; exception to "features over measurement"):** before measuring,
  write in HANDOFF what each result triggers (e.g. <= 1 ms: keep on everywhere; 1-2.5 ms: shorter max distance on
  Low/Medium presets; > 2.5 ms: rethink). Then a warm paired A/B (setting off/on, ABBA, one warm-up launch discarded)
  at a lake-shore pose looking over water and one pose with no water in view. Report p50/p99 frame ms per arm.
- HANDOFF `## For the doc`: the clarity table, the cost numbers, 2-3 images.

## Supervisor decision (2026-10-11 14:30, recommended-option rule; owner told in chat, may object)
(b): the look stays ON in play as committed; 75b splits the +3.5 ms and makes ONE cheaper change, target <= 2.5 ms.
