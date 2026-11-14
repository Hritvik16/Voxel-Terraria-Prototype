# 211-plan-gdd-batch-a-answers

Model: opus

Approval: OWNER, 2026-11-14, Queue decisions doc Q-AE (game design batch A, from task 202). **PLAN ONLY. No code.** This task may
edit ONE Claude Doc, the "Game design" doc https://claude.ai/artifact/BprGXbxRr6W5MPdDAXxYp8 (allowed here explicitly), plus
the repo copy `GAME_DESIGN.md`.

## The owner's answers (verbatim where quoted)
- A1 **(b)**: the core loop is close, but change the session row: "world change shouldn't happen for a while kind of like in
  terraria". So no early world flip; the first world-changing event comes late, like Terraria's hardmode.
- A2 **(c)**: "2 or 3 cities per medium world".
- A3 **(a)**: one rule-based humanoid generator for NPCs AND the player creator.
- A4 **(a)**: each species gives its own wood, some give fruit, felled trees regrow over time, "(seeds too)". Trees/plants drop
  seeds that can be planted.
- A5 **(c)**, with the owner's idea: "I wonder if we can make it dynamic. It is as safe as the townspeople keep it. Obviously this
  would need planning on how npcs go about doing things ... That way the player can mess with things (also allows the player to
  automatically build their own settlement that they can naturally grow into a city)".
- A6 **(a)**: "terraria's rules are simple and we should ideally try to keep all rules simple and depend on emergent behavior".

## Do
1. Fold A1-A6 into the doc's tables (Open / Leaning -> Decided, with "owner, 2026-11-14, Q-AE"), in the doc and in `GAME_DESIGN.md`.
2. Add a short **design principle** near the pitch, in the owner's words: keep every rule simple and let behaviour emerge
   (Terraria-style). Mark later rows that conflict with it as Leaning, with a note.
3. A5 opens a new design area, **town safety by NPC behaviour**. Write it as an Open section: what "as safe as the townspeople keep
   it" could mean with a few simple rules (guards patrol, lights and walls deter spawns, a town that loses its people becomes
   unsafe, the player's own settlement grows by the same rules into a city). List what it needs that does not exist yet (NPC
   routines, a spawn-danger map, the humanoid tool from A3). Then 3-5 questions for the owner, recommendation first, each with
   what it costs and unlocks. Keep the rules few.
4. Update the placement checklist for A2 (c) (2-3 cities per Medium world; site planner 187 / towns 190 numbers) and A4 (wood
   materials and seeds: how many new palette ids, from 128-223).
5. HANDOFF `## For the doc`: what changed in the doc (a short list) and the new questions (also printed in HANDOFF, so the
   supervisor can copy them to the Questions tab).
- No Unity launch, no tests, no memory cost (rule 11 does not apply: nothing is drawn or stored).

## End
Done with HANDOFF.
