// Assets/CoreEngine/Tests/PlayerPoseTests.cs
//
// Gameplay step 4 (task 93c1): player_pose.json (PlayerPose) round-trips and
// refuses what would put a KEEP=1 resume somewhere wrong.
using NUnit.Framework;

public class PlayerPoseTests
{
    [Test]
    public void RoundTrip_KeepsEveryField()
    {
        var p = new PlayerPose { mapVersion = 4, x = 4012.5f, y = 531.25f, z = -0.75f, yaw = 271f, pitch = -12f, walk = false };
        var q = PlayerPose.FromJson(p.ToJson(), 4, out string err);
        Assert.IsNull(err);
        Assert.IsNotNull(q);
        Assert.AreEqual(p.ToJson(), q.ToJson());
    }

    [Test]
    public void Refuses_BadFiles()
    {
        var ok = new PlayerPose { mapVersion = 4, x = 1f, y = 2f, z = 3f };
        Assert.IsNull(PlayerPose.FromJson("", 4, out var e1)); Assert.IsNotNull(e1);
        Assert.IsNull(PlayerPose.FromJson("{ not json", 4, out var e2)); Assert.IsNotNull(e2);
        Assert.IsNull(PlayerPose.FromJson(ok.ToJson(), 3, out var e3), "a v4 pose in a v3 world"); StringAssert.Contains("map version", e3);
        Assert.IsNull(PlayerPose.FromJson(ok.ToJson().Replace("\"version\": 1", "\"version\": 9"), 4, out var e4)); StringAssert.Contains("version", e4);
        var far = new PlayerPose { mapVersion = 4, x = 2e6f };
        Assert.IsNull(PlayerPose.FromJson(far.ToJson(), 4, out var e5), "off any world");
        var nan = new PlayerPose { mapVersion = 4, yaw = float.NaN };
        Assert.IsNull(PlayerPose.FromJson(nan.ToJson(), 4, out _), "NaN look");
    }
}
