// ==========================================
// Assets/CoreEngine/Gameplay/PlayerConfig.cs
//
// §8.1's "Controls as data + hot reload", which that section calls "the single
// highest-value tool for tuning game feel":
//
//   "the player controller's feel parameters -- acceleration, max speed, jump
//    impulse, air control, friction, step height, coyote time -- live as
//    tunable numbers in PlayerConfig (JSON), NOT hardcoded in the controller
//    logic. A [RuntimeInitializeOnLoadMethod] reloads PlayerConfig from disk at
//    runtime, so you tweak jump height and acceleration WHILE PLAYING, without
//    recompiling."
//
// The reason given there is worth repeating, because it constrains the design:
// "An AI can generate the controller mechanism correctly and still produce a
// controller that feels terrible; feel is found by iterating numbers in real
// time, not by regenerating code." So the loop has to be instant and it has to
// be safe -- a typo in the JSON while the game is running must not crash it or
// teleport the player through the floor. Hence Validate() below.
//
// WHERE THE FILE LIVES: Application.persistentDataPath/PlayerConfig.json,
// written with defaults on first run so there is always something to edit. On
// macOS that is
//   ~/Library/Application Support/DefaultCompany/<product>/PlayerConfig.json
// The path is printed once at startup so it is findable without guessing.
//
// TESTABILITY. Parse and Validate are pure static functions over a string --
// no file IO, no Unity lifecycle -- so EditMode can prove the malformed-input
// behaviour that matters most, without a scene. The file-watching half is
// proven end-to-end by Phase6PlayerRig, which rewrites the JSON mid-run and
// asserts the live controller picks it up.

using System;
using System.Globalization;
using System.IO;
using UnityEngine;

[Serializable]
public sealed class PlayerConfig
{
    // =====================================================================
    // THE FEEL PARAMETERS -- §8.1's named list, in its order.
    // These are the numbers to tune. Nothing in PlayerMotor hardcodes any of
    // them; if you find a movement constant living in the motor instead of
    // here, that is a bug against §8.1, not a shortcut.
    // =====================================================================

    /// Ground acceleration toward the input direction, m/s^2.
    public float accelerationMps2 = 55f;

    /// Horizontal speed cap, m/s. Walk/run; the grapple (§13, 60 m/s) is not
    /// this system and is not bounded by this.
    public float maxSpeedMps = 5.2f;

    /// Upward velocity applied on jump, m/s. Apex height is v^2 / (2g), so with
    /// the defaults here that is 6.2^2 / (2*22) = 0.87 m -- a bit under 9 voxels.
    public float jumpImpulseMps = 6.2f;

    /// Fraction of ground acceleration that applies while airborne, 0..1.
    public float airControl = 0.35f;

    /// Horizontal deceleration applied when there is no input, m/s^2. Ground
    /// only -- air keeps its momentum, which is what airControl is for.
    public float frictionMps2 = 60f;

    /// §13's acceptance number: "3-voxel steps climbable un-jumped". In VOXELS,
    /// not metres, because the world is voxels and the acceptance test is
    /// phrased in them. 3 voxels = 0.3 m.
    public int stepHeightVoxels = 3;

    /// Grace period after walking off an edge during which a jump still works.
    public float coyoteTimeSeconds = 0.12f;

    // =====================================================================
    // NOT "feel" in §8.1's sense, but the motor cannot run without them. Kept
    // here rather than in the motor so that everything the motor reads comes
    // from one place and a tuner can see the whole picture.
    // =====================================================================

    /// Downward acceleration, m/s^2. Deliberately well above real gravity;
    /// platformers universally are.
    public float gravityMps2 = 22f;

    /// Terminal fall speed, m/s. Also the guarantee that a long fall cannot
    /// outrun the substep budget below.
    public float terminalSpeedMps = 55f;

    /// Player AABB footprint (x and z) and height, metres.
    public float bodyWidthM = 0.6f;
    public float bodyHeightM = 1.8f;

    /// ANTI-TUNNELING. No single collision substep advances further than this
    /// many voxels, so the probes cannot step over a thin wall. At the defaults
    /// (0.5 voxel = 5 cm) terminal velocity costs 18 substeps in a 60 Hz frame.
    /// This is what makes "no tunneling at walk/run speeds" true by
    /// construction rather than by hoping the frame rate holds up. It is NOT
    /// the §8.2 swept CCD -- that is a separate file for the 60 m/s case.
    public float maxSubstepVoxels = 0.5f;

    // =====================================================================
    // Validation
    // =====================================================================

    /// Clamps every field into a range the motor can actually run with, and
    /// reports what it had to change.
    ///
    /// WHY THIS IS NOT OPTIONAL: this config is edited BY HAND, WHILE THE GAME
    /// IS RUNNING -- that is the entire point of §8.1's hot reload. A dropped
    /// minus sign or a stray zero arrives in a live frame. Unvalidated, a
    /// negative bodyHeightM inverts the AABB and the player falls through the
    /// world; a zero maxSubstepVoxels divides by zero and hangs the frame; a
    /// NaN anywhere propagates into the position and never comes back out.
    /// None of those are acceptable outcomes for a typo in a tuning file.
    public string Validate()
    {
        var notes = new System.Text.StringBuilder();

        accelerationMps2 = Fix(accelerationMps2, 0f, 10000f, 55f, nameof(accelerationMps2), notes);
        maxSpeedMps      = Fix(maxSpeedMps, 0.01f, 1000f, 5.2f, nameof(maxSpeedMps), notes);
        jumpImpulseMps   = Fix(jumpImpulseMps, 0f, 1000f, 6.2f, nameof(jumpImpulseMps), notes);
        airControl       = Fix(airControl, 0f, 1f, 0.35f, nameof(airControl), notes);
        frictionMps2     = Fix(frictionMps2, 0f, 10000f, 60f, nameof(frictionMps2), notes);
        coyoteTimeSeconds = Fix(coyoteTimeSeconds, 0f, 2f, 0.12f, nameof(coyoteTimeSeconds), notes);
        gravityMps2      = Fix(gravityMps2, 0.01f, 1000f, 22f, nameof(gravityMps2), notes);
        terminalSpeedMps = Fix(terminalSpeedMps, 0.1f, 10000f, 55f, nameof(terminalSpeedMps), notes);
        bodyWidthM       = Fix(bodyWidthM, 0.05f, 10f, 0.6f, nameof(bodyWidthM), notes);
        bodyHeightM      = Fix(bodyHeightM, 0.2f, 20f, 1.8f, nameof(bodyHeightM), notes);
        maxSubstepVoxels = Fix(maxSubstepVoxels, 0.05f, 4f, 0.5f, nameof(maxSubstepVoxels), notes);

        if (stepHeightVoxels < 0) { notes.Append($"{nameof(stepHeightVoxels)} {stepHeightVoxels}->0; "); stepHeightVoxels = 0; }
        if (stepHeightVoxels > 64) { notes.Append($"{nameof(stepHeightVoxels)} {stepHeightVoxels}->64; "); stepHeightVoxels = 64; }

        // A step taller than the body would let the player climb a cliff by
        // walking into it -- and, worse, step INTO a ceiling. Cap it at the
        // body height rather than trusting the tuner.
        int maxStep = Mathf.Max(0, Mathf.FloorToInt(bodyHeightM * 10f) - 1);
        if (stepHeightVoxels > maxStep)
        {
            notes.Append($"{nameof(stepHeightVoxels)} {stepHeightVoxels}->{maxStep} (capped to body height); ");
            stepHeightVoxels = maxStep;
        }

        return notes.Length == 0 ? null : notes.ToString().TrimEnd();
    }

    private static float Fix(float v, float lo, float hi, float fallback, string name,
                             System.Text.StringBuilder notes)
    {
        if (float.IsNaN(v) || float.IsInfinity(v))
        {
            notes.Append($"{name} not finite -> {fallback.ToString(CultureInfo.InvariantCulture)}; ");
            return fallback;
        }
        if (v < lo) { notes.Append($"{name} {v.ToString(CultureInfo.InvariantCulture)}->{lo.ToString(CultureInfo.InvariantCulture)}; "); return lo; }
        if (v > hi) { notes.Append($"{name} {v.ToString(CultureInfo.InvariantCulture)}->{hi.ToString(CultureInfo.InvariantCulture)}; "); return hi; }
        return v;
    }

    public PlayerConfig Clone() => (PlayerConfig)MemberwiseClone();

    // =====================================================================
    // Parsing -- pure, so EditMode can prove the failure behaviour
    // =====================================================================

    /// Parses `json`, validates it, and returns it. On ANY failure returns
    /// `fallback` unchanged and sets `error`.
    ///
    /// Returning the fallback rather than defaults is deliberate: a half-written
    /// file (the editor saving while the game reads) must leave the player
    /// moving exactly as they were, not snap them back to shipped defaults
    /// mid-jump. The next successful read picks up the finished edit.
    public static PlayerConfig Parse(string json, PlayerConfig fallback, out string error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(json)) { error = "empty file"; return fallback; }

        PlayerConfig parsed;
        try
        {
            parsed = JsonUtility.FromJson<PlayerConfig>(json);
        }
        catch (Exception e)
        {
            error = "malformed JSON: " + e.Message;
            return fallback;
        }
        if (parsed == null) { error = "malformed JSON (null result)"; return fallback; }

        string clamped = parsed.Validate();
        if (clamped != null) error = "clamped: " + clamped;   // not fatal; the values are usable
        return parsed;
    }

    public string ToJson() => JsonUtility.ToJson(this, true);

    // =====================================================================
    // The live instance + hot reload
    // =====================================================================

    private static PlayerConfig _active;
    private static string _path;
    private static DateTime _lastWriteUtc;
    private static long _lastLength = -1;

    /// The config every PlayerController reads. Never null after startup.
    public static PlayerConfig Active => _active ?? (_active = new PlayerConfig());

    /// Where the JSON lives. Empty until Initialize has run.
    public static string ActivePath => _path ?? string.Empty;

    /// Bumped on every successful reload. The rig asserts on this rather than
    /// sleeping and hoping.
    public static int ReloadCount { get; private set; }

    /// The most recent parse/clamp problem, or null. Surfaced so a typo shows
    /// up as a message instead of as mysteriously wrong movement.
    public static string LastError { get; private set; }

    /// §8.1's named mechanism. BeforeSceneLoad so the config is live before any
    /// PlayerController's Start runs and reads it.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    public static void Initialize()
    {
        try
        {
            _path = Path.Combine(Application.persistentDataPath, "PlayerConfig.json");
            if (!File.Exists(_path))
            {
                _active = new PlayerConfig();
                Directory.CreateDirectory(Path.GetDirectoryName(_path));
                File.WriteAllText(_path, _active.ToJson());
                Debug.Log($"[PlayerConfig] wrote defaults -> {_path}");
            }
            else
            {
                _active = new PlayerConfig();
                ReloadNow();
            }
            StampFile();
            Debug.Log($"[PlayerConfig] tune live by editing {_path}");
        }
        catch (Exception e)
        {
            // Never let config IO stop the game starting.
            _active = new PlayerConfig();
            LastError = "init failed: " + e.Message;
            Debug.LogWarning("[PlayerConfig] " + LastError);
        }
    }

    /// Cheap per-frame check: reload only when the file's timestamp or length
    /// actually moved. Polling beats FileSystemWatcher here -- the watcher is
    /// notoriously unreliable across editors and network volumes on macOS, and
    /// two stat() calls a frame is nothing next to a terrain probe.
    /// Returns true if a reload happened.
    public static bool ReloadIfChanged()
    {
        if (string.IsNullOrEmpty(_path)) return false;
        try
        {
            var info = new FileInfo(_path);
            if (!info.Exists) return false;
            if (info.LastWriteTimeUtc == _lastWriteUtc && info.Length == _lastLength) return false;
            StampFile();
            return ReloadNow();
        }
        catch (Exception e)
        {
            LastError = "stat failed: " + e.Message;
            return false;
        }
    }

    private static bool ReloadNow()
    {
        try
        {
            string json = File.ReadAllText(_path);
            string err;
            PlayerConfig next = Parse(json, Active, out err);
            LastError = err;
            if (!ReferenceEquals(next, Active))
            {
                _active = next;
                ReloadCount++;
                Debug.Log($"[PlayerConfig] reloaded (#{ReloadCount})" + (err != null ? " -- " + err : ""));
                return true;
            }
            return false;
        }
        catch (Exception e)
        {
            // A read that lands mid-write throws; the next poll will get it.
            LastError = "read failed: " + e.Message;
            return false;
        }
    }

    private static void StampFile()
    {
        try
        {
            var info = new FileInfo(_path);
            if (!info.Exists) return;
            _lastWriteUtc = info.LastWriteTimeUtc;
            _lastLength = info.Length;
        }
        catch { /* stamping is best-effort */ }
    }

    /// Test/rig seam: install a config directly, bypassing disk.
    public static void DebugSetActive(PlayerConfig c)
    {
        if (c == null) return;
        c.Validate();
        _active = c;
        ReloadCount++;
    }
}
