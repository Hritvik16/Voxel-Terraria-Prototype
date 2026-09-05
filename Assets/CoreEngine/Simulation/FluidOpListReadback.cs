// ==========================================
// Assets/CoreEngine/Simulation/FluidOpListReadback.cs
//
// Phase 5b, file 2 of §13's ordered list: "reads the bounded write-op list back
// via AsyncGPUReadback, applies each op through THE SAME SetVoxel used by
// mining/building (§8.3), marks chunks dirty."
//
// -------------------------------------------------------------------------
// THIS IS THE ONLY GPU->CPU STATE PATH IN THE ENGINE (§3.9)
// -------------------------------------------------------------------------
// §7.2: "CPU reads the op-list back via AsyncGPUReadback (bounded size, not a
// full state cube) and applies every op through the exact same SetVoxel path
// used by mining and building (§8.3). No second terrain-write mechanism exists
// anywhere in the engine."
//
// Applied through ChunkStore.SetVoxel, which is the concrete §8.3 path and the
// engine's single terrain writer -- NOT through a private array, and not by
// touching the clipmap. SetVoxel marks the chunk dirty itself, so the clipmap
// upload and the delta save both follow for free (§3.7, §4.2).
//
// -------------------------------------------------------------------------
// THE BOUNDEDNESS ASSERTION (§13's named failure signature)
// -------------------------------------------------------------------------
// §13 Phase 5b: "The op-list is genuinely bounded, not accidentally the size of
// the full active-slot count -- instrument and log its size per frame; assert it
// correlates with CHANGED cells, not total active cells." The failure signature
// is "Op-list size scales with total active slots instead of changed cells ->
// the Commit kernel is appending unconditionally instead of only on actual
// moves." OpsLastFrame / ActiveSlotsLastFrame are recorded here per frame and
// the acceptance rig asserts on their ratio; the emission gate itself lives in
// CSCommit, which appends only after every claim validity check has passed.
//
// -------------------------------------------------------------------------
// LATENCY IS EXPECTED AND BOUNDED, NOT A BUG (§7.2, §8.2)
// -------------------------------------------------------------------------
// The applied fluid state lags the GPU's decision by the readback pipeline
// delay, typically 1-3 frames. That is the deliberate, mitigated trade §7.2
// names. This class records the measured lag in FramesInFlight so §13's
// "fluid moves visibly lag by more than a couple of frames" diagnostic is a
// number rather than an impression -- at this phase the raw latency IS the only
// available diagnostic, because §8.2's speed clamp does not exist until Phase 6.

using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using VoxelEngine.Memory;

namespace VoxelEngine.Simulation
{
    public sealed class FluidOpListReadback : IDisposable
    {
        private readonly FluidGpuSimulation _sim;
        private readonly ChunkStore _store;

        private struct InFlight
        {
            // TWO requests, not four. With four concurrent AsyncGPUReadback
            // requests per frame the FIRST one issued came back with hasError
            // and no logged reason -- consistently, and regardless of which
            // buffer it targeted (it still failed when it and the wake-count
            // request read the SAME buffer). Cutting the request count is what
            // this addresses; the underlying per-request failure is documented
            // in the report as unexplained rather than claimed as understood.
            /// ONE request per frame. The op count rides in element 0's header
            /// so there is no second, small buffer to read -- that small read is
            /// exactly what kept failing.
            public AsyncGPUReadbackRequest OpsReq;
            public int IssuedFrame;
            public int ActiveSlotsAtIssue;
        }

        private readonly Queue<InFlight> _inFlight = new Queue<InFlight>();


        // ---- Instrumentation (§13's boundedness gate) ----
        public int OpsLastFrame { get; private set; }
        public int WakeCellsLastFrame { get; private set; }
        public long OpsTotal { get; private set; }
        public int PeakOpsInAFrame { get; private set; }
        public int FramesInFlight { get; private set; }
        public int MaxFramesInFlightSeen { get; private set; }
        public long AppliedVoxelWrites { get; private set; }
        /// SPLIT FROM A SINGLE CONFLATED COUNTER. These two failures look
        /// identical in the report if they share a slot -- "the append buffer
        /// filled" and "the readback never landed" both showed up as
        /// OverflowFramesTotal, which made a run with ZERO ops and a run with
        /// TOO MANY ops indistinguishable. They are opposite problems and
        /// diagnosing either while they share a counter is impossible.
        public int ReadbackErrorsTotal { get; private set; }
        public int AppendOverflowFramesTotal { get; private set; }
        /// Last readback error's stage, for §10.4-style triage.
        public string LastReadbackError { get; private set; } = "none";
        /// Ops dropped because terrain changed under them between decision and
        /// application. Expected to be non-zero in edit scenarios; a large value
        /// in a quiet scenario would mean the CA is deciding against stale state.
        public long StaleOpsDropped { get; private set; }
        /// Ops refused because a half of the move lay in a chunk that is not
        /// resident. NOT a mass loss -- the material stays exactly where it was,
        /// which is the point. A non-zero value here means fluid is live next to
        /// a streaming edge; see Phase 5d.
        public long OpsDroppedNonResident { get; private set; }
        public int SkippedIssuesTotal { get; private set; }

        /// §7.2 states the readback lag is "typically 1-3 frames". Nothing was
        /// ENFORCING that: IssueReadback queued unconditionally every frame, and
        /// with four requests per frame the queue reached 6 frames deep = 24
        /// concurrent AsyncGPUReadback requests, at which point the oldest
        /// request started coming back with hasError and no logged reason.
        /// Capping the queue at the spec's own bound both fixes that and makes
        /// the stated 1-3 frames a property of the code rather than a hope.
        /// Skipping an issue costs one frame of fluid motion, never a voxel.
        /// ONE outstanding op-list, and this is a CORRECTNESS bound, not a
        /// throughput knob.
        ///
        /// The CA reads terrain from the clipmap to decide moves (§7.3's
        /// Air-Only rule). Its own committed moves only reach terrain after the
        /// CPU applies the op-list and re-uploads. If a second tick runs before
        /// that lands, it decides against terrain that does not yet contain its
        /// own previous decisions -- and the same source gets committed twice.
        /// Measured directly: at 3 outstanding op-lists, sand_column reported
        /// conserved count GPU 25 vs CPU 24, a GAIN of one grain. At 1 it
        /// matches.
        ///
        /// This is §3.9's frame order taken literally -- "terrain upload (dirty,
        /// from LAST FRAME'S APPLIED OPS + edits) -> fluid Clear/Intent+Claim/
        /// Commit" -- which only reads as one CA tick per applied op-list.
        /// The cost is that the CA ticks once per readback round-trip rather
        /// than once per frame. That is a real throughput limit and it is the
        /// honest reading of the design; raising it trades conservation for
        /// tick rate, which §7.3 does not permit.
        public const int MaxFramesInFlightDefault = 1;

        /// Settable ONLY so the validation rig can reproduce the conservation
        /// GAIN on demand (-inflight N) and so a test can prove the ledger still
        /// catches it. Nothing on a shipped path may raise this.
        /// FluidReadbackInvariantTests pins the default at 1.
        public static int MaxFramesInFlight { get; set; } = MaxFramesInFlightDefault;

        /// BACK-PRESSURE. The caller must not run another CA tick while this is
        /// false. Ring-buffering the op-list alone did NOT fix the readback
        /// errors, and this is why: the ring advanced once per TICK while the
        /// queue drained once per COMPLETION, so a skipped issue desynchronised
        /// them and the ring wrapped onto a slot whose readback was still
        /// outstanding. Gating the whole tick -- not just the issue -- keeps
        /// ring advance and queue depth in lockstep, which is the actual
        /// invariant: a slot is reused only after its own readback has landed.
        public bool CanIssue => _inFlight.Count < MaxFramesInFlight;

        /// Invoked for every voxel the op-list applies. EXISTS BECAUSE
        /// ChunkStore.SetVoxel marks the CHUNK dirty but TerrainClipmap keeps a
        /// separate _dirtyChunks set that only StreamManager feeds -- so in a
        /// scene without a StreamManager the upload silently early-returns and
        /// the GPU reads stale terrain forever. The owner of the clipmap hooks
        /// this to close that gap; in the shipped streaming path StreamManager
        /// already does the equivalent.
        public Action<int3> OnVoxelApplied;

        public FluidOpListReadback(FluidGpuSimulation sim, ChunkStore store)
        {
            _sim = sim ?? throw new ArgumentNullException(nameof(sim));
            _store = store ?? throw new ArgumentNullException(nameof(store));

        }

        /// Issue this frame's readback. Call AFTER FluidGpuSimulation.Tick, and
        /// off the critical path -- §3.9's frame order puts it after the
        /// raymarch, applied next frame.
        public void IssueReadback(int activeSlotsHint)
        {
            if (!CanIssue) { SkippedIssuesTotal++; return; }

            if (_sim.CurrentRing < 0) return;

            _inFlight.Enqueue(new InFlight
            {
                OpsReq = AsyncGPUReadback.Request(_sim.OpsBuffer),
                IssuedFrame = Time.frameCount,
                ActiveSlotsAtIssue = activeSlotsHint,
            });
        }

        /// Apply whatever has landed. Returns the number of ops applied.
        /// Pump calls since the op-list last CAME BACK from the GPU. §8.2's
        /// "timestamp on the last applied batch", in the unit that matters: one
        /// pump per frame, so this is frames-since-the-readback-returned.
        ///
        /// IT COUNTS RETURNED BATCHES, NOT WRITTEN VOXELS, and the difference is
        /// the whole point. A first version reset only when a voxel was actually
        /// applied, which meant the counter climbed without bound whenever fluid
        /// was merely SETTLED -- the integrated rig measured 336 "stale" frames
        /// beside a pond that was simply at rest. Feeding that to the speed
        /// clamp would throttle a player to 20 m/s during ordinary play with no
        /// fluid anywhere near them. An empty op-list is a healthy readback
        /// saying nothing moved; a STALL is the readback not coming back at all,
        /// which is what §8.2 is actually protecting against.
        ///
        /// SweptCCD.SpeedClampMps consumes this. §8.2: "A speed clamp (to ~20
        /// m/s) engages if op-list readback stalls beyond ~2-3 frames". Until
        /// §8.6's buoyancy existed there was nothing to clamp, which is why this
        /// was not added in Phase 5.
        public int FramesSinceLastApplied { get; private set; }

        public int PumpAndApply()
        {
            int applied = 0;
            FramesSinceLastApplied++;
            FramesInFlight = _inFlight.Count;
            if (FramesInFlight > MaxFramesInFlightSeen) MaxFramesInFlightSeen = FramesInFlight;

            while (_inFlight.Count > 0)
            {
                InFlight f = _inFlight.Peek();
                if (f.OpsReq.hasError)
                {
                    // §9's contract: never crash. A failed readback costs one
                    // frame of fluid motion, never a voxel -- the terrain bytes
                    // are untouched and the slots simply retry next tick.
                    LastReadbackError = "ops";
                    _inFlight.Dequeue();
                    ReadbackErrorsTotal++;
                    continue;
                }
                if (!f.OpsReq.done) break;

                _inFlight.Dequeue();
                // A BATCH CAME BACK. That is what "not stale" means -- see the
                // property's comment. An empty op-list is a HEALTHY readback
                // reporting that nothing moved, not a stalled one.
                FramesSinceLastApplied = 0;
                applied += Apply(f);
            }
            return applied;
        }

        private int Apply(InFlight f)
        {
            NativeArray<FluidWriteOp> all = f.OpsReq.GetData<FluidWriteOp>();
            int opCount = all.Length > 0 ? all[0].dx : 0;   // element 0 is the header
            int wakeCount = 0;

            if (opCount >= _sim.MaxOpsPerFrame || wakeCount >= _sim.MaxOpsPerFrame)
            {
                // The append buffer filled. Ops beyond the cap were dropped by
                // the GPU, which is a MOTION loss, not a mass loss: the terrain
                // bytes for those moves were never written, so every drop is
                // still exactly where it was. Counted rather than hidden.
                AppendOverflowFramesTotal++;
                opCount = Math.Min(opCount, _sim.MaxOpsPerFrame);
                wakeCount = Math.Min(wakeCount, _sim.MaxOpsPerFrame);
            }

            OpsLastFrame = opCount;
            WakeCellsLastFrame = wakeCount;
            OpsTotal += opCount;
            if (opCount > PeakOpsInAFrame) PeakOpsInAFrame = opCount;

            if (opCount > 0)
            {
                for (int i = 0; i < opCount && (i + 1) < all.Length; i++)
                {
                    FluidWriteOp op = all[i + 1];          // +1: skip the header

                    // RE-VALIDATE AGAINST CURRENT TERRAIN BEFORE APPLYING.
                    // The op was decided on the GPU 1+ frames ago; an edit may
                    // have landed on either cell since. Applying a stale move
                    // half is how place_block/mine_drop drifted by one drop.
                    // Both halves apply, or neither does.
                    // RESIDENCY IS PART OF VALIDITY, NOT JUST MATERIAL.
                    //
                    // MEASURED BUG (Phase 5d, fixed here): with the CA's region
                    // straddling a chunk boundary and ONE side evicted, this
                    // path destroyed mass silently -- 7 water voxels of 52, with
                    // StaleOpsDropped staying 0 because nothing looked stale.
                    //
                    // The mechanism is that ChunkStore.GetVoxel returns Air for
                    // a NON-RESIDENT chunk, and its own comment says that is
                    // "DELIBERATELY ambiguous with real air" and that callers
                    // needing the distinction must ask IsResident/IsInWindow.
                    // This path never asked. So for an op whose Dst was in an
                    // evicted chunk:
                    //     GetVoxel(Dst) == Air == ExpectedAtDst  -> looked valid
                    //     SetVoxel(Dst, m)                       -> silent no-op
                    //     SetVoxel(Src, 0)                       -> succeeded
                    // and the material existed nowhere afterwards. The CA can
                    // generate exactly that op because SampleVoxel also returns
                    // AIR outside the window, and AIR reads as "free to move
                    // into".
                    //
                    // BOTH halves are checked, because the mirror case gains
                    // mass: a non-resident SRC means the vacate is the half that
                    // silently no-ops while the write lands. This op is
                    // documented directly above as "both halves apply, or
                    // neither does", and residency is part of being able to.
                    //
                    // This is a GUARD, not §7.4's moving active radius. Fluid
                    // that cannot move into unloaded space simply stays where it
                    // is -- which is the behaviour Phase 5d defined as correct
                    // BEFORE measuring, and what already happens when the whole
                    // region is evicted.
                    if (!_store.IsResident(CoordMath.VoxelToChunk(op.Dst)) ||
                        (op.HasSrc && !_store.IsResident(CoordMath.VoxelToChunk(op.Src))))
                    { OpsDroppedNonResident++; continue; }

                    if (_store.GetVoxel(op.Dst) != op.ExpectedAtDst) { StaleOpsDropped++; continue; }
                    if (op.HasSrc && _store.GetVoxel(op.Src) != op.NewMaterial)
                    { StaleOpsDropped++; continue; }

                    // THE single terrain write path (§8.3). ChunkStore.SetVoxel
                    // marks the chunk dirty and delta-dirty itself, so the
                    // clipmap upload (§3.7) and the save (§4.2) both follow.
                    _store.SetVoxel(op.Dst, op.NewMaterial);
                    OnVoxelApplied?.Invoke(op.Dst);
                    AppliedVoxelWrites++;
                    if (op.HasSrc)
                    {
                        _store.SetVoxel(op.Src, 0);        // vacated home -> Air
                        OnVoxelApplied?.Invoke(op.Src);
                        AppliedVoxelWrites++;
                    }
                    bool wake = op.Wake;

                    // §8.3's wake scan, run for the SAME reason an edit runs it:
                    // an applied op IS an edit as far as the neighbourhood is
                    // concerned (§7.6) -- but ONLY when the GPU flagged this op
                    // as descending. Waking on a lateral move re-promotes
                    // sleeping neighbours with a fresh budget and sustains the
                    // shuffle forever (PHASE_5A_COMPLETION.md §5.2).
                    if (wake)
                    {
                        _sim.RequestWakeNeighbourhood(op.Dst);
                        if (op.HasSrc) _sim.RequestWakeNeighbourhood(op.Src);
                    }
                }
            }

            return opCount;
        }

        /// Blocks until every outstanding readback has landed and been applied.
        /// For the acceptance rig only -- it needs a settled state to compare
        /// against the CPU oracle, and §7.8 compares steady states, not frames.
        /// Never call this from a shipped frame; it defeats the whole point of
        /// an ASYNC readback.
        public int DrainBlocking()
        {
            int applied = 0;
            while (_inFlight.Count > 0)
            {
                InFlight f = _inFlight.Peek();
                f.OpsReq.WaitForCompletion();
                _inFlight.Dequeue();
                if (!f.OpsReq.hasError)
                    applied += Apply(f);
                else ReadbackErrorsTotal++;
            }
            return applied;
        }

        public void Dispose()
        {
            foreach (var f in _inFlight) f.OpsReq.WaitForCompletion();
            _inFlight.Clear();

        }
    }
}
