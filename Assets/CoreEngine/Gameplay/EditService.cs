// Assets/CoreEngine/Gameplay/EditService.cs
//
// §13 Phase 6, file 3 of 6: "the §8.3 path (mining/building/prefab), tool tiers,
// wake-scan into the Phase-5 fluid pool".
//
// =========================================================================
// THIS IS AN ORCHESTRATOR, NOT A SECOND TERRAIN WRITER
// =========================================================================
// §8.3 opens "All edits go through EditService.SetVoxel", and CLAUDE.md's hard
// invariant says "The CPU is the sole terrain writer (ChunkStore.SetVoxel)".
// Those only look like they disagree. ChunkStore.SetVoxel already implements
// steps 1-4 of §8.3's own pseudocode -- uniform-chunk expansion, brick
// densification, the voxel write, and setting chunk.dirty / chunk.deltaDirty.
// It is frozen (§12) and it stays the writer.
//
// What §8.3 lists that ChunkStore does NOT do, because it cannot:
//   * mark the chunk dirty on the GPU MIRROR (§3.7) -- a different queue from
//     Chunk.dirty, and only a caller can reach it
//   * "scan 26-neighborhood for fluid/falling materials -> wake slots (§7.6)"
// Before this file, every edit path in the repo did that pair BY HAND:
// Playground.Edit, Phase5cEditStress, Phase6PlayerRig, Phase6CcdRig all write
// `SetVoxel(v, m); clipmap.MarkDirty(...); edits.NotifyEdited(v);`. Four
// copies of one sequence is the shape that produced the brush-guard bug
// (19b8ac6): one caller enforcing what another silently skips. This file is
// that sequence, once.
//
// =========================================================================
// WHAT THE PHASE 0.5 STUB USED TO SAY, AND WHY IT NO LONGER DOES
// =========================================================================
// SetVoxel threw NotImplementedException, correctly, because §13 Phase 5b said
// "THIS PHASE ADDS ONLY THE WAKE-SCAN HOOK... the full mining/building tool
// tiers arrive in Phase 6. Do not pull Phase 6's implementation forward." This
// is Phase 6. The wake-scan hook (AttachFluidSimulation / NotifyEdited) is
// unchanged and still additive; SetVoxel is now implemented on top of it.
//
// =========================================================================
// EDITING AN UNLOADED CHUNK IS REPORTED, NOT SWALLOWED (§9.4 again)
// =========================================================================
// ChunkStore.SetVoxel returns silently when the chunk is not resident -- right
// for a frozen low-level writer, wrong for a mining tool. A player digging at
// the edge of the streaming window would see the block simply not break, with
// nothing anywhere saying why. Every entry point here returns whether it
// actually wrote, and EditsRejectedNotResident counts the misses.

using System;
using Unity.Mathematics;
using VoxelEngine.Simulation;

public class EditService : IEditService
{
    /// The fluid simulation to wake on an edit, or null when no simulation is
    /// running (EditMode tests, the Phase 3/4 scenes). Null is a supported
    /// state: the wake scan simply does nothing, it never throws.
    private FluidGpuSimulation _fluid;

    /// The world to read materials from when deciding what is worth waking.
    private IWorldQuery _world;

    // ---- Phase 6 wiring ----
    private IEditService _writer;          // ChunkStore, in production
    private IVoxelResidency _residency;
    private IChunkDirtySink _dirty;

    // ---- Counters. Rigs assert on these; nothing branches on them. ----
    public long VoxelsWritten { get; private set; }
    public long EditsRejectedNotResident { get; private set; }
    public long EditsNoOp { get; private set; }
    public long WakeScansRun { get; private set; }

    public void ResetCounters()
    {
        VoxelsWritten = 0;
        EditsRejectedNotResident = 0;
        EditsNoOp = 0;
        WakeScansRun = 0;
    }

    // =====================================================================
    // Wiring
    // =====================================================================

    /// Phase 5b hook. Additive: nothing else in this class changed.
    public void AttachFluidSimulation(FluidGpuSimulation fluid, IWorldQuery world)
    {
        _fluid = fluid;
        _world = world;
    }

    public void DetachFluidSimulation()
    {
        _fluid = null;
        _world = null;
    }

    public bool HasFluidSimulation => _fluid != null;

    /// Phase 6 wiring. `writer` is the frozen ChunkStore.SetVoxel path; in
    /// production the same ChunkStore instance satisfies all three of writer,
    /// world and residency, and TerrainClipmap satisfies `dirty`.
    ///
    /// `dirty` may be null (a headless test with no mirror). `residency` may
    /// not: without it this class cannot tell "you cannot dig there, it is not
    /// loaded" from "you dug and nothing happened", which is the §9.4 mistake.
    public void AttachWorld(IEditService writer, IWorldQuery world,
                            IVoxelResidency residency, IChunkDirtySink dirty)
    {
        if (writer == null) throw new ArgumentNullException(nameof(writer));
        if (world == null) throw new ArgumentNullException(nameof(world));
        if (residency == null)
            throw new ArgumentNullException(nameof(residency),
                "EditService needs a residency source: ChunkStore.SetVoxel drops writes to " +
                "unloaded chunks silently, and a mining tool that cannot tell that apart " +
                "from a successful dig reports success for edits that never happened.");

        _writer = writer;
        _world = world;
        _residency = residency;
        _dirty = dirty;
    }

    public bool IsWired => _writer != null;

    // =====================================================================
    // §8.3's path
    // =====================================================================

    /// The §8.3 edit. Returns true if a voxel actually changed.
    ///
    /// Order matters and is §8.3's: write, mark the mirror dirty, then wake the
    /// neighbourhood. The wake scan reads the world AFTER the write so that a
    /// cell which just became Air is seen as Air by the fluid that is about to
    /// fall into it.
    public void SetVoxel(int3 worldVoxelCoord, byte material)
    {
        TrySetVoxel(worldVoxelCoord, material);
    }

    /// The form callers should prefer: says whether anything happened.
    public bool TrySetVoxel(int3 worldVoxelCoord, byte material)
    {
        if (_writer == null)
            throw new InvalidOperationException(
                "EditService.AttachWorld has not been called. The engine's terrain writer is " +
                "ChunkStore.SetVoxel (§8.3); this class orchestrates it and cannot run alone.");

        if (!_residency.IsResident(CoordMath.VoxelToChunk(worldVoxelCoord)))
        {
            EditsRejectedNotResident++;
            return false;
        }

        if (_world.GetVoxel(worldVoxelCoord) == material)
        {
            EditsNoOp++;
            return false;                      // §8.3's two no-op fast paths, observed here too
        }

        _writer.SetVoxel(worldVoxelCoord, material);
        _dirty?.MarkDirty(CoordMath.VoxelToChunk(worldVoxelCoord));
        VoxelsWritten++;

        NotifyEdited(worldVoxelCoord);
        return true;
    }

    /// THE HOOK. Call after any terrain edit to re-promote fluid the edit may
    /// have disturbed. §8.3's edit sequence ends with "scan 26-neighborhood for
    /// fluid/falling materials -> wake slots (§7.6)", and this is that step,
    /// factored out so it can be called from wherever the edit actually happened.
    ///
    /// Safe to call with no simulation attached, and safe to call for a cell
    /// outside the fluid region -- both are no-ops rather than errors, because
    /// an edit far from the player legitimately has no slots to wake.
    public void NotifyEdited(int3 worldVoxelCoord)
    {
        if (_fluid == null) return;
        WakeScansRun++;

        // The edited cell itself, plus its 26 neighbours. The full 3x3x3 rather
        // than the 6 faces because §7.4's Intent hierarchy includes diagonals:
        // a drop diagonally above a cell that just became Air has a legal move
        // into it and must not sleep through the change.
        for (int dz = -1; dz <= 1; dz++)
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            int3 n = worldVoxelCoord + new int3(dx, dy, dz);
            if (_world != null && !MaterialRules.IsMobile(_world.GetVoxel(n))) continue;
            _fluid.RequestWake(n);
        }
    }

    /// Batch form, for an edit that touched a box (§8.5's mass destruction will
    /// want this in Phase 6). Scans the box's shell plus its interior; callers
    /// with a huge box should prefer waking only the shell.
    public void NotifyEditedRegion(int3 minVoxel, int3 maxVoxel)
    {
        if (_fluid == null) return;
        WakeScansRun++;
        for (int z = minVoxel.z - 1; z <= maxVoxel.z + 1; z++)
        for (int y = minVoxel.y - 1; y <= maxVoxel.y + 1; y++)
        for (int x = minVoxel.x - 1; x <= maxVoxel.x + 1; x++)
        {
            int3 n = new int3(x, y, z);
            if (_world != null && !MaterialRules.IsMobile(_world.GetVoxel(n))) continue;
            _fluid.RequestWake(n);
        }
    }

    // =====================================================================
    // Batch variants (§8.3 "and batch variants"; §8.5 will build on these)
    // =====================================================================

    /// Fills an inclusive voxel box. Returns the number of voxels changed.
    ///
    /// ONE wake scan for the whole box, not one per voxel. A 40x40x40 fill is
    /// 64,000 voxels and 1.7 million neighbour probes if scanned per cell; the
    /// region form covers the same cells once. This is the difference between a
    /// prefab placement costing a frame and costing a second.
    public int SetBox(int3 lo, int3 hi, byte material)
    {
        RequireWired();
        int3 a = math.min(lo, hi), b = math.max(lo, hi);
        int changed = 0;

        for (int z = a.z; z <= b.z; z++)
        for (int y = a.y; y <= b.y; y++)
        for (int x = a.x; x <= b.x; x++)
            if (WriteOne(new int3(x, y, z), material)) changed++;

        if (changed > 0) NotifyEditedRegion(a, b);
        return changed;
    }

    /// Mines (or fills) a sphere. Returns voxels changed.
    ///
    /// Shares FluidGpuSimulation.SphereCovers with every other sphere brush in
    /// the engine, so "which cells does a radius-r brush touch" has exactly one
    /// answer -- the same reason that helper exists at all.
    public int SetSphere(int3 centre, int radius, byte material)
    {
        RequireWired();
        int changed = 0;
        for (int z = -radius; z <= radius; z++)
        for (int y = -radius; y <= radius; y++)
        for (int x = -radius; x <= radius; x++)
        {
            if (!FluidGpuSimulation.SphereCovers(x, y, z, radius)) continue;
            if (WriteOne(centre + new int3(x, y, z), material)) changed++;
        }

        if (changed > 0)
        {
            int3 r3 = new int3(radius, radius, radius);
            NotifyEditedRegion(centre - r3, centre + r3);
        }
        return changed;
    }

    /// §8.3's "prefab": stamps a dense block of materials with its origin at
    /// `origin`. `materials` is x-major within y within z, matching SetBox's
    /// iteration order. Materials.Air cells are SKIPPED rather than written, so
    /// a prefab can have a non-box silhouette.
    public int PlacePrefab(int3 origin, int3 dims, byte[] materials)
    {
        RequireWired();
        if (materials == null) throw new ArgumentNullException(nameof(materials));
        int expected = dims.x * dims.y * dims.z;
        if (materials.Length != expected)
            throw new ArgumentException(
                $"prefab is {dims.x}x{dims.y}x{dims.z} = {expected} cells but {materials.Length} " +
                "materials were given", nameof(materials));

        int changed = 0;
        int i = 0;
        for (int z = 0; z < dims.z; z++)
        for (int y = 0; y < dims.y; y++)
        for (int x = 0; x < dims.x; x++)
        {
            byte m = materials[i++];
            if (m == Materials.Air) continue;                 // silhouette, not a box
            if (WriteOne(origin + new int3(x, y, z), m)) changed++;
        }

        if (changed > 0) NotifyEditedRegion(origin, origin + dims - new int3(1, 1, 1));
        return changed;
    }

    /// One voxel, WITHOUT the wake scan -- for a caller driving its own batch
    /// across frames, which cannot use SetBox because it does not process the
    /// whole region at once.
    ///
    /// THE CONTRACT YOU ARE TAKING ON: you MUST call NotifyEditedRegion over
    /// the area you touched when the batch finishes, or fluid beside the edit
    /// never wakes and a breach silently does nothing. This is exactly the
    /// hand-rolled sequence EditService exists to stop people writing, and it
    /// is public only because §8.5's frame-split destruction genuinely cannot
    /// express itself with SetBox -- 400K voxels through TrySetVoxel would run
    /// a 27-cell wake scan per voxel, ~10.8 million probes for one detonation.
    /// DestructionReducer is the intended and only caller.
    public bool WriteVoxelDeferred(int3 v, byte material) => WriteOne(v, material);

    /// The per-voxel half of a batch: everything TrySetVoxel does EXCEPT the
    /// wake scan, which the batch does once at the end.
    private bool WriteOne(int3 v, byte material)
    {
        if (!_residency.IsResident(CoordMath.VoxelToChunk(v))) { EditsRejectedNotResident++; return false; }
        if (_world.GetVoxel(v) == material) { EditsNoOp++; return false; }

        _writer.SetVoxel(v, material);
        _dirty?.MarkDirty(CoordMath.VoxelToChunk(v));
        VoxelsWritten++;
        return true;
    }

    private void RequireWired()
    {
        if (_writer == null)
            throw new InvalidOperationException("EditService.AttachWorld has not been called.");
    }

    // =====================================================================
    // Tool tiers (§13's "tools at 10/40/200 vox/s")
    // =====================================================================

    /// A mining tool: a rate and a brush size. §13's Phase 6 scene calls for
    /// three, at 10 / 40 / 200 voxels per second.
    ///
    /// THE RATE IS THE POINT. Without it a "tool" is just a sphere fill and
    /// every tier behaves identically at frame rate; the tiers only mean
    /// anything if a slower tool takes longer to remove the same rock. Budget
    /// below is what enforces that.
    public readonly struct ToolTier
    {
        public readonly string Name;
        public readonly float VoxelsPerSecond;
        public readonly int RadiusVoxels;

        public ToolTier(string name, float voxelsPerSecond, int radiusVoxels)
        {
            Name = name;
            VoxelsPerSecond = voxelsPerSecond;
            RadiusVoxels = radiusVoxels;
        }
    }

    public static readonly ToolTier[] Tiers =
    {
        new ToolTier("hand",  10f, 1),
        new ToolTier("drill", 40f, 2),
        new ToolTier("bore", 200f, 3),
    };

    /// Accumulates a fractional voxel allowance across frames.
    ///
    /// A 10 vox/s tool at 60 Hz earns 0.167 voxels per frame. Truncating that
    /// per frame yields zero forever and the tool never digs; carrying the
    /// remainder is what makes a rate below one-per-frame work at all.
    public struct ToolBudget
    {
        private float _carry;

        /// Adds dt seconds of allowance and returns whole voxels available now.
        public int Accrue(float dt, float voxelsPerSecond)
        {
            if (dt <= 0f || voxelsPerSecond <= 0f) return 0;
            _carry += dt * voxelsPerSecond;
            int whole = (int)_carry;
            _carry -= whole;
            return whole;
        }

        public void Reset() => _carry = 0f;
        public float Carry => _carry;
    }

    /// Mines up to `budget` voxels from a sphere, nearest the centre first, and
    /// returns how many it actually removed.
    ///
    /// Nearest-first so a rate-limited tool eats a visible hole outward from
    /// where it is pointed, rather than scattering removals across the brush.
    public int MineSphereBudgeted(int3 centre, int radius, int budget, byte material = 0)
    {
        RequireWired();
        if (budget <= 0) return 0;

        // ITERATE SQUARED DISTANCE, NOT RADIUS. Cell distances-squared run
        // 0,1,2,3,4,5,6,... -- they are not perfect squares, so a loop matching
        // `x*x+y*y+z*z == r*r` silently skips every cell at d^2 = 2, 3, 5, 6...
        // For radius 2 that is most of the brush, and the tool would carve a
        // sparse star instead of a hole while still reporting the voxels it did
        // remove. Stepping d2 visits every covered cell exactly once, in
        // nearest-first order.
        int changed = 0;
        int maxD2 = radius * radius;
        for (int d2 = 0; d2 <= maxD2 && changed < budget; d2++)
        for (int z = -radius; z <= radius && changed < budget; z++)
        for (int y = -radius; y <= radius && changed < budget; y++)
        for (int x = -radius; x <= radius && changed < budget; x++)
        {
            if (x * x + y * y + z * z != d2) continue;         // shell d2, nearest first
            if (WriteOne(centre + new int3(x, y, z), material)) changed++;
        }

        if (changed > 0)
        {
            int3 r3 = new int3(radius, radius, radius);
            NotifyEditedRegion(centre - r3, centre + r3);
        }
        return changed;
    }
}
