// ==========================================
// Assets/CoreEngine/Tests/DeltaPresenceTests.cs
//
// STAGE 5b prerequisite: the delta-presence set that replaces a per-dispatch
// filesystem stat.
//
// The hybrid dispatch routes a chunk to the job path only when it has no delta
// file. If that question is ever answered WRONG in the "no delta" direction, a
// player's edits are silently dropped from a freshly-loaded chunk -- which
// nothing else in the suite would catch, because generation itself is still
// bit-identical. So the filename parse that backs the set is tested directly,
// including the cases where it must refuse to guess.
using NUnit.Framework;
using Unity.Mathematics;
using VoxelEngine.Streaming;

public class DeltaPresenceTests
{
    [Test]
    public void FileName_RoundTripsThroughTryParse()
    {
        var coords = new[]
        {
            new int3(0, 0, 0),
            new int3(11, 0, 11),
            new int3(100, 0, 100),
            new int3(-7, 3, -12345),   // negatives: the '-' must not be eaten by the '_' split
            new int3(int.MaxValue, int.MinValue, 1),
        };

        foreach (int3 c in coords)
        {
            string name = DeltaCodec.FileName(c);
            Assert.IsTrue(DeltaCodec.TryParseFileName(name, out int3 parsed),
                $"failed to parse a name this codec itself produced: {name}");
            Assert.AreEqual(c, parsed, $"round trip changed the coord for {name}");
        }
    }

    [Test]
    public void TryParseFileName_RefusesAnythingElse()
    {
        // A stray file must not register as some chunk's delta. Guessing here
        // would mean routing an unedited chunk down the worker path forever, or
        // worse, shadowing a real coord.
        string[] rejects =
        {
            null,
            "",
            "world.meta",
            "1_2_3.txt",            // wrong extension
            "1_2.delta",            // too few components
            "1_2_3_4.delta",        // too many
            "a_2_3.delta",          // non-numeric
            "1_2_.delta",           // empty component
            ".delta",
            "99999999999999_0_0.delta", // overflows int
        };

        foreach (string r in rejects)
            Assert.IsFalse(DeltaCodec.TryParseFileName(r, out _),
                $"accepted a filename it should have refused: {r ?? "<null>"}");
    }
}
