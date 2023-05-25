/*
Copyright (c) 2020 Omar Duarte
Unauthorized copying of this file, via any medium is strictly prohibited.
Writen by Omar Duarte, 2020.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
*/

Shader "PluginMaster/PsBlendModeSimple" 
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
	}
	
	SubShader
	{
		Tags
		{
			"Queue" = "Transparent"
			"IgnoreProjector" = "True"
			"RenderType" = "Transparent"
			"PreviewType" = "Plane"
			"CanUseSpriteAtlas" = "True"
		}

		ZWrite Off
		Lighting Off
		Cull Off
		Fog{ Mode Off }

		Pass
		{
			BlendOp[_blendOp1]
			Blend[_blendSrc1][_blendDst1]
			
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma fragmentoption ARB_precision_hint_fastest
			#include "UnityCG.cginc"
			#include "BlendMode.cginc"

			sampler2D _MainTex;
			int _blendMode;
			int _visible;
			float _opacity;

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
					color.a = (frac(sin(dot(input.texcoord, float2(12.9898, 78.233))) * 43758.5453123) + 0.001 > color.a) ? 0 : 1;
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
				else if (_blendMode == LINEAR_LIGHT)
				{
					color.rgb = (2 * color.rgb - 1.0) * color.a;
				}
				else if (_blendMode == DIVIDE)
				{
					color.rgb = color.a / max(0.001, color.rgb);
				}
				return color;
			}
			
			ENDCG
		}
	}
}
