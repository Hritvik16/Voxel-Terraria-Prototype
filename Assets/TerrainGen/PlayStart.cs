// Assets/TerrainGen/PlayStart.cs
//
// THE PLAY START (gameplay step 4, plan P21; brief "Start": Decided). Where an
// interactive session first stands: in the best easy area of the world record
// (B4, EasyAreas.cs), on Walk-band ground, never in water. Rigs and capture
// runs keep Phase4Bootstrapper.Tg2Spawn -- this is read by play only.
//
//   Area: the LARGEST easy area; areas within AREA_TIE of the largest count as
//         tied and the one whose centre is nearest the island centre wins
//         (areas come out of B4 at nearly the same size, SHARE / count each,
//         so "most central" usually decides). Ties of distance: lower index.
//   Cell: the first of that area's cells (nearest its centre first) that is
//         FLAG_EASY, Walk band, not sea, DRY (WaterClass and the column's own
//         still water), above the sea, and whose 8 neighbours are easy, Walk
//         and dry too -- you look out over easy ground, not off its rim.
//         None interior -> the first that passes alone.
//
// Read only: a pure function of the map (no state, nothing written), so the
// same seed gives the same start on every machine. Map v4 only: below v4 (no
// record, no easy areas) Find returns false and the caller keeps Tg2Spawn.
using Unity.Mathematics;

namespace TerrainGen
{
    public static class PlayStart
    {
        /// Areas at least this share of the largest one's cell count are tied on size.
        public const float AREA_TIE = 0.9f;

        public struct Pick
        {
            public int area;       // index in WorldRecord.EasyAreaList
            public int cell;       // map cell index (z * n + x)
            public int2 column;    // voxels (x, z): the cell centre
            public int groundY;    // ShapeField.Sample's top solid voxel there
            public bool interior;  // all 8 neighbours easy, Walk and dry
            public override string ToString() =>
                $"area {area} cell {cell} column ({column.x}, {column.y}) ground {groundY} interior {interior}";
        }

        /// The best area's index (rule above), or -1 when the record has none.
        public static int BestArea(EasyArea[] areas, in TerrainSpec spec)
        {
            if (areas == null || areas.Length == 0) return -1;
            int largest = 0;
            foreach (var a in areas) largest = math.max(largest, a.cellCount);
            float2 mid = new float2(spec.worldSizeVoxels * 0.5f, spec.worldSizeVoxels * 0.5f);
            int best = -1;
            long bestD2 = long.MaxValue;
            for (int i = 0; i < areas.Length; i++)
            {
                var a = areas[i];
                if (a.cells == null || a.cells.Length == 0 || a.cellCount < AREA_TIE * largest) continue;
                long dx = a.centre.x - (long)mid.x, dz = a.centre.y - (long)mid.y;
                long d2 = dx * dx + dz * dz;
                if (d2 < bestD2) { bestD2 = d2; best = i; }
            }
            return best;
        }

        /// The play start of this map; false below v4 or when no cell passes.
        public static bool Find(WorldMap map, out Pick pick)
        {
            pick = default;
            var rec = map?.Record;
            var spec = map != null ? map.Spec : default;
            int area = rec != null ? BestArea(rec.EasyAreaList, in spec) : -1;
            if (area < 0) return false;
            var view = map.View;
            var o = Offsets.For(in spec);
            int n = rec.Cells;
            bool haveFallback = false;
            Pick fallback = default;
            foreach (int cell in rec.EasyAreaList[area].cells)
            {
                if (!CellOk(map, cell)) continue;
                int cx = cell % n, cz = cell / n;
                int2 col = new int2(cx * rec.CellVoxels + rec.CellVoxels / 2, cz * rec.CellVoxels + rec.CellVoxels / 2);
                var s = ShapeField.Sample(in spec, in view, in o, col.x, col.y, 1);
                if (s.height <= spec.seaLevelY || (s.water != ColumnSample.NO_WATER_Y && s.water > s.height)) continue;
                bool interior = true;
                for (int dz = -1; dz <= 1 && interior; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int x = cx + dx, z = cz + dz;
                    if (x < 0 || z < 0 || x >= n || z >= n || !CellOk(map, z * n + x)) { interior = false; break; }
                }
                var p = new Pick { area = area, cell = cell, column = col, groundY = s.height, interior = interior };
                if (interior) { pick = p; return true; }
                if (!haveFallback) { fallback = p; haveFallback = true; }
            }
            pick = fallback;
            return haveFallback;
        }

        /// Easy, Walk band, not sea, no lake or river on the map cell.
        public static bool CellOk(WorldMap map, int cell)
        {
            var c = map.Record.Data[cell];
            return c.IsEasy && !c.IsSea && c.MoveBand == Danger.Walk && map.WaterClass[cell] == Hydrology.DRY;
        }
    }
}
