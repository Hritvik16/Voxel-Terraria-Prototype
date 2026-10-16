// Assets/Game/Playground.Stroke.cs
//
// GAMEPLAY BLD1 (task 93a1, GAMEPLAY_PHASE_PLAN.md P6/P7/P27): DRAG LINE in
// Survival. RMB press, drag (turn the view), release: the held block is
// stamped as brush cubes along the straight line between the press cube and
// the release cube (SurvivalBuild.LineCubes, integer DDA on the brush grid), in
// ONE EditService.SetCubes call. While dragging, every cube is outlined (cyan =
// paid for, red = past where the slot runs out, grey = skipped) with the cube
// and cell count under the crosshair; Esc cancels (and does NOT release the
// mouse). Placing stops at the first cube the slot cannot pay for (P7).
//
// LMB: held still digs at the tool's rate, exactly as before. Once the view
// turns more than DigDragDeg from where LMB was pressed, the press becomes a
// dig stroke: the tool dig stops, the line from the press voxel's cube to the
// aimed cube is previewed (orange) and dug on release, every voxel credited
// to the inventory as Dig does.
//
// Single-voxel items (torches) keep one-per-click: a line of torches is not a
// thing anyone draws.

using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Memory;

public partial class Playground
{
    private enum StrokeKind { None, Place, DigPending, Dig }

    private StrokeKind _stroke;
    private int3 _strokeFrom, _strokeTo;            // voxels: the press aim and the current aim
    private int _strokeEdge;
    private Vector3 _strokePressFwd;                // LMB: the view at the press
    private readonly List<int3> _strokeCubes = new List<int3>();      // after the guards
    private readonly List<int3> _strokeSkippedCubes = new List<int3>();
    private SurvivalBuild.StrokeCost _strokeCost;
    private bool _strokeDirty = true;
    private int3 _strokeLastTo = new int3(int.MinValue);
    private float _strokeRecostS;
    private int _strokeSeenFrame = -1;
    private bool _strokeDemoOwns;                   // -strokedemo: the mouse path keeps its hands off
    private const float DigDragDeg = 4f;
    private const float StrokeRecostS = 0.25f;      // the cost is re-read this often while still (the world changes)
    private const int StrokePreviewMaxCubes = 96;   // longer strokes draw the end cubes + the line between

    internal bool StrokeActive => _stroke == StrokeKind.Place || _stroke == StrokeKind.Dig;

    /// Survival mouse, called every frame the cursor is captured, target or not.
    /// Returns true when a stroke owns the mouse (the caller skips single-click handling).
    private bool HandleStrokeMouse(float dt)
    {
        if (_strokeDemoOwns) return true;               // -strokedemo drives the strokes itself

        // A frame without this call (E panel, F6 menu, dev map) ends the stroke:
        // its button release was never seen, so it must not commit later.
        if (_stroke != StrokeKind.None && _strokeSeenFrame != Time.frameCount - 1) CancelStroke();
        _strokeSeenFrame = Time.frameCount;

        if (_stroke != StrokeKind.None)
            PlaygroundFlyCamera.EscapeEatenUntilFrame = Time.frameCount + 1;   // Esc cancels, keeps the mouse

        if (_stroke != StrokeKind.None && Input.GetKeyDown(KeyCode.Escape))
        {
            bool was = StrokeActive;
            CancelStroke();
            if (was) _status = "stroke cancelled";
            return true;
        }

        // ---- RMB: place stroke ----
        if (_stroke == StrokeKind.None && Input.GetMouseButtonDown(1) && _hasTarget
            && !_inv.IsEmpty(_inv.Selected) && !Items.PlacesSingleVoxel(_inv.ItemAt(_inv.Selected)))
        {
            BeginPlaceStroke(_targetAdjacent);
        }
        if (_stroke == StrokeKind.Place)
        {
            if (_hasTarget) _strokeTo = _targetAdjacent;
            if (!Input.GetMouseButton(1)) CommitPlaceStroke();
            else RefreshStroke(dt);
            return true;
        }

        // ---- LMB: tool dig, turning into a dig stroke on a drag ----
        if (_stroke == StrokeKind.None && Input.GetMouseButtonDown(0) && _hasTarget)
        {
            _stroke = StrokeKind.DigPending;
            _strokeFrom = _strokeTo = _targetVoxel;
            _strokePressFwd = Camera.main != null ? Camera.main.transform.forward : Vector3.forward;
        }
        if (_stroke == StrokeKind.DigPending)
        {
            if (!Input.GetMouseButton(0)) { _stroke = StrokeKind.None; return false; }
            Vector3 fwd = Camera.main != null ? Camera.main.transform.forward : _strokePressFwd;
            if (Vector3.Angle(fwd, _strokePressFwd) <= DigDragDeg) return false;   // still: the tool digs
            _stroke = StrokeKind.Dig;
            _strokeEdge = SurvivalBuild.CubeEdges[_edgeIndex];
            _strokeDirty = true;
        }
        if (_stroke == StrokeKind.Dig)
        {
            if (_hasTarget) _strokeTo = _targetVoxel;
            if (!Input.GetMouseButton(0)) CommitDigStroke();
            else RefreshStroke(dt);
            return true;
        }
        return false;
    }

    /// The press half of a place stroke (internal: -strokedemo drives it).
    internal void BeginPlaceStroke(int3 at)
    {
        _stroke = StrokeKind.Place;
        _strokeFrom = _strokeTo = at;
        _strokeDirty = true;
    }

    /// The press half of a dig stroke without the tool-dig phase (-strokedemo).
    internal void BeginDigStroke(int3 at)
    {
        _stroke = StrokeKind.Dig;
        _strokeFrom = _strokeTo = at;
        _strokeEdge = SurvivalBuild.CubeEdges[_edgeIndex];
        _strokeDirty = true;
    }

    internal void CancelStroke()
    {
        _stroke = StrokeKind.None;
        _strokeCubes.Clear();
        _strokeSkippedCubes.Clear();
        _strokeLastTo = new int3(int.MinValue);
    }

    /// Recomputes the path and its cost when the aim moved to another cube, the
    /// slot changed, or every StrokeRecostS (the world under it may change).
    internal void RefreshStroke(float dt, bool force = false)
    {
        if (!StrokeActive) return;
        bool dig = _stroke == StrokeKind.Dig;
        int edge = dig ? SurvivalBuild.CubeEdges[_edgeIndex]
                       : SurvivalBuild.EdgeFor(_inv.ItemAt(_inv.Selected), SurvivalBuild.CubeEdges[_edgeIndex]);
        int3 toCube = SurvivalBuild.SnapToGrid(_strokeTo, edge);
        _strokeRecostS -= dt;
        if (!force && !_strokeDirty && edge == _strokeEdge && toCube.Equals(_strokeLastTo) && _strokeRecostS > 0f) return;
        _strokeDirty = false;
        _strokeRecostS = StrokeRecostS;
        _strokeEdge = edge;
        _strokeLastTo = toCube;

        SurvivalBuild.LineCubes(_strokeFrom, _strokeTo, edge, _strokeCubes);
        _strokeSkippedCubes.Clear();
        if (!dig) FilterPlaceCubes(_strokeCubes, edge, _strokeSkippedCubes);
        _strokeCost = SurvivalBuild.Cost(_store, _strokeCubes, edge, dig, dig ? int.MaxValue : _inv.CountAt(_inv.Selected));
    }

    /// The single-click guards, per cube: a cube that would enclose the player,
    /// or a mobile block (sand, snow, ...) outside the fluid wake radius (it
    /// would never fall), is SKIPPED -- the rest of the stroke still goes down.
    private void FilterPlaceCubes(List<int3> cubes, int edge, List<int3> skipped)
    {
        byte item = _inv.ItemAt(_inv.Selected);
        bool body = _mode == MoveMode.Walk && _player != null && _player.Motor != null;
        int3 bLo = int3.zero, bHi = int3.zero;
        if (body)
        {
            float3 feet = _player.Motor.PositionM;
            float hw = PlayerConfig.Active.bodyWidthM * 0.5f;
            bLo = CoordMath.WorldToVoxel(feet - new float3(hw, 0f, hw));
            bHi = CoordMath.WorldToVoxel(feet + new float3(hw, PlayerConfig.Active.bodyHeightM, hw));
        }
        bool mobile = MaterialRules.IsMobile(item);
        int w = 0;
        for (int i = 0; i < cubes.Count; i++)
        {
            int3 lo = cubes[i];
            if ((body && SurvivalBuild.CubeOverlaps(lo, edge, bLo, bHi)) ||
                (mobile && !SphereFitsInWakeRadius(lo + (edge >> 1), edge)))
            { skipped.Add(lo); continue; }
            cubes[w++] = lo;
        }
        cubes.RemoveRange(w, cubes.Count - w);
    }

    /// The release half of a place stroke.
    internal SurvivalBuild.StrokeResult CommitPlaceStroke()
    {
        RefreshStroke(0f, force: true);
        int slot = _inv.Selected;
        int skipped = _strokeSkippedCubes.Count;
        var r = SurvivalBuild.PlaceStroke(_edits, _store, _inv, slot, _strokeCubes, _strokeEdge);
        CancelStroke();
        string skip = skipped > 0 ? $", {skipped} cube(s) skipped (around you / outside the wake radius)" : "";
        switch (r.Outcome)
        {
            case SurvivalBuild.PlaceOutcome.Placed:
                _act = Act.Place;
                _actAge = 0f;
                _invDirty = true;
                _status = (r.RanOut ? "<color=#ffd479>" : "") +
                          $"placed {r.Placed:N0} {Items.Name(r.Item)} in {r.CubesPaid}/{r.Cubes} cube(s) of {r.Edge * 0.1f:0.0} m" +
                          (r.RanOut ? $" -- ran out ({r.Needed - r.Placed:N0} more needed)</color>" : "") +
                          $"{skip}, {_inv.CountAt(slot):N0} left";
                break;
            case SurvivalBuild.PlaceOutcome.EmptySlot: PlaceRefused($"slot {slot + 1} is empty -- dig (LMB) to fill it"); break;
            case SurvivalBuild.PlaceOutcome.NotPlaceable: PlaceRefused($"{Items.Name(r.Item)} cannot be placed"); break;
            case SurvivalBuild.PlaceOutcome.TooFew:
                PlaceRefused($"not enough {Items.Name(r.Item)}: the first {r.Edge * 0.1f:0.0} m cube needs more than the slot's {_inv.CountAt(slot)} -- Q for a smaller size");
                break;
            case SurvivalBuild.PlaceOutcome.NotLoaded: PlaceRefused("not placed -- that ground is not loaded yet"); break;
            default: if (skipped > 0) PlaceRefused($"not placed{skip}"); break;   // NothingToFill
        }
        Debug.Log($"[Stroke] place {r.Outcome}: {r.CubesPaid}/{r.Cubes} cubes of {r.Edge}, {r.Placed}/{r.Needed} cells, ranOut {r.RanOut}, skipped {skipped}");
        return r;
    }

    /// The release half of a dig stroke.
    internal SurvivalBuild.DigResult CommitDigStroke()
    {
        RefreshStroke(0f, force: true);
        int n = _strokeCubes.Count, edge = _strokeEdge;
        var r = SurvivalBuild.DigStroke(_edits, _inv, _strokeCubes, edge, _digScratch);
        CancelStroke();
        if (r.Removed > 0)
        {
            _dugCounter += r.Removed;
            _act = Act.Dig;
            _actAge = 0f;
            _invDirty |= r.Credited > 0;
            _lostTotal += r.Lost;
            _status = $"dug {r.Removed:N0} voxels in {n} cube(s) of {edge * 0.1f:0.0} m: +{r.Credited:N0} to the inventory" +
                      (r.Lost > 0 ? $" <color=#ff9a9a>({r.Lost:N0} lost -- inventory FULL)</color>" : "");
        }
        Debug.Log($"[Stroke] dig: {n} cubes of {edge}, removed {r.Removed}, credited {r.Credited}, lost {r.Lost}");
        return r;
    }

    // ---- HUD ----

    /// Every cube of the stroke (cyan paid / red unpaid / grey skipped; dig
    /// orange) and the count under the crosshair. Long strokes draw the two end
    /// cubes and the line between them.
    private void DrawStrokePreview(Camera cam, float k, float ui)
    {
        if (!StrokeActive || _strokeEdge <= 0) return;
        bool dig = _stroke == StrokeKind.Dig;
        int e = _strokeEdge;
        Color paid = dig ? new Color(1f, 0.6f, 0.2f, 0.9f) : new Color(0.45f, 0.9f, 1f, 0.9f);
        Color unpaid = new Color(1f, 0.35f, 0.35f, 0.9f);
        Color skip = new Color(0.6f, 0.6f, 0.6f, 0.6f);
        int n = _strokeCubes.Count;
        if (n <= StrokePreviewMaxCubes)
        {
            for (int i = 0; i < n; i++)
                DrawVoxelHighlight(cam, _strokeCubes[i], i < _strokeCost.Affordable ? paid : unpaid, 1.6f * k, e);
            for (int i = 0; i < _strokeSkippedCubes.Count && i < StrokePreviewMaxCubes; i++)
                DrawVoxelHighlight(cam, _strokeSkippedCubes[i], skip, 1.2f * k, e);
        }
        else
        {
            DrawVoxelHighlight(cam, _strokeCubes[0], paid, 2f * k, e);
            DrawVoxelHighlight(cam, _strokeCubes[n - 1], _strokeCost.Affordable >= n ? paid : unpaid, 2f * k, e);
            int last = math.max(0, _strokeCost.Affordable - 1);
            if (_strokeCost.Affordable > 0 && _strokeCost.Affordable < n)
                DrawVoxelHighlight(cam, _strokeCubes[last], paid, 2f * k, e);
            Vector3 a = CubeCentre(_strokeCubes[0], e), b = CubeCentre(_strokeCubes[n - 1], e);
            Vector3 sa = cam.WorldToScreenPoint(a), sb = cam.WorldToScreenPoint(b);
            if (sa.z > 0f && sb.z > 0f)
                DrawLine(new Vector2(sa.x, Screen.height - sa.y), new Vector2(sb.x, Screen.height - sb.y), paid, 2f * k);
        }

        string line;
        if (dig)
            line = $"<b>DIG</b> {n} cube(s) x {e * 0.1f:0.0} m   {_strokeCost.Cells:N0} voxels   <color=#9fb6c9>release = dig, Esc = cancel</color>";
        else
        {
            int have = _inv.CountAt(_inv.Selected);
            string runs = _strokeCost.Affordable < n
                ? $"   <color=#ff9a9a>slot runs out after {_strokeCost.Affordable} of {n}</color>" : "";
            string sk = _strokeSkippedCubes.Count > 0 ? $"   <color=#bbbbbb>{_strokeSkippedCubes.Count} skipped</color>" : "";
            line = $"<b>{n}</b> cube(s) x {e * 0.1f:0.0} m   <b>{_strokeCost.AffordableCells:N0}</b> of {_strokeCost.Cells:N0} cells   " +
                   $"{Items.Name(_inv.ItemAt(_inv.Selected))} {have:N0}{runs}{sk}   <color=#9fb6c9>release = place, Esc = cancel</color>";
        }
        float vw = Screen.width / ui, vh = Screen.height / ui;
        DrawTextPanel(new Rect(vw * 0.5f - 300f, vh * 0.5f + 28f, 900f, 24f), ui, line);
    }

    private static Vector3 CubeCentre(int3 lo, int e) => (new Vector3(lo.x, lo.y, lo.z) + Vector3.one * (e * 0.5f)) * 0.1f;

    // =====================================================================
    // -strokedemo (task 93a1): BLD1's acceptance screenshots through the real
    // stroke path (BeginPlaceStroke / RefreshStroke / CommitPlaceStroke). An
    // INTERACTIVE run: run-play.sh EXTRA="-strokedemo". Finds flat dry ground
    // with the sun behind, grants a demo stack of stone (logged), and shoots:
    // 1 the preview of a 10 m wall base (0.4 m cubes) mid-drag, 2 the base
    // placed by that one stroke, 3 a short-slot stroke's preview (red past the
    // run-out), 4 that stroke placed (stops at the run-out), 5 a dig stroke's
    // preview, 6 the trench it dug. Shots: persistentDataPath/StrokeDemo/.
    // =====================================================================
    private IEnumerator StrokeDemo()
    {
        float t0 = Time.unscaledTime;
        while (Time.unscaledTime - t0 < 25f || _player == null || !_player.Ready) yield return null;
        SetGameMode(true);
        _showHelp = false;
        _demoHideState = true;
        _mode = MoveMode.Walk;
        SunCycle.SetHours(SunCycle.NoonHours);
        VoxelEngine.Rendering.LightingDriver.ClockPaused = true;
        string dir = System.IO.Path.Combine(Application.persistentDataPath, "StrokeDemo");
        System.IO.Directory.CreateDirectory(dir);
        if (_cam == null && Camera.main != null) _cam = Camera.main.GetComponent<PlaygroundFlyCamera>();

        // 1. A stand: dry ground within 200 m, flat (+-0.5 m) over the 12 m x 6 m
        // ahead of it on a heading away from the sun.
        int3 f = CoordMath.WorldToVoxel(_player.Motor.PositionM);
        float2 sxz = math.normalizesafe(SunCycle.Direction.xz);
        float awayYaw = math.degrees(math.atan2(-sxz.x, -sxz.y));
        // The flattest stand (max height step over the 12 m x 6 m ahead), headings
        // within 45 deg of straight away from the sun; the first within 0.5 m wins.
        int3 stand = new int3(f.x, math.max(DryTopY(f.x, f.z), f.y - 1), f.z);
        float yaw = awayYaw;
        int bestDev = int.MaxValue;
        for (int r = 0; r <= 1200 && bestDev > 5; r += 20)
        {
            for (int dz = -r; dz <= r && bestDev > 5; dz += 20)
            for (int dx = -r; dx <= r && bestDev > 5; dx += 20)
            {
                if (math.max(math.abs(dx), math.abs(dz)) != r) continue;
                int x = f.x + dx, z = f.z + dz, sy = DryTopY(x, z);
                if (sy < 0) continue;
                for (int k = -2; k <= 2; k++)
                {
                    float kr = math.radians(awayYaw + k * 22.5f);
                    float2 kf = new float2(math.sin(kr), math.cos(kr)), kri = new float2(kf.y, -kf.x);
                    int dev = 0;
                    for (int sx = -60; sx <= 60 && dev < bestDev; sx += 10)
                    for (int a = 0; a <= 60 && dev < bestDev; a += 20)
                    {
                        int h = DryTopY(x + (int)math.round(kf.x * a + kri.x * sx), z + (int)math.round(kf.y * a + kri.y * sx));
                        dev = h < 0 ? int.MaxValue : math.max(dev, math.abs(h - sy));
                    }
                    if (dev < bestDev) { bestDev = dev; stand = new int3(x, sy, z); yaw = awayYaw + k * 22.5f; }
                }
            }
            yield return null;
        }
        Debug.Log($"[StrokeDemo] flattest stand: max step {bestDev} voxels over 12 m x 6 m");
        float yr = math.radians(yaw);
        float2 fwd = new float2(math.sin(yr), math.cos(yr));
        float2 right = new float2(fwd.y, -fwd.x);
        _player.Motor.Teleport(new float3(stand.x * 0.1f + 0.05f, (stand.y + 1) * 0.1f + 0.02f, stand.z * 0.1f + 0.05f));
        _player.Motor.ResolveSpawn();
        _cam?.SetLook(yaw, 18f);
        for (int i = 0; i < 90; i++) yield return null;
        Debug.Log($"[StrokeDemo] stand {stand}, yaw {yaw:F0} (straight away from the sun {awayYaw:F0}); {EyeReport()}");

        // A demo stack of the solid block whose colour differs most from the ground.
        _strokeDemoOwns = true;
        int3 up = new int3(0, 1, 0);                          // the air above the ground, as RMB's aim
        int3 A = GroundAt(stand, fwd * 60f - right * 50f) + up, B = GroundAt(stand, fwd * 60f + right * 50f) + up;
        var ground = MaterialPalette.Of(_store.GetVoxel(A - up));
        byte block = Materials.Stone;
        float bestDiff = -1f;
        foreach (byte m in new[] { Materials.Stone, Materials.FromTg2(TerrainGen.GenMat.BlueSlate), Materials.FromTg2(TerrainGen.GenMat.Marble),
                                   Materials.FromTg2(TerrainGen.GenMat.BlackBasalt), Materials.FromTg2(TerrainGen.GenMat.TealQuartzite) })
        {
            if (MaterialRules.IsMobile(m) || !Items.IsPlaceable(m)) continue;
            var c = MaterialPalette.Of(m);
            float diff = math.abs(c.R - ground.R) + math.abs(c.G - ground.G) + math.abs(c.B - ground.B);
            if (diff > bestDiff) { bestDiff = diff; block = m; }
        }
        _inv.Clear();
        _inv.Add(block, 10000);
        SelectSlot(0);
        _edgeIndex = SurvivalBuild.CubeEdges.Length - 1;      // 0.8 m: reads from 6 m away
        int e = SurvivalBuild.CubeEdges[_edgeIndex];
        Debug.Log($"[StrokeDemo] demo stack: {Items.Name(block)} x10000 (ground {Items.Name(_store.GetVoxel(A - up))}), cube {e}");

        // 2. The 10 m wall base: 6 m ahead, from 5 m left to 5 m right, ground to ground.
        int before = _inv.CountAt(0);
        BeginPlaceStroke(A);
        _strokeTo = B;
        RefreshStroke(0f, force: true);
        Debug.Log($"[StrokeDemo] wall base preview: {A} -> {B}, {_strokeCubes.Count} cubes, cost {_strokeCost.Cells} cells, affordable {_strokeCost.Affordable}");
        yield return DemoShot(dir, "1_wall_base_preview");
        var w = CommitPlaceStroke();
        Debug.Log($"[StrokeDemo] wall base: {w.CubesPaid}/{w.Cubes} cubes, placed {w.Placed}, slot {before} -> {_inv.CountAt(0)} " +
                  $"(conservation {(before - _inv.CountAt(0) == w.Placed ? "OK" : "BROKEN")})");
        for (int i = 0; i < 20; i++) yield return null;
        yield return DemoShot(dir, "2_wall_base_placed");

        // 3. A second course on top with a short slot (5 cubes and a bit): it runs out part way.
        _inv.Clear();
        _inv.Add(block, 512 * 5 + 100);
        SelectSlot(0);
        BeginPlaceStroke(A + new int3(0, e, 0));
        _strokeTo = B + new int3(0, e, 0);
        RefreshStroke(0f, force: true);
        Debug.Log($"[StrokeDemo] short slot preview: {_strokeCubes.Count} cubes, cost {_strokeCost.Cells} cells, affordable {_strokeCost.Affordable}");
        yield return DemoShot(dir, "3_short_slot_preview");
        var s2 = CommitPlaceStroke();
        Debug.Log($"[StrokeDemo] short slot: {s2.CubesPaid}/{s2.Cubes} cubes, placed {s2.Placed} of {s2.Needed}, ranOut {s2.RanOut}, left {_inv.CountAt(0)}");
        for (int i = 0; i < 20; i++) yield return null;
        yield return DemoShot(dir, "4_short_slot_placed");

        // 4. A dig stroke: a 0.4 m trench 3 m ahead, 8 m long.
        _inv.Clear();
        _edgeIndex = SurvivalBuild.DefaultEdgeIndex;
        int3 C = GroundAt(stand, fwd * 30f - right * 40f), D = GroundAt(stand, fwd * 30f + right * 40f);
        BeginDigStroke(C);
        _strokeTo = D;
        RefreshStroke(0f, force: true);
        Debug.Log($"[StrokeDemo] dig preview: {C} -> {D}, {_strokeCubes.Count} cubes, {_strokeCost.Cells} voxels");
        yield return DemoShot(dir, "5_dig_preview");
        var d = CommitDigStroke();
        Debug.Log($"[StrokeDemo] trench: removed {d.Removed}, credited {d.Credited}, lost {d.Lost}; {InventoryLine()}");
        for (int i = 0; i < 20; i++) yield return null;
        yield return DemoShot(dir, "6_dig_done");

        _inv = Inventory.WithStarterKit();
        Debug.Log("[StrokeDemo] done");
        Application.Quit();
    }

    /// The top dry voxel at `stand` + an xz offset in voxels.
    private int3 GroundAt(int3 stand, float2 offsetVoxels)
    {
        int x = stand.x + (int)math.round(offsetVoxels.x), z = stand.z + (int)math.round(offsetVoxels.y);
        int y = DryTopY(x, z);
        return new int3(x, y < 0 ? stand.y : y, z);
    }
}
