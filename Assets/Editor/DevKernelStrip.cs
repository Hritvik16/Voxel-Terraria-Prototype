// Assets/Editor/DevKernelStrip.cs
//
// TASK 194b (owner, 2026-11-13: "speed up development"): BUILD TIME ONLY. Leaves the Raymarch.compute kernels that
// play never dispatches out of PLAYER builds:
//   CSMainFarShadeDiag, CSShellPrepassDiag  the task-134 *Diag twins (counter windows, debug views on the far-shading
//                                           path, _DebugShellSkip arms, -shelldiet 0, -shellmask 1, -shellcolleap 1)
//   CSShellProbe                            task 130's cost-split probe (_DebugShellSkip bit 5)
//   CSMainChecker                           the checkerboard experiment (-checker 1, OFF)
//   CSMainDetail                            task 24's detail square (-detailspeed 1 / F5, OFF; not drawn with shells on)
// Measured (task 194b, all 26 kernels cold at once): CSMainFarShadeDiag 1245 s (the slowest of all 26), CSMainChecker 876 s,
// CSMainDetail 880 s, CSShellProbe 56 s, CSShellPrepassDiag 6 s. Raymarch's compile went 1504 s -> 1129 s wall, 8179 -> 5551 s
// of kernel compile time, with these left out. The shader cache is per kernel, keyed on the kernel's preprocessed source
// (line numbers included): an edit that shifts lines recompiles every kernel; a rebuild after editing anything else
// reuses them all (0.01 s). Table: Screenshots/194b/compile194b.txt.
//
// `VE_ALL_KERNELS=1` in the build's environment keeps all of them (run-cost-split.sh and run-farband.sh set it). Stripping runs ONLY
// while a player is being built: Editor play and the EditMode suite always have every kernel. Nothing in any kept
// kernel changes. The player reads what was left out from BuildStamp's `kernels` line
// (RaymarchFeature.KernelStripped); a run that asks for a missing kernel logs one `[kernels]` warning and draws with
// the kernel play uses (the lean twins; CSMain for -checker / -detailspeed).

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Rendering;
using UnityEngine;

public class DevKernelStrip : IPreprocessComputeShaders
{
    public const string ShaderName = "Raymarch";
    public const string EnvKeepAll = "VE_ALL_KERNELS";

    /// The kernels left out (RaymarchFeature.KernelStripped reads the same names back from the build stamp).
    public static readonly string[] Kernels =
    {
        "CSMainFarShadeDiag", "CSShellPrepassDiag", "CSShellProbe", "CSMainChecker", "CSMainDetail",
    };

    /// False when the build runs with VE_ALL_KERNELS=1.
    public static bool Enabled => Environment.GetEnvironmentVariable(EnvKeepAll) != "1";

    /// BuildStamp's `kernels` value: "all", or "stripped A,B,..".
    public static string StampValue => Enabled ? "stripped " + string.Join(",", Kernels) : "all";

    public int callbackOrder => 0;

    public void OnProcessComputeShader(ComputeShader shader, string kernelName, IList<ShaderCompilerData> data)
    {
        if (!BuildPipeline.isBuildingPlayer || !Enabled || shader == null || shader.name != ShaderName) return;
        if (Array.IndexOf(Kernels, kernelName) < 0) return;
        int n = data.Count;
        data.Clear();
        Debug.Log($"[DevKernelStrip] {ShaderName}.{kernelName}: {n} variant(s) left out of this player build ({EnvKeepAll}=1 keeps them)");
    }
}
