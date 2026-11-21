// Assets/CoreEngine/WorldGen/TerrainGenChunkSource.Towns.cs
//
// Task 190 (`-towns`, default ON since task 195a, owner 2026-11-13 Q-X (a);
// `-towns 0` = no towns and, unless `-sites 1`, no site records, byte for byte):
// TOWNS ON THE SITES in the chunk pass.
// TerrainGen.Towns holds the rules (header there); this file plumbs its plane
// through the engine's generators so LOD 0, the shells and the point query
// give the same voxel:
//   every MapView      MapView.town (the flatten runs inside ShapeField.Sample)
//   ColumnJob /        the highest building voxel over the footprint raises
//   ShellColumnJob     the column top, so the air fast paths stay exact
//   VoxelJob /         OverlayBuildings after the voxel pass (and after the
//   ShellVoxelJob      trees): every voxel a building part claims takes it
//                      (AIR parts carve); uniform bricks touched are expanded,
//                      touched bricks re-checked for uniformity
//   PointVoxel         Towns.VoxelAt first (FarQuery)
// The far band draws the flattened heights only (no buildings, like trees).
//
// THREADING: the plane is read-only and shared like the map arrays.
using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

namespace VoxelEngine.WorldGen
{
    public sealed partial class TerrainGenChunkSource
    {
        private NativeArray<int> _town;   // task 190: WorldMap.TownPlane (length 1 = none)

        internal NativeArray<int> PointTown => _town;

        /// -towns 0 (task 190; default flipped by task 195a): spec.townsOn stays 0 (and
        /// spec.sitesOn stays 0 unless -sites 1), no towns, byte for byte. Absent or
        /// 1 = spec.townsOn = 1 AND spec.sitesOn = 1 -- the site discs flattened and
        /// the building kit placed on them (the default). Last match wins. Read by this
        /// chunk source and FarField's own map (the flattened heights); a bare
        /// TerrainSpec stays 0.
        public static bool TownsArg()
        {
            string[] a = Environment.GetCommandLineArgs();
            bool on = true;
            for (int i = 0; i + 1 < a.Length; i++)
                if (a[i] == "-towns" && int.TryParse(a[i + 1], out int p)) on = p != 0;
            return on;
        }

        /// Tests and reports (task 190's on / off): spec.Towns for the chunks
        /// generated from now on (plans the sites and builds the plane on this
        /// source's own map the first time; trees keep their plane). Not
        /// thread-safe: call between Generate calls.
        public void SetTowns(bool on)
        {
            var t = Spec;
            t.townsOn = on ? (byte)1 : (byte)0;
            if (on)
            {
                t.sitesOn = 1;
                Map.EnsureTowns(true);
                if (_town.IsCreated) _town.Dispose();
                _town = new NativeArray<int>(Map.TownPlane, Allocator.Persistent);
            }
            Spec = t;
            _columnTop.Clear(); _specStamp++;
        }

        /// The town plane as a job array (length 1 = none), building it if the spec asks.
        public static NativeArray<int> TownArray(TerrainGen.WorldMap map)
        {
            if (map.Spec.Towns) map.EnsureTowns();
            return new NativeArray<int>(map.Spec.Towns && map.TownPlane != null ? map.TownPlane : new int[1], Allocator.Persistent);
        }

        /// The highest building voxel over a footprint, + `pad` (a shell's coarse
        /// cell reaches up to 2^shift above its sample), or int.MinValue.
        internal static unsafe int TownTop(NativeArray<int> town, int x0, int z0, int x1, int z1, int pad)
        {
            if (town.Length <= 1) return int.MinValue;
            int t = TerrainGen.Towns.TopOver((int*)town.GetUnsafeReadOnlyPtr(), x0, z0, x1, z1);
            return t == int.MinValue ? t : t + pad;
        }

        /// The building overlay after a voxel pass (LOD 0: shift = half = 0).
        /// Coarse voxel i samples fine voxel origin + (i << shift) + half and
        /// covers fine [origin + (i << shift), + 2^shift); Towns.CoarseRole decides
        /// its part. A voxel no part claims keeps the terrain.
        internal static unsafe void OverlayBuildings(NativeArray<int> town, int3 origin, int shift, int half, NativeArray<byte> toEngine,
                                                     NativeArray<byte> mats, NativeArray<byte> brickMat, NativeArray<bool> brickUniform)
        {
            if (town.Length <= 1) return;
            int* t = (int*)town.GetUnsafeReadOnlyPtr();
            int ns = t[0];
            if (ns <= 0) return;
            const int E = 128;
            int span = E << shift;
            int3 hi = origin + span - 1;
            NativeArray<bool> touched = default;
            for (int k = 0; k < ns; k++)
            {
                int* e = t + t[2] + k * TerrainGen.Towns.SITE_INTS;
                int R = e[2];
                if (e[6] == 0 || e[0] + R < origin.x || e[0] - R > hi.x || e[1] + R < origin.z || e[1] - R > hi.z) continue;
                for (int b = e[5]; b < e[5] + e[6]; b++)
                {
                    int* f = t + t[3] + b * TerrainGen.Towns.BLD_INTS;
                    int3 bmin = new int3(f[1], f[2] - f[4], f[3]), bmax = new int3(f[5], f[7], f[6]);   // half-open
                    int3 i0 = math.max((bmin - origin) >> shift, 0);
                    int3 i1 = math.min((bmax - 1 - origin) >> shift, E - 1);
                    if (math.any(i0 > i1)) continue;
                    if (!touched.IsCreated) touched = new NativeArray<bool>(EngineConfig.BRICKS_PER_CHUNK, Allocator.Temp);
                    var bd = TerrainGen.Towns.BuildingAt(f);
                    for (int y = i0.y; y <= i1.y; y++)
                    for (int z = i0.z; z <= i1.z; z++)
                    for (int x = i0.x; x <= i1.x; x++)
                    {
                        int3 p = origin + (new int3(x, y, z) << shift) + half;
                        byte r = TerrainGen.Towns.CoarseRole(in bd, p, shift, half);
                        if (r == TerrainGen.BuildingKit.NONE) continue;
                        byte m = toEngine[TerrainGen.BuildingKit.Material(r, bd.biome, bd.shape)];
                        int bi = ((z >> 3) << 8) | ((y >> 3) << 4) | (x >> 3);
                        if (brickUniform[bi] && !touched[bi])
                        {
                            byte u = brickMat[bi];   // LOD 0 leaves a uniform brick's mats unwritten
                            int bx = x >> 3, by = y >> 3, bz = z >> 3;
                            for (int vy = 0; vy < 8; vy++)
                            for (int vz = 0; vz < 8; vz++)
                            {
                                int row = (((by << 3) + vy) << 14) | (((bz << 3) + vz) << 7) | (bx << 3);
                                for (int vx = 0; vx < 8; vx++) mats[row + vx] = u;
                            }
                        }
                        touched[bi] = true;
                        mats[(y << 14) | (z << 7) | x] = m;
                    }
                }
            }
            if (!touched.IsCreated) return;
            for (int bi = 0; bi < EngineConfig.BRICKS_PER_CHUNK; bi++)
            {
                if (!touched[bi]) continue;
                int bx = bi & 15, by = (bi >> 4) & 15, bz = bi >> 8;
                byte first = mats[((by << 3) << 14) | ((bz << 3) << 7) | (bx << 3)];
                bool uni = true;
                for (int vy = 0; vy < 8 && uni; vy++)
                for (int vz = 0; vz < 8 && uni; vz++)
                {
                    int row = (((by << 3) + vy) << 14) | (((bz << 3) + vz) << 7) | (bx << 3);
                    for (int vx = 0; vx < 8; vx++) if (mats[row + vx] != first) { uni = false; break; }
                }
                brickUniform[bi] = uni;
                brickMat[bi] = first;
            }
            touched.Dispose();
        }
    }
}
