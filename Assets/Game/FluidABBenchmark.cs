// ==========================================
// Assets/Game/FluidABBenchmark.cs
//
// DENSE vs TILED, WALL CLOCK, AT MATCHED VOLUME -- and the CPU-side apply cost
// that has been flagged NOT MEASURED since Phase 5b.
//
// It answers three questions that no rig in this project has ever answered:
//
//   STEP 1  Does §7.2's tiled addressing cost more, less, or the same per
//           voxel than the dense region it replaces, at matched live-voxel
//           counts? Every existing fluid number is dense-only or tiled-only;
//           none is a controlled A/B.
//   STEP 2  Does SCATTER cost anything BEYOND raw volume? Same total voxels,
//           split across 1 / 8 / 64 / 512 disconnected pockets. This is the
//           specific risk DESIGN_NOTE_7_2 left unmeasured.
//   STEP 4  What does FluidOpListReadback.PumpAndApply cost on the CPU main
//           thread as volume climbs? A different lane from the GPU CA, and it
//           needs its own number rather than being buried in frame time.
//
// -------------------------------------------------------------------------
// METHODOLOGY -- inherited, not reinvented
// -------------------------------------------------------------------------
// RELEASE standalone launched outside the Editor, vsync off, uncapped, wall
// clock from Time.unscaledDeltaTime. Same family as RaymarchAutoBenchmark,
// PlaygroundCapture.FluidBenchmark, and PHASE_2_COMPLETION's table.
//
// ONE CONFIG PER PROCESS LAUNCH. Not a preference -- RaymarchAutoBenchmark's
// own header records back-to-back configs producing a physically impossible
// ordering on this fanless machine, with GPU frequency scaling as the leading
// hypothesis, and states the next step is isolating one config per launch.
// PlaygroundCapture did exactly that. So does this. Fluid also PERSISTS once
// poured, so a second config inside one process would not start clean even if
// DVFS were not a factor.
//
// NO gpuFrameTime, ANYWHERE. Amendment 8.10 measured it inflated ~2.6-2.7x on
// this hardware. Nothing this file prints is a GPU-stage attribution, and no
// figure from it may be quoted against §2.2 without that caveat attached.
// No Xcode and no Instruments -- Amendment 8.9 Rule 1, and per-kernel Metal
// attribution is a confirmed dead end here (Unity merges the CA's dispatches
// into a single encoder, so only the first kernel is ever named).
//
// THERE IS NO PERFORMANCE STATE FIELD. FrameTimingManager cannot report it on
// this platform. That is a permanent accepted limitation and is NOT worked
// around, faked, or inferred from anything else here.
//
// -------------------------------------------------------------------------
// WHAT MAKES THE A/B FAIR, AND WHERE IT IS DELIBERATELY UNFAIR
// -------------------------------------------------------------------------
// FAIR: dense and tiled run byte-identical scenarios. Same pockets, same
// materials, same placement order, same seed, same camera pose, same frame
// budget. The ONLY difference is whether FluidGpuSimulation was handed a
// FluidTileMap. Both report the live-slot count they actually sustained, so
// "matched volume" is evidenced per row rather than asserted.
//
// UNFAIR, ON PURPOSE, IN DENSE'S FAVOUR: the dense region is re-sized per
// config to the smallest power-of-two box that holds the scenario
// (FluidBenchPlan.SizeDenseRegion), rather than the fixed box the shipped
// dense path actually used. That is the best dense could possibly do, so a
// tiled win measured here is a conservative one -- and a tiled LOSS here is
// not automatically a loss against the real dense configuration.
//
// The mechanism behind whatever the numbers say is FluidGpuSimulation's own
// DispatchCells: dense dispatches its whole box every tick however little
// fluid is in it; tiled dispatches 32^3 = 32,768 cells per RESIDENT tile.
// Both cell counts are recorded per row, because they are the explanatory
// variable and without them the timings are just two numbers.
// ==========================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Memory;
using VoxelEngine.Simulation;
using VoxelEngine.Streaming;
using Debug = UnityEngine.Debug;

public class FluidABBenchmark : MonoBehaviour
{
    [SerializeField] private ComputeShader _fluidCA;
    [SerializeField] private int _tilePoolCap = 512;
    [SerializeField] private int _slotCapacity = 65536;
    [SerializeField] private int _maxOpsPerFrame = 65536;
    [SerializeField] private string _outputRootFolderName = "FluidAB";

    // Methodology constants. consts, not [SerializeField] -- RaymarchAutoBenchmark
    // shipped a 0.2s sampling window because a scene instance held stale
    // serialized values that silently overrode the code's defaults. A const
    // cannot go stale.
    private const int WarmupFrames = 420;   // ~7 s, once, before anything is touched
    private const int SettleFrames = 45;    // brief: the body must still be FALLING when sampled
    private const int TargetSamples = 180;  // ~3 s of frames

    private static ChunkStore Store => Phase4Bootstrapper.Store;
    private static TerrainClipmap Clip => Phase4Bootstrapper.Clipmap;

    private EditService _edits;
    private FluidGpuSimulation _fluid;
    private FluidOpListReadback _readback;
    private FluidTileMap _tiles;
    private long _applied;

    // -------------------------------------------------------------------
    // Per-frame instrumentation
    // -------------------------------------------------------------------
    // Stopwatch, not Time.unscaledDeltaTime, for the two sub-phases: delta
    // time can only measure a whole frame, and the whole point of step 4 is to
    // separate the CPU apply from everything else in that frame. Stopwatch is
    // the highest-resolution programmatic clock available without a profiler
    // session, which the "zero manual steps" constraint rules out anyway.
    private readonly Stopwatch _swSubmit = new Stopwatch();
    private readonly Stopwatch _swPump = new Stopwatch();

    IEnumerator Start()
    {
        string raw = ArgValue("-fluidab");
        if (string.IsNullOrEmpty(raw))
        {
            Debug.LogError("[fluidab] no -fluidab <config> given");
            Application.Quit(2); yield break;
        }

        FluidBenchConfig cfg;
        try { cfg = FluidBenchPlan.Parse(raw); }
        catch (Exception e)
        {
            // Refuse rather than default. A row labelled with a config it did
            // not run is worse than no row at all.
            Debug.LogError($"[fluidab] bad config '{raw}': {e.Message}");
            Application.Quit(2); yield break;
        }

        // -fluidopbudget <n>, or "off" for the pre-budget behaviour. Exists so
        // the burst can be shown RETURNING when the cap is removed -- a fix
        // whose absence cannot be demonstrated has not been shown to be the
        // thing that helped.
        string budgetArg = ArgValue("-fluidopbudget");
        if (!string.IsNullOrEmpty(budgetArg))
        {
            FluidOpListReadback.MaxOpsAppliedPerFrame =
                budgetArg == "off" ? int.MaxValue : int.Parse(budgetArg, CultureInfo.InvariantCulture);
        }

        Screen.SetResolution(1920, 1080, false);
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;   // a cap would floor the measurement

        float t0 = Time.realtimeSinceStartup;
        while (Store == null && Time.realtimeSinceStartup - t0 < 240f) yield return null;
        if (Store == null)
        {
            Debug.LogError("[fluidab] world never booted");
            Application.Quit(3); yield break;
        }

        // Let the streaming window fill before anything is measured or placed.
        for (int i = 0; i < 150; i++) yield return null;

        Camera cam = Camera.main;
        int3 camVox = CoordMath.WorldToVoxel(new float3(cam.transform.position.x,
                                                        cam.transform.position.y,
                                                        cam.transform.position.z));
        int surface = SurfaceY(camVox.x, camVox.z);
        if (surface < 1)
        {
            Debug.LogError("[fluidab] no surface under the camera");
            Application.Quit(3); yield break;
        }

        _edits = new EditService();
        _edits.AttachWorld(Store, Store, Store, Clip);

        var pockets = FluidBenchPlan.Layout(cfg, camVox, surface);
        int plannedVoxels = FluidBenchPlan.PlacedVoxels(pockets);

        // Warm up BEFORE building or placing: Metal pipeline compilation and
        // first-touch page faults are one-time costs that would otherwise land
        // inside the sampling window of whichever config ran first.
        for (int i = 0; i < WarmupFrames; i++) yield return null;

        int3 denseOrigin = int3.zero, denseDims = int3.zero;
        if (cfg.Tiled)
        {
            _tiles = new FluidTileMap(ChunkFluidMask.TILE_EDGE, new int3(128, 128, 128), _tilePoolCap);
            _fluid = new FluidGpuSimulation(_fluidCA, new int3(64, 64, 64), _slotCapacity,
                                            _maxOpsPerFrame, _tiles)
            {
                // Under tiling the region box is only an addressing origin --
                // the active set is the tile pool, so this stays small and the
                // radius is what bounds the simulation.
                RegionOriginVoxels = int3.zero,
                PlayerVoxel = camVox,
                ActiveRadiusVoxels = EngineConfig.FLUID_ACTIVE_RADIUS_VOXELS,
                SleepRadiusVoxels = FluidActiveRegion.SleepRadiusFor(EngineConfig.FLUID_ACTIVE_RADIUS_VOXELS),
            };
        }
        else
        {
            denseDims = FluidBenchPlan.SizeDenseRegion(pockets, surface, out denseOrigin);
            _fluid = new FluidGpuSimulation(_fluidCA, denseDims, _slotCapacity, _maxOpsPerFrame)
            {
                RegionOriginVoxels = denseOrigin,
                PlayerVoxel = camVox,
                // Same radius as tiled. The dense box is smaller than the
                // radius here, so the box bounds the simulation and the radius
                // does not bite -- which is exactly the situation §7.2 exists
                // to escape, and is the honest dense baseline.
                ActiveRadiusVoxels = EngineConfig.FLUID_ACTIVE_RADIUS_VOXELS,
            };
        }

        _readback = new FluidOpListReadback(_fluid, Store)
        {
            OnVoxelApplied = v => { Clip.MarkDirty(CoordMath.VoxelToChunk(v)); _applied++; },
        };
        _edits.AttachFluidSimulation(_fluid, Store);

        // Place the scenario. Materials cycle so reactions between adjacent
        // pockets are exercised in the scatter configs, matching what the
        // overnight explosion test did.
        int placed = 0;
        for (int i = 0; i < pockets.Length; i++)
        {
            byte m = (i % 3) == 0 ? Materials.Water : (i % 3) == 1 ? Materials.Sand : Materials.Lava;
            // SetBox's bounds are INCLUSIVE on both ends (`x <= b.x`), while
            // FluidBenchPocket.Size is a count. Passing Lo + Size placed
            // (side+1)^3 -- 35,937 voxels for a 32,768 target, and worse at
            // small sides. Caught by the planned-vs-placed columns disagreeing
            // in the first sweep, which is why both are in the CSV.
            placed += _edits.SetBox(pockets[i].Lo, pockets[i].Lo + pockets[i].Size - 1, m);
            if ((i & 15) == 0) yield return null;
        }

        if (cfg.Tiled) RefreshTiles(camVox);

        for (int i = 0; i < SettleFrames; i++) { Tick(); yield return null; }

        // ---------------- the measured window ----------------
        var frameMs = new List<double>(TargetSamples);
        var submitMs = new List<double>(TargetSamples);
        var pumpMs = new List<double>(TargetSamples);
        long dispatchCellsSum = 0, activeTilesSum = 0;
        int liveMin = int.MaxValue, liveMax = 0; long liveSum = 0;
        long appliedAtStart = _applied;

        // STEP 1: is the upload blowout CALL COUNT or BYTE VOLUME?
        // Measured, not read off the type of _dirtyChunks.
        long markCallsAtStart = Clip.MarkDirtyCallsTotal;
        long markCoalescedAtStart = Clip.MarkDirtyCoalescedTotal;
        long uploadBytesSum = 0, uploadChunksSum = 0, brickRunsSum = 0, brickSlotsSum = 0;

        for (int i = 0; i < TargetSamples; i++)
        {
            // Residency is refreshed on the same cadence the acceptance rig
            // uses. It is CPU work that only the tiled path does, so it must
            // be inside the measured frame or the comparison flatters tiling.
            if (cfg.Tiled && (i % 20) == 0) RefreshTiles(camVox);

            Tick();

            int cells = _fluid.Tiles == null
                ? _fluid.CellCount
                : _fluid.ActiveTileCount * _fluid.Tiles.TileCells;
            dispatchCellsSum += cells;
            activeTilesSum += _fluid.ActiveTileCount;

            _fluid.ReadSlotCounters(out uint hi, out uint ever);
            int live = (int)hi;
            liveMin = math.min(liveMin, live);
            liveMax = math.max(liveMax, live);
            liveSum += live;

            var us = Phase4Bootstrapper.Streamer != null
                ? Phase4Bootstrapper.Streamer.LastUploadStats
                : default;
            uploadBytesSum += us.bytesUploaded;
            uploadChunksSum += us.chunksUploaded;
            brickRunsSum += us.brickRuns;
            brickSlotsSum += us.brickSlots;

            yield return null;
            frameMs.Add(Time.unscaledDeltaTime * 1000.0);
            submitMs.Add(_swSubmit.Elapsed.TotalMilliseconds);
            pumpMs.Add(_swPump.Elapsed.TotalMilliseconds);
            _swSubmit.Reset(); _swPump.Reset();
        }

        _fluid.ReadSlotCounters(out uint hiEnd, out uint everEnd);

        string outDir = Path.Combine(Application.persistentDataPath, _outputRootFolderName);
        Directory.CreateDirectory(outDir);

        var row = new StringBuilder();
        row.Append(string.Join(",", new[]
        {
            cfg.Label,
            cfg.Tiled ? "tiled" : "dense",
            cfg.TargetVoxels.ToString(CultureInfo.InvariantCulture),
            cfg.Pockets.ToString(CultureInfo.InvariantCulture),
            cfg.IsDriftcheck ? "1" : "0",
            plannedVoxels.ToString(CultureInfo.InvariantCulture),
            placed.ToString(CultureInfo.InvariantCulture),
            frameMs.Count.ToString(CultureInfo.InvariantCulture),
            F(P(frameMs, 0.50)), F(P(frameMs, 0.99)), F(Mean(frameMs)),
            F(P(submitMs, 0.50)), F(P(submitMs, 0.99)), F(Mean(submitMs)),
            F(P(pumpMs, 0.50)), F(P(pumpMs, 0.99)), F(Mean(pumpMs)),
            (liveSum / Math.Max(1, frameMs.Count)).ToString(CultureInfo.InvariantCulture),
            liveMin.ToString(CultureInfo.InvariantCulture),
            liveMax.ToString(CultureInfo.InvariantCulture),
            (dispatchCellsSum / Math.Max(1, frameMs.Count)).ToString(CultureInfo.InvariantCulture),
            (activeTilesSum / Math.Max(1, frameMs.Count)).ToString(CultureInfo.InvariantCulture),
            (_applied - appliedAtStart).ToString(CultureInfo.InvariantCulture),
            _readback.OpsTotal.ToString(CultureInfo.InvariantCulture),
            everEnd.ToString(CultureInfo.InvariantCulture),
            cfg.Tiled ? "0" : ((long)denseDims.x * denseDims.y * denseDims.z).ToString(CultureInfo.InvariantCulture),
            cfg.Tiled ? "tiled" : $"{denseDims.x}x{denseDims.y}x{denseDims.z}",
            _readback.ReadbackErrorsTotal.ToString(CultureInfo.InvariantCulture),
            _readback.StaleOpsDropped.ToString(CultureInfo.InvariantCulture),
            _readback.OpsDroppedNonResident.ToString(CultureInfo.InvariantCulture),
            (Clip.MarkDirtyCallsTotal - markCallsAtStart).ToString(CultureInfo.InvariantCulture),
            (Clip.MarkDirtyCoalescedTotal - markCoalescedAtStart).ToString(CultureInfo.InvariantCulture),
            (uploadBytesSum / Math.Max(1, frameMs.Count)).ToString(CultureInfo.InvariantCulture),
            (uploadChunksSum / Math.Max(1, frameMs.Count)).ToString(CultureInfo.InvariantCulture),
            (brickRunsSum / Math.Max(1, frameMs.Count)).ToString(CultureInfo.InvariantCulture),
            (brickSlotsSum / Math.Max(1, frameMs.Count)).ToString(CultureInfo.InvariantCulture),
            FluidOpListReadback.MaxOpsAppliedPerFrame.ToString(CultureInfo.InvariantCulture),
            _readback.BudgetLimitedFrames.ToString(CultureInfo.InvariantCulture),
            _readback.BatchesCarriedForward.ToString(CultureInfo.InvariantCulture),
            _readback.MaxPendingSeen.ToString(CultureInfo.InvariantCulture),
        }));

        string csv = Path.Combine(outDir, "results.csv");
        if (!File.Exists(csv)) File.AppendAllText(csv, Header + "\n");
        File.AppendAllText(csv, row + "\n");
        Debug.Log("[fluidab] " + row);

        yield return null;
        Application.Quit(0);
    }

    private const string Header =
        "label,path,target_voxels,pockets,driftcheck,planned_voxels,placed_voxels,samples," +
        "frame_p50_ms,frame_p99_ms,frame_mean_ms," +
        "submit_p50_ms,submit_p99_ms,submit_mean_ms," +
        "pump_p50_ms,pump_p99_ms,pump_mean_ms," +
        "live_slots_mean,live_slots_min,live_slots_max," +
        "dispatch_cells_mean,active_tiles_mean,voxel_writes,ops_total,slots_ever," +
        "dense_region_cells,dense_region_dims,readback_errors,stale_ops,nonresident_ops," +
        "markdirty_calls,markdirty_coalesced,upload_bytes_mean,upload_chunks_mean," +
        "brick_runs_mean,brick_slots_mean," +
        "op_budget,budget_limited_frames,batches_carried,max_pending";

    private void OnDestroy() { _readback?.Dispose(); _fluid?.Dispose(); }

    // =====================================================================

    private void Tick()
    {
        _swSubmit.Start();
        if (_readback.CanIssue) { _fluid.Tick(Clip); _readback.IssueReadback(0); }
        _swSubmit.Stop();

        // STEP 4's number. Bracketed around PumpAndApply and nothing else --
        // this is the CPU main-thread lane, entirely separate from the GPU CA,
        // and it is the cost Phase 5b flagged as unmeasured.
        _swPump.Start();
        _readback.PumpAndApply();
        _swPump.Stop();
    }

    private void RefreshTiles(int3 centre)
    {
        _fluid.UpdatePlayerPosition(centre);
        FluidTileResidency.Refresh(Store, _tiles, centre,
                                   _fluid.ActiveRadiusVoxels, _fluid.SleepRadiusVoxels);
    }

    private int SurfaceY(int x, int z)
    {
        for (int y = WorldGenConstants.MAX_TERRAIN_HEIGHT + 2; y >= 1; y--)
        {
            byte m = Store.GetVoxel(new int3(x, y, z));
            if (m != Materials.Air && !MaterialRules.IsFluidMaterial(m)) return y;
        }
        return -1;
    }

    // =====================================================================

    private static double P(List<double> xs, double q)
    {
        if (xs.Count == 0) return 0;
        var copy = new List<double>(xs); copy.Sort();
        int k = Mathf.Clamp(Mathf.RoundToInt((float)(q * (copy.Count - 1))), 0, copy.Count - 1);
        return copy[k];
    }

    private static double Mean(List<double> xs)
    {
        if (xs.Count == 0) return 0;
        double s = 0; foreach (double v in xs) s += v; return s / xs.Count;
    }

    private static string F(double v) => v.ToString("F4", CultureInfo.InvariantCulture);

    private static string ArgValue(string flag)
    {
        string[] a = Environment.GetCommandLineArgs();
        for (int i = 0; i < a.Length - 1; i++)
            if (string.Equals(a[i], flag, StringComparison.OrdinalIgnoreCase)) return a[i + 1];
        return null;
    }
}
