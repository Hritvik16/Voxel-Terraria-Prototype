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
//      least MIN_SEP apart; relaxed passes if the strict one seats too few,
//      then (task 37d) an EDGE pass that may touch a feature margin or the
//      beach, and takes windows above the predicted lowest third last.
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
        public static readonly float2[] RELAX = { Relax(0), Relax(1), Relax(2), Relax(3) };
        public const int RELAX_N = 4;
        /// RELAX[i] as code (Burst reads no managed array).
        public static float2 Relax(int i) => i == 0 ? new float2(0.35f, 1f) : i == 1 ? new float2(0.5f, 1f) : i == 2 ? new float2(0.7f, 0.9f) : new float2(0.85f, 0.8f);
        /// Score bonus (voxels of score per voxel of distance) for distance from
        /// the sites already seated, counted up to SPREAD_CAP: 2 km is worth
        /// 100 m of height. Plan rule 8 (B8) joins easy areas by routes, so they
        /// are meant to lie apart.
        public const float SPREAD = 0.05f;
        public const float SPREAD_CAP = 30000f;
        /// Task 37d: the lowest-third line is a third of the world's OWN highest
        /// land, so on a low world (no great peak: seeds 1, 12, 123 top out near
        /// 300 m) it sits under LEVEL_MAX's 100 m. Pick predicts it as THIRD_SAFE
        /// of a third of the starting shapes' highest land (the final line was
        /// 0.75-0.94 of that on the 20 sweep seeds), caps the press level there,
        /// and charges HIGH_COST per voxel a window's pressed height stands over
        /// it; such a window waits for the EDGE_PASS. On a world with a peak the
        /// line is far above 100 m: no change.
        public const float THIRD_SAFE = 0.7f, HIGH_COST = 10f;
        /// Task 37d: the EDGE pass, last, run only when the RELAX passes seated
        /// too few sites: a window may touch a feature's footprint margin
        /// (Features.MarkBad) on up to .z of its cells and `bad` (the beach, the
        /// sea) on up to .w, each such cell costing like non-gentle land;
        /// non-gentle share and spacing as RELAX's .x and .y. Mark still never
        /// flags a feature or sea cell. (B5's footprints left seed 65537 three
        /// sites of six and seed 11 a fourth on 82% non-gentle land; seed 12,
        /// a low world, has its lowest-third gentle land only near the shore.)
        public static readonly float4 EDGE_PASS = EdgePass();
        public static float4 EdgePass() => new float4(0.5f, 0.8f, 0.35f, 0.15f);
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
        /// featureBad (optional): feature footprints, kept out of the RELAX
        /// passes like `bad` and allowed in part in the EDGE_PASS only.
        /// Burst (engine plan step 1): MapArena pointers; the sites are appended
        /// to `sites` (cleared first), the diagnostics go to `dg` (LastPick is
        /// formatted from them by MapJobs).
        public static unsafe void Pick(MapArena* A, Diag* dg, in TerrainSpec spec, int m, float cm, float* hc, float* gentle, bool* bad, bool* featureBad, ref PList<Site> sites)
        {
            int mark = A->Mark();
            float4 edge = EdgePass();
            sites.Clear();
            // Predicted lakes: the starting shape's own depressions (Hydrology's
            // priority flood). Erosion moves them a little, but a hollow this
            // deep before shaping is still a lake after it.
            float* spill = A->Alloc<float>(m * m);
            Hydrology.Flood(A, m, hc, 0f, spill, A->Alloc<int>(m * m), A->Alloc<int>(m * m));
            // summed-area tables, (m+1)^2
            int s = m + 1;
            int* nBad = A->Alloc<int>(s * s); int* nRough = A->Alloc<int>(s * s); int* nWet = A->Alloc<int>(s * s); int* nFeat = A->Alloc<int>(s * s);
            float top = 0f;
            for (int i = 0; i < m * m; i++) top = math.max(top, hc[i]);
            float third = THIRD_SAFE * top / 3f, levelMax = math.min(LEVEL_MAX, third);
            double* sH = A->Alloc<double>(s * s); double* sH2 = A->Alloc<double>(s * s);
            for (int z = 0; z < m; z++)
            for (int x = 0; x < m; x++)
            {
                int i = z * m + x, a = (z + 1) * s + x + 1;
                double h = hc[i];
                nBad[a] = (bad[i] ? 1 : 0) + nBad[a - 1] + nBad[a - s] - nBad[a - s - 1];
                nRough[a] = (gentle[i] < 0.5f ? 1 : 0) + nRough[a - 1] + nRough[a - s] - nRough[a - s - 1];
                nWet[a] = (spill[i] - hc[i] > WET_DEPTH ? 1 : 0) + nWet[a - 1] + nWet[a - s] - nWet[a - s - 1];
                nFeat[a] = (featureBad != null && featureBad[i] ? 1 : 0) + nFeat[a - 1] + nFeat[a - s] - nFeat[a - s - 1];
                sH[a] = h + sH[a - 1] + sH[a - s] - sH[a - s - 1];
                sH2[a] = h * h + sH2[a - 1] + sH2[a - s] - sH2[a - s - 1];
            }
            int r = (int)(WINDOW / cm), cellsW = (2 * r + 1) * (2 * r + 1);
            int count = CountFor(in spec) + SPARE;
            var cand = new PList<Cand>(A, 1024, true);
            int tried = 0, offBad = 0, offFeat = 0;
            for (int z = r; z < m - r; z += 2)
            for (int x = r; x < m - r; x += 2)
            {
                int x0 = x - r, z0 = z - r, x1 = x + r + 1, z1 = z + r + 1;
                int A0 = z0 * s + x0, B = z0 * s + x1, C = z1 * s + x0, D = z1 * s + x1;
                tried++;
                float eb = (nBad[D] - nBad[B] - nBad[C] + nBad[A0]) / (float)cellsW;
                if (eb > 0f && (featureBad == null || eb > edge.w)) { offBad++; continue; }
                float fb = (nFeat[D] - nFeat[B] - nFeat[C] + nFeat[A0]) / (float)cellsW;
                if (fb > edge.z) { offFeat++; continue; }
                float rough = (nRough[D] - nRough[B] - nRough[C] + nRough[A0]) / (float)cellsW;
                if (rough > Relax(RELAX_N - 1).x) continue;
                float wet = (nWet[D] - nWet[B] - nWet[C] + nWet[A0]) / (float)cellsW;
                if (wet > WET_MAX) continue;
                double mean = (sH[D] - sH[B] - sH[C] + sH[A0]) / cellsW;
                double vv = (sH2[D] - sH2[B] - sH2[C] + sH2[A0]) / cellsW - mean * mean;
                float sd = (float)math.sqrt(math.max(0.0, vv));
                // the ring between the window and one twice its size
                int X0 = math.max(0, x - 2 * r), Z0 = math.max(0, z - 2 * r), X1 = math.min(m, x + 2 * r + 1), Z1 = math.min(m, z + 2 * r + 1);
                double big = sH[Z1 * s + X1] - sH[Z0 * s + X1] - sH[Z1 * s + X0] + sH[Z0 * s + X0];
                int bigN = (X1 - X0) * (Z1 - Z0);
                double ring = (big - mean * cellsW) / (bigN - cellsW);
                float basin = (float)math.max(0.0, ring - mean);
                float pressed = (float)mean <= levelMax ? (float)mean : levelMax + KEEP * ((float)mean - levelMax);
                float high = math.max(0f, pressed - third);
                cand.Add(new Cand
                {
                    score = (float)mean + sd + NON_GENTLE_COST * (rough + wet + fb + eb) + BASIN_COST * basin + HIGH_COST * high,
                    x = x, z = z, mean = (float)mean, rough = rough, late = fb + eb > 0f || (high > 0f && featureBad != null),
                });
            }
            // Greedy: each step seats the candidate with the lowest score less a
            // bonus for distance from the sites already seated (SPREAD), so the
            // areas spread over the island instead of filling one lowland. Ties
            // keep grid order.
            int passes = RELAX_N + (featureBad != null ? 1 : 0);
            int* perPass = stackalloc int[RELAX_N + 1];
            for (int q = 0; q <= RELAX_N; q++) perPass[q] = 0;
            for (int pass = 0; pass < passes && sites.Length < count; pass++)
            {
                bool edgePass = pass == RELAX_N;
                float maxRough = edgePass ? edge.x : Relax(pass).x, sep = MIN_SEP * (edgePass ? edge.y : Relax(pass).y);
                while (sites.Length < count)
                {
                    int best = -1; float bestCost = float.MaxValue; float2 bestP = default;
                    for (int k = 0; k < cand.Length; k++)
                    {
                        Cand c = cand[k];
                        if (c.rough > maxRough || (c.late && !edgePass)) continue;
                        float2 p = new float2((c.x + 0.5f) * cm, (c.z + 0.5f) * cm);
                        float dmin = SPREAD_CAP;
                        for (int t = 0; t < sites.Length; t++) dmin = math.min(dmin, math.distance(sites[t].centre, p));
                        if (dmin < sep) continue;
                        float cost = c.score - SPREAD * dmin;
                        if (cost < bestCost) { bestCost = cost; best = k; bestP = p; }
                    }
                    if (best < 0) break;
                    sites.Add(new Site { centre = bestP, level = math.clamp(cand[best].mean, LEVEL_MIN, math.max(LEVEL_MIN, levelMax)) });
                    perPass[pass]++;
                }
            }
            if (dg != null)
            {
                dg->pickRan = true; dg->pickTried = tried; dg->pickOffBad = offBad; dg->pickOffFeat = offFeat; dg->pickCand = cand.Length;
                dg->pickSites = sites.Length; dg->pickCount = count; dg->pickPasses = passes; dg->pickFeatureBad = featureBad != null; dg->pickThird = third;
                for (int q = 0; q < passes; q++) dg->pickPerPass[q] = perPass[q];
            }
            A->Release(mark);
        }

        private struct Cand { public float score; public int x, z; public float mean, rough; public bool late; }

        /// How fully p is pressed (1 inside the core, 0 past the fade) and by
        /// which site. 0 and -1 when sites is empty or p is outside every area.
        public static unsafe float Weight(Site[] sites, in Offsets o, float2 p, out int site)
        {
            site = -1;
            if (sites == null || sites.Length == 0) return 0f;
            fixed (Site* s = sites) return Weight(s, sites.Length, in o, p, out site);
        }

        public static unsafe float Weight(Site* sites, int count, in Offsets o, float2 p, out int site)
        {
            site = -1;
            if (sites == null || count == 0) return 0f;
            float best = 0f;
            float bend = EDGE_WOBBLE * CORE * Noise.Fbm(p / 3000f + o.valley + 61f, 2);
            for (int k = 0; k < count; k++)
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

        /// The record's easy area for Burst (EasyArea with its cells as a pointer).
        public unsafe struct EasyAreaB
        {
            public int2 centre;
            public int cellCount;
            public float hectares, radiusM, meanHeightM;
            public int* cells;
            public int cellsLen;
        }

        /// Flags the easy cells in the record and fills its area list (Burst;
        /// LastMark is formatted from `dg` by MapJobs).
        public static unsafe void Mark(MapArena* A, Diag* dg, ref MapData map, ref RecordB rec)
        {
            if (!map.hasEasy) return;
            Site* sites = map.easySites; int ns = map.easyCount;
            var spec = map.spec;
            int n = spec.mapCells, cv = spec.mapCellVoxels, N = n * n;
            float* h = map.height;
            float hi = 0f; int land = 0;
            for (int i = 0; i < N; i++) if (h[i] > 0f) { land++; hi = math.max(hi, h[i]); }
            float lowThird = hi / 3f;
            int count = CountFor(in spec);
            int want = (int)math.round(SHARE * land / count);
            int g = (int)math.ceil(GROW / cv);
            int mark = A->Mark();
            byte* owner = A->Alloc<byte>(N);   // 0 = free, else area index + 1
            IntPtr* sorted = A->Alloc<IntPtr>(ns); int* sortedLen = A->Alloc<int>(ns);
            int* took = A->Alloc<int>(ns);
            long nn = (long)n * n;
            int* fails = stackalloc int[7];
            if (dg != null) { dg->markRan = true; dg->markSites = ns; }
            // candidates per area, nearest first (sites are >= MIN_SEP apart, so
            // the discs overlap little; a cell goes to the first area that takes it)
            for (int k = 0; k < ns; k++)
            {
                int cx = (int)math.floor(sites[k].centre.x / cv), cz = (int)math.floor(sites[k].centre.y / cv);
                var keys = new PList<long>(A, 4096, true);
                for (int f = 0; f < 7; f++) fails[f] = 0;
                for (int z = math.max(0, cz - g); z <= math.min(n - 1, cz + g); z++)
                for (int x = math.max(0, cx - g); x <= math.min(n - 1, cx + g); x++)
                {
                    int dx = x - cx, dz = z - cz, d2 = dx * dx + dz * dz;
                    if (d2 > g * g) continue;
                    int i = z * n + x;
                    int f = Fails(ref map, i, lowThird);
                    if (f == 0 && rec.data[i].IsFeature) f = 6;   // B5: never on a signature feature
                    fails[f]++;
                    if (f == 0) keys.Add(d2 * nn + i);
                }
                PSort.Longs(keys.Ptr, keys.Length);
                sorted[k] = (IntPtr)keys.Ptr; sortedLen[k] = keys.Length;
                if (dg != null && k < 8) for (int f = 0; f < 7; f++) dg->markFails[k * 7 + f] = fails[f];
            }
            // the `count` sites with the most easy ground become the areas (in
            // seeding order); the spares stay pressed flat but unflagged
            int* rank = A->Alloc<int>(ns);
            for (int k = 0; k < ns; k++) rank[k] = k;
            for (int i = 1; i < ns; i++)
            {
                int v = rank[i], j = i - 1;
                while (j >= 0 && (sortedLen[v] != sortedLen[rank[j]] ? sortedLen[v] > sortedLen[rank[j]] : v < rank[j])) { rank[j + 1] = rank[j]; j--; }
                rank[j + 1] = v;
            }
            int selN = math.min(count, ns);
            int* sel = A->Alloc<int>(math.max(1, selN));
            for (int q = 0; q < selN; q++) sel[q] = rank[q];
            PSort.Ints(sel, selN);
            // Round-robin growth, one cell per area per round, nearest first, so
            // land two areas both reach splits by nearness. Pass 0: each area
            // up to its share. Pass 1: areas with cells to spare make up what
            // the short ones missed (each up to SLACK more).
            int* cursor = A->Alloc<int>(ns);
            int all = want * count;
            Grow(want, ref all, sel, selN, cursor, sorted, sortedLen, owner, took, nn);
            int deficit = want * count;
            for (int q = 0; q < selN; q++) deficit -= took[sel[q]];
            Grow((int)(want * (1f + SLACK)), ref deficit, sel, selN, cursor, sorted, sortedLen, owner, took, nn);
            float cellM = cv / 10f;
            rec.easy = A->Root<EasyAreaB>(math.max(1, selN)); rec.easyCount = selN;
            for (int q = 0; q < selN; q++)
            {
                int k = sel[q];
                long* sk = (long*)sorted[k];
                int* cells = A->Root<int>(took[k]);
                double sumH = 0; long far2 = 0; int j = 0;
                for (int c = 0; c < cursor[k]; c++)
                {
                    int i = (int)(sk[c] % nn);
                    if (owner[i] != k + 1) continue;   // the first area to reach a cell owns it
                    cells[j++] = i;
                    rec.data[i].flags |= WorldRecord.FLAG_EASY;
                    sumH += h[i];
                    far2 = sk[c] / nn;
                }
                rec.easy[q] = new EasyAreaB
                {
                    centre = new int2((int)sites[k].centre.x, (int)sites[k].centre.y),
                    cellCount = j,
                    hectares = j * cellM * cellM / 10000f,
                    radiusM = math.sqrt((float)far2) * cellM,
                    meanHeightM = j > 0 ? (float)(sumH / j) / 10f : 0f,
                    cells = cells, cellsLen = took[k],
                };
            }
            if (dg != null) { dg->markKeptN = math.min(selN, 8); for (int q = 0; q < selN && q < 8; q++) dg->markKept[q] = sel[q]; }
            A->Release(mark);
        }

        private static unsafe bool Next(int k, int* cursor, IntPtr* sorted, int* sortedLen, byte* owner, int* took, long nn)
        {
            long* sk = (long*)sorted[k];
            while (cursor[k] < sortedLen[k])
            {
                int i = (int)(sk[cursor[k]++] % nn);
                if (owner[i] != 0) continue;
                owner[i] = (byte)(k + 1); took[k]++;
                return true;
            }
            return false;
        }

        private static unsafe void Grow(int quota, ref int budget, int* sel, int selN, int* cursor, IntPtr* sorted, int* sortedLen, byte* owner, int* took, long nn)
        {
            for (bool any = true; any && budget > 0;)
            {
                any = false;
                for (int q = 0; q < selN; q++)
                {
                    int k = sel[q];
                    if (budget > 0 && took[k] < quota && Next(k, cursor, sorted, sortedLen, owner, took, nn)) { any = true; budget--; }
                }
            }
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

        public static unsafe int Fails(ref MapData map, int i, float lowThird)
        {
            float* h = map.height;
            if (h[i] <= 0f) return 1;
            if (h[i] >= lowThird) return 2;
            byte reg = map.region[i];
            if (reg == 255 || Regions.ClassOf(reg) != Regions.Gentle) return 3;
            if (map.waterClass[i] != Hydrology.DRY) return 4;
            return Danger.SlopeTan2(h, map.spec.mapCells, map.spec.mapCellVoxels, i) < WALK_TAN2 ? 0 : 5;
        }
    }
}
