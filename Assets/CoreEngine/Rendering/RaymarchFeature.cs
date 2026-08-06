using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

// Two RenderGraph-managed passes: a compute pass that writes `target`, and a
// raster pass that reads `target` and blits it to the screen. The graph inserts
// the correct barrier between them from the declared UseTexture access flags.
//
// NOTE (this session): the original center-screen artifact was attributed to a
// missing barrier on the strength of a CPU sibling that "found zero anomalies"
// across 6561 pixels. That sibling had NO macro-skip path, so it could not have
// exercised the macro-skip the shader's MIN_PROGRESS guard exists to protect.
// A from-spec CPU macro-skip sibling now exists and passes 7/7 - meaning the
// leap ALGORITHM is correct and the tear's true cause is not yet settled.
// _DebugMode (StepHeat) below is the instrument to localize it: set it to 1
// and read whether the tear pixels are high-step-count (grinding) or not.
public class RaymarchFeature : ScriptableRendererFeature
{
    // Debug view selector, mirrors the branch in Raymarch.compute.
    //   Beauty = shipped shaded output; the rest are diagnostic views over the
    //   SAME traversal, so they localize bugs without changing the algorithm.
    public enum DebugMode
    {
        Beauty = 0,
        StepHeat = 1,
        UniformDense = 2,
        Normals = 3,
    }

    [Tooltip("Runtime debug view. Beauty is the shipped output; StepHeat is the §10.3 step-count heatmap.")]
    public DebugMode debugMode = DebugMode.Beauty;

    public static GraphicsBuffer DebugBuffer { get; private set; }
    public static Vector2Int DebugPixel = new Vector2Int(1400, 240);
    private const int DEBUG_BUFFER_FLOATS = 128;

    class RaymarchPass : ScriptableRenderPass
    {
        private ComputeShader _compute;
        private int _debugMode;

        class ComputePassData
        {
            public ComputeShader compute;
            public Camera cam;
            public TextureHandle target;
            public GraphicsBuffer clipmapBuffer;
            public GraphicsBuffer brickDataBuffer;
            public GraphicsBuffer debugBuffer;
            public Vector3Int windowDimsBricks;
            public Vector2Int debugPixel;
            public int debugMode;
        }

        class BlitPassData
        {
            public TextureHandle source;
        }

        public RaymarchPass(ComputeShader compute)
        {
            _compute = compute;
            renderPassEvent = RenderPassEvent.AfterRenderingSkybox;
        }

        // Called each frame from AddRenderPasses so the inspector value is live.
        public void SetDebugMode(int mode) => _debugMode = mode;

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (_compute == null || TerrainClipmap.Active == null) return;

            if (DebugBuffer == null)
            {
                DebugBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, DEBUG_BUFFER_FLOATS, sizeof(float));
            }

            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            TextureHandle activeColor = resourceData.activeColorTexture;

            RenderTextureDescriptor desc = cameraData.cameraTargetDescriptor;
            desc.enableRandomWrite = true;
            desc.depthBufferBits = 0;
            desc.msaaSamples = 1; // Metal rejects RWTexture2D binding if MSAA > 1
            desc.useMipMap = false;

            TextureHandle target = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, "_RaymarchTarget", false);

            // --- Pass 1: Compute dispatch, writes `target`. ---
            using (var builder = renderGraph.AddComputePass<ComputePassData>("VoxelRaymarch_Compute", out var passData))
            {
                passData.compute = _compute;
                passData.cam = cameraData.camera;
                passData.target = target;
                passData.clipmapBuffer = TerrainClipmap.Active.ClipmapBuffer;
                passData.brickDataBuffer = TerrainClipmap.Active.BrickDataBuffer;
                passData.debugBuffer = DebugBuffer;
                passData.debugPixel = DebugPixel;
                passData.debugMode = _debugMode;

                Unity.Mathematics.int3 dims = TerrainClipmap.Active.WindowDimsBricks;
                passData.windowDimsBricks = new Vector3Int(dims.x, dims.y, dims.z);

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
                    cmd.SetComputeBufferParam(data.compute, 0, "DebugOut", data.debugBuffer);

                    cmd.SetComputeIntParams(data.compute, "_WindowDimsBricksPacked",
                        new int[] { data.windowDimsBricks.x, data.windowDimsBricks.y, data.windowDimsBricks.z, 0 });
                    cmd.SetComputeIntParams(data.compute, "_DebugPixel",
                        new int[] { data.debugPixel.x, data.debugPixel.y });
                    cmd.SetComputeIntParams(data.compute, "_DebugModePacked",
                        new int[] { data.debugMode, 0, 0, 0 });

                    cmd.SetComputeTextureParam(data.compute, 0, "ResultTexture", data.target);

                    int threadGroupsX = Mathf.CeilToInt(data.cam.pixelWidth / 8f);
                    int threadGroupsY = Mathf.CeilToInt(data.cam.pixelHeight / 8f);
                    cmd.DispatchCompute(data.compute, 0, threadGroupsX, threadGroupsY, 1);
                });
            }

            // --- Pass 2: Raster pass, blits `target` -> activeColor. ---
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
    private RaymarchPass _pass;

    public override void Create()
    {
        _pass = new RaymarchPass(raymarchShader);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (raymarchShader != null)
        {
            _pass.SetDebugMode((int)debugMode);
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