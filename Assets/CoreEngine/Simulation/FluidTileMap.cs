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

            _ringSlot[r] = NO_TILE;
            _ringCoord[r] = default;
            _slotLive[slot] = 0;
            _freeSlots[_freeCount++] = slot;
            ResidentTiles--;
            TilesReleasedTotal++;
            return true;
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
