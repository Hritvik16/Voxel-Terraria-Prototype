// Assets/Game/Minimap.cs
//
// THE MINIMAP (owner, 2026-09-26: "Minimap always in the corner sounds good").
// A north-up map in the top-right corner, ~600 m across, with an arrow for the
// player's position and heading. F4 hides/shows it.
//
//   SURFACE  a relief map of the whole island, BAKED ONCE at startup on a
//            worker thread from the GENERATOR (TerrainGen's S + top material
//            for the sizeClass 2 world; the legacy ColumnSampler + biome
//            surface for the old island), coloured by the engine palette and
//            hill-shaded. It shows the world as generated -- "explored only"
//            is gameplay, later.
//   UNDERGROUND  more than UNDERGROUND_DEPTH_M below the ground in the camera's
//            column, it switches to a DEPTH SLICE at the camera's height, read
//            from the LIVE WORLD (ChunkStore.GetVoxel), so a tunnel the player
//            digs shows up. Refreshed every SLICE_REFRESH_S, a few rows per
//            frame, so a refresh costs well under a millisecond of any frame.
//
// READ-ONLY. The bake thread reads only the immutable TerrainGen map (through
// its own WorldMap view, as DevMap's slice worker does) or its own sampler
// state; the slice reads the store on the MAIN thread. Nothing here writes the
// world. Hidden while the dev map is open, when the HUD is hidden, and in
// capture runs (unless -minimap).
using System;
using System.Threading;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.WorldGen;

public class Minimap : MonoBehaviour
{
    public const float VIEW_M = 600f;
    public const int PANEL_PX = 240;
    public const float UNDERGROUND_DEPTH_M = 12f;
    public const int SLICE_N = 96;                 // 1 m per sample: 96 m across underground
    public const float SLICE_REFRESH_S = 0.5f;
    private const int BAKE_N = 768;

    public static bool Visible = true;
    /// Captures set this to photograph the minimap despite hiding the HUD.
    public static bool ForceShow;

    private Texture2D _surfaceTex, _sliceTex, _px;
    private Color32[] _bakePixels;
    private float[] _bakeHeightM;                  // ground height (metres) per bake texel
    private volatile bool _bakeDone;
    private Thread _bakeThread;
    private float _worldM;                         // world edge length in metres
    private readonly byte[] _toLinear = new byte[256];

    private readonly Color32[] _slicePixels = new Color32[SLICE_N * SLICE_N];
    private int _sliceRow = SLICE_N;               // SLICE_N = no refresh in progress
    private int3 _sliceCentre;
    private float _nextSliceAt;
    private bool _underground;
    private float _depthM;

    private TerrainGen.WorldMap _tgView;           // our own view over the map arrays (bake thread)

    void Start()
    {
        bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
        for (int i = 0; i < 256; i++) _toLinear[i] = (byte)Mathf.RoundToInt(255f * (linear ? Mathf.GammaToLinearSpace(i / 255f) : i / 255f));
        _px = new Texture2D(1, 1); _px.SetPixel(0, 0, Color.white); _px.Apply();
        _sliceTex = new Texture2D(SLICE_N, SLICE_N, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        StartBake();
    }

    void OnDestroy()
    {
        try { _bakeThread?.Join(2000); } catch { }
        _tgView?.Dispose();
        _tgView = null;
    }

    // =====================================================================
    // Surface bake (worker thread)
    // =====================================================================
    private void StartBake()
    {
        _bakePixels = new Color32[BAKE_N * BAKE_N];
        _bakeHeightM = new float[BAKE_N * BAKE_N];
        var src = Phase4Bootstrapper.Streamer != null ? Phase4Bootstrapper.Streamer.TerrainGenSource : null;
        if (Phase4Bootstrapper.IsTg2World && src != null)
        {
            var m = src.Map;
            _tgView = new TerrainGen.WorldMap(m.Spec, m.Height, m.Rough, m.PeakPosition, m.Water, m.Flow, m.Hydro, m.WaterClass);
            var spec = src.Spec; var o = src.O;
            _worldM = spec.worldSizeVoxels * 0.1f;
            _bakeThread = new Thread(() => BakeTg2(spec, o)) { IsBackground = true, Name = "MinimapBake" };
        }
        else
        {
            var meta = Phase4Bootstrapper.Meta;
            if (meta == null) return;
            _worldM = 2560f;   // sizeClass 1: 200 x 200 chunks (§2.4 Small)
            _bakeThread = new Thread(() => BakeLegacy(meta)) { IsBackground = true, Name = "MinimapBake" };
        }
        _bakeThread.Start();
    }

    private Color32 Palette(byte engineMat)
    {
        var c = MaterialPalette.Of(engineMat);
        return new Color32((byte)(c.R * 255), (byte)(c.G * 255), (byte)(c.B * 255), 255);
    }

    private void BakeTg2(TerrainGen.TerrainSpec spec, TerrainGen.Offsets o)
    {
        try
        {
            var view = _tgView.View;
            float step = spec.worldSizeVoxels / (float)BAKE_N;
            var mats = new byte[BAKE_N * BAKE_N];
            var water = new bool[BAKE_N * BAKE_N];
            for (int j = 0; j < BAKE_N; j++)
            for (int i = 0; i < BAKE_N; i++)
            {
                float x = (i + 0.5f) * step, z = (j + 0.5f) * step;
                var c = TerrainGen.ShapeField.Sample(in spec, in view, in o, x, z, 8);
                int k = j * BAKE_N + i;
                bool wet = c.height < spec.seaLevelY || c.water > c.height;
                water[k] = wet;
                _bakeHeightM[k] = (math.max(c.height, wet ? math.max(c.water, spec.seaLevelY) : c.height) + 1) * 0.1f;
                byte g = wet ? (c.waterKind == 1 && c.water > c.height ? TerrainGen.GenMat.Lava : TerrainGen.GenMat.Water)
                             : TerrainGen.DetailField.TopMaterial(in spec, in c, 0f, in o, (int)x, (int)z);
                mats[k] = VoxelEngine.Rendering.FarField.GenToEngine(g);
            }
            Shade(mats, water, step * 0.1f);
        }
        catch (Exception e) { Debug.LogWarning("[Minimap] bake failed: " + e.Message); }
    }

    private void BakeLegacy(VoxelEngine.WorldGen.WorldMetaData meta)
    {
        var st = ColumnSampler.CreateState(meta);
        try
        {
            float step = _worldM * 10f / BAKE_N;
            var mats = new byte[BAKE_N * BAKE_N];
            var water = new bool[BAKE_N * BAKE_N];
            for (int j = 0; j < BAKE_N; j++)
            for (int i = 0; i < BAKE_N; i++)
            {
                ColumnSampler.SampleColumn(in st, (int)((i + 0.5f) * step), (int)((j + 0.5f) * step), out int h, out byte biome);
                int k = j * BAKE_N + i;
                bool wet = h < WorldGenConstants.SEA_LEVEL_VOXEL_Y;
                water[k] = wet;
                _bakeHeightM[k] = (math.max(h, wet ? WorldGenConstants.SEA_LEVEL_VOXEL_Y : h) + 1) * 0.1f;
                mats[k] = wet ? Materials.Water : Biomes.Get(biome).surfaceMaterial;
            }
            Shade(mats, water, step * 0.1f);
        }
        catch (Exception e) { Debug.LogWarning("[Minimap] bake failed: " + e.Message); }
        finally { st.Dispose(); }
    }

    /// Palette colour x a north-west hillshade from the height field; water
    /// darkens with nothing but a faint shore line. Writes _bakePixels.
    private void Shade(byte[] mats, bool[] water, float texelM)
    {
        for (int j = 0; j < BAKE_N; j++)
        for (int i = 0; i < BAKE_N; i++)
        {
            int k = j * BAKE_N + i;
            var col = Palette(mats[k]);
            float s = 1f;
            if (!water[k])
            {
                float hx = _bakeHeightM[j * BAKE_N + math.min(i + 1, BAKE_N - 1)] - _bakeHeightM[j * BAKE_N + math.max(i - 1, 0)];
                float hz = _bakeHeightM[math.min(j + 1, BAKE_N - 1) * BAKE_N + i] - _bakeHeightM[math.max(j - 1, 0) * BAKE_N + i];
                float3 n = math.normalize(new float3(-hx / (2f * texelM), 1f, -hz / (2f * texelM)));
                s = 0.55f + 0.6f * math.saturate(math.dot(n, math.normalize(new float3(-0.6f, 0.9f, 0.5f))));
            }
            else s = 0.8f;
            _bakePixels[k] = new Color32(_toLinear[(byte)math.min(255f, col.r * s)], _toLinear[(byte)math.min(255f, col.g * s)],
                                         _toLinear[(byte)math.min(255f, col.b * s)], 255);
        }
        _bakeDone = true;
    }

    // =====================================================================
    // Per frame (main thread)
    // =====================================================================
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.F4)) Visible = !Visible;
        if (_bakeDone && _surfaceTex == null)
        {
            _surfaceTex = new Texture2D(BAKE_N, BAKE_N, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            _surfaceTex.SetPixels32(_bakePixels);
            _surfaceTex.Apply();
        }
        var cam = Camera.main;
        if (cam == null || _bakeHeightM == null || !_bakeDone) return;

        Vector3 p = cam.transform.position;
        float ground = GroundM(p.x, p.z);
        _depthM = ground - p.y;
        _underground = _depthM > UNDERGROUND_DEPTH_M;
        if (_underground) SliceStep(p);
    }

    private float GroundM(float xM, float zM)
    {
        int i = math.clamp((int)(xM / _worldM * BAKE_N), 0, BAKE_N - 1);
        int j = math.clamp((int)(zM / _worldM * BAKE_N), 0, BAKE_N - 1);
        return _bakeHeightM[j * BAKE_N + i];
    }

    /// A few rows of the depth slice per frame; a new refresh every
    /// SLICE_REFRESH_S, centred on the camera then.
    private void SliceStep(Vector3 camM)
    {
        var store = Phase4Bootstrapper.Store;
        if (store == null) return;
        if (_sliceRow >= SLICE_N)
        {
            if (Time.unscaledTime < _nextSliceAt) return;
            _nextSliceAt = Time.unscaledTime + SLICE_REFRESH_S;
            _sliceCentre = CoordMath.WorldToVoxel(new float3(camM.x, camM.y, camM.z));
            _sliceRow = 0;
        }
        const int ROWS_PER_FRAME = 12;
        for (int r = 0; r < ROWS_PER_FRAME && _sliceRow < SLICE_N; r++, _sliceRow++)
        {
            int z = _sliceCentre.z + (_sliceRow - SLICE_N / 2) * 10;
            for (int c = 0; c < SLICE_N; c++)
            {
                int x = _sliceCentre.x + (c - SLICE_N / 2) * 10;
                byte m = store.GetVoxel(new int3(x, _sliceCentre.y, z));
                Color32 col;
                if (m == Materials.Air) col = new Color32(18, 20, 26, 255);
                else
                {
                    var pc = Palette(m);
                    col = new Color32(_toLinear[(byte)(pc.r * 0.8f)], _toLinear[(byte)(pc.g * 0.8f)], _toLinear[(byte)(pc.b * 0.8f)], 255);
                }
                _slicePixels[_sliceRow * SLICE_N + c] = col;
            }
        }
        if (_sliceRow >= SLICE_N) { _sliceTex.SetPixels32(_slicePixels); _sliceTex.Apply(); }
    }

    void OnGUI()
    {
        if (!ForceShow)
        {
            if (!Visible || Playground.DebugHideUi || DevMap.IsOpen) return;
            if (Playground.IsCaptureRunForUi && !HasArg("-minimap")) return;
        }
        var cam = Camera.main;
        if (cam == null) return;

        float pad = 12f, size = PANEL_PX;
        var rect = new Rect(Screen.width - size - pad, pad, size, size);
        GUI.color = new Color(0f, 0f, 0f, 0.55f);
        GUI.DrawTexture(new Rect(rect.x - 3, rect.y - 3, size + 6, size + 6 + 18), _px);
        GUI.color = Color.white;

        Vector3 p = cam.transform.position;
        string label;
        if (_underground)
        {
            GUI.DrawTexture(rect, _sliceTex, ScaleMode.StretchToFill);
            label = $"underground  -{_depthM:F0} m   (slice {SLICE_N} m)";
        }
        else if (_surfaceTex != null)
        {
            float half = VIEW_M * 0.5f / _worldM;
            float u = p.x / _worldM, v = p.z / _worldM;
            GUI.DrawTextureWithTexCoords(rect, _surfaceTex, new Rect(u - half, v - half, half * 2f, half * 2f));
            label = $"{VIEW_M:F0} m   alt {p.y - (Phase4Bootstrapper.IsTg2World && Phase4Bootstrapper.Streamer?.TerrainGenSource != null ? Phase4Bootstrapper.Streamer.TerrainGenSource.Spec.seaLevelY * 0.1f : WorldGenConstants.SEA_LEVEL_VOXEL_Y * 0.1f):F0} m";
        }
        else label = "map baking...";

        // The player: a heading arrow at the centre (north-up map, +Z up).
        Vector3 f = cam.transform.forward;
        float heading = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
        var c = rect.center;
        var saved = GUI.matrix;
        GUIUtility.RotateAroundPivot(heading, c);
        GUI.color = new Color(1f, 0.85f, 0.3f, 1f);
        GUI.DrawTexture(new Rect(c.x - 2f, c.y - 9f, 4f, 12f), _px);
        GUI.DrawTexture(new Rect(c.x - 5f, c.y - 1f, 10f, 4f), _px);
        GUI.matrix = saved;
        GUI.color = Color.white;
        GUI.Label(new Rect(rect.x, rect.yMax + 1, size, 18), "N^   " + label + "   [F4]");
    }

    private static bool HasArg(string flag)
    {
        foreach (string a in Environment.GetCommandLineArgs()) if (a == flag) return true;
        return false;
    }
}
