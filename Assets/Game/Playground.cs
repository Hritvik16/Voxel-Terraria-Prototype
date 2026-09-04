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
        _status = $"arena at {_arenaCentre} — fluid is FIXED here and does not follow you";
    }

    private void RegisterDefaultToys()
    {
        Register(new Toy(KeyCode.Alpha1, "pour water", () => { _waterLeft = _waterBudget; _status = "pouring water"; }));
        Register(new Toy(KeyCode.Alpha2, "drop sand", () => { _sandLeft = _sandBudget; _status = "dropping sand"; }));
        Register(new Toy(KeyCode.Alpha3, "lava vent", () => { _lavaLeft = _lavaBudget; _status = "lava vent open"; }));
        Register(new Toy(KeyCode.Alpha4, "place block", () => { EditBox(Materials.Stone); _status = "placed stone"; }));
        Register(new Toy(KeyCode.Alpha5, "mine", () => { EditBox(Materials.Air); _status = "mined"; }));
        Register(new Toy(KeyCode.Alpha0, "stop sources", () => { _waterLeft = _sandLeft = _lavaLeft = 0; _status = "sources closed"; }));
        Register(new Toy(KeyCode.F, "fly to arena", TeleportToArena));
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
        foreach (var t in _toys) if (Input.GetKeyDown(t.Key)) t.Run();

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

    private void Emit(ref int budget, int3 cell, byte material)
    {
        if (budget <= 0) return;
        if (_store.GetVoxel(cell) != Materials.Air) return;
        Edit(cell, material);
        budget--;
    }

    /// For PlaygroundCapture only -- lets the screenshot pass trigger a toy
    /// without duplicating the key table. Not part of the playable surface.
    public void DebugRunKey(KeyCode k)
    {
        foreach (var t in _toys) if (t.Key == k) { t.Run(); return; }
    }

    void OnGUI()
    {
        var st = new GUIStyle(GUI.skin.label) { fontSize = 14, richText = true };
        var sb = new StringBuilder();
        sb.AppendLine("<b>Playground — dogfood scene. Not a diagnostic scene, not a scale test.</b>");
        sb.AppendLine("WASD + right-mouse to look, Q/E down/up, Shift sprint");
        if (_ready)
        {
            var keys = new StringBuilder();
            foreach (var t in _toys) keys.Append(t.Key.ToString().Replace("Alpha", "")).Append(' ').Append(t.Label).Append("   ");
            sb.AppendLine(keys.ToString());
            sb.AppendLine($"fluid arena {_arenaEdge}^3 voxels at {_arenaCentre} — FIXED, does not follow the camera (§7.4's moving radius is unbuilt)");
            sb.AppendLine($"budgets water {_waterBudget} / sand {_sandBudget} / lava {_lavaBudget} — deliberately tiny; this is NOT a scale test");
        }
        sb.AppendLine(_status);
        GUI.Label(new Rect(12, 8, 1400, 140), sb.ToString(), st);
    }

    void OnDestroy()
    {
        _readback?.Dispose();
        _fluid?.Dispose();
    }
}
