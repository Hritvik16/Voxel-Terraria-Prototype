// ==========================================
// Assets/Editor/Phase5aManualDriver.cs
//
// Drives Phase5aBasin's five §13 scenario handlers headlessly and dumps what
// the debug overlay would be showing, plus PNG captures of both slice views.
//
// WHAT THIS IS AND IS NOT. It opens the real "Phase 5a Basin" scene, uses the
// real Phase5aBasin component, and calls the SAME private handlers the five
// OnGUI buttons call. It is therefore a real exercise of the scenario code and
// the overlay's data. It is NOT a human in Play mode: it does not run Unity's
// frame loop, so Update()'s tick accumulator and the IMGUI layout/slider code
// in OnGUI are NOT covered here. Anything this driver reports is a genuine
// observation; anything it cannot reach is called out in the log rather than
// implied to be fine.
//
//   Unity -batchmode -quit -projectPath . \
//         -executeMethod Phase5aManualDriver.RunAllScenarios
//
// (-quit IS correct here: -executeMethod, not -runTests. See CLAUDE.md.)

using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class Phase5aManualDriver
{
    private const string OutDir = "Phase5aManualRun";
    private const BindingFlags Priv = BindingFlags.Instance | BindingFlags.NonPublic;

    private static readonly StringBuilder Log = new StringBuilder();

    [MenuItem("Voxel Engine/Phase 5a/Run Basin Scenarios (headless)")]
    public static void RunAllScenarios()
    {
        Directory.CreateDirectory(OutDir);
        Log.Clear();

        EditorSceneManager.OpenScene(Phase5aSceneBuilder.ScenePath, OpenSceneMode.Single);
        var basin = Object.FindAnyObjectByType<Phase5aBasin>();
        if (basin == null) { Fail("no Phase5aBasin component in the scene"); return; }

        Invoke(basin, "Start");
        var sim = Field<FluidReferenceCPU>(basin, "_sim");
        if (sim == null) { Fail("Start() did not construct the FluidReferenceCPU"); return; }

        Line($"basin built: {sim.SizeXVoxels} x {sim.SizeYVoxels} x {sim.SizeZVoxels} voxels, " +
             $"slot capacity {sim.SlotCapacity}");
        Line($"after BuildBasin: mobile={sim.CountMobileBytes()} slots={sim.ActiveSlotCount} " +
             $"stone={sim.CountMaterial(Materials.Stone)}");
        Capture(basin, "00_empty_basin");

        Scenario(basin, sim, "PourWater", "01_pour_water", 120);
        Scenario(basin, sim, "DropSandColumn", "02_sand_column", 160);
        Scenario(basin, sim, "LavaVent", "03_lava_vent", 200);
        Scenario(basin, sim, "PlaceBlockIntoStream", "04_place_block", 40);
        MineScenario(basin, sim);

        Line("");
        Line("NOT COVERED BY THIS DRIVER: OnGUI layout, the two view sliders, " +
             "Pause/Step/Reset buttons, and Update()'s tick accumulator -- " +
             "those need a human in Play mode.");

        string logPath = Path.Combine(OutDir, "manual_run.txt");
        File.WriteAllText(logPath, Log.ToString());
        Debug.Log("[Phase5aManualDriver]\n" + Log);
        Debug.Log($"[Phase5aManualDriver] wrote {logPath}");
    }

    private static void Scenario(Phase5aBasin basin, FluidReferenceCPU sim,
                                 string handler, string tag, int ticks)
    {
        Line("");
        Line($"================ {handler}() ================");
        Invoke(basin, handler);
        Line("overlay action line: " + Field<string>(basin, "_lastAction"));

        int firstRest = -1, maxSlots = 0, reactions = 0, orphans = 0;
        for (int t = 0; t < ticks; t++)
        {
            Invoke(basin, "StepOnce");
            maxSlots = Mathf.Max(maxSlots, sim.ActiveSlotCount);
            reactions += sim.ReactionsThisTick;
            orphans += sim.OrphansFreedThisTick;
            if (firstRest < 0 && sim.ChangedCellsThisTick == 0 && t > 4) firstRest = sim.TickCount;
            if (t == 14) Capture(basin, tag + "_a_midflight");
        }
        Capture(basin, tag + "_b_settled");
        ReportState(basin, sim, ticks, firstRest, maxSlots, reactions, orphans);
    }

    private static void MineScenario(Phase5aBasin basin, FluidReferenceCPU sim)
    {
        Line("");
        Line("================ MineAFallingDrop() ================");

        // Re-open the vent so there is something actually in flight to mine.
        Invoke(basin, "PourWater");
        for (int t = 0; t < 6; t++) Invoke(basin, "StepOnce");

        long orphansBefore = sim.OrphansFreedTotal;
        int slotsBefore = sim.ActiveSlotCount;
        int mobileBefore = sim.CountMobileBytes();

        Invoke(basin, "MineAFallingDrop");
        Line("overlay action line: " + Field<string>(basin, "_lastAction"));
        Line($"immediately after the edit: slots={sim.ActiveSlotCount} (was {slotsBefore}) " +
             $"mobile={sim.CountMobileBytes()} (was {mobileBefore}) " +
             $"orphansTotal={sim.OrphansFreedTotal} (was {orphansBefore})");

        Invoke(basin, "StepOnce");
        Line($"ONE TICK LATER: orphansThisTick={sim.OrphansFreedThisTick} " +
             $"orphansTotal={sim.OrphansFreedTotal} slots={sim.ActiveSlotCount} " +
             $"mobile={sim.CountMobileBytes()}");
        Capture(basin, "05_mine_drop");
        ReportState(basin, sim, 1, -1, sim.ActiveSlotCount, 0, sim.OrphansFreedThisTick);
    }

    private static void ReportState(Phase5aBasin basin, FluidReferenceCPU sim,
                                    int ticks, int firstRest, int maxSlots,
                                    int reactions, int orphans)
    {
        var check = sim.CheckConservation();
        int brokenAt = Field<int>(basin, "_brokenAtTick");
        Line($"after {ticks} tick(s) (sim tick {sim.TickCount}):");
        Line($"  conservation      {(check.Ok ? "OK" : "BROKEN")}  " +
             $"expected={check.Expected} counted={check.Actual} delta={check.Delta}");
        Line($"  overlay latch     {(brokenAt < 0 ? "clean (never broke)" : "BROKE ON TICK " + brokenAt)}");
        Line($"  mobile bytes      {sim.CountMobileBytes()}");
        Line($"  active slots      {sim.ActiveSlotCount} (peak {maxSlots} / cap {sim.SlotCapacity})");
        Line($"  changed cells     {sim.ChangedCellsThisTick}" +
             (sim.ChangedCellsThisTick == 0 ? "  <at rest>" : ""));
        Line($"  first rest tick   {(firstRest < 0 ? "did not reach rest" : firstRest.ToString())}");
        Line($"  orphans / reactions during window   {orphans} / {reactions}");
        Line($"  duplicate slot ownership            {sim.CountDuplicateSlotOwnership()}");
        Line($"  ledger  +{sim.Ledger.ExternalAdded} placed  -{sim.Ledger.ExternalRemoved} removed " +
             $"-{sim.Ledger.ReactionConsumed} reacted");
        Line($"  materials  water={sim.CountMaterial(Materials.Water)} " +
             $"sand={sim.CountMaterial(Materials.Sand)} lava={sim.CountMaterial(Materials.Lava)} " +
             $"obsidian={sim.CountMaterial(Materials.Obsidian)} stone={sim.CountMaterial(Materials.Stone)}");
    }

    /// Writes both slice views, upscaled 8x with nearest-neighbour so a
    /// 64x32 cross-section is actually legible as an image.
    private static void Capture(Phase5aBasin basin, string tag)
    {
        Invoke(basin, "RedrawSlices");
        Save(Field<Texture2D>(basin, "_sideView"), $"{tag}_side.png");
        Save(Field<Texture2D>(basin, "_topView"), $"{tag}_top.png");
    }

    private static void Save(Texture2D src, string name)
    {
        if (src == null) return;
        const int S = 8;
        var big = new Texture2D(src.width * S, src.height * S, TextureFormat.RGBA32, false);
        var srcPix = src.GetPixels32();
        var dst = new Color32[big.width * big.height];
        for (int y = 0; y < big.height; y++)
        for (int x = 0; x < big.width; x++)
            dst[y * big.width + x] = srcPix[(y / S) * src.width + (x / S)];
        big.SetPixels32(dst);
        big.Apply(false);
        File.WriteAllBytes(Path.Combine(OutDir, name), big.EncodeToPNG());
        Object.DestroyImmediate(big);
    }

    private static void Invoke(object o, string method) =>
        o.GetType().GetMethod(method, Priv)?.Invoke(o, null);

    private static T Field<T>(object o, string name)
    {
        var f = o.GetType().GetField(name, Priv);
        return f == null ? default : (T)f.GetValue(o);
    }

    private static void Line(string s) => Log.AppendLine(s);

    private static void Fail(string why)
    {
        Debug.LogError("[Phase5aManualDriver] " + why);
        if (Application.isBatchMode) EditorApplication.Exit(1);
    }
}
