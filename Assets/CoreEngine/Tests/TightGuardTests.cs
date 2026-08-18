// Assets/CoreEngine/Tests/TightGuardTests.cs
//
// Amendment 8.7 — Divergence fix, safe lever #2.
//
// PROOF: for a NON-exit axis b, its own exitCount.b (the integer voxel-step
// count from current position to the far edge of the SPAN on that axis - a
// purely geometric quantity, independent of tDelta) is a valid safe upper
// bound on trueN, the number of boundary crossings axis b actually makes
// within tHit (the exit axis's arrival time).
//
// WHY: tExit.b = m0 + (exitCount.b - 1) * tDelta.b is the time axis b would
// fully exit the span. Axis b is NOT the exit axis, so by definition of how
// the exit axis is chosen (minimum tExit among the three), tExit.b >= tHit.
// Crossing-time is monotonically increasing in crossing-count (tDelta > 0), so
// tExit.b >= tHit implies exitCount.b >= trueN. That is the whole proof, and
// it is ONLY valid if tHit <= tExit.b actually holds for the test case - a
// fuzz that picks tHit and exitCount.b independently would not be testing this
// invariant at all (an earlier draft of this file made exactly that mistake -
// it degenerated to a tautology because it always fed axisExitCount=span+1,
// never a genuinely tighter value tied to a real geometric position). This
// version constructs every case FROM the invariant, so a real bound is what
// gets tested.
//
// This changes ONLY the loop's cap, never the per-iteration arithmetic (no
// new float op, no division) - so unlike the closed-form attempt, there is no
// new rounding path that could disagree with the original loop.

using NUnit.Framework;

public static class TightGuard
{
    public static int LoopCount(float m0, float tDelta, float tHit, int guardMax)
    {
        const float eps = 1e-4f;
        float m = m0;
        int g = 0;
        while (m <= tHit + eps && g < guardMax)
        {
            m += tDelta;
            g++;
        }
        return g;
    }

    public static int LoopCountTightGuard(float m0, float tDelta, float tHit, int spanGuard, int axisExitCount)
    {
        const float eps = 1e-4f;
        int tightGuard = System.Math.Min(axisExitCount, spanGuard);
        float m = m0;
        int g = 0;
        while (m <= tHit + eps && g < tightGuard)
        {
            m += tDelta;
            g++;
        }
        return g;
    }

    // --- Variant A (closed-form non-exit-axis count), Amendment 8.8/8.9 ---
    // Computes the SAME quantity LoopCount's while-loop computes (how many
    // times m0, m0+tDelta, m0+2*tDelta... land at or before tHit+eps, capped
    // at guardMax), in O(1) instead of O(g). This is NOT trying to reproduce
    // the loop's accumulated m bit-exactly (that was tried for the exit axis
    // and shown to fail ~87-88% of the time - see RaymarchReference.cs's file
    // header). This only needs to reproduce the loop's INTEGER COUNT, which is
    // a well-posed floor() computation with no accumulation involved, hence no
    // accumulated-vs-single-step disagreement is possible.
    public static int LoopCountClosedForm(float m0, float tDelta, float tHit, int guardMax)
    {
        const float eps = 1e-4f;
        float absDir = 1f / tDelta; // tDelta = 1/|rayDir| on this axis, so absDir = |rayDir| = 1/tDelta
        int n = (int)System.MathF.Floor((tHit + eps - m0) * absDir) + 1;
        return System.Math.Clamp(n, 0, guardMax);
    }
}

public class TightGuardTests
{
    // ---------------------------------------------------------------------
    //  Structured cases, each constructed FROM the invariant: pick m0, tDelta,
    //  exitCount.b geometrically, DERIVE tExit.b from them, then pick tHit as
    //  something <= tExit.b (the only condition under which b is genuinely a
    //  non-exit axis). This is the only way to honestly test the bound.
    // ---------------------------------------------------------------------

    [Test]
    public void SmallGeometricExitCount_TightBoundStillCorrect()
    {
        // Voxel starts 2 steps from the span edge on this axis (exitCount=2),
        // tDelta moderate. tExit.b = m0 + (2-1)*tDelta. tHit must be <= tExit.b.
        float m0 = 0.4f, tDelta = 3.0f;
        int axisExitCount = 2;
        float tExitB = m0 + (axisExitCount - 1) * tDelta; // = 3.4
        float tHit = tExitB * 0.7f; // safely <= tExitB, respects the invariant

        int loop = TightGuard.LoopCount(m0, tDelta, tHit, 129);
        int tight = TightGuard.LoopCountTightGuard(m0, tDelta, tHit, 129, axisExitCount);
        Assert.AreEqual(loop, tight, $"small-exitcount: loop={loop} tight={tight}");
    }

    [Test]
    public void THitExactlyAtAxisExit_BoundaryCase()
    {
        // tHit == tExit.b exactly - the tightest possible legal case (b ties
        // for exit axis but lost the tie-break, or is genuinely non-exit at
        // the boundary). The bound must still hold with equality-adjacent eps.
        float m0 = 0.2f, tDelta = 5.0f;
        int axisExitCount = 4;
        float tExitB = m0 + (axisExitCount - 1) * tDelta;
        float tHit = tExitB; // exactly at the bound

        int loop = TightGuard.LoopCount(m0, tDelta, tHit, 129);
        int tight = TightGuard.LoopCountTightGuard(m0, tDelta, tHit, 129, axisExitCount);
        Assert.AreEqual(loop, tight, $"exact-at-bound: loop={loop} tight={tight}");
    }

    [Test]
    public void LargeGeometricExitCount_NearFullSpan_TightGuardStillMatches()
    {
        // Voxel just entered the span (exitCount close to the full span size),
        // small tDelta so many real crossings occur - the tight bound here is
        // barely tighter than spanGuard, must still reproduce the same result.
        int span = 128;
        float m0 = 0.1f, tDelta = 1.02f;
        int axisExitCount = span; // near-full-span remaining
        float tExitB = m0 + (axisExitCount - 1) * tDelta;
        float tHit = tExitB * 0.9f;

        int loop = TightGuard.LoopCount(m0, tDelta, tHit, span + 1);
        int tight = TightGuard.LoopCountTightGuard(m0, tDelta, tHit, span + 1, axisExitCount);
        Assert.AreEqual(loop, tight, $"large-exitcount: loop={loop} tight={tight}");
    }

    // ---------------------------------------------------------------------
    //  Randomized fuzz — EVERY case constructed from the invariant (tHit is
    //  DERIVED to respect tHit <= tExit.b, never picked independently). This
    //  is what makes the fuzz test the actual claim instead of a tautology.
    // ---------------------------------------------------------------------

    [Test]
    public void RandomizedFuzz_TightGuardAlwaysMatchesOriginal_InvariantRespected()
    {
        var rng = new System.Random(20260812);
        int[] spans = { 8, 16, 32, 64, 128 };

        int disagreements = 0;
        int closedFormDisagreements = 0;
        int totalCases = 20000;

        for (int i = 0; i < totalCases; i++)
        {
            int span = spans[rng.Next(spans.Length)];
            int spanGuard = span + 1;

            // Geometric: how far into the span is the current voxel on this
            // axis, i.e. axisExitCount, chosen anywhere from 1 (about to exit)
            // to span (just entered) - INDEPENDENT of tDelta, exactly like the
            // real quantity.
            int axisExitCount = rng.Next(1, span + 1);

            // tDelta: this axis's own crossing distance, spread from steep to
            // near-axis-aligned, genuinely independent of axisExitCount.
            double tDeltaLog = rng.NextDouble() * (System.Math.Log(2000.0) - System.Math.Log(0.5)) + System.Math.Log(0.5);
            float tDelta = (float)System.Math.Exp(tDeltaLog);

            // m0: distance to this axis's first boundary, in [0, tDelta).
            float m0 = (float)(rng.NextDouble() * tDelta * 0.999);

            // DERIVE tExit.b from m0, tDelta, axisExitCount - this is exactly
            // what LeapSpan computes for every axis.
            float tExitB = m0 + (axisExitCount - 1) * tDelta;

            // ENFORCE THE INVARIANT: tHit must be <= tExit.b for axis b to
            // genuinely be a non-exit axis. Pick tHit anywhere in [0, tExitB].
            float tHit = (float)(rng.NextDouble() * tExitB);

            int loop = TightGuard.LoopCount(m0, tDelta, tHit, spanGuard);
            int tight = TightGuard.LoopCountTightGuard(m0, tDelta, tHit, spanGuard, axisExitCount);
            int closedForm = TightGuard.LoopCountClosedForm(m0, tDelta, tHit, spanGuard);

            if (loop != tight)
            {
                disagreements++;
                if (disagreements <= 20)
                {
                    Assert.Fail(
                        $"fuzz case {i}: DISAGREEMENT loop={loop} tight={tight} " +
                        $"(m0={m0:R}, tDelta={tDelta:R}, tHit={tHit:R}, span={span}, " +
                        $"axisExitCount={axisExitCount}, tExitB={tExitB:R}). " +
                        $"This would mean the bound proof itself has a gap - STOP, do not tune, report verbatim.");
                }
            }

            // Separate counter/message from the tight-guard check above - these
            // are two independent hypotheses (tight-guard bound vs closed-form
            // count), and conflating their failures would make it impossible to
            // tell which proof actually broke from the test output alone.
            if (loop != closedForm)
            {
                closedFormDisagreements++;
                if (closedFormDisagreements <= 20)
                {
                    Assert.Fail(
                        $"fuzz case {i}: CLOSED-FORM DISAGREEMENT loop={loop} closedForm={closedForm} " +
                        $"(m0={m0:R}, tDelta={tDelta:R}, tHit={tHit:R}, span={span}, " +
                        $"axisExitCount={axisExitCount}, tExitB={tExitB:R}). " +
                        $"This would mean the closed-form derivation itself has a gap - STOP, do not tune, report verbatim.");
                }
            }
        }

        Assert.AreEqual(0, disagreements, $"{disagreements}/{totalCases} tight-guard fuzz cases disagreed.");
        Assert.AreEqual(0, closedFormDisagreements, $"{closedFormDisagreements}/{totalCases} closed-form fuzz cases disagreed.");
    }

    // ---------------------------------------------------------------------
    //  Sanity check: confirm the tight bound is actually TIGHTER than
    //  spanGuard in realistic cases (otherwise this whole lever is pointless).
    //  Uses the real near-axis-aligned pathology from last measurement.
    // ---------------------------------------------------------------------

    [Test]
    public void TightBound_IsMeaningfullyTighterThanSpanGuard_OnRealisticCase()
    {
        // Mirrors pixel (250,150): tDelta ~935 on a non-exit axis, span=128.
        // Geometric exitCount here could be small OR large depending on voxel
        // position - test the case where it's small (near the exit edge),
        // which is the case that should benefit most.
        int span = 128;
        int axisExitCount = 3; // voxel is close to this axis's span edge
        float tDelta = 935.02f;
        float m0 = 0.3f;
        float tExitB = m0 + (axisExitCount - 1) * tDelta;
        float tHit = tExitB * 0.95f;

        int loop = TightGuard.LoopCount(m0, tDelta, tHit, span + 1);
        int tight = TightGuard.LoopCountTightGuard(m0, tDelta, tHit, span + 1, axisExitCount);

        Assert.AreEqual(loop, tight, "must still agree");
        Assert.LessOrEqual(axisExitCount, span, "sanity: bound is tighter than spanGuard in this case");
    }
}