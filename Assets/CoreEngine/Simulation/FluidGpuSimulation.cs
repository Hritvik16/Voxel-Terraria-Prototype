// ==========================================
// Assets/CoreEngine/Simulation/FluidGpuSimulation.cs
//
// Phase 5b: buffer ownership and per-tick dispatch for FluidCA.compute.
//
// §13 lists two files for this phase (FluidCA.compute, FluidOpListReadback.cs);
// this is the seam between them. It owns the GPU-resident fluid state (§3.5's
// slot pool, the claim plane, the wake list, the op-list) and issues the
// Clear -> Promote -> React -> Intent+Claim -> Commit -> Sweep sequence.
// FluidOpListReadback owns the GPU->CPU half.
//
// -------------------------------------------------------------------------
// WHAT THIS CLASS IS NOT ALLOWED TO DO
// -------------------------------------------------------------------------
// It never writes terrain, on either side. The GPU decides where fluid moves;
// the decision reaches terrain only as an op-list the CPU applies through
// ChunkStore.SetVoxel (§7.2, §0.1 invariant 1). If you find yourself wanting to
// SetData terrain from here, the design has been misread.
//
// -------------------------------------------------------------------------
// PROMOTION IS CPU-DRIVEN, MOTION IS GPU-DRIVEN
// -------------------------------------------------------------------------
// §7.6: "the edit path scans the edit's neighbourhood for fluid/falling
// materials and wakes GPU slots -- this wake signal is a small CPU->GPU upload
// (§3.9), not a readback, so it is immediate." Wake requests are therefore
// uploaded, never read back. The only GPU->CPU traffic is the bounded op-list
// and the bounded descending-move wake list, both of which are appends sized to
// CHANGED cells rather than to active-slot count (§13's named failure signature
// if that is ever untrue -- FluidOpListReadback instruments it).

using System;
using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using VoxelEngine.Mirror;

namespace VoxelEngine.Simulation
{
    /// A.5 FluidSlot, CPU mirror of the HLSL struct in FluidCA.compute.
    /// MUST stay byte-identical to that struct. 24 bytes.
    [StructLayout(LayoutKind.Sequential)]
    public struct FluidSlotGpu
    {
        public int brickDataIndex;
        public uint localVoxelOffset;
        public uint materialID;
        public uint sleepCounter;
        public uint viscosityPhase;
        public uint stateFlags;
        public const int SizeBytes = 24;
    }

    /// A.9 FluidWriteOp. MUST stay byte-identical to the HLSL struct. 16 bytes.
    /// A.9 FluidWriteOp, extended to a MOVE op. MUST stay byte-identical to the
    /// HLSL struct in FluidCA.compute. 32 bytes. See that file for why a move is
    /// one record naming both cells rather than two single-cell writes.
    [StructLayout(LayoutKind.Sequential)]
    public struct FluidWriteOp
    {
        public int dx, dy, dz;
        public uint material;      // [7:0] new, [8] wake, [23:16] expected at dst
        public int sx, sy, sz;
        public uint flags;         // [0] HAS_SRC

        public const int SizeBytes = 32;
        public const uint HAS_SRC = 1u << 0;
        public const uint WAKE = 1u << 8;

        public int3 Dst => new int3(dx, dy, dz);
        public int3 Src => new int3(sx, sy, sz);
        public byte NewMaterial => (byte)(material & 0xFFu);
        public byte ExpectedAtDst => (byte)((material >> 16) & 0xFFu);
        public bool HasSrc => (flags & HAS_SRC) != 0u;
        public bool Wake => (material & WAKE) != 0u;
    }

    public sealed class FluidGpuSimulation : IDisposable
    {
        // Kernel indices, resolved once in the constructor.
        private readonly int _kClear, _kPromote, _kReact, _kIntent, _kCommit, _kSweep, _kFinalize, _kWakeScan, _kRecycle;
        private readonly ComputeShader _cs;

        private readonly int3 _regionDims;
        private readonly int _regionCellCount;
        private readonly int _shiftX, _shiftY;
        private readonly int _slotCapacity;

        private GraphicsBuffer _slots;
        private GraphicsBuffer _claim;
        private GraphicsBuffer _slotAt;
        private GraphicsBuffer _reacted;
        private GraphicsBuffer _wakeMark;
        private GraphicsBuffer _counters;
        /// THE OP-LIST AND WAKE-OUT ARE RING-BUFFERED, AND THEY HAVE TO BE.
        /// They are written by the GPU every tick and read back ASYNCHRONOUSLY
        /// over the following 1-3 frames (§7.2). A single buffer means each
        /// tick's SetCounterValue + appends overwrite data a pending readback is
        /// still reading -- which showed up as intermittent hasError on the
        /// count readback with no logged reason, and as ops going missing.
        /// RING must exceed FluidOpListReadback.MaxFramesInFlight so a slot is
        /// never rewritten while its own readback is outstanding.
        public const int RING = 4;
        /// Element 0 is a HEADER (.voxel.x = op count); elements 1.. are the ops.
        /// One buffer, one readback request per frame -- see the note on
        /// OpsBuffer in FluidCA.compute for why the shape changed.
        private readonly GraphicsBuffer[] _ops = new GraphicsBuffer[RING];
        private readonly GraphicsBuffer[] _opCounters = new GraphicsBuffer[RING];
        private readonly uint[] _opCountersZero = new uint[4];
        private int _ring = -1;
        private GraphicsBuffer _wakeRequests;
        private GraphicsBuffer _debugCounters;
        private readonly uint[] _debugZero = new uint[DebugSlots];
        private readonly uint[] _debugScratch = new uint[DebugSlots];

        /// §10.4's per-subsystem diagnostic dump. See FluidCA.compute for the
        /// slot meanings; they are duplicated in DebugCounterNames so a dump is
        /// readable without opening the shader.
        public const int DebugSlots = 18;
        public static readonly string[] DebugCounterNames =
        {
            "promote.seen", "promote.ALLOCATED", "promote.rej_owned", "promote.rej_notmobile",
            "intent.awake", "intent.CLAIMS", "commit.claims_seen", "commit.APPLIED",
            "intent.orphan_freed", "intent.no_destination", "commit.rej_home", "commit.rej_dst_not_air",
            "PROBE.promote_ran(ABCD)", "PROBE.saw_wakecount", "PROBE.saw_regioncells", "PROBE.saw_tick",
            "promote.rej_radius", "PROBE.saw_material_at_wake0",
        };
        public GraphicsBuffer DebugCountersBuffer => _debugCounters;

        // ---------------------------------------------------------------
        // GPU CAPTURE LABELLING
        // ---------------------------------------------------------------
        // The CA used to call ComputeShader.Dispatch directly. That works, but
        // an immediate dispatch cannot carry a Metal debug group, so a captured
        // trace showed eight anonymous compute encoders per tick with no way to
        // tell CSIntent from CSCommit -- which is exactly the per-kernel
        // attribution OPTIMIZATION_CANDIDATES.md says is missing and needed to
        // rank #1 against #3.
        //
        // Recording into a CommandBuffer lets BeginSample/EndSample wrap each
        // dispatch, and Unity emits those as Metal debug groups, so Instruments
        // shows named encoders. ONE command buffer per tick, executed once, in
        // the same place the immediate dispatches were issued -- so submission
        // order relative to the readback request is unchanged.
        //
        // Parameter binding is deliberately left on the ComputeShader object
        // (BindGlobals, immediate). ComputeShader parameter state is applied at
        // dispatch time, and nothing mutates it between recording and the
        // Execute at the end of Tick.
        private CommandBuffer _cb;

        /// CAPTURE ONLY. Off in every shipping path, and the Phase 5c rig runs
        /// with it off.
        ///
        /// Metal merges consecutive compute dispatches into ONE compute encoder,
        /// and Instruments names that encoder after the FIRST debug group inside
        /// it. Measured: with all eight dispatches in one command buffer, a
        /// capture showed 710 encoder intervals all labelled
        /// "VE.FluidCA.CSClear" and none for CSPromote/CSReact/CSIntent/
        /// CSCommit/CSSweep -- one encoder covering the whole tick, named after
        /// the first sample in it.
        ///
        /// Setting this executes a command buffer PER DISPATCH, which forces an
        /// encoder boundary between kernels so each is attributed separately.
        ///
        /// IT PERTURBS WHAT IT MEASURES: eight command-buffer submissions per
        /// tick instead of one adds real per-submission cost. Use the per-kernel
        /// numbers as a RATIO between kernels, never as an absolute budget.
        public static bool SplitDispatchEncodersForCapture;

        /// Kernel labels. MUST match the names parsed by tools/parse-gpu-trace.py.
        private const string LblClear    = "VE.FluidCA.CSClear";
        private const string LblPromote  = "VE.FluidCA.CSPromote";
        private const string LblReact    = "VE.FluidCA.CSReact";
        private const string LblIntent   = "VE.FluidCA.CSIntent";
        private const string LblCommit   = "VE.FluidCA.CSCommit";
        private const string LblWakeScan = "VE.FluidCA.CSWakeScan";
        private const string LblSweep    = "VE.FluidCA.CSSweep";
        private const string LblRecycle  = "VE.FluidCA.CSRecycle";
        private const string LblFinalize = "VE.FluidCA.CSFinalize";

        private readonly uint[] _counterScratch = new uint[4];

        /// §10.4 diagnostic. BLOCKING read -- diagnostics and rigs only, never a
        /// timing path.
        ///   highWater     = Counters[0], the highest slot index ever bound, which
        ///                   is how far CSIntent/CSSweep scan.
        ///   everAllocated = Counters[1], the AllocSlot bump counter: the number
        ///                   of slots EVER handed out. It never decreases, because
        ///                   AllocSlot is a bump allocator with no free list, so
        ///                   comparing it against SlotCapacity is how you tell
        ///                   whether promotion has run out of indices.
        /// §10.4 diagnostic. BLOCKING, whole-buffer read -- rigs only.
        /// Returns the slot index owning this region cell, or -1 (NONE).
        ///
        /// This distinguishes the two causes of a permanently stranded voxel,
        /// which need opposite fixes: a cell with NO owner was never promoted
        /// (a wake problem), while a cell WITH an owner cannot be promoted at
        /// all -- both CSPromote and CSWakeScan return early on an owned cell --
        /// so if its slot is not awake, nothing can ever move it again.
        public int ReadSlotAtCell(int regionCell)
        {
            if (regionCell < 0 || regionCell >= _regionCellCount) return -2;
            var all = new int[_regionCellCount];
            _slotAt.GetData(all);
            return all[regionCell];
        }

        public void ReadSlotCounters(out uint highWater, out uint everAllocated)
        {
            _counters.GetData(_counterScratch);
            highWater = _counterScratch[0];
            everAllocated = _counterScratch[1];
        }

        /// Slot indices currently sitting in A.5's free list, waiting to be
        /// reused. With recycling working, a region that has settled should show
        /// this rising back toward highWater -- that is what "sleep returns
        /// capacity" looks like from the CPU. BLOCKING; rigs only.
        public uint ReadFreeSlotCount()
        {
            _counters.GetData(_counterScratch);
            return _counterScratch[2];
        }

        /// A.5's free list, GPU side. Holds slot indices returned by CSRecycle.
        private GraphicsBuffer _freeList;

        /// The sparse active set, or null for the original dense region.
        public FluidTileMap Tiles { get; }

        private GraphicsBuffer _tileDirectory, _tileCoords, _activeTileSlots, _dummyTileBuf;
        private int[] _dirScratch, _coordScratch, _activeScratch;
        private int _tileShift, _tileCellShift, _ringShiftY, _ringShiftZ;
        private int _cellCount;
        private int _activeTileCount;

        /// Cells actually allocated -- the pool under tiling, the region when
        /// dense. Multiply by 16 B (four 4-byte per-cell buffers) for the
        /// footprint this design exists to bound.
        public int CellCount => _cellCount;

        /// GPU bytes for the per-cell buffers plus, under tiling, the directory.
        /// MEASURED FROM THE ACTUAL ALLOCATION, not computed from a formula --
        /// the acceptance bar asks for an allocation number, not arithmetic.
        public long CellBufferBytes
        {
            get
            {
                long b = (long)_cellCount * 16;
                if (Tiles != null)
                    b += (long)Tiles.RingDimsTiles.x * Tiles.RingDimsTiles.y
                       * Tiles.RingDimsTiles.z * 4;
                return b;
            }
        }

        /// Every GPU byte this simulation has actually allocated, summed from
        /// the buffer objects themselves (count x stride) rather than recomputed
        /// from the configuration.
        ///
        /// THE ACCEPTANCE BAR ASKS FOR AN ALLOCATION NUMBER, NOT ARITHMETIC.
        /// A formula can be right about a design and wrong about the code; this
        /// enumerates what was really created.
        public long GpuAllocatedBytes()
        {
            long total = 0;
            void Add(GraphicsBuffer b) { if (b != null) total += (long)b.count * b.stride; }

            Add(_slots); Add(_freeList); Add(_claim); Add(_slotAt); Add(_reacted);
            Add(_wakeMark); Add(_counters); Add(_wakeRequests); Add(_debugCounters);
            Add(_materialFlags); Add(_materialTick); Add(_reactSelf); Add(_reactOther);
            Add(_tileDirectory); Add(_tileCoords); Add(_activeTileSlots); Add(_dummyTileBuf);
            for (int i = 0; i < RING; i++) { Add(_ops[i]); Add(_opCounters[i]); }
            return total;
        }

        /// Just the per-cell buffers plus the tile directory -- the part that
        /// scaled with the radius before tiling and must not now.
        public long GpuActiveSetBytes()
        {
            long total = 0;
            void Add(GraphicsBuffer b) { if (b != null) total += (long)b.count * b.stride; }
            Add(_claim); Add(_slotAt); Add(_reacted); Add(_wakeMark);
            Add(_tileDirectory); Add(_tileCoords); Add(_activeTileSlots);
            return total;
        }

        public int ActiveTileCount => _activeTileCount;

        private GraphicsBuffer _materialFlags;
        private GraphicsBuffer _materialTick;
        private GraphicsBuffer _reactSelf;
        private GraphicsBuffer _reactOther;

        private readonly int[] _wakeScratch;
        private int3[] _wakeVoxelScratch;
        private int _wakeCount;

        /// Requests wait HERE until the GPU mirror can see the edit that caused
        /// them. See FluidWakeQueue for the frozen-fluid bug this fixes.
        private readonly FluidWakeQueue _wakePending;
        private readonly Func<int3, long, bool> _mirrorReady;  // cached: called per pending request per tick
        private TerrainClipmap _mirrorForReadyTest;

        // CPU-side half of the §10.4 dump. promote.seen==0 on the GPU has two
        // completely different causes -- the CPU never queued anything, or it
        // queued and the dispatch/upload lost it -- and they are indistinguishable
        // from GPU counters alone.
        public int PendingWakeRequests => _wakeCount;
        public int LastDispatchedWakeCount { get; private set; }
        /// Cumulative, because the per-tick value is 0 on every tick after the
        /// first and a late sample therefore proves nothing about the first.
        public long WakeDispatchedTotal { get; private set; }
        public long PromoteDispatchesTotal { get; private set; }
        public long WakeRequestsQueuedTotal { get; private set; }
        public long WakeRejectedOutOfRegion { get; private set; }
        public long WakeRejectedFull { get; private set; }
        /// Requests queued but not yet dispatched because the mirror is still
        /// stale for their chunk. Steady non-zero here means uploads are behind.
        public int DeferredWakeRequests => _wakePending.PendingCount;
        public long WakeCoalescedTotal => _wakePending.CoalescedTotal;
        /// Released without the mirror ever going clean (§FluidWakeQueue.Collect).
        /// Should be 0 in a healthy run.
        public long WakeReleasedStaleTotal => _wakePending.ReleasedStaleTotal;

        public int3 RegionOriginVoxels { get; set; }
        public int3 PlayerVoxel { get; set; }
        public int ActiveRadiusVoxels { get; set; } = EngineConfig.FLUID_ACTIVE_RADIUS_VOXELS;

        /// §7.4's DEMOTE radius. Larger than the wake radius; the gap is the
        /// hysteresis band. Defaults to the ratio in FluidActiveRegion.
        public int SleepRadiusVoxels { get; set; } =
            FluidActiveRegion.SleepRadiusFor(EngineConfig.FLUID_ACTIVE_RADIUS_VOXELS);

        /// Moves §7.4's active centre toward the player, subject to the
        /// re-centre threshold. Returns true if the centre actually moved.
        ///
        /// CALL THIS EVERY FRAME. Nothing else updates PlayerVoxel -- before
        /// this existed it was set once at construction and never again, so
        /// §7.4's radius (which the shader has always tested) was anchored to
        /// wherever the region was created.
        public bool UpdatePlayerPosition(int3 playerVoxel)
        {
            if (!FluidActiveRegion.ShouldRecentre(PlayerVoxel, playerVoxel, RecentreThresholdVoxels))
                return false;
            PlayerVoxel = playerVoxel;
            RecentresTotal++;
            _recentreSeedTicks = RecentreSeedTicks;   // §7.4's wake-on-approach
            return true;
        }

        /// How many ticks CSWakeScan is allowed its radius-driven seed after the
        /// active centre moves. See the long note in CSWakeScan: the seed is what
        /// gives §7.4's force-demote an inverse, without which a region that goes
        /// to sleep mid-flow can never restart itself.
        ///
        /// ENGINEERING DEFAULT, NOT MEASURED. One tick is enough in principle --
        /// the seeded cells move, set wake marks, and ordinary propagation takes
        /// over. It is larger than one so that a tick where §7.7's pool guard
        /// refuses the allocation is retried rather than lost, which would strand
        /// exactly the cells the seed exists to rescue.
        public int RecentreSeedTicks { get; set; } = 4;

        /// Counts down to zero; non-zero uploads _RecentreSeed. Ticks, not
        /// frames -- it must track dispatches, and a frame that skips the tick
        /// must not burn the budget.
        private int _recentreSeedTicks;

        /// Diagnostic: how many ticks have actually run with the seed enabled.
        /// A rig asserting the seed did something needs to know it was ON.
        public long RecentreSeedTicksRun { get; private set; }

        /// How far the player must move before the centre follows. See
        /// FluidActiveRegion for why this is a separate mechanism from
        /// hysteresis rather than a duplicate of it.
        public int RecentreThresholdVoxels { get; set; } =
            FluidActiveRegion.DefaultRecentreThresholdVoxels;

        public long RecentresTotal { get; private set; }
        public int TickCount { get; private set; }

        public int3 RegionDims => _regionDims;
        public int RegionCellCount => _regionCellCount;
        public int SlotCapacity => _slotCapacity;
        public int MaxOpsPerFrame { get; }

        /// The ring slot the most recent Tick wrote. IssueReadback must read
        /// THIS slot, not a fixed one.
        public int CurrentRing => _ring;
        /// THE only buffer the CPU reads back.
        public GraphicsBuffer OpsBuffer => _ops[_ring < 0 ? 0 : _ring];

        /// <param name="regionDims">Active-region size in voxels. EVERY component
        /// must be a power of two: FluidCA.compute addresses the region with
        /// shifts and masks (§0.1 invariant 2), and a non-power-of-two does not
        /// fail loudly, it aliases silently -- the §6.2 phantom-terrain bug
        /// class. Asserted, not trusted.</param>
        public FluidGpuSimulation(ComputeShader fluidCA, int3 regionDims,
                                  int slotCapacity, int maxOpsPerFrame)
            : this(fluidCA, regionDims, slotCapacity, maxOpsPerFrame, null) { }

        /// §7.2 SPARSE MODE. Passing a FluidTileMap replaces the dense per-cell
        /// buffers with a hard-capped pool of fixed-size tiles, so the footprint
        /// stops scaling with the region (and therefore with §7.4's radius) and
        /// starts scaling with how much fluid there actually is.
        ///
        /// `tiles == null` keeps the ORIGINAL dense path byte-for-byte, which is
        /// what lets every Phase 5a-5d proof keep exercising the code it was
        /// written against while the new path is proven beside it.
        /// <param name="slotCeilingOverride">
        /// A MEASUREMENT SEAM, NOT A CONFIGURATION. 0 (the default) clamps to
        /// §0.2's EngineConfig.MAX_ACTIVE_FLUID, which is what every shipped
        /// caller gets. A positive value clamps to THAT instead, so a rig can
        /// ask "what would a higher ceiling cost?" without a rebuild between
        /// the two numbers being compared -- and comparing two builds is how
        /// you get compiler variance mixed into a GPU measurement.
        ///
        /// WHY THIS QUESTION IS WORTH A SEAM: CSReact, CSIntent, CSSweep and
        /// CSRecycle all dispatch _slotCapacity threads EVERY TICK, live fluid
        /// or not. So the ceiling is not free headroom the way a tile pool's
        /// spare capacity nearly is -- see Tick(). That is a prediction, and
        /// the seam exists to test it rather than argue it.
        /// </param>
        public FluidGpuSimulation(ComputeShader fluidCA, int3 regionDims,
                                  int slotCapacity, int maxOpsPerFrame,
                                  FluidTileMap tiles, int slotCeilingOverride = 0)
        {
            _cs = fluidCA != null ? fluidCA
                : throw new ArgumentNullException(nameof(fluidCA), "FluidCA.compute not assigned");

            RequirePow2(regionDims.x, "regionDims.x");
            RequirePow2(regionDims.y, "regionDims.y");
            RequirePow2(regionDims.z, "regionDims.z");

            Tiles = tiles;
            _regionDims = regionDims;
            _regionCellCount = regionDims.x * regionDims.y * regionDims.z;
            _shiftX = Log2(regionDims.x);
            _shiftY = Log2(regionDims.y);
            int ceiling = slotCeilingOverride > 0 ? slotCeilingOverride : EngineConfig.MAX_ACTIVE_FLUID;
            _slotCapacity = Math.Min(slotCapacity, ceiling);
            MaxOpsPerFrame = maxOpsPerFrame;

            _kClear   = _cs.FindKernel("CSClear");
            _kPromote = _cs.FindKernel("CSPromote");
            _kReact   = _cs.FindKernel("CSReact");
            _kIntent  = _cs.FindKernel("CSIntent");
            _kCommit  = _cs.FindKernel("CSCommit");
            _kSweep   = _cs.FindKernel("CSSweep");
            _kFinalize = _cs.FindKernel("CSFinalize");
            _kWakeScan = _cs.FindKernel("CSWakeScan");
            _kRecycle = _cs.FindKernel("CSRecycle");

            _slots   = New(GraphicsBuffer.Target.Structured, _slotCapacity, FluidSlotGpu.SizeBytes);
            _freeList = New(GraphicsBuffer.Target.Structured, _slotCapacity, 4);
            // THE NUMBER THE WHOLE DESIGN IS ABOUT. Dense: one cell per voxel
            // of the region, so the radius can never exceed what fits. Tiled:
            // one cell per voxel of the POOL, which is capped and independent
            // of the radius entirely.
            _cellCount = tiles != null ? tiles.TileCapacity * tiles.TileCells : _regionCellCount;

            _claim   = New(GraphicsBuffer.Target.Structured, _cellCount, 4);
            _slotAt  = New(GraphicsBuffer.Target.Structured, _cellCount, 4);
            _reacted = New(GraphicsBuffer.Target.Structured, _cellCount, 4);
            _wakeMark = New(GraphicsBuffer.Target.Structured, _cellCount, 4);

            if (tiles != null)
            {
                int ringCount = tiles.RingDimsTiles.x * tiles.RingDimsTiles.y * tiles.RingDimsTiles.z;
                _tileDirectory = New(GraphicsBuffer.Target.Structured, ringCount, 4);
                _tileCoords = New(GraphicsBuffer.Target.Structured, tiles.TileCapacity * 4, 4);
                _activeTileSlots = New(GraphicsBuffer.Target.Structured, tiles.TileCapacity, 4);
                _dirScratch = new int[ringCount];
                _coordScratch = new int[tiles.TileCapacity * 4];
                _activeScratch = new int[tiles.TileCapacity];
                _tileShift = Log2(tiles.TileEdge);
                _tileCellShift = _tileShift * 3;
                _ringShiftY = Log2(tiles.RingDimsTiles.x);
                _ringShiftZ = _ringShiftY + Log2(tiles.RingDimsTiles.y);
            }
            _counters = New(GraphicsBuffer.Target.Structured, 4, 4);
            _dummyTileBuf = New(GraphicsBuffer.Target.Structured, 1, 4);
            for (int i = 0; i < RING; i++)
            {
                _ops[i] = New(GraphicsBuffer.Target.Structured, maxOpsPerFrame + 1, FluidWriteOp.SizeBytes);
                _opCounters[i] = New(GraphicsBuffer.Target.Structured, 4, 4);
            }
            _wakeRequests = New(GraphicsBuffer.Target.Structured, maxOpsPerFrame, 4);
            _cb = new CommandBuffer { name = "VE.FluidCA" };
            _wakeScratch = new int[maxOpsPerFrame];
            _wakeVoxelScratch = new int3[maxOpsPerFrame];
            // MaxWakeDeferTicks: how long a request may wait for its chunk to
            // upload before being released anyway. 120 ticks is far longer than
            // any observed upload lag (a single edited chunk normally clears on
            // the very next LateUpdate) while still bounding the queue.
            _wakePending = new FluidWakeQueue(maxOpsPerFrame, MaxWakeDeferTicks);
            _mirrorReady = IsMirrorReadyForCell;
            _debugCounters = New(GraphicsBuffer.Target.Structured, DebugSlots, 4);

            // §3.7's lesson, applied here too: a fresh GraphicsBuffer contains
            // whatever was in GPU memory. SlotAt in particular MUST start at
            // NONE or CSPromote will believe every cell already owns a slot and
            // silently promote nothing.
            // OVER THE WHOLE ALLOCATION, not the region. Under tiling the
            // buffers are TileCapacity * TileCells, and initialising only the
            // first _regionCellCount entries would leave the rest as whatever
            // was in GPU memory -- with SlotAt in particular that means
            // CSPromote believes every cell already owns a slot and silently
            // promotes nothing, which is exactly the §3.7 lesson this block
            // already existed to apply.
            var noneFill = new int[_cellCount];
            for (int i = 0; i < _cellCount; i++) noneFill[i] = -1;
            _slotAt.SetData(noneFill);
            _claim.SetData(noneFill);
            _reacted.SetData(new int[_cellCount]);
            _wakeMark.SetData(new int[_cellCount]);
            _counters.SetData(new uint[4]);
            _slots.SetData(new FluidSlotGpu[_slotCapacity]);
            // Not strictly required (Counters[2] starts at 0, so nothing is read),
            // but a deterministic buffer beats one holding whatever was in GPU memory.
            _freeList.SetData(new uint[_slotCapacity]);

            UploadMaterialRegistry();
        }

        private static GraphicsBuffer New(GraphicsBuffer.Target target, int count, int stride) =>
            new GraphicsBuffer(target, Math.Max(1, count), stride);

        /// §3.9's CPU->GPU registry upload, restricted to what the CA reads:
        /// A.7 flags, §7.4 tick intervals, and §7.6's reaction pairs flattened to
        /// a 256x256 lookup so the shader needs no loop over a pair table.
        private void UploadMaterialRegistry()
        {
            var flags = new uint[256];
            var interval = new uint[256];
            var reactSelf = new uint[256 * 256];
            var reactOther = new uint[256 * 256];
            for (int i = 0; i < reactSelf.Length; i++) reactSelf[i] = 0xFFFFFFFFu;

            for (int m = 0; m < 256; m++)
            {
                flags[m] = MaterialRules.Flags((byte)m);
                interval[m] = MaterialRules.TickInterval((byte)m);
                for (int o = 0; o < 256; o++)
                {
                    if (!MaterialRules.TryGetReaction((byte)m, (byte)o,
                            out byte sp, out byte op)) continue;
                    reactSelf[m * 256 + o] = sp;
                    reactOther[m * 256 + o] = op;
                }
            }

            _materialFlags = New(GraphicsBuffer.Target.Structured, 256, 4);
            _materialTick  = New(GraphicsBuffer.Target.Structured, 256, 4);
            _reactSelf     = New(GraphicsBuffer.Target.Structured, 256 * 256, 4);
            _reactOther    = New(GraphicsBuffer.Target.Structured, 256 * 256, 4);
            _materialFlags.SetData(flags);
            _materialTick.SetData(interval);
            _reactSelf.SetData(reactSelf);
            _reactOther.SetData(reactOther);
        }

        // =====================================================================
        // Wake requests (§7.6) -- uploaded, never read back
        // =====================================================================

        /// Ticks a wake request may wait for its chunk to reach the GPU.
        public const int MaxWakeDeferTicks = 120;

        public void ClearWakeRequests()
        {
            _wakeCount = 0;
            _wakePending.Clear();
        }

        /// True when the GPU clipmap already holds the chunk containing this
        /// region cell, i.e. CSPromote's SampleVoxel will read the CURRENT
        /// material rather than the pre-edit one. A chunk that was never dirty
        /// is ready by definition.
        /// Ready once the chunk has been uploaded at an epoch STRICTLY LATER
        /// than the edit that queued this request. Any such upload contains the
        /// edit, because UploadDirty copies the chunk's current CPU state.
        ///
        /// This replaced a plain !IsDirty(chunk) test, which STARVED: a cell
        /// edited every frame leaves its chunk dirty at every tick even though
        /// it is uploaded between them, so the request never came ready and
        /// only escaped via the stale timeout. The Phase 5c hold_paint case
        /// (holding the place button) measured that directly -- 8 voxels placed
        /// where 30 were asked for, 900/120 = the stale interval exactly.
        private bool IsMirrorReadyForCell(int3 voxel, long stamp)
        {
            TerrainClipmap m = Mirror;
            if (m == null) return true;      // no mirror at all: nothing to wait for

            int3 chunk = CoordMath.VoxelToChunk(voxel);

            // NOT DIRTY => the mirror already holds this chunk's current state,
            // so there is nothing to wait for and the request is ready NOW.
            //
            // This half is load-bearing and was missing at first. §8.3 wakes the
            // edited cell's whole 26-NEIGHBOURHOOD, and those neighbours are
            // routinely in a DIFFERENT chunk that was never edited and so is
            // never dirty. With only the epoch test below, no upload ever
            // advanced their chunk's epoch, so those requests waited out the
            // full MaxWakeDeferTicks. The Phase 5c large_pour case showed it as
            // voxels that looked permanently stranded but were merely 120 ticks
            // from being looked at.
            if (!m.IsDirty(chunk)) return true;

            // DIRTY => an upload is outstanding. Wait for one that happened
            // after the edit, since only such an upload can contain it.
            return m.LastUploadEpoch(chunk) > stamp;
        }

        /// The clipmap to measure readiness against. Tick() supplies it, but
        /// EDITS HAPPEN BEFORE THE FIRST TICK -- a vent places its first voxel
        /// in the same frame the scene starts simulating -- so fall back to the
        /// active mirror rather than to "no mirror".
        private TerrainClipmap Mirror => _mirrorForReadyTest ?? TerrainClipmap.Active;

        /// The mirror epoch to stamp a request with: the state of the world the
        /// edit was made against.
        ///
        /// THE DEFAULT HERE MUST BE CONSERVATIVE. Returning -1 for "I don't
        /// know the epoch yet" meant "already uploaded", which released the
        /// request on the very next tick against a mirror that did not contain
        /// the edit -- reintroducing the exact freeze this class exists to
        /// prevent, for the first edit of every scene. The Phase 5c rig caught
        /// it immediately: vent_sustained placed 1 voxel instead of 40 and left
        /// it floating. long.MaxValue instead means "not ready until a real
        /// epoch says so", so an unknown mirror errs toward waiting.
        private long CurrentMirrorStamp => Mirror?.UploadEpoch ?? long.MaxValue;

        /// Queue a world voxel to be considered for promotion next dispatch.
        /// Silently drops out-of-region coordinates and overflow, both of which
        /// are "this cell does not move this tick", never a lost byte.
        public void RequestWake(int3 worldVoxel)
        {
            // TILE ALLOCATION IS CPU-SIDE, DELIBERATELY (DESIGN_NOTE_7_2 §5.4).
            // Doing it on the GPU would need an atomic inside CSWakeScan, in the
            // tick, in the path FluidCA.compute's own header forbids. The CPU
            // already drives promotion, so an edit into a tile that does not yet
            // exist creates it here rather than being dropped.
            if (Tiles != null)
            {
                int3 tc = Tiles.TileOf(worldVoxel);
                if (Tiles.TryGetSlot(tc) == FluidTileMap.NO_TILE &&
                    Tiles.Acquire(tc) == FluidTileMap.NO_TILE)
                {
                    // Pool full: §7.7's guarded no-op. The material stays in
                    // terrain and CSWakeScan can find it once a tile frees.
                    WakeRejectedOutOfRegion++;
                    return;
                }
            }
            else if (!InRegion(worldVoxel)) { WakeRejectedOutOfRegion++; return; }

            if (!_wakePending.Add(worldVoxel, CurrentMirrorStamp)) { WakeRejectedFull++; return; }
            WakeRequestsQueuedTotal++;
        }

        /// §8.3's "scan 26-neighborhood for fluid/falling materials -> wake slots".
        public void RequestWakeNeighbourhood(int3 worldVoxel)
        {
            for (int dz = -1; dz <= 1; dz++)
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
                RequestWake(worldVoxel + new int3(dx, dy, dz));
        }

        public void RequestWakeRegionCell(int regionCell)
        {
            if (regionCell < 0 || regionCell >= _cellCount) { WakeRejectedOutOfRegion++; return; }
            RequestWake(RegionVoxel(regionCell));
        }

        /// Under §7.2's tiled active set this is "the voxel's tile exists",
        /// not "inside the region box". The box is only an addressing origin for
        /// the dense path; with tiles the active set IS the resident tiles.
        public bool InRegion(int3 v) => Tiles != null
            ? Tiles.SlotForVoxel(v) != FluidTileMap.NO_TILE
            : InRegion(v, RegionOriginVoxels, _regionDims);

        /// Pure form, so a caller can ask the question about a region it does not
        /// hold an instance of -- and so it is unit-testable without a GPU.
        public static bool InRegion(int3 v, int3 regionOrigin, int3 regionDims)
        {
            int3 r = v - regionOrigin;
            return r.x >= 0 && r.y >= 0 && r.z >= 0 &&
                   r.x < regionDims.x && r.y < regionDims.y && r.z < regionDims.z;
        }

        /// The ONE definition of which offsets a radius-`radius` sphere brush
        /// covers. Both the guard below and the code that actually writes the
        /// blob must use this, or they drift and the guard stops matching what
        /// gets written.
        public static bool SphereCovers(int dx, int dy, int dz, int radius)
            => dx * dx + dy * dy + dz * dz <= radius * radius;

        /// True iff EVERY cell a radius-`radius` sphere brush at `centre` would
        /// write lies inside the region -- i.e. a blob placed there would
        /// actually be simulated.
        ///
        /// WHY THIS EXISTS. RequestWake correctly and silently drops
        /// out-of-region wakes ("this cell does not move this tick"). That is
        /// right for the engine, but it means a CALLER that writes a mobile
        /// material outside the region gets no error and no movement: the voxel
        /// is committed to ChunkStore, uploaded to the mirror, drawn, and then
        /// never simulated. It hangs in mid-air forever. Sand that does not fall
        /// is the giveaway. Ask this BEFORE writing, not after.
        ///
        /// All-or-nothing on purpose: placing only the in-region cells of a blob
        /// that straddles the edge leaves a frozen rim outside it, which is the
        /// same silent-partial shape as the §9.4 residency-edge bug.
        public static bool SphereFitsInRegion(int3 centre, int radius, int3 regionOrigin, int3 regionDims)
        {
            for (int z = -radius; z <= radius; z++)
            for (int y = -radius; y <= radius; y++)
            for (int x = -radius; x <= radius; x++)
            {
                if (!SphereCovers(x, y, z, radius)) continue;
                if (!InRegion(centre + new int3(x, y, z), regionOrigin, regionDims)) return false;
            }
            return true;
        }

        /// Instance form against this region.
        public bool SphereFitsInRegion(int3 centre, int radius)
            => SphereFitsInRegion(centre, radius, RegionOriginVoxels, _regionDims);

        /// Cell index for a world voxel, or -1 when the voxel has no cell.
        /// MUST mirror the shader's RegionIndex exactly in both modes, or the
        /// CPU and GPU disagree about which cell a wake request names.
        public int RegionIndex(int3 v)
        {
            if (Tiles != null) return Tiles.CellIndex(v);
            int3 r = v - RegionOriginVoxels;
            return r.x | (r.y << _shiftX) | (r.z << (_shiftX + _shiftY));
        }

        /// Cell index -> world voxel. MUST mirror the shader's RegionVoxel in
        /// both modes; the two are inverses and a disagreement would put a wake
        /// request on a different cell than the one that asked for it.
        public int3 RegionVoxel(int index)
        {
            if (Tiles != null)
            {
                int slot = index >> _tileCellShift;
                int local = index & ((1 << _tileCellShift) - 1);
                int m = Tiles.TileEdge - 1;
                int3 inTile = new int3(local & m, (local >> _tileShift) & m,
                                       local >> (_tileShift + _tileShift));
                return (Tiles.CoordOfSlot(slot) << _tileShift) + inTile;
            }
            int mx = _regionDims.x - 1, my = _regionDims.y - 1;
            return new int3(index & mx, (index >> _shiftX) & my,
                            index >> (_shiftX + _shiftY)) + RegionOriginVoxels;
        }

        // =====================================================================
        // The tick
        // =====================================================================

        /// One CA tick: Clear -> Promote -> React -> Intent+Claim -> Commit -> Sweep.
        /// Each stage is its own dispatch, which is what puts a barrier between
        /// them -- CSReact in particular RELIES on that barrier (see its header).
        public void Tick(TerrainClipmap clipmap)
        {
            if (clipmap == null) throw new ArgumentNullException(nameof(clipmap));
            TickCount++;

            // Release only the requests the GPU can actually act on. When the
            // mirror is already current -- every rig and every existing test --
            // this releases them on the same tick they were queued, so nothing
            // about existing behaviour changes.
            _mirrorForReadyTest = clipmap;
            // COLLECT VOXELS, CONVERT TO CELLS HERE. The queue is keyed on the
            // world voxel because a tile slot is not stable across the deferral
            // window; the cell index is derived now, against the tile set this
            // very tick is about to upload. A voxel whose tile has since gone is
            // dropped rather than pointed at another tile's cells.
            int collected = _wakePending.Collect(_mirrorReady, _wakeVoxelScratch,
                                                 _wakeVoxelScratch.Length);
            _wakeCount = 0;
            for (int i = 0; i < collected; i++)
            {
                int cell = RegionIndex(_wakeVoxelScratch[i]);
                if (cell >= 0 && cell < _cellCount) _wakeScratch[_wakeCount++] = cell;
                else WakeRejectedOutOfRegion++;
            }

            _ring = (_ring + 1) % RING;          // advance BEFORE binding
            _opCounters[_ring].SetData(_opCountersZero);
            // Cleared every tick: these are PER-TICK counters, and a running
            // total would hide which tick a stage went silent on.
            _debugCounters.SetData(_debugZero);
            LastDispatchedWakeCount = _wakeCount;
            WakeDispatchedTotal += _wakeCount;
            if (_wakeCount > 0) _wakeRequests.SetData(_wakeScratch, 0, 0, _wakeCount);

            UploadTileState();
            BindGlobals(clipmap);

            // AFTER binding, so the tick that re-centred is itself seeded. Doing
            // it before would upload 0 on the very tick the centre moved and
            // spend the budget on the tick after, which is off by one in the
            // direction that matters least visibly and is hardest to spot.
            if (_recentreSeedTicks > 0) { _recentreSeedTicks--; RecentreSeedTicksRun++; }

            if (!SplitDispatchEncodersForCapture) _cb.Clear();
            Dispatch(_kClear, DispatchCells, LblClear);
            if (_wakeCount > 0) { Dispatch(_kPromote, _wakeCount, LblPromote); PromoteDispatchesTotal++; }
            Dispatch(_kReact, _slotCapacity, LblReact);
            Dispatch(_kIntent, _slotCapacity, LblIntent);
            Dispatch(_kCommit, DispatchCells, LblCommit);
            Dispatch(_kWakeScan, DispatchCells, LblWakeScan);   // immediate GPU-side wake
            Dispatch(_kSweep, _slotCapacity, LblSweep);
            // A.5's free list. AFTER every clear site, and after Promote, so an
            // index returned this tick is first reusable on the next one.
            Dispatch(_kRecycle, _slotCapacity, LblRecycle);
            Dispatch(_kFinalize, 1, LblFinalize);   // publish the count into ops[0]
            if (!SplitDispatchEncodersForCapture) Graphics.ExecuteCommandBuffer(_cb);

            _wakeCount = 0;
        }

        /// Pushes the CPU-owned tile directory to the GPU and compacts this
        /// tick's active slots.
        ///
        /// THE DIRECTORY IS MAINTAINED EXACTLY ON THE CPU -- entries are cleared
        /// on release -- so the shader never needs the identity check
        /// FluidTileMap does on its side. There is no stale entry to catch,
        /// which is the only reason RegionIndex can be one indexed read.
        private void UploadTileState()
        {
            if (Tiles == null) { _activeTileCount = 0; return; }

            for (int i = 0; i < _dirScratch.Length; i++) _dirScratch[i] = FluidTileMap.NO_TILE;

            _activeTileCount = 0;
            foreach (int3 coord in Tiles.ResidentTileCoords())
            {
                int slot = Tiles.TryGetSlot(coord);
                if (slot == FluidTileMap.NO_TILE) continue;

                _dirScratch[Tiles.RingIndex(coord)] = slot;
                _coordScratch[slot * 4 + 0] = coord.x;
                _coordScratch[slot * 4 + 1] = coord.y;
                _coordScratch[slot * 4 + 2] = coord.z;
                _activeScratch[_activeTileCount++] = slot;
            }

            _tileDirectory.SetData(_dirScratch);
            _tileCoords.SetData(_coordScratch);
            if (_activeTileCount > 0) _activeTileSlots.SetData(_activeScratch, 0, 0, _activeTileCount);
        }

        /// Cells dispatched for the per-cell kernels this tick. Dense: the whole
        /// region. Tiled: only ACTIVE tiles, so per-tick cost tracks fluid
        /// present rather than pool capacity.
        private int DispatchCells =>
            Tiles == null ? _regionCellCount : _activeTileCount * Tiles.TileCells;

        private void BindGlobals(TerrainClipmap clipmap)
        {
            _cs.SetInts("_RegionOriginVoxels", RegionOriginVoxels.x, RegionOriginVoxels.y, RegionOriginVoxels.z, 0);
            _cs.SetInts("_RegionDimsVoxels", _regionDims.x, _regionDims.y, _regionDims.z, 0);
            _cs.SetInts("_RegionShifts", _shiftX, _shiftY, 0, 0);
            _cs.SetInts("_PlayerVoxel", PlayerVoxel.x, PlayerVoxel.y, PlayerVoxel.z, 0);
            _cs.SetInt("_ActiveRadiusVoxels", ActiveRadiusVoxels);
            _cs.SetInt("_SleepRadiusVoxels", SleepRadiusVoxels);

            // §7.2 addressing mode. EVERY ONE OF THESE MUST BE SET EVEN WHEN
            // DENSE: an unset compute uniform is 0, and _HomeShift = 0 would
            // silently repack every slot's home address.
            bool tiled = Tiles != null;
            _cs.SetInt("_Tiled", tiled ? 1 : 0);
            _cs.SetInt("_TileShift", tiled ? _tileShift : 0);
            _cs.SetInt("_TileCellShift", tiled ? _tileCellShift : 0);
            _cs.SetInt("_HomeShift", tiled ? _tileCellShift : 9);   // 9 = A.5's 512-cell brick
            _cs.SetInt("_RingShiftY", _ringShiftY);
            _cs.SetInt("_RingShiftZ", _ringShiftZ);
            _cs.SetInt("_ActiveTileCount", _activeTileCount);
            if (tiled)
                _cs.SetInts("_RingMaskTiles", Tiles.RingDimsTiles.x - 1,
                            Tiles.RingDimsTiles.y - 1, Tiles.RingDimsTiles.z - 1, 0);
            else
                _cs.SetInts("_RingMaskTiles", 0, 0, 0, 0);
            _cs.SetInt("_RecentreSeed", _recentreSeedTicks > 0 ? 1 : 0);
            _cs.SetInt("_Tick", TickCount);
            _cs.SetInt("_SlotCapacity", _slotCapacity);
            _cs.SetInt("_RegionCellCount", _cellCount);
            _cs.SetInt("_WakeRequestCount", _wakeCount);
            _cs.SetInt("_MaxOpsPerFrame", MaxOpsPerFrame);
            _cs.SetInt("_SleepTicks", EngineConfig.FLUID_SLEEP_TICKS);

            int3 wdc = clipmap.WindowDimsChunks, wdb = clipmap.WindowDimsBricks, wob = clipmap.WindowOriginBricks;
            _cs.SetInts("_WindowDimsChunksPacked", wdc.x, wdc.y, wdc.z, 0);
            _cs.SetInts("_WindowDimsBricksPacked", wdb.x, wdb.y, wdb.z, 0);
            _cs.SetInts("_WindowOriginBricksPacked", wob.x, wob.y, wob.z, 0);

            // Bound even when dense: HLSL requires every declared StructuredBuffer
            // to have something bound, and an unbound one reads as garbage rather
            // than failing loudly. They are 1-element dummies in that case.
            foreach (int k in new[] { _kClear, _kPromote, _kReact, _kIntent, _kCommit, _kSweep, _kFinalize, _kWakeScan, _kRecycle })
            {
                _cs.SetBuffer(k, "TileDirectory", _tileDirectory ?? _dummyTileBuf);
                _cs.SetBuffer(k, "TileCoords", _tileCoords ?? _dummyTileBuf);
                _cs.SetBuffer(k, "ActiveTileSlots", _activeTileSlots ?? _dummyTileBuf);
            }

            foreach (int k in new[] { _kClear, _kPromote, _kReact, _kIntent, _kCommit, _kSweep, _kFinalize, _kWakeScan, _kRecycle })
            {
                _cs.SetBuffer(k, "ClipmapBuffer", clipmap.ClipmapBuffer);
                _cs.SetBuffer(k, "BrickDataBuffer", clipmap.BrickDataBuffer);
                _cs.SetBuffer(k, "FluidSlots", _slots);
                _cs.SetBuffer(k, "ClaimBuffer", _claim);
                _cs.SetBuffer(k, "SlotAtBuffer", _slotAt);
                _cs.SetBuffer(k, "FreeList", _freeList);
                _cs.SetBuffer(k, "ReactedBuffer", _reacted);
                _cs.SetBuffer(k, "WakeMark", _wakeMark);
                _cs.SetBuffer(k, "Counters", _counters);
                _cs.SetBuffer(k, "OpsBuffer", _ops[_ring]);
                _cs.SetBuffer(k, "OpCounters", _opCounters[_ring]);
                _cs.SetBuffer(k, "WakeRequests", _wakeRequests);
                _cs.SetBuffer(k, "DebugCounters", _debugCounters);
                _cs.SetBuffer(k, "MaterialFlags", _materialFlags);
                _cs.SetBuffer(k, "MaterialTickInterval", _materialTick);
                _cs.SetBuffer(k, "ReactionSelfProduct", _reactSelf);
                _cs.SetBuffer(k, "ReactionOtherProduct", _reactOther);
            }
        }

        private void Dispatch(int kernel, int threads, string label)
        {
            if (threads <= 0) return;
            if (SplitDispatchEncodersForCapture)
            {
                // One command buffer per dispatch => one encoder per kernel.
                _cb.Clear();
                _cb.BeginSample(label);
                DispatchGrid(kernel, threads);
                _cb.EndSample(label);
                Graphics.ExecuteCommandBuffer(_cb);
                return;
            }
            _cb.BeginSample(label);
            DispatchGrid(kernel, threads);
            _cb.EndSample(label);
        }

        /// Thread groups are capped at 65535 PER DIMENSION. A dense 64^3 region
        /// is 4096 groups and never came close; a tiled active set of 488 tiles
        /// x 32768 cells is 250,000, and exceeding the cap TRUNCATES THE
        /// DISPATCH SILENTLY -- measured as CSIntent placing 3253 claims that
        /// CSCommit never saw, while promote/intent counters looked healthy.
        ///
        /// Dispatches that still fit stay strictly 1D, so the dense path is
        /// bit-identical: gy = 1 makes LinearThread collapse to id.x.
        private const int MaxGroupsPerDim = 60000;

        private void DispatchGrid(int kernel, int threads)
        {
            int groups = (threads + 63) / 64;
            int gx = groups, gy = 1;
            if (groups > MaxGroupsPerDim)
            {
                gx = 1024;                                  // 65,536 threads per row
                gy = (groups + gx - 1) / gx;
            }
            // Set INSIDE the command buffer so it is sequenced with THIS
            // dispatch; _cs.SetInt would apply to whichever executes last.
            _cb.SetComputeIntParam(_cs, "_DispatchRowThreads", gx * 64);
            _cb.DispatchCompute(_cs, kernel, gx, gy, 1);
        }

        // =====================================================================

        private static void RequirePow2(int v, string name)
        {
            if (v <= 0 || (v & (v - 1)) != 0)
                throw new ArgumentException($"{name} must be a positive power of two (got {v}).", name);
        }

        private static int Log2(int p) { int n = 0; while ((p >> n) > 1) n++; return n; }

        /// Blocking read of this tick's diagnostic counters. Rig/debug only --
        /// it stalls the pipeline, which is exactly why it is not on any
        /// shipped path.
        public uint[] ReadDebugCountersBlocking()
        {
            _debugCounters.GetData(_debugScratch);
            return _debugScratch;
        }

        /// One-line §10.4 dump of the last tick's per-stage activity.
        public string DebugDump()
        {
            uint[] c = ReadDebugCountersBlocking();
            var sb = new System.Text.StringBuilder();
            sb.Append("cpu.wake_queued_total=").Append(WakeRequestsQueuedTotal)
              .Append(" cpu.wake_dispatched_total=").Append(WakeDispatchedTotal)
              .Append(" cpu.promote_dispatches=").Append(PromoteDispatchesTotal)
              .Append(" cpu.wake_dispatched_lasttick=").Append(LastDispatchedWakeCount)
              .Append(" cpu.wake_rej_region=").Append(WakeRejectedOutOfRegion)
              .Append(" cpu.wake_rej_full=").Append(WakeRejectedFull)
              .Append(" | ");
            for (int i = 0; i < DebugSlots; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(DebugCounterNames[i]).Append('=').Append(c[i]);
            }
            return sb.ToString();
        }

        public void Dispose()
        {
            _slots?.Dispose(); _claim?.Dispose(); _slotAt?.Dispose(); _reacted?.Dispose();
            _wakeMark?.Dispose(); _wakeMark = null;
            _cb?.Dispose(); _cb = null;
            _counters?.Dispose(); _wakeRequests?.Dispose();
            _freeList?.Dispose(); _freeList = null;
            _tileDirectory?.Dispose(); _tileDirectory = null;
            _tileCoords?.Dispose(); _tileCoords = null;
            _activeTileSlots?.Dispose(); _activeTileSlots = null;
            _dummyTileBuf?.Dispose(); _dummyTileBuf = null;
            for (int i = 0; i < RING; i++)
            {
                _ops[i]?.Dispose(); _ops[i] = null;
                _opCounters[i]?.Dispose(); _opCounters[i] = null;
            }
            _debugCounters?.Dispose(); _debugCounters = null;
            _materialFlags?.Dispose(); _materialTick?.Dispose();
            _reactSelf?.Dispose(); _reactOther?.Dispose();
            _slots = _claim = _slotAt = _reacted = _counters = null;
            _wakeRequests = _materialFlags = _materialTick = _reactSelf = _reactOther = null;
        }
    }
}
