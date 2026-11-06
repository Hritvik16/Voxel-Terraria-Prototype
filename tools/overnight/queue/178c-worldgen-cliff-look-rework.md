# 178c-worldgen-cliff-look-rework

Model: opus

Requires: 178b-worldgen-relief-skips-routes

Approval: owner, 2026-11-06, chat on 178's pictures: "Yeah, this looks horrible. What's with all the stripes in the painted look? This is not
good. We should rework it." Read `tools/overnight/blocked/178-worldgen-c4-cliff-geology.HANDOFF.md`, `Screenshots/178/shot_*_{before,after}.png`
(look at them yourself first), memories no-terrain-specific-engineering and game-design-intent (thick layers, no thin stripes; One Piece-like
bold shapes with a realistic twist).

## What is wrong (supervisor's read of `shot_cliff_after.png`, owner agreed)
- The thick layers render as huge FLAT colour fields (grey / orange / red / slate blue) with no texture: a painted map, not rock.
- Vertical "drip" streaks where layers meet the face (the fluting and/or layer boundaries following the face), and blob/hook shapes
  (a red hook on the right) -- the layer boundaries are noise contours, not strata.
- Colour steps between adjacent layers are far too strong.

## Do
- Rework so faces read as ROCK: layers mostly horizontal (strata dip gently, boundaries wavy only at a small amplitude), adjacent layers
  close in tone (same family per biome, subtle steps), and variation INSIDE each layer (per-brick/patch tone and material mixing from the
  layer's own rocks) so no layer is a flat field. Remove the vertical drip streaks (find which term makes them and say so). Keep ledges and
  scree only if they read well; otherwise drop them. No thin stripes.
- Compare against the BEFORE (plain faces) honestly: if the rework is not clearly better than before, recommend `-cliffs 0` instead.
- Shots: 178's three poses + one desert and one snow face, before | 178 | rework, full size. Make the PNG pairs openable directly
  (Preview); 178's `viewer.html` showed BLACK for the owner -- find out why and fix the viewer tool too.
- WHOLE suite (route test: only the known ford red allowed). Rough cost note; no timing arms.

## End
STOP_FOR_OWNER: "keep the reworked cliffs?" with the pictures.
