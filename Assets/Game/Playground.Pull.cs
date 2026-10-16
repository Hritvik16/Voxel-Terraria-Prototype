// Assets/Game/Playground.Pull.cs
//
// GAMEPLAY BLD2 (task 93b1, owner Q1 2026-10-15): FACE PULL in Survival, like
// SketchUp's push-pull. Hold Alt (Option) and RMB-drag on a face of the last
// shape you placed -- a block, a BLD1 line, or what the previous pull made --
// and that shape's cubes on the face are repeated along the face normal, one
// layer per cube length the aim moves along it (SurvivalBuild.PullSteps: the
// point on the normal line closest to the crosshair ray). Block -> line ->
// wall -> thick wall. Dragging back past the face (N < 0) digs the face layer
// and the layers behind it, credited like a dig stroke. Release commits in ONE
// EditService.SetCubes call (SurvivalBuild.PlacePull / DigPull), Esc cancels.
//
// "The shape" is the most recent click, line or pull while it is intact
// (every cube's centre voxel still solid, Shape.Intact). Aimed anywhere else,
// the pull extends the single brush cube under the crosshair (size: Q), and
// that cube + what the pull adds become the shape.
//
// Placing pays per cube in layer order and stops where the slot runs out (P7);
// BLD1's guards apply per cube (around the player / mobile blocks outside the
// wake radius are skipped). A pull writes at most MaxPullCubes(edge) cubes.

using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Memory;

public partial class Playground
{
    private readonly SurvivalBuild.Shape _shape = new SurvivalBuild.Shape();      // the last shape placed
    private readonly SurvivalBuild.Shape _pullTemp = new SurvivalBuild.Shape();   // the single aimed cube
    private SurvivalBuild.Shape _pullShape;
    private bool _pullFromShape;
    private int3 _pullNormal, _pullCube;
    private float3 _pullFaceCentreM;
    private int _pullSteps, _pullUsed;
    private int _pullDemoSteps = int.MinValue;      // -pulldemo sets N directly instead of the aim
    private readonly List<int3> _pullFace = new List<int3>();
    private static readonly List<int3> _oneCube = new List<int3> { int3.zero };

    private static bool PullKeyHeld => Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);

    /// A single placed cube becomes the shape (TrySurvivalPlace).
    private void RememberShape(int3 lo, int edge)
    {
        _oneCube[0] = lo;
        _shape.Set(_oneCube, 1, edge);
    }

    /// The face of the aimed voxel the crosshair ray enters through.
    private int3 AimFace()
    {
        Camera cam = Camera.main;
        if (cam == null) return new int3(0, 1, 0);
        Vector3 p = cam.transform.position * 10f, d = cam.transform.forward;
        return SurvivalBuild.EntryFace(new float3(p.x, p.y, p.z), new float3(d.x, d.y, d.z), _targetVoxel);
    }

    /// The press half of a pull: `target` is the aimed solid voxel, `normal` its face.
    internal void BeginPull(int3 target, int3 normal)
    {
        _stroke = StrokeKind.Pull;
        _pullFromShape = !_shape.IsEmpty && _shape.Contains(SurvivalBuild.SnapToGrid(target, _shape.Edge)) && _shape.Intact(_store);
        if (!_pullFromShape)
        {
            if (!_shape.IsEmpty && !_shape.Intact(_store)) _shape.Clear();       // broken: forget it
            int edge = SurvivalBuild.CubeEdges[_edgeIndex];
            _oneCube[0] = SurvivalBuild.SnapToGrid(target, edge);
            _pullTemp.Set(_oneCube, 1, edge);
        }
        _pullShape = _pullFromShape ? _shape : _pullTemp;
        int e = _pullShape.Edge;
        _pullNormal = normal;
        _pullCube = SurvivalBuild.SnapToGrid(target, e);
        _pullFaceCentreM = ((float3)_pullCube + e * 0.5f + (float3)normal * (e * 0.5f)) * 0.1f;
        _pullSteps = _pullUsed = 0;
        _strokeEdge = e;
        SurvivalBuild.FaceLayer(_pullShape, normal, _pullFace);
        _strokeDirty = true;
    }

    private void ResetPull()
    {
        _pullShape = null;
        _pullFace.Clear();
        _pullSteps = _pullUsed = 0;
    }

    /// Follows the aim: recomputes the pulled cubes when N changed, the slot
    /// changed, or every StrokeRecostS (the world under them may change).
    internal void RefreshPull(float dt, bool force = false)
    {
        if (_stroke != StrokeKind.Pull || _pullShape == null) return;
        int e = _pullShape.Edge;
        int steps = _pullDemoSteps;
        if (steps == int.MinValue)
        {
            Camera cam = Camera.main;
            steps = _pullSteps;
            if (cam != null)
            {
                Vector3 o = cam.transform.position, d = cam.transform.forward;
                steps = SurvivalBuild.PullSteps(new float3(o.x, o.y, o.z), new float3(d.x, d.y, d.z),
                                                _pullFaceCentreM, _pullNormal, e * 0.1f, _pullSteps);
            }
        }
        _strokeRecostS -= dt;
        if (!force && !_strokeDirty && steps == _pullSteps && _strokeRecostS > 0f) return;
        _strokeDirty = false;
        _strokeRecostS = StrokeRecostS;
        _pullSteps = steps;

        _pullUsed = SurvivalBuild.PullCubes(_pullFace, e, _pullNormal, steps, _strokeCubes, SurvivalBuild.MaxPullCubes(e));
        _strokeSkippedCubes.Clear();
        bool dig = _pullUsed < 0;
        if (!dig) FilterPlaceCubes(_strokeCubes, e, _strokeSkippedCubes);
        _strokeCost = SurvivalBuild.Cost(_store, _strokeCubes, e, dig, dig ? int.MaxValue : _inv.CountAt(_inv.Selected));
    }

    /// The release half of a pull.
    internal SurvivalBuild.StrokeResult CommitPull()
    {
        RefreshPull(0f, force: true);
        var r = new SurvivalBuild.StrokeResult { Outcome = SurvivalBuild.PlaceOutcome.NothingToFill, Edge = _strokeEdge };
        int slot = _inv.Selected, skipped = _strokeSkippedCubes.Count, layers = math.abs(_pullUsed);
        string what = _pullFromShape ? $"shape of {_pullShape.Count}" : "aimed cube";
        if (_pullUsed == 0)
        {
            _status = "pull: drag along the face (out = place, back in = dig)";
        }
        else if (_pullUsed < 0)
        {
            int n = _strokeCubes.Count;
            var d = SurvivalBuild.DigPull(_edits, _inv, _pullShape, _strokeCubes, _digScratch);
            if (!_pullFromShape) _shape.Set(_pullTemp.Cubes, _pullTemp.Count, _pullTemp.Edge);
            r.Cubes = n;
            if (d.Removed > 0)
            {
                _dugCounter += d.Removed;
                _act = Act.Dig;
                _actAge = 0f;
                _invDirty |= d.Credited > 0;
                _lostTotal += d.Lost;
                _status = $"pulled in {layers} layer(s): dug {d.Removed:N0} voxels in {n} cube(s), +{d.Credited:N0} to the inventory" +
                          (d.Lost > 0 ? $" <color=#ff9a9a>({d.Lost:N0} lost -- inventory FULL)</color>" : "");
            }
            Debug.Log($"[Pull] dig {what}, n {_pullNormal}, {_pullUsed} layers: {n} cubes of {_strokeEdge}, removed {d.Removed}, credited {d.Credited}");
        }
        else if (Items.PlacesSingleVoxel(_inv.ItemAt(slot)))
        {
            PlaceRefused($"{Items.Name(_inv.ItemAt(slot))} cannot be pulled -- pick a block");
        }
        else
        {
            r = SurvivalBuild.PlacePull(_edits, _store, _inv, slot, _pullShape, _strokeCubes);
            if (r.Outcome == SurvivalBuild.PlaceOutcome.Placed && !_pullFromShape)
                _shape.Set(_pullTemp.Cubes, _pullTemp.Count, _pullTemp.Edge);
            string skip = skipped > 0 ? $", {skipped} cube(s) skipped (around you / outside the wake radius)" : "";
            switch (r.Outcome)
            {
                case SurvivalBuild.PlaceOutcome.Placed:
                    _act = Act.Place;
                    _actAge = 0f;
                    _invDirty = true;
                    _status = (r.RanOut ? "<color=#ffd479>" : "") +
                              $"pulled {layers} layer(s): {r.Placed:N0} {Items.Name(r.Item)} in {r.CubesPaid}/{r.Cubes} cube(s)" +
                              (r.RanOut ? $" -- ran out ({r.Needed - r.Placed:N0} more needed)</color>" : "") +
                              $"{skip}, {_inv.CountAt(slot):N0} left";
                    break;
                case SurvivalBuild.PlaceOutcome.EmptySlot: PlaceRefused($"slot {slot + 1} is empty -- dig (LMB) to fill it"); break;
                case SurvivalBuild.PlaceOutcome.NotPlaceable: PlaceRefused($"{Items.Name(r.Item)} cannot be placed"); break;
                case SurvivalBuild.PlaceOutcome.TooFew:
                    PlaceRefused($"not enough {Items.Name(r.Item)}: the first {r.Edge * 0.1f:0.0} m cube needs more than the slot's {_inv.CountAt(slot)}");
                    break;
                case SurvivalBuild.PlaceOutcome.NotLoaded: PlaceRefused("not placed -- that ground is not loaded yet"); break;
                default: PlaceRefused($"nothing to fill there{skip}"); break;
            }
            Debug.Log($"[Pull] place {what}, n {_pullNormal}, {_pullUsed} layers: {r.Outcome} {r.CubesPaid}/{r.Cubes} cubes of {r.Edge}, " +
                      $"{r.Placed}/{r.Needed} cells, ranOut {r.RanOut}, skipped {skipped}, shape now {_shape.Count}");
        }
        CancelStroke();
        return r;
    }

    // ---- HUD ----

    /// The face being pulled (yellow), the pulled cubes (cyan paid / red past
    /// the run-out / orange dug / grey skipped) and the count. Big pulls draw
    /// the face's and the layers' boxes instead of every cube.
    private void DrawPullPreview(Camera cam, float k, float ui)
    {
        if (_pullShape == null) return;
        int e = _strokeEdge;
        bool dig = _pullUsed < 0;
        Color face = new Color(1f, 0.85f, 0.3f, 0.95f);
        Color paid = dig ? new Color(1f, 0.6f, 0.2f, 0.9f) : new Color(0.45f, 0.9f, 1f, 0.9f);
        Color unpaid = new Color(1f, 0.35f, 0.35f, 0.9f);
        Color skip = new Color(0.6f, 0.6f, 0.6f, 0.6f);
        int n = _strokeCubes.Count;
        if (n + _pullFace.Count <= StrokePreviewMaxCubes)
        {
            for (int i = 0; i < _pullFace.Count; i++) DrawVoxelHighlight(cam, _pullFace[i], face, 1.2f * k, e);
            for (int i = 0; i < n; i++)
                DrawVoxelHighlight(cam, _strokeCubes[i], i < _strokeCost.Affordable ? paid : unpaid, 1.6f * k, e);
            for (int i = 0; i < _strokeSkippedCubes.Count && i < StrokePreviewMaxCubes; i++)
                DrawVoxelHighlight(cam, _strokeSkippedCubes[i], skip, 1.2f * k, e);
        }
        else
        {
            DrawCubesBox(cam, _pullFace, 0, _pullFace.Count, e, face, 1.6f * k);
            DrawCubesBox(cam, _strokeCubes, 0, math.min(_strokeCost.Affordable, n), e, paid, 2f * k);
            if (_strokeCost.Affordable < n) DrawCubesBox(cam, _strokeCubes, _strokeCost.Affordable, n, e, unpaid, 2f * k);
        }

        string sign = _pullUsed > 0 ? "+" : "";
        string line;
        if (_pullUsed == 0)
            line = $"<b>PULL</b> face of {_pullFace.Count} cube(s) x {e * 0.1f:0.0} m   " +
                   "<color=#9fb6c9>drag along the face: out = place, back in = dig; Esc = cancel</color>";
        else if (dig)
            line = $"<b>PULL IN</b> {sign}{_pullUsed}   {n} cube(s)   {_strokeCost.Cells:N0} voxels   <color=#9fb6c9>release = dig, Esc = cancel</color>";
        else
        {
            int have = _inv.CountAt(_inv.Selected);
            string runs = _strokeCost.Affordable < n ? $"   <color=#ff9a9a>slot runs out after {_strokeCost.Affordable} of {n}</color>" : "";
            string sk = _strokeSkippedCubes.Count > 0 ? $"   <color=#bbbbbb>{_strokeSkippedCubes.Count} skipped</color>" : "";
            line = $"<b>PULL</b> {sign}{_pullUsed}   <b>{n}</b> cube(s) x {e * 0.1f:0.0} m   <b>{_strokeCost.AffordableCells:N0}</b> of {_strokeCost.Cells:N0} cells   " +
                   $"{Items.Name(_inv.ItemAt(_inv.Selected))} {have:N0}{runs}{sk}   <color=#9fb6c9>release = place, Esc = cancel</color>";
        }
        float vw = Screen.width / ui, vh = Screen.height / ui;
        DrawTextPanel(new Rect(vw * 0.5f - 300f, vh * 0.5f + 28f, 900f, 24f), ui, line);
    }

    /// One wire box around cubes [from, to) of `cubes`.
    private void DrawCubesBox(Camera cam, List<int3> cubes, int from, int to, int e, Color c, float width)
    {
        if (to <= from) return;
        int3 lo = cubes[from], hi = cubes[from];
        for (int i = from + 1; i < to; i++) { lo = math.min(lo, cubes[i]); hi = math.max(hi, cubes[i]); }
        DrawBoxHighlight(cam, lo, hi - lo + e, c, width);
    }

    // =====================================================================
    // -pulldemo (task 93b1): BLD2's acceptance screenshots through the real
    // paths (TrySurvivalPlace, the BLD1 stroke, BeginPull / RefreshPull /
    // CommitPull; only N is set directly instead of from the mouse). An
    // INTERACTIVE run: run-play.sh EXTRA="-pulldemo". The flattest dry stand
    // on a heading within 45 deg of away from the sun, noon, a demo stack of a
    // light block (refilled per pull, logged). Shots (persistentDataPath/PullDemo/):
    // 1 one block, 2 pulled into a line, 3 the wall pull mid-drag (preview),
    // 4 the wall, 5 the thick wall; 6 a hut's four walls (a pulled floor plate
    // + foundation, BLD1 lines pulled up), 7 the roof pull's preview, 8 the hut
    // with roof and a dug door at eye height, 9 both from above.
    // =====================================================================
    private IEnumerator PullDemo()
    {
        float t0 = Time.unscaledTime;
        while (Time.unscaledTime - t0 < 25f || _player == null || !_player.Ready) yield return null;
        SetGameMode(true);
        _showHelp = false;
        _demoHideState = true;
        _mode = MoveMode.Walk;
        SunCycle.SetHours(SunCycle.NoonHours);
        VoxelEngine.Rendering.LightingDriver.ClockPaused = true;
        string dir = System.IO.Path.Combine(Application.persistentDataPath, "PullDemo");
        System.IO.Directory.CreateDirectory(dir);
        if (_cam == null && Camera.main != null) _cam = Camera.main.GetComponent<PlaygroundFlyCamera>();

        // The flattest stand on a heading within 45 deg of away from the sun (as
        // -strokedemo); the structures are laid out on the voxel axis nearest
        // that heading (pulls follow the axes), the camera looks at each.
        int3 f = CoordMath.WorldToVoxel(_player.Motor.PositionM);
        float2 sxz = math.normalizesafe(SunCycle.Direction.xz);
        float awayYaw = math.degrees(math.atan2(-sxz.x, -sxz.y));
        // The spawn is on a mountain: first a flat dry site from the world map
        // (generator column heights, as -farwall), then walk there and wait for it.
        yield return FindFlatSite(f);
        if (_demoStandDev <= 3)
        {
            int3 site = _demoStand;
            _player.Motor.Teleport(new float3(site.x * 0.1f + 0.05f, (site.y + 20) * 0.1f, site.z * 0.1f + 0.05f));
            float tw = Time.unscaledTime;
            while (Time.unscaledTime - tw < 60f && DryTopY(site.x, site.z) < 0) yield return null;
            for (int i = 0; i < 120; i++) yield return null;
            Debug.Log($"[PullDemo] map site {site} after {Time.unscaledTime - tw:F0} s: top {DryTopY(site.x, site.z)}");
            f = site;
        }
        yield return FindDemoStand(f, awayYaw, new[] { -45f, -22.5f, 0f, 22.5f, 45f });
        int3 stand = _demoStand;
        float axisYaw = math.round(_demoYaw / 90f) * 90f;
        float ar = math.radians(axisYaw);
        int3 F = new int3((int)math.round(math.sin(ar)), 0, (int)math.round(math.cos(ar)));
        int3 R = new int3(F.z, 0, -F.x);
        int3 up = new int3(0, 1, 0);
        Debug.Log($"[PullDemo] stand {stand} (max step {_demoStandDev} voxels), heading {_demoYaw:F0}, axis {axisYaw:F0} " +
                  $"(away from the sun {awayYaw:F0}), F {F}, R {R}");
        _player.Motor.Teleport(new float3(stand.x * 0.1f + 0.05f, (stand.y + 1) * 0.1f + 0.02f, stand.z * 0.1f + 0.05f));
        _player.Motor.ResolveSpawn();
        _cam?.SetLook(_demoYaw, 8f);
        for (int i = 0; i < 90; i++) yield return null;

        _strokeDemoOwns = true;
        int3 ground0 = new int3(stand.x, DryTopY(stand.x, stand.z), stand.z);
        var groundMat = MaterialPalette.Of(_store.GetVoxel(ground0));
        byte block = Materials.FromTg2(TerrainGen.GenMat.Marble);   // light blocks: their faces read in the shots
        float bestDiff = -1f;
        foreach (byte m in new[] { Materials.FromTg2(TerrainGen.GenMat.Marble), Materials.FromTg2(TerrainGen.GenMat.BlueSlate),
                                   Materials.FromTg2(TerrainGen.GenMat.TealQuartzite) })
        {
            if (MaterialRules.IsMobile(m) || !Items.IsPlaceable(m)) continue;
            var c = MaterialPalette.Of(m);
            float diff = math.abs(c.R - groundMat.R) + math.abs(c.G - groundMat.G) + math.abs(c.B - groundMat.B);
            if (diff > bestDiff) { bestDiff = diff; block = m; }
        }
        _edgeIndex = SurvivalBuild.DefaultEdgeIndex;          // 0.4 m cubes
        int e = SurvivalBuild.CubeEdges[_edgeIndex];
        Debug.Log($"[PullDemo] block {Items.Name(block)} (ground {Items.Name(_store.GetVoxel(ground0))}), cube {e}");

        // Cube (a right, b ahead, h up) of a structure whose ground cube row is at y0.
        int3 sg = SurvivalBuild.SnapToGrid(stand, e);
        int3 Cell(int a, int b, int h, int y0) => new int3(sg.x, y0, sg.z) + (R * a + F * b) * e + up * (h * e);
        int Up(int y) => (y + e - 1) & ~(e - 1);                // the first grid row at or above y
        // The dry ground under cubes a0..a1 x b0..b1: lowest and highest top voxel.
        int2 GroundRange(int a0, int a1, int b0, int b1)
        {
            int lo = int.MaxValue, hi = int.MinValue;
            for (int a = a0; a <= a1; a++)
            for (int b = b0; b <= b1; b++)
            for (int q = 0; q < 4; q++)                       // the cube's 4 corner columns
            {
                int3 c = Cell(a, b, 0, 0) + new int3((q & 1) * (e - 1), 0, (q >> 1) * (e - 1));
                int y = DryTopY(c.x, c.z);
                if (y >= 0) { lo = math.min(lo, y); hi = math.max(hi, y); }
            }
            return lo == int.MaxValue ? new int2(stand.y, stand.y) : new int2(lo, hi);
        }
        void Refill() { _inv.Clear(); _inv.Add(block, Inventory.StackCap); SelectSlot(0); }

        // ---- Part 1: block -> line -> wall -> thick wall, left of the stand ----
        // The block sits on its own column; the line follows its row (into
        // higher ground, where solid cells are left alone).
        int y1 = Up(GroundRange(-12, -12, 12, 12).y + 1);
        LookAtM(((float3)Cell(-8, 13, 2, y1)) * 0.1f, 0f);
        Refill();
        var p0 = TrySurvivalPlace(Cell(-12, 12, 0, y1));
        Debug.Log($"[PullDemo] block: {p0.Outcome}, {p0.Placed} cells at {p0.Lo}; shape {_shape.Count}");
        for (int i = 0; i < 20; i++) yield return null;
        yield return DemoShot(dir, "1_block");

        yield return DemoPull(Cell(-12, 12, 0, y1), R, 9, block, "line (expect 10 cubes)", null, dir);
        yield return DemoShot(dir, "2_line");
        yield return DemoPull(Cell(-8, 12, 0, y1), up, 6, block, "wall (expect 70 cubes)", "3_wall_preview", dir);
        yield return DemoShot(dir, "4_wall");
        yield return DemoPull(Cell(-8, 12, 3, y1), -F, 2, block, "thick wall (expect 210 cubes)", null, dir);
        yield return DemoShot(dir, "5_thick_wall");

        // ---- Part 2: a hut right of the stand ----
        // Foundation: block -> line -> floor plate on the highest ground, pulled
        // down to the lowest (fills only the air under it). Walls: four BLD1
        // lines on the floor, each pulled up. Roof: a line along the front
        // wall's top, pulled back over the hut. Door: two BLD1 dig strokes.
        const int A0 = 2, A1 = 9, B0 = 16, B1 = 23, H = 6;   // 8 x 8 cubes (3.2 m), walls 6 cubes (2.4 m)
        int2 g2 = GroundRange(A0, A1, B0, B1);
        int y2 = Up(g2.y + 1);
        int down = (y2 - g2.x - 1 + e - 1) / e;              // layers from the floor to the lowest ground
        Debug.Log($"[PullDemo] hut ground {g2.x}..{g2.y}, floor row {y2}, foundation {down} layer(s) down");
        LookAtM(((float3)Cell((A0 + A1) / 2, (B0 + B1) / 2, 2, y2)) * 0.1f, 0f);
        Refill();
        var fb = TrySurvivalPlace(Cell(A0, B0, 0, y2));
        Debug.Log($"[PullDemo] floor block: {fb.Outcome}, shape {_shape.Count}");
        yield return DemoPull(Cell(A0, B0, 0, y2), R, A1 - A0, block, "floor line (expect 8 cubes)", null, dir);
        yield return DemoPull(Cell(A0, B0, 0, y2), F, B1 - B0, block, "floor plate (expect 64 cubes)", null, dir);
        if (down > 0) yield return DemoPull(Cell(A0, B0, 0, y2), -up, down, block, $"foundation (expect {64 * (down + 1)} cubes)", null, dir);

        (int a0, int b0, int a1, int b1)[] walls = { (A0, B0, A1, B0), (A1, B0 + 1, A1, B1), (A0, B1, A1 - 1, B1), (A0, B0 + 1, A0, B1 - 1) };
        foreach (var w in walls)
        {
            Refill();
            BeginPlaceStroke(Cell(w.a0, w.b0, 1, y2));
            _strokeTo = Cell(w.a1, w.b1, 1, y2);
            RefreshStroke(0f, force: true);
            var s = CommitPlaceStroke();
            Debug.Log($"[PullDemo] hut wall base {w}: {s.Outcome} {s.CubesPaid}/{s.Cubes} cubes, shape {_shape.Count}");
            yield return null;
            yield return DemoPull(Cell(w.a0, w.b0, 1, y2), up, H - 1, block, $"hut wall {w} (expect {_shape.Count * H} cubes)", null, dir);
        }
        for (int i = 0; i < 10; i++) yield return null;
        yield return DemoShot(dir, "6_hut_walls");

        Refill();
        BeginPlaceStroke(Cell(A0, B0, H + 1, y2));
        _strokeTo = Cell(A1, B0, H + 1, y2);
        RefreshStroke(0f, force: true);
        CommitPlaceStroke();
        yield return null;
        yield return DemoPull(Cell(A0, B0, H + 1, y2), F, B1 - B0, block, "roof (expect 64 cubes)", "7_roof_preview", dir);

        int door = (A0 + A1) / 2;
        for (int a = door; a <= door + 1; a++)
        {
            BeginDigStroke(Cell(a, B0, 1, y2));
            _strokeTo = Cell(a, B0, 5, y2);
            RefreshStroke(0f, force: true);
            CommitDigStroke();
        }
        for (int i = 0; i < 20; i++) yield return null;
        yield return DemoShot(dir, "8_hut");

        // From above: fly up behind the stand.
        _mode = MoveMode.Fly;
        Vector3 eye = new Vector3(stand.x, stand.y, stand.z) * 0.1f - new Vector3(F.x, 0f, F.z) * 2f + Vector3.up * 7f;
        Camera.main.transform.position = eye;
        LookAtM(((float3)Cell((A0 + A1) / 2 - 6, (B0 + B1) / 2 - 3, 0, y2)) * 0.1f, 0f);
        for (int i = 0; i < 60; i++) yield return null;
        yield return DemoShot(dir, "9_hut_above");

        _inv = Inventory.WithStarterKit();
        Debug.Log("[PullDemo] done");
        Application.Quit();
    }

    /// The nearest flat dry 14 m square within 1.5 km on the world map: column
    /// heights from the generator every 2 m, max - min <= 3 voxels, no water.
    /// _demoStand = its centre on the ground, _demoStandDev = its height range
    /// (int.MaxValue if none).
    private IEnumerator FindFlatSite(int3 f)
    {
        _demoStand = f;
        _demoStandDev = int.MaxValue;
        var src = Phase4Bootstrapper.Streamer?.TerrainGenSource;
        if (src == null) yield break;
        var spec = src.Spec; var view = src.Map.View; var o = src.O;
        for (int r = 0; r <= 15000; r += 400)
        {
            for (int dz = -r; dz <= r; dz += 400)
            for (int dx = -r; dx <= r; dx += 400)
            {
                if (math.max(math.abs(dx), math.abs(dz)) != r) continue;
                int lo = int.MaxValue, hi = int.MinValue;
                bool wet = false;
                for (int z = -70; z <= 70 && !wet && hi - lo <= 3; z += 20)
                for (int x = -70; x <= 70 && !wet && hi - lo <= 3; x += 20)
                {
                    var col = TerrainGen.ShapeField.Sample(in spec, in view, in o, f.x + dx + x, f.z + dz + z, 1);
                    lo = math.min(lo, col.height); hi = math.max(hi, col.height);
                    wet |= col.water > col.height;
                }
                if (wet || hi - lo > 3) continue;
                _demoStand = new int3(f.x + dx, hi, f.z + dz);
                _demoStandDev = hi - lo;
                Debug.Log($"[PullDemo] flat map site {_demoStand}, {r * 0.1f:F0} m away, height range {hi - lo} voxels");
                yield break;
            }
            yield return null;
        }
        Debug.Log("[PullDemo] no flat map site within 1.5 km: building at the spawn");
    }

    /// Turns the view at point `m` (metres), `yawBiasDeg` added to the yaw.
    private void LookAtM(float3 m, float yawBiasDeg)
    {
        Camera cam = Camera.main;
        if (cam == null || _cam == null) return;
        Vector3 d = new Vector3(m.x, m.y, m.z) - cam.transform.position;
        float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg + yawBiasDeg;
        float pitch = -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
        _cam.SetLook(yaw, pitch);
    }

    /// One demo pull through the real path, with an optional mid-pull shot.
    private IEnumerator DemoPull(int3 cubeLo, int3 n, int steps, byte block, string what, string previewShot, string dir)
    {
        _inv.Clear();
        _inv.Add(block, Inventory.StackCap);
        SelectSlot(0);
        int before = _inv.CountAt(0);
        BeginPull(cubeLo + (_shape.IsEmpty ? 0 : _shape.Edge >> 1), n);
        _pullDemoSteps = steps;
        RefreshPull(0f, force: true);
        Debug.Log($"[PullDemo] {what}: from shape {_pullFromShape}, face {_pullFace.Count} cubes, {_strokeCubes.Count} cubes to place, " +
                  $"cost {_strokeCost.Cells} cells, affordable {_strokeCost.Affordable}, skipped {_strokeSkippedCubes.Count}");
        if (previewShot != null)
        {
            for (int i = 0; i < 5; i++) yield return null;
            yield return DemoShot(dir, previewShot);
        }
        var r = CommitPull();
        _pullDemoSteps = int.MinValue;
        Debug.Log($"[PullDemo] {what}: {r.Outcome}, {r.CubesPaid}/{r.Cubes} cubes, placed {r.Placed}, slot {before} -> {_inv.CountAt(0)} " +
                  $"(conservation {(before - _inv.CountAt(0) == r.Placed ? "OK" : "BROKEN")}), shape now {_shape.Count}");
        for (int i = 0; i < 20; i++) yield return null;
    }
}
