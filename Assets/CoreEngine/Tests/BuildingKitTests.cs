// Assets/CoreEngine/Tests/BuildingKitTests.cs
//
// Task 184: the town building kit (TerrainGen/BuildingKit.cs). Structure checks
// on every shape x rotation x biome (sealed room, door a player passes, inner
// door joins the HOUSE's rooms, roof over every wall-plan column, no floating
// voxel, footprint tight, rotations agree, every material a solid, static,
// coloured engine material) and the kit sheets for the owner.
// Writes Logs/TerrainGen/kit184/kit.txt and Screenshots/184/*.png.
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using TerrainGen;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Rendering;

public class BuildingKitTests
{
    private static readonly string ShotDir = Path.Combine("Screenshots", "184");
    private static readonly string LogDir = Path.Combine("Logs", "TerrainGen", "kit184");
    private static readonly byte[] Biomes =
        { GenBiome.Forest, GenBiome.Snow, GenBiome.Jungle, GenBiome.Savanna, GenBiome.Desert, GenBiome.Badlands };
    private static readonly string[] BiomeNames = { "forest", "desert", "snow", "jungle", "savanna", "badlands" };
    private static readonly string[] ShapeNames = { "hut", "house", "hall" };

    // Player body (PlayerConfig): 0.6 m wide, 1.8 m tall.
    private const int BODY_W = 6, BODY_H = 18;

    private static void LocalRange(int shape, out int3 lo, out int3 hi)
    {
        BuildingKit.Dims(shape, out int sx, out int sz, out _, out _);
        int top = BuildingKit.LocalBox(shape).y;
        lo = new int3(-BuildingKit.OHX - 2, -3, -BuildingKit.OH - 2);
        hi = new int3(sx + BuildingKit.OHX + 2, top + 2, sz + BuildingKit.OH + 2);   // exclusive
    }

    /// 6-connected flood over voxels with pass(role, ly); returns the reached set.
    private static HashSet<int3> Flood(int shape, int3 start, bool closed, System.Func<byte, int, bool> pass, int yLo, int yHi)
    {
        LocalRange(shape, out int3 lo, out int3 hi);
        var seen = new HashSet<int3> { start };
        var q = new Queue<int3>(); q.Enqueue(start);
        var steps = new[] { new int3(1, 0, 0), new int3(-1, 0, 0), new int3(0, 1, 0), new int3(0, -1, 0), new int3(0, 0, 1), new int3(0, 0, -1) };
        while (q.Count > 0)
        {
            int3 p = q.Dequeue();
            foreach (var s in steps)
            {
                int3 n = p + s;
                if (math.any(n < lo) || math.any(n >= hi) || n.y < yLo || n.y >= yHi || seen.Contains(n)) continue;
                if (!pass(BuildingKit.RoleLocal(shape, n.x, n.y, n.z, closed), n.y)) continue;
                seen.Add(n); q.Enqueue(n);
            }
        }
        return seen;
    }

    private static bool Open(byte r, int ly) => r == BuildingKit.AIR || r == BuildingKit.NONE;

    [Test]
    public void RoomIsSealedExceptItsOpenings()
    {
        for (int shape = 0; shape < BuildingKit.SHAPES; shape++)
        {
            BuildingKit.Dims(shape, out int sx, out int sz, out _, out _);
            var start = new int3(sx / 4, 5, sz / 2);
            Assert.AreEqual(BuildingKit.AIR, BuildingKit.RoleLocal(shape, start.x, start.y, start.z, false), ShapeNames[shape]);
            LocalRange(shape, out int3 lo, out int3 hi);
            var closed = Flood(shape, start, true, Open, lo.y, hi.y);
            foreach (var v in closed)
                Assert.AreNotEqual(BuildingKit.NONE, BuildingKit.RoleLocal(shape, v.x, v.y, v.z, true),
                                   $"{ShapeNames[shape]}: the closed room leaks at {v}");
            var open = Flood(shape, start, false, Open, lo.y, hi.y);
            bool outside = false;
            foreach (var v in open) if (BuildingKit.RoleLocal(shape, v.x, v.y, v.z, false) == BuildingKit.NONE) { outside = true; break; }
            Assert.IsTrue(outside, $"{ShapeNames[shape]}: no way out through the openings");
        }
    }

    [Test]
    public void DoorsFitThePlayerAndJoinEveryRoom()
    {
        for (int shape = 0; shape < BuildingKit.SHAPES; shape++)
        {
            BuildingKit.Dims(shape, out int sx, out int sz, out int h, out int part);
            int doors = 0;
            for (int i = 0; i < BuildingKit.OpeningCount(shape); i++)
            {
                BuildingKit.Opening(shape, i, out int side, out int c, out int w, out int y0, out int oh, out bool door);
                Assert.Less(y0 + oh, h + 1, $"{ShapeNames[shape]} opening {i} cuts the wall top");
                if (!door) { Assert.GreaterOrEqual(y0, 5, "a window has a sill"); continue; }
                doors++;
                Assert.AreEqual(1, y0, "a door starts at the floor");
                Assert.GreaterOrEqual(w, BODY_W + 2, $"{ShapeNames[shape]} door {i} narrower than a player + margin");
                Assert.GreaterOrEqual(oh, BODY_H + 1, $"{ShapeNames[shape]} door {i} lower than a player");
            }
            Assert.AreEqual(shape == BuildingKit.HOUSE ? 2 : 1, doors);

            // Walking: a player column (feet at y 1, BODY_H tall) moves from outside, through the
            // front door, into every room; windows (sills) never let it through.
            var outside = new int2(shape == BuildingKit.HOUSE ? 25 : sx / 2, -BuildingKit.OH - 1);
            var reach = Walk(shape, outside, -1);
            var rooms = new List<int2> { new int2(part > 0 ? part / 2 : sx / 2, sz / 2) };
            if (part > 0) rooms.Add(new int2((part + sx) / 2, sz / 2));
            foreach (var r in rooms) Assert.IsTrue(reach.Contains(r), $"{ShapeNames[shape]}: room at {r} not reached from the front door");
            if (part > 0)
                Assert.IsFalse(Walk(shape, outside, part).Contains(rooms[1]), "HOUSE: room B reached with its inner door shut");
            // Negative control: with every opening shut, no room is reached.
            Assert.IsFalse(Walk(shape, outside, -2).Contains(rooms[0]), $"{ShapeNames[shape]}: room reached with every opening shut");
        }
    }

    /// 4-connected walk over (x, z) columns open from y 1 to BODY_H. shut: -1 none,
    /// -2 every opening, >= 0 the partition at that x (its door shut).
    private static HashSet<int2> Walk(int shape, int2 start, int shut)
    {
        LocalRange(shape, out int3 lo, out int3 hi);
        System.Func<int2, bool> free = c =>
        {
            bool closed = shut == -2 || (shut >= 0 && c.x >= shut && c.x < shut + BuildingKit.T);
            for (int y = 1; y <= BODY_H; y++) if (!Open(BuildingKit.RoleLocal(shape, c.x, y, c.y, closed), y)) return false;
            return true;
        };
        var seen = new HashSet<int2> { start };
        var q = new Queue<int2>(); q.Enqueue(start);
        var steps = new[] { new int2(1, 0), new int2(-1, 0), new int2(0, 1), new int2(0, -1) };
        while (q.Count > 0)
        {
            int2 p = q.Dequeue();
            foreach (var st in steps)
            {
                int2 n = p + st;
                if (n.x < lo.x || n.x >= hi.x || n.y < lo.z || n.y >= hi.z || seen.Contains(n) || !free(n)) continue;
                seen.Add(n); q.Enqueue(n);
            }
        }
        return seen;
    }

    [Test]
    public void RoofCoversEveryColumnAndNothingFloats()
    {
        for (int shape = 0; shape < BuildingKit.SHAPES; shape++)
        {
            BuildingKit.Dims(shape, out int sx, out int sz, out _, out _);
            LocalRange(shape, out int3 lo, out int3 hi);
            for (int lz = 0; lz < sz; lz++)
            for (int lx = 0; lx < sx; lx++)
            {
                bool roof = false;
                for (int ly = 0; ly < hi.y; ly++) if (BuildingKit.RoleLocal(shape, lx, ly, lz, false) == BuildingKit.ROOF) { roof = true; break; }
                Assert.IsTrue(roof, $"{ShapeNames[shape]}: no roof over ({lx}, {lz})");
            }

            // Every solid part connects to the foundation (foundation 2 deep here).
            const int depth = 2;
            System.Func<int3, bool> solid = v =>
            {
                if (v.y < -depth) return false;
                byte r = BuildingKit.RoleLocal(shape, v.x, v.y, v.z, false);
                return r != BuildingKit.NONE && r != BuildingKit.AIR;
            };
            int total = 0;
            for (int y = lo.y; y < hi.y; y++) for (int z = lo.z; z < hi.z; z++) for (int x = lo.x; x < hi.x; x++)
                if (solid(new int3(x, y, z))) total++;
            var start = new int3(0, -depth, 0);
            Assert.IsTrue(solid(start));
            var seen = new HashSet<int3> { start };
            var q = new Queue<int3>(); q.Enqueue(start);
            var steps = new[] { new int3(1, 0, 0), new int3(-1, 0, 0), new int3(0, 1, 0), new int3(0, -1, 0), new int3(0, 0, 1), new int3(0, 0, -1) };
            while (q.Count > 0)
            {
                int3 p = q.Dequeue();
                foreach (var s in steps)
                {
                    int3 n = p + s;
                    if (math.any(n < lo) || math.any(n >= hi) || seen.Contains(n) || !solid(n)) continue;
                    seen.Add(n); q.Enqueue(n);
                }
            }
            Assert.AreEqual(total, seen.Count, $"{ShapeNames[shape]}: {total - seen.Count} solid voxels not joined to the foundation");
        }
    }

    [Test]
    public void RotationsAgreeAndFootprintIsTight()
    {
        for (int shape = 0; shape < BuildingKit.SHAPES; shape++)
        {
            int[] baseCounts = null;
            for (int rot = 0; rot < 4; rot++)
            {
                var b = new Building { id = 16 + rot, shape = (byte)shape, rot = (byte)rot, biome = GenBiome.Forest,
                                       origin = new int3(1000, 500, 2000), foundDepth = 7 };
                var fp = BuildingKit.Footprint(in b);
                var counts = new int[BuildingKit.ROLES];
                int3 lo = fp.min - 1, hi = fp.max + 1;
                int2 doorSum = 0; int doorN = 0;
                bool[] face = new bool[6];
                for (int y = lo.y; y < hi.y; y++) for (int z = lo.z; z < hi.z; z++) for (int x = lo.x; x < hi.x; x++)
                {
                    var v = new int3(x, y, z);
                    byte r = BuildingKit.Role(in b, v);
                    counts[r] += fp.Contains(v) ? 1 : 0;
                    if (r == BuildingKit.NONE) continue;
                    Assert.IsTrue(fp.Contains(v), $"{ShapeNames[shape]} rot {rot}: part {r} at {v} outside its footprint");
                    if (x == fp.min.x) face[0] = true; if (x == fp.max.x - 1) face[1] = true;
                    if (y == fp.min.y) face[2] = true; if (y == fp.max.y - 1) face[3] = true;
                    if (z == fp.min.z) face[4] = true; if (z == fp.max.z - 1) face[5] = true;
                    // the front door's opening voxels (AIR at y 1 in the wall ring)
                    int3 d = v - b.origin;
                    BuildingKit.LocalOf(shape, rot, d.x, d.z, out int lx, out int lz);
                    if (r == BuildingKit.AIR && d.y == 1 && lz >= 0 && lz < BuildingKit.T) { doorSum += d.xz; doorN++; }
                }
                for (int f = 0; f < 6; f++) Assert.IsTrue(face[f], $"{ShapeNames[shape]} rot {rot}: footprint face {f} not touched (box not tight)");
                if (baseCounts == null) baseCounts = counts;
                else CollectionAssert.AreEqual(baseCounts, counts, $"{ShapeNames[shape]} rot {rot}: part counts differ from rot 0");
                Assert.Greater(doorN, 0);
                int3 size = BuildingKit.Size(shape, rot);
                float2 rel = (float2)doorSum / doorN - new float2(size.x - 1, size.z - 1) * 0.5f;
                int2 dir = BuildingKit.FrontDir(rot);
                Assert.Greater(math.dot(rel, (float2)dir), 0f, $"{ShapeNames[shape]} rot {rot}: the door is not on the FrontDir side");
            }
        }
    }

    [Test]
    public void EveryMaterialIsASolidStaticColouredEngineMaterial()
    {
        foreach (byte biome in Biomes)
        for (int shape = 0; shape < BuildingKit.SHAPES; shape++)
        for (byte role = 0; role < BuildingKit.ROLES; role++)
        {
            byte g = BuildingKit.Material(role, biome, shape);
            if (role == BuildingKit.NONE || role == BuildingKit.AIR) { Assert.AreEqual(GenMat.Air, g); continue; }
            string what = $"{BiomeNames[biome]} {ShapeNames[shape]} role {role} GenMat {g}";
            Assert.IsTrue(GenMat.IsSolid(g), what + ": not solid");
            Assert.Less(g, 96, what + ": no GenToEngine entry");
            byte e = FarField.GenToEngine(g);
            Assert.AreEqual(Materials.FromTg2(g), e, what);
            Assert.IsFalse(MaterialRules.IsFallingSolidMaterial(e), what + ": falls");
            Assert.IsFalse(MaterialRules.IsFluidMaterial(e), what + ": fluid");
            var c = MaterialPalette.Of(e);
            Assert.IsFalse(c.R == MaterialPalette.Missing.R && c.G == MaterialPalette.Missing.G && c.B == MaterialPalette.Missing.B, what + ": no colour");
        }
    }

    // ------------------------------------------------------------------
    // Report + kit sheets (cheap: < 1 s)
    // ------------------------------------------------------------------
    [Test]
    public void KitReportAndSheets()
    {
        Directory.CreateDirectory(ShotDir); Directory.CreateDirectory(LogDir);
        var sb = new StringBuilder("Task 184 building kit (voxels are 0.1 m; foundation 0.2 m deep in these counts)\n");
        for (int shape = 0; shape < BuildingKit.SHAPES; shape++)
        {
            BuildingKit.Dims(shape, out int sx, out int sz, out int h, out _);
            int3 box = BuildingKit.LocalBox(shape);
            var counts = new int[BuildingKit.ROLES];
            LocalRange(shape, out int3 lo, out int3 hi);
            for (int y = -2; y < hi.y; y++) for (int z = lo.z; z < hi.z; z++) for (int x = lo.x; x < hi.x; x++)
                counts[BuildingKit.RoleLocal(shape, x, y, z, false)]++;
            int solid = counts[2] + counts[3] + counts[4] + counts[5] + counts[6] + counts[7];
            sb.AppendLine($"{ShapeNames[shape],-6} walls {sx / 10f:F1} x {sz / 10f:F1} m, wall {h / 10f:F1} m, box incl. roof {box.x / 10f:F1} x {box.z / 10f:F1} x {box.y / 10f:F1} m, " +
                          $"openings {BuildingKit.OpeningCount(shape)}; solid voxels {solid:N0} (foundation {counts[2]:N0}, floor {counts[3]:N0}, wall {counts[4]:N0}, " +
                          $"post {counts[5]:N0}, frame {counts[6]:N0}, roof {counts[7]:N0}), air {counts[1]:N0}");
        }
        sb.AppendLine("materials by biome (wall / roof / wood / stone):");
        foreach (byte b in Biomes)
            for (int shape = 0; shape < BuildingKit.SHAPES; shape++)
                sb.AppendLine($"  {BiomeNames[b],-9} {ShapeNames[shape],-6} {BuildingKit.WallMat(b, shape),3} / {BuildingKit.RoofMat(b, shape),3} / {BuildingKit.Wood(b),3} / {BuildingKit.Stone(b),3}");
        File.WriteAllText(Path.Combine(LogDir, "kit.txt"), sb.ToString());
        Debug.Log(sb.ToString());

        // Sheet: rows = shapes, columns = biomes; and a roofless cutaway (forest, desert).
        Sheet(Path.Combine(ShotDir, "kit_sheet.png"), Biomes, false, 1);
        Sheet(Path.Combine(ShotDir, "kit_cutaway.png"), new[] { GenBiome.Forest, GenBiome.Desert }, true, 2);
        Assert.IsTrue(File.Exists(Path.Combine(ShotDir, "kit_sheet.png")));
    }

    private const int TW = 560, TH = 460;   // tile (pixels at scale 1)

    private static void Sheet(string path, byte[] biomes, bool roofless, int scale)
    {
        int cols = biomes.Length, rows = BuildingKit.SHAPES;
        int W = TW * cols, H = TH * rows;
        var px = new Color32[W * H];
        for (int i = 0; i < px.Length; i++) px[i] = new Color32(205, 220, 235, 255);
        for (int r = 0; r < rows; r++)
        for (int c = 0; c < cols; c++)
        {
            var tile = RenderIso(r, biomes[c], roofless);
            for (int y = 0; y < TH; y++) for (int x = 0; x < TW; x++)
                if (tile[y * TW + x].a != 0) px[((rows - 1 - r) * TH + y) * W + c * TW + x] = tile[y * TW + x];
        }
        var big = new Color32[W * scale * H * scale];
        for (int y = 0; y < H * scale; y++) for (int x = 0; x < W * scale; x++) big[y * W * scale + x] = px[(y / scale) * W + x / scale];
        var t = new Texture2D(W * scale, H * scale, TextureFormat.RGBA32, false);
        t.SetPixels32(big); t.Apply();
        File.WriteAllBytes(path, t.EncodeToPNG());
        Object.DestroyImmediate(t);
    }

    /// 2:1 isometric, camera at (-x, +y, -z) looking at the front-left corner;
    /// a grass plane at y < -2, the foundation 0.2 m (y -2..-1). Row 0 of the
    /// returned array is the BOTTOM of the image (Texture2D order).
    private static Color32[] RenderIso(int shape, byte biome, bool roofless)
    {
        BuildingKit.Dims(shape, out int sx, out int sz, out int h, out _);
        LocalRange(shape, out int3 lo, out int3 hi);
        const int M = 12;   // grass margin
        int x0 = lo.x - M, x1 = hi.x + M, z0 = lo.z - M, z1 = hi.z + M;
        int y0 = -4, y1 = hi.y + 1, NX = x1 - x0 + 2, NY = y1 - y0, NZ = z1 - z0 + 2;
        var grid = new byte[NX * NY * NZ];
        for (int y = y0; y < y1; y++) for (int z = z0 - 1; z <= z1; z++) for (int x = x0 - 1; x <= x1; x++)
        {
            byte g;
            if (y < -2) g = y == -3 && x >= x0 && x < x1 && z >= z0 && z < z1 ? GenMat.Grass : GenMat.Air;
            else
            {
                byte r = BuildingKit.RoleLocal(shape, x, y, z, false);
                g = (roofless && (r == BuildingKit.ROOF || y > h)) || r == BuildingKit.NONE ? GenMat.Air : BuildingKit.Material(r, biome, shape);
            }
            grid[((y - y0) * NZ + (z - z0 + 1)) * NX + (x - x0 + 1)] = g;
        }
        System.Func<int, int, int, byte> mat = (x, y, z) =>
            y < y0 || y >= y1 ? GenMat.Air : grid[((y - y0) * NZ + (z - z0 + 1)) * NX + (x - x0 + 1)];
        var col = new Color32[TW * TH];
        var depth = new int[TW * TH];
        for (int i = 0; i < depth.Length; i++) depth[i] = int.MinValue;
        int cx = TW / 2 - ((sx - sz) * 2) / 2, cy = 60;   // screen origin (image y up)
        for (int y = -3; y < hi.y; y++)
        for (int z = z0; z < z1; z++)
        for (int x = x0; x < x1; x++)
        {
            byte g = mat(x, y, z);
            if (g == GenMat.Air) continue;
            bool top = mat(x, y + 1, z) == GenMat.Air, fx = mat(x - 1, y, z) == GenMat.Air, fz = mat(x, y, z - 1) == GenMat.Air;
            if (!top && !fx && !fz) continue;
            int px = cx + (x - z) * 2, py = cy + (x + z) + 2 * y;   // image y up: far and high go up
            int n = y - x - z;
            var c = MaterialPalette.Of(FarField.GenToEngine(g));
            for (int dy = 0; dy < 4; dy++)
            for (int dx = 0; dx < 4; dx++)
            {
                int X = px - 2 + dx, Y = py - 2 + dy;
                if (X < 0 || X >= TW || Y < 0 || Y >= TH) continue;
                int k = Y * TW + X;
                if (n < depth[k]) continue;
                float s = dy >= 2 ? (top ? 1.0f : 0.55f) : dx < 2 ? (fx ? 0.62f : 0.5f) : (fz ? 0.8f : 0.5f);
                depth[k] = n;
                col[k] = new Color32((byte)math.clamp(c.R * s * 330f, 0, 255), (byte)math.clamp(c.G * s * 330f, 0, 255), (byte)math.clamp(c.B * s * 330f, 0, 255), 255);
            }
        }
        return col;
    }
}
