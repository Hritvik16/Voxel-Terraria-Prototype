// Assets/CoreEngine/Rendering/RaymarchFeature.RoofInc.cs
//
// TASK 168 (PERF_EXACT_CUTS row D, task 59's option (c)): the shadow roof skip's HeightMip
// pyramid is rebuilt INCREMENTALLY. Task 59 found the skip exact but lost it to the full
// per-frame rebuild (~2.5 ms at 432 rows, 161's roof_build arm). The pyramid is a pure
// function of ClipmapBuffer, the window's Y origin / height and the content ceiling
// (TerrainClipmap.HeightDirty.cs), and the CPU is the sole writer of ClipmapBuffer, so:
//   * FULL build: first use, a new TerrainClipmap or HeightMip buffer, a frame that did not
//     build (roof skip off), a Y origin / height / ceiling change, more than half the ring
//     columns dirty, or `-roofinc 0`;
//   * INCREMENTAL: only the ring chunk columns written since the last build
//     (Raymarch.compute BuildHeight*Cols, the same per-word functions as the full build);
//   * NOTHING: no column written and nothing else changed (the pyramid is current).
// `-roofverify N` (N >= 1): every Nth build also runs the full build into a second buffer and
// counts the differing words on the GPU (HeightVerify, async readback): RoofVerifyChecks /
// RoofVerifyBadChecks / RoofVerifyBadWords, and a log line per bad check. Exactness of the
// incremental build = 0 bad words over a streaming walk.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public partial class RaymarchFeature
{
    /// `-roofinc 0|1` (default OFF): incremental pyramid upkeep; 0 = the full rebuild every
    /// frame (task 59's build, the cost split's roof_full arm). OFF by default because task 168's
    /// streaming warm-up (`-roofverify 4`, Logs/overnight/168_warmup_Player.log) found the
    /// incremental pyramid != a full rebuild on 774 of 3749 checks (incl. idle frames): an input
    /// the dirty-column set does not track yet. NOT exact under streaming until that is found.
    public static bool RoofIncremental = ReadIntArg("-roofinc", 0) != 0;
    /// `-roofverify N`: compare against a full rebuild every Nth build (0 = never).
    public static int RoofVerifyEvery = ReadIntArg("-roofverify", 0);

    public static long RoofFullBuilds, RoofIncBuilds, RoofIncColumns, RoofIdleFrames;
    public static long RoofVerifyChecks, RoofVerifyBadChecks, RoofVerifyBadWords;

    private static TerrainClipmap _roofClip;
    private static int _roofBufGen, _heightMipGen;
    private static bool _roofValid;
    private static int _roofOriginY = int.MinValue, _roofDimsY, _roofCeil;
    private static long _roofBuildCount;
    private static readonly List<uint> _roofDirty = new List<uint>();
    private static GraphicsBuffer _roofDirtyBuf, _heightMipRef, _heightVerifyOut;
    private static readonly uint[] _verifyZero = new uint[1];

    /// 0 = nothing to build, 1 = full, 2 = incremental (cols filled).
    internal struct RoofPlan { public int mode; public uint[] cols; public int count; public bool verify; }

    /// Record time, once per Raymarch pass. build = the pass builds the pyramid this frame
    /// (roof skip on, or roof_build's build-only arm).
    internal static RoofPlan PlanRoofBuild(TerrainClipmap clip, bool build, int originY, int dimsY, int ceil)
    {
        var plan = new RoofPlan();
        CollectRoofVerify();
        if (!build || clip == null) { _roofValid = false; return plan; }
        _roofDirty.Clear();
        int n = clip.TakeHeightDirtyColumns(_roofDirty);
        int cols = clip.WindowDimsChunks.x * clip.WindowDimsChunks.z;
        bool full = !RoofIncremental || !_roofValid || clip != _roofClip || _roofBufGen != _heightMipGen
                    || originY != _roofOriginY || dimsY != _roofDimsY || ceil != _roofCeil || 2 * n > cols;
        _roofClip = clip; _roofBufGen = _heightMipGen; _roofValid = true;
        _roofOriginY = originY; _roofDimsY = dimsY; _roofCeil = ceil;
        if (full) { plan.mode = 1; RoofFullBuilds++; return plan; }
        if (n > 0)
        {
            plan.mode = 2; plan.count = n; plan.cols = _roofDirty.ToArray();
            RoofIncBuilds++; RoofIncColumns += n;
            if (_roofDirtyBuf == null || _roofDirtyBuf.count < cols)
            {
                _roofDirtyBuf?.Release();
                _roofDirtyBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, cols, sizeof(uint));
            }
        }
        else RoofIdleFrames++;
        // Verify incremental AND idle frames (an idle frame claims the pyramid is still current).
        _roofBuildCount++;
        if (RoofVerifyEvery > 0 && (_roofBuildCount % 1000) == 0) Debug.Log($"[roofverify] {RoofIncSummary()}");
        plan.verify = RoofVerifyEvery > 0 && (_roofBuildCount % RoofVerifyEvery) == 0;
        if (plan.verify && (_heightMipRef == null || _heightMipRef.count != HeightMipBuffer.count))
        {
            _heightMipRef?.Release();
            _heightMipRef = new GraphicsBuffer(GraphicsBuffer.Target.Structured, HeightMipBuffer.count, sizeof(uint));
            _heightVerifyOut ??= new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, sizeof(uint));
        }
        return plan;
    }

    /// Execute time: the incremental dispatches (mode 2). The full build stays in the pass.
    internal static void DispatchRoofIncremental(ComputeCommandBuffer cmd, ComputeShader cs, int kRaw, int kDil, int kLev,
                                                 GraphicsBuffer clipmap, GraphicsBuffer heightMip, RoofPlan plan)
    {
        int n = plan.count;
        cmd.SetBufferData(_roofDirtyBuf, plan.cols, 0, 0, n);
        cmd.SetComputeIntParam(cs, "_HeightDirtyCount", n);
        cmd.SetComputeBufferParam(cs, kRaw, "HeightDirtyCols", _roofDirtyBuf);
        cmd.SetComputeBufferParam(cs, kRaw, "ClipmapBuffer", clipmap);
        cmd.SetComputeBufferParam(cs, kRaw, "HeightMip", heightMip);
        cmd.DispatchCompute(cs, kRaw, 1, 1, n);
        cmd.SetComputeBufferParam(cs, kDil, "HeightDirtyCols", _roofDirtyBuf);
        cmd.SetComputeBufferParam(cs, kDil, "HeightMip", heightMip);
        cmd.DispatchCompute(cs, kDil, 3, 3, n);
        cmd.SetComputeBufferParam(cs, kLev, "HeightDirtyCols", _roofDirtyBuf);
        cmd.SetComputeBufferParam(cs, kLev, "HeightMip", heightMip);
        for (int L = 1; L < 6; L++)
        {
            cmd.SetComputeIntParam(cs, "_HeightLevel", L);
            cmd.DispatchCompute(cs, kLev, 2, 2, n);
        }
    }

    /// Execute time, after the incremental build: the full build into the reference buffer, then
    /// the word compare and an async readback of the count.
    internal static void DispatchRoofVerify(ComputeCommandBuffer cmd, ComputeShader cs, int kRaw, int kDil, int kLev, int kVerify,
                                            GraphicsBuffer clipmap, GraphicsBuffer heightMip, Vector3Int dims, int incCols)
    {
        int gx = (dims.x + 7) / 8, gz = (dims.z + 7) / 8;
        cmd.SetComputeBufferParam(cs, kRaw, "ClipmapBuffer", clipmap);
        cmd.SetComputeBufferParam(cs, kRaw, "HeightMip", _heightMipRef);
        cmd.DispatchCompute(cs, kRaw, gx, gz, 1);
        cmd.SetComputeBufferParam(cs, kDil, "HeightMip", _heightMipRef);
        cmd.DispatchCompute(cs, kDil, gx, gz, 1);
        cmd.SetComputeBufferParam(cs, kLev, "HeightMip", _heightMipRef);
        for (int L = 1; L < 6; L++)
        {
            cmd.SetComputeIntParam(cs, "_HeightLevel", L);
            cmd.DispatchCompute(cs, kLev, Mathf.Max(1, ((dims.x >> L) + 7) / 8), Mathf.Max(1, ((dims.z >> L) + 7) / 8), 1);
        }
        cmd.SetBufferData(_heightVerifyOut, _verifyZero);
        cmd.SetComputeIntParam(cs, "_HeightTotalWords", heightMip.count);
        cmd.SetComputeBufferParam(cs, kVerify, "HeightMip", heightMip);
        cmd.SetComputeBufferParam(cs, kVerify, "HeightMipRef", _heightMipRef);
        cmd.SetComputeBufferParam(cs, kVerify, "HeightVerifyOut", _heightVerifyOut);
        cmd.DispatchCompute(cs, kVerify, (heightMip.count + 63) / 64, 1, 1);
        _verifyPendingCheck = ++RoofVerifyChecks;
        _verifyPendingCols = incCols;
    }

    private static long _verifyPendingCheck;
    private static int _verifyPendingCols;

    /// ComputeCommandBuffer has no async readback, so the count is read back from the main thread at the
    /// NEXT record (the verify frame's commands were submitted by then).
    private static void CollectRoofVerify()
    {
        if (_verifyPendingCheck == 0 || _heightVerifyOut == null) return;
        long check = _verifyPendingCheck; int incCols = _verifyPendingCols;
        _verifyPendingCheck = 0;
        AsyncGPUReadback.Request(_heightVerifyOut, r =>
        {
            if (r.hasError) return;
            uint bad = r.GetData<uint>()[0];
            if (bad != 0)
            {
                RoofVerifyBadChecks++; RoofVerifyBadWords += bad;
                Debug.Log($"[roofverify] check {check}: {bad} words differ from a full rebuild ({(incCols > 0 ? $"incremental, {incCols} columns" : "idle frame")})");
            }
        });
    }

    /// One summary line for logs / reports.
    public static string RoofIncSummary() =>
        $"roof pyramid: full {RoofFullBuilds}, incremental {RoofIncBuilds} ({RoofIncColumns} columns), idle {RoofIdleFrames}; " +
        $"verify {RoofVerifyChecks} checks, {RoofVerifyBadChecks} bad ({RoofVerifyBadWords} words)";
}
