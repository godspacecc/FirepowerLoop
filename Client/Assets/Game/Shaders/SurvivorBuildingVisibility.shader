Shader "Game/Survivor/Building Visibility"
{
    Properties
    {
        [MainTexture] _BaseMap ("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor ("Base Color", Color) = (1,1,1,1)
        _Metallic ("Metallic", Range(0,1)) = 0
        _Smoothness ("Smoothness", Range(0,1)) = 0.5
        [HideInInspector] _BuildingSight ("Shared Sight Distances", 2D) = "black" {}
        [HideInInspector] _BuildingSightOrigin ("Eye XZ, Radius and Height", Vector) = (0,0,0,0)
        [HideInInspector] _BuildingSurfaceParameters ("Interior Projection and Opacity", Vector) = (0,1,0,0)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D(_BuildingSight);
        SAMPLER(sampler_BuildingSight);
        float4 _BuildingSightOrigin;
        float4 _BuildingSurfaceParameters;

        half VisibleBuildingSurface(float2 worldXZ, bool interior)
        {
            float radius = _BuildingSightOrigin.z;
            float2 offset = worldXZ - _BuildingSightOrigin.xy;
            float distanceSquared = dot(offset, offset);
            if (distanceSquared > radius * radius)
                return 0;
            if (distanceSquared < 0.000001)
                return 1;
            float angle = frac(atan2(offset.y, offset.x) * 0.1591549431);
            float4 encoded = SAMPLE_TEXTURE2D_LOD(_BuildingSight, sampler_BuildingSight, float2(angle, 0.5), 0);
            float2 firstObstacle = float2(dot(encoded.rg, float2(65280, 255)),
                dot(encoded.ba, float2(65280, 255))) * (radius / 65535.0);
            // 实体表面允许淡出第一个阻挡体的厚度；屋顶只开到该阻挡体之前。
            float limit = interior ? firstObstacle.x : firstObstacle.y + 0.035;
            return sqrt(distanceSquared) <= limit ? 1 : 0;
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
            #pragma fragment BuildingFragment
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile_fog

            #define REQUIRES_WORLD_SPACE_POS_INTERPOLATOR
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitForwardPass.hlsl"

            half4 BuildingFragment(Varyings input) : SV_Target
            {
                half4 color;
                LitPassFragment(input, color);
                bool interior = _BuildingSurfaceParameters.x > 0.5;
                // 所有表面都检查它在镜头中盖住的位置，避免透明后露出仍不可见的房间。
                float3 towardCamera = GetWorldSpaceNormalizeViewDir(input.positionWS);
                float2 coveredXZ = input.positionWS.xz - towardCamera.xz *
                    ((input.positionWS.y - _BuildingSightOrigin.w) / max(towardCamera.y, 0.001));
                half visible = VisibleBuildingSurface(coveredXZ, true);
                if (!interior) visible *= VisibleBuildingSurface(input.positionWS.xz, false);
                color.a = lerp(1, _BuildingSurfaceParameters.y, visible);
                return color;
            }
            ENDHLSL
        }

        // 视野只影响本机画面，建筑仍按完整几何投影。
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
    }
}
