// ==========================================
// Assets/Game/Phase5bBasin.cs
//
// §13 Phase 5b scene: "Phase5b_Basin -- same basin, now rendered live through
// the shipped raymarcher with real GPU fluid."
//
// Owns the real memory model (BrickDataPool / ChunkHandleAllocator / ChunkStore
// / TerrainClipmap), the GPU CA, the op-list readback, and -- running alongside
// on identical inputs -- the Phase 5a CPU reference as the correctness oracle
// (§7.2: "the CPU version is what tells you the GPU version is right").
//
// -------------------------------------------------------------------------
// THE TWO SIMULATIONS ARE FED THE SAME EDITS, NEVER COPIED FROM EACH OTHER
// -------------------------------------------------------------------------
// Every scenario applies its edit to BOTH the ChunkStore (GPU path) and the
// FluidReferenceCPU sandbox (oracle), from the same code, in the same order.
// Nothing is ever copied between them. If the oracle were seeded from GPU state
// -- or vice versa -- the comparison would be circular and would pass no matter
// how wrong the port was.
//
// The basin occupies world voxels [0..63] x [0..31] x [0..63], which is inside
// chunk (0,0,0), and the CPU sandbox is 8x4x8 bricks = exactly the same extent
// with the same coordinates. So a world voxel and a sandbox voxel are the same
// int3, and the comparison needs no coordinate translation to get wrong.
//
// -------------------------------------------------------------------------
// NO FLUID-SPECIFIC RENDERING CODE (§3.10)
// -------------------------------------------------------------------------
// §13's file 4 is "wire the shipped renderer to draw the fluid state as applied
// via the op-list." That wiring is this file constructing a TerrainClipmap --
// which sets TerrainClipmap.Active, which RaymarchFeature already reads -- and
// nothing else. The authoritative-byte rule means an applied op goes
// SetVoxel -> chunk dirty -> clipmap upload -> raymarcher, with the raymarcher
// having no idea fluid exists. If this file ever grows a fluid-specific draw
// path, §3.10 has been misread.

using System;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Memory;
using VoxelEngine.Mirror;
using VoxelEngine.Simulation;

public class Phase5bBasin : MonoBehaviour
{
    [Header("Fluid CA")]
    [SerializeField] private ComputeShader _fluidCA;
    [SerializeField] private int _slotCapacity = 65536;
    [SerializeField] private int _maxOpsPerFrame = 65536;

    // Basin extent, in world voxels. Power-of-two so the CA region can address
    // it with shifts (§0.1 invariant 2).
    public const int SX = 64, SY = 32, SZ = 64;

    public ChunkStore Store { get; private set; }
    public TerrainClipmap Clipmap { get; private set; }
    public FluidGpuSimulation Fluid { get; private set; }
    public FluidOpListReadback Readback { get; private set; }
    public FluidReferenceCPU Oracle { get; private set; }
    public EditService Edits { get; private set; }
    public int TicksRun { get; private set; }

    private BrickDataPool _pool;
    private ChunkHandleAllocator _allocator;

    /// One scenario, applied to BOTH simulations. Same shape as
    /// Phase5aBasin.BasinScenario, deliberately: the rig drives these the same
    /// way the 5a rig drove the buttons, so the two phases' evidence is
    /// comparable rather than merely similar.
    public readonly struct Scenario
    {
        public readonly string Id;
        public readonly Action<Phase5bBasin> Run;
        public Scenario(string id, Action<Phase5bBasin> run) { Id = id; Run = run; }
    }

    private Scenario[] _scenarios;
    public Scenario[] Scenarios => _scenarios ??= new[]
    {
        new Scenario("pour_water",  b => b.OpenSource(new int3(21, SY - 2, 32), Materials.Water, 250)),
        new Scenario("sand_column", b => b.DropColumn(new int3(32, SY - 2, 32), Materials.Sand, 24)),
        new Scenario("lava_vent",   b => b.OpenSource(new int3(42, SY - 2, 32), Materials.Lava, 62)),
        new Scenario("place_block", b => b.PlaceBlockIntoStream()),
        new Scenario("mine_drop",   b => b.MineAFallingDrop()),
    };

    // Continuous sources, emitted identically into both sims.
    private int3 _srcCell; private byte _srcMaterial; private int _srcRemaining;

    void Awake() => BuildBasin();

    public void BuildBasin()
    {
        Dispose();

        _pool = new BrickDataPool(EngineConfig.BRICK_POOL_CAP, rangeAware: true);
        _allocator = new ChunkHandleAllocator(1024);
        Store = new ChunkStore(_pool, _allocator);
        Store.InsertChunk(new Chunk { coord = int3.zero, isUniform = true, uniformMaterial = Materials.Air });

        int3 mirrorChunks = new int3(EngineConfig.WINDOW_CHUNKS_XZ,
                                     EngineConfig.MIRROR_CHUNKS_Y,
                                     EngineConfig.WINDOW_CHUNKS_XZ);
        Clipmap = new TerrainClipmap(mirrorChunks, _pool.Capacity);
        Clipmap.SetWindowOrigin(int3.zero);

        Oracle = new FluidReferenceCPU(SX / 8, SY / 8, SZ / 8);

        // Created BEFORE the geometry loops below, because PlaceBoth calls
        // Edits.NotifyEdited on every voxel it writes. Constructing it after
        // them null-referenced on the very first wall voxel. The fluid sim is
        // attached further down once it exists -- NotifyEdited with no
        // simulation attached is a documented no-op, which is exactly what the
        // basin's own construction wants: there is no fluid to wake yet.
        Edits = new EditService();

        // §13's "enclosed basin", built into BOTH sims from one loop so they
        // cannot start from different geometry.
        for (int z = 0; z < SZ; z++)
        for (int x = 0; x < SX; x++)
            PlaceBoth(new int3(x, 0, z), Materials.Stone);
        for (int y = 1; y < SY; y++)
        for (int i = 0; i < SX; i++)
        {
            PlaceBoth(new int3(i, y, 0), Materials.Stone);
            PlaceBoth(new int3(i, y, SZ - 1), Materials.Stone);
            PlaceBoth(new int3(0, y, i), Materials.Stone);
            PlaceBoth(new int3(SX - 1, y, i), Materials.Stone);
        }

        Oracle.PlayerVoxel = new int3(SX / 2, SY / 2, SZ / 2);
        Oracle.ActiveRadiusVoxels = EngineConfig.FLUID_ACTIVE_RADIUS_VOXELS;
        Oracle.ResetLedgerToCurrentState();

        Fluid = new FluidGpuSimulation(_fluidCA, new int3(SX, SY, SZ), _slotCapacity, _maxOpsPerFrame)
        {
            RegionOriginVoxels = int3.zero,
            PlayerVoxel = Oracle.PlayerVoxel,
            ActiveRadiusVoxels = EngineConfig.FLUID_ACTIVE_RADIUS_VOXELS,
        };
        Readback = new FluidOpListReadback(Fluid, Store);
        Edits.AttachFluidSimulation(Fluid, Store);

        Clipmap.UploadDirty(Store, _pool);
        _srcRemaining = 0;
        TicksRun = 0;
    }

    /// The ONE place an edit enters both simulations. Everything else routes here.
    public void PlaceBoth(int3 v, byte material)
    {
        Store.SetVoxel(v, material);
        Edits.NotifyEdited(v);          // §7.6 wake scan -- the Phase 5b hook
        Oracle.EditVoxel(v, material);
    }

    public void OpenSource(int3 cell, byte material, int budget)
    {
        _srcCell = cell; _srcMaterial = material; _srcRemaining = budget;
    }

    public void DropColumn(int3 top, byte material, int count)
    {
        for (int i = 0; i < count; i++)
        {
            int3 v = new int3(top.x, top.y - i, top.z);
            if (v.y <= 1) break;
            if (Store.GetVoxel(v) != Materials.Air) continue;
            PlaceBoth(v, material);
        }
    }

    public bool TryFindAirborneDrop(out int3 found)
    {
        for (int y = 2; y < SY; y++)
        for (int z = 1; z < SZ - 1; z++)
        for (int x = 1; x < SX - 1; x++)
        {
            int3 v = new int3(x, y, z);
            if (!MaterialRules.IsMobile(Store.GetVoxel(v))) continue;
            if (Store.GetVoxel(new int3(x, y - 1, z)) != Materials.Air) continue;
            found = v; return true;
        }
        found = default; return false;
    }

    public void PlaceBlockIntoStream()
    {
        if (!TryFindAirborneDrop(out int3 at)) return;
        PlaceBoth(new int3(at.x, at.y - 1, at.z), Materials.Stone);
    }

    public void MineAFallingDrop()
    {
        if (!TryFindAirborneDrop(out int3 at)) return;
        PlaceBoth(at, Materials.Air);
    }

    /// One CA tick on BOTH paths, in §3.9's frame order: terrain upload, fluid
    /// dispatch, then the readback issued off the critical path and last frame's
    /// ops applied.
    public void Tick()
    {
        TicksRun++;

        if (_srcRemaining > 0 && Store.GetVoxel(_srcCell) == Materials.Air)
        {
            PlaceBoth(_srcCell, _srcMaterial);
            _srcRemaining--;
        }

        Clipmap.UploadDirty(Store, _pool);      // GPU sees this frame's edits
        Fluid.Tick(Clipmap);                    // Clear/Promote/React/Intent/Commit/Sweep
        Readback.IssueReadback(Oracle.ActiveSlotCount);
        Readback.PumpAndApply();                // applies whatever has landed
        Clipmap.UploadDirty(Store, _pool);      // and the ops it just applied

        Oracle.Tick();                          // the oracle, same inputs, no coupling
    }

    /// Drain every outstanding readback so a steady-state comparison is not
    /// racing the pipeline. Rig only -- see FluidOpListReadback.DrainBlocking.
    public void SettleReadback()
    {
        Readback.DrainBlocking();
        Clipmap.UploadDirty(Store, _pool);
    }

    public int CountMaterialWorld(byte material)
    {
        int n = 0;
        for (int z = 0; z < SZ; z++)
        for (int y = 0; y < SY; y++)
        for (int x = 0; x < SX; x++)
            if (Store.GetVoxel(new int3(x, y, z)) == material) n++;
        return n;
    }

    /// Per-Y-layer count of a material -- the "final levels" §13 asks the GPU
    /// port to be compared on, as opposed to frame-exact positions (§7.8).
    public int[] LayerProfileWorld(byte material)
    {
        var p = new int[SY];
        for (int y = 0; y < SY; y++)
        for (int z = 0; z < SZ; z++)
        for (int x = 0; x < SX; x++)
            if (Store.GetVoxel(new int3(x, y, z)) == material) p[y]++;
        return p;
    }

    public int[] LayerProfileOracle(byte material)
    {
        var p = new int[SY];
        for (int y = 0; y < SY; y++)
        for (int z = 0; z < SZ; z++)
        for (int x = 0; x < SX; x++)
            if (Oracle.GetVoxel(x, y, z) == material) p[y]++;
        return p;
    }

    void OnDestroy() => Dispose();

    private void Dispose()
    {
        Readback?.Dispose(); Readback = null;
        Fluid?.Dispose(); Fluid = null;
        Clipmap?.Dispose(); Clipmap = null;
        _pool?.Dispose(); _pool = null;
        Store = null; Oracle = null; Edits = null;
    }
}
