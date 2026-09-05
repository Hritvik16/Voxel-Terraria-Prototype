// ==========================================
// Assets/Game/Playground.cs
//
// =========================================================================
// THIS IS A DOGFOOD / FEEL SCENE. IT IS NOT A DIAGNOSTIC SCENE.
// =========================================================================
// It exists to be walked around and poked at, to see whether the engine feels
// and looks right. It is NOT instrumented, it asserts nothing, and it proves
// nothing.
//
// IF SOMETHING LOOKS WRONG HERE, GO REPRODUCE IT IN THE RELEVANT PHASE'S
// ISOLATED SCENE BEFORE TREATING IT AS A BUG:
//   terrain / generation ...... Phase 3 Island 1
//   streaming / persistence ... Phase 4 Streaming  (./run-acceptance-rig.sh)
//   fluid correctness ......... Phase 5b Basin     (./run-phase5b-rig.sh)
//   fluid vs the oracle ....... FluidConservationTests (./run-editmode-tests.sh)
//   player / CCD / editing .... Phase 6 rigs       (./run-phase6-*.sh)
//   everything at once ........ ./run-phase6-sandbox.sh
// A number here is not evidence. A screenshot from here is a vibe check.
//
// =========================================================================
// WHAT THIS SCENE IS HONEST ABOUT
// =========================================================================
// 1. THE FLUID ARENA IS FIXED. THE ACTIVITY INSIDE IT NOW FOLLOWS YOU.
//    Two different things, and this note used to conflate them.
//
//    THE REGION BOX IS STILL FIXED, and deliberately so. It is the CA's
//    addressing space -- §7.2's op-list is indexed by region cell, so moving the
//    origin would re-index every slot home and every in-flight batch, applying
//    ops to the wrong voxels. Placing fluid outside it is still refused, still
//    for the original reason (see TryPaintBrush): a mobile material written out
//    there would be drawn and never simulated, hanging frozen in mid-air.
//
//    WHAT IS NEW is §7.4's near-player active radius, which is now DRIVEN.
//    Playground calls UpdatePlayerPosition every frame, so fluid simulates only
//    within a radius of you and sleeps back to static terrain when you leave --
//    "distant water is a settled terrain byte that looks like water but does not
//    tick". Walk away from a pool and it stops moving; come back and it wakes.
//    That is §7.4 working, not fluid breaking.
//
//    The GPU always had this test; nothing updated the centre, so it was
//    anchored wherever the region was created. The radius here is a DEMO value
//    (see _activeRadiusVoxels), far smaller than the shipped 1280 so the effect
//    is observable inside a 64-voxel arena at all.
//
// 2. THE FLUID POPULATION HERE IS DELIBERATELY TINY.
//    Source budgets are tens to low hundreds of voxels -- the range Phase 5a/5b
//    actually tested. §2.5's ~500,000 near-player active target has NEVER been
//    tested, and this scene is deliberately not where that gets discovered.
//
// 3. FLUID ON NATURAL TERRAIN IS NEW HERE. Phases 5a/5b ran on flat hand-built
//    basins. Anything odd at the fluid/terrain boundary here is a GENUINE NEW
//    FINDING and should be written up, not shrugged off.
//
// 4. SWIMMING IS COMPOSED HERE, NOT IN THE ENGINE. §8.6's Buoyancy PRODUCES
//    forces; PlayerMotor does not consume them, by design -- §8.6 is one-way
//    sampling and the motor knows nothing about fluid beyond "it does not
//    block". This scene applies the buoyant acceleration and drag to the motor
//    itself, in ApplyBuoyancy below, because a demo where you cannot swim
//    cannot show that §8.6 works. That composition is GAME-LAYER, it is not
//    part of Phase 6's proven surface, and no rig covers it.
//
// The world itself is real: Phase4Bootstrapper does the actual Phase 3
// generation and Phase 4 streaming. This file adds a player, some keys, one
// fluid arena and a HUD; it does not fake terrain.
//
// KEEPING THIS SCENE CURRENT IS PART OF FINISHING A PHASE. See
// PLAYGROUND_GUIDE.md -- when a phase's files land, they get woven in here and
// the guide is updated in the same pass. A phase whose work you cannot touch in
// the Playground is a phase nobody can feel.

using System;
using System.Collections;
using System.Collections.Generic;
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

    [Header("Phase 6")]
    [Tooltip("Left empty, one is found in the scene. Without it the scene is fly-only.")]
    [SerializeField] private PlayerController _player;
    [Tooltip("Radius in voxels of the B-key demo blast. §13's 400K reference is radius 46.")]
    [SerializeField] private int _bombRadiusVoxels = 20;

    [Tooltip("§7.4's active radius, in voxels, FOR THIS SCENE ONLY. The shipped engine " +
             "constant is FLUID_ACTIVE_RADIUS_VOXELS = 1280 (128 m), which is 23x this " +
             "arena's half-diagonal -- so at the real value the radius never bites here and " +
             "§7.4 would be correct but invisible. 128 voxels (12.8 m) keeps you comfortably " +
             "inside it while working, and lets you walk away and watch fluid sleep.")]
    [SerializeField] private int _activeRadiusVoxels = 128;

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

    // ---- Phase 6 systems, all driven from here ----
    private SweptCCD _ccd;
    private ProjectileTrace _projectiles;
    private DestructionReducer _demolition;
    private Buoyancy _buoyancy;

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

    // =====================================================================
    // HOTBAR
    //
    // SLOTS 0-3 MUST STAY water / sand / lava / stone. Phase6BrushGuard drives
    // this scene by slot INDEX (its BrushWater/BrushSand/BrushLava/BrushStone
    // constants are 0/1/2/3) and asserts on what each places. Appending is
    // safe; reordering silently changes what that rig tests.
    // =====================================================================
    private static readonly byte[] _brushes =
        { Materials.Water, Materials.Sand, Materials.Lava, Materials.Stone,
          Materials.Sandstone, Materials.Snow };
    private static readonly string[] _brushNames =
        { "water", "sand", "lava", "stone", "sandstone", "snow" };
    private int _brush;

    // ---- Movement mode ----
    private enum MoveMode { Walk, Fly }
    private MoveMode _mode = MoveMode.Walk;

    // ---- Tools (§8.3 tiers) ----
    private int _tier = 1;                       // hand / drill / bore
    private EditService.ToolBudget _digBudget;
    private int _dugThisSecond, _dugCounter;
    private float _dugTimer;

    // ---- Last action, for the HUD's DIG/PLACE indicator ----
    private enum Act { None, Dig, Place, Refused }
    private Act _act;
    private float _actAge = 99f;

    // ---- Phase 6 demo state, surfaced in the HUD ----
    private BuoyancyState _buoyState;
    private ProjectileHit _lastShot;
    private bool _hasShot;
    private float _shotAge = 99f;
    private ProxyDrop _lastDrop;
    private bool _hasDrop;
    private int _bombFrames;

    private PlaygroundFlyCamera _cam;
    private Texture2D _px;
    private bool _showHelp = true;

    // ---- Additive feature registration (header requirement e) ----
    public readonly struct Toy
    {
        public readonly KeyCode Key;
        public readonly string Label;
        public readonly Action Run;
        public Toy(KeyCode k, string label, Action run) { Key = k; Label = label; Run = run; }
    }
    private readonly List<Toy> _toys = new List<Toy>();
    public void Register(Toy toy) => _toys.Add(toy);

    // =====================================================================

    IEnumerator Start()
    {
        while (Phase4Bootstrapper.Store == null || Phase4Bootstrapper.Clipmap == null)
            yield return null;
        for (int i = 0; i < 30; i++) yield return null;   // let the window fill

        _store = Phase4Bootstrapper.Store;
        _clipmap = Phase4Bootstrapper.Clipmap;
        _pool = Phase4Bootstrapper.Pool;

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
            ActiveRadiusVoxels = _activeRadiusVoxels,
            SleepRadiusVoxels = FluidActiveRegion.SleepRadiusFor(_activeRadiusVoxels),
        };
        _readback = new FluidOpListReadback(_fluid, _store) { OnVoxelApplied = MarkDirtyFor };

        // §8.3's edit path, not the hand-rolled trio. EVERY edit in this scene
        // now goes through EditService: it marks the mirror dirty, runs §7.6's
        // wake scan, and REFUSES writes to unloaded chunks instead of dropping
        // them silently. The old code did SetVoxel + MarkDirty + NotifyEdited by
        // hand at four call sites, which is what EditService exists to end.
        _edits = new EditService();
        _edits.AttachWorld(_store, _store, _store, _clipmap);
        _edits.AttachFluidSimulation(_fluid, _store);

        _ccd = new SweptCCD(_store, _store);
        _projectiles = new ProjectileTrace(_store, _store);
        _demolition = new DestructionReducer(_edits, _store);
        _buoyancy = new Buoyancy(_store, _store);

        _waterSrc = new int3(_arenaCentre.x - 6, _arenaOrigin.y + _arenaEdge - 4, _arenaCentre.z);
        _sandSrc  = new int3(_arenaCentre.x + 8, _arenaOrigin.y + _arenaEdge - 4, _arenaCentre.z - 6);
        _lavaSrc  = new int3(_arenaCentre.x + 2, _arenaOrigin.y + _arenaEdge - 4, _arenaCentre.z + 7);

        // The player. Playground owns its clock and its input so mouse-look
        // lives in one place (the flycam) whichever mode is active.
        if (_player == null) _player = FindObjectOfType<PlayerController>();
        if (_player != null)
        {
            _player.DebugTakeControl();
            _player.Bind(_store, _store);
        }
        else
        {
            _mode = MoveMode.Fly;
            Debug.LogWarning("[Playground] no PlayerController in the scene — fly mode only");
        }

        // A CAPTURE PASS OWNS THE CAMERA, SO THE PLAYER MUST NOT.
        // PlaygroundCapture (-playgroundshots / -gputrace) positions the camera
        // itself for each shot; in walk mode DebugStep rewrites the camera every
        // frame from the motor, and the two fight -- the symptom is screenshots
        // taken from wherever the player happened to be standing. Fly mode
        // leaves the transform alone, which is what those passes expect.
        if (HasArg("-playgroundshots") || HasArg("-gputrace"))
        {
            _mode = MoveMode.Fly;
            _showHelp = false;
        }

        RegisterDefaultToys();
        _ready = true;
        TeleportToArena();
        _status = $"arena at {_arenaCentre} — fluid is FIXED here and does not follow you";
    }

    private static bool HasArg(string flag)
    {
        foreach (string a in Environment.GetCommandLineArgs())
            if (string.Equals(a, flag, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private void RegisterDefaultToys()
    {
        Register(new Toy(KeyCode.Tab, "walk/fly", ToggleMode));
        Register(new Toy(KeyCode.V, "vent here", OpenVentAtTarget));
        Register(new Toy(KeyCode.Alpha0, "stop vents", () =>
            { _waterLeft = _sandLeft = _lavaLeft = 0; _status = "vents closed"; }));
        Register(new Toy(KeyCode.F, "go to arena", TeleportToArena));
        Register(new Toy(KeyCode.R, "respawn on ground", Respawn));
        Register(new Toy(KeyCode.B, "bomb", DetonateAtTarget));
        Register(new Toy(KeyCode.T, "shoot", ShootProjectile));
        Register(new Toy(KeyCode.Z, "tool -", () => CycleTier(-1)));
        Register(new Toy(KeyCode.X, "tool +", () => CycleTier(+1)));
        Register(new Toy(KeyCode.F2, "help", () => _showHelp = !_showHelp));
    }

    // =====================================================================
    // Modes, tools, hotbar
    // =====================================================================

    private void ToggleMode()
    {
        if (_player == null) { _status = "no PlayerController — fly only"; return; }
        _mode = _mode == MoveMode.Walk ? MoveMode.Fly : MoveMode.Walk;
        if (_mode == MoveMode.Walk) SnapPlayerToCamera();
        _status = _mode == MoveMode.Walk
            ? "WALK — gravity, jumping, 3-voxel steps, swimming"
            : "FLY — noclip camera, gravity off";
    }

    /// Entering walk mode from wherever the camera drifted to. Uses the motor's
    /// own spawn resolution so you never materialise inside rock.
    private void SnapPlayerToCamera()
    {
        Camera cam = Camera.main;
        if (cam == null || _player == null || _player.Motor == null) return;
        float eye = 1.6f;
        Vector3 p = cam.transform.position - Vector3.up * eye;
        _player.Motor.Teleport(new float3(p.x, p.y, p.z));
        if (!_player.Motor.ResolveSpawn())
            _status = "could not find open space here — try F or R";
    }

    private void Respawn()
    {
        if (_player == null || _player.Motor == null) return;
        int3 c = CameraVoxel();
        int sy = SurfaceY(c.x, c.z);
        if (sy < 0) { _status = "no ground under you to respawn onto"; return; }
        _player.Motor.Teleport(new float3(c.x * 0.1f, (sy + 1) * 0.1f + 0.2f, c.z * 0.1f));
        _player.Motor.ResolveSpawn();
        _mode = MoveMode.Walk;
        _status = "respawned on the surface";
    }

    private void CycleTier(int d)
    {
        _tier = (int)Mathf.Repeat(_tier + d, EditService.Tiers.Length);
        var t = EditService.Tiers[_tier];
        _status = $"tool: {t.Name} — {t.VoxelsPerSecond} vox/s, radius {t.RadiusVoxels}";
    }

    private void SetBrush(int i)
    {
        _brush = Mathf.Clamp(i, 0, _brushes.Length - 1);
        _status = $"holding {_brushNames[_brush]}";
    }

    // =====================================================================
    // Phase 6 demo actions
    // =====================================================================

    /// §8.5's mass destruction, at the crosshair. Frame-split: Update drains it
    /// under the per-frame work budget, exactly as a real detonation would be.
    private void DetonateAtTarget()
    {
        if (!_hasTarget) { _status = "bomb: aim at something first"; return; }
        if (_demolition.InProgress) { _status = "bomb: one is still going off"; return; }
        int cells = DestructionReducer.SphereVoxelCount(_bombRadiusVoxels);
        _demolition.Detonate(_targetVoxel, _bombRadiusVoxels);
        _bombFrames = 0;
        _status = $"bomb: radius {_bombRadiusVoxels} ({cells} cells) — draining under the frame budget";
    }

    /// §8.4's projectile, fired down the crosshair. The trace is authoritative:
    /// §13's solo-dev note says provisional motion stays visual until a trace
    /// confirms, so nothing here moves anything before the answer comes back.
    private void ShootProjectile()
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        float3 from = new float3(cam.transform.position.x, cam.transform.position.y,
                                 cam.transform.position.z);
        float3 dir = new float3(cam.transform.forward.x, cam.transform.forward.y,
                                cam.transform.forward.z);
        _lastShot = _projectiles.Trace(from, from + dir * 60f);
        _hasShot = true;
        _shotAge = 0f;
        _status = _lastShot.Hit
            ? $"shot hit {MaterialName(_lastShot.Material)} at {_lastShot.DistanceM:F1} m" +
              (_lastShot.FluidTraversedDistanceM > 0f
                  ? $" (through {_lastShot.FluidTraversedDistanceM:F2} m of {MaterialName(_lastShot.PrimaryFluidMaterial)})"
                  : "")
            : $"shot travelled {_lastShot.DistanceM:F1} m and hit nothing";
    }

    /// Opens a continuous source in the air above whatever the crosshair is on.
    private void OpenVentAtTarget()
    {
        if (!_hasTarget) { _status = "vent: aim at a surface first"; return; }
        int3 cell = new int3(_targetVoxel.x, _targetVoxel.y + 18, _targetVoxel.z);
        if (!_fluid.InRegion(cell)) { _status = "vent: outside the fluid arena (press F)"; return; }
        byte m = _brushes[_brush];
        if (m == Materials.Water) { _waterSrc = cell; _waterLeft = _waterBudget; }
        else if (m == Materials.Sand) { _sandSrc = cell; _sandLeft = _sandBudget; }
        else if (m == Materials.Lava) { _lavaSrc = cell; _lavaLeft = _lavaBudget; }
        else { _status = "vent: hold water, sand or lava (1/2/3)"; return; }
        _status = $"{_brushNames[_brush]} vent open above {_targetVoxel}";
    }

    // =====================================================================
    // World helpers
    // =====================================================================

    private bool TryFindNaturalBasin(out int3 centre)
    {
        centre = default;
        int bestY = int.MaxValue;
        bool found = false;
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
            if (_store.GetVoxel(new int3(x, surface, z)) == Materials.Water) continue;
            if (surface < bestY) { bestY = surface; centre = new int3(x, surface, z); found = true; }
        }
        return found;
    }

    private int SurfaceY(int x, int z)
    {
        for (int y = WorldGenConstants.MAX_TERRAIN_HEIGHT + 2; y >= 1; y--)
        {
            byte m = _store.GetVoxel(new int3(x, y, z));
            if (m != Materials.Air && !MaterialRules.IsFluidMaterial(m)) return y;
        }
        return -1;
    }

    private void MarkDirtyFor(int3 v) => _clipmap.MarkDirty(CoordMath.VoxelToChunk(v));

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
        Vector3 at = new Vector3(_arenaCentre.x * 0.1f - 3.2f,
                                 _arenaCentre.y * 0.1f + 2.6f,
                                 _arenaCentre.z * 0.1f - 3.2f);
        cam.transform.position = at;
        cam.transform.rotation = Quaternion.Euler(18f, 45f, 0f);
        if (_mode == MoveMode.Walk) SnapPlayerToCamera();
        _status = "went to the fluid arena";
    }

    /// Voxel DDA from the camera, stopping at the first solid cell.
    private void UpdateTarget()
    {
        _hasTarget = false;
        Camera cam = Camera.main;
        if (cam == null || _store == null) return;

        float3 originM = new float3(cam.transform.position.x, cam.transform.position.y, cam.transform.position.z);
        float3 dir = math.normalize(new float3(cam.transform.forward.x, cam.transform.forward.y, cam.transform.forward.z));

        const float reachM = 12f;
        const float stepM = 0.05f;
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
                _targetAdjacent = prev;
                _targetMaterial = m;
                _targetDistM = t;
                return;
            }
            prev = v;
        }
    }

    // =====================================================================
    // Frame
    // =====================================================================

    void Update()
    {
        if (!_ready) return;
        if (_cam == null && Camera.main != null) _cam = Camera.main.GetComponent<PlaygroundFlyCamera>();

        float dt = Time.deltaTime;
        _actAge += dt;
        _shotAge += dt;

        // The flycam always owns capture + look; only its MOVEMENT is mode-gated.
        if (_cam != null) _cam.MovementEnabled = _mode == MoveMode.Fly;

        UpdateTarget();
        foreach (var t in _toys) if (Input.GetKeyDown(t.Key)) t.Run();
        HandleHotbarKeys();
        HandleMouse(dt);
        DriveWalk(dt);

        Emit(ref _waterLeft, _waterSrc, Materials.Water);
        Emit(ref _sandLeft, _sandSrc, Materials.Sand);
        Emit(ref _lavaLeft, _lavaSrc, Materials.Lava);

        // §8.5's frame-split drain. One Step per frame is exactly how a real
        // detonation recovers, and it is why a 400K blast does not stall a frame.
        if (_demolition.InProgress)
        {
            _demolition.Step();
            _bombFrames++;
            if (!_demolition.InProgress && _demolition.TryTakeCompleted(out _lastDrop))
            {
                _hasDrop = true;
                _status = $"bomb: {_lastDrop.TotalVoxels} voxels in {_bombFrames} frames, " +
                          $"mostly {MaterialName(_lastDrop.DominantMaterial)} — one Proxy Drop";
            }
        }

        // Dug-per-second readout, so the tool tiers are visible rather than
        // asserted. Reset on a whole-second boundary.
        _dugTimer += dt;
        if (_dugTimer >= 1f) { _dugThisSecond = _dugCounter; _dugCounter = 0; _dugTimer = 0f; }

        // §7.4: MOVE THE ACTIVE CENTRE WITH THE PLAYER. Nothing did this before
        // -- PlayerVoxel was set once at construction, so the GPU's radius test
        // (which has always existed) was anchored to the arena centre forever.
        // UpdatePlayerPosition applies the re-centre threshold itself, so
        // calling it every frame is cheap and does not churn the boundary.
        _fluid.UpdatePlayerPosition(PlayerOrCameraVoxel());

        if (_readback.CanIssue)
        {
            _fluid.Tick(_clipmap);
            _readback.IssueReadback(0);
        }
        _readback.PumpAndApply();
    }

    /// Where §7.4's radius is centred: the player's feet when walking, the
    /// camera when flying.
    private int3 PlayerOrCameraVoxel()
    {
        if (_mode == MoveMode.Walk && _player != null && _player.Motor != null)
            return CoordMath.WorldToVoxel(_player.Motor.PositionM);
        Camera cam = Camera.main;
        if (cam == null) return _arenaCentre;
        return CoordMath.WorldToVoxel(new float3(cam.transform.position.x,
                                                 cam.transform.position.y,
                                                 cam.transform.position.z));
    }

    private void HandleHotbarKeys()
    {
        for (int i = 0; i < _brushes.Length && i < 9; i++)
            if (Input.GetKeyDown(KeyCode.Alpha1 + i)) SetBrush(i);
    }

    /// Walks the player, then lets §8.6's buoyancy act on the result.
    private void DriveWalk(float dt)
    {
        if (_mode != MoveMode.Walk || _player == null || !_player.Ready || dt <= 0f) return;

        float yaw = _cam != null ? _cam.Yaw : 0f;
        _player.DebugSetLook(yaw, _cam != null ? _cam.Pitch : 0f);

        float2 wish = float2.zero;
        if (_cam == null || _cam.Captured)
        {
            if (Input.GetKey(KeyCode.W)) wish.y += 1f;
            if (Input.GetKey(KeyCode.S)) wish.y -= 1f;
            if (Input.GetKey(KeyCode.D)) wish.x += 1f;
            if (Input.GetKey(KeyCode.A)) wish.x -= 1f;
        }
        float r = math.radians(yaw);
        float sin = math.sin(r), cos = math.cos(r);
        float2 world = new float2(wish.x * cos + wish.y * sin, wish.y * cos - wish.x * sin);

        bool jump = (_cam == null || _cam.Captured) && Input.GetKeyDown(KeyCode.Space);
        _player.DebugStep(dt, world, jump);

        ApplyBuoyancy(dt);
    }

    /// SWIMMING, COMPOSED IN THE GAME LAYER. See header note 4.
    ///
    /// §8.6's Buoyancy is one-way: it samples terrain and returns forces, and
    /// PlayerMotor deliberately knows nothing about them. Something has to join
    /// the two for a body to actually float, and that something is game code --
    /// this. It is NOT part of Phase 6's proven surface and no rig covers it;
    /// it exists so the demo can show that §8.6 produces sensible numbers.
    private void ApplyBuoyancy(float dt)
    {
        var motor = _player.Motor;
        if (motor == null) return;

        _buoyState = _buoyancy.Sample(motor.PositionM, PlayerConfig.Active.bodyHeightM,
                                      PlayerConfig.Active.maxSpeedMps,
                                      _readback != null ? _readback.FramesSinceLastApplied : 0);
        if (!_buoyState.InFluid) return;

        motor.VelocityMps.y += _buoyState.BuoyantAccelMps2 * dt;

        // Linear drag, applied as an exponential decay so a large coefficient
        // cannot overshoot into a reversal at low frame rates.
        float k = math.exp(-_buoyState.DragPerSecond * dt);
        motor.VelocityMps *= k;
    }

    /// Mouse actions only fire while the camera has the cursor captured, so the
    /// click that re-focuses the window cannot also dig a hole.
    private void HandleMouse(float dt)
    {
        if (_cam == null || !_cam.Captured) return;

        float scroll = Input.mouseScrollDelta.y;
        if (Mathf.Abs(scroll) > 0.01f)
            SetBrush((int)Mathf.Repeat(_brush + (scroll > 0 ? 1 : -1), _brushes.Length));

        if (!_hasTarget) return;

        if (Input.GetMouseButton(0))          // held: dig at the tool's own rate
        {
            var tier = EditService.Tiers[_tier];
            int allow = _digBudget.Accrue(dt, tier.VoxelsPerSecond);
            if (allow > 0)
            {
                int got = _edits.MineSphereBudgeted(_targetVoxel, tier.RadiusVoxels,
                                                    allow, Materials.Air);
                _dugCounter += got;
                _act = Act.Dig;
                _actAge = 0f;
                _status = $"digging with {tier.Name} at {_targetVoxel}";
            }
        }
        else if (Input.GetMouseButton(1))     // held: place the held item
        {
            TryPaintBrush(_targetAdjacent);
        }
    }

    /// THE PLACE ACTION. Returns true if the blob was actually written.
    ///
    /// Factored out of HandleMouse so a rig can drive the REAL path without
    /// synthesising mouse input -- HandleMouse and Phase6BrushGuard are the only
    /// two callers, and they share this one implementation, so a rig result is a
    /// statement about what right-click does, not about a parallel copy of it.
    internal bool TryPaintBrush(int3 at)
    {
        byte m = _brushes[_brush];
        int radius = MaterialRules.IsMobile(m) ? 1 : 2;   // a small blob of fluid

        // REFUSE A MOBILE BRUSH OUTSIDE THE ARENA. The CA's region is fixed
        // (§7.4's moving radius is unbuilt), and FluidGpuSimulation.RequestWake
        // correctly drops out-of-region wakes -- so a mobile material written
        // outside the arena is committed to ChunkStore and uploaded to the
        // mirror, and then NEVER SIMULATED. It renders as a frozen blob hanging
        // wherever the crosshair was: sand that does not fall, water that does
        // not spread. Header note 1 says this scene must make that limit VISIBLE.
        //
        // All-or-nothing on purpose. Placing only the in-region cells of a blob
        // that straddles the edge would leave a frozen rim outside it -- the
        // same silent-partial shape as the §9.4 residency-edge bug.
        if (MaterialRules.IsMobile(m) && !SphereFitsInFluidArena(at, radius))
        {
            _status = $"<color=#ff9a9a>{_brushNames[_brush]} NOT placed at {at} — outside the " +
                      $"{_arenaEdge}^3 fluid arena at {_arenaCentre}. It would never simulate here " +
                      $"(§7.4's moving radius is unbuilt). Press F to go to the arena.</color>";
            _act = Act.Refused;
            _actAge = 0f;
            return false;
        }

        _edits.SetSphere(at, radius, m);
        _act = Act.Place;
        _actAge = 0f;
        _status = $"placing {_brushNames[_brush]} at {at}";
        return true;
    }

    /// True iff a blob placed here would actually be simulated. The predicate
    /// lives in FluidGpuSimulation next to the region it asks about.
    private bool SphereFitsInFluidArena(int3 centre, int radius)
        => _fluid != null && _fluid.SphereFitsInRegion(centre, radius);

    private void Emit(ref int budget, int3 cell, byte material)
    {
        if (budget <= 0) return;
        if (_store.GetVoxel(cell) != Materials.Air) return;
        if (_edits.TrySetVoxel(cell, material)) budget--;
    }

    // =====================================================================
    // Rig surface. Phase6BrushGuard / PlaygroundCapture only.
    // =====================================================================

    internal bool DebugReady => _ready;
    internal int3 DebugArenaCentre => _arenaCentre;
    internal int3 DebugArenaOrigin => _arenaOrigin;
    internal int DebugArenaEdge => _arenaEdge;
    internal string DebugStatus => _status;
    internal FluidGpuSimulation DebugFluid => _fluid;
    internal ChunkStore DebugStore => _store;
    internal byte DebugBrushMaterial => _brushes[_brush];
    internal string DebugBrushName => _brushNames[_brush];
    internal void DebugSetBrush(int i) => SetBrush(i);

    internal void DebugTickFluid()
    {
        if (_readback.CanIssue)
        {
            _fluid.Tick(_clipmap);
            _readback.IssueReadback(0);
        }
        _readback.PumpAndApply();
    }

    /// For PlaygroundCapture only. Opens all three vents above the arena centre.
    public void DebugOpenAllVents()
    {
        int top = _arenaOrigin.y + _arenaEdge - 4;
        _waterSrc = new int3(_arenaCentre.x - 6, top, _arenaCentre.z);
        _sandSrc  = new int3(_arenaCentre.x + 8, top, _arenaCentre.z - 6);
        _lavaSrc  = new int3(_arenaCentre.x + 2, top, _arenaCentre.z + 7);
        _waterLeft = _waterBudget; _sandLeft = _sandBudget; _lavaLeft = _lavaBudget;
        _status = "capture: all vents open";
    }

    /// For PlaygroundCapture only -- triggers a toy without duplicating the key
    /// table. Not part of the playable surface.
    public void DebugRunKey(KeyCode k)
    {
        foreach (var t in _toys) if (t.Key == k) { t.Run(); return; }
    }

    // =====================================================================
    // Screen-space drawing
    // =====================================================================

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
            if (sp.z <= 0f) return;
            p[i] = new Vector2(sp.x, Screen.height - sp.y);
        }

        int[,] edges = {
            {0,1},{1,3},{3,2},{2,0},
            {4,5},{5,7},{7,6},{6,4},
            {0,4},{1,5},{2,6},{3,7},
        };
        for (int e = 0; e < 12; e++)
            DrawLine(p[edges[e, 0]], p[edges[e, 1]], c, width);
    }

    private static Color MaterialSwatch(byte m)
    {
        var rgb = MaterialPalette.Of(m);
        return new Color(rgb.R, rgb.G, rgb.B, 1f);
    }

    private void Box(Rect r, Color fill)
    {
        if (_px == null) { _px = new Texture2D(1, 1); _px.SetPixel(0, 0, Color.white); _px.Apply(); }
        Color old = GUI.color;
        GUI.color = fill;
        GUI.DrawTexture(r, _px);
        GUI.color = old;
    }

    private void Frame(Rect r, Color c, float w)
    {
        Box(new Rect(r.x, r.y, r.width, w), c);
        Box(new Rect(r.x, r.yMax - w, r.width, w), c);
        Box(new Rect(r.x, r.y, w, r.height), c);
        Box(new Rect(r.xMax - w, r.y, w, r.height), c);
    }

    // =====================================================================
    // HUD
    // =====================================================================

    void OnGUI()
    {
        float ui = Mathf.Max(1f, Screen.height / 900f);
        Camera cam = Camera.main;

        // ---- World-space overlays, in REAL screen pixels ----
        if (cam != null)
        {
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
                DrawVoxelHighlight(cam, _targetVoxel, new Color(0f, 0f, 0f, 0.55f), 4.5f * k);
                DrawVoxelHighlight(cam, _targetVoxel,
                    inRegion ? new Color(1f, 0.78f, 0.25f, 0.98f)
                             : new Color(0.85f, 0.85f, 0.88f, 0.9f), 2f * k);
            }

            // The last shot's impact, fading. Makes T do something visible.
            if (_ready && _hasShot && _lastShot.Hit && _shotAge < 2f)
            {
                float a = 1f - _shotAge / 2f;
                DrawVoxelHighlight(cam, _lastShot.Voxel, new Color(1f, 0.35f, 0.2f, a), 3f * k);
            }
        }

        if (!_ready)
        {
            DrawTextPanel(new Rect(12, 8, 900, 40), ui, _status);
            return;
        }

        Matrix4x4 prev = GUI.matrix;
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one * ui);
        float vw = Screen.width / ui, vh = Screen.height / ui;

        DrawTopBar(vw);
        DrawHotbar(vw, vh);
        DrawStatePanel(vh);
        if (_showHelp) DrawHelp(vw, vh);

        GUI.matrix = prev;
    }

    private GUIStyle _label;
    private GUIStyle Label(int size, TextAnchor anchor = TextAnchor.UpperLeft)
    {
        _label = new GUIStyle(GUI.skin.label)
        {
            fontSize = size,
            richText = true,
            alignment = anchor,
            wordWrap = false,
            normal = { textColor = Color.white },
        };
        return _label;
    }

    private void DrawTextPanel(Rect r, float ui, string text)
    {
        Matrix4x4 prev = GUI.matrix;
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one * ui);
        GUI.Label(r, text, Label(14));
        GUI.matrix = prev;
    }

    private void DrawTopBar(float vw)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<b>Playground — dogfood scene. Not a diagnostic scene, not a scale test.</b>");
        if (_cam != null && !_cam.Captured)
            sb.AppendLine("<color=#ffc64a><b>CLICK the window to capture the mouse</b></color>   (ESC releases)");
        else
            sb.AppendLine($"<b>{(_mode == MoveMode.Walk ? "WALK" : "FLY")}</b>   " +
                          "<b>Tab</b> switch   <b>F2</b> controls   <b>F1</b> perf");
        sb.Append(_status);

        // Width is bounded so it cannot slide under PlaygroundHud's perf panel,
        // which anchors itself to the right edge at vw - 648.
        float w = Mathf.Min(760f, vw - 660f);
        Box(new Rect(8, 6, w, 66), new Color(0f, 0f, 0f, 0.45f));
        GUI.Label(new Rect(16, 10, w - 14, 62), sb.ToString(), Label(14));
    }

    /// The hotbar plus what the two mouse buttons currently do. This is the
    /// "what am I holding / what will clicking do" question, answered without
    /// reading a paragraph.
    private void DrawHotbar(float vw, float vh)
    {
        const float slot = 54f, pad = 6f;
        float total = _brushes.Length * (slot + pad) - pad;
        float x0 = (vw - total) * 0.5f;
        float y0 = vh - slot - 58f;

        for (int i = 0; i < _brushes.Length; i++)
        {
            var r = new Rect(x0 + i * (slot + pad), y0, slot, slot);
            bool sel = i == _brush;

            Box(r, new Color(0f, 0f, 0f, sel ? 0.75f : 0.45f));
            Box(new Rect(r.x + 6, r.y + 6, r.width - 12, r.height - 22), MaterialSwatch(_brushes[i]));
            Frame(r, sel ? new Color(1f, 0.82f, 0.3f, 1f) : new Color(1f, 1f, 1f, 0.25f), sel ? 2.5f : 1f);

            GUI.Label(new Rect(r.x + 5, r.y + 2, 20, 16), $"{i + 1}", Label(11));
            GUI.Label(new Rect(r.x, r.yMax - 17, r.width, 16), _brushNames[i],
                      Label(10, TextAnchor.MiddleCenter));
        }

        // What the mouse buttons do, and which fired most recently.
        var tier = EditService.Tiers[_tier];
        bool digHot = _act == Act.Dig && _actAge < 0.35f;
        bool placeHot = (_act == Act.Place || _act == Act.Refused) && _actAge < 0.35f;
        string refused = _act == Act.Refused && _actAge < 0.6f ? "  <color=#ff9a9a>REFUSED</color>" : "";

        var lr = new Rect(x0 - 250, y0 + 8, 240, 40);
        var rr = new Rect(x0 + total + 10, y0 + 8, 260, 40);
        Box(lr, new Color(0f, 0f, 0f, digHot ? 0.75f : 0.4f));
        Box(rr, new Color(0f, 0f, 0f, placeHot ? 0.75f : 0.4f));
        if (digHot) Frame(lr, new Color(1f, 0.82f, 0.3f, 1f), 2f);
        if (placeHot) Frame(rr, new Color(1f, 0.82f, 0.3f, 1f), 2f);

        GUI.Label(lr, $"<b>LMB — DIG</b>\n{tier.Name} · {tier.VoxelsPerSecond} vox/s · r{tier.RadiusVoxels}   <b>Z/X</b>",
                  Label(12, TextAnchor.MiddleCenter));
        GUI.Label(rr, $"<b>RMB — PLACE</b>{refused}\n{_brushNames[_brush]}   <b>1-{_brushes.Length}</b> / scroll",
                  Label(12, TextAnchor.MiddleCenter));
    }

    /// Live engine state, bottom-left. Everything here is a READOUT of a Phase 6
    /// system, so "is buoyancy doing anything" is answerable at a glance.
    private void DrawStatePanel(float vh)
    {
        var sb = new StringBuilder();

        if (_mode == MoveMode.Walk && _player != null && _player.Motor != null)
        {
            var m = _player.Motor;
            float speed = math.length(new float2(m.VelocityMps.x, m.VelocityMps.z));
            sb.AppendLine($"<b>player</b>  {(m.Grounded ? "<color=#8fd98f>grounded</color>" : "airborne")}" +
                          $"   {speed:F1} m/s   y {m.PositionM.y:F2} m");
            if (m.LastBlockedByNonResident)
                sb.AppendLine("<color=#ff9a9a>blocked by an UNLOADED chunk (§9.4) — the streamer is behind</color>");
            if (_buoyState.InFluid)
                sb.AppendLine($"<b>buoyancy</b>  {_buoyState.SubmergedFraction * 100f:F0}% submerged in " +
                              $"{MaterialName(_buoyState.DominantFluid)}   " +
                              $"lift {_buoyState.BuoyantAccelMps2:+0.0;-0.0} m/s²   drag {_buoyState.DragPerSecond:F1}/s");
            else sb.AppendLine("<b>buoyancy</b>  dry");
            if (_buoyState.SpeedClampEngaged)
                sb.AppendLine($"<color=#ffc64a><b>§8.2 SPEED CLAMP ENGAGED</b> — op-list stale, limited to " +
                              $"{_buoyState.ClampedSpeedMps:F0} m/s</color>");
        }
        else sb.AppendLine("<b>fly</b>  noclip camera — press Tab to walk");

        sb.AppendLine($"<b>dug</b>  {_dugThisSecond} vox in the last second" +
                      (_demolition != null && _demolition.InProgress
                          ? $"   <color=#ffc64a>bomb draining… {_demolition.VoxelsRemovedSoFar} voxels</color>"
                          : ""));
        if (_hasDrop)
            sb.AppendLine($"<b>last blast</b>  {_lastDrop.TotalVoxels} voxels in {_lastDrop.Frames} frames, " +
                          $"mostly {MaterialName(_lastDrop.DominantMaterial)}");
        if (_hasShot)
            sb.AppendLine($"<b>last shot</b>  " + (_lastShot.Hit
                ? $"{MaterialName(_lastShot.Material)} at {_lastShot.DistanceM:F1} m"
                : $"no hit in {_lastShot.DistanceM:F0} m"));

        if (_edits != null)
            sb.AppendLine($"<b>edits</b>  {_edits.VoxelsWritten} written   {_edits.EditsNoOp} no-op   " +
                          $"{_edits.EditsRejectedNotResident} refused (not loaded)");

        int3 pv = PlayerOrCameraVoxel();
        int3 d = pv - _fluid.PlayerVoxel;
        long dist2 = (long)d.x * d.x + (long)d.y * d.y + (long)d.z * d.z;
        bool inRadius = FluidActiveRegion.WithinWakeRadius(_arenaCentre, pv, _activeRadiusVoxels);
        sb.AppendLine($"<b>fluid arena</b>  {_arenaEdge}³ at {_arenaCentre} — the REGION is fixed " +
                      "(§7.2 addressing); the ACTIVITY follows you");
        sb.Append($"<b>§7.4 radius</b>  {_activeRadiusVoxels}v wake / " +
                  $"{_fluid.SleepRadiusVoxels}v sleep   centre {_fluid.PlayerVoxel}   " +
                  $"re-centres {_fluid.RecentresTotal}   " +
                  (inRadius ? "<color=#8fd98f>arena INSIDE the radius — it ticks</color>"
                            : "<color=#ffc64a>arena OUTSIDE — fluid is asleep as static terrain</color>"));
        _ = dist2;

        // SITS ABOVE THE HOTBAR ROW. The hotbar's LMB panel starts at
        // (vw - 354)/2 - 250 and spans vh-104..vh-64, so a panel anchored at
        // vh-180 runs underneath it -- the first build had the arena line
        // disappearing behind the DIG box.
        // TALL ENOUGH FOR THE WORST CASE, which is walk mode with every optional
        // line present: player, non-resident block, buoyancy, speed clamp, dug,
        // last blast, last shot, edits, arena, §7.4 radius. At 13pt that is ~10
        // lines; 128px held 8 and silently clipped the last two, which are the
        // ones this session added.
        Box(new Rect(8, vh - 356, 760, 184), new Color(0f, 0f, 0f, 0.45f));
        GUI.Label(new Rect(16, vh - 352, 746, 178), sb.ToString(), Label(13));
    }

    private void DrawHelp(float vw, float vh)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<b>CONTROLS</b>   (F2 hides this)");
        sb.AppendLine("");
        sb.AppendLine("<b>move</b>      WASD, Space jump, Shift sprint   ·   fly: Q/E down/up");
        sb.AppendLine("<b>Tab</b>       walk ↔ fly            <b>R</b>  respawn on the surface");
        sb.AppendLine("<b>LMB</b>       dig at the tool's rate    <b>Z / X</b>  tool tier");
        sb.AppendLine("<b>RMB</b>       place held item           <b>1-6</b> / scroll  pick item");
        sb.AppendLine("<b>V</b>         open a vent above the crosshair (water/sand/lava)");
        sb.AppendLine("<b>0</b>         close all vents       <b>F</b>  go to the fluid arena");
        sb.AppendLine("<b>B</b>         bomb at the crosshair (§8.5, frame-split)");
        sb.AppendLine("<b>T</b>         shoot a projectile (§8.4 DDA trace)");
        sb.AppendLine("<b>F1</b>        perf overlay          <b>ESC</b>  release the mouse");
        sb.AppendLine("");
        sb.Append("<color=#bbbbbb>Fluid only simulates inside the arena — the crosshair box turns grey " +
                  "outside it, and placing water/sand/lava there is refused.</color>");

        // CENTRED, not tucked into a corner: at 6 hotbar slots the bottom-right
        // is already occupied by the RMB panel, and a controls list that
        // overlaps the thing it is describing is worse than useless. It is
        // toggled, so covering the view while open is intended.
        float w = 640f, h = 244f;
        var r = new Rect((vw - w) * 0.5f, (vh - h) * 0.5f, w, h);
        Box(r, new Color(0f, 0f, 0f, 0.82f));
        Frame(r, new Color(1f, 1f, 1f, 0.22f), 1f);
        GUI.Label(new Rect(r.x + 18, r.y + 12, w - 36, h - 24), sb.ToString(), Label(13));
    }

    void OnDestroy()
    {
        _readback?.Dispose();
        _fluid?.Dispose();
    }
}
