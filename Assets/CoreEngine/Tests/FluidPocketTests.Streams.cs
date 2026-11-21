// Assets/CoreEngine/Tests/FluidPocketTests.Streams.cs
//
// Task 209b (owner Q-AG (a)): the ponds' still streams (TerrainGen.PocketStreams), DEFAULT ON,
// `-pocketstreams 0` = task 209's world byte for byte.
//   Streams_Rules                      (fast) the channel numbers: 0.6-1.2 m wide, 0.25-0.35 m of fluid,
//                                      Rivers' bank slopes; no section in a plane without one.
//   Streams_Seed42_AtRest_PathsAgree   (Slow) seed 42: the plane is deterministic; its site words are the
//                                      streams-off plane's (= task 209's FNV 38AF614C8AECA569); EVERY stream
//                                      column (channel + 2 rings, all streams) holds its fluid at rest
//                                      (no fluid voxel beside air or another fluid, 8 neighbours) and, for
//                                      the non-water fluids, every solid face it touches is lined; per fluid
//                                      the longest stream (its shore junction and its middle) and a river
//                                      join: the CPU fluid oracle moves nothing in 150 ticks (+ a negative
//                                      control: a hole beside the channel is seen), ON vs OFF changes only
//                                      columns within the stream's reach, FarQuery = chunk, shell 0 =
//                                      Generate; streams-off chunks equal a fresh -pocketstreams 0 source's.
//                                      Writes Logs/TerrainGen/streams209b/*.txt, Screenshots/209b/cpu_*.png
//                                      and the player poses (poses.txt).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using NUnit.Framework;
using TerrainGen;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine;
using VoxelEngine.Memory;
using VoxelEngine.Streaming;
using VoxelEngine.WorldGen;

public unsafe partial class FluidPocketTests
{
    private static readonly string StreamLogDir = Path.Combine("Logs", "TerrainGen", "streams209b");
    private static readonly string StreamShotDir = Path.Combine("Screenshots", "209b");

    [Test]
    public void Streams_Rules()
    {
        Assert.AreEqual(PocketStreams.HW_MIN, PocketStreams.HalfWidth(1f), 1e-4f, "a head is 0.6 m wide");
        Assert.AreEqual(PocketStreams.HW_MAX, PocketStreams.HalfWidth(Hydrology.RIVER_MIN_CELLS), 1e-4f, "the river threshold's catchment is 1.2 m wide");
        Assert.LessOrEqual(2f * PocketStreams.HW_MAX, Rivers.HALF_W_MIN, "a stream is narrower than the narrowest river");
        for (float f = 1f; f <= 4096f; f *= 2f)
        {
            float hw = PocketStreams.HalfWidth(f), d = PocketStreams.DepthOf(hw);
            Assert.That(hw, Is.InRange(3f, 6f));
            Assert.That(d, Is.InRange(2.5f, 3.5f), "0.25-0.35 m of fluid at the centre");
            Assert.GreaterOrEqual(Rivers.SHAPED_EDGE + Rivers.SHAPED_SHELF * hw, math.min(d, PocketStreams.DEPTH_CAP), "the section reaches the centre depth");
        }
        Assert.AreEqual(Rivers.BANK_CARVE, PocketStreams.BANK_CARVE); Assert.AreEqual(Rivers.BANK_SLOPE, PocketStreams.BANK_SLOPE);
        Assert.Less(PocketStreams.KEEP_OUT, PocketStreams.REACH);
        var plain = new int[160 * 160 * FluidPockets.STRIDE];
        fixed (int* p = plain) Assert.IsTrue(PocketStreams.Section(p, plain.Length, 1024, 80) == null, "a plane without a section has no streams");
        Assert.IsTrue(PocketStreams.Section(null, 0, 1024, 80) == null);
    }

    private struct SCase { public string name; public int rec; public int node; public int3 org; public int bx, by, bz; public byte fluid; }

    [Test, Category("Slow"), Timeout(3600000)]
    public void Streams_Seed42_AtRest_PathsAgree()
    {
        Directory.CreateDirectory(StreamLogDir); Directory.CreateDirectory(StreamShotDir);
        var log = new StringBuilder();
        int failures = 0;
        void Fail(string m) { failures++; log.AppendLine("  FAIL " + m); }
        // task 214: pinned to today's trees (-trees2 0). Its tree filter (IsTreeTrunk / IsTreeLeaf) predates the world trees: on the
        // default world the only out-of-reach change at water_shore is 2,908 air <-> GenMat.Leaves (birch leaf) voxels.
        int keepT2 = TerrainGenChunkSource.Trees2TestOverride; TerrainGenChunkSource.Trees2TestOverride = 0;
        FluidPockets.StreamTestOverride = 1;
        try
        {
            var sw = Stopwatch.StartNew();
            using var src = new TerrainGenChunkSource(42u);
            src.SetFluidPockets(true);
            var map = src.Map; var spec = src.Spec; var o = src.O;
            int n = FluidPockets.PlaneCells(in spec), baseLen = n * n * FluidPockets.STRIDE;
            int[] plane = map.PocketPlane;
            var st = map.PocketStats;
            log.AppendLine($"Task 209b streams, seed 42: {st.sites} ponds; {st.streams.Line()}; plane {plane.Length * 4:N0} B ({(plane.Length - baseLen) * 4:N0} B of streams)");
            Assert.IsTrue(st.streamsOn, "the streams are on by default");

            // determinism, and OFF = task 209's plane
            int[] again = FluidPockets.BuildPlane(map, out _);
            FastAssert.ArrayCollectionEqual(plane, again, "the stream plane is not deterministic");
            FluidPockets.StreamTestOverride = 0;
            int[] off = FluidPockets.BuildPlane(map, out _);
            FluidPockets.StreamTestOverride = 1;
            ulong Fnv(int[] pl, int len) { ulong h = 1469598103934665603UL; for (int i = 0; i < len; i++) { h ^= (uint)pl[i]; h *= 1099511628211UL; } return h; }
            Assert.AreEqual(baseLen, off.Length, "-pocketstreams 0 appends nothing");
            for (int i = 0; i < baseLen; i++) if (off[i] != plane[i]) { Fail($"site word {i} differs ON vs OFF"); break; }
            log.AppendLine($"  plane fnv: streams off {Fnv(off, off.Length):X16} (task 209: 38AF614C8AECA569), streams on {Fnv(plane, plane.Length):X16}");
            Assert.AreEqual(0x38AF614C8AECA569UL, Fnv(off, off.Length), "-pocketstreams 0 is not task 209's plane");

            var recs = PocketStreams.Records(plane, spec.mapCells, spec.mapCellVoxels);
            var byFluid = new Dictionary<string, int[]>();   // streams, seeps, nodes
            foreach (var r in recs)
            {
                string k = FluidName(r.fluid);
                if (!byFluid.TryGetValue(k, out var a)) byFluid[k] = a = new int[3];
                if (r.kind == PocketStreams.KIND_STREAM) a[0]++; else a[1]++;
                a[2] += r.count;
                if (r.fluid == GenMat.Quicksand) Fail("a quicksand pond got a stream");
            }
            foreach (var kv in Sorted(byFluid)) log.AppendLine($"    {kv.Key,-8} streams {kv.Value[0],3}, seeps {kv.Value[1],3}, nodes {kv.Value[2],5}");

            // ---- every stream, column level: at rest + lined ----
            var view = map.View; var offView = view; offView.stream = null;
            var cache = new Dictionary<long, ColumnSample>();
            ColumnSample At(int x, int z)
            {
                long key = ((long)x << 32) ^ (uint)z;
                if (!cache.TryGetValue(key, out var c)) cache[key] = c = ShapeField.Sample(in spec, in view, in o, x + 0.5f, z + 0.5f, 1, false);
                return c;
            }
            int cols = 0, wetCols = 0, lipCols = 0, breaches = 0, mixed = 0, unlined = 0; string firstBreach = null, firstUnlined = null;
            sw.Restart();
            foreach (var r in recs)
            {
                if (r.kind != PocketStreams.KIND_STREAM) continue;
                cache.Clear();
                float hwMax = 0f; foreach (float w in r.hw) hwMax = math.max(hwMax, w);
                int x0 = int.MaxValue, z0 = int.MaxValue, x1 = int.MinValue, z1 = int.MinValue;
                foreach (var nd in r.nodes) { x0 = math.min(x0, nd.x); z0 = math.min(z0, nd.z); x1 = math.max(x1, nd.x); z1 = math.max(z1, nd.z); }
                int pad = (int)math.ceil(hwMax) + 3;
                byte lin = FluidPockets.Lining(r.fluid);
                for (int z = z0 - pad; z <= z1 + pad; z++)
                for (int x = x0 - pad; x <= x1 + pad; x++)
                {
                    if (DistToCourse(r, x + 0.5f, z + 0.5f) > 2f) continue;
                    cols++;
                    var c = At(x, z);
                    if (c.lipLo != 0) lipCols++;
                    if (!(c.water != ColumnSample.NO_WATER_Y && c.water > c.height)) continue;
                    wetCols++;
                    byte f = FluidPockets.FluidOf(c.waterKind);
                    for (int k = 0; k < 8; k++)
                    {
                        int dx = k == 0 || k == 3 || k == 5 ? -1 : (k == 1 || k == 6 ? 0 : 1), dz = k < 3 ? -1 : (k < 5 ? 0 : 1);
                        var q = At(x + dx, z + dz);
                        bool qWet = q.water != ColumnSample.NO_WATER_Y && q.water > q.height;
                        if (qWet && FluidPockets.FluidOf(q.waterKind) != f) { mixed++; if (firstBreach == null) firstBreach = $"({x}, {z}) {FluidName(f)} beside {FluidName(FluidPockets.FluidOf(q.waterKind))}"; }
                        int top = qWet ? math.max(q.height, q.water) : q.height;
                        if (c.water > top) { breaches++; if (firstBreach == null) firstBreach = $"({x}, {z}) {FluidName(f)} at {c.water} over ground {c.height}: neighbour ({x + dx}, {z + dz}) top {top} (h {q.height}, water {q.water}, lip {q.lipLo})"; }
                        // non-water: every solid face the fluid touches is the lining (4 neighbours + the bed)
                        if (f != GenMat.Water && (dx == 0 || dz == 0) && q.height > c.height)
                        {
                            int yLo = c.height + 1, yHi = math.min(c.water, q.height);
                            if (!(q.pkLining == lin && q.pkLineLo <= yLo && q.pkLineHi >= yHi)) { unlined++; if (firstUnlined == null) firstUnlined = $"({x + dx}, {z + dz}) wall {yLo}..{yHi} lining {q.pkLining} [{q.pkLineLo}, {q.pkLineHi}] beside {FluidName(f)}"; }
                        }
                    }
                    if (f != GenMat.Water && !(c.pkLining == lin && c.pkLineLo <= c.height && c.pkLineHi >= c.height)) { unlined++; if (firstUnlined == null) firstUnlined = $"({x}, {z}) bed {c.height} lining {c.pkLining} [{c.pkLineLo}, {c.pkLineHi}]"; }
                }
            }
            log.AppendLine($"  every stream, channel + 2 rings: {cols:N0} columns, {wetCols:N0} hold fluid, {lipCols:N0} lips; breaches {breaches}, mixed fluids {mixed}, unlined faces {unlined} ({sw.Elapsed.TotalSeconds:F0} s)" +
                           (firstBreach != null ? $"; first breach {firstBreach}" : "") + (firstUnlined != null ? $"; first unlined {firstUnlined}" : ""));
            if (breaches + mixed > 0) Fail($"{breaches + mixed} stream columns not at rest, e.g. {firstBreach}");
            if (unlined > 0) Fail($"{unlined} faces touch a non-water stream unlined, e.g. {firstUnlined}");
            if (wetCols == 0) Fail("no stream holds any fluid");

            // ---- the cases: per fluid the longest stream's shore junction and middle; a river join ----
            var cases = new List<SCase>();
            var bestLen = new Dictionary<byte, int>(); var bestRec = new Dictionary<byte, int>();
            int joinRec = -1;
            for (int i = 0; i < recs.Length; i++)
            {
                var r = recs[i];
                if (r.kind != PocketStreams.KIND_STREAM) continue;
                if (!bestLen.TryGetValue(r.fluid, out int bl) || r.count > bl) { bestLen[r.fluid] = r.count; bestRec[r.fluid] = i; }
                if (joinRec < 0 && r.fluid == GenMat.Water && IsJoin(r, spec, offView, o)) joinRec = i;
            }
            foreach (var kv in bestRec)
            {
                var r = recs[kv.Value];
                int j = ShoreNode(r, map, spec);
                cases.Add(MakeCase($"{FluidName(kv.Key)}_shore", kv.Value, j, r));
                cases.Add(MakeCase($"{FluidName(kv.Key)}_middle", kv.Value, r.count / 2, r));
            }
            if (joinRec >= 0) cases.Add(MakeCase("water_river_join", joinRec, recs[joinRec].count - 1, recs[joinRec]));
            else log.AppendLine("  no outlet joins a river on seed 42");

            int[] offWords = new int[baseLen]; Array.Copy(plane, offWords, baseLen);
            var poses = new List<string>();
            foreach (var cs in cases)
            {
                var r = recs[cs.rec];
                int3 c0 = cs.org >> 7, c1 = (cs.org + new int3(cs.bx, cs.by, cs.bz) * 8 - 1) >> 7;
                var on = new Dictionary<long, byte[]>(); var offB = new Dictionary<long, byte[]>();
                long K(int3 c) => ((long)(c.x & 0xFFFFF) << 40) | ((long)(c.y & 0xFFFFF) << 20) | (long)(c.z & 0xFFFFF);
                var coords = new List<int3>();
                for (int cy = c0.y; cy <= c1.y; cy++) for (int cz = c0.z; cz <= c1.z; cz++) for (int cx = c0.x; cx <= c1.x; cx++) coords.Add(new int3(cx, cy, cz));
                foreach (var cc in coords) on[K(cc)] = Gen(src, cc);
                src.OverridePocketPlane(offWords);
                foreach (var cc in coords) offB[K(cc)] = Gen(src, cc);
                src.OverridePocketPlane(plane);
                byte AtOn(int x, int y, int z) => on.TryGetValue(K(new int3(x >> 7, y >> 7, z >> 7)), out var a) ? a[TerrainGenChunkSource.Index(x & 127, y & 127, z & 127)] : Materials.Air;
                int fails0 = failures;
                // ON vs OFF: only within a stream's reach (any stream), tree voxels aside
                var treeMat = new bool[256];
                for (int g = 0; g < 256; g++) if (GenMat.IsTreeTrunk((byte)g) || GenMat.IsTreeLeaf((byte)g)) treeMat[VoxelEngine.Rendering.FarField.GenToEngine((byte)g)] = true;
                var reachCol = new Dictionary<long, bool>();
                int changed = 0, outside = 0, trees = 0;
                foreach (var cc in coords)
                {
                    byte[] a = on[K(cc)], b = offB[K(cc)]; int3 org = cc << 7;
                    for (int i = 0; i < a.Length; i++)
                    {
                        if (a[i] == b[i]) continue;
                        changed++;
                        int x = org.x + (i & 127), z = org.z + ((i >> 7) & 127);
                        long key = ((long)x << 32) ^ (uint)z;
                        if (!reachCol.TryGetValue(key, out bool inReach))
                        {
                            inReach = false;
                            foreach (var rr in recs) if (DistToCourse(rr, x + 0.5f, z + 0.5f) <= PocketStreams.REACH + 2f) { inReach = true; break; }
                            reachCol[key] = inReach;
                        }
                        if (inReach) continue;
                        if (treeMat[a[i]] || treeMat[b[i]]) { trees++; continue; }
                        outside++;
                    }
                }
                if (outside > 0) Fail($"{cs.name}: {outside} voxels changed outside every stream's reach");
                // the settle check + a negative control (air beside a fluid voxel of the channel)
                string settle = Settle(AtOn, cs.org, cs.bx, cs.by, cs.bz, 150, out int fluidCells);
                if (settle.Length > 0) Fail($"{cs.name}: NOT at rest: {settle}");
                if (fluidCells == 0) Fail($"{cs.name}: no fluid in the settle box");
                int3 hole = int3.zero; bool found = false;
                byte fe = VoxelEngine.Rendering.FarField.GenToEngine(cs.fluid);
                int3 mid = cs.org + new int3(cs.bx * 4, 0, cs.bz * 4);
                for (int rad = 0; rad < 48 && !found; rad++)
                for (int dz = -rad; dz <= rad && !found; dz++)
                for (int dx = -rad; dx <= rad && !found; dx++)
                {
                    if (math.max(math.abs(dx), math.abs(dz)) != rad) continue;
                    int x = mid.x + dx, z = mid.z + dz;
                    for (int y = cs.org.y + 2; y < cs.org.y + cs.by * 8 - 2 && !found; y++)
                        if (AtOn(x, y, z) == fe && AtOn(x + 1, y, z) != fe && AtOn(x + 1, y, z) != Materials.Air && AtOn(x + 1, y + 1, z) == Materials.Air && x + 1 < cs.org.x + cs.bx * 8 - 1)
                        { hole = new int3(x + 1, y, z); found = true; }
                }
                string control = "no wall voxel found";
                if (found)
                {
                    string holed = Settle((x, y, z) => x == hole.x && z == hole.z && y == hole.y ? Materials.Air : AtOn(x, y, z), cs.org, cs.bx, cs.by, cs.bz, 150, out _);
                    control = $"hole at {hole} -> " + (holed.Length > 0 ? holed : "NOTHING (the check is blind)");
                    if (holed.Length == 0) Fail($"{cs.name}: negative control unnoticed");
                }
                // FarQuery = chunk; shell 0 = Generate
                var pts = new List<int3>(); var want = new List<byte>();
                var rng = new Unity.Mathematics.Random(209u + (uint)cs.name.Length * 7u);
                for (int k = 0; k < 3000; k++) { int3 p = rng.NextInt3(cs.org, cs.org + new int3(cs.bx, cs.by, cs.bz) * 8); pts.Add(p); want.Add(AtOn(p.x, p.y, p.z)); }
                using (var empty = new Empty())
                using (var fq = new FarQuery(empty.store, empty.store, src, EditOpLog.InMemory(42u), null, 42u))
                {
                    var p = new NativeArray<int3>(pts.ToArray(), Allocator.TempJob); var res = new NativeArray<byte>(pts.Count, Allocator.TempJob);
                    try
                    {
                        fq.GetVoxels(p, res);
                        int bad = 0; for (int i = 0; i < pts.Count; i++) if (want[i] != res[i] && bad++ == 0) Fail($"{cs.name}: FarQuery {pts[i]} {res[i]} != chunk {want[i]}");
                    }
                    finally { p.Dispose(); res.Dispose(); }
                }
                using (var sc = new TerrainGenChunkSource.ShellChunk())
                {
                    int3 mc3 = (cs.org + new int3(cs.bx * 4, cs.by * 4, cs.bz * 4)) >> 7;
                    src.GenerateShellChunk(0, mc3, sc);
                    byte[] a = on.TryGetValue(K(mc3), out var aa) ? aa : Gen(src, mc3);
                    for (int i = 0; i < a.Length; i++) if (sc.Get(i & 127, i >> 14, (i >> 7) & 127) != a[i]) { Fail($"{cs.name}: shell 0 voxel {i} != Generate"); break; }
                }
                var nd0 = r.nodes[cs.node];
                log.AppendLine($"  {cs.name}: {FluidName(r.fluid)} stream of {r.count} nodes, node {cs.node} at ({nd0.x}, {nd0.y}, {nd0.z}); {(failures == fails0 ? "PASS" : "FAIL")}; " +
                               $"ON vs OFF changed {changed:N0} voxels (tree voxels outside the reach {trees:N0}); settle 150 ticks {fluidCells:N0} mobile cells: {(settle.Length == 0 ? "nothing moved or reacted" : settle)}; " +
                               $"negative control: {control}");
                TopViewStream(src, cs, r, plane, offWords);
                poses.Add(StreamPose(map, spec, o, cs, r));
            }

            // streams-off chunks = a fresh -pocketstreams 0 source (the previous world byte for byte)
            FluidPockets.StreamTestOverride = 0;
            using (var src0 = new TerrainGenChunkSource(42u))
            {
                src0.SetFluidPockets(true);
                Assert.AreEqual(baseLen, src0.Map.PocketPlane.Length);
                src.OverridePocketPlane(offWords);
                int checkedChunks = 0;
                foreach (var cs in cases)
                {
                    int3 cc = (cs.org + new int3(cs.bx * 4, cs.by * 4, cs.bz * 4)) >> 7;
                    byte[] a = Gen(src, cc), b = Gen(src0, cc); checkedChunks++;
                    for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) { Fail($"streams-off chunk {cc} differs from a -pocketstreams 0 source at {i}"); break; }
                }
                src.OverridePocketPlane(plane);
                log.AppendLine($"  -pocketstreams 0: plane = task 209's; {checkedChunks} chunks equal a fresh streams-off source's");
            }
            FluidPockets.StreamTestOverride = 1;
            // one aerial pose over the water case
            foreach (var cs in cases) if (cs.name == "water_shore") { poses.Add(AerialPose(map, spec, o, recs[cs.rec])); break; }
            File.WriteAllText(Path.Combine(StreamLogDir, "streams.txt"), log.ToString());
            File.WriteAllText(Path.Combine(StreamLogDir, "poses.txt"), string.Join(";", poses));
            UnityEngine.Debug.Log(log.ToString());
            Assert.GreaterOrEqual(cases.Count, 8, "too few stream cases (4 fluids x 2)");
            Assert.AreEqual(0, failures, "see " + Path.Combine(StreamLogDir, "streams.txt"));
        }
        finally { FluidPockets.StreamTestOverride = 0; TerrainGenChunkSource.Trees2TestOverride = keepT2; }
    }

    [Test, Explicit, Category("Slow")]
    public void Streams_Diag_CrossSections()
    {
        FluidPockets.StreamTestOverride = 1;
        try
        {
            using var src = new TerrainGenChunkSource(42u);
            src.SetFluidPockets(true);
            var map = src.Map; var spec = src.Spec; var o = src.O;
            var recs = PocketStreams.Records(map.PocketPlane, spec.mapCells, spec.mapCellVoxels);
            var view = map.View; var offView = view; offView.stream = null;
            var sb = new StringBuilder();
            int shown = 0;
            foreach (var r in recs)
            {
                if (r.kind != PocketStreams.KIND_STREAM || r.fluid != GenMat.Water || r.count < 20) continue;
                sb.AppendLine($"stream {FluidName(r.fluid)} {r.count} nodes; levels: " + string.Join(",", Array.ConvertAll(r.nodes, q => q.y.ToString())));
                foreach (int j in new[] { r.count / 4, r.count / 2, 3 * r.count / 4 })
                {
                    var a = r.nodes[j]; var b2 = r.nodes[math.min(r.count - 1, j + 1)];
                    float2 dir = math.normalizesafe(new float2(b2.x - a.x, b2.z - a.z)), side = new float2(-dir.y, dir.x);
                    sb.AppendLine($"  node {j} ({a.x}, {a.z}) level {a.y} hw {r.hw[j]} d {r.depth[j]}");
                    for (int k = -9; k <= 9; k++)
                    {
                        float2 q = new float2(a.x + 0.5f, a.z + 0.5f) + side * k;
                        var c1 = ShapeField.Sample(in spec, in view, in o, q.x, q.y, 1, false);
                        var c0 = ShapeField.Sample(in spec, in offView, in o, q.x, q.y, 1, false);
                        sb.AppendLine($"    {k,3}: off h {c0.height} w {c0.water} | on h {c1.height} w {c1.water} lip {c1.lipLo} lin {c1.pkLining} [{c1.pkLineLo},{c1.pkLineHi}] rf {c1.riverFloor}; dist {DistToCourse(r, q.x, q.y):F1}");
                    }
                }
                if (++shown >= 2) break;
            }
            Directory.CreateDirectory(StreamLogDir);
            File.WriteAllText(Path.Combine(StreamLogDir, "diag.txt"), sb.ToString());
        }
        finally { FluidPockets.StreamTestOverride = 0; }
    }

    private static float DistToCourse(in PocketStreams.Rec r, float x, float z)
    {
        float best = float.MaxValue; float2 p = new float2(x, z);
        if (x < r.box.x || z < r.box.y || x > r.box.z || z > r.box.w) return best;
        for (int j = 0; j + 1 < r.count; j++)
        {
            float2 a = new float2(r.nodes[j].x, r.nodes[j].z), b = new float2(r.nodes[j + 1].x, r.nodes[j + 1].z), ab = b - a;
            float l2 = math.dot(ab, ab), t = l2 > 0f ? math.saturate(math.dot(p - a, ab) / l2) : 0f;
            best = math.min(best, math.length(p - (a + t * ab)) - math.lerp(r.hw[j], r.hw[j + 1], t));
        }
        return best;
    }

    /// Does the course end inside a river channel (an outlet that joins)?
    private static bool IsJoin(in PocketStreams.Rec r, TerrainSpec spec, MapView view, Offsets o)
    {
        var e = r.nodes[r.count - 1];
        var c = ShapeField.Sample(in spec, in view, in o, e.x + 0.5f, e.z + 0.5f, 1, false);
        return c.water != ColumnSample.NO_WATER_Y && c.water > c.height && c.pkLining == 0 && e.y == c.water;
    }

    /// The first node (from the pond's end) outside the pond's footprint.
    private static int ShoreNode(in PocketStreams.Rec r, WorldMap map, TerrainSpec spec)
    {
        var view = map.View; view.stream = null; var o = Offsets.For(in spec);   // the ponds alone (a lined stream column looks like a pond)
        bool PondAt(int3 nd) { var c = ShapeField.Sample(in spec, in view, in o, nd.x + 0.5f, nd.z + 0.5f, 1, false); return c.pkLining != 0 && c.water > c.height; }
        bool inletLike = PondAt(r.nodes[r.count - 1]);
        if (inletLike) { for (int j = r.count - 1; j >= 0; j--) if (!PondAt(r.nodes[j])) return j; }
        else { for (int j = 0; j < r.count; j++) if (!PondAt(r.nodes[j])) return j; }
        return r.count / 2;
    }

    private static SCase MakeCase(string name, int rec, int node, in PocketStreams.Rec r)
    {
        var nd = r.nodes[node];
        int lo = int.MaxValue, hi = int.MinValue;
        for (int j = 0; j < r.count; j++)
            if (math.abs(r.nodes[j].x - nd.x) < 64 && math.abs(r.nodes[j].z - nd.z) < 64) { lo = math.min(lo, r.nodes[j].y); hi = math.max(hi, r.nodes[j].y); }
        int by = (hi - lo + 36 + 7) / 8 <= 8 ? 8 : 16;   // the CPU oracle wants a power of two   // the pond's bed (<= 2 m) under the lowest level, 1.2 m over the highest
        return new SCase { name = name, rec = rec, node = node, fluid = r.fluid, org = new int3(nd.x - 64, lo - 24, nd.z - 64), bx = 16, by = by, bz = 16 };
    }

    /// The case from above on the CPU, ON | OFF side by side (north up); stream fluid tinted by depth.
    private static void TopViewStream(TerrainGenChunkSource src, SCase cs, in PocketStreams.Rec r, int[] onPlane, int[] offPlane)
    {
        var spec = src.Spec; var o = src.O; var viewOn = src.Map.View; var viewOff = viewOn; viewOff.stream = null;
        const int S = 160;
        int x0 = r.nodes[cs.node].x - S / 2, z0 = r.nodes[cs.node].z - S / 2, W = 2 * S + 4;
        var px = new Color32[W * S];
        for (int side = 0; side < 2; side++)
        {
            var v = side == 0 ? viewOn : viewOff;
            var hh = new int[S * S]; var mat = new byte[S * S]; var dep = new int[S * S];
            for (int j = 0; j < S; j++)
            for (int i = 0; i < S; i++)
            {
                int x = x0 + i, z = z0 + j, k = j * S + i;
                var c = ShapeField.Sample(in spec, in v, in o, x + 0.5f, z + 0.5f, 1, false);
                hh[k] = c.height; dep[k] = c.water > c.height ? c.water - c.height : 0;
                mat[k] = VoxelEngine.Rendering.FarField.GenToEngine(c.lipLo != 0 && cs.fluid == GenMat.Water ? GenMat.Water : DetailField.TopMaterial(in spec, in c, 1f, in o, x, z));
                if (dep[k] > 0) mat[k] = VoxelEngine.Rendering.FarField.GenToEngine(FluidPockets.FluidOf(c.waterKind));
            }
            for (int j = 0; j < S; j++)
            for (int i = 0; i < S; i++)
            {
                int k = j * S + i;
                float shade = dep[k] > 0 ? math.clamp(1.05f - dep[k] * 0.05f, 0.55f, 1.05f)
                    : math.clamp(0.85f - 0.12f * (((i > 0 ? hh[k - 1] : hh[k]) - hh[k]) + ((j < S - 1 ? hh[k + S] : hh[k]) - hh[k])) * -1f, 0.45f, 1.15f);
                px[j * W + i + side * (S + 4)] = Col(mat[k], shade);
            }
        }
        WritePng(Path.Combine(StreamShotDir, $"cpu_top_{cs.name}.png"), px, W, S, 3);
    }

    private static string M(float v) => (v * 0.1f).ToString("F1", System.Globalization.CultureInfo.InvariantCulture);

    /// A side view: the camera 22 m back along the course (downstream side) and 9 m up, on the target node.
    private static string StreamPose(WorldMap map, TerrainSpec spec, Offsets o, SCase cs, in PocketStreams.Rec r)
    {
        var view = map.View;
        int j = cs.node, j2 = math.min(r.count - 1, j + 3), j1 = math.max(0, j - 3);
        float2 dir = math.normalizesafe(new float2(r.nodes[j2].x - r.nodes[j1].x, r.nodes[j2].z - r.nodes[j1].z), new float2(1, 0));
        float2 side = new float2(-dir.y, dir.x);
        var t = r.nodes[j];
        float2 e = new float2(t.x, t.z) + dir * 160f + side * 90f;
        int g = ShapeField.Sample(in spec, in view, in o, e.x, e.y, 1).height;
        float ey = math.max(t.y, g) + 90f;
        return $"{cs.name}|{M(e.x)},{M(ey)},{M(e.y)}|{M(t.x + 0.5f)},{M(t.y)},{M(t.z + 0.5f)}";
    }

    private static string AerialPose(WorldMap map, TerrainSpec spec, Offsets o, in PocketStreams.Rec r)
    {
        var t = r.nodes[r.count / 2];
        return $"aerial_water|{M(t.x + 0.5f)},{M(t.y + 700f)},{M(t.z - 250f)}|{M(t.x + 0.5f)},{M(t.y)},{M(t.z + 0.5f)}";
    }
}
