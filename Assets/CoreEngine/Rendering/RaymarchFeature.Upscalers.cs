// Assets/CoreEngine/Rendering/RaymarchFeature.Upscalers.cs
//
// TASK 47 (engine plan step 3; owner Q4, 2026-10-09: "are there no third party
// or unity native tools we can use"). Ready-made TEMPORAL upscalers fed by the
// compute raymarch. Neither is ours; this file only produces their inputs:
//
//   Stp   URP 17.3's Spatial-Temporal Post-processing. URP runs it in its post
//         chain from the camera's render-scaled target; it reads
//         _CameraDepthTexture and _MotionVectorTexture, which a compute
//         raymarch never writes. StpInputsPass (BeforeRenderingPostProcessing,
//         after URP's own depth copy and motion-vector pass) overwrites both
//         with ours. Rays use URP's jittered projection (UseCameraDataProjection)
//         so the samples carry the jitter STP expects.
//   Fsr3  AMD FidelityFX Super Resolution 3.1 upscaler, the MIT Unity port
//         vendored in Assets/ThirdParty/FidelityFX.FSR (see its README). The
//         gate renders at the internal size; this file dispatches FSR 3 to the
//         camera target's size and the usual blit copies that 1:1. The rays
//         use FSR's own jitter sequence through _TaaJitter.
//
// Depth and motion come from TAA's existing buffers (TaaCur, TaaMetaW; task
// 06) via VoxelUpscaleInputs.compute. Our TAA RESOLVE is skipped in both
// modes: the upscaler is the temporal filter. Off (the default) changes
// nothing: no pass, no buffer, no uniform.
using FidelityFX;
using FidelityFX.FSR3;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

public partial class RaymarchFeature
{
    public enum UpscalerInputMode { Off, Stp, Fsr3 }

    /// Set by the game's upscaler selection (Assets/Game/UpscalerSelect.cs).
    public static UpscalerInputMode UpscaleInputs = UpscalerInputMode.Off;
    /// FSR 3 context's largest render size; (0,0) = the current gate size.
    /// A dynamic resolution sets it to the top of its range so a row change
    /// does not rebuild the context.
    public static Vector2Int Fsr3MaxRenderSize = Vector2Int.zero;
    /// FSR 3's RCAS sharpening: the port's own defaults (on, 0.8).
    public static bool Fsr3Sharpen = true;
    public static float Fsr3Sharpness = 0.8f;
    /// For logs: did the FSR 3 shaders load and the context build?
    public static string Fsr3Status = "not used";
    /// For logs: the format URP gave _CameraDepthTexture (STP path).
    public static string StpDepthTargetNote = "not used";

    private static ComputeShader _upsCs;
    private static int _upsKernel = -1;
    private static Material _upsDepthMat;
    private static Fsr3UpscalerContext _fsr;
    private static Vector2Int _fsrDisplay, _fsrMax;
    private static Fsr3Upscaler.InitializationFlags _fsrFlags;
    private static bool _fsrFailed;
    private static readonly Fsr3Upscaler.DispatchDescription _fsrDesc = new Fsr3Upscaler.DispatchDescription();
    private static Vector2 _fsrJitter;
    private StpInputsPass _stpInputsPass;

    public class UpsComputeData
    {
        public TextureHandle motion, depth;
        public Matrix4x4 invProj;
        public Vector4 size, jitter, depthParams;
    }

    /// Per-frame hand-over from the raymarch pass to the STP / FSR 3 passes.
    public class UpsFrameData : ContextItem
    {
        public TextureHandle motion, depth;
        public bool reset;
        public override void Reset() { motion = TextureHandle.nullHandle; depth = TextureHandle.nullHandle; reset = false; }
    }

    /// This frame's ray jitter in gate pixels (CSMain's _TaaJitter).
    private static Vector2 UpsJitter(uint frame, int w, int h, UniversalCameraData cameraData)
    {
        if (UpscaleInputs == UpscalerInputMode.Fsr3)
        {
            int displayW = cameraData.cameraTargetDescriptor.width;
            int phases = Fsr3Upscaler.GetJitterPhaseCount(w, displayW);
            Fsr3Upscaler.GetJitterOffset(out float jx, out float jy, (int)frame, phases);
            _fsrJitter = new Vector2(jx, jy);
            // FSR's offset is a projection translation of +j pixels; the same
            // image is a ray through pixel centre - j (VoxelUpscaleInputs.compute).
            return new Vector2(-jx, -jy);
        }
        return Vector2.zero;    // STP: URP's projection carries the jitter
    }

    private static UpsComputeData UpsPrepare(RenderGraph renderGraph, ContextContainer frameData, int w, int h, bool reset)
    {
        if (_upsCs == null)
        {
            _upsCs = Resources.Load<ComputeShader>("VoxelUpscaleInputs");
            _upsKernel = _upsCs != null ? _upsCs.FindKernel("UpscaleInputs") : -1;
            if (_upsCs == null) Debug.LogError("[Upscalers] Resources/VoxelUpscaleInputs.compute missing");
        }
        if (_upsCs == null) return null;
        var cameraData = frameData.Get<UniversalCameraData>();
        var cam = cameraData.camera;
        var d = new UpsComputeData();
        var md = new RenderTextureDescriptor(w, h, GraphicsFormat.R16G16_SFloat, 0) { enableRandomWrite = true, msaaSamples = 1 };
        d.motion = UniversalRenderer.CreateRenderGraphTexture(renderGraph, md, "_VoxelUpsMotion", false);
        var dd = new RenderTextureDescriptor(w, h, GraphicsFormat.R32_SFloat, 0) { enableRandomWrite = true, msaaSamples = 1 };
        d.depth = UniversalRenderer.CreateRenderGraphTexture(renderGraph, dd, "_VoxelUpsDepth", false);
        Matrix4x4 rayProj = UseCameraDataProjection ? cameraData.GetProjectionMatrix(0) : cam.projectionMatrix;
        d.invProj = rayProj.inverse;
        d.size = new Vector4(w, h, 0, 0);
        // Motion offset: with URP's jittered projection a ray through pixel
        // centre c hits the point whose UNjittered position is c - t (t = URP's
        // jitter translation, pixels). URP's motion vectors exclude jitter.
        Vector2 mvOffset = Vector2.zero;
        if (UseCameraDataProjection)
        {
            Matrix4x4 pu = cam.projectionMatrix;
            // (T * P).m02 = P.m02 - t.x for a perspective P (row 3 = 0,0,-1,0).
            mvOffset = new Vector2(-(pu.m02 - rayProj.m02) * w * 0.5f, -(pu.m12 - rayProj.m12) * h * 0.5f);
        }
        d.jitter = new Vector4(LastTaaJitter.x, LastTaaJitter.y, mvOffset.x, mvOffset.y);
        d.depthParams = new Vector4(cam.nearClipPlane, cam.farClipPlane, SystemInfo.usesReversedZBuffer ? 1f : 0f, 0f);
        var fd = frameData.Contains<UpsFrameData>() ? frameData.Get<UpsFrameData>() : frameData.Create<UpsFrameData>();
        fd.motion = d.motion; fd.depth = d.depth; fd.reset = reset;
        return d;
    }

    private static void UpsDispatch(ComputeCommandBuffer cmd, UpsComputeData u, GraphicsBuffer taaCur, GraphicsBuffer taaMeta)
    {
        cmd.BeginSample("VE.Raymarch.UpscaleInputs");
        cmd.SetComputeBufferParam(_upsCs, _upsKernel, "UpsTaaCur", taaCur);
        cmd.SetComputeBufferParam(_upsCs, _upsKernel, "UpsTaaMeta", taaMeta);
        cmd.SetComputeTextureParam(_upsCs, _upsKernel, "UpsMotion", u.motion);
        cmd.SetComputeTextureParam(_upsCs, _upsKernel, "UpsDepth", u.depth);
        cmd.SetComputeMatrixParam(_upsCs, "_UpsInvProj", u.invProj);
        cmd.SetComputeVectorParam(_upsCs, "_UpsSize", u.size);
        cmd.SetComputeVectorParam(_upsCs, "_UpsJitter", u.jitter);
        cmd.SetComputeVectorParam(_upsCs, "_UpsDepth", u.depthParams);
        cmd.DispatchCompute(_upsCs, _upsKernel, ((int)u.size.x + 7) / 8, ((int)u.size.y + 7) / 8, 1);
        cmd.EndSample("VE.Raymarch.UpscaleInputs");
    }

    private static bool EnsureFsr3(Vector2Int display, Vector2Int render)
    {
        if (_fsrFailed) return false;
        Vector2Int max = new Vector2Int(Mathf.Max(render.x, Fsr3MaxRenderSize.x), Mathf.Max(render.y, Fsr3MaxRenderSize.y));
        var flags = Fsr3MaxRenderSize != Vector2Int.zero ? Fsr3Upscaler.InitializationFlags.EnableDynamicResolution : 0;
        if (_fsr != null && display == _fsrDisplay && max == _fsrMax && flags == _fsrFlags) return true;
        _fsr?.Destroy(); _fsr = null;
        ComputeShader L(string n) => Resources.Load<ComputeShader>("FidelityFX/Shaders/" + n);
        var shaders = new Fsr3UpscalerShaders
        {
            prepareInputsPass = L("ffx_fsr3upscaler_prepare_inputs_pass"),
            lumaPyramidPass = L("ffx_fsr3upscaler_luma_pyramid_pass"),
            shadingChangePyramidPass = L("ffx_fsr3upscaler_shading_change_pyramid_pass"),
            shadingChangePass = L("ffx_fsr3upscaler_shading_change_pass"),
            prepareReactivityPass = L("ffx_fsr3upscaler_prepare_reactivity_pass"),
            lumaInstabilityPass = L("ffx_fsr3upscaler_luma_instability_pass"),
            accumulatePass = L("ffx_fsr3upscaler_accumulate_pass"),
            sharpenPass = L("ffx_fsr3upscaler_rcas_pass"),
            autoGenReactivePass = L("ffx_fsr3upscaler_autogen_reactive_pass"),
            tcrAutoGenPass = L("ffx_fsr3upscaler_tcr_autogen_pass"),
            debugViewPass = L("ffx_fsr3upscaler_debug_view_pass"),
        };
        if (shaders.prepareInputsPass == null || shaders.accumulatePass == null || shaders.sharpenPass == null
            || shaders.lumaPyramidPass == null || shaders.shadingChangePass == null)
        {
            _fsrFailed = true;
            Fsr3Status = "FAILED: FSR 3 compute shaders not found under Resources/FidelityFX/Shaders";
            Debug.LogError("[Upscalers] " + Fsr3Status);
            return false;
        }
        _fsr = Fsr3Upscaler.CreateContext(display, max, shaders, flags);
        _fsrDisplay = display; _fsrMax = max; _fsrFlags = flags;
        Fsr3Status = $"context {max.x}x{max.y} -> {display.x}x{display.y}, flags {flags}" +
                     $"{(SystemInfo.usesReversedZBuffer ? " +DepthInverted" : "")}";
        Debug.Log("[Upscalers] FSR 3 " + Fsr3Status);
        return true;
    }

    class Fsr3PassData
    {
        public TextureHandle color, depth, motion, output;
        public Vector2Int render, display;
        public bool reset;
        public float near, far, fovRad;
    }

    private static TextureHandle UpsRecordFsr3(RenderGraph renderGraph, ContextContainer frameData, TextureHandle color,
                                               UniversalCameraData cameraData)
    {
        if (!frameData.Contains<UpsFrameData>()) return color;
        var fd = frameData.Get<UpsFrameData>();
        if (!fd.motion.IsValid()) return color;
        var ctd = cameraData.cameraTargetDescriptor;
        var display = new Vector2Int(ctd.width, ctd.height);
        var render = LastDispatchResolution;
        if (!EnsureFsr3(display, render)) return color;

        var od = ctd;
        od.enableRandomWrite = true; od.depthBufferBits = 0; od.msaaSamples = 1; od.useMipMap = false;
        TextureHandle output = UniversalRenderer.CreateRenderGraphTexture(renderGraph, od, "_VoxelFsr3Output", false);
        using (var builder = renderGraph.AddUnsafePass<Fsr3PassData>("VoxelRaymarch_FSR3", out var p))
        {
            var cam = cameraData.camera;
            p.color = color; p.depth = fd.depth; p.motion = fd.motion; p.output = output;
            p.render = render; p.display = display; p.reset = fd.reset;
            p.near = cam.nearClipPlane; p.far = cam.farClipPlane; p.fovRad = cam.fieldOfView * Mathf.Deg2Rad;
            builder.UseTexture(color, AccessFlags.Read);
            builder.UseTexture(fd.depth, AccessFlags.Read);
            builder.UseTexture(fd.motion, AccessFlags.Read);
            builder.UseTexture(output, AccessFlags.Write);
            builder.AllowPassCulling(false);
            builder.SetRenderFunc((Fsr3PassData data, UnsafeGraphContext ctx) =>
            {
                var cmd = CommandBufferHelpers.GetNativeCommandBuffer(ctx.cmd);
                var d = _fsrDesc;
                d.Color = new ResourceView(data.color);
                d.Depth = new ResourceView(data.depth);
                d.MotionVectors = new ResourceView(data.motion);
                d.Exposure = ResourceView.Unassigned;
                d.Reactive = ResourceView.Unassigned;
                d.TransparencyAndComposition = ResourceView.Unassigned;
                d.Output = new ResourceView(data.output);
                d.JitterOffset = _fsrJitter;
                // Our motion is URP's convention (current - previous, UV units);
                // FSR wants previous - current in render pixels.
                d.MotionVectorScale = new Vector2(-data.render.x, -data.render.y);
                d.RenderSize = data.render;
                d.UpscaleSize = data.display;
                d.EnableSharpening = Fsr3Sharpen;
                d.Sharpness = Fsr3Sharpness;
                d.FrameTimeDelta = Time.unscaledDeltaTime;
                d.PreExposure = 1f;
                d.Reset = data.reset;
                d.CameraNear = data.near; d.CameraFar = data.far;
                // FSR expects near and far swapped with inverted depth (the port's own image effect does this).
                if (SystemInfo.usesReversedZBuffer) (d.CameraNear, d.CameraFar) = (d.CameraFar, d.CameraNear);
                d.CameraFovAngleVertical = data.fovRad;
                d.ViewSpaceToMetersFactor = 1f;
                d.Flags = 0;
                _fsr.Dispatch(d, cmd);
            });
        }
        return output;
    }

    private static void UpsDispose()
    {
        _fsr?.Destroy(); _fsr = null;
        _fsrDisplay = _fsrMax = Vector2Int.zero;
    }

    /// Writes our depth and motion over URP's _CameraDepthTexture and
    /// _MotionVectorTexture, after URP's copies and before its post chain (STP).
    class StpInputsPass : ScriptableRenderPass
    {
        class PassData { public TextureHandle source; public Material mat; }

        public StpInputsPass() { renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing; }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (!frameData.Contains<UpsFrameData>()) return;
            var fd = frameData.Get<UpsFrameData>();
            if (!fd.motion.IsValid()) return;
            var res = frameData.Get<UniversalResourceData>();

            if (res.motionVectorColor.IsValid())
            {
                using (var b = renderGraph.AddRasterRenderPass<PassData>("VoxelRaymarch_StpMotion", out var p))
                {
                    p.source = fd.motion;
                    b.UseTexture(fd.motion, AccessFlags.Read);
                    b.SetRenderAttachment(res.motionVectorColor, 0, AccessFlags.Write);
                    b.AllowPassCulling(false);
                    b.SetRenderFunc((PassData d, RasterGraphContext c) =>
                        Blitter.BlitTexture(c.cmd, d.source, new Vector4(1, 1, 0, 0), 0, false));
                }
            }
            else StpDepthTargetNote = "URP gave no _MotionVectorTexture";

            TextureHandle dt = res.cameraDepthTexture;
            if (!dt.IsValid()) { StpDepthTargetNote = "URP gave no _CameraDepthTexture"; return; }
            var desc = renderGraph.GetTextureDesc(dt);
            bool isDepth = GraphicsFormatUtility.IsDepthFormat(desc.format);
            if (isDepth && _upsDepthMat == null)
            {
                var sh = Resources.Load<Shader>("VoxelUpscaleDepthWrite");
                if (sh != null) _upsDepthMat = CoreUtils.CreateEngineMaterial(sh);
            }
            StpDepthTargetNote = $"_CameraDepthTexture {desc.format} ({(isDepth ? "SV_Depth write" : "colour copy")})";
            if (isDepth && _upsDepthMat == null) { StpDepthTargetNote += " -- depth shader MISSING"; return; }
            using (var b = renderGraph.AddRasterRenderPass<PassData>("VoxelRaymarch_StpDepth", out var p))
            {
                p.source = fd.depth; p.mat = isDepth ? _upsDepthMat : null;
                b.UseTexture(fd.depth, AccessFlags.Read);
                if (isDepth) b.SetRenderAttachmentDepth(dt, AccessFlags.Write);
                else b.SetRenderAttachment(dt, 0, AccessFlags.Write);
                b.AllowPassCulling(false);
                b.SetRenderFunc((PassData d, RasterGraphContext c) =>
                {
                    if (d.mat != null) Blitter.BlitTexture(c.cmd, d.source, new Vector4(1, 1, 0, 0), d.mat, 0);
                    else Blitter.BlitTexture(c.cmd, d.source, new Vector4(1, 1, 0, 0), 0, false);
                });
            }
        }
    }
}
