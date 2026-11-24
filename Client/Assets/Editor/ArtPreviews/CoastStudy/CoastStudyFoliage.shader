Shader "CoastStudy/Foliage"
{
    Properties
    {
        _BaseMap("Albedo",2D)="white"{}
        _BaseColor("Tint",Color)=(1,1,1,1)
        _Cutoff("Alpha cutoff",Range(0,1))=0.35
        _Smoothness("Smoothness",Range(0,1))=0.1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
        Cull Off
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST; half4 _BaseColor; half _Cutoff; half _Smoothness;
            CBUFFER_END
            struct A { float4 p:POSITION; float3 n:NORMAL; float2 uv:TEXCOORD0; };
            struct V { float4 p:SV_POSITION; float3 world:TEXCOORD0; float3 normal:TEXCOORD1; float2 uv:TEXCOORD2; };
            V Vert(A a) { V v; v.world=TransformObjectToWorld(a.p.xyz);v.p=TransformWorldToHClip(v.world);v.normal=TransformObjectToWorldNormal(a.n);v.uv=TRANSFORM_TEX(a.uv,_BaseMap);return v; }
            half4 Frag(V i):SV_Target
            {
                half4 albedo=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv)*_BaseColor;
                clip(albedo.a-_Cutoff);
                Light sun=GetMainLight(TransformWorldToShadowCoord(i.world));
                half diffuse=abs(dot(normalize(i.normal),sun.direction));
                half3 color=albedo.rgb*(half3(.42,.47,.33)+sun.color*(.55*diffuse+.13)*sun.shadowAttenuation);
                return half4(color,1);
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
    }
}
