// ==========================================
// Assets/Game/Phase5bValidationRig.cs
//
// Phase 5b validation: runs 5a's five scenarios on the GPU path with
// FluidReferenceCPU running alongside on identical inputs, compares STEADY-STATE
// invariants only (§7.8), instruments the op-list, and takes a WALL-CLOCK
// frame-time reading.
//
// =========================================================================
// MEASUREMENT METHODOLOGY -- A LOGGED DEVIATION FROM §10.2, NOT A SHORTCUT
// =========================================================================
// §13 Phase 5b's performance gate is written against an Xcode Metal Frame
// Capture with Performance State checked. This rig does NOT do that, and the
// deviation is deliberate and on the record, following the precedent
// AMENDMENT_8_9 §0 set for exactly this conflict:
//
//   8_9 §0 Rule 1: "No Xcode, ever, for any performance measurement. The
//   project uses only the in-engine automated benchmark harness ... This is a
//   deliberate workflow decision, not a limitation to work around."
//
//   8_9 §0 Rule 2: "FrameTimingManager cannot report Performance State
//   (thermal throttling status). This is a real, permanent limitation of the
//   chosen workflow, not a bug to fix. Mitigation: every benchmark run includes
//   a REPEAT_driftcheck line ... Treat a spread-out or drifted driftcheck as a
//   signal to re-run, not as data to use."
//
// So: driftcheck is implemented below, and EVERY number this rig prints is
// labelled PROVISIONAL -- NOT XCODE-VERIFIED. It exists to say roughly where we
// stand and to catch a gross regression early. IT CANNOT CLOSE PHASE 5b's
// PERFORMANCE GATE. Only the deferred end-of-phase verified pass can do that.
//
// WHAT IS MEASURED, AND WHAT IS DELIBERATELY NOT
// Wall clock only: Time.unscaledDeltaTime, the same source Phase4AcceptanceRig
// uses and the one CLAUDE.md names as the only trusted frame-time figure.
//
// gpuFrameTime IS NOT READ ANYWHERE IN THIS FILE, ON PURPOSE. Amendment 8.10
// measured FrameTimingManager's gpu_avg inflated by a near-constant ~2.6-2.7x
// on this hardware ("a frame's GPU time cannot exceed that frame's wall clock"),
// and an earlier session used it to produce a result that had to be retracted.
// RaymarchAutoBenchmark still reads it; this rig does not, and should not be
// "brought in line" with that file.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Mathematics;
using UnityEngine;

public class Phase5bValidationRig : MonoBehaviour
{
    [SerializeField] private int _maxTicksPerScenario = 4000;
    [SerializeField] private int _quietTicksForRest = 12;
    [SerializeField] private int _perfWarmupTicks = 60;
    [SerializeField] private int _perfSampleTicks = 300;
    [SerializeField] private string _outputRootFolderName = "Phase5bValidation";

    private Phase5bBasin _basin;
    private string _midRunDump, _firstTickDump;
    private string _runFolder;
    private readonly StringBuilder _report = new StringBuilder();

    private int _scenariosRun, _comparisonsMade, _comparisonsMatched;
    private int _perfConfigsMeasured;

    void Start()
    {
        if (!Application.isBatchMode && !HasFlag("-phase5brig")) return;
        StartCoroutine(RunAll());
    }

    private static bool HasFlag(string f)
    {
        foreach (string a in Environment.GetCommandLineArgs())
            if (string.Equals(a, f, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private IEnumerator RunAll()
    {
        _basin = FindAnyObjectByType<Phase5bBasin>();
        if (_basin == null) { Debug.LogError("[Phase5bRig] no Phase5bBasin"); Application.Quit(1); yield break; }

        _basin.SynchronousReadback = HasFlag("-syncreadback");
        for (int i = 0; i < 5; i++) yield return null;

        string ts = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
        _runFolder = Path.Combine(Application.persistentDataPath, _outputRootFolderName, ts);
        Directory.CreateDirectory(_runFolder);

        L("Phase 5b Basin — GPU port validation");
        L($"run     {ts}");
        L($"build   {(Debug.isDebugBuild ? "DEVELOPMENT" : "release")} standalone, {Application.platform}");
        L($"device  {SystemInfo.graphicsDeviceName} / {SystemInfo.graphicsDeviceType}");
        L($"basin   {Phase5bBasin.SX}x{Phase5bBasin.SY}x{Phase5bBasin.SZ} voxels, one chunk, enclosed");
        if (_basin.SynchronousReadback)
            L("readback SYNCHRONOUS (diagnostic) — this run does NOT validate §7.2's async path");
        L("");
        L("ALL TIMING BELOW IS PROVISIONAL — NOT XCODE-VERIFIED.");
        L("  Wall clock only (Time.unscaledDeltaTime), per AMENDMENT_8_9 §0 Rule 1.");
        L("  gpuFrameTime is deliberately NOT read (Amendment 8.10: inflated ~2.6-2.7x here).");
        L("  FrameTimingManager cannot report Performance State (8_9 §0 Rule 2), so a");
        L("  REPEAT_driftcheck line is the only throttle evidence available.");
        L("  These numbers CANNOT close §13 Phase 5b's performance gate.");
        L("");

        // ---- Part 1: steady-state equivalence, GPU vs CPU oracle (§7.8) ----
        // -fastcheck runs ONLY sand_column and skips the perf sweep. The
        // instruction is not to burn a full five-scenario sweep on a one-line
        // change; sand_column is the cheapest (it settles in ~30 ticks).
        bool fastCheck = HasFlag("-fastcheck");
        L("================ STEADY-STATE EQUIVALENCE (GPU vs CPU oracle) ================");
        L("§7.8: compare final levels and conserved counts, NEVER frame-exact positions.");
        if (fastCheck) L("FAST CHECKPOINT MODE: sand_column only, no perf sweep.");
        L("");
        foreach (var sc in _basin.Scenarios)
        {
            if (fastCheck && sc.Id != "sand_column") continue;
            yield return StartCoroutine(RunScenario(sc));
        }

        // ---- Part 2: provisional wall-clock, with driftcheck ----
        if (!fastCheck)
        {
            L("");
            L("================ WALL-CLOCK FRAME TIME (PROVISIONAL) ================");
            yield return StartCoroutine(MeasureAll());
        }

        // ---- Verdict on evidence completeness, not on performance ----
        L("");
        L("================ SUMMARY ================");
        L($"scenarios run            {_scenariosRun} (expected {(HasFlag("-fastcheck") ? 1 : _basin.Scenarios.Length)})");
        L($"steady-state comparisons {_comparisonsMade}, matched {_comparisonsMatched}");
        // 4, not 3: three configs plus the REPEAT_driftcheck, which is a real
        // measurement and was previously counted while the expectation said 3 --
        // so a fully successful run reported FAILED for the wrong reason.
        L($"perf measurements        {_perfConfigsMeasured} (expected {(HasFlag("-fastcheck") ? 0 : 4)})");

        int expectedScenarios = fastCheck ? 1 : _basin.Scenarios.Length;
        int expectedPerf = fastCheck ? 0 : 4;   // 3 configs + driftcheck
        bool enough = _scenariosRun == expectedScenarios
                   && _comparisonsMade > 0
                   && _perfConfigsMeasured == expectedPerf;
        // PHASE_5A_COMPLETION.md §8.4's lesson: a rig that can report success on
        // an absence of evidence is worse than no rig. Short-count is a FAIL.
        if (!enough)
            L("RESULT: FAILED — incomplete evidence. The comparison counts above are not a pass.");
        else if (_comparisonsMatched != _comparisonsMade)
            L("RESULT: FAILED — the GPU port diverged from the CPU oracle. See the per-scenario lines.");
        else
            L("RESULT: steady states matched on every comparison. Timing remains PROVISIONAL.");

        File.WriteAllText(Path.Combine(_runFolder, "phase5b_report.txt"), _report.ToString());
        try
        {
            string src = Application.consoleLogPath;
            if (!string.IsNullOrEmpty(src) && File.Exists(src))
                File.Copy(src, Path.Combine(_runFolder, "player_log.txt"), true);
        }
        catch (Exception e) { Debug.LogWarning($"[Phase5bRig] log copy failed: {e.Message}"); }

        Debug.Log("[Phase5bRig]\n" + _report);
        yield return null;
        Application.Quit(enough && _comparisonsMatched == _comparisonsMade ? 0 : 1);
    }

    private IEnumerator RunScenario(Phase5bBasin.Scenario sc)
    {
        _basin.BuildBasin();
        yield return null;
        sc.Run(_basin);

        int quiet = 0, peakOps = 0, peakSlots = 0, opsAtRest = -1;
        _midRunDump = null; _firstTickDump = null;
        for (int t = 0; t < _maxTicksPerScenario; t++)
        {
            bool ticked = _basin.Tick();
            yield return null;                       // one CA tick per frame
            // Back-pressured frames run NO tick. Counting them as quiet let
            // lava_vent declare rest after 12 frames in which nothing had been
            // asked to happen -- a spurious rest, and then a comparison against
            // a sim that had not moved yet.
            if (!ticked) continue;
            // Tick 1 is the ONLY tick that can show the scenario's own wake
            // requests being consumed: the GPU counters are cleared every tick,
            // so a sample at tick 8 shows an idle sim regardless of whether
            // tick 1 worked.
            if (t == 0) _firstTickDump = _basin.Fluid.DebugDump();
            if (t == 8) _midRunDump = _basin.Fluid.DebugDump();

            peakOps = Mathf.Max(peakOps, _basin.Readback.OpsLastFrame);
            peakSlots = Mathf.Max(peakSlots, _basin.Oracle.ActiveSlotCount);

            bool still = _basin.Readback.OpsLastFrame == 0 && _basin.Oracle.ChangedCellsThisTick == 0;
            quiet = still ? quiet + 1 : 0;
            if (quiet >= _quietTicksForRest) { opsAtRest = _basin.Readback.OpsLastFrame; break; }
        }
        _basin.SettleReadback();
        yield return null;

        _scenariosRun++;
        L($"---------------- {sc.Id} ----------------");
        L($"ticks {_basin.TicksRun}, rest {(opsAtRest >= 0 ? "reached" : "NOT reached")}" +
          (sc.Id == "place_block" || sc.Id == "mine_drop"
              ? $", edit applied to {_basin.LastEditCells} cell(s)" : ""));
        // §7.8 compares STEADY STATES. A scenario that never came to rest has no
        // steady state to compare, so its comparison is meaningless -- record it
        // as a failure rather than comparing two mid-motion snapshots.
        if (opsAtRest < 0)
        {
            _comparisonsMade++;
            L("  *** NOT AT REST — no steady state to compare. Counted as a divergence. ***");
            L("");
            yield break;
        }

        // §13's boundedness gate: the op-list must track CHANGED cells, not
        // total active slots. The sharpest form of that is the resting state --
        // slots may still exist while nothing changes, and ops must be zero.
        L($"op-list: peak {peakOps} ops/frame against peak {peakSlots} active slots" +
          $"; at rest {(opsAtRest >= 0 ? opsAtRest.ToString() : "n/a")}" +
          $"  [total {_basin.Readback.OpsTotal}, readback errors {_basin.Readback.ReadbackErrorsTotal}" +
          $" ({_basin.Readback.LastReadbackError}), append overflow {_basin.Readback.AppendOverflowFramesTotal}, skipped {_basin.Readback.SkippedIssuesTotal}, stale dropped {_basin.Readback.StaleOpsDropped}]");
        // §10.4 per-subsystem dump: which stage went silent, not just "nothing
        // happened". Sampled at rest and again mid-run so a stage that works
        // early and stops is distinguishable from one that never ran.
        if (_firstTickDump != null) L($"  §10.4 dump @tick1: {_firstTickDump}");
        L($"  §10.4 dump @rest : {_basin.Fluid.DebugDump()}");
        if (_midRunDump != null) L($"  §10.4 dump @tick8: {_midRunDump}");
        L($"readback latency: max frames in flight {_basin.Readback.MaxFramesInFlightSeen} " +
          "(§7.2 expects 1-3; §8.2's speed clamp does not exist until Phase 6)");

        foreach (byte m in new[] { Materials.Water, Materials.Sand, Materials.Lava, Materials.Obsidian })
        {
            int gpu = _basin.CountMaterialWorld(m);
            int cpu = _basin.Oracle.CountMaterial(m);
            if (gpu == 0 && cpu == 0) continue;

            _comparisonsMade++;
            bool countsMatch = gpu == cpu;

            int[] gp = _basin.LayerProfileWorld(m), cp = _basin.LayerProfileOracle(m);
            int worstLayer = -1, worstDelta = 0;
            for (int y = 0; y < gp.Length; y++)
            {
                int d = Mathf.Abs(gp[y] - cp[y]);
                if (d > worstDelta) { worstDelta = d; worstLayer = y; }
            }
            bool levelsMatch = worstDelta == 0;
            if (countsMatch && levelsMatch) _comparisonsMatched++;

            L($"  material {m}: conserved count GPU {gpu} vs CPU {cpu} " +
              $"{(countsMatch ? "MATCH" : "*** DIVERGED ***")}; " +
              $"final levels worst per-layer delta {worstDelta}" +
              (worstLayer >= 0 && worstDelta > 0 ? $" at y={worstLayer}" : "") +
              $" {(levelsMatch ? "MATCH" : "*** DIVERGED ***")}");
        }
        L("");
    }

    // =====================================================================
    // Wall-clock measurement (PROVISIONAL)
    // =====================================================================

    private IEnumerator MeasureAll()
    {
        // §13 Phase 5b's gate is "fluid CA + Phase 2's primary raymarch only --
        // shadow rays do not exist until Phase 7, so they are correctly absent."
        // The three configs isolate which half costs what; the gate config is
        // the third.
        yield return StartCoroutine(Measure("raymarch_only        (fluid CA off)", false, true));
        yield return StartCoroutine(Measure("fluidCA_only         (raymarch off)", true, false));
        double gate = 0;
        yield return StartCoroutine(MeasureInto("GATE fluidCA+raymarch (§13 5b gate config)", true, true, r => gate = r));

        // 8_9 §0 Rule 2's mitigation: repeat the HEAVIEST config at the end of
        // the sweep. This is the only throttle evidence this workflow can produce.
        double drift = 0;
        yield return StartCoroutine(MeasureInto("REPEAT_driftcheck    (same as GATE)", true, true, r => drift = r));

        double spread = Math.Abs(gate - drift);
        double pct = gate > 0 ? spread / gate * 100.0 : 0;
        L("");
        L($"driftcheck: GATE p50 {gate:F2}ms vs REPEAT p50 {drift:F2}ms — spread {spread:F2}ms ({pct:F1}%)");
        if (pct > 10.0)
            L("  *** DRIFTED >10% — TREAT THIS ENTIRE RUN AS UNRELIABLE AND RE-RUN. ***\n" +
              "  Per 8_9 §0 Rule 2 a drifted driftcheck is a signal to re-run, NOT data to average over.\n" +
              "  Most likely thermal throttling, which this workflow cannot observe directly.");
        else
            L("  within 10% — decent (not conclusive) evidence the machine was not mid-throttle.");
    }

    private IEnumerator Measure(string label, bool fluidOn, bool raymarchOn)
        => MeasureInto(label, fluidOn, raymarchOn, null);

    private IEnumerator MeasureInto(string label, bool fluidOn, bool raymarchOn, Action<double> sink)
    {
        _basin.BuildBasin();
        yield return null;
        // A busy, non-degenerate load: a live pour, so the CA has real work
        // rather than measuring an empty basin.
        _basin.OpenSource(new int3(21, Phase5bBasin.SY - 2, 32), Materials.Water, 100000);

        var cam = Camera.main;
        if (cam != null) cam.enabled = raymarchOn;

        for (int i = 0; i < _perfWarmupTicks; i++)
        {
            if (fluidOn) _basin.Tick();
            yield return null;
        }

        var ms = new List<double>(_perfSampleTicks);
        for (int i = 0; i < _perfSampleTicks; i++)
        {
            if (fluidOn) _basin.Tick();
            yield return null;
            // THE measurement. Wall clock, same source Phase4AcceptanceRig uses.
            ms.Add(Time.unscaledDeltaTime * 1000.0);
        }

        if (cam != null) cam.enabled = true;

        double p50 = Pct(ms, 0.50f), p99 = Pct(ms, 0.99f), max = Pct(ms, 1.0f);
        _perfConfigsMeasured++;
        L($"  {label}: p50 {p50,7:F2}  p99 {p99,7:F2}  max {max,7:F2} ms  " +
          $"over {ms.Count} frames   [PROVISIONAL — NOT XCODE-VERIFIED]");
        sink?.Invoke(p50);
    }

    private static double Pct(List<double> v, float p)
    {
        if (v.Count == 0) return 0;
        var c = new List<double>(v); c.Sort();
        return c[Mathf.Clamp((int)(c.Count * p), 0, c.Count - 1)];
    }

    private void L(string line) => _report.AppendLine(line);
}
