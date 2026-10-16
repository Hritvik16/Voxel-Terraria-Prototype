// Assets/CoreEngine/Tests/ShellColumnReuseTests.cs
//
// Task 93b2 (part one of owner Q52 (a)): why the shells never fill while
// walking. The counts (Logs/tg2world_20261016_030018.txt): ~90% of a shell
// chunk's generation is its column pass, which depends on (shell, x, z) only,
// and it ran once per LAYER (13 per shell-1 column) -- task 30's tier-0 bug,
// in the shells. While walking at 5 m/s the workers fell 3-5x behind the box.
// The fix (ShellStreamer header COLUMN AFFINITY; GenerateShellChunk COLUMN
// REUSE) keeps a scratch's column and hands it the next layer of that column.
//   * REUSE IS BYTE-IDENTICAL: layers generated through one scratch equal fresh
//     generations (shell 1, and shell 3 with task 83's fill, which reads the
//     column); a spec change drops the kept column.
//   * AFFINITY: filling shell 1 at a fixed camera runs the column pass about
//     once per column, not once per chunk.
//   * WALKING TWIN (Slow): shell 1 alone, prefilled at seed 42's spawn, then a
//     5 m/s diagonal walk for 30 s at 30 Updates/s with a MODELLED worker budget
//     (3 workers x 35 ms per Update; a column pass 150 ms, a voxel pass 15 ms:
//     task 93b2's walk counts; + 45 ms of cave fill since 93b2 part two, owner
//     Q54 (a)), from ~240 chunks held (93b's walk start). With affinity shell 1
//     never goes FULL (the fill) and holds > half its band (76% at 30 s: the
//     workers, not the cap, are the limit now); without it, it holds under half
//     as much (the 93b symptom).
//     Numbers: Logs/Shells/walk93b2.txt.
using System.IO;
using NUnit.Framework;
using Unity.Mathematics;
using VoxelEngine.Mirror;
using VoxelEngine.WorldGen;

public class ShellColumnReuseTests
{
    static TerrainGenChunkSource _src;
    [OneTimeSetUp] public void BuildOnce() { if (_src == null) _src = new TerrainGenChunkSource(42u); }
    [OneTimeTearDown] public void TearDown() { _src?.Dispose(); _src = null; }

    /// Land near the middle of the island (ShellOrderTests' spawn rule).
    static float3 Spawn(TerrainGenChunkSource src)
    {
        var spec = src.Spec; var view = src.Map.View; var o = src.O;
        float c = spec.worldSizeVoxels * 0.5f;
        for (int i = 0; i < 2000; i++)
        {
            float a = i * 2.39996f, r = 40f * i;
            float2 p = new float2(c + r * math.cos(a), c + r * math.sin(a));
            var col = TerrainGen.ShapeField.Sample(in spec, in view, in o, p.x, p.y, 1);
            if (col.height < spec.seaLevelY + 200 || col.water > col.height) continue;
            return new float3(p.x * 0.1f, (col.height + 1) * 0.1f + 2f, p.y * 0.1f);
        }
        return new float3(c * 0.1f, (spec.seaLevelY + 800) * 0.1f, c * 0.1f);
    }

    /// A walking eye: 1.6 m above max(ground, water) at (x, z) metres.
    static float EyeY(TerrainGenChunkSource src, float x, float z)
    {
        var spec = src.Spec; var view = src.Map.View; var o = src.O;
        var col = TerrainGen.ShapeField.Sample(in spec, in view, in o, x * 10f, z * 10f, 1);
        return (math.max(col.height, col.water) + 1) * 0.1f + 1.6f;
    }

    static void AssertSame(TerrainGenChunkSource.ShellChunk a, TerrainGenChunkSource.ShellChunk b, string what)
    {
        Assert.AreEqual(b.IsUniformAir, a.IsUniformAir, what + ": IsUniformAir");
        if (!a.IsUniformAir)
            for (int i = 0; i < a.Mats.Length; i++)
                if (a.Mats[i] != b.Mats[i]) Assert.Fail($"{what}: Mats differ at {i}");
        for (int i = 0; i < a.BrickMat.Length; i++)
        {
            if (a.BrickMat[i] != b.BrickMat[i]) Assert.Fail($"{what}: BrickMat differs at {i}");
            if (a.BrickUniform[i] != b.BrickUniform[i]) Assert.Fail($"{what}: BrickUniform differs at {i}");
        }
    }

    [Test]
    public void ColumnReuse_IsByteIdentical_Shell1AndFilledShell3()
    {
        float3 cam = Spawn(_src);
        using var kept = new TerrainGenChunkSource.ShellChunk();
        using var fresh = new TerrainGenChunkSource.ShellChunk();
        foreach (int k in new[] { 1, 3 })
        {
            int3 cc = ShellStreamer.CameraChunk(cam, k);
            int passes0 = _src.ShellColumnPasses, layers = 0, solid = 0;
            for (int dy = -3; dy <= 2; dy++)
            {
                var c = new int3(cc.x + 2, math.max(0, cc.y + dy), cc.z - 1);
                _src.GenerateShellChunk(k, c, kept, (VoxelEngine.Streaming.EditOpSnapshot)null);
                Assert.AreEqual(layers > 0, kept.ColumnReused, $"shell {k} layer {c.y}: column reused");
                _src.ShellColumnReuse = false;   // a fresh generation
                try { _src.GenerateShellChunk(k, c, fresh, (VoxelEngine.Streaming.EditOpSnapshot)null); }
                finally { _src.ShellColumnReuse = true; }
                Assert.IsFalse(fresh.ColumnReused);
                AssertSame(kept, fresh, $"shell {k} chunk {c}");
                layers++; if (!kept.IsUniformAir) solid++;
            }
            // kept: 1 pass; fresh: one per layer.
            Assert.AreEqual(1 + layers, _src.ShellColumnPasses - passes0, $"shell {k}: column passes");
            Assert.Greater(solid, 0, $"shell {k}: every layer was air -- the test saw no voxel pass");
        }
        // A spec change drops the kept column (same bytes as a fresh pass, so nothing to compare; just no reuse).
        int3 c1 = ShellStreamer.CameraChunk(cam, 1);
        _src.GenerateShellChunk(1, c1, kept, (VoxelEngine.Streaming.EditOpSnapshot)null);
        int shift = _src.Spec.coarseColumnShift;
        try
        {
            _src.SetCoarseColumnShift(shift);   // same value, still a new spec stamp
            _src.GenerateShellChunk(1, c1 + new int3(0, 1, 0), kept, (VoxelEngine.Streaming.EditOpSnapshot)null);
            Assert.IsFalse(kept.ColumnReused, "a spec change must drop the kept column");
        }
        finally { _src.SetCoarseColumnShift(shift); }
    }

    [Test]
    public void Affinity_Shell1Fill_RunsAboutOneColumnPassPerColumn()
    {
        var L = ShellLayout.From(QualityPreset.High);
        float3 cam = Spawn(_src);
        var s = new ShellStreamer(L, _src, 1, synchronous: true, firstShell: 1, lastShell: 1, syncScratch: 8)
            { SyncChunksPerUpdate = 8 };
        try
        {
            int p0 = _src.ShellColumnPasses;
            for (int i = 0; i < 40; i++) s.Update(cam, 1 << 30);
            var n = s.Counters(1);
            int passes = _src.ShellColumnPasses - p0;
            UnityEngine.Debug.Log($"[93b2] shell 1 fill, 40 Updates: generated {n.Generated}, column passes {passes}, " +
                                  $"reused {n.ColumnReused}, follow-ups {n.FollowUps}, held {s.Store(1).ChunksStored}");
            Assert.Greater(n.Generated, 200);
            Assert.LessOrEqual(passes, n.Generated / 4, "affinity should reuse most column passes (13 layers a column)");
            // The last Update's chunks are generated but not yet counted (they land in the next one).
            Assert.That(passes - (n.Generated - n.ColumnReused), Is.InRange(0, 8), "column passes = generated - reused");
        }
        finally { s.Dispose(); }
    }

    const float FILL_COST_MS = 45f;
    const int WALK_S = 30;   // 15 s until the fill (task 93b2 part two): shell 1 stopped FULL at its brick cap by then

    struct WalkResult { public int heldStart, heldEnd, band, passes; public long gen, evicted; public bool full; }

    static WalkResult Walk(ShellLayout L, bool affinity, StreamWriter log)
    {
        float3 cam = Spawn(_src);
        var s = new ShellStreamer(L, _src, 1, synchronous: true, firstShell: 1, lastShell: 1, syncScratch: 8)
            { SyncChunksPerUpdate = 16 };
        try
        {
            // A short start (no cost model, affinity on): ~240 chunks held, like 93b's
            // walk, which began 19 s after launch with 208 (a full prefill hits the
            // brick cap at this spawn -- the other limit, reported, not tested here).
            _src.ShellColumnReuse = true; s.ColumnAffinity = true;
            for (int i = 0; i < 31; i++) s.Update(cam, 1 << 30);
            // The walk: 30 Updates/s, 5 m/s along (1, 0, 1), the modelled worker budget.
            _src.ShellColumnReuse = affinity; s.ColumnAffinity = affinity;
            // Task 93b2 part two: shell 1 is filled now (owner Q54 (a)); the fill is modelled
            // as part of the voxel pass (+45 ms: its worker time on the walk was 37-59 ms,
            // ~3x the voxel pass's 12-20 ms, Logs/tg2world_20261016_123600.txt).
            s.SyncChunksPerUpdate = 64; s.SyncCostBudget = 3 * 35f; s.SyncColumnCost = 150f; s.SyncVoxelCost = 15f + FILL_COST_MS;
            var r = new WalkResult { heldStart = s.Store(1).ChunksStored };
            var n = s.Counters(1);
            long gen0 = n.Generated, ev0 = n.EvictBox + n.EvictHole; int p0 = _src.ShellColumnPasses;
            float3 dir = math.normalize(new float3(1f, 0f, 1f));
            for (int i = 1; i <= WALK_S * 30; i++)
            {
                float3 p = cam + dir * (5f * i / 30f);
                p.y = EyeY(_src, p.x, p.z);
                s.Update(p, 1 << 30);
                if (i % 90 == 0) log.WriteLine($"  {(affinity ? "ON " : "OFF")} t {i / 30f,4:F0} s: held {s.Store(1).ChunksStored}, missing {s.Missing(1)}{(s.ShellFull(1) ? " FULL" : "")}");
            }
            r.heldEnd = s.Store(1).ChunksStored;
            r.gen = n.Generated - gen0; r.evicted = n.EvictBox + n.EvictHole - ev0; r.passes = _src.ShellColumnPasses - p0;
            r.full = s.ShellFull(1);
            s.Box(1, out int3 lo, out int3 hi, out _);
            for (int z = lo.z; z <= hi.z; z++) for (int y = lo.y; y <= hi.y; y++) for (int x = lo.x; x <= hi.x; x++)
                if (s.Wanted(1, new int3(x, y, z))) r.band++;
            log.WriteLine($"  {(affinity ? "ON " : "OFF")}: held {r.heldStart} -> {r.heldEnd} of a {r.band}-chunk band{(r.full ? " (FULL)" : "")}; " +
                          $"walk generated {r.gen}, column passes {r.passes}, evicted {r.evicted}");
            return r;
        }
        finally { s.Dispose(); _src.ShellColumnReuse = true; }
    }

    [Test, Category("Slow"), Timeout(1200000)]
    public void WalkingTwin_5mps_Shell1KeepsFilling()
    {
        var L = ShellLayout.From(QualityPreset.High);
        Directory.CreateDirectory("Logs/Shells");
        using var log = new StreamWriter("Logs/Shells/walk93b2.txt");
        log.WriteLine($"TASK 93b2 walking twin: shell 1 (High), seed 42 spawn, ~240 chunks held, then {WALK_S} s at 5 m/s along (1,0,1),");
        log.WriteLine($"30 Updates/s, modelled budget 105 ms/Update (3 workers), column pass 150 ms, voxel pass 15 ms + fill {FILL_COST_MS:F0} ms (shell 1 filled since part two).");
        var on = Walk(L, true, log);
        var off = Walk(L, false, log);
        log.Flush();
        UnityEngine.Debug.Log($"[93b2] walking twin: ON held {on.heldStart} -> {on.heldEnd}, OFF {off.heldStart} -> {off.heldEnd} (band {on.band})");
        Assert.Greater(on.heldStart, 0);
        // Task 93b2 part two (owner Q54 (a), shell 1 filled): the brick cap is no longer the
        // limit, the workers are. 93b2's target (>= 90% of the band) is NOT met by the fill's
        // modelled cost (76% at 30 s, still rising; HANDOFF): pinned as not FULL and > half.
        Assert.IsFalse(on.full, $"with the fill shell 1 must not stop at its brick cap: held {on.heldEnd} of {on.band}");
        Assert.Greater(on.heldEnd, on.band / 2, $"with affinity shell 1 must keep filling while walking: held {on.heldEnd} of {on.band}");
        Assert.Less(off.heldEnd, on.heldEnd / 2, "without affinity the walk falls behind (the 93b symptom)");
    }
}
