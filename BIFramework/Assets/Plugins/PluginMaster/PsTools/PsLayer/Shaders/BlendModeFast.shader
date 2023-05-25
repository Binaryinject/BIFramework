/*
Copyright (c) 2020 Omar Duarte
Unauthorized copying of this file, via any medium is strictly prohibited.
Writen by Omar Duarte, 2020.

This file incorporates work by The Code Corsair
http://www.elopezr.com/photoshop-blend-modes-in-unity/

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
*/

Shader "PluginMaster/PsBlendModeFast" 
{
	Properties 
	{
		[HideInInspector] _MainTex ("Texture", 2D) = "white" {}

		[HideInInspector] _blendMode("BlendMode", Int) = 0

		[HideInInspector] _blendOp1("__op1", Float) = 0.0
		[HideInInspector] _blendSrc1("__src1", Float) = 1.0
		[HideInInspector] _blendDst1("__dst1", Float) = 0.0
		[HideInInspector] _blendSrcAlpha1("__src_alpha1", Float) = 1.0
		[HideInInspector] _blendDstAlpha1("__dst_alpha1", Float) = 0.0

		[HideInInspector] _blendOp2("__op2", Float) = 0.0
		[HideInInspector] _blendSrc2("__src2", Float) = 1.0
		[HideInInspector] _blendDst2("__dst2", Float) = 0.0
		[HideInInspector] _blendSrcAlpha2("__src_alpha2", Float) = 1.0
		[HideInInspector] _blendDstAlpha2("__dst_alpha2", Float) = 0.0

		[HideInInspector] _visible("Visible", Range(0, 1)) = 0
		[HideInInspector] _opacity("Opacity", Range(0.0, 1.0)) = 1.0
	}
	
	CGINCLUDE
	#include "BlendMode.cginc"

	struct vin
	{
		float4 vertex : POSITION;
		float2 texcoord : TEXCOORD0;
		float4 color : COLOR;
	};

	struct v2f
	{
		float4 vertex : POSITION;
		float2 texcoord : TEXCOORD0;
		float4 color : COLOR;
	};
	ENDCG
	
	SubShader
	{
		Tags{ "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderType" = "Transparent" }
		ZWrite Off Lighting Off Cull Off Fog{ Mode Off }

		Pass
		{
			BlendOp[_blendOp1]
			Blend[_blendSrc1][_blendDst1]

			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma fragmentoption ARB_precision_hint_fastest
			#include "UnityCG.cginc"

			sampler2D _MainTex;
			int _blendMode;
			int _visible;
			float _opacity;
			float4 _tint;

			v2f vert(vin input)
			{
				v2f output;
				output.vertex = UnityObjectToClipPos(input.vertex);
				output.texcoord = input.texcoord;
				output.color = input.color;
				return output;
			}

			float4 frag(v2f input) : COLOR
			{
				float4 color = tex2D(_MainTex, input.texcoord) * input.color;
				color.a *= _opacity;
				if (_visible == 0)
				{
					color.rgba = float4(0, 0, 0, 0);
				}
				else if (_blendMode == DISSOLVE)
				{
					float randAlpha = color.a;
					if (frac(sin(dot(input.texcoord, float2(12.9898, 78.233))) * 43758.5453123) > randAlpha)
					{
						randAlpha = 0;
					}
					color.a *= randAlpha;
				}
				else if (_blendMode == DARKEN)
				{
					color.rgb = lerp(float3(1, 1, 1), color.rgb, color.a);
				}
				else if (_blendMode == MULTIPLY)
				{
					color.rgb *= color.a;
				}
				else if (_blendMode == COLOR_BURN)
				{
					color.rgb = 1.0 - (1.0 / max(0.001, color.rgb * color.a + 1.0 - color.a));
				}
				else if (_blendMode == LINEAR_BURN)
				{
					color.rgb = (color.rgb - 1.0) * color.a;
				}
				else if (_blendMode == LIGHTEN)
				{
					color.rgb = lerp(float3(0, 0, 0), color.rgb, color.a);
				}
				else if (_blendMode == SCREEN)
				{
					color.rgb *= color.a;
				}
				else if (_blendMode == COLOR_DODGE)
				{
					color.rgb = 1.0 / max(0.001, (1.0 - color.rgb * color.a));
				}
				else if (_blendMode == OVERLAY)
				{
					if (color.a == 0.5) color.a = 0.4999;
					
					color.rgb *= color.a;

					fixed3 desiredValue = (4.0 * color.rgb - 1.0) / (2.0 - 4.0 * color.rgb);
					fixed3 backgroundValue = (1.0 - color.a) / ((2.0 - 4.0 * color.rgb) * max(0.001, color.a));

					color.rgb = desiredValue + backgroundValue;
				}
				else if (_blendMode == SOFT_LIGHT)
				{
					if (color.a == 0.5) color.a = 0.4999;
					float3 desiredValue = 2.0 * color.rgb * color.a / (1.0 - 2.0 * color.rgb * color.a);
					float3 backgroundValue = (1.0 - color.a) / ((1.0 - 2.0 * color.rgb * color.a) * max(0.001, color.a));

					color.rgb = desiredValue + backgroundValue;
				}
				else if (_blendMode == HARD_LIGHT)
				{
					float3 numerator = (2.0 * color.rgb * color.rgb - color.rgb) * (color.a);
					float3 denominator = max(0.001, (4.0 * color.rgb - 4.0 * color.rgb * color.rgb) * (color.a) + 1.0 - color.a);
					color.rgb = numerator / denominator;
				}
				else if (_blendMode == VIVID_LIGHT)
				{
					color.rgb *= color.a;
					color.rgb = color.rgb >= 0.5 ? (1.0 / max(0.001, 2.0 - 2.0 * color.rgb)) : 1.0;
				}
				else if (_blendMode == LINEAR_LIGHT)
				{
					color.rgb = (2 * color.rgb - 1.0) * color.a;
				}
				else if (_blendMode == EXCLUSION)
				{
					color.rgb *= 2.0 * color.a;
				}
				else if (_blendMode == DIVIDE)
				{
					color.rgb = color.a / max(0.001, color.rgb);
				}
				
				return color;
			}
			
			ENDCG
		}

		Pass
		{
			BlendOp[_blendOp2]
			Blend[_blendSrc2][_blendDst2]

			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma fragmentoption ARB_precision_hint_fastest
			#include "UnityCG.cginc"
			
			sampler2D _MainTex;
			float _blendMode;
			float _opacity;

			v2f vert(vin input)
			{
				v2f output;
				output.vertex = UnityObjectToClipPos(input.vertex);
				output.texcoord = input.texcoord;
				output.color = input.color;
				return output;
			}

			float4 frag(v2f input) : COLOR
			{	
				float4 color = tex2D(_MainTex, input.texcoord);

				color.rgb *= input.color.rgb;
				color.a *= input.color.a * _opacity;
				
				if (_blendMode == OVERLAY)
				{
					if (color.a == 0.5) color.a = 0.4999;
					color.rgb *= color.a; 
					
					float3 value = (2.0 - 4.0 * color.rgb);
					color.rgb = value * max(0.001, color.a);
				}
				else if(_blendMode == SOFT_LIGHT)
				{
					if (color.a == 0.5) color.a = 0.4999;
					color.rgb = (1.0 - 2.0 * color.rgb * color.a) * max(0.001, color.a);
				}
				else if (_blendMode == HARD_LIGHT)
				{
					color.rgb = max(0.001, (4.0 * color.rgb - 4.0 * color.rgb * color.rgb) * (color.a) + 1.0 - color.a); 
				}
				else if (_blendMode == VIVID_LIGHT)
				{
					color.rgb = color.rgb < 0.5 ? (color.a - color.a / max(0.0001, 2.0 * color.rgb)) : 0.0;
				}
				
				return color;
			}
			
			ENDCG
		}
	}
}
