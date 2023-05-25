Shader "GeoAO/VertAOOpti"
{
    //Example shader to show how to use computed AO value

    Properties
    {
        _MainTex ("Base (RGB)", 2D) = "white" {}
        _AOColor ("AO Color", Color) = (0,0,0,1)
        _AOScale ("AO Scale", Range(0.0, 5.0)) = 1.0
        //_AOTex ("AO Texture", 2D) = "white" {}
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline"
        }
        Pass
        {
            Name "ForwardLit"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
            half4 _AOColor;
            float _AOScale;
            CBUFFER_END

            TEXTURE2D(_AOTex);
            SAMPLER(sampler_AOTex);

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float uv : TEXCOORD0;
                float2 uv3 : TEXCOORD2;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float ao : TEXCOORD2;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT; // Shader output
                OUT.ao = 1 - SAMPLE_TEXTURE2D_LOD(_AOTex, sampler_AOTex, IN.uv3, 0).a;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
                return half4(lerp(color.rgb, _AOColor.rgb, IN.ao * _AOScale), color.a);
            }
            ENDHLSL
        }
    }
}