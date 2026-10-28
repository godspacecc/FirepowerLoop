// 临时候选：只在隔离场景内编译；未接入正式资源。
Shader "Hidden/FirepowerLoop/DockWaterPreview"
{
    Properties { _PreviewTime("Preview time",Float)=0 _ShoreDistance("Distance to actual dock",2D)="white"{} }
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
            TEXTURE2D(_ShoreDistance); SAMPLER(sampler_ShoreDistance);
            float _PreviewTime;
            struct A { float4 p:POSITION; };
            struct V { float4 p:SV_POSITION; float3 world:TEXCOORD0; };
            float Hash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
            float Noise(float2 p) { float2 c=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(Hash(c),Hash(c+float2(1,0)),f.x),lerp(Hash(c+float2(0,1)),Hash(c+1),f.x),f.y); }
            float Waves(float2 p,float t)
            {
                float bend=Noise(p*.23+float2(t*.014,-t*.018));
                return sin(p.x*1.5+p.y*.74-t*.75+bend*3)*.22
                    +sin(p.x*-.66+p.y*2.05+t*.46+bend)*.12
                    +Noise(p*2.2+float2(t*.055,t*.035))*.11;
            }
            V Vert(A a) { V v;v.world=TransformObjectToWorld(a.p.xyz);v.p=TransformWorldToHClip(v.world);return v; }
            half4 Frag(V i):SV_Target
            {
                float2 p=i.world.xz;float t=_PreviewTime;
                float distance=SAMPLE_TEXTURE2D(_ShoreDistance,sampler_ShoreDistance,(p+float2(35,31))/float2(70,62)).r;
                float broad=Noise(p*.12+float2(t*.009,-t*.012));
                float depth=smoothstep(.1,5.5,distance+broad*.6);
                float3 color=lerp(float3(.07,.31,.33),float3(.019,.155,.225),depth);
                float w=Waves(p,t);
                color*=.91+broad*.12+w*.27;
                float dx=(Waves(p+float2(.05,0),t)-w)/.05;
                float dz=(Waves(p+float2(0,.05),t)-w)/.05;
                float3 normal=normalize(float3(-dx*.14,1,-dz*.14));
                Light sun=GetMainLight(TransformWorldToShadowCoord(i.world));
                float3 view=GetWorldSpaceNormalizeViewDir(i.world);
                float glint=pow(saturate(dot(normal,normalize(view+sun.direction))),90)*.045;
                glint*=smoothstep(.42,.68,Noise(p*float2(1.3,3.1)+float2(t*.04,-t*.025)));
                float crest=pow(saturate(sin(p.x*3.6+p.y*1.2-t*.8+Noise(p*.4)*3)),18)*.026;
                color+=(glint+crest)*float3(.53,.76,.77)*sun.color;
                color*=.72+.28*sun.shadowAttenuation;
                float broken=smoothstep(.48,.71,Noise(p*2.8+float2(-t*.045,t*.025)));
                float foam=(1-smoothstep(.08,.34,distance))*.34*broken;
                color=lerp(color,float3(.42,.64,.61),foam);
                return half4(color,1);
            }
            ENDHLSL
        }
    }
}
