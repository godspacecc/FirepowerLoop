Shader "FirepowerLoop/DockWater"
{
    Properties { _BaseColor("Tint",Color)=(0.04,0.3,0.34,1) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
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
            struct A { float4 p:POSITION; };
            struct V { float4 p:SV_POSITION; float3 world:TEXCOORD0; };
            float Shore(float z) { return 14.8+0.9*sin(z*0.27)+0.42*sin(z*0.63); }
            float Hash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
            float Noise(float2 p) { float2 c=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(Hash(c),Hash(c+float2(1,0)),f.x),lerp(Hash(c+float2(0,1)),Hash(c+1),f.x),f.y); }
            float Waves(float2 p) { float w=Noise(p*.7)*3;return sin(p.x*2.6+p.y*1.7+w)*.22+Noise(p*3.1+float2(w,-w))*.32+Noise(p*8.3)*.09; }
            V Vert(A a) { V v; v.world=TransformObjectToWorld(a.p.xyz); v.p=TransformWorldToHClip(v.world); return v; }
            half4 Frag(V i):SV_Target
            {
                float2 p=i.world.xz;
                float d=8;
                float waves=Waves(p);
                float dx=(Waves(p+float2(.03,0))-waves)/.03;
                float dz=(Waves(p+float2(0,.03))-waves)/.03;
                float3 normal=normalize(float3(-dx*.23,1,-dz*.23));
                float3 view=GetWorldSpaceNormalizeViewDir(i.world);
                Light light=GetMainLight(TransformWorldToShadowCoord(i.world));
                float spec=pow(saturate(dot(normal,normalize(view+light.direction))),65)*3.2;
                float3 color=lerp(float3(0.12,0.59,0.51),float3(0.018,0.21,0.27),saturate(d/9));
                color*=0.78+0.37*waves;
                float sparkle=pow(saturate(waves*1.2),8)*.23;
                float foam=0;
                color=color*(0.52+0.48*light.shadowAttenuation)+(spec+sparkle)*light.color*light.shadowAttenuation;
                color=lerp(color,float3(0.85,0.94,0.83),foam);
                return half4(color,1);
            }
            ENDHLSL
        }
    }
}
