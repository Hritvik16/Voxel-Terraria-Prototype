// ==========================================
// Assets/Game/Playground.cs
//
// =========================================================================
// THIS IS A DOGFOOD / FEEL SCENE. IT IS NOT A DIAGNOSTIC SCENE.
// =========================================================================
// It exists to be flown around and poked at, to see whether the engine feels
// and looks right. It is NOT instrumented, it asserts nothing, and it proves
// nothing.
//
// IF SOMETHING LOOKS WRONG HERE, GO REPRODUCE IT IN THE RELEVANT PHASE'S
// ISOLATED SCENE BEFORE TREATING IT AS A BUG:
//   terrain / generation ...... Phase 3 Island 1
//   streaming / persistence ... Phase 4 Streaming  (./run-acceptance-rig.sh)
//   fluid correctness ......... Phase 5b Basin     (./run-phase5b-rig.sh)
//   fluid vs the oracle ....... FluidConservationTests (./run-editmode-tests.sh)
// A number read off this scene is not evidence. A screenshot from it is a
// vibe check, not a result.
//
// =========================================================================
// WHAT THIS SCENE IS HONEST ABOUT
// =========================================================================
// 1. FLUID IS FIXED TO ONE ARENA AND DOES NOT FOLLOW YOU.
//    §7.4 describes a near-player active radius that moves with the player.
//    THAT IS NOT BUILT. FluidGpuSimulation's region is created once, at one
//    world location, and stays there. Fly away and the fluid keeps simulating
//    where you left it; fly far enough and it is simply out of sight. This
//    scene puts fluid in ONE place on purpose so that limitation is visible
//    rather than disguised. "Fluid in one spot" is not "fluid works across the
//    world" and this scene must never be cited as the latter.
//
// 2. THE FLUID POPULATION HERE IS DELIBERATELY TINY.
//    Source budgets are tens to low hundreds of voxels -- the same range
//    Phase 5a/5b actually tested. §2.5's ~500,000 near-player active target has
//    NEVER been tested, and this scene is deliberately not where that gets
//    discovered. Scale testing is a separate, deliberate task; it is not a side
//    effect of a feel scene. Do not raise these budgets to "see what happens"
//    and then quote the result.
//
// 3. FLUID ON NATURAL TERRAIN IS NEW HERE.
//    Phases 5a and 5b both ran on flat, hand-built basins. This is the first
//    time the CA has met generated geometry -- slopes, overhangs, carved
//    features. Anything odd at the fluid/terrain boundary in this scene is a
//    GENUINE NEW FINDING and should be written up, not shrugged off. See
//    §"boundary watch" in the report.
//
// The world itself is real: Phase4Bootstrapper does the actual Phase 3
// generation and Phase 4 streaming. This file adds a camera, some keys, and one
// fluid arena; it does not fake terrain.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Memory;
using VoxelEngine.Mirror;
using VoxelEngine.Simulation;

public class Playground : MonoBehaviour
{
    [SerializeField] private ComputeShader _fluidCA;
    [Tooltip("Arena edge in voxels. Power of two -- the CA addresses its region with shifts.")]
    [SerializeField] private int _arenaEdge = 64;
    [SerializeField] private int _slotCapacity = 8192;
    [SerializeField] private int _maxOpsPerFrame = 8192;
    [SerializeField] private string _outputRootFolderName = "PlaygroundShots";

    // Budgets, tiny on purpose. See header note 2.
    [SerializeField] private int _waterBudget = 160;
    [SerializeField] private int _sandBudget = 90;
    [SerializeField] private int _lavaBudget = 60;

    private ChunkStore _store;
    private TerrainClipmap _clipmap;
    private BrickDataPool _pool;
    private FluidGpuSimulation _fluid;
    private FluidOpListReadback _readback;
    private EditService _edits;

    private int3 _arenaOrigin, _arenaCentre;
    private int3 _waterSrc, _sandSrc, _lavaSrc;
    private int _waterLeft, _sandLeft, _lavaLeft;
    private bool _ready;
    private string _status = "locating a basin in the generated terrain...";

    // ---- Crosshair targeting ----
    private bool _hasTarget;
    private int3 _targetVoxel;      // the solid voxel under the crosshair
    private int3 _targetAdjacent;   // the empty voxel in front of it (where placing goes)
    private byte _targetMaterial;
    private float _targetDistM;

    // ---- Brush ----
    private static readonly byte[] _brushes =
        { Materials.Water, Materials.Sand, Materials.Lava, Materials.Stone };
    private static readonly string[] _brushNames = { "water", "sand", "lava", "stone" };
    private int _brush;
    private PlaygroundFlyCamera _cam;
    private Texture2D _px;

    // ---- Additive feature registration (header requirement e) ----
    // Deliberately the simplest thing that works: a list of named actions the
    // HUD lists and a key runs. New Playground toys register here instead of
    // editing HandleKeys, so adding one is additive.
    public readonly struct Toy
    {
        public readonly KeyCode Key;
        public readonly string Label;
        public readonly Action Run;
        public Toy(KeyCode k, string label, Action run) { Key = k; Label = label; Run = run; }
    }
    private readonly List<Toy> _toys = new List<Toy>();
    public void Register(Toy toy) => _toys.Add(toy);

    IEnumerator Start()
    {
        // Phase4Bootstrapper owns the real world. Wait for it rather than
        // duplicating any of it here.
        while (Phase4Bootstrapper.Store == null || Phase4Bootstrapper.Clipmap == null)
            yield return null;
        for (int i = 0; i < 30; i++) yield return null;   // let the window fill

        _store = Phase4Bootstrapper.Store;
        _clipmap = Phase4Bootstrapper.Clipmap;
        _pool = Phase4Bootstrapper.Pool;
        _edits = new EditService();

        if (!TryFindNaturalBasin(out _arenaCentre))
        {
            _status = "no basin found near spawn — fluid arena not placed";
            yield break;
        }

        int half = _arenaEdge / 2;
        _arenaOrigin = new int3(_arenaCentre.x - half,
                                Mathf.Max(0, _arenaCentre.y - half),
                                _arenaCentre.z - half);

        _fluid = new FluidGpuSimulation(_fluidCA,
            new int3(_arenaEdge, _arenaEdge, _arenaEdge), _slotCapacity, _maxOpsPerFrame)
        {
            RegionOriginVoxels = _arenaOrigin,
            PlayerVoxel = _arenaCentre,
            ActiveRadiusVoxels = EngineConfig.FLUID_ACTIVE_RADIUS_VOXELS,
        };
        _readback = new FluidOpListReadback(_fluid, _store) { OnVoxelApplied = MarkDirtyFor };
        _edits.AttachFluidSimulation(_fluid, _store);

        // Sources sit above the basin floor, inside the arena, so what pours
        // lands on GENERATED ground rather than on anything this file built.
        _waterSrc = new int3(_arenaCentre.x - 6, _arenaOrigin.y + _arenaEdge - 4, _arenaCentre.z);
        _sandSrc  = new int3(_arenaCentre.x + 8, _arenaOrigin.y + _arenaEdge - 4, _arenaCentre.z - 6);
        _lavaSrc  = new int3(_arenaCentre.x + 2, _arenaOrigin.y + _arenaEdge - 4, _arenaCentre.z + 7);

        RegisterDefaultToys();
        _ready = true;
        // Start the player IN the arena. The fluid region is fixed, so spawning
        // outside it means every fluid control silently does nothing useful --
        // which is exactly how the first build felt.
        TeleportToArena();
        _status = $"arena at {_arenaCentre} — fluid is FIXED here and does not follow you";
    }

    private void RegisterDefaultToys()
    {
        // 1-4 SELECT A BRUSH; the mouse applies it at the crosshair.
        //
        // The first version bound 1/2/3 to vents at FIXED points inside the
        // arena, tens of metres from wherever the player happened to be. Press
        // one and nothing visibly happens, because the fluid is pouring
        // correctly somewhere off screen. That is a genuinely bad control
        // scheme, not a bug in the fluid.
        Register(new Toy(KeyCode.Alpha1, "brush: water", () => SetBrush(0)));
        Register(new Toy(KeyCode.Alpha2, "brush: sand", () => SetBrush(1)));
        Register(new Toy(KeyCode.Alpha3, "brush: lava", () => SetBrush(2)));
        Register(new Toy(KeyCode.Alpha4, "brush: stone", () => SetBrush(3)));
        Register(new Toy(KeyCode.V, "vent here", OpenVentAtTarget));
        Register(new Toy(KeyCode.Alpha0, "stop vents", () =>
            { _waterLeft = _sandLeft = _lavaLeft = 0; _status = "vents closed"; }));
        Register(new Toy(KeyCode.F, "fly to arena", TeleportToArena));
    }

    private void SetBrush(int i)
    {
        _brush = Mathf.Clamp(i, 0, _brushes.Length - 1);
        _status = $"brush: {_brushNames[_brush]}";
    }

    /// Opens a continuous source in the air above whatever the crosshair is on,
    /// so a vent appears WHERE YOU ARE LOOKING instead of at a fixed point.
    private void OpenVentAtTarget()
    {
        if (!_hasTarget) { _status = "vent: aim at a surface first"; return; }
        int3 cell = new int3(_targetVoxel.x, _targetVoxel.y + 18, _targetVoxel.z);
        if (!_fluid.InRegion(cell)) { _status = "vent: outside the fluid arena (press F)"; return; }
        byte m = _brushes[_brush];
        if (m == Materials.Water) { _waterSrc = cell; _waterLeft = _waterBudget; }
        else if (m == Materials.Sand) { _sandSrc = cell; _sandLeft = _sandBudget; }
        else if (m == Materials.Lava) { _lavaSrc = cell; _lavaLeft = _lavaBudget; }
        else { _status = "vent: pick a fluid brush (1/2/3)"; return; }
        _status = $"{_brushNames[_brush]} vent open above {_targetVoxel}";
    }

    /// Finds the lowest surface point in a band around spawn -- i.e. a valley or
    /// carved basin the GENERATOR made. Nothing here sculpts terrain; if the
    /// world has no dip near spawn, the arena is simply not placed.
    private bool TryFindNaturalBasin(out int3 centre)
    {
        centre = default;
        int bestY = int.MaxValue;
        bool found = false;
        // Search around WHERE THE CAMERA ACTUALLY IS, not a hardcoded point.
        // The first version searched around Phase4Bootstrapper's own spawn
        // (140.8 m), which sizeClass 1 left in deep ocean 1.1 km from the
        // island -- so the scan found only sea floor under generated water,
        // rejected all of it, and reported "no basin found".
        Camera cam = Camera.main;
        float3 camPos = cam != null
            ? new float3(cam.transform.position.x, cam.transform.position.y, cam.transform.position.z)
            : new float3(1280f, 12f, 1280f);
        int3 spawn = CoordMath.WorldToVoxel(camPos);

        for (int dz = -220; dz <= 220; dz += 8)
        for (int dx = -220; dx <= 220; dx += 8)
        {
            int x = spawn.x + dx, z = spawn.z + dz;
            int surface = SurfaceY(x, z);
            if (surface < 0) continue;
            // Skip ocean: a basin already full of generated water is not a place
            // to watch poured fluid interact with ground.
            if (_store.GetVoxel(new int3(x, surface, z)) == Materials.Water) continue;
            if (surface < bestY) { bestY = surface; centre = new int3(x, surface, z); found = true; }
        }
        return found;
    }

    private int SurfaceY(int x, int z)
    {
        // From above MAX_TERRAIN_HEIGHT (120) downward -- starting at 90 missed
        // anything on a hill.
        for (int y = WorldGenConstants.MAX_TERRAIN_HEIGHT + 2; y >= 1; y--)
            if (_store.GetVoxel(new int3(x, y, z)) != Materials.Air) return y;
        return -1;
    }

    private void MarkDirtyFor(int3 v) => _clipmap.MarkDirty(CoordMath.VoxelToChunk(v));

    private void Edit(int3 v, byte m)
    {
        _store.SetVoxel(v, m);
        MarkDirtyFor(v);
        _edits.NotifyEdited(v);
    }

    private void EditBox(byte m)
    {
        int3 c = CameraVoxel();
        for (int z = -1; z <= 1; z++)
        for (int y = -1; y <= 1; y++)
        for (int x = -1; x <= 1; x++)
            Edit(c + new int3(x, y, z), m);
    }

    /// Voxel DDA from the camera through the world, stopping at the first solid
    /// cell. Straight-line stepping on the CPU against ChunkStore.GetVoxel --
    /// it shares no code with the GPU raymarcher and is not a check on it.
    private void UpdateTarget()
    {
        _hasTarget = false;
        Camera cam = Camera.main;
        if (cam == null || _store == null) return;

        float3 originM = new float3(cam.transform.position.x, cam.transform.position.y, cam.transform.position.z);
        float3 dir = math.normalize(new float3(cam.transform.forward.x, cam.transform.forward.y, cam.transform.forward.z));

        const float reachM = 12f;
        const float stepM = 0.05f;       // half a voxel; fine enough not to skip one
        int3 prev = CoordMath.WorldToVoxel(originM);
        for (float t = 0.15f; t < reachM; t += stepM)
        {
            int3 v = CoordMath.WorldToVoxel(originM + dir * t);
            if (v.Equals(prev)) continue;
            byte m = _store.GetVoxel(v);
            if (m != Materials.Air)
            {
                _hasTarget = true;
                _targetVoxel = v;
                _targetAdjacent = prev;      // last empty cell before the hit
                _targetMaterial = m;
                _targetDistM = t;
                return;
            }
            prev = v;
        }
    }

    private static string MaterialName(byte m)
    {
        if (m == Materials.Air) return "air";
        if (m == Materials.Stone) return "stone";
        if (m == Materials.Grass) return "grass";
        if (m == Materials.Sand) return "sand";
        if (m == Materials.MossyStone) return "mossy stone";
        if (m == Materials.Water) return "water";
        if (m == Materials.Snow) return "snow";
        if (m == Materials.Sandstone) return "sandstone";
        if (m == Materials.JungleGrass) return "jungle grass";
        if (m == Materials.Deepstone) return "deepstone";
        if (m == Materials.Lava) return "lava";
        if (m == Materials.Honey) return "honey";
        if (m == Materials.Obsidian) return "obsidian";
        return $"id {m}";
    }

    private int3 CameraVoxel()
    {
        Camera cam = Camera.main;
        if (cam == null) return _arenaCentre;
        Vector3 p = cam.transform.position + cam.transform.forward * 3f;
        return CoordMath.WorldToVoxel(new float3(p.x, p.y, p.z));
    }

    private void TeleportToArena()
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        cam.transform.position = new Vector3(
            _arenaCentre.x * 0.1f - 3.2f, _arenaCentre.y * 0.1f + 2.6f, _arenaCentre.z * 0.1f - 3.2f);
        cam.transform.rotation = Quaternion.Euler(18f, 45f, 0f);
        _status = "flew to the fluid arena";
    }

    void Update()
    {
        if (!_ready) return;
        if (_cam == null && Camera.main != null) _cam = Camera.main.GetComponent<PlaygroundFlyCamera>();

        UpdateTarget();
        foreach (var t in _toys) if (Input.GetKeyDown(t.Key)) t.Run();
        HandleMouse();

        Emit(ref _waterLeft, _waterSrc, Materials.Water);
        Emit(ref _sandLeft, _sandSrc, Materials.Sand);
        Emit(ref _lavaLeft, _lavaSrc, Materials.Lava);

        if (_readback.CanIssue)
        {
            _fluid.Tick(_clipmap);
            _readback.IssueReadback(0);
        }
        _readback.PumpAndApply();
    }

    /// Mouse actions only fire while the camera has the cursor captured, so the
    /// click that re-focuses the window cannot also dig a hole.
    private void HandleMouse()
    {
        if (_cam == null || !_cam.Captured) return;

        float scroll = Input.mouseScrollDelta.y;
        if (Mathf.Abs(scroll) > 0.01f)
            SetBrush((_brush + (scroll > 0 ? 1 : _brushes.Length - 1)) % _brushes.Length);

        if (!_hasTarget) return;

        if (Input.GetMouseButton(0))          // held: mine continuously
        {
            EditSphere(_targetVoxel, 2, Materials.Air);
            _status = $"mining at {_targetVoxel}";
        }
        else if (Input.GetMouseButton(1))     // held: paint the brush
        {
            byte m = _brushes[_brush];
            if (m == Materials.Stone) EditSphere(_targetAdjacent, 2, m);
            else EditSphere(_targetAdjacent, 1, m);   // a small blob of fluid
            _status = $"placing {_brushNames[_brush]} at {_targetAdjacent}";
        }
    }

    private void EditSphere(int3 centre, int radius, byte m)
    {
        int r2 = radius * radius;
        for (int z = -radius; z <= radius; z++)
        for (int y = -radius; y <= radius; y++)
        for (int x = -radius; x <= radius; x++)
        {
            if (x * x + y * y + z * z > r2) continue;
            Edit(centre + new int3(x, y, z), m);
        }
    }

    private void Emit(ref int budget, int3 cell, byte material)
    {
        if (budget <= 0) return;
        if (_store.GetVoxel(cell) != Materials.Air) return;
        Edit(cell, material);
        budget--;
    }

    /// For PlaygroundCapture only. Opens all three vents above the arena centre.
    /// The capture pass used to press 1/2/3, which now select a BRUSH rather
    /// than opening a vent -- so without this the screenshot pass would have
    /// quietly photographed an empty arena and reported success.
    public void DebugOpenAllVents()
    {
        int top = _arenaOrigin.y + _arenaEdge - 4;
        _waterSrc = new int3(_arenaCentre.x - 6, top, _arenaCentre.z);
        _sandSrc  = new int3(_arenaCentre.x + 8, top, _arenaCentre.z - 6);
        _lavaSrc  = new int3(_arenaCentre.x + 2, top, _arenaCentre.z + 7);
        _waterLeft = _waterBudget; _sandLeft = _sandBudget; _lavaLeft = _lavaBudget;
        _status = "capture: all vents open";
    }

    /// For PlaygroundCapture only -- lets the screenshot pass trigger a toy
    /// without duplicating the key table. Not part of the playable surface.
    public void DebugRunKey(KeyCode k)
    {
        foreach (var t in _toys) if (t.Key == k) { t.Run(); return; }
    }

    // ---- Screen-space voxel highlight -------------------------------------
    // Drawn in IMGUI rather than with GL lines or a wireframe mesh. The scene is
    // a compute raymarch blitted into the camera target, so anything drawn
    // through the normal geometry path has no depth to sort against and may or
    // may not composite depending on the render-graph pass order. Projecting the
    // cube's eight corners and drawing the twelve edges as 2D lines always lands
    // on top, and costs nothing.
    private void DrawLine(Vector2 a, Vector2 b, Color c, float w)
    {
        if (_px == null)
        {
            _px = new Texture2D(1, 1);
            _px.SetPixel(0, 0, Color.white);
            _px.Apply();
        }
        Vector2 d = b - a;
        float len = d.magnitude;
        if (len < 0.01f) return;
        float ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
        Color old = GUI.color;
        GUI.color = c;
        Matrix4x4 m = GUI.matrix;
        GUIUtility.RotateAroundPivot(ang, a);
        GUI.DrawTexture(new Rect(a.x, a.y - w * 0.5f, len, w), _px);
        GUI.matrix = m;
        GUI.color = old;
    }

    private void DrawVoxelHighlight(Camera cam, int3 v, Color c, float width)
    {
        // Voxels are 0.1 m (§2.3). Inflate very slightly so the outline sits
        // just outside the surface instead of z-fighting it visually.
        Vector3 lo = new Vector3(v.x, v.y, v.z) * 0.1f - Vector3.one * 0.002f;
        Vector3 hi = lo + Vector3.one * (0.1f + 0.004f);

        Vector3[] w = new Vector3[8];
        for (int i = 0; i < 8; i++)
            w[i] = new Vector3((i & 1) == 0 ? lo.x : hi.x,
                               (i & 2) == 0 ? lo.y : hi.y,
                               (i & 4) == 0 ? lo.z : hi.z);

        Vector2[] p = new Vector2[8];
        for (int i = 0; i < 8; i++)
        {
            Vector3 sp = cam.WorldToScreenPoint(w[i]);
            if (sp.z <= 0f) return;                       // behind the camera
            p[i] = new Vector2(sp.x, Screen.height - sp.y);
        }

        int[,] edges = {
            {0,1},{1,3},{3,2},{2,0},   // -z face
            {4,5},{5,7},{7,6},{6,4},   // +z face
            {0,4},{1,5},{2,6},{3,7},   // connecting
        };
        for (int e = 0; e < 12; e++)
            DrawLine(p[edges[e, 0]], p[edges[e, 1]], c, width);
    }

    void OnGUI()
    {
        // UI scale. Unity lays IMGUI out in PHYSICAL pixels, so on a 2880x1800
        // backbuffer a 14 pt label is genuinely unreadable -- which is exactly
        // how the first build looked. Everything textual below is drawn through
        // this scale; the crosshair and voxel highlight are NOT, because they
        // are projected from world space and must stay in real screen pixels.
        float ui = Mathf.Max(1f, Screen.height / 900f);
        var st = new GUIStyle(GUI.skin.label) { fontSize = 14, richText = true };

        // ---- Crosshair + target highlight ----
        Camera cam = Camera.main;
        if (cam != null)
        {
            // Crosshair, drawn DARK-THEN-LIGHT. A plain white crosshair is
            // invisible against snow, which is most of this island -- the first
            // build's was, in the very screenshot taken to check it.
            // Sized off screen height so it does not shrink to nothing on a
            // Retina backbuffer.
            float cx = Screen.width * 0.5f, cy = Screen.height * 0.5f;
            float k = Mathf.Max(1f, Screen.height / 900f);
            float gap = 4f * k, len = 13f * k;
            Color dark = new Color(0f, 0f, 0f, 0.65f);
            Color lite = new Color(1f, 1f, 1f, 0.95f);
            for (int pass = 0; pass < 2; pass++)
            {
                Color c = pass == 0 ? dark : lite;
                float w = pass == 0 ? 4f * k : 1.6f * k;
                DrawLine(new Vector2(cx - len, cy), new Vector2(cx - gap, cy), c, w);
                DrawLine(new Vector2(cx + gap, cy), new Vector2(cx + len, cy), c, w);
                DrawLine(new Vector2(cx, cy - len), new Vector2(cx, cy - gap), c, w);
                DrawLine(new Vector2(cx, cy + gap), new Vector2(cx, cy + len), c, w);
            }

            if (_ready && _hasTarget)
            {
                bool inRegion = _fluid != null && _fluid.InRegion(_targetVoxel);
                // Amber inside the fluid arena, grey outside it -- so "why is
                // nothing happening here" is answerable at a glance.
                // Dark backing pass first, same reason as the crosshair.
                DrawVoxelHighlight(cam, _targetVoxel, new Color(0f, 0f, 0f, 0.55f), 4.5f * k);
                DrawVoxelHighlight(cam, _targetVoxel,
                    inRegion ? new Color(1f, 0.78f, 0.25f, 0.98f)
                             : new Color(0.85f, 0.85f, 0.88f, 0.9f), 2f * k);
            }
        }
        var sb = new StringBuilder();
        sb.AppendLine("<b>Playground — dogfood scene. Not a diagnostic scene, not a scale test.</b>");
        sb.AppendLine(_cam != null && _cam.Captured
            ? "<b>LMB</b> mine   <b>RMB</b> place brush   <b>scroll</b> cycle brush   WASD/QE fly, Shift sprint   ESC release mouse"
            : "<color=#ffc64a><b>CLICK the window to capture the mouse and look around</b></color>   (ESC releases)");
        if (_ready)
        {
            var keys = new StringBuilder();
            foreach (var t in _toys) keys.Append(t.Key.ToString().Replace("Alpha", "")).Append(' ').Append(t.Label).Append("   ");
            sb.AppendLine(keys.ToString());
            sb.AppendLine($"fluid arena {_arenaEdge}^3 voxels at {_arenaCentre} — FIXED, does not follow the camera (§7.4's moving radius is unbuilt)");
            sb.AppendLine($"budgets water {_waterBudget} / sand {_sandBudget} / lava {_lavaBudget} — deliberately tiny; this is NOT a scale test");
            sb.Append($"<b>brush: {_brushNames[_brush]}</b>   ");
            if (_hasTarget)
            {
                bool inRegion = _fluid != null && _fluid.InRegion(_targetVoxel);
                sb.AppendLine($"looking at <b>{MaterialName(_targetMaterial)}</b> @ {_targetVoxel}  " +
                              $"{_targetDistM:F1} m  " +
                              (inRegion ? "<color=#ffc64a>[inside fluid arena]</color>"
                                        : "<color=#bbbbbb>[OUTSIDE arena — fluid will not simulate here]</color>"));
            }
            else sb.AppendLine("looking at nothing within 12 m");
        }
        sb.AppendLine(_status);
        Matrix4x4 prev = GUI.matrix;
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one * ui);
        GUI.Label(new Rect(12, 8, 1400, 180), sb.ToString(), st);
        GUI.matrix = prev;
    }

    void OnDestroy()
    {
        _readback?.Dispose();
        _fluid?.Dispose();
    }
}
