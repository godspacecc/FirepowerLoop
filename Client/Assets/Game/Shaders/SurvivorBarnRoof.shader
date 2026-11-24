Shader "Game/Survivor/Barn Roof Visibility"
{
    Properties
    {
        [MainTexture] _BaseMap ("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor ("Base Color", Color) = (1,1,1,1)
        _Metallic ("Metallic", Range(0,1)) = 0
        _Smoothness ("Smoothness", Range(0,1)) = 0.5
        [HideInInspector] _RoofVisibilityMask ("Visibility Mask", 2D) = "black" {}
        [HideInInspector] _RoofVisibilityArea ("World XZ and Inverse Size", Vector) = (0,0,1,1)
        [HideInInspector] _RoofEyeHeight ("Eye Height", Float) = 0
        [HideInInspector] _RoofOpacity ("Visible Roof Opacity", Range(0,1)) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D(_RoofVisibilityMask);
        SAMPLER(sampler_RoofVisibilityMask);
        float4 _RoofVisibilityArea;
        float _RoofEyeHeight;
        half _RoofOpacity;

        half RoofVisibility(float2 worldXZ)
        {
            float2 uv = (worldXZ - _RoofVisibilityArea.xy) * _RoofVisibilityArea.zw;
            if (any(uv < 0) || any(uv > 1))
                return 0;
            return SAMPLE_TEXTURE2D(_RoofVisibilityMask, sampler_RoofVisibilityMask, uv).r;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForwardOnly" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex LitPassVertex
            #pragma fragment RoofFragment
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile_fog

            #define REQUIRES_WORLD_SPACE_POS_INTERPOLATOR
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitForwardPass.hlsl"

            half4 RoofFragment(Varyings input) : SV_Target
            {
                half4 color;
                LitPassFragment(input, color);
                // 镜头看到的屋面点投回眼高平面，使开口与下方可见物体对齐。
                float3 towardCamera = GetWorldSpaceNormalizeViewDir(input.positionWS);
                float2 eyePlane = input.positionWS.xz -
                    towardCamera.xz * ((input.positionWS.y - _RoofEyeHeight) / max(towardCamera.y, 0.001));
                color.a = lerp(1, _RoofOpacity, RoofVisibility(eyePlane));
                return color;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex RoofShadowVertex
            #pragma fragment RoofShadowFragment
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"

            struct RoofShadowVaryings
            {
                float4 positionCS : SV_POSITION;
                float2 worldXZ : TEXCOORD0;
            };

            RoofShadowVaryings RoofShadowVertex(Attributes input)
            {
                RoofShadowVaryings output;
                output.positionCS = GetShadowPositionHClip(input);
                output.worldXZ = TransformObjectToWorld(input.positionOS.xyz).xz;
                return output;
            }

            half4 RoofShadowFragment(RoofShadowVaryings input) : SV_Target
            {
                // 仅从投影中裁掉可见区域，未淡化的屋面继续挡光。
                clip(0.5 - RoofVisibility(input.worldXZ) * (1 - _RoofOpacity));
                return 0;
            }
            ENDHLSL
        }
    }
}
