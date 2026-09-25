// Assets/Game/DevMap.cs
//
// THE DEV MAP (owner, 2026-09-25: "full 3-D map for dev mode. Explored can come
// in when we do gameplay" + "In-game dev view"). Press O in the Playground.
//
// WHAT IT IS: the renderer's own far band, seen from an orbit camera over the
// whole island, with the generator's landmarks marked on it and an underground
// DEPTH SLICE beside it. It shows the world as GENERATED -- everything, not
// what a player has explored (no fog of war; that is gameplay, later).
//
//   sizeClass 2 (./run-demo.sh tg2, -farsource tg2): the TerrainGen island.
//     Markers: the peak, the volcano, the world tree, the geyser basin, and
//     every §2.5 cell stamp that lands on dry ground outside a singleton's area.
//     The slice is DetailField.Material itself (the same function a chunk
//     would be generated from), sampled on a 320^2 grid across the island.
//   legacy (./run-demo.sh): today's sizeClass 1 island through the far band.
//     Markers: world.meta's FeatureAnchors. No slice (that generator has no
//     cheap per-voxel entry point; the overhaul is about the new island).
//
// NOTHING HERE WRITES THE WORLD. The slice worker thread reads only the
// immutable TerrainGen map (through its own pinned WorldMap view over the same
// arrays, so FarField's disposal order cannot free it underneath the thread)
// and writes only its own pixel buffer; the main thread uploads it. No lock
// touches the streaming path (CLAUDE.md single-writer rule).
//
// While open the map owns the camera and the keyboard: Playground skips its
// own input, the flycam is disabled, and closing restores the pose, the far
// band switches and the cursor exactly as they were. Far haze and the step
// cap are raised while open so a 6 km island is not fogged out from 5 km up;
// frame time in this view is irrelevant (dev tool) and is not measured.
using System;
using System.Collections.Generic;
using System.Threading;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Rendering;
using VoxelEngine.WorldGen;

public sealed class DevMap : MonoBehaviour
{
    public static DevMap Instance { get; private set; }
    public static bool IsOpen => Instance != null && Instance._open;
    public static void Toggle() { if (Instance != null) { if (Instance._open) Instance.CloseMap(); else Instance.OpenMap(); } }

    // ---------------- state saved on open, restored on close ----------------
    private bool _open;
    private Vector3 _savedPos;
    private Quaternion _savedRot;
    private bool _savedFarOn, _savedFlyEnabled, _savedCursorVisible;
    private float _savedHaze;
    private int _savedSteps;
    private CursorLockMode _savedLock;
    private PlaygroundFlyCamera _fly;

    // ---------------- orbit camera ----------------
    private Vector3 _target;
    private float _yaw = 30f, _pitch = 50f, _dist = 5000f;
    private bool _dragOrbit, _dragPan;
    private Vector3 _lastMouse;

    // ---------------- world frame ----------------
    private bool _tg2;
    private TerrainGen.TerrainSpec _spec;
    private TerrainGen.Offsets _o;
    private TerrainGen.WorldMap _mapView;     // OUR pins over FarField's arrays
    private int2 _off;
    private int _seaShift;
    private Vector3 _islandCentre;
    private float _islandRadiusM;

    public struct Marker { public string name; public Vector3 pos; public Color colour; public bool major; }
    private readonly List<Marker> _markers = new List<Marker>();
    private readonly Dictionary<string, int> _kindCounts = new Dictionary<string, int>();
    private int _labelMode = 1;               // 0 none, 1 major only, 2 all
    private bool _showSlice = true;

    // ---------------- depth slice ----------------
    public const int SLICE_N = 320;
    private Texture2D _sliceTex;
    private SliceWorker _worker;
    private int _sliceY;                      // tg2 absolute voxel Y
    private int _shownSliceY = int.MinValue;
    private Rect _slicePx;                    // on-screen rect, GUI coords (top-left origin)
    private Texture2D _px;
    /// Z toggles an 800 m slice around the target at 2.5 m per pixel. The island
    /// slice (20 m per pixel) point-samples tunnels a few metres wide, so caves
    /// read as speckle there; the local slice resolves them.
    private bool _sliceLocal;
    private float2 _sliceCentre;
    public const int LOCAL_HALF_VOXELS = 4000;

    // IMGUI under the LINEAR colour space shows every colour one sRGB encode
    // too light (measured in the first -devmap shots: the sea legend drew
    // ~(74,122,149) for (22,60,92)). Colours are decoded once before drawing so
    // the panel shows the values it names.
    private static readonly byte[] ToLinear = new byte[256];
    private static bool _linear;

    void Awake()
    {
        Instance = this;
        _px = new Texture2D(1, 1); _px.SetPixel(0, 0, Color.white); _px.Apply();
        _linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
        for (int i = 0; i < 256; i++) ToLinear[i] = (byte)Mathf.RoundToInt(255f * (_linear ? Mathf.GammaToLinearSpace(i / 255f) : i / 255f));
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        _worker?.Stop();
        _worker = null;
        _mapView?.Dispose();
        _mapView = null;
    }

    // ======================================================================
    // Open / close
    // ======================================================================

    public void OpenMap()
    {
        var cam = Camera.main;
        if (cam == null || _open) return;
        EnsureWorldFrame();
        _savedPos = cam.transform.position; _savedRot = cam.transform.rotation;
        _savedLock = Cursor.lockState; _savedCursorVisible = Cursor.visible;
        _fly = cam.GetComponent<PlaygroundFlyCamera>();
        _savedFlyEnabled = _fly != null && _fly.enabled;
        if (_fly != null) _fly.enabled = false;
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;

        var far = FarField.Active;
        _savedFarOn = FarField.RenderEnabled;
        if (far != null)
        {
            FarField.RenderEnabled = true;
            _savedHaze = far.HazeRangeMetres; _savedSteps = far.PrimaryStepCap;
            far.HazeRangeMetres = Mathf.Max(_savedHaze, 40000f);
            far.PrimaryStepCap = Mathf.Max(_savedSteps, 1024);
        }
        if (_tg2 && _worker == null) StartSlice(false);
        _open = true;
        ApplyCamera();
    }

    public void CloseMap()
    {
        if (!_open) return;
        _open = false;
        var cam = Camera.main;
        if (cam != null) { cam.transform.position = _savedPos; cam.transform.rotation = _savedRot; }
        var far = FarField.Active;
        FarField.RenderEnabled = _savedFarOn;
        if (far != null) { far.HazeRangeMetres = _savedHaze; far.PrimaryStepCap = _savedSteps; }
        if (_fly != null) _fly.enabled = _savedFlyEnabled;
        Cursor.lockState = _savedLock; Cursor.visible = _savedCursorVisible;
    }

    // ======================================================================
    // World frame + markers (built once, on first open)
    // ======================================================================

    private bool _frameBuilt;

    private void EnsureWorldFrame()
    {
        if (_frameBuilt) return;
        _frameBuilt = true;
        var far = FarField.Active;
        _tg2 = far != null && far.FarOnly && far.Tg2Map != null;
        if (_tg2)
        {
            var m = far.Tg2Map;
            _spec = far.Tg2Spec;
            _o = TerrainGen.Offsets.For(_spec.seed);
            _mapView = new TerrainGen.WorldMap(m.Spec, m.Height, m.Rough, m.PeakPosition, m.Water, m.Flow, m.Hydro, m.WaterClass);
            _off = far.Tg2OffsetVoxels;
            _seaShift = _spec.seaLevelY - WorldGenConstants.SEA_LEVEL_VOXEL_Y;
            float c = _spec.worldSizeVoxels * 0.5f;
            _islandCentre = TgToEngine(c, _spec.seaLevelY, c);
            _islandRadiusM = _spec.islandRadius * 0.1f * 1.25f;
            _sliceY = _spec.seaLevelY - 300;
            BuildTg2Markers();
        }
        else
        {
            var meta = Phase4Bootstrapper.Meta;
            byte sc = meta != null ? meta.sizeClass : (byte)1;
            WorldGenConstants.DeriveIslandGeometry(sc, out float cx, out float cz, out float cr, out _);
            _islandCentre = new Vector3(cx * 0.1f, WorldGenConstants.SEA_LEVEL_VOXEL_Y * 0.1f, cz * 0.1f);
            _islandRadiusM = cr * 0.1f * 1.3f;
            BuildLegacyMarkers(meta);
        }
        _target = _islandCentre;
        _dist = _islandRadiusM * 1.9f;
    }

    private Vector3 TgToEngine(float gx, float gy, float gz)
        => new Vector3((gx - _off.x) * 0.1f, (gy - _seaShift) * 0.1f, (gz - _off.y) * 0.1f);

    private float2 EngineToTg(Vector3 e) => new float2(e.x * 10f + _off.x, e.z * 10f + _off.y);

    /// Top of the tg2 column (absolute voxels), or sea level. Map-level detail
    /// (texel 8) -- a marker height, not a collision test.
    private float TgGround(float gx, float gz)
    {
        var view = _mapView.View;
        var c = TerrainGen.ShapeField.Sample(in _spec, in view, in _o, gx, gz, 8);
        return math.max(c.height + 1, _spec.seaLevelY + 1);
    }

    private void AddMarker(string name, Vector3 pos, Color col, bool major)
    {
        _markers.Add(new Marker { name = name, pos = pos, colour = col, major = major });
        _kindCounts.TryGetValue(name, out int n);
        _kindCounts[name] = n + 1;
    }

    private static readonly Color MajorCol = new Color(1f, 0.85f, 0.35f);

    private void BuildTg2Markers()
    {
        float2 pk = _mapView.PeakPosition;
        AddMarker("PEAK", TgToEngine(pk.x, TgGround(pk.x, pk.y), pk.y), MajorCol, true);
        float2 v = TerrainGen.Stamps.VolcanoSite(in _spec, in _o);
        TerrainGen.Stamps.VolcanoHeight(in _spec, in _o, v, out _, out float lava);
        AddMarker("VOLCANO (caldera lava lake)", TgToEngine(v.x, _spec.seaLevelY + lava, v.y), new Color(1f, 0.45f, 0.2f), true);
        float2 t = TerrainGen.Stamps.TreeSite(in _spec, in _o);
        AddMarker("WORLD TREE", TgToEngine(t.x, TgGround(t.x, t.y), t.y), new Color(0.5f, 0.95f, 0.45f), true);
        float2 g = TerrainGen.Stamps.GeyserSite(in _spec, in _o);
        AddMarker("GEYSER BASIN", TgToEngine(g.x, TgGround(g.x, g.y), g.y), new Color(0.6f, 0.9f, 1f), true);

        int cells = _spec.worldSizeVoxels >> TerrainGen.Stamps.CELL_SHIFT;
        for (int gz = 0; gz < cells; gz++)
        for (int gx = 0; gx < cells; gx++)
        {
            if (!TerrainGen.Stamps.CellStamp(in _o, gx, gz, out var s)) continue;
            // The singletons own their areas (Stamps.Column / MacroApply use
            // the same three tests), and a stamp over the sea builds nothing.
            if (TerrainGen.Stamps.VolcanoHeight(in _spec, in _o, s.pos, out _, out _) > 0f) continue;
            if (math.distance(s.pos, t) < TerrainGen.Stamps.TREE_CANOPY_R + 200f) continue;
            if (math.distance(s.pos, g) < 1400f) continue;
            float gy = TgGround(s.pos.x, s.pos.y);
            if (gy < _spec.seaLevelY + 20) continue;
            AddMarker(StampName(s.kind), TgToEngine(s.pos.x, gy, s.pos.y), StampColour(s.kind), false);
        }
    }

    private static string StampName(byte k)
    {
        switch (k)
        {
            case TerrainGen.Stamps.CaveMouth: return "cave mouth";
            case TerrainGen.Stamps.Fissure: return "fissure";
            case TerrainGen.Stamps.Arch: return "arch";
            case TerrainGen.Stamps.StoneRing: return "stone ring";
            case TerrainGen.Stamps.Hoop: return "hoop";
            case TerrainGen.Stamps.CrystalSpires: return "crystal spires";
            case TerrainGen.Stamps.MeteorCrater: return "meteor crater";
            case TerrainGen.Stamps.Fossil: return "fossil";
            default: return "stamp " + k;
        }
    }

    private static Color StampColour(byte k)
    {
        switch (k)
        {
            case TerrainGen.Stamps.CaveMouth: return new Color(0.75f, 0.55f, 1f);
            case TerrainGen.Stamps.Fissure: return new Color(0.95f, 0.4f, 0.7f);
            case TerrainGen.Stamps.Arch: return new Color(1f, 0.75f, 0.45f);
            case TerrainGen.Stamps.StoneRing: return new Color(0.85f, 0.85f, 0.8f);
            case TerrainGen.Stamps.Hoop: return new Color(1f, 0.6f, 0.3f);
            case TerrainGen.Stamps.CrystalSpires: return new Color(0.45f, 0.8f, 1f);
            case TerrainGen.Stamps.MeteorCrater: return new Color(1f, 0.35f, 0.55f);
            case TerrainGen.Stamps.Fossil: return new Color(0.95f, 0.92f, 0.8f);
            default: return Color.white;
        }
    }

    private void BuildLegacyMarkers(WorldMetaData meta)
    {
        if (meta == null) return;
        var st = ColumnSampler.CreateState(meta);
        try
        {
            foreach (var a in meta.anchors)
            {
                ColumnSampler.SampleColumn(in st, (int)a.cx, (int)a.cz, out int h, out _);
                float top = Mathf.Max(h + 1, WorldGenConstants.SEA_LEVEL_VOXEL_Y + 1) * 0.1f;
                switch (a.kind)
                {
                    case FeatureKind.Mountain: AddMarker("mountain", new Vector3(a.cx * 0.1f, top, a.cz * 0.1f), MajorCol, true); break;
                    case FeatureKind.Crater: AddMarker("crater", new Vector3(a.cx * 0.1f, top, a.cz * 0.1f), new Color(1f, 0.35f, 0.55f), true); break;
                    case FeatureKind.Cave: AddMarker("cave (underground)", new Vector3(a.cx * 0.1f, a.cy * 0.1f, a.cz * 0.1f), new Color(0.75f, 0.55f, 1f), false); break;
                }
            }
        }
        finally { st.Dispose(); }
    }

    // ======================================================================
    // Camera + input
    // ======================================================================

    /// For capture passes: set the orbit directly.
    public void SetView(Vector3 target, float yaw, float pitch, float dist)
    { _target = target; _yaw = yaw; _pitch = pitch; _dist = dist; ApplyCamera(); }

    public Vector3 IslandCentre { get { EnsureWorldFrame(); return _islandCentre; } }
    public float IslandRadiusM { get { EnsureWorldFrame(); return _islandRadiusM; } }
    public IReadOnlyList<Marker> Markers => _markers;
    public bool IsTg2 { get { EnsureWorldFrame(); return _tg2; } }
    public int MarkerCount => _markers.Count;

    private void ApplyCamera()
    {
        var cam = Camera.main;
        if (cam == null) return;
        _pitch = Mathf.Clamp(_pitch, 5f, 89.5f);
        _dist = Mathf.Clamp(_dist, 40f, 14000f);
        Quaternion r = Quaternion.Euler(_pitch, _yaw, 0f);
        cam.transform.position = _target + r * new Vector3(0f, 0f, -_dist);
        cam.transform.rotation = r;
    }

    void Update()
    {
        if (!_open) return;
        float dt = Time.unscaledDeltaTime;
        bool fast = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        // Keyboard: WASD pan (in the view's ground plane), Q/E zoom, arrows orbit.
        Vector3 fwd = Quaternion.Euler(0f, _yaw, 0f) * Vector3.forward, right = Quaternion.Euler(0f, _yaw, 0f) * Vector3.right;
        float pan = _dist * 0.6f * dt * (fast ? 3f : 1f);
        if (Input.GetKey(KeyCode.W)) _target += fwd * pan;
        if (Input.GetKey(KeyCode.S)) _target -= fwd * pan;
        if (Input.GetKey(KeyCode.D)) _target += right * pan;
        if (Input.GetKey(KeyCode.A)) _target -= right * pan;
        if (Input.GetKey(KeyCode.Q)) _dist *= 1f + 1.2f * dt;
        if (Input.GetKey(KeyCode.E)) _dist /= 1f + 1.2f * dt;
        if (Input.GetKey(KeyCode.LeftArrow)) _yaw -= 60f * dt;
        if (Input.GetKey(KeyCode.RightArrow)) _yaw += 60f * dt;
        if (Input.GetKey(KeyCode.UpArrow)) _pitch += 40f * dt;
        if (Input.GetKey(KeyCode.DownArrow)) _pitch -= 40f * dt;
        if (Input.GetKeyDown(KeyCode.T)) _pitch = _pitch > 85f ? 50f : 89.5f;
        if (Input.GetKeyDown(KeyCode.H)) _labelMode = (_labelMode + 1) % 3;
        if (Input.GetKeyDown(KeyCode.G)) _showSlice = !_showSlice;
        if (_tg2 && Input.GetKeyDown(KeyCode.Z)) StartSlice(!_sliceLocal);
        // The local slice follows the target once it has moved a quarter of the
        // window away (not mid-drag, so a pan does not restart it every frame).
        if (_tg2 && _sliceLocal && !_dragOrbit && !_dragPan &&
            math.distance(EngineToTg(_target), _sliceCentre) > LOCAL_HALF_VOXELS * 0.5f) StartSlice(true);
        if (Input.GetKeyDown(KeyCode.N)) SunCycle.SetHours(SunCycle.GoldenHourHours);
        if (Input.GetKeyDown(KeyCode.M)) SunCycle.SetHours(SunCycle.NoonHours);
        if (Input.GetKeyDown(KeyCode.Alpha0)) { _target = _islandCentre; _dist = _islandRadiusM * 1.9f; _pitch = 50f; }
        for (int i = 0; i < 9; i++)
        {
            if (!Input.GetKeyDown(KeyCode.Alpha1 + i)) continue;
            int seen = 0;
            foreach (var m in _markers)
                if (m.major && seen++ == i) { _target = m.pos; _dist = Mathf.Min(_dist, 1500f); }
        }

        // Slice depth: PageUp/PageDown or R/F, 10 m a press (Shift: 50 m).
        if (_tg2)
        {
            int stepV = fast ? 500 : 100;
            if (Input.GetKeyDown(KeyCode.PageUp) || Input.GetKeyDown(KeyCode.R)) SetSliceY(_sliceY + stepV);
            if (Input.GetKeyDown(KeyCode.PageDown) || Input.GetKeyDown(KeyCode.F)) SetSliceY(_sliceY - stepV);
        }

        // Mouse: LMB drag orbits (or, on the slice panel, moves the target
        // there), RMB drag pans, wheel zooms.
        Vector3 mp = Input.mousePosition;
        Vector2 gui = new Vector2(mp.x, Screen.height - mp.y);
        bool overSlice = _showSlice && _tg2 && _slicePx.Contains(gui);
        if (Input.GetMouseButtonDown(0)) { if (overSlice) PickOnSlice(gui); else _dragOrbit = true; }
        if (Input.GetMouseButton(0) && overSlice && !_dragOrbit) PickOnSlice(gui);
        if (Input.GetMouseButtonDown(1)) _dragPan = true;
        if (!Input.GetMouseButton(0)) _dragOrbit = false;
        if (!Input.GetMouseButton(1)) _dragPan = false;
        Vector3 d = mp - _lastMouse;
        if (_dragOrbit) { _yaw += d.x * 0.25f; _pitch -= d.y * 0.2f; }
        if (_dragPan) { float k = _dist * 0.0015f; _target -= right * d.x * k + fwd * d.y * k; }
        _lastMouse = mp;
        float wheel = Input.mouseScrollDelta.y;
        if (Mathf.Abs(wheel) > 0.01f) _dist *= Mathf.Pow(0.88f, wheel);

        ApplyCamera();
        PumpSlice();
    }

    // ======================================================================
    // Depth slice
    // ======================================================================

    public int SliceY => _sliceY;
    public bool SliceShowing(int y) => _shownSliceY == y;
    public float SlicePrepFraction => _worker != null ? _worker.PrepFraction : 0f;
    public double SlicePrepMs => _worker != null ? _worker.PrepMs : 0;
    public double SliceLastRenderMs => _worker != null ? _worker.LastRenderMs : 0;
    public float SliceVoxelsPerPixel => _worker != null ? _worker.Step : 0;

    public void SetSliceY(int y)
    {
        if (!_tg2) return;
        _sliceY = math.clamp(y, _spec.worldBottomY, _spec.seaLevelY + (int)_spec.peakHeight + 400);
        _worker?.Request(_sliceY);
    }

    public bool SliceLocal => _sliceLocal;

    /// (Re)starts the slice worker: the whole island, or LOCAL_HALF_VOXELS
    /// around the orbit target. Each start re-prepares its columns (~5 s).
    public void StartSlice(bool local)
    {
        _worker?.Stop();
        _sliceLocal = local;
        float c = _spec.worldSizeVoxels * 0.5f;
        _sliceCentre = local ? EngineToTg(_target) : new float2(c, c);
        int half = local ? LOCAL_HALF_VOXELS : (int)(_spec.islandRadius * 1.3f);
        int step = (2 * half + SLICE_N - 1) / SLICE_N;
        if (_sliceTex == null)
            _sliceTex = new Texture2D(SLICE_N, SLICE_N, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        _shownSliceY = int.MinValue;   // never show the previous region's pixels as this one's
        _worker = new SliceWorker(_spec, _o, _mapView, (int)_sliceCentre.x - half, (int)_sliceCentre.y - half, step, SLICE_N);
        _worker.Request(_sliceY);
    }

    private void PumpSlice()
    {
        if (_worker == null) return;
        if (_worker.TryTake(out Color32[] px, out int y))
        {
            _sliceTex.SetPixels32(px);
            _sliceTex.Apply(false);
            _shownSliceY = y;
        }
    }

    private void PickOnSlice(Vector2 gui)
    {
        if (_worker == null) return;
        float u = (gui.x - _slicePx.x) / _slicePx.width, v = 1f - (gui.y - _slicePx.y) / _slicePx.height;
        float gx = _worker.X0 + u * _worker.Step * SLICE_N, gz = _worker.Z0 + v * _worker.Step * SLICE_N;
        Vector3 e = TgToEngine(gx, TgGround(gx, gz), gz);
        _target = e;
    }

    /// Owns the slice: column preparation once (ShapeField.Sample + MaxStep +
    /// PrepareColumn at voxel resolution), then one DetailField.Material per
    /// pixel per requested depth. Reads only the immutable map; writes only its
    /// own buffers.
    private sealed class SliceWorker
    {
        public readonly int X0, Z0, Step, N;
        private readonly TerrainGen.TerrainSpec _spec;
        private readonly TerrainGen.Offsets _o;
        private readonly TerrainGen.WorldMap _map;
        private readonly TerrainGen.ColumnSample[] _cols;
        private readonly TerrainGen.DetailField.ColumnDetail[] _det;
        private readonly float[] _st;
        private readonly Thread _thread;
        private readonly AutoResetEvent _wake = new AutoResetEvent(false);
        private volatile bool _quit;
        private volatile int _requested = int.MinValue;
        private volatile int _prepRows;
        private readonly object _outLock = new object();   // guards only this worker's own output hand-off
        private Color32[] _out;
        private int _outY = int.MinValue;
        private bool _outFresh;
        public double PrepMs, LastRenderMs;

        public float PrepFraction => _prepRows / (float)N;

        public SliceWorker(TerrainGen.TerrainSpec spec, TerrainGen.Offsets o, TerrainGen.WorldMap map, int x0, int z0, int step, int n)
        {
            _spec = spec; _o = o; _map = map; X0 = x0; Z0 = z0; Step = step; N = n;
            _cols = new TerrainGen.ColumnSample[n * n];
            _det = new TerrainGen.DetailField.ColumnDetail[n * n];
            _st = new float[n * n];
            _thread = new Thread(Run) { IsBackground = true, Name = "DevMap slice", Priority = System.Threading.ThreadPriority.BelowNormal };
            _thread.Start();
        }

        public void Request(int y) { _requested = y; _wake.Set(); }
        public void Stop() { _quit = true; _wake.Set(); _thread.Join(3000); }

        public bool TryTake(out Color32[] px, out int y)
        {
            lock (_outLock)
            {
                px = _out; y = _outY;
                if (!_outFresh) return false;
                _outFresh = false;
                return true;
            }
        }

        private void Run()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var view = _map.View;
            for (int j = 0; j < N && !_quit; j++)
            {
                for (int i = 0; i < N; i++)
                {
                    int x = X0 + i * Step, z = Z0 + j * Step, k = j * N + i;
                    _cols[k] = TerrainGen.ShapeField.Sample(in _spec, in view, in _o, x + 0.5f, z + 0.5f, 1);
                    _st[k] = TerrainGen.ShapeField.MaxStep(in _spec, in view, in _o, x + 0.5f, z + 0.5f, 1, _cols[k].height);
                    _det[k] = TerrainGen.DetailField.PrepareColumn(in _o, x, z);
                }
                _prepRows = j + 1;
            }
            PrepMs = sw.Elapsed.TotalMilliseconds;

            int done = int.MinValue;
            var buf = new Color32[N * N];
            while (!_quit)
            {
                int y = _requested;
                if (y == done) { _wake.WaitOne(); continue; }
                sw.Restart();
                bool aborted = false;
                for (int j = 0; j < N && !aborted; j++)
                {
                    for (int i = 0; i < N; i++)
                    {
                        int k = j * N + i, x = X0 + i * Step, z = Z0 + j * Step;
                        ref var c = ref _cols[k];
                        byte m = TerrainGen.DetailField.Material(in _spec, in _o, in c, in _det[k], _st[k], x, y, z);
                        Color32 sc = SliceColour(m, y, in c, _spec.seaLevelY);
                        buf[k] = new Color32(ToLinear[sc.r], ToLinear[sc.g], ToLinear[sc.b], 255);
                    }
                    if (_quit || _requested != y) aborted = true;
                }
                if (aborted) continue;
                LastRenderMs = sw.Elapsed.TotalMilliseconds;
                done = y;
                lock (_outLock)
                {
                    _out = (Color32[])buf.Clone();
                    _outY = y; _outFresh = true;
                }
            }
        }
    }

    // Provisional colours: palette direction A (tools/palette-board.py), the
    // neutral one -- the owner has not picked a palette yet, and this is a dev
    // view, so it only has to tell the materials apart.
    private static readonly Color32[] GenColours = BuildGenColours();

    private static Color32[] BuildGenColours()
    {
        var t = new Color32[256];
        for (int i = 0; i < 256; i++) t[i] = new Color32(255, 0, 255, 255);
        void S(byte k, uint rgb) => t[k] = new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);
        S(1, 0x24566a); S(2, 0xf26b1d); S(3, 0xc98f28); S(4, 0x1a1510); S(5, 0xa8d81f); S(6, 0x66cc33); S(7, 0x9e855c);
        S(10, 0x6b7f47); S(11, 0x4d7540); S(12, 0xdbc9a1); S(13, 0xeef0f2); S(14, 0xe3d4b0);
        S(20, 0x66503a); S(21, 0xb8a37f); S(22, 0xb8ccdb); S(23, 0x4d3e33);
        S(30, 0xb8b09e); S(31, 0x545a61); S(32, 0xa88c5c); S(33, 0x948a85); S(34, 0x474545);
        S(40, 0xd4c29e); S(41, 0x94543d); S(42, 0xbf8554); S(43, 0x663b36); S(44, 0xc79e94);
        S(50, 0x667385); S(51, 0xd9d9d4); S(52, 0x383b40); S(53, 0x789494); S(54, 0x9ea3a8);
        S(60, 0xb3b399); S(61, 0x333633); S(62, 0x94593d); S(63, 0x617352); S(64, 0x668c73);
        S(70, 0x40404a); S(71, 0x54504d); S(72, 0x6b6661); S(73, 0x141219); S(74, 0x59291a); S(75, 0x1f1f1f);
        S(80, 0xe0d6bd); S(81, 0x59a6fa); S(82, 0xfaad33); S(83, 0xf273b3); S(84, 0x59eb8c);
        S(85, 0xdbd1b3); S(86, 0xf24d8c); S(87, 0x5c422e); S(88, 0x476b38); S(89, 0xd1c7b3); S(90, 0x9e4d8c);
        S(91, 0x2e2929); S(92, 0x8f8c85);
        return t;
    }

    private static readonly Color32 CaveAir = new Color32(6, 6, 9, 255);
    private static readonly Color32 OpenAir = new Color32(38, 52, 78, 255);
    private static readonly Color32 SeaCol = new Color32(22, 60, 92, 255);

    private static Color32 SliceColour(byte m, int y, in TerrainGen.ColumnSample c, int sea)
    {
        if (m != TerrainGen.GenMat.Air) return GenColours[m];
        if (y > c.height) return y <= sea ? SeaCol : OpenAir;   // above the column: sky, or the sea
        return CaveAir;                                         // below the top: a cave
    }

    // ======================================================================
    // GUI
    // ======================================================================

    private GUIStyle _label, _small;

    void OnGUI()
    {
        if (!_open) return;
        var cam = Camera.main;
        if (cam == null) return;
        float ui = Mathf.Max(1f, Screen.height / 900f);
        if (_label == null)
        {
            _label = new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true };
            _small = new GUIStyle(GUI.skin.label) { richText = true, wordWrap = false };
        }
        _label.fontSize = Mathf.RoundToInt(13 * ui);
        _small.fontSize = Mathf.RoundToInt(11 * ui);
        _label.normal.textColor = _small.normal.textColor = new Color(0.93f, 0.92f, 0.88f);

        // ---- markers ----
        float sq = 7f * ui;
        foreach (var m in _markers)
        {
            Vector3 sp = cam.WorldToScreenPoint(m.pos);
            if (sp.z <= 0f) continue;
            float x = sp.x, y = Screen.height - sp.y;
            if (x < -50 || y < -50 || x > Screen.width + 50 || y > Screen.height + 50) continue;
            float s = m.major ? sq * 1.6f : sq;
            Box(new Rect(x - s * 0.5f - ui, y - s * 0.5f - ui, s + 2 * ui, s + 2 * ui), new Color(0f, 0f, 0f, 0.85f));
            Box(new Rect(x - s * 0.5f, y - s * 0.5f, s, s), m.colour);
            bool label = _labelMode == 2 || (_labelMode == 1 && m.major);
            if (!label) continue;
            var st = m.major ? _label : _small;
            var content = new GUIContent(m.major ? $"<b>{m.name}</b>" : m.name);
            Vector2 sz = st.CalcSize(content);
            var r = new Rect(x + s, y - sz.y * 0.5f, sz.x, sz.y);
            Box(new Rect(r.x - 3 * ui, r.y, r.width + 6 * ui, r.height), new Color(0f, 0f, 0f, 0.55f));
            GUI.Label(r, content, st);
        }
        // Orbit target crosshair.
        {
            Vector3 sp = cam.WorldToScreenPoint(_target);
            if (sp.z > 0f)
            {
                float x = sp.x, y = Screen.height - sp.y, L = 10f * ui;
                Box(new Rect(x - L, y - ui, 2 * L, 2 * ui), Color.white);
                Box(new Rect(x - ui, y - L, 2 * ui, 2 * L), Color.white);
            }
        }

        // ---- info panel (top left) ----
        var far = FarField.Active;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<b>DEV MAP</b>  <color=#9fb6c9>(O or Esc closes; everything as generated, no fog of war)</color>");
        sb.AppendLine(_tg2 ? "world: <b>sizeClass 2</b> (TerrainGen) via the far band"
                           : "world: <b>legacy sizeClass 1</b> via the far band" + (far == null ? "  <color=#e57a6a>-- far band NOT built: launch with -farband 1</color>" : ""));
        float2 tg = _tg2 ? EngineToTg(_target) : default;
        string biome = "";
        if (_tg2)
        {
            var view = _mapView.View;
            var cs = TerrainGen.ShapeField.Sample(in _spec, in view, in _o, tg.x, tg.y, 8);
            string[] bn = { "forest", "desert", "snow", "jungle" };
            biome = $"  ground {(cs.height - _spec.seaLevelY) * 0.1f:F0} m  biome {(cs.biome < 4 ? bn[cs.biome] : "?")}";
        }
        sb.AppendLine($"camera {cam.transform.position.y - WorldGenConstants.SEA_LEVEL_VOXEL_Y * 0.1f:F0} m above sea, {_dist:F0} m from the target");
        sb.AppendLine(_tg2 ? $"target tg2 voxel ({tg.x:F0}, {tg.y:F0}){biome}" : $"target ({_target.x:F0}, {_target.z:F0}) m");
        sb.Append("markers: ");
        foreach (var kv in _kindCounts) sb.Append($"{kv.Key} {kv.Value} · ");
        sb.AppendLine();
        sb.AppendLine("<color=#9fb6c9>LMB drag orbit · RMB drag pan · wheel/Q/E zoom · WASD pan (Shift fast) · arrows orbit · T top-down · 0 reset · 1-9 fly to a major landmark · H labels · N/M golden/noon" +
                      (_tg2 ? " · G slice panel · Z island/local slice · R/F or PgUp/PgDn slice depth ±10 m (Shift ±50) · click the slice to move there" : "") + "</color>");
        float pw = Mathf.Min(Screen.width - 32f * ui, 760f * ui);
        float ph = _label.CalcHeight(new GUIContent(sb.ToString()), pw - 20 * ui) + 16 * ui;
        var pr = new Rect(16 * ui, 16 * ui, pw, ph);
        Box(pr, new Color(0f, 0f, 0f, 0.72f));
        GUI.Label(new Rect(pr.x + 10 * ui, pr.y + 8 * ui, pw - 20 * ui, ph), sb.ToString(), _label);

        // ---- slice panel (bottom left) ----
        if (_showSlice && _tg2)
        {
            float side = Mathf.Min(Screen.height * 0.46f, 460f * ui);
            _slicePx = new Rect(16 * ui, Screen.height - side - 70 * ui, side, side);
            Box(new Rect(_slicePx.x - 6 * ui, _slicePx.y - 34 * ui, side + 12 * ui, side + 98 * ui), new Color(0f, 0f, 0f, 0.78f));
            if (_sliceTex != null && _shownSliceY != int.MinValue) GUI.DrawTexture(_slicePx, _sliceTex, ScaleMode.StretchToFill, false);
            float yM = (_sliceY - _spec.seaLevelY) * 0.1f;
            string layer = _sliceY > _spec.seaLevelY ? "above sea" : _sliceY > _spec.CavernTopY ? "shallow underground"
                         : _sliceY > _spec.UnderworldTopY ? "cavern layer" : "underworld";
            string state = _worker == null ? "" : _worker.PrepFraction < 1f ? $"  <color=#e6c25c>preparing columns {_worker.PrepFraction:P0}</color>"
                         : _shownSliceY != _sliceY ? "  <color=#e6c25c>slicing...</color>" : "";
            GUI.Label(new Rect(_slicePx.x, _slicePx.y - 30 * ui, side, 26 * ui),
                $"<b>SLICE at {yM:+0;-0} m</b> ({layer}){state}", _label);
            // target on the slice
            if (_worker != null)
            {
                float u = (tg.x - _worker.X0) / (_worker.Step * (float)SLICE_N), v = (tg.y - _worker.Z0) / (_worker.Step * (float)SLICE_N);
                if (u >= 0 && u <= 1 && v >= 0 && v <= 1)
                {
                    float x = _slicePx.x + u * side, y = _slicePx.y + (1f - v) * side, L = 7f * ui;
                    Box(new Rect(x - L, y - ui, 2 * L, 2 * ui), Color.white);
                    Box(new Rect(x - ui, y - L, 2 * ui, 2 * L), Color.white);
                }
            }
            // legend
            float lx = _slicePx.x, ly = _slicePx.yMax + 6 * ui, sw = 12 * ui;
            void Key(Color32 c, string n)
            {
                var sz = _small.CalcSize(new GUIContent(n));
                if (lx + sw + 6 * ui + sz.x > _slicePx.xMax) { lx = _slicePx.x; ly += 18 * ui; }
                Box(new Rect(lx, ly + 3 * ui, sw, sw), c);
                GUI.Label(new Rect(lx + sw + 4 * ui, ly, sz.x + 4, sz.y), n, _small);
                lx += sw + 12 * ui + sz.x;
            }
            Key(CaveAir, "cave"); Key(OpenAir, "open air"); Key(SeaCol, "sea"); Key(GenColours[1], "water");
            Key(GenColours[2], "lava"); Key(GenColours[70], "deepstone"); Key(GenColours[71], "cavern rock");
            Key(GenColours[91], "volcanic"); Key(GenColours[81], "crystal");
            GUI.Label(new Rect(_slicePx.x, ly + 18 * ui, side, 20 * ui),
                $"<color=#9fb6c9>{(_sliceLocal ? "LOCAL" : "island")} · {SLICE_N}² px, {_worker?.Step * 0.1f:F1} m each · strata coloured by provisional palette A</color>", _small);
        }
    }

    private void Box(Rect r, Color c)
    {
        var old = GUI.color; GUI.color = _linear ? c.linear : c;
        GUI.DrawTexture(r, _px);
        GUI.color = old;
    }
}
