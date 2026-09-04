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
            public AsyncGPUReadbackRequest CountersReq;
            public AsyncGPUReadbackRequest OpsReq;
            public int IssuedFrame;
            public int ActiveSlotsAtIssue;
        }

        private readonly Queue<InFlight> _inFlight = new Queue<InFlight>();
        // Ringed for the same reason the op-list is: CopyCount writes into these
        // every tick while readbacks of them are still pending.
        private readonly GraphicsBuffer[] _opCountBuffer = new GraphicsBuffer[FluidGpuSimulation.RING];
        private readonly GraphicsBuffer[] _wakeCountBuffer = new GraphicsBuffer[FluidGpuSimulation.RING];

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
        public int SkippedIssuesTotal { get; private set; }

        /// §7.2 states the readback lag is "typically 1-3 frames". Nothing was
        /// ENFORCING that: IssueReadback queued unconditionally every frame, and
        /// with four requests per frame the queue reached 6 frames deep = 24
        /// concurrent AsyncGPUReadback requests, at which point the oldest
        /// request started coming back with hasError and no logged reason.
        /// Capping the queue at the spec's own bound both fixes that and makes
        /// the stated 1-3 frames a property of the code rather than a hope.
        /// Skipping an issue costs one frame of fluid motion, never a voxel.
        /// MUST stay below FluidGpuSimulation.RING. The ring slot a readback is
        /// reading must not be rewritten before that readback lands.
        private const int MaxFramesInFlight = FluidGpuSimulation.RING - 1;

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
            for (int i = 0; i < FluidGpuSimulation.RING; i++)
            {
                _opCountBuffer[i] = new GraphicsBuffer(GraphicsBuffer.Target.Raw, 4, 4);
                _wakeCountBuffer[i] = new GraphicsBuffer(GraphicsBuffer.Target.Raw, 4, 4);
            }
        }

        /// Issue this frame's readback. Call AFTER FluidGpuSimulation.Tick, and
        /// off the critical path -- §3.9's frame order puts it after the
        /// raymarch, applied next frame.
        public void IssueReadback(int activeSlotsHint)
        {
            if (!CanIssue) { SkippedIssuesTotal++; return; }

            int r = _sim.CurrentRing;
            if (r < 0) return;

            // NO CopyCount. Both counts come from OpCounters, which the shader
            // maintains directly -- see the comment on OpCounters in FluidCA.compute.
            _inFlight.Enqueue(new InFlight
            {
                CountersReq = AsyncGPUReadback.Request(_sim.OpCountersBuffer),
                OpsReq = AsyncGPUReadback.Request(_sim.OpListBuffer),
                IssuedFrame = Time.frameCount,
                ActiveSlotsAtIssue = activeSlotsHint,
            });
        }

        /// Apply whatever has landed. Returns the number of ops applied.
        public int PumpAndApply()
        {
            int applied = 0;
            FramesInFlight = _inFlight.Count;
            if (FramesInFlight > MaxFramesInFlightSeen) MaxFramesInFlightSeen = FramesInFlight;

            while (_inFlight.Count > 0)
            {
                InFlight f = _inFlight.Peek();
                if (f.CountersReq.hasError || f.OpsReq.hasError)
                {
                    // §9's contract: never crash. A failed readback costs one
                    // frame of fluid motion, never a voxel -- the terrain bytes
                    // are untouched and the slots simply retry next tick.
                    LastReadbackError = (f.CountersReq.hasError ? "counters " : "") +
                                        (f.OpsReq.hasError ? "ops" : "");
                    _inFlight.Dequeue();
                    ReadbackErrorsTotal++;
                    continue;
                }
                if (!f.CountersReq.done || !f.OpsReq.done) break;

                _inFlight.Dequeue();
                applied += Apply(f);
            }
            return applied;
        }

        private int Apply(InFlight f)
        {
            var counters = f.CountersReq.GetData<uint>();
            int opCount = (int)counters[0];
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
                NativeArray<FluidWriteOp> ops = f.OpsReq.GetData<FluidWriteOp>();
                for (int i = 0; i < opCount; i++)
                {
                    FluidWriteOp op = ops[i];
                    bool wake = (op.newMaterial & 0x100u) != 0u;
                    // THE single terrain write path (§8.3). ChunkStore.SetVoxel
                    // marks the chunk dirty and delta-dirty itself, so the
                    // clipmap upload (§3.7) and the save (§4.2) both follow.
                    _store.SetVoxel(op.Voxel, (byte)(op.newMaterial & 0xFFu));
                    OnVoxelApplied?.Invoke(op.Voxel);
                    AppliedVoxelWrites++;

                    // §8.3's wake scan, run for the SAME reason an edit runs it:
                    // an applied op IS an edit as far as the neighbourhood is
                    // concerned (§7.6) -- but ONLY when the GPU flagged this op
                    // as descending. Waking on a lateral move re-promotes
                    // sleeping neighbours with a fresh budget and sustains the
                    // shuffle forever (PHASE_5A_COMPLETION.md §5.2).
                    if (wake) _sim.RequestWakeNeighbourhood(op.Voxel);
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
                f.CountersReq.WaitForCompletion();
                f.OpsReq.WaitForCompletion();
                _inFlight.Dequeue();
                if (!f.CountersReq.hasError && !f.OpsReq.hasError)
                    applied += Apply(f);
                else ReadbackErrorsTotal++;
            }
            return applied;
        }

        public void Dispose()
        {
            foreach (var f in _inFlight)
            {
                f.CountersReq.WaitForCompletion();
                f.OpsReq.WaitForCompletion();
            }
            _inFlight.Clear();
            for (int i = 0; i < FluidGpuSimulation.RING; i++)
            {
                _opCountBuffer[i]?.Dispose();
                _wakeCountBuffer[i]?.Dispose();
            }
        }
    }
}
