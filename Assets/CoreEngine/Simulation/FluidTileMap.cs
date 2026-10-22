// ==========================================
// Assets/CoreEngine/Simulation/FluidTileMap.cs
//
// §7.2's ACTIVE SET, made sparse. The CPU half of DESIGN_NOTE_7_2.
//
// WHY THIS EXISTS. FluidGpuSimulation allocated ONE dense per-cell buffer set
// for its whole region -- 16 B/cell, measured. For §7.4's radius to gate
// anything the region must exceed the radius, and the shipped
// FLUID_ACTIVE_RADIUS_VOXELS = 1280 needs a 2048^3 region: 8.6e9 cells,
// 137 GB. So the radius was inert by construction at every size that can exist.
//
// The saving is NOT from tiling the radius -- a sphere of r=1280 is ~8.8e9
// voxels however it is sliced. It is from SPARSITY: only tiles that actually
// contain fluid are instantiated, so memory becomes O(fluid present) and stops
// being O(r^3) entirely. The radius then does its real job, bounding
// SIMULATION cost by gating which tiles stay resident.
//
// THE SHAPE IS DELIBERATELY THE ONE THIS PROJECT ALREADY PROVES: a toroidal
// directory over a bounded window (ChunkStore's ring) plus a hard-capped pool
// of fixed-size units (BrickDataPool), with units freed the instant they hold
// nothing -- §3.10's Volatile rule, immediate, never the lazy §4.5 coalescer.
//
// PURE CPU, NO GPU TYPES, ON PURPOSE. Everything here is testable without a
// device, which is the only reason the aliasing and lifetime rules below can be
// mutation-checked at all.

using System;
using Unity.Mathematics;

namespace VoxelEngine.Simulation
{
    public sealed class FluidTileMap
    {
        public const int NO_TILE = -1;

        /// Tile edge in voxels. MUST divide CHUNK_EDGE_VOXELS (128).
        ///
        /// THIS IS A CORRECTNESS PROPERTY, NOT A CONVENIENCE. Chunk boundaries
        /// sit at multiples of 128; a tile spanning [kT, (k+1)T) with T | 128
        /// therefore lies wholly inside ONE chunk, so residency is decidable per
        /// tile. The old fixed region straddled chunks by design, and that is
        /// exactly the shape that silently destroyed 7 water voxels of 52 in
        /// Phase 5d with StaleOpsDropped reading 0. This removes the CLASS of
        /// that bug; §9.4's per-op guard still handles the TIMING case, because
        /// an op is decided a frame or more before it is applied.
        public readonly int TileEdge;
        public readonly int TileShift;
        public readonly int TileCells;

        /// Ring size in TILES, per axis. Powers of two.
        ///
        /// §6.2: a non-power-of-two does not fail loudly, it ALIASES SILENTLY.
        /// That exact bug ("phantom terrain") has already happened once here.
        public readonly int3 RingDimsTiles;
        public readonly int TileCapacity;

        private readonly int[] _ringSlot;      // ring index -> slot, or NO_TILE
        private readonly int3[] _ringCoord;    // ring index -> the tile coord that owns the entry
        private readonly int3[] _slotCoord;    // slot -> its tile coord
        private readonly int[] _slotLive;      // slot -> live fluid cells (the §3.10 count)
        private readonly long[] _slotLastActive; // slot -> Clock of the last op/wake in it (AwakeOnly)
        private readonly int[] _freeSlots;
        private int _freeCount;

        private readonly int3 _ringMask;
        private readonly int _shiftY, _shiftZ;

        public int ResidentTiles { get; private set; }
        public int PeakResidentTiles { get; private set; }
        public long TilesAcquiredTotal { get; private set; }
        public long TilesReleasedTotal { get; private set; }

        /// Times a tile could not be created because the pool was full.
        /// A GUARDED NO-OP, never an invalid write: the fluid stays where it is
        /// and the terrain byte is untouched, exactly as §7.7 requires of
        /// promotion underflow. Counted because a silent one is how the
        /// scratch-pool leak hid for eight days.
        public int PoolExhaustionsTotal { get; private set; }

        public FluidTileMap(int tileEdge, int3 ringDimsTiles, int tileCapacity)
        {
            RequirePow2(tileEdge, nameof(tileEdge));
            RequirePow2(ringDimsTiles.x, "ringDimsTiles.x");
            RequirePow2(ringDimsTiles.y, "ringDimsTiles.y");
            RequirePow2(ringDimsTiles.z, "ringDimsTiles.z");
            if (tileCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(tileCapacity));
            if ((EngineConfig.CHUNK_EDGE_VOXELS % tileEdge) != 0)
                throw new ArgumentException(
                    $"tileEdge {tileEdge} must divide CHUNK_EDGE_VOXELS " +
                    $"{EngineConfig.CHUNK_EDGE_VOXELS}: a tile that straddles a chunk boundary " +
                    "reintroduces the Phase 5d residency-edge bug by construction.",
                    nameof(tileEdge));

            TileEdge = tileEdge;
            TileShift = Log2(tileEdge);
            TileCells = tileEdge * tileEdge * tileEdge;
            RingDimsTiles = ringDimsTiles;
            TileCapacity = tileCapacity;

            _ringMask = ringDimsTiles - 1;
            _shiftY = Log2(ringDimsTiles.x);
            _shiftZ = _shiftY + Log2(ringDimsTiles.y);

            int ringCount = ringDimsTiles.x * ringDimsTiles.y * ringDimsTiles.z;
            _ringSlot = new int[ringCount];
            _ringCoord = new int3[ringCount];
            for (int i = 0; i < ringCount; i++) _ringSlot[i] = NO_TILE;

            _slotCoord = new int3[tileCapacity];
            _slotLive = new int[tileCapacity];
            _slotLastActive = new long[tileCapacity];
            _freeSlots = new int[tileCapacity];
            for (int i = 0; i < tileCapacity; i++) _freeSlots[i] = tileCapacity - 1 - i;
            _freeCount = tileCapacity;
        }

        // =====================================================================
        // Addressing -- bitwise only (§0: no float division, no modulo)
        // =====================================================================

        public int3 TileOf(int3 voxel) => new int3(
            voxel.x >> TileShift, voxel.y >> TileShift, voxel.z >> TileShift);

        public int RingIndex(int3 tileCoord)
        {
            int3 w = tileCoord & _ringMask;
            return w.x | (w.y << _shiftY) | (w.z << _shiftZ);
        }

        /// The slot holding this tile, or NO_TILE.
        ///
        /// THE IDENTITY CHECK IS NOT OPTIONAL. A ring entry is only yours if it
        /// records your coordinate; without that, two tiles a whole ring apart
        /// answer to the same index and the second silently reads the first's
        /// cells. That is §6.2's aliasing bug, in a different buffer.
        public int TryGetSlot(int3 tileCoord)
        {
            int r = RingIndex(tileCoord);
            int slot = _ringSlot[r];
            if (slot == NO_TILE) return NO_TILE;
            return _ringCoord[r].Equals(tileCoord) ? slot : NO_TILE;
        }

        public int SlotForVoxel(int3 voxel) => TryGetSlot(TileOf(voxel));

        /// Linear cell index for a world voxel, or -1 if its tile is absent.
        /// Layout matches the GPU's exactly: slot-major, then z, y, x within.
        public int CellIndex(int3 voxel)
        {
            int slot = SlotForVoxel(voxel);
            if (slot == NO_TILE) return -1;
            int m = TileEdge - 1;
            int lx = voxel.x & m, ly = voxel.y & m, lz = voxel.z & m;
            int local = lx | (ly << TileShift) | (lz << (TileShift + TileShift));
            return slot * TileCells + local;
        }

        /// Cell index -> world voxel. The inverse of CellIndex, and the CPU
        /// mirror of the shader's RegionVoxel.
        ///
        /// THE TWO DIRECTIONS MUST AGREE EXACTLY. A wake request is produced on
        /// the CPU and consumed on the GPU; if the mappings disagree by even one
        /// axis, the request names a different cell than the one that asked for
        /// it -- silently, because both indices are valid.
        public int3 VoxelOfCell(int cellIndex)
        {
            int slot = cellIndex / TileCells;
            int local = cellIndex - slot * TileCells;
            int m = TileEdge - 1;
            int3 inTile = new int3(local & m, (local >> TileShift) & m,
                                   local >> (TileShift + TileShift));
            return (_slotCoord[slot] << TileShift) + inTile;
        }

        // =====================================================================
        // Lifetime
        // =====================================================================

        /// Creates the tile if absent, returning its slot, or NO_TILE when the
        /// pool is full. Idempotent for an existing tile.
        public int Acquire(int3 tileCoord)
        {
            int existing = TryGetSlot(tileCoord);
            if (existing != NO_TILE) return existing;

            int r = RingIndex(tileCoord);
            // A stale entry from a tile that is no longer resident: the slide
            // and the release both clear, so reaching here with an occupied
            // entry means a DIFFERENT live tile aliases this index -- which the
            // radius is supposed to make impossible (see AssertNoAliasing).
            if (_ringSlot[r] != NO_TILE) return NO_TILE;

            if (_freeCount == 0) { PoolExhaustionsTotal++; return NO_TILE; }

            int slot = _freeSlots[--_freeCount];
            _slotCoord[slot] = tileCoord;
            _slotLive[slot] = 0;
            _slotLastActive[slot] = Clock;
            if (_unsettled.Remove(tileCoord)) { UnsettledReadmittedTotal++; SeedRequested = true; }
            _ringSlot[r] = slot;
            _ringCoord[r] = tileCoord;
            ResidentTiles++;
            TilesAcquiredTotal++;
            if (ResidentTiles > PeakResidentTiles) PeakResidentTiles = ResidentTiles;
            return slot;
        }

        public bool Release(int3 tileCoord)
        {
            int r = RingIndex(tileCoord);
            int slot = _ringSlot[r];
            if (slot == NO_TILE || !_ringCoord[r].Equals(tileCoord)) return false;

            if (AwakeOnly && !IsQuiet(slot)) RememberUnsettled(tileCoord);
            _ringSlot[r] = NO_TILE;
            _ringCoord[r] = default;
            _slotLive[slot] = 0;
            _freeSlots[_freeCount++] = slot;
            ResidentTiles--;
            TilesReleasedTotal++;
            return true;
        }

        // =====================================================================
        // AWAKE-ONLY RESIDENCY (task 54, engine plan step 5; BUGS #3)
        // =====================================================================
        //
        // WHY. Under the mask policy (FluidTileResidency's scan) EVERY tile that
        // holds a fluid-capable voxel inside the wake radius is resident. At a
        // tg2 coast that is ~4,700 tiles of resting sea and beach sand against a
        // 1,024-tile pool: the pool is full of fluid that never moves, and the
        // tiles the player disturbs lose. §7.4 says dormant fluid is "zero GPU
        // slots, zero sim cost"; DESIGN_NOTE_7_2 §4.4 says a tile is freed when
        // its live count reaches zero. AwakeOnly makes the tile pool follow that:
        //
        //   ADMIT   only from a wake (an edit, an applied op's neighbourhood, a
        //           flow reaching a tile face) or from the unsettled memory below
        //           -- never because generated water or sand is merely present.
        //   RELEASE a tile QUIET for QuietTicks: no op and no wake touched it, so
        //           every slot in it has slept (CSSweep frees a slot after
        //           FLUID_SLEEP_TICKS evaluated ticks without a descent; a
        //           viscous one is evaluated every TickInterval ticks, hence the
        //           product). No GPU readback is needed to know it is empty.
        //   REMEMBER a tile released while NOT quiet (radius, orphan, displaced):
        //           it may hold fluid that stopped mid-flow (force-demote beyond
        //           the sleep radius). DESIGN_NOTE_7_2 §9's "a sparse set does
        //           not know where dormant fluid is" -- here it only has to know
        //           where DISTURBED fluid was left, which is this set. Refresh
        //           re-admits it inside the wake radius and Acquire asks the sim
        //           for its recentre seed, which wakes the cells with air below.
        //
        // BOOKKEEPING ONLY: no struct layout, no shader, no tick-rate change.

        /// The policy switch. False = the mask policy every proven rig uses.
        public bool AwakeOnly { get; set; }

        /// Ticks without an op or wake before a resident tile counts as settled.
        /// Set from FluidGpuSimulation.QuietTicksFor (sleep ticks x the slowest
        /// material's interval, plus the readback lag).
        public int QuietTicks { get; set; } = 64;

        /// The CA tick count, advanced by FluidGpuSimulation.Tick.
        public long Clock { get; set; }

        /// Set when a remembered-unsettled tile is re-acquired; the sim consumes
        /// it to run its recentre seed over the (small) awake set.
        public bool SeedRequested { get; set; }

        /// Bound on the unsettled memory, so a long session cannot grow it
        /// without limit. 12 B per entry; beyond this a tile is forgotten (its
        /// fluid stays terrain, as it would after a save).
        public const int UNSETTLED_CAP = 65536;
        private readonly System.Collections.Generic.HashSet<int3> _unsettled =
            new System.Collections.Generic.HashSet<int3>();
        public int UnsettledCount => _unsettled.Count;
        public long UnsettledRememberedTotal { get; private set; }
        public long UnsettledReadmittedTotal { get; private set; }
        public long UnsettledDroppedTotal { get; private set; }

        /// Snapshot of the remembered tiles (callers acquire while iterating).
        public void CopyUnsettled(System.Collections.Generic.List<int3> into)
        {
            into.Clear();
            foreach (int3 c in _unsettled) into.Add(c);
        }

        public void ForgetUnsettled(int3 tileCoord) => _unsettled.Remove(tileCoord);

        private void RememberUnsettled(int3 tileCoord)
        {
            if (_unsettled.Contains(tileCoord)) return;
            if (_unsettled.Count >= UNSETTLED_CAP) { UnsettledDroppedTotal++; return; }
            _unsettled.Add(tileCoord);
            UnsettledRememberedTotal++;
        }

        /// Stamps the tile holding `voxel` as active now, if it is resident.
        public void NoteActivity(int3 voxel)
        {
            int slot = SlotForVoxel(voxel);
            if (slot != NO_TILE) _slotLastActive[slot] = Clock;
        }

        public bool IsQuiet(int slot) => Clock - _slotLastActive[slot] >= QuietTicks;

        /// Releases every resident tile that has been quiet for QuietTicks.
        /// Quiet tiles are settled, so they are NOT remembered. O(TileCapacity)
        /// over the slots, not O(ring) (the ring is 2M entries).
        public int ReleaseQuietTiles()
        {
            int freed = 0;
            for (int s = 0; s < TileCapacity; s++)
            {
                int3 c = _slotCoord[s];
                if (TryGetSlot(c) != s || !IsQuiet(s)) continue;   // a free slot keeps its old coord
                if (Release(c)) freed++;
            }
            return freed;
        }

        // =====================================================================
        // §3.10's Volatile rule, applied to tiles
        // =====================================================================

        public int LiveCells(int slot) => _slotLive[slot];
        public int3 CoordOfSlot(int slot) => _slotCoord[slot];

        public void SetLiveCells(int slot, int live) => _slotLive[slot] = live;

        /// Frees every tile holding no live fluid, IMMEDIATELY.
        ///
        /// §3.10 verbatim: "when its active-fluid count drops to zero, its body
        /// is returned to the free-list immediately -- not on the background
        /// sweep." The eviction spiral that rule prevents is precisely what a
        /// lazily-freed tile pool would reintroduce: a waterfall through open air
        /// would strand hundreds of empty tiles and push real fluid out of the
        /// pool to house air the water already left.
        public int ReleaseEmptyTiles()
        {
            int freed = 0;
            for (int r = 0; r < _ringSlot.Length; r++)
            {
                int slot = _ringSlot[r];
                if (slot == NO_TILE || _slotLive[slot] != 0) continue;
                if (Release(_ringCoord[r])) freed++;
            }
            return freed;
        }

        /// Frees every resident tile whose centre is beyond `sleepRadiusVoxels`
        /// of `centreVoxel` -- §7.4's departure half, at tile granularity.
        public int ReleaseBeyond(int3 centreVoxel, int sleepRadiusVoxels)
        {
            int freed = 0;
            int half = TileEdge >> 1;
            for (int r = 0; r < _ringSlot.Length; r++)
            {
                int slot = _ringSlot[r];
                if (slot == NO_TILE) continue;
                int3 c = (_ringCoord[r] << TileShift) + new int3(half, half, half);
                if (FluidActiveRegion.BeyondSleepRadius(c, centreVoxel, sleepRadiusVoxels)
                    && Release(_ringCoord[r])) freed++;
            }
            return freed;
        }

        /// Squared distance from `centreVoxel` to the NEAREST voxel of a tile --
        /// the metric FluidTileResidency admits by. Integer (§0).
        public long TileDistSq(int3 tileCoord, int3 centreVoxel)
        {
            int3 lo = tileCoord << TileShift, hi = lo + (TileEdge - 1);
            int3 d = math.max(math.max(lo - centreVoxel, centreVoxel - hi), 0);
            return (long)d.x * d.x + (long)d.y * d.y + (long)d.z * d.z;
        }

        /// The resident tile farthest from `centreVoxel` (TileDistSq), or false
        /// if none is resident. O(TileCapacity) over the slots, not O(ring).
        public bool TryFarthestResident(int3 centreVoxel, out int3 coord, out long distSq)
        {
            coord = default; distSq = -1;
            for (int s = 0; s < TileCapacity; s++)
            {
                int3 c = _slotCoord[s];
                if (TryGetSlot(c) != s) continue;            // a free slot keeps its old coord
                long d2 = TileDistSq(c, centreVoxel);
                if (d2 > distSq) { distSq = d2; coord = c; }
            }
            return distSq >= 0;
        }

        /// The coords of every resident tile, snapshotted.
        ///
        /// SNAPSHOTTED ON PURPOSE: callers release while iterating, and Release
        /// mutates the ring the enumeration would be walking.
        public System.Collections.Generic.List<int3> ResidentTileCoords()
        {
            var list = new System.Collections.Generic.List<int3>(ResidentTiles);
            for (int r = 0; r < _ringSlot.Length; r++)
                if (_ringSlot[r] != NO_TILE) list.Add(_ringCoord[r]);
            return list;
        }

        /// Would two tiles inside the wake radius collide on one ring entry?
        ///
        /// The ring is only safe while the resident set spans fewer tiles than
        /// the ring does. That holds by construction when the ring covers the
        /// radius, and this makes the invariant checkable instead of assumed --
        /// the §6.2 bug was exactly an aliasing assumption nobody could test.
        public bool RadiusFitsRing(int wakeRadiusVoxels)
        {
            int spanTiles = ((2 * wakeRadiusVoxels) >> TileShift) + 2;
            return spanTiles <= math.cmin(RingDimsTiles);
        }

        /// Bytes the GPU must allocate for this configuration: the directory
        /// plus the per-cell buffers for the whole tile pool. The number the
        /// design exists to bound, computed from the actual configuration
        /// rather than estimated.
        public long GpuBytes(int bytesPerCell)
        {
            long ring = (long)RingDimsTiles.x * RingDimsTiles.y * RingDimsTiles.z * sizeof(int);
            long cells = (long)TileCapacity * TileCells * bytesPerCell;
            return ring + cells;
        }

        // =====================================================================

        private static void RequirePow2(int v, string name)
        {
            if (v <= 0 || (v & (v - 1)) != 0)
                throw new ArgumentException(
                    $"{name} must be a power of two (got {v}). §6.2: a non-power-of-two does " +
                    "not fail loudly, it aliases silently.", name);
        }

        private static int Log2(int v)
        {
            int n = 0;
            while ((1 << n) < v) n++;
            return n;
        }
    }
}
