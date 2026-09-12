// ==========================================
// Assets/Game/Phase5bDemo.cs
//
// §13 Phase 5b's visual acceptance: "Live fluid visible while falling
// (authoritative-byte proof -- the renderer shows it with no fluid-specific
// code, once an op is applied)."
//
// ONE scene, TWO modes:
//   -phase5bdemo   scripted sequence, beauty screenshots at chosen moments,
//                  quits itself. This is the record.
//   (no flag)      playable: flycam + number keys, for messing with by hand.
//
// -------------------------------------------------------------------------
// THERE IS NO FLUID-SPECIFIC RENDERING CODE ANYWHERE IN THIS FILE
// -------------------------------------------------------------------------
// That is the whole point of the demo, and it is worth stating because it
// looks like an omission. §3.10's authoritative-byte rule means a committed
// fluid move becomes an op, the CPU applies it through ChunkStore.SetVoxel,
// the chunk goes dirty, the clipmap uploads it, and the SHIPPED raymarcher
// draws it having no idea fluid exists. Constructing a TerrainClipmap sets
// TerrainClipmap.Active, which RaymarchFeature already reads. That is the
// entire "wire the renderer to fluid" step (§13 5b file 4).
//
// The terrain is deliberately not a bare box: a stone shell with a raised
// shelf and a spillway, so poured water visibly cascades, pools, and finds a
// level instead of just filling a tub -- which is what makes a screenshot
// worth looking at.

using System;
using System.Collections;
using System.IO;
using System.Text;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Memory;
using VoxelEngine.Mirror;
using VoxelEngine.Simulation;

public class Phase5bDemo : MonoBehaviour
{
    [SerializeField] private ComputeShader _fluidCA;
    [SerializeField] private int _slotCapacity = 131072;
    [SerializeField] private int _maxOpsPerFrame = 32768;
    [SerializeField] private string _outputRootFolderName = "Phase5bDemo";

    // 128 x 64 x 128 voxels = 12.8 x 6.4 x 12.8 m, inside chunk (0,0,0).
    // Power of two in every axis: the CA addresses its region with shifts.
    public const int SX = 128, SY = 64, SZ = 128;

    private BrickDataPool _pool;
    private ChunkHandleAllocator _allocator;
    private ChunkStore _store;
    private TerrainClipmap _clipmap;
    private FluidGpuSimulation _fluid;
    private FluidOpListReadback _readback;
    private EditService _edits;

    private int3 _waterSrc, _lavaSrc, _sandSrc;
    private int _waterLeft, _lavaLeft, _sandLeft;
    private string _status = "";
    private string _runFolder;
    private readonly StringBuilder _log = new StringBuilder();
    private int _shots;

    // ---- Voxel-space landmarks, in one place so the camera and the sources
    // ---- cannot drift apart from the geometry. ----
    private static readonly int3 ShelfMin = new int3(20, 1, 20);
    private static readonly int3 ShelfMax = new int3(74, 26, 74);
    private const int PoolFloorY = 1;

    void Start()
    {
        BuildWorld();
        if (Application.isBatchMode || HasFlag("-phase5bdemo")) StartCoroutine(RunDemo());
        else _status = "PLAYABLE — 1 water  2 sand  3 lava  4 block  5 mine  R reset";
    }

    private static bool HasFlag(string f)
    {
        foreach (string a in Environment.GetCommandLineArgs())
            if (string.Equals(a, f, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    // =====================================================================

    public void BuildWorld()
    {
        Teardown();

        _pool = new BrickDataPool(EngineConfig.BRICK_POOL_CAP, rangeAware: true);
        _allocator = new ChunkHandleAllocator(1024);
        _store = new ChunkStore(_pool, _allocator);
        _store.InsertChunk(new Chunk { coord = int3.zero, isUniform = true, uniformMaterial = Materials.Air });

        _clipmap = new TerrainClipmap(new int3(EngineConfig.WINDOW_CHUNKS_XZ,
                                               EngineConfig.MIRROR_CHUNKS_Y,
                                               EngineConfig.WINDOW_CHUNKS_XZ), _pool.Capacity);
        _clipmap.SetWindowOrigin(int3.zero);
        _edits = new EditService();

        // ---- One open, walled arena. Deliberately SIMPLE: an earlier version
        // ---- had a shelf and a spillway, and the extra geometry only made it
        // ---- harder to frame and stopped the water and lava ever meeting.
        // Floor.
        Fill(new int3(2, 0, 2), new int3(SX - 3, 1, SZ - 3), Materials.Stone);
        // Walls, tall enough to contain a deep pool and to read as a room.
        for (int y = 2; y <= 34; y++)
        {
            Fill(new int3(2, y, 2), new int3(SX - 3, y, 3), Materials.Stone);
            Fill(new int3(2, y, SZ - 4), new int3(SX - 3, y, SZ - 3), Materials.Stone);
            Fill(new int3(2, y, 2), new int3(3, y, SZ - 3), Materials.Stone);
            Fill(new int3(SX - 4, y, 2), new int3(SX - 3, y, SZ - 3), Materials.Stone);
        }
        // A low plinth between the two liquid sources: water and lava each pour
        // off one side of it and meet on the open floor, which is what makes the
        // obsidian reaction happen somewhere visible instead of never.
        Fill(new int3(58, 2, 78), new int3(74, 9, 96), Materials.Stone);
        // A stepped ledge for the sand pile to build against.
        Fill(new int3(96, 2, 40), new int3(116, 6, 60), Materials.Stone);

        _clipmap.MarkDirty(int3.zero);
        _clipmap.UploadDirty(_store, _pool);

        _fluid = new FluidGpuSimulation(_fluidCA, new int3(SX, SY, SZ), _slotCapacity, _maxOpsPerFrame)
        {
            RegionOriginVoxels = int3.zero,
            PlayerVoxel = new int3(SX / 2, SY / 2, SZ / 2),
            ActiveRadiusVoxels = EngineConfig.FLUID_ACTIVE_RADIUS_VOXELS,
        };
        _readback = new FluidOpListReadback(_fluid, _store) { OnVoxelApplied = MarkDirtyFor };
        _edits.AttachFluidSimulation(_fluid, _store);

        // Both liquids fall ~40 voxels in open air (so the fall is visible),
        // land either side of the plinth, spread, and meet on the floor.
        // 16 voxels apart, not 32. At 32 the two pools spread too slowly to
        // ever touch and the obsidian reaction never fired -- measured
        // obsidian 0 across a whole run. Close enough that both pools reach the
        // midpoint, far enough that each is a distinct visible stream.
        _waterSrc = new int3(52, 46, 64);
        _lavaSrc  = new int3(68, 46, 64);
        _sandSrc  = new int3(106, 46, 50);
        _waterLeft = _lavaLeft = _sandLeft = 0;
    }

    private void Fill(int3 a, int3 b, byte m)
    {
        for (int z = a.z; z <= b.z; z++)
        for (int y = a.y; y <= b.y; y++)
        for (int x = a.x; x <= b.x; x++)
            _store.SetVoxel(new int3(x, y, z), m);
    }

    private void MarkDirtyFor(int3 v) => _clipmap.MarkDirty(CoordMath.VoxelToChunk(v));

    public void Edit(int3 v, byte m)
    {
        if (v.x < 0 || v.y < 0 || v.z < 0 || v.x >= SX || v.y >= SY || v.z >= SZ) return;
        _store.SetVoxel(v, m);
        MarkDirtyFor(v);
        _edits.NotifyEdited(v);
    }

    void Update()
    {
        if (_fluid == null) return;
        if (!Application.isBatchMode && !HasFlag("-phase5bdemo")) HandleKeys();
        StepFluid();
    }

    private void StepFluid()
    {
        Emit(ref _waterLeft, _waterSrc, Materials.Water);
        Emit(ref _lavaLeft, _lavaSrc, Materials.Lava);
        Emit(ref _sandLeft, _sandSrc, Materials.Sand);

        _clipmap.UploadDirty(_store, _pool);
        if (_readback.CanIssue)
        {
            _fluid.Tick(_clipmap);
            _readback.IssueReadback(0);
        }
        _readback.PumpAndApply();
        _clipmap.UploadDirty(_store, _pool);
    }

    private void Emit(ref int budget, int3 cell, byte material)
    {
        if (budget <= 0) return;
        if (_store.GetVoxel(cell) != Materials.Air) return;
        Edit(cell, material);
        budget--;
    }

    // =====================================================================
    // Playable controls
    // =====================================================================

    private void HandleKeys()
    {
        if (Input.GetKeyDown(KeyCode.Alpha1)) { _waterLeft = 900; _status = "pouring water"; }
        if (Input.GetKeyDown(KeyCode.Alpha2)) { _sandLeft = 400; _status = "dropping sand"; }
        if (Input.GetKeyDown(KeyCode.Alpha3)) { _lavaLeft = 300; _status = "lava vent open"; }
        if (Input.GetKeyDown(KeyCode.Alpha4)) { PlaceBlockUnderCamera(); _status = "placed a stone block"; }
        if (Input.GetKeyDown(KeyCode.Alpha5)) { MineUnderCamera(); _status = "mined a 3x3x3"; }
        if (Input.GetKeyDown(KeyCode.R)) { BuildWorld(); _status = "world reset"; }
        if (Input.GetKeyDown(KeyCode.Alpha0)) { _waterLeft = _lavaLeft = _sandLeft = 0; _status = "sources closed"; }
    }

    private int3 CameraVoxel()
    {
        Camera c = Camera.main;
        if (c == null) return new int3(SX / 2, SY / 2, SZ / 2);
        Vector3 p = c.transform.position + c.transform.forward * 2.5f;
        return CoordMath.WorldToVoxel(new float3(p.x, p.y, p.z));
    }

    private void PlaceBlockUnderCamera()
    {
        int3 c = CameraVoxel();
        for (int z = -1; z <= 1; z++)
        for (int y = -1; y <= 1; y++)
        for (int x = -1; x <= 1; x++)
            Edit(c + new int3(x, y, z), Materials.Stone);
    }

    private void MineUnderCamera()
    {
        int3 c = CameraVoxel();
        for (int z = -1; z <= 1; z++)
        for (int y = -1; y <= 1; y++)
        for (int x = -1; x <= 1; x++)
            Edit(c + new int3(x, y, z), Materials.Air);
    }

    void OnGUI()
    {
        if (Application.isBatchMode || HasFlag("-phase5bdemo")) return;
        var st = new GUIStyle(GUI.skin.label) { fontSize = 15, richText = true };
        GUI.Label(new Rect(14, 10, 900, 24), "<b>Phase 5b — GPU fluid, shipped raymarcher</b>", st);
        GUI.Label(new Rect(14, 32, 900, 24),
            "1 pour water   2 drop sand   3 lava vent   4 place block   5 mine   0 stop sources   R reset", st);
        GUI.Label(new Rect(14, 54, 900, 24),
            "WASD + mouse to fly (hold right-mouse to look), Q/E down/up", st);
        GUI.Label(new Rect(14, 76, 900, 24), _status, st);
    }

    // =====================================================================
    // Scripted demo
    // =====================================================================

    private IEnumerator RunDemo()
    {
        Screen.SetResolution(1920, 1080, false);
        for (int i = 0; i < 5; i++) yield return null;

        string ts = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
        _runFolder = Path.Combine(Application.persistentDataPath, _outputRootFolderName, ts);
        Directory.CreateDirectory(_runFolder);
        L($"Phase 5b demo — {ts}");
        L($"device {SystemInfo.graphicsDeviceName}, {Screen.width}x{Screen.height}");
        L("Rendered by the SHIPPED raymarcher. No fluid-specific render code exists (§3.10).");
        L("");

        yield return Shot("00_world_empty", "the built world, before any fluid");

        // ---- Water: pouring, mid-fall down the spillway, pooling ----
        _waterLeft = 1200;
        yield return Run(70);  yield return Shot("01_water_pouring", "water stream falling from the source");
        yield return Run(120); yield return Shot("02_water_midfall", "the stream mid-fall, ~40 voxels of open air");
        yield return Run(320); yield return Shot("03_water_pooled", "water pooled and spreading on the floor");

        // ---- Sand: a pile forming on the ledge ----
        _sandLeft = 500;
        yield return Run(120); yield return Shot("04_sand_falling", "sand column mid-fall");
        yield return Run(260); yield return Shot("05_sand_piled", "sand at its angle of repose");

        // ---- Lava: slow flow, then the obsidian reaction where it meets water
        _lavaLeft = 700;
        yield return Run(300); yield return Shot("06_lava_flowing", "lava flowing (ticks every 6, §7.4)");
        yield return Run(700); yield return Shot("07_lava_meets_water", "lava pool spreading toward the water");
        yield return Run(900); yield return Shot("08_obsidian", "water + lava -> obsidian (§7.6)");

        // ---- An edit into a live stream ----
        _waterLeft = 600;
        yield return Run(60);
        for (int z = 58; z <= 70; z++)
        for (int x = 46; x <= 58; x++)
            Edit(new int3(x, 30, z), Materials.Stone);
        L("edit: stone shelf inserted into the live waterfall");
        yield return Run(40);  yield return Shot("09_edit_into_stream", "block placed into the falling stream");
        yield return Run(240); yield return Shot("10_after_edit_settled", "stream re-routed and settled");

        L("");
        L($"materials at end: water {Count(Materials.Water)}  sand {Count(Materials.Sand)}  " +
          $"lava {Count(Materials.Lava)}  obsidian {Count(Materials.Obsidian)}  stone {Count(Materials.Stone)}");
        L($"screenshots captured: {_shots}");
        L(_shots >= 11 ? "RESULT: demo complete." : "RESULT: FAILED — expected 11 screenshots.");

        File.WriteAllText(Path.Combine(_runFolder, "demo_report.txt"), _log.ToString());
        Debug.Log("[Phase5bDemo]\n" + _log);
        yield return null;
        Application.Quit(_shots >= 11 ? 0 : 1);
    }

    private IEnumerator Run(int frames)
    {
        for (int i = 0; i < frames; i++) yield return null;
    }

    private IEnumerator Shot(string name, string caption)
    {
        _readback.DrainBlocking();
        _clipmap.UploadDirty(_store, _pool);
        for (int i = 0; i < 4; i++) yield return null;
        yield return new WaitForEndOfFrame();

        Texture2D shot = ScreenCapture.CaptureScreenshotAsTexture();
        try { File.WriteAllBytes(Path.Combine(_runFolder, name + ".png"), shot.EncodeToPNG()); }
        finally { Destroy(shot); }
        _shots++;
        L($"{name}.png — {caption}   [water {Count(Materials.Water)} sand {Count(Materials.Sand)} " +
          $"lava {Count(Materials.Lava)} obsidian {Count(Materials.Obsidian)}]");
    }

    /// EXACT, not sampled. An earlier version scanned every other cell and
    /// scaled by 4, which put estimates into a record that is supposed to be
    /// evidence. The scan is slower and only runs at screenshot time.
    private int Count(byte m)
    {
        int n = 0;
        for (int z = 0; z < SZ; z++)
        for (int y = 0; y < SY; y++)
        for (int x = 0; x < SX; x++)
            if (_store.GetVoxel(new int3(x, y, z)) == m) n++;
        return n;
    }

    private void L(string s) => _log.AppendLine(s);

    void OnDestroy() => Teardown();

    private void Teardown()
    {
        _readback?.Dispose(); _readback = null;
        _fluid?.Dispose(); _fluid = null;
        _clipmap?.Dispose(); _clipmap = null;
        _pool?.Dispose(); _pool = null;
        _store = null; _edits = null;
    }
}
