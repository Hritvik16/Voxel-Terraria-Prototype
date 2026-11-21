// Assets/TerrainGen/Sites.cs
//
// THE SITE PLANNER (map v4, surface rebuild B7; owner Q-S (a) 2026-11-07, plan
// decisions batch 2 2026-11-06). Where towns, villages, outposts and frontier
// towns go, from data the generator already has -- never a place or a seed
// constant. `-sites 1` (TerrainSpec.sitesOn / Sites), DEFAULT OFF; the planner
// changes NO terrain (the flatten rule is stored, not applied), so OFF and ON
// worlds are byte-identical and only the record's SiteList / site bytes differ.
//
// Inputs (SiteInput, built from the map by FromMap; a test can build one by
// hand): per 8 m cell the integer map height (Height rounded to a voxel), the
// water class, the world record (landform + depth, biome, fresh-water steps,
// danger, movement band, easy / feature / route flags), a blocked mask (the
// start zone = the START_REACH (128 m, the NEAR band of 8.15) disc round both
// start rules' columns (PlayStart.Find mild and not), and the stamp discs Routes
// keeps out of),
// the easy areas' cells and the routes' centre lines.
//
// Integer distance fields (5/7 chamfer in cell units, as WorldRecord): to the
// sea, to a FLAG_ROUTE cell, to a FLAG_FEATURE cell, and to the edge of the
// HARD ground (Volcanic landform or Snow biome).
//
// THE DISC RULE (every kind): a site is a disc of radius R round a cell centre.
// Every cell of it is land (not sea), dry (no lake or river cell) and at least
// one fresh-water step (16 m) from any lake or river cell, not blocked (start
// zone, stamp disc), not on a signature feature and FEATURE_GAP clear of one,
// SEA_GAP clear of the sea, not on another site's disc + SITE_GAP. The disc's
// height range (max - min of the integer map heights) is at most RANGE_MAX of
// its kind; its movement bands are Walk (Scramble allowed only on a frontier,
// at most a quarter of the cells; never Climb or Sheer).
//
// THE KINDS (in placement order; each greedy over an integer score, lowest
// first, ties by cell index):
//   TOWN      R 64 m, range <= 8 m. Centre on an easy area (FLAG_EASY) that is
//             clear of the start disc (the start's own area may hold a town
//             outside it -- a town by the start); at most one town per
//             area; the whole disc easy ground (pass 1) or Walk ground (pass 2,
//             same area, if no fully easy disc fits). Score: range, fresh-water
//             steps, base danger. Count: land / TOWN_LAND_CELLS (a ~6 km island,
//             ~28 km^2 of land, gets 3), capped by the areas that fit.
//             TOWN SPACING (task 190): no town inside another town's fair share
//             of the land (TownSep2: a disc of area land / towns round it). An
//             easy-area town closer than that is dropped; the towns still missing
//             go on Walk ground anywhere (TIER 2: the whole disc Walk, every disc
//             rule, the ground check, within ROUTE_FAR of a route). Where no such
//             ground lies far enough away there are simply fewer towns.
//   FRONTIER  R 48 m, range <= 8 m, Scramble <= 1/4. Centre on HARD ground
//             within FOOT_REACH of its edge (the foot of the volcano, the snow
//             line), not easy. Score: range, fresh water, route distance; HIGHER
//             base danger scores better (it is the frontier). At least one when
//             any hard cell fits; count 1 + towns / 4.
//   VILLAGE   R 32 m, range <= 3 m. Centre within VILLAGE_WATER fresh-water
//             steps (<= ~100 m) and ROUTE_FAR of a route corridor, base danger
//             <= VILLAGE_DANGER. Score: fresh water, route distance, range,
//             danger. Spaced VILLAGE_SEP from any other site centre.
//   OUTPOST   R 16 m, range <= 2 m. Within OUTPOST_WATER steps (<= ~260 m) and
//             ROUTE_FAR of a route; what a village cannot be (danger above
//             VILLAGE_DANGER or water farther than VILLAGE_WATER). Spaced
//             OUTPOST_SEP. Villages + outposts together: land /
//             SETTLE_LAND_CELLS (~10 on a 6 km island); villages take up to 6 in
//             10 of them, outposts the rest (and any villages left unfilled).
//
// "Along the routes": the routes join only the easy areas, which on most
// seeds lie together in one part of the island, so a strict corridor filter
// put every village in that corner. The route distance is therefore a SCORE
// term (ROUTE_W per 100 m) under a ROUTE_FAR cap, and each site records its
// spur to the nearest centre line (THE ROUTE JOIN below).
//
// THE GROUND CHECK (exact, last): the 8 m map does not see the voxel detail,
// cliff relief or canyon terraces, so a disc that passes on the map is measured
// on the real columns (ShapeField.Sample's top solid voxel, every 2 m inside
// the disc) before it is taken: the range must stay within the kind's
// RANGE_MAX + GROUND_SLACK (the detail noise of flat ground, ~2-3 m), and no
// 2 m step inside the disc may be steeper than Scramble (30 deg; a canyon
// terrace's cliff edge is invisible on the map), else the next candidate is
// tried. (Managed float code, like task 183's pocket plane:
// an Editor and an IL2CPP player could disagree by a voxel on a borderline
// disc; not observed.)
//
// Not every biome x landform pair appears in every world: the pair is recorded
// per site, never forced. Fewer sites than the targets is a valid plan (an
// island with little flat ground); a disc is never relaxed onto steep ground.
//
// THE ROUTE JOIN (records only): every site records the nearest route centre
// line (index in RouteList), the integer join point on it and the spur length.
// A town on an easy area is a route END (Routes joins the easy areas); the join
// is that route's nearest point. No route is re-searched and no spur is eased
// in this step, so RouteList and the terrain are unchanged.
//
// THE FLATTEN RULE (stored, NOT applied): pad level = the median integer map
// height of the disc's cells (+ sea level, an absolute voxel Y); inside R the
// ground would be set to it, over a ring of BLEND = R / 2 blended back to the
// natural ground; cutMax / fillMax record what that would cost (voxels).
//
// Determinism: integer heights (math.round of the map's floats, which follow
// the TerrainSpec.cs contract), integer distances, integer scores; no float in
// any decision. A pure function of the map, rebuilt (never saved).
using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace TerrainGen
{
    public struct Site
    {
        public const byte TOWN = 0, VILLAGE = 1, OUTPOST = 2, FRONTIER = 3;
        public int id;              // record site byte: SiteList index + FIRST_ID (16+)
        public byte kind;
        public int2 centre;         // voxels (x, z): a map cell centre
        public int radius;          // voxels
        public int4 flatBox;        // voxels: x0, z0, x1, z1 (half-open), the disc's box
        public int easyArea;        // EasyAreaList index (towns), else -1
        public byte biome, archetype;   // the record pair at the centre
        public int freshWaterM;     // the centre's fresh-water distance (record steps x 16 m)
        public int danger;          // the centre's base danger 0-7
        public int rangeVox;        // disc height range (integer map heights), voxels
        public int walkCells, scrambleCells, discCells;
        public int groundRangeVox;  // the ground check's range (real columns, 2 m grid), voxels; -1 = not measured
        public int groundStepVox;   // the ground check's largest 2 m neighbour step, voxels; -1 = not measured
        public int route;           // nearest route (RouteList index), -1 none
        public int2 joinPoint;      // voxels: the nearest point of that route's centre line
        public int joinDistance;    // voxels: centre -> joinPoint (the spur)
        // flatten rule (stored, not applied)
        public int padLevelY;       // absolute voxel Y (seaLevelY + median map height)
        public int blend;           // voxels: ring past the radius blended back
        public int cutMax, fillMax; // voxels the pad would cut / fill at most
        public int score;

        public static string KindName(byte k) => k == TOWN ? "town" : k == VILLAGE ? "village" : k == OUTPOST ? "outpost" : "frontier";
    }

    /// The planner's input: plain arrays, so a test can build an island by hand.
    public sealed class SiteInput
    {
        public int n, cellVoxels, seaLevelY;
        public int[] h;               // integer map height per cell, voxels rel. sea (<= 0 = sea)
        public byte[] waterClass;     // Hydrology.DRY / LAKE / RIVER
        public RecordCell[] rec;
        public bool[] blocked;        // start zone + stamp discs
        public bool[] start;          // task 190: the start zone alone (null = unknown)
        public int[][] easyCells;     // per easy area: its cell indices
        public float2[][] routeLines; // per route: centre line, voxels
        /// The real ground: top solid voxel Y of column (x, z). Null = no ground
        /// check (a hand-built island has no columns).
        public Func<int, int, int> ground;
    }

    public static class Sites
    {
        public const int FIRST_ID = 16, MAX_SITES = 255 - FIRST_ID + 1;
        // radii and height ranges, voxels (0.1 m)
        public const int TOWN_R = 640, FRONTIER_R = 480, VILLAGE_R = 320, OUTPOST_R = 160;
        public const int TOWN_RANGE = 80, FRONTIER_RANGE = 80, VILLAGE_RANGE = 30, OUTPOST_RANGE = 20;
        // keep-outs, voxels
        public const int SEA_GAP = 320, FEATURE_GAP = 320, SITE_GAP = 320;
        // spacing between centres, voxels
        public const int VILLAGE_SEP = 6000, OUTPOST_SEP = 4000;
        // water, route and danger rules
        public const int VILLAGE_WATER = 6, OUTPOST_WATER = 16;   // record fresh-water steps (16 m)
        public const int ROUTE_FAR = 20000;                         // voxels from a corridor cell (2 km)
        public const int ROUTE_W = 8;                               // score per 100 m from a corridor
        public const int START_REACH = OreRules.START_REACH;        // voxels: 128 m round each start column
        public const int VILLAGE_DANGER = 3;
        public const int FOOT_REACH = 2560;                         // voxels inside the hard ground's edge
        // counts per land area. Owner: a MEDIUM island gets ~3 towns + ~10 villages / outposts; the
        // tg2 island is the medium class (EasyAreas: sizeClass 2 = Medium) with 267-278 k land cells
        // (17.1-17.8 km^2) on the 4 test seeds, so one town per 90 k cells, one settlement per 27 k.
        public const int TOWN_LAND_CELLS = 90000;      // 5.8 km^2 of 8 m cells
        public const int SETTLE_LAND_CELLS = 27000;    // 1.7 km^2
        public const int VILLAGE_SHARE_TENTHS = 6;
        public const int GROUND_SLACK = 30, GROUND_STEP = 20;      // voxels

        private const int ORTHO = 5, DIAG = 7, INF = int.MaxValue / 2;
        public const int REJ_GAP = 0, REJ_OFF = 1, REJ_SEA = 2, REJ_WET = 3, REJ_BLOCKED = 4, REJ_EASY = 5, REJ_BAND = 6, REJ_RANGE = 7, REJ_STEP = 8, REJ_N = 9;
        [ThreadStatic] private static int _why;

        public struct Stats
        {
            public int landCells, townTarget, frontierTarget, settleTarget;
            public int towns, frontiers, villages, outposts;
            public int hardCells, candTown, candFrontier, candVillage, candOutpost, groundRejects, stepRejects;
            /// Task 190: THE TOWN SPACING (voxels; TownSep2), the easy-area towns it
            /// rejected, the tier-2 (Walk ground off the easy areas) candidates and towns.
            public int townSep, sepRejects, candTown2, towns2;
            /// Town discs rejected, by the first rule that failed (REJ_*).
            public int[] townReject;
            public string Line() =>
                $"land {landCells} cells; targets town {townTarget} frontier {frontierTarget} village+outpost {settleTarget}; " +
                $"placed town {towns} (tier 2: {towns2}) frontier {frontiers} village {villages} outpost {outposts}; " +
                $"town spacing {townSep / 10} m (rejected {sepRejects}, tier-2 candidates {candTown2}); " +
                $"candidates town {candTown} frontier {candFrontier} village {candVillage} outpost {candOutpost}; hard cells {hardCells}; ground check rejected {groundRejects} (step rule {stepRejects})" +
                (townReject == null ? "" : $"; town discs rejected: sea/feature gap {townReject[REJ_GAP]}, off map {townReject[REJ_OFF]}, " +
                 $"sea {townReject[REJ_SEA]}, wet {townReject[REJ_WET]}, blocked {townReject[REJ_BLOCKED]}, not easy {townReject[REJ_EASY]}, " +
                 $"band {townReject[REJ_BAND]}, range {townReject[REJ_RANGE]}");
        }

        public static bool IsHard(in RecordCell c) => !c.IsSea && (c.Archetype == Regions.Volcanic || c.Biome == GenBiome.Snow);

        // ------------------------------------------------------------------
        // From the map
        // ------------------------------------------------------------------

        public static SiteInput FromMap(WorldMap map)
        {
            var spec = map.Spec; var rec = map.Record;
            if (rec == null) return null;
            int n = spec.mapCells, N = n * n, cv = spec.mapCellVoxels;
            var inp = new SiteInput
            {
                n = n, cellVoxels = cv, seaLevelY = spec.seaLevelY,
                h = new int[N], waterClass = map.WaterClass, rec = rec.Data,
                blocked = StartDiscs(map),
            };
            inp.start = (bool[])inp.blocked.Clone();
            for (int i = 0; i < N; i++) inp.h[i] = (int)math.round(map.Height[i]);
            // the stamp discs Routes keeps out of (voxel-only steps the map does not see)
            var o = Offsets.Compute(in spec);
            int sc = spec.worldSizeVoxels >> Stamps.CELL_SHIFT;
            for (int gz = 0; gz <= sc; gz++)
            for (int gx = 0; gx <= sc; gx++)
                if (Stamps.CellStamp(in o, gx, gz, out var st) && Routes.StampKeepOut(st.kind) > 0f)
                    BlockDisc(inp, Routes.StampKeepOutCentre(in st), Routes.StampKeepOut(st.kind));
            for (int j = 0; j < 9; j++) BlockDisc(inp, Stamps.PoolCentre(in spec, in o, j), Routes.POOL_KEEP_OUT);
            inp.easyCells = new int[rec.EasyAreaList.Length][];
            for (int a = 0; a < rec.EasyAreaList.Length; a++) inp.easyCells[a] = rec.EasyAreaList[a].cells ?? new int[0];
            inp.routeLines = new float2[rec.RouteList.Length][];
            for (int r = 0; r < rec.RouteList.Length; r++) inp.routeLines[r] = rec.RouteList[r].line ?? new float2[0];
            var view = map.View; var oo = Offsets.For(in spec);
            inp.ground = (x, z) => ShapeField.Sample(in spec, in view, in oo, x, z, 1, false).height;
            return inp;
        }

        /// The start zone for sites: every cell whose nearest point lies within
        /// START_REACH of either start rule's column (PlayStart.Find, mild and
        /// not). FluidPockets.StartZone also blocks the whole start easy area;
        /// a town may sit on that area outside this disc.
        public static bool[] StartDiscs(WorldMap map)
        {
            var spec = map.Spec;
            int n = spec.mapCells;
            var inp = new SiteInput { n = n, cellVoxels = spec.mapCellVoxels, blocked = new bool[n * n] };
            if (map.Record == null) return inp.blocked;
            for (int mild = 0; mild < 2; mild++)
                if (PlayStart.Find(map, out var pick, mild == 1)) BlockDisc(inp, new float2(pick.column.x, pick.column.y), START_REACH);
            return inp.blocked;
        }

        /// Blocks every cell whose nearest point lies within `r` voxels of `c`
        /// (integer test on the rounded centre and radius).
        public static void BlockDisc(SiteInput inp, float2 c, float r)
        {
            int n = inp.n, cv = inp.cellVoxels;
            int px = (int)math.round(c.x), pz = (int)math.round(c.y), rr = (int)math.ceil(r);
            int c0x = math.max(0, (px - rr) / cv), c1x = math.min(n - 1, (px + rr) / cv);
            int c0z = math.max(0, (pz - rr) / cv), c1z = math.min(n - 1, (pz + rr) / cv);
            for (int z = c0z; z <= c1z; z++)
            for (int x = c0x; x <= c1x; x++)
            {
                long qx = math.clamp(px, x * cv, x * cv + cv - 1) - px, qz = math.clamp(pz, z * cv, z * cv + cv - 1) - pz;
                if (qx * qx + qz * qz <= (long)rr * rr) inp.blocked[z * n + x] = true;
            }
        }

        // ------------------------------------------------------------------
        // The plan
        // ------------------------------------------------------------------

        /// spread false (tests / the before map only): task 187's towns, no spacing, no tier 2.
        public static Site[] Plan(SiteInput inp, out Stats st, bool spread = true)
        {
            st = default;
            if (inp == null) return new Site[0];
            int n = inp.n, N = n * n, cv = inp.cellVoxels;
            var rec = inp.rec;
            int unit = ORTHO;   // chamfer units per cell edge

            int land = 0, hard = 0;
            var dSea = new int[N]; var dRoute = new int[N]; var dFeat = new int[N]; var dSoft = new int[N];
            for (int i = 0; i < N; i++)
            {
                bool sea = rec[i].IsSea || inp.h[i] <= 0;
                if (!sea) land++;
                bool hd = !sea && IsHard(in rec[i]);
                if (hd) hard++;
                dSea[i] = sea ? 0 : INF;
                dRoute[i] = rec[i].IsRoute ? 0 : INF;
                dFeat[i] = rec[i].IsFeature ? 0 : INF;
                dSoft[i] = hd ? INF : 0;   // distance into the hard ground from its edge
            }
            Chamfer(dSea, n); Chamfer(dRoute, n); Chamfer(dFeat, n); Chamfer(dSoft, n);
            st.landCells = land; st.hardCells = hard;
            st.townTarget = land > 0 ? math.max(1, (land + TOWN_LAND_CELLS / 2) / TOWN_LAND_CELLS) : 0;
            st.frontierTarget = hard > 0 ? 1 + st.townTarget / 4 : 0;
            st.settleTarget = land > 0 ? math.max(1, (land + SETTLE_LAND_CELLS / 2) / SETTLE_LAND_CELLS) : 0;

            int ToVox(int units) => units >= INF ? INF : (int)((long)units * cv / unit);
            var taken = new bool[N];   // site discs + SITE_GAP
            var sites = new List<Site>();

            // --- towns: one per non-start easy area, best disc first ---
            var townCands = new List<Site>();
            st.townReject = new int[REJ_N];
            if (inp.easyCells != null)
                for (int a = 0; a < inp.easyCells.Length; a++)
                {
                    bool any = false;
                    for (int pass = 0; pass < 2 && !any; pass++)
                    {
                        var list = new List<Site>();
                        foreach (int ci in inp.easyCells[a])
                        {
                            if (!Disc(inp, ci, TOWN_R, Site.TOWN, pass == 0, dSea, dFeat, taken, out var s)) { st.townReject[_why]++; continue; }
                            st.candTown++;
                            s.easyArea = a;
                            s.score = s.rangeVox * 4 + rec[ci].freshWater * 8 + s.danger * 32;
                            list.Add(s);
                        }
                        list.Sort((p, q) => Better(p, q, n, cv) ? -1 : Better(q, p, n, cv) ? 1 : 0);
                        for (int k = 0; k < list.Count && !any; k++)
                        {
                            var s = list[k];
                            if (GroundOk(inp, ref s)) { townCands.Add(s); any = true; }
                            else { st.groundRejects++; if (StepTooSteep(s.groundStepVox)) st.stepRejects++; }
                        }
                    }
                }
            townCands.Sort((p, q) => Better(p, q, n, cv) ? -1 : Better(q, p, n, cv) ? 1 : 0);
            long sep2 = spread ? TownSep2(land, st.townTarget, cv) : 0;
            st.townSep = (int)math.sqrt((double)sep2);
            for (int k = 0; k < townCands.Count && st.towns < st.townTarget; k++)
            {
                var s = townCands[k];
                if (!StillFree(inp, s, taken)) continue;
                if (!FarFromTowns(s.centre, sites, sep2)) { st.sepRejects++; continue; }
                Take(inp, ref s, taken, sites); st.towns++;
            }
            // Task 190, TIER 2: the easy areas lie together on many islands (the
            // routes join only them), so the spacing leaves towns unplaced; the rest
            // go on WALK ground anywhere (every disc rule of a town, the whole disc
            // Walk, never Scramble or steeper; ground check) within ROUTE_FAR of a
            // route corridor and TownSep of every town. None fits = fewer towns.
            if (spread && st.towns < st.townTarget)
            {
                var t2 = new List<Site>();
                for (int i = 0; i < N; i++)
                {
                    var c = rec[i];
                    if (c.IsSea || c.MoveBand != 0 || inp.h[i] <= 0) continue;
                    int rd = ToVox(dRoute[i]);
                    if (rd > ROUTE_FAR) continue;
                    int2 ctr = new int2((i % n) * cv + cv / 2, (i / n) * cv + cv / 2);
                    if (!FarFromTowns(ctr, sites, sep2)) continue;   // cheap: before the disc
                    if (!Disc(inp, i, TOWN_R, Site.TOWN, false, dSea, dFeat, taken, out var s)) continue;
                    st.candTown2++;
                    s.score = s.rangeVox * 4 + c.freshWater * 8 + s.danger * 32 + rd / 1000 * ROUTE_W;
                    t2.Add(s);
                }
                t2.Sort((p, q) => Better(p, q, n, cv) ? -1 : Better(q, p, n, cv) ? 1 : 0);
                foreach (var c2 in t2)
                {
                    if (st.towns >= st.townTarget) break;
                    var s = c2;
                    if (!StillFree(inp, s, taken) || !FarFromTowns(s.centre, sites, sep2)) continue;
                    if (!GroundOk(inp, ref s)) { st.groundRejects++; if (StepTooSteep(s.groundStepVox)) st.stepRejects++; continue; }
                    Take(inp, ref s, taken, sites); st.towns++; st.towns2++;
                }
            }

            // --- frontier: hard ground near its edge ---
            var fr = new List<Site>();
            for (int i = 0; i < N; i++)
            {
                var c = rec[i];
                if (!IsHard(in c) || c.IsEasy || ToVox(dSoft[i]) > FOOT_REACH) continue;
                if (!Disc(inp, i, FRONTIER_R, Site.FRONTIER, false, dSea, dFeat, taken, out var s)) continue;
                st.candFrontier++;
                int rd = math.min(ToVox(dRoute[i]), 20000);
                s.score = s.rangeVox * 4 + c.freshWater * 4 + rd / 40 - s.danger * 40 + s.scrambleCells * 8;
                fr.Add(s);
            }
            Greedy(inp, fr, st.frontierTarget, 0, taken, sites, n, cv, ref st.frontiers, ref st.groundRejects, ref st.stepRejects);

            // --- villages and outposts: near fresh water along the routes ---
            var vil = new List<Site>(); var outp = new List<Site>();
            for (int i = 0; i < N; i++)
            {
                var c = rec[i];
                if (c.IsSea || c.MoveBand != 0 || c.freshWater > OUTPOST_WATER) continue;
                int rd = ToVox(dRoute[i]);
                if (rd > ROUTE_FAR) continue;
                bool village = c.freshWater <= VILLAGE_WATER && c.Danger <= VILLAGE_DANGER;
                if (village && Disc(inp, i, VILLAGE_R, Site.VILLAGE, false, dSea, dFeat, taken, out var sv))
                {
                    st.candVillage++;
                    sv.score = c.freshWater * 16 + rd / 1000 * ROUTE_W + sv.rangeVox * 4 + sv.danger * 24;
                    vil.Add(sv);
                }
                else if (!village && Disc(inp, i, OUTPOST_R, Site.OUTPOST, false, dSea, dFeat, taken, out var so))
                {
                    st.candOutpost++;
                    so.score = c.freshWater * 8 + rd / 1000 * ROUTE_W + so.rangeVox * 4 + so.danger * 8;
                    outp.Add(so);
                }
            }
            int vTarget = st.settleTarget * VILLAGE_SHARE_TENTHS / 10;
            Greedy(inp, vil, vTarget, VILLAGE_SEP, taken, sites, n, cv, ref st.villages, ref st.groundRejects, ref st.stepRejects);
            Greedy(inp, outp, st.settleTarget - st.villages, OUTPOST_SEP, taken, sites, n, cv, ref st.outposts, ref st.groundRejects, ref st.stepRejects);

            // --- ids, route joins ---
            var arr = sites.ToArray();
            for (int k = 0; k < arr.Length; k++)
            {
                arr[k].id = FIRST_ID + k;
                Join(inp, ref arr[k]);
            }
            return arr;
        }

        /// THE TOWN SPACING (task 190, owner 2026-11-12: "spread the towns apart"):
        /// no town lies inside another town's FAIR SHARE of the land -- a disc of
        /// area land / townTarget round it, radius sqrt(land / (pi townTarget)).
        /// Squared, in voxels, integer (pi = 355 / 113): land cells x cell^2 x 113 /
        /// (355 x towns). A ~17 km^2 island with 3 towns: 1.35 km. Derived from the
        /// island alone; no seed or place constant.
        public static long TownSep2(int landCells, int townTarget, int cv)
            => landCells > 0 && townTarget > 0 ? (long)landCells * cv * cv * 113 / (355L * townTarget) : 0;

        private static bool FarFromTowns(int2 c, List<Site> sites, long sep2)
        {
            foreach (var o in sites)
            {
                if (o.kind != Site.TOWN) continue;
                long dx = c.x - o.centre.x, dz = c.y - o.centre.y;
                if (dx * dx + dz * dz < sep2) return false;
            }
            return true;
        }

        /// Lower score first, then lower cell index (centre z, then x).
        private static bool Better(in Site a, in Site b, int n, int cv)
        {
            if (a.score != b.score) return a.score < b.score;
            int ia = (a.centre.y / cv) * n + a.centre.x / cv, ib = (b.centre.y / cv) * n + b.centre.x / cv;
            return ia < ib;
        }

        private static void Greedy(SiteInput inp, List<Site> cands, int target, int sep, bool[] taken, List<Site> sites, int n, int cv, ref int placed, ref int groundRejects, ref int stepRejects)
        {
            if (target <= 0) return;
            cands.Sort((p, q) => Better(p, q, n, cv) ? -1 : Better(q, p, n, cv) ? 1 : 0);
            foreach (var c in cands)
            {
                if (placed >= target || sites.Count >= MAX_SITES) return;
                var s = c;
                if (!StillFree(inp, s, taken)) continue;
                bool farEnough = true;
                foreach (var o in sites)
                {
                    long dx = s.centre.x - o.centre.x, dz = s.centre.y - o.centre.y;
                    long need = math.max(sep, s.radius + o.radius + SITE_GAP);
                    if (dx * dx + dz * dz < need * need) { farEnough = false; break; }
                }
                if (!farEnough) continue;
                if (!GroundOk(inp, ref s)) { groundRejects++; if (StepTooSteep(s.groundStepVox)) stepRejects++; continue; }
                Take(inp, ref s, taken, sites); placed++;
            }
        }

        /// Cells of the disc of radius r round cell ci's centre (cell centres
        /// within r, plus the centre cell).
        public static void DiscCells(int n, int cv, int ci, int r, List<int> outCells)
        {
            outCells.Clear();
            int cx = ci % n, cz = ci / n, rc = r / cv;
            long r2 = (long)r * r;
            for (int dz = -rc; dz <= rc; dz++)
            for (int dx = -rc; dx <= rc; dx++)
            {
                if ((long)dx * dx * cv * cv + (long)dz * dz * cv * cv > r2) continue;
                int x = cx + dx, z = cz + dz;
                if (x < 0 || z < 0 || x >= n || z >= n) { outCells.Add(-1); continue; }
                outCells.Add(z * n + x);
            }
        }

        [ThreadStatic] private static List<int> _cells;

        /// The disc rule for one centre; fills the site's measured fields.
        private static bool Disc(SiteInput inp, int ci, int r, byte kind, bool allEasy, int[] dSea, int[] dFeat, bool[] taken, out Site s)
        {
            s = default;
            int n = inp.n, cv = inp.cellVoxels; var rec = inp.rec;
            // centre-only keep-outs first (cheap)
            if ((long)dSea[ci] * cv / ORTHO < r + SEA_GAP || (long)dFeat[ci] * cv / ORTHO < r + FEATURE_GAP) { _why = REJ_GAP; return false; }
            var cells = _cells ?? (_cells = new List<int>(512));
            DiscCells(n, cv, ci, r, cells);
            int lo = int.MaxValue, hi = int.MinValue, walk = 0, scr = 0;
            foreach (int i in cells)
            {
                if (i < 0) { _why = REJ_OFF; return false; }
                var c = rec[i];
                if (c.IsSea || inp.h[i] <= 0) { _why = REJ_SEA; return false; }
                if (inp.waterClass[i] != Hydrology.DRY || c.freshWater < 1) { _why = REJ_WET; return false; }
                if (inp.blocked[i] || c.IsFeature || taken[i]) { _why = REJ_BLOCKED; return false; }
                if (allEasy && !c.IsEasy) { _why = REJ_EASY; return false; }
                int band = c.MoveBand;
                if (band == 0) walk++;
                else if (band == 1 && kind == Site.FRONTIER) scr++;
                else { _why = REJ_BAND; return false; }
                lo = math.min(lo, inp.h[i]); hi = math.max(hi, inp.h[i]);
            }
            int range = hi - lo;
            int rmax = RangeMax(kind);
            if (range > rmax || scr * 4 > cells.Count) { _why = range > rmax ? REJ_RANGE : REJ_BAND; return false; }
            var hs = new int[cells.Count];
            for (int k = 0; k < cells.Count; k++) hs[k] = inp.h[cells[k]];
            Array.Sort(hs);
            int pad = hs[hs.Length / 2];
            int x = ci % n, z = ci / n;
            int2 ctr = new int2(x * cv + cv / 2, z * cv + cv / 2);
            s = new Site
            {
                kind = kind, centre = ctr, radius = r,
                flatBox = new int4(ctr.x - r, ctr.y - r, ctr.x + r, ctr.y + r),
                easyArea = -1, biome = rec[ci].Biome, archetype = rec[ci].Archetype,
                freshWaterM = rec[ci].FreshWaterM, danger = rec[ci].Danger, rangeVox = range,
                walkCells = walk, scrambleCells = scr, discCells = cells.Count, route = -1,
                padLevelY = inp.seaLevelY + pad, blend = r / 2, cutMax = hi - pad, fillMax = pad - lo,
            };
            return true;
        }

        public static int RangeMax(byte kind) =>
            kind == Site.TOWN ? TOWN_RANGE : kind == Site.FRONTIER ? FRONTIER_RANGE : kind == Site.VILLAGE ? VILLAGE_RANGE : OUTPOST_RANGE;

        /// THE GROUND CHECK: the real columns over the disc (GROUND_STEP grid).
        /// (1) range within RangeMax + GROUND_SLACK; (2) THE STEP RULE: no two
        /// grid neighbours inside the disc differ by more than the Scramble
        /// slope over GROUND_STEP (Danger.SCRAMBLE_DEG 30: 3 d^2 > step^2 is
        /// steeper, tan^2 30 = 1/3, integer). The 8 m map cannot see a canyon
        /// terrace's cliff edge (map range 0, a 6 m wall in the disc); the
        /// detail noise of flat ground (~22 deg on 1 m, less on 2 m) passes.
        /// Records both on the site.
        private static bool GroundOk(SiteInput inp, ref Site s)
        {
            s.groundRangeVox = -1; s.groundStepVox = -1;
            if (inp.ground == null) return true;
            int r = s.radius, m = 2 * r / GROUND_STEP + 1, lo = int.MaxValue, hi = int.MinValue, step = 0;
            var g = new int[m * m];
            for (int b = 0; b < m; b++)
            for (int a = 0; a < m; a++)
            {
                int dx = a * GROUND_STEP - r, dz = b * GROUND_STEP - r;
                if (dx * dx + dz * dz > r * r) { g[b * m + a] = int.MinValue; continue; }
                int v = inp.ground(s.centre.x + dx, s.centre.y + dz);
                g[b * m + a] = v;
                lo = math.min(lo, v); hi = math.max(hi, v);
                if (a > 0 && g[b * m + a - 1] != int.MinValue) step = math.max(step, math.abs(v - g[b * m + a - 1]));
                if (b > 0 && g[(b - 1) * m + a] != int.MinValue) step = math.max(step, math.abs(v - g[(b - 1) * m + a]));
                if (StepTooSteep(step)) { s.groundRangeVox = hi - lo; s.groundStepVox = step; _why = REJ_STEP; return false; }   // first steep step: rejected
            }
            s.groundRangeVox = hi - lo; s.groundStepVox = step;
            return hi - lo <= RangeMax(s.kind) + GROUND_SLACK;
        }

        /// The step rule: a rise of d voxels over GROUND_STEP is steeper than
        /// Scramble (30 deg) when 3 d^2 > GROUND_STEP^2.
        public static bool StepTooSteep(int d) => 3L * d * d > (long)GROUND_STEP * GROUND_STEP;

        /// The disc is still clear of every site taken since it was measured.
        private static bool StillFree(SiteInput inp, in Site s, bool[] taken)
        {
            var cells = _cells ?? (_cells = new List<int>(512));
            int ci = (s.centre.y / inp.cellVoxels) * inp.n + s.centre.x / inp.cellVoxels;
            DiscCells(inp.n, inp.cellVoxels, ci, s.radius, cells);
            foreach (int i in cells) if (i < 0 || taken[i]) return false;
            return true;
        }

        private static void Take(SiteInput inp, ref Site s, bool[] taken, List<Site> sites)
        {
            var cells = _cells ?? (_cells = new List<int>(512));
            int ci = (s.centre.y / inp.cellVoxels) * inp.n + s.centre.x / inp.cellVoxels;
            DiscCells(inp.n, inp.cellVoxels, ci, s.radius + SITE_GAP, cells);
            foreach (int i in cells) if (i >= 0) taken[i] = true;
            sites.Add(s);
        }

        /// Nearest route centre-line point (integer: points rounded to voxels,
        /// each segment sampled every <= 1 cell).
        private static void Join(SiteInput inp, ref Site s)
        {
            if (inp.routeLines == null) return;
            long best = long.MaxValue;
            for (int r = 0; r < inp.routeLines.Length; r++)
            {
                var line = inp.routeLines[r];
                for (int k = 0; k < line.Length; k++)
                {
                    int2 p = new int2((int)math.round(line[k].x), (int)math.round(line[k].y));
                    long dx = p.x - s.centre.x, dz = p.y - s.centre.y, d2 = dx * dx + dz * dz;
                    if (d2 < best) { best = d2; s.route = r; s.joinPoint = p; }
                }
            }
            if (s.route >= 0) s.joinDistance = (int)math.round(math.sqrt((double)best));
        }

        /// Writes site ids into the record's cells (the disc, on cells whose
        /// site byte is free) -- only for a record the caller owns.
        public static void Mark(WorldRecord rec, Site[] sites)
        {
            var cells = new List<int>(512);
            foreach (var s in sites)
            {
                int ci = (s.centre.y / rec.CellVoxels) * rec.Cells + s.centre.x / rec.CellVoxels;
                DiscCells(rec.Cells, rec.CellVoxels, ci, s.radius, cells);
                foreach (int i in cells) if (i >= 0 && rec.Data[i].site == 0) rec.Data[i].site = (byte)s.id;
            }
        }

        /// FNV-1a 64 over every field that decides a plan.
        public static ulong Hash(Site[] sites)
        {
            ulong h = 14695981039346656037UL;
            void H(int v) { h = (h ^ (uint)v) * 1099511628211UL; }
            foreach (var s in sites)
            {
                H(s.id); H(s.kind); H(s.centre.x); H(s.centre.y); H(s.radius); H(s.easyArea); H(s.rangeVox);
                H(s.route); H(s.groundRangeVox); H(s.groundStepVox); H(s.joinPoint.x); H(s.joinPoint.y); H(s.joinDistance); H(s.padLevelY); H(s.score);
            }
            return h;
        }

        // 5/7 chamfer, two passes, integer (WorldRecord's).
        private static void Chamfer(int[] d, int n)
        {
            for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                int i = z * n + x, v = d[i];
                if (x > 0) v = math.min(v, d[i - 1] + ORTHO);
                if (z > 0)
                {
                    v = math.min(v, d[i - n] + ORTHO);
                    if (x > 0) v = math.min(v, d[i - n - 1] + DIAG);
                    if (x < n - 1) v = math.min(v, d[i - n + 1] + DIAG);
                }
                d[i] = v;
            }
            for (int z = n - 1; z >= 0; z--)
            for (int x = n - 1; x >= 0; x--)
            {
                int i = z * n + x, v = d[i];
                if (x < n - 1) v = math.min(v, d[i + 1] + ORTHO);
                if (z < n - 1)
                {
                    v = math.min(v, d[i + n] + ORTHO);
                    if (x < n - 1) v = math.min(v, d[i + n + 1] + DIAG);
                    if (x > 0) v = math.min(v, d[i + n - 1] + DIAG);
                }
                d[i] = v;
            }
        }
    }
}
