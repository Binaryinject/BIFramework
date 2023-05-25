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

namespace PluginMaster
{
    [ExecuteInEditMode]
    [RequireComponent(typeof(SpriteRenderer))]
    public class PsLayerSprite : PsLayer
    {
        protected override string DefaultShaderName { get { return "Sprites/Default"; } }

        private class RendererProxy : ImageComponentProxy
        {
            private SpriteRenderer _renderer = null;
            public RendererProxy(SpriteRenderer renderer)
            {
                _renderer = renderer;
            }
            public override float alpha
            {
                get { return _renderer.color.a; }
                set
                {
                    if (_renderer.color.a == value) return;
                    var color = _renderer.color;
                    color.a = value;
                    _renderer.color = color;
                }
            }
            public override bool enabled
            {
                get { return _renderer.enabled; }
                set { _renderer.enabled = value; }
            }
            public override Material material
            {
                get { return _renderer.material; }
                set { _renderer.material = value; }
            }
        }

        protected override void InitMaterial()
        {
            var renderer = GetComponent<SpriteRenderer>();
            if (renderer == null)
            {
                renderer = gameObject.AddComponent<SpriteRenderer>();
            }
            Proxy = new RendererProxy(renderer);
            base.InitMaterial();
        }
    }
}