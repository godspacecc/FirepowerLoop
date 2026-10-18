Shader "FirepowerLoop/BombAdditive"
{
    Properties
    {
        _BaseMap("纹理", 2D) = "white" {}
        [HDR] _BaseColor("颜色", Color) = (1,1,1,1)
        _SoftEdge("光圈软边", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _BaseMap_ST;
                float _SoftEdge;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };
            Varyings Vert(Attributes input)
            {
                Varyings output; output.positionCS=TransformObjectToHClip(input.positionOS.xyz);
                output.uv=TRANSFORM_TEX(input.uv,_BaseMap);output.color=input.color;return output;
            }
            half4 Frag(Varyings input):SV_Target
            {
                half4 color=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,input.uv)*_BaseColor*input.color;
                // 仅光圈按线宽羽化；棱片使用网格顶点透明度保留尖锐轮廓。
                color.a*=lerp(1,smoothstep(0,.35,input.uv.y)*(1-smoothstep(.65,1,input.uv.y)),_SoftEdge);
                return color;
            }
            ENDHLSL
        }
    }
}
