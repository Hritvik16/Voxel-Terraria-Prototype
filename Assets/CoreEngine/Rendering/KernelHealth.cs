// Assets/CoreEngine/Rendering/KernelHealth.cs
//
// TASK 206 (bug from task 198): a Raymarch kernel that FAILS TO LOAD at run time must be loud.
// In a player, CSMainFarShade (debug views 1-14, the debug pixel) failed on a bool -> float select Metal rejects,
// and the torch-flood twins on 33 Metal buffer slots (the cap is 31). Metal compiles a kernel when it is first
// dispatched; the error goes to Player.log from native code only, so:
//   * ComputeShader.IsSupported(kernel) said TRUE for both broken kernels (task 206 launch A);
//   * Application.logMessageReceived never sees the lines (launch A: 0 of 6 error lines);
//   * the dispatch then does nothing: the frame keeps the last good image and DebugBuffer keeps old numbers.
// What does see it is the GPU itself: a HEARTBEAT. Every kernel outside diet 2 writes the debug pixel's ray
// direction into DebugBuffer[0..2]. Until a kernel is VERIFIED, RaymarchFeature fills those three slots with NaN
// before its dispatch and reads them back (async, at the next record): NaN = the kernel did not run. One NaN
// marks the kernel FAILED for the process (a Metal compile failure is permanent): ONE error line per kernel, its
// frames painted MAGENTA, RaymarchFeature.KernelFailFrames counted. VerifyReads good reads mark it verified and
// the check stops (nothing per frame once every kernel in use has run). The diet-2 play kernels have no
// DebugOut and are not checked (their code must not change); IsSupported is still asked for every kernel.
// Kept apart from RaymarchFeature (a URP type) so the EditMode tests can drive the state machine.

using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.Rendering
{
    public static class KernelHealth
    {
        public enum State { Unknown, Verified, Failed }
        /// Good heartbeat reads before a kernel counts as verified.
        public const int VerifyReads = 2;

        /// The state machine, CPU-testable: one read of DebugBuffer[0] after a dispatch of the kernel.
        public static State Next(State s, ref int goodReads, float slot0)
        {
            if (s != State.Unknown) return s;            // both ends are sticky
            if (float.IsNaN(slot0)) return State.Failed; // the kernel did not overwrite the NaN: it did not run
            return ++goodReads >= VerifyReads ? State.Verified : State.Unknown;
        }

        /// Is the frame's kernel broken? Not supported per the API, or the heartbeat saw it not run.
        public static bool Decide(bool isSupported, State s) => !isSupported || s == State.Failed;

        /// Is the debug pixel inside the dispatch (only then does the kernel write it)?
        public static bool PixelInside(Vector2Int px, int w, int h) => px.x >= 0 && px.y >= 0 && px.x < w && px.y < h;

        class Rec { public bool asked, supported; public State state; public int good; public string name; }
        static readonly Dictionary<(int shader, int kernel), Rec> _recs = new Dictionary<(int, int), Rec>();
        static readonly HashSet<string> _reported = new HashSet<string>();
        public static int HeartbeatReads { get; private set; }

        static Rec Get(ComputeShader cs, int kernel)
        {
            var key = (cs.GetInstanceID(), kernel);
            if (!_recs.TryGetValue(key, out Rec r)) { r = new Rec { name = NameOf(cs, kernel) }; _recs[key] = r; }
            if (!r.asked) { r.asked = true; r.supported = cs.IsSupported(kernel); }
            return r;
        }

        /// Did `kernel` of `cs` fail to load? Logs ONE error per kernel the first time it is true.
        public static bool Failed(ComputeShader cs, int kernel)
        {
            if (cs == null || kernel < 0) return false;
            Rec r = Get(cs, kernel);
            bool failed = Decide(r.supported, r.state);
            if (failed && _reported.Add(r.name ?? $"#{kernel}"))
                Debug.LogError($"[kernels] FAILED: {cs.name}.{r.name ?? "#" + kernel} did not load on this GPU " +
                    $"({(r.supported ? "the heartbeat read NaN: it never ran; Player.log holds Metal's 'program_source' error" : "ComputeShader.IsSupported = false")}). " +
                    "Its frames are painted MAGENTA and DebugBuffer's ray slots read NaN (task 206), instead of silently keeping the last good image.");
            return failed;
        }

        /// Should this frame's dispatch of `kernel` carry the heartbeat (not yet verified, not failed)?
        public static bool NeedsHeartbeat(ComputeShader cs, int kernel) =>
            cs != null && kernel >= 0 && Get(cs, kernel).state == State.Unknown;

        /// The heartbeat's read: DebugBuffer[0] after a dispatch of `kernel`.
        public static void Report(ComputeShader cs, int kernel, float slot0)
        {
            if (cs == null) return;
            HeartbeatReads++;
            Rec r = Get(cs, kernel);
            State before = r.state;
            r.state = Next(r.state, ref r.good, slot0);
            if (before != State.Verified && r.state == State.Verified)
                Debug.Log($"[kernels] {cs.name}.{r.name ?? "#" + kernel} verified: it ran ({VerifyReads} heartbeat reads)");
        }

        /// Raymarch.compute's kernel names (KernelHealthTests pins this list to the shader's #pragma kernel lines).
        public static readonly string[] RaymarchKernels =
        {
            "CSMain", "BuildHeightRaw", "BuildHeightDilate", "BuildHeightLevel", "FarScatter", "FarMipBuild", "TaaResolve",
            "TaaSharpen", "CSMainChecker", "CSMainDetail", "CSMainFarShade", "CSShellProbe", "CSShellPrepass",
            "CSMainFarShadeDiag", "CSShellPrepassDiag", "CSMainFlood", "CSMainFarShadeFlood", "CSMainFarShadeLean",
            "CSShellPrepassLean", "CSMainFarShadeLeanRoof", "BuildHeightRawCols", "BuildHeightDilateCols",
            "BuildHeightLevelCols", "HeightVerify", "CSMainFarShadeLeanSun", "CSSunDeferLean",
            "BloomDown", "BloomBlur", "BloomAdd",   // task 226
            "CSSkyFillTrace",                       // task 227
            "CSSunTrace",                           // task 232
        };

        /// The kernel's name (for the log line), or null if it is not one of RaymarchKernels.
        public static string NameOf(ComputeShader cs, int kernel)
        {
            if (cs == null) return null;
            var key = (cs.GetInstanceID(), kernel);
            if (_recs.TryGetValue(key, out Rec r) && r.name != null) return r.name;
            foreach (string k in RaymarchKernels)
                if (cs.HasKernel(k) && cs.FindKernel(k) == kernel) return k;
            return null;
        }

        /// One line for a capture report.
        public static string Summary()
        {
            var v = new List<string>(); var f = new List<string>(); var u = new List<string>();
            foreach (var kv in _recs)
            {
                string n = kv.Value.name ?? "#" + kv.Key.kernel;
                if (!kv.Value.supported || kv.Value.state == State.Failed) f.Add(n);
                else if (kv.Value.state == State.Verified) v.Add(n);
                else u.Add(n);
            }
            return $"[kernels] heartbeat reads {HeartbeatReads}; verified {(v.Count == 0 ? "none" : string.Join(",", v))}; " +
                   $"FAILED {(f.Count == 0 ? "none" : string.Join(",", f))}; unchecked {(u.Count == 0 ? "none" : string.Join(",", u))}";
        }
    }
}
