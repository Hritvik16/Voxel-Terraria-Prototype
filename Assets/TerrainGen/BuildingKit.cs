// Assets/TerrainGen/BuildingKit.cs
//
// Task 184 (placement plan step 3, first stage; owner 2026-11-06, batch 2 W1 (a):
// "a MODULAR KIT first"). The voxel building parts and three house shapes made
// from them. THE KIT ONLY: nothing here is placed in the world yet. Placement
// needs the town / village sites of surface-rebuild step B7, which was never
// built (WorldRecord's `site` byte keeps ids >= 16 for it, no list exists), so
// task 184 stopped for the owner there (tools/overnight/HANDOFF.md).
//
// A building is a pure function of (shape, rotation, biome, origin, floor
// height, foundation depth): Role() says which PART a voxel is, Material()
// which GenMat that part is made of. No tables, no state, integers only, so a
// later ColumnJob / ShellColumnJob / PointVoxel overlay (the Trees.cs pattern)
// can ask it per voxel from Burst and every consumer agrees.
//
// PARTS (voxels are 0.1 m; local x runs along the ridge, local z across it,
// y from the floor, front wall = low z):
//   FOUNDATION  stone under the whole wall plan, from the floor down to the
//               ground (foundDepth voxels; placement measures it; no stilts or
//               terraces: stage 2)
//   FLOOR       one voxel at y = 0 inside the walls
//   WALL        the T-thick wall ring, y 0..H, plus the gable triangles under
//               the roof at both ends; HOUSE also has one partition wall
//   POST        3 x 3 corner posts, y 0..H; HALL also has wall posts every
//               the thirds of its long walls (between the openings)
//   DOOR GAP    an opening (AIR) in a wall, from y 1; door + its frame
//   WINDOW      an opening (AIR) with a sill above the floor
//   FRAME       the wall voxels ringing a door or window (the jambs, lintel,
//               sill / threshold), always wood
//   ROOF        a pitched (45 deg) gable roof, RT thick, ridge along x,
//               overhanging OH (eaves) and OHX (gable ends)
//   AIR         inside the walls and under the roof: an EMPTY room (no
//               interiors beyond the room + door, task scope); placement
//               carves it, so ground inside a building becomes air
//   NONE        not part of the building: keep whatever the terrain has
//
// SHAPES (outer wall plan, wall height; openings in BuildingKit.Opening):
//   HUT    4 x 4 m,   2.4 m walls: 1 room, a door, 3 windows
//   HOUSE  9 x 5 m,   2.6 m walls: 2 rooms (5 m + 4 m) joined by an inner
//          door, a front door, 5 windows
//   HALL   13 x 7 m,  3.6 m walls: 1 big room, a double door (2 m x 2.6 m),
//          7 windows, wall posts at the thirds of the long walls
//
// STYLE BY BIOME (rule, from the biome's own palette; no new GenMat, so every
// material already has its FarField.GenToEngine entry):
//   wood   the biome's tree wood (Trees.cs): forest ForestWood, snow PineWood,
//          jungle JungleWood, savanna / desert / badlands AcaciaWood (the dry
//          biomes' only timber tree)
//   stone  the biome's own rock: forest Limestone, desert CreamSandstone, snow
//          Basalt, jungle KarstLimestone, savanna KopjeGranite, badlands
//          CreamBand
//   walls  timber where the biome is wooded (forest, snow, jungle) for a hut
//          or house; adobe (RedEarth) in the savanna; stone in desert and
//          badlands, and for every HALL
//   roof   thatch (SavannaGrass, the golden dry grass) in forest huts, jungle
//          and savanna; shingle in forest houses / halls (Slate) and in the
//          snow (BlueSlate); fired-clay tiles (MaroonMudstone) in desert and
//          badlands
//   floor  planks (wood) under timber walls, else the stone
//
// FOOTPRINT. Each placed building's integer cell box + id (BuildingFootprint)
// is what the world record will hold for NPCs (brief: "NPCs react" needs to
// know which voxels belong to a town). The record is never saved (a pure
// function of the seed), so this is no save-format change.
//
// ROTATION. rot 0-3 quarter turns; the front (door) faces -z, +x, +z, -x.
// origin is the world voxel of the rotated box's min corner (roof overhang
// included) at floor height.
using Unity.Mathematics;

namespace TerrainGen
{
    public struct Building
    {
        public int id;           // world-record id (sites take ids >= 16)
        public byte shape;       // BuildingKit.HUT / HOUSE / HALL
        public byte rot;         // 0-3 quarter turns
        public byte biome;       // GenBiome: picks the palette
        public int3 origin;      // (x, floor y, z) of the rotated box's min corner
        public int foundDepth;   // voxels of foundation under the floor (0..FOUND_MAX)
    }

    /// An integer voxel box (min inclusive, max exclusive) + the building's id.
    public struct BuildingFootprint
    {
        public int id;
        public byte shape;
        public int3 min, max;
        public bool Contains(int3 v) => math.all(v >= min) && math.all(v < max);
    }

    public static class BuildingKit
    {
        public const byte HUT = 0, HOUSE = 1, HALL = 2;
        public const int SHAPES = 3;

        // Roles (parts).
        public const byte NONE = 0, AIR = 1, FOUNDATION = 2, FLOOR = 3, WALL = 4, POST = 5, FRAME = 6, ROOF = 7;
        public const int ROLES = 8;

        public const int T = 2;           // wall thickness
        public const int POST_W = 3;      // corner / wall post width
        public const int RT = 2;          // roof thickness (vertical)
        public const int OH = 4;          // eave overhang (z)
        public const int OHX = 3;         // gable overhang (x)
        /// Deepest foundation the kit builds (4 m). A site needing more is
        /// stage 2 (stilts / terraces); placement must reject it.
        public const int FOUND_MAX = 40;

        /// Wall plan SX (along the ridge) x SZ, wall height H, partition x (-1 none).
        public static void Dims(int shape, out int sx, out int sz, out int h, out int part)
        {
            switch (shape)
            {
                case HUT:   sx = 40;  sz = 40; h = 24; part = -1; return;
                case HOUSE: sx = 90;  sz = 50; h = 26; part = 50; return;
                default:    sx = 130; sz = 70; h = 36; part = -1; return;
            }
        }

        public static int OpeningCount(int shape) => shape == HUT ? 4 : shape == HOUSE ? 7 : 8;

        /// Opening i of a shape: wall side (0 front z-low, 1 back z-high, 2 left
        /// x-low, 3 right x-high, 4 the partition), centre along the wall
        /// (x for sides 0/1, z for 2-4), width, sill y, height, door?
        public static void Opening(int shape, int i, out int side, out int c, out int w, out int y0, out int h, out bool door)
        {
            door = false;
            if (shape == HUT)
            {
                w = 8; y0 = 10; h = 8;
                switch (i)
                {
                    case 0: side = 0; c = 20; w = 10; y0 = 1; h = 21; door = true; return;
                    case 1: side = 1; c = 20; return;
                    case 2: side = 2; c = 20; return;
                    default: side = 3; c = 20; return;
                }
            }
            if (shape == HOUSE)
            {
                w = 8; y0 = 10; h = 9;
                switch (i)
                {
                    case 0: side = 0; c = 25; w = 10; y0 = 1; h = 21; door = true; return;
                    case 1: side = 4; c = 25; w = 10; y0 = 1; h = 21; door = true; return;
                    case 2: side = 0; c = 70; return;
                    case 3: side = 1; c = 25; return;
                    case 4: side = 1; c = 70; return;
                    case 5: side = 2; c = 25; return;
                    default: side = 3; c = 25; return;
                }
            }
            w = 10; y0 = 14; h = 12;
            switch (i)
            {
                case 0: side = 0; c = 65; w = 20; y0 = 1; h = 26; door = true; return;
                case 1: side = 0; c = 25; return;
                case 2: side = 0; c = 105; return;
                case 3: side = 1; c = 25; return;
                case 4: side = 1; c = 65; return;
                case 5: side = 1; c = 105; return;
                case 6: side = 2; c = 35; return;
                default: side = 3; c = 35; return;
            }
        }

        /// The unrotated box incl. overhang: x span, z span, top (exclusive, from the floor).
        public static int3 LocalBox(int shape)
        {
            Dims(shape, out int sx, out int sz, out int h, out _);
            int ridge = h + 1 + ((sz - 1) >> 1) + RT;   // u at the ridge + RT
            return new int3(sx + 2 * OHX, ridge, sz + 2 * OH);
        }

        /// The rotated box: (x span, top, z span).
        public static int3 Size(int shape, int rot)
        {
            int3 b = LocalBox(shape);
            return (rot & 1) == 0 ? b : new int3(b.z, b.y, b.x);
        }

        /// The front (door side) as a world direction: -z, +x, +z, -x.
        public static int2 FrontDir(int rot)
        {
            switch (rot & 3)
            {
                case 0: return new int2(0, -1);
                case 1: return new int2(1, 0);
                case 2: return new int2(0, 1);
                default: return new int2(-1, 0);
            }
        }

        public static BuildingFootprint Footprint(in Building b)
        {
            int3 s = Size(b.shape, b.rot);
            return new BuildingFootprint
            {
                id = b.id, shape = b.shape,
                min = new int3(b.origin.x, b.origin.y - b.foundDepth, b.origin.z),
                max = new int3(b.origin.x + s.x, b.origin.y + s.y, b.origin.z + s.z),
            };
        }

        /// World voxel -> role.
        public static byte Role(in Building b, int3 v)
        {
            int3 d = v - b.origin;
            if (d.y < -b.foundDepth) return NONE;
            LocalOf(b.shape, b.rot, d.x, d.z, out int lx, out int lz);
            return RoleLocal(b.shape, lx, d.y, lz, false);
        }

        /// World voxel -> GenMat, or Air for AIR; `isPart` false = keep the terrain.
        public static byte Voxel(in Building b, int3 v, out bool isPart)
        {
            byte r = Role(in b, v);
            isPart = r != NONE;
            return Material(r, b.biome, b.shape);
        }

        /// (dx, dz) inside the rotated box -> wall-plan local (lx, lz); the
        /// overhang makes lx < 0 / lz < 0 possible. Out-of-box maps out of range.
        public static void LocalOf(int shape, int rot, int dx, int dz, out int lx, out int lz)
        {
            int3 b = LocalBox(shape);
            int w0 = b.x, d0 = b.z, bx, bz;
            switch (rot & 3)
            {
                case 0: bx = dx; bz = dz; break;
                case 1: bx = dz; bz = d0 - 1 - dx; break;
                case 2: bx = w0 - 1 - dx; bz = d0 - 1 - dz; break;
                default: bx = w0 - 1 - dz; bz = dx; break;
            }
            lx = bx - OHX; lz = bz - OH;
        }

        /// The part at wall-plan local (lx, ly, lz). `closed` treats every
        /// opening as wall (the tests' seal check).
        public static byte RoleLocal(int shape, int lx, int ly, int lz, bool closed)
        {
            Dims(shape, out int sx, out int sz, out int h, out int part);
            if (lx < -OHX || lx >= sx + OHX || lz < -OH || lz >= sz + OH) return NONE;
            int e = math.min(lz, sz - 1 - lz);          // < 0 under the eaves
            int u = h + 1 + e;                           // roof underside
            if (ly >= u + RT) return NONE;
            if (ly >= u) return ROOF;
            bool inPlan = lx >= 0 && lx < sx && lz >= 0 && lz < sz;
            if (!inPlan) return NONE;
            if (ly < 0) return ly >= -FOUND_MAX ? FOUNDATION : NONE;

            bool ringX = lx < T || lx >= sx - T, ringZ = lz < T || lz >= sz - T;
            if (ly > h) return ringX ? WALL : AIR;      // gable triangles / the open roof space

            if ((lx < POST_W || lx >= sx - POST_W) && (lz < POST_W || lz >= sz - POST_W)) return POST;

            int side = -1, along = 0;
            if (lz < T) { side = 0; along = lx; }
            else if (lz >= sz - T) { side = 1; along = lx; }
            else if (lx < T) { side = 2; along = lz; }
            else if (lx >= sx - T) { side = 3; along = lz; }
            else if (part >= 0 && lx >= part && lx < part + T) { side = 4; along = lz; }
            if (side < 0) return ly == 0 ? FLOOR : AIR;

            bool frame = false;
            int n = OpeningCount(shape);
            for (int i = 0; i < n; i++)
            {
                Opening(shape, i, out int s, out int c, out int w, out int y0, out int oh, out _);
                if (s != side) continue;
                int a0 = c - (w >> 1), a1 = a0 + w;
                if (along >= a0 && along < a1 && ly >= y0 && ly < y0 + oh) return closed ? WALL : AIR;
                if (along >= a0 - 1 && along < a1 + 1 && ly >= y0 - 1 && ly <= y0 + oh) frame = true;
            }
            if (frame) return FRAME;
            if (shape == HALL && side <= 1 && (along * 3) % sx < POST_W * 3) return POST;   // posts at the thirds
            return WALL;
        }

        // ------------------------------------------------------------------
        // Materials by biome (rule; header)
        // ------------------------------------------------------------------
        public static byte Wood(byte biome)
        {
            switch (biome)
            {
                case GenBiome.Forest: return GenMat.ForestWood;
                case GenBiome.Snow: return GenMat.PineWood;
                case GenBiome.Jungle: return GenMat.JungleWood;
                default: return GenMat.AcaciaWood;
            }
        }

        public static byte Stone(byte biome)
        {
            switch (biome)
            {
                case GenBiome.Forest: return GenMat.Limestone;
                case GenBiome.Desert: return GenMat.CreamSandstone;
                case GenBiome.Snow: return GenMat.Basalt;
                case GenBiome.Jungle: return GenMat.KarstLimestone;
                case GenBiome.Savanna: return GenMat.KopjeGranite;
                default: return GenMat.CreamBand;
            }
        }

        public static bool TimberWalls(byte biome, int shape) =>
            shape != HALL && (biome == GenBiome.Forest || biome == GenBiome.Snow || biome == GenBiome.Jungle);

        public static byte WallMat(byte biome, int shape)
        {
            if (TimberWalls(biome, shape)) return Wood(biome);
            if (biome == GenBiome.Savanna && shape != HALL) return GenMat.RedEarth;   // adobe
            return Stone(biome);
        }

        public static byte RoofMat(byte biome, int shape)
        {
            switch (biome)
            {
                case GenBiome.Forest: return shape == HUT ? GenMat.SavannaGrass : GenMat.Slate;
                case GenBiome.Snow: return GenMat.BlueSlate;
                case GenBiome.Desert:
                case GenBiome.Badlands: return GenMat.MaroonMudstone;
                default: return GenMat.SavannaGrass;   // jungle, savanna: thatch
            }
        }

        public static byte Material(byte role, byte biome, int shape)
        {
            switch (role)
            {
                case FOUNDATION: return Stone(biome);
                case FLOOR: return TimberWalls(biome, shape) ? Wood(biome) : Stone(biome);
                case WALL: return WallMat(biome, shape);
                case POST:
                case FRAME: return Wood(biome);
                case ROOF: return RoofMat(biome, shape);
                default: return GenMat.Air;   // AIR and NONE
            }
        }
    }
}
