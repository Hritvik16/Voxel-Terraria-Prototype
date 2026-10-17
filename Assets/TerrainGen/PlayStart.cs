// Assets/TerrainGen/PlayStart.cs
//
// THE PLAY START (gameplay step 4, plan P21; brief "Start": Decided). Where an
// interactive session first stands: in the best easy area of the world record
// (B4, EasyAreas.cs), on Walk-band ground, never in water. Rigs and capture
// runs keep Phase4Bootstrapper.Tg2Spawn -- this is read by play only.
//
//   Mild first (task 98, owner Q53 (a)): a gentle first look, as Terraria's
//         forest start; the hard biomes come later. MILD biomes are the ones
//         Danger's tier table rates 0: GenBiome.Forest (0) and Savanna (4).
//         An area's MILD SIZE is how many of its cells have a mild biome.
//         When some area's mild size reaches MIN_MILD_HA, the rule below runs
//         on mild sizes and only mild cells may be the start (neighbours too
//         for "interior"). Otherwise (or when no mild cell passes) the area
//         rule runs on whole areas of any biome, as task 93c1 did.
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
        /// Smallest mild part of an area that may hold the start: a small clearing,
        /// 2 ha (~140 m across; 312 cells of 8 m). B4's areas are >= 25 ha.
        public const float MIN_MILD_HA = 2f;

        /// Danger tier 0 biomes: the gentle first look.
        public static bool IsMild(byte biome) => biome == GenBiome.Forest || biome == GenBiome.Savanna;

        public struct Pick
        {
            public int area;       // index in WorldRecord.EasyAreaList
            public int cell;       // map cell index (z * n + x)
            public int2 column;    // voxels (x, z): the cell centre
            public int groundY;    // ShapeField.Sample's top solid voxel there
            public bool interior;  // all 8 neighbours easy, Walk and dry (and mild when mild)
            public bool mild;      // picked by the mild rule (cell biome is mild)
            public byte biome;     // GenBiome of the start cell
            public override string ToString() =>
                $"area {area} cell {cell} column ({column.x}, {column.y}) ground {groundY} interior {interior} mild {mild} biome {biome}";
        }

        /// The best area's index (rule above), or -1 when the record has none.
        public static int BestArea(EasyArea[] areas, in TerrainSpec spec) => BestArea(areas, in spec, null);

        /// The same rule on `sizes` (one per area; null = cellCount). Areas of size 0 never win.
        public static int BestArea(EasyArea[] areas, in TerrainSpec spec, int[] sizes)
        {
            if (areas == null || areas.Length == 0) return -1;
            int largest = 0;
            for (int i = 0; i < areas.Length; i++) largest = math.max(largest, sizes != null ? sizes[i] : areas[i].cellCount);
            if (largest <= 0) return -1;
            float2 mid = new float2(spec.worldSizeVoxels * 0.5f, spec.worldSizeVoxels * 0.5f);
            int best = -1;
            long bestD2 = long.MaxValue;
            for (int i = 0; i < areas.Length; i++)
            {
                var a = areas[i];
                int size = sizes != null ? sizes[i] : a.cellCount;
                if (a.cells == null || a.cells.Length == 0 || size < AREA_TIE * largest) continue;
                long dx = a.centre.x - (long)mid.x, dz = a.centre.y - (long)mid.y;
                long d2 = dx * dx + dz * dz;
                if (d2 < bestD2) { bestD2 = d2; best = i; }
            }
            return best;
        }

        /// Cells of each area whose biome is mild (`data` = WorldRecord.Data).
        public static int[] MildSizes(EasyArea[] areas, RecordCell[] data)
        {
            var sizes = new int[areas?.Length ?? 0];
            for (int i = 0; i < sizes.Length; i++)
                if (areas[i].cells != null)
                    foreach (int cell in areas[i].cells)
                        if (IsMild(data[cell].Biome)) sizes[i]++;
            return sizes;
        }

        /// MIN_MILD_HA in map cells for this spec (313 for 8 m cells).
        public static int MinMildCells(in TerrainSpec spec)
        {
            float cellM = spec.mapCellVoxels / 10f;
            return (int)math.ceil(MIN_MILD_HA * 10000f / (cellM * cellM));
        }

        /// The mild rule's area (-1 when no area's mild size reaches MinMildCells).
        public static int BestMildArea(EasyArea[] areas, RecordCell[] data, in TerrainSpec spec)
        {
            var sizes = MildSizes(areas, data);
            int min = MinMildCells(in spec);
            for (int i = 0; i < sizes.Length; i++) if (sizes[i] < min) sizes[i] = 0;
            return BestArea(areas, in spec, sizes);
        }

        /// The area the start comes from: the mild rule's, else the largest of any biome.
        public static int ChooseArea(EasyArea[] areas, RecordCell[] data, in TerrainSpec spec, out bool mild)
        {
            int m = BestMildArea(areas, data, in spec);
            mild = m >= 0;
            return mild ? m : BestArea(areas, in spec);
        }

        /// The play start of this map; false below v4 or when no cell passes.
        /// preferMild false = task 93c1's rule (any biome; `-playstartmild 0`).
        public static bool Find(WorldMap map, out Pick pick, bool preferMild = true)
        {
            pick = default;
            var rec = map?.Record;
            if (rec == null) return false;
            var spec = map.Spec;
            bool mild = false;
            int area = preferMild ? ChooseArea(rec.EasyAreaList, rec.Data, in spec, out mild) : BestArea(rec.EasyAreaList, in spec);
            if (area < 0) return false;
            if (FindIn(map, area, mild, out pick)) return true;
            // No mild cell of that area passes: the any-biome rule, as task 93c1.
            if (!mild) return false;
            area = BestArea(rec.EasyAreaList, in spec);
            return area >= 0 && FindIn(map, area, false, out pick);
        }

        private static bool FindIn(WorldMap map, int area, bool mild, out Pick pick)
        {
            pick = default;
            var rec = map.Record;
            var spec = map.Spec;
            var view = map.View;
            var o = Offsets.For(in spec);
            int n = rec.Cells;
            bool haveFallback = false;
            Pick fallback = default;
            foreach (int cell in rec.EasyAreaList[area].cells)
            {
                if (!CellOk(map, cell, mild)) continue;
                int cx = cell % n, cz = cell / n;
                int2 col = new int2(cx * rec.CellVoxels + rec.CellVoxels / 2, cz * rec.CellVoxels + rec.CellVoxels / 2);
                var s = ShapeField.Sample(in spec, in view, in o, col.x, col.y, 1);
                if (s.height <= spec.seaLevelY || (s.water != ColumnSample.NO_WATER_Y && s.water > s.height)) continue;
                bool interior = true;
                for (int dz = -1; dz <= 1 && interior; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int x = cx + dx, z = cz + dz;
                    if (x < 0 || z < 0 || x >= n || z >= n || !CellOk(map, z * n + x, mild)) { interior = false; break; }
                }
                var p = new Pick { area = area, cell = cell, column = col, groundY = s.height, interior = interior,
                                   mild = mild, biome = rec.Data[cell].Biome };
                if (interior) { pick = p; return true; }
                if (!haveFallback) { fallback = p; haveFallback = true; }
            }
            pick = fallback;
            return haveFallback;
        }

        private static bool CellOk(WorldMap map, int cell, bool mild) =>
            CellOk(map, cell) && (!mild || IsMild(map.Record.Data[cell].Biome));

        /// Easy, Walk band, not sea, no lake or river on the map cell.
        public static bool CellOk(WorldMap map, int cell)
        {
            var c = map.Record.Data[cell];
            return c.IsEasy && !c.IsSea && c.MoveBand == Danger.Walk && map.WaterClass[cell] == Hydrology.DRY;
        }
    }
}
