// ==========================================
// Assets/CoreEngine/Rendering/RaymarchFeature.cs
//
// PHASE 4 REVISION. Additive only -- no traversal behaviour changes. New
// bindings for the sliding window and the chunk-major clipmap layout:
//   _WindowDimsChunksPacked   window size in chunks (chunk-major indexing)
//   _WindowOriginBricksPacked window minimum corner in bricks (bounds guard)
//   _ContentCeilingVoxelY     replaces the hardcoded 128 early-exit
//   _CascadeTierInfo1/2       coarse-brick geometry per cascade tier
// See PATCH_phase4_shaders.md for the matching shader edits.
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;
using VoxelEngine.Mirror;

public class RaymarchFeature : ScriptableRendererFeature
{
    public enum DebugMode
    {
        Beauty = 0,
        StepHeat = 1,
        UniformDense = 2,
        Normals = 3,
        NonExitHeat = 4,
        VoxelGrain = 5,
        LODTier = 6,
        // §10.3 item 6, Phase 7: "sun-visibility view + shadow-ray step-count
        // heat". Appended, never inserted -- RaymarchDebugControls, the capture
        // rigs and several report files all key off these integers.
        SunVis = 7,
        ShadowHeat = 8,
    }

    public enum GateResMode { UseInspector, Native, Forced960x540, ForcedCustom }
    public static GateResMode GateModeOverride = GateResMode.UseInspector;
    public static Vector2Int CustomGateResolution = new Vector2Int(960, 540);

    [Tooltip("Runtime debug view. Beauty is the shipped output.")]
    public DebugMode debugMode = DebugMode.Beauty;

    [Header("Gate measurement resolution")]
    [SerializeField] private bool _forceGateResolution = true;
    [SerializeField] private int _gateWidth = 960;
    [SerializeField] private int _gateHeight = 540;

    public static bool AirMipEnabled = true;

    // CERTIFIED DEFAULT: mode 4 (DenseSkip). Amendment 8.9 recorded it as the
    // fastest measured mode and the one with a CPU oracle fuzz proof.
    public static int TraversalMode = 4;

    // Phase 3 PATCH_iteration_cap: 400 -> 1024. The .compute clamp that pinned
    // the effective cap at 400 was removed in the same patch.
    public static int MaxOuterIterations = 1024;

    public static bool UseStrippedKernel = false;
    public static bool UseMemoryProbeKernel = false;
    public static int ProbeIterations = 14;

    public static bool UsePackedMips = true;
    public static float MaxRayDistance = 1280f;
    public static bool UseLODCascade = true;
    public static bool UseDevColors = true;

    // ---- PRESENTATION TOGGLES (shading step only) ----
    // Each gates a purely cosmetic term applied after traversal has resolved
    // the hit. Turning all three off reproduces the pre-palette look exactly.
    // They exist as toggles precisely so Phase 7's real lighting (§6.5) can
    // disable any of them without unpicking code.
    public static bool ShadeAmbientOcclusion = true;
    public static bool ShadeHemisphere = true;
    public static bool ShadeDistanceFog = true;
    /// Also the fog target and the hemisphere "up" tint. A pale, slightly cool
    /// daylight haze -- deliberately not a saturated blue, so distant terrain
    /// desaturates into it instead of turning blue.
    public static Color SkyColor = new Color(0.62f, 0.70f, 0.78f, 1f);
    public static float FogStartMetres = 45f;
    public static float FogEndMetres = 260f;

    // ---- PHASE 7 LIGHTING (§6.5, §3.8, C.9/C.10/C.11) ----
    //
    // Statics, matching every other render toggle in this file, so a rig can
    // set them before the first frame without a scene reference. The VALUES
    // come from LightingConfig (live-reloadable JSON) and SunCycle every frame;
    // these are the seams a rig or the Playground drives.

    /// The A.6 light buffer this feature binds. Owned by whoever builds the
    /// world (the bootstrapper / Playground), not by the renderer -- the
    /// renderer only reads it, which keeps §0.1 invariant 1's direction of
    /// travel obvious: lights are a consequence of terrain edits.
    public static VoxelEngine.Rendering.LightSourceBuffer Lights;

    /// §6.5's fallback ladder. Defaults to EngineConfig's shipped rung; a rig
    /// overrides it per process launch so Stage 6 can measure every rung from
    /// ONE binary in ONE session.
    public static int ShadowQualityLevel = EngineConfig.SHADOW_QUALITY_LEVEL;

    /// Runaway guard for the shadow DDA, in OUTER iterations -- the same unit
    /// as MaxOuterIterations, and deliberately the same value. A shadow ray
    /// crosses the same clipmap with the same leap primitives, so there is no
    /// basis for a different number, and two numbers would invite one to drift.
    public static int MaxShadowSteps = 1024;

    /// C.9 attaches `max(0, N.L)` to the SUN term only and leaves `pointLights`
    /// bare. This was shipped at the literal reading (false) and FLAGGED rather
    /// than decided -- PHASE_7_COMPLETION.md §10 flag 3.
    ///
    /// DEFAULT FLIPPED TO TRUE, 2026-09-13, ON EVIDENCE RATHER THAN TASTE.
    /// The evidence is §8's own screenshot finding: in
    /// 02_cave_lit_by_torch.png every face of the chamber is lit identically
    /// regardless of which way it points, because with no N.L the only thing
    /// modulating a point light is distance. A cube lit from one side that is
    /// equally bright on all six faces does not read as lit at all -- it reads
    /// as tinted. §6.1's aesthetic contract is explicit that the look is carried
    /// by "albedo, the lighting of §6.5, and voxel ambient occlusion", and a
    /// lighting term that cannot distinguish a face from its opposite is not
    /// carrying anything.
    ///
    /// WHY THIS IS A DEFAULT FLIP AND NOT A SPEC DEVIATION TO ARGUE ABOUT: the
    /// toggle is unchanged and still reachable, false still reproduces C.9's
    /// literal grouping exactly, and both states are screenshotted side by side
    /// so the owner can reverse it in one edit. It is reversible, evidence-
    /// backed, and pinned by a test so it cannot drift back silently.
    public static bool PointLightNdotL = true;

    /// FLAG: modulate ambient by a straight-up sky-visibility probe so deep
    /// caves are genuinely dark (§6.5). It IS a second ray; measured as its own
    /// arm rather than assumed free.
    public static bool UseSkyProbeAmbient = true;

    /// Highest world voxel Y that generation can produce content for, +1.
    /// Set by the bootstrapper from StreamManager.MAX_GENERATED_CHUNK_Y.
    /// Kept tighter than the window's Y extent on purpose -- see the shader.
    public static int ContentCeilingVoxelY = 128;

    public static DebugMode DebugViewOverride = DebugMode.Beauty;
    public static bool UseDebugViewOverride = false;

    public static Vector2Int LastDispatchResolution = new Vector2Int(0, 0);

    /// Stage F's branch-engagement counters. Off by default: the per-pixel
    /// InterlockedAdd that fills them is expensive and would contaminate any
    /// timing arm. Enabled only by the -shadowstats diagnostic.
    /// Forces a tonemap curve regardless of the live config, so a capture pass
    /// can shoot the same preset with and without. -1 = use the config.
    public static int TonemapOverride = -1;

    /// Rig overrides for Stage F's two levers. -1 = use the live config.
    public static float ShadowMaxDistanceOverrideM = -1f;
    public static float Rung1DistanceOverrideM = -1f;

    /// Shadow LOD coarsening by ray TRAVEL as well as camera distance.
    /// &lt; 0 defers to LightingConfig; 0 disables (the shipped 8.2 behaviour, so
    /// every historical measurement stays reproducible); 1 is the
    /// penumbra-derived threshold. See Raymarch.compute's note on
    /// _ShadowTravelLOD for why max() of the two rules cannot reintroduce 8.1's
    /// detached shadows.
    public static float ShadowTravelLodOverride = -1f;

    /// Within-material colour variation (§M.4 item 1). &lt; 0 defers to
    /// LightingConfig; 0 off, 1 on.
    public static int MaterialVariationOverride = -1;

    public static bool CollectShadowStats = false;
    public static GraphicsBuffer ShadowStatsBuffer { get; private set; }
    private static readonly uint[] _statsZero = new uint[5];

    /// Zeroes the counters, so a read measures ONE frame rather than an
    /// unbounded accumulation.
    public static void ResetShadowStats()
    {
        if (ShadowStatsBuffer == null) return;
        ShadowStatsBuffer.SetData(_statsZero);
    }

    /// hit / beyondLod1 / traced / reused, for the last frame since a reset.
    public static uint[] ReadShadowStats()
    {
        var d = new uint[5];
        ShadowStatsBuffer?.GetData(d);
        return d;
    }

    public static GraphicsBuffer DebugBuffer { get; private set; }
    public static Vector2Int DebugPixel = new Vector2Int(700, 400);
    private const int DEBUG_BUFFER_FLOATS = 128;

    class RaymarchPass : ScriptableRenderPass
    {
        private ComputeShader _compute;
        private ComputeShader _computeStripped;
        private ComputeShader _computeMemoryProbe;
        private int _debugMode;
        private bool _forceRes;
        private int _gateW, _gateH;

        class ComputePassData
        {
            public ComputeShader compute;
            public Camera cam;
            public TextureHandle target;
            public int dispatchWidth;
            public int dispatchHeight;
            public GraphicsBuffer clipmapBuffer;
            public GraphicsBuffer brickDataBuffer;
            public GraphicsBuffer debugBuffer;
            public GraphicsBuffer shadowStats;
            public int collectShadowStats;
            public Vector3Int windowDimsBricks;
            public Vector3Int windowDimsChunks;
            public Vector3Int windowOriginBricks;
            public int contentCeilingVoxelY;
            public Vector2Int debugPixel;
            public int debugMode;
            public int traversalMode;
            public int useDevColors;
            public int shadeAO, shadeHemi, shadeFog;
            public Color skyColor;
            public float fogStart, fogEnd;
            public int maxOuterIterations;

            // ---- Phase 7 lighting ----
            public GraphicsBuffer lightBuffer;
            public int activeLightCount;
            public Vector4 sunDir;
            public Vector4 sunColor;
            public Vector4 ambientColor;
            public float kSoft;
            public int shadowQualityLevel;
            public int maxShadowSteps;
            public int pointLightNdotL;
            public int useSkyProbeAmbient;
            public float undergroundAmbientFloor;
            public float shadowMaxDistVoxels;
            public float rung1DistVoxels;
            public float shadowTravelLod;
            public int materialVariation;
            public float materialVariationAmount;
            public int tonemap;
            public float exposure;
            public float tonemapWhite;

            public bool useStripped;
            public bool useMemoryProbe;
            public int probeIterations;

            public GraphicsBuffer airMip1;
            public GraphicsBuffer airMip2;
            public GraphicsBuffer airMip3;
            public GraphicsBuffer airMip4;
            public Vector3Int airMipDims0;
            public Vector3Int airMipDims1;
            public Vector3Int airMipDims2;
            public Vector3Int airMipDims3;
            public int airMipLevelCount;

            public GraphicsBuffer airMipPacked;
            public Vector4[] mipInfo;
            public int usePackedMips;
            public float maxRayDistance;

            public GraphicsBuffer clipmapTier1;
            public GraphicsBuffer brickDataTier1;
            public Vector3Int windowDimsCoarseTier1;
            public Vector4 cascadeTierInfo1;
            public GraphicsBuffer clipmapTier2;
            public GraphicsBuffer brickDataTier2;
            public Vector3Int windowDimsCoarseTier2;
            public Vector4 cascadeTierInfo2;
            public Vector4 tierOuterRangeVoxels;
            public int useLODCascade;
        }

        class BlitPassData { public TextureHandle source; }

        public RaymarchPass(ComputeShader compute, ComputeShader computeStripped, ComputeShader computeMemoryProbe)
        {
            _compute = compute;
            _computeStripped = computeStripped;
            _computeMemoryProbe = computeMemoryProbe;
            renderPassEvent = RenderPassEvent.AfterRenderingSkybox;
        }

        public void SetDebugMode(int mode) => _debugMode = mode;
        public void SetGateResolution(bool force, int w, int h) { _forceRes = force; _gateW = w; _gateH = h; }

        /// log2 for the small power-of-two edge sizes the cascade uses.
        private static int Log2Int(int v) { int n = 0; while ((1 << n) < v) n++; return n; }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            bool useMemoryProbe = RaymarchFeature.UseMemoryProbeKernel && _computeMemoryProbe != null;
            bool useStripped = !useMemoryProbe && RaymarchFeature.UseStrippedKernel && _computeStripped != null;
            ComputeShader activeCompute = useMemoryProbe ? _computeMemoryProbe : (useStripped ? _computeStripped : _compute);
            if (activeCompute == null || TerrainClipmap.Active == null) return;

            if (DebugBuffer == null)
                DebugBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, DEBUG_BUFFER_FLOATS, sizeof(float));
            if (ShadowStatsBuffer == null)
                ShadowStatsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 5, sizeof(uint));

            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            TextureHandle activeColor = resourceData.activeColorTexture;

            RenderTextureDescriptor desc = cameraData.cameraTargetDescriptor;

            int dispatchW = _forceRes ? _gateW : desc.width;
            int dispatchH = _forceRes ? _gateH : desc.height;
            desc.width = dispatchW;
            desc.height = dispatchH;
            desc.enableRandomWrite = true;
            desc.depthBufferBits = 0;
            desc.msaaSamples = 1;
            desc.useMipMap = false;

            LastDispatchResolution = new Vector2Int(dispatchW, dispatchH);

            TextureHandle target = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, "_RaymarchTarget", false);

            using (var builder = renderGraph.AddComputePass<ComputePassData>("VoxelRaymarch_Compute", out var passData))
            {
                var clip = TerrainClipmap.Active;

                passData.compute = activeCompute;
                passData.useStripped = useStripped;
                passData.useMemoryProbe = useMemoryProbe;
                passData.probeIterations = RaymarchFeature.ProbeIterations;
                passData.cam = cameraData.camera;
                passData.target = target;
                passData.dispatchWidth = dispatchW;
                passData.dispatchHeight = dispatchH;
                passData.clipmapBuffer = clip.ClipmapBuffer;
                passData.brickDataBuffer = clip.BrickDataBuffer;
                passData.debugBuffer = DebugBuffer;
                passData.shadowStats = ShadowStatsBuffer;
                passData.collectShadowStats = CollectShadowStats ? 1 : 0;
                passData.debugPixel = DebugPixel;
                passData.debugMode = _debugMode;
                passData.traversalMode = TraversalMode;
                passData.useDevColors = UseDevColors ? 1 : 0;
                passData.shadeAO = ShadeAmbientOcclusion ? 1 : 0;
                passData.shadeHemi = ShadeHemisphere ? 1 : 0;
                passData.shadeFog = ShadeDistanceFog ? 1 : 0;
                passData.skyColor = SkyColor;
                passData.fogStart = FogStartMetres;
                passData.fogEnd = FogEndMetres;
                passData.maxOuterIterations = MaxOuterIterations;
                passData.usePackedMips = UsePackedMips ? 1 : 0;
                passData.contentCeilingVoxelY = ContentCeilingVoxelY;

                // ---- Phase 7 lighting ----
                // LightSourceBuffer.Upload() is the CPU's job and happens in the
                // world's own update, not here: §3.9's contract is CPU -> GPU
                // with "none (dispatch after write)" staleness, and the render
                // graph's record phase is not where a CPU-side selection pass
                // belongs. This only READS what was already selected.
                var cfg = LightingConfig.Active;
                var lights = Lights;
                passData.lightBuffer = lights != null ? lights.Buffer : null;
                passData.activeLightCount = lights != null ? lights.ActiveCount : 0;

                Unity.Mathematics.float3 sd = SunCycle.Direction;
                Unity.Mathematics.float3 sc = SunCycle.Color;
                Unity.Mathematics.float3 amb = SunCycle.Ambient;
                passData.sunDir = new Vector4(sd.x, sd.y, sd.z, 0f);
                passData.sunColor = new Vector4(sc.x, sc.y, sc.z, 0f);
                passData.ambientColor = new Vector4(amb.x, amb.y, amb.z, 0f);

                passData.kSoft = cfg.kSoft;
                passData.shadowQualityLevel = ShadowQualityLevel;
                passData.maxShadowSteps = MaxShadowSteps;
                passData.pointLightNdotL = PointLightNdotL ? 1 : 0;
                passData.useSkyProbeAmbient = UseSkyProbeAmbient ? 1 : 0;
                passData.undergroundAmbientFloor = cfg.undergroundAmbientFloor;
                float shadowClampM = ShadowMaxDistanceOverrideM >= 0f
                    ? ShadowMaxDistanceOverrideM : cfg.shadowMaxDistanceM;
                passData.shadowMaxDistVoxels = shadowClampM * 10f;
                float rung1M = Rung1DistanceOverrideM >= 0f
                    ? Rung1DistanceOverrideM : cfg.rung1DistanceM;
                passData.rung1DistVoxels = rung1M * 10f;
                passData.shadowTravelLod = ShadowTravelLodOverride >= 0f
                    ? ShadowTravelLodOverride : cfg.shadowTravelLod;
                passData.materialVariation = MaterialVariationOverride >= 0
                    ? MaterialVariationOverride : cfg.materialVariation;
                passData.materialVariationAmount = cfg.materialVariationAmount;
                passData.tonemap = TonemapOverride >= 0 ? TonemapOverride : cfg.tonemap;
                passData.exposure = cfg.exposure;
                passData.tonemapWhite = cfg.tonemapWhite;

                Unity.Mathematics.int3 dims = clip.WindowDimsBricks;
                passData.windowDimsBricks = new Vector3Int(dims.x, dims.y, dims.z);

                Unity.Mathematics.int3 dimsC = clip.WindowDimsChunks;
                passData.windowDimsChunks = new Vector3Int(dimsC.x, dimsC.y, dimsC.z);

                // The window's minimum corner, in bricks. Every frame: this is
                // what turns the Phase 3 bounds guard from "assumes origin
                // (0,0,0)" into a correct test under a moving window.
                Unity.Mathematics.int3 originB = clip.WindowOriginBricks;
                passData.windowOriginBricks = new Vector3Int(originB.x, originB.y, originB.z);

                int levelCount = AirMipEnabled ? clip.AirMipLevelCount : 0;
                passData.airMipLevelCount = levelCount;

                GraphicsBuffer b1 = clip.AirMipBuffer(1);
                int available = clip.AirMipLevelCount;
                passData.airMip1 = b1;
                passData.airMip2 = available >= 2 ? clip.AirMipBuffer(2) : b1;
                passData.airMip3 = available >= 3 ? clip.AirMipBuffer(3) : b1;
                passData.airMip4 = available >= 4 ? clip.AirMipBuffer(4) : b1;

                Unity.Mathematics.int3 g0 = clip.Mips.DimsOfLevel(1);
                Unity.Mathematics.int3 g1 = available >= 2 ? clip.Mips.DimsOfLevel(2) : g0;
                Unity.Mathematics.int3 g2 = available >= 3 ? clip.Mips.DimsOfLevel(3) : g0;
                Unity.Mathematics.int3 g3 = available >= 4 ? clip.Mips.DimsOfLevel(4) : g0;
                passData.airMipDims0 = new Vector3Int(g0.x, g0.y, g0.z);
                passData.airMipDims1 = new Vector3Int(g1.x, g1.y, g1.z);
                passData.airMipDims2 = new Vector3Int(g2.x, g2.y, g2.z);
                passData.airMipDims3 = new Vector3Int(g3.x, g3.y, g3.z);

                passData.airMipPacked = clip.AirMipPackedBuffer;
                var packed = clip.Packed;
                var info = new Vector4[4];
                for (int k = 0; k < 4; k++)
                {
                    if (packed != null && k < packed.NumLevels)
                    {
                        Unity.Mathematics.int3 d = packed.LevelDims[k];
                        info[k] = new Vector4(d.x, d.y, d.z, packed.WordOffsets[k]);
                    }
                    else info[k] = new Vector4(1, 1, 1, 0);
                }
                passData.mipInfo = info;

                var cascadeManager = LODCascadeManager.Active;
                bool cascadeAvailable = UseLODCascade && cascadeManager != null;
                passData.useLODCascade = cascadeAvailable ? 1 : 0;

                if (cascadeAvailable)
                {
                    var tier1 = cascadeManager.TierPool(1);
                    var tier2 = cascadeManager.TierPool(2);

                    passData.clipmapTier1 = tier1.ClipmapBuffer;
                    passData.brickDataTier1 = tier1.BrickDataBuffer;
                    Unity.Mathematics.int3 d1 = tier1.WindowDimsCoarseBricks;
                    passData.windowDimsCoarseTier1 = new Vector3Int(d1.x, d1.y, d1.z);
                    passData.cascadeTierInfo1 = new Vector4(
                        tier1.CoarseBricksPerChunkEdge,
                        Log2Int(tier1.CoarseBricksPerChunkEdge),
                        tier1.EntriesPerChunk, 0f);

                    passData.clipmapTier2 = tier2.ClipmapBuffer;
                    passData.brickDataTier2 = tier2.BrickDataBuffer;
                    Unity.Mathematics.int3 d2 = tier2.WindowDimsCoarseBricks;
                    passData.windowDimsCoarseTier2 = new Vector3Int(d2.x, d2.y, d2.z);
                    passData.cascadeTierInfo2 = new Vector4(
                        tier2.CoarseBricksPerChunkEdge,
                        Log2Int(tier2.CoarseBricksPerChunkEdge),
                        tier2.EntriesPerChunk, 0f);

                    float tier0Outer = LODConfig.TIER_OUTER_RANGE_M[0] * 10f;
                    float tier1Outer = LODConfig.TIER_OUTER_RANGE_M[1] * 10f;
                    float tier2Outer = LODConfig.TIER_OUTER_RANGE_M[2] * 10f;
                    passData.tierOuterRangeVoxels = new Vector4(tier0Outer, tier1Outer, tier2Outer, 0f);
                    passData.maxRayDistance = tier2Outer;
                }
                else
                {
                    passData.clipmapTier1 = clip.ClipmapBuffer;
                    passData.brickDataTier1 = clip.BrickDataBuffer;
                    passData.windowDimsCoarseTier1 = passData.windowDimsBricks;
                    passData.cascadeTierInfo1 = new Vector4(8, 3, 512, 0);
                    passData.clipmapTier2 = clip.ClipmapBuffer;
                    passData.brickDataTier2 = clip.BrickDataBuffer;
                    passData.windowDimsCoarseTier2 = passData.windowDimsBricks;
                    passData.cascadeTierInfo2 = new Vector4(4, 2, 64, 0);
                    passData.tierOuterRangeVoxels = Vector4.zero;
                    passData.maxRayDistance = MaxRayDistance;
                }

                builder.UseTexture(target, AccessFlags.Write);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc((ComputePassData data, ComputeGraphContext context) =>
                {
                    var cmd = context.cmd;

                    cmd.SetComputeMatrixParam(data.compute, "_CameraInverseProjection", data.cam.projectionMatrix.inverse);
                    cmd.SetComputeMatrixParam(data.compute, "_CameraInverseView", data.cam.cameraToWorldMatrix);
                    cmd.SetComputeVectorParam(data.compute, "_CameraPos", data.cam.transform.position);

                    cmd.SetComputeBufferParam(data.compute, 0, "ClipmapBuffer", data.clipmapBuffer);
                    cmd.SetComputeBufferParam(data.compute, 0, "BrickDataBuffer", data.brickDataBuffer);

                    cmd.SetComputeBufferParam(data.compute, 0, "AirMip1", data.airMip1);
                    cmd.SetComputeBufferParam(data.compute, 0, "AirMip2", data.airMip2);
                    cmd.SetComputeBufferParam(data.compute, 0, "AirMip3", data.airMip3);
                    cmd.SetComputeBufferParam(data.compute, 0, "AirMip4", data.airMip4);
                    cmd.SetComputeIntParams(data.compute, "_AirMipDims0",
                        new int[] { data.airMipDims0.x, data.airMipDims0.y, data.airMipDims0.z, 0 });
                    cmd.SetComputeIntParams(data.compute, "_AirMipDims1",
                        new int[] { data.airMipDims1.x, data.airMipDims1.y, data.airMipDims1.z, 0 });
                    cmd.SetComputeIntParams(data.compute, "_AirMipDims2",
                        new int[] { data.airMipDims2.x, data.airMipDims2.y, data.airMipDims2.z, 0 });
                    cmd.SetComputeIntParams(data.compute, "_AirMipDims3",
                        new int[] { data.airMipDims3.x, data.airMipDims3.y, data.airMipDims3.z, 0 });
                    cmd.SetComputeIntParam(data.compute, "_AirMipLevelCount", data.airMipLevelCount);

                    if (data.airMipPacked != null)
                        cmd.SetComputeBufferParam(data.compute, 0, "AirMipPacked", data.airMipPacked);
                    cmd.SetComputeVectorArrayParam(data.compute, "_MipInfo", data.mipInfo);
                    cmd.SetComputeIntParam(data.compute, "_UsePackedMips", data.usePackedMips);
                    cmd.SetComputeFloatParam(data.compute, "_MaxRayDistance", data.maxRayDistance);

                    cmd.SetComputeIntParams(data.compute, "_WindowDimsBricksPacked",
                        new int[] { data.windowDimsBricks.x, data.windowDimsBricks.y, data.windowDimsBricks.z, 0 });

                    // --- PHASE 4 window bindings. Set for EVERY kernel,
                    // including stripped/probe, because all three now index the
                    // clipmap chunk-major and would read garbage without them.
                    cmd.SetComputeIntParams(data.compute, "_WindowDimsChunksPacked",
                        new int[] { data.windowDimsChunks.x, data.windowDimsChunks.y, data.windowDimsChunks.z, 0 });
                    cmd.SetComputeIntParams(data.compute, "_WindowOriginBricksPacked",
                        new int[] { data.windowOriginBricks.x, data.windowOriginBricks.y, data.windowOriginBricks.z, 0 });
                    cmd.SetComputeIntParam(data.compute, "_ContentCeilingVoxelY", data.contentCeilingVoxelY);

                    cmd.SetComputeBufferParam(data.compute, 0, "ClipmapBufferTier1", data.clipmapTier1);
                    cmd.SetComputeBufferParam(data.compute, 0, "BrickDataBufferTier1", data.brickDataTier1);
                    cmd.SetComputeBufferParam(data.compute, 0, "ClipmapBufferTier2", data.clipmapTier2);
                    cmd.SetComputeBufferParam(data.compute, 0, "BrickDataBufferTier2", data.brickDataTier2);
                    cmd.SetComputeIntParams(data.compute, "_WindowDimsCoarseBricksTier1",
                        new int[] { data.windowDimsCoarseTier1.x, data.windowDimsCoarseTier1.y, data.windowDimsCoarseTier1.z, 0 });
                    cmd.SetComputeIntParams(data.compute, "_WindowDimsCoarseBricksTier2",
                        new int[] { data.windowDimsCoarseTier2.x, data.windowDimsCoarseTier2.y, data.windowDimsCoarseTier2.z, 0 });
                    cmd.SetComputeIntParams(data.compute, "_CascadeTierInfo1",
                        new int[] { (int)data.cascadeTierInfo1.x, (int)data.cascadeTierInfo1.y, (int)data.cascadeTierInfo1.z, 0 });
                    cmd.SetComputeIntParams(data.compute, "_CascadeTierInfo2",
                        new int[] { (int)data.cascadeTierInfo2.x, (int)data.cascadeTierInfo2.y, (int)data.cascadeTierInfo2.z, 0 });
                    cmd.SetComputeVectorParam(data.compute, "_TierOuterRangeVoxels", data.tierOuterRangeVoxels);
                    cmd.SetComputeIntParam(data.compute, "_UseLODCascade", data.useLODCascade);

                    if (!data.useStripped && !data.useMemoryProbe)
                    {
                        cmd.SetComputeBufferParam(data.compute, 0, "DebugOut", data.debugBuffer);
                        cmd.SetComputeIntParams(data.compute, "_DebugPixel",
                            new int[] { data.debugPixel.x, data.debugPixel.y });
                        cmd.SetComputeIntParams(data.compute, "_DebugModePacked",
                            new int[] { data.debugMode, 0, 0, 0 });
                        cmd.SetComputeIntParam(data.compute, "_TraversalMode", data.traversalMode);
                        cmd.SetComputeIntParam(data.compute, "_MaxOuterIterations", data.maxOuterIterations);
                        cmd.SetComputeIntParam(data.compute, "_UseDevColors", data.useDevColors);
                        cmd.SetComputeIntParam(data.compute, "_ShadeAO", data.shadeAO);
                        cmd.SetComputeIntParam(data.compute, "_ShadeHemi", data.shadeHemi);
                        cmd.SetComputeIntParam(data.compute, "_ShadeFog", data.shadeFog);
                        cmd.SetComputeVectorParam(data.compute, "_SkyColor",
                            new Vector4(data.skyColor.r, data.skyColor.g, data.skyColor.b, 1f));
                        cmd.SetComputeFloatParam(data.compute, "_FogStartM", data.fogStart);
                        cmd.SetComputeFloatParam(data.compute, "_FogEndM", data.fogEnd);

                        // ---- Phase 7 lighting. FULL KERNEL ONLY ----
                        // The stripped and memory-probe kernels do not declare
                        // any of this and never shade; binding to them would be
                        // a silent no-op at best. Keeping them out of this block
                        // is also what preserves them as clean isolation arms --
                        // Amendment 8.10 §5.3 measured the diagnostic kernels
                        // earning their keep and this must not change what they
                        // measure.
                        if (data.lightBuffer != null)
                            cmd.SetComputeBufferParam(data.compute, 0, "LightBuffer", data.lightBuffer);
                        cmd.SetComputeIntParam(data.compute, "_ActiveLightCount",
                            data.lightBuffer != null ? data.activeLightCount : 0);
                        cmd.SetComputeVectorParam(data.compute, "_SunDir", data.sunDir);
                        cmd.SetComputeVectorParam(data.compute, "_SunColor", data.sunColor);
                        cmd.SetComputeVectorParam(data.compute, "_AmbientColor", data.ambientColor);
                        cmd.SetComputeFloatParam(data.compute, "_KSoft", data.kSoft);
                        cmd.SetComputeIntParam(data.compute, "_ShadowQualityLevel", data.shadowQualityLevel);
                        cmd.SetComputeIntParam(data.compute, "_MaxShadowSteps", data.maxShadowSteps);
                        cmd.SetComputeIntParam(data.compute, "_PointLightNdotL", data.pointLightNdotL);
                        cmd.SetComputeIntParam(data.compute, "_UseSkyProbeAmbient", data.useSkyProbeAmbient);
                        cmd.SetComputeFloatParam(data.compute, "_UndergroundAmbientFloor",
                            data.undergroundAmbientFloor);

                        // Stage F's counters. Bound unconditionally (an unbound
                        // RWStructuredBuffer is undefined even when unread) but
                        // only WRITTEN when the flag is set.
                        if (data.shadowStats != null)
                            cmd.SetComputeBufferParam(data.compute, 0, "ShadowStats", data.shadowStats);
                        cmd.SetComputeIntParam(data.compute, "_CollectShadowStats", data.collectShadowStats);
                        cmd.SetComputeFloatParam(data.compute, "_ShadowMaxDistVoxels", data.shadowMaxDistVoxels);
                        cmd.SetComputeFloatParam(data.compute, "_Rung1DistVoxels", data.rung1DistVoxels);
                        cmd.SetComputeFloatParam(data.compute, "_ShadowTravelLOD", data.shadowTravelLod);
                        cmd.SetComputeIntParam(data.compute, "_MaterialVariation", data.materialVariation);
                        cmd.SetComputeFloatParam(data.compute, "_MaterialVariationAmount", data.materialVariationAmount);
                        cmd.SetComputeIntParam(data.compute, "_Tonemap", data.tonemap);
                        cmd.SetComputeFloatParam(data.compute, "_Exposure", data.exposure);
                        cmd.SetComputeFloatParam(data.compute, "_TonemapWhite", data.tonemapWhite);
                    }

                    if (!data.useMemoryProbe)
                        cmd.SetComputeBufferParam(data.compute, 0, "BrickDataBuffer", data.brickDataBuffer);

                    if (data.useMemoryProbe)
                        cmd.SetComputeIntParam(data.compute, "_ProbeIterations", data.probeIterations);

                    cmd.SetComputeTextureParam(data.compute, 0, "ResultTexture", data.target);

                    int threadGroupsX = Mathf.CeilToInt(data.dispatchWidth / 8f);
                    int threadGroupsY = Mathf.CeilToInt(data.dispatchHeight / 8f);
                    // NAMED for GPU capture. CommandBuffer.BeginSample emits a
                    // Metal debug group, so this shows up in an Instruments
                    // "Metal System Trace" as a named encoder rather than an
                    // anonymous dispatch. See tools/capture-gpu-trace.sh.
                    // The name must match the one parsed by
                    // tools/parse-gpu-trace.py.
                    cmd.BeginSample("VE.Raymarch.Primary");
                    cmd.DispatchCompute(data.compute, 0, threadGroupsX, threadGroupsY, 1);
                    cmd.EndSample("VE.Raymarch.Primary");
                });
            }

            using (var builder = renderGraph.AddRasterRenderPass<BlitPassData>("VoxelRaymarch_Blit", out var blitData))
            {
                blitData.source = target;
                builder.UseTexture(target, AccessFlags.Read);
                builder.SetRenderAttachment(activeColor, 0, AccessFlags.Write);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc((BlitPassData data, RasterGraphContext context) =>
                {
                    Blitter.BlitTexture(context.cmd, data.source, new Vector4(1, 1, 0, 0), 0, false);
                });
            }
        }
    }

    public ComputeShader raymarchShader;
    public ComputeShader raymarchShaderStripped;
    public ComputeShader raymarchShaderMemoryProbe;

    private RaymarchPass _pass;

    public override void Create()
    {
        _pass = new RaymarchPass(raymarchShader, raymarchShaderStripped, raymarchShaderMemoryProbe);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (raymarchShader != null)
        {
            int mode = UseDebugViewOverride ? (int)DebugViewOverride : (int)debugMode;
            _pass.SetDebugMode(mode);

            bool forceRes; int gw, gh;
            switch (GateModeOverride)
            {
                case GateResMode.Native: forceRes = false; gw = 0; gh = 0; break;
                case GateResMode.Forced960x540: forceRes = true; gw = 960; gh = 540; break;
                case GateResMode.ForcedCustom: forceRes = true; gw = CustomGateResolution.x; gh = CustomGateResolution.y; break;
                default: forceRes = _forceGateResolution; gw = _gateWidth; gh = _gateHeight; break;
            }
            _pass.SetGateResolution(forceRes, gw, gh);

            renderer.EnqueuePass(_pass);
        }
    }

    protected override void Dispose(bool disposing)
    {
        DebugBuffer?.Release();
        DebugBuffer = null;
        ShadowStatsBuffer?.Release();
        ShadowStatsBuffer = null;
        base.Dispose(disposing);
    }
}