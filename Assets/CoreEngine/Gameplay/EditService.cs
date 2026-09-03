// Assets/CoreEngine/Simulation/EditService.cs
//
// Phase 5b, file 3 of §13's ordered list: the wake-scan hook.
//
// §13 is explicit about how far this file may go this phase:
//   "Wake-scan hook in EditService (CPU->GPU upload of newly-disturbed regions
//    -- immediate, not a readback). EditService at this point is still the
//    Phase 0.5 stub -- THIS PHASE ADDS ONLY THE WAKE-SCAN HOOK TO IT; the full
//    mining/building tool tiers arrive in Phase 6. Adding a hook to a stub is
//    additive and legal; do not pull Phase 6's implementation forward."
//
// So SetVoxel still throws NotImplementedException. That is not an oversight and
// it is not a regression -- it is the Phase 0.5 stub, unchanged, and Phase 6
// owns filling it in. What is new is only the hook and the wake scan itself.
//
// WHERE THE REAL EDIT PATH IS, so nobody wires this up wrongly: the engine's
// single terrain writer is ChunkStore.SetVoxel (§8.3, and CLAUDE.md's hard
// invariant). ChunkStore implements IEditService directly. FluidOpListReadback
// applies fluid ops through ChunkStore.SetVoxel, NOT through this class.
//
// WHY THE WAKE SIGNAL IS AN UPLOAD AND NOT A READBACK (§7.6): "Any adjacent
// edit re-promotes: the edit path scans the edit's neighbourhood for
// fluid/falling materials and wakes GPU slots -- this wake signal is a small
// CPU->GPU upload (§3.9), not a readback, so it is IMMEDIATE." Dormant fluid
// therefore costs nothing until something disturbs it, which is what makes
// static generated water (§5.5) free to have in the world.

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

    /// PHASE 0.5 STUB, DELIBERATELY UNCHANGED. §13 Phase 5b adds the hook only;
    /// Phase 6 ("Physics & Editing") implements this. Callers that need to write
    /// terrain today use ChunkStore.SetVoxel, which is the §8.3 path.
    public void SetVoxel(int3 worldVoxelCoord, byte material)
    {
        throw new NotImplementedException(
            "EditService.SetVoxel is the Phase 0.5 stub; Phase 6 implements it. " +
            "The engine's terrain writer today is ChunkStore.SetVoxel (§8.3). " +
            "Phase 5b added only the wake-scan hook to this class -- see NotifyEdited.");
    }

    /// THE HOOK. Call after any terrain edit to re-promote fluid the edit may
    /// have disturbed. §8.3's edit sequence ends with "scan 26-neighborhood for
    /// fluid/falling materials -> wake slots (§7.6)", and this is that step,
    /// factored out so it can be called from wherever the edit actually happened
    /// while SetVoxel above is still a stub.
    ///
    /// Safe to call with no simulation attached, and safe to call for a cell
    /// outside the fluid region -- both are no-ops rather than errors, because
    /// an edit far from the player legitimately has no slots to wake.
    public void NotifyEdited(int3 worldVoxelCoord)
    {
        if (_fluid == null) return;

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
        for (int z = minVoxel.z - 1; z <= maxVoxel.z + 1; z++)
        for (int y = minVoxel.y - 1; y <= maxVoxel.y + 1; y++)
        for (int x = minVoxel.x - 1; x <= maxVoxel.x + 1; x++)
        {
            int3 n = new int3(x, y, z);
            if (_world != null && !MaterialRules.IsMobile(_world.GetVoxel(n))) continue;
            _fluid.RequestWake(n);
        }
    }
}
