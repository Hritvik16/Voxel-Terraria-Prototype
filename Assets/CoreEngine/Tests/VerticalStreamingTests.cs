// Assets/CoreEngine/Tests/VerticalStreamingTests.cs
//
// V2 (design doc §15): a sizeClass 2 (TerrainGen) world streams in Y. The
// StreamManager, ChunkStore, TerrainClipmap and cascades are the production
// ones; the load radius is small (2 chunks) so the test generates ~175 chunks,
// not ~6,700. Checked:
//   - the band is the stream layer +- V_LOAD_LAYERS, clipped to the world;
//   - every chunk of the band is resident (deficit 0) and NONE outside it is;
//   - the resident band fits strictly inside the GPU mirror's toroidal box,
//     whose origin centres on the stream layer (a chunk outside it would alias
//     onto another layer's slot);
//   - the stream layer lags: one layer of camera movement does not move it,
//     two do, and the band follows with evictions;
//   - chunks wholly above the ground take the air fast path;
//   - VoxelCollision's ceiling follows the world's content bound.
// The test above runs with StreamManager.TerrainAwareBand OFF: it pins the V2
// centred rule (exact layers, the lag). TerrainAwareBand_* (2026-10-01) checks
// the placement that is now the default: same invariants, band biased toward
// the visible ground at a cliff foot, centred underground.
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Memory;
using VoxelEngine.Mirror;
using VoxelEngine.Streaming;
using VoxelEngine.WorldGen;

public class VerticalStreamingTests
{
    private const int LOAD_RADIUS = 2;

    private static Vector3 LandSite(TerrainGenChunkSource src, out int groundY)
    {
        var spec = src.Spec; var view = src.Map.View; var o = src.O;
        float c = spec.worldSizeVoxels * 0.5f;
        for (int i = 0; i < 400; i++)
        {
            float2 p = new float2(c, c) + new float2((i % 20) - 10, (i / 20) - 10) * 1500f;
            var col = TerrainGen.ShapeField.Sample(in spec, in view, in o, p.x, p.y, 1);
            if (col.height < spec.seaLevelY + 300 || col.water > col.height) continue;
            groundY = col.height;
            // Eye 2 m above the ground, in metres (0.1 m voxels).
            return new Vector3(p.x * 0.1f, (col.height + 20) * 0.1f, p.y * 0.1f);
        }
        Assert.Fail("no land site");
        groundY = 0;
        return default;
    }

    [Test]
    [Category("Slow")]   // > 10 s: SUITE=all only (task 11)
    public void TallWorld_StreamsABandThatFollowsTheCamera_AndFitsTheMirror()
    {
        var meta = new WorldMetaData { seed = 42, sizeClass = TerrainGenChunkSource.SIZE_CLASS };
        var pool = new BrickDataPool(400000, rangeAware: true);
        var alloc = new ChunkHandleAllocator(1024);
        var store = new ChunkStore(pool, alloc);
        var mirror = new int3(EngineConfig.WINDOW_CHUNKS_XZ, 8, EngineConfig.WINDOW_CHUNKS_XZ);
        var clip = new TerrainClipmap(mirror, pool.Capacity);
        var cascades = new LODCascadeManager(mirror, tier => EngineConfig.CascadeTierPoolCap(tier));
        string deltas = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "vstream_test_deltas");
        if (System.IO.Directory.Exists(deltas)) System.IO.Directory.Delete(deltas, true);
        StreamManager sm = null;
        bool aware = StreamManager.TerrainAwareBand;
        StreamManager.TerrainAwareBand = false;
        try
        {
            sm = new StreamManager(store, pool, alloc, clip, cascades, meta, deltas, LOAD_RADIUS);
            Assert.IsTrue(sm.VerticalStreaming, "sizeClass 2 must stream vertically");
            Assert.AreEqual(sm.TopLayerChunkY, StreamManager.ContentTopChunkY, "collision's ceiling follows the world");

            Vector3 cam = LandSite(sm.TerrainGenSource, out int groundY);
            int camLayer = groundY + 20 >> 7;
            sm.PrimeWindow(cam);
            var clk0 = System.Diagnostics.Stopwatch.StartNew();
            do { sm.WaitForIdle(); } while ((sm.PendingLoads > 0 || sm.InFlightLoads > 0) && clk0.Elapsed.TotalSeconds < 120);
            CheckBand(sm, store, clip, camLayer, "after PrimeWindow");
            Assert.Greater(sm.TerrainGenSource.AirFastPathTotal, 0, "no chunk took the air fast path");
            int residentAtStart = store.ResidentCount;

            // One layer up: inside the lag, the band must NOT move.
            Vector3 up1 = cam + new Vector3(0f, 12.8f, 0f);
            for (int i = 0; i < 4; i++) { sm.Update(up1); sm.WaitForIdle(); }
            Assert.AreEqual(camLayer, sm.StreamLayer, "a one-layer move moved the band (the lag is the hysteresis)");

            // Three layers up from the start: the band follows.
            Vector3 up3 = cam + new Vector3(0f, 3 * 12.8f, 0f);
            int layer3 = (int)math.floor(up3.y * 10f) >> 7;
            var clk3 = System.Diagnostics.Stopwatch.StartNew();
            do { sm.Update(up3); sm.WaitForIdle(); }
            while (sm.LoadDeficit() > 0 && clk3.Elapsed.TotalSeconds < 120);
            CheckBand(sm, store, clip, layer3, "after climbing three layers");

            // TELEPORT WITH LOADS IN FLIGHT (2026-09-26): dispatch a band, then
            // jump 1 km and 2 layers up before it lands. Completions for the old
            // position must be discarded, not inserted -- inserting them left
            // resident chunks outside the window that a later chunk's ring slot
            // collided with ("Insert of chunk ... would overwrite live chunk").
            Vector3 far = up3 + new Vector3(1000f, 0f, 700f);
            sm.Update(cam + new Vector3(40f, 0f, 40f));      // new loads dispatched here...
            sm.Update(far);                                  // ...and the camera leaves before they land
            // Wait on the CONDITION, not on WaitForIdle's fixed iteration cap:
            // on a loaded machine (measured 2026-09-26 with 3.7 GB of swap in
            // use: 281 ms/chunk instead of 10) the cap ran out with loads still
            // in flight and the test reported a deficit that was only latency.
            var clock = System.Diagnostics.Stopwatch.StartNew();
            do { sm.Update(far); sm.WaitForIdle(); }
            while (sm.LoadDeficit() > 0 && clock.Elapsed.TotalSeconds < 120);
            for (int i = 0; i < 2; i++) { sm.Update(far); sm.WaitForIdle(); }
            int farLayer = (int)math.floor(far.y * 10f) >> 7;
            CheckBand(sm, store, clip, farLayer, "after a 1 km teleport with loads in flight");
            Debug.Log($"[V2] teleport: {sm.DiscardedOnArrivalTotal} completions discarded on arrival");
            Debug.Log($"[V2] ground Y {groundY}, band {camLayer}+-{StreamManager.V_LOAD_LAYERS} -> {layer3}; " +
                      $"resident {residentAtStart} -> {store.ResidentCount}; air fast path {sm.TerrainGenSource.AirFastPathTotal}; " +
                      $"dense bricks held {store.DenseBricksHeld}; top layer {sm.TopLayerChunkY}");
        }
        finally
        {
            StreamManager.TerrainAwareBand = aware;
            sm?.Dispose();
            cascades.Dispose(); clip.Dispose(); pool.Dispose();
            if (System.IO.Directory.Exists(deltas)) System.IO.Directory.Delete(deltas, true);
        }
        Assert.AreEqual(StreamManager.MAX_GENERATED_CHUNK_Y, StreamManager.ContentTopChunkY, "Dispose must restore the flat-world ceiling");
    }

    // ------------------------------------------------------------------
    // TERRAIN-AWARE BAND (2026-10-01)
    // ------------------------------------------------------------------
    private const int AWARE_RADIUS = 2;

    /// A land site at the foot of a rise that climbs ABOVE the centred band:
    /// within AWARE_RADIUS chunks the map's surface reaches a layer higher than
    /// the eye's layer + V_LOAD_LAYERS (the `rise` argument is unused).
    private static Vector3 CliffFoot(TerrainGenChunkSource src, int rise, out int groundY, out int topY)
    {
        var spec = src.Spec; var map = src.Map; int n = spec.mapCells, cell = spec.mapCellVoxels;
        int reach = AWARE_RADIUS * 128 / cell;
        for (int gz = n / 4; gz < 3 * n / 4; gz += 3)
        for (int gx = n / 4; gx < 3 * n / 4; gx += 3)
        {
            float g = map.Height[gz * n + gx];
            if (g < 300f || map.Water[gz * n + gx] > g) continue;   // dry land, 30 m+ above the sea
            float top = g;
            for (int dz = -reach; dz <= reach; dz++)
            for (int dx = -reach; dx <= reach; dx++)
                top = math.max(top, map.Height[(gz + dz) * n + gx + dx]);
            float px = (gx + 0.5f) * cell, pz = (gz + 0.5f) * cell;
            var col = TerrainGen.ShapeField.Sample(in spec, src.Map.View, src.O, px, pz, 1);
            if (col.water > col.height) continue;
            int eyeLayer = (col.height + 20) >> 7;
            if (((spec.seaLevelY + (int)top) >> 7) <= eyeLayer + StreamManager.V_LOAD_LAYERS) continue;
            groundY = col.height; topY = spec.seaLevelY + (int)top;
            return new Vector3(px * 0.1f, (col.height + 20) * 0.1f, pz * 0.1f);
        }
        Assert.Fail("no cliff foot found");
        groundY = topY = 0;
        return default;
    }

    [Test, Timeout(600000)]
    [Category("Slow")]   // > 10 s: SUITE=all only (task 11)
    public void TerrainAwareBand_ShiftsTowardTheCliff_KeepsTheCameraLoaded_CentresUnderground()
    {
        var meta = new WorldMetaData { seed = 42, sizeClass = TerrainGenChunkSource.SIZE_CLASS };
        var pool = new BrickDataPool(400000, rangeAware: true);
        var alloc = new ChunkHandleAllocator(1024);
        var store = new ChunkStore(pool, alloc);
        var mirror = new int3(EngineConfig.WINDOW_CHUNKS_XZ, 8, EngineConfig.WINDOW_CHUNKS_XZ);
        var clip = new TerrainClipmap(mirror, pool.Capacity);
        var cascades = new LODCascadeManager(mirror, tier => EngineConfig.CascadeTierPoolCap(tier));
        string deltas = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "vstream_aware_deltas");
        if (System.IO.Directory.Exists(deltas)) System.IO.Directory.Delete(deltas, true);
        StreamManager sm = null;
        bool aware = StreamManager.TerrainAwareBand;
        StreamManager.TerrainAwareBand = true;
        try
        {
            sm = new StreamManager(store, pool, alloc, clip, cascades, meta, deltas, AWARE_RADIUS);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            Vector3 cam = CliffFoot(sm.TerrainGenSource, 400, out int groundY, out int topY);
            int camLayer = (int)math.floor(cam.y * 10f) >> 7;
            sm.PrimeWindow(cam);
            Settle(sm, cam);
            CheckInvariants(sm, store, clip, camLayer, "at the cliff foot");
            Assert.Greater(sm.StreamLayer, camLayer,
                $"ground {groundY}, rise to {topY} within {AWARE_RADIUS} chunks: the band stayed centred on the eye");
            Debug.Log($"[V2 aware] cliff foot: ground Y {groundY}, top Y {topY}, camera layer {camLayer} -> band centre {sm.StreamLayer} " +
                      $"({clock.Elapsed.TotalSeconds:F0} s)");

            // A step inside the same chunk is not a reason to move the band.
            int s0 = sm.StreamLayer, sw0 = sm.BandSwitchesTotal;
            Vector3 nudge = cam + new Vector3(0.5f, 0f, 0.5f);
            for (int i = 0; i < 4; i++) { sm.Update(nudge); sm.WaitForIdle(); }
            Assert.AreEqual(s0, sm.StreamLayer, "a sub-chunk step moved the band");
            Assert.AreEqual(sw0, sm.BandSwitchesTotal, "a sub-chunk step counted a switch");

            // Underground: 40 m under the ground, the centred rule applies.
            Vector3 deep = cam - new Vector3(0f, 40f, 0f);
            int deepLayer = (int)math.floor(deep.y * 10f) >> 7;
            Settle(sm, deep);
            CheckInvariants(sm, store, clip, deepLayer, "40 m underground");
            Assert.AreEqual(deepLayer, sm.StreamLayer, "underground, the band must centre on the camera");
            Debug.Log($"[V2 aware] underground: layer {deepLayer}, switches {sm.BandSwitchesTotal} ({clock.Elapsed.TotalSeconds:F0} s)");
        }
        finally
        {
            StreamManager.TerrainAwareBand = aware;
            sm?.Dispose();
            cascades.Dispose(); clip.Dispose(); pool.Dispose();
            if (System.IO.Directory.Exists(deltas)) System.IO.Directory.Delete(deltas, true);
        }
    }

    private static void Settle(StreamManager sm, Vector3 cam)
    {
        var clk = System.Diagnostics.Stopwatch.StartNew();
        do { sm.Update(cam); sm.WaitForIdle(); }
        while ((sm.LoadDeficit() > 0 || sm.PendingLoads > 0 || sm.InFlightLoads > 0) && clk.Elapsed.TotalSeconds < 120);
        for (int i = 0; i < 2; i++) { sm.Update(cam); sm.WaitForIdle(); }
    }

    /// The contract every placement must keep: the camera's layer and the one
    /// below it are in the band; the band is fully resident and nothing else
    /// is; the band fits strictly inside the GPU mirror, centred on it.
    private static void CheckInvariants(StreamManager sm, ChunkStore store, TerrainClipmap clip, int camLayer, string when)
    {
        sm.LayerRange(out int lo, out int hi);
        Assert.AreEqual(math.max(0, sm.StreamLayer - StreamManager.V_LOAD_LAYERS), lo, $"{when}: band bottom");
        Assert.AreEqual(math.min(sm.TopLayerChunkY, sm.StreamLayer + StreamManager.V_LOAD_LAYERS), hi, $"{when}: band top");
        Assert.IsTrue(lo <= math.max(0, camLayer - 1) && camLayer <= hi, $"{when}: camera layer {camLayer} or the one below is outside [{lo},{hi}]");
        Assert.AreEqual(0, sm.LoadDeficit(), $"{when}: band chunks missing");
        Assert.AreEqual(sm.ExpectedResidentChunks, store.ResidentCount, $"{when}: resident count != the band");
        int3 o = clip.WindowOrigin, d = clip.WindowDimsChunks;
        Assert.AreEqual(sm.StreamLayer - d.y / 2, o.y, $"{when}: mirror origin does not centre on the band");
        foreach (var c in store.ResidentChunks())
        {
            Assert.IsTrue(c.coord.y >= lo && c.coord.y <= hi, $"{when}: {c.coord} resident outside the band [{lo},{hi}]");
            Assert.IsTrue(c.coord.y >= o.y && c.coord.y < o.y + d.y, $"{when}: {c.coord} outside the mirror box");
        }
    }

    private static void CheckBand(StreamManager sm, ChunkStore store, TerrainClipmap clip, int expectedLayer, string when)
    {
        Assert.AreEqual(expectedLayer, sm.StreamLayer, $"{when}: stream layer");
        sm.LayerRange(out int lo, out int hi);
        Assert.AreEqual(math.max(0, expectedLayer - StreamManager.V_LOAD_LAYERS), lo, $"{when}: band bottom");
        Assert.AreEqual(math.min(sm.TopLayerChunkY, expectedLayer + StreamManager.V_LOAD_LAYERS), hi, $"{when}: band top");
        int deficit = sm.LoadDeficit();
        if (deficit != 0)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var mc in sm.MissingChunks(8))
            {
                var rec = sm.GetRecord(mc);
                sb.Append($" {mc}:{(rec == null ? "no record" : rec.state + " gen " + rec.generation)}");
            }
            Assert.Fail($"{when}: {deficit} chunks of the band missing (pending {sm.PendingLoads}, in flight {sm.InFlightLoads}, " +
                        $"discarded on arrival {sm.DiscardedOnArrivalTotal}):{sb}");
        }
        Assert.AreEqual(sm.ExpectedResidentChunks, store.ResidentCount, $"{when}: resident count != the band");

        int3 o = clip.WindowOrigin, d = clip.WindowDimsChunks;
        Assert.AreEqual(expectedLayer - d.y / 2, o.y, $"{when}: mirror origin does not centre on the stream layer");
        foreach (var c in store.ResidentChunks())
        {
            Assert.IsTrue(c.coord.y >= lo && c.coord.y <= hi, $"{when}: {c.coord} resident outside the band [{lo},{hi}]");
            Assert.IsTrue(c.coord.y >= o.y && c.coord.y < o.y + d.y, $"{when}: {c.coord} outside the mirror box");
        }
    }
}
