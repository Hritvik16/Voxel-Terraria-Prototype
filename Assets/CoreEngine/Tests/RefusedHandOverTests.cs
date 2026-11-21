// Assets/CoreEngine/Tests/RefusedHandOverTests.cs
//
// Task 194: the RECTANGULAR PITS of 191's town shots. A chunk the full near GPU
// body pool refuses is held as air in tier 0 and its column is non-resident, so the
// shells should draw it. But the chunk below it is uniform rock (no GPU body
// needed), so tier 0 still holds it: a ray through the refused chunk's air hit that
// rock -- a 12.8 m box pit. These tests build exactly that with a real
// TerrainClipmap (tiny GPU pool, CPU shadow), march a ray over its CPU mirror at
// brick level, and check the hand-over rule (Tier0HandOverReference, the CPU twin
// of Raymarch.compute's REFUSED-CHUNK HAND-OVER block): that hit is dropped at the
// refused column's entry, a hit before any refused column is kept, and once the
// refused chunk lands the hit is tier 0's again; a column non-resident only because
// it is streaming is never handed over.
using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;
using VoxelEngine.Memory;
using VoxelEngine.Rendering;

public class RefusedHandOverTests
{
    private static readonly int3 Box = new int3(4, 2, 4);
    private BrickDataPool _pool;
    private ChunkStore _store;
    private TerrainClipmap _clip;

    [SetUp]
    public void SetUp()
    {
        _pool = new BrickDataPool(16384);
        _store = new ChunkStore(_pool, new ChunkHandleAllocator(256));
        _clip = new TerrainClipmap(Box, _pool.Capacity, 25) { RecentreDirtiesStaleSlots = true };
        _clip.EnableCpuShadow();
        _clip.SetWindowOrigin(int3.zero);
    }

    [TearDown]
    public void TearDown()
    {
        _clip?.Dispose();
        _pool?.Dispose();
    }

    // Layer 0: uniform stone everywhere (the ground under the surface). Layer 1: air,
    // except two "surface" chunks with 20 dense bricks each on their local brick row z = 3.
    private static readonly int3 Kept = new int3(1, 1, 1), Refused = new int3(2, 1, 1);

    private void BuildWorld()
    {
        for (int z = 0; z < 4; z++)
        for (int x = 0; x < 4; x++)
        {
            _store.InsertChunk(new Chunk { coord = new int3(x, 0, z), isUniform = true, uniformMaterial = Materials.Stone });
            _clip.MarkDirty(new int3(x, 0, z));
            var c1 = new int3(x, 1, z);
            if (c1.Equals(Kept) || c1.Equals(Refused)) continue;
            _store.InsertChunk(new Chunk { coord = c1, isUniform = true, uniformMaterial = Materials.Air });
            _clip.MarkDirty(c1);
        }
        InsertDense(Kept);
        _clip.FlushAllDirty(_store, _pool);   // Kept takes 20 of the 25 GPU slots
        InsertDense(Refused);
        _clip.FlushAllDirty(_store, _pool);   // Refused needs 20 more: refused
    }

    private void InsertDense(int3 c)
    {
        _store.InsertChunk(new Chunk { coord = c, isUniform = true, uniformMaterial = Materials.Air });
        int3 v0 = c * EngineConfig.CHUNK_EDGE_VOXELS;
        for (int i = 0; i < 20; i++)
            _store.SetVoxel(v0 + new int3(i & 15, 0, 3) * 8 + new int3(1, 2, 3), Materials.Stone);
        _clip.MarkDirty(c);
    }

    private bool Resident(int2 rc) => FarField.ColumnResident(_store, rc.x, rc.y, 0, 1, _clip);
    private bool RefusedCol(int2 rc) => FarField.ColumnRefused(rc.x, rc.y, 0, 1, _clip);

    /// Tier 0 at brick level over the clipmap's CPU mirror: t of the first non-air brick (or -1).
    private float Tier0Hit(float3 o, float3 d)
    {
        float t = 0f;
        for (int i = 0; i < 4000; i++, t += 0.25f)
        {
            float3 p = o + d * t;
            int3 v = (int3)math.floor(p);
            if (math.any(v < 0) || math.any(v >= Box * 128)) { if (p.y < 0f) break; continue; }
            int3 c = v >> 7, lb = (v & 127) >> 3;
            if (_clip.CpuMirrorHandle(c, lb) != 0u) return t;
        }
        return -1f;
    }

    // Camera 30 m over column (0, 1); voxels.
    private static readonly float3 Eye = new float3(64f, 300f, 192f);

    [Test]
    public void Precondition_RefusedChunkIsAir_TheUniformRockBelowIsDrawn()
    {
        BuildWorld();
        Assert.IsTrue(_clip.IsGpuRefused(Refused), "the pool refused the second surface chunk");
        Assert.IsFalse(_clip.IsGpuRefused(Kept));
        for (int i = 0; i < 16; i++)
            Assert.AreEqual(0u, _clip.CpuMirrorHandle(Refused, new int3(i, 0, 3)), "refused chunk held as air");
        Assert.AreEqual((uint)Materials.Stone, _clip.CpuMirrorHandle(Refused - new int3(0, 1, 0), new int3(8, 15, 8)),
                        "the rock under it needs no GPU body: tier 0 still draws it (the pit's floor)");
        Assert.IsFalse(Resident(new int2(2, 1)), "its column is non-resident (the shells' cue)");
        Assert.IsTrue(RefusedCol(new int2(2, 1)), "and marked refused (the hand-over's cue)");
        Assert.IsFalse(RefusedCol(new int2(1, 1)));
        Assert.IsTrue(Resident(new int2(1, 1)));
        Assert.IsTrue(Resident(new int2(3, 1)));
    }

    [Test]
    public void RayIntoThePit_TierZeroHitsTheFloor_HandOverDropsItAtTheColumnEntry()
    {
        BuildWorld();
        float3 floor = new float3(2 * 128 + 64, 128f, 1 * 128 + 64);   // the top of the rock under Refused
        float3 d = math.normalize(floor - Eye);
        float tHit = Tier0Hit(Eye, d);
        float3 hp = Eye + d * tHit;
        Assert.Greater(tHit, 0f);
        Assert.AreEqual(2, (int)math.floor(hp.x) >> 7, $"tier 0 hits inside the refused column (the pit floor), at {hp}");
        Assert.Less(hp.y, 128.5f, "on the rock below the refused chunk");

        bool kept = Tier0HandOverReference.KeepsHit(_clip.GpuPoolRefusedNow > 0, RefusedCol, int2.zero, Box.xz, Eye, d, tHit, out float tEnd);
        float tEntry = (2 * 128 - Eye.x) / d.x;   // the ray crosses x = 256 into column (2, 1)
        Assert.IsFalse(kept, "the pit floor is not tier 0's: the shells draw this column");
        Assert.AreEqual(tEntry, tEnd, 1e-2f, "tier 0 ends where the ray enters the refused column");

        // Without the rule (no refusal flag) the floor would be kept: the old pit.
        Assert.IsTrue(Tier0HandOverReference.KeepsHit(false, RefusedCol, int2.zero, Box.xz, Eye, d, tHit, out _));
    }

    [Test]
    public void HitBeforeAnyRefusedColumn_IsKept()
    {
        BuildWorld();
        float3 target = new float3(1 * 128 + 64, 128f, 1 * 128 + 64);   // rock in front of the refused column
        float3 d = math.normalize(target - Eye);
        float tHit = Tier0Hit(Eye, d);
        Assert.Greater(tHit, 0f);
        Assert.IsTrue(Tier0HandOverReference.KeepsHit(true, RefusedCol, int2.zero, Box.xz, Eye, d, tHit, out float tEnd));
        Assert.AreEqual(tHit, tEnd);
    }

    [Test]
    public void OnceTheRefusedChunkLands_TierZeroOwnsTheColumnAgain()
    {
        BuildWorld();
        _store.EvictChunk(Kept); _clip.MarkDirty(Kept);
        _clip.FlushAllDirty(_store, _pool);   // Kept's clear frees 20 slots
        _clip.FlushAllDirty(_store, _pool);   // Refused retried: lands
        Assert.IsFalse(_clip.IsGpuRefused(Refused));
        Assert.AreEqual(0, _clip.GpuPoolRefusedNow, "the shader flag (_Tier0Refused) is off again");
        _store.InsertChunk(new Chunk { coord = Kept, isUniform = true, uniformMaterial = Materials.Air });   // column (1,1) resident again
        _clip.MarkDirty(Kept); _clip.FlushAllDirty(_store, _pool);
        Assert.IsTrue(Resident(new int2(2, 1)));

        float3 floor = new float3(2 * 128 + 64, 128f, 1 * 128 + 64);
        float3 d = math.normalize(floor - Eye);
        float tHit = Tier0Hit(Eye, d);
        Assert.IsTrue(Tier0HandOverReference.KeepsHit(true, RefusedCol, int2.zero, Box.xz, Eye, d, tHit, out _),
                      "a resident column's hit is tier 0's even while other chunks are refused");
    }

    [Test]
    public void StreamingColumn_IsNotHandedOver_OnlyTheRefusedOne()
    {
        // Column (1, 1) becomes non-resident the streaming way (its layer-1 chunk leaves the store), not
        // refused. Near the camera such a column lies in shell 1's hole, where no shell holds anything,
        // so its hits must stay tier 0's (the first version of the fix stopped at it: lost houses).
        BuildWorld();
        _store.EvictChunk(Kept);
        Assert.IsFalse(Resident(new int2(1, 1)));
        Assert.IsFalse(RefusedCol(new int2(1, 1)));

        float3 near = new float3(1 * 128 + 64, 128f, 1 * 128 + 64);   // rock under the streaming column
        float3 d = math.normalize(near - Eye);
        float tHit = Tier0Hit(Eye, d);
        Assert.IsTrue(Tier0HandOverReference.KeepsHit(true, RefusedCol, int2.zero, Box.xz, Eye, d, tHit, out float tEnd));
        Assert.AreEqual(tHit, tEnd);

        // The pit ray crosses the streaming column first and still ends at the REFUSED column's entry.
        float3 floor = new float3(2 * 128 + 64, 128f, 1 * 128 + 64);
        d = math.normalize(floor - Eye);
        tHit = Tier0Hit(Eye, d);
        Assert.IsFalse(Tier0HandOverReference.KeepsHit(true, RefusedCol, int2.zero, Box.xz, Eye, d, tHit, out tEnd));
        Assert.AreEqual((2 * 128 - Eye.x) / d.x, tEnd, 1e-2f);
    }

    [Test]
    public void Twin_MatchesAPlainColumnWalk_OnRandomRays()
    {
        // FirstColumnT (the shader's ColumnWalkT / FarStartT, line for line) against a fine fixed-step walk over
        // the same columns: the entry t agrees to within the walk's step on 500 random rays.
        var rnd = new Unity.Mathematics.Random(194u);
        var holes = new HashSet<int2> { new int2(2, 1), new int2(0, 3), new int2(3, 0) };
        Func<int2, bool> stop = rc => holes.Contains(rc);
        int2 dims = new int2(4, 4);
        int agreed = 0;
        for (int n = 0; n < 500; n++)
        {
            float3 o = new float3(rnd.NextFloat(-100f, 612f), 200f, rnd.NextFloat(-100f, 612f));
            float3 d = math.normalize(new float3(rnd.NextFloat(-1f, 1f), rnd.NextFloat(-0.5f, 0.1f), rnd.NextFloat(-1f, 1f)));
            float lim = 900f;
            float tTwin = Tier0HandOverReference.FirstColumnT(stop, int2.zero, dims, o, d, lim);
            float tWalk = lim;
            for (float t = 0f; t < lim; t += 0.05f)
            {
                float2 p = o.xz + d.xz * t;
                int2 rc = (int2)math.floor(p / 128f);
                if (math.any(rc < 0) || math.any(rc >= dims)) continue;
                if (stop(rc)) { tWalk = t; break; }
            }
            // The walk skips a column it re-enters after leaving the window; FarStartT stops at the window exit.
            if (math.abs(tTwin - tWalk) <= 0.06f) agreed++;
            else Assert.Fail($"ray {n}: twin {tTwin} walk {tWalk} o {o} d {d}");
        }
        Assert.AreEqual(500, agreed);
    }
}
