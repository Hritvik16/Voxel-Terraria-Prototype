// Assets/TerrainGen/Tests/PaletteSampleTests.cs
//
// Owner question (ii), 2026-09-25: "show me 3 directions" for the palette's own
// identity. The per-biome strata only exist as GenMat codes today (their engine
// ids come with V/H), so the fairest comparison colours the SAME generated
// content with each palette. This report writes raw GenMat-id grids of the
// sizeClass 2 island to Logs/TerrainGen/palette/*.bin; tools/palette-board.py
// colours them. Asserts nothing, so it is opt-in: REPORTS=1 (ReportOnly.cs).
//   top.bin      the map from above: top material, height and water per 8 m cell
//   peak.bin     a vertical section through the dominant peak (4 voxels / px)
//   deep.bin     a section from the surface to the world floor (4 voxels / px)
//   cliff.bin    a close-up of a steep flank (1 voxel / px): strata, soil, surface
// Format: int32 width, int32 height, then width*height bytes, row 0 at the top.
using System.IO;
using NUnit.Framework;
using Unity.Mathematics;

namespace TerrainGen.Tests
{
    public class PaletteSampleTests
    {
        private static TerrainSpec _spec;
        private static Offsets _o;
        private static WorldMap _map;

        [OneTimeSetUp]
        public void BuildOnce()
        {
            if (_map != null) return;
            _spec = TerrainSpec.SizeClass2(42);
            _o = Offsets.For(42);
            _map = SharedMaps.Get(_spec);
        }

        private static string Dir()
        {
            string d = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "TerrainGen", "palette");
            Directory.CreateDirectory(d);
            return d;
        }

        private static void WriteGrid(string name, int w, int h, byte[] ids)
        {
            using var f = new BinaryWriter(File.Create(Path.Combine(Dir(), name)));
            f.Write(w); f.Write(h); f.Write(ids);
        }

        private static ColumnSample S(int x, int z) { var v = _map.View; return ShapeField.Sample(in _spec, in v, in _o, x + 0.5f, z + 0.5f, 1); }

        /// A vertical section along +x at z, x in [x0, x0 + w*step), y from yTop
        /// down, `step` voxels per pixel.
        private static void Section(string name, int x0, int z, int w, int yTop, int h, int step)
        {
            var ids = new byte[w * h];
            for (int i = 0; i < w; i++)
            {
                int x = x0 + i * step;
                var c = S(x, z);
                var cd = DetailField.PrepareColumn(in _spec, in _o, x, z);
                var v = _map.View;
                float st = ShapeField.MaxStep(in _spec, in v, in _o, x + 0.5f, z + 0.5f, 1, c.height);
                for (int j = 0; j < h; j++)
                    ids[j * w + i] = DetailField.Material(in _spec, in _o, in c, in cd, st, x, yTop - j * step, z);
            }
            WriteGrid(name, w, h, ids);
        }

        [Test]
        public void Report_PaletteSamples()
        {
            ReportOnly.Require();
            int n = _spec.mapCells;
            var top = new byte[n * n];
            var hgt = new byte[n * n * 2];
            var v = _map.View;
            for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                float px = (x + 0.5f) * _spec.mapCellVoxels, pz = (z + 0.5f) * _spec.mapCellVoxels;
                var c = ShapeField.Sample(in _spec, in v, in _o, px, pz, 8);
                float st = ShapeField.MaxStep(in _spec, in v, in _o, px, pz, 8, c.height);
                int row = n - 1 - z;
                top[row * n + x] = DetailField.TopMaterial(in _spec, in c, st, in _o, (int)px, (int)pz);
                int rel = math.clamp(math.max(c.height, c.water) - _spec.seaLevelY + 1000, 0, 65535);
                hgt[(row * n + x) * 2] = (byte)(rel & 0xFF);
                hgt[(row * n + x) * 2 + 1] = (byte)(rel >> 8);
            }
            WriteGrid("top.bin", n, n, top);
            File.WriteAllBytes(Path.Combine(Dir(), "top_height.bin"), hgt);

            float2 pk = _map.PeakPosition;
            int peakTop = S((int)pk.x, (int)pk.y).height;
            Section("peak.bin", (int)pk.x - 4000, (int)pk.y, 2000, peakTop + 200, (peakTop + 200 - (_spec.seaLevelY - 800)) / 4, 4);
            // Surface to floor, through the cavern layer and the underworld.
            int cx = _spec.worldSizeVoxels / 2 + 3000, cz = _spec.worldSizeVoxels / 2 - 2000;
            int surf = S(cx, cz).height;
            Section("deep.bin", cx - 3000, cz, 1500, surf + 200, (surf + 200 - _spec.worldBottomY) / 4, 4);
            // A steep flank of the peak, voxel scale.
            Section("cliff.bin", (int)pk.x - 2600, (int)pk.y, 700, S((int)pk.x - 2300, (int)pk.y).height + 150, 450, 1);
            UnityEngine.Debug.Log("[PaletteSamples] wrote Logs/TerrainGen/palette/*.bin");
        }
    }
}
