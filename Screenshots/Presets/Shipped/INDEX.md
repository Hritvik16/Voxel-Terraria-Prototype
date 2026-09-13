# Shipped

> tonight's values, for A/B -- CLIPS SNOW AT NOON

`sunIntensity` 1.25 | `ambientIntensity` 0.30 | `kSoft` 0.030 | `tonemap` 2 | `exposure` 1.00

| # | image | time | what to look at |
|---|---|---|---|
| 1 | `1_noon_surface.png` | 12:00 | the flat midday case -- where highlights clip first |
| 2 | `2_golden_surface.png` | 17:35 | long shadows and the warmest sun colour the preset defines |
| 3 | `3_dusk.png` | 18:00 | sun at the horizon -- ambient is doing nearly all the work |
| 4 | `4_midnight.png` | 00:00 | the night floor: is it moody, or is it unreadably black? |
| 5 | `5_snow_noon.png` | 12:00 | THE CLIPPING TEST. Snow at noon is the brightest surface in the game |
| 6 | `6_cave_dark.png` | 12:00 | §6.5 requires caves be dark. Is this dark, or is it black? |
| 7 | `7_cave_torchlit.png` | 12:00 | one torch, the falloff, and whether the far wall still reads |
| 8 | `8_distant_terrain.png` | 17:35 | 200 m out, past the tier 1->2 boundary -- aerial and shadow at range |
| 9 | `9_shadow_edge.png` | 17:35 | close on a shadow edge: C.10's penumbra, or a hard step? |

## tonemap/

The same three scenes under each tone curve. `None` is what every
screenshot before Stage J looked like -- a bare `saturate()`, so anything
over 1.0 clips to flat white and the surface detail in it is gone.

| scene | None (pre-Phase-7 clamp) | Reinhard | ACES |
|---|---|---|---|
| 1_noon_surface | `tonemap/1_noon_surface_tmNone.png` | `tonemap/1_noon_surface_tmReinhard.png` | `tonemap/1_noon_surface_tmACES.png` |
| 5_snow_noon | `tonemap/5_snow_noon_tmNone.png` | `tonemap/5_snow_noon_tmReinhard.png` | `tonemap/5_snow_noon_tmACES.png` |
| 7_cave_torchlit | `tonemap/7_cave_torchlit_tmNone.png` | `tonemap/7_cave_torchlit_tmReinhard.png` | `tonemap/7_cave_torchlit_tmACES.png` |
