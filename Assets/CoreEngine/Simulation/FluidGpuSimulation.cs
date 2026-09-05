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

        private GraphicsBuffer _materialFlags;
        private GraphicsBuffer _materialTick;
        private GraphicsBuffer _reactSelf;
        private GraphicsBuffer _reactOther;

        private readonly int[] _wakeScratch;
        private int _wakeCount;

        /// Requests wait HERE until the GPU mirror can see the edit that caused
        /// them. See FluidWakeQueue for the frozen-fluid bug this fixes.
        private readonly FluidWakeQueue _wakePending;
        private readonly Func<int, long, bool> _mirrorReady;  // cached: called per pending request per tick
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
            return true;
        }

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
        {
            _cs = fluidCA != null ? fluidCA
                : throw new ArgumentNullException(nameof(fluidCA), "FluidCA.compute not assigned");

            RequirePow2(regionDims.x, "regionDims.x");
            RequirePow2(regionDims.y, "regionDims.y");
            RequirePow2(regionDims.z, "regionDims.z");

            _regionDims = regionDims;
            _regionCellCount = regionDims.x * regionDims.y * regionDims.z;
            _shiftX = Log2(regionDims.x);
            _shiftY = Log2(regionDims.y);
            _slotCapacity = Math.Min(slotCapacity, EngineConfig.MAX_ACTIVE_FLUID);
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
            _claim   = New(GraphicsBuffer.Target.Structured, _regionCellCount, 4);
            _slotAt  = New(GraphicsBuffer.Target.Structured, _regionCellCount, 4);
            _reacted = New(GraphicsBuffer.Target.Structured, _regionCellCount, 4);
            _wakeMark = New(GraphicsBuffer.Target.Structured, _regionCellCount, 4);
            _counters = New(GraphicsBuffer.Target.Structured, 4, 4);
            for (int i = 0; i < RING; i++)
            {
                _ops[i] = New(GraphicsBuffer.Target.Structured, maxOpsPerFrame + 1, FluidWriteOp.SizeBytes);
                _opCounters[i] = New(GraphicsBuffer.Target.Structured, 4, 4);
            }
            _wakeRequests = New(GraphicsBuffer.Target.Structured, maxOpsPerFrame, 4);
            _cb = new CommandBuffer { name = "VE.FluidCA" };
            _wakeScratch = new int[maxOpsPerFrame];
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
            var noneFill = new int[_regionCellCount];
            for (int i = 0; i < _regionCellCount; i++) noneFill[i] = -1;
            _slotAt.SetData(noneFill);
            _claim.SetData(noneFill);
            _reacted.SetData(new int[_regionCellCount]);
            _wakeMark.SetData(new int[_regionCellCount]);
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
        private bool IsMirrorReadyForCell(int cell, long stamp)
        {
            TerrainClipmap m = Mirror;
            if (m == null) return true;      // no mirror at all: nothing to wait for

            int3 chunk = CoordMath.VoxelToChunk(RegionVoxel(cell));

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
            if (!InRegion(worldVoxel)) { WakeRejectedOutOfRegion++; return; }
            if (!_wakePending.Add(RegionIndex(worldVoxel), CurrentMirrorStamp)) { WakeRejectedFull++; return; }
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
            if (regionCell < 0 || regionCell >= _regionCellCount) { WakeRejectedOutOfRegion++; return; }
            if (!_wakePending.Add(regionCell, CurrentMirrorStamp)) { WakeRejectedFull++; return; }
            WakeRequestsQueuedTotal++;
        }

        public bool InRegion(int3 v) => InRegion(v, RegionOriginVoxels, _regionDims);

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

        public int RegionIndex(int3 v)
        {
            int3 r = v - RegionOriginVoxels;
            return r.x | (r.y << _shiftX) | (r.z << (_shiftX + _shiftY));
        }

        public int3 RegionVoxel(int index)
        {
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
            _wakeCount = _wakePending.Collect(_mirrorReady, _wakeScratch, _wakeScratch.Length);

            _ring = (_ring + 1) % RING;          // advance BEFORE binding
            _opCounters[_ring].SetData(_opCountersZero);
            // Cleared every tick: these are PER-TICK counters, and a running
            // total would hide which tick a stage went silent on.
            _debugCounters.SetData(_debugZero);
            LastDispatchedWakeCount = _wakeCount;
            WakeDispatchedTotal += _wakeCount;
            if (_wakeCount > 0) _wakeRequests.SetData(_wakeScratch, 0, 0, _wakeCount);

            BindGlobals(clipmap);

            if (!SplitDispatchEncodersForCapture) _cb.Clear();
            Dispatch(_kClear, _regionCellCount, LblClear);
            if (_wakeCount > 0) { Dispatch(_kPromote, _wakeCount, LblPromote); PromoteDispatchesTotal++; }
            Dispatch(_kReact, _slotCapacity, LblReact);
            Dispatch(_kIntent, _slotCapacity, LblIntent);
            Dispatch(_kCommit, _regionCellCount, LblCommit);
            Dispatch(_kWakeScan, _regionCellCount, LblWakeScan);   // immediate GPU-side wake
            Dispatch(_kSweep, _slotCapacity, LblSweep);
            // A.5's free list. AFTER every clear site, and after Promote, so an
            // index returned this tick is first reusable on the next one.
            Dispatch(_kRecycle, _slotCapacity, LblRecycle);
            Dispatch(_kFinalize, 1, LblFinalize);   // publish the count into ops[0]
            if (!SplitDispatchEncodersForCapture) Graphics.ExecuteCommandBuffer(_cb);

            _wakeCount = 0;
        }

        private void BindGlobals(TerrainClipmap clipmap)
        {
            _cs.SetInts("_RegionOriginVoxels", RegionOriginVoxels.x, RegionOriginVoxels.y, RegionOriginVoxels.z, 0);
            _cs.SetInts("_RegionDimsVoxels", _regionDims.x, _regionDims.y, _regionDims.z, 0);
            _cs.SetInts("_RegionShifts", _shiftX, _shiftY, 0, 0);
            _cs.SetInts("_PlayerVoxel", PlayerVoxel.x, PlayerVoxel.y, PlayerVoxel.z, 0);
            _cs.SetInt("_ActiveRadiusVoxels", ActiveRadiusVoxels);
            _cs.SetInt("_SleepRadiusVoxels", SleepRadiusVoxels);
            _cs.SetInt("_Tick", TickCount);
            _cs.SetInt("_SlotCapacity", _slotCapacity);
            _cs.SetInt("_RegionCellCount", _regionCellCount);
            _cs.SetInt("_WakeRequestCount", _wakeCount);
            _cs.SetInt("_MaxOpsPerFrame", MaxOpsPerFrame);
            _cs.SetInt("_SleepTicks", EngineConfig.FLUID_SLEEP_TICKS);

            int3 wdc = clipmap.WindowDimsChunks, wdb = clipmap.WindowDimsBricks, wob = clipmap.WindowOriginBricks;
            _cs.SetInts("_WindowDimsChunksPacked", wdc.x, wdc.y, wdc.z, 0);
            _cs.SetInts("_WindowDimsBricksPacked", wdb.x, wdb.y, wdb.z, 0);
            _cs.SetInts("_WindowOriginBricksPacked", wob.x, wob.y, wob.z, 0);

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
                _cb.DispatchCompute(_cs, kernel, (threads + 63) / 64, 1, 1);
                _cb.EndSample(label);
                Graphics.ExecuteCommandBuffer(_cb);
                return;
            }
            _cb.BeginSample(label);
            _cb.DispatchCompute(_cs, kernel, (threads + 63) / 64, 1, 1);
            _cb.EndSample(label);
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
