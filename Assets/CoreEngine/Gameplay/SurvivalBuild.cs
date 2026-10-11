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

using System;
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
        if (r.Removed == 0) return r;
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
        return r;
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

    /// Does the cube [lo, lo+edge) overlap the inclusive voxel box [boxLo, boxHi]?
    public static bool CubeOverlaps(int3 lo, int edge, int3 boxLo, int3 boxHi)
    {
        int3 hi = lo + (edge - 1);
        return math.all(lo <= boxHi) && math.all(hi >= boxLo);
    }
}
