// Assets/CoreEngine/Tests/ShadowReuseTests.cs
//
// Task 124 (PERF_30FPS_PLAN row 5 (b)): temporal sun-shadow reuse one frame in N
// (-shadowreuse N, 4 by default = the pre-124 2x2 rotation). The look and the
// cost are checked with stills (run-look-review.sh -lookreuse 8) and the
// cost-split arm reuseN; this covers the flag, that it reaches the shader, and
// the turn pattern: every tile traces exactly once in every N consecutive frames,
// neighbours trace on different frames, and period 4 is the old test.
using System.IO;
using NUnit.Framework;

public class ShadowReuseTests
{
    /// The mirror of Raymarch.compute's `turn` (an 8x8 thread group's slot; the
    /// group traces when (turn & (period - 1)) == (frame & (period - 1))).
    /// Shader_TurnMatchesTheMirror pins the shader's line to this formula.
    private static uint Turn(uint tx, uint ty) =>
        (tx & 1u) | ((ty & 1u) << 1) | ((((tx ^ ty) >> 1) & 1u) << 2) | (((ty >> 1) & 1u) << 3);

    [Test]
    public void Flag_DefaultFour_AndIsBound()
    {
        string cs = File.ReadAllText("Assets/CoreEngine/Rendering/RaymarchFeature.cs");
        StringAssert.Contains("ShadowReusePeriod = ValidReusePeriod(ReadIntArg(\"-shadowreuse\", 4))", cs);
        StringAssert.Contains("passData.tsReuseMask = ValidReusePeriod(ShadowReusePeriod) - 1;", cs);
        StringAssert.Contains("\"_ShadowReuseMask\", data.tsReuseMask", cs, "the period reaches the shader");
        StringAssert.Contains("ValidReusePeriod(int n) => n == 1 || n == 2 || n == 4 || n == 8 || n == 16 ? n : 4;", cs, "anything else -> today's 4");
    }

    [Test]
    public void Shader_TurnMatchesTheMirror()
    {
        string src = File.ReadAllText("Assets/CoreEngine/Rendering/Raymarch.compute");
        StringAssert.Contains("uint turn = (tile.x & 1u) | ((tile.y & 1u) << 1u) | ((((tile.x ^ tile.y) >> 1u) & 1u) << 2u) | (((tile.y >> 1u) & 1u) << 3u);", src);
        StringAssert.Contains("bool myTurn = ((turn & reuseMask) == (sFrame & reuseMask));", src);
        StringAssert.Contains("int _ShadowReuseMask;", src);
    }

    [Test]
    public void EveryTileTracesOncePerPeriod_AndFourIsTheOldRotation()
    {
        const uint tiles = 32;
        foreach (uint period in new uint[] { 1, 2, 4, 8, 16 })
        {
            uint mask = period - 1;
            for (uint ty = 0; ty < tiles; ty++)
                for (uint tx = 0; tx < tiles; tx++)
                {
                    int traced = 0;
                    for (uint f = 100; f < 100 + period; f++)
                        if ((Turn(tx, ty) & mask) == (f & mask)) traced++;
                    Assert.AreEqual(1, traced, $"period {period}: tile ({tx},{ty}) traced {traced} times in {period} frames");
                    if (period == 4)
                        Assert.AreEqual((tx & 1u) | ((ty & 1u) << 1), Turn(tx, ty) & 3u, "period 4 = the pre-124 2x2 rotation");
                    if (period >= 4 && tx + 1 < tiles)
                        Assert.AreNotEqual(Turn(tx, ty) & mask, Turn(tx + 1, ty) & mask, "side neighbours trace on different frames");
                }
            // Each frame traces 1/period of the tiles.
            int[] perSlot = new int[period];
            for (uint ty = 0; ty < tiles; ty++)
                for (uint tx = 0; tx < tiles; tx++) perSlot[Turn(tx, ty) & mask]++;
            foreach (int c in perSlot) Assert.AreEqual((int)(tiles * tiles / period), c, $"period {period}: tiles per frame");
        }
    }
}
