// Assets/TerrainGen/EasyAreas.cs
//
// EASY AREAS (map v4, surface rebuild B4, plan rule 3; owner Q1 (a), Q2 (a)).
// Several easy areas are SEEDED IN THE LAYOUT BEFORE SHAPING -- built, not
// hoped for. Each is a gentle-class patch of land kept in the lowest third of
// the world's own land height range. "Easy" is decided by the LAND only (Q2
// (a)): the biome does not count.
//
//   1. Pick (BuildV3, after the starting shapes, before erosion): on the 16 m
//      erosion grid, the lowest and least rugged windows that lie mostly on
//      gentle-class land (not the wetland basin, which is shaped to hold lakes),
//      off the beach, the peak and the volcano, and are not the floor of a
//      basin (that fills with a lake). Greedy, lowest score first, centres at
//      least MIN_SEP apart; relaxed passes if the strict one seats too few.
//   2. Shape (BuildV3): inside an area the starting relief is pressed toward
//      the area's own mean height (KEEP of it stays, so drainage keeps its
//      direction), its stable slope is a plain's, there is no terracing or
//      dune field, and the fine map's detail amplitude is a wetland's. The
//      press fades out over FADE past an organic (noise-bent) edge.
//   3. Mark (WorldRecord.Build): easy ground = Walk band (8 m map slope under
//      15 deg, PlayMetricsReport's provisional band) AND gentle landform class
//      AND under a third of the highest land AND dry (no lake or river). Each
//      area takes the SHARE / count easy-ground cells nearest its centre and
//      flags them WorldRecord.FLAG_EASY; the list holds centre, hectares and
//      the cells. So the flagged share of land is SHARE whenever every area
//      found its cells -- an area that did not is smaller, and the acceptance
//      (8-12% of land, every area >= 25 ha) fails loudly.
//
// Count per world size (Q1 (a)): Small 3, Medium 4, Large 6. sizeClass 2 (the
// 8 km tg2 island, the only class this assembly builds) is MEDIUM -- the same
// reading as the B5 queue entry (Medium 5 features).
//
// Determinism: +, -, *, /, sqrt and 2-D noise only; integer distances and an
// integer sort key for the cell order (TerrainSpec.cs contract).
using System;
using Unity.Mathematics;

namespace TerrainGen
{
    public struct EasyArea
    {
        public int2 centre;        // voxels (x, z): the seeded centre
        public int cellCount;      // flagged map cells
        public float hectares;
        public float radiusM;      // distance from the centre to the farthest flagged cell
        public float meanHeightM;  // mean map height of the flagged cells, rel. sea
        public int[] cells;        // map cell indices (z * n + x), nearest first
    }

    public static class EasyAreas
    {
        /// Share of land flagged easy, over all areas together (plan rule 3).
        public const float SHARE = 0.10f;
        public const float MIN_HA = 25f;               // acceptance floor per area
        public const float CORE = 4000f;               // voxels: full press inside this radius
        public const float FADE = 3500f;               // voxels: the press fades to nothing over this
        public const float EDGE_WOBBLE = 0.18f;        // of CORE: noise bend of the edge
        public const float MIN_SEP = 2f * CORE + 0.4f * FADE; // centre spacing: cores stay apart
        public const float WINDOW = 3200f;             // voxels: half-size of the square a site is scored on
        public const float GROW = CORE + FADE;         // flagged cells come from within this, nearest first
        public const float COAST_MIN = 0.05f;          // whole window this far inland (off the beach)
        public const float KEEP = 0.2f;                // share of the starting relief left inside
        public const float TALUS = 0.15f;              // stable slope inside (~8.5 deg, under Walk's 15)
        public const float MIN_LAKE = 30f;             // voxels: a hollow inside is a lake from 3 m (a plain's), not 0.6 m (a wetland's)
        /// Score per voxel the surrounding ring stands above the window: a basin
        /// floor fills with a lake, so the flattest LOW ground that is not a pit wins.
        public const float BASIN_COST = 2f;
        /// A starting-shape cell this far (voxels) under its spill level is a
        /// predicted lake; it costs like non-gentle land, and a window more
        /// than WET_MAX predicted lake is never a site.
        public const float WET_DEPTH = 30f;
        public const float WET_MAX = 0.2f;
        /// An area may take up to this much more than its share when another
        /// came up short, so the total stays SHARE.
        public const float SLACK = 0.5f;
        /// Sites seeded beyond the count. All are pressed; Mark keeps the count
        /// with the most easy ground (a site can still flood or come out steep:
        /// its hydrology is not known before shaping).
        public const int SPARE = 2;
        public const float ROUGH = 0.2f;               // a wetland's detail amplitude
        public const float LEVEL_MIN = 150f, LEVEL_MAX = 1000f;   // voxels: 15-100 m
        /// Score per unit of non-gentle window share (voxels): 5% costs 100 m.
        public const float NON_GENTLE_COST = 20000f;
        /// Relax passes: (largest non-gentle window share, MIN_SEP scale).
        public static readonly float2[] RELAX = { new float2(0.35f, 1f), new float2(0.5f, 1f), new float2(0.7f, 0.9f), new float2(0.85f, 0.8f) };
        /// Score bonus (voxels of score per voxel of distance) for distance from
        /// the sites already seated, counted up to SPREAD_CAP: 2 km is worth
        /// 100 m of height. Plan rule 8 (B8) joins easy areas by routes, so they
        /// are meant to lie apart.
        public const float SPREAD = 0.05f;
        public const float SPREAD_CAP = 30000f;
        /// Walk band: tan(15 deg)^2, compared against the squared 8 m gradient (Danger.cs holds the bands).
        public const float WALK_TAN2 = Danger.WALK_TAN2;

        public struct Site { public float2 centre; public float level; }

        /// Diagnostics of the last Pick, like WorldMap.LastErosion.
        public static string LastPick = "";
        /// Diagnostics of the last Mark: per area, why the disc's cells were not
        /// easy ground (first failing test, in IsEasyGround's order).
        public static string LastMark = "";

        /// Q1 (a): Small 3, Medium 4, Large 6. Size by world edge: the 8 km
        /// sizeClass 2 world is Medium.
        public static int CountFor(in TerrainSpec spec) =>
            spec.worldSizeVoxels < 80000 ? 3 : spec.worldSizeVoxels <= 120000 ? 4 : 6;

        /// The sites, from the starting shapes on the m x m erosion grid (cell
        /// cm voxels): hc start height, gentle the gentle-class weight, bad =
        /// sea, the beach, or raised by the peak or the volcano. A window that
        /// touches a bad cell never qualifies. Greedy, lowest score first; if
        /// the strict pass seats fewer than the count, RELAX passes allow more
        /// non-gentle land and closer centres (the sweep shows which pass).
        public static Site[] Pick(in TerrainSpec spec, int m, float cm, float[] hc, float[] gentle, bool[] bad)
        {
            // Predicted lakes: the starting shape's own depressions (Hydrology's
            // priority flood). Erosion moves them a little, but a hollow this
            // deep before shaping is still a lake after it.
            var spill = new float[m * m];
            Hydrology.Flood(m, hc, 0f, spill, new int[m * m], new int[m * m]);
            // summed-area tables, (m+1)^2
            int s = m + 1;
            var nBad = new int[s * s]; var nRough = new int[s * s]; var nWet = new int[s * s];
            var sH = new double[s * s]; var sH2 = new double[s * s];
            for (int z = 0; z < m; z++)
            for (int x = 0; x < m; x++)
            {
                int i = z * m + x, a = (z + 1) * s + x + 1;
                double h = hc[i];
                nBad[a] = (bad[i] ? 1 : 0) + nBad[a - 1] + nBad[a - s] - nBad[a - s - 1];
                nRough[a] = (gentle[i] < 0.5f ? 1 : 0) + nRough[a - 1] + nRough[a - s] - nRough[a - s - 1];
                nWet[a] = (spill[i] - hc[i] > WET_DEPTH ? 1 : 0) + nWet[a - 1] + nWet[a - s] - nWet[a - s - 1];
                sH[a] = h + sH[a - 1] + sH[a - s] - sH[a - s - 1];
                sH2[a] = h * h + sH2[a - 1] + sH2[a - s] - sH2[a - s - 1];
            }
            int r = (int)(WINDOW / cm), cellsW = (2 * r + 1) * (2 * r + 1);
            int count = CountFor(in spec) + SPARE;
            var cand = new System.Collections.Generic.List<(float score, int x, int z, float mean, float rough)>();
            int tried = 0, offBad = 0;
            for (int z = r; z < m - r; z += 2)
            for (int x = r; x < m - r; x += 2)
            {
                int x0 = x - r, z0 = z - r, x1 = x + r + 1, z1 = z + r + 1;
                int A = z0 * s + x0, B = z0 * s + x1, C = z1 * s + x0, D = z1 * s + x1;
                tried++;
                if (nBad[D] - nBad[B] - nBad[C] + nBad[A] > 0) { offBad++; continue; }
                float rough = (nRough[D] - nRough[B] - nRough[C] + nRough[A]) / (float)cellsW;
                if (rough > RELAX[RELAX.Length - 1].x) continue;
                float wet = (nWet[D] - nWet[B] - nWet[C] + nWet[A]) / (float)cellsW;
                if (wet > WET_MAX) continue;
                double mean = (sH[D] - sH[B] - sH[C] + sH[A]) / cellsW;
                double vv = (sH2[D] - sH2[B] - sH2[C] + sH2[A]) / cellsW - mean * mean;
                float sd = (float)math.sqrt(math.max(0.0, vv));
                // the ring between the window and one twice its size
                int X0 = math.max(0, x - 2 * r), Z0 = math.max(0, z - 2 * r), X1 = math.min(m, x + 2 * r + 1), Z1 = math.min(m, z + 2 * r + 1);
                double big = sH[Z1 * s + X1] - sH[Z0 * s + X1] - sH[Z1 * s + X0] + sH[Z0 * s + X0];
                int bigN = (X1 - X0) * (Z1 - Z0);
                double ring = (big - mean * cellsW) / (bigN - cellsW);
                float basin = (float)math.max(0.0, ring - mean);
                cand.Add(((float)mean + sd + NON_GENTLE_COST * (rough + wet) + BASIN_COST * basin, x, z, (float)mean, rough));
            }
            // Greedy: each step seats the candidate with the lowest score less a
            // bonus for distance from the sites already seated (SPREAD), so the
            // areas spread over the island instead of filling one lowland. Ties
            // keep grid order.
            var sites = new System.Collections.Generic.List<Site>();
            var perPass = new int[RELAX.Length];
            for (int pass = 0; pass < RELAX.Length && sites.Count < count; pass++)
            {
                float maxRough = RELAX[pass].x, sep = MIN_SEP * RELAX[pass].y;
                while (sites.Count < count)
                {
                    int best = -1; float bestCost = float.MaxValue; float2 bestP = default;
                    for (int k = 0; k < cand.Count; k++)
                    {
                        var c = cand[k];
                        if (c.rough > maxRough) continue;
                        float2 p = new float2((c.x + 0.5f) * cm, (c.z + 0.5f) * cm);
                        float dmin = SPREAD_CAP;
                        foreach (var t in sites) dmin = math.min(dmin, math.distance(t.centre, p));
                        if (dmin < sep) continue;
                        float cost = c.score - SPREAD * dmin;
                        if (cost < bestCost) { bestCost = cost; best = k; bestP = p; }
                    }
                    if (best < 0) break;
                    sites.Add(new Site { centre = bestP, level = math.clamp(cand[best].mean, LEVEL_MIN, LEVEL_MAX) });
                    perPass[pass]++;
                }
            }
            LastPick = $"windows {tried}: {offBad} touch sea/beach/peak/volcano, {cand.Count} within the loosest non-gentle limit; " +
                       $"sites {sites.Count} of {count} (incl. {SPARE} spare), per relax pass {string.Join("/", perPass)}";
            return sites.ToArray();
        }

        /// How fully p is pressed (1 inside the core, 0 past the fade) and by
        /// which site. 0 and -1 when sites is empty or p is outside every area.
        public static float Weight(Site[] sites, in Offsets o, float2 p, out int site)
        {
            site = -1;
            if (sites == null || sites.Length == 0) return 0f;
            float best = 0f;
            float bend = EDGE_WOBBLE * CORE * Noise.Fbm(p / 3000f + o.valley + 61f, 2);
            for (int k = 0; k < sites.Length; k++)
            {
                float d = math.distance(p, sites[k].centre) + bend;
                if (d >= CORE + FADE) continue;
                float t = 1f - Noise.SmoothStep(CORE, CORE + FADE, d);
                if (t > best) { best = t; site = k; }
            }
            return best;
        }

        /// Starting relief inside an area: pressed toward the site's level.
        public static float Press(float h, float level, float t) => math.lerp(h, level + (h - level) * KEEP, t);

        /// Flags the easy cells in the record and fills its area list.
        public static void Mark(WorldMap map, WorldRecord rec)
        {
            var sites = map.EasySites;
            if (sites == null) return;
            var spec = map.Spec;
            int n = spec.mapCells, cv = spec.mapCellVoxels;
            float[] h = map.Height;
            float hi = 0f; int land = 0;
            for (int i = 0; i < h.Length; i++) if (h[i] > 0f) { land++; hi = math.max(hi, h[i]); }
            float lowThird = hi / 3f;
            int count = CountFor(in spec);
            int want = (int)math.round(SHARE * land / count);
            int g = (int)math.ceil(GROW / cv);
            var owner = new byte[n * n];   // 0 = free, else area index + 1
            var why = new System.Text.StringBuilder();
            var sorted = new long[sites.Length][];
            var took = new int[sites.Length];
            long nn = (long)n * n;
            // candidates per area, nearest first (sites are >= MIN_SEP apart, so
            // the discs overlap little; a cell goes to the first area that takes it)
            for (int k = 0; k < sites.Length; k++)
            {
                int cx = (int)math.floor(sites[k].centre.x / cv), cz = (int)math.floor(sites[k].centre.y / cv);
                var keys = new System.Collections.Generic.List<long>();
                var fails = new int[7];
                for (int z = math.max(0, cz - g); z <= math.min(n - 1, cz + g); z++)
                for (int x = math.max(0, cx - g); x <= math.min(n - 1, cx + g); x++)
                {
                    int dx = x - cx, dz = z - cz, d2 = dx * dx + dz * dz;
                    if (d2 > g * g) continue;
                    int i = z * n + x;
                    int f = Fails(map, i, lowThird);
                    if (f == 0 && rec.Data[i].IsFeature) f = 6;   // B5: never on a signature feature
                    fails[f]++;
                    if (f == 0) keys.Add(d2 * nn + i);
                }
                keys.Sort();
                sorted[k] = keys.ToArray();
                why.Append($"[{k}] easy {fails[0]} sea {fails[1]} high {fails[2]} not-gentle {fails[3]} water {fails[4]} steep {fails[5]} feature {fails[6]}; ");
            }
            // the `count` sites with the most easy ground become the areas (in
            // seeding order); the spares stay pressed flat but unflagged
            var rank = new int[sites.Length];
            for (int k = 0; k < rank.Length; k++) rank[k] = k;
            Array.Sort(rank, (a, b) => sorted[a].Length != sorted[b].Length ? sorted[b].Length.CompareTo(sorted[a].Length) : a.CompareTo(b));
            var sel = new int[math.min(count, sites.Length)];
            Array.Copy(rank, sel, sel.Length);
            Array.Sort(sel);
            var list = new EasyArea[sel.Length];
            // Round-robin growth, one cell per area per round, nearest first, so
            // land two areas both reach splits by nearness. Pass 0: each area
            // up to its share. Pass 1: areas with cells to spare make up what
            // the short ones missed (each up to SLACK more).
            var cursor = new int[sites.Length];
            bool Next(int k)
            {
                while (cursor[k] < sorted[k].Length)
                {
                    int i = (int)(sorted[k][cursor[k]++] % nn);
                    if (owner[i] != 0) continue;
                    owner[i] = (byte)(k + 1); took[k]++;
                    return true;
                }
                return false;
            }
            void Grow(int quota, ref int budget)
            {
                for (bool any = true; any && budget > 0;)
                {
                    any = false;
                    foreach (int k in sel)
                        if (budget > 0 && took[k] < quota && Next(k)) { any = true; budget--; }
                }
            }
            int all = want * count;
            Grow(want, ref all);
            int deficit = want * count;
            foreach (int k in sel) deficit -= took[k];
            Grow((int)(want * (1f + SLACK)), ref deficit);
            float cellM = cv / 10f;
            for (int q = 0; q < sel.Length; q++)
            {
                int k = sel[q];
                var cells = new int[took[k]];
                double sumH = 0; long far2 = 0; int j = 0;
                for (int c = 0; c < cursor[k]; c++)
                {
                    int i = (int)(sorted[k][c] % nn);
                    if (owner[i] != k + 1) continue;   // the first area to reach a cell owns it
                    cells[j++] = i;
                    rec.Data[i].flags |= WorldRecord.FLAG_EASY;
                    sumH += h[i];
                    far2 = sorted[k][c] / nn;
                }
                list[q] = new EasyArea
                {
                    centre = new int2((int)sites[k].centre.x, (int)sites[k].centre.y),
                    cellCount = j,
                    hectares = j * cellM * cellM / 10000f,
                    radiusM = math.sqrt((float)far2) * cellM,
                    meanHeightM = j > 0 ? (float)(sumH / j) / 10f : 0f,
                    cells = cells,
                };
            }
            rec.EasyAreaList = list;
            LastMark = why + $"kept {string.Join(",", sel)}";
        }

        /// Plan rule 3, from the land alone: Walk band at 8 m (central
        /// differences, PlayMetricsReport's formula), gentle landform class,
        /// under a third of the highest land, dry.
        public static bool IsEasyGround(WorldMap map, int i, float lowThird) => Fails(map, i, lowThird) == 0;

        /// 0 = easy ground; else the first test it fails: 1 sea, 2 not in the
        /// lowest third, 3 not gentle, 4 lake or river, 5 steeper than Walk.
        public static int Fails(WorldMap map, int i, float lowThird)
        {
            float[] h = map.Height;
            if (h[i] <= 0f) return 1;
            if (h[i] >= lowThird) return 2;
            byte reg = map.Region[i];
            if (reg == 255 || Regions.ClassOf(reg) != Regions.Gentle) return 3;
            if (map.WaterClass[i] != Hydrology.DRY) return 4;
            return Danger.SlopeTan2(h, map.Spec.mapCells, map.Spec.mapCellVoxels, i) < WALK_TAN2 ? 0 : 5;
        }
    }
}
