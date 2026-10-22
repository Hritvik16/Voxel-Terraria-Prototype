// VoxelUpscaleDepthWrite.shader -- TASK 47. Copies VoxelUpscaleInputs' device
// depth (an R32 colour texture) into URP's _CameraDepthTexture when URP made
// that texture a DEPTH format (it does with a depth prepass). Without a
// prepass it is R32_SFloat and a plain Blitter copy is used instead.
Shader "Hidden/VoxelEngine/UpscaleDepthWrite"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            ZWrite On ZTest Always Cull Off ColorMask 0
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            void Frag(Varyings input, out float depth : SV_Depth)
            {
                depth = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, input.texcoord.xy, 0).r;
            }
            ENDHLSL
        }
    }
}
