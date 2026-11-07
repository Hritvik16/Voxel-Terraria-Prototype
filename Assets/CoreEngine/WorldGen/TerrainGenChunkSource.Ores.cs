// Assets/CoreEngine/WorldGen/TerrainGenChunkSource.Ores.cs
//
// Task 181 (placement, resource deposits, `-ores 1`; default OFF): plumbs
// TerrainGen.OreRules' per-map-cell plane (OreRulePlane.CellPlane) into the
// engine's generators, as the step-3 ore plane and task 180's tree plane are:
// ColumnJob, ShellColumnJob and PointVoxel (FarQuery) copy the column's cell
// into ColumnDetail.oreCell through MapView.oreCell, so LOD 0, the shells and
// the point query run the same rule on the same data. The far band's top colour
// never reaches rock, and its dug floors see no plane: no rule ore there (one
// texel is >= 0.8 m; a vein is 0.4-0.6 m thick).
//
// THREADING: the plane is built once on the main thread (constructor or
// SetOres) and only read by jobs, like the map arrays (single-writer, CLAUDE.md).
using System;
using Unity.Collections;

namespace VoxelEngine.WorldGen
{
    public sealed partial class TerrainGenChunkSource
    {
        private NativeArray<uint> _oreCell;   // task 181: OreRulePlane.CellPlane; length 1 = none

        /// -ores 1 (task 181): spec.oresOn = 1, the rule ores. Absent or 0 = step 3's
        /// placement (the default). Last match wins.
        public static bool OresArg()
        {
            string[] a = Environment.GetCommandLineArgs();
            bool on = false;
            for (int i = 0; i + 1 < a.Length; i++)
                if (a[i] == "-ores" && int.TryParse(a[i + 1], out int p)) on = p != 0;
            return on;
        }

        /// The plane when the rules are on (built once), else a one-entry dummy.
        private void BuildOreCellPlane()
        {
            if (_oreCell.IsCreated && (_oreCell.Length > 1 || !Spec.Ores2)) return;
            if (_oreCell.IsCreated) _oreCell.Dispose();
            _oreCell = Spec.Ores2 && Map.Record != null
                ? new NativeArray<uint>(TerrainGen.OreRulePlane.CellPlane(Map, Spec), Allocator.Persistent)
                : new NativeArray<uint>(1, Allocator.Persistent);
        }

        /// The plane (tests and reports); length 1 while the rules have never been on.
        public NativeArray<uint> OreCellPlane => _oreCell;

        /// Tests and reports (task 181's on / off): spec.oresOn for the chunks
        /// generated from now on (builds the plane the first time). Not
        /// thread-safe: call between Generate calls.
        public void SetOres(bool on)
        {
            var t = Spec; t.oresOn = on ? (byte)1 : (byte)0; Spec = t;
            BuildOreCellPlane();
            _columnTop.Clear(); _specStamp++;
        }
    }
}
