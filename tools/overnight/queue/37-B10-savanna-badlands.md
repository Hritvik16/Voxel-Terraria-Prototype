# 37-B10-savanna-badlands

Requires: 36-B8-routes

Surface rebuild step B10. Approval:
- Plan approved by the owner 2026-10-02 (Q11 (a)); Q10 (a): "savanna and badlands biomes with
  palette entries, gated on your colour sign-off, and the B1 patch numbers repeated for 6
  biomes." Brief Decided rows: savanna and badlands are the next two biomes; biome count 6-8;
  biomes LARGER; biomes and landform regions are INDEPENDENT maps (snow beside desert is
  intended); not every biome x landform pair needs to appear.
- The COLOUR SIGN-OFF is the owner's (look and feel). This task does NOT wait for it: it ships
  its best candidate on v4 only (opt-in, reversible) and renders the alternatives for the
  owner's B10 review pack. v4 does not become the default here.
- Owner 2026-10-08: no performance arms (PREAMBLE rule 10). Queued by the supervisor 2026-10-08.

Branch `surface-rebuild`. Terrain-generator + palette change: `SUITE=all`. v3 byte-identical.

## The one change (v4 only)
- Add SAVANNA and BADLANDS to v4's biome field (climate fields, B1's scale), independent of
  landform. Say how many biomes v4 has after the change and the climate rule for each new one.
- Palette entries for their surface materials (`tools/palette_defs.py`, palette D,
  `DetailField.TopMaterial`; the slope rock rule above step 1.5 stays). `FarField.GenToEngine`
  is the chunk material table too (KNOWN_ISSUES) -- the far band must show the new colours.
  Emissive / mobile rules for materials unchanged (KNOWN_ISSUES).
- The record's biome field and B6's danger tiers pick them up (savanna tier 0, badlands tier 1).

## Acceptance (seeds 42, 7, 2026, 31337)
- B1's numbers for the new biome set: biome change walking east-west **>= 900 m**; median biome
  patch **>= 50 ha**; record **<= 7 MB**; map build **<= v3 + 2 s**.
- Both new biomes present on at least 3 of 4 seeds (report every seed's biome shares).
- B4/B5/B6/B8 gates still pass. `PaletteSampleTests` green. Hashes stable and seed-dependent;
  v3 unchanged. `SUITE=all` 0 FAIL.

## Pictures (the owner's B10 review pack -- caption every one)
- Biome PNG per seed, B8 beside B10.
- Seed 42: walk poses at 2 m eye in savanna and in badlands (gentle and broken ground), at
  noon and golden hour, and a far-band view across each.
- **Palette candidates:** 2-3 colour options per new biome, rendered at the same poses, with
  the swatches; say which one shipped. Put "Owner pick: savanna / badlands colours" with the
  paths at the top of HANDOFF (no STOP_FOR_OWNER).
- One `MODE=poses` run on v4 read for holes, floating geometry, 90%-sea frames. Clearly
  broken renders stop (STOP_FOR_OWNER).
