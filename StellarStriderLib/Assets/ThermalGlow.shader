Shader "Hidden/StellarStriderLib/Thermal Glow"
{
    Properties
    {
        [HDR] _BaseColor ("Glow color", Color) = (1, 1, 0, 0)
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent+100" }

        Pass
        {
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend SrcAlpha One, Zero One
            Cull Back
            ZTest LEqual
            ZWrite Off
            Offset -1, -2

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
            CBUFFER_END

            float4 Vertex(float3 positionOS : POSITION) : SV_POSITION
            {
                return TransformObjectToHClip(positionOS);
            }

            half4 Fragment() : SV_Target
            {
                return _BaseColor;
            }
            ENDHLSL
        }
    }
}
