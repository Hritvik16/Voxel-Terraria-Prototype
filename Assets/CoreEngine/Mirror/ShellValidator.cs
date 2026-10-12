// Assets/CoreEngine/Mirror/ShellValidator.cs
//
// Amendment 8.14 §6 "pow2 rings alias" row (engine step 11a, task 77): the
// stale-slot check per shell, in the spirit of ClipmapValidator.CountStaleSlots,
// plus pool accounting and a byte-exact decode of what was uploaded.
//
//   CountStaleSlots   CPU: a tagged ring slot whose chunk does not map to that
//                     slot, or is no longer in the shell's stored set (outside
//                     the box or inside the hole), or whose directory entry
//                     says absent; an untagged slot with a non-zero entry.
//                     This is the §6.2 phantom-terrain failure, per shell.
//   CountPoolErrors   bricks/pages used + free == cap; no body or page owned
//                     twice; every page's dense handles inside the pool.
//   CountGpuDirectoryMismatches  reads DirectoryBuffer back and compares it to
//                     the CPU directory (a lost upload). Needs a real GPU: the
//                     player only (EditMode runs -nographics). Debug only.
//   CompareShadow     decodes ShellStore.ShadowUploads' copy of the directory,
//                     page and bodies for one chunk and compares every voxel
//                     with a freshly generated ShellChunk (tests).
using System;
using Unity.Mathematics;
using VoxelEngine.WorldGen;

namespace VoxelEngine.Mirror
{
    public static class ShellValidator
    {
        public static int CountStaleSlots(ShellStreamer s, int k, out string example)
        {
            example = null;
            var st = s.Store(k);
            int stale = 0;
            for (int slot = 0; slot < st.SlotCount; slot++)
            {
                uint e = st.DirectoryEntry(slot);
                string why = null;
                if (st.SlotTagged(slot, out int3 tag))
                {
                    if (st.Slot(tag) != slot) why = $"tag {tag} maps to slot {st.Slot(tag)}";
                    else if (!s.Wanted(k, tag)) why = $"tag {tag} is outside the stored set";
                    else if (e == ShellStore.ABSENT) why = $"tag {tag} has an ABSENT entry";
                }
                else if (e != ShellStore.ABSENT) why = $"untagged slot holds 0x{e:X8}";
                if (why != null) { stale++; if (example == null) example = $"shell {k} slot {slot}: {why}"; }
            }
            return stale;
        }

        public static int CountPoolErrors(ShellStore st, out string example)
        {
            string first = null;
            int errors = 0;
            void Err(string m) { errors++; if (first == null) first = $"shell {st.Shell}: {m}"; }
            if (st.BricksUsed + st.FreeBricks != st.PoolCap) Err($"bricks used {st.BricksUsed} + free {st.FreeBricks} != cap {st.PoolCap}");
            if (st.PagesUsed + st.FreePages != st.PageCap) Err($"pages used {st.PagesUsed} + free {st.FreePages} != cap {st.PageCap}");
            var pageOwned = new bool[st.PageCap];
            int pages = 0, chunks = 0;
            for (int slot = 0; slot < st.SlotCount; slot++)
            {
                if (!st.SlotTagged(slot, out _)) continue;
                chunks++;
                uint e = st.DirectoryEntry(slot);
                if ((e & ShellStore.PAGE) == 0) continue;
                int p = (int)(e & ~ShellStore.PAGE);
                if (p >= st.PageCap) { Err($"slot {slot} page {p} >= cap"); continue; }
                if (pageOwned[p]) Err($"page {p} owned twice");
                pageOwned[p] = true; pages++;
            }
            if (pages + (st.JobActive ? 1 : 0) != st.PagesUsed) Err($"{pages} pages referenced (+ {(st.JobActive ? 1 : 0)} uploading), {st.PagesUsed} counted used");
            if (chunks != st.ChunksStored) Err($"{chunks} tagged slots, {st.ChunksStored} counted stored");
            example = first;
            return errors;
        }

        /// Debug only: forces a GPU sync. Call after FlushDirectory.
        public static int CountGpuDirectoryMismatches(ShellStore st, out string example)
        {
            example = null;
            var gpu = new uint[st.SlotCount];
            st.DirectoryBuffer.GetData(gpu);
            int bad = 0;
            for (int i = 0; i < gpu.Length; i++)
                if (gpu[i] != st.DirectoryEntry(i)) { bad++; if (example == null) example = $"shell {st.Shell} slot {i}: GPU 0x{gpu[i]:X8} CPU 0x{st.DirectoryEntry(i):X8}"; }
            return bad;
        }

        /// Voxels of chunk `fresh.Coord` that differ between the uploaded
        /// shadow (directory -> page -> body) and `fresh`. -1 = not stored.
        public static int CompareShadow(ShellStore st, TerrainGenChunkSource.ShellChunk fresh, out string example)
        {
            example = null;
            if (st.ShadowDir == null) throw new InvalidOperationException("ShellStore.ShadowUploads was off at construction");
            int slot = st.Slot(fresh.Coord);
            if (!st.Holds(fresh.Coord)) return -1;
            uint e = st.ShadowDir[slot];
            int bad = 0;
            for (int z = 0; z < 128; z++)
            for (int y = 0; y < 128; y++)
            for (int x = 0; x < 128; x++)
            {
                byte got;
                if ((e & ShellStore.PAGE) != 0)
                {
                    int page = (int)(e & ~ShellStore.PAGE);
                    uint h = st.ShadowPages[page * 4096 + ((z >> 3) << 8 | (y >> 3) << 4 | (x >> 3))];
                    if ((h & ShellStore.DENSE) != 0)
                    {
                        int body = (int)(h & 0x3FFFFFFFu);
                        int v = ((z & 7) << 6) | ((y & 7) << 3) | (x & 7);
                        got = (byte)(st.ShadowBricks[body * 128 + (v >> 2)] >> ((v & 3) * 8));
                    }
                    else got = (byte)h;
                }
                else if ((e & ShellStore.UNIFORM) != 0) got = (byte)e;
                else { got = 255; }
                byte want = fresh.Get(x, y, z);
                if (got != want) { bad++; if (example == null) example = $"shell {st.Shell} chunk {fresh.Coord} voxel ({x},{y},{z}): uploaded {got} generated {want} (entry 0x{e:X8})"; }
            }
            return bad;
        }
    }
}
