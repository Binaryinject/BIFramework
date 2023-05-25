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

Shader "PluginMaster/PsBlendModeAccurate" 
{
	Properties 
	{
		[HideInInspector] _MainTex ("Texture", 2D) = "white" {}
		[HideInInspector] _blendMode("BlendMode", Int) = 0
		[HideInInspector] _visible("Visible", Range(0, 1)) = 1
		[HideInInspector] _opacity("Opacity", Range(0.0, 1.0)) = 1.0
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
		
		GrabPass { }

		Pass
		{
			BlendOp Add
			Blend SrcAlpha OneMinusSrcAlpha

			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma fragmentoption ARB_precision_hint_fastest
			#include "UnityCG.cginc"
			#include "BlendMode.cginc"

			sampler2D _GrabTexture;
			sampler2D _MainTex;
			int _blendMode;
			int _visible;
			float _opacity;
			float4 _tint;

			struct vin
			{
				float4 vertex : POSITION;
				float2 uv : TEXCOORD0;
				float4 color : COLOR;
			};

			struct v2f
			{
				float4 vertex : SV_POSITION;
				float2 uv : TEXCOORD0;
				float4 grabUV : TEXCOORD1;
				float4 color : COLOR;
			};

			v2f vert(vin input)
			{
				v2f output;

				output.vertex = UnityObjectToClipPos(input.vertex);
				output.grabUV = ComputeGrabScreenPos(output.vertex);
				output.uv = input.uv;
				output.color = input.color;
				return output;
			}
			
			float GetLuminosity(float3 color)
			{
				return 0.3f * color.r + 0.59f * color.g + 0.11f * color.b;
			}

			float3 SetLuminosity(float3 color, float l)
			{
				float colorLum = GetLuminosity(color);
				float delta = l - colorLum;

				float3 result = color.rgb + delta.xxx;

				float minValue = min(min(color.r, color.g), color.b);
				float maxValue = max(max(color.r, color.g), color.b);

				if (minValue < 0.0)
				{
					result.rgb = colorLum.xxx + (((result.rgb - colorLum.xxx) * colorLum) / (colorLum - minValue));
				}
				if (maxValue > 1.0)
				{
					result.rgb = colorLum.xxx + (((result.rgb - colorLum.xxx) * (1.0 - colorLum)) / (maxValue - colorLum));
				}
				return result;
			}

			float GetSaturation(float3 color)
			{
				return max(max(color.r, color.g), color.b) - min(min(color.r, color.g), color.b);
			}

			float3 SetSaturation(float3 color, float s)
			{
				const int MIN_COLOR = 0, MID_COLOR = 1, MAX_COLOR = 2;
				int R = MIN_COLOR, G = MIN_COLOR, B = MIN_COLOR;
				float minValue, midValue, maxValue;
				
				if (color.r <= color.g && color.r <= color.b)
				{
					minValue = color.r;
					if (color.g <= color.b)
					{
						G = MID_COLOR;
						B = MAX_COLOR;
						midValue = color.g;
						maxValue = color.b;
					}
					else
					{
						B = MID_COLOR;
						G = MAX_COLOR;
						midValue = color.b;
						maxValue = color.g;
					}
				}
				else if (color.g <= color.r && color.g <= color.b)
				{
					minValue = color.g;
					if (color.r <= color.b)
					{
						R = MID_COLOR;
						B = MAX_COLOR;
						midValue = color.r;
						maxValue = color.b;
					}
					else
					{
						B = MID_COLOR;
						R = MAX_COLOR;
						midValue = color.b;
						maxValue = color.r;
					}
				}
				else 
				{
					minValue = color.b;
					if (color.r <= color.g)
					{
						R = MID_COLOR;
						G = MAX_COLOR;
						midValue = color.r;
						maxValue = color.g;
					}
					else
					{
						G = MID_COLOR;
						R = MAX_COLOR;
						midValue = color.g;
						maxValue = color.r;
					}
				}
				
				if ((maxValue - minValue) > 0.016)
				{
					midValue = (((midValue - minValue) * s) / (maxValue - minValue));
					maxValue = s;
				}
				else
				{
					midValue = maxValue = 0;
				}
				minValue = 0.0;
				
				float3 result;
				result.r = R == MIN_COLOR ? minValue : (R == MID_COLOR ? midValue : maxValue);
				result.g = G == MIN_COLOR ? minValue : (G == MID_COLOR ? midValue : maxValue);
				result.b = B == MIN_COLOR ? minValue : (B == MID_COLOR ? midValue : maxValue);
				return result;
			}

			float4 frag(v2f input) : COLOR
			{	
				float4 color = tex2D(_MainTex, input.uv) * input.color;
				color.a *= _opacity;
				float4 bgColor = tex2D(_GrabTexture, float2(input.grabUV.x/ input.grabUV.w, input.grabUV.y / input.grabUV.w));
				if (_visible == 0)
				{
					color.rgba = float4(0, 0, 0, 0);
				}
				else if (_blendMode == DISSOLVE)
				{
					float randAlpha = color.a;
					if (frac(sin(dot(input.uv, float2(12.9898, 78.233))) * 43758.5453123) > randAlpha)
					{
						randAlpha = 0.0;
					}
					color.a = randAlpha / max(0.001, _opacity);
				}
				else if (_blendMode == DARKEN)
				{
					color.rgb = min(bgColor.rgb, color.rgb);
				}
				else if (_blendMode == MULTIPLY)
				{
					color.rgb *= bgColor.rgb;
				}
				else if (_blendMode == COLOR_BURN)
				{
					color.rgb = 1.0 - (1.0 - bgColor.rgb) / max(0.001, color.rgb);
				}
				else if (_blendMode == LINEAR_BURN)
				{
					color.rgb = bgColor.rgb + color.rgb - 1.0;
				}
				else if (_blendMode == DARKER_COLOR)
				{
					if (GetLuminosity(bgColor) < GetLuminosity(color)) color.rgb = bgColor.rgb;
				}
				else if (_blendMode == LIGHTEN)
				{
					color.rgb = max(color.rgb, bgColor.rgb);
				}
				else if (_blendMode == SCREEN)
				{
					color.rgb = 1.0 - (1.0 - color.rgb) * (1.0 - bgColor.rgb);
				}
				else if (_blendMode == COLOR_DODGE)
				{
					color.rgb = bgColor.rgb / (1.0 - color.rgb);
				}
				else if (_blendMode == LINEAR_DODGE)
				{
					color.rgb = bgColor.rgb + color.rgb;
				}
				else if (_blendMode == LIGHTER_COLOR)
				{
					if (GetLuminosity(bgColor) > GetLuminosity(color)) color.rgb = bgColor.rgb;
				}
				else if (_blendMode == OVERLAY)
				{
					color.rgb = bgColor.rgb > 0.5f ? (1.0 - 2.0 * (1.0 - bgColor.rgb) * (1.0 - color.rgb)) : 2.0 * bgColor.rgb * color.rgb;
				}
				else if (_blendMode == SOFT_LIGHT)
				{
					color.rgb = color.rgb <= 0.5 ? bgColor.rgb - (1.0 - 2.0 * color.rgb) * bgColor.rgb * (1.0 - bgColor.rgb) : 
						bgColor.rgb + (2.0 * color.rgb - 1.0) * ( bgColor.rgb <= 0.25 
							? ((16.0 * bgColor.rgb - 12.0) * bgColor.rgb + 4.0) * bgColor.rgb 
							: sqrt(bgColor) - bgColor.rgb);
				}
				else if (_blendMode == HARD_LIGHT)
				{
					color.rgb = bgColor.rgb <= 0.5 ? (1.0 - 2.0 * (1.0 - bgColor.rgb) * (1.0 - color.rgb)) : 2.0 * bgColor.rgb * color.rgb;
				}
				else if (_blendMode == VIVID_LIGHT)
				{
					color.rgb = color.rgb <= 0.5
						? color.rgb == 0.0 ? 0.0 : 1.0 - (1.0 - bgColor.rgb) / (2.0 * color.rgb)
						: color.rgb == 1.0 ? 1.0 : bgColor.rgb / (2.0 * (1.0 - color.rgb));
				}
				else if (_blendMode == LINEAR_LIGHT)
				{
					color.rgb = bgColor.rgb + 2.0 * color.rgb - 1.0;
				}
				else if (_blendMode == PIN_LIGHT)
				{
					color.rgb = bgColor.rgb < 2.0 * color.rgb - 1.0 ? 
						2.0 * color.rgb - 1.0 : 
						(bgColor.rgb < 2.0 * color.rgb ? bgColor.rgb : 2.0 * color.rgb);
				}
				else if (_blendMode == HARD_MIX)
				{
					color.rgb = color.rgb < 1.0 - bgColor.rgb ? 0.0 : 1.0;
				}
				else if (_blendMode == DIFFERENCE)
				{
					color.rgb = abs(color.rgb - bgColor.rgb);
				}
				else if (_blendMode == EXCLUSION)
				{
					color.rgb = color.rgb + bgColor.rgb - 2.0 * color.rgb * bgColor.rgb;
				}
				else if (_blendMode == SUBSTRACT)
				{
					color.rgb = bgColor.rgb - color.rgb;
				}
				else if (_blendMode == DIVIDE)
				{
					color.rgb = color.rgb == 0.0 ? 1.0 : bgColor.rgb / color.rgb;
				}
				else if (_blendMode == HUE)
				{
					color.rgb = SetLuminosity(SetSaturation(color, GetSaturation(bgColor)), GetLuminosity(bgColor)).rgb;
				}
				else if (_blendMode == SATURATION)
				{
					color.rgb = SetLuminosity(SetSaturation(bgColor, GetSaturation(color)), GetLuminosity(bgColor)).rgb;
				}
				else if (_blendMode == COLOR)
				{
					color.rgb = SetLuminosity(color, GetLuminosity(bgColor)).rgb;
				}
				else if (_blendMode == LUMINOSITY)
				{
					color.rgb = SetLuminosity(bgColor, GetLuminosity(color)).rgb;
				}
				color.rgb = clamp(color.rgb, 0.0, 1.0);
				return color;
			}
			
			ENDCG
		}
	}
}
