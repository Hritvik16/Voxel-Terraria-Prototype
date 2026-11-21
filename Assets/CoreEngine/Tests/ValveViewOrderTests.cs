// Assets/CoreEngine/Tests/ValveViewOrderTests.cs
//
// Task 208 (owner 2026-11-14, Q-AB (a) fix 2): §3.6's pool-pressure valve picks
// its victims out of view and far first; LRU only breaks ties; a chunk inside
// the frustum within the near range goes only when nothing else is left
// (counted). The order changes, nothing else: the per-call 32-victim guard, the
// stop as soon as the pressure clears, the camera's 5x5 exemption.
//
// One flat world (seed 42, load radius 6 = 13 x 13 chunks, 144 outside the 5x5),
// primed once; each test pushes the store over the valve line with
// HighWaterBricksOverride, runs ONE Update (the valve), then lifts the line and
// settles so the victims reload for the next test.
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Memory;
using VoxelEngine.Mirror;
using VoxelEngine.Streaming;
using VoxelEngine.WorldGen;

public class ValveViewOrderTests
{
    private const int LOAD_RADIUS = 6;
    private static readonly Vector3 CamPos = new Vector3(64f, 20f, 64f);

    private BrickDataPool _pool;
    private ChunkStore _store;
    private TerrainClipmap _clip;
    private LODCascadeManager _cascades;
    private StreamManager _sm;
    private string _deltas;
    private GameObject _camGo;
    private Camera _cam;
    private int3 _camChunk;
    private bool _orderWas;

    [OneTimeSetUp]
    public void SetUp()
    {
        _orderWas = StreamManager.ValveViewOrder;
        var meta = new WorldMetaData { seed = 42, sizeClass = 0 };
        _pool = new BrickDataPool(EngineConfig.BRICK_POOL_CAP, rangeAware: true);
        var alloc = new ChunkHandleAllocator(1024);
        _store = new ChunkStore(_pool, alloc);
        var mirror = new int3(EngineConfig.WINDOW_CHUNKS_XZ, EngineConfig.MIRROR_CHUNKS_Y, EngineConfig.WINDOW_CHUNKS_XZ);
        _clip = new TerrainClipmap(mirror, _pool.Capacity);
        _cascades = new LODCascadeManager(mirror, tier => EngineConfig.CascadeTierPoolCap(tier));
        _deltas = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "valve_view_order_test_deltas");
        if (System.IO.Directory.Exists(_deltas)) System.IO.Directory.Delete(_deltas, true);
        _sm = new StreamManager(_store, _pool, alloc, _clip, _cascades, meta, _deltas, LOAD_RADIUS);
        _sm.PrimeWindow(CamPos);
        Settle();
        Assert.AreEqual(0, _sm.LoadDeficit(), "start square not resident");
        _camChunk = CoordMath.VoxelToChunk(CoordMath.WorldToVoxel((float3)CamPos));
        _camChunk.y = 0;

        // A real camera looking +X, slightly down: the -X half is behind it.
        _camGo = new GameObject("ValveViewOrderTests camera");
        _cam = _camGo.AddComponent<Camera>();
        _cam.fieldOfView = 60f; _cam.aspect = 16f / 9f; _cam.nearClipPlane = 0.1f; _cam.farClipPlane = 2000f;
        _camGo.transform.SetPositionAndRotation(CamPos, Quaternion.Euler(10f, 90f, 0f));
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        StreamManager.ValveViewOrder = _orderWas;
        if (_camGo != null) Object.DestroyImmediate(_camGo);
        _sm?.Dispose();
        _cascades?.Dispose(); _clip?.Dispose(); _pool?.Dispose();
        if (System.IO.Directory.Exists(_deltas)) System.IO.Directory.Delete(_deltas, true);
    }

    [SetUp]
    public void EachTest()
    {
        StreamManager.ValveViewOrder = true;
        _store.HighWaterBricksOverride = -1;
        UseCamera();
        Settle();   // the previous test's victims reload
        Assert.AreEqual(0, _sm.LoadDeficit(), "victims of the previous test did not reload");
        Assert.IsFalse(_store.IsUnderPoolPressure, "pressure before the test");
    }

    private void UseCamera()
    {
        var planes = new Plane[6];
        GeometryUtility.CalculateFrustumPlanes(_cam, planes);
        _sm.SetViewFrustum(planes);
    }

    /// A "frustum" that holds everything: 5 planes every point is in front of.
    private void UseEverythingInView()
    {
        var planes = new Plane[6];
        for (int i = 0; i < 6; i++) planes[i] = new Plane(Vector3.up, 1e7f);
        _sm.SetViewFrustum(planes);
    }

    private static int Cheb(int3 a, int3 b) => math.max(math.abs(a.x - b.x), math.abs(a.z - b.z));

    private void Settle()
    {
        var clk = System.Diagnostics.Stopwatch.StartNew();
        do { _sm.Update(CamPos); _sm.WaitForIdle(); }
        while ((_sm.LoadDeficit() > 0 || _sm.PendingLoads > 0 || _sm.InFlightLoads > 0) && clk.Elapsed.TotalSeconds < 120);
        for (int i = 0; i < 2; i++) { _sm.Update(CamPos); _sm.WaitForIdle(); }
    }

    private HashSet<int3> Residents()
    {
        var s = new HashSet<int3>();
        foreach (var c in _store.ResidentChunks()) s.Add(c.coord);
        return s;
    }

    /// Push over the line by `over` bricks (int.MinValue = line 0: always under
    /// pressure), run one Update (the valve), return the victims in a set.
    private HashSet<int3> OneValveCall(int over, out long lru, out long inView, out long inViewNear)
    {
        var before = Residents();
        long lru0 = _sm.LruEvictionsTotal, v0 = _sm.ValveInViewEvictionsTotal, n0 = _sm.ValveInViewNearEvictionsTotal;
        _store.HighWaterBricksOverride = over == int.MinValue ? 0 : _store.DenseBricksHeld - over;
        Assert.IsTrue(_store.IsUnderPoolPressure, "setup: not under pressure");
        _sm.Update(CamPos);
        var victims = new HashSet<int3>();
        foreach (var c in before) if (!_store.IsResident(c)) victims.Add(c);
        lru = _sm.LruEvictionsTotal - lru0; inView = _sm.ValveInViewEvictionsTotal - v0; inViewNear = _sm.ValveInViewNearEvictionsTotal - n0;
        _store.HighWaterBricksOverride = -1;
        return victims;
    }

    [Test]
    public void FullPool_EvictsOutOfViewFarChunksFirst_NeverAnInViewNearOne()
    {
        int outOfView = 0, inViewNear = 0;
        foreach (var c in Residents())
        {
            if (Cheb(c, _camChunk) <= 2) continue;
            if (_sm.ChunkInView(c)) inViewNear++; else outOfView++;
        }
        Assert.Greater(outOfView, 32, "setup: fewer out-of-view candidates than one call's 32 victims");
        Assert.Greater(inViewNear, 0, "setup: the camera sees no candidate chunk");

        var order = _sm.ValveOrderForTest(_camChunk);
        var victims = OneValveCall(int.MinValue, out long lru, out long inView, out long near);
        Assert.AreEqual(32, victims.Count, "a full pool evicts 32 chunks per call (unchanged guard)");
        Assert.AreEqual(32, lru, "evictions not counted as valve evictions");
        Assert.AreEqual(0, inView, "an in-view chunk was evicted while out-of-view ones were left");
        Assert.AreEqual(0, near);

        int minVictimDist = int.MaxValue;
        foreach (var v in victims)
        {
            Assert.IsFalse(_sm.ChunkInView(v), $"{v} is in view");
            Assert.Greater(Cheb(v, _camChunk), 2, $"{v} inside the camera's 5x5");
            minVictimDist = math.min(minVictimDist, Cheb(v, _camChunk));
        }
        foreach (var c in Residents())
            if (Cheb(c, _camChunk) > 2 && !_sm.ChunkInView(c))
                Assert.LessOrEqual(Cheb(c, _camChunk), minVictimDist, $"out-of-view survivor {c} is farther than a victim (far first)");

        // Deterministic: the victims are exactly the order's first 32.
        var head = new HashSet<int3>(order.GetRange(0, 32));
        Assert.IsTrue(head.SetEquals(victims), "the victims are not the order's first 32");
    }

    [Test]
    public void Order_IsDeterministic_AndRanksOutOfView_ThenFar_ThenLru()
    {
        var a = _sm.ValveOrderForTest(_camChunk);
        var b = _sm.ValveOrderForTest(_camChunk);
        Assert.AreEqual(a.Count, b.Count);
        for (int i = 0; i < a.Count; i++) Assert.AreEqual(a[i], b[i], $"order differs at {i}");

        int prevCls = -1, prevDist = int.MaxValue; uint prevTouched = 0;
        int nCls0 = 0, nCls2 = 0;
        foreach (var c in a)
        {
            Assert.Greater(Cheb(c, _camChunk), 2, $"{c} inside the 5x5 is a candidate");
            int dist = Cheb(c, _camChunk);
            int cls = _sm.ChunkInView(c) ? (dist <= _sm.LoadRadiusChunks ? 2 : 1) : 0;
            uint touched = _sm.GetRecord(c).lastTouchedFrame;
            if (cls == 0) nCls0++; else if (cls == 2) nCls2++;
            Assert.GreaterOrEqual(cls, prevCls, $"{c}: class {cls} after class {prevCls}");
            if (cls == prevCls)
            {
                Assert.LessOrEqual(dist, prevDist, $"{c}: nearer chunk ranked before a farther one in class {cls}");
                if (dist == prevDist) Assert.GreaterOrEqual(touched, prevTouched, $"{c}: warmer before colder at equal distance");
            }
            prevCls = cls; prevDist = dist; prevTouched = touched;
        }
        Assert.Greater(nCls0, 0); Assert.Greater(nCls2, 0);
    }

    [Test]
    public void SmallOvershoot_StopsAsSoonAsThePressureClears_OnTheOrdersPrefix()
    {
        var order = _sm.ValveOrderForTest(_camChunk);
        var victims = OneValveCall(1, out long lru, out long inView, out _);
        Assert.GreaterOrEqual(victims.Count, 1, "nothing evicted");
        Assert.Less(victims.Count, 32, "one brick over the line took the whole 32");
        Assert.AreEqual(victims.Count, lru);
        Assert.AreEqual(0, inView);
        var head = new HashSet<int3>(order.GetRange(0, victims.Count));
        Assert.IsTrue(head.SetEquals(victims), "the victims are not a prefix of the order");
        Assert.IsFalse(_store.IsUnderPoolPressure);
    }

    [Test]
    public void EverythingInView_TheValveStillRelievesThePool_AndCountsInViewNearEvictions()
    {
        UseEverythingInView();
        var victims = OneValveCall(int.MinValue, out long lru, out long inView, out long near);
        Assert.AreEqual(32, victims.Count, "the last resort still evicts (pool cap is hard)");
        Assert.AreEqual(32, inView);
        Assert.AreEqual(32, near, "in-view near evictions not counted");
    }

    [Test]
    public void OldOrder_EvictsTheSameCountPerCall_AndCountsItsInViewVictims()
    {
        StreamManager.ValveViewOrder = false;
        var victims = OneValveCall(int.MinValue, out long lru, out long inView, out long near);
        Assert.AreEqual(32, victims.Count, "the old order's per-call count");
        Assert.AreEqual(32, lru);
        int seen = 0;
        foreach (var v in victims) if (_sm.ChunkInView(v)) seen++;
        Assert.AreEqual(seen, inView, "the in-view counter disagrees with the frustum test in the old order");
        Debug.Log($"[Task208] old LRU order, one full-pressure call: {victims.Count} victims, {inView} in view ({near} near)");
    }

    [Test]
    public void NoFrustum_FallsBackToTheOldLruOrder()
    {
        _sm.ClearViewFrustum();
        Assert.IsFalse(_sm.HasViewFrustum);
        var victims = OneValveCall(int.MinValue, out long lru, out long inView, out _);
        Assert.AreEqual(32, victims.Count);
        Assert.AreEqual(0, inView, "no frustum = nothing counted in view");
    }
}
