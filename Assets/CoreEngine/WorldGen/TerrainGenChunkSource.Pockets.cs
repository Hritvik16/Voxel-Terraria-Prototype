// Assets/CoreEngine/WorldGen/TerrainGenChunkSource.Pockets.cs
//
// Task 183 (placement, fluid pockets at rest; DEFAULT ON since task 201, `-fluidpockets 0` = off):
// plumbs TerrainGen.FluidPockets' site plane into the engine's generators the
// way task 181's ore plane is: ColumnJob, ShellColumnJob, the shell fill and
// PointVoxel (FarQuery) hand it to ShapeField.Sample through MapView.pocket,
// and FarField builds its own copy from the same WorldMap. Sample applies the
// pocket to the column (FluidPockets.Apply), so LOD 0, the shells, the point
// query and the far band agree by construction.
//
// THREADING: the plane is built once on the main thread (constructor or
// SetFluidPockets) and only read by jobs, like the map arrays (single-writer).
using System;
using Unity.Collections;

namespace VoxelEngine.WorldGen
{
    public sealed partial class TerrainGenChunkSource
    {
        private NativeArray<int> _pocket;   // task 183: FluidPockets.BuildPlane; length 1 = none

        /// -fluidpockets 0 (task 183; default flipped by task 201): spec.fluidPocketsOn
        /// stays 0 = no pockets beyond the old deep ones, the pre-201 world byte for
        /// byte. Absent or 1 = spec.fluidPocketsOn = 1 (the default). Last match wins.
        /// Only this chunk source sets it: a bare TerrainSpec stays 0 (as 195 / 195a),
        /// because the pockets need the site plane this source builds (BuildPocketPlane).
        public static bool FluidPocketsArg()
        {
            string[] a = Environment.GetCommandLineArgs();
            bool on = true;
            for (int i = 0; i + 1 < a.Length; i++)
                if (a[i] == "-fluidpockets" && int.TryParse(a[i + 1], out int p)) on = p != 0;
            return on;
        }

        private void BuildPocketPlane()
        {
            if (_pocket.IsCreated && (_pocket.Length > 1 || !Spec.Pockets)) return;
            if (_pocket.IsCreated) _pocket.Dispose();
            if (Spec.Pockets && Map.Record != null)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                Map.EnsurePocketPlane();
                var st = Map.PocketStats;
                UnityEngine.Debug.Log($"[FluidPockets] plane {sw.ElapsedMilliseconds} ms: {st.sites} surface sites " +
                                      $"({st.candidates} cells, {st.ruled} ruled, rejected cells {st.rejectedCells} / samples {st.rejectedSamples} / flat {st.rejectedFlat}); " +
                                      $"basins (task 196): banked {st.banked}, capped {st.bankCapped}, max cut {st.maxUp} / fill {st.maxDn}, shallower bowls {st.depthCut}");
                _pocket = new NativeArray<int>(Map.PocketPlane, Allocator.Persistent);
            }
            else _pocket = new NativeArray<int>(1, Allocator.Persistent);
        }

        /// TESTS ONLY (task 183's adversarial case): overwrite plane cell `index`'s
        /// four words in this source's copy (the WorldMap's plane is untouched).
        /// Call between Generate calls, with the pockets on.
        public void OverridePocketSite(int index, int w0, int w1, int w2, int w3)
        {
            int b = index * TerrainGen.FluidPockets.STRIDE;
            _pocket[b] = w0; _pocket[b + 1] = w1; _pocket[b + 2] = w2; _pocket[b + 3] = w3;
            _columnTop.Clear(); _specStamp++;
        }

        /// Tests: this source's plane copy (length 1 = none).
        public NativeArray<int> PocketPlaneCopy => _pocket;

        /// Tests and reports (task 183's on / off): spec.fluidPocketsOn for the
        /// chunks generated from now on (builds the plane the first time). Not
        /// thread-safe: call between Generate calls.
        public void SetFluidPockets(bool on)
        {
            var t = Spec; t.fluidPocketsOn = on ? (byte)1 : (byte)0; Spec = t;
            BuildPocketPlane();
            _columnTop.Clear(); _specStamp++;
        }
    }
}
