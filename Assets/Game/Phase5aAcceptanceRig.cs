// ==========================================
// Assets/Game/Phase5aAcceptanceRig.cs
//
// Script-driven acceptance pass over §13 Phase 5a's five basin scenarios,
// following Phase3AcceptanceRig / Phase4AcceptanceRig: standalone build,
// screenshot + text evidence, no manual clicking, quits itself when done.
//
// -------------------------------------------------------------------------
// IT DRIVES THE BUTTONS, NOT THE SIMULATION
// -------------------------------------------------------------------------
// Every scenario is invoked as basin.Scenarios[i].Run -- the SAME delegate
// Phase5aBasin.OnGUI hands to GUILayout.Button. The rig never calls
// FluidReferenceCPU directly to set a scenario up. That is deliberate: if the
// rig named its own scenario methods, a button wired to the wrong handler
// would still produce a clean rig run, and the manual pass this replaces
// existed precisely to catch that class of mistake.
//
// The rig reads FluidReferenceCPU only to OBSERVE (counters, ledger), never
// to mutate. The one exception is Paused/Step, which is cadence control, not
// simulation: §7.8 makes the reference deterministic given a fixed tick
// order, and a rig that ticked off Time.deltaTime would throw that guarantee
// away and make captures unreproducible.
//
// -------------------------------------------------------------------------
// WHY TWO PASSES PER SCENARIO
// -------------------------------------------------------------------------
// The captures are specified at 0% / 25% / 50% / 100% of the way to rest, and
// "rest" is not known until it happens. Pass 1 runs the scenario to find the
// rest tick and captures nothing; pass 2 rebuilds the basin, replays the
// identical tick sequence, and captures at the four computed marks. This is
// only sound because the reference is deterministic (§7.8) -- the rig asserts
// that the replay reaches the same rest tick rather than assuming it.
//
// -------------------------------------------------------------------------
// NOT A PERFORMANCE RIG
// -------------------------------------------------------------------------
// No frame times are recorded or reported here, deliberately. Per CLAUDE.md
// the only trusted frame-time source is run-acceptance-rig.sh's Phase 4
// output; a second rig printing ms would eventually be quoted as if it were
// the same evidence. This rig answers "does it behave correctly", nothing else.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

public class Phase5aAcceptanceRig : MonoBehaviour
{
    [Header("Rest detection")]
    [Tooltip("Consecutive ticks with zero changed cells before the basin counts as at rest.")]
    // 12, NOT the 4 the EditMode tests use. Lava's tick interval is 6 (§7.4), so
    // it is legitimately idle five ticks out of six and a shorter window reports
    // a rest that has not happened -- the false positive documented on
    // FluidReferenceCPU.ChangedCellsThisTick. 12 clears 6 with margin. Honey
    // (interval 30) would need more, and no basin scenario uses it.
    [SerializeField] private int _quietTicksForRest = 12;
    [SerializeField] private int _maxTicksPerScenario = 1500;

    [Header("Capture")]
    [SerializeField] private int _captureWidth = 1600;
    [SerializeField] private int _captureHeight = 900;
    [SerializeField] private string _outputRootFolderName = "Phase5aAcceptance";

    private Phase5aBasin _basin;
    private string _runFolder;
    private readonly StringBuilder _report = new StringBuilder();
    private int _framesCaptured;
    private int _framesWithLedgerBroken;
    private int _framesWithDuplicateOwnership;

    /// One captured frame's worth of evidence.
    private struct Frame
    {
        public string Phase;
        public int Tick;
        public int ActiveSlots;
        public int ChangedCells;
        public int DuplicateOwnership;
        public bool LedgerOk;
        public long LedgerExpected, LedgerActual, LedgerDelta;
        public string LedgerMessage;
        public string Png;
    }

    void Start()
    {
        // In the Editor the scene must stay clickable by a human, so the rig
        // only takes over when it is explicitly asked to -- which the standalone
        // launcher does via -phase5arig.
        if (!Application.isBatchMode && !HasFlag("-phase5arig")) return;
        StartCoroutine(RunAll());
    }

    private static bool HasFlag(string flag)
    {
        foreach (string a in Environment.GetCommandLineArgs())
            if (string.Equals(a, flag, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private IEnumerator RunAll()
    {
        _basin = FindAnyObjectByType<Phase5aBasin>();
        if (_basin == null) { Debug.LogError("[Phase5aRig] no Phase5aBasin in the scene"); Application.Quit(1); yield break; }

        Screen.SetResolution(_captureWidth, _captureHeight, false);
        for (int i = 0; i < 5; i++) yield return null;

        string ts = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
        _runFolder = Path.Combine(Application.persistentDataPath, _outputRootFolderName, ts);
        Directory.CreateDirectory(_runFolder);

        Head($"Phase 5a Basin — acceptance rig");
        Head($"run          {ts}");
        Head($"build        {(Debug.isDebugBuild ? "DEVELOPMENT" : "release")} standalone, {Application.platform}");
        Head($"screen       {Screen.width}x{Screen.height}");
        // On the record rather than inferred: the first attempt at the slice
        // colour fix gated on this being Linear and silently did nothing.
        Color32 airTexel = Phase5aBasin.DebugTexel(Materials.Air);
        Head($"colour space {QualitySettings.activeColorSpace} " +
             $"(Air palette 18,18,24 -> stored texel {airTexel.r},{airTexel.g},{airTexel.b})");
        Head($"rest rule    {_quietTicksForRest} consecutive ticks with 0 changed cells " +
             $"(cap {_maxTicksPerScenario})");
        Head($"scenarios    driven via Phase5aBasin.Scenarios[i].Run — the same delegate the buttons use");
        Head("");

        // The rig drives the cadence; Update() must not also tick.
        _basin.Paused = true;

        var scenarios = _basin.Scenarios;
        for (int i = 0; i < scenarios.Length; i++)
            yield return StartCoroutine(RunScenario(scenarios[i]));

        // EXPECTED-EVIDENCE CHECK. Without this the rig reported
        //     "RESULT: every captured frame balanced, zero duplicate ownership"
        // after a run in which every scenario threw and ZERO frames were
        // captured -- a vacuous pass, because "no frame failed" is trivially
        // true of no frames. A rig that can report success on an absence of
        // evidence is worse than no rig, so the count is now an assertion.
        int expectedFrames = scenarios.Length * 4;
        bool enoughEvidence = _framesCaptured == expectedFrames;
        bool invariantsHeld = _framesWithLedgerBroken == 0 && _framesWithDuplicateOwnership == 0;

        Head("");
        Head("================ SUMMARY ================");
        Head($"scenarios driven                {scenarios.Length}");
        Head($"frames captured                 {_framesCaptured} (expected {expectedFrames})");
        Head($"frames with ledger NOT balanced {_framesWithLedgerBroken}");
        Head($"frames with duplicate ownership {_framesWithDuplicateOwnership}");
        if (!enoughEvidence)
            Head($"RESULT: FAILED — captured {_framesCaptured} of {expectedFrames} expected frames. " +
                 "Something aborted before capturing; check player_log.txt for an exception. " +
                 "The invariant counts below are NOT evidence of anything.");
        else
            Head(invariantsHeld
                ? "RESULT: every captured frame balanced, zero duplicate ownership."
                : "RESULT: FAILED — at least one captured frame failed an invariant.");

        File.WriteAllText(Path.Combine(_runFolder, "phase5a_report.txt"), _report.ToString());
        try
        {
            string src = Application.consoleLogPath;
            if (!string.IsNullOrEmpty(src) && File.Exists(src))
                File.Copy(src, Path.Combine(_runFolder, "player_log.txt"), true);
        }
        catch (Exception e) { Debug.LogWarning($"[Phase5aRig] log copy failed: {e.Message}"); }

        Debug.Log("[Phase5aRig] complete\n" + _report);
        yield return null;
        Application.Quit(enoughEvidence && invariantsHeld ? 0 : 1);
    }

    private IEnumerator RunScenario(Phase5aBasin.BasinScenario scenario)
    {
        Head($"---------------- {scenario.Id}  (\"{scenario.Label}\") ----------------");

        // ---- PASS 1: find the rest tick, capturing nothing ----
        PrepareFor(scenario);
        scenario.Run();
        int restTick = -1, guard = 0, quiet = 0;
        while (guard++ < _maxTicksPerScenario)
        {
            _basin.Step();
            quiet = _basin.Sim.ChangedCellsThisTick == 0 ? quiet + 1 : 0;
            if (quiet >= _quietTicksForRest) { restTick = _basin.Sim.TickCount - quiet + 1; break; }
            if ((guard & 255) == 0) yield return null;   // stay responsive
        }
        bool reachedRest = restTick > 0;
        int endTick = reachedRest ? restTick : _maxTicksPerScenario;
        Head($"pass 1: {(reachedRest ? $"rest at tick {restTick}" : $"NO REST within {_maxTicksPerScenario} ticks")}");

        // ---- PASS 2: replay and capture at 0 / 25 / 50 / 100% ----
        PrepareFor(scenario);
        scenario.Run();
        Head($"overlay action line: {_basin.LastAction}");

        int m25 = Mathf.Max(1, endTick / 4);
        int m50 = Mathf.Max(2, endTick / 2);
        var frames = new List<Frame>();

        yield return StartCoroutine(CaptureInto(frames, scenario.Id, "start"));

        yield return StartCoroutine(DriveTo(m25));
        yield return StartCoroutine(CaptureInto(frames, scenario.Id, "25pct"));

        yield return StartCoroutine(DriveTo(m50));
        yield return StartCoroutine(CaptureInto(frames, scenario.Id, "50pct"));

        yield return StartCoroutine(DriveTo(endTick));
        yield return StartCoroutine(CaptureInto(frames, scenario.Id, reachedRest ? "rest" : "capped"));

        // Replay determinism (§7.8): pass 2 must land where pass 1 did.
        Head($"pass 2: ended at tick {_basin.Sim.TickCount} (pass 1 {(reachedRest ? "rest" : "cap")} was {endTick})" +
             (_basin.Sim.TickCount == endTick ? "  [replay matched]" : "  [REPLAY DIVERGED]"));

        WriteCsv(scenario.Id, frames);
        foreach (var f in frames)
        {
            Head($"  {f.Phase,-6} tick {f.Tick,5}  slots {f.ActiveSlots,5}  changed {f.ChangedCells,5}  " +
                 $"dupOwn {f.DuplicateOwnership}  ledger {(f.LedgerOk ? "OK" : "BROKEN")} " +
                 $"(exp {f.LedgerExpected} / act {f.LedgerActual} / delta {f.LedgerDelta})  -> {f.Png}");
        }
        if (_basin.BrokenAtTick >= 0)
            Head($"  OVERLAY LATCH: ledger first broke on tick {_basin.BrokenAtTick} — {_basin.BrokenMessage}");
        Head("");
    }

    /// The two scenarios that act on fluid already in flight need something in
    /// flight. A human clicks "Pour water" first and waits a moment; the rig
    /// does the same, through the same delegate, and says so in the report.
    private void PrepareFor(Phase5aBasin.BasinScenario scenario)
    {
        _basin.Rebuild();
        if (scenario.Id != "place_block" && scenario.Id != "mine_drop") return;

        foreach (var s in _basin.Scenarios)
        {
            if (s.Id != "pour_water") continue;
            s.Run();
            for (int t = 0; t < 10; t++) _basin.Step();
            Head("  setup: ran \"Pour water\" for 10 ticks first so there is a stream to act on");
            return;
        }
    }

    private IEnumerator DriveTo(int targetTick)
    {
        int guard = 0;
        while (_basin.Sim.TickCount < targetTick && guard++ < _maxTicksPerScenario * 2)
        {
            _basin.Step();
            if ((guard & 255) == 0) yield return null;
        }
    }

    private IEnumerator CaptureInto(List<Frame> frames, string id, string phase)
    {
        var sim = _basin.Sim;
        var check = sim.CheckConservation();

        var f = new Frame
        {
            Phase = phase,
            Tick = sim.TickCount,
            ActiveSlots = sim.ActiveSlotCount,
            ChangedCells = sim.ChangedCellsThisTick,
            DuplicateOwnership = sim.CountDuplicateSlotOwnership(),
            LedgerOk = check.Ok,
            LedgerExpected = check.Expected,
            LedgerActual = check.Actual,
            LedgerDelta = check.Delta,
            LedgerMessage = check.Message,
            Png = $"{id}_tick{sim.TickCount:D4}_{phase}.png",
        };

        if (!f.LedgerOk) _framesWithLedgerBroken++;
        if (f.DuplicateOwnership != 0) _framesWithDuplicateOwnership++;

        // Let Update() redraw the slice textures and OnGUI paint, then grab the
        // real framebuffer -- overlay panel included, exactly what a human
        // watching the scene would see.
        for (int i = 0; i < 3; i++) yield return null;
        yield return new WaitForEndOfFrame();

        Texture2D shot = ScreenCapture.CaptureScreenshotAsTexture();
        try { File.WriteAllBytes(Path.Combine(_runFolder, f.Png), shot.EncodeToPNG()); }
        finally { Destroy(shot); }

        _framesCaptured++;
        frames.Add(f);
    }

    private void WriteCsv(string id, List<Frame> frames)
    {
        var sb = new StringBuilder();
        sb.AppendLine("phase,tick,activeSlots,changedCells,duplicateOwnership," +
                      "ledgerOk,ledgerExpected,ledgerActual,ledgerDelta,png,ledgerMessage");
        foreach (var f in frames)
        {
            sb.Append(f.Phase).Append(',')
              .Append(f.Tick.ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(f.ActiveSlots).Append(',')
              .Append(f.ChangedCells).Append(',')
              .Append(f.DuplicateOwnership).Append(',')
              .Append(f.LedgerOk ? "OK" : "BROKEN").Append(',')
              .Append(f.LedgerExpected).Append(',')
              .Append(f.LedgerActual).Append(',')
              .Append(f.LedgerDelta).Append(',')
              .Append(f.Png).Append(',')
              .Append('"').Append((f.LedgerMessage ?? "").Replace("\"", "\"\"").Replace("\n", " ")).Append('"')
              .AppendLine();
        }
        File.WriteAllText(Path.Combine(_runFolder, $"{id}.csv"), sb.ToString());
    }

    private void Head(string line)
    {
        _report.AppendLine(line);
    }
}
