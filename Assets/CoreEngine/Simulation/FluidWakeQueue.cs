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
using Unity.Mathematics;
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
        private readonly List<int3> _cells = new List<int3>();
        private readonly List<int> _ages = new List<int>();
        /// The mirror's upload epoch AT THE MOMENT THE EDIT HAPPENED. Readiness
        /// is "the chunk has been uploaded since then", which a plain
        /// is-it-dirty test cannot express -- see the header note on why.
        private readonly List<long> _stamps = new List<long>();
        private readonly HashSet<int3> _member = new HashSet<int3>();

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
        /// <param name="stamp">The mirror's upload epoch when the edit was
        /// made. The request is released once the chunk has been uploaded at a
        /// STRICTLY LATER epoch, because such an upload necessarily contains
        /// the edit.</param>
        /// KEYED ON THE WORLD VOXEL, NOT A CELL INDEX.
        ///
        /// Under §7.2's tiled active set a cell index is slot * cellsPerTile +
        /// offset, and a tile's SLOT is not stable: a request may wait up to
        /// maxDeferTicks, and the tile can be released and its slot reissued to
        /// a different tile in that window. A stored cell index would then
        /// address another tile's cells -- silently, which is the §6.2 aliasing
        /// failure in a third buffer. The world voxel is stable by definition,
        /// so the conversion happens at dispatch, against the tile set that is
        /// actually current.
        public bool Add(int3 regionCell, long stamp)
        {
            if (_member.Contains(regionCell))
            {
                // Keep the OLDEST stamp: a later edit to the same cell cannot
                // make an earlier one's wake less urgent, and taking the newer
                // stamp would let a cell edited every frame push its own
                // release forever -- which is precisely the starvation this
                // stamp scheme replaced.
                CoalescedTotal++;
                return true;
            }
            if (_cells.Count >= _capacity) { RejectedFullTotal++; return false; }
            _cells.Add(regionCell);
            _ages.Add(0);
            _stamps.Add(stamp);
            _member.Add(regionCell);
            QueuedTotal++;
            return true;
        }

        public void Clear()
        {
            _cells.Clear();
            _ages.Clear();
            _stamps.Clear();
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
        public int Collect(Func<int3, long, bool> isMirrorReady, int3[] dest, int destCapacity)
        {
            if (isMirrorReady == null) throw new ArgumentNullException(nameof(isMirrorReady));
            if (dest == null) throw new ArgumentNullException(nameof(dest));
            int n = 0;
            int keep = 0;

            for (int i = 0; i < _cells.Count; i++)
            {
                int3 cell = _cells[i];
                int age = _ages[i] + 1;
                long stamp = _stamps[i];
                bool ready = isMirrorReady(cell, stamp);
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
                _stamps[keep] = stamp;
                keep++;
            }

            _cells.RemoveRange(keep, _cells.Count - keep);
            _ages.RemoveRange(keep, _ages.Count - keep);
            _stamps.RemoveRange(keep, _stamps.Count - keep);
            return n;
        }
    }
}
