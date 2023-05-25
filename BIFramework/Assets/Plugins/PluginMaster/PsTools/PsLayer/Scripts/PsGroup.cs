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

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
namespace PluginMaster
{
    [ExecuteInEditMode]
    public class PsGroup : MonoBehaviour
    {
        public enum BlendingShaderType { DEFAULT, FAST, GRAB_PASS }
        [SerializeField] private bool _thereAreUiLayersInChildren = false;
        public bool thereAreUiLayersInChildren { get { return _thereAreUiLayersInChildren; } }

        [SerializeField] private Canvas _canvas = null;
        public Canvas canvas { get { return _canvas; } }
        
        public static string GetShaderTypeName(BlendingShaderType type)
        {
            switch (type)
            {
                case BlendingShaderType.GRAB_PASS: return "Accurate";
                case BlendingShaderType.FAST: return "Fast";
                default:
                case BlendingShaderType.DEFAULT: return "Default";
            }
        }

        [SerializeField] protected BlendingShaderType _blendingShader = BlendingShaderType.DEFAULT;
        public virtual BlendingShaderType BlendingShader 
        {
            get { return _blendingShader; }
            set
            {
                if (this is PsLayerImage)
                {
                    var layers = GetComponentsInChildren<PsLayerImage>();
                    var fastShaderInChildren = value == BlendingShaderType.FAST;
                    if (!fastShaderInChildren)
                    {
                        foreach (var layer in layers)
                        {
                            if (layer == this) continue;
                            if (layer.BlendingShader == PsGroup.BlendingShaderType.FAST)
                            {
                                fastShaderInChildren = true;
                                break;
                            }
                        }
                    }

                    if (fastShaderInChildren)
                    {
                        _canvas = GetComponentInParent<Canvas>();
                        if (_canvas != null)
                        {
                            _canvas.renderMode = RenderMode.ScreenSpaceCamera;
                            _canvas.sortingOrder = 20000;
                        }
                    }
                }
                if (_blendingShader == value) return;
                var prevShader = _blendingShader;
                _blendingShader = value;
                if (prevShader == BlendingShaderType.GRAB_PASS && ((PsdBlendModeType)BlendModeType).GrabPass
                    || (_blendingShader == BlendingShaderType.DEFAULT && BlendModeType != PsdBlendModeType.BlendModeType.PASS_THROUGH && BlendModeType != PsdBlendModeType.BlendModeType.NORMAL))
                {
                    BlendModeType = PsdBlendModeType.BlendModeType.NORMAL;
                }
            }
        } 

        [SerializeField] private PsdBlendModeType.BlendModeType _groupBlendModeTypeInHierarchy = PsdBlendModeType.BlendModeType.PASS_THROUGH;
        protected virtual PsdBlendModeType.BlendModeType BlendModeTypeInHierarchy 
        { 
            get { return _groupBlendModeTypeInHierarchy; }
            set { _groupBlendModeTypeInHierarchy = value; }
        }

        [SerializeField] private PsdBlendModeType.BlendModeType _blendModeType = PsdBlendModeType.BlendModeType.PASS_THROUGH;
        public virtual PsdBlendModeType.BlendModeType BlendModeType 
        {
            get { return _blendModeType; }
            set
            {
                if (_blendModeType == value) return;
                _blendModeType = value;
                var inheritedBlending = InheritedBlending;
                BlendModeTypeInHierarchy = inheritedBlending == PsdBlendModeType.BlendModeType.PASS_THROUGH ? _blendModeType : inheritedBlending;
                
                var children = GetComponentsInChildren<PsGroup>();
                foreach (var child in children)
                {
                    if (child == this) continue;

                    child.BlendModeTypeInHierarchy = child.InheritedBlending == PsdBlendModeType.BlendModeType.PASS_THROUGH 
                        ? child.BlendModeType : BlendModeTypeInHierarchy;
                }
                InitMaterial();
            }
        }

        private PsdBlendModeType.BlendModeType InheritedBlending
        {
            get
            {
                var result = PsdBlendModeType.BlendModeType.PASS_THROUGH;
                var parentTransform = transform.parent;
                do
                {
                    if (parentTransform == null) break;
                    var psParent = parentTransform.GetComponent<PsGroup>();
                    if (psParent == null) break;
                    if (psParent.BlendModeType != PsdBlendModeType.BlendModeType.PASS_THROUGH)
                    {
                        result = psParent.BlendModeType;
                    }
                    parentTransform = parentTransform.parent;
                } while (parentTransform != null);
                return result;
            }
        }

        protected virtual void InitMaterial(){ }

        [SerializeField] protected float _opacityInHierarchy = 1f;
        protected virtual float OpacityInHierarchy 
        {
            get { return _opacityInHierarchy; }
            set { _opacityInHierarchy = value; }
        }

        [SerializeField] protected float _opacity = 1f;
        public virtual float Opacity 
        {
            get { return _opacity; }
            set
            {
                if (_opacity == value) return;
                _opacity = value;
                OpacityInHierarchy = Opacity;
                if (transform.parent != null)
                {
                    var psParent = transform.parent.GetComponent<PsGroup>();
                    if(psParent != null)
                    {
                        OpacityInHierarchy = Opacity * psParent.OpacityInHierarchy;
                    }
                }
                var children = GetComponentsInChildren<PsGroup>();
                foreach (var child in children)
                {
                    if (child == this) continue;
                    child.OpacityInHierarchy = child.Opacity;
                    PsGroup childParent = child.transform.parent.GetComponent<PsGroup>();
                    while (childParent != null)
                    {
                        child.OpacityInHierarchy *= childParent.Opacity;
                        childParent = childParent.transform.parent == null ? null : childParent.transform.parent.GetComponent<PsGroup>();
                    }
                }
            }
        }

        [SerializeField] private bool _visibleInHierarchy = true;
        protected virtual bool VisibleInHierarchy 
        {
            get { return _visibleInHierarchy; }
            set { _visibleInHierarchy = value; }
        }
        [SerializeField] private bool _visible = true;
        public virtual bool Visible 
        {
            get { return _visible; }
            set
            {
                if (_visible == value) return;
                _visible = value;

                if (!VisibleInHierarchy) return;
                var children = GetComponentsInChildren<PsGroup>(true);
                foreach (var child in children)
                {
                    if (child == this) continue;
                    child.VisibleInHierarchy = _visible;
                }
            }
        }

        public virtual void Initialize(PsdBlendModeType.BlendModeType blendModeType, float opacity, bool visible, bool visibleInHierarchy, BlendingShaderType blendingShader = BlendingShaderType.DEFAULT)
        {
            BlendingShader = blendingShader;
            BlendModeType = blendModeType;
            Opacity = opacity;
            _visible = visible;
            VisibleInHierarchy = visibleInHierarchy;
        }

        public void UpdateUiLayersInChildren()
        {
            var layers = GetComponentsInChildren<PsLayerImage>(false);
            _thereAreUiLayersInChildren = layers.Length > 0;
            if (thereAreUiLayersInChildren)
            {
                var parentGroups = GetComponentsInParent<PsGroup>();
                foreach (var parentGroup in parentGroups)
                {
                    if (parentGroup == this) continue;
                    parentGroup.UpdateUiLayersInChildren();
                }
            }
        }
        private void Start()
        {
            _canvas = GetComponentInParent<Canvas>();
            UpdateUiLayersInChildren();
        }

        private void OnDestroy()
        {
            UpdateUiLayersInChildren();
        }

        private void OnEnable()
        {
            _canvas = GetComponentInParent<Canvas>();
            UpdateUiLayersInChildren();
        }

        private void OnDisable()
        {
            UpdateUiLayersInChildren();
        }

        private void OnTransformParentChanged()
        {
            if (BlendingShader == BlendingShaderType.FAST && this is PsLayerImage)
            {
                _canvas = GetComponentInParent<Canvas>();
                if (_canvas != null)
                {
                    _canvas.renderMode = RenderMode.ScreenSpaceCamera;
                    _canvas.sortingOrder = 20000;
                }
            }
            UpdateUiLayersInChildren();
        }
    }
}
