// ==========================================
// Assets/CoreEngine/Diagnostics/ShadowRayDump.cs
//
// PHASE 7, §10.4's per-subsystem diagnostic dump, for the shadow path.
//
// §10.4, verbatim, on the raymarcher's form of this:
//
//     "Raymarcher: if a single ray's step count exceeds a threshold (e.g.
//      >1000), log its origin, direction, and the last ~10 clipmap indices
//      traversed."
//
// and on why the mechanism exists at all:
//
//     "This converts a vague failure ('physics feels wrong,' 'memory creeping')
//      into the exact state that caused it -- the single highest-leverage
//      debugging aid when code is generated fast (by you or an AI) and bugs
//      arrive fast."
//
// SAME SHAPE AS THE EXISTING RAYMARCHER DUMP, deliberately. Raymarch.compute
// fills DebugOut for the one pixel at RaymarchFeature.DebugPixel;
// RaymarchGpuDebugReadback reads it back and logs it. This is that, for the
// shadow ray, plus the automatic threshold trigger §10.4 asks for.
//
// SLOT MAP (DebugOut is 128 floats; 0-19 are the pre-Phase-7 raymarcher dump
// and are NOT touched by any of this):
//   20      shadow-ray outer steps
//   21-23   voxel the shadow ray ended on
//   24-26   direction it travelled (= sun direction)
//   27-29   §6.5's origin -- the hit voxel one cell off along the face normal
//   30      sunVis          31  skyVis
//   32      C.10's m (minimum clearance/travel ratio), -1 = no near miss
//   33      shadow quality level in force
//   34-43   the last 10 clipmap flat indices traversed  (§10.4's ask)
//   44      active light count     45  whether the primary ray hit at all
//
// ASYNC, NOT BLOCKING. GraphicsBuffer.GetData stalls the CPU until the GPU
// catches up, which is exactly the wrong thing to do every frame in something
// whose entire job is to observe frame behaviour without changing it.
// AsyncGPUReadback keeps this off the critical path; the dump lands a few
// frames late and says so.
// ==========================================

using System;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace VoxelEngine.Diagnostics
{
    public class ShadowRayDump : MonoBehaviour
    {
        [Tooltip("Outer iterations above which a shadow ray is considered pathological " +
                 "and dumped. §10.4 suggests >1000 for the primary raymarcher; a shadow " +
                 "ray shares the clipmap and the leap primitives, so it shares the number.")]
        [SerializeField] private int _stepThreshold = 1000;

        [Tooltip("Frames between readbacks. This is a diagnostic, not a monitor -- " +
                 "sampling every frame would queue a readback per frame for a condition " +
                 "that is either persistent or not happening.")]
        [SerializeField] private int _sampleEveryFrames = 30;

        [Tooltip("Stop after this many dumps, so a genuinely pathological pose cannot " +
                 "fill the disk while nobody is watching. 0 = unlimited.")]
        [SerializeField] private int _maxDumps = 20;

        [Tooltip("Also dump on demand regardless of the threshold (the context menu).")]
        [SerializeField] private bool _logEveryRead = false;

        private int _dumps;
        private int _frame;
        private bool _pending;

        /// Where dumps are written. One directory, timestamped per session, so a
        /// run's dumps stay together instead of interleaving with a later run's.
        public static string DumpDirectory { get; private set; }

        /// Count of dumps written this session. A rig asserts on this rather
        /// than scanning the filesystem.
        public static int DumpsWritten { get; private set; }

        /// The most recent dump's text, for a rig that wants to embed it in its
        /// own report rather than point at a file.
        public static string LastDump { get; private set; }

        void Update()
        {
            if (RaymarchFeature.DebugBuffer == null) return;
            if (_maxDumps > 0 && _dumps >= _maxDumps) return;
            if (_pending) return;
            if ((_frame++ % Mathf.Max(1, _sampleEveryFrames)) != 0) return;

            _pending = true;
            AsyncGPUReadback.Request(RaymarchFeature.DebugBuffer, OnReadback);
        }

        private void OnReadback(AsyncGPUReadbackRequest req)
        {
            _pending = false;
            if (req.hasError) return;

            var data = req.GetData<float>();
            if (data.Length < 46) return;

            var d = new float[46];
            for (int i = 0; i < 46; i++) d[i] = data[i];

            int steps = Mathf.RoundToInt(d[20]);
            bool over = steps > _stepThreshold;
            if (!over && !_logEveryRead) return;

            string text = Format(d, steps, over);
            LastDump = text;
            Write(text);
        }

        /// Formats one dump. Split out from the readback so a rig or a test can
        /// format a hand-built record without a GPU.
        public static string Format(float[] d, int steps, bool overThreshold)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== SHADOW RAY DUMP (§10.4) ===");
            sb.AppendLine($"reason          : {(overThreshold ? "step count over threshold" : "manual read")}");
            sb.AppendLine($"time            : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"pixel           : {RaymarchFeature.DebugPixel.x},{RaymarchFeature.DebugPixel.y} " +
                          "(DISPATCH coordinates, not screen -- check the dispatch line if these look wrong)");
            sb.AppendLine($"primary hit     : {(d[45] > 0.5f ? "yes" : "NO -- this is a sky pixel and should have fired no shadow ray at all")}");
            sb.AppendLine($"shadow steps    : {steps}");
            sb.AppendLine($"quality level   : {Mathf.RoundToInt(d[33])} " +
                          "(0 full penumbra, 1 half-res distant, 2 hard only, 3 off)");
            sb.AppendLine($"origin voxel    : {Mathf.RoundToInt(d[27])},{Mathf.RoundToInt(d[28])},{Mathf.RoundToInt(d[29])}" +
                          "   <- §6.5's rule: one cell off the hit face. If this equals the HIT voxel, that is the acne bug.");
            sb.AppendLine($"ended at voxel  : {Mathf.RoundToInt(d[21])},{Mathf.RoundToInt(d[22])},{Mathf.RoundToInt(d[23])}");
            sb.AppendLine($"direction (sun) : {d[24]:F5},{d[25]:F5},{d[26]:F5}");
            sb.AppendLine($"sunVis          : {d[30]:F4}");
            sb.AppendLine($"skyVis          : {d[31]:F4}");
            sb.AppendLine($"C.10 m          : {(d[32] < 0f ? "(no near-miss cell seen)" : d[32].ToString("F6"))}");
            sb.AppendLine($"active lights   : {Mathf.RoundToInt(d[44])} / {EngineConfig.MAX_ACTIVE_LIGHTS}");
            sb.AppendLine("last clipmap indices traversed (oldest first within the ring):");
            for (int i = 0; i < 10; i++)
                sb.AppendLine($"    [{i}] {Mathf.RoundToInt(d[34 + i])}");
            sb.AppendLine();
            sb.AppendLine("HOW TO READ THIS:");
            sb.AppendLine("  - Repeated identical indices => the ray is not advancing (a leap that returns");
            sb.AppendLine("    the same voxel, which is the stalled-ray failure the §10.3 heat view shows red).");
            sb.AppendLine("  - An origin equal to the hit voxel => §6.5's origin rule was violated and every");
            sb.AppendLine("    lit surface will report shadow. Fix the ORIGIN; §13 says explicitly not to add a bias.");
            sb.AppendLine("  - A high step count with sunVis 1.0 => the ray ran out of budget rather than");
            sb.AppendLine("    reaching its clamp, and 'lit' here means 'gave up', not 'unoccluded'.");
            return sb.ToString();
        }

        private void Write(string text)
        {
            try
            {
                if (DumpDirectory == null)
                {
                    DumpDirectory = Path.Combine(Application.persistentDataPath, "ShadowRayDumps",
                                                 DateTime.Now.ToString("yyyy-MM-dd_HHmmss"));
                    Directory.CreateDirectory(DumpDirectory);
                }
                string file = Path.Combine(DumpDirectory, $"shadow_{_dumps:000}.txt");
                File.WriteAllText(file, text);
                _dumps++;
                DumpsWritten++;
                Debug.LogWarning($"[ShadowRayDump] wrote {file}\n{text}");
            }
            catch (Exception e)
            {
                // A diagnostic that can kill the frame it is diagnosing is worse
                // than no diagnostic.
                Debug.LogWarning("[ShadowRayDump] could not write dump: " + e.Message);
            }
        }

        [ContextMenu("Dump Shadow Ray Now")]
        public void DumpNow()
        {
            _logEveryRead = true;
            _frame = 0;
            _pending = false;
        }
    }
}
