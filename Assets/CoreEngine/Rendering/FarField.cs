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
// EDITS (task 82, engine step 13b; Amendment 8.14 row 9, the plan's E5): the
// save's D.4 edit ops (EditOpLog) are drawn too. Every fill job (initial fill,
// strip, full refill) applies the ops that reach its region to each texel's
// column, in seq order (ApplyOp below), so a texel is ALWAYS "generator + ops".
// An op recorded later PATCHES the texels it touches: a fill job over the op's
// footprint (clipped to the ring) goes up through the same one-strip staging
// buffer and FarScatter dispatch the strips use; a strip or refill computed
// before the op and uploaded after it gets a patch queued behind it. Jobs
// write only the result arrays they own; the main thread uploads them (the
// CPU stays the only writer of what the GPU holds).
//   What 2.5D CAN show: a crater (the top drops to the op's lowest carved
//   voxel, coloured with the generator's material there), a mound or a fill
//   that sits on the ground (the top rises to the op's highest voxel), a
//   carve under water (the water stays at its surface and gets deeper), a
//   mound that rises out of the water (dry land).
//   What it CANNOT: an overhang, a tunnel or a cave (a carve that does not
//   reach the column's top changes nothing), a floating blob (a fill that does
//   not touch the top), water draining into a dry crater; and a column under
//   water deeper than its depth field holds (12.7 m) or under lava is left as
//   it is (its ground is not known).
// The texel is sampled at its CENTRE column, so a texel shows the op only if
// the op covers that column -- an op narrower than the texel may not show.
//
// NOT IN THIS PROTOTYPE (§4.2 production form): persistence of its own, the
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

        /// Task 75: a WATER texel also carries its depth -- water voxels above the
        /// ground, clamped to 127 (12.7 m) -- in bits 25-31, so the shader can make
        /// far water as see-through as near water without a march. Read only when
        /// see-through liquids are on; the spare flag bits were zero before.
        public static uint DepthBits(int depthVoxels) => (uint)math.clamp(depthVoxels, 0, 127) << 25;

        // ---- the engine-facing switch ----
        public static FarField Active { get; private set; }

        /// Render-side switch for same-process A/B frames (the capture rig flips
        /// it). The ring keeps streaming either way; only the march is gated.
        public static bool RenderEnabled = true;

        /// Level selection by pixel footprint as well as horizontal distance
        /// (Raymarch.compute FarMarch). -farfootprint 0 is the pre-2026-09-25
        /// A/B arm. Runtime-switchable for same-process shimmer comparisons.
        public static bool FootprintLevels = ArgInt("-farfootprint", 1) != 0;

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
        /// V2: the rings are filled from TerrainGen -- either the -farsource tg2
        /// PREVIEW (FarOnly, offset + sea shift into today's world frame) or a
        /// real sizeClass 2 WORLD (aligned: offset 0, absolute Y, voxel band on).
        public bool UsesTg2 => Tg2Map != null;
        private bool _ownsTg2Map;
        public bool Tg2World { get; private set; }
        public int2 Tg2OffsetVoxels { get; private set; }
        /// Y the rings subtract from TerrainGen heights: the preview maps the
        /// new sea onto today's (SEA_LEVEL_VOXEL_Y); a real world keeps 0.
        public int Tg2SeaShift { get; private set; }
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

        /// What the LAST Update did, per phase (main-thread ms) and per GPU
        /// work item (counts: the GPU cost is not timed in this workflow).
        /// Read by -flyfast the frame after, aligned to the frame it describes.
        public struct FrameStats
        {
            public float harvestMs, applyFullMs, stagingMs, scheduleMs, residencyMs;
            public int strips, fulls, mipLevels;
        }
        public FrameStats LastFrame;

        /// DIAGNOSTIC ISOLATION ARM, not a setting: -farmipstress N rebuilds the
        /// whole mip chain of levels 0..N-1 EVERY frame, on top of the real
        /// upkeep. The frame-time delta against the same arm without it, divided
        /// by N, bounds one whole-level rebuild's GPU cost. Default 0 = off.
        public static int MipStressLevels = ArgInt("-farmipstress", 0);

        public struct Scatter { public int level, x0, z0, w, h; }
        /// GPU work the render pass must do before the primary dispatch this
        /// frame. Filled by Update, drained by RaymarchFeature.
        public readonly List<Scatter> PendingScatters = new List<Scatter>();
        public int PendingMipMask;   // bit L = rebuild level L's mips

        private readonly int2[] _origin = new int2[LEVELS];
        private NativeArray<float> _tgH, _tgR, _tgW;
        private NativeArray<byte> _tgCls;   // map water class: ShapeField's lake rims (v4)
        private NativeArray<ushort> _tgFlow; // v4 river flow bits (task 76; length 1 = none)
        /// Task 76 (R2/R5): the map's river flow bits on the GPU, two cells per
        /// uint (Raymarch.compute RiverFlow; 2 MB at 1024^2 cells). Null = none.
        public GraphicsBuffer RiverFlow { get; private set; }
        /// xy: gen voxel = engine voxel + xy; z: voxels per map cell; w: cells.
        public Vector4 RiverMapParams { get; private set; }
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
            public NativeArray<VoxelEngine.Streaming.EditOp> ops;   // the ops the job applies (owned by the job)
            public int opsUsed;                                     // the log's count when it was scheduled
            public int2 newOrigin;       // origin to adopt when applied
            public int x0, z0, w, h;     // region (texel coords) for a strip
            public double startedAt;
        }
        private readonly Pending[] _pending = new Pending[LEVELS];
        private readonly Queue<int> _readyStrips = new Queue<int>();

        // ---- edit ops (task 82, header EDITS) ----
        private VoxelEngine.Streaming.EditOpLog _ops;
        private int _opsSeen;
        /// One op patch: the texels of an op's footprint at one level, recomputed.
        private struct Patch
        {
            public int level, x0, z0, w, h;
            public JobHandle handle;
            public NativeArray<uint> result;
            public NativeArray<VoxelEngine.Streaming.EditOp> ops;
        }
        private readonly List<Patch> _patches = new List<Patch>();   // FIFO: issued in the order scheduled
        private bool _patchTurn;
        public int PatchesPending => _patches.Count;
        public long PatchesApplied { get; private set; }
        public long PatchTexels { get; private set; }

        /// The save's edit-op log (task 82). The constructor's initial fill already
        /// draws the ops it holds; set later, every op in it is patched in.
        public VoxelEngine.Streaming.EditOpLog Ops
        {
            get => _ops;
            set { _ops = value; _opsSeen = 0; }
        }

        /// Test hook (EditMode cannot read GPU buffers back): a CPU copy of every
        /// texel write (ApplyFull, and each scatter as FarScatter does it). Set
        /// before construction; Shadow is level-major ring slots.
        public static bool ShadowTexels;
        public uint[] Shadow;

        /// sharedTg2Map: a sizeClass 2 world's map, already built by the chunk
        /// source (TerrainGenChunkSource.Map) -- borrowed, not owned, so the
        /// ~25 s build happens once. Null: build (and own) one as before.
        public FarField(WorldMetaData meta, Vector3 cameraMetres, TerrainGen.WorldMap sharedTg2Map = null,
                        VoxelEngine.Streaming.EditOpLog ops = null)
        {
            _ops = ops;
            _opsSeen = ops != null ? ops.Count : 0;   // the initial fill below applies them
            if (ShadowTexels) Shadow = new uint[LEVELS * LEVEL_TEXELS];
            _st = ColumnSampler.CreateState(meta);
            Tg2World = meta.sizeClass == VoxelEngine.WorldGen.TerrainGenChunkSource.SIZE_CLASS;
            if (Tg2Requested() || Tg2World)
            {
                var swm = System.Diagnostics.Stopwatch.StartNew();
                FarOnly = !Tg2World;
                // A world's map carries its spec (with the chunk source's brick
                // geology), so the dev map's slice reads what the chunks hold.
                Tg2Spec = sharedTg2Map != null ? sharedTg2Map.Spec : TerrainGen.TerrainSpec.SizeClass2(meta.seed, VoxelEngine.WorldGen.TerrainGenChunkSource.MapVersionArg());
                _ownsTg2Map = sharedTg2Map == null;
                Tg2Map = sharedTg2Map ?? TerrainGen.WorldMap.Build(Tg2Spec);
                _tgO = TerrainGen.Offsets.For(Tg2Spec);
                _tgH = new NativeArray<float>(Tg2Map.Height, Allocator.Persistent);
                _tgR = new NativeArray<float>(Tg2Map.Rough, Allocator.Persistent);
                _tgW = new NativeArray<float>(Tg2Map.Water, Allocator.Persistent);
                _tgCls = new NativeArray<byte>(Tg2Map.WaterClass, Allocator.Persistent);
                _tgFlow = new NativeArray<ushort>(Tg2Map.FlowDir ?? new ushort[1], Allocator.Persistent);
                if (Tg2Map.FlowDir != null)
                {
                    var fd = Tg2Map.FlowDir;
                    var packed = new uint[(fd.Length + 1) >> 1];
                    for (int i = 0; i < fd.Length; i++) packed[i >> 1] |= (uint)fd[i] << ((i & 1) << 4);
                    RiverFlow = new GraphicsBuffer(GraphicsBuffer.Target.Structured, packed.Length, sizeof(uint));
                    RiverFlow.SetData(packed);
                }
                // PREVIEW: put today's camera over the new island's centre,
                // texel-aligned, and map its sea onto today's. A real world is
                // already in the island's own frame.
                int half = Tg2Spec.worldSizeVoxels / 2;
                Tg2OffsetVoxels = Tg2World ? int2.zero
                    : new int2(((half - (int)(cameraMetres.x * 10f)) >> 7) << 7,
                               ((half - (int)(cameraMetres.z * 10f)) >> 7) << 7);
                Tg2SeaShift = Tg2World ? 0 : Tg2Spec.seaLevelY - WorldGenConstants.SEA_LEVEL_VOXEL_Y;
                // Only an aligned tg2 world: a preview's near voxels are another world's.
                if (Tg2World && RiverFlow != null)
                    RiverMapParams = new Vector4(Tg2OffsetVoxels.x, Tg2OffsetVoxels.y, Tg2Spec.mapCellVoxels, Tg2Spec.mapCells);
                _genToEngine = new NativeArray<byte>(256, Allocator.Persistent);
                for (int i = 0; i < 256; i++) _genToEngine[i] = GenToEngine((byte)i);
                Debug.Log($"[FarField] tg2 {(Tg2World ? "WORLD (aligned)" : "preview")}: world map v{Tg2Spec.mapVersion} (biome x{Tg2Spec.BiomeScale}, {Tg2Spec.BiomeSet} biomes{(Tg2Map.Record != null ? $", record {Tg2Map.Record.Bytes} B" : "")}) built in {swm.ElapsedMilliseconds} ms, " +
                          $"offset {Tg2OffsetVoxels}, sea shift {Tg2SeaShift}");
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

        /// Task 19 probe (diagnostic): the bitmap word last uploaded, and the
        /// window/band it was built for.
        public uint ResidentWord(int i) => i >= 0 && i < _residentBits.Length ? _residentBits[i] : 0u;
        public int3 LastResidencyOrigin, LastResidencyDims;
        public int2 LastResidencyLayers;

        /// Rebuilds the residency bitmap from the store (1024 lookups for a
        /// 32x32 window; uploaded every frame, 128 bytes).
        public void UpdateResidency(ChunkStore store, int3 windowOriginChunks, int3 windowDimsChunks)
            => UpdateResidency(store, windowOriginChunks, windowDimsChunks, 0, 0);

        /// V2: a column is resident when EVERY chunk of the loaded band
        /// [layerLo, layerHi] is -- a half-streamed column would otherwise hand
        /// its missing layers to the voxel march as air. Flat worlds: 0..0,
        /// exactly the old "its y=0 chunk is resident".
        public void UpdateResidency(ChunkStore store, int3 windowOriginChunks, int3 windowDimsChunks, int layerLo, int layerHi)
        {
            long r0 = System.Diagnostics.Stopwatch.GetTimestamp();
            int n = windowDimsChunks.x * windowDimsChunks.z;
            int words = (n + 31) >> 5;
            if (_residentBits.Length != words) _residentBits = new uint[words];
            Array.Clear(_residentBits, 0, words);
            for (int rz = 0; rz < windowDimsChunks.z; rz++)
            for (int rx = 0; rx < windowDimsChunks.x; rx++)
            {
                bool all = true;
                for (int cy = layerLo; cy <= layerHi && all; cy++)
                    all = store.IsResident(new int3(windowOriginChunks.x + rx, cy, windowOriginChunks.z + rz));
                if (!all) continue;
                int i = rz * windowDimsChunks.x + rx;
                _residentBits[i >> 5] |= 1u << (i & 31);
            }
            if (Resident.count < words)
            {
                Resident.Release();
                Resident = new GraphicsBuffer(GraphicsBuffer.Target.Structured, words, sizeof(uint));
            }
            Resident.SetData(_residentBits, 0, 0, words);
            LastResidencyOrigin = windowOriginChunks; LastResidencyDims = windowDimsChunks;
            LastResidencyLayers = new int2(layerLo, layerHi);
            LastFrame.residencyMs = (float)((System.Diagnostics.Stopwatch.GetTimestamp() - r0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
        }

        /// Per-frame: finish jobs, issue at most ONE strip scatter, schedule new work.
        public void Update(Vector3 camMetres)
        {
            var fs = new FrameStats();
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            double applyTicks = 0;
            QueueOpPatches();
            // 1. Harvest finished jobs.
            for (int L = 0; L < LEVELS; L++)
            {
                ref Pending p = ref _pending[L];
                if (!p.active || !p.handle.IsCompleted) continue;
                p.handle.Complete();
                LastStripJobMs = (Time.realtimeSinceStartupAsDouble - p.startedAt) * 1000.0;
                if (p.full)
                {
                    long a0 = System.Diagnostics.Stopwatch.GetTimestamp();
                    ApplyFull(L);
                    applyTicks += System.Diagnostics.Stopwatch.GetTimestamp() - a0;
                    fs.fulls++;
                }
                else if (!_readyStrips.Contains(L)) _readyStrips.Enqueue(L);
            }
            long t1 = System.Diagnostics.Stopwatch.GetTimestamp();

            // 2. One strip per frame reaches the GPU (bounded upload).
            // The staging buffer holds ONE strip: never overwrite it before the
            // render pass has consumed the previous scatter.
            // Op patches (task 82) take turns with strips for the staging buffer.
            bool patchReady = _patches.Count > 0 && _patches[0].handle.IsCompleted;
            if (PendingScatters.Count == 0 && patchReady && (_readyStrips.Count == 0 || _patchTurn))
            {
                IssuePatch();
                _patchTurn = false;
            }
            else if (_readyStrips.Count > 0 && PendingScatters.Count == 0)
            {
                int L = _readyStrips.Dequeue();
                ref Pending p = ref _pending[L];
                Staging.SetData(p.result, 0, 0, p.w * p.h);
                BytesUploaded += (long)p.w * p.h * 4;
                PendingScatters.Add(new Scatter { level = L, x0 = p.x0, z0 = p.z0, w = p.w, h = p.h });
                ShadowScatter(L, p.x0, p.z0, p.w, p.h, p.result);
                PendingMipMask |= 1 << L;
                _origin[L] = p.newOrigin;
                MaxHeightVoxels = math.max(MaxHeightVoxels, p.maxH[0]);
                StripsApplied++;
                fs.strips++;
                PatchAfter(L, p.x0, p.z0, p.w, p.h, p.opsUsed);
                p.result.Dispose(); p.maxH.Dispose(); p.ops.Dispose();
                p.active = false;
                _patchTurn = true;
            }
            long t2 = System.Diagnostics.Stopwatch.GetTimestamp();

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
            long t3 = System.Diagnostics.Stopwatch.GetTimestamp();

            if (MipStressLevels > 0) PendingMipMask |= (1 << math.min(MipStressLevels, LEVELS)) - 1;
            double toMs = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            fs.harvestMs = (float)((t1 - t0 - applyTicks) * toMs);
            fs.applyFullMs = (float)(applyTicks * toMs);
            fs.stagingMs = (float)((t2 - t1) * toMs);
            fs.scheduleMs = (float)((t3 - t2) * toMs);
            fs.mipLevels = math.countbits(PendingMipMask);
            fs.residencyMs = LastFrame.residencyMs;   // UpdateResidency runs after Update and overwrites it
            LastFrame = fs;
        }

        private void ScheduleStrip(int L, int2 newOrigin, int x0, int z0, int w, int h)
        {
            ref Pending p = ref _pending[L];
            p.active = true; p.full = false; p.newOrigin = newOrigin;
            p.x0 = x0; p.z0 = z0; p.w = w; p.h = h;
            p.result = new NativeArray<uint>(w * h, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            p.maxH = new NativeArray<int>(1, Allocator.Persistent);
            p.startedAt = Time.realtimeSinceStartupAsDouble;
            p.ops = OpsFor(L, x0, z0, w, h, out p.opsUsed);
            var h1 = ScheduleFill(TexelVoxels(L), x0, z0, w, false, int2.zero, p.result, w * h, 1024, p.ops);
            p.handle = new MaxJob { values = p.result, maxH = p.maxH }.Schedule(h1);
            ColumnsSampled += (long)w * h;
        }

        private void ScheduleFull(int L, int2 origin)
        {
            ref Pending p = ref _pending[L];
            if (p.active) { p.handle.Complete(); p.result.Dispose(); p.maxH.Dispose(); p.ops.Dispose(); }
            p.active = true; p.full = true; p.newOrigin = origin;
            p.result = new NativeArray<uint>(LEVEL_TEXELS, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            p.maxH = new NativeArray<int>(1, Allocator.Persistent);
            p.startedAt = Time.realtimeSinceStartupAsDouble;
            p.ops = OpsFor(L, origin.x, origin.y, EDGE, EDGE, out p.opsUsed);
            var h1 = ScheduleFill(TexelVoxels(L), origin.x, origin.y, EDGE, true, origin, p.result, LEVEL_TEXELS, 4096, p.ops);
            p.handle = new MaxJob { values = p.result, maxH = p.maxH }.Schedule(h1);
            ColumnsSampled += LEVEL_TEXELS;
        }

        private void ApplyFull(int L)
        {
            ref Pending p = ref _pending[L];
            p.handle.Complete();
            Texels.SetData(p.result, 0, L * LEVEL_TEXELS, LEVEL_TEXELS);
            if (Shadow != null) NativeArray<uint>.Copy(p.result, 0, Shadow, L * LEVEL_TEXELS, LEVEL_TEXELS);
            BytesUploaded += LEVEL_TEXELS * 4L;
            _origin[L] = p.newOrigin;
            MaxHeightVoxels = math.max(MaxHeightVoxels, p.maxH[0]);
            PendingMipMask |= 1 << L;
            if (Ready) FullRefills++;
            PatchAfter(L, p.newOrigin.x, p.newOrigin.y, EDGE, EDGE, p.opsUsed);
            p.result.Dispose(); p.maxH.Dispose(); p.ops.Dispose();
            p.active = false;
        }

        private JobHandle ScheduleFill(int texelVoxels, int x0, int z0, int w, bool ringOrder, int2 ringOrigin,
                                       NativeArray<uint> output, int count, int batch,
                                       NativeArray<VoxelEngine.Streaming.EditOp> ops)
        {
            int opCount = ops.Length == 1 && ops[0].kind == 0 ? 0 : ops.Length;   // OpsFor's empty marker
            if (UsesTg2)
                return new FillTg2Job
                {
                    h = _tgH, r = _tgR, wat = _tgW, cls = _tgCls, flow = _tgFlow, spec = Tg2Spec, o = _tgO, genToEngine = _genToEngine, offset = Tg2OffsetVoxels,
                    seaShift = Tg2SeaShift,
                    texelVoxels = texelVoxels, x0 = x0, z0 = z0, w = w, ringOrder = ringOrder, ringOrigin = ringOrigin, output = output,
                    ops = ops, opCount = opCount,
                }.Schedule(count, batch);
            return new FillRegionJob
            {
                st = _st, surfaceByBiome = _surfaceByBiome, texelVoxels = texelVoxels,
                x0 = x0, z0 = z0, w = w, ringOrder = ringOrder, ringOrigin = ringOrigin, output = output,
                ops = ops, opCount = opCount,
            }.Schedule(count, batch);
        }

        // ------------------------------------------------------------------
        // Edit ops (task 82, header EDITS). Main thread only.
        // ------------------------------------------------------------------

        /// Level L texels [tx0, tx1] x [tz0, tz1] whose centre column an op's box reaches.
        public static void OpTexelRect(in VoxelEngine.Streaming.EditOp op, int L, out int2 t0, out int2 t1)
        {
            int sh = 3 + L, half = (8 << L) >> 1, tv = 8 << L;
            int2 c = op.centreVoxel.xz;
            t0 = (c - op.size - half + (tv - 1)) >> sh;   // ceil, by shift (floor for negatives too)
            t1 = (c + op.size - half) >> sh;
        }

        private static bool Overlaps(int2 t0, int2 t1, int x0, int z0, int w, int h)
            => t1.x >= x0 && t0.x < x0 + w && t1.y >= z0 && t0.y < z0 + h && t1.x >= t0.x && t1.y >= t0.y;

        /// The log's ops that reach region (x0, z0, w, h) of level L, in seq order,
        /// in an array the job owns. No op: one entry of kind 0 (never a real kind).
        private NativeArray<VoxelEngine.Streaming.EditOp> OpsFor(int L, int x0, int z0, int w, int h, out int used)
        {
            var snap = _ops?.Snapshot;
            used = snap?.Count ?? 0;
            int n = 0;
            if (snap != null)
                for (int i = 0; i < snap.Count; i++)
                {
                    OpTexelRect(snap.Ops[i], L, out int2 a, out int2 b);
                    if (Overlaps(a, b, x0, z0, w, h)) n++;
                }
            var arr = new NativeArray<VoxelEngine.Streaming.EditOp>(math.max(1, n), Allocator.Persistent);
            if (n == 0) return arr;   // default(EditOp).kind == 0: the empty marker
            int j = 0;
            for (int i = 0; i < snap.Count; i++)
            {
                OpTexelRect(snap.Ops[i], L, out int2 a, out int2 b);
                if (Overlaps(a, b, x0, z0, w, h)) arr[j++] = snap.Ops[i];
            }
            return arr;
        }

        /// The ops recorded since the last Update: patch each one's footprint, every level.
        private void QueueOpPatches()
        {
            if (_ops == null) return;
            var snap = _ops.Snapshot;
            if (snap.Count < _opsSeen) _opsSeen = snap.Count;   // the log was cleared
            for (int i = _opsSeen; i < snap.Count; i++)
                for (int L = 0; L < LEVELS; L++)
                {
                    OpTexelRect(snap.Ops[i], L, out int2 a, out int2 b);
                    SchedulePatch(L, a, b);
                }
            _opsSeen = snap.Count;
        }

        /// A strip or refill computed with ops 0..opsUsed has just gone up: the
        /// ops recorded since then get a patch behind it (FIFO, so it lands after).
        private void PatchAfter(int L, int x0, int z0, int w, int h, int opsUsed)
        {
            var snap = _ops?.Snapshot;
            if (snap == null || opsUsed >= snap.Count) return;
            for (int i = opsUsed; i < snap.Count; i++)
            {
                OpTexelRect(snap.Ops[i], L, out int2 a, out int2 b);
                a = math.max(a, new int2(x0, z0)); b = math.min(b, new int2(x0 + w - 1, z0 + h - 1));
                SchedulePatch(L, a, b);
            }
        }

        /// Recompute texels [t0, t1] of level L (clipped to the ring as it is now;
        /// texels that enter it later come with their strip) in bands that fit
        /// the staging buffer.
        private void SchedulePatch(int L, int2 t0, int2 t1)
        {
            int2 o = _origin[L];
            t0 = math.max(t0, o); t1 = math.min(t1, o + (EDGE - 1));
            if (math.any(t1 < t0)) return;
            int w = t1.x - t0.x + 1, rows = math.max(1, (STRIP_MAX * EDGE) / w);
            for (int z = t0.y; z <= t1.y; z += rows)
            {
                int h = math.min(rows, t1.y - z + 1);
                var pt = new Patch { level = L, x0 = t0.x, z0 = z, w = w, h = h };
                pt.result = new NativeArray<uint>(w * h, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                pt.ops = OpsFor(L, t0.x, z, w, h, out _);
                pt.handle = ScheduleFill(TexelVoxels(L), t0.x, z, w, false, int2.zero, pt.result, w * h, 256, pt.ops);
                ColumnsSampled += (long)w * h;
                _patches.Add(pt);
            }
        }

        /// The oldest patch goes up through the staging buffer, clipped to the ring
        /// as it is now (the origin may have moved since it was scheduled).
        private void IssuePatch()
        {
            var pt = _patches[0];
            _patches.RemoveAt(0);
            pt.handle.Complete();
            int2 o = _origin[pt.level];
            int2 a = math.max(new int2(pt.x0, pt.z0), o);
            int2 b = math.min(new int2(pt.x0 + pt.w - 1, pt.z0 + pt.h - 1), o + (EDGE - 1));
            if (math.all(b >= a))
            {
                int w = b.x - a.x + 1, h = b.y - a.y + 1;
                var sub = new NativeArray<uint>(w * h, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                int maxH = 0;
                for (int z = 0; z < h; z++)
                for (int x = 0; x < w; x++)
                {
                    uint v = pt.result[(a.y - pt.z0 + z) * pt.w + (a.x - pt.x0 + x)];
                    sub[z * w + x] = v;
                    maxH = math.max(maxH, (int)(v & 0xFFFFu));
                }
                Staging.SetData(sub, 0, 0, w * h);
                BytesUploaded += (long)w * h * 4;
                PendingScatters.Add(new Scatter { level = pt.level, x0 = a.x, z0 = a.y, w = w, h = h });
                ShadowScatter(pt.level, a.x, a.y, w, h, sub);
                PendingMipMask |= 1 << pt.level;
                MaxHeightVoxels = math.max(MaxHeightVoxels, maxH);
                PatchesApplied++; PatchTexels += (long)w * h;
                sub.Dispose();
            }
            pt.result.Dispose(); pt.ops.Dispose();
        }

        /// FarScatter on the CPU, into Shadow (tests).
        private void ShadowScatter(int L, int x0, int z0, int w, int h, NativeArray<uint> data)
        {
            if (Shadow == null) return;
            for (int i = 0; i < w * h; i++)
            {
                int tx = x0 + i % w, tz = z0 + i / w;
                Shadow[L * LEVEL_TEXELS + ((tz & EDGE_MASK) << 10) + (tx & EDGE_MASK)] = data[i];
            }
        }

        /// The vertical span [ylo, yhi] an op writes in engine column (px, pz), or false.
        /// Exactly EditOpApply.Covers: a sphere's |dy| <= isqrt(s^2 - dx^2 - dz^2).
        public static bool OpColumnSpan(in VoxelEngine.Streaming.EditOp op, int px, int pz, out int ylo, out int yhi)
        {
            int dx = px - op.centreVoxel.x, dz = pz - op.centreVoxel.z, s = op.size, t;
            ylo = 0; yhi = -1;
            if (op.kind == VoxelEngine.Streaming.EditOpKind.Box)
            {
                if (math.abs(dx) > s || math.abs(dz) > s) return false;
                t = s;
            }
            else if (op.kind == VoxelEngine.Streaming.EditOpKind.Sphere)
            {
                long q = (long)s * s - (long)dx * dx - (long)dz * dz;
                if (q < 0) return false;
                t = (int)math.sqrt((double)q);
                while ((long)t * t > q) t--;
                while ((long)(t + 1) * (t + 1) <= q) t++;
            }
            else return false;
            ylo = op.centreVoxel.y - t; yhi = op.centreVoxel.y + t;
            return true;
        }

        /// Does the op carve the top off a dry column (so the new top needs the floor's material)?
        public static bool CarvesDryTop(uint t, in VoxelEngine.Streaming.EditOp op, int ylo, int yhi)
        {
            if (op.material != Materials.Air || ((t >> 24) & FLAG_WATER) != 0) return false;
            int top = (int)(t & 0xFFFFu) - 1;
            return ylo <= top && yhi >= top;
        }

        private static uint PackFill(byte m, int groundTop, int newTop)
        {
            uint h = (uint)math.clamp(newTop, 0, 65535);
            if (m == Materials.Water) return h | ((uint)m << 16) | (FLAG_WATER << 24) | DepthBits(newTop - groundTop);
            if (m == Materials.Lava) return h | ((uint)m << 16) | (FLAG_WATER << 24);
            return h | ((uint)m << 16);
        }

        /// ONE op on ONE packed texel whose column it writes over [ylo, yhi]: the
        /// 2.5D picture of the edited column (the header's EDITS lists what it can
        /// and cannot show). `floorMat` is used only when CarvesDryTop.
        public static uint ApplyOp(uint t, in VoxelEngine.Streaming.EditOp op, int ylo, int yhi, byte floorMat)
        {
            int H = (int)(t & 0xFFFFu);                    // first air voxel above the column
            bool liquid = ((t >> 24) & FLAG_WATER) != 0;
            bool carve = op.material == Materials.Air;
            if (!liquid)
            {
                if (carve) return ylo <= H - 1 && yhi >= H - 1 ? (uint)math.max(ylo, 0) | ((uint)floorMat << 16) : t;
                // A fill that covers the top voxel or the first air above it.
                return ylo <= H && yhi >= H - 1 ? PackFill(op.material, H, math.max(H, yhi + 1)) : t;
            }
            byte m = (byte)(t >> 16);
            if (m == Materials.Lava)   // its ground is not known: only a fill on its surface shows
                return !carve && ylo <= H && yhi >= H - 1 ? PackFill(op.material, H, math.max(H, yhi + 1)) : t;
            int d = (int)(t >> 25);
            if (d <= 0 || d >= 127) return t;              // ground unknown (deeper than 12.7 m)
            int G = H - d;                                 // first water voxel = the ground's first air
            if (carve)
                return ylo <= G - 1 && yhi >= G - 1 ? (t & 0x01FFFFFFu) | DepthBits(H - math.max(ylo, 0)) : t;
            if (op.material == Materials.Water || op.material == Materials.Lava) return t;
            if (ylo > G || yhi < G - 1) return t;          // not on the bed
            int top = math.max(G, yhi + 1);
            return top >= H ? PackFill(op.material, G, top) : (t & 0x01FFFFFFu) | DepthBits(H - top);
        }

        /// GenMat -> today's material ids: the far band's preview and V1's
        /// TerrainGenChunkSource (the palette-D materials, V1b, replace it).
        /// TerrainGen material -> engine material. Fluids keep their engine ids
        /// (their behaviour lives there); every other GenMat is its own engine
        /// material, Materials.TG2_BASE + g, coloured by palette D (owner,
        /// 2026-09-26: "add the materials now"). V1's chunk source maps every
        /// voxel through here, the far band every surface texel.
        public static byte GenToEngine(byte g)
        {
            switch (g)
            {
                case TerrainGen.GenMat.Air: return Materials.Air;
                case TerrainGen.GenMat.Water: return Materials.Water;
                case TerrainGen.GenMat.Lava: return Materials.Lava;
                case TerrainGen.GenMat.Honey: return Materials.Honey;
                case TerrainGen.GenMat.Oil: return Materials.Oil;
                case TerrainGen.GenMat.Acid: return Materials.Acid;
                case TerrainGen.GenMat.Sludge: return Materials.Sludge;
                case TerrainGen.GenMat.Quicksand: return Materials.Quicksand;
            }
            return g < 96 ? Materials.FromTg2(g) : Materials.Stone;
        }

        /// The pre-palette-D mapping (every GenMat collapsed onto ~12 legacy
        /// engine materials). Kept for reference and A/B looks; unused.
        public static byte GenToEngineLegacy(byte g)
        {
            switch (g)
            {
                // Air is never a far-band surface, but V1's chunk source maps
                // every voxel through this table (found by its LOD-0 test: Air
                // fell through to Stone and every chunk was solid to its top).
                case TerrainGen.GenMat.Air: return Materials.Air;
                case TerrainGen.GenMat.Water: return Materials.Water;
                case TerrainGen.GenMat.Lava: return Materials.Lava;
                case TerrainGen.GenMat.Honey: return Materials.Honey;
                case TerrainGen.GenMat.Oil: return Materials.Oil;
                case TerrainGen.GenMat.Acid: return Materials.Acid;
                case TerrainGen.GenMat.Sludge: return Materials.Sludge;
                case TerrainGen.GenMat.Quicksand: return Materials.Quicksand;
                case TerrainGen.GenMat.Grass: return Materials.Grass;
                case TerrainGen.GenMat.JungleGrass: return Materials.JungleGrass;
                case TerrainGen.GenMat.Sand:
                case TerrainGen.GenMat.BeachSand: return Materials.Sand;
                case TerrainGen.GenMat.Snow:
                case TerrainGen.GenMat.PackedIce: return Materials.Snow;
                case TerrainGen.GenMat.PackedSand: return Materials.Sandstone;
                // §2.5 stamps -> the nearest existing engine material (PREVIEW
                // only; their own engine ids land with the §2.6 material set).
                case TerrainGen.GenMat.Sinter: return Materials.Snow;
                case TerrainGen.GenMat.CrystalBlue: return Materials.TorchBlue;
                case TerrainGen.GenMat.CrystalAmber: return Materials.Torch;
                case TerrainGen.GenMat.CrystalRose: return Materials.TorchPurple;
                case TerrainGen.GenMat.CrystalGreen: return Materials.TorchGreen;
                case TerrainGen.GenMat.Bone: return Materials.Sand;
                case TerrainGen.GenMat.MeteorOre: return Materials.Lava;
                case TerrainGen.GenMat.Bark: return Materials.Sandstone;
                case TerrainGen.GenMat.Leaves: return Materials.JungleGrass;
                case TerrainGen.GenMat.VolcanicRock: return Materials.Obsidian;
                case TerrainGen.GenMat.StandingStone: return Materials.Stone;
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
                p.handle.Complete(); p.result.Dispose(); p.maxH.Dispose(); p.ops.Dispose(); p.active = false;
            }
            foreach (var pt in _patches) { pt.handle.Complete(); pt.result.Dispose(); pt.ops.Dispose(); }
            _patches.Clear();
            Texels?.Release(); Mips?.Release(); Staging?.Release(); Resident?.Release(); Resident = null;
            Texels = null; Mips = null; Staging = null;
            _st.Dispose();
            if (_surfaceByBiome.IsCreated) _surfaceByBiome.Dispose();
            if (_tgH.IsCreated) _tgH.Dispose();
            if (_tgR.IsCreated) _tgR.Dispose();
            if (_tgW.IsCreated) _tgW.Dispose();
            if (_tgCls.IsCreated) _tgCls.Dispose();
            if (_tgFlow.IsCreated) _tgFlow.Dispose();
            RiverFlow?.Release(); RiverFlow = null;
            if (_genToEngine.IsCreated) _genToEngine.Dispose();
            if (_ownsTg2Map) Tg2Map?.Dispose();
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
                return (uint)(SEA + 1) | ((uint)Materials.Water << 16) | (FLAG_WATER << 24) | DepthBits(SEA - columnHeight);
            return (uint)(columnHeight + 1) | ((uint)surfaceByBiome[biome] << 16);
        }

        [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High, CompileSynchronously = true)]   // sync: EditMode tests compare jobs (task 82)
        private struct FillRegionJob : IJobParallelFor
        {
            [ReadOnly] public ColumnSampler.State st;
            [ReadOnly] public NativeArray<byte> surfaceByBiome;
            public int texelVoxels, x0, z0, w;
            public bool ringOrder;       // true: index i IS ring slot i (full level)
            public int2 ringOrigin;
            [WriteOnly] public NativeArray<uint> output;
            [ReadOnly] public NativeArray<VoxelEngine.Streaming.EditOp> ops;   // task 82
            public int opCount;

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
                uint t = Pack(h, b, surfaceByBiome);
                int px = tx * texelVoxels + half, pz = tz * texelVoxels + half;
                int fillLo = int.MaxValue; byte fillMat = 0;
                for (int j = 0; j < opCount; j++)
                {
                    var op = ops[j];
                    if (!OpColumnSpan(in op, px, pz, out int ylo, out int yhi)) continue;
                    byte fm = Materials.Stone;   // the legacy band's floor: no material oracle here
                    if (CarvesDryTop(t, in op, ylo, yhi) && ylo - 1 >= fillLo) fm = fillMat;
                    uint n = ApplyOp(t, in op, ylo, yhi, fm);
                    if (op.material != Materials.Air && n != t) { fillLo = ylo; fillMat = op.material; }
                    t = n;
                }
                output[i] = t;
            }
        }

        [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High, CompileSynchronously = true)]   // sync: EditMode tests compare jobs (task 82)
        private struct FillTg2Job : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float> h, r, wat;
            [ReadOnly] public NativeArray<byte> cls;
            [ReadOnly] public NativeArray<ushort> flow;
            [ReadOnly] public NativeArray<byte> genToEngine;
            public TerrainGen.TerrainSpec spec;
            public TerrainGen.Offsets o;
            public int2 offset;
            public int seaShift;
            public int texelVoxels, x0, z0, w;
            public bool ringOrder;
            public int2 ringOrigin;
            [WriteOnly] public NativeArray<uint> output;
            [ReadOnly] public NativeArray<VoxelEngine.Streaming.EditOp> ops;   // task 82
            public int opCount;

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
                    water = (float*)Unity.Collections.LowLevel.Unsafe.NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(wat),
                    waterClass = (byte*)Unity.Collections.LowLevel.Unsafe.NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(cls),
                    flowDir = flow.Length > 1 ? (ushort*)Unity.Collections.LowLevel.Unsafe.NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(flow) : null,   // task 76
                    cells = spec.mapCells, cellVoxels = spec.mapCellVoxels,
                };
                int half = texelVoxels >> 1;
                float gx = tx * texelVoxels + half + offset.x, gz = tz * texelVoxels + half + offset.y;
                var c = TerrainGen.ShapeField.Sample(in spec, in map, in o, gx, gz, texelVoxels);
                TerrainGen.ShapeField.FarColumn(in spec, ref c);   // v4: a ledge is drawn at its floor, not as a filled pillar
                float step = TerrainGen.ShapeField.MaxStep(in spec, in map, in o, gx, gz, texelVoxels, c.height) ;
                // MaxStep is per voxel at this spacing; the surface rules want the
                // per-voxel step, so it is directly comparable.
                byte gm = TerrainGen.DetailField.TopMaterial(in spec, in c, step, in o, (int)gx, (int)gz);
                int top = c.height + 1 - seaShift;
                uint t;
                if (c.height < spec.seaLevelY)
                    t = (uint)(spec.seaLevelY + 1 - seaShift) | ((uint)Materials.Water << 16) | (FLAG_WATER << 24) | DepthBits(spec.seaLevelY - c.height);
                else if (c.water > c.height)   // map v2: a lake or river (or the caldera's lava) over this column
                    t = (uint)math.clamp(c.water + 1 - seaShift, 0, 65535)
                              | ((uint)(c.waterKind == 1 ? Materials.Lava : Materials.Water) << 16) | (FLAG_WATER << 24)
                              | (c.waterKind == 1 ? 0u : DepthBits(c.water - c.height));
                else
                    t = (uint)math.clamp(top, 0, 65535) | ((uint)genToEngine[gm] << 16);

                // Task 82: the save's edit ops, in seq order (header EDITS). A carve's
                // floor takes the generator's material at its depth in this column
                // (the band's own column sample), or the material of an earlier fill.
                int px = tx * texelVoxels + half, pz = tz * texelVoxels + half;
                int fillLo = int.MaxValue; byte fillMat = 0;
                bool cdReady = false; var cd = default(TerrainGen.DetailField.ColumnDetail);
                for (int j = 0; j < opCount; j++)
                {
                    var op = ops[j];
                    if (!OpColumnSpan(in op, px, pz, out int ylo, out int yhi)) continue;
                    byte fm = 0;
                    if (CarvesDryTop(t, in op, ylo, yhi))
                    {
                        int fy = ylo - 1;
                        if (fy >= fillLo) fm = fillMat;
                        else if (fy < 0) fm = Materials.Stone;
                        else
                        {
                            if (!cdReady) { cd = TerrainGen.DetailField.PrepareColumn(in spec, in o, (int)gx, (int)gz); cdReady = true; }
                            byte g = TerrainGen.DetailField.Material(in spec, in o, in c, in cd, step, (int)gx, fy + seaShift, (int)gz);
                            fm = TerrainGen.GenMat.IsFluid(g) || g == TerrainGen.GenMat.Air ? Materials.Stone : genToEngine[g];
                        }
                    }
                    uint n = ApplyOp(t, in op, ylo, yhi, fm);
                    if (op.material != Materials.Air && n != t) { fillLo = ylo; fillMat = op.material; }
                    t = n;
                }
                output[i] = t;
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
