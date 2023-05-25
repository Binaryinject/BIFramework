// MIT License

// Copyright (c) 2017 Xavier Martinez

// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:

// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.

// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

Shader "GeoAO/VertexAO"
{
    //Shader used to compute AO value for each vertex

    Properties
    {
        _AOTex ("AO Texture to blend", 2D) = "white"{}
        _AOTex2 ("AO Texture to blend", 2D) = "white"{}
        _uCount ("Total samples",int) = 128
        _curCount ("Current sample",int) = 0
        _uVertex ("Vertex texture",2D) = "white" {}
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
        }
        ZWrite Off
        Pass
        {
            Cull Off
            Fog
            {
                Mode off
            }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // #include "UnityCG.cginc"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
            float4x4 _VP;
            float4x4 _InverseView;
            uniform float _uCount;
            uniform int _curCount;
            uniform float4 _AOTex_TexelSize;
            uniform float4 _uVertex_TexelSize;
            CBUFFER_END

            TEXTURE2D(_AOTex);
            SAMPLER(sampler_AOTex);
            
            TEXTURE2D(_AOTex2);
            SAMPLER(sampler_AOTex2);

            TEXTURE2D(_uPos);
            uniform SAMPLER(sampler_uPos);
            
            TEXTURE2D(_uSource);
            uniform SAMPLER(sampler_uSource);

            TEXTURE2D(_uVertex);
            uniform SAMPLER(sampler_uVertex);

            float4 DepthFromDepthTexture(float2 uv)
            {
                const float2 p11_22 = float2(unity_CameraProjection._11, unity_CameraProjection._22);
                const float2 p13_31 = float2(unity_CameraProjection._13, unity_CameraProjection._23);
                const float isOrtho = unity_OrthoParams.w;
                const float near = _ProjectionParams.y;
                const float far = _ProjectionParams.z;


                #if UNITY_REVERSED_Z
                float d = SampleSceneDepth(uv);
                #else
                    // Adjust Z to match NDC for OpenGL ([-1, 1])
                    float d = lerp(UNITY_NEAR_CLIP_VALUE, 1, SampleSceneDepth(uv));
                #endif


                #if UNITY_REVERSED_Z
                d = 1 - d;
                #endif

                // Does not seem to work
                // float3 worldPos = ComputeWorldSpacePosition(uv, d, UNITY_MATRIX_I_VP);

                float z_ortho = lerp(near, far, d);

                float z_pers = near * far / lerp(far, near, d);
                float vz = lerp(z_pers, z_ortho, isOrtho);

                float3 vpos = float3((uv * 2 - 1 - p13_31) / p11_22 * lerp(vz, 1, isOrtho), -vz);
                float4 wpos = mul(_InverseView, float4(vpos, 1));

                return wpos;
            }


            struct Attributes
            {
                // The positionOS variable contains the vertex positions in object
                // space.
                float4 positionOS : POSITION;
            };

            
            struct Varyings
            {
                // float4 p : POSITION;
                float4 positionNDC : TEXCOORD0;
                float4 positionCS : POSITION;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT; // Shader output
                VertexPositionInputs posInput = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = posInput.positionCS;
                OUT.positionNDC = posInput.positionNDC;

                return OUT;
            }

            half4 frag(Varyings IN) : SV_TARGET
            {
                float2 uv = IN.positionNDC.xy / IN.positionNDC.w;
                
                float3 vertex = SAMPLE_TEXTURE2D(_uVertex, sampler_uVertex, uv).xyz;

                //Vertex in clip space
                float4 vertexCS = mul(_VP, float4(vertex, 1.0));
                float4 posInCamDepth = ComputeScreenPos(vertexCS);
                posInCamDepth.xyz = posInCamDepth.xyz / posInCamDepth.w;

                float o = 2.0; //Higher than 1 to decrease texture darkness

                if (abs(vertex.z - DepthFromDepthTexture(posInCamDepth.xy).z) > 0.01)
                    o = 0.0;

                float src = SAMPLE_TEXTURE2D(_AOTex2, sampler_AOTex2, uv).w;
                if (_curCount == 0) src = 0.0f; //Fix clearing texture on OpenGL
                o = src + o / _uCount; //Previous value + new value 
                return float4(o, o, o, o);
            }
            ENDHLSL
        }
    }
}