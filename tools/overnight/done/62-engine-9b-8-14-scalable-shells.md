# 62-engine-9b-8-14-scalable-shells

Approval: owner, 2026-10-11 ~00:00 in chat, answering task 60's STOP: "I would go with your recommended for most of
these but remember that we are trying to stay open to as many devices as possible so the render range and LOD tiers
need to be dynamic based on the hardware. Or however popular games do it." Also: after 61, plan order (shells next).
Supervisor's reading (stated to the owner): Q2-Q6 = (a) as drafted; Q1 ("720 vs 540 sizing") is REPLACED by
hardware-scaled shells. Queued by the supervisor 2026-10-11. Branch `engine-phase`. ENGINE PLAN STEP 9, revision.
**PLANNING ONLY:** no code, no Unity, no builds, no rigs. You MAY read code, logs and docs. Edit ONE local file,
`AMENDMENT_8_14_LOD_SHELLS_DRAFT.md` (from task 60, commit `5ad05a4`), commit it, and end with `STOP_FOR_OWNER` at
the top of HANDOFF. The supervisor publishes it; you do not touch claude.ai.

## What to add / change in the draft
1. **New section "Scaling to the hardware"** (put it right after "For the owner"). Shells, render range and the 0.1 m
   window are NOT fixed constants: they are chosen per device at start-up from a quality preset.
   - First a short survey (from what you know; label it "from general knowledge, not looked up") of how shipped games
     do it: e.g. Minecraft render/simulation distance, the Distant Horizons LOD mod (LOD quality + distance), Unreal's
     scalability groups (view distance quality, `r.ViewDistanceScale`, HLOD), and how games auto-detect a default
     (VRAM / RAM / GPU class, sometimes a short benchmark) with a manual override. One line each, then what we copy.
   - Presets (e.g. Low / Medium / High / Ultra, names up to you): for each give the shell sizing rows (540 / 720 or
     lower), the number of shells (= render range in km), the 0.1 m window radius, pool caps, and the estimated
     memory (terrain MB and whole process) and generation need (cols/s, "full detail keeps up to N m/s"), using the
     draft's own formulas and sources. The owner's M1 8 GB / 3 GB cap (design doc section 11.3) must be one named
     preset that fits under 3 GB; add at least one preset BELOW it (weaker GPU / less RAM) and one above it.
   - Auto-pick on first launch from Unity's `SystemInfo` (systemMemorySize, graphicsMemorySize, GPU name/vendor,
     processorCount); say exactly which thresholds pick which preset, and that Apple unified memory counts once.
     The owner can override it in the F6 menu (task 58's `PlaySettings`, saved). Say what changing it costs at
     runtime (pools reallocate -> a re-prime / brief coarse view, or "applies on restart" -- pick the simpler, say why).
   - Constraints that must survive every preset: power-of-two rings, `&` indexing (CLAUDE.md, section 6.2 phantom
     terrain), `WINDOW_CHUNKS_Y` stays 16, the CPU stays the sole terrain writer, frozen APIs untouched. Rigs pin one
     preset so historical numbers stay comparable (name which).
   - Relation to frame rate: FSR 3 dynamic rows already scale the frame (task 52); presets scale memory and view
     distance. Say whether a preset should also pick the FSR 3 row range, and whether render range should ever change
     on its own at runtime (most games: no, it is a menu setting; dynamic resolution handles frame rate) --
     recommend one.
2. **Contract rows:** update row 1 (LODConfig: shell count and ranges become runtime values from the preset, not
   compile-time arrays; pools sized at start-up from the preset) and add rows for the preset/auto-detect if needed.
3. **Questions:** record the owner's answers (Q2-Q6 (a), Q1 replaced) in the questions section. Ask only NEW
   questions this revision raises (preset names/count, auto-pick thresholds, runtime vs restart, whether presets
   also set FSR 3 rows), each as (a)/(b)/(c) with your recommendation first and its cost. "All (a)" approves.
4. Keep the rest of the draft as it is unless the scaling changes it (e.g. the memory ledger becomes "per preset";
   keep the M1 preset's column as the headline).

## Acceptance
- The draft reads top-down; every number is sourced or "(est.)"; nothing built. Committed. HANDOFF with
  `STOP_FOR_OWNER` + the new questions at the top, and a `## For the doc` block (what changed, the preset table,
  the questions).

## Owner answer (2026-10-11 ~08:40, in chat)
"Approve all (a)" -- Amendment 8.14 as drafted: Q7-Q11 all (a) (four presets Low/Medium/High/Ultra, auto-pick =
lowest of memory/GPU/CPU tiers, F6 applies on restart, preset sets FSR 3 rows as a default, no automatic range
changes while playing). Engine step 10 is queued as task 74.
