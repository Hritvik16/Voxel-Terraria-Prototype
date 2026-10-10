// Assets/Game/MemoryLedger.cs
//
// DIAGNOSTIC ONLY (2026-10-01). Attributes the process's memory to the
// engine objects that own it, so a phys_footprint peak (run-tg2world.sh's
// `footprint` breakdown: IOAccelerator = Metal buffers, Untagged = Unity's
// native heap) can be split by owner instead of guessed at.
//
// HOW: walks a fixed list of ROOTS (the bootstrapper's statics, the far band,
// the fluid sim, RaymarchFeature's statics, the lighting driver) by
// reflection, descending into fields of this project's own types, arrays and
// lists, and sums what it finds:
//   GPU     GraphicsBuffer / ComputeBuffer (count * stride), RenderTexture /
//           Texture2D / Texture3D (dimensions * block size)
//   NATIVE  NativeArray<T> (Length * sizeof(T))
//   MANAGED arrays (Length * element size; reference elements count a pointer)
// Each object is counted ONCE, for the first root that reaches it, so roots
// are listed owners-first. Nothing is allocated in the engine and no engine
// type is changed; the walk itself allocates a visited set (a few MB at most).
//
// WHAT IT DOES NOT SEE: URP's own render targets and Unity's internal
// allocations, managed object headers, and anything not reachable from a
// root. The report prints Unity's own totals beside the sum so the unseen
// remainder is a number, not an assumption.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

public static class MemoryLedger
{
    public struct Totals { public long gpu, native, managed; }

    private sealed class RefEq : IEqualityComparer<object>
    {
        public new bool Equals(object a, object b) => ReferenceEquals(a, b);
        public int GetHashCode(object o) => RuntimeHelpers.GetHashCode(o);
    }

    private const int MAX_DEPTH = 6;
    private const BindingFlags ALL = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags STATIC = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    /// One line per root plus a total; MB with one decimal.
    public static List<string> Report()
    {
        var roots = new List<(string name, object obj, Type staticsOf)>
        {
            ("TerrainClipmap (GPU mirror)", Phase4Bootstrapper.Clipmap, null),
            ("BrickDataPool (CPU bricks)", Phase4Bootstrapper.Pool, null),
            ("ChunkStore", Phase4Bootstrapper.Store, null),
            ("LODCascadeManager", Phase4Bootstrapper.Cascades, null),
            ("StreamManager (+TerrainGen map, workers)", Phase4Bootstrapper.Streamer, null),
            ("CoalesceScheduler", Phase4Bootstrapper.Coalescer, null),
            ("FarField (far band)", VoxelEngine.Rendering.FarField.Active, null),
            ("Fluid simulation", UnityEngine.Object.FindAnyObjectByType<Playground>()?.DebugFluid, null),
            ("LightingDriver", UnityEngine.Object.FindAnyObjectByType<VoxelEngine.Rendering.LightingDriver>(), null),
            ("RaymarchFeature statics", null, typeof(RaymarchFeature)),
        };
        var visited = new HashSet<object>(new RefEq());
        var lines = new List<string>();
        Totals all = default;
        lines.Add($"  {"owner",-44}{"GPU MB",10}{"native MB",11}{"managed MB",12}");
        foreach (var (name, obj, staticsOf) in roots)
        {
            Totals t = default;
            try
            {
                if (staticsOf != null)
                    foreach (var f in staticsOf.GetFields(STATIC)) Walk(SafeGet(f, null), f.FieldType, 0, visited, ref t);
                else if (obj != null) Walk(obj, obj.GetType(), 0, visited, ref t);
                else { lines.Add($"  {name,-44}  (not present)"); continue; }
            }
            catch (Exception e) { lines.Add($"  {name,-44}  WALK FAILED: {e.GetType().Name}: {e.Message}"); continue; }
            all.gpu += t.gpu; all.native += t.native; all.managed += t.managed;
            lines.Add($"  {name,-44}{MB(t.gpu),10}{MB(t.native),11}{MB(t.managed),12}");
        }
        lines.Add($"  {"SUM of the above",-44}{MB(all.gpu),10}{MB(all.native),11}{MB(all.managed),12}   = {MB(all.gpu + all.native + all.managed)} MB");
        lines.Add($"  Unity totals: allocated {MB(UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong())} MB, " +
                  $"reserved {MB(UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong())} MB, " +
                  $"graphics driver {MB(UnityEngine.Profiling.Profiler.GetAllocatedMemoryForGraphicsDriver())} MB, " +
                  $"managed heap {MB(GC.GetTotalMemory(false))} MB  (Profiler figures may read 0 in a release player)");
        lines.AddRange(PoolReport());
        return lines;
    }

    /// POOL PEAKS (2026-10-10, engine plan step 4). Capacity, in-use and the
    /// high-water mark of every fixed pool, plus what each would cost if its
    /// cap were set to peak x 1.25 -- the numbers steps 5 (fluid tiles) and 6
    /// (cascade caps) size against. Reads counters only; changes nothing.
    /// Bytes per unit: a brick is 512 B of CPU array + 512 B of GPU mirror
    /// (BrickDataPool / TerrainClipmap / CascadeTierPool all allocate
    /// capacity * 512 B). A fluid tile is its cells in the four per-cell GPU
    /// buffers (claim, slotAt, reacted, wakeMark: 16 B/cell) plus 40 B of
    /// per-tile directory (tileCoords 16 + activeTileSlots 4, GPU and CPU).
    public static List<string> PoolReport()
    {
        var lines = new List<string>
        {
            "",
            "  --- POOL PEAKS (capacity / in use / peak since start; MB = whole cap and cap at peak x 1.25) ---",
            $"  {"pool",-22}{"capacity",10}{"in use",10}{"peak",10}{"peak/cap",10}{"B/unit",8}{"cap MB",9}{"1.25pk MB",11}",
        };
        void Row(string name, int cap, int inUse, int peak, long bytesPerUnit)
        {
            long at125 = (long)Math.Ceiling(peak * 1.25);
            lines.Add($"  {name,-22}{cap,10}{inUse,10}{peak,10}{(cap > 0 ? 100.0 * peak / cap : 0),9:F1}%{bytesPerUnit,8}" +
                      $"{MB(cap * bytesPerUnit),9}{MB(at125 * bytesPerUnit),11}");
        }
        void BrickRow(string name, VoxelEngine.Memory.BrickDataPool p)
        {
            if (p == null) { lines.Add($"  {name,-22}  (not present)"); return; }
            Row(name, p.Capacity, p.InUse, p.PeakUsed, 1024);
        }
        BrickRow("tier0 bricks", Phase4Bootstrapper.Pool);
        var casc = Phase4Bootstrapper.Cascades;
        for (int t = 1; t < LODConfig.TIER_COUNT; t++)
            BrickRow($"cascade tier {t}", casc?.TierPool(t)?.BrickPool);
        var tiles = UnityEngine.Object.FindAnyObjectByType<Playground>()?.DebugTiles;
        if (tiles == null) lines.Add($"  {"fluid tiles",-22}  (not present)");
        else
        {
            Row($"fluid tiles ({tiles.TileEdge}^3)", tiles.TileCapacity, tiles.ResidentTiles, tiles.PeakResidentTiles,
                (long)tiles.TileCells * 16 + 40);
            lines.Add($"  fluid tiles refused (pool full, since start): {tiles.PoolExhaustionsTotal:N0}   " +
                      $"acquired {tiles.TilesAcquiredTotal:N0}  released {tiles.TilesReleasedTotal:N0}");
        }
        return lines;
    }

    private static string MB(long b) => (b / (1024.0 * 1024.0)).ToString("F1");

    private static object SafeGet(FieldInfo f, object o)
    {
        try { return f.GetValue(o); } catch { return null; }
    }

    private static bool IsOurs(Type t)
    {
        string asm = t.Assembly.GetName().Name;
        return asm == "CoreEngine" || asm == "TerrainGen" || asm == "ContentModules" || asm == "Assembly-CSharp";
    }

    private static void Walk(object o, Type declared, int depth, HashSet<object> visited, ref Totals t)
    {
        if (o == null || depth > MAX_DEPTH) return;
        Type type = o.GetType();

        // Value types: NativeArray<T> is a struct; other structs are walked for
        // the buffers they hold (pass-data structs, job structs).
        if (type.IsValueType)
        {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(NativeArray<>))
            {
                var created = type.GetProperty("IsCreated");
                if (created != null && !(bool)created.GetValue(o)) return;
                int len = (int)type.GetProperty("Length").GetValue(o);
                t.native += (long)len * UnsafeUtility.SizeOf(type.GetGenericArguments()[0]);
                return;
            }
            if (IsOurs(type) && !type.IsPrimitive && !type.IsEnum)
                foreach (var f in type.GetFields(ALL))
                    if (!f.FieldType.IsPrimitive && !f.FieldType.IsEnum && !f.FieldType.IsPointer)
                        Walk(SafeGet(f, o), f.FieldType, depth + 1, visited, ref t);
            return;
        }

        if (o is string || o is Delegate || o is Type) return;
        if (!visited.Add(o)) return;

        switch (o)
        {
            case GraphicsBuffer gb:
                if (gb.IsValid()) t.gpu += (long)gb.count * gb.stride;
                return;
            case ComputeBuffer cb:
                if (cb.IsValid()) t.gpu += (long)cb.count * cb.stride;
                return;
            case RenderTexture rt:
                t.gpu += (long)rt.width * rt.height * Math.Max(1, rt.volumeDepth) * GraphicsFormatUtility.GetBlockSize(rt.graphicsFormat);
                return;
            case Texture2D t2:
                t.gpu += (long)t2.width * t2.height * GraphicsFormatUtility.GetBlockSize(t2.graphicsFormat);
                return;
            case Texture3D t3:
                t.gpu += (long)t3.width * t3.height * t3.depth * GraphicsFormatUtility.GetBlockSize(t3.graphicsFormat);
                return;
            case Array arr:
            {
                Type et = type.GetElementType();
                // Unmanaged elements hold no buffers worth walking (and an array
                // of millions must not be iterated by reflection): size only.
                if (et.IsPrimitive || et.IsEnum || (et.IsValueType && UnsafeUtility.IsUnmanaged(et)))
                {
                    t.managed += (long)arr.LongLength * UnsafeUtility.SizeOf(et);
                    return;
                }
                t.managed += arr.LongLength * IntPtr.Size;
                foreach (var e in arr) Walk(e, et, depth + 1, visited, ref t);
                return;
            }
            case IDictionary dict:
                foreach (DictionaryEntry e in dict) { Walk(e.Key, null, depth + 1, visited, ref t); Walk(e.Value, null, depth + 1, visited, ref t); }
                // fall through to fields is unnecessary: the backing arrays are internal
                return;
            case IList list when !IsOurs(type):
            {
                // List<T>: count its backing array (capacity) once; walk the
                // elements only when they can own buffers (reference types).
                var items = type.GetField("_items", ALL);
                if (items != null && SafeGet(items, o) is Array backing)
                {
                    Type bt = backing.GetType().GetElementType();
                    bool flat = bt.IsPrimitive || bt.IsEnum || (bt.IsValueType && UnsafeUtility.IsUnmanaged(bt));
                    t.managed += backing.LongLength * (flat ? UnsafeUtility.SizeOf(bt) : IntPtr.Size);
                    if (flat) return;
                }
                foreach (var e in list) Walk(e, null, depth + 1, visited, ref t);
                return;
            }
        }

        if (!IsOurs(type)) return;
        for (Type k = type; k != null && k != typeof(object) && k != typeof(MonoBehaviour); k = k.BaseType)
            foreach (var f in k.GetFields(ALL | BindingFlags.DeclaredOnly))
            {
                Type ft = f.FieldType;
                if (ft.IsPrimitive || ft.IsEnum || ft.IsPointer) continue;
                Walk(SafeGet(f, o), ft, depth + 1, visited, ref t);
            }
    }
}
