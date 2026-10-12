// Assets/CoreEngine/Mirror/ShellDebugView.cs
//
// Engine step 11a (task 77): a top-down picture of what each coarse shell
// holds, for screenshots before 11b can render shells. One panel per shell,
// left = shell 1 (finest), each the shell's stored box in its own chunks
// (north up = +z), one cell per chunk column, the camera's column outlined:
//   green   >= 1 non-uniform chunk stored (a page + dense bricks)
//   blue    every wanted chunk stored, all uniform (air / water / solid)
//   yellow  something still missing or in flight
//   red     something refused (pool or pages full, 8.14 row 15)
//   grey    the hole the finer shell covers (nothing stored, by design)
// CPU only (Texture2D.EncodeToPNG), so it also works in EditMode.
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace VoxelEngine.Mirror
{
    public static class ShellDebugView
    {
        private const int CELL = 16, GAP = 12;

        public static void WritePng(ShellStreamer s, string path)
        {
            var panels = new List<(int k, int3 lo, int3 hi, int3 cc)>();
            int W = GAP, H = 0;
            for (int k = s.FirstShell; k <= s.LastShell; k++)
            {
                s.Box(k, out int3 lo, out int3 hi, out int3 cc);
                panels.Add((k, lo, hi, cc));
                W += (hi.x - lo.x + 1) * CELL + GAP;
                H = math.max(H, (hi.z - lo.z + 1) * CELL);
            }
            H += 2 * GAP;
            var px = new Color32[W * H];
            var bg = new Color32(24, 24, 24, 255);
            for (int i = 0; i < px.Length; i++) px[i] = bg;
            int ox = GAP;
            foreach (var (k, lo, hi, cc) in panels)
            {
                var st = s.Store(k);
                for (int z = lo.z; z <= hi.z; z++)
                for (int x = lo.x; x <= hi.x; x++)
                {
                    bool any = false, page = false, missing = false, refused = false;
                    for (int y = lo.y; y <= hi.y; y++)
                    {
                        var c = new int3(x, y, z);
                        if (!s.Wanted(k, c)) continue;
                        any = true;
                        if (st.Holds(c)) { if ((st.DirectoryEntry(st.Slot(c)) & ShellStore.PAGE) != 0) page = true; }
                        else if (s.IsRefused(k, c)) refused = true;
                        else missing = true;
                    }
                    Color32 col = !any ? new Color32(70, 70, 70, 255)
                        : refused ? new Color32(220, 40, 40, 255)
                        : missing ? new Color32(230, 200, 40, 255)
                        : page ? new Color32(60, 170, 70, 255)
                        : new Color32(60, 100, 200, 255);
                    bool cam = x == cc.x && z == cc.z;
                    int x0 = ox + (x - lo.x) * CELL, y0 = GAP + (z - lo.z) * CELL;
                    for (int dy = 0; dy < CELL - 1; dy++)
                    for (int dx = 0; dx < CELL - 1; dx++)
                    {
                        bool edge = dx == 0 || dy == 0 || dx == CELL - 2 || dy == CELL - 2;
                        px[(y0 + dy) * W + x0 + dx] = cam && edge ? new Color32(255, 255, 255, 255) : col;
                    }
                }
                ox += (hi.x - lo.x + 1) * CELL + GAP;
            }
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            tex.SetPixels32(px);
            tex.Apply(false);
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }

        /// One line per shell: what is stored and how full the pools are.
        public static string Report(ShellStreamer s)
        {
            var sb = new System.Text.StringBuilder();
            var L = s.Layout;
            sb.AppendLine($"shells preset {L.Preset} ({L.SizingRows} rows), shells {s.FirstShell}-{s.LastShell}; requested {s.Requested} generated {s.Generated} " +
                          $"stale-dropped {s.DroppedStale} errors {s.GenErrors} over-budget frames {s.OverBudgetFrames}; gen ms/chunk {(s.Generated == 0 ? 0 : s.GenMsTotal / s.Generated):F1}");
            for (int k = s.FirstShell; k <= s.LastShell; k++)
            {
                var st = s.Store(k); var sh = L[k];
                s.Box(k, out int3 lo, out int3 hi, out int3 cc);
                sb.AppendLine($"  shell {k} ({sh.VoxelM:0.0##} m, ring {sh.Ring.x}x{sh.Ring.y}x{sh.Ring.z}, box {lo}..{hi}, hole +-{sh.Hole}): " +
                              $"chunks {st.ChunksStored} (uniform {st.UniformChunks}, pages {st.PagesUsed}/{st.PageCap} peak {st.PeakPages}), " +
                              $"bricks {st.BricksUsed}/{st.PoolCap} peak {st.PeakBricks} (est. {sh.EstBricks}), refusals {st.Refusals} ({st.RefusedBricks} bricks), missing {s.Missing(k)}, " +
                              $"GPU {st.BytesAllocated / 1e6:F1} MB, uploaded {st.BytesUploaded / 1e6:F1} MB");
            }
            return sb.ToString();
        }
    }
}
