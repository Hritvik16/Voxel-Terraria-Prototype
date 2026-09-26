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
        try
        {
            sm = new StreamManager(store, pool, alloc, clip, cascades, meta, deltas, LOAD_RADIUS);
            Assert.IsTrue(sm.VerticalStreaming, "sizeClass 2 must stream vertically");
            Assert.AreEqual(sm.TopLayerChunkY, StreamManager.ContentTopChunkY, "collision's ceiling follows the world");

            Vector3 cam = LandSite(sm.TerrainGenSource, out int groundY);
            int camLayer = groundY + 20 >> 7;
            sm.PrimeWindow(cam);
            sm.WaitForIdle();
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
            for (int i = 0; i < 4; i++) { sm.Update(up3); sm.WaitForIdle(); }
            CheckBand(sm, store, clip, layer3, "after climbing three layers");

            // TELEPORT WITH LOADS IN FLIGHT (2026-09-26): dispatch a band, then
            // jump 1 km and 2 layers up before it lands. Completions for the old
            // position must be discarded, not inserted -- inserting them left
            // resident chunks outside the window that a later chunk's ring slot
            // collided with ("Insert of chunk ... would overwrite live chunk").
            Vector3 far = up3 + new Vector3(1000f, 0f, 700f);
            sm.Update(cam + new Vector3(40f, 0f, 40f));      // new loads dispatched here...
            sm.Update(far);                                  // ...and the camera leaves before they land
            sm.WaitForIdle();
            for (int i = 0; i < 4; i++) { sm.Update(far); sm.WaitForIdle(); }
            int farLayer = (int)math.floor(far.y * 10f) >> 7;
            CheckBand(sm, store, clip, farLayer, "after a 1 km teleport with loads in flight");
            Debug.Log($"[V2] teleport: {sm.DiscardedOnArrivalTotal} completions discarded on arrival");
            Debug.Log($"[V2] ground Y {groundY}, band {camLayer}+-{StreamManager.V_LOAD_LAYERS} -> {layer3}; " +
                      $"resident {residentAtStart} -> {store.ResidentCount}; air fast path {sm.TerrainGenSource.AirFastPathTotal}; " +
                      $"dense bricks held {store.DenseBricksHeld}; top layer {sm.TopLayerChunkY}");
        }
        finally
        {
            sm?.Dispose();
            cascades.Dispose(); clip.Dispose(); pool.Dispose();
            if (System.IO.Directory.Exists(deltas)) System.IO.Directory.Delete(deltas, true);
        }
        Assert.AreEqual(StreamManager.MAX_GENERATED_CHUNK_Y, StreamManager.ContentTopChunkY, "Dispose must restore the flat-world ceiling");
    }

    private static void CheckBand(StreamManager sm, ChunkStore store, TerrainClipmap clip, int expectedLayer, string when)
    {
        Assert.AreEqual(expectedLayer, sm.StreamLayer, $"{when}: stream layer");
        sm.LayerRange(out int lo, out int hi);
        Assert.AreEqual(math.max(0, expectedLayer - StreamManager.V_LOAD_LAYERS), lo, $"{when}: band bottom");
        Assert.AreEqual(math.min(sm.TopLayerChunkY, expectedLayer + StreamManager.V_LOAD_LAYERS), hi, $"{when}: band top");
        Assert.AreEqual(0, sm.LoadDeficit(), $"{when}: chunks of the band missing");
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
