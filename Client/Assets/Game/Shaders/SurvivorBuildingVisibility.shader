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
        [HideInInspector] _BuildingSightGroundHeight ("Ground Height", Float) = 0
        [HideInInspector] _BuildingSightOpacity ("Visible Surface Opacity", Range(0,1)) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D(_BuildingSight);
        SAMPLER(sampler_BuildingSight);
        float4 _BuildingSightOrigin;
        float _BuildingSightGroundHeight;
        half _BuildingSightOpacity;

        half VisibleBuildingSurface(float2 worldXZ)
        {
            float radius = _BuildingSightOrigin.z;
            float2 offset = worldXZ - _BuildingSightOrigin.xy;
            float distanceSquared = dot(offset, offset);
            if (distanceSquared > radius * radius)
                return 0;
            if (distanceSquared < 0.000001)
                return 1;
            float distanceToEye = sqrt(distanceSquared);
            float angle = frac(atan2(offset.y, offset.x) * 0.1591549431);
            float4 encoded = SAMPLE_TEXTURE2D_LOD(_BuildingSight, sampler_BuildingSight, float2(angle, 0.5), 0);
            float firstObstacle = dot(encoded.rg, float2(65280, 255)) * (radius / 65535.0);
            return distanceToEye <= firstObstacle ? 1 : 0;
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
                // 所有表面都检查它在镜头中盖住的位置，避免透明后露出仍不可见的房间。
                float3 towardCamera = GetWorldSpaceNormalizeViewDir(input.positionWS);
                // 从低墙下方地面连续过渡到高处的眼高平面，取样始终位于表面之后。
                float height = input.positionWS.y - _BuildingSightGroundHeight;
                float eyeHeight = max(0, _BuildingSightOrigin.w - _BuildingSightGroundHeight);
                float behind = height - clamp(height - eyeHeight * 0.5, 0, eyeHeight);
                float2 coveredXZ = input.positionWS.xz -
                    towardCamera.xz * (behind / max(towardCamera.y, 0.001));
                half visible = behind > 0.001 && towardCamera.y > 0.001
                    ? VisibleBuildingSurface(coveredXZ) : 0;
                color.a = lerp(1, _BuildingSightOpacity, visible);
                return color;
            }
            ENDHLSL
        }

        // 视野只影响本机画面，建筑仍按完整几何投影。
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
    }
}
