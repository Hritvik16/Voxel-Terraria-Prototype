// Assets/CoreEngine/Rendering/KernelStripStamp.cs
//
// TASK 194b: the build stamp's `kernels` line (written by Assets/Editor/BuildStamp.cs from DevKernelStrip.StampValue)
// read back as kernel names. Kept apart from RaymarchFeature (a URP type) so the EditMode tests can call it.

namespace VoxelEngine.Rendering
{
    public static class KernelStripStamp
    {
        /// `kernels   stripped A,B,..` -> {A, B, ..}; empty for `kernels   all`, no such line (a stamp from before
        /// task 194b), or no stamp at all (Editor play).
        public static string[] Parse(string stamp)
        {
            if (string.IsNullOrEmpty(stamp)) return System.Array.Empty<string>();
            foreach (string raw in stamp.Split('\n'))
            {
                string l = raw.Trim();
                if (!l.StartsWith("kernels ")) continue;
                string v = l.Substring(8).Trim();
                if (!v.StartsWith("stripped ")) return System.Array.Empty<string>();
                var names = new System.Collections.Generic.List<string>();
                foreach (string n in v.Substring(9).Split(','))
                    if (n.Trim().Length > 0) names.Add(n.Trim());
                return names.ToArray();
            }
            return System.Array.Empty<string>();
        }
    }
}
