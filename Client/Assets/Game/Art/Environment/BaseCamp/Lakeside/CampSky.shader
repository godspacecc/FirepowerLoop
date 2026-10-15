Shader "AnotherDay/BaseCampSky"
{
    Properties
    {
        _Horizon("Horizon", Color) = (0.66,0.74,0.76,1)
        _Zenith("Zenith", Color) = (0.24,0.45,0.62,1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Background" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Cull Front ZWrite Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 direction : TEXCOORD0; };
            CBUFFER_START(UnityPerMaterial)
                half4 _Horizon;
                half4 _Zenith;
            CBUFFER_END
            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                // 天空只占远平面，不受俯视/越肩镜头不同的远裁剪距离截断。
                #if UNITY_REVERSED_Z
                    output.positionCS.z = output.positionCS.w * 0.0001;
                #else
                    output.positionCS.z = output.positionCS.w * 0.9999;
                #endif
                output.direction = input.positionOS.xyz;
                return output;
            }
            half4 frag(Varyings input) : SV_Target
            {
                float height = saturate(normalize(input.direction).y);
                return lerp(_Horizon, _Zenith, smoothstep(0,0.8,height));
            }
            ENDHLSL
        }
    }
}
