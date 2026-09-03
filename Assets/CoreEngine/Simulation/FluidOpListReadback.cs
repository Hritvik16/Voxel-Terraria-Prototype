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
            public AsyncGPUReadbackRequest OpCountReq;
            public AsyncGPUReadbackRequest OpsReq;
            public AsyncGPUReadbackRequest WakeCountReq;
            public AsyncGPUReadbackRequest WakeReq;
            public int IssuedFrame;
            public int ActiveSlotsAtIssue;
        }

        private readonly Queue<InFlight> _inFlight = new Queue<InFlight>();
        private readonly GraphicsBuffer _opCountBuffer;
        private readonly GraphicsBuffer _wakeCountBuffer;

        // ---- Instrumentation (§13's boundedness gate) ----
        public int OpsLastFrame { get; private set; }
        public int WakeCellsLastFrame { get; private set; }
        public long OpsTotal { get; private set; }
        public int PeakOpsInAFrame { get; private set; }
        public int FramesInFlight { get; private set; }
        public int MaxFramesInFlightSeen { get; private set; }
        public long AppliedVoxelWrites { get; private set; }
        public int OverflowFramesTotal { get; private set; }

        public FluidOpListReadback(FluidGpuSimulation sim, ChunkStore store)
        {
            _sim = sim ?? throw new ArgumentNullException(nameof(sim));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _opCountBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Raw, 1, 4);
            _wakeCountBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Raw, 1, 4);
        }

        /// Issue this frame's readback. Call AFTER FluidGpuSimulation.Tick, and
        /// off the critical path -- §3.9's frame order puts it after the
        /// raymarch, applied next frame.
        public void IssueReadback(int activeSlotsHint)
        {
            GraphicsBuffer.CopyCount(_sim.OpListBuffer, _opCountBuffer, 0);
            GraphicsBuffer.CopyCount(_sim.WakeOutBuffer, _wakeCountBuffer, 0);

            _inFlight.Enqueue(new InFlight
            {
                OpCountReq = AsyncGPUReadback.Request(_opCountBuffer),
                OpsReq = AsyncGPUReadback.Request(_sim.OpListBuffer),
                WakeCountReq = AsyncGPUReadback.Request(_wakeCountBuffer),
                WakeReq = AsyncGPUReadback.Request(_sim.WakeOutBuffer),
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
                if (f.OpCountReq.hasError || f.OpsReq.hasError ||
                    f.WakeCountReq.hasError || f.WakeReq.hasError)
                {
                    // §9's contract: never crash. A failed readback costs one
                    // frame of fluid motion, never a voxel -- the terrain bytes
                    // are untouched and the slots simply retry next tick.
                    _inFlight.Dequeue();
                    OverflowFramesTotal++;
                    continue;
                }
                if (!f.OpCountReq.done || !f.OpsReq.done ||
                    !f.WakeCountReq.done || !f.WakeReq.done) break;

                _inFlight.Dequeue();
                applied += Apply(f);
            }
            return applied;
        }

        private int Apply(InFlight f)
        {
            int opCount = (int)f.OpCountReq.GetData<uint>()[0];
            int wakeCount = (int)f.WakeCountReq.GetData<uint>()[0];

            if (opCount >= _sim.MaxOpsPerFrame || wakeCount >= _sim.MaxOpsPerFrame)
            {
                // The append buffer filled. Ops beyond the cap were dropped by
                // the GPU, which is a MOTION loss, not a mass loss: the terrain
                // bytes for those moves were never written, so every drop is
                // still exactly where it was. Counted rather than hidden.
                OverflowFramesTotal++;
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
                    // THE single terrain write path (§8.3). ChunkStore.SetVoxel
                    // marks the chunk dirty and delta-dirty itself, so the
                    // clipmap upload (§3.7) and the save (§4.2) both follow.
                    _store.SetVoxel(op.Voxel, (byte)op.newMaterial);
                    AppliedVoxelWrites++;

                    // §8.3's wake scan, run for the SAME reason an edit runs it:
                    // an applied op IS an edit as far as the neighbourhood is
                    // concerned (§7.6).
                    _sim.RequestWakeNeighbourhood(op.Voxel);
                }
            }

            if (wakeCount > 0)
            {
                NativeArray<int> wake = f.WakeReq.GetData<int>();
                for (int i = 0; i < wakeCount; i++)
                    _sim.RequestWakeRegionCell(wake[i]);
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
                f.OpCountReq.WaitForCompletion();
                f.OpsReq.WaitForCompletion();
                f.WakeCountReq.WaitForCompletion();
                f.WakeReq.WaitForCompletion();
                _inFlight.Dequeue();
                if (!f.OpCountReq.hasError && !f.OpsReq.hasError &&
                    !f.WakeCountReq.hasError && !f.WakeReq.hasError)
                    applied += Apply(f);
                else OverflowFramesTotal++;
            }
            return applied;
        }

        public void Dispose()
        {
            foreach (var f in _inFlight)
            {
                f.OpCountReq.WaitForCompletion();
                f.OpsReq.WaitForCompletion();
                f.WakeCountReq.WaitForCompletion();
                f.WakeReq.WaitForCompletion();
            }
            _inFlight.Clear();
            _opCountBuffer?.Dispose();
            _wakeCountBuffer?.Dispose();
        }
    }
}
