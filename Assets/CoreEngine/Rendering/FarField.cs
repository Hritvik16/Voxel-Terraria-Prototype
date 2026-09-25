// Assets/CoreEngine/Rendering/FarField.cs
//
// STEP 1 PROTOTYPE of DESIGN_LARGE_WORLD_AND_TERRAIN_OVERHAUL.md §4.2 / §2.3:
// the FAR BAND. A 2.5D heightfield clipmap that draws terrain beyond the voxel
// window, so the view no longer ends at 290 m.
//
// WHAT IT IS
//   LEVELS nested, camera-centred, TOROIDAL rings of EDGE x EDGE texels. Level L
//   has a texel of (8 << L) voxels: 0.8, 1.6, 3.2, 6.4, 12.8 m. One uint per
//   texel: bits 0-15 top-face height in voxels (the y of the first AIR voxel
//   above the column), bits 16-23 surface material, bits 24-31 flags (bit 0 =
//   water surface). Each level carries a max-height mip chain (mips 1..5, the
//   coarsest cell 32 texels) so rays skip empty space instead of walking every
//   texel.
//
// WHERE THE DATA COMES FROM
//   Today's generator, unchanged: ColumnSampler.SampleColumn at each texel's
//   CENTRE column. Point-sampled, NOT band-limited -- the band-limited shape
//   field is Step 2's job. Burst jobs on the worker pool; the main thread only
//   schedules, uploads and flips origins.
//
// INVARIANTS (CLAUDE.md "Hard invariants")
//   - Ring size is a power of two and every address is `& EDGE_MASK`: a
//     non-power-of-two ring aliases silently (the §6.2 phantom-terrain bug).
//   - Level origins are aligned to ALIGN (= the coarsest mip cell), so no mip
//     cell ever straddles the ring seam and mixes stale texels into a max.
//   - A level's origin changes ONLY in the same frame its new texels reach the
//     GPU (full SetData, or the scatter the render pass dispatches before the
//     primary ray). The shader never sees an origin whose data is not there.
//   - The CPU remains the only terrain writer. This is a read-only view of the
//     generator; nothing here touches ChunkStore.
//
// NOT IN THIS PROTOTYPE (§4.2 production form): edits, persistence, the
// band-limited field, a CPU march oracle.
using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.WorldGen;

namespace VoxelEngine.Rendering
{
    public sealed class FarField : IDisposable
    {
        public const int LEVELS = 5;
        public const int EDGE = 1024;
        public const int EDGE_MASK = EDGE - 1;
        public const int LEVEL_TEXELS = EDGE * EDGE;
        public const int ALIGN = 32;                 // == coarsest mip cell (mip 5)
        public const int MIP_COUNT = 5;              // mips 1..5
        public const int MIP_TEXELS_PER_LEVEL = 349184; // 512^2 + 256^2 + 128^2 + 64^2 + 32^2
        public static readonly int[] MipOffset = { 0, 0, 262144, 327680, 344064, 348160 }; // by mip k (1..5)

        /// A level is used for points within this many texels (Chebyshev) of
        /// the camera. The ring reaches at least 480 texels in every direction
        /// (origin aligned down to 32); 40 more are slack for a strip that is
        /// still in flight while the camera keeps moving.
        public const int USE_RADIUS_TEXELS = 440;

        /// Strips are generated in ALIGN-wide bands; a jump wider than this
        /// refills the whole level instead.
        public const int STRIP_MAX = 128;

        public const uint FLAG_WATER = 1u;

        // ---- the engine-facing switch ----
        public static FarField Active { get; private set; }

        /// Render-side switch for same-process A/B frames (the capture rig flips
        /// it). The ring keeps streaming either way; only the march is gated.
        public static bool RenderEnabled = true;

        /// -farband 1 turns the band on (last match wins, the project's rule for
        /// runner overrides). Default OFF: the shipped frame is unchanged.
        public static bool RequestedByArgs() => ArgInt("-farband", 0) != 0;
        public static bool ShadowRequestedByArgs() => ArgInt("-farshadow", 0) != 0;

        /// -farsource tg2: fill the rings from the Step 2 generator (TerrainGen,
        /// sizeClass 2) instead of today's world, and draw FAR BAND ONLY (the
        /// voxel window still holds today's world, which would contradict it).
        /// A PREVIEW of the new island at scale, not a playable mode.
        public static bool Tg2Requested() => ArgValue("-farsource") == "tg2";
        public bool FarOnly { get; private set; }
        public int2 Tg2OffsetVoxels { get; private set; }
        public TerrainGen.WorldMap Tg2Map { get; private set; }
        public TerrainGen.TerrainSpec Tg2Spec { get; private set; }

        public GraphicsBuffer Texels { get; private set; }
        public GraphicsBuffer Mips { get; private set; }
        public GraphicsBuffer Staging { get; private set; }

        /// One bit per chunk COLUMN of the voxel window (row-major, window
        /// dims), set when its y=0 chunk is resident. The far march hands over
        /// at the first NON-resident column a ray crosses and only yields to
        /// the voxel band on resident ones -- the window is not the resident
        /// set (the hysteresis ring and loading holes are air to the voxel march).
        public GraphicsBuffer Resident { get; private set; }
        private uint[] _residentBits = new uint[1];

        /// True while any ring work is queued or in flight. Captures wait on it.
        public bool Busy
        {
            get
            {
                if (_readyStrips.Count > 0 || PendingScatters.Count > 0 || PendingMipMask != 0) return true;
                for (int L = 0; L < LEVELS; L++) if (_pending[L].active) return true;
                return false;
            }
        }

        /// True once every level holds a complete ring for its origin.
        public bool Ready { get; private set; }
        public bool ShadowsOn = ShadowRequestedByArgs();
        public float HazeRangeMetres = ArgFloat("-farhaze", 4000f);
        /// Iteration caps for the far march (primary, and the far shadow ray). A
        /// ray that runs out draws sky; the step-heat view shows where.
        public int PrimaryStepCap = ArgInt("-farsteps", 384);
        public int ShadowStepCap = ArgInt("-farshadowsteps", 192);

        /// Global max top height over everything ever loaded (voxels). Rays
        /// above it and climbing cannot hit anything.
        public int MaxHeightVoxels { get; private set; }

        // ---- stats for the rigs ----
        public long ColumnsSampled { get; private set; }
        public long BytesUploaded { get; private set; }
        public int StripsApplied { get; private set; }
        public int FullRefills { get; private set; }
        public double InitialFillMs { get; private set; }
        public double LastStripJobMs { get; private set; }

        public struct Scatter { public int level, x0, z0, w, h; }
        /// GPU work the render pass must do before the primary dispatch this
        /// frame. Filled by Update, drained by RaymarchFeature.
        public readonly List<Scatter> PendingScatters = new List<Scatter>();
        public int PendingMipMask;   // bit L = rebuild level L's mips

        private readonly int2[] _origin = new int2[LEVELS];
        private NativeArray<float> _tgH, _tgR;
        private NativeArray<byte> _genToEngine;
        private TerrainGen.Offsets _tgO;
        private ColumnSampler.State _st;
        private NativeArray<byte> _surfaceByBiome;

        private struct Pending
        {
            public bool active, full;
            public JobHandle handle;
            public NativeArray<uint> result;
            public NativeArray<int> maxH;
            public int2 newOrigin;       // origin to adopt when applied
            public int x0, z0, w, h;     // region (texel coords) for a strip
            public double startedAt;
        }
        private readonly Pending[] _pending = new Pending[LEVELS];
        private readonly Queue<int> _readyStrips = new Queue<int>();

        public FarField(WorldMetaData meta, Vector3 cameraMetres)
        {
            _st = ColumnSampler.CreateState(meta);
            if (Tg2Requested())
            {
                var swm = System.Diagnostics.Stopwatch.StartNew();
                FarOnly = true;
                Tg2Spec = TerrainGen.TerrainSpec.SizeClass2(meta.seed);
                Tg2Map = TerrainGen.WorldMap.Build(Tg2Spec);
                _tgO = TerrainGen.Offsets.For(meta.seed);
                _tgH = new NativeArray<float>(Tg2Map.Height, Allocator.Persistent);
                _tgR = new NativeArray<float>(Tg2Map.Rough, Allocator.Persistent);
                // Put today's camera over the new island's centre, texel-aligned.
                int half = Tg2Spec.worldSizeVoxels / 2;
                Tg2OffsetVoxels = new int2(((half - (int)(cameraMetres.x * 10f)) >> 7) << 7,
                                           ((half - (int)(cameraMetres.z * 10f)) >> 7) << 7);
                _genToEngine = new NativeArray<byte>(256, Allocator.Persistent);
                for (int i = 0; i < 256; i++) _genToEngine[i] = GenToEngine((byte)i);
                Debug.Log($"[FarField] tg2 preview: world map built in {swm.ElapsedMilliseconds} ms, offset {Tg2OffsetVoxels}");
            }
            _surfaceByBiome = new NativeArray<byte>(256, Allocator.Persistent);
            for (int i = 0; i < 256; i++) _surfaceByBiome[i] = Materials.Stone;
            foreach (var b in Biomes.Table) _surfaceByBiome[b.id] = b.surfaceMaterial;

            Texels = new GraphicsBuffer(GraphicsBuffer.Target.Structured, LEVELS * LEVEL_TEXELS, sizeof(uint));
            Mips = new GraphicsBuffer(GraphicsBuffer.Target.Structured, LEVELS * MIP_TEXELS_PER_LEVEL, sizeof(uint));
            Staging = new GraphicsBuffer(GraphicsBuffer.Target.Structured, STRIP_MAX * EDGE, sizeof(uint));
            Resident = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 64, sizeof(uint));

            // Initial fill: every level, in parallel, blocking. At startup only.
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int L = 0; L < LEVELS; L++) ScheduleFull(L, DesiredOrigin(L, cameraMetres));
            for (int L = 0; L < LEVELS; L++) ApplyFull(L);
            InitialFillMs = sw.Elapsed.TotalMilliseconds;
            Ready = true;
            Active = this;
            Debug.Log($"[FarField] initial fill {LEVELS} x {EDGE}^2 texels in {InitialFillMs:F0} ms, " +
                      $"max height {MaxHeightVoxels} voxels, GPU {GpuBytes / (1024f * 1024f):F1} MB");
        }

        public long GpuBytes => (long)LEVELS * (LEVEL_TEXELS + MIP_TEXELS_PER_LEVEL) * 4 + (long)STRIP_MAX * EDGE * 4;

        public int2 Origin(int level) => _origin[level];
        public static int TexelVoxels(int level) => 8 << level;

        /// Ring min corner (texel coords) that centres the camera, aligned down.
        public static int2 DesiredOrigin(int level, Vector3 camMetres)
        {
            int tv = TexelVoxels(level);
            int cx = (int)math.floor(camMetres.x * 10f / tv);
            int cz = (int)math.floor(camMetres.z * 10f / tv);
            // Arithmetic shift == floor division for negatives too; bitwise per
            // the coordinate rule. ALIGN is a power of two.
            return new int2(((cx - EDGE / 2) >> 5) << 5, ((cz - EDGE / 2) >> 5) << 5);
        }

        /// Rebuilds the residency bitmap from the store (1024 lookups for a
        /// 32x32 window; uploaded every frame, 128 bytes).
        public void UpdateResidency(ChunkStore store, int3 windowOriginChunks, int3 windowDimsChunks)
        {
            int n = windowDimsChunks.x * windowDimsChunks.z;
            int words = (n + 31) >> 5;
            if (_residentBits.Length != words) _residentBits = new uint[words];
            Array.Clear(_residentBits, 0, words);
            for (int rz = 0; rz < windowDimsChunks.z; rz++)
            for (int rx = 0; rx < windowDimsChunks.x; rx++)
            {
                var c = new int3(windowOriginChunks.x + rx, 0, windowOriginChunks.z + rz);
                if (!store.IsResident(c)) continue;
                int i = rz * windowDimsChunks.x + rx;
                _residentBits[i >> 5] |= 1u << (i & 31);
            }
            if (Resident.count < words)
            {
                Resident.Release();
                Resident = new GraphicsBuffer(GraphicsBuffer.Target.Structured, words, sizeof(uint));
            }
            Resident.SetData(_residentBits, 0, 0, words);
        }

        /// Per-frame: finish jobs, issue at most ONE strip scatter, schedule new work.
        public void Update(Vector3 camMetres)
        {
            // 1. Harvest finished jobs.
            for (int L = 0; L < LEVELS; L++)
            {
                ref Pending p = ref _pending[L];
                if (!p.active || !p.handle.IsCompleted) continue;
                p.handle.Complete();
                LastStripJobMs = (Time.realtimeSinceStartupAsDouble - p.startedAt) * 1000.0;
                if (p.full) ApplyFull(L);
                else if (!_readyStrips.Contains(L)) _readyStrips.Enqueue(L);
            }

            // 2. One strip per frame reaches the GPU (bounded upload).
            // The staging buffer holds ONE strip: never overwrite it before the
            // render pass has consumed the previous scatter.
            if (_readyStrips.Count > 0 && PendingScatters.Count == 0)
            {
                int L = _readyStrips.Dequeue();
                ref Pending p = ref _pending[L];
                Staging.SetData(p.result, 0, 0, p.w * p.h);
                BytesUploaded += (long)p.w * p.h * 4;
                PendingScatters.Add(new Scatter { level = L, x0 = p.x0, z0 = p.z0, w = p.w, h = p.h });
                PendingMipMask |= 1 << L;
                _origin[L] = p.newOrigin;
                MaxHeightVoxels = math.max(MaxHeightVoxels, p.maxH[0]);
                StripsApplied++;
                p.result.Dispose(); p.maxH.Dispose();
                p.active = false;
            }

            // 3. Schedule: each idle level moves one step toward its desired origin.
            for (int L = 0; L < LEVELS; L++)
            {
                if (_pending[L].active || _readyStrips.Contains(L)) continue;
                int2 want = DesiredOrigin(L, camMetres);
                int2 have = _origin[L];
                if (math.all(want == have)) continue;
                int2 d = want - have;
                if (math.abs(d.x) > STRIP_MAX || math.abs(d.y) > STRIP_MAX) { ScheduleFull(L, want); continue; }
                // X first, then Z; each strip is the band of columns (or rows)
                // that enters the ring, over the ring's current other extent.
                if (d.x != 0)
                {
                    int w = math.abs(d.x);
                    int x0 = d.x > 0 ? have.x + EDGE : want.x;
                    ScheduleStrip(L, new int2(want.x, have.y), x0, have.y, w, EDGE);
                }
                else
                {
                    int h = math.abs(d.y);
                    int z0 = d.y > 0 ? have.y + EDGE : want.y;
                    ScheduleStrip(L, new int2(have.x, want.y), have.x, z0, EDGE, h);
                }
            }
        }

        private void ScheduleStrip(int L, int2 newOrigin, int x0, int z0, int w, int h)
        {
            ref Pending p = ref _pending[L];
            p.active = true; p.full = false; p.newOrigin = newOrigin;
            p.x0 = x0; p.z0 = z0; p.w = w; p.h = h;
            p.result = new NativeArray<uint>(w * h, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            p.maxH = new NativeArray<int>(1, Allocator.Persistent);
            p.startedAt = Time.realtimeSinceStartupAsDouble;
            var h1 = ScheduleFill(TexelVoxels(L), x0, z0, w, false, int2.zero, p.result, w * h, 1024);
            p.handle = new MaxJob { values = p.result, maxH = p.maxH }.Schedule(h1);
            ColumnsSampled += (long)w * h;
        }

        private void ScheduleFull(int L, int2 origin)
        {
            ref Pending p = ref _pending[L];
            if (p.active) { p.handle.Complete(); p.result.Dispose(); p.maxH.Dispose(); }
            p.active = true; p.full = true; p.newOrigin = origin;
            p.result = new NativeArray<uint>(LEVEL_TEXELS, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            p.maxH = new NativeArray<int>(1, Allocator.Persistent);
            p.startedAt = Time.realtimeSinceStartupAsDouble;
            var h1 = ScheduleFill(TexelVoxels(L), origin.x, origin.y, EDGE, true, origin, p.result, LEVEL_TEXELS, 4096);
            p.handle = new MaxJob { values = p.result, maxH = p.maxH }.Schedule(h1);
            ColumnsSampled += LEVEL_TEXELS;
        }

        private void ApplyFull(int L)
        {
            ref Pending p = ref _pending[L];
            p.handle.Complete();
            Texels.SetData(p.result, 0, L * LEVEL_TEXELS, LEVEL_TEXELS);
            BytesUploaded += LEVEL_TEXELS * 4L;
            _origin[L] = p.newOrigin;
            MaxHeightVoxels = math.max(MaxHeightVoxels, p.maxH[0]);
            PendingMipMask |= 1 << L;
            if (Ready) FullRefills++;
            p.result.Dispose(); p.maxH.Dispose();
            p.active = false;
        }

        private JobHandle ScheduleFill(int texelVoxels, int x0, int z0, int w, bool ringOrder, int2 ringOrigin,
                                       NativeArray<uint> output, int count, int batch)
        {
            if (FarOnly)
                return new FillTg2Job
                {
                    h = _tgH, r = _tgR, spec = Tg2Spec, o = _tgO, genToEngine = _genToEngine, offset = Tg2OffsetVoxels,
                    texelVoxels = texelVoxels, x0 = x0, z0 = z0, w = w, ringOrder = ringOrder, ringOrigin = ringOrigin, output = output,
                }.Schedule(count, batch);
            return new FillRegionJob
            {
                st = _st, surfaceByBiome = _surfaceByBiome, texelVoxels = texelVoxels,
                x0 = x0, z0 = z0, w = w, ringOrder = ringOrder, ringOrigin = ringOrigin, output = output,
            }.Schedule(count, batch);
        }

        /// GenMat -> today's material ids, for the PREVIEW only (the real
        /// mapping lands with the new materials, §2.6).
        private static byte GenToEngine(byte g)
        {
            switch (g)
            {
                case TerrainGen.GenMat.Water: return Materials.Water;
                case TerrainGen.GenMat.Grass: return Materials.Grass;
                case TerrainGen.GenMat.JungleGrass: return Materials.JungleGrass;
                case TerrainGen.GenMat.Sand:
                case TerrainGen.GenMat.BeachSand: return Materials.Sand;
                case TerrainGen.GenMat.Snow:
                case TerrainGen.GenMat.PackedIce: return Materials.Snow;
                case TerrainGen.GenMat.PackedSand: return Materials.Sandstone;
            }
            if (g >= TerrainGen.GenMat.CreamSandstone && g <= TerrainGen.GenMat.PinkSiltstone) return Materials.Sandstone;
            if (g >= TerrainGen.GenMat.KarstLimestone && g <= TerrainGen.GenMat.JadeSerpentine) return Materials.MossyStone;
            return Materials.Stone;
        }

        public void Dispose()
        {
            for (int L = 0; L < LEVELS; L++)
            {
                ref Pending p = ref _pending[L];
                if (!p.active) continue;
                p.handle.Complete(); p.result.Dispose(); p.maxH.Dispose(); p.active = false;
            }
            Texels?.Release(); Mips?.Release(); Staging?.Release(); Resident?.Release(); Resident = null;
            Texels = null; Mips = null; Staging = null;
            _st.Dispose();
            if (_surfaceByBiome.IsCreated) _surfaceByBiome.Dispose();
            if (_tgH.IsCreated) _tgH.Dispose();
            if (_tgR.IsCreated) _tgR.Dispose();
            if (_genToEngine.IsCreated) _genToEngine.Dispose();
            Tg2Map?.Dispose();
            if (Active == this) Active = null;
        }

        // ------------------------------------------------------------------
        // Packing. Top face = first air voxel above the column. Below sea the
        // visible surface is WATER at sea level (§5.5's static water).
        // ------------------------------------------------------------------
        public static uint Pack(int columnHeight, byte biome, NativeArray<byte> surfaceByBiome)
        {
            const int SEA = WorldGenConstants.SEA_LEVEL_VOXEL_Y;
            if (columnHeight < SEA)
                return (uint)(SEA + 1) | ((uint)Materials.Water << 16) | (FLAG_WATER << 24);
            return (uint)(columnHeight + 1) | ((uint)surfaceByBiome[biome] << 16);
        }

        [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High)]
        private struct FillRegionJob : IJobParallelFor
        {
            [ReadOnly] public ColumnSampler.State st;
            [ReadOnly] public NativeArray<byte> surfaceByBiome;
            public int texelVoxels, x0, z0, w;
            public bool ringOrder;       // true: index i IS ring slot i (full level)
            public int2 ringOrigin;
            [WriteOnly] public NativeArray<uint> output;

            public void Execute(int i)
            {
                int tx, tz;
                if (ringOrder)
                {
                    int sx = i & EDGE_MASK, sz = i >> 10;
                    tx = ringOrigin.x + ((sx - (ringOrigin.x & EDGE_MASK)) & EDGE_MASK);
                    tz = ringOrigin.y + ((sz - (ringOrigin.y & EDGE_MASK)) & EDGE_MASK);
                }
                else { tx = x0 + i % w; tz = z0 + i / w; }
                int half = texelVoxels >> 1;
                ColumnSampler.SampleColumn(in st, tx * texelVoxels + half, tz * texelVoxels + half,
                    out int h, out byte b);
                output[i] = Pack(h, b, surfaceByBiome);
            }
        }

        [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High)]
        private struct FillTg2Job : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float> h, r;
            [ReadOnly] public NativeArray<byte> genToEngine;
            public TerrainGen.TerrainSpec spec;
            public TerrainGen.Offsets o;
            public int2 offset;
            public int texelVoxels, x0, z0, w;
            public bool ringOrder;
            public int2 ringOrigin;
            [WriteOnly] public NativeArray<uint> output;

            public unsafe void Execute(int i)
            {
                int tx, tz;
                if (ringOrder)
                {
                    int sx = i & EDGE_MASK, sz = i >> 10;
                    tx = ringOrigin.x + ((sx - (ringOrigin.x & EDGE_MASK)) & EDGE_MASK);
                    tz = ringOrigin.y + ((sz - (ringOrigin.y & EDGE_MASK)) & EDGE_MASK);
                }
                else { tx = x0 + i % w; tz = z0 + i / w; }
                var map = new TerrainGen.MapView
                {
                    height = (float*)Unity.Collections.LowLevel.Unsafe.NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(h),
                    rough = (float*)Unity.Collections.LowLevel.Unsafe.NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(r),
                    cells = spec.mapCells, cellVoxels = spec.mapCellVoxels,
                };
                int half = texelVoxels >> 1;
                float gx = tx * texelVoxels + half + offset.x, gz = tz * texelVoxels + half + offset.y;
                var c = TerrainGen.ShapeField.Sample(in spec, in map, in o, gx, gz, texelVoxels);
                float step = TerrainGen.ShapeField.MaxStep(in spec, in map, in o, gx, gz, texelVoxels, c.height) ;
                // MaxStep is per voxel at this spacing; the surface rules want the
                // per-voxel step, so it is directly comparable.
                byte gm = TerrainGen.DetailField.TopMaterial(in spec, in c, step, in o, (int)gx, (int)gz);
                const int SEA = WorldGenConstants.SEA_LEVEL_VOXEL_Y;
                int top = c.height + 1 - (spec.seaLevelY - SEA);
                if (c.height < spec.seaLevelY)
                    output[i] = (uint)(SEA + 1) | ((uint)Materials.Water << 16) | (FLAG_WATER << 24);
                else
                    output[i] = (uint)math.clamp(top, 0, 65535) | ((uint)genToEngine[gm] << 16);
            }
        }

        [BurstCompile]
        private struct MaxJob : IJob
        {
            [ReadOnly] public NativeArray<uint> values;
            public NativeArray<int> maxH;
            public void Execute()
            {
                int m = 0;
                for (int i = 0; i < values.Length; i++) m = math.max(m, (int)(values[i] & 0xFFFFu));
                maxH[0] = m;
            }
        }

        // ---- args (last match wins -- PlaygroundCapture's documented rule) ----
        private static int ArgInt(string name, int def)
        {
            string v = ArgValue(name);
            return v != null && int.TryParse(v, out int r) ? r : def;
        }
        private static float ArgFloat(string name, float def)
        {
            string v = ArgValue(name);
            return v != null && float.TryParse(v, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float r) ? r : def;
        }
        private static string ArgValue(string name)
        {
            string[] a = Environment.GetCommandLineArgs();
            string found = null;
            for (int i = 0; i + 1 < a.Length; i++) if (a[i] == name) found = a[i + 1];
            return found;
        }
    }
}
