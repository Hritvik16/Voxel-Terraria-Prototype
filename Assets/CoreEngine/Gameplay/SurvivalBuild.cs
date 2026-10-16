// Assets/CoreEngine/Gameplay/SurvivalBuild.cs
//
// GAMEPLAY STEP 1 (task 64, GAMEPLAY_PHASE_PLAN.md picks P2-P8): Survival's dig
// and place, as plain static functions over EditService + Inventory so EditMode
// proves conservation on a synthetic ChunkStore (SurvivalBuildTests) and
// Playground's mouse handler is a thin caller.
//
// THE CPU STAYS THE SOLE TERRAIN WRITER (plan guardrail 1): both paths go
// through EditService (MineSphereBudgeted's reporting overload, PlacePrefab),
// which writes through ChunkStore.SetVoxel, marks the mirror dirty and runs the
// §7.6 wake scan once per action.
//
// CONSERVATION, BOTH WAYS. Dig: every voxel EditService reports removed is
// credited as Items.ItemFor(material) -- or reported lost when the inventory is
// full (P8); fluids give nothing (P9). Place: the cost is exactly the cells the
// cube actually changed (PlacePrefab's return), taken from the selected slot.
//
// PLACING = A GRID-SNAPPED CUBE (P6), edge 1/2/4/8 voxels. The cube containing
// the target cell on an edge-aligned grid: lo = at & ~(edge-1), bitwise, so it
// is right for negative coordinates too (CLAUDE.md: integer coordinate math,
// power-of-two sizes). Only Air and fluid cells are filled; solid cells inside
// the cube are left alone and cost nothing. All or nothing (P7): fewer items in
// the slot than cells to fill places nothing.
//
// STROKES (BLD1, task 93a1, GAMEPLAY_PHASE_PLAN.md P7/P27): press, drag,
// release stamps the brush cube along the straight line between the two cubes
// -- LineCubes, an integer 3D DDA on the brush grid (add/compare only, no
// division) -- in ONE EditService.SetCubes call. Placing pays per cube in
// path order and STOPS at the first cube the slot cannot pay for in full
// (whole cubes only; P7 for strokes). Digging stamps Air the same way and
// credits the inventory exactly as Dig does.
//
// FACE PULL (BLD2, task 93b1, owner Q1 2026-10-15): the last shape placed
// (Shape) is extended by repeating its cubes on the aimed face along that
// face's normal -- FaceLayer, PullCubes, PullSteps -- and placed/dug through
// the stroke path above (PlacePull / DigPull: same costs, one SetCubes call).

using System;
using System.Collections.Generic;
using Unity.Mathematics;

public static class SurvivalBuild
{
    /// Cube edges in voxels, the Q key's cycle (P6). Powers of two (bitwise snap).
    public static readonly int[] CubeEdges = { 1, 2, 4, 8 };
    public const int DefaultEdgeIndex = 2;                     // 4 voxels = 0.4 m

    /// The edge-aligned cube origin containing `v`. `edge` must be a power of two.
    public static int3 SnapToGrid(int3 v, int edge) => v & ~(edge - 1);

    public struct DigResult
    {
        public int Removed;      // voxels EditService changed to Air
        public int Credited;     // voxels added to the inventory
        public int Lost;         // removed, had an item, did not fit (P8)
        public byte LastItem;    // the last item credited or lost (status line)
    }

    /// Digs up to `budget` voxels, nearest the centre first, and credits the
    /// inventory. `scratch` is a caller-owned int[256] (no per-frame garbage).
    public static DigResult Dig(EditService edits, Inventory inv, int3 centre, int radius, int budget, int[] scratch)
    {
        Array.Clear(scratch, 0, scratch.Length);
        var r = new DigResult { Removed = edits.MineSphereBudgeted(centre, radius, budget, scratch) };
        Credit(inv, scratch, ref r);
        return r;
    }

    /// Credits `scratch` (removed voxels per material) to the inventory.
    private static void Credit(Inventory inv, int[] scratch, ref DigResult r)
    {
        if (r.Removed == 0) return;
        for (int m = 0; m < 256; m++)
        {
            int n = scratch[m];
            if (n == 0) continue;
            byte item = Items.ItemFor((byte)m);
            if (item == Items.None) continue;                  // fluid: nothing to pick up
            int added = inv.Add(item, n);
            r.Credited += added;
            r.Lost += n - added;
            r.LastItem = item;
        }
    }

    public enum PlaceOutcome { Placed, EmptySlot, NotPlaceable, NothingToFill, TooFew, NotLoaded }

    public struct PlaceResult
    {
        public PlaceOutcome Outcome;
        public byte Item;
        public int Edge;         // the edge actually used (1 for torches)
        public int3 Lo;          // the cube's origin
        public int Needed;       // cells the cube would fill
        public int Placed;       // cells written (= taken from the slot)
    }

    /// The cube Place would use for this item and edge (torches: one voxel).
    public static int EdgeFor(byte item, int edge) => Items.PlacesSingleVoxel(item) ? 1 : edge;

    /// Cells of the cube at `lo` that placing would fill: Air or fluid.
    public static bool Fillable(byte m) => m == Materials.Air || MaterialRules.IsFluidMaterial(m);

    /// Places a cube of the slot's item at the grid cell containing `at`.
    public static PlaceResult Place(EditService edits, IWorldQuery world, Inventory inv, int slot, int3 at, int edge)
    {
        var r = new PlaceResult { Item = inv.ItemAt(slot) };
        if (inv.IsEmpty(slot)) { r.Outcome = PlaceOutcome.EmptySlot; return r; }
        if (!Items.IsPlaceable(r.Item)) { r.Outcome = PlaceOutcome.NotPlaceable; return r; }

        r.Edge = EdgeFor(r.Item, edge);
        r.Lo = SnapToGrid(at, r.Edge);
        int n = r.Edge;
        var cells = new byte[n * n * n];          // PlacePrefab's order: x within y within z
        int i = 0;
        for (int z = 0; z < n; z++)
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++, i++)
        {
            if (!Fillable(world.GetVoxel(r.Lo + new int3(x, y, z)))) continue;   // Air = skipped by PlacePrefab
            cells[i] = r.Item;
            r.Needed++;
        }
        if (r.Needed == 0) { r.Outcome = PlaceOutcome.NothingToFill; return r; }
        if (inv.CountAt(slot) < r.Needed) { r.Outcome = PlaceOutcome.TooFew; return r; }

        r.Placed = edits.PlacePrefab(r.Lo, new int3(n, n, n), cells);
        inv.TakeFromSlot(slot, r.Placed);
        r.Outcome = r.Placed > 0 ? PlaceOutcome.Placed : PlaceOutcome.NotLoaded;
        return r;
    }

    // =====================================================================
    // Strokes (BLD1)
    // =====================================================================

    /// A stroke holds at most this many cubes (25.6 m at 1 voxel, 204.8 m at 8);
    /// a longer drag is cut at the far end.
    public const int MaxStrokeCubes = 256;

    /// The brush-grid cubes on the straight line from the cube containing `a`
    /// to the cube containing `b`, both ends included, as cube ORIGINS in path
    /// order (`cubes` is cleared first). Integer 3D DDA (Bresenham): the axis
    /// with the largest grid delta steps every cube, the others step when their
    /// doubled error crosses -- position i = round(i * d / n), ties away from
    /// the start, with adds and compares only. Grid coordinates are `v >> log2(edge)`,
    /// an arithmetic shift, so negative coordinates floor like SnapToGrid.
    public static void LineCubes(int3 a, int3 b, int edge, List<int3> cubes, int max = MaxStrokeCubes)
    {
        if (edge <= 0 || (edge & (edge - 1)) != 0) throw new ArgumentException("edge must be a power of two", nameof(edge));
        cubes.Clear();
        int s = math.tzcnt(edge);
        int3 p = a >> s, q = b >> s;
        int3 d = math.abs(q - p), st = math.sign(q - p);
        int n = math.cmax(d);
        int3 acc = new int3(n, n, n);            // "one half" in units of 1/(2n)
        int twoN = n << 1;
        for (int i = 0; i <= n && cubes.Count < max; i++)
        {
            cubes.Add(p << s);
            acc += d << 1;
            if (acc.x >= twoN) { acc.x -= twoN; p.x += st.x; }
            if (acc.y >= twoN) { acc.y -= twoN; p.y += st.y; }
            if (acc.z >= twoN) { acc.z -= twoN; p.z += st.z; }
        }
    }

    /// Cells of the cube at `lo` that placing would fill (Air or fluid).
    public static int FillableCells(IWorldQuery world, int3 lo, int edge)
    {
        int n = 0;
        for (int z = 0; z < edge; z++)
        for (int y = 0; y < edge; y++)
        for (int x = 0; x < edge; x++)
            if (Fillable(world.GetVoxel(lo + new int3(x, y, z)))) n++;
        return n;
    }

    /// Cells of the cube at `lo` that digging would change (anything not Air).
    public static int DiggableCells(IWorldQuery world, int3 lo, int edge)
    {
        int n = 0;
        for (int z = 0; z < edge; z++)
        for (int y = 0; y < edge; y++)
        for (int x = 0; x < edge; x++)
            if (world.GetVoxel(lo + new int3(x, y, z)) != Materials.Air) n++;
        return n;
    }

    public struct StrokeCost
    {
        public int Cells;        // cells the whole path would fill (place) or remove (dig)
        public int Cubes;        // cubes in the path
        public int Affordable;   // cubes the slot pays for, from the start (place; dig = Cubes)
        public int AffordableCells;
    }

    /// The preview's numbers: what the stroke would cost, and how far the
    /// selected slot's `budget` carries it (whole cubes, path order).
    public static StrokeCost Cost(IWorldQuery world, List<int3> cubes, int edge, bool dig, int budget)
    {
        var c = new StrokeCost { Cubes = cubes.Count };
        bool paying = true;
        for (int i = 0; i < cubes.Count; i++)
        {
            int k = dig ? DiggableCells(world, cubes[i], edge) : FillableCells(world, cubes[i], edge);
            c.Cells += k;
            if (paying && (dig || c.AffordableCells + k <= budget)) { c.Affordable++; c.AffordableCells += k; }
            else paying = false;
        }
        return c;
    }

    public struct StrokeResult
    {
        public PlaceOutcome Outcome;
        public byte Item;
        public int Edge;
        public int Cubes;        // cubes in the path
        public int CubesPaid;    // cubes stamped (the slot paid for each in full)
        public int Needed;       // cells the whole path would fill
        public int Placed;       // cells written (= taken from the slot)
        public bool RanOut;      // the slot ran out before the end of the path
    }

    /// Places the slot's item along `cubes` (origins on the `edge` grid,
    /// LineCubes' output). Pays cube by cube in path order and stops at the
    /// first cube the slot cannot pay for in full; cost = cells placed.
    public static StrokeResult PlaceStroke(EditService edits, IWorldQuery world, Inventory inv, int slot,
                                           List<int3> cubes, int edge)
    {
        var r = new StrokeResult { Item = inv.ItemAt(slot), Edge = edge, Cubes = cubes.Count };
        if (inv.IsEmpty(slot)) { r.Outcome = PlaceOutcome.EmptySlot; return r; }
        if (!Items.IsPlaceable(r.Item)) { r.Outcome = PlaceOutcome.NotPlaceable; return r; }

        var c = Cost(world, cubes, edge, false, inv.CountAt(slot));
        r.Needed = c.Cells;
        r.CubesPaid = c.Affordable;
        r.RanOut = c.Affordable < c.Cubes;
        if (c.Cells == 0) { r.Outcome = PlaceOutcome.NothingToFill; return r; }
        if (c.AffordableCells == 0) { r.Outcome = PlaceOutcome.TooFew; return r; }

        r.Placed = edits.SetCubes(cubes, c.Affordable, edge, r.Item, fillOnly: true);
        inv.TakeFromSlot(slot, r.Placed);
        r.Outcome = r.Placed > 0 ? PlaceOutcome.Placed : PlaceOutcome.NotLoaded;
        return r;
    }

    /// Digs every cube along `cubes` and credits the inventory as Dig does
    /// (fluids give nothing, a full inventory loses the overflow, P8/P9).
    public static DigResult DigStroke(EditService edits, Inventory inv, List<int3> cubes, int edge, int[] scratch)
    {
        Array.Clear(scratch, 0, scratch.Length);
        var r = new DigResult { Removed = edits.SetCubes(cubes, cubes.Count, edge, Materials.Air, false, scratch) };
        Credit(inv, scratch, ref r);
        return r;
    }

    /// Does the cube [lo, lo+edge) overlap the inclusive voxel box [boxLo, boxHi]?
    public static bool CubeOverlaps(int3 lo, int edge, int3 boxLo, int3 boxHi)
    {
        int3 hi = lo + (edge - 1);
        return math.all(lo <= boxHi) && math.all(hi >= boxLo);
    }

    // =====================================================================
    // Face pull (BLD2, task 93b1, owner 2026-10-15 Q1): aim at a face of the
    // last shape placed, drag along the face normal, and the shape's cubes on
    // that face are repeated N times along it -- block -> line -> plane -> box.
    // N < 0 digs the same cubes inward, starting with the face layer itself.
    // =====================================================================

    /// The shape a pull extends: the cubes of the player's most recent stroke
    /// (a click, a BLD1 line, or the previous pull plus what it added), on one
    /// brush grid. Cube origins in placement order and a set for membership.
    public sealed class Shape
    {
        /// More cubes than this and the shape is dropped (pulls fall back to
        /// the single aimed cube); keeps the face scan and the intact check cheap.
        public const int MaxCubes = 8192;

        public readonly List<int3> Cubes = new List<int3>();
        private readonly HashSet<int3> _set = new HashSet<int3>();
        public int Edge { get; private set; }
        public int3 Lo { get; private set; }     // inclusive voxel box of every cube
        public int3 Hi { get; private set; }
        public bool IsEmpty => Cubes.Count == 0;
        public int Count => Cubes.Count;

        public void Clear() { Cubes.Clear(); _set.Clear(); Edge = 0; }

        public bool Contains(int3 cubeLo) => _set.Contains(cubeLo);

        /// The shape becomes the first `count` cubes of `cubes`.
        public void Set(IReadOnlyList<int3> cubes, int count, int edge)
        {
            Clear();
            Edge = edge;
            Add(cubes, count);
        }

        /// Adds the first `count` cubes (a pull's paid cubes). Over MaxCubes -> cleared.
        public void Add(IReadOnlyList<int3> cubes, int count)
        {
            count = math.min(count, cubes.Count);
            for (int i = 0; i < count; i++)
                if (_set.Add(cubes[i])) Cubes.Add(cubes[i]);
            if (Cubes.Count > MaxCubes) { Clear(); return; }
            Bounds();
        }

        /// Removes the cubes a dig pull took out.
        public void Remove(IReadOnlyList<int3> cubes, int count)
        {
            count = math.min(count, cubes.Count);
            bool any = false;
            for (int i = 0; i < count; i++) any |= _set.Remove(cubes[i]);
            if (!any) return;
            Cubes.RemoveAll(c => !_set.Contains(c));
            if (Cubes.Count == 0) Clear(); else Bounds();
        }

        /// Does the shape's box overlap the inclusive voxel box [lo, hi]? (a dig there breaks it)
        public bool Overlaps(int3 lo, int3 hi) => !IsEmpty && math.all(Lo <= hi) && math.all(Hi >= lo);

        /// Still standing: the centre voxel of every cube is solid (not Air, not
        /// fluid). Cheap (one read per cube); a dig the caller did not report, a
        /// bomb or a Creative edit is caught here.
        public bool Intact(IWorldQuery world)
        {
            if (IsEmpty) return false;
            int h = Edge >> 1;
            for (int i = 0; i < Cubes.Count; i++)
                if (Fillable(world.GetVoxel(Cubes[i] + h))) return false;
            return true;
        }

        private void Bounds()
        {
            if (Cubes.Count == 0) return;
            int3 lo = Cubes[0], hi = Cubes[0];
            for (int i = 1; i < Cubes.Count; i++) { lo = math.min(lo, Cubes[i]); hi = math.max(hi, Cubes[i]); }
            Lo = lo;
            Hi = hi + (Edge - 1);
        }
    }

    /// Pull steps are capped at this many layers.
    public const int MaxPullSteps = 64;

    /// The most cubes one pull writes: 2,048, and never more cells than BLD1's
    /// longest stroke at 0.8 m (256 x 512 = 131,072) -- one EditService call.
    public static int MaxPullCubes(int edge) => math.min(2048, 131072 >> (3 * math.tzcnt(edge)));

    /// The face of `voxel` a ray enters through, as an outward unit normal
    /// (one axis). `origin` and `dir` in voxel units. Slab test: the axis whose
    /// entry plane is crossed last is the entry face. A ray starting inside
    /// (or parallel on every axis) gives the face against the largest |dir|.
    public static int3 EntryFace(float3 origin, float3 dir, int3 voxel)
    {
        float3 lo = voxel, hi = lo + 1f;
        float best = float.NegativeInfinity;
        int axis = 0;
        for (int a = 0; a < 3; a++)
        {
            if (math.abs(dir[a]) < 1e-9f) continue;
            float tEnter = ((dir[a] > 0f ? lo[a] : hi[a]) - origin[a]) / dir[a];
            if (tEnter > best) { best = tEnter; axis = a; }
        }
        if (float.IsNegativeInfinity(best))
        {
            float3 ad = math.abs(dir);
            axis = ad.x >= ad.y && ad.x >= ad.z ? 0 : ad.y >= ad.z ? 1 : 2;
        }
        int3 n = int3.zero;
        n[axis] = dir[axis] > 0f ? -1 : 1;
        return n;
    }

    /// The shape's cubes on its face toward `n`: every cube whose neighbour
    /// one cube along `n` is not part of the shape, in the shape's order. A
    /// line pulled sideways gives the whole line, pulled along itself its end
    /// cube; a box gives the layer on that side.
    public static void FaceLayer(Shape shape, int3 n, List<int3> face)
    {
        face.Clear();
        int3 step = n * shape.Edge;
        for (int i = 0; i < shape.Cubes.Count; i++)
            if (!shape.Contains(shape.Cubes[i] + step)) face.Add(shape.Cubes[i]);
    }

    /// The cubes a pull of `steps` layers writes, layer by layer from the face
    /// (so a short slot fills the nearest layers first). steps > 0: the face
    /// repeated at +1..+steps cubes along `n` (placed); steps < 0: the face
    /// layer itself and |steps| - 1 more inward (dug). Whole layers only, cut at
    /// MaxPullSteps and `maxCubes`. Returns the signed steps actually used.
    public static int PullCubes(List<int3> face, int edge, int3 n, int steps, List<int3> cubes, int maxCubes)
    {
        cubes.Clear();
        if (face.Count == 0 || steps == 0) return 0;
        int layers = math.min(math.min(math.abs(steps), MaxPullSteps), maxCubes / face.Count);
        int sgn = steps > 0 ? 1 : -1;
        for (int k = 1; k <= layers; k++)
        {
            int3 off = n * (edge * (sgn > 0 ? k : 1 - k));
            for (int i = 0; i < face.Count; i++) cubes.Add(face[i] + off);
        }
        return sgn * layers;
    }

    /// How many cubes the drag has pulled: the point on the line through the
    /// face centre along `n` (metres) closest to the aim ray, in cube lengths,
    /// rounded. A ray (nearly) parallel to the line, or one whose closest
    /// point is behind the eye, keeps `prev`.
    public static int PullSteps(float3 rayOrigin, float3 rayDir, float3 faceCentre, int3 n, float cubeM, int prev)
    {
        float3 d = math.normalizesafe(rayDir), u = n;
        float b = math.dot(d, u);
        float denom = 1f - b * b;
        if (denom < 1e-3f) return prev;
        float3 w = rayOrigin - faceCentre;
        float dw = math.dot(d, w), uw = math.dot(u, w);
        float tRay = (b * uw - dw) / denom;
        if (tRay <= 0f) return prev;
        float s = (uw - b * dw) / denom;
        return math.clamp((int)math.round(s / cubeM), -MaxPullSteps, MaxPullSteps);
    }

    /// Places a pull's cubes (PullCubes' output, after the caller's guards)
    /// exactly as a stroke: path order, whole cubes, stops when the slot runs
    /// out, one EditService call. The paid cubes join `shape`.
    public static StrokeResult PlacePull(EditService edits, IWorldQuery world, Inventory inv, int slot,
                                         Shape shape, List<int3> cubes)
    {
        var r = PlaceStroke(edits, world, inv, slot, cubes, shape.Edge);
        if (r.Outcome == PlaceOutcome.Placed) shape.Add(cubes, r.CubesPaid);
        return r;
    }

    /// Digs a pull's cubes (steps < 0) and credits the inventory as a dig
    /// stroke does; the cubes dug leave `shape`.
    public static DigResult DigPull(EditService edits, Inventory inv, Shape shape, List<int3> cubes, int[] scratch)
    {
        var r = DigStroke(edits, inv, cubes, shape.Edge, scratch);
        shape.Remove(cubes, cubes.Count);
        return r;
    }
}
