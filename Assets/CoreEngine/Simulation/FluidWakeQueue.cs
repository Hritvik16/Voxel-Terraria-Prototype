// ==========================================
// Assets/CoreEngine/Simulation/FluidWakeQueue.cs
//
// Holds §8.3 wake requests until the GPU MIRROR CAN ACTUALLY SEE THE EDIT.
//
// =========================================================================
// THE BUG THIS EXISTS TO FIX (found by clicking, 2026-09-04)
// =========================================================================
// Placing any fluid by hand in the Playground froze it in mid-air. It was not
// a CA bug, a claim bug, or a rendering bug -- the CA never simulated that
// voxel at all, because its wake request was destroyed before the GPU knew the
// voxel existed:
//
//   Playground.Update()                       <- fluid ticks HERE
//     Edit(v, Water)
//       ChunkStore.SetVoxel(v, Water)         CPU state: water. Authoritative.
//       Clipmap.MarkDirty(chunk)              queued for upload -- NOT uploaded
//       EditService.NotifyEdited(v)           -> RequestWake(v)
//     FluidGpuSimulation.Tick(clipmap)
//       CSPromote reads SampleVoxel(v)        <- reads the GPU CLIPMAP: still Air
//       IsMobile(Air) == false                -> no slot allocated
//       _wakeCount = 0                        <- REQUEST DESTROYED, never retried
//
//   Phase4Bootstrapper.LateUpdate()           <- upload happens HERE, too late
//       Clipmap.UploadDirty(...)              the voxel finally reaches the GPU
//
// So the voxel arrives on the GPU one frame later, is drawn, and sits there
// forever with no slot and no pending request. Deterministic, 100% of manual
// placements. Vents died the same way: Emit() only refills a source cell when
// it reads Air, so one frozen voxel at the source stopped the vent too.
//
// Phase 5b's rig never caught this because Phase5bBasin.Tick() uploads FIRST
// and says so on the line ("GPU sees this frame's edits"). The rig had the
// ordering right and nothing enforced that a caller must.
//
// =========================================================================
// WHY THE FIX IS HERE AND NOT IN THE SCENE
// =========================================================================
// The scene-level fix is one UploadDirty call before Tick, matching the rig.
// Rejected for two reasons:
//   1. It spends §4.3's MAX_CLIPMAP_UPLOAD_BYTES_PER_FRAME twice in one frame
//      (once here, once in LateUpdate). §0.2 forbids raising that cap, and
//      calling the budgeted path twice raises it in effect.
//   2. It is still racy. UploadDirty is BUDGETED -- under a streaming backlog
//      the edited chunk can be deferred behind others (TerrainClipmap:323,
//      "Deferred chunks stay in _dirtyChunks and upload on a later frame"), so
//      the request would still be consumed against a stale mirror, just less
//      often. A fix that turns "always broken" into "intermittently broken" is
//      worse than no fix, because the next person sees a flake.
//
// This is not a redesign. §3.9's CPU/GPU sync contract already says the mirror
// lags the authoritative CPU state; §8.3 says an edit ends by waking slots.
// What was missing is that those two facts interact: the wake must not be
// evaluated until the mirror has caught up. That is what this queue enforces,
// and it is data-driven (it asks the clipmap) rather than a fixed frame delay,
// which a budgeted upload would defeat.
//
// COMPATIBILITY: when the mirror is already clean -- every rig, every existing
// test -- a request is released on the SAME tick it was made, so behaviour is
// unchanged. Nothing here alters what gets promoted, only when it is asked.

using System;
using System.Collections.Generic;

namespace VoxelEngine.Simulation
{
    /// A staging queue for wake requests, keyed by region cell index.
    /// Deliberately free of Unity types and of the GPU: the readiness test is
    /// injected, so this whole class is testable in EditMode with no device.
    public sealed class FluidWakeQueue
    {
        private readonly int _capacity;
        private readonly int _maxDeferTicks;
        private readonly List<int> _cells = new List<int>();
        private readonly List<int> _ages = new List<int>();
        private readonly HashSet<int> _member = new HashSet<int>();

        /// Requests still waiting for the mirror to catch up.
        public int PendingCount => _cells.Count;

        public long QueuedTotal { get; private set; }
        public long RejectedFullTotal { get; private set; }
        /// Coalesced because the same cell was already pending. Holding RMB
        /// re-edits one cell every frame; without this the queue would fill
        /// with duplicates and start rejecting real requests.
        public long CoalescedTotal { get; private set; }
        public long ReleasedReadyTotal { get; private set; }
        /// Released WITHOUT the mirror ever reporting clean, after
        /// maxDeferTicks. Best-effort: see Collect. A non-zero value here is a
        /// signal worth reading, not a normal condition.
        public long ReleasedStaleTotal { get; private set; }

        public FluidWakeQueue(int capacity, int maxDeferTicks)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            if (maxDeferTicks < 0) throw new ArgumentOutOfRangeException(nameof(maxDeferTicks));
            _capacity = capacity;
            _maxDeferTicks = maxDeferTicks;
        }

        /// Queues a region cell. Returns false only when full, which is the
        /// same "this cell does not move this tick" no-op RequestWake already
        /// documented -- never a lost byte, because the material stays in
        /// terrain either way and CSWakeScan can still find it later.
        public bool Add(int regionCell)
        {
            if (_member.Contains(regionCell)) { CoalescedTotal++; return true; }
            if (_cells.Count >= _capacity) { RejectedFullTotal++; return false; }
            _cells.Add(regionCell);
            _ages.Add(0);
            _member.Add(regionCell);
            QueuedTotal++;
            return true;
        }

        public void Clear()
        {
            _cells.Clear();
            _ages.Clear();
            _member.Clear();
        }

        /// Moves every request whose mirror is ready into <paramref name="dest"/>
        /// and returns how many were written. Requests that are not ready stay
        /// queued and age by one.
        ///
        /// STALE RELEASE: a request that has waited maxDeferTicks is released
        /// anyway. This is deliberate and it is the LESS bad of two options. If
        /// a chunk somehow never reports clean (evicted, outside the window,
        /// perpetually re-dirtied by the op readback), holding its request
        /// forever leaks the queue and silently stops waking that cell --
        /// exactly the failure being fixed. Releasing it costs at most one
        /// wasted promotion attempt, which CSPromote already handles as a
        /// guarded no-op (§7.7). The counter exists so this is visible rather
        /// than a quiet fallback.
        public int Collect(Func<int, bool> isMirrorReady, int[] dest, int destCapacity)
        {
            if (isMirrorReady == null) throw new ArgumentNullException(nameof(isMirrorReady));
            if (dest == null) throw new ArgumentNullException(nameof(dest));
            int n = 0;
            int keep = 0;

            for (int i = 0; i < _cells.Count; i++)
            {
                int cell = _cells[i];
                int age = _ages[i] + 1;
                bool ready = isMirrorReady(cell);
                bool stale = age > _maxDeferTicks;

                if ((ready || stale) && n < destCapacity && n < dest.Length)
                {
                    dest[n++] = cell;
                    if (ready) ReleasedReadyTotal++; else ReleasedStaleTotal++;
                    _member.Remove(cell);
                    continue;
                }

                // Not ready (or no room in dest this tick) -- keep it, aged.
                _cells[keep] = cell;
                _ages[keep] = age;
                keep++;
            }

            _cells.RemoveRange(keep, _cells.Count - keep);
            _ages.RemoveRange(keep, _ages.Count - keep);
            return n;
        }
    }
}
