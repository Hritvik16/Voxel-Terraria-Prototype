// ==========================================
// Assets/Game/Phase5aBasin.cs
//
// §13 Phase 5a scene: "Phase5a_Basin -- enclosed basin; buttons: pour water,
// drop sand column, lava vent, place-block-into-stream, mine-a-falling-drop.
// Renders via a debug overlay reading FluidReferenceCPU directly (not the
// shipped renderer, which comes online in 5b)."
//
// It reads FluidReferenceCPU directly and draws two cross-sections into a
// Texture2D -- one voxel per pixel, point-filtered. That is deliberately NOT
// the raymarcher: §13 puts the shipped renderer on the fluid path in 5b, and
// wiring it here would put an untrusted second thing in a phase that is
// supposed to have exactly one (§0.1 invariant 5).
//
// NOTHING HERE IS A PERFORMANCE HARNESS. There is no frame-time reporting in
// this file on purpose. Per CLAUDE.md's measurement discipline the only
// trusted frame-time source is run-acceptance-rig.sh's standalone output, and
// a Play-mode overlay that printed ms would be read as evidence sooner or
// later. What it does report is CORRECTNESS state: the conservation ledger,
// live, every tick.

using System.Text;
using Unity.Mathematics;
using UnityEngine;

public class Phase5aBasin : MonoBehaviour
{
    [Header("Sandbox (bricks -- each is 8 voxels; powers of two only)")]
    [SerializeField] private int _bricksX = 8;
    [SerializeField] private int _bricksY = 4;
    [SerializeField] private int _bricksZ = 8;

    [Header("Simulation")]
    [Tooltip("CA ticks per second. The sim is frame-rate independent; this is " +
             "just how fast you want to watch it.")]
    [SerializeField] private float _ticksPerSecond = 20f;
    [SerializeField] private bool _paused;

    [Header("Sources")]
    [Tooltip("Drops a continuous source emits before it shuts itself off.")]
    [SerializeField] private int _sourceBudget = 250;

    /// One basin scenario: the label its button shows and the action that
    /// button runs. THIS IS THE SINGLE BINDING -- OnGUI renders a button per
    /// entry and Phase5aAcceptanceRig invokes the same entry's Run. There is no
    /// second copy of the mapping for the two to disagree about, which is the
    /// point: if the rig could name its own methods, a mis-wired button would
    /// stay invisible to the rig, which is exactly the bug this scene exists to
    /// catch.
    public readonly struct BasinScenario
    {
        public readonly string Id;
        public readonly string Label;
        public readonly System.Action Run;
        public BasinScenario(string id, string label, System.Action run)
        {
            Id = id; Label = label; Run = run;
        }
    }

    private BasinScenario[] _scenarios;

    /// §13's five scenarios, in the order the buttons appear.
    public BasinScenario[] Scenarios => _scenarios ??= new[]
    {
        new BasinScenario("pour_water",   "Pour water",              PourWater),
        new BasinScenario("sand_column",  "Drop sand column",        DropSandColumn),
        new BasinScenario("lava_vent",    "Lava vent",               LavaVent),
        new BasinScenario("place_block",  "Place block into stream", PlaceBlockIntoStream),
        new BasinScenario("mine_drop",    "Mine a falling drop",     MineAFallingDrop),
    };

    // ---- Surface the acceptance rig drives. Everything here is a view onto
    // ---- state the overlay already shows; the rig adds no simulation of its own.
    public FluidReferenceCPU Sim => _sim;
    public string LastAction => _lastAction;
    /// Tick the conservation ledger first went out of balance, or -1.
    public int BrokenAtTick => _brokenAtTick;
    public string BrokenMessage => _brokenMessage;
    public bool Paused { get => _paused; set => _paused = value; }
    public int SliceZ { get => _sliceZ; set => _sliceZ = value; }
    public int SliceY { get => _sliceY; set => _sliceY = value; }

    /// Rebuild the basin from scratch. The rig calls this between scenarios so
    /// each one starts from the same state a freshly-launched scene would have.
    public void Rebuild() => BuildBasin();

    /// Advance one CA tick, emitting from any open source first -- exactly what
    /// the unpaused Update loop does per tick. Public so the rig can drive the
    /// cadence deterministically instead of depending on frame timing (§7.8:
    /// the reference is deterministic given a fixed tick order, and a rig that
    /// ticked off Time.deltaTime would throw that away).
    public void Step() => StepOnce();

    private FluidReferenceCPU _sim;
    private Texture2D _sideView;     // XY cross-section at _sliceZ
    private Texture2D _topView;      // XZ cross-section at _sliceY
    private Color32[] _sidePixels;
    private Color32[] _topPixels;

    private int _sliceZ, _sliceY = 1;
    private double _tickAccumulator;
    private bool _stepOnce;

    // Active continuous sources: (cell, material, remaining)
    private int3 _waterSource, _lavaSource;
    private int _waterRemaining, _lavaRemaining;

    private string _lastAction = "";
    private FluidLedgerCheck _lastCheck;
    private int _brokenAtTick = -1;
    private string _brokenMessage;

    private int InteriorMinX => 1;
    private int InteriorMaxX => _sim.SizeXVoxels - 2;
    private int InteriorMinZ => 1;
    private int InteriorMaxZ => _sim.SizeZVoxels - 2;

    void Start() => BuildBasin();

    void BuildBasin()
    {
        _sim = new FluidReferenceCPU(_bricksX, _bricksY, _bricksZ);
        int sx = _sim.SizeXVoxels, sy = _sim.SizeYVoxels, sz = _sim.SizeZVoxels;

        // §13's "enclosed basin": stone floor, stone walls all the way up, open
        // top. Enclosed matters -- water leaving over an open edge is not the
        // scenario the acceptance scenarios describe.
        _sim.FillBox(new int3(0, 0, 0), new int3(sx - 1, 0, sz - 1), Materials.Stone);
        for (int y = 1; y < sy; y++)
        {
            _sim.FillBox(new int3(0, y, 0), new int3(sx - 1, y, 0), Materials.Stone);
            _sim.FillBox(new int3(0, y, sz - 1), new int3(sx - 1, y, sz - 1), Materials.Stone);
            _sim.FillBox(new int3(0, y, 0), new int3(0, y, sz - 1), Materials.Stone);
            _sim.FillBox(new int3(sx - 1, y, 0), new int3(sx - 1, y, sz - 1), Materials.Stone);
        }

        // The whole basin sits inside the near-player active radius, so §7.4's
        // scope never silently freezes half the demo. Press "Shrink radius" to
        // exercise force-demotion on purpose.
        _sim.PlayerVoxel = new int3(sx >> 1, sy >> 1, sz >> 1);
        _sim.ActiveRadiusVoxels = EngineConfig.FLUID_ACTIVE_RADIUS_VOXELS;
        _sim.ResetLedgerToCurrentState();

        _sliceZ = sz >> 1;
        _sliceY = 1;
        _waterRemaining = _lavaRemaining = 0;
        _brokenAtTick = -1;
        _brokenMessage = null;
        _lastAction = "basin rebuilt";

        _sideView = NewTex(sx, sy, ref _sidePixels);
        _topView = NewTex(sx, sz, ref _topPixels);
        _lastCheck = _sim.CheckConservation();
    }

    private static Texture2D NewTex(int w, int h, ref Color32[] buf)
    {
        var t = new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
        };
        buf = new Color32[w * h];
        return t;
    }

    void Update()
    {
        if (_sim == null) return;

        double interval = 1.0 / Mathf.Max(1f, _ticksPerSecond);
        if (_paused)
        {
            if (!_stepOnce) { RedrawSlices(); return; }
            _stepOnce = false;
            StepOnce();
        }
        else
        {
            _tickAccumulator += Time.unscaledDeltaTime;
            // Bounded catch-up: never let a hitch turn into a hundred-tick
            // burst that looks like the sim exploded.
            int budget = 8;
            while (_tickAccumulator >= interval && budget-- > 0)
            {
                _tickAccumulator -= interval;
                StepOnce();
            }
            if (_tickAccumulator > interval) _tickAccumulator = 0;
        }

        RedrawSlices();
    }

    private void StepOnce()
    {
        EmitSources();
        _sim.Tick();

        _lastCheck = _sim.CheckConservation();
        if (!_lastCheck.Ok && _brokenAtTick < 0)
        {
            // §13: the counter's whole job is to pin the tick. Latch the FIRST
            // break so it stays on screen instead of being scrolled away by
            // whatever the sim does afterwards.
            _brokenAtTick = _lastCheck.Tick;
            _brokenMessage = _lastCheck.Message;
            Debug.LogError("[Phase5a] " + _lastCheck.Message);
        }
    }

    private void EmitSources()
    {
        if (_waterRemaining > 0 && _sim.GetVoxel(_waterSource) == Materials.Air)
        {
            _sim.EditVoxel(_waterSource, Materials.Water);
            _waterRemaining--;
        }
        if (_lavaRemaining > 0 && _sim.GetVoxel(_lavaSource) == Materials.Air)
        {
            _sim.EditVoxel(_lavaSource, Materials.Lava);
            _lavaRemaining--;
        }
    }

    // =====================================================================
    // §13's five scenario buttons
    // =====================================================================

    private void PourWater()
    {
        _waterSource = new int3(_sim.SizeXVoxels / 3, _sim.SizeYVoxels - 2, _sim.SizeZVoxels >> 1);
        _waterRemaining = _sourceBudget;
        _lastAction = $"pouring water at {_waterSource} ({_sourceBudget} drops)";
    }

    private void DropSandColumn()
    {
        int x = _sim.SizeXVoxels >> 1, z = _sim.SizeZVoxels >> 1;
        int placed = 0;
        for (int y = _sim.SizeYVoxels - 2; y > _sim.SizeYVoxels - 2 - 24; y--)
        {
            if (_sim.GetVoxel(x, y, z) != Materials.Air) continue;
            _sim.EditVoxel(x, y, z, Materials.Sand);
            placed++;
        }
        _lastAction = $"dropped a {placed}-grain sand column at x={x} z={z}";
    }

    private void LavaVent()
    {
        _lavaSource = new int3(2 * _sim.SizeXVoxels / 3, _sim.SizeYVoxels - 2, _sim.SizeZVoxels >> 1);
        _lavaRemaining = _sourceBudget / 4;
        _lastAction = $"lava vent open at {_lavaSource} (ticks every " +
                      $"{MaterialRules.TickInterval(Materials.Lava)} -- §7.4)";
    }

    /// §13: "Place a block into a falling stream repeatedly -> no lost drops."
    private void PlaceBlockIntoStream()
    {
        if (!TryFindAirborneDrop(out int3 at)) { _lastAction = "no falling drop to block"; return; }
        int3 under = new int3(at.x, at.y - 1, at.z);
        _sim.EditVoxel(under, Materials.Stone);
        _lastAction = $"placed stone at {under}, directly under a falling drop";
    }

    /// §13: "Mine a falling drop -> orphan self-frees next tick."
    private void MineAFallingDrop()
    {
        if (!TryFindAirborneDrop(out int3 at)) { _lastAction = "no falling drop to mine"; return; }
        byte m = _sim.GetVoxel(at);
        _sim.EditVoxel(at, Materials.Air);
        _lastAction = $"mined material {m} at {at} -- watch orphans-freed go to 1 next tick";
    }

    /// A mobile voxel with Air under it, i.e. one that is actually mid-fall.
    private bool TryFindAirborneDrop(out int3 found)
    {
        for (int y = 1; y < _sim.SizeYVoxels; y++)
        for (int z = InteriorMinZ; z <= InteriorMaxZ; z++)
        for (int x = InteriorMinX; x <= InteriorMaxX; x++)
        {
            if (!MaterialRules.IsMobile(_sim.GetVoxel(x, y, z))) continue;
            if (_sim.GetVoxel(x, y - 1, z) != Materials.Air) continue;
            found = new int3(x, y, z);
            return true;
        }
        found = default;
        return false;
    }

    // =====================================================================
    // Debug overlay
    // =====================================================================

    private static Color32 ColorOf(byte material)
    {
        switch (material)
        {
            case Materials.Air:      return new Color32(18, 18, 24, 255);
            case Materials.Water:    return new Color32(48, 122, 224, 255);
            case Materials.Lava:     return new Color32(226, 88, 24, 255);
            case Materials.Honey:    return new Color32(226, 176, 40, 255);
            case Materials.Sand:     return new Color32(206, 184, 126, 255);
            case Materials.Obsidian: return new Color32(58, 40, 78, 255);
            case Materials.Stone:    return new Color32(104, 104, 112, 255);
            default:                 return new Color32(160, 60, 160, 255);
        }
    }

    private void RedrawSlices()
    {
        int sx = _sim.SizeXVoxels, sy = _sim.SizeYVoxels, sz = _sim.SizeZVoxels;
        _sliceZ = Mathf.Clamp(_sliceZ, 0, sz - 1);
        _sliceY = Mathf.Clamp(_sliceY, 0, sy - 1);

        for (int y = 0; y < sy; y++)
        for (int x = 0; x < sx; x++)
            _sidePixels[y * sx + x] = ColorOf(_sim.GetVoxel(x, y, _sliceZ));
        _sideView.SetPixels32(_sidePixels);
        _sideView.Apply(false);

        for (int z = 0; z < sz; z++)
        for (int x = 0; x < sx; x++)
            _topPixels[z * sx + x] = ColorOf(_sim.GetVoxel(x, _sliceY, z));
        _topView.SetPixels32(_topPixels);
        _topView.Apply(false);
    }

    void OnGUI()
    {
        if (_sim == null) return;

        const int PANEL = 330;
        GUILayout.BeginArea(new Rect(8, 8, PANEL, Screen.height - 16), GUI.skin.box);

        GUILayout.Label("<b>Phase 5a — Basin (CPU fluid reference)</b>",
            new GUIStyle(GUI.skin.label) { richText = true });

        // ---- Conservation, the thing this scene exists to show ----
        var conservationStyle = new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true };
        if (_brokenAtTick >= 0)
            GUILayout.Label($"<color=#ff5555><b>CONSERVATION BROKE ON TICK {_brokenAtTick}</b></color>\n{_brokenMessage}",
                conservationStyle);
        else
            GUILayout.Label($"<color=#66dd66><b>conservation OK</b></color> — {_lastCheck.Actual} mobile bytes, delta 0",
                conservationStyle);

        var sb = new StringBuilder();
        sb.Append("tick ").Append(_sim.TickCount).Append('\n');
        sb.Append("mobile bytes  ").Append(_lastCheck.Actual)
          .Append("   (ledger expects ").Append(_lastCheck.Expected).Append(")\n");
        sb.Append("active slots  ").Append(_sim.ActiveSlotCount)
          .Append(" / ").Append(_sim.SlotCapacity).Append('\n');
        sb.Append("moves         ").Append(_sim.MovesThisTick)
          .Append("  (lateral ").Append(_sim.LateralMovesThisTick).Append(")\n");
        sb.Append("changed cells ").Append(_sim.ChangedCellsThisTick)
          .Append(_sim.ChangedCellsThisTick == 0 ? "   <at rest>" : "").Append('\n');
        sb.Append("orphans       ").Append(_sim.OrphansFreedThisTick)
          .Append(" this tick, ").Append(_sim.OrphansFreedTotal).Append(" total\n");
        sb.Append("slept / demoted ").Append(_sim.SleepFreedThisTick)
          .Append(" / ").Append(_sim.ForceDemotedThisTick).Append('\n');
        sb.Append("reactions     ").Append(_sim.ReactionsThisTick).Append('\n');
        sb.Append("ledger        +").Append(_sim.Ledger.ExternalAdded)
          .Append(" placed, -").Append(_sim.Ledger.ExternalRemoved)
          .Append(" removed, -").Append(_sim.Ledger.ReactionConsumed).Append(" reacted\n");
        sb.Append("promotions refused ").Append(_sim.PromotionsFailedTotal).Append("  (§7.7)");
        GUILayout.Label(sb.ToString());

        GUILayout.Space(6);
        GUILayout.Label("<b>Scenarios (§13)</b>", new GUIStyle(GUI.skin.label) { richText = true });
        foreach (var scenario in Scenarios)
            if (GUILayout.Button(scenario.Label)) scenario.Run();

        GUILayout.Space(6);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button(_paused ? "Resume" : "Pause")) _paused = !_paused;
        GUI.enabled = _paused;
        if (GUILayout.Button("Step 1 tick")) _stepOnce = true;
        GUI.enabled = true;
        if (GUILayout.Button("Reset basin")) BuildBasin();
        GUILayout.EndHorizontal();

        GUILayout.Label($"ticks/sec  {_ticksPerSecond:F0}");
        _ticksPerSecond = GUILayout.HorizontalSlider(_ticksPerSecond, 1f, 60f);

        GUILayout.Label($"side view: Z = {_sliceZ}");
        _sliceZ = Mathf.RoundToInt(GUILayout.HorizontalSlider(_sliceZ, 0, _sim.SizeZVoxels - 1));
        GUILayout.Label($"top view:  Y = {_sliceY}");
        _sliceY = Mathf.RoundToInt(GUILayout.HorizontalSlider(_sliceY, 0, _sim.SizeYVoxels - 1));

        GUILayout.Space(4);
        GUILayout.Label($"<i>{_lastAction}</i>", new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true });
        GUILayout.EndArea();

        // ---- The slice views ----
        float left = PANEL + 24;
        float avail = Screen.width - left - 16;
        float viewW = Mathf.Min(avail * 0.5f - 8, Screen.height * 0.8f);
        DrawSlice(new Rect(left, 30, viewW, viewW), _sideView,
                  $"side (XY) at Z={_sliceZ} — up is +Y");
        DrawSlice(new Rect(left + viewW + 16, 30, viewW, viewW), _topView,
                  $"top (XZ) at Y={_sliceY}");
    }

    private static void DrawSlice(Rect r, Texture2D tex, string caption)
    {
        GUI.Label(new Rect(r.x, r.y - 20, r.width, 20), caption);
        // Textures are bottom-up and the GUI is top-down, so flip vertically to
        // put +Y at the top of the side view where a person expects it.
        GUIUtility.ScaleAroundPivot(new Vector2(1, -1), r.center);
        GUI.DrawTexture(r, tex, ScaleMode.ScaleToFit);
        GUIUtility.ScaleAroundPivot(new Vector2(1, -1), r.center);
    }
}
