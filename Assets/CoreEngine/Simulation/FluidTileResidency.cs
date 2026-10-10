// ==========================================
// Assets/CoreEngine/Simulation/FluidTileResidency.cs
//
// §7.4's "on approach it wakes", for a SPARSE active set.
//
// This is the join between the two halves built separately: ChunkFluidMask says
// WHERE dormant fluid is, FluidTileMap owns the pool of live tiles, and this
// decides which tiles should exist right now.
//
// THE COST ARGUMENT, WHICH IS THE WHOLE POINT. The dense region answered "what
// should be simulating" by dispatching over every cell in the radius every
// tick -- O(r^3), which at the shipped 1280-voxel radius is 8.8e9 cells. Here
// the same question is answered by walking the chunks the radius touches and
// reading ONE ULONG each: ~441 chunks at that radius on this world's single
// generated layer. Everything else follows from 64 bits per chunk.
//
// THREE GATES, IN THIS ORDER, AND THE ORDER MATTERS:
//   1. RESIDENCY (§9.4). A tile whose chunk is not resident is never admitted.
//      Fluid cannot be simulated into unloaded world, and GetVoxel returns Air
//      for a non-resident chunk -- "deliberately ambiguous with real air" by its
//      own contract -- so admitting such a tile would simulate against a lie.
//   2. THE MASK. No fluid-capable material, no tile. This is the sparsity.
//   3. THE RADIUS (§7.4), with hysteresis: wake radius to admit, the larger
//      sleep radius to release, so a player at the boundary cannot thrash.
//
// CONSERVATIVE AT TILE BOUNDARIES, DELIBERATELY. Admission tests the tile's
// NEAREST point against the wake radius and release tests its FARTHEST point
// against the sleep radius. A tile is 32 voxels, so testing its centre would
// mis-decide by up to ~16 voxels at the edge -- and both errors are pushed the
// same way: admit slightly early, release slightly late. Over-admitting costs
// one acquire-and-free (§3.10); under-admitting is fluid that never wakes.

using Unity.Mathematics;
using VoxelEngine.Memory;

namespace VoxelEngine.Simulation
{
    public static class FluidTileResidency
    {
        public struct Stats
        {
            public int ChunksScanned;
            public int ChunksSkippedNonResident;
            public int TilesWithFluid;
            public int TilesAcquired;
            public int TilesAlreadyResident;
            public int TilesRefusedPoolFull;
            public int TilesReleasedByRadius;
            /// Tiles released because their backing chunk stopped being
            /// resident. See ReleaseOrphaned.
            public int TilesReleasedOrphaned;
            /// Resident tiles released to make room for a NEARER wanted tile
            /// while the pool was full. See the priority note in Refresh.
            public int TilesDisplaced;
        }

        // Scratch for the full-pool path. Main thread only (the single-writer
        // rule: Refresh mutates the tile pool, so it never runs on a worker).
        private static readonly System.Collections.Generic.List<long> s_wantedD2 = new System.Collections.Generic.List<long>();
        private static readonly System.Collections.Generic.List<int3> s_wanted = new System.Collections.Generic.List<int3>();
        private static readonly System.Collections.Generic.List<long> s_residentD2 = new System.Collections.Generic.List<long>();
        private static readonly System.Collections.Generic.List<int3> s_resident = new System.Collections.Generic.List<int3>();
        private static int[] s_order = new int[0];

        /// Squared distance from `p` to the closest point of the inclusive box.
        /// Integer throughout (§0): no float distance, no sqrt.
        private static long DistSqToBox(int3 p, int3 lo, int3 hi)
        {
            long dx = p.x < lo.x ? lo.x - p.x : (p.x > hi.x ? p.x - hi.x : 0);
            long dy = p.y < lo.y ? lo.y - p.y : (p.y > hi.y ? p.y - hi.y : 0);
            long dz = p.z < lo.z ? lo.z - p.z : (p.z > hi.z ? p.z - hi.z : 0);
            return dx * dx + dy * dy + dz * dz;
        }

        /// Squared distance from `p` to the FARTHEST corner of the box.
        private static long DistSqToFarthest(int3 p, int3 lo, int3 hi)
        {
            long dx = math.max(math.abs((long)p.x - lo.x), math.abs((long)p.x - hi.x));
            long dy = math.max(math.abs((long)p.y - lo.y), math.abs((long)p.y - hi.y));
            long dz = math.max(math.abs((long)p.z - lo.z), math.abs((long)p.z - hi.z));
            return dx * dx + dy * dy + dz * dz;
        }

        /// Brings the tile set into line with where the player is and where fluid
        /// actually is. Call once per re-centre, not per tick.
        /// <param name="releaseOrphaned">
        /// A MEASUREMENT SEAM, NOT A CONFIGURATION. Passing false restores the
        /// pre-fix behaviour so an A/B can attribute the op-discard change to
        /// the orphan sweep and nothing else. Only a rig should ever pass
        /// false; shipped callers take the default.
        /// </param>
        public static Stats Refresh(ChunkStore store, FluidTileMap tiles,
                                    int3 centreVoxel, int wakeRadiusVoxels, int sleepRadiusVoxels,
                                    bool releaseOrphaned = true)
        {
            var st = new Stats();
            if (store == null || tiles == null) return st;

            // RELEASE FIRST, so a pool that is full of tiles the player has left
            // can still admit the ones they have arrived at. Doing it the other
            // way round makes departure and arrival race for capacity, and the
            // loser is silently a tile that does not wake.
            st.TilesReleasedByRadius = ReleaseBeyondSleep(tiles, centreVoxel, sleepRadiusVoxels);
            if (releaseOrphaned) st.TilesReleasedOrphaned = ReleaseOrphaned(store, tiles);

            int3 lo = CoordMath.VoxelToChunk(centreVoxel - wakeRadiusVoxels);
            int3 hi = CoordMath.VoxelToChunk(centreVoxel + wakeRadiusVoxels);
            long wake2 = (long)wakeRadiusVoxels * wakeRadiusVoxels;
            s_wanted.Clear(); s_wantedD2.Clear();

            for (int cz = lo.z; cz <= hi.z; cz++)
            for (int cy = lo.y; cy <= hi.y; cy++)
            for (int cx = lo.x; cx <= hi.x; cx++)
            {
                int3 cc = new int3(cx, cy, cz);

                // GATE 1 -- §9.4. Not resident, not simulated. GetChunk returns
                // null rather than throwing, and null here is the same answer.
                Chunk chunk = store.GetChunk(cc);
                if (chunk == null) { st.ChunksSkippedNonResident++; continue; }
                st.ChunksScanned++;

                // GATE 2 -- the mask. THE ENTIRE SCAN OF A CHUNK IS THIS READ.
                ulong mask = chunk.fluidTileMask;
                if (mask == 0UL) continue;

                while (mask != 0UL)
                {
                    int bit = math.tzcnt(mask);
                    mask &= mask - 1UL;
                    st.TilesWithFluid++;

                    int3 tileInChunk = new int3(bit & 3, (bit >> 2) & 3, (bit >> 4) & 3);
                    ChunkFluidMask.TileBounds(cc, tileInChunk, out int3 tLo, out int3 tHi);

                    // GATE 3 -- §7.4, nearest point, so a tile straddling the
                    // boundary is admitted rather than missed.
                    long d2 = DistSqToBox(centreVoxel, tLo, tHi);
                    if (d2 > wake2) continue;

                    int3 abs = ChunkFluidMask.AbsoluteTile(cc, tileInChunk);
                    if (tiles.TryGetSlot(abs) != FluidTileMap.NO_TILE)
                    { st.TilesAlreadyResident++; continue; }

                    s_wanted.Add(abs); s_wantedD2.Add(d2);
                }
            }

            AdmitNearestFirst(tiles, centreVoxel, ref st);
            return st;
        }

        /// Acquires this pass's wanted tiles. When they do not all fit, the
        /// NEAREST win: wanted tiles go in nearest-first, and a wanted tile
        /// displaces the farthest resident tile if that one is strictly
        /// farther from the player.
        ///
        /// WHY (task 39, 2026-10-08). The scan above visits chunks in z,y,x
        /// order from the radius box's low corner, and admission used to follow
        /// it. In tg2 the radius holds far more water/sand tiles than the pool
        /// (a sea or a lake inside 128 m), so the pool filled with whatever the
        /// scan met first -- typically far tiles on the -z side -- and stayed
        /// full under hysteresis, while the tiles at the player's feet, and
        /// fluid the player had just placed, were refused every pass.
        ///
        /// STABLE: displacement needs a STRICTLY farther victim, so with the
        /// player still a refused tile never evicts its equal and the next pass
        /// changes nothing. A displaced tile is released exactly as the radius
        /// release does (§7.7: the terrain byte is authoritative; it re-acquires
        /// when it is near enough again).
        /// A MEASUREMENT SEAM, NOT A CONFIGURATION (like Refresh's
        /// releaseOrphaned): true restores the pre-task-39 scan-order admission
        /// so -poolprobe can show both in one build. Shipped code leaves it false.
        public static bool LegacyScanOrder;

        private static void AdmitNearestFirst(FluidTileMap tiles, int3 centreVoxel, ref Stats st)
        {
            int n = s_wanted.Count;
            if (n == 0) return;
            int free = tiles.TileCapacity - tiles.ResidentTiles;
            if (n <= free || LegacyScanOrder)
            {
                for (int i = 0; i < n; i++)
                {
                    if (tiles.Acquire(s_wanted[i]) == FluidTileMap.NO_TILE) st.TilesRefusedPoolFull++;
                    else st.TilesAcquired++;
                }
                return;
            }

            if (s_order.Length < n) s_order = new int[math.max(n, s_order.Length * 2)];
            for (int i = 0; i < n; i++) s_order[i] = i;
            System.Array.Sort(s_order, 0, n, Comparer.Wanted);

            // Resident tiles, farthest first, as displacement victims.
            s_resident.Clear(); s_residentD2.Clear();
            int edge = tiles.TileEdge;
            foreach (int3 coord in tiles.ResidentTileCoords())
            {
                int3 tLo = coord * edge;
                s_resident.Add(coord);
                s_residentD2.Add(DistSqToBox(centreVoxel, tLo, tLo + new int3(edge - 1, edge - 1, edge - 1)));
            }
            int rn = s_resident.Count;
            var victims = new int[rn];
            for (int i = 0; i < rn; i++) victims[i] = i;
            System.Array.Sort(victims, Comparer.ResidentFarthestFirst);
            int v = 0;

            for (int k = 0; k < n; k++)
            {
                int w = s_order[k];
                if (tiles.ResidentTiles >= tiles.TileCapacity && v < rn &&
                    s_residentD2[victims[v]] > s_wantedD2[w] && tiles.Release(s_resident[victims[v]]))
                {
                    v++;
                    st.TilesDisplaced++;
                }
                if (tiles.Acquire(s_wanted[w]) == FluidTileMap.NO_TILE) st.TilesRefusedPoolFull++;
                else st.TilesAcquired++;
            }
        }

        private sealed class Comparer : System.Collections.Generic.IComparer<int>
        {
            public static readonly Comparer Wanted = new Comparer(false);
            public static readonly Comparer ResidentFarthestFirst = new Comparer(true);
            private readonly bool _resident;
            private Comparer(bool resident) { _resident = resident; }
            public int Compare(int a, int b) => _resident
                ? s_residentD2[b].CompareTo(s_residentD2[a])
                : s_wantedD2[a].CompareTo(s_wantedD2[b]);
        }

        /// Releases tiles whose backing chunk is no longer resident.
        ///
        /// GATE 1 WAS ONLY EVER TESTED ONCE. Refresh refuses to ADMIT a tile
        /// whose chunk is not resident, and then never asks again;
        /// ReleaseBeyondSleep tests only the radius. So a chunk evicted under
        /// a live tile left that tile resident indefinitely -- holding a pool
        /// slot, and dispatching every tick against terrain the CPU no longer
        /// has. Every op it emitted then hit §9.4's guard in
        /// FluidOpListReadback and was discarded.
        ///
        /// MEASURED, NOT THEORETICAL: 15 of 787 resident tiles (1.9%) were in
        /// this state at the end of a 200s siege. It did not show at the old
        /// 512 cap for a reason worth keeping in mind -- a saturated pool has
        /// no spare capacity to hold a tile it no longer needs, so raising the
        /// cap to 1024 is what made the pre-existing gap observable.
        ///
        /// SAFE FOR THE SAME REASON THE RADIUS RELEASE IS (§7.7): the terrain
        /// byte is authoritative, the tile carries no material of its own, and
        /// Refresh re-acquires when the chunk streams back and gate 1 passes.
        /// A tile lies wholly inside ONE chunk by construction (TileEdge | 128
        /// -- FluidTileMap calls that a correctness property, not a
        /// convenience), so "the tile's chunk" is unambiguous and one lookup
        /// decides it.
        public static int ReleaseOrphaned(ChunkStore store, FluidTileMap tiles)
        {
            if (store == null || tiles == null) return 0;
            int edge = tiles.TileEdge;
            int freed = 0;
            foreach (int3 coord in tiles.ResidentTileCoords())
            {
                int3 anyVoxelInTile = coord * edge;
                if (store.GetChunk(CoordMath.VoxelToChunk(anyVoxelInTile)) == null &&
                    tiles.Release(coord)) freed++;
            }
            return freed;
        }

        /// Releases tiles whose FARTHEST corner is beyond the sleep radius, i.e.
        /// only once the whole tile has left. §7.7 loses nothing by doing so:
        /// the terrain byte is authoritative and the tile re-acquires on return.
        public static int ReleaseBeyondSleep(FluidTileMap tiles, int3 centreVoxel, int sleepRadiusVoxels)
        {
            long sleep2 = (long)sleepRadiusVoxels * sleepRadiusVoxels;
            int freed = 0;
            int edge = tiles.TileEdge;

            foreach (int3 coord in tiles.ResidentTileCoords())
            {
                int3 tLo = coord * edge;
                int3 tHi = tLo + new int3(edge - 1, edge - 1, edge - 1);
                if (DistSqToFarthest(centreVoxel, tLo, tHi) > sleep2 && tiles.Release(coord)) freed++;
            }
            return freed;
        }
    }
}
