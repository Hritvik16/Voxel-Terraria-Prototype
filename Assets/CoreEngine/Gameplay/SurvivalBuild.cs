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
}
