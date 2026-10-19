Shader "FirepowerLoop/Treasure Surface"
{
    Properties
    {
        _BaseColor ("Color", Color) = (1,.72,.24,1)
        _SurfaceMap ("Cast surface: roughness / height / stamp", 2D) = "gray" {}
        _ReflectionCube ("Vault reflection", Cube) = "" {}
        _Crystal ("Sapphire", Float) = 0
        _Smoothness ("Polish", Range(0,1)) = .8
        [HideInInspector] _Surface ("Surface", Float) = 0
        [HideInInspector] _Blend ("Blend", Float) = 0
        [HideInInspector] _SrcBlend ("Source blend", Float) = 1
        [HideInInspector] _DstBlend ("Destination blend", Float) = 0
        [HideInInspector] _ZWrite ("Depth write", Float) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
        TEXTURE2D(_SurfaceMap); SAMPLER(sampler_SurfaceMap);
        TEXTURECUBE(_ReflectionCube); SAMPLER(sampler_ReflectionCube);
        // 所有 Pass 必须共享完全相同的材质布局，避免 SRP Batcher 将另一种奖励的参数复用到当前绘制。
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            float4 _SurfaceMap_TexelSize;
            float _Crystal;
            float _Smoothness;
            float _Surface;
            float _Blend;
            float _SrcBlend;
            float _DstBlend;
            float _ZWrite;
        CBUFFER_END
        ENDHLSL
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0; };
            struct Varyings { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; float3 normalWS:TEXCOORD1; float2 uv:TEXCOORD2; float3 positionOS:TEXCOORD3; };
            Varyings Vert(Attributes i)
            {
                Varyings o;
                o.positionWS=TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS=TransformWorldToHClip(o.positionWS);
                o.normalWS=TransformObjectToWorldNormal(i.normalOS);
                o.uv=i.uv; o.positionOS=i.positionOS.xyz; return o;
            }
            half3 Vault(float3 ray, float roughness)
            { return SAMPLE_TEXTURECUBE_LOD(_ReflectionCube,sampler_ReflectionCube,ray,roughness*5).rgb; }
            // 在简化切面体内追踪四次反射，得到随观察角度变化的内部亮面；固定循环上限控制宝石开销。
            half3 SapphireInterior(float3 start,float3 ray)
            {
                half3 sum=0,energy=1;
                float3 p=start+ray*.008;
                [unroll] for(int bounce=0;bounce<4;bounce++)
                {
                    float nearest=100;
                    float3 normal=float3(0,1,0);
                    float denom=ray.y;
                    float distance=(.49-p.y)/max(denom,.00001);
                    if(denom>.00001 && distance>.001){nearest=distance;}
                    [unroll] for(int side=0;side<8;side++)
                    {
                        float angle=(side+.5)*.785398;
                        float3 radial=float3(cos(angle)/.46,0,sin(angle)/.365);
                        float3 crown=radial+float3(0,2.27,0);
                        float3 pavilion=radial+float3(0,-4.166667,0);
                        float dc=dot(crown,ray),dp=dot(pavilion,ray);
                        float tc=(1.602-dot(crown,p))/max(dc,.00001);
                        float tp=-dot(pavilion,p)/max(dp,.00001);
                        if(dc>.00001 && tc>.001 && tc<nearest){nearest=tc;normal=normalize(crown);}
                        if(dp>.00001 && tp>.001 && tp<nearest){nearest=tp;normal=normalize(pavilion);}
                    }
                    if(nearest>99)break;
                    p+=ray*nearest;
                    energy*=exp(-nearest*float3(4.5,1.3,.3));
                    float3 outgoing=refract(ray,-normal,1.77);
                    half fresnel=.075+.925*pow(1-saturate(dot(ray,normal)),5);
                    bool total=dot(outgoing,outgoing)<.01;
                    if(!total)sum+=energy*(1-fresnel)*Vault(TransformObjectToWorldDir(outgoing),.02);
                    energy*=total?1:fresnel;
                    ray=reflect(ray,normal);p+=ray*.004;
                }
                return sum+energy*half3(.012,.04,.1);
            }
            half4 Frag(Varyings i):SV_Target
            {
                float3 n=normalize(i.normalWS), v=GetWorldSpaceNormalizeViewDir(i.positionWS);
                Light key=GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                float3 l=key.direction, h=normalize(l+v);
                half3 color;
                if (_Crystal>.5)
                {
                    // 单次绘制的折射与内部反射近似，避免移动端多层透明排序和屏幕颜色依赖。
                    float3 inside=refract(-v,n,1.0/1.77);
                    half fresnel=.075+.925*pow(1-saturate(dot(n,v)),5);
                    half3 body=SapphireInterior(i.positionOS,normalize(TransformWorldToObjectDir(inside)))*half3(.18,.65,1.2);
                    body+=_BaseColor.rgb*.12;
                    color=body*(1-fresnel)+Vault(reflect(-v,n),.015)*fresnel;
                    color+=key.color*pow(saturate(dot(n,h)),220)*.08;
                    color+=_BaseColor.rgb*.1*saturate(dot(n,l));
                }
                else
                {
                    half3 cast=SAMPLE_TEXTURE2D(_SurfaceMap,sampler_SurfaceMap,i.uv).rgb;
                    float2 texel=_SurfaceMap_TexelSize.xy;
                    float dx=SAMPLE_TEXTURE2D(_SurfaceMap,sampler_SurfaceMap,i.uv+float2(texel.x,0)).g-cast.g;
                    float dz=SAMPLE_TEXTURE2D(_SurfaceMap,sampler_SurfaceMap,i.uv+float2(0,texel.y)).g-cast.g;
                    n=normalize(n+TransformObjectToWorldDir(float3(-dx,0,-dz))*.4);
                    half polish=saturate(_Smoothness-(cast.r-.5)*.3);
                    half3 tint=_BaseColor.rgb*(1-cast.b*.48);
                    half nv=saturate(dot(n,v));
                    half3 fresnel=tint+(1-tint)*pow(1-nv,5);
                    float3 reflected=reflect(-v,n)+TransformObjectToWorldDir(i.positionOS*float3(.55,0,1.1));
                    color=Vault(reflected,1-polish)*fresnel*.55;
                    color+=tint*(SampleSH(n)*.18+key.color*saturate(dot(n,l))*.1);
                    color+=key.color*tint*pow(saturate(dot(n,h)),lerp(28,240,polish))*.25*key.shadowAttenuation;
                }
                return half4(color,_BaseColor.a);
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull Back
            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment DepthFrag
            #pragma target 3.0
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection, _LightPosition;
            float4 ShadowVert(float4 positionOS:POSITION,float3 normalOS:NORMAL):SV_POSITION
            {
                float3 p=TransformObjectToWorld(positionOS.xyz),n=TransformObjectToWorldNormal(normalOS);
                #ifdef _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 direction=normalize(_LightPosition-p);
                #else
                float3 direction=_LightDirection;
                #endif
                float4 clip=TransformWorldToHClip(ApplyShadowBias(p,n,direction));
                #if UNITY_REVERSED_Z
                clip.z=min(clip.z,UNITY_NEAR_CLIP_VALUE);
                #else
                clip.z=max(clip.z,UNITY_NEAR_CLIP_VALUE);
                #endif
                return clip;
            }
            half4 DepthFrag():SV_Target { return 0; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R Cull Back
            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            float4 DepthVert(float4 positionOS:POSITION):SV_POSITION { return TransformObjectToHClip(positionOS.xyz); }
            half4 DepthFrag():SV_Target { return 0; }
            ENDHLSL
        }
    }
}
