// Assets/TerrainGen/MapJob.cs
//
// ENGINE PLAN STEP 1 (owner Q26 (a), 2026-10-09): ONE compiled world-map
// generator path. Every float operation that shapes the world map -- the
// regions, the feature draw and placement, the easy areas, erosion, the fine
// map, hydrology, the world record, the routes, and Offsets.For's drawn
// volcano -- runs inside GenJob, Burst-compiled synchronously (FloatMode.Strict,
// FloatPrecision.High: the chunk jobs' settings) in the Editor and in the
// IL2CPP player alike. Before this, the Editor ran that code as Mono and the
// player as IL2CPP C++, and seed 42 v4's Height differed (task 32c;
// KNOWN_ISSUES "Burst vs the EDITOR's Mono").
//
// The job itself lives in the companion assembly TerrainGen.Burst
// (Assets/TerrainGen/Burst/MapJobRunner.cs), so that THIS assembly keeps its
// portability contract (TerrainSpec.cs: Unity.Mathematics only). It registers
// itself as MapJobs.Runner at load (Editor and player). MapJobs.Execute is the
// job's body: plain C# over pointers, compiled by Burst inside that job.
//
// No managed fallback: the runner records whether the job ran as managed code
// (a [BurstDiscard] probe) and MapJobs.Run THROWS if it did, or if no runner is
// registered -- Burst switched off gives an error, never a silently different
// world.
//
// MapJobs is the managed side: it fills a GenIO, runs the job, and converts the
// pointer results (MapData, RecordB, FeatureB, RouteB, ...) into WorldMap,
// WorldRecord, Feature, Route, EasyArea, and the Last* diagnostic strings.
// What stays managed is copying and formatting only; the measurement helpers
// in Features (VisibleShare, CanyonDepth, ...) read the finished map and are
// never part of it.
using System;
using System.Diagnostics;
using System.Text;
using Unity.Mathematics;

namespace TerrainGen
{
    /// The pointer form of a WorldMap (the Burst build's working state and result).
    public unsafe struct MapData
    {
        public TerrainSpec spec;
        public int n;                                  // spec.mapCells
        public float* height, rough, water, flow;      // n x n
        public byte* waterClass, region;               // region: null on v2
        public int* drainParent, drainOrder;           // v4: the eps flood's tree (task 76), else null
        public ushort* flowDir;                        // v4: Rivers.Profile's flow bits, else null
        public float2 peak;
        public Hydrology.Stats hydro;
        public Erosion.Stats erosion;
        public int beacons, inselbergs;
        public Regions.LayoutB layout; public bool hasLayout;
        public EasyAreas.Site* easySites; public int easyCount; public bool hasEasy;
        public FeatureB* feats; public int featCount; public bool hasFeats;
        public RouteB* routes; public int routeCount; public bool hasRoutes;
        public RecordB record; public bool hasRecord;
        /// WorldMap.DumpErosionInputsTo (tuning seam): the erosion grid's inputs.
        public bool dumpErosion; public int dumpM; public float* dumpH, dumpE, dumpT; public bool* dumpL;
    }

    /// The pointer form of a WorldRecord.
    public unsafe struct RecordB
    {
        public RecordCell* data;
        public int cells, cellVoxels, mapVersion;
        public EasyAreas.EasyAreaB* easy; public int easyCount;
    }

    /// One line of Features.LastPlace, as numbers (Features.EV_*).
    public struct DiagEvent { public int code, a0, a1, a2, a3, a4, a5, a6, a7; public float x, y; }

    /// The build's diagnostics as numbers; MapJobs formats the Last* strings.
    public unsafe struct Diag
    {
        public bool placeRan; public PList<DiagEvent> place;
        public bool pickRan; public int pickTried, pickOffBad, pickOffFeat, pickCand, pickSites, pickCount, pickPasses;
        public bool pickFeatureBad; public float pickThird;
        public fixed int pickPerPass[8];
        public bool markRan; public int markSites, markKeptN;
        public fixed int markFails[56];                // 8 sites x 7 reasons
        public fixed int markKept[8];
        public bool routeRan, routeFew, routeDiagnose;
        public int routeA, routeChosen, routePairs, routeCutCells, routeFillCells;
        public float routeMaxCut, routeMaxFill;
        public float* diagH0, diagLo, diagHi; public bool* diagSkip;
    }

    /// One request to the generator's Burst job (MapJobs.Execute's argument).
    public unsafe struct GenIO
    {
        public int op;
        public MapArena* arena;
        public TerrainSpec spec;
        public uint seed;
        public Offsets offsets;                        // OFFSETS out; REGIONS, LANDCELLS in
        public Danger.TiersB tiers;                    // BUILD, RECORD in
        public MapData map;                            // BUILD out; RECORD in (map.record out)
        public Diag diag;
        public byte* biomes;                           // BIOMES out; RECORD in
        public RecordB easyFrom; public bool hasEasyFrom;
        public byte* kinds; public int kindCount;      // DRAW out (8 slots)
        public Regions.LayoutB layout; public int* area;   // REGIONS out; LANDCELLS in / out
        public int ranManaged;                         // set by the runner: 1 = the job ran as managed code
        public long peakBytes;
    }

    /// The managed side of the one generator path (see the file header).
    public static unsafe class MapJobs
    {
        internal const int OP_BUILD = 1, OP_RECORD = 2, OP_BIOMES = 3, OP_OFFSETS_SPEC = 4, OP_OFFSETS_SEED = 5,
                           OP_DRAW = 6, OP_REGIONS = 7, OP_LANDCELLS = 8;

        /// The Burst job's body (TerrainGen.Burst's GenJob calls it).
        public static void Execute(GenIO* io)
        {
            MapArena* A = io->arena;
            Diag* dg = &io->diag;
            switch (io->op)
            {
                case OP_BUILD:
                    WorldMap.BuildCore(A, dg, in io->tiers, ref io->map);
                    if (A != null) io->peakBytes = A->PeakBytes;
                    break;
                case OP_RECORD:
                {
                    WorldRecord.Build(A, dg, ref io->map, io->biomes, io->hasEasyFrom ? &io->easyFrom : null, in io->tiers, out RecordB rec);
                    io->map.record = rec; io->map.hasRecord = true;
                    break;
                }
                case OP_BIOMES: WorldRecord.BiomePlane(in io->spec, io->biomes); break;
                case OP_OFFSETS_SPEC: io->offsets = Offsets.Compute(in io->spec); break;
                case OP_OFFSETS_SEED: io->offsets = Offsets.Compute(io->seed); break;
                case OP_DRAW: io->kindCount = Features.Draw(in io->spec, io->kinds); break;
                case OP_REGIONS: io->layout = Regions.Build(A, in io->spec, in io->offsets); break;
                case OP_LANDCELLS: Regions.LandCells(in io->spec, in io->offsets, in io->layout, io->area); break;
            }
        }

        /// The last BuildMap's arena high-water mark (bytes): scratch + results.
        public static long LastPeakBytes;

        /// Runs one GenIO through the Burst job; set by TerrainGen.Burst at load
        /// (MapJobRunner). It must set io->ranManaged when the job ran managed.
        public static Action<IntPtr> Runner;

        /// The arena's block (MapArena.Create / Dispose): an OS page allocator
        /// where TerrainGen.Burst has one (macOS), so a freed block leaves the
        /// process; null = Marshal.AllocHGlobal / FreeHGlobal.
        public static Func<long, IntPtr> BlockAlloc;
        public static Action<IntPtr, long> BlockFree;

        private static void Run(GenIO* io)
        {
            if (Runner == null)
                throw new InvalidOperationException(
                    "TerrainGen: no Burst runner registered for the world-map generator (is the TerrainGen.Burst assembly in the project?). " +
                    "The map must come from one compiled path in the Editor and the player (engine plan step 1).");
            Runner((IntPtr)io);
            if (io->arena != null && io->arena->Overflow)
                throw new InvalidOperationException("TerrainGen: the map build ran out of arena memory (MapArena.DEFAULT_BYTES).");
            if (io->ranManaged != 0)
                throw new InvalidOperationException(
                    "TerrainGen: the world-map generator ran as managed code, not Burst (is Burst compilation switched off?). " +
                    "The map must come from one compiled path in the Editor and the player (engine plan step 1).");
        }

        public static WorldMap BuildMap(TerrainSpec spec)
        {
            var a = MapArena.Create();
            try
            {
                GenIO io = default;
                io.op = OP_BUILD; io.arena = &a; io.spec = spec;
                io.tiers = Danger.TiersB.From(Danger.Default);
                io.map.spec = spec; io.map.n = spec.mapCells;
                io.map.dumpErosion = WorldMap.DumpErosionInputsTo != null;
                io.diag.place = new PList<DiagEvent>(&a, 32);
                io.diag.routeDiagnose = Routes.Diagnose;
                var sw = Stopwatch.StartNew();
                Run(&io);
                double ms = sw.Elapsed.TotalMilliseconds;
                LastPeakBytes = io.peakBytes;
                io.map.hydro.ms = ms;   // no clock under Burst: the whole build's time
                var map = WorldMap.FromCore(ref io.map);
                Publish(ref io.diag, map, ms);
                if (io.map.dumpErosion && io.map.dumpH != null) WorldMap.DumpErosion(ref io.map);
                if (spec.mapVersion >= 3) WorldMap.LastErosion = io.map.erosion;
                return map;
            }
            finally { a.Dispose(); }
        }

        public static WorldRecord Record(WorldMap map, byte[] biomes, WorldRecord easyFrom)
        {
            var a = MapArena.Create();
            try
            {
                GenIO io = default;
                io.op = OP_RECORD; io.arena = &a; io.spec = map.Spec;
                io.tiers = Danger.TiersB.From(Danger.Default);
                io.diag.place = new PList<DiagEvent>(&a, 4);
                ref MapData d = ref io.map;
                d.spec = map.Spec; d.n = map.Spec.mapCells;
                d.height = In(&a, map.Height); d.rough = In(&a, map.Rough); d.water = In(&a, map.Water); d.flow = In(&a, map.Flow);
                d.waterClass = In(&a, map.WaterClass); d.region = In(&a, map.Region);
                if (map.FlowDir != null) d.flowDir = In(&a, map.FlowDir);
                d.peak = map.PeakPosition;
                if (map.EasySites != null) { d.easySites = In(&a, map.EasySites); d.easyCount = map.EasySites.Length; d.hasEasy = true; }
                var feats = map.SignatureFeatures;
                if (feats != null)
                {
                    d.feats = a.Root<FeatureB>(math.max(1, feats.Length)); d.featCount = feats.Length; d.hasFeats = true;
                    for (int k = 0; k < feats.Length; k++) d.feats[k] = In(&a, feats[k]);
                }
                if (map.Routes != null)
                {
                    // Routes.Mark reads the corridors only
                    d.routes = a.Root<RouteB>(math.max(1, map.Routes.Length)); d.routeCount = map.Routes.Length; d.hasRoutes = true;
                    for (int k = 0; k < map.Routes.Length; k++) d.routes[k] = new RouteB { corridor = In(&a, map.Routes[k].corridor), corridorLen = map.Routes[k].corridor.Length };
                }
                io.biomes = In(&a, biomes);
                if (easyFrom != null)
                {
                    var list = easyFrom.EasyAreaList;
                    io.hasEasyFrom = true;
                    io.easyFrom.easy = a.Root<EasyAreas.EasyAreaB>(math.max(1, list.Length)); io.easyFrom.easyCount = list.Length;
                    for (int k = 0; k < list.Length; k++) io.easyFrom.easy[k] = In(&a, list[k]);
                }
                Run(&io);
                // Features.Mark sets each feature's cellCount, on the map's own list as before
                if (feats != null) for (int k = 0; k < feats.Length; k++) feats[k].cellCount = io.map.feats[k].cellCount;
                Publish(ref io.diag, null, 0);
                return WorldRecord.FromCore(ref io.map.record, feats, map.Routes, easyFrom?.EasyAreaList);
            }
            finally { a.Dispose(); }
        }

        public static byte[] BiomePlane(in TerrainSpec spec)
        {
            var b = new byte[spec.mapCells * spec.mapCells];
            fixed (byte* p = b)
            {
                GenIO io = default;
                io.op = OP_BIOMES; io.spec = spec; io.biomes = p;
                Run(&io);
            }
            return b;
        }

        public static Offsets OffsetsOf(in TerrainSpec spec)
        {
            GenIO io = default;
            io.op = OP_OFFSETS_SPEC; io.spec = spec;
            Run(&io);
            return io.offsets;
        }

        public static Offsets OffsetsOf(uint seed)
        {
            GenIO io = default;
            io.op = OP_OFFSETS_SEED; io.seed = seed;
            Run(&io);
            return io.offsets;
        }

        public static byte[] Draw(in TerrainSpec spec)
        {
            byte* k = stackalloc byte[8];
            GenIO io = default;
            io.op = OP_DRAW; io.spec = spec; io.kinds = k;
            Run(&io);
            var r = new byte[io.kindCount];
            for (int i = 0; i < r.Length; i++) r[i] = k[i];
            return r;
        }

        public static Regions.Layout RegionsLayout(in TerrainSpec spec, in Offsets o)
        {
            var a = MapArena.Create();
            try
            {
                GenIO io = default;
                io.op = OP_REGIONS; io.arena = &a; io.spec = spec; io.offsets = o;
                Run(&io);
                return Out(in io.layout);
            }
            finally { a.Dispose(); }
        }

        public static int[] LandCells(in TerrainSpec spec, in Offsets o, Regions.Layout L)
        {
            var area = new int[L.Count];
            fixed (float2* s = L.seeds) fixed (byte* k = L.kind) fixed (int* ar = area)
            {
                GenIO io = default;
                io.op = OP_LANDCELLS; io.spec = spec; io.offsets = o; io.area = ar;
                io.layout = new Regions.LayoutB { seeds = s, kind = k, Count = L.Count };
                Run(&io);
            }
            return area;
        }

        // ------------------------------------------------------------------
        // diagnostics: the strings the managed generator used to build
        // ------------------------------------------------------------------
        private static void Publish(ref Diag d, WorldMap map, double ms)
        {
            if (d.placeRan)
            {
                var sb = new StringBuilder();
                for (int i = 0; i < d.place.Length; i++)
                {
                    DiagEvent e = d.place[i];
                    switch (e.code)
                    {
                        case Features.EV_PEAK:
                            sb.Append($"peak: {e.a0} candidates, {(e.a1 != 0 ? $"at ({e.x:F0}, {e.y:F0})" : "NONE")}; ");
                            break;
                        case Features.EV_TOP:
                            sb.Append($"slot {e.a0} {Features.Name((byte)e.a1)}: {e.a2} fit (rejected: low {e.a3} sea {e.a4} geyser {e.a5} path {e.a6} spacing {e.a7}); ");
                            break;
                        case Features.EV_SUBST:
                            sb.Append($"slot {e.a0}: {Features.Name((byte)e.a1)} -> {Features.Name((byte)e.a2)}; ");
                            break;
                        case Features.EV_LATE:
                            sb.Append($"late: {e.a0} of {e.a1} placed, {e.a2} scans, {e.a3} easy checks (seats {e.a4} of {e.a5}){(e.a6 != 0 ? "" : ", INCOMPLETE")}; ");
                            break;
                    }
                }
                Features.LastPlace = sb.ToString();
            }
            if (d.pickRan)
            {
                var pp = new int[d.pickPasses];
                for (int q = 0; q < pp.Length; q++) pp[q] = d.pickPerPass[q];
                float4 edge = EasyAreas.EdgePass();
                EasyAreas.LastPick = $"windows {d.pickTried}: {d.pickOffBad} touch sea/beach/peak/volcano{(d.pickFeatureBad ? $" over {edge.w:P0}" : "")}, {d.pickOffFeat} over {edge.z:P0} feature, {d.pickCand} within the loosest non-gentle limit; " +
                                     $"sites {d.pickSites} of {d.pickCount} (incl. {EasyAreas.SPARE} spare), per relax pass {string.Join("/", pp)}; lowest third predicted {d.pickThird / 10f:F0} m";
            }
            if (d.markRan)
            {
                var sb = new StringBuilder();
                for (int k = 0; k < d.markSites && k < 8; k++)
                    sb.Append($"[{k}] easy {d.markFails[k * 7]} sea {d.markFails[k * 7 + 1]} high {d.markFails[k * 7 + 2]} not-gentle {d.markFails[k * 7 + 3]} " +
                              $"water {d.markFails[k * 7 + 4]} steep {d.markFails[k * 7 + 5]} feature {d.markFails[k * 7 + 6]}; ");
                var kept = new int[d.markKeptN];
                for (int q = 0; q < kept.Length; q++) kept[q] = d.markKept[q];
                EasyAreas.LastMark = sb + $"kept {string.Join(",", kept)}";
            }
            if (d.routeRan)
            {
                string s;
                if (d.routeFew) s = "fewer than two easy areas";
                else
                {
                    var sb = new StringBuilder();
                    sb.Append($"areas {d.routeA}, links {d.routeChosen} ({d.routePairs} reachable pairs); map build {ms:F0} ms (Burst); cut cells {d.routeCutCells}, deepest cut {d.routeMaxCut / 10f:F1} m; fill cells {d.routeFillCells}, deepest fill {d.routeMaxFill / 10f:F1} m; ");
                    if (map?.Routes != null)
                        foreach (var rt in map.Routes)
                            sb.Append($"[{rt.areaA}-{rt.areaB}{(rt.tree ? "" : " extra")} {rt.lengthM:F0} m / {rt.straightM:F0} m, overlap {rt.overlap * 100f:F0}%, passes {rt.passes.Length}] ");
                    s = sb.ToString();
                }
                Routes.LastBuild = s;
                if (map != null) map.SetRouteBuild(s);
            }
            if (d.routeDiagnose && d.diagH0 != null && map != null)
            {
                int N = map.Height.Length;
                Routes.DiagH0 = Out(d.diagH0, N); Routes.DiagLo = Out(d.diagLo, N); Routes.DiagHi = Out(d.diagHi, N); Routes.DiagSkip = Out(d.diagSkip, N);
            }
        }

        // ------------------------------------------------------------------
        // conversions
        // ------------------------------------------------------------------
        internal static T[] Out<T>(T* p, int n) where T : unmanaged
        {
            var r = new T[n];
            if (n > 0) fixed (T* d = r) Buffer.MemoryCopy(p, d, (long)n * sizeof(T), (long)n * sizeof(T));
            return r;
        }

        private static T* In<T>(MapArena* a, T[] src) where T : unmanaged
        {
            if (src == null) return null;
            T* p = a->Root<T>(math.max(1, src.Length));
            if (src.Length > 0) fixed (T* s = src) Buffer.MemoryCopy(s, p, (long)src.Length * sizeof(T), (long)src.Length * sizeof(T));
            return p;
        }

        internal static Regions.Layout Out(in Regions.LayoutB L) =>
            new Regions.Layout { seeds = Out(L.seeds, L.Count), kind = Out(L.kind, L.Count) };

        internal static Feature Out(in FeatureB b)
        {
            var f = new Feature
            {
                kind = b.kind, tier = b.tier, centre = b.centre, radiusM = b.radiusM, wayDown = b.wayDown, mapVersion = b.mapVersion, cellCount = b.cellCount,
                baseH = b.baseH, rise = b.rise, R = b.R, B = b.B, L = b.L, dir = b.dir, noiseOff = b.noiseOff, footR = b.footR, highSide = b.highSide,
                gapLo = b.gapLo, gapW = b.gapW, gapH = b.gapH, gapCell = b.gapCell, bbMin = b.bbMin, bbMax = b.bbMax,
            };
            if (b.path != null) f.path = Out(b.path, b.pathLen);
            if (b.floor != null) f.floor = Out(b.floor, b.pathLen);
            if (b.halfW != null) f.halfW = Out(b.halfW, b.pathLen);
            if (b.bank != null) f.bank = Out(b.bank, b.pathLen);
            if (b.halfWNat != null) f.halfWNat = Out(b.halfWNat, b.pathLen);
            if (b.gap != null) f.gap = Out(b.gap, b.gapW * b.gapH);
            if (b.segLo != null) { f.segLo = Out(b.segLo, b.gapW * b.gapH); f.segHi = Out(b.segHi, b.gapW * b.gapH); }
            return f;
        }

        private static FeatureB In(MapArena* a, Feature f)
        {
            var b = new FeatureB
            {
                kind = f.kind, tier = f.tier, centre = f.centre, radiusM = f.radiusM, wayDown = f.wayDown, mapVersion = f.mapVersion, cellCount = f.cellCount,
                baseH = f.baseH, rise = f.rise, R = f.R, B = f.B, L = f.L, dir = f.dir, noiseOff = f.noiseOff, footR = f.footR, highSide = f.highSide,
                gapLo = f.gapLo, gapW = f.gapW, gapH = f.gapH, gapCell = f.gapCell, bbMin = f.bbMin, bbMax = f.bbMax,
            };
            if (f.path != null) { b.path = In(a, f.path); b.pathLen = f.path.Length; }
            if (f.floor != null) b.floor = In(a, f.floor);
            if (f.halfW != null) b.halfW = In(a, f.halfW);
            if (f.bank != null) b.bank = In(a, f.bank);
            if (f.halfWNat != null) b.halfWNat = In(a, f.halfWNat);
            if (f.gap != null) b.gap = In(a, f.gap);
            if (f.segLo != null) { b.segLo = In(a, f.segLo); b.segHi = In(a, f.segHi); }
            return b;
        }

        internal static Route Out(in RouteB b) => new Route
        {
            mapVersion = b.mapVersion, areaA = b.areaA, areaB = b.areaB, areaCentreA = b.areaCentreA, areaCentreB = b.areaCentreB,
            tree = b.tree, cost = b.cost, overlap = b.overlap,
            cells = Out(b.cells, b.cellsLen), line = Out(b.line, b.lineLen),
            halfW = Out(b.halfW, b.lineLen), cut = Out(b.cut, b.lineLen), fill = Out(b.fill, b.lineLen), bandBefore = Out(b.bandBefore, b.lineLen),
            passes = Out(b.passes, b.passesLen), widthM = b.widthM, lengthM = b.lengthM, straightM = b.straightM,
            endA = b.endA, endB = b.endB, corridor = Out(b.corridor, b.corridorLen),
        };

        internal static EasyArea Out(in EasyAreas.EasyAreaB b) => new EasyArea
        {
            centre = b.centre, cellCount = b.cellCount, hectares = b.hectares, radiusM = b.radiusM, meanHeightM = b.meanHeightM,
            cells = Out(b.cells, b.cellsLen),
        };

        private static EasyAreas.EasyAreaB In(MapArena* a, EasyArea e) => new EasyAreas.EasyAreaB
        {
            centre = e.centre, cellCount = e.cellCount, hectares = e.hectares, radiusM = e.radiusM, meanHeightM = e.meanHeightM,
            cells = In(a, e.cells), cellsLen = e.cells.Length,
        };
    }

    public sealed unsafe partial class WorldMap
    {
        /// The managed map from the Burst build's MapData (MapJobs.BuildMap).
        internal static WorldMap FromCore(ref MapData d)
        {
            int N = d.n * d.n;
            var map = new WorldMap(d.spec, MapJobs.Out(d.height, N), MapJobs.Out(d.rough, N), d.peak,
                                   MapJobs.Out(d.water, N), MapJobs.Out(d.flow, N), d.hydro, MapJobs.Out(d.waterClass, N))
            {
                BeaconCount = d.beacons, InselbergCount = d.inselbergs,
            };
            if (d.region != null) map.Region = MapJobs.Out(d.region, N);
            if (d.flowDir != null) map.SetFlowDir(MapJobs.Out(d.flowDir, N));
            if (d.hasLayout) map.RegionLayout = MapJobs.Out(in d.layout);
            if (d.hasEasy) map.EasySites = MapJobs.Out(d.easySites, d.easyCount);
            if (d.hasFeats)
            {
                var f = new Feature[d.featCount];
                for (int k = 0; k < f.Length; k++) f[k] = MapJobs.Out(in d.feats[k]);
                map.SignatureFeatures = f;
            }
            if (d.hasRoutes)
            {
                var r = new Route[d.routeCount];
                for (int k = 0; k < r.Length; k++) r[k] = MapJobs.Out(in d.routes[k]);
                map.Routes = r;
            }
            if (d.hasRecord) map.Record = WorldRecord.FromCore(ref d.record, map.SignatureFeatures, map.Routes, null);
            return map;
        }

        internal void SetRouteBuild(string s) => RouteBuild = s;

        internal static void DumpErosion(ref MapData d)
        {
            int c = d.dumpM * d.dumpM;
            DumpErosionInputs(DumpErosionInputsTo + $"_{d.spec.seed}.bin", d.dumpM,
                              MapJobs.Out(d.dumpH, c), MapJobs.Out(d.dumpE, c), MapJobs.Out(d.dumpT, c), MapJobs.Out(d.dumpL, c));
        }
    }

    public sealed unsafe partial class WorldRecord
    {
        /// The managed record from the Burst build's RecordB. Its feature and
        /// route lists are the map's own (as Features.Mark / Routes.Mark set
        /// them); `easyList` (a record built from another's easy areas) is kept
        /// as that record's list.
        internal static WorldRecord FromCore(ref RecordB r, Feature[] feats, Route[] routes, EasyArea[] easyList)
        {
            var rec = new WorldRecord(r.cells, r.cellVoxels, r.mapVersion);
            long bytes = (long)rec.Data.Length * sizeof(RecordCell);
            fixed (RecordCell* dst = rec.Data) Buffer.MemoryCopy(r.data, dst, bytes, bytes);
            if (easyList != null) rec.EasyAreaList = easyList;
            else
            {
                var e = new EasyArea[r.easyCount];
                for (int k = 0; k < e.Length; k++) e[k] = MapJobs.Out(in r.easy[k]);
                rec.EasyAreaList = e;
            }
            if (feats != null) rec.FeatureList = feats;
            if (routes != null) rec.RouteList = routes;
            return rec;
        }
    }
}
