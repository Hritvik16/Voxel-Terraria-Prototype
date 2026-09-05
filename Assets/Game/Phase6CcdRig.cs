// ==========================================
// Assets/Game/Phase6CcdRig.cs
//
// SWEPT CCD ACCEPTANCE RIG. §13 Phase 6 file 2's acceptance slice, driven
// mechanically. Diagnostic scene, not a demo.
//
// §13's acceptance line for this file, clause by clause:
//   "0.2m wall, grapple in at 60 m/s: stop at the face, 20/20.
//    Disable the sweep -> confirm phasing (test isn't vacuous).
//    Re-enable, force a missed ray -> depenetration catches it, one corrected
//    frame, never a tunnel."
// plus §8.2's own Phase 6 test:
//   "fly through a 3-voxel water sheet at 60 m/s -- splash fires, drag applies,
//    no phase-through."
//
// SweptCCDTests already proves all of that against a synthetic Dictionary world,
// with exact geometry and mutation checks. What it CANNOT prove, and what this
// rig is for:
//   * the same maths against the REAL ChunkStore, through real chunk/brick
//     addressing and a real streaming window, rather than a hash lookup
//   * in an IL2CPP release build rather than the editor's Mono
//   * with geometry written through the real edit path, so a wall the rig built
//     is a wall the renderer and the collision agree about
// A rig PASS here and an EditMode PASS are different claims; both are needed.
//
// NO TIMING. Performance stays with run-acceptance-rig.sh.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Memory;

public class Phase6CcdRig : MonoBehaviour
{
    [SerializeField] private string _outputRootFolderName = "Phase6Ccd";

    private readonly StringBuilder _log = new StringBuilder();
    private int _pass, _fail;
    private string _phase = "-";
    private string _outDir;
    private int _shotIndex;

    private static ChunkStore Store => Phase4Bootstrapper.Store;
    private static TerrainClipmap Clip => Phase4Bootstrapper.Clipmap;

    private SweptCCD _ccd;

    // The body under test: the same shape PlayerConfig ships.
    private const float W = 0.6f, H = 1.8f;
    /// 60 m/s at 60 Hz -- §13's grapple speed, 1.0 m in a single frame.
    private const float FrameMoveM = 1.0f;

    private void L(string s) { _log.AppendLine(s); Debug.Log("[6ccd] " + s); }
    private void Note(string s) => L("    note  " + s);
    private void Pass(string s) { _pass++; L("    PASS  " + s); }
    private void Fail(string s) { _fail++; L($"    FAIL  {s}   [{_phase}]"); }
    private void Check(bool ok, string s) { if (ok) Pass(s); else Fail(s); }

    // =====================================================================

    IEnumerator Start()
    {
        float t0 = Time.realtimeSinceStartup;
        while (Store == null && Time.realtimeSinceStartup - t0 < 180f) yield return null;

        _outDir = Path.Combine(Application.persistentDataPath, _outputRootFolderName,
                               DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(_outDir);

        L("=== PHASE 6 FILE 2: SWEPT CCD ACCEPTANCE ===");
        L(DateTime.Now.ToString("u", CultureInfo.InvariantCulture));
        L("");

        if (Store == null) { Fail("world never booted"); yield return Report(); yield break; }
        for (int i = 0; i < 120; i++) yield return null;      // let the window fill

        _ccd = new SweptCCD(Store, Store);
        L($"body {W} x {H} m, frame move {FrameMoveM} m (= 60 m/s at 60 Hz)");
        L($"sweep threshold {SweptCCD.SweepThresholdMps} m/s, clamp {SweptCCD.ClampedSpeedMps} m/s " +
          $"after {SweptCCD.StaleFramesBeforeClamp} stale frames");
        L("");

        yield return Step1_GrappleIntoA02mWall();
        yield return Step2_EndpointOnlyPhasesThrough();
        yield return Step3_WaterSheetAt60Mps();
        yield return Step4_MissedRayCaughtByDepenetration();

        yield return Report();
    }

    private IEnumerator Report()
    {
        _log.AppendLine();
        _log.AppendLine($"PASS {_pass}  FAIL {_fail}");
        _log.AppendLine(_fail == 0 ? "RESULT: PASSED" : "RESULT: FAILED");
        File.WriteAllText(Path.Combine(_outDir, "phase6_ccd_report.txt"), _log.ToString());
        Debug.Log("[6ccd] report -> " + _outDir);
        yield return null;
        Application.Quit(_fail == 0 ? 0 : 1);
    }

    // =====================================================================
    // Helpers
    // =====================================================================

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

    private int SurfaceY(int x, int z)
    {
        for (int y = WorldGenConstants.MAX_TERRAIN_HEIGHT + 2; y >= 1; y--)
        {
            byte m = Store.GetVoxel(new int3(x, y, z));
            if (m != Materials.Air && !MaterialRules.IsFluidMaterial(m)) return y;
        }
        return -1;
    }

    private IEnumerator Shot(string name, float3 eyeM, float3 lookAtM)
    {
        Camera cam = Camera.main;
        if (cam != null)
        {
            cam.transform.position = new Vector3(eyeM.x, eyeM.y, eyeM.z);
            Vector3 d = new Vector3(lookAtM.x, lookAtM.y, lookAtM.z) - cam.transform.position;
            if (d.sqrMagnitude > 1e-6f) cam.transform.rotation = Quaternion.LookRotation(d.normalized, Vector3.up);
        }
        yield return null;
        yield return new WaitForEndOfFrame();
        Texture2D tex = ScreenCapture.CaptureScreenshotAsTexture();
        File.WriteAllBytes(Path.Combine(_outDir, $"{_shotIndex:D2}_{name}.png"), tex.EncodeToPNG());
        Destroy(tex);
        Note($"screenshot -> {_shotIndex:D2}_{name}.png");
        _shotIndex++;
    }

    /// Clears a corridor in real terrain and returns the voxel Y of its floor.
    private int BuildCorridor(int x0, int x1, int z0, int z1, out int floorY)
    {
        int sy = SurfaceY((x0 + x1) / 2, (z0 + z1) / 2);
        floorY = sy;
        FillBox(new int3(x0, sy + 1, z0), new int3(x1, sy + 30, z1), Materials.Air);
        FillBox(new int3(x0, sy, z0), new int3(x1, sy, z1), Materials.Stone);
        return sy;
    }

    // =====================================================================
    // STEP 1 -- §13: "0.2m wall, grapple in at 60 m/s: stop at the face, 20/20"
    // =====================================================================

    private IEnumerator Step1_GrappleIntoA02mWall()
    {
        _phase = "step1 0.2m wall at 60 m/s";
        L("STEP 1 -- 0.2 m wall, grapple in at 60 m/s, 20 trials");

        int x0 = 12600, x1 = 12760, z0 = 12670, z1 = 12690;
        int floorY;
        BuildCorridor(x0, x1, z0, z1, out floorY);

        // 0.2 m = exactly 2 voxels, per §13.
        int wallX = 12700;
        FillBox(new int3(wallX, floorY + 1, z0), new int3(wallX + 1, floorY + 25, z1), Materials.Stone);
        for (int i = 0; i < 20; i++) yield return null;

        float wallFaceM = wallX * 0.1f;
        float bodyY = (floorY + 1) * 0.1f + 0.05f;
        float zM = ((z0 + z1) / 2) * 0.1f;
        L($"  wall face at x = {wallFaceM:F2} m, 2 voxels thick, floor voxel y = {floorY}");

        int stopped = 0;
        var problems = new List<string>();

        for (int trial = 0; trial < 20; trial++)
        {
            // The leading face must start clear of the wall and end past it
            // after one 1.0 m frame: wallFace - 1.3 < startX < wallFace - 0.3.
            float startX = wallFaceM - 1.25f + trial * 0.045f;
            float3 from = new float3(startX, bodyY, zM);
            float3 to = from + new float3(FrameMoveM, 0f, 0f);

            CCDResult r = _ccd.Sweep(from, to, W, H);
            if (!r.HitSolid) { problems.Add($"trial {trial}: no hit from x={startX:F3}"); continue; }

            float3 clamped = SweptCCD.ClampToHit(from, to, r);
            float leading = clamped.x + W * 0.5f;
            if (leading > wallFaceM + 1e-3f)
                problems.Add($"trial {trial}: leading edge {leading:F4} past the face {wallFaceM:F2}");
            else if (VoxelCollision.OverlapsSolid(Store, Store, clamped, W, H))
                problems.Add($"trial {trial}: clamped position is inside the wall");
            else
                stopped++;
        }

        L($"  {stopped}/20 stopped cleanly at the face");
        Check(problems.Count == 0, problems.Count == 0
            ? "no trial passed into or through the wall"
            : "trials failed: " + string.Join(" | ", problems));
        Check(stopped == 20, $"all 20 grapple approaches stop at the wall face ({stopped}/20)");
        Note($"sweeps run {_ccd.SweepsRun}, of which hit solid {_ccd.SweepsThatHitSolid}");

        yield return Shot("step1_grapple_wall",
            new float3(wallFaceM - 4f, bodyY + 2.5f, zM - 3.5f),
            new float3(wallFaceM, bodyY + 0.9f, zM));
        L("");
    }

    // =====================================================================
    // STEP 2 -- §13: "Disable the sweep -> confirm phasing (test isn't vacuous)"
    // =====================================================================

    private IEnumerator Step2_EndpointOnlyPhasesThrough()
    {
        _phase = "step2 vacuity control";
        L("STEP 2 -- with the sweep disabled, the SAME approach phases through");

        int x0 = 12600, x1 = 12760, z0 = 12710, z1 = 12730;
        int floorY;
        BuildCorridor(x0, x1, z0, z1, out floorY);
        int wallX = 12700;
        FillBox(new int3(wallX, floorY + 1, z0), new int3(wallX + 1, floorY + 25, z1), Materials.Stone);
        for (int i = 0; i < 20; i++) yield return null;

        float wallFaceM = wallX * 0.1f;
        float bodyY = (floorY + 1) * 0.1f + 0.05f;
        float zM = ((z0 + z1) / 2) * 0.1f;

        // "Disabling the sweep" is exactly the endpoint-only test it replaces.
        // CLEARANCE IS GEOMETRY, NOT TASTE: the body is 0.6 m wide and the wall
        // is 0.2 m thick, so a 1.0 m frame only clears it fully when the feet
        // start in (face-0.5, face-0.3). -0.55 put the destination AABB back
        // across the wall's far edge, and the rig caught it.
        float3 from = new float3(wallFaceM - 0.4f, bodyY, zM);
        float3 to = from + new float3(FrameMoveM, 0f, 0f);

        bool startClear = !VoxelCollision.OverlapsSolid(Store, Store, from, W, H);
        bool endClear = !VoxelCollision.OverlapsSolid(Store, Store, to, W, H);

        Check(startClear, "the start position is clear of the wall");
        Check(endClear,
            "and so is the DESTINATION -- so an endpoint-only check sees no collision at " +
            "all and the body lands on the far side. This is the phase-through the swept " +
            "pass exists to prevent, and it confirms step 1 is not vacuous.");

        CCDResult r = _ccd.Sweep(from, to, W, H);
        Check(r.HitSolid, "the swept pass, on the same move, DOES see the wall");
        Check(SweptCCD.ClampToHit(from, to, r).x + W * 0.5f <= wallFaceM + 1e-3f,
            "and stops the body at its face");

        L($"  endpoint-only: start clear={startClear}, end clear={endClear} " +
          $"(landing at x={to.x:F2} m, beyond the {wallFaceM:F2} m face)");
        L("");
    }

    // =====================================================================
    // STEP 3 -- §8.2: "fly through a 3-voxel water sheet at 60 m/s"
    // =====================================================================

    private IEnumerator Step3_WaterSheetAt60Mps()
    {
        _phase = "step3 water sheet";
        L("STEP 3 -- 3-voxel water sheet at 60 m/s: noticed, though both endpoints are dry");

        int x0 = 12600, x1 = 12760, z0 = 12750, z1 = 12770;
        int floorY;
        BuildCorridor(x0, x1, z0, z1, out floorY);

        int sheetX = 12700;
        FillBox(new int3(sheetX, floorY + 1, z0), new int3(sheetX + 2, floorY + 25, z1), Materials.Water);
        for (int i = 0; i < 20; i++) yield return null;

        float sheetFaceM = sheetX * 0.1f;
        float bodyY = (floorY + 1) * 0.1f + 0.05f;
        float zM = ((z0 + z1) / 2) * 0.1f;

        // THE CLEARANCE WINDOW HERE IS ONLY 0.1 m WIDE, and that is geometry,
        // not sloppiness: a 1.0 m frame (60 m/s at 60 Hz) must carry a 0.6 m
        // body from fully-clear-before to fully-clear-after a 0.3 m sheet, which
        // needs 0.9 m minimum. -0.35 centres the body in what is left, giving
        // half a voxel of margin at each end. -0.4 put the trailing edge within
        // float rounding of the sheet's last voxel at ~1270 m world coordinates,
        // and the real dryness check caught it where OverlapsSolid could not.
        float3 from = new float3(sheetFaceM - 0.35f, bodyY, zM);
        float3 to = from + new float3(FrameMoveM, 0f, 0f);

        // Dryness needs AnyFluidInBody: OverlapsSolid ignores fluid by design
        // (rule 2), so asserting dryness with it asserts nothing.
        byte fs, fe;
        Check(!VoxelCollision.AnyFluidInBody(Store, Store, from, W, H, out fs),
            "the start is genuinely DRY");
        Check(!VoxelCollision.AnyFluidInBody(Store, Store, to, W, H, out fe),
            "and the destination is genuinely DRY -- a probe at either end sees nothing");

        CCDResult r = _ccd.Sweep(from, to, W, H);

        L($"  fluid traversed {r.FluidTraversedDistanceM:F3} m, primary material " +
          $"{r.PrimaryFluidMaterial}, hitSolid {r.HitSolid}");

        Check(!r.HitSolid, "water does not clamp the sweep (§8.2: clamps only on solid)");
        Check(r.FluidTraversedDistanceM > 0f,
            $"THE SPLASH FIRES: fluidTraversedDistance = {r.FluidTraversedDistanceM:F3} m > 0, " +
            "even though both endpoints are dry -- no silent phase-through");
        Check(r.PrimaryFluidMaterial == Materials.Water,
            $"and the primary fluid material is water ({r.PrimaryFluidMaterial})");
        Check(math.abs(r.FluidTraversedDistanceM - 0.3f) < 0.06f,
            $"measuring roughly the sheet's 3-voxel thickness ({r.FluidTraversedDistanceM:F3} m vs 0.30)");

        yield return Shot("step3_water_sheet",
            new float3(sheetFaceM - 4f, bodyY + 2.5f, zM - 3.5f),
            new float3(sheetFaceM, bodyY + 0.9f, zM));
        L("");
    }

    // =====================================================================
    // STEP 4 -- §13: "force a missed ray -> depenetration catches it, one
    //           corrected frame, never a tunnel"
    // =====================================================================

    private IEnumerator Step4_MissedRayCaughtByDepenetration()
    {
        _phase = "step4 depenetration backstop";
        L("STEP 4 -- a body forced inside geometry is recovered in ONE frame");

        int x0 = 12600, x1 = 12760, z0 = 12790, z1 = 12810;
        int floorY;
        BuildCorridor(x0, x1, z0, z1, out floorY);

        int blockX = 12700;
        FillBox(new int3(blockX, floorY + 1, z0), new int3(blockX + 4, floorY + 25, z1), Materials.Stone);
        for (int i = 0; i < 20; i++) yield return null;

        float bodyY = (floorY + 1) * 0.1f + 0.05f;
        float zM = ((z0 + z1) / 2) * 0.1f;

        // Force the failure the backstop exists for: place the body INSIDE the
        // block, as a missed ray would have left it, and give it the velocity it
        // was carrying.
        float3 lastFree = new float3(blockX * 0.1f - 0.8f, bodyY, zM);
        float3 pos = new float3((blockX + 2) * 0.1f, bodyY, zM);
        float3 vel = new float3(60f, 0f, 0f);

        Check(!VoxelCollision.OverlapsSolid(Store, Store, lastFree, W, H),
            "the last known free position really is free");
        Check(VoxelCollision.OverlapsSolid(Store, Store, pos, W, H),
            "and the forced position really is inside the block");

        long before = _ccd.DepenetrationsApplied;
        bool corrected = _ccd.Depenetrate(lastFree, ref pos, ref vel, W, H);

        Check(corrected, "the depenetration backstop fires");
        Check(_ccd.DepenetrationsApplied == before + 1,
            $"exactly ONE corrected frame ({_ccd.DepenetrationsApplied - before})");
        Check(!VoxelCollision.OverlapsSolid(Store, Store, pos, W, H),
            $"and the body ends OUT of the geometry at x={pos.x:F3} m -- never a silent " +
            "permanent tunnel");
        Check(math.abs(vel.x) < 1e-4f,
            $"with the offending velocity component zeroed (vx={vel.x:F4})");
        Check(pos.x <= blockX * 0.1f + 1e-2f,
            $"pushed back to the near side of the block, not shoved out the far side " +
            $"(x={pos.x:F3} m vs face {blockX * 0.1f:F2} m)");
        Note($"binary-search iterations: {_ccd.LastDepenetrationIterations}");

        yield return Shot("step4_depenetration",
            new float3(blockX * 0.1f - 4f, bodyY + 2.5f, zM - 3.5f),
            new float3(blockX * 0.1f, bodyY + 0.9f, zM));
        L("");
    }
}
