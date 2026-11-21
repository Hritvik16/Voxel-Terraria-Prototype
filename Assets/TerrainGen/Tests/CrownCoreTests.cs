// Assets/TerrainGen/Tests/CrownCoreTests.cs
//
// Task 243: solid crown cores (CrownCore.cs, TerrainSpec.crownCore, default OFF).
//   Adversarial_Shapes      the rule is the crown's own shape, proven on crowns nobody tuned for:
//                           tiny (4^3: unchanged; 5^3 with one inner hole: exactly that hole), all
//                           skin (a 4-voxel slab: unchanged), concave (an open bowl: its air stays
//                           air), a hollow 1-voxel shell (core touches nothing: dropped, no floating
//                           leaves) and the same shell with a branch through it (filled), a very
//                           large porous lens (400 x 80 x 400: filled to 2 voxels under its skin)
//   Oracle_BruteForce       the bit planes against a brute-force reading of the rule, exact sets (modes 1, 2)
//   Closed_Mode_GapsByClumpRadius  mode 2 closes a 6-voxel gap with r 4, not a 20-voxel one; the skin stays
//   Prototypes_OffIdentical_OnOnlyAirToLeaf   real library prototypes (birch, spruce, acacia, one
//                           oak): core 0 = today's build byte for byte; core 1 only turns AIR into the
//                           prototype's LEAF, inside the leaf box, never within SKIN of a line's end,
//                           every leaf stays 6-connected to the wood, and the placement inputs
//                           (placeBricks, reach, keep, leafBottom, leafReach, footR, box) and the BRICKS are unchanged
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;

namespace TerrainGen.Tests
{
    public class CrownCoreTests
    {
        private const byte WOOD = GenMat.ForestWood, LEAF = GenMat.ForestLeaves;

        private static TreeGallery.Vol NewVol(int3 lo, int3 hi) => new TreeGallery.Vol(lo, hi);

        private static int CountMat(TreeGallery.Vol v, byte m)
        {
            int c = 0;
            foreach (var b in v.bricks) foreach (var x in b) if (x == m) c++;
            return c;
        }

        private static uint H(int x, int y, int z) => Ores.Hash(243u, x, y, z);

        [Test]
        public void Adversarial_Shapes()
        {
            // (1) tiny: a 4^3 porous cube -- smaller than 2 SKIN + 1: nothing can be core
            {
                var v = NewVol(0, 15);
                for (int y = 0; y < 4; y++) for (int z = 0; z < 4; z++) for (int x = 0; x < 4; x++)
                    if ((H(x, y, z) & 3) != 0) v.SetIfEmpty(new int3(x, y, z), LEAF);
                v.SetIfEmpty(new int3(0, 0, 0), WOOD);
                Assert.AreEqual(0, CrownCore.Fill(v, LEAF, out _), "tiny 4^3 crown got a core");
            }
            // (1b) 5^3 leaf cube with its centre air: exactly the centre (depth 3) fills
            {
                var v = NewVol(0, 15);
                for (int y = 0; y < 5; y++) for (int z = 0; z < 5; z++) for (int x = 0; x < 5; x++)
                    if (!(x == 2 && y == 2 && z == 2)) v.SetIfEmpty(new int3(x, y, z), LEAF);
                Assert.AreEqual(1, CrownCore.Fill(v, LEAF, out _), "5^3 crown: the one deep hole should fill");
                Assert.AreEqual(LEAF, v.Get(new int3(2, 2, 2)));
            }
            // (2) all skin: a porous slab 64 x 4 x 64 (an acacia-thin top) -- unchanged
            {
                var v = NewVol(0, 63);
                for (int y = 0; y < 4; y++) for (int z = 0; z < 64; z++) for (int x = 0; x < 64; x++)
                    if ((H(x, y, z) & 1) != 0) v.SetIfEmpty(new int3(x, y, z), LEAF);
                Assert.AreEqual(0, CrownCore.Fill(v, LEAF, out var st), $"all-skin slab filled (hull {st.hull})");
            }
            // (3) concave: an open-top bowl (hemisphere shell 3 thick, r 30): the bowl's air stays air
            {
                var v = NewVol(new int3(-32, -32, -32), new int3(31, 31, 31));
                for (int y = -31; y <= 0; y++) for (int z = -31; z <= 31; z++) for (int x = -31; x <= 31; x++)
                {
                    int d2 = x * x + y * y + z * z;
                    if (d2 <= 30 * 30 && d2 > 27 * 27) v.SetIfEmpty(new int3(x, y, z), LEAF);
                }
                long n = CrownCore.Fill(v, LEAF, out _);
                Assert.AreEqual(0, v.Get(new int3(0, -10, 0)), "the bowl's inside air was filled");
                Assert.AreEqual(0, n, "an open bowl 3 thick has no interior");
            }
            // (4) hollow 1-voxel sphere shell (r 20): its core touches no solid -> dropped (no floating leaves)
            int3 lo = new int3(-24, -24, -24), hi = new int3(23, 23, 23);
            {
                var v = NewVol(lo, hi);
                Shell(v, 20);
                Assert.AreEqual(0, CrownCore.Fill(v, LEAF, out var st), "a floating core was written");
                Assert.Greater(st.dropped, 0, "the shell's core should have been found and dropped");
            }
            // (4b) the same shell with a wood branch through the centre: the core fills, connected
            {
                var v = NewVol(lo, hi);
                Shell(v, 20);
                for (int y = -20; y <= 20; y++) v.SetIfEmpty(new int3(0, y, 0), WOOD);
                var orig = Solids(v);
                long n = CrownCore.Fill(v, LEAF, out var st);
                Assert.Greater(n, 15000, "the shell with a branch did not fill");
                Assert.AreEqual(LEAF, v.Get(new int3(10, 0, 0)), "interior voxel not filled");
                Assert.AreEqual(0, v.Get(new int3(19, 0, 0)), "the skin (1 under the shell) was filled");
                Assert.AreEqual(0, v.Get(new int3(18, 0, 0)), "the skin (2 under the shell) was filled");
                AssertAddedConnected(v, orig);
            }
            // (4c) the library's setting (keepBricks): the same crown, no new brick; what is written stays connected
            {
                var v = NewVol(lo, hi);
                Shell(v, 20);
                for (int y = -20; y <= 20; y++) v.SetIfEmpty(new int3(0, y, 0), WOOD);
                var orig = Solids(v);
                long n = CrownCore.Fill(v, LEAF, out var st, CrownCore.SKIN, 0, keepBricks: true);
                Assert.AreEqual(st.bricksBefore, st.bricksAfter, "keepBricks created a brick");
                Assert.Greater(st.skipped, 0, "the shell's pure-interior bricks should have been skipped");
                Assert.Greater(n, 0, "keepBricks wrote nothing (the trunk's bricks hold core voxels)");
                AssertAddedConnected(v, orig);
            }
            // (5) very large: a porous lens 400 x 80 x 400 with a trunk; filled to SKIN under its surface
            {
                int R = 200, Hh = 40;
                var v = NewVol(new int3(-R - 2, -Hh - 2, -R - 2), new int3(R + 2, Hh + 2, R + 2));
                for (int y = -Hh; y <= Hh; y++) for (int z = -R; z <= R; z++) for (int x = -R; x <= R; x++)
                {
                    long e = (long)x * x * Hh * Hh + (long)z * z * Hh * Hh + (long)y * y * R * R;
                    if (e > (long)R * R * Hh * Hh) continue;
                    if ((math.hash(new int3(x >> 1, y >> 1, z >> 1)) & 0xFF) < 115) continue;   // ~45 % holes in 2-voxel cells, like Voxelise
                    // (math.hash, not Ores.Hash's low byte: that one leaves a near-planar air sheet through the whole lens at
                    // y >> 1 = -13, which the rule rightly keeps open -- an axis line with no crown on one side is not interior)
                    v.SetIfEmpty(new int3(x, y, z), LEAF);
                }
                for (int y = -Hh; y <= Hh; y++) v.SetIfEmpty(new int3(0, y, 0), WOOD);
                int leaf0 = CountMat(v, LEAF);
                var copy = NewVol(new int3(-R - 2, -Hh - 2, -R - 2), new int3(R + 2, Hh + 2, R + 2));
                for (int bi = 0; bi < v.index.Length; bi++) { int sl = v.index[bi]; if (sl < 0) continue; int3 bo = v.lo + new int3(bi % v.nb.x, bi / (v.nb.x * v.nb.z), (bi / v.nb.x) % v.nb.z) * 8; var bb = v.bricks[sl]; for (int j = 0; j < 512; j++) if (bb[j] != 0) copy.SetIfEmpty(bo + new int3(j & 7, j >> 6, (j >> 3) & 7), bb[j]); }
                // the outer 2 voxels of the top surface (per column), recorded before the fill
                var top = new List<KeyValuePair<int3, byte>>();
                for (int z = -20; z <= 20; z++) for (int x = -20; x <= 20; x++)
                {
                    int yt = Hh;
                    while ((long)x * x * Hh * Hh + (long)z * z * Hh * Hh + (long)yt * yt * R * R > (long)R * R * Hh * Hh) yt--;
                    for (int y = yt - 1; y <= yt; y++) top.Add(new KeyValuePair<int3, byte>(new int3(x, y, z), v.Get(new int3(x, y, z))));
                }
                long n = CrownCore.Fill(v, LEAF, out var st);
                Assert.Greater(n, 0);
                // deep inside (scaled depth well past the skin): every voxel solid
                int holes = 0, samples = 0, unexplained = 0;
                for (int y = -Hh + 8; y <= Hh - 8; y += 3) for (int z = -R + 40; z <= R - 40; z += 7) for (int x = -R + 40; x <= R - 40; x += 7)
                {
                    long e = (long)x * x * Hh * Hh + (long)z * z * Hh * Hh + (long)y * y * R * R;
                    if (e > (long)(R - 20) * (R - 20) * (Hh - 8) * (Hh - 8)) continue;
                    samples++;
                    if (v.Get(new int3(x, y, z)) != 0) continue;
                    holes++;
                    string why = Why(copy, new int3(x, y, z), new int3(-R, -Hh, -R), new int3(R, Hh, R));
                    if (why == null) unexplained++;
                    else if (holes <= 2) UnityEngine.Debug.Log($"[crowncore] lens: deep air left at {new int3(x, y, z)}, rightly: {why}");
                }
                Assert.AreEqual(0, unexplained, "air left deep inside the large crown with every axis line enclosed (a fill bug)");
                Assert.Less(holes, samples / 100, $"too much air left deep inside the large crown ({holes} of {samples})");
                Assert.AreEqual(leaf0 + n, CountMat(v, LEAF), "fill count != leaf voxels added");
                // the top surface keeps its ragged holes: the top layer of the lens is unchanged
                int topAir = 0, topChanged = 0;
                foreach (var kv in top) { byte now = v.Get(kv.Key); if (now != kv.Value) topChanged++; if (now == 0) topAir++; }
                Assert.AreEqual(0, topChanged, "the crown's outer 2 voxels changed");
                Assert.Greater(topAir, 500, "the crown's outer skin lost its holes");
                UnityEngine.Debug.Log($"[crowncore] lens 400x80x400: hull {st.hull:N0} core {st.core:N0} filled {st.filled:N0} dropped {st.dropped:N0} bricks {st.bricksBefore} -> {st.bricksAfter}");
            }
        }

        /// The bit-plane implementation against a brute-force reading of the rule (mode 1 and mode 2),
        /// exact voxel sets, on porous ellipsoids of several sizes and box alignments.
        [Test]
        public void Oracle_BruteForce()
        {
            int3[] sizes = { new int3(9, 5, 9), new int3(30, 10, 22), new int3(70, 21, 40) };
            int k = 0;
            foreach (var R in sizes)
            for (int close = 0; close <= 3; close += 3)
            {
                k++;
                int3 off = new int3(k * 3, -k, k * 5);   // box alignments against the 64-bit words / bricks
                var v = NewVol(off - R - 4, off + R + 4);
                for (int y = -R.y; y <= R.y; y++) for (int z = -R.z; z <= R.z; z++) for (int x = -R.x; x <= R.x; x++)
                {
                    double e = (double)x * x / (R.x * R.x) + (double)y * y / (R.y * R.y) + (double)z * z / (R.z * R.z);
                    if (e > 1) continue;
                    if ((math.hash(new int3(x >> 1, y >> 1, z >> 1) + k * 1000) & 0xFF) < 110) continue;
                    v.SetIfEmpty(off + new int3(x, y, z), LEAF);
                }
                for (int y = -R.y; y <= R.y; y++) v.SetIfEmpty(off + new int3(0, y, 0), WOOD);
                var orig = Solids(v);
                // brute force over the leaf box
                int3 lo = int.MaxValue, hi = int.MinValue;
                foreach (var p in orig) if (v.Get(p) == LEAF) { lo = math.min(lo, p); hi = math.max(hi, p); }
                int3 blo = lo - close, bhi = hi + close;
                bool InB(int3 p) => math.all(p >= blo) && math.all(p <= bhi);
                bool Sv(int3 p) => InB(p) && math.all(p >= lo) && math.all(p <= hi) && orig.Contains(p);
                var D = new HashSet<int3>(); var K = new HashSet<int3>();
                for (int y = blo.y; y <= bhi.y; y++) for (int z = blo.z; z <= bhi.z; z++) for (int x = blo.x; x <= bhi.x; x++)
                {
                    var p = new int3(x, y, z); bool any = false;
                    for (int dy = -close; dy <= close && !any; dy++) for (int dz = -close; dz <= close && !any; dz++) for (int dx = -close; dx <= close && !any; dx++)
                        any = Sv(p + new int3(dx, dy, dz));
                    if (any) D.Add(p);
                }
                for (int y = blo.y; y <= bhi.y; y++) for (int z = blo.z; z <= bhi.z; z++) for (int x = blo.x; x <= bhi.x; x++)
                {
                    var p = new int3(x, y, z); bool all = true;
                    for (int dy = -close; dy <= close && all; dy++) for (int dz = -close; dz <= close && all; dz++) for (int dx = -close; dx <= close && all; dx++)
                    { var q = p + new int3(dx, dy, dz); all = InB(q) && D.Contains(q); }
                    if (all || Sv(p)) K.Add(p);
                }
                var Hh = new HashSet<int3>();
                for (int y = blo.y; y <= bhi.y; y++) for (int z = blo.z; z <= bhi.z; z++) for (int x = blo.x; x <= bhi.x; x++)
                {
                    var p = new int3(x, y, z); bool enc = true;
                    for (int ax = 0; ax < 3 && enc; ax++)
                    {
                        bool neg = false, pos = false;
                        for (int t = 0; ; t++) { var q = p; q[ax] -= t; if (!InB(q)) break; if (K.Contains(q)) { neg = true; break; } }
                        for (int t = 0; ; t++) { var q = p; q[ax] += t; if (!InB(q)) break; if (K.Contains(q)) { pos = true; break; } }
                        enc = neg && pos;
                    }
                    if (enc) Hh.Add(p);
                }
                var want = new HashSet<int3>();
                foreach (var p in Hh)
                {
                    bool all = true;
                    for (int dy = -2; dy <= 2 && all; dy++) for (int dz = -2; dz <= 2 && all; dz++) for (int dx = -2; dx <= 2 && all; dx++)
                        all = Hh.Contains(p + new int3(dx, dy, dz));
                    if (all && !orig.Contains(p)) want.Add(p);
                }
                // connectivity to the original solid through `want`
                var reach = new HashSet<int3>(); var st = new Stack<int3>();
                int3[] d6 = { new int3(1, 0, 0), new int3(-1, 0, 0), new int3(0, 1, 0), new int3(0, -1, 0), new int3(0, 0, 1), new int3(0, 0, -1) };
                foreach (var p in want) foreach (var e in d6) if (orig.Contains(p + e)) { if (reach.Add(p)) st.Push(p); break; }
                while (st.Count > 0) { var p = st.Pop(); foreach (var e in d6) { var q = p + e; if (want.Contains(q) && reach.Add(q)) st.Push(q); } }
                CrownCore.Fill(v, LEAF, out var cs, CrownCore.SKIN, close);
                var got = Solids(v); got.ExceptWith(orig);
                int miss = 0, extra = 0; int3 firstMiss = int.MinValue, firstExtra = int.MinValue;
                foreach (var p in reach) if (!got.Contains(p)) { if (miss++ == 0) firstMiss = p - lo; }
                foreach (var p in got) if (!reach.Contains(p)) { if (extra++ == 0) firstExtra = p - lo; }
                Assert.IsTrue(miss == 0 && extra == 0,
                    $"size {R} close {close}: want {reach.Count} got {got.Count}, missing {miss} (first at box-local {firstMiss}), extra {extra} (first {firstExtra}); box {cs.boxSize}");
            }
        }

        [Test]
        public void Closed_Mode_GapsByClumpRadius()
        {
            // two porous 12^3 leaf blocks on a wood bar, a gap of g voxels between them; closing radius 4
            foreach (int g in new[] { 6, 20 })
            {
                var v = NewVol(new int3(-4, -4, -4), new int3(48, 20, 20));
                for (int x = 0; x < 24 + g; x++) v.SetIfEmpty(new int3(x, 6, 6), WOOD);
                for (int y = 0; y < 12; y++) for (int z = 0; z < 12; z++) for (int x = 0; x < 24 + g; x++)
                {
                    if (x >= 12 && x < 12 + g) continue;
                    if ((H(x >> 1, y >> 1, z >> 1) & 0xFF) < 100) continue;
                    v.SetIfEmpty(new int3(x, y, z), LEAF);
                }
                CrownCore.Fill(v, LEAF, out var st, CrownCore.SKIN, 4);
                byte mid = v.Get(new int3(12 + g / 2, 6, 5));
                if (g == 6) Assert.AreEqual(LEAF, mid, $"gap {g} <= 2 r was not closed");
                else Assert.AreEqual(0, mid, $"gap {g} > 2 r was closed");
                // the outer 2 voxels of the blocks' top stay as grown: nothing written at y >= 10
                for (int z = 0; z < 12; z++) for (int x = 0; x < 24 + g; x++)
                    for (int y = 10; y < 12; y++)
                    {
                        bool orig = !(x >= 12 && x < 12 + g) && (H(x >> 1, y >> 1, z >> 1) & 0xFF) >= 100;
                        Assert.AreEqual(orig ? LEAF : (byte)0, v.Get(new int3(x, y, z)), $"gap {g}: the skin changed at {x},{y},{z}");
                    }
                UnityEngine.Debug.Log($"[crowncore] closed gap {g}: solid {st.solid} closed {st.closed} hull {st.hull} core {st.core} filled {st.filled}");
            }
        }

        /// Why air at p may rightly stay (the rule): a voxel within Chebyshev 2 of p with no crown on one side of an axis line; null = none.
        private static string Why(TreeGallery.Vol v, int3 p, int3 blo, int3 bhi)
        {
            for (int dy = -2; dy <= 2; dy++) for (int dz = -2; dz <= 2; dz++) for (int dx = -2; dx <= 2; dx++)
            {
                var q = p + new int3(dx, dy, dz);
                for (int ax = 0; ax < 3; ax++) for (int sg = -1; sg <= 1; sg += 2)
                {
                    bool hit = false; int t = 0;
                    for (; t < 500; t++) { var r = q; r[ax] += sg * t; if (math.any(r < blo - 4) || math.any(r > bhi + 4)) break; if (v.Get(r) != 0) { hit = true; break; } }
                    if (!hit) return $"{q} (within 2) has no crown along axis {ax} sign {sg}";
                }
            }
            return null;
        }

        private static void Shell(TreeGallery.Vol v, int r, int thick = 1)
        {
            for (int y = -r - 1; y <= r + 1; y++) for (int z = -r - 1; z <= r + 1; z++) for (int x = -r - 1; x <= r + 1; x++)
            {
                int d2 = x * x + y * y + z * z;
                if (d2 <= r * r && d2 > (r - thick) * (r - thick)) v.SetIfEmpty(new int3(x, y, z), LEAF);
            }
        }

        private static HashSet<int3> Solids(TreeGallery.Vol v)
        {
            var h = new HashSet<int3>();
            for (int bi = 0; bi < v.index.Length; bi++)
            {
                int sl = v.index[bi]; if (sl < 0) continue;
                int3 bo = v.lo + new int3(bi % v.nb.x, bi / (v.nb.x * v.nb.z), (bi / v.nb.x) % v.nb.z) * 8;
                var b = v.bricks[sl];
                for (int j = 0; j < 512; j++) if (b[j] != 0) h.Add(bo + new int3(j & 7, j >> 6, (j >> 3) & 7));
            }
            return h;
        }

        /// Every ADDED voxel is 6-connected (through added voxels) to an original solid one: the fill never floats.
        private static void AssertAddedConnected(TreeGallery.Vol v, HashSet<int3> orig)
        {
            var now = Solids(v); var seen = new HashSet<int3>(orig); var st = new Stack<int3>(orig);
            int3[] d = { new int3(1, 0, 0), new int3(-1, 0, 0), new int3(0, 1, 0), new int3(0, -1, 0), new int3(0, 0, 1), new int3(0, 0, -1) };
            while (st.Count > 0)
            {
                var p = st.Pop();
                foreach (var e in d) { var q = p + e; if (now.Contains(q) && seen.Add(q)) st.Push(q); }
            }
            Assert.AreEqual(now.Count, seen.Count, "added voxels not connected to the original crown (floating)");
        }

        // ------------------------------------------------------------------
        private static int Code(Trees2.ProtoBuild pb, int3 p)
        {
            int3 q = p - pb.lo;
            if (math.any(q < 0) || math.any(q >= pb.nb * 8)) return 0;
            int3 b = q >> 3;
            int e = pb.index[(b.y * pb.nb.z + b.z) * pb.nb.x + b.x];
            if (e < 0) return 0;
            int3 w = q & 7;
            int off = (w.y << 6) | (w.z << 3) | w.x;
            return (pb.codes[(e >> 2) * 128 + (off >> 2)] >> ((off & 3) * 2)) & 3;
        }

        /// Every voxel of a packed prototype 6-connected to its wood (codes 1 / 3).
        private static void AssertConnectedProto(Trees2.ProtoBuild pb, string nm)
        {
            var seen = new HashSet<int3>(); var st = new Stack<int3>(); int total = 0;
            for (int y = pb.vlo.y; y <= pb.vhi.y; y++) for (int z = pb.vlo.z; z <= pb.vhi.z; z++) for (int x = pb.vlo.x; x <= pb.vhi.x; x++)
            {
                var p = new int3(x, y, z); int c = Code(pb, p);
                if (c == 0) continue;
                total++;
                if (c != 2 && seen.Add(p)) st.Push(p);
            }
            int3[] d = { new int3(1, 0, 0), new int3(-1, 0, 0), new int3(0, 1, 0), new int3(0, -1, 0), new int3(0, 0, 1), new int3(0, 0, -1) };
            while (st.Count > 0)
            {
                var p = st.Pop();
                foreach (var e in d) { var q = p + e; if (Code(pb, q) != 0 && seen.Add(q)) st.Push(q); }
            }
            Assert.AreEqual(total, seen.Count, nm + ": voxels not 6-connected to the wood");
        }

        [Test, Category("Slow")]
        public void Prototypes_OffIdentical_OnOnlyAirToLeaf()
        {
            var cases = new[] { new int2(TreeGallery.SP_BIRCH, 0), new int2(TreeGallery.SP_SPRUCE, 2), new int2(TreeGallery.SP_ACACIA, 0), new int2(TreeGallery.SP_OAK, 0) };
            foreach (var c in cases)
            {
                string nm = TreeGallery.SpeciesName[c.x] + " v" + c.y;
                var a = Trees2.BuildProto(c.x, c.y);
                var off = Trees2.BuildProto(c.x, c.y, 0);
                CollectionAssert.AreEqual(a.codes, off.codes, nm + ": core 0 changed the codes");
                CollectionAssert.AreEqual(a.index, off.index, nm + ": core 0 changed the index");
                Assert.AreEqual(a.bricks, off.placeBricks, nm + ": placeBricks != bricks when off");
                for (int mode = 1; mode <= 2; mode++) {
                var on = Trees2.BuildProto(c.x, c.y, mode);
                Assert.AreEqual(a.bricks, on.bricks, nm + ": the library fill created bricks (keepBricks)");
                Assert.AreEqual(a.bricks, on.placeBricks, nm + ": placement bricks changed");
                Assert.AreEqual(a.lo, on.lo); Assert.AreEqual(a.nb, on.nb);
                Assert.AreEqual(a.vlo, on.vlo, nm + ": vlo"); Assert.AreEqual(a.vhi, on.vhi, nm + ": vhi");
                Assert.AreEqual(a.reach, on.reach); Assert.AreEqual(a.keep, on.keep); Assert.AreEqual(a.leafBottom, on.leafBottom);
                Assert.AreEqual(a.leafReach, on.leafReach); Assert.AreEqual(a.footR, on.footR); Assert.AreEqual(a.top, on.top);
                Assert.AreEqual(a.wood, on.wood, nm + ": wood count changed");
                var cs = on.coreStats;
                int3 blo = cs.boxLo, bhi = cs.boxLo + cs.boxSize - 1;
                int added = 0, bad = 0, nearEnd = 0;
                for (int y = a.vlo.y; y <= a.vhi.y; y++)
                for (int z = a.vlo.z; z <= a.vhi.z; z++)
                for (int x = a.vlo.x; x <= a.vhi.x; x++)
                {
                    var p = new int3(x, y, z);
                    int ca = Code(a, p), cb = Code(on, p);
                    if (ca == cb) continue;
                    if (ca != 0 || cb != 2 || math.any(p < blo) || math.any(p > bhi)) { bad++; continue; }
                    added++;
                    // never within SKIN of the line's original extent along any axis
                    for (int ax = 0; ax < 3 && nearEnd == 0; ax++)
                        for (int sgn = -1; sgn <= 1; sgn += 2)
                        {
                            bool hit = false;
                            for (int t = CrownCore.SKIN; t <= 1024 && !hit; t++)
                            {
                                var q = p; q[ax] += sgn * t;
                                if (math.any(q < blo) || math.any(q > bhi)) break;
                                hit = Code(a, q) != 0;
                            }
                            if (!hit) { nearEnd++; break; }
                        }
                }
                Assert.AreEqual(0, bad, nm + ": core 1 changed a non-air voxel or wrote outside the leaf box / not leaf");
                Assert.AreEqual(on.coreStats.filled, added, nm + ": fill count != changed voxels");
                Assert.AreEqual(on.leaf, a.leaf + added, nm + ": leaf count");
                if (mode == 1) Assert.AreEqual(0, nearEnd, nm + ": a voxel inside the skin was filled");
                AssertConnectedProto(on, nm + " mode " + mode);
                UnityEngine.Debug.Log($"[crowncore] {nm} mode {mode}: leaf {a.leaf:N0} -> {on.leaf:N0} (+{added:N0}), bricks {a.bricks} -> {on.bricks}, hull {cs.hull:N0}, dropped {cs.dropped:N0}, {on.ms:F0} ms (off {a.ms:F0}), close {cs.close}, solid {cs.solid:N0} closed {cs.closed:N0} core {cs.core:N0}");
                }
            }
        }
    }
}
