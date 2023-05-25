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

using UnityEngine;
using System.Collections;
using UnityEngine.Rendering;

namespace PluginMaster
{
    [ExecuteAlways]
    public abstract class PsLayer : PsGroup
    {
        private static readonly int BLEND_MODE_PROP_ID = Shader.PropertyToID("_blendMode");
        private static readonly int VISIBLE_PROP_ID = Shader.PropertyToID("_visible");
        private static readonly int OPACITY_PROP_ID = Shader.PropertyToID("_opacity");
        private static readonly int BLEND_OP_1_PROP_ID = Shader.PropertyToID("_blendOp1");
        private static readonly int BLEND_SRC_1_PROP_ID = Shader.PropertyToID("_blendSrc1");
        private static readonly int BLEND_DST_1_PROP_ID = Shader.PropertyToID("_blendDst1");
        private static readonly int BLEND_SRC_ALPHA_1_PROP_ID = Shader.PropertyToID("_blendSrcAlpha1");
        private static readonly int BLEND_DST_ALPHA_1_PROP_ID = Shader.PropertyToID("_blendDstAlpha1");
        private static readonly int BLEND_OP_2_PROP_ID = Shader.PropertyToID("_blendOp2");
        private static readonly int BLEND_SRC_2_PROP_ID = Shader.PropertyToID("_blendSrc2");
        private static readonly int BLEND_DST_2_PROP_ID = Shader.PropertyToID("_blendDst2");
        private static readonly int BLEND_SRC_ALPHA_2_PROP_ID = Shader.PropertyToID("_blendSrcAlpha2");
        private static readonly int BLEND_DST_ALPHA_2_PROP_ID = Shader.PropertyToID("_blendDstAlpha2");

        public override BlendingShaderType BlendingShader 
        {
            get { return base.BlendingShader; }
            set
            {
                var prevValue = BlendingShader;
                base.BlendingShader = value;
                if (prevValue == value) return;
                InitMaterial();
            }
        }
        
        [SerializeField] private PsdBlendModeType.BlendModeType _layerBlendModeTypeInHierarchy = PsdBlendModeType.BlendModeType.NORMAL;
        protected override PsdBlendModeType.BlendModeType BlendModeTypeInHierarchy 
        {
            get { return _layerBlendModeTypeInHierarchy; }
            set 
            {
                if (_layerBlendModeTypeInHierarchy == value) return;
                _layerBlendModeTypeInHierarchy = value;
                InitMaterial();
            }
        }
       
        protected Material _material = null;

        private void Awake()
        {
            InitMaterial();
        }
        
        public override void Initialize(PsdBlendModeType.BlendModeType blendModeType, float opacity, bool visible, bool visibleInHierarchy, BlendingShaderType blendingShader)
        {
            base.Initialize(blendModeType, opacity, visible, visibleInHierarchy, blendingShader);
            InitMaterial();
        }

        private void SetBlendMode(PsdBlendModeType blendMode)
        {
            _material.SetInt(BLEND_MODE_PROP_ID, blendMode);
        }

        private void SetBlendOperation1(BlendOp blendOp, BlendMode blendSrc, BlendMode blendDst)
        {
            _material.SetInt(BLEND_OP_1_PROP_ID, (int)blendOp);
            _material.SetInt(BLEND_SRC_1_PROP_ID, (int)blendSrc);
            _material.SetInt(BLEND_DST_1_PROP_ID, (int)blendDst);
            _material.SetInt(BLEND_SRC_ALPHA_1_PROP_ID, (int)blendSrc);
            _material.SetInt(BLEND_DST_ALPHA_1_PROP_ID, (int)blendDst);
        }

        private void SetBlendOperation2(BlendOp blendOp, BlendMode blendSrc, BlendMode blendDst)
        {
            _material.SetInt(BLEND_OP_2_PROP_ID, (int)blendOp);
            _material.SetInt(BLEND_SRC_2_PROP_ID, (int)blendSrc);
            _material.SetInt(BLEND_DST_2_PROP_ID, (int)blendDst);
            _material.SetInt(BLEND_SRC_ALPHA_2_PROP_ID, (int)blendSrc);
            _material.SetInt(BLEND_DST_ALPHA_2_PROP_ID, (int)blendDst);
        }

        protected virtual void Update()
        {
            if (_material == null || Proxy == null)
            {
                InitMaterial();
            }
            if (_material == null) return;
            
            if (BlendingShader == BlendingShaderType.DEFAULT)
            {
                Proxy.alpha = OpacityInHierarchy;
                Proxy.enabled = VisibleInHierarchy && Visible;
                return;
            }

            SetBlendOperation2(BlendOp.Add, BlendMode.Zero, BlendMode.One); 
            _material.SetInt(VISIBLE_PROP_ID, (VisibleInHierarchy && Visible) ? 1 : 0);
            _material.SetFloat(OPACITY_PROP_ID, OpacityInHierarchy);
            
            if (!(VisibleInHierarchy && Visible))
            {
                SetBlendOperation1(BlendOp.Add, BlendMode.Zero, BlendMode.One);
                return;
            }

            if (BlendModeTypeInHierarchy == PsdBlendModeType.PASS_THROUGH
                || BlendModeTypeInHierarchy == PsdBlendModeType.NORMAL 
                || BlendModeTypeInHierarchy == PsdBlendModeType.DISSOLVE 
                || (BlendingShader == BlendingShaderType.GRAB_PASS && !PsdBlendModeType.IsSimple(BlendModeTypeInHierarchy)))
            {
                SetBlendOperation1(BlendOp.Add, BlendMode.SrcAlpha, BlendMode.OneMinusSrcAlpha);
            }
            else if (BlendModeTypeInHierarchy == PsdBlendModeType.DARKEN)
            {
                SetBlendOperation1(BlendOp.Min, BlendMode.One, BlendMode.One);
            }
            else if (BlendModeTypeInHierarchy == PsdBlendModeType.MULTIPLY)
            {
                SetBlendOperation1(BlendOp.Add, BlendMode.DstColor, BlendMode.OneMinusSrcAlpha);
            }
            else if (BlendModeTypeInHierarchy == PsdBlendModeType.COLOR_BURN)
            {
                SetBlendOperation1(BlendOp.Add, BlendMode.One, BlendMode.OneMinusSrcColor);
            }
            else if (BlendModeTypeInHierarchy == PsdBlendModeType.LINEAR_BURN)
            {
                SetBlendOperation1(BlendOp.Add, BlendMode.One, BlendMode.One);
            }
            else if (BlendModeTypeInHierarchy == PsdBlendModeType.LIGHTEN)
            {
                SetBlendOperation1(BlendOp.Max, BlendMode.One, BlendMode.One);
            }
            else if (BlendModeTypeInHierarchy == PsdBlendModeType.SCREEN)
            {
                SetBlendOperation1(BlendOp.Add, BlendMode.One, BlendMode.OneMinusSrcColor);
            }
            else if (BlendModeTypeInHierarchy == PsdBlendModeType.COLOR_DODGE)
            {
                SetBlendOperation1(BlendOp.Add, BlendMode.DstColor, BlendMode.Zero);
            }
            else if (BlendModeTypeInHierarchy == PsdBlendModeType.LINEAR_DODGE)
            {
                SetBlendOperation1(BlendOp.Add, BlendMode.SrcAlpha, BlendMode.One);
            }
            else if (BlendModeTypeInHierarchy == PsdBlendModeType.OVERLAY)
            {
                SetBlendOperation1(BlendOp.Add, BlendMode.DstColor, BlendMode.DstColor);
                SetBlendOperation2(BlendOp.Add, BlendMode.DstColor, BlendMode.Zero);
            }
            else if (BlendModeTypeInHierarchy == PsdBlendModeType.SOFT_LIGHT)
            {
                SetBlendOperation1(BlendOp.Add, BlendMode.DstColor, BlendMode.DstColor);
                SetBlendOperation2(BlendOp.Add, BlendMode.DstColor, BlendMode.Zero);
            }
            else if (BlendModeTypeInHierarchy == PsdBlendModeType.HARD_LIGHT)
            {
                SetBlendOperation1(BlendOp.Add, BlendMode.One, BlendMode.One);
                SetBlendOperation2(BlendOp.Add, BlendMode.DstColor, BlendMode.Zero);
            }
            else if (BlendModeTypeInHierarchy == PsdBlendModeType.VIVID_LIGHT)
            {
                SetBlendOperation1(BlendOp.Add, BlendMode.DstColor, BlendMode.Zero);
                SetBlendOperation2(BlendOp.Add, BlendMode.One, BlendMode.OneMinusSrcColor);
            }
            else if (BlendModeTypeInHierarchy == PsdBlendModeType.LINEAR_LIGHT)
            {
                SetBlendOperation1(BlendOp.Add, BlendMode.One, BlendMode.One);
            }
            else if (BlendModeTypeInHierarchy == PsdBlendModeType.EXCLUSION)
            {
                SetBlendOperation1(BlendOp.ReverseSubtract, BlendMode.DstColor, BlendMode.One);
                SetBlendOperation2(BlendOp.Add, BlendMode.SrcAlpha, BlendMode.One);
            }
            else if (BlendModeTypeInHierarchy == PsdBlendModeType.SUBTRACT)
            {
                SetBlendOperation1(BlendOp.ReverseSubtract, BlendMode.SrcAlpha, BlendMode.One);
            }
            else if (BlendModeTypeInHierarchy == PsdBlendModeType.DIVIDE)
            {
                SetBlendOperation1(BlendOp.Add, BlendMode.DstColor, BlendMode.OneMinusSrcAlpha);
            }
            SetBlendMode(BlendModeTypeInHierarchy);
        }

        protected abstract string DefaultShaderName { get; }
        private string ShaderName
        {
            get
            {
                if (BlendingShader != BlendingShaderType.DEFAULT && PsdBlendModeType.IsSimple(BlendModeTypeInHierarchy))
                {
                    return "PluginMaster/PsBlendModeSimple";
                }
                switch (BlendingShader)
                {
                    case BlendingShaderType.GRAB_PASS:
                        return "PluginMaster/PsBlendModeAccurate";
                    case BlendingShaderType.FAST:
                        return "PluginMaster/PsBlendModeFast";
                    case BlendingShaderType.DEFAULT:
                    default: 
                        return DefaultShaderName;
                }
            }
        }

        protected abstract class ImageComponentProxy
        {
            public abstract float alpha { get; set; }
            public abstract bool enabled { get; set; }
            public abstract Material material { get; set; }
        }

        protected ImageComponentProxy Proxy { get; set; }

        protected override float OpacityInHierarchy
        {
            get { return base.OpacityInHierarchy; }
            set
            {
                base.OpacityInHierarchy = value;
                if (BlendingShader == BlendingShaderType.DEFAULT)
                {
                    if (Proxy == null) InitMaterial();
                    Proxy.alpha = value;
                }
            }
        }

        protected override bool VisibleInHierarchy
        {
            get { return base.VisibleInHierarchy; }
            set
            {
                base.VisibleInHierarchy = value;
                if (BlendingShader == BlendingShaderType.DEFAULT)
                {
                    if (Proxy == null) InitMaterial();
                    Proxy.enabled = value;
                }
            }
        }

        public override bool Visible
        {
            get { return base.Visible; }
            set
            {
                var prevValue = Visible;
                base.Visible = value;
                if (Visible == prevValue) return;
                if (BlendingShader == BlendingShaderType.DEFAULT)
                {
                    if (Proxy == null) InitMaterial();
                    Proxy.enabled = VisibleInHierarchy ? value : false;
                }
            }
        }

        protected override void InitMaterial()
        {
            _material = Proxy.material = new Material(Shader.Find(ShaderName)) { hideFlags = HideFlags.HideAndDontSave };
            _material.DisableKeyword("_ALPHATEST_ON");
            _material.EnableKeyword("_ALPHABLEND_ON");
            _material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        }
    }
}