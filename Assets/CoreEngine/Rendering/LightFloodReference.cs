// ==========================================
// Assets/CoreEngine/Rendering/LightFloodReference.cs
//
// Task 157 (TORCH_LIGHT_PLAN.md step L1, owner 2026-10-30 "a to all" on 155's
// questions: plan A, 12 m, 200 torches in 48 + 48 MB, RGB per-channel max).
// The CPU ORACLE for the torch LIGHT FLOOD. No GPU code uses it yet; L2 ports
// the sampling, L3 the incremental worker path.
//
// THE QUANTITY (what "exact" means here). Light lives in AIR voxels only. A
// voxel is OPEN iff its chunk is resident and its material is 0 (air); every
// other material, fluids included, blocks light, as today's torch rays do
// (Raymarch.compute TorchRayBlocked). A torch voxel is solid; it is the source.
// A MOVE goes from a voxel to one of its 26 neighbours and is VALID iff the
// target is open and every other voxel of the 2x2(x2) block the move crosses
// is open ("no corner cutting": an edge move needs both face voxels it cuts
// past, a corner move the 3 face + 3 edge voxels). So every valid path refines
// into a 6-connected air path: a wall that seals air 6-connected seals light,
// at any thickness, diagonal and edge-touching walls included.
// Each move costs its CHAMFER weight W_FACE / W_EDGE / W_CORNER = 4 / 6 / 7
// units. Per channel c, a torch t with seed level s_c lights voxel v at
//     level_c(v) = max over torches t of ( s_c(t) - d_t(v) ),   0 if <= 0
// d_t = the cheapest valid path from t. Combining by per-channel MAX is what
// makes removal exact (the Minecraft two-queue pattern below).
//
// WHY 4 / 6 / 7 AND 4.2913 UNITS PER VOXEL. A 3x3x3 chamfer with the exact
// weights 1 / 1.414 / 1.732 overestimates Euclid by up to 12.8% (directions
// between the move axes), which misses the plan's 8%. Centring the error band
// is the fix: 4 / 6 / 7 read at 4.2913 units per voxel stays within +-6.8%
// of Euclid in every direction (axis -6.8%, worst off-axis +6.8%). Among
// integer triples, 7 / 10 / 12 (+-6.4%) is marginally closer but tops out at
// 13.8 m in 10 bits; 4 / 6 / 7 reaches 23.8 m (Q2's 15 m fits).
//
// STORAGE. A voxel's light is ONE uint: r | g << 10 | b << 20 (10-bit levels,
// 2 spare flag bits) = the plan's 4 B per voxel. Sparse per brick: a lit chunk
// holds a 4,096-entry slot table (16 KB), a lit brick a 512-entry block. A
// brick is allocated iff it holds a non-zero voxel (canonical: incremental and
// from-scratch states compare byte for byte, allocations included). The
// GPU-side layout (8-bit log, 0.2 m cells, 0.4 m air bricks) is L2's choice;
// this oracle keeps the exact per-voxel field it is checked against.
//
// LEVEL -> LIGHT is a SHADING choice, not the oracle's: level / seed is the
// fraction of the reach left. SeedFor maps a torch colour to per-channel seeds
// on an exponential falloff with STOPS_AT_REACH stops over the reach (a dimmer
// channel reaches less far), the L1 default L2 may revisit.
//
// UPDATES (single writer: the caller's thread; the oracle reads ChunkStore
// and writes only its own storage).
//   AddTorch       one SSSP flood (FloodJob, Burst, Dial's buckets) from the
//                  torch over a brick-aligned box, max-merged into storage.
//                  Exact: the truth is a max over torches.
//   RemoveTorch    darken + refill: every voxel whose level in some channel
//                  EQUALS (a darkened neighbour's old level - move weight) is
//                  zeroed, recursively; then every lit voxel or torch next to a
//                  zeroed one re-propagates (label-correcting relaxation).
//   OnVoxelsChanged  after ChunkStore writes: a now-closed voxel is darkened,
//                  every lit voxel within 1 whose level came through a move
//                  that is now invalid is darkened, then the neighbourhood
//                  re-propagates (opened voxels and newly valid moves).
//   OnChunkEvicted / OnChunkAdmitted  the same, for a whole chunk leaving or
//                  joining the resident set (call AFTER the ChunkStore change).
// Torch voxels must be non-air while registered (AddTorch / OnVoxelsChanged
// throw otherwise). A torch in a non-resident chunk stays registered and dark.
// ==========================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using VoxelEngine.Memory;

public sealed unsafe class LightFloodReference : IDisposable
{
    // ---- the metric ----
    public const int W_FACE = 4, W_EDGE = 6, W_CORNER = 7;
    /// Centre of the 4/6/7 chamfer's error band: units per 0.1 m voxel of Euclid.
    public const double UNITS_PER_VOXEL = 4.29128784747792;
    public const int LEVEL_BITS = 10, LEVEL_MAX = (1 << LEVEL_BITS) - 1;
    public const uint LEVEL_MASK = (uint)LEVEL_MAX;
    public const float DEFAULT_REACH_M = 12f;          // owner Q2 (a)
    public const float STOPS_AT_REACH = 8f;            // L1 default (see header)

    public static int ReachLevel(float reachM) =>
        math.min(LEVEL_MAX, (int)math.round(reachM * 10.0 * UNITS_PER_VOXEL));

    /// Flood distance in metres that a level drop of `units` stands for.
    public static double UnitsToMetres(int units) => units / UNITS_PER_VOXEL * 0.1;

    public static uint Pack(int r, int g, int b) =>
        (uint)math.clamp(r, 0, LEVEL_MAX) | ((uint)math.clamp(g, 0, LEVEL_MAX) << 10) | ((uint)math.clamp(b, 0, LEVEL_MAX) << 20);

    public static int R(uint p) => (int)(p & LEVEL_MASK);
    public static int G(uint p) => (int)((p >> 10) & LEVEL_MASK);
    public static int B(uint p) => (int)((p >> 20) & LEVEL_MASK);
    public static int MaxChannel(uint p) => math.max(R(p), math.max(G(p), B(p)));

    /// Per-channel (a - w), clamped at 0.
    public static uint Sub(uint a, int w) =>
        Pack(math.max(0, R(a) - w), math.max(0, G(a) - w), math.max(0, B(a) - w));

    /// Per-channel max.
    public static uint Max(uint a, uint b) =>
        Pack(math.max(R(a), R(b)), math.max(G(a), G(b)), math.max(B(a), B(b)));

    /// True if some channel of `cur` is lit and equals that channel of `cand`
    /// (cur's light in that channel may have come from cand's voxel).
    public static bool AnyEqualLit(uint cur, uint cand) =>
        (R(cur) > 0 && R(cur) == R(cand)) || (G(cur) > 0 && G(cur) == G(cand)) || (B(cur) > 0 && B(cur) == B(cand));

    /// Seed levels for a torch colour: the brightest channel reaches `reachM`,
    /// a channel at fraction f of it starts log2(f) stops lower.
    public static uint SeedFor(float r, float g, float b, float reachM = DEFAULT_REACH_M, float stopsAtReach = STOPS_AT_REACH)
    {
        int top = ReachLevel(reachM);
        float m = math.max(r, math.max(g, b));
        if (m <= 0f) return 0;
        float perStop = top / stopsAtReach;
        int Ch(float c) => c <= 0f ? 0 : (int)math.round(top + perStop * math.log2(c / m));
        return Pack(Ch(r), Ch(g), Ch(b));
    }

    public static uint SeedForMaterial(byte material, float reachM = DEFAULT_REACH_M)
    {
        if (!MaterialRules.TryGetLight(material, out float r, out float g, out float b, out _))
            throw new ArgumentException($"material {material} is not a light");
        return SeedFor(r, g, b, reachM);
    }

    // ---- the 26 moves ----
    public static readonly int3[] MoveDelta = new int3[26];
    public static readonly int[] MoveWeight = new int[26];
    /// The voxels a move cuts past (besides its start and its target): 0, 2 or 6.
    public static readonly int3[][] MoveFootprint = new int3[26][];

    static LightFloodReference()
    {
        int k = 0;
        for (int dz = -1; dz <= 1; dz++)
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            int n = math.abs(dx) + math.abs(dy) + math.abs(dz);
            if (n == 0) continue;
            var d = new int3(dx, dy, dz);
            MoveDelta[k] = d;
            MoveWeight[k] = n == 1 ? W_FACE : n == 2 ? W_EDGE : W_CORNER;
            var fp = new List<int3>();
            // every voxel of the bbox(0, d) except 0 and d
            for (int z = math.min(0, dz); z <= math.max(0, dz); z++)
            for (int y = math.min(0, dy); y <= math.max(0, dy); y++)
            for (int x = math.min(0, dx); x <= math.max(0, dx); x++)
            {
                var c = new int3(x, y, z);
                if (c.Equals(int3.zero) || c.Equals(d)) continue;
                fp.Add(c);
            }
            MoveFootprint[k] = fp.ToArray();
            k++;
        }
    }

    // ---- state ----
    private readonly ChunkStore _store;
    private readonly Dictionary<int3, uint> _torches = new Dictionary<int3, uint>();
    private readonly Dictionary<int3, int[]> _chunkSlots = new Dictionary<int3, int[]>();
    private readonly Dictionary<int3, int> _chunkUsed = new Dictionary<int3, int>();
    private readonly List<uint[]> _bricks = new List<uint[]>();
    private readonly Stack<int> _free = new Stack<int>();

    private const int BRICKS_PER_CHUNK = 4096;

    public LightFloodReference(ChunkStore store) { _store = store; }

    public int TorchCount => _torches.Count;
    public IReadOnlyDictionary<int3, uint> Torches => _torches;
    public int UsedBrickSlots => _bricks.Count - _free.Count;
    public int AllocatedBrickSlots => _bricks.Count;
    public int ChunkTables => _chunkSlots.Count;

    // ---- timing of the last AddTorch (ms) ----
    public double LastGatherMs, LastFloodMs, LastMergeMs;
    public int LastReached;

    // =========================================================================
    // Reads
    // =========================================================================

    public bool Open(int3 v)
    {
        Chunk ch = _store.GetChunk(v >> 7);
        if (ch == null) return false;
        if (ch.isUniform) return ch.uniformMaterial == 0;
        uint h = ch.bricks[CoordMath.LocalBrickIndex((v >> 3) & 15)].data;
        if ((h & 0x80000000u) == 0) return (h & 0xFFu) == 0;
        return _store.BrickPool.RawData[(int)(h & 0x3FFFFFFFu) * 512 + CoordMath.LocalVoxelIndex(v & 7)] == 0;
    }

    private bool Resident(int3 v) => _store.GetChunk(v >> 7) != null;

    private bool ActiveTorch(int3 v, out uint seed) => _torches.TryGetValue(v, out seed) && Resident(v);

    private int3 _lastC = new int3(int.MinValue);
    private int[] _lastSlots;

    private int[] Slots(int3 chunk)
    {
        if (!chunk.Equals(_lastC))
        {
            _lastC = chunk;
            _chunkSlots.TryGetValue(chunk, out _lastSlots);
        }
        return _lastSlots;
    }

    public uint Get(int3 v)
    {
        int[] s = Slots(v >> 7);
        if (s == null) return 0;
        int slot = s[CoordMath.LocalBrickIndex((v >> 3) & 15)];
        return slot < 0 ? 0 : _bricks[slot][CoordMath.LocalVoxelIndex(v & 7)];
    }

    /// The light of `v`, or a torch's seed if `v` is an active torch.
    private uint Emit(int3 v) => ActiveTorch(v, out uint s) ? s : Get(v);

    private void Set(int3 v, uint value)
    {
        int3 c = v >> 7;
        int[] s = Slots(c);
        int bi = CoordMath.LocalBrickIndex((v >> 3) & 15);
        if (s == null)
        {
            if (value == 0) return;
            s = new int[BRICKS_PER_CHUNK];
            for (int i = 0; i < s.Length; i++) s[i] = -1;
            _chunkSlots[c] = s; _chunkUsed[c] = 0;
            _lastC = c; _lastSlots = s;
        }
        int slot = s[bi];
        if (slot < 0)
        {
            if (value == 0) return;
            slot = AllocBrick();
            s[bi] = slot;
            _chunkUsed[c] = _chunkUsed[c] + 1;
        }
        _bricks[slot][CoordMath.LocalVoxelIndex(v & 7)] = value;
        if (value == 0) _maybeEmpty.Add(new int4(c, bi));
    }

    private int AllocBrick()
    {
        if (_free.Count > 0) return _free.Pop();
        _bricks.Add(new uint[512]);
        return _bricks.Count - 1;
    }

    private readonly HashSet<int4> _maybeEmpty = new HashSet<int4>();

    /// Frees every brick (and chunk table) that an operation emptied.
    private void Compact()
    {
        foreach (int4 k in _maybeEmpty)
        {
            int3 c = k.xyz;
            if (!_chunkSlots.TryGetValue(c, out int[] s)) continue;
            int slot = s[k.w];
            if (slot < 0) continue;
            uint[] b = _bricks[slot];
            bool any = false;
            for (int i = 0; i < 512 && !any; i++) any = b[i] != 0;
            if (any) continue;
            s[k.w] = -1; _free.Push(slot);
            int used = _chunkUsed[c] - 1;
            if (used == 0) { _chunkSlots.Remove(c); _chunkUsed.Remove(c); }
            else _chunkUsed[c] = used;
        }
        _maybeEmpty.Clear();
        _lastC = new int3(int.MinValue); _lastSlots = null;
    }

    /// True if the move start -> start + MoveDelta[m] is valid now (the start
    /// is open or an active torch; the target and the footprint are open).
    public bool MoveValid(int3 start, int m)
    {
        int3 n = start + MoveDelta[m];
        if (!Open(n)) return false;
        int3[] fp = MoveFootprint[m];
        for (int i = 0; i < fp.Length; i++) if (!Open(start + fp[i])) return false;
        return Open(start) || ActiveTorch(start, out _);
    }

    // =========================================================================
    // Torches
    // =========================================================================

    public void AddTorch(int3 v, uint seed)
    {
        if (_torches.ContainsKey(v)) throw new InvalidOperationException($"torch {v} already registered");
        if (Resident(v) && Open(v)) throw new InvalidOperationException($"torch {v} sits in air: a torch voxel must be solid");
        _torches[v] = seed;
        if (!Resident(v)) return;
        FloodMerge(v, seed);
    }

    public bool RemoveTorch(int3 v)
    {
        if (!_torches.TryGetValue(v, out uint seed)) return false;
        _torches.Remove(v);
        if (!Resident(v)) return true;
        BeginOp();
        _darkQ.Enqueue((v, seed));
        _zeroedOld[v] = seed;
        _zeroed.Add(v);
        Darken();
        RefillAround(_zeroed);
        Propagate();
        Compact();
        return true;
    }

    // =========================================================================
    // Edits and streaming
    // =========================================================================

    public void OnVoxelChanged(int3 v) => OnVoxelsChanged(new[] { v });

    /// Call after ChunkStore writes to `changed` (any mix of opened and closed voxels).
    public void OnVoxelsChanged(IReadOnlyList<int3> changed)
    {
        BeginOp();
        for (int i = 0; i < changed.Count; i++)
        {
            int3 x = changed[i];
            if (_torches.ContainsKey(x) && Open(x))
                throw new InvalidOperationException($"torch {x} turned to air while registered: RemoveTorch first");
            if (!Open(x)) ZeroCell(x);
        }
        for (int i = 0; i < changed.Count; i++)
        {
            int3 x = changed[i];
            for (int m = 0; m < 26; m++) CloseCheck(x + MoveDelta[m]);
        }
        Darken();
        RefillAround(_zeroed);
        RefillAround(changed);
        for (int i = 0; i < changed.Count; i++) PushLit(changed[i]);
        Propagate();
        Compact();
    }

    /// Call AFTER ChunkStore.EvictChunk(chunk).
    public void OnChunkEvicted(int3 chunk)
    {
        BeginOp();
        if (_chunkSlots.TryGetValue(chunk, out int[] s))
        {
            int3 baseV = chunk << 7;
            for (int bi = 0; bi < BRICKS_PER_CHUNK; bi++)
            {
                if (s[bi] < 0) continue;
                uint[] b = _bricks[s[bi]];
                int3 bo = baseV + (new int3(bi & 15, (bi >> 4) & 15, bi >> 8) << 3);
                for (int i = 0; i < 512; i++)
                {
                    if (b[i] == 0) continue;
                    int3 v = bo + new int3(i & 7, (i >> 3) & 7, i >> 6);
                    _darkQ.Enqueue((v, b[i]));
                    if (OnChunkFace(v, chunk)) { _zeroed.Add(v); _zeroedOld[v] = b[i]; }   // only face voxels have outside neighbours
                    b[i] = 0;
                }
                _maybeEmpty.Add(new int4(chunk, bi));
            }
        }
        foreach (var kv in _torches)
            if ((kv.Key >> 7).Equals(chunk))
            {
                _darkQ.Enqueue((kv.Key, kv.Value));
                _zeroedOld[kv.Key] = kv.Value;
                if (OnChunkFace(kv.Key, chunk)) _zeroed.Add(kv.Key);
            }
        // moves between two outside voxels whose footprint lay in the chunk are invalid now
        foreach (int3 b in ShellOutside(chunk)) CloseCheck(b);
        Darken();
        RefillAround(_zeroed);
        Propagate();
        Compact();
    }

    /// Call AFTER ChunkStore.InsertChunk(chunk) and after its voxels are in place.
    public void OnChunkAdmitted(int3 chunk)
    {
        BeginOp();
        foreach (var kv in _torches)
            if ((kv.Key >> 7).Equals(chunk)) _propQ.Enqueue(kv.Key);
        foreach (int3 b in ShellOutside(chunk)) PushLit(b);
        Propagate();
        Compact();
    }

    private static bool OnChunkFace(int3 v, int3 chunk)
    {
        int3 l = v - (chunk << 7);
        return math.any(l == 0) || math.any(l == 127);
    }

    /// The 1-voxel shell just outside a chunk (faces, edges and corners).
    private static IEnumerable<int3> ShellOutside(int3 chunk)
    {
        int3 lo = (chunk << 7) - 1, hi = (chunk << 7) + 128;
        for (int z = lo.z; z <= hi.z; z++)
        for (int y = lo.y; y <= hi.y; y++)
        {
            bool zyEdge = z == lo.z || z == hi.z || y == lo.y || y == hi.y;
            if (zyEdge)
            {
                for (int x = lo.x; x <= hi.x; x++) yield return new int3(x, y, z);
            }
            else
            {
                yield return new int3(lo.x, y, z);
                yield return new int3(hi.x, y, z);
            }
        }
    }

    // =========================================================================
    // Darken / refill / propagate
    // =========================================================================

    private readonly Queue<(int3 v, uint old)> _darkQ = new Queue<(int3, uint)>();
    private readonly Dictionary<int3, uint> _zeroedOld = new Dictionary<int3, uint>();
    private readonly List<int3> _zeroed = new List<int3>();
    private readonly Queue<int3> _propQ = new Queue<int3>();
    /// Old levels are only needed by CloseCheck (before Darken runs); not recording them later saves memory.
    private bool _recordOld;

    private void BeginOp()
    {
        _recordOld = true;
        _darkQ.Clear(); _zeroedOld.Clear(); _zeroed.Clear(); _propQ.Clear();
        _lastC = new int3(int.MinValue); _lastSlots = null;
    }

    private void ZeroCell(int3 v)
    {
        uint cur = Get(v);
        if (cur == 0) return;
        if (_recordOld) _zeroedOld[v] = cur;
        _zeroed.Add(v);
        _darkQ.Enqueue((v, cur));
        Set(v, 0);
    }

    /// The level `a` had at the start of this operation (a torch: its seed).
    private uint OldOf(int3 a)
    {
        if (_zeroedOld.TryGetValue(a, out uint o)) return o;
        if (_torches.TryGetValue(a, out uint s)) return s;
        return Get(a);
    }

    /// Darkens `b` if its light may have come through a move that is invalid now.
    private void CloseCheck(int3 b)
    {
        uint cur = Get(b);
        if (cur == 0) return;
        for (int m = 0; m < 26; m++)
        {
            // the move a -> b, a = b - d
            int3 a = b - MoveDelta[m];
            uint oa = OldOf(a);
            if (oa == 0) continue;
            if (!AnyEqualLit(cur, Sub(oa, MoveWeight[m]))) continue;
            if (MoveValid(a, m)) continue;
            ZeroCell(b);
            return;
        }
    }

    private void Darken()
    {
        _recordOld = false;
        while (_darkQ.Count > 0)
        {
            var (u, old) = _darkQ.Dequeue();
            for (int m = 0; m < 26; m++)
            {
                uint cand = Sub(old, MoveWeight[m]);
                if (cand == 0) continue;
                int3 n = u + MoveDelta[m];
                uint cur = Get(n);
                if (cur == 0 || !AnyEqualLit(cur, cand)) continue;
                ZeroCell(n);
            }
        }
    }

    private void PushLit(int3 a)
    {
        if (Get(a) != 0 || ActiveTorch(a, out _)) _propQ.Enqueue(a);
    }

    private void RefillAround(IReadOnlyList<int3> cells)
    {
        for (int i = 0; i < cells.Count; i++)
        {
            int3 z = cells[i];
            for (int m = 0; m < 26; m++) PushLit(z + MoveDelta[m]);
        }
    }

    private void Propagate()
    {
        while (_propQ.Count > 0)
        {
            int3 u = _propQ.Dequeue();
            uint val = Emit(u);
            if (val == 0) continue;
            for (int m = 0; m < 26; m++)
            {
                uint cand = Sub(val, MoveWeight[m]);
                if (cand == 0) continue;
                int3 n = u + MoveDelta[m];
                uint cur = Get(n);
                uint nv = Max(cur, cand);
                if (nv == cur) continue;
                if (!MoveValid(u, m)) continue;
                Set(n, nv);
                _propQ.Enqueue(n);
            }
        }
    }

    // =========================================================================
    // From scratch, comparison, enumeration
    // =========================================================================

    /// A fresh oracle over the same store with the same torches (each one an SSSP flood).
    public static LightFloodReference FromScratch(ChunkStore store, IReadOnlyDictionary<int3, uint> torches)
    {
        var o = new LightFloodReference(store);
        var keys = new List<int3>(torches.Keys);
        keys.Sort((p, q) => p.x != q.x ? p.x.CompareTo(q.x) : p.y != q.y ? p.y.CompareTo(q.y) : p.z.CompareTo(q.z));
        foreach (int3 k in keys) o.AddTorch(k, torches[k]);
        return o;
    }

    /// Every allocated brick: (world brick coord, its 512 levels).
    public IEnumerable<(int3 brick, uint[] data)> LitBricks()
    {
        foreach (var kv in _chunkSlots)
            for (int bi = 0; bi < BRICKS_PER_CHUNK; bi++)
                if (kv.Value[bi] >= 0)
                    yield return ((kv.Key << 4) + new int3(bi & 15, (bi >> 4) & 15, bi >> 8), _bricks[kv.Value[bi]]);
    }

    /// Byte-for-byte: the same bricks allocated, the same levels. Returns null if equal.
    public string Diff(LightFloodReference other)
    {
        var mine = new Dictionary<int3, uint[]>();
        foreach (var (b, d) in LitBricks()) mine[b] = d;
        int n = 0;
        foreach (var (b, d) in other.LitBricks())
        {
            n++;
            if (!mine.TryGetValue(b, out uint[] m)) return $"brick {b} allocated only in the second";
            for (int i = 0; i < 512; i++)
                if (m[i] != d[i])
                {
                    int3 v = (b << 3) + new int3(i & 7, (i >> 3) & 7, i >> 6);
                    return $"voxel {v}: {Str(m[i])} vs {Str(d[i])}";
                }
        }
        if (n != mine.Count) return $"{mine.Count - n} bricks allocated only in the first";
        foreach (var (b, d) in LitBricks())
        {
            bool any = false;
            for (int i = 0; i < 512 && !any; i++) any = d[i] != 0;
            if (!any) return $"brick {b} allocated but all zero";
        }
        return null;
    }

    public static string Str(uint p) => $"({R(p)},{G(p)},{B(p)})";

    // =========================================================================
    // AddTorch's flood: gather -> FloodJob (Burst) -> merge
    // =========================================================================

    private NativeArray<byte> _open;
    private NativeArray<ushort> _dist;
    private NativeArray<byte> _touched;
    private NativeArray<int> _brickSrc;
    private int _cap, _capBricks;

    /// The brick-aligned box a torch with max channel `top` can light, plus a 1-voxel margin.
    public static void TorchBox(int3 t, int top, out int3 lo, out int3 dims)
    {
        int h = (top - 1) / W_FACE + 1;              // a reached voxel lies within Chebyshev (top-1)/4 of t
        lo = ((t - h) >> 3) << 3;
        int3 hiEx = (((t + h) >> 3) + 1) << 3;
        dims = hiEx - lo;
    }

    private void Ensure(int cells, int bricks)
    {
        if (cells > _cap)
        {
            if (_open.IsCreated) { _open.Dispose(); _dist.Dispose(); }
            _cap = cells;
            _open = new NativeArray<byte>(cells, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            _dist = new NativeArray<ushort>(cells, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        }
        if (bricks > _capBricks)
        {
            if (_touched.IsCreated) { _touched.Dispose(); _brickSrc.Dispose(); }
            _capBricks = bricks;
            _touched = new NativeArray<byte>(bricks, Allocator.Persistent);
            _brickSrc = new NativeArray<int>(bricks, Allocator.Persistent);
        }
    }

    /// Runs the single-torch flood into the scratch box. Returns the reached count.
    /// `dist` holds the path cost for reached voxels and 0xFFFF elsewhere.
    public int Flood(int3 t, int top, out int3 lo, out int3 dims)
    {
        var sw = Stopwatch.StartNew();
        TorchBox(t, top, out lo, out dims);
        int cells = dims.x * dims.y * dims.z;
        int3 db = dims >> 3;
        int nb = db.x * db.y * db.z;
        Ensure(cells, nb);

        // gather: per box brick, where its voxels come from (-1 closed, -2 open, >= 0 a pool brick)
        for (int bz = 0; bz < db.z; bz++)
        for (int by = 0; by < db.y; by++)
        for (int bx = 0; bx < db.x; bx++)
        {
            int3 wb = (lo >> 3) + new int3(bx, by, bz);
            Chunk ch = _store.GetChunk(wb >> 4);
            int src;
            if (ch == null) src = -1;
            else if (ch.isUniform) src = ch.uniformMaterial == 0 ? -2 : -1;
            else
            {
                uint h = ch.bricks[CoordMath.LocalBrickIndex(wb & 15)].data;
                src = (h & 0x80000000u) != 0 ? (int)(h & 0x3FFFFFFFu) : ((h & 0xFFu) == 0 ? -2 : -1);
            }
            _brickSrc[bx + db.x * (by + db.y * bz)] = src;
        }
        new GatherJob
        {
            brickSrc = _brickSrc, raw = _store.BrickPool.RawData, open = _open, dims = dims,
        }.Run();
        // other torches' voxels are solid by contract; mark them anyway (cheap, and robust)
        foreach (var kv in _torches)
        {
            int3 r = kv.Key - lo;
            if (math.all(r >= 0) && math.all(r < dims)) _open[r.x + dims.x * (r.y + dims.y * r.z)] = 0;
        }
        LastGatherMs = sw.Elapsed.TotalMilliseconds;

        sw.Restart();
        int3 rt = t - lo;
        var stats = new NativeArray<int>(1, Allocator.TempJob);
        var tables = MoveTables(dims);
        var job = new FloodJob
        {
            open = _open, dist = _dist, touched = _touched, dims = dims,
            source = rt.x + dims.x * (rt.y + dims.y * rt.z), limit = top,
            moveOff = tables.off, moveDelta = tables.delta, moveW = tables.w, fpOff = tables.fp, fpCount = tables.fpCount,
            stats = stats,
            b0 = new NativeList<int>(1024, Allocator.TempJob), b1 = new NativeList<int>(1024, Allocator.TempJob),
            b2 = new NativeList<int>(1024, Allocator.TempJob), b3 = new NativeList<int>(1024, Allocator.TempJob),
            b4 = new NativeList<int>(1024, Allocator.TempJob), b5 = new NativeList<int>(1024, Allocator.TempJob),
            b6 = new NativeList<int>(1024, Allocator.TempJob), b7 = new NativeList<int>(1024, Allocator.TempJob),
        };
        job.Run();
        int reached = stats[0];
        stats.Dispose(); tables.Dispose();
        job.b0.Dispose(); job.b1.Dispose(); job.b2.Dispose(); job.b3.Dispose();
        job.b4.Dispose(); job.b5.Dispose(); job.b6.Dispose(); job.b7.Dispose();
        LastFloodMs = sw.Elapsed.TotalMilliseconds;
        LastReached = reached;
        return reached;
    }

    /// Calls `visit(world brick coord)` for every box brick the last Flood reached.
    public void ForEachTouchedBrick(int3 lo, int3 dims, Action<int3> visit)
    {
        int3 db = dims >> 3;
        for (int bz = 0; bz < db.z; bz++)
        for (int by = 0; by < db.y; by++)
        for (int bx = 0; bx < db.x; bx++)
            if (_touched[bx + db.x * (by + db.y * bz)] != 0) visit((lo >> 3) + new int3(bx, by, bz));
    }

    /// The last Flood's path cost at world voxel `v` (0xFFFF: not reached).
    public int DistAt(int3 lo, int3 dims, int3 v)
    {
        int3 r = v - lo;
        if (math.any(r < 0) || math.any(r >= dims)) return 0xFFFF;
        return _dist[r.x + dims.x * (r.y + dims.y * r.z)];
    }

    private void FloodMerge(int3 t, uint seed)
    {
        int top = MaxChannel(seed);
        if (top == 0) return;
        Flood(t, top, out int3 lo, out int3 dims);
        var sw = Stopwatch.StartNew();
        _lastC = new int3(int.MinValue); _lastSlots = null;
        ushort* dist = (ushort*)_dist.GetUnsafeReadOnlyPtr();
        int3 db = dims >> 3;
        for (int bz = 0; bz < db.z; bz++)
        for (int by = 0; by < db.y; by++)
        for (int bx = 0; bx < db.x; bx++)
        {
            if (_touched[bx + db.x * (by + db.y * bz)] == 0) continue;
            int3 bo = new int3(bx, by, bz) << 3;
            for (int z = 0; z < 8; z++)
            for (int y = 0; y < 8; y++)
            {
                int row = bo.x + dims.x * (bo.y + y + dims.y * (bo.z + z));
                for (int x = 0; x < 8; x++)
                {
                    int d = dist[row + x];
                    if (d >= top || d == 0) continue;          // d 0 is the torch voxel itself: solid, unlit
                    uint cand = Sub(seed, d);
                    if (cand == 0) continue;
                    int3 v = lo + bo + new int3(x, y, z);
                    uint cur = Get(v);
                    uint nv = Max(cur, cand);
                    if (nv != cur) Set(v, nv);
                }
            }
        }
        LastMergeMs = sw.Elapsed.TotalMilliseconds;
    }

    private struct Tables : IDisposable
    {
        public NativeArray<int> off, w, fp, fpCount;
        public NativeArray<int3> delta;
        public void Dispose() { off.Dispose(); w.Dispose(); fp.Dispose(); fpCount.Dispose(); delta.Dispose(); }
    }

    private static Tables MoveTables(int3 dims)
    {
        var t = new Tables
        {
            off = new NativeArray<int>(26, Allocator.TempJob),
            w = new NativeArray<int>(26, Allocator.TempJob),
            fp = new NativeArray<int>(26 * 6, Allocator.TempJob),
            fpCount = new NativeArray<int>(26, Allocator.TempJob),
            delta = new NativeArray<int3>(26, Allocator.TempJob),
        };
        int Off(int3 d) => d.x + dims.x * (d.y + dims.y * d.z);
        for (int m = 0; m < 26; m++)
        {
            t.off[m] = Off(MoveDelta[m]);
            t.w[m] = MoveWeight[m];
            t.delta[m] = MoveDelta[m];
            t.fpCount[m] = MoveFootprint[m].Length;
            for (int i = 0; i < MoveFootprint[m].Length; i++) t.fp[m * 6 + i] = Off(MoveFootprint[m][i]);
        }
        return t;
    }

    public void Dispose()
    {
        if (_open.IsCreated) { _open.Dispose(); _dist.Dispose(); }
        if (_touched.IsCreated) { _touched.Dispose(); _brickSrc.Dispose(); }
        _cap = _capBricks = 0;
    }

    // =========================================================================
    // Jobs
    // =========================================================================

    [BurstCompile(CompileSynchronously = true, DisableSafetyChecks = true)]   // as in a player build
    private struct GatherJob : IJob
    {
        [ReadOnly] public NativeArray<int> brickSrc;
        [ReadOnly] public NativeArray<byte> raw;
        public NativeArray<byte> open;
        public int3 dims;

        public void Execute()
        {
            int3 db = dims >> 3;
            for (int bz = 0; bz < db.z; bz++)
            for (int by = 0; by < db.y; by++)
            for (int bx = 0; bx < db.x; bx++)
            {
                int src = brickSrc[bx + db.x * (by + db.y * bz)];
                for (int z = 0; z < 8; z++)
                for (int y = 0; y < 8; y++)
                {
                    int row = bx * 8 + dims.x * (by * 8 + y + dims.y * (bz * 8 + z));
                    for (int x = 0; x < 8; x++)
                    {
                        byte o;
                        if (src == -1) o = 0;
                        else if (src == -2) o = 1;
                        else o = raw[src * 512 + ((z << 6) | (y << 3) | x)] == 0 ? (byte)1 : (byte)0;
                        open[row + x] = o;
                    }
                }
            }
        }
    }

    /// Dial's algorithm: costs are < 1024 and every move costs 4..7, so 8
    /// rotating buckets hold the frontier and a voxel settles at its exact cost.
    [BurstCompile(CompileSynchronously = true, DisableSafetyChecks = true)]   // as in a player build
    private struct FloodJob : IJob
    {
        [ReadOnly] public NativeArray<byte> open;
        public NativeArray<ushort> dist;
        public NativeArray<byte> touched;
        public int3 dims;
        public int source, limit;
        [ReadOnly] public NativeArray<int> moveOff, moveW, fpOff, fpCount;
        [ReadOnly] public NativeArray<int3> moveDelta;
        public NativeArray<int> stats;
        public NativeList<int> b0, b1, b2, b3, b4, b5, b6, b7;

        private NativeList<int> Bucket(int i)
        {
            switch (i & 7)
            {
                case 0: return b0; case 1: return b1; case 2: return b2; case 3: return b3;
                case 4: return b4; case 5: return b5; case 6: return b6; default: return b7;
            }
        }

        public void Execute()
        {
            int cells = dims.x * dims.y * dims.z;
            for (int i = 0; i < cells; i++) dist[i] = 0xFFFF;
            int3 db = dims >> 3;
            int nb = db.x * db.y * db.z;
            for (int i = 0; i < nb; i++) touched[i] = 0;

            dist[source] = 0;
            b0.Add(source);
            int reached = 0, pending = 1;
            int sxy = dims.x * dims.y;
            for (int d = 0; d < limit && pending > 0; d++)
            {
                NativeList<int> cur = Bucket(d);
                for (int k = 0; k < cur.Length; k++)
                {
                    int c = cur[k];
                    pending--;
                    if (dist[c] != d) continue;
                    int z = c / sxy, rem = c - z * sxy, y = rem / dims.x, x = rem - y * dims.x;
                    if (c != source)
                    {
                        reached++;
                        touched[(x >> 3) + db.x * ((y >> 3) + db.y * (z >> 3))] = 1;
                    }
                    for (int m = 0; m < 26; m++)
                    {
                        int3 dd = moveDelta[m];
                        int nx = x + dd.x, ny = y + dd.y, nz = z + dd.z;
                        if (nx < 0 || ny < 0 || nz < 0 || nx >= dims.x || ny >= dims.y || nz >= dims.z) continue;
                        int n = c + moveOff[m];
                        if (open[n] == 0) continue;
                        int nd = d + moveW[m];
                        if (nd >= limit || nd >= dist[n]) continue;
                        bool ok = true;
                        int fc = fpCount[m];
                        for (int f = 0; f < fc; f++)
                            if (open[c + fpOff[m * 6 + f]] == 0) { ok = false; break; }
                        if (!ok) continue;
                        dist[n] = (ushort)nd;
                        Bucket(nd).Add(n);
                        pending++;
                    }
                }
                cur.Clear();
            }
            stats[0] = reached;
        }
    }
}
