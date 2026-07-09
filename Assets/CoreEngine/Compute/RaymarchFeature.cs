using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using CoreEngine.Memory;

namespace CoreEngine.Compute
{
    public class RaymarchFeature : ScriptableRendererFeature
    {
        class RaymarchPass : ScriptableRenderPass
        {
            public ComputeShader raymarchShader;

            // Data container for the Render Graph's Unsafe Pass
            private class PassData
            {
                public ComputeShader computeShader;
                public VRAMAllocator vramAllocator;
                public MaterialRegistry materialRegistry;
                public Camera camera;
                public TextureHandle computeOutput;
                public TextureHandle cameraColorTarget;
                public int dispatchWidth, dispatchHeight;
            }

            public RaymarchPass(ComputeShader shader)
            {
                this.raymarchShader = shader;
                this.renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                if (raymarchShader == null) return;

                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();

                // --- THE SCENE VIEW FIX ---
                // Allow both the Game View AND the Scene View to render the voxels.
                // We only abort for tiny preview windows or reflection probes.
                if (cameraData.cameraType == CameraType.Preview || cameraData.cameraType == CameraType.Reflection) return;

                int width = cameraData.cameraTargetDescriptor.width;
                int height = cameraData.cameraTargetDescriptor.height;

                // 1. Describe the Texture for the Render Graph
                TextureDesc texDesc = new TextureDesc(width, height);
                texDesc.format = UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat; // Metal loves SFloat
                texDesc.enableRandomWrite = true;
                texDesc.name = "VoxelRaymarchOutput";

                // 2. Allocate the Texture via the Graph
                TextureHandle computeOutput = renderGraph.CreateTexture(texDesc);

                // 3. Build the Unsafe Pass
                using (var builder = renderGraph.AddUnsafePass<PassData>("Voxel Raymarch Pass", out var passData))
                {
                    passData.computeShader = raymarchShader;
                    passData.camera = cameraData.camera;
                    passData.computeOutput = computeOutput;
                    passData.cameraColorTarget = resourceData.activeColorTexture;
                    passData.dispatchWidth = width;
                    passData.dispatchHeight = height;

                    // Locate our static memory pools (In a full production engine, we'd pass these via dependency injection)
                    passData.vramAllocator = Object.FindFirstObjectByType<VRAMAllocator>();
                    passData.materialRegistry = Object.FindFirstObjectByType<MaterialRegistry>();

                    // Inform the Render Graph that we intend to WRITE to both of these textures
                    builder.UseTexture(computeOutput, AccessFlags.Write);
                    builder.UseTexture(passData.cameraColorTarget, AccessFlags.Write);

                    // 4. The actual Execution function
                    builder.SetRenderFunc((PassData data, UnsafeGraphContext context) =>
                    {
                        if (data.vramAllocator == null || data.vramAllocator.StaticBrickPool == null ||
                            data.materialRegistry == null || data.materialRegistry.MaterialPaletteBuffer == null) return;

                        CommandBuffer cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                        int kernel = data.computeShader.FindKernel("CSRaymarch");

                        // Bind Matrices
                        cmd.SetComputeMatrixParam(data.computeShader, "_CameraInverseProjection", data.camera.projectionMatrix.inverse);
                        cmd.SetComputeMatrixParam(data.computeShader, "_CameraInverseView", data.camera.cameraToWorldMatrix);
                        cmd.SetComputeVectorParam(data.computeShader, "_CameraPosition", data.camera.transform.position);
                        cmd.SetComputeVectorParam(data.computeShader, "_ScreenResolution", new Vector2(data.dispatchWidth, data.dispatchHeight));

                        // Bind Data Pools & Output Texture
                        cmd.SetComputeBufferParam(data.computeShader, kernel, "StaticBrickPool", data.vramAllocator.StaticBrickPool);
                        cmd.SetComputeBufferParam(data.computeShader, kernel, "MaterialPalette", data.materialRegistry.MaterialPaletteBuffer);
                        cmd.SetComputeTextureParam(data.computeShader, kernel, "Result", data.computeOutput);

                        // Dispatch Compute
                        cmd.DispatchCompute(data.computeShader, kernel, Mathf.CeilToInt(data.dispatchWidth / 8.0f), Mathf.CeilToInt(data.dispatchHeight / 8.0f), 1);

                        // Blit directly to the camera target using the modern Blitter API
                        Blitter.BlitCameraTexture(cmd, data.computeOutput, data.cameraColorTarget);
                    });
                }
            }
        }

        [Header("Compute References")]
        public ComputeShader RaymarchShader;
        private RaymarchPass raymarchPass;

        public override void Create()
        {
            raymarchPass = new RaymarchPass(RaymarchShader);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            renderer.EnqueuePass(raymarchPass);
        }
    }
}