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
using UnityEngine.UI;

namespace PluginMaster
{
    [ExecuteInEditMode]
    [RequireComponent(typeof(Image))]
    public class PsLayerImage : PsLayer
    {
        protected override string DefaultShaderName => "UI/Default";
        private class ImageProxy : ImageComponentProxy
        {
            private Image _image = null;
            public ImageProxy(Image image)
            {
                _image = image;
            }
            public override float alpha
            {
                get { return _image.color.a; }
                set
                {
                    if (_image.color.a == value) return;
                    var color = _image.color;
                    color.a = value;
                    _image.color = color;
                }
            }
            public override bool enabled
            {
                get { return _image.enabled; }
                set { _image.enabled = value; }
            }
            public override Material material
            {
                get { return _image.material; }
                set { _image.material = value; }
            }
        }

        protected override void InitMaterial()
        {
            var image = GetComponent<Image>();
            if (image == null)
            {
                image = gameObject.AddComponent<Image>();
            }
            Proxy = new ImageProxy(image);
            base.InitMaterial();
        }
    }
}