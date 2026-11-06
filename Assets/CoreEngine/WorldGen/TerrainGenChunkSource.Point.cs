// Assets/CoreEngine/WorldGen/TerrainGenChunkSource.Point.cs
//
// Amendment 8.15 row Q1 (task 104): ONE VOXEL of a 0.1 m chunk, without the
// chunk. The same calls Generate makes for that voxel, in the same Burst float
// mode (Strict, High), so the byte is the chunk's byte (FarQueryTests):
//   ColumnJob  S = ShapeField.Sample at (x + 0.5, z + 0.5), texel 1; the step
//              at the top = max |S.height(neighbour) - S.height| over the four
//              +-1 columns (ColumnJob reads those from its grid; they are the
//              neighbours' own samples, the same floats); D's column detail
//              (PrepareColumn) with the ore multiplier from the ore plane.
//   VoxelJob   CullBrick of the voxel's brick, then DetailField.Material,
//              mapped through FarField.GenToEngine.
// Generate's fast paths (air above a column's top, task 82b's brick skip) are
// exact by construction, so the per-voxel answer is the chunk's on them too.
//
// THREADING: a pure function of its arguments; the map arrays are this
// source's, shared read-only exactly as the stream workers share them.
using Unity.Collections;
using Unity.Mathematics;

namespace VoxelEngine.WorldGen
{
    public sealed partial class TerrainGenChunkSource
    {
        /// One column's generator inputs, kept between consecutive points of the
        /// same (x, z) (a vertical probe pays the column pass once).
        public struct PointColumn
        {
            internal bool valid;
            internal int x, z;
            internal TerrainGen.ColumnSample c;
            internal float st;
            internal TerrainGen.DetailField.ColumnDetail cd;
        }

        // The read-only arrays a point job holds (valid while this source lives).
        internal NativeArray<float> PointHeight => _h;
        internal NativeArray<float> PointRough => _r;
        internal NativeArray<float> PointWater => _w;
        internal NativeArray<byte> PointWaterClass => _cls;
        internal NativeArray<ushort> PointFlow => _flow;
        internal NativeArray<byte> PointRoute => _route;   // task 178b
        internal NativeArray<byte> PointOre => _ore;
        internal NativeArray<byte> PointToEngine => _toEngine;

        /// The engine material Generate gives voxel `v` (world voxel coordinates).
        /// Call from a Burst job compiled FloatMode.Strict / FloatPrecision.High.
        internal static byte PointVoxel(in TerrainGen.TerrainSpec spec, in TerrainGen.Offsets o, in TerrainGen.MapView map,
                                        NativeArray<byte> ore, NativeArray<byte> toEngine, int3 v,
                                        ref PointColumn col, ref TerrainGen.DetailField.NodeCache nodes)
        {
            if (!col.valid || col.x != v.x || col.z != v.z)
            {
                int x = v.x, z = v.z;
                var c = TerrainGen.ShapeField.Sample(in spec, in map, in o, x + 0.5f, z + 0.5f, 1);
                int ch = c.height;
                int he = TerrainGen.ShapeField.Sample(in spec, in map, in o, (x + 1) + 0.5f, z + 0.5f, 1, false).height;
                int hw = TerrainGen.ShapeField.Sample(in spec, in map, in o, (x - 1) + 0.5f, z + 0.5f, 1, false).height;
                int hn = TerrainGen.ShapeField.Sample(in spec, in map, in o, x + 0.5f, (z + 1) + 0.5f, 1, false).height;
                int hs = TerrainGen.ShapeField.Sample(in spec, in map, in o, x + 0.5f, (z - 1) + 0.5f, 1, false).height;
                int m = math.max(math.max(math.abs(he - ch), math.abs(hw - ch)),
                                 math.max(math.abs(hn - ch), math.abs(hs - ch)));
                var cd = TerrainGen.DetailField.PrepareColumn(in spec, in o, x, z, ref nodes);
                if (cd.oreMul != 0) cd.oreMul = ore[TerrainGen.Ores.CellIndex(x, z, spec.mapCells, spec.mapCellVoxels)];
                col.c = c; col.st = m / 1f; col.cd = cd;
                col.x = x; col.z = z; col.valid = true;
            }
            // The brick's origin: (v >> 3) << 3 by mask, negatives included.
            var cr = TerrainGen.DetailField.CullBrick(in spec, in o, v.x & ~7, v.y & ~7, v.z & ~7);
            byte g = TerrainGen.DetailField.Material(in spec, in o, in col.c, in col.cd, col.st, v.x, v.y, v.z, in cr);
            return toEngine[g];
        }
    }
}
