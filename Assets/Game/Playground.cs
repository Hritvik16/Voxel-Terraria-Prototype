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
// 1. WHAT BOUNDS FLUID HERE IS §7.4's WAKE RADIUS, NOT AN ARENA BOX.
//    This note used to open "THE FLUID ARENA IS FIXED" and describe the region
//    box as the thing that refuses placement. Under §7.2's tiled substrate that
//    is no longer what happens, and the guard was renamed to say so:
//    TryPaintBrush calls SphereFitsInWakeRadius, and refuses a mobile brush
//    that falls outside §7.4's radius around YOU. The radius follows the
//    player, so the bound moves; _arenaEdge now only sizes the startup basin
//    search.
//
//    The REASON for refusing is unchanged and still worth stating: a mobile
//    material written where it will never be simulated is drawn and then hangs
//    frozen in mid-air. That is the artifact this scene exists to make visible
//    rather than hide.
//
//    NOT GUARDED: the tile pool. Inside the radius with a full pool, placement
//    is accepted and the fluid still never moves -- the same frozen blob by a
//    different route, reproduced deliberately in FluidTileCapRig. The pool is
//    sized well above measured demand (1024 vs 603-779), so this is headroom
//    rather than a live defect; see FLUID_TILE_CAP_RESULTS.md.
//
//    WHAT IS NEW is §7.4's near-player active radius, which is now DRIVEN.
//    Playground calls UpdatePlayerPosition every frame, so fluid simulates only
//    within a radius of you and sleeps back to static terrain when you leave --
//    "distant water is a settled terrain byte that looks like water but does not
//    tick". Walk away from a pool and it stops moving; come back and it wakes.
//    That is §7.4 working, not fluid breaking.
//
//    The GPU always had this test; nothing updated the centre, so it was
//    anchored wherever the region was created.
//
//    2026-09-11: the radius is now the SHIPPED FLUID_ACTIVE_RADIUS_VOXELS
//    (1280 / 128 m), not a reduced demo value. Under the tiled substrate the
//    footprint no longer scales with it -- measured 264 MB identical at r=128,
//    640 and 1280 -- so the scene can finally run the real constant. THE COST
//    IS A DEMO: sleep/wake needs a 128 m flight to observe instead of 13 m.
//    Set _activeRadiusVoxels back to 128 in the inspector if you want the
//    quick version; nothing else depends on the value.
//
// 2. THE FLUID POPULATION USED TO BE DELIBERATELY TINY. THAT REASON EXPIRED.
//    This note read: "Source budgets are tens to low hundreds of voxels -- the
//    range Phase 5a/5b actually tested. §2.5's ~500,000 near-player active
//    target has NEVER been tested, and this scene is deliberately not where
//    that gets discovered."
//
//    It HAS been tested since, and well past that target: the late-game siege
//    holds ~320,000 live for 200s, and the MAX_ACTIVE_FLUID ladder ran to
//    1,500,000 live with 8/8 gates green and a 0.0% frozen-fluid census
//    (FLUID_TILE_CAP_RESULTS.md §13). The shipped ceiling is now 750,000.
//    Budgets were raised 2026-09-11 to match what the engine is known to
//    survive; this scene is no longer the place where scale would be
//    discovered, because it was discovered on the rigs first.
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

public partial class Playground : MonoBehaviour
{
    [SerializeField] private ComputeShader _fluidCA;
    [Tooltip("Arena edge in voxels. Power of two -- the CA addresses its region with shifts.")]
    [SerializeField] private int _arenaEdge = 64;
    [Tooltip("DEMO TUNING, raised 2026-09-11 from 8192. The shipped ceiling is " +
             "MAX_ACTIVE_FLUID = 750,000 and it has been measured to 1.5M live with every " +
             "gate green; 8192 was the Phase 5a/5b correctness range and showcased none of it.")]
    [SerializeField] private int _slotCapacity = EngineConfig.MAX_ACTIVE_FLUID;
    [SerializeField] private int _maxOpsPerFrame = 65536;
    [SerializeField] private string _outputRootFolderName = "PlaygroundShots";

    [Header("Phase 6")]
    [Tooltip("Left empty, one is found in the scene. Without it the scene is fly-only.")]
    [SerializeField] private PlayerController _player;
    [Tooltip("Radius in voxels of the B-key demo blast. §13's 400K reference IS radius 46, " +
             "which the late-game siege fires ten times a run inside a live fluid flood -- " +
             "so the demo now uses the reference figure instead of a third of it.")]
    [SerializeField] private int _bombRadiusVoxels = 46;

    // THE SHIPPED RADIUS, REFERENCED NOT RETYPED (corrected 2026-09-12).
    //
    // A UNIT MIXUP HAPPENED HERE TWICE AND THE FIELD IS NOW SHAPED TO PREVENT
    // A THIRD. Voxels are 0.1 m, so:
    //
    //     EngineConfig.FLUID_ACTIVE_RADIUS_VOXELS = 1280 voxels = 128 METRES
    //     the old demo value                      =  128 voxels =  12.8 metres
    //
    // Those two numbers share their digits and mean things a factor of TEN
    // apart. On 2026-09-12 a session "reverted" this field to 128 believing it
    // was restoring the shipped value, measured 65.5 -> 75.75 FPS, and
    // reported a performance win. The win was real arithmetic and a false
    // conclusion: it came from simulating a tenth of the radius, which is
    // exactly the fluid-scale compromise the project forbids.
    //
    // So this now REFERENCES the engine constant instead of restating it. A
    // literal here can silently drift from the spec; a reference cannot. If a
    // smaller radius is ever wanted for a quick sleep/wake demo, write it as
    // `EngineConfig.FLUID_ACTIVE_RADIUS_VOXELS / 10` so the relationship --
    // and the fact that it is a REDUCTION -- stays visible in the code.
    //
    // WHAT THE FULL RADIUS COSTS, measured and not hidden: §7.4's wake radius
    // decides how many §7.2 tiles stay resident, and the CA's three region
    // passes (CSClear / CSCommit / CSWakeScan) dispatch over
    // activeTileCount * 32768 cells every tick. At the shipped radius this
    // scene holds ~486 tiles resident. That cost is real and is tracked as
    // deferred work in FPS_INVESTIGATION_RESULTS.md §5.1 -- it is not a reason
    // to shrink the radius.
    [Tooltip("§7.4's active radius FOR THIS SCENE, in VOXELS. Voxels are 0.1 m, so the " +
             "shipped 1280 voxels is 128 METRES. Do not retype the number -- reference " +
             "EngineConfig.FLUID_ACTIVE_RADIUS_VOXELS, because 128 (voxels) and 1280 " +
             "(voxels) look alike and differ by 10x.")]
    [SerializeField] private int _activeRadiusVoxels = EngineConfig.FLUID_ACTIVE_RADIUS_VOXELS;

    // DEMO BUDGETS, raised 2026-09-11. See header note 2 for why they used to be
    // tiny and why that reason has expired.
    [SerializeField] private int _waterBudget = 60000;
    [SerializeField] private int _sandBudget = 20000;
    [SerializeField] private int _lavaBudget = 12000;
    [Tooltip("Edge of the cube a vent emits per frame. Was effectively 1 (one voxel per " +
             "frame per vent), which caps a vent at 60 voxels/second however large its " +
             "budget is -- raising the budget alone would just make the vents drip for " +
             "minutes. 5 gives up to 125 voxels/frame/vent.")]
    [SerializeField] private int _ventEdge = 5;
    [Tooltip("Half-width in voxels of the footprint a vent walks while emitting. A point " +
             "source chokes on its own output (Air-only placement) and only ever touches " +
             "one or two tiles; spreading the pour is what makes it both keep flowing and " +
             "exercise the tiled substrate.")]
    [SerializeField] private int _ventSpread = 40;

    private ChunkStore _store;
    private TerrainClipmap _clipmap;
    private BrickDataPool _pool;
    private FluidGpuSimulation _fluid;
    private FluidTileMap _tiles;
    private FluidOpListReadback _readback;
    private EditService _edits;

    // ---- Phase 6 systems, all driven from here ----
    private SweptCCD _ccd;
    private ProjectileTrace _projectiles;
    private DestructionReducer _demolition;
    private Buoyancy _buoyancy;

    [Tooltip("§7.2 tile pool cap, 0.5 MiB GPU per tile, allocated up front. Awake fluid " +
             "only (task 54), so 480 = 240 MiB fits plan §2's ~250 MB (task 55); the " +
             "footprint does not scale with the active radius.")]
    [SerializeField] private int _tilePoolCap = EngineConfig.PLAYGROUND_FLUID_TILE_POOL_CAPACITY;

    private int _residencyTick;
    private int3 _arenaOrigin, _arenaCentre;
    private int3 _waterSrc, _sandSrc, _lavaSrc;
    private int _waterLeft, _sandLeft, _lavaLeft;
    // §2.7: ONE extra vent for whichever other mobile material is selected
    // (oil, acid, sludge, quicksand, steam). Same emit path and budget style.
    private const int OtherVentBudget = 6000;
    private int3 _otherSrc;
    private int _otherLeft;
    private byte _otherMat;
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
    // TORCH IS APPENDED, NEVER INSERTED. The comment above this array says
    // reordering silently changes what Phase6BrushGuard tests, and that is still
    // true -- so Phase 7's brush goes on the end, where every existing index is
    // unchanged and slot 7 is simply new.
    private static readonly byte[] _brushes =
        { Materials.Water, Materials.Sand, Materials.Lava, Materials.Stone,
          Materials.Sandstone, Materials.Snow, Materials.Torch,
          Materials.TorchBlue, Materials.TorchGreen, Materials.TorchPurple,
          // §2.7's fluids (2026-09-25), APPENDED: every earlier index is
          // unchanged (Phase6BrushGuard keys off them). Scroll to reach them.
          Materials.Oil, Materials.Acid, Materials.Sludge, Materials.Quicksand, Materials.Steam };
    private static readonly string[] _brushNames =
        { "water", "sand", "lava", "stone", "sandstone", "snow", "torch",
          "blue torch", "green torch", "violet torch",
          "oil", "acid", "toxic sludge", "quicksand", "steam" };
    private int _brush;

    // =====================================================================
    // SURVIVAL (gameplay step 1, task 64; GAMEPLAY_PHASE_PLAN.md P1-P12)
    //
    // Survival: LMB digs into the inventory (SurvivalBuild.Dig), RMB places a
    // grid-snapped cube of the selected hotbar item (SurvivalBuild.Place), E
    // opens the 36-slot panel, Q cycles the cube size. Creative: the brushes
    // above, untouched. ONLY an interactive run can be Survival (the F6 menu's
    // saved "Game mode", SettingsMenu.LoadAndApply); every rig and capture run
    // never reads that file and so stays Creative -- Phase6BrushGuard still
    // drives _brushes by index.
    // =====================================================================
    internal static bool SurvivalMode;
    private bool _demoHideState;          // -survivaldemo: the dev readout panel out of the shots
    /// The E panel is open: it owns the mouse (PlaygroundFlyCamera releases it).
    internal static bool InventoryOpen { get; private set; }
    private Inventory _inv = Inventory.WithStarterKit();
    private readonly int[] _digScratch = new int[256];
    private int _edgeIndex = SurvivalBuild.DefaultEdgeIndex;
    private int _invPick = -1;            // the E panel's first clicked slot
    private float _placeRepeat;           // RMB held: one cube per PlaceRepeatS
    private const float PlaceRepeatS = 0.22f;
    private bool _invDirty;
    private float _invSaveTimer;
    private string _invPath;
    private int _lostTotal;

    internal Inventory DebugInventory => _inv;
    internal int DebugCubeEdge => SurvivalBuild.CubeEdges[_edgeIndex];

    /// Remembers k_soft across the hard/soft toggle so the owner's tuned value
    /// survives an A/B without a file round-trip.
    private static float _savedKSoft = 0.03f;

    /// The K key and the settings menu's "Shadow edges" (task 58): hard = k_soft
    /// 0 in the live config (the file is not written); soft restores the value
    /// it had. A LightingConfig.json reload brings the file's value back.
    internal static void SetSoftShadows(bool soft)
    {
        if (!soft)
        {
            if (LightingConfig.Active.kSoft > 0f) _savedKSoft = LightingConfig.Active.kSoft;
            LightingConfig.Active.kSoft = 0f;
        }
        else if (LightingConfig.Active.kSoft <= 0f)
            LightingConfig.Active.kSoft = _savedKSoft > 0f ? _savedKSoft : 0.03f;
    }

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

        // RENDER PRESET (RenderPreset.cs; owner, 2026-09-26: 60 FPS). Only an
        // INTERACTIVE run takes the default: every capture/rig mode keeps the
        // shipped path unless it asks (-renderpreset / -upscale), so the
        // historical comparisons stay like for like. Applied BEFORE the arena
        // search: a failed search yield-breaks out of Start, and the TerrainGen
        // world's first run lost its preset exactly that way.
        if (!IsCaptureRun()) RenderPreset.Apply(RenderPreset.FromArgs(RenderPreset.Default));
        else if (HasArg("-renderpreset")) RenderPreset.Apply(RenderPreset.FromArgs(RenderPreset.Kind.Quality));
        // -upscaler / -renderrows (task 47, UpscalerSelect.cs): when asked, in any mode.
        // Task 52 (owner Q38 (a), 2026-10-10): an INTERACTIVE run that names no
        // upscaler starts with FSR 3 dynamic, exactly as `-upscaler fsr3 -renderrows dyn`.
        // -renderpreset / -taa / -checker ask for the old frame path and keep it
        // (the -fluidprobe rig passes -renderpreset and is not a capture run).
        // Task 58: an interactive run (not a capture run, not -fluidprobe) takes
        // the settings menu's saved file instead, flags still winning per field
        // (SettingsMenu.LoadAndApply, below). Its defaults are exactly fsr3 dyn.
        bool playSettings = IsPlaySettingsRun();
        _moveAbilities = playSettings;
        if (!playSettings)
        {
            if (UpscalerSelect.FromArgs(out var upsKind, out int upsRows, out bool upsDyn)) UpscalerSelect.Apply(upsKind, upsRows, upsDyn);
            else if (!IsCaptureRun() && !HasArg("-renderpreset") && !HasArg("-taa") && !HasArg("-checker"))
                UpscalerSelect.Apply(UpscalerSelect.Kind.Fsr3, UpscalerSelect.MinRows, true);
        }
        // The minimap (owner, 2026-09-26: "always in the corner"). F4 hides it.
        if (GetComponent<Minimap>() == null) gameObject.AddComponent<Minimap>();
        // The graphics settings menu (task 58, F6): upscaler, rows, shadows, overlay, minimap.
        if (playSettings)
        {
            if (GetComponent<SettingsMenu>() == null) gameObject.AddComponent<SettingsMenu>();
            SettingsMenu.LoadAndApply();
        }
        // -seethrough 0|1 (task 75) outside play (capture runs, -fluidprobe): OFF unless asked.
        else if (TryArgFloat("-seethrough", out float seeArg)) RaymarchFeature.SeeThroughLiquids = seeArg >= 1f;
        // -playshot S (task 32b, run-play.sh SHOT=S): one screenshot of the
        // interactive session S seconds after startup. Not a capture mode --
        // the preset, HUD and controls stay exactly as a player gets them.
        if (!IsCaptureRun()) StartCoroutine(PlayShot());
        // -daylength S (task 33b, run-play.sh: 1200 = a 20-minute day). Only an
        // interactive run reads it; LightingConfig's own 120 s is left alone.
        if (!IsCaptureRun() && TryArgFloat("-daylength", out float dayLen) && dayLen >= 1f)
            SunCycle.DayLengthOverrideSeconds = dayLen;

        if (!TryFindNaturalBasin(out _arenaCentre))
        {
            _status = "no basin found near spawn — fluid arena not placed";
            yield break;
        }

        int half = _arenaEdge / 2;
        _arenaOrigin = new int3(_arenaCentre.x - half,
                                Mathf.Max(0, _arenaCentre.y - half),
                                _arenaCentre.z - half);

        // §7.2's TILED SUBSTRATE. Playground built the old DENSE simulation
        // until now -- it predated the tiled rewrite entirely, so the one
        // scene a human actually flies around in was the only thing still
        // exercising the path every rig had moved off.
        //
        // WHAT CHANGES FOR A PLAYER: the fluid "arena" is gone. There is no
        // box any more. Fluid lives wherever you put it, and what bounds it is
        // §7.4's ACTIVE RADIUS around you, not a region you can stand outside
        // of. _arenaEdge now only sizes the basin search and the HUD's old
        // arena marker; it no longer sizes any allocation.
        _tiles = new FluidTileMap(ChunkFluidMask.TILE_EDGE, new int3(128, 128, 128), _tilePoolCap)
        {
            // ENGINE PLAN STEP 5 (task 54): tiles only for AWAKE fluid. Resting
            // sea, lakes and supported sand are plain terrain until disturbed
            // (§7.4 "dormant = zero GPU slots"). "-awakeonly 0" is a MEASUREMENT
            // SEAM restoring the mask policy (every fluid tile in the radius).
            AwakeOnly = !(TryArgFloat("-awakeonly", out float awakeOnlyArg) && awakeOnlyArg == 0f),
        };
        _fluid = new FluidGpuSimulation(_fluidCA,
            new int3(64, 64, 64), _slotCapacity, _maxOpsPerFrame, _tiles)
        {
            // Under tiling the region box is only an addressing origin; the
            // tile pool is the active set. Zero, matching every proven rig.
            RegionOriginVoxels = int3.zero,
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

        // §6.6's enqueue, on the SAME EditService every other edit in this scene
        // already goes through. A torch is a terrain byte, so placing one is an
        // ordinary edit that happens to have a lighting consequence -- it is not
        // a second system with its own placement path (§0.1 invariant 1).
        var lighting = FindAnyObjectByType<VoxelEngine.Rendering.LightingDriver>();
        if (lighting != null) lighting.Attach(_edits);
        else Debug.LogWarning("[Playground] no LightingDriver in the scene -- torches will be " +
                              "placed as terrain but will not light anything. Regenerate the scene.");

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
        // A CAPTURE PASS OWNS THE CAMERA, SO THE PLAYER MUST NOT. -p7stress
        // ADDED 2026-09-13, after its first run produced ten unusable frames.
        //
        // This block's own comment already explained the failure exactly -- "in
        // walk mode DebugStep rewrites the camera every frame from the motor,
        // and the two fight; the symptom is screenshots taken from wherever the
        // player happened to be standing" -- and that is precisely what
        // happened: Stage D positioned the camera inside a cave and every shot
        // came back from the surface, with the F2 help panel open across the
        // middle of frame. The trap was documented; the new flag simply was not
        // added to the list. Any future capture mode must be added here too.
        if (HasArg("-playgroundshots") || HasArg("-gputrace") || HasArg("-p7stress") ||
            HasArg("-daysweep") || HasArg("-presetshots") ||
            HasArg("-sustained") || HasArg("-phase7demo") || HasArg("-roofskipab") ||
            HasArg("-upscaleab") || HasArg("-lavalight") || HasArg("-torchshadowshots") || HasArg("-upscalersmoke") || HasArg("-upscalercompare") || HasArg("-upscalerclips") || HasArg("-looksweep") ||
            HasArg("-farvisual") || HasArg("-farpreview") || HasArg("-flyfast") || HasArg("-farseams") || HasArg("-lodpop") || HasArg("-farshimmer") || HasArg("-nearshimmer") || HasArg("-fluidroster") ||
            HasArg("-devmap") || HasArg("-taarisk") || HasArg("-fluidprobe") || HasArg("-poolprobe") || HasArg("-streamprobe") || HasArg("-flysweep") || HasArg("-shadowdots"))
        {
            _mode = MoveMode.Fly;
            _showHelp = false;
        }

        // Screenshot passes also want the overlays OUT of frame. The Stage D
        // shots are meant to be looked at as pictures of the lighting; a perf
        // panel and a hotbar across them defeats the point.
        if (HasArg("-p7stress") || HasArg("-daysweep") || HasArg("-presetshots") ||
            HasArg("-phase7demo") || HasArg("-roofskipab") || HasArg("-upscaleab") ||
            HasArg("-lavalight") || HasArg("-torchshadowshots") || HasArg("-upscalersmoke") || HasArg("-upscalercompare") || HasArg("-upscalerclips") || HasArg("-looksweep") || HasArg("-farvisual") ||
            HasArg("-farpreview") || HasArg("-farseams") || HasArg("-lodpop") || HasArg("-farshimmer") || HasArg("-nearshimmer") || HasArg("-fluidroster") || HasArg("-taarisk") || HasArg("-streamprobe") || HasArg("-flysweep")) DebugHideUi = true;

        RegisterDefaultToys();
        // The dev map (O). Its own component: it owns the camera and the
        // keyboard while open, and Update/OnGUI below stand aside for it.
        if (GetComponent<DevMap>() == null) gameObject.AddComponent<DevMap>();
        LoadInventory();

        _ready = true;
        TeleportToArena();
        _status = $"arena at {_arenaCentre} — fluid is FIXED here and does not follow you";
        // TASK 32d: on tg2 the arena pose put the eye in a LAKE (the basin
        // search's water skip tests the voxel SurfaceY returns, which is solid
        // by construction, so the lowest lake bed wins) or in a hillside 3.2 m
        // off the basin. An interactive tg2 session stands on the nearest dry
        // column instead. Capture runs and the old world keep the arena pose.
        // TASK 33b: run-play.sh passes -startview, and only then does that
        // start also go to noon with a level camera (the arena's 18 deg
        // look-down under the start-up light was dim). The F key does not.
        if (Phase4Bootstrapper.IsTg2World && !IsCaptureRun()) StartCoroutine(SettleStartPose(HasArg("-startview")));
        // -survivaldemo (task 64): the step-1 screenshots, through the real dig/place paths, then quit.
        if (!IsCaptureRun() && HasArg("-survivaldemo")) StartCoroutine(SurvivalDemo());
        // -movedemo (task 65): movement set A's screenshots (dive, double jump over a gap), then quit.
        if (!IsCaptureRun() && HasArg("-movedemo")) StartCoroutine(MoveDemo());
        // -oredemo (task 66): ore screenshots (a tunnel wall, the hotbar, the dev map's close slice), then quit.
        if (!IsCaptureRun() && HasArg("-oredemo")) StartCoroutine(OreDemo());
        // -riverdemo (task 73): river/lake/waterfall/mouth screenshots for RIVERS_WATERFALLS_PLAN.md, then quit.
        // -riverlook 0|1 (task 76's river look; play: PlaySettings.riverLook, on) outside play: OFF unless asked.
        if (!playSettings && TryArgFloat("-riverlook", out float rlArg)) RaymarchFeature.RiverLook = rlArg >= 1f;
        if (!IsCaptureRun() && HasArg("-riverdemo")) StartCoroutine(RiverDemo());
        // -liquiddemo (task 75): see-through liquids off/on -- test pools of each fluid and a tg2 lake near/mid/far, then quit.
        if (!IsCaptureRun() && HasArg("-liquiddemo")) StartCoroutine(LiquidDemo());
    }

    /// For overlays (Minimap) that must stay out of capture frames.
    internal static bool IsCaptureRunForUi => IsCaptureRun();

    /// Task 58: the runs that read PlaySettings.json. Every run-*.sh that
    /// launches the Playground passes a capture flag except run-play.sh,
    /// run-demo.sh (both interactive) and run-tg2world.sh MODE=fluidprobe
    /// (-fluidprobe is a rig but deliberately not a capture run), excluded here.
    private static bool IsPlaySettingsRun() => !IsCaptureRun() && !HasArg("-fluidprobe");

    private static bool IsCaptureRun()
    {
        foreach (string a in Environment.GetCommandLineArgs())
            if (a.StartsWith("-") && (a == "-sustained" || a == "-playgroundshots" || a == "-gputrace" || a == "-p7stress" ||
                a == "-daysweep" || a == "-presetshots" || a == "-phase7demo" || a == "-roofskipab" || a == "-upscaleab" ||
                a == "-lavalight" || a == "-torchshadowshots" || a == "-upscalersmoke" || a == "-upscalercompare" || a == "-upscalerclips" || a == "-looksweep" || a == "-farvisual" || a == "-farpreview" || a == "-flyfast" ||
                a == "-farseams" || a == "-lodpop" || a == "-farshimmer" || a == "-nearshimmer" || a == "-fluidroster" || a == "-devmap" || a == "-taarisk" || a == "-streamprobe" || a == "-flysweep" || a == "-poolprobe" ||
                a == "-stutterprobe" || a == "-lightingab" || a == "-shadowstats" || a == "-pitchsweep" || a == "-fluidbench" || a == "-shadowdots"))
                return true;
        return false;
    }

    /// Last match wins, like PlaygroundCapture's ArgFloat.
    private static bool TryArgFloat(string flag, out float value)
    {
        value = 0f;
        bool found = false;
        string[] a = Environment.GetCommandLineArgs();
        for (int i = 0; i + 1 < a.Length; i++)
            if (a[i] == flag && float.TryParse(a[i + 1], System.Globalization.NumberStyles.Float,
                                               System.Globalization.CultureInfo.InvariantCulture, out float p)) { value = p; found = true; }
        return found;
    }

    private static bool HasArg(string flag)
    {
        foreach (string a in Environment.GetCommandLineArgs())
            if (string.Equals(a, flag, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// -playshot S: wait S unscaled seconds, write one PNG under
    /// persistentDataPath/PlayShots, log its path. Last match wins (like
    /// PlaygroundCapture's ArgValue).
    // =====================================================================
    // -survivaldemo (task 64): gameplay step 1's acceptance screenshots. An
    // INTERACTIVE run (run-play.sh EXTRA="-gamemode survival -survivaldemo"),
    // so the HUD is exactly what a player sees. Stands facing the nearest
    // rising ground with the sun behind, digs into it with SurvivalDig, opens
    // the E panel, builds a wall of 0.4 m cubes with TrySurvivalPlace beside
    // the hole, logs the inventory before/after (conservation), and quits.
    // Shots: persistentDataPath/SurvivalDemo/.
    // =====================================================================
    private IEnumerator SurvivalDemo()
    {
        float t0 = Time.unscaledTime;
        while (Time.unscaledTime - t0 < 25f || _player == null || !_player.Ready) yield return null;
        SetGameMode(true);
        _showHelp = false;
        _demoHideState = true;            // the dev readout panel, out of the pictures
        _mode = MoveMode.Walk;
        SunCycle.SetHours(SunCycle.NoonHours);
        VoxelEngine.Rendering.LightingDriver.ClockPaused = true;
        string dir = System.IO.Path.Combine(Application.persistentDataPath, "SurvivalDemo");
        System.IO.Directory.CreateDirectory(dir);
        if (_cam == null && Camera.main != null) _cam = Camera.main.GetComponent<PlaygroundFlyCamera>();

        // 1. A stand: flat dry ground within 60 m with a >= 1 m rise 4-8 m
        // ahead, heading within 45 deg of straight away from the sun.
        int3 f = CoordMath.WorldToVoxel(_player.Motor.PositionM);
        float2 sxz = math.normalizesafe(SunCycle.Direction.xz);
        float awayYaw = math.degrees(math.atan2(-sxz.x, -sxz.y));
        float bestScore = float.MinValue, bestYaw = awayYaw, bestD = -1f;
        int3 bestStand = f;
        for (int r = 0; r <= 600; r += 30)
        {
            for (int dz = -r; dz <= r; dz += 30)
            for (int dx = -r; dx <= r; dx += 30)
            {
                if (math.max(math.abs(dx), math.abs(dz)) != r) continue;
                int x = f.x + dx, z = f.z + dz, sy = DryTopY(x, z);
                if (sy < 0) continue;
                if (math.abs(DryTopY(x + 10, z) - sy) > 2 || math.abs(DryTopY(x - 10, z) - sy) > 2 ||
                    math.abs(DryTopY(x, z + 10) - sy) > 2 || math.abs(DryTopY(x, z - 10) - sy) > 2) continue;
                for (int k = -2; k <= 2; k++)
                {
                    float yaw = awayYaw + k * 22.5f, yr = math.radians(yaw);
                    float2 d = new float2(math.sin(yr), math.cos(yr));
                    for (float dm = 2.5f; dm <= 8f; dm += 0.5f)
                    {
                        int h = DryTopY(x + (int)math.round(d.x * dm * 10f), z + (int)math.round(d.y * dm * 10f));
                        if (h < 0) break;
                        if (h < sy + 10) { if (math.abs(h - sy) > 6) break; continue; }
                        if (dm < 4f) break;               // too close: no room for the wall
                        float score = -math.abs(dm - 5.5f) - 0.02f * r * 0.1f - 0.5f * math.abs(k);
                        if (score > bestScore) { bestScore = score; bestYaw = yaw; bestD = dm; bestStand = new int3(x, sy, z); }
                        break;
                    }
                }
            }
            yield return null;
        }
        if (bestD < 0f) { bestD = 5f; bestStand = new int3(f.x, math.max(DryTopY(f.x, f.z), f.y - 1), f.z); }
        _player.Motor.Teleport(new float3(bestStand.x * 0.1f + 0.05f, (bestStand.y + 1) * 0.1f + 0.02f, bestStand.z * 0.1f + 0.05f));
        _player.Motor.ResolveSpawn();
        float br = math.radians(bestYaw);
        float2 fwd = new float2(math.sin(br), math.cos(br));
        _cam?.SetLook(bestYaw, 6f);
        for (int i = 0; i < 90; i++) yield return null;
        Debug.Log($"[SurvivalDemo] stand {bestStand} top {Items.Name(_store.GetVoxel(bestStand))}, yaw {bestYaw:F0} (away from sun {awayYaw:F0}), rise at {bestD:F1} m; {EyeReport()}");

        // 2. Dig a pit in the ground to the left (the surface layers), then a
        // doorway into the hillside: a 3 x 3 grid of aims, deep enough to pass
        // the soil into rock. Bore-sized bites (r 3).
        int invBefore = InventoryTotal();
        int removed = 0, credited = 0, lost = 0;
        for (int a = 0; a < 6; a++)
        {
            _cam?.SetLook(bestYaw - 50f - (a & 1) * 12f, 50f + (a >> 1) * 6f);
            for (int k = 0; k < 16; k++)
            {
                yield return null;
                if (!_hasTarget || _targetDistM > 5f) continue;
                var r = SurvivalDig(_targetVoxel, 3, 150);
                removed += r.Removed; credited += r.Credited; lost += r.Lost;
            }
        }
        byte hillFace = Materials.Air;
        float aimPitch = math.degrees(math.atan2(1.6f - 0.5f, bestD));   // eye to ~0.5 m up the face
        for (int py = 0; py < 3; py++)
        for (int px = -1; px <= 1; px++)
        {
            _cam?.SetLook(bestYaw + px * 5f, aimPitch - py * 5f);
            for (int k = 0; k < 16; k++)
            {
                yield return null;
                if (!_hasTarget || _targetDistM > bestD + 4f) continue;
                if (hillFace == Materials.Air) hillFace = _targetMaterial;
                var r = SurvivalDig(_targetVoxel, 3, 150);
                removed += r.Removed; credited += r.Credited; lost += r.Lost;
            }
        }
        _cam?.SetLook(bestYaw, 8f);
        for (int i = 0; i < 30; i++) yield return null;
        Debug.Log($"[SurvivalDemo] dug {removed} voxels: credited {credited}, lost {lost}, inventory {invBefore} -> {InventoryTotal()} " +
                  $"(delta {InventoryTotal() - invBefore}); {InventoryLine()}");

        // Hold the plentiful block whose colour differs most from the hillside
        // behind the wall (so the wall reads in the picture).
        var ground = MaterialPalette.Of(hillFace != Materials.Air ? hillFace : _store.GetVoxel(bestStand));
        int bestSlot = -1;
        float bestPick = float.MinValue;
        for (int i = 0; i < Inventory.SlotCount; i++)
        {
            if (_inv.IsEmpty(i) || Items.PlacesSingleVoxel(_inv.ItemAt(i)) || _inv.CountAt(i) < 1600) continue;
            var c = MaterialPalette.Of(_inv.ItemAt(i));
            float diff = math.abs(c.R - ground.R) + math.abs(c.G - ground.G) + math.abs(c.B - ground.B);
            if (diff > bestPick) { bestPick = diff; bestSlot = i; }
        }
        if (bestSlot < 0)
            for (int i = 0; i < Inventory.SlotCount; i++)
                if (!_inv.IsEmpty(i) && !Items.PlacesSingleVoxel(_inv.ItemAt(i)) && (bestSlot < 0 || _inv.CountAt(i) > _inv.CountAt(bestSlot))) bestSlot = i;
        Debug.Log($"[SurvivalDemo] hill face {Items.Name(hillFace)}; wall block {(bestSlot >= 0 ? Items.Name(_inv.ItemAt(bestSlot)) : "none")}");
        if (bestSlot >= Inventory.HotbarSize) { _inv.Swap(bestSlot, 1); bestSlot = 1; }
        if (bestSlot >= 0) SelectSlot(bestSlot);
        yield return DemoShot(dir, "1_hotbar_after_dig");

        // 3. The E panel.
        InventoryOpen = true;
        for (int i = 0; i < 4; i++) yield return null;
        yield return DemoShot(dir, "2_inventory_panel");
        CloseInventory();

        // 4. A wall of 0.4 m cubes beside the doorway: 6 wide x 4 high, across
        // the view, 2.2 m ahead, from 0.6 m to 3.0 m right.
        _edgeIndex = SurvivalBuild.DefaultEdgeIndex;
        int e = SurvivalBuild.CubeEdges[_edgeIndex];
        bool alongX = math.abs(fwd.y) >= math.abs(fwd.x);
        float2 right = new float2(fwd.y, -fwd.x);
        int3 step = alongX ? new int3(right.x >= 0 ? e : -e, 0, 0) : new int3(0, 0, right.y >= 0 ? e : -e);
        int3 me = CoordMath.WorldToVoxel(_player.Motor.PositionM);
        int3 start = me + new int3((int)math.round(fwd.x * 22f + right.x * 6f), 0, (int)math.round(fwd.y * 22f + right.y * 6f));
        int invMid = InventoryTotal(), placed = 0, cubes = 0, refusedN = 0;
        for (int i = 0; i < 6; i++)
        {
            int3 c = start + step * i;
            int gy = DryTopY(c.x, c.z);
            if (gy < 0) continue;
            for (int j = 0; j < 4; j++)
            {
                var r = TrySurvivalPlace(new int3(c.x, gy + 1 + j * e, c.z));
                if (r.Outcome == SurvivalBuild.PlaceOutcome.Placed) { placed += r.Placed; cubes++; }
                else refusedN++;
                yield return null;
            }
        }
        Debug.Log($"[SurvivalDemo] wall: {cubes} cubes, {placed} voxels placed, {refusedN} not placed; inventory {invMid} -> {InventoryTotal()} " +
                  $"(delta {InventoryTotal() - invMid}, conservation {(invMid - InventoryTotal() == placed ? "OK" : "BROKEN")}); {InventoryLine()}");

        // 5. Back up 1.5 m and look between the doorway and the wall.
        float3 p = _player.Motor.PositionM - new float3(fwd.x, 0f, fwd.y) * 1.5f;
        int3 pv = CoordMath.WorldToVoxel(p);
        int py2 = DryTopY(pv.x, pv.z);
        if (py2 >= 0)
        {
            _player.Motor.Teleport(new float3(p.x, (py2 + 1) * 0.1f + 0.02f, p.z));
            _player.Motor.ResolveSpawn();
        }
        _cam?.SetLook(bestYaw + 14f, 12f);
        for (int i = 0; i < 60; i++) yield return null;
        yield return DemoShot(dir, "3_wall_beside_hole");
        SaveInventory();
        Debug.Log("[SurvivalDemo] done");
        Application.Quit();
    }

    private IEnumerator DemoShot(string dir, string name)
    {
        yield return new WaitForEndOfFrame();
        string path = System.IO.Path.Combine(dir, name + ".png");
        Texture2D tex = ScreenCapture.CaptureScreenshotAsTexture();
        System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
        Destroy(tex);
        Debug.Log($"[SurvivalDemo] shot {path}  {EyeReport()}, clock {SunCycle.ClockString()}");
    }

    private int InventoryTotal()
    {
        int n = 0;
        for (int i = 0; i < Inventory.SlotCount; i++) n += _inv.CountAt(i);
        return n;
    }

    private string InventoryLine()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < Inventory.SlotCount; i++)
            if (!_inv.IsEmpty(i)) sb.Append($"[{i}] {Items.Name(_inv.ItemAt(i))} {_inv.CountAt(i)}  ");
        return sb.ToString();
    }

    private IEnumerator PlayShot()
    {
        string[] a = Environment.GetCommandLineArgs();
        float s = -1f;
        for (int i = 0; i + 1 < a.Length; i++)
            if (a[i] == "-playshot" && float.TryParse(a[i + 1], System.Globalization.NumberStyles.Float,
                                                      System.Globalization.CultureInfo.InvariantCulture, out float p)) s = p;
        if (s < 0f) yield break;
        float t0 = Time.unscaledTime;
        while (Time.unscaledTime - t0 < s) yield return null;
        yield return new WaitForEndOfFrame();
        string dir = System.IO.Path.Combine(Application.persistentDataPath, "PlayShots");
        System.IO.Directory.CreateDirectory(dir);
        string path = System.IO.Path.Combine(dir, $"play_{DateTime.Now:yyyyMMdd_HHmmss}.png");
        Texture2D tex = ScreenCapture.CaptureScreenshotAsTexture();
        System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
        Destroy(tex);
        // What the eye is in (a black frame at the start pose: task 32b).
        Debug.Log($"[Playground] -playshot: {path}  {EyeReport()}, t {Time.unscaledTime:F1} s, clock {SunCycle.ClockString()}, day {SunCycle.DayLengthSeconds:F0} s");
    }

    private string EyeReport()
    {
        Camera cam = Camera.main;
        if (cam == null) return "no camera";
        Vector3 e = cam.transform.position;
        int3 v = CoordMath.WorldToVoxel(new float3(e.x, e.y, e.z));
        byte m = _store != null ? _store.GetVoxel(v) : (byte)255;
        return $"eye {e.x:F1},{e.y:F1},{e.z:F1} m = voxel {v} material {m}" +
               (m == Materials.Air ? " (air)" : m == Materials.Water ? " (WATER)" : " (solid/other)") + $", mode {_mode}";
    }

    /// Interactive tg2 start (task 32d): feet on the nearest column whose
    /// topmost non-air voxel in the loaded band is SOLID (not fluid), every
    /// chunk above it resident, so the eye is in open air. Waits while the
    /// columns are still streaming in.
    private IEnumerator SettleStartPose(bool startView = false)
    {
        Camera cam = Camera.main;
        if (cam == null) yield break;
        Vector3 a = cam.transform.position;
        int3 from = CoordMath.WorldToVoxel(new float3(a.x, a.y, a.z));
        string before = EyeReport();
        int3 col = default;
        bool found = false;
        for (int f = 0; f < 900 && !(found = TryFindDryColumn(from, out col)); f++) yield return null;
        if (!found)
        {
            Debug.LogWarning($"[Playground] start pose: no dry resident column within 32 m of {from} -- left at {before}");
            yield break;
        }
        bool basinWet = MaterialRules.IsFluidMaterial(_store.GetVoxel(_arenaCentre + new int3(0, 1, 0)));
        cam.transform.position = new Vector3(col.x * 0.1f + 0.05f, (col.y + 1) * 0.1f + 0.02f + 1.6f, col.z * 0.1f + 0.05f);
        if (startView)
        {
            // The M key's noon; the clock keeps running from here.
            SunCycle.SetHours(SunCycle.NoonHours);
            if (_cam == null) _cam = cam.GetComponent<PlaygroundFlyCamera>();
            float yaw = MostOpenYaw(cam.transform.position, cam.transform.rotation.eulerAngles.y);
            if (_cam != null) _cam.SetLook(yaw, 0f);
            else cam.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        }
        if (_mode == MoveMode.Walk) SnapPlayerToCamera();
        _status = "standing on dry ground near the fluid arena";
        Debug.Log($"[Playground] start pose: arena {before}; basin {_arenaCentre} under fluid: {basinWet}; " +
                  $"moved to dry column ({col.x}, {col.z}) top {col.y} {MaterialName(_store.GetVoxel(col))} -> {EyeReport()}" +
                  (startView ? $"; -startview: noon, level look (yaw {cam.transform.rotation.eulerAngles.y:F0}) at t {Time.unscaledTime:F1} s" : "") +
                  $"; day {SunCycle.DayLengthSeconds:F0} s");
        float t0 = Time.unscaledTime;
        while (Time.unscaledTime - t0 < 5f) yield return null;
        Debug.Log($"[Playground] start pose after 5 s: {EyeReport()}");
    }

    /// Of 16 level headings starting at `yaw`, the one whose eye-height ray
    /// runs furthest (to 64 m, or the first non-resident chunk) before it
    /// meets a non-air voxel; ties keep the earlier heading.
    private float MostOpenYaw(Vector3 eye, float yaw)
    {
        float bestYaw = yaw;
        int best = -1;
        for (int i = 0; i < 16; i++)
        {
            float y = yaw + i * 22.5f;
            float r = y * Mathf.Deg2Rad;
            float dx = Mathf.Sin(r), dz = Mathf.Cos(r);
            int run = 0;
            for (; run < 640; run += 4)
            {
                int3 v = CoordMath.WorldToVoxel(new float3(eye.x + dx * run * 0.1f, eye.y, eye.z + dz * run * 0.1f));
                if (!_store.IsResident(CoordMath.VoxelToChunk(v))) { run = 640; break; }
                if (_store.GetVoxel(v) != Materials.Air) break;
            }
            if (run > best) { best = run; bestYaw = y; }
        }
        return Mathf.Repeat(bestYaw, 360f);
    }

    /// Nearest column to `from` (8-voxel grid, square rings out to 32 m) whose
    /// top in the loaded band is dry ground; col.y is that top voxel.
    private bool TryFindDryColumn(int3 from, out int3 col)
    {
        col = default;
        for (int r = 0; r <= 320; r += 8)
        {
            long best = long.MaxValue;
            for (int dz = -r; dz <= r; dz += 8)
            for (int dx = -r; dx <= r; dx += 8)
            {
                if (math.max(math.abs(dx), math.abs(dz)) != r) continue;
                long d2 = (long)dx * dx + (long)dz * dz;
                if (d2 >= best) continue;
                int t = DryTopY(from.x + dx, from.z + dz);
                if (t < 0) continue;
                best = d2;
                col = new int3(from.x + dx, t, from.z + dz);
            }
            if (best != long.MaxValue) return true;
        }
        return false;
    }

    /// Topmost non-air voxel of the column in the loaded band if it is solid,
    /// else -1 (fluid on top, the band's ceiling is solid, or not resident).
    private int DryTopY(int x, int z)
    {
        int yTop = RaymarchFeature.ContentCeilingVoxelY - 1, yBot = RaymarchFeature.ContentFloorVoxelY;
        for (int y = yTop; y >= yBot; y--)
        {
            int3 v = new int3(x, y, z);
            if (!_store.IsResident(CoordMath.VoxelToChunk(v))) return -1;
            byte m = _store.GetVoxel(v);
            if (m == Materials.Air) continue;
            return y == yTop || MaterialRules.IsFluidMaterial(m) ? -1 : y;
        }
        return -1;
    }

    private void RegisterDefaultToys()
    {
        Register(new Toy(KeyCode.Tab, "walk/fly", ToggleMode));
        Register(new Toy(KeyCode.V, "vent here", OpenVentAtTarget));
        Register(new Toy(KeyCode.Alpha0, "stop vents", () =>
            { _waterLeft = _sandLeft = _lavaLeft = _otherLeft = 0; _status = "vents closed"; }));
        Register(new Toy(KeyCode.F, "go to arena", () =>
            { TeleportToArena(); if (Phase4Bootstrapper.IsTg2World) StartCoroutine(SettleStartPose()); }));
        Register(new Toy(KeyCode.R, "respawn on ground", Respawn));
        Register(new Toy(KeyCode.B, "bomb", DetonateAtTarget));
        Register(new Toy(KeyCode.G, "CHAOS BURST", StartChaosBurst));
        Register(new Toy(KeyCode.T, "shoot", ShootProjectile));
        Register(new Toy(KeyCode.Z, "tool -", () => CycleTier(-1)));
        Register(new Toy(KeyCode.X, "tool +", () => CycleTier(+1)));
        Register(new Toy(KeyCode.F2, "help", () => _showHelp = !_showHelp));
        Register(new Toy(KeyCode.O, "dev map (whole island + underground slice)", DevMap.Toggle));
        Register(new Toy(KeyCode.F3, "render preset: quality / balanced / performance", () =>
        {
            var next = (RenderPreset.Kind)(((int)RenderPreset.Current + 1) % 3);
            RenderPreset.Apply(next);
            _status = $"RENDER PRESET: {next} ({RenderPreset.InternalWidth(next)} px wide internal" +
                      (next == RenderPreset.Kind.Quality ? ", nearest blit)" : ", FSR to the screen)") +
                      (RaymarchFeature.TemporalShadows ? " -- temporal shadows ON" : "");
        }));
        // TAA (overnight task 06): OFF on the preset path; `-taa 1` starts with it on.
        // Under the FSR 3 / STP default (task 52) their own resolve replaces ours,
        // so the toggle has no visible effect until F3 hands the frame back.
        Register(new Toy(KeyCode.F4, "TAA on/off", () =>
        {
            RaymarchFeature.Taa = !RaymarchFeature.Taa;
            _status = RaymarchFeature.Taa
                ? $"TAA ON (jitter {RaymarchFeature.TaaJitterCycle}-frame Halton, sharpen {RaymarchFeature.TaaSharpenAmount:F2}) -- F4 to compare"
                : "TAA OFF -- F4 to compare";
            if (RaymarchFeature.UpscaleInputs != RaymarchFeature.UpscalerInputMode.Off)
                _status += $" (no visible effect: {UpscalerSelect.Name(UpscalerSelect.Current)} resolves the frame; F3 for the presets)";
        }));
        // Detail radius at speed (overnight task 24): OFF by default; `-detailspeed 1` starts with it on.
        Register(new Toy(KeyCode.F5, "voxel detail radius at speed on/off", () =>
        {
            VoxelEngine.Streaming.DetailRadius.Enabled = !VoxelEngine.Streaming.DetailRadius.Enabled;
            _status = VoxelEngine.Streaming.DetailRadius.Enabled
                ? $"DETAIL RADIUS AT SPEED ON (now {VoxelEngine.Streaming.DetailRadius.RadiusM:F0} m at {VoxelEngine.Streaming.DetailRadius.SpeedMps:F1} m/s) -- F5 to compare"
                : "DETAIL RADIUS AT SPEED OFF (hand-over at the residency edge) -- F5 to compare";
        }));

        // ================= PHASE 7 (§13, §6.5, §10.3) =================
        // THE THREE POSES THE OWNER HAS TO JUDGE IN MINUTES GET THEIR OWN KEYS.
        // §13's solo-dev note says the real work here is tuning by eye in real
        // time, and a tuning loop that starts with "scrub until it looks like
        // evening" wastes the minutes it is meant to save. Golden hour is
        // DERIVED from the sun's own arc (SunCycle.GoldenHourHours), never
        // typed, so it stays golden hour if the arc tilt is retuned.
        Register(new Toy(KeyCode.N, "golden hour", () =>
            { SunCycle.SetHours(SunCycle.GoldenHourHours); _status = SunStatus("GOLDEN HOUR"); }));
        Register(new Toy(KeyCode.M, "noon", () =>
            { SunCycle.SetHours(SunCycle.NoonHours); _status = SunStatus("NOON"); }));
        Register(new Toy(KeyCode.L, "midnight", () =>
            { SunCycle.SetHours(SunCycle.MidnightHours); _status = SunStatus("MIDNIGHT"); }));
        Register(new Toy(KeyCode.P, "pause/run the clock", () =>
        {
            VoxelEngine.Rendering.LightingDriver.ClockPaused =
                !VoxelEngine.Rendering.LightingDriver.ClockPaused;
            _status = VoxelEngine.Rendering.LightingDriver.ClockPaused
                ? $"clock PAUSED at {SunCycle.ClockString()} — , and . still scrub"
                : $"clock RUNNING — a full day takes {SunCycle.DayLengthSeconds:F0} s";
        }));
        Register(new Toy(KeyCode.J, "shadow quality 0-3", () =>
        {
            RaymarchFeature.ShadowQualityLevel = (RaymarchFeature.ShadowQualityLevel + 1) % 4;
            _status = "shadows: " + ShadowRungName(RaymarchFeature.ShadowQualityLevel) +
                      "   (§6.5's fallback rungs — stepping down one is the spec's own designed outcome)";
        }));
        Register(new Toy(KeyCode.K, "hard <-> soft shadows", () =>
        {
            // §6.5: "hard shadows remain a toggle." Flips k_soft between 0 and
            // the config's value WITHOUT writing the file, so the owner can A/B
            // it instantly and still have their tuned number to come back to.
            if (LightingConfig.Active.kSoft > 0f)
            {
                SetSoftShadows(false);
                _status = "HARD shadows (k_soft = 0). Press K again to restore " +
                          $"k_soft = {_savedKSoft:F3} from LightingConfig.json.";
            }
            else
            {
                SetSoftShadows(true);
                _status = $"SOFT shadows (C.10, k_soft = {LightingConfig.Active.kSoft:F3})";
            }
        }));
        // LightingConfig.look A/B without touching the file: flips between the
        // Brickmap look (1) and C.9 as specified (0), like K does for k_soft.
        Register(new Toy(KeyCode.U, "look: Brickmap <-> Phase 7", () =>
        {
            int cur = RaymarchFeature.LookOverride >= 0 ? RaymarchFeature.LookOverride
                                                        : LightingConfig.Active.look;
            RaymarchFeature.LookOverride = cur == 1 ? 0 : 1;
            _status = RaymarchFeature.LookOverride == 1
                ? "LOOK: Brickmap (display-space shading, sky gradient, haze) — U to compare"
                : "LOOK: Phase 7 / C.9 as specified (linear + ACES) — U to compare";
        }));
        // STAGE C's preset switch. Goes through the FILE (see
        // LightingPresets.Apply), so the chosen preset ends up sitting in the
        // very file the owner was told to edit -- tweaking from there needs no
        // extra step, and a preset that switches correctly is itself evidence
        // the §8.1 hot-reload path works.
        Register(new Toy(KeyCode.RightBracket, "next light preset", () => CyclePreset(+1)));
        Register(new Toy(KeyCode.LeftBracket, "prev light preset", () => CyclePreset(-1)));

        // FAR BAND (DESIGN_LARGE_WORLD §4.2). Only does anything when the player
        // was launched with -farband 1 (run-farband.sh / the demo command).
        Register(new Toy(KeyCode.C, "far band on/off", () =>
        {
            if (VoxelEngine.Rendering.FarField.Active == null) { _status = "far band not built -- launch with -farband 1"; return; }
            VoxelEngine.Rendering.FarField.RenderEnabled = !VoxelEngine.Rendering.FarField.RenderEnabled;
            _status = VoxelEngine.Rendering.FarField.RenderEnabled ? "FAR BAND ON (terrain to ~5.6 km)" : "FAR BAND OFF (shipped: sky past 290 m)";
        }));
        Register(new Toy(KeyCode.I, "far band view", () =>
        {
            // Beauty -> band view (who drew each pixel) -> far step heat -> Beauty.
            var cur = RaymarchFeature.UseDebugViewOverride ? RaymarchFeature.DebugViewOverride : RaymarchFeature.DebugMode.Beauty;
            if (cur == RaymarchFeature.DebugMode.FarBand)
            { RaymarchFeature.DebugViewOverride = RaymarchFeature.DebugMode.FarHeat; RaymarchFeature.UseDebugViewOverride = true;
              _status = "view: FAR STEP HEAT (blue cheap, red expensive; black = voxel band)"; }
            else if (cur == RaymarchFeature.DebugMode.FarHeat)
            { RaymarchFeature.UseDebugViewOverride = false; _status = "view: BEAUTY"; }
            else
            { RaymarchFeature.DebugViewOverride = RaymarchFeature.DebugMode.FarBand; RaymarchFeature.UseDebugViewOverride = true;
              _status = "view: BANDS (blue voxels; far rings F0 green, F1 yellow, F2 orange, F3 red, F4 magenta)"; }
        }));

        Register(new Toy(KeyCode.Y, "lighting view", () =>
        {
            // Beauty -> SunVis -> ShadowHeat -> Beauty. §10.3 item 6's two views,
            // reachable without cycling all nine.
            var cur = RaymarchFeature.UseDebugViewOverride
                ? RaymarchFeature.DebugViewOverride : RaymarchFeature.DebugMode.Beauty;
            if (cur == RaymarchFeature.DebugMode.SunVis)
            { RaymarchFeature.DebugViewOverride = RaymarchFeature.DebugMode.ShadowHeat;
              RaymarchFeature.UseDebugViewOverride = true;
              _status = "view: SHADOW-RAY STEP HEAT (blue cheap, red expensive — same ramp as the primary heat view)"; }
            else if (cur == RaymarchFeature.DebugMode.ShadowHeat)
            { RaymarchFeature.UseDebugViewOverride = false;
              _status = "view: BEAUTY"; }
            else
            { RaymarchFeature.DebugViewOverride = RaymarchFeature.DebugMode.SunVis;
              RaymarchFeature.UseDebugViewOverride = true;
              _status = "view: SUN VISIBILITY (white lit, black shadowed, dark blue sky, violet night)"; }
        }));
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
    // =====================================================================
    // CHAOS BURST (G) -- the scale showcase.
    //
    // The vents were the only way to add fluid and they are a trickle by
    // construction: Air-only placement around a walking source point. They
    // demonstrate that fluid works, not that it works AT SCALE, and the scale
    // is the thing this engine spent five phases proving -- 750,000 live
    // voxels, a 1024-tile pool, 1.5 M live measured with every gate green.
    //
    // This drops ~51,000 voxels of water, sand and lava across a wide
    // footprint around the player in one action, using the same scattered-box
    // pour LateGameSiegeRig uses (SetBox of a small cube at jittered offsets,
    // materials cycled) -- proven placement logic, not a new path.
    //
    // DRAINED OVER FRAMES, NOT DUMPED IN ONE. 51,000 voxels through SetBox in
    // a single frame is a visible hitch and would misrepresent the engine's
    // behaviour; the siege rig places ~1,000/frame for exactly this reason.
    // BurstBoxesPerFrame keeps each frame's edit cost near the siege's, so
    // what you see is the CA absorbing a real pour rather than a stall.
    //
    // Everything lands inside §7.4's wake radius, so all of it actually
    // simulates -- material placed outside it would be drawn and never tick,
    // which is the frozen-blob artifact this scene exists to avoid.
    private const int BurstTotalBoxes = 100;   // x 512 voxels = ~51,200
    private const int BurstBoxEdge = 8;
    private const int BurstSpreadVoxels = 56;
    private const int BurstBoxesPerFrame = 4;
    private int _burstBoxesLeft;
    private int3 _burstCentre;
    private System.Random _burstRng;

    private void StartChaosBurst()
    {
        if (_burstBoxesLeft > 0) { _status = "chaos burst: already pouring"; return; }
        _burstCentre = PlayerOrCameraVoxel();
        _burstRng = new System.Random(20260912);
        _burstBoxesLeft = BurstTotalBoxes;
        _status = $"CHAOS BURST — {BurstTotalBoxes * BurstBoxEdge * BurstBoxEdge * BurstBoxEdge:N0} " +
                  $"voxels of water/sand/lava over ±{BurstSpreadVoxels}v " +
                  $"({BurstSpreadVoxels * 0.1f:0.#}m), draining over frames";
    }

    private void StepChaosBurst()
    {
        if (_burstBoxesLeft <= 0) return;
        for (int i = 0; i < BurstBoxesPerFrame && _burstBoxesLeft > 0; i++, _burstBoxesLeft--)
        {
            int n = BurstTotalBoxes - _burstBoxesLeft;
            byte m = (n % 3) == 0 ? Materials.Water : (n % 3) == 1 ? Materials.Sand : Materials.Lava;
            int dx = _burstRng.Next(-BurstSpreadVoxels, BurstSpreadVoxels);
            int dz = _burstRng.Next(-BurstSpreadVoxels, BurstSpreadVoxels);
            int sx = _burstCentre.x + dx, sz = _burstCentre.z + dz;
            int sy = SurfaceY(sx, sz);
            if (sy < 1) continue;
            var lo = new int3(sx, sy + 10 + _burstRng.Next(0, 14), sz);
            _edits.SetBox(lo, lo + (BurstBoxEdge - 1), m);
        }
        if (_burstBoxesLeft == 0) _status = "chaos burst: all placed — watch it settle";
    }

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
        if (!InFluidRange(cell)) { _status = "vent: outside §7.4's active radius — move closer"; return; }
        byte m = _brushes[_brush];
        if (m == Materials.Water) { _waterSrc = cell; _waterLeft = _waterBudget; }
        else if (m == Materials.Sand) { _sandSrc = cell; _sandLeft = _sandBudget; }
        else if (m == Materials.Lava) { _lavaSrc = cell; _lavaLeft = _lavaBudget; }
        else if (MaterialRules.IsMobile(m)) { _otherSrc = cell; _otherLeft = OtherVentBudget; _otherMat = m; }
        else { _status = "vent: hold a fluid or sand (water, sand, lava, oil, acid, sludge, quicksand, steam)"; return; }
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
        // V2: the TerrainGen world's ground is ~5,000 voxels up and streams in
        // a band; scan the loaded band. The flat world keeps its exact range.
        int yTop = Phase4Bootstrapper.IsTg2World ? RaymarchFeature.ContentCeilingVoxelY - 1 : WorldGenConstants.MAX_TERRAIN_HEIGHT + 2;
        int yBot = Phase4Bootstrapper.IsTg2World ? RaymarchFeature.ContentFloorVoxelY : 1;
        for (int y = yTop; y >= yBot; y--)
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
        if (m == Materials.Torch) return "torch";
        if (m == Materials.TorchBlue) return "blue torch";
        if (m == Materials.TorchGreen) return "green torch";
        if (m == Materials.TorchPurple) return "violet torch";
        if (m == Materials.Oil) return "oil";
        if (m == Materials.Acid) return "acid";
        if (m == Materials.Sludge) return "toxic sludge";
        if (m == Materials.Quicksand) return "quicksand";
        if (m == Materials.Steam) return "steam";
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
        RenderPreset.Tick();
        UpscalerSelect.Tick();

        float dt = Time.deltaTime;
        _actAge += dt;
        _shotAge += dt;

        // The flycam always owns capture + look; only its MOVEMENT is mode-gated.
        if (_cam != null) _cam.MovementEnabled = _mode == MoveMode.Fly;

        // THE DEV MAP OWNS INPUT WHILE OPEN: no digging, no brushes, no walk
        // motor rewriting the camera. The simulation below keeps running.
        if (DevMap.IsOpen)
        {
            if (Input.GetKeyDown(KeyCode.O) || Input.GetKeyDown(KeyCode.Escape)) DevMap.Toggle();
        }
        else if (SettingsMenu.IsOpen)
        {
            // THE SETTINGS MENU (F6) OWNS THE MOUSE AND KEYS WHILE OPEN, like the
            // dev map: its buttons must not dig, and its keys must not walk.
            InventoryOpen = false;
        }
        else if (InventoryOpen)
        {
            // THE E PANEL OWNS THE MOUSE TOO (its slots are clicked); E or Esc closes.
            if (!SurvivalMode || Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.Escape)) CloseInventory();
        }
        else
        {
            UpdateTarget();
            foreach (var t in _toys) if (Input.GetKeyDown(t.Key)) t.Run();
            HandleTimeScrub(dt);
            HandleHotbarKeys();
            HandleSurvivalKeys();
            HandleMouse(dt);
            DriveWalk(dt);
        }
        TickInventorySave(dt);

        StepChaosBurst();

        _ventPhase++;
        Emit(ref _waterLeft, _waterSrc, Materials.Water);
        Emit(ref _sandLeft, _sandSrc, Materials.Sand);
        Emit(ref _lavaLeft, _lavaSrc, Materials.Lava);
        if (_otherLeft > 0) Emit(ref _otherLeft, _otherSrc, _otherMat);

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
        int3 centre = PlayerOrCameraVoxel();
        _fluid.UpdatePlayerPosition(centre);

        // TILED: residency must be refreshed or no tile is ever acquired and
        // nothing simulates. The dense path needed no equivalent, which is
        // exactly the kind of step that goes missing in a port.
        if ((_residencyTick++ % 20) == 0)
        {
            DebugLastResidency = FluidTileResidency.Refresh(_store, _tiles, centre,
                                       _fluid.ActiveRadiusVoxels, _fluid.SleepRadiusVoxels);
            DebugResidencyRefreshes++;
        }

        // MEASUREMENT SEAM, not a configuration. DebugSuspendFluidSim lets a
        // probe run the scene with the CA's per-tick dispatch removed while
        // everything else stays identical, to test how much of the frame is
        // fixed cost that has nothing to do with fluid being present. The CA
        // dispatches its region and slot passes EVERY tick regardless of
        // activity -- OPTIMIZATION_CANDIDATES #1 -- so "no fluid placed" is
        // NOT the same thing as "no fluid cost".
        if (!DebugSuspendFluidSim)
        {
            if (_readback.CanIssue)
            {
                _fluid.Tick(_clipmap);
                _readback.IssueReadback(0);
            }
            _readback.PumpAndApply();
        }
    }

    internal static bool DebugSuspendFluidSim = false;

    /// Fluid CA ticks issued so far (one per GPU readback round-trip -- see
    /// FluidOpListReadback.MaxFramesInFlight). Read by -sustained to report
    /// ticks/second, i.e. how fluid speed tracks frame rate. -1 before setup.
    internal long DebugFluidTicks => _readback != null ? _readback.RequestsIssuedTotal : -1;

    /// Decision 11's diagnosis: the readback whose counters split a tick's period.
    internal FluidOpListReadback DebugReadback => _readback;

    /// MEASUREMENT SEAM, NOT A CONFIGURATION -- the same standing this file's
    /// own header gives DebugSuspendFluidSim.
    ///
    /// LightSourceBuffer.Upload() runs unconditionally every frame: it copies
    /// the registry to scratch, sorts it by distance, rebuilds the staging array
    /// and calls GraphicsBuffer.SetData -- even with ZERO lights placed and the
    /// player standing still. That was a deliberate choice (the ACTIVE SET
    /// depends on player position, so it changes when the player moves even
    /// though no light did) and it is documented as such in that file. But
    /// "deliberate" is not "measured", and a per-frame driver call is exactly
    /// the shape of thing that shows up as variance rather than as mean cost.
    /// This flag lets Stage A ask the question instead of arguing it.
    internal static bool DebugSuspendLightUpload = false;

    /// Suppresses every in-scene overlay (help, perf, hotbar, state panels) so a
    /// capture pass produces a clean picture of the world rather than a picture
    /// of the UI. Set by -p7stress.
    internal static bool DebugHideUi = false;

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

    /// §13's time scrubber. HELD, not tapped: §13's acceptance bullet is "scrub
    /// dawn->dusk and watch the mountain shadow sweep CONTINUOUSLY", which is
    /// not a thing you can judge one keypress at a time.
    ///
    /// Scrubbing PAUSES the clock automatically. Without that, letting go of the
    /// key hands the time back to whatever the day-length multiplier was doing
    /// and the pose you just found drifts out from under you.
    private void HandleTimeScrub(float dt)
    {
        const float hoursPerSecond = 3f;
        bool back = Input.GetKey(KeyCode.Comma);
        bool fwd = Input.GetKey(KeyCode.Period);
        if (!back && !fwd) return;

        VoxelEngine.Rendering.LightingDriver.ClockPaused = true;
        SunCycle.SetHours(SunCycle.Hours + (fwd ? 1f : -1f) * hoursPerSecond * dt);
        _status = SunStatus("scrubbing");
    }

    /// Which preset is showing. -1 until one is chosen, because at startup the
    /// live config is whatever LightingConfig.json holds, which may be a
    /// hand-edit rather than any preset -- claiming a name for it would be a lie.
    private int _presetIndex = -1;
    internal string CurrentPresetName =>
        _presetIndex < 0 ? "custom (LightingConfig.json as found)"
                         : VoxelEngine.Rendering.LightingPresets.All[_presetIndex].Name;

    private void CyclePreset(int delta)
    {
        int n = VoxelEngine.Rendering.LightingPresets.All.Length;
        _presetIndex = _presetIndex < 0
            ? (delta > 0 ? 0 : n - 1)
            : (int)Mathf.Repeat(_presetIndex + delta, n);
        string what = VoxelEngine.Rendering.LightingPresets.Apply(_presetIndex);
        _status = $"LIGHT PRESET {_presetIndex + 1}/{n}: {what}";
    }

    private static string SunStatus(string label) =>
        $"{label} — {SunCycle.ClockString()}, sun {SunCycle.ElevationDegrees:F1}° ({SunCycle.PhaseName})";

    internal static string ShadowRungName(int level)
    {
        switch (level)
        {
            case 0: return "0 FULL (soft penumbra, C.10)";
            case 1: return "1 HALF-RES beyond LOD1";
            case 2: return "2 HARD only";
            case 3: return "3 OFF (flat N.L + ambient)";
            default: return level.ToString();
        }
    }

    private void HandleHotbarKeys()
    {
        if (SurvivalMode)
        {
            for (int i = 0; i < Inventory.HotbarSize; i++)
                if (Input.GetKeyDown(KeyCode.Alpha1 + i)) SelectSlot(i);
            return;
        }
        for (int i = 0; i < _brushes.Length && i < 9; i++)
            if (Input.GetKeyDown(KeyCode.Alpha1 + i)) SetBrush(i);
    }

    /// E and Q, Survival and WALK only: in fly mode they stay the flycam's
    /// up/down (a dev mode, pick P11), so flying never opens the panel.
    private void HandleSurvivalKeys()
    {
        if (!SurvivalMode || _mode != MoveMode.Walk) return;
        if (Input.GetKeyDown(KeyCode.E)) { InventoryOpen = true; _invPick = -1; }
        if (Input.GetKeyDown(KeyCode.Q))
        {
            _edgeIndex = (_edgeIndex + 1) % SurvivalBuild.CubeEdges.Length;
            int e = SurvivalBuild.CubeEdges[_edgeIndex];
            _status = $"place size {e} voxel{(e > 1 ? "s" : "")} = {e * 0.1f:0.0} m cube";
        }
    }

    private void SelectSlot(int i)
    {
        _inv.Selected = i;
        _status = _inv.IsEmpty(_inv.Selected) ? $"slot {_inv.Selected + 1}: empty"
            : $"holding {Items.Name(_inv.ItemAt(_inv.Selected))} x{_inv.CountAt(_inv.Selected):N0}";
    }

    private void CloseInventory()
    {
        InventoryOpen = false;
        _invPick = -1;
    }

    /// Called by the settings menu (F6 "Game mode") and LoadAndApply.
    internal static void SetGameMode(bool survival)
    {
        SurvivalMode = survival;
        if (!survival) InventoryOpen = false;
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

        bool keys = _cam == null || _cam.Captured;
        bool jump = keys && Input.GetKeyDown(KeyCode.Space);
        if (!_moveAbilities) _player.DebugStep(dt, world, jump);
        else
        {
            // Movement set A (task 65): swim/dive, double jump, dash. Interactive
            // runs only; rigs and captures keep the legacy step above.
            var input = MotorInput.FromLook(wish, yaw, _cam != null ? _cam.Pitch : 0f);
            input.JumpPressed = jump;
            input.UpHeld = keys && Input.GetKey(KeyCode.Space);
            input.DownHeld = keys && Input.GetKey(KeyCode.LeftControl);
            input.DashPressed = keys && Input.GetKeyDown(KeyCode.LeftShift);
            // -movedemo: scripted input; and the frame after a screenshot (a
            // PNG encode, often 0.3 s) is not allowed to bend the trajectory.
            if (_demoInput.HasValue) { input = _demoInput.Value; dt = math.min(dt, 1f / 30f); }
            _demoInput = null;
            _player.DebugStep(dt, input);
        }

        ApplyBuoyancy(dt);
    }

    /// Movement set A on (interactive runs). Set in Start.
    private bool _moveAbilities;
    /// -movedemo: one frame of scripted input, consumed by the next DriveWalk.
    private MotorInput? _demoInput;

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
        // A swimming motor (movement set A) already moves at the fluid's own
        // swim speed; the game-layer forces below are for wading and sinking.
        if (motor.Swimming || motor.SwimLockS > 0f) return;

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
        {
            if (SurvivalMode) SelectSlot(_inv.Selected + (scroll > 0 ? 1 : -1));
            else SetBrush((int)Mathf.Repeat(_brush + (scroll > 0 ? 1 : -1), _brushes.Length));
        }

        if (!Input.GetMouseButton(1)) _placeRepeat = 0f;
        if (!_hasTarget) return;
        if (SurvivalMode) { HandleSurvivalMouse(dt); return; }

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

    /// Survival's LMB / RMB. Dig: the tool tier's rate and radius, as Creative,
    /// but every voxel lands in the inventory. Place: one cube per click, and
    /// one per PlaceRepeatS while RMB is held.
    private void HandleSurvivalMouse(float dt)
    {
        if (Input.GetMouseButton(0))
        {
            var tier = EditService.Tiers[_tier];
            int allow = _digBudget.Accrue(dt, tier.VoxelsPerSecond);
            if (allow > 0) SurvivalDig(_targetVoxel, tier.RadiusVoxels, allow);
        }
        else if (Input.GetMouseButton(1))
        {
            _placeRepeat -= dt;
            if (_placeRepeat > 0f) return;
            _placeRepeat = PlaceRepeatS;
            TrySurvivalPlace(_targetAdjacent);
        }
    }

    /// The dig half of the loop. Internal so the -survivaldemo shots drive
    /// the real path rather than a copy of it.
    internal SurvivalBuild.DigResult SurvivalDig(int3 at, int radius, int budget)
    {
        var r = SurvivalBuild.Dig(_edits, _inv, at, radius, budget, _digScratch);
        if (r.Removed == 0) return r;
        _dugCounter += r.Removed;
        _act = Act.Dig;
        _actAge = 0f;
        _invDirty |= r.Credited > 0;
        _lostTotal += r.Lost;
        _status = r.Lost > 0
            ? $"<color=#ff9a9a>inventory FULL -- {r.Lost} voxel(s) of {Items.Name(r.LastItem)} lost</color>"
            : r.Credited > 0 ? $"+{r.Credited} {Items.Name(r.LastItem)}  (now {_inv.Count(r.LastItem):N0})"
            : "dug fluid -- nothing to pick up (no bucket yet)";
        return r;
    }

    /// The place half. Refuses (status line, nothing written) when the cube
    /// would enclose the player, when a mobile item would leave the wake
    /// radius (the Creative guard below, same reason), or when the slot holds
    /// fewer voxels than the cube needs (P7).
    internal SurvivalBuild.PlaceResult TrySurvivalPlace(int3 at)
    {
        int slot = _inv.Selected;
        byte item = _inv.ItemAt(slot);
        int edge = SurvivalBuild.EdgeFor(item, SurvivalBuild.CubeEdges[_edgeIndex]);
        int3 lo = SurvivalBuild.SnapToGrid(at, edge);
        var refused = new SurvivalBuild.PlaceResult { Outcome = SurvivalBuild.PlaceOutcome.NothingToFill, Item = item, Edge = edge, Lo = lo };

        if (!_inv.IsEmpty(slot) && _mode == MoveMode.Walk && _player != null && _player.Motor != null)
        {
            float3 feet = _player.Motor.PositionM;
            float hw = PlayerConfig.Active.bodyWidthM * 0.5f;
            int3 bLo = CoordMath.WorldToVoxel(feet - new float3(hw, 0f, hw));
            int3 bHi = CoordMath.WorldToVoxel(feet + new float3(hw, PlayerConfig.Active.bodyHeightM, hw));
            if (SurvivalBuild.CubeOverlaps(lo, edge, bLo, bHi))
            {
                PlaceRefused($"not placed -- the {edge * 0.1f:0.0} m cube would enclose you; step back or press Q for a smaller size");
                return refused;
            }
        }
        if (!_inv.IsEmpty(slot) && MaterialRules.IsMobile(item) && !SphereFitsInWakeRadius(lo + (edge >> 1), edge))
        {
            PlaceRefused($"{Items.Name(item)} not placed -- outside the {_fluid.ActiveRadiusVoxels * 0.1f:0.#} m wake radius, it would never fall");
            return refused;
        }

        var r = SurvivalBuild.Place(_edits, _store, _inv, slot, at, edge);
        switch (r.Outcome)
        {
            case SurvivalBuild.PlaceOutcome.Placed:
                _act = Act.Place;
                _actAge = 0f;
                _invDirty = true;
                _status = $"placed {r.Placed} {Items.Name(r.Item)} ({r.Edge * 0.1f:0.0} m cube), {_inv.CountAt(slot):N0} left";
                break;
            case SurvivalBuild.PlaceOutcome.EmptySlot: PlaceRefused($"slot {slot + 1} is empty -- dig (LMB) to fill it"); break;
            case SurvivalBuild.PlaceOutcome.NotPlaceable: PlaceRefused($"{Items.Name(r.Item)} cannot be placed"); break;
            case SurvivalBuild.PlaceOutcome.TooFew:
                PlaceRefused($"not enough {Items.Name(r.Item)}: the {r.Edge * 0.1f:0.0} m cube needs {r.Needed}, slot has {_inv.CountAt(slot)} -- Q for a smaller size");
                break;
            case SurvivalBuild.PlaceOutcome.NotLoaded: PlaceRefused("not placed -- that ground is not loaded yet"); break;
            default: break;                                   // NothingToFill: already solid, silent
        }
        return r;
    }

    private void PlaceRefused(string why)
    {
        _status = $"<color=#ff9a9a>{why}</color>";
        _act = Act.Refused;
        _actAge = 0f;
    }

    // ---- P12: player.json beside the world's deltas ----

    private void LoadInventory()
    {
        if (!IsPlaySettingsRun() || string.IsNullOrEmpty(Phase4Bootstrapper.DeltaDirectory)) return;
        _invPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Phase4Bootstrapper.DeltaDirectory), "player.json");
        // A fresh world (-cleardeltas, run-play.sh's default) starts a fresh
        // inventory; KEEP=1 (-keepdeltas) keeps both.
        if (!HasArg("-keepdeltas") || !System.IO.File.Exists(_invPath))
        {
            Debug.Log($"[Playground] inventory: fresh (starter kit), saves to {_invPath}");
            _invDirty = true;
            return;
        }
        try
        {
            var inv = Inventory.FromJson(System.IO.File.ReadAllText(_invPath), out string err);
            if (inv != null) _inv = inv;
            Debug.Log($"[Playground] inventory: loaded {_invPath}" + (err != null ? $" ({err})" : ""));
        }
        catch (Exception e) { Debug.LogWarning($"[Playground] inventory: read failed ({e.Message}), starter kit"); }
    }

    private void TickInventorySave(float dt)
    {
        _invSaveTimer += dt;
        if (_invSaveTimer < 30f) return;
        _invSaveTimer = 0f;
        SaveInventory();
    }

    private void SaveInventory()
    {
        if (!_invDirty || _invPath == null) return;
        try
        {
            string tmp = _invPath + ".tmp";
            System.IO.File.WriteAllText(tmp, _inv.ToJson());
            if (System.IO.File.Exists(_invPath)) System.IO.File.Delete(_invPath);
            System.IO.File.Move(tmp, _invPath);
            _invDirty = false;
        }
        catch (Exception e) { Debug.LogWarning($"[Playground] inventory: save failed ({e.Message})"); }
    }

    void OnApplicationQuit() => SaveInventory();

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

        // REFUSE A MOBILE BRUSH OUTSIDE §7.4's WAKE RADIUS. (This comment
        // also said "the CA's region is fixed, the moving radius is unbuilt".
        // It IS built -- that is what the guard below tests.) RequestWake
        // correctly drops wakes outside the radius, so a mobile material
        // written outside it is committed to ChunkStore and uploaded to the
        // mirror, and then NEVER SIMULATED. It renders as a frozen blob
        // hanging wherever the crosshair was: sand that does not fall, water
        // that does not spread. Header note 1 says this scene must make that
        // limit VISIBLE.
        //
        // THIS GUARD COVERS THE RADIUS AND NOT THE TILE POOL, and those are
        // two different ways to get the same frozen blob. Inside the radius
        // with a FULL POOL, placement is still accepted and the fluid still
        // never simulates -- reproduced deliberately in FluidTileCapRig. The
        // pool half is deliberately NOT guarded here yet; it is one of the
        // options written up in FLUID_TILE_CAP_RESULTS.md and is a decision,
        // not a cleanup.
        //
        // All-or-nothing on purpose. Placing only the in-region cells of a blob
        // that straddles the edge would leave a frozen rim outside it -- the
        // same silent-partial shape as the §9.4 residency-edge bug.
        if (MaterialRules.IsMobile(m) && !SphereFitsInWakeRadius(at, radius))
        {
            // THE MESSAGE HAS TO NAME THE BOUND THAT ACTUALLY REFUSED.
            // It used to read "outside the 64^3 fluid arena ... §7.4's moving
            // radius is unbuilt ... press F to go to the arena", which was
            // written for the dense build and is now wrong three times over:
            // the guard above tests §7.4's WAKE RADIUS, that radius is built
            // and is what refused, and F teleports to the startup basin --
            // not the remedy, because the radius follows the player. A
            // refusal that misnames its own cause sends the player to fix
            // the wrong thing.
            int away = (int)math.distance((float3)at, (float3)_fluid.PlayerVoxel);
            _status = $"<color=#ff9a9a>{_brushNames[_brush]} NOT placed at {at} — {away} voxels " +
                      $"away, outside §7.4's {_fluid.ActiveRadiusVoxels}v / " +
                      $"{_fluid.ActiveRadiusVoxels * 0.1f:0.#}m wake radius. " +
                      "It would be written to terrain and then never simulate. " +
                      "Move closer — the radius follows you.</color>";
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

    /// True iff a blob placed here would actually be simulated.
    ///
    /// REWRITTEN FOR TILING, AND THE OLD PREDICATE WOULD HAVE BEEN A TRAP.
    /// SphereFitsInRegion tests the dense region BOX, which under tiling is a
    /// 64^3 addressing origin at world zero -- it would have rejected every
    /// placement in the world. And InRegion(v) under tiling asks "is this
    /// voxel in a RESIDENT TILE", which is only true where fluid ALREADY is,
    /// so using it as a placement guard would refuse to start a new pool
    /// anywhere. Both compile and both are silently wrong.
    ///
    /// The real tiled bound is §7.4's active radius around the player, plus
    /// residency of the chunk (EditService already refuses unloaded chunks).
    private bool SphereFitsInWakeRadius(int3 centre, int radius)
    {
        if (_fluid == null) return false;
        // Every cell the brush covers must be inside the wake radius, or the
        // part outside it is placed and then never promoted.
        for (int dz = -radius; dz <= radius; dz++)
            for (int dy = -radius; dy <= radius; dy++)
                for (int dx = -radius; dx <= radius; dx++)
                {
                    if (!FluidGpuSimulation.SphereCovers(dx, dy, dz, radius)) continue;
                    if (!FluidActiveRegion.WithinWakeRadius(centre + new int3(dx, dy, dz),
                                                            _fluid.PlayerVoxel,
                                                            _fluid.ActiveRadiusVoxels))
                        return false;
                }
        return true;
    }

    /// Will fluid at this voxel be simulated right now? Used for the crosshair
    /// tint and the vent guard. Under tiling this is the RADIUS question, not
    /// a region-box question.
    private bool InFluidRange(int3 v)
        => _fluid != null &&
           FluidActiveRegion.WithinWakeRadius(v, _fluid.PlayerVoxel, _fluid.ActiveRadiusVoxels);

    /// Emits a small cube per frame, SCATTERED over a footprint.
    ///
    /// TWO FAILURES FIXED HERE, THE SECOND FOUND BY LOOKING AT THE CAPTURE.
    ///
    /// 1. The original placed ONE voxel per frame, capping a vent at 60
    ///    voxels/second however large its budget was -- raising the budget
    ///    alone would have turned a 160-voxel trickle into a 60,000-voxel
    ///    trickle lasting sixteen minutes.
    ///
    /// 2. Emitting a cube at a FIXED point then choked itself. Air-only
    ///    placement means that once the source neighbourhood fills, nothing
    ///    more is placed and the budget stops draining entirely. The capture
    ///    proved it: 310 edits and 1,714 live slots after 8.7 s, and the 20 s
    ///    frame was pixel-identical to the 8.7 s one. A self-limiting vent is
    ///    not the same thing as a paced one.
    ///
    /// So the emission point WALKS a deterministic lattice across a
    /// _ventSpread footprint each frame. A blocked spot costs one frame, not
    /// the rest of the run, and the pour covers an area -- which is also the
    /// shape that actually exercises the tiled substrate, since a point source
    /// only ever touches one or two tiles.
    ///
    /// Still one voxel at a time through TrySetVoxel, still Air-only, so every
    /// §9.4 residency and refusal path is exercised exactly as before.
    private int _ventPhase;
    private void Emit(ref int budget, int3 cell, byte material)
    {
        if (budget <= 0) return;
        int r = math.max(0, _ventEdge / 2);

        // HASHED, NOT RASTER-SCANNED. A lattice walk in index order lays
        // material down in rows, and the capture showed it: the finished lake
        // had regular parallel stripes of lava and obsidian across its
        // surface, which reads as a comb rather than as chaos. A cheap
        // integer hash scatters consecutive frames across the whole footprint
        // instead, and the same hash still covers it evenly over time.
        uint h = (uint)_ventPhase * 2654435761u + (uint)material * 40503u;
        h ^= h >> 13; h *= 1274126177u; h ^= h >> 16;
        int spanV = math.max(1, 2 * _ventSpread);
        int3 at = cell + new int3((int)(h % (uint)spanV) - _ventSpread, 0,
                                  (int)((h >> 8) % (uint)spanV) - _ventSpread);

        for (int dy = -r; dy <= r; dy++)
        for (int dz = -r; dz <= r; dz++)
        for (int dx = -r; dx <= r; dx++)
        {
            if (budget <= 0) return;
            int3 c = at + new int3(dx, dy, dz);
            if (_store.GetVoxel(c) != Materials.Air) continue;
            if (_edits.TrySetVoxel(c, material)) budget--;
        }
    }

    // =====================================================================
    // Rig surface. Phase6BrushGuard / PlaygroundCapture only.
    // =====================================================================

    internal bool DebugReady => _ready;
    internal int3 DebugArenaCentre => _arenaCentre;
    /// For PlaygroundCapture only: THE edit path, attached to the GPU fluid sim,
    /// so a capture's edits wake fluid exactly as the player's do.
    internal EditService DebugEdits => _edits;
    internal int3 DebugArenaOrigin => _arenaOrigin;
    internal int DebugArenaEdge => _arenaEdge;
    internal string DebugStatus => _status;
    internal FluidGpuSimulation DebugFluid => _fluid;
    internal ChunkStore DebugStore => _store;
    internal byte DebugBrushMaterial => _brushes[_brush];
    internal string DebugBrushName => _brushNames[_brush];
    internal void DebugSetBrush(int i) => SetBrush(i);
    /// -poolprobe (task 39): the tile pool, the last residency pass, and the
    /// V key's water vent at a given cell.
    internal FluidTileMap DebugTiles => _tiles;
    internal FluidTileResidency.Stats DebugLastResidency;
    internal long DebugResidencyRefreshes;
    internal void DebugOpenWaterVent(int3 cell) { _waterSrc = cell; _waterLeft = _waterBudget; }
    internal void DebugCloseVents() => _waterLeft = _sandLeft = _lavaLeft = _otherLeft = 0;

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

    private void DrawVoxelHighlight(Camera cam, int3 v, Color c, float width, int edge = 1)
    {
        Vector3 lo = new Vector3(v.x, v.y, v.z) * 0.1f - Vector3.one * 0.002f;
        Vector3 hi = lo + Vector3.one * (0.1f * edge + 0.004f);

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
        if (DebugHideUi || DevMap.IsOpen) return;
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

            if (_ready && _hasTarget && !InventoryOpen)
            {
                bool inRegion = InFluidRange(_targetVoxel);
                DrawVoxelHighlight(cam, _targetVoxel, new Color(0f, 0f, 0f, 0.55f), 4.5f * k);
                DrawVoxelHighlight(cam, _targetVoxel,
                    inRegion ? new Color(1f, 0.78f, 0.25f, 0.98f)
                             : new Color(0.85f, 0.85f, 0.88f, 0.9f), 2f * k);
                // Survival: where RMB's cube would go (cyan), when holding something.
                if (SurvivalMode && !_inv.IsEmpty(_inv.Selected))
                {
                    int e = SurvivalBuild.EdgeFor(_inv.ItemAt(_inv.Selected), SurvivalBuild.CubeEdges[_edgeIndex]);
                    DrawVoxelHighlight(cam, SurvivalBuild.SnapToGrid(_targetAdjacent, e),
                                       new Color(0.45f, 0.9f, 1f, 0.75f), 1.5f * k, e);
                }
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
        if (SurvivalMode) DrawSurvivalHotbar(vw, vh); else DrawHotbar(vw, vh);
        DrawMoveHud(vw, vh);
        if (!_demoHideState) DrawStatePanel(vh);
        if (SurvivalMode && InventoryOpen && !SettingsMenu.IsOpen) DrawInventoryPanel(vw, vh);
        else if (_showHelp && !SettingsMenu.IsOpen) DrawHelp(vw, vh);   // the menu (F6) sits where help does

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
        Box(new Rect(8, 6, w, 66), new Color(0.04f, 0.06f, 0.09f, 0.88f));
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

    /// Survival's hotbar: the inventory's first 9 slots, counts in voxels.
    /// Movement set A (task 65): the dash cooldown bar, and what the body is
    /// doing (swimming in X / double jump spent), above the hotbar.
    private void DrawMoveHud(float vw, float vh)
    {
        if (!_moveAbilities || _mode != MoveMode.Walk || _player == null || _player.Motor == null) return;
        var m = _player.Motor;
        var c = m.Config;
        const float w = 200f, h = 10f;
        float x = (vw - w) * 0.5f, y = vh - 58f - 58f - 44f;
        float ready = c.dashCooldownS > 0f ? 1f - Mathf.Clamp01(m.DashCooldownS / c.dashCooldownS) : 1f;
        bool airSpent = !m.Grounded && !m.Swimming && m.DashUsedInAir;
        Box(new Rect(x - 1, y - 1, w + 2, h + 2), new Color(0f, 0f, 0f, 0.55f));
        Box(new Rect(x, y, w * ready, h), ready >= 1f && !airSpent ? new Color(0.45f, 0.9f, 1f, 0.9f)
                                                                  : new Color(0.55f, 0.6f, 0.65f, 0.8f));
        string state = m.Swimming ? $"swimming in {MaterialName(m.SwimFluid)}  <b>Space</b> up  <b>Ctrl</b> down"
                     : m.Dashing ? "<b>DASH</b>"
                     : !m.Grounded ? (m.AirJumpsUsed < c.airJumps ? "air: <b>Space</b> double jump" : "double jump spent")
                     : "";
        GUI.Label(new Rect(x - 120, y - 19, w + 240, 18),
                  $"<b>Shift</b> dash {(ready >= 1f && !airSpent ? "ready" : "")}   {state}",
                  Label(11, TextAnchor.MiddleCenter));
    }

    private void DrawSurvivalHotbar(float vw, float vh)
    {
        const float slot = 58f, pad = 6f;
        float total = Inventory.HotbarSize * (slot + pad) - pad;
        float x0 = (vw - total) * 0.5f;
        float y0 = vh - slot - 58f;
        for (int i = 0; i < Inventory.HotbarSize; i++)
            DrawSlot(new Rect(x0 + i * (slot + pad), y0, slot, slot), i, i == _inv.Selected, $"{i + 1}");

        var tier = EditService.Tiers[_tier];
        bool digHot = _act == Act.Dig && _actAge < 0.35f;
        bool placeHot = (_act == Act.Place || _act == Act.Refused) && _actAge < 0.35f;
        string refused = _act == Act.Refused && _actAge < 0.6f ? "  <color=#ff9a9a>REFUSED</color>" : "";
        int e = SurvivalBuild.CubeEdges[_edgeIndex];
        var lr = new Rect(x0 - 250, y0 + 8, 240, 44);
        var rr = new Rect(x0 + total + 10, y0 + 8, 270, 44);
        Box(lr, new Color(0f, 0f, 0f, digHot ? 0.75f : 0.4f));
        Box(rr, new Color(0f, 0f, 0f, placeHot ? 0.75f : 0.4f));
        if (digHot) Frame(lr, new Color(1f, 0.82f, 0.3f, 1f), 2f);
        if (placeHot) Frame(rr, new Color(1f, 0.82f, 0.3f, 1f), 2f);
        GUI.Label(lr, $"<b>LMB — DIG</b> into inventory\n{tier.Name} · {tier.VoxelsPerSecond} vox/s   <b>Z/X</b>",
                  Label(12, TextAnchor.MiddleCenter));
        byte held = _inv.ItemAt(_inv.Selected);
        string what = _inv.IsEmpty(_inv.Selected) ? "empty slot" : Items.Name(held);
        string size = !_inv.IsEmpty(_inv.Selected) && Items.PlacesSingleVoxel(held) ? "1 voxel" : $"{e * 0.1f:0.0} m cube <b>Q</b>";
        GUI.Label(rr, $"<b>RMB — PLACE</b>{refused}\n{what} · {size}   <b>E</b> bag",
                  Label(12, TextAnchor.MiddleCenter));
        GUI.Label(new Rect(x0, y0 - 20, total, 18), "<color=#cfe3f5><b>SURVIVAL</b></color>  <color=#9fb6c9>F6 switches to Creative</color>",
                  Label(11, TextAnchor.MiddleCenter));
    }

    /// One inventory slot: swatch, count, key label. Counts above 9,999 read "10k".
    private void DrawSlot(Rect r, int i, bool sel, string key)
    {
        Box(r, new Color(0f, 0f, 0f, sel ? 0.78f : 0.5f));
        if (!_inv.IsEmpty(i))
        {
            Box(new Rect(r.x + 6, r.y + 6, r.width - 12, r.height - 24), MaterialSwatch(_inv.ItemAt(i)));
            int c = _inv.CountAt(i);
            string n = c >= 10000 ? "10k" : c.ToString();
            GUI.Label(new Rect(r.x, r.y + r.height - 38, r.width - 5, 16), $"<b>{n}</b>", Label(12, TextAnchor.MiddleRight));
            string name = Items.Name(_inv.ItemAt(i));
            if (name.Length > 12) name = name.Substring(0, 11) + "…";
            GUI.Label(new Rect(r.x, r.yMax - 17, r.width, 16), name, Label(9, TextAnchor.MiddleCenter));
        }
        Frame(r, sel ? new Color(1f, 0.82f, 0.3f, 1f) : new Color(1f, 1f, 1f, 0.25f), sel ? 2.5f : 1f);
        if (key != null)
        {
            Box(new Rect(r.x + 2, r.y + 2, 14, 15), new Color(0f, 0f, 0f, 0.7f));
            GUI.Label(new Rect(r.x + 5, r.y + 1, 14, 16), key, Label(11));
        }
    }

    /// The E panel: hotbar row + 3 backpack rows. Click one slot, then another:
    /// they swap (same item merges). Right-click a hotbar slot to select it.
    private void DrawInventoryPanel(float vw, float vh)
    {
        const float slot = 62f, pad = 6f;
        float gridW = 9 * (slot + pad) - pad;
        float w = gridW + 40f, h = 4 * (slot + pad) + 96f;
        var r = new Rect((vw - w) * 0.5f, (vh - h) * 0.5f - 40f, w, h);
        Box(r, new Color(0.03f, 0.05f, 0.08f, 0.95f));
        Frame(r, new Color(1f, 1f, 1f, 0.25f), 1f);
        GUI.Label(new Rect(r.x + 20, r.y + 10, w - 40, 24),
                  "<b>INVENTORY</b>   <color=#9fb6c9>E or Esc closes · click two slots to swap · counts are voxels (0.1 m)</color>", Label(14));

        float x0 = r.x + 20f, yBack = r.y + 44f;
        float yHot = yBack + 3 * (slot + pad) + 14f;
        GUI.Label(new Rect(x0, yHot - 16, gridW, 16), "<color=#9fb6c9>hotbar (1-9)</color>", Label(11));
        Event ev = Event.current;
        for (int i = 0; i < Inventory.SlotCount; i++)
        {
            bool hot = i < Inventory.HotbarSize;
            int col = hot ? i : (i - Inventory.HotbarSize) % 9;
            int row = hot ? 0 : (i - Inventory.HotbarSize) / 9;
            var sr = new Rect(x0 + col * (slot + pad), hot ? yHot : yBack + row * (slot + pad), slot, slot);
            DrawSlot(sr, i, i == _invPick || (hot && i == _inv.Selected && _invPick < 0), hot ? $"{i + 1}" : null);
            if (i == _invPick) Frame(sr, new Color(0.45f, 0.9f, 1f, 1f), 3f);
            if (ev.type == EventType.MouseDown && sr.Contains(ev.mousePosition))
            {
                if (ev.button == 1 && hot) SelectSlot(i);
                else if (_invPick < 0) { if (!_inv.IsEmpty(i)) _invPick = i; }
                else { _inv.Swap(_invPick, i); _invPick = -1; _invDirty = true; }
                ev.Use();
            }
        }

        // Totals by item, so "how much do I have" needs no adding up.
        var totals = new SortedDictionary<string, int>();
        for (int i = 0; i < Inventory.SlotCount; i++)
            if (!_inv.IsEmpty(i))
            {
                string n = Items.Name(_inv.ItemAt(i));
                totals[n] = (totals.TryGetValue(n, out int t) ? t : 0) + _inv.CountAt(i);
            }
        var sb = new StringBuilder("<color=#cfe3f5>");
        foreach (var kv in totals) sb.Append($"{kv.Key} {kv.Value:N0}   ");
        sb.Append("</color>");
        if (_lostTotal > 0) sb.Append($"<color=#ff9a9a>   lost to a full bag: {_lostTotal:N0}</color>");
        var wrap = Label(12);
        wrap.wordWrap = true;
        GUI.Label(new Rect(x0, yHot + slot + 10, gridW, 44), sb.ToString(), wrap);
    }

    /// Live engine state, bottom-left. Everything here is a READOUT of a Phase 6
    /// system, so "is buoyancy doing anything" is answerable at a glance.
    private const int FluidCounterRefreshFrames = 15;
    private int _fluidCounterCountdown;

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

        // ---- SKIMMABLE LAYOUT ----
        // This panel used to be eight lines of prose with the numbers buried
        // mid-sentence, so finding one value meant reading all of them. It is
        // now grouped under headers with the LABEL LEFT-PADDED TO A FIXED
        // WIDTH, so the values form a column the eye can run down. Colour is
        // used consistently and only for state: green = healthy, amber =
        // worth noticing, red = wrong.
        string Row(string k, string v) => $"  <color=#b9cfe4>{k,-13}</color>{v}\n";

        sb.AppendLine();
        sb.AppendLine("<b><color=#ffd479>── EDITS ──────────────────────────────</color></b>");
        sb.Append(Row("dug/s", $"{_dugThisSecond} vox" +
            (_demolition != null && _demolition.InProgress
                ? $"   <color=#ffc64a>bomb draining… {_demolition.VoxelsRemovedSoFar}</color>" : "")));
        if (_edits != null)
            sb.Append(Row("written", $"{_edits.VoxelsWritten:N0}   " +
                $"<color=#b9cfe4>no-op</color> {_edits.EditsNoOp:N0}   " +
                (_edits.EditsRejectedNotResident > 0
                    ? $"<color=#ffc64a>refused {_edits.EditsRejectedNotResident:N0} (not loaded)</color>"
                    : "<color=#8fd98f>0 refused</color>")));
        if (_burstBoxesLeft > 0)
            sb.Append(Row("chaos burst", $"<color=#ffc64a>pouring… {_burstBoxesLeft} boxes left</color>"));
        if (_hasDrop)
            sb.Append(Row("last blast", $"{_lastDrop.TotalVoxels:N0} vox in {_lastDrop.Frames} frames, " +
                $"mostly {MaterialName(_lastDrop.DominantMaterial)}"));
        if (_hasShot)
            sb.Append(Row("last shot", _lastShot.Hit
                ? $"{MaterialName(_lastShot.Material)} at {_lastShot.DistanceM:F1} m"
                : $"no hit in {_lastShot.DistanceM:F0} m"));

        int3 pv = PlayerOrCameraVoxel();
        int3 d = pv - _fluid.PlayerVoxel;
        long dist2 = (long)d.x * d.x + (long)d.y * d.y + (long)d.z * d.z;
        bool inRadius = FluidActiveRegion.WithinWakeRadius(_arenaCentre, pv, _activeRadiusVoxels);

        // ASYNC, NOT THROTTLED-BLOCKING. This used to call ReadSlotCounters
        // (GraphicsBuffer.GetData) every FluidCounterRefreshFrames OnGUI calls.
        // OnGUI runs at least twice a frame, so that was a full CPU<-GPU sync
        // every ~7.5 frames: a ~150 ms hitch plus two catch-up frames near the
        // ground, i.e. the demo's stutter (-flyfast 2026-09-25: frames > 100 ms
        // 646 with the HUD on vs 19 with -nohud). The async request never
        // waits; the number it prints is a few frames old, which is fine for
        // a readout.
        if (--_fluidCounterCountdown <= 0)
        {
            _fluid.RequestSlotCountersAsync();
            _fluidCounterCountdown = FluidCounterRefreshFrames;
        }
        uint liveSlots = _fluid.LastLiveSlots;
        var lastPass = DebugLastResidency;

        sb.AppendLine("<b><color=#ffd479>── FLUID (§7.2 tiles / §7.4 radius) ───</color></b>");
        sb.Append(Row("tiles", $"{_tiles.ResidentTiles} / {_tiles.TileCapacity} resident" +
            (_tiles.AwakeOnly ? " <color=#b9cfe4>(awake fluid only)</color>   " : "   ") +
            (lastPass.TilesRefusedPoolFull > 0
                ? $"<color=#ffc64a>pool FULL: {lastPass.TilesRefusedPoolFull:N0} far tiles left asleep</color> " +
                  $"<color=#b9cfe4>(nearest kept; {_tiles.PoolExhaustionsTotal:N0} refusals since start)</color>"
                : _tiles.PoolExhaustionsTotal > 0
                    ? $"<color=#8fd98f>pool has room</color> <color=#b9cfe4>({_tiles.PoolExhaustionsTotal:N0} refusals since start)</color>"
                    : "<color=#8fd98f>pool has room</color>")));
        // LIVE = high-water minus the free list. The high-water mark alone
        // (what this row printed until task 39) never comes down, so it read
        // "750,000 slots simulating" for the rest of a session once it hit the cap.
        sb.Append(Row("live", $"{liveSlots:N0} slots simulating " +
            $"<color=#b9cfe4>(high-water {_fluid.LastSlotHighWater:N0} / {_fluid.SlotCapacity:N0})</color>   " +
            $"<color=#b9cfe4>active set</color> {_fluid.GpuActiveSetBytes() / 1048576.0:F0} MB " +
            "<color=#b9cfe4>(fixed — does not grow with the radius)</color>"));
        // METRES BESIDE VOXELS, ALWAYS. 1280v and 128v differ by 10x and read
        // alike; "1280v / 128m" makes the unit unmistakable at a glance. A
        // session already mistook one for the other once.
        sb.Append(Row("radius", $"wake {_activeRadiusVoxels}v / {_activeRadiusVoxels * 0.1f:0.#}m   " +
            $"sleep {_fluid.SleepRadiusVoxels}v / {_fluid.SleepRadiusVoxels * 0.1f:0.#}m   " +
            $"<color=#b9cfe4>re-centres</color> {_fluid.RecentresTotal}"));
        sb.Append(Row("basin", inRadius
            ? "<color=#8fd98f>inside the radius — it simulates</color>"
            : "<color=#ffc64a>outside — sleeps as static terrain</color>"));
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
        // SIZED FOR THE WORST CASE, COUNTED NOT GUESSED. Grouping the panel
        // added two section headers and a spacer, and the rows are longer.
        // Worst case is walk mode with every optional row present: player,
        // non-resident block, buoyancy, speed clamp, spacer, EDITS header,
        // dug/s, written, chaos burst, last blast, last shot, FLUID header,
        // tiles, live, radius, basin = 16 lines. At 13pt that is ~272 px; the
        // old 184 px box would have silently clipped the fluid rows, which is
        // the same way this panel lost two lines once before.
        // 0.55 alpha was unreadable over bright snow in the capture; the text
        // needs its own ground, not a tint.
        Box(new Rect(8, vh - 474, 792, 302), new Color(0.04f, 0.06f, 0.09f, 0.88f));
        GUI.Label(new Rect(16, vh - 470, 778, 296), sb.ToString(), Label(13));
    }

    private void DrawHelp(float vw, float vh)
    {
        var sb = new StringBuilder();
        // GROUPED BY WHAT YOU ARE TRYING TO DO, not by key order. The old
        // list mixed movement, tools and fluid on adjacent lines, so finding
        // one binding meant reading all of them.
        sb.AppendLine("<b>CONTROLS</b>   <color=#9fb6c9>(F2 hides this)</color>");
        sb.AppendLine("");
        sb.AppendLine("<b><color=#cfe3f5>MOVE</color></b>");
        sb.AppendLine("  <b>WASD</b> walk    <b>Space</b> jump    <b>Shift</b> fast (fly)    <b>Q/E</b> down/up (fly)");
        if (_moveAbilities)
            sb.AppendLine("  <b><color=#ffd479>Space</color></b> in the air: double jump    <b><color=#ffd479>Shift</color></b> dash (walk)    " +
                          "in water: <b>WASD</b> swim where you look, <b>Space</b> up / jump out, <b>Ctrl</b> down");
        sb.AppendLine("  <b>Tab</b> walk ↔ fly      <b>R</b> respawn on the surface      <b>F</b> go to the basin");
        sb.AppendLine("");
        if (SurvivalMode)
        {
            sb.AppendLine("<b><color=#cfe3f5>BUILD & DIG</color></b>   <color=#9fb6c9>(SURVIVAL -- F6 Game mode switches to Creative's free brushes)</color>");
            sb.AppendLine("  <b>LMB</b> dig: every voxel goes into your inventory      <b>Z / X</b> tool tier");
            sb.AppendLine("  <b>RMB</b> place a cube of the held block (cyan box), costs the cells it fills");
            sb.AppendLine("  <b><color=#ffd479>Q</color></b> cube size 0.1 / 0.2 / 0.4 / 0.8 m   <b><color=#ffd479>E</color></b> inventory (click two slots to swap)   <b>1-9</b> / scroll  hotbar");
        }
        else
        {
            sb.AppendLine("<b><color=#cfe3f5>BUILD & DIG</color></b>   <color=#9fb6c9>(CREATIVE -- F6 Game mode switches to Survival)</color>");
            sb.AppendLine("  <b>LMB</b> dig at the tool's rate        <b>Z / X</b> tool tier");
            sb.AppendLine("  <b>RMB</b> place held item               <b>1-9</b> / scroll  pick item");
        }
        sb.AppendLine("");
        sb.AppendLine("<b><color=#cfe3f5>FLUID & DESTRUCTION</color></b>");
        sb.AppendLine("  <b><color=#ffd479>G</color></b>   <b>CHAOS BURST</b> — ~51,000 voxels of water/sand/lava at once");
        sb.AppendLine("  <b>V</b>   open a vent above the crosshair      <b>0</b>  close all vents");
        sb.AppendLine("  <b>B</b>   bomb at the crosshair (§8.5, frame-split)");
        sb.AppendLine("  <b>T</b>   shoot a projectile (§8.4 DDA trace)");
        sb.AppendLine("");
        sb.AppendLine("<b><color=#cfe3f5>LIGHT & TIME</color></b>   <color=#9fb6c9>(Phase 7)</color>");
        sb.AppendLine("  <b><color=#ffd479>N</color></b> golden hour   <b>M</b> noon   <b>L</b> midnight   <b>,</b> / <b>.</b> scrub (hold)   <b>P</b> pause the clock");
        sb.AppendLine(SurvivalMode ? "  <b>torch</b>: starter kit, hotbar slot 1 -- RMB places one; LMB digs it back"
                                   : "  <b>7</b>   place a <b>torch</b> (hotbar slot 7, then RMB)      LMB digs it out again");
        sb.AppendLine("  <b>J</b>   shadow quality 0-3 — full soft / half-res distant / hard / off (§6.5)");
        sb.AppendLine("  <b>K</b>   hard ↔ soft shadows (k_soft toggle)");
        sb.AppendLine("  <b>Y</b>   view: beauty → sun visibility → shadow step heat");
        sb.AppendLine("  <b><color=#ffd479>[</color></b> / <b><color=#ffd479>]</color></b>   cycle LIGHT PRESET — Warm / Atmospheric / HighContrast / Muted / Shipped");
        sb.AppendLine("");
        sb.AppendLine("<b><color=#cfe3f5>VIEW</color></b>");
        sb.AppendLine("  <b>F1</b> perf overlay      <b>F2</b> these controls      <b>ESC</b> release the mouse      <b>O</b> dev map      <b>F3</b> render preset      <b>F4</b> minimap");
        sb.AppendLine("  <b><color=#ffd479>F6</color></b> SETTINGS — game mode, shadows, upscaler, resolution, overlay, minimap (saved)");
        sb.AppendLine("");
        sb.Append("<color=#9fb6c9>Fluid simulates only inside §7.4's wake radius (shown in the state " +
                  "panel in both voxels and metres). The crosshair box turns grey outside it, and " +
                  "placing water/sand/lava there is refused rather than left frozen.\n\n" +
                  "Lighting tunes LIVE from LightingConfig.json in the same folder as " +
                  "PlayerConfig.json — edit, save, and the NEXT FRAME is different. k_soft, the " +
                  "torch falloff, the ambient curve, the sun colour ramp and the day length are " +
                  "all in there, and a typo is ignored rather than fatal.</color>");

        // CENTRED, not tucked into a corner: at 6 hotbar slots the bottom-right
        // is already occupied by the RMB panel, and a controls list that
        // overlaps the thing it is describing is worse than useless. It is
        // toggled, so covering the view while open is intended.
        float w = 760f, h = 512f;   // grew again with Phase 7's LIGHT & TIME group, F6 (task 58), Survival (task 64)
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
