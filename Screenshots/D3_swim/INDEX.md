# D3_swim — does the distant shadow drift when only the camera moves?

**The question.** §8 of `PHASE_7_COMPLETION.md` flagged a speckle in distant
shadows and could not settle it, because tiers 1 and 2 have no CPU oracle for
shadow rays any more than for primary rays (Amendment 8.10 §5.7). If the speckle
is **stable** — fixed to the terrain, the same shape every frame — it is an
aliasing artefact of coarse sampling and it is cosmetic. If it **swims** — if the
pattern slides across the rock as the camera moves sideways — it is temporal
instability, which is far more visible in motion than any still can show and is
the version worth spending frame budget to fix.

**How these were made.** A **5-step lateral pan at 200 m** (past the tier 1→2
boundary at 128 m), golden hour. Nothing changes between frames except the
camera's sideways position: same sun, same time, same terrain, same rung.

| frame | lateral step |
|---|---|
| `D3_swim_0_200m.png` | −2 |
| `D3_swim_1_200m.png` | −1 |
| `D3_swim_2_200m.png` | 0 (centre) |
| `D3_swim_3_200m.png` | +1 |
| `D3_swim_4_200m.png` | +2 |

**How to read them.** Flip between the five at full size, fast, and watch one
specific rock face rather than the picture as a whole.

- The speckle pattern **stays welded to the same rock faces** → stable aliasing.
  Cosmetic, and a candidate for §11's carry-forward rather than a fix now.
- The speckle **slides or boils across the surface** → temporal instability, and
  it will be much worse in real motion than these stills suggest.

**This package deliberately contains no verdict.** Judging shadow stability from
stills is exactly the thing stills are worst at, and the finding would be an
opinion dressed as a measurement. The frames are here so the question can be
answered by looking.
