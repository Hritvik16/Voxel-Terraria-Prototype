// Assets/CoreEngine/Mirror/UploadShadow.cs
//
// TASK 168e: `-uploadcheck`. A CPU copy of every word the hooked mirror writes put into a GPU buffer, applied
// in call order -- i.e. exactly what sequential SetData leaves there -- whether the ring is on or off.
// Compare() reads the GPU buffers back (synchronous GetData, check launches only) and counts the written
// words that differ. With the ring OFF it is the control (SetData vs itself); with it ON it proves the
// staged copies land byte for byte. Memory: 16 KiB + 512 B per touched 4,096-word page (check launches only).

using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace VoxelEngine.Memory
{
    public static class UploadShadow
    {
        public static bool Enabled;
        private const int PAGE = 4096;

        private sealed class Buf
        {
            public string Name;
            public readonly Dictionary<int, uint[]> Pages = new Dictionary<int, uint[]>();
            public readonly Dictionary<int, ulong[]> Masks = new Dictionary<int, ulong[]>();
        }

        private static readonly Dictionary<GraphicsBuffer, Buf> _bufs = new Dictionary<GraphicsBuffer, Buf>();
        public static long WordsRecorded;

        private static Buf Get(GraphicsBuffer dst, string name)
        {
            if (!_bufs.TryGetValue(dst, out var b)) { b = new Buf { Name = name }; _bufs[dst] = b; }
            return b;
        }

        private static void Put(Buf b, int at, uint v)
        {
            int p = at >> 12, o = at & (PAGE - 1);
            if (!b.Pages.TryGetValue(p, out var page)) { page = new uint[PAGE]; b.Pages[p] = page; b.Masks[p] = new ulong[PAGE / 64]; }
            page[o] = v;
            b.Masks[p][o >> 6] |= 1UL << (o & 63);
        }

        public static void Record(GraphicsBuffer dst, string name, uint[] src, int srcStart, int dstStart, int count)
        {
            var b = Get(dst, name);
            for (int i = 0; i < count; i++) Put(b, dstStart + i, src[srcStart + i]);
            WordsRecorded += count;
        }

        public static void Record(GraphicsBuffer dst, string name, int[] src, int srcStart, int dstStart, int count)
        {
            var b = Get(dst, name);
            for (int i = 0; i < count; i++) Put(b, dstStart + i, unchecked((uint)src[srcStart + i]));
            WordsRecorded += count;
        }

        public static void Record(GraphicsBuffer dst, string name, NativeArray<uint> src, int srcStart, int dstStart, int count)
        {
            var b = Get(dst, name);
            for (int i = 0; i < count; i++) Put(b, dstStart + i, src[srcStart + i]);
            WordsRecorded += count;
        }

        /// Forget a buffer (released / replaced): its words can no longer be read back.
        public static void Forget(GraphicsBuffer dst) { if (dst != null) _bufs.Remove(dst); }

        /// GPU vs shadow over every recorded word. One line per buffer in `lines`.
        public static long Compare(List<string> lines, out long checkedWords)
        {
            checkedWords = 0;
            long bad = 0;
            var tmp = new uint[PAGE];
            foreach (var kv in _bufs)
            {
                var gb = kv.Key; var b = kv.Value;
                if (gb == null || !gb.IsValid()) { lines.Add($"    {b.Name}: released, skipped"); continue; }
                long words = 0, diff = 0; string ex = null;
                foreach (var pp in b.Pages)
                {
                    int p = pp.Key, start = p * PAGE;
                    int n = Mathf.Min(PAGE, gb.count - start);
                    if (n <= 0) continue;
                    gb.GetData(tmp, 0, start, n);
                    var page = pp.Value; var mask = b.Masks[p];
                    for (int o = 0; o < n; o++)
                    {
                        if ((mask[o >> 6] & (1UL << (o & 63))) == 0) continue;
                        words++;
                        if (tmp[o] != page[o]) { diff++; if (ex == null) ex = $"word {start + o}: GPU 0x{tmp[o]:X8} shadow 0x{page[o]:X8}"; }
                    }
                }
                checkedWords += words; bad += diff;
                lines.Add($"    {b.Name}: {words} words in {b.Pages.Count} pages, {diff} differ" + (ex != null ? $" (first: {ex})" : ""));
            }
            return bad;
        }
    }
}
