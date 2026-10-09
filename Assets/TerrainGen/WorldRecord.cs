// Assets/TerrainGen/WorldRecord.cs
//
// THE WORLD RECORD (map v4, surface rebuild B1, owner 2026-10-03). One small
// entry per 8 m map cell saying what kind of place this is -- the landform
// archetype and how deep into it, the biome, how far fresh water is -- so that
// later steps (object placement, towns, danger, movement bands) read the
// biome x landform pair from ONE table instead of re-deriving it.
//
// CPU only, built once with the map (WorldMap.BuildV4), NEVER SAVED: it is a
// pure function of the spec (seed), so every machine in a co-op game rebuilds
// the same bytes (WorldRecordTests hash it). Integer distance transforms only
// (5/7 chamfer), no float in anything but the biome, which is ShapeField.Biome
// itself.
//
// LAYOUT: 6 bytes per cell (RecordCell), 1024^2 cells = 6,291,456 bytes.
//   landform   lo nibble archetype (Regions.*; SEA = 15), hi nibble depth 0-15
//              (DEPTH_STEP_M steps to the nearest cell of another archetype or
//              the sea; 15 = that far or more)
//   biome      lo nibble GenBiome (4 today, room for 16); hi nibble spare
//   freshWater distance to the nearest LAKE or RIVER cell (WorldMap.WaterClass,
//              kept as is), WATER_STEP_M steps; WATER_FAR (250) = 4 km or more
//   hazard     B6 (Danger.cs): bits 0-2 base danger 0-7 (altitude + landform
//              + biome tier, from Danger's data table), bits 3-4 movement band
//              (Walk / Scramble / Climb / Sheer from the 8 m map slope); 0 on
//              sea cells; bits 5-7 zero
//   flags      bit 0 FLAG_EASY (B4: the cell is easy ground inside a seeded
//              easy area, EasyAreas.cs); bit 1 FLAG_FEATURE (B5: the cell is
//              on a signature feature's footprint, Features.cs; never both);
//              bits 2-7 B6-B8 -- zero
//   site       B5: on a FLAG_FEATURE cell, the feature's index in FeatureList
//              + 1 (ids 1-15 are features); 0 elsewhere. B7-B8 take ids >= 16.
//
// LISTS beside the cells (each carries MapVersion): EasyAreaList (B4),
// FeatureList (B5).
using System.Runtime.InteropServices;
using Unity.Mathematics;

namespace TerrainGen
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct RecordCell
    {
        public byte landform;
        public byte biome;
        public byte freshWater;
        public byte hazard;
        public byte flags;
        public byte site;

        public byte Archetype => (byte)(landform & 15);
        public bool IsSea => (landform & 15) == WorldRecord.SEA;
        public int Depth => landform >> 4;
        public byte Biome => (byte)(biome & 15);
        public int FreshWaterM => freshWater * WorldRecord.WATER_STEP_M;
        public int Danger => hazard & 7;
        public int MoveBand => (hazard >> 3) & 3;
        public bool IsEasy => (flags & WorldRecord.FLAG_EASY) != 0;
        public bool IsFeature => (flags & WorldRecord.FLAG_FEATURE) != 0;
    }

    public sealed class WorldRecord
    {
        public const int BYTES_PER_CELL = 6;
        public const byte SEA = 15;
        public const int DEPTH_STEP_M = 32;
        public const int WATER_STEP_M = 16;
        public const byte WATER_FAR = 250;          // 250 x 16 m = 4 km
        public const byte FLAG_EASY = 1;
        public const byte FLAG_FEATURE = 2;

        /// The map version this record was built for (4 today).
        public readonly int MapVersion;
        /// B4: the easy areas, in seeding order (lowest-scored site first);
        /// empty when none was seeded.
        public EasyArea[] EasyAreaList = new EasyArea[0];
        /// B5: the signature features in slot order (Features.cs); each
        /// carries its own mapVersion. Empty when none was placed.
        public Feature[] FeatureList = new Feature[0];

        public readonly int Cells;                  // per edge
        public readonly int CellVoxels;
        public readonly RecordCell[] Data;

        public long Bytes => (long)Data.Length * BYTES_PER_CELL;

        private WorldRecord(int cells, int cellVoxels, int mapVersion)
        {
            Cells = cells; CellVoxels = cellVoxels; MapVersion = mapVersion;
            Data = new RecordCell[cells * cells];
        }

        public RecordCell AtCell(int cx, int cz)
        {
            cx = math.clamp(cx, 0, Cells - 1); cz = math.clamp(cz, 0, Cells - 1);
            return Data[cz * Cells + cx];
        }

        /// The record of the map cell holding voxel column (x, z). Clamped to
        /// the map, so a point off the world reads its nearest edge cell.
        public RecordCell At(int x, int z) =>
            AtCell(math.max(x, 0) / CellVoxels, math.max(z, 0) / CellVoxels);

        /// ShapeField.Biome at every cell centre -- exactly what Sample reports.
        public static byte[] BiomePlane(TerrainSpec spec)
        {
            int n = spec.mapCells;
            var o = Offsets.For(in spec);
            var b = new byte[n * n];
            for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
                b[z * n + x] = ShapeField.Biome(in o, new float2((x + 0.5f) * spec.mapCellVoxels, (z + 0.5f) * spec.mapCellVoxels));
            return b;
        }

        public static WorldRecord Build(WorldMap map) => Build(map, BiomePlane(map.Spec));

        /// `biomes`: BiomePlane(map.Spec), computed ahead (WorldMap.BuildV4).
        public static WorldRecord Build(WorldMap map, byte[] biomes)
        {
            var spec = map.Spec;
            int n = spec.mapCells;
            var rec = new WorldRecord(n, spec.mapCellVoxels, spec.mapVersion);
            byte[] region = map.Region;
            var arch = new byte[n * n];
            for (int i = 0; i < arch.Length; i++) arch[i] = region[i] == 255 ? SEA : region[i];

            // Depth into the archetype: distance to the nearest cell whose
            // archetype differs (the sea included). Seeds are the cells with
            // a different 4-neighbour.
            var d = new int[n * n];
            for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                int i = z * n + x; byte a = arch[i];
                bool edge = (x > 0 && arch[i - 1] != a) || (x < n - 1 && arch[i + 1] != a)
                         || (z > 0 && arch[i - n] != a) || (z < n - 1 && arch[i + n] != a);
                d[i] = edge ? 0 : INF;
            }
            Chamfer(d, n);
            int depthUnits = DEPTH_STEP_M * 10 * CHAMFER_ORTHO / spec.mapCellVoxels;   // 32 m = 20 units
            for (int i = 0; i < d.Length; i++)
                rec.Data[i].landform = (byte)(arch[i] | (math.min(15, d[i] / depthUnits) << 4));

            // Distance to fresh water.
            byte[] wc = map.WaterClass;
            for (int i = 0; i < d.Length; i++) d[i] = wc[i] == Hydrology.LAKE || wc[i] == Hydrology.RIVER ? 0 : INF;
            Chamfer(d, n);
            int waterUnits = WATER_STEP_M * 10 * CHAMFER_ORTHO / spec.mapCellVoxels;   // 16 m = 10 units
            for (int i = 0; i < d.Length; i++)
                rec.Data[i].freshWater = (byte)math.min(WATER_FAR, d[i] / waterUnits);

            for (int i = 0; i < d.Length; i++) rec.Data[i].biome = biomes[i];
            Features.Mark(map, rec);   // before the easy areas: they skip feature cells
            EasyAreas.Mark(map, rec);
            Danger.Mark(map, rec);
            return rec;
        }

        // 5/7 chamfer (max error ~2% against Euclidean), two passes, integer.
        private const int CHAMFER_ORTHO = 5, CHAMFER_DIAG = 7, INF = int.MaxValue / 2;

        private static void Chamfer(int[] d, int n)
        {
            for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                int i = z * n + x, v = d[i];
                if (x > 0) v = math.min(v, d[i - 1] + CHAMFER_ORTHO);
                if (z > 0)
                {
                    v = math.min(v, d[i - n] + CHAMFER_ORTHO);
                    if (x > 0) v = math.min(v, d[i - n - 1] + CHAMFER_DIAG);
                    if (x < n - 1) v = math.min(v, d[i - n + 1] + CHAMFER_DIAG);
                }
                d[i] = v;
            }
            for (int z = n - 1; z >= 0; z--)
            for (int x = n - 1; x >= 0; x--)
            {
                int i = z * n + x, v = d[i];
                if (x < n - 1) v = math.min(v, d[i + 1] + CHAMFER_ORTHO);
                if (z < n - 1)
                {
                    v = math.min(v, d[i + n] + CHAMFER_ORTHO);
                    if (x < n - 1) v = math.min(v, d[i + n + 1] + CHAMFER_DIAG);
                    if (x > 0) v = math.min(v, d[i + n - 1] + CHAMFER_DIAG);
                }
                d[i] = v;
            }
        }

        /// FNV-1a 64 over every byte, in cell order.
        public ulong Hash()
        {
            ulong h = 14695981039346656037UL;
            foreach (var c in Data)
            {
                h = (h ^ c.landform) * 1099511628211UL;
                h = (h ^ c.biome) * 1099511628211UL;
                h = (h ^ c.freshWater) * 1099511628211UL;
                h = (h ^ c.hazard) * 1099511628211UL;
                h = (h ^ c.flags) * 1099511628211UL;
                h = (h ^ c.site) * 1099511628211UL;
            }
            return h;
        }
    }
}
