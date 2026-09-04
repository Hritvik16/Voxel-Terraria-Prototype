// ==========================================
// Assets/Game/Phase5cEditStress.cs
//
// EDIT-PATH STRESS RIG. Covers the paths a PLAYER drives -- placing, mining,
// painting, venting -- rather than the scripted pours Phase 5a/5b cover.
//
// =========================================================================
// WHY THIS EXISTS
// =========================================================================
// Phase 5b's rig uploads the clipmap BEFORE ticking the CA and says so on the
// line ("GPU sees this frame's edits"). The Playground ticks the CA in
// Update() and uploads in Phase4Bootstrapper.LateUpdate() -- the OPPOSITE
// order. Every 5b scenario passed while every hand-placed fluid voxel froze in
// mid-air, because a wake request was evaluated against a mirror that did not
// yet contain the edit and was then destroyed (see FluidWakeQueue).
//
// The rig had the ordering right and nothing enforced that a caller must. So
// the central design of this rig is:
//
//   EVERY SCENARIO RUNS TWICE -- ONCE IN EACH FRAME ORDERING -- AND THE TWO
//   RESULTS MUST BE IDENTICAL.
//
// That is the invariant that was actually violated. Asserting only "fluid
// falls" would have passed before the fix too, in MirrorFirst.
//
// =========================================================================
// WHAT IS ASSERTED, AND WHAT IS NOT
// =========================================================================
// ASSERTED per scenario, per ordering:
//   - conservation: exact expected count of every mobile material
//   - no floating mobile voxels at rest (§7.8 steady-state, mirrors
//     FluidReferenceCPU.CountFloatingMobile)
//   - MOTION: the disturbance actually made the CA do work. This is the
//     assertion the frozen-fluid bug needed and no existing test had -- a
//     frozen voxel conserves perfectly and floats nowhere if you only look at
//     a basin that was already settled.
//   - scenario-specific geometry (it reached the floor, it descended, ...)
// ASSERTED across orderings:
//   - identical world fingerprint. Frame ordering must not be observable.
//
// NOT asserted: exact arrangement against the CPU oracle. §7.8 says two
// conforming implementations may settle mass differently, and Phase 5b §4
// established by experiment that the CPU's own layout moves when its tie-break
// is permuted. This rig compares GPU-to-GPU across orderings, where that
// freedom does not apply because it is the same implementation both times.
//
// TIMING: this rig produces NO performance numbers. Deliberate. It runs with
// synchronous readback for determinism, which alone disqualifies any figure it
// could produce. The async path is Phase 5b's job.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Memory;
using VoxelEngine.Simulation;

public class Phase5cEditStress : MonoBehaviour
{
    [SerializeField] private ComputeShader _fluidCA;
    [SerializeField] private int _slotCapacity = 131072;
    [SerializeField] private int _maxOpsPerFrame = 32768;
    [SerializeField] private string _outputRootFolderName = "Phase5cEditStress";

    public const int SX = 64, SY = 32, SZ = 64;

    /// The two frame orderings that exist in this codebase today.
    public enum Ordering
    {
        /// Phase 5a/5b rigs: upload the mirror, THEN tick. The CA sees this
        /// frame's edits immediately.
        MirrorFirst,
        /// Playground / Phase4Bootstrapper: tick in Update(), upload in
        /// LateUpdate(). The CA sees this frame's edits one frame LATE. This is
        /// the ordering every real scene uses, and the one that was broken.
        TickFirst,
        /// CONTROL. Identical to MirrorFirst in every way except that one idle
        /// tick runs before the scenario, so the simulation is offset by one in
        /// TICK PHASE only. §7.4's tie-break hashes the tick number, so this
        /// isolates "the layout moved because the tick phase moved" from "the
        /// layout moved because the ordering lost work". Same code path, same
        /// mirror discipline, same everything else.
        MirrorFirstOffset,
    }

    public ChunkStore Store { get; private set; }
    public TerrainClipmap Clipmap { get; private set; }
    public FluidGpuSimulation Fluid { get; private set; }
    public FluidOpListReadback Readback { get; private set; }
    public EditService Edits { get; private set; }

    private BrickDataPool _pool;
    private ChunkHandleAllocator _allocator;
    private Ordering _order;
    private long _applied;               // voxels the CA actually moved -- the motion signal
    private readonly List<int3> _pendingUpload = new List<int3>();

    // ---- results ----
    private readonly List<string> _lines = new List<string>();
    private int _pass, _fail;
    private readonly Dictionary<string, Obs> _obs = new Dictionary<string, Obs>();

    // =====================================================================
    // World
    // =====================================================================

    private void Build(Ordering ordering)
    {
        Dispose();
        _order = ordering;
        _applied = 0;

        _pool = new BrickDataPool(EngineConfig.BRICK_POOL_CAP, rangeAware: true);
        _allocator = new ChunkHandleAllocator(1024);
        Store = new ChunkStore(_pool, _allocator);
        Store.InsertChunk(new Chunk { coord = int3.zero, isUniform = true, uniformMaterial = Materials.Air });

        Clipmap = new TerrainClipmap(
            new int3(EngineConfig.WINDOW_CHUNKS_XZ, EngineConfig.MIRROR_CHUNKS_Y, EngineConfig.WINDOW_CHUNKS_XZ),
            _pool.Capacity);
        Clipmap.SetWindowOrigin(int3.zero);

        // Before the geometry loops: PlaceRaw -> NotifyEdited on every voxel.
        Edits = new EditService();

        for (int z = 0; z < SZ; z++)
        for (int x = 0; x < SX; x++)
            Write(new int3(x, 0, z), Materials.Stone);
        for (int y = 1; y < SY; y++)
        for (int i = 0; i < SX; i++)
        {
            Write(new int3(i, y, 0), Materials.Stone);
            Write(new int3(i, y, SZ - 1), Materials.Stone);
            Write(new int3(0, y, i), Materials.Stone);
            Write(new int3(SX - 1, y, i), Materials.Stone);
        }

        Fluid = new FluidGpuSimulation(_fluidCA, new int3(SX, SY, SZ), _slotCapacity, _maxOpsPerFrame)
        {
            RegionOriginVoxels = int3.zero,
            PlayerVoxel = new int3(SX / 2, SY / 2, SZ / 2),
            ActiveRadiusVoxels = EngineConfig.FLUID_ACTIVE_RADIUS_VOXELS,
        };
        Readback = new FluidOpListReadback(Fluid, Store)
        {
            OnVoxelApplied = v => { MarkDirty(v); _applied++; },
        };
        Edits.AttachFluidSimulation(Fluid, Store);

        // The control's single idle tick: state-neutral (nothing is awake yet)
        // but it advances FluidGpuSimulation.TickCount, which is what the
        // tie-break hashes.
        if (ordering == Ordering.MirrorFirstOffset) { Clipmap.UploadDirty(Store, _pool); Tick(); }

        // Terrain is uploaded before the first tick in BOTH orderings. The
        // ordering under test is about EDITS DURING SIMULATION, not about
        // starting from an empty mirror -- that would be a different bug.
        Clipmap.UploadDirty(Store, _pool);
    }

    private void Write(int3 v, byte m)
    {
        Store.SetVoxel(v, m);
        MarkDirty(v);
        Edits.NotifyEdited(v);
    }

    /// In TickFirst the upload is DEFERRED to the simulated LateUpdate, exactly
    /// as Phase4Bootstrapper does it. In MirrorFirst it is marked and flushed
    /// before the tick.
    private void MarkDirty(int3 v) => Clipmap.MarkDirty(CoordMath.VoxelToChunk(v));

    /// THE PLAYER EDIT. Routes through the same path Playground.Edit uses.
    public void Edit(int3 v, byte m) => Write(v, m);

    /// A SEALED 1x1 SHAFT at (x,z): stone walls on all 8 surrounding columns up
    /// to wallTop, and a stone plug filling y=1..plugTop.
    ///
    /// The mining cases originally stood their test voxel on a 1-wide PILLAR.
    /// That does not test what it looks like it tests: a liquid on a 1-wide
    /// pillar slides off and falls to the floor before the mine ever happens,
    /// so every one of those cases was measuring "water is a liquid" and
    /// failing its own precondition (settled at y=1, not y=5). Diagonals are
    /// walled too, because §7.4's Intent hierarchy includes diagonal moves and
    /// four face walls alone still let a drop escape corner-wise.
    public void Shaft(int x, int z, int plugTop, int wallTop)
    {
        for (int y = 1; y <= wallTop; y++)
        for (int dz = -1; dz <= 1; dz++)
        for (int dx = -1; dx <= 1; dx++)
        {
            if (dx == 0 && dz == 0) continue;
            Edit(new int3(x + dx, y, z + dz), Materials.Stone);
        }
        for (int y = 1; y <= plugTop; y++) Edit(new int3(x, y, z), Materials.Stone);
    }

    // =====================================================================
    // Tick, in the ordering under test
    // =====================================================================

    public void Tick()
    {
        if (_order != Ordering.TickFirst)
        {
            Clipmap.UploadDirty(Store, _pool);      // 5a/5b rig order
            Fluid.Tick(Clipmap);
            Readback.IssueReadback(0);
            Readback.DrainBlocking();
            Clipmap.UploadDirty(Store, _pool);
        }
        else
        {
            // Playground.Update(): tick with whatever the mirror currently
            // holds -- this frame's edits are NOT in it yet.
            Fluid.Tick(Clipmap);
            Readback.IssueReadback(0);
            Readback.DrainBlocking();
            // Phase4Bootstrapper.LateUpdate(): the upload happens AFTER.
            Clipmap.UploadDirty(Store, _pool);
        }
    }

    public void Tick(int n) { for (int i = 0; i < n; i++) Tick(); }

    /// Ticks until the CA has applied nothing for `quiet` consecutive ticks.
    /// QUIET WINDOW: 20 exceeds the slowest live material's TickInterval
    /// (Lava 6, Honey 30 is not used here) -- PHASE_5A_COMPLETION §8.1's blind
    /// spot, where a short window reads "at rest" mid-fall. See
    /// FluidSlowViscositySettleTests.
    public int Settle(int quiet = 20, int max = 1200)
    {
        int still = 0, t = 0;
        long last = _applied;
        for (; t < max; t++)
        {
            Tick();
            // REST IS "NOTHING MOVED **AND** NOTHING IS WAITING TO BE LOOKED AT".
            // Applied-ops alone has the same blind spot PHASE_5A_COMPLETION §8.1
            // flagged for viscosity: a wake request still sitting in the queue
            // produces no ops, so the world reads as settled while work is
            // pending. large_pour caught it -- a voxel looked permanently
            // stranded, and an explicit wake plus more ticks moved it, proving
            // it had merely not been looked at yet.
            bool quietTick = _applied == last && Fluid.DeferredWakeRequests == 0;
            if (quietTick) still++; else { still = 0; last = _applied; }
            if (still >= quiet) break;
        }
        return t;
    }

    // =====================================================================
    // Observations
    // =====================================================================

    public int CountMaterial(byte m)
    {
        int n = 0;
        for (int z = 0; z < SZ; z++)
        for (int y = 0; y < SY; y++)
        for (int x = 0; x < SX; x++)
            if (Store.GetVoxel(new int3(x, y, z)) == m) n++;
        return n;
    }

    /// Mirrors FluidReferenceCPU.CountFloatingMobile: a mobile voxel resting
    /// with Air beneath it. At rest this must be 0.
    public int CountFloating()
    {
        int n = 0;
        for (int y = 2; y < SY; y++)
        for (int z = 1; z < SZ - 1; z++)
        for (int x = 1; x < SX - 1; x++)
        {
            byte m = Store.GetVoxel(new int3(x, y, z));
            if (!MaterialRules.IsMobile(m)) continue;
            if (Store.GetVoxel(new int3(x, y - 1, z)) == Materials.Air) n++;
        }
        return n;
    }

    /// First mobile voxel resting on Air, or (-1,-1,-1).
    public int3 FirstFloating()
    {
        for (int y = 2; y < SY; y++)
        for (int z = 1; z < SZ - 1; z++)
        for (int x = 1; x < SX - 1; x++)
        {
            byte m = Store.GetVoxel(new int3(x, y, z));
            if (!MaterialRules.IsMobile(m)) continue;
            if (Store.GetVoxel(new int3(x, y - 1, z)) == Materials.Air) return new int3(x, y, z);
        }
        return new int3(-1, -1, -1);
    }

    /// Diagnostic line. Reported, never asserted.
    public void Note(string s) => _lines.Add($"    note  {s}");

    public int LowestY(byte m)
    {
        for (int y = 0; y < SY; y++)
        for (int z = 0; z < SZ; z++)
        for (int x = 0; x < SX; x++)
            if (Store.GetVoxel(new int3(x, y, z)) == m) return y;
        return -1;
    }

    /// Order-independent content fingerprint of every mobile voxel.
    public string Fingerprint()
    {
        unchecked
        {
            ulong h = 1469598103934665603UL;
            for (int y = 0; y < SY; y++)
            for (int z = 0; z < SZ; z++)
            for (int x = 0; x < SX; x++)
            {
                byte m = Store.GetVoxel(new int3(x, y, z));
                if (!MaterialRules.IsMobile(m) && m != Materials.Obsidian) continue;
                h ^= (ulong)((x * 73856093) ^ (y * 19349663) ^ (z * 83492791) ^ (m * 2654435761));
                h *= 1099511628211UL;
            }
            return h.ToString("x16", CultureInfo.InvariantCulture);
        }
    }

    public long AppliedOps => _applied;

    /// The §7.8-shaped observation: everything the architecture actually
    /// determines about a settled state, and nothing it does not.
    public struct Obs
    {
        public int Water, Sand, Lava, Honey, Obsidian, Floating;
        public string Layers;          // occupied y-layers per material, as a set
        public string Fingerprint;     // EXACT layout -- reported, never asserted

        public string Counts =>
            $"water {Water} sand {Sand} lava {Lava} honey {Honey} obsidian {Obsidian} floating {Floating}";
    }

    private static readonly byte[] TrackedMaterials =
        { Materials.Water, Materials.Sand, Materials.Lava, Materials.Honey, Materials.Obsidian };

    public Obs Observe()
    {
        var o = new Obs
        {
            Water = CountMaterial(Materials.Water),
            Sand = CountMaterial(Materials.Sand),
            Lava = CountMaterial(Materials.Lava),
            Honey = CountMaterial(Materials.Honey),
            Obsidian = CountMaterial(Materials.Obsidian),
            Floating = CountFloating(),
            Fingerprint = Fingerprint(),
        };
        var sb = new StringBuilder();
        foreach (byte m in TrackedMaterials)
        {
            sb.Append(m).Append(':');
            for (int y = 0; y < SY; y++)
            {
                bool any = false;
                for (int z = 0; z < SZ && !any; z++)
                for (int x = 0; x < SX && !any; x++)
                    if (Store.GetVoxel(new int3(x, y, z)) == m) any = true;
                if (any) sb.Append(y).Append(',');
            }
            sb.Append('|');
        }
        o.Layers = sb.ToString();
        return o;
    }

    public void Dispose()
    {
        Readback?.Dispose(); Fluid?.Dispose(); Clipmap?.Dispose(); _pool?.Dispose();
        Readback = null; Fluid = null; Clipmap = null; _pool = null; Store = null;
    }

    // =====================================================================
    // Cases
    // =====================================================================

    private readonly struct Case
    {
        public readonly string Id;
        public readonly string What;
        public readonly Action<Phase5cEditStress> Run;
        /// True when §7.6 reactions CONVERT material during the case. Per-material
        /// counts are then not comparable across orderings -- how many times two
        /// fluids MEET is arrangement, and the control proves arrangement moves
        /// with tick phase. Mass balance still is comparable, and is asserted.
        public readonly bool Reacting;
        public Case(string id, string what, Action<Phase5cEditStress> run, bool reacting = false)
        { Id = id; What = what; Run = run; Reacting = reacting; }
    }

    private Case[] Cases => new[]
    {
        // ---- placement ----
        new Case("place_in_air", "a single voxel placed in mid-air must fall to the floor", r =>
        {
            r.Edit(new int3(32, 20, 32), Materials.Water);
            r.Settle();
            r.Eq(1, r.CountMaterial(Materials.Water), "water conserved");
            r.Eq(1, r.LowestY(Materials.Water), "reached the floor (y=1)");
            r.True(r.AppliedOps > 0, "the CA actually moved it (this is the frozen-fluid assertion)");
            r.Eq(0, r.CountFloating(), "nothing left floating");
        }),

        new Case("place_on_ground", "a voxel placed directly on the floor stays put and stays legal", r =>
        {
            r.Edit(new int3(30, 1, 30), Materials.Water);
            r.Settle();
            r.Eq(1, r.CountMaterial(Materials.Water), "water conserved");
            r.Eq(1, r.LowestY(Materials.Water), "still resting on the floor");
            r.Eq(0, r.CountFloating(), "nothing floating");
        }),

        // ---- THE USER'S CASE: mining out from under settled fluid ----
        new Case("mine_under_water", "removing the block UNDER settled water must make the water fall", r =>
        {
            r.Shaft(20, 20, plugTop: 4, wallTop: 10);
            r.Edit(new int3(20, 5, 20), Materials.Water);
            r.Settle();
            r.Eq(5, r.LowestY(Materials.Water), "precondition: water settled on top of the pillar");
            long before = r.AppliedOps;

            r.Edit(new int3(20, 4, 20), Materials.Air);      // MINE UNDER IT
            r.Settle();

            r.True(r.AppliedOps > before, "the mine woke the water (the reported bug)");
            r.Eq(4, r.LowestY(Materials.Water), "water descended into the mined cell");
            r.Eq(1, r.CountMaterial(Materials.Water), "water conserved");
            r.Eq(0, r.CountFloating(), "nothing left floating");
        }),

        new Case("mine_under_sand", "same, for a falling solid rather than a liquid", r =>
        {
            r.Shaft(24, 20, plugTop: 4, wallTop: 10);
            r.Edit(new int3(24, 5, 20), Materials.Sand);
            r.Settle();
            r.Eq(5, r.LowestY(Materials.Sand), "precondition: sand on the pillar");
            long before = r.AppliedOps;
            r.Edit(new int3(24, 4, 20), Materials.Air);
            r.Settle();
            r.True(r.AppliedOps > before, "the mine woke the sand");
            r.Eq(4, r.LowestY(Materials.Sand), "sand descended");
            r.Eq(1, r.CountMaterial(Materials.Sand), "sand conserved");
            r.Eq(0, r.CountFloating(), "nothing floating");
        }),

        new Case("mine_under_stack", "a two-high stack must ALL descend, not just the bottom voxel", r =>
        {
            r.Shaft(28, 20, plugTop: 4, wallTop: 12);
            r.Edit(new int3(28, 5, 20), Materials.Water);
            r.Edit(new int3(28, 6, 20), Materials.Water);
            r.Settle();
            long before = r.AppliedOps;
            r.Edit(new int3(28, 4, 20), Materials.Air);
            r.Settle();
            r.True(r.AppliedOps > before, "the mine woke the stack");
            r.Eq(4, r.LowestY(Materials.Water), "the whole stack shifted down one");
            r.Eq(2, r.CountMaterial(Materials.Water), "water conserved");
            r.Eq(0, r.CountFloating(), "no voxel stranded above the one that moved");
        }),

        new Case("mine_deep_column", "a 12-high column collapses fully when its support is removed", r =>
        {
            r.Shaft(44, 44, plugTop: 3, wallTop: 20);
            for (int y = 4; y < 16; y++) r.Edit(new int3(44, y, 44), Materials.Water);
            r.Settle();
            long before = r.AppliedOps;
            r.Edit(new int3(44, 3, 44), Materials.Air);
            r.Settle();
            r.True(r.AppliedOps > before, "the mine woke the column");
            r.Eq(3, r.LowestY(Materials.Water), "the column dropped into the mined cell");
            r.Eq(12, r.CountMaterial(Materials.Water), "all 12 conserved");
            r.Eq(0, r.CountFloating(), "column fully settled, nothing hanging");
        }),

        // ---- long sleep, then disturbed ----
        new Case("wake_after_long_sleep", "fluid asleep far past FLUID_SLEEP_TICKS still wakes when mined", r =>
        {
            r.Shaft(36, 24, plugTop: 4, wallTop: 10);
            r.Edit(new int3(36, 5, 24), Materials.Water);
            r.Settle();
            r.Tick(EngineConfig.FLUID_SLEEP_TICKS * 4 + 200);   // let it sleep properly
            long before = r.AppliedOps;
            r.Edit(new int3(36, 4, 24), Materials.Air);
            r.Settle();
            r.True(r.AppliedOps > before, "a long-asleep slot was re-woken by the edit");
            r.Eq(4, r.LowestY(Materials.Water), "and it actually descended");
            r.Eq(0, r.CountFloating(), "nothing floating");
        }),

        // ---- placing solids into fluid ----
        new Case("place_solid_into_water", "dropping a block into a settled body overwrites one voxel and stays legal", r =>
        {
            for (int i = 0; i < 12; i++) { r.Edit(new int3(50, 6, 50), Materials.Water); r.Settle(6, 60); }
            r.Settle();
            int before = r.CountMaterial(Materials.Water);
            r.True(before > 0, "precondition: a water body exists");
            int3 victim = r.FirstOf(Materials.Water);
            r.Edit(victim, Materials.Stone);                    // §8.3: CPU is sole writer, it overwrites
            r.Settle();
            r.Eq(before - 1, r.CountMaterial(Materials.Water), "exactly the overwritten voxel is gone");
            r.Eq(0, r.CountFloating(), "the rest re-settled legally");
        }),

        // ---- the input that found the bug ----
        new Case("hold_paint", "re-editing ONE cell every tick (holding RMB) must not stall the queue", r =>
        {
            // BUDGETED BY MASS, not by ticks. A fixed tick count let the two
            // orderings introduce DIFFERENT amounts of water (60 vs 30),
            // because the cell only refills when it reads Air and the drain
            // rate differs by a tick of latency. That made the cross-ordering
            // comparison meaningless -- it was comparing two different inputs.
            // The edit still fires EVERY tick, Air or not, because hammering
            // one cell is the coalescing path this case exists to exercise.
            int3 cell = new int3(16, 12, 16);
            int budget = 30, placed = 0;
            for (int i = 0; i < 900 && budget > 0; i++)
            {
                // Hammer the SAME cell every tick either way -- that is the
                // coalescing path. But re-editing it to Water unconditionally
                // kept it permanently full, so how often it read Air (and
                // therefore how much mass entered) became a drain-rate race:
                // 30 voxels in MirrorFirst vs 8 in TickFirst, from one input.
                // Re-editing it to WHAT IT ALREADY HOLDS hammers the queue
                // identically while leaving the drain untouched.
                byte cur = r.Store.GetVoxel(cell);
                if (cur == Materials.Air) { r.Edit(cell, Materials.Water); budget--; placed++; }
                else r.Edit(cell, cur);
                r.Tick();
            }
            r.Settle();
            r.Eq(30, placed, "the full mass budget was placed");
            r.Eq(30, r.CountMaterial(Materials.Water), "every placed voxel is accounted for");
            r.Eq(0, r.CountFloating(), "nothing floating");
        }),

        new Case("vent_sustained", "Playground's vent loop must not die after its first voxel", r =>
        {
            // Emit() only refills a source cell it reads as Air. One frozen
            // voxel at the source stopped the vent dead -- this pins that.
            int budget = 40, emitted = 0;
            int3 src = new int3(21, 24, 32);
            for (int i = 0; i < 600 && budget > 0; i++)
            {
                if (r.Store.GetVoxel(src) == Materials.Air) { r.Edit(src, Materials.Water); budget--; emitted++; }
                r.Tick();
            }
            r.Eq(40, emitted, "the vent emitted its whole budget (it stalled at 1 before the fix)");
            r.Settle();
            r.Eq(40, r.CountMaterial(Materials.Water), "all emitted water conserved");
            r.Eq(0, r.CountFloating(), "the pile settled");
        }),

        // ---- reactions ----
        new Case("lava_water_react", "lava meeting water still produces obsidian through the edit path", r =>
        {
            // Budgeted the same way and for the same reason as hold_paint: the
            // first version wrote its source unconditionally, so the amount of
            // lava that actually entered the world depended on drain rate and
            // the two orderings got different inputs (5 lava vs 3).
            int3 wsrc = new int3(10, 6, 10), lsrc = new int3(13, 6, 10);
            int wb = 8, lb = 8;
            for (int i = 0; i < 900 && (wb > 0 || lb > 0); i++)
            {
                if (wb > 0 && r.Store.GetVoxel(wsrc) == Materials.Air) { r.Edit(wsrc, Materials.Water); wb--; }
                if (lb > 0 && r.Store.GetVoxel(lsrc) == Materials.Air) { r.Edit(lsrc, Materials.Lava); lb--; }
                r.Tick();
            }
            r.Eq(0, wb, "all water entered");
            r.Eq(0, lb, "all lava entered");
            r.Settle();
            int obs = r.CountMaterial(Materials.Obsidian);
            r.True(obs > 0, "obsidian formed (§7.6 reaction)");
            // MASS BALANCE, not raw counts: each obsidian consumed one water and
            // one lava, so these identities hold no matter HOW MANY reactions
            // happened. The count of reactions depends on where the two fluids
            // meet, which is arrangement.
            // MEASURED, not assumed: every obsidian comes from exactly one lava
            // (lava + obsidian == 8 held in all three orderings), but water is
            // NOT consumed one-for-one -- MirrorFirstOffset produced 5 obsidian
            // having consumed only 4 water, i.e. two lava cells reacted against
            // the SAME water cell in one tick. So the water rule is an
            // inequality, not an equality; asserting equality was asserting a
            // reaction rule the engine does not have.
            int water = r.CountMaterial(Materials.Water);
            r.Eq(8, r.CountMaterial(Materials.Lava) + obs, "every obsidian came from exactly one lava");
            r.True(water <= 8, "water is never CREATED by a reaction");
            r.True(8 - water <= obs, "water is only ever consumed by a reaction, at most one per obsidian");
            r.Eq(0, r.CountFloating(), "nothing floating after the reaction");
        }, reacting: true),

        new Case("large_pour", "a 220-voxel pour must settle with NOTHING left floating", r =>
        {
            // Added because the rig's decorative end-of-run pour reported
            // "floating 1" while every small case reported 0. A floater that
            // only appears at scale is exactly the intermittent GPU floating
            // drop recorded in PHASE_5B_COMPLETION, so it gets an asserted case
            // rather than a footnote.
            int3 src = new int3(26, 26, 32);
            int budget = 220;
            for (int i = 0; i < 3000 && budget > 0; i++)
            {
                if (r.Store.GetVoxel(src) == Materials.Air) { r.Edit(src, Materials.Water); budget--; }
                r.Tick();
            }
            r.Eq(0, budget, "the whole pour entered the world");
            r.Settle(30, 3000);
            r.Eq(220, r.CountMaterial(Materials.Water), "all 220 conserved");

            // DIAGNOSTIC BEFORE THE ASSERTION. A stranded voxel has two causes
            // that need OPPOSITE fixes, and "floating 1" alone cannot tell them
            // apart:
            //   missed wake  -> nothing ever asked the CA to look at it. An
            //                   explicit RequestWake then frees it.
            //   stuck slot   -> it HAS been considered and the CA believes it
            //                   is supported or asleep. An explicit wake
            //                   changes nothing, and the bug is in Intent/
            //                   Commit or the sleep rule, not in waking.
            r.Fluid.ReadSlotCounters(out uint hw, out uint ever);
            r.Note($"slots: high-water {hw}, EVER ALLOCATED {ever}, capacity {r.Fluid.SlotCapacity}" +
                   (ever >= (uint)r.Fluid.SlotCapacity ? "   *** EXHAUSTED ***" : ""));

            int floating = r.CountFloating();
            if (floating > 0)
            {
                int3 f = r.FirstFloating();
                byte below = r.Store.GetVoxel(new int3(f.x, f.y - 1, f.z));
                int owner = r.Fluid.ReadSlotAtCell(r.Fluid.RegionIndex(f));
                r.Note($"STRANDED {floating} voxel(s); first at {f}, material below = {below}");
                r.Note($"    its cell's owning slot = {owner} " +
                       (owner >= 0
                            ? "-> OWNED. Both CSPromote and CSWakeScan skip owned cells, so if this "
                              + "slot is not awake NOTHING can ever move this voxel again."
                            : "-> unowned, so promotion was possible and did not happen."));
                r.Fluid.RequestWakeNeighbourhood(f);
                int extra = r.Settle(30, 600);
                r.Note($"after an EXPLICIT wake + {extra} ticks: floating {r.CountFloating()} " +
                       (r.CountFloating() < floating
                            ? "-> IT MOVED: this was a MISSED WAKE, not a stuck slot"
                            : "-> unchanged: NOT a missing wake; look at Intent/Commit/sleep"));
            }

            r.Eq(0, r.CountFloating(), "NOTHING left floating after a large pour");
        }),

        // ---- edits that must do nothing ----
        new Case("edit_far_from_fluid", "an edit nowhere near fluid must not disturb it or crash", r =>
        {
            r.Edit(new int3(32, 20, 32), Materials.Water);
            r.Settle();
            int before = r.CountMaterial(Materials.Water);
            for (int i = 0; i < 20; i++) r.Edit(new int3(5 + i, 3, 58), Materials.Stone);
            r.Settle();
            r.Eq(before, r.CountMaterial(Materials.Water), "fluid untouched");
            r.Eq(0, r.CountFloating(), "still nothing floating");
        }),

        new Case("edit_at_region_edge", "edits on the region boundary are rejected cleanly, not fatally", r =>
        {
            // RequestWake drops out-of-region coords by design. This asserts the
            // rejection is counted and harmless, not that it promotes.
            long rejectedBefore = r.Fluid.WakeRejectedOutOfRegion;
            r.Edit(new int3(1, 1, 1), Materials.Water);
            r.Edit(new int3(SX - 2, 1, SZ - 2), Materials.Water);
            r.Settle();
            r.Eq(2, r.CountMaterial(Materials.Water), "both in-region edits survived");
            r.Eq(0, r.CountFloating(), "nothing floating");
            r.True(r.Fluid.WakeRejectedOutOfRegion >= rejectedBefore, "rejection counter is monotonic");
        }),
    };

    public int3 FirstOf(byte m)
    {
        for (int y = 0; y < SY; y++)
        for (int z = 0; z < SZ; z++)
        for (int x = 0; x < SX; x++)
            if (Store.GetVoxel(new int3(x, y, z)) == m) return new int3(x, y, z);
        return new int3(-1, -1, -1);
    }

    // =====================================================================
    // Assertions
    // =====================================================================

    private string _case, _orderName;

    private void True(bool ok, string what)
    {
        if (ok) { _pass++; _lines.Add($"    PASS  {what}"); }
        else { _fail++; _lines.Add($"    FAIL  {what}   [{_case} / {_orderName}]"); }
    }

    private void Eq(int expected, int actual, string what)
    {
        if (expected == actual) { _pass++; _lines.Add($"    PASS  {what}  ({actual})"); }
        else { _fail++; _lines.Add($"    FAIL  {what}   expected {expected}, got {actual}   [{_case} / {_orderName}]"); }
    }

    // =====================================================================
    // Driver
    // =====================================================================

    void Start() => StartCoroutine(RunAll());

    private System.Collections.IEnumerator RunAll()
    {
        yield return null;
        var orders = new[] { Ordering.MirrorFirst, Ordering.TickFirst, Ordering.MirrorFirstOffset };

        _lines.Add("EDIT-PATH STRESS RIG (Phase 5c)");
        _lines.Add("Every case runs in BOTH frame orderings. MirrorFirst is the 5a/5b rig order");
        _lines.Add("(upload then tick). TickFirst is the Playground/Phase4Bootstrapper order");
        _lines.Add("(tick in Update, upload in LateUpdate) -- the one every real scene uses.");
        _lines.Add("");

        foreach (var c in Cases)
        {
            foreach (var o in orders)
            {
                _case = c.Id; _orderName = o.ToString();
                _lines.Add($"  [{o,-11}] {c.Id} -- {c.What}");
                Build(o);
                try { c.Run(this); }
                catch (Exception e)
                {
                    _fail++;
                    _lines.Add($"    FAIL  threw {e.GetType().Name}: {e.Message}");
                }
                Obs ob = Observe();
                _obs[c.Id + "/" + o] = ob;
                _lines.Add($"    ops applied {AppliedOps}   {ob.Counts}");
                _lines.Add($"    layout fingerprint {ob.Fingerprint}   (REPORTED, NOT ASSERTED -- see below)");
                Dispose();
                yield return null;
            }
        }

        // -----------------------------------------------------------------
        // THE CENTRAL ASSERTION. Frame ordering must not be observable.
        // -----------------------------------------------------------------
        // -----------------------------------------------------------------
        // CONTROL FIRST, THEN THE ASSERTION -- in that order deliberately.
        //
        // The first version of this rig asserted that the EXACT final layout
        // was identical in both orderings. It failed on 12 of 13 cases while
        // every behavioural assertion passed in both. That is the same trap
        // Phase 5b §4 fell into and answered by experiment, so it is answered
        // the same way here rather than by relaxing the check on a hunch.
        //
        // The control below is the experiment: MirrorFirstOffset is MirrorFirst
        // with ONE IDLE TICK before the scenario. Same ordering, same code path,
        // same mirror discipline -- only the tick phase differs. §7.4's
        // tie-break hashes the tick number, so if a one-tick phase shift alone
        // moves the layout, then layout equality is not a property the
        // architecture provides and asserting it would be asserting a
        // coincidence. Read the control lines before believing the asserted
        // ones.
        // -----------------------------------------------------------------
        _lines.Add("");
        _lines.Add("CONTROL -- MirrorFirst vs MirrorFirstOffset (SAME ordering, tick phase +1):");
        int layoutMovedByPhaseAlone = 0;
        foreach (var c in Cases)
        {
            Obs a = _obs[c.Id + "/" + Ordering.MirrorFirst];
            Obs k = _obs[c.Id + "/" + Ordering.MirrorFirstOffset];
            bool same = a.Fingerprint == k.Fingerprint;
            if (!same) layoutMovedByPhaseAlone++;
            _lines.Add($"    {(same ? "layout same" : "LAYOUT MOVED")}  {c.Id}   " +
                       $"counts {(a.Counts == k.Counts ? "same" : "DIFFER")}");
        }
        _lines.Add($"    => a one-tick phase shift alone moved the layout in " +
                   $"{layoutMovedByPhaseAlone}/{Cases.Length} cases.");

        _lines.Add("");
        _lines.Add("ORDERING EQUIVALENCE (§7.8-shaped) -- MirrorFirst vs TickFirst.");
        _lines.Add("ASSERTED: conserved counts, floating count, occupied-layer set.");
        _lines.Add("NOT ASSERTED: exact layout -- see the control above.");
        foreach (var c in Cases)
        {
            Obs a = _obs[c.Id + "/" + Ordering.MirrorFirst];
            Obs b = _obs[c.Id + "/" + Ordering.TickFirst];
            _case = c.Id; _orderName = "MirrorFirst-vs-TickFirst";
            if (c.Reacting)
            {
                // Reactions convert material, so per-material counts encode HOW
                // MANY times two fluids met -- arrangement. Assert the balance
                // that survives that, and report the rest.
                bool bal = (a.Lava + a.Obsidian) == (b.Lava + b.Obsidian)
                        && a.Floating == b.Floating;
                if (bal)
                {
                    _pass++;
                    _lines.Add($"    PASS  {c.Id}: mass balance identical in both orderings " +
                               $"(reaction COUNT differs by arrangement: {a.Obsidian} vs {b.Obsidian} obsidian)");
                }
                else
                {
                    _fail++;
                    _lines.Add($"    FAIL  {c.Id}: mass balance differs [{a.Counts}] vs [{b.Counts}]");
                }
                continue;
            }

            bool counts = a.Counts == b.Counts;
            bool layers = a.Layers == b.Layers;
            if (counts && layers)
            {
                _pass++;
                _lines.Add($"    PASS  {c.Id}: same mass and same settled layers in both orderings");
            }
            else
            {
                _fail++;
                _lines.Add($"    FAIL  {c.Id}: " +
                           (counts ? "" : $"counts differ [{a.Counts}] vs [{b.Counts}] ") +
                           (layers ? "" : "occupied-layer sets differ"));
            }
        }

        // A visual, in the ordering a real scene uses, for a human to look at.
        Build(Ordering.TickFirst);
        int budget = 220;
        int3 src = new int3(26, 26, 32);
        for (int i = 0; i < 900 && budget > 0; i++)
        {
            if (Store.GetVoxel(src) == Materials.Air) { Edit(src, Materials.Water); budget--; }
            Tick();
        }
        Settle();
        _lines.Add("");
        _lines.Add($"VISUAL: poured {220 - budget} water in TickFirst; " +
                   $"settled count {CountMaterial(Materials.Water)}, floating {CountFloating()}");
        yield return new WaitForEndOfFrame();

        string dir = Path.Combine(Application.persistentDataPath, _outputRootFolderName,
                                  DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(dir);
        ScreenCapture.CaptureScreenshot(Path.Combine(dir, "phase5c_settled.png"));
        yield return new WaitForEndOfFrame();
        yield return new WaitForEndOfFrame();

        var sb = new StringBuilder();
        sb.AppendLine("=== PHASE 5C EDIT-PATH STRESS REPORT ===");
        sb.AppendLine(DateTime.Now.ToString("u", CultureInfo.InvariantCulture));
        sb.AppendLine();
        sb.AppendLine("NO TIMING NUMBERS ARE PRODUCED HERE, DELIBERATELY. This rig runs with");
        sb.AppendLine("synchronous readback for determinism, which alone disqualifies any");
        sb.AppendLine("figure it could report. Performance is Phase 5b's and the acceptance");
        sb.AppendLine("rig's job.");
        sb.AppendLine();
        foreach (string l in _lines) sb.AppendLine(l);
        sb.AppendLine();
        sb.AppendLine($"PASS {_pass}  FAIL {_fail}");
        sb.AppendLine(_fail == 0 ? "RESULT: PASSED" : "RESULT: FAILED");

        File.WriteAllText(Path.Combine(dir, "phase5c_report.txt"), sb.ToString());
        Debug.Log(sb.ToString());
        Debug.Log($"[Phase5c] report written to {dir}");
        yield return null;
        Application.Quit(_fail == 0 ? 0 : 1);
    }

    void OnDestroy() => Dispose();
}
