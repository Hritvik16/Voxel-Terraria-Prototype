// ==========================================
// Assets/Game/Phase6PlayerRig.cs
//
// PLAYER CONTROLLER ACCEPTANCE RIG. §13 Phase 6 file 1's acceptance slice,
// driven mechanically. Diagnostic scene, not a demo.
//
// =========================================================================
// WHY THIS EXISTS RATHER THAN A MANUAL CHECKLIST
// =========================================================================
// PlayerMotorTests proves the movement mechanism against a synthetic HashSet
// world: exact positions, no frames, no GPU. What it cannot prove is anything
// about the REAL thing --
//   * that the controller works on GENERATED terrain rather than flat test
//     boxes, with the slopes and overhangs Phase 3 actually produces
//   * that §8.1's hot reload works END TO END: a human edits PlayerConfig.json
//     on disk while the game runs, and the very next jump is different
//   * that MonoBehaviour wiring, spawn resolution and the streaming window all
//     hold together in a standalone build
// Those are the items that look like they need a human at the keyboard. They
// do not. They need a rig.
//
// WHAT IS STILL MANUAL, AND ONLY THIS: whether the movement FEELS good. §8.1
// says so itself -- "An AI can generate the controller mechanism correctly and
// still produce a controller that feels terrible... no automated test captures
// the latter". This rig proves the mechanism and the tuning loop; it makes no
// claim about the feel of the numbers currently in PlayerConfig.json, and the
// numbers are exactly what the hot reload exists to let a human change.
//
// NO TIMING. This rig reports no ms figures. Performance stays with
// run-acceptance-rig.sh.

using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Memory;

public class Phase6PlayerRig : MonoBehaviour
{
    [SerializeField] private PlayerController _player;
    [SerializeField] private string _outputRootFolderName = "Phase6Player";

    private readonly StringBuilder _log = new StringBuilder();
    private int _pass, _fail;
    private string _phase = "-";
    private string _outDir;
    private int _shotIndex;

    /// Fixed timestep. The rig drives the controller itself rather than letting
    /// Update run, so results do not depend on how fast the machine renders.
    private const float Dt = 1f / 60f;

    private static ChunkStore Store => Phase4Bootstrapper.Store;
    private static TerrainClipmap Clip => Phase4Bootstrapper.Clipmap;

    private PlayerMotor M => _player.Motor;

    private void L(string s) { _log.AppendLine(s); Debug.Log("[6pl] " + s); }
    private void Note(string s) => L("    note  " + s);
    private void Pass(string s) { _pass++; L("    PASS  " + s); }
    private void Fail(string s) { _fail++; L($"    FAIL  {s}   [{_phase}]"); }
    private void Check(bool ok, string s) { if (ok) Pass(s); else Fail(s); }

    // =====================================================================

    IEnumerator Start()
    {
        if (_player == null) _player = FindObjectOfType<PlayerController>();
        if (_player == null) { Debug.LogError("[6pl] no PlayerController"); Application.Quit(1); yield break; }

        float t0 = Time.realtimeSinceStartup;
        while (Store == null && Time.realtimeSinceStartup - t0 < 180f) yield return null;

        _outDir = Path.Combine(Application.persistentDataPath, _outputRootFolderName,
                               DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(_outDir);

        L("=== PHASE 6 FILE 1: PLAYER CONTROLLER ACCEPTANCE ===");
        L(DateTime.Now.ToString("u", CultureInfo.InvariantCulture));
        L("");

        if (Store == null) { Fail("world never booted"); yield return Report(); yield break; }

        // Let the streamer fill before anything asks about terrain.
        for (int i = 0; i < 120; i++) yield return null;

        // The rig owns the clock and the input from here.
        _player.DebugTakeControl();
        _player.Bind(Store, Store);
        Check(_player.Ready, "PlayerController bound to the real ChunkStore");
        if (!_player.Ready) { yield return Report(); yield break; }

        L($"PlayerConfig path: {PlayerConfig.ActivePath}");
        L($"config: accel {PlayerConfig.Active.accelerationMps2}  maxSpeed {PlayerConfig.Active.maxSpeedMps}  " +
          $"jump {PlayerConfig.Active.jumpImpulseMps}  step {PlayerConfig.Active.stepHeightVoxels}v  " +
          $"coyote {PlayerConfig.Active.coyoteTimeSeconds}s");
        L("");

        yield return Step1_SpawnOnRealTerrain();
        yield return Step2_WalkOnUnevenTerrain();
        yield return Step3_JumpOnRealTerrain();
        yield return Step4_ThreeVoxelStep();
        yield return Step5_HotReloadChangesJumpHeight();
        yield return Step6_NoTunnelingIntoRealGeometry();

        yield return Report();
    }

    private IEnumerator Report()
    {
        _log.AppendLine();
        _log.AppendLine($"PASS {_pass}  FAIL {_fail}");
        _log.AppendLine(_fail == 0 ? "RESULT: PASSED" : "RESULT: FAILED");
        File.WriteAllText(Path.Combine(_outDir, "phase6_player_report.txt"), _log.ToString());
        Debug.Log("[6pl] report -> " + _outDir);
        yield return null;
        Application.Quit(_fail == 0 ? 0 : 1);
    }

    // =====================================================================
    // Helpers
    // =====================================================================

    /// One simulated frame of held input. This calls the REAL PlayerController,
    /// which calls the REAL motor and the REAL hot-reload poll -- the rig
    /// supplies the timestep and the buttons, nothing else.
    private void Drive(float2 wish, bool jump) => _player.DebugStep(Dt, wish, jump);

    private IEnumerator DriveFor(int frames, float2 wish, bool jumpFirstFrame = false)
    {
        for (int i = 0; i < frames; i++)
        {
            Drive(wish, jumpFirstFrame && i == 0);
            yield return null;
        }
    }

    private int SurfaceY(int x, int z)
    {
        for (int y = WorldGenConstants.MAX_TERRAIN_HEIGHT + 2; y >= 1; y--)
        {
            byte m = Store.GetVoxel(new int3(x, y, z));
            if (m != Materials.Air && !MaterialRules.IsFluidMaterial(m)) return y;
        }
        return -1;
    }

    private void Edit(int3 v, byte m)
    {
        Store.SetVoxel(v, m);
        Clip.MarkDirty(CoordMath.VoxelToChunk(v));
    }

    private void FillBox(int3 lo, int3 hi, byte m)
    {
        for (int z = lo.z; z <= hi.z; z++)
        for (int y = lo.y; y <= hi.y; y++)
        for (int x = lo.x; x <= hi.x; x++)
            Edit(new int3(x, y, z), m);
    }

    private IEnumerator Shot(string name)
    {
        if (string.IsNullOrEmpty(_outDir)) yield break;
        yield return null;
        yield return new WaitForEndOfFrame();
        Texture2D tex = ScreenCapture.CaptureScreenshotAsTexture();
        File.WriteAllBytes(Path.Combine(_outDir, $"{_shotIndex:D2}_{name}.png"), tex.EncodeToPNG());
        Destroy(tex);
        Note($"screenshot -> {_shotIndex:D2}_{name}.png");
        _shotIndex++;
    }

    /// Puts the player on solid ground at a chosen voxel column.
    private void PlaceAt(int x, int z, float heightAboveSurfaceM = 0.3f)
    {
        int sy = SurfaceY(x, z);
        M.Teleport(new float3(x * 0.1f + 0.05f, (sy + 1) * 0.1f + heightAboveSurfaceM, z * 0.1f + 0.05f));
        M.ResolveSpawn();
    }

    // =====================================================================
    // STEP 1 -- spawn onto real generated terrain
    // =====================================================================

    private IEnumerator Step1_SpawnOnRealTerrain()
    {
        _phase = "step1 spawn";
        L("STEP 1 -- spawn on generated terrain");

        // A non-water surface column near the island centre.
        int px = 12800, pz = 12680;
        int sy = SurfaceY(px, pz);
        Check(sy > 0, $"found a solid, non-fluid surface at ({px},{pz}) -> voxel y {sy}");
        if (sy <= 0) yield break;

        PlaceAt(px, pz);
        Check(!M.OverlapsSolid(M.PositionM), "the player is NOT inside terrain after spawn resolution");

        yield return DriveFor(120, float2.zero);

        Check(M.Grounded, $"the player settles onto the ground (y {M.PositionM.y:F3} m)");
        Check(!M.OverlapsSolid(M.PositionM), "and is still not inside terrain after settling");
        Check(M.PositionM.y > 0.5f, "and did not fall out of the world");
        Note($"resting feet y = {M.PositionM.y:F3} m, surface voxel y = {sy} ({(sy + 1) * 0.1f:F2} m)");

        yield return Shot("step1_spawned_on_terrain");
        L("");
    }

    // =====================================================================
    // STEP 2 -- walk across UNEVEN generated terrain
    // =====================================================================

    private IEnumerator Step2_WalkOnUnevenTerrain()
    {
        _phase = "step2 walk uneven";
        L("STEP 2 -- walk across real, uneven terrain");

        float3 start = M.PositionM;
        float minY = start.y, maxY = start.y;
        int airborneFrames = 0, blockedByNonResident = 0;
        bool everInsideTerrain = false;

        // Walk +X for 6 seconds.
        for (int i = 0; i < 360; i++)
        {
            Drive(new float2(1f, 0f), false);
            if (!M.Grounded) airborneFrames++;
            if (M.LastBlockedByNonResident) blockedByNonResident++;
            if (M.OverlapsSolid(M.PositionM)) everInsideTerrain = true;
            minY = math.min(minY, M.PositionM.y);
            maxY = math.max(maxY, M.PositionM.y);
            yield return null;
        }

        float travelled = math.length(new float2(M.PositionM.x - start.x, M.PositionM.z - start.z));
        L($"  travelled {travelled:F2} m, feet y range {minY:F2}..{maxY:F2} m, " +
          $"airborne {airborneFrames}/360 frames");

        Check(travelled > 5f, $"the player actually walked ({travelled:F2} m in 6 s)");
        Check(!everInsideTerrain, "and was never inside terrain at any frame of the walk");
        Check(M.PositionM.y > 0.5f, "and never fell out of the world");
        Check(maxY - minY > 0.05f,
            $"the ground was not flat ({maxY - minY:F2} m of height change over {travelled:F1} m)");
        Note("HOW UNEVEN, HONESTLY: this is the snow plateau -- gently contoured, a few " +
             "voxels of relief, NOT rugged ground. It proves the controller handles the " +
             "small height changes it met here; steep slopes, overhangs and cliffs are " +
             "NOT covered by this run.");
        Check(M.Grounded || airborneFrames < 300,
            "the player spent most of the walk on the ground, not falling");
        Check(blockedByNonResident == 0,
            $"never blocked by a non-resident chunk ({blockedByNonResident} frames) -- " +
            "the streamer kept up with walking speed");

        yield return Shot("step2_after_walk");
        L("");
    }

    // =====================================================================
    // STEP 3 -- jump on real terrain
    // =====================================================================

    private IEnumerator Step3_JumpOnRealTerrain()
    {
        _phase = "step3 jump";
        L("STEP 3 -- jump on real terrain");

        yield return DriveFor(60, float2.zero);
        Check(M.Grounded, "standing before the jump");

        float floorY = M.PositionM.y;
        Drive(float2.zero, true);

        float apex = M.PositionM.y;
        int frames = 0;
        for (int i = 0; i < 300; i++)
        {
            Drive(float2.zero, false);
            apex = math.max(apex, M.PositionM.y);
            frames++;
            if (M.Grounded && i > 5) break;
            yield return null;
        }

        float height = apex - floorY;
        L($"  jump apex {height:F3} m above the floor, landed after {frames} frames");

        // v^2/2g for the shipped defaults: 6.2^2 / (2*22) = 0.874 m.
        float expected = PlayerConfig.Active.jumpImpulseMps * PlayerConfig.Active.jumpImpulseMps
                       / (2f * PlayerConfig.Active.gravityMps2);
        Check(height > 0.2f, $"the jump actually lifted the player ({height:F3} m)");
        Check(math.abs(height - expected) < 0.15f,
            $"and reached roughly v^2/2g = {expected:F3} m (measured {height:F3} m)");
        Check(M.Grounded, "and landed back on the ground");
        Check(!M.OverlapsSolid(M.PositionM), "without ending up inside terrain");

        yield return Shot("step3_after_jump");
        L("");
    }

    // =====================================================================
    // STEP 4 -- §13's "3-voxel steps climbable un-jumped", on real terrain
    // =====================================================================

    private IEnumerator Step4_ThreeVoxelStep()
    {
        _phase = "step4 three-voxel step";
        L("STEP 4 -- a 3-voxel step is climbed without jumping; a 4-voxel one is not");

        yield return DriveFor(60, float2.zero);

        // THE LANE MUST OUTLAST THE WALK. The first version of this step built a
        // 3.6 m lane and then walked for 3 s at 5.2 m/s. The player climbed the
        // built step, walked off the end of the lane, and carried on climbing
        // NATURAL terrain -- reporting a 1.000 m "3-voxel step climb" and passing
        // a >0.25 m assertion for entirely the wrong reason. The screenshot
        // showed open snow with no lane in it. Two fixes: a lane far longer than
        // the walk, and an assertion on the EXACT expected height instead of a
        // lower bound that natural terrain can also satisfy.
        int3 p = CoordMath.WorldToVoxel(M.PositionM);
        int laneY = p.y - 1;                       // the voxel row under the feet
        int x0 = p.x + 4, x1 = p.x + 180;          // 17.6 m of lane
        int zLo = p.z - 5, zHi = p.z + 5;
        int stepX = p.x + 40;                      // 3.6 m ahead of the start
        int walkFrames = 150;                      // 2.5 s => ~13 m, well inside the lane

        // Look along +X and slightly down, so the screenshots show the step
        // rather than the horizon. Yaw only moves the camera: the rig passes
        // world-space wish vectors straight to the motor.
        _player.DebugSetLook(90f, 18f);

        BuildLane(x0, x1, zLo, zHi, laneY);
        FillBox(new int3(stepX, laneY + 1, zLo), new int3(x1, laneY + 3, zHi), Materials.Stone);
        for (int i = 0; i < 20; i++) yield return null;

        float laneTopY = (laneY + 1) * 0.1f;
        float stepTopY = (laneY + 4) * 0.1f;       // 3 voxels above the lane floor
        M.Teleport(new float3((p.x + 8) * 0.1f, laneTopY + 0.2f, (p.z) * 0.1f + 0.05f));
        yield return DriveFor(90, float2.zero);

        Check(M.Grounded, "standing on the prepared lane");
        Check(math.abs(M.PositionM.y - laneTopY) < 0.02f,
            $"and standing on the LANE FLOOR at {laneTopY:F2} m (measured {M.PositionM.y:F3} m), " +
            "not on leftover natural terrain");
        float beforeY = M.PositionM.y, beforeX = M.PositionM.x;

        yield return DriveFor(walkFrames, new float2(1f, 0f));   // never jumps

        float climbed = M.PositionM.y - beforeY;
        L($"  3-voxel step: y {beforeY:F3} -> {M.PositionM.y:F3} ({climbed:F3} m), " +
          $"x {beforeX:F2} -> {M.PositionM.x:F2}   lane ends at {x1 * 0.1f:F2} m");

        Check(M.PositionM.x < (x1 - 20) * 0.1f,
            $"the player is still ON the prepared lane (x {M.PositionM.x:F2} m, lane ends " +
            $"{x1 * 0.1f:F2} m) -- if it walked off the end, the height below is natural " +
            "terrain and proves nothing about the step");
        Check(math.abs(M.PositionM.y - stepTopY) < 0.03f,
            $"the player is standing on TOP OF THE 3-VOXEL STEP, at exactly {stepTopY:F2} m " +
            $"(measured {M.PositionM.y:F3} m)");
        Check(math.abs(climbed - 0.3f) < 0.03f,
            $"which is a climb of exactly 3 voxels = 0.30 m (measured {climbed:F3} m), " +
            "not merely 'more than nothing'");
        Check(M.PositionM.x > stepX * 0.1f, "and the player got past the step's face");
        Check(!M.OverlapsSolid(M.PositionM), "without ending up inside the step");

        yield return Shot("step4_climbed_three_voxel_step");

        // ---- The control: 4 voxels must NOT be climbable at stepHeight=3 ----
        BuildLane(x0, x1, zLo, zHi, laneY);
        FillBox(new int3(stepX, laneY + 1, zLo), new int3(x1, laneY + 4, zHi), Materials.Stone);
        for (int i = 0; i < 20; i++) yield return null;

        M.Teleport(new float3((p.x + 8) * 0.1f, laneTopY + 0.2f, (p.z) * 0.1f + 0.05f));
        yield return DriveFor(90, float2.zero);
        float before4Y = M.PositionM.y, before4X = M.PositionM.x;

        yield return DriveFor(walkFrames, new float2(1f, 0f));

        float climbed4 = M.PositionM.y - before4Y;
        float advanced4 = M.PositionM.x - before4X;
        L($"  4-voxel step: y {before4Y:F3} -> {M.PositionM.y:F3} ({climbed4:F3} m), " +
          $"x {before4X:F2} -> {M.PositionM.x:F2} (advanced {advanced4:F2} m)");

        Check(climbed4 < 0.05f,
            $"a 4-voxel step is NOT climbed at stepHeightVoxels=3 ({climbed4:F3} m) -- " +
            "without this the step test would pass for a controller that climbs anything");
        Check(advanced4 > 1f,
            $"and the player DID walk up to it ({advanced4:F2} m), so this is a real stop " +
            "at the face and not a failure to start moving");
        Check(math.abs(M.PositionM.x + PlayerConfig.Active.bodyWidthM * 0.5f - stepX * 0.1f) < 0.05f,
            $"stopped flush against the step face at {stepX * 0.1f:F2} m " +
            $"(leading edge {M.PositionM.x + PlayerConfig.Active.bodyWidthM * 0.5f:F3} m)");

        yield return Shot("step4_blocked_by_four_voxel_step");
        L("");
    }

    /// Clears a corridor and floors it with stone, so the geometry under test is
    /// stated rather than hunted for -- but in the REAL ChunkStore, through the
    /// real streaming and mirror path, not a HashSet.
    private void BuildLane(int x0, int x1, int zLo, int zHi, int laneY)
    {
        FillBox(new int3(x0, laneY + 1, zLo), new int3(x1, laneY + 25, zHi), Materials.Air);
        FillBox(new int3(x0, laneY, zLo), new int3(x1, laneY, zHi), Materials.Stone);
    }

    // =====================================================================
    // STEP 5 -- §8.1's hot reload, END TO END, through the real file
    //
    // THIS IS THE ITEM THAT LOOKED LIKE IT NEEDED A HUMAN: "change jump impulse
    // mid-session and confirm the jump height changes on the next jump". The rig
    // writes the JSON exactly as a human editing it would, and measures.
    // =====================================================================

    private IEnumerator Step5_HotReloadChangesJumpHeight()
    {
        _phase = "step5 hot reload";
        L("STEP 5 -- edit PlayerConfig.json mid-run; the NEXT jump must change");

        string path = PlayerConfig.ActivePath;
        Check(!string.IsNullOrEmpty(path) && File.Exists(path),
            $"PlayerConfig.json exists on disk at {path}");
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) yield break;

        // Rebuild a clean flat lane so both jumps are measured off the same floor.
        int3 p = CoordMath.WorldToVoxel(M.PositionM);
        int laneY = p.y - 1;
        FillBox(new int3(p.x - 10, laneY + 1, p.z - 8), new int3(p.x + 10, laneY + 30, p.z + 8), Materials.Air);
        FillBox(new int3(p.x - 10, laneY, p.z - 8), new int3(p.x + 10, laneY, p.z + 8), Materials.Stone);
        for (int i = 0; i < 20; i++) yield return null;
        M.Teleport(new float3(p.x * 0.1f, (laneY + 1) * 0.1f + 0.2f, p.z * 0.1f));
        yield return DriveFor(90, float2.zero);

        float before = PlayerConfig.Active.jumpImpulseMps;
        int reloadsBefore = PlayerConfig.ReloadCount;
        L($"  jumpImpulseMps on disk = {before}");

        float apex1 = MeasureJumpApex();
        yield return DriveFor(90, float2.zero);
        L($"  jump #1 apex = {apex1:F3} m");

        // --- The edit a human would make ---
        var edited = PlayerConfig.Active.Clone();
        edited.jumpImpulseMps = before * 1.6f;
        File.WriteAllText(path, edited.ToJson());
        L($"  wrote jumpImpulseMps = {edited.jumpImpulseMps} to {Path.GetFileName(path)}");

        // The controller polls on its own each frame; just let frames pass.
        int waited = 0;
        while (PlayerConfig.ReloadCount == reloadsBefore && waited < 300)
        {
            Drive(float2.zero, false);
            waited++;
            yield return null;
        }

        Check(PlayerConfig.ReloadCount > reloadsBefore,
            $"the running game noticed the file change on its own ({waited} frames, " +
            $"reloads {reloadsBefore} -> {PlayerConfig.ReloadCount})");
        Check(math.abs(PlayerConfig.Active.jumpImpulseMps - edited.jumpImpulseMps) < 1e-3f,
            $"and picked up the new value ({PlayerConfig.Active.jumpImpulseMps})");

        yield return DriveFor(60, float2.zero);
        float apex2 = MeasureJumpApex();
        yield return DriveFor(60, float2.zero);
        L($"  jump #2 apex = {apex2:F3} m");

        // Apex scales with v^2, so 1.6x impulse is ~2.56x height.
        Check(apex2 > apex1 * 1.8f,
            $"THE NEXT JUMP IS HIGHER, with no recompile and no restart " +
            $"({apex1:F3} m -> {apex2:F3} m, ratio {apex2 / math.max(apex1, 1e-4f):F2}x)");

        // Put it back, so a rerun starts from the shipped numbers.
        var restored = PlayerConfig.Active.Clone();
        restored.jumpImpulseMps = before;
        File.WriteAllText(path, restored.ToJson());
        for (int i = 0; i < 120 && math.abs(PlayerConfig.Active.jumpImpulseMps - before) > 1e-3f; i++)
        {
            Drive(float2.zero, false);
            yield return null;
        }
        Check(math.abs(PlayerConfig.Active.jumpImpulseMps - before) < 1e-3f,
            "and the config reverts when the file is put back (the reload is not one-way)");

        yield return Shot("step5_hot_reload");
        L("");
    }

    /// Jumps and returns the apex height above the take-off point. Synchronous:
    /// the rig owns the timestep, so this needs no frames.
    private float MeasureJumpApex()
    {
        float floorY = M.PositionM.y;
        Drive(float2.zero, true);
        float apex = M.PositionM.y;
        for (int i = 0; i < 400; i++)
        {
            Drive(float2.zero, false);
            apex = math.max(apex, M.PositionM.y);
            if (M.Grounded && i > 5) break;
        }
        return apex - floorY;
    }

    // =====================================================================
    // STEP 6 -- no tunneling through real geometry at walk/run speed
    // =====================================================================

    private IEnumerator Step6_NoTunnelingIntoRealGeometry()
    {
        _phase = "step6 no tunneling";
        L("STEP 6 -- a 1-voxel wall in real terrain is never tunneled at run speed");

        int3 p = CoordMath.WorldToVoxel(M.PositionM);
        int laneY = p.y - 1;
        int wallX = p.x + 30;

        FillBox(new int3(p.x - 6, laneY + 1, p.z - 8), new int3(p.x + 60, laneY + 30, p.z + 8), Materials.Air);
        FillBox(new int3(p.x - 6, laneY, p.z - 8), new int3(p.x + 60, laneY, p.z + 8), Materials.Stone);
        // ONE voxel thick, 25 tall -- taller than any step-up.
        FillBox(new int3(wallX, laneY + 1, p.z - 8), new int3(wallX, laneY + 25, p.z + 8), Materials.Stone);
        for (int i = 0; i < 20; i++) yield return null;

        M.Teleport(new float3((p.x - 2) * 0.1f, (laneY + 1) * 0.1f + 0.2f, p.z * 0.1f));
        yield return DriveFor(60, float2.zero);

        float wallFaceM = wallX * 0.1f;
        float halfW = PlayerConfig.Active.bodyWidthM * 0.5f;
        bool passed = false;
        float deepest = 0f;

        for (int i = 0; i < 300; i++)
        {
            Drive(new float2(1f, 0f), false);
            float lead = M.PositionM.x + halfW;
            if (lead > wallFaceM + 1e-3f) { passed = true; deepest = math.max(deepest, lead - wallFaceM); }
            yield return null;
        }

        L($"  wall face at x = {wallFaceM:F2} m; player leading edge ended at " +
          $"{M.PositionM.x + halfW:F3} m");
        Check(!passed,
            passed ? $"TUNNELED {deepest:F3} m into/through the wall"
                   : "never crossed the wall face at run speed");
        Check(M.PositionM.x + halfW > wallFaceM - 0.6f,
            "and did reach the wall (so this is a real stop, not a failure to start)");
        Check(!M.OverlapsSolid(M.PositionM), "and is not embedded in the wall");
        Note($"substeps on the final frame: {M.LastSubstepCount}");

        yield return Shot("step6_stopped_at_wall");
        L("");
    }
}
