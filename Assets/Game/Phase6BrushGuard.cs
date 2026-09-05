// ==========================================
// Assets/Game/Phase6BrushGuard.cs
//
// BRUSH-GUARD END-TO-END RIG. Diagnostic scene, not a demo.
//
// =========================================================================
// THE GAP THIS EXISTS TO CLOSE
// =========================================================================
// The fix in 19b8ac6 ("Refuse a fluid brush placed outside the CA arena")
// stopped the Playground painting mobile materials where the CA can never
// simulate them -- the frozen sand column and floating water trail in bug.png.
//
// That fix shipped correctness-proven at the UNIT level only:
// FluidRegionBrushGuardTests pins the predicate (and is mutation-checked), but
// it cannot prove the Playground actually CALLS it. Playground lives in
// Assembly-CSharp; a Unity asmdef assembly cannot reference the predefined
// assemblies, and CoreEngine.Tests additionally sets overrideReferences:true.
// So the scene wiring was untestable from EditMode by construction, and the
// commit said so.
//
// This rig closes that gap the same way Phase5dStreamFluid closed the "the
// camera never captured anything" gap: drive the REAL MonoBehaviour, in a real
// standalone build, with no human input, and assert on what actually happened
// to ChunkStore.
//
// WHAT MAKES THIS A REAL TEST AND NOT A PARALLEL COPY. The rig calls
// Playground.TryPaintBrush -- the exact method HandleMouse calls on RMB. There
// is one implementation and two callers. A PASS here is a statement about what
// right-click does, not about a re-implementation of it.
//
// HOW SMALL THE ARENA IS, and why this bug was so easy to hit: 64 voxels is
// 6.4 m across, so "outside the arena" starts about three metres from its
// centre. bug.png's placement was 37 voxels (3.7 m) past the far X face. No
// teleport, no streaming hazard -- ordinary flying puts you outside it.
//
// TIMING: this rig reports NO performance numbers. It is a correctness rig.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Memory;
using VoxelEngine.Simulation;

public class Phase6BrushGuard : MonoBehaviour
{
    [SerializeField] private Playground _playground;
    [SerializeField] private string _outputRootFolderName = "Phase6BrushGuard";

    private readonly StringBuilder _log = new StringBuilder();
    private int _pass, _fail;
    private string _phase = "-";
    private string _outDir;
    private int _shotIndex;

    private void L(string s) { _log.AppendLine(s); Debug.Log("[6bg] " + s); }
    private void Note(string s) => L("    note  " + s);
    private void Pass(string s) { _pass++; L("    PASS  " + s); }
    private void Fail(string s) { _fail++; L($"    FAIL  {s}   [{_phase}]"); }
    private void Check(bool ok, string s) { if (ok) Pass(s); else Fail(s); }

    private Playground P => _playground;
    private ChunkStore Store => P.DebugStore;
    private FluidGpuSimulation Fluid => P.DebugFluid;

    private int3 ArenaCentre => P.DebugArenaCentre;
    private int3 ArenaOrigin => P.DebugArenaOrigin;
    private int ArenaEdge => P.DebugArenaEdge;

    // Brush indices, matching Playground's own _brushes table.
    private const int BrushWater = 0, BrushSand = 1, BrushLava = 2, BrushStone = 3;
    private static readonly int[] MobileBrushes = { BrushWater, BrushSand, BrushLava };

    // =====================================================================

    IEnumerator Start()
    {
        if (_playground == null) _playground = FindObjectOfType<Playground>();
        if (_playground == null)
        {
            Debug.LogError("[6bg] no Playground in the scene");
            Application.Quit(1);
            yield break;
        }

        // Playground.Start waits for Phase4Bootstrapper, then finds a basin and
        // places the arena. Wait for it rather than racing it.
        float t0 = Time.realtimeSinceStartup;
        while (!P.DebugReady && Time.realtimeSinceStartup - t0 < 180f) yield return null;

        _outDir = Path.Combine(Application.persistentDataPath, _outputRootFolderName,
                               DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(_outDir);

        L("=== PHASE 6: BRUSH GUARD END-TO-END ===");
        L(DateTime.Now.ToString("u", CultureInfo.InvariantCulture));
        L("");

        if (!P.DebugReady)
        {
            L("Playground never became ready (no basin found near spawn?) -- cannot run.");
            Fail("Playground ready");
            yield return Report();
            yield break;
        }

        L($"arena {ArenaEdge}^3  centre {ArenaCentre}  origin {ArenaOrigin}");
        L($"arena spans X {ArenaOrigin.x}..{ArenaOrigin.x + ArenaEdge - 1}  " +
          $"Y {ArenaOrigin.y}..{ArenaOrigin.y + ArenaEdge - 1}  " +
          $"Z {ArenaOrigin.z}..{ArenaOrigin.z + ArenaEdge - 1}");
        L($"that is {ArenaEdge * 0.1f:F1} m across -- 'outside' starts ~{ArenaEdge * 0.05f:F1} m from centre");
        L("");

        Camera cam = Camera.main;

        yield return Step1_OutsideIsRefused(cam);
        yield return Step2_StoneOutsideStillWorks(cam);
        yield return Step3_TheEdgeIsAllOrNothing(cam);
        yield return Step4_InsideIsPlacedAndTheCaPicksItUp(cam);

        yield return Report();
    }

    private IEnumerator Report()
    {
        _log.AppendLine();
        _log.AppendLine($"PASS {_pass}  FAIL {_fail}");
        _log.AppendLine(_fail == 0 ? "RESULT: PASSED" : "RESULT: FAILED");
        File.WriteAllText(Path.Combine(_outDir, "phase6_brushguard_report.txt"), _log.ToString());
        Debug.Log("[6bg] report -> " + _outDir);
        yield return null;
        Application.Quit(_fail == 0 ? 0 : 1);
    }

    // =====================================================================
    // Helpers
    // =====================================================================

    private IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

    /// Moves the camera to a world position and points it at the arena centre.
    /// This is the rig's "fly": Playground's own fly camera never runs here (no
    /// input, and HandleMouse is gated on _cam.Captured, which is false in a
    /// scripted build) so nothing fights the rig for the transform.
    private IEnumerator FlyTo(Camera cam, float3 posM, int settleFrames = 20)
    {
        if (cam != null)
        {
            cam.transform.position = new Vector3(posM.x, posM.y, posM.z);
            Vector3 look = new Vector3(ArenaCentre.x * 0.1f, ArenaCentre.y * 0.1f, ArenaCentre.z * 0.1f)
                         - cam.transform.position;
            if (look.sqrMagnitude > 1e-6f)
                cam.transform.rotation = Quaternion.LookRotation(look.normalized, Vector3.up);
        }
        yield return Frames(settleFrames);
    }

    private IEnumerator Shot(string name)
    {
        if (string.IsNullOrEmpty(_outDir)) yield break;
        yield return null;
        yield return new WaitForEndOfFrame();
        Texture2D tex = ScreenCapture.CaptureScreenshotAsTexture();
        string file = Path.Combine(_outDir, $"{_shotIndex:D2}_{name}.png");
        File.WriteAllBytes(file, tex.EncodeToPNG());
        Destroy(tex);
        _shotIndex++;
        Note($"screenshot -> {Path.GetFileName(file)}");
    }

    /// Every cell a radius-`radius` brush at `centre` would write.
    private static List<int3> BrushCells(int3 centre, int radius)
    {
        var cells = new List<int3>();
        for (int z = -radius; z <= radius; z++)
        for (int y = -radius; y <= radius; y++)
        for (int x = -radius; x <= radius; x++)
            if (FluidGpuSimulation.SphereCovers(x, y, z, radius))
                cells.Add(centre + new int3(x, y, z));
        return cells;
    }

    private List<byte> Snapshot(List<int3> cells)
    {
        var v = new List<byte>(cells.Count);
        for (int i = 0; i < cells.Count; i++) v.Add(Store.GetVoxel(cells[i]));
        return v;
    }

    private int Differences(List<int3> cells, List<byte> before)
    {
        int n = 0;
        for (int i = 0; i < cells.Count; i++) if (Store.GetVoxel(cells[i]) != before[i]) n++;
        return n;
    }

    private int CountMaterialInArena(byte m)
    {
        int n = 0;
        int3 o = ArenaOrigin;
        for (int z = 0; z < ArenaEdge; z++)
        for (int y = 0; y < ArenaEdge; y++)
        for (int x = 0; x < ArenaEdge; x++)
            if (Store.GetVoxel(o + new int3(x, y, z)) == m) n++;
        return n;
    }

    /// An air cell a few metres outside the arena, on the same two axes bug.png
    /// was outside on (past the far X face, short of the near Z face), at a legal
    /// Y. Deliberately the same shape of mistake, not a contrived far-away point.
    private int3 OutsideCell()
        => new int3(ArenaOrigin.x + ArenaEdge + 5, ArenaCentre.y + 18, ArenaOrigin.z - 13);

    // =====================================================================
    // STEP 1 -- outside the arena, every mobile brush is refused, and NOTHING
    // is written. This is bug.png's exact scenario.
    // =====================================================================

    private IEnumerator Step1_OutsideIsRefused(Camera cam)
    {
        _phase = "step1 outside refused";
        L("STEP 1 -- fly outside the arena, paint each mobile brush");

        int3 at = OutsideCell();
        yield return FlyTo(cam, new float3(at.x * 0.1f + 2.5f, at.y * 0.1f + 1.5f, at.z * 0.1f - 2.5f));

        Check(!Fluid.InRegion(at), $"the target cell {at} really is outside the arena");
        if (cam != null)
        {
            int3 camVox = CoordMath.WorldToVoxel(
                new float3(cam.transform.position.x, cam.transform.position.y, cam.transform.position.z));
            Check(!Fluid.InRegion(camVox), $"and the camera itself is outside the arena at {camVox}");
        }

        long rejectedBefore = Fluid.WakeRejectedOutOfRegion;

        for (int i = 0; i < MobileBrushes.Length; i++)
        {
            P.DebugSetBrush(MobileBrushes[i]);
            string name = P.DebugBrushName;
            byte m = P.DebugBrushMaterial;

            Check(MaterialRules.IsMobile(m), $"{name} is a mobile material (the CA is what moves it)");

            var cells = BrushCells(at, 1);
            var before = Snapshot(cells);

            bool placed = P.TryPaintBrush(at);

            Check(!placed, $"{name} outside the arena is REFUSED (TryPaintBrush returned false)");
            Check(Differences(cells, before) == 0,
                $"{name}: not one of the {cells.Count} cells the brush would have written was changed");
            Check(P.DebugStatus.Contains("NOT placed"),
                $"{name}: the status line tells the player why, rather than failing silently");
        }

        // The blob never reached ChunkStore, so the CA was never even asked about
        // it. This is the counter that used to absorb the whole mistake silently.
        Note($"WakeRejectedOutOfRegion unchanged by refusal: {rejectedBefore} -> {Fluid.WakeRejectedOutOfRegion}");

        // Let the CA run: with nothing placed there is nothing to freeze.
        for (int i = 0; i < 60; i++) { P.DebugTickFluid(); yield return null; }

        var frozen = BrushCells(at, 1);
        int nonAir = 0;
        for (int i = 0; i < frozen.Count; i++) if (Store.GetVoxel(frozen[i]) != Materials.Air) nonAir++;
        Check(nonAir == 0,
            $"after 60 CA ticks there is still nothing frozen at {at} ({nonAir} non-air cells) " +
            "-- this is the bug.png signature, and it is absent");

        yield return Shot("step1_outside_refused");
        L("");
    }

    // =====================================================================
    // STEP 2 -- the guard must not over-refuse. Stone is not simulated, so
    // placing it outside the arena is legitimate terrain editing.
    // =====================================================================

    private IEnumerator Step2_StoneOutsideStillWorks(Camera cam)
    {
        _phase = "step2 stone outside";
        L("STEP 2 -- stone (not simulated) outside the arena must still place");

        int3 at = OutsideCell() + new int3(0, 6, 0);
        P.DebugSetBrush(BrushStone);

        Check(!MaterialRules.IsMobile(P.DebugBrushMaterial), "stone is NOT a mobile material");
        Check(!Fluid.InRegion(at), $"the stone target {at} is outside the arena");

        var cells = BrushCells(at, 2);       // stone paints at radius 2
        var before = Snapshot(cells);

        bool placed = P.TryPaintBrush(at);

        Check(placed, "stone outside the arena IS placed (a guard that refused it would be over-broad)");
        int changed = Differences(cells, before);
        Check(changed > 0, $"stone actually reached ChunkStore ({changed}/{cells.Count} cells changed)");

        int stone = 0;
        for (int i = 0; i < cells.Count; i++) if (Store.GetVoxel(cells[i]) == Materials.Stone) stone++;
        Check(stone == cells.Count, $"every cell of the stone blob is stone ({stone}/{cells.Count})");

        yield return Shot("step2_stone_outside_placed");
        L("");
    }

    // =====================================================================
    // STEP 3 -- all-or-nothing at the boundary. A blob centred on the last
    // legal cell straddles the face; clipping it would leave a frozen rim.
    // =====================================================================

    private IEnumerator Step3_TheEdgeIsAllOrNothing(Camera cam)
    {
        _phase = "step3 edge all-or-nothing";
        L("STEP 3 -- a blob straddling the arena face is refused entirely, not clipped");

        // Last legal cell on the far X face, at a height inside the arena.
        int3 at = new int3(ArenaOrigin.x + ArenaEdge - 1, ArenaCentre.y + 10, ArenaCentre.z);
        yield return FlyTo(cam, new float3(at.x * 0.1f + 3f, at.y * 0.1f + 1f, at.z * 0.1f - 3f));

        Check(Fluid.InRegion(at), $"the centre cell {at} is itself inside the arena");
        Check(!Fluid.InRegion(at + new int3(1, 0, 0)),
            "but its +X neighbour -- which a radius-1 brush also writes -- is outside");

        P.DebugSetBrush(BrushWater);
        var cells = BrushCells(at, 1);
        var before = Snapshot(cells);

        bool placed = P.TryPaintBrush(at);

        Check(!placed, "the straddling placement is refused");
        Check(Differences(cells, before) == 0,
            "and NO cell was written -- not even the in-region ones, which would " +
            "have left a frozen rim outside the face");

        yield return Shot("step3_edge_refused");
        L("");
    }

    // =====================================================================
    // STEP 4 -- the other half of the claim: inside the arena, placement
    // succeeds AND the CA actually picks the fluid up and moves it.
    //
    // Without this step the rig would pass just as happily if the guard
    // refused everything, everywhere.
    // =====================================================================

    private IEnumerator Step4_InsideIsPlacedAndTheCaPicksItUp(Camera cam)
    {
        _phase = "step4 inside placed and simulated";
        L("STEP 4 -- fly back inside; placement succeeds and the CA moves it");

        // Playground's own 'fly to arena' toy (key F), not a hand-rolled move.
        P.DebugRunKey(KeyCode.F);
        yield return Frames(30);

        // Well above the basin floor so the blob has somewhere to fall, and well
        // inside every face so settling cannot leave the region (which would look
        // like mass loss and confuse the conservation check below).
        int3 at = new int3(ArenaCentre.x, ArenaCentre.y + 12, ArenaCentre.z);
        yield return FlyTo(cam, new float3(at.x * 0.1f - 3.5f, at.y * 0.1f + 1.2f, at.z * 0.1f - 3.5f));

        Check(Fluid.SphereFitsInRegion(at, 1), $"the target {at} is comfortably inside the arena");

        P.DebugSetBrush(BrushWater);
        var cells = BrushCells(at, 1);

        int waterBefore = CountMaterialInArena(Materials.Water);
        bool placed = P.TryPaintBrush(at);

        Check(placed, "water inside the arena IS placed");

        int waterAfterPlace = CountMaterialInArena(Materials.Water);
        int poured = waterAfterPlace - waterBefore;
        Check(poured > 0, $"the placement actually added water to the arena (+{poured})");
        L($"  water in arena: {waterBefore} -> {waterAfterPlace}");

        // Where the blob sits right now, before any tick.
        var placedCells = new List<int3>();
        for (int i = 0; i < cells.Count; i++)
            if (Store.GetVoxel(cells[i]) == Materials.Water) placedCells.Add(cells[i]);
        Check(placedCells.Count > 0, $"the blob is in ChunkStore as water ({placedCells.Count} cells)");

        yield return Shot("step4_inside_placed");

        // Now run the CA. THIS is the end-to-end claim: a blob placed in-arena is
        // picked up and moved, unlike the frozen out-of-arena blobs in bug.png.
        for (int i = 0; i < 240; i++) { P.DebugTickFluid(); yield return null; }

        int stillThere = 0;
        for (int i = 0; i < placedCells.Count; i++)
            if (Store.GetVoxel(placedCells[i]) == Materials.Water) stillThere++;

        Check(stillThere < placedCells.Count,
            $"the CA MOVED the blob: {placedCells.Count - stillThere}/{placedCells.Count} of the " +
            "originally-placed cells are no longer water (a frozen blob would move 0)");

        int waterAfterTicks = CountMaterialInArena(Materials.Water);
        L($"  water in arena after 240 ticks: {waterAfterTicks}");
        Check(waterAfterTicks >= waterAfterPlace,
            $"and no water was lost while it moved ({waterAfterTicks} vs {waterAfterPlace}) " +
            "-- generated water can flow in, so this is a floor, not an equality");

        Note($"WakeRejectedOutOfRegion total for the run: {Fluid.WakeRejectedOutOfRegion} " +
             "(nonzero is normal: EditService's 27-cell wake scan straddles the arena face)");

        yield return Shot("step4_inside_simulated");
        L("");
    }
}
