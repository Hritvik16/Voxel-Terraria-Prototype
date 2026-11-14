// Assets/CoreEngine/Tests/Tier0LackHandOverTests.cs
//
// Task 197: the SECOND path to task 194's rectangular pits (194b's jungle_high shot:
// pits with 0 chunks refused). RetryGpuRefused clears the whole refused set as soon
// as a GPU slot frees up and re-dirties the chunks, but their re-upload waits in the
// budgeted, nearest-first dirty queue (1,248 chunks deep in task 197's repro). Until
// it lands, the chunk is no longer "refused" -- so task 194's hand-over (refused
// columns only, and only while GpuPoolRefusedNow > 0) is off -- while its tier-0
// slot still holds the AIR its refusal wrote: the same 12.8 m box pit. Same for a
// resident chunk whose first upload is still queued.
// The fix hands over every resident column tier 0 LACKS (TerrainClipmap.
// Tier0LacksChunk: its slot does not hold its own write), via FarField.ColumnState.
// These tests build the retry gap with a real TerrainClipmap (tiny GPU pool, CPU
// shadow) and a real budgeted UploadDirty pass, like RefusedHandOverTests does for
// the first path.
using System;
using NUnit.Framework;
using Unity.Mathematics;
using VoxelEngine.Memory;
using VoxelEngine.Rendering;

public class Tier0LackHandOverTests
{
    private static readonly int3 Box = new int3(4, 2, 4);
    private BrickDataPool _pool;
    private ChunkStore _store;
    private TerrainClipmap _clip;
    private bool _ring;

    [SetUp]
    public void SetUp()
    {
        _ring = UploadRing.Enabled;
        UploadRing.Enabled = false;   // the plain byte budget (the ring would admit one chunk per pass)
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
        UploadRing.Enabled = _ring;
    }

    // RefusedHandOverTests' world: layer 0 uniform stone, layer 1 air except two 20-brick surface chunks.
    private static readonly int3 Kept = new int3(1, 1, 1), Refused = new int3(2, 1, 1);
    private static readonly float3 Eye = new float3(64f, 300f, 192f);
    private static readonly float3 PitFloor = new float3(2 * 128 + 64, 128f, 1 * 128 + 64);
    private static readonly int3 FarCamera = new int3(100, 0, 100);   // nothing exempt from the budget

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

    /// Refused, then a slot frees (Kept evicted), then ONE budgeted pass: the retry clears the refused
    /// set and re-dirties Refused, but the zero byte budget defers its upload -- 194b's state.
    private void BuildRetryGap()
    {
        BuildWorld();
        Assert.IsTrue(_clip.IsGpuRefused(Refused), "precondition: the pool refused the second surface chunk");
        _store.EvictChunk(Kept); _clip.MarkDirty(Kept);
        _clip.FlushAllDirty(_store, _pool);   // Kept's clear frees its 20 slots (the retry runs at the NEXT pass)
        Assert.IsTrue(_clip.IsGpuRefused(Refused));
        _clip.UploadDirty(_store, _pool, 0, FarCamera, 0);
    }

    private bool RefusedCol(int2 rc) => FarField.ColumnRefused(rc.x, rc.y, 0, 1, _clip);
    private bool HandOverCol(int2 rc) => FarField.ColumnHandOver(_store, rc.x, rc.y, 0, 1, _clip);
    private bool Resident(int2 rc) => FarField.ColumnResident(_store, rc.x, rc.y, 0, 1, _clip);

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

    [Test]
    public void RetryGap_RefusedCountIsZero_ButTheChunkIsStillAirInTierZero()
    {
        BuildRetryGap();
        Assert.AreEqual(0, _clip.GpuPoolRefusedNow, "the retry cleared the refused set: 194's hand-over flag is OFF");
        Assert.IsFalse(_clip.IsGpuRefused(Refused));
        Assert.IsTrue(_clip.IsDirty(Refused), "its re-upload is queued behind the budget");
        Assert.IsTrue(_store.IsResident(Refused));
        for (int i = 0; i < 16; i++)
            Assert.AreEqual(0u, _clip.CpuMirrorHandle(Refused, new int3(i, 0, 3)), "tier 0 still holds the refusal's air");
        Assert.AreEqual((uint)Materials.Stone, _clip.CpuMirrorHandle(Refused - new int3(0, 1, 0), new int3(8, 15, 8)),
                        "and the rock below it: the pit's floor");
        Assert.IsFalse(RefusedCol(new int2(2, 1)), "194's rule: not refused, so not handed over (the pit)");
        Assert.IsTrue(_clip.Tier0LacksChunk(Refused, _store.GetChunk(Refused)), "197's rule: tier 0 lacks it");
        Assert.IsTrue(HandOverCol(new int2(2, 1)), "so its column hands over");
        Assert.IsFalse(Resident(new int2(2, 1)), "and is non-resident (the far band's cue too)");
    }

    [Test]
    public void RetryGap_PitRay_OldRuleKeepsTheFloor_NewRuleEndsAtTheColumnEntry()
    {
        BuildRetryGap();
        float3 d = math.normalize(PitFloor - Eye);
        float tHit = Tier0Hit(Eye, d);
        float3 hp = Eye + d * tHit;
        Assert.Greater(tHit, 0f);
        Assert.AreEqual(2, (int)math.floor(hp.x) >> 7, $"tier 0 hits the pit floor inside the retried column, at {hp}");
        Assert.Less(hp.y, 128.5f);

        // Task 194's flag and bits: refused now 0 -> the block does not run -> the floor is drawn.
        Assert.IsTrue(Tier0HandOverReference.KeepsHit(_clip.GpuPoolRefusedNow > 0, RefusedCol, int2.zero, Box.xz, Eye, d, tHit, out _),
                      "194b's pit: the old hand-over keeps the floor");
        // Task 197: the flag is 'any hand-over column' and the bit is the lack.
        bool anyHandOver = false;
        for (int z = 0; z < 4; z++) for (int x = 0; x < 4; x++) anyHandOver |= HandOverCol(new int2(x, z));
        Assert.IsTrue(anyHandOver);
        Assert.IsFalse(Tier0HandOverReference.KeepsHit(anyHandOver, HandOverCol, int2.zero, Box.xz, Eye, d, tHit, out float tEnd),
                       "the floor is not tier 0's: the shells draw this column");
        Assert.AreEqual((2 * 128 - Eye.x) / d.x, tEnd, 1e-2f, "tier 0 ends where the ray enters the column");
    }

    [Test]
    public void RetryGap_OnceTheUploadLands_TierZeroOwnsTheColumnAgain()
    {
        BuildRetryGap();
        _clip.FlushAllDirty(_store, _pool);
        Assert.IsFalse(_clip.IsDirty(Refused));
        Assert.IsFalse(_clip.Tier0LacksChunk(Refused, _store.GetChunk(Refused)));
        Assert.IsFalse(HandOverCol(new int2(2, 1)));
        for (int z = 0; z < 4; z++) for (int x = 0; x < 4; x++)
            Assert.IsFalse(HandOverCol(new int2(x, z)), $"no column hands over once everything landed ({x},{z})");
        float3 d = math.normalize(PitFloor - Eye);
        float tHit = Tier0Hit(Eye, d);
        Assert.Greater(tHit, 0f);
        Assert.IsTrue(Tier0HandOverReference.KeepsHit(false, HandOverCol, int2.zero, Box.xz, Eye, d, tHit, out _));
    }

    [Test]
    public void FirstUploadQueued_IsHandedOver_AnUploadedAirSlotIsNot()
    {
        BuildWorld();
        int3 c = new int3(3, 1, 2);
        _store.EvictChunk(c); _clip.MarkDirty(c);
        _clip.FlushAllDirty(_store, _pool);                 // the eviction's clear: slot not held
        Assert.IsFalse(HandOverCol(new int2(3, 2)), "a streaming (non-resident) column is never handed over (194's rule)");

        _store.InsertChunk(new Chunk { coord = c, isUniform = true, uniformMaterial = Materials.Air });
        _clip.MarkDirty(c);
        Assert.IsFalse(_clip.Tier0LacksChunk(c, _store.GetChunk(c)), "air over an empty slot is drawn right: no hand-over");
        Assert.IsFalse(HandOverCol(new int2(3, 2)));

        _store.EvictChunk(c);
        _store.InsertChunk(new Chunk { coord = c, isUniform = true, uniformMaterial = Materials.Stone });
        _clip.MarkDirty(c);                                  // generated, first upload still queued
        Assert.IsTrue(_clip.Tier0LacksChunk(c, _store.GetChunk(c)), "rock not yet uploaded: tier 0 lacks it");
        Assert.IsTrue(HandOverCol(new int2(3, 2)));
        _clip.FlushAllDirty(_store, _pool);
        Assert.IsFalse(HandOverCol(new int2(3, 2)), "landed: tier 0's again");
    }

    [Test]
    public void EvictedSurfaceChunk_HandsOverWhereTheShellHoldsIt_NotInTheShellHole()
    {
        // 197's third path (h1's jungle_high pit): the CPU pool's LRU valve evicts a visible surface chunk; its
        // clear writes air into tier 0 while it reloads, the rock under it stays: a pit, and the column is
        // non-resident (streaming), which task 194 never handed over.
        BuildWorld();
        _store.EvictChunk(Kept); _clip.MarkDirty(Kept);
        _clip.FlushAllDirty(_store, _pool);                       // the eviction's clear
        for (int i = 0; i < 16; i++) Assert.AreEqual(0u, _clip.CpuMirrorHandle(Kept, new int3(i, 0, 3)));
        Assert.IsFalse(_store.IsResident(Kept));
        int2 col = new int2(1, 1);
        Func<int3, bool> shellHolds = c => true, inHole = c => false;
        Assert.IsFalse(Resident(col));
        Assert.IsTrue(FarField.ColumnHandOver(_store, col.x, col.y, 0, 1, _clip, shellHolds), "shell 1 holds it: hand over");
        Assert.IsFalse(FarField.ColumnHandOver(_store, col.x, col.y, 0, 1, _clip, inHole), "in shell 1's hole: stays tier 0's");
        Assert.IsFalse(FarField.ColumnHandOver(_store, col.x, col.y, 0, 1, _clip), "no shell predicate: 194's rule");
        FarField.LackHandOver = false;
        try { Assert.IsFalse(FarField.ColumnHandOver(_store, col.x, col.y, 0, 1, _clip, shellHolds), "_lh0 arm: 194's rule"); }
        finally { FarField.LackHandOver = true; }

        float3 floor = new float3(1 * 128 + 64, 128f, 1 * 128 + 64);   // the rock under the evicted chunk
        float3 eye = new float3(-200f, 300f, 192f);
        float3 d = math.normalize(floor - eye);
        float tHit = Tier0Hit(eye, d);
        float3 hp = eye + d * tHit;
        Assert.AreEqual(1, (int)math.floor(hp.x) >> 7, $"tier 0 hits the pit floor in the evicted column, at {hp}");
        Func<int2, bool> bitHolds = rc => FarField.ColumnHandOver(_store, rc.x, rc.y, 0, 1, _clip, shellHolds);
        Func<int2, bool> bitHole = rc => FarField.ColumnHandOver(_store, rc.x, rc.y, 0, 1, _clip, inHole);
        Assert.IsFalse(Tier0HandOverReference.KeepsHit(true, bitHolds, int2.zero, Box.xz, eye, d, tHit, out float tEnd),
                       "the shells draw the evicted column");
        Assert.AreEqual((1 * 128 - eye.x) / d.x, tEnd, 1e-2f, "from the ray's entry into it");
        Assert.IsTrue(Tier0HandOverReference.KeepsHit(true, bitHole, int2.zero, Box.xz, eye, d, tHit, out _),
                      "inside shell 1's hole tier 0 keeps it (194's lost-houses rule)");

        InsertDense(Kept);                                         // reloaded
        _clip.FlushAllDirty(_store, _pool);
        Assert.IsFalse(FarField.ColumnHandOver(_store, col.x, col.y, 0, 1, _clip, shellHolds), "landed: tier 0's again");
    }

    [Test]
    public void ForeignSlot_AfterARecentre_IsHandedOverUntilRewritten()
    {
        // The box moves by one chunk in x: position (4,1,1) takes the slot (0,1,1) held. Until (4,1,1)'s own
        // write lands, tier 0 would draw (0,1,1)'s content there: lacking, even for an air chunk.
        BuildWorld();
        int3 c = new int3(4, 1, 1);
        _store.InsertChunk(new Chunk { coord = c, isUniform = true, uniformMaterial = Materials.Air });
        _clip.SetWindowOrigin(new int3(1, 0, 0));
        Assert.IsTrue(_clip.Tier0LacksChunk(c, _store.GetChunk(c)), "slot held by another chunk");
        _clip.MarkDirty(c);
        _clip.FlushAllDirty(_store, _pool);
        Assert.IsFalse(_clip.Tier0LacksChunk(c, _store.GetChunk(c)));
        Assert.IsFalse(_clip.Tier0LacksChunk(new int3(9, 1, 1), new Chunk { coord = new int3(9, 1, 1), isUniform = true }),
                       "outside the box: not tier 0's concern");
    }
}
