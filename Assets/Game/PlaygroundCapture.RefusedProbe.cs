// Assets/Game/PlaygroundCapture.RefusedProbe.cs
//
// TASK 194 (DIAGNOSTIC, capture only): which tier-0 chunks the near GPU body pool
// refused at a pose, and what draws them. One `[refused]` line per pose (counts,
// the mirror box, shell 1's hole) and one `[refused] chunk` line per refused chunk
// (on screen only, nearest first, up to 24): its coord, horizontal distance, dense bricks, whether
// the chunk below it is resident and solid on the GPU, whether shell 1 STORES its
// shell-1 chunk (Wanted: false = in shell 1's hole) and where its top centre lands
// in a 1920 x 1080 shot (image y down). Nothing here changes what is drawn.
using System.Collections.Generic;
using System.Text;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Memory;
using VoxelEngine.Mirror;

public partial class PlaygroundCapture
{
    private static List<string> RefusedProbeLines(Camera cam, ShellStreamer sh)
    {
        var lines = new List<string>();
        var clip = Phase4Bootstrapper.Clipmap;
        var store = Phase4Bootstrapper.Store;
        if (clip == null || !clip.GpuPoolEnabled || store == null) return lines;
        int3 o = clip.WindowOrigin, d = clip.WindowDimsChunks;
        string hole = "-";
        if (sh != null && sh.PublishedBox(1, out int3 lo1, out int3 hi1, out int3 cc1))
            hole = $"shell 1 cam chunk {cc1} hole +-{sh.Layout[1].Hole} (25.6 m chunks: tier-0 x/z {cc1.x * 2 - sh.Layout[1].Hole * 2}..{cc1.x * 2 + sh.Layout[1].Hole * 2 + 1} / {cc1.z * 2 - sh.Layout[1].Hole * 2}..{cc1.z * 2 + sh.Layout[1].Hole * 2 + 1})";
        lines.Add($"  [refused] now {clip.GpuPoolRefusedNow:N0}, total {clip.GpuPoolRefusedTotal:N0}, in use {clip.GpuPoolInUse:N0} of {clip.GpuPoolCapacity:N0} | " +
                  $"box chunks {o}..{o + d - 1} | {hole}");
        var list = new List<int3>(clip.GpuRefusedChunks);
        Vector3 e = cam.transform.position;
        float H(int3 c) => math.length(new float2(c.x * 12.8f + 6.4f - e.x, c.z * 12.8f + 6.4f - e.z));
        list.Sort((a, b) => H(a).CompareTo(H(b)));
        int n = 0, inHole = 0, belowSolid = 0;
        foreach (int3 c in list)
        {
            var ch = store.GetChunk(c);
            int dense = 0;
            if (ch != null && !ch.isUniform && ch.bricks != null)
                for (int i = 0; i < EngineConfig.BRICKS_PER_CHUNK; i++) if ((ch.bricks[i].data & 0x80000000u) != 0) dense++;
            int3 below = c - new int3(0, 1, 0);
            var chB = store.GetChunk(below);
            bool bSolid = chB != null && !clip.IsGpuRefused(below) && (!chB.isUniform || chB.uniformMaterial != 0);
            bool wanted1 = sh != null && sh.Wanted(1, c >> 1);
            if (!wanted1) inHole++;
            if (bSolid) belowSolid++;
            Vector3 top = new Vector3(c.x * 12.8f + 6.4f, c.y * 12.8f + 12.8f, c.z * 12.8f + 6.4f);
            Vector3 vp = cam.WorldToViewportPoint(top);
            bool onScreen = vp.z > 0f && vp.x >= 0f && vp.x <= 1f && vp.y >= 0f && vp.y <= 1f;
            if (!onScreen || n++ >= 24) continue;   // on-screen ones only, nearest first
            var sb = new StringBuilder();
            sb.Append($"  [refused] chunk {c} world x {c.x * 12.8f:F1}..{c.x * 12.8f + 12.8f:F1} y {c.y * 12.8f:F1}..{c.y * 12.8f + 12.8f:F1} z {c.z * 12.8f:F1}..{c.z * 12.8f + 12.8f:F1}");
            sb.Append($" | dist {H(c):F0} m | dense {dense} | below {(chB == null ? "absent" : chB.isUniform ? $"uniform {chB.uniformMaterial}" : "dense")}{(bSolid ? " (tier 0 draws it)" : "")}");
            sb.Append($" | shell 1 {(wanted1 ? "STORES it" : "HOLE (stores nothing)")}");
            sb.Append($" | top centre at px ({vp.x * 1920f:F0}, {(1f - vp.y) * 1080f:F0})");
            lines.Add(sb.ToString());
        }
        if (list.Count > 0) lines.Add($"  [refused] of {list.Count}: on screen {n}, in shell 1's hole {inHole}, chunk below drawn solid by tier 0 {belowSolid}");
        return lines;
    }
}
