using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

// Two RenderGraph-managed passes: a compute pass that writes `target` at a FIXED
// gate resolution, and a raster pass that upscales-blits it to the screen.
//
// Amendment 8.7/8.8 measurement infrastructure:
//  (1) RESOLUTION CLAMP: the compute target is forced to a fixed
//      _GateWidth x _GateHeight so the dispatch does exactly gateW*gateH
//      rays, not the Retina backing store's pixel count.
//  (2) The air-mip A/B toggle (AirMipEnabled).
//  (3) TraversalMode (0/1/2) - three-way A/B/C between the original LeapSpan,
//      LeapSpanReseed (Amendment 8.7 attempt #3), and LeapSpanReseed +
//      same-level chaining (Amendment 8.8 Phase B/C). Replaces the earlier
//      boolean UseReseedLeap now that there are three traversal variants to
//      compare, not two. Default 0 (original, unchanged behavior) so nothing
//      changes until explicitly flipped.
public class RaymarchFeature : ScriptableRendererFeature
{
    public enum DebugMode
    {
        Beauty = 0,
        StepHeat = 1,
        UniformDense = 2,
        Normals = 3,
        NonExitHeat = 4,
    }

    [Tooltip("Runtime debug view. Beauty is the shipped output; StepHeat is the §10.3 step-count heatmap.")]
    public DebugMode debugMode = DebugMode.Beauty;

    [Header("Gate measurement resolution")]
    [Tooltip("Force the compute dispatch to this exact ray resolution, independent " +
             "of the display's Retina backing store. The Phase 2 gate is defined at " +
             "1920x1080 ACTUAL rays. Untick to render at native camera resolution.")]
    [SerializeField] private bool _forceGateResolution = true;
    [SerializeField] private int _gateWidth = 960; //1920;
    [SerializeField] private int _gateHeight = 540;//1080;

    public static bool AirMipEnabled = true;

    // --- Amendment 8.7/8.8 traversal A/B/C toggle ---
    // 0 (default): original proven LeapSpan. Unchanged behavior from the
    //   correctness-complete state - nothing changes until this is flipped.
    // 1: LeapSpanReseed (Amendment 8.7 attempt #3). CPU-proven, GPU-confirmed
    //   (53.11ms -> 35.12ms cold, controlled A/B, Y=84 pose, half-res).
    // 2: LeapSpanReseed + same-level chaining (Amendment 8.8 Phase B/C).
    //   CPU-proven (RaymarchOccupancyTests, 11/11 green). NOT yet proven on
    //   Metal - this is how that gets measured.
    public static int TraversalMode = 0;

    // Diagnostic only (iteration-cap sweep). 400 = uncapped, matches the
    // shader's own hardcoded ceiling - default here changes nothing until
    // explicitly lowered by RaymarchDebugControls.
    public static int MaxOuterIterations = 400;

    // Diagnostic only (register-pressure A/B). When true and a stripped
    // kernel is assigned, dispatches RaymarchStripped.compute - mode-1-only,
    // Beauty-only, no debug branches - instead of the full Raymarch.compute.
    // Purpose: isolate whether the full kernel's always-compiled traversal-
    // mode and debug-view branches cause register spilling that shows up as
    // flat per-iteration cost regardless of which branch actually executes.
    // Default false - unchanged behavior until explicitly flipped AND a
    // stripped shader is assigned on the RaymarchFeature asset.
    public static bool UseStrippedKernel = false;

    // Diagnostic only (memory-latency isolation). Same priority note as
    // above: requires a shader assigned in the new slot below. Takes
    // priority over UseStrippedKernel if both are somehow true.
    public static bool UseMemoryProbeKernel = false;
    public static int ProbeIterations = 14; // matches the ~12-16 real converged iteration count measured this session

    public static DebugMode DebugViewOverride = DebugMode.Beauty;
    public static bool UseDebugViewOverride = false;

    // Actual ray resolution the compute dispatched at this frame (for the overlay).
    public static Vector2Int LastDispatchResolution = new Vector2Int(0, 0);

    public static GraphicsBuffer DebugBuffer { get; private set; }
    public static Vector2Int DebugPixel = new Vector2Int(700, 400);
    private const int DEBUG_BUFFER_FLOATS = 128;

    class RaymarchPass : ScriptableRenderPass
    {
        private ComputeShader _compute;
        private ComputeShader _computeStripped;
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
            public Vector3Int windowDimsBricks;
            public Vector2Int debugPixel;
            public int debugMode;
            public int traversalMode;
            public int maxOuterIterations;
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
        }

        class BlitPassData
        {
            public TextureHandle source;
        }

        private ComputeShader _computeMemoryProbe;

        public RaymarchPass(ComputeShader compute, ComputeShader computeStripped, ComputeShader computeMemoryProbe)
        {
            _compute = compute;
            _computeStripped = computeStripped;
            _computeMemoryProbe = computeMemoryProbe;
            renderPassEvent = RenderPassEvent.AfterRenderingSkybox;
        }

        public void SetDebugMode(int mode) => _debugMode = mode;
        public void SetGateResolution(bool force, int w, int h) { _forceRes = force; _gateW = w; _gateH = h; }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            bool useMemoryProbe = RaymarchFeature.UseMemoryProbeKernel && _computeMemoryProbe != null;
            bool useStripped = !useMemoryProbe && RaymarchFeature.UseStrippedKernel && _computeStripped != null;
            ComputeShader activeCompute = useMemoryProbe ? _computeMemoryProbe : (useStripped ? _computeStripped : _compute);
            if (activeCompute == null || TerrainClipmap.Active == null) return;

            if (DebugBuffer == null)
            {
                DebugBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, DEBUG_BUFFER_FLOATS, sizeof(float));
            }

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
                passData.debugPixel = DebugPixel;
                passData.debugMode = _debugMode;
                passData.traversalMode = TraversalMode;
                passData.maxOuterIterations = MaxOuterIterations;

                Unity.Mathematics.int3 dims = clip.WindowDimsBricks;
                passData.windowDimsBricks = new Vector3Int(dims.x, dims.y, dims.z);

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

                    cmd.SetComputeIntParams(data.compute, "_WindowDimsBricksPacked",
                        new int[] { data.windowDimsBricks.x, data.windowDimsBricks.y, data.windowDimsBricks.z, 0 });

                    // Debug-only / mode-only uniforms - neither sibling kernel
                    // declares these resources, so they must never be set on
                    // them (Unity errors if you name a resource the active
                    // kernel doesn't have).
                    if (!data.useStripped && !data.useMemoryProbe)
                    {
                        cmd.SetComputeBufferParam(data.compute, 0, "DebugOut", data.debugBuffer);
                        cmd.SetComputeIntParams(data.compute, "_DebugPixel",
                            new int[] { data.debugPixel.x, data.debugPixel.y });
                        cmd.SetComputeIntParams(data.compute, "_DebugModePacked",
                            new int[] { data.debugMode, 0, 0, 0 });
                        cmd.SetComputeIntParam(data.compute, "_TraversalMode", data.traversalMode);
                        cmd.SetComputeIntParam(data.compute, "_MaxOuterIterations", data.maxOuterIterations);
                    }

                    // BrickDataBuffer isn't declared by the memory probe (it only
                    // exercises AirMip + Clipmap reads).
                    if (!data.useMemoryProbe)
                        cmd.SetComputeBufferParam(data.compute, 0, "BrickDataBuffer", data.brickDataBuffer);

                    if (data.useMemoryProbe)
                        cmd.SetComputeIntParam(data.compute, "_ProbeIterations", data.probeIterations);

                    cmd.SetComputeTextureParam(data.compute, 0, "ResultTexture", data.target);

                    int threadGroupsX = Mathf.CeilToInt(data.dispatchWidth / 8f);
                    int threadGroupsY = Mathf.CeilToInt(data.dispatchHeight / 8f);
                    cmd.DispatchCompute(data.compute, 0, threadGroupsX, threadGroupsY, 1);
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

    [Tooltip("Diagnostic only: register-pressure A/B sibling kernel (mode-1-only, " +
             "Beauty-only). Assign RaymarchStripped.compute here to enable the " +
             "UseStrippedKernel toggle; leave unassigned to disable it entirely.")]
    public ComputeShader raymarchShaderStripped;

    [Tooltip("Diagnostic only: memory-latency isolation probe (same dependent-read " +
             "shape, no traversal math). Assign RaymarchMemoryProbe.compute here.")]
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
            _pass.SetGateResolution(_forceGateResolution, _gateWidth, _gateHeight);
            renderer.EnqueuePass(_pass);
        }
    }

    protected override void Dispose(bool disposing)
    {
        DebugBuffer?.Release();
        DebugBuffer = null;
        base.Dispose(disposing);
    }
}