// Assets/CoreEngine/Tests/PlayerConfigTests.cs
//
// §8.1's hot reload means this file is edited BY HAND, WHILE THE GAME RUNS.
// That is the feature, and it is also the threat model: a dropped minus sign or
// a half-written save arrives in a live frame. These tests pin what happens
// then. The file-watching half is proven end-to-end by Phase6PlayerRig.
//
// Parse and Validate are pure static functions over a string, so none of this
// needs a scene, a file, or a frame.

using NUnit.Framework;

public class PlayerConfigTests
{
    private static PlayerConfig Sane() => new PlayerConfig();

    [Test]
    public void RoundTrip_PreservesEveryTunableValue()
    {
        var a = new PlayerConfig
        {
            accelerationMps2 = 42f,
            maxSpeedMps = 7.5f,
            jumpImpulseMps = 9f,
            airControl = 0.5f,
            frictionMps2 = 33f,
            stepHeightVoxels = 5,
            coyoteTimeSeconds = 0.2f,
            gravityMps2 = 18f,
            terminalSpeedMps = 44f,
            bodyWidthM = 0.7f,
            bodyHeightM = 1.9f,
            maxSubstepVoxels = 0.25f,
        };

        string err;
        var b = PlayerConfig.Parse(a.ToJson(), Sane(), out err);

        Assert.IsNull(err, "a config the game itself wrote must reload without complaint");
        Assert.AreEqual(42f, b.accelerationMps2, 1e-4f);
        Assert.AreEqual(7.5f, b.maxSpeedMps, 1e-4f);
        Assert.AreEqual(9f, b.jumpImpulseMps, 1e-4f);
        Assert.AreEqual(0.5f, b.airControl, 1e-4f);
        Assert.AreEqual(33f, b.frictionMps2, 1e-4f);
        Assert.AreEqual(5, b.stepHeightVoxels);
        Assert.AreEqual(0.2f, b.coyoteTimeSeconds, 1e-4f);
        Assert.AreEqual(18f, b.gravityMps2, 1e-4f);
        Assert.AreEqual(44f, b.terminalSpeedMps, 1e-4f);
        Assert.AreEqual(0.7f, b.bodyWidthM, 1e-4f);
        Assert.AreEqual(1.9f, b.bodyHeightM, 1e-4f);
        Assert.AreEqual(0.25f, b.maxSubstepVoxels, 1e-4f);
    }

    [Test]
    public void EveryFeelParameterNamedInSection81_IsPresentInTheJson()
    {
        // §8.1 names these exactly: "acceleration, max speed, jump impulse, air
        // control, friction, step height, coyote time". If one ever stops being
        // serialized, live tuning silently stops working for it -- which is the
        // kind of thing nobody notices until they are trying to find feel.
        string json = new PlayerConfig().ToJson();
        foreach (string field in new[]
        {
            "accelerationMps2", "maxSpeedMps", "jumpImpulseMps",
            "airControl", "frictionMps2", "stepHeightVoxels", "coyoteTimeSeconds",
        })
            StringAssert.Contains(field, json, $"§8.1's '{field}' must be tunable from the JSON");
    }

    [Test]
    public void MalformedJson_KeepsTheRunningConfig_RatherThanResettingToDefaults()
    {
        var running = new PlayerConfig { jumpImpulseMps = 11f };
        string err;
        var result = PlayerConfig.Parse("{ this is not json", running, out err);

        Assert.AreSame(running, result,
            "a half-written file must leave the player moving exactly as they were");
        Assert.IsNotNull(err, "but it must say so");
    }

    [Test]
    public void AnEmptyFile_KeepsTheRunningConfig()
    {
        // Editors routinely truncate-then-write, so a zero-byte read is a normal
        // transient, not corruption.
        var running = new PlayerConfig { jumpImpulseMps = 11f };
        string err;
        Assert.AreSame(running, PlayerConfig.Parse("", running, out err));
        Assert.IsNotNull(err);
        Assert.AreSame(running, PlayerConfig.Parse("   \n", running, out err));
        Assert.IsNotNull(err);
    }

    [Test]
    public void NegativeAndZeroValues_AreClampedIntoSomethingRunnable()
    {
        var c = new PlayerConfig
        {
            maxSpeedMps = -5f,
            bodyHeightM = -1f,
            bodyWidthM = 0f,
            maxSubstepVoxels = 0f,
            gravityMps2 = -9f,
            airControl = -0.5f,
        };
        string note = c.Validate();

        Assert.IsNotNull(note, "clamping must be reported, not silent");
        Assert.Greater(c.maxSpeedMps, 0f);
        Assert.Greater(c.bodyHeightM, 0f, "a negative body height inverts the AABB");
        Assert.Greater(c.bodyWidthM, 0f);
        Assert.Greater(c.maxSubstepVoxels, 0f, "zero substep size would divide by zero");
        Assert.Greater(c.gravityMps2, 0f);
        Assert.GreaterOrEqual(c.airControl, 0f);
    }

    [Test]
    public void NonFiniteValues_AreReplaced()
    {
        // A NaN anywhere propagates into the position and never comes back out.
        var c = new PlayerConfig
        {
            jumpImpulseMps = float.NaN,
            maxSpeedMps = float.PositiveInfinity,
            gravityMps2 = float.NegativeInfinity,
        };
        c.Validate();

        Assert.IsFalse(float.IsNaN(c.jumpImpulseMps));
        Assert.IsFalse(float.IsInfinity(c.maxSpeedMps));
        Assert.IsFalse(float.IsInfinity(c.gravityMps2));
        Assert.Greater(c.gravityMps2, 0f);
    }

    [Test]
    public void AirControl_IsClampedToZeroOne()
    {
        var c = new PlayerConfig { airControl = 5f };
        c.Validate();
        Assert.AreEqual(1f, c.airControl, 1e-5f, "air control above 1 would out-accelerate the ground");
    }

    [Test]
    public void StepHeight_CannotExceedBodyHeight()
    {
        // A step taller than the player lets them climb a cliff by walking into
        // it, and step INTO a ceiling.
        var c = new PlayerConfig { bodyHeightM = 1.8f, stepHeightVoxels = 99 };
        string note = c.Validate();

        Assert.LessOrEqual(c.stepHeightVoxels, 17, "capped to just under the 18-voxel body");
        Assert.IsNotNull(note);
        StringAssert.Contains("stepHeightVoxels", note);
    }

    [Test]
    public void StepHeight_IsNeverNegative()
    {
        var c = new PlayerConfig { stepHeightVoxels = -3 };
        c.Validate();
        Assert.AreEqual(0, c.stepHeightVoxels, "0 means 'no step-up', which is a legitimate setting");
    }

    [Test]
    public void ParsingClampedButValidJson_StillReturnsTheNewValues()
    {
        // Clamping is a note, not a rejection: the tuner should still see their
        // edit take effect, just bounded.
        var running = new PlayerConfig { maxSpeedMps = 5f };
        string err;
        var result = PlayerConfig.Parse("{\"maxSpeedMps\":-2.0,\"jumpImpulseMps\":7.0}", running, out err);

        Assert.AreNotSame(running, result, "a parseable file is applied even if some field was clamped");
        Assert.AreEqual(7f, result.jumpImpulseMps, 1e-4f, "the valid field takes effect");
        Assert.Greater(result.maxSpeedMps, 0f, "the invalid one is clamped");
        StringAssert.Contains("clamped", err);
    }

    [Test]
    public void APartialJsonFile_LeavesUnmentionedFieldsAtTheirDefaults()
    {
        // JsonUtility fills absent fields from the object's initialisers, so a
        // tuner can keep a two-line file with just what they are working on.
        string err;
        var c = PlayerConfig.Parse("{\"jumpImpulseMps\":9.5}", Sane(), out err);

        Assert.AreEqual(9.5f, c.jumpImpulseMps, 1e-4f);
        Assert.AreEqual(new PlayerConfig().maxSpeedMps, c.maxSpeedMps, 1e-4f,
            "unmentioned fields keep the shipped default");
    }

    [Test]
    public void Clone_IsIndependent()
    {
        var a = new PlayerConfig { jumpImpulseMps = 3f };
        var b = a.Clone();
        b.jumpImpulseMps = 99f;
        Assert.AreEqual(3f, a.jumpImpulseMps, 1e-5f);
    }
}
