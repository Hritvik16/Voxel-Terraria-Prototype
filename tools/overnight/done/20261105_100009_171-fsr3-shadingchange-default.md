# 171-fsr3-shadingchange-default

Model: opus

Approval: owner, 2026-11-02, doc Q107 answer (a): ship F2 (`-fsr3sc 10`) as the play default AFTER a play check with the clock running near water and a torch.
Read `tools/overnight/blocked/166-look-fsr3-softening.HANDOFF.md` (code `0b43c28`).

## Do
1. QA first, capture only (no frame times): FSR 3 dyn 432-540 rows, today vs `-fsr3sc 10`, with the day clock RUNNING (sun moving) at walk_open, beside
   water (waves/flow) and beside a torch at night (flicker / moving light). Bursts of frames as in 166 (`tools/fsr166.py`), plus 4x zoom stills.
   Look for ghost trails behind moving light or water edges, and lag after a light change.
2. Clean result (no new ghosting or smear vs today): make `-fsr3sc 10` the play default (flag `-fsr3sc 0` = old; rigs/capture unchanged), fast suite once.
3. Any new ghosting/lag: do NOT flip the default; end STOP_FOR_OWNER with the sheets (captioned, one picture per problem).

## For the doc
Before/after stills of the three QA scenes, with what to look at in each.
