// Assets/CoreEngine/Gameplay/PlayerPose.cs
//
// Where the player stood (gameplay step 4, task 93c1; plan P12): a small file
// beside player.json (`player_pose.json`), written by an interactive session
// with the inventory, read back only by a KEEP=1 (-keepdeltas) start so the
// session resumes where it ended. A fresh world (the default) ignores it and
// starts at the play start. A plain class -- no world access -- like Inventory.
//
// Map v3 and v4 share one delta folder, so the pose carries its map version
// and a pose from the other map is refused (it would stand in the other
// generator's rock).
using System;
using UnityEngine;

[Serializable]
public sealed class PlayerPose
{
    public const int Version = 1;
    public const string FileName = "player_pose.json";
    /// Metres; anything farther out is not on any world this engine builds.
    public const float MaxCoordM = 100000f;

    public int version = Version;
    public int mapVersion;
    public float x, y, z;        // feet centre, metres
    public float yaw, pitch;     // degrees
    public bool walk = true;     // walk mode (false = fly)

    public string ToJson() => JsonUtility.ToJson(this, true);

    /// Never throws. Empty, malformed, another version or map, or a
    /// non-finite / out-of-world number -> null with `error` set.
    public static PlayerPose FromJson(string json, int expectMapVersion, out string error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(json)) { error = "empty file"; return null; }
        PlayerPose p;
        try { p = JsonUtility.FromJson<PlayerPose>(json); }
        catch (Exception e) { error = "malformed JSON: " + e.Message; return null; }
        if (p == null) { error = "malformed JSON (null result)"; return null; }
        if (p.version != Version) { error = $"version {p.version}, expected {Version}"; return null; }
        if (p.mapVersion != expectMapVersion) { error = $"map version {p.mapVersion}, this world is {expectMapVersion}"; return null; }
        if (!Ok(p.x) || !Ok(p.y) || !Ok(p.z) || !float.IsFinite(p.yaw) || !float.IsFinite(p.pitch))
        { error = "position or look out of range"; return null; }
        return p;
    }

    private static bool Ok(float v) => float.IsFinite(v) && Math.Abs(v) <= MaxCoordM;
}
