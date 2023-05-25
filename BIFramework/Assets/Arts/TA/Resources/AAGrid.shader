Shader "Unlit/AAGrid"
{
    Properties
    {
        _GridColour ("Grid Colour", color) = (1, 1, 1, 1)
        _BaseColour ("Base Colour", color) = (1, 1, 1, 0)
        _GridSpacing ("Grid Spacing", float) = 0.1
        _LineThickness ("Line Thickness", float) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" }
        LOD 100

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Offset -1, -1
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            CBUFFER_START(UnityPerMaterial)
                fixed4 _GridColour;
                fixed4 _BaseColour;
                float _GridSpacing;
                float _LineThickness;
            CBUFFER_END
            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = (mul(unity_ObjectToWorld, v.vertex).xz + float2(_GridSpacing * 0.5, _GridSpacing * 0.5)) / _GridSpacing;

                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 wrapped = frac(i.uv) - 0.5f;
                float2 range = abs(wrapped);
                float2 pixelRange = range / fwidth(i.uv);
                float lineWeight = saturate(min(pixelRange.x, pixelRange.y) - _LineThickness);

                return lerp(_GridColour, _BaseColour, lineWeight);
            }
            ENDCG
        }
    }
}